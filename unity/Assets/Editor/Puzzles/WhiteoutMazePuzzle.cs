// Whiteout Maze (puzzle 18, blind mask): a maze of snow walls seven cells by seven, too tall to see over, with a cairn in
// eight of its dead ends and, over each cairn, a picture lying face up on a mast where only someone high above can read
// it. A lamp board hidden in a closet behind the signal hut, 13 m beyond the maze, lights one lamp at a time with a
// picture beside it. The Reader in the hut names the lit lamp's picture; the Spotter on a fenced gantry 9.5 m up finds
// the cairn showing it and talks the blindfolded Walker there through the maze. Stepping into the wrong dead end stops
// the puzzle; six cairns (five, in pairs, in a 4-player lobby) before the clock runs out win it. The board's shutter is
// open only while someone stands on the gantry, so one player can never be reader and spotter.
//
// On every start the custom script WhiteoutDeal (Assets/PuzzleScripts) deals the pad sensors out afresh among the eight
// alcoves (which lamp means which cairn) and gives every cairn a new picture, then Begins the Press In Order 0.6 s later.
// The maze itself is generated here, deterministically: a depth-first carve from a searched seed (growing tree if no
// seed qualifies), cairns in the dead ends farthest apart, gantry posts only where maze walls meet.
//
// Coordinates: design metres, maze centred at (0, 0), x east, z north (towards the hut); the build adds (OffX, OffZ) so
// the 34 x 46 footprint is centred on the root. Heights above the (levelled) ground. Spec: puzzle-specs\WhiteoutMaze.md.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WhiteoutMazePuzzle : PuzzleDef
{
    public override string Name => "WhiteoutMaze";
    public override Vector2 Footprint => new Vector2(34f, 46f);
    public override Vector2? FixedSite => new Vector2(-753f, -1251f);      // East Lowland flats (layout-final)

    // design -> build: the layout runs x -11..18 and z -16..27 about the maze's centre
    const float OffX = -2.5f, OffZ = -5.5f;

    const int N = 7, Cairns = 8;
    const float CellSize = 2.6f, X0 = -N * CellSize / 2f;      // wall lines at X0 + 2.6 k (-9.1 .. 9.1)
    const float WallT = 0.45f, WallBody = 2.0f, Ridge = 0.35f;  // WallH 2.35 (E + J + 0.3 with E = J = 1.0)
    const float Sink = 0.3f, Eps = 0.001f;
    const float DeckH = 9.5f, DeckZ0 = 0.6f, DeckZ1 = 2.0f, DeckX0 = -9.4f, DeckX1 = 13.8f;
    const float FenceH = 2.6f;
    const float PicH = 3.0f, RimUp = 0.12f;                     // cairn pictures face up at PicH, in a tray whose rim stands RimUp above
    const float SensorH = 2.6f;
    const float HookUp = 1.55f, ItemUp = 1.3f;                  // as IsleKit.BlindfoldHook: hook 0.03 m, item 0.31 m in front of the face
    const float DoorHalf = 0.62f;                               // the game's Door is 1.24 m wide, 2.02 high
    const float GateL = 1.11f, GateR = 1.01f;                   // "Gate No items" runs -1.11 .. +1.01 m across at yaw 0

    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);
    static readonly Color LampGold = new Color(0.95f, 0.72f, 0.22f);

    PuzzleBuilder b;

    static float Line(int k) => X0 + k * CellSize;
    static float Mid(int i) => X0 + CellSize / 2f + i * CellSize;
    static float X(float x) => x + OffX;
    static float Z(float z) => z + OffZ;
    Piece P(string prefab, float x, float z, float h = 0f, float yaw = 0f) => b.Part(prefab, X(x), Z(z), h, yaw);
    Piece E(string name, float x, float z, float h = 0f) => b.Empty(name, X(x), Z(z), h);
    Quaternion Turned(float yaw) => b.Root.transform.rotation * Quaternion.Euler(0f, yaw, 0f);

    // A mesh in design coordinates, placed at the maze's centre
    Piece AddMesh(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(OffX, OffZ);
        return piece;
    }

    // ---- directions: 0 south (-z), 1 north (+z), 2 west (-x), 3 east (+x) ----
    static readonly int[] DX = { 0, 0, -1, 1 }, DZ = { -1, 1, 0, 0 };
    static readonly float[] DirYaw = { 180f, 0f, -90f, 90f };
    static int Opposite(int d) => d ^ 1;

    sealed class Cairn { public int i, j, open, back, index; }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(X(0f), Z(-15f));
        b.ClearGround(34f, 46f);
        // no jetpack thrust over the maze, the gantry, the tower or the hut (Winter Walk's JetpackDriver)
        IsleKit.CalmAir(b, "Whiteout Maze", 0f, 0f, new Vector3(64f, 150f, 76f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ================= the maze =================
        FindMaze(out bool[,] east, out bool[,] north, out int seed, out bool tree);
        List<Cairn> cairns = PickCairns(east, north);
        var h = new List<Vector2>[N + 1];
        var v = new List<Vector2>[N + 1];
        for (int k = 0; k <= N; k++) { h[k] = Solids(true, k, east, north); v[k] = Solids(false, k, east, north); }
        BuildWalls(snowy, h, v);
        List<int> posts = PostLattice(h, v);
        LogMaze(east, north, seed, tree, cairns, posts, out float meanL);

        // ================= groups by lobby size =================
        Piece rigA = IsleKit.Lobby(b, "Rig A", true, true, false);
        Piece rigB = IsleKit.Lobby(b, "Rig B", false, false, true);
        Piece lobby2 = IsleKit.Lobby(b, "Lobby 2", true, false, false);
        Piece lobby3 = IsleKit.Lobby(b, "Lobby 3", false, true, false);

        // ================= cairns: rocks, masts, face-up pictures, alcove anchors, pads =================
        var cairnMesh = new MeshBuilder();
        var padsA = new Component[Cairns + 1];
        var padsB = new Component[Cairns + 1];
        foreach (Cairn c in cairns)
        {
            float cx = Mid(c.i), cz = Mid(c.j);
            float bx = DX[c.back], bz = DZ[c.back];
            float yawBack = DirYaw[c.back];
            // three stacked stones against the back wall and a sprig of holly
            Vector3 stack = new Vector3(cx + bx * 0.78f, 0f, cz + bz * 0.78f);
            Kit.AddRock(cairnMesh, stack, new Vector3(0.5f, 0.3f, 0.5f), 500 + c.index * 3);
            Kit.AddRock(cairnMesh, stack + Vector3.up * 0.2f, new Vector3(0.4f, 0.25f, 0.4f), 501 + c.index * 3);
            Kit.AddRock(cairnMesh, stack + Vector3.up * 0.37f, new Vector3(0.28f, 0.22f, 0.28f), 502 + c.index * 3);
            Kit.AddBall(cairnMesh, stack + new Vector3(0.05f, 0.6f, 0f), 0.06f, IsleKit.Green);
            Kit.AddBall(cairnMesh, stack + new Vector3(-0.05f, 0.62f, 0.03f), 0.05f, IsleKit.Green);
            Kit.AddBall(cairnMesh, stack + new Vector3(0f, 0.64f, -0.04f), 0.035f, IsleKit.Red);
            Kit.AddBall(cairnMesh, stack + new Vector3(0.04f, 0.63f, -0.06f), 0.035f, IsleKit.Red);
            // the mast on the back wall's centre and the tray the picture lies in: its rim stands RimUp above the face,
            // so no eye lower than PicH + RimUp sees the picture
            float mx = cx + bx * CellSize / 2f, mz = cz + bz * CellSize / 2f;
            Blk(cairnMesh, mx - 0.075f, mx + 0.075f, WallBody - 0.1f, 2.8f, mz - 0.075f, mz + 0.075f, IsleKit.Wood);
            Blk(cairnMesh, mx - 0.28f, mx + 0.28f, 2.77f, 2.8f, mz - 0.28f, mz + 0.28f, IsleKit.DarkWood);
            float rimTop = PicH + RimUp;
            Blk(cairnMesh, mx - 0.28f, mx + 0.28f, 2.8f, rimTop, mz - 0.28f, mz - 0.25f, IsleKit.DarkWood);
            Blk(cairnMesh, mx - 0.28f, mx + 0.28f, 2.8f, rimTop, mz + 0.25f, mz + 0.28f, IsleKit.DarkWood);
            Blk(cairnMesh, mx - 0.28f, mx - 0.25f, 2.8f, rimTop, mz - 0.25f, mz + 0.25f, IsleKit.DarkWood);
            Blk(cairnMesh, mx + 0.25f, mx + 0.28f, 2.8f, rimTop, mz - 0.25f, mz + 0.25f, IsleKit.DarkWood);
            // the picture, face up; its top towards the far side from the gantry, so it reads upright from the deck
            float yawPic = c.j <= 2 ? 0f : 180f;
            Piece pic = P("Display", mx, mz, PicH, yawPic).Named("Cairn Picture " + c.index)
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", false);
            pic.Transform.rotation = Turned(yawPic) * Quaternion.Euler(-90f, 0f, 0f);
            // the alcove's anchor at a pad's centre height, turned so +z runs from the opening to the back wall
            float ax = cx + bx * 0.3f, az = cz + bz * 0.3f;
            E("Alcove " + c.index, ax, az, SensorH / 2f).Transform.rotation = Turned(yawBack);
            // pad k starts in alcove k; WhiteoutDeal moves the pads on every start
            Piece a = IsleKit.Sensor(b, "Pad " + c.index, X(ax), Z(az), new Vector3(1.9f, SensorH, 1.5f))
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1).Under(rigA);
            a.Transform.rotation = Turned(yawBack);
            padsA[c.index] = a.Component("PuzzlePresenceSensor");
            Piece pair = IsleKit.Sensor(b, "Pad " + c.index + " Pair", X(ax), Z(az), new Vector3(1.9f, SensorH, 1.5f))
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 2).Under(rigB);
            pair.Transform.rotation = Turned(yawBack);
            padsB[c.index] = pair.Component("PuzzlePresenceSensor");
        }
        AddMesh("Cairns", cairnMesh, snowy);

        // ================= ways in and out =================
        float south = Line(0);
        IsleKit.BlindGate(b, X(Mid(3)), Z(south), 0f);      // "Blind Turnstile": in only, blindfolded
        P("Gate No items", Mid(1), south, 0f, 180f).Named("Exit West")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false);
        P("Gate No items", Mid(5), south, 0f, 180f).Named("Exit East")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false);
        P("Door", Line(0), Mid(3), 0f, 90f).Named("Maze Door W").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);
        P("Door", Line(N), Mid(3), 0f, 90f).Named("Maze Door E").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);
        P("Door", Mid(3), Line(N), 0f, 0f).Named("Maze Door N").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);
        BuildGates(snowy);

        // blindfolds (the second only in a 4-player lobby) and the empty hooks inside the exits
        var posts2 = new MeshBuilder();
        var hookPosts = new MeshBuilder();
        HookPost(hookPosts, 2.5f, -12.5f);
        Hang("Blindfold", "Blindfold Hook", 2.5f, -12.5f + 0.09f, 0f, 0f, 1, 0);
        HookPost(posts2, 3.4f, -12.5f);
        Piece[] blind2 = Hang("Blindfold 2", "Blindfold Hook 2", 3.4f, -12.5f + 0.09f, 0f, 0f, 1, 0);
        foreach (Piece p in blind2) p.Under(rigB);
        HookPost(hookPosts, -6.15f, -8.15f);
        Hang(null, "Exit Hook W", -6.15f, -8.15f + 0.09f, 0f, 0f, 1, -1);
        HookPost(hookPosts, 6.15f, -8.15f);
        Hang(null, "Exit Hook E", 6.15f, -8.15f + 0.09f, 0f, 0f, 1, -1);
        AddMesh("Hook Posts", hookPosts, snowy);
        AddMesh("Blindfold Post 2", posts2, snowy).Under(rigB);

        // ================= the gantry, the stair tower, the signal hut =================
        BuildGantry(snowy, posts);
        BuildTower(snowy);
        BuildHut(snowy, bare);

        // ================= the board: two rows of four, a gold lamp over each picture =================
        var lamps = new Component[Cairns + 1];
        for (int k = 1; k <= Cairns; k++)
        {
            int col = (k - 1) % 4, row = (k - 1) / 4;
            float x = 0.75f - 0.5f * col;      // board-left is east (the reader faces south)
            Piece lamp = P("Light 8", x, 20.42f, 2.25f - row, 0f).Named("Board Lamp " + k)
                .Set("PuzzleLight", "color", LampGold).Set("PuzzleLight", "range", 0.8f).Set("PuzzleLight", "intensity", 0f);
            lamps[k] = lamp.Component("PuzzleLight");
            P("Display", x, 20.52f, 1.85f - row, 0f).Named("Board Picture " + k)
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", false);
        }
        Piece shutter = P("Door", 0f, 22.2f, 0f, 0f).Named("Board Shutter")
            .Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", false);      // ControlWithFunctionsOnly: starts closed

        // the shutter is open only while someone stands on the deck (in every lobby)
        Piece watch = IsleKit.Sensor(b, "Gantry Watch", X((DeckX0 + DeckX1) / 2f), Z((DeckZ0 + DeckZ1) / 2f),
                new Vector3(DeckX1 - DeckX0, 1.3f, DeckZ1 - DeckZ0), DeckH)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        Component door = shutter.Component("PuzzleDoor");
        watch.On("PuzzlePresenceSensor", "onPressed", door, "Open");
        watch.On("PuzzlePresenceSensor", "onReleased", door, "Close");

        // ================= bars, clocks, walkies, binoculars, dead zone =================
        Piece barGantry = P("Progress Bar", 13.75f, 1.3f, DeckH + 2.9f, -90f).Named("Bar Gantry").Scaled(0.75f, 1f, 1f)
            .Set("PuzzleProgressBar", "total", 6);
        Piece barHut = P("Progress Bar", 0f, 22.4f, 2.55f, 0f).Named("Bar Hut").Set("PuzzleProgressBar", "total", 6);
        // 3: six blind walks at 0.8 m/s plus 20 s of relay and search each, 30 % spare; 4: five rounds, a quarter more to
        // keep two walkers together; 2: B's 90 m loop to the hut at a sprint, gates, a guided sighted walk and the relay
        float t3 = Round10(6f * (meanL / 0.8f + 20f) * 1.3f);
        float t4 = Round10(5f * (meanL / 0.8f + 20f) * 1.3f * 1.25f);
        float t2 = Round10(6f * (90f / 3.5f + 10f + meanL / 2f + 15f) * 1.3f);
        Piece timer2 = P("Timer", 13.75f, 1.3f, DeckH + 3.6f, -90f).Named("Timer 2").Set("PuzzleTimer", "duration", t2).Under(lobby2);
        Piece timer3 = P("Timer", 13.75f, 1.3f, DeckH + 3.6f, -90f).Named("Timer 3").Set("PuzzleTimer", "duration", t3).Under(lobby3);
        Piece timer4 = P("Timer", 13.75f, 1.3f, DeckH + 3.6f, -90f).Named("Timer 4").Set("PuzzleTimer", "duration", t4).Under(rigB);
        Debug.Log("[BWM] WhiteoutMaze: timers 2/3/4 players " + Sdk.F(t2) + " / " + Sdk.F(t3) + " / " + Sdk.F(t4) + " s (L " + Sdk.F(meanL) + " m)");

        Hang("Walkie Hut", "Walkie Hut Hook", 2.38f, 24.0f, -90f, 0f, 16, 4);                // inside the hut's east wall
        Hang("Walkie Tower", "Walkie Tower Hook", 15.3f, -0.2f, 180f, 0f, 16, 4);            // the tower's south face
        Hang("Deck Binoculars", "Deck Binoculars Hook", 14.45f, 0f, 0f, DeckH, 32, 5);      // inside the tower's top, on its south wall

        Piece dead = P("Radio Dead Zone", 0f, 0f, 1.6f, 0f).Named("Maze Dead Zone");      // theme only: it ignores held radios
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(18.8f, 3.2f, 18.8f);
        zone.center = Vector3.zero;

        // ================= the rules: one Press In Order per lobby size, sharing the board's lamps =================
        Piece orderA = E("Press In Order A", 0f, 0f, 0.5f).Under(rigA);
        Component pioA = b.AddScript(orderA, "PuzzlePressInOrder");
        Piece orderB = E("Press In Order B", 0f, 0f, 0.6f).Under(rigB);
        Component pioB = b.AddScript(orderB, "PuzzlePressInOrder");
        for (int k = 1; k <= Cairns; k++)
        {
            Sdk.Add(pioA, "lights", lamps[k]); Sdk.Add(pioA, "buttons", padsA[k]);
            Sdk.Add(pioB, "lights", lamps[k]); Sdk.Add(pioB, "buttons", padsB[k]);
        }
        Component barG = barGantry.Component("PuzzleProgressBar"), barH = barHut.Component("PuzzleProgressBar");
        Sdk.Add(pioA, "progressBars", barG, barH);
        Sdk.Add(pioB, "progressBars", barG, barH);
        Sdk.Set(pioA, "rounds", 6); Sdk.Set(pioA, "holdAtEnd", false);
        Sdk.Set(pioB, "rounds", 5); Sdk.Set(pioB, "holdAtEnd", false);

        // the deal (host only), at the root so it is in every lobby
        Component deal = b.AddCustomScript(E("Whiteout Deal", 0f, 0f, 0.5f), "WhiteoutDeal");
        Sdk.Set(deal, "imageSet", 0);       // PuzzleImageSet.A
        Sdk.Set(deal, "beginDelay", 0.6f);

        // ================= events =================
        Component t2c = timer2.Component("PuzzleTimer"), t3c = timer3.Component("PuzzleTimer"), t4c = timer4.Component("PuzzleTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", deal, "Deal");      // which Begins both rigs 0.6 s later
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pioA, "End");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pioB, "End");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", deal, "Clear");
        foreach (Component t in new[] { t2c, t3c, t4c })
        {
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", t, "StartTimer");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", t, "ResetTimer");
            b.RootPiece.On("PuzzleSDK", "onWin", t, "PauseTimer");
            Sdk.Wire(t, "onFinished", b.Puzzle, "StopPuzzle");
        }
        orderA.On("PuzzlePressInOrder", "onFail", b.Puzzle, "StopPuzzle");
        orderB.On("PuzzlePressInOrder", "onFail", b.Puzzle, "StopPuzzle");
        barGantry.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");
        // (nothing on start or stop touches the shutter: it follows the deck alone)

        // ================= start corner and lights =================
        IsleKit.StartCorner(b, X(-5f), Z(-13.5f), 0f);
        Kit.LightRing(b, "Site Lights", X(2f), Z(5.5f), 32f, 43f, 2.6f, true, 181);
        foreach (float fz in new[] { DeckZ0, DeckZ1 })
        {
            var rail = new List<Vector3>();
            for (int s = 0; s <= 8; s++) rail.Add(new Vector3(X(Mathf.Lerp(DeckX0, DeckX1, s / 8f)), DeckH + FenceH + 0.2f, Z(fz)));
            Kit.LightString(b, fz < 1f ? "Deck Lights South" : "Deck Lights North", rail, 0.1f, 0.4f, true, false, fz < 1f ? 191 : 192);
        }
        Kit.LightString(b, "Deck Lights West", new List<Vector3> { new Vector3(X(DeckX0), DeckH + FenceH + 0.2f, Z(DeckZ0)), new Vector3(X(DeckX0), DeckH + FenceH + 0.2f, Z(DeckZ1)) },
            0.1f, 0.4f, true, false, 193);
        Kit.LightString(b, "Hut Eaves", new List<Vector3> { new Vector3(X(-2.9f), 2.95f, Z(26.15f)), new Vector3(X(0f), 2.95f, Z(26.15f)), new Vector3(X(2.9f), 2.95f, Z(26.15f)) },
            0.15f, 0.4f, true, false, 194);
        Kit.LightString(b, "Arch Lights", new List<Vector3> { new Vector3(X(-1.45f), 2.75f, Z(-9.75f)), new Vector3(X(1.35f), 2.75f, Z(-9.75f)) }, 0.2f, 0.3f, true, false, 195);
    }

    static float Round10(float s) => Mathf.Max(60f, Mathf.Round(s / 10f) * 10f);

    // ================= maze generation =================

    // Seeds upward from 1812, depth-first carve first, then a growing-tree carve, until the maze has 8 eligible dead
    // ends with at least one in each quadrant
    void FindMaze(out bool[,] east, out bool[,] north, out int seed, out bool tree)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            tree = pass == 1;
            for (seed = 1812; seed < 1812 + 2000; seed++)
            {
                Carve(seed, tree, out east, out north);
                List<Vector2Int> eligible = Eligible(east, north);
                if (eligible.Count >= Cairns && QuadrantsCovered(eligible)) return;
            }
        }
        throw new System.Exception("WhiteoutMaze: no seed from 1812 to 3811 gives " + Cairns + " eligible dead ends (depth-first or growing tree)");
    }

    static void Carve(int seed, bool tree, out bool[,] east, out bool[,] north)
    {
        var random = new System.Random(seed);
        east = new bool[N, N];
        north = new bool[N, N];
        var seen = new bool[N, N];
        var active = new List<Vector2Int> { new Vector2Int(N / 2, 0) };
        seen[N / 2, 0] = true;
        var options = new List<Vector2Int>();
        while (active.Count > 0)
        {
            int index = active.Count - 1;
            if (tree) index = random.NextDouble() < 0.5 ? active.Count - 1 : random.Next(active.Count);
            Vector2Int c = active[index];
            options.Clear();
            if (c.x > 0 && !seen[c.x - 1, c.y]) options.Add(new Vector2Int(c.x - 1, c.y));
            if (c.x < N - 1 && !seen[c.x + 1, c.y]) options.Add(new Vector2Int(c.x + 1, c.y));
            if (c.y > 0 && !seen[c.x, c.y - 1]) options.Add(new Vector2Int(c.x, c.y - 1));
            if (c.y < N - 1 && !seen[c.x, c.y + 1]) options.Add(new Vector2Int(c.x, c.y + 1));
            if (options.Count == 0) { active.RemoveAt(index); continue; }
            Vector2Int n = options[random.Next(options.Count)];
            if (n.x != c.x) east[Mathf.Min(c.x, n.x), c.y] = true; else north[c.x, Mathf.Min(c.y, n.y)] = true;
            seen[n.x, n.y] = true;
            active.Add(n);
        }
    }

    // The open sides of a cell inside the maze (outer openings are not counted)
    static List<int> Openings(bool[,] east, bool[,] north, int i, int j)
    {
        var open = new List<int>();
        if (j > 0 && north[i, j - 1]) open.Add(0);
        if (j < N - 1 && north[i, j]) open.Add(1);
        if (i > 0 && east[i - 1, j]) open.Add(2);
        if (i < N - 1 && east[i, j]) open.Add(3);
        return open;
    }

    // Dead ends that may hold a cairn: not the entrance, exit or door cells, and not rows 3-4 beside the gantry
    static List<Vector2Int> Eligible(bool[,] east, bool[,] north)
    {
        var list = new List<Vector2Int>();
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                if (j == 3 || j == 4) continue;
                if (j == 0 && (i == 1 || i == 3 || i == 5)) continue;
                if (j == 6 && i == 3) continue;
                if (Openings(east, north, i, j).Count == 1) list.Add(new Vector2Int(i, j));
            }
        return list;
    }

    static bool QuadrantsCovered(List<Vector2Int> cells)
    {
        bool sw = false, se = false, nw = false, ne = false;
        foreach (Vector2Int c in cells)
        {
            if (c.x == N / 2) continue;
            bool west = c.x < N / 2, low = c.y <= 2;
            if (west && low) sw = true; else if (!west && low) se = true; else if (west) nw = true; else ne = true;
        }
        return sw && se && nw && ne;
    }

    // Path lengths in cells from one cell to every other (the maze is a tree)
    static int[,] Distances(bool[,] east, bool[,] north, Vector2Int from)
    {
        var d = new int[N, N];
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) d[i, j] = -1;
        var queue = new Queue<Vector2Int>();
        d[from.x, from.y] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (int dir in Openings(east, north, c.x, c.y))
            {
                var n = new Vector2Int(c.x + DX[dir], c.y + DZ[dir]);
                if (d[n.x, n.y] >= 0) continue;
                d[n.x, n.y] = d[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }
        return d;
    }

    // Farthest-point greedy from the eligible dead end farthest from the entrance
    List<Cairn> PickCairns(bool[,] east, bool[,] north)
    {
        List<Vector2Int> eligible = Eligible(east, north);
        int[,] fromEntrance = Distances(east, north, new Vector2Int(N / 2, 0));
        var chosen = new List<Vector2Int>();
        Vector2Int first = eligible[0];
        foreach (Vector2Int c in eligible) if (fromEntrance[c.x, c.y] > fromEntrance[first.x, first.y]) first = c;
        chosen.Add(first);
        var dist = new Dictionary<Vector2Int, int[,]>();
        foreach (Vector2Int c in eligible) dist[c] = Distances(east, north, c);
        while (chosen.Count < Cairns)
        {
            Vector2Int best = default;
            int bestScore = -1;
            foreach (Vector2Int c in eligible)
            {
                if (chosen.Contains(c)) continue;
                int score = int.MaxValue;
                foreach (Vector2Int k in chosen) score = Mathf.Min(score, dist[c][k.x, k.y]);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            chosen.Add(best);
        }
        var cairns = new List<Cairn>();
        for (int n = 0; n < chosen.Count; n++)
        {
            int open = Openings(east, north, chosen[n].x, chosen[n].y)[0];
            cairns.Add(new Cairn { i = chosen[n].x, j = chosen[n].y, open = open, back = Opposite(open), index = n + 1 });
        }
        return cairns;
    }

    void LogMaze(bool[,] east, bool[,] north, int seed, bool tree, List<Cairn> cairns, List<int> posts, out float meanL)
    {
        string[] dirName = { "S", "N", "W", "E" };
        string cells = "";
        foreach (Cairn c in cairns) cells += (cells.Length > 0 ? ", " : "") + c.index + ":(" + c.i + "," + c.j + ") open " + dirName[c.open];
        int sum = 0, count = 0, max = 0, sumE = 0, maxE = 0;
        int[,] fromEntrance = Distances(east, north, new Vector2Int(N / 2, 0));
        for (int a = 0; a < cairns.Count; a++)
        {
            int[,] d = Distances(east, north, new Vector2Int(cairns[a].i, cairns[a].j));
            for (int c = a + 1; c < cairns.Count; c++) { int x = d[cairns[c].i, cairns[c].j]; sum += x; count++; max = Mathf.Max(max, x); }
            int e = fromEntrance[cairns[a].i, cairns[a].j];
            sumE += e; maxE = Mathf.Max(maxE, e);
        }
        float mean = count > 0 ? (float)sum / count : 0f;
        meanL = mean * CellSize;
        string postText = "";
        foreach (int k in posts) postText += (postText.Length > 0 ? ", " : "") + Sdk.F(Line(k));
        Debug.Log("[BWM] WhiteoutMaze: seed " + seed + " (" + (tree ? "growing tree" : "depth-first") + "); cairns " + cells);
        Debug.Log("[BWM] WhiteoutMaze: gantry posts at x " + postText + "; cairn to cairn mean " + Sdk.F(mean) + " cells (L " + Sdk.F(meanL)
            + " m), max " + max + "; entrance to cairn mean " + Sdk.F(cairns.Count > 0 ? (float)sumE / cairns.Count : 0f) + ", max " + maxE);
    }

    // ================= walls =================

    // The solid stretches of one wall line (horizontal: z = Line(k), along x; vertical: x = Line(k), along z), with the
    // turnstiles' and doors' gaps cut out, merged into runs
    static List<Vector2> Solids(bool horizontal, int k, bool[,] east, bool[,] north)
    {
        var parts = new List<Vector2>();
        for (int i = 0; i < N; i++)
        {
            bool present = k == 0 || k == N || (horizontal ? !north[i, k - 1] : !east[k - 1, i]);
            if (!present) continue;
            if (Gap(horizontal, k, i, out float a, out float c))
            {
                if (a > Line(i) + Eps) parts.Add(new Vector2(Line(i), a));
                if (c < Line(i + 1) - Eps) parts.Add(new Vector2(c, Line(i + 1)));
            }
            else parts.Add(new Vector2(Line(i), Line(i + 1)));
        }
        var merged = new List<Vector2>();
        foreach (Vector2 p in parts)
        {
            if (merged.Count > 0 && Mathf.Abs(merged[merged.Count - 1].y - p.x) < Eps) merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, p.y);
            else merged.Add(p);
        }
        return merged;
    }

    // The clear gap in an outer cell edge: the blind turnstile (yaw 0), the exits (yaw 180, so mirrored) and the doors
    static bool Gap(bool horizontal, int k, int i, out float a, out float c)
    {
        a = c = 0f;
        if (horizontal && k == 0 && i == 3) { a = Mid(3) - GateL; c = Mid(3) + GateR; return true; }
        if (horizontal && k == 0 && (i == 1 || i == 5)) { a = Mid(i) - GateR; c = Mid(i) + GateL; return true; }
        if ((horizontal && k == N && i == 3) || (!horizontal && (k == 0 || k == N) && i == 3)) { a = Mid(3) - DoorHalf; c = Mid(3) + DoorHalf; return true; }
        return false;
    }

    static bool Through(List<Vector2> line, float u)
    {
        foreach (Vector2 s in line) if (s.x < u - Eps && s.y > u + Eps) return true;
        return false;
    }

    static bool Touches(List<Vector2> line, float u)
    {
        foreach (Vector2 s in line) if (s.x <= u + Eps && s.y >= u - Eps) return true;
        return false;
    }

    static int Lattice(float u)
    {
        int m = Mathf.RoundToInt((u - X0) / CellSize);
        return m >= 0 && m <= N && Mathf.Abs(Line(m) - u) < Eps ? m : -1;
    }

    // Snow-block walls: a body 2.0 m high (sunk 0.3) under a peaked 0.35 m ridge, so nobody stands on a wall top. Where
    // walls meet, a wall that another passes straight through stops at its face; at a corner the horizontal wall runs on
    // and the vertical one stops; a pyramid caps every junction no wall passes straight through. Turnstile and door gaps
    // end in gables.
    void BuildWalls(Material snowy, List<Vector2>[] h, List<Vector2>[] v)
    {
        var mb = new MeshBuilder();
        var caps = new HashSet<Vector2Int>();
        for (int pass = 0; pass < 2; pass++)
        {
            bool horizontal = pass == 0;
            List<Vector2>[] lines = horizontal ? h : v, across = horizontal ? v : h;
            for (int k = 0; k <= N; k++)
            {
                float c = Line(k);
                foreach (Vector2 run in lines[k])
                {
                    float bodyFrom = run.x, bodyTo = run.y;
                    for (int end = 0; end < 2; end++)
                    {
                        float u = end == 0 ? run.x : run.y;
                        int m = Lattice(u);
                        if (m < 0) continue;      // a gap's edge: ends exactly there
                        bool through = Through(across[m], c), touch = Touches(across[m], c);
                        float ext = through ? -WallT / 2f : touch ? (horizontal ? WallT / 2f : -WallT / 2f) : WallT / 2f;
                        if (end == 0) bodyFrom = u - ext; else bodyTo = u + ext;
                        if (!through) caps.Add(horizontal ? new Vector2Int(m, k) : new Vector2Int(k, m));
                    }
                    if (horizontal)
                    {
                        Blk(mb, bodyFrom, bodyTo, -Sink, WallBody, c - WallT / 2f, c + WallT / 2f, SnowBlock);
                        Prism(mb, new Vector3(run.x, WallBody, c), new Vector3(run.y, WallBody, c), WallT, Ridge, SnowBlock);
                    }
                    else
                    {
                        Blk(mb, c - WallT / 2f, c + WallT / 2f, -Sink, WallBody, bodyFrom, bodyTo, SnowBlock);
                        Prism(mb, new Vector3(c, WallBody, run.x), new Vector3(c, WallBody, run.y), WallT, Ridge, SnowBlock);
                    }
                }
            }
        }
        foreach (Vector2Int p in caps) Pyramid(mb, new Vector3(Line(p.x), WallBody, Line(p.y)), WallT / 2f, Ridge, SnowBlock);

        // timber frames and lintels (to 2.4 m) round the three doors
        float mid = Mid(3);
        foreach (float wx in new[] { Line(0), Line(N) })
        {
            Blk(mb, wx - 0.25f, wx + 0.25f, -Sink, 2.4f, mid - DoorHalf - 0.13f, mid - DoorHalf + 0.01f, IsleKit.DarkWood);
            Blk(mb, wx - 0.25f, wx + 0.25f, -Sink, 2.4f, mid + DoorHalf - 0.01f, mid + DoorHalf + 0.13f, IsleKit.DarkWood);
            Blk(mb, wx - 0.25f, wx + 0.25f, 2.02f, 2.4f, mid - DoorHalf + 0.01f, mid + DoorHalf - 0.01f, IsleKit.DarkWood);
        }
        float nz = Line(N);
        Blk(mb, mid - DoorHalf - 0.13f, mid - DoorHalf + 0.01f, -Sink, 2.4f, nz - 0.25f, nz + 0.25f, IsleKit.DarkWood);
        Blk(mb, mid + DoorHalf - 0.01f, mid + DoorHalf + 0.13f, -Sink, 2.4f, nz - 0.25f, nz + 0.25f, IsleKit.DarkWood);
        Blk(mb, mid - DoorHalf + 0.01f, mid + DoorHalf - 0.01f, 2.02f, 2.4f, nz - 0.25f, nz + 0.25f, IsleKit.DarkWood);
        AddMesh("Maze Walls", mb, snowy);
    }

    // Gantry posts on the z = 1.3 wall line (lattice row 4), only where a maze wall meets: the outer walls, and the
    // qualifying points nearest x = -3.9, 1.3 and 6.5. Returns lattice column indices.
    static List<int> PostLattice(List<Vector2>[] h, List<Vector2>[] v)
    {
        const int Row = 4;
        bool Qualifies(int k) => Touches(h[Row], Line(k)) || Touches(v[k], Line(Row));
        var posts = new List<int> { 0 };
        foreach (int target in new[] { 2, 4, 6 })
        {
            for (int d = 0; d <= N; d++)
            {
                int found = -1;
                foreach (int k in new[] { target - d, target + d })
                    if (k >= 1 && k <= N - 1 && !posts.Contains(k) && Qualifies(k)) { found = k; break; }
                if (found >= 0) { posts.Add(found); break; }
            }
        }
        posts.Add(N);
        posts.Sort();
        return posts;
    }

    // ================= turnstile dressing =================

    void BuildGates(Material snowy)
    {
        var mb = new MeshBuilder();
        float zo = Line(0) - 0.5f;
        // the entrance's red arch
        foreach (float x in new[] { Mid(3) - GateL - 0.34f, Mid(3) + GateR + 0.34f })
            Blk(mb, x - 0.11f, x + 0.11f, -Sink, 2.8f, zo - 0.11f, zo + 0.11f, IsleKit.Red);
        Blk(mb, Mid(3) - GateL - 0.45f, Mid(3) + GateR + 0.45f, 2.8f, 3.04f, zo - 0.15f, zo + 0.15f, IsleKit.Red);
        // green posts and lintels at the two exits (yaw 180: the gate runs -1.01 .. +1.11)
        foreach (int i in new[] { 1, 5 })
        {
            float g = Mid(i);
            foreach (float x in new[] { g - GateR - 0.34f, g + GateL + 0.34f })
                Blk(mb, x - 0.09f, x + 0.09f, -Sink, 2.4f, zo - 0.09f, zo + 0.09f, IsleKit.Green);
            Blk(mb, g - GateR - 0.43f, g + GateL + 0.43f, 2.4f, 2.58f, zo - 0.1f, zo + 0.1f, IsleKit.Green);
        }
        AddMesh("Maze Gates", mb, snowy);
    }

    // ================= hooks =================

    // A free-standing 0.18 m post, 1.9 m high, centred at (x, z)
    static void HookPost(MeshBuilder mb, float x, float z) => Blk(mb, x - 0.09f, x + 0.09f, -Sink, 1.9f, z - 0.09f, z + 0.09f, IsleKit.Wood);

    // A hook on a face at (fx, fz) looking 'yaw', 'baseH' above the ground, accepting 'mask' (PuzzleHookAccepts); with
    // itemType >= 0 a Vanilla Item of that type hangs on it (savesOnReload off). Returns the hook and the item, if any.
    Piece[] Hang(string itemName, string hookName, float fx, float fz, float yaw, float baseH, int mask, int itemType)
    {
        Vector2 f = IsleKit.Turn(0f, 1f, yaw);
        Piece hook = P("Hook", fx + f.x * 0.03f, fz + f.y * 0.03f, baseH + HookUp, yaw).Named(hookName);
        Sdk.SetFlags(hook.Component("PuzzleHook"), "acceptedVanilla", mask);
        if (itemType < 0) return new[] { hook };
        Piece item = P("Vanilla Item", fx + f.x * 0.31f, fz + f.y * 0.31f, baseH + ItemUp, yaw).Named(itemName)
            .Set("PuzzleVanillaItem", "itemType", itemType).Set("PuzzleVanillaItem", "savesOnReload", false);
        hook.Set("PuzzleHook", "defaultItem", item.Component("PuzzleVanillaItem"));
        return new[] { hook, item };
    }

    // ================= the gantry =================

    // A deck 1.4 m wide along the z = 1.3 wall line, top 9.5 m, from x -9.4 to the tower's west face (13.8), on posts at
    // wall junctions (tie beams only high up, nothing in the corridors), with a 2.6 m baluster screen on both long sides
    // and the west end (5 cm balusters, 12 cm gaps) under a peaked capping rail
    void BuildGantry(Material snowy, List<int> posts)
    {
        var mb = new MeshBuilder();
        float zc = (DeckZ0 + DeckZ1) / 2f;
        for (int n = 0; n < posts.Count; n++)
        {
            float x = Line(posts[n]);
            Blk(mb, x - 0.15f, x + 0.15f, -Sink, DeckH - 0.25f, zc - 0.15f, zc + 0.15f, IsleKit.Wood);
            if (n + 1 < posts.Count)
            {
                float x2 = Line(posts[n + 1]);
                foreach (float y in new[] { 4.5f, 7.5f })
                    Blk(mb, x + 0.15f, x2 - 0.15f, y, y + 0.2f, zc - 0.08f, zc + 0.08f, IsleKit.Wood);
            }
        }
        Blk(mb, DeckX0, DeckX1, DeckH - 0.25f, DeckH, DeckZ0, DeckZ1, IsleKit.DarkWood);
        Blk(mb, DeckX0 - 0.05f, DeckX1, DeckH - 0.45f, DeckH - 0.25f, zc - 0.2f, zc + 0.2f, IsleKit.Wood);      // the stringer under it

        const float Bal = 0.05f, Pitch = 0.17f;
        float top = DeckH + FenceH;
        // long sides
        foreach (float z0 in new[] { DeckZ0, DeckZ1 - Bal })
        {
            for (float x = DeckX0; x + Bal <= DeckX1 + Eps; x += Pitch)
                Blk(mb, x, x + Bal, DeckH, top, z0, z0 + Bal, IsleKit.Wood);
            Blk(mb, DeckX0, DeckX1, DeckH, DeckH + 0.08f, z0 - 0.015f, z0 + Bal + 0.015f, IsleKit.DarkWood);
            Blk(mb, DeckX0, DeckX1, top, top + 0.06f, z0 - 0.015f, z0 + Bal + 0.015f, IsleKit.DarkWood);
            Prism(mb, new Vector3(DeckX0, top + 0.06f, z0 + Bal / 2f), new Vector3(DeckX1, top + 0.06f, z0 + Bal / 2f), Bal + 0.03f, 0.08f, IsleKit.DarkWood);
        }
        // the west end
        for (float z = DeckZ0 + Bal + 0.12f; z + Bal <= DeckZ1 - Bal - 0.1f; z += Pitch)
            Blk(mb, DeckX0, DeckX0 + Bal, DeckH, top, z, z + Bal, IsleKit.Wood);
        Blk(mb, DeckX0 - 0.015f, DeckX0 + Bal + 0.015f, top, top + 0.06f, DeckZ0, DeckZ1, IsleKit.DarkWood);
        Prism(mb, new Vector3(DeckX0 + Bal / 2f, top + 0.06f, DeckZ0), new Vector3(DeckX0 + Bal / 2f, top + 0.06f, DeckZ1), Bal + 0.03f, 0.08f, IsleKit.DarkWood);
        AddMesh("Gantry", mb, snowy);
    }

    // ================= the stair tower =================

    // 3 x 3 m, boarded solid to its roof at 13.7 m: a door at its foot in the east wall (away from the maze) and the
    // deck doorway in its west wall at the top. Inside, a winding stair climbs round a 0.8 m well in 0.9 m lanes: 52
    // rises of 0.183 m, a step on each side run of three and one on each corner, a revolution every 2.9 m (headroom
    // 2.6 m), arriving on the top floor over the west lane at 9.5 m, beside the deck doorway.
    void BuildTower(Material snowy)
    {
        var mb = new MeshBuilder();
        const float W0 = 13.8f, W1 = 16.8f, S0 = -0.2f, S1 = 2.8f, Th = 0.2f, Top = DeckH + 4.2f;
        float ix0 = W0 + Th, ix1 = W1 - Th, iz0 = S0 + Th, iz1 = S1 - Th;      // inside: x 14.0..16.6, z 0..2.6
        const float Lane = 0.9f;
        float wellX0 = ix0 + Lane, wellX1 = ix1 - Lane, wellZ0 = iz0 + Lane, wellZ1 = iz1 - Lane;
        // walls: west, round the deck doorway (z 0.85..1.75, 9.5..11.7)
        Blk(mb, W0, ix0, -Sink, Top, S0, 0.85f, IsleKit.Wood);
        Blk(mb, W0, ix0, -Sink, Top, 1.75f, S1, IsleKit.Wood);
        Blk(mb, W0, ix0, -Sink, DeckH, 0.85f, 1.75f, IsleKit.Wood);
        Blk(mb, W0, ix0, DeckH + 2.2f, Top, 0.85f, 1.75f, IsleKit.Wood);
        // east, round the foot door (z 0.9..1.7, 2.0 high, under the first revolution's east run)
        Blk(mb, ix1, W1, -Sink, Top, S0, wellZ0, IsleKit.Wood);
        Blk(mb, ix1, W1, -Sink, Top, wellZ1, S1, IsleKit.Wood);
        Blk(mb, ix1, W1, 2.0f, Top, wellZ0, wellZ1, IsleKit.Wood);
        Blk(mb, ix0, ix1, -Sink, Top, S0, iz0, IsleKit.Wood);       // south
        Blk(mb, ix0, ix1, -Sink, Top, iz1, S1, IsleKit.Wood);       // north
        Blk(mb, W0 - 0.2f, W1 + 0.2f, Top, Top + 0.2f, S0 - 0.2f, S1 + 0.2f, IsleKit.Red);      // roof
        Blk(mb, ix0, ix1, -Sink, 0.02f, iz0, iz1, IsleKit.DarkWood);                          // ground floor
        // the winding stair: per revolution north run (west-going), NW corner, west run (south-going), SW corner, south
        // run (east-going), SE corner, east run (north-going), NE corner
        const int Rises = 52;
        float rise = DeckH / Rises, run = (wellX1 - wellX0) / 3f;
        for (int n = 1; n < Rises; n++)      // the last rise is the top floor itself
        {
            int pos = (n - 1) % 16, s = pos % 4;
            float x0, x1, z0, z1;
            switch (pos / 4)
            {
                case 0:  // north side, then NW corner
                    z0 = wellZ1; z1 = iz1;
                    if (s < 3) { x1 = wellX1 - s * run; x0 = x1 - run; } else { x0 = ix0; x1 = wellX0; }
                    break;
                case 1:  // west side, then SW corner
                    x0 = ix0; x1 = wellX0;
                    if (s < 3) { z1 = wellZ1 - s * run; z0 = z1 - run; } else { z0 = iz0; z1 = wellZ0; }
                    break;
                case 2:  // south side, then SE corner
                    z0 = iz0; z1 = wellZ0;
                    if (s < 3) { x0 = wellX0 + s * run; x1 = x0 + run; } else { x0 = wellX1; x1 = ix1; }
                    break;
                default: // east side, then NE corner
                    x0 = wellX1; x1 = ix1;
                    if (s < 3) { z0 = wellZ0 + s * run; z1 = z0 + run; } else { z0 = wellZ1; z1 = iz1; }
                    break;
            }
            float t = n * rise;
            Blk(mb, x0, x1, t < 0.6f ? -Sink : t - 0.3f, t, z0, z1, IsleKit.DarkWood);
        }
        // the top floor over the west lane (the last rise lands on it at the NW corner), a rail along its open edge
        Blk(mb, ix0, wellX0, DeckH - 0.2f, DeckH, iz0, iz1, IsleKit.DarkWood);
        Blk(mb, wellX0, wellX0 + 0.06f, DeckH, DeckH + 1.0f, iz0, wellZ1, IsleKit.Wood);
        AddMesh("Stair Tower", mb, snowy);
    }

    // ================= the signal hut and the board closet =================

    // The hut: 5.0 x 3.6 x 3.0 m, centre (0, 24), open to the north, its back wall (z 22.1..22.3) towards the maze with
    // the closet's mouth in it, exactly the Door's 1.24 x 2.02, trimmed on the hut side by a lintel (1.98..2.12) and jambs
    // that overlap the door by 4 cm. The closet: 2.4 x 1.9 x 2.8 m south of the back wall, roofed and sealed, the lamp
    // board on its south wall facing north, a clear pane (PuzzleGlass) in a partition 0.25 m behind the shutter.
    void BuildHut(Material snowy, Material bare)
    {
        var mb = new MeshBuilder();
        const float H = 3.0f, B0 = 22.1f, B1 = 22.3f, Front = 25.8f;
        Blk(mb, -2.5f, -DoorHalf, -Sink, H, B0, B1, IsleKit.Wood);                 // back wall, round the mouth
        Blk(mb, DoorHalf, 2.5f, -Sink, H, B0, B1, IsleKit.Wood);
        Blk(mb, -DoorHalf, DoorHalf, 2.02f, H, B0, B1, IsleKit.Wood);
        Blk(mb, -0.72f, 0.72f, 1.98f, 2.12f, B1, B1 + 0.08f, IsleKit.DarkWood);    // lintel and jambs on the hut side
        Blk(mb, -0.72f, -0.58f, -Sink, 2.12f, B1, B1 + 0.08f, IsleKit.DarkWood);
        Blk(mb, 0.58f, 0.72f, -Sink, 2.12f, B1, B1 + 0.08f, IsleKit.DarkWood);
        Blk(mb, -2.5f, -2.38f, -Sink, H, B1, Front, IsleKit.Wood);                // side walls
        Blk(mb, 2.38f, 2.5f, -Sink, H, B1, Front, IsleKit.Wood);
        Blk(mb, -2.9f, 2.9f, H, H + 0.15f, B0 - 0.3f, Front + 0.4f, IsleKit.DarkWood);      // roof, 0.4 m overhang
        // the closet: x -1.2..1.2, z 20.2..22.1, walls 0.1, roof at 2.8
        Blk(mb, -1.2f, -1.1f, -Sink, 2.8f, 20.3f, B0, IsleKit.DarkWood);      // (from the south wall's inner face: no coplanar corners)
        Blk(mb, 1.1f, 1.2f, -Sink, 2.8f, 20.3f, B0, IsleKit.DarkWood);
        Blk(mb, -1.2f, 1.2f, -Sink, 2.8f, 20.2f, 20.3f, IsleKit.DarkWood);
        Blk(mb, -1.25f, 1.25f, 2.8f, 2.92f, 20.15f, B0, IsleKit.DarkWood);
        // the partition the pane fills (the pane overlaps its edges by a centimetre)
        Blk(mb, -1.1f, -0.64f, -Sink, 2.8f, 21.85f, 21.95f, IsleKit.DarkWood);
        Blk(mb, 0.64f, 1.1f, -Sink, 2.8f, 21.85f, 21.95f, IsleKit.DarkWood);
        Blk(mb, -0.64f, 0.64f, 2.06f, 2.8f, 21.85f, 21.95f, IsleKit.DarkWood);
        AddMesh("Signal Hut", mb, snowy);

        // inside, where no snow settles: the floors, the lamp board (2 x 2 m, bottom 0.5, face z 20.35) and a snowflake
        var inside = new MeshBuilder();
        Blk(inside, -2.38f, 2.38f, -Sink, 0.03f, B1, Front, IsleKit.DarkWood);
        Blk(inside, -1.1f, 1.1f, -Sink, 0.02f, 20.3f, B0, IsleKit.DarkWood);
        Blk(inside, -1.0f, 1.0f, 0.5f, 2.5f, 20.3f, 20.35f, IsleKit.Board);
        for (int a = 0; a < 3; a++)
        {
            float ang = a * Mathf.PI / 3f;
            var d = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * 0.13f;
            var w = new Vector3(-Mathf.Sin(ang), Mathf.Cos(ang), 0f) * 0.015f;
            var centre = new Vector3(0f, 2.64f, 20.31f);
            inside.Quad(centre - d - w, centre + d - w, centre + d + w, centre - d + w, Color.white);
            inside.Quad(centre - d + w, centre + d + w, centre + d - w, centre - d - w, Color.white);
        }
        AddMesh("Hut Inside", inside, bare);

        // the pane: a thin box made here (the Glass prefab is a flat plane that Part lays down), clear, muffling
        var pane = new MeshBuilder();
        Kit.AddBox(pane, new Vector3(0f, 0f, 21.9f), new Vector3(1.30f, 2.06f, 0.03f), Color.white);
        Piece glass = AddMesh("Closet Pane", pane, bare, true);
        Component glassC = b.AddScript(glass, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // Clear
        Sdk.Set(glassC, "sound", 0);      // Muffles
        Sdk.Set(glassC, "wire", false);

        // a low warm light in the closet so the board reads (Winter Walk's RoomLights: colour, range, intensity)
        E("Room Light 1 FFE2B8 3.0 0.9", 0f, 21.0f, 2.4f);
    }

    // ================= mesh helpers =================

    // An axis-aligned block x0..x1, y0..y1, z0..z1
    static void Blk(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);

    // A ridge: a triangular prism from 'from' to 'to' (its base line, level), 'width' across and 'height' to its peak,
    // with both ends closed; wound as Kit.AddBox so it shows from outside
    static void Prism(MeshBuilder mb, Vector3 from, Vector3 to, float width, float height, Color c)
    {
        Vector3 u = to - from; u.y = 0f;
        if (u.sqrMagnitude < 1e-6f) return;
        u.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, u) * (width / 2f), up = Vector3.up * height;
        Vector3 a0 = from + r, a1 = to + r, b0 = from - r, b1 = to - r, t0 = from + up, t1 = to + up;
        mb.Quad(a0, a1, t1, t0, c);
        mb.Quad(b1, b0, t0, t1, c);
        mb.Tri(a1, t1, b1, c);
        mb.Tri(b0, t0, a0, c);
    }

    // A square pyramid cap on a wall junction: base 2 * half across at 'foot', peak 'height' above
    static void Pyramid(MeshBuilder mb, Vector3 foot, float half, float height, Color c)
    {
        Vector3 apex = foot + Vector3.up * height;
        var corner = new[]
        {
            foot + new Vector3(-half, 0f, -half), foot + new Vector3(half, 0f, -half),
            foot + new Vector3(half, 0f, half), foot + new Vector3(-half, 0f, half),
        };
        for (int i = 0; i < 4; i++) mb.Tri(corner[i], apex, corner[(i + 1) % 4], c);
    }
}
