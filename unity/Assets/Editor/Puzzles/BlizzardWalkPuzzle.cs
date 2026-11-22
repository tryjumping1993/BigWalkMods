// Blizzard Walk: a whiteout maze with a three-link, one-way relay (3+ players; 2 with the speaker variant).
//
// A 21 x 21 m maze of 2.6 m packed-snow walls has twelve identical dead-end alcoves, each a Presence Sensor pad. A
// caged crow's nest stands 14 m over the maze's closed centre cell on four stilts, its floor slatted so the maze shows
// straight down through it; the only way up is the roofed switchback stair tower north of the maze and a caged bridge
// (one closed enclosure, its only opening the tower's ground door). Every phase one alcove is lit, but its lamp shows
// only on a plan without walls inside a windowless signal hut 289 m away (an ExtraSite on a knoll). The Caller in the
// hut talks into an intercom that plays only into the headphones of the nest's two Listening Chairs; the Lookout sits
// in one and shouts directions down to the Walker. When the phase bar runs out the Walker must stand in the lit
// alcove and nobody in any other (Press In Order, timed mode). Six right in a row win (3 players: 22 s phases; 4: eight
// at 20 s, a second Walker helping; 2: five at 26 s, with four intercom speakers in the maze instead of the Lookout).
// A miss empties the bars and a 6 s regroup clock restarts the run. Radio dead zones round the maze and the hut, a
// two-way No Items turnstile at the hut and Calm Air over both keep walkie-talkies, items and jetpacks out of it.
// Quit: a Start Switch at the archway, in the hut, in the nest and inside the maze; stopping ends every copy and
// resets the regroup clock. No custom code. Spec: puzzle-specs/BlizzardWalk.md (revision 2).
//
// Maze-local coordinates: metres from the maze centre, x east, z north, heights above the root's level (the island
// levels the site). Hut parts use hut-local (hx, hz), the hut's +z towards the maze, its door in its south wall.
using System.Collections.Generic;
using System.Globalization;
using Bwm;
using UnityEngine;

public sealed class BlizzardWalkPuzzle : PuzzleDef
{
    public override string Name => "BlizzardWalk";
    public override Vector2 Footprint => new Vector2(30f, 50f);      // levelled as a circle of radius 29.2 m
    public override Vector2? FixedSite => new Vector2(-1160f, -980f);      // layout-final: a level shelf by a wood, off pistes and the rail bench

    // The signal hut: an ExtraSite on a knoll 289 m south of the maze, the rise between (layout-final)
    public static readonly Vector2 HutSite = new Vector2(-1167f, -1269f);
    const float HutRadius = 8f;
    public override IEnumerable<Vector3> ExtraSites => new[] { new Vector3(HutSite.x, HutSite.y, HutRadius) };

    const float WallH = 2.6f, WallT = 0.4f, Sink = 0.5f, RidgeH = 0.35f;
    const float Deck = 14f, CageTop = 16.4f;
    const float HutFloor = 0.05f;      // the hut's floor top above the ground at its centre

    // Interior walls (each a 3 m cell edge, built 3.4 m long): "x=c zA..B" runs north-south on x = c, "z=c xA..B"
    // east-west on z = c. A Prim maze on a 7 x 7 grid of 3 m cells, centre cell closed, seed 2563 (spec section 5).
    static readonly string[] Walls =
    {
        "x=-7.5 z-10.5..-7.5", "z=-4.5 x-10.5..-7.5", "z=-1.5 x-10.5..-7.5", "x=-7.5 z1.5..4.5",
        "x=-4.5 z-10.5..-7.5", "z=-4.5 x-7.5..-4.5", "x=-4.5 z-1.5..1.5", "x=-4.5 z1.5..4.5",
        "z=4.5 x-7.5..-4.5", "x=-4.5 z4.5..7.5", "z=7.5 x-7.5..-4.5", "x=-4.5 z7.5..10.5",
        "z=-7.5 x-4.5..-1.5", "z=-4.5 x-4.5..-1.5", "x=-1.5 z-1.5..1.5", "z=1.5 x-4.5..-1.5",
        "z=4.5 x-4.5..-1.5", "z=7.5 x-4.5..-1.5", "x=1.5 z-7.5..-4.5", "z=-1.5 x-1.5..1.5",
        "x=1.5 z-1.5..1.5", "z=1.5 x-1.5..1.5", "x=1.5 z4.5..7.5", "x=1.5 z7.5..10.5",
        "x=4.5 z-10.5..-7.5", "z=-7.5 x1.5..4.5", "x=4.5 z-7.5..-4.5", "x=4.5 z4.5..7.5",
        "x=4.5 z7.5..10.5", "x=7.5 z-7.5..-4.5", "z=-1.5 x4.5..7.5", "z=1.5 x4.5..7.5",
        "x=7.5 z1.5..4.5", "z=-7.5 x7.5..10.5", "z=-1.5 x7.5..10.5", "z=4.5 x7.5..10.5",
        "z=7.5 x7.5..10.5",
    };

