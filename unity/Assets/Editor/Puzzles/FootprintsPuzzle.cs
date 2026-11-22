// Footprints: the Signal Field. A fenced snowfield holds sixteen pads hidden under the snow, four rows by four
// columns, named only by stakes: row stakes along the side fences (red, green, gold, ice-blue, front to back) and
// column stakes along the front and back fences (short to tall, one to four white bands, left to right).
//
// The lamps that say which pad to stand on are on a board inside the Signal Hut: sealed, soundproof, windowless,
// entered through a roofed vestibule whose one opening is a No Items turnstile (and a radio dead zone over both).
// The Lookout's only way out for the voice is a Directional Mic beside the board, wired to the Listening Chair in the
// raised Signal Cab at the field's front edge. The Signalman in that chair hears only the hut and shouts the call
// through the cab's slatted window to the Walker in the field. Both rooms shut their doors while the puzzle runs.
//
// Timed Press In Order: each pad stays lit 9 s, and when the time runs out the Walker must be on that pad and on no
// other. Ten in a row win (the back board's bar fills, Win opens both doors); one miss fails the run, and any Start
// Switch off and on deals a new one.
//
// 4 players: a 2.8 m divider splits the field into two pens, one Walker each. 2 players: the hut is built without
// soundproofing or turnstile, and two walkie-talkies are supplied. No custom code.
//
// Coordinates: x right, z forward (towards the back of the field), heights above the ground; the cab's floor is
// 2.6 m up. Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z.
using Bwm;
using UnityEngine;

public sealed class FootprintsPuzzle : PuzzleDef
{
    public override string Name => "Footprints";
    public override Vector2 Footprint => new Vector2(24f, 40f);
    // A level shelf above a beach on the south-east coast (layout-final.json); the island's build levels and clears it
    public override Vector2? FixedSite => new Vector2(-798f, -1980f);

    const float CabFloor = 2.6f;          // the Signal Cab's floor, above the ground
    const int Rounds = 10;
    const float PhaseSeconds = 9f;        // tune 8 (harder) to 10 (easier) after playtests
    const float LampRange = 0.3f;         // the board's lamps' light reach (see where they are placed)
    const float BarScale = 0.6f;         // the bars inside the hut and the cab
    const float BarHalfDepth = 0.101f;    // a Progress Bar's back is 0.101 m behind its pivot (part-metrics)
    const float SwitchBack = 0.073f;      // a Switch Start's back is 0.073 m behind its pivot
    const float LiveBack = 0.006f;        // a Live Light's back is 0.006 m behind its pivot

    static readonly float[] PadX = { -6.3f, -2.1f, 2.1f, 6.3f };      // columns 1..4, left to right
    static readonly float[] PadZ = { 1.0f, 5.2f, 9.4f, 13.6f };       // rows 1..4, front (nearest the cab) to back
    static readonly float[] LampX = { -1.25f, -0.75f, -0.25f, 0.25f };
    static readonly float[] LampH = { 0.95f, 1.35f, 1.75f, 2.15f };

    // Field fence: x -9.2..9.2, z -2.2..16.6; front gates 1.6 m wide centred at x = -4.2 and 4.2
    const float FenceX = 9.2f, FenceZ0 = -2.2f, FenceZ1 = 16.6f, FenceH = 1.8f, GateX = 4.2f, GateHalf = 0.8f;

