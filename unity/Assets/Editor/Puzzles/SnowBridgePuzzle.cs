// Snow Bridge: the Survey Party (puzzle-specs/SnowBridge.md, revised after review). Two high banks of packed snow
// face each other across two gullies, with an iced stone pier between them; the gourd sits in its vice on the far
// bank. Twelve survey tiles of invisible ink lie in a jumble in a tray on the near bank: only a Lookout at the
// Invisible Ink telescope on the Survey Cairn sees their pictures. Two five-flap Displays are set low in the gully
// walls, facing away from the cairn: a Spotter reads them and describes the pictures, the Lookout finds each one in
// the tray, and a Builder (who sees no picture) hangs it on a Peg Board.
//
// Board A (near bank) right removes the Pier Key Stone that covers the pier's load-test button and lifts the
// Survey Hatch off Display B for the rest of the run; Board A going wrong brings the key stone back. Board B (on
// the pier) is judged by the load test: N-Hold buttons at the cairn, on the pier and down in Gully 2 (two buttons
// in a 2-player lobby), all held at once, run the Load Validator. Right wins and raises the far span (Span 2); wrong
// stops the puzzle, and the next start re-deals both Displays. With four players a Winchman holds the shutters off
// both Displays. Span 1 always stands and both banks have stairs, so everything is reachable on foot at all times.
// Two Start Switches (near bank, pier) move together.
//
// Coordinates are local metres, +z from the near bank towards the far bank; heights are from the root's level (the
// pad's top, the gully floors); Up = 3.2 is the top of the banks and the pier. Parts face their local +z; yaw 180
// faces -z, -90 faces -x. No custom code.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowBridgePuzzle : PuzzleDef
{
    public override string Name => "SnowBridge";
    public override Vector2 Footprint => new Vector2(30f, 38f);
    public override Vector2? FixedSite => new Vector2(-3003f, -2178f);     // layout-final: the crossing above the West Cliffs
    // Local +z points west (towards the cliff) and +x north: the gullies' open ends run north and south along the
    // shelf, not towards the cliff, and the near bank and its stairs face the West Cliffs station to the east.
    public override float Yaw => -90f;

    const float Up = 3.2f;                 // banks and pier top
    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);
    static readonly Color IcedStone = new Color(0.62f, 0.70f, 0.78f);
    static readonly Color Granite = new Color(0.42f, 0.43f, 0.46f);
    static readonly Color GraniteCap = new Color(0.36f, 0.37f, 0.40f);
    static readonly Color SpanDeck = new Color(0.88f, 0.92f, 0.98f);

    // The Tile Tray: floor centre (x, height, z) and its tilt (15 degrees, back edge higher, so a lying tile's face
    // turns towards the cairn at -z)
    static readonly Vector3 TrayCentre = new Vector3(-2.2f, Up + 0.95f, -9.6f);
    // Rotating about x by -15 takes the tray's local +z to (0, sin 15, cos 15): the back (+z) edge is higher, and the
    // floor's normal (local +y) becomes (0, cos 15, -sin 15), leaning towards -z, the cairn.
    static readonly Quaternion TrayTilt = Quaternion.Euler(-15f, 0f, 0f);

    // Tile n (1..12) shows Peg image TileImage[n - 1]: not ordered by place in the heap
    static readonly int[] TileImage = { 7, 2, 11, 5, 9, 1, 12, 4, 8, 3, 10, 6 };
    // Spin of each tile about its face normal, and two small tilts (degrees): random-looking, fixed
    static readonly float[] TileSpin = { 0f, 80f, 175f, 265f, 95f, 5f, 260f, 170f, 185f, 275f, 10f, 85f };
    static readonly Vector2[] TileTilt =
    {
        new Vector2(12f, -4f), new Vector2(-10f, 6f), new Vector2(14f, 8f), new Vector2(-12f, -6f),
        new Vector2(8f, 12f), new Vector2(-14f, 4f), new Vector2(10f, -10f), new Vector2(-6f, 13f),
        new Vector2(13f, 5f), new Vector2(-9f, -11f), new Vector2(6f, -14f), new Vector2(-13f, 7f),
    };

    PuzzleBuilder b;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(30f, 38f);
        b.DebugTeleport(10f, -15.7f);      // on the ground at the foot of the near stairs
        // Winter Walk's jetpacks do not fire over the whole crossing, well past the banks, stairs and pier
        IsleKit.CalmAir(b, "Snow Bridge", 1.5f, -3f, new Vector3(46f, 120f, 50f), 0f, -10f, false);

        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        var works = new MeshBuilder();
        BuildNearBank(works);
        BuildPier(works);
        BuildFarBank(works);
        b.MeshPiece("Snow Bridge Works", works.ToMesh(), snowy);

        Piece four = IsleKit.Lobby(b, "Four Party", false, false, true);
        Piece threeFour = IsleKit.Lobby(b, "Three Four Party", false, true, true);

        // ---- the toggled pieces, each its own mesh ----
        Span("First Span", -6.3f, -1.5f, snowy);                          // always stands: in no event
        Piece secondSpan = Span("Second Span", 1.5f, 6.3f, snowy);       // up while stopped and on the first win

        var keyMb = new MeshBuilder();
        // A carved keystone, a little wider at the top, against the East Parapet's -z face; it encloses the Load Test
        // Pier button's face and sides (button x 2.9..3.1, z 1.047..1.18, h Up+1.08..Up+1.28)
        Hexa(keyMb,
            new[] { new Vector3(2.6f, Up + 0.75f, 0.8f), new Vector3(3.4f, Up + 0.75f, 0.8f), new Vector3(3.4f, Up + 0.75f, 1.2f), new Vector3(2.6f, Up + 0.75f, 1.2f) },
            new[] { new Vector3(2.52f, Up + 1.55f, 0.8f), new Vector3(3.48f, Up + 1.55f, 0.8f), new Vector3(3.48f, Up + 1.55f, 1.2f), new Vector3(2.52f, Up + 1.55f, 1.2f) },
            Granite);
        Block(keyMb, 2.56f, 3.44f, Up + 1.55f, Up + 1.62f, 0.78f, 1.2f, GraniteCap);      // a cap stone
        Piece keyStone = b.MeshPiece("Pier Key Stone", keyMb.ToMesh(), bare);

        var hatchMb = new MeshBuilder();
        for (int i = 0; i < 5; i++)      // red and cream planks, flush on Frame B's front
            Block(hatchMb, -1.5f, 1.5f, 1.30f + i * 0.21f, 1.30f + (i + 1) * 0.21f, 2.40f, 2.48f, i % 2 == 0 ? IsleKit.Red : IsleKit.Cream);
        Block(hatchMb, -1.2f, -1.05f, 1.32f, 2.33f, 2.48f, 2.51f, IsleKit.DarkWood);      // two battens
        Block(hatchMb, 1.05f, 1.2f, 1.32f, 2.33f, 2.48f, 2.51f, IsleKit.DarkWood);
        Piece hatch = b.MeshPiece("Survey Hatch", hatchMb.ToMesh(), snowy);

        Piece shutterA = Shutter("Shutter A", -5.40f, snowy).Under(four);
        Piece shutterB = Shutter("Shutter B", 2.51f, snowy).Under(four);

        var winchMb = new MeshBuilder();
        Block(winchMb, 4.30f, 4.60f, Up, Up + 1.8f, -11.15f, -10.85f, IsleKit.Wood);              // the post
        Block(winchMb, 4.32f, 4.58f, Up + 1.35f, Up + 1.65f, -11.75f, -11.15f, IsleKit.DarkWood); // the drum
        Block(winchMb, 4.40f, 4.50f, Up + 1.45f, Up + 1.55f, -11.95f, -11.75f, IsleKit.Wood);     // crank arm
        Block(winchMb, 4.40f, 4.60f, Up + 1.25f, Up + 1.55f, -12.0f, -11.9f, IsleKit.Wood);       // crank handle
        b.MeshPiece("Winch Post", winchMb.ToMesh(), snowy).Under(four);

        for (int i = 0; i < 4; i++)      // soft drifts in the gullies, clear of the frames
        {
            float x = i % 2 == 0 ? -3.2f : 3.2f, z = i < 2 ? -3.9f : 3.9f;
            b.MeshPiece("Soft Snow " + (i + 1), Kit.Mound(1.8f, 0.5f, 2201 + i, 8, 20), b.ShaderMaterial("BWM/Snowy", "Drift"), true)
                .Transform.position = b.WorldPoint(x, z, 0f, false);
        }

        BuildParts(secondSpan, hatch, keyStone, shutterA, shutterB, four, threeFour);
        BuildTiles();
        BuildLights();
    }

    // ================================================================ geometry helpers

    // An axis-aligned block from its low and high corners (heights from the root's level)
    static void Block(MeshBuilder mb, float x0, float x1, float h0, float h1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, h0, (z0 + z1) / 2f), new Vector3(x1 - x0, h1 - h0, z1 - z0), c);

    // A six-sided solid from its four bottom and four top corners, both in the order (-x,-z), (+x,-z), (+x,+z),
    // (-x,+z) as Kit.AddBox lays them, wound the same way
    static void Hexa(MeshBuilder mb, Vector3[] low, Vector3[] high, Color c)
    {
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], c);
        mb.Tri(high[0], high[3], high[2], c); mb.Tri(high[0], high[2], high[1], c);
        mb.Tri(low[0], low[1], low[2], c); mb.Tri(low[0], low[2], low[3], c);
    }

    // A block given in a turned frame (origin, rotation; extents in that frame)
    static void OBlock(MeshBuilder mb, Vector3 origin, Quaternion q, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
    {
        Vector3 At(float x, float y, float z) => origin + q * new Vector3(x, y, z);
        Hexa(mb, new[] { At(x0, y0, z0), At(x1, y0, z0), At(x1, y0, z1), At(x0, y0, z1) },
                 new[] { At(x0, y1, z0), At(x1, y1, z0), At(x1, y1, z1), At(x0, y1, z1) }, c);
    }

    // A stone frame low in a wall face at z = face (the wall behind at smaller z), protruding 0.6 m: cheeks, a solid
    // sill, the backing block the Display hangs on (0.15 m behind the frame's front), and a lintel
    static void Frame(MeshBuilder mb, float face)
    {
        float front = face + 0.6f;
        Block(mb, -1.6f, -1.4f, 0f, 2.6f, face, front, IsleKit.Stone);
        Block(mb, 1.4f, 1.6f, 0f, 2.6f, face, front, IsleKit.Stone);
        Block(mb, -1.4f, 1.4f, 0f, 1.35f, face, front, IsleKit.Stone);              // solid sill
        Block(mb, -1.4f, 1.4f, 1.35f, 2.3f, face, front - 0.15f, IsleKit.Stone);    // backing block: the opening is 0.15 deep
        Block(mb, -1.6f, 1.6f, 2.3f, 2.6f, face, front, IsleKit.Stone);             // lintel
    }

    // ================================================================ structures

    void BuildNearBank(MeshBuilder mb)
    {
        // the pad: one level slab for the whole site, sunk 1.5 m; its top is the gully floors
        Block(mb, -15f, 15f, -1.5f, 0f, -19f, 19f, SnowBlock);
        // the near bank, sheer on every side (3.2 m: no jump reaches its top)
        Block(mb, -5f, 5f, 0f, Up, -17.5f, -6.0f, SnowBlock);
        // the near stairs on +x, from the ground up to the bank top (ten steps of 0.29 m)
        StairsUp(mb, 5f, 8.2f, -16.5f, -14.9f, 10);
        // the Survey Cairn (telescope on top) with four steps on its -z side
        Block(mb, -1.2f, 1.2f, Up, Up + 1.4f, -16.2f, -13.8f, IsleKit.Stone);
        for (int s = 0; s < 4; s++)
            Block(mb, -0.6f, 0.6f, Up, Up + 1.4f * (s + 1) / 5f, -16.2f - (4 - s) * 0.3f, -16.2f - (3 - s) * 0.3f, IsleKit.Stone);
        Block(mb, 1.5f, 1.7f, Up, Up + 1.7f, -15.1f, -14.9f, IsleKit.Wood);          // Cairn Post (Load Test Cairn)
        Block(mb, 2.87f, 3.13f, Up, Up + 1.5f, -16.9f, -16.7f, IsleKit.Wood);        // Start Post
        // the Tile Tray: a stone base whose top follows the tilted floor, the wooden floor, a low lip on its front and
        // sides, and a back board
        Vector3 F(float u, float v, float w) => TrayCentre + TrayTilt * new Vector3(u, v, w);
        Vector3 Down(Vector3 p) => new Vector3(p.x, Up, p.z);
        Vector3[] top = { F(-0.95f, -0.06f, -0.36f), F(0.95f, -0.06f, -0.36f), F(0.95f, -0.06f, 0.36f), F(-0.95f, -0.06f, 0.36f) };
        Hexa(mb, new[] { Down(top[0]), Down(top[1]), Down(top[2]), Down(top[3]) }, top, IsleKit.Stone);
        OBlock(mb, TrayCentre, TrayTilt, -1.0f, 1.0f, -0.06f, 0f, -0.415f, 0.415f, IsleKit.Wood);       // floor, top at Up+0.95 at its centre
        OBlock(mb, TrayCentre, TrayTilt, -1.0f, 1.0f, 0f, 0.06f, -0.415f, -0.375f, IsleKit.DarkWood);   // front lip
        OBlock(mb, TrayCentre, TrayTilt, -1.0f, -0.96f, 0f, 0.06f, -0.375f, 0.375f, IsleKit.DarkWood);  // side lips
        OBlock(mb, TrayCentre, TrayTilt, 0.96f, 1.0f, 0f, 0.06f, -0.375f, 0.375f, IsleKit.DarkWood);
        OBlock(mb, TrayCentre, TrayTilt, -1.0f, 1.0f, 0f, 0.3f, 0.375f, 0.415f, IsleKit.Wood);          // back board
        // Board A's parapet at the near lip, east of the span (Board A on its -z face)
        Block(mb, 1.2f, 4.6f, Up, Up + 1.8f, -6.6f, -6.3f, SnowBlock);
        // Frame A in the near bank's +z face, facing the pier
        Frame(mb, -6.0f);
        // the hidden validator's bank interior needs nothing more: it is inside the near bank's solid
    }

    void BuildPier(MeshBuilder mb)
    {
        Block(mb, -4f, 4f, 0f, Up, -1.8f, 1.8f, IcedStone);
        // the Pier Shelf, west side, with a 6 cm kerb round its top
        Block(mb, -3.4f, -1.4f, Up, Up + 0.8f, -1.5f, -0.9f, IsleKit.Stone);
        Block(mb, -3.4f, -1.4f, Up + 0.8f, Up + 0.86f, -1.5f, -1.46f, IsleKit.Stone);
        Block(mb, -3.4f, -1.4f, Up + 0.8f, Up + 0.86f, -0.94f, -0.9f, IsleKit.Stone);
        Block(mb, -3.4f, -3.36f, Up + 0.8f, Up + 0.86f, -1.46f, -0.94f, IsleKit.Stone);
        Block(mb, -1.44f, -1.4f, Up + 0.8f, Up + 0.86f, -1.46f, -0.94f, IsleKit.Stone);
        Block(mb, 3.47f, 3.73f, Up, Up + 1.5f, -1.3f, -1.1f, IsleKit.Wood);          // Pier Post (second start)
        Block(mb, -4.4f, -1.2f, Up, Up + 1.8f, 1.2f, 1.5f, SnowBlock);               // Board B's parapet
        Block(mb, 1.2f, 4.0f, Up, Up + 2.2f, 1.2f, 1.5f, SnowBlock);                 // East Parapet: validator, pier button
        // Frame B in the pier's +z face, facing Gully 2 and the far bank
        Frame(mb, 1.8f);
        // a small wooden plate behind the Load Test Gully button on the pier's far wall
        Block(mb, 2.82f, 3.18f, 0.97f, 1.33f, 1.8f, 1.85f, IsleKit.Wood);
    }

    void BuildFarBank(MeshBuilder mb)
    {
        Block(mb, -5f, 5f, 0f, Up, 6.0f, 13.0f, SnowBlock);
        StairsUp(mb, 5f, 8.2f, 9.0f, 10.6f, 10);                                       // the far bank is always reachable
        Block(mb, -0.55f, 0.55f, Up, Up + 0.45f, 9.45f, 10.55f, IsleKit.Stone);       // Gourd Stone
    }

    // Stone steps from the bank's side at x0 (highest) out to x1 (lowest), 'count' of them, between z0 and z1, each
    // solid to the ground. Rises are Up / (count + 1): 0.29 m for ten, the last step 0.29 below the bank top.
    static void StairsUp(MeshBuilder mb, float x0, float x1, float z0, float z1, int count)
    {
        float tread = (x1 - x0) / count;
        for (int s = 0; s < count; s++)
            Block(mb, x0 + s * tread, x0 + (s + 1) * tread, 0f, Up * (count - s) / (count + 1f), z0, z1, IsleKit.Stone);
    }

    // A span: a snow deck 1.8 m wide (x -0.9..0.9) with cream kerbs, its top at Up, overlapping bank and pier by 0.3 m
    Piece Span(string name, float z0, float z1, Material snowy)
    {
        var mb = new MeshBuilder();
        Block(mb, -0.9f, 0.9f, Up - 0.25f, Up, z0, z1, SpanDeck);
        Block(mb, -0.9f, -0.78f, Up, Up + 0.45f, z0, z1, IsleKit.Cream);
        Block(mb, 0.78f, 0.9f, Up, Up + 0.45f, z0, z1, IsleKit.Cream);
        return b.MeshPiece(name, mb.ToMesh(), snowy);
    }

    // A wooden shutter 3.0 x 1.05 x 0.08 m flush on a frame's front (z from 'z0'), overlapping sill, lintel and cheeks
    Piece Shutter(string name, float z0, Material snowy)
    {
        var mb = new MeshBuilder();
        for (int i = 0; i < 6; i++)
            Block(mb, -1.5f + i * 0.5f, -1.0f + i * 0.5f, 1.30f, 2.35f, z0, z0 + 0.08f, i % 2 == 0 ? IsleKit.Wood : IsleKit.DarkWood);
        return b.MeshPiece(name, mb.ToMesh(), snowy);
    }

    // ================================================================ parts and wiring

    Piece P(string prefab, float x, float z, float h, float yaw, string name, Piece group = null)
    {
        Piece p = b.Part(prefab, x, z, h, yaw, false).Named(name);
        if (group != null) p.Under(group);
        return p;
    }

    // A Validator with its own poke button cleared and switched off: it is checked only by what is wired to Check()
    Component Validator(float x, float z, float h, float yaw, string name)
    {
        Piece v = P("Validator", x, z, h, yaw, name);
        Component c = v.Component("PuzzleValidator");
        Sdk.Set(c, "pokeButton", (UnityEngine.Object)null);
        foreach (Transform t in v.Go.GetComponentsInChildren<Transform>(true))
            if (t != v.Transform && t.GetComponent(Sdk.T("PuzzlePokeButton")) != null) t.gameObject.SetActive(false);
        return c;
    }

    Piece Board(string name, float x, float z, Piece display, Component validator)
    {
        Piece board = P("Peg Board", x, z, Up + 1.3f, 180f, name)
            .Set("PuzzlePegBoard", "size", 4)                 // Slots5
            .Set("PuzzlePegBoard", "startMarker", true);
        Component c = board.Component("PuzzlePegBoard"), d = display.Component("PuzzleDisplay");
        Sdk.Add(c, "answerFrom", d, d, d, d, d);
        Sdk.AddInts(c, "answerFromFlap", 1, 2, 3, 4, 5);
        Sdk.Set(c, "validator", validator);
        return board;
    }

    void BuildParts(Piece secondSpan, Piece hatch, Piece keyStone, Piece shutterA, Piece shutterB, Piece four, Piece threeFour)
    {
        Component sdk = b.Puzzle;

        // ---- the two Displays, low in the gully walls, each on its frame's backing block ----
        Piece displayA = P("Display", 0f, -5.55f, 1.75f, 0f, "Display A").Set("PuzzleDisplay", "size", 3);      // Five
        Piece displayB = P("Display", 0f, 2.25f, 1.75f, 0f, "Display B").Set("PuzzleDisplay", "size", 3);

        // ---- two validators on purpose: with only one, the loader would link Board A to it too ----
        Component loadValidator = Validator(2.0f, 1.18f, Up + 1.9f, 180f, "Load Validator");
        Component hiddenValidator = Validator(0f, -12f, 1.0f, 0f, "Hidden Validator");      // inside the solid near bank; nothing calls its Check()
        Sdk.Wire(loadValidator, "onIncorrect", sdk, "StopPuzzle");      // a failed load test stops the puzzle; the next start re-deals

        // ---- the boards ----
        Piece boardA = Board("Board A", 1.8f, -6.62f, displayA, hiddenValidator);      // runs to x 3.82 (turned 180)
        Board("Board B", -4.0f, 1.18f, displayB, loadValidator);                      // runs to x -1.78
        boardA.On("PuzzlePegBoard", "onCorrect", keyStone.Go, "SetActive", false)
              .On("PuzzlePegBoard", "onCorrect", hatch.Go, "SetActive", false)
              .On("PuzzlePegBoard", "onIncorrect", keyStone.Go, "SetActive", true)
              .On("PuzzlePegBoard", "onMissingPegs", keyStone.Go, "SetActive", true);

        // ---- the Invisible Ink telescope on the cairn ----
        P("Telescope", 0f, -14.6f, Up + 1.8f, 0f, "Survey Telescope").Set("PuzzleTelescope", "invisibleInkView", true);

        // ---- the load test: cairn, pier (under the key stone) and Gully 2 (lobbies of 3 and 4) ----
        P("Button N Hold", 1.6f, -14.88f, Up + 1.15f, 0f, "Load Test Cairn").Set("PuzzleButtonNHold", "group", "load");
        Piece pierButton = P("Button N Hold", 3.0f, 1.18f, Up + 1.15f, 180f, "Load Test Pier").Set("PuzzleButtonNHold", "group", "load");
        P("Button N Hold", 3.0f, 1.85f, 1.15f, 0f, "Load Test Gully", threeFour).Set("PuzzleButtonNHold", "group", "load");
        pierButton.On("PuzzleButtonNHold", "onAllHeld", loadValidator, "Check");      // the only onAllHeld wiring

        // ---- the winch (four players): holds both shutters off the Displays while pressed ----
        P("Button Hold", 4.30f, -11.0f, Up + 1.2f, -90f, "Winch", four)
            .On("PuzzleButton", "onPressed", shutterA.Go, "SetActive", false)
            .On("PuzzleButton", "onPressed", shutterB.Go, "SetActive", false)
            .On("PuzzleButton", "onReleased", shutterA.Go, "SetActive", true)
            .On("PuzzleButton", "onReleased", shutterB.Go, "SetActive", true);

        // ---- two Start Switches with their Live Lights: near stairs' head and on the pier ----
        P("Switch Start", 3.0f, -16.8f + 0.173f, Up + 1.25f, 0f, "Switch Start");
        P("Live Light", 3.0f, -16.8f + 0.1f, Up + 1.42f, 0f, "Live Light");
        P("Switch Start", 3.6f, -1.2f - 0.173f, Up + 1.25f, 180f, "Switch Start Pier");
        P("Live Light", 3.6f, -1.2f - 0.1f, Up + 1.42f, 180f, "Live Light Pier");

        // ---- the gourd on the far bank ----
        P("Gourd Vice", 0f, 10.0f, Up + 0.45f, 180f, "Gourd Vice");
        P("Pillow", -1.6f, 10.0f, Up + 0.13f, 0f, "Pillow");
        P("Confetti", 1.6f, 10.0f, Up + 0.2f, 0f, "Confetti");

        // ---- the root's events ----
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", secondSpan.Go, "SetActive", false)
            .On("PuzzleSDK", "onPuzzleStart", hatch.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStart", keyStone.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStart", shutterA.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStart", shutterB.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStop", secondSpan.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStop", hatch.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStop", keyStone.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStop", shutterA.Go, "SetActive", true)
            .On("PuzzleSDK", "onPuzzleStop", shutterB.Go, "SetActive", true)
            .On("PuzzleSDK", "onWin", secondSpan.Go, "SetActive", true);
    }

    // The twelve survey tiles: three layers of four spawned above the tray, staggered, face up (the face is the
    // tile's local +z, the peg behind it), so they fall and settle into a jumble against the tray's lip
    void BuildTiles()
    {
        float[] layerX = { 0f, 0.1f, -0.1f }, layerZ = { 0f, 0.12f, -0.1f };
        float[] layerUp = { 0.3f, 0.75f, 1.2f };      // pivot above the tray floor, along its normal (a tile is 0.3 m deep)
        for (int i = 0; i < 12; i++)
        {
            int layer = i / 4, col = i % 4;
            float u = (col - 1.5f) * 0.53f + layerX[layer];
            float w = (col % 2 == 0 ? -0.06f : 0.06f) + layerZ[layer];
            Vector3 local = TrayCentre + TrayTilt * new Vector3(u, layerUp[layer], w);
            Piece tile = P("Peg Tile", local.x, local.z, local.y, 0f, "Survey Tile " + (i + 1));
            tile.Transform.position = b.Root.transform.TransformPoint(local);
            // face up in the tray's frame (local +z to the tray's normal), spun about the normal, tipped a little
            Quaternion q = TrayTilt * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, TileSpin[i]) * Quaternion.Euler(TileTilt[i].x, TileTilt[i].y, 0f);
            tile.Transform.rotation = b.Root.transform.rotation * q;
            tile.Set("PuzzlePegTile", "imageSet", 1)      // Peg (the game's set B), every Peg tile on the island invisible ink
                .Set("PuzzlePegTile", "image", TileImage[i])
                .Set("PuzzlePegTile", "invisibleInkView", true);
        }
    }

    // ================================================================ lights

    void BuildLights()
    {
        float h = Up + 1.0f;
        Kit.LightString(b, "Near Lip Lights", new List<Vector3> { new Vector3(-4.8f, h, -6.25f), new Vector3(-3.0f, h, -6.25f), new Vector3(-1.3f, h, -6.25f) }, 0.4f, 0.5f, false, true, 31);
        Kit.LightString(b, "Near Side Lights", new List<Vector3> { new Vector3(-4.8f, h, -17.2f), new Vector3(-4.8f, h, -13.5f), new Vector3(-4.8f, h, -9.8f), new Vector3(-4.8f, h, -6.25f) }, 0.4f, 0.5f, false, true, 32);
        Kit.LightString(b, "Pier West Lights", new List<Vector3> { new Vector3(-3.85f, h, -1.6f), new Vector3(-3.85f, h, -0.5f) }, 0.4f, 0.5f, false, true, 33);      // stops short of Board B's sightline
        Kit.LightString(b, "Pier East Lights", new List<Vector3> { new Vector3(3.85f, h, -1.6f), new Vector3(3.85f, h, 1.0f) }, 0.4f, 0.5f, false, true, 34);
        Kit.LightString(b, "Pier Near Lights", new List<Vector3> { new Vector3(1.2f, h, -1.65f), new Vector3(2.5f, h, -1.65f), new Vector3(3.85f, h, -1.65f) }, 0.4f, 0.5f, false, true, 35);
        Kit.LightString(b, "Far Lip West Lights", new List<Vector3> { new Vector3(-4.8f, h, 6.25f), new Vector3(-3.0f, h, 6.25f), new Vector3(-1.3f, h, 6.25f) }, 0.4f, 0.5f, false, true, 36);
        Kit.LightString(b, "Far Lip East Lights", new List<Vector3> { new Vector3(1.3f, h, 6.25f), new Vector3(3.0f, h, 6.25f), new Vector3(4.8f, h, 6.25f) }, 0.4f, 0.5f, false, true, 37);
        Kit.LightRing(b, "Site Lights", 0f, -2f, 28f, 34f, 2.6f, false, 21);
    }
}
