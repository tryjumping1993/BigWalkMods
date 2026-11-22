// The Winter Isle's railway network (WinterIsleRail), built into the island's pack.
//
// The lines are designed by the route tools (tools/railnet) and kept in unity/Land/WinterIsle.railnet:
//   LINE name closed|open        a line: "loop" is a closed loop round the island that trains run one way round
//                                (clockwise), single track, stopping near the puzzles; "summit" is the mountain line,
//                                open, from Cirque Gate up a long climb round the mountain and a rack spiral inside the
//                                summit's rock to a station under the crest, double track with trains shuttling
//   P x y z                      a point every 4 m (track level: the ballast's top)
//   S index name                 a station's middle
// (Without that file the old single line, unity/Land/WinterIsle.rail, is built as one open line.)
//
// Where a line keeps near the ground the island benches the ground to it (cuttings and embankments, WinterIslePuzzle
// step 3c); where the ground stands well above, it runs in a tunnel with stone portals; where it falls away, on a
// timber trestle, or across the sea on a viaduct. Stations have platforms, shelters, lights and their names.
//
// Winter Walk's TrainDriver runs the trains (nine metres a second, five minutes at each station, a throttle button in
// every car to leave at once, never running into the train ahead) with the host deciding and the island pack's
// RailControl script (unity/Assets/PuzzleScripts) carrying the host's orders to everyone. The network, its stations,
// its trains and the kind of ground under each point are written beside the plugin as WinterIsle.railline.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bwm;
using UnityEngine;

public sealed class RailLine
{
    public string Name;
    public bool Closed, Double;
    public readonly List<Vector3> P = new List<Vector3>();
    public readonly List<int> Stops = new List<int>();
    public readonly List<string> Names = new List<string>();
    public int[] Kind = new int[0];          // 0 on the ground (benched), 1 tunnel, 2 trestle
    public float[] Inside = new float[0];    // in a tunnel: metres from its nearest mouth
    public bool[] Thin = new bool[0];        // in a tunnel under too little ground: the ground is cut away and it is roofed in stone
    public int Trains;
    public int N => P.Count;
    public int W(int i) => Closed ? ((i % N) + N) % N : Mathf.Clamp(i, 0, N - 1);
    public Vector2 Flat(int i) { Vector3 v = P[W(i)]; return new Vector2(v.x, v.z); }
    // how many points apart two indices are (round the loop the short way)
    public int Apart(int a, int b) { int d = Mathf.Abs(a - b); return Closed ? Mathf.Min(d, N - d) : d; }
}

public static class WinterIsleRail
{
    public const float TrackOffset = 2.2f;          // each track's middle from the line, where there are two
    public const float BedHalf = 4.8f;              // half the ballast bed's width
    public const float TunnelHalf = 5.2f, TunnelHigh = 6.4f;
    public const float StationHalf = 11f;           // half the level ground at a station, across the line
    public const int StationReach = 10;             // points either side of a station's middle (4 m each): 80 m of platform
    public const int Turnout = 12;                  // points over which two tracks become one at a terminus
    public const float PlatformTop = 0.5f;          // above the line (the ballast's top is at 0.05; a coach's floor at 0.7)
    public const int Cars = 3;
    public const float CarLength = 10.5f;           // car middle to car middle, along the track
    public const float BenchBelow = 0.4f;           // the benched ground under the line

    public static readonly List<RailLine> Lines = new List<RailLine>();
    // every point of every line, in one list (the island's build walks them all), and every station's place
    public static readonly List<Vector3> Line = new List<Vector3>();
    public static readonly List<Vector2> Stops = new List<Vector2>();
    static readonly List<RailLine> pointLine = new List<RailLine>();
    static readonly List<int> pointIndex = new List<int>();
    static readonly HashSet<long> cutCells = new HashSet<long>();
    static readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
    const float Bucket = 32f;

    static string NetFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Land", "WinterIsle.railnet"));
    static string OldFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Land", "WinterIsle.rail"));
    static string LineFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.railline"));
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static long BucketKey(int bx, int bz) => ((long)bx << 32) ^ (uint)bz;
    static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    public static bool Load()
    {
        Lines.Clear(); Line.Clear(); Stops.Clear(); pointLine.Clear(); pointIndex.Clear(); buckets.Clear();
        bool net = File.Exists(NetFile);
        if (!net && !File.Exists(OldFile)) return false;
        RailLine line = null;
        foreach (string raw in File.ReadAllLines(net ? NetFile : OldFile))
        {
            if (raw.Length == 0 || raw[0] == '#') continue;
            string[] p = raw.Split(' ');
            if (p[0] == "LINE" && p.Length >= 3)
            {
                line = new RailLine { Name = p[1], Closed = p[2] == "closed" };
                line.Double = !line.Closed;
                Lines.Add(line);
            }
            else if (p[0] == "P" && p.Length >= 4)
            {
                if (line == null) { line = new RailLine { Name = "summit", Closed = false, Double = true }; Lines.Add(line); }
                line.P.Add(new Vector3(Num(p[1]), Num(p[2]), Num(p[3])));
            }
            else if (p[0] == "S" && p.Length >= 2 && line != null)
            {
                line.Stops.Add(int.Parse(p[1], CultureInfo.InvariantCulture));
                line.Names.Add(p.Length > 2 ? string.Join(" ", p, 2, p.Length - 2) : "");
            }
        }
        Lines.RemoveAll(l => l.N < 2 * (StationReach + Turnout) + 10 || l.Stops.Count < 2);
        if (Lines.Count == 0) return false;
        foreach (RailLine l in Lines)
        {
            if (!l.Closed)      // the termini's platforms end at the buffers, so their stops stand a platform's half-length in
            {
                l.Stops[0] = Mathf.Max(l.Stops[0], StationReach + 2);
                l.Stops[l.Stops.Count - 1] = Mathf.Min(l.Stops[l.Stops.Count - 1], l.N - 1 - (StationReach + 2));
            }
            for (int s = 0; s < l.Names.Count; s++) if (string.IsNullOrEmpty(l.Names[s])) l.Names[s] = "Station " + (s + 1);
            l.Trains = l.Closed ? Mathf.Clamp(Mathf.RoundToInt(l.N * 4f / 1300f), 3, 8) : (net ? 2 : 8);
            if (l.Kind.Length != l.N) { l.Kind = new int[l.N]; l.Inside = new float[l.N]; }
            for (int i = 0; i < l.N; i++)
            {
                Line.Add(l.P[i]); pointLine.Add(l); pointIndex.Add(i);
                long key = BucketKey(Mathf.FloorToInt(l.P[i].x / Bucket), Mathf.FloorToInt(l.P[i].z / Bucket));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(Line.Count - 1);
            }
            foreach (int s in l.Stops) Stops.Add(l.Flat(s));
        }
        return true;
    }

