// Downhill: the pack run. A ski run down the middle run (run 1) that a group wins by skiing it together. No clock, no
// callers, nobody shut away, and the same course whatever the size of the lobby.
//
// What the players do. Turn the puzzle on at either end (the Start Board on the start pad beside the ski lift's top
// station, the panel in the finish shelter, or the start corner beside it), and walk through the red start gate on the
// pad two together: its lamp is lit while the puzzle is on. Below, six rows of three gates (red, green, blue, in a
// different order on every row) cross the run. Going through the start gate lights the lamp over one gate of row 1;
// skiing through that gate lights one in row 2, and so on down: each row's gate is picked at random at the moment the
// row before is passed, and its lamp stands on the gate itself, facing up the run. But a gate counts only while two
// skiers are inside its lane together, side by side or nose to tail (a lane runs 14 m down from its gate to the frame
// with the result bar): one skier alone lights nothing. The lit gate of every row, and then the two of them together
// anywhere between the chequered finish arch and the back of the shelter, win the gourd. Two skiers together in a wrong
// gate, or together on past a row whose lit gate was not taken, end the run: the lamps go out and the bars over that
// row's gates show the failure. Walking through the start gate again, two together, always deals a fresh run (and ends
// any run still on the course). A group of three or four sends any two, or all go: whichever two are in the lit gate
// together count. Nothing hurries anyone, and the first of a pair may brake in a lit gate and wait there for the other.
// Two players can finish it; one cannot.
//
// How it is made: the SDK's Press In Order chains and no custom code, as on the Slalom (SlalomPuzzle.cs). Each row is a
// chain of three pairs (a gate's lamp and the Presence Sensor over its lane, which takes two players) with one round,
// begun when the row before is passed; a wrong lane fails it. Press In Order tells of a right press only by filling its
// Progress Bars, so a row's three result bars are the chain's bars, and the first one's "full" counts one on the two
// rows-cleared bars (Start Board, finish shelter) and begins the next row. The start gate and the finish are chains of
// one pair, each with a small bar out of sight; a second chain at the finish, the finish watch, wins for a pair who were
// already standing in the finish lane when it was lit. A sensor right across the run below each row ends that row if it
// is still waiting when two skiers get there together. Everything hangs off the Start Switches: with the puzzle off no
// chain runs.
//
// What it was before: the Called Slalom, a timed run for one skier who could not see the live gates and was talked down
// by callers shut in three timing booths, with a timer ball to carry to the finish. Kept from it: the site and both
// far sites, the six rows and their coloured gates, the start pad and its gate, the finish shelter with the chequered
// board, the start corner, Calm Air over both ends and the light strings. Gone: the three booths with their lamp panels,
// clocks and walkie-talkies, the intercom and its fourteen speakers down the run, the ball's dispenser, holder and
// Validator, and the groups by lobby size. Rows 4 and 5 moved (see RowNominal).
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class DownhillPuzzle : PuzzleDef
{
    public override string Name => "Downhill";
    public override Vector2 Footprint => new Vector2(22f, 16f);
    // In the run-out below the finish arch (the layout's site: PisteFinish + direction * 34 - side * half * 0.2, as before)
    public override Vector2? FixedSite => new Vector2(-949.8f, -1459.3f);
    // The start pad (beside the ski lift's top station, off run 1's right-hand edge) and the place by the cat track where
    // the first design's third booth stood, where nothing stands now; the island levels both and keeps them clear
    public override IEnumerable<Vector3> ExtraSites => new[]
    {
        new Vector3(-1774.6f, -1786.5f, 6.4f),
        new Vector3(-1383.4f, -1625.9f, 3.9f),
    };
    static readonly Vector2 StartSite = new Vector2(-1774.6f, -1786.5f);

    // ---- the course ----
    const int Run = 1;                     // the middle run
    const int RowCount = 6;
    // How many skiers must be inside a lane together for its gate to count (the start gate and the finish too), and how
    // many together in the box below a row for the row to count as skied past. With CatchPack 1 any one skier below a
    // waiting row ends the run, as on the Slalom: but then a third of the pack skiing ahead ends it, and so does anyone
    // coming off the Slalom, whose run has joined this one by about along 950. Build with both at 1 to ski the course
    // alone as a test.
    const int Pack = 2, CatchPack = 2;
    const float WideHalf = 16f;            // a row slides within its window to where the run is at least this wide
    const float GateSpread = 10f, GateWidth = 4.5f, NarrowSpread = 8f, NarrowWidth = 4f;
    // A lane: the sensor box under a gate and down the run from it, 1 m under the snow to 4 m above. A free run reaches a
    // gate at 12 to 18 m/s; two skiers 6 m apart are then both inside for half a second, and a sensor looks ten times a
    // second, so a pair up to some 11 m apart still counts.
    const float LaneLong = 14f, LaneTall = 5f, LaneCentre = 1.5f;
    // Where the rows stand, in metres from the peak. Rows 1, 2, 3 and 6 are where the first design had them. Row 4 was at
    // 1030, where a bank now stands 4 m high in its left lane and the run is narrow: at 970 the run is wide and smooth and
    // the gates show from row 3 on. Row 5 was at 1137, under a brow that hid its gates until 23 m above them: at 1105 it
    // is on the shelf above the brow, in sight all the way from row 4 (the run is 14.5 m to a side there, so it is narrow).
    static readonly float[] RowNominal = { 560f, 680f, 800f, 970f, 1105f, 1240f };
    static readonly float[] RowLo = { 520f, 640f, 760f, 960f, 1095f, 1200f };
    static readonly float[] RowHi = { 600f, 720f, 840f, 980f, 1105f, 1280f };
    // each row's colours from left to right looking downhill (0 red, 1 green, 2 blue)
    static readonly int[][] RowOrder = { new[] { 0, 1, 2 }, new[] { 2, 0, 1 }, new[] { 1, 2, 0 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 2, 1, 0 } };
    static readonly string[] ColourName = { "Red", "Green", "Blue" };
    static readonly Color[] GateColour = { IsleKit.Red, new Color(0.1f, 0.6f, 0.24f), new Color(0.14f, 0.3f, 0.86f) };
    static readonly Color White = new Color(0.95f, 0.95f, 0.92f), Black = new Color(0.1f, 0.1f, 0.12f);
    // Every lamp on the course is this warm white and means the same: through here next, two together. (A lamp's colour
    // tints its lens whether it is lit or not: near-white is a dull disc when off and a bright one when lit.)
    static readonly Color GoLight = new Color(1f, 0.92f, 0.75f);
    // a gate: two poles and a banner over the lane's upper end, high over a skier's head
    const float BannerLow = 2.6f, BannerTall = 0.8f;
    // A gate's lamp is the game's flood lamp (Light 6: 0.48 m across, its back 0.142 m behind its pivot, its middle 0.017 m
    // under it) on a dark board standing on the gate's banner, facing up the run. Range and intensity are the Slalom's.
    const float LampBoardWide = 1.0f, LampBoardTall = 1.0f, SignDeep = 0.06f;
    const float LampOut = 0.142f + 0.01f, LampDrop = 0.017f, LampRange = 16f, LampIntensity = 3f;
    // a lane's lower end: a strip in the gate's colour between two poles, and on it a dark board with the result bar on
    // its uphill face, in front of a skier from the moment they enter the lane (a Progress Bar's back is 0.101 m behind its pivot)
    const float ExitLow = 2.0f, ExitTall = 0.3f, BarBoardWide = 2.6f, BarBoardTall = 0.8f, BarOut = 0.101f + 0.01f;
    // The catch box below a row: right across the run and a metre beyond each edge, from 8 to 32 m below the row's middle
    // (the lanes end at 7), so that a pair strung out too far for a lane is still seen together here. It lies along the
    // slope and is made as tall as its ground needs, from 1.5 m under the lowest snow in it to 5 m over the highest: the
    // run falls away across its width as well (by 9 m from edge to edge below row 3), and row 5's lies over a brow.
    const float CatchFrom = 8f, CatchLong = 24f, CatchBeyond = 1f, CatchUnder = 1.5f, CatchOver = 5f;
    // the sight check: a skier's eyes over the snow, and the least distance above its gates at which a row must be in sight
    const float EyeHeight = 0.6f, MinSight = 35f;

    // ---- the two ends ----
    const float PadTop = 0.15f;            // floors stand this far above the levelled ground
    // the start pad (frame: +z down the fall line, +x right looking downhill, so the run lies towards -x): the red gate
    const float StartGateX = -2f, StartGateZ = 3f, StartGateTop = 2.95f;
    // the finish (frame: +z down the run-out): the arch, and the lane from it to the back wall inside the shelter
    const float ArchZ = -10.5f, ArchLeft = -4f, ArchRight = 7.5f, ArchTop = 4.5f, ShelterBack = 1.25f;
    // the dark faces with a switch on them: the Start Board on its posts, and a panel on the shelter's back wall
    const float BoardWide = 2.0f, BoardLow = 0.9f, BoardHigh = 1.9f, BoardDeep = 0.12f;

    PuzzleBuilder b;
    Vector3 root;
    Vector2 site;
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

    // One row as built: where it stands; from left to right as a skier sees them, its lanes' sensors, the lamps on its
    // gates (and where those are, root-local) and the result bars over its exits; and the box that catches a pair skiing past
    sealed class Row
    {
        public float along;
        public readonly Component[] lanes = new Component[3], lamps = new Component[3], bars = new Component[3];
        public readonly Vector3[] lampAt = new Vector3[3];
        public Component catcher;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        root = b.Root.transform.position;
        site = new Vector2(root.x, root.z);
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        Sdk.Set(b.Puzzle, "gourdColor", IsleKit.Gold);      // the mountain's long run keeps its gold gourd

        // ---- frames: the finish (+z downhill along the run-out, +x right looking downhill) and the start pad ----
        WinterIslePuzzle.PisteFinish(out Vector2 direction, out _);
        float runYaw = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        var finish = new Frame(b, Vector2.zero, runYaw, GroundAt(Vector2.zero) + PadTop);
        Vector2 startLocal = StartSite - site;
        float startAlong = (StartSite - WinterIslePuzzle.Peak).magnitude, startYaw = FallYaw(startAlong);
        var start = new Frame(b, startLocal, startYaw, GroundAt(startLocal) + PadTop);

        Vector2 debug = finish.Local(0f, -4.5f);
        b.DebugTeleport(debug.x, debug.y);
        Vector2 clearAt = finish.Local(0.5f, 1.5f);
        b.ClearGround(22f, 16f, clearAt.x, clearAt.y).Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, runYaw, 0f);
        b.ClearGround(10f, 8f, startLocal.x, startLocal.y).Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, startYaw, 0f);

        // ---- the start pad: a level stone pad, the red start gate with its lamp, the Start Board ----
        var pad = new MeshBuilder();
        Block(pad, -5f, 5f, -1.15f, 0f, -4f, 4f, IsleKit.Stone);
        start.Mesh("Start Pad", pad, snowy);
        var gate = new MeshBuilder();
        Block(gate, -3.68f, -3.52f, 0f, 3.0f, 2.92f, 3.08f, IsleKit.Red);
        Block(gate, -0.48f, -0.32f, 0f, 3.0f, 2.92f, 3.08f, IsleKit.Red);
        Block(gate, -3.52f, -0.48f, 2.45f, StartGateTop, 2.97f, 3.03f, IsleKit.Red);
        for (int i = 0; i < 12; i++)
            Block(gate, -3.5f + i * 0.25f, -3.5f + (i + 1) * 0.25f, 2.6f, 2.8f, 2.95f, 2.97f, i % 2 == 0 ? White : Black);
        Block(gate, StartGateX - LampBoardWide / 2f, StartGateX + LampBoardWide / 2f, StartGateTop, StartGateTop + LampBoardTall,
            StartGateZ - SignDeep / 2f, StartGateZ + SignDeep / 2f, IsleKit.Board);      // the lamp's board, on the beam
        start.Mesh("Start Gate", gate, bare);
        // (the lamp faces the pad, up the hill: whoever stands on the pad sees whether the gate is live)
        Component startLamp = GoLamp(start.Part("Light 6", StartGateX, StartGateZ - SignDeep / 2f - LampOut, StartGateTop + LampBoardTall / 2f + LampDrop, 180f).Named("Start Lamp"));
        Kit.LightString(b, "Start Gate Lights", new List<Vector3> { start.Post(-3.6f, 3.0f, 2.95f), start.Post(-0.4f, 3.0f, 2.95f) }, 0.3f, 0.4f, false, false, 51);
        // the start lane: the gateway itself, 1.5 m either side of the beam; the two walk through it together
        Component startLane = Zone(start.Part("Presence Sensor", StartGateX, StartGateZ, 1.0f, 0f).Named("Start Lane"), new Vector3(3.0f, 3.0f, 3.0f), Pack);
        // the Start Board, beside the gate and facing the pad as the gate's lamp does: the switch on the half nearer the gate
        Frame startBoard = start.Sub(2.0f, 2.6f, 180f);
        var board = new MeshBuilder();
        foreach (int e in new[] { -1, 1 })
            Block(board, e * (BoardWide / 2f + 0.12f) - 0.1f, e * (BoardWide / 2f + 0.12f) + 0.1f, -0.4f, BoardHigh + 0.3f, -0.1f, 0.1f, IsleKit.Wood);
        Block(board, -BoardWide / 2f, BoardWide / 2f, BoardLow, BoardHigh, -BoardDeep / 2f, BoardDeep / 2f, IsleKit.Board);
        Block(board, -BoardWide / 2f - 0.35f, BoardWide / 2f + 0.35f, BoardHigh + 0.3f, BoardHigh + 0.42f, -0.25f, 0.35f, IsleKit.DarkWood);      // a little roof against the snow
        startBoard.Mesh("Start Board", board, snowy);
        Component startRows = BoardParts(startBoard, "start", BoardDeep / 2f, 1f, out Component startRelay);
        CourseCard(startBoard, BoardDeep / 2f, -1f);

        // ---- the course: six rows of three lanes, each row's catch box below it ----
        var rows = new Row[RowCount];
        for (int r = 0; r < RowCount; r++) rows[r] = BuildRow(r);
        for (int r = 0; r < RowCount; r++)
        {
            float entry = rows[r].along - LaneLong / 2f, from = r == 0 ? startAlong + 8f : rows[r - 1].along;
            float sight = SightAbove(from, entry, rows[r].lampAt);
            string line = "[BWM] Downhill: row " + (r + 1) + "'s lamps are all in sight from " + Sdk.F(sight) + " m above its gates (it is dealt " + Sdk.F(entry - from) + " m above them)";
            if (sight < MinSight) Debug.LogWarning(line + ": too late to steer for, move the row");
            else Debug.Log(line);
        }

        // ---- the finish: the shelter with its panel, the chequered board behind it, the arch, the start corner ----
        var shelter = new MeshBuilder();
        Block(shelter, -2f, 2f, -1.15f, 0f, -1.5f, 1.5f, IsleKit.Stone);         // floor, sunk about a metre
        Block(shelter, -2f, 2f, 0f, 2.6f, ShelterBack, 1.5f, IsleKit.Wood);      // back wall (downhill)
        Block(shelter, -2f, -1.75f, 0f, 2.6f, -1.5f, ShelterBack, IsleKit.Wood);
        Block(shelter, 1.75f, 2f, 0f, 2.6f, -1.5f, ShelterBack, IsleKit.Wood);
        Block(shelter, -2.3f, 2.3f, 2.6f, 2.8f, -1.8f, 1.8f, IsleKit.DarkWood);   // roof
        Block(shelter, -1.75f, 1.75f, 2.25f, 2.6f, -1.5f, -1.3f, IsleKit.Wood);  // beam over the open uphill side
        Block(shelter, -BoardWide / 2f, BoardWide / 2f, BoardLow, BoardHigh, ShelterBack - 0.04f, ShelterBack, IsleKit.Board);      // the panel on the back wall
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
        // the panel faces up the run-out, out of the shelter's open side: the switch on its right as the run goes
        Component finishRows = BoardParts(finish.Sub(0f, ShelterBack - 0.04f, 180f), "finish", 0f, -1f, out Component finishRelay);

        // The finish arch, across the run-out 9 m above the shelter: two red posts, a chequered beam, the finish lamp on a
        // board over it. Nothing of it stops a skier. Red pennants run from its feet down both edges of the finish lane.
        float archMid = (ArchLeft + ArchRight) / 2f;
        var arch = new MeshBuilder();
        foreach (float x in new[] { ArchLeft - 0.2f, ArchRight + 0.2f })
            Block(arch, x - 0.1f, x + 0.1f, -0.4f, ArchTop + 0.1f, ArchZ - 0.1f, ArchZ + 0.1f, IsleKit.Red);
        Block(arch, ArchLeft - 0.1f, ArchRight + 0.1f, ArchTop - 0.6f, ArchTop, ArchZ - 0.04f, ArchZ + 0.04f, IsleKit.Red);
        int squares = Mathf.RoundToInt((ArchRight - ArchLeft) / 0.3f);
        float square = (ArchRight - ArchLeft) / squares;
        for (int i = 0; i < squares; i++)
            for (int j = 0; j < 2; j++)
                Block(arch, ArchLeft + i * square, ArchLeft + (i + 1) * square, ArchTop - 0.6f + j * 0.3f, ArchTop - 0.3f + j * 0.3f, ArchZ - 0.06f, ArchZ - 0.04f, (i + j) % 2 == 0 ? Black : White);
        Block(arch, archMid - LampBoardWide / 2f, archMid + LampBoardWide / 2f, ArchTop, ArchTop + LampBoardTall, ArchZ - SignDeep / 2f, ArchZ + SignDeep / 2f, IsleKit.Board);
        for (float z = ArchZ + 2.5f; z < ShelterBack - 1f; z += 2.5f)
            foreach (float x in new[] { ArchLeft, ArchRight })
            {
                Block(arch, x - 0.015f, x + 0.015f, -0.25f, 0.9f, z - 0.015f, z + 0.015f, IsleKit.DarkWood);
                Block(arch, x - 0.01f, x + 0.01f, 0.66f, 0.88f, z + 0.01f, z + 0.31f, IsleKit.Red);
            }
        finish.Mesh("Finish Arch", arch, bare, false);
        Component finishLamp = GoLamp(finish.Part("Light 6", archMid, ArchZ - SignDeep / 2f - LampOut, ArchTop + LampBoardTall / 2f + LampDrop, 180f).Named("Finish Lamp"));
        Kit.LightString(b, "Finish Arch Lights", new List<Vector3> { finish.Post(ArchLeft - 0.2f, ArchZ, ArchTop - 0.6f), finish.Post(ArchRight + 0.2f, ArchZ, ArchTop - 0.6f) },
            0.4f, 0.5f, false, false, 52);
        // The finish lane: from the arch to the shelter's back wall, level ground all of it. Skiers pass through it or, better,
        // brake and stop in it: the first to arrive waits there for the second.
        Component finishLane = Zone(finish.Part("Presence Sensor", archMid, (ArchZ + ShelterBack) / 2f, 1.6f, 0f).Named("Finish Lane"),
            new Vector3(ArchRight - ArchLeft, 5f, ShelterBack - ArchZ), Pack);
        // The finish watch's lamp and bar (see the chains): neither is for seeing, so both are sunk in the shelter's stone
        // floor, the lamp's light held within a hand's breadth
        Component watchLamp = finish.Part("Light 8", 0f, 0f, -0.5f, 0f).Named("Finish Watch Lamp").Set("PuzzleLight", "range", 0.05f).Component("PuzzleLight");
        Component watchRelay = finish.Part("Progress Bar", 0.6f, 0f, -0.5f, 0f).Named("Relay Bar (finish watch)").Scaled(0.05f, 0.05f, 0.05f)
            .Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");

        Vector2 corner = finish.Local(-7.5f, -1.5f);
        IsleKit.StartCorner(b, corner.x, corner.y, runYaw + 180f);
        Kit.LightString(b, "Finish Lights", new List<Vector3>
        {
            RingPost(finish, -10.5f, -5f), RingPost(finish, -10.5f, 1f), RingPost(finish, -10.5f, 7f), RingPost(finish, 0.25f, 7f),
            RingPost(finish, 11f, 7f), RingPost(finish, 11f, 1f), RingPost(finish, 11f, -5f),
        }, 0.4f, 0.5f, true, true, 17);      // three sides, open uphill where the skiers come in

        // ---- Calm Air over each end (the course itself stays open, as it always was) ----
        Vector2 calm = finish.Local(0.5f, 1.5f);
        IsleKit.CalmAir(b, "Downhill Finish", calm.x, calm.y, new Vector3(30f, 120f, 24f), runYaw, -10f);
        IsleKit.CalmAir(b, "Downhill Start", startLocal.x, startLocal.y, new Vector3(18f, 120f, 16f), startYaw, -10f);

        // ---- the Press In Order chains ----
        Component startChain = Chain("Start Chain", new[] { startLamp }, new[] { startLane }, new[] { startRelay });
        var rowChain = new Component[RowCount];
        for (int r = 0; r < RowCount; r++)
        {
            rowChain[r] = Chain("Row Chain " + (r + 1), rows[r].lamps, rows[r].lanes, rows[r].bars);
            // past the row without its lit gate: a pair reaching this box while the row still waits ends it (End does nothing
            // to a row already passed or failed, or with the puzzle off)
            Sdk.Wire(rows[r].catcher, "onPressed", rowChain[r], "End");
        }
        Component finishChain = Chain("Finish Chain", new[] { finishLamp }, new[] { finishLane }, new[] { finishRelay });
        // The finish watch. A Presence Sensor presses only when it fills, so a pair already standing in the finish lane when
        // the last row is passed (two of four waiting in the shelter for the two who ski) would never press it, and the
        // skiers' arrival would change nothing. So a second chain on the same sensor, in Press In Order's timed mode,
        // asks once, half a second after the finish is lit, whether the lane is full at that moment, and wins if it is. If
        // it is not, the lane can only fill later by somebody coming in, and that press is the finish chain's.
        Component finishWatch = Chain("Finish Watch", new[] { watchLamp }, new[] { finishLane }, new[] { watchRelay });
        Sdk.Set(finishWatch, "holdAtEnd", true);
        Sdk.Set(finishWatch, "phaseSeconds", 0.5f);

        // ---- wiring ----
        Component sdk = b.Puzzle;
        // every bar a run fills (the rows' result bars, the finish's two bars and the two rows-cleared bars), and every
        // chain below the start gate
        var runBars = new List<Component> { finishRelay, watchRelay, startRows, finishRows };
        foreach (Row row in rows) runBars.AddRange(row.bars);
        var chains = new List<Component>(rowChain) { finishChain, finishWatch };
        // the switch: on arms the start gate; off ends every chain (their lamps go out) and empties every bar
        Sdk.Wire(sdk, "onPuzzleStart", startChain, "Begin");
        foreach (Component chain in chains) Sdk.Wire(sdk, "onPuzzleStop", chain, "End");
        Sdk.Wire(sdk, "onPuzzleStop", startChain, "End");
        Sdk.Wire(sdk, "onPuzzleStop", startRelay, "Reset");
        foreach (Component bar in runBars) Sdk.Wire(sdk, "onPuzzleStop", bar, "Reset");
        // Through the start gate: whatever run was on the course ends, the bars empty, row 1 is dealt, and the start arms
        // itself again. That last Begin runs inside the start chain's own press (its bar filling is what fires this event):
        // it empties the bar again, so Press In Order finds its round unfinished and lights the start lamp once more half a
        // second later. The start chain must keep this bar as its only one for that.
        foreach (Component chain in chains) Sdk.Wire(startRelay, "onFull", chain, "End");
        foreach (Component bar in runBars) Sdk.Wire(startRelay, "onFull", bar, "Reset");
        Sdk.Wire(startRelay, "onFull", rowChain[0], "Begin");
        Sdk.Wire(startRelay, "onFull", startChain, "Begin");
        // each row passed counts one on both rows-cleared bars and deals the next; the last one lights the finish arch
        for (int r = 0; r < RowCount; r++)
        {
            Component passed = rows[r].bars[0];
            Sdk.Wire(passed, "onFull", startRows, "Increment");
            Sdk.Wire(passed, "onFull", finishRows, "Increment");
            Sdk.Wire(passed, "onFull", r + 1 < RowCount ? rowChain[r + 1] : finishChain, "Begin");
        }
        Sdk.Wire(rows[RowCount - 1].bars[0], "onFull", finishWatch, "Begin");
        // two together in the finish lane after every row, by either chain: the rows-cleared bars fill, the gourd is won,
        // and the other chain is put out (the puzzle stays on after a win, until somebody turns it off)
        foreach (Component relay in new[] { finishRelay, watchRelay })
        {
            Sdk.Wire(relay, "onFull", startRows, "Increment");
            Sdk.Wire(relay, "onFull", finishRows, "Increment");
            Sdk.Wire(relay, "onFull", sdk, "Win");
        }
        Sdk.Wire(finishRelay, "onFull", finishWatch, "End");
        Sdk.Wire(watchRelay, "onFull", finishChain, "End");
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

    // One of the course's lamps: warm white, lighting the snow in front of it
    static Component GoLamp(Piece lamp)
        => lamp.Set("PuzzleLight", "color", GoLight).Set("PuzzleLight", "range", LampRange).Set("PuzzleLight", "intensity", LampIntensity).Component("PuzzleLight");

    // Makes a Presence Sensor one box of this size (across, up, along) that takes this many players
    static Component Zone(Piece sensor, Vector3 size, int players)
    {
        BoxCollider box = sensor.Go.GetComponent<BoxCollider>();
        if (box == null) box = sensor.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        sensor.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", players);
        return sensor.Component("PuzzlePresenceSensor");
    }

    // ---- the two ends ----

    // What each end of the course has on a dark face that looks along a frame's +z from z = 'face': a start switch and
    // its live lamp to one side ('side' is +1 or -1 along the frame's x) and the rows-cleared bar across the top, which
    // is returned. 'relay' is the one bar of the chain at this end (the start gate's or the finish's): Press In Order
    // needs it to report a press, nobody needs to see it, so it is 18 cm long and behind the face.
    Component BoardParts(Frame f, string tag, float face, float side, out Component relay)
    {
        f.Part("Switch Start", side * 0.6f, face + 0.073f, 1.3f, 0f).Named("Start Switch (" + tag + ")");
        f.Part("Live Light", side * 0.6f, face, 1.52f, 0f).Named("Live Light (" + tag + ")");
        relay = f.Part("Progress Bar", 0f, face - 0.08f, 1.2f, 0f).Named("Relay Bar (" + tag + ")").Scaled(0.05f, 0.05f, 0.05f)
            .Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        return f.Part("Progress Bar", 0f, face + 0.07f, 1.75f, 0f).Named("Rows Bar (" + tag + ")").Scaled(0.5f, 0.5f, 1f)
            .Set("PuzzleProgressBar", "total", RowCount + 1).Component("PuzzleProgressBar");
    }

    // The course card on the Start Board's other half: three gates as the pack comes down on them, the lamp over the
    // middle one lit, and two ski tracks through that gate side by side
    void CourseCard(Frame f, float face, float side)
    {
        var card = new MeshBuilder();
        void Mark(float x0, float x1, float y0, float y1, float proud, Color c)
            => Block(card, Mathf.Min(side * x0, side * x1), Mathf.Max(side * x0, side * x1), y0, y1, face, face + proud, c);
        Mark(0.05f, 0.9f, 0.95f, 1.55f, 0.015f, IsleKit.Cream * 0.35f + IsleKit.Board * 0.65f);
        for (int k = 0; k < 3; k++)
        {
            float u = 0.22f + 0.255f * k;
            bool lit = k == 1;
            Mark(u - 0.095f, u + 0.095f, 1.12f, 1.19f, 0.025f, GateColour[k]);                          // the gate's banner
            Mark(u - 0.045f, u + 0.045f, 1.23f, 1.32f, 0.025f, lit ? GoLight : IsleKit.DarkWood);      // its lamp
            if (!lit) continue;
            foreach (float t in new[] { -0.04f, 0.04f })
            {
                Mark(u + t - 0.012f, u + t + 0.012f, 1.34f, 1.52f, 0.025f, White);      // the two tracks down to the gate
                Mark(u + t - 0.012f, u + t + 0.012f, 1.0f, 1.11f, 0.025f, White);       // and on below it
            }
        }
        f.Mesh("Course Card", card, bare, false);
    }

    // ---- the course ----

    // Row r: its three lanes (a gate with its lamp at the upper end, pennants down both edges, the result bar at the
    // lower end, the sensor box between) and the catch box below it
    Row BuildRow(int r)
    {
        var row = new Row();
        row.along = ChooseAlong(r, out float half, out bool narrow);
        Vector2 centre = WinterIslePuzzle.RunCentre(Run, row.along, out _) - site;
        Vector2 down = RunDown(row.along);
        var side = new Vector2(down.y, -down.x);
        float yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
        float spread = narrow ? NarrowSpread : GateSpread, width = narrow ? NarrowWidth : GateWidth;
        Debug.Log("[BWM] Downhill: row " + (r + 1) + " at along " + Sdk.F(row.along) + " (window " + Sdk.F(RowLo[r]) + "-" + Sdk.F(RowHi[r])
            + "), half-width " + Sdk.F(half) + (narrow ? " NARROW: gates at +-8 m, 4 m wide (no pitch of 16 m half-width in its window)" : ""));
        var mb = new MeshBuilder();
        for (int p = 0; p < 3; p++)
        {
            int colour = RowOrder[r][p];
            string tag = (r + 1) + " " + ColourName[colour];
            Vector2 mid = centre + side * ((p - 1) * spread), entry = mid - down * (LaneLong / 2f), exit = mid + down * (LaneLong / 2f);
            // the lane's box, tilted with the slope so that it holds a skier over its whole length
            float pitch = Mathf.Atan2(GroundAt(entry) - GroundAt(exit), LaneLong) * Mathf.Rad2Deg;
            Piece sensor = b.Part("Presence Sensor", mid.x, mid.y, LaneCentre, yaw).Named("Lane " + tag);
            sensor.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
            row.lanes[p] = Zone(sensor, new Vector3(width, LaneTall, LaneLong), Pack);
            // the gate, and the lamp on its board's uphill face
            AddGate(mb, entry, down, width, colour);
            float lampHigh = BannerLow + BannerTall + LampBoardTall / 2f + LampDrop;
            Vector2 lampOn = entry - down * (SignDeep / 2f + LampOut);
            row.lamps[p] = GoLamp(b.Part("Light 6", lampOn.x, lampOn.y, lampHigh + GroundAt(entry) - GroundAt(lampOn), yaw + 180f).Named("Lamp " + tag));
            row.lampAt[p] = new Vector3(entry.x, GroundAt(entry) + lampHigh, entry.y);
            // pennants in the gate's colour down both edges (nothing on the run has a collider: skiers pass through it all)
            for (int e = -1; e <= 1; e += 2)
                for (int t = -1; t <= 1; t++)
                {
                    Vector2 at = mid + side * (e * width / 2f) + down * (t * LaneLong / 4f);
                    Kit.AddBox(mb, Foot(at) + Vector3.down * 0.1f, new Vector3(0.03f, 1.0f, 0.03f), IsleKit.DarkWood, yaw);
                    Kit.AddBox(mb, Foot(at + down * 0.16f) + Vector3.up * 0.66f, new Vector3(0.02f, 0.22f, 0.3f), GateColour[colour], yaw);
                }
            // the lower end: a strip between two poles, the board on it and the result bar on the board's uphill face
            Vector3 foot = Foot(exit);
            for (int e = -1; e <= 1; e += 2) Pole(mb, exit + side * (e * width / 2f), foot.y + ExitLow + ExitTall, 0.07f, GateColour[colour], yaw);
            Kit.AddBox(mb, foot + Vector3.up * ExitLow, new Vector3(width, ExitTall, 0.04f), GateColour[colour], yaw);
            Kit.AddBox(mb, foot + Vector3.up * (ExitLow + ExitTall), new Vector3(BarBoardWide, BarBoardTall, SignDeep), IsleKit.Board, yaw);
            Vector2 barOn = exit - down * (SignDeep / 2f + BarOut);
            row.bars[p] = b.Part("Progress Bar", barOn.x, barOn.y, ExitLow + ExitTall + BarBoardTall / 2f + foot.y - GroundAt(barOn), yaw + 180f).Named("Result " + tag)
                .Scaled(0.6f, 0.8f, 1f).Set("PuzzleProgressBar", "total", 1).Component("PuzzleProgressBar");
        }
        b.MeshPiece("Row " + (r + 1) + " Gates", mb.ToMesh(), bare, false);

        // the catch box
        float top = row.along + CatchFrom, middle = top + CatchLong / 2f;
        Vector2 cMid = WinterIslePuzzle.RunCentre(Run, middle, out _) - site, cDown = RunDown(middle);
        var cSide = new Vector2(cDown.y, -cDown.x);
        float cYaw = Mathf.Atan2(cDown.x, cDown.y) * Mathf.Rad2Deg, ground = GroundAt(cMid);
        float fall = (GroundAt(cMid - cDown * (CatchLong / 2f)) - GroundAt(cMid + cDown * (CatchLong / 2f))) / CatchLong, cPitch = Mathf.Atan(fall);
        float reach = Mathf.Max(HalfAt(top), Mathf.Max(HalfAt(middle), HalfAt(top + CatchLong))) + CatchBeyond;
        // how far the snow lies under and over the plane through the box's middle, measured along the box's own up
        float low = 0f, high = 0f;
        for (int i = -8; i <= 8; i++)
            for (int j = -6; j <= 6; j++)
            {
                float x = reach * i / 8f, z = CatchLong / 2f * j / 6f, plane = ground - z * fall;
                float over = (GroundOr(cMid + cSide * x + cDown * z, plane) - plane) * Mathf.Cos(cPitch);
                low = Mathf.Min(low, over);
                high = Mathf.Max(high, over);
            }
        Piece catcher = b.Part("Presence Sensor", cMid.x, cMid.y, 0f, cYaw).Named("Row " + (r + 1) + " Catch");
        catcher.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(cPitch * Mathf.Rad2Deg, cYaw, 0f);
        row.catcher = Zone(catcher, new Vector3(2f * reach, high + CatchOver - (low - CatchUnder), CatchLong), CatchPack);
        catcher.Go.GetComponent<BoxCollider>().center = new Vector3(0f, (high + CatchOver + low - CatchUnder) / 2f, 0f);
        return row;
    }

    // Slides a row within its window to the nearest along where the run is wide enough over the lane's length
    static float ChooseAlong(int r, out float half, out bool narrow)
    {
        float best = RowNominal[r], bestHalf = -1f;
        for (int d = 0; d <= 120; d++)
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                if (d == 0 && sign < 0) continue;
                float a = RowNominal[r] + sign * d;
                if (a < RowLo[r] || a > RowHi[r]) continue;
                float h = Mathf.Min(HalfAt(a), Mathf.Min(HalfAt(a - LaneLong / 2f), HalfAt(a + LaneLong / 2f)));
                if (h >= WideHalf) { half = h; narrow = false; return a; }
                if (h > bestHalf) { bestHalf = h; best = a; }
            }
        half = bestHalf;
        narrow = true;
        return best;
    }

    static float HalfAt(float along) { WinterIslePuzzle.RunCentre(Run, along, out float half); return half; }

    // the way the run goes at a distance from the peak
    static Vector2 RunDown(float along)
        => (WinterIslePuzzle.RunCentre(Run, along + 6f, out _) - WinterIslePuzzle.RunCentre(Run, along, out _)).normalized;

    static float FallYaw(float along)
    {
        Vector2 down = RunDown(along);
        return Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
    }

    // How far above a row's gates (whose upper end is at 'entry' metres from the peak) all three of its lamps are in sight
    // of a skier coming down the middle of the run from 'from': only the ground between can hide them, as a brow does
    float SightAbove(float from, float entry, Vector3[] lamps)
    {
        float hiddenTo = from;
        for (float a = from; a < entry - 4f; a += 2f)
        {
            Vector2 at = WinterIslePuzzle.RunCentre(Run, a, out _) - site;
            var eye = new Vector3(at.x, GroundAt(at) + EyeHeight, at.y);
            bool hidden = false;
            foreach (Vector3 lamp in lamps)
                for (int k = 1; k < 60 && !hidden; k++)
                {
                    Vector3 q = Vector3.Lerp(eye, lamp, k / 60f);
                    hidden = GroundOr(new Vector2(q.x, q.z), float.MinValue) > q.y + 0.05f;
                }
            if (hidden) hiddenTo = a;
        }
        return entry - hiddenTo;
    }

    // A slalom gate across a lane's upper end at a root-local point: two poles, a banner in the gate's colour with its
    // shape in white on both faces (triangle red, square green, disc blue: for colour-blind players), and standing on
    // the banner the dark board that carries the gate's lamp
    void AddGate(MeshBuilder mb, Vector2 at, Vector2 down, float width, int colour)
    {
        var side = new Vector2(down.y, -down.x);
        var fwd = new Vector3(down.x, 0f, down.y);
        var right = new Vector3(side.x, 0f, side.y);
        float yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
        Vector3 foot = Foot(at);
        Color c = GateColour[colour];
        for (int e = -1; e <= 1; e += 2) Pole(mb, at + side * (e * width / 2f), foot.y + BannerLow + BannerTall, 0.08f, c, yaw);
        Kit.AddBox(mb, foot + Vector3.up * BannerLow, new Vector3(width, BannerTall, 0.05f), c, yaw);
        AddShape(mb, foot + Vector3.up * (BannerLow + BannerTall / 2f) - fwd * 0.035f, right, Vector3.up, colour, 0.3f, White);
        AddShape(mb, foot + Vector3.up * (BannerLow + BannerTall / 2f) + fwd * 0.035f, right, Vector3.up, colour, 0.3f, White);
        Kit.AddBox(mb, foot + Vector3.up * (BannerLow + BannerTall), new Vector3(LampBoardWide, LampBoardTall, SignDeep), IsleKit.Board, yaw);
    }

    // A pole standing on its own ground at a root-local point, its top at 'top' above the root's level: a gate's two
    // poles end level with each other however the run falls away across it
    void Pole(MeshBuilder mb, Vector2 at, float top, float thick, Color color, float yaw)
    {
        float from = GroundAt(at) - 0.3f;
        Kit.AddBox(mb, new Vector3(at.x, from, at.y), new Vector3(thick, top - from, thick), color, yaw);
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

    // ---- helpers ----

    // The ground's height at a root-local point, above the root's level
    float GroundAt(Vector2 local) => b.WorldPoint(local.x, local.y, 0f, true).y - root.y;

    // ...and where there may be no ground to ask about, 'otherwise'
    float GroundOr(Vector2 local, float otherwise)
    {
        Vector3 world = b.Root.transform.TransformPoint(new Vector3(local.x, 0f, local.y));
        return b.Ground.At(world.x, world.z, out float y) ? y - root.y : otherwise;
    }

    // a root-local point on the ground, as a mesh's vertex (y above the root's level)
    Vector3 Foot(Vector2 local) => new Vector3(local.x, GroundAt(local), local.y);

    // A light string post 2.6 m above the ground at a point of a frame
    static Vector3 RingPost(Frame f, float x, float z) { Vector2 p = f.Local(x, z); return new Vector3(p.x, 2.6f, p.y); }

    // A box between x0..x1, y0..y1, z0..z1
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);
}
