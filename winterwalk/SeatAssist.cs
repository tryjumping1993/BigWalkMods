// Sitting down without having to find a seat's click box.
//
// A seat of ours (the puzzle SDK's PuzzleSeat) is clicked on an unseen box, 0.6 to 0.7 m across, that sits on the seat.
// The game's reach finds it only in a narrow band: measured at a chair in Cast Iron Pizza from 0.45 to 0.9 m, only
// with the view 17 to 54 degrees down; from 0.25 m only straight down; never looking level at the chair's back, and
// never looking steeply down at the seat from beside it (the view meets the floor first). The test player's clicks
// were aimed by a script that swept the view until the box was found, so they always sat; players who walked up to a
// chair and pressed use mostly got nothing ("players still can't sit in seats", host too: the user, 2026-11-25).
//
// So: when this PC's player presses use, the game's own reach has found nothing to use (no target, no item), their
// hands are empty and they are not seated, the nearest free seat within Reach of the camera that they are roughly
// looking at (or are almost on top of) takes them, by the same command a click on its box sends (CmdEnterPose, as
// Riders uses for the mantrips). A click that does find the box is the game's own business and is left alone.
//
// For tests: get?path=WinterWalk.SeatAssist.State ; Off ; Reach, Near, Cone ; TestUse (as if use were pressed).
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WinterWalk
{
    internal static class SeatAssist
    {
        public static bool Off, TestUse;
        public static float Reach = 1.8f;      // metres from the camera to the seat
        public static float Near = 0.9f;       // this close, anywhere in front counts
        public static float Cone = 0.45f;      // otherwise the seat must be within this cosine of the view (63 degrees)
        public static string State = "not started";
        public static int Sat;

        static readonly List<PlayerPose> seats = new List<PlayerPose>();
        static float nextLook;
        static int useAction = -1;
        static float nextAction;
        static bool told;
        static bool wasUse;
        static MethodInfo enterPose;

        static void FindSeats()
        {
            seats.Clear();
            foreach (BigPuzzleSDK.PuzzleSeat seat in UnityEngine.Object.FindObjectsOfType<BigPuzzleSDK.PuzzleSeat>())
            {
                if (seat == null) continue;
                Transform poseT = seat.transform.Find("pose");
                PlayerPose pose = poseT != null ? poseT.GetComponent<PlayerPose>() : null;
                if (pose != null) seats.Add(pose);
            }
            State = seats.Count + " seats known, " + Sat + " players sat down";
        }

        static bool Use()
        {
            // (asked for only in a walk, and again every few seconds until found: at the title screen, where this first
            // ran, the game's input has no actions yet, and the helper gave up for the whole session)
            if (useAction < 0 && Time.unscaledTime >= nextAction)
            {
                nextAction = Time.unscaledTime + 5f;
                try
                {
                    for (int id = 0; id < 256; id++)
                    {
                        Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                        if (a != null && a.name == "use") { useAction = a.id; break; }
                    }
                }
                catch (Exception) { }
                if (useAction >= 0) Plugin.L.LogInfo("Winter Walk: seats read use from Rewired action " + useAction);
                else if (!told) { told = true; Plugin.L.LogInfo("Winter Walk: seats: the game has no input action named use yet (asking again)"); }
            }
            if (useAction < 0) return false;
            try { Rewired.Player p = Rewired.ReInput.players.GetPlayer(0); return p != null && p.GetButton(useAction); }
            catch (Exception) { return false; }
        }

        // Once a frame (SkyDriver.Update)
        internal static void Tick(float now)
        {
            if (Off) return;
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { wasUse = false; return; }
                bool use = Use();
                bool pressed = (use && !wasUse) || TestUse;
                wasUse = use;
                if (!pressed) return;
                TestUse = false;
                PlayerCharacter pc = me != null ? me.GetComponent<PlayerCharacter>() : null;
                Camera cam = Camera.main;
                if (pc == null || cam == null) return;
                if (pc.poser != null && pc.poser.currentPose != null) return;                 // seated already
                PlayerCaster caster = pc.caster;
                if (caster != null && (caster.castableTarget != null || caster.castProp != null)) return;      // the game has something to use
                if (pc.hands != null && pc.hands.heldProp != null) return;                    // use is for what they hold
                if ((seats.Count == 0 || seats[0] == null) && now >= nextLook) { nextLook = now + 3f; FindSeats(); }
                if (seats.Count == 0) return;
                Vector3 eye = cam.transform.position, look = cam.transform.forward;
                float ex = eye.x, ey = eye.y, ez = eye.z, lx = look.x, ly = look.y, lz = look.z;
                PlayerPose best = null; float bestScore = float.MaxValue;
                foreach (PlayerPose pose in seats)
                {
                    if (pose == null) continue;
                    Vector3 p = pose.transform.position;
                    float dx = p.x - ex, dy = p.y + 0.2f - ey, dz = p.z - ez;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > Reach * Reach) continue;
                    float d = (float)Math.Sqrt(d2);
                    float cos = d > 0.01f ? (dx * lx + dy * ly + dz * lz) / d : 1f;
                    if (cos < (d < Near ? 0f : Cone)) continue;
                    if (pose.occupant != null) continue;
                    float score = d * (2f - cos);
                    if (score < bestScore) { bestScore = score; best = pose; }
                }
                if (best == null) return;
                PlayerNetworking net = pc.playerNetworking;
                if (net == null) return;
                if (enterPose == null)
                    foreach (MethodInfo m in typeof(PlayerNetworking).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        if (m.Name == "CmdEnterPose" && m.GetParameters().Length == 2) { enterPose = m; break; }
                if (enterPose == null) { Off = true; Plugin.L.LogWarning("Winter Walk: seats: the game has no CmdEnterPose to sit a player with"); return; }
                int action = 1;
                try { action = net.poseReference.actionNumber + 1; } catch (Exception) { }
                enterPose.Invoke(net, new object[] { best.shellReference, action });
                Sat++;
                State = seats.Count + " seats known, " + Sat + " players sat down";
                Diag.Event("seats: use pressed with nothing under the crosshair, sat the player in " + best.transform.parent.name);
            }
            catch (Exception e)
            {
                Off = true;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: seat help stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
