// The Winter Isle's set dressing: where copies of the game's own granite rocks, trees, shrubs and logs go.
//
// The island's build calls Dress() with its height grid and what it knows about paths and puzzle sites. This
// decides the places and writes them to WinterIsle.dressing beside the Winter Walk plugin, which makes the
// copies in the game and puts snow on them (see winterwalk\Dresser.cs). Sizes come from world\catalog.tsv,
// which the companion writes from the running game ('bwm.ps1 game catalog').
//
// What goes where, modelled on the base game's island:
//   - tors: piles of big rounded granite boulders on summits, ridges and knolls, smaller stones perched on top
//   - outcrops: big granite masses where the ground is too steep for snow, so cliffs are real rock
//   - the coast: boulders tumbled along the waterline between the beaches, and a few sea stacks
//   - loose boulders scattered across the slopes
//   - plants by place: salt-bitten heath by the sea, woodland and thickets on sheltered ground, grass trees and
//     button grass on the open tops, dead trees and fallen logs throughout
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bwm;
using UnityEngine;

public static class WinterIsleDressing
{
    // What the island's build knows, handed over to place things
    public sealed class Land
    {
        public Func<Vector2, float> Height;          // ground height (NaN off the island)
        public Func<Vector2, float> Slope;           // steepest gradient there (1 = 45 degrees)
        public Func<Vector2, float> ToShore;         // metres to the waterline (0 on it, negative out at sea)
        public Func<Vector2, float> ToPath;          // metres to the nearest trodden path or the plaza
        public Func<Vector2, float, bool> Free;      // clear of paths, plaza, lake and puzzle sites by a margin
        public Func<Vector2, bool> Beach;            // part of a sandy cove: no rocks
        public Action<Vector2, float> Block;         // marks ground no puzzle may be sited on
        public float X0, X1, Z0, Z1;
        public int Seed = 1224;
    }

    struct Kind { public string name; public bool tree; public Vector3 centre, size; }
    static Dictionary<string, Kind> catalog;

