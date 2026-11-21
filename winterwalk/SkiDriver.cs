// Winter Walk: skiing and snowboarding on the great mountain's pistes.
//
// The game moves its players itself, so a slippery surface does nothing. This glides this PC's own player
// instead. Walking up to a rack ("Ski Rack" / "Board Rack", by the gondola's top station) puts skis or a board on:
// a model from the island's pack ("Gear Models") is hung under the player. On a piste (the unseen "Piste Sheet"
// from the pack) a player with gear on runs downhill: gravity along the slope speeds them up, they steer by
// where they look, turning across the fall line scrubs speed, and looking uphill brakes. Skis turn faster and
// run faster; a board carves wider. Off the piste the gear comes off after a few seconds.
//
// Only this player is driven, through its own Rigidbody; the game's own networking shows the movement to the
// others. Other players' gear is not shown yet.
using System;
using UnityEngine;

namespace WinterWalk
{
    public class SkiDriver : MonoBehaviour
    {
        public SkiDriver(IntPtr ptr) : base(ptr) { }

        public static int Gear;                 // 0 none, 1 skis, 2 snowboard (the companion can set it: set?path=WinterWalk.SkiDriver.Gear&value=1)
        public static string State = "not loaded";
        public static float Speed;              // metres a second, for reading
        public static bool Straight;            // for testing without a hand on the camera: steer straight down the fall line

        const float BodyHeight = 0.75f;         // a standing player's origin above the ground
        GameObject rackSki, rackBoard, modelSki, modelBoard, worn;
        int wornGear;
        float nextLook, offPisteSince = -1f;
        Vector3 velocity;
        bool wasOn, airborne;
        float airSince;

        // every rack on the island: the first of each found by name, the rest beside it under the same parent
        // ("Ski Rack 2", "Board Rack 3", ...: by the gondola's top station, the ski lift's two stations, the lodge)
        readonly System.Collections.Generic.List<Vector3> skiRacks = new System.Collections.Generic.List<Vector3>(), boardRacks = new System.Collections.Generic.List<Vector3>();

        void Find()
        {
            rackSki = GameObject.Find("Ski Rack");
            rackBoard = GameObject.Find("Board Rack");
            skiRacks.Clear(); boardRacks.Clear();
            Transform parent = rackSki != null ? rackSki.transform.parent : rackBoard != null ? rackBoard.transform.parent : null;
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform c = parent.GetChild(i);
                    if (c.name.StartsWith("Ski Rack")) skiRacks.Add(c.position);
                    else if (c.name.StartsWith("Board Rack")) boardRacks.Add(c.position);
                }
            GameObject models = GameObject.Find("Gear Models");
            if (models != null)
            {
                Transform s = models.transform.Find("Ski Model"), b = models.transform.Find("Board Model");
                modelSki = s != null ? s.gameObject : null;
                modelBoard = b != null ? b.gameObject : null;
            }
        }

        void Wear(Transform player)
        {
            if (wornGear == Gear && (worn != null || Gear == 0)) return;
            if (worn != null) Destroy(worn);
            worn = null;
            wornGear = Gear;
            GameObject model = Gear == 1 ? modelSki : Gear == 2 ? modelBoard : null;
            if (model == null) return;
            worn = Instantiate((UnityEngine.Object)model, player).Cast<GameObject>();
            worn.transform.localPosition = new Vector3(0f, -BodyHeight + 0.03f, 0f);
            worn.transform.localRotation = Quaternion.identity;
            worn.SetActive(true);
        }

