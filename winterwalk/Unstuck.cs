// Winter Walk's way out of a trap. A tester (2026-11-22) was wedged against one of the island's dressing rocks: the
// ground under them was too steep to stand on (the game gives no grip there and will not jump from it) and nothing
// they pressed moved them, so they had to quit the game. The base game has no such rocks in such numbers.
//
// So each PC watches its own player. When they have been pushing to move (or pressing jump) for StuckSeconds without
// getting anywhere, on ground the game will not let them walk or jump from (steep, or held up without being on the
// ground), they are set down on firm ground nearby: the game's own last safe position if it is near and not the same
// spot, else the nearest open, level spot within a few metres. Never while sitting, swimming, asleep, flying, skiing
// or being moved by another of Winter Walk's drivers (mantrips, lifts, planes, dinosaurs). Each rescue is logged.
//
// Under the island (the user, 2026-11-24: "you can sometimes get into environments where you're not supposed to and
// exist within the world geometry": they stood 32 m under the mountain's snow on the outside of a railway tunnel by the
// Summit station, seeing the sky through the ground's back). The island's ground is one sheet with nothing above it but
// air, so a player with the island's ground over their head and no roof of a tunnel, the mine or a building between
// them and it is somewhere no one should be, however they got there: after UnderSeconds they are set down on the
// snow above. The log says where they last stood in the open, which is where the way in is.
using System;
using System.Globalization;
using UnityEngine;

namespace WinterWalk
{
    public class Unstuck : MonoBehaviour
    {
        public Unstuck(IntPtr ptr) : base(ptr) { }

        public static float StuckSeconds = 3f;      // live setting
        public static bool Off;
        public static int Rescues;
        public static string State = "watching";
        public static bool TestRescue;              // for tests: rescue now, as if stuck

        int moveX = -2, moveY = -2, jump = -2;
        float next, stuckFor, lastRescue = -100f, jumpAt = -100f;
        Vector3 anchor;                              // where the player was when they started pushing without moving
        float anchorAt;
        Vector3 lastRescueAt = new Vector3(1e9f, 0f, 0f);
        int repeats;

        public static float UnderSeconds = 1.5f;    // live setting: this long under the island's ground, then up
        public static bool UnderOff;
        public static string UnderState = "";
        float nextUnder, underSince = -1f;
        Vector3 lastOpen;                            // where the player last stood with no ground over their head
        bool lastOpenKnown;

        // The island's ground above the player's head with no roof of ours in between: how high it is, or NaN.
        // (The ground's pieces are named "Island i-j"; rays only meet a mesh from its front, so from below the ground
        // itself is not met, while a tunnel's lining, the mine's rock and a building's ceiling, which face down, are.)
        static float GroundOverhead(Transform self, Vector3 pos, out string roof)
        {
            roof = "";
            float surface = float.NaN;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(pos.x, pos.y + 500f, pos.z), Vector3.down, 497.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || !hit.collider.name.StartsWith("Island ", StringComparison.Ordinal)) continue;
                if (float.IsNaN(surface) || hit.point.y > surface) surface = hit.point.y;
            }
            if (float.IsNaN(surface)) return surface;
            foreach (RaycastHit hit in Physics.RaycastAll(pos, Vector3.up, surface - pos.y + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider c = hit.collider;
                if (c == null || c.transform.IsChildOf(self) || c.name.StartsWith("Island ", StringComparison.Ordinal)) continue;
                if (c.transform.root.name == "Winter Dressing") continue;      // (a rock buried in the mountain is no roof)
                roof = c.name;
                return float.NaN;
            }
            return surface;
        }

