// Winter Walk: the island's lamps give light at night.
//
// With the night as dark as the game's (2026-11-24) the island needs light of its own, sparsely: the island's build
// lists every lantern, porch lamp and station lamp in WinterIsle.lamps (x y z power). The build already shows each
// from afar (a warm point in the tiny lights' mesh, a pool of light baked into the ground's glow map). This adds the
// real thing near the player, for the eight lamps nearest this player's camera, in two ways at once:
//  - a point light without shadows on each, which the game's own surfaces take (its rocks and plants, puzzle parts,
//    players): they are lit deferred, with no limit on lights;
//  - the same eight as shader globals (_BwmLampPos0..7, _BwmLampCol0..7, _BwmLampCount, _BwmLampReach), which our own
//    surfaces light themselves from (BwmLamps.hlsl). Ours are drawn forward, and the game allows a forward surface
//    four lights, chosen for the whole object: the ground's district tiles and the meshes that span the island
//    (cabins, lanterns, platforms) each got some four of the lamps, and a cabin stood black beside its own porch lamp.
//    Those shaders leave our real lights out of their own count (by where they are), so no lamp counts twice.
//
// A lamp fades in from nothing at the edge of the reach (or, where more lamps are near than eight, at the distance
// of the nearest lamp left out), so none pops on; and a light stays with its lamp until that lamp is out of reach, so
// none jumps from lamp to lamp while lit. They are lit as far as it is night (WeatherDriver.Night, which the light
// strings and the pools go by too); by day there are no lights at all.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WinterWalk
{
    public class IsleLamps : MonoBehaviour
    {
        public IsleLamps(IntPtr ptr) : base(ptr) { }

        public static bool Off;                           // live, for tests
        public const int Most = 8;                        // lamps lit at once (the shaders' globals hold eight)
        public static int Count = Most;                   // live: fewer, for tests
        public static float Reach = 60f;                  // metres from the camera within which a lamp is lit for real
        public static float Fade = 14f;                   // ...fading in over this much of it
        public static float Power = 1.6f, Range = 9f;     // a lamp of power 1: its light's intensity and range
        public static float Red = 1f, Green = 0.8f, Blue = 0.56f;      // its colour
        // The real lights leave layer 10 alone, the layer the puzzle SDK puts every pack's objects on: ours take the
        // lamps from the globals, and a real light they do not use would still take one of a mesh's four places from
        // a torch. (The game's own surfaces are lit deferred, where a light's culling mask is not asked: its rocks and
        // plants on layer 10 are lit all the same. Checked 2026-11-24.) Live, for tests: -1 lights every layer.
        public static int Mask = ~(1 << 10);
        public static bool NoGlobals;                     // live, for tests: our own surfaces are not handed the lamps
        public static string State = "not started";
        public static int Known, Lit;

        float[] lampX, lampY, lampZ, lampPower;
        bool loaded;
        GameObject holder;
        readonly List<Light> lights = new List<Light>();
        int[] onLamp = new int[0];      // the lamp each light is set on, -1 none
        int[] near = new int[0];        // the nearest lamps, nearest first (one more than there are lights)
        float[] nearD = new float[0];
        float nextStep, cutoff;
        bool anyOn, globalsSet;
        int maskSet = -1;      // (a new light lights every layer)
        static readonly string[] PosName = { "_BwmLampPos0", "_BwmLampPos1", "_BwmLampPos2", "_BwmLampPos3", "_BwmLampPos4", "_BwmLampPos5", "_BwmLampPos6", "_BwmLampPos7" };
        static readonly string[] ColName = { "_BwmLampCol0", "_BwmLampCol1", "_BwmLampCol2", "_BwmLampCol3", "_BwmLampCol4", "_BwmLampCol5", "_BwmLampCol6", "_BwmLampCol7" };

        void Load()
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.lamps");
                if (!File.Exists(file)) { State = "no WinterIsle.lamps beside the plugin"; Plugin.L.LogWarning("Winter Walk: " + State + ": the island's lamps throw no light of their own"); return; }
                var x = new List<float>(); var y = new List<float>(); var z = new List<float>(); var p = new List<float>();
                foreach (string line in File.ReadAllLines(file))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] f = line.Split(' ');
                    if (f.Length < 3) continue;
                    if (!float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float lx) || !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ly)
                        || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float lz)) continue;
                    float power = 1f;
                    if (f.Length > 3) float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out power);
                    if (float.IsNaN(lx) || float.IsNaN(ly) || float.IsNaN(lz) || !(power > 0f)) continue;
                    x.Add(lx); y.Add(ly); z.Add(lz); p.Add(power);
                }
                lampX = x.ToArray(); lampY = y.ToArray(); lampZ = z.ToArray(); lampPower = p.ToArray();
                Known = lampX.Length;
                Plugin.L.LogInfo("Winter Walk: read " + Known + " lamps from WinterIsle.lamps");
            }
            catch (Exception e) { State = "WinterIsle.lamps: " + e.GetType().Name + ": " + e.Message; Plugin.L.LogWarning("Winter Walk: " + State); }
        }

        void MakeLights()
        {
            if (holder != null) Destroy(holder);
            holder = new GameObject("Winter Walk Isle Lamps");
            lights.Clear();
            int count = Math.Max(1, Math.Min(Count, Most));
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Isle Lamp " + i);
                go.transform.SetParent(holder.transform, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(Red, Green, Blue);
                light.shadows = LightShadows.None;
                light.range = Range;
                light.intensity = 0f;
                light.enabled = false;
                lights.Add(light);
            }
            onLamp = new int[count];
            for (int i = 0; i < count; i++) onLamp[i] = -1;
            near = new int[count + 1];
            nearD = new float[count + 1];
            maskSet = -1;
        }

        // Our own surfaces' lamps: none
        void ClearGlobals()
        {
            if (!globalsSet) return;
            Shader.SetGlobalFloat("_BwmLampCount", 0f);
            Shader.SetGlobalVector("_BwmLampReach", new Vector4(0f, 1f, 0f, 0f));
            globalsSet = false;
        }

        void AllOff()
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null && lights[i].enabled) lights[i].enabled = false;
                if (i < onLamp.Length) onLamp[i] = -1;
            }
            anyOn = false;
            Lit = 0;
            ClearGlobals();
        }

        static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        void Update()
        {
            using var diag = Diag.Time("lamps");
            try
            {
                if (!loaded) { loaded = true; Load(); }
                if (lampX == null || lampX.Length == 0) return;
                float now = Time.unscaledTime;
                if (now < nextStep) return;      // fifteen times a second: a walker moves 15 cm in that, of a fade 14 m long
                nextStep = now + 0.066f;
                float night = Off ? 0f : WeatherDriver.Night;
                if (night < 0.01f)
                {
                    if (anyOn) AllOff();
                    State = Known + " lamps, unlit by day";
                    nextStep = now + 0.5f;
                    return;
                }
                Camera cam = Camera.main;
                if (cam == null) return;
                if (holder == null || lights.Count != Math.Max(1, Math.Min(Count, Most))) MakeLights();
                Vector3 eye = cam.transform.position;
                float ex = eye.x, ey = eye.y, ez = eye.z;

                // the nearest lamps within reach, and one more: the first left out sets where the lights fade to nothing
                int want = lights.Count, found = 0;
                float reach2 = Reach * Reach;
                for (int i = 0; i < lampX.Length; i++)
                {
                    float dx = lampX[i] - ex, dz = lampZ[i] - ez;
                    if (dx > Reach || dx < -Reach || dz > Reach || dz < -Reach) continue;
                    float dy = lampY[i] - ey, d = dx * dx + dy * dy + dz * dz;
                    if (d > reach2) continue;
                    int at = found < want + 1 ? found : want + 1;
                    while (at > 0 && nearD[at - 1] > d) at--;
                    if (at > want) continue;
                    int last = found < want + 1 ? found : want;
                    for (int k = last; k > at; k--) { near[k] = near[k - 1]; nearD[k] = nearD[k - 1]; }
                    near[at] = i; nearD[at] = d;
                    if (found < want + 1) found++;
                }
                cutoff = found > want ? (float)Math.Sqrt(nearD[want]) : Reach;
                int chosen = found < want ? found : want;

                // lights whose lamp is no longer among them go out; lamps without a light take a free one
                for (int s = 0; s < want; s++)
                {
                    if (lights[s] == null) { holder = null; return; }
                    if (onLamp[s] < 0) continue;
                    bool kept = false;
                    for (int k = 0; k < chosen; k++) if (near[k] == onLamp[s]) { kept = true; break; }
                    if (!kept) { onLamp[s] = -1; if (lights[s].enabled) lights[s].enabled = false; }
                }
                if (maskSet != Mask)
                {
                    for (int s = 0; s < want; s++) lights[s].cullingMask = Mask;
                    maskSet = Mask;
                }
                int lit = 0;
                float perRange = 1f / Math.Max(Range * Range, 0.01f);
                bool hand = !NoGlobals;
                for (int k = 0; k < chosen; k++)
                {
                    int lamp = near[k], slot = -1;
                    for (int s = 0; s < want; s++) if (onLamp[s] == lamp) { slot = s; break; }
                    if (slot < 0)
                    {
                        for (int s = 0; s < want; s++) if (onLamp[s] < 0) { slot = s; break; }
                        if (slot < 0) continue;
                        onLamp[slot] = lamp;
                        lights[slot].transform.position = new Vector3(lampX[lamp], lampY[lamp], lampZ[lamp]);
                    }
                    float d = (float)Math.Sqrt(nearD[k]);
                    float strength = Power * lampPower[lamp] * night * Smooth((cutoff - d) / Math.Max(Fade, 0.1f));
                    if (hand)
                    {
                        Shader.SetGlobalVector(PosName[k], new Vector4(lampX[lamp], lampY[lamp], lampZ[lamp], perRange));
                        Shader.SetGlobalVector(ColName[k], new Vector4(Red * strength, Green * strength, Blue * strength, 0f));
                    }
                    Light light = lights[slot];
                    if (strength < 0.004f) { if (light.enabled) light.enabled = false; continue; }
                    light.intensity = strength;
                    light.range = Range;
                    if (!light.enabled) light.enabled = true;
                    lit++;
                }
                if (hand)
                {
                    Shader.SetGlobalFloat("_BwmLampCount", chosen);
                    Shader.SetGlobalVector("_BwmLampReach", new Vector4(chosen > 0 ? cutoff : 0f, Math.Max(Fade, 0.1f), 0f, 0f));
                    globalsSet = true;
                }
                else ClearGlobals();
                Lit = lit;
                anyOn = true;
                State = Known + " lamps, " + lit + " lit for real (night " + night.ToString("0.00") + ")";
            }
            catch (Exception e)
            {
                if (!State.StartsWith("stopped")) Plugin.L.LogWarning("Winter Walk: lamps: " + e.GetType().Name + ": " + e.Message);
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                nextStep = Time.unscaledTime + 10f;
            }
        }
    }
}
