// The sea is not heard all over the Winter Isle.
//
// The game's sea sound (AmbiencePlayer: beach loops close, surrounding and far, a rumble, waves and rocky water) is
// worked out from a map of the base island's sea (AmbienceSampleData.OceanDepthImage and its samples). The Winter Isle
// lies outside that map, where everything counts as open sea, so the surf played at full strength everywhere on it,
// summit and mine included (the user, 2026-11-24: "the ocean sound effect is present everywhere on the island due to
// its placement in the game world").
//
// So on the island the sea's loudness follows the listener's height: full on the beaches (below Shore metres), gone
// by Inland metres up, and faded in with the island's winter (SkyDriver's weight: 0 off the island, 1 on it). It is
// turned down in two places, either of which is enough: the ocean bus's volume (AmbienceSoundData.OceanBus.BusVolume)
// and the beach sources' own script volume (AudioSourceController.ScriptableVolume). The wind is left as it is.
//
// The game's names are used directly; where one is missing or throws, that way is given up with a line in the log.
// Nothing here was heard in the game when it was written (2026-11-24, the user was in a walk): check State.
//
// For tests: get?path=WinterWalk.IsleSound.State ; Off (leave the game's sound alone), Shore, Inland, and
// TestSea (0..1: hold the sea at this loudness; -1 lets go).
using System;
using UnityEngine;

namespace WinterWalk
{
    internal static class IsleSound
    {
        public static bool Off;
        public static float Shore = 3f;        // metres above the sea: below this the sea is at full loudness
        public static float Inland = 11f;      // and from here up it is silent
        public static float TestSea = -1f;
        public static string State = "not started";
        public static float Sea = 1f;          // the sea's loudness now, 0..1

        static IntPtr playerSeen = IntPtr.Zero;
        static AmbiencePlayer player;
        static AudioVolume busVolume;
        static float bus0 = 1f, applied = -1f, nextLook, nextApply;
        static bool busFailed, sourcesFailed, told;
        static int sources;

        // Once a frame from SkyDriver: weight is how far the camera is on the island (0..1), y its height
        internal static void Tick(float weight, float y, float now)
        {
            if (Off) { if (applied >= 0f && applied < 0.999f) Apply(1f, now, true); return; }
            float inland = Inland > Shore ? (y - Shore) / (Inland - Shore) : 1f;
            inland = inland < 0f ? 0f : inland > 1f ? 1f : inland;
            float want = TestSea >= 0f ? Math.Min(1f, TestSea) : 1f - weight * inland;
            // glide, so walking up from a beach does not switch the surf off
            float step = Time.unscaledDeltaTime * 0.6f;
            Sea = want > Sea ? Math.Min(want, Sea + step) : Math.Max(want, Sea - step);
            bool changed = Math.Abs(Sea - applied) > 0.01f || (Sea != applied && (Sea <= 0f || Sea >= 1f));
            if (!changed && now < nextApply) return;      // set again twice a second, in case the game sets its own
            if (applied < 0f && Sea >= 0.999f) return;    // never touched, nothing to do: leave the game alone
            Apply(Sea, now, changed);
        }

        static void Apply(float sea, float now, bool changed)
        {
            nextApply = now + 0.5f;
            if (!Find(now)) return;
            applied = sea;
            if (!busFailed)
                try { if (busVolume != null) busVolume.Value = bus0 * sea; }
                catch (Exception e) { busFailed = true; Plugin.L.LogWarning("Winter Walk: the ocean bus's volume could not be set (" + e.GetType().Name + ": " + e.Message + ")"); }
            if (!sourcesFailed)
                try
                {
                    sources = 0;
                    Quiet(player._beachCloseASCs, sea);
                    Quiet(player._beachSurroundedASCs, sea);
                    Quiet(player._beachFarASCs, sea);
                    Quiet(player._beachRumbleASC, sea);
                    var waves = player._beachWavesASCs;
                    if (waves != null) foreach (var pair in waves) Quiet(pair.Value, sea);
                    var rocky = player._rockyWaterASCs;
                    if (rocky != null) foreach (var pair in rocky) Quiet(pair.Value, sea);
                }
                catch (Exception e) { sourcesFailed = true; Plugin.L.LogWarning("Winter Walk: the beach sounds' own volume could not be set (" + e.GetType().Name + ": " + e.Message + ")"); }
            State = "sea at " + sea.ToString("0.00") + "; bus " + (busFailed ? "failed" : busVolume != null ? "set" : "none") + ", " + (sourcesFailed ? "sources failed" : sources + " beach sources set");
            if (!told && sea < 0.5f)
            {
                told = true;
                Plugin.L.LogInfo("Winter Walk: the sea's sound is turned down inland on the isle (" + State + ")");
            }
        }

        static void Quiet(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AudioSourceController> list, float sea)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++) Quiet(list[i], sea);
        }

        static void Quiet(AudioSourceController source, float sea)
        {
            if (source == null) return;
            source.ScriptableVolume = sea;
            sources++;
        }

        // The game's ambience player and its ocean bus (a new walk makes a new player)
        static bool Find(float now)
        {
            if (player != null && player.Pointer == playerSeen) return true;
            if (now < nextLook) return false;
            nextLook = now + 2f;
            try
            {
                player = AmbiencePlayer.Instance;
                if (player == null) { playerSeen = IntPtr.Zero; State = "no ambience player yet"; return false; }
                playerSeen = player.Pointer;
                busVolume = null; busFailed = false; sourcesFailed = false;
                try
                {
                    AmbienceSoundData data = player.SoundData;
                    AudioBus bus = data != null ? data.OceanBus : null;
                    busVolume = bus != null ? bus.BusVolume : null;
                    // the bus is an asset that outlives the walk: its own volume is the one it had when first seen
                    if (busVolume != null && applied < 0f) bus0 = busVolume.Value;
                }
                catch (Exception e) { busFailed = true; Plugin.L.LogWarning("Winter Walk: the game's ocean bus was not found (" + e.GetType().Name + ": " + e.Message + ")"); }
                return true;
            }
            catch (Exception e)
            {
                player = null; playerSeen = IntPtr.Zero;
                State = "no ambience player: " + e.GetType().Name + ": " + e.Message;
                return false;
            }
        }
    }
}
