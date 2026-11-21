// Winter Walk: the Winter Isle's railway.
//
// The island's pack carries the lines and the trains ("Trains/Train t Car c"). Its build writes the network beside
// this plugin as WinterIsle.railline:
//   LINE name closed|open single|double     a line: a closed loop (single track, one way round) or an open line
//                                           (double track, single at both ends, trains shuttling)
//   P x y z kind  /  P ux uy uz dx dy dz kind   a point every 4 m: the track's middle (single) or the up and down
//                                           tracks' (double); kind 0 open, 1 tunnel, 2 trestle
//   S index name                            a station (its middle, as a point of that line)
//   TRAIN line                              train t (in order) runs on that line
//   L x y z                                 a tunnel lamp
//
// Every train lives on a "ring" of its line: the loop's own length, or for an open line its operating span up and
// back (up on the right-hand track, a turn at the far terminus, down on the other, a turn at the near one). Each
// train's movement is an ORDER: from time T0 at ring position r0 with speed v0, accelerate to nine metres a second
// (three times the game's train), run, and brake to stop at r1. Positions follow from orders on Mirror's network
// clock, so every PC sees the same trains.
//
// The HOST makes the orders and sends them to the others through the island pack's RailControl script (the SDK's
// synced variables): a train stands five minutes at each station (Dwell), then leaves for the next one; pushing its
// throttle (a button in each locomotive and coach, wired to RailControl.Throttle) sends it on at once; and a train
// always plans to stop short of where the train ahead will stop (Safe), so it never runs into another. The trains
// wait at their stations until the train tower's five plinths ("Train Plinth 1".."5") all hold a gourd.
//
// Locomotives lead round the loop; on an open line the locomotive stays on the downhill end, a rack railway's, pushing
// up and leading down. This also tells the mine's driver when this player's camera is in a railway tunnel, so the
// snowfall stops there.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WinterWalk
{
    public class TrainDriver : MonoBehaviour
    {
        public TrainDriver(IntPtr ptr) : base(ptr) { }

        public const float LineSpeed = 9f;   // metres a second between stations (the game's own train: 3)
        const float Accel = 0.45f, Brake = 0.55f, Emergency = 2.5f;      // metres a second squared
        public const float Dwell = 300f;     // seconds standing at each station
        const float CarLength = 10.5f;       // car middle to car middle
        const float Bogies = 3.2f;           // each bogie from its car's middle
        const float Safe = 70f;              // a train stops at least this far (middle to middle) behind the one ahead

        public static bool ForceOpen;        // for testing (set?path=WinterWalk.TrainDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the trains are running
        public static bool InTunnel;         // read-only: whether this player's camera is in a railway tunnel
        public static int TestThrottle = -1; // for testing: the host pushes this train's throttle
        public static string State = "not loaded";
        public static string Next = "";      // read-only: what train 0 is doing

        sealed class Line
        {
            public string Name; public bool Closed, Double;
            public readonly List<Vector3> A = new List<Vector3>(), B = new List<Vector3>();      // up (or only) track, down track
            public readonly List<int> Kinds = new List<int>();
            public readonly List<int> StopIdx = new List<int>();
            public readonly List<string> StopNames = new List<string>();
            public float[] Along;            // metres along the line to each point (closed: and back to the first)
            public float Length;
            // the ring
            public float Ring, U0, U1;       // ring length; an open line's operating span (its terminal stops' positions)
            public readonly List<float> RingStops = new List<float>();
            public readonly List<string> RingStopNames = new List<string>();
        }
        sealed class Train
        {
            public int Index; public Line Line; public Transform[] Cars;
            // the order
            public double T0; public float R0, V0, R1; public double DwellUntil; public int Phase;      // 0 running, 1 at a station, 2 held back
            public bool ToStation;
            // the plan worked out from it
            public float A1, T1, Vp, T2, Bk, T3, D;
            public double ThrottleAt = -1e9;
        }

        readonly List<Line> lines = new List<Line>();
        readonly List<Train> trains = new List<Train>();
        readonly List<Vector3> lamps = new List<Vector3>();
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck, nextTunnelCheck, nextDecide, nextRepublish;
        bool towerComplete, laidOut, wasOpen;
        GameObject root;
        string file;
        int seenVersion = -1;
        string lastPublished = "";

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- RailControl (the pack's script holding the synced orders), found by reflection ----
        static Type railType;
        static FieldInfo railOrders, railVersion, railThrottles;
        static MethodInfo railPublish;
        static PropertyInfo railPresent;
        static bool BindRail()
        {
            if (railType != null) return true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType("RailControl"); } catch (Exception) { continue; }
                if (t == null) continue;
                railType = t;
                railOrders = t.GetField("Orders", BindingFlags.Public | BindingFlags.Static);
                railVersion = t.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                railThrottles = t.GetField("Throttles", BindingFlags.Public | BindingFlags.Static);
                railPublish = t.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
                railPresent = t.GetProperty("Present", BindingFlags.Public | BindingFlags.Static);
                Plugin.L.LogInfo("Winter Walk: railway found the pack's RailControl in " + asm.GetName().Name);
                return true;
            }
            return false;
        }
        static bool RailPresent => railPresent != null && (bool)railPresent.GetValue(null);

        static bool IsHost { get { try { return Mirror.NetworkServer.active; } catch (Exception) { return false; } } }

        // ---- loading ----
        bool Find()
        {
            root = GameObject.Find("Trains");
            if (root == null) return false;
            if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.railline");
            if (!File.Exists(file)) { State = "no WinterIsle.railline beside the plugin"; root = null; return false; }
            lines.Clear(); lamps.Clear();
            var trainLines = new List<string>();
            Line line = null;
            foreach (string text in File.ReadAllLines(file))
            {
                if (text.Length == 0 || text[0] == '#') continue;
                string[] p = text.Split(' ');
                if (p[0] == "LINE" && p.Length >= 4)
                {
                    line = new Line { Name = p[1], Closed = p[2] == "closed", Double = p[3] == "double" };
                    lines.Add(line);
                }
                else if (p[0] == "P" && line != null)
                {
                    if (p.Length >= 8) { line.A.Add(new Vector3(P(p[1]), P(p[2]), P(p[3]))); line.B.Add(new Vector3(P(p[4]), P(p[5]), P(p[6]))); line.Kinds.Add(int.Parse(p[7], CultureInfo.InvariantCulture)); }
                    else if (p.Length >= 5) { var v = new Vector3(P(p[1]), P(p[2]), P(p[3])); line.A.Add(v); line.B.Add(v); line.Kinds.Add(int.Parse(p[4], CultureInfo.InvariantCulture)); }
                }
                else if (p[0] == "S" && line != null && p.Length >= 2)
                {
                    line.StopIdx.Add(int.Parse(p[1], CultureInfo.InvariantCulture));
                    line.StopNames.Add(p.Length > 2 ? string.Join(" ", p, 2, p.Length - 2) : "station " + line.StopIdx.Count);
                }
                else if (p[0] == "TRAIN" && p.Length >= 2) trainLines.Add(p[1]);
                else if (p[0] == "L" && p.Length >= 4) lamps.Add(new Vector3(P(p[1]), P(p[2]), P(p[3])));
            }
            foreach (Line l in lines) Measure(l);
            trains.Clear();
            for (int t = 0; t < trainLines.Count; t++)
            {
                Line l = null;
                foreach (Line x in lines) if (x.Name == trainLines[t]) l = x;
                var cars = new List<Transform>();
                for (int c = 0; ; c++)
                {
                    Transform car = root.transform.Find("Train " + t + " Car " + c);
                    if (car == null) break;
                    cars.Add(car);
                }
                if (l == null || cars.Count == 0 || l.RingStops.Count == 0) continue;
                trains.Add(new Train { Index = t, Line = l, Cars = cars.ToArray() });
            }
            if (lines.Count == 0 || trains.Count == 0) { State = "the railway's lines or trains are missing"; root = null; return false; }
            laidOut = false;
            seenVersion = -1;
            var sb = new StringBuilder("Winter Walk: railway found:");
            foreach (Line l in lines) sb.Append(" ").Append(l.Name).Append(" (").Append(l.Length.ToString("0")).Append(" m, ").Append(l.StopIdx.Count).Append(" stations, ").Append(TrainsOn(l).Count).Append(" trains)");
            Plugin.L.LogInfo(sb.ToString());
            return true;
        }

        static void Measure(Line l)
        {
            int n = l.A.Count;
            l.Along = new float[n + 1];
            for (int i = 1; i <= n; i++)
            {
                if (i == n && !l.Closed) { l.Along[n] = l.Along[n - 1]; break; }
                Vector3 a = (l.A[i - 1] + l.B[i - 1]) * 0.5f, b = (l.A[i % n] + l.B[i % n]) * 0.5f;
                l.Along[i] = l.Along[i - 1] + (b - a).magnitude;
            }
            l.Length = l.Closed ? l.Along[n] : l.Along[n - 1];
            l.RingStops.Clear(); l.RingStopNames.Clear();
            var us = new List<float>();
            for (int s = 0; s < l.StopIdx.Count; s++) us.Add(l.Along[Mathf.Clamp(l.StopIdx[s], 0, n - 1)]);
            if (l.Closed)
            {
                l.Ring = l.Length;
                for (int s = 0; s < us.Count; s++) { l.RingStops.Add(us[s]); l.RingStopNames.Add(l.StopNames[s]); }
            }
            else
            {
                // the operating span runs between the first and the last station; up, then back down
                l.U0 = us.Count > 0 ? us[0] : 0f; l.U1 = us.Count > 0 ? us[us.Count - 1] : l.Length;
                float span = Mathf.Max(1f, l.U1 - l.U0);
                l.Ring = 2f * span;
                // up: every station (the termini once each); down: the stations between, in reverse
                for (int s = 0; s < us.Count; s++) { l.RingStops.Add(us[s] - l.U0); l.RingStopNames.Add(s == 0 || s == us.Count - 1 ? l.StopNames[s] : l.StopNames[s] + " (up)"); }
                for (int s = us.Count - 2; s >= 1; s--) { l.RingStops.Add(span + (l.U1 - us[s])); l.RingStopNames.Add(l.StopNames[s] + " (down)"); }
            }
        }

        List<Train> TrainsOn(Line l)
        {
            var on = new List<Train>();
            foreach (Train t in trains) if (t.Line == l) on.Add(t);
            return on;
        }
        Train TrainNumber(int index)
        {
            foreach (Train t in trains) if (t.Index == index) return t;
            return null;
        }

        // ---- kinematics: the plan for an order (accelerate, run, brake to a stop d metres on) ----
        static void Plan(Train t)
        {
            float d = Wrap(t.R1 - t.R0, t.Line.Ring);
            if (t.Phase != 0) d = 0f;
            t.D = d;
            float v0 = Mathf.Max(0f, t.V0);
            float stop = v0 * v0 / (2f * Brake);
            if (d <= 0.01f || stop >= d)
            {
                float b = d > 0.05f ? Mathf.Min(v0 * v0 / (2f * d), Emergency) : Emergency;
                t.A1 = 0f; t.T1 = 0f; t.Vp = v0; t.T2 = 0f; t.Bk = Mathf.Max(b, 0.01f); t.T3 = v0 / t.Bk;
                return;
            }
            float dAcc = (LineSpeed * LineSpeed - v0 * v0) / (2f * Accel), dDec = LineSpeed * LineSpeed / (2f * Brake);
            if (v0 <= LineSpeed && dAcc + dDec <= d)
            {
                t.A1 = Accel; t.T1 = (LineSpeed - v0) / Accel; t.Vp = LineSpeed; t.T2 = (d - dAcc - dDec) / LineSpeed; t.Bk = Brake; t.T3 = LineSpeed / Brake;
            }
            else
            {
                float vp = Mathf.Sqrt((2f * Accel * Brake * d + Brake * v0 * v0) / (Accel + Brake));
                vp = Mathf.Max(vp, v0);
                t.A1 = Accel; t.T1 = (vp - v0) / Accel; t.Vp = vp; t.T2 = 0f; t.Bk = Brake; t.T3 = vp / Brake;
            }
        }
        static void Eval(Train t, double now, out float r, out float v)
        {
            float tau = (float)(now - t.T0), dist;
            if (t.Phase != 0 || tau <= 0f) { r = t.R0; v = t.Phase != 0 ? 0f : t.V0; return; }
            float v0 = Mathf.Max(0f, t.V0);
            if (tau < t.T1) { dist = v0 * tau + 0.5f * t.A1 * tau * tau; v = v0 + t.A1 * tau; }
            else
            {
                float d1 = v0 * t.T1 + 0.5f * t.A1 * t.T1 * t.T1; tau -= t.T1;
                if (tau < t.T2) { dist = d1 + t.Vp * tau; v = t.Vp; }
                else
                {
                    float d2 = d1 + t.Vp * t.T2; tau -= t.T2;
                    if (tau < t.T3) { dist = d2 + t.Vp * tau - 0.5f * t.Bk * tau * tau; v = t.Vp - t.Bk * tau; }
                    else { dist = t.D; v = 0f; }
                }
            }
            r = Wrap(t.R0 + Mathf.Min(dist, t.D), t.Line.Ring);
        }
        static bool Arrived(Train t, double now) => t.Phase == 0 && now - t.T0 >= t.T1 + t.T2 + t.T3 - 1e-3;
        static float Wrap(float x, float ring) { x %= ring; return x < 0f ? x + ring : x; }

        // ---- placing the cars ----
        Vector3 TrackAt(Line l, float u, bool down)
        {
            int n = l.A.Count;
            if (l.Closed) u = Wrap(u, l.Length); else u = Mathf.Clamp(u, 0f, l.Length);
            int lo = 0, hi = l.Closed ? n : n - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (l.Along[m] <= u) lo = m; else hi = m; }
            float f = (u - l.Along[lo]) / Mathf.Max(l.Along[hi] - l.Along[lo], 1e-4f);
            List<Vector3> track = down ? l.B : l.A;
            return Vector3.Lerp(track[lo % n], track[hi % n], f);
        }
        void Place(Train t, float r)
        {
            Line l = t.Line;
            int cars = t.Cars.Length;
            for (int c = 0; c < cars; c++)
            {
                if (t.Cars[c] == null) { root = null; return; }
                Vector3 back, front;
                bool reversed;
                if (l.Closed)
                {
                    // the locomotive (car 0) leads round the loop
                    float at = r + ((cars - 1) * 0.5f - c) * CarLength;
                    back = TrackAt(l, at - Bogies, false); front = TrackAt(l, at + Bogies, false);
                    reversed = false;
                }
                else
                {
                    float span = l.Ring * 0.5f;
                    bool down = r > span;
                    float mid = down ? l.U1 - (r - span) : l.U0 + r;
                    float at = mid + (c - (cars - 1) * 0.5f) * CarLength;      // the locomotive downhill (lower u)
                    back = TrackAt(l, at - Bogies, down); front = TrackAt(l, at + Bogies, down);
                    reversed = c == 0;      // it faces downhill
                }
                Vector3 heading = front - back;
                t.Cars[c].position = (back + front) * 0.5f;
                if (heading.sqrMagnitude > 1e-6f) t.Cars[c].rotation = Quaternion.LookRotation(reversed ? -heading.normalized : heading.normalized, Vector3.up);
            }
        }

        // ---- the host's decisions ----
        void LayOut(double now, bool running)
        {
            foreach (Line l in lines)
            {
                List<Train> on = TrainsOn(l);
                for (int k = 0; k < on.Count; k++)
                {
                    Train t = on[k];
                    int stop = (int)((long)k * l.RingStops.Count / on.Count) % l.RingStops.Count;
                    t.R0 = t.R1 = l.RingStops[stop]; t.V0 = 0f; t.T0 = now; t.Phase = 1; t.ToStation = true;
                    t.DwellUntil = running ? now + 20.0 + Dwell * k / Math.Max(1, on.Count) : double.MaxValue;
                    Plan(t);
                }
            }
            laidOut = true;
        }

        float NextStop(Train t, float r)
        {
            Line l = t.Line;
            float best = float.MaxValue, at = r;
            foreach (float s in l.RingStops)
            {
                float d = Wrap(s - r, l.Ring);
                if (d > 2f && d < best) { best = d; at = s; }
            }
            return at;
        }

        // How far ahead along the ring this train may plan to stop: short of where the train ahead will stop
        float Room(Train t, float r, double now)
        {
            float room = float.MaxValue;
            foreach (Train o in trains)
            {
                if (o == t || o.Line != t.Line) continue;
                Eval(o, now, out float ro, out _);
                float stopAt = o.Phase == 0 ? o.R1 : ro;
                // the other train is ahead if its present position is ahead (within half the ring) or it stops ahead
                float gapNow = Wrap(ro - r, t.Line.Ring), gapStop = Wrap(stopAt - r, t.Line.Ring);
                if (gapNow > t.Line.Ring * 0.5f && gapStop > t.Line.Ring * 0.5f) continue;      // behind
                float gap = Mathf.Min(gapNow + Wrap(stopAt - ro, t.Line.Ring), t.Line.Ring);
                room = Mathf.Min(room, gap - Safe);
            }
            return room;
        }

        bool Decide(double now)
        {
            bool changed = false;
            // throttles pushed since last time
            var pushed = new List<int>();
            try
            {
                if (railThrottles != null && railThrottles.GetValue(null) is Queue<int> q)
                    while (q.Count > 0) pushed.Add(q.Dequeue());
            }
            catch (Exception) { }
            if (TestThrottle >= 0) { pushed.Add(TestThrottle); TestThrottle = -1; }
            foreach (int i in pushed) { Train t = TrainNumber(i); if (t != null) t.ThrottleAt = now; }

            if (Open && !wasOpen)      // the tower just filled: everyone sets off in turn
            {
                foreach (Line l in lines)
                {
                    List<Train> on = TrainsOn(l);
                    for (int k = 0; k < on.Count; k++) if (on[k].Phase != 0) on[k].DwellUntil = now + 15.0 + 40.0 * k;
                }
                changed = true;
            }
            if (!Open && wasOpen) { foreach (Train t in trains) if (t.Phase != 0) t.DwellUntil = double.MaxValue; changed = true; }
            wasOpen = Open;

            foreach (Train t in trains)
            {
                Eval(t, now, out float r, out float v);
                if (t.Phase == 0 && Arrived(t, now))
                {
                    t.R0 = t.R1; t.V0 = 0f; t.T0 = now;
                    t.Phase = t.ToStation ? 1 : 2;
                    if (t.ToStation) t.DwellUntil = Open ? now + Dwell : double.MaxValue;
                    Plan(t);
                    r = t.R0; v = 0f;
                    changed = true;
                }
                float room = Room(t, r, now);
                if (t.Phase == 1 || t.Phase == 2)
                {
                    bool go = Open && (t.Phase == 2 || now >= t.DwellUntil || now - t.ThrottleAt < 10.0);
                    if (!go) continue;
                    float next = NextStop(t, r), toNext = Wrap(next - r, t.Line.Ring);
                    if (room >= toNext) Order(t, now, r, 0f, next, true);
                    else if (room > 40f) Order(t, now, r, 0f, Wrap(r + room, t.Line.Ring), false);      // creep up behind it
                    else continue;
                    t.ThrottleAt = -1e9;
                    changed = true;
                }
                else
                {
                    float toTarget = Wrap(t.R1 - r, t.Line.Ring);
                    if (toTarget > room + 0.5f)
                    {
                        // the train ahead is in the way: plan to stop short of it
                        Order(t, now, r, v, Wrap(r + Mathf.Max(0f, room), t.Line.Ring), false);
                        changed = true;
                    }
                    else if (!t.ToStation)
                    {
                        float next = NextStop(t, r), toNext = Wrap(next - r, t.Line.Ring);
                        float can = Mathf.Min(toNext, room);
                        if (can > toTarget + 25f)      // the way has cleared: on to the station, or as far as allowed
                        {
                            Order(t, now, r, v, can >= toNext ? next : Wrap(r + can, t.Line.Ring), can >= toNext);
                            changed = true;
                        }
                    }
                }
            }
            return changed;
        }

        void Order(Train t, double now, float r, float v, float target, bool toStation)
        {
            t.T0 = now; t.R0 = r; t.V0 = v; t.R1 = target; t.ToStation = toStation; t.Phase = 0;
            Plan(t);
        }

        string Encode()
        {
            var sb = new StringBuilder("1");
            foreach (Train t in trains)
                sb.Append(';').Append(t.Index).Append(',').Append(t.Phase).Append(',').Append(F(t.T0)).Append(',').Append(F(t.R0)).Append(',').Append(F(t.V0))
                  .Append(',').Append(F(t.R1)).Append(',').Append(t.DwellUntil >= double.MaxValue * 0.5 ? "-1" : F(t.DwellUntil)).Append(',').Append(t.ToStation ? 1 : 0);
            return sb.ToString();
        }
        void Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split(';');
            if (parts.Length < 2 || parts[0] != "1") return;
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length < 8) continue;
                Train t = TrainNumber(int.Parse(f[0], CultureInfo.InvariantCulture));
                if (t == null) continue;
                t.Phase = int.Parse(f[1], CultureInfo.InvariantCulture);
                t.T0 = double.Parse(f[2], CultureInfo.InvariantCulture);
                t.R0 = P(f[3]); t.V0 = P(f[4]); t.R1 = P(f[5]);
                t.DwellUntil = f[6] == "-1" ? double.MaxValue : double.Parse(f[6], CultureInfo.InvariantCulture);
                t.ToStation = f[7] == "1";
                Plan(t);
            }
            laidOut = true;
        }

        bool TowerComplete()
        {
            if (Time.unscaledTime < nextPlinthCheck) return towerComplete;
            nextPlinthCheck = Time.unscaledTime + 1f;
            if (plinths.Count < 5)
            {
                plinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Train Plinth " + i);
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p != null ? p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>() : null;
                    if (plinth != null) plinths.Add(plinth);
                }
            }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths) if (plinth != null && plinth.HasGourd) filled++;
            towerComplete = plinths.Count == 5 && filled == 5;
            if (!towerComplete) State = plinths.Count < 5 ? "waiting: the train tower shows " + plinths.Count + " of 5 plinths (the towers show one plinth per puzzle loaded, The Tree's, the mine's and the airport's first)" : "waiting: " + filled + " of 5 gourds on the train tower";
            return towerComplete;
        }

        void CheckTunnel()
        {
            Camera cam = Camera.main;
            bool inside = false;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                foreach (Line l in lines)
                {
                    for (int i = 0; i < l.A.Count && !inside; i += 2)
                    {
                        if (l.Kinds[i] != 1) continue;
                        Vector3 mid = (l.A[i] + l.B[i]) * 0.5f, d = eye - mid;
                        inside = d.y > -1f && d.y < 7f && d.x * d.x + d.z * d.z < 6f * 6f;
                    }
                    if (inside) break;
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
                BindRail();
                double now = Mirror.NetworkTime.time;
                bool host = IsHost;
                Open = ForceOpen || TowerComplete();
                if (host)
                {
                    if (!laidOut) { LayOut(now, Open); wasOpen = Open; Publish(true); }
                    if (Time.unscaledTime >= nextDecide)
                    {
                        nextDecide = Time.unscaledTime + 0.2f;
                        if (Decide(now)) Publish(false);
                    }
                    // a reloaded pack starts its synced text empty again: send ours back
                    if (Time.unscaledTime >= nextRepublish) { nextRepublish = Time.unscaledTime + 5f; Publish(false, true); }
                }
                else
                {
                    int version = railVersion != null ? (int)railVersion.GetValue(null) : 0;
                    if (version != seenVersion) { seenVersion = version; Decode(railOrders != null ? (string)railOrders.GetValue(null) : ""); }
                    if (!laidOut) LayOut(now, false);
                }
                if (ForceOpen) State = "open (forced open for testing)";
                else if (Open) State = "open";
                if (!RailPresent) State += " (no RailControl in the loaded packs: this PC runs the trains alone)";
                for (int i = 0; i < trains.Count; i++)
                {
                    Train t = trains[i];
                    Eval(t, now, out float r, out float v);
                    Place(t, r);
                    if (root == null) return;
                    if (i == 0)
                    {
                        int stop = -1;
                        for (int k = 0; k < t.Line.RingStops.Count; k++) if (Wrap(t.Line.RingStops[k] - r, t.Line.Ring) < 3f || Wrap(r - t.Line.RingStops[k], t.Line.Ring) < 3f) stop = k;
                        Next = "train 0 (" + t.Line.Name + ") " + (t.Phase == 1 ? "at " + (stop >= 0 ? t.Line.RingStopNames[stop] : "a station") + (t.DwellUntil < double.MaxValue * 0.5 ? ", leaving in " + Math.Max(0, t.DwellUntil - now).ToString("0") + " s" : ", waiting for the tower") : t.Phase == 2 ? "held back by the train ahead" : "running at " + v.ToString("0.0") + " m/s, " + r.ToString("0") + " m round");
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
                Plugin.L.LogWarning("Winter Walk: railway stopped: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                root = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }

        void Publish(bool force, bool onlyIfLost = false)
        {
            string text = Encode();
            if (onlyIfLost)
            {
                string there = railOrders != null ? (string)railOrders.GetValue(null) : null;
                if (there == text || !RailPresent) return;
            }
            else if (!force && text == lastPublished) return;
            lastPublished = text;
            try { if (railPublish != null) railPublish.Invoke(null, new object[] { text }); } catch (Exception) { }
        }
    }
}
