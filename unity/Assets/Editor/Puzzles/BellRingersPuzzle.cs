// Bell Ringers (puzzle 4): Ring the Peal Back. A memory game for a group, with no clock.
//
// A snow-roofed stone belfry in a churchyard is one ringing room with a doorway at each end. Six coloured mats lie
// under six coloured bell ropes: three along the west wall (bells 1 to 3), two along the east wall (4 and 5), and the
// great tenor (6) in the middle, whose mat is wide enough for two. Every bell has a lamp on a board of its own colour:
// on the side wall in line with its rope, and the tenor's on the north wall. Everyone in the room sees every lamp and
// each other.
//
// Once the puzzle is started the belfry rings a short peal on the lamps, one bell after another; then the ringers
// ring it back by stepping on the same mats in the same order (Echo, in PuzzleScripts). A right step flashes its
// lamp. Each round done adds a bell: rounds of 3, 4, 5 and 6 bells, counted on two bars on the north wall, and the
// fourth wins. A wrong step only plays the same round's peal again (which is also how to see it once more). The tenor
// counts only with two ringers on its mat at once, and the mats are 3 to 8 m apart, so a group shares the bells out
// and calls the order. Two players can finish it; it is built for three or four, and is the same for every lobby
// size.
// Start or quit: the knob at the gourd's corner, or the two on the room's north wall.
// (Before the user's rules of 2026-11-24 this was Call Changes: a caller shut in a box 20 m away read a lamp map and
// called the mats over an intercom on a 5 s clock, twelve calls in a row, and then everyone pulled a lever within
// the same second. Its first rework stood a wall between two ringing rooms, each seeing only its own lamps: the
// ringers could not see each other, which the user's rules ask to be the exception, so the wall came out.)
//
// Coordinates: the belfry's are measured from its centre (x east, z north), heights from the pad's top, which is the
// root's level. The belfry stands 5 m south of the site's middle (it shared the churchyard with the Caller's box, and
// the site has not moved): Z() turns a belfry z into the puzzle's local z. Every wall part is placed from the wall
// constants below (inner face + the part's back depth from world\part-metrics.tsv + 1 cm), never by hand.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BellRingersPuzzle : PuzzleDef
{
    public override string Name => "BellRingers";
    public override Vector2 Footprint => new Vector2(26f, 46f);
    public override Vector2? FixedSite => new Vector2(-1070f, -2190f);      // a churchyard terrace above the south coast (layout-final)

    // A belfry z to the puzzle's local z
    const float Z0 = -5f;
    static float Z(float belfryZ) => belfryZ + Z0;

    // ---- the belfry ----
    const float OutX = 6.4f, OutZ = 5.0f, Wall = 0.3f, InX = OutX - Wall, InZ = OutZ - Wall;      // inner faces x +-6.1, z +-4.7
    const float WallH = 2.6f, RoofT = 0.2f, Eave = 0.3f;
    const float DoorW = 1.4f, DoorH = 2.2f, DoorX = 5.2f;      // West door in the north wall at x -5.2, East door in the south wall at x +5.2
    const float FloorTop = 0.02f;
    const float TowerHalf = 1.7f;
    const float Gap = 0.01f;      // between a wall part's back and its wall

    // Bells 1 to 6: Treble, Second, Third (west side), Fourth, Fifth (east side), Tenor (in the middle):
    // mat centres and colours
    const int Bells = 6, Tenor = 6;
    static readonly Vector2[] MatAt =
    {
        new Vector2(-4.2f, 3.2f), new Vector2(-4.2f, 0f), new Vector2(-4.2f, -3.2f),
        new Vector2(4.2f, 2.2f), new Vector2(4.2f, -2.2f), new Vector2(0f, 0f),
    };
    static readonly string[] BellHex = { "DDEEFF", "FF2A2A", "2AFF4A", "FFC21A", "2A6BFF", "B03CFF" };
    const float MatSize = 1.4f, TenorX = 2.6f, TenorZ = 1.8f, SensorH = 2.4f;

    // The peal: rounds of First to Last bells, each lamp lit for ShowSeconds with GapSeconds between two
    const int First = 3, Last = 6, Rounds = Last - First + 1;
    const float ShowSeconds = 1.0f, GapSeconds = 0.45f;

    // A bell's lamp is the game's flood lamp (Light 6: 0.48 m across, its back 0.142 m behind its pivot, its middle
    // 0.017 m and its foot 0.259 m under it, its top 0.225 m over it), as high as the roof allows, on a board of the
    // bell's colour. Its lens is near white: a strong colour tints a lens the same lit or not, a near-white one is a
    // dull disc when off and a bright one when lit.
    const float LampH = 2.3f, LampBack = 0.142f, LampDrop = 0.017f, LampFoot = 0.259f, LampRange = 3f;
    const float BoardHalf = 0.31f, BoardDeep = 0.04f;
    static readonly Color LampWhite = new Color(1f, 0.92f, 0.75f);

    // On the north wall, twice (west and east of the tenor's lamp): the rounds bar (a Progress Bar at BarScale is 2.15 m wide; its back is 0.101 m behind
    // its pivot at scale 1) and a start switch with its live lamp (backs 0.073 and 0.006 m behind their pivots)
    const float BarX = 2.7f, BarH = 1.9f, BarScale = 0.6f, BarBack = 0.101f;
    const float SwitchX = 4.0f, SwitchBack = 0.073f, LiveBack = 0.006f;

    static readonly Color Slate = new Color(0.25f, 0.27f, 0.32f);
    static readonly Color Floorboards = new Color(0.42f, 0.31f, 0.21f);
    static readonly Color Hemp = new Color(0.78f, 0.70f, 0.52f);
    static readonly Color Bronze = new Color(0.80f, 0.58f, 0.22f);

    PuzzleBuilder b;
    float rootY;

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    // Vertex colours are read as linear: this makes a painted colour look as written
    static Color Paint(Color c) => c.linear;

    // A solid block given by its extents in the belfry's coordinates (heights from the root's level), into a mesh in
    // the root's local space
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
        // Jetpacks do not fire anywhere over the churchyard (Winter Walk)
        IsleKit.CalmAir(b, "Bell Ringers", 0f, 0f, new Vector3(46f, 130f, 66f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        BuildBelfry(snowy, bare);

        // ---- mats: Presence Sensors, players only; the tenor's is as wide as its mat (Echo asks
        // for two ringers on it) ----
        for (int i = 0; i < Bells; i++)
        {
            Vector3 size = i + 1 == Tenor ? new Vector3(TenorX, SensorH, TenorZ) : new Vector3(MatSize, SensorH, MatSize);
            IsleKit.Sensor(b, "Bell Mat " + (i + 1), MatAt[i].x, Z(MatAt[i].y), size, 0f, false)
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        }

        // ---- lamps: each bell's on a board of its colour, on the side wall in line with its rope; the tenor's
        // in the middle of the north wall, facing south down the room ----
        var boards = new MeshBuilder();
        float boardLow = LampH - LampDrop - BoardHalf, boardHigh = LampH - LampDrop + BoardHalf;
        for (int i = 0; i < Bells; i++)
        {
            Color paint = Paint(Hex(BellHex[i]));
            Piece lamp;
            if (i + 1 == Tenor)
            {
                Block(boards, -BoardHalf, BoardHalf, boardLow, boardHigh, InZ - BoardDeep, InZ, paint);
                lamp = b.Part("Light 6", 0f, Z(InZ - BoardDeep - LampBack - Gap), LampH, 180f, false);
            }
            else
            {
                float side = Mathf.Sign(MatAt[i].x), wall = side * InX, face = side * (InX - BoardDeep);
                Block(boards, Mathf.Min(wall, face), Mathf.Max(wall, face), boardLow, boardHigh, MatAt[i].y - BoardHalf, MatAt[i].y + BoardHalf, paint);
                lamp = b.Part("Light 6", face - side * (LampBack + Gap), Z(MatAt[i].y), LampH, -90f * side, false);
            }
            lamp.Named("Bell Lamp " + (i + 1)).Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", LampRange);
        }
        AddMesh("Belfry Lamp Boards", boards, bare, false);

        // ---- the north wall, twice: the rounds done, and a knob to start or quit from where the ringers stand ----
        Bar("Peal Bar West", -BarX);
        Bar("Peal Bar East", BarX);
        foreach (float side in new[] { -1f, 1f })
        {
            string room = side < 0f ? "West" : "East";
            b.Part("Switch Start", side * SwitchX, Z(InZ - SwitchBack), 1.25f, 180f, false).Named(room + " Room Switch");
            b.Part("Live Light", side * SwitchX, Z(InZ - LiveBack), 1.42f, 180f, false).Named(room + " Room Live");
        }

        // Room lamps, which Winter Walk's RoomLights makes into point lights near the camera (numbers need a '.')
        int lampNo = 0;
        foreach (float x in new[] { -3.2f, 3.2f })
            foreach (float z in new[] { -2.4f, 2.4f })
                b.Empty("Room Light " + (++lampNo) + " FFD9A0 7.0 1.2", x, Z(z), 2.4f, false);
        // The rooms' boxes as "Indoors ..." empties (MineDriver's convention for where it must not snow; today it only
        // reads the pizzeria's, so these wait for that to be generalised)
        b.Empty("Indoors Bell Ringers", 0f, Z(0f), WallH / 2f, false).Transform.localScale = new Vector3(2f * InX, WallH, 2f * InZ);

        // ---- the gourd: start corner north of the belfry, facing it ----
        IsleKit.StartCorner(b, 0f, Z(12f), 180f);

        // ---- the rule: the lamps play a peal, the ringers step it back on the mats; four rounds win (Echo.cs).
        // Start deals, stop clears ----
        Component echo = b.AddCustomScript(b.Empty("Belfry Echo", 0f, Z(0f), 0.5f, false), "Echo");
        Sdk.Set(echo, "sensorPrefix", "Bell Mat ");
        Sdk.Set(echo, "lampPrefix", "Bell Lamp ");
        Sdk.Set(echo, "barPrefix", "Peal Bar");
        Sdk.Set(echo, "count", Bells);
        Sdk.Set(echo, "first", First);
        Sdk.Set(echo, "last", Last);
        Sdk.Set(echo, "pairSpot", Tenor);
        Sdk.Set(echo, "pairNeed", 2);
        Sdk.Set(echo, "showSeconds", ShowSeconds);
        Sdk.Set(echo, "gapSeconds", GapSeconds);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", echo, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", echo, "End");

        BuildChurchyard(snowy);
    }

    // A rounds bar on the north wall at belfry x, facing south
    Piece Bar(string name, float x)
        => b.Part("Progress Bar", x, Z(InZ - BarBack * BarScale - Gap), BarH, 180f, false).Named(name).Scaled(BarScale, BarScale, BarScale)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", Rounds);

    void BuildBelfry(Material snowy, Material bare)
    {
        // The pad, sunk well into the ground
        var pad = new MeshBuilder();
        Block(pad, -7.5f, 7.5f, -1.5f, 0f, -6.5f, 6.5f, IsleKit.Stone);
        AddMesh("Belfry Pad", pad, snowy, true);

        // Outer walls with the two open doorways, and the roof slab
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
        Block(walls, -OutX - Eave, OutX + Eave, WallH, WallH + RoofT, -OutZ - Eave, OutZ + Eave, Slate);
        // door frames: dark wooden posts either side of each doorway
        foreach (float x in new[] { wd0 - 0.08f, wd1 + 0.08f })
            Block(walls, x - 0.08f, x + 0.08f, 0f, DoorH + 0.1f, OutZ, OutZ + 0.06f, IsleKit.DarkWood);
        foreach (float x in new[] { ed0 - 0.08f, ed1 + 0.08f })
            Block(walls, x - 0.08f, x + 0.08f, 0f, DoorH + 0.1f, -OutZ - 0.06f, -OutZ, IsleKit.DarkWood);
        AddMesh("Belfry Walls", walls, snowy, true);

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
        for (int i = 0; i < Bells; i++)
        {
            if (i + 1 == Tenor) continue;
            Vector2 m = MatAt[i];
            Block(mats, m.x - MatSize / 2f, m.x + MatSize / 2f, FloorTop, FloorTop + 0.008f, m.y - MatSize / 2f, m.y + MatSize / 2f, cream);
            Block(mats, m.x - 0.6f, m.x + 0.6f, FloorTop, FloorTop + 0.012f, m.y - 0.6f, m.y + 0.6f, Paint(Hex(BellHex[i])));
        }
        // the tenor's mat: wide enough for two ringers side by side
        Block(mats, -TenorX / 2f, TenorX / 2f, FloorTop, FloorTop + 0.008f, -TenorZ / 2f, TenorZ / 2f, cream);
        Block(mats, -TenorX / 2f + 0.1f, TenorX / 2f - 0.1f, FloorTop, FloorTop + 0.012f, -TenorZ / 2f + 0.1f, TenorZ / 2f - 0.1f, Paint(Hex(BellHex[Tenor - 1])));
        AddMesh("Belfry Mats", mats, bare, false);

        // Bell ropes with striped sallies over each mat (two for the tenor, side by side)
        var ropes = new MeshBuilder();
        var ropeAt = new List<Vector3>();      // x, the bell's index, z
        for (int i = 0; i < Bells; i++)
            if (i + 1 != Tenor) ropeAt.Add(new Vector3(MatAt[i].x, i, MatAt[i].y));
        ropeAt.Add(new Vector3(-0.7f, Tenor - 1, 0f));
        ropeAt.Add(new Vector3(0.7f, Tenor - 1, 0f));
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
        for (int i = 0; i < Bells; i++)
        {
            float bx = (i % 3 - 1) * 0.68f, bz = i < 3 ? -0.45f : 0.45f, r = i + 1 == Tenor ? 0.3f : 0.2f + 0.015f * i;
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
