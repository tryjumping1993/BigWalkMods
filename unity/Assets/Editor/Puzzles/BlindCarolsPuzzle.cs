// Blind Carols (puzzle 9, blind mask): the Carol Chapel. An unseen choir sings four carols (five in a 4-player
// lobby), dealt again from nine tunes on every start, and they can only be heard through the headphones of the pews
// (Listening Chairs): each pew's microphone hangs beside its carol's speaker 120 to 420 m under the yard, and nothing
// else in the game blocks music, so distance is the seal. The Listener sits in a pew and hums what they hear (the
// pew yard is a radio dead zone, and a seated player hears nothing else). The Guide stands at the vestry's
// soundproof window: they hear the humming by ear, see the room through the glass, hear the speaking tile the Hanger
// holds up on the window niche, and steer the Hanger by intercom. The Hanger is inside the sealed vestry,
// blindfolded (BlindWatch puts anyone in there without a blindfold out in the porch): they carry tiles from the bin
// to the niche and hang the matches in the slots under each carol's colour. The blind turnstile opens only while
// someone sits in the pews, and passing either vestry gate stops the puzzle, so the Hanger never hears the carols
// first-hand. A 10-minute clock stops it too. Spec: puzzle-specs\BlindCarols.md (revision 2).
//
// Coordinates: X east, Z north, h above the levelled pad (every part is placed on one plane, the ground at the
// centre). Walls are 0.24 thick and centred on the room's lines (Z 6 and 11, X -1.5, 5.5 and 9.5).
using Bwm;
using UnityEngine;

public sealed class BlindCarolsPuzzle : PuzzleDef
{
    public override string Name => "BlindCarols";
    public override Vector2 Footprint => new Vector2(30f, 26f);
    public override Vector2? FixedSite => new Vector2(-2724f, -1557f);      // layout-final: a chapel in a quiet far-west wood (Cirque Gate, M13)

    const float T = 0.12f;           // half a wall's thickness
    const float WallH = 2.8f;
    const float ClockSeconds = 600f;

    PuzzleBuilder b;
    float baseH;                     // the pad's level above the root's

    Piece P(string prefab, float x, float z, float h, float yaw) => b.Part(prefab, x, z, baseH + h, yaw, false);
    Piece E(string name, float x, float z, float h) => b.Empty(name, x, z, baseH + h, false);
    Piece MeshAt(string name, MeshBuilder mb, Material m, bool collider = true)
    {
        Piece p = b.MeshPiece(name, mb.ToMesh(), m, collider);
        p.Transform.position = b.WorldPoint(0f, 0f, baseH, false);
        return p;
    }

