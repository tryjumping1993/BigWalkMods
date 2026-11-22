// Winter Walk's debug log: one file per game start that survives a crash, so a problem met while playing can be
// read afterwards, on this PC or sent in by a friend.
//
// Where: LocalLow\House House\Big Walk\WinterWalk-logs\session-<date>-<time>.log (beside the game's own logs; the
// last 12 kept). Send-Logs.cmd in the players' download zips them up.
//
// What goes in:
//   - everything BepInEx logs (the game's own Unity log included), with the time, written through to Windows at
//     once, so a crash loses nothing; heartbeats and snapshots are also forced to disk, for a PC that freezes
//   - at the start: this PC (CPU, GPU, memory, screen), the game and the mod's versions, and every pack with its
//     size and a fingerprint of the set, to compare with friends ("same version?")
//   - what became of the last session: whether it closed normally, and what Windows recorded in its time (the
//     game crashing, with module and offset; hanging; CPU hardware errors)
//   - a heartbeat every 10 s: frame rate, Winter Walk's own share of each frame, memory, the player's state
//   - events: joining and leaving walks, players coming and going, the island, our mod taking or letting go of the
//     player's body (skis, plane, jetpack, mine, dinosaurs), CPU affinity changes
//   - snapshots of the player's movement state when something looks wrong: not on the ground yet not falling,
//     jump pressed and no jump, crouch held and no crouch
//   - slow frames, and when the frame rate stays low, what is drawn (visible renderers by owner) and where the
//     camera is looking; memory past each 2 GB step, with the biggest textures and meshes
//   - F8 (config MarkKey): "something is wrong here": a snapshot, both counts and a screenshot beside the log
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WinterWalk
{
    internal static class Diag
    {
        internal static ConfigEntry<bool> On, PlayerChecks;
        internal static ConfigEntry<float> HeartbeatSeconds, SlowFrameMs, LowFps;
        internal static ConfigEntry<string> MarkKey;

        internal static string Dir, FilePath;
        const int Keep = 12;                       // session files kept
        const long Cap = 32L << 20;                // past this, only warnings and Winter Walk's own lines are written
        static readonly object gate = new object();
        static FileStream fs;
        static StreamWriter w;
        static long bytes;
        static double lastHardFlush;
        static bool ended;
        static readonly Stopwatch clock = Stopwatch.StartNew();
        internal static volatile string Pending;   // the background check's report, logged by the main thread
        internal static DateTime Started;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        internal static string V(Vector3 v) => "(" + v.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.0", CultureInfo.InvariantCulture) + ")";

        // ---- the file ----

        internal static void Start(ConfigFile cfg, string version)
        {
            On = cfg.Bind("Debug", "DebugLog", true, "Write a debug log of each session to LocalLow\\House House\\Big Walk\\WinterWalk-logs (send it in when something goes wrong).");
            PlayerChecks = cfg.Bind("Debug", "PlayerChecks", true, "Watch the player's movement and write a snapshot when it looks stuck (floating, jump or crouch doing nothing).");
            HeartbeatSeconds = cfg.Bind("Debug", "HeartbeatSeconds", 10f, "How often the debug log notes the frame rate, memory and the player's state.");
            SlowFrameMs = cfg.Bind("Debug", "SlowFrameMs", 150f, "A frame longer than this (milliseconds) is logged as a hitch.");
            LowFps = cfg.Bind("Debug", "LowFps", 24f, "When the frame rate stays under this, the log counts what is being drawn (once a session; F8 counts again).");
            MarkKey = cfg.Bind("Debug", "MarkKey", "F8", "Press in game to mark a moment in the log (with a screenshot) when something is wrong.");
            // A PC with a faulty CPU core (the dev PC's logical CPUs 4 and 5 log machine-check errors, and the game
            // crashed on that core with corrupted addresses): keep the whole game off it, however it was started
            ConfigEntry<string> mask = cfg.Bind("Debug", "CpuAffinity", "", "For a PC with a faulty CPU core only: a hex mask of the logical CPUs the game may use (FFFFFFCF leaves out CPUs 4 and 5). Empty leaves it alone.");
            if (mask.Value.Trim().Length > 0)
            {
                try
                {
                    using (Process p = Process.GetCurrentProcess()) p.ProcessorAffinity = (IntPtr)Convert.ToInt64(mask.Value.Trim(), 16);
                    Plugin.L.LogInfo("Winter Walk: kept the game to the CPUs in mask " + mask.Value.Trim() + " (config CpuAffinity)");
                }
                catch (Exception e) { Plugin.L.LogWarning("Winter Walk: CpuAffinity " + mask.Value + " not applied: " + e.Message); }
            }
            if (!On.Value) return;
            try
            {
                string root;
                try { root = Application.persistentDataPath; } catch (Exception) { root = null; }
                if (string.IsNullOrEmpty(root)) root = Paths.BepInExRootPath;
                Dir = Path.Combine(root, "WinterWalk-logs");
                Directory.CreateDirectory(Dir);
                var old = new List<string>(Directory.GetFiles(Dir, "session-*.log"));
                old.Sort(StringComparer.Ordinal);
                string previous = old.Count > 0 ? old[old.Count - 1] : null;
                for (int i = 0; i < old.Count - (Keep - 1); i++)
                {
                    try { File.Delete(old[i]); } catch (Exception) { }
                    try { foreach (string png in Directory.GetFiles(Dir, Path.GetFileNameWithoutExtension(old[i]) + "-mark*.png")) File.Delete(png); } catch (Exception) { }
                }
                Started = DateTime.Now;
                FilePath = Path.Combine(Dir, "session-" + Started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
                w = new StreamWriter(fs, new UTF8Encoding(false), 4096) { AutoFlush = true };
                lock (gate)
                {
                    w.WriteLine("Winter Walk session log");
                    w.WriteLine("started " + Started.ToString("o", CultureInfo.InvariantCulture));
                    foreach (string line in Header(version)) w.WriteLine(line);
                    // what BepInEx has logged before Winter Walk loaded (its own file is overwritten at every start)
                    w.WriteLine("---- BepInEx's log up to Winter Walk's start ----");
                    try
                    {
                        using (var r = new StreamReader(new FileStream(Path.Combine(Paths.BepInExRootPath, "LogOutput.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                            w.Write(r.ReadToEnd());
                    }
                    catch (Exception e) { w.WriteLine("(could not read LogOutput.log: " + e.Message + ")"); }
                    w.WriteLine("---- live (times are this PC's clock) ----");
                    BepInEx.Logging.Logger.Listeners.Add(new Listener());
                }
                var t = new Thread(() => { try { Pending = Previous(previous); } catch (Exception e) { Pending = "previous session not checked: " + e.Message; } }) { IsBackground = true, Name = "WinterWalk diag" };
                t.Start();
            }
            catch (Exception e)
            {
                w = null;
                Plugin.L.LogWarning("Winter Walk: no debug log: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static IEnumerable<string> Header(string version)
        {
            var lines = new List<string>();
            void Try(Func<string> f) { try { lines.Add(f()); } catch (Exception e) { lines.Add("(" + e.GetType().Name + ")"); } }
            Try(() => "Winter Walk " + version + " (WinterWalk.dll " + File.GetLastWriteTime(typeof(Diag).Assembly.Location).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "), game " + Application.version + ", Unity " + Application.unityVersion);
            Try(() => "Windows " + Environment.OSVersion.Version + ", " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " threads), RAM " + SystemInfo.systemMemorySize + " MB");
            Try(() => "GPU " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsMemorySize + " MB, " + SystemInfo.graphicsDeviceVersion + ")");
            Try(() => "screen " + Screen.width + "x" + Screen.height + ", " + Screen.fullScreenMode + ", quality level " + QualitySettings.GetQualityLevel() + ", vsync " + QualitySettings.vSyncCount + ", frame limit " + Application.targetFrameRate);
            Try(() => "CPU affinity " + Affinity());
            Try(() => "game folder " + Paths.GameRootPath + ", BepInEx " + Paths.BepInExRootPath);
            Try(() =>
            {
                var sb = new StringBuilder();
                var packs = new List<string>(Directory.GetFiles(Paths.PluginPath, "*.puzzlepack", SearchOption.AllDirectories));
                packs.AddRange(Directory.GetFiles(Paths.PluginPath, "WinterWalk*.dll", SearchOption.AllDirectories));
                packs.Sort(StringComparer.OrdinalIgnoreCase);
                uint hash = 2166136261;
                foreach (string p in packs)
                {
                    var fi = new FileInfo(p);
                    string id = fi.Name + ":" + fi.Length;
                    foreach (char c in id) { hash ^= c; hash *= 16777619; }
                    sb.Append("\n  ").Append(Path.GetRelativePath(Paths.PluginPath, p)).Append("  ").Append(fi.Length).Append(" bytes  ").Append(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                }
                return packs.Count + " packs and scripts, fingerprint " + hash.ToString("x8") + " (should match everyone else's in the walk):" + sb;
            });
            Try(() => "config: heartbeat " + F(HeartbeatSeconds.Value) + " s, slow frame " + F(SlowFrameMs.Value) + " ms, low fps " + F(LowFps.Value) + ", mark key " + MarkKey.Value + ", player checks " + PlayerChecks.Value + ", winter sky " + Plugin.Enabled.Value + ", snow tracks " + Plugin.Tracks.Value);
            return lines;
        }

        internal static string Affinity()
        {
            try { using (Process p = Process.GetCurrentProcess()) return ((long)p.ProcessorAffinity).ToString("X"); }
            catch (Exception) { return "?"; }
        }

        sealed class Listener : ILogListener
        {
            public LogLevel LogLevelFilter => LogLevel.All;
            public void LogEvent(object sender, LogEventArgs e)
            {
                try
                {
                    string source = e.Source != null ? e.Source.SourceName : "?";
                    bool serious = (e.Level & (LogLevel.Fatal | LogLevel.Error | LogLevel.Warning)) != 0;
                    lock (gate)
                    {
                        if (w == null) return;
                        if (bytes > Cap && !serious && source != "Winter Walk") return;
                        WriteLocked("[" + e.Level.ToString().PadRight(7) + ":" + source + "] " + e.Data);
                        if ((e.Level & (LogLevel.Fatal | LogLevel.Error)) != 0) FlushLocked(false);
                    }
                }
                catch (Exception) { }      // never log from here: that would come back here
            }
            public void Dispose() { }
        }

        static void WriteLocked(string text)
        {
            if (w == null) return;
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff ", CultureInfo.InvariantCulture) + text;
                w.WriteLine(line);
                bytes += line.Length + 2;
                if (bytes > Cap && bytes - line.Length - 2 <= Cap) w.WriteLine("(the log passed " + (Cap >> 20) + " MB: from here only warnings, errors and Winter Walk's own lines)");
            }
            catch (Exception) { }
        }

        // Straight into the session file (not BepInEx's log): heartbeats and other routine lines
        internal static void Write(string text) { lock (gate) WriteLocked(text); }

        // Into BepInEx's log, and so into the session file too
        internal static void Event(string text) => Plugin.L.LogInfo("Winter Walk: " + text);
        internal static void Warn(string text) => Plugin.L.LogWarning("Winter Walk: " + text);

        // Through Windows' cache to the disk itself, for a PC that freezes (a crash alone loses nothing: every line
        // has already been handed to Windows). At most once a second.
        static void FlushLocked(bool force)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (!force && now - lastHardFlush < 1.0) return;
            lastHardFlush = now;
            try { fs?.Flush(true); } catch (Exception) { }
        }
        internal static void HardFlush()
        {
            if (w == null) return;
            ThreadPool.QueueUserWorkItem(_ => { lock (gate) FlushLocked(true); });
        }

        internal static void End(string why)
        {
            lock (gate)
            {
                if (w == null || ended) return;
                ended = true;
                WriteLocked("session ended: " + why);
                FlushLocked(true);
            }
        }

        // ---- the last session ----

        // Did the previous session close normally, and what did Windows record while it ran? (Windows' event logs:
        // the game crashing or hanging, and CPU hardware errors; a crash dump in LocalAppData\CrashDumps.)
        static string Previous(string path)
        {
            if (path == null) return "no previous session log (the first with this version)";
            DateTime start = File.GetCreationTime(path), end = File.GetLastWriteTime(path);
            bool clean = false;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var head = new byte[Math.Min(4096, f.Length)];
                f.Read(head, 0, head.Length);
                foreach (string line in Encoding.UTF8.GetString(head).Split('\n'))
                    if (line.StartsWith("started ") && DateTime.TryParse(line.Substring(8).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime s)) { start = s.ToLocalTime(); break; }
                long tail = Math.Min(65536, f.Length);
                f.Seek(-tail, SeekOrigin.End);
                var buf = new byte[tail];
                f.Read(buf, 0, buf.Length);
                clean = Encoding.UTF8.GetString(buf).Contains("session ended: ");
            }
            var sb = new StringBuilder();
            sb.Append("previous session (" + Path.GetFileName(path) + ", " + start.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " to " + end.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "): ");
            sb.Append(clean ? "closed normally" : "DID NOT CLOSE NORMALLY: the game crashed, froze or was ended from outside");
            // Windows' records from its start to a few minutes after its last line (a crash report follows the crash)
            DateTime from = start.AddSeconds(-10), to = end.AddMinutes(3);
            string window = "TimeCreated[@SystemTime>='" + from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "' and @SystemTime<='" + to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "']";
            int found = 0;
            foreach (string ev in Events("Application", "*[System[(Provider[@Name='Application Error'] or Provider[@Name='Application Hang'] or Provider[@Name='.NET Runtime']) and " + window + "]]"))
            {
                if (!ev.Contains("Big Walk.exe")) continue;
                found++;
                string when = Field(ev, "Date:");
                if (when.Length >= 19) when = when.Substring(11, 8);      // (wevtutil prints local time, whatever its "Z" says)
                if (ev.Contains("Source: Application Error"))
                    sb.Append("\n  " + when + " Windows: Big Walk.exe crashed in " + Field(ev, "Faulting module name:").Split(',')[0] + " at offset " + Field(ev, "Fault offset:") + ", exception " + Field(ev, "Exception code:") + (Field(ev, "Exception code:") == "0xc0000005" ? " (access violation)" : ""));
                else if (ev.Contains("Source: Application Hang"))
                    sb.Append("\n  " + when + " Windows: Big Walk.exe stopped responding and was closed");
                else
                    sb.Append("\n  " + when + " .NET: " + Field(ev, "Description:", true) + " " + Field(ev, "Exception Info:"));
            }
            if (found == 0) sb.Append(clean ? "" : "\n  Windows recorded no crash or hang of Big Walk then (killed from outside, or the whole PC stopped)");
            var apic = new List<string>();
            foreach (string ev in Events("System", "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and " + window + "]]"))
            {
                string when = Field(ev, "Date:");
                apic.Add((when.Length >= 19 ? when.Substring(11, 8) : when) + " " + Field(ev, "Error Type:") + " on processor APIC " + Field(ev, "Processor APIC ID:"));
            }
            if (apic.Count > 0) sb.Append("\n  CPU hardware errors (WHEA) in that time: " + apic.Count + ": " + string.Join("; ", apic));
            try
            {
                string dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                if (Directory.Exists(dumps))
                    foreach (string d in Directory.GetFiles(dumps, "Big Walk.exe*.dmp"))
                    {
                        DateTime t = File.GetLastWriteTime(d);
                        if (t >= from && t <= to) sb.Append("\n  crash dump: " + d + " (" + (new FileInfo(d).Length >> 20) + " MB)");
                    }
            }
            catch (Exception) { }
            return sb.ToString();
        }

        static IEnumerable<string> Events(string log, string query)
        {
            string text;
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wevtutil.exe"), "qe " + log + " /q:\"" + query + "\" /c:20 /rd:true /f:text")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (Process p = Process.Start(psi))
                {
                    text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                }
            }
            catch (Exception) { yield break; }
            foreach (string ev in text.Split(new[] { "Event[" }, StringSplitOptions.RemoveEmptyEntries)) yield return ev;
        }

        static string Field(string ev, string name, bool last = false)
        {
            int i = last ? ev.LastIndexOf(name, StringComparison.Ordinal) : ev.IndexOf(name, StringComparison.Ordinal);
            if (i < 0) return "";
            int e = ev.IndexOf('\n', i);
            return (e < 0 ? ev.Substring(i + name.Length) : ev.Substring(i + name.Length, e - i - name.Length)).Trim();
        }

        // ---- Winter Walk's own share of each frame, and who moved the player ----

        // using var diag = Diag.Time("ski");  at the top of a driver's Update: its time goes to "ski"
        internal readonly struct Timer : IDisposable
        {
            readonly int slot; readonly long t0;
            internal Timer(int slot) { this.slot = slot; t0 = Stopwatch.GetTimestamp(); }
            public void Dispose() => Add(slot, Stopwatch.GetTimestamp() - t0);
        }

        const int Slots = 24;
        static readonly string[] names = new string[Slots];
        static int used;
        static readonly long[] thisFrame = new long[Slots], window = new long[Slots], worst = new long[Slots];
        internal static readonly long[] lastFrame = new long[Slots];
        static readonly float[] touchedAt = new float[Slots];
        static readonly bool[] moving = new bool[Slots];
        static int frame = -1;
        internal static int WindowFrames;

        static int Slot(string name)
        {
            for (int i = 0; i < used; i++) if ((object)names[i] == name || names[i] == name) return i;
            if (used == Slots) return Slots - 1;
            names[used] = name; touchedAt[used] = -1000f;
            return used++;
        }

        internal static Timer Time(string name) => new Timer(Slot(name));

        static void Add(int slot, long ticks)
        {
            Roll();
            thisFrame[slot] += ticks;
        }

        // A new frame: last frame's times go to the window's sums
        internal static void Roll()
        {
            int f = UnityEngine.Time.frameCount;
            if (f == frame) return;
            frame = f;
            for (int i = 0; i < used; i++)
            {
                lastFrame[i] = thisFrame[i];
                window[i] += thisFrame[i];
                if (thisFrame[i] > worst[i]) worst[i] = thisFrame[i];
                thisFrame[i] = 0;
            }
            WindowFrames++;
        }

        internal static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        internal static double LastFrameMs() { long t = 0; for (int i = 0; i < used; i++) t += lastFrame[i]; return Ms(t); }

        // "trails 0.6, train 0.3" for the biggest shares; per frame on average over the window, or of the last frame
        internal static string Shares(bool last, int top, out double total)
        {
            long[] src = last ? lastFrame : window;
            int frames = last ? 1 : Math.Max(1, WindowFrames);
            long sum = 0;
            for (int i = 0; i < used; i++) sum += src[i];
            total = Ms(sum) / frames;
            var order = new List<int>();
            for (int i = 0; i < used; i++) if (src[i] > 0) order.Add(i);
            order.Sort((a, b) => src[b].CompareTo(src[a]));
            var sb = new StringBuilder();
            for (int k = 0; k < order.Count && k < top; k++)
            {
                if (k > 0) sb.Append(", ");
                sb.Append(names[order[k]]).Append(' ').Append((Ms(src[order[k]]) / frames).ToString("0.00", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        internal static void ResetWindow()
        {
            for (int i = 0; i < used; i++) { window[i] = 0; worst[i] = 0; }
            WindowFrames = 0;
        }

        // Called where Winter Walk writes the local player's body (position or velocity)
        internal static void Touched(string who) => touchedAt[Slot(who)] = UnityEngine.Time.unscaledTime;

        internal static bool AnyTouch(float within)
        {
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < used; i++) if (now - touchedAt[i] < within) return true;
            return false;
        }

        internal static string Touches(float within)
        {
            float now = UnityEngine.Time.unscaledTime;
            var sb = new StringBuilder();
            for (int i = 0; i < used; i++)
                if (now - touchedAt[i] < within) sb.Append(sb.Length > 0 ? ", " : "").Append(names[i]).Append(' ').Append((now - touchedAt[i]).ToString("0.00", CultureInfo.InvariantCulture)).Append(" s ago");
            return sb.Length > 0 ? sb.ToString() : "none in the last " + F(within) + " s";
        }

        // "ski started moving the player" / "stopped": one line each way
        internal static void WatchTouches()
        {
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < used; i++)
            {
                bool m = now - touchedAt[i] < 1f;
                if (m == moving[i]) continue;
                moving[i] = m;
                Event(names[i] + (m ? " started moving the player" : " stopped moving the player"));
            }
        }
    }

    // The checks, the heartbeat and the mark key, on Winter Walk's object
    public class DiagDriver : MonoBehaviour
    {
        public DiagDriver(IntPtr ptr) : base(ptr) { }

        public static bool TestMark;      // for testing without a keyboard: set?path=WinterWalk.DiagDriver.TestMark&value=true

        float nextBeat, windowStart, worstFrame, nextSlowLine, nextCensus, nextSnapshot, toastUntil;
        int slowFrames, slowSuppressed, snapshots, marks;
        string toast = "";
        bool inWalk, onIsle, guiFailed;
        int players = -1, quality = -1;
        string affinity = "", states = "";
        long memStep;
        IntPtr meAt = IntPtr.Zero;
        PlayerCharacter me;
        int jumpId = -2, crouchId = -2;
        KeyCode markKey = KeyCode.F8;
        // the player checks
        float floatFor, airFor, crouchHeld, jumpUntil, renderCheckAt;
        bool floatReported, crouchReported, jumpWatch, jumpSeen, jumpAsleep;

        void Start()
        {
            if (!Enum.TryParse(Diag.MarkKey.Value, true, out markKey)) markKey = KeyCode.F8;
            windowStart = Time.unscaledTime;
            nextBeat = Time.unscaledTime + Diag.HeartbeatSeconds.Value;
            // a normal exit ends the log with "session ended" (OnApplicationQuit alone is not called on this object)
            try { Application.add_quitting(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Diag.End("the game closed normally")))); }
            catch (Exception e) { Diag.Warn("debug log: no quit notice (" + e.Message + ")"); }
            AppDomain.CurrentDomain.ProcessExit += (s, a) => Diag.End("the game closed (the process exited)");
        }

        void OnApplicationQuit() => Diag.End("the game closed normally");

        void Update()
        {
            if (!Diag.On.Value || Diag.FilePath == null) return;
            try
            {
                Diag.Roll();
                float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
                string pending = Diag.Pending;
                if (pending != null) { Diag.Pending = null; if (pending.Contains("DID NOT")) Diag.Warn(pending); else Diag.Event(pending); }

                // slow frames: the frame that just ended, and what of it was ours
                if (dt > worstFrame) worstFrame = dt;
                if (dt * 1000f > Diag.SlowFrameMs.Value && Time.frameCount > 10)
                {
                    slowFrames++;
                    if (now >= nextSlowLine)
                    {
                        nextSlowLine = now + 5f;
                        string ours = Diag.Shares(true, 3, out double total);
                        Diag.Write("[slow] a " + (dt * 1000f).ToString("0", CultureInfo.InvariantCulture) + " ms frame; Winter Walk's part " + total.ToString("0.0", CultureInfo.InvariantCulture) + " ms" + (ours.Length > 0 ? " (" + ours + ")" : "") + (slowSuppressed > 0 ? "; " + slowSuppressed + " more since the last line" : ""));
                        slowSuppressed = 0;
                    }
                    else slowSuppressed++;
                }

                Diag.WatchTouches();
                Player(now, dt);

                if (KeyDown(markKey) || TestMark) { TestMark = false; Mark(); }
                if (now >= nextBeat) Beat(now);
            }
            catch (Exception e)
            {
                if (now0 < Time.unscaledTime) { now0 = Time.unscaledTime + 30f; Diag.Warn("debug log check failed: " + e.GetType().Name + ": " + e.Message); }
            }
        }
        float now0;

        bool KeyDown(KeyCode k)
        {
            try { var kb = Rewired.ReInput.controllers.Keyboard; if (kb != null) return kb.GetKeyDown(k); } catch (Exception) { }
            try { return Input.GetKeyDown(k); } catch (Exception) { return false; }
        }

        PlayerCharacter Me()
        {
            try
            {
                Mirror.NetworkIdentity id = Mirror.NetworkClient.localPlayer;
                if (id == null) { me = null; meAt = IntPtr.Zero; return null; }
                if (id.Pointer != meAt || me == null) { meAt = id.Pointer; me = id.GetComponent<PlayerCharacter>(); }
                return me;
            }
            catch (Exception) { me = null; meAt = IntPtr.Zero; return null; }
        }

        static int Action(string word)
        {
            try
            {
                for (int id = 0; id < 256; id++)
                {
                    Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                    if (a != null && a.name != null && a.name.ToLowerInvariant().Contains(word)) return a.id;
                }
            }
            catch (Exception) { }
            return -1;
        }

        // ---- the player ----

        void Player(float now, float dt)
        {
            PlayerCharacter pc = Me();
            if (pc == null)
            {
                if (inWalk) { inWalk = false; Diag.Event("left the walk"); Diag.HardFlush(); }
                return;
            }
            Vector3 pos = pc.transform.position;
            if (!inWalk)
            {
                inWalk = true;
                bool host = false;
                try { host = Mirror.NetworkServer.active; } catch (Exception) { }
                Diag.Event("in a walk as " + (host ? "host" : "client") + " at " + Diag.V(pos));
                floatFor = airFor = crouchHeld = 0f; jumpWatch = false;
                renderCheckAt = now + 25f;      // once the packs have built
            }
            if (renderCheckAt > 0f && now >= renderCheckAt)
            {
                renderCheckAt = 0f;
                try { Diag.Event(Census.Shaders()); } catch (Exception e) { Diag.Warn("shader check failed: " + e.Message); }
                try { Diag.Event("how this PC renders: " + Census.Pipeline()); quality = QualitySettings.GetQualityLevel(); } catch (Exception e) { Diag.Warn("pipeline check failed: " + e.Message); }
            }
            // the player changing the graphics settings
            if (quality >= 0 && Time.frameCount % 60 == 0)
            {
                int q = QualitySettings.GetQualityLevel();
                if (q != quality) { quality = q; try { Diag.Event("graphics settings changed: " + Census.Pipeline()); } catch (Exception e) { Diag.Warn("graphics settings changed to level " + q + " (" + e.Message + ")"); } }
            }
            bool isle = pos.x > Dresser.X0 && pos.x < Dresser.X1 && pos.z > Dresser.Z0 && pos.z < Dresser.Z1;
            if (isle != onIsle) { onIsle = isle; Diag.Event((isle ? "came onto the Winter Isle at " : "left the Winter Isle at ") + Diag.V(pos)); }
            if (!Diag.PlayerChecks.Value) return;

            Rigidbody rb = pc.rb;
            Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
            PlayerGround ground = pc.ground;
            bool grounded = ground != null && ground.isGrounded, swimming = ground != null && ground.isSwimming;
            bool sitting = pc.sitter != null && pc.sitter.isSittingLocal, asleep = pc.sleeper != null && pc.sleeper.localIsSleeping;
            bool flying = PlaneDriver.Flying || JetpackDriver.Worn;

            // in the air but not falling: held up by something (what "floating above the ground" looks like)
            if (!grounded && !sitting && !swimming && !flying && Mathf.Abs(v.y) < 0.4f) floatFor += dt; else floatFor = 0f;
            if (grounded) floatReported = false;
            if (floatFor > 2f && !floatReported) { floatReported = true; Snapshot("not on the ground and not falling for 2 s"); }
            // a long time off the ground (a fall, a launch, a stuck state): when it ends
            if (!grounded && !sitting && !swimming) airFor += dt;
            else { if (airFor > 4f && !flying) Diag.Event("back on the ground after " + airFor.ToString("0.0", CultureInfo.InvariantCulture) + " s off it, at " + Diag.V(pos)); airFor = 0f; }

            Rewired.Player input = null;
            try { input = pc.inputPlayer; } catch (Exception) { }
            if (input == null) try { input = Rewired.ReInput.players.GetPlayer(0); } catch (Exception) { }
            if (input == null) return;
            if (jumpId == -2) { jumpId = Action("jump"); crouchId = Action("crouch"); Diag.Event("debug log watches jump (Rewired action " + jumpId + ") and crouch (action " + crouchId + ")"); }

            // jump pressed, and no jump (held jumps count: watched until 0.7 s after it is let go)
            if (jumpId >= 0)
            {
                if (input.GetButtonDown(jumpId)) { if (!jumpWatch) { jumpWatch = true; jumpSeen = false; jumpAsleep = asleep; } jumpUntil = now + 0.7f; }
                if (jumpWatch)
                {
                    if (input.GetButton(jumpId)) jumpUntil = Mathf.Max(jumpUntil, now + 0.7f);
                    if (v.y > 1.5f || (pc.jumper != null && pc.jumper.justJumped)) jumpSeen = true;
                    if (now > jumpUntil)
                    {
                        jumpWatch = false;
                        if (!jumpSeen && !flying && !sitting) Snapshot("jump pressed but no jump" + (jumpAsleep ? " (the player was asleep)" : ""));
                    }
                }
            }
            // crouch held, and no crouch
            if (crouchId >= 0)
            {
                bool held = input.GetButton(crouchId);
                crouchHeld = held ? crouchHeld + dt : 0f;
                if (!held) crouchReported = false;
                if (crouchHeld > 0.8f && !crouchReported && pc.croucher != null && pc.croucher.localTrueCrouchness < 0.15f && !sitting)
                { crouchReported = true; Snapshot("crouch held but not crouching"); }
            }
        }

        // Everything about the player's movement: what the game thinks (ground, grip, pose...), what is under them,
        // and whether Winter Walk has been moving them. At most one every 20 s, except a mark.
        void Snapshot(string why, bool mark = false)
        {
            float now = Time.unscaledTime;
            if (!mark && now < nextSnapshot) { snapshots++; return; }
            nextSnapshot = now + 20f;
            var sb = new StringBuilder();
            sb.Append("SNAPSHOT: ").Append(why);
            if (!mark && snapshots > 0) sb.Append(" (").Append(snapshots).Append(" more held back since the last one)");
            snapshots = 0;
            PlayerCharacter pc = Me();
            if (pc == null) { sb.Append("\n  not in a walk"); Diag.Warn(sb.ToString()); Diag.HardFlush(); return; }
            void Part(string label, Func<string> f)
            {
                string said;
                try { said = f(); } catch (Exception e) { said = "(" + e.GetType().Name + ": " + e.Message + ")"; }
                sb.Append("\n  ").Append(label).Append(said);
            }
            Part("", () =>
            {
                Rigidbody rb = pc.rb;
                return "player at " + Diag.V(pc.transform.position) + " moving " + Diag.V(rb.linearVelocity) + "; grounded " + pc.ground.isGrounded + ", jumpable ground " + pc.ground.isOnJumpableGround
                    + ", swimming " + pc.ground.isSwimming + ", sitting " + pc.sitter.isSittingLocal + ", asleep " + pc.sleeper.localIsSleeping + ", crouch " + pc.croucher.localTrueCrouchness.ToString("0.00", CultureInfo.InvariantCulture)
                    + (pc.bypassUpdate || pc.bypassFixedUpdate ? ", UPDATES BYPASSED" : "");
            });
            Part("body: ", () => { Rigidbody rb = pc.rb; return "kinematic " + rb.isKinematic + ", gravity " + rb.useGravity + ", sleeping " + rb.IsSleeping() + ", constraints " + rb.constraints + ", collisions " + rb.detectCollisions + ", drag " + rb.linearDamping.ToString("0.##", CultureInfo.InvariantCulture); });
            Part("ground: ", () =>
            {
                Collider g = pc.ground.groundCollider;
                return (g == null ? "no collider" : Path(g.transform) + " (layer " + g.gameObject.layer + ", material " + (g.sharedMaterial != null ? g.sharedMaterial.name : "none") + ", steep material " + pc.ground.IsSteepMaterial(g) + ")")
                    + "; grip " + pc.ground.GetGroundGrip().ToString("0.00", CultureInfo.InvariantCulture) + ", steepness " + pc.ground.GetSteepness().ToString("0.00", CultureInfo.InvariantCulture) + ", normal " + Diag.V(pc.ground.normal) + ", to terrain " + pc.ground.distanceToTerrain.ToString("0.00", CultureInfo.InvariantCulture) + " m";
            });
            Part("the game's ground layers below: ", () =>
            {
                // (the game casts a sphere; SphereCastAll is stripped from it, so a ray down the same mask and length)
                Transform from = pc.ground.castFrom;
                if (from == null) return "no cast origin";
                var s = new StringBuilder("from " + Diag.V(from.position) + " mask " + pc.ground.layerMask.value + " within " + (pc.ground.castLength + pc.ground.radius).ToString("0.00", CultureInfo.InvariantCulture) + " m: ");
                int n = 0;
                foreach (RaycastHit hit in Physics.RaycastAll(from.position, Vector3.down, pc.ground.castLength + pc.ground.radius, pc.ground.layerMask.value, QueryTriggerInteraction.Ignore))
                    if (hit.collider != null) { n++; s.Append(Path(hit.collider.transform)).Append(" at ").Append(hit.distance.ToString("0.00", CultureInfo.InvariantCulture)).Append("; "); }
                if (n == 0) s.Append("nothing");
                // and the first thing of any kind further down
                if (Physics.Raycast(from.position, Vector3.down, out RaycastHit far, 500f, ~0, QueryTriggerInteraction.Ignore) && far.collider != null)
                    s.Append(" | the first thing below at all: ").Append(Path(far.collider.transform)).Append(" (layer ").Append(far.collider.gameObject.layer).Append(") ").Append(far.distance.ToString("0.0", CultureInfo.InvariantCulture)).Append(" m down");
                else s.Append(" | nothing at all within 500 m below");
                return s.ToString();
            });
            Part("everything 2 m below: ", () =>
            {
                var s = new StringBuilder();
                Transform self = pc.transform.root;
                foreach (RaycastHit hit in Physics.RaycastAll(pc.transform.position + Vector3.up * 0.2f, Vector3.down, 2.2f, ~0, QueryTriggerInteraction.Collide))
                {
                    Collider c = hit.collider;
                    if (c == null || c.transform.root == self) continue;
                    s.Append(Path(c.transform)).Append(" (layer ").Append(c.gameObject.layer).Append(c.isTrigger ? ", trigger" : "").Append(") at ").Append(hit.distance.ToString("0.00", CultureInfo.InvariantCulture)).Append("; ");
                }
                return s.Length > 0 ? s.ToString() : "nothing";
            });
            // From well above (a ray that starts under a surface misses it): what is really under the player, and the
            // island's ground piece there (switched on? its collider on?). A tester glided "off the ground" over island
            // ground that this PC finds solid (2026-11-22); this tells "standing under the surface" from "no ground".
            Part("from 5 m above: ", () =>
            {
                Vector3 at = pc.transform.position;
                var s = new StringBuilder();
                Transform self = pc.transform.root;
                int n = 0;
                foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 5f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || hit.collider.transform.root == self || n++ > 5) continue;
                    s.Append(Path(hit.collider.transform)).Append(" ").Append((hit.point.y - at.y).ToString("+0.00;-0.00", CultureInfo.InvariantCulture)).Append(" m; ");
                }
                if (s.Length == 0) s.Append("nothing within 7 m below the player; ");
                GameObject isle = GameObject.Find("WinterIsle");
                if (isle != null)
                {
                    Transform best = null; float bestD = float.MaxValue;
                    for (int i = 0; i < isle.transform.childCount; i++)
                    {
                        Transform c = isle.transform.GetChild(i);
                        if (!c.name.StartsWith("Island ")) continue;
                        MeshCollider mc = c.GetComponent<MeshCollider>();
                        if (mc == null) continue;
                        Bounds b = mc.bounds;
                        float d = b.SqrDistance(at);
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    if (best != null)
                    {
                        MeshCollider mc = best.GetComponent<MeshCollider>();
                        s.Append("nearest ground piece ").Append(best.name).Append(": active ").Append(best.gameObject.activeInHierarchy)
                         .Append(", collider ").Append(mc.enabled ? "on" : "OFF").Append(", mesh ").Append(mc.sharedMesh != null ? mc.sharedMesh.vertexCount + " vertices" : "NONE")
                         .Append(", ").Append(Mathf.Sqrt(bestD).ToString("0.0", CultureInfo.InvariantCulture)).Append(" m from its box");
                    }
                }
                return s.ToString();
            });
            Part("Winter Walk moving the player: ", () => Diag.Touches(10f));
            Part("Winter Walk: ", () => DriverStates());
            Part("camera: ", () => Camera());
            // every plain value of the movement components (as the companion's "player" lists them)
            object[] parts = { pc.ground, pc.jumper, pc.mover, pc.croucher, pc.sitter, pc.sleeper, pc.faller, pc.platformer, pc.grease, pc.sprinter };
            foreach (object part in parts)
            {
                if (part == null) continue;
                Part(part.GetType().Name + ":", () =>
                {
                    var s = new StringBuilder();
                    foreach (PropertyInfo p in part.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        Type t = p.PropertyType;
                        if (!(t.IsPrimitive || t.IsEnum || t == typeof(Vector3)) || p.Name.StartsWith("log") || p.Name.StartsWith("draw") || p.GetIndexParameters().Length > 0) continue;
                        try { object val = p.GetValue(part); s.Append(' ').Append(p.Name).Append('=').Append(val is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : val is Vector3 vv ? Diag.V(vv) : val); } catch (Exception) { }
                    }
                    return s.ToString();
                });
            }
            Diag.Warn(sb.ToString());
            Diag.HardFlush();
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 7; i++, t = t.parent) parts.Insert(0, t.name);
            if (t != null) parts.Insert(0, "...");
            return string.Join("/", parts);
        }

        static string Camera()
        {
            Camera cam = UnityEngine.Camera.main;
            if (cam == null) return "none";
            Vector3 f = cam.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            return "at " + Diag.V(cam.transform.position) + " facing " + yaw.ToString("0", CultureInfo.InvariantCulture) + " deg (0 = +z, 90 = +x), pitch " + pitch.ToString("0", CultureInfo.InvariantCulture) + ", far clip " + cam.farClipPlane.ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        static string DriverStates()
        {
            return "ski " + SkiDriver.State + (SkiDriver.Gear > 0 ? " (gear " + SkiDriver.Gear + ")" : "")
                + " | plane " + (PlaneDriver.Flying ? "flying" : PlaneDriver.State)
                + " | jetpack " + (JetpackDriver.Worn ? "worn" : "-")
                + " | train " + TrainDriver.State + (TrainDriver.InTunnel ? " (in a tunnel)" : "")
                + " | mine " + (MineDriver.Open ? "open" : "closed") + (MineDriver.Underground ? ", underground" : "")
                + " | gondola " + GondolaDriver.State
                + " | dinosaurs " + DinoDriver.State + (DinoDriver.Last.Length > 0 ? " (last: " + DinoDriver.Last + ")" : "")
                + " | weather " + WeatherDriver.State + (WeatherDriver.Blizzard > 0.01f ? ", blizzard " + WeatherDriver.Blizzard.ToString("0.00", CultureInfo.InvariantCulture) : "")
                + " | snow tracks " + SnowTrails.Tiles + " tiles"
                + " | dressing " + Dresser.Shown + " of " + Dresser.Made + " shown"
                + " | scatter " + Scatter.State
                + " | unstuck " + Unstuck.State
                + " | puzzles " + PuzzleSleep.State;
        }

        // ---- the heartbeat ----

        void Beat(float now)
        {
            nextBeat = now + Mathf.Max(2f, Diag.HeartbeatSeconds.Value);
            float span = Mathf.Max(0.001f, now - windowStart);
            int frames = Diag.WindowFrames;
            float fps = frames / span;
            string ours = Diag.Shares(false, 3, out double total);
            var sb = new StringBuilder("[hb] ");
            sb.Append("fps ").Append(fps.ToString("0", CultureInfo.InvariantCulture)).Append(" (worst frame ").Append((worstFrame * 1000f).ToString("0", CultureInfo.InvariantCulture)).Append(" ms");
            if (slowFrames > 0) sb.Append(", ").Append(slowFrames).Append(" slow");
            sb.Append(") | Winter Walk ").Append(total.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ms/frame");
            if (ours.Length > 0) sb.Append(" (").Append(ours).Append(')');
            long priv = 0;
            try
            {
                using (Process p = Process.GetCurrentProcess()) priv = p.PrivateMemorySize64;
                sb.Append(" | memory ").Append(Gb(priv)).Append(" GB private, ").Append(Gb(Environment.WorkingSet)).Append(" GB in RAM, .NET ").Append(Gb(GC.GetTotalMemory(false)));
                string unity = UnityMemory();
                if (unity.Length > 0) sb.Append(", ").Append(unity);
            }
            catch (Exception) { }
            PlayerCharacter pc = Me();
            if (pc != null)
            {
                try
                {
                    var all = PlayerCharacter.allPlayerCharacters;
                    int n = all != null ? all.Count : 0;
                    bool host = Mirror.NetworkServer.active;
                    sb.Append(" | ").Append(host ? "host" : "client").Append(", ").Append(n).Append(n == 1 ? " player" : " players");
                    if (n != players)
                    {
                        if (players >= 0) Diag.Event("players in the walk: " + n + " (was " + players + ")");
                        players = n;
                    }
                    sb.Append(" | at ").Append(Diag.V(pc.transform.position));
                    if (pc.ground != null) sb.Append(pc.ground.isGrounded ? " grounded" : " OFF THE GROUND");
                    if (pc.sitter != null && pc.sitter.isSittingLocal) sb.Append(" sitting");
                    if (pc.sleeper != null && pc.sleeper.localIsSleeping) sb.Append(" asleep");
                    if (onIsle) sb.Append(" on the isle");
                    Enviro.EnviroManager env = Enviro.EnviroManager.instance;
                    if (env != null && env.Time != null) sb.Append(" | hour ").Append(env.Time.Settings.timeOfDay.ToString("0.0", CultureInfo.InvariantCulture));
                }
                catch (Exception e) { sb.Append(" | (").Append(e.GetType().Name).Append(')'); }
            }
            else { sb.Append(" | not in a walk"); players = -1; }
            Diag.Write(sb.ToString());
            string s = DriverStates();
            if (pc != null && s != states) { states = s; Diag.Write("[states] " + s); }
            string aff = Diag.Affinity();
            if (aff != affinity) { if (affinity.Length > 0) Diag.Event("CPU affinity now " + aff); affinity = aff; }

            // a frame rate that stays low: count what is drawn and where the camera looks (once a session: the count
            // itself takes about a second; F8 counts again whenever wanted)
            if (pc != null && fps < Diag.LowFps.Value && frames > 0 && nextCensus == 0f && Time.frameCount > 600)
            {
                nextCensus = now;
                Diag.Write("[perf] the frame rate is " + fps.ToString("0", CultureInfo.InvariantCulture) + "; camera " + Camera() + "\n" + Census.Drawn());
            }
            // memory past each 2 GB step: the biggest textures and meshes
            long step = priv / (2L << 30);
            if (step > memStep && priv > (4L << 30))
            {
                memStep = step;
                Diag.Warn("memory passed " + (step * 2) + " GB (private bytes)\n" + Census.Memory());
            }
            Diag.HardFlush();
            Diag.ResetWindow();
            windowStart = now; worstFrame = 0f; slowFrames = 0;
        }

        static string Gb(long b) => (b / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture);

        static bool profilerGone;
        static string UnityMemory()
        {
            if (profilerGone) return "";
            try
            {
                return "Unity " + Gb(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()) + " allocated of " + Gb(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()) + " reserved, graphics driver " + Gb(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver());
            }
            catch (Exception) { profilerGone = true; return ""; }
        }

        // ---- the mark key ----

        void Mark()
        {
            marks++;
            string shot = null;
            try
            {
                shot = System.IO.Path.Combine(Diag.Dir, System.IO.Path.GetFileNameWithoutExtension(Diag.FilePath) + "-mark" + marks + ".png");
                ScreenCapture.CaptureScreenshot(shot);
            }
            catch (Exception e) { shot = "(no screenshot: " + e.GetType().Name + ")"; }
            Snapshot("MARK " + marks + " (the player pressed " + markKey + "): " + shot, true);
            try { Diag.Write("[mark " + marks + "] camera " + Camera() + "\n" + Census.Drawn() + "\n" + Census.Memory()); } catch (Exception e) { Diag.Write("[mark " + marks + "] census failed: " + e.Message); }
            Diag.HardFlush();
            toast = "Winter Walk: marked in the debug log (" + marks + ")";
            toastUntil = Time.unscaledTime + 4f;
        }

        void OnGUI()
        {
            if (guiFailed || Time.unscaledTime > toastUntil) return;
            try { GUI.Label(new Rect(20f, 20f, 700f, 30f), toast); }
            catch (Exception) { guiFailed = true; }
        }
    }

    // Counting what the game holds: what is drawn (by owner), and the biggest textures and meshes
    internal static class Census
    {
        // How this PC draws: the quality level and render pipeline, and each of our shaders (BWM/...) in use, whether
        // this GPU runs it and how many renderers wear it. (For "the ground is see-through on my PC".)
        // The quality level in use and what its render pipeline does (depth priming, rendering path, depth and opaque
        // textures, MSAA, HDR, render scale, shadows...) with the renderer's features (SSAO, HBAO...): above "Low"
        // players see through much of the world and it runs slowly, so every log says what this PC was set to
        internal static string Pipeline()
        {
            var sb = new StringBuilder("quality ");
            int q = QualitySettings.GetQualityLevel();
            try { sb.Append(QualitySettings.names[q]); } catch (Exception) { }
            sb.Append(" (level ").Append(q).Append(')');
            UnityEngine.Rendering.RenderPipelineAsset rp = null;
            try { rp = QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline; } catch (Exception) { }
            if (rp == null) return sb.Append(", no render pipeline asset").ToString();
            sb.Append(", pipeline ").Append(rp.name).Append(':');
            var urp = rp.TryCast<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
            if (urp == null) return sb.ToString();
            sb.Append(Plain(urp));
            try
            {
                UnityEngine.Rendering.Universal.ScriptableRendererData data = urp.scriptableRendererData;
                if (data != null)
                {
                    sb.Append("\n  renderer ").Append(data.name).Append(':');
                    var u = data.TryCast<UnityEngine.Rendering.Universal.UniversalRendererData>();
                    sb.Append(Plain(u != null ? (object)u : data));
                    var features = data.rendererFeatures;
                    sb.Append("\n  renderer features:");
                    if (features != null)
                        for (int i = 0; i < features.Count; i++)
                        {
                            var f = features[i];
                            if (f != null) sb.Append(' ').Append(f.name).Append(" (").Append(f.GetIl2CppType().Name).Append(f.isActive ? "" : ", off").Append(");");
                        }
                }
            }
            catch (Exception e) { sb.Append("\n  (renderer: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(')'); }
            // every quality level's renderer, to compare with "Low"
            try
            {
                sb.Append("\n  every level:");
                string[] names = QualitySettings.names;
                for (int i = 0; i < names.Length; i++)
                {
                    var asset = QualitySettings.GetRenderPipelineAssetAt(i)?.TryCast<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
                    var d = asset != null ? asset.scriptableRendererData?.TryCast<UnityEngine.Rendering.Universal.UniversalRendererData>() : null;
                    sb.Append(' ').Append(names[i]).Append(" = ").Append(asset != null ? asset.name : "default");
                    if (d != null) sb.Append(" (").Append(d.renderingMode).Append(", depth priming ").Append(d.depthPrimingMode).Append(", depth texture ").Append(asset.supportsCameraDepthTexture).Append(", shadows ").Append(asset.shadowDistance.ToString("0", CultureInfo.InvariantCulture)).Append(" m)");
                    sb.Append(';');
                }
            }
            catch (Exception e) { sb.Append(" (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(')'); }
            return sb.ToString();
        }

        // " name=value" for every plain value (numbers, switches, choices) of an object, as the game holds them
        static string Plain(object o)
        {
            var sb = new StringBuilder();
            foreach (PropertyInfo p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Type t = p.PropertyType;
                if (!(t.IsPrimitive || t.IsEnum) || p.GetIndexParameters().Length > 0 || p.Name.StartsWith("Native") || p.Name.Contains("Debug")) continue;
                try { object v = p.GetValue(o); sb.Append(' ').Append(p.Name.StartsWith("m_") ? p.Name.Substring(2) : p.Name).Append('=').Append(v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : v); } catch (Exception) { }
            }
            return sb.ToString();
        }

        internal static string Shaders()
        {
            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder("how this PC draws: quality ");
            int q = QualitySettings.GetQualityLevel();
            try { sb.Append(QualitySettings.names[q]); } catch (Exception) { sb.Append(q); }
            try { var rp = QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline; sb.Append(", pipeline ").Append(rp != null ? rp.name : "none"); } catch (Exception) { }
            try { sb.Append(", shader level ").Append(SystemInfo.graphicsShaderLevel).Append(", ").Append(SystemInfo.graphicsDeviceType); } catch (Exception) { }
            var shaders = new Dictionary<string, int[]>();
            var bad = new List<string>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    Shader s = m.shader;
                    string name = s.name;
                    if (!name.StartsWith("BWM/") && !name.Contains("Error") && s.isSupported) continue;
                    if (!shaders.TryGetValue(name, out int[] c))
                    {
                        shaders[name] = c = new int[3];
                        c[1] = s.isSupported ? 1 : 0;
                        c[2] = s.passCount;
                        if (!s.isSupported || name.Contains("Error")) bad.Add(name + " (on " + r.name + ")");
                    }
                    c[0]++;
                }
            }
            sb.Append("; our shaders:");
            foreach (var kv in shaders) sb.Append(' ').Append(kv.Key).Append(kv.Value[1] == 1 ? "" : " NOT SUPPORTED").Append(" (").Append(kv.Value[0]).Append(" renderers, ").Append(kv.Value[2]).Append(" passes);");
            if (bad.Count > 0) sb.Append(" PROBLEMS: ").Append(string.Join(", ", bad));
            sb.Append(" (checked in ").Append(sw.ElapsedMilliseconds).Append(" ms)");
            return sb.ToString();
        }

        // Renderers Unity counts as visible (seen by a camera or a shadow last frame), grouped by their top object and
        // by the one under it, with how many are in front of the camera and how far away they are: for "it is slow
        // looking at the mountain", whether the cost is far things that fog could hide
        internal static string Drawn()
        {
            var sw = Stopwatch.StartNew();
            var roots = new Dictionary<string, int[]>();
            var subs = new Dictionary<string, int[]>();
            int total = 0, visible = 0;
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero, fwd = cam != null ? cam.transform.forward : Vector3.forward;
            float cone = 0f;
            if (cam != null)
            {
                float v = cam.fieldOfView, h = 2f * Mathf.Atan(Mathf.Tan(v * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
                cone = Mathf.Cos(Mathf.Min(89f, Mathf.Max(v, h) * 0.5f + 5f) * Mathf.Deg2Rad);
            }
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                total++;
                if (!r.isVisible) continue;
                visible++;
                Transform t = r.transform, below = null;
                while (t.parent != null) { below = t; t = t.parent; }
                string root = t.name, sub = root + "/" + (below != null ? below.name : "-");
                Bounds b = r.bounds;
                Vector3 d = b.center - eye;
                float dist = d.magnitude, reach = b.extents.magnitude;
                bool front = dist < reach || Vector3.Dot(d / Mathf.Max(dist, 0.001f), fwd) > cone;
                // [0] visible [1] in front [2] <100 m [3] <300 m [4] <1000 m [5] further
                int band = dist < 100f ? 2 : dist < 300f ? 3 : dist < 1000f ? 4 : 5;
                if (!roots.TryGetValue(root, out int[] a)) roots[root] = a = new int[6];
                a[0]++; if (front) a[1]++; a[band]++;
                if (!subs.TryGetValue(sub, out int[] c)) subs[sub] = c = new int[6];
                c[0]++; if (front) c[1]++; c[band]++;
            }
            var sb = new StringBuilder("  drawn: " + visible + " of " + total + " renderers count as visible (counted in " + sw.ElapsedMilliseconds + " ms); by top object: visible, in front of the camera, and by distance <100 m / <300 / <1000 / further:");
            Top(sb, roots, 10);
            sb.Append("\n  by the object under it:");
            Top(sb, subs, 12);
            return sb.ToString();
        }

        static void Top(StringBuilder sb, Dictionary<string, int[]> d, int n)
        {
            var list = new List<KeyValuePair<string, int[]>>(d);
            list.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            for (int i = 0; i < list.Count && i < n; i++)
            {
                int[] v = list[i].Value;
                sb.Append("\n    ").Append(v[0]).Append(", in front ").Append(v[1]).Append(", ").Append(v[2]).Append(" / ").Append(v[3]).Append(" / ").Append(v[4]).Append(" / ").Append(v[5]).Append("  ").Append(list[i].Key);
            }
        }

        // Textures and meshes in memory, the biggest of each (sizes estimated from their dimensions and format)
        internal static string Memory()
        {
            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            var tex = new List<KeyValuePair<string, long>>();
            long texTotal = 0; int texCount = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture>()))
            {
                Texture t = o.TryCast<Texture>();
                if (t == null) continue;
                long b = TextureBytes(t);
                texTotal += b; texCount++;
                tex.Add(new KeyValuePair<string, long>(t.name + " " + t.width + "x" + t.height + " " + Kind(t), b));
            }
            tex.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append("  textures: ").Append(texCount).Append(", about ").Append((texTotal >> 20)).Append(" MB; the biggest:");
            for (int i = 0; i < tex.Count && i < 10; i++) sb.Append("\n    ").Append(tex[i].Value >> 20).Append(" MB ").Append(tex[i].Key);
            var mesh = new List<KeyValuePair<string, long>>();
            long vertTotal = 0; int meshCount = 0, readable = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Mesh>()))
            {
                Mesh m = o.TryCast<Mesh>();
                if (m == null) continue;
                int v = m.vertexCount;
                vertTotal += v; meshCount++;
                bool r = false;
                try { r = m.isReadable; } catch (Exception) { }
                if (r) readable++;
                mesh.Add(new KeyValuePair<string, long>(m.name + (r ? " (readable)" : ""), v));
            }
            mesh.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append("\n  meshes: ").Append(meshCount).Append(" (").Append(readable).Append(" readable, which keep a second copy), ").Append(vertTotal / 1000).Append("k vertices; the biggest:");
            for (int i = 0; i < mesh.Count && i < 10; i++) sb.Append("\n    ").Append(mesh[i].Value / 1000).Append("k vertices ").Append(mesh[i].Key);
            try
            {
                int objects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>()).Length;
                int materials = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()).Length;
                sb.Append("\n  objects: ").Append(objects).Append(" game objects, ").Append(materials).Append(" materials; the island's dressing ").Append(Dresser.Count).Append(" copies, snow tracks ").Append(SnowTrails.Tiles).Append(" tiles");
            }
            catch (Exception) { }
            sb.Append(" (counted in ").Append(sw.ElapsedMilliseconds).Append(" ms)");
            return sb.ToString();
        }

        static string Kind(Texture t)
        {
            Texture2D t2 = t.TryCast<Texture2D>();
            if (t2 != null) return t2.format.ToString();
            RenderTexture rt = t.TryCast<RenderTexture>();
            if (rt != null) return "render texture " + rt.format;
            return t.GetIl2CppType().Name;
        }

        static long TextureBytes(Texture t)
        {
            try
            {
                long px = (long)t.width * t.height;
                Texture2D t2 = t.TryCast<Texture2D>();
                if (t2 != null) return (long)(px * Bpp(t2.format.ToString()) * (t2.mipmapCount > 1 ? 1.33 : 1.0));
                RenderTexture rt = t.TryCast<RenderTexture>();
                if (rt != null) return (long)(px * (Bpp(rt.format.ToString()) + (rt.depth > 0 ? 4 : 0)) * Math.Max(1, rt.volumeDepth) * Math.Max(1, rt.antiAliasing));
                Texture2DArray ta = t.TryCast<Texture2DArray>();
                if (ta != null) return (long)(px * ta.depth * Bpp(ta.format.ToString()) * 1.33);
                Cubemap cm = t.TryCast<Cubemap>();
                if (cm != null) return (long)(px * 6 * Bpp(cm.format.ToString()) * 1.33);
                return px * 4;
            }
            catch (Exception) { return 0; }
        }

        static double Bpp(string f)
        {
            if (f.StartsWith("DXT1") || f.StartsWith("BC4") || f.StartsWith("ETC_") || f.StartsWith("ETC2_RGB") || f.Contains("PVRTC") || f == "ETC2_RGBA1") return 0.5;
            if (f.StartsWith("DXT5") || f.StartsWith("BC5") || f.StartsWith("BC6") || f.StartsWith("BC7") || f.StartsWith("ASTC") || f.StartsWith("ETC2_RGBA8")) return 1;
            switch (f)
            {
                case "Alpha8": case "R8": case "R8_SInt": case "R8_UInt": return 1;
                case "R16": case "RHalf": case "RG16": case "RGB565": case "RGBA4444": case "ARGB4444": case "R16_UInt": return 2;
                case "RGB24": return 3;
                case "RGBAHalf": case "ARGBHalf": case "RGFloat": case "RG32": return 8;
                case "RGBAFloat": case "ARGBFloat": return 16;
                case "RFloat": case "RGHalf": return 4;
                default: return 4;
            }
        }
    }
}
