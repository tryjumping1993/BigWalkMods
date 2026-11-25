// Bauble Hunt: a palisaded fir wood, the Bauble Wood, holds sixteen frosted baubles on little racks among its drifts.
// They are Peg Tiles with Invisible Ink View: blank to the naked eye, their pictures show only through the Invisible
// Ink telescope on the lookout tower north of the wood. South of the wood, in the open, stands the Bauble Stand: a
// Display of the three baubles wanted, a three-slot Peg Board under it and a check screen with its button.
//
// One player climbs the tower and reads the blank baubles through the telescope; the others walk the wood, fetch the
// ones the spotter picks out and hang them on the board in the Display's order (left to right); anyone presses the
// check button. A right board wins the gourd, a wrong one only shows the cross. Nobody is shut in, nothing locks,
// there is no clock, and everyone can talk to everyone (a walkie-talkie on the tower and one by the stand help over
// the 25 m between them). A hunter can lean a bauble on the Show Lectern in the wood for the spotter to read. One
// patient player can finish it alone (read the Display, climb, look and remember, fetch); it is built for three or
// four. The same in every lobby size. Eight designs, two tiles each, every combination of three features (dark or
// light body, high or low band, stars or dots).
// (Before the user's rules of 2026-11-24 the Display and the board were in a sealed Hut in the wood's south wall,
// reached only by an intercom and a posting box; the answer was checked only while three players held buttons at
// once, a wrong check stopped the puzzle, and the ways in locked while it ran.)
//
// Coordinates are local metres from the centre, +z north; heights are above the pad's top (Floor). Parts face their
// local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z. No custom code: the bauble sheet is drawn here in C#.
using System;
using System.Collections.Generic;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class BaubleHuntPuzzle : PuzzleDef
{
    public override string Name => "BaubleHunt";
    public override Vector2 Footprint => new Vector2(28f, 43f);
    public override Vector2? FixedSite => new Vector2(-1310f, -830f);      // layout-final: a level wood-edge shelf by Northwest station
    public override float Yaw => 0f;                                         // the long axis north-south: tower north, stand south

    const float Floor = 0.25f;              // the pad's top above the root's level
    const float WallH = 2.8f;               // the palisade
    const float DeckTop = 4.0f;             // the tower's deck
    static readonly Vector2 Eyepiece = new Vector2(0f, 9.75f);
    const float EyeHeight = 5.6f;
    // The Bauble Stand: a board on posts (IsleKit.BoardOnPosts, 0.12 m thick) south of the wood, facing south. Its
    // posts' middle, its south face, its width and its face's foot and top
    const float StandZ = -13f, StandFace = StandZ - 0.06f, StandWide = 3.2f, StandLow = 0.7f, StandHigh = 2.5f;
    const float DisplayBack = 0.168f;       // a Display's back is 0.168 m behind its pivot (part-metrics); a Peg Board's and a Validator's are at their pivots
    const float SlotOne = -0.9f, SlotPitch = 0.45f;      // the board's pivot is its first slot; the others follow 0.45 m apart to the right of someone facing it
    static readonly Vector2 WalkieStump = new Vector2(-3.1f, -14.5f);
    const float StumpTop = 0.5f;
    const string SheetPath = "Assets/Textures/Baubles/BaubleSheet.png";
    const string SheetVersion = "bauble-sheet-1";

    static readonly Color Snow = new Color(0.92f, 0.94f, 0.97f);
    static readonly Color Log = new Color(0.40f, 0.28f, 0.18f);
    static readonly Color LogDark = new Color(0.31f, 0.21f, 0.13f);
    static readonly Color Plank = new Color(0.50f, 0.36f, 0.24f);
    static readonly float[] MarkX = { 0.35f, 0.5f, 0.65f, 0.42f, 0.58f };      // the five marks: three across the middle, two on the far row

    // The baubles' racks: tile n and n + 8 show design n
    static readonly Vector2[] Racks =
    {
        new Vector2(-5.0f, 0.25f), new Vector2(-2.5f, 0.25f), new Vector2(2.5f, 0.25f), new Vector2(5.5f, 0.25f),
        new Vector2(-7.0f, -2.25f), new Vector2(-3.5f, -2.25f), new Vector2(0f, -2.25f), new Vector2(3.5f, -2.25f), new Vector2(7.0f, -2.25f),
        new Vector2(-8.5f, -4.75f), new Vector2(-5.0f, -4.75f), new Vector2(-1.5f, -4.75f), new Vector2(2.0f, -4.75f), new Vector2(5.5f, -4.75f), new Vector2(9.0f, -4.75f),
        new Vector2(-6.0f, -7.05f),
    };
    static readonly Vector2 Lectern = new Vector2(0f, 0.25f);
    static readonly Vector2[] EmptyDrifts = { new Vector2(6.5f, -7.05f), new Vector2(-2.5f, -7.25f), new Vector2(8.5f, -0.25f) };
    // Firs in the wood (x, z, height): landmarks, each kept off every eyepiece-to-rack line (checked below)
    static readonly Vector3[] WoodFirs = { new Vector3(-9.0f, -7.1f, 4.0f), new Vector3(-0.5f, -7.1f, 3.5f), new Vector3(8.5f, -7.1f, 4.0f), new Vector3(-9.0f, -2.1f, 3.0f), new Vector3(-7.0f, 1.4f, 2.5f) };
    // Backdrop firs outside the palisade
    static readonly Vector3[] OuterFirs = { new Vector3(-13.3f, -6f, 4.5f), new Vector3(-13.3f, 1.5f, 5f), new Vector3(13.3f, -4.5f, 4.8f), new Vector3(13.3f, 2.5f, 4.2f), new Vector3(-10.5f, -14.5f, 4f), new Vector3(11f, -14f, 4.4f), new Vector3(-4.5f, 17f, 5.2f), new Vector3(4.8f, 16.5f, 4.6f) };

    PuzzleBuilder b;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        CheckSightLines();
        b.ClearGround(30f, 45f);
        b.DebugTeleport(4f, -21f);
        Sdk.Set(b.Puzzle, "gourdColor", new Color(0.42f, 0.07f, 0.12f));      // a dark bauble's crimson
        // Winter Walk's jetpacks do not fire over the wood, the stand or the tower (well past every wall)
        IsleKit.CalmAir(b, "Bauble Hunt", 0f, 0.2f, new Vector3(70f, 400f, 90f), 0f, -10f, false);

        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        var works = new MeshBuilder();      // everything solid: pad, palisade, tower, racks, lectern, the walkie's stump
        var trim = new MeshBuilder();       // painted bits that take no snow

        // ---- the pad: one level slab for the whole site, sunk 1.5 m ----
        Kit.AddBox(works, new Vector3(0f, Floor - 1.75f, 0f), new Vector3(28f, 1.75f, 43f), Snow);

        BuildPalisade(works);
        BuildTower(works);
        BuildWoodFurniture(works, trim);
        BuildStandFurniture(works, trim);

        b.MeshPiece("Bauble Hunt Works", works.ToMesh(), snowy);
        b.MeshPiece("Bauble Hunt Trim", trim.ToMesh(), bare);

        BuildFirs(snowy, bare);
        BuildDrifts();
        Texture2D sheet = BaubleSheet();
        BuildBaubles(sheet);
        BuildParts();
        BuildLights();
    }

    // ================================================================ geometry helpers

    static Vector3 L(float x, float h, float z) => new Vector3(x, Floor + h, z);

    // An axis-aligned block from its low and high corners (heights above the pad)
    static void Block(MeshBuilder mb, float x0, float x1, float h0, float h1, float z0, float z1, Color c)
        => Kit.AddBox(mb, L((x0 + x1) / 2f, h0, (z0 + z1) / 2f), new Vector3(x1 - x0, h1 - h0, z1 - z0), c);

    // A triangle wound to face away from 'interior'
    static void TriOut(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Vector3 interior, Color col)
    {
        Vector3 n = Vector3.Cross(b2 - a, c - a);
        if (Vector3.Dot(n, (a + b2 + c) / 3f - interior) < 0f) mb.Tri(a, c, b2, col);
        else mb.Tri(a, b2, c, col);
    }

    // A convex solid from its faces (each a convex loop of corners); every face is wound outwards
    static void Solid(MeshBuilder mb, Color col, params Vector3[][] faces)
    {
        Vector3 centre = Vector3.zero; int n = 0;
        foreach (Vector3[] f in faces) foreach (Vector3 p in f) { centre += p; n++; }
        centre /= n;
        foreach (Vector3[] f in faces)
            for (int k = 1; k + 1 < f.Length; k++) TriOut(mb, f[0], f[k], f[k + 1], centre, col);
    }

    // A box of 'size' centred on 'centre', turned by 'turn' (local space of the root)
    static void OBox(MeshBuilder mb, Vector3 centre, Vector3 size, Quaternion turn, Color col)
    {
        Vector3 h = size / 2f;
        Vector3 K(float sx, float sy, float sz) => centre + turn * new Vector3(sx * h.x, sy * h.y, sz * h.z);
        Solid(mb, col,
            new[] { K(-1, -1, -1), K(1, -1, -1), K(1, -1, 1), K(-1, -1, 1) },
            new[] { K(-1, 1, -1), K(1, 1, -1), K(1, 1, 1), K(-1, 1, 1) },
            new[] { K(-1, -1, -1), K(1, -1, -1), K(1, 1, -1), K(-1, 1, -1) },
            new[] { K(-1, -1, 1), K(1, -1, 1), K(1, 1, 1), K(-1, 1, 1) },
            new[] { K(-1, -1, -1), K(-1, -1, 1), K(-1, 1, 1), K(-1, 1, -1) },
            new[] { K(1, -1, -1), K(1, -1, 1), K(1, 1, 1), K(1, 1, -1) });
    }

    // A prism running along x from x0 to x1 whose cross-section is a convex polygon of (z, height) points
    static void PrismX(MeshBuilder mb, float x0, float x1, Color col, params Vector2[] zh)
    {
        var faces = new List<Vector3[]>();
        var end0 = new Vector3[zh.Length]; var end1 = new Vector3[zh.Length];
        for (int i = 0; i < zh.Length; i++) { end0[i] = L(x0, zh[i].y, zh[i].x); end1[i] = L(x1, zh[i].y, zh[i].x); }
        faces.Add(end0); faces.Add(end1);
        for (int i = 0; i < zh.Length; i++)
        {
            int j = (i + 1) % zh.Length;
            faces.Add(new[] { end0[i], end0[j], end1[j], end1[i] });
        }
        Solid(mb, col, faces.ToArray());
    }

    // 2D distance from p to the segment a-c
    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 c)
    {
        Vector2 d = c - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
        return Vector2.Distance(p, a + d * t);
    }

    // ================================================================ structures

    // The palisade: split logs 0.3 thick, 2.8 m tall with pointed tops, no gaps, centred on x = +-12, z = 3.75 and
    // z = -10.25; two open gateways in its south side, 2.12 m wide and 2.3 m tall under log lintels
    void BuildPalisade(MeshBuilder mb)
    {
        LogRun(mb, new Vector2(-12f, -10.25f), new Vector2(-12f, 3.75f));
        LogRun(mb, new Vector2(-12f, 3.75f), new Vector2(12f, 3.75f));
        LogRun(mb, new Vector2(12f, 3.75f), new Vector2(12f, -10.25f));
        // south side: x 12..9 | gateway 9..6.9 | 6.9..-6.9 | gateway -6.9..-9 | -9..-12
        LogRun(mb, new Vector2(12f, -10.25f), new Vector2(9.01f, -10.25f));
        LogRun(mb, new Vector2(6.89f, -10.25f), new Vector2(-6.89f, -10.25f));
        LogRun(mb, new Vector2(-9.01f, -10.25f), new Vector2(-12f, -10.25f));
        Block(mb, 6.89f, 9.01f, 2.3f, WallH, -10.4f, -10.1f, LogDark);      // lintels
        Block(mb, -9.01f, -6.89f, 2.3f, WallH, -10.4f, -10.1f, LogDark);
    }

    static void LogRun(MeshBuilder mb, Vector2 a, Vector2 c)
    {
        const float Point = 0.25f;
        Vector2 d = c - a;
        int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 0.3f));
        float w = d.magnitude / n, yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        for (int i = 0; i < n; i++)
        {
            Vector2 at = a + d * ((i + 0.5f) / n);
            Color col = i % 3 == 1 ? LogDark : Color.Lerp(Log, LogDark, (i * 37 % 7) / 14f);
            Kit.AddBox(mb, L(at.x, -0.2f, at.y), new Vector3(0.3f, WallH - Point + 0.2f, w), col, yaw);
            Vector3 apex = L(at.x, WallH, at.y);
            Vector3 C(float sx, float sz) => L(at.x, WallH - Point, at.y) + turn * new Vector3(sx * 0.15f, 0f, sz * w / 2f);
            Vector3 p0 = C(-1, -1), p1 = C(1, -1), p2 = C(1, 1), p3 = C(-1, 1);
            Solid(mb, col, new[] { p0, p1, p2, p3 }, new[] { p0, p1, apex }, new[] { p1, p2, apex }, new[] { p2, p3, apex }, new[] { p3, p0, apex });
        }
    }

    // The lookout tower: deck x -1.8..1.8, z 9.8..13.4 at 4.0 m on four log legs, 1.3 m solid rails except the scope
    // bay (south) and a stair opening at the north-east corner; a roof at 6.6 m. The bay: x -0.6..0.6, z 8.8..9.8, a
    // 0.5 m parapet in front and a low roof at 2.1 m over the deck. The stair falls to the ground at z 20.
    void BuildTower(MeshBuilder mb)
    {
        float d = DeckTop, r = d + 1.3f;
        foreach (float lx in new[] { -1.65f, 1.65f })
            foreach (float lz in new[] { 9.95f, 13.25f })
                Block(mb, lx - 0.125f, lx + 0.125f, -0.2f, d - 0.2f, lz - 0.125f, lz + 0.125f, Log);
        Block(mb, -0.67f, -0.53f, -0.2f, d - 0.2f, 8.83f, 8.97f, Log);      // the bay's legs
        Block(mb, 0.53f, 0.67f, -0.2f, d - 0.2f, 8.83f, 8.97f, Log);
        Block(mb, -1.8f, 1.8f, d - 0.2f, d, 9.8f, 13.4f, Plank);            // deck
        Block(mb, -0.72f, 0.72f, d - 0.2f, d, 8.8f, 9.8f, Plank);           // bay floor
        // rails
        Block(mb, -1.8f, -1.68f, d, r, 9.8f, 13.4f, LogDark);               // west
        Block(mb, 1.68f, 1.8f, d, r, 9.8f, 13.4f, LogDark);                 // east (Tower Knob)
        Block(mb, -1.68f, 0.6f, d, r, 13.28f, 13.4f, LogDark);              // north, the stair opening x 0.6..1.8
        Block(mb, -1.68f, -0.72f, d, r, 9.8f, 9.92f, LogDark);              // south, either side of the bay
        Block(mb, 0.72f, 1.68f, d, r, 9.8f, 9.92f, LogDark);
        Block(mb, -0.72f, -0.6f, d, r, 8.8f, 9.92f, LogDark);               // the bay's sides
        Block(mb, 0.6f, 0.72f, d, r, 8.8f, 9.92f, LogDark);
        Block(mb, -0.6f, 0.6f, d, d + 0.5f, 8.8f, 8.92f, LogDark);          // the bay's parapet
        // the bay's low roof (0.4 m over the telescope: no room to stand on it), on posts at its back
        Block(mb, -0.72f, 0.72f, d + 2.1f, d + 2.22f, 8.8f, 9.92f, LogDark);
        Block(mb, -0.71f, -0.61f, r, d + 2.1f, 9.8f, 9.9f, Log);
        Block(mb, 0.61f, 0.71f, r, d + 2.1f, 9.8f, 9.9f, Log);
        // the main roof on corner posts
        foreach (float px in new[] { -1.74f, 1.74f })
            foreach (float pz in new[] { 9.86f, 13.34f })
                Block(mb, px - 0.06f, px + 0.06f, r, 6.6f, pz - 0.06f, pz + 0.06f, Log);
        Block(mb, -2.05f, 2.05f, 6.6f, 6.75f, 9.65f, 13.6f, LogDark);
        // the stair: 19 solid steps of 0.2 m from the deck down to z 20, with solid side walls 1 m above each step
        const int Steps = 20;
        float run = (20f - 13.4f) / (Steps - 1);
        for (int k = 1; k < Steps; k++)
        {
            float top = d - 0.2f * k, z0 = 13.4f + run * (k - 1), z1 = z0 + run;
            Block(mb, 0.6f, 1.8f, -0.2f, top, z0, z1, Plank);
            Block(mb, 0.48f, 0.6f, -0.2f, top + 1f, z0, z1, LogDark);
            Block(mb, 1.8f, 1.92f, -0.2f, top + 1f, z0, z1, LogDark);
        }
    }

    // The racks and the Show Lectern
    void BuildWoodFurniture(MeshBuilder mb, MeshBuilder trim)
    {
        for (int i = 0; i < Racks.Length; i++) BuildRack(mb, trim, Racks[i]);
        // Show Lectern: 1.4 m wide, its face 1.0..1.3 m tilted about 65 degrees from level towards the tower (north),
        // with a 6 cm lip; a solid wedge so a tile leant on it for the spotter has a face to lie on
        float lx = Lectern.x, lz = Lectern.y;
        PrismX(mb, lx - 0.7f, lx + 0.7f, Plank,
            new Vector2(lz - 0.15f, 0f), new Vector2(lz + 0.08f, 0f), new Vector2(lz + 0.08f, 0.99f),
            new Vector2(lz - 0.08f, 1.3f), new Vector2(lz - 0.15f, 1.3f));
        // the lip along the face's foot (6 cm proud of the face)
        Quaternion faceTurn = Quaternion.Euler(-25f, 0f, 0f);
        OBox(mb, L(lx, 1.02f, lz + 0.1f), new Vector3(1.4f, 0.04f, 0.08f), faceTurn, LogDark);
        Block(trim, lx - 0.72f, lx + 0.72f, 1.3f, 1.33f, lz - 0.17f, lz - 0.06f, IsleKit.Red);
    }

    // What stands by the Bauble Stand besides the board itself (BuildParts): red bands painted along the top and the
    // foot of its face, and a log stump out in front to the west with the stand's walkie-talkie on it
    void BuildStandFurniture(MeshBuilder mb, MeshBuilder trim)
    {
        Block(trim, -StandWide / 2f, StandWide / 2f, StandHigh - 0.07f, StandHigh, StandFace - 0.012f, StandFace, IsleKit.Red);
        Block(trim, -StandWide / 2f, StandWide / 2f, StandLow, StandLow + 0.07f, StandFace - 0.012f, StandFace, IsleKit.Red);
        Block(mb, WalkieStump.x - 0.3f, WalkieStump.x + 0.3f, -0.2f, StumpTop, WalkieStump.y - 0.3f, WalkieStump.y + 0.3f, Log);
    }

    // The frame a rack's tile rests in: tipped back 15 degrees towards the eyepiece
    static Quaternion RackTurn(Vector2 at) => Quaternion.Euler(-15f, RackYaw(at), 0f);
    static float RackYaw(Vector2 at) => Mathf.Atan2(Eyepiece.x - at.x, Eyepiece.y - at.y) * Mathf.Rad2Deg;
    static Vector3 LedgeTop(Vector2 at) => L(at.x, 0.6f, at.y);

    // A rack: two stakes, a ledge 0.5 m wide with a 6 cm lip, and a back board, all tipped back 15 degrees
    static void BuildRack(MeshBuilder mb, MeshBuilder trim, Vector2 at)
    {
        Quaternion q = RackTurn(at);
        Vector3 up = q * Vector3.up, fwd = q * Vector3.forward, right = q * Vector3.right, p = LedgeTop(at);
        OBox(mb, p - up * 0.03f - fwd * 0.04f, new Vector3(0.5f, 0.06f, 0.44f), q, Plank);              // ledge, fwd -0.26..0.18
        OBox(mb, p + up * 0.03f + fwd * 0.15f, new Vector3(0.5f, 0.06f, 0.06f), q, LogDark);            // lip
        OBox(mb, p + up * 0.25f - fwd * 0.28f, new Vector3(0.56f, 0.5f, 0.04f), q, Plank);              // back board
        foreach (float s in new[] { -0.2f, 0.2f })
            OBox(mb, p + right * s - up * 0.46f - fwd * 0.04f, new Vector3(0.06f, 0.8f, 0.06f), q, LogDark);
        OBox(trim, p + up * 0.51f - fwd * 0.28f, new Vector3(0.58f, 0.04f, 0.06f), q, IsleKit.Red);     // painted top
    }

    // Where a bauble's tile sits on its rack, and how it is turned: on the ledge, leaning on the back board
    static Vector3 TileSpot(Vector2 at)
    {
        Quaternion q = RackTurn(at);
        return LedgeTop(at) + q * new Vector3(0f, 0.245f, -0.02f);
    }

    // Firs (static, trunks only collide so nobody climbs them) with decoy baubles on the wood's five
    void BuildFirs(Material snowy, Material bare)
    {
        var firs = new MeshBuilder();
        var decoys = new MeshBuilder();
        Color[] decoyColours = { IsleKit.Red, IsleKit.Gold, new Color(0.12f, 0.55f, 0.25f), new Color(0.82f, 0.84f, 0.88f) };
        var all = new List<Vector3>(WoodFirs); all.AddRange(OuterFirs);
        for (int i = 0; i < all.Count; i++)
        {
            Vector3 f = all[i];
            Kit.AddPine(firs, L(f.x, 0f, f.y), f.z, 7100 + i * 13);
        }
        int n = 0;
        foreach (Vector3 f in WoodFirs)      // six baubles a fir, thirty in all: bright, visible and not items
            for (int k = 0; k < 6; k++)
            {
                int tier = k % 3;
                float h = f.z * (0.16f + 0.19f * tier) - 0.04f * f.z, r = f.z * 0.27f * (1f - 0.21f * tier) * 0.88f;
                float a = (k * 61f + n * 17f) * Mathf.Deg2Rad;
                Kit.AddBall(decoys, L(f.x + Mathf.Cos(a) * r, h, f.y + Mathf.Sin(a) * r), 0.075f, decoyColours[n++ % decoyColours.Length]);
            }
        Piece firPiece = b.MeshPiece("Bauble Wood Firs", firs.ToMesh(), snowy, false);
        foreach (Vector3 f in all)
        {
            CapsuleCollider trunk = firPiece.Go.AddComponent<CapsuleCollider>();
            trunk.center = L(f.x, 1.0f, f.y);
            trunk.radius = Mathf.Max(0.1f, f.z * 0.035f);
            trunk.height = 2.0f;
        }
        b.MeshPiece("Decoy Baubles", decoys.ToMesh(), bare, false);
    }

    // Drifts: a low one behind each rack (away from the tower) and three empty ones
    void BuildDrifts()
    {
        Material drift = b.ShaderMaterial("BWM/Snowy", "Drift");
        var random = new System.Random(707);
        float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
        for (int i = 0; i < Racks.Length; i++)
        {
            Vector2 at = Racks[i];
            Vector2 toEye = (Eyepiece - at).normalized;
            Vector2 c = at - toEye * 0.7f;
            b.MeshPiece("Drift " + (i + 1), Kit.Mound(R(0.8f, 1.1f), R(0.35f, 0.45f), 700 + i), drift).Transform.position = b.WorldPoint(c.x, c.y, Floor - 0.05f, false);
        }
        for (int i = 0; i < EmptyDrifts.Length; i++)
            b.MeshPiece("Empty Drift " + (i + 1), Kit.Mound(R(0.8f, 1.1f), R(0.4f, 0.6f), 800 + i), drift).Transform.position = b.WorldPoint(EmptyDrifts[i].x, EmptyDrifts[i].y, Floor - 0.05f, false);
    }

    // ================================================================ SDK parts

    Piece P(string prefab, float x, float z, float h, float yaw, string name)
        => b.Part(prefab, x, z, Floor + h, yaw, false).Named(name);

    void BuildBaubles(Texture2D sheet)
    {
        for (int i = 0; i < Racks.Length; i++)
        {
            Vector2 at = Racks[i];
            Piece tile = P("Peg Tile", at.x, at.y, 1f, 0f, "Bauble " + (i + 1));
            tile.Transform.position = b.Root.transform.TransformPoint(TileSpot(at));
            tile.Transform.rotation = b.Root.transform.rotation * RackTurn(at);
            tile.Set("PuzzlePegTile", "imageSet", 4)                    // Custom (the enum's index; its value is 9)
                .Set("PuzzlePegTile", "image", i % 8 + 1)               // tiles n and n + 8 show design n
                .Set("PuzzlePegTile", "customSheet", sheet)
                .Set("PuzzlePegTile", "invisibleInkView", true);
        }
    }

    void BuildParts()
    {
        // ---- the start corner, in the open south of the stand ----
        IsleKit.StartCorner(b, 6f, -19.5f, 0f, true, Floor, false);

        // ---- the Bauble Stand, where the Hut stood: a roofed board on posts facing south, out of the wood. On its
        // face the Display of the three baubles wanted, the board's three slots under its three flaps, and the check
        // screen beside them. Parts facing south run left to right towards +x.
        Piece stand = IsleKit.BoardOnPosts(b, "Bauble Stand", 0f, StandZ, 180f, StandWide, StandLow, StandHigh, IsleKit.Board, false);
        stand.Transform.position = b.WorldPoint(0f, StandZ, Floor, false);      // on the pad's top
        Piece wanted = P("Display", SlotOne + SlotPitch, StandFace - DisplayBack, 2.0f, 180f, "Wanted Baubles").Set("PuzzleDisplay", "size", 2);      // Three
        Piece board = P("Peg Board", SlotOne, StandFace, 1.3f, 180f, "Bauble Board").Set("PuzzlePegBoard", "size", 2);                               // Slots3
        Piece check = P("Validator", 1.0f, StandFace, 1.6f, 180f, "Bauble Check Screen");
        Component wantedC = wanted.Component("PuzzleDisplay"), boardC = board.Component("PuzzlePegBoard"), checkC = check.Component("PuzzleValidator");
        Sdk.Add(boardC, "answerFrom", wantedC, wantedC, wantedC);      // slot n follows flap n, both counted from the left
        Sdk.AddInts(boardC, "answerFromFlap", 1, 2, 3);
        Sdk.Set(boardC, "validator", checkC);
        // The Validator prefab carries its own poke button (a child 0.44 m under the screen, already its pokeButton):
        // one press checks. A right board wins by itself; onIncorrect is not wired, so a wrong one only shows the cross.
        Component pokeC = check.Component("PuzzlePokeButton");
        pokeC.gameObject.name = "Bauble Check";
        Sdk.Set(checkC, "pokeButton", pokeC);

        // ---- the tower: the Invisible Ink telescope in its bay; a second start knob and its live light on the east rail ----
        P("Telescope", 0f, 9.55f, DeckTop + 1.1f, 180f, "Spotter Scope").Set("PuzzleTelescope", "invisibleInkView", true);
        P("Switch Start", 1.607f, 11.4f, DeckTop + 1.0f, -90f, "Tower Knob");
        P("Live Light", 1.674f, 11.4f, DeckTop + 1.17f, -90f, "Tower Live Light");
        Piece towerLamp = P("Light 1", -1.3f, 13.0f, DeckTop, 0f, "Tower Lamp");
        P("Confetti", 0f, 12.0f, DeckTop + 0.1f, 0f, "Tower Confetti");

        // ---- a walkie-talkie on the tower's deck and one on the stump by the stand ----
        P("Vanilla Item", -1.0f, 12.8f, DeckTop + 0.21f, 0f, "Tower Walkie").Set("PuzzleVanillaItem", "itemType", 4);      // WalkieTalkie
        P("Vanilla Item", WalkieStump.x, WalkieStump.y, StumpTop + 0.21f, 0f, "Stand Walkie").Set("PuzzleVanillaItem", "itemType", 4);

        // ---- the tower's lamp burns while the puzzle runs; nothing else happens on start or stop ----
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", towerLamp.Component("PuzzleLight"), "TurnOn");
        root.On("PuzzleSDK", "onPuzzleStop", towerLamp.Component("PuzzleLight"), "TurnOff");
    }

    // ================================================================ lights

    void BuildLights()
    {
        // all the way round the palisade's top, over both gateways (a hanging point every 4 m or less)
        var ring = new List<Vector3>();
        Vector2[] corners = { new Vector2(-12f, -10.25f), new Vector2(-12f, 3.75f), new Vector2(12f, 3.75f), new Vector2(12f, -10.25f), new Vector2(-12f, -10.25f) };
        for (int i = 0; i + 1 < corners.Length; i++)
        {
            Vector2 a = corners[i], c = corners[i + 1];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, c) / 4f));
            for (int k = (i == 0 ? 0 : 1); k <= n; k++)
            {
                Vector2 p = Vector2.Lerp(a, c, (float)k / n);
                ring.Add(new Vector3(p.x, Floor + WallH + 0.3f, p.y));
            }
        }
        Kit.LightString(b, "Palisade Lights", ring, 0.25f, 0.45f, false, false, 71);
        // up the tower stair, on its east wall, and round the tower's roof
        var stair = new List<Vector3>();
        for (int k = 0; k <= 3; k++)
        {
            float t = k / 3f;
            stair.Add(new Vector3(1.92f, Floor + Mathf.Lerp(DeckTop + 0.9f, 1.3f, t), Mathf.Lerp(13.4f, 20f, t)));
        }
        Kit.LightString(b, "Tower Stair Lights", stair, 0.15f, 0.45f, false, false, 72);
        float roof = Floor + 6.55f;
        Kit.LightString(b, "Tower Roof Lights", new List<Vector3>
        {
            new Vector3(-2.05f, roof, 9.65f), new Vector3(2.05f, roof, 9.65f), new Vector3(2.05f, roof, 13.6f),
            new Vector3(-2.05f, roof, 13.6f), new Vector3(-2.05f, roof, 9.65f),
        }, 0.2f, 0.4f, false, false, 73);
        // round the start corner (its south and east sides; open towards the stand)
        Kit.LightString(b, "Corner Lights", new List<Vector3>
        {
            new Vector3(2.4f, Floor + 2.4f, -21.3f), new Vector3(6.1f, Floor + 2.4f, -21.3f), new Vector3(9.8f, Floor + 2.4f, -21.3f),
            new Vector3(9.8f, Floor + 2.4f, -19.5f), new Vector3(9.8f, Floor + 2.4f, -17.9f),
        }, 0.4f, 0.5f, false, true, 74);
        // under the front edge of the stand's little roof (its underside is 0.3 m over the face's top), so that the
        // Display and the board have a light to be read by at night
        float eave = Floor + StandHigh + 0.28f, eaveZ = StandZ - 0.33f;
        Kit.LightString(b, "Stand Lights", new List<Vector3>
        {
            new Vector3(-StandWide / 2f - 0.3f, eave, eaveZ), new Vector3(StandWide / 2f + 0.3f, eave, eaveZ),
        }, 0.12f, 0.3f, false, false, 75);
    }

    // ================================================================ checks

    // Every fir in the wood must stand 1.6 m or more (and clear of its own boughs) off every line from the eyepiece to
    // a rack or the lectern, or the telescope cannot see past it
    static void CheckSightLines()
    {
        var targets = new List<Vector2>(Racks) { Lectern };
        foreach (Vector3 f in WoodFirs)
        {
            float need = Mathf.Max(1.6f, f.z * 0.27f * 1.12f + 0.3f);
            foreach (Vector2 t in targets)
            {
                float d = SegmentDistance(new Vector2(f.x, f.y), Eyepiece, t);
                if (d < need)
                    throw new Exception("BaubleHunt: the fir at (" + f.x + ", " + f.y + ") stands " + d.ToString("0.00") + " m off the telescope's line to (" + t.x + ", " + t.y + "); it needs " + need.ToString("0.00") + " m");
            }
        }
        // and no rack may hide another from the eyepiece (the line to a rack passes 0.4 m or more over a nearer one)
        foreach (Vector2 far in Racks)
            foreach (Vector2 near in targets)
            {
                if (near == far) continue;
                float dFar = Vector2.Distance(Eyepiece, far), dNear = Vector2.Distance(Eyepiece, near);
                if (dNear >= dFar || SegmentDistance(near, Eyepiece, far) > 0.8f) continue;
                float lineHeight = EyeHeight - (EyeHeight - 1.0f) * dNear / dFar;
                if (lineHeight < 1.55f)
                    throw new Exception("BaubleHunt: the rack or lectern at (" + near.x + ", " + near.y + ") hides the rack at (" + far.x + ", " + far.y + ") from the telescope");
            }
    }

    // ================================================================ the bauble sheet

    // The eight designs as a custom peg sheet: 2048 px, a 4 x 4 grid, top-left cell blank, designs 1..8 in cells 1..8
    // (cells 9..15 blank). Each feature is told apart by value or shape, not hue (the telescope is a colour filter):
    // body dark crimson or pale gold, band high or low (white with dark edges), five stars or five dots in the colour
    // opposite the body's value. Drawn here (not by a Python tool) and only redrawn when SheetVersion changes.
    static Texture2D BaubleSheet()
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", SheetPath));
        var existing = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
        if (!System.IO.File.Exists(full) || existing == null || existing.userData != SheetVersion)
        {
            const int Size = 2048, Cell = Size / 4;
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int col = x / Cell, rowFromTop = 3 - y / Cell, cell = rowFromTop * 4 + col;
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x % Cell + 0.25f + 0.5f * sx) / Cell, v = (y % Cell + 0.25f + 0.5f * sy) / Cell;
                            sum += Shade(cell >= 1 && cell <= 8 ? cell : 0, u, v);
                        }
                    px[y * Size + x] = sum / 4f;
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.userData = SheetVersion;
            importer.SaveAndReimport();
        }
        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        if (sheet == null) throw new Exception("BaubleHunt: could not make " + SheetPath);
        return sheet;
    }

    // One sample of a cell: design 0 is the blank; (u, v) run 0..1 across and up the cell
    static Color Shade(int design, float u, float v)
    {
        var background = new Color(0.52f, 0.57f, 0.64f);
        var outline = new Color(0.08f, 0.07f, 0.08f);
        if (design == 0) return background;
        bool dark = design <= 4, high = (design - 1) / 2 % 2 == 0, stars = (design - 1) % 2 == 0;
        var body = dark ? new Color(0.34f, 0.04f, 0.08f) : new Color(0.97f, 0.87f, 0.55f);
        var mark = dark ? new Color(0.98f, 0.96f, 0.88f) : new Color(0.12f, 0.07f, 0.09f);
        const float Cx = 0.5f, Cy = 0.45f, R = 0.33f;

        // the cap and its loop
        float lr = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.875f) * (v - 0.875f));
        if (Mathf.Abs(lr - 0.035f) < 0.011f) return outline;
        if (u > 0.42f && u < 0.58f && v > 0.755f && v < 0.84f)
            return (u < 0.43f || u > 0.57f || v > 0.83f || v < 0.765f) ? outline : new Color(0.66f, 0.64f, 0.58f);

        float dx = u - Cx, dy = v - Cy, d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d > R) return background;
        if (d > R - 0.012f) return outline;

        // the band: high across the shoulder, low across the belly; white with a dark edge
        float bandY = high ? Cy + 0.55f * R : Cy - 0.5f * R;
        float bd = Mathf.Abs(v - bandY);
        if (bd < 0.06f) return bd > 0.048f ? outline : new Color(0.97f, 0.97f, 0.97f);

        // five marks: three across the middle and two on the side away from the band
        float row2 = high ? Cy - 0.2f : Cy + 0.2f;
        for (int i = 0; i < MarkX.Length; i++)
        {
            float sx = MarkX[i], sy = i < 3 ? Cy : row2;
            if (stars ? InStar(u - sx, v - sy, 0.07f, 0.03f) : (u - sx) * (u - sx) + (v - sy) * (v - sy) < 0.05f * 0.05f)
                return mark;
        }
        return body;
    }

    // Inside a five-pointed star (point up) of outer radius ro and inner radius ri centred on the origin
    static bool InStar(float x, float y, float ro, float ri)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        if (r > ro) return false;
        if (r < ri) return true;
        float a = Mathf.Atan2(x, y);                       // 0 straight up, clockwise
        float step = Mathf.PI / 5f;
        float t = Mathf.Repeat(a, 2f * step);
        // the sector runs from a point (angle 0, radius ro) to a notch (angle step, radius ri), or back
        float a0 = 0f, r0 = ro, a1 = step, r1 = ri;
        if (t > step) { a0 = step; r0 = ri; a1 = 2f * step; r1 = ro; }
        var p0 = new Vector2(Mathf.Sin(a0) * r0, Mathf.Cos(a0) * r0);
        var p1 = new Vector2(Mathf.Sin(a1) * r1, Mathf.Cos(a1) * r1);
        var q = new Vector2(Mathf.Sin(t) * r, Mathf.Cos(t) * r);
        // inside when q is on the origin's side of the edge p0-p1
        float side = (p1.x - p0.x) * (q.y - p0.y) - (p1.y - p0.y) * (q.x - p0.x);
        float origin = (p1.x - p0.x) * (0f - p0.y) - (p1.y - p0.y) * (0f - p0.x);
        return side * origin >= 0f;
    }
}
