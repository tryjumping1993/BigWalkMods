// The game world as the SDK's reference map (built from the user's own game files), and queries on it:
// terrain height, obstacles, and finding flat clear ground for a puzzle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bwm
{
    public static class World
    {
        public const string GameData = @"G:\SteamLibrary\steamapps\common\Big Walk\Big Walk_Data\data.unity3d";

        // Where the game's four spawn rooms are (x, z); the default place to look for a site near
        public static readonly Vector2 Spawn = new Vector2(-230.2f, -481.2f);

        // Builds the SDK's map into the open scene (as unsaved, reference-only objects) and returns its root
        public static GameObject ImportMap()
        {
            EditorPrefs.SetString("BigPuzzleSDK.GameDataPath", GameData);
            bool models = (bool)Sdk.Call(Sdk.T("BigWalkGameModels"), null, "Import", GameData);
            if (!models) Debug.LogWarning("[BWM] The game's models could not be read from " + GameData);
            Sdk.Call(Sdk.T("BigWalkMapImporter"), null, "Import", GameData);
            GameObject root = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == "BigWalk Map" && g.transform.parent == null && g.scene.IsValid());
            if (root == null) throw new Exception("The BigWalk Map was not built (see the log above).");
            return root;
        }

        // Writes world\world-index.tsv: every object of the game's world with its position and size
        public static int Dump(GameObject map, string file)
        {
            var sb = new StringBuilder("path\tx\ty\tz\tsizeX\tsizeY\tsizeZ\tverts\n");
            int n = 0;
            foreach (Transform t in map.GetComponentsInChildren<Transform>(true))
            {
                MeshFilter mf = t.GetComponent<MeshFilter>();
                Renderer r = t.GetComponent<Renderer>();
                Vector3 p = r != null ? r.bounds.center : t.position;
                Vector3 s = r != null ? r.bounds.size : Vector3.zero;
                var parts = new List<string>();
                for (Transform c = t; c != null && c != map.transform; c = c.parent) parts.Add(c.name);
                parts.Reverse();
                sb.Append(string.Join("/", parts)).Append('\t')
                  .Append(Sdk.F(p.x)).Append('\t').Append(Sdk.F(p.y)).Append('\t').Append(Sdk.F(p.z)).Append('\t')
                  .Append(Sdk.F(s.x)).Append('\t').Append(Sdk.F(s.y)).Append('\t').Append(Sdk.F(s.z)).Append('\t')
                  .Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0).Append('\n');
                n++;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            return n;
        }
    }

    // The game's terrain as height grids (the SDK map builds each terrain as a regular grid of vertices, 2 m apart,
    // leaving out the cells where the game cut a hole), plus everything else in the world as obstacles.
    public sealed class Ground
    {
        sealed class Grid { public Vector3 origin; public float step; public int side; public float[] h; public bool[] solid; }
        readonly List<Grid> grids = new List<Grid>();
        readonly List<MeshFilter> obstacles = new List<MeshFilter>();
        readonly Dictionary<MeshFilter, Vector3[]> worldVerts = new Dictionary<MeshFilter, Vector3[]>();

        public Ground(GameObject map)
        {
            Transform deathFloor = map.transform.Find("Death Floor");
            foreach (MeshFilter mf in map.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || mf.GetComponent<Renderer>() == null) continue;
                if (deathFloor != null && mf.transform.IsChildOf(deathFloor)) continue;
                // The real terrains sit directly under "Big parts" and are square grids; other things share the name prefix
                bool terrain = mf.name.StartsWith("Terrain_") && mf.transform.parent != null && mf.transform.parent.name == "Big parts";
                int side = terrain ? Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount)) : 0;
                if (!terrain || side * side != mesh.vertexCount) { obstacles.Add(mf); continue; }
                Vector3[] v = mesh.vertices;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                var g = new Grid { side = side, h = new float[v.Length], solid = new bool[(side - 1) * (side - 1)] };
                g.origin = m.MultiplyPoint3x4(v[0]);
                g.step = m.MultiplyPoint3x4(v[1]).x - g.origin.x;
                for (int i = 0; i < v.Length; i++) g.h[i] = m.MultiplyPoint3x4(v[i]).y;
                int[] tris = mesh.triangles;      // two triangles per cell, the first starting at the cell's own corner
                for (int k = 0; k + 5 < tris.Length; k += 6)
                {
                    int v00 = tris[k];
                    g.solid[(v00 / side) * (side - 1) + v00 % side] = true;
                }
                grids.Add(g);
            }
            if (grids.Count == 0) throw new Exception("No terrain in the map");
        }

        // Terrain height at (x, z); false over a hole or off the terrain
        public bool At(float x, float z, out float y)
        {
            foreach (Grid g in grids)
            {
                float fx = (x - g.origin.x) / g.step, fz = (z - g.origin.z) / g.step;
                if (fx < 0f || fz < 0f || fx >= g.side - 1 || fz >= g.side - 1) continue;
                int i = (int)fx, j = (int)fz;
                if (!g.solid[j * (g.side - 1) + i]) { y = float.NaN; return false; }
                float tx = fx - i, tz = fz - j;
                float a = g.h[j * g.side + i], b = g.h[j * g.side + i + 1], c = g.h[(j + 1) * g.side + i], d = g.h[(j + 1) * g.side + i + 1];
                y = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
                return true;
            }
            y = float.NaN;
            return false;
        }

        // Lowest and highest terrain over a rectangle; false if any of it is a hole or off the terrain
        public bool Range(float cx, float cz, float hx, float hz, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (float z = cz - hz; z <= cz + hz + 0.01f; z += 1f)
                for (float x = cx - hx; x <= cx + hx + 0.01f; x += 1f)
                {
                    if (!At(x, z, out float y)) return false;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            return true;
        }

        // The first world object with geometry inside the box, or null
        public string Blocker(float cx, float cz, float hx, float hz, float yMin, float yMax)
        {
            var box = new Bounds(new Vector3(cx, (yMin + yMax) / 2f, cz), new Vector3(hx * 2f, yMax - yMin, hz * 2f));
            foreach (MeshFilter mf in obstacles)
            {
                Bounds b = mf.GetComponent<Renderer>().bounds;
                if (!b.Intersects(box)) continue;
                if (b.size.x < 40f && b.size.z < 40f) return mf.name;      // small thing nearby: its box is close enough to the truth
                if (!worldVerts.TryGetValue(mf, out Vector3[] w))
                {
                    Vector3[] v = mf.sharedMesh.vertices;
                    Matrix4x4 m = mf.transform.localToWorldMatrix;
                    w = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
                    worldVerts[mf] = w;
                }
                foreach (Vector3 p in w) if (box.Contains(p)) return mf.name;
            }
            return null;
        }

        // The flattest clear patch of terrain of the given size (metres, x by z), nearest to 'near' (x, z)
        // 'taken' lists other puzzles' sites as (x, z, radius): the new site keeps clear of them.
        public Vector3 FindSite(Vector2 near, Vector2 size, List<Vector3> taken = null, float minDist = 15f, float maxDist = 300f)
        {
            float hx = size.x / 2f, hz = size.y / 2f;
            float radius = Mathf.Sqrt(hx * hx + hz * hz);
            var candidates = new List<(float dist, float x, float z, float min, float max)>();
            for (float dz = -maxDist; dz <= maxDist; dz += 3f)
                for (float dx = -maxDist; dx <= maxDist; dx += 3f)
                {
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist < minDist || dist > maxDist) continue;
                    float x = near.x + dx, z = near.y + dz;
                    if (taken != null && taken.Any(t => (new Vector2(t.x, t.y) - new Vector2(x, z)).magnitude < t.z + radius + 6f)) continue;
                    if (!Range(x, z, hx, hz, out float min, out float max) || max - min > 1.5f) continue;
                    candidates.Add((dist, x, z, min, max));
                }
            foreach (float tolerance in new[] { 0.4f, 0.8f, 1.5f })
                foreach (var c in candidates.Where(c => c.max - c.min <= tolerance).OrderBy(c => c.dist))
                {
                    if (Blocker(c.x, c.z, hx + 2f, hz + 2f, c.min - 1f, c.max + 12f) != null) continue;
                    Debug.Log("[BWM] Site found: " + Sdk.F(c.x) + ", " + Sdk.F(c.z) + " (ground " + Sdk.F(c.min) + " to " + Sdk.F(c.max) + " m, " + Sdk.F(c.dist) + " m from the reference point)");
                    return new Vector3(c.x, (c.min + c.max) / 2f, c.z);
                }
            throw new Exception("No flat, clear site of " + size.x + " x " + size.y + " m within " + maxDist + " m of " + near);
        }
    }
}
