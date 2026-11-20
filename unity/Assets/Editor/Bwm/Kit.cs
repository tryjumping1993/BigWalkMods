// Ready-made things for puzzle definitions: shapes built in code, and the winter edition's decorations.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bwm
{
    public static class Kit
    {
        // The Christmas palette for bulbs: red, green, gold, blue, warm white
        public static readonly Color[] BulbColors =
        {
            new Color(1f, 0.16f, 0.12f), new Color(0.15f, 0.9f, 0.25f), new Color(1f, 0.72f, 0.15f),
            new Color(0.2f, 0.45f, 1f), new Color(1f, 0.93f, 0.8f),
        };

        static readonly Color WireColor = new Color(0.05f, 0.16f, 0.08f);
        static readonly Color PostColor = new Color(0.36f, 0.25f, 0.17f);

        // ---- meshes ----

        // A ball: a sphere of latitude rings, optionally lumpy (0 = perfect, 0.15 = hand-rolled snowball)
        public static Mesh Ball(float radius, int rings = 10, int segments = 14, float lumpy = 0f, int seed = 1)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float v = (float)r / rings, u = (float)s / segments;
                    float lat = Mathf.PI * (v - 0.5f), lon = 2f * Mathf.PI * u;
                    var dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    // lumps from noise on the direction, wrapped so the seam matches
                    float n = Mathf.PerlinNoise(dir.x * 2.3f + seed * 7.1f + 10f, dir.y * 2.3f + dir.z * 2.3f + seed * 3.3f + 10f) - 0.5f;
                    verts.Add(dir * radius * (1f + lumpy * 2f * n));
                    uvs.Add(new Vector2(u, v));
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, b = a + segments + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A snow drift: a soft uneven mound sitting on the ground (origin at the centre of its base)
        public static Mesh Mound(float radius, float height, int seed = 1, int rings = 8, int segments = 20)
        {
            var verts = new List<Vector3> { new Vector3(0f, height, 0f) };
            var tris = new List<int>();
            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float t = (float)r / rings, a = 2f * Mathf.PI * s / segments;
                    float wobble = 1f + 0.25f * (Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + seed * 5.7f + 20f, Mathf.Sin(a) * 1.3f + seed * 2.9f + 20f) - 0.5f);
                    float rad = radius * t * wobble;
                    float y = height * Mathf.Cos(t * Mathf.PI * 0.5f) * Mathf.Cos(t * Mathf.PI * 0.5f) - (r == rings ? 0.15f : 0f);
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
                }
            for (int s = 0; s < segments; s++)
            {
                tris.Add(0); tris.Add(1 + (s + 1) % segments); tris.Add(1 + s);
            }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = 1 + (r - 1) * segments + s, b = 1 + (r - 1) * segments + (s + 1) % segments;
                    int c = a + segments, d = b + segments;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- scenery ----

        // A snow-laden pine added to a mesh being built: a trunk and four drooping tiers. Flat-shaded, so each
        // face takes snow by how much it faces up (the Snowy shader does that).
        public static void AddPine(MeshBuilder mb, Vector3 foot, float height, int seed)
        {
            var random = new System.Random(seed * 2654435 + 11);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            float yaw = R(0f, Mathf.PI * 2f);
            var bark = new Color(R(0.24f, 0.30f), R(0.16f, 0.20f), R(0.10f, 0.13f));
            var needles = new Color(R(0.07f, 0.11f), R(0.24f, 0.33f), R(0.14f, 0.20f));
            var under = needles * 0.6f; under.a = 1f;

            const int sides = 6;
            float trunkR = height * 0.035f, trunkH = height * 0.32f;
            for (int s = 0; s < sides; s++)
            {
                float a0 = yaw + 2f * Mathf.PI * s / sides, a1 = yaw + 2f * Mathf.PI * (s + 1) / sides;
                Vector3 p0 = foot + new Vector3(Mathf.Cos(a0) * trunkR, -0.3f, Mathf.Sin(a0) * trunkR), p1 = foot + new Vector3(Mathf.Cos(a1) * trunkR, -0.3f, Mathf.Sin(a1) * trunkR);
                Vector3 up = Vector3.up * (trunkH + 0.3f);
                mb.Quad(p0, p1, p1 + up, p0 + up, bark);
            }

            const int tiers = 4, segments = 9;
            for (int k = 0; k < tiers; k++)
            {
                float baseY = height * (0.16f + 0.19f * k);
                float tierH = height * (0.36f - 0.02f * k);
                float radius = height * 0.27f * (1f - 0.21f * k) * R(0.92f, 1.08f);
                Vector3 apex = foot + new Vector3(R(-0.04f, 0.04f) * height * 0.1f, baseY + tierH, R(-0.04f, 0.04f) * height * 0.1f);
                Vector3 centre = foot + Vector3.up * (baseY + tierH * 0.12f);
                var ring = new Vector3[segments];
                for (int s = 0; s < segments; s++)
                {
                    float a = yaw + k * 0.7f + 2f * Mathf.PI * s / segments;
                    float r = radius * R(0.86f, 1.12f);
                    ring[s] = foot + new Vector3(Mathf.Cos(a) * r, baseY - R(0f, 0.12f) * tierH, Mathf.Sin(a) * r);
                }
                for (int s = 0; s < segments; s++)
                {
                    Vector3 a = ring[s], b2 = ring[(s + 1) % segments];
                    mb.Tri(apex, b2, a, needles);          // the bough's upper face
                    mb.Tri(centre, a, b2, under);          // its underside
                }
            }
        }

        // A boulder added to a mesh being built: a lumpy, squashed, flat-shaded ball, half sunk in the ground
        public static void AddRock(MeshBuilder mb, Vector3 foot, Vector3 size, int seed)
        {
            var random = new System.Random(seed * 40503 + 7);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            float grey = R(0.34f, 0.46f);
            var colour = new Color(grey, grey * 1.02f, grey * 1.08f);
            float yaw = R(0f, 360f);
            Quaternion turn = Quaternion.Euler(R(-12f, 12f), yaw, R(-12f, 12f));
            const int rings = 5, segments = 8;
            var points = new Vector3[(rings + 1) * segments];
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float lat = Mathf.PI * ((float)r / rings - 0.5f), lon = 2f * Mathf.PI * s / segments;
                    var dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    float lump = (r == 0 || r == rings) ? 1f : R(0.78f, 1.15f);
                    points[r * segments + s] = foot + Vector3.up * size.y * 0.25f + turn * Vector3.Scale(dir * lump, size * 0.5f);
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    Vector3 a = points[r * segments + s], b2 = points[r * segments + (s + 1) % segments];
                    Vector3 c = points[(r + 1) * segments + s], d = points[(r + 1) * segments + (s + 1) % segments];
                    mb.Tri(a, c, b2, colour);
                    mb.Tri(b2, c, d, colour);
                }
        }

        // A small round ball (a bauble) added to a mesh being built, flat-shaded
        public static void AddBall(MeshBuilder mb, Vector3 centre, float radius, Color color)
        {
            const int rings = 5, segments = 8;
            var points = new Vector3[(rings + 1) * segments];
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float lat = Mathf.PI * ((float)r / rings - 0.5f), lon = 2f * Mathf.PI * s / segments;
                    points[r * segments + s] = centre + new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)) * radius;
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    Vector3 a = points[r * segments + s], b2 = points[r * segments + (s + 1) % segments];
                    Vector3 c = points[(r + 1) * segments + s], d = points[(r + 1) * segments + (s + 1) % segments];
                    float shine = 0.85f + 0.3f * Mathf.Clamp01(((float)r / rings) - 0.2f);
                    mb.Tri(a, c, b2, color * shine);
                    mb.Tri(b2, c, d, color * shine);
                }
        }

        // A solid block added to a mesh being built: 'foot' is the centre of its underside, yaw turns it about that.
        // Build a puzzle's posts, walls, roofs and plinths this way into one mesh with the Snowy shader, and snow
        // settles on their tops by itself.
        public static void AddBox(MeshBuilder mb, Vector3 foot, Vector3 size, Color color, float yaw = 0f)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            float hx = size.x / 2f, hz = size.z / 2f;
            var low = new[]
            {
                foot + turn * new Vector3(-hx, 0f, -hz), foot + turn * new Vector3(hx, 0f, -hz),
                foot + turn * new Vector3(hx, 0f, hz), foot + turn * new Vector3(-hx, 0f, hz),
            };
            var high = new Vector3[4];
            for (int i = 0; i < 4; i++) high[i] = low[i] + Vector3.up * size.y;
            for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
            mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
            mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
        }

        // A flat round slab (a step, a pad, a table top) added to a mesh being built; 'foot' is the centre of its underside
        public static void AddDisc(MeshBuilder mb, Vector3 foot, float radius, float height, Color color, int segments = 24)
        {
            Vector3 top = foot + Vector3.up * height;
            for (int s = 0; s < segments; s++)
            {
                float a0 = 2f * Mathf.PI * s / segments, a1 = 2f * Mathf.PI * (s + 1) / segments;
                var d0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var d1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                mb.Tri(top, top + d1, top + d0, color);
                mb.Quad(foot + d0, foot + d1, top + d1, top + d0, color);
            }
        }

        // A five-pointed star standing upright, facing along z (a tree-topper)
        public static void AddStar(MeshBuilder mb, Vector3 centre, float radius, float thickness, Color color)
        {
            var rim = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / 10f, r = i % 2 == 0 ? radius : radius * 0.42f;
                rim[i] = centre + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            Vector3 front = centre + Vector3.back * thickness, back = centre + Vector3.forward * thickness;
            for (int i = 0; i < 10; i++)
            {
                Vector3 a = rim[i], c = rim[(i + 1) % 10];
                mb.Tri(front, a, c, color);
                mb.Tri(back, c, a, color * 0.85f);
            }
        }

        // A snow-laden fir added to a mesh being built (use with the BWM/Fir shader and FirBranchTexture): a trunk
        // and whorls of drooping branch fronds, each frond a shallow roof of two cards so it has a top for snow to
        // lie on and an underside that stays dark. Vertex colour: rgb tints the tree, alpha is 1 at the crown's
        // surface and 0 deep inside it. uv.x below 0 marks bark.
        public static void AddFir(MeshBuilder mb, Vector3 foot, float height, int seed, float detail = 1f)
        {
            var random = new System.Random(seed * 7919 + 3);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            var tint = new Color(R(0.82f, 1.05f), R(0.85f, 1.08f), R(0.8f, 1.0f), 1f);
            float spin = R(0f, Mathf.PI * 2f);

            // trunk: a tapering eight-sided column, bark-marked
            float trunkR = height * 0.022f + 0.06f;
            const int sides = 8;
            var bark = new Color(1f, 1f, 1f, 1f);
            for (int s = 0; s < sides; s++)
            {
                float a0 = 2f * Mathf.PI * s / sides, a1 = 2f * Mathf.PI * (s + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = foot + d0 * trunkR + Vector3.down * 0.4f, b1 = foot + d1 * trunkR + Vector3.down * 0.4f;
                Vector3 t0 = foot + d0 * trunkR * 0.15f + Vector3.up * height * 0.97f, t1 = foot + d1 * trunkR * 0.15f + Vector3.up * height * 0.97f;
                mb.QuadUV(b0, b1, t1, t0, new Vector2(-1f, 0f), new Vector2(-1f, 0f), new Vector2(-1f, 1f), new Vector2(-1f, 1f), bark, bark, bark, bark);
            }

            // the crown's core: a dark cone inside the boughs, so the tree reads as full from a distance (uv.x -2);
            // close up it is only a thin shadow round the trunk
            {
                float coreBase = height * 0.16f, coreR = (height * 0.36f * 0.5f + 0.2f) * (detail < 0.7f ? 1f : 0.35f);
                for (int s = 0; s < sides; s++)
                {
                    float a0 = 2f * Mathf.PI * s / sides + spin, a1 = 2f * Mathf.PI * (s + 1) / sides + spin;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 b0 = foot + Vector3.up * coreBase + d0 * coreR, b1 = foot + Vector3.up * coreBase + d1 * coreR;
                    Vector3 apex = foot + Vector3.up * height * 0.94f;
                    var deep = new Color(tint.r, tint.g, tint.b, 0.45f);
                    mb.QuadUV(b0, b1, apex, apex, new Vector2(-2f, 0f), new Vector2(-2f, 0f), new Vector2(-2f, 1f), new Vector2(-2f, 1f), deep, deep, deep, deep);
                }
            }

            // whorls of fronds, longest at the bottom; each frond droops and curves down towards its tip
            float first = height * R(0.10f, 0.18f), spacing = Mathf.Lerp(0.42f, 0.66f, (height - 4f) / 10f) / Mathf.Max(detail, 0.2f);
            int whorl = 0;
            for (float y = first; y < height * 0.93f; y += spacing * R(0.85f, 1.15f), whorl++)
            {
                float t = (y - first) / (height * 0.93f - first);      // 0 bottom ... 1 top
                float reach = height * 0.36f * Mathf.Pow(1f - t, 0.95f) + 0.25f;
                int count = t > 0.85f || detail < 0.7f ? 4 : random.Next(5, 7);
                float offset = spin + whorl * 2.39996f;      // golden angle between whorls
                for (int k = 0; k < count; k++)
                {
                    float a = offset + 2f * Mathf.PI * k / count + R(-0.25f, 0.25f);
                    float len = reach * R(0.82f, 1.12f);
                    float droop = Mathf.Lerp(0.38f, 0.12f, t) + R(-0.06f, 0.06f);      // radians below horizontal at the base
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    Vector3 root = foot + Vector3.up * y + dir * trunkR * 0.8f;
                    // three points along the bough: it curves further down towards the tip
                    Vector3 p0 = root;
                    Vector3 p1 = p0 + (dir * Mathf.Cos(droop) + Vector3.down * Mathf.Sin(droop)) * len * 0.5f;
                    float droop2 = droop + 0.32f;
                    Vector3 p2 = p1 + (dir * Mathf.Cos(droop2) + Vector3.down * Mathf.Sin(droop2)) * len * 0.5f;
                    float width = len * (detail < 0.7f ? 0.8f : 0.62f);
                    float roof = 0.32f;      // how far each half of the frond tilts down from its spine
                    Vector3 wingL = (side * Mathf.Cos(roof) + Vector3.down * Mathf.Sin(roof)) * (width * 0.5f);
                    Vector3 wingR = (-side * Mathf.Cos(roof) + Vector3.down * Mathf.Sin(roof)) * (width * 0.5f);
                    var inner = new Color(tint.r, tint.g, tint.b, 0.25f);
                    var mid = new Color(tint.r, tint.g, tint.b, 0.75f);
                    var outer = new Color(tint.r, tint.g, tint.b, 1f);
                    // the texture's u runs along the bough (0 at the trunk), v across it (0.5 on the spine)
                    for (int half = 0; half < 2; half++)
                    {
                        Vector3 w = half == 0 ? wingL : wingR;
                        float v = half == 0 ? 0f : 1f;
                        mb.QuadUV(p0, p1, p1 + w * 0.9f, p0 + w * 0.35f, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, v), new Vector2(0f, Mathf.Lerp(0.5f, v, 0.35f)), inner, mid, mid, inner);
                        mb.QuadUV(p1, p2, p2 + w * 0.25f, p1 + w * 0.9f, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, Mathf.Lerp(0.5f, v, 0.3f)), new Vector2(0.5f, v), mid, outer, outer, mid);
                    }
                    if (detail >= 0.7f)
                    {
                        // twigs hanging under the bough: a card straight down from its spine, so it has body seen from the side
                        Vector3 hang = Vector3.down * width * 0.32f;
                        mb.QuadUV(p0, p2, p2 + hang * 0.4f, p1 + hang, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.62f), new Vector2(0.5f, 0.9f), inner, outer, mid, mid);
                    }
                }
            }
            // the leader: two crossed fronds standing up at the top
            Vector3 tipBase = foot + Vector3.up * height * 0.88f, tip = foot + Vector3.up * height * 1.02f;
            var top = new Color(tint.r, tint.g, tint.b, 1f);
            for (int k = 0; k < 2; k++)
            {
                float a = spin + k * Mathf.PI * 0.5f;
                var side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * height * 0.045f;
                mb.QuadUV(tipBase - side, tipBase + side, tip + side * 0.1f, tip - side * 0.1f, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.55f), new Vector2(1f, 0.45f), top, top, top, top);
            }
        }

        // Draws the needle spray the fir fronds are textured with and saves it as a PNG asset (alpha = the needles'
        // cut-out; colour = dark needles, lighter tips, a brown twig). u runs along the twig, v across it.
        public static Texture2D FirBranchTexture(string assetPath, int size = 512)
        {
            var px = new Color[size * size];
            var random = new System.Random(4242);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            void Dot(float x, float y, float r, Color c)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(x - r)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(x + r));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(y - r)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(y + r));
                for (int j = y0; j <= y1; j++)
                    for (int i = x0; i <= x1; i++)
                    {
                        float d = Mathf.Sqrt((i - x) * (i - x) + (j - y) * (j - y));
                        float cover = Mathf.Clamp01(r + 0.5f - d);
                        if (cover <= 0f) continue;
                        Color old = px[j * size + i];
                        px[j * size + i] = new Color(Mathf.Lerp(old.r, c.r, cover), Mathf.Lerp(old.g, c.g, cover), Mathf.Lerp(old.b, c.b, cover), Mathf.Max(old.a, cover));
                    }
            }
            void Line(Vector2 a, Vector2 b, float r0, float r1, Color c0, Color c1)
            {
                int steps = Mathf.CeilToInt((b - a).magnitude / 0.7f) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    Vector2 p = Vector2.Lerp(a, b, t);
                    Dot(p.x, p.y, Mathf.Lerp(r0, r1, t), Color.Lerp(c0, c1, t));
                }
            }
            // the frond's outline: widest a little past the middle, narrowing to the tip
            float Half(float u) => size * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.08f + u * 0.92f)), 0.6f) * (1f - 0.25f * u);
            var twig = new Color(0.30f, 0.22f, 0.12f);
            // side twigs, each with its own needles
            for (float u = 0.05f; u < 0.95f; u += R(0.085f, 0.13f))
                for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                {
                    float x0 = u * size, y0 = size * 0.5f;
                    float ang = R(0.55f, 0.85f);      // forward angle of the side twig
                    float reach = Half(u) * R(0.6f, 1.0f) / Mathf.Sin(ang);
                    Vector2 start = new Vector2(x0, y0), end = start + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * sideSign) * reach;
                    int needles = Mathf.CeilToInt(reach / 2.6f);
                    for (int n = 0; n < needles; n++)
                    {
                        float t = (float)n / needles;
                        Vector2 at = Vector2.Lerp(start, end, t);
                        float nl = Mathf.Lerp(size * 0.055f, size * 0.03f, t) * R(0.8f, 1.2f);
                        for (int ns = -1; ns <= 1; ns += 2)
                        {
                            float na = ang + ns * R(0.7f, 1.1f) * sideSign;
                            Vector2 tipP = at + new Vector2(Mathf.Cos(na), Mathf.Sin(na) * sideSign) * nl;
                            float shade = R(0.75f, 1.15f);
                            var dark = new Color(0.07f * shade, 0.20f * shade, 0.11f * shade);
                            var light = new Color(0.16f * shade, 0.36f * shade, 0.18f * shade);
                            Line(at, tipP, 1.7f, 0.8f, dark, light);
                        }
                    }
                    Line(start, end, 2.2f, 0.9f, twig, twig * 1.3f);
                }
            Line(new Vector2(0f, size * 0.5f), new Vector2(size * 0.98f, size * 0.5f), 4f, 1.2f, twig, twig);
            // spread colour into the transparent pixels so mipmaps do not fringe dark
            for (int pass = 0; pass < 6; pass++)
                for (int j = 1; j < size - 1; j++)
                    for (int i = 1; i < size - 1; i++)
                    {
                        int k = j * size + i;
                        if (px[k].a > 0.01f) continue;
                        Color sum = Color.clear; int n = 0;
                        foreach (int o in new[] { k - 1, k + 1, k - size, k + size })
                            if (px[o].a > 0.01f || px[o].r + px[o].g > 0f) { sum += px[o]; n++; }
                        if (n > 0) { sum /= n; px[k] = new Color(sum.r, sum.g, sum.b, 0f); }
                    }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", assetPath));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(assetPath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.42f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ---- decorations ----

        // A string of Christmas lights hung between posts. Each post is (local x, height of the wire there, local z);
        // the wire sags between them with a bulb about every 'spacing' metres. With 'onGround' the heights are above
        // the terrain at each post (use false on a pad). Decoration only: no collisions, except the posts.
        public static Piece LightString(PuzzleBuilder b, string name, IList<Vector3> posts, float sag = 0.4f, float spacing = 0.5f,
            bool onGround = true, bool addPosts = true, int seed = 1)
        {
            if (posts.Count < 2) throw new Exception("A light string needs at least two posts");
            Transform root = b.Root.transform;
            var tops = new List<Vector3>();      // in the root's local space
            for (int i = 0; i < posts.Count; i++)
            {
                Vector3 world = b.WorldPoint(posts[i].x, posts[i].z, posts[i].y, onGround);
                tops.Add(root.InverseTransformPoint(world));
                bool repeated = false;      // a ring ends where it began: one post there, not two
                for (int j = 0; j < i; j++) if ((posts[j] - posts[i]).sqrMagnitude < 0.01f) repeated = true;
                if (addPosts && !repeated)
                    b.Box(name + " Post " + (i + 1), posts[i].x, posts[i].z, new Vector3(0.1f, posts[i].y + 0.08f, 0.1f), PostColor, 0f, 0f, onGround);
            }

            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var random = new System.Random(seed * 7919 + 17);
            int bulb = 0;
            for (int i = 0; i + 1 < tops.Count; i++)
            {
                Vector3 a = tops[i], c = tops[i + 1];
                float length = Vector3.Distance(a, c);
                int steps = Mathf.Max(6, Mathf.RoundToInt(length / 0.4f));
                Vector3 Point(float t) => Vector3.Lerp(a, c, t) + Vector3.down * (sag * length / 4f * 4f * t * (1f - t));
                // wire: a thin three-sided tube
                for (int s = 0; s < steps; s++)
                {
                    Vector3 p0 = Point((float)s / steps), p1 = Point((float)(s + 1) / steps);
                    Vector3 along = (p1 - p0).normalized, side = Vector3.Cross(along, Vector3.up).normalized * 0.012f, up = Vector3.up * 0.012f;
                    int v = verts.Count;
                    foreach (Vector3 p in new[] { p0, p1 })
                    {
                        verts.Add(p + up); verts.Add(p - up * 0.5f + side); verts.Add(p - up * 0.5f - side);
                        for (int k = 0; k < 3; k++) { colors.Add(WireColor); uvs.Add(Vector2.zero); }
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        int k2 = (k + 1) % 3;
                        tris.Add(v + k); tris.Add(v + 3 + k); tris.Add(v + k2);
                        tris.Add(v + k2); tris.Add(v + 3 + k); tris.Add(v + 3 + k2);
                    }
                }
                // bulbs: small drops hanging just under the wire
                int count = Mathf.Max(2, Mathf.RoundToInt(length / spacing));
                for (int n = 0; n < count; n++)
                {
                    Vector3 p = Point((n + 0.5f) / count) + Vector3.down * 0.06f;
                    Color color = BulbColors[bulb++ % BulbColors.Length];
                    float phase = (float)random.NextDouble();
                    int v = verts.Count;
                    const float r = 0.04f, h = 0.065f;
                    Vector3[] shape = { p + Vector3.up * h * 0.6f, p + Vector3.down * h, p + Vector3.right * r, p + Vector3.left * r, p + Vector3.forward * r, p + Vector3.back * r };
                    foreach (Vector3 q in shape) { verts.Add(q); colors.Add(color); uvs.Add(new Vector2(1f, phase)); }
                    int[] faces = { 0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5, 1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2 };
                    foreach (int f in faces) tris.Add(v + f);
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return b.MeshPiece(name, mesh, b.ShaderMaterial("BWM/FairyLight", "FairyLight"), false);
        }

        // A light string round a rectangle (local centre and size), one post at each corner
        public static Piece LightRing(PuzzleBuilder b, string name, float cx, float cz, float sizeX, float sizeZ, float height = 2.6f, bool onGround = true, int seed = 1)
        {
            float hx = sizeX / 2f, hz = sizeZ / 2f;
            var posts = new List<Vector3>
            {
                new Vector3(cx - hx, height, cz - hz), new Vector3(cx, height, cz - hz), new Vector3(cx + hx, height, cz - hz),
                new Vector3(cx + hx, height, cz), new Vector3(cx + hx, height, cz + hz), new Vector3(cx, height, cz + hz),
                new Vector3(cx - hx, height, cz + hz), new Vector3(cx - hx, height, cz), new Vector3(cx - hx, height, cz - hz),
            };
            return LightString(b, name, posts, 0.4f, 0.5f, onGround, true, seed);
        }
    }

    // Collects flat-shaded, vertex-coloured triangles from many objects into one mesh (every triangle has its own
    // three vertices, so each face has its own normal: the low-poly look, and what lets snow settle face by face).
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int v = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(color); colors.Add(color); colors.Add(color);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
        }

        // A textured quad with its own colour at each corner (smooth-shaded: its two triangles share vertices)
        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color ca, Color cb, Color cc, Color cd)
        {
            int v = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            colors.Add(ca); colors.Add(cb); colors.Add(cc); colors.Add(cd);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Tri(a, c, b, color);
            Tri(a, d, c, color);
        }

        public Mesh ToMesh()
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
