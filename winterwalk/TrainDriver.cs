// Winter Walk: the Winter Isle's mountain railway.
//
// The island's pack carries the line (WinterIsleRail in the Unity project): double track from the Plaza Halt twice
// round the mountain and up to the Summit, single at the two ends, and the trains ("Trains/Train t Car c": a
// locomotive and two coaches each). Its build writes the line beside this plugin as WinterIsle.railline: per point
// of the line (every 4 m) the up track's middle, the down track's and the kind of ground (0 open, 1 tunnel,
// 2 trestle); the stations; the tunnel lamps.
//
// The trains shuttle: up on the right-hand track, a turn at the Summit, down the other, a turn at the Plaza Halt.
// Each is a rack railway's train, the locomotive always on the downhill end, pushing up and leading down. Between
// stations they run at nine metres a second (three times the game's own train), easing in and out of each, and they
// stand five minutes at every station. Every train keeps the same timetable a fixed share of the round apart, from the
// network clock (Mirror's NetworkTime), so every player sees the same train in the same place and nothing is sent.
//
// The trains run once the train tower's five plinths ("Train Plinth 1".."5", The Tree's pack) all hold a gourd;
// until then they stand at the stations. The plinths are read every second, never counted from events.
//
// It also tells the mine's driver when this player's camera is in a railway tunnel, so the snowfall stops there.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WinterWalk
{
    public class TrainDriver : MonoBehaviour
    {
        public TrainDriver(IntPtr ptr) : base(ptr) { }

        public const float LineSpeed = 9f;   // metres a second between stations (the game's own train: 3)
        const float StopSpeed = 0.8f;        // ...creeping the last metres into a station
        const float Ease = 110f;             // metres over which a train slows for a station or picks up after it
        public const float Dwell = 300f;     // seconds standing at each station
        const float CarLength = 10.5f;       // car middle to car middle
        const float Bogies = 3.2f;           // each bogie from its car's middle

        public static bool ForceOpen;        // for testing (set?path=WinterWalk.TrainDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the trains are running
        public static bool InTunnel;         // read-only: whether this player's camera is in a railway tunnel
        public static float TimeScale = 1f;  // for testing: how many seconds of timetable pass in one of the clock's
        public static string State = "not loaded";
        public static string Next = "";      // read-only: where the first train is and what it does next

        readonly List<Transform[]> trains = new List<Transform[]>();
        readonly List<Vector3> up = new List<Vector3>(), down = new List<Vector3>();
        readonly List<int> kinds = new List<int>();
        readonly List<float> along = new List<float>();          // metres along the line to each point
        readonly List<int> stops = new List<int>();
        readonly List<string> stopNames = new List<string>();
        readonly List<Vector3> lamps = new List<Vector3>();
        // the timetable: the train's middle, as metres along the line, against time; with which way it is going
        readonly List<float> tableTime = new List<float>(), tableAt = new List<float>();
        readonly List<int> tableDir = new List<int>(), tableStop = new List<int>();
        readonly List<float> dwellStarts = new List<float>();
        float period;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck, nextTunnelCheck;
        GameObject root;
        string file;

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        bool Find()
        {
            root = GameObject.Find("Trains");
            if (root == null) return false;
            if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.railline");
            if (!File.Exists(file)) { State = "no WinterIsle.railline beside the plugin"; root = null; return false; }
            up.Clear(); down.Clear(); kinds.Clear(); stops.Clear(); stopNames.Clear(); lamps.Clear(); along.Clear();
            foreach (string text in File.ReadAllLines(file))
            {
                if (text.Length == 0 || text[0] == '#') continue;
                string[] p = text.Split(' ');
                if (p[0] == "P" && p.Length >= 8)
                {
                    up.Add(new Vector3(P(p[1]), P(p[2]), P(p[3])));
                    down.Add(new Vector3(P(p[4]), P(p[5]), P(p[6])));
                    kinds.Add(int.Parse(p[7], CultureInfo.InvariantCulture));
                }
                else if (p[0] == "S" && p.Length >= 2)
                {
                    stops.Add(int.Parse(p[1], CultureInfo.InvariantCulture));
                    stopNames.Add(p.Length > 2 ? string.Join(" ", p, 2, p.Length - 2) : "station " + stops.Count);
                }
                else if (p[0] == "L" && p.Length >= 4) lamps.Add(new Vector3(P(p[1]), P(p[2]), P(p[3])));
            }
            trains.Clear();
            for (int t = 0; ; t++)
            {
                var cars = new List<Transform>();
                for (int c = 0; ; c++)
                {
                    Transform car = root.transform.Find("Train " + t + " Car " + c);
                    if (car == null) break;
                    cars.Add(car);
                }
                if (cars.Count == 0) break;
                trains.Add(cars.ToArray());
            }
            if (up.Count < 3 || stops.Count < 2 || trains.Count == 0) { State = "the railway's line or trains are missing"; root = null; return false; }
            along.Add(0f);
            for (int i = 1; i < up.Count; i++) along.Add(along[i - 1] + ((up[i] + down[i]) - (up[i - 1] + down[i - 1])).magnitude * 0.5f);
            BuildTable();
            Plugin.L.LogInfo("Winter Walk: railway found: " + along[along.Count - 1].ToString("0") + " m of line, " + stops.Count + " stations, " + trains.Count + " trains, a round trip of " + (period / 60f).ToString("0") + " min");
            return true;
        }

        // The round as a table: up from the first station to the last, standing at each, then down again
        // (plain methods and a field for the running time, no local functions: the game's class injection cannot
        // take the methods those compile to)
        float tableClock;
        void TableAdd(float at, int dir, int stop) { tableTime.Add(tableClock); tableAt.Add(at); tableDir.Add(dir); tableStop.Add(stop); }
        void TableRun(int fromStop, int toStop)
        {
            int dir = toStop > fromStop ? 1 : -1;
            float a = along[stops[fromStop]], b = along[stops[toStop]], length = Mathf.Abs(b - a);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 4f));
            for (int k = 1; k <= steps; k++)
            {
                float s0 = length * (k - 1) / steps, s1 = length * k / steps, mid = (s0 + s1) * 0.5f;
                float toStation = Mathf.Min(mid, length - mid);
                tableClock += (s1 - s0) / Mathf.Lerp(StopSpeed, LineSpeed, Smooth(toStation / Ease));
                TableAdd(a + dir * s1, dir, k == steps ? toStop : -1);
            }
        }
        void TableStand(int stop, int dir) { dwellStarts.Add(tableClock); tableClock += Dwell; TableAdd(along[stops[stop]], dir, stop); }
        void BuildTable()
        {
            tableTime.Clear(); tableAt.Clear(); tableDir.Clear(); tableStop.Clear(); dwellStarts.Clear();
            tableClock = 0f;
            TableAdd(along[stops[0]], 1, 0);
            TableStand(0, 1);
            for (int s = 0; s + 1 < stops.Count; s++) { TableRun(s, s + 1); if (s + 1 < stops.Count - 1) TableStand(s + 1, 1); }
            TableStand(stops.Count - 1, -1);
            for (int s = stops.Count - 1; s > 0; s--) { TableRun(s, s - 1); if (s - 1 > 0) TableStand(s - 1, -1); }
            period = tableClock;
        }

        // Where a train's middle is at time t of the round, which way it is going, and the station it stands at (or -1)
        float MiddleAt(float t, out int dir, out int standing)
        {
            t = Mathf.Repeat(t, period);
            int lo = 0, hi = tableTime.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (tableTime[m] <= t) lo = m; else hi = m; }
            dir = tableDir[hi];
            standing = tableAt[hi] == tableAt[lo] ? tableStop[hi] : -1;
            float f = (t - tableTime[lo]) / Mathf.Max(tableTime[hi] - tableTime[lo], 1e-5f);
            return Mathf.Lerp(tableAt[lo], tableAt[hi], f);
        }

        // The point 'at' metres along the line, on the up (dir 1) or down (dir -1) track
        Vector3 TrackAt(float at, int dir)
        {
            at = Mathf.Clamp(at, 0f, along[along.Count - 1]);
            int lo = 0, hi = along.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (along[m] <= at) lo = m; else hi = m; }
            float f = (at - along[lo]) / Mathf.Max(along[hi] - along[lo], 1e-5f);
            List<Vector3> track = dir > 0 ? up : down;
            return Vector3.Lerp(track[lo], track[hi], f);
        }

        void Place(Transform[] cars, float middle, int dir)
        {
            // the locomotive (car 0) on the downhill end; the cars face uphill but the locomotive faces downhill
            for (int c = 0; c < cars.Length; c++)
            {
                if (cars[c] == null) { root = null; return; }
                float at = middle + (c - (cars.Length - 1) * 0.5f) * CarLength;
                Vector3 back = TrackAt(at - Bogies, dir), front = TrackAt(at + Bogies, dir);
                Vector3 heading = front - back;
                cars[c].position = (back + front) * 0.5f;
                if (heading.sqrMagnitude > 1e-6f) cars[c].rotation = Quaternion.LookRotation(c == 0 ? -heading.normalized : heading.normalized, Vector3.up);
            }
        }

        bool TowerComplete()
        {
            if (Time.unscaledTime < nextPlinthCheck) return towerComplete;
            nextPlinthCheck = Time.unscaledTime + 1f;
            towerComplete = CountGourds();
            return towerComplete;
        }
        bool towerComplete;

        bool CountGourds()
        {
            if (plinths.Count < 5)
            {
                plinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Train Plinth " + i);
                    if (p == null) continue;
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>();
                    if (plinth != null) plinths.Add(plinth);
                }
            }
            if (plinths.Count < 5) { State = "waiting: the train tower shows " + plinths.Count + " of 5 plinths (the towers show one plinth per puzzle loaded, The Tree's, the mine's and the airport's first)"; return false; }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths)
                if (plinth != null && plinth.HasGourd) filled++;
            State = filled >= 5 ? "open" : "waiting: " + filled + " of 5 gourds on the train tower";
            return filled >= 5;
        }

        void CheckTunnel()
        {
            Camera cam = Camera.main;
            bool inside = false;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                for (int i = 0; i < up.Count && !inside; i += 2)
                {
                    if (kinds[i] != 1) continue;
                    Vector3 mid = (up[i] + down[i]) * 0.5f, d = eye - mid;
                    inside = d.y > -1f && d.y < 7f && d.x * d.x + d.z * d.z < 6f * 6f;
                }
            }
            InTunnel = inside;
        }

        void Update()
        {
            try
            {
                if (root == null)
                {
                    InTunnel = false;
                    if (Time.unscaledTime < nextLook) return;
                    nextLook = Time.unscaledTime + 2f;
                    plinths.Clear();
                    if (!Find()) return;
                }
                bool complete = TowerComplete();
                Open = ForceOpen || complete;
                if (ForceOpen) State = "open (forced open for testing)";
                double now = Mirror.NetworkTime.time * TimeScale;
                for (int t = 0; t < trains.Count; t++)
                {
                    // closed: each train stands at a station of its own; open: the timetable, a share of the round apart
                    float time = Open ? (float)((now + (double)period * t / trains.Count) % period) : dwellStarts[(t * dwellStarts.Count / trains.Count) % dwellStarts.Count] + 1f;
                    float middle = MiddleAt(time, out int dir, out int standing);
                    Place(trains[t], middle, dir);
                    if (root == null) return;
                    if (t == 0)
                    {
                        if (standing >= 0) Next = "train 0 at " + stopNames[standing] + " (" + (dir > 0 ? "up" : "down") + ")";
                        else Next = "train 0 running " + (dir > 0 ? "up" : "down") + " at " + middle.ToString("0") + " m";
                    }
                }
                if (Time.unscaledTime >= nextTunnelCheck)
                {
                    nextTunnelCheck = Time.unscaledTime + 0.25f;
                    CheckTunnel();
                }
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: railway stopped: " + e.GetType().Name + ": " + e.Message);
                root = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
