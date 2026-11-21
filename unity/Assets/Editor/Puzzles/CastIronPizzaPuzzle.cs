// Cast Iron Pizza (a scenery pack): the user's own pizzeria, rebuilt on a terrace at the summit from photographs of
// the restaurant, at about its real proportions (a narrow storefront: one door, two windows; the counter some 7 m in).
// The summit is a sharp peak, so the terrace is built out level from the summit's plateau over the south-west face:
// a stone bastion near the summit, a braced timber trestle further out. On it: a swept path to the door, a paved patio
// with cafe tables under umbrellas and festoon lights, patio heaters, a decorated tree with presents, a snowman,
// planters, lamp posts, and room for the disc golf (SummitDiscGolfPuzzle) on the other side.
//  - The front: brick with a cream storefront (pilasters, kickplates, a black lintel), the door standing open with a
//    fir wreath and bells, a frosted transom numbered 1103B, two windows of real glass with the logo on the first in
//    dark vinyl (backwards from inside, as in the photos), a black and tan awning, the logo on a black fascia board
//    under three gooseneck lamps, a round blade sign, a bronze cornice with a garland and lights, downpipes.
//  - Inside: burgundy over white beadboard, chair rail and skirting, a narrow oak floor with a runner from the door, a
//    black ceiling with black joists and a spiral duct, amber and red teardrop pendants. The window bar on iron pipe
//    with two swivel stools; two black vinyl booths; two small tables; the built-in bookshelf with books, T-shirts and
//    a poinsettia; the two cat canvases; a wine shelf, a clock, the chalkboard menu; a drinks cooler; a high chair.
//  - The counter: a raised display end with the two chalkboards, the cake dome and a lantern, a lower till end with the
//    skillet pizzas (players can carry them off), orange string lights, a black soffit over it.
//  - Behind it: the cook line on a stainless partition (warming cabinet, range under a hood, sauces, hand sink); the back
//    kitchen (the stacked deck ovens with their red panels and the fire suppression cylinder, a pan shelf with pans
//    hanging, two- and three-bowl sinks, a wire rack of glowing countertop ovens over stacks of boxes, a speed rack, a
//    prep table); and the back hall of exposed brick under a ragged plaster line, with a pale door at its end.
// Light: three zones (dining warm, kitchen and hall cool) each with its own material and renderer, a low room level with
// shading baked into the vertex colours (dark at the floor's edges, under furniture and towards the ceiling), and real
// lamps that Winter Walk's RoomLights hangs at the "Room Light" markers. Pictures and surfaces come from
// tools/pizzeria_art.py (one atlas with a normal map and gloss, CastIronAtlas.png/.json, CastIronNormals.png); the logo
// is tools/pizzeria_logo.py's decal of the user's own logo, centred on its lettering.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    public const float ShopFront = 28f, ShopDepth = 14f, ShopHalf = 4.5f;
    public static Vector2 Out => new Vector2(Mathf.Sin(Bearing * Mathf.Deg2Rad), Mathf.Cos(Bearing * Mathf.Deg2Rad));
    public static Vector2 Side => new Vector2(Out.y, -Out.x);
    public static Vector2 At(float u, float v) => WinterIslePuzzle.Summit + Out * u + Side * v;
    // In the shop's own frame (below) u = ShopFront - z and v = -x: the terrace runs z +17.5 (the summit's edge) to -16,
    // x -13..13. Its top is the summit's level, this pack's root.

    // ---- colours, written as they look (sRGB 0..255) and made linear: the game reads vertex colours as linear ----
    static Color B(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f).linear;
    static readonly Color White = Color.white;
    static readonly Color Black = B(20, 20, 22), Iron = B(30, 30, 32), Cream = B(225, 215, 190), Gold = B(200, 170, 90);
    static readonly Color Walnut = B(65, 40, 30), Espresso = B(60, 45, 40), Bronze = B(45, 40, 36), Granite = B(122, 120, 116);
    static readonly Color Red = B(175, 35, 35), Green = B(35, 95, 55), Fir = B(25, 70, 40), Tan = B(190, 165, 115);
    static readonly Color Chrome = B(185, 186, 188), Steel = B(176, 178, 179), Timber = B(85, 60, 40);
    static readonly Color Amber = new Color(1f, 0.72f, 0.38f), Warm = new Color(1f, 0.83f, 0.55f), Cool = new Color(0.92f, 0.96f, 1f);

    // ---- the shop: x to the right looking out of the door, y up from the floor, z forward (out, to the summit) ----
    const float W = ShopHalf, IX = 4.2f, D = ShopDepth, IZ = -13.7f, CD = 3.7f, CK = 3.1f, H = 4.4f;
    const float DoorL = -2.9f, DoorR = -1.9f, DoorTop = 2.15f, TranTop = 3.15f, Lintel = 3.25f;
    const float WinAL = -1.7f, WinAR = 0.3f, WinBL = 0.9f, WinBR = 2.9f, Sill = 0.9f, Head = 2.45f, TrTop = 3.2f;
    const float CounterZ = -7.3f, CounterBack = -8.0f, PartZ = -10.4f, PartEnd = -0.6f, HallX = 2.9f;

    PuzzleBuilder b;
    Vector3 rootPos, o3, rgt, fwd, up = Vector3.up;
    float shopYaw;
    Dictionary<string, Vector4> atlas;
    readonly Dictionary<string, Vector2> metres = new Dictionary<string, Vector2>();
    readonly HashSet<string> missing = new HashSet<string>();
    int lightMarkers, seats;
    float logoAspect = 1.25f;
    Material snowy, bare;
    // what is drawn: the outside (sunlit pictures), structure that takes snow, the three rooms, glows, glass, the logo
    readonly MeshBuilder face = new MeshBuilder(), shell = new MeshBuilder(), dine = new MeshBuilder(), kit = new MeshBuilder(), hall = new MeshBuilder();
    readonly MeshBuilder glow = new MeshBuilder(), bulbs = new MeshBuilder(), glass = new MeshBuilder(), ink = new MeshBuilder(), deck = new MeshBuilder();

    Vector3 S(float x, float y, float z) => o3 + rgt * x + up * y + fwd * z - rootPos;
    Vector3 Dir(float x, float y, float z) => rgt * x + up * y + fwd * z;
    Vector3 ToShop(Vector3 local) { Vector3 w = local + rootPos - o3; return new Vector3(Vector3.Dot(w, rgt), w.y, Vector3.Dot(w, fwd)); }
    Vector3 ToShopDir(Vector3 n) => new Vector3(Vector3.Dot(n, rgt), n.y, Vector3.Dot(n, fwd));
    void Box(MeshBuilder mb, float x, float y, float z, float w, float h, float d, Color c) => Kit.AddBox(mb, S(x, y, z), new Vector3(w, h, d), c, shopYaw);
    static Color Scale(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

    // ================= the atlas =================
    static float Num(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    void LoadAtlas(string path)
    {
        atlas = new Dictionary<string, Vector4>();
        string text = File.ReadAllText(path);
        Match namesBlock = Regex.Match(text, "\"names\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
        int rectsAt = text.IndexOf("\"rects\""), tileAt = text.IndexOf("\"tile\""), metresAt = text.IndexOf("\"metres\"");
        if (!namesBlock.Success || rectsAt < 0) throw new System.Exception(path + " has no names or rects");
        var names = new List<string>();
        foreach (Match m in Regex.Matches(namesBlock.Groups[1].Value, "\"([^\"]*)\"")) names.Add(m.Groups[1].Value);
        string n = "\\s*([-+0-9.eE]+)\\s*";
        string rects = text.Substring(rectsAt, (tileAt > rectsAt ? tileAt : text.Length) - rectsAt);
        int i = 0;
        foreach (Match m in Regex.Matches(rects, "\\[" + n + "," + n + "," + n + "," + n + "\\]"))
            if (i < names.Count) atlas[names[i++]] = new Vector4(Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value));
        if (metresAt >= 0)
        {
            i = 0;
            foreach (Match m in Regex.Matches(text.Substring(metresAt), "\\[" + n + "," + n + "\\]"))
                if (i < names.Count) metres[names[i++]] = new Vector2(Num(m.Groups[1].Value), Num(m.Groups[2].Value));
        }
    }
    Vector4 R(string name)
    {
        if (atlas.TryGetValue(name, out Vector4 r)) return r;
        missing.Add(name);
        return atlas.TryGetValue("white", out Vector4 w) ? w : new Vector4(0.001f, 0.001f, 0.01f, 0.01f);
    }
    float Mt(string name) => metres.TryGetValue(name, out Vector2 m) && m.x > 0f ? m.x : 1f;      // one repeat, metres

    // ================= drawing helpers =================
    // Shading baked into vertex colours, for the rooms' floors, walls and ceilings (when 'shading' is on)
    bool shading;
    readonly List<Vector4> walls = new List<Vector4>();      // (x0, z0, x1, z1): wall faces, for the floor's dark edges
    readonly List<Vector4> blobs = new List<Vector4>();      // (x, z, rx, rz): the floor darkens under furniture
    readonly List<Vector2> pools = new List<Vector2>();      // the floor brightens under lamps
    static float SegmentDistance(float x, float z, Vector4 s)
    {
        float dx = s.z - s.x, dz = s.w - s.y, len = dx * dx + dz * dz;
        float t = len > 1e-6f ? Mathf.Clamp01(((x - s.x) * dx + (z - s.y) * dz) / len) : 0f;
        float px = s.x + dx * t - x, pz = s.y + dz * t - z;
        return Mathf.Sqrt(px * px + pz * pz);
    }
    float WallDistance(float x, float z, float skipWithin)
    {
        float best = 99f;
        foreach (Vector4 s in walls)
        {
            float d = SegmentDistance(x, z, s);
            if (skipWithin > 0f && d < skipWithin) continue;
            if (d < best) best = d;
        }
        return best;
    }
    float Occlusion(Vector3 p, Vector3 n)
    {
        float ceil = p.z > CounterZ ? CD : CK, f = 1f;
        if (n.y > 0.7f)
        {
            f *= 1f - 0.38f * Mathf.Exp(-WallDistance(p.x, p.z, -1f) / 0.28f);
            foreach (Vector4 o in blobs) { float dx = (p.x - o.x) / o.z, dz = (p.z - o.y) / o.w; f *= 1f - 0.5f * Mathf.Exp(-2f * (dx * dx + dz * dz)); }
            foreach (Vector2 l in pools) { float dx = p.x - l.x, dz = p.z - l.y; f *= 1f + 0.14f * Mathf.Exp(-(dx * dx + dz * dz) / 1.44f); }
        }
        else if (n.y < -0.7f) f *= 0.85f * (1f - 0.25f * Mathf.Exp(-WallDistance(p.x, p.z, -1f) / 0.3f));
        else
        {
            f *= Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0f, 1f, p.y / 1.4f));
            f *= Mathf.Lerp(1f, 0.66f, Mathf.SmoothStep(0f, 1f, (p.y - 2.2f) / Mathf.Max(0.3f, ceil - 2.2f)));
            f *= 1f - 0.2f * Mathf.Exp(-WallDistance(p.x, p.z, 0.03f) / 0.3f);
        }
        return f;
    }
    Color Shade(Color c, Vector3 at, Vector3 normal) => shading ? Scale(c, Occlusion(ToShop(at), ToShopDir(normal))) : c;

    // A picture: part of a surface (u0f..u1f, v0f..v1f of it) on a w x h quad centred at c (pack local), its right and
    // up along 'right' and 'upv'; the face is seen from cross(upv, right)
    void Pic(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 upv, float w, float h, string name, Color? tint = null, bool mirror = false, float u0f = 0f, float u1f = 1f, float v0f = 0f, float v1f = 1f)
    {
        Vector4 r = R(name);
        float ua = Mathf.Lerp(r.x, r.z, mirror ? 1f - u0f : u0f), ub = Mathf.Lerp(r.x, r.z, mirror ? 1f - u1f : u1f), va = Mathf.Lerp(r.y, r.w, v0f), vb = Mathf.Lerp(r.y, r.w, v1f);
        Vector3 hr = right.normalized * (w * 0.5f), hu = upv.normalized * (h * 0.5f);
        Color col = tint ?? White;
        Vector3 nrm = Vector3.Cross(upv, right).normalized;
        Vector3 a = c - hr - hu, bb = c + hr - hu, cc = c + hr + hu, d = c - hr + hu;
        mb.QuadUV(a, bb, cc, d, new Vector2(ua, va), new Vector2(ub, va), new Vector2(ub, vb), new Vector2(ua, vb), Shade(col, a, nrm), Shade(col, bb, nrm), Shade(col, cc, nrm), Shade(col, d, nrm));
    }
    // a surface tiled with a material, one repeat tw x th metres (0: the surface's own size from the atlas); split finer
    // when shading, so the shading has corners to live on
    void Tiled(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 upv, float w, float h, string name, Color? tint = null, float tw = 0f, float th = 0f)
    {
        if (w < 0.005f || h < 0.005f) return;
        if (tw <= 0f) tw = Mt(name);
        if (th <= 0f) th = metres.TryGetValue(name, out Vector2 m) && m.y > 0f ? m.y : tw;
        right = right.normalized; upv = upv.normalized;
        int nx = Mathf.Max(1, Mathf.CeilToInt(w / tw - 0.01f)), ny = Mathf.Max(1, Mathf.CeilToInt(h / th - 0.01f));
        Vector3 corner = c - right * (w * 0.5f) - upv * (h * 0.5f);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x0 = i * tw, x1 = Mathf.Min(w, (i + 1) * tw), y0 = j * th, y1 = Mathf.Min(h, (j + 1) * th);
                int sx = shading ? Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 0.5f - 0.01f)) : 1, sy = shading ? Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / 0.5f - 0.01f)) : 1;
                for (int a = 0; a < sx; a++)
                    for (int k = 0; k < sy; k++)
                    {
                        float ax = x0 + (x1 - x0) * a / sx, bx = x0 + (x1 - x0) * (a + 1) / sx, ay = y0 + (y1 - y0) * k / sy, by = y0 + (y1 - y0) * (k + 1) / sy;
                        Vector3 mid = corner + right * ((ax + bx) * 0.5f) + upv * ((ay + by) * 0.5f);
                        Pic(mb, mid, right, upv, bx - ax, by - ay, name, tint, false, (ax - x0) / tw, (bx - x0) / tw, (ay - y0) / th, (by - y0) / th);
                    }
            }
    }
    // A box in shop coordinates (foot = the middle of its bottom), its sides tiled with 'side' and its top with 'top'
    void TBox(MeshBuilder mb, float x, float y, float z, float w, float h, float d, string side, Color? tint = null, string top = null, float tile = 0f, bool bottom = false)
    {
        string tp = top ?? side;
        float t = tile > 0f ? tile : Mt(side), tt = tile > 0f ? tile : Mt(tp);
        Tiled(mb, S(x, y + h * 0.5f, z + d * 0.5f), -rgt, up, w, h, side, tint, t, t);
        Tiled(mb, S(x, y + h * 0.5f, z - d * 0.5f), rgt, up, w, h, side, tint, t, t);
        Tiled(mb, S(x - w * 0.5f, y + h * 0.5f, z), -fwd, up, d, h, side, tint, t, t);
        Tiled(mb, S(x + w * 0.5f, y + h * 0.5f, z), fwd, up, d, h, side, tint, t, t);
        Tiled(mb, S(x, y + h, z), rgt, fwd, w, d, tp, tint, tt, tt);
        if (bottom) Tiled(mb, S(x, y, z), rgt, -fwd, w, d, tp, tint, tt, tt);
    }
    // a disc facing 'normal', its picture's up along 'upv' (the face is seen from 'normal', as Pic's)
    void Disc(MeshBuilder mb, Vector3 c, Vector3 normal, Vector3 upv, float radius, string name, Color? tint = null, int seg = 20)
    {
        Vector4 r = R(name);
        Vector3 right = Vector3.Cross(normal, upv).normalized;
        upv = Vector3.Cross(right, normal).normalized;
        Vector2 uc = new Vector2((r.x + r.z) * 0.5f, (r.y + r.w) * 0.5f), hs = new Vector2((r.z - r.x) * 0.5f, (r.w - r.y) * 0.5f);
        Color col = tint ?? White;
        for (int k = 0; k < seg; k++)
        {
            float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
            Vector3 p0 = c + (right * Mathf.Cos(a0) + upv * Mathf.Sin(a0)) * radius, p1 = c + (right * Mathf.Cos(a1) + upv * Mathf.Sin(a1)) * radius;
            Vector2 t0 = uc + new Vector2(Mathf.Cos(a0) * hs.x, Mathf.Sin(a0) * hs.y), t1 = uc + new Vector2(Mathf.Cos(a1) * hs.x, Mathf.Sin(a1) * hs.y);
            mb.QuadUV(c, p0, p1, c, uc, t0, t1, uc, col, col, col, col);
        }
    }
    // a solid of revolution about a vertical axis from 'foot' (pack local): radii rs at heights ys, wrapped once in 'name';
    // 'capTop' closes the last ring with a disc
    void Lathe(MeshBuilder mb, Vector3 foot, float[] rs, float[] ys, string name, Color? tint = null, int seg = 12, bool capTop = false, string topName = null)
    {
        Vector4 r = R(name);
        Color col = tint ?? White;
        for (int i = 0; i + 1 < rs.Length; i++)
            for (int k = 0; k < seg; k++)
            {
                float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 p00 = foot + d0 * rs[i] + up * ys[i], p10 = foot + d1 * rs[i] + up * ys[i], p11 = foot + d1 * rs[i + 1] + up * ys[i + 1], p01 = foot + d0 * rs[i + 1] + up * ys[i + 1];
                float u0 = Mathf.Lerp(r.x, r.z, (float)k / seg), u1 = Mathf.Lerp(r.x, r.z, (float)(k + 1) / seg);
                float v0 = Mathf.Lerp(r.y, r.w, (float)i / (rs.Length - 1)), v1 = Mathf.Lerp(r.y, r.w, (float)(i + 1) / (rs.Length - 1));
                mb.QuadUV(p00, p10, p11, p01, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), col, col, col, col);
            }
        if (capTop && rs[rs.Length - 1] > 0.001f) Disc(mb, foot + up * ys[ys.Length - 1], up, fwd, rs[rs.Length - 1], topName ?? name, tint, seg);
    }
    void Cyl(MeshBuilder mb, Vector3 foot, float radius, float height, string side, string top, Color? tint = null, int seg = 12)
        => Lathe(mb, foot, new[] { radius, radius }, new[] { 0f, height }, side, tint, seg, top != null, top);
    // a cylinder from a to b (any direction)
    void CylAlong(MeshBuilder mb, Vector3 a, Vector3 bEnd, float radius, string name, Color? tint = null, int seg = 10)
    {
        Vector3 axis = (bEnd - a).normalized;
        Vector3 p1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, p2 = Vector3.Cross(p1, axis);
        Vector4 r = R(name);
        Color col = tint ?? White;
        for (int k = 0; k < seg; k++)
        {
            float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
            Vector3 r0 = (p1 * Mathf.Cos(a0) + p2 * Mathf.Sin(a0)) * radius, r1 = (p1 * Mathf.Cos(a1) + p2 * Mathf.Sin(a1)) * radius;
            float u0 = Mathf.Lerp(r.x, r.z, (float)k / seg), u1 = Mathf.Lerp(r.x, r.z, (float)(k + 1) / seg);
            mb.QuadUV(a + r0, a + r1, bEnd + r1, bEnd + r0, new Vector2(u0, r.y), new Vector2(u1, r.y), new Vector2(u1, r.w), new Vector2(u0, r.w), col, col, col, col);
        }
    }
    // a quad (or a triangle, with c2 = c3) seen from 'outward'
    static void QuadOut(MeshBuilder mb, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 outward, Color col)
    {
        if (Vector3.Dot(Vector3.Cross(c3 - c0, c1 - c0), outward) >= 0f) mb.Quad(c0, c1, c2, c3, col);
        else mb.Quad(c0, c3, c2, c1, col);
    }
    // the same, sampling one point of a picture (a stripe of the awning's canvas)
    static void TexQuadOut(MeshBuilder mb, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector2 uv, Color col, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(c3 - c0, c1 - c0), outward) >= 0f) mb.QuadUV(c0, c1, c2, c3, uv, uv, uv, uv, col, col, col, col);
        else mb.QuadUV(c0, c3, c2, c1, uv, uv, uv, uv, col, col, col, col);
    }
    // a square-section bar from a to b, w x d (struts, splayed legs, braces, boards); 'across' turns its width that way
    static void Strut(MeshBuilder mb, Vector3 a, Vector3 bEnd, float w, float d, Color col, Vector3? across = null)
    {
        Vector3 axis = (bEnd - a).normalized;
        Vector3 p1 = across.HasValue ? (across.Value - axis * Vector3.Dot(across.Value, axis)).normalized
            : Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 p2 = Vector3.Cross(p1, axis) * (d * 0.5f);
        p1 *= w * 0.5f;
        Vector3[] s = { p1 + p2, -p1 + p2, -p1 - p2, p1 - p2 };
        Vector3 mid = (a + bEnd) * 0.5f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 q0 = a + s[i], q1 = a + s[(i + 1) % 4], q2 = bEnd + s[(i + 1) % 4], q3 = bEnd + s[i];
            QuadOut(mb, q0, q1, q2, q3, (q0 + q2) * 0.5f - mid, col);
        }
        QuadOut(mb, a + s[0], a + s[1], a + s[2], a + s[3], -axis, col);
        QuadOut(mb, bEnd + s[0], bEnd + s[1], bEnd + s[2], bEnd + s[3], axis, col);
    }
    // a ring of bars round 'centre' in the plane square to 'normal' (stool foot rings, chair backs, rims)
    static void Ring(MeshBuilder mb, Vector3 centre, Vector3 normal, float radius, float thick, Color col, int seg = 14)
    {
        Vector3 p1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized, p2 = Vector3.Cross(p1, normal.normalized);
        for (int k = 0; k < seg; k++)
        {
            float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
            Strut(mb, centre + (p1 * Mathf.Cos(a0) + p2 * Mathf.Sin(a0)) * radius, centre + (p1 * Mathf.Cos(a1) + p2 * Mathf.Sin(a1)) * radius, thick, thick, col);
        }
    }
    // the logo's ink (tools/pizzeria_logo.py), h tall, centred at c, read along 'right'; seen from cross(upv, right).
    // 'mirror' flips it (the same ink's other side)
    void Logo(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 upv, float h, Color tint, bool mirror = false)
    {
        float w = h * logoAspect, u0 = mirror ? 1f : 0f, u1 = mirror ? 0f : 1f;
        Vector3 hr = right.normalized * (w * 0.5f), hu = upv.normalized * (h * 0.5f);
        mb.QuadUV(c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f), tint, tint, tint, tint);
    }
    void Pane(float x, float y, float z, float w, float h) => Pic(glass, S(x, y, z), -rgt, up, w, h, "white");
    void Glow(MeshBuilder mb, float x, float y, float z, float w, float h, float d, Color c, float phase) => Kit.AddGlow(mb, S(x, y, z), new Vector3(w, h, d), shopYaw, c, phase);
    void GlowAt(MeshBuilder mb, Vector3 at, float w, float h, float d, Color c, float phase) => Kit.AddGlow(mb, at, new Vector3(w, h, d), shopYaw, c, phase);
    void Seat(string name, Vector3 at, float yaw)      // pack-local position, world yaw
    {
        var seat = new Piece(new GameObject(name + " " + (++seats)));
        seat.Transform.SetParent(b.Root.transform, false);
        seat.Transform.position = at + rootPos;
        seat.Transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        b.AddScript(seat, "PuzzleSeat");
        seat.Set("PuzzleSeat", "pose", 0);
        seat.Set("PuzzleSeat", "clickSize", new Vector3(0.6f, 0.6f, 0.6f));
    }
    // a marker for Winter Walk's RoomLights: colour (sRGB hex), range, intensity
    void Lamp(Vector3 at, string hex, float range, float intensity)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var go = new GameObject("Room Light " + (++lightMarkers) + " " + hex + " " + range.ToString("0.0#", inv) + " " + intensity.ToString("0.0#", inv));
        go.transform.SetParent(b.Root.transform, false);
        go.transform.position = at + rootPos;
    }

    // ================= build =================
    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        rootPos = b.Root.transform.position;
        string dir = "Assets/Textures/Pizzeria/";
        var importer = UnityEditor.AssetImporter.GetAtPath(dir + "CastIronAtlas.png") as UnityEditor.TextureImporter;
        var nimporter = UnityEditor.AssetImporter.GetAtPath(dir + "CastIronNormals.png") as UnityEditor.TextureImporter;
        var limporter = UnityEditor.AssetImporter.GetAtPath(dir + "CastIronLogoInk.png") as UnityEditor.TextureImporter;
        if (importer == null || nimporter == null || !File.Exists(dir + "CastIronAtlas.json")) throw new System.Exception("No pizzeria atlas in " + dir + ": run tools\\pizzeria_art.py");
        if (limporter == null) throw new System.Exception("No logo decal in " + dir + ": run tools\\pizzeria_logo.py");
        if (importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize < 4096 || importer.alphaIsTransparency || !importer.mipmapEnabled)
        { importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 4096; importer.alphaIsTransparency = false; importer.mipmapEnabled = true; importer.SaveAndReimport(); }
        if (nimporter.textureType != UnityEditor.TextureImporterType.NormalMap || nimporter.maxTextureSize < 4096 || nimporter.wrapMode != TextureWrapMode.Clamp)
        { nimporter.textureType = UnityEditor.TextureImporterType.NormalMap; nimporter.maxTextureSize = 4096; nimporter.wrapMode = TextureWrapMode.Clamp; nimporter.SaveAndReimport(); }
        if (!limporter.alphaIsTransparency || limporter.wrapMode != TextureWrapMode.Clamp || limporter.maxTextureSize < 2048)
        { limporter.alphaIsTransparency = true; limporter.wrapMode = TextureWrapMode.Clamp; limporter.maxTextureSize = 2048; limporter.SaveAndReimport(); }
        var colours = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "CastIronAtlas.png");
        var normals = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "CastIronNormals.png");
        var logo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "CastIronLogoInk.png");
        logoAspect = (float)logo.width / logo.height;
        LoadAtlas(dir + "CastIronAtlas.json");

        snowy = IsleKit.Snowy(b); bare = IsleKit.Bare(b);
        System.Func<string, Color, float, Material> room = (name, light, level) => b.ShaderMaterial("BWM/Picture", name, m =>
        {
            m.SetTexture("_MainTex", colours); m.SetTexture("_NormalMap", normals); m.SetFloat("_Indoor", 1f);
            m.SetColor("_RoomColor", light); m.SetFloat("_RoomLevel", level); m.SetVector("_RoomDir", new Vector4(0.2f, 1f, 0.35f, 0f));
        });
        Material dining = room("Pizzeria Dining", new Color(1f, 0.87f, 0.72f), 0.6f);
        Material kitchen = room("Pizzeria Kitchen", new Color(0.95f, 0.97f, 1f), 0.78f);
        Material hallway = room("Pizzeria Hall", new Color(0.95f, 0.97f, 1f), 0.62f);
        Material outside = b.ShaderMaterial("BWM/Picture", "Pizzeria Outside", m => { m.SetTexture("_MainTex", colours); m.SetTexture("_NormalMap", normals); m.SetFloat("_Indoor", 0f); });
        Material glowMat = b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.1f); m.SetFloat("_Glow", 1.8f); m.SetFloat("_Speed", 3.1f); });
        Material bulbMat = b.ShaderMaterial("BWM/FairyLight", "Pizzeria Bulbs", m => { m.SetFloat("_Twinkle", 0.04f); m.SetFloat("_Glow", 1.2f); m.SetFloat("_Speed", 2f); });
        System.Action<Material> clear = m =>
        {
            m.SetFloat("_AlphaMode", 1f); m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Indoor", 0f); m.renderQueue = 3000;
        };
        Material glassMat = b.ShaderMaterial("BWM/Picture", "Pizzeria Glass", m => { clear(m); m.SetFloat("_Cull", 0f); m.SetColor("_Tint", new Color(0.62f, 0.72f, 0.76f, 0.14f)); m.SetFloat("_Gloss", 0.95f); m.SetFloat("_Shine", 1.4f); });
        Material inkMat = b.ShaderMaterial("BWM/Picture", "Pizzeria Logo Ink", m => { clear(m); m.SetTexture("_MainTex", logo); m.renderQueue = 3001; m.SetFloat("_Gloss", 0.5f); m.SetFloat("_Shine", 0.6f); });

        Vector2 front = At(ShopFront, 0f);
        o3 = new Vector3(front.x, rootPos.y, front.y);
        fwd = new Vector3(-Out.x, 0f, -Out.y);
        rgt = Vector3.Cross(Vector3.up, fwd).normalized;
        shopYaw = Bearing + 180f;

        Rooms();
        Outside();
        Dining();
        Counter();
        Kitchen();
        BackHall();
        Terrace();

        var indoors = new GameObject("Indoors Pizzeria");      // for Winter Walk: where it does not snow
        indoors.transform.SetParent(b.Root.transform, false);
        indoors.transform.position = S(0f, 2.2f, -D * 0.5f) + rootPos;
        indoors.transform.rotation = Quaternion.Euler(0f, shopYaw, 0f);
        indoors.transform.localScale = new Vector3(2f * W, 4.4f, D);

        b.MeshPiece("Pizzeria Shell", shell.ToMesh(), snowy, true);
        b.MeshPiece("Pizzeria Front", face.ToMesh(true), outside, true);
        b.MeshPiece("Pizzeria Paving", deck.ToMesh(true), outside, false);
        b.MeshPiece("Pizzeria Dining", dine.ToMesh(true), dining, true);
        b.MeshPiece("Pizzeria Kitchen", kit.ToMesh(true), kitchen, true);
        b.MeshPiece("Pizzeria Hall", hall.ToMesh(true), hallway, true);
        b.MeshPiece("Pizzeria Lamps", glow.ToMesh(), glowMat, false);
        b.MeshPiece("Pizzeria Bulbs", bulbs.ToMesh(), bulbMat, false);
        Piece panes = b.MeshPiece("Pizzeria Glass", glass.ToMesh(), glassMat, true);
        Piece vinyl = b.MeshPiece("Pizzeria Logo", ink.ToMesh(), inkMat, false);
        foreach (Piece p in new[] { panes, vinyl }) p.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SkilletPizzas();
        if (missing.Count > 0) Debug.LogWarning("[BWM] Cast Iron Pizza: the atlas has no " + string.Join(", ", missing));
        Debug.Log("[BWM] Cast Iron Pizza: " + Sdk.F(2f * W) + " x " + Sdk.F(D) + " m on a terrace " + Sdk.F(TerraceTo - TerraceFrom) + " m out from the summit at bearing " + Bearing + ", floor at " + Sdk.F(rootPos.y) + " m; " + lightMarkers + " lamps, " + seats + " seats");
    }

    // ================= the shell: walls, floors and ceilings of the three rooms =================
    void Rooms()
    {
        walls.AddRange(new[]
        {
            new Vector4(-IX, -0.3f, IX, -0.3f), new Vector4(-IX, -0.3f, -IX, IZ), new Vector4(IX, -0.3f, IX, IZ), new Vector4(-IX, IZ, IX, IZ),
            new Vector4(-IX, CounterZ, 0.2f, CounterZ), new Vector4(1.0f, CounterZ, HallX, CounterZ), new Vector4(HallX, CounterZ, HallX, IZ),
            new Vector4(-IX, PartZ, PartEnd, PartZ), new Vector4(-IX, CounterBack, 0.2f, CounterBack),
        });
        blobs.AddRange(new[]
        {
            new Vector4(-0.7f, -0.62f, 1.0f, 0.3f), new Vector4(3.6f, -1.85f, 0.7f, 1.2f), new Vector4(3.6f, -4.35f, 0.7f, 1.2f), new Vector4(1.2f, -2.6f, 0.45f, 0.45f),
            new Vector4(1.2f, -4.9f, 0.45f, 0.45f), new Vector4(-4.0f, -1.5f, 0.3f, 0.7f), new Vector4(3.9f, -6.25f, 0.4f, 0.45f), new Vector4(-3.8f, -10.0f, 0.4f, 0.45f),
            new Vector4(-2.9f, -10.0f, 0.5f, 0.45f), new Vector4(-3.3f, -13.3f, 0.7f, 0.45f), new Vector4(-1.95f, -13.4f, 0.7f, 0.35f), new Vector4(0.4f, -13.4f, 0.7f, 0.4f),
            new Vector4(0.8f, -11.8f, 0.85f, 0.45f), new Vector4(-3.9f, -11.8f, 0.35f, 1.1f), new Vector4(-2.0f, CounterZ + 0.15f, 2.4f, 0.25f),
        });
        pools.AddRange(new[] { new Vector2(-0.7f, -0.6f), new Vector2(3.6f, -1.85f), new Vector2(3.6f, -4.35f), new Vector2(-3.0f, -6.9f), new Vector2(-1.0f, -6.9f), new Vector2(0f, -12f) });

        shading = true;
        // dining: a narrow oak floor, beadboard, chair rail and burgundy walls under a black ceiling
        Tiled(dine, S(0f, 0.004f, (-0.3f + CounterZ) * 0.5f), rgt, fwd, 2f * IX, -0.3f - CounterZ, "floor_hardwood");
        Tiled(dine, S(0f, CD, (-0.3f + CounterZ) * 0.5f), rgt, -fwd, 2f * IX, -0.3f - CounterZ, "ceiling_black");
        DiningWall(S(-IX, 0f, (-0.3f + CounterZ) * 0.5f), fwd, -0.3f - CounterZ, CD);
        DiningWall(S(IX, 0f, (-0.3f + CounterZ) * 0.5f), -fwd, -0.3f - CounterZ, CD);
        foreach (var (x0, x1) in new[] { (-IX, DoorL - 0.1f), (DoorR + 0.1f, WinAL - 0.1f), (WinAR + 0.1f, WinBL - 0.1f), (WinBR + 0.1f, IX) })
            if (x1 - x0 > 0.05f) DiningWall(S((x0 + x1) * 0.5f, 0f, -0.3f), rgt, x1 - x0, Lintel + 0.1f);
        Tiled(dine, S(0f, (Lintel + 0.1f + CD) * 0.5f, -0.3f), rgt, up, 2f * IX, CD - Lintel - 0.1f, "plaster_burgundy");
        // kitchen: concrete under a pale ceiling, stainless by the cook line, cream elsewhere
        Color creamWall = B(232, 222, 205), greyWall = B(195, 190, 180);
        float kw = IX + HallX - 0.05f, kx = (-IX + HallX - 0.05f) * 0.5f;
        Tiled(kit, S(kx, 0.004f, (CounterZ + IZ) * 0.5f), rgt, fwd, kw, CounterZ - IZ, "concrete");
        Tiled(kit, S(kx, CK, (CounterBack + IZ) * 0.5f), rgt, -fwd, kw, CounterBack - IZ, "white", B(196, 196, 194));
        Tiled(kit, S(-IX, CK * 0.5f, (CounterZ + PartZ) * 0.5f), fwd, up, CounterZ - PartZ, CK, "stainless");
        Tiled(kit, S(-IX, CK * 0.5f, (PartZ + IZ) * 0.5f), fwd, up, PartZ - IZ, CK, "white", creamWall);
        Tiled(kit, S(kx, CK * 0.5f, IZ), -rgt, up, kw, CK, "white", creamWall);
        Tiled(kit, S(HallX - 0.05f, CK * 0.5f, (CounterZ + IZ) * 0.5f), -fwd, up, CounterZ - IZ, CK, "white", creamWall);
        Tiled(kit, S((1.0f + HallX - 0.05f) * 0.5f, 1.375f, CounterZ - 0.105f), rgt, up, HallX - 0.05f - 1.0f, 2.75f, "white", creamWall);      // behind the red partition
        // the partition the cook line stands on: stainless in front, grey behind with a white skirting, a stainless end
        Tiled(kit, S((-IX + PartEnd) * 0.5f, CK * 0.5f, PartZ + 0.05f), -rgt, up, PartEnd + IX, CK, "stainless");
        Tiled(kit, S((-IX + PartEnd) * 0.5f, CK * 0.5f, PartZ - 0.05f), rgt, up, PartEnd + IX, CK, "white", greyWall);
        shading = false;
        Tiled(kit, S(PartEnd, CK * 0.5f, PartZ), fwd, up, 0.1f, CK, "stainless");
        TBox(kit, (-IX + PartEnd) * 0.5f, 0f, PartZ - 0.06f, PartEnd + IX, 0.12f, 0.02f, "trim_white", B(235, 230, 215));
        // the hall: concrete, khaki on the partition side, brick on the outer wall under a ragged plaster line, grey at its end
        shading = true;
        float hx = (HallX + 0.05f + IX) * 0.5f, hw = IX - HallX - 0.05f;
        Tiled(hall, S(hx, 0.004f, (CounterZ + IZ) * 0.5f), rgt, fwd, hw, CounterZ - IZ, "concrete", B(205, 200, 192));
        Tiled(hall, S(hx, CK, (CounterBack + IZ) * 0.5f), rgt, -fwd, hw, CounterBack - IZ, "white", B(196, 196, 194));
        Tiled(hall, S(HallX + 0.05f, CK * 0.5f, (CounterZ + IZ) * 0.5f), fwd, up, CounterZ - IZ, CK, "white", B(175, 160, 135));
        {
            float tw = 1.2f, th = tw * 2f * 0.52f, len = CounterZ - IZ;      // the cell is 1.2 x 2.4 m; its lower half is brick
            for (float a = 0f; a < len - 0.01f; a += tw)
                for (float y = 0f; y < CK - 0.01f; y += th)
                {
                    float w = Mathf.Min(tw, len - a), h = Mathf.Min(th, CK - y);
                    Pic(hall, S(IX, y + h * 0.5f, CounterZ - a - w * 0.5f), -fwd, up, w, h, "brick_hall", null, false, 0f, w / tw, 0f, 0.52f * h / th);
                }
        }
        Tiled(hall, S(hx, CK * 0.5f, IZ), -rgt, up, hw, CK, "white", B(205, 205, 205));
        shading = false;
        {
            // the plaster above the brick: one ragged line wandering between 1.8 and 2.5 m
            Color plaster = B(213, 210, 206);
            Vector4 r = R("white");
            Vector2 uv = new Vector2((r.x + r.z) * 0.5f, (r.y + r.w) * 0.5f);
            float len = CounterZ - IZ;
            int n = 40;
            for (int k = 0; k < n; k++)
            {
                float z0 = CounterZ - len * k / n, z1 = CounterZ - len * (k + 1) / n;
                Vector3 a = S(IX - 0.006f, Ragged(z0), z0), bq = S(IX - 0.006f, Ragged(z1), z1), c = S(IX - 0.006f, CK, z1), d = S(IX - 0.006f, CK, z0);
                Color lo = Scale(plaster, 0.92f), hi = Scale(plaster, 0.7f);
                hall.QuadUV(a, bq, c, d, uv, uv, uv, uv, lo, lo, hi, hi);
            }
        }
        // aluminium strips where the oak stops at the counter's line
        Box(hall, (HallX + IX) * 0.5f, 0f, CounterZ, IX - HallX, 0.012f, 0.04f, B(170, 170, 172));
        Box(kit, 0.6f, 0f, CounterZ, 0.8f, 0.012f, 0.04f, B(170, 170, 172));
        // the soffit along the counter's line, and the black wall above it up to the dining ceiling, with grilles
        TBox(dine, 0f, 2.75f, (CounterZ + CounterBack) * 0.5f, 2f * IX, 0.55f, CounterZ - CounterBack, "white", B(37, 38, 44), null, 0f, true);
        Tiled(dine, S(0f, (3.3f + CD) * 0.5f, CounterZ), -rgt, up, 2f * IX, CD - 3.3f, "white", B(28, 28, 30));
        foreach (float x in new[] { -2.6f, -1.0f }) Pic(dine, S(x, 3.5f, CounterZ + 0.006f), -rgt, up, 0.55f, 0.28f, "stainless_baffle", B(170, 170, 170));
        // the red partition beside the staff gap (to the hall's mouth), with two certificates; the hall wall's front edge
        TBox(dine, (1.0f + HallX) * 0.5f, 0f, CounterZ - 0.05f, HallX - 1.0f, 2.75f, 0.1f, "plaster_burgundy");
        foreach (float x in new[] { 1.55f, 2.3f })
        {
            Box(dine, x, 1.44f, CounterZ, 0.3f, 0.37f, 0.02f, Black);
            Pic(dine, S(x, 1.625f, CounterZ + 0.011f), -rgt, up, 0.25f, 0.32f, "white", B(240, 238, 230));
        }
        TBox(dine, HallX + 0.025f, 0f, CounterZ - 0.05f, 0.05f, 2.75f, 0.1f, "white", B(175, 160, 135));
    }
    static float Ragged(float z) => 2.15f + 0.22f * Mathf.Sin(z * 1.7f + 0.4f) + 0.12f * Mathf.Sin(z * 4.9f + 1.3f) + 0.06f * Mathf.Sin(z * 13.1f);

    // a dining wall: beadboard to 1.05, a cream chair rail, burgundy above, a cream skirting; 'along' runs to the right
    // looking at it, its foot's middle at 'foot'
    void DiningWall(Vector3 foot, Vector3 along, float length, float top)
    {
        Vector3 normal = Vector3.Cross(up, along).normalized;
        Tiled(dine, foot + up * 0.525f, along, up, length, 1.05f, "beadboard", B(226, 223, 214));
        Tiled(dine, foot + up * ((1.05f + top) * 0.5f), along, up, length, top - 1.05f, "plaster_burgundy");
        bool keep = shading;
        shading = false;
        foreach (var (y0, y1, outBy) in new[] { (1.02f, 1.08f, 0.035f), (0f, 0.12f, 0.018f) })
        {
            Pic(dine, foot + up * ((y0 + y1) * 0.5f) + normal * outBy, along, up, length, y1 - y0, "trim_white", Cream);
            Pic(dine, foot + up * y1 + normal * (outBy * 0.5f), along, -normal, length, outBy, "trim_white", Cream);
        }
        shading = keep;
    }

    // ================= outside: the front, the side walls, the roof =================
    void Outside()
    {
        Color cream = Cream;
        // brick round the storefront, up to the front's parapet; brick sides and back up to their coping
        // (0.28 thick: their inner faces sit 2 cm behind the rooms' own walls, so the two never fight)
        TBox(face, -(W + 3.0f) * 0.5f, 0f, -0.14f, W - 3.0f, 5.6f, 0.28f, "brick_ext");
        TBox(face, (W + 3.0f) * 0.5f, 0f, -0.14f, W - 3.0f, 5.6f, 0.28f, "brick_ext");
        TBox(face, 0f, Lintel, -0.14f, 6.0f, 5.6f - Lintel, 0.28f, "brick_ext");
        TBox(face, (WinAR + WinBL) * 0.5f, 0f, -0.14f, WinBL - WinAR, Lintel, 0.28f, "brick_ext");
        TBox(face, -W + 0.14f, 0f, -D * 0.5f - 0.15f, 0.28f, 4.9f, D - 0.3f, "brick_ext");
        TBox(face, W - 0.14f, 0f, -D * 0.5f - 0.15f, 0.28f, 4.9f, D - 0.3f, "brick_ext");
        TBox(face, 0f, 0f, -D + 0.14f, 2f * W - 0.56f, 4.9f, 0.28f, "brick_ext");
        // the storefront: a cream mullion, pilasters, a black lintel band, a darker course over it
        TBox(face, (DoorR + WinAL) * 0.5f, 0f, -0.15f, WinAL - DoorR, Lintel, 0.2f, "trim_white", cream);
        foreach (float x in new[] { -2.9f, -1.8f, 0.3f, 0.9f, 2.9f })
        {
            TBox(face, x, 0f, 0.05f, 0.22f, Lintel, 0.1f, "trim_white", cream);
            TBox(face, x, 0f, 0.06f, 0.28f, 0.18f, 0.12f, "trim_white", Scale(cream, 0.9f));
            TBox(face, x, Lintel - 0.12f, 0.06f, 0.28f, 0.12f, 0.12f, "trim_white", cream);
        }
        TBox(face, 0f, Lintel, 0.03f, 6.2f, 0.2f, 0.06f, "white", B(25, 25, 28));
        TBox(face, 0f, Lintel + 0.2f, 0.01f, 6.2f, 0.2f, 0.02f, "brick_ext", Scale(White, 0.82f));
        // window frames, sills, kickplates, frosted six-pane transoms and real glass
        foreach (var (x0, x1) in new[] { (WinAL, WinAR), (WinBL, WinBR) })
        {
            float xm = (x0 + x1) * 0.5f, w = x1 - x0;
            foreach (float x in new[] { x0 + 0.05f, x1 - 0.05f }) TBox(face, x, 0f, -0.15f, 0.1f, Lintel, 0.2f, "trim_white", cream);
            TBox(face, xm, 0.82f, -0.15f, w, 0.08f, 0.2f, "trim_white", cream);
            TBox(face, xm, Head, -0.15f, w, 0.1f, 0.2f, "trim_white", cream);
            TBox(face, xm, TrTop, -0.15f, w, Lintel - TrTop, 0.2f, "trim_white", cream);
            for (int k = 1; k < 6; k++) TBox(face, x0 + 0.1f + (w - 0.2f) * k / 6f, Head + 0.1f, -0.15f, 0.04f, TrTop - Head - 0.1f, 0.12f, "trim_white", cream);
            for (int k = 0; k < 6; k++)
            {
                float px = x0 + 0.1f + (w - 0.2f) * (k + 0.5f) / 6f, pw = (w - 0.2f) / 6f - 0.04f, py = (Head + 0.1f + TrTop) * 0.5f, ph = TrTop - Head - 0.1f;
                Pic(face, S(px, py, -0.12f), -rgt, up, pw, ph, "glass_plain", B(205, 210, 212));
                Pic(dine, S(px, py, -0.18f), rgt, up, pw, ph, "glass_plain", B(205, 210, 212), true);
            }
            TBox(face, xm, 0.82f, 0.02f, w + 0.1f, 0.08f, 0.2f, "paving", B(185, 180, 170), null, 0.6f);      // the stone sill
            TBox(face, xm, 0f, -0.15f, w - 0.2f, 0.1f, 0.18f, "white", B(25, 25, 28));      // a black plinth
            TBox(face, xm, 0.1f, -0.17f, w - 0.2f, 0.72f, 0.14f, "trim_white", Scale(cream, 0.88f));      // the kickplate, recessed
            foreach (float y in new[] { 0.1f, 0.74f }) TBox(face, xm, y, -0.1f, w - 0.2f, 0.08f, 0.02f, "trim_white", cream);
            foreach (float x in new[] { x0 + 0.14f, x1 - 0.14f }) TBox(face, x, 0.1f, -0.1f, 0.08f, 0.72f, 0.02f, "trim_white", cream);
            Pane(xm, (Sill + Head) * 0.5f, -0.15f, w - 0.2f, Head - Sill);
        }
        // the logo in dark vinyl on window A, read from the terrace (backwards from inside)
        {
            float h = 0.86f, xm = (WinAL + WinAR) * 0.5f, y = 1.72f;
            Color vinyl = B(52, 54, 58);
            Logo(ink, S(xm, y, -0.142f), -rgt, up, h, vinyl);
            Logo(ink, S(xm, y, -0.158f), rgt, up, h, vinyl, true);
        }
        // the door: frame, transom bar, frosted transom with 1103B in gold, the leaf standing open into the room
        foreach (float x in new[] { DoorL + 0.03f, DoorR - 0.03f }) TBox(face, x, 0f, -0.15f, 0.06f, Lintel, 0.24f, "trim_white", cream);
        TBox(face, (DoorL + DoorR) * 0.5f, DoorTop, -0.15f, DoorR - DoorL, 0.1f, 0.24f, "trim_white", cream);
        TBox(face, (DoorL + DoorR) * 0.5f, TranTop, -0.15f, DoorR - DoorL, Lintel - TranTop, 0.24f, "trim_white", cream);
        Pic(face, S((DoorL + DoorR) * 0.5f, (DoorTop + 0.1f + TranTop) * 0.5f, -0.12f), -rgt, up, DoorR - DoorL - 0.12f, TranTop - DoorTop - 0.1f, "transom_1103B");
        Pic(dine, S((DoorL + DoorR) * 0.5f, (DoorTop + 0.1f + TranTop) * 0.5f, -0.18f), rgt, up, DoorR - DoorL - 0.12f, TranTop - DoorTop - 0.1f, "transom_1103B", null, true);
        Door();
        Awning();
        // a bronze cornice that takes snow; the fascia board above it with the logo; three gooseneck lamps; the coping
        Kit.AddBox(shell, S(0f, 3.95f, 0.125f), new Vector3(2f * W + 0.4f, 0.22f, 0.25f), Bronze, shopYaw);
        Kit.AddBox(shell, S(0f, 4.17f, 0.155f), new Vector3(2f * W + 0.5f, 0.06f, 0.31f), Scale(Bronze, 1.15f), shopYaw);
        TBox(face, 0f, 4.24f, 0.04f, 1.34f, 1.32f, 0.08f, "wood_dark", Black);
        Pic(face, S(0f, 4.9f, 0.081f), -rgt, up, 1.3f, 1.3f, "logo_sign");
        foreach (float x in new[] { -0.6f, 0f, 0.6f })
        {
            Strut(shell, S(x, 5.5f, 0.0f), S(x, 5.62f, 0.3f), 0.025f, 0.025f, Iron);
            Strut(shell, S(x, 5.62f, 0.3f), S(x, 5.55f, 0.45f), 0.025f, 0.025f, Iron);
            Lathe(face, S(x, 5.42f, 0.47f), new[] { 0.125f, 0.07f, 0.03f }, new[] { 0f, 0.1f, 0.14f }, "white", Iron, 10);
            Glow(glow, x, 5.43f, 0.47f, 0.16f, 0.02f, 0.16f, Warm, x);
        }
        Lamp(S(0f, 5.2f, 0.7f), "FFC88C", 4.0f, 1.4f);
        Kit.AddBox(shell, S(0f, 5.6f, -0.12f), new Vector3(2f * W + 0.1f, 0.1f, 0.42f), B(180, 178, 172), shopYaw);
        foreach (int sd in new[] { -1, 1 }) Kit.AddBox(shell, S(sd * (W - 0.15f), 4.9f, -D * 0.5f - 0.15f), new Vector3(0.42f, 0.1f, D - 0.3f), B(180, 178, 172), shopYaw);
        Kit.AddBox(shell, S(0f, 4.9f, -D + 0.15f), new Vector3(2f * W, 0.1f, 0.42f), B(180, 178, 172), shopYaw);
        // a garland along the cornice with red bows, and coloured lights
        for (float x = -W; x < W - 0.01f; x += 0.3f)
        {
            Kit.AddBox(shell, S(x + 0.15f, 4.2f + 0.05f * Mathf.Sin(x * 2.1f), 0.33f), new Vector3(0.36f, 0.2f, 0.24f), Scale(Fir, 0.85f + 0.3f * Mathf.PerlinNoise(x * 3f, 0.3f)), shopYaw + x * 40f);
            if (Mathf.Abs(x + 0.15f - Mathf.Round((x + 0.15f) / 1.8f) * 1.8f) < 0.16f) Kit.AddBox(shell, S(x + 0.15f, 4.2f, 0.47f), new Vector3(0.2f, 0.16f, 0.05f), Red, shopYaw);
        }
        Kit.LightString(b, "Pizzeria Front Lights", new List<Vector3> { S(-W, 4.3f, 0.46f), S(-1.5f, 4.3f, 0.46f), S(1.5f, 4.3f, 0.46f), S(W, 4.3f, 0.46f) }, 0.12f, 0.25f, false, false, 141);
        // downpipes at the front corners
        foreach (int sd in new[] { -1, 1 })
        {
            float x = sd * (W - 0.1f);
            Kit.AddBox(shell, S(x, 5.3f, 0.1f), new Vector3(0.2f, 0.18f, 0.2f), B(60, 60, 62), shopYaw);
            CylAlong(face, S(x, 5.3f, 0.1f), S(x, 0.25f, 0.1f), 0.05f, "white", B(60, 60, 62), 8);
            Strut(face, S(x, 0.25f, 0.1f), S(x, 0.08f, 0.3f), 0.1f, 0.1f, B(60, 60, 62));
        }
        // the round blade sign on a scroll bracket, beside the door
        {
            Vector3 c = S(-3.45f, 3.0f, 0.62f);
            Strut(shell, S(-3.45f, 3.47f, 0.0f), S(-3.45f, 3.47f, 1.05f), 0.04f, 0.04f, Iron);
            Ring(shell, S(-3.45f, 3.38f, 0.98f), rgt, 0.08f, 0.02f, Iron, 10);
            foreach (float z in new[] { 0.3f, 0.95f }) Strut(shell, S(-3.45f, 3.47f, z), S(-3.45f, 3.42f, z), 0.015f, 0.015f, Iron);
            foreach (int sd in new[] { -1, 1 })
            {
                Disc(face, c + rgt * (sd * 0.025f), rgt * sd, up, 0.4f, "white", B(24, 24, 26), 28);
                Logo(ink, c + rgt * (sd * 0.032f), fwd * sd, up, 0.5f, B(232, 224, 196));
            }
            Ring(face, c, rgt, 0.4f, 0.03f, Gold, 24);
        }
        // a second wreath on the pier between the windows
        Wreath(S((WinAR + WinBL) * 0.5f, 2.15f, 0.13f), fwd, 0.28f);
        // the roof: its slab, an upblast fan over the hood, a condenser, a vent, a hatch
        Kit.AddBox(shell, S(0f, H - 0.3f, -D * 0.5f), new Vector3(2f * W - 0.2f, 0.3f, D - 0.2f), B(80, 80, 84), shopYaw);
        Kit.AddBox(shell, S(-2.6f, H, -9.9f), new Vector3(0.9f, 0.3f, 0.9f), B(140, 140, 142), shopYaw);
        Lathe(face, S(-2.6f, H + 0.3f, -9.9f), new[] { 0.35f, 0.35f, 0.42f, 0.2f, 0.04f }, new[] { 0f, 0.35f, 0.38f, 0.52f, 0.6f }, "stainless", null, 14);
        Kit.AddBox(shell, S(2.0f, H, -4f), new Vector3(1.0f, 0.9f, 0.8f), B(150, 150, 150), shopYaw);
        Disc(face, S(2.0f, H + 0.905f, -4f), up, fwd, 0.32f, "stainless_baffle", B(40, 40, 42), 16);
        CylAlong(face, S(3.5f, H, -12f), S(3.5f, H + 0.6f, -12f), 0.05f, "white", B(60, 60, 62), 8);
        Kit.AddBox(shell, S(0f, H, -11.5f), new Vector3(0.8f, 0.15f, 0.8f), B(120, 120, 124), shopYaw);
        // the ghost sign on the side wall over the patio, and the firewood under its lean-to; the back door
        Color ghost = B(225, 215, 190);
        Logo(ink, S(-W - 0.004f, 2.9f, -7f), -fwd, up, 2.6f, new Color(ghost.r, ghost.g, ghost.b, 0.45f));
        {
            float x = -W - 0.4f;
            Kit.AddBox(shell, S(x, 1.7f, -10.5f), new Vector3(0.85f, 0.08f, 3.4f), B(70, 55, 45), shopYaw);
            foreach (float z in new[] { -8.9f, -12.1f }) Kit.AddBox(shell, S(x - 0.35f, 0f, z), new Vector3(0.08f, 1.7f, 0.08f), B(70, 55, 45), shopYaw);
            var random = new System.Random(17);
            for (int row = 0; row < 7; row++)
                for (int k = 0; k < 18; k++)
                {
                    float r = 0.06f + 0.03f * (float)random.NextDouble(), z = -9.0f - k * 0.17f - (row % 2) * 0.08f, y = 0.1f + row * 0.17f;
                    if (z < -12.0f) continue;
                    CylAlong(face, S(x + 0.2f, y + r, z), S(x - 0.2f, y + r, z), r, "timber", Timber, 7);
                    Disc(face, S(x - 0.2f, y + r, z), -rgt, up, r, "timber", B(150, 110, 75), 7);
                }
        }
        {
            float x = (HallX + IX) * 0.5f;
            TBox(face, x, 0f, -D - 0.02f, 0.95f, 2.1f, 0.04f, "white", B(110, 32, 32));
            Pic(face, S(x, 1.05f, -D - 0.041f), rgt, up, 0.95f, 2.1f, "door_white", B(150, 50, 45), true);
            Kit.AddBox(shell, S(x, 0f, -D - 0.4f), new Vector3(1.3f, 0.15f, 0.8f), B(150, 148, 144), shopYaw);
            Kit.AddBox(shell, S(x, 2.3f, -D - 0.08f), new Vector3(0.2f, 0.26f, 0.12f), Iron, shopYaw);
            Glow(glow, x, 2.43f, -D - 0.15f, 0.12f, 0.16f, 0.04f, Warm, 0.7f);
        }
        // snow drifted against the side and back walls (long mounds, out of the way of the firewood and the back door)
        int drift = 0;
        foreach (var (x, z, along) in new[] { (-W - 0.5f, -2.6f, true), (-W - 0.5f, -6.2f, true), (W + 0.5f, -3.0f, true), (W + 0.5f, -7.5f, true), (W + 0.5f, -12.0f, true), (-2.4f, -D - 0.5f, false), (0.6f, -D - 0.5f, false) })
        {
            Piece mound = b.MeshPiece("Pizzeria Drift " + (++drift), Kit.Mound(1.1f, 0.42f, 40 + drift), snowy, false);
            mound.Transform.position = S(x, -0.05f, z) + rootPos;
            mound.Transform.rotation = Quaternion.Euler(0f, shopYaw + (along ? 0f : 90f), 0f);
            mound.Transform.localScale = new Vector3(0.7f, 1f, 1.9f);
        }
        // window spill: warm light on the snow in front of the shop
        Lamp(S(-0.7f, 1.6f, 1.4f), "FFAA5A", 5.0f, 1.2f);
        Lamp(S(-2.4f, 2.0f, 1.0f), "FFAA5A", 4.5f, 1.0f);
    }

    // the awning: black and tan canvas from 3.5 m at the wall to 2.9 m, 1.2 m out, a zig-zag valance, side wings
    void Awning()
    {
        float x0 = -3.1f, x1 = 3.1f, outZ = 1.2f, backY = 3.5f, frontY = 2.9f, drop = 0.19f;
        Vector3 back = S(0f, backY, 0.06f), edge = S(0f, frontY, outZ);
        Vector3 slope = (edge - back).normalized;
        float len = (edge - back).magnitude, stripe = 0.12f;
        Tiled(face, (back + edge) * 0.5f, -rgt, -slope, x1 - x0, len, "canvas_awning", null, stripe, len);
        Tiled(face, (back + edge) * 0.5f - up * 0.006f, rgt, -slope, x1 - x0, len, "canvas_awning", B(150, 150, 150), stripe, len);
        Vector4 r = R("canvas_awning");
        Color dk = Scale(White, 0.85f);
        for (float x = x0; x < x1 - 0.01f; x += 1f / 12f)      // the valance: a band, then a tooth, both sides
        {
            float xa = x, xb = Mathf.Min(x1, x + 1f / 12f);
            Vector2 t = new Vector2(Mathf.Lerp(r.x, r.z, (((xa + xb) * 0.5f - x0) % stripe) / stripe), (r.y + r.w) * 0.5f);
            Vector3 ta = S(xa, frontY, outZ), tb = S(xb, frontY, outZ), ba = S(xa, frontY - drop, outZ), bb = S(xb, frontY - drop, outZ), tip = S((xa + xb) * 0.5f, frontY - drop - 0.06f, outZ);
            TexQuadOut(face, ba, bb, tb, ta, t, dk, fwd);
            TexQuadOut(face, ba, bb, tb, ta, t, Scale(dk, 0.6f), -fwd);
            TexQuadOut(face, ba, bb, tip, tip, t, dk, fwd);
            TexQuadOut(face, ba, bb, tip, tip, t, Scale(dk, 0.6f), -fwd);
        }
        foreach (int sd in new[] { -1, 1 })      // the side wings: triangles under the canvas's ends
        {
            float x = sd < 0 ? x0 : x1;
            Vector2 t = new Vector2((r.x + r.z) * 0.5f, (r.y + r.w) * 0.5f);
            Vector3 p0 = S(x, backY, 0.06f), p1 = S(x, frontY, outZ), p2 = S(x, frontY - drop, outZ);
            TexQuadOut(face, p0, p1, p2, p2, t, White, rgt * sd);
            TexQuadOut(face, p0, p1, p2, p2, t, Scale(White, 0.6f), -rgt * sd);
        }
        foreach (float x in new[] { x0, -1.0f, 1.0f, x1 })
            Strut(shell, S(x, 2.6f, 0.03f), S(x, frontY - 0.02f, outZ - 0.03f), 0.03f, 0.03f, Iron);
        Strut(shell, S(x0, frontY - 0.03f, outZ - 0.03f), S(x1, frontY - 0.03f, outZ - 0.03f), 0.03f, 0.03f, Iron);
    }

    // the door: standing open 90 degrees into the room, hinged at DoorL: a cream leaf with blinds behind its glass (the
    // atlas's front door), a kick plate, a pull bar, a closer at its head, bells on a red ribbon and a fir wreath
    void Door()
    {
        float x = DoorL + 0.08f, z0 = -0.3f, w = DoorR - DoorL - 0.12f, z1 = z0 - w, zm = (z0 + z1) * 0.5f;
        Color leaf = B(232, 226, 210);
        TBox(dine, x + 0.0225f, 0f, zm, 0.045f, DoorTop - 0.02f, w, "white", leaf);
        Pic(dine, S(x + 0.047f, DoorTop * 0.5f, zm), fwd, up, w, DoorTop - 0.02f, "front_door");      // its outer face, now facing +x
        Pic(dine, S(x - 0.002f, DoorTop * 0.5f, zm), -fwd, up, w, DoorTop - 0.02f, "front_door", null, true);
        foreach (float xf in new[] { x + 0.048f, x - 0.003f }) Box(dine, xf, 0.02f, zm, 0.004f, 0.25f, w - 0.06f, B(170, 172, 175));
        Strut(dine, S(x + 0.11f, 0.85f, z1 + 0.12f), S(x + 0.11f, 1.15f, z1 + 0.12f), 0.02f, 0.02f, Chrome);
        foreach (float y in new[] { 0.85f, 1.15f }) Strut(dine, S(x + 0.045f, y, z1 + 0.12f), S(x + 0.11f, y, z1 + 0.12f), 0.015f, 0.015f, Chrome);
        Disc(dine, S(x + 0.05f, 1.3f, z1 + 0.1f), rgt, up, 0.025f, "white", Gold, 10);
        Box(dine, DoorL + 0.25f, DoorTop - 0.12f, -0.36f, 0.3f, 0.07f, 0.08f, Black);      // the closer, on the frame's head
        Strut(dine, S(DoorL + 0.25f, DoorTop - 0.08f, -0.4f), S(x + 0.03f, DoorTop - 0.1f, z0 - 0.35f), 0.02f, 0.02f, Black);
        // bells on a red ribbon from the pull bar
        Strut(dine, S(x + 0.12f, 1.12f, z1 + 0.12f), S(x + 0.12f, 0.87f, z1 + 0.12f), 0.02f, 0.006f, Red, fwd);
        for (int k = 0; k < 3; k++) Lathe(dine, S(x + 0.13f, 0.88f + k * 0.08f, z1 + 0.12f), new[] { 0.001f, 0.016f, 0.012f, 0.001f }, new[] { 0f, 0.008f, 0.025f, 0.032f }, "copper", Gold, 8);
        Wreath(S(x + 0.06f, 1.62f, zm), rgt, 0.22f);
    }

    // a fir wreath: clusters of needles on a ring, berries and a red bow, facing 'normal', radius R
    void Wreath(Vector3 c, Vector3 normal, float radius)
    {
        var mb = new MeshBuilder();
        Vector3 p1 = Vector3.Cross(normal, Vector3.up).normalized, p2 = Vector3.up;
        var random = new System.Random((int)(radius * 1000f));
        for (int k = 0; k < 32; k++)
        {
            float a = 2f * Mathf.PI * k / 32f;
            Vector3 radial = p1 * Mathf.Cos(a) + p2 * Mathf.Sin(a), along = p1 * Mathf.Cos(a + Mathf.PI * 0.5f) + p2 * Mathf.Sin(a + Mathf.PI * 0.5f);
            Vector3 at = c + radial * radius;
            for (int n = 0; n < 5; n++)
            {
                Vector3 dirv = along * (0.5f + (float)random.NextDouble()) * (n % 2 == 0 ? 1f : -1f) + normal * (float)random.NextDouble() * 0.6f + radial * ((float)random.NextDouble() - 0.5f) * 1.6f;
                Vector3 o = at + normal * (0.01f + 0.04f * (float)random.NextDouble());
                Strut(mb, o, o + dirv.normalized * radius * 0.42f, 0.03f * radius / 0.22f, 0.012f, Scale(Fir, 0.75f + 0.5f * (float)random.NextDouble()));
            }
        }
        float s = radius / 0.22f;
        for (int k = 0; k < 6; k++)
        {
            float a = 2f * Mathf.PI * k / 6f + 0.4f;
            for (int n = 0; n < 3; n++)
                Kit.AddBox(mb, c + (p1 * Mathf.Cos(a + n * 0.07f) + p2 * Mathf.Sin(a + n * 0.07f)) * (radius * (0.95f + 0.1f * n)) + normal * 0.06f, Vector3.one * 0.022f * s, Red, 0f);
        }
        Vector3 bow = c - p2 * radius + normal * 0.07f;
        foreach (int sd in new[] { -1, 1 })
        {
            Strut(mb, bow, bow + (p1 * sd * 0.12f + p2 * 0.05f) * s, 0.06f * s, 0.02f, Red, p2);
            Strut(mb, bow, bow + (p1 * sd * 0.07f - p2 * 0.18f) * s, 0.04f * s, 0.015f, Scale(Red, 0.85f), p1);
        }
        Kit.AddBox(mb, bow - p2 * 0.02f * s, new Vector3(0.05f, 0.05f, 0.05f) * s, Scale(Red, 0.9f), 0f);
        b.MeshPiece("Pizzeria Wreath " + (int)(radius * 100f), mb.ToMesh(), bare, false);
    }

    // ================= the dining room =================
    void Dining()
    {
        // the runner from the door, with a darker border
        Tiled(dine, S((DoorL + DoorR) * 0.5f, 0.012f, -1.975f), rgt, fwd, 0.9f, 3.25f, "rug_black");
        foreach (int sd in new[] { -1, 1 }) Pic(dine, S((DoorL + DoorR) * 0.5f + sd * 0.43f, 0.0125f, -1.975f), rgt, fwd, 0.04f, 3.25f, "white", B(32, 33, 33));
        // the ceiling: black joists, a timber header beam along the front, the spiral duct with rings and diffusers, a tube light
        for (float z = -0.75f; z > CounterZ + 0.2f; z -= 0.6f) Box(dine, 0f, CD - 0.22f, z, 2f * IX, 0.22f, 0.08f, B(28, 28, 30));
        TBox(dine, 0f, CD - 0.3f, -0.43f, 2f * IX, 0.3f, 0.25f, "wood_dark", B(110, 80, 75), null, 0f, true);
        CylAlong(dine, S(2.2f, 3.25f, -0.8f), S(2.2f, 3.25f, CounterZ + 0.02f), 0.18f, "ceiling_black", B(60, 60, 62), 14);
        Disc(dine, S(2.2f, 3.25f, -0.8f), fwd, up, 0.18f, "ceiling_black", B(40, 40, 42), 14);
        for (float z = -1.5f; z > CounterZ + 0.3f; z -= 1.5f) CylAlong(dine, S(2.2f, 3.25f, z + 0.02f), S(2.2f, 3.25f, z - 0.02f), 0.19f, "ceiling_black", B(45, 45, 47), 14);
        foreach (float z in new[] { -2.5f, -5.5f })
        {
            CylAlong(dine, S(2.2f, 3.08f, z), S(2.2f, 3.0f, z), 0.15f, "stainless", B(120, 120, 122), 14);
            Disc(dine, S(2.2f, 2.999f, z), -up, fwd, 0.15f, "stainless_baffle", B(140, 140, 142), 14);
        }
        Box(dine, -1.5f, CD - 0.3f, -6.6f, 1.2f, 0.08f, 0.14f, B(235, 235, 232));
        Glow(bulbs, -1.5f, CD - 0.305f, -6.6f, 1.1f, 0.01f, 0.08f, Cool, 0.2f);
        // the front wall's inside: white panels under the windows, cream casings and window stools
        foreach (var (x0, x1) in new[] { (WinAL, WinAR), (WinBL, WinBR) })
        {
            float xm = (x0 + x1) * 0.5f;
            Pic(dine, S(xm, Sill * 0.5f, -0.3f), rgt, up, x1 - x0, Sill, "beadboard", B(232, 230, 224));
            TBox(dine, xm, Sill - 0.03f, -0.39f, x1 - x0 + 0.24f, 0.04f, 0.18f, "trim_white", Cream);
            foreach (float x in new[] { x0 - 0.05f, x1 + 0.05f }) TBox(dine, x, Sill, -0.3125f, 0.1f, Lintel - Sill, 0.025f, "trim_white", Cream);
            TBox(dine, xm, Lintel, -0.3125f, x1 - x0 + 0.2f, 0.1f, 0.025f, "trim_white", Cream);
            TBox(dine, xm, 0f, -0.309f, x1 - x0, 0.12f, 0.018f, "trim_white", Cream);
        }
        foreach (float x in new[] { DoorL - 0.05f, DoorR + 0.05f }) TBox(dine, x, 0f, -0.3125f, 0.1f, Lintel, 0.025f, "trim_white", Cream);
        TBox(dine, (DoorL + DoorR) * 0.5f, Lintel, -0.3125f, DoorR - DoorL + 0.2f, 0.1f, 0.025f, "trim_white", Cream);
        // a poinsettia and a candle on window B's stool
        Poinsettia(S(WinBL + 0.4f, Sill + 0.01f, -0.39f), 0.7f);
        Lathe(dine, S(WinBR - 0.4f, Sill + 0.01f, -0.39f), new[] { 0.035f, 0.035f }, new[] { 0f, 0.12f }, "white", B(240, 236, 225), 10, true);
        Glow(bulbs, WinBR - 0.4f, Sill + 0.16f, -0.39f, 0.015f, 0.03f, 0.015f, Amber, 0.5f);
        WindowBar();
        Curtains(WinAL, WinAR);
        Curtains(WinBL, WinBR);
        Bookshelf();
        // the cat canvases, unframed and staggered; a wine shelf, a clock, the chalkboard menu on the left wall
        foreach (var (z, y, name) in new[] { (-3.95f, 1.9f, "cat_pizza"), (-3.35f, 1.6f, "cat_wine") })
        {
            TBox(dine, -IX + 0.0175f, y - 0.26f, z, 0.035f, 0.52f, 0.4f, "white", B(30, 34, 30));
            Pic(dine, S(-IX + 0.036f, y, z), fwd, up, 0.4f, 0.52f, name, null, false, 0.11f, 0.89f, 0f, 1f);
        }
        WineShelf(-5.0f);
        Disc(dine, S(-IX + 0.03f, 2.5f, -5.0f), rgt, up, 0.16f, "clock", null, 28);
        Ring(dine, S(-IX + 0.03f, 2.5f, -5.0f), rgt, 0.165f, 0.02f, Black, 20);
        TBox(dine, -IX + 0.02f, 1.25f, -6.25f, 0.04f, 1.0f, 0.9f, "wood_dark", B(110, 76, 52));
        Pic(dine, S(-IX + 0.041f, 1.75f, -6.25f), fwd, up, 0.8f, 0.9f, "menu_board", null, false, 0.05f, 0.95f, 0f, 1f);
        HighChair(S(-3.85f, 0f, -6.75f));
        // the booths, two small tables, the drinks cooler and a bin
        foreach (float zc in new[] { -1.85f, -4.35f }) Booth(zc);
        foreach (float zc in new[] { -2.6f, -4.9f }) TwoTop(1.2f, zc);
        Cooler(S(IX - 0.33f, 0f, -6.25f));
        TBox(dine, 2.4f, 0f, -6.95f, 0.45f, 0.82f, 0.45f, "wood_dark", B(110, 85, 70));
        TBox(dine, 2.4f, 0.82f, -6.95f, 0.47f, 0.05f, 0.47f, "wood_dark", B(80, 60, 50));
        // the pendants: teardrop shades on black cords (one red over the bar, one over a booth), each with its lamp
        foreach (var (x, z, red) in new[] { (-1.2f, -0.6f, false), (-0.2f, -0.6f, true), (3.6f, -1.85f, false), (3.6f, -4.35f, true), (-3.0f, -6.9f, false), (-1.0f, -6.9f, false) })
        {
            Color shade = red ? B(215, 95, 105) : B(190, 150, 110);
            Disc(dine, S(x, CD - 0.225f, z), -up, fwd, 0.05f, "white", Black, 10);
            Strut(dine, S(x, CD - 0.225f, z), S(x, 2.58f, z), 0.008f, 0.008f, Black);
            Lathe(dine, S(x, 2.3f, z), new[] { 0.045f, 0.06f, 0.065f, 0.058f, 0.04f, 0.025f }, new[] { 0f, 0.05f, 0.11f, 0.17f, 0.24f, 0.28f }, "glass_plain", shade, 8);
            Glow(bulbs, x, 2.31f, z, 0.04f, 0.06f, 0.04f, red ? new Color(1f, 0.5f, 0.5f) : Amber, x);
            Lamp(S(x, 2.22f, z), red ? "F07878" : "FFBE7D", 3.0f, 1.5f);
        }
    }

    void Poinsettia(Vector3 foot, float s)
    {
        Lathe(dine, foot, new[] { 0.06f * s, 0.09f * s, 0.09f * s }, new[] { 0f, 0.14f * s, 0.15f * s }, "copper", Gold, 10, true, "wood_dark");
        var random = new System.Random((int)(foot.x * 100f + foot.z * 7f));
        for (int k = 0; k < 14; k++)
        {
            float a = k * 2.4f, tilt = 0.25f + 0.35f * (float)random.NextDouble();
            Vector3 o = foot + up * 0.16f * s, dirv = new Vector3(Mathf.Cos(a), tilt, Mathf.Sin(a)).normalized;
            Strut(dine, o, o + dirv * (0.13f + 0.05f * (float)random.NextDouble()) * s, 0.06f * s, 0.008f, k % 3 == 0 ? Green : Scale(Red, 0.85f + 0.3f * (float)random.NextDouble()));
        }
        Kit.AddBox(dine, foot + up * 0.2f * s, Vector3.one * 0.03f * s, Gold, 0f);
    }

    void WindowBar()
    {
        float x0 = WinAL + 0.1f, x1 = WinAR - 0.1f, z = -0.62f, top = 1.05f;
        TBox(dine, (x0 + x1) * 0.5f, top - 0.04f, z, x1 - x0, 0.04f, 0.35f, "wood_dark", B(110, 70, 55), null, 0f, true);
        Color pipe = B(35, 35, 37);
        foreach (float x in new[] { x0 + 0.15f, x1 - 0.15f })
        {
            CylAlong(dine, S(x, 0f, z - 0.05f), S(x, top - 0.04f, z - 0.05f), 0.017f, "pipe_iron", pipe, 8);
            Cyl(dine, S(x, 0f, z - 0.05f), 0.055f, 0.01f, "pipe_iron", "pipe_iron", pipe, 10);
            Box(dine, x, 0.28f, z - 0.05f, 0.05f, 0.05f, 0.05f, pipe);
            Cyl(dine, S(x, top - 0.08f, z - 0.05f), 0.03f, 0.04f, "pipe_iron", "pipe_iron", pipe, 8);
            CylAlong(dine, S(x, 0.75f, z - 0.05f), S(x, 0.75f, -0.31f), 0.014f, "pipe_iron", pipe, 8);      // to a flange on the wall
            Disc(dine, S(x, 0.75f, -0.305f), -fwd, up, 0.045f, "pipe_iron", pipe, 10);
        }
        CylAlong(dine, S(x0 + 0.15f, 0.305f, z - 0.05f), S(x1 - 0.15f, 0.305f, z - 0.05f), 0.015f, "pipe_iron", pipe, 8);
        // the stools: round grey seats, splayed black legs, a foot ring, a ladder back
        foreach (float x in new[] { -1.15f, -0.25f })
        {
            float sz = -1.05f;
            Cyl(dine, S(x, 0.72f, sz), 0.21f, 0.08f, "leather_black", "leather_black", B(88, 86, 86), 16);
            for (int k = 0; k < 4; k++)
            {
                float a = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f;
                Vector3 d = Dir(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Strut(dine, S(x, 0.72f, sz) + d * 0.16f, S(x, 0f, sz) + d * 0.25f, 0.025f, 0.025f, Black);
            }
            Ring(dine, S(x, 0.3f, sz), up, 0.21f, 0.02f, Black, 12);
            foreach (int sd in new[] { -1, 1 }) Strut(dine, S(x + sd * 0.17f, 0.75f, sz - 0.16f), S(x + sd * 0.18f, 1.12f, sz - 0.2f), 0.025f, 0.025f, Black);
            for (int k = 0; k < 4; k++) Strut(dine, S(x - 0.19f, 0.86f + k * 0.08f, sz - 0.17f - k * 0.008f), S(x + 0.19f, 0.86f + k * 0.08f, sz - 0.17f - k * 0.008f), 0.035f, 0.012f, Black, up);
            Seat("Bar Stool", S(x, 0.82f, sz), shopYaw);
            CylAlong(dine, S(x - 0.08f, top + 0.02f, z + 0.05f), S(x + 0.12f, top + 0.02f, z + 0.05f), 0.02f, "curtain_sheer", B(70, 70, 74), 8);      // a rolled napkin
            Disc(dine, S(x, top + 0.003f, z - 0.02f), up, fwd, 0.12f, "white", B(28, 28, 30), 16);      // a placemat
        }
        Color[] fill = { B(240, 240, 240), B(50, 40, 35), B(235, 225, 190), B(190, 40, 30) };
        for (int k = 0; k < 4; k++) Cyl(dine, S(x1 - 0.5f + k * 0.06f, top, z + 0.08f), 0.02f, 0.09f, "glass_plain", "stainless", Scale(fill[k], 0.9f), 8);
        // a tiny chalkboard easel, and the poinsettia arrangement in a pot at the end
        Strut(dine, S(x1 - 0.18f, top, z + 0.02f), S(x1 - 0.18f, top + 0.16f, z + 0.06f), 0.12f, 0.01f, B(95, 62, 40), rgt);
        Pic(dine, S(x1 - 0.18f, top + 0.085f, z + 0.033f), -rgt, (up * 0.16f + fwd * 0.04f).normalized, 0.1f, 0.13f, "chalk_treats");
        Poinsettia(S(x1, top, z - 0.05f), 1.2f);
    }

    // sheer curtains gathered at both sides of a window, on a black rod with finials, with a black header band
    void Curtains(float x0, float x1)
    {
        float top = Head + 0.05f, bottom = Sill + 0.05f, rodZ = -0.38f;
        CylAlong(dine, S(x0 - 0.25f, top, rodZ), S(x1 + 0.25f, top, rodZ), 0.0125f, "pipe_iron", Black, 8);
        foreach (float x in new[] { x0 - 0.27f, x1 + 0.27f }) Lathe(dine, S(x, top - 0.03f, rodZ), new[] { 0.001f, 0.03f, 0.001f }, new[] { 0f, 0.03f, 0.06f }, "pipe_iron", Black, 8);
        foreach (int sd in new[] { -1, 1 })
        {
            float edge = sd < 0 ? x0 - 0.18f : x1 + 0.18f;
            for (int k = 0; k < 8; k++)
            {
                float x = edge - sd * (0.0275f + k * 0.055f);
                float ang = (k % 2 == 0 ? 35f : -35f) * Mathf.Deg2Rad;
                Vector3 across = (rgt * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)).normalized;
                Color c = k % 2 == 0 ? B(205, 198, 196) : B(175, 168, 166);
                Vector3 at = S(x, (bottom + top - 0.1f) * 0.5f, rodZ + 0.01f);
                Pic(dine, at, across, up, 0.067f, top - 0.1f - bottom, "curtain_sheer", c, false, k / 8f, (k + 1) / 8f);
                Pic(dine, at, -across, up, 0.067f, top - 0.1f - bottom, "curtain_sheer", Scale(c, 0.8f), true, k / 8f, (k + 1) / 8f);      // its back, seen through the glass
                Box(dine, x, top - 0.1f, rodZ + 0.01f, 0.06f, 0.1f, 0.04f, B(30, 30, 32));
            }
        }
    }

    void Bookshelf()
    {
        float xb = -IX, d = 0.33f, z0 = -0.9f, z1 = -2.1f, zm = (z0 + z1) * 0.5f, hgt = 2.25f;
        Color wood = B(60, 38, 28);
        TBox(dine, xb + d * 0.5f, 0f, z0 - 0.02f, d, hgt, 0.04f, "wood_dark", wood);
        TBox(dine, xb + d * 0.5f, 0f, z1 + 0.02f, d, hgt, 0.04f, "wood_dark", wood);
        Pic(dine, S(xb + 0.01f, hgt * 0.5f, zm), fwd, up, z0 - z1, hgt, "wood_dark", Scale(wood, 0.7f));
        float[] shelves = { 0.08f, 0.5f, 0.92f, 1.34f, 1.76f, hgt - 0.04f };
        foreach (float y in shelves) TBox(dine, xb + d * 0.5f, y, zm, d, 0.03f, z0 - z1 - 0.08f, "wood_dark", wood);
        foreach (float z in new[] { z0 + 0.05f, z1 - 0.05f }) TBox(dine, xb + 0.0125f + d, 0f, z, 0.025f, hgt + 0.15f, 0.1f, "trim_white", Cream);
        TBox(dine, xb + 0.0125f + d, hgt, zm, 0.025f, 0.15f, z0 - z1 + 0.2f, "trim_white", Cream);
        TBox(dine, xb + d * 0.5f, hgt + 0.15f, zm, d + 0.05f, 0.04f, z0 - z1 + 0.26f, "trim_white", Cream);
        // books, leaning now and then; one shelf of folded T-shirts; the poinsettia; two plates on top
        Color[] covers = { B(110, 35, 35), B(35, 45, 75), B(40, 70, 50), B(220, 210, 185), B(30, 30, 30), B(190, 150, 60), B(90, 60, 45) };
        var random = new System.Random(5);
        for (int s = 0; s < 5; s++)
        {
            float y = shelves[s] + 0.03f;
            if (s == 3)
            {
                for (int k = 0; k < 3; k++)
                    for (int n = 0; n < 4; n++) Box(dine, xb + 0.17f, y + n * 0.045f, z0 - 0.2f - k * 0.34f, 0.26f, 0.04f, 0.3f, n % 2 == 0 ? B(28, 28, 30) : B(110, 110, 112));
                continue;
            }
            if (s == 2)
            {
                Poinsettia(S(xb + 0.17f, y, zm + 0.25f), 0.9f);
                for (float z = zm - 0.05f; z > z1 + 0.08f; z -= 0.04f) { float hb = 0.18f + 0.08f * (float)random.NextDouble(); Box(dine, xb + 0.16f, y, z, 0.22f, hb, 0.035f, covers[random.Next(covers.Length)]); }
                continue;
            }
            float zz = z0 - 0.06f;
            while (zz > z1 + 0.08f)
            {
                float wb = 0.025f + 0.025f * (float)random.NextDouble(), hb = 0.18f + 0.08f * (float)random.NextDouble();
                Color c = Scale(covers[random.Next(covers.Length)], 0.8f + 0.4f * (float)random.NextDouble());
                if (random.NextDouble() < 0.08)
                {
                    Vector3 foot = S(xb + 0.16f, y, zz - hb * 0.4f);
                    Strut(dine, foot, foot + (up * 0.92f - fwd * 0.4f).normalized * hb, 0.22f, wb, c, rgt);      // leaning
                    zz -= hb * 0.45f + wb;
                }
                else { Box(dine, xb + 0.16f, y, zz - wb * 0.5f, 0.22f, hb, wb, c); zz -= wb + 0.002f; }
            }
        }
        foreach (float z in new[] { zm + 0.25f, zm - 0.25f })
        {
            Vector3 c = S(xb + 0.08f, shelves[5] + 0.17f, z), n = (rgt * 0.95f + up * 0.3f).normalized;
            Disc(dine, c, n, up, 0.13f, "white", B(235, 232, 225), 18);
            Ring(dine, c + n * 0.003f, n, 0.11f, 0.01f, B(40, 80, 140), 18);
        }
    }

    void WineShelf(float zc)
    {
        float xb = -IX, d = 0.28f;
        Color wood = B(60, 38, 28);
        foreach (float z in new[] { zc + 0.33f, zc - 0.33f }) TBox(dine, xb + d * 0.5f, 1.0f, z, d, 0.9f, 0.03f, "wood_dark", wood);
        foreach (float y in new[] { 1.0f, 1.3f, 1.6f, 1.88f }) TBox(dine, xb + d * 0.5f, y, zc, d, 0.025f, 0.63f, "wood_dark", wood);
        Pic(dine, S(xb + 0.01f, 1.45f, zc), fwd, up, 0.66f, 0.9f, "wood_dark", Scale(wood, 0.7f));
        for (int s = 0; s < 3; s++)
            for (int k = 0; k < 3; k++)
            {
                Vector3 foot = S(xb + 0.14f, 1.025f + s * 0.3f, zc - 0.2f + k * 0.2f);
                Color glassC = (s + k) % 2 == 0 ? B(30, 50, 35) : B(60, 15, 20);
                Lathe(dine, foot, new[] { 0.037f, 0.037f, 0.03f, 0.014f, 0.014f, 0.016f }, new[] { 0f, 0.17f, 0.2f, 0.23f, 0.26f, 0.27f }, "glass_plain", glassC, 10);
                Cyl(dine, foot + up * 0.24f, 0.016f, 0.035f, "copper", "copper", (s + k) % 2 == 0 ? Gold : Red, 8);
                Pic(dine, foot + up * 0.09f + rgt * 0.038f, fwd, up, 0.05f, 0.06f, "white", B(230, 220, 195));
            }
    }

    void HighChair(Vector3 foot)
    {
        Color wood = B(70, 45, 32);
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f;
            Vector3 d = Dir(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Strut(dine, foot + up * 0.75f + d * 0.16f, foot + d * 0.3f, 0.035f, 0.035f, wood);
        }
        Kit.AddBox(dine, foot + up * 0.75f, new Vector3(0.36f, 0.04f, 0.34f), wood, shopYaw);
        foreach (int sd in new[] { -1, 1 }) Strut(dine, foot + up * 0.75f + Dir(sd * 0.16f, 0f, -0.15f), foot + up * 1.25f + Dir(sd * 0.16f, 0f, -0.18f), 0.03f, 0.03f, wood);
        Strut(dine, foot + up * 1.2f + Dir(-0.17f, 0f, -0.18f), foot + up * 1.2f + Dir(0.17f, 0f, -0.18f), 0.05f, 0.02f, wood, up);
        Strut(dine, foot + up * 0.95f + Dir(-0.17f, 0f, 0.12f), foot + up * 0.95f + Dir(0.17f, 0f, 0.12f), 0.03f, 0.03f, wood);
        Ring(dine, foot + up * 0.3f, up, 0.22f, 0.02f, wood, 10);
    }

    // a booth across the right wall: two black vinyl benches facing over a pedestal table, laid for diners
    void Booth(float zc)
    {
        float xa = 3.0f, xw = IX - 0.01f, xm = (xa + xw) * 0.5f, len = xw - xa;
        Color vinyl = B(34, 35, 35), shellC = B(22, 22, 22), cap = B(60, 38, 28);
        foreach (int s in new[] { -1, 1 })
        {
            float seatFront = zc + s * 0.575f, seatBack = zc + s * 1.025f;
            TBox(dine, xm, 0f, (seatFront + seatBack) * 0.5f, len, 0.33f, 0.45f, "leather_black", shellC);
            TBox(dine, xm, 0.33f, (seatFront + seatBack) * 0.5f - s * 0.01f, len, 0.12f, 0.43f, "leather_black", vinyl);
            CylAlong(dine, S(xa, 0.39f, seatFront + s * 0.03f), S(xw, 0.39f, seatFront + s * 0.03f), 0.06f, "leather_black", vinyl, 10);      // its rolled edge
            // the back, tilted 8 degrees, with two seams; its black shell; the cap rail; the aisle end
            Vector3 lo = S(xm, 0.45f, seatBack - s * 0.06f), hi = S(xm, 1.05f, seatBack + s * 0.02f);
            Strut(dine, lo, hi, len, 0.12f, vinyl, rgt);
            foreach (float t in new[] { 0.35f, 0.68f })
            {
                Vector3 seam = Vector3.Lerp(lo, hi, t) - fwd * (s * 0.061f);
                Strut(dine, seam - rgt * (len * 0.5f), seam + rgt * (len * 0.5f), 0.008f, 0.01f, B(12, 12, 12));
            }
            TBox(dine, xm, 0.33f, seatBack + s * 0.085f, len, 0.77f, 0.08f, "leather_black", shellC);
            TBox(dine, xm, 1.1f, seatBack + s * 0.085f, len + 0.04f, 0.06f, 0.14f, "wood_dark", cap);
            TBox(dine, xa - 0.02f, 0f, zc + s * 0.83f, 0.04f, 1.1f, 0.58f, "wood_dark", B(25, 25, 25));
            for (int k = 0; k < 2; k++) Seat("Booth Seat", S(xa + 0.32f + k * 0.55f, 0.48f, zc + s * 0.8f), shopYaw + (s > 0 ? 180f : 0f));
        }
        // the table: espresso top with a silver edge, a black column on an X foot
        TBox(dine, xm - 0.05f, 0.705f, zc, 1.1f, 0.035f, 0.75f, "wood_dark", Espresso);
        Box(dine, xm - 0.05f, 0.7f, zc, 1.11f, 0.008f, 0.76f, B(170, 170, 172));
        Cyl(dine, S(xm - 0.05f, 0f, zc), 0.04f, 0.7f, "pipe_iron", null, Black, 10);
        foreach (int sd in new[] { -1, 1 }) Strut(dine, S(xm - 0.05f - 0.3f, 0.02f, zc - sd * 0.3f), S(xm - 0.05f + 0.3f, 0.02f, zc + sd * 0.3f), 0.06f, 0.03f, Black);
        // napkins, shakers, plates and cutlery
        TBox(dine, xw - 0.2f, 0.74f, zc, 0.1f, 0.15f, 0.12f, "stainless", null, null, 0.3f);
        Cyl(dine, S(xw - 0.2f, 0.74f, zc + 0.12f), 0.02f, 0.09f, "glass_plain", "stainless", B(240, 240, 240), 8);
        Cyl(dine, S(xw - 0.2f, 0.74f, zc + 0.17f), 0.02f, 0.09f, "glass_plain", "stainless", B(60, 45, 35), 8);
        Cyl(dine, S(xw - 0.2f, 0.74f, zc - 0.13f), 0.022f, 0.1f, "glass_plain", "stainless", B(220, 205, 160), 8);
        foreach (float px in new[] { xm - 0.35f, xm + 0.15f })
            foreach (int sd in new[] { -1, 1 })
            {
                Disc(dine, S(px, 0.742f, zc + sd * 0.2f), up, fwd, 0.11f, "white", B(240, 238, 232), 16);
                Box(dine, px + 0.15f, 0.741f, zc + sd * 0.2f, 0.015f, 0.004f, 0.17f, Chrome);
            }
    }

    // a round two-top table with two ladder-back chairs
    void TwoTop(float x, float z)
    {
        Cyl(dine, S(x, 0.71f, z), 0.3f, 0.03f, "wood_dark", "wood_dark", Walnut, 18);
        Cyl(dine, S(x, 0f, z), 0.03f, 0.71f, "pipe_iron", null, Black, 8);
        Cyl(dine, S(x, 0f, z), 0.22f, 0.02f, "pipe_iron", "pipe_iron", Black, 14);
        Disc(dine, S(x, 0.742f, z), up, fwd, 0.1f, "white", B(240, 238, 232), 14);
        Cyl(dine, S(x + 0.12f, 0.74f, z + 0.1f), 0.018f, 0.08f, "glass_plain", "stainless", B(240, 240, 240), 8);
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 c = S(x, 0f, z + s * 0.5f);
            Color wood = B(55, 36, 28);
            foreach (var (dx, dz) in new[] { (-0.19f, -0.19f), (0.19f, -0.19f), (-0.19f, 0.19f), (0.19f, 0.19f) }) Strut(dine, c + Dir(dx, 0f, dz), c + Dir(dx * 0.95f, 0.46f, dz * 0.95f), 0.03f, 0.03f, wood);
            Kit.AddBox(dine, c + up * 0.44f, new Vector3(0.42f, 0.04f, 0.42f), wood, shopYaw);
            foreach (int sd in new[] { -1, 1 }) Strut(dine, c + Dir(sd * 0.19f, 0.46f, s * 0.19f), c + Dir(sd * 0.19f, 0.95f, s * 0.22f), 0.03f, 0.03f, wood);
            for (int k = 0; k < 3; k++) Strut(dine, c + Dir(-0.19f, 0.6f + k * 0.13f, s * (0.2f + k * 0.008f)), c + Dir(0.19f, 0.6f + k * 0.13f, s * (0.2f + k * 0.008f)), 0.04f, 0.012f, wood, up);
            Seat("Table Chair", c + up * 0.55f, shopYaw + (s > 0 ? 180f : 0f));
        }
    }

    // the drinks cooler against the right wall, facing the room: a black cabinet, lit inside, cans and bottles, a glass door
    void Cooler(Vector3 foot)
    {
        Vector3 F(float dx, float y, float dz) => foot + Dir(dx, y, dz);      // dx along +x (to the wall), dz along the wall
        Color frame = B(25, 25, 28);
        foreach (int sd in new[] { -1, 1 }) Kit.AddBox(dine, F(0f, 0f, sd * 0.35f), new Vector3(0.65f, 2.0f, 0.04f), frame, shopYaw);
        Kit.AddBox(dine, F(0f, 1.8f, 0f), new Vector3(0.65f, 0.2f, 0.7f), frame, shopYaw);
        Kit.AddBox(dine, F(0f, 0f, 0f), new Vector3(0.65f, 0.18f, 0.7f), frame, shopYaw);
        Pic(dine, F(0.31f, 1.0f, 0f), -fwd, up, 0.66f, 1.6f, "white", B(230, 240, 255));
        Color[] cans = { B(200, 30, 30), B(30, 70, 170), B(40, 140, 60), B(230, 180, 40) };
        for (int sh = 0; sh < 4; sh++)
        {
            float y = 0.2f + sh * 0.4f;
            Kit.AddBox(dine, F(0f, y, 0f), new Vector3(0.6f, 0.012f, 0.66f), B(200, 202, 206), shopYaw);
            for (int k = 0; k < 5; k++)
                for (int n = 0; n < 2; n++)
                    Cyl(dine, F(-0.05f + n * 0.14f, y + 0.012f, -0.25f + k * 0.125f), sh < 2 ? 0.033f : 0.03f, sh < 2 ? 0.12f : 0.22f, "copper", "stainless", cans[(k + sh + n) % 4], 8);
        }
        Pic(glass, F(-0.325f, 1.0f, 0f), -fwd, up, 0.62f, 1.62f, "white");
        Kit.AddBox(dine, F(-0.345f, 0.8f, 0.27f), new Vector3(0.025f, 0.45f, 0.025f), Chrome, shopYaw);
        GlowAt(bulbs, F(-0.33f, 0.09f, 0.15f), 0.005f, 0.03f, 0.1f, new Color(1f, 0.15f, 0.1f), 0.1f);
        GlowAt(bulbs, F(0.05f, 1.77f, 0f), 0.06f, 0.02f, 0.6f, Cool, 0.3f);
    }

    // ================= the counter =================
    void Counter()
    {
        float z = CounterZ, zb = CounterBack;
        // the raised display end, its planked front, a back shelf; the lower till end with an overhanging top
        TBox(dine, (-IX - 1.6f) * 0.5f, 0f, (z + zb) * 0.5f, IX - 1.6f, 1.0f, z - zb, "wood_dark", B(78, 50, 40));
        TBox(dine, (-IX - 1.6f) * 0.5f, 1.0f, (z + zb) * 0.5f + 0.06f, IX - 1.6f, 0.05f, z - zb + 0.12f, "wood_dark", B(88, 46, 36), null, 0f, true);
        TBox(dine, (-IX - 1.6f) * 0.5f, 1.05f, zb + 0.175f, IX - 1.6f, 0.25f, 0.35f, "wood_dark", B(78, 50, 40));
        TBox(dine, (-1.6f + 0.2f) * 0.5f, 0f, (z + zb) * 0.5f, 1.8f, 0.9f, z - zb, "wood_dark", B(78, 50, 40));
        TBox(dine, (-1.6f + 0.2f) * 0.5f, 0.9f, (z + zb) * 0.5f + 0.06f, 1.8f, 0.05f, z - zb + 0.12f, "wood_dark", Scale(Espresso, 0.8f), null, 0f, true);
        Tiled(dine, S((-IX - 1.6f) * 0.5f, 0.5f, z + 0.004f), -rgt, up, IX - 1.6f, 1.0f, "counter_planks");
        Tiled(dine, S((-1.6f + 0.2f) * 0.5f, 0.45f, z + 0.004f), -rgt, up, 1.8f, 0.9f, "counter_planks");
        Tiled(dine, S(0.2f + 0.004f, 0.45f, (z + zb) * 0.5f), fwd, up, z - zb, 0.9f, "counter_planks");
        foreach (float x in new[] { -1.6f, 0.2f }) TBox(dine, x + (x > 0f ? -0.05f : 0.05f), 0f, z + 0.01f, 0.1f, x > 0f ? 0.9f : 1.0f, 0.03f, "wood_dark", B(60, 40, 32));
        // the staff gap's flap, folded up against the partition
        TBox(dine, 0.95f, 0.9f, (z + zb) * 0.5f, 0.04f, 0.8f, z - zb, "wood_dark", Espresso);
        // the chalkboards and a lantern on the back shelf; the cake dome and the tip jar; a gingerbread house; the till
        foreach (var (x, name, frame) in new[] { (-3.75f, "chalk_special", B(95, 62, 40)), (-3.1f, "chalk_treats", B(120, 140, 145)) })
        {
            Vector3 foot = S(x, 1.3f, zb + 0.2f);
            Vector3 lean = (up * 0.97f - fwd * 0.2f).normalized;
            Strut(dine, foot, foot + lean * 0.62f, 0.5f, 0.03f, frame, rgt);
            Pic(dine, foot + lean * 0.31f + Vector3.Cross(lean, -rgt).normalized * 0.017f, -rgt, lean, 0.44f, 0.56f, name, null, false, 0.1f, 0.9f, 0f, 1f);
        }
        {
            Vector3 foot = S(-4.0f, 1.05f, z - 0.25f);
            Kit.AddBox(dine, foot, new Vector3(0.14f, 0.02f, 0.14f), Black, shopYaw);
            foreach (var (dx, dz) in new[] { (-0.06f, -0.06f), (0.06f, -0.06f), (-0.06f, 0.06f), (0.06f, 0.06f) }) Kit.AddBox(dine, foot + Dir(dx, 0.02f, dz), new Vector3(0.012f, 0.2f, 0.012f), Black, shopYaw);
            Kit.AddBox(dine, foot + up * 0.22f, new Vector3(0.15f, 0.03f, 0.15f), Black, shopYaw);
            Lathe(dine, foot + up * 0.25f, new[] { 0.06f, 0.001f }, new[] { 0f, 0.06f }, "white", Black, 4);
            GlowAt(bulbs, foot + up * 0.12f, 0.05f, 0.1f, 0.05f, Amber, 0.8f);
        }
        {
            Vector3 foot = S(-2.5f, 1.05f, z - 0.3f);
            Color mint = B(150, 205, 175);
            Lathe(dine, foot, new[] { 0.07f, 0.06f, 0.02f, 0.02f, 0.14f, 0.14f }, new[] { 0f, 0.015f, 0.03f, 0.12f, 0.13f, 0.15f }, "white", mint, 14, true);
            for (int k = 0; k < 7; k++) { float a = k * 0.9f, r = k == 0 ? 0f : 0.07f; Cyl(dine, foot + up * 0.15f + Dir(Mathf.Cos(a) * r, 0.012f * (k % 2), Mathf.Sin(a) * r), 0.03f, 0.012f, "pizza_top", "pizza_top", B(180, 130, 80), 10); }
            Lathe(glass, foot + up * 0.15f, new[] { 0.13f, 0.13f, 0.11f, 0.06f, 0.012f, 0.012f }, new[] { 0f, 0.1f, 0.15f, 0.18f, 0.19f, 0.22f }, "white", null, 12);
            Lathe(dine, S(-2.15f, 1.05f, z - 0.15f), new[] { 0.05f, 0.05f, 0.04f, 0.04f }, new[] { 0f, 0.13f, 0.14f, 0.16f }, "glass_plain", B(200, 210, 210), 12);      // the tip jar
            Cyl(dine, S(-2.15f, 1.09f, z - 0.15f), 0.0505f, 0.035f, "white", null, Red, 12);
            Vector3 g = S(-3.45f, 1.05f, z - 0.2f);
            Kit.AddBox(dine, g, new Vector3(0.18f, 0.12f, 0.14f), B(160, 100, 55), shopYaw);
            foreach (int sd in new[] { -1, 1 }) Strut(dine, g + Dir(0f, 0.12f, sd * 0.075f), g + Dir(0f, 0.2f, 0f), 0.2f, 0.015f, B(235, 232, 225), rgt);
        }
        {
            Vector3 foot = S(-1.9f, 1.05f, z - 0.45f);
            Cyl(dine, foot, 0.075f, 0.01f, "pipe_iron", "pipe_iron", Black, 12);
            Strut(dine, foot, foot + up * 0.12f, 0.03f, 0.03f, Black);
            Vector3 pad = foot + up * 0.2f;
            Strut(dine, pad - up * 0.07f - fwd * 0.03f, pad + up * 0.07f + fwd * 0.03f, 0.3f, 0.02f, Black, rgt);      // the tablet, turned to the staff
            Kit.AddBox(dine, S(-1.7f, 1.05f, z - 0.45f), new Vector3(0.07f, 0.11f, 0.03f), B(40, 40, 42), shopYaw + 20f);
        }
        // the orange C9 lights along both tops' front edges, swagged, and down the end to the floor
        var path = new List<Vector3>();
        for (int k = 0; k <= 30; k++)
        {
            float t = k / 30f, x = Mathf.Lerp(-IX + 0.05f, 0.2f, t), top = x < -1.6f ? 1.0f : 0.9f;
            path.Add(new Vector3(x, top - 0.03f - 0.05f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 9f)), z + 0.13f));
        }
        for (int k = 1; k <= 7; k++) path.Add(new Vector3(-IX + 0.04f, 1.0f - k * 0.14f, z + 0.12f));
        for (int k = 0; k + 1 < path.Count; k++)
        {
            Strut(dine, S(path[k].x, path[k].y, path[k].z), S(path[k + 1].x, path[k + 1].y, path[k + 1].z), 0.006f, 0.006f, B(20, 50, 25));
            Glow(bulbs, path[k].x, path[k].y - 0.03f, path[k].z + 0.01f, 0.035f, 0.06f, 0.035f, new Color(1f, 0.37f, 0.1f), k * 0.37f);
        }
        Lamp(S(-2.0f, 2.6f, -6.6f), "FFBE78", 4.5f, 2.0f);
        Lamp(S(-3.0f, 0.9f, -7.0f), "FF641E", 1.6f, 0.8f);
        Pic(dine, S(-2.4f, 0.014f, -6.8f), rgt, fwd, 1.6f, 0.9f, "mat_rubber", B(70, 72, 72));      // a black mat in front of it
    }

    void SkilletPizzas()
    {
        for (int k = 0; k < 3; k++)
        {
            var mb = new MeshBuilder();
            Kit.AddDisc(mb, Vector3.zero, 0.17f, 0.045f, B(26, 26, 28), 18);
            Kit.AddBox(mb, new Vector3(0.24f, 0.015f, 0f), new Vector3(0.18f, 0.03f, 0.04f), B(26, 26, 28));
            Kit.AddDisc(mb, new Vector3(0f, 0.045f, 0f), 0.145f, 0.012f, B(242, 184, 97), 18);
            for (int p = 0; p < 7; p++) { float a = p * 0.9f + k, r = 0.04f + (p % 3) * 0.03f; Kit.AddDisc(mb, new Vector3(Mathf.Cos(a) * r, 0.057f, Mathf.Sin(a) * r), 0.02f, 0.004f, B(158, 31, 20), 8); }
            Vector3 at = S(-1.35f + k * 0.55f, 0.97f, CounterZ - 0.28f);
            b.Item("Skillet Pizza " + (k + 1), mb.ToMesh(), bare, at.x, at.z, at.y, 1.5f, false);
        }
    }

    // ================= the kitchen =================
    void Kitchen()
    {
        float pz = PartZ + 0.05f;      // the cook line's wall face
        // --- the cook line: warming cabinet, range under the hood, sauces on a shelf, the hand sink ---
        {
            float x0 = -4.1f, x1 = -3.45f, xm = (x0 + x1) * 0.5f, dz = 0.75f;
            foreach (float x in new[] { x0 + 0.02f, x1 - 0.02f }) TBox(kit, x, 0f, pz + dz * 0.5f, 0.04f, 1.85f, dz, "stainless", Steel);
            TBox(kit, xm, 1.65f, pz + dz * 0.5f, x1 - x0, 0.2f, dz, "stainless", Steel);
            TBox(kit, xm, 0f, pz + dz * 0.5f, x1 - x0, 0.15f, dz, "stainless", Steel);
            for (int sh = 0; sh < 4; sh++)
            {
                float y = 0.17f + sh * 0.37f;
                Box(kit, xm, y, pz + 0.37f, 0.56f, 0.01f, 0.68f, Chrome);
                for (int n = 0; n < 4; n++) TBox(kit, xm, y + 0.012f + n * 0.05f, pz + 0.4f, 0.42f, 0.048f, 0.42f, "box_side", null, "logo_box", 0.42f);
            }
            Pic(glass, S(xm, 0.9f, pz + dz + 0.002f), -rgt, up, 0.57f, 1.48f, "white");
            Box(kit, xm, 1.75f, pz + dz + 0.002f, 0.6f, 0.06f, 0.004f, Red);
            Strut(kit, S(x1 - 0.06f, 0.6f, pz + dz + 0.03f), S(x1 - 0.06f, 1.2f, pz + dz + 0.03f), 0.02f, 0.02f, Chrome);
        }
        {
            float x0 = -3.35f, x1 = -2.45f, xm = (x0 + x1) * 0.5f, dz = 0.8f;
            TBox(kit, xm, 0f, pz + dz * 0.5f, x1 - x0, 0.9f, dz, "stainless", Steel);
            Pic(kit, S(xm, 0.42f, pz + dz + 0.003f), -rgt, up, 0.78f, 0.5f, "stainless", Scale(Steel, 0.85f));      // the oven door
            CylAlong(kit, S(x0 + 0.08f, 0.72f, pz + dz + 0.05f), S(x1 - 0.08f, 0.72f, pz + dz + 0.05f), 0.012f, "stainless", Chrome, 8);
            for (int k = 0; k < 6; k++) CylAlong(kit, S(x0 + 0.1f + k * 0.14f, 0.8f, pz + dz), S(x0 + 0.1f + k * 0.14f, 0.8f, pz + dz + 0.04f), 0.02f, "white", Red, 8);
            for (int g = 0; g < 4; g++)
            {
                float gx = xm + (g % 2 == 0 ? -0.22f : 0.22f), gz = pz + (g < 2 ? 0.25f : 0.58f);
                for (int k = 0; k < 4; k++) Box(kit, gx - 0.15f + k * 0.1f, 0.9f, gz, 0.02f, 0.025f, 0.3f, B(25, 25, 25));
                for (int k = 0; k < 3; k++) Box(kit, gx, 0.9f, gz - 0.12f + k * 0.12f, 0.3f, 0.025f, 0.02f, B(25, 25, 25));
            }
            Cyl(kit, S(xm - 0.22f, 0.925f, pz + 0.58f), 0.14f, 0.04f, "pipe_iron", "pipe_iron", B(30, 30, 32), 14);      // a frying pan
            CylAlong(kit, S(xm - 0.09f, 0.95f, pz + 0.62f), S(xm + 0.15f, 0.97f, pz + 0.76f), 0.012f, "pipe_iron", B(30, 30, 32), 6);
        }
        {
            float x0 = -3.6f, x1 = -1.6f, xm = (x0 + x1) * 0.5f, z1 = pz + 1.0f;
            TBox(kit, xm, 2.0f, (pz + z1) * 0.5f, x1 - x0, 0.6f, z1 - pz, "stainless", Steel);
            Box(kit, xm, 1.97f, z1 - 0.02f, x1 - x0, 0.04f, 0.04f, Chrome);
            for (int k = 0; k < 3; k++)
                Pic(kit, S(x0 + 0.35f + k * 0.65f, 1.99f, (pz + z1) * 0.5f - 0.1f), -rgt, (fwd * 0.87f + up * 0.5f).normalized, 0.6f, 0.45f, "stainless_baffle", Tan);
            foreach (float x in new[] { -3.0f, -2.2f }) Cyl(kit, S(x, 1.88f, (pz + z1) * 0.5f + 0.2f), 0.01f, 0.1f, "stainless", null, Chrome, 6);
            TBox(kit, -2.6f, 2.6f, pz + 0.2f, 0.8f, CK - 2.6f, 0.4f, "white", B(28, 28, 30));      // its duct to the ceiling
            Lamp(S(-2.6f, 1.9f, -9.9f), "FFEBCD", 2.5f, 1.4f);
            Glow(bulbs, -2.6f, 1.985f, (pz + z1) * 0.5f + 0.25f, 0.5f, 0.01f, 0.06f, Cool, 0.4f);
        }
        {
            float x0 = -2.3f, x1 = -1.05f;
            TBox(kit, (x0 + x1) * 0.5f, 1.45f, pz + 0.15f, x1 - x0, 0.03f, 0.3f, "stainless", Steel);
            Color[] sauce = { B(200, 40, 30), B(240, 240, 235), B(230, 190, 40), B(110, 70, 40), B(200, 40, 30), B(240, 240, 235) };
            for (int k = 0; k < 6; k++)
                Lathe(kit, S(x0 + 0.1f + k * 0.09f, 1.48f, pz + 0.1f), new[] { 0.03f, 0.03f, 0.02f, 0.004f }, new[] { 0f, 0.17f, 0.2f, 0.24f }, "white", sauce[k], 8);
            for (int k = 0; k < 4; k++)
                for (int n = 0; n < 2; n++)
                    Cyl(kit, S(x0 + 0.68f + k * 0.13f, 1.48f + n * 0.1f, pz + 0.18f), 0.055f, 0.095f, "white", "white", n == 1 ? B(120, 180, 90) : B(240, 240, 235), 10);
            Lathe(kit, S(x1 - 0.06f, 1.48f, pz + 0.08f), new[] { 0.03f, 0.025f, 0.025f, 0.015f }, new[] { 0f, 0.05f, 0.18f, 0.22f }, "wood_dark", B(50, 32, 24), 8, true);
        }
        {
            float xm = -0.8f;
            TBox(kit, xm, 0.72f, pz + 0.175f, 0.4f, 0.13f, 0.35f, "stainless", Steel);
            Pic(kit, S(xm, 0.852f, pz + 0.19f), rgt, fwd, 0.3f, 0.24f, "stainless", Scale(Steel, 0.55f));
            TBox(kit, xm, 0.85f, pz + 0.01f, 0.4f, 0.25f, 0.02f, "stainless", Steel);
            Strut(kit, S(xm, 0.95f, pz + 0.04f), S(xm, 1.15f, pz + 0.08f), 0.02f, 0.02f, Chrome);
            Strut(kit, S(xm, 1.15f, pz + 0.08f), S(xm, 1.08f, pz + 0.2f), 0.02f, 0.02f, Chrome);
            TBox(kit, xm, 1.34f, pz + 0.06f, 0.28f, 0.33f, 0.12f, "white", B(25, 25, 25));
            CylAlong(kit, S(xm, 0.72f, pz + 0.17f), S(xm, 0.4f, pz + 0.17f), 0.025f, "white", B(235, 235, 232), 8);
            CylAlong(kit, S(xm, 0.4f, pz + 0.17f), S(xm, 0.4f, pz + 0.02f), 0.025f, "white", B(235, 235, 232), 8);
        }
        // --- the back kitchen ---
        float bz = IZ;
        {
            // the deck ovens on their stand, the cylinder above, a peel on top, a bin beneath, towels
            float x0 = -3.9f, x1 = -2.7f, xm = (x0 + x1) * 0.5f;
            TBox(kit, xm, 0.62f, bz + 0.375f, x1 - x0, 0.03f, 0.75f, "stainless", Steel);
            TBox(kit, xm, 0.15f, bz + 0.375f, x1 - x0 - 0.06f, 0.02f, 0.7f, "stainless", Steel);
            foreach (var (dx, dz) in new[] { (-0.57f, 0.03f), (0.57f, 0.03f), (-0.57f, 0.72f), (0.57f, 0.72f) }) Box(kit, xm + dx, 0f, bz + dz, 0.04f, 0.62f, 0.04f, Steel);
            for (int u = 0; u < 2; u++)
            {
                float y = 0.65f + u * 0.62f, ox = xm - 0.06f, ow = 1.07f;
                TBox(kit, ox, y, bz + 0.35f, ow, 0.62f, 0.7f, "stainless", Steel);
                for (int k = 0; k < 2; k++)
                {
                    float dy = y + 0.155f + k * 0.31f;
                    Pic(kit, S(ox, dy, bz + 0.703f), -rgt, up, 0.98f, 0.3f, "oven_deck", null, false, 0f, 1f, 0.12f, 0.88f);
                }
                Box(kit, ox, y + 0.6f, bz + 0.7f, ow, 0.02f, 0.02f, Scale(Steel, 0.8f));
            }
            Lamp(S(-3.3f, 1.2f, -13.1f), "FF7828", 1.8f, 0.6f);
            Cyl(kit, S(xm, 2.1f, bz + 0.12f), 0.1f, 0.6f, "fire_canister", "stainless", B(205, 50, 60), 12);
            Box(kit, xm, 2.35f, bz, 0.24f, 0.04f, 0.23f, B(40, 40, 42));
            Disc(kit, S(xm, 2.55f, bz + 0.225f), fwd, up, 0.035f, "clock", null, 12);
            CylAlong(kit, S(xm + 0.1f, 2.6f, bz + 0.12f), S(xm + 0.4f, 2.8f, bz + 0.04f), 0.01f, "white", B(120, 120, 122), 6);
            CylAlong(kit, S(xm + 0.4f, 2.8f, bz + 0.04f), S(xm + 0.4f, CK, bz + 0.04f), 0.01f, "white", B(120, 120, 122), 6);
            Box(kit, xm + 0.4f, 2.75f, bz + 0.05f, 0.1f, 0.1f, 0.06f, B(130, 130, 132));
            Strut(kit, S(xm - 0.3f, 1.9f, bz + 0.35f), S(xm + 0.3f, 1.9f, bz + 0.35f), 0.35f, 0.012f, B(190, 150, 100));      // the peel
            Strut(kit, S(xm + 0.3f, 1.9f, bz + 0.35f), S(xm + 0.85f, 1.9f, bz + 0.38f), 0.035f, 0.025f, B(190, 150, 100));
            TBox(kit, xm, 0f, bz + 0.38f, 0.5f, 0.6f, 0.4f, "white", B(28, 28, 28));
            Box(kit, x1 - 0.1f, 0.48f, bz + 0.76f, 0.3f, 0.15f, 0.04f, B(225, 220, 205));
        }
        {
            // the two-bowl sink with its shelves of cleaning things
            float x0 = -2.6f, x1 = -1.3f, xm = (x0 + x1) * 0.5f;
            TBox(kit, xm, 0.85f, bz + 0.33f, x1 - x0, 0.05f, 0.65f, "stainless", Steel);
            foreach (float x in new[] { x0 + 0.33f, x0 + 0.88f }) Pic(kit, S(x, 0.902f, bz + 0.35f), rgt, fwd, 0.48f, 0.48f, "stainless", Scale(Steel, 0.5f));
            TBox(kit, xm, 0.9f, bz + 0.02f, x1 - x0, 0.25f, 0.04f, "stainless", Steel);
            foreach (var (dx, dz) in new[] { (-0.6f, 0.05f), (0.6f, 0.05f), (-0.6f, 0.6f), (0.6f, 0.6f) }) Box(kit, xm + dx, 0f, bz + dz, 0.04f, 0.85f, 0.04f, Steel);
            Strut(kit, S(xm - 0.2f, 1.0f, bz + 0.05f), S(xm - 0.2f, 1.3f, bz + 0.05f), 0.03f, 0.03f, Chrome);
            Strut(kit, S(xm - 0.2f, 1.3f, bz + 0.05f), S(xm - 0.2f, 1.25f, bz + 0.35f), 0.025f, 0.025f, Chrome);
            foreach (float x in new[] { x0 + 0.33f, x0 + 0.88f })
            {
                CylAlong(kit, S(x, 0.85f, bz + 0.35f), S(x, 0.5f, bz + 0.35f), 0.025f, "white", B(240, 240, 238), 8);
                CylAlong(kit, S(x, 0.5f, bz + 0.35f), S(x, 0.5f, bz + 0.02f), 0.025f, "white", B(240, 240, 238), 8);
            }
            foreach (float x in new[] { x0 + 0.35f, x1 - 0.35f }) TBox(kit, x, 1.55f, bz + 0.15f, 0.6f, 0.025f, 0.3f, "stainless", Steel);
            Lathe(kit, S(x0 + 0.2f, 1.575f, bz + 0.15f), new[] { 0.07f, 0.07f, 0.03f, 0.02f }, new[] { 0f, 0.22f, 0.26f, 0.3f }, "white", B(240, 240, 240), 4, true);
            for (int k = 0; k < 2; k++) Lathe(kit, S(x0 + 0.4f + k * 0.1f, 1.575f, bz + 0.15f), new[] { 0.035f, 0.035f, 0.015f, 0.015f }, new[] { 0f, 0.16f, 0.2f, 0.25f }, "white", k == 0 ? B(60, 120, 200) : B(240, 240, 240), 8);
            Lathe(kit, S(x1 - 0.3f, 1.575f, bz + 0.15f), new[] { 0.09f, 0.11f, 0.11f }, new[] { 0f, 0.16f, 0.17f }, "white", B(180, 140, 220), 12);
        }
        {
            // the pan shelf on the partition's back face: stacked pans on it, a strainer and five frying pans hanging
            float x0 = -3.8f, x1 = -1.9f, zw = PartZ - 0.05f;
            TBox(kit, (x0 + x1) * 0.5f, 1.75f, zw - 0.15f, x1 - x0, 0.03f, 0.3f, "stainless", Steel);
            foreach (float x in new[] { x0 + 0.1f, x1 - 0.1f }) Strut(kit, S(x, 1.75f, zw), S(x, 1.55f, zw - 0.02f), 0.03f, 0.03f, Steel);
            CylAlong(kit, S(x0, 1.63f, zw - 0.08f), S(x1, 1.63f, zw - 0.08f), 0.01f, "stainless", Chrome, 6);
            for (int k = 0; k < 6; k++)
            {
                int n = 3 + (k * 5) % 6;
                for (int m = 0; m < n; m++)
                    Lathe(kit, S(x0 + 0.17f + k * 0.3f, 1.78f + m * 0.06f, zw - 0.15f), new[] { 0.1f, 0.125f, 0.125f }, new[] { 0f, 0.07f, 0.075f }, "stainless", Scale(Chrome, 0.95f + 0.05f * (m % 2)), 10);
            }
            Color[] pans = { B(140, 95, 60), B(185, 185, 182), B(120, 80, 50), B(140, 95, 60), B(185, 185, 182) };
            for (int k = 0; k < 5; k++)
            {
                float r = 0.13f + 0.02f * (k % 3), x = x0 + 0.45f + k * 0.3f;
                Vector3 hook = S(x, 1.63f, zw - 0.08f), centre = S(x, 1.63f - 0.25f - r, zw - 0.1f - k * 0.012f);
                Strut(kit, hook, centre + up * r, 0.025f, 0.012f, pans[k], rgt);
                Disc(kit, centre, -fwd, up, r, "copper", pans[k], 18);
                Ring(kit, centre - fwd * 0.02f, fwd, r, 0.012f, Scale(pans[k], 0.8f), 18);
            }
            Disc(kit, S(x0 + 0.15f, 1.3f, zw - 0.1f), -fwd, up, 0.14f, "stainless_baffle", B(160, 160, 160), 16);
        }
        {
            // the three-bowl sink along the left wall, a green wire shelf of sheet pans over it, a cutting board
            float z0 = -10.8f, z1 = -12.8f, zm = (z0 + z1) * 0.5f, xw = -IX;
            TBox(kit, xw + 0.3f, 0.85f, zm, 0.6f, 0.05f, z0 - z1, "stainless", Steel);
            for (int k = 0; k < 3; k++) Pic(kit, S(xw + 0.32f, 0.902f, z0 - 0.5f - k * 0.5f), rgt, fwd, 0.45f, 0.45f, "stainless", Scale(Steel, 0.5f));
            TBox(kit, xw + 0.02f, 0.9f, zm, 0.04f, 0.25f, z0 - z1, "stainless", Steel);
            foreach (float z in new[] { z0 - 0.05f, z1 + 0.05f, zm }) { Box(kit, xw + 0.05f, 0f, z, 0.04f, 0.85f, 0.04f, Steel); Box(kit, xw + 0.55f, 0f, z, 0.04f, 0.85f, 0.04f, Steel); }
            for (int k = 0; k < 3; k++)
            {
                float z = z0 - 0.5f - k * 0.5f;
                CylAlong(kit, S(xw + 0.32f, 0.85f, z), S(xw + 0.32f, 0.45f, z), 0.025f, "white", B(240, 240, 238), 8);
                CylAlong(kit, S(xw + 0.32f, 0.45f, z), S(xw + 0.02f, 0.45f, z), 0.025f, "white", B(240, 240, 238), 8);
            }
            Strut(kit, S(xw + 0.06f, 1.2f, zm), S(xw + 0.06f, 1.38f, zm + 0.2f), 0.03f, 0.03f, Chrome);
            Box(kit, xw + 0.2f, 1.6f, zm, 0.4f, 0.02f, z0 - z1 - 0.2f, B(60, 110, 90));
            for (int k = 0; k < 6; k++) Box(kit, xw + 0.21f, 1.62f + k * 0.025f, zm, 0.33f, 0.012f, 0.45f, B(190, 190, 192));
            Strut(kit, S(xw + 0.6f, 0.9f, z1 + 0.2f), S(xw + 0.6f, 1.38f, z1 + 0.3f), 0.4f, 0.015f, B(240, 240, 235), fwd);
        }
        Cyl(kit, S(1.95f, 0f, -11.2f), 0.22f, 0.75f, "white", null, B(28, 28, 28), 14);      // a bin
        {
            // the wire rack of countertop ovens against the back wall, boxes under and beside it, a speed rack
            float x0 = -0.2f, x1 = 1.0f, xm = (x0 + x1) * 0.5f;
            foreach (var (dx, dz) in new[] { (-0.59f, 0.02f), (0.59f, 0.02f), (-0.59f, 0.58f), (0.59f, 0.58f) }) Cyl(kit, S(xm + dx, 0f, bz + dz), 0.0125f, 1.9f, "stainless", null, Chrome, 6);
            foreach (float y in new[] { 0.15f, 0.95f, 1.45f, 1.9f })
            {
                Box(kit, xm, y, bz + 0.3f, 1.2f, 0.012f, 0.6f, Scale(Chrome, 0.9f));
                for (int k = 0; k <= 12; k++) Box(kit, x0 + k * 0.1f, y + 0.012f, bz + 0.3f, 0.005f, 0.005f, 0.6f, Chrome);
            }
            for (int k = 0; k < 2; k++)
            {
                float ox = x0 + 0.3f + k * 0.6f;
                Color body = k == 0 ? B(35, 35, 38) : Steel;
                TBox(kit, ox, 0.962f, bz + 0.3f, 0.55f, 0.45f, 0.5f, k == 0 ? "white" : "stainless", body);
                for (int n = 0; n < 2; n++)
                {
                    Pic(kit, S(ox, 0.962f + 0.12f + n * 0.21f, bz + 0.553f), -rgt, up, 0.52f, 0.2f, "oven_counter", null, false, 0f, 1f, 0.1f, 0.9f);
                }
                TBox(kit, ox, 1.462f, bz + 0.3f, 0.55f, 0.28f, 0.45f, "stainless", Steel);
                Pic(kit, S(ox, 1.462f + 0.14f, bz + 0.528f), -rgt, up, 0.52f, 0.2f, "oven_counter", null, false, 0f, 1f, 0.1f, 0.9f);
            }
            Box(kit, x1 - 0.1f, 0.85f, bz + 0.6f, 0.25f, 0.12f, 0.03f, B(225, 220, 205));
            var random = new System.Random(9);
            for (int s = 0; s < 6; s++)
            {
                float sx = s < 3 ? x0 + 0.21f + s * 0.39f : 2.35f, sz = s < 3 ? bz + 0.3f : bz + 0.9f + (s - 3) * 0.45f;
                float floor = s < 3 ? 0.165f : 0f;
                int n = s < 3 ? 15 + random.Next(2) : 12 + random.Next(7);
                for (int m = 0; m < n; m++)
                {
                    float jx = ((float)random.NextDouble() - 0.5f) * 0.02f, jz = ((float)random.NextDouble() - 0.5f) * 0.02f;
                    TBox(kit, sx + jx, floor + m * 0.045f, sz + jz, 0.41f, 0.044f, 0.41f, "box_side", null, "logo_box", 0.41f);
                }
            }
            float rx = 1.6f;
            foreach (var (dx, dz) in new[] { (-0.27f, 0.02f), (0.27f, 0.02f), (-0.27f, 0.63f), (0.27f, 0.63f) }) Box(kit, rx + dx, 0f, bz + dz, 0.025f, 1.75f, 0.025f, B(200, 200, 204));
            for (int k = 0; k < 15; k++) foreach (int sd in new[] { -1, 1 }) Box(kit, rx + sd * 0.26f, 0.2f + k * 0.1f, bz + 0.33f, 0.02f, 0.01f, 0.6f, B(200, 200, 204));
            for (int k = 0; k < 5; k++) Box(kit, rx, 0.21f + k * 0.3f, bz + 0.33f, 0.5f, 0.01f, 0.62f, B(185, 186, 190));
        }
        {
            // the prep table with flour sacks under it, dough trays, a peel and a cutter; the tube light over it
            float x = 0.8f, z = -11.8f;
            TBox(kit, x, 0.86f, z, 1.5f, 0.04f, 0.75f, "stainless", Steel);
            TBox(kit, x, 0.18f, z, 1.44f, 0.02f, 0.69f, "stainless", Steel);
            foreach (var (dx, dz) in new[] { (-0.7f, -0.33f), (0.7f, -0.33f), (-0.7f, 0.33f), (0.7f, 0.33f) }) Box(kit, x + dx, 0f, z + dz, 0.04f, 0.86f, 0.04f, Steel);
            for (int k = 0; k < 3; k++) Box(kit, x - 0.4f + (k % 2) * 0.05f, 0.2f + k * 0.12f, z, 0.4f, 0.12f, 0.25f, B(235, 228, 205));
            for (int k = 0; k < 4; k++) Box(kit, x + 0.35f, 0.2f + k * 0.08f, z, 0.45f, 0.08f, 0.65f, B(225, 220, 205));
            for (int k = 0; k < 3; k++) Box(kit, x - 0.35f, 0.9f + k * 0.08f, z, 0.45f, 0.075f, 0.65f, Scale(B(225, 220, 205), 0.97f + 0.03f * k));
            Strut(kit, S(x + 0.2f, 0.905f, z - 0.1f), S(x + 0.6f, 0.905f, z - 0.1f), 0.35f, 0.012f, B(190, 150, 100));
            Strut(kit, S(x + 0.6f, 0.905f, z - 0.1f), S(x + 1.1f, 0.91f, z - 0.15f), 0.035f, 0.025f, B(190, 150, 100));
            Disc(kit, S(x + 0.3f, 0.904f, z + 0.25f), up, fwd, 0.05f, "stainless", Chrome, 12);
            Strut(kit, S(x + 0.3f, 0.92f, z + 0.25f), S(x + 0.42f, 0.92f, z + 0.25f), 0.025f, 0.02f, Black);
            Lamp(S(0f, 2.55f, -12f), "EBF2FF", 6.0f, 1.1f);
            Box(kit, x, CK - 0.08f, z, 1.2f, 0.08f, 0.3f, B(235, 235, 232));
            Glow(bulbs, x, CK - 0.085f, z, 1.1f, 0.01f, 0.24f, Cool, 0.6f);
        }
        // black mats in front of the range, the ovens and the sinks; conduit up the walls
        foreach (var (x, z, w, d) in new[] { (-2.9f, -9.1f, 1.5f, 0.9f), (-3.3f, -12.5f, 1.2f, 0.9f), (-1.95f, -12.75f, 1.3f, 0.8f), (-3.25f, -11.8f, 0.9f, 1.5f) })
            Pic(kit, S(x, 0.012f, z), rgt, fwd, w, d, "mat_rubber");
        foreach (float x in new[] { -0.4f, 2.3f })
        {
            CylAlong(kit, S(x, 1.2f, IZ + 0.03f), S(x, CK, IZ + 0.03f), 0.012f, "white", B(130, 130, 132), 6);
            Box(kit, x, 1.15f, IZ + 0.04f, 0.1f, 0.1f, 0.05f, B(140, 140, 142));
        }
    }

    // ================= the back hall =================
    void BackHall()
    {
        float xm = (HallX + IX) * 0.5f;
        // the end door: pale blue-grey, six raised panels, a cream casing, a lever
        Color door = B(185, 192, 198);
        TBox(hall, xm, 0f, IZ + 0.02f, 0.9f, 2.05f, 0.04f, "white", door);
        Pic(hall, S(xm, 1.025f, IZ + 0.041f), -rgt, up, 0.9f, 2.05f, "door_white", door);
        foreach (float x in new[] { xm - 0.495f, xm + 0.495f }) TBox(hall, x, 0f, IZ + 0.02f, 0.09f, 2.1f, 0.04f, "trim_white", Cream);
        TBox(hall, xm, 2.05f, IZ + 0.02f, 1.08f, 0.09f, 0.04f, "trim_white", Cream);
        Strut(hall, S(xm - 0.33f, 1.0f, IZ + 0.07f), S(xm - 0.2f, 1.0f, IZ + 0.07f), 0.02f, 0.02f, Chrome);
        // the tube light down the ceiling, the exit sign at the end, an extinguisher, the side door on the partition
        Box(hall, xm, CK - 0.06f, -10.8f, 0.1f, 0.06f, 1.2f, B(235, 235, 232));
        Glow(bulbs, xm, CK - 0.065f, -10.8f, 0.06f, 0.01f, 1.15f, Cool, 0.1f);
        Lamp(S(xm, 2.45f, -10.8f), "EBF2FF", 5.0f, 0.8f);
        Box(hall, xm, 2.8f, IZ + 0.25f, 0.02f, CK - 2.8f, 0.02f, B(240, 240, 238));
        TBox(hall, xm, 2.62f, IZ + 0.25f, 0.3f, 0.18f, 0.06f, "white", B(240, 240, 238));
        Pic(hall, S(xm, 2.71f, IZ + 0.281f), -rgt, up, 0.28f, 0.14f, "exit_sign", null, false, 0f, 1f, 0.25f, 0.75f);
        Pic(hall, S(xm, 2.71f, IZ + 0.219f), rgt, up, 0.28f, 0.14f, "exit_sign", null, false, 0f, 1f, 0.25f, 0.75f);
        Lamp(S(xm, 2.7f, -13.5f), "FF281E", 1.4f, 0.7f);
        Cyl(hall, S(HallX + 0.12f, 0.9f, -12.2f), 0.06f, 0.45f, "fire_canister", "white", B(205, 40, 40), 10);
        Box(hall, HallX + 0.06f, 1.0f, -12.2f, 0.02f, 0.25f, 0.06f, B(40, 40, 42));
        {
            float z0 = -8.6f, z1 = -9.5f, zm = (z0 + z1) * 0.5f;
            Color white = B(235, 228, 210);
            TBox(hall, HallX + 0.07f, 0f, zm, 0.04f, 2.1f, z0 - z1, "white", white);
            Pic(hall, S(HallX + 0.091f, 1.05f, zm), fwd, up, z0 - z1, 2.1f, "door_white", white);
            foreach (float z in new[] { z0 + 0.045f, z1 - 0.045f }) TBox(hall, HallX + 0.06f, 0f, z, 0.03f, 2.15f, 0.09f, "trim_white", Cream);
            Strut(hall, S(HallX + 0.1f, 1.0f, z1 + 0.1f), S(HallX + 0.1f, 1.0f, z1 + 0.22f), 0.02f, 0.02f, Chrome);
            Box(hall, HallX + 0.1f, 1.95f, z0 - 0.2f, 0.07f, 0.06f, 0.3f, Black);
            Pic(hall, S(HallX + 0.093f, 1.55f, zm), fwd, up, 0.18f, 0.08f, "white", B(240, 240, 240));
        }
        // a mop bucket, a broom against the wall, two flour sacks
        {
            Vector3 foot = S(IX - 0.3f, 0f, -11.3f);
            Kit.AddBox(hall, foot, new Vector3(0.4f, 0.35f, 0.3f), B(230, 190, 40), shopYaw);
            Strut(hall, foot + up * 0.3f, foot + up * 1.3f + Dir(-0.1f, 0f, 0.15f), 0.025f, 0.025f, B(170, 130, 80));
            Vector3 broom = S(IX - 0.06f, 0f, -12.6f);
            Strut(hall, broom + up * 0.2f + Dir(-0.25f, 0f, 0f), broom + up * 1.45f, 0.025f, 0.025f, B(170, 130, 80));
            Strut(hall, broom + Dir(-0.32f, 0f, -0.12f), broom + Dir(-0.32f, 0f, 0.12f) + up * 0.02f, 0.06f, 0.2f, B(60, 60, 62));
            for (int k = 0; k < 2; k++) Kit.AddBox(hall, S(IX - 0.3f, k * 0.14f, -9.0f - k * 0.03f), new Vector3(0.42f, 0.14f, 0.62f), B(235, 228, 205), shopYaw + k * 6f);
        }
    }

    // ================= the terrace =================
    void Terrace()
    {
        float zA = ShopFront - TerraceFrom, zB = ShopFront - TerraceTo, xs = TerraceHalf;      // z +17.5..-16, x -13..13
        float zm = (zA + zB) * 0.5f, len = zA - zB;
        // the deck: a granite slab that takes snow; a swept path to the door; the patio and the cafe floor paved
        Kit.AddBox(shell, S(0f, -0.6f, zm), new Vector3(2f * xs, 0.6f, len), Granite, shopYaw);
        Tiled(deck, S(-2.4f, 0.006f, zA * 0.5f), rgt, fwd, 1.6f, zA, "paving");
        Tiled(deck, S(-8.75f, 0.006f, -6.5f), rgt, fwd, 7.5f, 13f, "paving");
        Tiled(deck, S(-8.0f, 0.006f, 8.25f), rgt, fwd, 6.0f, 9.5f, "paving");
        Foundation(zA, zB, xs);
        // railings round the three open sides, with lights along them
        var rails = new[] { (zA, -xs, zB, -xs), (zA, xs, zB, xs), (zB, -xs, zB, xs) };
        int railLights = 0;
        foreach (var (za, xa, zb, xb) in rails)
        {
            float l = Mathf.Sqrt((za - zb) * (za - zb) + (xa - xb) * (xa - xb));
            int posts = Mathf.CeilToInt(l / 2f);
            var tops = new List<Vector3>();
            for (int k = 0; k <= posts; k++)
            {
                float t = (float)k / posts;
                Vector3 p = S(Mathf.Lerp(xa, xb, t), 0f, Mathf.Lerp(za, zb, t));
                Kit.AddBox(shell, p, new Vector3(0.12f, 1.12f, 0.12f), Iron, shopYaw);
                Kit.AddBox(shell, p + up * 1.12f, new Vector3(0.16f, 0.06f, 0.16f), Iron, shopYaw);
                if (k < posts)
                    for (int n = 1; n < 6; n++)
                    {
                        float tb = (k + n / 6f) / posts;
                        Kit.AddBox(shell, S(Mathf.Lerp(xa, xb, tb), 0.08f, Mathf.Lerp(za, zb, tb)), new Vector3(0.03f, 0.92f, 0.03f), Iron, shopYaw);
                    }
                if (k % 2 == 0) tops.Add(p + up * 1.28f);
            }
            Strut(shell, S(xa, 1.06f, za), S(xb, 1.06f, zb), 0.09f, 0.09f, Iron);
            Strut(shell, S(xa, 0.08f, za), S(xb, 0.08f, zb), 0.05f, 0.05f, Iron);
            if (tops.Count > 1) Kit.LightString(b, "Terrace Lights " + (++railLights), tops, 0.25f, 0.45f, false, false, 120 + railLights);
        }
        // timber planters of small firs along the patio's railing
        foreach (float z in new[] { 13.5f, 9.5f, 3.5f, -9.75f })
        {
            float x = -(xs - 0.45f);
            Kit.AddBox(shell, S(x, 0f, z), new Vector3(0.45f, 0.5f, 1.2f), Timber, shopYaw);
            var pines = new MeshBuilder();
            for (int k = 0; k < 3; k++) Kit.AddPine(pines, S(x, 0.45f, z - 0.4f + k * 0.4f), 0.8f + 0.1f * k, 70 + (int)z + k);
            b.MeshPiece("Pizzeria Planter " + (int)(z * 10f), pines.ToMesh(), snowy, false);
        }
        // cafe tables with bistro chairs, three umbrellas open, three closed
        int t2 = 0;
        foreach (var (x, z) in new[] { (-6.5f, 5.5f), (-9.5f, 8f), (-6.5f, 11f), (-7f, -3f), (-10f, -6f), (-7f, -9f) })
        {
            bool open = (t2++ % 2) == 0;
            Vector3 c = S(x, 0f, z);
            Kit.AddDisc(shell, c, 0.225f, 0.03f, Iron, 14);
            Kit.AddBox(shell, c, new Vector3(0.06f, 0.71f, 0.06f), Iron, shopYaw);
            Kit.AddDisc(shell, c + up * 0.71f, 0.35f, 0.03f, Iron, 18);
            Kit.AddBox(shell, c + up * 0.74f, new Vector3(0.04f, 1.7f, 0.04f), B(200, 200, 196), shopYaw);
            if (open)
            {
                Vector3 apex = c + up * 2.6f;
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                    Vector3 e0 = c + up * 2.15f + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 1.2f, e1 = c + up * 2.15f + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 1.2f;
                    Color panel = k % 2 == 0 ? B(22, 22, 24) : Tan;
                    Vector3 outward = (e0 + e1) * 0.5f - c - up * 2.15f;
                    QuadOut(shell, e0, e1, apex, apex, up, panel);
                    QuadOut(shell, e0, e1, apex, apex, -up, Scale(panel, 0.6f));
                    QuadOut(shell, e0, e1, e1 - up * 0.15f, e0 - up * 0.15f, outward, panel);
                    QuadOut(shell, e0, e1, e1 - up * 0.15f, e0 - up * 0.15f, -outward, Scale(panel, 0.6f));
                    Strut(shell, c + up * 1.85f, Vector3.Lerp(e0, apex, 0.15f), 0.015f, 0.015f, Iron);
                }
                Kit.AddDisc(shell, apex, 0.05f, 0.08f, Iron, 8);
            }
            else
            {
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                    Vector3 e0 = c + up * 1.25f + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.11f, e1 = c + up * 1.25f + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.11f;
                    QuadOut(shell, e0, e1, c + up * 2.4f, c + up * 2.4f, (e0 + e1) * 0.5f - c - up * 1.25f, k % 2 == 0 ? B(22, 22, 24) : Tan);
                }
            }
            int chairs = (t2 % 3) == 0 ? 3 : 2;
            for (int k = 0; k < chairs; k++)
            {
                float a = k * 2f * Mathf.PI / chairs + x;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f, p = c + off;
                float facing = Mathf.Atan2(-off.x, -off.z) * Mathf.Rad2Deg;
                Quaternion q = Quaternion.Euler(0f, facing, 0f);
                foreach (var (dx, dz) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) }) Strut(shell, p + q * new Vector3(dx, 0f, dz), p + q * new Vector3(dx * 0.95f, 0.45f, dz * 0.95f), 0.025f, 0.025f, Iron);
                Kit.AddBox(shell, p + up * 0.43f, new Vector3(0.42f, 0.03f, 0.42f), Iron, facing);
                Vector3 back = p + q * new Vector3(0f, 0f, -0.2f);
                foreach (int sd in new[] { -1, 1 }) Strut(shell, back + q * new Vector3(sd * 0.19f, 0.45f, 0f), back + q * new Vector3(sd * 0.17f, 0.86f, -0.03f), 0.02f, 0.02f, Iron);
                Ring(shell, back + up * 0.73f + q * new Vector3(0f, 0f, -0.02f), q * Vector3.forward, 0.14f, 0.018f, Iron, 10);
                Seat("Terrace Chair", p + up * 0.55f, facing);
            }
        }
        // festoon lights over the side patio, on black poles
        {
            var poles = new[] { new Vector2(-5f, 0f), new Vector2(-12.5f, 0f), new Vector2(-12.5f, -6.5f), new Vector2(-5f, -6.5f), new Vector2(-5f, -13f), new Vector2(-12.5f, -13f) };
            foreach (Vector2 p in poles) { Kit.AddBox(shell, S(p.x, 0f, p.y), new Vector3(0.08f, 3.2f, 0.08f), Iron, shopYaw); Kit.AddBox(shell, S(p.x, 0f, p.y), new Vector3(0.2f, 0.06f, 0.2f), Iron, shopYaw); }
            var warm = new[] { new Color(1f, 0.84f, 0.63f) };
            int[][] runs = { new[] { 0, 2, 3, 5 }, new[] { 1, 3, 2, 4 } };
            int f = 0;
            foreach (int[] run in runs)
            {
                var tops = new List<Vector3>();
                foreach (int i in run) tops.Add(S(poles[i].x, 3.1f, poles[i].y));
                Kit.LightString(b, "Patio Festoon " + (++f), tops, 0.3f, 0.4f, false, false, 160 + f, warm);
            }
        }
        // patio heaters
        foreach (var (x, z) in new[] { (-8.5f, -1.5f), (-8.5f, -10.5f) })
        {
            Vector3 c = S(x, 0f, z);
            Kit.AddDisc(shell, c, 0.225f, 0.06f, B(140, 142, 146), 14);
            Kit.AddBox(shell, c, new Vector3(0.06f, 2.1f, 0.06f), B(150, 152, 156), shopYaw);
            Kit.AddDisc(shell, c + up * 2.22f, 0.4f, 0.03f, B(160, 162, 166), 18);
            Glow(glow, x, 2.1f, z, 0.25f, 0.14f, 0.25f, new Color(1f, 0.43f, 0.16f), x + z);
        }
        Lamp(S(-8.5f, 2.1f, -1.5f), "FF6E28", 4.0f, 1.2f);
        // planters by the door with small firs wrapped in lights; an A-frame board; a bench; bins; a sled of pizza boxes
        foreach (float x in new[] { -3.5f, 3.6f })
        {
            Kit.AddBox(shell, S(x, 0f, 0.45f), new Vector3(0.5f, 0.6f, 0.5f), B(28, 28, 30), shopYaw);
            var pine = new MeshBuilder();
            Kit.AddPine(pine, S(x, 0.55f, 0.45f), 1.1f, (int)(x * 10f) + 50);
            b.MeshPiece("Pizzeria Door Fir " + (x < 0f ? "L" : "R"), pine.ToMesh(), snowy, false);
            var spiral = new List<Vector3>();
            for (int k = 0; k <= 10; k++) { float a = k * 1.3f, hy = 0.75f + k * 0.08f, rr = 0.32f * (1f - k / 11f); spiral.Add(S(x, hy, 0.45f) + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr)); }
            Kit.LightString(b, "Door Fir Lights " + (x < 0f ? "L" : "R"), spiral, 0.02f, 0.12f, false, false, 170 + (int)x);
        }
        {
            Vector3 c = S(-1.0f, 0f, 1.8f);
            Vector3 f = Quaternion.Euler(0f, shopYaw + 15f, 0f) * Vector3.forward;
            foreach (int sd in new[] { 1, -1 })
            {
                Vector3 facing = f * sd, lean = (up * 0.96f - facing * 0.26f).normalized, right = -Vector3.Cross(up, facing).normalized;
                Vector3 mid = c + facing * 0.15f + lean * 0.5f;
                Strut(face, mid - lean * 0.49f, mid + lean * 0.49f, 0.62f, 0.03f, B(95, 62, 40), right);
                Pic(face, mid + Vector3.Cross(lean, right).normalized * 0.017f, right, lean, 0.52f, 0.78f, sd > 0 ? "chalk_special" : "chalk_treats", null, false, 0.12f, 0.88f, 0f, 1f);
            }
        }
        {
            Vector3 c = S(1.9f, 0f, 0.45f);
            Kit.AddBox(shell, c + up * 0.42f, new Vector3(1.6f, 0.05f, 0.4f), Walnut, shopYaw);
            Kit.AddBox(shell, c + up * 0.5f + Dir(0f, 0f, -0.18f), new Vector3(1.6f, 0.35f, 0.04f), Walnut, shopYaw);
            foreach (int sd in new[] { -1, 1 }) Kit.AddBox(shell, c + Dir(sd * 0.7f, 0f, 0f), new Vector3(0.06f, 0.42f, 0.36f), Iron, shopYaw);
        }
        foreach (float x in new[] { 4.85f, 5.45f }) { Kit.AddBox(shell, S(x, 0f, -1.0f), new Vector3(0.5f, 0.95f, 0.5f), B(28, 28, 30), shopYaw); Kit.AddBox(shell, S(x, 0.95f, -1.0f), new Vector3(0.52f, 0.05f, 0.52f), B(36, 36, 38), shopYaw); }
        {
            Vector3 c = S(-3.7f, 0f, 1.6f);
            foreach (int sd in new[] { -1, 1 }) Strut(shell, c + Dir(sd * 0.22f, 0.05f, -0.5f), c + Dir(sd * 0.22f, 0.05f, 0.5f), 0.03f, 0.04f, B(120, 40, 30));
            foreach (int sd in new[] { -1, 1 }) Strut(shell, c + Dir(sd * 0.22f, 0.05f, 0.5f), c + Dir(sd * 0.22f, 0.25f, 0.62f), 0.03f, 0.04f, B(120, 40, 30));
            Kit.AddBox(shell, c + up * 0.15f, new Vector3(0.5f, 0.04f, 0.9f), B(150, 105, 65), shopYaw);
            for (int k = 0; k < 5; k++) TBox(face, -3.7f + (k % 2) * 0.02f, 0.19f + k * 0.045f, 1.6f, 0.41f, 0.044f, 0.41f, "box_side", null, "logo_box", 0.41f);
        }
        // the Christmas tree with its star, spiral lights and presents, and a snowman beside it
        {
            float tx = -10.5f, tz = 2.5f, th = 6f;
            var tree = new MeshBuilder();
            Kit.AddPine(tree, S(tx, 0f, tz), th, 31);
            Kit.AddStar(tree, S(tx, th + 0.25f, tz), 0.4f, 0.07f, Gold);
            b.MeshPiece("Pizzeria Fir", tree.ToMesh(), snowy, true);
            var spiral = new List<Vector3>();
            for (int k = 0; k <= 26; k++)
            {
                float t = k / 26f, a = k * 0.95f, hy = th * (0.16f + 0.76f * t);
                float rr = 0.95f * th * 0.27f * (1f - 0.75f * t);
                spiral.Add(S(tx, hy, tz) + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr));
            }
            Kit.LightString(b, "Pizzeria Tree Lights", spiral, 0.04f, 0.28f, false, false, 133);
            var random = new System.Random(3);
            Color[] wraps = { Red, Green, Gold };
            for (int k = 0; k < 8; k++)
            {
                float a = k * 0.8f + 0.3f, rr = 1.3f + 0.35f * (float)random.NextDouble(), s = 0.25f + 0.25f * (float)random.NextDouble();
                Vector3 p = S(tx, 0f, tz) + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                float yaw = (float)random.NextDouble() * 90f;
                Kit.AddBox(shell, p, new Vector3(s, s * 0.8f, s * 0.9f), wraps[k % 3], yaw);
                Kit.AddBox(shell, p, new Vector3(s + 0.01f, s * 0.8f + 0.01f, 0.03f), k % 3 == 2 ? Red : Gold, yaw);
                Kit.AddBox(shell, p, new Vector3(0.03f, s * 0.8f + 0.01f, s * 0.9f + 0.01f), k % 3 == 2 ? Red : Gold, yaw);
            }
            Vector3 sm = S(-11.8f, 0f, 5.2f);
            foreach (var (r0, y0) in new[] { (0.42f, 0.38f), (0.3f, 0.98f), (0.21f, 1.42f) })
            {
                Piece ball = b.MeshPiece("Pizzeria Snowman " + (int)(y0 * 100f), Kit.Ball(r0, 10, 14, 0.04f, (int)(y0 * 10f)), snowy, true);
                ball.Transform.position = sm + up * y0 + rootPos;
            }
            Kit.AddBox(shell, sm + up * 1.42f + Dir(0f, 0f, 0.2f), new Vector3(0.04f, 0.04f, 0.14f), B(230, 110, 30), shopYaw);
            Strut(shell, sm + up * 1.2f - Dir(0.22f, 0f, 0f), sm + up * 1.24f + Dir(0.22f, 0f, 0f), 0.08f, 0.06f, Red);
        }
        // lamp posts flanking the path at the head of the terrace
        foreach (float x in new[] { -4.4f, -0.4f })
        {
            Vector3 c = S(x, 0f, 17.0f);
            Kit.AddBox(shell, c, new Vector3(0.3f, 0.3f, 0.3f), Iron, shopYaw);
            Kit.AddBox(shell, c, new Vector3(0.1f, 3.0f, 0.1f), Iron, shopYaw);
            Kit.AddBox(shell, c + up * 3.0f, new Vector3(0.34f, 0.06f, 0.34f), Iron, shopYaw);
            Glow(glow, x, 3.25f, 17.0f, 0.24f, 0.4f, 0.24f, Warm, x);
            foreach (var (dx, dz) in new[] { (-0.14f, -0.14f), (0.14f, -0.14f), (-0.14f, 0.14f), (0.14f, 0.14f) }) Kit.AddBox(shell, c + Dir(dx, 3.03f, dz), new Vector3(0.025f, 0.45f, 0.025f), Iron, shopYaw);
            Lathe(face, c + up * 3.48f, new[] { 0.22f, 0.04f }, new[] { 0f, 0.18f }, "white", Iron, 4);
        }
    }

    // under the terrace: a stone bastion where the ground is near, a braced timber trestle where it falls away
    void Foundation(float zA, float zB, float xs)
    {
        var random = new System.Random(23);
        float GroundAt(float x, float z)
        {
            Vector3 w = S(x, 0f, z) + rootPos;
            return b.Ground.At(w.x, w.z, out float gy) ? gy - rootPos.y : -60f;
        }
        float top = -0.6f;
        // the bastion: coursed blocks under every 3 m cell where the drop is under 14 m, buttresses on its outer faces
        for (float z = zA; z > zB + 0.01f; z -= 3f)
            for (float x = -xs; x < xs - 0.01f; x += 3f)
            {
                float low = Mathf.Min(Mathf.Min(GroundAt(x, z), GroundAt(x + 3f, z)), Mathf.Min(GroundAt(x, z - 3f), GroundAt(x + 3f, z - 3f)));
                low = Mathf.Min(low, GroundAt(x + 1.5f, z - 1.5f));
                float tall = top - (low - 1.5f);
                if (tall <= 0.2f || tall > 14f) continue;
                for (float y = low - 1.5f; y < top - 0.01f; y += 1.2f)
                {
                    float hgt = Mathf.Min(1.2f, top - y);
                    Color c = Scale(Granite, 0.85f + 0.25f * (float)random.NextDouble());
                    for (float bx = 0f; bx < 3f - 0.01f; bx += 1.5f)
                        Kit.AddBox(shell, S(x + bx + 0.75f, y, z - 1.5f), new Vector3(1.52f, hgt + 0.01f, 3.02f), Scale(c, 0.92f + 0.12f * (float)random.NextDouble()), shopYaw);
                }
                bool left = Mathf.Abs(x + xs) < 0.1f, right = Mathf.Abs(x + 3f - xs) < 0.1f, back = z - 3f < zB + 0.1f;
                if ((left || right || back) && Mathf.Abs(Mathf.Round(z / 6f) * 6f - z) < 0.1f)
                {
                    float bxp = left ? x - 0.4f : right ? x + 3.4f : x + 1.5f, bzp = (left || right) ? z - 1.5f : zB - 0.4f;
                    float inX = left ? 0.5f : right ? -0.5f : 0f, inZ = (left || right) ? 0f : 0.5f;
                    Strut(shell, S(bxp, low - 1.5f, bzp), S(bxp + inX, top - 0.2f, bzp + inZ), 1.2f, 1.2f, Scale(Granite, 0.9f));
                }
            }
        // the trestle: bents every 6 m across the deck where the drop is deeper, four posts each, X braces, walers
        float[] postX = { -xs + 0.5f, -4.4f, 4.4f, xs - 0.5f };
        var bents = new List<float>();
        for (float z = zB + 0.5f; z < zA; z += 6f) bents.Add(z);
        foreach (float z in bents)
        {
            float[] low = new float[postX.Length];
            bool deep = false;
            for (int i = 0; i < postX.Length; i++) { low[i] = GroundAt(postX[i], z); if (top - low[i] > 14f) deep = true; }
            if (!deep) continue;
            for (int i = 0; i < postX.Length; i++)
            {
                if (top - low[i] <= 1f) continue;
                Kit.AddBox(shell, S(postX[i], low[i] - 1f, z), new Vector3(0.45f, top - low[i] + 1f, 0.45f), Timber, shopYaw);
                Kit.AddRock(shell, S(postX[i], low[i] - 0.4f, z), new Vector3(1.6f, 0.9f, 1.4f), 200 + i + (int)z);
                if (i + 1 < postX.Length && top - low[i + 1] > 1f)
                {
                    float floor = Mathf.Max(low[i], low[i + 1]) + 1f;
                    for (float y = top - 0.5f; y - 6f > floor; y -= 6f)
                    {
                        Strut(shell, S(postX[i], y, z), S(postX[i + 1], y - 6f, z), 0.2f, 0.25f, Scale(Timber, 0.9f));
                        Strut(shell, S(postX[i + 1], y, z), S(postX[i], y - 6f, z), 0.2f, 0.25f, Scale(Timber, 0.9f));
                        Strut(shell, S(postX[i], y, z), S(postX[i + 1], y, z), 0.2f, 0.3f, Scale(Timber, 0.85f));
                    }
                }
            }
        }
        for (int k = 0; k + 1 < bents.Count; k++)
            foreach (float x in new[] { postX[0], postX[3] })
            {
                float l0 = GroundAt(x, bents[k]), l1 = GroundAt(x, bents[k + 1]);
                if (top - l0 <= 14f && top - l1 <= 14f) continue;
                float floor = Mathf.Max(l0, l1) + 1f;
                for (float y = top - 0.5f; y - 6f > floor; y -= 6f)
                {
                    Strut(shell, S(x, y, bents[k]), S(x, y - 6f, bents[k + 1]), 0.2f, 0.25f, Scale(Timber, 0.9f));
                    Strut(shell, S(x, y, bents[k]), S(x, y, bents[k + 1]), 0.2f, 0.3f, Scale(Timber, 0.85f));
                }
            }
        foreach (var (za, xa, zb, xb) in new[] { (zA, -xs, zB, -xs), (zA, xs, zB, xs), (zB, -xs, zB, xs) })
            Strut(shell, S(xa, -0.95f, za), S(xb, -0.95f, zb), 0.5f, 0.7f, Scale(Timber, 0.8f));
    }
}
