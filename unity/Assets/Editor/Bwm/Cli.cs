// Command-line entry points. Run through tools\bwm.ps1, which calls
//   Unity.exe -batchmode -projectPath unity -executeMethod Bwm.Cli.<Method> [-bwmPuzzle <Name>] [flags]
// Every line this code logs starts with "[BWM]"; the tools print just those.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Bwm
{
    public static class Cli
    {
        static readonly string RepoDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string PluginsDir => Path.Combine(RepoDir, "profile", "BepInEx", "plugins");
        static string PreviewDir => Path.Combine(RepoDir, "previews");

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static void Run(Action action)
        {
            try
            {
                action();
                Debug.Log("[BWM] OK");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[BWM] FAILED: " + Sdk.Unwrap(e));
                EditorApplication.Exit(1);
            }
        }

        static List<PuzzleDef> Defs()
        {
            var defs = new List<PuzzleDef>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<PuzzleDef>())
                if (!t.IsAbstract) defs.Add((PuzzleDef)Activator.CreateInstance(t));
            return defs.OrderBy(d => d.Name).ToList();
        }

        // ---- entry points ----

        // One-off after creating the project: a URP pipeline asset (the game uses URP)
        public static void SetupProject() => Run(() => { EnsureUrp(); AssetDatabase.SaveAssets(); });

        public static void List() => Run(() =>
        {
            foreach (PuzzleDef d in Defs()) Debug.Log("[BWM] Puzzle: " + d.Name + " (" + d.GetType().Name + ", " + d.Footprint.x + " x " + d.Footprint.y + " m)");
        });

        // -bwmPuzzle <Name> [-bwmResite] [-bwmNoPreview] [-bwmNoExport] [-bwmAllowIssues]
        public static void Build() => Run(() =>
        {
            string name = Arg("-bwmPuzzle") ?? throw new Exception("Pass -bwmPuzzle <Name>");
            PuzzleDef def = Defs().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("No puzzle named '" + name + "'. Known: " + string.Join(", ", Defs().Select(d => d.Name)));
            BuildOne(def);
        });

        public static void BuildAll() => Run(() => { foreach (PuzzleDef d in Defs()) BuildOne(d); });

        public static void DumpWorld() => Run(() =>
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int n = World.Dump(World.ImportMap(), Path.Combine(RepoDir, "world", "world-index.tsv"));
            Debug.Log("[BWM] World index: " + n + " objects");
        });

        // Writes world\part-metrics.tsv: the size of every SDK part as the game's own model, relative to its pivot
        // (x right, y up, z forward), so definitions can place parts against walls and floors without guessing.
        public static void PartMetrics() => Run(() =>
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorPrefs.SetString("BigPuzzleSDK.GameDataPath", World.GameData);
            Sdk.Call(Sdk.T("BigWalkGameModels"), null, "Import", World.GameData);
            var root = new GameObject("Metrics");
            root.AddComponent(Sdk.T("PuzzleSDK"));
            var instances = new List<GameObject>();
            foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "BigPuzzleSDK"), "*.prefab").OrderBy(p => p))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BigPuzzleSDK/" + Path.GetFileName(path));
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                go.transform.position = new Vector3(instances.Count * 40f, 0f, 0f);
                instances.Add(go);
            }
            Type previews = Sdk.T("GamePreviews");
            previews.GetField("nextTick", Sdk.Any).SetValue(null, 0.0);
            Sdk.Call(previews, null, "Tick");
            var table = (System.Collections.IDictionary)previews.GetField("previews", Sdk.Any).GetValue(null);
            var bounds = new Dictionary<GameObject, Bounds>();
            foreach (System.Collections.DictionaryEntry entry in table)
            {
                var owner = (MonoBehaviour)entry.Key;
                var previewRoot = (GameObject)entry.Value.GetType().GetField("root", Sdk.Any).GetValue(entry.Value);
                if (owner == null || previewRoot == null) continue;
                GameObject instance = instances.FirstOrDefault(i => owner.transform == i.transform || owner.transform.IsChildOf(i.transform));
                if (instance == null) continue;
                foreach (Renderer r in previewRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (bounds.TryGetValue(instance, out Bounds b)) { b.Encapsulate(r.bounds); bounds[instance] = b; }
                    else bounds[instance] = r.bounds;
                }
            }
            var sb = new System.Text.StringBuilder("part\tsizeX\tsizeY\tsizeZ\tminX\tmaxX\tminY\tmaxY\tminZ\tmaxZ\n");
            foreach (GameObject i in instances)
            {
                if (!bounds.TryGetValue(i, out Bounds b)) { sb.Append(i.name).Append("\t(no preview model)\n"); continue; }
                Vector3 min = b.min - i.transform.position, max = b.max - i.transform.position;
                sb.Append(i.name).Append('\t').Append(Sdk.F(b.size.x)).Append('\t').Append(Sdk.F(b.size.y)).Append('\t').Append(Sdk.F(b.size.z)).Append('\t')
                  .Append(Sdk.F(min.x)).Append('\t').Append(Sdk.F(max.x)).Append('\t').Append(Sdk.F(min.y)).Append('\t').Append(Sdk.F(max.y)).Append('\t')
                  .Append(Sdk.F(min.z)).Append('\t').Append(Sdk.F(max.z)).Append('\n');
            }
            string file = Path.Combine(RepoDir, "world", "part-metrics.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[BWM] Part metrics: " + bounds.Count + " of " + instances.Count + " parts measured, written to " + file);
        });

        // ---- building ----

        [Serializable]
        sealed class Site { public float x, z, yaw; }

        static void BuildOne(PuzzleDef def)
        {
            string name = def.Name;
            if (string.IsNullOrEmpty(name) || !name.All(char.IsLetterOrDigit)) throw new Exception("Puzzle names are letters and digits only: '" + name + "'");
            Debug.Log("[BWM] Building " + name);
            EnsureUrp();

            string assetDir = "Assets/Puzzles/" + name;
            if (!AssetDatabase.IsValidFolder("Assets/Puzzles")) AssetDatabase.CreateFolder("Assets", "Puzzles");
            if (!AssetDatabase.IsValidFolder(assetDir)) AssetDatabase.CreateFolder("Assets/Puzzles", name);
            string scenePath = assetDir + "/" + name + ".unity";

            // The scene is kept between builds only for the root object's identity: the SDK ties the puzzle's
            // permanent id to it. Everything under the root is rebuilt from the definition every time.
            Scene scene;
            if (File.Exists(scenePath)) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new Exception("Could not create " + scenePath);
            }

            GameObject map = World.ImportMap();
            var ground = new Ground(map);
            SceneManager.SetActiveScene(scene);

            // Site: pinned by the definition, remembered from the last build, or searched for
            string siteFile = Path.Combine(Application.dataPath, "Puzzles", name, "site.json");
            Site site = null;
            if (def.FixedSite.HasValue) site = new Site { x = def.FixedSite.Value.x, z = def.FixedSite.Value.y, yaw = def.Yaw };
            else if (File.Exists(siteFile) && !Flag("-bwmResite")) site = JsonUtility.FromJson<Site>(File.ReadAllText(siteFile));
            if (site == null)
            {
                // Keep clear of every other puzzle's remembered site (x, z, radius)
                var taken = new List<Vector3>();
                foreach (PuzzleDef other in Defs())
                {
                    string otherFile = Path.Combine(Application.dataPath, "Puzzles", other.Name, "site.json");
                    if (other.Name == name || !File.Exists(otherFile)) continue;
                    Site s = JsonUtility.FromJson<Site>(File.ReadAllText(otherFile));
                    taken.Add(new Vector3(s.x, s.z, other.Footprint.magnitude / 2f));
                }
                Vector3 found = ground.FindSite(def.Near, def.Footprint, taken);
                site = new Site { x = found.x, z = found.z, yaw = def.Yaw };
            }
            site.yaw = def.Yaw;
            File.WriteAllText(siteFile, JsonUtility.ToJson(site, true));
            if (!ground.At(site.x, site.z, out float y)) throw new Exception("The site " + site.x + ", " + site.z + " is over a hole in the terrain");
            Debug.Log("[BWM] Site: " + Sdk.F(site.x) + ", " + Sdk.F(y) + ", " + Sdk.F(site.z) + " yaw " + Sdk.F(site.yaw));

            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (root == null)
            {
                root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (Component c in root.GetComponents<Component>()) if (!(c is Transform)) UnityEngine.Object.DestroyImmediate(c);
            root.transform.SetPositionAndRotation(new Vector3(site.x, y, site.z), Quaternion.Euler(0f, site.yaw, 0f));
            root.AddComponent(Sdk.T("PuzzleSDK"));

            def.Build(new PuzzleBuilder(root, ground, assetDir));
            Debug.Log("[BWM] Built " + root.GetComponentsInChildren<Transform>(true).Length + " objects");

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + scenePath);
            if (!Flag("-bwmNoPreview")) Preview.Render(root, name, PreviewDir, Mathf.Max(def.Footprint.x, def.Footprint.y) / 2f);
            Check(root);
            if (!Flag("-bwmNoExport")) Export(root);
        }

        // The SDK's own checks (missing pillow, unlinked boards, ...). Any issue stops the build unless -bwmAllowIssues.
        static void Check(GameObject root)
        {
            var issues = (System.Collections.IEnumerable)Sdk.Call(Sdk.T("PuzzleChecks"), null, "ForPuzzle", root.GetComponent(Sdk.T("PuzzleSDK")));
            int count = 0;
            foreach (object issue in issues)
            {
                count++;
                Debug.LogWarning("[BWM] Check: " + issue.GetType().GetField("message", Sdk.Any).GetValue(issue));
            }
            Debug.Log("[BWM] SDK checks: " + count + " issue(s)");
            if (count > 0 && !Flag("-bwmAllowIssues")) throw new Exception(count + " SDK check issue(s); fix them or pass -bwmAllowIssues");
        }

        // The SDK's exporter, into the mod profile's dev folder (plugins\BigPuzzleSDK: Ctrl+F1 / Ctrl+F2 work on puzzles there)
        static void Export(GameObject root)
        {
            Directory.CreateDirectory(PluginsDir);
            EditorPrefs.SetString("BigPuzzleSDK.ExportFolder", PluginsDir);
            string pack = Path.Combine(PluginsDir, "BigPuzzleSDK", root.name + ".puzzlepack");
            DateTime before = File.Exists(pack) ? File.GetLastWriteTimeUtc(pack) : DateTime.MinValue;
            Sdk.Call(Sdk.T("PuzzleExporterWindow"), null, "AutoExport", root.scene, false);
            if (!File.Exists(pack) || File.GetLastWriteTimeUtc(pack) <= before) throw new Exception("The SDK's export did not write " + pack + " (see the Unity log)");
            string id = (string)Sdk.Call(Sdk.T("PuzzleIds"), null, "Find", root);
            Debug.Log("[BWM] Exported " + pack + " (" + new FileInfo(pack).Length + " bytes, puzzle id " + id + ")");
        }

        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;
            const string dir = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Settings");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(dir + "/BwmURP.asset");
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, dir + "/BwmURP_Renderer.asset");
                ResourceReloader.TryReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, dir + "/BwmURP.asset");
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            EditorUtility.SetDirty(asset);
            Debug.Log("[BWM] URP pipeline asset assigned");
        }
    }
}
