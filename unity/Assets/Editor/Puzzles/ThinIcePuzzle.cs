// Thin Ice: a frozen pond of 25 floes in a 5 x 5 grid on a raised stone basin behind a tall picket fence, five of them
// broken to open black water. A puzzle for two, in the open, with no clock.
//
// The crosser puts on the blindfold on the roofed entry landing and goes through the blind turnstile onto the ice. The
// guide climbs the watch tower. On the tower's rear wall, behind the guide as they watch the pond, hangs a lamp map of
// the floes: one lamp is lit. The guide reads it, turns back to the pond and talks the blind crosser to that floe,
// round the holes. When the crosser has stood on it for a moment it counts, its lamp goes out and another lights
// (Seek, in PuzzleScripts). Six floes win the gourd. Stepping on other floes on the way costs nothing; a fall into a
// hole costs the climb out (a stone step on each pit's south side). The two talk freely: nobody is shut in.
//
// The ice is blind-only: BlindWatch (PuzzleScripts) puts anyone on the pond who is not wearing the blindfold back
// outside, so the crosser's half cannot be done by looking. That makes two players the fewest who can finish it.
// (Before the user's rules of 2026-11-24 this was a three-link relay on a 17 s phase clock: a spotter shut in a
// soundproof lodge with the map, an intercom into the headphones of a chair on the tower, the watcher's voice.)
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
    const float LampRange = 0.15f;      // map lamps: the light stays on the board
    const int Rounds = 6;               // floes to reach
    const float Dwell = 1.5f;           // seconds on the lit floe before it counts

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

    // A Progress Bar (the only scalable kind of part with Glass and Timer)
    void Bar(string name, float x, float z, float height, float yaw, Vector3 scale, int total)
        => P("Progress Bar", x, z, height, yaw).Named(name).Scaled(scale.x, scale.y, scale.z)
            .Set("PuzzleProgressBar", "total", total).Set("PuzzleProgressBar", "style", 0);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(36f, 32f, 0f, 1.5f);
        b.DebugTeleport(-8.5f, -5.5f, 1.5f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Material ice = b.ShaderMaterial("BWM/Snowy", "Ice", m => m.SetFloat("_SnowLine", 2f));
        // No jetpacks over the puzzle: nobody flies onto the ice
        IsleKit.CalmAir(b, "Thin Ice", 0f, 1.5f, new Vector3(44f, 120f, 40f), 0f, -10f, false);

        BuildPond(snowy, bare, ice);
        BuildTower(snowy, bare);

        // ---- the floes: one Presence Sensor each (the holes have none) ----
        for (int r = 1; r <= 5; r++)
            for (int c = 1; c <= 5; c++)
                if (!IsHole(c, r))
                    IsleKit.Sensor(b, "Floe " + c + "-" + r, CellX(c), CellZ(r), new Vector3(1.9f, 2.6f, 1.9f), Deck, false);
        IsleKit.BlindfoldHook(b, -5.4f, -4.4f, 90f, Deck, false);
        Piece gate = IsleKit.BlindGate(b, -4.4f, -2.5f, 0f, Deck, false);

        IsleKit.StartCorner(b, 6.5f, -9.5f, 0f, true, 0f, false);
        P("Switch Start", 0.38f, -9.227f, TowerDeck + 1.05f, 0f).Named("Tower Switch");
        P("Live Light", 0.38f, -9.294f, TowerDeck + 1.42f, 0f).Named("Tower Live");

        // ---- the lamp map on the tower's rear wall, facing the deck: the guide turns round to read it ----
        // Row 1 (the tower's side of the pond) is the bottom row. The board is read facing south, so its columns are
        // mirrored (column 1, the pond's west, hangs at the east end): what is on the left of the board is on the
        // left of the pond when the guide turns back to it.
        const float boardX = -0.6f, wallFace = -9.3f;
        var paint = new MeshBuilder();
        for (int r = 1; r <= 5; r++)
        {
            float h = TowerDeck + 0.5f + (r - 1) * 0.3f;
            Block(paint, boardX - 0.77f, boardX + 0.77f, h - 0.14f, h + 0.14f, wallFace, wallFace + 0.02f, RowColours[r - 1]);
            for (int c = 1; c <= 5; c++)
            {
                float x = boardX + (3 - c) * 0.3f;
                if (IsHole(c, r)) Block(paint, x - 0.12f, x + 0.12f, h - 0.12f, h + 0.12f, wallFace + 0.02f, wallFace + 0.035f, HoleMark);
                else P("Light 8", x, wallFace + 0.1f, h, 0f).Named("Tower Lamp " + c + "-" + r).Set("PuzzleLight", "range", LampRange);
            }
        }
        OnRoot("Tower Map Paint", paint, bare, false);
        Bar("Seek Bar Board", boardX, wallFace + 0.101f, TowerDeck + 2.2f, 0f, new Vector3(0.3f, 0.5f, 1f), Rounds);
        Bar("Seek Bar Tower", 0f, -6.9f, 6.15f, 180f, new Vector3(0.45f, 1f, 1f), Rounds);

        // ---- the rule: go to the lit floe; six found win (Seek.cs). Start deals, stop clears ----
        Component seek = b.AddCustomScript(b.Empty("Ice Seek", 0f, 4f, Deck, false), "Seek");
        Sdk.Set(seek, "sensorPrefix", "Floe ");
        Sdk.Set(seek, "lampPrefix", "Tower Lamp ");
        Sdk.Set(seek, "barPrefix", "Seek Bar");
        Sdk.Set(seek, "rounds", Rounds);
        Sdk.Set(seek, "dwell", Dwell);
        Sdk.Set(seek, "need", 1);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", seek, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", seek, "End");

        // ---- the ice is blind-only: anyone on the pond or the jetty without the blindfold is put back outside ----
        Piece putOut = b.Empty("Put Out", -8.3f, -3.4f, 1.0f, false);
        putOut.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, -90f, 0f);      // facing west, away from the pond
        Component watch = b.AddCustomScript(b.Empty("Blind Watch", 0f, 0f, 0f, false), "BlindWatch");
        Sdk.Set(watch, "boxMin", new Vector3(-6.5f, 0.5f, -2.5f));      // the basin x -6.5..6.5, z -2.5..10.5 and the jetty to z 12.9,
        Sdk.Set(watch, "boxMax", new Vector3(6.5f, 31f, 12.9f));        // from 0.5 m (someone in a pit rides at about 0.8) up
        Sdk.Set(watch, "putOut", putOut.Transform);
        Sdk.Add(watch, "gates", gate.Transform);
        Sdk.Set(watch, "gateSkip", 1.2f);
        Sdk.Set(watch, "cooldown", 1.5f);
        Sdk.Set(watch, "interval", 0.25f);
        Sdk.Set(watch, "dropHeld", true);
        Sdk.Set(watch, "onlyWhileRunning", false);

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

    // The watch tower: a deck at 4.0 on posts, see-through picket parapets, the solid rear wall (the lamp map on its
    // pond side), the rear landing, the roof with its eave beam and bar board, and the stair down to the east
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

}
