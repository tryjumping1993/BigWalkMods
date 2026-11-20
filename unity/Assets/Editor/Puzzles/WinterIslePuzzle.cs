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

    // The coast's wandering, in metres added to how far inside the coastline a point is. Broad: wide coves and
    // bulges. Full: those, with rocky points pushed out along the lines of one ridged noise and narrow inlets cut
    // in along the lines of another, so the outline is ragged at the scale a walker sees.
    static float CoastBroad(Vector2 p)
    {
        return (Mathf.PerlinNoise(p.x * 0.0032f + 13.1f, p.y * 0.0032f + 2.7f) - 0.5f) * 150f
            + (Mathf.PerlinNoise(p.x * 0.006f + 3.3f, p.y * 0.006f + 8.8f) - 0.5f) * 100f + (Mathf.PerlinNoise(p.x * 0.02f + 1.3f, p.y * 0.02f + 4.1f) - 0.5f) * 34f;
    }
    static float CoastFull(Vector2 p)
    {
        float points = (Ridged(p.x * 0.0085f + 31.3f, p.y * 0.0085f + 9.9f) - 0.3f) * 80f;
        float inlets = Ridged(p.x * 0.0125f + 7.7f, p.y * 0.0125f + 41.4f);
        return CoastBroad(p) + points - inlets * inlets * 70f;
    }

    // Deep coves bitten out of the coast, each with a beach round its head, and a bar of sand out to an islet
    // (chosen by the build, where the coast is low and nothing else is)
    static readonly List<(Vector2 at, float radius)> coveList = new List<(Vector2, float)>();
    static readonly List<(Vector2 from, Vector2 to)> barList = new List<(Vector2, Vector2)>();
    static float ToBar(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
        return (p - (a + ab * t)).magnitude;
    }

    // How much of a sandy cove a point of coast is: sand gathers in the bays (where the coast is pushed in), in
    // stretches a slow noise picks, away from the high ground. The points between the bays are rock.
    static float BeachAmount(Vector2 p)
    {
        float b = Smooth((Mathf.PerlinNoise(p.x * 0.0075f + 61.1f, p.y * 0.0075f + 3.7f) - 0.4f) / 0.1f);
        float bay = Smooth((-CoastFull(p) - 1f) / 20f);
        float sand = b * bay;
        foreach (var cove in coveList)
        {
            float d = (p - cove.at).magnitude;
            if (d < cove.radius + 60f) sand = Mathf.Max(sand, 1f - Smooth((d - cove.radius - 10f) / 45f));
        }
        sand *= (1f - Smooth(RidgeHeight(p) / 10f)) * Smooth(((p - Lake).magnitude - LakeRadius - 25f) / 40f);      // (the lake's shore is snow, not sand)
        foreach (var bar in barList)
        {
            float d = ToBar(p, bar.from, bar.to);
            if (d < 40f) sand = Mathf.Max(sand, 1f - Smooth((d - 12f) / 24f));
        }
        return sand;
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
        float rise = Smooth(inside / Mathf.Lerp(26f, 48f, beach));
        float lowland = 0.9f + (2f + 10f * Fbm(x * 0.0045f + 40.2f, z * 0.0045f + 17.9f, 3) + 3f * Mathf.PerlinNoise(x * 0.018f + 9.1f, z * 0.018f + 61.4f)) * rise;
        // ...broken up so that no view across it stays open for long: sharp-crested ridgelets (like moraines),
        // hummocks, dells, and a fine unevenness underfoot
        float ridgelets = (Ridged(x * 0.0105f + 3.3f, z * 0.0105f + 7.7f) - 0.35f) * 8f;
        float hummocks = (Fbm(x * 0.042f + 21.7f, z * 0.042f + 5.3f, 2) - 0.5f) * 4f;
        float dells = Smooth((Mathf.PerlinNoise(x * 0.019f + 77.1f, z * 0.019f + 12.9f) - 0.6f) / 0.12f) * 3.5f;
        float fine = (Mathf.PerlinNoise(x * 0.09f + 3.9f, z * 0.09f + 44.2f) - 0.5f) * 1.1f + (Mathf.PerlinNoise(x * 0.21f + 8.1f, z * 0.21f + 1.7f) - 0.5f) * 0.45f;
        lowland += (Mathf.Max(ridgelets, -1.5f) + hummocks - dells) * Smooth(inside / 45f) + fine * Smooth(inside / 8f);
        // in places the lowland is benched: level shelves a couple of metres apart with a bank between
        float benched = Smooth((Mathf.PerlinNoise(x * 0.0065f + 91.7f, z * 0.0065f + 33.1f) - 0.5f) / 0.12f) * Smooth(inside / 40f);
        if (benched > 0f) lowland = Mathf.Lerp(lowland, Terrace(lowland, 2.4f, 0.25f), benched);
        float beachY = 0.35f + inside * 0.035f + fine * 0.3f;
        float y = Mathf.Lerp(lowland, Mathf.Min(lowland, beachY), beach * (1f - Smooth((inside - 14f) / 24f)));

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

        // 2. Heights above sea level, before levelling; and how much of each point is beach.
        //    Near anything that has a place (the plaza, puzzle sites, the lake, the creek, the gorge, the causeway's
        //    landing) the coast keeps its broad line, so none of them is ever cut off by a new inlet.
        float Guard(Vector2 p)
        {
            float g = Mathf.Min((p - Plaza).magnitude - PlazaRadius, (p - Lake).magnitude - LakeRadius);
            g = Mathf.Min(g, Mathf.Min(ToLine(p, Creek, out _), ToLine(p, Gorge)));
            g = Mathf.Min(g, (p - ShorePoint).magnitude - 90f);
            foreach (Vector3 site in b.OtherSites) g = Mathf.Min(g, (p - new Vector2(site.x, site.y)).magnitude - site.z);
            return g;
        }
        var inside = new float[count];
        var offMainland = new float[count];
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
            float coves = Mathf.Lerp(CoastBroad(p), CoastFull(p), Smooth((Guard(p) - 30f) / 55f))
                + (Mathf.PerlinNoise(p.x * 0.05f + 6.6f, p.y * 0.05f + 2.4f) - 0.5f) * 14f * (1f - BeachAmount(p));      // rocky shores ragged, beaches smooth
            // the coves can push the coast outwards, but never off the grid (the land would end there in a straight
            // cut): the coast stays about 45 m inside it, wavering, with room for the submerged rim
            float toGrid = Mathf.Min(Mathf.Min(p.x - X0, X1 - p.x), Mathf.Min(p.y - Z0, Z1 - p.y));
            float rim = toGrid - 45f + (Mathf.PerlinNoise(p.x * 0.013f + 5.1f, p.y * 0.013f + 9.4f) - 0.5f) * 30f;
            inside[k] = Mathf.Min(Mathf.Min(toMainland, toEdge) + coves, rim);
            offMainland[k] = Mathf.Min(toMainland, toGrid - 45f);
        }
        // Coves: three or four round bays bitten out of low coast well away from everything, so the island's outline
        // is not an oval; BeachAmount puts sand round the head of each
        coveList.Clear();
        barList.Clear();
        var coveRandom = new System.Random(808);
        for (int tries = 0; tries < 60000 && coveList.Count < 4; tries++)
        {
            int k = coveRandom.Next(count);
            if (Mathf.Abs(inside[k]) > 4f || offMainland[k] < 160f) continue;
            Vector2 p = At(k);
            if (Mathf.Min(Mathf.Min(p.x - X0, X1 - p.x), Mathf.Min(p.y - Z0, Z1 - p.y)) < 150f || Guard(p) < 230f || RidgeHeight(p) > 10f) continue;
            bool apart = true;
            foreach (var other in coveList) if ((other.at - p).magnitude < 430f) apart = false;
            if (apart) coveList.Add((p, 85f + (float)coveRandom.NextDouble() * 45f));
        }
        for (int k = 0; k < count; k++)
        {
            Vector2 p = At(k);
            foreach (var cove in coveList)
            {
                Vector2 d = p - cove.at;
                if (d.magnitude > cove.radius * 1.3f) continue;
                float r = cove.radius * (1f + 0.12f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 3f + cove.at.x) + 0.06f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 7f + cove.at.y));
                inside[k] = Mathf.Min(inside[k], d.magnitude - r);
            }
        }
        // Islets: a few small rocky ones standing off the coast, clear of the channel and of each other
        var islets = new List<(Vector2 at, float radius)>();
        var isletRandom = new System.Random(4242);
        for (int tries = 0; tries < 20000 && islets.Count < 7; tries++)
        {
            int k = isletRandom.Next(count);
            float radius = 10f + (float)isletRandom.NextDouble() * 14f;
            if (inside[k] > -48f - radius || inside[k] < -120f || offMainland[k] < 60f + radius) continue;
            Vector2 p = At(k);
            bool apart = true;
            foreach (var other in islets) if ((other.at - p).magnitude < 190f) apart = false;
            if (apart) islets.Add((p, radius));
        }
        // A bar: the islet nearest the shore is joined to it by a low spit of sand (as the base island's two halves are)
        {
            float nearest = float.MaxValue; Vector2 barFrom = Vector2.zero, barTo = Vector2.zero;
            foreach (var islet in islets)
                for (int k = 0; k < count; k++)
                {
                    if (inside[k] < 2f || inside[k] > 8f) continue;
                    float d = (At(k) - islet.at).magnitude;
                    if (d < nearest && d > islet.radius + 35f) { nearest = d; barFrom = islet.at; barTo = At(k); }
                }
            if (nearest < 170f)
            {
                barList.Add((barFrom, barTo + (barTo - barFrom).normalized * 12f));
                for (int k = 0; k < count; k++)
                {
                    Vector2 p = At(k);
                    float d = ToBar(p, barFrom, barTo);
                    if (d < 30f) inside[k] = Mathf.Max(inside[k], 9f + 3f * Mathf.PerlinNoise(p.x * 0.05f, p.y * 0.05f) - d);
                }
            }
        }
        var height = new float[count];
        var beach = new float[count];
        for (int k = 0; k < count; k++)
        {
            Vector2 p = At(k);
            float lift = 0f;
            foreach (var islet in islets)
            {
                Vector2 d = p - islet.at;
                if (d.magnitude > islet.radius * 1.6f + 40f) continue;
                float r = islet.radius * (1f + 0.3f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 3f + islet.at.x) + 0.15f * Mathf.Sin(Mathf.Atan2(d.y, d.x) * 5f + islet.at.y));
                inside[k] = Mathf.Max(inside[k], r - d.magnitude);
                lift = Mathf.Max(lift, (1.2f + islet.radius * 0.1f) * Smooth((r - d.magnitude) / (r * 0.8f)));      // a low hump for the dressing's granite to stand on
            }
            height[k] = RawHeight(p.x, p.y, inside[k]);
            if (lift > 0f && height[k] > 0f) height[k] += lift;
            beach[k] = lift > 0f ? 0f : BeachAmount(p);
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
            if (!reachable[k] || !(beach[k] > 0.45f) || !(height[k] > 0.8f && height[k] < 2.5f)) continue;
            Vector2 p = At(k);
            bool far = true;
            foreach (Vector2 e in beachEnds) if ((e - p).magnitude < 230f) far = false;
            if (far) { beachEnds.Add(p); trailEnds.Add(k); }
        }
        // ...and out to the rocky points: the tips of the headlands, where the view is along the coast
        var pointEnds = new List<Vector2>();
        for (int k = 3; k < count; k += 5)
        {
            if (!reachable[k] || beach[k] > 0.1f || !(height[k] > 1.5f && height[k] < 9f) || inside[k] > 14f || inside[k] < 4f) continue;
            Vector2 p = At(k);
            if (Ridged(p.x * 0.0085f + 31.3f, p.y * 0.0085f + 9.9f) < 0.7f) continue;      // on a point (CoastFull's ridge lines)
            bool far = true;
            foreach (Vector2 e in pointEnds) if ((e - p).magnitude < 300f) far = false;
            foreach (Vector2 e in beachEnds) if ((e - p).magnitude < 150f) far = false;
            if (far) { pointEnds.Add(p); trailEnds.Add(k); }
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
            trodden[k] = 1f - Smooth((pathDistance - 0.4f + (Mathf.PerlinNoise(p.x * 0.15f, p.y * 0.15f) - 0.5f) * 0.9f) / 1.6f);      // a narrow, ragged-edged track
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
        // The ground's height anywhere: a smooth curve through the grid's heights (bicubic; bilinear at the rim of
        // the land, where the grid runs out), with crags on steep ground away from the paths. The mesh follows
        // exactly this, so banks, peaks and the shore are not flat 3 m facets.
        var crag = new float[count];
        for (int k = 0; k < count; k++)
            crag[k] = float.IsNaN(height[k]) || height[k] < 1.2f ? 0f : Smooth((Slope(k) - 0.62f) / 0.45f) * Smooth((toPath[k] * Cell - 5f) / 6f);
        float Cubic(float h0, float h1, float h2, float h3, float t) => h1 + 0.5f * t * (h2 - h0 + t * (2f * h0 - 5f * h1 + 4f * h2 - h3 + t * (3f * (h1 - h2) + h3 - h0)));
        float HeightIn(int i, int j, float tx, float tz)      // in the cell whose low corner is (i, j), all four corners land
        {
            int k = j * row + i;
            float h00 = height[k], h10 = height[k + 1], h01 = height[k + row], h11 = height[k + row + 1];
            float y = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
            bool cubic = i >= 1 && j >= 1 && i + 2 <= nx && j + 2 <= nz;
            for (int oj = -1; oj <= 2 && cubic; oj++)
                for (int oi = -1; oi <= 2; oi++)
                    if (float.IsNaN(height[k + oj * row + oi])) { cubic = false; break; }
            if (cubic)
            {
                float r0 = Cubic(height[k - row - 1], height[k - row], height[k - row + 1], height[k - row + 2], tx);
                float r1 = Cubic(height[k - 1], h00, h10, height[k + 2], tx);
                float r2 = Cubic(height[k + row - 1], h01, h11, height[k + row + 2], tx);
                float r3 = Cubic(height[k + 2 * row - 1], height[k + 2 * row], height[k + 2 * row + 1], height[k + 2 * row + 2], tx);
                // the curve may bulge a little past the corners (rounding a crest), but not ring at a cliff's foot and lip
                float lo = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)), hi = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
                y = Mathf.Clamp(Cubic(r0, r1, r2, r3, tz), lo - 0.04f * (hi - lo) - 0.05f, hi + 0.04f * (hi - lo) + 0.05f);
            }
            float rough = Mathf.Lerp(Mathf.Lerp(crag[k], crag[k + 1], tx), Mathf.Lerp(crag[k + row], crag[k + row + 1], tx), tz);
            if (rough > 0f)
            {
                float x = X0 + (i + tx) * Cell, z = Z0 + (j + tz) * Cell;
                y += rough * ((Ridged(x * 0.17f + 2.7f, z * 0.17f + 8.3f) - 0.4f) * 1.1f + (Mathf.PerlinNoise(x * 0.43f + 6.1f, z * 0.43f + 1.9f) - 0.5f) * 0.5f);
            }
            return y;
        }
        float HeightAt(Vector2 p)
        {
            float fx = Mathf.Clamp((p.x - X0) / Cell, 0f, nx - 0.001f), fz = Mathf.Clamp((p.y - Z0) / Cell, 0f, nz - 0.001f);
            int i = (int)fx, j = (int)fz, k = j * row + i;
            if (float.IsNaN(height[k]) || float.IsNaN(height[k + 1]) || float.IsNaN(height[k + row]) || float.IsNaN(height[k + row + 1])) return height[Cell_(p)];
            return HeightIn(i, j, fx - i, fz - j);
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
        // Woods: a few dense stands that the trails run through (alternately fir and birch), so that a walker is
        // sometimes among close trunks instead of always in the open
        var woods = new List<(Vector2 at, float radius, bool birch)>();
        foreach (var r in routes)
        {
            if (!r.trail) continue;
            for (int i = 6; i < r.points.Count - 6 && woods.Count < 9; i += 4)
            {
                Vector2 p = r.points[i];
                int k = Cell_(p);
                if (!(height[k] > 3f && height[k] < 34f) || Slope(k) > 0.3f || (p - Plaza).magnitude < 140f || (p - Lake).magnitude < LakeRadius + 60f) continue;
                bool clearOfSites = true;
                foreach (Vector3 site in b.OtherSites) if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 70f) clearOfSites = false;
                if (!clearOfSites) continue;
                bool apart = true;
                foreach (var w in woods) if ((w.at - p).magnitude < 200f) apart = false;
                foreach (var c in cabins) if ((c.at - p).magnitude < 60f) apart = false;
                if (apart) woods.Add((p, 55f + 25f * Mathf.PerlinNoise(p.x * 0.01f, p.y * 0.01f), woods.Count % 3 == 1));
            }
        }
        var isletList = new List<(Vector2 at, float radius)>(islets);
        WinterIsleDressing.Dress(new WinterIsleDressing.Land
        {
            Woods = woods,
            Islets = isletList,
            Lake = Lake,
            LakeRadius = LakeRadius,
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
        // ...and two more, carried in the mesh's texture coordinates: needle litter under our conifers (x), scree at a
        // rock's foot (y)
        var under = new Vector2[count];
        void Spot(List<(Vector2 at, float radius)> list, bool needles)
        {
            foreach (var spot in list)
            {
                int r = Mathf.CeilToInt((spot.radius + 1.5f) / Cell), centre = Cell_(spot.at);
                for (int oj = -r; oj <= r; oj++)
                    for (int oi = -r; oi <= r; oi++)
                    {
                        int i = centre % row + oi, j = centre / row + oj;
                        if (i < 0 || j < 0 || i > nx || j > nz) continue;
                        float amount = 1f - Smooth((new Vector2(oi, oj).magnitude * Cell - spot.radius * 0.6f) / 2.5f);
                        int k = j * row + i;
                        under[k] = needles ? new Vector2(Mathf.Max(under[k].x, amount), under[k].y) : new Vector2(under[k].x, Mathf.Max(under[k].y, amount));
                    }
            }
        }
        Spot(WinterIsleDressing.Needles, true);
        Spot(WinterIsleDressing.Scree, false);
        var tint = new Color[count];
        var land = new float[count];
        float top = float.MinValue; Vector2 topAt = Vector2.zero;
        for (int k = 0; k < count; k++)
        {
            land[k] = height[k] > 0.2f ? height[k] : float.NaN;
            if (float.IsNaN(height[k])) continue;
            Vector2 p = At(k);
            if (height[k] > top) { top = height[k]; topAt = p; }
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
            // ...and beside the trails the snow lies thin in patches, the earth and litter showing (where walkers look)
            float beside = (1f - Smooth((PathDistance(p) - 2.5f) / 9f)) * (1f - trodden[k]) * Smooth((Mathf.PerlinNoise(p.x * 0.065f + 41.7f, p.y * 0.065f + 9.3f) - 0.48f) / 0.14f) * 0.8f;
            bare = Mathf.Max(bare, Mathf.Max(worn, beside));
            float sand = beach[k] * (1f - Smooth((height[k] - 0.4f) / 0.8f));      // a strip by the water; above it the beach lies under snow
            tint[k] = new Color(1f - trodden[k], bare, sand, Mathf.Clamp01(0.5f + convex * 0.11f));      // alpha: hollow (0) .. crest (1)
        }
        bool Whole(int i, int j) { int k = j * row + i; return !float.IsNaN(height[k]) && !float.IsNaN(height[k + 1]) && !float.IsNaN(height[k + row]) && !float.IsNaN(height[k + row + 1]); }
        float area = 0f;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * row + i;
                if (height[k] > 0f && height[k + 1] > 0f && height[k + row] > 0f && height[k + row + 1] > 0f) area += Cell * Cell;
            }

        // The mesh is twice as fine as the grid (1.5 m) and follows HeightAt. It is the collider too, so players
        // walk on exactly what they see. Colours are the grid's, blended.
        const int Sub = 2;
        int fw = nx * Sub + 1;
        var fineIndex = new int[fw * (nz * Sub + 1)];
        for (int f = 0; f < fineIndex.Length; f++) fineIndex[f] = -1;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();
        int Vertex(int fi, int fj, int ci, int cj)      // the fine vertex at (fi, fj), made when first asked for; (ci, cj) is a whole cell it is in
        {
            int f = fj * fw + fi;
            if (fineIndex[f] >= 0) return fineIndex[f];
            float tx = (float)(fi - ci * Sub) / Sub, tz = (float)(fj - cj * Sub) / Sub;
            int k = cj * row + ci;
            var p = new Vector2(X0 + fi * Cell / Sub, Z0 + fj * Cell / Sub);
            fineIndex[f] = verts.Count;
            verts.Add(new Vector3(p.x - root.x, HeightIn(ci, cj, tx, tz) - root.y, p.y - root.z));
            uvs.Add(Vector2.Lerp(Vector2.Lerp(under[k], under[k + 1], tx), Vector2.Lerp(under[k + row], under[k + row + 1], tx), tz));
            colors.Add(Color.Lerp(Color.Lerp(tint[k], tint[k + 1], tx), Color.Lerp(tint[k + row], tint[k + row + 1], tx), tz));
            return fineIndex[f];
        }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                if (!Whole(i, j)) continue;
                for (int sj = 0; sj < Sub; sj++)
                    for (int si = 0; si < Sub; si++)
                    {
                        int fi = i * Sub + si, fj = j * Sub + sj;
                        int a = Vertex(fi, fj, i, j), bb = Vertex(fi + 1, fj, i, j), c = Vertex(fi, fj + 1, i, j), d = Vertex(fi + 1, fj + 1, i, j);
                        tris.Add(a); tris.Add(c); tris.Add(bb);
                        tris.Add(bb); tris.Add(c); tris.Add(d);
                    }
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();      // over the whole island, so the tiles below share their seam normals
        var normals = mesh.normals;
        Object.DestroyImmediate(mesh);

        // The ground goes in as tiles of TileCells x TileCells cells (each well under 65,535 vertices) rather than one
        // mesh: as one mesh, the game left out whole stretches of it near a player standing on them.
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
                        if (!Whole(i, j)) continue;
                        for (int sj = 0; sj < Sub; sj++)
                            for (int si = 0; si < Sub; si++)
                            {
                                int f = (j * Sub + sj) * fw + i * Sub + si;
                                int a = fineIndex[f], bb = fineIndex[f + 1], c = fineIndex[f + fw], d = fineIndex[f + fw + 1];
                                tt.Add(Local(a)); tt.Add(Local(c)); tt.Add(Local(bb));
                                tt.Add(Local(bb)); tt.Add(Local(c)); tt.Add(Local(d));
                            }
                    }
                if (tt.Count == 0) continue;
                var tile = new Mesh();
                tile.SetVertices(tv); tile.SetNormals(tn); tile.SetUVs(0, tu); tile.SetColors(tc);
                tile.SetTriangles(tt, 0);
                tile.RecalculateBounds();
                b.MeshPiece("Island " + ti + "-" + tj, tile, groundMaterial);
                tiles++;
            }

        // 7b. Scatter: what breaks the snow's surface at the height of a walker's eye (60 cm above it). Drift lumps
        //     and sharp-crested ridges lying with the wind, tussocks of dry grass, fallen sticks, stones and pebbles.
        //     They are built into the pack as a few large meshes by district rather than as tens of thousands of
        //     objects; none is solid; all cast shadows, which is most of what shows them at midday.
        {
            var random = new System.Random(77031);
            float Rs(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            int Region(Vector2 p) => Mathf.Clamp((int)((p.y - Z0) / 290f), 0, 9) * 16 + Mathf.Clamp((int)((p.x - X0) / 290f), 0, 15);
            var lumpVerts = new Dictionary<int, List<Vector3>>();
            var lumpTris = new Dictionary<int, List<int>>();
            var lumpRims = new Dictionary<int, List<int>>();      // each drift's outer ring: its normals are the ground's, so no seam shows
            var litter = new Dictionary<int, MeshBuilder>();
            var grass = new Dictionary<int, MeshBuilder>();
            Vector3 On(Vector2 p, float lift) => new Vector3(p.x - root.x, HeightAt(p) + lift - root.y, p.y - root.z);
            Vector2 wind = new Vector2(0.8f, -0.6f), cross = new Vector2(0.6f, 0.8f);
            int lumps = 0, tufts = 0, sticks = 0, stones = 0;
            var straw = new Color(0.86f, 0.74f, 0.42f); var dryStraw = new Color(0.6f, 0.5f, 0.28f);
            var wood = new Color(0.44f, 0.33f, 0.22f); var granite = new Color(0.46f, 0.41f, 0.36f);      // (dark wood in shade reads as a black slit)
            void Stick(MeshBuilder mb, Vector3 from, Vector3 to, float thick)      // a round stick (five sides), tapering
            {
                Vector3 dir = (to - from).normalized;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(u, dir);
                for (int sd = 0; sd < 5; sd++)
                {
                    float t0 = 2f * Mathf.PI * sd / 5f + Mathf.PI * 0.3f, t1 = 2f * Mathf.PI * (sd + 1) / 5f + Mathf.PI * 0.3f;
                    Vector3 d0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);
                    mb.Quad(from + d0 * thick, from + d1 * thick, to + d1 * thick * 0.55f, to + d0 * thick * 0.55f, wood * Rs(0.85f, 1.15f));
                }
            }
            void Stone(MeshBuilder mb, Vector2 at, float radius)
            {
                float tall = radius * Rs(0.5f, 0.9f), spin = Rs(0f, 6.28f);
                Vector3 foot = On(at, -radius * 0.15f), crown = foot + Vector3.up * tall + new Vector3(Rs(-0.3f, 0.3f), 0f, Rs(-0.3f, 0.3f)) * radius;
                Color col = granite * Rs(0.8f, 1.15f);
                var rim = new Vector3[5];
                var shoulder = new Vector3[5];
                for (int sd = 0; sd < 5; sd++)
                {
                    rim[sd] = foot + new Vector3(Mathf.Cos(spin + sd * 1.2566f), 0f, Mathf.Sin(spin + sd * 1.2566f)) * radius * Rs(0.8f, 1.15f);
                    shoulder[sd] = Vector3.Lerp(rim[sd], crown, 0.55f) + Vector3.up * tall * 0.25f;
                }
                for (int sd = 0; sd < 5; sd++)
                {
                    int nxt = (sd + 1) % 5;
                    mb.Quad(rim[sd], rim[nxt], shoulder[nxt], shoulder[sd], col);
                    mb.Tri(shoulder[sd], crown, shoulder[nxt], col);
                }
            }
            for (float z = Z0 + 2f; z < Z1; z += 2f)
                for (float x = X0 + 2f; x < X1; x += 2f)
                {
                    var p = new Vector2(x + Rs(-1f, 1f), z + Rs(-1f, 1f));
                    int k = Cell_(p);
                    float roll = Rs(0f, 1f), roll2 = Rs(0f, 1f);
                    if (!(height[k] > 0.9f) || !Free(p, -1.9f)) continue;
                    float byPath = 0.7f + 2.4f * (1f - Smooth((PathDistance(p) - 3f) / 22f));      // most where walkers look
                    int region = Region(p);
                    float slope = Slope(k);
                    bool onSand = beach[k] > 0.35f && height[k] < 1.1f;
                    // fields of lumps here, smooth snow there
                    float field = Mathf.PerlinNoise(p.x * 0.023f + 51.7f, p.y * 0.023f + 14.2f);
                    if (!onSand && slope < 0.5f && roll < 0.06f + 0.22f * Smooth((field - 0.38f) / 0.3f))
                    {
                        // a drift: a low teardrop, blunt end to the wind, tail trailing downwind; one in three is a
                        // narrow ridge with a sharp crest (sastrugi)
                        bool ridge = random.Next(3) == 0;
                        float length = ridge ? Rs(2.5f, 7f) : Rs(1.2f, 4.6f), width = length * (ridge ? Rs(0.14f, 0.24f) : Rs(0.32f, 0.55f));
                        float tall = Mathf.Min(ridge ? 0.5f : 0.6f, width * (ridge ? Rs(0.4f, 0.6f) : Rs(0.26f, 0.42f)));
                        if (!lumpVerts.TryGetValue(region, out List<Vector3> lv)) { lv = lumpVerts[region] = new List<Vector3>(); lumpTris[region] = new List<int>(); lumpRims[region] = new List<int>(); }
                        List<int> lt = lumpTris[region];
                        int rings = length > 2.6f ? 4 : 3;
                        const int segments = 10;
                        int first = lv.Count;
                        lv.Add(On(p, tall));
                        for (int r = 1; r <= rings; r++)
                            for (int sg = 0; sg < segments; sg++)
                            {
                                float t = (float)r / rings, a = 2f * Mathf.PI * sg / segments;
                                float downwind = Mathf.Cos(a) * length * 0.5f * (Mathf.Cos(a) > 0f ? 1.45f : 0.6f), sideways = Mathf.Sin(a) * width * 0.5f;
                                float wobble = 0.85f + 0.3f * Mathf.PerlinNoise(p.x * 0.7f + Mathf.Cos(a) * 1.3f, p.y * 0.7f + Mathf.Sin(a) * 1.3f);
                                Vector2 q = p + (wind * downwind + cross * sideways) * (t * wobble);
                                float c = Mathf.Max(0f, Mathf.Cos(t * Mathf.PI * 0.5f));      // (never below 0: its square root is taken for a ridge)
                                if (r == rings) lumpRims[region].Add(lv.Count);
                                lv.Add(On(q, tall * (ridge ? c * Mathf.Sqrt(c) : c * c) - (r == rings ? 0.02f : 0f)));
                            }
                        for (int sg = 0; sg < segments; sg++) { lt.Add(first); lt.Add(first + 1 + (sg + 1) % segments); lt.Add(first + 1 + sg); }
                        for (int r = 1; r < rings; r++)
                            for (int sg = 0; sg < segments; sg++)
                            {
                                int a = first + 1 + (r - 1) * segments + sg, bb = first + 1 + (r - 1) * segments + (sg + 1) % segments;
                                int c = a + segments, d = bb + segments;
                                lt.Add(a); lt.Add(bb); lt.Add(c);
                                lt.Add(bb); lt.Add(d); lt.Add(c);
                            }
                        lumps++;
                    }
                    if (!litter.TryGetValue(region, out MeshBuilder mb)) { mb = litter[region] = new MeshBuilder(); grass[region] = new MeshBuilder(); }
                    MeshBuilder gb = grass[region];
                    float patch = Mathf.PerlinNoise(p.x * 0.05f + 5.9f, p.y * 0.05f + 77.3f);
                    // the wrack line: a band of driftwood and pebbles where the last high water left them
                    bool wrack = onSand && height[k] > 0.4f && height[k] < 0.8f;
                    float tuftChance = wrack ? 0.03f : (onSand ? 0.1f : 0.07f + 0.26f * Smooth((patch - 0.48f) / 0.2f) * (height[k] < 45f ? 1f : 0.3f)) * byPath;
                    if (roll2 < tuftChance)
                    {
                        // dry grass: a few tussocks standing through the snow, each a spray of blades
                        int clumps = random.Next(2, 6);
                        for (int c = 0; c < clumps; c++)
                        {
                            Vector2 at = p + new Vector2(Rs(-0.9f, 0.9f), Rs(-0.9f, 0.9f));
                            Vector3 foot = On(at, -0.04f);
                            int blades = random.Next(10, 19);
                            float tallest = Rs(0.14f, 0.75f);
                            for (int bl = 0; bl < blades; bl++)
                            {
                                float a = Rs(0f, Mathf.PI * 2f), lean = Rs(0.15f, 1.3f), tall = tallest * Rs(0.35f, 1f);
                                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                                var edge = new Vector3(-outward.z, 0f, outward.x) * Rs(0.008f, 0.018f);
                                Vector3 root0 = foot + outward * Rs(0f, 0.1f), tip = root0 + Vector3.up * tall + outward * (tall * lean);
                                gb.Tri(root0 - edge, root0 + edge, tip, Color.Lerp(straw, dryStraw, Rs(0f, 1f)));      // drawn from both sides (Cull off)
                            }
                            tufts++;
                        }
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.4f : onSand ? 0.12f : 0.045f * byPath) && slope < 0.3f)
                    {
                        // a fallen stick, half sunk, with a twig or two; its top takes the snow
                        // (each end lies on the ground where it is, so a stick never juts out of a slope or hovers over a dip)
                        float a = Rs(0f, Mathf.PI * 2f), length = Rs(0.4f, 1.4f), thick = Rs(0.012f, 0.03f);
                        var lie = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length * 0.5f;
                        Vector3 a0 = On(p - lie, thick * 0.2f), a1 = On(p + lie, thick * 0.2f + Rs(0f, 0.05f));
                        Vector3 dir = (a1 - a0).normalized;
                        Stick(mb, a0, a1, thick);
                        int twigs = random.Next(0, 3);
                        for (int tw = 0; tw < twigs; tw++)
                        {
                            Vector3 fork = Vector3.Lerp(a0, a1, Rs(0.3f, 0.8f));
                            Vector3 off = Quaternion.Euler(0f, Rs(25f, 60f) * (tw % 2 == 0 ? 1f : -1f), 0f) * dir;
                            Stick(mb, fork, fork + (off + Vector3.up * Rs(0.1f, 0.5f)).normalized * length * Rs(0.2f, 0.4f), thick * 0.5f);
                        }
                        sticks++;
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.55f : onSand ? 0.2f : 0.085f * byPath))
                    {
                        // a stone's top showing, its crown catching snow
                        Stone(mb, p, Rs(0.06f, 0.17f));      // small: anything bigger is the game's own textured rock
                        stones++;
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.85f : onSand ? 0.32f : 0.105f * byPath))
                    {
                        // a patch of pebbles
                        int pebbles = random.Next(5, 12);
                        for (int pb = 0; pb < pebbles; pb++) Stone(mb, p + new Vector2(Rs(-0.7f, 0.7f), Rs(-0.7f, 0.7f)), Rs(0.04f, 0.11f));
                        stones += pebbles;
                    }
                }
            // lumps are all snow: the same shader as roofs and boughs lights them like the ground they lie on
            Material lumpMaterial = b.ShaderMaterial("BWM/Snowy", "Drift", m => { m.SetFloat("_SnowLine", -0.5f); m.SetColor("_Tint", new Color(0.94f, 0.96f, 1f)); });
            Material litterMaterial = b.ShaderMaterial("BWM/Snowy", "Litter", m => { m.SetFloat("_SnowLine", 0.7f); m.SetFloat("_SnowEdge", 0.1f); });
            Material grassMaterial = b.ShaderMaterial("BWM/Snowy", "Dry Grass", m => { m.SetFloat("_SnowLine", 1.5f); m.SetFloat("_Through", 1f); m.SetFloat("_Cull", 0f); });
            foreach (var kv in lumpVerts)
            {
                var lumpMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                lumpMesh.SetVertices(kv.Value);
                lumpMesh.SetTriangles(lumpTris[kv.Key], 0);
                lumpMesh.RecalculateNormals();
                // a drift's edge takes the normal of the ground it runs out into
                Vector3[] lumpNormals = lumpMesh.normals;
                foreach (int rim in lumpRims[kv.Key])
                {
                    Vector3 at = kv.Value[rim];
                    var world = new Vector2(at.x + root.x, at.z + root.z);
                    float hx = HeightAt(world + Vector2.right * 0.75f) - HeightAt(world - Vector2.right * 0.75f), hz = HeightAt(world + Vector2.up * 0.75f) - HeightAt(world - Vector2.up * 0.75f);
                    lumpNormals[rim] = new Vector3(-hx / 1.5f, 1f, -hz / 1.5f).normalized;
                }
                lumpMesh.normals = lumpNormals;
                lumpMesh.RecalculateBounds();
                Piece drifts = b.MeshPiece("Drifts " + kv.Key, lumpMesh, lumpMaterial, false);
                drifts.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;      // they are the ground: no shadow of their own
            }
            foreach (var kv in litter) if (kv.Value.VertexCount > 0) b.MeshPiece("Litter " + kv.Key, kv.Value.ToMesh(), litterMaterial, false);
            foreach (var kv in grass) if (kv.Value.VertexCount > 0) b.MeshPiece("Grass " + kv.Key, kv.Value.ToMesh(), grassMaterial, false);
            Debug.Log("[BWM] Winter Isle scatter: " + lumps + " drifts, " + tufts + " tussocks, " + sticks + " sticks, " + stones + " stones and pebbles, in " + lumpVerts.Count + " districts");
        }

        // 8. Ice: the lake, and the creek above and below it (sheets just over the beds players walk on)
        var ice = new MeshBuilder();
        var iceColor = new Color(0.62f, 0.80f, 0.93f);
        Kit.AddDisc(ice, new Vector3(Lake.x - root.x, LakeLevel - 0.3f - root.y, Lake.y - root.z), LakeRadius + 3f, 0.34f, iceColor, 72);
        for (int s = 0; s + 1 < Creek.Length; s++)
        {
            Vector3 a = Creek[s], c = Creek[s + 1];
            Vector2 dir = new Vector2(c.x - a.x, c.z - a.z).normalized, side = new Vector2(dir.y, -dir.x) * 4.2f;
            Vector3 A(Vector3 q, float sign) => new Vector3(q.x + side.x * sign - root.x, Mathf.Max(q.y, 0.05f) + 0.06f - root.y, q.z + side.y * sign - root.z);
            ice.Quad(A(a, -1f), A(c, -1f), A(c, 1f), A(a, 1f), iceColor);
            ice.Quad(A(a, 1f), A(c, 1f), A(c, -1f), A(a, -1f), iceColor);
        }
        // floes: plates of ice drifting in the bays, thickest close in, each a little tilted to the swell
        {
            var floeRandom = new System.Random(9911);
            float Rf(float lo, float hi) => lo + (hi - lo) * (float)floeRandom.NextDouble();
            int floes = 0;
            for (int tries = 0; tries < 200000 && floes < 900; tries++)
            {
                int k = floeRandom.Next(count);
                if (float.IsNaN(height[k]) || height[k] > -0.2f || beach[k] < 0.25f || inside[k] < -30f) continue;
                if (inside[k] < -10f && Rf(0f, 1f) > 0.3f) continue;      // most of them hug the shore
                Vector2 p = At(k) + new Vector2(Rf(-1.5f, 1.5f), Rf(-1.5f, 1.5f));
                float radius = Rf(0.9f, 3.6f) * Mathf.Lerp(1.2f, 0.6f, Mathf.Clamp01(-inside[k] / 30f));
                Kit.AddPlate(ice, new Vector3(p.x - root.x, -0.16f - root.y, p.y - root.z), radius, 0.22f, Color.Lerp(iceColor, Color.white, Rf(0.2f, 0.7f)), floeRandom.Next(5, 9), tries);
                floes++;
            }
            Debug.Log("[BWM] Winter Isle: " + coveList.Count + " coves, " + barList.Count + " sand bar, " + islets.Count + " islets, " + floes + " ice floes");
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
                float girth = kind == "spruce" ? 0.58f : kind == "pine" ? 0.9f : 1f, clear = kind == "pine" ? 0.5f : 0f;
                Kit.AddFir(near, Vector3.zero, tall, seed, 1f, girth, clear);
                Kit.AddFir(far, Vector3.zero, tall, seed, 0.5f, girth, clear);
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
