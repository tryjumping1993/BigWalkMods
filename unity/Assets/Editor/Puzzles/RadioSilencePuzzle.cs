// Radio Silence (puzzle 12): the pictures to match are down in the ravine north of the plaza, on a board on its
// floor, and the ravine is a radio dead zone: walkie-talkies and megaphones go silent inside it. The peg board is
// up on the flat seventy metres away, out of earshot. So the answer travels by runner, or by someone standing on
// the ravine's lip calling down and across. No custom code: a dead zone, a three-flap display, tiles, a peg board.
using Bwm;
using UnityEngine;

public sealed class RadioSilencePuzzle : PuzzleDef
{
    public override string Name => "RadioSilence";
    public override Vector2 Footprint => new Vector2(22f, 18f);
    public override Vector2? FixedSite => new Vector2(-760f, -730f);      // chosen on the island's lowland; its build levels and clears it

    static readonly Vector2 InRavine = new Vector2(-792f, -664f);      // on the ravine's floor (WinterIslePuzzle.Gorge)

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -6f);
        b.ClearGround(22f, 18f);
        Material snowy = IsleKit.Snowy(b);
        Vector3 site = b.Root.transform.position;
        float rx = InRavine.x - site.x, rz = InRavine.y - site.z;

        // ---- down in the ravine: the display on a board, a lamp over it, and the dead zone round it all ----
        IsleKit.BoardOnPosts(b, "Ravine Board", rx, rz, 180f, 3.2f, 1.0f, 2.6f, IsleKit.Cream);
        b.Part("Display", rx, rz - IsleKit.FaceZ - 0.04f, 1.8f, 180f).Set("PuzzleDisplay", "size", 2);      // three flaps
        Piece lamp = b.Part("Light 2", rx, rz - 1.2f, 3.4f, 180f);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", lamp.Component("PuzzleLight"), "TurnOn");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", lamp.Component("PuzzleLight"), "TurnOff");
        Piece dead = b.Part("Radio Dead Zone", rx, rz, 4f);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(60f, 22f, 26f);
        zone.center = Vector3.zero;
        Kit.LightRing(b, "Ravine Lights", rx, rz - 2f, 7f, 5f, 2.4f, true, 121);

        // ---- up on the flat: walkie-talkies (no use down there), the tiles, and the board ----
        var table = new MeshBuilder();
        Kit.AddBox(table, Vector3.zero, new Vector3(4f, 0.9f, 0.9f), IsleKit.Wood);
        Kit.AddBox(table, new Vector3(-4.6f, 0f, 0f), new Vector3(2f, 0.9f, 0.9f), IsleKit.Wood);
        b.MeshPiece("Tables", table.ToMesh(), snowy).Transform.position = b.WorldPoint(-1f, 2f);
        for (int i = 0; i < 6; i++)
            b.Part("Peg Tile", -1f - 1.5f + i * 0.6f, 2f, 0.9f + 0.24f, 0f).Named("Tile " + (i + 1)).Set("PuzzlePegTile", "image", i + 1);
        for (int i = 0; i < 3; i++)
            b.Part("Vanilla Item", -5.6f + 0.1f + i * 0.6f, 2f, 0.9f + 0.15f, 0f).Named("Walkie-Talkie " + (i + 1)).Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        IsleKit.BoardOnPosts(b, "Peg Board Wall", 6f, 3f, 180f, 4f, 0.4f, 2.4f, IsleKit.Board);
        b.Part("Peg Board", 4.5f, 3f - IsleKit.FaceZ - 0.06f, 1.3f, 180f).Set("PuzzlePegBoard", "size", 2);      // three slots (its pivot is at one end)
        b.Part("Validator", 7.2f, 3f - IsleKit.FaceZ - 0.06f, 2.0f, 180f);

        IsleKit.StartCorner(b, 0f, -6f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, -1f, 20f, 15f, 2.6f, true, 122);
    }
}