    public override void Build(PuzzleBuilder b)
    {
        b.ClearGround(24f, 40f);
        b.DebugTeleport(3f, -11f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Color[] rowColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice };

        // Jetpacks do not fire anywhere over the puzzle: no hopping the divider or the fences, nor onto the hut's roof
        IsleKit.CalmAir(b, "Footprints", 0f, 0f, new Vector3(44f, 150f, 60f), 0f, -10f);

        // ================= the field =================
        var pads = new UnityEngine.Object[16];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
            {
                Piece pad = IsleKit.Sensor(b, "Pad " + (r + 1) + "-" + (c + 1), PadX[c], PadZ[r], new Vector3(2.0f, 2.6f, 2.0f))
                    .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
                pads[r * 4 + c] = pad.Component("PuzzlePresenceSensor");
            }
        BuildFence(b, snowy, rowColour);
        BuildStakes(b, snowy, rowColour);

        // the back board, behind the back fence, facing the field: the count and the phase
        IsleKit.BoardOnPosts(b, "Back Board", 0f, 18f, 180f, 3.8f, 1.9f, 3.2f, IsleKit.Board);
        Piece barBack = b.Part("Progress Bar", 0f, 17.87f, 2.85f, 180f).Named("Bar Back").Set("PuzzleProgressBar", "total", Rounds);
        Piece phaseBack = b.Part("Progress Bar", 0f, 17.87f, 2.30f, 180f).Named("Phase Back").Set("PuzzleProgressBar", "total", 9);

        // the field corner's start switch, the gourd's vice, the pillow and the confetti
        IsleKit.StartCorner(b, 6.5f, -7.5f, 0f);

        // ================= the Signal Hut and its vestibule =================
        // hut outer x -2.4..2.4, z -17.6..-14.4 (inside x -2.2..2.2, z -17.4..-14.6); vestibule outer x 2.4..5.0
        const float HutZ = -16f;
        Piece sealedGroup = IsleKit.Lobby(b, "Sealed Hut", false, true, true);
        Piece signalHut = b.Empty("Signal Hut", 1.3f, HutZ, 0f).Under(sealedGroup);
        Component soundproof = b.AddScript(signalHut, "PuzzleSoundProof");
        Sdk.Set(soundproof, "material", 0);      // Wood
        // only the walls, roof and floor go under the soundproof parent: it turns every child collider solid
        Place(b, "Hut Walls", HutShell(), snowy, 0f, HutZ).Under(signalHut);
        Place(b, "Hut Floor", HutFloor(), bare, 0f, HutZ).Under(signalHut);
        Piece doorSealed = b.Part("Door", 2.3f, HutZ, 0f, 90f).Named("Hut Door Sealed")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true).Under(sealedGroup);
        b.Part("Gate No items", 4.9f, HutZ, 0f, 90f).Named("Hut Gate")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "defaultLocked", false).Under(sealedGroup);
        Piece dead = b.Part("Radio Dead Zone", 1.4f, HutZ, 1.5f).Named("Hut Dead Zone").Under(sealedGroup);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(8.4f, 4.0f, 4.4f);      // hut, vestibule and the turnstile
        zone.center = Vector3.zero;

        // 2 players: the same hut, not soundproof, a plain door, the turnstile's opening left as a gap, walkies supplied
        Piece openGroup = IsleKit.Lobby(b, "Open Hut", true, false, false);
        Place(b, "Hut Walls Open", HutShell(), snowy, 0f, HutZ).Under(openGroup);
        Place(b, "Hut Floor Open", HutFloor(), bare, 0f, HutZ).Under(openGroup);
        Piece doorOpen = b.Part("Door", 2.3f, HutZ, 0f, 90f).Named("Hut Door Open")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false).Under(openGroup);
        var shelves = new MeshBuilder();
        Block(shelves, 0f, 0f, -2.1f, -1.7f, 0f, 0.9f, -17.3f, -16.9f, IsleKit.Wood);      // in the hut's south-west corner
        Block(shelves, 0f, 0f, 7.45f, 7.75f, 0f, 0.9f, -6.55f, -6.25f, IsleKit.Wood);      // a post by the field corner
        Place(b, "Walkie Shelves", shelves, snowy, 0f, 0f).Under(openGroup);
        b.Part("Vanilla Item", -1.9f, -17.1f, 1.05f, 0f).Named("Walkie Hut")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false).Under(openGroup);
        b.Part("Vanilla Item", 7.6f, -6.4f, 1.05f, 0f).Named("Walkie Field")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false).Under(openGroup);

        // inside the hut (every lobby): the lamp board with its light baffle, the lamps, the mic, the bars, a switch
        Place(b, "Hut Fittings", HutFittings(rowColour), bare, 0f, HutZ);
        var lamps = new UnityEngine.Object[16];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
            {
                // facing the Lookout, who faces +z (towards the field), so board left is field left. Range 0.3 m, not the
                // spec's 1 m: the north wall's outer face is 0.36 m from the lamps, and a game light may not cast
                // shadows, so a longer reach could show the lit lamp as a glow on the outside of the wall
                Piece lamp = b.Part("Light 8", LampX[c], -14.76f, LampH[r], 180f).Named("Lamp " + (r + 1) + "-" + (c + 1))
                    .Set("PuzzleLight", "range", LampRange);
                lamps[r * 4 + c] = lamp.Component("PuzzleLight");
            }
        // the mic hangs between the board and the baffle, aimed at the Lookout's place in front of the board
        Piece mic = b.Part("Directional Mic", 0.85f, -15.3f, 1.4f, -124f).Named("Hut Mic");
        float hutWest = -2.2f + BarHalfDepth * BarScale, hutEast = 2.2f;
        Piece barHut = b.Part("Progress Bar", hutWest, -16.0f, 1.95f, 90f).Named("Bar Hut")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", Rounds);
        Piece phaseHut = b.Part("Progress Bar", hutWest, -16.0f, 1.45f, 90f).Named("Phase Hut")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", 9);
        b.Part("Switch Start", hutEast - SwitchBack, -17.0f, 1.25f, -90f).Named("Hut Switch");
        b.Part("Live Light", hutEast - LiveBack, -17.07f, 1.42f, -90f).Named("Hut Live");
        b.Empty("Room Light Hut FFE2B8 2.0 0.8", -0.6f, -16.2f, 2.3f);      // Winter Walk's room light, so the board reads

        // ================= the Signal Cab =================
        // platform x -2.2..2.2, z -7.6..-4.4, floor 2.6 m; inside x -2.0..2.0, z -7.4..-4.6; steps down behind it
        const float CabZ = -6f;
        Place(b, "Signal Cab", CabMesh(), snowy, 0f, CabZ);
        Piece chair = b.Part("Listening Chair", 1.5f, -5.3f, CabFloor, 0f).Named("Cab Chair");
        Sdk.Set(chair.Component("PuzzleListeningChair"), "microphone", mic.Component("PuzzleDirectionalMic"));
        Piece doorCab = b.Part("Door", 0f, -7.5f, CabFloor, 180f).Named("Cab Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);
        float cabWest = -2.0f + BarHalfDepth * BarScale, cabEast = 2.0f;
        b.Part("Switch Start", cabEast - SwitchBack, -6.9f, CabFloor + 1.25f, -90f).Named("Cab Switch");
        b.Part("Live Light", cabEast - LiveBack, -6.97f, CabFloor + 1.42f, -90f).Named("Cab Live");
        Piece barCab = b.Part("Progress Bar", cabWest, -6.0f, CabFloor + 1.9f, 90f).Named("Bar Cab")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", Rounds);
        Piece phaseCab = b.Part("Progress Bar", cabWest, -6.0f, CabFloor + 1.4f, 90f).Named("Phase Cab")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", 9);
        b.Empty("Room Light Cab FFE2B8 2.4 0.8", -0.5f, -6.4f, CabFloor + 2.3f);

        // ================= 4 players: the divider =================
        Piece dividerGroup = IsleKit.Lobby(b, "Field Divider", false, false, true);
        var divider = new MeshBuilder();
        float midZ = (FenceZ0 + FenceZ1) / 2f;
        Block(divider, 0f, midZ, -0.05f, 0.05f, -0.1f, 2.8f, FenceZ0, FenceZ1, IsleKit.Wood);
        for (int i = 0; i <= 8; i++)
        {
            float z = Mathf.Lerp(FenceZ0 + 0.1f, FenceZ1 - 0.1f, i / 8f);
            Block(divider, 0f, midZ, -0.1f, 0.1f, -0.1f, 2.9f, z - 0.07f, z + 0.07f, IsleKit.DarkWood);
        }
        Place(b, "Divider Fence", divider, snowy, 0f, midZ).Under(dividerGroup);

        // ================= Press In Order =================
        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", pads);
        Sdk.Add(pio, "progressBars", barBack.Component("PuzzleProgressBar"), barHut.Component("PuzzleProgressBar"), barCab.Component("PuzzleProgressBar"));
        Sdk.Add(pio, "phaseBars", phaseBack.Component("PuzzleProgressBar"), phaseHut.Component("PuzzleProgressBar"), phaseCab.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", Rounds);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", PhaseSeconds);

        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        barBack.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");      // one bar only
        // a win opens both rooms (the doors otherwise follow the switches: closed while the puzzle is on)
        b.RootPiece.On("PuzzleSDK", "onWin", doorSealed.Component("PuzzleDoor"), "Open");
        b.RootPiece.On("PuzzleSDK", "onWin", doorOpen.Component("PuzzleDoor"), "Open");
        b.RootPiece.On("PuzzleSDK", "onWin", doorCab.Component("PuzzleDoor"), "Open");

        Kit.LightRing(b, "Site Lights", 0f, 0f, 22f, 38f, 2.6f, true, 161);
    }

    // ---- helpers ----

    // A block given by its bounds in the puzzle's local metres (x, y up, z), added to a mesh whose piece stands at
    // local (ox, oz)
    static void Block(MeshBuilder mb, float ox, float oz, float x0, float x1, float y0, float y1, float z0, float z1, Color colour)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f - ox, y0, (z0 + z1) / 2f - oz), new Vector3(x1 - x0, y1 - y0, z1 - z0), colour);

    // Adds a mesh piece standing on the ground at local (ox, oz)
    static Piece Place(PuzzleBuilder b, string name, MeshBuilder mb, Material material, float ox, float oz)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material);
        piece.Transform.position = b.WorldPoint(ox, oz);
        return piece;
    }

    // The field fence: solid boards along the sides and back, vertical slats along the front (the cab sees through,
    // nobody passes) with two gate gaps, posts about every 2 m, and a colour stripe on the side boards behind each
    // row stake (inside and out)
    static void BuildFence(PuzzleBuilder b, Material snowy, Color[] rowColour)
    {
        float oz = (FenceZ0 + FenceZ1) / 2f;
        var mb = new MeshBuilder();
        // side boards (x ±9.1..9.2) and the back boards (z 16.5..16.6)
        Block(mb, 0f, oz, -FenceX, -FenceX + 0.1f, -0.1f, FenceH, FenceZ0, FenceZ1, IsleKit.Wood);
        Block(mb, 0f, oz, FenceX - 0.1f, FenceX, -0.1f, FenceH, FenceZ0, FenceZ1, IsleKit.Wood);
        Block(mb, 0f, oz, -FenceX, FenceX, -0.1f, FenceH, FenceZ1 - 0.1f, FenceZ1, IsleKit.Wood);

        // front: slats 0.06 wide every 0.25 m, rails top and bottom, all left out across the two gates
        bool InGate(float x0, float x1) => (x1 > -GateX - GateHalf && x0 < -GateX + GateHalf) || (x1 > GateX - GateHalf && x0 < GateX + GateHalf);
        for (float x = -FenceX + 0.125f; x < FenceX; x += 0.25f)
            if (!InGate(x - 0.03f, x + 0.03f))
                Block(mb, 0f, oz, x - 0.03f, x + 0.03f, -0.1f, FenceH, FenceZ0, FenceZ0 + 0.06f, IsleKit.Wood);
        float[] railFrom = { -FenceX, -GateX + GateHalf, GateX + GateHalf };
        float[] railTo = { -GateX - GateHalf, GateX - GateHalf, FenceX };
        for (int i = 0; i < 3; i++)
        {
            Block(mb, 0f, oz, railFrom[i], railTo[i], 0.15f, 0.25f, FenceZ0 + 0.06f, FenceZ0 + 0.12f, IsleKit.DarkWood);
            Block(mb, 0f, oz, railFrom[i], railTo[i], FenceH - 0.12f, FenceH - 0.02f, FenceZ0 + 0.06f, FenceZ0 + 0.12f, IsleKit.DarkWood);
        }

        // posts: about every 2 m round the fence, none in a gate gap, and one on each side of each gate
        const float P = 0.07f;
        for (int i = 0; i <= 10; i++)
        {
            float z = Mathf.Lerp(FenceZ0, FenceZ1, i / 10f);
            Block(mb, 0f, oz, -FenceX - P, -FenceX + P, -0.1f, FenceH + 0.1f, z - P, z + P, IsleKit.DarkWood);
            Block(mb, 0f, oz, FenceX - P, FenceX + P, -0.1f, FenceH + 0.1f, z - P, z + P, IsleKit.DarkWood);
            float x = Mathf.Lerp(-FenceX, FenceX, i / 10f);
            Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.1f, FenceZ1 - P, FenceZ1 + P, IsleKit.DarkWood);
            if (!InGate(x - P - 0.05f, x + P + 0.05f))
                Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.1f, FenceZ0 - P + 0.03f, FenceZ0 + P + 0.03f, IsleKit.DarkWood);
        }
        foreach (float g in new[] { -GateX, GateX })
            foreach (float side in new[] { -1f, 1f })
            {
                float x = g + side * (GateHalf + P);
                Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.25f, FenceZ0 - P + 0.03f, FenceZ0 + P + 0.03f, IsleKit.DarkWood);
            }

        // a 0.3 m colour stripe on the side boards behind each row's stakes, inside and outside
        for (int r = 0; r < 4; r++)
            foreach (float s in new[] { -1f, 1f })
            {
                float inner = s * (FenceX - 0.1f), outer = s * FenceX;
                Block(mb, 0f, oz, Mathf.Min(inner, inner - s * 0.02f), Mathf.Max(inner, inner - s * 0.02f), 0.2f, 1.7f, PadZ[r] - 0.15f, PadZ[r] + 0.15f, rowColour[r]);
                Block(mb, 0f, oz, Mathf.Min(outer, outer + s * 0.02f), Mathf.Max(outer, outer + s * 0.02f), 0.2f, 1.7f, PadZ[r] - 0.15f, PadZ[r] + 0.15f, rowColour[r]);
            }
        Place(b, "Field Fence", mb, snowy, 0f, oz);
    }

    // Row stakes (red, green, gold, ice-blue, front to back) inside both side fences; column stakes (0.6 to 1.5 m,
    // one to four white bands, left to right) inside the front and back fences
    static void BuildStakes(PuzzleBuilder b, Material snowy, Color[] rowColour)
    {
        float oz = (FenceZ0 + FenceZ1) / 2f;
        var mb = new MeshBuilder();
        for (int r = 0; r < 4; r++)
            foreach (float x in new[] { -8.5f, 8.5f })
                Block(mb, 0f, oz, x - 0.08f, x + 0.08f, 0f, 1.5f, PadZ[r] - 0.08f, PadZ[r] + 0.08f, rowColour[r]);
        for (int c = 0; c < 4; c++)
        {
            float h = 0.6f + 0.3f * c;
            foreach (float z in new[] { -1.6f, 16.0f })
            {
                Block(mb, 0f, oz, PadX[c] - 0.07f, PadX[c] + 0.07f, 0f, h, z - 0.07f, z + 0.07f, IsleKit.Wood);
                for (int band = 0; band <= c; band++)
                {
                    float top = h - 0.06f - band * 0.14f;
                    Block(mb, 0f, oz, PadX[c] - 0.085f, PadX[c] + 0.085f, top - 0.07f, top, z - 0.085f, z + 0.085f, IsleKit.Cream);
                }
            }
        }
        Place(b, "Stakes", mb, snowy, 0f, oz);
    }

    // The hut's and vestibule's walls and roof, in the hut's frame (its piece stands at local (0, -16)). No windows;
    // walls from 0.5 m below the ground into the roof. The hut's door opening in its east wall has a lintel and jamb
    // strips over the door frame's posts; the vestibule's east wall leaves exactly the turnstile's opening, with
    // fillers and a lintel round it. The same mesh serves the sealed (soundproof) and the open (2p) hut.
    static MeshBuilder HutShell()
    {
        const float oz = -16f, low = -0.5f, top = 2.7f;
        Color wall = IsleKit.Wood;
        var mb = new MeshBuilder();
        Block(mb, 0f, oz, -2.4f, -2.2f, low, top, -17.6f, -14.4f, wall);                // hut west
        Block(mb, 0f, oz, -2.4f, 5.0f, low, top, -14.6f, -14.4f, wall);                 // north (hut and vestibule)
        Block(mb, 0f, oz, -2.4f, 5.0f, low, top, -17.6f, -17.4f, wall);                 // south (hut and vestibule)
        // hut east wall (shared with the vestibule): door opening z -16.62..-15.38, 2.02 m high
        Block(mb, 0f, oz, 2.2f, 2.4f, low, top, -17.4f, -16.62f, wall);
        Block(mb, 0f, oz, 2.2f, 2.4f, low, top, -15.38f, -14.6f, wall);
        Block(mb, 0f, oz, 2.2f, 2.4f, low, 0f, -16.62f, -15.38f, wall);                  // under the sill, in the ground
        Block(mb, 0f, oz, 2.2f, 2.4f, 2.02f, top, -16.62f, -15.38f, wall);               // lintel
        Block(mb, 0f, oz, 2.1f, 2.2f, 0f, 2.02f, -16.62f, -16.52f, wall);                // jamb strips over the frame posts
        Block(mb, 0f, oz, 2.1f, 2.2f, 0f, 2.02f, -15.48f, -15.38f, wall);
        // vestibule east wall: the turnstile's opening z -17.01..-14.89, 2.3 m high
        Block(mb, 0f, oz, 4.8f, 5.0f, low, top, -17.4f, -16.96f, wall);
        Block(mb, 0f, oz, 4.8f, 5.0f, low, top, -14.94f, -14.6f, wall);
        Block(mb, 0f, oz, 4.8f, 5.0f, low, 0f, -16.96f, -14.94f, wall);
        Block(mb, 0f, oz, 4.8f, 5.0f, 2.3f, top, -16.96f, -14.94f, wall);
        // one roof over both, the walls running 0.1 m into it
        Block(mb, 0f, oz, -2.7f, 5.3f, 2.6f, 2.8f, -17.9f, -14.1f, IsleKit.Red);
        return mb;
    }

    // The floor slab of the hut and vestibule, its top 3 cm above the ground (bare: no snow indoors)
    static MeshBuilder HutFloor()
    {
        var mb = new MeshBuilder();
        Block(mb, 0f, -16f, -2.4f, 5.0f, -0.5f, 0.03f, -17.6f, -14.4f, IsleKit.DarkWood);
        return mb;
    }

    // The lamp board (a small map of the field: four row bands, red at the bottom = the front row, up to ice-blue =
    // the back row; four column marks under the bottom row, one to four white bands, left to right) and the light
    // baffle 1 m inside the door, which hides the board from the doorway. Not soundproof.
    static MeshBuilder HutFittings(Color[] rowColour)
    {
        const float oz = -16f;
        var mb = new MeshBuilder();
        Block(mb, 0f, oz, -1.6f, 0.6f, 0.6f, 2.6f, -14.66f, -14.6f, IsleKit.Board);
        for (int r = 0; r < 4; r++)
            Block(mb, 0f, oz, -1.55f, 0.55f, LampH[r] - 0.1f, LampH[r] + 0.1f, -14.67f, -14.66f, rowColour[r]);
        for (int c = 0; c < 4; c++)
            for (int band = 0; band <= c; band++)
            {
                float y = 0.64f + band * 0.05f;
                Block(mb, 0f, oz, LampX[c] - 0.05f, LampX[c] + 0.05f, y, y + 0.035f, -14.67f, -14.66f, IsleKit.Cream);
            }
        Block(mb, 0f, oz, 1.15f, 1.25f, 0f, 2.6f, -16.5f, -14.6f, IsleKit.Wood);       // the light baffle
        return mb;
    }

    // The Signal Cab, in its own frame (its piece stands at local (0, -6)): the platform, the walls with the slatted
    // front window and the back door's opening, the roof, and the steps up to the door
    static MeshBuilder CabMesh()
    {
        const float oz = -6f, f = CabFloor, top = CabFloor + 2.7f;
        var mb = new MeshBuilder();
        Block(mb, 0f, oz, -2.2f, 2.2f, -0.3f, f, -7.6f, -4.4f, IsleKit.Stone);                  // platform
        // front wall: solid to 0.4 m above the floor, a window to 2.2 m filled with slats, solid above
        Block(mb, 0f, oz, -2.2f, 2.2f, f, f + 0.4f, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -2.2f, 2.2f, f + 2.2f, top, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -2.2f, -1.8f, f + 0.4f, f + 2.2f, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, 1.8f, 2.2f, f + 0.4f, f + 2.2f, -4.6f, -4.4f, IsleKit.Wood);
        for (int k = 1; k <= 14; k++)
        {
            float x = -1.8f + 0.25f * k;
            Block(mb, 0f, oz, x - 0.03f, x + 0.03f, f + 0.4f, f + 2.2f, -4.54f, -4.46f, IsleKit.DarkWood);
        }
        // back wall: a 1.24 m door opening at x = 0 with a lintel from 2.02 m above the floor
        Block(mb, 0f, oz, -2.2f, -0.62f, f, top, -7.6f, -7.4f, IsleKit.Wood);
        Block(mb, 0f, oz, 0.62f, 2.2f, f, top, -7.6f, -7.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -0.62f, 0.62f, f + 2.02f, top, -7.6f, -7.4f, IsleKit.Wood);
        // side walls
        Block(mb, 0f, oz, -2.2f, -2.0f, f, top, -7.4f, -4.6f, IsleKit.Wood);
        Block(mb, 0f, oz, 2.0f, 2.2f, f, top, -7.4f, -4.6f, IsleKit.Wood);
        // roof 4.8 x 3.6
        Block(mb, 0f, oz, -2.4f, 2.4f, f + 2.6f, f + 2.8f, -7.8f, -4.2f, IsleKit.Red);
        // steps: 9 rises up to the floor, 0.3 m treads, from z -10.3 to the door at -7.6
        for (int i = 0; i < 9; i++)
        {
            float z0 = -10.3f + 0.3f * i;
            Block(mb, 0f, oz, -0.8f, 0.8f, -0.3f, f * (i + 1) / 9f, z0, z0 + 0.3f, IsleKit.Stone);
        }
        return mb;
    }
}
