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
        public static float OffPisteSeconds = 20f;      // gear comes off this long after leaving a piste (or a rack)
        public static bool Straight;           // for testing without a hand on the camera: steer straight down the fall line
        public static float PoleSpeed = 4f, PoleAccel = 3f;     // pushing the stick forward poles along the way the player looks, up to this speed
        public static float TestPush;          // for testing: the stick (1 forward)

        const float BodyHeight = 0.75f;         // a standing player's origin above the ground
        GameObject rackSki, rackBoard, modelSki, modelBoard, worn;
        int wornGear;
        float nextLook, offPisteSince = -1f;
        Vector3 velocity;
        bool wasOn, airborne, turned;
        // Skis and boards that are things (the summit's stand: items named "Skis n" and "Snowboard n", the user,
        // 2026-11-24: "intuitive to use as is most of the equipment in the base game"): pick a pair up and they are on
        // your feet for as long as you hold them, anywhere; put them down and they are off. In the hand they are not
        // drawn on this PC (the pair on the feet is).
        readonly System.Collections.Generic.List<Prop> gearProps = new System.Collections.Generic.List<Prop>();
        readonly System.Collections.Generic.List<int> gearKinds = new System.Collections.Generic.List<int>();
        float nextGearLook, nextCarryCheck;
        bool fromItem;
        Prop hidden;

        void FindGear()
        {
            gearProps.Clear(); gearKinds.Clear();
            foreach (BigPuzzleSDK.PuzzleItem item in FindObjectsOfType<BigPuzzleSDK.PuzzleItem>())
            {
                if (item == null) continue;
                string n = item.gameObject.name;
                int kind = n.StartsWith("Skis", StringComparison.Ordinal) ? 1 : n.StartsWith("Snowboard", StringComparison.Ordinal) ? 2 : 0;
                if (kind == 0) continue;
                Prop prop = DiscGolf.PropOf(item);
                if (prop != null) { gearProps.Add(prop); gearKinds.Add(kind); }
            }
        }

        static void Show(Prop prop, bool on)
        {
            if (prop == null) return;
            foreach (Component c in prop.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>(), true))
            {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r != null) r.enabled = on;
            }
        }

        // which gear this PC's player has in the hand (0 none, 1 skis, 2 a board)
        int Carried(Mirror.NetworkIdentity me)
        {
            PlayerCharacter pc = me.GetComponent<PlayerCharacter>();
            Prop held = pc != null && pc.hands != null ? pc.hands.heldProp : null;
            int kind = 0;
            if (held != null)
                for (int i = 0; i < gearProps.Count; i++)
                    if (gearProps[i] != null && gearProps[i].Pointer == held.Pointer) { kind = gearKinds[i]; break; }
            Prop want = kind != 0 ? held : null;
            if ((want != null ? want.Pointer : IntPtr.Zero) != (hidden != null ? hidden.Pointer : IntPtr.Zero))
            {
                try { Show(hidden, true); } catch (Exception) { }
                hidden = want;
                Show(hidden, false);
            }
            return kind;
        }
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
            using var diag = Diag.Time("ski");
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { State = "no player"; return; }
                if (Time.unscaledTime >= nextLook && (rackSki == null || modelSki == null) && Setup.Turn())
                {
                    nextLook = Time.unscaledTime + 2f;
                    Find();
                }
                Transform t = me.transform;
                Vector3 p = t.position;
                // the racks
                // (At a rack the time allowed off the piste starts afresh. It used to keep running from the last time the
                // gear came off, so after one timeout every rack that stands beside its run, not on it, gave gear that
                // vanished the moment the player stepped away: "the skis don't do anything" at Big Air's hut, 2026-11-24.)
                bool atRack = false;
                foreach (Vector3 r in skiRacks) if ((r - p).sqrMagnitude < 2.5f * 2.5f) { Gear = 1; atRack = true; }
                foreach (Vector3 r in boardRacks) if ((r - p).sqrMagnitude < 2.5f * 2.5f) { Gear = 2; atRack = true; }
                if (atRack) offPisteSince = -1f;
                // gear that is a thing: in the hand, it is on
                float nowT = Time.unscaledTime;
                bool gearStale = false;
                foreach (Prop gp in gearProps) if (gp == null) { gearStale = true; break; }
                if ((gearStale || gearProps.Count == 0) && nowT >= nextGearLook && Setup.Turn()) { nextGearLook = nowT + 5f; hidden = null; FindGear(); }
                if (gearProps.Count > 0 && nowT >= nextCarryCheck)
                {
                    nextCarryCheck = nowT + 0.1f;
                    int kind = Carried(me);
                    if (kind != 0) { if (!fromItem || Gear != kind) Diag.Event("ski gear: picked up " + (kind == 1 ? "skis" : "a snowboard")); Gear = kind; fromItem = true; }
                    else if (fromItem) { fromItem = false; if (!atRack) Gear = 0; Diag.Event("ski gear: put down"); }
                }
                if (fromItem) offPisteSince = -1f;
                Wear(t);
                if (Gear == 0 || PlaneDriver.Flying) { Driven.Give("ski"); State = Gear == 0 ? "no gear" : "in a plane"; wasOn = false; airborne = false; return; }

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
                    Driven.Take(flyer, "ski", Driven.SkiSit);
                    velocity += Vector3.down * (9.81f * Time.fixedDeltaTime);
                    Diag.Touched("ski");
                    flyer.rb.linearVelocity = velocity;
                    Speed = velocity.magnitude;
                    State = (Gear == 2 ? "boarding" : "skiing") + ": in the air";
                    return;
                }
                if (airborne) { airborne = false; if (on) velocity = Vector3.ProjectOnPlane(velocity, normal) * 0.9f; }      // landed
                if (!on)
                {
                    Driven.Give("ski");      // off the piste the player walks as ever
                    if (turned && worn != null) { worn.transform.localRotation = Quaternion.identity; turned = false; }
                    if (offPisteSince < 0f) offPisteSince = Time.unscaledTime;
                    if (fromItem) State = "carrying " + (Gear == 1 ? "skis" : "a snowboard") + ", off the piste";
                    else if (atRack) State = "at a rack (gear " + Gear + "): the run is a few steps away";
                    else if (Time.unscaledTime - offPisteSince > OffPisteSeconds) { Gear = 0; State = "gear off: left the piste"; Diag.Event("ski gear came off after " + OffPisteSeconds + " s off the piste at " + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0")); }
                    else State = "off the piste (gear " + Gear + ", " + (OffPisteSeconds - (Time.unscaledTime - offPisteSince)).ToString("0") + " s left)";
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
                // The game's mover is held off while the gear runs (see Driven.cs): it sets a standing player's speed
                // itself every step, so the glide written below never moved anyone who had walked onto the piste.
                Driven.Take(pc, "ski", Driven.SkiSit);

                bool board = Gear == 2;
                float dt = Time.fixedDeltaTime;
                Vector3 fall = Vector3.ProjectOnPlane(Vector3.down, normal);      // down the slope, its length the sine of the slope
                velocity = Vector3.ProjectOnPlane(velocity, normal);
                velocity += fall * (9.81f * dt);
                // steer: turn what speed there is towards where the player looks, along the slope
                Camera cam = Camera.main;
                Vector3 look = cam != null && !Straight ? Vector3.ProjectOnPlane(cam.transform.forward, normal) : fall;
                bool haveLook = look.sqrMagnitude > 1e-4f;
                if (haveLook) look.Normalize();
                // poling: with the mover off the stick walks nobody, so on the flat (a run's foot, a cat track) a skier
                // stood where they stopped. The stick pushed forward now poles them along the way they look.
                float stick = TestPush != 0f ? TestPush : PlaneDriver.Stick();
                if (haveLook && stick > 0.3f && Vector3.Dot(velocity, look) < PoleSpeed) velocity += look * (PoleAccel * dt);
                float speed = velocity.magnitude;
                if (haveLook && speed > 0.3f)
                {
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
                // The skis point the way they run. They hung under the player at the body's own facing, so a skier who
                // looked across the slope went down it with the skis sideways (seen in the recording of 2026-11-28).
                if (worn != null && speed > 1f) { worn.transform.rotation = Quaternion.LookRotation(velocity / speed, normal); turned = true; }
                // keep to the snow: close the gap to the sheet a little each step
                Diag.Touched("ski");
                body.linearVelocity = velocity + Vector3.down * Mathf.Clamp(gap * 6f, -2f, 6f);
                State = (board ? "boarding " : "skiing ") + speed.ToString("0.0") + " m/s";
            }
            catch (Exception e)
            {
                Driven.Give("ski");
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: skiing stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
