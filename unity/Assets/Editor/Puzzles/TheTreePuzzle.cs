// The Tree: the winter edition's gourd tower, a great Christmas tree in the island's central plaza.
//
// Every puzzle on the island rewards a gourd, and the gourds are the tree's ornaments: round its foot is a ring
// of plinths, one per puzzle, and holding the button opens the next empty one. The SDK keeps only as many
// plinths as there are puzzles installed. With this pack present it replaces the game's red tower for modded
// gourds. Lights wind up the tree and ring the plaza.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class TheTreePuzzle : PuzzleDef
{
    public override string Name => "TheTree";
    public override int PuzzleType => 1;                                             // Tower
    public override Vector2 Footprint => new Vector2(WinterIslePuzzle.PlazaRadius * 2f, WinterIslePuzzle.PlazaRadius * 2f);
    public override Vector2? FixedSite => WinterIslePuzzle.Plaza;

    const float TreeHeight = 16f;
    const int Plinths = 24;
    const float RingRadius = 6.2f;
    static readonly Color Stone = new Color(0.50f, 0.52f, 0.56f);
    static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color Gold = new Color(1f, 0.78f, 0.2f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -14f);
        Material snowy = b.ShaderMaterial("BWM/Snowy", "Snowy");
        Material stone = b.ShaderMaterial("BWM/Snowy", "Stone", m => { m.SetFloat("_SnowLine", 0.93f); m.SetFloat("_SnowNoise", 0.5f); });
        Material bare = b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f));      // never takes snow

        // ---- the tree, on a round stone step ----
        var step = new MeshBuilder();
        Kit.AddDisc(step, Vector3.zero, RingRadius + 1.6f, 0.3f, Stone, 32);
        Kit.AddDisc(step, Vector3.up * 0.3f, RingRadius - 1.4f, 0.3f, Stone * 0.92f, 32);
        b.MeshPiece("Step", step.ToMesh(), stone);

        var tree = new MeshBuilder();
        Kit.AddPine(tree, Vector3.up * 0.6f, TreeHeight, 2026);
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(), snowy, false);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * (0.6f + TreeHeight * 0.2f);
        trunk.radius = 0.7f;
        trunk.height = TreeHeight * 0.45f;

        var star = new MeshBuilder();
        Kit.AddStar(star, Vector3.up * (0.6f + TreeHeight + 0.55f), 0.9f, 0.22f, Gold);
        b.MeshPiece("Star", star.ToMesh(), bare, false);

        // ---- lights winding up the tree: five turns, hugging the boughs ----
        var spiral = new List<Vector3>();
        const int points = 60;
        for (int i = 0; i <= points; i++)
        {
            float t = (float)i / points;
            float y = 0.6f + TreeHeight * (0.17f + 0.78f * t);
            float r = TreeHeight * 0.275f * (1f - t * 0.93f) + 0.15f;
            float a = t * 5f * 2f * Mathf.PI;
            spiral.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }
        Kit.LightString(b, "Tree Lights", spiral, 0.06f, 0.42f, false, false, 5);

        // ---- the ornaments' places: a ring of plinths facing outwards, filled clockwise from the south ----
        var plinths = new List<UnityEngine.Object>();
        for (int i = 0; i < Plinths; i++)
        {
            float a = Mathf.PI * 1.5f - 2f * Mathf.PI * i / Plinths;      // start at -z (south), go clockwise seen from above
            float x = Mathf.Cos(a) * RingRadius, z = Mathf.Sin(a) * RingRadius;
            Piece plinth = b.Empty("Gourd Plinth " + (i + 1), x, z, 0.3f, false);
            plinth.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f);
            plinths.Add(b.AddScript(plinth, "PuzzleGourdPlinth"));
        }

        // ---- the button that opens the next plinth, on a post in front of the tree ----
        b.Box("Button Post", 0f, -(RingRadius + 3.2f), new Vector3(0.3f, 1.35f, 0.22f), Wood, 0f, 0f, false);
        Piece button = b.Part("Button Hold", 0f, -(RingRadius + 3.2f) - 0.11f, 1.15f, 180f, false);
        Sdk.Add(b.AddScript(button, "TowerButton"), "plinths", plinths.ToArray());

        // ---- lights round the plaza: a sixteen-sided ring, open to the south where the path comes in ----
        var ring = new List<Vector3>();
        float outer = WinterIslePuzzle.PlazaRadius - 2.5f;
        for (int i = 1; i <= 15; i++)
        {
            float a = Mathf.PI * 1.5f + 2f * Mathf.PI * i / 16f;
            ring.Add(new Vector3(Mathf.Cos(a) * outer, 3f, Mathf.Sin(a) * outer));
        }
        Kit.LightString(b, "Plaza Lights", ring, 0.3f, 0.5f, true, true, 9);
    }
}