        void FixedUpdate()
        {
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { State = "no player"; return; }
                if (Time.unscaledTime >= nextLook && (rackSki == null || modelSki == null))
                {
                    nextLook = Time.unscaledTime + 2f;
                    Find();
                }
                Transform t = me.transform;
                Vector3 p = t.position;
                // the racks
                foreach (Vector3 r in skiRacks) if ((r - p).sqrMagnitude < 2.5f * 2.5f) Gear = 1;
                foreach (Vector3 r in boardRacks) if ((r - p).sqrMagnitude < 2.5f * 2.5f) Gear = 2;
                Wear(t);
                if (Gear == 0) { State = "no gear"; wasOn = false; return; }

                // on a piste? (the nearest sheet below; far down it only counts while flying off a jump)
                bool on = false;
                Vector3 normal = Vector3.up;
                float gap = 0f, nearest = float.MaxValue;
                foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || hit.collider.name != "Piste Sheet" || hit.distance >= nearest) continue;
                    nearest = hit.distance; on = true; normal = hit.normal; gap = hit.distance - 0.5f - BodyHeight;
                }
                if (on && gap > 2.7f && !wasOn && !airborne) on = false;      // (a sheet far below someone walking: not riding)
                // In the air: off a kicker's lip (or a roller taken fast) the snow falls away below. Keep the speed, let
                // gravity have it, until the snow comes back up to meet them (or four seconds pass)
                PlayerCharacter flyer = (airborne || (wasOn && on && gap > 0.6f)) ? me.GetComponent<PlayerCharacter>() : null;
                if (flyer != null && flyer.rb != null && (!on || gap > 0.25f) && (airborne ? Time.unscaledTime - airSince < 4f : true))
                {
                    if (!airborne) { airborne = true; airSince = Time.unscaledTime; }
                    velocity += Vector3.down * (9.81f * Time.fixedDeltaTime);
                    flyer.rb.linearVelocity = velocity;
                    Speed = velocity.magnitude;
                    State = (Gear == 2 ? "boarding" : "skiing") + ": in the air";
                    return;
                }
                if (airborne) { airborne = false; if (on) velocity = Vector3.ProjectOnPlane(velocity, normal) * 0.9f; }      // landed
                if (!on)
                {
                    if (offPisteSince < 0f) offPisteSince = Time.unscaledTime;
                    if (Time.unscaledTime - offPisteSince > 6f) { Gear = 0; State = "gear off: left the piste"; }
                    else State = "off the piste";
                    wasOn = false;
                    Speed = 0f;
                    return;
                }
                offPisteSince = -1f;
                PlayerCharacter pc = me.GetComponent<PlayerCharacter>();
                Rigidbody body = pc != null ? pc.rb : null;
                if (body == null) { State = "no body"; return; }
                if (!wasOn) velocity = Vector3.ProjectOnPlane(body.linearVelocity, normal);
                wasOn = true;

                bool board = Gear == 2;
                float dt = Time.fixedDeltaTime;
                Vector3 fall = Vector3.ProjectOnPlane(Vector3.down, normal);      // down the slope, its length the sine of the slope
                velocity = Vector3.ProjectOnPlane(velocity, normal);
                velocity += fall * (9.81f * dt);
                // steer: turn what speed there is towards where the player looks, along the slope
                Camera cam = Camera.main;
                Vector3 look = cam != null && !Straight ? Vector3.ProjectOnPlane(cam.transform.forward, normal) : fall;
                float speed = velocity.magnitude;
                if (look.sqrMagnitude > 1e-4f && speed > 0.3f)
                {
                    look.Normalize();
                    Vector3 heading = velocity / speed;
                    float turn = (board ? 0.9f : 1.5f) * dt;
                    Vector3 turned = Vector3.RotateTowards(heading, look, turn, 0f);
                    float scrub = Vector3.Angle(heading, turned) * Mathf.Deg2Rad * (board ? 0.1f : 0.16f);      // carving costs a little
                    float brake = Mathf.Clamp01(-Vector3.Dot(look, fall.normalized) - 0.55f) * 9f * dt;          // looking well up the hill
                    speed = Mathf.Max(0f, speed * (1f - scrub) - brake);
                    velocity = turned * speed;
                }
                // drag, and a top speed
                speed = velocity.magnitude;
                float top = board ? 23f : 27f;
                speed = Mathf.Min(top, speed - (0.25f + 0.012f * speed * speed) * dt);
                if (speed < 0f) speed = 0f;
                velocity = velocity.sqrMagnitude > 1e-6f ? velocity.normalized * speed : Vector3.zero;
                Speed = speed;
                // keep to the snow: close the gap to the sheet a little each step
                body.linearVelocity = velocity + Vector3.down * Mathf.Clamp(gap * 6f, -2f, 6f);
                State = (board ? "boarding " : "skiing ") + speed.ToString("0.0") + " m/s";
            }
            catch (Exception e)
            {
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: skiing stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
