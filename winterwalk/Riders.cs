// Winter Walk: riding the island's vehicles on foot.
//
// The game's own train carries whoever stands in it. Ours (the mantrips, the trains' cars, the gondola's cabins) are
// set down at a new place every frame by their drivers, and a player standing in one was not carried: the floor slid
// from under them and the back wall shoved them along, off the end, or into the tunnel's wall, where they hung
// without ground under them and could not jump (players, 2026-11-24: "players get stuck (example: mantrip)"; the test
// player left behind by a mantrip ended wedged at -575.6, 12.5, -749.6). Seats were never the trouble: a seated
// player is held by the game's own pose and rides anything.
//
// So this PC's player, while the thing under their feet is one of our vehicles, is moved each frame by exactly what
// that vehicle moved, as the game's train does, and stays with it through a jump (for Hold seconds off its floor, while
// still over it). A mantrip's tunnel is lower than a standing player: anyone standing in one when it sets off is sat
// in a free seat of it, by the same call a click on the seat makes.
// Each PC carries only its own player; the others see them through the game's ordinary position updates, a little
// behind the vehicle at speed. Seated players are exact, as before.
using System;
using System.Reflection;
using UnityEngine;

namespace WinterWalk
{
    public class Riders : MonoBehaviour
    {
        public Riders(IntPtr ptr) : base(ptr) { }

        public static bool Off;                 // live, for tests
        public static float Hold = 1.2f;        // seconds a rider stays with a vehicle after their feet leave it
        public static float Reach = 9f;         // metres from the vehicle's own origin within which someone can be aboard it
        public static string State = "not started";
        public static int Carried, Seated;      // frames a player was carried; players sat down as a mantrip set off

        static readonly string[] Groups = { "Mine Trips", "Trains", "Gondola Cabins", "Ski Lift Chairs", "Summit Lift Chairs" };

        PlayerCharacter player;
        float nextPlayer;
        IntPtr colliderSeen = IntPtr.Zero;      // the ground collider last looked up, and what it belongs to
        Transform colliderVehicle;
        Transform vehicle;                      // the one being ridden
        bool mantrip;
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool haveLast;
        float offSince = -1f, speed, seatTriedAt = -10f;
        static MethodInfo enterPose;

        // The vehicle a collider belongs to: the object straight under one of our vehicle groups, or null
        static Transform VehicleOf(Collider c)
        {
            Transform t = c.transform;
            for (int up = 0; up < 6 && t != null; up++)
            {
                Transform parent = t.parent;
                if (parent == null) return null;
                string name = parent.name;
                for (int g = 0; g < Groups.Length; g++) if (name == Groups[g]) return t;
                t = parent;
            }
            return null;
        }

        // Sits the player in a free seat of the mantrip, by the game's own command (what a click on the seat sends)
        void SitDown(PlayerCharacter pc, Transform trip)
        {
            seatTriedAt = Time.unscaledTime;
            PlayerNetworking net = pc.playerNetworking;
            if (net == null) return;
            Vector3 at = pc.transform.position;
            PlayerPose best = null; float bestD = float.MaxValue;
            int n = trip.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform seat = trip.GetChild(i);
                if (!seat.name.StartsWith("Seat ", StringComparison.Ordinal)) continue;
                Transform poseT = seat.Find("pose");
                PlayerPose pose = poseT != null ? poseT.GetComponent<PlayerPose>() : null;
                if (pose == null || pose.occupant != null) continue;
                float d = (seat.position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pose; }
            }
            if (best == null) { Plugin.L.LogInfo("Winter Walk: riders: no free seat for the player standing in " + trip.name); return; }
            if (enterPose == null)
                foreach (MethodInfo m in typeof(PlayerNetworking).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (m.Name == "CmdEnterPose" && m.GetParameters().Length == 2) { enterPose = m; break; }
            if (enterPose == null) { Plugin.L.LogWarning("Winter Walk: riders: the game has no CmdEnterPose to sit a player with"); return; }
            int action = 1;
            try { action = net.poseReference.actionNumber + 1; } catch (Exception) { }
            enterPose.Invoke(net, new object[] { best.shellReference, action });
            Seated++;
            Plugin.L.LogInfo("Winter Walk: riders: sat the player standing in " + trip.name + " down as it set off");
        }

