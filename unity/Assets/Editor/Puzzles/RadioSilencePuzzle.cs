// Radio Silence: a snowbound signal station over the gorge north of the plaza, for three players (four with a Lookout,
// two the hard way). The picture to match is split across sealed, windowless rooms. The Watcher is shut in the
// Listening Hut on the gorge floor with a three-flap display; the Relay in the Relay Booth on the near lip with a
// two-flap display; the Signalman works the Signal Box on the flat, 55 m away, with ten scribble tiles and the peg
// board. Voice runs one way only, down two "wires" made of the game's studio microphone and listening chair: whoever
// sits in the Booth's chair hears the Hut, whoever sits in the Box's chair hears the Booth, and nobody hears back. The
// only way back is a hold button that lights an amber lamp in the room before. A five-minute clock runs; a wrong check
// ends the round and re-deals; stepping out of a sealed room during a round (into its vestibule) ends it too. With
// four players a Lookout is sealed in a cabin on the far lip with a sixth picture and talks into the Relay's
// headphones over an intercom.
//
// Every sealed room is entered only through a roofed vestibule with a No Items turnstile, so no radio gets in. The
// rooms are PuzzleSoundProof shells holding only walls, roof and floor; every SDK part stands in the puzzle's frame,
// never under a shell (the shell turns child triggers solid). Spec: puzzle-specs\RadioSilence.md.
//
// Parts face their local +z; "yaw" turns that: 90 faces +x (east), -90 faces -x (west), 180 faces -z.
using System.Collections.Generic;
using System.Globalization;
using Bwm;
using UnityEngine;

public sealed class RadioSilencePuzzle : PuzzleDef
{
    public override string Name => "RadioSilence";
    public override Vector2 Footprint => new Vector2(20f, 18f);
    public override Vector2? FixedSite => Site;      // layout-final.json: the Signal Box on the flat south of the gorge
    public override float Yaw => 0f;

    static readonly Vector2 Site = new Vector2(-760f, -730f);
    // The gorge's second segment (WinterIslePuzzle.Gorge, which is private: keep in step by hand). Its floor is full
    // depth only within 3.5 m of this line and climbs out by 13.5 m.
    static readonly Vector2 GorgeA = new Vector2(-815f, -668f), GorgeB = new Vector2(-760f, -660f);
    static readonly Vector2 HutWorld = new Vector2(-792f, -664f);       // on the gorge floor (moved onto the line, below)
    static readonly Vector2 BoothWorld = new Vector2(-789f, -683f);     // the near (south) lip, 19 m from the line
    static readonly Vector2 CabinWorld = new Vector2(-803f, -647f);     // the far (north) lip, 19 m from the line
    const float HutPadShift = 1.6f;      // the Hut's pad centre is 1.6 m behind its anchor; that centre goes on the line

    // The ravine posts' ground, which the island should keep clear without levelling the gorge (world x, z, radius).
    // The framework list the spec asks for (PuzzleDef.KeepClearSites) does not exist yet: when it is added as a
    // virtual, mark this 'override'. Until then the island neither clears nor levels these (see RadioSilence.impl.md).
    public IEnumerable<Vector3> KeepClearSites => new[]
    {
        new Vector3(HutAnchorWorld().x, HutAnchorWorld().y, 7f),
        new Vector3(BoothWorld.x, BoothWorld.y, 7f),
        new Vector3(CabinWorld.x, CabinWorld.y, 7f),
    };

    static Vector2 GorgeDir => (GorgeB - GorgeA).normalized;
    // the yaw that lays a post's +x along the gorge (so its +z points north, across it)
    static float GorgeYaw => Mathf.Atan2(-GorgeDir.y, GorgeDir.x) * Mathf.Rad2Deg;
    static Vector2 HutAnchorWorld()
    {
        Vector2 foot = GorgeA + GorgeDir * Vector2.Dot(HutWorld - GorgeA, GorgeDir);
        return foot + IsleKit.Turn(0f, HutPadShift, GorgeYaw);
    }

    const float T = 0.2f;        // wall thickness
    const float H = 2.6f;        // inside height of the sealed rooms
    const float Floor = 0.25f;   // the Signal Box's floor above the root's level
    static readonly Color HutBlue = new Color(0.36f, 0.58f, 0.80f);
    static readonly Color BoxWood = new Color(0.50f, 0.35f, 0.22f);
    static readonly Color Amber = new Color(1f, 0.7f, 0.2f);
    static readonly Color FloorBoards = new Color(0.30f, 0.22f, 0.15f);
    static readonly Color GourdBlue = new Color(0.2f, 0.35f, 0.85f);

    // A post: its own frame (x right, z to its front) at an anchor in the puzzle's frame, turned by its yaw, with heights
    // from its pad's top. A sealed room's inside is x -Hx..Hx, z -Hz..Hz, its door in the -z wall at DoorX.
    sealed class Post
    {
        public string Name;
        public Vector2 At;
        public float Yaw, Top, Hx, Hz, DoorX;
        public Color Wall, Trim;
        public float PadX0, PadX1, PadZ0, PadZ1;
        public Vector2 L(float x, float z) => At + IsleKit.Turn(x, z, Yaw);
        public float DoorZ => -Hz - T / 2f;
        public float GateZ => DoorZ - 2.4f;            // the vestibule's outer wall, 2.4 m out from the door
        public float GateX => DoorX - 0.05f;           // turned 180, the gate's off-centre model spans DoorX +- 1.06
    }

