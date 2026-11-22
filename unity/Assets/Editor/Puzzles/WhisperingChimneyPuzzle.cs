// Whispering Chimney: "telephone" over fixed one-way channels (spec: puzzle-specs/WhisperingChimney.md, 2026-11-22).
//
// A low Listening Hut stands at the north end of a clearing, four squat stone chimneys behind its back wall. Each
// chimney's flue drops to a hearth far down in the rock (500, 800, 1100, 1400 m under the root), where a Sound Peg
// Answer plays a tune; a Directional Mic beside each hearth is picked by one Listening Chair, so only a seated
// listener hears it. 22 m south stands the Tile Shed, a closed room with nine Speaking Tiles (one of each of the
// game's nine tunes) and a peg board with one coloured slot per chimney.
//
//   listener (seated)  hears only their chimney, and hums it;
//   relay (in the hut) hears that hum, repeats it into the Hut Intercom (to the Hut Line speaker in the shed) and
//                      hears the matcher's candidate on the Shed Line speaker on the hut's baffle; compares the two;
//   matcher (in shed)  auditions the tiles, hums candidates into the Talk-back, hangs the confirmed tiles.
//
// No intercom lists a chair, so a seated listener hears nothing but the hearth. The hut is a soundproof shell inside
// a Radio Dead Zone, its only door a No Items turnstile behind a porch baffle; the shed is walled and roofed, its only
// opening a No Items turnstile, so the tiles never leave it. A right check wins (the gourd and the confetti at the
// start corner outside the shed); a wrong one shows its cross for 3 s and then stops the puzzle, and the next start
// deals every hearth afresh, so guessing throws away all progress. Two Start Switches (the corner, and one inside the
// hut) let anyone quit at any time; nothing ever locks. Four players: a fourth chimney (blue) and a four-slot board.
//
// Coordinates: local metres from the root, x east, z north (+z forward). Every building piece is placed onGround:
// false on its pad, heights above that pad's top. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WhisperingChimneyPuzzle : PuzzleDef
{
    public override string Name => "WhisperingChimney";
    public override Vector2 Footprint => new Vector2(20f, 42f);
    public override Vector2? FixedSite => new Vector2(-2580f, -1719f);      // layout-final: a smokehouse on the High Shelf's wooded south lip

    // From the shed's outer north wall (z -11) to the hut's outer south wall (z 10.96): at least the tile's audible
    // range + 3 m (spec Risk 1, to be measured in game). Documentation only: the hut's z values are literals, so a
    // bigger gap means shifting every hut, stack, eaves and dead-zone z by the difference and growing Footprint.y and
    // the Calm Air box to match.
    const float ShedGap = 22f;
    const float HearthDepth = 500f, HearthStep = 300f;       // hearth k at -(HearthDepth + k * HearthStep) under the root's level
    const float HutFloor = 0.25f, ShedFloor = 0.25f;         // tops of the two pads, above the root's level
    const float Wall = 2.7f;                                 // inside height of both rooms (roofs 2.7..2.94)
    const float ResultSeconds = 3f;                          // how long a wrong check's cross shows before the stop
    const float StackTop = 4.5f;                             // chimney stacks' tops, above the hut's floor

    static readonly float[] StackX = { -3.32f, -1.22f, 0.88f, 2.98f };       // behind each chair (chair x + 0.38)
    static readonly float[] ChairX = { -3.7f, -1.6f, 0.5f, 2.6f };
    static readonly string[] Colour = { "Red", "Green", "Gold", "Blue" };
    static readonly Color Blue = new Color(0.16f, 0.32f, 0.78f);
    static readonly Color[] Paint = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, Blue };
    static readonly Color Warm = new Color(1f, 0.88f, 0.7f);

    PuzzleBuilder b;

    // A block between two corners (local x, y above the pad top, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A mesh built in pad-top coordinates, placed on a pad whose top is 'floor' above the root's level
    Piece OnPad(string name, MeshBuilder mb, Material material, float floor, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, floor, false);
        return piece;
    }

    // An SDK part on a pad
    Piece P(string prefab, float x, float z, float floor, float height, float yaw = 0f) => b.Part(prefab, x, z, floor + height, yaw, false);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(20f, 42f);
        b.DebugTeleport(0f, 5f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Component sdk = b.Puzzle;
        Piece root = b.RootPiece;
        // No jetpacks over the compound: nobody flies onto the roofs or round the walls
        IsleKit.CalmAir(b, "Whispering Chimney", 0f, 0f, new Vector3(40f, 120f, 62f), 0f, -10f, false);

        // The four-player pieces (the blue chimney, chair, hearth and mic) and the two boards' lobby groups
        Piece fourth = IsleKit.Lobby(b, "Fourth Chimney", false, false, true);
        Piece boardThree = IsleKit.Lobby(b, "Board Three", true, true, false);
        Piece boardFour = IsleKit.Lobby(b, "Board Four", false, false, true);

        // ---- the hearths: deep under the root, 300 m apart; each mic 1 m in front of its speaker, aimed at it ----
        var hearths = new Component[4];
        var mics = new Component[4];
        for (int k = 0; k < 4; k++)
        {
            float depth = -(HearthDepth + k * HearthStep);
            Piece hearth = b.Part("Peg Sound Mounted", 0f, 0f, depth, 0f, false).Named("Hearth " + Colour[k]);
            Piece mic = b.Part("Directional Mic", 0f, 1.0f, depth + 0.4f, 0f, false).Named("Mic " + Colour[k]);
            mic.Transform.rotation = b.Root.transform.rotation * Quaternion.LookRotation(new Vector3(0f, -0.4f, -1.0f).normalized);
            if (k == 3) { hearth.Under(fourth); mic.Under(fourth); }
            hearths[k] = hearth.Component("PuzzleSoundPegAnswer");
            mics[k] = mic.Component("PuzzleDirectionalMic");
        }

        BuildHut(snowy, bare, fourth);
        BuildShed(snowy);

        // ---- the hut's parts (siblings of the soundproof shell, never its children) ----
        for (int k = 0; k < 4; k++)
        {
            Piece chair = P("Listening Chair", ChairX[k], 17.6f, HutFloor, 0f, 180f).Named("Chair " + Colour[k]);
            Sdk.Set(chair.Component("PuzzleListeningChair"), "microphone", mics[k]);
            if (k == 3) chair.Under(fourth);
        }
        Piece hutIntercom = P("Intercom Mic", 0f, 13.14f, HutFloor, 1.3f).Named("Hut Intercom");
        Piece shedLine = P("Intercom Speaker", -1.0f, 13.22f, HutFloor, 1.9f).Named("Shed Line");
        // the second knob (defaults: togglePuzzleState on), so players inside can quit; flipping either moves both
        P("Switch Start", 1.6f, 13.19f, HutFloor, 1.3f).Named("Hut Start");
        P("Live Light", 1.6f, 13.13f, HutFloor, 1.6f).Named("Hut Live");
        Piece hutLamp = P("Light 2", 0f, 16.0f, HutFloor, 2.6f).Named("Hut Lamp").Set("PuzzleLight", "color", Warm);
        P("Gate No items", 0.05f, 11.08f, HutFloor, 0f).Named("Hut Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        Piece dead = P("Radio Dead Zone", 0f, 15.2f, HutFloor, 2f).Named("Hut Dead Zone");
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(11f, 6f, 10f);
        zone.center = Vector3.zero;

        // ---- the lanterns on the stacks, in each chimney's colour ----
        var lanterns = new List<Component>();
        for (int k = 0; k < 4; k++)
        {
            Piece lantern = P("Light 1", StackX[k], 20.15f, HutFloor, StackTop + 0.1f + 0.05f).Named("Lantern " + Colour[k])
                .Set("PuzzleLight", "color", Paint[k]);
            if (k == 3) lantern.Under(fourth);
            lanterns.Add(lantern.Component("PuzzleLight"));
        }

        // ---- the shed's parts ----
        for (int i = 0; i < 9; i++)
            P("Peg Sound", -2.0f + 0.5f * i, -13.4f, ShedFloor, 1.14f).Named("Speaking Tile " + (i + 1)).Set("PuzzleSoundPeg", "sound", i);
        P("Gate No items", 0.05f, -11.12f, ShedFloor, 0f).Named("Shed Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        Piece hutLine = P("Intercom Speaker", -3.66f, -13.5f, ShedFloor, 1.9f, 90f).Named("Hut Line");
        Piece talkBack = P("Intercom Mic", 3.74f, -13.5f, ShedFloor, 1.3f, -90f).Named("Talk-back");
        Piece shedLamp = P("Light 2", 0f, -13.5f, ShedFloor, 2.55f).Named("Shed Lamp").Set("PuzzleLight", "color", Warm);
        Sdk.Add(hutIntercom.Component("PuzzleIntercom"), "speakers", hutLine.Component("PuzzleIntercomSpeaker"));
        Sdk.Add(talkBack.Component("PuzzleIntercom"), "speakers", shedLine.Component("PuzzleIntercomSpeaker"));

        // ---- the boards on the shed's south wall: slot n at local x -0.45 n, so red is on the left of a player
        //      facing the board; a wrong check shows its cross, then stops the puzzle ----
        Piece resultDelay = P("Timer", 0f, -13.5f, ShedFloor, -0.8f).Named("Result Delay").Set("PuzzleTimer", "duration", ResultSeconds);      // hidden in the pad
        Component delayC = resultDelay.Component("PuzzleTimer");
        // validators a little further out than the spec's -1.1 / -1.35: the 3- and 4-slot models' left ends are not
        // measured (the 5-slot model runs 0.218 past slot 1 and 0.218 past slot 5)
        Board(boardThree, 3, 0.45f, -1.3f, hearths, delayC, bare);
        Board(boardFour, 4, 0.675f, -1.55f, hearths, delayC, bare);

        // ---- the start corner outside the shed's door (the gourd can be carried away from there) ----
        IsleKit.StartCorner(b, 4.5f, -8.6f, 0f);

        // ---- lights: an S of bulbs from door to door, a string round the shed (open at its door), the hut's eaves ----
        Kit.LightString(b, "Path Lights", new List<Vector3>
        {
            new Vector3(-1.5f, 2.4f, -9.4f), new Vector3(1.5f, 2.4f, -5f), new Vector3(-1.5f, 2.4f, 0f),
            new Vector3(1.5f, 2.4f, 5f), new Vector3(-1.5f, 2.4f, 9.4f),
        }, 0.35f, 0.5f, true, true, 103);
        Kit.LightString(b, "Shed Lights", new List<Vector3>
        {
            new Vector3(-5.6f, 2.6f, -10f), new Vector3(-5.6f, 2.6f, -13.7f), new Vector3(-5.6f, 2.6f, -17.4f),
            new Vector3(0f, 2.6f, -17.4f), new Vector3(5.6f, 2.6f, -17.4f), new Vector3(5.6f, 2.6f, -13.7f), new Vector3(5.6f, 2.6f, -10.3f),
        }, 0.4f, 0.5f, true, true, 101);
        Kit.LightString(b, "Hut Eaves", new List<Vector3>
        {
            new Vector3(-4.75f, HutFloor + 2.72f, 19.4f), new Vector3(-4.75f, HutFloor + 2.72f, 10.76f),
            new Vector3(4.75f, HutFloor + 2.72f, 10.76f), new Vector3(4.75f, HutFloor + 2.72f, 19.4f),
        }, 0.12f, 0.45f, false, false, 102);      // not the north side: the chimney stacks stand there

        // ---- wiring: the lamps follow the puzzle (start and stop differ, as the checks require) ----
        var lamps = new List<Component> { hutLamp.Component("PuzzleLight"), shedLamp.Component("PuzzleLight") };
        lamps.AddRange(lanterns);
        foreach (Component lamp in lamps)
        {
            root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
            root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        }
        root.On("PuzzleSDK", "onPuzzleStop", delayC, "ResetTimer");
        // StopPuzzle = SetRunning(false): both knobs go to off, the hearths fall silent, and the next flip re-deals
        resultDelay.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
    }

    // One peg board (3 or 4 slots) with its validator and poke button and the colour plaques over its slots, all in
    // a lobby group; its answers are the first 'slots' hearths, each Sound Peg Answer's own tune (flap 1)
    void Board(Piece group, int slots, float pivotX, float checkX, Component[] hearths, Component delayC, Material bare)
    {
        const float face = -15.76f;      // the south wall's inner face; every part's back sits on it
        Piece board = P("Peg Board", pivotX, face, ShedFloor, 1.3f).Named("Chimney Board " + slots)
            .Set("PuzzlePegBoard", "size", slots - 1).Under(group);      // Slots3 = 2, Slots4 = 3
        Piece check = P("Validator", checkX, face, ShedFloor, 1.6f).Named("Validator " + slots).Under(group);
        Piece poke = P("Button Poke", checkX, face, ShedFloor, 0.9f).Named("Poke " + slots).Under(group);
        Component boardC = board.Component("PuzzlePegBoard"), checkC = check.Component("PuzzleValidator");
        var answers = new UnityEngine.Object[slots];
        var flaps = new int[slots];
        for (int k = 0; k < slots; k++) { answers[k] = hearths[k]; flaps[k] = 1; }
        Sdk.Add(boardC, "answerFrom", answers);
        Sdk.AddInts(boardC, "answerFromFlap", flaps);
        Sdk.Set(boardC, "validator", checkC);
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));
        check.On("PuzzleValidator", "onIncorrect", delayC, "StartTimer");

        var plaques = new MeshBuilder();
        for (int k = 0; k < slots; k++)
        {
            float x = pivotX - 0.45f * k;
            Block(plaques, x - 0.15f, x + 0.15f, 1.58f, 1.74f, face + 0.01f, face + 0.05f, Paint[k]);
        }
        OnPad("Slot Plaques " + slots, plaques, bare, ShedFloor, false).Under(group);
    }

    // The Listening Hut (shell x -4.64..4.64, z 10.96..19.44): pad, then the soundproof shell, roof and porch baffle
    // under "Listening Hut", then the chimney stacks and the chairs' colour plaques
    void BuildHut(Material snowy, Material bare, Piece fourth)
    {
        const float y0 = -0.1f;
        var pad = new MeshBuilder();
        Block(pad, -5f, 5f, -1.75f, 0f, 10.45f, 19.95f, IsleKit.Stone);
        OnPad("Hut Pad", pad, snowy, HutFloor);

        Color wall = IsleKit.Wood, roof = IsleKit.Red;
        var mb = new MeshBuilder();
        // south wall with the doorway x -1.1..1.1, 0..2.3, and its lintel
        Block(mb, -4.64f, -1.1f, y0, Wall, 10.96f, 11.2f, wall);
        Block(mb, 1.1f, 4.64f, y0, Wall, 10.96f, 11.2f, wall);
        Block(mb, -1.1f, 1.1f, 2.3f, Wall, 10.96f, 11.2f, wall);
        Block(mb, -4.64f, 4.64f, y0, Wall, 19.2f, 19.44f, wall);       // north
        Block(mb, -4.64f, -4.4f, y0, Wall, 11.2f, 19.2f, wall);        // west
        Block(mb, 4.4f, 4.64f, y0, Wall, 11.2f, 19.2f, wall);          // east
        Block(mb, -4.79f, 4.79f, Wall, Wall + 0.24f, 10.81f, 19.53f, roof);      // stops short of the stacks (from z 19.55)
        // the porch baffle: blocks every straight line from the chairs to the doorway; parts on its north face
        Block(mb, -2.6f, 2.6f, y0, Wall, 12.88f, 13.12f, IsleKit.DarkWood);
        Piece hut = b.Empty("Listening Hut", 0f, 15.2f, HutFloor, false);
        Sdk.Set(b.AddScript(hut, "PuzzleSoundProof"), "material", 0);
        OnPad("Hut Shell", mb, snowy, HutFloor).Under(hut);

        // the chimney stacks against the north wall, from below the ground (they straddle the pad's edge) to 4.5 m,
        // a band of the chimney's colour at 3.6 m and a cap
        for (int k = 0; k < 4; k++)
        {
            var stack = new MeshBuilder();
            float x = StackX[k];
            Block(stack, x - 0.6f, x + 0.6f, -1.75f, StackTop, 19.55f, 20.75f, IsleKit.Stone);
            Block(stack, x - 0.62f, x + 0.62f, 3.6f, 3.85f, 19.53f, 20.77f, Paint[k]);
            Block(stack, x - 0.7f, x + 0.7f, StackTop, StackTop + 0.1f, 19.45f, 20.85f, IsleKit.DarkWood);
            Piece piece = OnPad("Chimney " + Colour[k], stack, snowy, HutFloor);
            if (k == 3) piece.Under(fourth);
        }

        // the chairs' plaques on the north wall's inner face, above each chair
        for (int k = 0; k < 4; k++)
        {
            var plaque = new MeshBuilder();
            float x = StackX[k];
            Block(plaque, x - 0.35f, x + 0.35f, 1.95f, 2.3f, 19.14f, 19.19f, Paint[k]);
            Piece piece = OnPad("Chair Plaque " + Colour[k], plaque, bare, HutFloor, false);
            if (k == 3) piece.Under(fourth);
        }
    }

    // The Tile Shed (shell x -4..4, z -16..-11): pad, four walls with the doorway in the north one, a closed roof,
    // and the bench the tiles lie on
    void BuildShed(Material snowy)
    {
        const float y0 = -0.1f;
        var pad = new MeshBuilder();
        Block(pad, -4.3f, 4.3f, -1.75f, 0f, -16.3f, -10.7f, IsleKit.Stone);
        OnPad("Shed Pad", pad, snowy, ShedFloor);

        Color wall = IsleKit.Wood;
        var mb = new MeshBuilder();
        Block(mb, -4f, 4f, y0, Wall, -16f, -15.76f, wall);                     // south: the boards' wall
        Block(mb, -4f, -1.1f, y0, Wall, -11.24f, -11f, wall);                  // north, with the doorway x -1.1..1.1
        Block(mb, 1.1f, 4f, y0, Wall, -11.24f, -11f, wall);
        Block(mb, -1.1f, 1.1f, 2.3f, Wall, -11.24f, -11f, wall);               // the lintel, down to 2.3 m
        Block(mb, -4f, -3.76f, y0, Wall, -15.76f, -11.24f, wall);              // west
        Block(mb, 3.76f, 4f, y0, Wall, -15.76f, -11.24f, wall);                // east
        Block(mb, -4.15f, 4.15f, Wall, Wall + 0.24f, -16.15f, -10.85f, IsleKit.Green);      // the roof, closed
        Block(mb, -2.4f, 2.4f, 0f, 0.9f, -13.75f, -13.05f, IsleKit.DarkWood);  // the bench, top at 0.9 m
        OnPad("Shed Shell", mb, snowy, ShedFloor);
    }
}
