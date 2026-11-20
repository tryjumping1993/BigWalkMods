// Winter Isle: the land the winter edition is built on. Not a puzzle (type None).
//
// A snow island in the open sea south-west of the opening area, generated to fill the water between the
// mainland shore and the south-west corner of the game's terrain square, with a causeway to the shore. The
// shape comes from the sea itself: the coast keeps a channel's width from the mainland and stays inside the
// square. Inland it is terraced: wide level shelves two metres apart with gentle ramps between them, so there
// is flat ground for two dozen puzzles and no slope too steep to walk.
//
// The build also registers the island's heights as land, so other puzzles can be sited on it.
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
    const float PlazaHeight = 8.5f;                                       // one of the island's shelves (they are 2 m apart from 0.5)

    public override Vector2 Footprint => new Vector2(X1 - X0, Z1 - Z0);
    public override Vector2? FixedSite => new Vector2((X0 + X1) / 2f, (Z0 + Z1) / 2f);

    static readonly Color Snow = new Color(0.92f, 0.95f, 1f);
    static readonly Color PackedSnow = new Color(0.8f, 0.85f, 0.93f);

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Level shelves every 2 m, with the upper half of each step a ramp to the next
    static float Terrace(float raw)
    {
        float t = raw / 2f, f = Mathf.Floor(t);
        return (f + Smooth((t - f - 0.5f) * 2f)) * 2f;
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

        // 1. Distance from every grid point to the mainland (terrain above about -1 m), over a padded grid
        const int pad = 24;
        int w = nx + 1 + 2 * pad, h = nz + 1 + 2 * pad;
        var dist = new float[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = X0 + (i - pad) * Cell, z = Z0 + (j - pad) * Cell;
                dist[j * w + i] = b.Ground.At(x, z, out float y) && y > -1f ? 0f : 1e6f;
            }
        const float diag = 1.41421f;
        for (int j = 0; j < h; j++)             // two chamfer passes: forwards, then backwards
            for (int i = 0; i < w; i++)
            {
                float d = dist[j * w + i];
                if (i > 0) d = Mathf.Min(d, dist[j * w + i - 1] + 1f);
                if (j > 0) d = Mathf.Min(d, dist[(j - 1) * w + i] + 1f);
                if (i > 0 && j > 0) d = Mathf.Min(d, dist[(j - 1) * w + i - 1] + diag);
                if (i < w - 1 && j > 0) d = Mathf.Min(d, dist[(j - 1) * w + i + 1] + diag);
                dist[j * w + i] = d;
            }
        for (int j = h - 1; j >= 0; j--)
            for (int i = w - 1; i >= 0; i--)
            {
                float d = dist[j * w + i];
                if (i < w - 1) d = Mathf.Min(d, dist[j * w + i + 1] + 1f);
                if (j < h - 1) d = Mathf.Min(d, dist[(j + 1) * w + i] + 1f);
                if (i < w - 1 && j < h - 1) d = Mathf.Min(d, dist[(j + 1) * w + i + 1] + diag);
                if (i > 0 && j < h - 1) d = Mathf.Min(d, dist[(j + 1) * w + i - 1] + diag);
                dist[j * w + i] = d;
            }

        // 2. Heights above sea level. 'inside' is metres in from the coastline (negative out at sea).
        var height = new float[(nx + 1) * (nz + 1)];
        var land = new float[height.Length];
        float top = float.MinValue; Vector2 topAt = Vector2.zero;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = X0 + i * Cell, z = Z0 + j * Cell;
                float toMainland = dist[(j + pad) * w + i + pad] * Cell - Channel;
                float toEdge = Mathf.Min(Mathf.Min(x - X0, X1 - x), Mathf.Min(z - Z0, Z1 - z)) - 22f;
                float inside = Mathf.Min(toMainland, toEdge) + (Mathf.PerlinNoise(x * 0.02f + 3.3f, z * 0.02f + 8.8f) - 0.5f) * 40f;
                float y;
                if (inside <= -Skirt) y = float.NaN;
                else if (inside <= 0f) y = Mathf.Lerp(-9f, 0.5f, Smooth((inside + Skirt) / Skirt));
                else
                {
                    float hills = 2f + 12f * Mathf.PerlinNoise(x * 0.004f + 40.2f, z * 0.004f + 17.9f) + 1.5f * Mathf.PerlinNoise(x * 0.015f + 9.1f, z * 0.015f + 61.4f);
                    y = 0.5f + Terrace(Smooth(inside / 70f) * hills);
                    float toPlaza = (new Vector2(x, z) - Plaza).magnitude;
                    if (toPlaza < PlazaRadius + 16f) y = Mathf.Lerp(PlazaHeight, y, Smooth((toPlaza - PlazaRadius) / 16f));
                }
                height[j * (nx + 1) + i] = y;
                land[j * (nx + 1) + i] = !float.IsNaN(y) && y > 0.2f ? y : float.NaN;
                if (!float.IsNaN(y) && y > top) { top = y; topAt = new Vector2(x, z); }
            }

        // 3. Where the causeway lands, the plaza in the middle, and the paths: landing to plaza, plaza to every puzzle
        Vector2 landing = Vector2.zero; float best = float.MaxValue;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                if (float.IsNaN(height[j * (nx + 1) + i]) || height[j * (nx + 1) + i] < 0.45f) continue;
                var p = new Vector2(X0 + i * Cell, Z0 + j * Cell);
                float d = (p - ShorePoint).sqrMagnitude;
                if (d < best) { best = d; landing = p; }
            }
        float HeightAt(Vector2 p)      // nearest grid point; NaN off the island
        {
            int i = Mathf.RoundToInt((p.x - X0) / Cell), j = Mathf.RoundToInt((p.y - Z0) / Cell);
            return i < 0 || j < 0 || i > nx || j > nz ? float.NaN : height[j * (nx + 1) + i];
        }
        var paths = new List<(Vector2 a, Vector2 b)> { (landing, Plaza) };
        foreach (Vector3 site in b.OtherSites)
        {
            var at = new Vector2(site.x, site.y);
            if (HeightAt(at) > 0.2f) paths.Add((Plaza, at));      // only the puzzles that are on the island
        }
        float PathDistance(Vector2 p)
        {
            float nearest = float.MaxValue;
            foreach (var s in paths)
            {
                Vector2 ab = s.b - s.a;
                float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                nearest = Mathf.Min(nearest, (p - (s.a + ab * t)).magnitude);
            }
            return nearest;
        }
        bool Free(Vector2 p, float margin)      // clear of paths, the plaza and every puzzle site
        {
            if (PathDistance(p) < 4f + margin || (p - Plaza).magnitude < PlazaRadius + margin) return false;
            foreach (Vector3 site in b.OtherSites)
                if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 3f + margin) return false;
            return true;
        }

        // 4. Mesh: only the cells with all four corners, so nothing shows under water beyond the rim.
        //    Vertex colour red says how fresh the snow is: 1 untouched, 0 on a trodden path.
        var index = new int[height.Length];
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var blocked = new bool[height.Length];
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                int k = j * (nx + 1) + i;
                if (float.IsNaN(height[k])) { index[k] = -1; continue; }
                float x = X0 + i * Cell, z = Z0 + j * Cell;
                index[k] = verts.Count;
                verts.Add(new Vector3(x - root.x, height[k] - root.y, z - root.z));
                uvs.Add(new Vector2(x / 8f, z / 8f));
                var here = new Vector2(x, z);
                float pathDistance = Mathf.Min(PathDistance(here), Mathf.Max(0f, (here - Plaza).magnitude - PlazaRadius + 3f));
                float trodden = 1f - Smooth((pathDistance - 1.2f) / 3.5f);
                colors.Add(new Color(1f - trodden, 1f, 1f, 1f));
                blocked[k] = pathDistance < 7f;
            }
        var tris = new List<int>();
        float area = 0f;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * (nx + 1) + i;
                int a = index[k], bb = index[k + 1], c = index[k + nx + 1], d = index[k + nx + 2];
                if (a < 0 || bb < 0 || c < 0 || d < 0) continue;
                tris.Add(a); tris.Add(c); tris.Add(bb);
                tris.Add(bb); tris.Add(c); tris.Add(d);
                if (height[k] > 0f && height[k + 1] > 0f && height[k + nx + 1] > 0f && height[k + nx + 2] > 0f) area += Cell * Cell;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        b.MeshPiece("Island", mesh, b.ShaderMaterial("BWM/Snow", "Snow"));

        // 5. Pine groves and boulders, in the places a slow noise picks, clear of paths, the plaza, puzzles and ramps
        var scenery = new MeshBuilder();
        var trunks = new GameObject("Tree Trunks");
        trunks.transform.SetParent(b.Root.transform, false);
        var random = new System.Random(20261224);
        float Rand(float a, float c) => a + (c - a) * (float)random.NextDouble();
        void Block(Vector2 p, float radius)
        {
            int r = Mathf.CeilToInt(radius / Cell);
            int ci = Mathf.RoundToInt((p.x - X0) / Cell), cj = Mathf.RoundToInt((p.y - Z0) / Cell);
            for (int dj = -r; dj <= r; dj++)
                for (int di = -r; di <= r; di++)
                {
                    int i = ci + di, j = cj + dj;
                    if (i >= 0 && j >= 0 && i <= nx && j <= nz) blocked[j * (nx + 1) + i] = true;
                }
        }
        int trees = 0, rocks = 0;
        for (float z = Z0 + 6f; z < Z1; z += 7f)
            for (float x = X0 + 6f; x < X1; x += 7f)
            {
                var p = new Vector2(x + Rand(-2.8f, 2.8f), z + Rand(-2.8f, 2.8f));
                float keep = Rand(0f, 1f), size = Rand(0f, 1f);
                float y = HeightAt(p);
                if (float.IsNaN(y) || y < 0.9f) continue;
                // not on a ramp between shelves
                if (Mathf.Abs(HeightAt(p + new Vector2(Cell, 0f)) - HeightAt(p - new Vector2(Cell, 0f))) > 0.5f) continue;
                if (Mathf.Abs(HeightAt(p + new Vector2(0f, Cell)) - HeightAt(p - new Vector2(0f, Cell))) > 0.5f) continue;
                float grove = Mathf.PerlinNoise(p.x * 0.013f + 71.3f, p.y * 0.013f + 4.4f);
                float stony = Mathf.PerlinNoise(p.x * 0.021f + 13.7f, p.y * 0.021f + 55.1f);
                var foot = new Vector3(p.x - root.x, y - root.y, p.y - root.z);
                if (grove > 0.5f && keep < Smooth((grove - 0.5f) / 0.16f) * 0.9f && Free(p, 2.5f))
                {
                    float tall = Mathf.Lerp(4.5f, 9.5f, size * size);
                    Kit.AddPine(scenery, foot, tall, trees + 1);
                    CapsuleCollider trunk = trunks.AddComponent<CapsuleCollider>();
                    trunk.center = foot + Vector3.up * (tall * 0.25f);
                    trunk.radius = Mathf.Max(0.22f, tall * 0.05f);
                    trunk.height = tall * 0.6f;
                    Block(p, 4.5f);
                    trees++;
                }
                else if (stony > 0.66f && keep < 0.22f && Free(p, 3f))
                {
                    var bulk = new Vector3(Rand(1.4f, 3.6f), Rand(1.0f, 2.4f), Rand(1.4f, 3.6f));
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

        // 6. Causeway from the mainland shore to the landing
        Vector2 run = ShorePoint - landing, mid = (ShorePoint + landing) / 2f;
        float yaw = Mathf.Atan2(run.x, run.y) * Mathf.Rad2Deg;
        b.Box("Causeway", mid.x - root.x, mid.y - root.z, new Vector3(7f, 3.2f, run.magnitude + 24f), PackedSnow, -2.2f - root.y, yaw, false);

        Snowfall(b);

        // Christmas lights down both sides of the causeway, a post every 9 m
        Vector2 along = run.normalized, across = new Vector2(along.y, -along.x);
        float span = run.magnitude + 16f;
        for (int side = -1; side <= 1; side += 2)
        {
            var posts = new List<Vector3>();
            for (float d = -8f; d <= span - 8f + 0.01f; d += 9f)
            {
                Vector2 p = landing + along * d + across * (side * 3.1f);
                posts.Add(new Vector3(p.x - root.x, 1f + 2.4f - root.y, p.y - root.z));
            }
            Kit.LightString(b, side < 0 ? "Causeway Lights Left" : "Causeway Lights Right", posts, 0.35f, 0.5f, false, false, side + 3);
            foreach (Vector3 post in posts)
                b.Box("Light Post", post.x, post.z, new Vector3(0.12f, 2.9f, 0.12f), new Color(0.36f, 0.25f, 0.17f), 0.5f - root.y, 0f, false);
        }

        b.Empty("Debug Teleport", topAt.x - root.x, topAt.y - root.z, top + 2f - root.y, false);
        Sdk.Set(b.Puzzle, "debugTeleport", b.Root.transform.Find("Debug Teleport"));

        Debug.Log("[BWM] Winter Isle: " + trees + " pines, " + rocks + " boulders (" + scenery.VertexCount + " vertices), " + paths.Count + " paths from the plaza at " + Plaza);
        Debug.Log("[BWM] Winter Isle: " + Sdk.F(area / 1e6f) + " km2 above the sea, summit " + Sdk.F(top) + " m at " + Sdk.F(topAt.x) + ", " + Sdk.F(topAt.y)
            + "; causeway " + Sdk.F(run.magnitude) + " m from " + Sdk.F(landing.x) + ", " + Sdk.F(landing.y) + "; mesh " + verts.Count + " vertices");
    }
}
