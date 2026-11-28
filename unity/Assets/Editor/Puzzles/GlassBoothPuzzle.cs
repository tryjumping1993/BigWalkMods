// Glass Booth: a three-link relay whose first link has no spoken word (spec: puzzle-specs/GlassBooth.md, 2026-11-22).
//
// The Reader is shut in a soundproof, radio-dead glass booth, entered empty-handed through a No Items turnstile and
// two soundproof doors in series (turnstile, collar pocket, outer door, vestibule, inner door). Above the window the
// Reader sees a five-flap Display, gold dots 1..5 under its flaps. Through the glass the Reader can only point, at a
// rack of twelve awkward pictures (Scribbles and Emotes, reserved for this puzzle island-wide) standing in stalls.
// The Spotter outside watches, takes the tiles and carries them round to a small hatch in the Keeper's hut. The
// Keeper, locked in the roofed, windowless hut (door, porch and a locked turnstile; ordinary walls, so voices pass),
// hangs them on a five-slot board whose slots are labelled with dots in the scrambled order 3, 1, 5, 2, 4. The
// board follows those labels (answerFromFlap), so order travels as numbers, never as "left to right".
//
// A four-minute clock stops the round; the Validator's poke button checks; a right answer wins (the gourd and the
// confetti at the start corner), a wrong one shows its cross for 3 s and then stops the round, and the next start
// deals afresh. Every start sends all twelve tiles back to their stalls (GlassBoothTileReset, host only). Six Start
// Switches flip together, one in every pocket a player can be shut in (booth, vestibule, collar, hut, porch) and one
// at the start corner outside, so anyone can quit at any time. In a two-player lobby the hut's door and turnstile are
// left out and one player is Spotter and Keeper both.
//
// Coordinates: local metres from the root, x east, z north (+z forward); heights above the pad top (Floor above the
// root), every piece placed onGround: false on the one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class GlassBoothPuzzle : PuzzleDef
{
    public override string Name => "GlassBooth";
    public override Vector2 Footprint => new Vector2(24f, 22f);
    public override Vector2? FixedSite => new Vector2(-852f, -594f);      // layout-final: a quiet north-coast shelf near the plaza

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Wall = 2.6f;            // inside height of every room (walls stand 0..2.6, roofs 2.6..2.8)
    const float RoofTop = 2.8f;
    const float FloorTop = 0.02f;       // the booth's soundproof floor slabs stand 2 cm proud of the pad (coplanar faces z-fight)
    const float Doorway = 1.30f;        // provisional: the soundproof door's real size is risk 1 (the plain door is 1.24 x 2.04)
    const float DoorHigh = 2.05f;
    const float RoundSeconds = 240f;
    const float ResultSeconds = 3f;
    static readonly int[] SlotLabels = { 3, 1, 5, 2, 4 };      // dots over board slots 1..5 = the Display flap each follows
    const float FlapPitch = 0.42f;      // risk 5: confirm from a shot
    const float FlapOneX = -5.16f;      // flap 1 is the Reader's far left: the east end, as the Reader faces south
    const float SlotPitch = 0.45f;      // PuzzlePegBoard SlotSpacing
    const float SlotOneZ = -5.3f;
    static readonly Color Warm = new Color(1f, 0.9f, 0.75f);

    PuzzleBuilder b;

    // A block between two corners (local x, y above the pad top, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A mesh built in pad-top coordinates, placed on the pad
    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, Floor, false);
        return piece;
    }

    // An SDK part on the pad
    Piece P(string prefab, float x, float z, float height, float yaw = 0f) => b.Part(prefab, x, z, Floor + height, yaw, false);

    // Sets an enum field by its name (Sdk.Set takes the index, and PuzzlePegSet's values have gaps)
    static void SetEnumByName(Component c, string field, string value)
    {
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.propertyType != SerializedPropertyType.Enum) throw new System.Exception(c.GetType().Name + " has no enum '" + field + "'");
        int index = System.Array.IndexOf(p.enumNames, value);
        if (index < 0) throw new System.Exception(c.GetType().Name + "." + field + " has no value '" + value + "'");
        p.enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // A Radio Dead Zone box centred at (x, z, height above the pad top)
    void DeadZone(string name, float x, float z, float height, Vector3 size)
    {
        Piece dead = P("Radio Dead Zone", x, z, height).Named(name);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = size;
        zone.center = Vector3.zero;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(26f, 24f, 0.5f, 0f);
        b.DebugTeleport(-6f, -3f, 2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Component sdk = b.Puzzle;
        // No jetpacks over the puzzle: nobody flies over the booth, onto the hut's roof or round the seals
        IsleKit.CalmAir(b, "Glass Booth", 0.5f, 0f, new Vector3(40f, 120f, 40f), 0f, -10f, false);

        // ---- the pad: one level slab, sunk 1.5 m so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -11f, 12f, -1.75f, 0f, -10.4f, 10.4f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildBooth(snowy);
        BuildHut(snowy);

        // ---- the booth: windows, doors, turnstile, switches, lamps, clocks, the Display, dead zones ----
        P("Glass", -8f, 0.7f, 1.1f).Named("Window A").Pane(3.1f, 1.3f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 2).Set("PuzzleGlass", "wire", false);
        P("Glass", -4f, 0.7f, 1.1f).Named("Window B").Pane(3.1f, 1.3f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 2).Set("PuzzleGlass", "wire", false);
        P("Door", -6f, 3.7f, 0f).Named("Booth Inner Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        P("Door", -6f, 7.1f, 0f).Named("Booth Outer Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        Piece boothGate = P("Gate No items", -6f, 9f, 0f, 180f).Named("Booth Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

        P("Switch Start", -7.2f, 3.59f, 1.25f, 180f).Named("Start Booth");
        P("Switch Start", -7.19f, 5.4f, 1.25f, 90f).Named("Start Vestibule");
        P("Switch Start", -7.19f, 7.75f, 1.25f, 90f).Named("Start Collar");
        P("Live Light", -7.6f, 3.59f, 1.45f, 180f).Named("Live Booth");

        Piece lampA = P("Light 2", -8f, 2.2f, 2.55f).Named("Booth Lamp A").Set("PuzzleLight", "color", Warm);
        Piece lampB = P("Light 2", -4f, 2.2f, 2.55f).Named("Booth Lamp B").Set("PuzzleLight", "color", Warm);

        Piece clockPen = P("Timer", -6f, 0.59f, 2.15f, 180f).Named("Clock Pen").Set("PuzzleTimer", "duration", RoundSeconds);
        Piece clockBooth = P("Timer", -4.6f, 3.59f, 1.9f, 180f).Named("Clock Booth").Set("PuzzleTimer", "duration", RoundSeconds);
        // Hidden inside the sunk pad under the booth: it only holds the validator's result up before the stop
        Piece resultDelay = P("Timer", -6f, 2.2f, -0.8f).Named("Result Delay").Set("PuzzleTimer", "duration", ResultSeconds);

        Piece display = P("Display", -6f, 0.97f, 2.1f).Named("Display Five").Set("PuzzleDisplay", "size", 3);      // five flaps

        DeadZone("Booth Dead Zone", -6f, 2.2f, 1.6f, new Vector3(8.8f, 3.4f, 3.4f));        // x -10.4..-1.6, z 0.5..3.9: not the pen
        DeadZone("Entrance Dead Zone", -6f, 6.8f, 1.6f, new Vector3(3.0f, 3.4f, 6.0f));     // x -7.5..-4.5, z 3.8..9.8

        // ---- the rack: twelve tiles in twelve stalls, pictures towards the booth; Scribbles and Emotes in turn ----
        for (int i = 0; i < 12; i++)
        {
            Piece tile = P("Peg Tile", -9f + 0.55f * i, RackTileZ, 0.78f).Named("Tile " + (i + 1));
            Component tileC = tile.Component("PuzzlePegTile");
            SetEnumByName(tileC, "imageSet", i % 2 == 0 ? "Scribbles" : "Emotes");
            Sdk.Set(tileC, "image", i / 2 + 1);
        }
        Kit.LightString(b, "Rack Lights", new List<Vector3> { new Vector3(-9.8f, Floor + 1.3f, -1.45f), new Vector3(-2.2f, Floor + 1.3f, -1.45f) },
            0.05f, 0.5f, false, true, 141);

        // ---- the hut: lamp, clock, switches, the board with its validator and button, the hatch's shutter ----
        P("Switch Start", 7.79f, -3.2f, 1.25f, -90f).Named("Start Hut");
        P("Switch Start", 8.8f, -3.71f, 1.25f, 180f).Named("Start Porch");
        P("Live Light", 7.79f, -2.9f, 1.45f, -90f).Named("Live Hut");
        Piece hutLamp = P("Light 2", 4.5f, -3.9f, 2.55f).Named("Hut Lamp").Set("PuzzleLight", "color", Warm);
        Piece clockHut = P("Timer", 4.5f, -1.61f, 1.8f, 180f).Named("Clock Hut").Set("PuzzleTimer", "duration", RoundSeconds);

        // The board on the west wall: its pivot is at slot 1's end, its back on the wall (local z 0..0.225)
        Piece board = P("Peg Board", 1.21f, -5.3f, 1.3f, 90f).Named("Board Five").Set("PuzzlePegBoard", "size", 4);      // five slots
        Piece check = P("Validator", 1.21f, -2.3f, 1.75f, 90f).Named("Check Screen");
        Piece poke = P("Button Poke", 1.21f, -2.3f, 1.1f, 90f).Named("Check Button");
        Component boardC = board.Component("PuzzlePegBoard"), checkC = check.Component("PuzzleValidator"), displayC = display.Component("PuzzleDisplay");
        Sdk.Set(boardC, "validator", checkC);
        Sdk.Add(boardC, "answerFrom", displayC, displayC, displayC, displayC, displayC);
        Sdk.AddInts(boardC, "answerFromFlap", SlotLabels);
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));

        // Open only while the puzzle runs: before the start nothing can be passed into the hut
        P("Door", 6.8f, -8.92f, 0f, 180f).Named("Hatch Shutter").Set("PuzzleDoor", "mode", 0).Set("PuzzleDoor", "soundproof", false);

        // The hut's seal, for three players or more: in a lobby of two the porch stands open and one player is both
        // Spotter and Keeper
        Piece hutSeal = IsleKit.Lobby(b, "Hut Seal", false, true, true);
        P("Door", 7.9f, -4.9f, 0f, 90f).Named("Hut Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false).Under(hutSeal);
        Piece porchGate = P("Gate No items", 10.3f, -4.9f, 0f, 90f).Named("Porch Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false)
            .Under(hutSeal);

        // ---- the key's two halves: dots under the Display's flaps (the Reader's), dots over the board's slots (the
        //      Keeper's, scrambled) ----
        var dots = new MeshBuilder();
        for (int flap = 1; flap <= 5; flap++)
        {
            float fx = FlapOneX - FlapPitch * (flap - 1);
            for (int d = 0; d < flap; d++)
                Kit.AddBall(dots, new Vector3(fx + (d - (flap - 1) / 2f) * 0.08f, 1.80f, 0.835f), 0.035f, IsleKit.Gold);
        }
        for (int slot = 1; slot <= 5; slot++)
        {
            float sz = SlotOneZ + SlotPitch * (slot - 1);
            int count = SlotLabels[slot - 1];
            for (int d = 0; d < count; d++)
                Kit.AddBall(dots, new Vector3(1.235f, 1.62f, sz + (d - (count - 1) / 2f) * 0.075f), 0.035f, IsleKit.Gold);
        }
        OnPad("Order Dots", dots, bare, false);

        // ---- the corner: the sixth Start Switch, the gourd's vice, the pillow and the confetti ----
        IsleKit.StartCorner(b, -6f, -7.4f, 0f, true, Floor, false);
        Kit.LightRing(b, "Site Lights", 0.5f, 0f, 23f, 20f, Floor + 2.6f, false, 142);
        // Tells GlassBoothTileReset (and any later Winter Walk tile reset) to send this puzzle's tiles home on every start
        Piece reset = b.Empty("Tile Reset On Start", 0f, 0f, Floor, false);
        Sdk.Set(b.AddCustomScript(reset, "GlassBoothTileReset"), "label", Name);

        // ---- wiring ----
        Component lampAC = lampA.Component("PuzzleLight"), lampBC = lampB.Component("PuzzleLight"), hutLampC = hutLamp.Component("PuzzleLight");
        Component penC = clockPen.Component("PuzzleTimer"), boothClockC = clockBooth.Component("PuzzleTimer"), hutClockC = clockHut.Component("PuzzleTimer");
        Component delayC = resultDelay.Component("PuzzleTimer");
        Component boothGateC = boothGate.Component("PuzzleNoItemsGate"), porchGateC = porchGate.Component("PuzzleNoItemsGate");
        Piece root = b.RootPiece;
        foreach (Component lamp in new[] { lampAC, lampBC, hutLampC })
        {
            root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
            root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        }
        foreach (Component clock in new[] { penC, boothClockC, hutClockC })
            root.On("PuzzleSDK", "onPuzzleStart", clock, "StartTimer");
        foreach (Component clock in new[] { penC, boothClockC, hutClockC, delayC })
            root.On("PuzzleSDK", "onPuzzleStop", clock, "ResetTimer");
        foreach (Component gate in new[] { boothGateC, porchGateC })
        {
            root.On("PuzzleSDK", "onPuzzleStart", gate, "Lock");
            root.On("PuzzleSDK", "onPuzzleStop", gate, "Unlock");
        }
        // A win shows its tick for 3 s, then the stop opens everything; a wrong check shows its cross, then the round ends
        root.On("PuzzleSDK", "onWin", delayC, "StartTimer");
        check.On("PuzzleValidator", "onIncorrect", delayC, "StartTimer");
        resultDelay.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
        clockPen.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
    }

    const float RackTileZ = -1.12f;     // the tile's back (local z -0.237) clears the rack's lip

    // The booth, vestibule and collar: one soundproof shell (walls, roofs, floors, jambs, headers), the only child of
    // the "Booth" object that carries PuzzleSoundProof (it takes every collider under it, so no SDK part goes there)
    void BuildBooth(Material snowy)
    {
        Color wall = IsleKit.Cream, roof = IsleKit.DarkWood, trim = IsleKit.Red;
        float half = Doorway / 2f;
        var mb = new MeshBuilder();

        // floors: closing the voice path under the walls
        Block(mb, -10.2f, -1.8f, FloorTop - 0.15f, FloorTop, 0.6f, 3.8f, IsleKit.Wood);
        Block(mb, -7.4f, -4.6f, FloorTop - 0.15f, FloorTop, 3.8f, 9.75f, IsleKit.Wood);

        // booth: outer x -10.2..-1.8, z 0.6..3.8; south wall with two windows (0.5..1.7 high) and a pier between
        const float y0 = -0.1f;
        Block(mb, -10.2f, -9.5f, y0, Wall, 0.6f, 0.8f, wall);
        Block(mb, -6.5f, -5.5f, y0, Wall, 0.6f, 0.8f, wall);       // the pier
        Block(mb, -2.5f, -1.8f, y0, Wall, 0.6f, 0.8f, wall);
        foreach (float wx in new[] { -9.5f, -5.5f })
        {
            Block(mb, wx, wx + 3f, y0, 0.5f, 0.6f, 0.8f, wall);     // sill
            Block(mb, wx, wx + 3f, 1.7f, Wall, 0.6f, 0.8f, wall);   // header (the Display hangs on its inside)
            Block(mb, wx - 0.05f, wx + 3.05f, 0.42f, 0.5f, 0.45f, 0.6f, trim);      // a red ledge outside
        }
        Block(mb, -10.2f, -10f, y0, Wall, 0.8f, 3.6f, wall);        // west
        Block(mb, -2f, -1.8f, y0, Wall, 0.8f, 3.6f, wall);          // east
        // north wall with the inner doorway centred on x -6
        Block(mb, -10.2f, -6f - half, y0, Wall, 3.6f, 3.8f, wall);
        Block(mb, -6f + half, -1.8f, y0, Wall, 3.6f, 3.8f, wall);
        Block(mb, -6f - half, -6f + half, DoorHigh, Wall, 3.6f, 3.8f, wall);
        Block(mb, -10.35f, -1.65f, Wall, RoofTop, 0.45f, 3.8f, roof);

        // vestibule (z 3.8..7.2) and collar (z 7.2..9.75): side walls, the outer door's wall, roofs
        Block(mb, -7.4f, -7.2f, y0, Wall, 3.8f, 9.75f, wall);
        Block(mb, -4.8f, -4.6f, y0, Wall, 3.8f, 9.75f, wall);
        Block(mb, -7.2f, -6f - half, y0, Wall, 7.0f, 7.2f, wall);
        Block(mb, -6f + half, -4.8f, y0, Wall, 7.0f, 7.2f, wall);
        Block(mb, -6f - half, -6f + half, DoorHigh, Wall, 7.0f, 7.2f, wall);
        Block(mb, -7.55f, -4.45f, Wall, RoofTop, 3.8f, 9.9f, roof);
        // the turnstile's mouth: a header from the gate's top to the roof, and jambs from its asymmetric frame (local
        // x -1.11..+1.01, flipped by yaw 180 to world x -7.01..-4.89), the gate's depth z 8.29..9.75
        Block(mb, -7.2f, -4.8f, 2.3f, Wall, 8.29f, 9.75f, wall);
        Block(mb, -7.2f, -7.01f, y0, 2.3f, 8.29f, 9.75f, trim);
        Block(mb, -4.89f, -4.8f, y0, 2.3f, 8.29f, 9.75f, trim);

        Piece booth = b.Empty("Booth", -6f, 3f, Floor, false);
        b.AddScript(booth, "PuzzleSoundProof");
        OnPad("Booth Shell", mb, snowy).Under(booth);

        // the rack outside the window: bench, legs, twelve stalls and a lip
        var rack = new MeshBuilder();
        Block(rack, -9.5f, -2.5f, 0.49f, 0.55f, -1.4f, -1.0f, IsleKit.Wood);
        foreach (float lx in new[] { -9.4f, -6.05f, -2.6f })
            Block(rack, lx - 0.05f, lx + 0.05f, -0.05f, 0.49f, -1.35f, -1.05f, IsleKit.DarkWood);
        for (int k = 0; k <= 12; k++)
        {
            float dx = -9.275f + 0.55f * k;
            Block(rack, dx - 0.02f, dx + 0.02f, 0.55f, 0.67f, -1.4f, -1.0f, IsleKit.DarkWood);
        }
        Block(rack, -9.5f, -2.5f, 0.55f, 0.6f, -1.4f, -1.36f, IsleKit.DarkWood);
        OnPad("Rack", rack, snowy);
    }

    // The Keeper's hut and its porch: ordinary walls (voices pass), roofed and windowless, on the root
    void BuildHut(Material snowy)
    {
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.1f;
        var mb = new MeshBuilder();
        // hut: outer x 1.0..8.0, z -8.4..-1.4
        Block(mb, 1.0f, 1.2f, y0, Wall, -8.4f, -1.4f, wall);                   // west: the board's wall
        Block(mb, 1.2f, 7.8f, y0, Wall, -1.6f, -1.4f, wall);                   // north
        // south, with the hatch x 6.4..7.2, 0.9..1.4 high
        Block(mb, 1.2f, 6.4f, y0, Wall, -8.4f, -8.2f, wall);
        Block(mb, 7.2f, 7.8f, y0, Wall, -8.4f, -8.2f, wall);
        Block(mb, 6.4f, 7.2f, y0, 0.9f, -8.4f, -8.2f, wall);
        Block(mb, 6.4f, 7.2f, 1.4f, Wall, -8.4f, -8.2f, wall);
        // the hatch's collar, 0.6 deep (z -8.8..-8.2), too small and deep for a player
        Block(mb, 6.2f, 6.4f, 0.7f, 1.6f, -8.8f, -8.4f, roof);
        Block(mb, 7.2f, 7.4f, 0.7f, 1.6f, -8.8f, -8.4f, roof);
        Block(mb, 6.4f, 7.2f, 0.7f, 0.9f, -8.8f, -8.4f, roof);
        Block(mb, 6.4f, 7.2f, 1.4f, 1.6f, -8.8f, -8.4f, roof);
        // east, with the doorway z -5.55..-4.25
        Block(mb, 7.8f, 8.0f, y0, Wall, -8.4f, -5.55f, wall);
        Block(mb, 7.8f, 8.0f, y0, Wall, -4.25f, -1.4f, wall);
        Block(mb, 7.8f, 8.0f, DoorHigh, Wall, -5.55f, -4.25f, wall);
        Block(mb, 0.85f, 8.0f, Wall, RoofTop, -8.55f, -1.25f, roof);
        // inside: the partition that hides the board from the hatch, and the counter tiles land on
        Block(mb, 1.2f, 5.6f, y0, Wall, -6.3f, -6.1f, wall);
        Block(mb, 5.9f, 7.7f, y0, 0.85f, -8.2f, -7.4f, roof);

        // porch: outer x 8.0..11.05, z -6.3..-3.5, facing east; the turnstile in its mouth (x 9.595..11.005) with a
        // header from 2.3 m and jambs from its frame (local x -1.11..+1.01 maps to world z +1.11..-1.01 at yaw 90)
        Block(mb, 8.0f, 11.05f, y0, Wall, -6.3f, -6.1f, wall);
        Block(mb, 8.0f, 11.05f, y0, Wall, -3.7f, -3.5f, wall);
        Block(mb, 8.0f, 11.2f, Wall, RoofTop, -6.45f, -3.35f, roof);
        Block(mb, 9.595f, 11.05f, 2.3f, Wall, -6.1f, -3.7f, wall);
        Block(mb, 9.595f, 11.005f, y0, 2.3f, -6.1f, -5.91f, IsleKit.Red);
        Block(mb, 9.595f, 11.005f, y0, 2.3f, -3.79f, -3.7f, IsleKit.Red);
        OnPad("Hut", mb, snowy);
    }
}
