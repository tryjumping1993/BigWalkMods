// The Tree: the winter edition's gourd tower, a great Christmas tree in the island's central plaza.
//
// Every puzzle on the island rewards a gourd. The first five go round the tree's foot: five plinths, and holding
// the button opens the next empty one. When all five hold a gourd the gondola opens (Winter Walk's
// GondolaDriver watches "Tree Plinth 1".."5"), and the next twenty go to the summit, reached only by the
// gondola, where a ring of plinths stands round a great gold star with its own hold button.
//
// All 25 plinths are in this one pack and in one Tower Button list (tree first), because the SDK puts gourds
// back from a save only into the plinths of a single Tower-type pack; a summit in a pack of its own would lose
// its gourds on every reload. The SDK shows only as many plinths as there are modded puzzles installed, first
// first, so the summit's plinths appear as puzzles are added. Lights wind up the tree and ring the plaza.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class TheTreePuzzle : PuzzleDef
{
    public override string Name => "TheTree";
    public override int PuzzleType => 1;                                             // Tower
    public override Vector2 Footprint => new Vector2(WinterIslePuzzle.PlazaRadius * 2f, WinterIslePuzzle.PlazaRadius * 2f);
    public override Vector2? FixedSite => WinterIslePuzzle.Plaza;

    const float TreeHeight = 19f;
    const int TreePlinths = 5, SummitPlinths = 20;
    const float SummitRing = 9f;
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

        // a great snow-laden fir, the same make as the island's (Kit.AddFir), decked with baubles
        Texture2D needles = Kit.FirBranchTexture("Assets/Puzzles/TheTree/FirBranch.png");
        Material fir = b.ShaderMaterial("BWM/Fir", "Fir", m => { m.SetTexture("_MainTex", needles); m.SetFloat("_SnowLine", 0.45f); });
        var tree = new MeshBuilder();
        Kit.AddFir(tree, Vector3.up * 0.6f, TreeHeight, 2026, 1.4f);
        Kit.AddFir(tree, Vector3.up * 0.6f, TreeHeight * 0.97f, 1225, 0.9f);      // a second set of boughs, for a full crown
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(), fir, false);

        var baubles = new MeshBuilder();
        var random = new System.Random(2512);
        Color[] colours = { new Color(0.85f, 0.08f, 0.1f), new Color(1f, 0.75f, 0.18f), new Color(0.82f, 0.85f, 0.9f), new Color(0.15f, 0.35f, 0.9f), new Color(0.75f, 0.1f, 0.12f) };
        for (int i = 0; i < 90; i++)
        {
            float t = 0.06f + 0.86f * (float)random.NextDouble();
            float y = 0.6f + TreeHeight * (0.14f + 0.79f * t);
            float r = (TreeHeight * 0.36f * Mathf.Pow(1f - t, 0.95f) + 0.25f) * (0.78f + 0.15f * (float)random.NextDouble());
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            var at = new Vector3(Mathf.Cos(a) * r, y - r * 0.42f, Mathf.Sin(a) * r);
            Kit.AddBall(baubles, at, 0.17f + 0.1f * (float)random.NextDouble() * (1f - t), colours[i % colours.Length]);
        }
        b.MeshPiece("Baubles", baubles.ToMesh(), bare, false);
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
            float r = TreeHeight * 0.31f * (1f - t * 0.93f) + 0.2f;
            float a = t * 5f * 2f * Mathf.PI;
            spiral.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }
        Kit.LightString(b, "Tree Lights", spiral, 0.06f, 0.42f, false, false, 5);

        // ---- the first five: a ring of plinths round the tree, facing outwards, filled clockwise from the south ----
        var plinths = new List<UnityEngine.Object>();
        for (int i = 0; i < TreePlinths; i++)
        {
            float a = Mathf.PI * 1.5f - 2f * Mathf.PI * i / TreePlinths;      // start at -z (south), go clockwise seen from above
            float x = Mathf.Cos(a) * RingRadius, z = Mathf.Sin(a) * RingRadius;
            Piece plinth = b.Empty("Tree Plinth " + (i + 1), x, z, 0.3f, false);
            plinth.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f);
            plinths.Add(b.AddScript(plinth, "PuzzleGourdPlinth"));
        }
        // the list fills in this order: the tree's five (the gondola), the mine tower's five (the mine), the summit's twenty
        plinths.AddRange(MineTower(b, snowy));
        var summitPlinths = Summit(b, stone, bare, snowy);
        plinths.AddRange(summitPlinths);

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

    // The mine tower, on the apron outside the mine's east portal (WinterIsleMine.Tower): a timber headframe with a
    // lantern at its top, five plinths in an arc before it and a hold button that opens them while it is held.
    // When the five hold gourds, Winter Walk opens the mine (MineDriver). Local coordinates are from the plaza.
    const int MinePlinths = 5;
    static List<UnityEngine.Object> MineTower(PuzzleBuilder b, Material snowy)
    {
        var plinths = new List<UnityEngine.Object>();
        if (!WinterIsleMine.Load()) { Debug.LogWarning("[BWM] The Tree: no mine yet (build WinterIsle first); the mine tower is left out"); return plinths; }
        Vector2 c = WinterIsleMine.Tower - WinterIslePuzzle.Plaza;
        Vector2 toTrack = WinterIsleMine.Right(0);                  // the tower stands on the track's left; this faces the loop
        Vector2 along = WinterIsleMine.Dir(0);
        float yaw = Mathf.Atan2(toTrack.x, toTrack.y) * Mathf.Rad2Deg;
        Vector3 Here(float x, float z, float up) => b.WorldPoint(c.x + x, c.y + z, up) - b.Root.transform.position;
        Vector3 At(float forward, float side, float up) { Vector2 p = toTrack * forward + along * side; return Here(p.x, p.y, up); }
        var frame = new MeshBuilder();
        Color timber = new Color(0.5f, 0.35f, 0.2f), iron = new Color(0.22f, 0.22f, 0.25f);
        Kit.AddBox(frame, At(-1.2f, 0f, -0.4f), new Vector3(4.4f, 0.6f, 4.4f), Stone, yaw);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                Kit.AddBox(frame, At(-1.2f + sz * 1.5f, sx * 1.5f, 0.2f), new Vector3(0.36f, 8.2f, 0.36f), timber, yaw);
        foreach (float up in new[] { 2.6f, 5.2f, 8.1f })
        {
            foreach (int sgn in new[] { -1, 1 })
            {
                Kit.AddBox(frame, At(-1.2f + sgn * 1.5f, 0f, up), new Vector3(3.3f, 0.26f, 0.26f), timber * 0.9f, yaw);
                Kit.AddBox(frame, At(-1.2f, sgn * 1.5f, up), new Vector3(0.26f, 0.26f, 3.3f), timber * 0.9f, yaw);
            }
        }
        Kit.AddBox(frame, At(-1.2f, 0f, 8.4f), new Vector3(4.2f, 0.3f, 4.2f), timber * 0.8f, yaw);             // the top deck
        Kit.AddBox(frame, At(-1.2f, 0f, 8.7f), new Vector3(0.3f, 1.7f, 2.2f), iron, yaw);                      // the winding wheel, edge on
        Kit.AddBox(frame, At(-1.2f, 0f, 9.15f), new Vector3(0.5f, 0.8f, 0.8f), iron * 1.3f, yaw);
        Kit.AddBox(frame, At(-1.2f, 0f, 10.4f), new Vector3(3.2f, 0.25f, 3.2f), new Color(0.55f, 0.16f, 0.13f), yaw);      // a red cap
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                Kit.AddBox(frame, At(-1.2f + sz * 1.3f, sx * 1.3f, 8.7f), new Vector3(0.2f, 1.7f, 0.2f), timber, yaw);
        b.MeshPiece("Mine Tower", frame.ToMesh(), snowy);

        for (int i = 0; i < MinePlinths; i++)
        {
            float a = (i - (MinePlinths - 1) / 2f) * 0.5f;
            Vector2 p = toTrack * (-1.2f + Mathf.Cos(a) * 4.3f) + along * (Mathf.Sin(a) * 4.3f);
            Vector2 facing = (toTrack * Mathf.Cos(a) + along * Mathf.Sin(a)).normalized;
            Piece plinth = b.Empty("Mine Plinth " + (i + 1), c.x + p.x, c.y + p.y, 0.15f, true);
            plinth.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, 0f);
            plinths.Add(b.AddScript(plinth, "PuzzleGourdPlinth"));
        }
        Vector2 post = toTrack * 1.4f - along * 3.6f;
        b.Box("Mine Button Post", c.x + post.x, c.y + post.y, new Vector3(0.3f, 1.35f, 0.22f), Wood, 0f, yaw, true);
        Vector2 face = post + toTrack * 0.11f;
        Piece button = b.Part("Button Hold", c.x + face.x, c.y + face.y, 1.15f, yaw, true);
        foreach (UnityEngine.Object p in plinths) button.On("PuzzleButton", "onPressed", p, "HoldToOpen");
        var lights = new List<Vector3>();
        for (int i = 0; i <= 6; i++)
        {
            float a = (i - 3) * 0.5f;
            Vector2 p = toTrack * (-1.2f + Mathf.Cos(a) * 5.6f) + along * (Mathf.Sin(a) * 5.6f);
            lights.Add(new Vector3(c.x + p.x, 2.2f, c.y + p.y));
        }
        Kit.LightString(b, "Mine Tower Lights", lights, 0.3f, 0.5f, true, true, 31);
        return plinths;
    }

    // The summit sanctuary, at WinterIslePuzzle.Summit: twenty plinths in a ring round a great gold star on a
    // stone column, a hold button that opens every summit plinth while it is held, lanterns and a ring of lights.
    // Local coordinates here are from the plaza (this pack's root).
    static List<UnityEngine.Object> Summit(PuzzleBuilder b, Material stone, Material bare, Material snowy)
    {
        Vector2 c = WinterIslePuzzle.Summit - WinterIslePuzzle.Plaza;
        var built = new MeshBuilder();
        Vector3 Here(float x, float z, float up) => b.WorldPoint(c.x + x, c.y + z, up) - b.Root.transform.position;
        // a round stone floor, two steps, the column and the star
        Kit.AddDisc(built, Here(0f, 0f, -0.3f), SummitRing + 2.2f, 0.45f, Stone, 40);
        Kit.AddDisc(built, Here(0f, 0f, 0.15f), 3.2f, 0.35f, Stone * 0.92f, 28);
        Kit.AddDisc(built, Here(0f, 0f, 0.5f), 1.6f, 4.5f, Stone * 0.85f, 16);
        b.MeshPiece("Summit Floor", built.ToMesh(), stone);
        var star = new MeshBuilder();
        Kit.AddStar(star, Here(0f, 0f, 7.4f), 2.4f, 0.5f, Gold);
        b.MeshPiece("Summit Star", star.ToMesh(), bare, false);
        // a second star crossing the first, so it reads from every side
        var star2 = new MeshBuilder();
        Kit.AddStar(star2, Vector3.zero, 2.4f, 0.5f, Gold);
        Piece cross = b.MeshPiece("Summit Star Cross", star2.ToMesh(), bare, false);
        cross.Transform.position = b.Root.transform.position + Here(0f, 0f, 7.4f);
        cross.Transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        var plinths = new List<UnityEngine.Object>();
        for (int i = 0; i < SummitPlinths; i++)
        {
            float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / SummitPlinths;      // from the east (the gondola's side), clockwise
            float x = Mathf.Cos(a) * SummitRing, z = Mathf.Sin(a) * SummitRing;
            Piece plinth = b.Empty("Summit Plinth " + (i + 1), c.x + x, c.y + z, 0.15f, true);
            plinth.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f);
            plinths.Add(b.AddScript(plinth, "PuzzleGourdPlinth"));
        }
        // the hold button, on a post on the gondola's side, just inside the ring
        Vector2 toGondola = (WinterIslePuzzle.GondolaTop - WinterIslePuzzle.Summit).normalized;
        Vector2 post = toGondola * (SummitRing - 3f);
        b.Box("Summit Button Post", c.x + post.x, c.y + post.y, new Vector3(0.3f, 1.35f, 0.22f), Wood, 0f, Mathf.Atan2(toGondola.x, toGondola.y) * Mathf.Rad2Deg, true);
        Vector2 face = post + toGondola * 0.11f;
        Piece button = b.Part("Button Hold", c.x + face.x, c.y + face.y, 1.15f, Mathf.Atan2(toGondola.x, toGondola.y) * Mathf.Rad2Deg, true);
        foreach (UnityEngine.Object p in plinths) button.On("PuzzleButton", "onPressed", p, "HoldToOpen");

        // lights: a ring round the sanctuary on short posts
        var ring = new List<Vector3>();
        for (int i = 0; i < 24; i++)
        {
            float a = 2f * Mathf.PI * i / 24f;
            ring.Add(new Vector3(c.x + Mathf.Cos(a) * (SummitRing + 1.6f), 1.8f, c.y + Mathf.Sin(a) * (SummitRing + 1.6f)));
        }
        ring.Add(ring[0]);
        Kit.LightString(b, "Summit Lights", ring, 0.3f, 0.5f, true, true, 24);
        return plinths;
    }
}
