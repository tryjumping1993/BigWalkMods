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
}