    public static bool NearStation(RailLine l, int i) { foreach (int s in l.Stops) if (l.Apart(i, s) <= StationReach + 3) return true; return false; }
    public static Vector2 Station(int s) => Stops[s];

    // How far (horizontally) from 'p' the nearest point of the railway is, looking no further than about 'reach'
    public static float Distance(Vector2 p, float reach = Bucket)
    {
        float best = float.MaxValue;
        int r = Mathf.CeilToInt(reach / Bucket), bx = Mathf.FloorToInt(p.x / Bucket), bz = Mathf.FloorToInt(p.y / Bucket);
        for (int oz = -r; oz <= r; oz++)
            for (int ox = -r; ox <= r; ox++)
                if (buckets.TryGetValue(BucketKey(bx + ox, bz + oz), out var list))
                    foreach (int g in list) best = Mathf.Min(best, (new Vector2(Line[g].x, Line[g].z) - p).sqrMagnitude);
        return Mathf.Sqrt(best);
    }

    // A track's middle at point i: side +1 the right-hand (up) track, -1 the other; one track on a loop and at an open
    // line's termini
    public static float Offset(RailLine l, int i, int side)
    {
        if (!l.Double) return 0f;
        float fromStart = (i - (l.Stops[0] + StationReach + 1)) / (float)Turnout, fromEnd = ((l.Stops[l.Stops.Count - 1] - StationReach - 1) - i) / (float)Turnout;
        return side * TrackOffset * Smooth(Mathf.Min(fromStart, fromEnd));
    }

