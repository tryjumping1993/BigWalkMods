// Snowball Range: an open range of six snow baskets on poles. Lob a snowball into each one; a basket's lamp lights
// when it has been hit, and six lamps win. One player can do it alone, any number can throw at once, there is no
// clock and a miss costs nothing: the ball lies on the snow to be picked up again, and a ball that lands in a basket
// is laid back in the bin a moment later (ItemReturn).
// (The user's rules of 2026-11-24: fewer timers and sealed rooms, and some puzzles one or two players can finish,
// "like the basketball puzzle in the base game". Before that this was a walled range worked by a caller in a hut,
// a spotter behind glass and a thrower who never saw a target.)
//
// A basket is an open-topped box, a metre square inside and 0.6 m deep, its rim 3.0 to 3.6 m up: higher than a ball
// in the hand of a jumping player can reach (hand 1.25 m, jump 1.0 m), so a ball only gets in from above. The counting
// zone fills the box. A sloped floor does not empty it: the game pins a prop that has come to rest (on 15 and on 48
// degrees the balls stayed where they landed), hence the script.
// Throw reach was never measured in the game; the heights follow the base game's own hoop (its funnel's mouth is
// about 4.6 m up). Players may stand where they like, so the far baskets are only as hard as they choose.
//
// Coordinates: local metres from the root, x east, z north (+z forward); heights above the pad top (Floor above the
// root), every piece onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowballRangePuzzle : PuzzleDef
{
    public override string Name => "SnowballRange";
    public override Vector2 Footprint => new Vector2(42f, 36f);
    public override Vector2? FixedSite => new Vector2(-2418f, -1620f);      // layout-final: a broad, quiet High Shelf meadow, off the paths

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Y0 = -0.1f;             // posts start just under the pad top, so no crack shows at their foot
    const int Balls = 12;
    const float Inner = 1.0f;           // a basket's inside, square
    const float Wall = 0.06f;           // its boards

    // The baskets: centre x, rim height, centre z. Each faces the bin at (BinX, BinZ).
    static readonly Vector3[] Baskets =
    {
        new Vector3(-7.0f, 3.0f, -2.5f), new Vector3(-4.5f, 3.3f, 0.5f), new Vector3(-2.0f, 3.1f, -3.0f),
        new Vector3(0.5f, 3.6f, 1.0f), new Vector3(3.0f, 3.0f, -2.5f), new Vector3(5.5f, 3.4f, 0.5f),
    };
    const float BinX = -0.75f, BinZ = -8.5f;

    // Part depths from part-metrics.tsv (how far the pivot stands in front of the part's back), plus 1 cm clear
    const float DepthLight6 = 0.142f + 0.01f, DepthBar = 0.101f + 0.01f;

    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);
    static readonly Color White = new Color(0.94f, 0.93f, 0.9f);

    PuzzleBuilder b;

    // A block between two corners (local x, y above the pad top, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A closed box of any orientation: its centre, size along its own axes, and its rotation
    static void OBox(MeshBuilder mb, Vector3 centre, Vector3 size, Quaternion rot, Color color)
    {
        float hx = size.x / 2f, hy = size.y / 2f, hz = size.z / 2f;
        float[] sx = { -1f, 1f, 1f, -1f }, sz = { -1f, -1f, 1f, 1f };
        var low = new Vector3[4];
        var high = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            low[i] = centre + rot * new Vector3(sx[i] * hx, -hy, sz[i] * hz);
            high[i] = centre + rot * new Vector3(sx[i] * hx, hy, sz[i] * hz);
        }
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
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

    // An empty on the pad (lamp markers, parents)
    Piece E(string name, float x, float z, float height) => b.Empty(name, x, z, Floor + height, false);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(42f, 36f);
        b.DebugTeleport(0f, -11f, 2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks over the range: nobody hovers over a basket and drops a ball in (Winter Walk's Calm Air)
        IsleKit.CalmAir(b, "Snowball Range", -1f, -0.5f, new Vector3(56f, 120f, 50f), 0f, Floor - 10f, false);

        // ---- the pad: one level slab, sunk 1.5 m so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -21f, 19f, -1.75f, 0f, -17.5f, 16.5f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        // ---- snowballs: twelve in one layer of 3 x 4 in the bin, 0.28 m apart (a ball is 0.26 across) ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Object>();
        for (int i = 0; i < Balls; i++)
        {
            float x = BinX - 0.28f + (i % 3) * 0.28f, z = BinZ - 0.42f + (i / 3) * 0.28f;
            Piece ball = b.Item("Snowball " + (i + 1), Kit.AsLooseSnow(Kit.Ball(0.13f, 8, 12, 0.1f, 2200 + i)), snow, x, z, Floor + 0.20f, 0.6f, false);
            Component item = ball.Component("PuzzleItem");
            Sdk.Set(item, "savesOnReload", false);
            Sdk.Set(item, "canBelt", true);
            Sdk.Set(item, "canBackpack", true);
            balls.Add(item);
        }

        // ---- the tally: each basket counts once; six marked wins (Tally.cs) ----
        Piece tallyAt = E("Basket Tally", 0f, -6f, 0.5f);
        Component tally = b.AddCustomScript(tallyAt, "Tally");
        Sdk.Set(tally, "count", Baskets.Length);
        Sdk.Set(tally, "lampPrefix", "Basket Lamp ");
        Sdk.Set(tally, "barPrefix", "Tally Bar");

        // ---- the baskets ----
        var boxes = new MeshBuilder();      // boards and poles (snow lies on their tops)
        var trim = new MeshBuilder();       // the striped rims (painted, no snow, no collider)
        for (int k = 0; k < Baskets.Length; k++)
        {
            Vector3 c = Baskets[k];
            float rim = c.y;
            // the basket's front looks at the bin: yaw is the way its front faces
            float yaw = Mathf.Atan2(BinX - c.x, BinZ - c.z) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 At(float across, float up, float forward) => new Vector3(c.x, 0f, c.z) + rot * new Vector3(across, up, forward);
            float half = Inner / 2f, outer = half + Wall;

            // four walls, rim - 0.6 .. rim, and a flat floor under them
            OBox(boxes, At(0f, rim - 0.3f, outer - Wall / 2f), new Vector3(Inner + 2f * Wall, 0.6f, Wall), rot, IsleKit.Wood);
            OBox(boxes, At(0f, rim - 0.3f, -outer + Wall / 2f), new Vector3(Inner + 2f * Wall, 0.6f, Wall), rot, IsleKit.Wood);
            OBox(boxes, At(-outer + Wall / 2f, rim - 0.3f, 0f), new Vector3(Wall, 0.6f, Inner), rot, IsleKit.Wood);
            OBox(boxes, At(outer - Wall / 2f, rim - 0.3f, 0f), new Vector3(Wall, 0.6f, Inner), rot, IsleKit.Wood);
            OBox(boxes, At(0f, rim - 0.62f, 0f), new Vector3(Inner + 2f * Wall, 0.04f, Inner + 2f * Wall), rot, IsleKit.DarkWood);
            // two poles, outside the side walls (the front is the lamp's, the back is where the ball comes out)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 pole = At(side * (outer + 0.08f), 0f, 0f);
                Kit.AddBox(boxes, new Vector3(pole.x, Y0, pole.z), new Vector3(0.16f, rim - 0.2f - Y0, 0.16f), IsleKit.DarkWood, yaw);
            }
            // the rim: red and white bands along the four top edges
            for (int side = 0; side < 4; side++)
            {
                Quaternion turn = rot * Quaternion.Euler(0f, 90f * side, 0f);
                for (int s = 0; s < 6; s++)
                {
                    float along = -outer + (s + 0.5f) * (2f * outer / 6f);
                    Vector3 at = new Vector3(c.x, rim + 0.03f, c.z) + turn * new Vector3(along, 0f, outer - Wall / 2f);
                    OBox(trim, at, new Vector3(2f * outer / 6f, 0.07f, Wall + 0.03f), turn, s % 2 == 0 ? IsleKit.Red : White);
                }
            }

            // the counting zone: the box's inside above the floor. Named "Catch k": ItemReturn reads these objects
            // as its catching boxes (in each one's own frame)
            Piece zone = P("Presence Sensor", c.x, c.z, rim - 0.28f, yaw).Named("Catch " + (k + 1));
            BoxCollider col = zone.Go.GetComponent<BoxCollider>();
            if (col == null) col = zone.Go.AddComponent<BoxCollider>();
            col.size = new Vector3(Inner - 0.1f, 0.4f, Inner - 0.1f);
            col.center = Vector3.zero;
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            zone.Add("PuzzlePresenceSensor", "acceptedCustom", balls.ToArray());
            zone.On("PuzzlePresenceSensor", "onItemEntered", tally, "Hit", k + 1);

            // its lamp on the front wall, facing the bin: lit once the basket has been hit
            Vector3 lampAt = At(0f, 0f, outer + DepthLight6);
            P("Light 6", lampAt.x, lampAt.z, rim - 0.3f, yaw).Named("Basket Lamp " + (k + 1))
                .Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 4f);
        }
        OnPad("Baskets", boxes, snowy);
        OnPad("Basket Rims", trim, bare, false);

        // ---- the bin and its shelter: a trough of snowballs under a little roof on four posts ----
        var stall = new MeshBuilder();
        Color bin = IsleKit.DarkWood;
        float bx0 = BinX - 0.55f, bx1 = BinX + 0.55f, bz0 = BinZ - 0.7f, bz1 = BinZ + 0.7f;
        Block(stall, bx0, bx1, 0f, 0.05f, bz0, bz1, bin);
        Block(stall, bx0, bx0 + 0.05f, 0f, 0.4f, bz0, bz1, bin);
        Block(stall, bx1 - 0.05f, bx1, 0f, 0.4f, bz0, bz1, bin);
        Block(stall, bx0 + 0.05f, bx1 - 0.05f, 0f, 0.4f, bz0, bz0 + 0.05f, bin);
        Block(stall, bx0 + 0.05f, bx1 - 0.05f, 0f, 0.4f, bz1 - 0.05f, bz1, bin);
        foreach (float px in new[] { BinX - 1.6f, BinX + 1.6f })
            foreach (float pz in new[] { BinZ - 1.1f, BinZ + 1.1f })
                Block(stall, px - 0.08f, px + 0.08f, Y0, 2.5f, pz - 0.08f, pz + 0.08f, IsleKit.Wood);
        Block(stall, BinX - 1.9f, BinX + 1.9f, 2.5f, 2.62f, BinZ - 1.4f, BinZ + 1.4f, IsleKit.Red);
        Block(stall, BinX - 1.5f, BinX + 1.5f, 2.62f, 2.74f, BinZ - 1.0f, BinZ + 1.0f, IsleKit.Red);
        OnPad("Snowball Stall", stall, snowy);
        E("Room Light 1 FFE2B8 5.0 0.9", BinX, BinZ, 2.3f);

        // ---- the score board beyond the baskets, facing the thrower: six dots, one per basket hit ----
        var board = new MeshBuilder();
        Block(board, -2.2f, -2.0f, Y0, 4.6f, 5.9f, 6.1f, IsleKit.Wood);
        Block(board, 2.0f, 2.2f, Y0, 4.6f, 5.9f, 6.1f, IsleKit.Wood);
        Block(board, -2.0f, 2.0f, 3.5f, 4.3f, 5.94f, 6.06f, IsleKit.Board);
        Block(board, -2.4f, 2.4f, 4.6f, 4.72f, 5.7f, 6.3f, IsleKit.DarkWood);
        OnPad("Score Board", board, snowy);
        P("Progress Bar", 0f, 5.94f - DepthBar, 3.9f, 180f).Named("Tally Bar Range").Set("PuzzleProgressBar", "total", Baskets.Length);

        // ---- two lamp poles, so the baskets can be seen at night (Winter Walk's room lights), and lights between them ----
        var poles = new MeshBuilder();
        Block(poles, -10.08f, -9.92f, Y0, 4.4f, 2.92f, 3.08f, IsleKit.DarkWood);
        Block(poles, 7.92f, 8.08f, Y0, 4.4f, 2.92f, 3.08f, IsleKit.DarkWood);
        Block(poles, -10.08f, -9.92f, Y0, 4.4f, -5.08f, -4.92f, IsleKit.DarkWood);
        Block(poles, 7.92f, 8.08f, Y0, 4.4f, -5.08f, -4.92f, IsleKit.DarkWood);
        OnPad("Range Poles", poles, snowy);
        E("Room Light 2 FFF1DC 16.0 1.3", -10f, 3f, 4.45f);
        E("Room Light 3 FFF1DC 16.0 1.3", 8f, 3f, 4.45f);
        float wire = Floor + 4.3f;
        Kit.LightString(b, "Range Lights", new List<Vector3>
        {
            new Vector3(-10f, wire, -5f), new Vector3(-10f, wire, 3f), new Vector3(-2.1f, Floor + 4.55f, 6f), new Vector3(2.1f, Floor + 4.55f, 6f),
            new Vector3(8f, wire, 3f), new Vector3(8f, wire, -5f),
        }, 0.35f, 0.5f, false, false, 223);
        Kit.LightString(b, "Stall Lights", new List<Vector3>
        {
            new Vector3(BinX - 1.9f, Floor + 2.45f, BinZ - 1.42f), new Vector3(BinX, Floor + 2.45f, BinZ - 1.42f), new Vector3(BinX + 1.9f, Floor + 2.45f, BinZ - 1.42f),
        }, 0.12f, 0.5f, false, false, 225);

        // ---- the start corner behind the stall, facing south (the approach) ----
        IsleKit.StartCorner(b, 0f, -12.5f, 180f, true, Floor, false);

        // ---- a ball that lands in a basket goes back to the bin (the game pins it where it lands) ----
        Piece ballHome = E("Ball Home", BinX, BinZ, 0.7f);
        Component back = b.AddCustomScript(E("Ball Return", BinX, BinZ, 1.5f), "ItemReturn");
        Sdk.Set(back, "itemPrefix", "Snowball ");
        Sdk.Set(back, "boxPrefix", "Catch ");
        Sdk.Set(back, "boxMin", new Vector3(-0.5f, -0.36f, -0.5f));      // from the floor (0.34 under the zone's middle)
        Sdk.Set(back, "boxMax", new Vector3(0.5f, 0.3f, 0.5f));          // to the rim
        Sdk.Set(back, "home", ballHome.Transform);
        Sdk.Set(back, "spread", 0.25f);
        Sdk.Set(back, "delay", 1.5f);

        // ---- wiring: a stop (and the spawn) clears the lamps and the bar ----
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", tally, "Clear");
    }
}
