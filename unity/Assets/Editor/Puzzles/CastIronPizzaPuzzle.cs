// Cast Iron Pizza (a scenery pack): the user's own pizzeria, rebuilt on a terrace at the summit from photographs of
// the restaurant, simplified. The summit is a sharp peak, so the terrace is built out level from the summit's
// plateau over the south-west face on a foundation of stone, railed, with picnic tables, a Christmas tree and room for
// the disc golf (SummitDiscGolfPuzzle). The shop stands at its outer end, its front to the summit:
//  - outside: a brick front with a glass door (a wreath on it) under a transom numbered 1103B, a wide window with
//    the CASTIRON PIZZA skillet on the glass and a second window, a black and tan striped awning, a fascia sign;
//  - inside: burgundy walls over white beadboard, a dark plank floor, a black ceiling with glass pendant lamps, a
//    window bar on black pipe legs with two ladder-back stools, two black booths, a bookshelf by the door, two
//    paintings of black cats (one with pizza, one with red wine), and the order counter faced in reclaimed planks
//    with chalkboards ("Pizza Special", "Treats") and orange string lights along it;
//  - behind the counter: the kitchen, stainless and concrete with black mats: two stacked double deck ovens with red
//    control panels under a hood, a wire rack of countertop ovens over stacks of pizza boxes, a three-bowl sink, a
//    shelf of steel pans with copper pans hanging below, a red fire suppression canister; and the back hall of
//    exposed brick with a white door at its end.
// Players can sit at the bar and in the booths (the game's seats), and carry off the three skillet pizzas on the
// counter. The pictures (logo, signs, paintings, chalkboards, brick, planks) are drawn by tools/pizzeria_art.py into
// one atlas; the room is lit by BWM/Picture's warm room light, not the sky.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class CastIronPizzaPuzzle : PuzzleDef
{
    public override string Name => "CastIronPizza";
    public override int PuzzleType => 2;
    public override Vector2 Footprint => new Vector2(8f, 8f);
    public override Vector2? FixedSite => WinterIslePuzzle.Summit;

    // ---- the terrace's frame: u out from the summit along Bearing, v across (positive to the right looking out) ----
    public const float Bearing = 235f;
    public const float TerraceFrom = 10.5f, TerraceTo = 44f, TerraceHalf = 13f;
    public const float ShopFront = 28f, ShopDepth = 14f, ShopHalf = 8f;
    public static Vector2 Out => new Vector2(Mathf.Sin(Bearing * Mathf.Deg2Rad), Mathf.Cos(Bearing * Mathf.Deg2Rad));
    public static Vector2 Side => new Vector2(Out.y, -Out.x);
    public static Vector2 At(float u, float v) => WinterIslePuzzle.Summit + Out * u + Side * v;
    // The terrace's top is the summit's own level: this pack's root (and the disc golf's), at the summit

    // ---- colours from the photographs ----
    // (written as they look, then made linear: the game reads vertex colours as linear, so unconverted they washed out)
    static readonly Color Burgundy = new Color(0.55f, 0.15f, 0.16f).linear, Cream = new Color(0.93f, 0.91f, 0.86f).linear, Black = new Color(0.07f, 0.07f, 0.08f).linear;
    static readonly Color DarkWood = new Color(0.3f, 0.17f, 0.11f).linear, Brick = new Color(0.5f, 0.25f, 0.2f).linear;
    static readonly Color Steel = new Color(0.76f, 0.78f, 0.8f).linear, Concrete = new Color(0.52f, 0.52f, 0.52f).linear, Copper = new Color(0.72f, 0.45f, 0.26f).linear;
    static readonly Color StoneGrey = new Color(0.5f, 0.5f, 0.52f), Timber = new Color(0.42f, 0.29f, 0.18f), Glass = new Color(0.78f, 0.86f, 0.92f).linear;
    static readonly Color Leather = new Color(0.09f, 0.08f, 0.08f).linear, Paint = new Color(0.86f, 0.84f, 0.8f).linear;

    PuzzleBuilder b;
    Vector3 rootPos, o3, rgt, fwd;
    float shopYaw;

    // shop coordinates: x to the right looking out of the front door, y up from the floor, z forward (out of the door,
    // towards the summit); the front wall's outer face is z = 0 and the room runs back to z = -ShopDepth
    Vector3 S(float x, float y, float z) => o3 + rgt * x + Vector3.up * y + fwd * z - rootPos;
    void Box(MeshBuilder mb, float x, float y, float z, float w, float h, float d, Color c) => Kit.AddBox(mb, S(x, y, z), new Vector3(w, h, d), c, shopYaw);

    static Vector4 CellUV(int cell)
    {
        const float m = 4f / 2048f;
        float u0 = (cell % 4) * 0.25f, vTop = 1f - (cell / 4) * 0.25f;
        return new Vector4(u0 + m, vTop - 0.25f + m, u0 + 0.25f - m, vTop - m);
    }
    // A picture: the atlas cell's part from vFrom to vTo (0 its bottom, 1 its top), on a w x h quad centred at c (pack
    // local), its right and up along 'right' and 'up' (its face looks along cross(up, right))
    static void Pic(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 up, float w, float h, int cell, bool mirror = false, float vFrom = 0f, float vTo = 1f)
    {
        Vector4 r = CellUV(cell);
        float v0 = Mathf.Lerp(r.y, r.w, vFrom), v1 = Mathf.Lerp(r.y, r.w, vTo);
        float ua = mirror ? r.z : r.x, ub = mirror ? r.x : r.z;
        Vector3 hr = right * (w * 0.5f), hu = up * (h * 0.5f);
        mb.QuadUV(c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, new Vector2(ua, v0), new Vector2(ub, v0), new Vector2(ub, v1), new Vector2(ua, v1), Color.white, Color.white, Color.white, Color.white);
    }
    // a surface tiled with a cell: 'right' and 'up' as for Pic, tiles of tw x th
    static void Tiled(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 up, float w, float h, float tw, float th, int cell)
    {
        int nx = Mathf.Max(1, Mathf.RoundToInt(w / tw)), ny = Mathf.Max(1, Mathf.RoundToInt(h / th));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                Pic(mb, c + right * (-w * 0.5f + (i + 0.5f) * w / nx) + up * (-h * 0.5f + (j + 0.5f) * h / ny), right, up, w / nx, h / ny, cell);
    }

    void Seat(string name, float x, float y, float z, float facing)
    {
        var seat = new Piece(new GameObject(name));
        seat.Transform.SetParent(b.Root.transform, false);
        seat.Transform.position = S(x, y, z) + rootPos;
        seat.Transform.rotation = Quaternion.Euler(0f, shopYaw + facing, 0f);
        b.AddScript(seat, "PuzzleSeat");
        seat.Set("PuzzleSeat", "pose", 0);
        seat.Set("PuzzleSeat", "clickSize", new Vector3(0.6f, 0.6f, 0.6f));
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        rootPos = b.Root.transform.position;
        float deckY = rootPos.y;
        string atlasPath = "Assets/Textures/Pizzeria/CastIronAtlas.png";
        var importer = UnityEditor.AssetImporter.GetAtPath(atlasPath) as UnityEditor.TextureImporter;
        if (importer == null) throw new System.Exception("No " + atlasPath + ": run tools\\pizzeria_art.py");
        if (importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize < 2048) { importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048; importer.SaveAndReimport(); }
        var atlas = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Material inside = b.ShaderMaterial("BWM/Picture", "Pizzeria Inside", m => { m.SetTexture("_MainTex", atlas); m.SetFloat("_Indoor", 1f); m.SetColor("_RoomColor", new Color(1f, 0.93f, 0.84f)); m.SetFloat("_RoomLevel", 1.05f); });
        Material outside = b.ShaderMaterial("BWM/Picture", "Pizzeria Outside", m => { m.SetTexture("_MainTex", atlas); m.SetFloat("_Indoor", 0f); });
        Material glowMat = b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); });

        Vector2 front = At(ShopFront, 0f);
        o3 = new Vector3(front.x, deckY, front.y);
        fwd = new Vector3(-Out.x, 0f, -Out.y);
        rgt = Vector3.Cross(Vector3.up, fwd).normalized;
        shopYaw = Bearing + 180f;
        Vector3 up = Vector3.up;

        // ================= the terrace =================
        var terrace = new MeshBuilder();
        Vector3 T(float u, float v, float y) { Vector2 w = At(u, v); return new Vector3(w.x - rootPos.x, y - rootPos.y, w.y - rootPos.z); }
        float terraceYaw = Bearing;
        for (float u = TerraceFrom; u < TerraceTo - 0.01f; u += 2f)      // paving: a deck of stone flags in two greys
            for (float v = -TerraceHalf; v < TerraceHalf - 0.01f; v += 2f)
                Kit.AddBox(terrace, T(u + 1f, v + 1f, deckY - 0.6f), new Vector3(2.02f, 0.6f, 2.02f), StoneGrey * (((int)(u / 2f) + (int)((v + 20f) / 2f)) % 2 == 0 ? 1f : 0.92f), terraceYaw + 90f);
        // the foundation: piers of stone from the mountainside up under the deck, a wall round its outer edges
        for (float u = TerraceFrom; u < TerraceTo - 0.01f; u += 4f)
            for (float v = -TerraceHalf; v < TerraceHalf - 0.01f; v += 4f)
            {
                float low = float.MaxValue;
                foreach (var (du, dv) in new[] { (0f, 0f), (4f, 0f), (0f, 4f), (4f, 4f), (2f, 2f) })
                {
                    Vector2 q = At(u + du, v + dv);
                    if (b.Ground.At(q.x, q.y, out float gy)) low = Mathf.Min(low, gy);
                }
                if (low == float.MaxValue) low = deckY - 60f;
                float top = deckY - 0.6f, tall = top - (low - 1.5f);
                if (tall <= 0.2f) continue;
                if (tall < 7f)      // near the summit: a block of stone
                {
                    Kit.AddBox(terrace, T(u + 2f, v + 2f, low - 1.5f), new Vector3(4.05f, tall, 4.05f), StoneGrey * (0.78f + 0.08f * Mathf.PerlinNoise(u * 0.3f, v * 0.3f)), terraceYaw + 90f);
                    continue;
                }
                // further out: a timber stilt under the cell's corner, braced to its neighbours every 8 m
                Vector2 foot = At(u + 2f, v + 2f);
                float ground = b.Ground.At(foot.x, foot.y, out float gy2) ? gy2 : low;
                Kit.AddBox(terrace, T(u + 2f, v + 2f, ground - 1f), new Vector3(0.7f, top - ground + 1f, 0.7f), Timber * 0.85f, terraceYaw);
                for (float y = top - 1.5f; y > ground + 2f; y -= 8f)
                {
                    if (u + 4f < TerraceTo - 0.01f) Kit.AddBox(terrace, T(u + 4f, v + 2f, y), new Vector3(0.25f, 0.3f, 4f), Timber * 0.75f, terraceYaw);
                    if (v + 4f < TerraceHalf - 0.01f) Kit.AddBox(terrace, T(u + 2f, v + 4f, y), new Vector3(4f, 0.3f, 0.25f), Timber * 0.75f, terraceYaw);
                }
            }
        // a beam round the deck's edge, under the flags
        foreach (var (u0, v0, u1, v1) in new[] { (TerraceFrom, -TerraceHalf, TerraceTo, -TerraceHalf), (TerraceFrom, TerraceHalf, TerraceTo, TerraceHalf), (TerraceTo, -TerraceHalf, TerraceTo, TerraceHalf) })
        {
            Vector2 a = At(u0, v0), z = At(u1, v1);
            float beamYaw = Mathf.Atan2(z.x - a.x, z.y - a.y) * Mathf.Rad2Deg, len = (z - a).magnitude;
            Kit.AddBox(terrace, T((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, deckY - 1.2f), new Vector3(0.5f, 0.7f, len + 0.5f), Timber * 0.8f, beamYaw);
        }
        // railings round the three open sides (the plateau's side is left open), and lights along the top rail
        var rails = new List<(float u0, float v0, float u1, float v1)>
        {
            (TerraceFrom, -TerraceHalf, TerraceTo, -TerraceHalf), (TerraceFrom, TerraceHalf, TerraceTo, TerraceHalf), (TerraceTo, -TerraceHalf, TerraceTo, TerraceHalf),
        };
        int railLights = 0;
        foreach (var (u0, v0, u1, v1) in rails)
        {
            float len = Mathf.Max(Mathf.Abs(u1 - u0), Mathf.Abs(v1 - v0));
            int posts = Mathf.CeilToInt(len / 2f);
            var tops = new List<Vector3>();
            for (int k = 0; k <= posts; k++)
            {
                float t = (float)k / posts, u = Mathf.Lerp(u0, u1, t), v = Mathf.Lerp(v0, v1, t);
                Kit.AddBox(terrace, T(u, v, deckY), new Vector3(0.16f, 1.12f, 0.16f), Timber, terraceYaw);
                if (k % 2 == 0) tops.Add(T(u, v, deckY + 1.3f));
            }
            Vector2 a = At(u0, v0), z = At(u1, v1);
            float railYaw = Mathf.Atan2(z.x - a.x, z.y - a.y) * Mathf.Rad2Deg;
            foreach (float h in new[] { 0.5f, 1.05f })
                Kit.AddBox(terrace, T((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, deckY + h), new Vector3(0.08f, 0.1f, len), Timber * 0.9f, railYaw);
            if (tops.Count > 1) Kit.LightString(b, "Terrace Lights " + (++railLights), tops, 0.25f, 0.45f, false, false, 120 + railLights);
        }
        // picnic tables and a Christmas tree
        foreach (var (u, v) in new[] { (17f, -7f), (21f, -7.5f) })
        {
            Kit.AddBox(terrace, T(u, v, deckY + 0.72f), new Vector3(1.8f, 0.06f, 0.8f), Timber * 1.1f, terraceYaw);
            foreach (int s in new[] { -1, 1 })
            {
                Kit.AddBox(terrace, T(u, v + s * 0.75f, deckY), new Vector3(0.1f, 0.72f, 0.1f), Timber, terraceYaw);      // legs, along the table
                Kit.AddBox(terrace, T(u + s * 0.7f, v, deckY + 0.42f), new Vector3(1.8f, 0.05f, 0.3f), Timber, terraceYaw);      // benches, either side
                Kit.AddBox(terrace, T(u + s * 0.7f, v, deckY), new Vector3(0.08f, 0.42f, 0.08f), Timber, terraceYaw);
            }
        }
        b.MeshPiece("Pizzeria Terrace", terrace.ToMesh(), snowy, true);
        {
            var tree = new MeshBuilder();
            Kit.AddPine(tree, T(25.5f, 10.5f, deckY), 4.2f, 31);
            b.MeshPiece("Pizzeria Tree", tree.ToMesh(), snowy, true);
            var ring = new List<Vector3>();
            for (int k = 0; k <= 16; k++) { float a = k * 0.9f; float hgt = 0.6f + k * 0.22f, rr = 1.5f * (1f - k / 18f); ring.Add(T(25.5f, 10.5f, deckY + hgt) + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr)); }
            Kit.LightString(b, "Pizzeria Tree Lights", ring, 0.04f, 0.3f, false, false, 133);
        }

        // ================= the shop =================
        const float D = ShopDepth, W = ShopHalf, H = 4.4f;      // depth, half width, eaves
        var shell = new MeshBuilder();      // outer walls and roof (snowy)
        var facade = new MeshBuilder();     // the front's pictures outdoors
        var room = new MeshBuilder();       // everything inside, and the inner faces
        var glow = new MeshBuilder();
        var glassOut = new MeshBuilder();
        // the front wall, around its openings: door x -6.4..-5.1; logo window -4.2..-0.6; second window 0.8..4.4; sills 0.7, tops 2.85
        const float DoorL = -6.4f, DoorR = -5.1f, DoorTop = 2.35f, Win1L = -4.2f, Win1R = -0.6f, Win2L = 0.8f, Win2R = 4.4f, Sill = 0.7f, WinTop = 2.85f, TransomTop = 3.45f;
        void FrontPiece(float x0, float x1, float y0, float y1) { if (x1 - x0 > 0.01f && y1 - y0 > 0.01f) Box(shell, (x0 + x1) * 0.5f, y0, -0.15f, x1 - x0, y1 - y0, 0.3f, Brick); }
        FrontPiece(-W, DoorL, 0f, 5.6f);
        FrontPiece(DoorR, Win1L, 0f, TransomTop);
        FrontPiece(Win1R, Win2L, 0f, TransomTop);
        FrontPiece(Win2R, W, 0f, 5.6f);
        FrontPiece(DoorL, DoorR, DoorTop + 1.1f, TransomTop);       // over the door's transom
        FrontPiece(Win1L, Win1R, 0f, Sill);
        FrontPiece(Win2L, Win2R, 0f, Sill);
        FrontPiece(DoorR, Win2R, TransomTop, 5.6f);                 // the parapet over the windows
        FrontPiece(DoorL, DoorR, TransomTop, 5.6f);
        // the transom band over both windows: cream frames
        foreach (var (x0, x1) in new[] { (Win1L, Win1R), (Win2L, Win2R) })
        {
            Box(shell, (x0 + x1) * 0.5f, WinTop, -0.12f, x1 - x0, 0.1f, 0.24f, Cream);      // the transom bar
            for (float x = x0; x <= x1 + 0.01f; x += (x1 - x0) / 4f) Box(shell, x, WinTop + 0.1f, -0.12f, 0.06f, TransomTop - WinTop - 0.1f, 0.2f, Cream);
            foreach (float x in new[] { x0, x1 }) Box(shell, x, Sill, -0.12f, 0.1f, WinTop - Sill, 0.24f, Cream);      // the window's frame
            Box(shell, (x0 + x1) * 0.5f, Sill - 0.05f, -0.12f, x1 - x0 + 0.1f, 0.08f, 0.3f, Cream);
        }
        // side walls, back wall, roof with a low parapet, a vent stack
        Box(shell, -W + 0.15f, 0f, -D * 0.5f, 0.3f, 4.9f, D, Brick);
        Box(shell, W - 0.15f, 0f, -D * 0.5f, 0.3f, 4.9f, D, Brick);
        Box(shell, 0f, 0f, -D + 0.15f, 2f * W, 4.9f, 0.3f, Brick);
        Box(shell, 0f, H, -D * 0.5f, 2f * W, 0.3f, D, new Color(0.3f, 0.3f, 0.32f));
        Box(shell, 2.6f, H + 0.3f, -11f, 0.8f, 1.6f, 0.8f, Steel * 0.8f);       // the hood's vent
        // the brick front's face, outdoors (the lower part of the brick picture: no plaster outside)
        void BrickFace(float x0, float x1, float y0, float y1)
        {
            if (x1 - x0 < 0.05f || y1 - y0 < 0.05f) return;
            int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 1.4f)), ny = Mathf.Max(1, Mathf.RoundToInt((y1 - y0) / 1.0f));
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    float cx = x0 + (i + 0.5f) * (x1 - x0) / nx, cy = y0 + (j + 0.5f) * (y1 - y0) / ny;
                    Pic(facade, S(cx, cy, 0.006f), -rgt, up, (x1 - x0) / nx, (y1 - y0) / ny, 6, false, 0.05f, 0.62f);
                }
        }
        BrickFace(-W, DoorL, 0f, 5.6f); BrickFace(DoorR, Win1L, 0f, TransomTop); BrickFace(Win1R, Win2L, 0f, TransomTop); BrickFace(Win2R, W, 0f, 5.6f);
        BrickFace(Win1L, Win1R, 0f, Sill); BrickFace(Win2L, Win2R, 0f, Sill); BrickFace(DoorR, Win2R, TransomTop, 5.6f); BrickFace(DoorL, DoorR, DoorTop + 1.1f, 5.6f);
        // the fascia sign over the awning, the awning, the transom with the number, the windows' glass
        Pic(facade, S(-0.2f, 4.7f, 0.07f), -rgt, up, 3.4f, 1.6f, 11);
        Box(shell, -0.2f, 3.85f, 0.03f, 3.6f, 1.75f, 0.06f, Black);
        {
            // the awning: a sloped striped sheet from 3.65 m at the wall to 3.0 m, 1.5 m out, over the door and windows
            float x0 = DoorL - 0.3f, x1 = Win2R + 0.3f;
            Vector3 back = S((x0 + x1) * 0.5f, 3.65f, 0.05f), outEdge = S((x0 + x1) * 0.5f, 3.0f, 1.55f);
            Vector3 slope = (outEdge - back).normalized;
            Pic(facade, (back + outEdge) * 0.5f, -rgt, -slope, x1 - x0, (outEdge - back).magnitude, 14);      // the top
            Pic(facade, (back + outEdge) * 0.5f - Vector3.up * 0.01f, rgt, -slope, x1 - x0, (outEdge - back).magnitude, 14);      // its underside
            Pic(facade, S((x0 + x1) * 0.5f, 2.82f, 1.56f), -rgt, up, x1 - x0, 0.36f, 14);      // the valance
            foreach (float x in new[] { x0, x1 }) Box(shell, x, 2.8f, 1.5f, 0.05f, 0.25f, 0.05f, Black);
        }
        Pic(glassOut, S((DoorL + DoorR) * 0.5f, (DoorTop + DoorTop + 1.1f) * 0.5f, -0.1f), -rgt, up, DoorR - DoorL, 1.1f, 5);       // the transom, 1103B
        Pic(room, S((DoorL + DoorR) * 0.5f, (DoorTop + DoorTop + 1.1f) * 0.5f, -0.12f), rgt, up, DoorR - DoorL, 1.1f, 5, true);
        Pic(glassOut, S((Win1L + Win1R) * 0.5f, (Sill + WinTop) * 0.5f, -0.1f), -rgt, up, Win1R - Win1L, WinTop - Sill, 0);      // the logo window
        Pic(room, S((Win1L + Win1R) * 0.5f, (Sill + WinTop) * 0.5f, -0.12f), rgt, up, Win1R - Win1L, WinTop - Sill, 0, true);   // ...read backwards from inside
        foreach (var (x0, x1) in new[] { (Win1L, Win1R), (Win2L, Win2R) })
            Box(glassOut, (x0 + x1) * 0.5f, WinTop + 0.1f, -0.1f, x1 - x0, TransomTop - WinTop - 0.1f, 0.02f, Glass);      // the transoms' glass
        Box(glassOut, (Win2L + Win2R) * 0.5f, Sill, -0.1f, Win2R - Win2L, WinTop - Sill, 0.02f, Glass);      // the second window
        // the door, standing open into the room (hinged on its left), glass above with white blinds, and its wreath
        {
            float hingeX = DoorL + 0.05f;
            Box(room, hingeX + 0.03f, 0f, -0.75f, 0.05f, DoorTop - 0.05f, 1.2f, Cream);
            Box(room, hingeX + 0.06f, 1.0f, -0.75f, 0.02f, 1.2f, 0.95f, new Color(0.95f, 0.95f, 0.93f).linear);
            var wreath = new MeshBuilder();
            for (int k = 0; k < 14; k++) { float a = k * Mathf.PI * 2f / 14f; Kit.AddBox(wreath, S(hingeX + 0.1f, 1.6f + Mathf.Sin(a) * 0.32f, -0.75f + Mathf.Cos(a) * 0.32f), new Vector3(0.14f, 0.16f, 0.16f), IsleKit.Green, shopYaw); }
            Kit.AddBox(wreath, S(hingeX + 0.12f, 1.22f, -0.75f), new Vector3(0.1f, 0.14f, 0.18f), IsleKit.Red, shopYaw);
            b.MeshPiece("Pizzeria Wreath", wreath.ToMesh(), bare, false);
            foreach (float x in new[] { DoorL, DoorR }) Box(shell, x, 0f, -0.12f, 0.1f, DoorTop, 0.26f, Cream);
            Box(shell, (DoorL + DoorR) * 0.5f, DoorTop, -0.12f, DoorR - DoorL + 0.1f, 0.1f, 0.26f, Cream);
        }
        // lights along the parapet
        Kit.LightString(b, "Pizzeria Front Lights", new List<Vector3> { S(-W, 5.5f, 0.1f), S(-W * 0.33f, 5.5f, 0.1f), S(W * 0.33f, 5.5f, 0.1f), S(W, 5.5f, 0.1f) }, 0.3f, 0.4f, false, false, 141);

        // ---- the dining room: z from -0.3 back to -7 ----
        const float RoomBack = -7.2f;
        Tiled(room, S(0f, 0.01f, (RoomBack - 0.3f) * 0.5f), rgt, fwd, 2f * W - 0.6f, -RoomBack - 0.3f, 2f, 2f, 15);      // hardwood
        Box(room, 0f, 4.0f, -D * 0.5f, 2f * W - 0.6f, 0.05f, D - 0.6f, Black);      // the black ceiling
        // the walls inside: burgundy over beadboard with a white rail, on both sides and the front's inner face
        void Wainscot(Vector3 centreAt, Vector3 right, float length)
        {
            Tiled(room, centreAt + Vector3.up * 0.5f, right, up, length, 1.0f, 1.0f, 1.0f, 13);
        }
        Box(room, -W + 0.32f, 0f, (RoomBack - 0.3f) * 0.5f, 0.02f, 4f, -RoomBack - 0.3f, Burgundy);      // left wall paint
        Box(room, W - 0.32f, 0f, (RoomBack - 0.3f) * 0.5f, 0.02f, 4f, -RoomBack - 0.3f, Burgundy);       // right wall paint
        Wainscot(S(-W + 0.34f, 0f, (RoomBack - 0.3f) * 0.5f), fwd, -RoomBack - 0.3f);
        Wainscot(S(W - 0.34f, 0f, (RoomBack - 0.3f) * 0.5f), -fwd, -RoomBack - 0.3f);
        Box(room, -W + 0.36f, 1.0f, (RoomBack - 0.3f) * 0.5f, 0.05f, 0.08f, -RoomBack - 0.3f, Cream);
        Box(room, W - 0.36f, 1.0f, (RoomBack - 0.3f) * 0.5f, 0.05f, 0.08f, -RoomBack - 0.3f, Cream);
        // the front wall's inner face, burgundy, round the openings
        foreach (var (x0, x1, y0, y1) in new[] { (-W + 0.3f, DoorL, 0f, 4f), (DoorR, Win1L, 0f, 4f), (Win1R, Win2L, 0f, 4f), (Win2R, W - 0.3f, 0f, 4f), (Win1L, Win1R, 0f, Sill), (Win2L, Win2R, 0f, Sill), (DoorL, DoorR, DoorTop + 1.1f, 4f), (Win1L, Win2R, TransomTop, 4f) })
            if (x1 - x0 > 0.01f && y1 - y0 > 0.01f) Box(room, (x0 + x1) * 0.5f, y0, -0.31f, x1 - x0, y1 - y0, 0.02f, Burgundy);
        // pendant lamps on cords
        foreach (var (x, z) in new[] { (-2.4f, -1.4f), (2.6f, -1.4f), (5.6f, -3.6f), (-3.5f, -4.8f) })
        {
            Box(room, x, 2.9f, z, 0.02f, 1.1f, 0.02f, Black);
            Kit.AddGlow(glow, S(x, 2.72f, z), new Vector3(0.22f, 0.36f, 0.22f), shopYaw, x > 0f && z < -3f ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.6f), x * 0.1f);
        }
        // the window bar on black pipe legs, and two ladder-back stools at it
        {
            float x0 = Win1L + 0.1f, x1 = Win1R - 0.1f, z = -0.55f;
            Box(room, (x0 + x1) * 0.5f, 1.05f, z, x1 - x0, 0.05f, 0.35f, DarkWood);
            foreach (float x in new[] { x0 + 0.1f, x1 - 0.1f }) Box(room, x, 0f, z - 0.05f, 0.05f, 1.05f, 0.05f, Black);
            Box(room, (x0 + x1) * 0.5f, 0.3f, z - 0.05f, x1 - x0 - 0.2f, 0.04f, 0.04f, Black);
            int stool = 0;
            foreach (float x in new[] { x0 + 0.9f, x1 - 0.9f })
            {
                float sz = z - 0.75f;
                Box(room, x, 0.74f, sz, 0.44f, 0.08f, 0.44f, new Color(0.25f, 0.24f, 0.24f).linear);      // seat
                foreach (var (dx, dz) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) }) Box(room, x + dx, 0f, sz + dz, 0.035f, 0.74f, 0.035f, Black);
                Box(room, x, 0.25f, sz, 0.4f, 0.03f, 0.4f, Black);      // the foot ring
                Box(room, x, 0.82f, sz - 0.21f, 0.4f, 0.03f, 0.03f, Black);      // ladder back
                foreach (float dx in new[] { -0.19f, 0.19f }) Box(room, x + dx, 0.82f, sz - 0.21f, 0.03f, 0.6f, 0.03f, Black);
                for (int k = 0; k < 4; k++) Box(room, x, 0.98f + k * 0.12f, sz - 0.21f, 0.4f, 0.05f, 0.025f, Black);
                Seat("Bar Stool " + (++stool), x, 0.86f, sz, 0f);
            }
        }
        // the booths along the right wall: black benches with high backs either side of a dark table
        int booth = 0;
        foreach (float zc in new[] { -2.0f, -4.6f })
        {
            float x = W - 1.25f;
            Box(room, x, 0.72f, zc, 0.9f, 0.05f, 1.3f, DarkWood);
            Box(room, x, 0f, zc, 0.12f, 0.72f, 0.12f, Black);
            foreach (int s in new[] { -1, 1 })
            {
                float bz = zc + s * 1.05f;
                Box(room, x - 0.15f, 0f, bz, 1.6f, 0.45f, 0.55f, Leather);
                Box(room, x - 0.15f, 0.45f, bz + s * 0.25f, 1.6f, 0.8f, 0.12f, Leather);
                Box(room, x - 0.15f, 1.25f, bz + s * 0.25f, 1.65f, 0.06f, 0.18f, DarkWood);
                for (int k = 0; k < 2; k++) Seat("Booth Seat " + (++booth), x - 0.55f + k * 0.75f, 0.5f, bz - s * 0.05f, s > 0 ? 180f : 0f);
            }
        }
        // the bookshelf by the door, with a poinsettia
        {
            float x = -W + 0.55f, z = -1.7f;
            Box(room, x, 0f, z, 0.45f, 2.3f, 1.0f, DarkWood);
            for (int k = 0; k < 5; k++) Box(room, x + 0.05f, 0.3f + k * 0.45f, z, 0.4f, 0.03f, 0.92f, DarkWood * 1.3f);
            for (int k = 0; k < 6; k++) Box(room, x + 0.05f, 0.75f + (k % 3) * 0.45f, z - 0.3f + (k / 3) * 0.5f, 0.25f, 0.3f, 0.12f, new Color(0.3f + 0.1f * k, 0.25f, 0.2f + 0.05f * k).linear);
            for (int k = 0; k < 7; k++) { float a = k * 0.9f; Box(room, x + Mathf.Cos(a) * 0.12f, 2.35f, z + 0.3f + Mathf.Sin(a) * 0.12f, 0.16f, 0.06f, 0.16f, k % 2 == 0 ? IsleKit.Red.linear : new Color(0.85f, 0.5f, 0.2f).linear); }
        }
        // the paintings of black cats on the left wall
        foreach (var (z, cell) in new[] { (-3.6f, 1), (-5.2f, 2) })
        {
            Box(room, -W + 0.37f, 1.55f, z, 0.04f, 0.82f, 0.68f, Black);
            Pic(room, S(-W + 0.4f, 1.96f, z), fwd, up, 0.64f, 0.78f, cell);
        }

        // ---- the counter, faced in reclaimed planks, with chalkboards, string lights, the till and a cake dome ----
        {
            float x0 = -2.6f, x1 = 5.4f, z = RoomBack;
            Box(room, (x0 + x1) * 0.5f, 0f, z - 0.35f, x1 - x0, 1.0f, 0.7f, DarkWood);
            Tiled(room, S((x0 + x1) * 0.5f, 0.5f, z + 0.01f), -rgt, up, x1 - x0, 1.0f, 2.0f, 1.0f, 7);
            Box(room, (x0 + x1) * 0.5f, 1.0f, z - 0.32f, x1 - x0 + 0.1f, 0.07f, 0.8f, DarkWood * 1.2f);
            Box(room, x1 - 2.2f, 1.07f, z - 0.55f, 2.2f, 0.2f, 0.35f, DarkWood);      // the raised back of its right half
            Box(room, -1.4f, 1.07f, z - 0.4f, 0.4f, 0.04f, 0.3f, Black); Box(room, -1.4f, 1.11f, z - 0.45f, 0.36f, 0.3f, 0.04f, Black);      // the till
            Box(room, 0.6f, 1.07f, z - 0.35f, 0.34f, 0.32f, 0.34f, new Color(0.8f, 0.88f, 0.9f).linear);     // the cake dome
            foreach (var (x, cell, frame) in new[] { (3.6f, 3, new Color(0.38f, 0.26f, 0.16f)), (4.8f, 4, new Color(0.48f, 0.55f, 0.57f)) })
            {
                Box(room, x, 1.27f, z - 0.5f, 0.9f, 0.85f, 0.06f, frame);
                Pic(room, S(x, 1.7f, z - 0.465f), -rgt, up, 0.84f, 0.8f, cell);
            }
            for (int k = 0; k <= 20; k++)      // orange string lights hanging along the counter's front
            {
                float t = k / 20f, x = Mathf.Lerp(x0, x1, t), y = 0.95f - Mathf.Sin(t * Mathf.PI * 4f) * 0.08f;
                Kit.AddGlow(glow, S(x, y, z + 0.04f), new Vector3(0.05f, 0.08f, 0.05f), shopYaw, new Color(1f, 0.42f, 0.1f), t * 3f);
            }
        }
        // the back hall on the left: exposed brick on its wall, the white door at its end, an exit sign, a tube light
        {
            float xL = -W + 0.32f, xR = -3.4f;
            float hall = D + RoomBack - 0.3f;      // its length, from the dining room's back to the back wall
            Box(room, (xL + xR) * 0.5f, 0.005f, (RoomBack - D) * 0.5f, xR - xL, 0.01f, hall, Concrete * 1.1f);      // the hall's floor
            Tiled(room, S(xL + 0.01f, 2.0f, (RoomBack - D + 0.3f) * 0.5f), fwd, up, hall, 4.0f, 1.4f, 1.33f, 6);
            Box(room, xR, 0f, (RoomBack - D) * 0.5f, 0.12f, 4f, hall, Cream);     // the hall's other wall
            Box(room, (xL + xR) * 0.5f, 0f, -D + 0.31f, xR - xL, 4f, 0.02f, Paint);      // the hall's end wall
            Box(room, (xL + xR) * 0.5f, 0f, -D + 0.34f, 1.0f, 2.1f, 0.05f, new Color(0.75f, 0.8f, 0.86f).linear);      // the white door
            Box(room, (xL + xR) * 0.5f, 2.1f, -D + 0.33f, 1.15f, 0.1f, 0.07f, Cream);
            Kit.AddGlow(glow, S((xL + xR) * 0.5f, 2.6f, -D + 0.4f), new Vector3(0.35f, 0.15f, 0.06f), shopYaw, new Color(1f, 0.1f, 0.08f), 0.5f);
            Kit.AddGlow(glow, S((xL + xR) * 0.5f, 3.92f, (RoomBack - D) * 0.5f), new Vector3(0.12f, 0.05f, 2.4f), shopYaw, new Color(0.95f, 0.97f, 1f), 0.1f);
        }

        // ---- the kitchen: z from -7.2 back to -14, x from -3.4 to the right wall ----
        {
            float kx0 = -3.35f, kx1 = W - 0.32f, kz0 = RoomBack - 0.7f, kz1 = -D + 0.32f;
            Box(room, (kx0 + kx1) * 0.5f, 0.005f, (kz0 + kz1) * 0.5f, kx1 - kx0, 0.01f, kz0 - kz1, Concrete);
            foreach (var (x, z, w, d) in new[] { (0.2f, -9.6f, 1.2f, 2.4f), (3.2f, -9.4f, 1.0f, 1.8f) }) Box(room, x, 0.012f, z, w, 0.012f, d, new Color(0.06f, 0.06f, 0.06f).linear);      // rubber mats
            Box(room, (kx0 + kx1) * 0.5f, 0f, kz1 - 0.01f, kx1 - kx0, 4f, 0.02f, Steel);      // stainless back wall
            Box(room, kx1 - 0.01f, 0f, (RoomBack + kz1) * 0.5f, 0.02f, 4f, RoomBack - kz1, Paint);      // the right wall, painted
            // the stacked deck ovens on their stand, the hood over them, the red canister
            float ox = 0.4f, oz = kz1 + 0.6f;
            Box(room, ox, 0f, oz, 1.5f, 0.8f, 1.0f, Steel * 0.85f);
            for (int k = 0; k < 4; k++)
            {
                float y = 0.8f + k * 0.44f;
                Box(room, ox, y, oz, 1.5f, 0.42f, 1.0f, Steel);
                Pic(room, S(ox, y + 0.21f, oz + 0.505f), -rgt, up, 1.5f, 0.42f, 8);
            }
            Box(room, ox, 2.65f, oz + 0.1f, 2.6f, 0.9f, 1.6f, Steel * 1.05f);      // the hood
            Kit.AddDisc(room, S(ox - 0.9f, 2.0f, oz - 0.3f), 0.17f, 0.75f, IsleKit.Red.linear, 12);      // the fire suppression canister
            // the three-bowl sink
            Box(room, -2.1f, 0f, kz1 + 0.4f, 2.2f, 0.9f, 0.7f, Steel * 0.9f);
            for (int k = 0; k < 3; k++) Box(room, -2.8f + k * 0.7f, 0.88f, kz1 + 0.42f, 0.6f, 0.03f, 0.55f, Steel * 0.6f);
            Box(room, -2.1f, 0.9f, kz1 + 0.1f, 2.2f, 0.35f, 0.05f, Steel);
            Box(room, -2.1f, 1.25f, kz1 + 0.15f, 0.06f, 0.25f, 0.06f, Steel * 1.1f);      // the tap
            // the pot shelf: stacked steel pans on it, copper pans hanging below
            float px0 = 2.0f, px1 = 4.6f;
            Box(room, (px0 + px1) * 0.5f, 2.1f, kz1 + 0.18f, px1 - px0, 0.04f, 0.35f, Steel);
            for (int k = 0; k < 5; k++) Box(room, px0 + 0.3f + k * 0.5f, 2.14f, kz1 + 0.2f, 0.3f, 0.12f + (k % 3) * 0.14f, 0.28f, Steel * 1.08f);
            for (int k = 0; k < 4; k++)
            {
                float x = px0 + 0.6f + k * 0.42f;
                Box(room, x, 1.55f, kz1 + 0.06f, 0.03f, 0.5f, 0.03f, Steel);      // the handle
                Box(room, x, 1.2f - (k % 2) * 0.08f, kz1 + 0.07f, 0.34f, 0.34f, 0.05f, Copper * (0.9f + 0.1f * k));
            }
            // the wire rack of countertop ovens by the right wall, pizza boxes stacked under it
            float rx = kx1 - 0.4f, rz = -10.6f;
            foreach (var (dx, dz) in new[] { (-0.3f, -1.1f), (0.3f, -1.1f), (-0.3f, 1.1f), (0.3f, 1.1f) }) Box(room, rx + dx, 0f, rz + dz, 0.03f, 2.1f, 0.03f, Steel * 1.1f);
            foreach (float y in new[] { 0.35f, 1.05f, 1.7f, 2.05f }) Box(room, rx, y, rz, 0.62f, 0.03f, 2.24f, Steel * 1.15f);
            foreach (var (y, z) in new[] { (1.08f, -0.55f), (1.08f, 0.55f), (1.73f, -0.55f), (1.73f, 0.55f) })
            {
                Box(room, rx, y, rz + z, 0.55f, 0.32f, 1.0f, new Color(0.2f, 0.2f, 0.22f).linear);
                Pic(room, S(rx - 0.28f, y + 0.16f, rz + z), -fwd, up, 1.0f, 0.32f, 9);
            }
            for (int k = 0; k < 3; k++)
                for (int n = 0; n < 6; n++)
                {
                    float y = 0.02f + n * 0.05f, z = rz - 0.7f + k * 0.7f;
                    Box(room, rx, y, z, 0.42f, 0.05f, 0.42f, new Color(0.77f, 0.63f, 0.44f).linear);
                    if (n == 5) Pic(room, S(rx, y + 0.052f, z), rgt, fwd, 0.42f, 0.42f, 10);
                }
            // a stainless prep table with a pizza peel
            Box(room, 2.8f, 0f, -9.4f, 1.6f, 0.9f, 0.8f, Steel * 0.9f);
            Box(room, 2.6f, 0.9f, -9.4f, 0.45f, 0.02f, 0.45f, new Color(0.75f, 0.6f, 0.4f).linear);
            Box(room, 3.1f, 0.9f, -9.4f, 0.6f, 0.02f, 0.06f, new Color(0.7f, 0.55f, 0.35f).linear);
        }
        // the room, for Winter Walk: an empty object whose position, turn and size are the room's box; no snow falls
        // for a camera inside it
        var indoors = new GameObject("Indoors Pizzeria");
        indoors.transform.SetParent(b.Root.transform, false);
        indoors.transform.position = S(0f, 2.1f, -D * 0.5f) + rootPos;
        indoors.transform.rotation = Quaternion.Euler(0f, shopYaw, 0f);
        indoors.transform.localScale = new Vector3(2f * W, 4.4f, D);
        b.MeshPiece("Pizzeria Shell", shell.ToMesh(), snowy, true);
        b.MeshPiece("Pizzeria Front", facade.ToMesh(), outside, false);
        b.MeshPiece("Pizzeria Glass", glassOut.ToMesh(), outside, true);
        b.MeshPiece("Pizzeria Room", room.ToMesh(), inside, true);
        b.MeshPiece("Pizzeria Lamps", glow.ToMesh(), glowMat, false);

        // ---- three skillet pizzas on the counter, for carrying off ----
        for (int k = 0; k < 3; k++)
        {
            var mb = new MeshBuilder();
            Kit.AddDisc(mb, Vector3.zero, 0.17f, 0.045f, Black * 1.6f, 18);
            Kit.AddBox(mb, new Vector3(0.24f, 0.015f, 0f), new Vector3(0.18f, 0.03f, 0.04f), Black * 1.6f);
            Kit.AddDisc(mb, new Vector3(0f, 0.045f, 0f), 0.145f, 0.012f, new Color(0.95f, 0.72f, 0.38f), 18);
            for (int p = 0; p < 7; p++) { float a = p * 0.9f + k, r = 0.04f + (p % 3) * 0.03f; Kit.AddDisc(mb, new Vector3(Mathf.Cos(a) * r, 0.057f, Mathf.Sin(a) * r), 0.02f, 0.004f, new Color(0.62f, 0.12f, 0.08f), 8); }
            Vector3 at = S(-0.6f + k * 0.55f, 1.12f, RoomBack - 0.3f);
            b.Item("Skillet Pizza " + (k + 1), mb.ToMesh(), bare, at.x, at.z, at.y, 1.5f, false);
        }
        Debug.Log("[BWM] Cast Iron Pizza: on a terrace " + Sdk.F(TerraceTo - TerraceFrom) + " m out from the summit at bearing " + Bearing + ", its floor at " + Sdk.F(deckY) + " m");
    }
}
