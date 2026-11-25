// Winter Walk's disc golf (the Summit Disc Golf pack: items named "Frisbee n" and "Disc Golf Basket n").
//
// Baskets stand up. A basket nobody holds is turned upright where it is (keeping the way it faces) and cannot tip:
// put down or dropped anywhere, on any slope, it stands (the user, 2026-11-24).
//
// Discs fly like discs. The game throws every prop as a ball: it leaves the hand at some speed and falls. From the
// moment a disc is let go at a throw's speed, this flies it instead, as a spinning wing (after Hummel's frisbee model,
// with a golf disc's numbers): lift and drag from its speed and the angle it meets the air at, and the slow roll a
// spinning disc makes under the air's twist on it, which is what gives a real throw its shape: it turns a little to
// the right while fast, flattens, then fades left as it slows (a right-hand backhand's spin). What the thrower does
// decides the flight: how hard the game throws it (speed), where they aim (the launch angle), and how they swing: a
// thrower turning left as they let go releases it tilted left (hyzer: it curves left and drops early), turning right
// tilted right (anhyzer: it holds right, even rolls over). The flight ends at the first thing it hits; a basket's
// chains stop it dead. Whichever PC is simulating the prop (its body is not kinematic there) flies it, and the game
// passes its motion on as it does any thrown prop's.
//
// Discs do not run out. The host lays a disc back in its place on the rack when it has lain still for RestSeconds
// with nobody within NobodyWithin of it, and at once (the one furthest from every player) when someone stands at an
// empty rack.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using N3 = System.Numerics.Vector3;

namespace WinterWalk
{
    public class DiscGolf : MonoBehaviour
    {
        public DiscGolf(IntPtr ptr) : base(ptr) { }

        public static bool Off;
        public static string State = "not started", Last = "";
        public static float Power = 1.5f;           // the game's throw speed times this is the disc's
        public static float MaxSpeed = 26f;         // m/s (a strong drive; much faster and this disc turns right over)
        public static float MinThrow = 3f;          // m/s as the game lets it go: slower is a drop, not a throw
        public static float BankPerTurn = 0.2f;     // degrees of tilt for each degree a second the thrower is turning
        public static float MaxBank = 35f;
        public static float NoseUp = 2f;            // degrees
        public static float Stability = -0.012f;    // the air's twist at no angle (more negative: turns right more when fast)
        public static float Fade = 0.20f;           // and how it grows with the angle (more: fades left harder when slow)
        public static float RestSeconds = 30f, NobodyWithin = 30f;
        // for tests (host): TestThrow m/s launches disc 1 from over the rack towards the first basket's place,
        // TestPitch degrees up, TestBank degrees (right positive), TestYaw degrees right of that line
        public static float TestThrow, TestPitch = 8f, TestBank, TestYaw;

        const float Rho = 1.25f, Area = 0.0346f, Mass = 0.175f, Inertia = 0.0012f, Diameter = 0.21f;
        const float CL0 = 0.12f, CLa = 1.6f, CD0 = 0.06f, CDa = 2.6f;

        sealed class Disc
        {
            public BigPuzzleSDK.PuzzleItem item; public Prop prop; public Rigidbody rb; public string name; public Vector3 home;
            public bool flies = true;        // a disc; false for the summit's skis and boards, which only come back to their stand
            public int mode;                 // 0 lying, 1 held, 2 just let go, 3 flying
            public float since, turn, lastYaw; public bool yawKnown;
            public N3 n, expect, start, heading; public float spin, spinAngle, flown, top; public int steps;
            public float restSince = -1f; public N3 at;
        }
        sealed class Basket { public Prop prop; public Rigidbody rb; public string name; public N3 at; public bool frozen; }
        readonly List<Disc> discs = new List<Disc>();
        readonly List<Basket> baskets = new List<Basket>();
        readonly List<N3> players = new List<N3>();
        static PropertyInfo gameProp;
        float nextScan, nextSlow;
        N3 rack, firstBasket;

