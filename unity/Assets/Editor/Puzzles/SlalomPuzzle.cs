// Slalom: a reaction slalom for one skier, on the left-hand run (run 0). No clock, no caller, nobody shut away, and the
// same course whatever the size of the lobby.
//
// What the player does. Turn the puzzle on at the Start Board (above the start, beside the island's ski racks), put skis
// on and ski through the start arch, whose lamp is lit while the puzzle is on. Below it the run is crossed by six
// rows (five if six do not fit) of three gates side by side, red, gold and blue in a different order on every row.
// Passing the start lights the lamp over one gate of row 1; skiing through that gate lights one in row 2, and so on down:
// each row's gate is picked at random at the moment the row before is passed, and its lamp stands on the gate itself,
// facing up the run. The lit gate of every row and then the finish arch, in one run, win the gourd. A wrong gate, or
// skiing on past a row without taking its lit gate, ends the run: the lamps go out, the bars over that row's gates show
// the failure, and it is back up to the start. Skiing through the start arch always begins a fresh run (and ends any run
// still on the course), so one skier can try again alone as often as they like, and several skiers take turns. Nothing
// hurries anyone: the skill is in holding a speed at which the next lit gate can still be reached.
//
// How it is made: the SDK's Press In Order chains, no custom code. Each row is a chain of three pairs (a gate's lamp and
// the Presence Sensor over its lane, which one player sets off) with one round, begun when the row before is passed; a
// wrong lane fails it. Press In Order tells of a right press only by filling its Progress Bars, so a row's three result
// bars (over its exit banners) are the chain's bars, and the first one's "full" counts one on the two rows-cleared bars
// (Start Board, Finish Board) and begins the next row. The start arch and the finish arch are chains of one pair, each
// with a small bar out of sight inside its board. A sensor right across the run below each row ends that row if it is
// still waiting when a skier gets there. Everything hangs off the Start Switch (three of them: the start corner, the
// Start Board, the Finish Board): with the puzzle off no chain runs, and the sensors do nothing.
//
// Kept from the first design (the Called Slalom, Z:\Temp\claude\BigWalkMods\puzzle-specs\Slalom.md): the site and its
// corridor, the start corner in the clearing, the flag line from there to the start, the search that puts the rows on
// pitches a skier can hold (its 4b), the lanes with their banners, pennants and sensors, the start and finish arches,
// the boards at the run's edge with their switches, Calm Air over the course and the light strings. Gone: the caller's
// timing hut with its lamp map and intercom, the speakers on call posts, the Row Clock and its bars, the lanes that
// needed two skiers at once, and the groups by lobby size.
using System;
using System.Collections.Generic;
using System.Linq;
using Bwm;
using UnityEngine;

public sealed class SlalomPuzzle : PuzzleDef
{
    public override string Name => "Slalom";
    public override Vector2 Footprint => new Vector2(14f, 14f);
    // layout-final: a clearing at RunCentre(0, 580) - side * (half + 20), in the woods on the left-hand run's outer side
    public override Vector2? FixedSite => Site;
    static readonly Vector2 Site = new Vector2(-1798.5f, -1521f);
    // two r 6 circles that the island levels and keeps clear: the corridor from the clearing to the Start Board
    public override IEnumerable<Vector3> ExtraSites
    {
        get
        {
            Vector2 toRun = -Away(), edge = Site + toRun * (Footprint.y / 2f), board = BoardSpot();
            for (int i = 1; i <= 2; i++)
            {
                Vector2 c = Vector2.Lerp(edge, board, i / 3f);
                yield return new Vector3(c.x, c.y, CorridorRadius);
            }
        }
    }

