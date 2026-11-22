// Winter Walk: tracks in the snow.
//
// Whoever moves over the Winter Isle's snow presses it down and pushes some of it aside. Walking treads a
// trench with an uneven floor and a low bank heaped along each side; sliding (anything fast) leaves a shallower,
// smooth groove. Falling snow slowly fills every track in again.
//
// How: the snow's surface is a height field in millimetres over the whole island, kept only where something has
// disturbed it (sparse 16 m tiles, forgotten 100 m away). A 64 m window of it round this player's camera lives in a texture, which the
// island's ground shader reads to shade the trench, its walls and the banks (BWM/Snow, "_BwmTrail..." globals).
// Every player's PC works this out for itself from where it sees the other players, so nothing is sent; someone
// who joins late does not see tracks made before they came.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WinterWalk
{
    public class SnowTrails : MonoBehaviour
    {
        public SnowTrails(IntPtr ptr) : base(ptr) { }

        const int N = 1024;                    // the window's side in texels (a power of two: it wraps)
        const float Texel = 0.0625f;           // metres per texel: the window is 64 m across
        const int TileBits = 8, TileSide = 1 << TileBits;      // the store's tiles: 256 texels, 16 m
        const byte Level = 128;                // untouched snow; one step is a millimetre
        const float SecondsPerStep = 7f;       // snowfall fills a track by a millimetre in this time
        const float Recentre = 4f;             // the window follows the camera once it is this far off centre
        const float SlideSpeed = 4.2f;         // metres a second: faster than this is a slide, not a walk
        const float BodyHeight = 0.75f;        // a standing player's origin is this far above the ground (measured in game)
        public const float Reach = 22f;        // tracks are drawn in full this near the camera (and fade over 5 m)
        const float Forget = 100f;             // tracks further than this from the camera are forgotten (and off the isle, all)
        public static int Tiles;               // how many 16 m tiles of tracks are kept (for the debug log)

        // set from outside (the test companion: set?path=WinterWalk.SnowTrails.TestPattern&value=true) to draw a
        // walked line, a slid curve and a crossing in front of the camera
        public static bool TestPattern;
        public static int Stamps;              // how many stamps have been made (for the companion to read)

        readonly Dictionary<long, byte[]> tiles = new Dictionary<long, byte[]>();
        readonly Dictionary<long, float> relaxedAt = new Dictionary<long, float>();
        readonly List<long> relaxQueue = new List<long>();
        readonly byte[] window = new byte[N * N];
        GCHandle pin;
        Texture2D texture;
        int upload;                            // which way of sending the window to the texture works here (0 = untried)
        bool dirty, active, haveOrigin, toldGround, toldTrack, failed;
        int ox, oz;                            // the window's lowest texel, in world texels
        Vector3 camAt;                         // the camera, as of this frame
        long cachedKey = long.MinValue;
        byte[] cachedTile;

        struct Walker { public Vector3 last; public bool onSnow; }
        readonly Dictionary<IntPtr, Walker> walkers = new Dictionary<IntPtr, Walker>();

        static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        byte[] Tile(int gx, int gz, bool make)
        {
            long key = Key(gx >> TileBits, gz >> TileBits);
            if (key == cachedKey && cachedTile != null) return cachedTile;
            if (!tiles.TryGetValue(key, out byte[] tile))
            {
                if (!make) return null;
                tile = new byte[TileSide * TileSide];
                for (int i = 0; i < tile.Length; i++) tile[i] = Level;
                tiles[key] = tile;
                relaxedAt[key] = Time.time;
            }
            cachedKey = key; cachedTile = tile;
            return tile;
        }

        byte Get(int gx, int gz)
        {
            byte[] tile = Tile(gx, gz, false);
            return tile == null ? Level : tile[((gz & (TileSide - 1)) << TileBits) | (gx & (TileSide - 1))];
        }

        void Set(int gx, int gz, byte value)
        {
            Tile(gx, gz, true)[((gz & (TileSide - 1)) << TileBits) | (gx & (TileSide - 1))] = value;
            if (haveOrigin && gx >= ox && gx < ox + N && gz >= oz && gz < oz + N)
            {
                window[((gz & (N - 1)) * N) | (gx & (N - 1))] = value;
                dirty = true;
            }
        }

        // ---- the window ----

        void FillColumn(int gx)
        {
            int ix = gx & (N - 1);
            for (int gz = oz; gz < oz + N; gz++) window[((gz & (N - 1)) * N) | ix] = Get(gx, gz);
        }

        void FillRow(int gz)
        {
            int row = (gz & (N - 1)) * N;
            for (int gx = ox; gx < ox + N; gx++) window[row | (gx & (N - 1))] = Get(gx, gz);
        }

        void Centre(Vector3 camera)
        {
            int nox = Mathf.FloorToInt(camera.x / Texel) - N / 2, noz = Mathf.FloorToInt(camera.z / Texel) - N / 2;
            if (haveOrigin && Mathf.Abs(nox - ox) * Texel < Recentre && Mathf.Abs(noz - oz) * Texel < Recentre) return;
            if (!haveOrigin || Mathf.Abs(nox - ox) >= N || Mathf.Abs(noz - oz) >= N)
            {
                ox = nox; oz = noz; haveOrigin = true;
                for (int gz = oz; gz < oz + N; gz++) FillRow(gz);
            }
            else
            {
                while (ox < nox) { FillColumn(ox + N); ox++; }      // the column leaving on one side is the one arriving on the other
                while (ox > nox) { ox--; FillColumn(ox); }
                while (oz < noz) { FillRow(oz + N); oz++; }
                while (oz > noz) { oz--; FillRow(oz); }
            }
            dirty = true;
        }

        void Send()
        {
            if (upload == 0 || upload == 1)
            {
                try
                {
                    if (!pin.IsAllocated) pin = GCHandle.Alloc(window, GCHandleType.Pinned);
                    texture.LoadRawTextureData(pin.AddrOfPinnedObject(), N * N);
                    texture.Apply(false, false);
                    if (upload == 0) Plugin.L.LogInfo("Winter Walk: snow tracks are sent to the texture straight from memory");
                    upload = 1;
                    return;
                }
                catch (Exception e)
                {
                    Plugin.L.LogWarning("Winter Walk: sending tracks from memory failed (" + e.GetType().Name + ": " + e.Message + "); trying an array");
                    upload = 2;
                }
            }
            texture.LoadRawTextureData(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(window));
            texture.Apply(false, false);
        }

        // ---- pressing the snow ----

        // One press of the snow at a point: a flat-floored dent, and outside it a bank of the snow pushed aside.
        // Sizes in metres, depths in millimetres.
        void Stamp(float x, float z, float inner, float outer, float depth, float bank)
        {
            int r = Mathf.CeilToInt(outer / Texel);
            int cx = Mathf.FloorToInt(x / Texel), cz = Mathf.FloorToInt(z / Texel);
            for (int gz = cz - r; gz <= cz + r; gz++)
                for (int gx = cx - r; gx <= cx + r; gx++)
                {
                    float dx = (gx + 0.5f) * Texel - x, dz = (gz + 0.5f) * Texel - z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= outer) continue;
                    byte old = Get(gx, gz);
                    float h = old;
                    if (d < inner)
                    {
                        // flat over the middle, then a wall rising to the surface at the dent's edge
                        float floor = Level - depth * (1f - Smooth((d - inner * 0.5f) / (inner * 0.5f)));
                        if (floor < h) h = floor;
                    }
                    else if (old >= Level - 2)
                    {
                        // the bank: steep on the track's side, tailing off outwards; never heaped into another track
                        float t = (d - inner) / (outer - inner);
                        float heap = Level + bank * Smooth(t / 0.3f) * (1f - Smooth((t - 0.3f) / 0.7f));
                        if (heap > h) h = heap;
                    }
                    byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(h), 0, 255);
                    if (value != old) Set(gx, gz, value);
                }
            Stamps++;
        }

        // Something moved from a to b over the snow at this speed
        void Line(Vector3 a, Vector3 b, float speed)
        {
            float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / Texel));
            bool slide = speed > SlideSpeed;
            for (int s = 1; s <= steps; s++)
            {
                float x = Mathf.Lerp(a.x, b.x, (float)s / steps), z = Mathf.Lerp(a.z, b.z, (float)s / steps);
                if (slide) Stamp(x, z, 0.24f, 0.46f, 46f, 30f);
                else
                {
                    // a walked trench is uneven: each step sinks a little more or less than the last
                    float uneven = Mathf.PerlinNoise(x * 3.1f + 17f, z * 3.1f + 5f);
                    Stamp(x, z, 0.2f + 0.05f * uneven, 0.48f, Mathf.Lerp(72f, 105f, uneven), Mathf.Lerp(28f, 50f, Mathf.PerlinNoise(x * 5.3f, z * 5.3f + 9f)));
                }
            }
        }

        // Is this character standing on the island's snow? (The island's ground is its "Island ..." tiles. Anything
        // else under the feet, a rock or a cabin floor, hides the snow: then it must be within reach just below.)
        bool OnSnow(Transform t)
        {
            Vector3 p = t.position;
            float snow = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider c = hit.collider;
                if (c != null && c.name.StartsWith("Island ") && hit.distance < snow) snow = hit.distance;
            }
            if (snow == float.MaxValue) return false;
            if (!toldGround)
            {
                toldGround = true;
                Plugin.L.LogInfo("Winter Walk: a player is over the snow, " + (snow - 0.6f).ToString("0.00") + " m above it");
            }
            return snow - 0.6f < BodyHeight + 0.25f;
        }

        void Follow(IntPtr key, Transform t)
        {
            walkers.TryGetValue(key, out Walker w);
            Vector3 p = t.position;
            bool onSnow = OnSnow(t);
            if (onSnow && w.onSnow)
            {
                float moved = new Vector2(p.x - w.last.x, p.z - w.last.z).magnitude;
                if (moved > 1.5f) w.last = p;      // carried somewhere else: no track between
                else if (moved > 0.03f)
                {
                    if (!toldTrack) { toldTrack = true; Plugin.L.LogInfo("Winter Walk: a player is leaving a track in the snow"); }
                    Line(w.last, p, moved / Mathf.Max(Time.deltaTime, 0.001f));
                    w.last = p;
                }
            }
            else w.last = p;
            w.onSnow = onSnow;
            walkers[key] = w;
        }

        // ---- snowfall fills the tracks in ----

        void Relax()
        {
            if (relaxQueue.Count == 0)
            {
                if (tiles.Count == 0) return;
                relaxQueue.AddRange(tiles.Keys);
            }
            long key = relaxQueue[relaxQueue.Count - 1];
            relaxQueue.RemoveAt(relaxQueue.Count - 1);
            if (!tiles.TryGetValue(key, out byte[] tile)) return;
            // far from this player: forget it (the window is 64 m across, so a tile this far off is not in it)
            {
                float cx = (((int)(key >> 32) << TileBits) + TileSide / 2) * Texel - camAt.x, cz = (((int)(uint)(key & 0xffffffffL) << TileBits) + TileSide / 2) * Texel - camAt.z;
                if (cx * cx + cz * cz > Forget * Forget)
                {
                    tiles.Remove(key);
                    relaxedAt.Remove(key);
                    cachedKey = long.MinValue; cachedTile = null;
                    Tiles = tiles.Count;
                    return;
                }
            }
            int steps = (int)((Time.time - relaxedAt[key]) / SecondsPerStep);
            if (steps <= 0) return;
            relaxedAt[key] += steps * SecondsPerStep;
            bool any = false;
            for (int i = 0; i < tile.Length; i++)
            {
                int b = tile[i];
                if (b == Level) continue;
                b = b < Level ? Mathf.Min(Level, b + steps) : Mathf.Max(Level, b - steps);
                tile[i] = (byte)b;
                any |= b != Level;
            }
            int tx = (int)(key >> 32), tz = (int)(uint)(key & 0xffffffffL);
            if (!any)
            {
                tiles.Remove(key);
                relaxedAt.Remove(key);
                cachedKey = long.MinValue; cachedTile = null;
            }
            // show it, where the tile is in the window
            int gx0 = tx << TileBits, gz0 = tz << TileBits;
            if (!haveOrigin || gx0 + TileSide <= ox || gx0 >= ox + N || gz0 + TileSide <= oz || gz0 >= oz + N) return;
            for (int j = 0; j < TileSide; j++)
            {
                int gz = gz0 + j;
                if (gz < oz || gz >= oz + N) continue;
                for (int i = 0; i < TileSide; i++)
                {
                    int gx = gx0 + i;
                    if (gx >= ox && gx < ox + N) window[((gz & (N - 1)) * N) | (gx & (N - 1))] = any ? tile[(j << TileBits) | i] : Level;
                }
            }
            dirty = true;
        }

        void DrawTest(Camera cam)
        {
            Vector3 c = cam.transform.position, f = cam.transform.forward, r = cam.transform.right;
            f.y = 0f; f.Normalize(); r.y = 0f; r.Normalize();
            // a walked line going away, wandering a little
            Vector3 last = c + f * 2.5f - r * 1.2f;
            for (int i = 1; i <= 60; i++)
            {
                Vector3 next = c + f * (2.5f + i * 0.2f) - r * (1.2f + Mathf.Sin(i * 0.25f) * 0.5f);
                Line(last, next, 1.5f);
                last = next;
            }
            // a slid curve crossing the view
            last = c + f * 4f + r * 5f;
            for (int i = 1; i <= 60; i++)
            {
                float a = i / 60f;
                Vector3 next = c + f * (4f + a * 9f + Mathf.Sin(a * 6f) * 1.2f) + r * (5f - a * 9f);
                Line(last, next, 8f);
                last = next;
            }
            Plugin.L.LogInfo("Winter Walk: drew the test tracks (" + Stamps + " stamps so far, " + tiles.Count + " tiles)");
        }

        void LateUpdate()
        {
            using var diag = Diag.Time("tracks");
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 c = cam.transform.position;
                camAt = c;
                Tiles = tiles.Count;
                bool near = Plugin.Tracks.Value && c.x > Dresser.X0 - 100f && c.x < Dresser.X1 + 100f && c.z > Dresser.Z0 - 100f && c.z < Dresser.Z1 + 100f;
                if (!near)
                {
                    if (active) { Shader.SetGlobalFloat("_BwmTrail", 0f); active = false; }
                    if (tiles.Count > 0) { tiles.Clear(); relaxedAt.Clear(); relaxQueue.Clear(); cachedKey = long.MinValue; cachedTile = null; Tiles = 0; }
                    return;
                }
                if (texture == null)
                {
                    texture = new Texture2D(N, N, TextureFormat.R8, false, true);
                    texture.name = "Winter Walk snow tracks";
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    haveOrigin = false;
                    active = false;
                }
                Centre(c);

                Il2CppSystem.Collections.Generic.List<PlayerCharacter> all = PlayerCharacter.allPlayerCharacters;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        PlayerCharacter pc = all[i];
                        if (pc != null) Follow(pc.Pointer, pc.transform);
                    }
                if (TestPattern) { TestPattern = false; DrawTest(cam); }
                Relax();
                if (dirty) { Send(); dirty = false; }

                if (!active)
                {
                    Shader.SetGlobalTexture("_BwmTrailTex", texture);
                    Shader.SetGlobalFloat("_BwmTrail", 1f);
                    active = true;
                }
                // x: texture coordinates per metre; y: full strength this near the camera; z: fades to nothing by here
                Shader.SetGlobalVector("_BwmTrailParams", new Vector4(1f / (N * Texel), Reach, Reach + 5f, Texel));
            }
            catch (Exception e)
            {
                if (!failed) Plugin.L.LogWarning("Winter Walk: snow tracks stopped: " + e.GetType().Name + ": " + e.Message);
                failed = true;      // said once
                Shader.SetGlobalFloat("_BwmTrail", 0f);
                active = false;
            }
        }
    }
}
