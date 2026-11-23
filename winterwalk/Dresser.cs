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

        // ---- the debug log's check of the copies (players report rocks floating round the island) ----
        // Five seconds after dressing, a few milliseconds a frame: every copy against what is under it (its lowest
        // point, or a tree's foot, more than 0.6 m above anything that is not itself) and against where it was put
        // (moved more than half a metre: something physical in it). The worst go to the debug log with places.
        readonly List<int> placedEntry = new List<int>();      // the entry each child of the holder was copied from
        int checkAt = -1, floating, moved, bodies, bare, buried, inTunnel, bareStanding;
        float checkFrom;
        readonly List<KeyValuePair<float, string>> worstFloat = new List<KeyValuePair<float, string>>(), worstMoved = new List<KeyValuePair<float, string>>(),
            worstBuried = new List<KeyValuePair<float, string>>(), worstBare = new List<KeyValuePair<float, string>>(), worstTunnel = new List<KeyValuePair<float, string>>();

        static void Keep(List<KeyValuePair<float, string>> list, float score, Func<string> say)
        {
            if (list.Count >= 8 && score <= list[list.Count - 1].Key) return;
            list.Add(new KeyValuePair<float, string>(score, say()));
            list.Sort((a, b) => b.Key.CompareTo(a.Key));
            if (list.Count > 8) list.RemoveAt(list.Count - 1);
        }

        // ---- what is drawn, by distance ----
        // The copies cost about 6 ms a frame with all of them on (2026-11-22: 84 fps at the east lowland on this PC,
        // 163 without them; the base game draws its trees through its terrains, far cheaper a tree). So they are
        // grouped in 64 m cells, each in three tiers: tier 0 within 600 m (big rocks, one tree in ten: the groves
        // and cliffs keep their outline from across the island), tier 1 within 500 m (middling rocks, two more trees
        // in ten), tier 2 within 200 m (the rest: small rocks, bushes, most trees). The cells are switched a few a
        // frame as the camera moves; the island's fog hides most of the thinning.
        const float CellSize = 64f;
        public static float NearFar = 600f, NearMid = 500f, NearAll = 200f;      // live settings (the companion can set them: set?path=WinterWalk.Dresser.NearAll&value=200)
        sealed class Cell { public Vector2 centre; public readonly GameObject[] tier = new GameObject[3]; public readonly bool[] on = { true, true, true }; public readonly int[] count = new int[3]; }
        readonly Dictionary<long, Cell> cellAt = new Dictionary<long, Cell>();
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Transform> copies = new List<Transform>();      // each made copy, beside placedEntry
        bool culling;
        int cullCursor;
        public static int Shown, Made;      // copies switched on now, and in all (for the debug log)
        public static string Tiers = "";

        // Small copies (bushes, logs, stones under 4 m) cast no shadow: above the game's Low setting every shadow caster in
        // reach is culled and drawn again for each of four cascades, and theirs are hardly seen (live: SmallShadows)
        public static bool SmallShadows;
        bool smallShadowsApplied;
        readonly List<bool> small = new List<bool>();      // beside copies

        static float SizeOf(GameObject copy)      // the mesh's size: an inactive renderer has no bounds
        {
            MeshFilter mf = copy.GetComponentInChildren<MeshFilter>(true);
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.magnitude * Mathf.Abs(mf.transform.lossyScale.x) : 1f;
        }
        static bool IsSmall(Entry e, float size) => e.kind.StartsWith("fir:") ? false : e.kind.StartsWith("tree:") ? size < 3f : size < 4f;

        // For measuring (the companion: set?path=WinterWalk.Dresser.TestHide&value=1): 1 hides the firs, 2 the game's
        // plants and logs, 3 the rocks; 0 shows them all again
        public static int TestHide;
        int testHidden;
        static readonly string[] hideKinds = { "", "fir:", "tree:", "rock" };
        void ApplyTestHide()
        {
            for (int i = 0; i < copies.Count && i < placedEntry.Count; i++)
            {
                if (copies[i] == null) continue;
                string kind = entries[placedEntry[i]].kind;
                bool hide = TestHide > 0 && TestHide < 4 && kind.StartsWith(hideKinds[TestHide]);
                bool was = testHidden > 0 && testHidden < 4 && kind.StartsWith(hideKinds[testHidden]);
                if (hide != was) copies[i].gameObject.SetActive(!hide);
            }
            testHidden = TestHide;
        }

        void ApplySmallShadows(bool on)
        {
            var mode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < copies.Count; i++)
            {
                if (!small[i] || copies[i] == null) continue;
                foreach (Component c in copies[i].GetComponentsInChildren(Il2CppType.Of<Renderer>(), true))
                {
                    Renderer r = c.TryCast<Renderer>();
                    if (r != null && !r.name.Contains("Impostor")) r.shadowCastingMode = mode;
                }
            }
            smallShadowsApplied = on;
        }

        // Which tier a copy goes in, from its kind and size
        static int TierOf(Entry e, int index, float size)
        {
            if (e.kind.StartsWith("tree:") || e.kind.StartsWith("fir:"))
            {
                if (size < 3f) return 2;
                uint h = (uint)index * 2654435761u;
                int d = (int)((h >> 16) % 10);
                return d == 0 ? 0 : d <= 2 ? 1 : 2;
            }
            return size >= 12f ? 0 : size >= 4f ? 1 : 2;
        }

        Transform GroupFor(Entry e, int tier)
        {
            int cx = (int)Math.Floor(e.at.x / CellSize), cz = (int)Math.Floor(e.at.z / CellSize);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (!cellAt.TryGetValue(key, out Cell c))
            {
                c = new Cell { centre = new Vector2((cx + 0.5f) * CellSize, (cz + 0.5f) * CellSize) };
                cellAt[key] = c; cells.Add(c);
            }
            if (c.tier[tier] == null)
            {
                var g = new GameObject("cell " + cx + " " + cz + " tier " + tier);
                g.SetActive(false);      // switched on once every copy is made, a few groups a frame (Wake)
                g.transform.SetParent(holder.transform, false);
                c.tier[tier] = g; c.on[tier] = false;
            }
            c.count[tier]++;
            return c.tier[tier].transform;
        }

        // ---- copies from prototypes ----
        // Each kind of copy is made once (per snow line, and a shadowless twin for small ones) and cleaned: the parts
        // that would save, move or hide it taken out, lit by the sky, its snow material on. Every place in the list is
        // then a plain copy of that. Copying the game's own object and cleaning each copy cost about 70 us a copy, 6.5 s
        // of the main thread over the island (2026-11-23).
        sealed class Proto { public GameObject go, shadowless; public float size; }
        readonly Dictionary<string, Proto> protos = new Dictionary<string, Proto>();
        Transform protoRoot;      // under the holder, switched off: the prototypes never wake

        // After the copies are made, their cell groups are switched on in about ten steps, nearest the camera first: all at
        // once (the holder switched on) was a 0.7 s frame. Not in smaller steps: some seconds after a dressing, physics
        // queries find nothing anywhere for a while (rays, the game's ground check: the debug log's "not on the ground"
        // snapshots; it happened with the old dressing too), and switching on 4 ms a frame over 130 frames made that 1.3 to
        // 5.4 s where all at once and 60 ms steps made it 1.5 to 1.9 s (2026-11-23, five and three re-dresses).
        readonly List<KeyValuePair<float, int>> wakeOrder = new List<KeyValuePair<float, int>>();      // distance², cell * 3 + tier
        int wakeAt = -1;
        public static float WakeMs = 60f;      // live: milliseconds of switching on a frame (a large value switches all at once)
        public static float CheckDelay = 5f;      // live: seconds from the end of dressing to the debug log's check (and culling)

        // how the dressing went, for its log line
        double setupMs, copyMs, wakeMs;
        int copyFrames, wakeFrames;
        static double MsSince(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        Proto MakeProto(Entry e, GameObject template, string kind, bool tree, bool fir)
        {
            GameObject copy = Instantiate((UnityEngine.Object)template, protoRoot).Cast<GameObject>();
            copy.name = kind;
            var doomed = new List<Component>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
                if (c != null && Removed.Contains(c.GetIl2CppType().Name)) doomed.Add(c);
            foreach (Component c in doomed) DestroyImmediate(c);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            // lit by the sky, not the base island's far-off light probes (see PuzzleSleep.SkyLit)
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true))
            {
                Renderer r = c.TryCast<Renderer>();
                if (r == null) continue;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            if (fir) copy.SetActive(true);      // our own firs: snow is in their shader
            else
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
            return new Proto { go = copy, size = SizeOf(copy) };
        }

        GameObject Shadowless(GameObject go)
        {
            GameObject twin = Instantiate((UnityEngine.Object)go, protoRoot).Cast<GameObject>();
            twin.name = go.name;
            foreach (Component c in twin.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true))
            {
                Renderer r = c.TryCast<Renderer>();
                if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return twin;
        }

        // A child of the island's root (the parent of its "Material Store", WinterIslePuzzle.cs), or null
        internal static Transform IslandChild(string name)
        {
            GameObject store = GameObject.Find("Material Store");
            Transform root = store != null ? store.transform.parent : null;
            return root != null ? root.Find(name) : null;
        }

        // The rocks' snow material: the one the island's "Material Store" carries
        static Material StoreMaterial()
        {
            GameObject store = GameObject.Find("Material Store");
            MeshRenderer r = store != null ? store.GetComponent<MeshRenderer>() : null;
            Material m = r != null ? r.sharedMaterial : null;
            return m != null && m.name.StartsWith("SnowOverlay", StringComparison.Ordinal) ? m : null;
        }

        void StartWaking()
        {
            wakeOrder.Clear();
            float ex = 0f, ez = 0f;
            Camera cam = Camera.main;
            if (cam != null) { Vector3 p = cam.transform.position; ex = p.x; ez = p.z; }
            for (int i = 0; i < cells.Count; i++)
            {
                float dx = cells[i].centre.x - ex, dz = cells[i].centre.y - ez;
                for (int t = 0; t < 3; t++)
                    if (cells[i].tier[t] != null) wakeOrder.Add(new KeyValuePair<float, int>(dx * dx + dz * dz, i * 3 + t));
            }
            wakeOrder.Sort((a, b) => a.Key.CompareTo(b.Key));
            wakeAt = 0;
        }

        void Wake()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float budget = Time.realtimeSinceStartup + WakeMs / 1000f;
            while (wakeAt < wakeOrder.Count)
            {
                int k = wakeOrder[wakeAt++].Value;
                Cell c = cells[k / 3];
                int t = k % 3;
                c.tier[t].SetActive(true);
                c.on[t] = true;
                Shown += c.count[t];
                if (Time.realtimeSinceStartup >= budget) break;
            }
            wakeMs += MsSince(t0); wakeFrames++;
            if (wakeAt < wakeOrder.Count) return;
            wakeAt = -1;
            // the debug log's check wants every copy there (rocks on rocks); drawing by distance starts after it. Not later
            // than 5 s: some seconds after a dressing every physics query starts finding nothing (rays, the game's ground
            // check), and that lasts until colliders change again, which the culling's first pass does; with the check at
            // 60 s it lasted 54 s (2026-11-23). So the check can fall in it (thousands of copies "over nothing").
            if (Diag.On.Value) { checkAt = 0; checkFrom = Time.unscaledTime + CheckDelay; floating = moved = bodies = bare = buried = inTunnel = bareStanding = 0; worstFloat.Clear(); worstMoved.Clear(); worstBuried.Clear(); worstBare.Clear(); worstTunnel.Clear(); }
            else StartCulling();
            string Ms(double v) => v.ToString("0", CultureInfo.InvariantCulture);
            Plugin.L.LogInfo("Winter Walk: dressed the island with " + made + " copies (" + missing + " missing) in "
                + (Time.realtimeSinceStartup - started).ToString("0.0", CultureInfo.InvariantCulture) + " s: set-up " + Ms(setupMs) + " ms, copying "
                + Ms(copyMs) + " ms over " + copyFrames + " frames from " + protos.Count + " prototypes, switching on " + Ms(wakeMs) + " ms over " + wakeFrames + " frames; "
                + rocks.Count + " rock kinds, " + trees.Count + " tree kinds and " + firs.Count + " fir models to copy from");
        }

        // A few cells a frame: each tier on or off by the camera's distance to the cell (to its near edge, roughly)
        void Cull()
        {
            Camera cam = Camera.main;
            if (cam == null || cells.Count == 0) return;
            Vector3 p = cam.transform.position;
            int toggles = 0;
            for (int k = 0; k < 240 && toggles < 12; k++)
            {
                Cell c = cells[cullCursor];
                cullCursor = (cullCursor + 1) % cells.Count;
                // (plain arithmetic: Vector2.Distance is a call into the game, 240 of them a frame)
                float ex = p.x - c.centre.x, ez = p.z - c.centre.y;
                float d = (float)Math.Sqrt(ex * ex + ez * ez) - CellSize * 0.7f;
                for (int t = 0; t < 3; t++)
                {
                    bool want = d < (t == 0 ? NearFar : t == 1 ? NearMid : NearAll);
                    if (want == c.on[t] || c.tier[t] == null) continue;
                    c.on[t] = want;
                    c.tier[t].SetActive(want);
                    Shown += want ? c.count[t] : -c.count[t];
                    toggles++;
                }
            }
        }

        void StartCulling()
        {
            culling = true; cullCursor = 0;
            int[] per = new int[3];
            foreach (Cell c in cells) for (int t = 0; t < 3; t++) per[t] += c.count[t];
            Tiers = per[0] + " within " + NearFar + " m, " + per[1] + " within " + NearMid + " m, " + per[2] + " within " + NearAll + " m, in " + cells.Count + " cells";
            Plugin.L.LogInfo("Winter Walk: dressing drawn by distance: " + Tiers);
        }

        void CheckStep()
        {
            if (holder == null) { checkAt = -1; return; }
            Transform h = holder.transform;
            int n = Mathf.Min(copies.Count, placedEntry.Count);
            float budget = Time.realtimeSinceStartup + 0.004f;
            while (checkAt < n && Time.realtimeSinceStartup < budget)
            {
                Transform c = copies[checkAt];
                Entry e = entries[placedEntry[checkAt]];
                checkAt++;
                if (c == null) continue;
                Vector3 want = h.TransformPoint(e.at), at = c.position;
                float off = (at - want).magnitude;
                if (off > 0.5f) { moved++; Keep(worstMoved, off, () => e.kind + " " + off.ToString("0.0", CultureInfo.InvariantCulture) + " m from " + Diag.V(want) + " to " + Diag.V(at)); }
                bool standing = e.kind.StartsWith("tree:") || e.kind.StartsWith("fir:");
                if (!standing && c.GetComponentInChildren<Rigidbody>(true) != null) bodies++;
                float bottom, top;
                if (standing) { bottom = at.y; top = at.y + 2f; }
                else
                {
                    Renderer r = c.GetComponentInChildren<Renderer>();
                    if (r == null) continue;
                    Bounds b = r.bounds;
                    bottom = b.min.y; top = b.max.y + 0.5f;
                    // poking into a rail tunnel or the mine ("jagged ground in the tunnels"): its box, shrunk to
                    // stay inside a roundish rock, against the tunnels' walls
                    foreach (Collider wall in Physics.OverlapBox(b.center, b.extents * 0.7f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (wall == null) continue;
                        string wn = wall.name;
                        if (wn != "Rail Tunnels" && wn != "Rail Portals" && !wn.StartsWith("Mine Walls") && wn != "Mine Sheds" && wn != "Mine Portals") continue;
                        inTunnel++;
                        float size = b.extents.magnitude * 2f;
                        Keep(worstTunnel, size, () => e.kind + " (" + size.ToString("0.0", CultureInfo.InvariantCulture) + " m) at " + Diag.V(at) + " through '" + wn + "'");
                        break;
                    }
                }
                // down its middle from well above (a ray started under the ground's surface would miss it: surfaces
                // only stop rays from their outside): the highest thing below its top that is not itself, and
                // whether anything other than itself lies above its top (buried in the ground, or under something)
                float ground = float.NaN, cover = float.NaN; string under = null, over = null;
                int hits = 0, self = 0;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(at.x, top + 60f, at.z), Vector3.down, top - bottom + 180f, ~0, QueryTriggerInteraction.Ignore))
                {
                    hits++;
                    if (hit.collider == null || hit.collider.transform.IsChildOf(c)) { self++; continue; }
                    float y = hit.point.y;
                    if (y > top) { if (float.IsNaN(cover) || y < cover) { cover = y; over = hit.collider.name; } }
                    else if (float.IsNaN(ground) || y > ground) { ground = y; under = hit.collider.name; }
                }
                if (!float.IsNaN(cover) && cover - top < 30f && !standing)
                {
                    buried++;
                    float depth = cover - top;
                    Keep(worstBuried, depth, () => e.kind + " at " + Diag.V(at) + " whole " + depth.ToString("0.0", CultureInfo.InvariantCulture) + " m under '" + over + "'");
                    continue;
                }
                if (float.IsNaN(ground))
                {
                    bare++;
                    if (standing) bareStanding++;
                    float y0 = top + 60f, y1 = bottom - 120f;
                    // (the first few: a falling score keeps the earliest)
                    Keep(worstBare, -bare, () => e.kind + " at " + Diag.V(at) + " (ray from " + y0.ToString("0.0", CultureInfo.InvariantCulture) + " to " + y1.ToString("0.0", CultureInfo.InvariantCulture) + ": " + hits + " hits, " + self + " on itself)");
                    continue;
                }
                float gap = bottom - ground;
                if (gap > 0.6f) { floating++; Keep(worstFloat, gap, () => e.kind + " at " + Diag.V(at) + " " + gap.ToString("0.0", CultureInfo.InvariantCulture) + " m above '" + under + "'"); }
            }
            if (checkAt < n) return;
            checkAt = -1;
            string msg = "dressing check: " + n + " copies; " + floating + " float 0.6 m or more above what is under them, " + buried + " rocks lie wholly under the ground or something else, " + inTunnel + " rocks reach into a rail tunnel or the mine, " + moved + " moved since placed, " + bodies + " rocks carry a physics body, " + bare + " have nothing under them (" + bareStanding + " of them trees or firs)";
            // (normal checks find 1,100 to 1,800 over nothing; one made while physics queries find nothing, which happens
            // some seconds after a dressing, finds 10,000 to 76,000: see CheckDelay)
            if (bare > 5000) msg += "\n  NOT TRUSTWORTHY: physics queries were finding nothing during the check (they do for a while some seconds after a dressing), so these counts are wrong";
            if (worstTunnel.Count > 0) msg += "\n  into tunnels, biggest: " + string.Join("; ", worstTunnel.ConvertAll(kv => kv.Value));
            if (worstFloat.Count > 0) msg += "\n  floating worst: " + string.Join("; ", worstFloat.ConvertAll(kv => kv.Value));
            if (worstBuried.Count > 0) msg += "\n  buried deepest: " + string.Join("; ", worstBuried.ConvertAll(kv => kv.Value));
            if (worstMoved.Count > 0) msg += "\n  moved worst: " + string.Join("; ", worstMoved.ConvertAll(kv => kv.Value));
            if (worstBare.Count > 0) msg += "\n  nothing under (some): " + string.Join("; ", worstBare.ConvertAll(kv => kv.Value));
            if (floating + moved + bodies + inTunnel > 0) Diag.Warn(msg); else Diag.Event(msg);
            StartCulling();
        }

        static float N(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // The list's bytes are read on a thread, started from Plugin.Load: read by the first frame, from a hard disk
        // Windows had not cached, it held the game for 2 s while the game was loading its own data (a players' install
        // in a USB drive's Steam library, 2026-11-23). Parsed on the main thread once read (a tenth of a second).
        static System.Threading.Tasks.Task<byte[]> reading;
        static DateTime readingTime;

        static void StartReading(DateTime time)
        {
            string path = file;
            readingTime = time;
            reading = System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(path));
        }

        internal static void Prefetch()
        {
            try
            {
                file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.dressing");
                if (File.Exists(file)) StartReading(File.GetLastWriteTimeUtc(file));
            }
            catch (Exception) { reading = null; }
        }

        static void Load(byte[] bytes)
        {
            var list = new List<Entry>();
            using var text = new StreamReader(new MemoryStream(bytes));
            for (string raw; (raw = text.ReadLine()) != null;)
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
            // our firs ride in the island's pack beside its Material Store, switched off: found there rather than by going
            // through every object in the world
            Transform firModels = IslandChild("Fir Models");
            if (firModels != null)
                for (int i = 0; i < firModels.childCount; i++)
                {
                    Transform t = firModels.GetChild(i);
                    if (t.name.StartsWith("Fir Model ")) firs[t.name.Substring(10)] = t.gameObject;
                }
            else
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
            m.SetFloat("_SnowLine", key / 20f + Plugin.RockSnowRaise);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_SnowEdge", 0.1f);
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
            // one prototype per kind and snow line (RockSnow and LeafSnow round the line to twentieths)
            bool snowy = tree ? e.snow > -5f : !fir && e.snow >= -5f;
            string key = snowy ? e.kind + "/" + (int)Math.Round(e.snow * 20f) : e.kind;
            if (!protos.TryGetValue(key, out Proto p))
            {
                GameObject template = null;
                if (tree) trees.TryGetValue(kind, out template); else if (fir) firs.TryGetValue(kind, out template); else rocks.TryGetValue(kind, out template);
                p = template != null ? MakeProto(e, template, kind, tree, fir) : null;
                protos[key] = p;
            }
            if (p == null)
            {
                missing++;
                if (warned.Add(e.kind)) Plugin.L.LogWarning("Winter Walk: no '" + e.kind + "' in this world to copy");
                return;
            }
            float size = p.size * e.scale;
            bool isSmall = IsSmall(e, size);
            GameObject from = p.go;
            if (isSmall && !SmallShadows) { if (p.shadowless == null) p.shadowless = Shadowless(p.go); from = p.shadowless; }
            // its cell and tier (all at the origin, like the holder), switched off until every copy is made
            GameObject copy = Instantiate((UnityEngine.Object)from, GroupFor(e, TierOf(e, made, size))).Cast<GameObject>();
            copy.name = kind;
            Transform t = copy.transform;
            t.localPosition = e.at;
            t.localRotation = e.turn;
            Vector3 s = default; s.x = e.scale; s.y = e.scale; s.z = e.scale;      // (fields: Vector3's own methods are calls into the game)
            t.localScale = s;
            copies.Add(t);
            small.Add(isSmall);
            made++;
        }
        void Update()
        {
            using var diag = Diag.Time("dresser");
            try
            {
                if (Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + 1f;
                    if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.dressing");
                    if (File.Exists(file))
                    {
                        DateTime t = File.GetLastWriteTimeUtc(file);
                        if (t != fileTime)
                        {
                            if (reading == null || readingTime != t) StartReading(t);
                            if (reading.IsCompleted)
                            {
                                var done = reading;
                                reading = null;
                                fileTime = t;
                                Load(done.GetAwaiter().GetResult());
                                dirty = true;
                            }
                        }
                    }
                    // Dress when the island is here (its pack carries our material) and not yet dressed
                    bool due = next < 0 && wakeAt < 0 && (holder == null || dirty) && entries.Count > 0;
                    if (due && !Setup.Turn()) nextCheck = 0f;      // another driver set up this frame: try the next
                    else if (due && GameObject.Find("Material Store") != null)
                    {
                        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        overlay = StoreMaterial() ?? FindMaterial("SnowOverlay");
                        setupMs = MsSince(t0);
                        if (overlay != null) Begin();
                    }
                }
                if (next >= 0) Step();
                else if (wakeAt >= 0) Wake();
                else if (checkAt >= 0) { if (Time.unscaledTime >= checkFrom) CheckStep(); }
                else if (culling) { Cull(); if (SmallShadows != smallShadowsApplied) ApplySmallShadows(SmallShadows); if (TestHide != testHidden) ApplyTestHide(); }
            }
            catch (Exception ex)
            {
                Plugin.L.LogWarning("Winter Walk: dressing stopped: " + ex.GetType().Name + ": " + ex.Message);
                next = -1;
                wakeAt = -1;
                dirty = false;
            }
            Busy = next >= 0 || wakeAt >= 0;
        }

        // Copying or switching copies on (the debug log holds its counts back meanwhile)
        public static bool Busy;

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
            // the game's terrain layers: heath, woodland litter, gravel and dry sand, each with its normal map and size
            TerrainLayer heathLayer = null, litterLayer = null, gravelLayer = null, sandLayer = null;
            foreach (Terrain terrain in FindObjectsOfType<Terrain>())
            {
                foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                {
                    if (layer == null || layer.diffuseTexture == null) continue;
                    if (layer.name.Contains("Heathland") && heathLayer == null) heathLayer = layer;
                    if (layer.name.Contains("Woodland") && litterLayer == null) litterLayer = layer;
                    if (layer.name.Contains("Gravel") && gravelLayer == null) gravelLayer = layer;
                    if (layer.name.Contains("SandDry") && sandLayer == null) sandLayer = layer;
                }
                if (heathLayer != null && litterLayer != null && gravelLayer != null && sandLayer != null) break;
            }
            if (heathLayer != null) heath = heathLayer.diffuseTexture;
            if (sandLayer != null) sand = sandLayer.diffuseTexture;
            // (the game's terrain shader sets its own sizes: some layers say 500 or 700 m, so anything that large is taken
            // to be a ground texture of the usual few metres)
            float Tiling(TerrainLayer layer) => layer != null && layer.tileSize.x > 0.01f && layer.tileSize.x < 20f ? 1f / layer.tileSize.x : 0.36f;
            int lent = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m == null || m.shader == null || m.shader.name != "BWM/Snow") continue;
                if (granite != null) m.SetTexture("_RockTex", granite);
                if (heath != null) m.SetTexture("_BareTex", heath);
                if (sand != null) m.SetTexture("_SandTex", sand);
                bool all = heathLayer != null && litterLayer != null && gravelLayer != null && sandLayer != null;
                if (all)
                {
                    m.SetTexture("_BareNormal", heathLayer.normalMapTexture);
                    m.SetTexture("_LitterTex", litterLayer.diffuseTexture);
                    m.SetTexture("_LitterNormal", litterLayer.normalMapTexture);
                    m.SetTexture("_GravelTex", gravelLayer.diffuseTexture);
                    m.SetTexture("_GravelNormal", gravelLayer.normalMapTexture);
                    m.SetTexture("_SandNormal", sandLayer.normalMapTexture);
                    m.SetFloat("_BareTiling", Tiling(heathLayer));
                    m.SetFloat("_LitterTiling", Tiling(litterLayer));
                    m.SetFloat("_GravelTiling", Tiling(gravelLayer));
                    m.SetFloat("_SandTiling", Tiling(sandLayer));
                    m.SetFloat("_GameGround", 1f);
                }
                lent++;
            }
            Plugin.L.LogInfo("Winter Walk: lent the snow " + (granite != null ? "granite " : "") + (heath != null ? "heath " : "") + (sand != null ? "sand " : "") + "to " + lent + " material(s)"
                + "; the game's ground layers: heath " + (heathLayer != null ? heathLayer.tileSize.x.ToString("0.#") + " m" : "none") + ", litter " + (litterLayer != null ? litterLayer.tileSize.x.ToString("0.#") + " m" : "none")
                + ", gravel " + (gravelLayer != null ? gravelLayer.tileSize.x.ToString("0.#") + " m" : "none") + ", sand " + (sandLayer != null ? sandLayer.tileSize.x.ToString("0.#") + " m" : "none"));
        }

        // Our trees, roofs and props draw their snow with the same surface maps as the ground (BwmSnowCommon.hlsl)
        // and read them as globals, so the maps ride in the island's pack once: hand over the ones on the rocks'
        // snow material
        void LendSnowMaps()
        {
            Texture wind = overlay.GetTexture("_WindNormal"), height = overlay.GetTexture("_WindHeight"), grain = overlay.GetTexture("_GrainNormal");
            if (wind == null || height == null || grain == null) { Plugin.L.LogWarning("Winter Walk: the snow maps are not on the island's material; snow on trees and roofs stays smooth"); return; }
            Shader.SetGlobalTexture("_BwmWindNormal", wind);
            Shader.SetGlobalTexture("_BwmWindHeight", height);
            Shader.SetGlobalTexture("_BwmGrainNormal", grain);
            Shader.SetGlobalFloat("_BwmSnowMaps", 1f);
            Plugin.L.LogInfo("Winter Walk: lent the snow's surface maps to trees, roofs and props");
        }

        void Begin()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            dirty = false;
            if (holder != null) Destroy(holder);
            rockSnow.Clear(); stilled.Clear(); leafSnow.Clear(); warned.Clear(); protos.Clear();
            Templates();
            try { LendTextures(); } catch (Exception ex) { Plugin.L.LogWarning("Winter Walk: textures not lent: " + ex.Message); }
            try { LendSnowMaps(); } catch (Exception ex) { Plugin.L.LogWarning("Winter Walk: snow maps not lent: " + ex.GetType().Name + ": " + ex.Message); }
            holder = new GameObject("Winter Dressing");
            holder.SetActive(false);      // nothing wakes until every copy is made
            var prototypes = new GameObject("Prototypes");
            prototypes.SetActive(false);
            prototypes.transform.SetParent(holder.transform, false);
            protoRoot = prototypes.transform;
            next = 0; made = 0; missing = 0; wakeAt = -1;
            placedEntry.Clear(); checkAt = -1;
            cells.Clear(); cellAt.Clear(); copies.Clear(); small.Clear(); culling = false; Shown = 0; Made = 0; smallShadowsApplied = SmallShadows;
            started = Time.realtimeSinceStartup;
            setupMs += MsSince(t0); copyMs = wakeMs = 0; copyFrames = wakeFrames = 0;
        }

        void Step()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float budget = Time.realtimeSinceStartup + 0.012f;      // about 12 ms of copying per frame
            while (next < entries.Count && Time.realtimeSinceStartup < budget)
            {
                int before = made, index = next;
                Copy(entries[next++]);
                if (made > before) placedEntry.Add(index);
            }
            copyMs += MsSince(t0); copyFrames++;
            if (next < entries.Count) return;
            next = -1;
            Made = made;
            holder.SetActive(true);      // its cell groups are still switched off: they wake a few a frame
            StartWaking();
        }
    }
}
