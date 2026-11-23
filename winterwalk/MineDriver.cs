// Winter Walk: the Winter Isle's mine railway.
//
// The island's pack carries the mine (WinterIsleMine in the Unity project): the hall, the low tunnels, the track,
// the mantrips ("Mine Trips/Mantrip 0"..) and the gates across every tunnel mouth ("Mine Gates"). Its build also
// writes the line the mantrips follow beside this plugin, as WinterIsle.mineline: one closed round, a point every
// two metres ("x y z kind stop": kind 0 in the open at a station, 1 in a tunnel, 2 in the hall; stop 1 where a
// mantrip halts).
//
// This moves the mantrips round the line. As with the gondola, each one's place comes from the network clock
// (Mirror's NetworkTime), so every player sees the same mantrip in the same place and nothing is sent. They run
// fast in the tunnels, slow down for a stop and wait there a while.
//
// The mine opens when the mine tower's five plinths ("Mine Plinth 1".."5") all hold a gourd: the gates go and
// the mantrips start. The plinths are read every second, never counted from events (a save puts gourds back
// without firing them).
//
// It also stops the island's snowfall for a player whose camera is underground, in a railway tunnel or in the summit's
// pizzeria.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WinterWalk
{
    public class MineDriver : MonoBehaviour
    {
        public MineDriver(IntPtr ptr) : base(ptr) { }

        const float LineSpeed = 20f;         // metres a second in the tunnels
        const float StopSpeed = 1.4f;        // ...and creeping into a stop
        const float Ease = 34f;              // metres over which a mantrip slows for a stop or picks up after it
        const float Dwell = 7f;              // seconds waiting at a stop

        public static bool ForceOpen;        // for testing (the companion: set?path=WinterWalk.MineDriver.ForceOpen&value=true)
        public static bool Open;             // read-only: whether the mine is open
        public static bool Underground;      // read-only: whether this player's camera is in the mine
        public static string State = "not loaded";

        readonly List<Transform> trips = new List<Transform>();
        readonly List<Vector3> points = new List<Vector3>();      // the line as built, for telling who is underground
        readonly List<int> kinds = new List<int>();
        readonly List<Vector3> loop = new List<Vector3>();        // the line with its waits, against time
        readonly List<float> loopTime = new List<float>();
        float period;
        readonly List<BigPuzzleSDK.PuzzleGourdPlinth> plinths = new List<BigPuzzleSDK.PuzzleGourdPlinth>();
        float nextLook, nextPlinthCheck, nextUnderCheck;
        GameObject tripRoot, gates, snowfall;
        string file;
        bool dressed;
        // The lamps' light: a few real lights, moved every quarter second to the lamps nearest this player's camera
        // (hundreds of lamps hang in the mine; only those near enough to see cost anything)
        const int LightCount = 14;
        const float LightReach = 48f;
        readonly List<Vector3> lamps = new List<Vector3>();
        readonly List<Light> lights = new List<Light>();
        GameObject lightRoot;
        float nextDress;
        public static string Look = "not dressed";      // read-only: which of the game's materials the mine wears

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        bool Find()
        {
            tripRoot = GameObject.Find("Mine Trips");
            gates = GameObject.Find("Mine Gates");
            snowfall = GameObject.Find("Snowfall");
            if (tripRoot == null) return false;
            if (file == null) file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.mineline");
            if (!File.Exists(file)) { State = "no WinterIsle.mineline beside the plugin"; tripRoot = null; return false; }
            points.Clear(); kinds.Clear(); lamps.Clear();
            var stops = new List<int>();
            foreach (string text in File.ReadAllLines(file))
            {
                if (text.Length == 0 || text[0] == '#') continue;
                string[] parts = text.Split(' ');
                if (parts[0] == "L" && parts.Length >= 4)
                {
                    lamps.Add(new Vector3(float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    continue;
                }
                if (parts.Length < 5) continue;
                if (parts[4] == "1") stops.Add(points.Count);
                points.Add(new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture)));
                kinds.Add(int.Parse(parts[3], CultureInfo.InvariantCulture));
            }
            trips.Clear();
            for (int i = 0; ; i++)
            {
                Transform c = tripRoot.transform.Find("Mantrip " + i);
                if (c == null) break;
                trips.Add(c);
            }
            if (points.Count < 3 || trips.Count == 0) { tripRoot = null; return false; }
            BuildLoop(stops);
            Plugin.L.LogInfo("Winter Walk: mine found: " + points.Count + " track points, " + stops.Count + " stops, " + trips.Count + " mantrips, a round trip of " + period.ToString("0") + " s");
            return true;
        }

        void BuildLoop(List<int> stops)
        {
            int n = points.Count;
            var along = new float[n + 1];
            for (int i = 0; i < n; i++) along[i + 1] = along[i] + (points[(i + 1) % n] - points[i]).magnitude;
            float total = along[n];
            // the time to each point: slow near a stop, a wait at it, fast between
            loop.Clear(); loopTime.Clear();
            float time = 0f;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = points[i % n];
                if (i > 0)
                {
                    float mid = (along[i] + along[i - 1]) * 0.5f, toStop = float.MaxValue;
                    foreach (int s in stops)
                    {
                        float d = Mathf.Abs(mid - along[s]);
                        toStop = Mathf.Min(toStop, Mathf.Min(d, total - d));
                    }
                    time += (along[i] - along[i - 1]) / Mathf.Lerp(StopSpeed, LineSpeed, Smooth(toStop / Ease));
                }
                loop.Add(p); loopTime.Add(time);
                if (i < n && stops.Contains(i)) { time += Dwell; loop.Add(p); loopTime.Add(time); }
            }
            period = time;
        }

        Vector3 At(float t, out Vector3 heading)
        {
            t = Mathf.Repeat(t, period);
            int lo = 0, hi = loopTime.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (loopTime[m] <= t) lo = m; else hi = m; }
            float f = (t - loopTime[lo]) / Mathf.Max(loopTime[hi] - loopTime[lo], 1e-5f);
            heading = loop[hi] - loop[lo];
            return Vector3.Lerp(loop[lo], loop[hi], f);
        }

        bool TowerComplete()
        {
            if (Time.unscaledTime < nextPlinthCheck) return Open;
            nextPlinthCheck = Time.unscaledTime + 1f;
            if (plinths.Count < 5)
            {
                plinths.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    GameObject p = GameObject.Find("Mine Plinth " + i);
                    if (p == null) continue;
                    BigPuzzleSDK.PuzzleGourdPlinth plinth = p.GetComponent<BigPuzzleSDK.PuzzleGourdPlinth>();
                    if (plinth != null) plinths.Add(plinth);
                }
            }
            if (plinths.Count < 5) { State = "waiting: the mine tower shows " + plinths.Count + " of 5 plinths (the tower shows one plinth per puzzle loaded, The Tree's five first, so the mine needs ten puzzles)"; return false; }
            int filled = 0;
            foreach (BigPuzzleSDK.PuzzleGourdPlinth plinth in plinths)
                if (plinth != null && plinth.HasGourd) filled++;
            State = filled >= 5 ? "open" : "waiting: " + filled + " of 5 gourds on the mine tower";
            return filled >= 5;
        }

        // The mine wears the game's own materials, so it is drawn to the same standard as the game's tunnels: their
        // concrete (a 2048 px surface with a normal map, tinted by our vertex colours, lit and shadowed by the game)
        // on the rock, and the game's flat vertex-colour material on timber and track. The pack's own material is
        // only what shows until these are found.
        void Dress()
        {
            Material concrete = null, flat = null;
            // among the loaded materials (a few thousand) rather than every renderer in the world's (93 to 140 ms of the
            // frame after joining a walk)
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>()))
            {
                Material m = o != null ? o.TryCast<Material>() : null;
                if (m == null) continue;
                if (concrete == null && m.name == "Concrete_Mottle_Vertex") concrete = m;
                else if (flat == null && m.name == "hhVertexColors") flat = m;
                if (concrete != null && flat != null) break;
            }
            if (concrete == null) { Look = "the game's tunnel material is not loaded yet"; return; }
            var rock = new Material(concrete) { name = "Mine Rock" };
            // our texture coordinates are in metres: one repeat of the surface a metre, as in the game's tunnels
            foreach (string map in new[] { "_AlbedoMap", "_NormalMap", "_MaskMap" })
                if (rock.HasProperty(map)) { rock.SetTextureScale(map, Vector2.one); rock.SetTextureOffset(map, Vector2.zero); }
            int rocks = 0, others = 0;
            Transform island = tripRoot.transform.parent;
            for (int i = 0; i < island.childCount; i++)
            {
                Transform child = island.GetChild(i);
                string n = child.name;
                if (!n.StartsWith("Mine ")) continue;
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                if (n.StartsWith("Mine Tunnel") || n == "Mine Hall" || n == "Mine Mouths") { renderer.sharedMaterial = rock; rocks++; }
                else if ((n == "Mine Props" || n == "Mine Track") && flat != null) { renderer.sharedMaterial = flat; others++; }
            }
            dressed = true;
            Look = "rock: " + concrete.name + " (" + concrete.shader.name + ") on " + rocks + " meshes; timber and track: " + (flat != null ? flat.name + " on " + others : "the pack's own");
            Plugin.L.LogInfo("Winter Walk: mine dressed: " + Look);
        }

        void MoveLights()
        {
            Camera cam = Camera.main;
            if (cam == null || lamps.Count == 0) return;
            if (lightRoot == null)
            {
                lightRoot = new GameObject("Mine Lights");
                lights.Clear();
                for (int i = 0; i < LightCount; i++)
                {
                    var go = new GameObject("Mine Light " + i);
                    go.transform.SetParent(lightRoot.transform, false);
                    Light light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 9f;
                    light.intensity = 2.2f;
                    light.color = new Color(1f, 0.76f, 0.47f);
                    light.shadows = LightShadows.None;
                    light.enabled = false;
                    lights.Add(light);
                }
            }
            Vector3 eye = cam.transform.position;
            // the nearest lamps, by insertion into a short sorted list
            var best = new List<int>();
            var bestD = new List<float>();
            for (int i = 0; i < lamps.Count; i++)
            {
                float d = (lamps[i] - eye).sqrMagnitude;
                if (d > LightReach * LightReach) continue;
                int at = bestD.Count;
                while (at > 0 && bestD[at - 1] > d) at--;
                if (at >= LightCount) continue;
                best.Insert(at, i); bestD.Insert(at, d);
                if (best.Count > LightCount) { best.RemoveAt(LightCount); bestD.RemoveAt(LightCount); }
            }
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null) { lightRoot = null; return; }
                bool on = i < best.Count;
                if (on) lights[i].transform.position = lamps[best[i]] - Vector3.up * 0.25f;
                if (lights[i].enabled != on) lights[i].enabled = on;
            }
        }

        // Whether the camera is in a tunnel or the hall: near a point of the line that is roofed
        void CheckUnderground()
        {
            Camera cam = Camera.main;
            bool under = false;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                for (int i = 0; i < points.Count && !under; i++)
                {
                    if (kinds[i] == 0) continue;
                    Vector3 d = eye - points[i];
                    if (kinds[i] == 2) under = d.y > -2f && d.y < 14f && d.x * d.x + d.z * d.z < 30f * 30f;
                    else under = d.y > -1.5f && d.y < 2.6f && d.x * d.x + d.z * d.z < 3f * 3f;
                }
            }
            Underground = under;
            Rescue();
            under |= TrainDriver.InTunnel || (cam != null && Indoors(cam.transform.position));      // a railway tunnel or a room stops the snowfall too
            if (snowfall != null && snowfall.activeSelf == under) snowfall.SetActive(!under);
        }

        // Rooms where it must not snow: empty objects named "Indoors ..." whose position, turn and size are the room's box
        // (the summit pizzeria's, "Indoors Pizzeria")
        readonly List<Transform> rooms = new List<Transform>();
        float nextRoomLook;
        bool Indoors(Vector3 eye)
        {
            if (rooms.Count == 0 && Time.unscaledTime >= nextRoomLook)
            {
                nextRoomLook = Time.unscaledTime + 5f;
                GameObject room = GameObject.Find("Indoors Pizzeria");
                if (room != null) rooms.Add(room.transform);
            }
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i] == null) { rooms.Clear(); return false; }
                Vector3 p = rooms[i].InverseTransformPoint(eye);
                if (Mathf.Abs(p.x) < 0.5f && Mathf.Abs(p.y) < 0.5f && Mathf.Abs(p.z) < 0.5f) return true;
            }
            return false;
        }

        // Nobody is left under the mine: the ground is cut away over the sheds, so a player shoved through a tunnel's
        // floor (a mantrip can do it) would fall to the sea under the island. Anyone well below the track, straight
        // under it, is set back on it.
        void Rescue()
        {
            Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
            if (me == null) return;
            Vector3 at = me.transform.position;
            for (int i = 0; i < points.Count; i += 2)
            {
                Vector3 d = at - points[i];
                if (d.y > -2.5f || d.y < -60f || d.x * d.x + d.z * d.z > 4.5f * 4.5f) continue;
                Vector3 back = points[i] + Vector3.up * 1.2f;
                PlayerCharacter pc = me.GetComponent<PlayerCharacter>();
                Rigidbody body = pc != null ? pc.rb : null;
                Diag.Touched("mine rescue");
                me.transform.position = back;
                if (body != null) { body.position = back; body.linearVelocity = Vector3.zero; }
                Plugin.L.LogInfo("Winter Walk: mine: set a player who had fallen under the track back on it");
                return;
            }
        }

        // While the mine is closed the mantrips stand still: they are placed once, not every frame (each placing is
        // several calls into the game and moves colliders), and the tower is asked four times a second
        bool parked;
        float nextOpenCheck;

        void Update()
        {
            using var diag = Diag.Time("mine");
            try
            {
                if (tripRoot == null)
                {
                    if (Underground && snowfall != null) snowfall.SetActive(true);
                    Underground = false;
                    if (Time.unscaledTime < nextLook || !Setup.Turn()) return;
                    nextLook = Time.unscaledTime + 2f;
                    trips.Clear();
                    plinths.Clear();
                    dressed = false;
                    parked = false; nextOpenCheck = 0f;
                    if (!Find()) return;
                }
                if (!dressed && Time.unscaledTime >= nextDress && Setup.Turn()) { nextDress = Time.unscaledTime + 10f; Dress(); }
                if (Time.unscaledTime >= nextOpenCheck)
                {
                    nextOpenCheck = Time.unscaledTime + 0.25f;
                    Open = ForceOpen || TowerComplete();
                    if (ForceOpen) State = "open (forced open for testing)";
                    if (gates != null && gates.activeSelf == Open) gates.SetActive(!Open);
                }
                // closed: the mantrips stand where the clock's zero puts them
                double now = Open ? Mirror.NetworkTime.time : 0.0;
                bool place = Open || !parked;
                parked = !Open;
                for (int i = 0; place && i < trips.Count; i++)
                {
                    if (trips[i] == null) { tripRoot = null; return; }
                    float t = (float)((now + (double)period * i / trips.Count) % period);
                    Vector3 at = At(t, out Vector3 heading);
                    trips[i].position = at;
                    if (heading.sqrMagnitude > 1e-8f) trips[i].rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);      // nose up or down on the ramps
                }
                if (Time.unscaledTime >= nextUnderCheck)
                {
                    nextUnderCheck = Time.unscaledTime + 0.25f;
                    CheckUnderground();
                    MoveLights();
                }
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: mine stopped: " + e.GetType().Name + ": " + e.Message);
                tripRoot = null;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
