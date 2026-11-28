// While Winter Walk drives this PC's player (skis, a board, a biplane), the game's own mover is held off.
//
// The game's PlayerMover sets a standing player's speed itself every physics step, from the stick: whatever a driver
// wrote into the Rigidbody was gone a step later. Skiing and flying were only ever tried with the test player, who had
// been teleported, and a teleported player is one the game takes for sitting (PlayerSitter.isSittingLocal), whom the
// mover leaves to the physics. So for the test player the skis ran and the planes flew, and for every real player,
// who walks up on their feet, the skis did nothing and the plane was left behind at once ("skis and snowboards don't
// work at all", "players immediately fall out of planes when they try to fly them": the user, 2026-11-25).
//
// A driver calls Take every step it drives and Give when it stops. Take switches the mover's own fixed step off
// (PlayerMover.bypassFixedUpdate, a switch of the game's) and, for a pilot, sits the player down, which is also
// how a pilot should look; Give puts both back.
//
// For tests: get?path=WinterWalk.Driven.By ; NoBypass (leave the mover running), SkiSit (sit skiers down too, the
// state all the old tests ran in).
using System;

namespace WinterWalk
{
    internal static class Driven
    {
        public static string By = "";
        public static bool NoBypass, SkiSit;

        static PlayerCharacter held;
        static bool sat;

        internal static void Take(PlayerCharacter pc, string who, bool sit)
        {
            if (pc == null) return;
            if (By != who || held == null || held.Pointer != pc.Pointer)
            {
                if (By != "" && By != who) Give(By);
                By = who; held = pc; sat = false;
                Diag.Event(who + ": the game's mover is held off while Winter Walk moves the player");
            }
            try
            {
                PlayerMover mover = pc.mover;
                if (mover != null && mover.bypassFixedUpdate == NoBypass) mover.bypassFixedUpdate = !NoBypass;
            }
            catch (Exception) { }
            if (!sit) return;
            try
            {
                PlayerSitter sitter = pc.sitter;
                if (sitter != null && !sitter.isSittingLocal) { sitter.SetSittingLocal(true); sat = true; }
            }
            catch (Exception) { }
        }

        internal static void Give(string who)
        {
            if (By != who) return;
            By = "";
            PlayerCharacter pc = held;
            held = null;
            if (pc == null) return;
            try { if (pc.mover != null) pc.mover.bypassFixedUpdate = false; } catch (Exception) { }
            if (sat) try { if (pc.sitter != null && pc.sitter.isSittingLocal) pc.sitter.SetSittingLocal(false); } catch (Exception) { }
            sat = false;
            Diag.Event(who + ": the player's movement is the game's again");
        }
    }
}
