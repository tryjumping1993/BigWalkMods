// Winter Walk: the Winter Isle's mine railway.
//
// The island's pack carries the mine (WinterIsleMine in the Unity project): the hall, the low tunnels, the track,
// the mantrips ("Mine Trips/Mantrip 0"..) and the gates across every tunnel mouth ("Mine Gates"). Its build also
// writes the line the mantrips follow beside this plugin, as WinterIsle.mineline: one closed round, a point every
// two metres ("x y z kind stop": kind 0 in the open at a station, 1 in a tunnel, 2 in the hall; stop 1 where a
// mantrip halts).
//
// This moves the mantrips round the line. As with the gondola, each one's place comes from the network clock
// (Mirror's NetworkTime), so every player sees the same mantrip in the same place and nothing is sent. They run
// fast in the tunnels, slow down for a stop and wait there a while.
//
// The mine opens when the mine tower's five plinths ("Mine Plinth 1".."5") all hold a gourd: the gates go and
// the mantrips start. The plinths are read every second, never counted from events (a save puts gourds back
// without firing them).
//
// It also stops the island's snowfall for a player whose camera is underground.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WinterWalk
{
    public class MineDriver : MonoBehaviour
    {
        public MineDriver(IntPtr ptr) : base(ptr) { }

        const float LineSpeed = 20f;         // metres a second in the tunnels
        const float StopSpeed = 1.4f;        // ...and creeping into a stop
        const float Ease = 34f;              // metres over which a mantrip slows for a stop or picks up after it
        const float Dwell = 7f;              // seconds waiting at a stop

        public static bool ForceOpen;        // for testing (the companion: set?path=WinterWalk.MineDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the mine is open
        public static bool Underground;      // read-only: whether this player's camera is in the mine
        public static string State = "not loaded";

        readonly List<Transform> trips = new List<Transform>();
        readonly List<Vector3> points = new List<Vector3>();      // the line as built, for telling who is underground
        readonly List<int> kinds = new List<int>();
        readonly List<Vector3> loop = new List<Vector3>();        // the line with its waits, against time
        readonly List<float> loopTime = new List<float>();
        float period;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck, nextUnderCheck;
        GameObject tripRoot, gates, snowfall;
        string file;

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        bool Find()
        {
            tripRoot = GameObject.Find("Mine Trips");
            gates = GameObject.Find("Mine Gates");
            snowfall = GameObject.Find("Snowfall");
            if (tripRoot == null) return false;
            if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.mineline");
            if (!File.Exists(file)) { State = "no WinterIsle.mineline beside the plugin"; tripRoot = null; return false; }
            points.Clear(); kinds.Clear();
            var stops = new List<int>();
            foreach (string text in File.ReadAllLines(file))
            {
                if (text.Length == 0 || text[0] == '#') continue;
                string[] parts = text.Split(' ');
                if (parts.Length < 5) continue;
                if (parts[4] == "1") stops.Add(points.Count);
                points.Add(new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture)));
                kinds.Add(int.Parse(parts[3], CultureInfo.InvariantCulture));
            }
            trips.Clear();
            for (int i = 0; ; i++)
            {
                Transform c = tripRoot.transform.Find("Mantrip " + i);
                if (c == null) break;
                trips.Add(c);
            }
            if (points.Count < 3 || trips.Count == 0) { tripRoot = null; return false; }
            BuildLoop(stops);
            Plugin.L.LogInfo("Winter Walk: mine found: " + points.Count + " track points, " + stops.Count + " stops, " + trips.Count + " mantrips, a round trip of " + period.ToString("0") + " s");
            return true;
        }

        void BuildLoop(List<int> stops)
        {
            int n = points.Count;
            var along = new float[n + 1];
            for (int i = 0; i < n; i++) along[i + 1] = along[i] + (points[(i + 1) % n] - points[i]).magnitude;
            float total = along[n];
            // the time to each point: slow near a stop, a wait at it, fast between
            loop.Clear(); loopTime.Clear();
            float time = 0f;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = points[i % n];
                if (i > 0)
                {
                    float mid = (along[i] + along[i - 1]) * 0.5f, toStop = float.MaxValue;
                    foreach (int s in stops)
                    {
                        float d = Mathf.Abs(mid - along[s]);
                        toStop = Mathf.Min(toStop, Mathf.Min(d, total - d));
                    }
                    time += (along[i] - along[i - 1]) / Mathf.Lerp(StopSpeed, LineSpeed, Smooth(toStop / Ease));
                }
                loop.Add(p); loopTime.Add(time);
                if (i < n && stops.Contains(i)) { time += Dwell; loop.Add(p); loopTime.Add(time); }
            }
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
            if (plinths.Count < 5) { State = "waiting: the mine tower shows " + plinths.Count + " of 5 plinths (the tower shows one plinth per puzzle loaded, The Tree's five first, so the mine needs ten puzzles)"; return false; }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths)
                if (plinth != null && plinth.HasGourd) filled++;
            State = filled >= 5 ? "open" : "waiting: " + filled + " of 5 gourds on the mine tower";
            return filled >= 5;
        }

        // Whether the camera is in a tunnel or the hall: near a point of the line that is roofed
        void CheckUnderground()
        {
            Camera cam = Camera.main;
            bool under = false;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                for (int i = 0; i < points.Count && !under; i++)
                {
                    if (kinds[i] == 0) continue;
                    Vector3 d = eye - points[i];
                    if (kinds[i] == 2) under = d.y > -2f && d.y < 14f && d.x * d.x + d.z * d.z < 30f * 30f;
                    else under = d.y > -1.5f && d.y < 2.6f && d.x * d.x + d.z * d.z < 3f * 3f;
                }
            }
            Underground = under;
            if (snowfall != null && snowfall.activeSelf == under) snowfall.SetActive(!under);
        }

        void Update()
        {
            try
            {
                if (tripRoot == null)
                {
                    if (Underground && snowfall != null) snowfall.SetActive(true);
                    Underground = false;
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
                    if (trips[i] == null) { tripRoot = null; return; }
                    float t = (float)((now + (double)period * i / trips.Count) % period);
                    Vector3 at = At(t, out Vector3 heading);
                    trips[i].position = at;
                    if (heading.sqrMagnitude > 1e-8f) trips[i].rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);      // nose up or down on the ramps
                }
                if (Time.unscaledTime >= nextUnderCheck)
                {
                    nextUnderCheck = Time.unscaledTime + 0.25f;
                    CheckUnderground();
                }
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: mine stopped: " + e.GetType().Name + ": " + e.Message);
                tripRoot = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
