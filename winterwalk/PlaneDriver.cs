// Winter Walk: the biplanes.
//
// The island's pack parks biplanes in the airport's hangar on the south-east shore and on the summit's airstrip
// ("Biplanes/Biplane Stand N", each with its model as "Model", whose propeller is "Propeller"). Once the airport
// tower's five plinths ("Airport Plinth 1".."5", The Tree's pack) all hold a gourd, the hangar's doors ("Airport
// Gates") go and walking up to a parked plane's cockpit boards it: the parked model is hidden on this PC and a copy
// of it flies with the player.
//
// Flying: hold forward (the move stick, or W) to open the throttle; the plane turns and climbs towards where the
// camera looks. On the ground it taxis, and lifts off once it is fast enough and the camera looks up. Let go of
// forward to cruise; hold back to slow down (on the ground, to brake). Land by flying low and slow over level ground,
// stop, and press jump to climb out: the plane stays parked where it stopped, for anyone on this PC to take again.
// Fly into the ground or a mountain too fast and the flight is over where it struck.
//
// Only this player is driven, through its own Rigidbody, as the skiing and the jetpack are; the game's networking
// shows the player flying to the others, but not the plane (nothing tells one PC what another flies), and the
// parked planes stay at their stands on other PCs.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterWalk
{
    public class PlaneDriver : MonoBehaviour
    {
        public PlaneDriver(IntPtr ptr) : base(ptr) { }

        const float SeatHeight = 1.55f;     // the player's origin above the ground while the plane stands on its wheels
        const float BoardReach = 3.2f;      // how near the cockpit a player must come to board
        const float TopSpeed = 34f, Cruise = 24f, SlowFlight = 15f, Stall = 12f, Takeoff = 17f;
        const float Accel = 5f, Brake = 6f;
        const float TurnRate = 55f, PitchRate = 32f, MaxPitch = 32f;

        public static bool ForceOpen;       // for testing: board without the airport tower's gourds
        public static bool Open;            // read-only: whether the planes can be flown
        public static bool Flying;          // read-only: whether this player is in a plane
        public static float Speed;          // read-only: airspeed, metres a second
        public static string State = "not loaded";
        // for testing without a controller: board the nearest plane within 60 m, hold the throttle, steer to a yaw
        // and pitch (degrees; NaN follows the camera), climb out
        public static bool TestBoard, TestLeave;
        public static float TestThrottle, TestYaw = float.NaN, TestPitch = float.NaN;

        readonly List<GameObject> parked = new List<GameObject>();      // planes that can be boarded (the stands' models, then any left where they landed)
        GameObject flown, flownFrom, gates;
        bool found;
        Transform propeller;
        float nextLook, nextPlinthCheck, yaw, pitch, bank, slowFor, leftAt = -10f;
        bool wasJump, towerComplete;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        int jumpAction = -2, forwardAction = -2;
        float nextAlive;

        void Find()
        {
            parked.Clear();
            found = false;
            // a new walk: nothing of the walk before is kept. Until 2026-11-24 the tower's plinths and the hangar's
            // doors were kept for the whole game session, so after hosting a second time the driver watched five
            // plinths that no longer existed ("0 of 5 gourds" for ever) and doors that were gone: the user's five
            // gourds on the airport tower opened nothing.
            plinths.Clear(); towerComplete = false; nextPlinthCheck = 0f;
            gates = null;
            if (flown != null) { try { UnityEngine.Object.Destroy(flown); } catch (Exception) { } flown = null; }
            GameObject planes = GameObject.Find("Biplanes");
            if (planes == null) return;
            for (int i = 0; i < planes.transform.childCount; i++)
            {
                Transform stand = planes.transform.GetChild(i);
                if (!stand.name.StartsWith("Biplane Stand")) continue;
                Transform model = stand.Find("Model");
                if (model != null) parked.Add(model.gameObject);
            }
            if (gates == null) gates = GameObject.Find("Airport Gates");
            found = parked.Count > 0;
        }

        void FindActions()
        {
            jumpAction = -1; forwardAction = -1;
            try
            {
                // (the action list is an IL2CPP collection that does not walk from here: ask for each id instead)
                for (int id = 0; id < 256; id++)
                {
                    Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                    if (a == null || a.name == null) continue;
                    string n = a.name.ToLowerInvariant().Replace(" ", "");
                    if (jumpAction < 0 && n.Contains("jump")) jumpAction = a.id;
                    if (forwardAction < 0 && !n.StartsWith("ui") && (n == "movey" || n == "movevertical" || n == "vertical" || n.Contains("forward"))) forwardAction = a.id;      // (the game's is "moveY"; "UIVertical" is the menus')
                }
            }
            catch (Exception) { }
            Plugin.L.LogInfo("Winter Walk: planes read jump from " + (jumpAction >= 0 ? "Rewired action " + jumpAction : "the space bar") + ", the throttle from " + (forwardAction >= 0 ? "Rewired action " + forwardAction : "W and S"));
        }

        bool Jump()
        {
            try
            {
                if (jumpAction >= 0) { Rewired.Player p = Rewired.ReInput.players.GetPlayer(0); if (p != null) return p.GetButton(jumpAction); }
            }
            catch (Exception) { jumpAction = -1; }
            try { return Input.GetKey(KeyCode.Space); } catch (Exception) { return false; }
        }

        float Forward()
        {
            if (TestThrottle != 0f) return TestThrottle;
            try
            {
                if (forwardAction >= 0) { Rewired.Player p = Rewired.ReInput.players.GetPlayer(0); if (p != null) return p.GetAxis(forwardAction); }
            }
            catch (Exception) { forwardAction = -1; }
            try { return (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f); } catch (Exception) { return 0f; }
        }

        bool AirportOpen()
        {
            if (Time.unscaledTime < nextPlinthCheck) return towerComplete;
            nextPlinthCheck = Time.unscaledTime + 1f;
            plinths.RemoveAll(p => p == null);      // a new walk builds the packs again: plinths of the walk before are gone
            if (plinths.Count < 5)
            {
                plinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Airport Plinth " + i);
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p != null ? p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>() : null;
                    if (plinth != null) plinths.Add(plinth);
                }
            }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths) if (Towers.Filled(plinth)) filled++;
            towerComplete = plinths.Count == 5 && filled == 5;
            if (!towerComplete) State = plinths.Count < 5 ? "waiting: the airport tower shows " + plinths.Count + " of 5 plinths (the towers show one plinth per puzzle loaded, The Tree's and the mine's first)" : "waiting: " + filled + " of 5 gourds on the airport tower";
            return towerComplete;
        }

        // the height of whatever is under 'p' (ignoring the player), as a distance below p; 99 if nothing within 40 m
        static float Gap(Vector3 p, Transform me)
        {
            float gap = 99f;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(me)) continue;
                gap = Mathf.Min(gap, hit.distance - 0.5f);
            }
            return gap;
        }

        void Board(Transform player, GameObject plane)
        {
            flownFrom = plane;
            flown = Instantiate((UnityEngine.Object)plane).Cast<GameObject>();      // a copy of this very plane, without its colliders
            foreach (Collider c in flown.GetComponentsInChildren<Collider>(true)) Destroy(c);
            flown.SetActive(true);
            propeller = flown.transform.Find("Propeller");
            yaw = plane.transform.eulerAngles.y;
            pitch = 0f; bank = 0f; Speed = 0f; slowFor = 0f;
            plane.SetActive(false);
            parked.Remove(plane);
            Flying = true;
            Vector3 seat = plane.transform.position + Vector3.up * SeatHeight;
            Diag.Touched("plane");
            player.position = seat;
            PlayerCharacter pc = player.GetComponent<PlayerCharacter>();
            if (pc != null && pc.rb != null) { pc.rb.position = seat; pc.rb.linearVelocity = Vector3.zero; }
            Plugin.L.LogInfo("Winter Walk: boarded a biplane at " + plane.transform.position);
        }

        void Leave(Transform player, string why)
        {
            Flying = false;
            if (flown != null)
            {
                // the plane stays where it stopped, level, for anyone to take again
                flown.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (propeller != null) propeller.localRotation = Quaternion.identity;
                parked.Add(flown);
            }
            if (flownFrom != null && flownFrom != flown) { /* the stand's own model stays hidden on this PC: its plane is the one just left */ }
            flown = null; flownFrom = null; propeller = null;
            Speed = 0f;
            leftAt = Time.unscaledTime;
            // out beside the cockpit, on the left
            Vector3 side = Quaternion.Euler(0f, yaw, 0f) * Vector3.left;
            Diag.Touched("plane");
            player.position = player.position + side * 2.2f;
            State = "climbed out (" + why + ")";
            Plugin.L.LogInfo("Winter Walk: left a biplane: " + why);
        }

        void FixedUpdate()
        {
            using var diag = Diag.Time("plane");
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { State = "no player"; return; }
                // the parked planes are the pack's: when they are gone (the packs were built again), look again
                if (found && Time.unscaledTime >= nextAlive)
                {
                    nextAlive = Time.unscaledTime + 1f;
                    if (parked.Count == 0 || parked[0] == null) { found = false; Open = false; }
                }
                if (!found)
                {
                    if (Time.unscaledTime < nextLook || !Setup.Turn()) return;
                    nextLook = Time.unscaledTime + 3f;
                    Find();
                    if (!found) { State = "no planes in the loaded packs"; return; }
                }
                if (jumpAction == -2) FindActions();
                Transform t = me.transform;
                PlayerCharacter pc = me.GetComponent<PlayerCharacter>();
                Rigidbody body = pc != null ? pc.rb : null;
                if (body == null) { State = "no body"; return; }
                bool jump = Jump();
                bool jumpPressed = jump && !wasJump;
                wasJump = jump;
                Open = ForceOpen || AirportOpen();
                if (ForceOpen) State = "open (forced open for testing)";
                if (gates != null && gates.activeSelf == Open) gates.SetActive(!Open);

                if (!Flying)
                {
                    // (A plane anyone comes upon can be flown, whatever the towers hold: the gourds only open the hangar's
                    // doors, which is what keeps the planes behind them out of reach. The user, 2026-11-24.)
                    // board the plane whose cockpit the player walks up to (not straight after climbing out of it)
                    GameObject nearest = null; float best = TestBoard ? 60f : BoardReach;
                    foreach (GameObject plane in parked)
                    {
                        if (plane == null) continue;
                        float d = (plane.transform.position + Vector3.up * 1.2f - t.position).magnitude;
                        if (d < best) { best = d; nearest = plane; }
                    }
                    if (nearest != null && (TestBoard || Time.unscaledTime - leftAt > 4f)) { TestBoard = false; Board(t, nearest); }
                    else State = "open: " + parked.Count + " planes parked";
                    return;
                }
                if (flown == null) { Flying = false; return; }

                float dt = Time.fixedDeltaTime;
                float gap = Gap(t.position, t) - SeatHeight;      // the wheels' height above what is under them
                bool onGround = gap < 0.35f;
                float throttle = Forward();
                // steering: towards where the camera looks
                Camera cam = Camera.main;
                float wantYaw = yaw, wantPitch = 0f;
                if (cam != null)
                {
                    Vector3 look = cam.transform.forward;
                    wantYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                    wantPitch = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg * 1.3f + 6f;      // the camera looks a little down on the player
                }
                if (!float.IsNaN(TestYaw)) wantYaw = TestYaw;
                if (!float.IsNaN(TestPitch)) wantPitch = TestPitch;
                wantPitch = Mathf.Clamp(wantPitch, -MaxPitch, MaxPitch);
                // the throttle
                float target = throttle > 0.3f ? TopSpeed : throttle < -0.3f ? (onGround ? 0f : SlowFlight) : (onGround ? 0f : Cruise);
                Speed = Mathf.MoveTowards(Speed, target, (target > Speed ? Accel : Brake) * dt);
                // turning: a ground turn is slow and only while rolling; in the air the plane banks into it
                float turn = Mathf.DeltaAngle(yaw, wantYaw);
                float rate = onGround ? TurnRate * 0.6f * Mathf.Clamp01(Speed / 6f) : TurnRate;
                float step = Mathf.Clamp(turn, -rate * dt, rate * dt);
                yaw += step;
                bank = Mathf.Lerp(bank, onGround ? 0f : Mathf.Clamp(-turn * 0.9f, -40f, 40f), dt * 3f);
                // pitch: level on the ground until fast enough to lift off and the camera looks up
                if (onGround && (Speed < Takeoff || wantPitch < 4f)) wantPitch = Mathf.Max(0f, Mathf.Min(wantPitch, 0f));
                pitch = Mathf.MoveTowards(pitch, wantPitch, PitchRate * dt);
                Quaternion heading = Quaternion.Euler(-pitch, yaw, 0f);
                Vector3 v = heading * Vector3.forward * Speed;
                if (onGround && pitch <= 0.5f)
                    v.y = Mathf.Clamp((0f - gap) * 6f, -3f, 3f);      // roll along on the wheels
                else if (Speed < Stall)
                    v.y -= (Stall - Speed) * 0.8f;                  // too slow to fly: sinking
                // flown into something: when the body cannot go where it is sent, the flight ends there
                if (!onGround && Speed > 10f && body.linearVelocity.magnitude < Speed * 0.35f) slowFor += dt; else slowFor = 0f;
                if (slowFor > 0.4f) { body.linearVelocity = Vector3.zero; Leave(t, "struck something at speed"); return; }
                Diag.Touched("plane");
                body.linearVelocity = v;
                // the plane round the player: its wheels SeatHeight below them, nose along the heading, banked
                flown.transform.position = t.position - Vector3.up * SeatHeight;
                flown.transform.rotation = Quaternion.Euler(-pitch, yaw, bank);
                if (propeller != null) propeller.localRotation = Quaternion.Euler(0f, 0f, Time.time * (300f + Speed * 40f) % 360f);
                State = (onGround ? "taxiing" : "flying") + " at " + Speed.ToString("0") + " m/s, " + (onGround ? "" : gap.ToString("0") + " m up, ") + "heading " + Mathf.Repeat(yaw, 360f).ToString("0");
                // climbing out: stopped on the ground, jump
                if ((jumpPressed || TestLeave) && onGround && Speed < 3f) { TestLeave = false; Leave(t, "landed"); }
            }
            catch (Exception e)
            {
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: planes stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