    public const int Run = 0;
    const float SiteAlong = 580f, SiteOff = 20f, CorridorRadius = 6f, RingHalf = 5f;
    const float StartAlong = 610f, StartBoardAlong = 590f;
    const float LaneLong = 10f, LaneTall = 6f, LaneCentreH = 2.0f, EndLaneWidth = 9f;
    const float CandFrom = 640f, CandTo = 880f, CandStep = 2f, FinishFrom = 880f, FinishTo = 910f;
    // A row stands only on a pitch where a skier can hold a pace. Steeper than PitchMax the hardest brake (4.05 m/s^2, with
    // SkiDriver's drag of 0.25 + 0.012 v^2) no longer holds 7-9 m/s; flatter than PitchMin a skier barely glides. Run 0 is
    // 20-28 degrees almost everywhere from 640 to 910, so the search prefers the gentler places (PitchCost over PitchSoft)
    // where the spacing allows. The gaps are the skier's time to see the next lamp and reach its gate: 36 m at 13 m/s
    // is under three seconds.
    const float PitchMin = 8f, PitchMax = 26f, GapPitchMax = 26f, GapMin = 36f, GapMax6 = 50f, GapMax5 = 56f;
    const float PitchSoft = 20f, PitchCost = 4f;
    const float WideLane = 4.5f, WidePitch = 5.5f, WideNeeds = 8.75f, NarrowLane = 4.0f, NarrowPitch = 4.75f, NarrowNeeds = 7.5f;
    const float FinishNeeds = 5f;
    // a lane's entry and exit banners (skiers pass through them: nothing on the run has a collider)
    const float BannerLow = 1.35f, BannerTall = 0.5f, BannerDeep = 0.04f;
    // A gate's lamp is the game's flood lamp (Light 6: 0.48 m across, its back 0.142 m behind its pivot, its middle 0.017 m
    // under it) on a dark board standing on the gate's entry banner, clear of a skier's head, facing up the run. A skier
    // sees it under the banners of the row above from the moment it lights, 36 to 56 m off. Range and intensity are first
    // guesses for the light it throws on the snow in front of the gate (tune in the game: the mod's log gives the lamp's own).
    const float LampBoardWide = 1.0f, LampBoardTall = 1.0f, SignDeep = 0.06f;
    const float LampOut = 0.142f + 0.01f, LampDrop = 0.017f, LampRange = 16f, LampIntensity = 3f;
    // a row's result bars: one over each lane's exit banner, on a dark board (a Progress Bar's back is 0.101 m behind its pivot)
    const float BarBoardWide = 2.6f, BarBoardTall = 0.8f, BarOut = 0.101f + 0.01f;
    // the sensor across the whole run below a row (from 6 to 14 m under the row's middle; the lanes end at 5): tall, because
    // the run falls away across its width as well as down its length
    const float CatchBelow = 10f, CatchLong = 8f, CatchTall = 10f, CatchBeyond = 1f;
    // the boards at the run's edge
    const float BoardWide = 2.0f, BoardLow = 0.9f, BoardHigh = 1.9f;

    // lane colours: 0 red, 1 gold, 2 blue; each row's left to right as a skier looking downhill sees them
    static readonly int[][] RowOrder = { new[] { 0, 1, 2 }, new[] { 2, 0, 1 }, new[] { 1, 2, 0 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 2, 1, 0 } };
    static readonly string[] ColourName = { "Red", "Gold", "Blue" };
    static readonly Color[] FlagColour = { IsleKit.Red, IsleKit.Gold, new Color(0.15f, 0.35f, 0.85f) };
    static readonly string[] LaneTag = { "L", "M", "R" };
    static readonly Color White = new Color(0.94f, 0.95f, 0.96f);
    // every lamp on the course is this warm white, and means the same: ski through here next. A lamp's colour tints
    // its lens whether it is lit or not: a strong green looked the same on and off in daylight, a near-white lens is a
    // dull disc when off and a bright one when lit
    static readonly Color GoLight = new Color(1f, 0.92f, 0.75f);

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
    // +1 or -1 so that side * Hs points from the run towards the clearing (worked out from where the site is)
    static float Hs()
    {
        Vector2 mid = RunAt(SiteAlong, out _, out Vector2 side, out _);
        return Vector2.Dot(Site - mid, side) >= 0f ? 1f : -1f;
    }
    // the way from the run to the clearing, which is the site frame's +z
    static Vector2 Away() => (Site - WinterIslePuzzle.RunCentre(Run, SiteAlong, out _)).normalized;
    // the Start Board above the start lane, just off the run's edge on the clearing's side
    static Vector2 BoardSpot() { Vector2 mid = RunAt(StartBoardAlong, out _, out Vector2 side, out float half); return mid + side * Hs() * (half + 1.5f); }
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
    float siteYaw, hs;
    Material bare;

