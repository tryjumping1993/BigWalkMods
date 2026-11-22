// Bell Ringers (puzzle 4): Call Changes. A snow-roofed stone belfry in a churchyard holds two ringing rooms with a
// solid wall between them and their doors on opposite sides. Six coloured mats lie under six coloured bell ropes:
// three in the West room, two in the East room, and the great tenor, whose mat runs across the dividing wall so a
// ringer on each side can reach it. Twenty metres north stands the Caller's box, whose back wall carries a lamp map
// of both rooms that nobody else can see. Once the knob is on and the caller presses Begin Peal, the SDK's Press In
// Order (Hold At End) lights one lamp at a time: at the end of each 5 s call the called mat must be occupied and no
// other mat may count as occupied, and the tenor counts only with every ringer on it. Twelve clean calls ring the
// peal (10 of 6 s for two players, 14 for four); only then does the Validator switch on, and everyone has 30 s to
// "fire" the bells by pulling their own lever (box, West room, East room; the Sexton's post for four) within the
// same second. Spec: Z:\Temp\claude\BigWalkMods\puzzle-specs\BellRingers.md (revision 2 and its review notes).
// No custom scripts.
//
// Coordinates: the spec's are measured from the belfry's centre (x east, z north). The churchyard runs from spec
// z -18 to +28, so everything is moved by Z0 (-5 m) to centre the churchyard, and so the 26 x 46 m footprint the
// island levels round the site, on the site. Every wall part is placed from the wall constants below (inner face +
// the part's back depth from world\part-metrics.tsv + 1 cm), never by hand.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BellRingersPuzzle : PuzzleDef
{
    public override string Name => "BellRingers";
    public override Vector2 Footprint => new Vector2(26f, 46f);
    public override Vector2? FixedSite => new Vector2(-1070f, -2190f);      // a churchyard terrace above the south coast (layout-final)

    // The spec's z to the puzzle's local z
    const float Z0 = -5f;
    static float Z(float specZ) => specZ + Z0;

    // ---- the belfry (spec coordinates; the pad's top is the root's level) ----
    const float OutX = 6.4f, OutZ = 5.0f, Wall = 0.3f, InX = OutX - Wall, InZ = OutZ - Wall;      // inner faces x +-6.1, z +-4.7
    const float WallH = 2.6f, RoofT = 0.2f, Eave = 0.3f, Div = 0.15f;
    const float DoorW = 1.4f, DoorH = 2.2f, DoorX = 5.2f;      // West door in the north wall at x -5.2, East door in the south wall at x +5.2
    const float GapZ0 = 1.4f, GapZ1 = 3.0f;                    // the divider's gap segment (absent in 2-player lobbies)
    const float FloorTop = 0.02f;
    const float TowerHalf = 1.7f;

    // ---- the Caller's box (spec coordinates; heights from its floor) ----
    const float BoxZ = 23.8f, BoxOutX = 1.6f, BoxBackOut = 22.6f, BoxOpenZ = 25.0f, BoxWall = 0.2f;
    const float BoxInX = BoxOutX - BoxWall, BoxBackIn = BoxBackOut + BoxWall;      // inner faces x +-1.4, back z 22.8
    const float BoxH = 2.5f, BoxPad = 0.1f, Pilaster = 0.4f;
    const float BoardZ = 22.82f, BoardDepth = 0.04f, BoardW = 1.3f, BoardH = 0.95f, BoardMid = 1.30f;
    const float BoardFront = BoardZ + BoardDepth / 2f;     // 22.84
    const float MapScale = 0.09f;                          // metres of board per metre of belfry

    const float SextonZ = -14f;
    const float Gap = 0.01f;      // between a wall part's back and its wall

    // Back depths (-minZ in world\part-metrics.tsv, at scale 1)
    const float DepthBar = 0.101f, DepthSim = 0.02f, DepthPoke = 0f, DepthValidator = 0f, DepthLamp = 0.10f,
        DepthMic = 0.02f, DepthSpeaker = 0.10f, DepthTimer = 0.04f;

    // Treble, Second, Third (West), Fourth, Fifth (East), Tenor (across the divider): mat centres and colours
    static readonly string[] BellName = { "Treble", "Second", "Third", "Fourth", "Fifth", "Tenor" };
    static readonly Vector2[] MatAt =
    {
        new Vector2(-4.2f, 3.2f), new Vector2(-4.2f, 0f), new Vector2(-4.2f, -3.2f),
        new Vector2(4.2f, 2.2f), new Vector2(4.2f, -2.2f), new Vector2(0f, 0f),
    };
    static readonly string[] BellHex = { "DDEEFF", "FF2A2A", "2AFF4A", "FFC21A", "2A6BFF", "B03CFF" };
    const float MatSize = 1.4f, TenorX = 2.6f, TenorZ = 1.8f, SensorH = 2.4f;

    static readonly Color Slate = new Color(0.25f, 0.27f, 0.32f);
    static readonly Color Floorboards = new Color(0.42f, 0.31f, 0.21f);
    static readonly Color Hemp = new Color(0.78f, 0.70f, 0.52f);
    static readonly Color Bronze = new Color(0.80f, 0.58f, 0.22f);
    static readonly Color Lantern = new Color(1f, 0.85f, 0.45f);

    PuzzleBuilder b;
    float rootY;

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    // Vertex colours are read as linear: this makes a painted colour look as written
    static Color Paint(Color c) => c.linear;

    float GroundAt(float x, float specZ) => b.WorldPoint(x, Z(specZ), 0f, true).y - rootY;

    // A solid block given by its extents in spec coordinates (heights from the root's level), into a mesh in the
    // root's local space
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, Z((z0 + z1) / 2f)), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);

    // A four-sided pyramid (the tower's cap): base square half-width 'half' at 'y0', apex 'height' above it
    static void Pyramid(MeshBuilder mb, float cx, float cz, float y0, float half, float height, Color c)
    {
        var low = new[]
        {
            new Vector3(cx - half, y0, cz - half), new Vector3(cx + half, y0, cz - half),
            new Vector3(cx + half, y0, cz + half), new Vector3(cx - half, y0, cz + half),
        };
        Vector3 apex = new Vector3(cx, y0 + height, cz);
        for (int i = 0; i < 4; i++) mb.Tri(low[i], apex, low[(i + 1) % 4], c);
        mb.Tri(low[0], low[1], low[2], c * 0.8f);
        mb.Tri(low[0], low[2], low[3], c * 0.8f);
    }

    Piece AddMesh(string name, MeshBuilder mb, Material material, bool collider) => b.MeshPiece(name, mb.ToMesh(), material, collider);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        rootY = b.Root.transform.position.y;
        b.ClearGround(28f, 48f);
        b.DebugTeleport(0f, Z(9f));
        // Jetpacks do not fire anywhere over the churchyard (Winter Walk): nobody flies over the rooms or the box
        IsleKit.CalmAir(b, "Bell Ringers", 0f, 0f, new Vector3(46f, 130f, 66f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // Lobby groups
        Piece two = IsleKit.Lobby(b, "Two", true, false, false);
        Piece three = IsleKit.Lobby(b, "Three", false, true, false);
        Piece four = IsleKit.Lobby(b, "Four", false, false, true);
        Piece gapGroup = IsleKit.Lobby(b, "Divider Gap", false, true, true);
        Piece eastLeverGroup = IsleKit.Lobby(b, "East Lever", false, true, true);
        Piece sextonGroup = IsleKit.Lobby(b, "Sexton", false, false, true);

        BuildBelfry(snowy, bare, gapGroup);

        // ---- mats: Presence Sensors, players only ----
        var mats = new Piece[5];
        for (int i = 0; i < 5; i++)
            mats[i] = IsleKit.Sensor(b, "Mat " + BellName[i], MatAt[i].x, Z(MatAt[i].y), new Vector3(MatSize, SensorH, MatSize), 0f, false)
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        Piece[] groups = { two, three, four };
        string[] tag = { "2p", "3p", "4p" };
        int[] tenorNeeds = { 1, 2, 3 };
        var tenors = new Piece[3];
        for (int k = 0; k < 3; k++)
            tenors[k] = IsleKit.Sensor(b, "Mat Tenor " + tag[k], 0f, Z(0f), new Vector3(TenorX, SensorH, TenorZ), 0f, false)
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", tenorNeeds[k]).Under(groups[k]);

        // ---- room parts: bars, levers, speakers ----
        float southBarZ = -InZ + DepthBar * 0.6f + Gap, northBarZ = InZ - DepthBar * 0.6f - Gap;     // -4.63 / +4.63
        Piece pealWest = Bar("Peal Bar West", -3.2f, Z(southBarZ), 2.25f, 0f, 0.6f, 12);
        Piece pealEast = Bar("Peal Bar East", 3.2f, Z(northBarZ), 2.25f, 180f, 0.6f, 12);
        Piece callWest = Bar("Call Bar West", -3.2f, Z(southBarZ), 1.95f, 0f, 0.6f, 10);
        Piece callEast = Bar("Call Bar East", 3.2f, Z(northBarZ), 1.95f, 180f, 0.6f, 10);
        Piece fireWest = Bar("Fire Bar West", -3.2f, Z(southBarZ), 1.65f, 0f, 0.6f, 20);
        Piece fireEast = Bar("Fire Bar East", 3.2f, Z(northBarZ), 1.65f, 180f, 0.6f, 20);

        Lever("Lever West", -InX + DepthSim + Gap, Z(1.6f), 1.25f, 90f);
        Lever("Lever East", InX - DepthSim - Gap, Z(0f), 1.25f, -90f).Under(eastLeverGroup);
        Piece speakerWest = b.Part("Intercom Speaker", -InX + DepthSpeaker + Gap, Z(-1.6f), 2.10f, 90f, false).Named("Speaker West");
        Piece speakerEast = b.Part("Intercom Speaker", InX - DepthSpeaker - Gap, Z(-1.2f), 2.10f, -90f, false).Named("Speaker East");

        // Room lamps, which Winter Walk's RoomLights makes into point lights near the camera (numbers need a '.')
        int lampNo = 0;
        foreach (float x in new[] { -3.2f, 3.2f })
            foreach (float z in new[] { -2.4f, 2.4f })
                b.Empty("Room Light " + (++lampNo) + " FFD9A0 7.0 1.2", x, Z(z), 2.4f, false);
        // The rooms' boxes as "Indoors ..." empties (MineDriver's convention for where it must not snow; today it only
        // reads the pizzeria's, so these wait for that to be generalised)
        float roomW = InX - Div;
        b.Empty("Indoors Bell Ringers West", -(InX + Div) / 2f, Z(0f), WallH / 2f, false).Transform.localScale = new Vector3(roomW, WallH, 2f * InZ);
        b.Empty("Indoors Bell Ringers East", (InX + Div) / 2f, Z(0f), WallH / 2f, false).Transform.localScale = new Vector3(roomW, WallH, 2f * InZ);

        // ---- the Caller's box ----
        float boxGround = GroundAt(0f, BoxZ), boxFloor = boxGround + BoxPad;
        BuildCallersBox(snowy, bare, gapGroup, boxGround, boxFloor);

        var lamps = new Object[6];
        for (int i = 0; i < 6; i++)
        {
            Piece lamp = b.Part("Light 8", MatAt[i].x * MapScale, Z(BoardFront + DepthLamp + Gap), boxFloor + BoardMid - MatAt[i].y * MapScale, 0f, false)
                .Named("Lamp " + BellName[i])
                .Set("PuzzleLight", "type", 7).Set("PuzzleLight", "color", Hex(BellHex[i]))
                .Set("PuzzleLight", "range", 0.6f).Set("PuzzleLight", "intensity", 0.6f);
            lamps[i] = lamp.Component("PuzzleLight");
        }
        float backBarZ = Z(BoxBackIn + DepthBar * 0.6f + Gap);       // 22.87
        Piece pealBox = Bar("Peal Bar Box", 0f, backBarZ, boxFloor + 2.25f, 0f, 0.6f, 12);
        Piece callBox = Bar("Call Bar Box", 0f, backBarZ, boxFloor + 1.95f, 0f, 0.6f, 10);
        float westWallX = -BoxInX;
        Piece fireBox = Bar("Fire Bar Box", westWallX + DepthBar * 0.5f + Gap, Z(23.9f), boxFloor + 2.15f, 90f, 0.5f, 20);
        Piece check = b.Part("Validator", westWallX + DepthValidator + Gap, Z(23.3f), boxFloor + 1.50f, 90f, false).Named("Peal Check");
        Piece timer = b.Part("Timer", westWallX + DepthTimer + Gap, Z(24.3f), boxFloor + 1.55f, 90f, false).Named("Firing Timer")
            .Set("PuzzleTimer", "duration", 30f);
        timer.Add("PuzzleTimer", "progressBars", fireBox.Component("PuzzleProgressBar"), fireWest.Component("PuzzleProgressBar"), fireEast.Component("PuzzleProgressBar"));
        Piece begin = b.Part("Button Poke", -1.0f, Z(BoxBackIn + DepthPoke + Gap), boxFloor + 1.10f, 0f, false).Named("Begin Peal")
            .Set("PuzzlePokeButton", "autoEnableSeconds", 3f);
        Piece leverCaller = Lever("Lever Caller", BoxInX - DepthSim - Gap, Z(23.8f), boxFloor + 1.25f, -90f);
        Piece mic = b.Part("Intercom Mic", 1.0f, Z(BoxBackIn + DepthMic + Gap), boxFloor + 1.45f, 0f, false).Named("Caller Intercom");
        mic.Add("PuzzleIntercom", "speakers", speakerWest.Component("PuzzleIntercomSpeaker"), speakerEast.Component("PuzzleIntercomSpeaker"));

        // ---- the Sexton's post (four-player lobbies only) ----
        float sextonGround = GroundAt(0f, SextonZ);
        var post = new MeshBuilder();
        Block(post, -0.13f, 0.13f, sextonGround - 0.3f, sextonGround + 1.6f, SextonZ - 0.1f, SextonZ + 0.1f, IsleKit.Wood);
        Block(post, -0.2f, 0.2f, sextonGround + 1.6f, sextonGround + 1.64f, SextonZ - 0.2f, SextonZ + 0.2f, IsleKit.DarkWood);
        Block(post, -0.11f, 0.11f, sextonGround + 1.64f, sextonGround + 1.9f, SextonZ - 0.11f, SextonZ + 0.11f, Lantern);
        Pyramid(post, 0f, Z(SextonZ), sextonGround + 1.9f, 0.17f, 0.16f, IsleKit.DarkWood);
        AddMesh("Sexton Post", post, snowy, true).Under(sextonGroup);
        Lever("Lever Sexton", 0f, Z(SextonZ + 0.1f + DepthSim + Gap), sextonGround + 1.25f, 0f).Under(sextonGroup);

        // ---- the gourd: start corner between the box and the West door, facing the belfry ----
        IsleKit.StartCorner(b, 0f, Z(12f), 180f);

        // ---- Press In Order: one per lobby size, sharing the lamps, the mats and the bars ----
        Component pokeC = begin.Component("PuzzlePokeButton"), checkC = check.Component("PuzzleValidator"), timerC = timer.Component("PuzzleTimer");
        Component pealBoxC = pealBox.Component("PuzzleProgressBar"), pealWestC = pealWest.Component("PuzzleProgressBar"), pealEastC = pealEast.Component("PuzzleProgressBar");
        int[] rounds = { 10, 12, 14 };
        float[] phase = { 6f, 5f, 5f };
        for (int k = 0; k < 3; k++)
        {
            Piece order = b.Empty("Peal " + tag[k], 0f, Z(0f), 0.5f, false).Under(groups[k]);
            Component pio = b.AddScript(order, "PuzzlePressInOrder");
            var buttons = new List<Object>();
            for (int i = 0; i < 5; i++) buttons.Add(mats[i].Component("PuzzlePresenceSensor"));
            buttons.Add(tenors[k].Component("PuzzlePresenceSensor"));
            Sdk.Add(pio, "lights", lamps);
            Sdk.Add(pio, "buttons", buttons.ToArray());
            Sdk.Add(pio, "progressBars", pealBoxC, pealWestC, pealEastC);
            Sdk.Add(pio, "phaseBars", callBox.Component("PuzzleProgressBar"), callWest.Component("PuzzleProgressBar"), callEast.Component("PuzzleProgressBar"));
            Sdk.Set(pio, "rounds", rounds[k]);
            Sdk.Set(pio, "holdAtEnd", true);
            Sdk.Set(pio, "phaseSeconds", phase[k]);
            // Each peal is begun by the caller; a hidden copy never ran Start(), so its Begin does nothing
            begin.On("PuzzlePokeButton", "onPressed", pio, "Begin");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
            // A fresh peal throws away an open finale
            Sdk.Wire(pio, "onBegin", checkC, "TurnOff");
            Sdk.Wire(pio, "onBegin", checkC, "SetCorrect", false);
            Sdk.Wire(pio, "onBegin", timerC, "ResetTimer");
        }

        // ---- wiring (spec section 4) ----
        Piece rootP = b.RootPiece;
        rootP.On("PuzzleSDK", "onPuzzleStart", pokeC, "SetClickable", true);
        rootP.On("PuzzleSDK", "onPuzzleStart", checkC, "TurnOff");
        rootP.On("PuzzleSDK", "onPuzzleStart", checkC, "SetCorrect", false);
        rootP.On("PuzzleSDK", "onPuzzleStart", timerC, "ResetTimer");
        foreach (Component bar in new[] { pealBoxC, pealWestC, pealEastC }) rootP.On("PuzzleSDK", "onPuzzleStop", bar, "Reset");
        rootP.On("PuzzleSDK", "onPuzzleStop", checkC, "TurnOff");          // also at spawn: the Validator is off until a peal is rung
        rootP.On("PuzzleSDK", "onPuzzleStop", checkC, "SetCorrect", false);
        rootP.On("PuzzleSDK", "onPuzzleStop", pokeC, "SetClickable", false);
        rootP.On("PuzzleSDK", "onPuzzleStop", timerC, "ResetTimer");

        // The peal is rung: firing opens for 30 s
        pealBox.On("PuzzleProgressBar", "onFull", checkC, "SetCorrect", true);
        pealBox.On("PuzzleProgressBar", "onFull", checkC, "TurnOn");
        pealBox.On("PuzzleProgressBar", "onFull", timerC, "StartTimer");
        pealBox.On("PuzzleProgressBar", "onFull", pokeC, "SetClickable", false);
        // Too slow: firing closes, ring again
        timer.On("PuzzleTimer", "onFinished", checkC, "TurnOff");
        timer.On("PuzzleTimer", "onFinished", checkC, "SetCorrect", false);
        foreach (Component bar in new[] { pealBoxC, pealWestC, pealEastC }) timer.On("PuzzleTimer", "onFinished", bar, "Fail");
        timer.On("PuzzleTimer", "onFinished", pokeC, "SetClickable", true);
        // Every lever in the group fires onSuccess: wire one. Check() does nothing unless the Validator is on.
        leverCaller.On("PuzzleSimPressSwitch", "onSuccess", timerC, "PauseTimer");
        leverCaller.On("PuzzleSimPressSwitch", "onSuccess", checkC, "Check");

        BuildChurchyard(snowy);
    }

    // A Progress Bar on a wall at local (x, z), 'height' from the root's level, uniformly scaled
    Piece Bar(string name, float x, float localZ, float height, float yaw, float scale, int total)
        => b.Part("Progress Bar", x, localZ, height, yaw, false).Named(name).Scaled(scale, scale, scale)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", total);

    // A "fire" lever: the SDK's press-together switch (all enabled levers of the group within 1 s)
    Piece Lever(string name, float x, float localZ, float height, float yaw)
        => b.Part("Button Sim", x, localZ, height, yaw, false).Named(name)
            .Set("PuzzleSimPressSwitch", "group", "BellRingersFire")
            .Set("PuzzleSimPressSwitch", "activeSeconds", 1.0f)
            .Set("PuzzleSimPressSwitch", "timeoutSeconds", 3f);

    void BuildBelfry(Material snowy, Material bare, Piece gapGroup)
    {
        // The pad, sunk well into the ground
        var pad = new MeshBuilder();
        Block(pad, -7.5f, 7.5f, -1.5f, 0f, -6.5f, 6.5f, IsleKit.Stone);
        AddMesh("Belfry Pad", pad, snowy, true);

        // Outer walls with the two open doorways, the divider (less its gap segment) and the roof slab
        var walls = new MeshBuilder();
        Color stone = IsleKit.Stone;
        float wd0 = -DoorX - DoorW / 2f, wd1 = -DoorX + DoorW / 2f;      // West door x -5.9..-4.5
        float ed0 = DoorX - DoorW / 2f, ed1 = DoorX + DoorW / 2f;        // East door x 4.5..5.9
        Block(walls, -OutX, wd0, 0f, WallH, InZ, OutZ, stone);
        Block(walls, wd1, OutX, 0f, WallH, InZ, OutZ, stone);
        Block(walls, wd0, wd1, DoorH, WallH, InZ, OutZ, stone);
        Block(walls, -OutX, ed0, 0f, WallH, -OutZ, -InZ, stone);
        Block(walls, ed1, OutX, 0f, WallH, -OutZ, -InZ, stone);
        Block(walls, ed0, ed1, DoorH, WallH, -OutZ, -InZ, stone);
        Block(walls, -OutX, -InX, 0f, WallH, -InZ, InZ, stone);
        Block(walls, InX, OutX, 0f, WallH, -InZ, InZ, stone);
        Block(walls, -Div, Div, 0f, WallH, -InZ, GapZ0, stone);
        Block(walls, -Div, Div, 0f, WallH, GapZ1, InZ, stone);
        Block(walls, -OutX - Eave, OutX + Eave, WallH, WallH + RoofT, -OutZ - Eave, OutZ + Eave, Slate);
        // door frames: dark wooden posts either side of each doorway
        foreach (float x in new[] { wd0 - 0.08f, wd1 + 0.08f })
            Block(walls, x - 0.08f, x + 0.08f, 0f, DoorH + 0.1f, OutZ, OutZ + 0.06f, IsleKit.DarkWood);
        foreach (float x in new[] { ed0 - 0.08f, ed1 + 0.08f })
            Block(walls, x - 0.08f, x + 0.08f, 0f, DoorH + 0.1f, -OutZ - 0.06f, -OutZ, IsleKit.DarkWood);
        AddMesh("Belfry Walls", walls, snowy, true);

        var gap = new MeshBuilder();
        Block(gap, -Div, Div, 0f, WallH, GapZ0, GapZ1, stone);
        AddMesh("Belfry Divider Gap", gap, snowy, true).Under(gapGroup);

        // The tower on the roof: a solid body, open bell arches (corner pillars), a band and a slate cap with a star
        var tower = new MeshBuilder();
        float roofTop = WallH + RoofT;
        Block(tower, -TowerHalf, TowerHalf, roofTop, 6.0f, -TowerHalf, TowerHalf, stone);
        Block(tower, -TowerHalf - 0.1f, TowerHalf + 0.1f, 5.88f, 6.0f, -TowerHalf - 0.1f, TowerHalf + 0.1f, IsleKit.Cream);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Block(tower, sx * TowerHalf - (sx > 0 ? 0.6f : 0f), sx * TowerHalf + (sx < 0 ? 0.6f : 0f), 6.0f, 7.5f,
                    sz * TowerHalf - (sz > 0 ? 0.6f : 0f), sz * TowerHalf + (sz < 0 ? 0.6f : 0f), stone);
        Block(tower, -TowerHalf, TowerHalf, 7.5f, 8.5f, -TowerHalf, TowerHalf, stone);
        Pyramid(tower, 0f, Z(0f), 8.5f, TowerHalf + 0.2f, 1.5f, Slate);
        AddMesh("Belfry Tower", tower, snowy, true);

        // Painted things that never take snow: floorboards, mats, ropes, bells, the star
        var floor = new MeshBuilder();
        Block(floor, -InX, InX, 0f, FloorTop, -InZ, InZ, Floorboards);
        for (float x = -InX + 0.6f; x < InX; x += 0.6f)      // board seams
            Block(floor, x - 0.01f, x + 0.01f, FloorTop, FloorTop + 0.002f, -InZ, InZ, new Color(0.30f, 0.22f, 0.15f));
        AddMesh("Belfry Floor", floor, bare, true);

        var mats = new MeshBuilder();
        Color cream = Paint(IsleKit.Cream);
        for (int i = 0; i < 5; i++)
        {
            Vector2 m = MatAt[i];
            Block(mats, m.x - MatSize / 2f, m.x + MatSize / 2f, FloorTop, FloorTop + 0.008f, m.y - MatSize / 2f, m.y + MatSize / 2f, cream);
            Block(mats, m.x - 0.6f, m.x + 0.6f, FloorTop, FloorTop + 0.012f, m.y - 0.6f, m.y + 0.6f, Paint(Hex(BellHex[i])));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            float x0 = s < 0 ? -TenorX / 2f : Div, x1 = s < 0 ? -Div : TenorX / 2f;
            float ix0 = s < 0 ? -TenorX / 2f + 0.1f : Div, ix1 = s < 0 ? -Div : TenorX / 2f - 0.1f;
            Block(mats, x0, x1, FloorTop, FloorTop + 0.008f, -TenorZ / 2f, TenorZ / 2f, cream);
            Block(mats, ix0, ix1, FloorTop, FloorTop + 0.012f, -TenorZ / 2f + 0.1f, TenorZ / 2f - 0.1f, Paint(Hex(BellHex[5])));
        }
        AddMesh("Belfry Mats", mats, bare, false);

        // Bell ropes with striped sallies over each mat (two for the tenor, one on each side of the wall)
        var ropes = new MeshBuilder();
        var ropeAt = new List<Vector3>();
        for (int i = 0; i < 5; i++) ropeAt.Add(new Vector3(MatAt[i].x, i, MatAt[i].y));
        ropeAt.Add(new Vector3(-0.7f, 5, 0f));
        ropeAt.Add(new Vector3(0.7f, 5, 0f));
        foreach (Vector3 r in ropeAt)
        {
            Color c = Paint(Hex(BellHex[(int)r.y]));
            Block(ropes, r.x - 0.03f, r.x + 0.03f, 1.25f, WallH, r.z - 0.03f, r.z + 0.03f, Hemp);
            Block(ropes, r.x - 0.06f, r.x + 0.06f, 1.60f, 1.77f, r.z - 0.06f, r.z + 0.06f, c);
            Block(ropes, r.x - 0.06f, r.x + 0.06f, 1.77f, 1.93f, r.z - 0.06f, r.z + 0.06f, cream);
            Block(ropes, r.x - 0.06f, r.x + 0.06f, 1.93f, 2.10f, r.z - 0.06f, r.z + 0.06f, c);
        }
        AddMesh("Bell Ropes", ropes, bare, false);

        var bells = new MeshBuilder();
        Color bronze = Paint(Bronze);
        for (int i = 0; i < 6; i++)
        {
            float bx = (i % 3 - 1) * 0.68f, bz = i < 3 ? -0.45f : 0.45f, r = i == 5 ? 0.3f : 0.2f + 0.015f * i;
            float cy = 7.0f;
            Kit.AddBall(bells, new Vector3(bx, cy, Z(bz)), r, bronze);
            Kit.AddDisc(bells, new Vector3(bx, cy - r * 0.75f, Z(bz)), r * 1.12f, 0.05f, bronze, 12);
            Block(bells, bx - 0.03f, bx + 0.03f, cy + r * 0.9f, 7.5f, bz - 0.03f, bz + 0.03f, IsleKit.DarkWood);
        }
        AddMesh("Bells", bells, bare, false);

        var star = new MeshBuilder();
        Kit.AddStar(star, new Vector3(0f, 10.4f, Z(0f)), 0.4f, 0.08f, Paint(IsleKit.Gold));
        AddMesh("Tower Star", star, bare, false);

        // Christmas lights round the tower's cap and along the room roof's eaves (no posts: they hang off the building)
        float th = TowerHalf + 0.22f;
        Kit.LightString(b, "Tower Lights", new List<Vector3>
        {
            new Vector3(-th, 8.5f, Z(-th)), new Vector3(th, 8.5f, Z(-th)), new Vector3(th, 8.5f, Z(th)),
            new Vector3(-th, 8.5f, Z(th)), new Vector3(-th, 8.5f, Z(-th)),
        }, 0.25f, 0.4f, false, false, 41);
        float ex = OutX + Eave, ez = OutZ + Eave, eh = WallH + RoofT - 0.02f;
        Kit.LightString(b, "Eaves Lights", new List<Vector3>
        {
            new Vector3(-ex, eh, Z(-ez)), new Vector3(0f, eh, Z(-ez)), new Vector3(ex, eh, Z(-ez)), new Vector3(ex, eh, Z(0f)),
            new Vector3(ex, eh, Z(ez)), new Vector3(0f, eh, Z(ez)), new Vector3(-ex, eh, Z(ez)), new Vector3(-ex, eh, Z(0f)),
            new Vector3(-ex, eh, Z(-ez)),
        }, 0.3f, 0.5f, false, false, 43);
    }

    void BuildCallersBox(Material snowy, Material bare, Piece gapGroup, float ground, float floor)
    {
        var box = new MeshBuilder();
        Color wood = IsleKit.DarkWood;
        float wallTop = floor + BoxH;
        Block(box, -2f, 2f, ground - 1.2f, floor, BoxZ - 1.75f, BoxZ + 1.75f, IsleKit.Stone);      // its own small pad
        Block(box, -BoxOutX, BoxOutX, floor, wallTop, BoxBackOut, BoxBackIn, wood);                 // back wall, towards the belfry
        Block(box, -BoxOutX, -BoxInX, floor, wallTop, BoxBackIn, BoxOpenZ, wood);
        Block(box, BoxInX, BoxOutX, floor, wallTop, BoxBackIn, BoxOpenZ, wood);
        Block(box, -BoxOutX + BoxWall, -BoxOutX + Pilaster, floor, wallTop, BoxOpenZ - BoxWall, BoxOpenZ, wood);      // pilasters either side of the opening
        Block(box, BoxOutX - Pilaster, BoxOutX - BoxWall, floor, wallTop, BoxOpenZ - BoxWall, BoxOpenZ, wood);
        Block(box, -BoxOutX - 0.3f, BoxOutX + 0.3f, wallTop, wallTop + 0.2f, BoxBackOut - 0.3f, BoxOpenZ + 0.3f, IsleKit.Red);      // roof
        AddMesh("Callers Box", box, snowy, true);

        // The lamp map: the belfry's plan as seen through the back wall (West on the caller's right, south at the
        // top), a coloured square behind each lamp, the doors in gold
        var map = new MeshBuilder();
        Block(map, -BoardW / 2f, BoardW / 2f, floor + BoardMid - BoardH / 2f, floor + BoardMid + BoardH / 2f, BoardZ - BoardDepth / 2f, BoardFront, IsleKit.Board);
        Color line = Paint(IsleKit.Cream), gold = Paint(IsleKit.Gold);
        float MapX(float x) => x * MapScale;
        float MapY(float z) => floor + BoardMid - z * MapScale;
        void Paint2D(MeshBuilder mb, float x0, float x1, float z0, float z1, float proud, Color c)
        {
            float y0 = Mathf.Min(MapY(z0), MapY(z1)), y1 = Mathf.Max(MapY(z0), MapY(z1));
            Block(mb, MapX(x0), MapX(x1), y0, y1, BoardFront - 0.002f, BoardFront + proud, c);
        }
        const float t = 0.14f;      // outline width, in belfry metres (1.3 cm on the board)
        Paint2D(map, -InX, InX, InZ, InZ + t, 0.005f, line);          // north (bottom of the board)
        Paint2D(map, -InX, InX, -InZ - t, -InZ, 0.005f, line);        // south (top)
        Paint2D(map, -InX - t, -InX, -InZ - t, InZ + t, 0.005f, line);
        Paint2D(map, InX, InX + t, -InZ - t, InZ + t, 0.005f, line);
        Paint2D(map, -Div, Div, -InZ, GapZ0, 0.005f, line);           // the divider
        Paint2D(map, -Div, Div, GapZ1, InZ, 0.005f, line);
        Paint2D(map, -DoorX - DoorW / 2f, -DoorX + DoorW / 2f, InZ, InZ + 0.3f, 0.006f, gold);      // West door
        Paint2D(map, DoorX - DoorW / 2f, DoorX + DoorW / 2f, -InZ - 0.3f, -InZ, 0.006f, gold);      // East door
        for (int i = 0; i < 5; i++)
        {
            const float half = 1.45f;      // 0.26 m squares on the board
            Paint2D(map, MatAt[i].x - half, MatAt[i].x + half, MatAt[i].y - half, MatAt[i].y + half, 0.006f, Paint(Hex(BellHex[i])));
        }
        Paint2D(map, -2f, 2f, -1.1f, 1.1f, 0.008f, Paint(Hex(BellHex[5])));      // the tenor: a bar across the divider line
        AddMesh("Lamp Map", map, bare, false);
        var mapGap = new MeshBuilder();
        Paint2D(mapGap, -Div, Div, GapZ0, GapZ1, 0.005f, line);       // drawn only where the wall's gap segment stands
        AddMesh("Lamp Map Gap", mapGap, bare, false).Under(gapGroup);

        // a bell-rope sign over Begin Peal
        var sign = new MeshBuilder();
        Block(sign, -1.06f, -0.94f, floor + 1.30f, floor + 1.60f, BoxBackIn, BoxBackIn + 0.012f, Hemp);
        Block(sign, -1.08f, -0.92f, floor + 1.30f, floor + 1.42f, BoxBackIn, BoxBackIn + 0.016f, Paint(IsleKit.Red));
        AddMesh("Begin Peal Sign", sign, bare, false);
    }

    // The churchyard: a low fence (no collider: nothing depends on it) on a 26 x 46 m rectangle with a 3 m gap in
    // each long side, opposite the start corner, and lights strung along its posts
    void BuildChurchyard(Material snowy)
    {
        const float hx = 13f, hz = 23f, gap0 = 2.5f, gap1 = 5.5f, postH = 1.05f;
        var north = new List<Vector2> { new Vector2(-hx, gap1), new Vector2(-hx, hz), new Vector2(hx, hz), new Vector2(hx, gap1) };
        var south = new List<Vector2> { new Vector2(hx, gap0), new Vector2(hx, -hz), new Vector2(-hx, -hz), new Vector2(-hx, gap0) };
        var fence = new MeshBuilder();
        int seed = 51;
        foreach (List<Vector2> run in new[] { north, south })
        {
            var posts = new List<Vector3>();
            for (int leg = 0; leg + 1 < run.Count; leg++)
            {
                Vector2 a = run[leg], c = run[leg + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, c) / 4f));
                for (int s = leg == 0 ? 0 : 1; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, c, (float)s / steps);
                    posts.Add(new Vector3(p.x, postH, p.y));
                }
            }
            for (int i = 0; i < posts.Count; i++)
            {
                Vector3 p = posts[i];
                float g = b.WorldPoint(p.x, p.z, 0f, true).y - rootY;
                Kit.AddBox(fence, new Vector3(p.x, g - 0.2f, p.z), new Vector3(0.1f, postH + 0.2f, 0.1f), IsleKit.DarkWood);
                if (i + 1 >= posts.Count) continue;
                Vector3 q = posts[i + 1];
                float g2 = b.WorldPoint(q.x, q.z, 0f, true).y - rootY;
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, mid = (g + g2) / 2f;
                foreach (float rail in new[] { 0.4f, 0.82f })
                    Kit.AddBox(fence, new Vector3((p.x + q.x) / 2f, mid + rail, (p.z + q.z) / 2f), new Vector3(0.05f, 0.07f, d.magnitude), IsleKit.Wood, yaw);
            }
            Kit.LightString(b, run == north ? "Fence Lights North" : "Fence Lights South", posts, 0.35f, 0.5f, true, false, seed++);
        }
        AddMesh("Churchyard Fence", fence, snowy, false);
    }
}
