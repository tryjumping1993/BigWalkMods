// BigPuzzleSDK: shows what a Terrain Hole (PuzzleTerrainHole) does in the game on the BigWalk map preview: the preview terrain is cut open
// where the hole's collider touches it, and the preview's rocks (found by name, like the game does) inside it are hidden. Only the preview is
// changed; it follows the Terrain Hole as you move it, and everything is put back when the Terrain Hole is removed or the editor recompiles.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK {

[InitializeOnLoad]
public static class BigWalkMapPuzzleHoles {
    private const string RootName = "BigWalk Map";
    private const double CheckEvery = 0.2;       // seconds between looks at the Terrain Holes
    private const float Margin = 0.5f;           // how close to the collider counts as touching, like the game
    private const float TerrainMargin = 1.5f;    // the preview terrain has a point every 2 m, so it needs a bit more to cut small holes

    private static double nextCheck;
    private static int lastSignature;
    private static readonly Dictionary<int, int[]> baseline = new Dictionary<int, int[]>();   // preview terrain mesh -> its triangles with no puzzle holes
    private static readonly Dictionary<int, Mesh> cutMeshes = new Dictionary<int, Mesh>();
    private static readonly List<Renderer> hiddenRocks = new List<Renderer>();
    private static List<Renderer> rocks;
    private static int rocksForRoot;

    static BigWalkMapPuzzleHoles() {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;      // so nothing half-cut is left behind across a recompile
    }

    private static void Update() {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + CheckEvery;
        GameObject root = GameObject.Find(RootName);
        PuzzleTerrainHole[] holes = root == null ? new PuzzleTerrainHole[0] : Object.FindObjectsByType<PuzzleTerrainHole>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        // Look at the holes' shapes first, and only redo the preview when something changed
        var areas = new List<HoleArea>();
        int signature = root == null ? 0 : root.GetInstanceID();
        foreach (PuzzleTerrainHole h in holes) {
            var so = new SerializedObject(h);
            bool cut = Flag(so, "cutHoles"), rock = Flag(so, "removeRocks");
            if (!cut && !rock) continue;
            Collider[] colliders = h.GetComponentsInChildren<Collider>(false);
            if (colliders.Length == 0) continue;
            var a = new HoleArea { colliders = colliders, cut = cut, rocks = rock };
            a.bounds = colliders[0].bounds;
            foreach (Collider c in colliders) a.bounds.Encapsulate(c.bounds);
            areas.Add(a);
            signature = signature * 31 + a.bounds.center.GetHashCode() * 7 + a.bounds.size.GetHashCode() + (cut ? 1 : 0) + (rock ? 2 : 0);
        }
        if (signature == lastSignature) return;

        RestoreAll();
        lastSignature = signature;
        if (root == null || areas.Count == 0) return;
        foreach (HoleArea a in areas) if (a.cut) CutTerrain(root, a);
        if (areas.Exists(a => a.rocks)) HideRocks(root, areas);
    }

    private static bool Flag(SerializedObject so, string name) {
        SerializedProperty p = so.FindProperty(name);
        return p == null || p.boolValue;
    }

    private class HoleArea { public Collider[] colliders; public Bounds bounds; public bool cut, rocks; }