        void LateUpdate()
        {
            using var diag = Diag.Time("riders");
            try
            {
                float now = Time.unscaledTime;
                if (Off) { vehicle = null; haveLast = false; State = "off"; return; }
                if (player == null || now >= nextPlayer)
                {
                    nextPlayer = now + 1f;
                    Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                    player = me != null ? me.GetComponent<PlayerCharacter>() : null;
                    if (player == null) { vehicle = null; haveLast = false; colliderSeen = IntPtr.Zero; State = "not in a walk"; return; }
                }
                PlayerGround ground = player.ground;
                if (ground == null) return;
                Collider under = ground.groundCollider;
                Transform on = null;
                if (under != null)
                {
                    if (under.Pointer != colliderSeen) { colliderSeen = under.Pointer; colliderVehicle = VehicleOf(under); }
                    on = colliderVehicle;
                }
                else colliderSeen = IntPtr.Zero;

                if (on != null)
                {
                    if (vehicle == null || on.Pointer != vehicle.Pointer) { vehicle = on; haveLast = false; mantrip = on.name.StartsWith("Mantrip", StringComparison.Ordinal); }
                    offSince = -1f;
                }
                else if (vehicle != null)
                {
                    // feet off it: stay with it through a jump, but not once standing on something else or the time is up
                    if (offSince < 0f) offSince = now;
                    if (under != null || now - offSince > Hold) { vehicle = null; haveLast = false; }
                }
                if (vehicle == null) { State = "watching"; return; }

                Vector3 position = vehicle.position;
                Quaternion rotation = vehicle.rotation;
                bool sitting = false;
                try { sitting = player.poser != null && player.poser.currentPose != null; } catch (Exception) { }
                if (haveLast && !sitting)
                {
                    Vector3 at = player.transform.position;
                    // only someone still at the vehicle: a player set down elsewhere (a teleport, a rescue) within the hold
                    // was swung about the far-off vehicle as it turned (the test player ended 28 m from where it was put)
                    if ((at - lastPosition).sqrMagnitude > Reach * Reach) { vehicle = null; haveLast = false; colliderSeen = IntPtr.Zero; State = "watching"; return; }
                    Vector3 moved = position + rotation * (Quaternion.Inverse(lastRotation) * (at - lastPosition)) - at;
                    float step = moved.magnitude, dt = Math.Max(Time.unscaledDeltaTime, 1e-4f);
                    speed = (position - lastPosition).magnitude / dt;
                    // (a vehicle set down somewhere else altogether, as when a line opens, is not a ride: let it go)
                    if (step >= 4f) { vehicle = null; haveLast = false; colliderSeen = IntPtr.Zero; State = "watching"; return; }
                    if (step > 1e-5f)
                    {
                        Vector3 to = at + moved;
                        Diag.Touched("riding");
                        player.transform.position = to;
                        Rigidbody body = player.rb;
                        if (body != null) body.position = to;
                        Carried++;
                    }
                    if (mantrip && speed > 0.4f && speed < 60f && now - seatTriedAt > 1.5f) SitDown(player, vehicle);
                    State = "riding " + vehicle.name + " at " + speed.ToString("0.0") + " m/s";
                }
                else State = (sitting ? "seated in " : "aboard ") + vehicle.name;
                lastPosition = position; lastRotation = rotation; haveLast = true;
            }
            catch (Exception e)
            {
                if (!State.StartsWith("stopped")) Plugin.L.LogWarning("Winter Walk: riders: " + e.GetType().Name + ": " + e.Message);
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                vehicle = null; haveLast = false; player = null; nextPlayer = Time.unscaledTime + 5f;
            }
        }
    }
}
