// The Winter Isle's coal mine: a hall under the great mountain's summit, three tunnels out to portals on the
// mountain's east, north-west and south flanks, cross-cuts with coal faces off them, and a double track that
// mantrips run round (out of one tunnel, round a loop on the portal's apron, back in, round the hall and out of
// the next). Part of the island's pack: WinterIslePuzzle calls FindPortals, FindCuts and Build.
//
// Everything is level, at Floor metres above the sea. A portal is where a tunnel's line meets the mountainside;
// the last stretch, where there is too little rock overhead, is an open cut under a stone shed (InCut tells the
// island's mesh to leave the ground out there).
//
// Nothing from the sky lights the mine. Lamps hang in some stretches and not in others, and their light is
// worked out here and stored in the mesh's colours (shader BWM/Mine). Winter Walk's MineDriver moves the mantrips
// along the "Mine Line" markers and opens the gates when the mine tower's five plinths hold gourds (TheTreePuzzle
// builds that tower beside the east portal; it reads where from WinterIsle.mine, which Build writes).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Bwm;
using UnityEngine;

public static class WinterIsleMine
{
    public const float Floor = 30f;                 // the mine's level, metres above the sea
    public const float HalfWidth = 3.4f;            // of a tunnel
    public const float TrackSide = 1.5f;            // each track's centre, either side of the tunnel's middle
    public const float HallRadius = 26f, RingRadius = 12f, HallHeight = 13f, PillarRadius = 5f;
    public const float ApronOut = 10f, ApronRadius = 14f;      // the level ground outside a portal
    public const int Trips = 8;
    public static readonly float[] Bearings = { 0.52f, 2.62f, 4.45f };      // east (towards the plaza), north-west, south
    public static readonly float[] Threshold = new float[3];      // metres from the hub to where each tunnel meets daylight
    public static readonly float[] CutStart = new float[3];       // ...and to where its open cut begins

    public static Vector2 Hub => WinterIslePuzzle.Summit;
    public static Vector2 Dir(int i) => new Vector2(Mathf.Cos(Bearings[i]), Mathf.Sin(Bearings[i]));
    public static Vector2 Right(int i) { Vector2 d = Dir(i); return new Vector2(d.y, -d.x); }
    public static Vector2 Point(int i, float along, float lateral) => Hub + Dir(i) * along + Right(i) * lateral;
    public static Vector2 Apron(int i) => Point(i, Threshold[i] + ApronOut, 0f);
    // where the mine tower stands: on the east portal's apron, beside the track's loop
    public static Vector2 Tower => Point(0, Threshold[0] + 9f, -10.5f);

