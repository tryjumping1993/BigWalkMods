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
    [BepInDependency("markviews.BigPuzzleSDK")]      // the gondola reads The Tree's plinths
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Tracks;
        internal static ConfigEntry<float> BlizzardMean;
        internal const float RockSnowRaise = 0.2f;      // rocks keep more bare granite than the list's snow line says

        public override void Load()
        {
            L = Log;
            Enabled = Config.Bind("General", "WinterSky", true, "Blend the sky and light towards winter near the Winter Isle.");
            ClassInjector.RegisterTypeInIl2Cpp<SkyDriver>();
            Tracks = Config.Bind("General", "SnowTracks", true, "Players leave tracks in the Winter Isle's snow.");
            ClassInjector.RegisterTypeInIl2Cpp<Dresser>();
            ClassInjector.RegisterTypeInIl2Cpp<SnowTrails>();
            ClassInjector.RegisterTypeInIl2Cpp<SkiDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<GondolaDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<MineDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<TrainDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<PlaneDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<RoomLights>();
            ClassInjector.RegisterTypeInIl2Cpp<DinoDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<JetpackDriver>();
            BlizzardMean = Config.Bind("General", "BlizzardMeanMinutes", 90f, "Blizzards arrive at random on the Winter Isle; this is the average wait between them, in minutes of play.");
            ClassInjector.RegisterTypeInIl2Cpp<WeatherDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<MapDriver>();
            var go = new GameObject("WinterWalk");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<SkyDriver>();
            go.AddComponent<Dresser>();
            go.AddComponent<SnowTrails>();
            go.AddComponent<SkiDriver>();
            go.AddComponent<GondolaDriver>();
            go.AddComponent<GondolaDriver>().ConfigureLift("Ski Lift Line", "Ski Lift Chairs", "Ski Lift Chair ", 1.6f, 9f);      // the chairlift from the lodge
            go.AddComponent<GondolaDriver>().ConfigureLift("Summit Lift Line", "Summit Lift Chairs", "Summit Lift Chair ", 1.6f, 5f);      // the summit lift, from the railway's Summit station to the crest
            go.AddComponent<MineDriver>();
            go.AddComponent<TrainDriver>();
            go.AddComponent<PlaneDriver>();
            go.AddComponent<RoomLights>();
            go.AddComponent<DinoDriver>();
            go.AddComponent<JetpackDriver>();
            go.AddComponent<WeatherDriver>();
            go.AddComponent<MapDriver>();
            L.LogInfo("Winter Walk loaded");
        }
    }

    public class SkyDriver : MonoBehaviour
    {
        public SkyDriver(IntPtr ptr) : base(ptr) { }

        // The island's box in the world comes from the dressing list (Dresser.X0...), which the island's build writes
        const float FadeDistance = 170f;      // metres outside the box over which winter fades out

        // Winter's values
        const float SkyExponent = 0.55f;
        // (an overcast island: a greyer sky, a weaker sun and a thick cloud layer; WeatherDriver moves these by the hour)
        static readonly Color SkyClear = new Color(0.8f, 0.84f, 0.92f, 1f);
        const float SunBright = 0.72f, SkyLight = 1.6f, ShadowStrength = 0.6f;
        const float FogBase = 2.9f;
        const float CloudBase = 2.1f;      // the cloud layer at full winter (the game's own value is 1.51)
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
                float dx = Mathf.Max(0f, Mathf.Max(Dresser.X0 - p.x, p.x - Dresser.X1)), dz = Mathf.Max(0f, Mathf.Max(Dresser.Z0 - p.z, p.z - Dresser.Z1));
                float weight = Plugin.Enabled.Value ? 1f - Smooth(Mathf.Sqrt(dx * dx + dz * dz) / FadeDistance) : 0f;
                // the island's weather (WeatherDriver): mist and cloud by the hour, and blizzards
                float mist = WeatherDriver.Mist, cloud = WeatherDriver.Cloud, gloom = WeatherDriver.Gloom, storm = WeatherDriver.Blizzard;
                float signature = weight + mist * 0.37f + cloud * 0.11f + storm * 1.7f;
                if (Mathf.Abs(signature - lastWeight) < 0.002f) return;      // nothing to change: leave the sky alone
                lastWeight = signature;
                float SunStrength = Mathf.Lerp(SunBright, 0.3f, gloom);
                float FogDensity = FogBase * (1f + mist * 0.9f + storm * 3.2f);
                float CloudDensity = Mathf.Lerp(CloudBase, CloudBase * 1.7f, Mathf.Max(cloud, storm));
                Color SkyTint = Color.Lerp(SkyClear, new Color(0.62f, 0.66f, 0.72f, 1f), Mathf.Max(cloud * 0.6f, storm));

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
