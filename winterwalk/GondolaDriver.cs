// Winter Walk: the Winter Isle's gondola.
//
// The island's pack carries the gondola's stations, pylons and cable, a "Gondola Line" of markers (P0 at the
// bottom station up to Pn at the top), and the cabins ("Gondola Cabin 0".."7"), each with two chairlift seats.
// This moves the cabins round the loop: up one line, round the top bullwheel, down the other, round the bottom
// one. Each cabin's place is worked out from the network clock (Mirror's NetworkTime, the same on every
// player's game), so everyone sees the same cabin in the same place and nothing has to be sent. Cabins creep
// through the stations, where players get in and out, and run fast on the line.
//
// The gondola opens when The Tree's five plinths ("Tree Plinth 1".."5") all hold a gourd. Until then the
// cabins wait in a row in the bottom station. The plinths are read every second, never counted from events: a
// save puts gourds back without firing them.
//
// The same driver runs the ski lift (a second component, set up by Configure): the chairlift from the lodge at the
// pistes' foot up to the bench at the tree line, its line "Ski Lift Line", its chairs "Ski Lift Chair 0".. under
// "Ski Lift Chairs". It opens with the gondola (the pistes are only reached by the gondola anyway).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterWalk
{
    public class GondolaDriver : MonoBehaviour
    {
        public GondolaDriver(IntPtr ptr) : base(ptr) { }

        float LineSpeed = 28f;               // metres a second on the cable (100 km/h): the line climbs 590 m over 1.9 km, about 70 s
        float StationSpeed = 0.7f;           // metres a second through a station
        const float StationLength = 12f;     // the slow stretch at each end
        const float Ease = 45f;              // metres over which a cabin speeds up or slows down
        float Side = 2f;                     // each line runs this far either side of the centre (WinterIslePuzzle.GondolaSide)
        string lineName = "Gondola Line", cabinsName = "Gondola Cabins", cabinPrefix = "Gondola Cabin ";
        bool isLift;

        public static bool ForceOpen;        // for testing (the companion: set?path=WinterWalk.GondolaDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the gondola is running
        public static string State = "not loaded";
        public static string LiftState = "not loaded";      // the ski lift's

        // Makes this one the ski lift: its own line and chairs, a slower cable, open whenever the gondola is
        public void ConfigureLift(string line, string cabins, string prefix, float side, float lineSpeed)
        {
            lineName = line; cabinsName = cabins; cabinPrefix = prefix; Side = side; LineSpeed = lineSpeed; StationSpeed = 0.9f; isLift = true;
        }
        void Say(string state) { if (isLift) LiftState = state; else State = state; }

        readonly List<Transform> cabins = new List<Transform>();
        readonly List<Vector3> loop = new List<Vector3>();      // grip positions round the loop, closed
        readonly List<float> loopTime = new List<float>();      // seconds from the start of the loop to each point
        float period;
        Vector3 bottom, dirFlat;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> treePlinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck;
        GameObject lineRoot;

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        bool Find()
        {
            lineRoot = GameObject.Find(lineName);
            GameObject cabinRoot = GameObject.Find(cabinsName);
            if (lineRoot == null || cabinRoot == null) return false;
            var points = new List<Vector3>();
            for (int i = 0; ; i++)
            {
                Transform p = lineRoot.transform.Find("P" + i);
                if (p == null) break;
                points.Add(p.position);
            }
            if (points.Count < 2) return false;
            cabins.Clear();
            for (int i = 0; ; i++)
            {
                Transform c = cabinRoot.transform.Find(cabinPrefix + i);
                if (c == null) break;
                cabins.Add(c);
            }
            if (cabins.Count == 0) return false;
            BuildLoop(points);
            Plugin.L.LogInfo("Winter Walk: " + (isLift ? "ski lift" : "gondola") + " found: " + points.Count + " cable points, " + cabins.Count + " cabins, a round trip of " + period.ToString("0") + " s");
            return true;
        }

        void BuildLoop(List<Vector3> points)
        {
            Vector3 d = points[points.Count - 1] - points[0];
            dirFlat = new Vector3(d.x, 0f, d.z).normalized;
            var side = new Vector3(dirFlat.z, 0f, -dirFlat.x) * Side;
            bottom = points[0];
            loop.Clear();
            // up the right-hand line, round the top wheel, down the left, round the bottom wheel
            foreach (Vector3 p in points) loop.Add(p + side);
            Vector3 top = points[points.Count - 1];
            for (int k = 1; k < 8; k++)
            {
                float a = Mathf.PI * k / 8f;
                loop.Add(top + side * Mathf.Cos(a) + dirFlat * (Side * Mathf.Sin(a)));
            }
            for (int i = points.Count - 1; i >= 0; i--) loop.Add(points[i] - side);
            for (int k = 1; k < 8; k++)
            {
                float a = Mathf.PI * k / 8f;
                loop.Add(bottom - side * Mathf.Cos(a) - dirFlat * (Side * Mathf.Sin(a)));
            }
            loop.Add(loop[0]);
            // break long spans into short steps, so the speed can change smoothly along them
            var fine = new List<Vector3> { loop[0] };
            for (int i = 1; i < loop.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((loop[i] - loop[i - 1]).magnitude / 1f));
                for (int s = 1; s <= steps; s++) fine.Add(Vector3.Lerp(loop[i - 1], loop[i], (float)s / steps));
            }
            loop.Clear(); loop.AddRange(fine);
            // time along it: slow within a station's length of either end of the line, fast between
            float topFlat = Flat(top);
            loopTime.Clear();
            loopTime.Add(0f);
            float time = 0f;
            for (int i = 1; i < loop.Count; i++)
            {
                Vector3 mid = (loop[i] + loop[i - 1]) * 0.5f;
                float along = Flat(mid);
                float fromEnd = Mathf.Min(along, topFlat - along);
                float speed = Mathf.Lerp(StationSpeed, LineSpeed, Smooth((fromEnd - StationLength * 0.5f) / Ease));
                time += (loop[i] - loop[i - 1]).magnitude / speed;
                loopTime.Add(time);
            }
            period = time;
        }

        // how far along the line (from the bottom station, on the ground plane) a point is
        float Flat(Vector3 p) => Vector3.Dot(new Vector3(p.x - bottom.x, 0f, p.z - bottom.z), dirFlat);

        Vector3 At(float t, out Vector3 heading)
        {
            t = Mathf.Repeat(t, period);
            int lo = 0, hi = loopTime.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (loopTime[m] <= t) lo = m; else hi = m; }
            float f = (t - loopTime[lo]) / Mathf.Max(loopTime[hi] - loopTime[lo], 1e-5f);
            heading = loop[hi] - loop[lo];
            return Vector3.Lerp(loop[lo], loop[hi], f);
        }

        bool TreeComplete()
        {
            if (Time.unscaledTime < nextPlinthCheck) return Open;
            nextPlinthCheck = Time.unscaledTime + 1f;
            if (treePlinths.Count < 5)
            {
                treePlinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Tree Plinth " + i);
                    if (p == null) continue;
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>();
                    if (plinth != null) treePlinths.Add(plinth);
                }
            }
            if (treePlinths.Count < 5) { State = "waiting: The Tree's plinths are not loaded (" + treePlinths.Count + " of 5)"; return false; }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in treePlinths)
                if (plinth != null && plinth.HasGourd) filled++;
            State = filled >= 5 ? "running" : "waiting: " + filled + " of 5 gourds on The Tree";
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
                    cabins.Clear();
                    treePlinths.Clear();
                    if (!Find()) return;
                }
                bool open;
                if (isLift) { open = Open; Say(open ? "running" : "waiting for the gondola"); }
                else
                {
                    Open = open = ForceOpen || TreeComplete();
                    if (ForceOpen) State = "running (forced open for testing)";
                }
                if (!open)
                {
                    // waiting in a row along the bottom station's return line
                    var side = new Vector3(dirFlat.z, 0f, -dirFlat.x) * Side;
                    for (int i = 0; i < cabins.Count; i++)
                    {
                        if (cabins[i] == null) { lineRoot = null; return; }
                        cabins[i].position = bottom - side + dirFlat * (5f - i * 1.75f);
                        cabins[i].rotation = Quaternion.LookRotation(-dirFlat, Vector3.up);
                    }
                    return;
                }
                double now = Mirror.NetworkTime.time;
                for (int i = 0; i < cabins.Count; i++)
                {
                    if (cabins[i] == null) { lineRoot = null; return; }
                    float t = (float)((now + (double)period * i / cabins.Count) % period);
                    Vector3 grip = At(t, out Vector3 heading);
                    heading.y = 0f;
                    cabins[i].position = grip;
                    if (heading.sqrMagnitude > 1e-6f) cabins[i].rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);      // hangs level, faces where it goes
                }
            }
            catch (Exception e)
            {
                Say("stopped: " + e.GetType().Name + ": " + e.Message);
                Plugin.L.LogWarning("Winter Walk: " + (isLift ? "ski lift" : "gondola") + " stopped: " + e.GetType().Name + ": " + e.Message);
                lineRoot = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
