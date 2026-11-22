// Winter Walk: jetpacks.
//
// Three jetpacks stand among the presents under The Tree ("Jetpack Present", "Jetpack Present 2", ...). Walking up
// to one straps a jetpack on this PC's player: the model from The Tree's pack ("Jetpack Model") is hung on their
// back, and its tank is filled. To fly: jump, let go of jump, and press it again in the air; while it is held the
// jetpack fires (flames under its nozzles), pushing up hard and a little the way the camera looks, and burning fuel.
// It never runs out of fuel.
//
// Only this player is driven, through its own Rigidbody, as the skiing is; the game's networking shows the flight
// to the others. Other players' jetpacks are not shown on their backs yet (nothing tells one PC what another wears).
// The jump button is read from Rewired, the game's input system, by the action whose name has "jump" in it; if no
// such action is found it falls back to the space bar.
using System;
using UnityEngine;

namespace WinterWalk
{
    public class JetpackDriver : MonoBehaviour
    {
        public JetpackDriver(IntPtr ptr) : base(ptr) { }

        public const float Tank = 40f;          // seconds of firing on a full tank
        const float Lift = 17f;                 // metres a second squared upwards while firing (gravity is 9.81)
        const float Push = 5f;                  // ... and along where the camera looks
        const float MaxRise = 8f, MaxAcross = 12f;
        const float BodyHeight = 0.75f;         // a standing player's origin above the ground

        public static bool Worn;                // whether this player has a jetpack on (the companion can set it)
        public static float Fuel;               // seconds left
        public static bool TestFire;            // for testing without a controller: fire as if jump were held in the air
        public static string State = "not loaded";
        // where the jetpack sits: on the back of the round body the player sees ("localBottom_mesh", which bobs and swings
        // as they move, so the jetpack follows it every frame instead of hanging from the player's origin, where it cut
        // through the body), 'Back' metres behind its middle and 'Up' above; and its size (all can be set live)
        public static float Up = 0.02f, Back = 0.25f, Scale = 0.6f;
        Transform anchor, anchorOf;

        GameObject model, worn, flame;
        readonly System.Collections.Generic.List<Vector3> presents = new System.Collections.Generic.List<Vector3>();
        float nextLook;
        int jumpAction = -2;                    // Rewired action id; -1 when none was found (space bar instead)
        bool wasHeld, armed, firing;

        void Find()
        {
            presents.Clear();
            GameObject first = GameObject.Find("Jetpack Present");
            if (first != null && first.transform.parent != null)
            {
                Transform parent = first.transform.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform c = parent.GetChild(i);
                    if (c.name.StartsWith("Jetpack Present")) presents.Add(c.position);
                }
            }
            GameObject models = GameObject.Find("Jetpack Models");
            if (models == null)
            {
                // the models' parent is kept switched off, so Find cannot see it; look under the presents' root instead
                Transform root = first != null ? first.transform.parent : null;
                Transform m = root != null ? root.Find("Jetpack Models") : null;
                models = m != null ? m.gameObject : null;
            }
            if (models != null)
            {
                Transform m = models.transform.Find("Jetpack Model");
                model = m != null ? m.gameObject : null;
            }
        }

        bool JumpHeld()
        {
            try
            {
                if (jumpAction == -2)
                {
                    jumpAction = -1;
                    // (the action list is an IL2CPP collection that does not walk from here: ask for each id instead)
                    for (int id = 0; id < 256 && jumpAction < 0; id++)
                    {
                        Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                        if (a != null && a.name != null && a.name.ToLowerInvariant().Contains("jump")) jumpAction = a.id;
                    }
                    Plugin.L.LogInfo("Winter Walk: jetpack reads jump from " + (jumpAction >= 0 ? "Rewired action " + jumpAction : "the space bar"));
                }
                if (jumpAction >= 0)
                {
                    Rewired.Player player = Rewired.ReInput.players.GetPlayer(0);
                    if (player != null) return player.GetButton(jumpAction);
                }
            }
            catch (Exception) { jumpAction = -1; }
            try { return Input.GetKey(KeyCode.Space); } catch (Exception) { return false; }
        }

