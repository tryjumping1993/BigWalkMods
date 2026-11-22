// Thin Ice: a frozen pond of 25 floes in a 5 x 5 grid on a raised stone basin behind a tall picket fence, five of them
// broken to open black water (spec: puzzle-specs/ThinIce.md, revision 2, 2026-11-22).
//
// The crosser puts on the blindfold on the roofed entry landing and goes through the blind turnstile onto the ice.
// Every phase a lamp lights on a map of the pond, and when the phase runs out the crosser must stand on that floe and
// nobody on any other (the SDK's Press In Order in timed mode, the floes its Presence Sensor buttons). The map hangs
// inside the ice-master's lodge: soundproof, windowless, entered through a baffled vestibule, its door shut while the
// puzzle runs. The spotter there sees the lamp but never the pond and speaks only into an intercom, which plays into the
// headphones of the listening chair on the watch tower. The watcher in the chair hears only the spotter, sees the pond
// and talks the blind crosser round the holes. Ten floes in a row win the gourd; the lit floe is random every phase.
//
// Lobby sizes: three players as above (Map 3); four split the map between the lodge (west half) and the Far Hut (east
// half), each with its own intercom into the same chair (Map 4, eight rounds); two players get a 20-lamp board on the
// back of the tower's solid rear wall, facing away from the pond, which the watcher reads and turns round to call
// (Map 2, 15 s phases). The IceWarden script (Assets/PuzzleScripts) ends a run when anyone on the ice is not
// blindfolded, or anyone in a hut holds a walkie-talkie or megaphone. A miss (or the warden) ends the run with the
// puzzle still switched on; any start switch off and on deals a new run.
//
// Coordinates: local metres from the root, x east, z north (+z forward); heights from the root's level, every piece
// onGround: false (the island levels the site). Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class ThinIcePuzzle : PuzzleDef
{
    public override string Name => "ThinIce";
    public override Vector2 Footprint => new Vector2(36f, 36f);
    public override Vector2? FixedSite => new Vector2(-843f, -1089f);      // layout-final: the frozen lake's south shore, by the plaza
    public override float Yaw => 0f;

    const float Deck = 1.6f;            // the ice and the rim
    const float Bed = 1.5f;             // under the floes (the dark seams)
    const float Sunk = -1.5f;           // foundations start this far under the root's level
    const float TowerDeck = 4.0f;
    const float TowerRoof = 6.6f;
    const float HutWall = 2.6f;
    const float HutFloor = 0.02f;       // 2 cm proud of the ground (coplanar faces z-fight)
    const float DoorTop = 2.02f;        // the Door part: 1.24 wide, -0.02..2.02 high
    const float LampRange = 0.15f;      // map lamps: the light ends inside the wall
    const int Rounds3 = 10, Rounds4 = 8, Rounds2 = 10;
    const float Phase3 = 17f, Phase4 = 17f, Phase2 = 15f;

    // Holes (column, row), no floe, no sensor, no lamp
    static readonly Vector2Int[] Holes = { new Vector2Int(4, 1), new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(1, 4), new Vector2Int(4, 5) };
    // Row colours, south (the tower side) to north: on the posts outside the fence and on every map
    static readonly Color[] RowColours =
    {
        IsleKit.Red, new Color(0.15f, 0.6f, 0.25f), IsleKit.Gold, new Color(0.2f, 0.4f, 0.9f), new Color(0.95f, 0.95f, 0.95f),
    };
    static readonly Color Seam = new Color(0.10f, 0.13f, 0.17f);
    static readonly Color Water = new Color(0.02f, 0.04f, 0.07f);
    static readonly Color HoleMark = new Color(0.03f, 0.03f, 0.04f);
    static readonly Color FlagWhite = new Color(0.95f, 0.95f, 0.93f);

    PuzzleBuilder b;

    static float CellX(int c) => -4.4f + (c - 1) * 2.2f;
    static float CellZ(int r) => -0.4f + (r - 1) * 2.2f;
    static bool IsHole(int c, int r)
    {
        foreach (Vector2Int h in Holes) if (h.x == c && h.y == r) return true;
        return false;
    }
    // The 4-player split: west half = columns 1-2 plus (3,1), (3,2); east half = columns 4-5 plus (3,4), (3,5)
    static bool WestHalf(int c, int r) => c <= 2 || (c == 3 && r <= 2);

    // A block between two corners (local x, y, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
    {
        if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
        if (z1 < z0) { float t = z0; z0 = z1; z1 = t; }
        Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);
    }

    // Pickets along a straight run from (x0, z0) to (x1, z1), standing on 'baseY', 'height' tall, one every 'pitch'
    // metres, each 'width' along the run; a bottom rail and a top rail join them
    static void Pickets(MeshBuilder mb, float x0, float z0, float x1, float z1, float baseY, float height, float pitch, float width, Color color)
    {
        float dx = x1 - x0, dz = z1 - z0, length = Mathf.Sqrt(dx * dx + dz * dz);
        if (length < 0.01f) return;
        float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;      // the run along the boxes' local z
        int n = Mathf.Max(1, Mathf.RoundToInt(length / pitch));
        float inset = width / 2f / length;
        for (int i = 0; i <= n; i++)
        {
            float t = Mathf.Lerp(inset, 1f - inset, (float)i / n);
            Kit.AddBox(mb, new Vector3(x0 + dx * t, baseY, z0 + dz * t), new Vector3(0.05f, height, width), color, yaw);
        }
        Vector3 mid = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
        Kit.AddBox(mb, mid + Vector3.up * (baseY + 0.12f), new Vector3(0.06f, 0.08f, length), color, yaw);
        Kit.AddBox(mb, mid + Vector3.up * (baseY + height - 0.08f), new Vector3(0.07f, 0.08f, length), color, yaw);
    }

    // A mesh built in root-level coordinates, placed at the root
    Piece OnRoot(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        return piece;
    }

    // An SDK part, measured from the root's level
    Piece P(string prefab, float x, float z, float height, float yaw = 0f) => b.Part(prefab, x, z, height, yaw, false);

    // A map lamp (Light 8) with its light kept on the board
    Component Lamp(string name, float x, float z, float height, float yaw, Piece group)
        => P("Light 8", x, z, height, yaw).Named(name).Set("PuzzleLight", "range", LampRange).Under(group).Component("PuzzleLight");

    // A Progress Bar (the only scalable kind of part with Glass and Timer)
    Component Bar(string name, float x, float z, float height, float yaw, Vector3 scale, int total, Piece group)
        => P("Progress Bar", x, z, height, yaw).Named(name).Scaled(scale.x, scale.y, scale.z)
            .Set("PuzzleProgressBar", "total", total).Set("PuzzleProgressBar", "style", 0).Under(group).Component("PuzzleProgressBar");

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(36f, 32f, 0f, 1.5f);
        b.DebugTeleport(-8.5f, -5.5f, 1.5f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Material ice = b.ShaderMaterial("BWM/Snowy", "Ice", m => m.SetFloat("_SnowLine", 2f));
        // No jetpacks over the puzzle: nobody flies onto the ice, the tower or the huts' roofs
        IsleKit.CalmAir(b, "Thin Ice", 0f, 1.5f, new Vector3(44f, 120f, 40f), 0f, -10f, false);

        BuildPond(snowy, bare, ice);
        BuildTower(snowy, bare);

        // ---- shared by every lobby size ----
        var floes = new List<Vector2Int>();      // floe order: every map pairs its i-th lamp with the i-th floe
        var sensors = new List<Component>();
        for (int r = 1; r <= 5; r++)
            for (int c = 1; c <= 5; c++)
            {
                if (IsHole(c, r)) continue;
                floes.Add(new Vector2Int(c, r));
                Piece floe = IsleKit.Sensor(b, "Floe " + c + "-" + r, CellX(c), CellZ(r), new Vector3(1.9f, 2.6f, 1.9f), Deck, false);
                sensors.Add(floe.Component("PuzzlePresenceSensor"));
            }
        IsleKit.BlindfoldHook(b, -5.4f, -4.4f, 90f, Deck, false);
        IsleKit.BlindGate(b, -4.4f, -2.5f, 0f, Deck, false);

        Piece chairGroup = IsleKit.Lobby(b, "Chair", false, true, true);
        Piece chair = P("Listening Chair", 0f, -7.4f, TowerDeck, 0f).Named("Watch Chair").Under(chairGroup);      // no microphone: only the intercoms
        Component chairC = chair.Component("PuzzleListeningChair");

        IsleKit.StartCorner(b, 6.5f, -9.5f, 0f, true, 0f, false);
        P("Switch Start", -1.0f, -9.227f, TowerDeck + 1.25f, 0f).Named("Tower Switch");
        P("Live Light", -0.6f, -9.294f, TowerDeck + 1.42f, 0f).Named("Tower Live");

        Piece dead = P("Radio Dead Zone", 0f, 1.5f, 3f).Named("Dead Ice");
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(40f, 12f, 36f);
        zone.center = Vector3.zero;

        Piece warden = b.Empty("Ice Warden", 0f, 0f, 0f, false);
        Component wardenC = b.AddCustomScript(warden, "IceWarden");
        Sdk.Set(wardenC, "pondCentre", new Vector3(0f, 15.75f, 4f));      // the basin x -6.5..6.5, z -2.5..10.5, from 0.5 m (a pit's floor rider) to 31 m
        Sdk.Set(wardenC, "pondSize", new Vector3(13f, 30.5f, 13f));
        Sdk.Set(wardenC, "jettyCentre", new Vector3(0f, 16.4f, 11.7f));    // the jetty x -0.8..0.8, z 10.5..12.9, from 1.8 m (its first step is 2.1) up
        Sdk.Set(wardenC, "jettySize", new Vector3(1.6f, 29.2f, 2.4f));
        Sdk.Set(wardenC, "lodgeCentre", new Vector3(-13.25f, 1.4f, -2f));
        Sdk.Set(wardenC, "lodgeSize", new Vector3(4.9f, 2.8f, 3.6f));
        Sdk.Set(wardenC, "farCentre", new Vector3(13.25f, 1.4f, 12f));
        Sdk.Set(wardenC, "farSize", new Vector3(4.9f, 2.8f, 3.6f));
        Sdk.Set(wardenC, "interval", 0.3f);
        Sdk.Set(wardenC, "checkHat", true);
        Sdk.Set(wardenC, "checkRadios", true);

        // ---- the huts (lodge: 3 and 4 players; Far Hut: 4 players) ----
        Piece lodge = IsleKit.Lobby(b, "Lodge", false, true, true);
        Component lodgeLamp = BuildHut(lodge, "Lodge", "Lodge Walls", "Lodge Door", "Lodge Switch", "Lodge Live", "Lodge Intercom", "Lodge Lamp",
            "Room Light 1 FFD8A8 2.5 0.5", "Room Light 2 FFD8A8 1.5 0.3", -1f, 0f, chairC, snowy, bare);
        Piece farHut = IsleKit.Lobby(b, "Far Hut", false, false, true);
        Component farLamp = BuildHut(farHut, "Far Hut", "Far Hut Walls", "Far Door", "Far Switch", "Far Live", "Far Intercom", "Far Hut Lamp",
            "Room Light 3 FFD8A8 2.5 0.5", "Room Light 4 FFD8A8 1.5 0.3", 1f, 14f, chairC, snowy, bare);

        // ---- the three maps: one shown per lobby size ----
        Vector3 eave = new Vector3(0.45f, 1f, 1f), hut = new Vector3(0.4f, 0.5f, 1f), board = new Vector3(0.3f, 0.5f, 1f);

        // Map 2: the board on the south face of the tower's rear wall, read from the rear landing
        Piece map2 = IsleKit.Lobby(b, "Map 2", true, false, false);
        var lamps2 = new List<Component>();
        foreach (Vector2Int f in floes)
            lamps2.Add(Lamp("Tower Lamp " + f.x + "-" + f.y, -0.5f + (f.x - 3) * 0.3f, -9.6f, TowerDeck + 0.5f + (f.y - 1) * 0.3f, 180f, map2));
        Component rounds2 = Bar("Rounds (2)", 0f, -6.9f, 6.15f, 180f, eave, Rounds2, map2);
        Component phase2 = Bar("Phase (2)", 0f, -6.9f, 5.65f, 180f, eave, (int)Phase2, map2);
        Component rounds2Board = Bar("Rounds (2) Board", -0.5f, -9.601f, 6.2f, 180f, board, Rounds2, map2);
        Component phase2Board = Bar("Phase (2) Board", -0.5f, -9.601f, 4.25f, 180f, board, (int)Phase2, map2);
        var paint2 = new MeshBuilder();
        for (int r = 1; r <= 5; r++)
        {
            float h = TowerDeck + 0.5f + (r - 1) * 0.3f;
            Block(paint2, -1.27f, 0.27f, h - 0.14f, h + 0.14f, -9.525f, -9.505f, RowColours[r - 1]);
            for (int c = 1; c <= 5; c++)
                if (IsHole(c, r))
                {
                    float x = -0.5f + (c - 3) * 0.3f;
                    Block(paint2, x - 0.12f, x + 0.12f, h - 0.12f, h + 0.12f, -9.535f, -9.52f, HoleMark);
                }
        }
        OnRoot("Tower Map Paint", paint2, bare, false).Under(map2);
        Component pio2 = Order("Ice Map (2)", map2, lamps2, sensors, new[] { rounds2, rounds2Board }, new[] { phase2, phase2Board }, Rounds2, Phase2);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio2, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio2, "End");
        WireWin(rounds2);

        // Map 3: the lodge's east wall holds all 20 lamps
        Piece map3 = IsleKit.Lobby(b, "Map 3", false, true, false);
        var lamps3 = new List<Component>();
        foreach (Vector2Int f in floes)
            lamps3.Add(Lamp("Lodge Lamp " + f.x + "-" + f.y, -11.1f, -2.0f + (3 - f.x) * 0.3f, 1.0f + (f.y - 1) * 0.3f, -90f, map3));
        Component rounds3Lodge = Bar("Rounds (3) Lodge", -12.2f, -0.501f, 2.05f, 180f, hut, Rounds3, map3);
        Component phase3Lodge = Bar("Phase (3) Lodge", -12.2f, -0.501f, 1.75f, 180f, hut, (int)Phase3, map3);
        Component rounds3Tower = Bar("Rounds (3) Tower", 0f, -6.9f, 6.15f, 180f, eave, Rounds3, map3);
        Component phase3Tower = Bar("Phase (3) Tower", 0f, -6.9f, 5.65f, 180f, eave, (int)Phase3, map3);
        Component pio3 = Order("Ice Map (3)", map3, lamps3, sensors, new[] { rounds3Lodge, rounds3Tower }, new[] { phase3Lodge, phase3Tower }, Rounds3, Phase3);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio3, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio3, "End");
        WireWin(rounds3Tower);

        // Map 4: the lodge's west-half lamps and the Far Hut's east-half lamps, each paired with its floe
        Piece map4 = IsleKit.Lobby(b, "Map 4", false, false, true);
        var lamps4 = new List<Component>();
        foreach (Vector2Int f in floes)
        {
            if (WestHalf(f.x, f.y))
                lamps4.Add(Lamp("Lodge Lamp " + f.x + "-" + f.y + " (4)", -11.1f, -2.0f + (3 - f.x) * 0.3f, 1.0f + (f.y - 1) * 0.3f, -90f, map4));
            else
                lamps4.Add(Lamp("Far Lamp " + f.x + "-" + f.y, 11.1f, 12.0f + (f.x - 3) * 0.3f, 1.0f + (f.y - 1) * 0.3f, 90f, map4));
        }
        Component rounds4Lodge = Bar("Rounds (4) Lodge", -12.2f, -0.501f, 2.05f, 180f, hut, Rounds4, map4);
        Component phase4Lodge = Bar("Phase (4) Lodge", -12.2f, -0.501f, 1.75f, 180f, hut, (int)Phase4, map4);
        Component rounds4Far = Bar("Rounds (4) Far", 12.2f, 13.499f, 2.05f, 180f, hut, Rounds4, map4);
        Component phase4Far = Bar("Phase (4) Far", 12.2f, 13.499f, 1.75f, 180f, hut, (int)Phase4, map4);
        Component rounds4Tower = Bar("Rounds (4) Tower", 0f, -6.9f, 6.15f, 180f, eave, Rounds4, map4);
        Component phase4Tower = Bar("Phase (4) Tower", 0f, -6.9f, 5.65f, 180f, eave, (int)Phase4, map4);
        Component pio4 = Order("Ice Map (4)", map4, lamps4, sensors, new[] { rounds4Lodge, rounds4Far, rounds4Tower },
            new[] { phase4Lodge, phase4Far, phase4Tower }, Rounds4, Phase4);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio4, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio4, "End");
        WireWin(rounds4Tower);

        // The huts' lamps: on while the puzzle runs (start and stop do different things, as the SDK's checks require)
        foreach (Component lamp in new[] { lodgeLamp, farLamp })
        {
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        }

        // ---- decoration ----
        Kit.LightRing(b, "Site Lights", 0f, 1.5f, 36f, 32f, 2.6f, true, 211);
        Kit.LightString(b, "Fence Lights", new List<Vector3>
        {
            new Vector3(-2.3f, Deck + 1.85f, -2.45f), new Vector3(6.45f, Deck + 1.85f, -2.45f), new Vector3(6.45f, Deck + 1.85f, 10.45f),
            new Vector3(0.88f, Deck + 1.85f, 10.45f),
        }, 0.05f, 0.5f, false, false, 212);
        Kit.LightString(b, "Fence Lights West", new List<Vector3>
        {
            new Vector3(-0.88f, Deck + 1.85f, 10.45f), new Vector3(-6.45f, Deck + 1.85f, 10.45f), new Vector3(-6.45f, Deck + 1.85f, -2.45f),
        }, 0.05f, 0.5f, false, false, 213);
        Kit.LightString(b, "Tower Lights", new List<Vector3> { new Vector3(-1.8f, TowerRoof - 0.05f, -6.48f), new Vector3(1.8f, TowerRoof - 0.05f, -6.48f) },
            0.05f, 0.4f, false, false, 214);
        Kit.LightString(b, "Lodge Lights", new List<Vector3> { new Vector3(-15.88f, HutWall + 0.12f, -3.95f), new Vector3(-15.88f, HutWall + 0.12f, -0.05f) },
            0.05f, 0.4f, false, false, 215).Under(lodge);
        Kit.LightString(b, "Far Hut Lights", new List<Vector3> { new Vector3(15.88f, HutWall + 0.12f, 10.05f), new Vector3(15.88f, HutWall + 0.12f, 13.95f) },
            0.05f, 0.4f, false, false, 216).Under(farHut);
    }

    // The basin (rim, bed, floes, the five pits), the fence, the roofed entry landing, the exit jetty, the row posts and
    // the column flags
    void BuildPond(Material snowy, Material bare, Material ice)
    {
        var basin = new MeshBuilder();      // stone: snow on top
        var bed = new MeshBuilder();        // the dark bed under the floes and the black water in the pits: no snow
        var floes = new MeshBuilder();      // the ice
        var fence = new MeshBuilder();

        // rim: 1 m of stone round the 11 x 11 m of ice, x -6.5..6.5, z -2.5..10.5
        Block(basin, -6.5f, 6.5f, Sunk, Deck, -2.5f, -1.5f, IsleKit.Stone);
        Block(basin, -6.5f, 6.5f, Sunk, Deck, 9.5f, 10.5f, IsleKit.Stone);
        Block(basin, -6.5f, -5.5f, Sunk, Deck, -1.5f, 9.5f, IsleKit.Stone);
        Block(basin, 5.5f, 6.5f, Sunk, Deck, -1.5f, 9.5f, IsleKit.Stone);

        for (int r = 1; r <= 5; r++)
            for (int c = 1; c <= 5; c++)
            {
                float cx = CellX(c), cz = CellZ(r);
                if (!IsHole(c, r))
                {
                    Block(bed, cx - 1.1f, cx + 1.1f, Sunk, Bed, cz - 1.1f, cz + 1.1f, Seam);
                    Block(floes, cx - 1.05f, cx + 1.05f, Bed, Deck, cz - 1.05f, cz + 1.05f, IsleKit.Ice);      // every floe alike: no "safe" colour
                    continue;
                }
                // a pit: black water 1.55 m down, a stone step along its south side to climb out in two moves, and
                // broken ice along its other three edges
                Block(bed, cx - 1.1f, cx + 1.1f, Sunk, 0.05f, cz - 1.1f, cz + 1.1f, Water);
                Block(basin, cx - 1.1f, cx + 1.1f, 0.05f, 0.85f, cz - 1.1f, cz - 0.5f, IsleKit.Stone);
                var random = new System.Random(c * 31 + r * 7);
                for (int k = 0; k < 3; k++)
                {
                    float along = -0.7f + 0.7f * k + (float)(random.NextDouble() - 0.5) * 0.2f;
                    float tilt = (float)(random.NextDouble() - 0.5) * 24f;
                    float w = 0.45f + (float)random.NextDouble() * 0.2f, y0 = Bed - 0.06f - (float)random.NextDouble() * 0.05f;
                    Kit.AddBox(floes, new Vector3(cx + along, y0, cz + 1.1f - 0.12f), new Vector3(w, 0.09f, 0.22f), IsleKit.Ice, tilt);           // north edge
                    Kit.AddBox(floes, new Vector3(cx - 1.1f + 0.12f, y0, cz + along * 0.8f + 0.2f), new Vector3(0.22f, 0.09f, w), IsleKit.Ice, tilt);      // west edge
                    Kit.AddBox(floes, new Vector3(cx + 1.1f - 0.12f, y0, cz + along * 0.8f + 0.2f), new Vector3(0.22f, 0.09f, w), IsleKit.Ice, -tilt);     // east edge
                }
            }

        // the entry landing (x -5.7..-3.1, z -4.7..-2.5, at the deck's height) and its stair down to the west
        Block(basin, -5.7f, -3.1f, Sunk, Deck, -4.7f, -2.5f, IsleKit.Stone);
        for (int k = 1; k <= 5; k++)
            Block(basin, -5.7f - k * 0.3f, -5.7f - (k - 1) * 0.3f, Sunk, Deck * (6 - k) / 6f, -3.9f, -2.9f, IsleKit.Stone);

        // the exit jetty: three 0.5 m steps up from the rim, a platform at 3.1 and an open north end (a 3.1 m drop)
        float[] treadZ = { 10.5f, 10.9f, 11.3f, 11.7f, 12.9f };
        float[] treadTop = { 2.1f, 2.6f, 3.1f, 3.1f };
        for (int k = 0; k < 4; k++)
        {
            Block(basin, -0.8f, 0.8f, Sunk, treadTop[k], treadZ[k], treadZ[k + 1], IsleKit.Stone);
            foreach (float side in new[] { -0.775f, 0.775f })
                Pickets(fence, side, treadZ[k], side, treadZ[k + 1], treadTop[k], 1.8f, 0.3f, 0.1f, IsleKit.Wood);
        }

        // the fence: 1.8 m pickets on the deck's outer edge, 3.2 m beside the landing; gaps only for the turnstile
        // (x -5.51..-3.39) and the jetty (x -0.8..0.8)
        const float tall = 3.2f, low = 1.8f;
        Pickets(fence, -6.45f, -2.45f, -5.51f, -2.45f, Deck, tall, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, -3.39f, -2.45f, -2.3f, -2.45f, Deck, tall, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, -2.3f, -2.45f, 6.45f, -2.45f, Deck, low, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, 6.45f, -2.45f, 6.45f, 10.45f, Deck, low, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, 6.45f, 10.45f, 0.8f, 10.45f, Deck, low, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, -0.8f, 10.45f, -6.45f, 10.45f, Deck, low, 0.3f, 0.1f, IsleKit.Wood);
        Pickets(fence, -6.45f, 10.45f, -6.45f, -2.45f, Deck, low, 0.3f, 0.1f, IsleKit.Wood);
        // over the turnstile, a header up to the tall fence's top; the landing's roof at 4.2 on four corner posts
        Block(fence, -5.51f, -3.39f, Deck + 2.3f, Deck + tall, -2.55f, -2.35f, IsleKit.Wood);
        foreach (Vector2 post in new[] { new Vector2(-5.64f, -4.64f), new Vector2(-3.16f, -4.64f), new Vector2(-5.64f, -2.56f), new Vector2(-3.16f, -2.56f) })
            Block(fence, post.x - 0.06f, post.x + 0.06f, Deck, 4.2f, post.y - 0.06f, post.y + 0.06f, IsleKit.DarkWood);
        Block(fence, -5.8f, -3.0f, 4.2f, 4.35f, -4.8f, -2.4f, IsleKit.DarkWood);
        // an arch over the jetty's mouth, which carries column 3's flag
        Block(fence, -0.88f, -0.8f, Deck, 4.3f, 10.4f, 10.5f, IsleKit.Wood);
        Block(fence, 0.8f, 0.88f, Deck, 4.3f, 10.4f, 10.5f, IsleKit.Wood);
        Block(fence, -0.88f, 0.88f, 4.15f, 4.3f, 10.4f, 10.5f, IsleKit.Wood);

        OnRoot("Basin", basin, snowy);
        OnRoot("Ice Bed", bed, bare);
        OnRoot("Sound Ice", floes, ice);
        OnRoot("Fence", fence, snowy);

        // row posts outside the fence (red, green, gold, blue, white from the south), and column flags on the north
        // fence with one to five stripes, counted from the left as seen from the tower
        var posts = new MeshBuilder();
        for (int r = 1; r <= 5; r++)
            foreach (float px in new[] { -7.4f, 7.4f })
            {
                float pz = CellZ(r);
                Block(posts, px - 0.09f, px + 0.09f, -0.5f, 2.4f, pz - 0.09f, pz + 0.09f, RowColours[r - 1]);
                Kit.AddBall(posts, new Vector3(px, 2.5f, pz), 0.13f, RowColours[r - 1]);
            }
        OnRoot("Row Posts", posts, bare);

        var flags = new MeshBuilder();
        for (int c = 1; c <= 5; c++)
        {
            float fx = CellX(c);
            float top = c == 3 ? 4.13f : Deck + 2.75f, bottom = top - 0.45f;      // column 3 hangs from the arch over the jetty
            if (c != 3) Block(flags, fx - 0.03f, fx + 0.03f, Deck + 1.8f, Deck + 2.78f, 10.42f, 10.48f, IsleKit.DarkWood);
            Block(flags, fx - 0.35f, fx + 0.35f, bottom, top, 10.43f, 10.47f, FlagWhite);
            for (int k = 0; k < c; k++)
            {
                float sx = fx + (k - (c - 1) / 2f) * 0.12f;
                Block(flags, sx - 0.035f, sx + 0.035f, bottom + 0.04f, top - 0.04f, 10.415f, 10.43f, IsleKit.Red);
            }
        }
        OnRoot("Column Flags", flags, bare, false);
    }

    // The watch tower: a deck at 4.0 on posts, see-through picket parapets, the solid rear wall (the 2-player board on
    // its far side), the rear landing, the roof with its eave beam and bar board, and the stair down to the east
    void BuildTower(Material snowy, Material bare)
    {
        var mb = new MeshBuilder();
        foreach (float lx in new[] { -1.45f, 1.45f })
            foreach (float lz in new[] { -6.85f, -8.7f, -10.55f })
                Block(mb, lx - 0.125f, lx + 0.125f, Sunk, TowerDeck - 0.2f, lz - 0.125f, lz + 0.125f, IsleKit.DarkWood);
        Block(mb, -1.6f, 1.6f, TowerDeck - 0.2f, TowerDeck, -10.7f, -6.7f, IsleKit.Wood);       // deck and rear landing
        Block(mb, -1.6f, 0.6f, TowerDeck, TowerRoof, -9.5f, -9.3f, IsleKit.Wood);               // rear wall; the gap x 0.6..1.6 leads round it
        foreach (Vector2 post in new[] { new Vector2(-1.55f, -6.75f), new Vector2(1.55f, -6.75f), new Vector2(1.55f, -10.65f), new Vector2(-1.55f, -10.65f) })
            Block(mb, post.x - 0.05f, post.x + 0.05f, TowerDeck, TowerRoof, post.y - 0.05f, post.y + 0.05f, IsleKit.DarkWood);
        Block(mb, -1.8f, 1.8f, TowerRoof, TowerRoof + 0.2f, -10.9f, -6.5f, IsleKit.DarkWood);
        Block(mb, -1.8f, 1.8f, 6.37f, TowerRoof, -6.8f, -6.7f, IsleKit.DarkWood);              // eave beam
        Block(mb, -0.82f, 0.82f, 5.38f, 6.37f, -6.81f, -6.72f, IsleKit.Board);                 // the board the tower's bars hang on, above every sightline
        // parapets: 1.1 m pickets 0.06 wide every 0.25 m (76 % open), north and both sides; the rear landing's three sides
        Pickets(mb, -1.6f, -6.73f, 1.6f, -6.73f, TowerDeck, 1.1f, 0.25f, 0.06f, IsleKit.Wood);
        Pickets(mb, -1.57f, -10.7f, -1.57f, -6.73f, TowerDeck, 1.1f, 0.25f, 0.06f, IsleKit.Wood);
        Pickets(mb, 1.57f, -10.7f, 1.57f, -8.4f, TowerDeck, 1.1f, 0.25f, 0.06f, IsleKit.Wood);
        Pickets(mb, 1.57f, -7.6f, 1.57f, -6.73f, TowerDeck, 1.1f, 0.25f, 0.06f, IsleKit.Wood);
        Pickets(mb, -1.6f, -10.67f, 1.6f, -10.67f, TowerDeck, 1.1f, 0.25f, 0.06f, IsleKit.Wood);
        // the stair: 15 risers from the deck's east edge (z -8.4..-7.6) down to the east, its foot at x 5.65
        for (int k = 1; k <= 14; k++)
            Block(mb, 1.6f + (k - 1) * 0.27f, 1.6f + k * 0.27f, Sunk, TowerDeck - k * (TowerDeck / 15f), -8.4f, -7.6f, IsleKit.Stone);
        OnRoot("Watch Tower", mb, snowy);
    }

    // One hut, described as the lodge (s = -1, dz = 0: outer x -15.7..-10.8, z -3.8..-0.2) and mirrored in x and moved
    // dz north for the Far Hut (s = 1). A soundproof shell (walls, roof, floor, the vestibule's baffle) under its own
    // "... Walls" object, which holds nothing else (PuzzleSoundProof turns every collider under it into a voice wall);
    // the door, switch, live lamp, intercom, ceiling lamp, room lights and the map's painted rows go under the hut's
    // group. The map is on the wall nearest the pond, out of every line from the door. Returns the ceiling lamp.
    Component BuildHut(Piece group, string label, string wallsName, string doorName, string switchName, string liveName, string intercomName,
        string lampName, string room1, string room2, float s, float dz, Component chair, Material snowy, Material bare)
    {
        float X(float x) => -s * x;
        float Z(float z) => z + dz;
        void H(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c) => Block(mb, X(x0), X(x1), y0, y1, Z(z0), Z(z1), c);

        var shell = new MeshBuilder();
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.1f;
        // west wall with the door opening exactly the Door's size (z -3.42..-2.18, up to 2.02)
        H(shell, -15.7f, -15.5f, y0, HutWall, -3.8f, -3.42f, wall);
        H(shell, -15.7f, -15.5f, y0, HutWall, -2.18f, -0.2f, wall);
        H(shell, -15.7f, -15.5f, DoorTop, HutWall, -3.42f, -2.18f, wall);
        H(shell, -11.0f, -10.8f, y0, HutWall, -3.8f, -0.2f, wall);          // east: the map's wall
        H(shell, -15.5f, -11.0f, y0, HutWall, -3.8f, -3.6f, wall);          // south: switch, live lamp, intercom
        H(shell, -15.5f, -11.0f, y0, HutWall, -0.4f, -0.2f, wall);          // north: the bars
        H(shell, -15.85f, -10.65f, HutWall, HutWall + 0.2f, -3.95f, -0.05f, roof);
        H(shell, -14.1f, -13.9f, HutFloor, HutWall, -3.6f, -1.2f, wall);     // the baffle: the way in is the 0.8 m gap at its north end
        var floor = new MeshBuilder();
        H(floor, -15.7f, -10.8f, -0.3f, HutFloor, -3.8f, -0.2f, IsleKit.DarkWood);      // under the whole footprint: no voice gap under the walls

        Piece walls = b.Empty(wallsName, X(-13.25f), Z(-2.0f), 0f, false).Under(group);
        Sdk.Set(b.AddScript(walls, "PuzzleSoundProof"), "material", 0);
        OnRoot(label + " Shell", shell, snowy).Under(walls);
        OnRoot(label + " Floor", floor, bare).Under(walls);

        // outside: a red door frame and a stone chimney (not soundproofing, so not under the walls)
        var outside = new MeshBuilder();
        H(outside, -15.78f, -15.7f, 0f, 2.12f, -3.52f, -3.42f, IsleKit.Red);
        H(outside, -15.78f, -15.7f, 0f, 2.12f, -2.18f, -2.08f, IsleKit.Red);
        H(outside, -15.78f, -15.7f, 2.02f, 2.12f, -3.52f, -2.08f, IsleKit.Red);
        H(outside, -11.85f, -11.35f, HutWall + 0.2f, HutWall + 1.1f, -1.25f, -0.75f, IsleKit.Stone);
        OnRoot(label + " Trim", outside, snowy).Under(group);

        // the map's painted rows on the inside of the map wall (row colours, black squares at the holes), 1 cm off it
        var paint = new MeshBuilder();
        for (int r = 1; r <= 5; r++)
        {
            float h = 1.0f + (r - 1) * 0.3f;
            H(paint, -11.025f, -11.005f, h - 0.14f, h + 0.14f, -2.77f, -1.23f, RowColours[r - 1]);
            for (int c = 1; c <= 5; c++)
                if (IsHole(c, r))
                {
                    float z = -2.0f - s * (3 - c) * 0.3f;      // lodge: column 1 to the north (the reader's left); Far Hut: to the south
                    Block(paint, X(-11.035f), X(-11.02f), h - 0.12f, h + 0.12f, z + dz - 0.12f, z + dz + 0.12f, HoleMark);
                }
        }
        OnRoot(label + " Map Paint", paint, bare, false).Under(group);

        P("Door", X(-15.6f), Z(-2.8f), 0f, -s * 90f).Named(doorName)
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true).Under(group);
        P("Switch Start", X(-13.2f), Z(-3.527f), 1.25f, 0f).Named(switchName).Under(group);
        P("Live Light", X(-12.8f), Z(-3.594f), 1.42f, 0f).Named(liveName).Under(group);
        P("Intercom Mic", X(-12.0f), Z(-3.58f), 1.35f, 0f).Named(intercomName).Add("PuzzleIntercom", "speakers", chair).Under(group);
        Piece lamp = P("Light 2", X(-12.45f), Z(-2.0f), HutWall, 0f).Named(lampName).Under(group);
        // always on, the same whatever the puzzle does: the room is never dark (Winter Walk's RoomLights)
        b.Empty(room1, X(-12.45f), Z(-2.0f), 2.2f, false).Under(group);
        b.Empty(room2, X(-14.8f), Z(-1.8f), 2.2f, false).Under(group);
        return lamp.Component("PuzzleLight");
    }

    void WireWin(Component rounds) => Sdk.Wire(rounds, "onFull", b.Puzzle, "Win");

    // A Press In Order in timed mode on an empty in its map group
    Component Order(string name, Piece group, List<Component> lamps, List<Component> buttons, Component[] rounds, Component[] phases, int roundCount, float seconds)
    {
        Piece order = b.Empty(name, 0f, 0f, 0.5f, false).Under(group);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps.ToArray());
        Sdk.Add(pio, "buttons", buttons.ToArray());
        Sdk.Add(pio, "progressBars", rounds);
        Sdk.Add(pio, "phaseBars", phases);
        Sdk.Set(pio, "rounds", roundCount);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", seconds);
        return pio;
    }
}
