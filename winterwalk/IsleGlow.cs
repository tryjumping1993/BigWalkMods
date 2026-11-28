// Winter Walk: the Christmas lights throw light.
//
// The island's bulbs (the string down the mountain, the ski runs' edges, the railway's sides: 6,000 of them on
// stakes) and the light strings between posts at every puzzle, station and cabin are only drawn bright. Until
// 2026-11-24 the stakes' bulbs lit the snow through a map the build baked, each one's colour spread 4 m round it,
// which with the night as dark as the game's lit the hillsides in bands of red and green ten metres wide; and the
// strings at the puzzles threw no light at all, so the snow under a ring of lights was black. The user: "a natural
// amount of illumination".
//
// Now each is a small light with a real light's falloff (BwmGlow.hlsl has the rule): a bulb is a point, a stretch of
// string a line. This hands the 24 nearest this player's camera to our shaders as globals (_BwmGlowA/B/C, arrays),
// so every surface of ours near them is lit from the bulbs themselves, whatever the mesh; beyond those the ground
// shows the same light from the island's baked map (_BwmGlowReach says where one hands over to the other). The
// nearest strings also get a real point light at their middle (no shadows), for the game's own surfaces, which are
// lit deferred: players, puzzle parts, the game's rocks. Our shaders leave those lights out (they all have one odd
// range, _BwmGlowReal), so a string is not counted twice.
//
// Bulbs come from WinterIsle.glow, which the island's build writes beside the plugin (x y z r g b). Strings are
// found in the loaded packs: Kit.LightString leaves an empty object named "String Glow <colour> <bulbs> <length>" at
// the middle of each stretch, turned along it, under the string. A string that is not shown (in a lobby group for
// another number of players, say) throws no light. All of it is lit as far as it is night (WeatherDriver.Night).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace WinterWalk
{
    public class IsleGlow : MonoBehaviour
    {
        public IsleGlow(IntPtr ptr) : base(ptr) { }

        public static bool Off;                           // live, for tests
        public const int Most = 24;                       // bulbs and stretches lit at once (the shaders' arrays hold 24)
        public static int Count = Most;                   // live: fewer, for tests
        public static float Reach = 48f;                  // metres from the camera within which they are lit this way
        public static float Fade = 12f;                   // ...fading in over this much of it
        // The same numbers as the island's build bakes with (WinterIslePuzzle.Garlands): a staked bulb's and a string
        // bulb's light at 1 m (a lantern's is 1.6), and how far each counts
        public static float BulbPower = 0.16f, BulbRange = 3.6f, FairyPower = 0.05f, StringRange = 9f;
        public static int RealLights = 8;                 // real point lights, on the nearest strings
        public static float RealScale = 1f;               // live: their brightness
        public const float RealRange = 9.0731f;           // their range: an odd one, which our shaders know them by
        public static int Mask = ~(1 << 10);              // (not our own surfaces' layer: see IsleLamps.Mask)
        public static bool NoGlobals;                     // live, for tests: our own surfaces are not handed the lights
        public static string State = "not started";
        public static int Bulbs, Strings, Lit;

        // the island's bulbs
        float[] bx, by, bz, br, bg, bb;
        volatile bool bulbsRead;
        bool loadStarted, loadSaid;
        string loadError;

        // the packs' strings: one end, the vector to the other, colour, bulbs, and the marker (for whether it is shown)
        float[] sx = new float[0], sy, sz, vx, vy, vz, sr, sg, sb, sLength, sChecked;
        int[] sBulbs;
        bool[] sShown;
        GameObject[] sMark;
        bool stringsFound;
        float nextLook;
        // (the search goes pack by pack, one a frame)
        readonly List<Transform> roots = new List<Transform>();
        int rootAt = -1;
        readonly List<float> fx = new List<float>(), fy = new List<float>(), fz = new List<float>(), fvx = new List<float>(), fvy = new List<float>(), fvz = new List<float>(),
            fr = new List<float>(), fg = new List<float>(), fb = new List<float>(), fLength = new List<float>();
        readonly List<int> fBulbs = new List<int>();
        readonly List<GameObject> fMark = new List<GameObject>();

        readonly int[] near = new int[Most + 1];      // nearest first: a bulb's index, or -1 - a string's
        readonly float[] nearD = new float[Most + 1];
        Il2CppStructArray<Vector4> arrayA, arrayB, arrayC;
        int idA, idB, idC, idCount, idReach, idReal;
        bool globalsSet, anyOn;
        float nextStep;

        GameObject holder;
        readonly List<Light> lights = new List<Light>();
        int[] onString = new int[0];
        // (what each light was last told, so that a light is only spoken to when something changes: every call into the game costs)
        bool[] lightOn = new bool[0];
        float[] lightBright = new float[0];
        float nextAlive;
        int maskSet = -1;

        void ReadBulbs(string file)
        {
            try
            {
                if (!File.Exists(file)) { loadError = "no WinterIsle.glow beside the plugin: the island's bulbs throw no light near the camera"; return; }
                var x = new List<float>(); var y = new List<float>(); var z = new List<float>(); var r = new List<float>(); var g = new List<float>(); var b = new List<float>();
                foreach (string line in File.ReadLines(file))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] f = line.Split(' ');
                    if (f.Length < 6) continue;
                    var v = new float[6];
                    bool good = true;
                    for (int k = 0; k < 6 && good; k++) good = float.TryParse(f[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) && !float.IsNaN(v[k]) && !float.IsInfinity(v[k]);
                    if (!good) continue;
                    x.Add(v[0]); y.Add(v[1]); z.Add(v[2]); r.Add(v[3]); g.Add(v[4]); b.Add(v[5]);
                }
                by = y.ToArray(); bz = z.ToArray(); br = r.ToArray(); bg = g.ToArray(); bb = b.ToArray();
                bx = x.ToArray();
            }
            catch (Exception e) { loadError = "WinterIsle.glow: " + e.GetType().Name + ": " + e.Message; }
            finally { bulbsRead = true; }
        }

        // The next pack's strings (one pack a frame: reading every object's name takes a few milliseconds a pack)
        void Look()
        {
            if (rootAt < 0)
            {
                roots.Clear();
                fx.Clear(); fy.Clear(); fz.Clear(); fvx.Clear(); fvy.Clear(); fvz.Clear(); fr.Clear(); fg.Clear(); fb.Clear(); fLength.Clear(); fBulbs.Clear(); fMark.Clear();
                foreach (BigPuzzleSDK.PuzzleSDK sdk in UnityEngine.Object.FindObjectsOfType<BigPuzzleSDK.PuzzleSDK>())
                {
                    if (sdk == null) continue;
                    Transform root = sdk.transform.root;
                    bool have = false;
                    for (int i = 0; i < roots.Count; i++) if (roots[i] == root) { have = true; break; }
                    if (!have) roots.Add(root);
                }
                if (roots.Count == 0) { nextLook = Time.unscaledTime + 5f; return; }      // (not in a walk yet)
                rootAt = 0;
                return;
            }
            Transform pack = roots[rootAt++];
            if (pack != null)
                foreach (Component c in pack.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Transform>(), true))
                {
                    Transform t = c != null ? c.TryCast<Transform>() : null;
                    if (t == null) continue;
                    string name = t.name;
                    if (!name.StartsWith("String Glow ")) continue;
                    string[] w = name.Split(' ');
                    if (w.Length < 5 || w[2].Length != 6) continue;
                    if (!int.TryParse(w[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) || !int.TryParse(w[3], out int bulbs)
                        || !float.TryParse(w[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float length)) continue;
                    Vector3 at = t.position, along = t.forward;
                    float ax = at.x, ay = at.y, az = at.z;
                    if (float.IsNaN(ax) || float.IsNaN(ay) || float.IsNaN(az) || bulbs <= 0 || !(length >= 0f)) continue;
                    if (length < 0.05f) length = 0f;
                    float lx = along.x * length, ly = along.y * length, lz = along.z * length;
                    fx.Add(ax - lx * 0.5f); fy.Add(ay - ly * 0.5f); fz.Add(az - lz * 0.5f); fvx.Add(lx); fvy.Add(ly); fvz.Add(lz);
                    fr.Add(((rgb >> 16) & 255) / 255f); fg.Add(((rgb >> 8) & 255) / 255f); fb.Add((rgb & 255) / 255f);
                    fLength.Add(length); fBulbs.Add(bulbs); fMark.Add(t.gameObject);
                }
            if (rootAt < roots.Count) return;
            sx = fx.ToArray(); sy = fy.ToArray(); sz = fz.ToArray(); vx = fvx.ToArray(); vy = fvy.ToArray(); vz = fvz.ToArray();
            sr = fr.ToArray(); sg = fg.ToArray(); sb = fb.ToArray(); sLength = fLength.ToArray(); sBulbs = fBulbs.ToArray(); sMark = fMark.ToArray();
            sShown = new bool[sx.Length]; sChecked = new float[sx.Length];
            for (int i = 0; i < sChecked.Length; i++) sChecked[i] = -100f;
            for (int s = 0; s < onString.Length; s++) onString[s] = -1;
            Strings = sx.Length;
            stringsFound = true;
            rootAt = -1;
            nextLook = Time.unscaledTime + (Strings > 0 ? 1e9f : 30f);
            Plugin.L.LogInfo("Winter Walk: " + Strings + " stretches of light string in " + roots.Count + " packs");
        }

        void MakeLights(int count)
        {
            if (holder != null) Destroy(holder);
            holder = new GameObject("Winter Walk String Glow");
            lights.Clear();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("String Glow Light " + i);
                go.transform.SetParent(holder.transform, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.range = RealRange;
                light.intensity = 0f;
                light.enabled = false;
                lights.Add(light);
            }
            onString = new int[count];
            for (int i = 0; i < count; i++) onString[i] = -1;
            lightOn = new bool[count];
            lightBright = new float[count];
            maskSet = -1;
        }

        void ClearGlobals()
        {
            if (!globalsSet) return;
            Shader.SetGlobalFloat(idCount, 0f);
            Shader.SetGlobalVector(idReach, new Vector4(0f, 1f, 0f, 0f));
            Shader.SetGlobalVector(idReal, new Vector4(0f, 0f, 0f, 0f));
            globalsSet = false;
        }

        void AllOff()
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null && lights[i].enabled) lights[i].enabled = false;
                if (i < onString.Length) { onString[i] = -1; lightOn[i] = false; }
            }
            anyOn = false;
            Lit = 0;
            ClearGlobals();
        }

        static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
        static float Gamma(float v) => v <= 0.0031308f ? 12.92f * v : 1.055f * (float)Math.Pow(v, 1.0 / 2.4) - 0.055f;

        void Insert(int who, float d, int want, ref int found)
        {
            int at = found < want + 1 ? found : want + 1;
            while (at > 0 && nearD[at - 1] > d) at--;
            if (at > want) return;
            int last = found < want + 1 ? found : want;
            for (int k = last; k > at; k--) { near[k] = near[k - 1]; nearD[k] = nearD[k - 1]; }
            near[at] = who; nearD[at] = d;
            if (found < want + 1) found++;
        }

        void Update()
        {
            using var diag = Diag.Time("glow");
            try
            {
                if (!loadStarted)
                {
                    loadStarted = true;
                    idA = Shader.PropertyToID("_BwmGlowA"); idB = Shader.PropertyToID("_BwmGlowB"); idC = Shader.PropertyToID("_BwmGlowC");
                    idCount = Shader.PropertyToID("_BwmGlowCount"); idReach = Shader.PropertyToID("_BwmGlowReach"); idReal = Shader.PropertyToID("_BwmGlowReal");
                    arrayA = new Il2CppStructArray<Vector4>(Most); arrayB = new Il2CppStructArray<Vector4>(Most); arrayC = new Il2CppStructArray<Vector4>(Most);
                    string file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.glow");
                    System.Threading.Tasks.Task.Run(() => ReadBulbs(file));
                }
                if (!bulbsRead) return;
                if (!loadSaid)
                {
                    loadSaid = true;
                    if (bx == null) bx = by = bz = br = bg = bb = new float[0];
                    Bulbs = bx.Length;
                    if (loadError != null) Plugin.L.LogWarning("Winter Walk: " + loadError);
                    else Plugin.L.LogInfo("Winter Walk: read " + Bulbs + " strung bulbs from WinterIsle.glow");
                }
                float now = Time.unscaledTime;
                if ((!stringsFound || rootAt >= 0) && now >= nextLook && Setup.Turn()) Look();
                if (now < nextStep) return;      // fifteen times a second, as the lamps
                nextStep = now + 0.066f;
                float night = Off ? 0f : WeatherDriver.Night;
                if (night < 0.01f || (Bulbs == 0 && Strings == 0))
                {
                    if (anyOn) AllOff();
                    State = Bulbs + " bulbs and " + Strings + " stretches of string, " + (night < 0.01f ? "unlit by day" : "none to light");
                    nextStep = now + 0.5f;
                    return;
                }
                Camera cam = Camera.main;
                if (cam == null) return;
                int want = Count < 0 ? 0 : Count > Most ? Most : Count;
                int real = RealLights < 0 ? 0 : RealLights > want ? want : RealLights;
                if (holder == null || lights.Count != real) MakeLights(real);
                Vector3 eye = cam.transform.position;
                float ex = eye.x, ey = eye.y, ez = eye.z;

                // the nearest within reach, and one more: the first left out sets where the light fades to nothing
                int found = 0;
                float reach2 = Reach * Reach;
                for (int i = 0; i < bx.Length; i++)
                {
                    float dx = bx[i] - ex, dz = bz[i] - ez;
                    if (dx > Reach || dx < -Reach || dz > Reach || dz < -Reach) continue;
                    float dy = by[i] - ey, d = dx * dx + dy * dy + dz * dz;
                    if (d > reach2) continue;
                    Insert(i, d, want, ref found);
                }
                if (rootAt < 0)
                    for (int j = 0; j < sx.Length; j++)
                    {
                        // from the eye to the nearest point of the stretch
                        float px = ex - sx[j], py = ey - sy[j], pz = ez - sz[j];
                        float length2 = sLength[j] * sLength[j];
                        float t = length2 > 0f ? (px * vx[j] + py * vy[j] + pz * vz[j]) / length2 : 0f;
                        t = t < 0f ? 0f : t > 1f ? 1f : t;
                        float dx = px - vx[j] * t, dy = py - vy[j] * t, dz = pz - vz[j] * t, d = dx * dx + dy * dy + dz * dz;
                        if (d > reach2) continue;
                        if (now - sChecked[j] > 0.5f)
                        {
                            GameObject mark = sMark[j];
                            if (mark == null)
                            {
                                // the packs were loaded again: these strings are gone; look for the new ones
                                stringsFound = false; nextLook = now + 2f; Strings = 0; sx = new float[0];
                                AllOff();
                                return;
                            }
                            sShown[j] = mark.activeInHierarchy;
                            sChecked[j] = now;
                        }
                        if (sShown[j]) Insert(-1 - j, d, want, ref found);
                    }
                float cutoff = found > want ? (float)Math.Sqrt(nearD[want]) : Reach;
                int chosen = found < want ? found : want;

                // real lights whose string is no longer among the nearest go out
                if (now >= nextAlive)
                {
                    nextAlive = now + 1f;
                    for (int s = 0; s < real; s++) if (lights[s] == null) { holder = null; return; }
                }
                for (int s = 0; s < real; s++)
                {
                    if (onString[s] < 0) continue;
                    bool kept = false;
                    for (int k = 0; k < chosen; k++) if (near[k] == -1 - onString[s]) { kept = true; break; }
                    if (!kept) { onString[s] = -1; if (lightOn[s]) { lights[s].enabled = false; lightOn[s] = false; } }
                }
                if (maskSet != Mask)
                {
                    for (int s = 0; s < real; s++) lights[s].cullingMask = Mask;
                    maskSet = Mask;
                }

                bool hand = !NoGlobals;
                int lit = 0, litBulbs = 0, litStrings = 0;
                float fade = Math.Max(Fade, 0.1f);
                var a = new Vector4(); var b = new Vector4(); var c = new Vector4();
                for (int k = 0; k < chosen; k++)
                {
                    float strength = night * Smooth((cutoff - (float)Math.Sqrt(nearD[k])) / fade);
                    if (near[k] >= 0)
                    {
                        int i = near[k];
                        float power = BulbPower * strength;
                        a.x = bx[i]; a.y = by[i]; a.z = bz[i]; a.w = 1f / Math.Max(BulbRange * BulbRange, 0.01f);
                        b.x = 0f; b.y = 0f; b.z = 0f; b.w = 0f;
                        c.x = br[i] * power; c.y = bg[i] * power; c.z = bb[i] * power; c.w = 0f;
                        litBulbs++;
                    }
                    else
                    {
                        int j = -1 - near[k];
                        float length2 = sLength[j] * sLength[j], power = FairyPower * sBulbs[j] * strength;
                        a.x = sx[j]; a.y = sy[j]; a.z = sz[j]; a.w = 1f / Math.Max(StringRange * StringRange, 0.01f);
                        b.x = vx[j]; b.y = vy[j]; b.z = vz[j]; b.w = length2 > 0f ? 1f / length2 : 0f;
                        c.x = sr[j] * power; c.y = sg[j] * power; c.z = sb[j] * power; c.w = length2 * 0.25f;
                        litStrings++;

                        // a real light at its middle, for the game's own surfaces: as bright there as the stretch is 2.6 m off
                        int slot = -1;
                        for (int s = 0; s < real; s++) if (onString[s] == j) { slot = s; break; }
                        if (slot < 0)
                            for (int s = 0; s < real; s++)
                                if (onString[s] < 0)
                                {
                                    slot = s; onString[s] = j; lightBright[s] = -1f;
                                    lights[s].transform.position = new Vector3(sx[j] + vx[j] * 0.5f, sy[j] + vy[j] * 0.5f, sz[j] + vz[j] * 0.5f);
                                    lights[s].color = new Color(Gamma(sr[j]), Gamma(sg[j]), Gamma(sb[j]));      // (a light's colour is asked for as sRGB)
                                    break;
                                }
                        if (slot >= 0)
                        {
                            float bright = power * RealScale * 2.6f / (float)Math.Sqrt(2.6f * 2.6f + length2 * 0.25f);
                            if (bright < 0.002f) { if (lightOn[slot]) { lights[slot].enabled = false; lightOn[slot] = false; } }
                            else
                            {
                                if (Math.Abs(bright - lightBright[slot]) > 0.01f * bright) { lights[slot].intensity = bright; lightBright[slot] = bright; }
                                if (!lightOn[slot]) { lights[slot].enabled = true; lightOn[slot] = true; }
                                lit++;
                            }
                        }
                    }
                    if (hand) { arrayA[k] = a; arrayB[k] = b; arrayC[k] = c; }
                }
                if (hand)
                {
                    Shader.SetGlobalVectorArray(idA, arrayA);
                    Shader.SetGlobalVectorArray(idB, arrayB);
                    Shader.SetGlobalVectorArray(idC, arrayC);
                    Shader.SetGlobalFloat(idCount, chosen);
                    a.x = chosen > 0 ? cutoff : 0f; a.y = fade; a.z = 0f; a.w = 0f;
                    Shader.SetGlobalVector(idReach, a);
                    a.x = real > 0 ? 1f / (RealRange * RealRange) : 0f; a.y = 0f;
                    Shader.SetGlobalVector(idReal, a);
                    globalsSet = true;
                }
                else ClearGlobals();
                Lit = lit;
                anyOn = true;
                State = Bulbs + " bulbs and " + Strings + " stretches of string; " + litBulbs + " bulbs and " + litStrings + " stretches lit within " + cutoff.ToString("0") + " m, " + lit
                    + " with a real light (night " + night.ToString("0.00") + ")";
            }
            catch (Exception e)
            {
                if (!State.StartsWith("stopped")) Plugin.L.LogWarning("Winter Walk: Christmas lights' glow: " + e.GetType().Name + ": " + e.Message);
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                nextStep = Time.unscaledTime + 10f;
            }
        }
    }
}