    // The twelve dead-end alcoves in pad order: centre (x, z) and the way it opens (ox, oz)
    static readonly (float x, float z, int ox, int oz)[] Pads =
    {
        (-3f, 9f, 1, 0), (3f, 9f, 0, -1), (9f, 9f, -1, 0), (-3f, 6f, 1, 0), (-6f, 3f, 0, -1), (9f, 3f, 0, -1),
        (-3f, 0f, 0, -1), (-9f, -3f, 1, 0), (3f, -6f, 0, 1), (9f, -6f, 0, 1), (-3f, -9f, 1, 0), (9f, -9f, -1, 0),
    };

    PuzzleBuilder b;
    Vector2 hut;            // the hut's centre in maze-local coordinates
    float hutYaw, hutBase;  // its heading (its +z towards the maze) and its floor's height above the root's level

    // ---- geometry helpers ----

    // An axis-aligned block from its two corners
    static void Blk(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);

    // A triangle facing 'outward'
    static void Face(MeshBuilder mb, Vector3 a, Vector3 p, Vector3 q, Vector3 outward, Color c)
    {
        if (Vector3.Dot(Vector3.Cross(p - a, q - a), outward) < 0f) mb.Tri(a, q, p, c);
        else mb.Tri(a, p, q, c);
    }

    // A closed six-sided block from eight corners: 0..3 one end's loop, 4..7 the other end's in the same order
    static void AddHull(MeshBuilder mb, Vector3[] p, Color c)
    {
        Vector3 centre = Vector3.zero;
        foreach (Vector3 q in p) centre += q;
        centre /= p.Length;
        int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        foreach (int[] f in faces)
        {
            Vector3 outward = (p[f[0]] + p[f[1]] + p[f[2]] + p[f[3]]) / 4f - centre;
            Face(mb, p[f[0]], p[f[1]], p[f[2]], outward, c);
            Face(mb, p[f[0]], p[f[2]], p[f[3]], outward, c);
        }
    }

    // A beam 'w' x 'h' in section from a to e (braces)
    static void AddBeam(MeshBuilder mb, Vector3 a, Vector3 e, float w, float h, Color c)
    {
        Vector3 dir = (e - a).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side = side.normalized * (w / 2f);
        Vector3 up = Vector3.Cross(dir, side).normalized * (h / 2f);
        AddHull(mb, new[] { a - side - up, a + side - up, a + side + up, a - side + up, e - side - up, e + side - up, e + side + up, e - side + up }, c);
    }

    // A steep snow ridge along a wall top from a to e (both at the wall's top), so the walls are no walkway
    static void AddRidge(MeshBuilder mb, Vector3 a, Vector3 e, float halfWidth, float height, Color c)
    {
        Vector3 dir = (e - a).normalized, side = Vector3.Cross(Vector3.up, dir).normalized * halfWidth, up = Vector3.up * height;
        Vector3 l0 = a - side, r0 = a + side, t0 = a + up, l1 = e - side, r1 = e + side, t1 = e + up;
        Vector3 centre = (a + e) / 2f + up / 3f;
        Face(mb, l0, l1, t1, (l0 + l1 + t1 + t0) / 4f - centre, c); Face(mb, l0, t1, t0, (l0 + l1 + t1 + t0) / 4f - centre, c);
        Face(mb, r0, r1, t1, (r0 + r1 + t1 + t0) / 4f - centre, c); Face(mb, r0, t1, t0, (r0 + r1 + t1 + t0) / 4f - centre, c);
        Face(mb, l0, r0, t0, -dir, c);
        Face(mb, l1, r1, t1, dir, c);
    }

    // A wall on a grid line from (x0, z0) to (x1, z1) (one of them equal), with its ridge cap; 'extend' past each end
    static void AddWall(MeshBuilder mb, float x0, float z0, float x1, float z1, float extend, Color c)
    {
        if (Mathf.Abs(x1 - x0) < 0.01f)
        {
            float za = Mathf.Min(z0, z1) - extend, zb = Mathf.Max(z0, z1) + extend;
            Blk(mb, x0 - WallT / 2f, x0 + WallT / 2f, -Sink, WallH, za, zb, c);
            AddRidge(mb, new Vector3(x0, WallH, za), new Vector3(x0, WallH, zb), WallT / 2f, RidgeH, c);
        }
        else
        {
            float xa = Mathf.Min(x0, x1) - extend, xb = Mathf.Max(x0, x1) + extend;
            Blk(mb, xa, xb, -Sink, WallH, z0 - WallT / 2f, z0 + WallT / 2f, c);
            AddRidge(mb, new Vector3(xa, WallH, z0), new Vector3(xb, WallH, z0), WallT / 2f, RidgeH, c);
        }
    }

