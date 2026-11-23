// Winter Walk's scatter: the dry grass, fallen sticks and stones that break the snow's surface at a walker's feet.
//
// The island's build (WinterIslePuzzle.cs, section 7b) puts a few models of each in the island's pack ("Scatter
// Models", switched off) and writes where each one stands to WinterIsle.scatter beside this plugin: about 400,000
// places. Here the ones near the camera are drawn, instanced: a handful of draw calls and a few megabytes, where
// the same things built into the pack as merged meshes were 49 million vertices (4.7 GB of memory, half of it
// video memory) and were all drawn from anywhere on the island.
//
// The list is sorted into 32 m cells. When the camera has moved a few metres, the cells in reach are gone through
// again, a little each frame, into a second set of matrix arrays, which then replaces the first. Things shrink into
// the ground over the last metres of their reach, so nothing pops. Nothing is sent between players: every PC draws
// the same list.
using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterWalk
{
    public class Scatter : MonoBehaviour
    {
        public Scatter(IntPtr ptr) : base(ptr) { }

        // Live settings (the companion: set?path=WinterWalk.Scatter.GrassNear&value=70)
        public static float GrassNear = 70f, LitterNear = 50f, Fade = 14f;
        public static bool Off;
        public static string State = "no list";
        public static int Drawn, Calls;

        struct Place { public byte model; public Vector3 at; public Quaternion turn; public float scale; }
        struct Span { public int start, count; }

        const float CellSize = 32f;
        const int Batch = 1023;      // the most instances one call draws

        static string file;
        DateTime fileTime;
        float nextCheck, nextSearch;
        string[] names = new string[0];
        bool[] grass = new bool[0];
        Place[] places = new Place[0];
        readonly Dictionary<long, Span> cells = new Dictionary<long, Span>();
        Mesh[] meshes = new Mesh[0];
        Material[] materials = new Material[0];
        bool[] have = new bool[0];      // which models were found
        bool models;

        sealed class Set
        {
            public readonly List<Il2CppStructArray<Matrix4x4>>[] arrays;
            public readonly List<int>[] counts;
            public int total;
            public Set(int n)
            {
                arrays = new List<Il2CppStructArray<Matrix4x4>>[n]; counts = new List<int>[n];
                for (int i = 0; i < n; i++) { arrays[i] = new List<Il2CppStructArray<Matrix4x4>>(); counts[i] = new List<int>(); }
            }
            public void Clear() { foreach (List<int> c in counts) for (int i = 0; i < c.Count; i++) c[i] = 0; total = 0; }
            public void Add(int model, ref Matrix4x4 m)
            {
                List<int> c = counts[model];
                int b = 0;
                while (b < c.Count && c[b] >= Batch) b++;
                if (b == c.Count) { arrays[model].Add(new Il2CppStructArray<Matrix4x4>(Batch)); c.Add(0); }
                arrays[model][b][c[b]] = m;
                c[b]++; total++;
            }
        }
        Set front, back;

        // the pass in hand: the cells still to go through, round 'centre'
        readonly List<long> todo = new List<long>();
        int todoAt = -1;
        Vector3 centre, drawnCentre = new Vector3(1e9f, 0f, 0f);
        float drawnGrass, drawnLitter;
        bool drawnOff;

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        // The list's bytes are read on a thread, started from Plugin.Load (as the dresser's: read by the first frame,
        // from a hard disk Windows had not cached, it held the game for 2 s)
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
                file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.scatter");
                if (File.Exists(file)) StartReading(File.GetLastWriteTimeUtc(file));
            }
            catch (Exception) { reading = null; }
        }

        void Load(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadByte() != 'B' || r.ReadByte() != 'W' || r.ReadByte() != 'S' || r.ReadByte() != 'C' || r.ReadInt32() != 1) throw new InvalidDataException("not a scatter list this version reads");
                int n = r.ReadInt32();
                names = new string[n]; grass = new bool[n];
                for (int i = 0; i < n; i++) { names[i] = r.ReadString(); grass[i] = r.ReadBoolean(); }
                int count = r.ReadInt32();
                var read = new Place[count];
                var keys = new long[count];
                for (int i = 0; i < count; i++)
                {
                    // (fields set one by one, and System.Math: every method of a game type, even Vector3's
                    // constructor or Mathf.Sqrt, is a call into the game, and there are 400,000 of these)
                    Place p = default;
                    p.model = r.ReadByte();
                    p.at.x = r.ReadSingle(); p.at.y = r.ReadSingle(); p.at.z = r.ReadSingle();
                    p.turn.x = r.ReadInt16() / 32767f; p.turn.y = r.ReadInt16() / 32767f; p.turn.z = r.ReadInt16() / 32767f; p.turn.w = r.ReadInt16() / 32767f;
                    p.scale = r.ReadSingle();
                    read[i] = p;
                    keys[i] = Key((int)Math.Floor(p.at.x / CellSize), (int)Math.Floor(p.at.z / CellSize));
                }
                Array.Sort(keys, read);
                cells.Clear();
                for (int i = 0; i < count;)
                {
                    int j = i;
                    while (j < count && keys[j] == keys[i]) j++;
                    cells[keys[i]] = new Span { start = i, count = j - i };
                    i = j;
                }
                places = read;
            }
            front = new Set(names.Length); back = new Set(names.Length);
            meshes = new Mesh[names.Length]; materials = new Material[names.Length]; have = new bool[names.Length];
            models = false; todoAt = -1; drawnCentre = new Vector3(1e9f, 0f, 0f);
            Plugin.L.LogInfo("Winter Walk: read " + places.Length + " places of " + names.Length + " scatter models in " + cells.Count + " cells from " + Path.GetFileName(file));
        }

        // The models ride in the island's pack, switched off
        void FindModels()
        {
            int found = 0;
            foreach (Transform t in FindObjectsOfType<Transform>(true))
            {
                if (!t.name.StartsWith("Scatter Model ") || t.parent == null || t.parent.name != "Scatter Models") continue;
                int i = Array.IndexOf(names, t.name.Substring(14));
                if (i < 0) continue;
                MeshFilter mf = t.GetComponent<MeshFilter>();
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
                meshes[i] = mf.sharedMesh; materials[i] = mr.sharedMaterial; have[i] = true;
                found++;
            }
            models = found > 0;
            if (models) Plugin.L.LogInfo("Winter Walk: " + found + " of " + names.Length + " scatter models found in the island's pack; drawing grass within " + GrassNear + " m, sticks and stones within " + LitterNear + " m");
        }

        void StartPass(Vector3 eye)
        {
            centre = eye;
            drawnGrass = GrassNear; drawnLitter = LitterNear; drawnOff = Off;
            back.Clear();
            todo.Clear();
            float reach = Off ? 0f : Math.Max(GrassNear, LitterNear);
            if (reach > 0f)
            {
                int x0 = (int)Math.Floor((eye.x - reach) / CellSize), x1 = (int)Math.Floor((eye.x + reach) / CellSize);
                int z0 = (int)Math.Floor((eye.z - reach) / CellSize), z1 = (int)Math.Floor((eye.z + reach) / CellSize);
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        if (cells.ContainsKey(Key(cx, cz))) todo.Add(Key(cx, cz));
            }
            todoAt = 0;
        }

        // A few tenths of a millisecond a frame
        void StepPass()
        {
            float budget = Time.realtimeSinceStartup + 0.0004f;
            float fade = Math.Max(1f, Fade);
            while (todoAt < todo.Count)
            {
                Span span = cells[todo[todoAt++]];
                for (int i = span.start; i < span.start + span.count; i++)
                {
                    Place p = places[i];
                    if (!have[p.model]) continue;
                    float reach = grass[p.model] ? drawnGrass : drawnLitter;
                    float dx = p.at.x - centre.x, dy = p.at.y - centre.y, dz = p.at.z - centre.z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 >= reach * reach) continue;
                    float t = Math.Min(1f, (reach - (float)Math.Sqrt(d2)) / fade);
                    float s = p.scale * t * t * (3f - 2f * t);
                    // position, turn and size as a matrix
                    Quaternion q = p.turn;
                    float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z, xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z, wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
                    Matrix4x4 m = default;
                    m.m00 = (1f - 2f * (yy + zz)) * s; m.m01 = 2f * (xy - wz) * s; m.m02 = 2f * (xz + wy) * s; m.m03 = p.at.x;
                    m.m10 = 2f * (xy + wz) * s; m.m11 = (1f - 2f * (xx + zz)) * s; m.m12 = 2f * (yz - wx) * s; m.m13 = p.at.y;
                    m.m20 = 2f * (xz - wy) * s; m.m21 = 2f * (yz + wx) * s; m.m22 = (1f - 2f * (xx + yy)) * s; m.m23 = p.at.z;
                    m.m33 = 1f;
                    back.Add(p.model, ref m);
                }
                if (Time.realtimeSinceStartup >= budget) return;
            }
            Set was = front; front = back; back = was;
            drawnCentre = centre;
            todoAt = -1;
        }

        bool failed;

        void Update()
        {
            using var diag = Diag.Time("scatter");
            if (failed) return;
            try
            {
                if (Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + 1f;
                    if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.scatter");
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
                            }
                        }
                    }
                    // the island's pack was loaded, or loaded again: its models are new objects
                    if (models && (meshes.Length == 0 || Array.TrueForAll(meshes, m => m == null))) { models = false; Array.Clear(have, 0, have.Length); drawnCentre = new Vector3(1e9f, 0f, 0f); todoAt = -1; if (front != null) front.Clear(); }
                    if (!models && places.Length > 0 && Time.unscaledTime >= nextSearch && GameObject.Find("Material Store") != null)
                    {
                        nextSearch = Time.unscaledTime + 5f;
                        FindModels();
                    }
                    State = places.Length == 0 ? "no list" : !models ? places.Length + " places, no models (the island is not loaded)" : Off ? "off" : Drawn + " of " + places.Length + " drawn in " + Calls + " calls";
                }
                if (!models) { Drawn = Calls = 0; return; }
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 eye = cam.transform.position;
                float mx = eye.x - drawnCentre.x, my = eye.y - drawnCentre.y, mz = eye.z - drawnCentre.z;
                if (todoAt < 0 && (mx * mx + my * my + mz * mz > 36f || drawnGrass != GrassNear || drawnLitter != LitterNear || drawnOff != Off)) StartPass(eye);
                if (todoAt >= 0) StepPass();

                int calls = 0;
                for (int i = 0; i < meshes.Length; i++)
                {
                    List<int> c = front.counts[i];
                    for (int b = 0; b < c.Count; b++)
                    {
                        if (c[b] == 0) continue;
                        Graphics.DrawMeshInstanced(meshes[i], 0, materials[i], front.arrays[i][b], c[b], null, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off, null);
                        calls++;
                    }
                }
                Drawn = front.total; Calls = calls;
            }
            catch (Exception e)
            {
                failed = true;
                Plugin.L.LogWarning("Winter Walk: scatter stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