    struct Room { public Piece Door, Gate, Sensor, Lamp; }

    PuzzleBuilder b;
    Material snowy, bare;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        Vector3 rootPos = b.Root.transform.position;
        Vector2 Local(Vector2 world) => new Vector2(world.x - rootPos.x, world.y - rootPos.z);

        b.DebugTeleport(0f, -6f);
        b.ClearGround(22f, 20f, 0f, 0.5f);
        Sdk.Set(b.Puzzle, "gourdColor", GourdBlue);

        Piece four = IsleKit.Lobby(b, "Four", false, false, true);
        Piece boards23 = IsleKit.Lobby(b, "Board 2-3", true, true, false);
        Piece boothSeal = IsleKit.Lobby(b, "Booth Seal 3-4", false, true, true);

        // ---- the posts ----
        var hut = new Post { Name = "Hut", At = Local(HutAnchorWorld()), Yaw = GorgeYaw, Hx = 1.9f, Hz = 1.4f, DoorX = -1.0f, Wall = HutBlue, Trim = IsleKit.Ice };
        var booth = new Post { Name = "Booth", At = Local(BoothWorld), Hx = 1.7f, Hz = 1.5f, DoorX = -0.9f, Wall = IsleKit.Red, Trim = IsleKit.Cream };
        booth.Yaw = Mathf.Atan2(booth.At.x, booth.At.y) * Mathf.Rad2Deg;      // its door (-z) towards the Signal Box at the root
        var cabin = new Post { Name = "Lookout", At = Local(CabinWorld), Yaw = GorgeYaw + 180f, Hx = 1.5f, Hz = 1.3f, DoorX = -0.8f, Wall = IsleKit.Wood, Trim = IsleKit.Green };
        var box = new Post { Name = "Box", At = Vector2.zero, Yaw = 0f, Top = Floor };
        foreach (Post p in new[] { hut, booth, cabin }) b.ClearGround(12f, 12f, p.At.x, p.At.y);

        // ---- Listening Hut, on the gorge floor: the Watcher, Display A, the studio mic heard in the Booth's chair ----
        Room h = BuildRoom(hut, null, null, -1.0f);
        Piece displayA = P(hut, "Display", 0.75f, -hut.Hz + 0.13f, 1.9f, 0f).Named("Display A").Set("PuzzleDisplay", "size", 2);      // Three
        Piece hutMic = P(hut, "Directional Mic", 0.75f, -0.55f, 1.3f, 180f).Named("Hut Mic");      // faces the mat under Display A
        Piece hutAsk = P(hut, "Light 8", hut.Hx - 0.12f, -0.6f, 1.9f, -90f).Named("Hut Ask Lamp").Set("PuzzleLight", "color", Amber);
        Piece hutBar = P(hut, "Progress Bar", 0.8f, hut.Hz - 0.07f, 1.9f, 180f).Named("Hut Time Bar").Scaled(0.35f, 0.35f, 1f);
        var hutDeco = new MeshBuilder();
        Blk(hutDeco, hut, 0.25f, 1.25f, -1.15f, -0.65f, 0.02f, 0.035f, HutBlue);                  // the mat: stand here
        MicCollar(hutDeco, hut, 0.75f, -0.55f, 1.3f, HutBlue);
        b.MeshPiece("Hut Inside", hutDeco.ToMesh(), bare, false);
        LightLoop(hut, "Hut Lights", 131);

        // ---- Relay Booth, on the near lip: the Relay, Display B, the chair (hears the Hut), the mic heard in the Box ----
        Room r = BuildRoom(booth, null, boothSeal, -1.4f);
        Piece displayB = P(booth, "Display", 0.4f, -booth.Hz + 0.13f, 1.35f, 0f).Named("Display B").Set("PuzzleDisplay", "size", 1);  // Two, at seated eye level
        // The chair's model runs x -1.135..0.38, z -1.188..0.401 about its pivot: turned 180, its back stops at the back wall
        Piece boothChair = P(booth, "Listening Chair", 0.3f, booth.Hz - 1.22f, 0f, 180f).Named("Booth Chair");
        Sdk.Set(boothChair.Component("PuzzleListeningChair"), "microphone", hutMic.Component("PuzzleDirectionalMic"));
        // the studio mic hangs to the chair's left, clear of the line from the seat to Display B, aimed at the sitter's head
        Vector3 boothMicAt = V(booth, -0.2f, 1.25f, -0.35f), sitterHead = V(booth, 0.3f, 1.05f, 0.1f);
        Piece boothMic = P(booth, "Directional Mic", -0.2f, -0.35f, 1.25f, 0f).Named("Booth Mic");
        boothMic.Transform.rotation = b.Root.transform.rotation * Quaternion.LookRotation(sitterHead - boothMicAt);
        Piece boothAsk = P(booth, "Light 8", booth.Hx - 0.12f, -0.6f, 1.6f, -90f).Named("Booth Ask Lamp").Set("PuzzleLight", "color", Amber);
        Piece boothBar = P(booth, "Progress Bar", booth.DoorX, -booth.Hz + 0.07f, 2.3f, 0f).Named("Booth Time Bar").Scaled(0.3f, 0.3f, 1f);
        // "ask" buttons at the chair's arms: to the Hut on a short post at its left, to the Lookout on the side wall (4p)
        var boothDeco = new MeshBuilder();
        Blk(boothDeco, booth, -0.51f, -0.39f, 0.39f, 0.51f, 0f, 0.95f, IsleKit.DarkWood);
        Piece askHut = P(booth, "Button Hold", -0.39f, 0.45f, 0.9f, 90f).Named("Ask Hut");
        Piece askLookout = P(booth, "Button Hold", booth.Hx, 0.45f, 0.9f, -90f).Named("Ask Lookout");
        askLookout.Under(four);
        Blk(boothDeco, booth, -0.1f, 1.3f, booth.Hz - 0.03f, booth.Hz - 0.01f, 1.62f, 1.82f, HutBlue);     // this chair hears blue
        MicCollar(boothDeco, booth, -0.2f, -0.35f, 1.25f, IsleKit.Red);
        b.MeshPiece("Booth Inside", boothDeco.ToMesh(), bare, false);
        var aerial = new MeshBuilder();      // theme: a small aerial on the roof
        Blk(aerial, booth, 1.0f, 1.07f, 0.6f, 0.67f, H + 0.2f, H + 1.9f, IsleKit.DarkWood);
        Blk(aerial, booth, 0.6f, 1.47f, 0.6f, 0.67f, H + 1.3f, H + 1.35f, IsleKit.DarkWood);
        Blk(aerial, booth, 0.75f, 1.32f, 0.6f, 0.67f, H + 1.6f, H + 1.65f, IsleKit.DarkWood);
        b.MeshPiece("Booth Aerial", aerial.ToMesh(), snowy, false);

