// A joining friend gets the puzzles as they stand, a few at a time.
//
// The puzzle SDK's values (every lamp, door, display, bar and lobby group of every puzzle) reach other PCs only when
// they change, so after a player joins the host sends all of them again (SyncedValue.ResendAll, at 3, 6, 10, 16, 25,
// 40 and 60 s after the join). It does that in one frame, and the game puts a frame's changes in one network message.
// With the island's puzzles that message is 68,000 bytes and the game sends at most 64,350: the host's log said
//   NetworkConnection.ValidatePacketSize: cannot send packet larger than 64350 bytes, was 68000 bytes
//   ArgumentException: GetBatch needs a fresh writer!
// seven times for every join (2026-11-24, three players), and nothing of it arrived. A friend who joined therefore
// saw every puzzle as it is built, not as it stood on the host: lamps that were lit dark, groups for another number of
// players showing, doors as they started. Changes made after the join did arrive, so it looked like puzzles that
// half work.
//
// Here the SDK's "send all" is taken over: the same values are sent again by the SDK's own Resend(), a share of them
// every Step seconds (a sixteenth, at least Least), so each message is a few KB. A request that comes while a round
// is under way starts one more round after it.
//
// It hangs on the SDK mod's internals (SyncedValue.ResendAll / all / Resend / oneShot), found by name: with another
// SDK version they may be gone, and then the log says so and the SDK sends as before.
//
// For tests: get?path=WinterWalk.JoinFlood.State ; set?path=WinterWalk.JoinFlood.Off&value=true gives the SDK its own
// way back; set?path=WinterWalk.JoinFlood.TestSend&value=true starts a round as a join would.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WinterWalk
{
    internal static class JoinFlood
    {
        public static bool Off;
        public static bool TestSend;
        public static float Step = 0.15f;      // seconds between shares
        public static int Shares = 16;         // a round is sent in this many shares
        public static int Least = 40;          // values in a share at least
        public static string State = "not applied";

        static FieldInfo allField, oneShotField;
        static MethodInfo resend, resendAll;
        static readonly List<object> queue = new List<object>();
        static int at, share, rounds, sent, failed;
        static bool again, sending, told;
        static float next;

        internal static void Apply()
        {
            try
            {
                Type value = AccessTools.TypeByName("BigPuzzleSDK.SyncedValue");
                resendAll = value != null ? AccessTools.Method(value, "ResendAll") : null;
                resend = value != null ? AccessTools.Method(value, "Resend") : null;
                allField = value != null ? AccessTools.Field(value, "all") : null;
                oneShotField = value != null ? AccessTools.Field(value, "oneShot") : null;
                if (resendAll == null || resend == null || allField == null)
                {
                    State = "not applied: the SDK's synced values were not found (ResendAll " + (resendAll != null) + ", Resend " + (resend != null) + ", all " + (allField != null) + ")";
                    Plugin.L.LogWarning("Winter Walk: " + State + ": a joining player is sent every puzzle's state in one message, which may be too big to send");
                    return;
                }
                new Harmony("bwm.winterwalk.joinflood").Patch(resendAll, prefix: new HarmonyMethod(typeof(JoinFlood), nameof(BeforeResendAll)));
                State = "applied, nothing sent yet";
                Plugin.L.LogInfo("Winter Walk: a joining player is sent the puzzles' state in shares, not in one message");
            }
            catch (Exception e)
            {
                State = "not applied: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: the puzzles' state is sent to joining players as the SDK does it: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // SyncedValue.ResendAll(): ours instead (false: the SDK's own does not run)
        static bool BeforeResendAll()
        {
            if (Off || sending) return true;      // sending: our own call of the SDK's (never made, but never loop)
            Ask();
            return false;
        }

        static void Ask()
        {
            if (queue.Count > 0) { again = true; return; }
            try
            {
                var all = allField.GetValue(null) as IEnumerable;
                if (all == null) return;
                foreach (object v in all)
                {
                    if (v == null) continue;
                    if (oneShotField != null && oneShotField.GetValue(v) is bool once && once) continue;
                    queue.Add(v);
                }
            }
            catch (Exception e)
            {
                queue.Clear();
                if (!told) { told = true; Plugin.L.LogWarning("Winter Walk: the puzzles' state could not be listed for a joining player: " + e.GetType().Name + ": " + e.Message); }
                return;
            }
            at = 0;
            share = Math.Max(Least, (queue.Count + Shares - 1) / Math.Max(1, Shares));
            next = 0f;
            rounds++;
            if (rounds <= 3 || rounds % 20 == 0)
                Plugin.L.LogInfo("Winter Walk: sending the puzzles' state again for a joining player: " + queue.Count + " values, " + share + " every " + Step.ToString("0.00") + " s (round " + rounds + ")");
        }

        // Once a frame (SkyDriver.Update)
        internal static void Tick(float now)
        {
            if (TestSend) { TestSend = false; if (allField != null) Ask(); }
            if (queue.Count == 0 || now < next) return;
            next = now + Step;
            int end = Math.Min(queue.Count, at + share);
            for (; at < end; at++)
            {
                // SyncedValue.Resend(): the host sends this value again (it does nothing on another PC, or for a value
                // never sent)
                try { resend.Invoke(queue[at], null); sent++; }
                catch (Exception e)
                {
                    failed++;
                    if (!told) { told = true; Exception inner = e.InnerException ?? e; Plugin.L.LogWarning("Winter Walk: a puzzle value could not be sent again: " + inner.GetType().Name + ": " + inner.Message); }
                }
            }
            if (at < queue.Count) return;
            queue.Clear();
            State = "applied; " + rounds + " rounds, " + sent + " values sent again" + (failed > 0 ? ", " + failed + " failed" : "");
            if (again) { again = false; Ask(); }
        }
    }
}
