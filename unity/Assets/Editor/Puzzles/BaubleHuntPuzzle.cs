// Bauble Hunt (puzzle 7): a round board on a post in a little clearing, and the baubles for it lost in the drifts
// all round, up to thirty metres out: a gleam of red or gold half buried in a mound of snow. Find them, carry them
// back, and the board is full. Spread out and call where they are. No custom code: the SDK's circle pegs and
// their board.
using Bwm;
using UnityEngine;

public sealed class BaubleHuntPuzzle : PuzzleDef
{
    public override string Name => "BaubleHunt";
    public override Vector2 Footprint => new Vector2(30f, 30f);
    public override Vector2? FixedSite => new Vector2(-876f, -2172f);      // spread out from the plaza (tools/sites: a site checker and the layout it passed)

    static readonly Color[] Colours =
    {
        new Color(0.8f, 0.1f, 0.1f), new Color(0.95f, 0.75f, 0.2f), new Color(0.15f, 0.35f, 0.85f), new Color(0.1f, 0.55f, 0.25f),
        new Color(0.85f, 0.85f, 0.9f), new Color(0.6f, 0.15f, 0.6f),
    };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -6f);
        b.ClearGround(30f, 30f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the board, on a post in the middle ----
        var post = new MeshBuilder();
        Kit.AddBox(post, Vector3.zero, new Vector3(0.3f, 1.5f, 0.3f), IsleKit.Wood);
        Kit.AddDisc(post, new Vector3(0f, 1.5f, 0f), 0.9f, 0.08f, IsleKit.DarkWood);
        b.MeshPiece("Board Post", post.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        Piece board = b.Part("Peg Board Circle", 0f, 0f, 1.58f, 180f);
        board.On("PuzzleCirclePegBoard", "onFull", b.Puzzle, "Win");

        // ---- the baubles, in drifts out to 30 m: a ring of mounds, each with a bauble half in it ----
        var random = new System.Random(707);
        float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
        Material drift = b.ShaderMaterial("BWM/Snowy", "Drift");
        for (int i = 0; i < 12; i++)
        {
            float a = i * 2.39996f + R(-0.3f, 0.3f), r = R(9f, 30f);
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            Piece mound = b.MeshPiece("Drift " + (i + 1), Kit.Mound(R(1.4f, 2.4f), R(0.5f, 0.9f), 700 + i), drift, false);
            mound.Transform.position = b.WorldPoint(x, z, -0.1f);
            Vector2 side = IsleKit.Turn(R(-0.5f, 0.5f), R(0.5f, 0.9f), a * Mathf.Rad2Deg);
            b.Part("Peg Circle", x + side.x, z + side.y, 0.18f, R(0f, 360f)).Named("Bauble " + (i + 1))
                .Set("PuzzleCirclePeg", "color", Colours[i % Colours.Length]);
            // a few empty drifts too, so finding a mound is not finding a bauble
            if (i % 3 == 0)
            {
                float a2 = a + 1.3f, r2 = R(10f, 28f);
                b.MeshPiece("Empty Drift " + (i + 1), Kit.Mound(R(1.2f, 2.2f), R(0.4f, 0.8f), 800 + i), drift, false).Transform.position = b.WorldPoint(Mathf.Sin(a2) * r2, Mathf.Cos(a2) * r2, -0.1f);
            }
        }

        IsleKit.StartCorner(b, 0f, -5f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 8f, 8f, 2.4f, true, 71);
    }
}