        static bool IsHost { get { try { return Mirror.NetworkServer.active; } catch (Exception) { return false; } } }
        static N3 V(Vector3 v) => new N3(v.x, v.y, v.z);
        static Vector3 U(N3 v) => new Vector3(v.X, v.Y, v.Z);
        static bool Held(Prop p) { try { return p._collidersAreHeld || p._isInInventory; } catch (Exception) { return false; } }

        // the game's prop behind one of the SDK's items, or null
        internal static Prop PropOf(BigPuzzleSDK.PuzzleItem item)
        {
            if (gameProp == null) gameProp = AccessTools.Property(typeof(BigPuzzleSDK.PuzzleItem), "GameProp");
            return gameProp != null && item != null ? gameProp.GetValue(item) as Prop : null;
        }

        void Scan()
        {
            discs.Clear(); baskets.Clear();
            foreach (BigPuzzleSDK.PuzzleItem item in FindObjectsOfType<BigPuzzleSDK.PuzzleItem>())
            {
                if (item == null) continue;
                string n = item.gameObject.name;
                bool disc = n.StartsWith("Frisbee", StringComparison.Ordinal), basket = n.StartsWith("Disc Golf Basket", StringComparison.Ordinal);
                bool gear = n.StartsWith("Skis", StringComparison.Ordinal) || n.StartsWith("Snowboard", StringComparison.Ordinal);
                if (!disc && !basket && !gear) continue;
                Prop prop = PropOf(item);
                if (prop == null || prop.rb == null) continue;
                if (gear) discs.Add(new Disc { item = item, prop = prop, rb = prop.rb, name = n, home = item.transform.position, flies = false });
                else if (disc) discs.Add(new Disc { item = item, prop = prop, rb = prop.rb, name = n, home = item.transform.position });
                else baskets.Add(new Basket { prop = prop, rb = prop.rb, name = n, at = V(item.transform.position) });
            }
            if (discs.Count == 0) return;
            discs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            baskets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            rack = N3.Zero;
            int real = 0;
            foreach (Disc d in discs) if (d.flies) { rack += V(d.home); real++; }
            if (real > 0) rack /= real;
            firstBasket = baskets.Count > 0 ? baskets[0].at : rack + new N3(0f, 0f, 10f);
            Plugin.L.LogInfo("Winter Walk: disc golf: " + discs.Count + " discs, " + baskets.Count + " baskets");
        }