    static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Land", "WinterIsle.mine"));

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
        }
    }

    // Where each tunnel comes out: the first point along its line where the ground is down to the mine's level
    public static void FindPortals(Func<Vector2, float> gridHeight)
    {
        for (int i = 0; i < 3; i++)
        {
            Threshold[i] = 1600f;
            for (float r = 150f; r < 1600f; r += 1.5f)
            {
                float h = gridHeight(Point(i, r, 0f));
                if (float.IsNaN(h) || h < Floor + 0.5f) { Threshold[i] = r; break; }
            }
        }
    }

    // Where each open cut begins: going in from the portal, the first point with enough rock over the tunnel
    public static void FindCuts(Func<Vector2, float> heightAt)
    {
        for (int i = 0; i < 3; i++)
        {
            CutStart[i] = Threshold[i] - 90f;
            for (float r = Threshold[i]; r > Threshold[i] - 90f; r -= 1f)
            {
                float cover = Mathf.Min(heightAt(Point(i, r, -5.2f)), Mathf.Min(heightAt(Point(i, r, 0f)), heightAt(Point(i, r, 5.2f))));
                if (cover >= Floor + 5.3f) { CutStart[i] = r; break; }
            }
        }
    }

    // Whether the island's ground is left out at p (the open cuts)
    public static bool InCut(Vector2 p)
    {
        Vector2 q = p - Hub;
        for (int i = 0; i < 3; i++)
        {
            float along = Vector2.Dot(q, Dir(i)), lateral = Vector2.Dot(q, Right(i));
            if (along > CutStart[i] - 1f && along < Threshold[i] + 0.6f && Mathf.Abs(lateral) < 4.6f) return true;
        }
        return false;
    }

    public static void Save()
    {
        var lines = new List<string>();
        for (int i = 0; i < 3; i++) lines.Add(Threshold[i].ToString("R", CultureInfo.InvariantCulture) + " " + CutStart[i].ToString("R", CultureInfo.InvariantCulture));
        File.WriteAllLines(FilePath, lines);
    }

    // For the other packs' builds (The Tree): where the island's last build put the portals
    public static bool Load()
    {
        if (!File.Exists(FilePath)) return false;
        string[] lines = File.ReadAllLines(FilePath);
        for (int i = 0; i < 3 && i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(' ');
            Threshold[i] = float.Parse(parts[0], CultureInfo.InvariantCulture);
            CutStart[i] = float.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return lines.Length >= 3;
    }

    static readonly Color Rock = new Color(0.46f, 0.42f, 0.38f), Coal = new Color(0.075f, 0.075f, 0.09f), Ballast = new Color(0.34f, 0.31f, 0.28f);
    static readonly Color Timber = new Color(0.55f, 0.39f, 0.22f), Steel = new Color(0.5f, 0.5f, 0.54f), Tie = new Color(0.26f, 0.19f, 0.13f);
    static readonly Color LampGlass = new Color(1f, 0.74f, 0.4f), Stone = new Color(0.56f, 0.56f, 0.58f);

    static Vector2[] Profile(float w, float wall, float crown)
    {
        float third = wall / 2.6f;
        return new[]
        {
            new Vector2(-w, 0f), new Vector2(-w, 1f * third), new Vector2(-w, 1.8f * third), new Vector2(-w, wall), new Vector2(-w * 0.55f, wall + (crown - wall) * 0.8f),
            new Vector2(0f, crown),
            new Vector2(w * 0.55f, wall + (crown - wall) * 0.8f), new Vector2(w, wall), new Vector2(w, 1.8f * third), new Vector2(w, 1f * third), new Vector2(w, 0f),
        };
    }

    public static void Build(PuzzleBuilder b, Vector3 root, Func<Vector2, float> heightAt)
    {
        var lamps = new List<Vector3>();
        var solid = new MeshBuilder();
        Vector3 W3(Vector2 p, float up) => new Vector3(p.x, Floor + up, p.y);
        Vector3 Loc(Vector3 w) => w - root;

        // ---- where the lamps hang: near the hall and the portals, and in a stretch now and then between ----
        bool LitStretch(int i, float s) => s < HallRadius + 70f || s > Threshold[i] - 80f || Mathf.Repeat(s, 150f) < 55f;
        const float SetSpacing = 6f;
        for (int i = 0; i < 3; i++)
        {
            int n = 0;
            for (float s = HallRadius + 5f; s < Threshold[i] - 3f; s += SetSpacing, n++)
                if (n % 2 == 0 && LitStretch(i, s)) lamps.Add(W3(Point(i, s, 0f), 2.45f));
        }
        var hallLamps = new List<Vector3>();
        for (int k = 0; k < 18; k++)
        {
            float a = 2f * Mathf.PI * k / 18f;
            bool nearTunnel = false;
            for (int i = 0; i < 3; i++) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, Bearings[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * HallRadius < 6f) nearTunnel = true;
            if (!nearTunnel) hallLamps.Add(W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HallRadius - 0.9f), 3.7f));
        }
        for (int k = 0; k < 6; k++)
        {
            float a = 2f * Mathf.PI * (k + 0.5f) / 6f;
            hallLamps.Add(W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (PillarRadius + 0.35f), 3.6f));
        }
        lamps.AddRange(hallLamps);

        Color Lit(Vector3 w, Color albedo)
        {
            float r = 0.03f, g = 0.035f, bl = 0.05f;
            foreach (Vector3 lamp in lamps)
            {
                float dx = lamp.x - w.x, dy = lamp.y - w.y, dz = lamp.z - w.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > 576f) continue;
                float fade = 1f - Mathf.Sqrt(d2) / 24f;
                float light = 2.6f * fade * fade / (1f + d2 / 6f);
                r += light; g += light * 0.74f; bl += light * 0.42f;
            }
            var q = new Vector2(w.x, w.z) - Hub;
            for (int i = 0; i < 3; i++)
            {
                float along = Vector2.Dot(q, Dir(i)), lateral = Vector2.Dot(q, Right(i));
                if (Mathf.Abs(lateral) > 18f || along < Threshold[i] - 40f) continue;
                float day = Smooth((along - (Threshold[i] - 40f)) / 40f);
                day *= day;
                r += 0.82f * day; g += 0.87f * day; bl += 0.97f * day;
            }
            return new Color(albedo.r * r, albedo.g * g, albedo.b * bl, 1f);
        }
        void Q(MeshBuilder mb, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color albedo, float glow = 0f, float phase = 0f)
        {
            var uv = new Vector2(glow, phase);
            if (glow > 0.5f) mb.QuadUV(Loc(p0), Loc(p1), Loc(p2), Loc(p3), uv, uv, uv, uv, albedo, albedo, albedo, albedo);
            else mb.QuadUV(Loc(p0), Loc(p1), Loc(p2), Loc(p3), uv, uv, uv, uv, Lit(p0, albedo), Lit(p1, albedo), Lit(p2, albedo), Lit(p3, albedo));
        }
        void Solid(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            solid.Quad(Loc(p0), Loc(p1), Loc(p2), Loc(p3), Color.white);
            solid.Quad(Loc(p3), Loc(p2), Loc(p1), Loc(p0), Color.white);
        }
        // a box standing on 'foot' (world), its z along 'forward'
        void Box(MeshBuilder mb, Vector3 foot, Vector3 size, Vector2 forward, Color albedo, float glow = 0f, float phase = 0f)
        {
            var f = new Vector3(forward.x, 0f, forward.y) * (size.z / 2f);
            var s = new Vector3(forward.y, 0f, -forward.x) * (size.x / 2f);
            Vector3 up = Vector3.up * size.y;
            Vector3[] low = { foot - s - f, foot + s - f, foot + s + f, foot - s + f };
            for (int k = 0; k < 4; k++) Q(mb, low[k], low[(k + 1) % 4], low[(k + 1) % 4] + up, low[k] + up, albedo, glow, phase);
            Q(mb, low[0] + up, low[1] + up, low[2] + up, low[3] + up, albedo, glow, phase);
            Q(mb, low[0], low[3], low[2], low[1], albedo, glow, phase);
        }
        void Lamp(MeshBuilder mb, Vector3 at, int seed)
        {
            Box(mb, at + Vector3.up * 0.1f, new Vector3(0.05f, 0.5f, 0.05f), Vector2.up, Tie);
            Box(mb, at - Vector3.up * 0.2f, new Vector3(0.24f, 0.3f, 0.24f), Vector2.up, LampGlass, 1f, Hash(5, seed, 3));
        }
        // a heap of coal: a lumpy cone
        void Heap(MeshBuilder mb, Vector2 at, float radius, float height, int seed)
        {
            Vector3 top = W3(at, height);
            for (int k = 0; k < 9; k++)
            {
                float a0 = 2f * Mathf.PI * k / 9f, a1 = 2f * Mathf.PI * (k + 1) / 9f;
                float r0 = radius * (0.75f + 0.5f * Hash(seed, k, 1)), r1 = radius * (0.75f + 0.5f * Hash(seed, (k + 1) % 9, 1));
                Vector3 p0 = W3(at + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0, 0f), p1 = W3(at + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r1, 0f);
                Vector3 m0 = Vector3.Lerp(p0, top, 0.55f) + Vector3.up * 0.15f * Hash(seed, k, 2), m1 = Vector3.Lerp(p1, top, 0.55f) + Vector3.up * 0.15f * Hash(seed, (k + 1) % 9, 2);
                Q(mb, p0, p1, m1, m0, Coal * (0.8f + 0.5f * Hash(seed, k, 4)));
                Q(mb, m0, m1, top, top, Coal * (0.9f + 0.5f * Hash(seed, k, 5)));
            }
        }
        // the wall across a tube's end, between its profile and a rectangle (wide half-width, top)
        void Bulkhead(MeshBuilder mb, Vector2 origin, Vector2 dir, float s, Vector2[] prof, float wide, float top, Color albedo)
        {
            var right = new Vector2(dir.y, -dir.x);
            Vector3 V(float lateral, float y) => W3(origin + dir * s + right * lateral, y);
            Vector3 tl = V(-wide, top), tr = V(wide, top);
            int apex = 0;
            for (int k = 1; k < prof.Length; k++) if (prof[k].y > prof[apex].y) apex = k;
            void T(Vector3 p0, Vector3 p1, Vector3 p2) { Q(mb, p0, p1, p2, p2, albedo); Solid(p0, p1, p2, p2); }
            for (int k = 0; k < apex; k++) T(tl, V(prof[k].x, prof[k].y), V(prof[k + 1].x, prof[k + 1].y));
            T(tl, V(prof[apex].x, prof[apex].y), tr);
            for (int k = apex; k + 1 < prof.Length; k++) T(tr, V(prof[k].x, prof[k].y), V(prof[k + 1].x, prof[k + 1].y));
            T(V(-wide, 0f), V(prof[0].x, 0f), tl);
            T(V(wide, 0f), tr, V(prof[prof.Length - 1].x, 0f));
        }
        // a rock tube along 'dir' from s0 to s1; 'open' leaves wall panels out (ring's middle s, panel)
        void Tube(MeshBuilder mb, Vector2 origin, Vector2 dir, float s0, float s1, Vector2[] prof, int seed, Func<float, int, bool> open, bool capEnd)
        {
            var right = new Vector2(dir.y, -dir.x);
            int n = Mathf.Max(1, Mathf.RoundToInt((s1 - s0) / 2f));
            float step = (s1 - s0) / n;
            int last = prof.Length - 1;
            bool OpenNear(int ring, int k)
            {
                if (open == null) return false;
                for (int dr = -1; dr <= 0; dr++)
                    for (int dk = -1; dk <= 0; dk++)
                    {
                        int rr = ring + dr, kk = k + dk;
                        if (rr < 0 || rr >= n || kk < 0 || kk >= last) continue;
                        if (open(s0 + (rr + 0.5f) * step, kk)) return true;
                    }
                return false;
            }
            Vector3 P(int ring, int k)
            {
                float s = s0 + ring * step;
                Vector2 pr = prof[k];
                bool plain = ring == 0 || ring == n || OpenNear(ring, k);
                float rough = plain ? 0f : (Hash(seed, ring, k) - 0.5f) * 0.55f;
                if (k == 0 || k == last) rough = Mathf.Abs(rough) * 0.5f;
                Vector2 outward = (pr - new Vector2(0f, 1.4f)).normalized * rough;
                if (k == 0 || k == last) outward.y = 0f;
                float slip = plain ? 0f : (Hash(seed + 7, ring, k) - 0.5f) * 0.6f;
                return W3(origin + dir * (s + slip) + right * (pr.x + outward.x), pr.y + outward.y);
            }
            for (int ring = 0; ring < n; ring++)
            {
                float mid = s0 + (ring + 0.5f) * step;
                for (int k = 0; k < last; k++)
                {
                    if (open != null && open(mid, k)) continue;
                    bool upright = Mathf.Abs(prof[k].x - prof[k + 1].x) < 0.01f;
                    bool seam = upright && (k == 1 || k == last - 2);
                    Color albedo = seam ? Coal * (0.8f + 0.6f * Hash(seed + 3, ring, k)) : Rock * (0.82f + 0.36f * Hash(seed + 3, ring, k));
                    Vector3 p0 = P(ring, k), p1 = P(ring + 1, k), p2 = P(ring + 1, k + 1), p3 = P(ring, k + 1);
                    Q(mb, p0, p1, p2, p3, albedo);
                    if (upright) Solid(p0, p1, p2, p3);
                }
                Vector3 fl = P(ring, 0), fr = P(ring, last), fl2 = P(ring + 1, 0), fr2 = P(ring + 1, last);
                Vector3 fm = (fl + fr) * 0.5f, fm2 = (fl2 + fr2) * 0.5f;
                Color floorColor = Ballast * (0.85f + 0.3f * Hash(seed + 5, ring, 0));
                Q(mb, fl, fm, fm2, fl2, floorColor);
                Q(mb, fm, fr, fr2, fm2, floorColor);
                Solid(fl, fr, fr2, fl2);
            }
            if (capEnd)
            {
                Vector3 centre = W3(origin + dir * (s1 + 0.4f), 1.2f);
                for (int k = 0; k < last; k++)
                {
                    Q(mb, P(n, k), P(n, k + 1), centre, centre, Coal * (0.8f + 0.8f * Hash(seed + 9, k, 0)));
                    Solid(P(n, k), P(n, k + 1), centre, centre);
                }
            }
        }

        Material mineMaterial = b.ShaderMaterial("BWM/Mine", "Mine");
        Vector2[] main = Profile(HalfWidth, 2.6f, 3.95f), small = Profile(2f, 1.9f, 2.6f);
        int crossCuts = 0, sets = 0;
        float leastCover = float.MaxValue;

        // ---- the three tunnels ----
        for (int i = 0; i < 3; i++)
        {
            var mb = new MeshBuilder();
            Vector2 dir = Dir(i), right = Right(i);
            float s0 = HallRadius - 0.6f, s1 = Threshold[i] + 1f;
            int rings = Mathf.Max(1, Mathf.RoundToInt((s1 - s0) / 2f));
            float step = (s1 - s0) / rings;
            // cross-cuts: short dead ends to a coal face, alternately left and right, in the lit stretches
            var cuts = new List<(float s, int side)>();
            for (float s = HallRadius + 42f; s < Threshold[i] - 70f; s += 96f)
            {
                float snapped = s0 + Mathf.Round((s - s0) / step) * step;
                if (LitStretch(i, snapped)) cuts.Add((snapped, cuts.Count % 2 == 0 ? -1 : 1));
            }
            bool Open(float mid, int k)
            {
                foreach (var cut in cuts)
                    if (Mathf.Abs(mid - cut.s) < step && (cut.side < 0 ? k <= 2 : k >= main.Length - 4)) return true;
                return false;
            }
            Tube(mb, Hub, dir, s0, s1, main, 100 + i, Open, false);
            Bulkhead(mb, Hub, dir, s0, main, 7f, 6f, Rock);
            foreach (var cut in cuts)
            {
                Vector2 mouth = Point(i, cut.s, cut.side * (HalfWidth - 0.05f));
                Vector2 into = right * cut.side;
                Bulkhead(mb, mouth, into, 0f, small, 2.5f, 3f, Rock * 0.9f);
                Tube(mb, mouth, into, 0f, 14f, small, 200 + i * 20 + crossCuts, null, true);
                Heap(mb, mouth + into * 11.5f + new Vector2(into.y, -into.x) * 0.5f, 1.5f, 1.1f, 300 + crossCuts);
                Heap(mb, mouth + into * 8.5f - new Vector2(into.y, -into.x) * 1.1f, 0.8f, 0.6f, 340 + crossCuts);
                lamps.Add(W3(mouth + into * 7f, 2.1f));
                Lamp(mb, W3(mouth + into * 7f, 2.3f), 60 + crossCuts);
                crossCuts++;
            }
            // timber sets, and a lamp under every other one in the lit stretches
            int n = 0;
            for (float s = HallRadius + 5f; s < Threshold[i] - 3f; s += SetSpacing, n++)
            {
                foreach (int side in new[] { -1, 1 })
                    Box(mb, W3(Point(i, s, side * (HalfWidth - 0.32f)), 0f), new Vector3(0.26f, 2.75f, 0.26f), dir, Timber * (0.8f + 0.3f * Hash(40 + i, n, side + 1)));
                Box(mb, W3(Point(i, s, 0f), 2.75f), new Vector3(HalfWidth * 2f - 0.2f, 0.28f, 0.3f), dir, Timber * (0.8f + 0.3f * Hash(41 + i, n, 0)));
                if (n % 2 == 0 && LitStretch(i, s)) Lamp(mb, W3(Point(i, s, 0f), 2.65f), i * 1000 + n);
                sets++;
            }
            for (float s = HallRadius + 10f; s < CutStart[i] - 2f; s += 10f) leastCover = Mathf.Min(leastCover, heightAt(Point(i, s, 0f)) - Floor - 3.95f);
            b.MeshPiece("Mine Tunnel " + (i + 1), mb.ToMesh(), mineMaterial, false);
        }

        // ---- the hall: a round room under the summit, a pillar of coal left standing in the middle ----
        {
            var mb = new MeshBuilder();
            const int N = 72;
            float[] ringR = { HallRadius, HallRadius, HallRadius, 24f, 19f, 11f, PillarRadius * 0.6f };
            float[] ringY = { 0f, 2.2f, 4.4f, 8f, 11f, 12.6f, HallHeight };
            Vector3 HP(int seg, int ring)
            {
                seg %= N;
                float a = 2f * Mathf.PI * seg / N;
                float rr = ringR[ring] + (ring >= 1 && ring <= 5 ? (Hash(91, seg, ring) - 0.5f) * 0.7f : 0f);
                return W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, ringY[ring]);
            }
            for (int seg = 0; seg < N; seg++)
            {
                float a = 2f * Mathf.PI * (seg + 0.5f) / N;
                bool mouth = false;
                for (int i = 0; i < 3; i++) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, Bearings[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * HallRadius < 4.5f) mouth = true;
                for (int ring = 0; ring < 6; ring++)
                {
                    if (ring < 2 && mouth) continue;
                    Color albedo = ring == 0 ? Coal * (0.8f + 0.6f * Hash(92, seg, ring)) : Rock * (0.8f + 0.4f * Hash(92, seg, ring));
                    Q(mb, HP(seg, ring), HP(seg + 1, ring), HP(seg + 1, ring + 1), HP(seg, ring + 1), albedo);
                    if (ring < 2) Solid(HP(seg, ring), HP(seg + 1, ring), HP(seg + 1, ring + 1), HP(seg, ring + 1));
                }
                // floor, in rings out from the pillar
                float[] floorR = { PillarRadius - 0.5f, 9f, 13f, 17f, 21f, HallRadius + 0.5f };
                float a0 = 2f * Mathf.PI * seg / N, a1 = 2f * Mathf.PI * (seg + 1) / N;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                for (int k = 0; k + 1 < floorR.Length; k++)
                    Q(mb, W3(Hub + d0 * floorR[k], 0f), W3(Hub + d1 * floorR[k], 0f), W3(Hub + d1 * floorR[k + 1], 0f), W3(Hub + d0 * floorR[k + 1], 0f), Ballast * (0.85f + 0.3f * Hash(93, seg, k)));
                Solid(W3(Hub + d0 * floorR[0], 0f), W3(Hub + d1 * floorR[0], 0f), W3(Hub + d1 * floorR[5], 0f), W3(Hub + d0 * floorR[5], 0f));
            }
            float[] pillarY = { 0f, 3f, 6f, 10f, HallHeight };
            for (int k = 0; k < 12; k++)
            {
                float a0 = 2f * Mathf.PI * k / 12f, a1 = 2f * Mathf.PI * (k + 1) / 12f;
                for (int level = 0; level + 1 < pillarY.Length; level++)
                {
                    float r0 = PillarRadius + (Hash(94, k, level) - 0.5f) * 0.6f, r1 = PillarRadius + (Hash(94, (k + 1) % 12, level) - 0.5f) * 0.6f;
                    float r2 = PillarRadius + (Hash(94, (k + 1) % 12, level + 1) - 0.5f) * 0.6f, r3 = PillarRadius + (Hash(94, k, level + 1) - 0.5f) * 0.6f;
                    Vector3 p0 = W3(Hub + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0, pillarY[level]), p1 = W3(Hub + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r1, pillarY[level]);
                    Vector3 p2 = W3(Hub + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r2, pillarY[level + 1]), p3 = W3(Hub + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r3, pillarY[level + 1]);
                    Q(mb, p0, p1, p2, p3, Coal * (0.9f + 0.8f * Hash(95, k, level)));
                    if (level == 0) Solid(p0, p1, p2, p3);
                }
            }
            for (int k = 0; k < hallLamps.Count; k++) Lamp(mb, hallLamps[k] + Vector3.up * 0.2f, 500 + k);
            // coal heaps and crates against the wall between the tunnels
            for (int k = 0; k < 9; k++)
            {
                float a = Bearings[k % 3] + 0.5f + 0.42f * (k / 3) + 0.3f;
                Vector2 at = Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HallRadius - 3.2f);
                if (k % 3 == 1) Box(mb, W3(at, 0f), new Vector3(1.1f, 0.9f, 1.1f), new Vector2(Mathf.Cos(a + 0.4f), Mathf.Sin(a + 0.4f)), Timber * 0.9f);
                else Heap(mb, at, 1.9f + 0.6f * Hash(96, k, 0), 1.3f + 0.5f * Hash(96, k, 1), 400 + k);
            }
            b.MeshPiece("Mine Hall", mb.ToMesh(), mineMaterial, false);
        }

        // ---- the track: one closed line. Out of each tunnel on its right-hand track, round the apron's loop, back
        //      in on the other, round the hall anticlockwise to the next tunnel ----
        var route = new List<Vector2>();
        var stops = new List<Vector2>();
        for (int i = 0; i < 3; i++)
        {
            float thr = Threshold[i];
            route.Add(Point(i, 19f, TrackSide));
            route.Add(Point(i, thr, TrackSide));
            for (float a = 2f; a <= 11f; a += 1.5f) route.Add(Point(i, thr + a, Mathf.Lerp(TrackSide, 5f, Smooth(a / 11f))));
            for (int k = 1; k <= 12; k++) { float phi = Mathf.PI * k / 12f; route.Add(Point(i, thr + 11f + 5f * Mathf.Sin(phi), 5f * Mathf.Cos(phi))); }
            stops.Add(Point(i, thr + 16f, 0f));
            for (float a = 9.5f; a >= 2f; a -= 1.5f) route.Add(Point(i, thr + a, -Mathf.Lerp(TrackSide, 5f, Smooth(a / 11f))));
            route.Add(Point(i, thr, -TrackSide));
            route.Add(Point(i, 19f, -TrackSide));
            float a0 = Bearings[i] + 0.55f, a1 = Bearings[(i + 1) % 3] - 0.55f;
            if (a1 < a0) a1 += 2f * Mathf.PI;
            int steps = Mathf.CeilToInt((a1 - a0) / 0.14f);
            for (int k = 0; k <= steps; k++) { float a = Mathf.Lerp(a0, a1, (float)k / steps); route.Add(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingRadius); }
            float am = (a0 + a1) / 2f;
            stops.Add(Hub + new Vector2(Mathf.Cos(am), Mathf.Sin(am)) * RingRadius);
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var smooth = new List<Vector2>();
            for (int k = 0; k < route.Count; k++)
            {
                Vector2 p = route[k], q = route[(k + 1) % route.Count];
                smooth.Add(Vector2.Lerp(p, q, 0.25f));
                smooth.Add(Vector2.Lerp(p, q, 0.75f));
            }
            route = smooth;
        }
        float trackLength = 0f;
        {
            var mb = new MeshBuilder();
            float sleeperAt = 0f;
            for (int k = 0; k < route.Count; k++)
            {
                Vector2 from = route[k], to = route[(k + 1) % route.Count];
                float length = (to - from).magnitude;
                if (length < 0.01f) continue;
                Vector2 t = (to - from) / length, nrm = new Vector2(t.y, -t.x);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(length / 2.5f));
                for (int piece = 0; piece < pieces; piece++)
                {
                    Vector2 p = Vector2.Lerp(from, to, (float)piece / pieces), q = Vector2.Lerp(from, to, (float)(piece + 1) / pieces);
                    foreach (float side in new[] { -0.45f, 0.45f })
                    {
                        Vector2 pi = p + nrm * (side - 0.04f), po = p + nrm * (side + 0.04f), qi = q + nrm * (side - 0.04f), qo = q + nrm * (side + 0.04f);
                        Q(mb, W3(pi, 0.17f), W3(po, 0.17f), W3(qo, 0.17f), W3(qi, 0.17f), Steel);
                        Q(mb, W3(pi, 0.05f), W3(pi, 0.17f), W3(qi, 0.17f), W3(qi, 0.05f), Steel * 0.6f);
                        Q(mb, W3(po, 0.05f), W3(po, 0.17f), W3(qo, 0.17f), W3(qo, 0.05f), Steel * 0.6f);
                    }
                }
                for (; sleeperAt < length; sleeperAt += 1.1f)
                {
                    Vector2 c = from + t * sleeperAt;
                    Q(mb, W3(c - nrm * 0.75f - t * 0.11f, 0.08f), W3(c + nrm * 0.75f - t * 0.11f, 0.08f), W3(c + nrm * 0.75f + t * 0.11f, 0.08f), W3(c - nrm * 0.75f + t * 0.11f, 0.08f), Tie);
                }
                sleeperAt -= length;
                trackLength += length;
            }
            b.MeshPiece("Mine Track", mb.ToMesh(), mineMaterial, false);
        }
        var lineRoot = new GameObject("Mine Line");
        lineRoot.transform.SetParent(b.Root.transform, false);
        for (int k = 0; k < route.Count; k++)
        {
            var marker = new GameObject("P" + k);
            marker.transform.SetParent(lineRoot.transform, false);
            marker.transform.position = W3(route[k], 0.17f);
        }
        for (int k = 0; k < stops.Count; k++)
        {
            var marker = new GameObject("Stop" + k);
            marker.transform.SetParent(lineRoot.transform, false);
            marker.transform.position = W3(stops[k], 0.17f);
        }

        // ---- the portals: a stone shed over each open cut, and a barred gate until the mine is open ----
        {
            var shed = new MeshBuilder();
            var face = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                Vector2 dir = Dir(i);
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                float frontTop = 0f;
                for (float s = CutStart[i] - 3f; s < Threshold[i] + 1f; s += 2f)
                {
                    float to = Mathf.Min(s + 2f, Threshold[i] + 1f), mid = (s + to) / 2f;
                    float top = Floor + 4.7f;
                    for (float lateral = -5.6f; lateral <= 5.61f; lateral += 1.4f) top = Mathf.Max(top, heightAt(Point(i, Mathf.Min(mid, Threshold[i] - 0.5f), lateral)) + 0.5f);
                    frontTop = top;
                    foreach (int side in new[] { -1, 1 })
                    {
                        Vector2 at = Point(i, mid, side * 4.35f);
                        Kit.AddBox(shed, new Vector3(at.x, Floor - 1.5f, at.y) - root, new Vector3(1.7f, top - Floor + 1.5f, to - s), Stone * (0.9f + 0.15f * Hash(70 + i, (int)s, side + 1)), yaw);
                    }
                    Vector2 over = Point(i, mid, 0f);
                    Kit.AddBox(shed, new Vector3(over.x, Floor + 4.15f, over.y) - root, new Vector3(10.4f, top - Floor - 4.15f, to - s), Stone * (0.92f + 0.12f * Hash(71 + i, (int)s, 0)), yaw);
                }
                Bulkhead(face, Hub, dir, Threshold[i] + 1.02f, main, 5.2f, frontTop - Floor, Stone * 1.05f);
                // a timber lintel and posts round the mouth
                foreach (int side in new[] { -1, 1 })
                    Box(face, W3(Point(i, Threshold[i] + 1.2f, side * (HalfWidth + 0.1f)), 0f), new Vector3(0.4f, 3.3f, 0.4f), dir, Timber);
                Box(face, W3(Point(i, Threshold[i] + 1.2f, 0f), 3.3f), new Vector3(HalfWidth * 2f + 1.2f, 0.45f, 0.45f), dir, Timber * 0.9f);
            }
            b.MeshPiece("Mine Sheds", shed.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"));
            b.MeshPiece("Mine Mouths", face.ToMesh(), mineMaterial, false);

            var gate = new MeshBuilder();
            foreach (int side in new[] { -1, 1 })
                Kit.AddBox(gate, new Vector3(side * (HalfWidth - 0.25f), 0f, 0f), new Vector3(0.3f, 2.9f, 0.3f), Timber);
            for (int k = 0; k < 5; k++) Kit.AddBox(gate, new Vector3(0f, 0.25f + k * 0.55f, 0f), new Vector3(HalfWidth * 2f - 0.2f, 0.34f, 0.12f), Timber * (0.8f + 0.25f * Hash(80, k, 0)));
            Kit.AddBox(gate, new Vector3(0f, 1.2f, -0.1f), new Vector3(1.5f, 0.75f, 0.06f), new Color(0.85f, 0.2f, 0.15f));
            Mesh gateMesh = b.SaveMesh("Mine Gate", gate.ToMesh());
            Material gateMaterial = b.ShaderMaterial("BWM/Snowy", "Mine Timber", m => m.SetFloat("_SnowLine", 2f));
            var gates = new GameObject("Mine Gates");
            gates.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Mine Gate " + (i + 1));
                go.transform.SetParent(gates.transform, false);
                go.transform.position = W3(Point(i, Threshold[i] - 2.5f, 0f), 0f);
                go.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(Dir(i).x, Dir(i).y) * Mathf.Rad2Deg, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = gateMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = gateMaterial;
                go.AddComponent<MeshCollider>().sharedMesh = gateMesh;
            }
        }

        Piece walls = b.MeshPiece("Mine Walls", solid.ToMesh(), mineMaterial);
        walls.Go.GetComponent<MeshRenderer>().enabled = false;

        // ---- the mantrips: low yellow man-cars with a canopy and two benches. Built facing +z, the rail's top at
        //      the origin; Winter Walk moves them (MineDriver) ----
        {
            var car = new MeshBuilder();
            var glow = new MeshBuilder();
            Color yellow = new Color(0.96f, 0.74f, 0.08f), dark = new Color(0.14f, 0.14f, 0.16f), seatColor = new Color(0.3f, 0.22f, 0.16f);
            Kit.AddBox(car, new Vector3(0f, 0.3f, 0f), new Vector3(1.5f, 0.22f, 4.6f), yellow);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Kit.AddBox(car, new Vector3(sx * 0.45f, 0f, sz * 1.5f), new Vector3(0.12f, 0.44f, 0.44f), dark);
            foreach (int sz in new[] { -1, 1 })
            {
                Kit.AddBox(car, new Vector3(0f, 0.52f, sz * 1.95f), new Vector3(1.5f, 0.7f, 0.7f), yellow * 0.95f);
                Kit.AddBox(car, new Vector3(0f, 0.3f, sz * 2.36f), new Vector3(1.2f, 0.3f, 0.14f), dark);                 // buffer
                foreach (int sx in new[] { -1, 1 })
                    Kit.AddGlow(glow, new Vector3(sx * 0.45f, 0.98f, sz * 2.31f), new Vector3(0.2f, 0.2f, 0.04f), 0f, new Color(1f, 0.9f, 0.65f), 0.3f);
                Kit.AddBox(car, new Vector3(0f, 0.52f, sz * 1.5f), new Vector3(1.5f, 0.12f, 0.2f), dark);
            }
            foreach (float bz in new[] { -0.75f, 0.55f })
            {
                Kit.AddBox(car, new Vector3(0f, 0.52f, bz), new Vector3(1.34f, 0.4f, 0.46f), seatColor);
                Kit.AddBox(car, new Vector3(0f, 0.92f, bz - 0.27f), new Vector3(1.34f, 0.5f, 0.08f), seatColor * 0.9f);
            }
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (float pz in new[] { -1.55f, 0f, 1.55f })
                    Kit.AddBox(car, new Vector3(sx * 0.7f, 0.52f, pz), new Vector3(0.07f, 1.7f, 0.07f), dark);
                Kit.AddBox(car, new Vector3(sx * 0.72f, 0.52f, 0f), new Vector3(0.06f, 0.42f, 3.2f), yellow * 0.9f);      // low side rail
                Kit.AddBox(car, new Vector3(sx * 0.72f, 1.05f, -1.05f), new Vector3(0.05f, 0.06f, 1f), dark);            // grab rails
            }
            Kit.AddBox(car, new Vector3(0f, 2.2f, 0f), new Vector3(1.62f, 0.08f, 3.5f), yellow);
            Mesh carMesh = b.SaveMesh("Mantrip", car.ToMesh());
            Mesh glowMesh = b.SaveMesh("Mantrip Lamps", glow.ToMesh());
            Material carMaterial = b.ShaderMaterial("BWM/Snowy", "Mantrip", m => m.SetFloat("_SnowLine", 2f));
            Material glowMaterial = b.ShaderMaterial("BWM/FairyLight", "Mantrip Lamps", m => { m.SetFloat("_Twinkle", 0.05f); m.SetFloat("_Glow", 3f); });
            var tripsRoot = new GameObject("Mine Trips");
            tripsRoot.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < Trips; i++)
            {
                var trip = new GameObject("Mantrip " + i);
                trip.transform.SetParent(tripsRoot.transform, false);
                trip.transform.position = W3(Hub + new Vector2(Mathf.Cos(i * 0.7f), Mathf.Sin(i * 0.7f)) * RingRadius, 0.17f);
                var body = new GameObject("Body");
                body.transform.SetParent(trip.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = carMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = carMaterial;
                body.AddComponent<MeshCollider>().sharedMesh = carMesh;
                var lampsGo = new GameObject("Lamps");
                lampsGo.transform.SetParent(trip.transform, false);
                lampsGo.AddComponent<MeshFilter>().sharedMesh = glowMesh;
                lampsGo.AddComponent<MeshRenderer>().sharedMaterial = glowMaterial;
                int seatNumber = 0;
                foreach (float bz in new[] { -0.75f, 0.55f })
                    foreach (int sx in new[] { -1, 1 })
                    {
                        var seat = new Piece(new GameObject("Seat " + (++seatNumber)));
                        seat.Transform.SetParent(trip.transform, false);
                        seat.Transform.localPosition = new Vector3(0.36f * sx, 0.98f, bz + 0.04f);
                        b.AddScript(seat, "PuzzleSeat");
                        seat.Set("PuzzleSeat", "pose", sx < 0 ? 12 : 13);      // ChairliftLeft, ChairliftRight
                        seat.Set("PuzzleSeat", "clickSize", new Vector3(0.6f, 0.7f, 0.6f));
                    }
            }
        }

        Debug.Log("[BWM] Winter Isle mine: tunnels " + Sdk.F(Threshold[0]) + ", " + Sdk.F(Threshold[1]) + ", " + Sdk.F(Threshold[2]) + " m, open cuts " + Sdk.F(Threshold[0] - CutStart[0]) + ", "
            + Sdk.F(Threshold[1] - CutStart[1]) + ", " + Sdk.F(Threshold[2] - CutStart[2]) + " m, least rock overhead " + Sdk.F(leastCover) + " m; " + crossCuts + " cross-cuts, " + sets + " timber sets, "
            + lamps.Count + " lamps, track " + Sdk.F(trackLength) + " m, " + Trips + " mantrips");
        if (leastCover < 1.5f) Debug.LogWarning("[BWM] Winter Isle mine: a tunnel comes within " + Sdk.F(leastCover) + " m of the surface");
    }
}
