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
        public Action<Vector2, float, float> Bank;   // banks snow against something: at, radius, height
        public List<(Vector2 at, float radius, bool birch)> Woods = new List<(Vector2, float, bool)>();      // dense stands the trails run through
        public List<(Vector2 at, float radius)> Islets = new List<(Vector2, float)>();                       // small islets, to be built of granite
        public Func<Vector2, float> Forest = p => 0f;      // 1 where the ground is to be dense forest whatever else (between the ski runs)
        public Vector2 Lake;                         // the frozen lake's centre
        public float LakeRadius;
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
    static readonly List<(Vector2 at, float diameter)> big = new List<(Vector2, float)>();     // rocks big enough to bank snow against
    public static readonly List<(Vector2 at, float radius)> Cover = new List<(Vector2, float)>();  // plants placed: the ground under them shows through the snow
    public static readonly List<(Vector2 at, float radius)> Needles = new List<(Vector2, float)>();   // under our conifers: needle litter, not heath
    public static readonly List<(Vector2 at, float radius)> Scree = new List<(Vector2, float)>();     // at a rock's foot: stones
    public static readonly List<(Vector2 at, float height, int seed)> Firs = new List<(Vector2, float, int)>();   // our own firs, built into the island's pack

    // Our own trees. Each is one of these models, which the island's build makes and Winter Walk copies to every
    // place the dressing list gives ("fir:<index>", turned and scaled a little): firs, slim spruces and bare birches
    public static readonly List<(string kind, float height, int seed)> TreeModels = Models();
    static List<(string kind, float height, int seed)> Models()
    {
        var list = new List<(string, float, int)>();
        for (int v = 0; v < 20; v++) list.Add(("fir", 4.5f + (v / 2) * 1f, 100 + v));
        for (int v = 0; v < 10; v++) list.Add(("spruce", 7f + (v / 2) * 2.5f, 300 + v));
        for (int v = 0; v < 8; v++) list.Add(("birch", 5.5f + (v / 2) * 1.5f, 500 + v));
        for (int v = 0; v < 8; v++) list.Add(("pine", 9f + (v / 2) * 2.5f, 700 + v));      // a tall bare trunk and a high crown: a walker passes under it
        return list;
    }

    // Stands one of our trees at p: the model of that kind nearest in height, scaled to fit
    static void Tree(string kind, Vector2 p, float ground, float tallness, float trunk, float cover)
    {
        int best = -1;
        for (int i = 0; i < TreeModels.Count; i++)
            if (TreeModels[i].kind == kind && (best < 0 || Mathf.Abs(TreeModels[i].height - tallness) < Mathf.Abs(TreeModels[best].height - tallness) - 0.001f)) best = i;
        best += random.Next(2);      // each height comes in two shapes
        output.Append("fir:").Append(best).Append('\t').Append(F(p.x)).Append('\t').Append(F(ground - 0.05f)).Append('\t').Append(F(p.y)).Append('\t')
            .Append(F(R(0f, 360f))).Append("\t0\t0\t").Append(F(tallness / TreeModels[best].height)).Append("\t-9\n");
        solids.Add((p, trunk));
        Cover.Add((p, cover));
        if (kind != "birch") Needles.Add((p, cover * 1.15f));
    }

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
        if (onTerrain && diameter > 2.5f && land.Forest(p) > 0.4f) return ground;
        if (onTerrain && (p - WinterIslePuzzle.Peak).magnitude < 1500f && WinterIslePuzzle.Piste(p) > 0.15f) return ground;
        if (onTerrain && diameter > 1.2f && ground > 45f && (p - WinterIslePuzzle.Peak).magnitude < 1500f && land.Slope(p) > 0.55f) return ground;      // none stuck on the mountain's steep faces: they lie at its foot
        if (onTerrain && diameter > 1.5f && (p - WinterIslePuzzle.Peak).magnitude < 1500f && WinterIslePuzzle.SkiFace(p) > 0.5f && R(0f, 1f) > 0.15f) return ground;
        if (onTerrain && diameter > 1.2f && (p - WinterIslePuzzle.Peak).magnitude < 1500f && WinterIslePuzzle.SkiFace(p) > 0.3f && ground > WinterIslePuzzle.TreeLine - 60f) return ground;      // nothing along the bowl's lip: a row of brown boulders hid the run below      // on the ski face rock is a few outcrops, not a band across it      // no rock on a piste or in the open bowl, however steep      // the forest between the ski runs is trees, not a boulder field
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
        if (onTerrain && diameter >= 3.5f) big.Add((p, diameter));
        if (onTerrain && diameter >= 1.5f) { Cover.Add((p, diameter * 0.55f)); Scree.Add((p, diameter * 0.62f)); }      // the wind scours the ground bare at a rock's foot
        rocks++;
        return y + (k.centre.y + k.size.y / 2f) * scale;
    }

    // A plant about 'height' metres tall (or the catalogue's own size times 'scale' when height is 0)
    static void Plant(string kind, Vector2 p, float scale, float snow = 0.42f)      // (at 0.3 whole leaves went white; at 0.55 they kept their summer green)
    {
        if (!catalog.ContainsKey(kind)) throw new Exception("No '" + kind + "' in world\\catalog.tsv");
        float y = land.Height(p) - 0.08f;
        Kind k = catalog[kind];
        // nothing of the game's own flora up the ski mountain: above the trees it is snow and rock, and on the ski face
        // only our firs grow (grass trees in an alpine bowl read wrong)
        if ((p - WinterIslePuzzle.Peak).magnitude < 1500f && (land.Height(p) > WinterIslePuzzle.TreeLine - 40f || WinterIslePuzzle.SkiFace(p) > 0.4f) && !kind.Contains("Log") && !kind.Contains("Litter")) return;
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
            Rock(Pick(Masses), p, LowGround(p, d * 0.4f), d, R(0.2f, 0.35f), 0.55f, 5f);
        }
    }

    public static void Dress(Land l, string outputFile)
    {
        land = l;
        LoadCatalog();
        output = new StringBuilder();
        random = new System.Random(l.Seed);
        solids.Clear();
        big.Clear();
        Cover.Clear();
        Needles.Clear();
        Scree.Clear();
        Firs.Clear();
        rocks = plants = 0;
        output.Append("# Winter Isle set dressing, written by WinterIslePuzzle's build. kind, x, y, z, yaw, pitch, roll, scale, snow line\n");
        output.Append("@bounds\t").Append(F(l.X0)).Append('\t').Append(F(l.X1)).Append('\t').Append(F(l.Z0)).Append('\t').Append(F(l.Z1)).Append('\n');

        // ---- the massif: the high ground built of granite, big masses sunk deep, so the mountain is rock with
        //      snow between rather than a snowy hill with boulders on it (placed first, so nothing crowds them) ----
        int massif = 0;
        for (float z = l.Z0; z < l.Z1; z += 15f)
            for (float x = l.X0; x < l.X1; x += 15f)
            {
                var p = new Vector2(x + R(-6f, 6f), z + R(-6f, 6f));
                float h = l.Height(p), s = l.Slope(p);
                if (float.IsNaN(h) || h < 38f || !l.Free(p, 3f) || InsideRock(p, 4f)) continue;
                if (R(0f, 1f) > 0.45f + Mathf.Clamp01(s - 0.3f) * 0.5f + Mathf.Clamp01((h - 60f) / 40f) * 0.3f) continue;
                float d = R(18f, 36f) * (h > 70f ? 1.15f : 1f);
                Rock(Pick(Masses), p, LowGround(p, d * 0.4f), d, R(0.12f, 0.25f), 0.5f, 6f);      // already lowered to the foot of its slope
                massif++;
            }

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
        for (float z = l.Z0; z < l.Z1; z += 10f)
            for (float x = l.X0; x < l.X1; x += 10f)
            {
                var p = new Vector2(x + R(-4f, 4f), z + R(-4f, 4f));
                float h = l.Height(p), s = l.Slope(p);
                if (float.IsNaN(h) || s < 0.5f || h < 1f || !l.Free(p, 4f) || InsideRock(p, 2f)) continue;
                if (R(0f, 1f) > Mathf.Clamp01((s - 0.5f) * 2.5f)) continue;
                string kind = Pick(Masses);
                float d = R(14f, 32f);
                Rock(kind, p, LowGround(p, d * 0.45f), d, R(0.08f, 0.2f), 0.5f, 6f);
                outcrops++;
            }

        // ---- crests: the high ground's tops built of granite, big masses sunk deep along every rise ----
        for (float z = l.Z0; z < l.Z1; z += 14f)
            for (float x = l.X0; x < l.X1; x += 14f)
            {
                var p = new Vector2(x + R(-5f, 5f), z + R(-5f, 5f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 30f || !l.Free(p, 4f) || InsideRock(p, 3f)) continue;
                float around = 0f; int n = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f, hh = l.Height(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 22f);
                    if (!float.IsNaN(hh)) { around += hh; n++; }
                }
                if (n == 0 || h - around / n < 2f || R(0f, 1f) > 0.7f) continue;
                float d = R(16f, 30f);
                Rock(Pick(Masses), p, LowGround(p, d * 0.4f), d, R(0.25f, 0.4f), 0.5f, 5f);
                outcrops++;
            }

        // ---- the coast: boulders along the waterline, except on the beaches; a few sea stacks ----
        int coast = 0;
        // islets are granite: a mass sunk into each, boulders round its foot
        foreach (var islet in l.Islets)
        {
            Rock(Pick(Masses), islet.at, LowGround(islet.at, islet.radius * 0.5f), islet.radius * R(1.5f, 1.9f), R(0.25f, 0.4f), 0.5f, 7f);
            int ring = random.Next(4, 8);
            for (int i = 0; i < ring; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 q = islet.at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * islet.radius * R(0.55f, 1.15f);
                if (InsideRock(q, 0.3f)) continue;
                float hq = l.Height(q);
                Rock(Pick(BigRound), q, float.IsNaN(hq) ? -2f : Mathf.Min(hq, 0.4f), R(3f, 8f), R(0.3f, 0.45f), 0.45f, 16f);
                coast++;
            }
        }
        for (float z = l.Z0; z < l.Z1; z += 4f)
            for (float x = l.X0; x < l.X1; x += 4f)
            {
                var p = new Vector2(x + R(-1.7f, 1.7f), z + R(-1.7f, 1.7f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h > 2.5f || h < -2.5f || l.Beach(p) || !l.Free(p, 2f) || InsideRock(p, 0.5f)) continue;
                float rough = Mathf.PerlinNoise(p.x * 0.03f + 5.5f, p.y * 0.03f + 91.2f);
                if (R(0f, 1f) > 0.2f + rough * 0.5f) continue;
                if (rough > 0.5f && R(0f, 1f) < 0.22f)
                    Rock("rock-cliff01", p, Mathf.Min(h, 0.3f), R(8f, 15f), R(0.5f, 0.62f), 0.5f, 5f);      // a shelf of rock running into the sea
                else
                    Rock(Pick(BigRound), p, Mathf.Min(h, 0.5f), R(3f, 11f) * (0.6f + rough), R(0.25f, 0.45f), 0.45f, 18f);
                coast++;
            }
        // skerries: sea stacks and half-drowned boulders off the rocky shores, in little clusters
        for (int i = 0; i < 1500; i++)
        {
            var p = new Vector2(R(l.X0, l.X1), R(l.Z0, l.Z1));
            float toShore = l.ToShore(p);
            if (toShore > -6f || toShore < -40f || l.Beach(p) || InsideRock(p, 3f) || R(0f, 1f) > 0.14f) continue;
            Rock(Pick(new[] { "rock-whale", "rock-boulder01", "rock-knuckles" }), p, -3f, R(4f, 9f), 0.25f, 0.5f, 6f);      // (no pillars: they stood round the mountain's foot like spikes)
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

        // ---- bedrock: slabs of granite flush with the ground (a big mass sunk until only its back shows), in the
        //      stony parts of the lowland; the rock is the island's frame, not something set down on the snow ----
        int slabs = 0;
        for (float z = l.Z0; z < l.Z1; z += 17f)
            for (float x = l.X0; x < l.X1; x += 17f)
            {
                var p = new Vector2(x + R(-7f, 7f), z + R(-7f, 7f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 2f || h > 40f || l.Slope(p) > 0.4f || !l.Free(p, 2f) || InsideRock(p, 2f)) continue;
                float stony = Mathf.PerlinNoise(p.x * 0.02f + 13.7f, p.y * 0.02f + 55.1f);
                if (R(0f, 1f) > 0.1f + (stony - 0.4f) * 1.2f) continue;
                float d = R(9f, 20f);
                Rock(Pick(new[] { "rock-cliff01", "rock-whale", "rock-dome" }), p, LowGround(p, d * 0.4f), d, R(0.7f, 0.84f), 0.55f, 4f);
                slabs++;
            }

        // ---- trailside rock: domes sunk to their shoulders within a few steps of the paths, each with cobbles at its
        //      foot, so a walker passes between rocks instead of beside a field of them ----
        int trailside = 0;
        for (float z = l.Z0; z < l.Z1; z += 5f)
            for (float x = l.X0; x < l.X1; x += 5f)
            {
                var p = new Vector2(x + R(-2.2f, 2.2f), z + R(-2.2f, 2.2f));
                float h = l.Height(p), toPath = l.ToPath(p);
                if (float.IsNaN(h) || h < 1.5f || toPath < 3.6f || toPath > 12f || l.Slope(p) > 0.5f || !l.Free(p, 0f) || InsideRock(p, 1.5f)) continue;
                if (R(0f, 1f) > 0.45f + 0.5f * Mathf.Clamp01((Mathf.PerlinNoise(p.x * 0.03f + 3.1f, p.y * 0.03f + 61.9f) - 0.45f) * 4f)) continue;
                float d = Mathf.Min(R(2.2f, 7.5f), (toPath - 1.6f) * 1.7f);
                Rock(Pick(BigRound), p, LowGround(p, d * 0.4f), d, R(0.38f, 0.58f), 0.45f, 10f);
                // a few more round it, smaller and turned differently, so it is a cluster and not one rock
                int companions = random.Next(0, 4);
                for (int i = 0; i < companions; i++)
                {
                    Vector2 q = p + new Vector2(R(-1f, 1f), R(-1f, 1f)).normalized * d * R(0.7f, 1.3f);
                    float dq = d * R(0.3f, 0.7f);
                    if (l.ToPath(q) < 2.5f || InsideRock(q, 0.2f) || float.IsNaN(l.Height(q))) continue;
                    Rock(Pick(BigRound), q, LowGround(q, dq * 0.4f), dq, R(0.35f, 0.6f), 0.45f, 18f);
                }
                int cobbles = random.Next(2, 6);
                for (int i = 0; i < cobbles; i++)
                {
                    Vector2 q = p + new Vector2(R(-1f, 1f), R(-1f, 1f)).normalized * d * R(0.45f, 0.85f);
                    if (l.ToPath(q) < 1.8f || InsideRock(q, 0.1f) || float.IsNaN(l.Height(q))) continue;
                    Rock(Pick(SmallRound), q, LowGround(q, 0.3f), R(0.35f, 1.3f), R(0.35f, 0.6f), 0.35f, 25f, true, false);
                }
                trailside++;
            }

        // ---- snow banked against the bigger rocks, before anything else is stood on that ground ----
        if (l.Bank != null) foreach (var r in big) l.Bank(r.at, r.diameter * 0.75f + 1.5f, Mathf.Min(0.22f * r.diameter, 1.4f));

        // ---- firs: groves on the middle slopes, thinning at their edges, with glades, young trees among the old,
        //      stands where slim spruces take over, and a few firs standing alone ----
        int spruces = 0, birches = 0, pines = 0;
        float Grove(Vector2 q) => Mathf.PerlinNoise(q.x * 0.0105f + 211.3f, q.y * 0.0105f + 87.4f) + (Mathf.PerlinNoise(q.x * 0.04f + 5.2f, q.y * 0.04f + 9.9f) - 0.5f) * 0.18f;
        for (float z = l.Z0; z < l.Z1; z += 4.6f)
            for (float x = l.X0; x < l.X1; x += 4.6f)
            {
              for (int twice = 0; twice < 2; twice++)      // the ski forest is planted twice over: from the air it must be a dark mass the runs are cut through
              {
                var p = new Vector2(x + R(-2.1f, 2.1f), z + R(-2.1f, 2.1f));
                float h = l.Height(p);
                if (twice == 1 && l.Forest(p) < 0.5f) continue;
                if (float.IsNaN(h) || h < 2.5f || h > 340f || l.Slope(p) > 0.6f || l.ToShore(p) < 22f || l.Beach(p)) continue;      // (the tree line is high on the great mountain)
                float forest = l.Forest(p);
                float inGrove = Mathf.Max(Smooth01((Grove(p) - 0.4f) / 0.14f) * (1f - Smooth01((h - 200f) / 60f)), forest);
                // glades: open ground inside a grove, a few tens of metres across
                if (forest < 0.5f && inGrove > 0.3f && Mathf.PerlinNoise(p.x * 0.031f + 77.7f, p.y * 0.031f + 31.3f) > 0.7f) continue;
                // the ski forest has glades too, larger and fewer, and clumps rather than an even stand
                if (forest >= 0.5f && WinterIslePuzzle.Piste(p) <= 0.005f && Mathf.PerlinNoise(p.x * 0.013f + 17.7f, p.y * 0.013f + 61.3f) > 0.62f) continue;
                // denser and thinner patches within a grove, and saplings feathering its edges
                float thick = Mathf.Lerp(0.6f, 1.05f, Mathf.PerlinNoise(p.x * 0.033f + 41.9f, p.y * 0.033f + 13.6f));
                float edge = inGrove > 0.02f && inGrove < 0.45f ? 0.18f : 0f;
                float chance = inGrove * 0.9f * thick + 0.015f + edge;
                // along a piste the forest is a wall: full-grown firs, close, no gaps (below the thinning under the tree line)
                float pisteHere = WinterIslePuzzle.Piste(p);
                bool wall = forest > 0.5f && pisteHere > 0.005f && pisteHere <= 0.3f && h < WinterIslePuzzle.TreeLine - 110f;
                if (wall) chance = 1f;
                if (R(0f, 1f) > chance || !l.Free(p, 1.5f) || InsideRock(p, 1.2f)) continue;
                float tallness = Mathf.Lerp(4.5f, 13.5f, Mathf.Clamp01(inGrove * R(0.55f, 1.15f))) * (h > 40f ? 0.8f : 1f);
                tallness *= Mathf.Lerp(1f, 0.45f, Smooth01((h - (WinterIslePuzzle.TreeLine - 60f)) / 60f));      // stunted over the last stretch below the tree line
                if (wall) tallness = Mathf.Max(tallness, R(9f, 13f));
                bool young = !wall && (edge > 0f ? R(0f, 1f) < 0.65f : R(0f, 1f) < 0.16f);      // saplings feather the edges and grow among the old trees
                // beside a path a grown fir's boughs would hang across it (and through a walker's eyes): there it is a
                // pine, its crown overhead, or a sapling
                bool byPath = l.ToPath(p) < tallness * 0.34f + 1.5f;
                if (byPath && tallness > 5f && R(0f, 1f) < 0.45f) young = true;
                if (young) tallness = R(1.4f, 4.2f);
                if (!young && (byPath || R(0f, 1f) < 0.1f) && h < 45f)
                {
                    Firs.Add((p, tallness, random.Next(1, 1000000)));
                    Tree("pine", p, h, Mathf.Max(8.5f, tallness * 1.2f), 0.6f, tallness * 0.2f);
                    pines++;
                    continue;
                }
                // spruces: slim and tall, taking over in some stands and scattered through the rest
                bool spruce = !young && R(0f, 1f) < (Mathf.PerlinNoise(p.x * 0.017f + 9.1f, p.y * 0.017f + 55.5f) > 0.56f ? 0.7f : 0.07f);
                Firs.Add((p, tallness, random.Next(1, 1000000)));
                if (spruce) { Tree("spruce", p, h, tallness * 1.3f, 0.9f, tallness * 0.14f); spruces++; }
                else Tree("fir", p, h, tallness, 1.1f, tallness * 0.22f);
              }
            }

        // ---- birches: bare, pale stands on the low ground where the firs are not, and a few alone ----
        for (float z = l.Z0; z < l.Z1; z += 5.2f)
            for (float x = l.X0; x < l.X1; x += 5.2f)
            {
                var p = new Vector2(x + R(-2.4f, 2.4f), z + R(-2.4f, 2.4f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 2f || h > 36f || l.Slope(p) > 0.45f || l.ToShore(p) < 14f || l.Beach(p)) continue;
                if ((p - WinterIslePuzzle.Peak).magnitude < 1500f && WinterIslePuzzle.SkiFace(p) > 0.3f) continue;      // the ski face is firs only
                float stand = Mathf.PerlinNoise(p.x * 0.012f + 57.3f, p.y * 0.012f + 140.8f) + (Mathf.PerlinNoise(p.x * 0.05f + 3.3f, p.y * 0.05f + 8.1f) - 0.5f) * 0.2f;
                float inStand = Smooth01((stand - 0.5f) / 0.1f) * (1f - Smooth01((Grove(p) - 0.41f) / 0.1f));
                if (R(0f, 1f) > inStand * 0.7f + 0.004f || !l.Free(p, 1.5f) || InsideRock(p, 0.9f)) continue;
                float tallness = R(5.5f, 10.5f) * Mathf.Lerp(0.8f, 1f, inStand);
                if (R(0f, 1f) < 0.15f) tallness = R(2.4f, 4.5f);
                Tree("birch", p, h, tallness, 0.5f, 0.9f);
                birches++;
            }

        // ---- woods: dense stands the trails run through, trunks a few metres apart right up to the path's edge ----
        float Wood(Vector2 q, bool birchOnly, bool firOnly)
        {
            float best = 0f;
            foreach (var w in l.Woods)
            {
                if (birchOnly && !w.birch || firOnly && w.birch) continue;
                best = Mathf.Max(best, 1f - Smooth01(((q - w.at).magnitude - w.radius * 0.6f) / (w.radius * 0.4f)));
            }
            return best;
        }
        int wooded = 0;
        foreach (var w in l.Woods)
            for (float z = w.at.y - w.radius; z < w.at.y + w.radius; z += 3.3f)
                for (float x = w.at.x - w.radius; x < w.at.x + w.radius; x += 3.3f)
                {
                    var p = new Vector2(x + R(-1.4f, 1.4f), z + R(-1.4f, 1.4f));
                    float h = l.Height(p);
                    float inWood = 1f - Smooth01(((p - w.at).magnitude - w.radius * 0.6f) / (w.radius * 0.4f));
                    if (float.IsNaN(h) || h < 2f || l.Slope(p) > 0.6f || R(0f, 1f) > inWood * 0.82f || !l.Free(p, -1.2f) || InsideRock(p, 0.7f)) continue;
                    float pick = R(0f, 1f);
                    if (w.birch)
                    {
                        if (pick < 0.12f) Tree("fir", p, h, R(1.4f, 4.5f), 1.1f, 0.5f);
                        else { Tree("birch", p, h, pick < 0.3f ? R(2.4f, 5f) : R(6f, 10.5f), 0.5f, 0.9f); birches++; }
                    }
                    else
                    {
                        float tallness = pick < 0.18f ? R(1.4f, 4.2f) : R(6f, 14f);
                        // the path through a wood is an aisle: a big tree stands back far enough that its boughs reach
                        // the path's edge but do not hang across it
                        Firs.Add((p, tallness, random.Next(1, 1000000)));
                        if (tallness > 5f && (l.ToPath(p) < tallness * 0.34f + 1.5f || pick > 0.9f))
                        {
                            Tree("pine", p, h, Mathf.Max(9f, tallness * 1.25f), 0.6f, tallness * 0.2f);      // canopy over the path, nothing at eye height
                            pines++; wooded++;
                            continue;
                        }
                        if (pick > 0.75f) { Tree("spruce", p, h, tallness * 1.25f, 0.9f, tallness * 0.14f); spruces++; }
                        else Tree("fir", p, h, tallness, 1.1f, tallness * 0.22f);
                    }
                    wooded++;
                }

        // ---- the lake's shore: reeds standing through the ice's edge and stones along it, so ice does not meet snow
        //      on a bare line; and on the beaches a few boulders set deep in the sand ----
        for (int i = 0; i < 150; i++)
        {
            float a = R(0f, Mathf.PI * 2f);
            Vector2 p = l.Lake + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (l.LakeRadius + R(1.5f, 9f));
            if (float.IsNaN(l.Height(p)) || l.ToPath(p) < 2f || InsideRock(p, 0.2f)) continue;
            if (i % 5 == 0) Rock(Pick(SmallRound), p, LowGround(p, 0.5f), R(0.6f, 2.4f), R(0.35f, 0.55f), 0.4f, 20f, true, false);
            else Plant(Pick(new[] { "SaltMarshRush", "SaltMarshRush", "ButtonGrass", "TeatreeCoast_Dead" }), p, R(0.45f, 0.9f), 0.3f);
        }
        for (float z = l.Z0; z < l.Z1; z += 8f)
            for (float x = l.X0; x < l.X1; x += 8f)
            {
                var p = new Vector2(x + R(-3.5f, 3.5f), z + R(-3.5f, 3.5f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 0.1f || h > 1.6f || !l.Beach(p) || R(0f, 1f) > 0.14f || InsideRock(p, 1f) || l.ToPath(p) < 2.5f) continue;
                Rock(Pick(BigRound), p, LowGround(p, 0.6f), R(1.2f, 4.5f), R(0.45f, 0.62f), 0.5f, 12f, true, false);
            }

        // ---- plants ----
        string[] heath = { "TeatreeCoast_Solo", "TeatreeCoast_Wall", "PovertyBush", "SeaberrySaltbush", "CoastSaltbush", "CorreaWhite_Bush", "CoastBeardHeath_Tall", "CoastalWattle_Bush", "CommonBoobialla", "TeatreeCoast_Bush" };
        string[] shoreTrees = { "TeatreeCoast_Dead_Tall", "TeatreeCoast_Dead", "BanksiaCoast_Dead", "TeatreeCoast_Dead_Tall" };      // (in leaf they read as summer)
        // winter woodland: mostly bare, dead-looking broadleaf (evergreen gum and banksia read as summer from afar,
        // where only their impostors draw), a few hardy evergreens, and thickets
        string[] woodland = { "BanksiaCoast_Dead", "TeatreeCoast_Dead", "BrownStringybark_Dead_Skinny", "Messmate_Dead_Short", "AustralianBlackthorn_Bush", "BrownStringybark_Dead_Short" };
        string[] tall = { "Messmate_Dead", "Messmate_Dead_Short", "BrownStringybark_Dead", "BrownStringybark_Dead_Skinny", "LillyPilly_ForestTall_Dead", "Messmate_Dead_ClosedForest", "TeatreeCoast_Dead_Tall", "BrownStringybark_Dead_Short" };
        string[] under = { "Paperbark_Bush", "BushyNeedlewood", "TreeEverlasting", "PricklyMoses", "BrownStringybark_Bush", "AustralianMulberry", "TreeBroomHeath", "HazelPomaderris_Thicket", "Paperbark_Bush", "BushyNeedlewood", "PricklyMoses" };
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
                else if (h > 34f && l.Forest(p) < 0.4f && Wood(p, false, false) < 0.3f)
                {
                    if (roll < 0.06f + 0.10f * clump) Plant(Pick(tops), p, R(0.7f, 1.05f));
                    else if (roll < 0.075f + 0.10f * clump) Plant(Pick(dead), p, R(0.5f, 0.85f), 0.25f);
                }
                else
                {
                    float wood = Mathf.Clamp01((thicket - 0.38f) * 3f);
                    // under the firs and among the birches there is an understorey: snow-laden bushes in clumps,
                    // fallen logs and stumps, the odd dead snag (a grove with bare snow between its trunks reads as
                    // planted, not grown)
                    float grove = Mathf.Max(Mathf.Max(Smooth01((Grove(p) - 0.44f) / 0.14f), Wood(p, false, false)) * (h < 50f ? 1f : 0f), Mathf.Max(Wood(p, false, false), l.Forest(p) * 0.7f));
                    float a = 0.07f * wood + 0.02f * grove, b = a + 0.16f * wood * clump + 0.03f + 0.42f * grove * clump, c = b + 0.02f + 0.05f * (1f - wood) * clump;
                    if (roll < 0.016f * wood + 0.004f && s < 0.4f && !InsideRock(p, 4f)) Plant(Pick(tall), p, R(0.55f, 0.95f), 0.32f);
                    else if (roll < a) Plant(Pick(woodland), p, R(0.6f, 1.0f));
                    else if (roll < b) Plant(Pick(under), p, R(0.6f, 1.1f));
                    else if (roll < c) Plant(Pick(tops), p, R(0.7f, 1.0f));
                    else if (roll < c + 0.02f + 0.04f * grove) Plant(Pick(dead), p, R(0.5f, 0.9f), 0.25f);
                    else if (roll < c + 0.04f + 0.11f * grove && s < 0.45f) Plant(Pick(logs), p, R(0.8f, 1.1f), 0.35f);
                }
            }

        // ---- scatter: what pokes through the snow underfoot (tufts, twigs, stone tips, sticks, low bushes), thickest
        //      beside the paths where people walk and look down ----
        int scattered = 0;
        string[] tufts = { "ButtonGrass", "ButtonGrass", "SaltMarshRush", "GrassTree_Short_NoFlower", "TeatreeCoast_Dead", "TeatreeCoast_Dead" };
        string[] sticks = { "Litter_Log_Dry_Small", "Litter_Log_Wet_Small", "Litter_Stump_Wet" };
        string[] low = { "PovertyBush", "CoastSaltbush", "TeatreeCoast_Bush", "BushyNeedlewood", "SaltMarshRush", "Paperbark_Bush", "TreeBroomHeath", "PricklyMoses", "CorreaWhite_Bush", "TreeEverlasting" };
        for (float z = l.Z0; z < l.Z1; z += 2.5f)
            for (float x = l.X0; x < l.X1; x += 2.5f)
            {
                var p = new Vector2(x + R(-1.2f, 1.2f), z + R(-1.2f, 1.2f));
                float h = l.Height(p);
                if (float.IsNaN(h) || h < 0.8f || l.Slope(p) > 0.7f || l.Beach(p)) continue;
                float toPath = l.ToPath(p);
                if (toPath < 1.7f || InsideRock(p, 0.3f) || !l.Free(p, -1.9f)) continue;
                float near = 1f - Smooth01((toPath - 4f) / 30f);
                float patch = Mathf.PerlinNoise(p.x * 0.05f + 91.3f, p.y * 0.05f + 27.7f);
                if (R(0f, 1f) > (0.2f + 0.55f * near) * (0.4f + patch * 1.2f)) continue;
                // by the trails it is mostly low bushes and ferns under snow, in clumps; further off, tufts and stones
                float pick = R(0f, 1f), bushy = 0.32f + 0.25f * near;
                if (pick < bushy && toPath > 3.4f) Plant(Pick(low), p, R(0.5f, 1.05f), 0.55f);      // (a bush nearer than that stands in the walker's face)
                else if (pick < bushy + 0.3f) Plant(Pick(tufts), p, R(0.28f, 0.5f), 0.5f);
                else if (pick < bushy + 0.5f) Rock(Pick(SmallRound), p, LowGround(p, 0.3f), R(0.35f, 1.1f), R(0.45f, 0.65f), 0.35f, 25f, true, false);
                else Plant(Pick(sticks), p, R(0.35f, 0.7f), 0.35f);
                scattered++;
            }

        File.WriteAllText(outputFile, output.ToString());
        Debug.Log("[BWM] Winter Isle dressing: " + Firs.Count + " firs (" + spruces + " of them spruces, " + pines + " pines), " + birches + " birches, " + wooded + " of the trees in " + l.Woods.Count + " woods, " + rocks + " rocks (" + slabs + " slabs, " + trailside + " by the trails, " + massif + " massif, " + tors + " tors, " + bands + " rock bands, " + outcrops + " outcrops, " + coast + " on the coast, " + loose + " loose), "
            + plants + " plants (" + scattered + " of them scatter); written to " + outputFile);
    }
}