    Vector2 Local(Vector2 world) => world - site2;
    float GroundY(Vector2 world) => b.WorldPoint(world.x - site.x, world.y - site.z).y;
    // a mesh vertex 'above' metres over the ground at a world point, relative to a piece standing at 'origin'
    Vector3 V(Vector3 origin, Vector2 world, float above) => new Vector3(world.x - origin.x, GroundY(world) + above - origin.y, world.y - origin.z);
    // the site frame: local metres about the clearing's middle, +z away from the run
    Vector2 SiteW(float x, float z) => site2 + IsleKit.Turn(x, z, siteYaw);
    // a part on the course, its height above the ground there
    Piece CoursePart(string prefab, Vector2 world, float h, float yaw)
    {
        Vector2 l = Local(world);
        return b.Part(prefab, l.x, l.y, h, yaw);
    }
    // a part on the uphill face of something that stands at 'mid' across the run: 'outBy' metres up the run from its middle,
    // facing up the run, its height measured from the ground at 'mid' as the thing's own mesh is
    Piece UphillPart(string prefab, Vector2 mid, Vector2 dir, float yaw, float outBy, float h)
    {
        Vector2 at = mid - dir * outBy;
        return CoursePart(prefab, at, h + GroundY(mid) - GroundY(at), yaw + 180f);
    }
    // a point on a board that stands at 'o' facing 'yaw': x along its face, z out in front of it
    static Vector2 OnBoard(Vector2 o, float yaw, float x, float z) => o + IsleKit.Turn(x, z, yaw);

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
            Kit.AddBox(mb, V(origin, mid, BannerLow), new Vector3(width, BannerTall, BannerDeep), colour, yaw);
        }
    }

    // A gate's lamp: a dark board standing on a banner whose top edge is 'top' above the ground at 'mid' (added to the
    // gate's mesh), and the flood lamp on its uphill face. The lamp is one pair's light in the gate's Press In Order.
    Component GateLamp(MeshBuilder mb, Vector3 origin, string name, Vector2 mid, Vector2 dir, float yaw, float top)
    {
        Kit.AddBox(mb, V(origin, mid, top), new Vector3(LampBoardWide, LampBoardTall, SignDeep), IsleKit.Board, yaw);
        return UphillPart("Light 6", mid, dir, yaw, SignDeep / 2f + LampOut, top + LampBoardTall / 2f + LampDrop).Named(name)
            .Set("PuzzleLight", "color", GoLight).Set("PuzzleLight", "range", LampRange).Set("PuzzleLight", "intensity", LampIntensity)
            .Component("PuzzleLight");
    }

    // A lane's result bar: a dark board standing on its exit banner and a Progress Bar on the board's uphill face, over
    // the skier's head and in front of them from the moment they enter the lane
    Component GateBar(MeshBuilder mb, Vector3 origin, string name, Vector2 mid, Vector2 dir, float yaw, float top)
    {
        Kit.AddBox(mb, V(origin, mid, top), new Vector3(BarBoardWide, BarBoardTall, SignDeep), IsleKit.Board, yaw);
        return UphillPart("Progress Bar", mid, dir, yaw, SignDeep / 2f + BarOut, top + BarBoardTall / 2f).Named(name)
            .Scaled(0.6f, 0.8f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
    }

    // a Presence Sensor box on the run that one player sets off: 'width' across, 'length' down the run, 'tall' high, its
    // centre 2 m above the ground
    Component RunSensor(string name, Vector2 world, float yaw, float width, float length, float tall)
    {
        Piece zone = CoursePart("Presence Sensor", world, LaneCentreH, yaw).Named(name);
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, tall, length);
        box.center = Vector3.zero;
        zone.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        return zone.Component("PuzzlePresenceSensor");
    }

    Component Chain(string name, Component[] lights, Component[] buttons, Component[] bars)
    {
        Piece holder = b.Empty(name, 0f, 0f, 0.5f);
        Component c = b.AddScript(holder, "PuzzlePressInOrder");
        Sdk.Add(c, "lights", lights);
        Sdk.Add(c, "buttons", buttons);
        Sdk.Add(c, "progressBars", bars);
        Sdk.Set(c, "rounds", 1);
        return c;
    }

    // A board on posts just off the run's edge at 'at', facing 'yaw': a start switch and its live lamp on the half of its
    // face nearer the run ('toRun' is +1 or -1: which way along the board's own x the run lies) and the rows-cleared bar
    // across the top, which is returned. 'relay' is the one bar of the chain at this end of the course (the start arch's
    // or the finish arch's): Press In Order needs it to report a press, nobody needs to see it, so it is 18 cm long
    // and inside the board.
    Component EdgeBoard(string name, string tag, Vector2 at, float yaw, float toRun, int total, out Component relay)
    {
        Vector2 l = Local(at);
        IsleKit.BoardOnPosts(b, name, l.x, l.y, yaw, BoardWide, BoardLow, BoardHigh, IsleKit.Board);
        CoursePart("Switch Start", OnBoard(at, yaw, toRun * 0.6f, IsleKit.FaceZ + 0.03f), 1.3f, yaw).Named("Start Switch (" + tag + ")");
        CoursePart("Live Light", OnBoard(at, yaw, toRun * 0.6f, IsleKit.FaceZ), 1.52f, yaw).Named("Live Light (" + tag + ")");
        relay = CoursePart("Progress Bar", OnBoard(at, yaw, 0f, -0.02f), 1.2f, yaw).Named("Relay Bar (" + tag + ")").Scaled(0.05f, 0.05f, 0.05f)
            .Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        return CoursePart("Progress Bar", OnBoard(at, yaw, 0f, IsleKit.FaceZ + 0.03f), 1.75f, yaw).Named("Rows Bar (" + tag + ")").Scaled(0.5f, 0.5f, 1f)
            .Set("PuzzleProgressBar", "total", total).Component("PuzzleProgressBar");
    }

    // ---------------- the build ----------------

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        site2 = new Vector2(site.x, site.z);
        bare = IsleKit.Bare(b);
        hs = Hs();
        siteYaw = YawOf(Away());
        {
            Vector2 mid = RunAt(SiteAlong, out _, out Vector2 side, out float half);
            Vector2 computed = mid + side * hs * (half + SiteOff);
            Debug.Log("[BWM] Slalom: clearing at " + Sdk.F(site.x) + ", " + Sdk.F(site.z) + " (RunCentre(0, 580) + side * hs * (half + 20) is "
                + Sdk.F(computed.x) + ", " + Sdk.F(computed.y) + ": " + Sdk.F((computed - site2).magnitude) + " m off), hs " + Sdk.F(hs) + ", site yaw " + Sdk.F(siteYaw)
                + ", " + Sdk.F(Vector2.Dot(site2 - mid, side * hs) - half) + " m from the run's edge");
        }
        b.ClearGround(14f, 14f);
        Vector2 dbg = IsleKit.Turn(1.15f, 3.6f, siteYaw);
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
            Debug.Log("[BWM] Slalom row " + (r + 1) + " along " + Sdk.F(rowAlong[r]) + " half " + Sdk.F(c.half) + " pitch " + Sdk.F(c.pitch) + " lanes " + Sdk.F(c.lane)
                + " gap " + Sdk.F(rowAlong[r] - (r == 0 ? StartAlong : rowAlong[r - 1])) + " lateral " + Sdk.F(rowLat[r]));
        }
        Debug.Log("[BWM] Slalom: " + n + " rows, start " + Sdk.F(StartAlong) + ", finish " + Sdk.F(finishAlong) + " (half " + Sdk.F(fin.half) + " pitch " + Sdk.F(fin.pitch)
            + "), last gap " + Sdk.F(finishAlong - rowAlong[n - 1]));

        // ======== the clearing (site frame): the start corner and Christmas lights round it ========
        // the start switch, its live lamp, the gourd's vice, the pillow and the confetti, on the side away from the run and facing it
        Vector2 corner = IsleKit.Turn(0f, 4.6f, siteYaw);
        IsleKit.StartCorner(b, corner.x, corner.y, siteYaw + 180f, true, 0f, false);
        {
            var ring = new List<Vector3>();
            foreach (Vector2 q in new[] { new Vector2(-RingHalf, RingHalf), new Vector2(-RingHalf, 0f), new Vector2(-RingHalf, -RingHalf), new Vector2(0f, -RingHalf),
                new Vector2(RingHalf, -RingHalf), new Vector2(RingHalf, 0f), new Vector2(RingHalf, RingHalf), new Vector2(0f, RingHalf), new Vector2(-RingHalf, RingHalf) })
            {
                Vector2 t = IsleKit.Turn(q.x, q.y, siteYaw);
                ring.Add(new Vector3(t.x, 2.6f, t.y));
            }
            Kit.LightString(b, "Clearing Lights", ring, 0.4f, 0.5f, false, true, 31);
        }

        // ======== the Start Board above the start lane, facing uphill: the switch, the rows-cleared bar, the course card ========
        Vector2 p0 = BoardSpot();
        RunAt(StartBoardAlong, out Vector2 dir0, out _, out _);
        float yaw0 = YawOf(dir0) + 180f;
        // (a viewer stands uphill looking down the run: the board's +x is their left, the run lies along x * hs and the
        // clearing the other way)
        Component startRows = EdgeBoard("Start Board", "start", p0, yaw0, hs, n + 1, out Component startRelay);
        {
            // the course card on the board's other half: three gates as a skier comes down on them, the lamp over the middle
            // one lit, and a ski track through that gate
            var card = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(p0).x, Local(p0).y);
            Vector3 C(float x, float y, float z) { Vector2 t = IsleKit.Turn(x, z, yaw0); return new Vector3(t.x, y, t.y); }
            void CardBox(float x0, float x1, float y0, float y1, float z0, float z1, Color c)
            {
                Vector2 f = IsleKit.Turn((x0 + x1) / 2f, (z0 + z1) / 2f, yaw0);
                Kit.AddBox(card, new Vector3(f.x, y0, f.y), new Vector3(Mathf.Abs(x1 - x0), y1 - y0, z1 - z0), c, yaw0);
            }
            float s = -hs;
            CardBox(s * 0.05f, s * 0.9f, 0.95f, 1.55f, 0.06f, 0.075f, IsleKit.Cream * 0.35f + IsleKit.Board * 0.65f);
            for (int k = 0; k < 3; k++)
            {
                float u = s * (0.22f + 0.255f * k);
                bool lit = k == 1;
                CardBox(u - 0.095f, u + 0.095f, 1.12f, 1.19f, 0.075f, 0.085f, FlagColour[k]);                           // the gate's banner
                CardBox(u - 0.045f, u + 0.045f, 1.23f, 1.32f, 0.075f, 0.085f, lit ? GoLight : IsleKit.DarkWood);      // its lamp
                if (!lit) continue;
                CardBox(u - 0.015f, u + 0.015f, 1.34f, 1.52f, 0.075f, 0.085f, White);                                  // the track down to the gate
                CardBox(u - 0.015f, u + 0.015f, 1.03f, 1.11f, 0.075f, 0.085f, White);                                  // and on below it
                TwoSided(card, C(u, 0.97f, 0.08f), C(u - 0.05f, 1.04f, 0.08f), C(u + 0.05f, 1.04f, 0.08f), White);
            }
            b.MeshPiece("Course Card", card.ToMesh(), bare, false).Transform.position = o;
        }

        // ======== the flag line: the way between the clearing and the Start Board, from the lights' corner post nearest it ========
        {
            Vector2 d0 = p0 - site2, inSite = IsleKit.Turn(d0.x, d0.y, -siteYaw);      // the Start Board in the site frame
            Vector2 from = SiteW(Mathf.Sign(inSite.x) * RingHalf, -RingHalf), to = p0 + (from - p0).normalized * 2.0f;
            Vector2 way = (to - from).normalized;
            float wayYaw = YawOf(way);
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / 6f));
            var mb = new MeshBuilder();
            var posts = new List<Vector3>();
            for (int i = 0; i <= steps; i++)
            {
                Vector2 q = Vector2.Lerp(from, to, (float)i / steps);
                if (i > 0) Kit.AddBox(mb, V(site, q, -0.2f), new Vector3(0.08f, 1.9f, 0.08f), IsleKit.Wood);      // (the first flags hang on the corner post)
                for (int k = 0; k < 3; k++)
                    Kit.AddBox(mb, V(site, q + way * 0.2f, 1.25f + k * 0.13f), new Vector3(0.02f, 0.1f, 0.32f), FlagColour[(i + k) % 3], wayYaw);
                Vector2 lq = Local(q);
                posts.Add(new Vector3(lq.x, 1.62f, lq.y));
            }
            b.MeshPiece("Flag Line", mb.ToMesh(), bare, false).Transform.position = site;
            Kit.LightString(b, "Flag Line Lights", posts, 0.3f, 0.5f, true, false, 34);
        }

        // ======== the course ========
        // the start lane (along 610) and its arch at 605, with the start lamp over it
        Vector2 startMid = RunAt(StartAlong, out Vector2 startDir, out Vector2 startSide, out _);
        float startYaw = YawOf(startDir);
        Component startLamp;
        {
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(startMid).x, Local(startMid).y);
            LaneMarks(mb, o, startMid, startDir, startSide, startYaw, EndLaneWidth, IsleKit.Green, false);
            Vector2 arch = startMid - startDir * (LaneLong / 2f);
            for (int e = -1; e <= 1; e += 2) Kit.AddBox(mb, V(o, arch + startSide * (e * 4.7f), -0.1f), new Vector3(0.12f, 2.7f, 0.12f), IsleKit.Wood, startYaw);
            Kit.AddBox(mb, V(o, arch, 2.05f), new Vector3(9.4f, 0.5f, 0.05f), IsleKit.Green, startYaw);
            startLamp = GateLamp(mb, o, "Start Lamp", arch, startDir, startYaw, 2.55f);
            b.MeshPiece("Start Arch", mb.ToMesh(), bare, false).Transform.position = o;
            var tops = new List<Vector3>();
            foreach (int e in new[] { -1, 1 }) { Vector2 q = Local(arch + startSide * (e * 4.7f)); tops.Add(new Vector3(q.x, 2.6f, q.y)); }
            Kit.LightString(b, "Start Arch Lights", tops, 0.15f, 0.5f, true, false, 32);
        }

        // the rows: three lanes each with banners and pennants, a lamp over every entry banner and a result bar over every exit banner
        var laneCentre = new Vector2[n, 3];
        var lamps = new Component[n, 3];
        var rowBars = new Component[n][];
        for (int r = 0; r < n; r++)
        {
            Vector2 centre = rowMid[r] + rowSide[r] * rowLat[r];
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(centre).x, Local(centre).y);
            rowBars[r] = new Component[3];
            for (int k = 0; k < 3; k++)
            {
                laneCentre[r, k] = centre + rowSide[r] * ((k - 1) * rowStep[r]);
                string lane = (r + 1) + "." + LaneTag[k] + " " + ColourName[RowOrder[r][k]];
                LaneMarks(mb, o, laneCentre[r, k], rowDir[r], rowSide[r], rowYaw[r], rowLane[r], FlagColour[RowOrder[r][k]], true);
                Vector2 entry = laneCentre[r, k] - rowDir[r] * (LaneLong / 2f), exit = laneCentre[r, k] + rowDir[r] * (LaneLong / 2f);
                lamps[r, k] = GateLamp(mb, o, "Lamp " + lane, entry, rowDir[r], rowYaw[r], BannerLow + BannerTall);
                rowBars[r][k] = GateBar(mb, o, "Row Bar " + lane, exit, rowDir[r], rowYaw[r], BannerLow + BannerTall);
            }
            b.MeshPiece("Row " + (r + 1) + " Lanes", mb.ToMesh(), bare, false).Transform.position = o;
        }

        // the finish lane and its arch at F - 5 with the finish lamp over it, the confetti, and the Finish Board below
        Vector2 finMid = RunAt(finishAlong, out Vector2 finDir, out Vector2 finSide, out _);
        float finYaw = YawOf(finDir);
        Component finishLamp;
        {
            var mb = new MeshBuilder();
            Vector3 o = b.WorldPoint(Local(finMid).x, Local(finMid).y);
            LaneMarks(mb, o, finMid, finDir, finSide, finYaw, EndLaneWidth, IsleKit.Red, false);
            Vector2 arch = finMid - finDir * (LaneLong / 2f);
            for (int e = -1; e <= 1; e += 2) Kit.AddBox(mb, V(o, arch + finSide * (e * 4.7f), -0.1f), new Vector3(0.12f, 3.1f, 0.12f), IsleKit.Wood, finYaw);
            Kit.AddBox(mb, V(o, arch, 2.5f), new Vector3(9.4f, 0.5f, 0.05f), IsleKit.Red, finYaw);
            finishLamp = GateLamp(mb, o, "Finish Lamp", arch, finDir, finYaw, 3.0f);
            b.MeshPiece("Finish Arch", mb.ToMesh(), bare, false).Transform.position = o;
            var tops = new List<Vector3>();
            foreach (int e in new[] { -1, 1 }) { Vector2 q = Local(arch + finSide * (e * 4.7f)); tops.Add(new Vector3(q.x, 3.0f, q.y)); }
            Kit.LightString(b, "Finish Arch Lights", tops, 0.15f, 0.5f, true, false, 33);
        }
        CoursePart("Confetti", finMid + finDir * 5f, 0.2f, 0f).Named("Finish Confetti");
        float finishBoardAlong = finishAlong + 14f > 920f ? finishAlong + 8f : finishAlong + 14f;
        Vector2 fbMid = RunAt(finishBoardAlong, out Vector2 fbDir, out Vector2 fbSide, out float fbHalf);
        Vector2 fb = fbMid + fbSide * hs * (fbHalf + 1.5f);
        // (it faces downhill, for arrivals at the bottom: its +x is the run's right, so the run lies along x * -hs)
        Component finishRows = EdgeBoard("Finish Board", "finish", fb, YawOf(fbDir), -hs, n + 1, out Component finishRelay);

        // ======== the sensors and the Press In Order chains ========
        Component startLane = RunSensor("Start Lane", startMid, startYaw, EndLaneWidth, LaneLong, LaneTall);
        Component startChain = Chain("Start Chain", new[] { startLamp }, new[] { startLane }, new[] { startRelay });
        var rowChain = new Component[n];
        for (int r = 0; r < n; r++)
        {
            var buttons = new Component[3];
            var lights = new Component[3];
            for (int k = 0; k < 3; k++)
            {
                buttons[k] = RunSensor("Lane " + (r + 1) + "." + LaneTag[k] + " " + ColourName[RowOrder[r][k]], laneCentre[r, k], rowYaw[r], rowLane[r], LaneLong, LaneTall);
                lights[k] = lamps[r, k];
            }
            rowChain[r] = Chain("Row Chain " + (r + 1), lights, buttons, rowBars[r]);
            // past the row without its lit gate: whoever reaches this box while the row still waits ends it (End does nothing
            // to a row already passed or failed, or with the puzzle off)
            Vector2 below = RunAt(rowAlong[r] + CatchBelow, out Vector2 belowDir, out _, out float belowHalf);
            Component catcher = RunSensor("Row " + (r + 1) + " Catch", below, YawOf(belowDir), 2f * (belowHalf + CatchBeyond), CatchLong, CatchTall);
            Sdk.Wire(catcher, "onPressed", rowChain[r], "End");
        }
        Component finishLane = RunSensor("Finish Lane", finMid, finYaw, EndLaneWidth, LaneLong, LaneTall);
        Component finishChain = Chain("Finish Chain", new[] { finishLamp }, new[] { finishLane }, new[] { finishRelay });

        // ======== wiring ========
        Component sdk = b.Puzzle;
        // every bar a run fills: the rows' result bars, the finish's bar and the two rows-cleared bars
        var runBars = new List<Component> { finishRelay, startRows, finishRows };
        for (int r = 0; r < n; r++) runBars.AddRange(rowBars[r]);
        // the switch: on arms the start arch; off ends every chain (their lamps go out) and empties every bar
        Sdk.Wire(sdk, "onPuzzleStart", startChain, "Begin");
        for (int r = 0; r < n; r++) Sdk.Wire(sdk, "onPuzzleStop", rowChain[r], "End");
        Sdk.Wire(sdk, "onPuzzleStop", finishChain, "End");
        Sdk.Wire(sdk, "onPuzzleStop", startChain, "End");
        Sdk.Wire(sdk, "onPuzzleStop", startRelay, "Reset");
        foreach (Component bar in runBars) Sdk.Wire(sdk, "onPuzzleStop", bar, "Reset");
        // Through the start arch: whatever run was on the course ends, the bars empty, row 1 is dealt, and the start arms
        // itself again. That last Begin runs inside the start chain's own press (its bar filling is what fires this event):
        // it empties the bar again, so Press In Order finds its round unfinished and lights the start lamp once more half a
        // second later. The start chain must keep this bar as its only one for that.
        for (int r = 0; r < n; r++) Sdk.Wire(startRelay, "onFull", rowChain[r], "End");
        Sdk.Wire(startRelay, "onFull", finishChain, "End");
        foreach (Component bar in runBars) Sdk.Wire(startRelay, "onFull", bar, "Reset");
        Sdk.Wire(startRelay, "onFull", rowChain[0], "Begin");
        Sdk.Wire(startRelay, "onFull", startChain, "Begin");
        // each row passed counts one on both boards and deals the next; the last one lights the finish arch
        for (int r = 0; r < n; r++)
        {
            Component passed = rowBars[r][0];
            Sdk.Wire(passed, "onFull", startRows, "Increment");
            Sdk.Wire(passed, "onFull", finishRows, "Increment");
            Sdk.Wire(passed, "onFull", r + 1 < n ? rowChain[r + 1] : finishChain, "Begin");
        }
        // through the finish arch after every row: the boards' bars fill, and the gourd is won
        Sdk.Wire(finishRelay, "onFull", startRows, "Increment");
        Sdk.Wire(finishRelay, "onFull", finishRows, "Increment");
        Sdk.Wire(finishRelay, "onFull", sdk, "Win");

        // ======== Calm Air: no jetpack thrust over the course, from the Start Board to below the Finish Board ========
        {
            float from = StartBoardAlong - 5f, to = Mathf.Max(finishAlong + 12f, finishBoardAlong + 5f);
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
            Debug.Log("[BWM] Slalom: Calm Air in " + boxes + " boxes along the course (" + Sdk.F(from) + " to " + Sdk.F(to) + ")");
        }
    }
}
