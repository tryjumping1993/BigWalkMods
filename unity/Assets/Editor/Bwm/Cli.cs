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

        // -bwmPuzzle <Name>[,<Name>...] [-bwmResite] [-bwmNoPreview] [-bwmNoExport] [-bwmAllowIssues]: several names, comma
        // separated, are built one after another in one editor session, in the order given
        public static void Build() => Run(() =>
        {
            string names = Arg("-bwmPuzzle") ?? throw new Exception("Pass -bwmPuzzle <Name>");
            var defs = new List<PuzzleDef>();
            foreach (string raw in names.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                defs.Add(Defs().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception("No puzzle named '" + name + "'. Known: " + string.Join(", ", Defs().Select(d => d.Name))));
            }
            foreach (PuzzleDef def in defs) BuildOne(def);
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

        // Writes world\heightmap.png (the whole 2048 m square at 4 m per pixel, north up) and
        // world\heightmap-zoom.png (-bwmCx/-bwmCz/-bwmSize in metres, default the coast by the opening area, 1 m per pixel).
        // Sea (below height 0) is blue, land is shaded by height with a line every 10 m, holes are magenta,
        // the spawn rooms are a red dot and every puzzle site a yellow one.
        public static void Survey() => Run(() =>
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = new Ground(World.ImportMap());
            var marks = new List<(Vector2 at, Color color)> { (World.Spawn, Color.red) };
            foreach (PuzzleDef d in Defs())
            {
                string f = Path.Combine(Application.dataPath, "Puzzles", d.Name, "site.json");
                if (!File.Exists(f)) continue;
                Site s = JsonUtility.FromJson<Site>(File.ReadAllText(f));
                marks.Add((new Vector2(s.x, s.z), Color.yellow));
            }
            string dir = Path.Combine(RepoDir, "world");
            Directory.CreateDirectory(dir);
            float land = HeightImage(ground, 0f, 0f, 2048f, 512, marks, Path.Combine(dir, "heightmap.png"));
            Debug.Log("[BWM] Survey: land above sea level is about " + Sdk.F(land / 1e6f) + " km2 of the 4.19 km2 square");
            float cx = float.Parse(Arg("-bwmCx") ?? "-300", System.Globalization.CultureInfo.InvariantCulture);
            float cz = float.Parse(Arg("-bwmCz") ?? "-700", System.Globalization.CultureInfo.InvariantCulture);
            float size = float.Parse(Arg("-bwmSize") ?? "800", System.Globalization.CultureInfo.InvariantCulture);
            HeightImage(ground, cx, cz, size, 800, marks, Path.Combine(dir, "heightmap-zoom.png"));
            Debug.Log("[BWM] Survey: zoom is " + size + " m wide, centred on " + cx + ", " + cz);
        });

        // Renders a square of the world to a PNG; returns the land area in it (m2)
        static float HeightImage(Ground ground, float cx, float cz, float size, int pixels, List<(Vector2 at, Color color)> marks, string file)
        {
            float step = size / pixels, land = 0f;
            var tex = new Texture2D(pixels, pixels, TextureFormat.RGB24, false);
            var colors = new Color[pixels * pixels];
            for (int j = 0; j < pixels; j++)
                for (int i = 0; i < pixels; i++)
                {
                    float x = cx - size / 2f + (i + 0.5f) * step, z = cz - size / 2f + (j + 0.5f) * step;
                    Color c;
                    if (!ground.At(x, z, out float y)) c = (x < -1024f || x > 1024f || z < -1024f || z > 1024f) ? new Color(0.05f, 0.1f, 0.25f) : Color.magenta;
                    else if (y < 0f) c = Color.Lerp(new Color(0.45f, 0.7f, 0.9f), new Color(0.08f, 0.2f, 0.45f), Mathf.Clamp01(-y / 20f));
                    else
                    {
                        land += step * step;
                        float t = Mathf.Clamp01(y / 160f);
                        c = t < 0.5f ? Color.Lerp(new Color(0.55f, 0.75f, 0.45f), new Color(0.75f, 0.65f, 0.4f), t * 2f) : Color.Lerp(new Color(0.75f, 0.65f, 0.4f), Color.white, t * 2f - 1f);
                        if (y % 10f < 10f * step / 30f + 0.35f) c *= 0.75f;      // a line every 10 m of height
                    }
                    // a grid line every 100 m, heavier every 500 m
                    float gx = Mathf.Abs(x % 100f), gz = Mathf.Abs(z % 100f);
                    if (gx < step || gz < step) c = Color.Lerp(c, Color.black, (Mathf.Abs(x % 500f) < step || Mathf.Abs(z % 500f) < step) ? 0.45f : 0.15f);
                    colors[j * pixels + i] = c;
                }
            foreach (var m in marks)
            {
                int mi = Mathf.RoundToInt((m.at.x - (cx - size / 2f)) / step), mj = Mathf.RoundToInt((m.at.y - (cz - size / 2f)) / step);
                int r = Mathf.Max(3, pixels / 160);
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                    {
                        int pi = mi + di, pj = mj + dj;
                        if (pi < 0 || pj < 0 || pi >= pixels || pj >= pixels || di * di + dj * dj > r * r) continue;
                        colors[pj * pixels + pi] = m.color;
                    }
            }
            tex.SetPixels(colors);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("[BWM] Survey image: " + file);
            return land;
        }

        // ---- building ----

        [Serializable]
        sealed class Site { public float x, z, yaw; }

        static void BuildOne(PuzzleDef def)
        {
            string name = def.Name;
            if (string.IsNullOrEmpty(name) || !name.All(char.IsLetterOrDigit)) throw new Exception("Puzzle names are letters and digits only: '" + name + "'");
            Debug.Log("[BWM] Building " + name);
            EnsureUrp();
            KeepShaderVariants();

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
            var ground = new Ground(map, name);
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
                    if (other.Name == name || other.PuzzleType == 2 || !File.Exists(otherFile)) continue;      // land and scenery packs are built on, not avoided
                    Site s = JsonUtility.FromJson<Site>(File.ReadAllText(otherFile));
                    taken.Add(new Vector3(s.x, s.z, other.Footprint.magnitude / 2f));
                }
                Vector3 found = ground.FindSite(def.Near, def.Footprint, taken, def.MinDistance, def.MaxDistance);
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
            Sdk.Set(root.AddComponent(Sdk.T("PuzzleSDK")), "puzzleType", def.PuzzleType);

            var builder = new PuzzleBuilder(root, ground, assetDir);
            foreach (PuzzleDef other in Defs())
            {
                string otherFile = Path.Combine(Application.dataPath, "Puzzles", other.Name, "site.json");
                if (other.Name == name || other.PuzzleType == 2 || !File.Exists(otherFile)) continue;
                Site s = JsonUtility.FromJson<Site>(File.ReadAllText(otherFile));
                builder.OtherSites.Add(new Vector3(s.x, s.z, other.Footprint.magnitude / 2f));
                foreach (Vector3 extra in other.ExtraSites) builder.OtherSites.Add(extra);
            }
            def.Build(builder);
            Debug.Log("[BWM] Built " + root.GetComponentsInChildren<Transform>(true).Length + " objects");

            AssetDatabase.SaveAssets();
            CheckShaders(root);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + scenePath);
            if (!Flag("-bwmNoPreview")) Preview.Render(root, name, PreviewDir, Mathf.Max(def.Footprint.x, def.Footprint.y) / 2f);
            Check(root);
            if (!Flag("-bwmNoExport")) Export(root);
        }

        // A shader of ours that does not compile would export and then draw nothing (or pink) in the game
        static void CheckShaders(GameObject root)
        {
            var seen = new HashSet<Shader>();
            int errors = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || !seen.Add(m.shader)) continue;
                    foreach (ShaderMessage msg in ShaderUtil.GetShaderMessages(m.shader))
                    {
                        if (msg.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) continue;
                        errors++;
                        Debug.LogError("[BWM] Shader " + m.shader.name + ": " + msg.message + " (" + msg.file + ":" + msg.line + ")");
                    }
                }
            if (errors > 0) throw new Exception(errors + " shader error(s)");
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

        // Our own shaders are compiled here and run in the game, whose render settings we do not control. Turn on
        // every lighting feature in our pipeline asset, so URP's variant stripping keeps whichever variant the game
        // asks for. Stripping itself must stay on: without it the stock Lit shader alone is 221,000 variants.
        static void KeepShaderVariants()
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset asset)) return;
            var so = new SerializedObject(asset);
            void Set(string name, Action<SerializedProperty> apply) { SerializedProperty p = so.FindProperty(name); if (p != null) apply(p); }
            Set("m_MainLightShadowsSupported", p => p.boolValue = true);
            Set("m_ShadowCascadeCount", p => p.intValue = 4);
            Set("m_SoftShadowsSupported", p => p.boolValue = true);
            Set("m_AdditionalLightsRenderingMode", p => p.intValue = 1);      // per pixel
            Set("m_AdditionalLightShadowsSupported", p => p.boolValue = true);
            so.ApplyModifiedPropertiesWithoutUndo();
            var stripping = GraphicsSettings.GetRenderPipelineSettings<UnityEngine.Rendering.ShaderStrippingSetting>();
            if (stripping != null) stripping.stripRuntimeDebugShaders = true;
            var urpStripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            if (urpStripping != null)
            {
                urpStripping.stripUnusedVariants = true;
                urpStripping.stripUnusedPostProcessingVariants = true;
            }
        }
    }
}
