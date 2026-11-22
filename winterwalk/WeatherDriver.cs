// Winter Walk: the Winter Isle's weather.
//
// The island is cloudy, and its weather turns with the time of day: freezing mist at dawn, a clearer, brighter spell
// about midday, snow thickening towards evening, a heavy still night. On top of that come blizzards, as a Poisson
// process: on average one every hour and a half of play (config BlizzardMeanMinutes), each lasting six to eleven minutes,
// setting in over a minute and blowing itself out over two. In a blizzard the fog closes in, the light dims, and a
// second, driving snowfall from the island's pack ("Blizzard Snowfall") is switched on.
//
// Nothing is sent between players: the blizzards are worked out from the network clock (Mirror's NetworkTime, the
// same on every player's game), minute by minute from a hash of the minute's number, so everyone is in the same
// storm. SkyDriver reads Mist, Cloud, Gloom and Blizzard from here and blends them in with the island's sky.
using System;
using UnityEngine;

namespace WinterWalk
{
    public class WeatherDriver : MonoBehaviour
    {
        public WeatherDriver(IntPtr ptr) : base(ptr) { }

        public static float Night;           // 0 day .. 1 night: how far the island's lights are lit
        public static float ForceNight = -1f;      // for tests: 0..1 holds the lights there, -1 lets go
        public static float Blizzard;        // 0 none .. 1 full
        public static float Mist;            // 0..1: how much the fog is thickened by the hour
        public static float Cloud;           // 0..1: extra cloud by the hour
        public static float Gloom;           // 0..1: how much the sun is dimmed
        public static float ForceBlizzard = -1f;      // for testing: 0..1 holds the blizzard at that strength (set?path=WinterWalk.WeatherDriver.ForceBlizzard&value=1)
        public static string State = "not loaded";

        const float Slot = 60f;              // seconds: a blizzard may begin at the start of any minute
        GameObject snowfall, blizzardSnow;
        float nextLook;

        static float Hash(long n, int salt)
        {
            unchecked
            {
                ulong x = (ulong)n * 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL;
                x ^= x >> 31; x *= 0x94D049BB133111EBUL; x ^= x >> 29; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 32;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // How hard it is blowing at 'time' seconds on the shared clock
        public static float BlizzardAt(double time)
        {
            float mean = Mathf.Max(1f, Plugin.BlizzardMean.Value) * 60f;
            float chance = Mathf.Clamp01(Slot / mean);      // each minute's chance of a blizzard beginning: Poisson arrivals, to the minute
            long now = (long)Math.Floor(time / Slot);
            float strength = 0f;
            for (long slot = now - 12; slot <= now; slot++)      // none lasts longer than eleven minutes
            {
                if (slot < 1 || Hash(slot, 1) >= chance) continue;
                float began = slot * Slot, lasts = Mathf.Lerp(360f, 660f, Hash(slot, 2));
                float into = (float)(time - began);
                if (into < 0f || into > lasts) continue;
                strength = Mathf.Max(strength, Smooth(into / 60f) * Smooth((lasts - into) / 120f));
            }
            return strength;
        }

        float nextWeather;

        void Update()
        {
            using var diag = Diag.Time("weather");
            try
            {
                if (Time.unscaledTime < nextWeather) return;      // five times a second: the weather turns slowly
                nextWeather = Time.unscaledTime + 0.2f;
                Enviro.EnviroManager env = Enviro.EnviroManager.instance;
                if (env == null || env.Time == null) { State = "no sky"; return; }
                float hour = env.Time.Settings.timeOfDay;
                // by the hour: mist thickest about six in the morning and again at dusk, least at one in the afternoon
                float dawn = Mathf.Exp(-Mathf.Pow(Mathf.DeltaAngle(hour * 15f, 6.5f * 15f) / 30f, 2f));
                float dusk = Mathf.Exp(-Mathf.Pow(Mathf.DeltaAngle(hour * 15f, 18.5f * 15f) / 34f, 2f));
                float night = Smooth((Mathf.Abs(Mathf.DeltaAngle(hour * 15f, 180f)) - 95f) / 40f);      // 1 in the small hours
                Mist = Mathf.Clamp01(dawn * 0.9f + dusk * 0.5f + night * 0.35f);
                Cloud = Mathf.Clamp01(0.35f + dusk * 0.5f + night * 0.4f + dawn * 0.3f);

                double clock = 0;
                try { clock = Mirror.NetworkTime.time; } catch (Exception) { clock = Time.unscaledTimeAsDouble; }
                Blizzard = ForceBlizzard >= 0f ? Mathf.Clamp01(ForceBlizzard) : BlizzardAt(clock);
                Gloom = Mathf.Clamp01(Blizzard * 0.75f + Mist * 0.15f);
                Shader.SetGlobalFloat("_BwmBlizzard", Blizzard);
                // how far the island's lights are lit: up through dusk (17 to 19 h), down through dawn (5.5 to 7.5 h), and
                // half lit in a blizzard's gloom. The light strings, the fog's glow and the lights' pools on the snow read it.
                float lit = ForceNight >= 0f ? Mathf.Clamp01(ForceNight) : Mathf.Max(Mathf.Max(Smooth((hour - 17f) / 2f), 1f - Smooth((hour - 5.5f) / 2f)), Blizzard * 0.5f);
                Night = lit;
                Shader.SetGlobalFloat("_BwmNight", lit);

                // the driving snow: on the island only (it follows the camera as the snowfall does), not underground
                if (Time.unscaledTime >= nextLook && (snowfall == null || blizzardSnow == null))
                {
                    nextLook = Time.unscaledTime + 3f;
                    snowfall = GameObject.Find("Snowfall");
                    Transform parent = snowfall != null ? snowfall.transform.parent : null;
                    Transform b = parent != null ? parent.Find("Blizzard Snowfall") : null;
                    blizzardSnow = b != null ? b.gameObject : null;
                }
                if (blizzardSnow != null)
                {
                    bool on = Blizzard > 0.12f && !MineDriver.Underground;
                    if (blizzardSnow.activeSelf != on) blizzardSnow.SetActive(on);
                }
                State = (Blizzard > 0.01f ? "blizzard " + Blizzard.ToString("0.00") : "no blizzard") + ", mist " + Mist.ToString("0.00") + ", cloud " + Cloud.ToString("0.00") + " at " + hour.ToString("0.0") + " h";
            }
            catch (Exception e)
            {
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: weather stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
