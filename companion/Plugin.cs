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
            InputFake.Install();
            Server.Start(Port);
            L.LogInfo("[BWM] Companion listening on http://127.0.0.1:" + Port);
        }
    }

    // Pretend input: makes the game's input system (Rewired) report actions as pressed, so a test can jump, crouch and
    // steer like a player without the window having the keyboard.  press?action=crouch,moveY&ms=1500  holds the
    // crouch button and pushes the moveY axis to 1 (or &value=) for 1.5 s. Action names are those of `actions`.
    internal static class InputFake
    {
        static readonly Dictionary<int, float> held = new Dictionary<int, float>();
        static readonly Dictionary<int, int> downFrame = new Dictionary<int, int>(), upFrame = new Dictionary<int, int>();
        internal static int Hooked, Asked;

        internal static void Install()
        {
            var harmony = new HarmonyLib.Harmony("bwm.companion.input");
            foreach (MethodInfo m in typeof(Rewired.Player).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 1 || (ps[0].ParameterType != typeof(int) && ps[0].ParameterType != typeof(string))) continue;
                string fix = null;
                if (m.ReturnType == typeof(bool) && m.Name == "GetButton") fix = "Button";
                else if (m.ReturnType == typeof(bool) && m.Name == "GetButtonDown") fix = "Down";
                else if (m.ReturnType == typeof(bool) && m.Name == "GetButtonUp") fix = "Up";
                else if (m.ReturnType == typeof(float) && (m.Name == "GetAxis" || m.Name == "GetAxisRaw")) fix = "Axis";
                if (fix == null) continue;
                fix += ps[0].ParameterType == typeof(int) ? "Int" : "Str";
                try
                {
                    harmony.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(InputFake).GetMethod(fix, BindingFlags.Static | BindingFlags.NonPublic)));
                    Hooked++;
                }
                catch (Exception e) { Plugin.L.LogWarning("[BWM] could not hook Rewired " + m.Name + ": " + e.Message); }
            }
            Plugin.L.LogInfo("[BWM] pretend input: " + Hooked + " Rewired methods hooked");
        }

        static int Id(string name) { try { return Rewired.ReInput.mapping.GetActionId(name); } catch (Exception) { return -1; } }
        static bool IsHeld(int id) { Asked++; return held.TryGetValue(id, out float v) && v != 0f; }
        static void ButtonInt(int __0, ref bool __result) { if (held.Count > 0 && IsHeld(__0)) __result = true; }
        static void ButtonStr(string __0, ref bool __result) { if (held.Count > 0 && IsHeld(Id(__0))) __result = true; }
        static void DownInt(int __0, ref bool __result) { if (downFrame.TryGetValue(__0, out int f) && f == Time.frameCount) __result = true; }
        static void DownStr(string __0, ref bool __result) { if (downFrame.Count > 0) DownInt(Id(__0), ref __result); }
        static void UpInt(int __0, ref bool __result) { if (upFrame.TryGetValue(__0, out int f) && f == Time.frameCount) __result = true; }
        static void UpStr(string __0, ref bool __result) { if (upFrame.Count > 0) UpInt(Id(__0), ref __result); }
        static void AxisInt(int __0, ref float __result) { if (held.Count > 0 && held.TryGetValue(__0, out float v)) { Asked++; __result = v; } }
        static void AxisStr(string __0, ref float __result) { if (held.Count > 0) AxisInt(Id(__0), ref __result); }

        internal static IEnumerator<string> Press(Request r)
        {
            var ids = new List<int>();
            foreach (string name in r.Str("action", "").Split(','))
            {
                int id = Id(name.Trim());
                if (id < 0) { yield return "ERROR no input action '" + name + "' (see /actions)"; yield break; }
                ids.Add(id);
            }
            float value = r.Has("value") ? r.Num("value") : 1f;
            float until = Time.unscaledTime + (r.Has("ms") ? r.Num("ms") : 200f) / 1000f;
            int asked = Asked;
            foreach (int id in ids) { held[id] = value; downFrame[id] = Time.frameCount + 1; }
            while (Time.unscaledTime < until) yield return null;
            foreach (int id in ids) { held.Remove(id); upFrame[id] = Time.frameCount + 1; }
            yield return "pressed " + r.Str("action") + " (the game asked " + (Asked - asked) + " times)";
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
                        + "find?name=text[&max=40] | host[?slot=BWM Test][&players=2] | menu | call?type=T&method=M | env | terrain[?filter=Snow][&trees] | global?name=_X&value=1 | terrainset?prop=_X&value=1 | snowtest[?undo] | move?name=N[&nx&ny&nz]&x&y&z | look?x&y&z[&n=12] | ground?x&z[&all=1] | sit?path=Full/Path/To/Seat (or ?stand) | walk?x&z[&speed=1.5] | get?path=Type.member... | set?path=...&value=v | player[?do=jump|dud] | press?action=jump[,moveY...][&value=1][&ms=200] | crest[?kind=Level][&off=N|&on=N] | quit";
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
                case "mat": yield return Materials(r); break;
                case "active": yield return SetActive(r); break;
                case "comp": yield return Components(r); break;
                case "inspect": yield return InspectFirst(r); break;
                case "actions": yield return InputActions(); break;
                case "treemat": yield return TreeMaterials(r); break;
                case "trees": yield return NearTrees(r); break;
                case "crest": yield return CrestInputs(r); break;
                case "menu": yield return Host.Menus(); break;
                case "call": yield return Call(r); break;
                case "player": yield return PlayerState(r); break;
                case "press": { IEnumerator<string> e = InputFake.Press(r); while (e.MoveNext()) yield return e.Current; break; }
                case "env": yield return Env(); break;
                case "render": yield return Render(r); break;
                case "perf": { IEnumerator<string> e = Perf(r); while (e.MoveNext()) yield return e.Current; break; }
                case "textures": yield return Textures(r); break;
                case "terrain": yield return TerrainInfo(r); break;
                case "global": Shader.SetGlobalFloat(r.Str("name"), r.Num("value")); yield return "set global " + r.Str("name") + " = " + F(r.Num("value")); break;
                case "terrainset": yield return TerrainSet(r); break;
                case "snowtest": yield return SnowTest(r); break;
                case "move": yield return Move(r); break;
                case "look": yield return Look(r); break;
                case "ground": yield return GroundHeight(r); break;
                case "sit": yield return Sit(r); break;
                case "walk": { IEnumerator<string> e = Walk(r); while (e.MoveNext()) yield return e.Current; break; }
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
            // &yaw=degrees (0 = +z, 90 = +x): which way the player faces
            Quaternion facing = r.Has("yaw") ? Quaternion.Euler(0f, r.Num("yaw"), 0f) : player.transform.rotation;
            if (r.Has("yaw")) player.transform.rotation = facing;
            if (body != null)
            {
                body.position = target;
                if (r.Has("yaw")) body.rotation = facing;
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

        // Loaded textures: textures?filter=Snow,Fir,CastIron lists those whose name holds any of the words (size, format,
        // mips), and sums up the sizes of all the rest (the base game's) for comparison
        static string Textures(Request r)
        {
            string[] words = r.Str("filter", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var sizes = new List<int>();
            int listed = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Texture2D>()))
            {
                Texture2D t = o.TryCast<Texture2D>();
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                bool ours = words.Any(w => t.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                if (ours) { listed++; sb.AppendLine("  " + t.name + " " + t.width + "x" + t.height + " " + t.format + " mips " + t.mipmapCount); }
                else sizes.Add(Mathf.Max(t.width, t.height));
            }
            sizes.Sort();
            int Count(int min) => sizes.Count(s => s >= min);
            string spread = sizes.Count == 0 ? "none" : "median " + sizes[sizes.Count / 2] + ", 4096+ " + Count(4096) + ", 2048+ " + Count(2048) + ", 1024+ " + Count(1024) + ", 512+ " + Count(512) + " of " + sizes.Count;
            return listed + " textures matching '" + r.Str("filter", "") + "':\n" + sb + "all other Texture2Ds, by their longer side: " + spread;
        }

        // The frame rate here, now: perf?s=3 waits s/2 seconds to settle, then measures s seconds of frames:
        // "fps 120 (1% low 95, worst 14.2 ms) over 360 frames | camera ... | quality ..."
        internal static IEnumerator<string> Perf(Request r)
        {
            float span = r.Has("s") ? r.Num("s") : 3f;
            float settle = Time.unscaledTime + span * 0.5f;
            while (Time.unscaledTime < settle) yield return null;
            var times = new List<float>();
            float end = Time.unscaledTime + span;
            while (Time.unscaledTime < end) { yield return null; times.Add(Time.unscaledDeltaTime); }
            if (times.Count == 0) { yield return "ERROR no frames"; yield break; }
            float total = 0f; foreach (float t in times) total += t;
            var sorted = new List<float>(times); sorted.Sort();
            float p99 = sorted[Mathf.Clamp((int)(sorted.Count * 0.99f), 0, sorted.Count - 1)];
            Camera cam = Camera.main;
            string view = "no camera";
            if (cam != null)
            {
                Vector3 f = cam.transform.forward;
                view = "camera at " + V(cam.transform.position) + " facing " + F(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg) + " pitch " + F(Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg);
            }
            yield return "fps " + F(times.Count / total) + " (1% low " + F(1f / p99) + ", worst " + F(sorted[sorted.Count - 1] * 1000f) + " ms) over " + times.Count + " frames | " + view + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()];
        }

        // The graphics settings for testing what draws wrong where: render  lists the quality levels and the renderer's
        // features;  render?quality=2  switches the quality level (as the game's settings would);
        // render?feature=HBAO&on=0  switches one renderer feature off (1 on) until the next change of level
        static string Render(Request r)
        {
            var sb = new StringBuilder();
            if (r.Has("quality")) { QualitySettings.SetQualityLevel((int)r.Num("quality"), true); sb.AppendLine("quality level set to " + (int)r.Num("quality")); }
            // render?awake=1: the local player never falls asleep (so the view holds still between screenshots)
            if (r.Has("awake")) { PlayerCharacter me = LocalCharacter(); if (me != null && me.sleeper != null) { me.sleeper.preventSleeping = r.Num("awake") > 0.5f; me.sleeper.RecordAction(); sb.AppendLine("player kept awake: " + me.sleeper.preventSleeping); } }
            int q = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            sb.AppendLine("quality: " + (q < names.Length ? names[q] : "?") + " (" + q + ") of " + string.Join(", ", names.Select((n, i) => i + " " + n)));
            var urp = (QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline)?.TryCast<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
            if (urp == null) return sb.Append("no URP asset").ToString();
            UnityEngine.Rendering.Universal.ScriptableRendererData data = urp.scriptableRendererData;
            sb.AppendLine("pipeline " + urp.name + ", renderer " + (data != null ? data.name : "none") + ", render scale " + F(urp.renderScale) + ", shadows " + F(urp.shadowDistance) + " m, soft " + urp.supportsSoftShadows + ", HDR " + urp.supportsHDR + ", MSAA " + urp.msaaSampleCount);
            if (data == null) return sb.ToString();
            var features = data.rendererFeatures;
            for (int i = 0; i < features.Count; i++)
            {
                var f = features[i];
                if (f == null) continue;
                if (r.Has("feature") && f.name.IndexOf(r.Str("feature"), StringComparison.OrdinalIgnoreCase) >= 0) { f.SetActive(r.Num("on", 1f) > 0.5f); sb.AppendLine("set " + f.name + " " + (f.isActive ? "on" : "off")); }
                sb.AppendLine("  feature " + f.name + " (" + f.GetIl2CppType().Name + ") " + (f.isActive ? "on" : "off"));
            }
            return sb.ToString();
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

        // What is drawn near a point, and with what: /mat?x&y&z[&r=12] lists every renderer within r metres (path,
        // mesh, shadows) with its materials (shader, textures and their sizes, main colours) and every light there.
        // For learning how the game itself builds something (its tunnels) before making our own to match.
        // The materials of the terrain's tree kinds (the base island's trees are terrain instances, not objects):
        // /treemat?kind=Stringybark lists each matching prototype's renderers, meshes, bounds and materials.
        static string TreeMaterials(Request r)
        {
            string kind = r.Str("kind", "");
            var sb = new StringBuilder();
            var seenKinds = new HashSet<string>();
            foreach (Terrain t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                if (t == null || t.terrainData == null) continue;
                foreach (TreePrototype proto in t.terrainData.treePrototypes)
                {
                    if (proto == null || proto.prefab == null) continue;
                    string n = proto.prefab.name;
                    if (kind != "" && n.IndexOf(kind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!seenKinds.Add(n)) continue;
                    sb.AppendLine("tree " + n);
                    foreach (Renderer rend in proto.prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        MeshFilter mf = rend.GetComponent<MeshFilter>();
                        sb.AppendLine("  " + rend.name + " | bounds " + V(rend.bounds.size) + (mf != null && mf.sharedMesh != null ? " | mesh " + mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount + " v" : ""));
                        foreach (Material m in rend.sharedMaterials) DescribeMaterial(sb, m, "    ");
                    }
                }
            }
            return sb.Length > 0 ? sb.ToString() : "no tree kind matching '" + kind + "'";
        }

        // The terrain trees nearest a point: /trees?x&z[&n=10][&kind=Text] gives each one's kind, place and height
        static string NearTrees(Request r)
        {
            var at = new Vector2(r.Num("x"), r.Num("z"));
            int want = (int)r.Num("n", 10);
            string kind = r.Str("kind", "");
            var found = new List<(float d, string line)>();
            foreach (Terrain t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                if (t == null || t.terrainData == null) continue;
                TerrainData data = t.terrainData;
                TreePrototype[] protos = data.treePrototypes;
                Vector3 origin = t.transform.position, size = data.size;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    Vector3 w = origin + Vector3.Scale(tree.position, size);
                    float d = (new Vector2(w.x, w.z) - at).magnitude;
                    if (d > 200f) continue;
                    string name = tree.prototypeIndex < protos.Length && protos[tree.prototypeIndex].prefab != null ? protos[tree.prototypeIndex].prefab.name : "?";
                    if (kind != "" && name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found.Add((d, name + " | " + V(w) + " | scale " + F(tree.heightScale) + " | " + F(d) + " m"));
                }
            }
            found.Sort((a2, b2) => a2.d.CompareTo(b2.d));
            var sb = new StringBuilder();
            for (int i = 0; i < found.Count && i < want; i++) sb.AppendLine(found[i].line);
            return sb.Length > 0 ? sb.ToString() : "no trees within 200 m";
        }

        static void DescribeMaterial(StringBuilder sb, Material m, string indent)
        {
            if (m == null) return;
            sb.AppendLine(indent + "material " + m.name + " | shader " + (m.shader != null ? m.shader.name : "none"));
            string[] names = null;
            try { names = m.GetTexturePropertyNames(); } catch (Exception) { }
            if (names == null) return;
            foreach (string n in names)
            {
                try
                {
                    Texture t = m.GetTexture(n);
                    if (t == null) continue;
                    Vector2 scale = m.GetTextureScale(n);
                    sb.AppendLine(indent + "  " + n + ": " + t.name + " " + t.width + "x" + t.height + " scale " + F(scale.x) + ", " + F(scale.y) + " filter " + t.filterMode + " aniso " + t.anisoLevel);
                }
                catch (Exception) { }
            }
        }

        static string Materials(Request r)
        {
            var at = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            float radius = r.Num("r", 12f);
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            string[] common = { "_BaseMap", "_MainTex", "_BumpMap", "_NormalMap", "_MetallicGlossMap", "_OcclusionMap", "_DetailAlbedoMap", "_DetailNormalMap", "_EmissionMap", "_MaskMap", "_Albedo", "_Normal" };
            string[] colours = { "_BaseColor", "_Color", "_EmissionColor", "_Tint" };
            string[] floats = { "_Smoothness", "_Metallic", "_BumpScale", "_Tiling", "_Scale", "_TriplanarScale", "_Cull" };
            int shown = 0;
            foreach (Renderer rend in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (rend == null || (rend.bounds.ClosestPoint(at) - at).magnitude > radius) continue;
                string where = PathOf(rend.transform);
                if (where.StartsWith("Winter Dressing") || (r.Has("name") && where.IndexOf(r.Str("name"), StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (++shown > 40) break;
                MeshFilter mf = rend.GetComponent<MeshFilter>();
                sb.AppendLine(PathOf(rend.transform) + " | bounds " + V(rend.bounds.size) + (mf != null && mf.sharedMesh != null ? " | mesh " + mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount + " v" : "")
                    + " | shadows " + rend.shadowCastingMode + " | layer " + rend.gameObject.layer);
                foreach (Material m in rend.sharedMaterials)
                {
                    if (m == null) continue;
                    sb.Append("    material " + m.name + " | shader " + (m.shader != null ? m.shader.name : "none"));
                    if (!seen.Add(m.name)) { sb.AppendLine(" (above)"); continue; }
                    sb.AppendLine();
                    string[] names = null;
                    try { names = m.GetTexturePropertyNames(); } catch (Exception) { }
                    foreach (string n in names ?? common)
                    {
                        try
                        {
                            if (!m.HasProperty(n)) continue;
                            Texture t = m.GetTexture(n);
                            if (t == null) continue;
                            Vector2 scale = m.GetTextureScale(n);
                            sb.AppendLine("        " + n + ": " + t.name + " " + t.width + "x" + t.height + " scale " + F(scale.x) + ", " + F(scale.y));
                        }
                        catch (Exception) { }
                    }
                    foreach (string n in colours) try { if (m.HasProperty(n)) sb.AppendLine("        " + n + ": " + m.GetColor(n)); } catch (Exception) { }
                    foreach (string n in floats) try { if (m.HasProperty(n)) sb.AppendLine("        " + n + ": " + F(m.GetFloat(n))); } catch (Exception) { }
                }
            }
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light == null || (light.transform.position - at).magnitude > radius * 2f) continue;
                sb.AppendLine("light " + PathOf(light.transform) + " | " + light.type + " at " + V(light.transform.position) + " range " + F(light.range) + " intensity " + F(light.intensity) + " colour " + light.color
                    + " shadows " + light.shadows + (light.isActiveAndEnabled ? "" : " | off"));
            }
            sb.AppendLine("ambient: mode " + RenderSettings.ambientMode + " intensity " + F(RenderSettings.ambientIntensity) + " sky " + RenderSettings.ambientSkyColor);
            return sb.ToString();
        }

        // The game's input actions (Rewired): id, name, type, and what player 0 has on each now
        static string InputActions()
        {
            var lines = new List<string>();
            Rewired.Player player = Rewired.ReInput.players.GetPlayer(0);
            for (int id = 0; id < 256; id++)
            {
                Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                if (a == null) continue;
                string now = player == null ? "" : " now " + F(player.GetAxis(id)) + (player.GetButton(id) ? " (held)" : "");
                lines.Add(id + " " + a.name + " (" + a.type + ", " + a.descriptiveName + ")" + now);
            }
            return lines.Count == 0 ? "no input actions found" : string.Join(Environment.NewLine, lines);
        }

        // The fields and properties of the first live object of a game type: /inspect?type=Train[&filter=speed]
        static string InspectFirst(Request r)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(r.Str("type", ""))).FirstOrDefault(t => t != null);
            if (type == null) return "ERROR no type '" + r.Str("type") + "'";
            object typed = typeof(UnityEngine.Object).GetMethods().First(x => x.Name == "FindObjectOfType" && x.IsGenericMethod && x.GetParameters().Length == 0).MakeGenericMethod(type).Invoke(null, null);
            if (typed == null) return "ERROR no live " + type.Name;
            string filter = r.Str("filter", "");
            var sb = new StringBuilder(type.Name + " on " + PathOf(((Component)typed).transform) + Environment.NewLine);
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (filter != "" && p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (p.GetIndexParameters().Length > 0) continue;
                string v;
                try { object o = p.GetValue(typed); v = o == null ? "null" : o.ToString(); } catch (Exception e) { v = "(" + e.GetType().Name + ")"; }
                if (v.Length > 160) v = v.Substring(0, 160);
                sb.AppendLine("  " + p.Name + " = " + v);
            }
            return sb.ToString();
        }

        // Switches on or off the object (active or not) whose name is exactly ?name=, the nearest to (nx, ny, nz):
        // /active?name=UnfurledMap&nx=..&ny=..&nz=..&on=1   (for looking at things the game keeps hidden)
        // comp?list=1 counts the components on every puzzle's objects by type (behaviours, renderers, colliders);
        // comp?type=PuzzleDisplay&on=0 switches every enabled Behaviour of that type under the puzzles off (on=1 back on),
        // to measure what each costs a frame. Only objects under a puzzle (a PuzzleSDK root) are touched.
        static readonly System.Collections.Generic.List<Behaviour> compOff = new System.Collections.Generic.List<Behaviour>();
        static readonly System.Collections.Generic.List<Renderer> rendOff = new System.Collections.Generic.List<Renderer>();
        static readonly System.Collections.Generic.List<Collider> collOff = new System.Collections.Generic.List<Collider>();
        static readonly System.Collections.Generic.List<GameObject> goOff = new System.Collections.Generic.List<GameObject>();
        static string Components(Request r)
        {
            var roots = new System.Collections.Generic.HashSet<IntPtr>();
            foreach (PuzzleSDK p in Puzzles()) roots.Add(p.transform.root.Pointer);
            if (r.Has("type"))
            {
                string type = r.Str("type"); bool on = r.Num("on", 0f) > 0.5f; int n = 0;
                if (on)
                {
                    foreach (Behaviour b in compOff) if (b != null) { b.enabled = true; n++; }
                    foreach (Renderer x in rendOff) if (x != null) { x.enabled = true; n++; }
                    foreach (Collider x in collOff) if (x != null) { x.enabled = true; n++; }
                    foreach (GameObject g in goOff) if (g != null) { g.SetActive(true); n++; }
                    compOff.Clear(); rendOff.Clear(); collOff.Clear(); goOff.Clear();
                    return "switched " + n + " back on";
                }
                if (type == "Renderer" || type == "Collider")
                {
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(type == "Renderer" ? Il2CppInterop.Runtime.Il2CppType.Of<Renderer>() : Il2CppInterop.Runtime.Il2CppType.Of<Collider>(), true))
                    {
                        Component c = o != null ? o.TryCast<Component>() : null;
                        if (c == null || !roots.Contains(c.transform.root.Pointer)) continue;
                        Renderer rr = c.TryCast<Renderer>(); Collider cc = c.TryCast<Collider>();
                        if (rr != null && rr.enabled) { rr.enabled = false; rendOff.Add(rr); n++; }
                        if (cc != null && cc.enabled) { cc.enabled = false; collOff.Add(cc); n++; }
                    }
                    return "switched " + n + " " + type + "s off";
                }
                if (r.Has("go"))
                {
                    // deactivate the objects that carry this component (OnDisable: off any manager's list)
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Component>(), true))
                    {
                        Component c = o != null ? o.TryCast<Component>() : null;
                        if (c == null || c.GetIl2CppType().Name != type || !roots.Contains(c.transform.root.Pointer) || !c.gameObject.activeSelf) continue;
                        c.gameObject.SetActive(false); goOff.Add(c.gameObject); n++;
                    }
                    return "deactivated " + n + " objects with a " + type;
                }
                if (type == "Bodies")
                {
                    int awake = 0, kin = 0, total = 0;
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Rigidbody>(), true))
                    {
                        Rigidbody b = o != null ? o.TryCast<Rigidbody>() : null;
                        if (b == null || !roots.Contains(b.transform.root.Pointer)) continue;
                        total++; if (b.isKinematic) kin++; else if (!b.IsSleeping()) awake++;
                        if (r.Has("sleep") && !b.isKinematic) b.Sleep();
                    }
                    return total + " rigidbodies on the puzzles: " + kin + " kinematic, " + awake + " awake (not kinematic)";
                }
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Behaviour>(), true))
                {
                    Behaviour b = o != null ? o.TryCast<Behaviour>() : null;
                    if (b == null || !b.enabled || !roots.Contains(b.transform.root.Pointer)) continue;
                    string bn = b.GetIl2CppType().Name;
                    // type=* every behaviour; type=range:A:L those whose type name starts A..L (for bisecting)
                    if (type == "*") { }
                    else if (type.StartsWith("range:")) { string[] ab = type.Split(':'); char f0 = char.ToUpperInvariant(bn[0]); if (f0 < ab[1][0] || f0 > ab[2][0]) continue; }
                    else if (bn != type) continue;
                    b.enabled = false; compOff.Add(b); n++;
                }
                return "switched " + n + " " + type + " off";
            }
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Component>(), true))
            {
                Component c = o != null ? o.TryCast<Component>() : null;
                if (c == null || !roots.Contains(c.transform.root.Pointer)) continue;
                string k = c.GetIl2CppType().Name;
                counts[k] = counts.TryGetValue(k, out int v) ? v + 1 : 1;
            }
            var sb = new StringBuilder();
            foreach (var kv in counts.OrderByDescending(kv => kv.Value)) sb.AppendLine(kv.Value + " " + kv.Key);
            return sb.ToString();
        }

        static string SetActive(Request r)
        {
            // active?prefix=Grass%20&on=0 switches every object whose name starts with that (for measuring what costs)
            if (r.Has("prefix"))
            {
                string pre = r.Str("prefix"); int n = 0; bool on = r.Num("on", 1f) > 0.5f;
                foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                    if (t.name.StartsWith(pre, StringComparison.Ordinal)) { t.gameObject.SetActive(on); n++; }
                return "switched " + n + " objects named '" + pre + "...' " + (on ? "on" : "off");
            }
            var near = new Vector3(r.Num("nx"), r.Num("ny"), r.Num("nz"));
            Transform best = null; float bestD = float.MaxValue;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != r.Str("name", "")) continue;
                float d = (t.position - near).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == null) return "ERROR nothing named '" + r.Str("name") + "'";
            best.gameObject.SetActive(r.Num("on", 1f) > 0.5f);
            var sb = new StringBuilder(PathOf(best) + " active " + best.gameObject.activeSelf + " at " + V(best.position) + " rotation " + V(best.eulerAngles) + " scale " + V(best.lossyScale));
            Renderer rend = best.GetComponent<Renderer>();
            if (rend != null)
            {
                sb.Append(" | bounds " + V(rend.bounds.size));
                foreach (Material m in rend.sharedMaterials)
                    if (m != null) sb.Append(" | material " + m.name + " (" + (m.shader != null ? m.shader.name : "none") + ")" + (m.mainTexture != null ? " texture " + m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : ""));
            }
            return sb.ToString();
        }

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

        // Crest's water inputs (water level, depth, clip and so on): /crest[?kind=Level] lists them with an index,
        // &off=N / &on=N switches one (the index is into the same filtered list)
        static string CrestInputs(Request r)
        {
            string kind = r.Str("kind", "");
            var found = new List<MonoBehaviour>();
            foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string tn = mb.GetIl2CppType().Name;
                if (!tn.EndsWith("LodInput") && tn != "WaterBody" && tn != "DepthProbe") continue;
                if (kind != "" && tn.IndexOf(kind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found.Add(mb);
            }
            found.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
            if (r.Has("off") || r.Has("on"))
            {
                int i = (int)r.Num(r.Has("off") ? "off" : "on");
                if (i < 0 || i >= found.Count) return "ERROR no input " + i;
                found[i].enabled = r.Has("on");
                return (r.Has("on") ? "enabled " : "disabled ") + i + " " + PathOf(found[i].transform);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < found.Count; i++)
            {
                MonoBehaviour mb = found[i];
                sb.Append(i + " " + mb.GetIl2CppType().Name + " | " + PathOf(mb.transform) + " | " + (mb.isActiveAndEnabled ? "on" : mb.enabled ? "inactive" : "off")
                    + " | at " + V(mb.transform.position) + " scale " + V(mb.transform.lossyScale));
                string full = mb.GetIl2CppType().FullName;
                Type managed = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(full)).FirstOrDefault(x => x != null);
                if (managed != null)
                {
                    try
                    {
                        object w = Activator.CreateInstance(managed, mb.Pointer);
                        foreach (string p in new[] { "Mode", "Weight", "Queue", "Blend", "HeightRange", "FeatherWidth" })
                        {
                            PropertyInfo pi = managed.GetProperty(p, Any | BindingFlags.FlattenHierarchy);
                            if (pi == null) continue;
                            try { sb.Append(" | " + p + " " + pi.GetValue(w)); } catch { }
                        }
                    }
                    catch (Exception e) { sb.Append(" | (" + e.GetType().Name + ")"); }
                }
                Renderer rend = mb.GetComponent<Renderer>();
                if (rend != null) sb.Append(" | renderer " + V(rend.bounds.min) + " .. " + V(rend.bounds.max) + (rend.enabled ? "" : " (off)"));
                sb.AppendLine();
            }
            sb.AppendLine(found.Count + " input(s)");
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
        // Seats the local player in a seat (an SDK Custom Chair, or any object with the game's PlayerPose under it),
        // as clicking it would: sit?path=Winter Isle root/.../Seat Left. sit?stand gets up. For testing what
        // carries a seated player (the gondola).
        static string Sit(Request r)
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null) return "ERROR not in a walk";
            PlayerNetworking net = player.GetComponent<PlayerNetworking>();
            if (net == null) return "ERROR the player has no PlayerNetworking";
            var methods = typeof(PlayerNetworking).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            int action = 1;
            try { action = net.poseReference.actionNumber + 1; } catch (Exception) { }
            if (r.Has("stand"))
            {
                MethodInfo exit = methods.FirstOrDefault(m => m.Name == "CmdExitPose" && m.GetParameters().Length == 1);
                if (exit == null) return "ERROR no CmdExitPose";
                exit.Invoke(net, new object[] { action });
                return "asked to stand (action " + action + ")";
            }
            Transform t = Objects.Locate(r);
            if (t == null) return "ERROR no seat at that path or name";
            PlayerPose pose = t.GetComponentInChildren<PlayerPose>(true);
            if (pose == null) return "ERROR no PlayerPose under " + t.name;
            MethodInfo enter = methods.FirstOrDefault(m => m.Name == "CmdEnterPose" && m.GetParameters().Length == 2);
            if (enter == null) return "ERROR no CmdEnterPose";
            enter.Invoke(net, new object[] { pose.shellReference, action });
            return "asked to sit in " + t.name + " (ticket " + pose.ticket + ", action " + action + "); ServerEnterPoseAuto takes ("
                + string.Join(", ", methods.Where(m => m.Name == "ServerEnterPoseAuto").SelectMany(m => m.GetParameters()).Select(p => p.ParameterType.Name)) + ")";
        }

        // The height of the ground itself at x, z (terrain or the island's land, not a tree, rock or cabin on it):
        // ground?x=..&z=..  ->  "ground: 12.3" or "ground: none". With &all=1, the highest thing a player could
        // stand on there, rocks included (much of the base game's island is rock, not terrain).
        // The local player's movement state (ground, grip, pose, jump, crouch...), for "why can't I jump" questions:
        // player  lists every plain value of the movement components;  player?do=jump  forces a jump first
        static string PlayerState(Request r)
        {
            PlayerCharacter me = LocalCharacter();
            if (me == null) return "ERROR not in a walk";
            var sb = new StringBuilder();
            string act = r.Str("do", "");
            try
            {
                if (act == "jump") { me.jumper.ForceAJump(); sb.AppendLine("forced a jump"); }
                else if (act == "onjump") { me.jumper.OnJump(); sb.AppendLine("called OnJump"); }
                else if (act == "dud") { me.jumper.DoDudJump(); sb.AppendLine("did a dud jump"); }
            }
            catch (Exception e) { sb.AppendLine("ERROR " + act + ": " + e.Message); }
            sb.AppendLine("position " + me.transform.position + " velocity " + (me.rb != null ? me.rb.linearVelocity.ToString() : "?"));
            try
            {
                Rigidbody rb = me.rb;
                if (act == "wake") { rb.WakeUp(); sb.AppendLine("woke the body"); }
                sb.AppendLine("body: sleeping " + rb.IsSleeping() + " kinematic " + rb.isKinematic + " gravity " + rb.useGravity + " constraints " + rb.constraints + " collisions " + rb.detectCollisions + " position " + rb.position.ToString("F3") + " on " + PathOf(rb.transform) + " active " + rb.gameObject.activeInHierarchy);
                Transform from = me.ground.castFrom;
                if (from != null)
                {
                    sb.Append("cast from " + from.position.ToString("F3") + " mask " + me.ground.layerMask.value + ": ");
                    foreach (RaycastHit hit in Physics.SphereCastAll(from.position, me.ground.radius, Vector3.down, me.ground.castLength, me.ground.layerMask.value, QueryTriggerInteraction.Ignore))
                        if (hit.collider != null) sb.Append(PathOf(hit.collider.transform) + " at " + F(hit.distance) + "; ");
                    sb.AppendLine();
                    sb.Append("everything within 1.5 m below: ");
                    foreach (RaycastHit hit in Physics.RaycastAll(from.position, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Collide))
                        if (hit.collider != null) sb.Append(hit.collider.name + " (layer " + hit.collider.gameObject.layer + (hit.collider.isTrigger ? ", trigger" : "") + ") at " + F(hit.distance) + "; ");
                    sb.AppendLine();
                }
            }
            catch (Exception e) { sb.AppendLine("ERROR body: " + e.Message); }
            try
            {
                Collider g = me.ground.groundCollider;
                sb.AppendLine("ground collider: " + (g == null ? "none" : PathOf(g.transform) + " | layer " + g.gameObject.layer + " | material " + (g.sharedMaterial != null ? g.sharedMaterial.name : "none") + " | steep material " + me.ground.IsSteepMaterial(g)));
                sb.AppendLine("grip " + F(me.ground.GetGroundGrip()) + " steepness " + F(me.ground.GetSteepness()) + " normal " + me.ground.normal + " still for sliding " + me.mover.IsStandingStillForSliding());
                sb.AppendLine("pose: " + (me.poser.currentPose == null ? "none" : PathOf(me.poser.currentPose.transform)));
            }
            catch (Exception e) { sb.AppendLine("ERROR ground: " + e.Message); }
            object[] parts = { me.ground, me.jumper, me.mover, me.croucher, me.grease, me.sitter, me.poser, me.sleeper, me.faller, me.platformer, me.sprinter, me.tunings };
            foreach (object part in parts)
            {
                if (part == null) continue;
                sb.Append(part.GetType().Name + ":");
                foreach (PropertyInfo p in part.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Type t = p.PropertyType;
                    if (!(t.IsPrimitive || t.IsEnum || t == typeof(Vector3)) || p.Name.StartsWith("log") || p.Name.StartsWith("draw") || p.GetIndexParameters().Length > 0) continue;
                    try { object v = p.GetValue(part); sb.Append(" " + p.Name + "=" + (v is float f ? F(f) : v)); } catch (Exception) { }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string GroundHeight(Request r)
        {
            float best = float.MinValue;
            bool all = r.Has("all");
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(r.Num("x"), 3000f, r.Num("z")), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider col = hit.collider;
                if (col == null || col.TryCast<CapsuleCollider>() != null || col.TryCast<SphereCollider>() != null) continue;
                string root = col.transform.root.name, name = col.name;
                if (!all && (root == "Winter Dressing" || name.StartsWith("rock") || name == "collider" || name.StartsWith("Cabin"))) continue;
                best = Mathf.Max(best, hit.point.y);
            }
            return best > float.MinValue ? "ground: " + F(best) : "ground: none";
        }

        // How far the view is open along the level from an eye position, in n directions (yaw 0 = +z, 90 = +x):
        // look?x=..&y=..&z=..&n=12  ->  "open: 3.2 60 60 ..." (metres to the first thing in the way, at most 60)
        static string Look(Request r)
        {
            var eye = new Vector3(r.Num("x"), r.Num("y"), r.Num("z"));
            int n = r.Has("n") ? Mathf.Max(1, (int)r.Num("n")) : 12;
            PlayerCharacter me = LocalCharacter();
            Transform self = me != null ? me.transform.root : null;
            var sb = new StringBuilder("open:");
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n, best = 60f;
                foreach (RaycastHit hit in Physics.RaycastAll(eye, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), 60f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || (self != null && hit.collider.transform.root == self)) continue;
                    best = Mathf.Min(best, hit.distance);
                }
                sb.Append(' ').Append(F(best));
            }
            return sb.ToString();
        }

        // Moves the player over the ground in a straight line to x, z at a speed (metres a second), keeping them at
        // the height above the ground they started at: walk?x=..&z=..&speed=1.5. For testing what reacts to a
        // player moving (tracks in the snow); it does not play the game's own walk.
        static IEnumerator<string> Walk(Request r)
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null) { yield return "ERROR not in a walk"; yield break; }
            PlayerCharacter character = player.GetComponent<PlayerCharacter>();
            Rigidbody body = character != null ? character.rb : player.GetComponent<Rigidbody>();
            Transform root = player.transform.root;
            float Ground(float x, float z, float near)
            {
                float best = float.MinValue;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, near + 3f, z), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider != null && hit.collider.transform.root != root) best = Mathf.Max(best, hit.point.y);
                return best > float.MinValue ? best : near;
            }
            Vector3 start = player.transform.position;
            float lift = Mathf.Clamp(start.y - Ground(start.x, start.z, start.y), 0f, 1f);
            var target = new Vector2(r.Num("x"), r.Num("z"));
            float speed = r.Has("speed") ? r.Num("speed") : 1.5f;
            for (int frame = 0; frame < 6000; frame++)
            {
                Vector3 p = player.transform.position;
                Vector2 to = target - new Vector2(p.x, p.z);
                float step = speed * Time.deltaTime;
                if (to.magnitude <= step) break;
                Vector2 d = to.normalized * step;
                var next = new Vector3(p.x + d.x, 0f, p.z + d.y);
                next.y = Ground(next.x, next.z, p.y) + lift;
                player.transform.position = next;
                if (body != null) { body.position = next; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                yield return null;
            }
            yield return "walked to " + V(player.transform.position) + " (riding " + F(lift) + " m above the ground)";
        }

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
                // ch / th: the camera's / target's height above the ground there (found by a ray from high up),
                // instead of cy / ty, so eye-level shots are never taken from inside a hill
                float GroundAt(float x, float z)
                {
                    // the ground itself: the highest hit that is not a tree, a rock or one of Winter Walk's copies
                    float best = float.MinValue;
                    foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        Collider col = hit.collider;
                        if (col == null || col.TryCast<CapsuleCollider>() != null || col.TryCast<SphereCollider>() != null) continue;
                        string root = col.transform.root.name, name = col.name;
                        if (root == "Winter Dressing" || name.StartsWith("rock") || name == "collider" || name.StartsWith("Cabin")) continue;
                        best = Mathf.Max(best, hit.point.y);
                    }
                    return best > float.MinValue ? best : 0f;
                }
                float cy = r.Has("ch") ? Mathf.Max(GroundAt(r.Num("cx"), r.Num("cz")), 0.3f) + r.Num("ch") : r.Num("cy");
                float ty = r.Has("th") ? GroundAt(r.Num("tx"), r.Num("tz")) + r.Num("th") : r.Num("ty");
                camGo.transform.position = new Vector3(r.Num("cx"), cy, r.Num("cz"));
                camGo.transform.LookAt(new Vector3(r.Num("tx"), ty, r.Num("tz")));
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
