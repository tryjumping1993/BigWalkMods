// Winter Walk: the sky and light of the Winter Isle.
//
// The game draws its sky, sun, fog and clouds with one system (Enviro), shared by the whole world. This plugin
// leaves it alone on the mainland and, as a player's camera comes near the island, blends it towards a winter
// setting: a paler, washed-out sky, haze on the horizon, a softer sun with gentler shadows, brighter light from
// the sky so the snow stays white, and a thin high layer of cloud. Each player sees it by their own position,
// so someone on the island and someone on the mainland each get the right sky. Nothing is sent between players.
//
// The winter values were found by trying them live in the game (see shots\sky-*.png in the repo).
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace WinterWalk
{
    [BepInPlugin("bwm.winterwalk", "Winter Walk", "0.1.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<bool> Enabled;

        public override void Load()
        {
            L = Log;
            Enabled = Config.Bind("General", "WinterSky", true, "Blend the sky and light towards winter near the Winter Isle.");
            ClassInjector.RegisterTypeInIl2Cpp<SkyDriver>();
            var go = new GameObject("WinterWalk");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<SkyDriver>();
            L.LogInfo("Winter Walk loaded");
        }
    }

    public class SkyDriver : MonoBehaviour
    {
        public SkyDriver(IntPtr ptr) : base(ptr) { }

        // The island's box in the world. Keep in step with WinterIslePuzzle.cs (X0, X1, Z0, Z1).
        const float X0 = -1016f, X1 = -428f, Z0 = -1016f, Z1 = -560f;
        const float FadeDistance = 170f;      // metres outside the box over which winter fades out

        // Winter's values
        const float SkyExponent = 0.55f;
        static readonly Color SkyTint = new Color(0.86f, 0.90f, 0.98f, 1f);
        const float SunStrength = 0.85f, SkyLight = 1.6f, ShadowStrength = 0.7f;
        const float FogDensity = 2.6f;
        const float CloudDensity = 1.2f;      // the thin cloud layer at full winter (the game's own value is 1.51)
        static readonly Color FogTint = new Color(0.92f, 0.95f, 1f, 1f);

        // The game's own values, read once per sky (a new walk makes a new one)
        static IntPtr captured = IntPtr.Zero;
        static float skyExponent0, sun0, skyLight0, shadow0, fog0;
        static Color skyTint0, fogTint0;
        static bool flatClouds0;
        static float cloudDensity0;
        static bool[] cloudSwitch0 = new bool[0];      // the cloud layer's switch in each quality level, default first
        static float lastWeight = -1f;

        // The game's quality levels each say whether the thin cloud layer is drawn, and the sky copies that from
        // the level in use every frame. So the switch has to be thrown there, in all of them.
        static Enviro.EnviroQuality Level(Enviro.EnviroManager env, int i)
        {
            Enviro.EnviroQualities q = env.Quality.Settings;
            return i == 0 ? q.defaultQuality : q.Qualities[i - 1];
        }
        static int Levels(Enviro.EnviroManager env)
        {
            if (env.Quality == null || env.Quality.Settings == null) return 0;
            return 1 + (env.Quality.Settings.Qualities != null ? env.Quality.Settings.Qualities.Count : 0);
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        void LateUpdate()
        {
            try
            {
                Enviro.EnviroManager env = Enviro.EnviroManager.instance;
                Camera cam = Camera.main;
                if (env == null || cam == null || env.Sky == null || env.Lighting == null || env.Fog == null) return;

                if (env.Pointer != captured)
                {
                    captured = env.Pointer;
                    skyExponent0 = env.Sky.Settings.skyColorExponent;
                    skyTint0 = env.Sky.Settings.skyColorTint;
                    sun0 = env.Lighting.Settings.directLightIntensityModifier;
                    skyLight0 = env.Lighting.Settings.ambientIntensityModifier;
                    shadow0 = env.Lighting.Settings.shadowIntensity;
                    fog0 = env.Fog.Settings.fogDensity;
                    fogTint0 = env.Fog.Settings.fogColorMod;
                    flatClouds0 = env.FlatClouds != null && env.FlatClouds.settings.useFlatClouds;
                    cloudDensity0 = env.FlatClouds != null ? env.FlatClouds.settings.flatCloudsDensity : 0f;
                    cloudSwitch0 = new bool[Levels(env)];
                    for (int i = 0; i < cloudSwitch0.Length; i++)
                    {
                        Enviro.EnviroQuality level = Level(env, i);
                        cloudSwitch0[i] = level != null && level.flatCloudsOverride != null && level.flatCloudsOverride.flatClouds;
                    }
                    lastWeight = -1f;
                    Plugin.L.LogInfo("Winter Walk: read the game's sky settings");
                }

                Vector3 p = cam.transform.position;
                float dx = Mathf.Max(0f, Mathf.Max(X0 - p.x, p.x - X1)), dz = Mathf.Max(0f, Mathf.Max(Z0 - p.z, p.z - Z1));
                float weight = Plugin.Enabled.Value ? 1f - Smooth(Mathf.Sqrt(dx * dx + dz * dz) / FadeDistance) : 0f;
                if (Mathf.Abs(weight - lastWeight) < 0.002f) return;      // nothing to change: leave the sky alone
                lastWeight = weight;

                env.Sky.Settings.skyColorExponent = Mathf.Lerp(skyExponent0, SkyExponent, weight);
                env.Sky.Settings.skyColorTint = Color.Lerp(skyTint0, SkyTint, weight);
                env.Lighting.Settings.directLightIntensityModifier = Mathf.Lerp(sun0, SunStrength, weight);
                env.Lighting.Settings.ambientIntensityModifier = Mathf.Lerp(skyLight0, SkyLight, weight);
                env.Lighting.Settings.shadowIntensity = Mathf.Lerp(shadow0, ShadowStrength, weight);
                env.Fog.Settings.fogDensity = Mathf.Lerp(fog0, FogDensity, weight);
                env.Fog.Settings.fogColorMod = Color.Lerp(fogTint0, FogTint, weight);
                // The thin cloud layer: where the game has it off, switch it on and fade it in by its density
                if (env.FlatClouds != null && !flatClouds0)
                {
                    bool on = weight > 0.004f;
                    for (int i = 0; i < cloudSwitch0.Length; i++)
                    {
                        Enviro.EnviroQuality level = Level(env, i);
                        if (level != null && level.flatCloudsOverride != null) level.flatCloudsOverride.flatClouds = on || cloudSwitch0[i];
                    }
                    env.FlatClouds.settings.flatCloudsDensity = on ? CloudDensity * weight : cloudDensity0;
                }
            }
            catch (Exception e)
            {
                // The sky system is not there between walks; anything else is worth one line, not one per frame
                if (lastWeight != -2f) { Plugin.L.LogWarning("Winter Walk: sky not adjusted: " + e.GetType().Name + ": " + e.Message); lastWeight = -2f; }
            }
        }
    }
}
