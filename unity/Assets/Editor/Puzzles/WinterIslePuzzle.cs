// Winter Isle: the land the winter edition is built on. Not a puzzle (type None).
//
// A snow island in the open sea south-west of the opening area, generated to fill the water between the
// mainland shore and the south-west corner of the game's terrain square, with a causeway to the shore. The
// coastline comes from the sea itself: it keeps a channel's width from the mainland and stays inside the square.
//
// The land has four characters:
//   - shelves in the east and middle: wide level steps 2 m apart joined by gentle ramps, where most puzzles go
//   - the mountain in the west, with a shoulder ridge to its north-east and a headland hill in the south: steep,
//     banded with cliffs, bare rock where it is too steep for snow, sea cliffs where it meets the coast
//   - a ravine cut across the north, too wide to jump
//   - a frozen lake in a hollow in the south-west
// The plaza and every puzzle site are levelled, whatever the land round them does. Paths are routed from the
// plaza to the landing and to every puzzle by the easiest gradient, so they wind round the steep ground.
//
// The build also registers the island's heights as land (so other puzzles can be sited on it) and marks the
// ground no puzzle may take: paths, the plaza, the lake, trees and boulders.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WinterIslePuzzle : PuzzleDef
{
    public override string Name => "WinterIsle";
    public override int PuzzleType => 2;

    // The box of sea the island may fill (world metres), a little inside the terrain square's edge at -1024
    const float X0 = -1016f, X1 = -428f, Z0 = -1016f, Z1 = -560f, Cell = 4f;
    const float Channel = 50f;          // open water kept between the island and the mainland
    const float Skirt = 18f;            // how far the submerged rim runs out from the coast
    static readonly Vector2 ShorePoint = new Vector2(-468f, -700f);     // where the causeway meets the mainland

    public static readonly Vector2 Plaza = new Vector2(-705f, -795f);   // the open middle, where The Tree stands
    public const float PlazaRadius = 24f;

    // The high ground: centre, height it adds, radius
    static readonly (Vector2 at, float height, float radius)[] Peaks =
    {
        (new Vector2(-890f, -770f), 62f, 150f),      // the mountain
        (new Vector2(-815f, -655f), 30f, 105f),      // its shoulder ridge
        (new Vector2(-640f, -962f), 26f, 88f),       // the southern headland
    };
    public static readonly Vector2 Lake = new Vector2(-850f, -938f);    // the frozen lake
    public const float LakeRadius = 30f, LakeLevel = 1f;
    static readonly Vector2 RavineA = new Vector2(-775f, -706f), RavineB = new Vector2(-640f, -698f);
    const float RavineDepth = 9f;

    public override Vector2 Footprint => new Vector2(X1 - X0, Z1 - Z0);
    public override Vector2? FixedSite => new Vector2((X0 + X1) / 2f, (Z0 + Z1) / 2f);

    static readonly Color PackedSnow = new Color(0.8f, 0.85f, 0.93f);

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Level steps with a riser between: 'riser' is the share of each step that is slope (0.5 = gentle ramps, 0.3 = cliff bands)
    static float Terrace(float raw, float step, float riser)
    {
        float t = raw / step, f = Mathf.Floor(t);
        return (f + Smooth((t - f - (1f - riser)) / riser)) * step;
    }

    static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
        return (p - (a + ab * t)).magnitude;
    }

    // Height above sea level at a point 'inside' metres in from the coastline, before any levelling
    static float RawHeight(float x, float z, float inside)
    {
        if (inside <= -Skirt) return float.NaN;
        if (inside <= 0f) return Mathf.Lerp(-9f, 0.5f, Smooth((inside + Skirt) / Skirt));
        var p = new Vector2(x, z);

        // shelves
        float hills = 2f + 12f * Mathf.PerlinNoise(x * 0.004f + 40.2f, z * 0.004f + 17.9f) + 1.5f * Mathf.PerlinNoise(x * 0.015f + 9.1f, z * 0.015f + 61.4f);
        float y = 0.5f + Terrace(Smooth(inside / 70f) * hills, 2f, 0.5f);

        // high ground: each peak a rough cone with a small flat top, partly cut into cliff bands; it rises
        // straight from the coast where it meets it, which makes the sea cliffs
        float high = 0f;
        foreach (var peak in Peaks)
        {
            float f = 1f - Smooth((p - peak.at).magnitude / peak.radius);
            f = Mathf.Min(Mathf.Pow(f, 1.45f), 0.9f) / 0.9f;
            float rough = Mathf.Lerp(0.78f, 1.18f, Mathf.PerlinNoise(x * 0.021f + peak.height, z * 0.021f + peak.radius));
            high += peak.height * f * Mathf.Lerp(rough, 1f, f * f);
        }
        high *= Smooth(inside / 20f);
        y += Mathf.Lerp(high, Terrace(high, 7f, 0.3f), 0.55f);

        // the ravine: a flat floor 8 m wide, walls too steep to climb
        float toRavine = ToSegment(p, RavineA, RavineB);
        if (toRavine < 13f) y = Mathf.Max(0.8f, y - RavineDepth * (1f - Smooth((toRavine - 4f) / 8f)));

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

        // 1. Distance from every grid point to the mainland (terrain above about -1 m), over a padded grid
        const int pad = 24;
        int pw = row + 2 * pad, ph = nz + 1 + 2 * pad;
        var toLand = new float[pw * ph];
        for (int j = 0; j < ph; j++)
            for (int i = 0; i < pw; i++)
                toLand[j * pw + i] = b.Ground.At(X0 + (i - pad) * Cell, Z0 + (j - pad) * Cell, out float y) && y > -1f ? 0f : 1e6f;
        Spread(toLand, pw, ph);

        // 2. Heights above sea level, before levelling
        var height = new float[count];
        for (int k = 0; k < count; k++)
        {
            Vector2 p = At(k);
            float toMainland = toLand[(k / row + pad) * pw + k % row + pad] * Cell - Channel;
            float toEdge = Mathf.Min(Mathf.Min(p.x - X0, X1 - p.x), Mathf.Min(p.y - Z0, Z1 - p.y)) - 22f;
            float inside = Mathf.Min(toMainland, toEdge) + (Mathf.PerlinNoise(p.x * 0.02f + 3.3f, p.y * 0.02f + 8.8f) - 0.5f) * 40f;
            height[k] = RawHeight(p.x, p.y, inside);
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
        for (int k = 0; k < count; k++) { cost[k] = float.MaxValue; from[k] = -1; }
        var open = new SortedSet<(float cost, int cell)>();
        int start = Cell_(Plaza);
        cost[start] = 0f;
        open.Add((0f, start));
        int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            int k = current.cell, i = k % row, j = k / row;
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
                float c = cost[k] + run * (1f + 40f * grade * grade) * lakeside;
                if (c >= cost[nk]) continue;
                open.Remove((cost[nk], nk));
                cost[nk] = c; from[nk] = k;
                open.Add((c, nk));
            }
        }
        var toPath = new float[count];
        for (int k = 0; k < count; k++) toPath[k] = 1e6f;
        int routed = 0;
        var targets = new List<int> { landingCell };
        foreach (Vector2 site in sites) targets.Add(Cell_(site));
        foreach (int target in targets)
        {
            if (from[target] < 0 && target != start) { Debug.LogWarning("[BWM] Winter Isle: no walkable route from the plaza to " + At(target)); continue; }
            for (int k = target; k >= 0; k = from[k]) toPath[k] = 0f;
            routed++;
        }
        Spread(toPath, row, nz + 1);      // now in cells; times Cell for metres
        float PathDistance(Vector2 p) => toPath[Cell_(p)] * Cell;
        bool Free(Vector2 p, float margin)      // clear of paths, the plaza, the lake and every puzzle site
        {
            if (PathDistance(p) < 5f + margin || (p - Plaza).magnitude < PlazaRadius + margin || (p - Lake).magnitude < LakeRadius + 6f + margin) return false;
            foreach (Vector3 site in b.OtherSites)
                if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 3f + margin) return false;
            return true;
        }

        // 5. Mesh: only the cells with all four corners, so nothing shows under water beyond the rim.
        //    Vertex colour red says how fresh the snow is: 1 untouched, 0 on a trodden path.
        var index = new int[count];
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var land = new float[count];
        var blocked = new bool[count];
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
            float pathDistance = Mathf.Min(PathDistance(p), Mathf.Max(0f, (p - Plaza).magnitude - PlazaRadius + 3f));
            float trodden = 1f - Smooth((pathDistance - 1.5f) / 4f);
            colors.Add(new Color(1f - trodden, 1f, 1f, 1f));
            blocked[k] = pathDistance < 7f || (p - Lake).magnitude < LakeRadius + 8f;
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
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        b.MeshPiece("Island", mesh, b.ShaderMaterial("BWM/Snow", "Snow"));

        // 6. The lake's ice: a sheet just above the hollow's floor (the floor is what players stand on)
        var ice = new MeshBuilder();
        Kit.AddDisc(ice, new Vector3(Lake.x - root.x, LakeLevel - 0.3f - root.y, Lake.y - root.z), LakeRadius + 7f, 0.36f, new Color(0.62f, 0.80f, 0.93f), 40);
        b.MeshPiece("Lake Ice", ice.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Ice", m => { m.SetFloat("_SnowLine", 1.02f); m.SetFloat("_SnowNoise", 0.9f); m.SetFloat("_SnowEdge", 0.05f); }), false);

        // 7. Pine groves and boulders. Pines keep to gentle ground below the tree line, in the places a slow noise
        //    picks; boulders gather where the ground is steep. Both keep clear of paths, the plaza, the lake and puzzles.
        var scenery = new MeshBuilder();
        var trunks = new GameObject("Tree Trunks");
        trunks.transform.SetParent(b.Root.transform, false);
        var random = new System.Random(20261224);
        float Rand(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
        void Block(Vector2 p, float radius)
        {
            int r = Mathf.CeilToInt(radius / Cell), centre = Cell_(p);
            for (int oj = -r; oj <= r; oj++)
                for (int oi = -r; oi <= r; oi++)
                {
                    int i = centre % row + oi, j = centre / row + oj;
                    if (i >= 0 && j >= 0 && i <= nx && j <= nz) blocked[j * row + i] = true;
                }
        }
        int trees = 0, rocks = 0;
        for (float z = Z0 + 6f; z < Z1; z += 7f)
            for (float x = X0 + 6f; x < X1; x += 7f)
            {
                var p = new Vector2(x + Rand(-2.8f, 2.8f), z + Rand(-2.8f, 2.8f));
                float keep = Rand(0f, 1f), size = Rand(0f, 1f);
                int k = Cell_(p);
                float y = height[k];
                if (!(y >= 0.9f)) continue;
                float slope = Slope(k);
                float grove = Mathf.PerlinNoise(p.x * 0.013f + 71.3f, p.y * 0.013f + 4.4f);
                float stony = Mathf.PerlinNoise(p.x * 0.021f + 13.7f, p.y * 0.021f + 55.1f);
                var foot = new Vector3(p.x - root.x, y - root.y, p.y - root.z);
                float treeLine = 1f - Smooth((y - 30f) / 14f);      // thinning out above 30 m, none above 44 m
                if (slope < 0.28f && grove > 0.46f && keep < Smooth((grove - 0.46f) / 0.18f) * 0.9f * treeLine && Free(p, 2.5f))
                {
                    float tall = Mathf.Lerp(4.5f, 9.5f, size * size) * Mathf.Lerp(0.7f, 1f, treeLine);
                    Kit.AddPine(scenery, foot, tall, trees + 1);
                    CapsuleCollider trunk = trunks.AddComponent<CapsuleCollider>();
                    trunk.center = foot + Vector3.up * (tall * 0.25f);
                    trunk.radius = Mathf.Max(0.22f, tall * 0.05f);
                    trunk.height = tall * 0.6f;
                    Block(p, 4.5f);
                    trees++;
                }
                else if (slope < 0.9f && keep < (slope > 0.3f ? 0.3f : stony > 0.66f ? 0.22f : 0f) && Free(p, 3f))
                {
                    var bulk = new Vector3(Rand(1.4f, 3.6f), Rand(1.0f, 2.4f), Rand(1.4f, 3.6f)) * (slope > 0.3f ? 1.3f : 1f);
                    Kit.AddRock(scenery, foot, bulk, rocks + 1);
                    SphereCollider stone = trunks.AddComponent<SphereCollider>();
                    stone.center = foot + Vector3.up * (bulk.y * 0.2f);
                    stone.radius = Mathf.Min(bulk.x, bulk.z) * 0.45f;
                    Block(p, 4f);
                    rocks++;
                }
            }
        b.MeshPiece("Trees and Rocks", scenery.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), false);
        b.RegisterLand(X0, Z0, Cell, nx, nz, land, blocked);

        // 8. Causeway from the mainland shore to the landing
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

        b.Empty("Debug Teleport", topAt.x - root.x, topAt.y - root.z, top + 2f - root.y, false);
        Sdk.Set(b.Puzzle, "debugTeleport", b.Root.transform.Find("Debug Teleport"));

        int steep = 0, flat = 0;
        for (int k = 0; k < count; k++)
        {
            if (!(height[k] > 0.5f)) continue;
            float s = Slope(k);
            if (s > 0.7f) steep++; else if (s < 0.06f) flat++;
        }
        Debug.Log("[BWM] Winter Isle: " + trees + " pines, " + rocks + " boulders (" + scenery.VertexCount + " vertices); " + routed + " of " + targets.Count + " paths routed from the plaza");
        Debug.Log("[BWM] Winter Isle: " + Sdk.F(area / 1e6f) + " km2 above the sea, summit " + Sdk.F(top) + " m at " + Sdk.F(topAt.x) + ", " + Sdk.F(topAt.y)
            + "; level ground " + Sdk.F(flat * Cell * Cell / 1e6f) + " km2, cliff " + Sdk.F(steep * Cell * Cell / 1e6f) + " km2; plaza at " + Sdk.F(level[0]) + " m; mesh " + verts.Count + " vertices");
    }
}
