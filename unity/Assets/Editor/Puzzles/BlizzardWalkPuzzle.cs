// Blizzard Walk: a snow maze with a crow's nest over it. No clock, and nobody is shut in or out of earshot.
//
// A 21 x 21 m maze of 2.6 m packed-snow walls has twelve identical dead-end alcoves, each a Presence Sensor pad with
// a lamp on a mast over its closed end, turned to face the crow's nest. The nest is caged and stands 14 m over the
// maze's closed centre cell on four stilts, its floor slatted so the maze shows straight down through it; the way up
// is the roofed switchback stair tower north of the maze and a caged bridge. One alcove's lamp is lit. From the nest
// the lit alcove and the whole maze are plain to see; in the corridors a walker sees only walls, and the lamp's glow
// once they are close. Stand in the lit alcove for a moment and it counts, its lamp goes out and another lights
// (Seek, in PuzzleScripts). Five alcoves win. One player can do it alone, climbing to look and walking from memory;
// a group puts a lookout in the nest to call the walkers through.
// Quit: a Start Switch at the archway, in the nest and inside the maze.
// (Before the user's rules of 2026-11-24 the lamps were on a plan in a windowless signal hut 289 m away, called by
// intercom into headphones in the nest, on 22 s phases: a three-link, one-way relay.)
//
// Maze-local coordinates: metres from the maze centre, x east, z north, heights above the root's level (the island
// levels the site).
using System.Collections.Generic;
using System.Globalization;
using Bwm;
using UnityEngine;

public sealed class BlizzardWalkPuzzle : PuzzleDef
{
    public override string Name => "BlizzardWalk";
    public override Vector2 Footprint => new Vector2(30f, 50f);      // levelled as a circle of radius 29.2 m
    public override Vector2? FixedSite => new Vector2(-1160f, -980f);      // layout-final: a level shelf by a wood, off pistes and the rail bench

    const int Rounds = 5;               // alcoves to find
    const float Dwell = 1.5f;           // seconds in the lit alcove before it counts
    const float MastTop = 3.9f;         // the alcove lamps' height: over the 2.6 m walls
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one

    const float WallH = 2.6f, WallT = 0.4f, Sink = 0.5f, RidgeH = 0.35f;
    const float Deck = 14f, CageTop = 16.4f;

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

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -17f);
        b.ClearGround(30f, 50f);
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
        var masts = new MeshBuilder();
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
            IsleKit.Sensor(b, "Pad " + (i + 1), s.x, s.y, new Vector3(2.0f, 2.6f, 2.0f), 0f, false);
            // its lamp: on a mast at the closed end, 3.9 m up, turned to look at the crow's nest, so the lookout sees
            // the lit one as a bright disc; in the maze its light falls on the alcove's walls and snow
            Kit.AddBox(masts, new Vector3(post.x, 1.0f, post.y), new Vector3(0.12f, MastTop - 1.15f, 0.12f), IsleKit.DarkWood);
            Kit.AddBox(masts, new Vector3(post.x, MastTop - 0.15f, post.y), new Vector3(0.3f, 0.3f, 0.3f), IsleKit.Board);
            Piece lamp = b.Part("Light 6", post.x, post.y, MastTop, 0f, false).Named("Pad Lamp " + (i + 1))
                .Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 5f);
            Vector3 head = lamp.Transform.position;
            Vector3 toNest = (b.WorldPoint(0f, 0f, Deck + 1.4f, false) - head).normalized;
            lamp.Transform.rotation = Quaternion.LookRotation(toNest, Vector3.up);
            lamp.Transform.position = head + toNest * 0.3f;
        }
        b.MeshPiece("Pad Markers", markers.ToMesh(), bare).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        b.MeshPiece("Pad Masts", masts.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

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

        // ================= feedback: alcoves found, on the nest, in the maze's centre and by the archway =================
        Piece Bar(string name, float x, float z, float h, float yaw, float scale)
            => b.Part("Progress Bar", x, z, h, yaw, false).Named(name).Scaled(scale, scale, scale).Set("PuzzleProgressBar", "total", Rounds);
        Bar("Seek Bar Nest", 0f, 2.0f, 16.0f, 180f, 0.6f);
        Bar("Seek Bar Maze S", 0f, -1.76f, 3.3f, 180f, 0.7f);
        Bar("Seek Bar Maze N", 0f, 1.76f, 3.3f, 0f, 0.7f);
        IsleKit.BoardOnPosts(b, "Count Board", -5f, -13.5f, 180f, 3.8f, 1.6f, 2.8f, IsleKit.Board);
        b.Part("Progress Bar", -5f, -13.5f - IsleKit.FaceZ, 2.2f, 180f).Named("Seek Bar Archway").Set("PuzzleProgressBar", "total", Rounds);

        // ================= the rule: go to the lit alcove; five found win (Seek.cs). Start deals, stop clears =================
        Component seek = b.AddCustomScript(b.Empty("Maze Seek", 0f, -13.5f, 0.5f, false), "Seek");
        Sdk.Set(seek, "sensorPrefix", "Pad ");
        Sdk.Set(seek, "lampPrefix", "Pad Lamp ");
        Sdk.Set(seek, "barPrefix", "Seek Bar");
        Sdk.Set(seek, "rounds", Rounds);
        Sdk.Set(seek, "dwell", Dwell);
        Sdk.Set(seek, "need", 1);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", seek, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", seek, "End");

        // ================= start corner and the other switches (all toggle together) =================
        IsleKit.StartCorner(b, 5.5f, -13.5f, 180f);
        b.Part("Switch Start", 2.007f, 0f, 15.0f, -90f, false);       // the nest, on the board on the cage's east side
        b.Part("Live Light", 2.074f, 0f, 15.17f, -90f, false);
        b.Part("Switch Start", -0.9f, -1.773f, 1.25f, 180f, false);   // inside the maze, on the centre cell's south wall
        b.Part("Live Light", -0.9f, -1.706f, 1.42f, 180f, false);
        b.Part("Confetti", 0f, 0f, 16.2f, 0f, false);

        // ================= calm air: no jetpack over the maze or up to the nest =================
        IsleKit.CalmAir(b, "Blizzard Walk", 0f, 2.5f, new Vector3(60f, 120f, 70f), 0f, -10f, false);

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
    }
}