    // A row of 0.06 m cage posts from p to q (x, z), 0.30 m apart at most (gaps 0.24 m or less), ends included
    static void PostRow(MeshBuilder mb, Vector2 p, Vector2 q, float y0, float y1, Color c)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((q - p).magnitude / 0.30f));
        for (int i = 0; i <= n; i++)
        {
            Vector2 at = Vector2.Lerp(p, q, (float)i / n);
            Kit.AddBox(mb, new Vector3(at.x, y0, at.y), new Vector3(0.06f, y1 - y0, 0.06f), c);
        }
    }

    // A rail 0.06 m square from p to q (x, z) at height y
    static void Rail(MeshBuilder mb, Vector2 p, Vector2 q, float y, Color c)
    {
        Vector2 mid = (p + q) / 2f, d = q - p;
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        Kit.AddBox(mb, new Vector3(mid.x, y, mid.y), new Vector3(0.06f, 0.06f, d.magnitude + 0.06f), c, yaw);
    }

    // One cage wall: posts, and rails at the bottom, the middle and under the roof
    static void CageRun(MeshBuilder mb, Vector2 p, Vector2 q, Color post, Color rail)
    {
        PostRow(mb, p, q, Deck, CageTop, post);
        Rail(mb, p, q, Deck, rail);
        Rail(mb, p, q, (Deck + CageTop) / 2f - 0.03f, rail);
        Rail(mb, p, q, CageTop - 0.06f, rail);
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    // ---- hut helpers ----

    Vector2 HutLocal(float hx, float hz) => hut + IsleKit.Turn(hx, hz, hutYaw);

    Piece HutPart(string prefab, float hx, float hz, float h, float yaw)
    {
        Vector2 p = HutLocal(hx, hz);
        return b.Part(prefab, p.x, p.y, hutBase + h, hutYaw + yaw, false);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        Transform root = b.Root.transform;
        Vector3 hutInRoot = root.InverseTransformPoint(new Vector3(HutSite.x, root.position.y, HutSite.y));
        hut = new Vector2(hutInRoot.x, hutInRoot.z);
        hutYaw = Mathf.Atan2(-hut.x, -hut.y) * Mathf.Rad2Deg;      // the hut's +z points at the maze, so its door faces away
        hutBase = b.WorldPoint(hut.x, hut.y, 0f, true).y - root.position.y + HutFloor;

        b.DebugTeleport(0f, -17f);
        b.ClearGround(30f, 50f);
        b.ClearGround(16f, 16f, hut.x, hut.y);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ================= the maze =================
        var maze = new MeshBuilder();
        // perimeter, 21 x 21, with a 3 m archway in the south wall
        AddWall(maze, -10.5f, -10.5f, -10.5f, 10.5f, 0.2f, IsleKit.Cream);
        AddWall(maze, 10.5f, -10.5f, 10.5f, 10.5f, 0.2f, IsleKit.Cream);
        AddWall(maze, -10.5f, 10.5f, 10.5f, 10.5f, 0.2f, IsleKit.Cream);
        Blk(maze, -10.7f, -1.5f, -Sink, WallH, -10.7f, -10.3f, IsleKit.Cream);
        AddRidge(maze, new Vector3(-10.7f, WallH, -10.5f), new Vector3(-1.5f, WallH, -10.5f), WallT / 2f, RidgeH, IsleKit.Cream);
        Blk(maze, 1.5f, 10.7f, -Sink, WallH, -10.7f, -10.3f, IsleKit.Cream);
        AddRidge(maze, new Vector3(1.5f, WallH, -10.5f), new Vector3(10.7f, WallH, -10.5f), WallT / 2f, RidgeH, IsleKit.Cream);
        // interior walls
        for (int i = 0; i < Walls.Length; i++)
        {
            string[] parts = Walls[i].Split(' ');
            float onLine = F(parts[0].Substring(2));
            string[] span = parts[1].Substring(1).Split(new[] { ".." }, System.StringSplitOptions.None);
            float s0 = F(span[0]), s1 = F(span[1]);
            Color c = i % 3 == 0 ? IsleKit.Ice : IsleKit.Cream;
            if (parts[0][0] == 'x') AddWall(maze, onLine, s0, onLine, s1, 0.2f, c);
            else AddWall(maze, s0, onLine, s1, onLine, 0.2f, c);
        }
        // the red-and-gold arch over the archway (its posts on the wall ends, outside the 3 m gap)
        Blk(maze, -1.8f, -1.5f, -Sink, 3.7f, -10.75f, -10.25f, IsleKit.Red);
        Blk(maze, 1.5f, 1.8f, -Sink, 3.7f, -10.75f, -10.25f, IsleKit.Red);
        Blk(maze, -1.8f, 1.8f, 3.4f, 3.75f, -10.75f, -10.25f, IsleKit.Red);
        Blk(maze, -1.6f, 1.6f, 3.5f, 3.62f, -10.8f, -10.75f, IsleKit.Gold);
        Kit.AddBall(maze, new Vector3(0f, 3.92f, -10.5f), 0.17f, IsleKit.Gold);
        b.MeshPiece("Maze Walls", maze.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // pad markers (identical, unnumbered): eight red lantern stones round each alcove and a gold-capped post at its
        // closed end; painted (no snow), so they read from the nest
        var markers = new MeshBuilder();
        var pads = new UnityEngine.Object[Pads.Length];
        for (int i = 0; i < Pads.Length; i++)
        {
            var (px, pz, ox, oz) = Pads[i];
            Vector2 centre = new Vector2(px, pz), closed = new Vector2(-ox, -oz), across = new Vector2(oz, -ox);
            foreach (float u in new[] { -1f, 0f, 1f })
                foreach (float v in new[] { -1f, 1f })
                {
                    Vector2 at = centre + closed * u + across * v;
                    Kit.AddBox(markers, new Vector3(at.x, 0f, at.y), new Vector3(0.25f, 0.25f, 0.25f), IsleKit.Red);
                }
            foreach (float v in new[] { -0.5f, 0.5f })
            {
                Vector2 at = centre + closed * 1f + across * v;
                Kit.AddBox(markers, new Vector3(at.x, 0f, at.y), new Vector3(0.25f, 0.25f, 0.25f), IsleKit.Red);
            }
            Vector2 post = centre + closed * 1.05f;
            Kit.AddBox(markers, new Vector3(post.x, -0.2f, post.y), new Vector3(0.14f, 1.1f, 0.14f), IsleKit.Wood);
            Kit.AddBox(markers, new Vector3(post.x, 0.9f, post.y), new Vector3(0.22f, 0.14f, 0.22f), IsleKit.Gold);
            // the pad: a 2 m sensor shifted 0.3 m towards the closed end, so it never reaches the corridor
            Vector2 s = centre + closed * 0.3f;
            pads[i] = IsleKit.Sensor(b, "Pad " + (i + 1), s.x, s.y, new Vector3(2.0f, 2.6f, 2.0f), 0f, false).Component("PuzzlePresenceSensor");
        }
        b.MeshPiece("Pad Markers", markers.ToMesh(), bare).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // ================= stilts, crow's nest and caged bridge =================
        var nest = new MeshBuilder();
        foreach (float sx in new[] { -1.5f, 1.5f })
            foreach (float sz in new[] { -1.5f, 1.5f })
                Blk(nest, sx - 0.15f, sx + 0.15f, -Sink, Deck - 0.2f, sz - 0.15f, sz + 0.15f, IsleKit.DarkWood);
        // crossbeams at 3.6 m, a mounting plate under each for the in-maze phase bars, X-braces from 6 to 13 m
        Blk(nest, -1.7f, 1.7f, 3.6f, 3.85f, -1.6f, -1.4f, IsleKit.Wood);
        Blk(nest, -1.7f, 1.7f, 3.6f, 3.85f, 1.4f, 1.6f, IsleKit.Wood);
        Blk(nest, -1.6f, -1.4f, 3.6f, 3.85f, -1.7f, 1.7f, IsleKit.Wood);
        Blk(nest, 1.4f, 1.6f, 3.6f, 3.85f, -1.7f, 1.7f, IsleKit.Wood);
        Blk(nest, -1.35f, 1.35f, 3.1f, 3.6f, -1.69f, -1.63f, IsleKit.Board);
        Blk(nest, -1.35f, 1.35f, 3.1f, 3.6f, 1.63f, 1.69f, IsleKit.Board);
        Blk(nest, -1.69f, -1.63f, 3.1f, 3.6f, -1.35f, 1.35f, IsleKit.Board);
        Blk(nest, 1.63f, 1.69f, 3.1f, 3.6f, -1.35f, 1.35f, IsleKit.Board);
        for (int side = 0; side < 4; side++)
        {
            Vector2 a = IsleKit.Turn(-1.5f, -1.7f, side * 90f), e = IsleKit.Turn(1.5f, -1.7f, side * 90f);
            AddBeam(nest, new Vector3(a.x, 6f, a.y), new Vector3(e.x, 13f, e.y), 0.08f, 0.08f, IsleKit.Wood);
            AddBeam(nest, new Vector3(e.x, 6f, e.y), new Vector3(a.x, 13f, a.y), 0.08f, 0.08f, IsleKit.Wood);
        }
        // the nest's floor: two joists on the stilts, 0.06 m slats with 0.24 m gaps, cantilevered 0.7 m past the stilts
        Blk(nest, -2.2f, 2.2f, Deck - 0.2f, Deck - 0.05f, -1.575f, -1.425f, IsleKit.DarkWood);
        Blk(nest, -2.2f, 2.2f, Deck - 0.2f, Deck - 0.05f, 1.425f, 1.575f, IsleKit.DarkWood);
        for (int i = 0; i <= 15; i++)
        {
            float x = Mathf.Lerp(-2.17f, 2.17f, i / 15f);
            Blk(nest, x - 0.03f, x + 0.03f, Deck - 0.05f, Deck, -2.2f, 2.2f, IsleKit.Wood);
        }
        // the bridge's floor: two edge joists, slats across it, x -0.2..1.2, z 2.2..11.0
        Blk(nest, -0.2f, -0.1f, Deck - 0.25f, Deck - 0.05f, 2.2f, 11.0f, IsleKit.DarkWood);
        Blk(nest, 1.1f, 1.2f, Deck - 0.25f, Deck - 0.05f, 2.2f, 11.0f, IsleKit.DarkWood);
        for (int i = 0; i <= 29; i++)
        {
            float z = Mathf.Lerp(2.23f, 10.97f, i / 29f);
            Blk(nest, -0.2f, 1.2f, Deck - 0.05f, Deck, z - 0.03f, z + 0.03f, IsleKit.Wood);
        }
        // the cage: the nest's four sides (the north side open only onto the bridge) and the bridge's two sides
        CageRun(nest, new Vector2(-2.17f, -2.17f), new Vector2(-2.17f, 2.17f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(-2.17f, -2.17f), new Vector2(2.17f, -2.17f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(2.17f, -2.17f), new Vector2(2.17f, 2.17f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(-2.17f, 2.17f), new Vector2(-0.2f, 2.17f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(1.2f, 2.17f), new Vector2(2.17f, 2.17f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(-0.2f, 2.17f), new Vector2(-0.2f, 11.0f), IsleKit.DarkWood, IsleKit.Red);
        CageRun(nest, new Vector2(1.2f, 2.17f), new Vector2(1.2f, 11.0f), IsleKit.DarkWood, IsleKit.Red);
        // roofs
        Blk(nest, -2.45f, 2.45f, CageTop, CageTop + 0.15f, -2.45f, 2.45f, IsleKit.Red);
        Blk(nest, -0.35f, 1.35f, CageTop, CageTop + 0.12f, 2.2f, 11.0f, IsleKit.Red);
        // instrument boards hung under the roof's edges (clear of the view down), and the switch board on the east side
        Blk(nest, -1.25f, 1.65f, 15.4f, 16.3f, 2.06f, 2.12f, IsleKit.Board);
        Blk(nest, -1.2f, 1.2f, 15.4f, 15.9f, -2.12f, -2.06f, IsleKit.Board);
        Blk(nest, 2.08f, 2.14f, 14.7f, 15.5f, -0.4f, 0.4f, IsleKit.Wood);
        b.MeshPiece("Crows Nest", nest.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // ================= the stair tower (x -1.7..1.7, z 11.0..20.8; door in the north wall) =================
        var tower = new MeshBuilder();
        const float T0 = 11.0f, T1 = 20.8f, Top = CageTop;
        Blk(tower, -1.7f, -1.5f, -Sink, Top, T0, T1, IsleKit.Wood);      // west
        Blk(tower, 1.5f, 1.7f, -Sink, Top, T0, T1, IsleKit.Wood);        // east
        Blk(tower, -1.5f, -1.45f, -Sink, Top, T1 - 0.2f, T1, IsleKit.Wood);      // north, round its 1.2 m doorway (x -1.45..-0.25, 2.3 m)
        Blk(tower, -0.25f, 1.5f, -Sink, Top, T1 - 0.2f, T1, IsleKit.Wood);
        Blk(tower, -1.45f, -0.25f, 2.3f, Top, T1 - 0.2f, T1, IsleKit.Wood);
        Blk(tower, -1.5f, 1.5f, -Sink, Deck, T0, T0 + 0.2f, IsleKit.Wood);      // south, open above the top landing onto the bridge
        Blk(tower, -1.5f, -0.2f, Deck, Top, T0, T0 + 0.2f, IsleKit.Wood);
        Blk(tower, 1.2f, 1.5f, Deck, Top, T0, T0 + 0.2f, IsleKit.Wood);
        Blk(tower, -1.85f, 1.85f, Top, Top + 0.2f, T0 - 0.15f, T1 + 0.15f, IsleKit.Red);      // roof
        Blk(tower, -0.15f, 0.15f, -Sink, Top, 12.4f, 19.4f, IsleKit.DarkWood);      // spine between the two lanes
        // three switchback flights of 23 rises (0.203 m) over 7 m, landings at both ends, full width
        float rise = Deck / 69f, tread = 7f / 22f;
        for (int k = 1; k <= 22; k++)
        {
            float south = 19.4f - k * tread, north = 19.4f - (k - 1) * tread;      // flights 1 and 3 run south on lane x -0.85
            Blk(tower, -1.5f, -0.15f, -Sink, k * rise, south, north, IsleKit.Wood);
            Blk(tower, -1.5f, -0.15f, 2f * Deck / 3f - 0.3f, 2f * Deck / 3f + k * rise, south, north, IsleKit.Wood);
            float s2 = 12.4f + (k - 1) * tread, n2 = 12.4f + k * tread;      // flight 2 runs north on lane x +0.85
            Blk(tower, 0.15f, 1.5f, -Sink, Deck / 3f + k * rise, s2, n2, IsleKit.Wood);
        }
        Blk(tower, -1.5f, 1.5f, Deck / 3f - 0.25f, Deck / 3f, T0 + 0.2f, 12.4f, IsleKit.DarkWood);          // south landing
        Blk(tower, -1.5f, 1.5f, 2f * Deck / 3f - 0.25f, 2f * Deck / 3f, 19.4f, T1 - 0.2f, IsleKit.DarkWood);      // north landing
        Blk(tower, -1.5f, 1.5f, Deck - 0.25f, Deck, T0 + 0.2f, 12.4f, IsleKit.DarkWood);                    // top landing, onto the bridge
        b.MeshPiece("Stair Tower", tower.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // ================= the signal hut (hut-local: door in the south wall, the board on the north side) =================
        var shell = new MeshBuilder();
        Blk(shell, -2.5f, 2.5f, -0.7f, 0f, -3.0f, 3.0f, IsleKit.Stone);         // floor
        Blk(shell, -2.5f, 0.2f, -0.7f, 0f, -3.9f, -3.0f, IsleKit.Stone);        // the step the turnstile stands on
        Blk(shell, -2.5f, -2.31f, -0.7f, 3.3f, -3.0f, -2.8f, IsleKit.Wood);     // south wall: a doorway exactly the turnstile
        Blk(shell, -0.19f, 2.5f, -0.7f, 3.3f, -3.0f, -2.8f, IsleKit.Wood);
        Blk(shell, -2.31f, -0.19f, 2.30f, 3.3f, -3.0f, -2.8f, IsleKit.Wood);
        Blk(shell, -2.5f, 2.5f, -0.7f, 3.3f, 2.8f, 3.0f, IsleKit.Wood);         // north wall
        Blk(shell, -2.5f, -2.3f, -0.7f, 3.3f, -2.8f, 2.8f, IsleKit.Wood);       // side walls
        Blk(shell, 2.3f, 2.5f, -0.7f, 3.3f, -2.8f, 2.8f, IsleKit.Wood);
        Blk(shell, -2.5f, 2.5f, 3.3f, 3.5f, -3.0f, 3.0f, IsleKit.DarkWood);     // roof (ceiling at 3.3)
        Blk(shell, -2.8f, 2.8f, 3.5f, 3.62f, -3.3f, 3.3f, IsleKit.Red);
        Piece shellPiece = b.MeshPiece("Signal Hut", shell.ToMesh(), snowy);
        var room = new MeshBuilder();
        Blk(room, -2.3f, 1.0f, 0f, 3.3f, -1.05f, -0.95f, IsleKit.Cream);        // vestibule partition (the dogleg: a 1.3 m gap at the right)
        Blk(room, -2.3f, 2.3f, 0f, 3.3f, 1.4f, 1.5f, IsleKit.Cream);            // board partition, a closed void behind it
        Blk(room, -1.05f, 1.05f, 0.6f, 2.7f, 1.34f, 1.40f, IsleKit.Board);      // the lamp board: the maze's plan without walls
        Blk(room, -1.05f, -1.02f, 0.6f, 2.7f, 1.33f, 1.34f, IsleKit.Cream);
        Blk(room, 1.02f, 1.05f, 0.6f, 2.7f, 1.33f, 1.34f, IsleKit.Cream);
        Blk(room, -1.05f, 1.05f, 2.67f, 2.7f, 1.33f, 1.34f, IsleKit.Cream);
        Blk(room, -1.05f, -0.15f, 0.6f, 0.63f, 1.33f, 1.34f, IsleKit.Cream);    // bottom edge, with the archway's gap
        Blk(room, 0.15f, 1.05f, 0.6f, 0.63f, 1.33f, 1.34f, IsleKit.Cream);
        Blk(room, -0.15f, 0.15f, 1.5f, 1.8f, 1.33f, 1.34f, IsleKit.Cream);      // the nest
        Blk(room, 0f, 0.1f, 1.8f, 2.67f, 1.33f, 1.34f, IsleKit.Cream);          // the bridge
        Piece roomPiece = b.MeshPiece("Signal Hut Room", room.ToMesh(), bare);
        Vector3 hutAt = b.WorldPoint(hut.x, hut.y, hutBase, false);
        Quaternion hutTurn = root.rotation * Quaternion.Euler(0f, hutYaw, 0f);
        shellPiece.Transform.SetPositionAndRotation(hutAt, hutTurn);
        roomPiece.Transform.SetPositionAndRotation(hutAt, hutTurn);

        // lamps: pad (x, z) lights at hut (x / 10, height 1.65 + z / 10) on the board, facing the room; range 0.6 m so
        // no glow reaches an outside face (every lamp is 0.8 m or more inside)
        var lamps = new UnityEngine.Object[Pads.Length];
        Piece lampGroup = b.Empty("Board Lamps", 0f, 0f, 0f, false);
        for (int i = 0; i < Pads.Length; i++)
            lamps[i] = HutPart("Light 8", Pads[i].x / 10f, 1.34f, 1.65f + Pads[i].z / 10f, 180f).Named("Lamp " + (i + 1)).Under(lampGroup)
                .Set("PuzzleLight", "color", IsleKit.Gold).Set("PuzzleLight", "range", 0.6f).Component("PuzzleLight");
        Piece hutMic = HutPart("Intercom Mic", 1.35f, 1.38f, 1.4f, 180f).Named("Hut Mic");
        HutPart("Switch Start", 0.4f, -0.877f, 1.25f, 0f);
        HutPart("Live Light", 0.4f, -0.944f, 1.42f, 0f);
        HutPart("Confetti", 0f, 0.2f, 2.9f, 0f);
        HutPart("Gate No items", -1.2f, -2.9f, 0f, 0f).Named("Hut Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

        // ================= feedback =================
        Piece Bar(string name, float x, float z, float h, float yaw, float scale, int total, bool inHut)
        {
            Piece bar = inHut ? HutPart("Progress Bar", x, z, h, yaw) : b.Part("Progress Bar", x, z, h, yaw, false);
            return bar.Named(name).Scaled(scale, scale, scale).Set("PuzzleProgressBar", "total", total);
        }
        Piece rounds1 = Bar("Rounds Bar Nest", 0f, 2.0f, 16.0f, 180f, 0.6f, 6, false);
        Piece rounds2 = Bar("Rounds Bar Hut", 0f, 1.345f, 2.95f, 180f, 0.55f, 6, true);
        IsleKit.BoardOnPosts(b, "Count Board", -5f, -13.5f, 180f, 3.8f, 1.6f, 2.8f, IsleKit.Board);
        Piece rounds3 = b.Part("Progress Bar", -5f, -13.5f - IsleKit.FaceZ, 2.2f, 180f).Named("Rounds Bar Archway").Set("PuzzleProgressBar", "total", 6);
        var roundsBars = new UnityEngine.Object[] { rounds1.Component("PuzzleProgressBar"), rounds2.Component("PuzzleProgressBar"), rounds3.Component("PuzzleProgressBar") };
        var phaseBars = new UnityEngine.Object[]
        {
            Bar("Phase Bar Hut", 0f, 1.345f, 0.35f, 180f, 0.55f, 10, true).Component("PuzzleProgressBar"),
            Bar("Phase Bar Nest N", 0f, 2.0f, 15.65f, 180f, 0.6f, 10, false).Component("PuzzleProgressBar"),
            Bar("Phase Bar Nest S", 0f, -2.0f, 15.65f, 0f, 0.6f, 10, false).Component("PuzzleProgressBar"),
            Bar("Phase Bar Maze S", 0f, -1.76f, 3.3f, 180f, 0.7f, 10, false).Component("PuzzleProgressBar"),
            Bar("Phase Bar Maze N", 0f, 1.76f, 3.3f, 0f, 0.7f, 10, false).Component("PuzzleProgressBar"),
            Bar("Phase Bar Maze E", 1.76f, 0f, 3.3f, 90f, 0.7f, 10, false).Component("PuzzleProgressBar"),
            Bar("Phase Bar Maze W", -1.76f, 0f, 3.3f, -90f, 0.7f, 10, false).Component("PuzzleProgressBar"),
        };
        Piece regroup = b.Part("Timer", 1.3f, 2.0f, 15.8f, 180f, false).Named("Regroup Timer").Set("PuzzleTimer", "duration", 6f);
        Component regroupC = regroup.Component("PuzzleTimer");

        // ================= the nest's chairs, and the 2-player speakers =================
        // two back to back, so every direction is in front of someone; no microphone: they hear only the hut's intercom
        Piece chairN = b.Part("Listening Chair", -0.9f, -0.45f, Deck, 0f, false).Named("Lookout Chair N");
        Piece chairS = b.Part("Listening Chair", -1.6f, 0.45f, Deck, 180f, false).Named("Lookout Chair S");
        Piece two = IsleKit.Lobby(b, "Two Players", true, false, false);
        Piece three = IsleKit.Lobby(b, "Three Players", false, true, false);
        Piece four = IsleKit.Lobby(b, "Four Players", false, false, true);
        var listeners = new List<UnityEngine.Object> { chairN.Component("PuzzleListeningChair"), chairS.Component("PuzzleListeningChair") };
        (string name, float x, float z, float yaw)[] beams = { ("S", 0f, -1.78f, 180f), ("N", 0f, 1.78f, 0f), ("E", 1.78f, 0f, 90f), ("W", -1.78f, 0f, -90f) };
        foreach (var (name, x, z, yaw) in beams)
        {
            Vector2 right = IsleKit.Turn(1.5f, 0f, yaw);
            listeners.Add(b.Part("Intercom Speaker", x + right.x, z + right.y, 2.95f, yaw, false).Named("Speaker " + name).Under(two).Component("PuzzleIntercomSpeaker"));
        }
        Sdk.Add(hutMic.Component("PuzzleIntercom"), "speakers", listeners.ToArray());

        // ================= Press In Order: one copy per lobby size =================
        (Piece group, int players, int rounds, float seconds)[] copies = { (two, 2, 5, 26f), (three, 3, 6, 22f), (four, 4, 8, 20f) };
        foreach (var (group, players, rounds, seconds) in copies)
        {
            Piece order = b.Empty("Press In Order " + players, 0f, -13.5f, 0.5f, false).Under(group);
            Component pio = b.AddScript(order, "PuzzlePressInOrder");
            Sdk.Add(pio, "lights", lamps);
            Sdk.Add(pio, "buttons", pads);
            Sdk.Add(pio, "progressBars", roundsBars);
            Sdk.Add(pio, "phaseBars", phaseBars);
            Sdk.Set(pio, "rounds", rounds);
            Sdk.Set(pio, "holdAtEnd", true);
            Sdk.Set(pio, "phaseSeconds", seconds);
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
            Sdk.Wire(pio, "onFail", regroupC, "StartTimer");
            regroup.On("PuzzleTimer", "onFinished", pio, "Begin");
        }
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", regroupC, "ResetTimer");      // a pending regroup never restarts a stopped puzzle
        rounds1.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        // ================= start corner and the other switches (all toggle together) =================
        IsleKit.StartCorner(b, 5.5f, -13.5f, 180f);
        b.Part("Switch Start", 2.007f, 0f, 15.0f, -90f, false);       // the nest, on the board on the cage's east side
        b.Part("Live Light", 2.074f, 0f, 15.17f, -90f, false);
        b.Part("Switch Start", -0.9f, -1.773f, 1.25f, 180f, false);   // inside the maze, on the centre cell's south wall
        b.Part("Live Light", -0.9f, -1.706f, 1.42f, 180f, false);
        b.Part("Confetti", 0f, 0f, 16.2f, 0f, false);

        // ================= radio dead zones and calm air =================
        Piece mazeZone = b.Part("Radio Dead Zone", 0f, 0f, 10f, 0f, false).Named("Maze Dead Zone");
        SetBox(mazeZone, new Vector3(90f, 60f, 90f));
        Piece hutZone = b.Part("Radio Dead Zone", hut.x, hut.y, hutBase + 10f, hutYaw, false).Named("Hut Dead Zone");
        SetBox(hutZone, new Vector3(120f, 100f, 120f));
        IsleKit.CalmAir(b, "Blizzard Walk", 0f, 2.5f, new Vector3(60f, 120f, 70f), 0f, -10f, false);
        IsleKit.CalmAir(b, "Blizzard Hut", hut.x, hut.y, new Vector3(24f, 100f, 24f), hutYaw, hutBase - 10f, false);

        // ================= lights =================
        var rim = new List<Vector3>();
        void Rim(float x, float z) => rim.Add(new Vector3(x, 3.15f, z));
        for (float x = -1.5f; x >= -10.49f; x -= 3f) Rim(x, -10.5f);
        for (float z = -10.5f; z <= 10.49f; z += 3f) Rim(-10.5f, z);
        for (float x = -10.5f; x <= 10.49f; x += 3f) Rim(x, 10.5f);
        for (float z = 10.5f; z >= -10.49f; z -= 3f) Rim(10.5f, z);
        for (float x = 10.5f; x >= 1.49f; x -= 3f) Rim(x, -10.5f);
        Kit.LightString(b, "Maze Lights", rim, 0.1f, 0.5f, false, true, 113);
        Kit.LightString(b, "Nest Lights", new List<Vector3> { new Vector3(-2.45f, 16.45f, -2.45f), new Vector3(2.45f, 16.45f, -2.45f), new Vector3(2.45f, 16.45f, 2.45f), new Vector3(-2.45f, 16.45f, 2.45f), new Vector3(-2.45f, 16.45f, -2.45f) }, 0.05f, 0.4f, false, false, 114);
        Kit.LightString(b, "Tower Lights", new List<Vector3> { new Vector3(-1.85f, 16.5f, 10.85f), new Vector3(1.85f, 16.5f, 10.85f), new Vector3(1.85f, 16.5f, 20.95f), new Vector3(-1.85f, 16.5f, 20.95f), new Vector3(-1.85f, 16.5f, 10.85f) }, 0.05f, 0.4f, false, false, 115);
        Kit.LightRing(b, "Site Lights", 0f, 2.5f, 34f, 44f, 2.6f, true, 116);

        // a line of lamp posts from the tower's door, round the maze's west side, to the hut's door (about 300 m)
        var path = new List<Vector2> { new Vector2(-2.5f, 22.5f), new Vector2(-20f, 22.5f), new Vector2(-20f, -17f), HutLocal(-6f, 5f), HutLocal(-6f, -6f), HutLocal(-3f, -6.5f) };
        var line = new List<Vector3> { new Vector3(path[0].x, 2.6f, path[0].y) };
        for (int i = 1; i < path.Count; i++)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((path[i] - path[i - 1]).magnitude / 15f));
            for (int k = 1; k <= n; k++)
            {
                Vector2 p = Vector2.Lerp(path[i - 1], path[i], (float)k / n);
                line.Add(new Vector3(p.x, 2.6f, p.y));
            }
        }
        Kit.LightString(b, "Hut Line", line, 0.15f, 1.0f, true, true, 117);

        Debug.Log("[BWM] BlizzardWalk: hut at local (" + Sdk.F(hut.x) + ", " + Sdk.F(hut.y) + "), " + Sdk.F(hut.magnitude) + " m, yaw " + Sdk.F(hutYaw)
                  + ", floor " + Sdk.F(hutBase) + " m above the maze");
    }

    // A dead zone's trigger box, centred on its object
    static void SetBox(Piece zone, Vector3 size)
    {
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
    }
}
