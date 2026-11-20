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

        void Update()
        {
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
                        + "find?name=text[&max=40] | host[?slot=BWM Test][&players=2] | menu | call?type=T&method=M | env | terrain[?filter=Snow][&trees] | global?name=_X&value=1 | terrainset?prop=_X&value=1 | snowtest[?undo] | move?name=N[&nx&ny&nz]&x&y&z | quit";
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
