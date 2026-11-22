// Winter Walk: the dinosaurs (the Dinosaurs pack, DinosaursPuzzle.cs).
//
// Calm, each dinosaur wanders round its home on a path worked out from the network clock and its number alone, so every
// PC walks it the same way with nothing sent. Noise annoys it: every PC measures its own player's noise (the voice's
// loudness from the game's lips, three times louder through a megaphone, sprinting, jumping, waving, a cowbell) and
// reports it to the host through the pack's DinoControl script. The host adds up what each dinosaur hears (fading with
// distance, 40 m at most, and settling again with time); past the threshold the dinosaur chases the noisiest player at
// 95 % of a player's sprinting speed, and gives up when that player gets far away, rides or sits somewhere safe, or after
// a while; then it walks home and calms down. The host publishes chases (where, how fast, after whom) a few times a
// second; the other PCs follow them.
// A player's own PC decides whether its player is caught (within reach of a dinosaur chasing them, and not riding or
// sitting): it drops everything the player carries, in hand and in every pocket, where they stand, and a moment later
// wakes them at the last tower they visited (The Tree's, the mine's, the airport's, the train's or the summit's; The
// Tree's to begin with), and tells the host, which calls the dinosaur off.
// They only come out at night (NightFrom to NightTo, by the game's clock): by day they keep out of sight, each one
// appearing or vanishing only while no player is close enough to see it happen, and a chase still running at dawn ends.
// For testing: ForceNight (1 night, 0 day, -1 the clock), TestNoise (noise a second added to this player's), Threshold, Peaceful (no chases), State, Last.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WinterWalk
{
    public class DinoDriver : MonoBehaviour
    {
        public DinoDriver(IntPtr ptr) : base(ptr) { }

        public static string State = "not loaded";
        public static string Last = "";              // what happened last (a chase, a catch)
        public static float TestNoise;               // for testing: noise a second added to this player's
        public static float Threshold = 1f;          // how much heard noise turns a dinosaur
        public static bool Peaceful;                 // no chases (the companion can set it)
        public static float Noise;                   // read-only: this player's noise a second, lately
        public static string Tower = "The Tree";     // read-only: where this player wakes if caught
        public static float NightFrom = 19f, NightTo = 6f;      // the hours the dinosaurs are out
        public static int ForceNight = -1;           // for testing: 1 night, 0 day, -1 the game's clock
        public static bool Night;                    // read-only: whether they are out
        public static int Heard;                     // read-only (host): noise reports heard
        public static float MostAnnoyed;             // read-only (host): the most annoyed dinosaur's level
        public static string LastReport = "";
        public static string SafeWhy = "";           // read-only: why this player counts as out of reach (riding...)
        public static float ChaseSpeed;              // read-only (host): how fast a chasing dinosaur runs, m/s
        public static float SprintRead;              // read-only: the player's sprint speed as the game's tunings give it        // read-only (host): the last report and what came of it

        const float Hearing = 40f, Calming = 0.12f, Reach = 1.9f, WalkSpeed = 1.1f, ReturnSpeed = 2.6f, Segment = 42f;
        const float GiveUpFar = 110f, GiveUpAfter = 75f;

        sealed class Dino
        {
            public Transform root, head, tail, eyes;
            public Transform[] legs = new Transform[4];
            public Vector2 home; public float radius; public int kind;
            public float stride, legSwing;
            public int mode;                   // 0 calm, 1 chasing, 2 going home
            public uint target;
            public double t0, chaseSince, farSince, safeSince;
            public Vector2 p0, v0;             // the host's last word: where at t0, and how fast
            public Vector2 pos, shown; public float heading, phase, ground;
            public float annoyed; public uint loudest; public float loudestAmount;
            public readonly Dictionary<int, Vector3> waypoints = new Dictionary<int, Vector3>();
            public float nextGround;
        }

        readonly List<Dino> dinos = new List<Dino>();
        GameObject packRoot;
        float nextLook, nextReport, nextTowerCheck, nextPublish, caughtUntil, dropAt = -1f, teleportAt = -1f;
        Vector3 wakeAt;
        int seenVersion = -1;
        string lastPublished = "";
        float noiseAccum;
        bool wasJumping;
        int waveLeft = -1, waveRight = -1;
        bool actionsLooked;

        // DinoControl (in the packs' script assembly), by reflection
        static FieldInfo ctlOrders, ctlVersion, ctlReports;
        static MethodInfo ctlPublish, ctlReport;
        static PropertyInfo ctlPresent;
        static bool Bind()
        {
            if (ctlPublish != null) return true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("DinoControl");
                if (t == null) continue;
                ctlOrders = t.GetField("Orders"); ctlVersion = t.GetField("Version"); ctlReports = t.GetField("Reports");
                ctlPublish = t.GetMethod("Publish"); ctlReport = t.GetMethod("Report"); ctlPresent = t.GetProperty("Present");
                return ctlPublish != null;
            }
            return false;
        }
        static bool Present { get { try { return ctlPresent != null && (bool)ctlPresent.GetValue(null); } catch (Exception) { return false; } } }
        static void Report(string text) { try { if (ctlReport != null) ctlReport.Invoke(null, new object[] { text }); } catch (Exception) { } }
        static bool IsHost { get { try { return Mirror.NetworkServer.active; } catch (Exception) { return false; } } }
        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // ---- finding them ----
        bool Find()
        {
            dinos.Clear();
            packRoot = GameObject.Find("Dinosaurs");
            if (packRoot == null) { State = "no Dinosaurs pack loaded"; return false; }
            for (int i = 0; i < packRoot.transform.childCount; i++)
            {
                Transform t = packRoot.transform.GetChild(i);
                if (!t.name.StartsWith("Dino ") || t.name == "Dino Control") continue;
                var d = new Dino { root = t, head = t.Find("Head"), tail = t.Find("Tail") };
                d.eyes = d.head != null ? d.head.Find("Angry Eyes") : null;
                string[] names = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
                for (int l = 0; l < 4; l++) d.legs[l] = t.Find(names[l]);
                d.kind = t.name.EndsWith("Longneck") ? 0 : t.name.EndsWith("Plated") ? 1 : 2;
                d.home = new Vector2(t.position.x, t.position.z);
                d.radius = d.kind == 0 ? 50f : 45f;
                d.stride = d.kind == 0 ? 2.2f : 1.3f;
                d.legSwing = d.kind == 0 ? 22f : 30f;
                d.pos = d.shown = d.home;
                d.ground = t.position.y;
                dinos.Add(d);
            }
            State = dinos.Count + " dinosaurs";
            return dinos.Count > 0;
        }

        // ---- the ground and the calm walk ----
        static bool GroundAt(Vector2 p, float from, out float y)
        {
            y = 0f;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(p.x, from + 80f, p.y), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null) continue;      // players, carried things
                // the island's own ground ("Island i-j" tiles) or the base game's terrain: not trestles, roofs or rocks
                bool ground = hit.collider.gameObject.name.StartsWith("Island ") || hit.collider.TryCast<TerrainCollider>() != null;      // (IL2CPP: TryCast, not 'is')
                if (!ground) continue;
                if (!found || hit.point.y > y) { y = hit.point.y; found = true; }
            }
            return found;
        }

        static uint Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }

        Vector3 Waypoint(Dino d, int index, int k)
        {
            if (d.waypoints.TryGetValue(k, out Vector3 w)) return w;
            Vector2 at = d.home;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint h = Hash(index * 1000 + attempt, k);
                float a = (h & 0xFFFF) / 65535f * Mathf.PI * 2f, r = d.radius * Mathf.Sqrt(((h >> 16) & 0xFFFF) / 65535f);
                Vector2 p = d.home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (GroundAt(p, d.ground, out float gy) && gy > 1.5f && Mathf.Abs(gy - d.ground) < 25f) { at = p; break; }
            }
            if (d.waypoints.Count > 64) d.waypoints.Clear();
            w = new Vector3(at.x, 0f, at.y);
            d.waypoints[k] = w;
            return w;
        }

        // where a calm dinosaur is at time t: walking from one waypoint to the next, then grazing, a segment at a time
        Vector2 Wander(Dino d, int index, double t, out bool moving, out Vector2 dir)
        {
            double s = t / Segment + index * 0.37;
            int k = (int)Math.Floor(s);
            float f = (float)(s - k) * Segment;
            Vector3 a3 = Waypoint(d, index, k), b3 = Waypoint(d, index, k + 1);
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z);
            float walk = Mathf.Min((b - a).magnitude / WalkSpeed, Segment * 0.75f);
            dir = (b - a).sqrMagnitude > 0.01f ? (b - a).normalized : Vector2.up;
            moving = f < walk;
            return walk > 0.01f ? Vector2.Lerp(a, b, Mathf.Clamp01(f / walk)) : b;
        }

        // ---- the local player ----
        static PlayerCharacter Me()
        {
            try { Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer; return me != null ? me.GetComponent<PlayerCharacter>() : null; }
            catch (Exception) { return null; }
        }
        static uint NetId(PlayerCharacter pc) { try { return pc.netId; } catch (Exception) { return 0; } }

        static PlayerCharacter PlayerById(uint id)
        {
            try
            {
                var all = PlayerCharacter.allPlayerCharacters;
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++) { PlayerCharacter pc = all[i]; if (pc != null && pc.netId == id) return pc; }
            }
            catch (Exception) { }
            return null;
        }

        bool Safe(PlayerCharacter pc)
        {
            try
            {
                if (PlaneDriver.Flying) { SafeWhy = "flying"; return true; }
                // (sitting is not counted: the game reports a player sitting in states that are not a seat, which would make
                // them untouchable; seated riders on the trains and lifts outrun the dinosaurs anyway)
                if (pc.playerNetworking != null && pc.playerNetworking.isGhost) { SafeWhy = "ghost"; return true; }
                if (JetpackDriver.Worn && pc.transform.position.y - GroundBelow(pc.transform.position) > 2.5f) { SafeWhy = "jetpack"; return true; }
                if (SkiDriver.Speed > 6f) { SafeWhy = "skiing"; return true; }
                SafeWhy = "";
            }
            catch (Exception) { }
            return false;
        }
        static float GroundBelow(Vector3 p) => GroundAt(new Vector2(p.x, p.z), p.y, out float y) ? y : p.y;

        void LookActions()
        {
            actionsLooked = true;
            try
            {
                for (int id = 0; id < 256; id++)
                {
                    Rewired.InputAction a = Rewired.ReInput.mapping.GetAction(id);
                    if (a == null || a.name == null) continue;
                    string n = a.name.ToLowerInvariant();
                    if (waveLeft < 0 && n.Contains("waveleft")) waveLeft = a.id;
                    if (waveRight < 0 && n.Contains("waveright")) waveRight = a.id;
                }
            }
            catch (Exception) { }
        }

        // this player's noise a second, now
        float MeasureNoise(PlayerCharacter pc)
        {
            float n = TestNoise;
            try
            {
                string held = "";
                try { if (pc.hands != null && pc.hands.heldProp != null) held = pc.hands.heldProp.gameObject.name.ToLowerInvariant(); } catch (Exception) { }
                if (pc.lips != null && !pc.lips.isMuted)
                {
                    float voice = Mathf.Clamp01(pc.lips.amplitude);
                    if (voice > 0.05f) n += voice * 1.4f * (held.Contains("megaphone") ? 3f : 1f);
                }
                if (pc.sprinter != null && pc.sprinter.isSprinting && pc.rb != null && pc.rb.linearVelocity.magnitude > 3f) n += 0.35f;
                bool jumping = pc.jumper != null && pc.jumper.justJumped;
                if (jumping && !wasJumping) n += 2.0f;      // a jump is a thump: counted once (the measurement is a second's worth)
                wasJumping = jumping;
                if (held.Contains("cowbell") || held.Contains("bell")) n += 0.25f;
                if (!actionsLooked) LookActions();
                Rewired.Player input = Rewired.ReInput.players.GetPlayer(0);
                if (input != null && ((waveLeft >= 0 && input.GetButton(waveLeft)) || (waveRight >= 0 && input.GetButton(waveRight)))) n += 0.25f;
            }
            catch (Exception) { }
            return n;
        }

        // ---- towers: where a caught player wakes ----
        static readonly string[] TowerPosts = { "Button Post", "Mine Button Post", "Airport Button Post", "Train Button Post", "Summit Button Post" };
        static readonly string[] TowerNames = { "The Tree", "the mine tower", "the airport tower", "the train tower", "the summit tower" };
        readonly Transform[] posts = new Transform[5];
        Transform lastPost;

        void CheckTowers(Vector3 me)
        {
            for (int i = 0; i < 5; i++)
            {
                if (posts[i] == null)
                {
                    GameObject g = GameObject.Find(TowerPosts[i]);
                    if (g == null && i == 0) { GameObject tree = GameObject.Find("TheTree"); if (tree != null) { Transform f = tree.transform.Find(TowerPosts[0]); if (f != null) g = f.gameObject; } }
                    if (g != null) posts[i] = g.transform;
                }
                if (posts[i] != null && (posts[i].position - me).sqrMagnitude < 16f * 16f) { lastPost = posts[i]; Tower = TowerNames[i]; }
            }
            if (lastPost == null && posts[0] != null) { lastPost = posts[0]; Tower = TowerNames[0]; }
        }

        Vector3 WakePlace()
        {
            if (lastPost == null) return Vector3.zero;
            Vector3 p = lastPost.position + lastPost.forward * 3f;
            return new Vector3(p.x, GroundBelow(p + Vector3.up * 4f) + 1.2f, p.z);
        }

        // ---- being caught (on the caught player's own PC) ----
        void Caught(PlayerCharacter pc, int index)
        {
            caughtUntil = Time.unscaledTime + 10f;
            dropAt = Time.unscaledTime;
            teleportAt = Time.unscaledTime + 0.6f;
            wakeAt = WakePlace();
            Last = "caught by dinosaur " + index + " at " + pc.transform.position + "; waking at " + Tower;
            Plugin.L.LogInfo("Winter Walk: " + Last);
            Report("caught " + NetId(pc) + " " + index);
        }

        void DropAndWake(PlayerCharacter pc)
        {
            if (dropAt >= 0f && Time.unscaledTime >= dropAt)
            {
                dropAt = -1f;
                try { if (pc.hands != null && pc.hands.heldProp != null && pc.actions != null) pc.actions.ActionDropPropOrPlayer(); } catch (Exception e) { Plugin.L.LogWarning("Winter Walk: dinosaur catch: could not drop the held thing: " + e.Message); }
                try { if (pc.misc != null) pc.misc.EmptyAllPockets(); } catch (Exception e) { Plugin.L.LogWarning("Winter Walk: dinosaur catch: could not empty the pockets: " + e.Message); }
            }
            if (teleportAt >= 0f && Time.unscaledTime >= teleportAt)
            {
                teleportAt = -1f;
                if (wakeAt == Vector3.zero) return;
                try
                {
                    Diag.Touched("dinosaur catch");
                    pc.transform.position = wakeAt;
                    if (pc.rb != null) { pc.rb.position = wakeAt; pc.rb.linearVelocity = Vector3.zero; pc.rb.angularVelocity = Vector3.zero; }
                }
                catch (Exception e) { Plugin.L.LogWarning("Winter Walk: dinosaur catch: could not move the player: " + e.Message); }
            }
        }

        // ---- the host ----
        float SprintSpeed()
        {
            PlayerCharacter me = Me();
            try { if (me != null && me.tunings != null) { SprintRead = me.tunings.forwardSprintSpeed; if (SprintRead > 0.5f) return SprintRead; } } catch (Exception) { }
            return 7f;
        }

        void HearReports(double now)
        {
            if (ctlReports == null) return;
            Queue<string> q = null;
            try { q = ctlReports.GetValue(null) as Queue<string>; } catch (Exception) { }
            if (q == null) return;
            int guard = 0;
            while (q.Count > 0 && guard++ < 256)
            {
                string raw = q.Dequeue();
                string[] w = raw.Split(' ');
                Heard++;
                if (w.Length >= 3 && w[0] == "noise")
                {
                    uint id = uint.Parse(w[1], CultureInfo.InvariantCulture);
                    float amount = P(w[2]);
                    bool safe = w.Length > 3 && w[3] == "1";
                    PlayerCharacter pc = PlayerById(id);
                    if (pc == null) { LastReport = raw + ": no such player"; continue; }
                    Vector3 at = pc.transform.position;
                    LastReport = raw + " from " + at;
                    float nearest = 1e9f; int nearestIndex = -1;
                    for (int i = 0; i < dinos.Count; i++) { float dd = Vector2.Distance(new Vector2(at.x, at.z), dinos[i].pos); if (dd < nearest) { nearest = dd; nearestIndex = i; } }
                    if (nearestIndex >= 0) LastReport += "; nearest dinosaur " + nearestIndex + " at " + dinos[nearestIndex].pos + " (" + nearest.ToString("0.#") + " m), mode " + dinos[nearestIndex].mode + ", annoyed " + dinos[nearestIndex].annoyed.ToString("0.##") + ", safe " + safe;
                    for (int i = 0; i < dinos.Count; i++)
                    {
                        Dino d = dinos[i];
                        if (d.mode == 1 && d.target == id) { if (safe) { if (d.safeSince <= 0) d.safeSince = now; } else d.safeSince = 0; }
                        if (d.mode != 0 || amount <= 0f || safe) continue;
                        float dist = Vector2.Distance(new Vector2(at.x, at.z), d.pos);
                        if (dist > Hearing) continue;
                        float heard = amount * (1f - dist / Hearing);
                        d.annoyed += heard;
                        if (heard >= d.loudestAmount * 0.5f || d.loudest == 0) { d.loudest = id; d.loudestAmount = heard; }
                    }
                }
                else if (w.Length >= 3 && w[0] == "caught")
                {
                    int index = int.Parse(w[2], CultureInfo.InvariantCulture);
                    if (index >= 0 && index < dinos.Count) GoHome(dinos[index], now);
                }
            }
        }

        void GoHome(Dino d, double now)
        {
            d.mode = 2; d.t0 = now; d.p0 = d.pos; d.v0 = Vector2.zero; d.annoyed = 0f; d.loudest = 0; d.loudestAmount = 0f; d.safeSince = 0; d.farSince = 0;
        }

        bool Decide(double now, float dt)
        {
            bool changed = false;
            float speed = SprintSpeed() * 0.95f;
            ChaseSpeed = speed;
            MostAnnoyed = 0f;
            for (int i = 0; i < dinos.Count; i++) MostAnnoyed = Mathf.Max(MostAnnoyed, dinos[i].annoyed);
            for (int i = 0; i < dinos.Count; i++)
            {
                Dino d = dinos[i];
                if (d.mode == 0)
                {
                    d.annoyed = Mathf.Max(0f, d.annoyed - Calming * dt);
                    if (d.annoyed <= 0f) { d.loudest = 0; d.loudestAmount = 0f; }
                    if (!Night) { d.annoyed = 0f; d.loudest = 0; d.loudestAmount = 0f; continue; }
                    if (!Peaceful && d.annoyed >= Threshold && d.loudest != 0 && PlayerById(d.loudest) != null)
                    {
                        d.mode = 1; d.target = d.loudest; d.t0 = now; d.chaseSince = now; d.p0 = d.pos; d.v0 = Vector2.zero; d.farSince = 0; d.safeSince = 0;
                        Last = "dinosaur " + i + " chases player " + d.target;
                        Plugin.L.LogInfo("Winter Walk: " + Last);
                        changed = true;
                    }
                    continue;
                }
                if (d.mode == 1)
                {
                    PlayerCharacter pc = PlayerById(d.target);
                    if (pc == null || Peaceful || !Night || now - d.chaseSince > GiveUpAfter || (d.safeSince > 0 && now - d.safeSince > 3.0)) { GoHome(d, now); changed = true; continue; }
                    Vector3 tp = pc.transform.position;
                    Vector2 to = new Vector2(tp.x, tp.z) - d.pos;
                    float dist = to.magnitude;
                    if (dist > GiveUpFar) { if (d.farSince <= 0) d.farSince = now; if (now - d.farSince > 4.0) { GoHome(d, now); changed = true; continue; } } else d.farSince = 0;
                    Vector2 v = dist > 1.0f ? to / dist * speed : Vector2.zero;
                    d.pos += v * dt;
                    if ((v - d.v0).magnitude > 0.8f || now - d.t0 > 0.4) { d.t0 = now; d.p0 = d.pos; d.v0 = v; changed = true; }
                    continue;
                }
                // going home: back to where its calm walk is now
                Vector2 calm = Wander(d, i, now, out _, out _);
                Vector2 back = calm - d.pos;
                if (back.magnitude < 1.5f) { d.mode = 0; d.annoyed = 0f; changed = true; continue; }
                Vector2 vb = back.normalized * ReturnSpeed;
                d.pos += vb * dt;
                if ((vb - d.v0).magnitude > 0.5f || now - d.t0 > 1.0) { d.t0 = now; d.p0 = d.pos; d.v0 = vb; changed = true; }
            }
            return changed;
        }

        string Encode()
        {
            var sb = new StringBuilder("1");
            for (int i = 0; i < dinos.Count; i++)
            {
                Dino d = dinos[i];
                if (d.mode == 0) continue;
                sb.Append(';').Append(i).Append(',').Append(d.mode).Append(',').Append(d.target).Append(',').Append(F(d.t0)).Append(',')
                  .Append(F(d.p0.x)).Append(',').Append(F(d.p0.y)).Append(',').Append(F(d.v0.x)).Append(',').Append(F(d.v0.y));
            }
            return sb.ToString();
        }

        void Decode(string text, double now)
        {
            var listed = new bool[dinos.Count];
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(';');
                for (int k = 1; k < parts.Length; k++)
                {
                    string[] f = parts[k].Split(',');
                    if (f.Length < 8) continue;
                    int i = int.Parse(f[0], CultureInfo.InvariantCulture);
                    if (i < 0 || i >= dinos.Count) continue;
                    Dino d = dinos[i];
                    d.mode = int.Parse(f[1], CultureInfo.InvariantCulture);
                    d.target = uint.Parse(f[2], CultureInfo.InvariantCulture);
                    d.t0 = double.Parse(f[3], CultureInfo.InvariantCulture);
                    d.p0 = new Vector2(P(f[4]), P(f[5])); d.v0 = new Vector2(P(f[6]), P(f[7]));
                    listed[i] = true;
                }
            }
            for (int i = 0; i < dinos.Count; i++) if (!listed[i]) dinos[i].mode = 0;
        }

        // ---- placing and animating ----
        void Show(Dino d, int index, double now, float dt)
        {
            Vector2 target; bool moving; Vector2 dir;
            if (d.mode == 0) { target = Wander(d, index, now, out moving, out dir); }
            else
            {
                target = IsHost ? d.pos : d.p0 + d.v0 * Mathf.Min((float)(now - d.t0), 0.6f);
                moving = d.v0.sqrMagnitude > 0.01f;
                dir = moving ? d.v0.normalized : new Vector2(Mathf.Sin(d.heading * Mathf.Deg2Rad), Mathf.Cos(d.heading * Mathf.Deg2Rad));
                if (d.mode == 1 && !moving)      // standing over its catch: face the player
                {
                    PlayerCharacter pc = PlayerById(d.target);
                    if (pc != null) { Vector2 to = new Vector2(pc.transform.position.x, pc.transform.position.z) - d.shown; if (to.sqrMagnitude > 0.01f) dir = to.normalized; }
                }
            }
            if (!IsHost || d.mode == 0) d.pos = target;
            Vector2 before = d.shown;
            d.shown = (d.shown - target).sqrMagnitude > 25f ? target : Vector2.Lerp(d.shown, target, Mathf.Clamp01(dt * 10f));
            float travelled = (d.shown - before).magnitude;
            if (Time.unscaledTime >= d.nextGround)
            {
                d.nextGround = Time.unscaledTime + (d.mode == 0 ? 0.25f : 0.08f);
                if (GroundAt(d.shown, d.ground, out float gy)) d.ground = gy;
            }
            float want = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            d.heading = Mathf.MoveTowardsAngle(d.heading, want, (d.mode == 1 ? 240f : 70f) * dt);
            d.root.position = new Vector3(d.shown.x, Mathf.Lerp(d.root.position.y, d.ground, Mathf.Clamp01(dt * 8f)), d.shown.y);
            d.root.rotation = Quaternion.Euler(0f, d.heading, 0f);
            // legs swing with the distance walked; the head bobs, the tail sways; red eyes while it chases
            d.phase += travelled / d.stride * Mathf.PI;
            float swing = travelled > 0.0005f ? d.legSwing : 0f;
            for (int l = 0; l < 4; l++)
            {
                if (d.legs[l] == null) continue;
                float a = Mathf.Sin(d.phase + (l == 0 || l == 3 ? 0f : Mathf.PI)) * swing;
                d.legs[l].localRotation = Quaternion.Slerp(d.legs[l].localRotation, Quaternion.Euler(a, 0f, 0f), Mathf.Clamp01(dt * 12f));
            }
            float t = (float)now;
            if (d.head != null)
            {
                float graze = d.mode == 0 && !moving ? 18f + 6f * Mathf.Sin(t * 0.7f + index) : 0f;
                float raise = d.mode == 1 ? -14f : 0f;
                d.head.localRotation = Quaternion.Slerp(d.head.localRotation, Quaternion.Euler(graze + raise + Mathf.Sin(d.phase * 2f) * 3f, Mathf.Sin(t * 0.4f + index) * 12f, 0f), Mathf.Clamp01(dt * 3f));
            }
            if (d.tail != null) d.tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * (d.mode == 1 ? 6f : 1.3f) + index) * (d.mode == 1 ? 18f : 10f), 0f);
            if (d.eyes != null && d.eyes.gameObject.activeSelf != (d.mode == 1)) d.eyes.gameObject.SetActive(d.mode == 1);
        }

        static bool IsNight()
        {
            if (ForceNight >= 0) return ForceNight == 1;
            try
            {
                Enviro.EnviroManager env = Enviro.EnviroManager.instance;
                if (env == null || env.Time == null) return false;
                float h = env.Time.Settings.timeOfDay;
                return NightFrom > NightTo ? (h >= NightFrom || h < NightTo) : (h >= NightFrom && h < NightTo);
            }
            catch (Exception) { return false; }
        }

        // a dinosaur shows or hides only while nobody is near enough to watch it happen
        void SetOut(Dino d, bool wanted)
        {
            GameObject go = d.root.gameObject;
            if (go.activeSelf == wanted) return;
            try
            {
                var all = PlayerCharacter.allPlayerCharacters;
                for (int i = 0; all != null && i < all.Count; i++)
                {
                    PlayerCharacter pc = all[i];
                    if (pc == null) continue;
                    Vector3 p = pc.transform.position;
                    if ((new Vector2(p.x, p.z) - d.shown).sqrMagnitude < 60f * 60f) return;
                }
            }
            catch (Exception) { }
            go.SetActive(wanted);
        }

        float nextState;

        void Update()
        {
            using var diag = Diag.Time("dinosaurs");
            try
            {
                if (dinos.Count == 0 || dinos[0].root == null)
                {
                    if (Time.unscaledTime < nextLook) return;
                    nextLook = Time.unscaledTime + 3f;
                    if (!Find()) return;
                }
                Bind();
                Night = IsNight();
                double now = Mirror.NetworkTime.time;
                float dt = Mathf.Min(Time.deltaTime, 0.1f);
                bool host = IsHost;
                PlayerCharacter me = Me();
                if (me != null)
                {
                    Vector3 mp = me.transform.position;
                    if (Time.unscaledTime >= nextTowerCheck) { nextTowerCheck = Time.unscaledTime + 1f; CheckTowers(mp); }
                    float n = Night ? MeasureNoise(me) : 0f;      // by day nobody is listening
                    noiseAccum += n * dt;
                    Noise = Mathf.Lerp(Noise, n, Mathf.Clamp01(dt * 2f));
                    if (Time.unscaledTime >= nextReport)
                    {
                        nextReport = Time.unscaledTime + 0.25f;
                        bool safe = Safe(me);
                        if (noiseAccum > 0.005f || safe) Report("noise " + NetId(me) + " " + noiseAccum.ToString("0.###", CultureInfo.InvariantCulture) + " " + (safe ? "1" : "0"));
                        noiseAccum = 0f;
                    }
                    DropAndWake(me);
                    // caught? only by a dinosaur chasing this player, and not while riding, sitting or just woken
                    if (Time.unscaledTime >= caughtUntil && !Safe(me))
                    {
                        uint id = NetId(me);
                        for (int i = 0; i < dinos.Count; i++)
                        {
                            Dino d = dinos[i];
                            if (d.mode != 1 || d.target != id) continue;
                            Vector2 to = new Vector2(mp.x, mp.z) - d.shown;
                            if (to.magnitude < Reach + (d.kind == 0 ? 0.8f : 0.3f) && Mathf.Abs(mp.y - d.root.position.y) < 4f) { Caught(me, i); break; }
                        }
                    }
                }
                if (host)
                {
                    HearReports(now);
                    if (Decide(now, dt) || Time.unscaledTime >= nextPublish)
                    {
                        string text = Encode();
                        if (text != lastPublished || Time.unscaledTime >= nextPublish)
                        {
                            nextPublish = Time.unscaledTime + 2f;
                            if (text != lastPublished || !string.Equals(ctlOrders != null ? (string)ctlOrders.GetValue(null) : text, text))
                            {
                                lastPublished = text;
                                try { if (ctlPublish != null) ctlPublish.Invoke(null, new object[] { text }); } catch (Exception) { }
                            }
                        }
                    }
                }
                else
                {
                    int version = ctlVersion != null ? (int)ctlVersion.GetValue(null) : 0;
                    if (version != seenVersion) { seenVersion = version; Decode(ctlOrders != null ? (string)ctlOrders.GetValue(null) : "", now); }
                }
                int chasing = 0;
                int outNow = 0;
                for (int i = 0; i < dinos.Count; i++)
                {
                    // (one out of sight by day is not moved: it is placed again before it is shown)
                    bool wanted = Night || dinos[i].mode != 0;
                    if (wanted || dinos[i].root.gameObject.activeSelf) Show(dinos[i], i, now, dt);
                    SetOut(dinos[i], wanted);
                    if (dinos[i].root.gameObject.activeSelf) outNow++;
                    if (dinos[i].mode == 1) chasing++;
                }
                if (Time.unscaledTime < nextState) return;
                nextState = Time.unscaledTime + 0.5f;
                State = dinos.Count + " dinosaurs, " + outNow + " out (" + (Night ? "night" : "day") + "), " + chasing + " chasing" + (Peaceful ? " (peaceful)" : "") + (Present ? "" : " (no DinoControl: this PC runs them alone)");
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: dinosaurs stopped: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                dinos.Clear();
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
