// Downhill (puzzle 5): the Called Slalom. A timed giant slalom on the middle run that nobody can win alone. Six rows
// of three gates (red, green, blue, in a different left-to-right order on every row) cross the piste; the skier must
// pass the one live gate of each row, and cannot see which it is. Only lamp panels shut inside timing booths show it,
// and every row is shown only on a panel the skier has left far behind or will reach only after the row:
//   2-3 players: the finish timing hut shows rows 1-3, the start house by the ski lift's top station rows 4-6;
//   4 players:   a hut on the cat track shows rows 1-2, the finish rows 3-4, the start house rows 5-6.
// The callers talk the skier down over the course intercom (speakers along the run) and walkie-talkies. A row is dealt
// at random only when the one before is cleared; one wrong gate kills the run. The clock runs from the start knob, and
// the timer ball (from the dispenser at the start gate) must go into the holder in the finish shelter after a clean
// course, before the clock runs out: the Validator checks and wins. No custom code: the SDK's Press In Order rows with
// Presence Sensor gates, Timers, a Validator, a Timer Spawner and Goal, Intercoms, and Hider groups by lobby size.
// Spec: Z:\Temp\claude\BigWalkMods\puzzle-specs\Downhill.md (revision 2); what this file changed from it: Downhill.impl.md.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class DownhillPuzzle : PuzzleDef
{
    public override string Name => "Downhill";
    public override Vector2 Footprint => new Vector2(22f, 16f);
    // In the run-out below the finish arch (the layout's site: PisteFinish + direction * 34 - side * half * 0.2, as before)
    public override Vector2? FixedSite => new Vector2(-949.8f, -1459.3f);
    // The start house (beside the ski lift's top station) and the cat track hut, both off run 1's right-hand edge at
    // half + 18 m (layout-final: half + 8 put them on the piste); the island levels and keeps them clear
    public override IEnumerable<Vector3> ExtraSites => new[]
    {
        new Vector3(-1774.6f, -1786.5f, 6.4f),
        new Vector3(-1383.4f, -1625.9f, 3.9f),
    };
    static readonly Vector2 StartHouseSite = new Vector2(-1774.6f, -1786.5f);
    static readonly Vector2 CatHutSite = new Vector2(-1383.4f, -1625.9f);

    // ---- the course ----
    const float T = 140f;                  // course time on every clock and the ball (tune in the game; never above 160)
    const int Run = 1;                     // the middle run
    const float WideHalf = 16f;            // a row slides within its window to where the run is at least this wide
    const float GateSpread = 10f, GateWidth = 4.5f, NarrowSpread = 8f, NarrowWidth = 4f;
    const float GateDepth = 7f, GateTall = 5f, GateCentre = 1.5f;      // sensor box: 1 m under the snow to 4 m above
    static readonly float[] RowNominal = { 560f, 680f, 800f, 1045f, 1135f, 1240f };
    static readonly float[] RowLo = { 520f, 640f, 760f, 1030f, 1100f, 1200f };
    static readonly float[] RowHi = { 600f, 720f, 840f, 1070f, 1170f, 1280f };
    // each row's colours from left to right looking downhill (0 red, 1 green, 2 blue)
    static readonly int[][] RowOrder = { new[] { 0, 1, 2 }, new[] { 2, 0, 1 }, new[] { 1, 2, 0 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 2, 1, 0 } };
    static readonly string[] ColourName = { "Red", "Green", "Blue" };
    static readonly Color[] LampColour = { new Color(1f, 0.125f, 0.125f), new Color(0.125f, 0.878f, 0.251f), new Color(0.188f, 0.376f, 1f) };      // #FF2020 #20E040 #3060FF
    static readonly Color[] GateColour = { IsleKit.Red, new Color(0.1f, 0.6f, 0.24f), new Color(0.14f, 0.3f, 0.86f) };
    static readonly Color White = new Color(0.95f, 0.95f, 0.92f), Black = new Color(0.1f, 0.1f, 0.12f);
    const float LampRange = 0.25f;         // the Mod sets the light's range when > 0: the glow cannot leave the booth

    // ---- the booths: one design for all three (canonical frame: doorway in the front wall at -z, the panel on the
    // back wall at +z, the caller facing +z, so "left" is -x) ----
    const float PadTop = 0.15f;            // floors stand this far above the levelled ground
    const float HW = 2.1f, HD = 1.8f, Wall = 0.3f, WallH = 2.5f, RoofT = 0.3f, Backboard = 0.6f, DoorTop = 2.1f;
    const float IW = HW - Wall;                         // 1.8: the inside half-width
    const float Face = HD - Wall - Backboard;           // 0.9: the backboard's face, 0.9 m in from the back wall's outside
    const float Front = -HD + Wall;                     // -1.5: the front wall's inside
    const float DoorX0 = 0.3f, DoorX1 = 1.5f;           // the doorway, 1.2 m wide, at the front wall's right
    const float BaffleX0 = -0.45f;                      // the baffle runs from here to the right wall, 1.0-1.3 m in from the front
    static readonly float[] SlotHeight = { 2.0f, 1.65f, 1.3f, 0.95f };      // panel rows, top down (0.35 m apart)
    const float Col0 = -0.9f, ColStep = 0.35f, BarX = 0.55f, SideX = 1.45f;

    PuzzleBuilder b;
    Vector3 root;
    Material snowy, bare;

    // A placing frame: an origin (root-local x, z), a yaw about the root, and a base height above the root's level
    sealed class Frame
    {
        readonly PuzzleBuilder b;
        public readonly Vector2 Origin;
        public readonly float Yaw, Base;
        public Frame(PuzzleBuilder b, Vector2 origin, float yaw, float baseHeight) { this.b = b; Origin = origin; Yaw = yaw; Base = baseHeight; }
        public Frame Sub(float x, float z, float yaw) => new Frame(b, Local(x, z), Yaw + yaw, Base);
        public Vector2 Local(float x, float z) => Origin + IsleKit.Turn(x, z, Yaw);
        public Piece Part(string prefab, float x, float z, float h, float yaw)
        {
            Vector2 p = Local(x, z);
            return b.Part(prefab, p.x, p.y, Base + h, Yaw + yaw, false);
        }
        public Piece Empty(string name, float x, float z, float h)
        {
            Vector2 p = Local(x, z);
            return b.Empty(name, p.x, p.y, Base + h, false);
        }
        // A mesh built in this frame's coordinates (y up from the base)
        public Piece Mesh(string name, MeshBuilder mb, Material material, bool collider = true)
        {
            Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
            piece.Transform.position = b.WorldPoint(Origin.x, Origin.y, Base, false);
            piece.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Yaw, 0f);
            return piece;
        }
        // A light string post (root-local x, height above the root's level, z), for Kit.LightString with onGround false
        public Vector3 Post(float x, float z, float h) { Vector2 p = Local(x, z); return new Vector3(p.x, Base + h, p.y); }
    }

    // One row's panel: its Press In Order, bar and lamps (R, G, B)
    sealed class PanelRow { public Component order, bar; }
    // What every booth has
    sealed class BoothParts { public Component timer, fail, mic, speaker; }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        root = b.Root.transform.position;
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        var site = new Vector2(root.x, root.z);
        Sdk.Set(b.Puzzle, "gourdColor", IsleKit.Gold);      // a harder puzzle

        // ---- frames: the finish (+z downhill along the run-out, +x right looking downhill), the start house, the cat hut ----
        WinterIslePuzzle.PisteFinish(out Vector2 direction, out _);
        float runYaw = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        var finish = new Frame(b, Vector2.zero, runYaw, GroundAt(Vector2.zero) + PadTop);
        Vector2 startLocal = StartHouseSite - site, catLocal = CatHutSite - site;
        float startYaw = FallYaw((StartHouseSite - WinterIslePuzzle.Peak).magnitude);
        float catYaw = FallYaw((CatHutSite - WinterIslePuzzle.Peak).magnitude);
        var start = new Frame(b, startLocal, startYaw, GroundAt(startLocal) + PadTop);
        var cat = new Frame(b, catLocal, catYaw, GroundAt(catLocal) + PadTop);

        Vector2 debug = finish.Local(0f, -4.5f);
        b.DebugTeleport(debug.x, debug.y);
        Vector2 clearAt = finish.Local(0.5f, 1.5f);
        b.ClearGround(22f, 16f, clearAt.x, clearAt.y).Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, runYaw, 0f);
        b.ClearGround(10f, 8f, startLocal.x, startLocal.y).Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, startYaw, 0f);
        b.ClearGround(6f, 5f, catLocal.x, catLocal.y).Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, catYaw, 0f);

        // ---- the course: 18 gate sensors, their poles and banners, the speakers along the run ----
        var along = new float[7];
        var gateSpots = new List<Vector2>();
        Component[][] gates = Course(site, along, gateSpots);
        List<Component> course = CourseSpeakers(site, gateSpots);

        // ---- the lobby groups ----
        Piece rows12Finish = IsleKit.Lobby(b, "Rows 1-2 at Finish", true, true, false);
        Piece row4Start = IsleKit.Lobby(b, "Row 4 at Start", true, true, false);
        Piece row4Finish = IsleKit.Lobby(b, "Row 4 at Finish", false, false, true);
        Piece mid = IsleKit.Lobby(b, "Mid Caller", false, false, true);

        // ---- the start house: a level stone pad, the start gate and the ball's dispenser, the booth (rows 4A, 5, 6) ----
        var pad = new MeshBuilder();
        Block(pad, -5f, 5f, -1.15f, 0f, -4f, 4f, IsleKit.Stone);
        Block(pad, -3.45f, -2.55f, 0f, 0.35f, 2.15f, 3.05f, IsleKit.Stone);       // under the dispenser
        start.Mesh("Start House Pad", pad, snowy);
        var gate = new MeshBuilder();
        Block(gate, -3.68f, -3.52f, 0f, 3.0f, 2.92f, 3.08f, IsleKit.Red);
        Block(gate, -0.48f, -0.32f, 0f, 3.0f, 2.92f, 3.08f, IsleKit.Red);
        Block(gate, -3.52f, -0.48f, 2.45f, 2.95f, 2.97f, 3.03f, IsleKit.Red);
        for (int i = 0; i < 12; i++)
            Block(gate, -3.5f + i * 0.25f, -3.5f + (i + 1) * 0.25f, 2.6f, 2.8f, 2.95f, 2.97f, i % 2 == 0 ? White : Black);
        start.Mesh("Start Gate", gate, bare);
        Piece spawner = start.Part("Timer Spawner", -3.0f, 2.6f, 0.35f + 0.3f, 0f).Named("Timer Ball Dispenser");
        spawner.Set("PuzzleTimerSpawner", "style", 0).Set("PuzzleTimerSpawner", "time", T);
        Kit.LightString(b, "Start Gate Lights", new List<Vector3> { start.Post(-3.6f, 3.0f, 2.95f), start.Post(-0.4f, 3.0f, 2.95f) }, 0.3f, 0.4f, false, false, 51);
        Frame startBooth = Booth(start, "Start Booth", 1.4f, -1.3f, 0f, false, 52);
        BoothParts startKit = BoothKit(startBooth, "Start", null, 2);
        PanelRow r4A = Row(startBooth, "4A", 4, 0, gates[3], row4Start);
        PanelRow r5 = Row(startBooth, "5", 5, 1, gates[4], null);
        PanelRow r6 = Row(startBooth, "6", 6, 2, gates[5], null);

        // ---- the cat track hut (rows 1B, 2B in 4-player lobbies; an empty shelter otherwise) ----
        var catPad = new MeshBuilder();
        Block(catPad, -3f, 3f, -1.15f, 0f, -2.5f, 2.5f, IsleKit.Stone);
        cat.Mesh("Cat Hut Pad", catPad, snowy);
        Frame catBooth = Booth(cat, "Cat Hut", 0f, 0f, 180f, false, 53);      // doorway downhill, onto the cat track
        BoothParts catKit = BoothKit(catBooth, "Mid", mid, 1);
        PanelRow r1B = Row(catBooth, "1B", 1, 0, gates[0], mid);
        PanelRow r2B = Row(catBooth, "2B", 2, 1, gates[1], mid);

        // ---- the finish: the shelter with the ball's holder and the Validator, the start corner, the timing hut ----
        var shelter = new MeshBuilder();
        Block(shelter, -2f, 2f, -1.15f, 0f, -1.5f, 1.5f, IsleKit.Stone);         // floor, sunk about a metre
        Block(shelter, -2f, 2f, 0f, 2.6f, 1.25f, 1.5f, IsleKit.Wood);            // back wall (downhill)
        Block(shelter, -2f, -1.75f, 0f, 2.6f, -1.5f, 1.25f, IsleKit.Wood);
        Block(shelter, 1.75f, 2f, 0f, 2.6f, -1.5f, 1.25f, IsleKit.Wood);
        Block(shelter, -2.3f, 2.3f, 2.6f, 2.8f, -1.8f, 1.8f, IsleKit.DarkWood);   // roof
        Block(shelter, -1.75f, 1.75f, 2.25f, 2.6f, -1.5f, -1.3f, IsleKit.Wood);  // beam over the open uphill side
        Block(shelter, -1.25f, -0.55f, 0f, 0.5f, 0.25f, 0.95f, IsleKit.Stone);   // the holder's plinth
        Block(shelter, 0.65f, 1.15f, 0f, 1.75f, 0.6f, 0.9f, IsleKit.Stone);      // the Validator's post
        // the chequered board behind the shelter, high enough to read over its roof from up the run
        Block(shelter, -1.6f, -1.4f, -0.6f, 4.2f, 2.3f, 2.5f, IsleKit.Red);
        Block(shelter, 1.4f, 1.6f, -0.6f, 4.2f, 2.3f, 2.5f, IsleKit.Red);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 2; j++)
                Block(shelter, -1.4f + i * 0.4667f, -1.4f + (i + 1) * 0.4667f, 3.0f + j * 0.5f, 3.5f + j * 0.5f, 2.36f, 2.44f, (i + j) % 2 == 0 ? Black : White);
        finish.Mesh("Finish Shelter", shelter, snowy);
        var banner = new MeshBuilder();      // chequer along the beam: legible as "the end"
        for (int i = 0; i < 14; i++)
            for (int j = 0; j < 2; j++)
                Block(banner, -1.75f + i * 0.25f, -1.5f + i * 0.25f, 2.25f + j * 0.175f, 2.425f + j * 0.175f, -1.52f, -1.5f, (i + j) % 2 == 0 ? Black : White);
        finish.Mesh("Finish Banner", banner, bare, false);
        Piece goal = finish.Part("Timer Goal", -0.9f, 0.6f, 0.5f + 0.12f, 180f).Named("Ball Holder");
        Piece validator = finish.Part("Validator", 0.9f, 0.6f, 1.4f, 180f).Named("Finish Check");      // its back on the post, screen to the opening
        Vector2 corner = finish.Local(-7.5f, -1.5f);
        IsleKit.StartCorner(b, corner.x, corner.y, runYaw + 180f);
        Frame hut = Booth(finish, "Timing Hut", 8f, 3f, 180f, true, 54);       // doorway downhill, panel on the uphill wall
        BoothParts finishKit = BoothKit(hut, "Finish", null, 1);
        PanelRow r1A = Row(hut, "1A", 1, 0, gates[0], rows12Finish);
        PanelRow r2A = Row(hut, "2A", 2, 1, gates[1], rows12Finish);
        PanelRow r3 = Row(hut, "3", 3, 2, gates[2], null);
        PanelRow r4B = Row(hut, "4B", 4, 3, gates[3], row4Finish);
        Kit.LightString(b, "Finish Lights", new List<Vector3>
        {
            RingPost(finish, -10.5f, -5f), RingPost(finish, -10.5f, 1f), RingPost(finish, -10.5f, 7f), RingPost(finish, 0.25f, 7f),
            RingPost(finish, 11f, 7f), RingPost(finish, 11f, 1f), RingPost(finish, 11f, -5f),
        }, 0.4f, 0.5f, true, true, 17);      // three sides, open uphill where the skier comes in

        // ---- Calm Air over each structure (the course itself stays open: see Downhill.impl.md) ----
        Vector2 calm = finish.Local(0.5f, 1.5f);
        IsleKit.CalmAir(b, "Downhill Finish", calm.x, calm.y, new Vector3(30f, 120f, 24f), runYaw, -10f);
        IsleKit.CalmAir(b, "Downhill Start", startLocal.x, startLocal.y, new Vector3(18f, 120f, 16f), startYaw, -10f);
        IsleKit.CalmAir(b, "Downhill Cat Hut", catLocal.x, catLocal.y, new Vector3(12f, 120f, 11f), catYaw, -10f);

        // ---- voice: every mic plays to every course speaker and the other booths' speakers ----
        Sdk.Add(startKit.mic, "speakers", Speakers(course, finishKit.speaker, catKit.speaker));
        Sdk.Add(finishKit.mic, "speakers", Speakers(course, startKit.speaker, catKit.speaker));
        Sdk.Add(catKit.mic, "speakers", Speakers(course, startKit.speaker, finishKit.speaker));

        // ---- the build assert: no row on a panel the skier can read on the way down ----
        along[0] = (World(start.Local(-2f, 3f)) - WinterIslePuzzle.Peak).magnitude;      // the start gate
        AssertPanels(along, World(startBooth.Origin), World(catBooth.Origin), World(hut.Origin));

        // ---- wiring ----
        PanelRow[] rows = { r1A, r1B, r2A, r2B, r3, r4A, r4B, r5, r6 };
        Component[] timers = { startKit.timer, finishKit.timer, catKit.timer };
        Component[] fails = { startKit.fail, finishKit.fail, catKit.fail };
        Component check = validator.Component("PuzzleValidator");
        Piece sdk = b.RootPiece;

        sdk.On("PuzzleSDK", "onPuzzleStart", r1A.order, "Begin");
        sdk.On("PuzzleSDK", "onPuzzleStart", r1B.order, "Begin");
        foreach (Component t in timers) sdk.On("PuzzleSDK", "onPuzzleStart", t, "StartTimer");
        sdk.On("PuzzleSDK", "onPuzzleStart", check, "TurnOn");
        sdk.On("PuzzleSDK", "onPuzzleStart", check, "SetCorrect", false);
        foreach (PanelRow r in rows) sdk.On("PuzzleSDK", "onPuzzleStart", r.bar, "Reset");
        foreach (PanelRow r in rows) sdk.On("PuzzleSDK", "onPuzzleStop", r.order, "End");
        foreach (Component t in timers) sdk.On("PuzzleSDK", "onPuzzleStop", t, "ResetTimer");
        foreach (Component t in timers) sdk.On("PuzzleSDK", "onWin", t, "PauseTimer");

        foreach (PanelRow r in new[] { r1A, r1B })
            foreach (Component f in fails) Sdk.Wire(r.order, "onBegin", f, "TurnOff");
        Chain(new[] { r1A, r1B }, r2A, r2B);
        Chain(new[] { r2A, r2B }, r3);
        Chain(new[] { r3 }, r4A, r4B);
        Chain(new[] { r4A, r4B }, r5);
        Chain(new[] { r5 }, r6);
        Sdk.Wire(r6.bar, "onFull", check, "SetCorrect", true);
        foreach (PanelRow r in rows)
            foreach (Component f in fails) Sdk.Wire(r.order, "onFail", f, "TurnOn");

        Component clock = finishKit.timer;      // the finish clock's run-out is the one wired
        Sdk.Wire(clock, "onFinished", check, "SetCorrect", false);
        foreach (PanelRow r in rows) Sdk.Wire(clock, "onFinished", r.order, "End");
        foreach (Component f in fails) Sdk.Wire(clock, "onFinished", f, "TurnOn");

        goal.On("PuzzleTimerGoal", "onTimerPlaced", check, "Check");      // never Win directly: the Validator wins
    }

    // Each of 'from' rows' bars, when full, begins every row in 'to'
    static void Chain(PanelRow[] from, params PanelRow[] to)
    {
        foreach (PanelRow f in from)
            foreach (PanelRow t in to) Sdk.Wire(f.bar, "onFull", t.order, "Begin");
    }

    static Object[] Speakers(List<Component> course, params Component[] booths)
    {
        var all = new List<Object>(course);
        all.AddRange(booths);
        return all.ToArray();
    }

    // ---- the course ----

    // The 18 gate sensors, by row and colour; fills along[1..6] and the gates' world spots
    Component[][] Course(Vector2 site, float[] along, List<Vector2> gateSpots)
    {
        var sensors = new Component[6][];
        var poles = new MeshBuilder();
        for (int r = 0; r < 6; r++)
        {
            float a = ChooseAlong(r, out float half, out bool narrow);
            along[r + 1] = a;
            Vector2 centre = WinterIslePuzzle.RunCentre(Run, a, out _);
            Vector2 down = (WinterIslePuzzle.RunCentre(Run, a + 6f, out _) - centre).normalized;
            var side = new Vector2(down.y, -down.x);
            float yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
            float spread = narrow ? NarrowSpread : GateSpread, width = narrow ? NarrowWidth : GateWidth;
            Debug.Log("[BWM] Downhill: row " + (r + 1) + " at along " + Sdk.F(a) + " (window " + Sdk.F(RowLo[r]) + "-" + Sdk.F(RowHi[r])
                + "), half-width " + Sdk.F(half) + (narrow ? " NARROW: gates at +-8 m, 4 m wide (no pitch of 16 m half-width in its window)" : ""));
            sensors[r] = new Component[3];
            for (int p = 0; p < 3; p++)
            {
                int colour = RowOrder[r][p];
                Vector2 world = centre + side * ((p - 1) * spread);
                gateSpots.Add(world);
                Vector2 l = world - site;
                float g = GroundAt(l);
                // tilt the box with the slope, so it holds a skier over its whole depth
                float drop = GroundAt(l - down * (GateDepth / 2f)) - GroundAt(l + down * (GateDepth / 2f));
                float pitch = Mathf.Atan2(drop, GateDepth) * Mathf.Rad2Deg;
                Piece sensor = b.Part("Presence Sensor", l.x, l.y, GateCentre, yaw).Named("Gate " + (r + 1) + " " + ColourName[colour]);
                sensor.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
                BoxCollider box = sensor.Go.GetComponent<BoxCollider>();
                if (box == null) box = sensor.Go.AddComponent<BoxCollider>();
                box.size = new Vector3(width, GateTall, GateDepth);
                box.center = Vector3.zero;
                sensor.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
                sensors[r][colour] = sensor.Component("PuzzlePresenceSensor");
                AddGate(poles, new Vector3(l.x, g, l.y), down, width, colour);
            }
        }
        b.MeshPiece("Slalom Gates", poles.ToMesh(), bare, false);      // no colliders: skiers pass through banners and poles
        return sensors;
    }

    // Slides a row within its window to the nearest along where the run is wide enough over the sensor's depth
    static float ChooseAlong(int r, out float half, out bool narrow)
    {
        float best = RowNominal[r], bestHalf = -1f;
        for (int d = 0; d <= 120; d++)
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                if (d == 0 && sign < 0) continue;
                float a = RowNominal[r] + sign * d;
                if (a < RowLo[r] || a > RowHi[r]) continue;
                float h = Mathf.Min(HalfAt(a), Mathf.Min(HalfAt(a - GateDepth / 2f), HalfAt(a + GateDepth / 2f)));
                if (h >= WideHalf) { half = h; narrow = false; return a; }
                if (h > bestHalf) { bestHalf = h; best = a; }
            }
        half = bestHalf;
        narrow = true;
        return best;
    }

    static float HalfAt(float along) { WinterIslePuzzle.RunCentre(Run, along, out float half); return half; }

    static float FallYaw(float along)
    {
        Vector2 c = WinterIslePuzzle.RunCentre(Run, along, out _);
        Vector2 down = WinterIslePuzzle.RunCentre(Run, along + 6f, out _) - c;
        return Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
    }

    // A slalom gate in root-local coordinates: two poles, a banner in the gate's colour and its shape in white on both
    // faces (triangle red, square green, disc blue), for colour-blind players
    static void AddGate(MeshBuilder mb, Vector3 foot, Vector2 down, float width, int colour)
    {
        var fwd = new Vector3(down.x, 0f, down.y);
        var right = new Vector3(down.y, 0f, -down.x);
        float yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
        Color c = GateColour[colour];
        Kit.AddBox(mb, foot + right * (width / 2f), new Vector3(0.08f, 3.4f, 0.08f), c, yaw);
        Kit.AddBox(mb, foot - right * (width / 2f), new Vector3(0.08f, 3.4f, 0.08f), c, yaw);
        Kit.AddBox(mb, foot + Vector3.up * 2.6f, new Vector3(width, 0.8f, 0.05f), c, yaw);
        AddShape(mb, foot + Vector3.up * 3.0f - fwd * 0.035f, right, Vector3.up, colour, 0.3f, White);
        AddShape(mb, foot + Vector3.up * 3.0f + fwd * 0.035f, right, Vector3.up, colour, 0.3f, White);
    }

    // A flat shape, seen from both sides: 0 a triangle point up, 1 a square, 2 a disc
    static void AddShape(MeshBuilder mb, Vector3 centre, Vector3 right, Vector3 up, int kind, float radius, Color color)
    {
        int sides = kind == 0 ? 3 : kind == 1 ? 4 : 16;
        float spin = kind == 0 ? Mathf.PI / 2f : kind == 1 ? Mathf.PI / 4f : 0f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = spin + 2f * Mathf.PI * i / sides, a1 = spin + 2f * Mathf.PI * (i + 1) / sides;
            Vector3 p0 = centre + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            Vector3 p1 = centre + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            mb.Tri(centre, p0, p1, color);
            mb.Tri(centre, p1, p0, color);
        }
    }

    // The course speakers: fourteen, every 60 m from along 515, alternating sides just off the piste, facing the run,
    // none within 8 m of a gate
    List<Component> CourseSpeakers(Vector2 site, List<Vector2> gateSpots)
    {
        var speakers = new List<Component>();
        var posts = new MeshBuilder();
        for (int i = 0; i < 14; i++)
        {
            float s = i % 2 == 0 ? -1f : 1f;
            Vector2 spot = default, inward = default;
            bool clear = false;
            for (int t = 0; t < 9 && !clear; t++)
            {
                float a = 515f + 60f * i + ((t + 1) / 2) * 6f * (t % 2 == 1 ? 1f : -1f);
                Vector2 c = WinterIslePuzzle.RunCentre(Run, a, out float half);
                Vector2 d = (WinterIslePuzzle.RunCentre(Run, a + 6f, out _) - c).normalized;
                Vector2 side = new Vector2(d.y, -d.x) * s;
                spot = c + side * (half + 3f);
                inward = -side;
                clear = true;
                foreach (Vector2 g in gateSpots) if ((g - spot).magnitude < 8f) clear = false;
            }
            if (!clear) Debug.LogWarning("[BWM] Downhill: course speaker " + (i + 1) + " is within 8 m of a gate");
            Vector2 l = spot - site;
            float yaw = Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg;
            var foot = new Vector3(l.x, GroundAt(l), l.y);
            var face = new Vector3(inward.x, 0f, inward.y);
            Kit.AddBox(posts, foot, new Vector3(0.14f, 1.95f, 0.14f), IsleKit.Wood, yaw);
            Kit.AddBox(posts, foot + Vector3.up * 1.95f, new Vector3(0.36f, 0.06f, 0.36f), IsleKit.DarkWood, yaw);
            Kit.AddBox(posts, foot + Vector3.up * 1.38f + face * 0.09f, new Vector3(0.38f, 0.44f, 0.04f), IsleKit.Board, yaw);
            Vector2 at = l + inward * 0.21f;      // the speaker's back (0.1 m behind its pivot) on the plate
            Piece speaker = b.Part("Intercom Speaker", at.x, at.y, 1.43f, yaw).Named("Course Speaker " + (i + 1));
            speakers.Add(speaker.Component("PuzzleIntercomSpeaker"));
        }
        b.MeshPiece("Course Speaker Posts", posts.ToMesh(), snowy);
        return speakers;
    }

    // ---- the booths ----

    // A booth at (cx, cz) in 'parent', turned by 'yaw': 0.3 m walls and roof with no gaps, a solid backboard 0.6 m
    // thick on the back wall (its face 0.9 m in from the outside), the doorway in the front wall and a baffle inside
    // it that hides the panel from every line through the doorway. Returns the booth's own frame.
    Frame Booth(Frame parent, string name, float cx, float cz, float yaw, bool floor, int seed)
    {
        Frame f = parent.Sub(cx, cz, yaw);
        var mb = new MeshBuilder();
        if (floor) Block(mb, -HW, HW, -1.15f, 0f, -HD, HD, IsleKit.Stone);
        Block(mb, -HW, -IW, 0f, WallH, -HD, HD, IsleKit.Wood);                       // left wall
        Block(mb, IW, HW, 0f, WallH, -HD, HD, IsleKit.Wood);                         // right wall
        Block(mb, -IW, IW, 0f, WallH, HD - Wall, HD, IsleKit.Wood);                  // back wall
        Block(mb, -IW, IW, 0f, WallH, Face, HD - Wall, IsleKit.Board);              // the backboard
        Block(mb, -IW, DoorX0, 0f, WallH, -HD, Front, IsleKit.Wood);                 // front wall, left of the doorway
        Block(mb, DoorX1, IW, 0f, WallH, -HD, Front, IsleKit.Wood);                  // and right of it
        Block(mb, DoorX0, DoorX1, DoorTop, WallH, -HD, Front, IsleKit.Wood);         // lintel
        Block(mb, BaffleX0, IW, 0f, WallH, Front + 1.0f, Front + 1.3f, IsleKit.Wood);   // baffle
        Block(mb, -HW - 0.3f, HW + 0.3f, WallH, WallH + RoofT, -HD - 0.3f, HD + 0.3f, IsleKit.DarkWood);   // roof, overhanging 0.3 m
        f.Mesh(name, mb, snowy);
        // the colour and shape over each panel column, which the panel always shows red, green, blue from the left
        var marks = new MeshBuilder();
        for (int c = 0; c < 3; c++)
            AddShape(marks, new Vector3(Col0 + c * ColStep, 2.3f, Face - 0.012f), Vector3.right, Vector3.up, c, 0.09f, GateColour[c]);
        f.Mesh(name + " Marks", marks, bare, false);
        Kit.LightString(b, name + " Lights", new List<Vector3> { f.Post(-HW - 0.2f, -HD - 0.32f, WallH - 0.02f), f.Post(0f, -HD - 0.32f, WallH - 0.02f), f.Post(HW + 0.2f, -HD - 0.32f, WallH - 0.02f) },
            0.15f, 0.4f, false, false, seed);
        return f;
    }

    // What every booth has: a start knob and live lamp in the doorway passage, the clock, the fail lamp and the
    // intercom mic beside the panel, a booth speaker on the right wall, walkie-talkies on hooks on the left wall
    BoothParts BoothKit(Frame f, string tag, Piece group, int walkies)
    {
        var made = new List<Piece>();
        made.Add(f.Part("Switch Start", -1.2f, Front + 0.073f, 1.25f, 0f).Named(tag + " Start Switch"));
        made.Add(f.Part("Live Light", -1.2f, Front + 0.006f, 1.42f, 0f).Named(tag + " Live Light"));
        Piece clock = f.Part("Timer", SideX, Face - 0.02f, 1.55f, 180f).Named(tag + " Clock").Scaled(0.6f * PuzzleBuilder.TimerScale, 0.6f * PuzzleBuilder.TimerScale, 0.6f * PuzzleBuilder.TimerScale);
        clock.Set("PuzzleTimer", "duration", T);
        Piece fail = f.Part("Light 8", SideX, Face - 0.03f, 2.05f, 180f).Named(tag + " Fail Lamp")
            .Set("PuzzleLight", "color", new Color(1f, 0f, 0f)).Set("PuzzleLight", "range", LampRange);
        Piece mic = f.Part("Intercom Mic", SideX, Face - 0.02f, 1.0f, 180f).Named(tag + " Intercom");
        Piece speaker = f.Part("Intercom Speaker", IW - 0.1f, 0.35f, 1.85f, -90f).Named(tag + " Booth Speaker");
        made.Add(clock); made.Add(fail); made.Add(mic); made.Add(speaker);
        for (int i = 0; i < walkies; i++)
        {
            float z = i == 0 ? 0.35f : -1.0f;
            Piece walkie = f.Part("Vanilla Item", -IW + 0.3f, z, 1.3f, 90f).Named(tag + " Walkie-Talkie " + (i + 1))
                .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
            Piece hook = f.Part("Hook", -IW + 0.03f, z, 1.55f, 90f).Named(tag + " Walkie Hook " + (i + 1));
            hook.Set("PuzzleHook", "defaultItem", walkie.Component("PuzzleVanillaItem"));
            Sdk.SetFlags(hook.Component("PuzzleHook"), "acceptedVanilla", 16);      // WalkieTalkie
            made.Add(walkie); made.Add(hook);
        }
        if (group != null) foreach (Piece p in made) p.Under(group);
        return new BoothParts
        {
            timer = clock.Component("PuzzleTimer"), fail = fail.Component("PuzzleLight"),
            mic = mic.Component("PuzzleIntercom"), speaker = speaker.Component("PuzzleIntercomSpeaker"),
        };
    }

    // One row on a booth's panel, in slot 'slot': three lamps (R, G, B from the caller's left), its bar, the row's
    // number in white pips at its left, and its Press In Order over the row's three gate sensors
    PanelRow Row(Frame f, string tag, int row, int slot, Component[] sensors, Piece group)
    {
        float h = SlotHeight[slot];
        var made = new List<Piece>();
        var lamps = new Object[3];
        for (int c = 0; c < 3; c++)
        {
            Piece lamp = f.Part("Light 8", Col0 + c * ColStep, Face - 0.03f, h, 180f).Named("Row " + tag + " Lamp " + ColourName[c])
                .Set("PuzzleLight", "color", LampColour[c]).Set("PuzzleLight", "range", LampRange);
            lamps[c] = lamp.Component("PuzzleLight");
            made.Add(lamp);
        }
        Piece bar = f.Part("Progress Bar", BarX, Face - 0.03f, h, 180f).Named("Row " + tag + " Bar").Scaled(0.3f, 0.7f, 1f);
        bar.Set("PuzzleProgressBar", "total", 1);
        made.Add(bar);
        var pips = new MeshBuilder();
        for (int i = 0; i < row; i++)
            Kit.AddBox(pips, new Vector3(Col0 - 0.22f - i * 0.07f, h - 0.0225f, Face - 0.01f), new Vector3(0.045f, 0.045f, 0.02f), White);
        made.Add(f.Mesh("Row " + tag + " Pips", pips, bare, false));
        Piece order = f.Empty("Row " + tag, 0f, 0.3f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", sensors[0], sensors[1], sensors[2]);
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 1);
        Sdk.Set(pio, "holdAtEnd", false);
        made.Add(order);
        if (group != null) foreach (Piece p in made) p.Under(group);
        return new PanelRow { order = pio, bar = bar.Component("PuzzleProgressBar") };
    }

    // Every row r shown on panel P (in both lobby layouts) must have P at least 200 m uphill of where the skier stands
    // when r lights (row r-1, or the start gate) or at least 150 m below row r. Fails the build otherwise.
    static void AssertPanels(float[] along, Vector2 startPanel, Vector2 catPanel, Vector2 finishPanel)
    {
        float aStart = (startPanel - WinterIslePuzzle.Peak).magnitude, aCat = (catPanel - WinterIslePuzzle.Peak).magnitude, aFinish = (finishPanel - WinterIslePuzzle.Peak).magnitude;
        string[] names = { "start house", "cat track hut", "finish timing hut" };
        float[] at = { aStart, aCat, aFinish };
        int[][] shown = { new[] { 2, 2, 2, 0, 0, 0 }, new[] { 1, 1, 2, 2, 0, 0 } };      // 2-3 players, then 4
        string[] lobby = { "2-3 players", "4 players" };
        var bad = new List<string>();
        for (int v = 0; v < 2; v++)
            for (int r = 1; r <= 6; r++)
            {
                int p = shown[v][r - 1];
                float lo = along[r - 1] - 200f, hi = along[r] + 150f;
                bool inside = at[p] >= lo && at[p] <= hi;
                float margin = at[p] < lo ? lo - at[p] : at[p] - hi;
                string line = "row " + r + " on the " + names[p] + " (" + lobby[v] + "): panel at along " + Sdk.F(at[p]) + ", kept out of " + Sdk.F(lo) + "-" + Sdk.F(hi)
                    + (inside ? " INSIDE" : ", margin " + Sdk.F(margin) + " m");
                Debug.Log("[BWM] Downhill: " + line);
                if (inside) bad.Add(line);
            }
        if (bad.Count > 0) throw new System.Exception("Downhill: a panel can be read on the way down: " + string.Join("; ", bad));
    }

    // ---- helpers ----

    // The ground's height at a root-local point, above the root's level
    float GroundAt(Vector2 local) => b.WorldPoint(local.x, local.y, 0f, true).y - root.y;

    Vector2 World(Vector2 local)
    {
        Vector3 w = b.Root.transform.TransformPoint(new Vector3(local.x, 0f, local.y));
        return new Vector2(w.x, w.z);
    }

    // A light string post 2.6 m above the ground at a point of a frame
    static Vector3 RingPost(Frame f, float x, float z) { Vector2 p = f.Local(x, z); return new Vector3(p.x, 2.6f, p.y); }

    // A box between x0..x1, y0..y1, z0..z1
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);
}
