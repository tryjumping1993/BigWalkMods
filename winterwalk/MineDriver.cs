// Winter Walk: the Winter Isle's mine railway.
//
// The island's pack carries the mine (WinterIsleMine in the Unity project): its "Mine Line" of markers (P0..Pn, a
// closed line of track; Stop0.. where the mantrips halt), the mantrips ("Mine Trips/Mantrip 0"..) and the gates
// across the three portals ("Mine Gates"). This moves the mantrips round the line. As with the gondola, each
// one's place comes from the network clock (Mirror's NetworkTime), so every player sees the same mantrip in the
// same place and nothing is sent. They run fast in the tunnels, slow down for a stop and wait there a while.
//
// The mine opens when the mine tower's five plinths ("Mine Plinth 1".."5") all hold a gourd: the gates go and
// the mantrips start. The plinths are read every second, never counted from events (a save puts gourds back
// without firing them).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterWalk
{
    public class MineDriver : MonoBehaviour
    {
        public MineDriver(IntPtr ptr) : base(ptr) { }

        const float LineSpeed = 20f;         // metres a second in the tunnels
        const float StopSpeed = 1.4f;        // ...and creeping into a stop
        const float Ease = 45f;              // metres over which a mantrip slows for a stop or picks up after it
        const float Dwell = 9f;              // seconds waiting at a stop

        public static bool ForceOpen;        // for testing (the companion: set?path=WinterWalk.MineDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the mine is open
        public static string State = "not loaded";

        readonly List<Transform> trips = new List<Transform>();
        readonly List<Vector3> loop = new List<Vector3>();
        readonly List<float> loopTime = new List<float>();
        float period;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck;
        GameObject lineRoot, gates;

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        bool Find()
        {
            lineRoot = GameObject.Find("Mine Line");
            GameObject tripRoot = GameObject.Find("Mine Trips");
            gates = GameObject.Find("Mine Gates");
            if (lineRoot == null || tripRoot == null) { lineRoot = null; return false; }
            var points = new List<Vector3>();
            for (int i = 0; ; i++)
            {
                Transform p = lineRoot.transform.Find("P" + i);
                if (p == null) break;
                points.Add(p.position);
            }
            var stops = new List<Vector3>();
            for (int i = 0; ; i++)
            {
                Transform p = lineRoot.transform.Find("Stop" + i);
                if (p == null) break;
                stops.Add(p.position);
            }
            trips.Clear();
            for (int i = 0; ; i++)
            {
                Transform c = tripRoot.transform.Find("Mantrip " + i);
                if (c == null) break;
                trips.Add(c);
            }
            if (points.Count < 3 || trips.Count == 0) { lineRoot = null; return false; }
            BuildLoop(points, stops);
            Plugin.L.LogInfo("Winter Walk: mine found: " + points.Count + " track points, " + stops.Count + " stops, " + trips.Count + " mantrips, a round trip of " + period.ToString("0") + " s");
            return true;
        }

        void BuildLoop(List<Vector3> points, List<Vector3> stops)
        {
            // the closed line in steps of a metre or less
            loop.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 from = points[i], to = points[(i + 1) % points.Count];
                int steps = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / 1f));
                for (int s = 0; s < steps; s++) loop.Add(Vector3.Lerp(from, to, (float)s / steps));
            }
            int n = loop.Count;
            var along = new float[n + 1];
            for (int i = 0; i < n; i++) along[i + 1] = along[i] + (loop[(i + 1) % n] - loop[i]).magnitude;
            float total = along[n];
            // each stop's nearest point on the line
            var stopAt = new List<int>();
            foreach (Vector3 stop in stops)
            {
                int best = 0; float bestD = float.MaxValue;
                for (int i = 0; i < n; i++) { float d = (loop[i] - stop).sqrMagnitude; if (d < bestD) { bestD = d; best = i; } }
                stopAt.Add(best);
            }
            // the time to each point: slow near a stop, a wait at it, fast between
            var timed = new List<Vector3>();
            loopTime.Clear();
            float time = 0f;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = loop[i % n];
                if (i > 0)
                {
                    float mid = (along[i] + along[i - 1]) * 0.5f, toStop = float.MaxValue;
                    foreach (int s in stopAt)
                    {
                        float d = Mathf.Abs(mid - along[s]);
                        toStop = Mathf.Min(toStop, Mathf.Min(d, total - d));
                    }
                    time += (along[i] - along[i - 1]) / Mathf.Lerp(StopSpeed, LineSpeed, Smooth(toStop / Ease));
                }
                timed.Add(p); loopTime.Add(time);
                if (i < n && stopAt.Contains(i)) { time += Dwell; timed.Add(p); loopTime.Add(time); }
            }
            loop.Clear(); loop.AddRange(timed);
            period = time;
        }

        Vector3 At(float t, out Vector3 heading)
        {
            t = Mathf.Repeat(t, period);
            int lo = 0, hi = loopTime.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (loopTime[m] <= t) lo = m; else hi = m; }
            float f = (t - loopTime[lo]) / Mathf.Max(loopTime[hi] - loopTime[lo], 1e-5f);
            heading = loop[hi] - loop[lo];
            return Vector3.Lerp(loop[lo], loop[hi], f);
        }

        bool TowerComplete()
        {
            if (Time.unscaledTime < nextPlinthCheck) return Open;
            nextPlinthCheck = Time.unscaledTime + 1f;
            if (plinths.Count < 5)
            {
                plinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Mine Plinth " + i);
                    if (p == null) continue;
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>();
                    if (plinth != null) plinths.Add(plinth);
                }
            }
            if (plinths.Count < 5) { State = "waiting: the mine tower's plinths are not loaded (" + plinths.Count + " of 5)"; return false; }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths)
                if (plinth != null && plinth.HasGourd) filled++;
            State = filled >= 5 ? "open" : "waiting: " + filled + " of 5 gourds on the mine tower";
            return filled >= 5;
        }

        void Update()
        {
            try
            {
                if (lineRoot == null)
                {
                    if (Time.unscaledTime < nextLook) return;
                    nextLook = Time.unscaledTime + 2f;
                    trips.Clear();
                    plinths.Clear();
                    if (!Find()) return;
                }
                Open = ForceOpen || TowerComplete();
                if (ForceOpen) State = "open (forced open for testing)";
                if (gates != null && gates.activeSelf == Open) gates.SetActive(!Open);
                // closed: the mantrips stand where the clock's zero puts them
                double now = Open ? Mirror.NetworkTime.time : 0.0;
                for (int i = 0; i < trips.Count; i++)
                {
                    if (trips[i] == null) { lineRoot = null; return; }
                    float t = (float)((now + (double)period * i / trips.Count) % period);
                    Vector3 at = At(t, out Vector3 heading);
                    heading.y = 0f;
                    trips[i].position = at;
                    if (heading.sqrMagnitude > 1e-8f) trips[i].rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
                }
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: mine stopped: " + e.GetType().Name + ": " + e.Message);
                lineRoot = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