        // ---- baskets stand up ----
        void Stand(Basket b)
        {
            bool held = Held(b.prop);
            if (held) { if (b.frozen) { b.rb.freezeRotation = false; b.frozen = false; } return; }
            b.at = V(b.rb.position);
            Quaternion q = b.rb.rotation;
            Vector3 up = q * Vector3.up;
            if (up.y < 0.9995f)
            {
                // upright, facing the way it did (or any way, if it lay flat on its side)
                Vector3 f = q * Vector3.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.01f) { f = up; f.y = 0f; }
                Quaternion stood = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up);
                if (!b.rb.isKinematic || IsHost)
                {
                    b.rb.rotation = stood;
                    b.prop.transform.rotation = stood;
                    if (!b.rb.isKinematic) b.rb.angularVelocity = Vector3.zero;
                }
            }
            if (!b.frozen) { b.rb.freezeRotation = true; b.frozen = true; }
        }

        // ---- a disc in the air ----
        void Launch(Disc d, N3 v, float bankDegrees, string how)
        {
            float speed = v.Length();
            float fast = Math.Min(speed * Power, MaxSpeed);
            N3 vh = v / speed;
            v = vh * fast;
            N3 up = new N3(0f, 1f, 0f);
            N3 n = up - N3.Dot(up, vh) * vh;
            if (n.LengthSquared() < 1e-4f) n = new N3(1f, 0f, 0f);       // (thrown straight up)
            n = N3.Normalize(n);
            n = N3.Normalize(n - vh * (float)Math.Tan(NoseUp * Math.PI / 180.0));
            N3 right = N3.Normalize(N3.Cross(n, vh));
            double b = Math.Max(-MaxBank, Math.Min(MaxBank, bankDegrees)) * Math.PI / 180.0;
            n = N3.Normalize(n * (float)Math.Cos(b) + right * (float)Math.Sin(b));
            d.n = n; d.spin = 0.55f * fast / (Diameter * 0.5f); d.spinAngle = 0f;
            d.start = V(d.rb.position); d.heading = N3.Normalize(new N3(vh.X, 0f, vh.Z) + new N3(1e-5f, 0f, 0f));
            d.flown = 0f; d.top = 0f; d.steps = 0; d.mode = 3; d.restSince = -1f;
            // (the game caps how fast a prop's body may move: the first test throw at 20 m/s was read back slower and
            // taken for a collision 0.1 s into its flight)
            float cap = d.prop.maxSpeed;
            if (cap < 80f) d.prop.maxSpeed = 80f;
            d.rb.linearVelocity = U(v);
            d.expect = v;
            how += " (body: speed cap was " + cap.ToString("0.#") + ", drag " + d.rb.linearDamping.ToString("0.##") + ", gravity " + d.rb.useGravity + ", mass " + d.rb.mass.ToString("0.##") + ")";
            Last = d.name + " " + how + " at " + speed.ToString("0.0") + " m/s (flies at " + fast.ToString("0.0") + "), " + (Math.Asin(vh.Y) * 180.0 / Math.PI).ToString("0") + " deg up, tilted " + bankDegrees.ToString("0") + " deg";
            Diag.Event("disc golf: " + Last);
        }

        void Fly(Disc d, float dt)
        {
            if (d.rb.isKinematic || Held(d.prop)) { d.mode = Held(d.prop) ? 1 : 0; return; }
            N3 v = V(d.rb.linearVelocity), at = V(d.rb.position);
            float speed = v.Length();
            d.flown += dt; d.steps++;
            if (at.Y - d.start.Y > d.top) d.top = at.Y - d.start.Y;
            // something was hit (the body's speed is not what the air alone left it), it has stopped, or it has flown long enough
            bool hit = d.steps > 1 && (v - d.expect).Length() > 2.5f;
            if (hit || speed < 1.5f || d.flown > 20f) { Land(d, at, hit); return; }

            N3 vh = v / speed, n = d.n;
            float c = N3.Dot(vh, n);
            float alpha = -(float)Math.Asin(Math.Max(-1f, Math.Min(1f, c)));
            float q = 0.5f * Rho * speed * speed * Area;
            float lifting = Math.Max(-0.35f, Math.Min(0.35f, alpha));      // past about 20 degrees it stalls: no more lift
            float cl = CL0 + CLa * lifting, cd = CD0 + CDa * alpha * alpha;
            N3 l = n - c * vh;
            N3 a = -cd * vh;
            if (l.LengthSquared() > 1e-6f) a += cl * N3.Normalize(l);
            a *= q / Mass;
            // the air's twist on a spinning disc rolls it: right while it is fast and flat, left as it slows and sinks
            N3 right = N3.Cross(n, vh);
            if (right.LengthSquared() > 1e-6f)
            {
                right = N3.Normalize(right);
                float cm = Stability + Fade * alpha;
                float roll = q * Diameter * cm / (Inertia * Math.Max(d.spin, 20f));
                d.n = N3.Normalize(n - right * (roll * dt));
            }
            d.spin -= d.spin * 0.03f * dt;
            d.spinAngle = (d.spinAngle + d.spin * dt * 57.29578f) % 360f;
            N3 nv = v + a * dt;
            d.rb.linearVelocity = U(nv);
            d.rb.angularVelocity = Vector3.zero;
            N3 f = vh - N3.Dot(vh, d.n) * d.n;
            if (f.LengthSquared() > 1e-6f) d.rb.rotation = Quaternion.LookRotation(U(N3.Normalize(f)), U(d.n)) * Quaternion.AngleAxis(d.spinAngle, Vector3.up);
            d.expect = nv + new N3(0f, -9.81f * dt, 0f);
        }

        void Land(Disc d, N3 at, bool hit)
        {
            d.mode = 0;
            N3 gone = at - d.start; gone.Y = 0f;
            float far = gone.Length();
            float drift = N3.Dot(gone, new N3(d.heading.Z, 0f, -d.heading.X));      // to the right of the line it left on
            N3 vNow = V(d.rb.linearVelocity);
            string what = hit ? "hit something (moving at " + vNow.Length().ToString("0.0") + " m/s where the air alone would leave " + d.expect.Length().ToString("0.0") + ")" : "came down";
            if (hit)
                foreach (Basket b in baskets)
                {
                    float dx = at.X - b.at.X, dz = at.Z - b.at.Z, dy = at.Y - b.at.Y;
                    if (dx * dx + dz * dz > 0.65f * 0.65f || dy < 0.45f || dy > 1.45f) continue;
                    d.rb.linearVelocity = Vector3.zero;      // the chains
                    d.rb.angularVelocity = Vector3.zero;
                    what = "in the chains of " + b.name;
                    break;
                }
            Last = d.name + " " + what + " after " + far.ToString("0") + " m and " + d.flown.ToString("0.0") + " s, " + d.top.ToString("0.0") + " m up at its highest, "
                + Math.Abs(drift).ToString("0.0") + " m " + (drift >= 0f ? "right" : "left") + " of its line, " + (d.start.Y - at.Y).ToString("0") + " m below where it left the hand";
            Diag.Event("disc golf: " + Last);
        }

        // the thrower's turning, from the nearest player's heading while the disc is in a hand (degrees a second, right positive)
        void Swing(Disc d, float dt)
        {
            N3 at = V(d.rb.position);
            PlayerCharacter nearest = null; double best = 2.5 * 2.5;
            var all = PlayerCharacter.allPlayerCharacters;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                {
                    PlayerCharacter pc = all[i];
                    if (pc == null) continue;
                    N3 p = V(pc.transform.position);
                    double dd = (p - at).LengthSquared();
                    if (dd < best) { best = dd; nearest = pc; }
                }
            if (nearest == null) { d.yawKnown = false; return; }
            float yaw = nearest.transform.eulerAngles.y;
            if (d.yawKnown)
            {
                float delta = yaw - d.lastYaw;
                while (delta > 180f) delta -= 360f;
                while (delta < -180f) delta += 360f;
                float k = Math.Min(1f, dt / 0.15f);
                d.turn += (delta / dt - d.turn) * k;
            }
            else d.turn = 0f;
            d.lastYaw = yaw; d.yawKnown = true;
        }

        void FixedUpdate()
        {
            if (Off) return;
            using var diag = Diag.Time("disc golf");
            try
            {
                float now = Time.unscaledTime, dt = Time.fixedDeltaTime;
                if (Mirror.NetworkClient.localPlayer == null) { State = "no player"; return; }
                bool stale = false;
                foreach (Disc d in discs) if (d.item == null || d.prop == null || d.rb == null) { stale = true; break; }
                if (stale || (discs.Count == 0 && now >= nextScan && Setup.Turn())) { nextScan = now + 5f; Scan(); }
                if (discs.Count == 0) { State = "no discs in the loaded packs"; return; }

                bool slow = now >= nextSlow;
                if (slow) nextSlow = now + 0.2f;
                int flying = 0, held = 0;
                foreach (Disc d in discs)
                {
                    if (!d.flies) continue;
                    if (d.mode == 3) { Fly(d, dt); if (d.mode == 3) flying++; continue; }
                    if (d.mode == 0) { if (slow && Held(d.prop)) { d.mode = 1; d.yawKnown = false; d.restSince = -1f; } continue; }
                    if (d.mode == 1)
                    {
                        held++;
                        if (Held(d.prop)) { Swing(d, dt); continue; }
                        d.mode = 2; d.since = now;
                    }
                    // just let go: a throw, if this PC is the one moving it and it left fast enough
                    if (now - d.since > 0.3f) { d.mode = 0; continue; }
                    if (d.rb.isKinematic) continue;
                    N3 v = V(d.rb.linearVelocity);
                    if (v.Length() >= MinThrow) Launch(d, v, d.yawKnown ? d.turn * BankPerTurn : 0f, "thrown");
                }
                if (slow)
                {
                    foreach (Basket b in baskets) if (b.prop != null && b.rb != null) Stand(b);
                    if (IsHost) Host(now);
                }
                State = discs.Count + " discs (" + held + " in hand, " + flying + " flying), " + baskets.Count + " baskets" + (Last.Length > 0 ? "; " + Last : "");
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: disc golf stopped: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                Off = true;
            }
        }

        // ---- the host: discs come back to the rack; test throws ----
        void Host(float now)
        {
            if (TestThrow > 0f)
            {
                float speed = TestThrow; TestThrow = 0f;
                Disc d = discs.Find(x => x.flies);
                if (d == null) return;
                try { d.prop.ServerSetUnpinned(); } catch (Exception) { }
                N3 line = firstBasket - rack; line.Y = 0f;
                line = line.LengthSquared() > 0.01f ? N3.Normalize(line) : new N3(0f, 0f, 1f);
                double yaw = TestYaw * Math.PI / 180.0, pitch = TestPitch * Math.PI / 180.0;
                N3 dir = new N3((float)(line.X * Math.Cos(yaw) + line.Z * Math.Sin(yaw)), 0f, (float)(-line.X * Math.Sin(yaw) + line.Z * Math.Cos(yaw)));
                dir = N3.Normalize(dir * (float)Math.Cos(pitch) + new N3(0f, (float)Math.Sin(pitch), 0f));
                Vector3 from = U(rack + new N3(0f, 0.8f, 0f) + line * 1.5f);
                d.prop.transform.position = from; d.rb.position = from;
                d.rb.isKinematic = false;
                float keep = Power; Power = 1f;
                Launch(d, dir * speed, TestBank, "test-thrown");
                Power = keep;
                return;
            }
            players.Clear();
            var all = PlayerCharacter.allPlayerCharacters;
            if (all != null) for (int i = 0; i < all.Count; i++) { PlayerCharacter pc = all[i]; if (pc != null) players.Add(V(pc.transform.position)); }
            bool someoneAtRack = false; int onRack = 0;
            foreach (N3 p in players) if ((p - rack).LengthSquared() < 3f * 3f) someoneAtRack = true;
            Disc furthest = null; float furthestBy = 0f;
            foreach (Disc d in discs)
            {
                if (d.mode != 0 || Held(d.prop)) { d.restSince = -1f; continue; }
                N3 at = V(d.rb.position);
                if ((at - V(d.home)).LengthSquared() < 2f * 2f || (d.flies && (at - rack).LengthSquared() < 2f * 2f)) { if (d.flies) onRack++; d.restSince = -1f; continue; }
                bool still = d.rb.isKinematic || (at - d.at).LengthSquared() < 0.02f * 0.02f;
                d.at = at;
                if (!still) { d.restSince = -1f; continue; }
                if (d.restSince < 0f) d.restSince = now;
                float nearest = float.MaxValue;
                foreach (N3 p in players) nearest = Math.Min(nearest, (p - at).Length());
                if (now - d.restSince > RestSeconds && nearest > NobodyWithin) { Home(d, "lay " + RestSeconds.ToString("0") + " s with nobody near"); if (d.flies) onRack++; continue; }
                if (d.flies && now - d.restSince > 3f && nearest > furthestBy) { furthestBy = nearest; furthest = d; }
            }
            if (someoneAtRack && onRack == 0 && furthest != null && furthestBy > 6f) Home(furthest, "the rack was empty");
        }

        void Home(Disc d, string why)
        {
            d.restSince = -1f;
            d.item.SendTo(d.home + Vector3.up * 0.05f);
            d.rb.rotation = Quaternion.identity;
            Diag.Event((d.flies ? "disc golf: " : "ski gear: ") + d.name + " back in its place (" + why + ")");
        }
    }
}
