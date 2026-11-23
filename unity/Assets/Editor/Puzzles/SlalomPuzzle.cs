// Slalom: the Called Slalom. A ski race where the line is known only to someone who is not skiing it. A caller in
// a windowless timing hut in the woods beside run 0 reads a lamp map; two skiers race down the run through six rows
// (five if six do not fit) of three coloured lanes (red, gold, blue, in a different order on every row). One lane per
// row is live, dealt at random the moment the row before is passed, and shown only as a lamp on the caller's map, so
// the caller must call it (the hut's intercom plays to speakers on call posts along the course, or walkie-talkies)
// before the skiers get there. Both skiers must be in the called lane at once (one in a 2-player lobby), and every row
// must be reached within its own Row Clock, so nobody can stop and wait. A wrong lane or the clock running out ends the
// run and re-arms the start. All rows and the finish win the gourd. No custom code: Press In Order chains (one pair
// for the start and the finish, three per row), one Timer, Progress Bars, Presence Sensors, an Intercom and Hider
// groups by lobby size, everything gated by the Start Switch (three of them: hut, Call Post 0, Finish Board).
// Spec: Z:\Temp\claude\BigWalkMods\puzzle-specs\Slalom.md (with its review notes); what this file changed from it:
// Slalom.impl.md beside it.
using System;
using System.Collections.Generic;
using System.Linq;
using Bwm;
using UnityEngine;

public sealed class SlalomPuzzle : PuzzleDef
{
    public override string Name => "Slalom";
    public override Vector2 Footprint => new Vector2(14f, 14f);
    // layout-final: the hut at RunCentre(0, 580) - side * (half + 20), in the woods on the left-hand run's outer side
    public override Vector2? FixedSite => Site;
    static readonly Vector2 Site = new Vector2(-1798.5f, -1521f);
    // two r 6 circles that the island levels and keeps clear: the corridor from the hut to Call Post 0
    public override IEnumerable<Vector3> ExtraSites
    {
        get
        {
            Vector2 toRun = -Away(), edge = Site + toRun * (Footprint.y / 2f), p0 = Post0();
            for (int i = 1; i <= 2; i++)
            {
                Vector2 c = Vector2.Lerp(edge, p0, i / 3f);
                yield return new Vector3(c.x, c.y, CorridorRadius);
            }
        }
    }

    public const int Run = 0;
    const float HutAlong = 580f, HutOff = 20f, CorridorRadius = 6f;
    const float StartAlong = 610f, Post0Along = 590f, PostBack = 18f;
    const float LaneLong = 10f, LaneTall = 6f, LaneCentreH = 2.0f, EndLaneWidth = 9f;
    const float CandFrom = 640f, CandTo = 880f, CandStep = 2f, FinishFrom = 880f, FinishTo = 910f;
    // PitchMax was 20 in the spec (brake 4.05 m/s^2 alone); run 0 is 20-28 deg almost everywhere from 640 to 910, so no
    // layout fitted. With SkiDriver's drag (0.25 + 0.012 v^2) a skier braking holds 7-9 m/s up to about 30 deg, so 26 keeps
    // margin; the search still prefers gentler rows (PitchCost) where the spacing allows.
    const float PitchMin = 8f, PitchMax = 26f, GapPitchMax = 26f, GapMin = 36f, GapMax6 = 50f, GapMax5 = 56f;
    const float PitchSoft = 20f, PitchCost = 4f;
    const float WideLane = 4.5f, WidePitch = 5.5f, WideNeeds = 8.75f, NarrowLane = 4.0f, NarrowPitch = 4.75f, NarrowNeeds = 7.5f;
    const float FinishNeeds = 5f;
    // Row Clock per stretch: path length at this speed plus the call (first guesses: tune in the game)
    const float ClockSpeed = 6.0f, CallSeconds = 1.5f;
    // the lamp map on the hut's back wall: its face, and the rows' heights (top down)
    const float MapFace = -1.94f;
    static readonly float[] RowV = { 2.25f, 1.93f, 1.61f, 1.29f, 0.97f, 0.65f };
    static readonly float[] LampU = { -0.80f, -0.35f, 0.10f };      // map columns, the caller's left to right
    const float BarU = 0.70f, TopV = 2.62f, FinishV = 0.38f, ClockU = -0.6f, MicU = 1.45f, MicV = 1.4f;
    const float LampRange = 1.2f, LampIntensity = 0.5f;

    // lane colours: 0 red, 1 gold, 2 blue; each row's left to right as a skier looking downhill sees them
    static readonly int[][] RowOrder = { new[] { 0, 1, 2 }, new[] { 2, 0, 1 }, new[] { 1, 2, 0 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 2, 1, 0 } };
    static readonly string[] ColourName = { "Red", "Gold", "Blue" };
    static readonly Color[] LampColour = { new Color(0.9f, 0.12f, 0.1f), new Color(0.95f, 0.72f, 0.2f), new Color(0.15f, 0.4f, 0.95f) };
    static readonly Color[] FlagColour = { IsleKit.Red, IsleKit.Gold, new Color(0.15f, 0.35f, 0.85f) };
    static readonly string[] LaneTag = { "L", "M", "R" };
    static readonly Color White = new Color(0.94f, 0.95f, 0.96f);
    static readonly Color StartGreen = new Color(0.2f, 0.9f, 0.3f);

    // ---------------- run geometry (static: ExtraSites needs it without a build) ----------------

