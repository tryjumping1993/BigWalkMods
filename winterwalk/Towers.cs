// Whether a tower's plinth holds a gourd, for the drivers that open the gondola, the mine, the airport and the railway.
//
// A plinth counts as filled when a gourd sits in it, however that came about (the user, 2026-11-24: "It shouldn't
// matter where the plinths came from; if the players fill them with gourds, they are satisfied"): the SDK's own flag
// (PuzzleGourdPlinth.HasGourd, which it sets when it sees a gourd go in, or when a save puts one back), or the game's
// slot itself holding a prop right now (PropHome.pinnedProp, through the SDK's internal Home), which also covers a
// plinth whose flag was missed.
using System;
using System.Reflection;
using HarmonyLib;

namespace WinterWalk
{
    internal static class Towers
    {
        static PropertyInfo home;
        static bool looked;

        internal static bool Filled(BigPuzzleSDK.PuzzleGourdPlinth plinth)
        {
            if (plinth == null) return false;
            try { if (plinth.HasGourd) return true; } catch (Exception) { }
            try
            {
                PropHome slot = Slot(plinth);
                return slot != null && slot.pinnedProp != null;
            }
            catch (Exception) { return false; }
        }

        // the game's slot behind a plinth (the SDK's internal Home), or null
        internal static PropHome Slot(BigPuzzleSDK.PuzzleGourdPlinth plinth)
        {
            if (!looked) { looked = true; home = AccessTools.Property(typeof(BigPuzzleSDK.PuzzleGourdPlinth), "Home"); }
            return home != null && plinth != null ? home.GetValue(plinth) as PropHome : null;
        }
    }
}
