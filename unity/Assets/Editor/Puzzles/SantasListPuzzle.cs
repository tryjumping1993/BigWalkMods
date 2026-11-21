// Santa's List (puzzle 8): the list is pinned up high on a tall board forty metres off, behind a fence, too far to
// read by eye: only the telescope on the platform reads it. One player reads; the others find the five pictures on
// the list among the tiles on the table and hang them on the board in order. No custom code: a five-flap display,
// a telescope, tiles, a peg board and its check screen.
using Bwm;
using UnityEngine;

public sealed class SantasListPuzzle : PuzzleDef
{
    public override string Name => "SantasList";
    public override Vector2 Footprint => new Vector2(24f, 26f);
    public override Vector2? FixedSite => new Vector2(-816f, -1332f);      // spread out from the plaza (tools/sites: a site checker and the layout it passed)

    const float ListZ = 40f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -8f);
        b.ClearGround(24f, 26f);
        b.ClearGround(10f, 10f, 0f, ListZ);
        Material snowy = IsleKit.Snowy(b);

        // ---- the list: a five-flap display high on a tall board, fenced off ----
        var tall = new MeshBuilder();
        Kit.AddBox(tall, new Vector3(-2.2f, 0f, 0f), new Vector3(0.3f, 6.2f, 0.3f), IsleKit.Wood);
        Kit.AddBox(tall, new Vector3(2.2f, 0f, 0f), new Vector3(0.3f, 6.2f, 0.3f), IsleKit.Wood);
        Kit.AddBox(tall, new Vector3(0f, 4.4f, 0.12f), new Vector3(4.6f, 1.6f, 0.14f), IsleKit.Cream);
        Kit.AddBox(tall, new Vector3(0f, 6.2f, 0.05f), new Vector3(5.2f, 0.16f, 0.8f), IsleKit.Red);
        for (int i = 0; i < 12; i++)      // the fence round its foot, too high to climb
        {
            float a = Mathf.PI * (i / 11f) + Mathf.PI;
            Kit.AddBox(tall, new Vector3(Mathf.Cos(a) * 4f, 0f, Mathf.Sin(a) * 4f + 0.5f), new Vector3(0.18f, 2.4f, 0.18f), IsleKit.DarkWood);
        }
        b.MeshPiece("List Board", tall.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, ListZ);
        b.Part("Display", 0f, ListZ - 0.08f, 4.75f, 180f).Set("PuzzleDisplay", "size", 3);      // five flaps, facing the platform

        // ---- the platform with the telescope, looking up the line at the list ----
        var platform = new MeshBuilder();
        Kit.AddBox(platform, Vector3.zero, new Vector3(3f, 0.45f, 3f), IsleKit.Stone);
        Kit.AddBox(platform, new Vector3(0f, 0f, -1.9f), new Vector3(1.6f, 0.22f, 0.8f), IsleKit.Stone);      // a step up
        Kit.AddBox(platform, new Vector3(-1.44f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 3f), IsleKit.Wood);      // rails either side
        Kit.AddBox(platform, new Vector3(1.44f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 3f), IsleKit.Wood);
        b.MeshPiece("Platform", platform.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 4f);
        b.Part("Telescope", 0f, 4.4f, 0.45f, 0f);

        // ---- the tiles on a table, and the board to hang five of them on ----
        var table = new MeshBuilder();
        Kit.AddBox(table, Vector3.zero, new Vector3(4.6f, 0.9f, 0.9f), IsleKit.Wood);
        b.MeshPiece("Table", table.ToMesh(), snowy).Transform.position = b.WorldPoint(-6f, -2f);
        for (int i = 0; i < 8; i++)
            b.Part("Peg Tile", -6f - 2f + i * 0.57f, -2f, 0.9f + 0.24f, 180f).Named("Tile " + (i + 1)).Set("PuzzlePegTile", "image", i + 1);
        IsleKit.BoardOnPosts(b, "Peg Board Wall", 6f, -2f, 180f, 4.6f, 0.4f, 2.4f, IsleKit.Board);
        // (the board's pivot is at one end: turned to face south it runs two metres east of it)
        b.Part("Peg Board", 4.0f, -2f - IsleKit.FaceZ - 0.06f, 1.3f, 180f).Set("PuzzlePegBoard", "size", 4);      // five slots
        b.Part("Validator", 7.4f, -2f - IsleKit.FaceZ - 0.06f, 2.0f, 180f);

        IsleKit.StartCorner(b, 0f, -9f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, -1f, 22f, 16f, 2.6f, true, 81);
    }
}