        void Wear(Transform player)
        {
            if (Worn && worn == null && model != null)
            {
                worn = Instantiate((UnityEngine.Object)model, player).Cast<GameObject>();
                worn.transform.localRotation = Quaternion.identity;
                worn.SetActive(true);
                Transform f = worn.transform.Find("Flame");
                flame = f != null ? f.gameObject : null;
                if (flame != null) flame.SetActive(false);
            }
            if (!Worn && worn != null) { Destroy(worn); worn = null; flame = null; }
            if (worn != null) worn.transform.localScale = Vector3.one * Scale;
        }

        // the body ball under the player (found once per player by name, anywhere below it)
        static Transform FindBelow(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name == name) return c;
                Transform deeper = FindBelow(c, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        void LateUpdate()
        {
            using var diag = Diag.Time("jetpack");
            try
            {
                if (worn == null) return;
                Transform player = worn.transform.parent;
                if (player == null) return;
                if (anchorOf != player || anchor == null) { anchorOf = player; anchor = FindBelow(player, "localBottom_mesh") ?? FindBelow(player, "torso_mesh"); }
                Vector3 forward = player.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 centre = anchor != null ? anchor.position : player.position + Vector3.up * -0.5f;
                worn.transform.position = centre - forward * Back + Vector3.up * Up;
                worn.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            catch (Exception) { }
        }

        void FixedUpdate()
        {
            using var diag = Diag.Time("jetpack");
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { State = "no player"; return; }
                if (Time.unscaledTime >= nextLook && (presents.Count == 0 || model == null))
                {
                    nextLook = Time.unscaledTime + 3f;
                    Find();
                }
                Transform t = me.transform;
                Vector3 p = t.position;
                // the presents: strap one on, fill the tank
                foreach (Vector3 present in presents)
                    if ((present - p).sqrMagnitude < 2.6f * 2.6f) { Worn = true; Fuel = Tank; }
                Wear(t);
                if (!Worn) { State = "no jetpack"; return; }

                bool held = JumpHeld() || TestFire;
                float gap = 99f;
                foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || hit.collider.transform.IsChildOf(t)) continue;
                    gap = Mathf.Min(gap, hit.distance - 0.3f - BodyHeight);
                }
                bool grounded = gap < 0.25f;
                if (grounded && !TestFire) { armed = false; firing = false; }
                else if (!held) armed = true;      // let go after the jump (or walked off an edge): the next press fires
                firing = (armed || TestFire) && held;      // (no fuel: it never runs out)
                if (flame != null && flame.activeSelf != firing) flame.SetActive(firing);
                wasHeld = held;
                if (!firing)
                {
                    State = "jetpack on" + (grounded ? "" : armed ? ", ready to fire" : "");
                    return;
                }
                PlayerCharacter pc = me.GetComponent<PlayerCharacter>();
                Rigidbody body = pc != null ? pc.rb : null;
                if (body == null) { State = "no body"; return; }
                float dt = Time.fixedDeltaTime;
                Vector3 v = body.linearVelocity;
                v.y = Mathf.Min(MaxRise, v.y + Lift * dt);
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 look = cam.transform.forward;
                    look.y = 0f;
                    if (look.sqrMagnitude > 1e-4f)
                    {
                        Vector3 across = new Vector3(v.x, 0f, v.z) + look.normalized * (Push * dt);
                        if (across.magnitude > MaxAcross) across = across.normalized * MaxAcross;
                        v.x = across.x; v.z = across.z;
                    }
                }
                Diag.Touched("jetpack");
                body.linearVelocity = v;
                State = "jetpack firing";
            }
            catch (Exception e)
            {
                if (State.StartsWith("stopped")) return;
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: jetpack stopped: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