        // ---- Lookout Cabin, on the far lip (4 players only, structures and all): Display B', intercom to the Booth chair ----
        Room c = BuildRoom(cabin, four, null, -0.6f);
        Piece displayB1 = P(cabin, "Display", 0.6f, -cabin.Hz + 0.13f, 1.9f, 0f).Named("Display B'").Set("PuzzleDisplay", "size", 0);   // One
        displayB1.Under(four);
        Piece lookoutMic = P(cabin, "Intercom Mic", 0.6f, -cabin.Hz + 0.04f, 1.25f, 0f).Named("Lookout Mic");
        Sdk.Add(lookoutMic.Component("PuzzleIntercom"), "speakers", boothChair.Component("PuzzleListeningChair"));
        lookoutMic.Under(four);
        Piece cabinAsk = P(cabin, "Light 8", cabin.Hx - 0.12f, 0.2f, 1.9f, -90f).Named("Lookout Ask Lamp").Set("PuzzleLight", "color", Amber);
        cabinAsk.Under(four);
        Piece cabinBar = P(cabin, "Progress Bar", 0.6f, cabin.Hz - 0.07f, 1.9f, 180f).Named("Lookout Time Bar").Scaled(0.35f, 0.35f, 1f);
        cabinBar.Under(four);
        var cabinDeco = new MeshBuilder();
        Blk(cabinDeco, cabin, 0.32f, 0.88f, -cabin.Hz + 0.005f, -cabin.Hz + 0.02f, 1.02f, 1.48f, IsleKit.Green);      // behind the intercom
        b.MeshPiece("Lookout Inside", cabinDeco.ToMesh(), bare, false).Under(four);

