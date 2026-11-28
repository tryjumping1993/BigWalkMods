// Reindeer Count: how many noses are glowing? A counting puzzle one player can finish, with no clock.
//
// A sealed, roofed timber shed (32 x 32 m inside) holds twenty reindeer round a decorated fir: eighteen in closed
// stalls and two under open pavilions. Each of three walls (West, East, North) has one long window band of glass,
// 1 m tall, and outside each runs a raised gallery with its own stair. A closed stall is a roofed box open on one
// side only, aimed at one gallery, and its reindeer's nose is on the back wall: a lamp in the middle of a painted
// red disc. Six stalls face each gallery, and no other gallery sees into them. The two pavilions (one gold, one
// green, the tops of their posts red round a hanging lamp) are open all round and show from every gallery, so each
// is counted once, not once a window.
//
// At every start some of the twenty noses light, a different number each time (6 to 13; the CountDeal script).
// Walk round the galleries, count the lit noses through each window band, add them up, set the counter on the
// Counting Post beside the start corner to the total and press the button under its check screen. A right count
// wins. A wrong one shows the cross and lights a different set of noses, so the number cannot be found by trying
// them all. One player can do it alone; a group puts someone on each gallery and adds up at the post.
// (Before the user's rules of 2026-11-24 this was Roll Call: spotters on the galleries called one lit nose at a time
// to a presser shut in a sealed Tally Barn with a floor plan of pressure pads, on 7 s phases, thirteen in a row.)
//
// The stalls are placed at build time by a seeded search, and the build proves its sight lines: every nose seen from
// its own gallery, no closed stall's nose from any other gallery, and no point outside the shed (ground to 40 m up,
// 60 m out) that sees the stalls of two galleries, so no one place shows them all. Any failure fails the build.
//
// Coordinates: local metres, x east, z north (+z forward), heights above the top of one level pad (Floor above the
// root); every piece is placed onGround: false on that pad. Yaw: 0 faces +z, 90 +x, -90 -x, 180 -z.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bwm;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ReindeerCountPuzzle : PuzzleDef
{
    public override string Name => "ReindeerCount";
    public override Vector2 Footprint => new Vector2(54f, 70f);
    // layout-final: a big farm flat on the north shore by Northwest and North Shore stations, its north side open
    public override Vector2? FixedSite => new Vector2(-1590f, -720f);

    const float Floor = 0.2f;           // the pad's top, above the root's level
    const int Least = 6, Most = 13;     // how many of the twenty noses a deal lights
    // CountDeal finds its parts by name: every lamp whose name starts with NosePrefix, and the check screen. Nothing
    // else in the puzzle may have a name that starts with NosePrefix.
    const string NosePrefix = "Nose ", CheckName = "Nose Validator";
    // Near-white: the fir's lights, and every nose lamp (a lamp's colour tints its lens lit or not, so a red lamp
    // looks the same on and off; this one is a dull disc when off and a bright one when lit, on the nose's red paint)
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);

    PuzzleBuilder b;
    readonly List<Solid> solids = new List<Solid>();

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(56f, 72f, 0f, -1f);
        b.DebugTeleport(0f, -30f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks anywhere over the puzzle (Winter Walk): no hopping between galleries or onto the roofs
        IsleKit.CalmAir(b, "Reindeer Count", 0f, -1f, new Vector3(70f, 150f, 86f), 0f, -10f, false);

        // ---- the layout: placed and proved by the seeded search (throws if no seed passes) ----
        var log = new List<string>();
        Layout plan = Find(log);
        foreach (string line in log) Debug.Log("[BWM] Reindeer Count: " + line);
        solids.AddRange(plan.Solids);

        // ---- meshes, one per group of solids ----
        Emit("Ground Pad", "Pad", snowy);
        Emit("Reindeer Shed", "Shed", snowy);
        Emit("Shed Inside", "Inside", bare);
        Emit("West Gallery Deck", "West Gallery", snowy);
        Emit("East Gallery Deck", "East Gallery", snowy);
        Emit("North Gallery Deck", "North Gallery", snowy);

        // the fir in the middle of the shed (indoors: no snow on it), its warm white and gold lights (no red: red is
        // the noses' colour)
        var fir = new MeshBuilder();
        Kit.AddPine(fir, new Vector3(HX, 0.02f, HZ), 4.2f, 2512);
        OnPad("The Shed Tree", fir, bare, false);
        var spiral = new List<Vector3>();
        for (int k = 0; k <= 20; k++)
        {
            float t = k / 20f, h = 0.9f + 3.1f * t, r = 1.25f * (4.3f - h) / 3.6f + 0.05f, a = t * 3f * 2f * Mathf.PI;
            spiral.Add(new Vector3(HX + r * Mathf.Cos(a), Floor + h, HZ + r * Mathf.Sin(a)));
        }
        Kit.LightString(b, "Shed Tree Lights", spiral, 0.05f, 0.3f, false, false, 2513, new[] { Warm, IsleKit.Gold });

        // ================= the shed: the noses, the glass, the room lights =================
        // One lamp a stall, "Nose 1" to "Nose 20": a disc on a closed stall's back wall, in the middle of its red
        // paint, and a pendant under a pavilion's roof, between the red tops of its posts. Their light stops inside
        // the stall (range 1.5 m), so no glow shows where the lamp itself does not.
        foreach (Stall s in plan.Stalls)
        {
            Piece lamp = s.Kind == Kind.P
                ? b.Part("Light 2", s.C.x, s.C.y, Floor + PavPivot, 0f, false)
                : b.Part("Light 6", s.Lamp.x, s.Lamp.z, Floor + LampH, s.Yaw, false);
            lamp.Named(NosePrefix + s.Nose).Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 1.5f);
        }

        // eight panes per window band, flush with the wall's outer face (no ledge outside), between the mullions
        for (int k = 0; k < 8; k++)
        {
            float along = WinZ0 + k * (PaneW + Mullion) + PaneW / 2f;
            Pane("Window West " + (k + 1), -ShedOut + 0.02f, along, 90f);
            Pane("Window East " + (k + 1), ShedOut - 0.02f, along, 90f);
            Pane("Window North " + (k + 1), -NWinX + k * (PaneW + Mullion) + PaneW / 2f, ShedZ1 - 0.02f, 0f);
        }
        int light = 1;
        foreach (float dx in new[] { -8f, 8f })
            foreach (float dz in new[] { -8f, 8f })
                b.Empty("Room Light " + light++ + " FFE6C4 12 1.1", HX + dx, HZ + dz, Floor + 4.2f, false);

        // confetti on each gallery, for whoever is still up there when the count is checked
        b.Part("Confetti", -17.3f, 17.5f, Floor + Deck + 0.2f, 0f, false).Named("West Confetti");
        b.Part("Confetti", 17.3f, 17.5f, Floor + Deck + 0.2f, 0f, false).Named("East Confetti");
        b.Part("Confetti", 8.5f, 26.3f, Floor + Deck + 0.2f, 0f, false).Named("North Confetti");

        // ================= the start corner and the Counting Post, side by side on the pad, facing arriving players =================
        IsleKit.StartCorner(b, -4.0f, -27.5f, 180f, true, Floor, false);
        Piece check = CountingPost(bare);

        // ================= the rule: start lights some of the noses and tells the check screen how many (CountDeal);
        // a wrong count lights a different set; stop puts them all out. A right count wins by itself =================
        Component deal = b.AddCustomScript(b.Empty("Count Deal", PostX, PostZ, Floor + 0.5f, false), "CountDeal");
        Sdk.Set(deal, "lampPrefix", NosePrefix);
        Sdk.Set(deal, "validator", CheckName);
        Sdk.Set(deal, "least", Least);
        Sdk.Set(deal, "most", Most);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", deal, "Deal");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", deal, "Dark");
        check.On("PuzzleCountValidator", "onIncorrect", deal, "Redeal");      // deals again 3 s later, once the cross has been seen

        // ================= lights: a U round the site, open to the south where players arrive, every post 7 m or
        // more from a stair; a short string under the Counting Post's roof =================
        var site = new List<Vector3>();
        foreach (Vector2 p in new[] { new Vector2(-26f, -33f), new Vector2(-26f, -1f), new Vector2(-26f, 31f), new Vector2(0f, 31f),
                                      new Vector2(26f, 31f), new Vector2(26f, -1f), new Vector2(26f, -33f) })
            site.Add(new Vector3(p.x, Floor + 2.6f, p.y));
        Kit.LightString(b, "Site Lights", site, 0.4f, 0.5f, false, true, 2514);
        float eaveZ = PostZ - 0.36f, eaveH = Floor + PostHigh + 0.28f;      // the front edge of the post's little roof
        Kit.LightString(b, "Counting Post Lights", new List<Vector3> { new Vector3(PostX - PostWide / 2f - 0.35f, eaveH, eaveZ), new Vector3(PostX + PostWide / 2f + 0.35f, eaveH, eaveZ) },
            0.12f, 0.3f, false, false, 2515);
    }

    // ---- the Counting Post: a roofed board on posts (IsleKit.BoardOnPosts, 0.12 m thick) east of the start corner,
    // facing south. Its middle, its south face, its width and its face's foot and top above the pad ----
    const float PostX = 3.0f, PostZ = -27.5f, PostFace = PostZ - 0.06f, PostWide = 2.8f, PostLow = 0.6f, PostHigh = 2.5f;
    // Parts facing south run left to right towards +x, and their own +x points west. A Count Input is 1.02 m wide
    // from 0.66 m west of its pivot (its + and - panel) to 0.36 m east, 0.84 m tall about its pivot, and its back is
    // 0.17 m behind the pivot; a Count Validator's back is at its pivot (part-metrics)
    const float CounterX = 2.5f, CounterH = 1.2f, CounterBack = 0.17f, CheckX = 3.75f, CheckH = 1.7f;

    // Builds the post and returns its check screen
    Piece CountingPost(Material bare)
    {
        Piece post = IsleKit.BoardOnPosts(b, "Counting Post", PostX, PostZ, 180f, PostWide, PostLow, PostHigh, IsleKit.Board, false);
        post.Transform.position = b.WorldPoint(PostX, PostZ, Floor, false);      // on the pad's top

        // the counter (the game's + and - counter, 0 to 99) on the left and the check screen on the right
        Piece counter = b.Part("Count Input", CounterX, PostFace - CounterBack, Floor + CounterH, 180f, false).Named("Counter");
        Piece check = b.Part("Count Validator", CheckX, PostFace, Floor + CheckH, 180f, false).Named(CheckName);
        Component checkC = check.Component("PuzzleCountValidator");
        Sdk.Set(checkC, "input", counter.Component("PuzzleCountInput"));
        // The Count Validator prefab carries its own poke button (a child 0.51 m under the screen, already its
        // pokeButton): one press checks, and the screen re-arms it after each result.
        Component pokeC = check.Component("PuzzlePokeButton");
        pokeC.gameObject.name = "Check Button";
        Sdk.Set(checkC, "pokeButton", pokeC);

        // a bracket under the counter, and over it a painted reindeer with a glowing nose, to say what is counted:
        // flat paint on the board's face, each layer 2 cm prouder than the one under it
        var trim = new MeshBuilder();
        float mid = CounterX - 0.15f;      // the counter's middle
        Kit.AddBox(trim, new Vector3(mid, CounterH - 0.42f - 0.065f, PostFace - 0.3f), new Vector3(1.1f, 0.06f, 0.6f), IsleKit.DarkWood);
        void Paint(float x, float y0, float wide, float tall, Color colour, int layer = 1)
            => Kit.AddBox(trim, new Vector3(x, y0, PostFace - 0.01f * layer), new Vector3(wide, tall, 0.02f * layer), colour);
        var brown = new Color(0.45f, 0.3f, 0.18f);
        var dark = new Color(0.16f, 0.11f, 0.08f);
        Paint(mid, 1.78f, 0.36f, 0.42f, brown);                                  // the head
        foreach (float side in new[] { -1f, 1f })
        {
            Paint(mid + side * 0.24f, 2.06f, 0.12f, 0.08f, brown);               // ears
            Paint(mid + side * 0.12f, 2.20f, 0.05f, 0.24f, IsleKit.Cream);       // antlers
            Paint(mid + side * 0.21f, 2.32f, 0.14f, 0.05f, IsleKit.Cream);
            Paint(mid + side * 0.09f, 2.05f, 0.05f, 0.05f, dark, 2);             // eyes
            Paint(mid + side * 0.24f, 1.875f, 0.10f, 0.04f, IsleKit.Gold);       // rays beside the nose
        }
        var south = new Vector2(0f, -1f);
        AddSolid(trim, Solid.Roundel(new Vector3(mid, 1.895f, PostFace), south, 0.11f, 0.04f, NoseRed, ""));       // the nose's red paint
        AddSolid(trim, Solid.Roundel(new Vector3(mid, 1.895f, PostFace), south, 0.06f, 0.06f, Warm, ""));          // and the lamp, lit
        OnPad("Counting Post Paint", trim, bare, false);
        return check;
    }

    // A clear glass pane in a window band, centred at (x, z) half way up the band; yaw 90 on the side walls, 0 on the
    // north (PuzzleBuilder.Part keeps the Glass prefab upright; Pane sizes it in metres)
    void Pane(string name, float x, float z, float yaw)
    {
        b.Part("Glass", x, z, Floor + (Sill + Head) / 2f, yaw, false).Named(name)
            .Pane(PaneW, (Head - Sill)).Set("PuzzleGlass", "style", 2);
    }

    // A mesh built in pad-top coordinates, placed on the pad
    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, Floor, false);
        return piece;
    }

    // Every solid of one group into one mesh piece
    void Emit(string name, string group, Material material)
    {
        var mb = new MeshBuilder();
        foreach (Solid s in solids)
            if (s.Group == group) AddSolid(mb, s);
        if (mb.VertexCount == 0) throw new Exception("Reindeer Count: nothing in group " + group);
        OnPad(name, mb, material);
    }

    // A convex solid's faces, flat-shaded, each wound to face out (Make oriented them)
    static void AddSolid(MeshBuilder mb, Solid s)
    {
        foreach (int[] f in s.F)
            for (int k = 1; k + 1 < f.Length; k++)
                mb.Tri(s.P[f[0]], s.P[f[k]], s.P[f[k + 1]], s.Colour);
    }

    // ==== PURE BEGIN: geometry, placement and sight checks (no editor calls; run offline as well as in the build) ====

    public enum Kind { W, E, N, P }
    public const int Gw = 0, Ge = 1, Gn = 2;          // the galleries
    public static string GalleryName(int g) => g == Gw ? "West" : g == Ge ? "East" : "North";
    // Whether gallery g must see a stall of this kind: its own six closed stalls, and from the West and East the
    // pavilions. A pavilion is open all round, so the North gallery sees it too wherever nothing stands in the way:
    // the search neither asks for that nor forbids it.
    public static bool Sees(int g, Kind k) => k == Kind.P ? g != Gn : (int)k == g;

    // ---- the shed (local metres, heights above the pad top) ----
    public const float HX = 0f, HZ = 9f;                                      // the shed's centre H
    public const float ShedOut = 16.3f, ShedIn = 16f, ShedZ0 = -7.3f, ShedZ1 = 25.3f, ShedInZ0 = -7f, ShedInZ1 = 25f;
    public const float Eaves = 4.6f, RoofTop = 4.9f, Ridge = 6.0f, Sill = 3.0f, Head = 4.0f;
    public const float WinZ0 = 1f, WinZ1 = 17f, NWinX = 8f, Mullion = 0.05f, PaneW = (16f - 7f * Mullion) / 8f;
    // The decks stand 2.9 m up: players' eyes are only 0.6 m above their feet, so a standing eye is at 3.5 m,
    // mid-window. Eye points: crouch 0.4, stand 0.6, 1.0, jump 1.6 above the deck.
    public const float Deck = 2.9f;
    public const int StairSteps = 10, Stand = 1;
    public static readonly float[] EyeUp = { 0.4f, 0.6f, 1.0f, 1.6f };
    // ---- the stalls ----
    public const float StallW = 2.0f, StallD = 2.8f, StallWall = 0.15f, StallRoof = 2.65f, LampH = 2.2f;
    public const float LampOut = 0.142f, DiscOut = 0.222f;      // a Light 6's pivot and face in front of its back (part-metrics)
    public const float PavHalf = 1.2f, PavPost = 0.15f, PavBulb = 2.25f, PavPivot = 2.6f;
    // The noses' paint: a red disc on a closed stall's back wall, behind and round its lamp (0.48 m across), and the
    // tops of a pavilion's posts, from PavRed up, round its pendant. Below that the posts, and the roof, are in the
    // pavilion's own colour (by the order they were placed in: one gold, one green), so that the same pavilion can
    // be told from every gallery.
    public const float NoseDisc = 0.42f, PavRed = 1.9f;
    public static readonly Color NoseRed = new Color(0.95f, 0.1f, 0.08f);
    public static readonly Color[] PavColours = { IsleKit.Gold, IsleKit.Green };
    public const float MinApart = 4.8f, FirClear = 4.5f, MarkClear = 3.6f, Spread = 13.5f;
    // Regions: North stalls z <= 13 (13 m or more from the North deck); West, East and pavilions anywhere (keeping
    // West stalls to x >= -4 and East to x <= 4 left almost no layout that passes the galleries' check: a stall's lit
    // side walls show up to about 45 degrees off its axis)
    public const float NorthMaxZ = 13f;
    public const int Tries = 600, SampleStep = 3;      // candidates per stall; every third exterior point is checked while drawing
    public static readonly int[] Rejects = new int[6];   // diagnostics: candidates refused for (own sight, hiding another, seen from W, E, N, outside)
    public static string LastFail = "";     // which stall the last failed draw could not place
    // ---- the search ----
    public const int SeedBase = 2512, SeedSpan = 2000;
    public const int FoundSeed = 2570;                 // the seed the last search found: tried first, so builds are stable

    public static Vector2 Turn(float x, float z, float yaw)
    {
        float a = yaw * Mathf.Deg2Rad;
        return new Vector2(x * Mathf.Cos(a) + z * Mathf.Sin(a), -x * Mathf.Sin(a) + z * Mathf.Cos(a));
    }
    public static float Aim(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;
    static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);
    static float Ax(Vector3 v, int k) => k == 0 ? v.x : k == 1 ? v.y : v.z;

    // A convex solid: its corners, faces (wound to face out) and planes. The same list draws the meshes and blocks
    // the sight checks, so the two cannot drift apart.
    public sealed class Solid
    {
        public Vector3[] P; public int[][] F; public Vector3[] N; public float[] D;
        public Vector3 Min, Max;
        public Color Colour; public string Group;  // the mesh it is drawn in ("" = none: a check volume only)
        public bool Check = true;                  // takes part in the sight checks
        public bool Sure = true;                   // surely blocks sight (false: decks, rails, generous volumes; these only make "must see" harder)
        public int Owner = -1;                     // the stall it belongs to; -2 the fir and landmarks; -1 fixed

        public static Solid Make(Vector3[] p, int[][] f, Color colour, string group)
        {
            var s = new Solid { P = p, F = f, Colour = colour, Group = group, N = new Vector3[f.Length], D = new float[f.Length] };
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in p) c += v;
            c /= p.Length;
            s.Min = p[0]; s.Max = p[0];
            foreach (Vector3 v in p) { s.Min = Vector3.Min(s.Min, v); s.Max = Vector3.Max(s.Max, v); }
            for (int i = 0; i < f.Length; i++)
            {
                Vector3 a = p[f[i][0]], b2 = p[f[i][1]], e = p[f[i][2]];
                Vector3 n = Vector3.Cross(b2 - a, e - a).normalized;
                float d = Vector3.Dot(n, a);
                if (Vector3.Dot(n, c) - d > 0f) { n = -n; d = -d; Array.Reverse(f[i]); }
                s.N[i] = n; s.D[i] = d;
            }
            return s;
        }

        // A box standing on 'foot' (x, z) from height y0, size (across, up, along), turned by yaw
        public static Solid Box(Vector2 foot, float y0, Vector3 size, float yaw, Color colour, string group)
        {
            float hx = size.x / 2f, hz = size.z / 2f;
            Vector2[] q = { Turn(-hx, -hz, yaw), Turn(hx, -hz, yaw), Turn(hx, hz, yaw), Turn(-hx, hz, yaw) };
            var p = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                p[i] = new Vector3(foot.x + q[i].x, y0, foot.y + q[i].y);
                p[i + 4] = new Vector3(foot.x + q[i].x, y0 + size.y, foot.y + q[i].y);
            }
            return Make(p, new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } }, colour, group);
        }

        // An axis-aligned block between two corners
        public static Solid Block(float x0, float x1, float y0, float y1, float z0, float z1, Color colour, string group)
            => Box(new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f), y0, new Vector3(Mathf.Abs(x1 - x0), y1 - y0, Mathf.Abs(z1 - z0)), 0f, colour, group);

        // A convex profile in (z, y), in order round it, run along x from x0 to x1 (gables, roof slopes, the ridge)
        public static Solid ExtrudeX(float x0, float x1, Vector2[] zy, Color colour, string group)
        {
            int n = zy.Length;
            var p = new Vector3[2 * n];
            for (int i = 0; i < n; i++) { p[i] = new Vector3(x0, zy[i].y, zy[i].x); p[n + i] = new Vector3(x1, zy[i].y, zy[i].x); }
            var f = new List<int[]>();
            var cap0 = new int[n]; var cap1 = new int[n];
            for (int i = 0; i < n; i++) { cap0[i] = i; cap1[i] = n + i; }
            f.Add(cap0); f.Add(cap1);
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; f.Add(new[] { i, j, n + j, n + i }); }
            return Make(p, f.ToArray(), colour, group);
        }

        // An eight-sided frustum about (x, z), radius r0 at y0 to r1 at y1 (the fir's foliage, as a check volume)
        public static Solid Frustum(Vector2 c, float y0, float r0, float y1, float r1, string group)
        {
            var p = new Vector3[16];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                p[i] = new Vector3(c.x + r0 * Mathf.Cos(a), y0, c.y + r0 * Mathf.Sin(a));
                p[8 + i] = new Vector3(c.x + r1 * Mathf.Cos(a), y1, c.y + r1 * Mathf.Sin(a));
            }
            var f = new List<int[]> { new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 8, 9, 10, 11, 12, 13, 14, 15 } };
            for (int i = 0; i < 8; i++) { int j = (i + 1) % 8; f.Add(new[] { i, j, 8 + j, 8 + i }); }
            return Make(p, f.ToArray(), Color.white, group);
        }

        // A round plate of 16 sides standing against a wall: its back's middle at 'centre', facing along 'facing'
        // (x, z; a unit vector), 'thick' deep (the noses' red paint)
        public static Solid Roundel(Vector3 centre, Vector2 facing, float radius, float thick, Color colour, string group)
        {
            const int n = 16;
            Vector3 across = new Vector3(-facing.y, 0f, facing.x), front = new Vector3(facing.x, 0f, facing.y) * thick;
            var p = new Vector3[2 * n];
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                p[i] = centre + (across * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a)) * radius;
                p[n + i] = p[i] + front;
            }
            var f = new List<int[]>();
            var cap0 = new int[n]; var cap1 = new int[n];
            for (int i = 0; i < n; i++) { cap0[i] = i; cap1[i] = n + i; }
            f.Add(cap0); f.Add(cap1);
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; f.Add(new[] { i, j, n + j, n + i }); }
            return Make(p, f.ToArray(), colour, group);
        }

        // Whether the segment a .. a + d passes through the solid's inside (touching a face does not count)
        public bool Hits(Vector3 a, Vector3 d)
        {
            float t0 = 0f, t1 = 1f;
            for (int k = 0; k < 3; k++)
            {
                float ak = Ax(a, k), dk = Ax(d, k), lo = Ax(Min, k), hi = Ax(Max, k);
                if (Mathf.Abs(dk) < 1e-9f) { if (ak < lo || ak > hi) return false; continue; }
                float ta = (lo - ak) / dk, tb = (hi - ak) / dk;
                if (ta > tb) { float t = ta; ta = tb; tb = t; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            t0 = 0f; t1 = 1f;
            for (int i = 0; i < N.Length; i++)
            {
                float den = Vector3.Dot(N[i], d), num = D[i] - Vector3.Dot(N[i], a);
                if (Mathf.Abs(den) < 1e-9f) { if (num < 0f) return false; continue; }
                float t = num / den;
                if (den < 0f) { if (t > t0) t0 = t; }
                else if (t < t1) t1 = t;
                if (t0 >= t1) return false;
            }
            return t1 - t0 > 1e-5f && t0 < 1f - 1e-5f && t1 > 1e-5f;
        }

        public bool Contains(Vector3 p)
        {
            if (p.x < Min.x || p.x > Max.x || p.y < Min.y || p.y > Max.y || p.z < Min.z || p.z > Max.z) return false;
            for (int i = 0; i < N.Length; i++) if (Vector3.Dot(N[i], p) - D[i] > 0f) return false;
            return true;
        }

        // A lower bound on the distance from p to the solid (exact off a face, short of the truth off an edge or corner)
        public float Distance(Vector3 p)
        {
            float d = 0f;
            for (int i = 0; i < N.Length; i++) d = Mathf.Max(d, Vector3.Dot(N[i], p) - D[i]);
            return d;
        }
    }

    public sealed class Stall
    {
        public Kind Kind; public int Number, Id; public Vector2 C; public float Yaw;     // Id: the order it was placed in (its solids' Owner)
        public int Nose;                                       // 1 to 20: its lamp is "Nose <n>"
        public Vector3 Lamp;                                   // the lamp part's pivot (x, height, z)
        public readonly List<Vector3> Targets = new List<Vector3>();     // [0] the lamp's disc or bulb, then points its light reaches
        public readonly int[] Seen = new int[3];               // eye points of each gallery that see the lamp
        public string Name => Kind.ToString() + Number;
        public Vector2 At(float right, float forward) => C + Turn(right, forward, Yaw);
    }

    public sealed class Mark { public string Name; public Vector2 C; public float Yaw; public Vector3 Size; }

    public sealed class Layout
    {
        public int Seed;
        public readonly List<Stall> Stalls = new List<Stall>();
        public readonly List<Mark> Marks = new List<Mark>();
        public readonly List<Solid> Solids = new List<Solid>();
    }

    // ---- the search: seeds from FoundSeed upwards (wrapping round to SeedBase), the first that passes every check ----
    public static Layout Find(List<string> log)
    {
        var watch = Stopwatch.StartNew();
        List<Solid> fixedSolids = FixedSolids();
        List<Vector3> outside = ExteriorPoints(fixedSolids), sample = new List<Vector3>();
        for (int k = 0; k < outside.Count; k += SampleStep) sample.Add(outside[k]);
        int unplaced = 0, spacing = 0, gallery = 0, exterior = 0;
        string lastWhy = "";
        for (int n = 0; n <= SeedSpan; n++)
        {
            int seed = FoundSeed + n;
            if (seed > SeedBase + SeedSpan) seed -= SeedSpan + 1;
            Layout layout = Draw(seed, fixedSolids, sample);
            if (layout == null) { unplaced++; continue; }
            if (!Spacing(layout, out lastWhy)) { spacing++; continue; }
            if (!Galleries(layout, out lastWhy)) { gallery++; continue; }
            if (!Exterior(layout, outside, out lastWhy, out List<string> table)) { exterior++; log.Add("seed " + seed + " failed outside the shed: " + lastWhy); continue; }
            log.Add("seed " + seed + (seed == FoundSeed ? " (FoundSeed)" : " passed: set ReindeerCountPuzzle.FoundSeed = " + seed + " so later builds start there")
                + "; tried " + (n + 1) + " seeds (" + unplaced + " would not place, " + spacing + " failed spacing, " + gallery + " the galleries, "
                + exterior + " the exterior) in " + (watch.ElapsedMilliseconds / 1000f).ToString("0.0") + " s");
            log.Add("spacing: no other stall, landmark or the fir within 1.6 m of a nose's lamp");
            log.Add("stalls (kind, nose, centre, yaw; eye points of " + (21 * EyeUp.Length) + " per gallery that see its lamp: West / East / North):");
            foreach (Stall s in layout.Stalls)
                log.Add("  " + s.Name + "  Nose " + s.Nose + "  (" + s.C.x.ToString("0.0") + ", " + s.C.y.ToString("0.0") + ") yaw " + s.Yaw.ToString("0") + ": "
                    + s.Seen[Gw] + " / " + s.Seen[Ge] + " / " + s.Seen[Gn]);
            log.AddRange(table);
            return layout;
        }
        throw new Exception("Reindeer Count: no seed from " + SeedBase + " to " + (SeedBase + SeedSpan) + " passes (" + unplaced + " would not place, "
            + spacing + " failed spacing, " + gallery + " the galleries, " + exterior + " the exterior; last: " + lastWhy + "; last unplaced: " + LastFail
            + "; candidates refused for own sight " + Rejects[0] + ", hiding another " + Rejects[1] + ", seen from W/E/N " + Rejects[2] + "/" + Rejects[3] + "/" + Rejects[4]
            + ", outside " + Rejects[5] + ")");
    }

    // Draws one layout: the landmarks, then N, W and E stalls in their regions and the pavilions anywhere, by rejection
    public static Layout Draw(int seed, List<Solid> fixedSolids, List<Vector3> drawPoints = null)
    {
        var rng = new System.Random(seed);
        float R(float lo, float hi) => lo + (hi - lo) * (float)rng.NextDouble();
        var layout = new Layout { Seed = seed };
        var h = new Vector2(HX, HZ);
        var marks = new[] { ("Hay", new Vector3(2f, 1.2f, 2f)), ("Trough", new Vector3(2.4f, 0.6f, 0.6f)), ("Sledge", new Vector3(1.6f, 0.8f, 0.8f)) };
        foreach (var (name, size) in marks)
        {
            bool placed = false;
            for (int tries = 0; tries < 300 && !placed; tries++)
            {
                var c = new Vector2(HX + R(-12f, 12f), HZ + R(-12f, 12f));
                if ((c - h).magnitude < 3.6f) continue;
                bool ok = true;
                foreach (Mark m in layout.Marks) if ((m.C - c).magnitude < 3.5f) ok = false;
                if (!ok) continue;
                layout.Marks.Add(new Mark { Name = name, C = c, Yaw = R(0f, 180f), Size = size });
                placed = true;
            }
            if (!placed) return null;
        }
        layout.Solids.AddRange(fixedSolids);
        AddLandmarks(layout);

        // A candidate is kept only if its own gallery still sees its lamp from three standing points or more, it takes
        // no stall already placed below three, and no other gallery sees a closed stall's light (more stalls only hide
        // more, so a candidate that passes now still passes at the end; the full checks run again on the finished layout)
        Vector3[][,,] eyes = { Eyes(Gw), Eyes(Ge), Eyes(Gn) };
        var seen = new List<bool[][]>();          // per placed stall and gallery: which of the 7 standing points see its lamp
        int[] kindsSeen = drawPoints == null ? null : new int[drawPoints.Count];      // per sample point: the kinds it sees so far
        Kind[] order = { Kind.N, Kind.W, Kind.E, Kind.P };
        int[] counts = { 6, 6, 6, 2 };
        for (int o = 0; o < order.Length; o++)
            for (int i = 0; i < counts[o]; i++)
            {
                Kind kind = order[o];
                bool placed = false;
                Solid[] all = Pick(layout, false), sure = Pick(layout, true);
                for (int tries = 0; tries < Tries && !placed; tries++)
                {
                    var c = new Vector2(HX + R(-Spread, Spread), HZ + R(-Spread, Spread));
                    if (kind == Kind.N && c.y > NorthMaxZ) continue;
                    if ((c - h).magnitude < FirClear) continue;
                    bool ok = true;
                    foreach (Stall s in layout.Stalls) if ((s.C - c).magnitude < MinApart) { ok = false; break; }
                    foreach (Mark m in layout.Marks) if ((m.C - c).magnitude < MarkClear) ok = false;
                    if (!ok) continue;
                    float yaw = kind == Kind.W ? Aim(c, new Vector2(-17.3f, 9f)) : kind == Kind.E ? Aim(c, new Vector2(17.3f, 9f))
                        : kind == Kind.N ? Aim(c, new Vector2(0f, 26.3f)) : 0f;
                    var st = new Stall { Kind = kind, C = c, Yaw = yaw, Id = layout.Stalls.Count };
                    var mine = new List<Solid>();
                    AddStall(mine, st);
                    var mineCheck = mine.FindAll(s => s.Check);
                    Solid[] withAll = Concat(all, mineCheck, false), withSure = Concat(sure, mineCheck, true);
                    // its own galleries see it
                    var vis = new bool[3][];
                    for (int g = 0; g < 3 && ok; g++)
                    {
                        if (!Sees(g, kind)) continue;
                        vis[g] = new bool[7];
                        int count = 0;
                        for (int k = 0; k < 7; k++) if (vis[g][k] = Visible(eyes[g][1, k, Stand], st.Targets[0], withAll)) count++;
                        if (count < 3) { ok = false; Rejects[0]++; }
                    }
                    // it hides no placed stall below three points
                    for (int j = 0; j < layout.Stalls.Count && ok; j++)
                        for (int g = 0; g < 3 && ok; g++)
                        {
                            if (seen[j][g] == null) continue;
                            int count = 0;
                            for (int k = 0; k < 7; k++) if (seen[j][g][k] && !HitsAny(mineCheck, eyes[g][1, k, Stand], layout.Stalls[j].Targets[0])) count++;
                            if (count < 3) { ok = false; Rejects[1]++; }
                        }
                    // no other gallery sees its light
                    for (int g = 0; g < 3 && ok; g++)
                    {
                        if (kind == Kind.P || Sees(g, kind)) continue;
                        foreach (Vector3 eye in eyes[g])
                        {
                            foreach (Vector3 t in st.Targets) if (Visible(eye, t, withSure)) { ok = false; Rejects[2 + g]++; break; }
                            if (!ok) break;
                        }
                    }
                    // no point outside (a sample of Exterior's) that sees it already sees a kind it must not go with: each
                    // placed stall's footprint was taken with fewer stalls in the way, so footprints only shrink later
                    bool[] foot = null;
                    if (ok && drawPoints != null)
                    {
                        foot = new bool[drawPoints.Count];
                        int bit = 1 << (int)kind;
                        for (int k = 0; k < drawPoints.Count && ok; k++)
                        {
                            foreach (Vector3 t in st.Targets)
                                if (Visible(drawPoints[k], t, withSure)) { foot[k] = true; break; }
                            if (foot[k] && Conflict(kindsSeen[k] | bit)) { ok = false; Rejects[5]++; }
                        }
                    }
                    if (!ok) continue;
                    if (foot != null)
                        for (int k = 0; k < foot.Length; k++) if (foot[k]) kindsSeen[k] |= 1 << (int)kind;
                    for (int j = 0; j < layout.Stalls.Count; j++)
                        for (int g = 0; g < 3; g++)
                            if (seen[j][g] != null)
                                for (int k = 0; k < 7; k++) if (seen[j][g][k] && HitsAny(mineCheck, eyes[g][1, k, Stand], layout.Stalls[j].Targets[0])) seen[j][g][k] = false;
                    layout.Stalls.Add(st);
                    layout.Solids.AddRange(mine);
                    seen.Add(vis);
                    placed = true;
                }
                if (!placed) { LastFail = kind + " stall " + (i + 1); return null; }
            }
        // number each kind from north to south, then west to east; the noses run on through the kinds (West 1 to 6,
        // East 7 to 12, North 13 to 18, the pavilions 19 and 20)
        layout.Stalls.Sort((p, q) => p.Kind != q.Kind ? p.Kind.CompareTo(q.Kind) : p.C.y != q.C.y ? q.C.y.CompareTo(p.C.y) : p.C.x.CompareTo(q.C.x));
        for (int i = 0, n = 0; i < layout.Stalls.Count; i++)
        {
            if (i > 0 && layout.Stalls[i].Kind != layout.Stalls[i - 1].Kind) n = 0;
            layout.Stalls[i].Number = ++n;
            layout.Stalls[i].Nose = i + 1;
        }
        return layout;
    }

    static Solid[] Concat(Solid[] a, List<Solid> extra, bool sureOnly)
    {
        var list = new List<Solid>(a);
        foreach (Solid s in extra) if (!sureOnly || s.Sure) list.Add(s);
        return list.ToArray();
    }

    static bool HitsAny(List<Solid> solids, Vector3 a, Vector3 b2)
    {
        Vector3 d = b2 - a;
        foreach (Solid s in solids) if (s.Hits(a, d)) return true;
        return false;
    }

    static Solid Add(List<Solid> into, Solid s, int owner, bool check = true, bool sure = true)
    {
        s.Owner = owner; s.Check = check; s.Sure = sure;
        into.Add(s);
        return s;
    }

    // A stall's walls, roof, lamp and sight targets, its nose's red paint, and its reindeer
    static void AddStall(List<Solid> into, Stall s)
    {
        const string g = "Inside";
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        float back = -StallD / 2f + StallWall;                   // the back wall's inner face (-1.25)
        if (s.Kind != Kind.P)
        {
            Add(into, Solid.Box(s.At(0f, -StallD / 2f + StallWall / 2f), 0f, new Vector3(StallW, StallRoof, StallWall), s.Yaw, wall, g), s.Id);
            float side = StallW / 2f - StallWall / 2f, len = StallD - StallWall;
            Add(into, Solid.Box(s.At(-side, back + len / 2f), 0f, new Vector3(StallWall, StallRoof, len), s.Yaw, wall, g), s.Id);
            Add(into, Solid.Box(s.At(side, back + len / 2f), 0f, new Vector3(StallWall, StallRoof, len), s.Yaw, wall, g), s.Id);
            Add(into, Solid.Box(s.At(0f, 0f), StallRoof, new Vector3(StallW, 0.15f, StallD), s.Yaw, roof, g), s.Id);
            s.Lamp = V(s.At(0f, back + LampOut), LampH);
            Add(into, Solid.Roundel(V(s.At(0f, back), LampH), Turn(0f, 1f, s.Yaw), NoseDisc, 0.03f, NoseRed, g), s.Id, false, false);     // paint: drawn only
            s.Targets.Add(V(s.At(0f, back + DiscOut + 0.01f), LampH));                     // the lamp's disc
            s.Targets.Add(V(s.At(-0.6f, back + 0.02f), LampH));                            // the back wall beside it
            s.Targets.Add(V(s.At(0.6f, back + 0.02f), LampH));
            float inner = StallW / 2f - StallWall - 0.02f;
            s.Targets.Add(V(s.At(-inner, back + 1.0f), LampH));                            // the side walls' inner faces
            s.Targets.Add(V(s.At(inner, back + 1.0f), LampH));
            s.Targets.Add(V(s.At(0f, back + 0.8f), StallRoof - 0.02f));                    // the roof's underside
        }
        else
        {
            Color own = PavColours[s.Id % PavColours.Length];
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Add(into, Solid.Box(s.At(sx * PavHalf, sz * PavHalf), 0f, new Vector3(PavPost, PavRed, PavPost), s.Yaw, own, g), s.Id);
                    Add(into, Solid.Box(s.At(sx * PavHalf, sz * PavHalf), PavRed, new Vector3(PavPost, StallRoof - PavRed, PavPost), s.Yaw, NoseRed, g), s.Id);
                }
            Add(into, Solid.Box(s.At(0f, 0f), StallRoof, new Vector3(2 * PavHalf + 0.3f, 0.15f, 2 * PavHalf + 0.3f), s.Yaw, own, g), s.Id);
            s.Lamp = V(s.C, PavPivot);
            s.Targets.Add(V(s.C, PavBulb));
            float inner = PavHalf - PavPost / 2f - 0.02f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    s.Targets.Add(V(s.At(sx * inner, sz * inner), 2.0f));
        }
        Reindeer(into, s);
    }

    // A reindeer lying down, its head towards the back wall, 0.35 m off the lamp's axis; top of the antlers 1.6 m.
    // Drawn from small boxes; for the checks, its body surely blocks and a generous box makes "must see" harder.
    static void Reindeer(List<Solid> into, Stall s)
    {
        const string g = "Inside";
        var brown = new Color(0.45f, 0.3f, 0.18f);
        var dark = new Color(0.16f, 0.11f, 0.08f);
        float x = 0.35f;
        void Part(float right, float forward, float y0, Vector3 size, Color c) =>
            Add(into, Solid.Box(s.At(x + right, forward), y0, size, s.Yaw, c, g), s.Id, false);
        Part(0f, 0.25f, 0.02f, new Vector3(0.5f, 0.42f, 1.1f), brown);                    // body
        foreach (float side in new[] { -0.2f, 0.2f })
        {
            Part(side, 0.6f, 0.02f, new Vector3(0.12f, 0.12f, 0.35f), brown * 0.85f);     // folded legs
            Part(side, -0.1f, 0.02f, new Vector3(0.12f, 0.12f, 0.35f), brown * 0.85f);
            Part(side * 0.5f, -0.42f, 1.0f, new Vector3(0.04f, 0.6f, 0.04f), IsleKit.Cream);    // antlers
            Part(side * 0.9f, -0.42f, 1.35f, new Vector3(0.16f, 0.04f, 0.04f), IsleKit.Cream);
        }
        Part(0f, -0.38f, 0.3f, new Vector3(0.16f, 0.55f, 0.18f), brown);                  // neck
        Part(0f, -0.5f, 0.82f, new Vector3(0.2f, 0.2f, 0.36f), brown);                    // head
        Part(0f, -0.7f, 0.86f, new Vector3(0.08f, 0.08f, 0.06f), dark);                   // its own nose (dark: the red one is the lamp's disc on the wall)
        Part(0f, 0.82f, 0.3f, new Vector3(0.1f, 0.1f, 0.08f), IsleKit.Cream);             // tail
        Add(into, Solid.Box(s.At(x, 0.25f), 0.02f, new Vector3(0.5f, 0.42f, 1.1f), s.Yaw, Color.white, ""), s.Id);
        Add(into, Solid.Box(s.At(x, 0.05f), 0f, new Vector3(0.6f, 1.7f, 1.6f), s.Yaw, Color.white, ""), s.Id, true, false);
    }

    // The hay stack, the frozen trough and the sledge (drawn and checked), and the fir as check volumes
    static void AddLandmarks(Layout layout)
    {
        const string g = "Inside";
        foreach (Mark m in layout.Marks)
        {
            if (m.Name == "Hay")
            {
                Add(layout.Solids, Solid.Box(m.C, 0f, m.Size, m.Yaw, new Color(0.82f, 0.68f, 0.34f), g), -2);
                Add(layout.Solids, Solid.Box(m.C, m.Size.y, new Vector3(1.6f, 0.25f, 1.6f), m.Yaw, new Color(0.78f, 0.64f, 0.3f), g), -2, false);
            }
            else if (m.Name == "Trough")
            {
                Add(layout.Solids, Solid.Box(m.C, 0f, m.Size, m.Yaw, IsleKit.Wood, g), -2);
                Add(layout.Solids, Solid.Box(m.C, m.Size.y, new Vector3(m.Size.x - 0.16f, 0.02f, m.Size.z - 0.16f), m.Yaw, IsleKit.Ice, g), -2, false);
            }
            else
            {
                Add(layout.Solids, Solid.Box(m.C, 0.15f, new Vector3(m.Size.x, 0.45f, m.Size.z), m.Yaw, IsleKit.Green, g), -2);
                foreach (float side in new[] { -0.32f, 0.32f })
                {
                    Vector2 off = Turn(0f, side, m.Yaw);      // the runners along the sledge's length (its local x)
                    Add(layout.Solids, Solid.Box(m.C + off, 0f, new Vector3(m.Size.x + 0.2f, 0.15f, 0.06f), m.Yaw, IsleKit.Gold, g), -2, false);
                }
                Add(layout.Solids, Solid.Box(m.C, 0f, new Vector3(m.Size.x, 0.8f, m.Size.z), m.Yaw, Color.white, ""), -2, true, false);
            }
        }
        // the fir: its sure core, and a generous cone for "must see"
        Add(layout.Solids, Solid.Frustum(new Vector2(HX, HZ), 0.7f, 0.85f, 4.2f, 0.1f, ""), -2);
        Add(layout.Solids, Solid.Frustum(new Vector2(HX, HZ), 0.5f, 1.3f, 4.35f, 0.15f, ""), -2, true, false);
    }

    // Everything that never moves: the shed's shell and floor, the galleries and the pad
    public static List<Solid> FixedSolids()
    {
        var list = new List<Solid>();
        Solid Add2(Solid s, bool check = true, bool sure = true) { s.Check = check; s.Sure = sure; list.Add(s); return s; }
        Color wood = IsleKit.Wood, dark = IsleKit.DarkWood;
        const float low = -0.3f;
        float o = ShedOut, i = ShedIn;

        Add2(Solid.Block(-27f, 27f, -1.5f, 0f, -35f, 33f, IsleKit.Stone, "Pad"), false);

        // ---- the shed: walls with the three window bands and their mullions, a blank south wall, roof and ridge ----
        const string sh = "Shed";
        foreach (float sx in new[] { -1f, 1f })
        {
            float a = sx * o, c = sx * i;
            Add2(Solid.Block(a, c, low, Eaves, ShedZ0, WinZ0, wood, sh));
            Add2(Solid.Block(a, c, low, Eaves, WinZ1, ShedZ1, wood, sh));
            Add2(Solid.Block(a, c, low, Sill, WinZ0, WinZ1, wood, sh));
            Add2(Solid.Block(a, c, Head, Eaves, WinZ0, WinZ1, wood, sh));
            for (int k = 1; k < 8; k++)
            {
                float m1 = WinZ0 + k * (PaneW + Mullion), m0 = m1 - Mullion;
                Add2(Solid.Block(a, c, Sill, Head, m0, m1, dark, sh));
            }
        }
        Add2(Solid.Block(-i, -NWinX, low, Eaves, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(NWinX, i, low, Eaves, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(-NWinX, NWinX, low, Sill, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(-NWinX, NWinX, Head, Eaves, ShedInZ1, ShedZ1, wood, sh));
        for (int k = 1; k < 8; k++)
        {
            float m1 = -NWinX + k * (PaneW + Mullion), m0 = m1 - Mullion;
            Add2(Solid.Block(m0, m1, Sill, Head, ShedInZ1, ShedZ1, dark, sh));
        }
        Add2(Solid.Block(-i, i, low, Eaves, ShedZ0, ShedInZ0, wood, sh));
        Add2(Solid.Block(-o - 0.15f, o + 0.15f, Eaves, RoofTop, ShedZ0 - 0.15f, ShedZ1 + 0.15f, dark, sh));
        Add2(Solid.ExtrudeX(-o - 0.15f, o + 0.15f, new[] { new Vector2(ShedZ0 - 0.15f, RoofTop), new Vector2(ShedZ1 + 0.15f, RoofTop), new Vector2(HZ, Ridge) }, dark, sh));
        Add2(Solid.Block(-i, i, -0.1f, 0.02f, ShedInZ0, ShedInZ1, dark, "Inside"));

        // ---- the galleries (rails and posts only make "must see" harder: they are not counted on to block) ----
        foreach (int gal in new[] { Gw, Ge })
        {
            float sx = gal == Gw ? -1f : 1f;
            string g = gal == Gw ? "West Gallery" : "East Gallery";
            float wallX = sx * o, outX = sx * (o + 2f);
            Add2(Solid.Block(wallX, outX, Deck - 0.2f, Deck, 0f, 18f, wood, g), true, false);
            foreach (float z in new[] { 0.1f, 3f, 6f, 9f, 12f, 15f, 17.9f })
            {
                Add2(Solid.Block(outX - sx * 0.15f, outX, low, Deck + 1.0f, z - 0.075f, z + 0.075f, dark, g), true, false);    // outer posts, up to the top rail
                Add2(Solid.Block(wallX + sx * 0.05f, wallX + sx * 0.2f, low, Deck - 0.2f, z - 0.075f, z + 0.075f, dark, g), true, false);
            }
            foreach (float r in new[] { 0.45f, 0.95f })
            {
                Add2(Solid.Block(outX - sx * 0.08f, outX, Deck + r - 0.05f, Deck + r + 0.05f, 0f, 18f, wood, g), true, false);          // outer edge
                Add2(Solid.Block(wallX, outX, Deck + r - 0.05f, Deck + r + 0.05f, 17.92f, 18f, wood, g), true, false);                 // north end
                Add2(Solid.Block(wallX, sx * (o + 0.8f), Deck + r - 0.05f, Deck + r + 0.05f, 0f, 0.08f, wood, g), true, false);        // south end, beside the stair
            }
            Add2(Solid.Block(sx * (o + 0.8f) - sx * 0.15f, sx * (o + 0.8f), Deck - 0.2f, Deck + 1.0f, 0f, 0.15f, dark, g), true, false);   // post beside the stair
            for (int s = 0; s < StairSteps; s++)
                Add2(Solid.Block(sx * (o + 0.8f), outX, low, Deck * (s + 1) / StairSteps, -0.3f * StairSteps + 0.3f * s, -0.3f * StairSteps + 0.3f * (s + 1), IsleKit.Stone, g), true, false);
        }
        {
            const string g = "North Gallery";
            Add2(Solid.Block(-9f, 9f, Deck - 0.2f, Deck, ShedZ1, ShedZ1 + 2f, wood, g), true, false);
            foreach (float x in new[] { -8.9f, -6f, -3f, 0f, 3f, 6f, 8.9f })
            {
                Add2(Solid.Block(x - 0.075f, x + 0.075f, low, Deck + 1.0f, ShedZ1 + 1.85f, ShedZ1 + 2f, dark, g), true, false);
                Add2(Solid.Block(x - 0.075f, x + 0.075f, low, Deck - 0.2f, ShedZ1 + 0.05f, ShedZ1 + 0.2f, dark, g), true, false);
            }
            foreach (float r in new[] { 0.45f, 0.95f })
            {
                Add2(Solid.Block(-9f, 9f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1 + 1.92f, ShedZ1 + 2f, wood, g), true, false);      // north edge
                Add2(Solid.Block(8.92f, 9f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1, ShedZ1 + 2f, wood, g), true, false);           // east end
                Add2(Solid.Block(-9f, -8.92f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1, 26.1f, wood, g), true, false);               // west end, beside the stair
            }
            Add2(Solid.Block(-9f, -8.85f, Deck - 0.2f, Deck + 1.0f, 25.95f, 26.1f, dark, g), true, false);
            for (int s = 0; s < StairSteps; s++)
                Add2(Solid.Block(-9f - 0.3f * StairSteps + 0.3f * s, -9f - 0.3f * StairSteps + 0.3f * (s + 1), low, Deck * (s + 1) / StairSteps, 26.1f, 27.3f, IsleKit.Stone, g), true, false);
        }

        return list;
    }

    // Where a sight line from outside enters the shed: true only through a window band (the glass is clear). The
    // roof, walls and ridge stop every other line.
    static bool EntersByOpening(Vector3 a, Vector3 b2)
    {
        Vector3 d = b2 - a;
        float tEnter = -1f; int axis = -1;
        float[] lo = { -ShedOut, -10f, ShedZ0 }, hi = { ShedOut, RoofTop, ShedZ1 };
        for (int k = 0; k < 3; k++)
        {
            float ak = Ax(a, k), dk = Ax(d, k);
            if (ak >= lo[k] && ak <= hi[k]) continue;                  // inside this slab already
            if (Mathf.Abs(dk) < 1e-9f) return false;
            float t = ((ak < lo[k]) ? lo[k] - ak : hi[k] - ak) / dk;
            if (t > tEnter) { tEnter = t; axis = k; }
        }
        if (axis < 0 || tEnter < 0f || tEnter > 1f) return false;
        Vector3 p = a + d * tEnter;
        if (axis == 0) return p.z >= WinZ0 && p.z <= WinZ1 && p.y >= Sill && p.y <= Head;
        if (axis == 2 && a.z > ShedZ1) return p.x >= -NWinX && p.x <= NWinX && p.y >= Sill && p.y <= Head;
        return false;
    }

    static bool Visible(Vector3 eye, Vector3 target, Solid[] obstacles)
    {
        if (!EntersByOpening(eye, target)) return false;
        Vector3 d = target - eye;
        foreach (Solid s in obstacles) if (s.Hits(eye, d)) return false;
        return true;
    }

    // The galleries' eye points: 3 across the deck, 7 along it, at the heights in EyeUp above it
    public static Vector3[,,] Eyes(int g)
    {
        var e = new Vector3[3, 7, EyeUp.Length];
        float[] up = EyeUp;
        for (int a = 0; a < 3; a++)
            for (int i = 0; i < 7; i++)
                for (int h = 0; h < up.Length; h++)
                {
                    float along = 0.5f + 17f * i / 6f, across = (a - 1) * 0.7f, y = Deck + up[h];
                    e[a, i, h] = g == Gw ? new Vector3(-17.3f + across, y, along) : g == Ge ? new Vector3(17.3f - across, y, along)
                        : new Vector3(-8.5f + 17f * i / 6f, y, 26.3f - across);
                }
        return e;
    }

    static Solid[] Pick(Layout layout, bool sureOnly)
    {
        var list = new List<Solid>();
        foreach (Solid s in layout.Solids) if (s.Check && (!sureOnly || s.Sure)) list.Add(s);
        return list.ToArray();
    }

    // Spacing: nothing of another stall, the fir or a landmark within 1.6 m of a lamp
    public static bool Spacing(Layout layout, out string why)
    {
        why = "";
        foreach (Stall s in layout.Stalls)
        {
            Vector3 lamp = s.Kind == Kind.P ? V(s.C, PavBulb) : s.Targets[0];
            foreach (Solid o in layout.Solids)
            {
                if (o.Owner == s.Id || o.Owner == -1 || !o.Sure) continue;      // not its own stall, the shell, or generous check volumes
                if (o.Distance(lamp) < 1.6f) { why = s.Name + "'s lamp is within 1.6 m of " + (o.Owner >= 0 ? layout.Stalls.Find(q => q.Id == o.Owner).Name : "a landmark"); return false; }
            }
        }
        return true;
    }

    // The galleries: every lamp seen from its own gallery (the disc, from the stand eye on the deck's centre line, at
    // three points along it or more), and no light of a closed stall's from any eye point of another gallery
    public static bool Galleries(Layout layout, out string why)
    {
        why = "";
        Solid[] all = Pick(layout, false), sure = Pick(layout, true);
        var eyes = new[] { Eyes(Gw), Eyes(Ge), Eyes(Gn) };
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                if (!Sees(g, s.Kind)) continue;
                int count = 0;
                for (int i = 0; i < 7; i++) if (Visible(eyes[g][1, i, Stand], s.Targets[0], all)) count++;
                if (count < 3) { why = s.Name + " is seen from only " + count + " of 7 standing points on the " + GalleryName(g) + " gallery"; return false; }
            }
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                if (s.Kind == Kind.P || Sees(g, s.Kind)) continue;
                foreach (Vector3 eye in eyes[g])
                    foreach (Vector3 t in s.Targets)
                        if (Visible(eye, t, sure)) { why = s.Name + "'s light is seen from the " + GalleryName(g) + " gallery at (" + eye.x.ToString("0.0") + ", " + eye.y.ToString("0.0") + ", " + eye.z.ToString("0.0") + ")"; return false; }
            }
        // for the table: eye points of each gallery that see each lamp's disc
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                s.Seen[g] = 0;
                foreach (Vector3 eye in eyes[g]) if (Visible(eye, s.Targets[0], all)) s.Seen[g]++;
            }
        return true;
    }

    // The exterior eye points: outside the shed's box and every sure solid, from H - 60 to H + 60, 0.6 to 40 m up;
    // 1 m apart (0.5 m in height) within 6 m of a window band, 3 m (2 m) beyond
    public static List<Vector3> ExteriorPoints(List<Solid> fixedSolids)
    {
        var sure = new List<Solid>();
        foreach (Solid s in fixedSolids) if (s.Check && s.Sure) sure.Add(s);
        var points = new List<Vector3>();
        bool Keep(Vector3 p)
        {
            if (p.x > -ShedOut && p.x < ShedOut && p.z > ShedZ0 && p.z < ShedZ1 && p.y < Ridge + 0.1f) return false;
            foreach (Solid s in sure) if (s.Contains(p)) return false;
            return true;
        }
        for (int i = 0; i <= 40; i++)
            for (int j = 0; j <= 40; j++)
                for (int k = 0; k <= 20; k++)
                {
                    var p = new Vector3(HX - 60f + 3f * i, k == 20 ? 40f : 0.6f + 2f * k, HZ - 60f + 3f * j);
                    if (Keep(p)) points.Add(p);
                }
        // near each opening: the window bands (side walls, north wall)
        void Near(Vector3 c0, Vector3 c1, Vector3 outward)
        {
            for (int u = 0; u <= 6; u++)
                for (float a = -6f; a <= 6f + Vector3.Distance(c0, c1) + 1e-3f; a += 1f)
                    for (float y = 0.6f; y <= Mathf.Max(c0.y, c1.y) + 6f + 1e-3f; y += 0.5f)
                    {
                        Vector3 along = (c1 - c0); along.y = 0f; along = along.normalized;
                        Vector3 p = new Vector3(c0.x, y, c0.z) + along * a + outward * (0.05f + u);
                        // the distance to the opening's rectangle
                        float len = new Vector2(c1.x - c0.x, c1.z - c0.z).magnitude, s = Mathf.Clamp(a, 0f, len);
                        float dy = y < Mathf.Min(c0.y, c1.y) ? Mathf.Min(c0.y, c1.y) - y : y > Mathf.Max(c0.y, c1.y) ? y - Mathf.Max(c0.y, c1.y) : 0f;
                        float dist = Mathf.Sqrt((a - s) * (a - s) + (0.05f + u) * (0.05f + u) + dy * dy);
                        if (dist <= 6f && Keep(p)) points.Add(p);
                    }
        }
        Near(new Vector3(-ShedOut, Sill, WinZ0), new Vector3(-ShedOut, Head, WinZ1), new Vector3(-1f, 0f, 0f));
        Near(new Vector3(ShedOut, Sill, WinZ0), new Vector3(ShedOut, Head, WinZ1), new Vector3(1f, 0f, 0f));
        Near(new Vector3(-NWinX, Sill, ShedZ1), new Vector3(NWinX, Head, ShedZ1), new Vector3(0f, 0f, 1f));
        return points;
    }

    // Kinds that no one place may see together: the closed stalls of two galleries (any two of W, E and N), or one
    // spot would show more than its own window's share. The pavilions go with any of them.
    static bool Conflict(int mask)
    {
        bool w = (mask & 1) != 0, e = (mask & 2) != 0, n = (mask & 4) != 0;
        return (w && e) || (n && (w || e));
    }

    // Outside the shed: no exterior point sees two galleries' stalls; counts per kind the points that see it
    public static bool Exterior(Layout layout, List<Vector3> points, out string why, out List<string> table)
    {
        why = "";
        table = new List<string>();
        Solid[] sure = Pick(layout, true);
        int[] perKind = new int[4];
        int conflicts = 0;
        foreach (Vector3 eye in points)
        {
            int mask = 0;
            foreach (Stall s in layout.Stalls)
            {
                int bit = 1 << (int)s.Kind;
                if ((mask & bit) != 0) continue;
                foreach (Vector3 t in s.Targets)
                    if (Visible(eye, t, sure)) { mask |= bit; break; }
            }
            for (int k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) perKind[k]++;
            if (Conflict(mask))
            {
                if (conflicts == 0) why = "the point (" + eye.x.ToString("0.0") + ", " + eye.y.ToString("0.0") + ", " + eye.z.ToString("0.0") + ") sees kinds " + MaskName(mask);
                conflicts++;
            }
        }
        table.Add("outside the shed: " + points.Count + " exterior points; seeing W " + perKind[0] + ", E " + perKind[1] + ", N " + perKind[2] + ", P " + perKind[3]
            + "; seeing two galleries' stalls: " + conflicts);
        return conflicts == 0;
    }

    static string MaskName(int mask)
    {
        string s = "";
        for (int k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) s += ((Kind)k).ToString();
        return s;
    }

    // ==== PURE END ====
}
