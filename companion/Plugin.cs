// BWM Companion: lets the build tools look at and drive the running game.
// A tiny HTTP server on 127.0.0.1 takes one command per request (GET /<command>?key=value...) and answers in
// plain text. Commands run on Unity's main thread; ones that need several frames (screenshots, hosting) are
// iterators that yield null to wait a frame and yield their answer when done.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BigPuzzleSDK;
using Il2CppInterop.Runtime.Injection;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BwmCompanion
{
    [BepInPlugin("bwm.companion", "BWM Companion", "0.1.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal const int Port = 17361;
        internal static string RepoDir;      // ...\BigWalkMods (the profile's parent)

        public override void Load()
        {
            L = Log;
            RepoDir = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, "..", ".."));
            ClassInjector.RegisterTypeInIl2Cpp<Runner>();
            var go = new GameObject("BwmCompanion");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>();
            Application.runInBackground = true;      // keep answering while the window is not focused
            Server.Start(Port);
            L.LogInfo("[BWM] Companion listening on http://127.0.0.1:" + Port);
        }
    }

    internal sealed class Request
    {
        public string Command;
        public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Response;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

        public string Str(string key, string fallback = null) => Query.TryGetValue(key, out string v) ? v : fallback;
        public bool Has(string key) => Query.ContainsKey(key);
        public float Num(string key, float fallback = 0f) => Query.TryGetValue(key, out string v) ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;
    }

    internal static class Server
    {
        internal static readonly Queue<Request> Incoming = new Queue<Request>();

        public static void Start(int port)
        {
            var thread = new Thread(() => Listen(port)) { IsBackground = true, Name = "BwmCompanion" };
            thread.Start();
        }

        static void Listen(int port)
        {
            TcpListener listener;
            try { listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); }
            catch (Exception e) { Plugin.L.LogError("[BWM] Companion could not listen: " + e.Message); return; }
            while (true)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 5000;
                        NetworkStream stream = client.GetStream();
                        var buffer = new byte[8192];
                        var head = new StringBuilder();
                        while (!head.ToString().Contains("\r\n\r\n"))
                        {
                            int n = stream.Read(buffer, 0, buffer.Length);
                            if (n <= 0) break;
                            head.Append(Encoding.ASCII.GetString(buffer, 0, n));
                        }
                        string[] first = head.ToString().Split('\n')[0].Split(' ');
                        string body;
                        if (first.Length < 2) body = "ERROR bad request";
                        else
                        {
                            Request req = Parse(first[1]);
                            lock (Incoming) Incoming.Enqueue(req);
                            body = req.Done.Wait(TimeSpan.FromSeconds(180)) ? req.Response : "ERROR timed out (is the game paused or minimised?)";
                        }
                        byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(payload, 0, payload.Length);
                    }
                }
                catch (Exception e) { Plugin.L.LogWarning("[BWM] Companion request failed: " + e.Message); }
            }
        }

        static Request Parse(string target)
        {
            var req = new Request();
            int q = target.IndexOf('?');
            req.Command = Uri.UnescapeDataString(q < 0 ? target : target.Substring(0, q)).Trim('/').ToLowerInvariant();
            if (q >= 0)
                foreach (string pair in target.Substring(q + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString((eq < 0 ? pair : pair.Substring(0, eq)).Replace('+', ' '));
                    string value = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                    req.Query[key] = value;
                }
            return req;
        }
    }

    public class Runner : MonoBehaviour
    {
        public Runner(IntPtr ptr) : base(ptr) { }

        static readonly List<KeyValuePair<Request, IEnumerator<string>>> running = new List<KeyValuePair<Request, IEnumerator<string>>>();

        internal static float FrameMs = 16f, WorstMs = 16f;      // smoothed frame time, and the worst in the last second or so

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            FrameMs = Mathf.Lerp(FrameMs, ms, 0.05f);
            WorstMs = Mathf.Max(ms, Mathf.Lerp(WorstMs, ms, 0.02f));
            lock (Server.Incoming)
                while (Server.Incoming.Count > 0)
                {
                    Request req = Server.Incoming.Dequeue();
                    running.Add(new KeyValuePair<Request, IEnumerator<string>>(req, Commands.Run(req)));
                }
            for (int i = running.Count - 1; i >= 0; i--)
            {
                Request req = running[i].Key;
                string answer = null;
                bool finished;
                try
                {
                    finished = !running[i].Value.MoveNext();
                    if (!finished && running[i].Value.Current != null) { answer = running[i].Value.Current; finished = true; }
                    else if (finished) answer = "ERROR the command ended without an answer";
                }
                catch (Exception e)
                {
                    finished = true;
                    answer = "ERROR " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
                }
                if (!finished) continue;
                running.RemoveAt(i);
                req.Response = answer;
                req.Done.Set();
            }
        }
    }

    internal static class Commands
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x) + ", " + F(v.y) + ", " + F(v.z);

        public static IEnumerator<string> Run(Request r)
        {
            switch (r.Command)
            {
                case "": case "help":
                    yield return "commands: status | teleport?x&y&z or ?puzzle=Name | reload | shot?file=name[&cx&cy&cz&tx&ty&tz][&fov] | "
                        + "puzzle?name=Name&do=start|stop|win | time?hour=12 | weather[?set=Name] | season?set=Winter | snow?amount=1 | "
                        + "find?name=text[&max=40] | host[?slot=BWM Test][&players=2] | menu | call?type=T&method=M | env | terrain[?filter=Snow][&trees] | global?name=_X&value=1 | terrainset?prop=_X&value=1 | snowtest[?undo] | move?name=N[&nx&ny&nz]&x&y&z | get?path=Type.member... | set?path=...&value=v | quit";
                    break;
                case "status": yield return Status(); break;
                case "teleport": yield return Teleport(r); break;
                case "reload": yield return Reload(); break;
                case "puzzle": yield return Puzzle(r); break;
                case "time": yield return SetTime(r); break;
                case "weather": yield return Weather(r); break;
                case "season": yield return Season(r); break;
                case "snow": yield return Snow(r); break;
                case "find": yield return Find(r); break;
                case "menu": yield return Host.Menus(); break;
                case "call": yield return Call(r); break;
                case "env": yield return Env(); break;
                case "terrain": yield return TerrainInfo(r); break;
                case "global": Shader.SetGlobalFloat(r.Str("name"), r.Num("value")); yield return "set global " + r.Str("name") + " = " + F(r.Num("value")); break;
                case "terrainset": yield return TerrainSet(r); break;
                case "snowtest": yield return SnowTest(r); break;
                case "move": yield return Move(r); break;
                case "obj": yield return Objects.Describe(r); break;
                case "clone": yield return Objects.Clone(r); break;
                case "overlay": yield return Objects.Overlay(r); break;
                case "mats": yield return Objects.Materials(r); break;
                case "tree": yield return Objects.Tree(r); break;
                case "matset": yield return Objects.MatSet(r); break;
                case "destroy": yield return Objects.DestroyCopy(r); break;
                case "catalog": yield return Objects.Catalog(r); break;
                case "get": yield return Inspect(r, false); break;
                case "set": yield return Inspect(r, true); break;
                case "quit": Application.Quit(); yield return "quitting"; break;
                case "shot": { IEnumerator<string> e = Shot(r); while (e.MoveNext()) yield return e.Current; break; }
                case "host": { IEnumerator<string> e = Host.Run(r); while (e.MoveNext()) yield return e.Current; break; }
                default: yield return "ERROR unknown command '" + r.Command + "' (try /help)"; break;
            }
        }

        // ---- state ----

        static PlayerCharacter LocalCharacter()
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            return player == null ? null : player.GetComponent<PlayerCharacter>();
        }

        static PuzzleSDK[] Puzzles() => UnityEngine.Object.FindObjectsOfType<PuzzleSDK>().ToArray();

        static string Status()
        {
            var sb = new StringBuilder();
            sb.AppendLine("scene: " + SceneManager.GetActiveScene().name + " (" + SceneManager.sceneCount + " loaded)");
            sb.AppendLine("network: server " + NetworkServer.active + ", client " + NetworkClient.active + ", players " + NetworkServer.connections.Count);
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null) sb.AppendLine("player: none (not in a walk)");
            else sb.AppendLine("player: " + V(player.transform.position));
            Camera cam = Camera.main;
            if (cam != null) sb.AppendLine("camera: " + V(cam.transform.position) + " facing " + V(cam.transform.forward));
            try
            {
                Enviro.EnviroManager env = Enviro.EnviroManager.instance;
                if (env != null && env.Time != null) sb.AppendLine("time of day: " + F(env.Time.GetTimeOfDay()) + " h");
            }
            catch (Exception e) { sb.AppendLine("time of day: unavailable (" + e.Message + ")"); }
            foreach (PuzzleSDK p in Puzzles())
                sb.AppendLine("puzzle: " + p.transform.root.name + " at " + V(p.transform.position) + " running " + p.IsRunning + " won " + p.IsWon);
            sb.AppendLine("window: " + Screen.width + " x " + Screen.height + ", focused " + Application.isFocused);
            sb.AppendLine("frame: " + F(Runner.FrameMs) + " ms average (" + F(1000f / Mathf.Max(Runner.FrameMs, 0.1f)) + " fps), worst lately " + F(Runner.WorstMs) + " ms");
            return sb.ToString();
        }

        static string Teleport(Request r)
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null) return "ERROR not in a walk";
            Vector3 target;
            if (r.Has("puzzle"))
            {
                PuzzleSDK p = Puzzles().FirstOrDefault(x => string.Equals(x.transform.root.name, r.Str("puzzle"), StringComparison.OrdinalIgnoreCase));
                if (p == null) return "ERROR no loaded puzzle named '" + r.Str("puzzle") + "'";
                Transform mark = p.transform.Find("Debug Teleport");
                target = mark != null ? mark.position : p.transform.position + Vector3.up * 2f;
            }
            else target = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            PlayerCharacter character = player.GetComponent<PlayerCharacter>();
            Rigidbody body = character != null ? character.rb : player.GetComponent<Rigidbody>();
            player.transform.position = target;
            if (body != null)
            {
                body.position = target;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            return "teleported to " + V(target);
        }

        // What the SDK does on Ctrl+F2 (host only): rescan the packs and rebuild the puzzles
        static string Reload()
        {
            if (NetworkClient.active && !NetworkServer.active) return "ERROR only the host can reload puzzles";
            Assembly sdk = typeof(PuzzleSDK).Assembly;
            GameObject holder = GameObject.Find("BigPuzzleSDK");
            if (holder == null) return "ERROR the SDK's object was not found";
            Type editorType = sdk.GetType("BigPuzzleSDK.BigPuzzleEditor");
            object managed = typeof(GameObject).GetMethods().First(x => x.Name == "GetComponent" && x.IsGenericMethod && x.GetParameters().Length == 0).MakeGenericMethod(editorType).Invoke(holder, null);
            if (managed == null) return "ERROR the SDK's editor component was not found";
            Invoke(sdk, "BigPuzzleSDK.PuzzleSave", "SnapshotForReload");
            Invoke(sdk, "BigPuzzleSDK.PuzzleLibrary", "Scan");
            editorType.GetMethod("LoadPuzzle", Any).Invoke(managed, null);
            Invoke(sdk, "BigPuzzleSDK.ReloadChannel", "Announce");
            return "reloaded: " + string.Join(", ", Puzzles().Select(p => p.transform.root.name));
        }

        static void Invoke(Assembly assembly, string type, string method)
        {
            Type t = assembly.GetType(type) ?? throw new Exception("SDK type not found: " + type);
            MethodInfo m = t.GetMethods(Any).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == 0) ?? throw new Exception("SDK method not found: " + type + "." + method);
            m.Invoke(null, null);
        }

        static string Puzzle(Request r)
        {
            PuzzleSDK p = Puzzles().FirstOrDefault(x => string.Equals(x.transform.root.name, r.Str("name"), StringComparison.OrdinalIgnoreCase));
            if (p == null) return "ERROR no loaded puzzle named '" + r.Str("name") + "'";
            switch (r.Str("do", ""))
            {
                case "start": p.StartPuzzle(); break;
                case "stop": p.StopPuzzle(); break;
                case "win": p.Win(); break;
                case "": break;
                default: return "ERROR do=start|stop|win";
            }
            return p.transform.root.name + ": running " + p.IsRunning + " won " + p.IsWon;
        }

        // ---- environment ----

        static string SetTime(Request r)
        {
            Enviro.EnviroManager env = Enviro.EnviroManager.instance;
            if (env == null || env.Time == null) return "ERROR no Enviro time module (not in a walk?)";
            if (r.Has("hour")) env.Time.SetTimeOfDay(r.Num("hour"));
            return "time of day: " + F(env.Time.GetTimeOfDay()) + " h";
        }

        static string Weather(Request r)
        {
            Enviro.EnviroManager env = Enviro.EnviroManager.instance;
            if (env == null || env.Weather == null) return "ERROR no Enviro weather module (not in a walk?)";
            if (r.Has("set")) env.Weather.ChangeWeatherInstant(r.Str("set"));
            var sb = new StringBuilder();
            Enviro.EnviroWeatherType target = env.Weather.targetWeatherType;
            sb.AppendLine("current: " + (target != null ? target.name : "none") + ", auto change " + env.Weather.globalAutoWeatherChange);
            foreach (Enviro.EnviroWeatherType w in env.Weather.Settings.weatherTypes) sb.AppendLine("type: " + (w != null ? w.name : "null"));
            return sb.ToString();
        }

        static string Season(Request r)
        {
            Enviro.EnviroManager env = Enviro.EnviroManager.instance;
            if (env == null || env.Environment == null) return "ERROR no Enviro environment module (not in a walk?)";
            if (r.Has("set")) env.Environment.ChangeSeason((Enviro.EnviroEnvironment.Seasons)Enum.Parse(typeof(Enviro.EnviroEnvironment.Seasons), r.Str("set"), true));
            Enviro.EnviroEnvironment s = env.Environment.Settings;
            return "season " + s.season + ", change season " + s.changeSeason + ", temperature " + F(s.temperature) + ", snow " + F(s.snow) + " (target " + F(s.snowTarget) + "), wetness " + F(s.wetness);
        }

        static string Snow(Request r)
        {
            Enviro.EnviroManager env = Enviro.EnviroManager.instance;
            if (env == null || env.Environment == null) return "ERROR no Enviro environment module (not in a walk?)";
            Enviro.EnviroEnvironment s = env.Environment.Settings;
            if (r.Has("amount")) { s.snow = r.Num("amount"); s.snowTarget = r.Num("amount"); }
            return "snow " + F(s.snow) + " (target " + F(s.snowTarget) + ")";
        }

        // For experiments: calls a method with no parameters on the first active object of a game type,
        // e.g. /call?type=PlayerCountMenu&method=ActionProceed
        static string Call(Request r)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(r.Str("type", ""))).FirstOrDefault(t => t != null);
            if (type == null) return "ERROR no type '" + r.Str("type") + "'";
            // The generic FindObjectOfType<T>() survives in this build; the (Type) overload was stripped
            object typed = typeof(UnityEngine.Object).GetMethods().First(x => x.Name == "FindObjectOfType" && x.IsGenericMethod && x.GetParameters().Length == 0).MakeGenericMethod(type).Invoke(null, null);
            if (typed == null) return "ERROR no active " + type.Name;
            MethodInfo m = type.GetMethods(Any).FirstOrDefault(x => x.Name == r.Str("method") && x.GetParameters().Length == 0);
            if (m == null) return "ERROR " + type.Name + " has no " + r.Str("method") + "()";
            object result = m.Invoke(typed, null);
            return "called " + type.Name + "." + m.Name + "()" + (result != null ? " -> " + result : "");
        }

        // Which of Enviro's modules this game uses (sky, fog, weather, ...)
        static string Env()
        {
            Enviro.EnviroManager e = Enviro.EnviroManager.instance;
            if (e == null) return "ERROR no Enviro manager (not in a walk?)";
            var sb = new StringBuilder();
            sb.AppendLine("Time " + (e.Time != null) + ", Lighting " + (e.Lighting != null) + ", Reflections " + (e.Reflections != null) + ", Sky " + (e.Sky != null)
                + ", Fog " + (e.Fog != null) + ", VolumetricClouds " + (e.VolumetricClouds != null) + ", FlatClouds " + (e.FlatClouds != null) + ", Weather " + (e.Weather != null)
                + ", Aurora " + (e.Aurora != null) + ", Audio " + (e.Audio != null) + ", Effects " + (e.Effects != null) + ", Lightning " + (e.Lightning != null)
                + ", Quality " + (e.Quality != null) + ", Environment " + (e.Environment != null));
            sb.AppendLine("fog: " + RenderSettings.fog + " density " + RenderSettings.fogDensity.ToString("0.#####", CultureInfo.InvariantCulture) + " colour " + RenderSettings.fogColor);
            sb.AppendLine("ambient: " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + ", skybox " + (RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "none"));
            Light sun = RenderSettings.sun;
            if (sun != null) sb.AppendLine("sun: " + sun.name + " intensity " + F(sun.intensity) + " colour " + sun.color);
            return sb.ToString();
        }

        // The game's terrains: material, shader keywords, the shader properties matching ?filter= (default Snow), layers, trees
        static string TerrainInfo(Request r)
        {
            string filter = r.Str("filter", "Snow");
            var sb = new StringBuilder();
            foreach (Terrain t in Terrain.activeTerrains)
            {
                TerrainData d = t.terrainData;
                sb.AppendLine("terrain: " + t.name + " at " + V(t.transform.position) + " size " + V(d.size) + ", trees " + d.treeInstanceCount + " of " + d.treePrototypes.Length + " kinds, detail kinds " + d.detailPrototypes.Length);
                Material m = t.materialTemplate;
                if (m == null) { sb.AppendLine("  material: none"); continue; }
                sb.AppendLine("  material: " + m.name + ", shader " + m.shader.name);
                var keywords = new List<string>();
                foreach (string k in m.shaderKeywords) keywords.Add(k);
                sb.AppendLine("  keywords (" + keywords.Count + "): " + string.Join(" ", keywords));
                var props = new List<string>();
                int count = m.shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string name = m.shader.GetPropertyName(i);
                    if (filter == "*" || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) props.Add(name);
                }
                sb.AppendLine("  properties matching '" + filter + "' (" + props.Count + " of " + count + "): " + string.Join(" ", props));
                var layers = new List<string>();
                foreach (TerrainLayer l in d.terrainLayers) layers.Add(l != null ? l.name : "null");
                sb.AppendLine("  layers (" + layers.Count + "): " + string.Join(", ", layers));
                if (r.Has("trees"))
                {
                    var kinds = new List<string>();
                    foreach (TreePrototype tp in d.treePrototypes) kinds.Add(tp != null && tp.prefab != null ? tp.prefab.name : "null");
                    sb.AppendLine("  tree kinds: " + string.Join(", ", kinds));
                }
            }
            if (sb.Length == 0) sb.AppendLine("no active terrains");
            return sb.ToString();
        }

        // Sets a float (value=) or colour (color=r,g,b) on every terrain's material, to try looks out live
        static string TerrainSet(Request r)
        {
            int n = 0;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Material m = t.materialTemplate;
                if (m == null) continue;
                if (r.Has("color"))
                {
                    string[] c = r.Str("color").Split(',');
                    m.SetColor(r.Str("prop"), new Color(float.Parse(c[0], CultureInfo.InvariantCulture), float.Parse(c[1], CultureInfo.InvariantCulture), float.Parse(c[2], CultureInfo.InvariantCulture)));
                }
                else m.SetFloat(r.Str("prop"), r.Num("value"));
                if (r.Has("keyword")) m.EnableKeyword(r.Str("keyword"));
                n++;
            }
            return "set " + r.Str("prop") + " on " + n + " terrain material(s)";
        }

        // Experiment for a winter look: swaps every terrain's ground-texture array for flat snow (rock layers stay grey).
        // /snowtest?undo puts the game's own textures back.
        static readonly Dictionary<int, Texture> originalDiffuse = new Dictionary<int, Texture>();
        static string SnowTest(Request r)
        {
            var sb = new StringBuilder();
            Texture2DArray snow = null;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Material m = t.materialTemplate;
                if (m == null) continue;
                int id = m.GetInstanceID();
                Texture current = m.GetTexture("_Diffuse");
                if (r.Has("undo"))
                {
                    if (originalDiffuse.TryGetValue(id, out Texture old)) { m.SetTexture("_Diffuse", old); sb.AppendLine(t.name + ": restored"); }
                    continue;
                }
                if (!originalDiffuse.ContainsKey(id)) originalDiffuse[id] = current;
                sb.AppendLine(t.name + ": _Diffuse is " + (current != null ? current.GetIl2CppType().Name + " " + current.width + "x" + current.height + " (" + current.dimension + ")" : "null") + ", material id " + id);
                if (snow == null)
                {
                    const int size = 4, layers = 12;
                    snow = new Texture2DArray(size, size, layers, TextureFormat.RGBA32, false);
                    for (int layer = 0; layer < layers; layer++)
                    {
                        bool rock = layer == 3 || layer == 4 || layer == 10;      // GraniteCracked, GraniteSand, Gravel
                        Color c = rock ? new Color(0.62f, 0.64f, 0.68f, 0.5f) : new Color(0.93f, 0.95f, 1f, 0.5f);
                        var pixels = new Color[size * size];
                        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
                        snow.SetPixels(pixels, layer);
                    }
                    snow.Apply();
                }
                m.SetTexture("_Diffuse", snow);
            }
            return sb.Length == 0 ? "no terrains" : sb.ToString();
        }

        // Puts an object at a world position, for testing zones and snap points without a player's hands.
        // Host only. A custom item's real prop is a root object called "VanillaItem" (the placeholder with the
        // item's own name is only a marker), so pick one by where it is: nx, ny, nz choose the nearest of that name.
        static string Move(Request r)
        {
            string name = r.Str("name", "");
            bool nearest = r.Has("nx");
            var near = new Vector3(r.Num("nx"), r.Num("ny"), r.Num("nz"));
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != name) continue;
                float d = nearest ? (t.position - near).sqrMagnitude : 0f;
                if (d < bestDistance) { bestDistance = d; best = t; }
                if (!nearest) break;
            }
            if (best == null) return "ERROR no object named '" + name + "'";
            var target = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            string from = V(best.position);
            Rigidbody body = best.GetComponent<Rigidbody>();
            if (body == null) body = best.GetComponentInChildren<Rigidbody>();
            best.position = target;
            if (body != null) { body.position = target; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            return "moved " + PathOf(best) + " from " + from + " to " + V(target) + (body != null ? " (rigidbody)" : " (no rigidbody)");
        }

        // For exploring the game's own objects: /get?path=Type.Full.Name.member.member lists what is there,
        // /set?path=...&value=v changes a number, bool, enum or colour (r,g,b,a). The path starts at a type and
        // walks static then instance members, e.g. Enviro.EnviroManager.instance.Fog.Settings
        static string Inspect(Request r, bool write)
        {
            string[] parts = r.Str("path", "").Split('.');
            Type type = null;
            int used = 0;
            for (int n = parts.Length; n > 0 && type == null; n--)
            {
                string name = string.Join(".", parts, 0, n);
                type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
                used = n;
            }
            if (type == null) return "ERROR no type at the start of '" + r.Str("path") + "'";
            object current = null;
            Type currentType = type;
            for (int i = used; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                PropertyInfo prop = currentType.GetProperty(parts[i], Any | BindingFlags.FlattenHierarchy);
                FieldInfo field = prop == null ? currentType.GetField(parts[i], Any | BindingFlags.FlattenHierarchy) : null;
                if (prop == null && field == null) return "ERROR " + currentType.Name + " has no member '" + parts[i] + "'";
                if (write && last)
                {
                    Type target = prop != null ? prop.PropertyType : field.FieldType;
                    string text = r.Str("value", "");
                    object value;
                    if (target == typeof(float)) value = float.Parse(text, CultureInfo.InvariantCulture);
                    else if (target == typeof(int)) value = int.Parse(text, CultureInfo.InvariantCulture);
                    else if (target == typeof(bool)) value = text == "1" || text == "true";
                    else if (target.IsEnum) value = Enum.Parse(target, text, true);
                    else if (target == typeof(Color))
                    {
                        string[] c = text.Split(',');
                        value = new Color(float.Parse(c[0], CultureInfo.InvariantCulture), float.Parse(c[1], CultureInfo.InvariantCulture), float.Parse(c[2], CultureInfo.InvariantCulture), c.Length > 3 ? float.Parse(c[3], CultureInfo.InvariantCulture) : 1f);
                    }
                    else return "ERROR cannot set a " + target.Name;
                    if (prop != null) prop.SetValue(current, value); else field.SetValue(current, value);
                    return "set " + parts[i] + " = " + value;
                }
                current = prop != null ? prop.GetValue(current) : field.GetValue(current);
                if (current == null) return string.Join(".", parts, 0, i + 1) + " is null";
                currentType = current.GetType();
            }
            if (current == null) return "type " + type.FullName + ": static members " + string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.Static).Select(x => x.Name));
            if (currentType.IsPrimitive || currentType.IsEnum || currentType == typeof(string) || currentType == typeof(Color) || currentType == typeof(Vector3) || currentType == typeof(Vector2))
                return currentType.Name + " = " + current;
            var sb = new StringBuilder();
            sb.AppendLine(currentType.FullName);
            string[] skip = { "Pointer", "ObjectClass", "WasCollected", "hideFlags", "name" };
            foreach (PropertyInfo prop in currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0 || skip.Contains(prop.Name) || prop.Name.StartsWith("NativeFieldInfoPtr") || prop.Name.StartsWith("NativeMethodInfoPtr")) continue;
                string shown;
                try
                {
                    object v = prop.GetValue(current);
                    Type pt = prop.PropertyType;
                    shown = v == null ? "null" : (pt.IsPrimitive || pt.IsEnum || pt == typeof(string) || pt == typeof(Color) || pt == typeof(Vector3) || pt == typeof(Vector2)) ? v.ToString() : "<" + pt.Name + ">";
                }
                catch (Exception e) { shown = "(" + e.GetType().Name + ")"; }
                sb.AppendLine("  " + prop.Name + " = " + shown);
            }
            return sb.ToString();
        }

        // ---- looking ----

        static string Find(Request r)
        {
            string text = r.Str("name", "");
            int max = (int)r.Num("max", 40);
            var sb = new StringBuilder();
            int n = 0;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n > max) continue;
                sb.AppendLine(PathOf(t) + " | " + V(t.position) + (t.gameObject.activeInHierarchy ? "" : " | inactive"));
            }
            sb.AppendLine(n + " match(es)" + (n > max ? ", first " + max + " shown" : ""));
            return sb.ToString();
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null && parts.Count < 12; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Saves a screenshot to <repo>\shots\<file>.png. With cx,cy,cz (camera) and tx,ty,tz (target) it is taken
        // from a temporary camera at that viewpoint; otherwise it is the player's own view.
        static IEnumerator<string> Shot(Request r)
        {
            string dir = Path.Combine(Plugin.RepoDir, "shots");
            Directory.CreateDirectory(dir);
            string name = r.Str("file", "shot");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string file = Path.Combine(dir, name + ".png");
            if (File.Exists(file)) File.Delete(file);

            GameObject camGo = null;
            if (r.Has("cx"))
            {
                Camera main = Camera.main;
                if (main == null) { yield return "ERROR no main camera to copy"; yield break; }
                camGo = new GameObject("BwmShotCamera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.targetTexture = null;
                cam.depth = main.depth + 50f;
                if (r.Has("fov")) cam.fieldOfView = r.Num("fov");
                camGo.transform.position = new Vector3(r.Num("cx"), r.Num("cy"), r.Num("cz"));
                camGo.transform.LookAt(new Vector3(r.Num("tx"), r.Num("ty"), r.Num("tz")));
            }
            for (int i = 0; i < 4; i++) yield return null;      // let the view settle
            ScreenCapture.CaptureScreenshot(file);
            for (int i = 0; i < 300 && !File.Exists(file); i++) yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            yield return File.Exists(file) ? "saved " + file : "ERROR the screenshot was not written (is the window minimised?)";
        }
    }

    // Looking inside the game's own objects, and copying them (the groundwork for Winter Walk's set dressing).
    internal static class Objects
    {
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x) + ", " + F(v.y) + ", " + F(v.z);

        // Parts of the game's objects that would write to the save, move the copy onto the terrain, clear the
        // ground round it or hide it by distance (the SDK's PuzzleVanillaCopy removes the same ones)
        internal static readonly HashSet<string> Removed = new HashSet<string>
        {
            "SaveIdentity", "BuoySaver", "PeckEffectSavableHome", "PeckEffectGourdMap", "LandmarkSaveSetter",
            "CullingLocation", "TerrainAligner", "ClearStamp",
        };

        static int clones;

        // path=full/path, or name=exact name [&index=n] [&under=text in the path]
        internal static Transform Locate(Request r)
        {
            string name = r.Str("name", ""), under = r.Str("under", null), path = r.Str("path", null);
            int index = (int)r.Num("index", 0), n = 0;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (path != null) { if (Full(t) == path) return t; continue; }
                if (t.name != name) continue;
                if (under != null && Full(t).IndexOf(under, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n++ == index) return t;
            }
            return null;
        }

        static string Full(Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static void Material(StringBuilder sb, string indent, Material m)
        {
            if (m == null) { sb.AppendLine(indent + "material: null"); return; }
            sb.AppendLine(indent + "material: " + m.name + " | shader " + (m.shader != null ? m.shader.name : "null") + " | queue " + m.renderQueue);
            try
            {
                Shader s = m.shader;
                int count = s.GetPropertyCount();
                var props = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string p = s.GetPropertyName(i);
                    var type = s.GetPropertyType(i);
                    string value;
                    if (type == UnityEngine.Rendering.ShaderPropertyType.Texture) { Texture tex = m.GetTexture(p); value = tex != null ? tex.name + " " + tex.width + "x" + tex.height : "-"; }
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Color) value = m.GetColor(p).ToString();
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector) value = m.GetVector(p).ToString();
                    else value = F(m.GetFloat(p));
                    props.Add(p + "=" + value);
                }
                sb.AppendLine(indent + "  " + string.Join("; ", props));
            }
            catch (Exception e) { sb.AppendLine(indent + "  (properties unavailable: " + e.GetType().Name + ": " + e.Message + ")"); }
            try { sb.AppendLine(indent + "  keywords: " + string.Join(" ", m.shaderKeywords)); } catch (Exception) { }
        }

        static void Describe(StringBuilder sb, Transform t, int depth, string indent, bool materials)
        {
            sb.AppendLine(indent + t.name + (t.gameObject.activeSelf ? "" : " (inactive)") + " | at " + V(t.position) + " | scale " + V(t.lossyScale) + " | layer " + t.gameObject.layer);
            var names = new List<string>();
            foreach (Component c in t.gameObject.GetComponents(Il2CppInterop.Runtime.Il2CppType.Of<Component>()))
                if (c != null) names.Add(c.GetIl2CppType().Name);
            sb.AppendLine(indent + "  components: " + string.Join(", ", names));
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                sb.AppendLine(indent + "  mesh: " + mf.sharedMesh.name + " | " + mf.sharedMesh.vertexCount + " vertices, " + mf.sharedMesh.subMeshCount + " submeshes, readable " + mf.sharedMesh.isReadable + " | bounds " + V(mf.sharedMesh.bounds.size));
            Renderer rend = t.GetComponent<Renderer>();
            if (rend != null)
            {
                sb.AppendLine(indent + "  renderer: enabled " + rend.enabled + ", shadows " + rend.shadowCastingMode + ", bounds " + V(rend.bounds.size));
                if (materials) foreach (Material m in rend.sharedMaterials) Material(sb, indent + "  ", m);
            }
            foreach (Component c in t.gameObject.GetComponents(Il2CppInterop.Runtime.Il2CppType.Of<Collider>()))
            {
                Collider col = c != null ? c.TryCast<Collider>() : null;
                if (col == null) continue;
                MeshCollider mc = col.TryCast<MeshCollider>();
                sb.AppendLine(indent + "  collider: " + col.GetIl2CppType().Name + ", enabled " + col.enabled + ", trigger " + col.isTrigger + (mc != null && mc.sharedMesh != null ? ", mesh " + mc.sharedMesh.name + " (" + mc.sharedMesh.vertexCount + ")" : "") + ", bounds " + V(col.bounds.size));
            }
            LODGroup lods = t.GetComponent<LODGroup>();
            if (lods != null) sb.AppendLine(indent + "  lod group: " + lods.lodCount + " levels, size " + F(lods.size));
            if (depth <= 0) { if (t.childCount > 0) sb.AppendLine(indent + "  (" + t.childCount + " children)"); return; }
            for (int i = 0; i < t.childCount; i++) Describe(sb, t.GetChild(i), depth - 1, indent + "    ", materials);
        }

        // obj?name=X[&index=n][&under=text][&depth=2][&materials=0]
        public static string Describe(Request r)
        {
            Transform t = Locate(r);
            if (t == null) return "ERROR nothing found";
            var sb = new StringBuilder(Full(t) + "\n");
            Describe(sb, t, (int)r.Num("depth", 2), "", r.Num("materials", 1) > 0);
            return sb.ToString();
        }

        // Copies a game object into place with its save, terrain and culling parts taken out first.
        // clone?name=X[&index][&under]&x&y&z[&yaw][&scale]
        public static string Clone(Request r)
        {
            Transform t = Locate(r);
            if (t == null) return "ERROR nothing found";
            var holder = new GameObject("BWM Clone Holder");
            holder.SetActive(false);      // nothing in the copy wakes until it is cleaned
            GameObject copy = UnityEngine.Object.Instantiate((UnityEngine.Object)t.gameObject, holder.transform).Cast<GameObject>();
            int removed = 0;
            var doomed = new List<Component>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Component>(), true))
                if (c != null && Removed.Contains(c.GetIl2CppType().Name)) doomed.Add(c);
            foreach (Component c in doomed) { UnityEngine.Object.DestroyImmediate(c); removed++; }
            int renderers = 0, colliders = 0;
            foreach (Component c in copy.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>(), true)) { Renderer rr = c.TryCast<Renderer>(); if (rr != null) { rr.enabled = true; renderers++; } }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Collider>(), true)) colliders++;
            copy.name = "BWM Clone " + (++clones);
            copy.transform.SetParent(null, true);
            copy.transform.position = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            copy.transform.rotation = Quaternion.Euler(0f, r.Num("yaw", 0f), 0f) * t.rotation;
            copy.transform.localScale = t.lossyScale * r.Num("scale", 1f);
            UnityEngine.Object.Destroy(holder);
            copy.SetActive(true);
            return "made " + copy.name + " from " + Full(t) + ": removed " + removed + " components, " + renderers + " renderers, " + colliders + " colliders";
        }

        // Copies one of the terrain's tree kinds (they are prefabs the terrain draws, not scene objects).
        // tree?kind=Name&x&y&z[&yaw][&scale][&describe=1]
        public static string Tree(Request r)
        {
            string kind = r.Str("kind", "");
            GameObject prefab = null;
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                foreach (TreePrototype proto in terrain.terrainData.treePrototypes)
                    if (proto.prefab != null && proto.prefab.name == kind) { prefab = proto.prefab; break; }
                if (prefab != null) break;
            }
            if (prefab == null) return "ERROR no tree kind '" + kind + "'";
            GameObject copy = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab).Cast<GameObject>();
            copy.name = "BWM Tree " + (++clones);
            copy.transform.position = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            copy.transform.rotation = Quaternion.Euler(0f, r.Num("yaw", 0f), 0f);
            copy.transform.localScale = Vector3.one * r.Num("scale", 1f);
            var sb = new StringBuilder("made " + copy.name + "\n");
            if (r.Num("describe", 0) > 0) Describe(sb, copy.transform, 3, "", true);
            return sb.ToString();
        }

        // Sets a material value on every renderer under a target (making the target's own material copies).
        // matset?target=X&prop=_Name&value=1 | &color=r,g,b,a | &keyword=KW&on=1 [&slot=n (default all but our snow)]
        public static string MatSet(Request r)
        {
            Transform t = Locate(new Request { Query = { ["name"] = r.Str("target", "") } });
            if (t == null) return "ERROR no target";
            int done = 0;
            foreach (Component c in t.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>(), true))
            {
                Renderer rend = c != null ? c.TryCast<Renderer>() : null;
                if (rend == null) continue;
                var mats = rend.materials;      // instances
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || m.name.StartsWith("SnowOverlay")) continue;
                    if (r.Has("slot") && (int)r.Num("slot") != i) continue;
                    string prop = r.Str("prop", "");
                    if (r.Has("value")) m.SetFloat(prop, r.Num("value"));
                    if (r.Has("color"))
                    {
                        string[] p = r.Str("color").Split(',');
                        m.SetColor(prop, new Color(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), p.Length > 3 ? float.Parse(p[3], CultureInfo.InvariantCulture) : 1f));
                    }
                    if (r.Has("keyword")) { if (r.Num("on", 1) > 0) m.EnableKeyword(r.Str("keyword")); else m.DisableKeyword(r.Str("keyword")); }
                    done++;
                }
            }
            return "set on " + done + " material(s)";
        }

        // destroy?target=X (one of our copies)
        public static string DestroyCopy(Request r)
        {
            string name = r.Str("target", "");
            if (!name.StartsWith("BWM ")) return "ERROR only our own copies (BWM ...) can be destroyed";
            Transform t = Locate(new Request { Query = { ["name"] = name } });
            if (t == null) return "ERROR no target";
            UnityEngine.Object.Destroy(t.gameObject);
            return "destroyed " + name;
        }

        static string Base(string name)
        {
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }

        // Bounds of a renderer's mesh in another transform's local space (that transform's own scale removed)
        static Bounds LocalBounds(Renderer rend, Transform space)
        {
            MeshFilter mf = rend.GetComponent<MeshFilter>();
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 toSpace = space.worldToLocalMatrix * rend.transform.localToWorldMatrix;
            var result = new Bounds(toSpace.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(toSpace.MultiplyPoint3x4(corner));
            }
            return result;
        }

        // Writes <repo>\world\catalog.tsv: every rock kind (from the world's rock objects) and every tree kind
        // (from the terrains), with its LOD0 bounds at scale 1, the scale range the game uses it at, and how
        // many there are. The island build places copies from this.
        public static string Catalog(Request r)
        {
            var sb = new StringBuilder("kind\tsource\tcx\tcy\tcz\tsx\tsy\tsz\tminScale\tmaxScale\tcount\ttemplate\n");
            var rocks = new Dictionary<string, (Transform t, float lo, float hi, int n)>();
            foreach (LODGroup g in UnityEngine.Object.FindObjectsOfType<LODGroup>(true))
            {
                Transform t = g.transform;
                string kind = Base(t.name);
                if (!kind.StartsWith("rock-") || t.root.name == "Winter Dressing" || Full(t).Contains("_OLD")) continue;
                float s = t.lossyScale.x;
                if (rocks.TryGetValue(kind, out var e)) rocks[kind] = (e.t, Mathf.Min(e.lo, s), Mathf.Max(e.hi, s), e.n + 1);
                else rocks[kind] = (t, s, s, 1);
            }
            foreach (var kv in rocks)
            {
                Renderer lod0 = null;
                foreach (Component c in kv.Value.t.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<MeshRenderer>(), true))
                {
                    Renderer rr = c.TryCast<Renderer>();
                    if (rr != null && (lod0 == null || rr.name.EndsWith("_LOD0"))) lod0 = rr;
                    if (lod0 != null && lod0.name.EndsWith("_LOD0")) break;
                }
                if (lod0 == null) continue;
                Bounds b = LocalBounds(lod0, kv.Value.t);
                sb.AppendLine(kv.Key + "\trock\t" + F(b.center.x) + "\t" + F(b.center.y) + "\t" + F(b.center.z) + "\t" + F(b.size.x) + "\t" + F(b.size.y) + "\t" + F(b.size.z)
                    + "\t" + F(kv.Value.lo) + "\t" + F(kv.Value.hi) + "\t" + kv.Value.n + "\t" + Full(kv.Value.t));
            }
            var seen = new HashSet<string>();
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                TerrainData data = terrain.terrainData;
                var protos = data.treePrototypes;
                var counts = new int[protos.Length];
                var lo = new float[protos.Length]; var hi = new float[protos.Length];
                for (int i = 0; i < protos.Length; i++) { lo[i] = 99f; hi[i] = 0f; }
                foreach (TreeInstance inst in data.treeInstances)
                {
                    int p = inst.prototypeIndex;
                    if (p < 0 || p >= protos.Length) continue;
                    counts[p]++; lo[p] = Mathf.Min(lo[p], inst.heightScale); hi[p] = Mathf.Max(hi[p], inst.heightScale);
                }
                for (int i = 0; i < protos.Length; i++)
                {
                    GameObject prefab = protos[i].prefab;
                    if (prefab == null || !seen.Add(prefab.name)) continue;
                    Renderer lod0 = null;
                    foreach (Component c in prefab.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<MeshRenderer>(), true))
                    {
                        Renderer rr = c.TryCast<Renderer>();
                        if (rr != null && rr.name.EndsWith("_LOD0")) { lod0 = rr; break; }
                        if (rr != null && lod0 == null) lod0 = rr;
                    }
                    if (lod0 == null) continue;
                    Bounds b = LocalBounds(lod0, prefab.transform);
                    sb.AppendLine(prefab.name + "\ttree\t" + F(b.center.x) + "\t" + F(b.center.y) + "\t" + F(b.center.z) + "\t" + F(b.size.x) + "\t" + F(b.size.y) + "\t" + F(b.size.z)
                        + "\t" + F(counts[i] > 0 ? lo[i] : 1f) + "\t" + F(counts[i] > 0 ? hi[i] : 1f) + "\t" + counts[i] + "\t" + prefab.name);
                }
            }
            string file = Path.Combine(Plugin.RepoDir, "world", "catalog.tsv");
            File.WriteAllText(file, sb.ToString());
            return "wrote " + file + " (" + rocks.Count + " rock kinds, " + seen.Count + " tree kinds)";
        }

        static Material FindMaterial(string name)
        {
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m != null && m.name == name) return m;
            }
            return null;
        }

        // mats?name=text: loaded materials whose name contains the text
        public static string Materials(Request r)
        {
            string text = r.Str("name", "");
            var sb = new StringBuilder();
            int n = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m == null || m.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n <= 60) sb.AppendLine(m.name + " | " + (m.shader != null ? m.shader.name : "null"));
            }
            sb.AppendLine(n + " material(s)");
            return sb.ToString();
        }

        // Adds snow to a copy: appends a copy of the named material to every renderer.
        // overlay?target=BWM Clone 1[&mat=SnowOverlay][&line=0.45][&cutout=0.3 (foliage: the leaf texture's alpha)][&tex=_BaseMap]
        public static string Overlay(Request r)
        {
            Transform t = Locate(new Request { Query = { ["name"] = r.Str("target", "") } });
            if (t == null) return "ERROR no target";
            Material source = FindMaterial(r.Str("mat", "SnowOverlay"));
            if (source == null) return "ERROR material not loaded";
            int done = 0;
            foreach (Component c in t.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>(), true))
            {
                Renderer rend = c != null ? c.TryCast<Renderer>() : null;
                if (rend == null) continue;
                var old = rend.sharedMaterials;
                var mats = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(old.Length + 1);
                for (int i = 0; i < old.Length; i++) mats[i] = old[i];
                var snow = new Material(source);
                if (r.Has("line")) snow.SetFloat("_SnowLine", r.Num("line"));
                if (r.Has("cutout") && old.Length > 0 && old[0] != null)
                {
                    string texName = r.Str("tex", "_BaseMap");
                    if (old[0].HasProperty(texName)) snow.SetTexture("_AlphaTex", old[0].GetTexture(texName));
                    snow.SetFloat("_Cutoff", r.Num("cutout"));
                }
                mats[old.Length] = snow;
                rend.sharedMaterials = mats;
                done++;
            }
            return "snow on " + done + " renderer(s) of " + t.name;
        }
    }

    // Hosting a walk from the main menu without anyone at the keyboard. Only ever uses a save slot with the
    // given name (default "BWM Test"), creating it the first time; the user's own slots are never touched.
    internal static class Host
    {
        static bool Active(Component c) => c != null && c.gameObject.activeInHierarchy;

        // MainMenuManager.instance throws while the menu scene is still loading, so look the object up instead
        static MainMenuManager Manager() => UnityEngine.Object.FindObjectOfType<MainMenuManager>();

        public static string Menus()
        {
            MainMenuManager m = Manager();
            if (m == null) return "main menu: not present" + (NetworkClient.localPlayer != null ? " (in a walk)" : "");
            var sb = new StringBuilder("main menu active:");
            if (Active(m.splashMenu)) sb.Append(" splash");
            if (Active(m.micCheckMenu)) sb.Append(" micCheck");
            if (Active(m.titleMenu)) sb.Append(" title");
            if (Active(m.hostMenuSelect)) sb.Append(" hostSelect");
            if (Active(m.hostMenuConfirm)) sb.Append(" hostConfirm");
            if (Active(m.playerCountMenu)) sb.Append(" playerCount");
            if (Active(m.SessionOptionsMenu)) sb.Append(" sessionOptions");
            if (Active(m.loadingMenu)) sb.Append(" loading");
            if (Active(m.loadingBlockMenu)) sb.Append(" loadingBlock");
            if (Active(m.errorMenu)) sb.Append(" error");
            if (Active(m.passwordMenu)) sb.Append(" password");
            if (Active(m.joinMenu)) sb.Append(" join");
            return sb.ToString();
        }

        static IEnumerable<string> Wait(Func<bool> until, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!until() && Time.realtimeSinceStartup < end) yield return null;
        }

        public static IEnumerator<string> Run(Request r)
        {
            string slot = r.Str("slot", "BWM Test");
            if (NetworkClient.localPlayer != null) { yield return "already in a walk"; yield break; }
            MainMenuManager m = Manager();
            if (m == null) { yield return "ERROR no main menu"; yield break; }
            var log = new StringBuilder();

            bool atCount = Active(m.playerCountMenu);
            if (!atCount) foreach (string s in Wait(() => Active(m.titleMenu) || Active(m.hostMenuSelect), 30f)) yield return s;
            if (Active(m.titleMenu)) { m.titleMenu.GoToHostMenu(); log.AppendLine("title -> host menu"); }
            if (!atCount) foreach (string s in Wait(() => Active(m.hostMenuSelect), 15f)) yield return s;
            if (!atCount && !Active(m.hostMenuSelect)) { yield return "ERROR the host menu did not open\n" + log + Menus(); yield break; }
            for (int i = 0; i < 30; i++) yield return null;

            SaveData found = null;
            var all = atCount ? null : SaveManager.GetAllSaveDatasInFolder();
            if (all != null)
                foreach (SaveData d in all)
                    if (d != null && d.slotName == slot) found = d;
            if (atCount) log.AppendLine("already at the player count menu");
            else if (found != null) { m.hostMenuSelect.ActionSelectSaveData(found); log.AppendLine("selected existing slot '" + slot + "'"); }
            else { m.hostMenuSelect.StartNewGame(); log.AppendLine("started a new slot"); }

            foreach (string s in Wait(() => Active(m.hostMenuConfirm) || Active(m.playerCountMenu), 15f)) yield return s;
            for (int i = 0; i < 30; i++) yield return null;
            if (Active(m.hostMenuConfirm))
            {
                HostMenuConfirm c = m.hostMenuConfirm;
                if (found == null && c.gameNameField != null) { c.gameNameField.text = slot; log.AppendLine("named it '" + slot + "'"); }
                if (c.passwordField != null && string.IsNullOrEmpty(c.passwordField.text)) c.passwordField.text = "bwmtest";
                for (int i = 0; i < 10; i++) yield return null;
                log.AppendLine("confirm: ready " + c.IsReadyToContinue() + ", password required " + c.passwordRequired);
                c.ActionStart();
                log.AppendLine("confirm -> start");
            }
            foreach (string s in Wait(() => Active(m.playerCountMenu) || Active(m.loadingMenu) || NetworkClient.localPlayer != null, 15f)) yield return s;
            for (int i = 0; i < 30; i++) yield return null;
            if (Active(m.playerCountMenu))
            {
                int players = Mathf.Clamp((int)r.Num("players", 2f), 2, 4);
                m.playerCountMenu.settingsRow.ActionSelect(players - 2);      // the row's options: 2, 3, 4+ players
                for (int i = 0; i < 20; i++) yield return null;
                m.playerCountMenu.ActionProceed();
                log.AppendLine("player count " + players + " -> proceed");
            }
            foreach (string s in Wait(() => NetworkClient.localPlayer != null, 150f)) yield return s;
            if (NetworkClient.localPlayer == null) { yield return "ERROR no walk after 150 s\n" + log + Menus(); yield break; }
            for (int i = 0; i < 120; i++) yield return null;
            yield return "hosting\n" + log;
        }
    }
}