        // ---- Signal Box, on the flat (the root): tiles, the peg board, the chair that hears the Booth, the clocks ----
        BuildSignalBox(box);
        // its only way in: a two-way No Items turnstile in the south wall (the model spans x -1.11..1.01 about its pivot,
        // so the pivot at x +0.05 fills the opening cut to x -1.06..1.06)
        P(box, "Gate No items", 0.05f, 0f, 0f, 0f).Named("Box Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        // tiles: Scribbles (index 3 of Shapes, Peg, Emotes, Scribbles, Custom), images 1..10
        for (int i = 0; i < 6; i++)
            P(box, "Peg Tile", -3.25f, 0.7f + 0.6f * i, 0.9f + 0.24f, 90f).Named("Tile " + (i + 1)).Set("PuzzlePegTile", "imageSet", 3).Set("PuzzlePegTile", "image", i + 1);
        for (int i = 0; i < 2; i++)
            P(box, "Peg Tile", 3.25f, 0.7f + 0.6f * i, 0.9f + 0.24f, -90f).Named("Tile " + (7 + i)).Set("PuzzlePegTile", "imageSet", 3).Set("PuzzlePegTile", "image", 7 + i);
        for (int i = 0; i < 2; i++)
            P(box, "Peg Tile", -3.55f, 1.6f + 1.0f * i, 1.56f + 0.24f, 90f).Named("Tile " + (9 + i)).Set("PuzzlePegTile", "imageSet", 3).Set("PuzzlePegTile", "image", 9 + i);

        Piece validator = P(box, "Validator", 1.7f, 4.28f, 1.75f, 180f).Named("Validator");
        Piece check = P(box, "Button Poke", 1.7f, 4.28f, 0.95f, 180f).Named("Check Button");
        Sdk.Set(validator.Component("PuzzleValidator"), "pokeButton", check.Component("PuzzlePokeButton"));
        Component dA = displayA.Component("PuzzleDisplay"), dB = displayB.Component("PuzzleDisplay"), dB1 = displayB1.Component("PuzzleDisplay");
        // 2-3 players: five slots, blue 1-3 from the Hut, red 4-5 from the Booth (the pivot is slot 1's end; it runs to +x)
        Piece board5 = P(box, "Peg Board", -1.4f, 4.28f, 1.3f, 180f).Named("Board 5").Set("PuzzlePegBoard", "size", 4);
        Component b5 = board5.Component("PuzzlePegBoard");
        Sdk.Set(b5, "validator", validator.Component("PuzzleValidator"));
        Sdk.Add(b5, "answerFrom", dA, dA, dA, dB, dB);
        Sdk.AddInts(b5, "answerFromFlap", 1, 2, 3, 1, 2);
        board5.Under(boards23);
        // 4 players: six slots, slot 6 green from the Lookout
        Piece board6 = P(box, "Peg Board", -1.6f, 4.28f, 1.3f, 180f).Named("Board 6").Set("PuzzlePegBoard", "size", 5);
        Component b6 = board6.Component("PuzzlePegBoard");
        Sdk.Set(b6, "validator", validator.Component("PuzzleValidator"));
        Sdk.Add(b6, "answerFrom", dA, dA, dA, dB, dB, dB1);
        Sdk.AddInts(b6, "answerFromFlap", 1, 2, 3, 1, 2, 1);
        board6.Under(four);
        // the board wall's colour bands behind the slots, one set per board (slot pitch about 0.447 m)
        var bands5 = new MeshBuilder();
        Band(bands5, box, -1.4f, 0, 3, HutBlue); Band(bands5, box, -1.4f, 3, 5, IsleKit.Red);
        b.MeshPiece("Board 5 Bands", bands5.ToMesh(), bare, false).Under(boards23);
        var bands6 = new MeshBuilder();
        Band(bands6, box, -1.6f, 0, 3, HutBlue); Band(bands6, box, -1.6f, 3, 5, IsleKit.Red); Band(bands6, box, -1.6f, 5, 6, IsleKit.Green);
        b.MeshPiece("Board 6 Bands", bands6.ToMesh(), bare, false).Under(four);

        Piece checkClock = P(box, "Timer", 2.4f, 4.3f, 1.6f, 180f).Named("Check Clock").Set("PuzzleTimer", "duration", 8f).Scaled(0.5f * PuzzleBuilder.TimerScale, 0.5f * PuzzleBuilder.TimerScale, 0.5f * PuzzleBuilder.TimerScale);
        Piece boxBar = P(box, "Progress Bar", -2.9f, 4.33f, 2.4f, 180f).Named("Box Time Bar").Scaled(0.35f, 0.35f, 1f);
        Piece stationClock = P(box, "Timer", -2.9f, 4.3f, 1.9f, 180f).Named("Station Clock").Set("PuzzleTimer", "duration", 300f);
        Sdk.Add(stationClock.Component("PuzzleTimer"), "progressBars", boxBar.Component("PuzzleProgressBar"), hutBar.Component("PuzzleProgressBar"),
            boothBar.Component("PuzzleProgressBar"), cabinBar.Component("PuzzleProgressBar"));
        P(box, "Live Light", 2.4f, 4.33f, 2.2f, 180f).Named("Box Live");
        Piece boxLamp = P(box, "Light 2", 0f, 2.2f, 2.9f, 0f).Named("Box Lamp");
        // the chair in the north-east corner, facing west to the board (turned -90 its back reaches x +1.19: the east wall)
        Piece boxChair = P(box, "Listening Chair", 2.55f, 3.0f, 0f, -90f).Named("Box Chair");
        Sdk.Set(boxChair.Component("PuzzleListeningChair"), "microphone", boothMic.Component("PuzzleDirectionalMic"));
        Piece askBooth = P(box, "Button Hold", 3.8f, 1.65f, 0.9f, -90f).Named("Ask Booth");
        var boxDeco = new MeshBuilder();
        Blk(boxDeco, box, 3.78f, 3.8f, 1.95f, 3.3f, 1.62f, 1.82f, IsleKit.Red);      // this chair hears red
        b.MeshPiece("Box Plaque", boxDeco.ToMesh(), bare, false);
        Piece boxZone = P(box, "Radio Dead Zone", 0f, 2.25f, 1.5f, 0f).Named("Box Dead Zone");
        SizeCollider(boxZone, new Vector3(14f, 8f, 12f));
        E(box, Hush(9f, 5f, 6f), 0f, 2.25f, 1.5f);
        E(box, "Room Light 1 FFD9A8 6.5 1.2", 0f, 2.25f, 2.6f);

        // ---- the gourd: outside the shed, since an item cannot pass the turnstile ----
        IsleKit.StartCorner(b, 0f, -5f, 0f);

        // ---- the gorge: a dead zone (theme only: radios dropped down there show "dead zone") ----
        Vector2 mid = (hut.At + booth.At + cabin.At) / 3f;
        Piece ravineZone = b.Part("Radio Dead Zone", mid.x, mid.y, hut.Top + 8f, GorgeYaw, false).Named("Ravine Dead Zone");
        SizeCollider(ravineZone, new Vector3(46f, 22f, 48f));

        // ---- Calm Air: no jetpack thrust anywhere over the station (an oriented box from the Box to the Cabin) ----
        CalmAirOver(new[] { box, hut, booth, cabin });

        // ---- lanterns from the Box to the Booth, round it and down to the Hut; across the gorge to the Cabin (4p) ----
        Vector2 boothFront = booth.L(booth.GateX, booth.GateZ - 1.8f), hutFront = hut.L(hut.GateX, hut.GateZ - 1.8f);
        Lanterns("Path Lanterns", new List<Vector2> { new Vector2(-5.5f, -2.5f), boothFront, booth.L(-3.9f, booth.GateZ - 1.5f), booth.L(-3.9f, booth.Hz + 2.2f), hutFront }, 1, null);
        Lanterns("Lookout Lanterns", new List<Vector2> { hut.L(hut.Hx + 2.4f, hut.GateZ - 1.5f), hut.L(hut.Hx + 2.4f, hut.Hz + 2.4f),
            cabin.L(cabin.DoorX - 3.3f, cabin.Hz + 2.4f), cabin.L(cabin.DoorX - 3.3f, cabin.GateZ - 1.5f), cabin.L(cabin.GateX, cabin.GateZ - 1.8f) }, 2, four);
        // Christmas lights round the Signal Box, open in front of its turnstile
        Kit.LightString(b, "Box Lights", new List<Vector3>
        {
            new Vector3(2.6f, 2.6f, -2.6f), new Vector3(6.6f, 2.6f, -2.6f), new Vector3(6.6f, 2.6f, 7.2f), new Vector3(-6.6f, 2.6f, 7.2f),
            new Vector3(-6.6f, 2.6f, -2.6f), new Vector3(-2.6f, 2.6f, -2.6f),
        }, 0.4f, 0.5f, true, true, 122);

        // ---- events ----
        Component sdk = b.Puzzle;
        Component clockT = stationClock.Component("PuzzleTimer"), checkT = checkClock.Component("PuzzleTimer");
        Piece rootP = b.RootPiece;
        rootP.On("PuzzleSDK", "onPuzzleStart", clockT, "StartTimer");
        rootP.On("PuzzleSDK", "onPuzzleStart", checkT, "ResetTimer");      // a check pressed before the start must not end this round
        rootP.On("PuzzleSDK", "onPuzzleStop", clockT, "ResetTimer");
        rootP.On("PuzzleSDK", "onPuzzleStop", checkT, "ResetTimer");
        foreach (Piece lamp in new[] { h.Lamp, r.Lamp, boxLamp, c.Lamp })
        {
            rootP.On("PuzzleSDK", "onPuzzleStart", lamp.Component("PuzzleLight"), "TurnOn");
            rootP.On("PuzzleSDK", "onPuzzleStop", lamp.Component("PuzzleLight"), "TurnOff");
        }
        rootP.On("PuzzleSDK", "onWin", sdk, "StopPuzzle");                 // first win: every door opens at once
        stationClock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
        validator.On("PuzzleValidator", "onIncorrect", sdk, "StopPuzzle");
        check.On("PuzzlePokeButton", "onPressed", checkT, "StartTimer");
        checkClock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");     // any check ends the round 8 s later (a replayed win)
        foreach (Room room in new[] { h, r, c })
            room.Sensor.On("PuzzlePresenceSensor", "onPlayerEntered", sdk, "StopPuzzle");
        Wire(askBooth, boothAsk);
        Wire(askHut, hutAsk);
        Wire(askLookout, cabinAsk);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A sealed room: its pad (and steps up to it), the soundproof shell (walls, roof, floor, boards lapping the door's
    // frame), the roofed vestibule with its turnstile, the Door, the exit sensor, the pendant lamp, the start switch and
    // live light on the back wall, a room light, and a Radio Hush box. 'group' holds everything (the Cabin's lobby);
    // 'seal' holds just the door and the exit sensor (the Booth's, hidden in 2-player lobbies).
    Room BuildRoom(Post p, Piece group, Piece seal, float switchX)
    {
        float hx = p.Hx, hz = p.Hz, dx = p.DoorX, gz = p.GateZ;
        float vx0 = dx - 1.35f, vx1 = dx + 1.35f;
        var made = new List<Piece>();

        // the pad: level with the highest ground under it, sunk 1.5 m under the lowest
        p.PadX0 = Mathf.Min(-hx - T, vx0) - 0.6f; p.PadX1 = Mathf.Max(hx + T, vx1) + 0.6f;
        p.PadZ0 = gz - 1.5f; p.PadZ1 = hz + T + 0.6f;
        float high = float.MinValue, low = float.MaxValue;
        for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++)
            {
                Vector2 q = p.L(Mathf.Lerp(p.PadX0, p.PadX1, i / 2f), Mathf.Lerp(p.PadZ0, p.PadZ1, j / 2f));
                float g = GroundRel(q.x, q.y);
                high = Mathf.Max(high, g); low = Mathf.Min(low, g);
            }
        p.Top = high + 0.12f;
        var pad = new MeshBuilder();
        Blk(pad, p, p.PadX0, p.PadX1, p.PadZ0, p.PadZ1, low - p.Top - 1.5f, 0f, IsleKit.Stone);
        // steps up to the pad in front of the turnstile, where the ground outside is lower
        Vector2 outside = p.L(p.GateX, p.PadZ0 - 0.6f);
        float drop = p.Top - GroundRel(outside.x, outside.y);
        if (drop > 0.2f)
        {
            int n = Mathf.Min(4, Mathf.CeilToInt(drop / 0.3f));
            for (int s = 1; s <= n; s++)
                Blk(pad, p, p.GateX - 1.3f, p.GateX + 1.3f, p.PadZ0 - 0.6f * (n - s + 1), p.PadZ0, -drop - 0.6f, -drop + drop * s / (n + 1f), IsleKit.Stone);
        }
        made.Add(b.MeshPiece(p.Name + " Pad", pad.ToMesh(), snowy));

        // the soundproof shell (Wood): only walls, roof and floor under it
        Piece shell = E(p, p.Name + " Shell", 0f, 0f, 0f);
        Sdk.Set(b.AddScript(shell, "PuzzleSoundProof"), "material", 0);
        made.Add(shell);
        float o0 = dx - 0.62f, o1 = dx + 0.62f;      // the door's opening, cut to the Door (x +-0.62, 2.02 high)
        var walls = new MeshBuilder();
        Blk(walls, p, -hx - T, hx + T, hz, hz + T, 0f, H, p.Wall);                  // back
        Blk(walls, p, -hx - T, -hx, -hz, hz, 0f, H, p.Wall);                         // sides
        Blk(walls, p, hx, hx + T, -hz, hz, 0f, H, p.Wall);
        Blk(walls, p, -hx - T, o0, -hz - T, -hz, 0f, H, p.Wall);                     // front, either side of the door
        Blk(walls, p, o1, hx + T, -hz - T, -hz, 0f, H, p.Wall);
        Blk(walls, p, o0, o1, -hz - T, -hz, 2.02f, H, p.Wall);                       // and closed above it to the roof
        Blk(walls, p, -hx - T - 0.15f, hx + T + 0.15f, -hz - T - 0.15f, hz + T + 0.15f, H, H + 0.2f, IsleKit.DarkWood);     // flat roof
        // soundproof boards on the inside face, lapping the door's frame by 0.1 m (the Door blocks voices only with its leaves)
        Blk(walls, p, o0 - 0.15f, o0 + 0.1f, -hz + 0.025f, -hz + 0.065f, 0f, 2.12f, p.Trim);
        Blk(walls, p, o1 - 0.1f, o1 + 0.15f, -hz + 0.025f, -hz + 0.065f, 0f, 2.12f, p.Trim);
        Blk(walls, p, o0 - 0.15f, o1 + 0.15f, -hz + 0.025f, -hz + 0.075f, 1.92f, 2.17f, p.Trim);
        b.MeshPiece(p.Name + " Shell Walls", walls.ToMesh(), snowy).Under(shell);
        var floor = new MeshBuilder();
        Blk(floor, p, -hx, hx, -hz, hz, 0f, 0.02f, FloorBoards);
        b.MeshPiece(p.Name + " Shell Floor", floor.ToMesh(), bare).Under(shell);

        // the vestibule (ordinary walls, outside the shell): 2.3 m wide inside, roofed, the turnstile in its outer wall
        var vest = new MeshBuilder();
        Blk(vest, p, vx0, dx - 1.15f, gz + 0.1f, -hz - 0.02f, 0f, 2.45f, p.Wall);
        Blk(vest, p, dx + 1.15f, vx1, gz + 0.1f, -hz - 0.02f, 0f, 2.45f, p.Wall);
        float g0 = dx - 1.06f, g1 = dx + 1.06f;      // the opening, cut exactly to the gate
        Blk(vest, p, vx0, g0, gz - 0.1f, gz + 0.1f, 0f, 2.45f, p.Wall);
        Blk(vest, p, g1, vx1, gz - 0.1f, gz + 0.1f, 0f, 2.45f, p.Wall);
        Blk(vest, p, g0, g1, gz - 0.1f, gz + 0.1f, 2.30f, 2.45f, p.Wall);
        Blk(vest, p, vx0 - 0.1f, vx1 + 0.1f, gz - 0.25f, -hz - T, 2.45f, 2.6f, IsleKit.DarkWood);
        made.Add(b.MeshPiece(p.Name + " Vestibule", vest.ToMesh(), snowy));

        var room = new Room();
        room.Door = P(p, "Door", dx, p.DoorZ, 0f, 180f).Named(p.Name + " Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        room.Gate = P(p, "Gate No items", p.GateX, gz, 0f, 180f).Named(p.Name + " Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        made.Add(room.Gate);
        // the exit sensor: the vestibule's floor from 0.1 m outside the door into the gate's inner half, full width
        Vector2 sc = p.L(dx, p.DoorZ - 1.15f);
        room.Sensor = IsleKit.Sensor(b, p.Name + " Exit Sensor", sc.x, sc.y, new Vector3(2.3f, 2.6f, 1.9f), p.Top, false);
        room.Sensor.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, p.Yaw, 0f);
        room.Sensor.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        room.Lamp = P(p, "Light 2", 0f, 0f, H - 0.05f, 0f).Named(p.Name + " Lamp");
        made.Add(room.Lamp);
        made.Add(P(p, "Switch Start", switchX, hz - 0.08f, 1.3f, 180f).Named(p.Name + " Switch"));
        made.Add(P(p, "Live Light", switchX + 0.4f, hz - 0.06f, 1.45f, 180f).Named(p.Name + " Live"));
        made.Add(E(p, "Room Light 1 FFD9A8 3.5 0.8", 0f, 0f, 2.2f));
        // Radio Hush (Winter Walk silences walkies and megaphones inside by position): the room and its vestibule
        float rx0 = Mathf.Min(-hx - T, vx0), rx1 = hx + T, rz0 = gz - 0.1f, rz1 = hz + T;
        made.Add(E(p, Hush(rx1 - rx0 + 0.6f, 4f, rz1 - rz0 + 0.6f), (rx0 + rx1) / 2f, (rz0 + rz1) / 2f, 1.5f));

        Piece holder = seal ?? group;
        if (holder != null) { room.Door.Under(holder); room.Sensor.Under(holder); }
        if (group != null) foreach (Piece piece in made) piece.Under(group);
        return room;
    }

    // The Signal Box: 8 x 4.8 m of wood with a pitched roof, its only way in the turnstile in the south wall, cut
    // exactly to it; tables along the west and east walls, a shelf over the west one; walls to the roof everywhere
    void BuildSignalBox(Post p)
    {
        var pad = new MeshBuilder();
        Blk(pad, p, -4.6f, 4.6f, -1.3f, 5.3f, -1.75f, 0f, IsleKit.Stone);
        b.MeshPiece("Box Pad", pad.ToMesh(), snowy);

        const float ridgeZ = 2.25f, ridgeY = 3.95f, wallY = 2.85f;
        float slope = (ridgeY - wallY) / (ridgeZ + 0.1f);
        var mb = new MeshBuilder();
        Blk(mb, p, -4.0f, -3.8f, -0.1f, 4.6f, 0f, wallY, BoxWood);           // west, east
        Blk(mb, p, 3.8f, 4.0f, -0.1f, 4.6f, 0f, wallY, BoxWood);
        Blk(mb, p, -3.8f, 3.8f, 4.4f, 4.6f, 0f, 2.95f, BoxWood);             // north (its top inside the roof slab)
        Blk(mb, p, -3.8f, -1.06f, -0.1f, 0.1f, 0f, 2.95f, BoxWood);          // south, either side of the turnstile
        Blk(mb, p, 1.06f, 3.8f, -0.1f, 0.1f, 0f, 2.95f, BoxWood);
        Blk(mb, p, -1.06f, 1.06f, -0.1f, 0.1f, 2.30f, 2.95f, BoxWood);       // solid above the gate
        foreach (float x in new[] { -4.0f, -3.8f, 3.8f, 4.0f })              // gables, both faces of each
        {
            Vector3 a = V(p, x, wallY, -0.1f), top = V(p, x, ridgeY, ridgeZ), c = V(p, x, wallY, 4.6f);
            mb.Tri(a, top, c, BoxWood); mb.Tri(a, c, top, BoxWood);
        }
        float eaveY = ridgeY - slope * (ridgeZ + 0.7f);
        Slab(mb, new[] { V(p, -4.3f, eaveY, -0.7f), V(p, 4.3f, eaveY, -0.7f), V(p, 4.3f, ridgeY, ridgeZ), V(p, -4.3f, ridgeY, ridgeZ) }, 0.18f, IsleKit.DarkWood);
        Slab(mb, new[] { V(p, -4.3f, ridgeY, ridgeZ), V(p, 4.3f, ridgeY, ridgeZ), V(p, 4.3f, eaveY, 2f * ridgeZ + 0.7f), V(p, -4.3f, eaveY, 2f * ridgeZ + 0.7f) }, 0.18f, IsleKit.DarkWood);
        Blk(mb, p, -4.35f, 4.35f, ridgeZ - 0.15f, ridgeZ + 0.15f, ridgeY + 0.1f, ridgeY + 0.26f, IsleKit.DarkWood);
        b.MeshPiece("Signal Box", mb.ToMesh(), snowy);

        var inside = new MeshBuilder();
        Blk(inside, p, -3.8f, 3.8f, 0.1f, 4.4f, 0f, 0.02f, FloorBoards);
        Blk(inside, p, -3.78f, -2.88f, 0.4f, 4.0f, 0f, 0.9f, IsleKit.Wood);     // west table
        Blk(inside, p, 2.88f, 3.78f, 0.35f, 1.55f, 0f, 0.9f, IsleKit.Wood);     // east table
        Blk(inside, p, -3.8f, -3.48f, 1.2f, 3.0f, 1.5f, 1.56f, IsleKit.DarkWood);   // shelf over the west table
        b.MeshPiece("Signal Box Inside", inside.ToMesh(), bare);
    }

    // ---------------------------------------------------------------------------------------------------------------
    Piece P(Post p, string prefab, float x, float z, float h, float yaw)
    {
        Vector2 q = p.L(x, z);
        return b.Part(prefab, q.x, q.y, p.Top + h, p.Yaw + yaw, false);
    }

    // An empty in a post's frame, turned with it
    Piece E(Post p, string name, float x, float z, float h)
    {
        Vector2 q = p.L(x, z);
        Piece e = b.Empty(name, q.x, q.y, p.Top + h, false);
        e.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, p.Yaw, 0f);
        return e;
    }

