// Winter Walk's help for the puzzle SDK: lookups it is slow at that we can answer, so the frame that builds every puzzle
// on joining a walk is shorter. Nothing here changes what the SDK builds: only how quickly it finds the game's objects.
using System;
using HarmonyLib;

namespace WinterWalk
{
    internal static class SdkSpeedups
    {
        // The SDK's "Circle" lamp (PuzzleLightType.Circle) has no known place in the world (GameRefs.Light.ExactPath gives
        // none), so every one was found by going through every lamp in the game, our own lamp copies too: 13 ms each and
        // slower as copies add lamps. Our puzzles have 40 of them, half a second of the build frame (2026-11-23). The search
        // always finds the train's flood light at this place, so the SDK is told it and looks it up directly (and keeps
        // it for the next lamp). Where it is not, the SDK searches as before.
        internal const string CircleLamp = "TrainSystem/TrainNew/TrainCarNew B(5)/CarriageBody/CarriageContentsFront/TrainLightFlood";

        internal static void Apply()
        {
            try
            {
                Type light = AccessTools.TypeByName("BigPuzzleSDK.GameRefs+Light");
                var target = light != null ? AccessTools.Method(light, "ExactPath") : null;
                if (target == null) { Plugin.L.LogWarning("Winter Walk: the SDK's lamp places were not found (another SDK version?): its Circle lamps are found by searching"); return; }
                new Harmony("bwm.winterwalk.sdk").Patch(target, postfix: new HarmonyMethod(typeof(SdkSpeedups), nameof(ExactPathPostfix)));
                Plugin.L.LogInfo("Winter Walk: the SDK looks its Circle lamps up at " + CircleLamp);
            }
            catch (Exception e) { Plugin.L.LogWarning("Winter Walk: the SDK's lamp lookup was not sped up: " + e.GetType().Name + ": " + e.Message); }
        }

        static void ExactPathPostfix(BigPuzzleSDK.PuzzleLightType kind, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) && kind == BigPuzzleSDK.PuzzleLightType.Circle) __result = CircleLamp;
        }
    }
}