    // run 0's middle at 'along' metres from the peak, the way it runs there, its right as a skier sees it, and its half-width
    static Vector2 RunAt(float along, out Vector2 dir, out Vector2 side, out float half)
    {
        Vector2 mid = WinterIslePuzzle.RunCentre(Run, along, out half);
        dir = (WinterIslePuzzle.RunCentre(Run, along + 6f, out _) - mid).normalized;
        side = new Vector2(dir.y, -dir.x);
        return mid;
    }
    static float YawOf(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
    // +1 or -1 so that side * Hs points from the run towards the hut (worked out from where the hut is)
    static float Hs()
    {
        Vector2 mid = RunAt(HutAlong, out _, out Vector2 side, out _);
        return Vector2.Dot(Site - mid, side) >= 0f ? 1f : -1f;
    }
    // the way from the run to the hut, which is the hut's +z (its doorway side)
    static Vector2 Away() => (Site - WinterIslePuzzle.RunCentre(Run, HutAlong, out _)).normalized;
    // Call Post 0 above the start lane, on the hut side of the run
    static Vector2 Post0() { Vector2 mid = RunAt(Post0Along, out _, out Vector2 side, out float half); return mid + side * Hs() * (half + 1.5f); }
    static float MinHalf(float along)
    {
        WinterIslePuzzle.RunCentre(Run, along - 5f, out float h0);
        WinterIslePuzzle.RunCentre(Run, along, out float h1);
        WinterIslePuzzle.RunCentre(Run, along + 5f, out float h2);
        return Mathf.Min(h0, Mathf.Min(h1, h2));
    }

    // ---------------- build state ----------------

    PuzzleBuilder b;
    Vector3 site;
    Vector2 site2;
    float hutYaw, hs;
    Material snowy, bare;

    Vector2 Local(Vector2 world) => world - site2;
    float GroundY(Vector2 world) => b.WorldPoint(world.x - site.x, world.y - site.z).y;
    // a mesh vertex 'above' metres over the ground at a world point, relative to a piece standing at 'origin'
    Vector3 V(Vector3 origin, Vector2 world, float above) => new Vector3(world.x - origin.x, GroundY(world) + above - origin.y, world.y - origin.z);
    // the hut frame: local metres about the hut's centre, +z the doorway side (away from the run)
    Vector2 HutW(float x, float z) => site2 + IsleKit.Turn(x, z, hutYaw);
    Vector3 H(float x, float y, float z) { Vector2 t = IsleKit.Turn(x, z, hutYaw); return new Vector3(t.x, y, t.y); }
    void HutBox(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
    {
        Vector2 f = IsleKit.Turn((x0 + x1) / 2f, (z0 + z1) / 2f, hutYaw);
        Kit.AddBox(mb, new Vector3(f.x, y0, f.y), new Vector3(x1 - x0, y1 - y0, z1 - z0), c, hutYaw);
    }
    // a part in the hut frame, its height above the root's level (the levelled site)
    Piece HutPart(string prefab, float x, float z, float h, float yaw)
    {
        Vector2 p = IsleKit.Turn(x, z, hutYaw);
        return b.Part(prefab, p.x, p.y, h, hutYaw + yaw, false);
    }
    // a part on the map, at u (the caller's right) and v (height), 'depth' in front of the board's face, facing into the hut
    Piece MapPart(string prefab, float u, float v, float depth) => HutPart(prefab, -u, MapFace + depth, v, 0f);
    // a part on the course, its height above the ground there
    Piece CoursePart(string prefab, Vector2 world, float h, float yaw)
    {
        Vector2 l = Local(world);
        return b.Part(prefab, l.x, l.y, h, yaw);
    }

    // a block from eight corners (four low, four high, counter-clockwise seen from above), as Kit.AddBox draws its faces
    static void Hexa(MeshBuilder mb, Vector3[] low, Vector3[] high, Color color)
    {
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }
    // a flat piece seen from both sides
    static void TwoSided(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Color color)
    {
        mb.Tri(a, b2, c, color);
        mb.Tri(a, c, b2, color);
    }

    // ---------------- the row search ----------------

    sealed class Cand
    {
        public float along, half, pitch, lane, step;
        public bool ok;
        public string why;
    }

    float MidHeight(float along)
    {
        Vector2 m = RunAt(along, out _, out _, out _);
        return b.Ground.At(m.x, m.y, out float y) ? y : float.NaN;
    }
    // mean pitch (degrees, positive downhill) of the run's middle from a0 down to a1
    float Slope(float a0, float a1)
    {
        Vector2 m0 = RunAt(a0, out _, out _, out _), m1 = RunAt(a1, out _, out _, out _);
        float h0 = MidHeight(a0), h1 = MidHeight(a1);
        if (float.IsNaN(h0) || float.IsNaN(h1)) return float.NaN;
        return Mathf.Atan2(h0 - h1, (m1 - m0).magnitude) * Mathf.Rad2Deg;
    }
    Cand Measure(float along, bool finish)
    {
        var c = new Cand { along = along, half = MinHalf(along), pitch = Slope(along - 10f, along + 10f) };
        if (finish)
        {
            c.lane = EndLaneWidth;
            if (c.half < FinishNeeds) c.why = "narrow";
        }
        else if (c.half >= WideNeeds) { c.lane = WideLane; c.step = WidePitch; }
        else if (c.half >= NarrowNeeds) { c.lane = NarrowLane; c.step = NarrowPitch; }
        else c.why = "narrow";
        if (c.why == null)
        {
            if (float.IsNaN(c.pitch)) c.why = "no ground";
            else if (c.pitch > PitchMax) c.why = "steep";
            else if (c.pitch < PitchMin) c.why = "gentle";
        }
        c.ok = c.why == null;
        return c;
    }
    bool GapOk(float a0, float a1, float gapMax)
    {
        float gap = a1 - a0;
        if (gap < GapMin - 0.01f || gap > gapMax + 0.01f) return false;
        float s = Slope(a0, a1);
        return !float.IsNaN(s) && Mathf.Abs(s) <= GapPitchMax;
    }
    // n rows between the start and one of the finishes, every gap within limits, closest to even spacing
    bool Choose(int n, float gapMax, List<Cand> rows, List<Cand> finishes, out float[] alongs, out Cand finish)
    {
        alongs = null; finish = null;
        float bestCost = float.MaxValue;
        int m = rows.Count;
        foreach (Cand f in finishes)
        {
            float ideal = (f.along - StartAlong) / (n + 1);
            var dp = new float[n, m];
            var prev = new int[n, m];
            for (int k = 0; k < n; k++) for (int i = 0; i < m; i++) { dp[k, i] = float.MaxValue; prev[k, i] = -1; }
            for (int i = 0; i < m; i++)
                if (GapOk(StartAlong, rows[i].along, gapMax)) dp[0, i] = Sq(rows[i].along - StartAlong - ideal) + Steep(rows[i]);
            for (int k = 1; k < n; k++)
                for (int i = 0; i < m; i++)
                    for (int j = 0; j < i; j++)
                    {
                        if (dp[k - 1, j] == float.MaxValue || !GapOk(rows[j].along, rows[i].along, gapMax)) continue;
                        float cost = dp[k - 1, j] + Sq(rows[i].along - rows[j].along - ideal) + Steep(rows[i]);
                        if (cost < dp[k, i]) { dp[k, i] = cost; prev[k, i] = j; }
                    }
            for (int i = 0; i < m; i++)
            {
                if (dp[n - 1, i] == float.MaxValue || !GapOk(rows[i].along, f.along, gapMax)) continue;
                float total = dp[n - 1, i] + Sq(f.along - rows[i].along - ideal);
                if (total >= bestCost) continue;
                bestCost = total;
                finish = f;
                alongs = new float[n];
                for (int k = n - 1, at = i; k >= 0; at = prev[k, at], k--) alongs[k] = rows[at].along;
            }
        }
        return alongs != null;
    }
    static float Sq(float x) => x * x;
    static float Steep(Cand c) => Sq(Mathf.Max(0f, c.pitch - PitchSoft)) * PitchCost;
    // consecutive candidates with the same verdict, as "640-668 narrow (half 6.1-7.2, pitch 9.0-12.4)"
    static string Stretches(List<Cand> all)
    {
        var parts = new List<string>();
        for (int i = 0; i < all.Count;)
        {
            int j = i;
            while (j + 1 < all.Count && all[j + 1].why == all[i].why) j++;
            var run = all.GetRange(i, j - i + 1);
            parts.Add(Sdk.F(all[i].along) + "-" + Sdk.F(all[j].along) + " " + (all[i].why ?? "ok") + " (half " + Sdk.F(run.Min(c => c.half)) + "-" + Sdk.F(run.Max(c => c.half))
                + ", pitch " + Sdk.F(run.Min(c => c.pitch)) + "-" + Sdk.F(run.Max(c => c.pitch)) + ")");
            i = j + 1;
        }
        return string.Join("; ", parts);
    }

    // ---------------- course pieces ----------------

    // a lane's marks on a mesh whose piece stands at 'origin': pennants in its colour every 2.5 m down both edges, and
    // (with banners) an entry and an exit banner on two poles each. No colliders: a skier clipping a pole is never stopped.
    void LaneMarks(MeshBuilder mb, Vector3 origin, Vector2 centre, Vector2 dir, Vector2 side, float yaw, float width, Color colour, bool banners)
    {
        for (int e = -1; e <= 1; e += 2)
            for (int t = -1; t <= 1; t++)
            {
                Vector2 at = centre + side * (e * width / 2f) + dir * (t * 2.5f);
                Kit.AddBox(mb, V(origin, at, -0.1f), new Vector3(0.03f, 1.0f, 0.03f), IsleKit.DarkWood, yaw);
                Kit.AddBox(mb, V(origin, at + dir * 0.16f, 0.66f), new Vector3(0.02f, 0.22f, 0.3f), colour, yaw);
            }
        if (!banners) return;
        for (int end = -1; end <= 1; end += 2)
        {
            Vector2 mid = centre + dir * (end * LaneLong / 2f);
            for (int e = -1; e <= 1; e += 2)
                Kit.AddBox(mb, V(origin, mid + side * (e * width / 2f), -0.1f), new Vector3(0.07f, 2.3f, 0.07f), colour, yaw);
            Kit.AddBox(mb, V(origin, mid, 1.35f), new Vector3(width, 0.5f, 0.04f), colour, yaw);
        }
    }

    // a Presence Sensor box over a lane: 'width' across, 6 m tall, 10 m down the run, its centre 2 m above the ground
    Component LaneSensor(string name, Vector2 world, float yaw, float width, int required, Piece group)
    {
        Piece zone = CoursePart("Presence Sensor", world, LaneCentreH, yaw).Named(name);
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, LaneTall, LaneLong);
        box.center = Vector3.zero;
        zone.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", required);
        zone.Under(group);
        return zone.Component("PuzzlePresenceSensor");
    }

    Component Chain(string name, Piece group, Component[] lights, Component[] buttons, Component bar)
    {
        Piece holder = b.Empty(name, 0f, 0f, 0.5f).Under(group);
        Component c = b.AddScript(holder, "PuzzlePressInOrder");
        Sdk.Add(c, "lights", lights);
        Sdk.Add(c, "buttons", buttons);
        Sdk.Add(c, "progressBars", bar);
        Sdk.Set(c, "rounds", 1);
        return c;
    }

    // a call post above row p: a timber post, a crossboard with a Clock Bar, a speaker on top, a small roof; facing uphill
    void CallPost(int p, Vector2 at, float yaw, List<UnityEngine.Object> clockBars, List<UnityEngine.Object> speakers)
    {
        var mb = new MeshBuilder();
        Vector2 l = Local(at);
        Vector3 origin = b.WorldPoint(l.x, l.y);
        Vector3 P(float x, float y, float z) { Vector2 t = IsleKit.Turn(x, z, yaw); return new Vector3(t.x, y, t.y); }
        Kit.AddBox(mb, P(0f, -0.3f, 0f), new Vector3(0.2f, 3.0f, 0.2f), IsleKit.Wood, yaw);
        Kit.AddBox(mb, P(0f, 1.9f, 0.14f), new Vector3(1.9f, 0.4f, 0.08f), IsleKit.Board, yaw);
        Kit.AddBox(mb, P(0f, 2.7f, 0.08f), new Vector3(2.1f, 0.1f, 0.55f), IsleKit.DarkWood, yaw);
        b.MeshPiece("Call Post " + p, mb.ToMesh(), snowy).Transform.position = origin;
        Vector2 bar = at + IsleKit.Turn(0f, 0.21f, yaw), spk = at + IsleKit.Turn(0f, 0.12f, yaw);
        clockBars.Add(CoursePart("Progress Bar", bar, 2.1f, yaw).Named("Clock Bar " + p).Scaled(0.5f, 0.5f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar"));
        speakers.Add(CoursePart("Intercom Speaker", spk, 2.31f, yaw).Named("Call Speaker " + p).Component("PuzzleIntercomSpeaker"));
    }

    // ---------------- the build ----------------

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        site2 = new Vector2(site.x, site.z);
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        hs = Hs();
        Vector2 away = Away();
        hutYaw = YawOf(away);
        {
            Vector2 mid = RunAt(HutAlong, out _, out Vector2 side, out float half);
            Vector2 computed = mid + side * hs * (half + HutOff);
            Debug.Log("[BWM] Slalom: hut at " + Sdk.F(site.x) + ", " + Sdk.F(site.z) + " (RunCentre(0, 580) + side * hs * (half + 20) is "
                + Sdk.F(computed.x) + ", " + Sdk.F(computed.y) + ": " + Sdk.F((computed - site2).magnitude) + " m off), hs " + Sdk.F(hs) + ", hut yaw " + Sdk.F(hutYaw)
                + ", " + Sdk.F(Vector2.Dot(site2 - mid, side * hs) - half) + " m from the run's edge");
        }
        b.ClearGround(14f, 14f);
        Vector2 dbg = IsleKit.Turn(1.15f, 3.6f, hutYaw);
        b.DebugTeleport(dbg.x, dbg.y);

        // ======== where the rows go: a search over run 0 ========
        var cands = new List<Cand>();
        for (float a = CandFrom; a <= CandTo + 0.01f; a += CandStep) cands.Add(Measure(a, false));
        var finishCands = new List<Cand>();
        for (float a = FinishFrom; a <= FinishTo + 0.01f; a += CandStep) finishCands.Add(Measure(a, true));
        List<Cand> usable = cands.Where(c => c.ok).ToList(), finishes = finishCands.Where(c => c.ok).ToList();
        int n = 6;
        if (!Choose(6, GapMax6, usable, finishes, out float[] rowAlong, out Cand fin))
        {
            n = 5;
            if (!Choose(5, GapMax5, usable, finishes, out rowAlong, out fin))
                throw new Exception("[BWM] Slalom: no row layout fits run 0 (widest candidates: "
                    + string.Join(", ", cands.OrderByDescending(c => c.half).Take(6).Select(c => Sdk.F(c.along) + " half " + Sdk.F(c.half) + " pitch " + Sdk.F(c.pitch)))
                    + "). Rows " + Stretches(cands) + ". Finishes " + Stretches(finishCands) + ". Row rule: half >= " + Sdk.F(NarrowNeeds) + " over the lane, pitch "
                    + Sdk.F(PitchMin) + "-" + Sdk.F(PitchMax) + " deg; gaps " + Sdk.F(GapMin) + "-" + Sdk.F(GapMax6) + " m (5 rows: " + Sdk.F(GapMax5) + "), mean pitch <= " + Sdk.F(GapPitchMax));
        }
        float finishAlong = fin.along;

        // each row's geometry: its middle, way, right, yaw, lane width and pitch, and how far it swings off the middle
        var rowMid = new Vector2[n]; var rowDir = new Vector2[n]; var rowSide = new Vector2[n];
        var rowYaw = new float[n]; var rowLane = new float[n]; var rowStep = new float[n]; var rowLat = new float[n];
        for (int r = 0; r < n; r++)
        {
            Cand c = cands.First(x => Mathf.Abs(x.along - rowAlong[r]) < 0.01f);
            rowMid[r] = RunAt(rowAlong[r], out rowDir[r], out rowSide[r], out _);
            rowYaw[r] = YawOf(rowDir[r]);
            rowLane[r] = c.lane; rowStep[r] = c.step;
            float s = r % 2 == 0 ? -1f : 1f;
            rowLat[r] = s * Mathf.Max(0f, c.half - (c.step + c.lane / 2f + 0.5f));
        }
        // the Row Clock for each stretch: start to row 1, row to row, the last row to the finish
        var clock = new float[n + 1];
        for (int k = 0; k <= n; k++)
        {
            float a0 = k == 0 ? StartAlong : rowAlong[k - 1], a1 = k == n ? finishAlong : rowAlong[k];
            float l0 = k == 0 ? 0f : rowLat[k - 1], l1 = k == n ? 0f : rowLat[k];
            float path = Mathf.Sqrt(Sq(a1 - a0) + Sq(l1 - l0));
            clock[k] = Mathf.Round((path / ClockSpeed + CallSeconds) * 2f) / 2f;
        }
        for (int r = 0; r < n; r++)
        {
            Cand c = cands.First(x => Mathf.Abs(x.along - rowAlong[r]) < 0.01f);
            Debug.Log("[BWM] Slalom row " + (r + 1) + " along " + Sdk.F(rowAlong[r]) + " half " + Sdk.F(c.half) + " pitch " + Sdk.F(c.pitch) + " lanes " + Sdk.F(c.lane)
                + " gap " + Sdk.F(rowAlong[r] - (r == 0 ? StartAlong : rowAlong[r - 1])) + " lateral " + Sdk.F(rowLat[r]) + " clock " + Sdk.F(clock[r]));
        }
        Debug.Log("[BWM] Slalom: " + n + " rows, start " + Sdk.F(StartAlong) + ", finish " + Sdk.F(finishAlong) + " (half " + Sdk.F(fin.half) + " pitch " + Sdk.F(fin.pitch)
            + "), last gap " + Sdk.F(finishAlong - rowAlong[n - 1]) + ", finish clock " + Sdk.F(clock[n]));

        // ======== the timing hut (hut frame: interior 4 x 4, walls 0.2, 3.0 high, doorway in the +z wall away from the run) ========
        var hut = new MeshBuilder();
        HutBox(hut, -2.25f, 2.25f, -0.6f, 0.05f, -2.25f, 2.25f, IsleKit.DarkWood);          // floor on a timber pad
        HutBox(hut, -2.2f, 2.2f, -0.6f, 3.0f, -2.2f, -2.0f, IsleKit.Wood);                 // back wall (run side), unbroken
        HutBox(hut, -2.2f, -2.0f, -0.6f, 3.0f, -2.0f, 2.0f, IsleKit.Wood);                 // west wall
        HutBox(hut, 2.0f, 2.2f, -0.6f, 3.0f, -2.0f, 2.0f, IsleKit.Wood);                   // east wall
        HutBox(hut, -2.2f, 0.6f, -0.6f, 3.0f, 2.0f, 2.2f, IsleKit.Wood);                   // front wall, west of the doorway
        HutBox(hut, 1.7f, 2.2f, -0.6f, 3.0f, 2.0f, 2.2f, IsleKit.Wood);                    // front wall, east of it
        HutBox(hut, 0.6f, 1.7f, 2.1f, 3.0f, 2.0f, 2.2f, IsleKit.Wood);                     // lintel: the doorway is 1.1 x 2.1
        // the porch screen (z 0.7..1.0, x -0.3 to the east wall, 2.6 m tall): no straight line from outside reaches the map
        // (the worst, doorway (0.6, 2.0) to the map's west edge, crosses z 1.0 at x 0.04 and z 0.7 at x -0.13; a line
        // passing west of x -0.3 hits the west wall first), with a 1.0 m gap behind the doorway and a 1.7 m passage
        HutBox(hut, -0.3f, 2.0f, 0.0f, 2.6f, 0.7f, 1.0f, IsleKit.DarkWood);
        HutBox(hut, 0.5f, 1.8f, -0.6f, 0.02f, 2.2f, 2.9f, IsleKit.DarkWood);               // doorstep
        // the roof: two slabs from eaves at 3.0 m (z +-2.5) to the ridge at 3.8 m, and the gable ends
        Hexa(hut, new[] { H(-2.5f, 3.0f, -2.5f), H(2.5f, 3.0f, -2.5f), H(2.5f, 3.8f, 0f), H(-2.5f, 3.8f, 0f) },
                  new[] { H(-2.5f, 3.14f, -2.5f), H(2.5f, 3.14f, -2.5f), H(2.5f, 3.94f, 0f), H(-2.5f, 3.94f, 0f) }, IsleKit.DarkWood);
        Hexa(hut, new[] { H(-2.5f, 3.8f, 0f), H(2.5f, 3.8f, 0f), H(2.5f, 3.0f, 2.5f), H(-2.5f, 3.0f, 2.5f) },
                  new[] { H(-2.5f, 3.94f, 0f), H(2.5f, 3.94f, 0f), H(2.5f, 3.14f, 2.5f), H(-2.5f, 3.14f, 2.5f) }, IsleKit.DarkWood);
        Hexa(hut, new[] { H(-2.2f, 3.0f, -2.2f), H(2.2f, 3.0f, -2.2f), H(2.2f, 3.0f, 2.2f), H(-2.2f, 3.0f, 2.2f) },
                  new[] { H(-2.2f, 3.78f, 0f), H(2.2f, 3.78f, 0f), H(2.2f, 3.78f, 0f), H(-2.2f, 3.78f, 0f) }, IsleKit.Wood);
        b.MeshPiece("Timing Hut", hut.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        // the room as an "Indoors ..." box (Winter Walk's convention for where it must not snow)
        Piece indoors = b.Empty("Indoors Slalom Hut", 0f, 0f, 1.5f, false);
        indoors.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, hutYaw, 0f);
        indoors.Transform.localScale = new Vector3(4f, 3f, 4f);

        // the lamp map on the back wall: a painted board, a white ribbon for the run (uphill at the top), a swatch of
        // each lane's colour behind its lamp
        var map = new MeshBuilder();
        HutBox(map, -1.6f, 1.6f, 0.25f, 2.75f, -2.0f, MapFace, IsleKit.Board);
        HutBox(map, -0.4f, 1.1f, 0.48f, 2.44f, MapFace, MapFace + 0.005f, White);          // u -1.1..0.4 (u = -x)
        b.MeshPiece("Lamp Map", map.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        b.Empty("Room Light 1 FFE2B8 2.2 0.45", HutW(-0.8f, 0f).x - site.x, HutW(-0.8f, 0f).y - site.z, 2.6f, false);

        // the lamps: column from the lane's place across the run, mirrored (the map looks up the mountain, so the
        // skiers' right lane is the caller's left column), each lamp in its lane's colour
        var lamps = new Component[n, 3];
        var swatches = new MeshBuilder();
        for (int r = 0; r < n; r++)
            for (int k = 0; k < 3; k++)
            {
                int colour = RowOrder[r][k];
                int column = 2 - k;      // lanes are laid L, M, R along the skiers' right; the map's columns run the other way
                float u = LampU[column], v = RowV[r];
                HutBox(swatches, -u - 0.15f, -u + 0.15f, v - 0.15f, v + 0.15f, MapFace + 0.005f, MapFace + 0.012f, FlagColour[colour]);
                lamps[r, k] = MapPart("Light 8", u, v, 0.05f).Named("Lamp " + (r + 1) + "." + LaneTag[k] + " " + ColourName[colour])
                    .Set("PuzzleLight", "color", LampColour[colour]).Set("PuzzleLight", "range", LampRange).Set("PuzzleLight", "intensity", LampIntensity)
                    .Component("PuzzleLight");
            }
        b.MeshPiece("Lamp Swatches", swatches.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // the Row Clock, the bars and the mic
        Piece clockPiece = MapPart("Timer", ClockU, TopV, 0.02f).Named("Row Clock").Scaled(0.6f * PuzzleBuilder.TimerScale, 0.6f * PuzzleBuilder.TimerScale, 0.6f * PuzzleBuilder.TimerScale);
        clockPiece.Set("PuzzleTimer", "duration", clock[0]);
        Component rowClock = clockPiece.Component("PuzzleTimer");
        Component startBar = MapPart("Progress Bar", BarU, TopV, 0.03f).Named("Start Bar").Scaled(0.25f, 0.4f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        var rowBars = new Component[n];
        for (int r = 0; r < n; r++)
            rowBars[r] = MapPart("Progress Bar", BarU, RowV[r], 0.03f).Named("Row Bar " + (r + 1)).Scaled(0.25f, 0.4f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        Component finishBar = MapPart("Progress Bar", BarU, FinishV, 0.03f).Named("Finish Bar").Scaled(0.25f, 0.4f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        Piece mic = MapPart("Intercom Mic", MicU, MicV, 0.02f).Named("Caller Mic");

        // the start corner outside the doorway, facing it: the hut's start switch, live lamp, the gourd's vice, the pillow, the confetti
        Vector2 corner = IsleKit.Turn(0f, 4.6f, hutYaw);
        IsleKit.StartCorner(b, corner.x, corner.y, hutYaw + 180f, true, 0f, false);
        // Christmas lights round the eaves
        var eaves = new List<Vector3>();
        foreach (Vector2 q in new[] { new Vector2(-2.35f, -2.35f), new Vector2(2.35f, -2.35f), new Vector2(2.35f, 2.35f), new Vector2(-2.35f, 2.35f), new Vector2(-2.35f, -2.35f) })
        {
            Vector2 t = IsleKit.Turn(q.x, q.y, hutYaw);
            eaves.Add(new Vector3(t.x, 2.95f, t.y));
        }
        Kit.LightString(b, "Hut Eaves Lights", eaves, 0.25f, 0.5f, false, false, 31);

        // ======== the course ========
        var clockBars = new List<UnityEngine.Object>();
        var speakers = new List<UnityEngine.Object>();

        // Call Post 0 above the start: a board on posts facing uphill with the start switch, its live lamp, the Clock Bar,
        // a speaker on its roof and a course card pointing to the hut
        Vector2 p0 = Post0();
        RunAt(Post0Along, out Vector2 dir0, out _, out _);
        float yaw0 = YawOf(dir0) + 180f;
        Vector2 p0l = Local(p0);
        Piece post0 = IsleKit.BoardOnPosts(b, "Call Post 0", p0l.x, p0l.y, yaw0, 2.0f, 0.9f, 1.9f, IsleKit.Board);
        Vector2 BP(Vector2 o, float yaw, float x, float z) => o + IsleKit.Turn(x, z, yaw);
        // the viewer stands uphill looking down the run: the board's +x is their left; the hut is on side * hs, their right when hs is +1
        float hutX = -hs;
        CoursePart("Switch Start", BP(p0, yaw0, -hutX * 0.6f, IsleKit.FaceZ + 0.03f), 1.3f, yaw0).Named("Start Switch (start)");
        CoursePart("Live Light", BP(p0, yaw0, -hutX * 0.6f, IsleKit.FaceZ), 1.52f, yaw0).Named("Live Light (start)");
        clockBars.Add(CoursePart("Progress Bar", BP(p0, yaw0, 0f, IsleKit.FaceZ + 0.03f), 1.75f, yaw0).Named("Clock Bar 0").Scaled(0.5f, 0.5f, 1f)
            .Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar"));
        speakers.Add(CoursePart("Intercom Speaker", BP(p0, yaw0, 0f, 0.1f), 2.33f, yaw0).Named("Call Speaker 0").Component("PuzzleIntercomSpeaker"));
        {
            // the course card: the run as a white ribbon, the hut as a brown block on its side, a gold arrow between
            var card = new MeshBuilder();
            Vector3 o = post0.Transform.position;
            Vector3 C(float x, float y, float z) { Vector2 t = IsleKit.Turn(x, z, yaw0); return new Vector3(t.x, y, t.y); }
            void CardBox(float x0, float x1, float y0, float y1, float z0, float z1, Color c)
            {
                Vector2 f = IsleKit.Turn((x0 + x1) / 2f, (z0 + z1) / 2f, yaw0);
                Kit.AddBox(card, new Vector3(f.x, y0, f.y), new Vector3(Mathf.Abs(x1 - x0), y1 - y0, z1 - z0), c, yaw0);
            }
            float s = hutX;
            CardBox(s * 0.05f, s * 0.9f, 0.95f, 1.55f, 0.06f, 0.075f, IsleKit.Cream * 0.35f + IsleKit.Board * 0.65f);
            CardBox(s * 0.12f, s * 0.28f, 0.98f, 1.52f, 0.075f, 0.085f, White);
            for (int r = 0; r < n; r++) CardBox(s * 0.1f, s * 0.3f, 1.47f - r * 0.08f, 1.49f - r * 0.08f, 0.085f, 0.09f, FlagColour[RowOrder[r][1]]);
            CardBox(s * 0.66f, s * 0.84f, 1.16f, 1.34f, 0.075f, 0.09f, IsleKit.Wood);
            CardBox(s * 0.72f, s * 0.78f, 1.16f, 1.26f, 0.09f, 0.095f, IsleKit.DarkWood);
            CardBox(s * 0.32f, s * 0.54f, 1.235f, 1.265f, 0.075f, 0.09f, IsleKit.Gold);
            TwoSided(card, C(s * 0.62f, 1.25f, 0.09f), C(s * 0.53f, 1.31f, 0.09f), C(s * 0.53f, 1.19f, 0.09f), IsleKit.Gold);
            b.MeshPiece("Course Card", card.ToMesh(), bare, false).Transform.position = o;
        }

        // the start lane (along 610) and its arch at 605
        Vector2 startMid = RunAt(StartAlong, out Vector2 startDir, out Vector2 startSide, out _);
        float startYaw = YawOf(startDir);
        {
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(startMid).x, Local(startMid).y);
            LaneMarks(mb, o, startMid, startDir, startSide, startYaw, EndLaneWidth, IsleKit.Green, false);
            Vector2 arch = startMid - startDir * (LaneLong / 2f);
            for (int e = -1; e <= 1; e += 2) Kit.AddBox(mb, V(o, arch + startSide * (e * 4.7f), -0.1f), new Vector3(0.12f, 2.7f, 0.12f), IsleKit.Wood, startYaw);
            Kit.AddBox(mb, V(o, arch, 2.05f), new Vector3(9.4f, 0.5f, 0.05f), IsleKit.Green, startYaw);
            b.MeshPiece("Start Arch", mb.ToMesh(), bare, false).Transform.position = o;
            var tops = new List<Vector3>();
            foreach (int e in new[] { -1, 1 }) { Vector2 q = Local(arch + startSide * (e * 4.7f)); tops.Add(new Vector3(q.x, 2.6f, q.y)); }
            Kit.LightString(b, "Start Arch Lights", tops, 0.15f, 0.5f, true, false, 32);
        }
        Component startLamp = CoursePart("Light 8", startMid - startDir * (LaneLong / 2f + 0.08f), 2.4f, startYaw + 180f).Named("Start Lamp")
            .Set("PuzzleLight", "color", StartGreen).Set("PuzzleLight", "range", 6f).Component("PuzzleLight");

        // the rows: lanes with banners and pennants, and a call post 18 m above each
        var laneCentre = new Vector2[n, 3];
        for (int r = 0; r < n; r++)
        {
            Vector2 centre = rowMid[r] + rowSide[r] * rowLat[r];
            for (int k = 0; k < 3; k++) laneCentre[r, k] = centre + rowSide[r] * ((k - 1) * rowStep[r]);
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(centre).x, Local(centre).y);
            for (int k = 0; k < 3; k++)
                LaneMarks(mb, o, laneCentre[r, k], rowDir[r], rowSide[r], rowYaw[r], rowLane[r], FlagColour[RowOrder[r][k]], true);
            b.MeshPiece("Row " + (r + 1) + " Lanes", mb.ToMesh(), bare, false).Transform.position = o;

            Vector2 postMid = RunAt(rowAlong[r] - PostBack, out Vector2 pd, out Vector2 ps, out float ph);
            CallPost(r + 1, postMid + ps * hs * (ph + 1.5f), YawOf(pd) + 180f, clockBars, speakers);
        }

        // the finish lane and its arch at F - 5, the finish lamp, confetti, and the Finish Board below
        Vector2 finMid = RunAt(finishAlong, out Vector2 finDir, out Vector2 finSide, out _);
        float finYaw = YawOf(finDir);
        {
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(finMid).x, Local(finMid).y);
            LaneMarks(mb, o, finMid, finDir, finSide, finYaw, EndLaneWidth, IsleKit.Red, false);
            Vector2 arch = finMid - finDir * (LaneLong / 2f);
            for (int e = -1; e <= 1; e += 2) Kit.AddBox(mb, V(o, arch + finSide * (e * 4.7f), -0.1f), new Vector3(0.12f, 3.1f, 0.12f), IsleKit.Wood, finYaw);
            Kit.AddBox(mb, V(o, arch, 2.5f), new Vector3(9.4f, 0.5f, 0.05f), IsleKit.Red, finYaw);
            b.MeshPiece("Finish Arch", mb.ToMesh(), bare, false).Transform.position = o;
            var tops = new List<Vector3>();
            foreach (int e in new[] { -1, 1 }) { Vector2 q = Local(arch + finSide * (e * 4.7f)); tops.Add(new Vector3(q.x, 3.0f, q.y)); }
            Kit.LightString(b, "Finish Arch Lights", tops, 0.15f, 0.5f, true, false, 33);
        }
        Component finishLamp = CoursePart("Light 8", finMid - finDir * (LaneLong / 2f + 0.08f), 2.4f, finYaw + 180f).Named("Finish Lamp")
            .Set("PuzzleLight", "color", StartGreen).Set("PuzzleLight", "range", 6f).Component("PuzzleLight");
        CoursePart("Confetti", finMid + finDir * 5f, 0.2f, 0f).Named("Finish Confetti");
        float boardAlong = finishAlong + 14f > 920f ? finishAlong + 8f : finishAlong + 14f;
        Vector2 fbMid = RunAt(boardAlong, out Vector2 fbDir, out Vector2 fbSide, out float fbHalf);
        Vector2 fb = fbMid + fbSide * hs * (fbHalf + 1.5f);
        float fbYaw = YawOf(fbDir);      // faces downhill, for arrivals at the bottom
        Vector2 fbl = Local(fb);
        IsleKit.BoardOnPosts(b, "Finish Board", fbl.x, fbl.y, fbYaw, 1.4f, 0.9f, 1.7f, IsleKit.Board);
        CoursePart("Switch Start", BP(fb, fbYaw, 0f, IsleKit.FaceZ + 0.03f), 1.3f, fbYaw).Named("Start Switch (finish)");
        CoursePart("Live Light", BP(fb, fbYaw, 0f, IsleKit.FaceZ), 1.52f, fbYaw).Named("Live Light (finish)");

        // the Caller Mic plays on every call post's speaker; the Row Clock drains every post's Clock Bar
        mic.Add("PuzzleIntercom", "speakers", speakers.ToArray());
        Sdk.Add(rowClock, "progressBars", clockBars.ToArray());

        // ======== the flag line: the way between the hut's doorway and Call Post 0 ========
        {
            var path = new List<Vector2> { HutW(2.0f, 3.0f), HutW(3.2f, 0.6f), HutW(3.2f, -2.9f) };
            Vector2 from = path[path.Count - 1], to = p0 + (path[path.Count - 1] - p0).normalized * 2.0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / 6f));
            for (int i = 1; i <= steps; i++) path.Add(Vector2.Lerp(from, to, (float)i / steps));
            var mb = new MeshBuilder();
            var posts = new List<Vector3>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 q = path[i];
                Kit.AddBox(mb, V(site, q, -0.2f), new Vector3(0.08f, 1.9f, 0.08f), IsleKit.Wood);
                Vector2 next = i + 1 < path.Count ? path[i + 1] : q + (q - path[i - 1]);
                float pyaw = YawOf((next - q).normalized);
                for (int k = 0; k < 3; k++)
                    Kit.AddBox(mb, V(site, q + (next - q).normalized * 0.2f, 1.25f + k * 0.13f), new Vector3(0.02f, 0.1f, 0.32f), FlagColour[(i + k) % 3], pyaw);
                Vector2 lq = Local(q);
                posts.Add(new Vector3(lq.x, 1.62f, lq.y));
            }
            b.MeshPiece("Flag Line", mb.ToMesh(), bare, false).Transform.position = site;
            Kit.LightString(b, "Flag Line Lights", posts, 0.3f, 0.5f, true, false, 34);
        }

        // ======== the lobby groups: sensors and Press In Order chains, one copy per group ========
        string[] groupName = { "Two Players", "Three or Four Players" };
        string[] tag = { " (2)", " (3-4)" };
        int[] required = { 1, 2 };
        var startChain = new Component[2];
        var finishChain = new Component[2];
        var rowChain = new Component[2, n];
        for (int g = 0; g < 2; g++)
        {
            Piece group = IsleKit.Lobby(b, groupName[g], g == 0, g == 1, g == 1);
            Component startLane = LaneSensor("Start Lane" + tag[g], startMid, startYaw, EndLaneWidth, required[g], group);
            startChain[g] = Chain("Start Chain" + tag[g], group, new[] { startLamp }, new[] { startLane }, startBar);
            for (int r = 0; r < n; r++)
            {
                var buttons = new Component[3];
                var lights = new Component[3];
                for (int k = 0; k < 3; k++)
                {
                    buttons[k] = LaneSensor("Lane " + (r + 1) + "." + LaneTag[k] + " " + ColourName[RowOrder[r][k]] + tag[g], laneCentre[r, k], rowYaw[r], rowLane[r], required[g], group);
                    lights[k] = lamps[r, k];
                }
                rowChain[g, r] = Chain("Row Chain " + (r + 1) + tag[g], group, lights, buttons, rowBars[r]);
            }
            Component finishLane = LaneSensor("Finish Lane" + tag[g], finMid, finYaw, EndLaneWidth, required[g], group);
            finishChain[g] = Chain("Finish Chain" + tag[g], group, new[] { finishLamp }, new[] { finishLane }, finishBar);
        }

        // ======== wiring (spec 4c) ========
        Component sdk = b.Puzzle;
        for (int g = 0; g < 2; g++) Sdk.Wire(sdk, "onPuzzleStart", startChain[g], "Begin");
        for (int g = 0; g < 2; g++)
        {
            Sdk.Wire(sdk, "onPuzzleStop", startChain[g], "End");
            for (int r = 0; r < n; r++) Sdk.Wire(sdk, "onPuzzleStop", rowChain[g, r], "End");
            Sdk.Wire(sdk, "onPuzzleStop", finishChain[g], "End");
        }
        Sdk.Wire(sdk, "onPuzzleStop", rowClock, "ResetTimer");
        Sdk.Wire(sdk, "onPuzzleStop", startBar, "Reset");
        for (int r = 0; r < n; r++) Sdk.Wire(sdk, "onPuzzleStop", rowBars[r], "Reset");
        Sdk.Wire(sdk, "onPuzzleStop", finishBar, "Reset");

        // passing the start clears the last run's bars and deals row 1
        for (int r = 1; r < n; r++) Sdk.Wire(startBar, "onFull", rowBars[r], "Reset");
        Sdk.Wire(startBar, "onFull", finishBar, "Reset");
        for (int g = 0; g < 2; g++) Sdk.Wire(startBar, "onFull", rowChain[g, 0], "Begin");
        // each row passed deals the next; the last one arms the finish
        for (int r = 0; r < n; r++)
            for (int g = 0; g < 2; g++)
                Sdk.Wire(rowBars[r], "onFull", r + 1 < n ? rowChain[g, r + 1] : finishChain[g], "Begin");
        // every dealt row (and the finish) restarts the Row Clock from its own stretch's time
        for (int g = 0; g < 2; g++)
        {
            for (int r = 0; r < n; r++)
            {
                float d = clock[r];
                Sdk.Wire(rowChain[g, r], "onBegin", rowClock, "SetDuration", d);
                Sdk.Wire(rowChain[g, r], "onBegin", rowClock, "ResetTimer");
                Sdk.Wire(rowChain[g, r], "onBegin", rowClock, "StartTimer");
                // a wrong lane: stop the clock first (so it cannot also time out), then re-arm the start
                Sdk.Wire(rowChain[g, r], "onFail", rowClock, "ResetTimer");
                Sdk.Wire(rowChain[g, r], "onFail", startChain[g], "Begin");
            }
            float df = clock[n];
            Sdk.Wire(finishChain[g], "onBegin", rowClock, "SetDuration", df);
            Sdk.Wire(finishChain[g], "onBegin", rowClock, "ResetTimer");
            Sdk.Wire(finishChain[g], "onBegin", rowClock, "StartTimer");
        }
        // out of time: end whatever row is running and re-arm the start
        for (int g = 0; g < 2; g++)
        {
            for (int r = 0; r < n; r++) Sdk.Wire(rowClock, "onFinished", rowChain[g, r], "End");
            Sdk.Wire(rowClock, "onFinished", finishChain[g], "End");
            Sdk.Wire(rowClock, "onFinished", startChain[g], "Begin");
        }
        // through the finish together: stop the clock and win
        Sdk.Wire(finishBar, "onFull", rowClock, "PauseTimer");
        Sdk.Wire(finishBar, "onFull", sdk, "Win");

        // ======== Calm Air: no jetpack thrust over the hut, its corridor and the whole course ========
        {
            Vector2 d0 = p0 - site2;
            Vector2 p0h = IsleKit.Turn(d0.x, d0.y, -hutYaw);      // Call Post 0 in the hut frame
            float x0 = Mathf.Min(-9f, p0h.x - 8f), x1 = Mathf.Max(9f, p0h.x + 8f), z0 = Mathf.Min(-9f, p0h.y - 8f), z1 = 10f;
            Vector2 c = IsleKit.Turn((x0 + x1) / 2f, (z0 + z1) / 2f, hutYaw);
            float drop = Mathf.Max(x1 - x0, z1 - z0) / 2f * Mathf.Tan(25f * Mathf.Deg2Rad);
            IsleKit.CalmAir(b, "Slalom Hut", c.x, c.y, new Vector3(x1 - x0, 150f, z1 - z0), hutYaw, -(drop + 10f));
        }
        {
            float from = Post0Along - 5f, to = Mathf.Max(finishAlong + 12f, boardAlong + 5f);
            int boxes = Mathf.CeilToInt((to - from) / 40f);
            float len = (to - from) / boxes;
            for (int i = 0; i < boxes; i++)
            {
                float a0 = from + i * len, a1 = a0 + len, ac = (a0 + a1) / 2f;
                Vector2 mid = RunAt(ac, out Vector2 dir, out Vector2 side, out _);
                float reach = 0f;
                for (float a = a0; a <= a1 + 0.01f; a += 5f)
                {
                    Vector2 m = WinterIslePuzzle.RunCentre(Run, a, out float h);
                    reach = Mathf.Max(reach, Mathf.Abs(Vector2.Dot(m - mid, side)) + h);
                }
                Vector2 l = Local(mid);
                float halfLen = len / 2f + 5f;
                IsleKit.CalmAir(b, "Slalom Course " + (i + 1), l.x, l.y, new Vector3(2f * (reach + 12f), 150f, 2f * halfLen), YawOf(dir), -(halfLen * Mathf.Tan(25f * Mathf.Deg2Rad) + 10f));
            }
            Debug.Log("[BWM] Slalom: Calm Air over the hut and " + boxes + " boxes along the course (" + Sdk.F(from) + " to " + Sdk.F(to) + ")");
        }
    }
}