    // A point in a post's frame, in the root's local space (the root is unturned, so that is the puzzle's frame)
    static Vector3 V(Post p, float x, float h, float z)
    {
        Vector2 q = p.L(x, z);
        return new Vector3(q.x, p.Top + h, q.y);
    }

    // A block given by its extents in a post's frame
    static void Blk(MeshBuilder mb, Post p, float x0, float x1, float z0, float z1, float h0, float h1, Color color)
        => Kit.AddBox(mb, V(p, (x0 + x1) / 2f, h0, (z0 + z1) / 2f), new Vector3(x1 - x0, h1 - h0, z1 - z0), color, p.Yaw);

    // A slab whose underside has the four corners given (in AddBox's order: -x-z, +x-z, +x+z, -x+z), 'thick' deep
    static void Slab(MeshBuilder mb, Vector3[] low, float thick, Color color)
    {
        var high = new Vector3[4];
        for (int i = 0; i < 4; i++) high[i] = low[i] + Vector3.up * thick;
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }

    // A coloured band on the Box's north wall behind slots [from, to) of a peg board whose pivot (slot 1's end) is at x
    static void Band(MeshBuilder mb, Post p, float pivotX, int from, int to, Color color)
    {
        const float pitch = 0.447f, start = -0.218f;
        Blk(mb, p, pivotX + start + pitch * from, pivotX + start + pitch * to, 4.29f, 4.4f, 0.95f, 1.65f, color);
    }

