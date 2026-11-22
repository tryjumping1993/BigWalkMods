// Santa's List (puzzle 8), "Checked Twice": he's making a list, he's checking it twice (spec: puzzle-specs/SantasList.md,
// 2026-11-22). Two secret lists reach one Packer by two different senses.
//
// - The Nice List hangs on the far end wall of Santa's Long Hall, a sealed log gallery 31 m long with a flat ceiling,
//   solid gables and a sealed roof. Its only opening is a glazed keyhole into the Lookout's hut at the north end, where
//   a telescope looks down the hall. The Lookout reads it out (voice, or the two walkie-talkies on the table).
// - The Naughty List hangs inside the Post Office, a soundproof, radio-dead booth whose door shuts while the puzzle
//   runs. The Postmaster cannot speak to anyone: they point through the glass at tiles on the Toy Shelf outside. An
//   entry baffle inside the door breaks every straight line from the doorway to the flaps.
// - The Packer hangs six tiles (of twelve, Shapes and Emotes mixed) on the sack board behind the Sleigh, whose slots are
//   labelled by plaque (gold star = Nice, red stocking = booth B, green tree = booth C) and pips, in a scrambled order.
//   Every list entry is its own one-flap Display under (or over) its own plaque, and each slot answers from flap 1 of a
//   named Display, so the order never depends on how the game lays flaps out.
// - The check is the sleigh bells: one Button Sim per role in group "sleighbells", all pulled within 3 s; the sleigh's
//   lever fires the Validator's Check (with an empty slot the Validator is off and nothing happens). A tick wins (the
//   gourd at the start corner, the confetti, the clock paused, booth B's door opened); a cross is a strike. Three
//   strikes or five minutes stop the round: lists blank, the doors open, and the next start deals afresh.
// - Lobby sizes: a 4-player lobby adds booth C (a second Postmaster) and two-flap lists; a 2-player lobby drops the
//   Lookout's lever (the Packer walks to the keyhole themselves) and uses a four-slot board.
// - Quit: a Start Switch in each booth, in the hut and at the start corner; they all stay in step.
// - Calm Air over the whole site: no jetpack flight over the roofs or between the hut's lever and the sleigh's.
//
// No custom code. Coordinates: local metres from the root, x east, z north (+z forward); heights above the pad top
// (Floor above the root's level), every piece placed onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x,
// -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class SantasListPuzzle : PuzzleDef
{
    public override string Name => "SantasList";
    public override Vector2 Footprint => new Vector2(20f, 44f);
    public override Vector2? FixedSite => new Vector2(-816f, -1332f);      // layout-final: "Post Office by the station", East Lowland

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float RoundSeconds = 300f;
    const float BellWindow = 3f;        // every lever yellow within this many seconds
    const float BellRearm = 4f;
    const string BellGroup = "sleighbells";
    const float SlotPitch = 0.45f;      // PuzzlePegBoard: slot n at local -0.45 (n - 1) from the pivot (risk 5: confirm from the preview)
    const float SlotPlaqueH = 1.62f;
    const float SackFaceZ = -2.34f;     // the sack panel's north face

    static readonly Color Pip = new Color(0.96f, 0.96f, 0.96f);
    static readonly Color DarkRed = new Color(0.45f, 0.06f, 0.06f);

    enum Mark { Star, Stocking, Tree }

    PuzzleBuilder b;

    // ---------------------------------------------------------------- helpers

    // A block between two corners (local x, y above the pad top, z), corners in any order
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
    {
        float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1), za = Mathf.Min(z0, z1), zb = Mathf.Max(z0, z1);
        Kit.AddBox(mb, new Vector3((xa + xb) / 2f, Mathf.Min(y0, y1), (za + zb) / 2f), new Vector3(xb - xa, Mathf.Abs(y1 - y0), zb - za), color);
    }

    // A triangle wound to face away from 'centre' (the middle of the convex solid it belongs to)
    static void TriOut(MeshBuilder mb, Vector3 a, Vector3 p, Vector3 q, Vector3 centre, Color color)
    {
        Vector3 n = Vector3.Cross(p - a, q - a);
        if (Vector3.Dot(n, (a + p + q) / 3f - centre) < 0f) { Vector3 t = p; p = q; q = t; }
        mb.Tri(a, p, q, color);
    }

    // A convex prism: a flat convex outline (any plane) swept along 'along' (gables, roof slabs)
    static void Extrude(MeshBuilder mb, Vector3[] outline, Vector3 along, Color color)
    {
        int n = outline.Length;
        Vector3 centre = Vector3.zero;
        foreach (Vector3 p in outline) centre += p;
        centre = centre / n + along / 2f;
        for (int i = 1; i + 1 < n; i++)
        {
            TriOut(mb, outline[0], outline[i], outline[i + 1], centre, color);
            TriOut(mb, outline[0] + along, outline[i] + along, outline[i + 1] + along, centre, color);
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outline[i], c = outline[(i + 1) % n];
            TriOut(mb, a, c, c + along, centre, color);
            TriOut(mb, a, c + along, a + along, centre, color);
        }
    }

    // A mesh built in pad-top coordinates, placed on the pad
    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, Floor, false);
        return piece;
    }

    // An SDK part on the pad
    Piece P(string prefab, float x, float z, float height, float yaw = 0f) => b.Part(prefab, x, z, Floor + height, yaw, false);

    // An empty marker on the pad
    Piece E(string name, float x, float z, float height) => b.Empty(name, x, z, Floor + height, false);

    // Sets an enum field by its value's name (Sdk.Set takes the index, and PuzzlePegSet's values have gaps)
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
    Piece DeadZone(string name, float x, float z, float height, Vector3 size)
    {
        Piece dead = P("Radio Dead Zone", x, z, height).Named(name);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = size;
        zone.center = Vector3.zero;
        return dead;
    }

    // A press-together lever of the sleigh bells
    Piece Lever(string name, float x, float z, float height, float yaw)
        => P("Button Sim", x, z, height, yaw).Named(name)
            .Set("PuzzleSimPressSwitch", "group", BellGroup)
            .Set("PuzzleSimPressSwitch", "activeSeconds", BellWindow)
            .Set("PuzzleSimPressSwitch", "timeoutSeconds", BellRearm);

    // A one-flap Display
    Piece Flap(string name, float x, float z, float height, float yaw)
        => P("Display", x, z, height, yaw).Named(name).Set("PuzzleDisplay", "size", 0);

    // A plaque on a wall: a dark board 0.3 x 0.18 m centred on (cx, cy), its back on the wall plane z = wallZ, facing +z
    // (facing = 1) or -z (facing = -1); its mark on the viewer's left and 1 to 3 white pips on their right
    static void AddPlaque(MeshBuilder mb, float cx, float cy, float wallZ, float facing, Mark mark, int pips)
    {
        // u runs to the viewer's right: a viewer facing a plaque that faces +z looks south, and their right is -x
        float X(float u) => cx - facing * u;
        float Zf(float d) => wallZ + facing * d;
        Block(mb, X(-0.15f), X(0.15f), cy - 0.09f, cy + 0.09f, Zf(0f), Zf(0.02f), IsleKit.Board);
        switch (mark)
        {
            case Mark.Star:
                Kit.AddStar(mb, new Vector3(X(-0.08f), cy, Zf(0.034f)), 0.06f, 0.012f, IsleKit.Gold);
                break;
            case Mark.Stocking:
                Block(mb, X(-0.105f), X(-0.065f), cy - 0.03f, cy + 0.045f, Zf(0.02f), Zf(0.035f), IsleKit.Red);       // leg
                Block(mb, X(-0.105f), X(-0.03f), cy - 0.065f, cy - 0.03f, Zf(0.02f), Zf(0.035f), IsleKit.Red);        // foot
                Block(mb, X(-0.112f), X(-0.058f), cy + 0.045f, cy + 0.07f, Zf(0.02f), Zf(0.038f), Pip);               // cuff
                break;
            case Mark.Tree:
                Block(mb, X(-0.088f), X(-0.072f), cy - 0.07f, cy - 0.05f, Zf(0.02f), Zf(0.032f), IsleKit.Wood);       // trunk
                Block(mb, X(-0.125f), X(-0.035f), cy - 0.05f, cy - 0.01f, Zf(0.02f), Zf(0.035f), IsleKit.Green);
                Block(mb, X(-0.112f), X(-0.048f), cy - 0.01f, cy + 0.03f, Zf(0.02f), Zf(0.035f), IsleKit.Green);
                Block(mb, X(-0.098f), X(-0.062f), cy + 0.03f, cy + 0.065f, Zf(0.02f), Zf(0.035f), IsleKit.Green);
                break;
        }
        for (int k = 0; k < pips; k++)
            Kit.AddBall(mb, new Vector3(X(0.01f + 0.045f * k), cy, Zf(0.045f)), 0.025f, Pip);
    }

    // ---------------------------------------------------------------- the puzzle

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -6f, Floor + 1.5f);
        b.ClearGround(20f, 44f);
        Sdk.Set(b.Puzzle, "gourdColor", DarkRed);      // darker: a hard puzzle
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Component sdk = b.Puzzle;
        // No jetpacks over the site: nobody flies onto the roofs or carries one body from the hut's lever to the sleigh's
        IsleKit.CalmAir(b, "Santas List", 0.5f, 3f, new Vector3(36f, 120f, 60f), 0f, -10f, false);

        // ---- the pad: one level slab under the yard, the booths, the hall and the hut, sunk so no gap ever shows ----
        var pad = new MeshBuilder();
        Block(pad, -9.7f, 9.7f, -1.75f, 0f, -16.2f, 21.9f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        // ---- lobby groups ----
        Piece flapsThird = IsleKit.Lobby(b, "Flaps Third", false, true, false);
        Piece boothC = IsleKit.Lobby(b, "Booth C", false, false, true);
        Piece lookoutLever = IsleKit.Lobby(b, "Lookout Lever", false, true, true);
        Piece boardTwo = IsleKit.Lobby(b, "Board Two", true, false, false);
        Piece boardThree = IsleKit.Lobby(b, "Board Three", false, true, false);
        Piece boardFour = IsleKit.Lobby(b, "Board Four", false, false, true);

        BuildHall(snowy);
        BuildHut(snowy);
        BuildBooth(snowy, false, null);
        BuildBooth(snowy, true, boothC);
        BuildYard(snowy);

        // ---- the hall: the Nice list on its south end wall, facing north up the hall ----
        Piece nice1 = Flap("Nice 1", 6.0f, -13.53f, 1.5f, 0f);
        Piece nice2 = Flap("Nice 2", 6.5f, -13.53f, 1.5f, 0f);
        Piece nice3 = Flap("Nice 3", 7.0f, -13.53f, 1.5f, 0f).Under(flapsThird);
        Piece hallLamp = P("Light 2", 6.5f, -12.9f, 2.6f).Named("Hall Lamp");
        E("Room Light 4 FFE3B0 3 0.5", 6.5f, -12.5f, 2.6f);      // the end wall is never pitch black

        // ---- the hut: keyhole glass, telescope, the Lookout's lever, knob and lamp ----
        P("Glass", 6.5f, 16.85f, 1.5f, 180f).Named("Keyhole Glass").Pane(0.45f, 0.4f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0).Set("PuzzleGlass", "wire", false);
        // (risk 2: the placeholder's foot is 1.1 m under its pivot; confirm the eye lines up with the keyhole)
        P("Telescope", 6.5f, 18.1f, 1.1f, 180f).Named("Keyhole Telescope").Set("PuzzleTelescope", "invisibleInkView", false);
        Lever("Lookout Lever", 5.4f, 17.02f, 1.3f, 0f).Under(lookoutLever);
        P("Switch Start", 7.6f, 17.073f, 1.25f, 0f).Named("Hut Start");
        P("Live Light", 7.6f, 17.006f, 1.45f, 0f).Named("Hut Live");
        E("Room Light 3 FFE3B0 5 1.0", 6.5f, 19.0f, 2.3f);

        // ---- booth B: door, window, the Naughty list over the window, lever, knob, lamp, light, dead zone ----
        Piece doorB = P("Door", -1.75f, -13.6f, 0f).Named("Booth B Door").Set("PuzzleDoor", "soundproof", true).Set("PuzzleDoor", "mode", 1);
        P("Glass", -1.5f, -10.4f, 1.35f).Named("Booth B Window").Pane(2.2f, 1.3f)
            .Set("PuzzleGlass", "style", 0).Set("PuzzleGlass", "sound", 2).Set("PuzzleGlass", "wire", true);
        Piece naughtyB1 = Flap("Naughty B1", -2.55f, -10.67f, 2.4f, 180f);
        Piece naughtyB2 = Flap("Naughty B2", -2.05f, -10.67f, 2.4f, 180f);
        Piece naughtyB3 = Flap("Naughty B3", -1.55f, -10.67f, 2.4f, 180f).Under(flapsThird);
        Lever("Post Lever B", -0.02f, -12.35f, 1.3f, -90f);
        P("Switch Start", -2.927f, -12.0f, 1.25f, 90f).Named("Booth B Start");
        P("Live Light", -2.994f, -12.0f, 1.45f, 90f).Named("Booth B Live");
        E("Room Light 1 FFE3B0 4 0.9", -1.5f, -12.2f, 2.6f);
        DeadZone("Booth B Dead Zone", -1.5f, -12.0f, 1.45f, new Vector3(3.0f, 2.9f, 3.0f));

        // ---- booth C (4 players): the mirror of B about x -3.5 ----
        P("Door", -5.25f, -13.6f, 0f).Named("Booth C Door").Set("PuzzleDoor", "soundproof", true).Set("PuzzleDoor", "mode", 1).Under(boothC);
        P("Glass", -5.5f, -10.4f, 1.35f).Named("Booth C Window").Pane(2.2f, 1.3f)
            .Set("PuzzleGlass", "style", 0).Set("PuzzleGlass", "sound", 2).Set("PuzzleGlass", "wire", true).Under(boothC);
        Piece naughtyC1 = Flap("Naughty C1", -4.45f, -10.67f, 2.4f, 180f).Under(boothC);
        Piece naughtyC2 = Flap("Naughty C2", -4.95f, -10.67f, 2.4f, 180f).Under(boothC);
        Lever("Post Lever C", -6.98f, -12.35f, 1.3f, 90f).Under(boothC);
        P("Switch Start", -4.073f, -12.0f, 1.25f, -90f).Named("Booth C Start").Under(boothC);
        P("Live Light", -4.006f, -12.0f, 1.45f, -90f).Named("Booth C Live").Under(boothC);
        E("Room Light 2 FFE3B0 4 0.9", -5.5f, -12.2f, 2.6f).Under(boothC);
        DeadZone("Booth C Dead Zone", -5.5f, -12.0f, 1.45f, new Vector3(3.0f, 2.9f, 3.0f)).Under(boothC);

        // ---- the list plaques: under each Nice flap (facing north), over each Naughty flap (facing into the booth) ----
        var niceMarks = new MeshBuilder();
        AddPlaque(niceMarks, 6.0f, 1.2f, -13.7f, 1f, Mark.Star, 1);
        AddPlaque(niceMarks, 6.5f, 1.2f, -13.7f, 1f, Mark.Star, 2);
        OnPad("Nice Plaques", niceMarks, bare, false);
        var boothBMarks = new MeshBuilder();
        AddPlaque(boothBMarks, -2.55f, 2.7f, -10.5f, -1f, Mark.Stocking, 1);
        AddPlaque(boothBMarks, -2.05f, 2.7f, -10.5f, -1f, Mark.Stocking, 2);
        OnPad("Booth B Plaques", boothBMarks, bare, false);
        var thirdMarks = new MeshBuilder();
        AddPlaque(thirdMarks, 7.0f, 1.2f, -13.7f, 1f, Mark.Star, 3);
        AddPlaque(thirdMarks, -1.55f, 2.7f, -10.5f, -1f, Mark.Stocking, 3);
        OnPad("Third Flap Plaques", thirdMarks, bare, false).Under(flapsThird);
        var boothCMarks = new MeshBuilder();
        AddPlaque(boothCMarks, -4.45f, 2.7f, -10.5f, -1f, Mark.Tree, 1);
        AddPlaque(boothCMarks, -4.95f, 2.7f, -10.5f, -1f, Mark.Tree, 2);
        OnPad("Booth C Plaques", boothCMarks, bare, false).Under(boothC);

        // ---- the Toy Shelf: twelve tiles facing the booths' windows, Shapes and Emotes mixed ----
        // Lower tier S1 E1 S2 S3 E2 S4, upper tier S5 E3 S6 S7 E4 S8 (S = Shapes, E = Emotes, by the enum's name)
        string[] lowerSet = { "Shapes", "Emotes", "Shapes", "Shapes", "Emotes", "Shapes" };
        int[] lowerImage = { 1, 1, 2, 3, 2, 4 };
        string[] upperSet = { "Shapes", "Emotes", "Shapes", "Shapes", "Emotes", "Shapes" };
        int[] upperImage = { 5, 3, 6, 7, 4, 8 };
        for (int k = 0; k < 6; k++)
        {
            float tx = -4.275f + 0.55f * k;
            Component lower = P("Peg Tile", tx, -8.8f, 0.55f + 0.24f, 180f).Named("Tile " + (k + 1)).Component("PuzzlePegTile");
            SetEnumByName(lower, "imageSet", lowerSet[k]);
            Sdk.Set(lower, "image", lowerImage[k]);
            Component upper = P("Peg Tile", tx, -8.4f, 0.95f + 0.24f, 180f).Named("Tile " + (k + 7)).Component("PuzzlePegTile");
            SetEnumByName(upper, "imageSet", upperSet[k]);
            Sdk.Set(upper, "image", upperImage[k]);
        }

        // ---- the sack board: Validator, Timer, strike bar and three boards (one per lobby size), facing north ----
        Piece validator = P("Validator", -0.55f, -2.32f, 1.6f).Named("Sack Validator");      // no poke button: only the bells check
        Component validatorC = validator.Component("PuzzleValidator");
        Piece timer = P("Timer", -2.4f, -2.30f, 2.3f).Named("Sack Timer").Set("PuzzleTimer", "duration", RoundSeconds);
        Piece strikes = P("Progress Bar", -3.9f, -2.22f, 2.3f).Named("Strikes").Scaled(0.3f, 0.6f, 1f)
            .Set("PuzzleProgressBar", "total", 3).Set("PuzzleProgressBar", "style", 0);

        Component nice1C = nice1.Component("PuzzleDisplay"), nice2C = nice2.Component("PuzzleDisplay"), nice3C = nice3.Component("PuzzleDisplay");
        Component b1C = naughtyB1.Component("PuzzleDisplay"), b2C = naughtyB2.Component("PuzzleDisplay"), b3C = naughtyB3.Component("PuzzleDisplay");
        Component c1C = naughtyC1.Component("PuzzleDisplay"), c2C = naughtyC2.Component("PuzzleDisplay");

        SackBoard("Sack Board 4", -1.7f, 3, boardTwo, validatorC,
            new[] { b2C, nice1C, b1C, nice2C },
            new[] { Mark.Stocking, Mark.Star, Mark.Stocking, Mark.Star }, new[] { 2, 1, 1, 2 }, bare);
        SackBoard("Sack Board 6 (3p)", -1.25f, 5, boardThree, validatorC,
            new[] { nice2C, b1C, nice3C, b3C, nice1C, b2C },
            new[] { Mark.Star, Mark.Stocking, Mark.Star, Mark.Stocking, Mark.Star, Mark.Stocking }, new[] { 2, 1, 3, 3, 1, 2 }, bare);
        SackBoard("Sack Board 6 (4p)", -1.25f, 5, boardFour, validatorC,
            new[] { c1C, nice2C, b2C, c2C, nice1C, b1C },
            new[] { Mark.Tree, Mark.Star, Mark.Stocking, Mark.Tree, Mark.Star, Mark.Stocking }, new[] { 1, 2, 2, 2, 1, 1 }, bare);

        // ---- the sleigh bells: the sleigh's lever on the bell post, facing the booths; it alone carries the wire ----
        Piece sleighLever = Lever("Sleigh Lever", -2.4f, -4.72f, 1.6f, 180f);

        // ---- the walkie-talkies, and the corner: switch, live lamp, vice, pillow, confetti ----
        P("Vanilla Item", 2.4f, 4.2f, 0.9f + 0.15f).Named("Walkie 1")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        P("Vanilla Item", 2.8f, 4.2f, 0.9f + 0.15f).Named("Walkie 2")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        IsleKit.StartCorner(b, 2.6f, 1.0f, -90f, true, Floor, false);

        // ---- wiring ----
        Component timerC = timer.Component("PuzzleTimer"), strikesC = strikes.Component("PuzzleProgressBar");
        Component hallLampC = hallLamp.Component("PuzzleLight"), doorBC = doorB.Component("PuzzleDoor");
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", timerC, "StartTimer");          // 1  the round's clock
        root.On("PuzzleSDK", "onPuzzleStart", strikesC, "Reset");             // 2  fresh strikes
        root.On("PuzzleSDK", "onPuzzleStart", hallLampC, "TurnOn");           // 3  the list lights up
        root.On("PuzzleSDK", "onPuzzleStop", timerC, "ResetTimer");           // 4
        root.On("PuzzleSDK", "onPuzzleStop", strikesC, "Reset");              // 5  (not Fail: stop also fires at spawn)
        root.On("PuzzleSDK", "onPuzzleStop", hallLampC, "TurnOff");           // 6
        root.On("PuzzleSDK", "onWin", timerC, "PauseTimer");                  // 7  first win on a save only
        root.On("PuzzleSDK", "onWin", doorBC, "Open");                        // 8  lets the Postmaster out
        timer.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");             // 9  time up
        sleighLever.On("PuzzleSimPressSwitch", "onSuccess", validatorC, "Check");      // 10 the bells are the send key
        validator.On("PuzzleValidator", "onIncorrect", strikesC, "Increment");          // 11 a strike
        strikes.On("PuzzleProgressBar", "onFull", sdk, "StopPuzzle");                    // 12 third strike
    }

    // A Peg Board on the sack panel (pivot at slot 1, slots running west), answering slot by slot from flap 1 of the
    // displays given, with a plaque over each slot; all of it in its lobby group
    void SackBoard(string name, float pivotX, int sizeIndex, Piece group, Component validatorC, Component[] answers, Mark[] marks, int[] pips, Material bare)
    {
        Piece board = P("Peg Board", pivotX, -2.32f, 1.3f).Named(name).Set("PuzzlePegBoard", "size", sizeIndex).Under(group);
        Component boardC = board.Component("PuzzlePegBoard");
        Sdk.Set(boardC, "validator", validatorC);
        Sdk.Add(boardC, "answerFrom", answers);
        var flaps = new int[answers.Length];
        for (int i = 0; i < flaps.Length; i++) flaps[i] = 1;
        Sdk.AddInts(boardC, "answerFromFlap", flaps);

        var mb = new MeshBuilder();
        for (int k = 0; k < answers.Length; k++)
            AddPlaque(mb, pivotX - SlotPitch * k, SlotPlaqueH, SackFaceZ, 1f, marks[k], pips[k]);
        OnPad(name.Replace("(", "").Replace(")", "") + " Plaques", mb, bare, false).Under(group);
    }

    // ---------------------------------------------------------------- structures

    // Santa's Long Hall: outer x 5.0..8.0, z -14.0..17.0, walls 0.3 sunk 0.5, a flat ceiling slab at 2.8 over all four
    // walls, solid gables, a sealed pitched roof overhanging 0.2; the only opening is the keyhole in the north end wall
    void BuildHall(Material snowy)
    {
        Color log = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.5f, top = 2.8f;
        var mb = new MeshBuilder();
        Block(mb, 5.0f, 5.3f, y0, top, -14.0f, 17.0f, log);                 // west
        Block(mb, 7.7f, 8.0f, y0, top, -14.0f, 17.0f, log);                 // east
        Block(mb, 5.3f, 7.7f, y0, top, -14.0f, -13.7f, log);                // south end: the Nice list's wall
        // north end, with the keyhole x 6.275..6.725, 1.3..1.7 high, through its whole thickness
        Block(mb, 5.3f, 6.275f, y0, top, 16.7f, 17.0f, log);
        Block(mb, 6.725f, 7.7f, y0, top, 16.7f, 17.0f, log);
        Block(mb, 6.275f, 6.725f, y0, 1.3f, 16.7f, 17.0f, log);
        Block(mb, 6.275f, 6.725f, 1.7f, top, 16.7f, 17.0f, log);
        // the ceiling slab: rests on all four walls to their outer faces, so the inside ends at 2.8 m whatever the roof does
        Block(mb, 5.0f, 8.0f, top, 3.0f, -14.0f, 17.0f, roof);
        // solid gables over both end walls, 3 m wide at 2.8, 3.6 at the ridge
        foreach (float gz in new[] { -14.0f, 16.7f })
            Extrude(mb, new[] { new Vector3(5.0f, top, gz), new Vector3(8.0f, top, gz), new Vector3(6.5f, 3.6f, gz) }, new Vector3(0f, 0f, 0.3f), log);
        // the roof: two slabs on the gables' slopes (extended 0.2 past the eaves), 0.15 thick, and a ridge cap
        Vector3 along = new Vector3(0f, 0f, 31.4f);
        Extrude(mb, new[] { new Vector3(4.8f, 2.693f, -14.2f), new Vector3(6.5f, 3.6f, -14.2f), new Vector3(6.5f, 3.75f, -14.2f), new Vector3(4.8f, 2.843f, -14.2f) }, along, roof);
        Extrude(mb, new[] { new Vector3(8.2f, 2.693f, -14.2f), new Vector3(6.5f, 3.6f, -14.2f), new Vector3(6.5f, 3.75f, -14.2f), new Vector3(8.2f, 2.843f, -14.2f) }, along, roof);
        Block(mb, 6.38f, 6.62f, 3.66f, 3.82f, -14.25f, 17.25f, roof);
        // a red sill band outside, for the look
        Block(mb, 4.96f, 5.0f, 0.0f, 0.35f, -14.0f, 17.0f, IsleKit.Red);
        Block(mb, 8.0f, 8.04f, 0.0f, 0.35f, -14.0f, 17.0f, IsleKit.Red);
        OnPad("Long Hall", mb, snowy);

        // fairy lights along both eaves
        var west = new List<Vector3>();
        var east = new List<Vector3>();
        for (int i = 0; i <= 6; i++)
        {
            float z = -14.2f + 31.4f * i / 6f;
            west.Add(new Vector3(4.75f, Floor + 2.6f, z));
            east.Add(new Vector3(8.25f, Floor + 2.6f, z));
        }
        Kit.LightString(b, "Hall Lights West", west, 0.1f, 0.5f, false, false, 81);
        Kit.LightString(b, "Hall Lights East", east, 0.1f, 0.5f, false, false, 82);
    }

    // The Lookout's hut: x 4.7..8.3, z 17.0..20.6 (its south wall is the hall's end wall), walls 0.2, 2.6 high, a doorway
    // 1.2 m wide in the east wall (z 18.2..19.4), a pitched roof with its ridge east-west at 3.4
    void BuildHut(Material snowy)
    {
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.1f, top = 2.6f;
        var mb = new MeshBuilder();
        Block(mb, 4.7f, 4.9f, y0, top, 16.7f, 20.6f, wall);                 // west
        Block(mb, 4.9f, 8.1f, y0, top, 20.4f, 20.6f, wall);                 // north
        Block(mb, 8.1f, 8.3f, y0, top, 16.7f, 18.2f, wall);                 // east, either side of the doorway
        Block(mb, 8.1f, 8.3f, y0, top, 19.4f, 20.6f, wall);
        Block(mb, 8.1f, 8.3f, 2.05f, top, 18.2f, 19.4f, wall);              // lintel
        Block(mb, 4.9f, 5.0f, y0, top, 16.7f, 17.0f, wall);                 // the corners against the hall
        Block(mb, 8.0f, 8.1f, y0, top, 16.7f, 17.0f, wall);
        // gables over the west and east walls, then the two roof slabs
        foreach (float gx in new[] { 4.7f, 8.1f })
            Extrude(mb, new[] { new Vector3(gx, top, 16.7f), new Vector3(gx, top, 20.6f), new Vector3(gx, 3.4f, 18.8f) }, new Vector3(0.2f, 0f, 0f), wall);
        Vector3 along = new Vector3(4.0f, 0f, 0f);
        Extrude(mb, new[] { new Vector3(4.5f, 2.511f, 16.8f), new Vector3(4.5f, 3.4f, 18.8f), new Vector3(4.5f, 3.55f, 18.8f), new Vector3(4.5f, 2.661f, 16.8f) }, along, roof);
        Extrude(mb, new[] { new Vector3(4.5f, 2.511f, 20.8f), new Vector3(4.5f, 3.4f, 18.8f), new Vector3(4.5f, 3.55f, 18.8f), new Vector3(4.5f, 2.661f, 20.8f) }, along, roof);
        Block(mb, 4.45f, 8.55f, 3.47f, 3.62f, 18.7f, 18.9f, roof);
        // a step and a red door frame at the doorway
        Block(mb, 8.3f, 8.9f, 0f, 0.08f, 18.0f, 19.6f, IsleKit.Stone);
        Block(mb, 8.3f, 8.36f, y0, 2.1f, 18.1f, 18.2f, IsleKit.Red);
        Block(mb, 8.3f, 8.36f, y0, 2.1f, 19.4f, 19.5f, IsleKit.Red);
        OnPad("Lookout Hut", mb, snowy);
    }

    // A Post Office booth: inside x -3.0..0.0, z -13.5..-10.5 (booth B), or its mirror about x -3.5 (booth C, x -7..-4).
    // Walls 0.2 and 2.9 high, roof 0.2; a 2.2 x 1.3 window in the north wall (sill 0.7, top 2.0), a doorway in the south
    // wall for the 1.24 m door, and the entry baffle from the far side wall to 1.0 m short of the near one, full height.
    // The shell is the only child of the soundproof parent (PuzzleSoundProof takes every collider under it).
    void BuildBooth(Material snowy, bool mirror, Piece group)
    {
        float M(float x) => mirror ? -7f - x : x;
        void Bx(MeshBuilder into, float xa, float xb, float ya, float yb, float za, float zb, Color c) => Block(into, M(xa), M(xb), ya, yb, za, zb, c);
        Color wall = IsleKit.Red, roof = IsleKit.DarkWood, trim = mirror ? IsleKit.Green : IsleKit.Cream;
        const float y0 = -0.1f, top = 2.9f;
        var mb = new MeshBuilder();
        Bx(mb, -3.0f, 0.0f, -0.13f, 0.02f, -13.5f, -10.5f, IsleKit.Wood);       // floor, closing the voice path under the walls
        Bx(mb, -3.2f, -3.0f, y0, top, -13.7f, -10.3f, wall);                   // west (B) / east (C)
        Bx(mb, 0.0f, 0.2f, y0, top, -13.7f, -10.3f, wall);                     // east (B) / west (C): the lever's wall
        // north wall with the window x -2.6..-0.4
        Bx(mb, -3.0f, -2.6f, y0, top, -10.5f, -10.3f, wall);
        Bx(mb, -0.4f, 0.0f, y0, top, -10.5f, -10.3f, wall);
        Bx(mb, -2.6f, -0.4f, y0, 0.7f, -10.5f, -10.3f, wall);
        Bx(mb, -2.6f, -0.4f, 2.0f, top, -10.5f, -10.3f, wall);
        // south wall with the doorway x -2.37..-1.13 and a lintel from 2.04
        Bx(mb, -3.0f, -2.37f, y0, top, -13.7f, -13.5f, wall);
        Bx(mb, -1.13f, 0.0f, y0, top, -13.7f, -13.5f, wall);
        Bx(mb, -2.37f, -1.13f, 2.04f, top, -13.7f, -13.5f, wall);
        // the entry baffle: z -12.75..-12.65 from the side wall to x -1.0, leaving the 1.0 m gap by the lever's wall
        Bx(mb, -3.0f, -1.0f, y0, top, -12.75f, -12.65f, IsleKit.Cream);
        Bx(mb, -3.35f, 0.35f, top, top + 0.2f, -13.85f, -10.15f, roof);        // roof
        Piece shellParent = b.Empty(mirror ? "Post Office C" : "Post Office B", M(-1.5f), -12.0f, Floor, false);
        Component soundproof = b.AddScript(shellParent, "PuzzleSoundProof");
        Sdk.Set(soundproof, "material", 0);
        OnPad(mirror ? "Booth C Shell" : "Booth B Shell", mb, snowy).Under(shellParent);
        if (group != null) shellParent.Under(group);

        // outside trim (not soundproof): a ledge under the window, a sign board over it, corner posts
        var dress = new MeshBuilder();
        Bx(dress, -2.65f, -0.35f, 0.62f, 0.7f, -10.3f, -10.15f, trim);
        Bx(dress, -2.3f, -0.7f, 2.2f, 2.7f, -10.3f, -10.26f, trim);
        Bx(dress, -2.2f, -0.8f, 2.4f, 2.5f, -10.26f, -10.24f, mirror ? IsleKit.Gold : IsleKit.Red);
        foreach (float px in new[] { -3.25f, 0.25f })
            foreach (float pz in new[] { -13.75f, -10.25f })
                Bx(dress, px - 0.06f, px + 0.06f, y0, top, pz - 0.06f, pz + 0.06f, trim);
        Bx(dress, -2.47f, -1.03f, 2.04f, 2.16f, -13.76f, -13.7f, trim);          // over the door
        Piece dressing = OnPad(mirror ? "Booth C Trim" : "Booth B Trim", dress, snowy, false);
        if (group != null) dressing.Under(group);

        // lights along the booth's front eave
        Piece lights = Kit.LightString(b, mirror ? "Booth C Lights" : "Booth B Lights",
            new List<Vector3> { new Vector3(M(-3.3f), Floor + 2.95f, -10.1f), new Vector3(M(0.3f), Floor + 2.95f, -10.1f) }, 0.15f, 0.45f, false, false, mirror ? 84 : 83);
        if (group != null) lights.Under(group);
    }

    // The yard: the Toy Shelf, the Sleigh with its sack panel, the bell post, the walkie table, the lights round it all
    void BuildYard(Material snowy)
    {
        var mb = new MeshBuilder();
        // Toy Shelf, x -4.6..-1.2: a stepped block, lower tier top 0.55 (z -8.95..-8.65), upper top 0.95 (z -8.55..-8.25),
        // with a lip on the lower tier's front edge
        Block(mb, -4.6f, -1.2f, -0.05f, 0.55f, -8.95f, -8.55f, IsleKit.Wood);
        Block(mb, -4.6f, -1.2f, -0.05f, 0.95f, -8.55f, -8.25f, IsleKit.Wood);
        Block(mb, -4.6f, -1.2f, 0.55f, 0.61f, -8.97f, -8.93f, IsleKit.DarkWood);
        Block(mb, -4.7f, -4.6f, -0.05f, 1.1f, -8.97f, -8.25f, IsleKit.Red);           // end boards
        Block(mb, -1.2f, -1.1f, -0.05f, 1.1f, -8.97f, -8.25f, IsleKit.Red);

        // the Sleigh: a red tub on runners x -4.7..-0.1, z -3.8..-2.5, 1.2 high, gold rims, and the sack panel behind it
        // (z -2.46..-2.34, 2.8 high) whose north face carries the board, Validator, Timer and strikes
        foreach (float rz in new[] { -3.7f, -2.6f })
        {
            Block(mb, -5.0f, 0.1f, 0f, 0.1f, rz - 0.05f, rz + 0.05f, IsleKit.Gold);    // runner
            Block(mb, -5.1f, -5.0f, 0f, 0.45f, rz - 0.05f, rz + 0.05f, IsleKit.Gold);  // its curl
            foreach (float sx in new[] { -4.2f, -2.4f, -0.6f })
                Block(mb, sx - 0.04f, sx + 0.04f, 0.1f, 0.25f, rz - 0.04f, rz + 0.04f, IsleKit.DarkWood);
        }
        Block(mb, -4.7f, -0.1f, 0.25f, 1.2f, -3.8f, -2.5f, IsleKit.Red);
        Block(mb, -4.75f, -0.05f, 1.2f, 1.28f, -3.85f, -2.45f, IsleKit.Gold);
        Block(mb, -4.75f, -0.05f, 0.25f, 0.33f, -3.85f, -2.45f, IsleKit.Gold);
        Block(mb, -4.7f, -0.1f, 0f, 2.8f, -2.46f, -2.34f, IsleKit.Red);              // the sack panel
        Block(mb, -4.8f, 0.0f, 2.8f, 2.92f, -2.52f, -2.28f, IsleKit.Gold);
        // three small red "spoiled presents" beside the strike bar, so its fill reads as lives used
        foreach (float gx in new[] { -4.25f, -3.9f, -3.55f })
        {
            Block(mb, gx - 0.06f, gx + 0.06f, 2.55f, 2.67f, -2.34f, -2.22f, IsleKit.Red);
            Block(mb, gx - 0.015f, gx + 0.015f, 2.55f, 2.68f, -2.345f, -2.215f, IsleKit.Gold);
        }
        // the sack itself, heaped in the tub
        Kit.AddBall(mb, new Vector3(-3.4f, 1.45f, -3.15f), 0.55f, new Color(0.62f, 0.47f, 0.3f));
        Kit.AddBall(mb, new Vector3(-1.6f, 1.35f, -3.15f), 0.4f, IsleKit.Green);

        // the Sleigh Bell post (0.2 x 1.9 m) with a bell finial; the sleigh's lever faces the booths on its south face
        Block(mb, -2.5f, -2.3f, -0.05f, 1.9f, -4.7f, -4.5f, IsleKit.Wood);
        Kit.AddBall(mb, new Vector3(-2.4f, 2.02f, -4.6f), 0.12f, IsleKit.Gold);

        // the walkie table at (2.6, 4.2), 1.0 x 0.5, top 0.9
        Block(mb, 2.1f, 3.1f, 0.84f, 0.9f, 3.95f, 4.45f, IsleKit.Wood);
        foreach (float lx in new[] { 2.17f, 3.03f })
            foreach (float lz in new[] { 4.02f, 4.38f })
                Block(mb, lx - 0.03f, lx + 0.03f, -0.05f, 0.84f, lz - 0.03f, lz + 0.03f, IsleKit.DarkWood);
        OnPad("Yard", mb, snowy);

        // the lights round the yard and the booths (west of the hall, which has its own on its eaves)
        Kit.LightRing(b, "Site Lights", -2.4f, -4.25f, 13.6f, 22.5f, Floor + 2.6f, false, 88);
    }
}
