// Winter Walk's set dressing: copies of the game's own granite rocks, trees, shrubs and logs, placed over the
// Winter Isle and given snow.
//
// The island's build (WinterIslePuzzle.cs) decides where everything goes and writes it to WinterIsle.dressing
// beside this plugin. When a walk loads, the dresser finds one of the game's own objects of each kind (rocks are
// objects in the world; trees are the terrains' tree prefabs), copies it to every place in the list, takes out
// the parts that would save it, move it or hide it, and adds snow:
//   - rocks and logs get our own snow material drawn over their surface (BWM/SnowOverlay, from the island's pack)
//   - trees and shrubs use their own shader's snow layer ("overlay", which the game leaves off), so the snow
//     sways with the leaves and stays on when the tree turns into its far-away impostor
// Every player builds the same copies from the same list, so the rocks they collide with are the same.
// When the list changes on disk (the island was rebuilt), the island is dressed again without a restart.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WinterWalk
{
    public class Dresser : MonoBehaviour
    {
        public Dresser(IntPtr ptr) : base(ptr) { }

        struct Entry { public string kind; public Vector3 at; public Quaternion turn; public float scale; public float snow; }

        static readonly HashSet<string> Removed = new HashSet<string>
        {
            "SaveIdentity", "BuoySaver", "PeckEffectSavableHome", "PeckEffectGourdMap", "LandmarkSaveSetter",
            "CullingLocation", "TerrainAligner", "ClearStamp",
        };

        // The island's box, read from the list (the sky uses it); these are the island as first built
        public static float X0 = -1016f, X1 = -428f, Z0 = -1016f, Z1 = -560f;

        static string file;
        static DateTime fileTime;
        static List<Entry> entries = new List<Entry>();
        static bool dirty;
        static float nextCheck;

        GameObject holder;
        int next = -1;      // the next entry to copy while dressing; -1 when idle
        float started;
        int made, missing;
        readonly Dictionary<string, GameObject> rocks = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> trees = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> firs = new Dictionary<string, GameObject>();
        readonly Dictionary<int, Material> rockSnow = new Dictionary<int, Material>();
        readonly HashSet<string> warned = new HashSet<string>();
        Material overlay;

        public static int Count => entries.Count;

        static float N(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Load()
        {
            var list = new List<Entry>();
            foreach (string raw in File.ReadAllLines(file))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] p = raw.Split('\t');
                if (p[0] == "@bounds" && p.Length >= 5) { X0 = N(p[1]); X1 = N(p[2]); Z0 = N(p[3]); Z1 = N(p[4]); continue; }
                if (p.Length < 9) continue;
                list.Add(new Entry
                {
                    kind = p[0],
                    at = new Vector3(N(p[1]), N(p[2]), N(p[3])),
                    turn = Quaternion.Euler(N(p[5]), N(p[4]), N(p[6])),
                    scale = N(p[7]),
                    snow = N(p[8]),
                });
            }
            entries = list;
            Plugin.L.LogInfo("Winter Walk: read " + list.Count + " placements from " + Path.GetFileName(file));
        }

        static string Base(string name)
        {
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }

        static string Full(Transform t)
        {
            string s = t.name;
            for (Transform c = t.parent; c != null; c = c.parent) s = c.name + "/" + s;
            return s;
        }

        void Templates()
        {
            rocks.Clear(); trees.Clear(); firs.Clear();
            foreach (Transform t in FindObjectsOfType<Transform>(true))
                if (t.name.StartsWith("Fir Model ") && t.parent != null && t.parent.name == "Fir Models") firs[t.name.Substring(10)] = t.gameObject;
            foreach (LODGroup g in FindObjectsOfType<LODGroup>(true))
            {
                string kind = Base(g.name);
                if (!kind.StartsWith("rock-") || rocks.ContainsKey(kind) || g.transform.root.name == "Winter Dressing" || Full(g.transform).Contains("_OLD")) continue;
                rocks[kind] = g.gameObject;
            }
            foreach (Terrain terrain in FindObjectsOfType<Terrain>())
                foreach (TreePrototype proto in terrain.terrainData.treePrototypes)
                    if (proto.prefab != null && !trees.ContainsKey(proto.prefab.name)) trees[proto.prefab.name] = proto.prefab;
        }

        static Material FindMaterial(string name)
        {
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m != null && m.name == name) return m;
            }
            return null;
        }

        Material RockSnow(float line)
        {
            int key = Mathf.RoundToInt(line * 20f);
            if (rockSnow.TryGetValue(key, out Material m)) return m;
            m = new Material(overlay) { name = "SnowOverlay rock " + key };
            m.SetFloat("_SnowLine", key / 20f);
            m.SetFloat("_Cull", 2f);
            rockSnow[key] = m;
            return m;
        }

        // Trees: their shader's own snow layer is compiled out of the game, so they get ours too. Their sway is
        // stilled (cold, still air), so our snow stays on the leaves it was laid on; the leaf texture's cut-out
        // keeps snow off the empty parts of the leaf cards. Far-away impostors are only paled.
        readonly Dictionary<IntPtr, Material> stilled = new Dictionary<IntPtr, Material>();
        readonly Dictionary<string, Material> leafSnow = new Dictionary<string, Material>();

        Material Stilled(Material original, bool impostor)
        {
            if (original == null) return null;
            if (stilled.TryGetValue(original.Pointer, out Material m)) return m;
            m = new Material(original) { name = original.name + " (winter)" };
            if (m.HasProperty("_MotionIntensityValue")) m.SetFloat("_MotionIntensityValue", 0f);
            if (impostor && m.HasProperty("_ImpostorColor")) m.SetColor("_ImpostorColor", m.GetColor("_ImpostorColor") * new Color(1.25f, 1.3f, 1.4f, 1f));
            stilled[original.Pointer] = m;
            return m;
        }

        Material LeafSnow(Material original, float line)
        {
            Texture leaves = original != null && original.HasProperty("_MainAlbedoTex") ? original.GetTexture("_MainAlbedoTex") : null;
            string key = (leaves != null ? leaves.GetInstanceID().ToString() : "none") + "/" + Mathf.RoundToInt(line * 20f);
            if (leafSnow.TryGetValue(key, out Material m)) return m;
            m = new Material(overlay) { name = "SnowOverlay leaves " + key };
            m.SetFloat("_SnowLine", Mathf.Round(line * 20f) / 20f);
            m.SetFloat("_Cull", 0f);
            if (leaves != null)
            {
                m.SetTexture("_AlphaTex", leaves);
                m.SetFloat("_Cutoff", original.HasProperty("_MainAlphaClipValue") ? Mathf.Max(0.05f, original.GetFloat("_MainAlphaClipValue")) : 0.3f);
            }
            leafSnow[key] = m;
            return m;
        }

        void Copy(Entry e)
        {
            bool tree = e.kind.StartsWith("tree:"), fir = e.kind.StartsWith("fir:");
            string kind = tree ? e.kind.Substring(5) : fir ? e.kind.Substring(4) : e.kind;
            GameObject template = null;
            if (tree) trees.TryGetValue(kind, out template); else if (fir) firs.TryGetValue(kind, out template); else rocks.TryGetValue(kind, out template);
            if (template == null)
            {
                missing++;
                if (warned.Add(e.kind)) Plugin.L.LogWarning("Winter Walk: no '" + e.kind + "' in this world to copy");
                return;
            }
            GameObject copy = Instantiate((UnityEngine.Object)template, holder.transform).Cast<GameObject>();
            copy.name = kind;
            var doomed = new List<Component>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
                if (c != null && Removed.Contains(c.GetIl2CppType().Name)) doomed.Add(c);
            foreach (Component c in doomed) DestroyImmediate(c);
            copy.transform.localPosition = e.at;
            copy.transform.localRotation = e.turn;
            copy.transform.localScale = Vector3.one * e.scale;
            if (fir) { copy.SetActive(true); made++; return; }      // our own firs: snow is in their shader
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true))
            {
                Renderer rend = c.TryCast<Renderer>();
                if (rend == null) continue;
                rend.enabled = true;
                var old = rend.sharedMaterials;
                if (tree)
                {
                    bool impostor = rend.name.Contains("Impostor");
                    bool snowy = e.snow > -5f && !impostor;
                    var mats = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(old.Length + (snowy ? 1 : 0));
                    for (int i = 0; i < old.Length; i++) mats[i] = Stilled(old[i], impostor);
                    if (snowy) mats[old.Length] = LeafSnow(old.Length > 0 ? old[0] : null, e.snow);
                    rend.sharedMaterials = mats;
                }
                else if (e.snow < -5f) continue;      // no snow on this one
                else
                {
                    var mats = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(old.Length + 1);
                    for (int i = 0; i < old.Length; i++) mats[i] = old[i];
                    mats[old.Length] = RockSnow(e.snow);
                    rend.sharedMaterials = mats;
                }
            }
            made++;
        }

        void Update()
        {
            try
            {
                if (Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + 1f;
                    if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.dressing");
                    if (File.Exists(file))
                    {
                        DateTime t = File.GetLastWriteTimeUtc(file);
                        if (t != fileTime) { fileTime = t; Load(); dirty = true; }
                    }
                    // Dress when the island is here (its pack carries our material) and not yet dressed
                    if (next < 0 && (holder == null || dirty) && entries.Count > 0 && GameObject.Find("Material Store") != null)
                    {
                        overlay = FindMaterial("SnowOverlay");
                        if (overlay != null) Begin();
                    }
                }
                if (next >= 0) Step();
            }
            catch (Exception ex)
            {
                Plugin.L.LogWarning("Winter Walk: dressing stopped: " + ex.GetType().Name + ": " + ex.Message);
                next = -1;
                dirty = false;
            }
        }

        // The island's own snow material shows bare ground, sand and cliffs where the snow is thin; it borrows
        // the game's textures for them (its granite, heathland and dry sand)
        void LendTextures()
        {
            Texture granite = null, heath = null, sand = null;
            foreach (var kv in rocks)
            {
                Renderer rend = kv.Value.GetComponentInChildren<Renderer>(true);
                if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_MainTex")) { granite = rend.sharedMaterial.GetTexture("_MainTex"); break; }
            }
            foreach (Terrain terrain in FindObjectsOfType<Terrain>())
            {
                foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                {
                    if (layer == null || layer.diffuseTexture == null) continue;
                    if (layer.name.Contains("Heathland") && heath == null) heath = layer.diffuseTexture;
                    if (layer.name.Contains("SandDry") && sand == null) sand = layer.diffuseTexture;
                }
                if (heath != null && sand != null) break;
            }
            int lent = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m == null || m.shader == null || m.shader.name != "BWM/Snow") continue;
                if (granite != null) m.SetTexture("_RockTex", granite);
                if (heath != null) m.SetTexture("_BareTex", heath);
                if (sand != null) m.SetTexture("_SandTex", sand);
                lent++;
            }
            Plugin.L.LogInfo("Winter Walk: lent the snow " + (granite != null ? "granite " : "") + (heath != null ? "heath " : "") + (sand != null ? "sand " : "") + "to " + lent + " material(s)");
        }

        void Begin()
        {
            dirty = false;
            if (holder != null) Destroy(holder);
            rockSnow.Clear(); stilled.Clear(); leafSnow.Clear(); warned.Clear();
            Templates();
            try { LendTextures(); } catch (Exception ex) { Plugin.L.LogWarning("Winter Walk: textures not lent: " + ex.Message); }
            holder = new GameObject("Winter Dressing");
            holder.SetActive(false);      // nothing wakes until every copy is cleaned
            next = 0; made = 0; missing = 0;
            started = Time.realtimeSinceStartup;
        }

        void Step()
        {
            float budget = Time.realtimeSinceStartup + 0.012f;      // about 12 ms of copying per frame
            while (next < entries.Count && Time.realtimeSinceStartup < budget) Copy(entries[next++]);
            if (next < entries.Count) return;
            next = -1;
            holder.SetActive(true);
            Plugin.L.LogInfo("Winter Walk: dressed the island with " + made + " copies (" + missing + " missing) in "
                + (Time.realtimeSinceStartup - started).ToString("0.0", CultureInfo.InvariantCulture) + " s; "
                + rocks.Count + " rock kinds, " + trees.Count + " tree kinds and " + firs.Count + " fir models to copy from");
        }
    }
}