    // A coloured collar round the top of a hanging studio mic (the colour of the room that hears it)
    static void MicCollar(MeshBuilder mb, Post p, float x, float z, float h, Color color)
        => Blk(mb, p, x - 0.1f, x + 0.1f, z - 0.1f, z + 0.1f, h + 0.3f, h + 0.42f, color);

    // The ground's height above the root's level at puzzle-local (x, z)
    float GroundRel(float x, float z)
    {
        Vector3 w = b.Root.transform.TransformPoint(new Vector3(x, 0f, z));
        if (!b.Ground.At(w.x, w.z, out float y))
            throw new System.Exception("RadioSilence: nothing to stand on at local (" + x + ", " + z + ")");
        return y - b.Root.transform.position.y;
    }

    static string Hush(float sx, float sy, float sz) => string.Format(CultureInfo.InvariantCulture, "Radio Hush {0:0.##} {1:0.##} {2:0.##}", sx, sy, sz);

    static void SizeCollider(Piece piece, Vector3 size)
    {
        BoxCollider box = piece.Go.GetComponent<BoxCollider>();
        if (box == null) box = piece.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
    }

    // A hold button that lights a lamp while held
    static void Wire(Piece button, Piece lamp)
    {
        button.On("PuzzleButton", "onPressed", lamp.Component("PuzzleLight"), "TurnOn");
        button.On("PuzzleButton", "onReleased", lamp.Component("PuzzleLight"), "TurnOff");
    }

