// Winter Walk: swimming round the Winter Isle.
//
// In the game a player in the sea stands on an unseen floor just under the surface ("Ocean/FloatFloorCollider", a box
// 2500 m square about the world's middle), and being on it is what makes them swim. The island runs 1.9 km past that
// box's edge to the west and 1.4 km to the south, so in most of its sea there was no floor: a player who stepped off
// the shore fell through the water for ever (players, 2026-11-24: "can't swim in the area where the island lives";
// the test player dropped at -1041, -2420 passed -500 m in ten seconds). The game also stops swimmers going out over
// ground deeper than 5 m (PlayerGround.limitWaterMovement), measured to its terrain, which round the island is the
// sea bed 13 m down or not there at all.
//
// So, on every player's PC:
//   - the game's floor is widened to take in the island's box and Margin metres of sea beyond it (its own collider,
//     so whatever the game asks of it, swimming included, holds out there too);
//   - within reach of the island the game's depth limit is switched off for this PC's player, and ours takes its
//     place: the island's build writes how far every point of its sea is from land (WinterIsle.coast, a 6 m grid),
//     and a swimmer further out than Reach is drawn back towards the land faster than anyone swims (the game's mover
//     sets a swimmer's speed itself every step, so taking speed away did nothing: the swimmer is moved), so they can
//     swim anywhere along the coast and into its bays but not out to the open ocean. Where
//     the game's own terrain is shallow (the mainland's beaches) its own rule stays;
//   - anyone still found deep under the sea out there (through a gap in something of ours) is set back on the last
//     safe ground the game remembers.
// Nothing is sent between players: each PC moves its own player.
using System;
using System.IO;
using UnityEngine;

namespace WinterWalk
{
    public class SwimDriver : MonoBehaviour
    {
        public SwimDriver(IntPtr ptr) : base(ptr) { }

        public static bool Off;                  // live, for tests: leaves the player's swimming alone (the floor stays widened)
        public static float Reach = 60f;         // metres out from the island's shore a swimmer may go
        public static float Margin = 400f;       // the floor runs this far past the island's box
        public static float Pull = 3.2f;         // metres a second a swimmer beyond the reach is drawn back
        public static string State = "not started";
        public static float ShoreDistance = -1f; // the local player's distance from the island's land, for tests
        public static int Rescues, Held;         // players set back from under the sea; frames the limit held a swimmer

        // how far the sea is from the island's land: bytes of 2 m on a grid
        static float mapX0, mapZ0, mapCell;
        static int mapWide, mapDeep;
        static byte[] map;
        static bool mapTried;