    // A block from its two corners (x0..x1, z0..z1, h0..h1), into a mesh in the pad's frame
    static void Blk(MeshBuilder mb, float x0, float x1, float z0, float z1, float h0, float h1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) * 0.5f, h0, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h1 - h0, z1 - z0), c);

    // A bell cote (a sealed little tower, decoration only) at (x, z) on a plinth 0.4 high; its painted bell and colour
    // band go on the face towards the pews (north, or east for the west belfry)
    static void Cote(MeshBuilder body, MeshBuilder paint, float x, float z, Color band, bool faceEast)
    {
        Blk(body, x - 0.5f, x + 0.5f, z - 0.5f, z + 0.5f, 0.4f, 2.6f, IsleKit.Cream);
        Blk(body, x - 0.6f, x + 0.6f, z - 0.6f, z + 0.6f, 2.6f, 2.75f, IsleKit.DarkWood);
        if (faceEast)
        {
            Blk(paint, x + 0.5f, x + 0.53f, z - 0.4f, z + 0.4f, 1.95f, 2.12f, band);
            Blk(paint, x + 0.5f, x + 0.53f, z - 0.32f, z + 0.32f, 1.05f, 1.15f, band);
            Kit.AddBall(paint, new Vector3(x + 0.5f, 1.55f, z), 0.2f, IsleKit.Gold);
        }
        else
        {
            Blk(paint, x - 0.4f, x + 0.4f, z + 0.5f, z + 0.53f, 1.95f, 2.12f, band);
            Blk(paint, x - 0.32f, x + 0.32f, z + 0.5f, z + 0.53f, 1.05f, 1.15f, band);
            Kit.AddBall(paint, new Vector3(x, 1.55f, z + 0.5f), 0.2f, IsleKit.Gold);
        }
    }

    // A carol: its speaker deep under the yard and a directional mic 0.5 m north of it, facing it
    (Piece speaker, Piece mic) Carol(string colour, float x, float z, float depth)
    {
        Piece speaker = P("Peg Sound Mounted", x, z, depth, 0f).Named("Carol " + colour);
        Piece mic = P("Directional Mic", x, z + 0.5f, depth, 180f).Named("Mic " + colour);
        return (speaker, mic);
    }

    Piece Pew(string colour, float x, float z, float yaw, Piece mic)
    {
        Piece chair = P("Listening Chair", x, z, 0f, yaw).Named("Pew " + colour);
        Sdk.Set(chair.Component("PuzzleListeningChair"), "microphone", mic.Component("PuzzleDirectionalMic"));
        return chair;
    }

    Piece DeadZone(string name, float x, float z, Vector3 size)
    {
        Piece dead = P("Radio Dead Zone", x, z, size.y / 2f, 0f).Named(name);
        BoxCollider box = dead.Go.GetComponent<BoxCollider>();
        if (box == null) box = dead.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        return dead;
    }

    // The peg board on the vestry's north wall (pivot at its west end: slots run 0.45 m apart towards +X), its validator,
    // and a colour bar over each slot
    void Board(string tag, float pivotX, int sizeIndex, Component[] answers, Color[] colours, Piece group)
    {
        Piece validator = P("Validator", 3.5f, 10.86f, 1.65f, 180f).Named("Validator " + tag).Under(group);
        Piece board = P("Peg Board", pivotX, 10.86f, 1.3f, 180f).Named("Carol Board " + tag).Under(group);
        Component boardC = board.Component("PuzzlePegBoard");
        Sdk.Set(boardC, "size", sizeIndex);
        Sdk.Add(boardC, "answerFrom", answers);
        var flaps = new int[answers.Length];
        for (int i = 0; i < flaps.Length; i++) flaps[i] = 1;
        Sdk.AddInts(boardC, "answerFromFlap", flaps);
        Sdk.Set(boardC, "validator", validator.Component("PuzzleValidator"));
        var marks = new MeshBuilder();
        for (int i = 0; i < colours.Length; i++)
        {
            float x = pivotX + 0.45f * i;
            Blk(marks, x - 0.15f, x + 0.15f, 10.85f, 10.88f, 1.56f, 1.68f, colours[i]);
        }
        MeshAt("Slot Marks " + tag, marks, IsleKit.Bare(b), false).Under(group);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        baseH = b.WorldPoint(0f, 0f).y - b.Root.transform.position.y;
        b.ClearGround(30f, 26f);
        b.DebugTeleport(2f, 3f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // no jetpacks over the chapel (Winter Walk's Calm Air): the whole yard, from 10 m under the pad to 120 m above
        IsleKit.CalmAir(b, "Blind Carols", 0f, 0f, new Vector3(40f, 130f, 36f), 0f, baseH - 10f, false);

        // ================= the vestry and its porch (room X -1.5..5.5, porch 5.5..9.5, Z 6..11) =================
        Piece vestry = E("Vestry", 2f, 8.5f, 0f);
        Sdk.Set(b.AddScript(vestry, "PuzzleSoundProof"), "material", 0);      // Wood
        var walls = new MeshBuilder();
        float xw = -1.5f - T, xe = 9.5f + T;
        Blk(walls, xw, xe, 6f - T, 11f + T, -0.6f, 0f, IsleKit.Stone);                     // footing
        // south wall, the window opening X 0.8..3.2, h 0.9..2.3
        Blk(walls, xw, 0.8f, 6f - T, 6f + T, 0f, WallH, IsleKit.Wood);
        Blk(walls, 3.2f, xe, 6f - T, 6f + T, 0f, WallH, IsleKit.Wood);
        Blk(walls, 0.8f, 3.2f, 6f - T, 6f + T, 0f, 0.9f, IsleKit.Wood);
        Blk(walls, 0.8f, 3.2f, 6f - T, 6f + T, 2.3f, WallH, IsleKit.Wood);
        // north and west walls
        Blk(walls, xw, xe, 11f - T, 11f + T, 0f, WallH, IsleKit.Wood);
        Blk(walls, xw, -1.5f + T, 6f + T, 11f - T, 0f, WallH, IsleKit.Wood);
        // the room's east wall, with the opening into the porch at Z 7.9..9.1, h 0..2.3
        Blk(walls, 5.5f - T, 5.5f + T, 6f + T, 7.9f, 0f, WallH, IsleKit.Wood);
        Blk(walls, 5.5f - T, 5.5f + T, 9.1f, 11f - T, 0f, WallH, IsleKit.Wood);
        Blk(walls, 5.5f - T, 5.5f + T, 7.9f, 9.1f, 2.3f, WallH, IsleKit.Wood);
        // the porch's outer wall: the two gates fill it (Z 6.04..8.16 and 8.84..10.96), a pier between, lintels over
        Blk(walls, 9.5f - T, 9.5f + T, 8.16f, 8.84f, 0f, 2.3f, IsleKit.Wood);
        Blk(walls, 9.5f - T, 9.5f + T, 6f + T, 11f - T, 2.3f, WallH, IsleKit.Wood);
        // the baffle in the porch: no straight line from either gate reaches the room's opening
        Blk(walls, 7.6f - T, 7.6f + T, 6.7f, 10.3f, 0f, WallH, IsleKit.Wood);
        // the roof over both
        Blk(walls, -1.7f, 9.7f, 5.7f, 11.3f, WallH, WallH + 0.24f, IsleKit.Red);
        MeshAt("Vestry Walls", walls, snowy).Under(vestry);

        // inside (never snowy): the floor, the window niche with its lip, the tile bin in the NW corner
        var fit = new MeshBuilder();
        Blk(fit, -1.5f + T, 9.5f - T, 6f + T, 11f - T, 0f, 0.04f, IsleKit.Wood);
        Blk(fit, 1.0f, 3.0f, 6f + T, 6.45f, 0f, 0.9f, IsleKit.DarkWood);
        Blk(fit, 1.0f, 3.0f, 6.42f, 6.45f, 0.9f, 0.93f, IsleKit.DarkWood);
        const float binX0 = -1.32f, binX1 = 0.45f, binZ0 = 9.4f, binZ1 = 10.84f, binSide = 0.06f;
        Blk(fit, binX0, binX1, binZ0, binZ1, 0f, 0.6f, IsleKit.Wood);
        Blk(fit, binX0, binX1, binZ0, binZ0 + binSide, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX0, binX1, binZ1 - binSide, binZ1, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX0, binX0 + binSide, binZ0 + binSide, binZ1 - binSide, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX1 - binSide, binX1, binZ0 + binSide, binZ1 - binSide, 0.6f, 0.95f, IsleKit.Wood);
        MeshAt("Vestry Fittings", fit, bare);

        // the window: wired soundproof glass, filling the opening exactly. The Glass prefab is Unity's 10 m Plane stood
        // upright by a 90 degree X turn; Part keeps only the yaw (the pane would lie flat, an 11.5 x 10 m slab through
        // the room), so put the turn back and size the plane directly: 2.4 m wide (local X), 1.4 m tall (local Z).
        // Yaw 180 turns the one-sided plane's face towards the Guide.
        Piece window = b.Part("Glass", 2f, 6f, baseH + 1.6f, 180f, false).Named("Vestry Window")
            .Set("PuzzleGlass", "style", 0).Set("PuzzleGlass", "sound", 2).Set("PuzzleGlass", "wire", true);
        window.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f);
        window.Transform.localScale = new Vector3(2.4f / 10f, 0.18683f, 1.4f / 10f);
        BoxCollider pane = window.Go.GetComponent<BoxCollider>();
        if (pane != null) pane.size = new Vector3(10f, 0.04f / 0.18683f, 10f);      // 4 cm thick rather than none

        // nine speaking tiles jumbled in the bin: which tile plays which tune is shuffled on every build; they stand in
        // a 3 x 3 grid (cells 0.55 x 0.44 m for a 0.448 x 0.337 m tile) with a little play in place and turn (up to
        // 10 degrees), so none starts inside another or the bin; a tile's body sits 0.0685 m behind its pivot
        var rng = new System.Random((int)(System.DateTime.Now.Ticks & 0x7fffffff));
        int[] tunes = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        for (int i = tunes.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = tunes[i]; tunes[i] = tunes[j]; tunes[j] = t; }
        float cellX = (binX1 - binX0 - 2f * binSide) / 3f, cellZ = (binZ1 - binZ0 - 2f * binSide) / 3f;
        for (int i = 0; i < 9; i++)
        {
            float x = binX0 + binSide + cellX * (i % 3 + 0.5f) + (float)(rng.NextDouble() - 0.5) * 0.04f;
            float z = binZ0 + binSide + cellZ * (i / 3 + 0.5f) + 0.0685f + (float)(rng.NextDouble() - 0.5) * 0.02f;
            float yaw = (float)(rng.NextDouble() - 0.5) * 20f;
            P("Peg Sound", x, z, 0.85f, yaw).Named("Speaking Tile " + (i + 1)).Set("PuzzleSoundPeg", "sound", tunes[i]);
        }

        // the gates: in blindfolded (one way, east to west, locked unless someone sits in the pews), out (never locked)
        Piece blindGate = IsleKit.BlindGate(b, 9.5f, 7.1f, -90f, baseH, false).Set("PuzzleNoItemsGate", "defaultLocked", true);
        Piece wayOut = P("Gate No items", 9.5f, 9.9f, 0f, 90f).Named("Way Out")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        Component sdk = b.Puzzle;
        foreach (Piece gate in new[] { blindGate, wayOut })
        {
            gate.On("PuzzleNoItemsGate", "onEnter", sdk, "StopPuzzle");
            gate.On("PuzzleNoItemsGate", "onExit", sdk, "StopPuzzle");
        }
        Component blindGateC = blindGate.Component("PuzzleNoItemsGate");

        // the inner start switch (moves with the outside one) and live lamps, in the Guide's view
        P("Switch Start", 5.5f - T - 0.073f, 9.7f, 1.25f, -90f).Named("Inner Start Switch");
        P("Live Light", 5.5f - T - 0.005f, 9.7f, 1.6f, -90f).Named("Inner Live Light");
        P("Live Light", 0.6f, 11f - T - 0.01f, 2.3f, 180f).Named("Vestry Live Light");

        // intercoms: the Guide's mic outside plays in the room and the porch; the Hanger's mic plays at the window
        Piece windowSpeaker = P("Intercom Speaker", 0.25f, 6f - T - 0.1f, 2.2f, 180f).Named("Window Speaker");
        Piece vestrySpeaker = P("Intercom Speaker", 2.0f, 11f - T - 0.1f, 2.35f, 180f).Named("Vestry Speaker");
        Piece porchSpeaker = P("Intercom Speaker", 7.6f + T + 0.1f, 7.6f, 2.2f, 90f).Named("Porch Speaker");
        Piece guideMic = P("Intercom Mic", 3.75f, 6f - T - 0.02f, 1.35f, 180f).Named("Guide Mic");
        Piece vestryMic = P("Intercom Mic", 4.4f, 11f - T - 0.02f, 1.3f, 180f).Named("Vestry Mic");
        guideMic.Add("PuzzleIntercom", "speakers", vestrySpeaker.Component("PuzzleIntercomSpeaker"), porchSpeaker.Component("PuzzleIntercomSpeaker"));
        vestryMic.Add("PuzzleIntercom", "speakers", windowSpeaker.Component("PuzzleIntercomSpeaker"));

        // the clocks: one in the room (seen through the window), one over the belfry facing the pews
        Piece vestryClock = P("Timer", -0.4f, 11f - T - 0.12f, 2.0f, 180f).Named("Vestry Clock").Set("PuzzleTimer", "duration", ClockSeconds);
        Piece belfryClock = P("Timer", 2f, -9.5f + 0.12f, 3.0f, 0f).Named("Belfry Clock").Set("PuzzleTimer", "duration", ClockSeconds);
        Component vClock = vestryClock.Component("PuzzleTimer"), bClock = belfryClock.Component("PuzzleTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", vClock, "StartTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", bClock, "StartTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", vClock, "ResetTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", bClock, "ResetTimer");
        b.RootPiece.On("PuzzleSDK", "onWin", vClock, "PauseTimer");
        b.RootPiece.On("PuzzleSDK", "onWin", bClock, "PauseTimer");
        vestryClock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");

        // a hook by the Way Out for a blindfold the plain gate will not let through, and three on the rack outside
        Piece back = P("Hook", 8.3f, 11f - T - 0.03f, 1.55f, 180f).Named("Blindfold Return");
        Sdk.SetFlags(back.Component("PuzzleHook"), "acceptedVanilla", 1);
        IsleKit.BlindfoldHook(b, 12.6f, 4.6f, -90f, baseH, false, " A");
        IsleKit.BlindfoldHook(b, 12.6f, 5.8f, -90f, baseH, false, " B");
        IsleKit.BlindfoldHook(b, 12.6f, 7.0f, -90f, baseH, false, " C");

        // BlindWatch: anyone in the room or the inner porch without a blindfold drops what they hold and is put in the
        // porch (outside its box and 1.2 m clear of both gates), from where only the Way Out leads on
        Piece watch = E("Blind Watch", 2f, 8.5f, 0f);
        Piece spill = E("Porch Spill", 8.45f, 8.5f, 0.1f);
        spill.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        Component bw = b.AddCustomScript(watch, "BlindWatch");
        Sdk.Set(bw, "boxMin", new Vector3(-3.4f, -0.5f, -2.4f));      // X -1.4, Z 6.1
        Sdk.Set(bw, "boxMax", new Vector3(6.1f, 2.6f, 2.4f));         // X 8.1, Z 10.9
        Sdk.Set(bw, "putOut", spill.Transform);
        Sdk.Add(bw, "gates", blindGate.Transform, wayOut.Transform);
        Sdk.Set(bw, "gateSkip", 1.2f);
        Sdk.Set(bw, "cooldown", 1.5f);

        // warm room lamps so the Guide can see in at night (Winter Walk's RoomLights)
        E("Room Light 1 FFC88C 6.0 0.8", 0.5f, 8.5f, 2.5f);
        E("Room Light 2 FFC88C 6.0 0.8", 4.0f, 8.5f, 2.5f);

        // ================= the yard: the Guide's step, the kerb behind the pews =================
        var yard = new MeshBuilder();
        Blk(yard, 0.5f, 3.5f, 4.25f, 5.75f, -0.3f, 0.15f, IsleKit.Stone);
        Blk(yard, -2.6f, 6.8f, -4.4f, -4.2f, -0.2f, 0.15f, IsleKit.Stone);
        MeshAt("Chapel Yard", yard, snowy);

        // ================= the south belfry: plinth, red / green / gold cotes, a crossbeam with the clock =================
        var belfry = new MeshBuilder();
        var paint = new MeshBuilder();
        Blk(belfry, -2.3f, 6.3f, -9.8f, -8.2f, -0.6f, 0.4f, IsleKit.Stone);
        Cote(belfry, paint, -1.3f, -9f, IsleKit.Red, false);
        Cote(belfry, paint, 0.9f, -9f, IsleKit.Green, false);
        Cote(belfry, paint, 3.1f, -9f, IsleKit.Gold, false);
        Blk(belfry, -2.7f, -2.5f, -9.62f, -9.42f, -0.3f, 3.4f, IsleKit.Wood);       // posts
        Blk(belfry, 6.5f, 6.7f, -9.62f, -9.42f, -0.3f, 3.4f, IsleKit.Wood);
        Blk(belfry, -2.8f, 6.8f, -9.62f, -9.42f, 3.2f, 3.4f, IsleKit.DarkWood);      // crossbeam
        Blk(belfry, 0.4f, 3.6f, -9.62f, -9.5f, 2.78f, 3.2f, IsleKit.Board);          // the clock's panel, over the cotes
        Blk(belfry, 1.55f, 2.45f, -9.62f, -9.5f, 1.9f, 2.78f, IsleKit.Board);        // the lamp's, between them
        MeshAt("South Belfry", belfry, snowy);
        MeshAt("South Belfry Paint", paint, bare, false);
        P("Live Light", 2f, -9.5f + 0.006f, 2.4f, 0f).Named("Belfry Live Light");
        DeadZone("Belfry Hush South", 2f, -7.5f, new Vector3(14f, 8f, 8f));          // X -5..9, Z -11.5..-3.5

        // the three carols every lobby has, and their pews (facing the cotes)
        var red = Carol("Red", -8f, -8f, -120f);
        var green = Carol("Green", 8f, -8f, -180f);
        var gold = Carol("Gold", -8f, 8f, -240f);
        Pew("Red", -1.3f, -6f, 180f, red.mic);
        Pew("Green", 0.9f, -6f, 180f, green.mic);
        Pew("Gold", 3.1f, -6f, 180f, gold.mic);

        // ================= 2 and 3 players: a blue pew in the south belfry, a 4-slot board =================
        Piece lobby23 = IsleKit.Lobby(b, "Lobby 2-3", true, true, false);
        var blue = Carol("Blue", 8f, 8f, -300f);
        blue.speaker.Under(lobby23);
        blue.mic.Under(lobby23);
        Pew("Blue", 5.3f, -6f, 180f, blue.mic).Under(lobby23);
        var blueCote = new MeshBuilder();
        var bluePaint = new MeshBuilder();
        Cote(blueCote, bluePaint, 5.3f, -9f, IsleKit.Ice, false);
        MeshAt("Blue Cote", blueCote, snowy).Under(lobby23);
        MeshAt("Blue Cote Paint", bluePaint, bare, false).Under(lobby23);
        Board("4", 1.025f, 3,
            new[] { red.speaker.Component("PuzzleSoundPegAnswer"), green.speaker.Component("PuzzleSoundPegAnswer"),
                    gold.speaker.Component("PuzzleSoundPegAnswer"), blue.speaker.Component("PuzzleSoundPegAnswer") },
            new[] { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice }, lobby23);
        // whoever sits in a pew unlocks the blind turnstile; the box covers the chairs' whole footprint (they reach
        // from Z -6.4 to -4.8, and from 0.4 m west to 1.1 m east of each pivot)
        Piece pewRow = IsleKit.Sensor(b, "Pew Row", 2.1f, -5.9f, new Vector3(9.4f, 2.6f, 2.8f), baseH, false).Under(lobby23);
        pewRow.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        pewRow.On("PuzzlePresenceSensor", "onPressed", blindGateC, "Unlock");
        pewRow.On("PuzzlePresenceSensor", "onReleased", blindGateC, "Lock");

        // ================= 4 players: the West Belfry with blue and cream pews, a 5-slot board =================
        Piece lobby4 = IsleKit.Lobby(b, "Lobby 4", false, false, true);
        var blueW = Carol("Blue W", 0f, 0f, -360f);
        var creamW = Carol("Cream W", 0f, -10f, -420f);
        foreach (var c in new[] { blueW, creamW }) { c.speaker.Under(lobby4); c.mic.Under(lobby4); }
        Pew("Blue W", -9f, 5.1f, -90f, blueW.mic).Under(lobby4);
        Pew("Cream W", -9f, 2.9f, -90f, creamW.mic).Under(lobby4);
        var west = new MeshBuilder();
        var westPaint = new MeshBuilder();
        Blk(west, -12.8f, -11.2f, 1.8f, 6.2f, -0.6f, 0.4f, IsleKit.Stone);
        Cote(west, westPaint, -12f, 5.1f, IsleKit.Ice, true);
        Cote(west, westPaint, -12f, 2.9f, IsleKit.Cream, true);
        Blk(west, -13.1f, -10.9f, 1.5f, 6.5f, 2.75f, 2.95f, IsleKit.Red);           // one roof over both
        Blk(west, -7.25f, -7.05f, 1.4f, 6.6f, -0.2f, 0.15f, IsleKit.Stone);          // kerb behind the pews
        MeshAt("West Belfry", west, snowy).Under(lobby4);
        MeshAt("West Belfry Paint", westPaint, bare, false).Under(lobby4);
        DeadZone("Belfry Hush West", -10.25f, 4f, new Vector3(6.5f, 8f, 7f)).Under(lobby4);      // X -13.5..-7, Z 0.5..7.5
        Board("5", 0.8f, 4,
            new[] { red.speaker.Component("PuzzleSoundPegAnswer"), green.speaker.Component("PuzzleSoundPegAnswer"),
                    gold.speaker.Component("PuzzleSoundPegAnswer"), blueW.speaker.Component("PuzzleSoundPegAnswer"),
                    creamW.speaker.Component("PuzzleSoundPegAnswer") },
            new[] { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice, IsleKit.Cream }, lobby4);
        // one sensor, one box over each pew row, so it lets go only when both rows are empty
        Piece pewRow4 = P("Presence Sensor", 0f, 0f, 1.3f, 0f).Named("Pew Row 4").Under(lobby4);
        BoxCollider south = pewRow4.Go.GetComponent<BoxCollider>();
        if (south == null) south = pewRow4.Go.AddComponent<BoxCollider>();
        south.center = new Vector3(1.3f, 0f, -5.9f);                 // X -2.5..5.1, Z -7.3..-4.5
        south.size = new Vector3(7.6f, 2.6f, 2.8f);
        BoxCollider westBox = pewRow4.Go.AddComponent<BoxCollider>();
        westBox.isTrigger = south.isTrigger;
        westBox.center = new Vector3(-8.6f, 0f, 3.6f);               // X -9.9..-7.3, Z 1.2..6.0
        westBox.size = new Vector3(2.6f, 2.6f, 4.8f);
        pewRow4.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        pewRow4.On("PuzzlePresenceSensor", "onPressed", blindGateC, "Unlock");
        pewRow4.On("PuzzlePresenceSensor", "onReleased", blindGateC, "Lock");

        // ================= the start corner and the lights =================
        IsleKit.StartCorner(b, -5f, 0.5f, 90f, true, baseH, false);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 28f, 24f, 2.6f, true, 91);
    }
}