    // True when the point is inside (or within 'margin' metres of) one of the hole's colliders. Done with the colliders' own shapes and transforms,
    // because Unity's physics isn't kept up to date for objects moved in the editor
    private static bool Touches(HoleArea a, Vector3 p, float margin) {
        Bounds b = a.bounds; b.Expand(margin * 2f);
        if (!b.Contains(new Vector3(p.x, b.center.y, p.z)) || p.y < b.min.y || p.y > b.max.y) return false;
        foreach (Collider c in a.colliders) {
            if (c == null || !c.enabled) continue;
            if (c is BoxCollider box) {
                Vector3 s = c.transform.lossyScale;
                Vector3 l = c.transform.InverseTransformPoint(p) - box.center;
                Vector3 d = new Vector3(Mathf.Max(0f, Mathf.Abs(l.x) - box.size.x * 0.5f) * Mathf.Abs(s.x), Mathf.Max(0f, Mathf.Abs(l.y) - box.size.y * 0.5f) * Mathf.Abs(s.y), Mathf.Max(0f, Mathf.Abs(l.z) - box.size.z * 0.5f) * Mathf.Abs(s.z));
                if (d.sqrMagnitude <= margin * margin) return true;
            } else if (c is SphereCollider sph) {
                Vector3 s = c.transform.lossyScale;
                float r = sph.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                if ((c.transform.TransformPoint(sph.center) - p).magnitude - r <= margin) return true;
            } else {
                if ((c.bounds.ClosestPoint(p) - p).sqrMagnitude <= margin * margin) return true;      // capsules and meshes: their bounding box
            }
        }
        return false;
    }

    // ---- terrain ----

    private static void CutTerrain(GameObject root, HoleArea area) {
        foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true)) {
            if (f == null || f.sharedMesh == null || f.sharedMesh.vertexCount < 100000) continue;      // the terrain tiles are the only big grid meshes
            Mesh mesh = f.sharedMesh;
            Bounds mb = mesh.bounds; mb.Expand(TerrainMargin * 2f);
            if (!mb.Intersects(area.bounds)) continue;
            int id = mesh.GetInstanceID();
            if (!baseline.TryGetValue(id, out int[] tris)) { tris = mesh.triangles; baseline[id] = tris; }
            cutMeshes[id] = mesh;
            // the terrain's own triangles as they are now (an earlier hole in this pass may already have cut some)
            int[] now = mesh.triangles;
            Vector3[] v = mesh.vertices;
            var kept = new List<int>(now.Length);
            for (int i = 0; i + 2 < now.Length; i += 3) {
                Vector3 p = (v[now[i]] + v[now[i + 1]] + v[now[i + 2]]) / 3f;
                if (!Touches(area, p, TerrainMargin)) { kept.Add(now[i]); kept.Add(now[i + 1]); kept.Add(now[i + 2]); }
            }
            if (kept.Count == now.Length) continue;
            mesh.triangles = kept.ToArray();
        }
    }

    // ---- rocks ----

    // True if the hole's colliders reach into this box (sampled on a 3 x 3 x 3 grid), or the hole's middle is inside it
    private static bool TouchesBounds(HoleArea a, Bounds r) {
        if (r.Contains(a.bounds.center)) return true;
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    if (Touches(a, r.min + Vector3.Scale(r.size, new Vector3(x * 0.5f, y * 0.5f, z * 0.5f)), 0f)) return true;
        return false;
    }

    private static bool RockName(string n) {
        n = n.ToLowerInvariant();
        return n.Contains("rock") || n.Contains("boulder") || n.Contains("stone") || n.Contains("cliff");
    }

    private static void HideRocks(GameObject root, List<HoleArea> areas) {
        if (rocks == null || rocksForRoot != root.GetInstanceID()) {
            rocks = new List<Renderer>();
            rocksForRoot = root.GetInstanceID();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) {
                Transform t = r.transform;
                for (int up = 0; up < 4 && t != null && t != root.transform; up++, t = t.parent)
                    if (RockName(t.name)) { rocks.Add(r); break; }
            }
        }
        foreach (Renderer r in rocks) {
            if (r == null || !r.enabled) continue;
            foreach (HoleArea a in areas) {
                if (!a.rocks || !a.bounds.Intersects(r.bounds) || !TouchesBounds(a, r.bounds)) continue;
                r.enabled = false;
                hiddenRocks.Add(r);
                break;
            }
        }
    }

    // ---- put everything back ----

    private static void RestoreAll() {
        foreach (var kv in cutMeshes) if (kv.Value != null && baseline.TryGetValue(kv.Key, out int[] tris)) kv.Value.triangles = tris;
        cutMeshes.Clear();
        foreach (Renderer r in hiddenRocks) if (r != null) r.enabled = true;
        hiddenRocks.Clear();
    }
}

}