        static void LoadMap()
        {
            mapTried = true;
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "WinterIsle.coast");
                if (!File.Exists(file)) { Plugin.L.LogWarning("Winter Walk: no WinterIsle.coast beside the plugin: swimmers round the island are not kept near its shore"); return; }
                using (var r = new BinaryReader(File.OpenRead(file)))
                {
                    mapX0 = r.ReadSingle(); mapZ0 = r.ReadSingle(); mapCell = r.ReadSingle(); mapWide = r.ReadInt32(); mapDeep = r.ReadInt32();
                    if (mapWide < 2 || mapDeep < 2 || mapWide * (long)mapDeep > 4000000 || !(mapCell > 0f)) { Plugin.L.LogWarning("Winter Walk: WinterIsle.coast is not what was expected"); return; }
                    byte[] read = r.ReadBytes(mapWide * mapDeep);
                    if (read.Length != mapWide * mapDeep) { Plugin.L.LogWarning("Winter Walk: WinterIsle.coast is cut short"); return; }
                    map = read;
                }
                Plugin.L.LogInfo("Winter Walk: read the sea's distance from the island's land (" + mapWide + " x " + mapDeep + " points, " + mapCell.ToString("0.#") + " m apart)");
            }
            catch (Exception e) { Plugin.L.LogWarning("Winter Walk: WinterIsle.coast: " + e.GetType().Name + ": " + e.Message); }
        }

        // Metres from (x, z) to the island's land: 0 on it, 1e9 where the map does not reach
        public static float Shore(float x, float z)
        {
            if (map == null) return 1e9f;
            float fx = (x - mapX0) / mapCell, fz = (z - mapZ0) / mapCell;
            if (fx < 0f || fz < 0f || fx > mapWide - 1 || fz > mapDeep - 1) return 1e9f;
            int i = Math.Min((int)fx, mapWide - 2), j = Math.Min((int)fz, mapDeep - 2);
            float u = fx - i, v = fz - j;
            float a = map[j * mapWide + i], b = map[j * mapWide + i + 1], c = map[(j + 1) * mapWide + i], d = map[(j + 1) * mapWide + i + 1];
            return ((a + (b - a) * u) * (1f - v) + (c + (d - c) * u) * v) * 2f;
        }

        IntPtr floorSeen = IntPtr.Zero;
        float floorX0, floorX1, floorZ0, floorZ1;      // the widened floor, in the world
        float nextFloor, nextCheck;
        PlayerCharacter player;
        IntPtr groundSeen = IntPtr.Zero;
        bool limitWas = true, walling;
        float lastRescue;

        // The game's floor under the sea's surface, widened to the island's sea. Its transform is left alone (the box's own
        // size and centre are changed), so anything the game hangs on that object stays where it was.
        void WidenFloor()
        {
            GameObject go = GameObject.Find("Ocean/FloatFloorCollider");
            BoxCollider box = go != null ? go.GetComponent<BoxCollider>() : null;
            if (box == null) return;
            if (box.Pointer == floorSeen) return;
            if (!(Dresser.X1 > Dresser.X0) || !(Dresser.Z1 > Dresser.Z0)) return;
            Transform t = go.transform;
            Vector3 at = t.position, scale = t.lossyScale, size = box.size, centre = box.center;
            if (Math.Abs(scale.x) < 1e-3f || Math.Abs(scale.z) < 1e-3f) return;
            float cx = at.x + centre.x * scale.x, cz = at.z + centre.z * scale.z, hx = Math.Abs(size.x * scale.x) * 0.5f, hz = Math.Abs(size.z * scale.z) * 0.5f;
            float x0 = Math.Min(cx - hx, Dresser.X0 - Margin), x1 = Math.Max(cx + hx, Dresser.X1 + Margin);
            float z0 = Math.Min(cz - hz, Dresser.Z0 - Margin), z1 = Math.Max(cz + hz, Dresser.Z1 + Margin);
            box.size = new Vector3((x1 - x0) / Math.Abs(scale.x), size.y, (z1 - z0) / Math.Abs(scale.z));
            box.center = new Vector3(((x0 + x1) * 0.5f - at.x) / scale.x, centre.y, ((z0 + z1) * 0.5f - at.z) / scale.z);
            floorSeen = box.Pointer;
            floorX0 = x0; floorX1 = x1; floorZ0 = z0; floorZ1 = z1;
            State = "the sea's floor widened";
            Plugin.L.LogInfo("Winter Walk: the sea's floating floor widened from " + (hx * 2f).ToString("0") + " x " + (hz * 2f).ToString("0") + " m about (" + cx.ToString("0") + ", " + cz.ToString("0") + ") to x "
                + x0.ToString("0") + ".." + x1.ToString("0") + ", z " + z0.ToString("0") + ".." + z1.ToString("0") + ", so players can swim round the Winter Isle");
        }

        void Update()
        {
            using var diag = Diag.Time("swim");
            try
            {
                float now = Time.unscaledTime;
                if (now >= nextFloor) { nextFloor = now + 3f; if (!mapTried) LoadMap(); WidenFloor(); }
                if (now >= nextCheck)
                {
                    nextCheck = now + 0.1f;
                    walling = false;
                    Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                    player = me != null ? me.GetComponent<PlayerCharacter>() : null;
                    if (player == null) { groundSeen = IntPtr.Zero; ShoreDistance = -1f; return; }
                    PlayerGround ground = player.ground;
                    if (ground == null) return;
                    if (ground.Pointer != groundSeen) { groundSeen = ground.Pointer; limitWas = ground.limitWaterMovement; }
                    Vector3 at = player.transform.position;
                    float shore = Shore(at.x, at.z);
                    ShoreDistance = shore;
                    bool mapped = shore < 1e8f;
                    // ours to rule: within the map, and either near the island's shore or over water the game calls too deep
                    bool ours = !Off && mapped && (shore <= Reach || ground.distanceToTerrain < -5f);
                    bool want = ours ? false : limitWas;
                    if (ground.limitWaterMovement != want) ground.limitWaterMovement = want;
                    bool swimming = ground.isSwimming;
                    walling = ours && swimming && shore > Reach - 4f;
                    if (!Off && swimming) State = "swimming " + shore.ToString("0") + " m from the island's shore" + (walling ? " (at the limit)" : "");
                    else if (!Off && mapped) State = "watching (" + shore.ToString("0") + " m from the island's land)";
                    else State = Off ? "off" : "watching (away from the island)";

                    // under the sea where nothing of the game's is: back to the last safe ground
                    bool outThere = at.x > floorX0 && at.x < floorX1 && at.z > floorZ0 && at.z < floorZ1 && (at.x < -1300f || at.z < -1300f);
                    if (!Off && floorSeen != IntPtr.Zero && outThere && at.y < -30f && now - lastRescue > 2f)
                    {
                        Vector3 safe = ground.lastSafePosition;
                        Vector3 to = safe.y > -5f && (safe - at).sqrMagnitude < 4000f * 4000f ? safe + Vector3.up * 0.4f : new Vector3(at.x, 0.6f, at.z);
                        Diag.Touched("swim rescue");
                        player.transform.position = to;
                        Rigidbody body = player.rb;
                        if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
                        lastRescue = now; Rescues++;
                        Diag.Warn("swim: the player was " + (-at.y).ToString("0") + " m under the sea at " + Diag.V(at) + "; set back at " + Diag.V(to));
                    }
                }
                if (walling && player != null)
                {
                    // out past the reach: take away the part of the swimmer's speed that heads further out, and draw them back
                    Vector3 at = player.transform.position;
                    float shore = Shore(at.x, at.z);
                    if (shore > Reach && shore < 1e8f)
                    {
                        float gx = Shore(at.x + mapCell, at.z) - Shore(at.x - mapCell, at.z), gz = Shore(at.x, at.z + mapCell) - Shore(at.x, at.z - mapCell);
                        float g = (float)Math.Sqrt(gx * gx + gz * gz);
                        Rigidbody body = player.rb;
                        if (g > 1e-3f && g < 1e7f && body != null)
                        {
                            // a current towards the land, faster than anyone swims (1.6 m/s; sprinting 2.5), never past the line
                            gx /= g; gz /= g;
                            float back = Math.Min(shore - Reach, Pull * Math.Min(Time.deltaTime, 0.1f));
                            Vector3 to = new Vector3(at.x - gx * back, at.y, at.z - gz * back);
                            Diag.Touched("swim limit");
                            player.transform.position = to;
                            body.position = to;
                            Held++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!State.StartsWith("stopped")) Plugin.L.LogWarning("Winter Walk: swimming: " + e.GetType().Name + ": " + e.Message);
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                nextCheck = Time.unscaledTime + 5f;
            }
        }
    }
}