    // Christmas lights round a ravine post's pad, on the ground just outside it, open in front of its turnstile
    void LightLoop(Post p, string name, int seed)
    {
        float x0 = p.PadX0 - 0.4f, x1 = p.PadX1 + 0.4f, z0 = p.PadZ0 - 0.4f, z1 = p.PadZ1 + 0.4f;
        var posts = new List<Vector3>();
        foreach (Vector2 q in new[] { p.L(p.GateX + 1.8f, z0), p.L(x1, z0), p.L(x1, z1), p.L(x0, z1), p.L(x0, z0), p.L(p.GateX - 1.8f, z0) })
            posts.Add(new Vector3(q.x, 2.4f, q.y));
        Kit.LightString(b, name, posts, 0.35f, 0.5f, true, true, seed);
    }

    // Lanterns along a path (puzzle-local points), one about every 8 m, standing 1.3 m to the left of the way
    void Lanterns(string name, List<Vector2> path, int seed, Piece group)
    {
        var iron = new MeshBuilder();
        var glow = new MeshBuilder();
        const float spacing = 8f;
        float next = spacing / 2f, walked = 0f;
        int n = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Vector2 a = path[i], d = path[i + 1] - a;
            float length = d.magnitude;
            if (length < 0.01f) continue;
            Vector2 along = d / length, left = new Vector2(-along.y, along.x);
            while (next <= walked + length)
            {
                Vector2 q = a + along * (next - walked) + left * 1.3f;
                Kit.AddLantern(iron, glow, new Vector3(q.x, GroundRel(q.x, q.y), q.y), seed * 100 + n++);
                next += spacing;
            }
            walked += length;
        }
        Piece posts = b.MeshPiece(name, iron.ToMesh(), snowy);
        Piece lamps = b.MeshPiece(name + " Glow", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        if (group != null) { posts.Under(group); lamps.Under(group); }
    }