    static void LoadCatalog()
    {
        string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "world", "catalog.tsv"));
        if (!File.Exists(file)) throw new Exception("world\\catalog.tsv is missing: start a walk and run 'bwm.ps1 game catalog'");
        catalog = new Dictionary<string, Kind>();
        foreach (string line in File.ReadAllLines(file))
        {
            string[] p = line.Split('\t');
            if (p.Length < 8 || p[0] == "kind") continue;
            float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            catalog[p[0]] = new Kind { name = p[0], tree = p[1] == "tree", centre = new Vector3(F(2), F(3), F(4)), size = new Vector3(F(5), F(6), F(7)) };
        }
    }

    static StringBuilder output;
    static System.Random random;
    static Land land;
    static int rocks, plants;
    static readonly List<(Vector2 at, float radius)> solids = new List<(Vector2, float)>();      // rocks placed, to keep plants out of them
    public static readonly List<(Vector2 at, float radius)> Cover = new List<(Vector2, float)>();  // plants placed: the ground under them shows through the snow
    public static readonly List<(Vector2 at, float height, int seed)> Firs = new List<(Vector2, float, int)>();   // our own firs, built into the island's pack

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static T Pick<T>(T[] items) => items[random.Next(items.Length)];

    static bool InsideRock(Vector2 p, float margin)
    {
        foreach (var s in solids) if ((p - s.at).sqrMagnitude < (s.radius + margin) * (s.radius + margin)) return true;
        return false;
    }

    // The lowest ground under a footprint, so nothing floats on a slope
    static float LowGround(Vector2 p, float radius)
    {
        float low = land.Height(p);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            float h = land.Height(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            if (!float.IsNaN(h)) low = Mathf.Min(low, h);
        }
        return low;
    }

    // A rock 'diameter' metres across, its bottom 'sink' of its height below 'ground'. Returns its top's height.
    static float Rock(string kind, Vector2 p, float ground, float diameter, float sink, float snow, float tilt = 8f, bool onTerrain = true, bool block = true)
    {
        if (!catalog.TryGetValue(kind, out Kind k)) throw new Exception("No '" + kind + "' in world\\catalog.tsv");
        if (onTerrain)
        {
            // never float: the rock's bottom goes below the lowest ground under it, deeper on a slope
            ground = Mathf.Min(ground, LowGround(p, diameter * 0.4f));
            sink += Mathf.Clamp01(land.Slope(p)) * 0.15f;
        }
        float scale = diameter / Mathf.Max(k.size.x, k.size.z);
        float h = k.size.y * scale;
        float bottom = (k.centre.y - k.size.y / 2f) * scale;
        float y = ground - sink * h - bottom;
        output.Append(kind).Append('\t').Append(F(p.x)).Append('\t').Append(F(y)).Append('\t').Append(F(p.y)).Append('\t')
            .Append(F(R(0f, 360f))).Append('\t').Append(F(R(-tilt, tilt))).Append('\t').Append(F(R(-tilt, tilt))).Append('\t')
            .Append(F(scale)).Append('\t').Append(F(snow)).Append('\n');
        solids.Add((p, diameter * 0.45f));
        if (block) land.Block(p, diameter * 0.55f + 2f);
        rocks++;
        return y + (k.centre.y + k.size.y / 2f) * scale;
    }

    // A plant about 'height' metres tall (or the catalogue's own size times 'scale' when height is 0)
    static void Plant(string kind, Vector2 p, float scale, float snow = 0.3f)
    {
        if (!catalog.ContainsKey(kind)) throw new Exception("No '" + kind + "' in world\\catalog.tsv");
        float y = land.Height(p) - 0.08f;
        Kind k = catalog[kind];
        Cover.Add((p, Mathf.Max(k.size.x, k.size.z) * scale * 0.4f));
        output.Append("tree:").Append(kind).Append('\t').Append(F(p.x)).Append('\t').Append(F(y)).Append('\t').Append(F(p.y)).Append('\t')
            .Append(F(R(0f, 360f))).Append("\t0\t0\t").Append(F(scale)).Append('\t').Append(F(snow)).Append('\n');
        plants++;
    }

    static readonly string[] BigRound = { "rock-whale", "rock-boulder01", "rock-quadrasphere", "rock-dome", "rock-bullet" };
    static readonly string[] SmallRound = { "rock-dome", "rock-pointy", "rock-bullet", "rock-boulder01", "rock-wedge" };
    static readonly string[] Masses = { "rock-cliff01", "rock-cliff01", "rock-knuckles", "rock-whale" };

    // A tor: a pile of big boulders with a few smaller ones on top
    static void Tor(Vector2 centre, float size)
    {
        int bases = random.Next(2, 5);
        var tops = new List<(Vector2 at, float top, float d)>();
        for (int i = 0; i < bases; i++)
        {
            float a = R(0f, Mathf.PI * 2f), r = i == 0 ? 0f : R(0.25f, 0.5f) * size;
            Vector2 p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (float.IsNaN(land.Height(p)) || !land.Free(p, size * 0.3f)) continue;
            float d = size * R(0.55f, 1f) * (i == 0 ? 1f : 0.8f);
            float top = Rock(Pick(BigRound), p, LowGround(p, d * 0.4f), d, R(0.3f, 0.45f), R(0.35f, 0.5f));
            tops.Add((p, top, d));
        }
        int perched = random.Next(0, 4);
        for (int i = 0; i < perched && tops.Count > 0; i++)
        {
            var under = tops[random.Next(tops.Count)];
            Vector2 p = under.at + new Vector2(R(-0.2f, 0.2f), R(-0.2f, 0.2f)) * under.d;
            float d = under.d * R(0.25f, 0.5f);
            Rock(Pick(SmallRound), p, under.top - under.d * 0.12f, d, R(0.25f, 0.4f), 0.4f, 14f, false);
        }
        // scree: stones fallen from the tor, lying round its foot
        if (tops.Count == 0) return;
        int scree = random.Next(3, 9);
        for (int i = 0; i < scree; i++)
        {
            float a = R(0f, Mathf.PI * 2f), r = R(0.55f, 1.05f) * size;
            Vector2 p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (float.IsNaN(land.Height(p)) || !land.Free(p, 0.5f) || InsideRock(p, 0.3f)) continue;
            Rock(Pick(SmallRound), p, LowGround(p, 0.6f), R(0.8f, 2.6f), R(0.3f, 0.5f), 0.4f, 20f);
        }
    }

    // A band of granite breaking out of a mountainside: a few masses in a row across the slope, sunk deep so they
    // read as bedrock showing through, not boulders set on top
    static void RockBand(Vector2 at, Vector2 across, float length)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(length / 9f));
        for (int i = 0; i < n; i++)
        {
            Vector2 p = at + across * ((i / (float)(n - 1) - 0.5f) * length) + new Vector2(R(-2.5f, 2.5f), R(-2.5f, 2.5f));
            if (float.IsNaN(land.Height(p)) || !land.Free(p, 2f) || InsideRock(p, 1.5f)) continue;
            float d = R(8f, 16f);
            Rock(Pick(Masses), p, LowGround(p, d * 0.4f), d, R(0.45f, 0.6f), 0.55f, 5f);
        }
    }

    public static void Dress(Land l, string outputFile)
    {
        land = l;
        LoadCatalog();
        output = new StringBuilder();
        random = new System.Random(l.Seed);
        solids.Clear();
        Cover.Clear();
        Firs.Clear();
        rocks = plants = 0;
        output.Append("# Winter Isle set dressing, written by WinterIslePuzzle's build. kind, x, y, z, yaw, pitch, roll, scale, snow line\n");
        output.Append("@bounds\t").Append(F(l.X0)).Append('\t').Append(F(l.X1)).Append('\t').Append(F(l.Z0)).Append('\t').Append(F(l.Z1)).Append('\n');

        // ---- tors: on knolls (higher than the ground round them) and in rocky ground ----
        int tors = 0;
        for (float z = l.Z0; z < l.Z1; z += 15f)
            for (float x = l.X0; x < l.X1; x += 15f)
            {
                var p = new Vector2(x + R(-6f, 6f), z + R(-6f, 6f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 2f || l.ToShore(p) < 12f) continue;
                float around = 0f; int n = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f, hh = l.Height(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 30f);
                    if (!float.IsNaN(hh)) { around += hh; n++; }
                }
                float knoll = n > 0 ? h - around / n : 0f;
                float rocky = Mathf.PerlinNoise(p.x * 0.012f + 31.7f, p.y * 0.012f + 8.2f);
                float chance = Mathf.Clamp01(knoll * 0.18f) + (rocky > 0.52f ? 0.45f : 0.06f) + (h > 30f ? 0.3f : 0f);
                if (R(0f, 1f) > chance || l.Slope(p) > 0.85f || !l.Free(p, 6f) || InsideRock(p, 3f)) continue;
                Tor(p, R(7f, 20f) * (h > 30f ? 1.3f : 1f) * (knoll > 4f ? 1.25f : 1f));
                tors++;
            }

        // ---- rock bands: bedrock breaking out across the mountainsides, along the contours ----
        int bands = 0;
        for (float z = l.Z0; z < l.Z1; z += 26f)
            for (float x = l.X0; x < l.X1; x += 26f)
            {
                var p = new Vector2(x + R(-10f, 10f), z + R(-10f, 10f));
                float h = l.Height(p), s = l.Slope(p);
                if (float.IsNaN(h) || h < 18f || s < 0.3f || s > 1.2f || !l.Free(p, 6f)) continue;
                if (R(0f, 1f) > 0.55f) continue;
                // across the slope: at right angles to the way it falls
                float hx = l.Height(p + new Vector2(2f, 0f)) - l.Height(p - new Vector2(2f, 0f)), hz = l.Height(p + new Vector2(0f, 2f)) - l.Height(p - new Vector2(0f, 2f));
                if (float.IsNaN(hx) || float.IsNaN(hz)) continue;
                Vector2 across = new Vector2(-hz, hx).normalized;
                RockBand(p, across, R(18f, 40f));
                bands++;
            }

        // ---- outcrops: granite masses on the cliff bands and steepest slopes ----
        int outcrops = 0;
        for (float z = l.Z0; z < l.Z1; z += 19f)
            for (float x = l.X0; x < l.X1; x += 19f)
            {
                var p = new Vector2(x + R(-7f, 7f), z + R(-7f, 7f));
                float h = l.Height(p), s = l.Slope(p);
                if (float.IsNaN(h) || s < 0.7f || h < 1f || !l.Free(p, 4f) || InsideRock(p, 2f)) continue;
                if (R(0f, 1f) > Mathf.Clamp01((s - 0.7f) * 2.2f)) continue;
                string kind = Pick(Masses);
                float d = R(14f, 28f);
                Rock(kind, p, LowGround(p, d * 0.45f), d, R(0.25f, 0.4f), 0.5f, 6f);
                outcrops++;
            }

        // ---- the coast: boulders along the waterline, except on the beaches; a few sea stacks ----
        int coast = 0;
        for (float z = l.Z0; z < l.Z1; z += 5f)
            for (float x = l.X0; x < l.X1; x += 5f)
            {
                var p = new Vector2(x + R(-2f, 2f), z + R(-2f, 2f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h > 2.5f || h < -2.5f || l.Beach(p) || !l.Free(p, 2f) || InsideRock(p, 0.5f)) continue;
                float rough = Mathf.PerlinNoise(p.x * 0.03f + 5.5f, p.y * 0.03f + 91.2f);
                if (R(0f, 1f) > 0.12f + rough * 0.5f) continue;
                Rock(Pick(BigRound), p, Mathf.Min(h, 0.5f), R(3f, 11f) * (0.6f + rough), R(0.25f, 0.45f), 0.45f, 18f);
                coast++;
            }
        // skerries: sea stacks and half-drowned boulders off the rocky shores, in little clusters
        for (int i = 0; i < 1500; i++)
        {
            var p = new Vector2(R(l.X0, l.X1), R(l.Z0, l.Z1));
            float toShore = l.ToShore(p);
            if (toShore > -6f || toShore < -40f || l.Beach(p) || InsideRock(p, 3f) || R(0f, 1f) > 0.14f) continue;
            Rock(Pick(new[] { "rock-pillar", "rock-whale", "rock-boulder01", "rock-knuckles" }), p, -3f, R(5f, 12f), 0.15f, 0.5f, 6f);
            coast++;
            int around = random.Next(0, 4);
            for (int j = 0; j < around; j++)
            {
                Vector2 q = p + new Vector2(R(-12f, 12f), R(-12f, 12f));
                if (l.ToShore(q) > -3f || InsideRock(q, 0.5f)) continue;
                Rock(Pick(BigRound), q, -2.2f, R(2.5f, 6f), 0.2f, 0.5f, 15f);
                coast++;
            }
        }

        // ---- loose boulders across the slopes ----
        int loose = 0;
        for (float z = l.Z0; z < l.Z1; z += 6.5f)
            for (float x = l.X0; x < l.X1; x += 6.5f)
            {
                var p = new Vector2(x + R(-3f, 3f), z + R(-3f, 3f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 1f || !l.Free(p, 1f) || InsideRock(p, 1f)) continue;
                float stony = Mathf.PerlinNoise(p.x * 0.02f + 13.7f, p.y * 0.02f + 55.1f);
                if (R(0f, 1f) > (stony - 0.42f) * 0.5f + 0.02f + (h < 30f ? l.Slope(p) * 0.1f : 0f)) continue;
                Rock(Pick(SmallRound), p, LowGround(p, 1f), R(1.2f, 4f), R(0.25f, 0.45f), 0.4f, 15f);
                loose++;
            }

        // ---- firs: groves on the middle slopes, thinning at their edges, and a few standing alone ----
        for (float z = l.Z0; z < l.Z1; z += 4.6f)
            for (float x = l.X0; x < l.X1; x += 4.6f)
            {
                var p = new Vector2(x + R(-2.1f, 2.1f), z + R(-2.1f, 2.1f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 2.5f || h > 62f || l.Slope(p) > 0.6f || l.ToShore(p) < 22f || l.Beach(p)) continue;
                float grove = Mathf.PerlinNoise(p.x * 0.0105f + 211.3f, p.y * 0.0105f + 87.4f) + (Mathf.PerlinNoise(p.x * 0.04f + 5.2f, p.y * 0.04f + 9.9f) - 0.5f) * 0.18f;
                float inGrove = Smooth01((grove - 0.46f) / 0.14f) * (1f - Smooth01((h - 50f) / 14f));
                // denser and thinner patches within a grove, and saplings feathering its edges
                float thick = Mathf.Lerp(0.6f, 1.05f, Mathf.PerlinNoise(p.x * 0.033f + 41.9f, p.y * 0.033f + 13.6f));
                float edge = inGrove > 0.02f && inGrove < 0.45f ? 0.18f : 0f;
                float chance = inGrove * 0.9f * thick + 0.015f + edge;
                if (R(0f, 1f) > chance || !l.Free(p, 1.5f) || InsideRock(p, 1.2f)) continue;
                float tallness = Mathf.Lerp(4.5f, 13.5f, Mathf.Clamp01(inGrove * R(0.55f, 1.15f))) * (h > 40f ? 0.8f : 1f);
                if (edge > 0f && R(0f, 1f) < 0.65f) tallness = R(1.8f, 4.2f);      // a sapling
                Firs.Add((p, tallness, random.Next(1, 1000000)));
                int band = Mathf.Clamp(Mathf.RoundToInt(tallness - 4.5f), 0, 9);
                output.Append("fir:").Append(band * 2 + random.Next(2)).Append('\t').Append(F(p.x)).Append('\t').Append(F(h - 0.05f)).Append('\t').Append(F(p.y)).Append('\t')
                    .Append(F(R(0f, 360f))).Append("\t0\t0\t").Append(F(tallness / (4.5f + band))).Append("\t-9\n");
                solids.Add((p, 1.1f));
                Cover.Add((p, tallness * 0.22f));
            }

        // ---- plants ----
        string[] heath = { "TeatreeCoast_Solo", "TeatreeCoast_Wall", "PovertyBush", "SeaberrySaltbush", "CoastSaltbush", "CorreaWhite_Bush", "CoastBeardHeath_Tall", "CoastalWattle_Bush", "CommonBoobialla", "TeatreeCoast_Bush" };
        string[] shoreTrees = { "TeatreeCoast_Hero", "TeatreeCoast", "BanksiaCoast", "BanksiaCoast_Senior", "TeatreeCoast_Dead_Tall" };
        // winter woodland: mostly bare, dead-looking broadleaf (evergreen gum and banksia read as summer from afar,
        // where only their impostors draw), a few hardy evergreens, and thickets
        string[] woodland = { "SheoakDrooping_Short", "Paperbark_Thicket", "KangarooApple", "HazelPomaderris", "AustralianBlackthorn_Bush", "BanksiaCoast_Dead", "TeatreeCoast_Dead", "BrownStringybark_Dead_Skinny", "Messmate_Dead_Short" };
        string[] tall = { "Messmate_Dead", "Messmate_Dead_Short", "BrownStringybark_Dead", "BrownStringybark_Dead_Skinny", "LillyPilly_ForestTall_Dead", "Messmate_Dead", "SheoakDrooping", "Blackwood" };
        string[] under = { "Paperbark_Bush", "BushyNeedlewood", "TreeEverlasting", "PricklyMoses", "FernMan_Short", "BrownStringybark_Bush", "AustralianMulberry", "TreeBroomHeath" };
        string[] tops = { "GrassTree", "GrassTree_Short", "GrassTree_NoFlower", "ButtonGrass", "PricklyMoses", "TreeEverlasting" };
        string[] dead = { "Messmate_Dead", "Messmate_Dead_Short", "BrownStringybark_Dead", "BrownStringybark_Dead_Skinny", "BanksiaCoast_Dead", "TeatreeCoast_Dead", "LillyPilly_ForestTall_Dead" };
        string[] logs = { "Litter_Log_Dry", "Litter_Log_Dry_Small", "Litter_Log_Wet", "Litter_Log_Wet_Small", "Litter_Stump_Wet" };
        for (float z = l.Z0; z < l.Z1; z += 2.4f)
            for (float x = l.X0; x < l.X1; x += 2.4f)
            {
                var p = new Vector2(x + R(-1.1f, 1.1f), z + R(-1.1f, 1.1f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 0.7f) continue;
                float s = l.Slope(p), shore = l.ToShore(p);
                if (s > 0.75f || InsideRock(p, 0.6f) || !l.Free(p, 0.5f) || l.Beach(p)) continue;
                float thicket = Mathf.PerlinNoise(p.x * 0.018f + 71.3f, p.y * 0.018f + 4.4f);
                float clump = Mathf.PerlinNoise(p.x * 0.07f + 2.1f, p.y * 0.07f + 17.3f);
                float roll = R(0f, 1f);
                if (shore < 35f)
                {
                    // heath by the sea: low and dense, a few wind-bent trees
                    if (roll < 0.05f * clump) Plant(Pick(shoreTrees), p, R(0.55f, 0.9f));
                    else if (roll < 0.30f * clump + 0.04f) Plant(Pick(heath), p, R(0.55f, 1.1f));
                }
                else if (h > 34f)
                {
                    if (roll < 0.06f + 0.10f * clump) Plant(Pick(tops), p, R(0.7f, 1.05f));
                    else if (roll < 0.075f + 0.10f * clump) Plant(Pick(dead), p, R(0.5f, 0.85f), 0.25f);
                }
                else
                {
                    float wood = Mathf.Clamp01((thicket - 0.38f) * 3f);
                    float a = 0.035f * wood, b = a + 0.13f * wood * clump + 0.022f, c = b + 0.02f + 0.05f * (1f - wood) * clump;
                    if (roll < 0.006f * wood && s < 0.4f && !InsideRock(p, 4f)) Plant(Pick(tall), p, R(0.55f, 0.95f), 0.32f);
                    else if (roll < a) Plant(Pick(woodland), p, R(0.6f, 1.0f));
                    else if (roll < b) Plant(Pick(under), p, R(0.6f, 1.1f));
                    else if (roll < c) Plant(Pick(tops), p, R(0.7f, 1.0f));
                    else if (roll < c + 0.008f) Plant(Pick(dead), p, R(0.5f, 0.9f), 0.25f);
                    else if (roll < c + 0.018f && s < 0.35f) Plant(Pick(logs), p, R(0.8f, 1.1f), 0.35f);
                }
            }

        // ---- scatter: what pokes through the snow underfoot (tufts, twigs, stone tips, sticks, low bushes), thickest
        //      beside the paths where people walk and look down ----
        int scattered = 0;
        string[] tufts = { "ButtonGrass", "ButtonGrass", "SaltMarshRush", "GrassTree_Short_NoFlower", "TeatreeCoast_Dead", "TeatreeCoast_Dead" };
        string[] sticks = { "Litter_Log_Dry_Small", "Litter_Log_Wet_Small", "Litter_Stump_Wet" };
        string[] low = { "PovertyBush", "CoastSaltbush", "TeatreeCoast_Bush", "BushyNeedlewood" };
        for (float z = l.Z0; z < l.Z1; z += 3f)
            for (float x = l.X0; x < l.X1; x += 3f)
            {
                var p = new Vector2(x + R(-1.4f, 1.4f), z + R(-1.4f, 1.4f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 0.8f || l.Slope(p) > 0.7f || l.Beach(p)) continue;
                float toPath = l.ToPath(p);
                if (toPath < 2.2f || InsideRock(p, 0.3f) || !l.Free(p, -1.3f)) continue;
                float near = 1f - Smooth01((toPath - 4f) / 40f);
                float patch = Mathf.PerlinNoise(p.x * 0.05f + 91.3f, p.y * 0.05f + 27.7f);
                if (R(0f, 1f) > (0.09f + 0.14f * near) * (0.4f + patch * 1.2f)) continue;
                float pick = R(0f, 1f);
                if (pick < 0.5f) Plant(Pick(tufts), p, R(0.28f, 0.5f), 0.3f);
                else if (pick < 0.75f) Rock(Pick(SmallRound), p, LowGround(p, 0.3f), R(0.35f, 1.1f), R(0.45f, 0.65f), 0.35f, 25f, true, false);
                else if (pick < 0.9f) Plant(Pick(sticks), p, R(0.35f, 0.7f), 0.35f);
                else Plant(Pick(low), p, R(0.3f, 0.55f), 0.3f);
                scattered++;
            }

        File.WriteAllText(outputFile, output.ToString());
        Debug.Log("[BWM] Winter Isle dressing: " + Firs.Count + " firs, " + rocks + " rocks (" + tors + " tors, " + bands + " rock bands, " + outcrops + " outcrops, " + coast + " on the coast, " + loose + " loose), "
            + plants + " plants (" + scattered + " of them scatter); written to " + outputFile);
    }
}
