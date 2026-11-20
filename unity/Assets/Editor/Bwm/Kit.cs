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

        // A glowing box for windows and lantern lights (into a mesh drawn with BWM/FairyLight: uv.x 1 = lit, uv.y = phase)
        public static void AddGlow(MeshBuilder mb, Vector3 centre, Vector3 size, float yaw, Color color, float phase)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Vector3 h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = centre + turn * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            var uv = new Vector2(1f, phase);
            int[][] faces = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] f in faces) mb.QuadUV(p[f[0]], p[f[1]], p[f[2]], p[f[3]], uv, uv, uv, uv, color, color, color, color);
        }

        // A log cabin: stone footing, log walls, a steep gable roof that takes snow, a chimney, a door and warm lit
        // windows (into 'glow'). 'foot' is the middle of its floor; size is width (x) by depth (z); it faces +z by yaw.
        public static void AddCabin(MeshBuilder mb, MeshBuilder glow, Vector3 foot, Vector2 size, float yaw, int seed)
        {
            var random = new System.Random(seed * 131 + 7);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + turn * new Vector3(x, y, z);
            var logs = new Color(R(0.32f, 0.42f), R(0.21f, 0.27f), R(0.12f, 0.16f));
            var stone = new Color(0.45f, 0.46f, 0.48f);
            var roofColour = random.NextDouble() < 0.5 ? new Color(0.45f, 0.12f, 0.1f) : new Color(0.2f, 0.27f, 0.2f);
            float w = size.x, d = size.y, wallH = 2.6f, ridge = wallH + w * 0.55f;
            AddBox(mb, foot + Vector3.down * 2f, new Vector3(w + 0.4f, 2.3f, d + 0.4f), stone, yaw);           // footing, sunk into the slope
            // log walls in courses, alternating shade
            for (int c = 0; c < 8; c++)
            {
                float y0 = 0.3f + c * (wallH - 0.3f) / 8f;
                var shade = logs * (c % 2 == 0 ? 1f : 0.86f); shade.a = 1f;
                AddBox(mb, foot + Vector3.up * y0, new Vector3(w, (wallH - 0.3f) / 8f, d), shade, yaw);
            }
            // gables
            mb.Tri(P(-w / 2f, wallH, d / 2f), P(w / 2f, wallH, d / 2f), P(0f, ridge, d / 2f), logs);
            mb.Tri(P(w / 2f, wallH, -d / 2f), P(-w / 2f, wallH, -d / 2f), P(0f, ridge, -d / 2f), logs);
            // roof: two slabs overhanging the walls
            float o = 0.5f;
            Vector3 eaveL = P(-w / 2f - o, wallH - o * 0.9f, 0f), eaveR = P(w / 2f + o, wallH - o * 0.9f, 0f), top = P(0f, ridge + 0.15f, 0f);
            Vector3 along = turn * Vector3.forward * (d / 2f + o);
            Vector3 thick = Vector3.up * 0.18f;
            mb.Quad(eaveL - along, top - along, top + along, eaveL + along, roofColour);
            mb.Quad(top - along, eaveR - along, eaveR + along, top + along, roofColour);
            mb.Quad(eaveL + along - thick, top + along - thick, top - along - thick, eaveL - along - thick, roofColour * 0.7f);
            mb.Quad(top + along - thick, eaveR + along - thick, eaveR - along - thick, top - along - thick, roofColour * 0.7f);
            // chimney
            AddBox(mb, P(w * 0.25f, wallH, -d * 0.2f), new Vector3(0.7f, ridge - wallH + 0.9f, 0.7f), stone, yaw);
            // door and windows on the front, a window on each side
            AddBox(mb, P(-w * 0.18f, 0.3f, d / 2f + 0.02f), new Vector3(1.05f, 2.0f, 0.12f), logs * 0.6f, yaw);
            var warm = new Color(1f, 0.72f, 0.36f);
            AddGlow(glow, P(w * 0.22f, 1.55f, d / 2f + 0.03f), new Vector3(1.0f, 0.85f, 0.06f), yaw, warm, R(0f, 1f));
            AddGlow(glow, P(w / 2f + 0.03f, 1.55f, 0f), new Vector3(0.06f, 0.85f, 1.1f), yaw, warm, R(0f, 1f));
            AddGlow(glow, P(-w / 2f - 0.03f, 1.55f, d * 0.15f), new Vector3(0.06f, 0.85f, 1.1f), yaw, warm, R(0f, 1f));
            // a wreath on the door
            AddGlow(glow, P(-w * 0.18f, 1.75f, d / 2f + 0.1f), new Vector3(0.45f, 0.45f, 0.05f), yaw, new Color(0.15f, 0.5f, 0.2f), 0.5f);
        }

        // A lantern on a post beside a trail: a dark iron post with a warm glowing lamp under a little snow-capped hood
        public static void AddLantern(MeshBuilder mb, MeshBuilder glow, Vector3 foot, int seed)
        {
            var iron = new Color(0.12f, 0.12f, 0.13f);
            AddBox(mb, foot + Vector3.down * 0.4f, new Vector3(0.11f, 2.4f, 0.11f), iron);
            AddBox(mb, foot + Vector3.up * 2.42f, new Vector3(0.42f, 0.08f, 0.42f), iron);       // hood, takes snow
            AddBox(mb, foot + Vector3.up * 1.92f, new Vector3(0.34f, 0.05f, 0.34f), iron);
            AddGlow(glow, foot + Vector3.up * 2.18f, new Vector3(0.26f, 0.42f, 0.26f), 0f, new Color(1f, 0.78f, 0.42f), (seed * 0.618f) % 1f);
        }

        // A trail marker: a thin wooden snow pole with a painted top, leaning a little, as set out along winter
        // trails so they can be followed when the path is drifted over
        public static void AddSnowPole(MeshBuilder mb, Vector3 foot, int seed)
        {
            var random = new System.Random(seed * 92821 + 5);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            float tall = R(1.5f, 2.1f), spin = R(0f, 6.28f), radius = 0.04f;
            var wood = new Color(R(0.42f, 0.5f), R(0.31f, 0.37f), R(0.2f, 0.25f));
            Color paint = random.Next(3) == 0 ? new Color(0.92f, 0.76f, 0.14f) : new Color(0.8f, 0.12f, 0.1f), white = new Color(0.93f, 0.92f, 0.88f);
            // a round post (six sides), tapering a little, its top third painted in bands so it shows against snow
            float[] levels = { -0.3f, tall * 0.62f, tall * 0.72f, tall * 0.8f, tall * 0.9f, tall };
            Color[] bands = { wood, paint, white, paint, white };
            for (int level = 0; level + 1 < levels.Length; level++)
                for (int sd = 0; sd < 6; sd++)
                {
                    float a0 = spin + sd * Mathf.PI / 3f, a1 = spin + (sd + 1) * Mathf.PI / 3f;
                    float r0 = radius * (1f - 0.25f * Mathf.Clamp01(levels[level] / tall)), r1 = radius * (1f - 0.25f * Mathf.Clamp01(levels[level + 1] / tall));
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    mb.Quad(foot + Vector3.up * levels[level] + d0 * r0, foot + Vector3.up * levels[level] + d1 * r0,
                        foot + Vector3.up * levels[level + 1] + d1 * r1, foot + Vector3.up * levels[level + 1] + d0 * r1, bands[level]);
                }
            // a little cap, which takes the snow
            Vector3 top = foot + Vector3.up * tall;
            for (int sd = 0; sd < 6; sd++)
            {
                float a0 = spin + sd * Mathf.PI / 3f, a1 = spin + (sd + 1) * Mathf.PI / 3f;
                mb.Tri(top + Vector3.up * 0.03f, top + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius * 0.75f, top + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius * 0.75f, white);
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
        // surface and 0 deep inside it. uv.x below 0 marks bark. girth below 1 makes a slim spruce: a narrow crown of
        // fewer, more drooping boughs.
        public static void AddFir(MeshBuilder mb, Vector3 foot, float height, int seed, float detail = 1f, float girth = 1f)
        {
            var random = new System.Random(seed * 7919 + 3);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            var tint = new Color(R(0.82f, 1.05f), R(0.85f, 1.08f), R(0.8f, 1.0f), 1f);
            float spin = R(0f, Mathf.PI * 2f);

            // trunk: a tapering eight-sided column, bark-marked
            float trunkR = height * 0.022f + 0.06f;
            const int sides = 12;
            var bark = new Color(1f, 1f, 1f, 1f);
            for (int s = 0; s < sides; s++)
            {
                float a0 = 2f * Mathf.PI * s / sides, a1 = 2f * Mathf.PI * (s + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = foot + d0 * trunkR + Vector3.down * 0.4f, b1 = foot + d1 * trunkR + Vector3.down * 0.4f;
                Vector3 t0 = foot + d0 * trunkR * 0.15f + Vector3.up * height * 0.93f, t1 = foot + d1 * trunkR * 0.15f + Vector3.up * height * 0.93f;
                mb.QuadUV(b0, b1, t1, t0, new Vector2(-1f, 0f), new Vector2(-1f, 0f), new Vector2(-1f, 1f), new Vector2(-1f, 1f), bark, bark, bark, bark);
            }

            // the crown's core: a dark cone inside the boughs, so the tree reads as full from a distance (uv.x -2);
            // close up it is only a thin shadow round the trunk
            {
                float coreBase = height * 0.16f, coreR = (height * 0.36f * 0.5f + 0.2f) * girth * (detail < 0.7f ? 1f : 0.35f);
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
            // (on a grown tree the boughs start above the head of a walker 70 cm tall, who can then pass under them)
            float first = height > 5f ? Mathf.Max(height * R(0.12f, 0.2f), R(1.25f, 1.7f)) : height * R(0.10f, 0.18f), spacing = Mathf.Lerp(0.34f, 0.54f, (height - 4f) / 10f) / Mathf.Max(detail, 0.2f);
            int whorl = 0;
            for (float y = first; y < height * 0.96f; y += spacing * R(0.85f, 1.15f), whorl++)
            {
                float t = (y - first) / (height * 0.96f - first);      // 0 bottom ... 1 top
                // a fuller crown than a straight cone, drawing in to a narrow spire at the top
                float reach = (height * 0.36f * Mathf.Pow(1f - t, 0.82f) + Mathf.Lerp(0.3f, 0.07f, t)) * girth;
                int count = t > 0.88f || detail < 0.7f ? 5 : girth < 0.8f ? random.Next(5, 8) : random.Next(7, 10);
                float offset = spin + whorl * 2.39996f;      // golden angle between whorls
                for (int k = 0; k < count; k++)
                {
                    float a = offset + 2f * Mathf.PI * k / count + R(-0.25f, 0.25f);
                    float len = reach * R(0.82f, 1.12f);
                    // radians below horizontal at the base; the youngest shoots, at the top, point upwards
                    float droop = Mathf.Lerp(0.38f, 0.12f, t) + (1f - girth) * 0.55f - Mathf.Clamp01((t - 0.72f) / 0.28f) * 0.8f + R(-0.06f, 0.06f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    Vector3 root = foot + Vector3.up * y + dir * trunkR * 0.8f;
                    // three points along the bough: it curves further down towards the tip
                    Vector3 p0 = root;
                    Vector3 p1 = p0 + (dir * Mathf.Cos(droop) + Vector3.down * Mathf.Sin(droop)) * len * 0.5f;
                    float droop2 = droop + 0.32f;
                    Vector3 p2 = p1 + (dir * Mathf.Cos(droop2) + Vector3.down * Mathf.Sin(droop2)) * len * 0.5f;
                    float width = len * (detail < 0.7f ? 0.72f : 0.46f);      // narrow sprays, not paddles
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
            // the leader: three narrow fronds standing against the axis and meeting in a point, so the top is a spire
            // from every side (two crossed full-width fronds read as a diamond, a splayed tuft as a forked V)
            Vector3 tipBase = foot + Vector3.up * height * 0.9f, tip = foot + Vector3.up * height * 1.03f;
            var top = new Color(tint.r, tint.g, tint.b, 1f);
            for (int k = 0; k < 3; k++)
            {
                float a = spin + k * 2.0944f + 0.4f;
                var side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * height * 0.017f;
                mb.QuadUV(tipBase - side, tipBase + side, tip + side * 0.08f, tip - side * 0.08f, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.55f), new Vector2(1f, 0.45f), top, top, top, top);
            }
        }

        // A bare winter birch added to a mesh being built (use with the BWM/Snowy shader, which lays snow on whatever
        // faces up, so every limb gets a white top): a pale trunk with dark scars, leaning a little; limbs rising
        // steeply and arching out; fine dark twigs hanging from their ends. detail below 0.7 leaves the twigs out
        // (the version for distance).
        public static void AddBirch(MeshBuilder mb, Vector3 foot, float height, int seed, float detail = 1f)
        {
            var random = new System.Random(seed * 6151 + 11);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            Color pale = new Color(R(0.82f, 0.9f), R(0.81f, 0.88f), R(0.74f, 0.82f)), scar = new Color(0.17f, 0.16f, 0.16f), twigColor = new Color(0.30f, 0.19f, 0.17f);
            // a tapered limb from a to b
            void Limb(Vector3 a, Vector3 b, float ra, float rb, int sides, Color ca, Color cb)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                Vector3 v = Vector3.Cross(u, axis);
                for (int s = 0; s < sides; s++)
                {
                    // turned so that one face looks straight up from a leaning limb: that face takes the snow
                    float turned = Mathf.PI / 2f - Mathf.PI / sides;
                    float a0 = 2f * Mathf.PI * s / sides + turned, a1 = 2f * Mathf.PI * (s + 1) / sides + turned;
                    Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                    mb.Quad(a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb, Color.Lerp(ca, cb, 0.5f));
                }
            }
            // fine twigs hanging from a limb's end
            void Twigs(Vector3 at, Vector3 dir, int count, float length)
            {
                if (detail < 0.7f) return;
                var flat = new Vector3(dir.x, 0f, dir.z).normalized;
                for (int i = 0; i < count; i++)
                {
                    float turn = R(-1.1f, 1.1f);
                    var outward = Quaternion.Euler(0f, turn * Mathf.Rad2Deg, 0f) * flat;
                    Vector3 mid = at + (outward * R(0.5f, 0.9f) + Vector3.up * R(-0.1f, 0.35f)).normalized * length * 0.45f;
                    Vector3 end = mid + (outward * R(0.2f, 0.6f) + Vector3.down * R(0.5f, 1f)).normalized * length * R(0.5f, 0.9f);
                    Limb(at, mid, 0.014f, 0.009f, 3, twigColor, twigColor);
                    Limb(mid, end, 0.009f, 0.004f, 3, twigColor, twigColor);
                    // finer still: a spray of hanging tips off each twig
                    for (int tip = 0; tip < 2; tip++)
                    {
                        Vector3 from = Vector3.Lerp(mid, end, R(0.1f, 0.7f));
                        Vector3 to = from + (outward * R(-0.25f, 0.45f) + Vector3.down * R(0.6f, 1f) + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f))).normalized * length * R(0.3f, 0.55f);
                        Limb(from, to, 0.006f, 0.003f, 3, twigColor, twigColor);
                    }
                }
            }

            // the trunk: leaning and gently curved, pale with dark scars, dark and rough at the foot
            float lean = R(0f, Mathf.PI * 2f), leanBy = height * R(0.02f, 0.09f);
            var leanDir = new Vector3(Mathf.Cos(lean), 0f, Mathf.Sin(lean));
            float baseR = height * 0.011f + 0.045f;
            Vector3 Spine(float t) => foot + Vector3.up * (height * t) + leanDir * (leanBy * t * t) + new Vector3(Mathf.Sin(t * 7f + lean), 0f, Mathf.Cos(t * 5f + lean)) * (height * 0.006f);
            float Radius(float t) => Mathf.Lerp(baseR, 0.018f, Mathf.Pow(t, 0.8f));
            const int segments = 10;
            Limb(foot + Vector3.down * 0.4f, Spine(0f), baseR * 1.25f, baseR * 1.1f, 6, scar, scar);
            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments, t1 = (float)(i + 1) / segments;
                Color lower = i == 0 ? Color.Lerp(scar, pale, 0.35f) : pale;
                if (R(0f, 1f) < 0.4f) { Limb(Spine(t0), Spine(t1), Radius(t0), Radius(t1), 6, lower, lower); continue; }      // a clean stretch
                float cut = Mathf.Lerp(t0, t1, R(0.1f, 0.85f)), band = R(0.003f, 0.016f);      // a dark scar ring somewhere up the segment
                Limb(Spine(t0), Spine(cut), Radius(t0), Radius(cut), 6, lower, lower);
                Limb(Spine(cut), Spine(cut + band), Radius(cut) * 1.04f, Radius(cut + band) * 1.04f, 6, scar, scar);
                Limb(Spine(cut + band), Spine(t1), Radius(cut + band), Radius(t1), 6, pale, pale);
            }
            Twigs(Spine(1f), Vector3.up + leanDir * 0.3f, 4, height * 0.09f);

            // limbs: rising steeply, arching outwards, each with side branches and hanging twigs
            int limbs = Mathf.RoundToInt(Mathf.Lerp(8f, 14f, Mathf.Clamp01((height - 4f) / 7f)));
            float spin = R(0f, Mathf.PI * 2f);
            for (int k = 0; k < limbs; k++)
            {
                float t = Mathf.Lerp(0.3f, 0.93f, (k + R(0f, 0.8f)) / limbs);
                float a = spin + k * 2.39996f + R(-0.3f, 0.3f);
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float length = height * Mathf.Lerp(0.36f, 0.12f, (t - 0.3f) / 0.63f) * R(0.8f, 1.2f);
                float tilt = R(0.35f, 0.7f);      // radians from upright where it leaves the trunk
                Vector3 at = Spine(t);
                float r = Radius(t) * 0.55f;
                // the dark eye where a limb leaves the trunk
                Limb(at, at + (Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized * (Radius(t) + 0.05f), r * 1.5f, r * 1.15f, 4, scar, scar);
                const int parts = 5;
                for (int j = 0; j < parts; j++)
                {
                    Vector3 dir = (Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized;
                    Vector3 next = at + dir * (length / parts);
                    float r1 = Mathf.Max(0.014f, r * 0.72f);
                    Limb(at, next, r, r1, 4, j == 0 ? pale : Color.Lerp(pale, twigColor, 0.6f), Color.Lerp(pale, twigColor, j == 0 ? 0.6f : 1f));
                    // a side branch from each joint, to one side and the other
                    float sideTurn = (j % 2 == 0 ? 1f : -1f) * R(0.5f, 1.0f);
                    Vector3 sideDir = (Quaternion.Euler(0f, sideTurn * Mathf.Rad2Deg, 0f) * outward * Mathf.Sin(tilt + 0.25f) + Vector3.up * Mathf.Cos(tilt + 0.25f)).normalized;
                    float sideLength = length * R(0.3f, 0.45f);
                    Vector3 sideEnd = next + sideDir * sideLength;
                    if (detail >= 0.7f ? j >= 1 : j == 2)
                    {
                        Limb(next, sideEnd, r1 * 0.7f, 0.011f, 3, twigColor, twigColor);
                        Twigs(sideEnd, sideDir, 3, height * 0.1f);
                    }
                    at = next; r = r1; tilt += R(0.07f, 0.2f);
                }
                Twigs(at, outward, 4, height * 0.11f);
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

        // One of the snow surface maps that tools/snowtex.py writes into Assets/Textures/Snow (bwm.ps1 runs it when
        // they are missing or older than the script), imported for tiling: normals as a normal map, the rest as
        // linear data, with full anisotropic filtering (the ground is mostly seen at a grazing angle). Settings are
        // only re-applied when they differ, so a build does not recompress the maps each time. Null, with a
        // warning, when the file is missing; the shaders then draw plain snow.
        public static Texture2D SnowMap(string file, bool normal)
        {
            string assetPath = "Assets/Textures/Snow/" + file;
            if (!System.IO.File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", assetPath))))
            {
                Debug.LogWarning("[BWM] " + assetPath + " is missing: run python tools/snowtex.py (bwm.ps1 build does)");
                return null;
            }
            UnityEditor.AssetDatabase.ImportAsset(assetPath);      // picks up a regenerated file
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(assetPath);
            var type = normal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Repeat
                || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 16 || importer.maxTextureSize != 2048
                || importer.textureCompression != UnityEditor.TextureImporterCompression.CompressedHQ)
            {
                importer.textureType = type;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 16;
                importer.maxTextureSize = 2048;
                importer.textureCompression = UnityEditor.TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // The snow surface maps on a material that has them (BWM/Snow, BWM/SnowOverlay): each map the shader asks for
        public static void SetSnowMaps(Material m)
        {
            foreach (string map in new[] { "Wind", "Grain", "Track" })
            {
                if (m.HasProperty("_" + map + "Normal")) m.SetTexture("_" + map + "Normal", SnowMap("Snow" + map + "_N.png", true));
                if (m.HasProperty("_" + map + "Height")) m.SetTexture("_" + map + "Height", SnowMap("Snow" + map + "_H.png", false));
            }
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