    // One Calm Air box over the whole station: laid along the line from the Box to the Cabin, wide enough for every
    // post's pad with a margin, from 15 m under the gorge floor to 160 m up
    void CalmAirOver(Post[] posts)
    {
        Vector2 far = posts[posts.Length - 1].At;
        Vector2 dir = far.normalized, across = new Vector2(dir.y, -dir.x);
        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        float a0 = float.MaxValue, a1 = float.MinValue, c0 = float.MaxValue, c1 = float.MinValue, lowest = 0f;
        foreach (Post p in posts)
        {
            float r = p == posts[0] ? 12f : 9f;      // the Box with its start corner and lights; a room with its pad
            for (int i = 0; i < 4; i++)
            {
                Vector2 q = p.At + new Vector2(i % 2 == 0 ? -r : r, i < 2 ? -r : r);
                float a = Vector2.Dot(q, dir), c = Vector2.Dot(q, across);
                a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); c0 = Mathf.Min(c0, c); c1 = Mathf.Max(c1, c);
            }
            lowest = Mathf.Min(lowest, p.Top);
        }
        Vector2 centre = dir * (a0 + a1) / 2f + across * (c0 + c1) / 2f;
        IsleKit.CalmAir(b, "Radio Silence", centre.x, centre.y, new Vector3(c1 - c0, 160f, a1 - a0), yaw, lowest - 15f, false);
    }
}