        void Under(PlayerCharacter pc, Vector3 pos, bool sitting)
        {
            float now = Time.unscaledTime;
            if (now < nextUnder) return;
            nextUnder = now + 0.5f;
            bool onIsle = pos.x > Dresser.X0 && pos.x < Dresser.X1 && pos.z > Dresser.Z0 && pos.z < Dresser.Z1;
            if (UnderOff || !onIsle || sitting || PlaneDriver.Flying) { underSince = -1f; UnderState = ""; return; }
            Transform self = pc.transform.root;
            float surface = GroundOverhead(self, pos, out string roof);
            if (float.IsNaN(surface)) { underSince = -1f; UnderState = roof.Length > 0 ? "under " + roof : ""; if (roof.Length == 0) { lastOpen = pos; lastOpenKnown = true; } return; }
            if (underSince < 0f) underSince = now;
            UnderState = "under the island's ground by " + (surface - pos.y).ToString("0.0", CultureInfo.InvariantCulture) + " m";
            if (now - underSince < UnderSeconds) return;
            underSince = -1f;
            // up onto the snow above: there, or the nearest open level spot round it
            Vector3? to = Firm(self, pos.x, pos.z, surface);
            for (float r = 1.5f; r <= 12f && !to.HasValue; r += 1.5f)
                for (int k = 0; k < 12 && !to.HasValue; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    Vector3 around = new Vector3(pos.x + Mathf.Cos(a) * r, pos.y, pos.z + Mathf.Sin(a) * r);
                    float there = GroundOverhead(self, around, out _);
                    if (!float.IsNaN(there)) to = Firm(self, around.x, around.z, there);
                }
            if (!to.HasValue) to = new Vector3(pos.x, surface + 1.2f, pos.z);
            string under = "nothing";
            foreach (RaycastHit hit in Physics.RaycastAll(pos, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && !hit.collider.transform.IsChildOf(self)) { under = hit.collider.name; break; }
            Diag.Touched("unstuck");
            pc.transform.position = to.Value;
            if (pc.rb != null) { pc.rb.position = to.Value; pc.rb.linearVelocity = Vector3.zero; pc.rb.angularVelocity = Vector3.zero; }
            lastRescue = now; Rescues++;
            State = "rescued " + Rescues + " time(s)";
            Diag.Warn("unstuck: the player was UNDER THE ISLAND'S GROUND at " + Diag.V(pos) + ", " + (surface - pos.y).ToString("0.0", CultureInfo.InvariantCulture) + " m below the snow, standing on " + under
                + "; last in the open at " + (lastOpenKnown ? Diag.V(lastOpen) : "(not known)") + "; set down on the snow at " + Diag.V(to.Value));
        }

        static int Action(string word, bool exact)
        {
            try
            {
                for (int id = 0; id < 256; id++)
                {
                    Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                    if (a == null || a.name == null) continue;
                    string n = a.name.ToLowerInvariant();
                    if (exact ? n == word : n.Contains(word)) return a.id;
                }
            }
            catch (Exception) { }
            return -1;
        }

        void Update()
        {
            using var diag = Diag.Time("unstuck");
            try
            {
                if (Off) return;
                Rewired.Player input = null;
                try { input = Rewired.ReInput.players.GetPlayer(0); } catch (Exception) { }
                if (jump == -2 && input != null) { moveX = Action("movex", true); moveY = Action("movey", true); jump = Action("jump", false); }
                if (input != null && jump >= 0 && input.GetButtonDown(jump)) jumpAt = Time.unscaledTime;
                if (Time.unscaledTime < next) return;
                float dt = Time.unscaledTime - (next - 0.1f);
                next = Time.unscaledTime + 0.1f;

                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                PlayerCharacter pc = me != null ? me.GetComponent<PlayerCharacter>() : null;
                if (pc == null) { stuckFor = 0f; return; }
                Vector3 pos = pc.transform.position;
                if (TestRescue) { TestRescue = false; Rescue(pc, pos, "a test"); return; }

                PlayerGround ground = pc.ground;
                Rigidbody rb = pc.rb;
                bool sitting = pc.sitter != null && pc.sitter.isSittingLocal;
                Under(pc, pos, sitting);
                bool asleep = pc.sleeper != null && pc.sleeper.localIsSleeping;
                bool swimming = ground != null && ground.isSwimming;
                bool busy = sitting || asleep || swimming || PlaneDriver.Flying || JetpackDriver.Worn && rb != null && rb.linearVelocity.y > 0.5f || Diag.AnyTouch(3f);
                float push = 0f;
                if (input != null)
                {
                    if (moveX >= 0) push += Math.Abs(input.GetAxis(moveX));
                    if (moveY >= 0) push += Math.Abs(input.GetAxis(moveY));
                }
                bool wants = push > 0.5f || Time.unscaledTime - jumpAt < 1.5f;
                bool grounded = ground != null && ground.isGrounded;
                bool jumpable = ground != null && ground.isOnJumpableGround;
                float vy = rb != null ? rb.linearVelocity.y : 0f;
                bool trapped = grounded ? !jumpable : Math.Abs(vy) < 0.4f;      // too steep to use, or held up off the ground

                if (busy || !wants || !trapped) { stuckFor = 0f; anchor = pos; anchorAt = Time.unscaledTime; State = "watching"; return; }
                float dx = pos.x - anchor.x, dz = pos.z - anchor.z;
                if (dx * dx + dz * dz > 0.6f * 0.6f) { anchor = pos; anchorAt = Time.unscaledTime; stuckFor = 0f; return; }      // getting somewhere
                stuckFor = Time.unscaledTime - anchorAt;
                State = "pushing without moving for " + stuckFor.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                if (stuckFor >= StuckSeconds && Time.unscaledTime - lastRescue > 4f)
                    Rescue(pc, pos, grounded ? "wedged on ground too steep to walk or jump from" : "held up off the ground");
            }
            catch (Exception e)
            {
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: unstuck stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Firm, open, level-ish ground at (x, z) near height y: the point to stand on, or null
        static Vector3? Firm(Transform self, float x, float z, float y)
        {
            Vector3? best = null;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, y + 4f, z), Vector3.down, 9f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(self)) continue;
                if (hit.normal.y < 0.8f) continue;      // too steep to stand on
                if (best.HasValue && hit.point.y < best.Value.y) continue;
                best = hit.point;
            }
            if (!best.HasValue) return null;
            Vector3 stand = best.Value + Vector3.up * 0.95f;
            // room for the player there: nothing solid in a capsule from just above the ground to head height
            foreach (Collider c in Physics.OverlapCapsule(best.Value + Vector3.up * 0.45f, best.Value + Vector3.up * 1.5f, 0.32f, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && !c.transform.IsChildOf(self)) return null;
            return stand;
        }

        void Rescue(PlayerCharacter pc, Vector3 pos, string why)
        {
            Transform self = pc.transform.root;
            bool again = (pos - lastRescueAt).sqrMagnitude < 25f && Time.unscaledTime - lastRescue < 30f;
            repeats = again ? repeats + 1 : 0;
            Vector3? to = null;
            string how = "";
            // the game's own last safe position, if it is near and not this spot
            if (pc.ground != null && repeats == 0)
            {
                Vector3 safe = pc.ground.lastSafePosition;
                float d = (safe - pos).magnitude;
                if (d > 1f && d < 40f) { to = Firm(self, safe.x, safe.z, safe.y) ?? (Vector3?)(safe + Vector3.up * 0.3f); how = "the game's last safe spot " + d.ToString("0.0", CultureInfo.InvariantCulture) + " m away"; }
            }
            // else the nearest open level spot round about, farther out each time it happens again here
            if (!to.HasValue)
            {
                float reach = 2f + 2f * repeats;
                for (float r = 1.5f; r <= reach + 6f && !to.HasValue; r += 1.5f)
                    for (int k = 0; k < 12 && !to.HasValue; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        Vector3? f = Firm(self, pos.x + Mathf.Cos(a) * r, pos.z + Mathf.Sin(a) * r, pos.y);
                        if (f.HasValue) { to = f; how = "open ground " + r.ToString("0.0", CultureInfo.InvariantCulture) + " m away"; }
                    }
            }
            if (!to.HasValue) { to = pos + Vector3.up * 2f; how = "2 m up (no open ground found near)"; }
            Diag.Touched("unstuck");
            pc.transform.position = to.Value;
            if (pc.rb != null) { pc.rb.position = to.Value; pc.rb.linearVelocity = Vector3.zero; pc.rb.angularVelocity = Vector3.zero; }
            lastRescue = Time.unscaledTime; lastRescueAt = pos; stuckFor = 0f; anchor = to.Value; anchorAt = Time.unscaledTime;
            Rescues++;
            State = "rescued " + Rescues + " time(s)";
            Diag.Warn("unstuck: the player was " + why + " at " + Diag.V(pos) + "; set down on " + how + " at " + Diag.V(to.Value));
        }
    }
}
