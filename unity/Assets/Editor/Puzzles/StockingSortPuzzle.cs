// Stocking Sort (puzzle 13): four stockings hang from a mantelpiece, each with an emblem (a star, a bell, a tree, a
// moon), and four wrapped presents lie under the tree. Each stocking takes only its own present. Who gets what is
// written on a board on the far side of the frozen lake: there each emblem stands beside a ribbon of a present's
// colour. Someone runs to read it (or the presents are tried one by one, which takes longer). All four hung wins
// it. No custom code: hooks that take one item each, and a zone along the stockings that counts the presents in it.
using Bwm;
using UnityEngine;

public sealed class StockingSortPuzzle : PuzzleDef
{
    public override string Name => "StockingSort";
    public override Vector2 Footprint => new Vector2(24f, 22f);
    public override Vector2? FixedSite => new Vector2(-750f, -1030f);      // chosen on the island's lowland; its build levels and clears it

    static readonly Vector2 FarBoard = new Vector2(-858f, -1001f);      // across the frozen lake (WinterIslePuzzle.Lake), on its western shore
    static readonly Color[] PresentColour = { new Color(0.8f, 0.12f, 0.1f), new Color(0.12f, 0.5f, 0.22f), new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.75f, 0.2f) };
    static readonly int[] Owner = { 2, 0, 3, 1 };      // stocking i takes present Owner[i]

    // An emblem: 0 star, 1 bell, 2 tree, 3 moon, about 'size' across, standing at 'centre' facing along z
    static void Emblem(MeshBuilder mb, int which, Vector3 centre, float size)
    {
        var white = new Color(0.95f, 0.95f, 0.92f);
        if (which == 0) Kit.AddStar(mb, centre, size * 0.5f, 0.05f, IsleKit.Gold);
        else if (which == 1) { Kit.AddBall(mb, centre, size * 0.35f, IsleKit.Gold); Kit.AddBox(mb, centre + Vector3.down * size * 0.45f, new Vector3(size * 0.7f, size * 0.12f, 0.06f), IsleKit.Gold); }
        else if (which == 2) { for (int t = 0; t < 3; t++) Kit.AddBox(mb, centre + Vector3.down * size * (0.45f - t * 0.3f), new Vector3(size * (0.8f - t * 0.22f), size * 0.3f, 0.06f), IsleKit.Green); }
        else { Kit.AddBall(mb, centre, size * 0.38f, white); Kit.AddBall(mb, centre + new Vector3(size * 0.2f, size * 0.08f, -0.06f), size * 0.33f, IsleKit.Board); }
    }

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -8f);
        b.ClearGround(24f, 22f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ---- the fireplace wall, its mantel, and four stockings with their emblems ----
        var hearth = new MeshBuilder();
        Kit.AddBox(hearth, new Vector3(0f, 0f, 4f), new Vector3(8f, 2.6f, 0.6f), new Color(0.6f, 0.32f, 0.25f));      // the chimney breast
        Kit.AddBox(hearth, new Vector3(0f, 0f, 3.62f), new Vector3(1.6f, 1.0f, 0.2f), new Color(0.12f, 0.1f, 0.1f));     // the grate
        Kit.AddBox(hearth, new Vector3(0f, 1.9f, 3.55f), new Vector3(8.4f, 0.16f, 0.5f), IsleKit.DarkWood);              // the mantel
        b.MeshPiece("Fireplace", hearth.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        var stockings = new MeshBuilder();
        var hooks = new Piece[4];
        for (int i = 0; i < 4; i++)
        {
            float x = -3f + i * 2f;
            Color sock = i % 2 == 0 ? IsleKit.Red : IsleKit.Green;
            Kit.AddBox(stockings, new Vector3(x, 0.95f, 3.4f), new Vector3(0.42f, 0.8f, 0.16f), sock);       // leg
            Kit.AddBox(stockings, new Vector3(x + 0.18f, 0.85f, 3.4f), new Vector3(0.5f, 0.22f, 0.16f), sock); // foot
            Kit.AddBox(stockings, new Vector3(x, 1.65f, 3.4f), new Vector3(0.5f, 0.2f, 0.18f), IsleKit.Cream); // cuff
            Emblem(stockings, i, new Vector3(x, 2.35f, 3.38f), 0.42f);
            hooks[i] = b.Part("Hook", x, 3.3f, 1.4f, 180f).Named("Stocking " + (i + 1)).Set("PuzzleHook", "hookType", 2);
            Sdk.SetFlags(hooks[i].Component("PuzzleHook"), "acceptedVanilla", 0);
        }
        b.MeshPiece("Stockings", stockings.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the presents, under a little tree off to one side ----
        var tree = new MeshBuilder();
        Kit.AddPine(tree, Vector3.zero, 3.4f, 1301);
        b.MeshPiece("Tree", tree.ToMesh(), snowy, false).Transform.position = b.WorldPoint(-7f, 1f);
        var presents = new Piece[4];
        for (int i = 0; i < 4; i++)
        {
            var box = new MeshBuilder();
            Kit.AddBox(box, new Vector3(0f, -0.18f, 0f), new Vector3(0.42f, 0.36f, 0.42f), PresentColour[i]);
            Kit.AddBox(box, new Vector3(0f, -0.185f, 0f), new Vector3(0.44f, 0.37f, 0.08f), IsleKit.Cream);
            Kit.AddBox(box, new Vector3(0f, -0.185f, 0f), new Vector3(0.08f, 0.37f, 0.44f), IsleKit.Cream);
            presents[i] = b.Item("Present " + (i + 1), box.ToMesh(), bare, -7f + Mathf.Cos(i * 1.6f) * 1.4f, 1f + Mathf.Sin(i * 1.6f) * 1.4f, 0.2f, 1.5f);
        }
        for (int i = 0; i < 4; i++) hooks[i].Add("PuzzleHook", "acceptedCustom", presents[Owner[i]].Component("PuzzleItem"));

        // ---- the zone along the stockings, at the height they hang: all four presents in it wins ----
        Piece zone = IsleKit.Sensor(b, "Stocking Zone", 0f, 3.2f, new Vector3(8f, 0.9f, 1.0f), 0.85f);
        zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 4);
        foreach (Piece p in presents) zone.Add("PuzzlePresenceSensor", "acceptedCustom", p.Component("PuzzleItem"));
        zone.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");

        // ---- the far board, across the lake: each emblem beside its present's colour ----
        Vector3 site = b.Root.transform.position;
        float fx = FarBoard.x - site.x, fz = FarBoard.y - site.z;
        float faceYaw = Mathf.Atan2(-fx, -fz) * Mathf.Rad2Deg;      // facing back across the lake towards the site
        IsleKit.BoardOnPosts(b, "Far Board", fx, fz, faceYaw, 3.4f, 0.6f, 2.6f, IsleKit.Cream);
        var key = new MeshBuilder();
        for (int i = 0; i < 4; i++)
        {
            Vector2 row = IsleKit.Turn(-1.1f + (i % 2) * 1.9f, 0.14f, faceYaw);
            float y = 2.05f - (i / 2) * 0.9f;
            Emblem(key, i, new Vector3(row.x, y, row.y), 0.38f);
            Vector2 ribbon = IsleKit.Turn(-1.1f + (i % 2) * 1.9f + 0.55f, 0.14f, faceYaw);
            Kit.AddBox(key, new Vector3(ribbon.x, y - 0.18f, ribbon.y), new Vector3(0.45f, 0.36f, 0.04f), PresentColour[Owner[i]], faceYaw);
        }
        b.MeshPiece("Far Board Key", key.ToMesh(), bare, false).Transform.position = b.WorldPoint(fx, fz);

        IsleKit.StartCorner(b, 5f, -6f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 1f, 22f, 18f, 2.6f, true, 131);
    }
}