    // Which stretches are on the ground, in a tunnel or on a trestle, from the ground as it stands ('ground' is NaN off
    // the island). With some hysteresis, no stretch shorter than 28 m, and never a tunnel or trestle at a station
    public static void Classify(Func<Vector2, float> ground)
    {
        cutCells.Clear();
        foreach (RailLine l in Lines)
        {
            int n = l.N;
            l.Kind = new int[n]; l.Inside = new float[n];
            int start = l.Closed ? l.Stops[0] : 0, state = 0;      // round a loop, start at a station (always on the ground)
            for (int k = 0; k < n; k++)
            {
                int i = l.W(start + k);
                float g = ground(l.Flat(i));
                if (float.IsNaN(g)) g = -6f;
                float gap = g - l.P[i].y;
                if (state == 0) state = gap > 9.5f ? 1 : gap < -7f ? 2 : 0;
                else if (state == 1 && gap < 7.5f) state = gap < -7f ? 2 : 0;
                else if (state == 2 && gap > -5f) state = gap > 9.5f ? 1 : 0;
                bool outside = !l.Closed && (i < l.Stops[0] || i > l.Stops[l.Stops.Count - 1]);
                l.Kind[i] = NearStation(l, i) || outside ? 0 : state;
            }
            for (int pass = 0; pass < 2; pass++)      // short stretches take their neighbours' kind
            {
                int from = 0;
                for (int i = 1; i <= n; i++)
                {
                    if (i < n && l.Kind[i] == l.Kind[from]) continue;
                    if (i - from < 7 && from > 0 && i < n) for (int k = from; k < i; k++) l.Kind[k] = l.Kind[from - 1];
                    from = i;
                }
            }
            for (int i = 0; i < n; i++) l.Inside[i] = l.Kind[i] == 1 ? 1e6f : 0f;
            for (int round = 0; round < (l.Closed ? 2 : 1); round++)
            {
                for (int i = 1; i < n + (l.Closed ? 1 : 0); i++) { int a = l.W(i), b = l.W(i - 1); if (l.Kind[a] == 1) l.Inside[a] = Mathf.Min(l.Inside[a], l.Inside[b] + 4f); }
                for (int i = n - 2 + (l.Closed ? 1 : 0); i >= 0; i--) { int a = l.W(i), b = l.W(i + 1); if (l.Kind[a] == 1) l.Inside[a] = Mathf.Min(l.Inside[a], l.Inside[b] + 4f); }
            }
            // the ground is left out over each tunnel's first 14 m (the portal's stone hood covers the hole)
            foreach (var (mouth, dir) in Mouths(l))
                for (int k = 0; k < 4; k++) Cut(l, l.W(mouth + dir * k));
            // ...and wherever the ground over a tunnel is too thin to clear its roof across the whole bore: the classing
            // above keeps a tunnel going while the ground over its middle is 7.5 m up and folds dips shorter than 28 m into
            // it, so on a hillside or in a dip the ground ran through the bore and blocked the line (players, 2026-11-21).
            // Those stretches are roofed in stone like the portals' hoods (a snow gallery), one point beyond each end.
            l.Thin = new bool[n];
            var thin = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (l.Kind[i] != 1) continue;
                Vector2 c = l.Flat(i), along = (l.Flat(i + 1) - l.Flat(i - 1)).normalized, side = new Vector2(along.y, -along.x);
                float lowest = float.MaxValue;
                for (float s = -TunnelHalf - 1f; s <= TunnelHalf + 1.01f; s += 1.3f)
                {
                    float g = ground(c + side * s);
                    lowest = Mathf.Min(lowest, float.IsNaN(g) ? -6f : g);
                }
                thin[i] = lowest - l.P[i].y < TunnelHigh + CoverNeeded;
            }
            for (int i = 0; i < n; i++)
                if (thin[i]) for (int d = -1; d <= 1; d++) { int j = l.W(i + d); if (l.Kind[j] == 1) l.Thin[j] = true; }
            for (int i = 0; i < n; i++) if (l.Thin[i]) Cut(l, i);
        }
    }
    // Ground a tunnel needs over its roof, across the whole bore, before it is left alone (the mesh between grid
    // points and the crags on steep ground can stand about a metre off the grid's own heights)
    const float CoverNeeded = 2f;
    // leave the ground out round point j of a line, across the bore and a metre beyond (a stone hood covers it)
    static void Cut(RailLine l, int j)
    {
        Vector2 c = l.Flat(j), along = (l.Flat(j + 1) - l.Flat(j - 1)).normalized, side = new Vector2(along.y, -along.x);
        for (float a = -2f; a <= 2.01f; a += 1f)
            for (float s = -TunnelHalf - 1f; s <= TunnelHalf + 1.01f; s += 0.75f)
                cutCells.Add(CutKey(c + along * a + side * s));
    }
    // each tunnel mouth: its first point inside, and which way the tunnel runs from it
    static IEnumerable<(int mouth, int dir)> Mouths(RailLine l)
    {
        for (int i = 1; i < l.N + (l.Closed ? 1 : 0); i++)
        {
            int a = l.W(i), b = l.W(i - 1);
            if ((l.Kind[a] == 1) == (l.Kind[b] == 1)) continue;
            yield return l.Kind[a] == 1 ? (a, 1) : (b, -1);
        }
    }
    static long CutKey(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 1.5f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 1.5f);
    public static bool InCut(Vector2 p) => cutCells.Count > 0 && cutCells.Contains(CutKey(p));

    // How the ground near point g (of Line, all lines' points together) is benched to the line: the half-width of the
    // level bed and the width of its banks, and how strongly (1 on the ground; fading over a tunnel's first 12 m, so the
    // cutting runs into the portal; 0 on trestles and further into tunnels)
    public static void Bench(int g, float gap, out float half, out float bank, out float strength)
    {
        RailLine l = pointLine[g]; int i = pointIndex[g];
        half = NearStation(l, i) ? StationHalf : BedHalf + 1.2f;
        bank = Mathf.Clamp(Mathf.Abs(gap) * 1.4f, 5f, 24f);
        strength = l.Kind[i] == 0 ? 1f : l.Kind[i] == 1 ? 1f - Smooth(l.Inside[i] / 12f) : 0f;
    }
    // How much room the dressing leaves round point g (none over tunnels, except at the portals)
    public static float Keep(int g)
    {
        RailLine l = pointLine[g]; int i = pointIndex[g];
        return l.Kind[i] == 1 ? (l.Inside[i] < 18f ? 11f : 0f) : NearStation(l, i) ? StationHalf + 3f : 8f;
    }

    // ---- building ----
    static void OBox(MeshBuilder mb, Vector3 c, Vector3 R, Vector3 U, Vector3 Fw, float hx, float hy, float hz, Color col)
    {
        var low = new[] { c - U * hy - R * hx - Fw * hz, c - U * hy + R * hx - Fw * hz, c - U * hy + R * hx + Fw * hz, c - U * hy - R * hx + Fw * hz };
        var high = new Vector3[4];
        for (int i = 0; i < 4; i++) high[i] = low[i] + U * (2f * hy);
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], col);
        mb.Tri(high[0], high[3], high[2], col); mb.Tri(high[0], high[2], high[1], col);
        mb.Tri(low[0], low[1], low[2], col); mb.Tri(low[0], low[2], low[3], col);
    }
    static void Frame(RailLine l, int i, out Vector3 Fw, out Vector3 R, out Vector3 U)
    {
        Fw = (l.P[l.W(i + 1)] - l.P[l.W(i - 1)]).normalized;
        R = Vector3.Cross(Vector3.up, Fw).normalized;
        U = Vector3.Cross(Fw, R);
    }
    public static Vector3 Right(RailLine l, int i) { Frame(l, i, out _, out Vector3 R, out _); return R; }
    public static Vector3 Track(RailLine l, int i, int side) => l.P[l.W(i)] + Right(l, i) * Offset(l, i, side);

    // The train tower's place (The Tree builds it): beside the loop's Plaza station on the plaza's side, clear of the
    // station's bench, and the way to the track from it
    public static Vector2 TowerSpot(out Vector2 toTrack)
    {
        RailLine l = Lines[0]; int c = l.Stops[0];
        foreach (RailLine x in Lines)
            for (int s = 0; s < x.Stops.Count; s++)
                if (x.Names[s].StartsWith("Plaza")) { l = x; c = x.Stops[s]; }
        Vector3 r3 = Right(l, c);
        Vector2 side = new Vector2(r3.x, r3.z).normalized;
        if (Vector2.Dot(side, WinterIslePuzzle.Plaza - l.Flat(c)) < 0f) side = -side;
        toTrack = -side;
        return l.Flat(c + StationReach / 2) + side * (StationHalf + 9f);
    }

    // Where the summit lift's bottom station stands: just beyond the buffers at the top of the mountain line
    public static Vector2 SummitLiftFoot()
    {
        RailLine l = Lines[Lines.Count - 1];
        foreach (RailLine x in Lines) if (!x.Closed) l = x;
        int n = l.N;
        Vector2 dir = (l.Flat(n - 1) - l.Flat(n - 4)).normalized;
        return l.Flat(n - 1) + dir * 8f;
    }

    public static void Build(PuzzleBuilder b, Vector3 root, Func<Vector2, float> heightAt)
    {
        if (Lines.Count == 0) return;
        var track = new MeshBuilder();      // ballast bed and sleepers (takes snow)
        var steel = new MeshBuilder();      // rails (bare)
        var trestle = new MeshBuilder();
        var tunnel = new MeshBuilder();
        var portal = new MeshBuilder();
        var station = new MeshBuilder();
        var glow = new MeshBuilder();
        var ballast = new Color(0.36f, 0.35f, 0.34f); var sleeper = new Color(0.33f, 0.24f, 0.16f); var rail = new Color(0.42f, 0.4f, 0.4f);
        var timber = new Color(0.45f, 0.32f, 0.2f); var stone = new Color(0.52f, 0.5f, 0.48f); var dark = new Color(0.3f, 0.29f, 0.28f);
        var red = new Color(0.68f, 0.1f, 0.08f); var green = new Color(0.1f, 0.33f, 0.18f); var gold = new Color(0.95f, 0.72f, 0.2f); var cream = new Color(0.9f, 0.86f, 0.76f);
        Vector3 L(Vector3 w) => w - root;
        float Ground(Vector2 p) { float h = heightAt(p); return float.IsNaN(h) ? -6f : h; }
        int bents = 0, tunnelPoints = 0, portals = 0, stations = 0, galleries = 0;
        var lampsAt = new List<Vector3>();
        var markers = new GameObject("Rail Stations");
        markers.transform.SetParent(b.Root.transform, false);
        var ring = new List<Vector2> { new Vector2(-TunnelHalf, -0.6f), new Vector2(-TunnelHalf, 4f) };
        for (int k = 1; k < 6; k++) { float a = Mathf.PI * k / 6f; ring.Add(new Vector2(-Mathf.Cos(a) * TunnelHalf, 4f + Mathf.Sin(a) * (TunnelHigh - 4f))); }
        ring.Add(new Vector2(TunnelHalf, 4f)); ring.Add(new Vector2(TunnelHalf, -0.6f)); ring.Add(ring[0]);

        foreach (RailLine l in Lines)
        {
            int n = l.N, segs = l.Closed ? n : n - 1;
            // the track: a bed, a sleeper at every point, two rails per track
            for (int i = 0; i < segs; i++)
            {
                Vector3 p0 = l.P[i], p1 = l.P[l.W(i + 1)], mid = (p0 + p1) * 0.5f;
                Vector3 Fw = (p1 - p0).normalized, R = Vector3.Cross(Vector3.up, Fw).normalized, U = Vector3.Cross(Fw, R);
                float half = (p1 - p0).magnitude * 0.5f + 0.06f;
                OBox(track, L(mid - U * 0.25f), R, U, Fw, l.Double ? BedHalf : BedHalf - 1.4f, 0.3f, half, ballast);
                foreach (int side in new[] { -1, 1 })
                {
                    if (side < 0 && Mathf.Abs(Offset(l, i, 1)) < 0.01f && Mathf.Abs(Offset(l, i + 1, 1)) < 0.01f) continue;      // one track here
                    Vector3 a = Track(l, i, side), z = Track(l, i + 1, side), m = (a + z) * 0.5f;
                    Vector3 f = (z - a).normalized, r = Vector3.Cross(Vector3.up, f).normalized, u = Vector3.Cross(f, r);
                    float h = (z - a).magnitude * 0.5f + 0.04f;
                    OBox(track, L(a + u * 0.1f), r, u, f, 1.3f, 0.07f, 0.13f, sleeper);
                    OBox(steel, L(m + r * -0.58f + u * 0.24f), r, u, f, 0.05f, 0.07f, h, rail);
                    OBox(steel, L(m + r * 0.58f + u * 0.24f), r, u, f, 0.05f, 0.07f, h, rail);
                }
            }
            // buffer stops at an open line's ends: a timber beam on posts with a red lamp
            if (!l.Closed)
                foreach (int end in new[] { 0, n - 1 })
                {
                    Frame(l, end, out Vector3 Fw, out Vector3 R, out Vector3 U);
                    Vector3 at = l.P[end] + Fw * (end == 0 ? -1.5f : 1.5f);
                    OBox(station, L(at + U * 0.9f), R, U, Fw, 1.6f, 0.3f, 0.3f, red);
                    foreach (int sx in new[] { -1, 1 }) OBox(station, L(at + R * (sx * 1.2f) + U * 0.4f), R, U, Fw, 0.2f, 0.65f, 0.2f, timber);
                    Kit.AddGlow(glow, L(at + U * 1.45f), new Vector3(0.3f, 0.3f, 0.3f), 0f, new Color(1f, 0.15f, 0.1f), 0.4f);
                }
            // trestles: a deck under the bed, and every 12 m a timber bent down to the ground (or the seabed), braced
            for (int i = 0; i < n; i++)
            {
                if (l.Kind[i] != 2) continue;
                Frame(l, i, out Vector3 Fw, out Vector3 R, out Vector3 U);
                if (i < segs) OBox(trestle, L((l.P[i] + l.P[l.W(i + 1)]) * 0.5f - U * 0.85f), R, U, Fw, BedHalf + 0.4f, 0.3f, 2.06f, timber * 0.8f);
                if (i % 3 != 0) continue;
                Vector3 top = l.P[i] - Vector3.up * 1.1f;
                Vector3 flatF = new Vector3(Fw.x, 0f, Fw.z).normalized;
                foreach (float s in new[] { -BedHalf + 0.6f, -1.6f, 1.6f, BedHalf - 0.6f })
                {
                    Vector3 legTop = top + R * s;
                    float foot = Ground(new Vector2(legTop.x, legTop.z)) - 1f, tall = legTop.y - foot;
                    if (tall <= 0.5f) continue;
                    OBox(trestle, L(new Vector3(legTop.x, (legTop.y + foot) * 0.5f, legTop.z)), R, Vector3.up, flatF, 0.28f, tall * 0.5f, 0.28f, timber);
                }
                float lowest = Mathf.Max(Ground(new Vector2(top.x, top.z) - new Vector2(R.x, R.z) * BedHalf), Ground(new Vector2(top.x, top.z) + new Vector2(R.x, R.z) * BedHalf));
                for (float y = top.y - 5f; y > lowest + 1f; y -= 5f)      // cross-braces
                    OBox(trestle, L(new Vector3(top.x, y, top.z)), R, Vector3.up, flatF, BedHalf, 0.15f, 0.15f, timber * 0.85f);
                bents++;
            }
            // tunnels: a lining of stone round the bed with a floor under it, lit by a lamp every 40 m
            int sinceLamp = 0;
            for (int i = 0; i < segs; i++)
            {
                int j = l.W(i + 1);
                if (l.Kind[i] != 1 && l.Kind[j] != 1) { sinceLamp = 0; continue; }
                tunnelPoints++;
                Frame(l, i, out Vector3 F0, out Vector3 R0, out Vector3 U0);
                Frame(l, j, out Vector3 F1, out Vector3 R1, out Vector3 U1);
                float lit = 0.4f + 0.6f * Mathf.Exp(-Mathf.Pow(((sinceLamp % 10) - 5f) / 2.4f, 2f));
                for (int k = 0; k + 1 < ring.Count; k++)
                {
                    Vector3 a0 = l.P[i] + R0 * ring[k].x + U0 * ring[k].y, b0 = l.P[i] + R0 * ring[k + 1].x + U0 * ring[k + 1].y;
                    Vector3 a1 = l.P[j] + R1 * ring[k].x + U1 * ring[k].y, b1 = l.P[j] + R1 * ring[k + 1].x + U1 * ring[k + 1].y;
                    Color c = stone * (0.85f + 0.15f * Mathf.PerlinNoise(i * 0.37f, k * 0.91f)) * lit;
                    tunnel.Quad(L(a0), L(b0), L(b1), L(a1), c);
                    tunnel.Quad(L(a0), L(a1), L(b1), L(b0), c);      // both ways: seen from inside, and from outside where it shows
                }
                if (sinceLamp % 10 == 5)
                {
                    Vector3 lamp = l.P[i] + R0 * (TunnelHalf - 0.15f) + U0 * 3.6f;
                    Kit.AddGlow(glow, L(lamp), new Vector3(0.06f, 0.5f, 0.35f), Mathf.Atan2(F0.x, F0.z) * Mathf.Rad2Deg, new Color(1f, 0.78f, 0.42f), (i * 0.13f) % 1f);
                    lampsAt.Add(lamp);
                }
                sinceLamp++;
            }
            // portals: a headwall of dressed stone round each mouth, and a stone hood over the tunnel's first 16 m that
            // covers the hole left in the hillside there
            foreach (var (mouth, dir) in Mouths(l))
            {
                Frame(l, mouth, out Vector3 Fw, out _, out _);
                Vector3 flatF = new Vector3(Fw.x, 0f, Fw.z).normalized * dir, Rr = Vector3.Cross(Vector3.up, flatF).normalized;      // flatF points into the tunnel
                float firstTop = l.P[mouth].y + TunnelHigh + 1.5f;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 a = l.P[l.W(mouth + dir * k)], z = l.P[l.W(mouth + dir * (k + 1))], mid = (a + z) * 0.5f;
                    Vector3 f = new Vector3(z.x - a.x, 0f, z.z - a.z).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
                    float top = Mathf.Max(a.y, z.y) + TunnelHigh + 1.5f;
                    for (float s = -TunnelHalf - 3.5f; s <= TunnelHalf + 3.51f; s += 1.75f)
                        foreach (Vector3 q in new[] { a, mid, z })
                            top = Mathf.Max(top, Ground(new Vector2(q.x + r.x * s, q.z + r.z * s)) + 0.4f);
                    if (k == 0) firstTop = top;
                    float baseY = Mathf.Min(a.y, z.y) - 0.6f, half = (z - a).magnitude * 0.5f + 0.15f;
                    foreach (int sx in new[] { -1, 1 })
                        OBox(portal, L(new Vector3(mid.x, (baseY + top) * 0.5f, mid.z) + r * (sx * (TunnelHalf + 1.9f))), r, Vector3.up, f, 1.6f, (top - baseY) * 0.5f, half, stone * (0.9f + 0.05f * k));
                    float roofLow = Mathf.Max(a.y, z.y) + TunnelHigh + 0.2f;
                    OBox(portal, L(new Vector3(mid.x, (roofLow + top) * 0.5f, mid.z)), r, Vector3.up, f, TunnelHalf + 3.5f, (top - roofLow) * 0.5f, half, stone * 0.88f);
                }
                Vector3 face = l.P[mouth] - flatF * 1.3f;
                float faceBase = l.P[mouth].y - 0.6f, faceTop = firstTop + 0.6f, lintelLow = l.P[mouth].y + TunnelHigh;
                foreach (int sx in new[] { -1, 1 })
                    OBox(portal, L(new Vector3(face.x, (faceBase + faceTop) * 0.5f, face.z) + Rr * (sx * (TunnelHalf + 1.9f))), Rr, Vector3.up, flatF, 1.9f, (faceTop - faceBase) * 0.5f, 1.4f, stone);
                OBox(portal, L(new Vector3(face.x, (lintelLow + faceTop) * 0.5f, face.z)), Rr, Vector3.up, flatF, TunnelHalf + 0.05f, (faceTop - lintelLow) * 0.5f, 1.4f, stone * 0.92f);
                OBox(portal, L(new Vector3(face.x, faceTop + 0.2f, face.z) - flatF * 0.2f), Rr, Vector3.up, flatF, TunnelHalf + 4.2f, 0.2f, 1.8f, dark);
                OBox(portal, L(new Vector3(face.x, lintelLow + 0.9f, face.z) - flatF * 1.45f), Rr, Vector3.up, flatF, 1.6f, 0.4f, 0.05f, cream);      // a name board
                Vector3 pl = new Vector3(face.x, lintelLow + 1.9f, face.z) - Rr * (TunnelHalf + 2.6f) - flatF * 1.5f, pr = new Vector3(face.x, lintelLow + 1.9f, face.z) + Rr * (TunnelHalf + 2.6f) - flatF * 1.5f;
                Kit.LightString(b, "Rail Portal Lights " + (portals + 1), new List<Vector3> { L(pl), L(pr) }, 0.4f, 0.4f, false, false, 700 + portals);
                portals++;
            }
            // snow galleries where the ground over a tunnel is thin (Classify cut it away there): a stone roof over the bore
            // and a wall each side up to the ground on that side, so the hole in the hillside is closed and nothing stands in
            // the bore (the portals' own hoods cover each tunnel's first 16 m)
            for (int i = 0; i < l.N - (l.Closed ? 0 : 1); i++)
            {
                int i1 = l.W(i + 1);
                if (!(l.Thin[i] || l.Thin[i1]) || l.Kind[i] != 1 || l.Kind[i1] != 1 || Mathf.Min(l.Inside[i], l.Inside[i1]) < 16f) continue;
                Vector3 a = l.P[i], z = l.P[i1], mid = (a + z) * 0.5f;
                Vector3 f = new Vector3(z.x - a.x, 0f, z.z - a.z).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
                float roofLow = Mathf.Max(a.y, z.y) + TunnelHigh + 0.2f, roofTop = roofLow + 1.3f;
                float baseY = Mathf.Min(a.y, z.y) - 0.6f, half = (z - a).magnitude * 0.5f + 0.15f;
                foreach (int sx in new[] { -1, 1 })
                {
                    float top = roofTop;
                    for (float s = TunnelHalf - 1f; s <= TunnelHalf + 3.51f; s += 1.5f)
                        foreach (Vector3 q in new[] { a, mid, z })
                            top = Mathf.Max(top, Ground(new Vector2(q.x + r.x * s * sx, q.z + r.z * s * sx)) + 0.4f);
                    OBox(portal, L(new Vector3(mid.x, (baseY + top) * 0.5f, mid.z) + r * (sx * (TunnelHalf + 1.9f))), r, Vector3.up, f, 1.6f, (top - baseY) * 0.5f, half, stone * 0.9f);
                }
                OBox(portal, L(new Vector3(mid.x, (roofLow + roofTop) * 0.5f, mid.z)), r, Vector3.up, f, TunnelHalf + 3.5f, (roofTop - roofLow) * 0.5f, half, stone * 0.86f);
                galleries++;
            }
            // stations: a platform beside each track (each side of a single track), a shelter on each, lamps and lights,
            // ramps at the platforms' ends
            for (int s = 0; s < l.Stops.Count; s++)
            {
                int c = l.Stops[s];
                Frame(l, c, out Vector3 Fw, out _, out _);
                Vector3 flatF = new Vector3(Fw.x, 0f, Fw.z).normalized, Rr = Vector3.Cross(Vector3.up, flatF).normalized;
                float yaw = Mathf.Atan2(flatF.x, flatF.z) * Mathf.Rad2Deg;
                string name = l.Names[s];
                foreach (int side in new[] { -1, 1 })
                {
                    float Out(int j) => Mathf.Abs(Offset(l, j, 1)) + 1.3f + 1.9f;      // the platform's middle from the line
                    for (int k = -StationReach; k < StationReach; k++)
                    {
                        int j = l.W(c + k), j1 = l.W(c + k + 1);
                        Vector3 a = l.P[j] + Right(l, j) * (side * Out(j)), z = l.P[j1] + Right(l, j1) * (side * Out(j1));
                        Vector3 f = (z - a).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
                        OBox(station, L((a + z) * 0.5f + Vector3.up * (PlatformTop - 0.55f)), r, Vector3.up, f, 1.9f, 0.55f, (z - a).magnitude * 0.5f + 0.05f, cream * 0.8f);
                    }
                    foreach (int endK in new[] { -StationReach, StationReach })      // a ramp down from each end
                    {
                        int j = l.W(c + endK);
                        Frame(l, j, out Vector3 fj, out _, out _);
                        Vector3 f = new Vector3(fj.x, 0f, fj.z).normalized * Mathf.Sign(endK), r = Vector3.Cross(Vector3.up, f).normalized;
                        Vector3 edge = l.P[j] + Right(l, j) * (side * Out(j));
                        float drop = PlatformTop + BenchBelow;
                        Vector3 rampMid = edge + f * 2.5f + Vector3.up * (PlatformTop - drop * 0.5f - 0.15f);
                        Vector3 slope = (f * 5f - Vector3.up * drop).normalized, up = Vector3.Cross(slope, r);
                        OBox(station, L(rampMid), r, up, slope, 1.9f, 0.15f, 2.6f, cream * 0.75f);
                    }
                    // the shelter: posts, a back wall, a pitched roof, the name board
                    Vector3 sh = l.P[c] + Rr * (side * (Out(c) + 0.6f)) + Vector3.up * PlatformTop;
                    foreach (float along in new[] { -6f, 0f, 6f })
                        OBox(station, L(sh + flatF * along + Vector3.up * 1.6f - Rr * (side * 0.9f)), Rr, Vector3.up, flatF, 0.12f, 1.6f, 0.12f, timber);
                    OBox(station, L(sh + Vector3.up * 1.3f + Rr * (side * 0.9f)), Rr, Vector3.up, flatF, 0.1f, 1.3f, 6.3f, timber * 0.9f);
                    OBox(station, L(sh + Vector3.up * 3.35f), Rr, Vector3.up, flatF, 1.4f, 0.12f, 6.8f, red);
                    OBox(station, L(sh + Vector3.up * 3.55f), Rr, Vector3.up, flatF, 0.9f, 0.1f, 6.6f, green);
                    OBox(station, L(sh + Vector3.up * 2.55f + Rr * (side * 0.78f)), Rr, Vector3.up, flatF, 0.04f, 0.35f, 2.2f, green);      // the name board
                    for (int k = 0; k < name.Length && k < 14; k++)      // letters as gold blocks, a stand-in for the name
                        if (name[k] != ' ') OBox(station, L(sh + Vector3.up * 2.55f + Rr * (side * 0.73f) + flatF * ((-1.8f + k * 0.27f) * side)), Rr, Vector3.up, flatF, 0.02f, 0.18f, 0.09f, gold);
                    Kit.AddGlow(glow, L(sh + Vector3.up * 3.15f), new Vector3(0.4f, 0.14f, 0.4f), yaw, new Color(1f, 0.8f, 0.45f), s * 0.17f);
                    var posts = new List<Vector3>();
                    for (int k = -StationReach; k <= StationReach; k += 5)
                    {
                        int j = l.W(c + k);
                        Vector3 top = l.P[j] + Right(l, j) * (side * (Out(j) + 1.7f)) + Vector3.up * (PlatformTop + 3.1f);
                        posts.Add(L(top));
                        // its post, from the platform up (LightString's own posts start at the pack's root: the sea floor)
                        OBox(station, L(top - Vector3.up * 1.55f), Rr, Vector3.up, flatF, 0.06f, 1.6f, 0.06f, timber);
                    }
                    Kit.LightString(b, "Rail Station Lights " + l.Name + " " + s + (side < 0 ? "L" : "R"), posts, 0.35f, 0.5f, false, false, 800 + stations * 2 + side);
                }
                var mk = new GameObject("Rail Station " + l.Name + " " + s + " " + name);
                mk.transform.SetParent(markers.transform, false);
                mk.transform.position = l.P[c] + Vector3.up * 1f;
                stations++;
            }
        }
        Material snowy = b.ShaderMaterial("BWM/Snowy", "Snowy"), bare = b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f));
        b.MeshPiece("Rail Track", track.ToMesh(), snowy, true);
        b.MeshPiece("Rail Steel", steel.ToMesh(), bare, false);
        if (bents > 0) b.MeshPiece("Rail Trestles", trestle.ToMesh(), snowy, true);
        if (tunnelPoints > 0) b.MeshPiece("Rail Tunnels", tunnel.ToMesh(), b.ShaderMaterial("BWM/Mine", "Rail Tunnel"), true);
        if (portals > 0) b.MeshPiece("Rail Portals", portal.ToMesh(), snowy, true);
        b.MeshPiece("Rail Platforms", station.ToMesh(), snowy, true);
        b.MeshPiece("Rail Lamps", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        // the host's orders travel to everyone through this script's synced text (see RailControl.cs)
        Piece control = b.Empty("Rail Control", 0f, 0f, 0f);
        Component railControl = b.AddCustomScript(control, "RailControl");
        BuildTrains(b, railControl);
        Save(lampsAt);
        var report = new StringBuilder("[BWM] Winter Isle railway:");
        foreach (RailLine l in Lines)
        {
            int tun = 0, tre = 0; for (int i = 0; i < l.N; i++) { if (l.Kind[i] == 1) tun++; else if (l.Kind[i] == 2) tre++; }
            report.Append(" ").Append(l.Name).Append(" (").Append(l.Closed ? "loop" : "open").Append(") ").Append(l.N * 4).Append(" m, ").Append(l.Stops.Count).Append(" stations (").Append(string.Join(", ", l.Names)).Append("), ").Append(tun * 4).Append(" m in tunnel, ").Append(tre * 4).Append(" m on trestles, ").Append(l.Trains).Append(" trains;");
        }
        Debug.Log(report.Append(" ").Append(portals / 2).Append(" tunnels, ").Append(galleries * 4).Append(" m of snow gallery where the ground over a tunnel is thin, ").Append(bents).Append(" trestle bents").ToString());
    }

    // The trains: a red locomotive and two open green coaches each, built facing +z with their middle at the origin
    // (on the rails); Winter Walk moves and turns them. Each coach has eight seats and a throttle (a push button on a
    // brass stand at its front, wired to RailControl.Throttle with the train's number), and so does the locomotive's
    // cab end
    static void BuildTrains(PuzzleBuilder b, Component railControl)
    {
        var red = new Color(0.72f, 0.09f, 0.08f); var green = new Color(0.12f, 0.38f, 0.2f); var gold = new Color(0.95f, 0.72f, 0.2f);
        var black = new Color(0.12f, 0.12f, 0.13f); var wood = new Color(0.45f, 0.32f, 0.2f); var cream = new Color(0.92f, 0.88f, 0.78f);
        var loco = new MeshBuilder();
        Kit.AddBox(loco, new Vector3(0f, 0.25f, 0f), new Vector3(2.4f, 0.5f, 8.4f), black);                  // the frame
        for (int k = 0; k < 10; k++) Kit.AddBox(loco, new Vector3(0f, 0.75f, -1.2f + k * 0.55f), new Vector3(2.0f, 1.9f, 0.56f), red * (k % 3 == 0 ? 0.88f : 1f));      // the boiler
        Kit.AddBox(loco, new Vector3(0f, 2.65f, 3.6f), new Vector3(0.5f, 1.1f, 0.5f), black);                // the chimney
        Kit.AddBox(loco, new Vector3(0f, 3.75f, 3.6f), new Vector3(0.75f, 0.25f, 0.75f), gold);
        Kit.AddBox(loco, new Vector3(0f, 2.65f, 1.6f), new Vector3(0.6f, 0.45f, 0.6f), gold);                // the dome
        Kit.AddBox(loco, new Vector3(0f, 0.75f, -3.1f), new Vector3(2.5f, 2.6f, 2.0f), green);              // the cab
        Kit.AddBox(loco, new Vector3(0f, 3.35f, -3.1f), new Vector3(2.8f, 0.2f, 2.4f), black);
        Kit.AddBox(loco, new Vector3(0f, 0.5f, 4.25f), new Vector3(2.6f, 0.35f, 0.2f), gold);               // the buffer beam
        foreach (int sx in new[] { -1, 1 })
            foreach (float z in new[] { -2.6f, -0.6f, 1.4f, 3.2f })
                Kit.AddBox(loco, new Vector3(sx * 1.05f, 0.05f, z), new Vector3(0.15f, 0.8f, 0.85f), red * 0.7f);      // wheels
        Kit.AddStar(loco, new Vector3(0f, 1.7f, 4.4f), 0.45f, 0.06f, gold);
        var coach = new MeshBuilder();
        Kit.AddBox(coach, new Vector3(0f, 0.25f, 0f), new Vector3(2.5f, 0.3f, 8.6f), black);
        Kit.AddBox(coach, new Vector3(0f, 0.55f, 0f), new Vector3(2.5f, 0.15f, 8.6f), wood);                // the floor (its top 0.7 m up)
        foreach (int sx in new[] { -1, 1 })
        {
            Kit.AddBox(coach, new Vector3(sx * 1.2f, 0.7f, 0f), new Vector3(0.12f, 0.8f, 8.6f), green);    // low sides
            foreach (float z in new[] { -4.1f, -1.4f, 1.4f, 4.1f })
                Kit.AddBox(coach, new Vector3(sx * 1.2f, 1.5f, z), new Vector3(0.12f, 1.4f, 0.12f), gold);      // posts
            foreach (float z in new[] { -3.2f, 3.2f })
                Kit.AddBox(coach, new Vector3(sx * 1.05f, 0.05f, z), new Vector3(0.15f, 0.75f, 0.75f), black);
        }
        Kit.AddBox(coach, new Vector3(0f, 2.9f, 0f), new Vector3(2.8f, 0.18f, 9f), red);                    // the roof
        Kit.AddBox(coach, new Vector3(0f, 3.08f, 0f), new Vector3(2.2f, 0.12f, 8.6f), cream);
        for (int row = 0; row < 4; row++)      // benches, facing forward
        {
            Kit.AddBox(coach, new Vector3(0f, 0.7f, -3.2f + row * 2.1f - 0.1f), new Vector3(2.2f, 0.45f, 0.55f), wood * 1.15f);
            Kit.AddBox(coach, new Vector3(0f, 0.7f, -3.2f + row * 2.1f - 0.45f), new Vector3(2.2f, 1.0f, 0.12f), wood);      // the back
        }
        Kit.AddBox(coach, new Vector3(-0.85f, 0.7f, 4.05f), new Vector3(0.14f, 1.0f, 0.14f), gold);        // the throttle's stand, at the front on the left
        Mesh locoMesh = b.SaveMesh("Train Locomotive", loco.ToMesh()), coachMesh = b.SaveMesh("Train Coach", coach.ToMesh());
        Material mat = b.ShaderMaterial("BWM/Snowy", "Train", m => m.SetFloat("_SnowLine", 0.8f));
        var trainsRoot = new GameObject("Trains");
        trainsRoot.transform.SetParent(b.Root.transform, false);
        int t = 0;
        foreach (RailLine l in Lines)
            for (int k = 0; k < l.Trains; k++, t++)
                for (int c = 0; c < Cars; c++)
                {
                    var car = new GameObject("Train " + t + " Car " + c);
                    car.transform.SetParent(trainsRoot.transform, false);
                    // parked under the first station until Winter Walk places them (each a little apart, so none overlap)
                    car.transform.position = l.P[l.Stops[0]] + Vector3.down * (20f + t * 6f) + Vector3.forward * (c * CarLength);
                    var body = new GameObject("Body");
                    body.transform.SetParent(car.transform, false);
                    Mesh m = c == 0 ? locoMesh : coachMesh;
                    body.AddComponent<MeshFilter>().sharedMesh = m;
                    body.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    body.AddComponent<MeshCollider>().sharedMesh = m;
                    // the throttle: the game's lever, pushed forward to set off, on the coach's stand (facing the seats) or on
                    // the cab's back wall. Alone in its own group, one push is a success, which asks the host to depart
                    Piece throttle = b.Part("Button Sim", 0f, 0f, 0f, 0f).Named("Throttle " + t + (c == 0 ? " Cab" : " Coach " + c));
                    throttle.Transform.SetParent(car.transform, false);
                    throttle.Transform.localPosition = c == 0 ? new Vector3(0.6f, 1.45f, -4.12f) : new Vector3(-0.85f, 1.6f, 3.98f);
                    throttle.Transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    throttle.Set("PuzzleSimPressSwitch", "group", "throttle " + t + " " + c);
                    throttle.Set("PuzzleSimPressSwitch", "activeSeconds", 1f);
                    throttle.Set("PuzzleSimPressSwitch", "timeoutSeconds", 4f);
                    throttle.On("PuzzleSimPressSwitch", "onSuccess", railControl, "Throttle", t);
                    if (c == 0) continue;
                    for (int row = 0; row < 4; row++)
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            var seat = new Piece(new GameObject("Seat " + row + (sgn < 0 ? "L" : "R")));
                            seat.Transform.SetParent(car.transform, false);
                            seat.Transform.localPosition = new Vector3(0.55f * sgn, 1.15f, -3.2f + row * 2.1f);
                            b.AddScript(seat, "PuzzleSeat");
                            seat.Set("PuzzleSeat", "pose", 14 + (row * 2 + (sgn > 0 ? 1 : 0)) % 5);      // TrainSeatA..E
                            seat.Set("PuzzleSeat", "clickSize", new Vector3(0.7f, 0.7f, 0.7f));
                        }
                }
    }

    // The network for Winter Walk: "LINE name closed|open single|double"; per point "P x y z kind" (single) or
    // "P ux uy uz dx dy dz kind" (double: the up and down tracks); "S index name"; then "TRAIN line" for each train in
    // order; "L x y z" for each tunnel lamp
    static void Save(List<Vector3> lamps)
    {
        var text = new StringBuilder("# the Winter Isle railway: LINE name closed|open single|double; P track points, kind; S point name; TRAIN line; L lamp\n");
        foreach (RailLine l in Lines)
        {
            text.Append("LINE ").Append(l.Name).Append(l.Closed ? " closed" : " open").Append(l.Double ? " double" : " single").Append('\n');
            for (int i = 0; i < l.N; i++)
            {
                if (l.Double)
                {
                    Vector3 u = Track(l, i, 1), d = Track(l, i, -1);
                    text.Append("P ").Append(F(u.x)).Append(' ').Append(F(u.y)).Append(' ').Append(F(u.z)).Append(' ')
                        .Append(F(d.x)).Append(' ').Append(F(d.y)).Append(' ').Append(F(d.z)).Append(' ').Append(l.Kind[i]).Append('\n');
                }
                else text.Append("P ").Append(F(l.P[i].x)).Append(' ').Append(F(l.P[i].y)).Append(' ').Append(F(l.P[i].z)).Append(' ').Append(l.Kind[i]).Append('\n');
            }
            for (int s = 0; s < l.Stops.Count; s++) text.Append("S ").Append(l.Stops[s]).Append(' ').Append(l.Names[s]).Append('\n');
        }
        foreach (RailLine l in Lines) for (int k = 0; k < l.Trains; k++) text.Append("TRAIN ").Append(l.Name).Append('\n');
        foreach (Vector3 lp in lamps) text.Append("L ").Append(F(lp.x)).Append(' ').Append(F(lp.y)).Append(' ').Append(F(lp.z)).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(LineFile));
        File.WriteAllText(LineFile, text.ToString());
    }
}
