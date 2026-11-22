// Summit Shot: the Lob House (spec: puzzle-specs/SummitShot.md, revision 2, 2026-11-22).
//
// A long timber-and-glass house on the summit's crest. One blindfolded thrower stands in a closed timber pen and lobs
// snowballs over a 3.3 m wall into the right one of four hanging baskets (six with four players) in a glass gallery
// beyond it. The thrower cannot see the baskets even with the mask off, and hears only the spotter's intercom. The
// spotter, on a deck outside the gallery's glass, sees the baskets, each with a shape on a split-flap display, and
// every landing, but not which basket is wanted: that lamp lights only on a panel inside the caller's windowless hut,
// round the far side of the house, beside a copy of the basket's shape, and the panel's lamps change places every run
// (LobHouseControl). The caller calls the shape into their intercom, which plays only on the deck; the spotter finds
// that basket and steers the throw. A ball through the lit basket fills the bar and lights another; a ball through any
// other basket fails the streak, and three seconds later it starts again from nothing. 8 in a row (6 with two players,
// 10 with four) before the clock runs out wins the gourd. Missed balls roll down the gallery's sloped floor, through a
// slot under the wall and back to the tray at the thrower's feet. With four players a second hut and caller watch half
// the lamps; with two the lamps sit on the deck's rail. Anyone in the gallery or on the wall's cap stops the puzzle.
//
// Coordinates: the spec's own (x east, z north, metres; heights above the pad's top, every piece onGround: false on
// the one level pad), so every number below reads as the spec writes it. The spec's pad (x -24..12.5, z -8..24) is
// centred on (-5.75, 8); the root sits at that centre (the island levels a circle of radius Footprint.magnitude / 2
// round the root), so the helpers add (OX, OZ) to turn spec coordinates into the root's. Yaw: 0 faces +z, 90 +x,
// -90 -x, 180 -z.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SummitShotPuzzle : PuzzleDef
{
    public override string Name => "SummitShot";
    public override Vector2 Footprint => new Vector2(37f, 32f);
    public override Vector2? FixedSite => new Vector2(-2211.9f, -1922f);      // layout-final: the crest beside the summit

    // spec coordinates + (OX, OZ) = the root's local coordinates
    const float OX = 5.75f, OZ = -8f;
    const float Floor = 0.25f;           // the pad's top above the root

    // ---- the throw's layout (spec 4.0): layout A. A change of layout is an edit here and a rebuild ----
    const float WallSolid = 3.0f;        // the lob wall's solid top (the cap's flat underside)
    const float WallTop = 3.3f;          // the cap's top at the gallery face
    const float CapFrontTop = 3.05f;     // the cap's top at its front edge, overhanging the pen
    const float CapOver = 0.3f;          // how far the cap overhangs the pen
    const float ToeZ = -2.0f;
    const float RoofH = 6.5f;            // pen and gallery
    static readonly float[] BX = { -3.0f, 2.6f, -0.6f, 3.2f, -3.4f, 0.6f };
    static readonly float[] BZ = { 2.8f, 3.6f, 4.6f, 5.8f, 5.6f, 6.4f };
    static readonly float[] BH = { 1.6f, 2.2f, 1.2f, 1.8f, 2.3f, 1.5f };      // rim heights above the pad
    const float RimR = 0.42f, RimW = 0.06f, SkirtDeep = 0.6f, SkirtBottomR = 0.32f;
    const float DisplayOut = 0.71f;      // a basket display's pivot east of the basket's centre (its back clears the post)

    // ---- the house ----
    const float PenFlat = 0.2f;          // the pen's floor south of the toe mark
    const float PorchH = 2.8f;           // porch, vestibule and hut walls
    const float Deck = 1.4f;             // the spotter's deck top
    const float VestFloor = 0.55f;       // the gallery's exit vestibule floor
    const float HutH = 2.8f;
    static readonly Color Warm = new Color(1f, 0.9f, 0.75f);
    static readonly Color Timber = new Color(0.45f, 0.32f, 0.2f);

    PuzzleBuilder b;

    // ---------------------------------------------------------------- helpers (spec coordinates)

    Piece P(string prefab, float x, float z, float h, float yaw = 0f) => b.Part(prefab, x + OX, z + OZ, Floor + h, yaw, false);
    Piece E(string name, float x, float z, float h) => b.Empty(name, x + OX, z + OZ, Floor + h, false);

    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(OX, OZ, Floor, false);
        return piece;
    }

    // A block between two corners (x, height above the pad, z)
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A box with eight corners of its own: low and high in AddBox's order, (-x-z), (+x-z), (+x+z), (-x+z); same winding
    static void Hull(MeshBuilder mb, Vector3[] low, Vector3[] high, Color color)
    {
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }

    // A column over a rectangle from y0 up to a top with its own height at each corner (a sloped floor's cell)
    static void Column(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float t00, float t10, float t11, float t01, Color color)
    {
        Hull(mb,
            new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) },
            new[] { new Vector3(x0, t00, z0), new Vector3(x1, t10, z0), new Vector3(x1, t11, z1), new Vector3(x0, t01, z1) }, color);
    }

    // Both faces of a quad (thin shells balls hit from inside and players see from outside)
    static void TwoSided(MeshBuilder mb, Vector3 a, Vector3 c, Vector3 d, Vector3 e, Color color)
    {
        mb.Quad(a, c, d, e, color);
        mb.Quad(a, e, d, c, color);
    }

    // A Presence Sensor with a box collider: centre (x, z, height above the pad), size in Unity's (x, y, z)
    Piece Sensor(string name, float x, float z, float h, Vector3 size)
    {
        Piece zone = P("Presence Sensor", x, z, h).Named(name);
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        return zone;
    }

    static void AddBoxCollider(GameObject go, Vector3 centre, Vector3 size, bool first)
    {
        BoxCollider box = first ? go.GetComponent<BoxCollider>() : null;
        if (box == null) box = go.AddComponent<BoxCollider>();
        box.center = centre;
        box.size = size;
    }

    Quaternion Turned(float yaw) => b.Root.transform.rotation * Quaternion.Euler(0f, yaw, 0f);

    // The pen floor's sloped band (z ToeZ..0): PenFlat along the toe mark, falling to 0.06 + 0.028|x| at the wall's foot
    static float PenY(float x, float z)
    {
        float foot = 0.06f + 0.028f * Mathf.Abs(x);
        return foot + (PenFlat - foot) * Mathf.Clamp01(-z / -ToeZ);
    }

    // The gallery floor: 5 % up towards the north, 4 % up away from x = 0 (0.03 at its south edge), and the gutter
    static float GalleryY(float x, float z) => 0.03f + 0.05f * (z - 0.6f) + 0.04f * Mathf.Abs(x);
    static float GutterY(float x) => 0.02f * Mathf.Abs(x);

    // ---------------------------------------------------------------- build

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(38f, 34f);
        b.DebugTeleport(8f + OX, 10.8f + OZ);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks over the house: nobody flies onto the roofs, round the glass or over the huts
        IsleKit.CalmAir(b, "Summit Shot", 0f, 0f, new Vector3(50f, 150f, 46f), 0f, -10f, false);

        Piece two = IsleKit.Lobby(b, "Two", true, false, false);
        Piece callers = IsleKit.Lobby(b, "Callers", false, true, true);
        Piece three = IsleKit.Lobby(b, "Three", false, true, false);
        Piece four = IsleKit.Lobby(b, "Four", false, false, true);

        // ---- the pad: one level slab, sunk 1.5 m ----
        var pad = new MeshBuilder();
        Block(pad, -24f, 12.5f, -1.75f, 0f, -8f, 24f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildFloors(bare);
        BuildShell(snowy);
        BuildGallery(bare);
        BuildDeck(snowy);
        BuildTileCase(snowy);
        Piece westHut = BuildWestHut(snowy);
        westHut.Under(callers);
        Piece northHut = BuildNorthHut(snowy);
        northHut.Under(four);

        // ---- baskets: rims, skirts, posts, display caps (B1..B4 shared, B5 and B6 in "Four") ----
        OnPad("Baskets", Baskets(0, 4), snowy);
        OnPad("Baskets Four", Baskets(4, 6), snowy).Under(four);

        // ---- snowballs: twelve, in and beside the tray ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Object>();
        for (int k = 0; k < 12; k++)
        {
            float x = -0.75f + 0.3f * (k % 6), z = k < 6 ? -0.3f : -0.65f;
            Piece ball = b.Item("Snowball " + (k + 1), Kit.Ball(0.14f, 8, 12, 0.1f, 4100 + k), snow, x + OX, z + OZ, Floor + 0.15f, 0.6f, false);
            balls.Add(ball.Component("PuzzleItem"));
        }

        // ---- basket displays and zones ----
        var displays = new Component[6];
        var zones = new Component[6];
        for (int i = 0; i < 6; i++)
        {
            Piece display = P("Display", BX[i] + DisplayOut, BZ[i], BH[i] - 0.55f, 90f).Named("Basket Display " + (i + 1))
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", true);
            // the zone: 0.2 tall, 0.15..0.35 below the rim, wholly inside the skirt; only these twelve snowballs count
            Piece zone = Sensor("Basket " + (i + 1), BX[i], BZ[i], BH[i] - 0.25f, new Vector3(0.5f, 0.2f, 0.5f))
                .Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            zone.Add("PuzzlePresenceSensor", "acceptedCustom", balls.ToArray());
            if (i >= 4) { display.Under(four); zone.Under(four); }
            displays[i] = display.Component("PuzzleDisplay");
            zones[i] = zone.Component("PuzzlePresenceSensor");
        }

        // ---- the shape tiles: the deck the displays deal from, sealed in the tile case ----
        for (int k = 0; k < 8; k++)
        {
            float x = 1.35f + 0.55f * (k % 4), z = k < 4 ? 13.25f : 13.75f;
            P("Peg Tile", x, z, 0.69f, 180f).Named("Shape Tile " + (k + 1))
                .Set("PuzzlePegTile", "imageSet", 0).Set("PuzzlePegTile", "image", k + 1);      // Shapes
        }

        // ---- turnstiles and masks ----
        IsleKit.BlindGate(b, -7.65f + OX, -4.0f + OZ, 0f, Floor, false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        IsleKit.BlindGate(b, 7.55f + OX, -4.0f + OZ, 180f, Floor, false).Named("Way Out").Set("PuzzleNoItemsGate", "defaultLocked", false);
        P("Gate No items", -0.5f, 9.36f, VestFloor, -90f).Named("Gallery Exit")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        for (int i = 0; i < 4; i++) IsleKit.BlindfoldHook(b, -12.5f + i * 1.1f + OX, -6.0f + OZ, 0f, Floor, false, " " + (i + 1));
        IsleKit.BlindfoldHook(b, 6.45f + OX, -2.6f + OZ, 90f, Floor, false, " Spare 1");
        IsleKit.BlindfoldHook(b, 6.45f + OX, -2.0f + OZ, 90f, Floor, false, " Spare 2");

        // ---- the intercom to the pen, the deck's shared instruments ----
        Piece penSpeaker = P("Intercom Speaker", 0f, -3.85f, PenFlat + 2.2f, 0f).Named("Pen Speaker");
        Piece spotterMic = P("Intercom Mic", 6.0f, 5.6f, Deck + 1.05f, 90f).Named("Spotter Mic");
        spotterMic.Add("PuzzleIntercom", "speakers", penSpeaker.Component("PuzzleIntercomSpeaker"));

        Piece countDeck = P("Progress Bar", 7.5f, 1.6f, Deck + 2.0f, 0f).Named("Count Bar Deck").Scaled(0.8f, 0.8f, 0.8f)
            .Set("PuzzleProgressBar", "total", 8).Set("PuzzleProgressBar", "style", 0);
        Piece retry = P("Timer", 6.15f, 2.6f, Deck + 1.3f, 90f).Named("Retry Clock").Set("PuzzleTimer", "duration", 3f);
        Piece againDeck = P("Button Poke", 5.99f, 7.0f, Deck + 1.1f, 90f).Named("Again Deck").Set("PuzzlePokeButton", "autoEnableSeconds", 5f);

        IsleKit.StartCorner(b, 8f + OX, 13f + OZ, 180f, true, Floor, false);

        // ---- the house's radio dead zone: pen and porches, gallery, exit vestibule (not the deck) ----
        Piece dead = P("Radio Dead Zone", 0f, 0f, 0f).Named("House Dead Zone");
        AddBoxCollider(dead.Go, new Vector3(1.0f, 3.25f, -2.25f), new Vector3(20.5f, 6.5f, 5.0f), true);
        AddBoxCollider(dead.Go, new Vector3(0f, 3.25f, 4.25f), new Vector3(10.5f, 6.5f, 8.0f), false);
        AddBoxCollider(dead.Go, new Vector3(1.6f, 1.5f, 9.6f), new Vector3(7.0f, 3.0f, 2.7f), false);

        Piece penLamp = P("Light 2", 0f, -2.0f, RoofH - 0.1f).Named("Pen Lamp").Set("PuzzleLight", "color", Warm);

        // ---- the guards: anyone in the gallery or on the wall's cap stops the puzzle ----
        Piece galleryGuard = Sensor("Gallery Guard", 0f, 4.1f, RoofH / 2f, new Vector3(10.0f, RoofH, 7.75f))
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        float guardLow = WallTop - 0.2f;
        Piece wallGuard = Sensor("Wall Guard", 0f, -0.05f, (guardLow + RoofH) / 2f, new Vector3(10.0f, RoofH - guardLow, 0.7f))
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);

        // Panel shuffles and the guard's keep-stopped check (finds everything by name)
        Component control = b.AddCustomScript(E("Lob House Control", 0f, 0f, 0f), "LobHouseControl");

        // Where Winter Walk should not snow (MineDriver's "Indoors ..." convention)
        E("Indoors Lob House", 0f, 2f, RoofH / 2f).Transform.localScale = new Vector3(10.5f, RoofH, 12.5f);
        E("Indoors West Hut", -20.7f, 4f, HutH / 2f).Transform.localScale = new Vector3(4.9f, HutH, 3.5f);

        // ================================================================ lobby of two: the panel on the deck
        var deckLamps = new Object[4];
        for (int i = 0; i < 4; i++)
        {
            float z = 3.4f + 0.5f * i;
            Piece lamp = P("Light 8", 6.08f, z, Deck + 1.45f, 90f).Named("Deck Lamp " + (i + 1))
                .Set("PuzzleLight", "color", IsleKit.Gold).Set("PuzzleLight", "range", 0.3f).Under(two);
            P("Display", 6.15f, z, Deck + 1.0f, 90f).Named("Deck Key " + (i + 1)).Set("PuzzleDisplay", "copyDisplay", displays[i]).Under(two);
            deckLamps[i] = lamp.Component("PuzzleLight");
        }
        Piece clock2 = P("Timer", 6.15f, 7.6f, Deck + 1.3f, 90f).Named("Run Clock 2").Set("PuzzleTimer", "duration", 300f).Under(two);
        Piece pio2Piece = E("PIO 2", 7.5f, 5f, Deck + 0.2f).Under(two);
        Component pio2 = b.AddScript(pio2Piece, "PuzzlePressInOrder");
        Sdk.Add(pio2, "lights", deckLamps);
        Sdk.Add(pio2, "buttons", zones[0], zones[1], zones[2], zones[3]);
        Sdk.Add(pio2, "progressBars", countDeck.Component("PuzzleProgressBar"));
        Sdk.Set(pio2, "rounds", 6);

        // ================================================================ callers (3 and 4): the West Hut's fixtures
        Piece westSpeaker = P("Intercom Speaker", 6.08f, 3.6f, Deck + 1.65f, 90f).Named("West Speaker").Under(callers);
        P("Intercom Mic", -19.45f, 5.25f, 1.25f, -90f).Named("West Mic").Under(callers)
            .Add("PuzzleIntercom", "speakers", westSpeaker.Component("PuzzleIntercomSpeaker"));
        Piece countWest = P("Progress Bar", -20.45f, 2.6f, 2.0f, 0f).Named("Count Bar West").Scaled(0.55f, 0.55f, 0.55f)
            .Set("PuzzleProgressBar", "total", 8).Set("PuzzleProgressBar", "style", 0).Under(callers);
        Piece clockBarWest = P("Progress Bar", -20.45f, 2.6f, 1.6f, 0f).Named("Clock Bar West").Scaled(0.55f, 0.55f, 0.55f)
            .Set("PuzzleProgressBar", "total", 8).Set("PuzzleProgressBar", "style", 0).Under(callers);
        P("Switch Start", -21.43f, 3.2f, 1.25f, 90f).Named("Start Hut").Under(callers);
        P("Live Light", -21.49f, 3.2f, 1.42f, 90f).Named("Hut Live").Under(callers);
        Piece againWest = P("Button Poke", -21.5f, 3.9f, 1.1f, 90f).Named("Again West").Set("PuzzlePokeButton", "autoEnableSeconds", 5f).Under(callers);
        Piece hutLampWest = P("Light 2", -20.45f, 4.0f, HutH - 0.2f).Named("Hut Lamp West").Set("PuzzleLight", "color", Warm).Under(callers);

        // ---- lobby of three: the West Panel's four pairs, its clock and its Press In Order ----
        Piece panel3 = Panel("West Panel 3", -19.45f, 3.9f, -90f, three);
        var westLamps = new Object[4];
        for (int i = 0; i < 4; i++)
            westLamps[i] = Pair(panel3, "West Pair " + (i + 1), "West Lamp " + (i + 1), "West Key " + (i + 1), -19.45f, 3.0f + 0.6f * i, -90f, displays[i]);
        Piece clock3 = P("Timer", 6.15f, 7.6f, Deck + 1.3f, 90f).Named("Run Clock 3").Set("PuzzleTimer", "duration", 360f).Under(three);
        Sdk.Add(clock3.Component("PuzzleTimer"), "progressBars", clockBarWest.Component("PuzzleProgressBar"));
        Piece pio3Piece = E("PIO 3", 7.5f, 5f, Deck + 0.2f).Under(three);
        Component pio3 = b.AddScript(pio3Piece, "PuzzlePressInOrder");
        Sdk.Add(pio3, "lights", westLamps);
        Sdk.Add(pio3, "buttons", zones[0], zones[1], zones[2], zones[3]);
        Sdk.Add(pio3, "progressBars", countDeck.Component("PuzzleProgressBar"), countWest.Component("PuzzleProgressBar"));
        Sdk.Set(pio3, "rounds", 8);

        // ================================================================ lobby of four
        Piece panel4 = Panel("West Panel 4", -19.45f, 3.9f, -90f, four);
        var lamps4 = new Object[6];
        for (int i = 0; i < 3; i++)
            lamps4[i] = Pair(panel4, "West Pair 4p " + (i + 1), "West Lamp 4p " + (i + 1), "West Key 4p " + (i + 1), -19.45f, 3.3f + 0.6f * i, -90f, displays[i]);
        Piece northPanel = Panel("North Panel", 0f, 19.45f, 0f, four);
        for (int i = 0; i < 3; i++)
            lamps4[3 + i] = Pair(northPanel, "North Pair " + (i + 4), "North Lamp " + (i + 4), "North Key " + (i + 4), -0.6f + 0.6f * i, 19.45f, 0f, displays[3 + i]);
        Piece northSpeaker = P("Intercom Speaker", 6.08f, 6.4f, Deck + 1.65f, 90f).Named("North Speaker").Under(four);
        P("Intercom Mic", 1.15f, 19.45f, 1.25f, 0f).Named("North Mic").Under(four)
            .Add("PuzzleIntercom", "speakers", northSpeaker.Component("PuzzleIntercomSpeaker"));
        Piece countNorth = P("Progress Bar", 1.45f, 20.45f, 2.0f, -90f).Named("Count Bar North").Scaled(0.55f, 0.55f, 0.55f)
            .Set("PuzzleProgressBar", "total", 10).Set("PuzzleProgressBar", "style", 0).Under(four);
        Piece clockBarNorth = P("Progress Bar", 1.45f, 20.45f, 1.6f, -90f).Named("Clock Bar North").Scaled(0.55f, 0.55f, 0.55f)
            .Set("PuzzleProgressBar", "total", 10).Set("PuzzleProgressBar", "style", 0).Under(four);
        Piece againNorth = P("Button Poke", -1.5f, 20.2f, 1.1f, 90f).Named("Again North").Set("PuzzlePokeButton", "autoEnableSeconds", 5f).Under(four);
        Piece hutLampNorth = P("Light 2", 0f, 20.45f, HutH - 0.2f).Named("Hut Lamp North").Set("PuzzleLight", "color", Warm).Under(four);
        Piece clock4 = P("Timer", 6.15f, 7.6f, Deck + 1.3f, 90f).Named("Run Clock 4").Set("PuzzleTimer", "duration", 420f).Under(four);
        Sdk.Add(clock4.Component("PuzzleTimer"), "progressBars", clockBarWest.Component("PuzzleProgressBar"), clockBarNorth.Component("PuzzleProgressBar"));
        Piece pio4Piece = E("PIO 4", 7.5f, 5f, Deck + 0.2f).Under(four);
        Component pio4 = b.AddScript(pio4Piece, "PuzzlePressInOrder");
        Sdk.Add(pio4, "lights", lamps4);
        Sdk.Add(pio4, "buttons", zones);
        Sdk.Add(pio4, "progressBars", countDeck.Component("PuzzleProgressBar"), countWest.Component("PuzzleProgressBar"), countNorth.Component("PuzzleProgressBar"));
        Sdk.Set(pio4, "rounds", 10);
        E("Indoors North Hut", 0f, 20.7f, HutH / 2f).Under(four).Transform.localScale = new Vector3(3.5f, HutH, 4.9f);

        // ================================================================ wiring
        Component sdk = b.Puzzle;
        Component retryC = retry.Component("PuzzleTimer");
        Component c2 = clock2.Component("PuzzleTimer"), c3 = clock3.Component("PuzzleTimer"), c4 = clock4.Component("PuzzleTimer");
        Component[] pios = { pio2, pio3, pio4 };
        Component[] clocks = { c2, c3, c4 };
        Component[] agains = { againDeck.Component("PuzzlePokeButton"), againWest.Component("PuzzlePokeButton"), againNorth.Component("PuzzlePokeButton") };
        Component[] roomLamps = { penLamp.Component("PuzzleLight"), hutLampWest.Component("PuzzleLight"), hutLampNorth.Component("PuzzleLight") };

        // each run: its own clock restarts (and, for the huts, the panel shuffles); a wrong basket retries after 3 s
        Sdk.Wire(pio2, "onBegin", c2, "ResetTimer");
        Sdk.Wire(pio2, "onBegin", c2, "StartTimer");
        Sdk.Wire(pio3, "onBegin", control, "Shuffle", "West Panel 3");
        Sdk.Wire(pio3, "onBegin", c3, "ResetTimer");
        Sdk.Wire(pio3, "onBegin", c3, "StartTimer");
        Sdk.Wire(pio4, "onBegin", control, "Shuffle", "West Panel 4");
        Sdk.Wire(pio4, "onBegin", control, "Shuffle", "North Panel");
        Sdk.Wire(pio4, "onBegin", c4, "ResetTimer");
        Sdk.Wire(pio4, "onBegin", c4, "StartTimer");
        foreach (Component pio in pios)
        {
            Sdk.Wire(pio, "onFail", retryC, "ResetTimer");
            Sdk.Wire(pio, "onFail", retryC, "StartTimer");
            retry.On("PuzzleTimer", "onFinished", pio, "Begin");      // only the lobby's own PIO does anything (hidden ones never started)
        }
        clock2.On("PuzzleTimer", "onFinished", pio2, "End");
        clock3.On("PuzzleTimer", "onFinished", pio3, "End");
        clock4.On("PuzzleTimer", "onFinished", pio4, "End");

        // Again: stop a pending retry first, so only one Begin runs
        againDeck.On("PuzzlePokeButton", "onPressed", retryC, "ResetTimer");
        foreach (Component pio in pios) againDeck.On("PuzzlePokeButton", "onPressed", pio, "Begin");
        againWest.On("PuzzlePokeButton", "onPressed", retryC, "ResetTimer");
        againWest.On("PuzzlePokeButton", "onPressed", pio3, "Begin");
        againWest.On("PuzzlePokeButton", "onPressed", pio4, "Begin");
        againNorth.On("PuzzlePokeButton", "onPressed", retryC, "ResetTimer");
        againNorth.On("PuzzlePokeButton", "onPressed", pio4, "Begin");

        // the full bar wins and holds the clock
        countDeck.On("PuzzleProgressBar", "onFull", sdk, "Win");
        foreach (Component clock in clocks) countDeck.On("PuzzleProgressBar", "onFull", clock, "PauseTimer");

        // the guards
        galleryGuard.On("PuzzlePresenceSensor", "onPressed", sdk, "StopPuzzle");
        wallGuard.On("PuzzlePresenceSensor", "onPressed", sdk, "StopPuzzle");

        // start and stop (stop also runs once at spawn, so the Again buttons start greyed out)
        Piece root = b.RootPiece;
        foreach (Component pio in pios) root.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        foreach (Component again in agains) root.On("PuzzleSDK", "onPuzzleStart", again, "SetClickable", true);
        foreach (Component lamp in roomLamps) root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
        foreach (Component pio in pios) root.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        foreach (Component clock in clocks) root.On("PuzzleSDK", "onPuzzleStop", clock, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", retryC, "ResetTimer");
        foreach (Component again in agains) root.On("PuzzleSDK", "onPuzzleStop", again, "SetClickable", false);
        foreach (Component lamp in roomLamps) root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");

        // ---- winter dressing ----
        var ring = new List<Vector3>();
        float[] ringX = { -23.5f, -17.5f, -11.5f, -5.75f, 0.25f, 6.25f, 12f };
        float[] ringZ = { -7.5f, -1.5f, 4.5f, 10.5f, 16.5f, 23.5f };
        foreach (float x in ringX) ring.Add(new Vector3(x + OX, Floor + 2.6f, -7.5f + OZ));
        for (int k = 1; k < ringZ.Length; k++) ring.Add(new Vector3(12f + OX, Floor + 2.6f, ringZ[k] + OZ));
        for (int k = ringX.Length - 2; k >= 0; k--) ring.Add(new Vector3(ringX[k] + OX, Floor + 2.6f, 23.5f + OZ));
        for (int k = ringZ.Length - 2; k >= 0; k--) ring.Add(new Vector3(-23.5f + OX, Floor + 2.6f, ringZ[k] + OZ));
        Kit.LightString(b, "Site Lights", ring, 0.15f, 0.5f, false, true, 311);
        Kit.LightString(b, "Deck Lights", new List<Vector3> { new Vector3(5.85f + OX, Floor + Deck + 2.5f, 1.4f + OZ), new Vector3(5.85f + OX, Floor + Deck + 2.5f, 8.1f + OZ) },
            0.1f, 0.4f, false, true, 312);
        Vector3[] drifts = { new Vector3(-20.2f, 1.3f, 0.8f), new Vector3(-17.4f, 6.8f, 0.9f), new Vector3(-23.6f, 7.0f, 1.0f), new Vector3(2.9f, 17.4f, 1.0f), new Vector3(-2.8f, 23.6f, 0.9f), new Vector3(10.5f, -3.0f, 1.1f) };
        for (int i = 0; i < drifts.Length; i++)
            b.MeshPiece("Lob House Drift " + (i + 1), Kit.Mound(drifts[i].z, 0.4f, 330 + i), snowy, false).Transform.position = b.WorldPoint(drifts[i].x + OX, drifts[i].y + OZ, Floor - 0.05f, false);
    }

    // A panel on a hut's wall: an empty turned to face the room, whose children are the pairs LobHouseControl moves
    Piece Panel(string name, float x, float z, float yaw, Piece group)
    {
        Piece panel = E(name, x, z, 1.0f);
        panel.Transform.rotation = Turned(yaw);
        panel.Under(group);
        return panel;
    }

    // One pair under a panel: an empty at the slot (panel depth and height) holding the lamp (above) and the key that
    // copies a basket's shape (below), mounted off the wall by their own depths. Returns the lamp's PuzzleLight.
    Object Pair(Piece panel, string pairName, string lampName, string keyName, float x, float z, float yaw, Component source)
    {
        Piece pair = E(pairName, x, z, 1.0f);
        pair.Transform.rotation = Turned(yaw);
        pair.Under(panel);
        Vector2 lampOff = IsleKit.Turn(0f, 0.05f, yaw), keyOff = IsleKit.Turn(0f, 0.13f, yaw);      // the lamp's back and the key's back on the wall
        Piece lamp = P("Light 8", x + lampOff.x, z + lampOff.y, 1.5f, yaw).Named(lampName)
            .Set("PuzzleLight", "color", IsleKit.Gold).Set("PuzzleLight", "range", 0.3f).Under(pair);
        P("Display", x + keyOff.x, z + keyOff.y, 1.0f, yaw).Named(keyName).Set("PuzzleDisplay", "copyDisplay", source).Under(pair);
        return lamp.Component("PuzzleLight");
    }

    // ---------------------------------------------------------------- geometry

    // The pen's floor (flat south of the toe mark, the sloped band to the wall round the tray), the toe mark, the
    // guide strip, the handrail, the porches' ramps; the gallery's floor and gutter; the vestibule's floor and steps
    void BuildFloors(Material bare)
    {
        var mb = new MeshBuilder();
        Color plank = Timber, mark = IsleKit.Gold;
        const float y0 = -0.05f;
        // pen: flat part and the doorways' sills
        Block(mb, -5f, 5f, y0, PenFlat, -4f, ToeZ, plank);
        Block(mb, -5.25f, -5f, y0, PenFlat, -3.6f, -2.7f, plank);
        Block(mb, 5f, 5.25f, y0, PenFlat, -3.6f, -2.7f, plank);
        // pen: the sloped band, leaving the tray (x -0.9..0.9, z -0.9..0) at the pad's level
        float[] xs = { -5f, -4f, -3f, -2f, -0.9f, 0f, 0.9f, 2f, 3f, 4f, 5f };
        float[] zs = { ToeZ, -1.5f, -0.9f, -0.45f, 0f };
        for (int i = 0; i + 1 < xs.Length; i++)
            for (int j = 0; j + 1 < zs.Length; j++)
            {
                float xa = xs[i], xb = xs[i + 1], za = zs[j], zb = zs[j + 1];
                if (xa >= -0.9f && xb <= 0.9f && za >= -0.9f) continue;      // the tray
                Column(mb, xa, xb, za, zb, y0, PenY(xa, za), PenY(xb, za), PenY(xb, zb), PenY(xa, zb), plank);
            }
        // the toe mark (two halves either side of a notch at x = 0) and the guide strip from the handrail's middle
        Block(mb, -0.3f, -0.05f, PenFlat, PenFlat + 0.03f, ToeZ - 0.06f, ToeZ + 0.06f, mark);
        Block(mb, 0.05f, 0.3f, PenFlat, PenFlat + 0.03f, ToeZ - 0.06f, ToeZ + 0.06f, mark);
        Block(mb, -0.15f, 0.15f, PenFlat, PenFlat + 0.03f, -3.8f, ToeZ - 0.1f, mark);
        // the handrail along the south wall, a ridge on top so nothing stands on it, from each side wall to x = 0
        foreach (float side in new[] { -1f, 1f })
        {
            float xa = side < 0 ? -4.95f : 0.05f, xb = side < 0 ? -0.05f : 4.95f;
            float top = PenFlat + 0.9f;
            Hull(mb,
                new[] { new Vector3(xa, top - 0.08f, -3.89f), new Vector3(xb, top - 0.08f, -3.89f), new Vector3(xb, top - 0.08f, -3.81f), new Vector3(xa, top - 0.08f, -3.81f) },
                new[] { new Vector3(xa, top, -3.852f), new Vector3(xb, top, -3.852f), new Vector3(xb, top, -3.848f), new Vector3(xa, top, -3.848f) }, IsleKit.Red);
            for (int k = 0; k < 3; k++)
            {
                float bx = Mathf.Lerp(xa + 0.3f, xb - 0.3f, k / 2f);
                Block(mb, bx - 0.03f, bx + 0.03f, top - 0.14f, top - 0.08f, -4f, -3.85f, IsleKit.DarkWood);
            }
        }
        // the porches' corridors rise to the pen's floor at the doorways
        foreach (float side in new[] { -1f, 1f })
        {
            float xa = side < 0 ? -6.2f : 5.25f, xb = side < 0 ? -5.25f : 6.2f;
            Block(mb, xa, xb, y0, PenFlat, -4f, -2.6f, plank);
            Column(mb, xa, xb, -2.6f, -1.2f, y0, PenFlat, PenFlat, 0f, 0f, plank);
        }

        // gallery: the gutter along the wall's foot, then the floor in 1 m cells (split at x = 0 for the cross-fall)
        float[] gx = { -5f, -4f, -3f, -2f, -1f, -0.6f, 0f, 0.6f, 1f, 2f, 3f, 4f, 5f };
        for (int i = 0; i + 1 < gx.Length; i++)
        {
            float xa = gx[i], xb = gx[i + 1];
            Column(mb, xa, xb, 0.25f, 0.6f, y0, GutterY(xa), GutterY(xb), GutterY(xb), GutterY(xa), plank);
        }
        float[] gz = { 0.6f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.25f, 8.0f };
        for (int i = 0; i + 1 < gx.Length; i++)
            for (int j = 0; j + 1 < gz.Length; j++)
            {
                float xa = gx[i], xb = gx[i + 1], za = gz[j], zb = gz[j + 1];
                Column(mb, xa, xb, za, zb, y0, GalleryY(xa, za), GalleryY(xb, za), GalleryY(xb, zb), GalleryY(xa, zb), plank);
            }
        // the return slot's floor under the wall is the pad itself (x -0.6..0.6, z 0..0.25)

        // vestibule: floor level with the gallery's at the doorway, the doorway's sill, steps down at the open west end
        Block(mb, -1.6f, 4.6f, y0, VestFloor, 8.0f, 10.45f, plank);
        Block(mb, 3.4f, 4.4f, VestFloor, VestFloor + 0.25f, 8.0f, 8.25f, IsleKit.DarkWood);
        Block(mb, -2.0f, -1.6f, y0, VestFloor * 2f / 3f, 8.12f, 10.45f, IsleKit.Stone);
        Block(mb, -2.4f, -2.0f, y0, VestFloor / 3f, 8.12f, 10.45f, IsleKit.Stone);
        OnPad("House Floors", mb, bare);
    }

    // The timber shell: pen walls and roof, lob wall and cap, both porches with their baffles and turnstile hoods, the
    // exit vestibule's walls, baffle, roof and turnstile hood. Drawn once with its colliders (balls and players), and a
    // collider-only copy under "Lob House" (PuzzleSoundProof), so the voice walls never change how the balls bounce.
    void BuildShell(Material snowy)
    {
        var mb = new MeshBuilder();      // walls that close the house: these block voices too
        Color wall = Timber, roof = IsleKit.DarkWood, trim = IsleKit.Red;
        const float y0 = -0.1f;
        float doorTop = PenFlat + 2.2f;

        // pen: west and east walls with their doorways (z -3.6..-2.7) at the south end, the south wall, the roof
        foreach (float side in new[] { -1f, 1f })
        {
            float xa = side < 0 ? -5.25f : 5f, xb = side < 0 ? -5f : 5.25f;
            Block(mb, xa, xb, y0, RoofH, -4.25f, -3.6f, wall);
            Block(mb, xa, xb, y0, RoofH, -2.7f, 0.25f, wall);
            Block(mb, xa, xb, doorTop, RoofH, -3.6f, -2.7f, wall);
        }
        Block(mb, -5.25f, 5.25f, y0, RoofH, -4.25f, -4f, wall);
        Block(mb, -5.4f, 5.4f, RoofH, RoofH + 0.2f, -4.4f, 0.25f, roof);

        // porches: inside x -9.0..-5.25 (west) and 5.25..9.0 (east), z -4..-0.5, a baffle 0.95 m from the pen wall
        foreach (float side in new[] { -1f, 1f })
        {
            float S(float x) => side * x;
            float Lo(float a, float c) => Mathf.Min(S(a), S(c));
            float Hi(float a, float c) => Mathf.Max(S(a), S(c));
            float gateX = side < 0 ? -7.65f : 7.55f;
            float gateA = side < 0 ? gateX - 1.11f : gateX - 1.01f, gateB = side < 0 ? gateX + 1.01f : gateX + 1.11f;      // the frame, turned by yaw 0 / 180
            Block(mb, Lo(9.25f, 9.0f), Hi(9.25f, 9.0f), y0, PorchH, -4.25f, -0.25f, wall);           // outer side
            Block(mb, Lo(9.25f, 5.25f), Hi(9.25f, 5.25f), y0, PorchH, -0.5f, -0.25f, wall);          // north
            Block(mb, Lo(9.25f, 5.25f), gateA, y0, PorchH, -4.25f, -4f, wall);                         // south, either side of the turnstile
            Block(mb, gateB, Hi(9.25f, 5.25f), y0, PorchH, -4.25f, -4f, wall);
            Block(mb, gateA, gateB, 2.3f, PorchH, -4.25f, -3.3f, wall);                               // the turnstile's hood
            Block(mb, gateA - 0.06f, gateA, y0, 2.3f, -4.25f, -4f, trim);                             // its jambs
            Block(mb, gateB, gateB + 0.06f, y0, 2.3f, -4.25f, -4f, trim);
            Block(mb, Lo(6.35f, 6.2f), Hi(6.35f, 6.2f), y0, PorchH, -4f, -1.8f, wall);                // the baffle
            Block(mb, Lo(9.4f, 5.25f), Hi(9.4f, 5.25f), PorchH, PorchH + 0.2f, -4.4f, -0.1f, roof);
        }

        // the exit vestibule: inside x -1.6..4.6, z 8.12 (the gallery's glass) to 10.45, open to the west
        Block(mb, -1.6f, 4.85f, y0, VestFloor + PorchH, 10.45f, 10.7f, wall);                         // north
        Block(mb, 4.6f, 4.85f, y0, VestFloor + PorchH, 8.12f, 10.7f, wall);                           // east
        Block(mb, 1.5f, 1.65f, VestFloor, VestFloor + PorchH, 8.12f, 10.0f, wall);                    // the baffle
        Block(mb, -1.75f, 5.0f, VestFloor + PorchH, VestFloor + PorchH + 0.2f, 8.12f, 10.85f, roof);
        Block(mb, -1.2f, 0.2f, VestFloor + 2.3f, VestFloor + PorchH, 8.12f, 10.45f, wall);           // the turnstile's hood
        Block(mb, -1.2f, 0.2f, VestFloor, VestFloor + 2.3f, 10.37f, 10.45f, trim);                   // the stub to the north wall
        OnPad("Lob House Shell", mb, snowy);

        Piece house = E("Lob House", 0f, 0f, 0f);
        Sdk.Set(b.AddScript(house, "PuzzleSoundProof"), "material", 0);      // wood
        Piece voice = OnPad("Lob House Voice Shell", mb, snowy);
        Object.DestroyImmediate(voice.Go.GetComponent<MeshRenderer>());
        voice.Under(house);

        // inside the sealed volume (no soundproofing needed): the lob wall with its return slot, and the sloped cap
        var inner = new MeshBuilder();
        Block(inner, -5f, -0.6f, y0, WallSolid, 0f, 0.25f, wall);
        Block(inner, 0.6f, 5f, y0, WallSolid, 0f, 0.25f, wall);
        Block(inner, -0.6f, 0.6f, 0.32f, WallSolid, 0f, 0.25f, wall);
        Hull(inner,
            new[] { new Vector3(-5f, WallSolid, -CapOver), new Vector3(5f, WallSolid, -CapOver), new Vector3(5f, WallSolid, 0.25f), new Vector3(-5f, WallSolid, 0.25f) },
            new[] { new Vector3(-5f, CapFrontTop, -CapOver), new Vector3(5f, CapFrontTop, -CapOver), new Vector3(5f, WallTop, 0.25f), new Vector3(-5f, WallTop, 0.25f) }, trim);
        // the tray's back: a red band on the wall over the slot, so the slot reads from the pen
        Block(inner, -0.7f, 0.7f, 0.32f, 0.42f, -0.02f, 0f, IsleKit.Gold);
        OnPad("Lob Wall", inner, snowy);
    }

    // The gallery's glass (soundproof, clear) and its frame
    void BuildGallery(Material bare)
    {
        var glass = new MeshBuilder();
        const float g = 0.12f, y0 = -0.1f;
        float sill = VestFloor, head = VestFloor + 2.2f;
        Block(glass, -5f - g, -5f, y0, RoofH, 0.25f, 8.0f + g, Color.white);       // west
        Block(glass, 5f, 5f + g, y0, RoofH, 0.25f, 8.0f + g, Color.white);         // east
        Block(glass, -5f, 3.4f, y0, RoofH, 8.0f, 8.0f + g, Color.white);           // north, with the vestibule's doorway x 3.4..4.4
        Block(glass, 4.4f, 5f, y0, RoofH, 8.0f, 8.0f + g, Color.white);
        Block(glass, 3.4f, 4.4f, y0, sill, 8.0f, 8.0f + g, Color.white);
        Block(glass, 3.4f, 4.4f, head, RoofH, 8.0f, 8.0f + g, Color.white);
        Block(glass, -5f - g, 5f + g, RoofH, RoofH + g, 0.25f, 8.0f + g, Color.white);      // roof
        Piece pane = OnPad("Gallery Glass", glass, bare);
        Component glassC = b.AddScript(pane, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // clear
        Sdk.Set(glassC, "sound", 2);      // soundproof
        Sdk.Set(glassC, "wire", false);

        var frame = new MeshBuilder();
        foreach (float x in new[] { -5.06f, 5.06f })
        {
            Block(frame, x - 0.15f, x + 0.15f, y0, RoofH + 0.3f, 7.97f, 8.27f, IsleKit.Red);
            Block(frame, x - 0.1f, x + 0.1f, RoofH + g + 0.01f, RoofH + 0.3f, 0.25f, 8.24f, IsleKit.Gold);
        }
        Block(frame, -5.18f, 5.18f, RoofH + g + 0.01f, RoofH + 0.3f, 8.0f, 8.24f, IsleKit.Gold);
        Block(frame, 3.3f, 3.4f, sill, head, 7.97f, 8.15f, IsleKit.Red);      // the doorway's jambs
        Block(frame, 4.4f, 4.5f, sill, head, 7.97f, 8.15f, IsleKit.Red);
        OnPad("Gallery Frame", frame, IsleKit.Snowy(b));
    }

    // Baskets from..to-1: a rim of 16 thin segments, an open skirt narrowing downwards (both sides drawn), a post on the
    // east side, and a sloped cap over its display so no ball rests anywhere but the floor
    MeshBuilder Baskets(int from, int to)
    {
        var mb = new MeshBuilder();
        const int Segs = 16;
        for (int i = from; i < to; i++)
        {
            float cx = BX[i], cz = BZ[i], rim = BH[i];
            for (int s = 0; s < Segs; s++)
            {
                float a = 360f * s / Segs, r = RimR + RimW / 2f;
                Vector3 foot = new Vector3(cx + r * Mathf.Cos(a * Mathf.Deg2Rad), rim - RimW, cz + r * Mathf.Sin(a * Mathf.Deg2Rad));
                Kit.AddBox(mb, foot, new Vector3(2f * Mathf.PI * r / Segs + 0.02f, RimW, RimW), IsleKit.Red, 90f - a);
            }
            float top = rim - RimW, bottom = rim - SkirtDeep;
            for (int s = 0; s < Segs; s++)
            {
                float a0 = 2f * Mathf.PI * s / Segs, a1 = 2f * Mathf.PI * (s + 1) / Segs;
                Vector3 u0 = new Vector3(cx + RimR * Mathf.Cos(a0), top, cz + RimR * Mathf.Sin(a0));
                Vector3 u1 = new Vector3(cx + RimR * Mathf.Cos(a1), top, cz + RimR * Mathf.Sin(a1));
                Vector3 d0 = new Vector3(cx + SkirtBottomR * Mathf.Cos(a0), bottom, cz + SkirtBottomR * Mathf.Sin(a0));
                Vector3 d1 = new Vector3(cx + SkirtBottomR * Mathf.Cos(a1), bottom, cz + SkirtBottomR * Mathf.Sin(a1));
                TwoSided(mb, d0, d1, u1, u0, IsleKit.Gold);
            }
            // the post: from the floor to the rim's underside, just outside the rim on the east
            float px = cx + RimR + RimW * 0.5f;
            float floorY = GalleryY(px, cz) - 0.05f;
            Block(mb, px - 0.03f, px + 0.03f, floorY, top, cz - 0.03f, cz + 0.03f, IsleKit.DarkWood);
            // an arm from the post to the display's back, and a cap sloping east over the display's top
            float dispTop = rim - 0.55f + 0.21f, back = cx + DisplayOut - 0.175f;
            Block(mb, px + 0.03f, back, dispTop - 0.2f, dispTop - 0.12f, cz - 0.03f, cz + 0.03f, IsleKit.DarkWood);
            float capW = cx + DisplayOut + 0.05f;
            Hull(mb,
                new[] { new Vector3(px - 0.03f, dispTop, cz - 0.24f), new Vector3(capW, dispTop, cz - 0.24f), new Vector3(capW, dispTop, cz + 0.24f), new Vector3(px - 0.03f, dispTop, cz + 0.24f) },
                new[] { new Vector3(px - 0.03f, dispTop + 0.2f, cz - 0.24f), new Vector3(capW, dispTop + 0.01f, cz - 0.24f), new Vector3(capW, dispTop + 0.01f, cz + 0.24f), new Vector3(px - 0.03f, dispTop + 0.2f, cz + 0.24f) },
                IsleKit.Red);
        }
        return mb;
    }

    // The spotter's deck: x 6..9, z 1.5..8, top 1.4; stairs down at the north end; the rail along x 6 with instrument
    // boards only behind the parts (so the gallery is seen over the rail between them); the count board at the south end
    void BuildDeck(Material snowy)
    {
        var mb = new MeshBuilder();
        Color wood = Timber, dark = IsleKit.DarkWood, board = IsleKit.Board;
        Block(mb, 6f, 9f, -0.1f, Deck, 1.5f, 8.0f, wood);
        for (int k = 0; k < 5; k++)
            Block(mb, 6f, 9f, -0.1f, Deck * (5 - k) / 6f, 8.0f + 0.5f * k, 8.5f + 0.5f * k, wood);
        // the rail: posts and a top bar
        for (int k = 0; k <= 5; k++)
        {
            float z = Mathf.Lerp(1.55f, 7.95f, k / 5f);
            Block(mb, 5.92f, 6.0f, Deck, Deck + 1.0f, z - 0.04f, z + 0.04f, dark);
        }
        Block(mb, 5.92f, 6.0f, Deck + 0.94f, Deck + 1.02f, 1.5f, 8.0f, dark);
        // instrument boards behind the parts (x 5.92..5.98): retry clock, lamps and keys, speakers, mic, Again, run clock
        Block(mb, 5.92f, 5.98f, Deck + 1.0f, Deck + 1.6f, 2.25f, 2.95f, board);
        Block(mb, 5.92f, 5.98f, Deck + 0.8f, Deck + 1.6f, 3.15f, 5.15f, board);
        Block(mb, 5.92f, 5.98f, Deck + 1.6f, Deck + 2.0f, 3.4f, 3.8f, board);
        Block(mb, 5.92f, 5.98f, Deck + 0.9f, Deck + 1.3f, 5.4f, 5.8f, board);
        Block(mb, 5.92f, 5.98f, Deck + 1.0f, Deck + 2.0f, 6.2f, 6.6f, board);
        Block(mb, 5.92f, 5.98f, Deck + 0.9f, Deck + 1.3f, 6.8f, 7.2f, board);
        Block(mb, 5.92f, 5.98f, Deck + 1.0f, Deck + 1.6f, 7.25f, 7.95f, board);
        // the count board on posts at the south end, facing north (its face at z 1.5)
        Block(mb, 6.0f, 9.0f, Deck + 1.72f, Deck + 2.28f, 1.38f, 1.5f, board);
        Block(mb, 6.0f, 6.12f, Deck, Deck + 2.4f, 1.38f, 1.5f, dark);
        Block(mb, 8.88f, 9.0f, Deck, Deck + 2.4f, 1.38f, 1.5f, dark);
        Block(mb, 5.9f, 9.1f, Deck + 2.4f, Deck + 2.5f, 1.2f, 1.7f, dark);
        OnPad("Spotter Deck", mb, snowy);
    }

    // The tile case: a stone plinth and a sealed glass box (rippled) round the eight shape tiles
    void BuildTileCase(Material snowy)
    {
        var plinth = new MeshBuilder();
        Block(plinth, 0.85f, 3.55f, -0.1f, 0.45f, 12.8f, 14.2f, IsleKit.Stone);
        OnPad("Tile Case Plinth", plinth, snowy);
        var glass = new MeshBuilder();
        const float t = 0.05f, x0 = 1.025f, x1 = 3.375f, z0 = 12.975f, z1 = 14.025f, y0 = 0.45f, y1 = 1.05f;
        Block(glass, x0 - t, x1 + t, y0, y1, z0 - t, z0, Color.white);
        Block(glass, x0 - t, x1 + t, y0, y1, z1, z1 + t, Color.white);
        Block(glass, x0 - t, x0, y0, y1, z0, z1, Color.white);
        Block(glass, x1, x1 + t, y0, y1, z0, z1, Color.white);
        Block(glass, x0 - t, x1 + t, y1, y1 + t, z0 - t, z1 + t, Color.white);
        Piece pane = OnPad("Tile Case Glass", glass, IsleKit.Bare(b));
        Component c = b.AddScript(pane, "PuzzleGlass");
        Sdk.Set(c, "style", 1);      // rippled
        Sdk.Set(c, "wire", false);
    }

    // The West Hut: room x -21.5..-19.4, z 2.5..5.5; a sealed void behind the partition (x -19.3..-18.5); a dog-leg
    // vestibule along the west side (x -22.9..-21.75) entered from its south end, the doorway at its north end; no windows
    Piece BuildWestHut(Material snowy)
    {
        var mb = new MeshBuilder();
        Color wall = Timber, roof = IsleKit.DarkWood;
        const float y0 = -0.1f;
        Block(mb, -21.75f, -21.5f, y0, HutH, 2.25f, 4.5f, wall);          // room's west wall, the doorway z 4.5..5.4
        Block(mb, -21.75f, -21.5f, y0, HutH, 5.4f, 5.75f, wall);
        Block(mb, -21.75f, -21.5f, 2.2f, HutH, 4.5f, 5.4f, wall);
        Block(mb, -21.75f, -18.25f, y0, HutH, 2.25f, 2.5f, wall);         // south
        Block(mb, -23.15f, -18.25f, y0, HutH, 5.5f, 5.75f, wall);         // north (closing the vestibule's north end)
        Block(mb, -19.4f, -19.3f, y0, HutH, 2.5f, 5.5f, wall);            // the partition
        Block(mb, -18.5f, -18.25f, y0, HutH, 2.5f, 5.5f, wall);           // outer east
        Block(mb, -23.15f, -22.9f, y0, HutH, 2.25f, 5.75f, wall);         // the vestibule's outer west wall
        Block(mb, -23.3f, -18.1f, HutH, HutH + 0.2f, 2.1f, 5.9f, roof);
        Block(mb, -23.15f, -21.75f, HutH - 0.35f, HutH, 2.1f, 2.25f, IsleKit.Red);      // a lintel over the entry
        return OnPad("West Hut", mb, snowy);
    }

    // The North Hut (four players): room x -1.5..1.5, z 19.4..21.5; sealed void z 18.5..19.3; vestibule along the north
    // side (z 21.75..22.9) entered from its east end, the doorway at the west end of the room's north wall
    Piece BuildNorthHut(Material snowy)
    {
        var mb = new MeshBuilder();
        Color wall = Timber, roof = IsleKit.DarkWood;
        const float y0 = -0.1f;
        Block(mb, -1.75f, -1.5f, y0, HutH, 18.25f, 23.15f, wall);         // west (closing the vestibule's west end)
        Block(mb, 1.5f, 1.75f, y0, HutH, 18.25f, 21.75f, wall);           // east
        Block(mb, -1.5f, 1.5f, y0, HutH, 18.25f, 18.5f, wall);            // outer south
        Block(mb, -1.5f, 1.5f, y0, HutH, 19.3f, 19.4f, wall);             // the partition
        Block(mb, -1.5f, -1.4f, y0, HutH, 21.5f, 21.75f, wall);           // the room's north wall, the doorway x -1.4..-0.5
        Block(mb, -0.5f, 1.75f, y0, HutH, 21.5f, 21.75f, wall);
        Block(mb, -1.4f, -0.5f, 2.2f, HutH, 21.5f, 21.75f, wall);
        Block(mb, -1.75f, 1.75f, y0, HutH, 22.9f, 23.15f, wall);          // the vestibule's outer north wall
        Block(mb, -1.9f, 1.9f, HutH, HutH + 0.2f, 18.1f, 23.3f, roof);
        Block(mb, 1.75f, 1.9f, HutH - 0.35f, HutH, 21.75f, 22.9f, IsleKit.Red);      // a lintel over the entry
        return OnPad("North Hut", mb, snowy);
    }
}
