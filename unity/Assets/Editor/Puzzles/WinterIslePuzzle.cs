// Winter Isle: the land the winter edition is built on. Not a puzzle (type None).
//
// A snow island in the open sea south-west of the opening area, generated to fill the water between the
// mainland shore and well past the south-west corner of the game's terrain square, with a causeway to the
// shore. The coastline comes from the sea itself (it keeps a channel's width from the mainland) and wanders in
// coves and headlands.
//
// The land:
//   - a mountain range in the west, crested with crags and cut by cliff bands, with a spur running north-east
//     towards the plaza and two lower headlands in the south
//   - rolling lowland in the east and middle, scattered with rounded granite knolls, where most puzzles go
//   - a gorge winding down to the north shore, too deep to climb and too wide to jump
//   - a creek running from the high valley to a frozen lake and on to a beach on the south shore, iced over
//   - sandy coves between rocky stretches of coast
// The plaza and every puzzle site are levelled, whatever the land round them does. Paths are routed from the
// plaza to the landing and to every puzzle by the easiest gradient, so they wind round the steep ground.
//
// The set dressing (the game's own granite, trees, shrubs and logs, with snow) is placed by WinterIsleDressing
// and made in the game by the Winter Walk plugin. The build also registers the island's heights as land (so
// other puzzles can be sited on it) and marks the ground no puzzle may take: paths, the plaza, the lake, rocks.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WinterIslePuzzle : PuzzleDef
{
    public override string Name => "WinterIsle";
    public override int PuzzleType => 2;

    // The box of sea the island may fill (world metres). It runs past the terrain square's edge at -1024, which
    // the game allows: land there holds players like any other.
    const float X0 = -1992f, X1 = -360f, Z0 = -1581f, Z1 = -501f, Cell = 3f;
    // The island's outline: two overlapping ovals (west and east), roughened by coves; the east one reaches the
    // causeway and is cut back where it would come within a channel's width of the mainland
    static readonly (Vector2 centre, Vector2 radius)[] Ovals = { (new Vector2(-1230f, -1060f), new Vector2(620f, 420f)), (new Vector2(-740f, -870f), new Vector2(295f, 320f)) };
    const float Channel = 50f;          // open water kept between the island and the mainland
    const float Skirt = 24f;            // how far the submerged rim runs out from the coast
    static readonly Vector2 ShorePoint = new Vector2(-468f, -700f);     // where the causeway meets the mainland

    public static readonly Vector2 Plaza = new Vector2(-705f, -795f);   // the open middle, where The Tree stands
    public const float PlazaRadius = 24f;

    // Ridges: lines of (x, crest height, z), and how far their slopes reach
    static readonly (Vector3[] line, float width)[] Ridges =
    {
        (new[] { new Vector3(-1230f, 38f, -700f), new Vector3(-1120f, 80f, -760f), new Vector3(-1020f, 92f, -820f), new Vector3(-945f, 64f, -880f), new Vector3(-905f, 34f, -955f) }, 115f),
        (new[] { new Vector3(-1020f, 70f, -820f), new Vector3(-905f, 58f, -765f), new Vector3(-830f, 30f, -690f) }, 85f),
        (new[] { new Vector3(-1215f, 20f, -1150f), new Vector3(-1160f, 42f, -1110f), new Vector3(-1090f, 30f, -1080f) }, 80f),
        (new[] { new Vector3(-700f, 14f, -990f), new Vector3(-640f, 28f, -965f), new Vector3(-585f, 18f, -985f) }, 70f),
        (new[] { new Vector3(-1760f, 26f, -950f), new Vector3(-1630f, 66f, -1005f), new Vector3(-1510f, 84f, -1075f), new Vector3(-1400f, 52f, -1150f) }, 120f),
        (new[] { new Vector3(-1460f, 22f, -1330f), new Vector3(-1310f, 44f, -1380f), new Vector3(-1160f, 30f, -1350f) }, 90f),
    };
    public static readonly Vector2 Lake = new Vector2(-822f, -1012f);
    public const float LakeRadius = 32f, LakeLevel = 1f;
    static readonly Vector2[] Gorge = { new Vector2(-868f, -712f), new Vector2(-815f, -668f), new Vector2(-760f, -660f), new Vector2(-718f, -632f), new Vector2(-690f, -596f) };
    const float GorgeDepth = 9f;
    // The creek's bed: (x, floor height, z), falling all the way from the high valley to the sea
    static readonly Vector3[] Creek =
    {
        new Vector3(-1010f, 30f, -905f), new Vector3(-962f, 20f, -938f), new Vector3(-915f, 10f, -972f), new Vector3(-868f, 3f, -998f),
        new Vector3(-822f, 0.75f, -1012f), new Vector3(-790f, 0.6f, -1052f), new Vector3(-772f, 0.3f, -1100f), new Vector3(-765f, -1.5f, -1160f),
    };

    static readonly List<(Vector2 at, float radius, float height)> knolls = new List<(Vector2, float, float)>();

    public override Vector2 Footprint => new Vector2(X1 - X0, Z1 - Z0);
    // The pack's root stays where it was first placed (it must be over the game's terrain square, which the island
    // now runs well past); everything is built relative to it
    public override Vector2? FixedSite => new Vector2(-869f, -909.5f);

    static readonly Color PackedSnow = new Color(0.8f, 0.85f, 0.93f);

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Level steps with a riser between: 'riser' is the share of each step that is slope (0.3 = cliff bands)
    static float Terrace(float raw, float step, float riser)
    {
        float t = raw / step, f = Mathf.Floor(t);
        return (f + Smooth((t - f - (1f - riser)) / riser)) * step;
    }

    static float Fbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq + o * 17.3f, z * freq + o * 31.7f);
            norm += amp; amp *= 0.5f; freq *= 2.03f;
        }
        return sum / norm;
    }

    static float Ridged(float x, float z) { float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x, z) * 2f - 1f); return n * n; }

    // Distance from p to a line of points (x, y, z) on the ground plane, and the line's y there
    static float ToLine(Vector2 p, Vector3[] line, out float y)
    {
        float best = float.MaxValue; y = line[0].y;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = new Vector2(line[i].x, line[i].z), b = new Vector2(line[i + 1].x, line[i + 1].z), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            float d = (p - (a + ab * t)).magnitude;
            if (d < best) { best = d; y = Mathf.Lerp(line[i].y, line[i + 1].y, t); }
        }
        return best;
    }

    static float ToLine(Vector2 p, Vector2[] line)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 ab = line[i + 1] - line[i];
            float t = Mathf.Clamp01(Vector2.Dot(p - line[i], ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            best = Mathf.Min(best, (p - (line[i] + ab * t)).magnitude);
        }
        return best;
    }

    // How much the high ground rises at p (before the crags), for the land and for where beaches may be
    static float RidgeHeight(Vector2 p)
    {
        float high = 0f;
        foreach (var ridge in Ridges)
        {
            float d = ToLine(p, ridge.line, out float crest);
            float f = Mathf.Pow(1f - Smooth(d / ridge.width), 1.35f);
            float c = crest * f;
            high = Mathf.Max(high, c) + 0.15f * Mathf.Min(high, c);
        }
        return high;
    }

    // How much of a sandy cove a point of coast is: where a slow noise says so, away from the high ground
    static float BeachAmount(Vector2 p)
    {
        float b = Smooth((Mathf.PerlinNoise(p.x * 0.0075f + 61.1f, p.y * 0.0075f + 3.7f) - 0.52f) / 0.1f);
        return b * (1f - Smooth(RidgeHeight(p) / 10f));
    }

    // Height above sea level at a point 'inside' metres in from the coastline, before any levelling
    static float RawHeight(float x, float z, float inside)
    {
        // the submerged rim runs out further in some places than others, so the shallows round the coast vary
        float skirt = Skirt * (0.45f + 1.1f * Mathf.PerlinNoise(x * 0.008f + 17.7f, z * 0.008f + 4.4f));
        if (inside <= -skirt) return float.NaN;
        var p = new Vector2(x, z);
        float beach = BeachAmount(p);
        if (inside <= 0f)
        {
            float t = Smooth((inside + skirt) / skirt);
            return Mathf.Lerp(Mathf.Lerp(-14f, 0.4f, t), Mathf.Lerp(-7f, 0.25f, t * t), beach);      // beaches shelve gently; the rim is deep enough that its edge never shows
        }

        // rolling lowland, rising from the shore (slowly behind a beach)
        float rise = Smooth(inside / Mathf.Lerp(26f, 75f, beach));
        float lowland = 0.9f + (2f + 10f * Fbm(x * 0.0045f + 40.2f, z * 0.0045f + 17.9f, 3) + 3f * Mathf.PerlinNoise(x * 0.018f + 9.1f, z * 0.018f + 61.4f)) * rise;
        // ...broken up so that no view across it stays open for long: sharp-crested ridgelets (like moraines),
        // hummocks, dells, and a fine unevenness underfoot
        float ridgelets = (Ridged(x * 0.0105f + 3.3f, z * 0.0105f + 7.7f) - 0.35f) * 8f;
        float hummocks = (Fbm(x * 0.042f + 21.7f, z * 0.042f + 5.3f, 2) - 0.5f) * 4f;
        float dells = Smooth((Mathf.PerlinNoise(x * 0.019f + 77.1f, z * 0.019f + 12.9f) - 0.6f) / 0.12f) * 3.5f;
        float fine = (Mathf.PerlinNoise(x * 0.09f + 3.9f, z * 0.09f + 44.2f) - 0.5f) * 1.1f + (Mathf.PerlinNoise(x * 0.21f + 8.1f, z * 0.21f + 1.7f) - 0.5f) * 0.45f;
        lowland += (Mathf.Max(ridgelets, -1.5f) + hummocks - dells) * Smooth(inside / 45f) + fine * Smooth(inside / 8f);
        float beachY = 0.35f + inside * 0.035f + fine * 0.3f;
        float y = Mathf.Lerp(lowland, Mathf.Min(lowland, beachY), beach * (1f - Smooth((inside - 30f) / 40f)));

        // the mountains: ridges, crested with crags, cut into cliff bands; straight from the sea where they meet it
        float high = RidgeHeight(p);
        float mountain = Mathf.Clamp01(high / 30f);
        high += (Ridged(x * 0.022f + 5.1f, z * 0.022f + 8.3f) - 0.45f) * 14f * mountain + (Ridged(x * 0.06f + 1.7f, z * 0.06f + 3.9f) - 0.5f) * 4f * mountain;
        high = Mathf.Max(0f, high) * Smooth(inside / 16f);
        y += Mathf.Lerp(high, Terrace(high, 8f, 0.32f), 0.25f);

        // granite knolls: rounded domes
        foreach (var k in knolls)
        {
            float d = (p - k.at).magnitude;
            if (d >= k.radius) continue;
            float f = 1f - (d / k.radius) * (d / k.radius);
            y += k.height * Mathf.Pow(f, 0.6f) * Smooth(inside / 10f);
        }

        // the gorge: a narrow floor, walls too steep to climb
        float toGorge = ToLine(p, Gorge);
        if (toGorge < 15f) y = Mathf.Max(0.6f, y - GorgeDepth * (1f - Smooth((toGorge - 3.5f) / 10f)));

        // the creek's bed, and its banks
        float toCreek = ToLine(p, Creek, out float bed);
        if (toCreek < 17f && y > bed) y = Mathf.Lerp(bed, y, Smooth((toCreek - 3.5f) / 12f));

        // the lake's hollow
        float toLake = (p - Lake).magnitude;
        if (toLake < LakeRadius + 18f) y = Mathf.Lerp(LakeLevel, y, Smooth((toLake - LakeRadius) / 18f));
        return y;
    }

    // Falling snow: one particle emitter that a small script keeps over each player's camera while they are on or
    // near the island. Flakes drift on a light wind with a wandering flutter, and live in world space so that
    // walking does not drag them along.
    void Snowfall(PuzzleBuilder b)
    {
        Piece fall = b.Empty("Snowfall", 0f, 0f, 0f, false);
        ParticleSystem ps = fall.Go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
        main.startColor = new Color(1f, 1f, 1f, 0.92f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 9000;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 1100f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(46f, 1f, 46f);

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);       // the wind
        velocity.y = new ParticleSystem.MinMaxCurve(-2.6f, -1.5f);     // falling
        velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.5f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.55f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.25f;
        noise.octaveCount = 2;

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystemRenderer renderer = fall.Go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = b.ShaderMaterial("BWM/Snowflake", "Snowflake");
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Component follow = b.AddCustomScript(fall, "SnowfallFollow");
        Sdk.Set(follow, "areaCentre", new Vector3((X0 + X1) / 2f, 0f, (Z0 + Z1) / 2f));
        Sdk.Set(follow, "areaSize", new Vector3(X1 - X0, 0f, Z1 - Z0));
    }

    public override void Build(PuzzleBuilder b)
    {
        Vector3 root = b.Root.transform.position;      // on the sea floor, at the box's centre
        int nx = Mathf.RoundToInt((X1 - X0) / Cell), nz = Mathf.RoundToInt((Z1 - Z0) / Cell);
        int row = nx + 1, count = row * (nz + 1);
        Vector2 At(int k) => new Vector2(X0 + (k % row) * Cell, Z0 + (k / row) * Cell);
        int Cell_(Vector2 p) => Mathf.Clamp(Mathf.RoundToInt((p.y - Z0) / Cell), 0, nz) * row + Mathf.Clamp(Mathf.RoundToInt((p.x - X0) / Cell), 0, nx);
        const float diag = 1.41421f;

        // Fills a grid with the distance (in cells) to the nearest cell that starts at 0: two chamfer passes
        void Spread(float[] d, int w, int h)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float v = d[j * w + i];
                    if (i > 0) v = Mathf.Min(v, d[j * w + i - 1] + 1f);
                    if (j > 0) v = Mathf.Min(v, d[(j - 1) * w + i] + 1f);
                    if (i > 0 && j > 0) v = Mathf.Min(v, d[(j - 1) * w + i - 1] + diag);
                    if (i < w - 1 && j > 0) v = Mathf.Min(v, d[(j - 1) * w + i + 1] + diag);
                    d[j * w + i] = v;
                }
            for (int j = h - 1; j >= 0; j--)
                for (int i = w - 1; i >= 0; i--)
                {
                    float v = d[j * w + i];
                    if (i < w - 1) v = Mathf.Min(v, d[j * w + i + 1] + 1f);
                    if (j < h - 1) v = Mathf.Min(v, d[(j + 1) * w + i] + 1f);
                    if (i < w - 1 && j < h - 1) v = Mathf.Min(v, d[(j + 1) * w + i + 1] + diag);
                    if (i > 0 && j < h - 1) v = Mathf.Min(v, d[(j + 1) * w + i - 1] + diag);
                    d[j * w + i] = v;
                }
        }

        // 0. Granite knolls, kept off the plaza and the puzzle sites
        knolls.Clear();
        var knollRandom = new System.Random(1717);
        for (int tries = 0; tries < 1600 && knolls.Count < 170; tries++)
        {
            var at = new Vector2(X0 + (float)knollRandom.NextDouble() * (X1 - X0), Z0 + (float)knollRandom.NextDouble() * (Z1 - Z0));
            float radius = 14f + (float)knollRandom.NextDouble() * 22f, tall = 4f + (float)knollRandom.NextDouble() * 9f;
            if (tries % 2 == 1) { radius *= 0.6f; tall *= 0.7f; }      // half of them smaller: humps and hillocks
            bool clear = (at - Plaza).magnitude > PlazaRadius + radius + 25f && (at - Lake).magnitude > LakeRadius + radius + 10f;
            foreach (Vector3 site in b.OtherSites)
                if ((at - new Vector2(site.x, site.y)).magnitude < site.z + radius + 15f) clear = false;
            if (clear) knolls.Add((at, radius, tall));
        }

        // 1. Distance from every grid point to the mainland (terrain above about -1 m), over a padded grid
        const int pad = 24;
        int pw = row + 2 * pad, ph = nz + 1 + 2 * pad;
        var toLand = new float[pw * ph];
        for (int j = 0; j < ph; j++)
            for (int i = 0; i < pw; i++)
                toLand[j * pw + i] = b.Ground.At(X0 + (i - pad) * Cell, Z0 + (j - pad) * Cell, out float y) && y > -1f ? 0f : 1e6f;
        Spread(toLand, pw, ph);

        // 2. Heights above sea level, before levelling; and how much of each point is beach
        var height = new float[count];
        var beach = new float[count];
        for (int k = 0; k < count; k++)
        {
            Vector2 p = At(k);
            float toMainland = toLand[(k / row + pad) * pw + k % row + pad] * Cell - Channel;
            float toEdge = Mathf.Min(Mathf.Min(p.x - X0, X1 - p.x), Mathf.Min(p.y - Z0, Z1 - p.y)) - 10f;
            float shape = float.MinValue;
            foreach (var oval in Ovals)
            {
                Vector2 d = new Vector2((p.x - oval.centre.x) / oval.radius.x, (p.y - oval.centre.y) / oval.radius.y);
                float wobble = 1f + 0.08f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 3f + oval.centre.x) + 0.05f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 5f + oval.centre.y);
                shape = Mathf.Max(shape, (wobble - d.magnitude) * Mathf.Min(oval.radius.x, oval.radius.y));
            }
            toEdge = Mathf.Min(toEdge, shape);
            float coves = (Mathf.PerlinNoise(p.x * 0.0032f + 13.1f, p.y * 0.0032f + 2.7f) - 0.5f) * 150f
                + (Mathf.PerlinNoise(p.x * 0.006f + 3.3f, p.y * 0.006f + 8.8f) - 0.5f) * 100f + (Mathf.PerlinNoise(p.x * 0.02f + 1.3f, p.y * 0.02f + 4.1f) - 0.5f) * 34f
                + (Mathf.PerlinNoise(p.x * 0.05f + 6.6f, p.y * 0.05f + 2.4f) - 0.5f) * 14f * (1f - BeachAmount(p));      // rocky shores ragged, beaches smooth
            // the coves can push the coast outwards, but never off the grid (the land would end there in a straight
            // cut): the coast stays about 45 m inside it, wavering, with room for the submerged rim
            float toGrid = Mathf.Min(Mathf.Min(p.x - X0, X1 - p.x), Mathf.Min(p.y - Z0, Z1 - p.y));
            float rim = toGrid - 45f + (Mathf.PerlinNoise(p.x * 0.013f + 5.1f, p.y * 0.013f + 9.4f) - 0.5f) * 30f;
            float inside = Mathf.Min(Mathf.Min(toMainland, toEdge) + coves, rim);
            height[k] = RawHeight(p.x, p.y, inside);
            beach[k] = BeachAmount(p);
        }

        // 3. Level the plaza and every puzzle site on the island, each at the height the land has at its centre
        var sites = new List<Vector2>();
        var flats = new List<(Vector2 at, float radius, float blend)> { (Plaza, PlazaRadius, 16f) };
        foreach (Vector3 site in b.OtherSites)
        {
            var at = new Vector2(site.x, site.y);
            if (at.x < X0 || at.x > X1 || at.y < Z0 || at.y > Z1 || !(height[Cell_(at)] > 0.2f)) continue;      // not on the island
            sites.Add(at);
            flats.Add((at, site.z + 2f, 10f));
        }
        var level = new float[flats.Count];
        for (int f = 0; f < flats.Count; f++) level[f] = Mathf.Round(height[Cell_(flats[f].at)] * 2f) / 2f;
        for (int k = 0; k < count; k++)
        {
            if (!(height[k] > 0f)) continue;
            Vector2 p = At(k);
            for (int f = 0; f < flats.Count; f++)
            {
                float d = (p - flats[f].at).magnitude;
                if (d < flats[f].radius + flats[f].blend) height[k] = Mathf.Lerp(level[f], height[k], Smooth((d - flats[f].radius) / flats[f].blend));
            }
        }
        float Slope(int k)      // steepest rise to a neighbour, as a gradient (1 = 45 degrees)
        {
            int i = k % row, j = k / row;
            float s = 0f, y = height[k];
            if (i > 0 && !float.IsNaN(height[k - 1])) s = Mathf.Max(s, Mathf.Abs(height[k - 1] - y));
            if (i < nx && !float.IsNaN(height[k + 1])) s = Mathf.Max(s, Mathf.Abs(height[k + 1] - y));
            if (j > 0 && !float.IsNaN(height[k - row])) s = Mathf.Max(s, Mathf.Abs(height[k - row] - y));
            if (j < nz && !float.IsNaN(height[k + row])) s = Mathf.Max(s, Mathf.Abs(height[k + row] - y));
            return s / Cell;
        }

        // 3a. Crags: where the ground is steep it is rock, so break it into facets and ledges instead of the smooth
        //     sweep the grid gives (the plaza, the puzzle sites and gentle ground are left alone)
        {
            var craggy = new float[count];
            for (int k = 0; k < count; k++) craggy[k] = float.IsNaN(height[k]) ? 0f : Smooth((Slope(k) - 0.75f) / 0.5f);
            for (int k = 0; k < count; k++)
            {
                if (craggy[k] <= 0f || height[k] < 1f) continue;
                Vector2 p = At(k);
                float facets = (Ridged(p.x * 0.11f + 4.4f, p.y * 0.11f + 9.1f) - 0.4f) * 2.6f + (Mathf.PerlinNoise(p.x * 0.31f + 2.2f, p.y * 0.31f + 6.6f) - 0.5f) * 1.4f;
                height[k] += facets * craggy[k];
            }
        }

        // 4. Where the causeway lands; then paths from the plaza to the landing and to every site, by the easiest
        //    gradient (Dijkstra over the grid: steep steps cost a great deal, very steep ones are not allowed)
        int landingCell = 0; float best = float.MaxValue;
        for (int k = 0; k < count; k++)
        {
            if (!(height[k] >= 0.45f)) continue;
            float d = (At(k) - ShorePoint).sqrMagnitude;
            if (d < best) { best = d; landingCell = k; }
        }
        Vector2 landing = At(landingCell);
        var cost = new float[count];
        var from = new int[count];
        int start = Cell_(Plaza);
        int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
        // The easiest way from any of 'sources' to every cell, or until 'goal' is reached: steep ground costs more,
        // too steep is impassable, and a slow noise makes routes meander rather than run in straight lines
        void Search(List<int> sources, int goal)
        {
            for (int k = 0; k < count; k++) { cost[k] = float.MaxValue; from[k] = -1; }
            var open = new SortedSet<(float cost, int cell)>();
            foreach (int s in sources) { cost[s] = 0f; open.Add((0f, s)); }
            while (open.Count > 0)
            {
                var current = open.Min;
                open.Remove(current);
                int k = current.cell, i = k % row, j = k / row;
                if (k == goal) return;
                if (current.cost > cost[k]) continue;
                for (int n = 0; n < 8; n++)
                {
                    int ni = i + di[n], nj = j + dj[n];
                    if (ni < 0 || nj < 0 || ni > nx || nj > nz) continue;
                    int nk = nj * row + ni;
                    if (!(height[nk] > 0.4f)) continue;
                    float run = Cell * (n < 4 ? 1f : diag), grade = Mathf.Abs(height[nk] - height[k]) / run;
                    if (grade > 0.5f) continue;
                    float lakeside = (At(nk) - Lake).magnitude < LakeRadius ? 4f : 1f;      // go round the lake, not over it
                    Vector2 q = At(nk);
                    float wander = 0.6f + 0.8f * Mathf.PerlinNoise(q.x * 0.026f + 7.1f, q.y * 0.026f + 3.3f);
                    float c = cost[k] + run * (1f + 40f * grade * grade) * lakeside * wander;
                    if (c >= cost[nk]) continue;
                    open.Remove((cost[nk], nk));
                    cost[nk] = c; from[nk] = k;
                    open.Add((c, nk));
                }
            }
        }
        Search(new List<int> { start }, -1);
        var reachable = new bool[count];      // walkable from the plaza at all
        for (int k = 0; k < count; k++) reachable[k] = from[k] >= 0 || k == start;
        var toPath = new float[count];
        for (int k = 0; k < count; k++) toPath[k] = 1e6f;
        int routed = 0;
        var routes = new List<(List<Vector2> points, bool trail)>();
        var targets = new List<int> { landingCell };
        foreach (Vector2 site in sites) targets.Add(Cell_(site));
        // Trails for exploring: to the highest ground walkable in each part of the island, the lake, and the beaches
        int trails = 0;
        int Highest(System.Func<Vector2, bool> where)
        {
            int bestCell = -1; float bestHeight = 0f;
            for (int k = 0; k < count; k++)
                if (reachable[k] && height[k] > bestHeight && where(At(k))) { bestHeight = height[k]; bestCell = k; }
            return bestCell;
        }
        var trailEnds = new List<int>
        {
            Highest(p => true),                                       // the summit (as near as walking gets)
            Highest(p => p.x < -1400f),                               // the western range
            Highest(p => p.x > -1400f && p.x < -1000f && p.y < -1250f),   // the southern ridge
            Highest(p => p.x > -800f && p.y < -900f),                 // the southern headland
        };
        int lakeShore = Cell_(Lake + (Plaza - Lake).normalized * (LakeRadius + 10f));
        if (reachable[lakeShore]) trailEnds.Add(lakeShore);
        var beachEnds = new List<Vector2>();
        for (int k = 0; k < count; k += 7)
        {
            if (!reachable[k] || !(beach[k] > 0.6f) || !(height[k] > 0.8f && height[k] < 2.5f)) continue;
            Vector2 p = At(k);
            bool far = true;
            foreach (Vector2 e in beachEnds) if ((e - p).magnitude < 260f) far = false;
            if (far) { beachEnds.Add(p); trailEnds.Add(k); }
        }
        foreach (int end in trailEnds) if (end >= 0 && !targets.Contains(end)) { targets.Add(end); trails++; }
        // Each route is found from the whole network laid so far, so it branches off the nearest path instead of
        // running all the way back to the plaza beside the others (which made the paths spokes of a wheel)
        var network = new List<int> { start };
        var onNetwork = new bool[count];
        onNetwork[start] = true;
        foreach (int target in targets)
        {
            if (!reachable[target]) { Debug.LogWarning("[BWM] Winter Isle: no walkable route from the plaza to " + At(target)); continue; }
            if (onNetwork[target]) continue;
            Search(network, target);
            // the route as points, smoothed into curves (Chaikin), then drawn into the grid
            var route = new List<Vector2>();
            bool isTrail = !(target == landingCell || sites.Exists(s => Cell_(s) == target));
            for (int k = target; k >= 0; k = from[k])
            {
                route.Add(At(k));
                if (!onNetwork[k]) { onNetwork[k] = true; network.Add(k); }
            }
            for (int pass = 0; pass < 4 && route.Count > 2; pass++)
            {
                var smooth = new List<Vector2> { route[0] };
                for (int i = 0; i + 1 < route.Count; i++)
                {
                    smooth.Add(Vector2.Lerp(route[i], route[i + 1], 0.25f));
                    smooth.Add(Vector2.Lerp(route[i], route[i + 1], 0.75f));
                }
                smooth.Add(route[route.Count - 1]);
                route = smooth;
            }
            for (int i = 0; i + 1 < route.Count; i++)
            {
                float length = (route[i + 1] - route[i]).magnitude;
                for (float s = 0f; s <= length; s += 1f) toPath[Cell_(Vector2.Lerp(route[i], route[i + 1], s / Mathf.Max(length, 0.001f)))] = 0f;
            }
            routed++;
            routes.Add((route, isTrail));
        }
        Spread(toPath, row, nz + 1);      // now in cells; times Cell for metres
        float PathDistance(Vector2 p) => toPath[Cell_(p)] * Cell;
        var structures = new List<(Vector2 at, float radius)>();      // cabins and the like: the dressing keeps off them
        bool Free(Vector2 p, float margin)      // clear of paths, the plaza, the lake, structures and every puzzle site
        {
            if (PathDistance(p) < 3.5f + margin || (p - Plaza).magnitude < PlazaRadius + margin || (p - Lake).magnitude < LakeRadius + 6f + margin) return false;
            foreach (var s in structures) if ((p - s.at).magnitude < s.radius + margin) return false;
            foreach (Vector3 site in b.OtherSites)
                if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 3f + margin) return false;
            return true;
        }

        // 5. Ground no puzzle may take: paths, the plaza's edge, the lake; the dressing adds its rocks
        var blocked = new bool[count];
        var trodden = new float[count];
        for (int k = 0; k < count; k++)
        {
            if (float.IsNaN(height[k])) continue;
            Vector2 p = At(k);
            float pathDistance = Mathf.Min(PathDistance(p), Mathf.Max(0f, (p - Plaza).magnitude - PlazaRadius + 3f));
            trodden[k] = 1f - Smooth((pathDistance - 0.8f + (Mathf.PerlinNoise(p.x * 0.15f, p.y * 0.15f) - 0.5f) * 1.2f) / 2.4f);      // a narrow, ragged-edged track
            blocked[k] = pathDistance < 7f || (p - Lake).magnitude < LakeRadius + 8f;
        }
        void Block(Vector2 p, float radius)
        {
            int r = Mathf.CeilToInt(radius / Cell), centre = Cell_(p);
            for (int oj = -r; oj <= r; oj++)
                for (int oi = -r; oi <= r; oi++)
                {
                    int i = centre % row + oi, j = centre / row + oj;
                    if (i >= 0 && j >= 0 && i <= nx && j <= nz && oi * oi + oj * oj <= r * r) blocked[j * row + i] = true;
                }
        }

        // 6. The set dressing: the game's own rocks and plants, placed here and made by the Winter Walk plugin
        var toWater = new float[count];
        var toLandCells = new float[count];
        for (int k = 0; k < count; k++)
        {
            bool wet = !(height[k] > 0f);
            toWater[k] = wet ? 0f : 1e6f;
            toLandCells[k] = wet ? 1e6f : 0f;
        }
        Spread(toWater, row, nz + 1);
        Spread(toLandCells, row, nz + 1);
        float HeightAt(Vector2 p)      // bilinear over the grid
        {
            float fx = Mathf.Clamp((p.x - X0) / Cell, 0f, nx - 0.001f), fz = Mathf.Clamp((p.y - Z0) / Cell, 0f, nz - 0.001f);
            int i = (int)fx, j = (int)fz;
            float tx = fx - i, tz = fz - j;
            float a = height[j * row + i], b2 = height[j * row + i + 1], c = height[(j + 1) * row + i], d = height[(j + 1) * row + i + 1];
            if (float.IsNaN(a) || float.IsNaN(b2) || float.IsNaN(c) || float.IsNaN(d)) return height[Cell_(p)];
            return Mathf.Lerp(Mathf.Lerp(a, b2, tx), Mathf.Lerp(c, d, tx), tz);
        }
        float ToShore(Vector2 p) { int k = Cell_(p); return toWater[k] > 0f ? toWater[k] * Cell : -toLandCells[k] * Cell; }
        // 6a. A hamlet of cabins near the plaza, on gentle ground off the paths, facing the nearest path
        var cabins = new List<(Vector2 at, float yaw, Vector2 size)>();
        var cabinRandom = new System.Random(1903);
        for (int tries = 0; tries < 4000 && cabins.Count < 9; tries++)
        {
            float a = (float)cabinRandom.NextDouble() * Mathf.PI * 2f, d = 45f + (float)cabinRandom.NextDouble() * 75f;
            Vector2 p = Plaza + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            int k = Cell_(p);
            if (!(height[k] > 1.5f) || Slope(k) > 0.14f || PathDistance(p) > 16f || !Free(p, -6f)) continue;
            bool apart = true;
            foreach (var c in cabins) if ((c.at - p).magnitude < 17f) apart = false;
            if (!apart) continue;
            // face the path: the downhill direction of the path distance
            Vector2 toward = new Vector2(PathDistance(p + Vector2.left * 3f) - PathDistance(p + Vector2.right * 3f), PathDistance(p + Vector2.down * 3f) - PathDistance(p + Vector2.up * 3f));
            float facing = toward.sqrMagnitude > 0.01f ? Mathf.Atan2(toward.x, toward.y) * Mathf.Rad2Deg : 0f;
            var size = new Vector2(5.5f + (float)cabinRandom.NextDouble() * 2.5f, 6.5f + (float)cabinRandom.NextDouble() * 3f);
            cabins.Add((p, facing, size));
            structures.Add((p, size.magnitude * 0.5f + 9f));      // a clearing round each cabin: no trees or rocks
            Block(p, size.magnitude * 0.5f + 4f);
        }

        string dressingFile = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.dressing"));
        // Snow banked against a rock: a drift round it with a tail trailing downwind (the wind is from the
        // north-west). Where two drifts overlap the bigger one wins, so a tor's boulders do not pile up drifts.
        var preDrift = (float[])height.Clone();
        var drift = new float[count];
        void Bank(Vector2 at, float radius, float amount)
        {
            Vector2 lee = new Vector2(0.8f, -0.6f), across = new Vector2(0.6f, 0.8f);
            int r = Mathf.CeilToInt(radius * 2f / Cell), centre = Cell_(at);
            for (int oj = -r; oj <= r; oj++)
                for (int oi = -r; oi <= r; oi++)
                {
                    int i = centre % row + oi, j = centre / row + oj;
                    if (i < 0 || j < 0 || i > nx || j > nz) continue;
                    int k = j * row + i;
                    if (!(preDrift[k] > 0.6f)) continue;
                    Vector2 q = At(k) - at;
                    float along = Vector2.Dot(q, lee), side = Vector2.Dot(q, across);
                    if (along > 0f) along /= 2f;      // the tail runs twice as far downwind
                    float d = Mathf.Sqrt(along * along + side * side) / radius;
                    float bump = amount * (1f - Smooth(d)) * (0.75f + 0.5f * Mathf.PerlinNoise(At(k).x * 0.2f, At(k).y * 0.2f));
                    if (bump <= drift[k]) continue;
                    // no drift on paths, the plaza, the lake, cabins or puzzle sites, and it tapers off towards them
                    Vector2 g = At(k);
                    bump *= !Free(g, 0f) ? 0f : !Free(g, 2f) ? 0.35f : !Free(g, 4f) ? 0.7f : 1f;
                    if (bump <= drift[k]) continue;
                    drift[k] = bump;
                    height[k] = preDrift[k] + bump;
                }
        }
        WinterIsleDressing.Dress(new WinterIsleDressing.Land
        {
            Height = HeightAt,
            Slope = p => Slope(Cell_(p)),
            ToShore = ToShore,
            ToPath = PathDistance,
            Free = Free,
            Beach = p => beach[Cell_(p)] > 0.35f && HeightAt(p) < 4f,
            Block = Block,
            Bank = Bank,
            X0 = X0, X1 = X1, Z0 = Z0, Z1 = Z1,
        }, dressingFile);

        // 7. Mesh: only the cells with all four corners, so nothing shows under water beyond the rim.
        //    Vertex colours: red says how fresh the snow is (1 untouched, 0 a trodden path), green how much bare
        //    ground shows through (steep banks, wind-scoured knolls, heath in patches, under bushes), blue sand.
        var bareUnder = new float[count];
        foreach (var cover in WinterIsleDressing.Cover)
        {
            int r = Mathf.CeilToInt((cover.radius + 1.5f) / Cell), centre = Cell_(cover.at);
            for (int oj = -r; oj <= r; oj++)
                for (int oi = -r; oi <= r; oi++)
                {
                    int i = centre % row + oi, j = centre / row + oj;
                    if (i < 0 || j < 0 || i > nx || j > nz) continue;
                    float d = new Vector2(oi, oj).magnitude * Cell;
                    int k = j * row + i;
                    bareUnder[k] = Mathf.Max(bareUnder[k], 1f - Smooth((d - cover.radius * 0.6f) / 2.5f));
                }
        }
        var index = new int[count];
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var land = new float[count];
        float top = float.MinValue; Vector2 topAt = Vector2.zero;
        for (int k = 0; k < count; k++)
        {
            land[k] = height[k] > 0.2f ? height[k] : float.NaN;
            if (float.IsNaN(height[k])) { index[k] = -1; continue; }
            Vector2 p = At(k);
            if (height[k] > top) { top = height[k]; topAt = p; }
            index[k] = verts.Count;
            verts.Add(new Vector3(p.x - root.x, height[k] - root.y, p.y - root.z));
            uvs.Add(new Vector2(p.x / 8f, p.y / 8f));
            int i = k % row, j = k / row;
            float around = 0f; int n = 0;
            for (int o = -3; o <= 3; o += 6)
            {
                if (i + o >= 0 && i + o <= nx && !float.IsNaN(height[k + o])) { around += height[k + o]; n++; }
                if (j + o >= 0 && j + o <= nz && !float.IsNaN(height[k + o * row])) { around += height[k + o * row]; n++; }
            }
            float convex = n > 0 ? height[k] - around / n : 0f;
            float slope = Slope(k);
            float heath = Smooth((Mathf.PerlinNoise(p.x * 0.035f + 7.7f, p.y * 0.035f + 2.2f) - 0.58f) / 0.12f) * 0.6f * (1f - Smooth((height[k] - 28f) / 14f));
            float shoreline = height[k] > 0f && height[k] < 1.4f ? 0.55f * (1f - beach[k]) * Smooth((Mathf.PerlinNoise(p.x * 0.022f + 8.8f, p.y * 0.022f + 1.9f) - 0.38f) / 0.25f) : 0f;      // in stretches: elsewhere snow lies to the water
            float bare = Mathf.Clamp01(Mathf.Clamp01((slope - 0.45f) * 1.2f) * 0.3f + Mathf.Clamp01(convex * 0.15f) + heath + bareUnder[k] * 0.8f + shoreline) * (1f - trodden[k]);
            float worn = trodden[k] * Smooth((Mathf.PerlinNoise(p.x * 0.08f + 3.1f, p.y * 0.08f + 7.3f) - 0.4f) / 0.3f) * 0.6f;      // trodden through to the ground in places
            bare = Mathf.Max(bare, worn);
            float sand = beach[k] * (1f - Smooth((height[k] - 0.7f) / 1.5f));
            colors.Add(new Color(1f - trodden[k], bare, sand, Mathf.Clamp01(0.5f + convex * 0.11f)));      // alpha: hollow (0) .. crest (1)
        }
        var tris = new List<int>();
        float area = 0f;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * row + i;
                int a = index[k], bb = index[k + 1], c = index[k + row], d = index[k + row + 1];
                if (a < 0 || bb < 0 || c < 0 || d < 0) continue;
                tris.Add(a); tris.Add(c); tris.Add(bb);
                tris.Add(bb); tris.Add(c); tris.Add(d);
                if (height[k] > 0f && height[k + 1] > 0f && height[k + row] > 0f && height[k + row + 1] > 0f) area += Cell * Cell;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();      // over the whole island, so the tiles below share their seam normals
        var normals = mesh.normals;
        Object.DestroyImmediate(mesh);

        // The ground goes in as tiles of TileCells x TileCells cells (each well under 65,535 vertices) rather than one
        // mesh: as one 121,000-vertex mesh, the game left out whole stretches of it near a player standing on them.
        const int TileCells = 96;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("Island t:Mesh", new[] { "Assets/Puzzles/WinterIsle/Meshes" }))
        {
            string old = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(old).StartsWith("Island")) UnityEditor.AssetDatabase.DeleteAsset(old);
        }
        Material groundMaterial = b.ShaderMaterial("BWM/Snow", "Snow", m => { m.SetFloat("_RockLine", 0.6f); m.SetFloat("_RockTiling", 0.3f); Kit.SetSnowMaps(m); });
        int tiles = 0;
        for (int tj = 0; tj * TileCells < nz; tj++)
            for (int ti = 0; ti * TileCells < nx; ti++)
            {
                var local = new Dictionary<int, int>();
                var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tu = new List<Vector2>(); var tc = new List<Color>(); var tt = new List<int>();
                int Local(int g)
                {
                    if (local.TryGetValue(g, out int l)) return l;
                    l = tv.Count; local[g] = l;
                    tv.Add(verts[g]); tn.Add(normals[g]); tu.Add(uvs[g]); tc.Add(colors[g]);
                    return l;
                }
                for (int j = tj * TileCells; j < Mathf.Min(nz, (tj + 1) * TileCells); j++)
                    for (int i = ti * TileCells; i < Mathf.Min(nx, (ti + 1) * TileCells); i++)
                    {
                        int k = j * row + i;
                        int a = index[k], bb = index[k + 1], c = index[k + row], d = index[k + row + 1];
                        if (a < 0 || bb < 0 || c < 0 || d < 0) continue;
                        tt.Add(Local(a)); tt.Add(Local(c)); tt.Add(Local(bb));
                        tt.Add(Local(bb)); tt.Add(Local(c)); tt.Add(Local(d));
                    }
                if (tt.Count == 0) continue;
                var tile = new Mesh();
                tile.SetVertices(tv); tile.SetNormals(tn); tile.SetUVs(0, tu); tile.SetColors(tc);
                tile.SetTriangles(tt, 0);
                tile.RecalculateBounds();
                b.MeshPiece("Island " + ti + "-" + tj, tile, groundMaterial);
                tiles++;
            }

        // 8. Ice: the lake, and the creek above and below it (sheets just over the beds players walk on)
        var ice = new MeshBuilder();
        var iceColor = new Color(0.62f, 0.80f, 0.93f);
        Kit.AddDisc(ice, new Vector3(Lake.x - root.x, LakeLevel - 0.3f - root.y, Lake.y - root.z), LakeRadius + 7f, 0.36f, iceColor, 40);
        for (int s = 0; s + 1 < Creek.Length; s++)
        {
            Vector3 a = Creek[s], c = Creek[s + 1];
            Vector2 dir = new Vector2(c.x - a.x, c.z - a.z).normalized, side = new Vector2(dir.y, -dir.x) * 4.2f;
            Vector3 A(Vector3 q, float sign) => new Vector3(q.x + side.x * sign - root.x, Mathf.Max(q.y, 0.05f) + 0.06f - root.y, q.z + side.y * sign - root.z);
            ice.Quad(A(a, -1f), A(c, -1f), A(c, 1f), A(a, 1f), iceColor);
            ice.Quad(A(a, 1f), A(c, 1f), A(c, -1f), A(a, -1f), iceColor);
        }
        b.MeshPiece("Ice", ice.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Ice", m => { m.SetFloat("_SnowLine", 1.02f); m.SetFloat("_SnowNoise", 0.9f); m.SetFloat("_SnowEdge", 0.05f); }), false);

        // 9. Our own trees: firs, slim spruces and bare birches (WinterIsleDressing.TreeModels), each model with a
        // lighter version for distance; every such tree on the island is one of them, turned and scaled a little
        Texture2D needles = Kit.FirBranchTexture("Assets/Puzzles/WinterIsle/FirBranch.png");
        Material firMaterial = b.ShaderMaterial("BWM/Fir", "Fir", m => m.SetTexture("_MainTex", needles));
        Material birchMaterial = b.ShaderMaterial("BWM/Snowy", "Birch", m => { m.SetFloat("_SnowLine", 0.45f); m.SetFloat("_SnowEdge", 0.08f); m.SetFloat("_SnowNoise", 0.5f); });
        var models = new List<(Mesh near, Mesh far, float height, Material material)>();
        for (int v = 0; v < WinterIsleDressing.TreeModels.Count; v++)
        {
            var (kind, tall, seed) = WinterIsleDressing.TreeModels[v];
            var near = new MeshBuilder();
            var far = new MeshBuilder();
            if (kind == "birch") { Kit.AddBirch(near, Vector3.zero, tall, seed, 1f); Kit.AddBirch(far, Vector3.zero, tall, seed, 0.5f); }
            else
            {
                float girth = kind == "spruce" ? 0.58f : 1f;
                Kit.AddFir(near, Vector3.zero, tall, seed, 1f, girth);
                Kit.AddFir(far, Vector3.zero, tall, seed, 0.5f, girth);
            }
            models.Add((b.SaveMesh("Fir " + v, near.ToMesh()), b.SaveMesh("Fir " + v + " far", far.ToMesh()), tall, kind == "birch" ? birchMaterial : firMaterial));
        }
        // The models ride in the pack switched off, far under the island; Winter Walk copies them to every place
        // the dressing list gives ("fir:<model>")
        var firModels = new GameObject("Fir Models");
        firModels.transform.SetParent(b.Root.transform, false);
        firModels.transform.localPosition = new Vector3(landing.x - root.x, -60f - root.y, landing.y - root.z);
        firModels.SetActive(false);
        for (int v = 0; v < models.Count; v++)
        {
            var model = models[v];
            var go = new GameObject("Fir Model " + v);
            go.transform.SetParent(firModels.transform, false);
            var lods = new LOD[2];
            for (int l = 0; l < 2; l++)
            {
                var part = new GameObject(l == 0 ? "Near" : "Far");
                part.transform.SetParent(go.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = l == 0 ? model.near : model.far;
                var rend = part.AddComponent<MeshRenderer>();
                rend.sharedMaterial = model.material;
                lods[l] = new LOD(l == 0 ? 0.05f : 0.004f, new Renderer[] { rend });
            }
            go.AddComponent<LODGroup>().SetLODs(lods);
            CapsuleCollider trunk = go.AddComponent<CapsuleCollider>();
            trunk.center = Vector3.up * (model.height * 0.3f);
            trunk.radius = model.material == birchMaterial ? 0.18f : Mathf.Max(0.25f, model.height * 0.03f);
            trunk.height = model.height * 0.6f;
        }

        // 10. The hamlet's cabins, and lanterns along the trails
        var cabinMesh = new MeshBuilder();
        var glowMesh = new MeshBuilder();
        var cabinColliders = new GameObject("Cabin Walls");
        cabinColliders.transform.SetParent(b.Root.transform, false);
        int seedCabin = 0;
        foreach (var cabin in cabins)
        {
            float ground = Mathf.Min(HeightAt(cabin.at), Mathf.Min(HeightAt(cabin.at + Vector2.one * 3f), HeightAt(cabin.at - Vector2.one * 3f)));
            var foot = new Vector3(cabin.at.x - root.x, ground - root.y, cabin.at.y - root.z);
            Kit.AddCabin(cabinMesh, glowMesh, foot, cabin.size, cabin.yaw, ++seedCabin);
            var box = new GameObject("Cabin");
            box.transform.SetParent(cabinColliders.transform, false);
            box.transform.localPosition = foot + Vector3.up * 1.8f;
            box.transform.localRotation = Quaternion.Euler(0f, cabin.yaw, 0f);
            box.AddComponent<BoxCollider>().size = new Vector3(cabin.size.x, 3.6f, cabin.size.y);
        }
        // Lanterns: a waymark now and then, not a street. Close together near the plaza and the hamlet, further
        // apart out on the trails, on whichever side, and never two near each other where trails share a stretch.
        int lanterns = 0;
        var lit = new List<Vector2>();
        var lanternRandom = new System.Random(2512);
        float Rl(float lo, float hi) => lo + (hi - lo) * (float)lanternRandom.NextDouble();
        foreach (var r in routes)
        {
            if (!r.trail) continue;
            var pts = new List<Vector2>(r.points);
            pts.Reverse();      // from where the trail leaves the network, outwards
            float run = 0f, next = Rl(15f, 35f);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], c = pts[i + 1];
                float seg = (c - a).magnitude;
                while (run + seg >= next)
                {
                    Vector2 p = Vector2.Lerp(a, c, (next - run) / Mathf.Max(seg, 0.001f));
                    Vector2 dir = (c - a).normalized, offset = new Vector2(dir.y, -dir.x) * (Rl(0f, 1f) < 0.5f ? -1f : 1f) * Rl(2.6f, 4f);
                    Vector2 q = p + offset;
                    float fromPlaza = (q - Plaza).magnitude;
                    if (fromPlaza > PlazaRadius + 4f && HeightAt(q) > 0.8f && !lit.Exists(o => (o - q).magnitude < 30f))
                    {
                        Kit.AddLantern(cabinMesh, glowMesh, new Vector3(q.x - root.x, HeightAt(q) - root.y, q.y - root.z), lanterns);
                        lit.Add(q);
                        lanterns++;
                    }
                    next += Mathf.Lerp(38f, 120f, Smooth((fromPlaza - 120f) / 450f)) * Rl(0.75f, 1.35f);
                }
                run += seg;
            }
        }
        // Snow poles: trail markers every 20 to 30 m on alternate sides, so a trail reads at eye level
        int poles = 0;
        var poled = new List<Vector2>();
        foreach (var r in routes)
        {
            float run = 0f, next = Rl(6f, 20f);
            int side = 1;
            for (int i = 0; i + 1 < r.points.Count; i++)
            {
                Vector2 a = r.points[i], c = r.points[i + 1];
                float seg = (c - a).magnitude;
                while (run + seg >= next)
                {
                    Vector2 p = Vector2.Lerp(a, c, (next - run) / Mathf.Max(seg, 0.001f));
                    Vector2 dir = (c - a).normalized;
                    Vector2 q = p + new Vector2(dir.y, -dir.x) * side * Rl(2.2f, 3f);
                    if ((q - Plaza).magnitude > PlazaRadius + 6f && HeightAt(q) > 0.8f && !poled.Exists(o => (o - q).magnitude < 12f) && !lit.Exists(o => (o - q).magnitude < 8f))
                    {
                        Kit.AddSnowPole(cabinMesh, new Vector3(q.x - root.x, HeightAt(q) - root.y, q.y - root.z), poles);
                        poled.Add(q);
                        poles++;
                    }
                    next += Rl(20f, 30f);
                    side = -side;
                }
                run += seg;
            }
        }
        b.MeshPiece("Hamlet and Lanterns", cabinMesh.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), false);
        b.MeshPiece("Warm Windows", glowMesh.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);

        // Viewpoints for review shots: along every path, every 130 m, looking the way the path goes
        var views = new System.Text.StringBuilder("x\tz\tdx\tdz\n");
        foreach (var r in routes)
        {
            float run = 0f, next = 60f;
            for (int i = 0; i + 1 < r.points.Count; i++)
            {
                float seg = (r.points[i + 1] - r.points[i]).magnitude;
                while (run + seg >= next)
                {
                    Vector2 p = Vector2.Lerp(r.points[i], r.points[i + 1], (next - run) / Mathf.Max(seg, 0.001f));
                    Vector2 dir = (r.points[0] - r.points[r.points.Count - 1]).normalized;      // routes run from the end back to the plaza: look outwards
                    views.Append(Sdk.F(p.x)).Append('\t').Append(Sdk.F(p.y)).Append('\t').Append(Sdk.F(dir.x)).Append('\t').Append(Sdk.F(dir.y)).Append('\n');
                    next += 130f;
                }
                run += seg;
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "world", "winter-viewpoints.tsv")), views.ToString());
        Debug.Log("[BWM] Winter Isle: " + cabins.Count + " cabins, " + lanterns + " lanterns, " + poles + " snow poles");

        b.RegisterLand(X0, Z0, Cell, nx, nz, land, blocked);

        // 9. Causeway from the mainland shore to the landing
        Vector2 across_ = ShorePoint - landing, mid = (ShorePoint + landing) / 2f;
        float yaw = Mathf.Atan2(across_.x, across_.y) * Mathf.Rad2Deg;
        b.Box("Causeway", mid.x - root.x, mid.y - root.z, new Vector3(7f, 3.2f, across_.magnitude + 24f), PackedSnow, -2.2f - root.y, yaw, false);

        Snowfall(b);

        // Christmas lights down both sides of the causeway, a post every 9 m
        Vector2 along = across_.normalized, side_ = new Vector2(along.y, -along.x);
        float span = across_.magnitude + 16f;
        for (int side = -1; side <= 1; side += 2)
        {
            var posts = new List<Vector3>();
            for (float d = -8f; d <= span - 8f + 0.01f; d += 9f)
            {
                Vector2 p = landing + along * d + side_ * (side * 3.1f);
                posts.Add(new Vector3(p.x - root.x, 1f + 2.4f - root.y, p.y - root.z));
            }
            Kit.LightString(b, side < 0 ? "Causeway Lights Left" : "Causeway Lights Right", posts, 0.35f, 0.5f, false, false, side + 3);
            foreach (Vector3 post in posts)
                b.Box("Light Post", post.x, post.z, new Vector3(0.12f, 2.9f, 0.12f), new Color(0.36f, 0.25f, 0.17f), 0.5f - root.y, 0f, false);
        }

        // Materials Winter Walk uses at runtime on copies of the game's own objects. A material only reaches the
        // game inside a pack if something in the pack uses it, so a speck deep under the island carries them.
        var speck = new MeshBuilder();
        Kit.AddBox(speck, new Vector3(landing.x - root.x, -30f - root.y, landing.y - root.z), Vector3.one * 0.01f, Color.white);
        b.MeshPiece("Material Store", speck.ToMesh(), b.ShaderMaterial("BWM/SnowOverlay", "SnowOverlay", Kit.SetSnowMaps), false);

        b.Empty("Debug Teleport", topAt.x - root.x, topAt.y - root.z, top + 2f - root.y, false);
        Sdk.Set(b.Puzzle, "debugTeleport", b.Root.transform.Find("Debug Teleport"));

        int steep = 0, flat = 0, sandy = 0;
        for (int k = 0; k < count; k++)
        {
            if (!(height[k] > 0.5f)) continue;
            float s = Slope(k);
            if (s > 0.7f) steep++; else if (s < 0.06f) flat++;
            if (beach[k] > 0.35f && height[k] < 4f) sandy++;
        }
        int mainland = 0;
        for (float mz = -1024f; mz < 1024f; mz += 8f)
            for (float mx = -1024f; mx < 1024f; mx += 8f)
                if (b.Ground.At(mx, mz, out float my) && my > 0.5f) mainland++;
        Debug.Log("[BWM] Winter Isle: " + knolls.Count + " knolls; " + routed + " of " + targets.Count + " paths routed from the plaza (" + trails + " of them trails); the base game's island is " + Sdk.F(mainland * 64f / 1e6f) + " km2");
        Debug.Log("[BWM] Winter Isle: " + Sdk.F(area / 1e6f) + " km2 above the sea, summit " + Sdk.F(top) + " m at " + Sdk.F(topAt.x) + ", " + Sdk.F(topAt.y)
            + "; level ground " + Sdk.F(flat * Cell * Cell / 1e6f) + " km2, cliff " + Sdk.F(steep * Cell * Cell / 1e6f) + " km2, beach " + Sdk.F(sandy * Cell * Cell / 1e6f)
            + " km2; plaza at " + Sdk.F(level[0]) + " m; mesh " + verts.Count + " vertices in " + tiles + " tiles");
    }
}
