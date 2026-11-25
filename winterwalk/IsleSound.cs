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
// Loudness (the user, 2026-11-24, an hour after the first version: "the ocean sounds are now EXTREMELY loud compared to
// the base game"). The first version wrote the sea's share (0..1) straight into every beach source's ScriptableVolume,
// which is the game's own mix for that source: at "full" all thirteen loops played at 1, everywhere, the mainland
// included. Now a source's own value is read before it is first touched and only ever multiplied (never raised above
// it), nothing at all is touched until the sea has to be turned down, and the bus is turned down in its own units
// (a value at or below 0 is taken for decibels and lowered, never scaled towards 0 dB).
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
        public static float Shore = 3f;        // metres above the sea: below this the sea is at its loudest on the island (ShoreLevel)
        public static float Inland = 11f;      // and from here up it is silent
        public static float ShoreLevel = 0.35f; // the sea's loudness at the island's own water's edge (live setting)
        public static float TestSea = -1f;
        // The bus is left alone unless this is set (live, for tests): it is an asset the whole game shares and that
        // outlives the walk, its units are not known (its value is logged once), and the sources alone are enough
        public static bool UseBus;
        public static string State = "not started";
        public static float Sea = 1f;          // the sea's loudness now, 0..1

        static IntPtr playerSeen = IntPtr.Zero;
        static AmbiencePlayer player;
        static AudioVolume busVolume;
        static float bus0 = 1f, applied = -1f, nextLook, nextApply;
        static bool busFailed, sourcesFailed, told;
        static int sources;
        // each beach source's own volume, as the game had it before this first touched it (by the source's pointer)
        static readonly System.Collections.Generic.Dictionary<IntPtr, float> own = new System.Collections.Generic.Dictionary<IntPtr, float>();

        // Once a frame from SkyDriver: weight is how far the camera is on the island (0..1), y its height
        internal static void Tick(float weight, float y, float now)
        {
            if (Off) { if (applied >= 0f && applied < 0.999f) Apply(1f, now, true); return; }
            float inland = Inland > Shore ? (y - Shore) / (Inland - Shore) : 1f;
            inland = inland < 0f ? 0f : inland > 1f ? 1f : inland;
            // only well onto the island: the winter's weight is already above nothing 170 m outside the island's box,
            // which reaches the mainland's shore by the bridge, where the game's own surf belongs (at the spawn rooms
            // the sea was at 0.78 with the plain weight)
            // (0.9, not 0.6: at the intro pavilion on the mainland, 60 m from the bridge, the sea was already at 0.49)
            float isle = weight <= 0.9f ? 0f : (weight - 0.9f) / 0.1f;
            // On the island's own shore the game takes the listener for one standing in open sea and plays every beach
            // loop at once, far louder than any beach of the base island (the user, 2026-11-24: quiet inland is right,
            // "it just shouldn't be so loud near the sea"): so even at the water's edge it is held to ShoreLevel there.
            float want = TestSea >= 0f ? Math.Min(1f, TestSea) : 1f - isle * (1f - ShoreLevel * (1f - inland));
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
                try
                {
                    if (busVolume != null && UseBus)
                        busVolume.Value = bus0 > 0f ? bus0 * sea                                               // a plain share
                            : sea <= 0.001f ? bus0 - 80f : bus0 + 20f * (float)Math.Log10(sea);                 // decibels: only ever down
                }
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
            State = "sea at " + sea.ToString("0.00") + "; bus " + (busFailed ? "failed" : !UseBus ? "left alone" : busVolume != null ? "set" : "none") + ", " + (sourcesFailed ? "sources failed" : sources + " beach sources set");
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
            IntPtr key = source.Pointer;
            if (!own.TryGetValue(key, out float mine)) { mine = source.ScriptableVolume; own[key] = mine; }
            source.ScriptableVolume = mine * sea;
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
                own.Clear();
                try
                {
                    AmbienceSoundData data = player.SoundData;
                    AudioBus bus = data != null ? data.OceanBus : null;
                    busVolume = bus != null ? bus.BusVolume : null;
                    // the bus is an asset that outlives the walk: its own volume is the one it had when first seen
                    if (busVolume != null && applied < 0f) { bus0 = busVolume.Value; Plugin.L.LogInfo("Winter Walk: the game's ocean bus volume is " + bus0.ToString("0.###")); }
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
