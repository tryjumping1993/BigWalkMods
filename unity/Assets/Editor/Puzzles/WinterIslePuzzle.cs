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
    const float X0 = -3120f, X1 = -360f, Z0 = -2700f, Z1 = -501f, Cell = 3f;      // (grown west and south for the great mountain; still a whole number of cells from the old corner)
    // The island's outline: two overlapping ovals (west and east), roughened by coves; the east one reaches the
    // causeway and is cut back where it would come within a channel's width of the mainland
    static readonly (Vector2 centre, Vector2 radius)[] Ovals = { (new Vector2(-1230f, -1060f), new Vector2(620f, 420f)), (new Vector2(-740f, -870f), new Vector2(295f, 320f)) };
    const float Channel = 50f;          // open water kept between the island and the mainland
    const float Skirt = 24f;            // how far the submerged rim runs out from the coast
    static readonly Vector2 ShorePoint = new Vector2(-468f, -700f);     // where the causeway meets the mainland

    public static readonly Vector2 Plaza = new Vector2(-705f, -795f);   // the open middle, where The Tree stands
    public const float PlazaRadius = 24f;

    // The summit: a level sanctuary ringed by crags, with no trail; the gondola is the only way up. The Tree's
    // twenty summit plinths stand on it (TheTreePuzzle).
    public static Vector2 Summit => Peak;      // the great mountain's top (see Peak below)
    public const float SummitRadius = 21f;
    public const float SummitLiftReach = 27f;      // the summit lift's top station, from the summit
    // The gondola: from a station just west of the plaza straight up to the summit's eastern edge
    static Vector2 GondolaDir => (Summit - Plaza).normalized;
    public static Vector2 GondolaBottom => Plaza + GondolaDir * 55f;
    public static Vector2 GondolaTop => Summit - GondolaDir * 12f;
    // The ski lift: a chairlift from beside the lodge at the pistes' foot up to the bench at the tree line, between the
    // middle and right-hand runs, so a skier rides back up instead of walking to the plaza. Winter Walk runs it
    // (GondolaDriver.ConfigureLift) and opens it with the gondola.
    public const int SkiLiftChairs = 26;
    public const float SkiLiftSide = 1.6f;
    public static Vector2 SkiLiftBottom
    {
        get
        {
            Vector2 finish = PisteFinish(out Vector2 down, out float half);
            return finish + new Vector2(down.y, -down.x) * (Mathf.Min(half, 24f) + 44f) - down * 30f;
        }
    }
    public static Vector2 SkiLiftTop
    {
        get
        {
            const float along = 478f;
            float angle = SkiBearing - (RunOffset(1, along) + RunOffset(2, along)) * 0.5f;
            return Peak + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * along;
        }
    }
    public const float GondolaSide = 2f;          // each line runs this far either side of the centre
    public const int GondolaCabins = 16;

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

    // The great mountain, south-west of the old island: three times the height of the base game's highest point
    // (about 185 m). Three sides are steep and craggy. The fourth, facing east-north-east towards the old island,
    // is the ski face: a broad, even apron falling about 21 degrees for a kilometre and a half to the shore
    // below the lake, kept clear of rocks and trees. The top is a small level summit.
    public static readonly Vector2 Peak = new Vector2(-2250f, -1900f);
    public const float PeakHeight = 580f, SummitFlat = 26f;
    const float SteepRadius = 650f, SkiRadius = 1450f, SkiBearing = 0.32f, SkiHalfWidth = 0.95f;      // radians: the bearing from the peak, and the face's half-width
    // 1 on the middle of the ski face, 0 off it
    public static float SkiFace(Vector2 p)
    {
        Vector2 d = p - Peak;
        float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, SkiBearing * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
        off += (Mathf.PerlinNoise(d.magnitude * 0.0065f, 1.3f + (d.x * d.y > 0f ? 0f : 5f)) - 0.5f) * 0.24f;      // the face's sides are buttresses and bays, not one straight wall
        return 1f - Smooth((off - SkiHalfWidth * 0.5f) / (SkiHalfWidth * 0.5f));
    }
    // Cirques on the steep sides: (bearing from the peak in radians, distance to the bowl's middle, radius, floor height)
    static readonly (float bearing, float distance, float radius, float floor)[] Cirques =
    {
        (1.75f, 300f, 210f, 195f),      // north, between the ski face and the northern ridge
        (3.1f, 285f, 190f, 220f),       // west, under the long ridge
        (4.3f, 280f, 180f, 200f),       // south-west, between the knob and the southern spur
    };
    // Three lesser peaks on the steep sides, joined to the main one by saddles: (offset from the peak, height, radius).
    // Well below the summit (a fifth to three fifths lower), so the main peak stands alone above them
    static readonly (Vector2 off, float height, float radius)[] LesserPeaks =
    {
        (new Vector2(-330f, 215f), 360f, 400f),      // a long shoulder lying across the north-west
        (new Vector2(95f, -430f), 470f, 350f),       // the second summit, four fifths of the main one, across a deep col to the south
        (new Vector2(-470f, -250f), 220f, 300f),     // a low knob to the south-west
    };
    // how far below the lesser top the saddle between it and the main summit lies (a share of the lesser top's height)
    static readonly float[] SaddleDrop = { 0.2f, 0.3f, 0.2f };
    // Couloirs down the steep sides: (bearing from the peak in radians, where it ends, how deep it is cut). They wander
    // a little and each ends in a fan of snow and fallen rock at its foot; between them the sides stand as buttresses.
    // Spaced unevenly, so no two faces alike
    static readonly (float bearing, float end, float depth)[] Couloirs =
    {
        (1.42f, 560f, 40f), (1.86f, 470f, 30f), (2.62f, 600f, 46f), (2.98f, 430f, 26f), (3.55f, 540f, 38f),
        (4.12f, 500f, 34f), (4.62f, 420f, 28f), (5.08f, 610f, 44f), (5.52f, 470f, 30f),
    };
    // The ski face is not one plane: two rocky ribs run down it from under the summit, wandering, and split it into
    // three runs. 1 on a rib, 0 on open piste (and off the ski face).
    public static float Rib(Vector2 p)
    {
        Vector2 d = p - Peak;
        float along = d.magnitude;
        if (along < 540f || along > SkiRadius - 120f) return 0f;      // none in the open bowl: rock there stood as a wall across its lip
        float off = Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, SkiBearing * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        off += (Mathf.PerlinNoise(along * 0.004f + 7.1f, 3.3f) - 0.5f) * 0.3f;      // the ribs wander as they descend
        float rib = Mathf.Max(1f - Smooth(Mathf.Abs(off - 0.27f) / 0.075f), 1f - Smooth(Mathf.Abs(off + 0.25f) / 0.065f));
        // broken into lengths, with gaps where one run crosses to the next
        return rib * Smooth((Mathf.PerlinNoise(along * 0.006f + 1.7f, off > 0f ? 9.9f : 4.4f) - 0.38f) / 0.12f) * Smooth((along - 540f) / 80f);
    }
    // The pistes: above the tree line the whole upper face is open snow; below it three runs wind down through
    // the forest, each a cleared ribbon 50 to 70 m wide, swinging from side to side and meeting at the bottom.
    // 1 on a piste, 0 off it.
    public const float TreeLine = 330f;      // metres above the sea, on average
    // ...and here: the line rises and falls by up to 75 m across the mountain, so the forest does not stop at one height
    public static float TreeLineAt(Vector2 p) => TreeLine + 75f * (Mathf.PerlinNoise(p.x * 0.0045f + 21.7f, p.y * 0.0045f + 3.3f) - 0.5f) * 2f;
    public static float Piste(Vector2 p) => Piste(p, true);
    // ...only the runs themselves, where the snow is groomed (the open bowl round them is not)
    public static float Groomed(Vector2 p) => Piste(p, false);
    static float Piste(Vector2 p, bool withBowl)
    {
        float ski = SkiFace(p);
        if (ski <= 0f) return 0f;
        Vector2 d = p - Peak;
        float along = d.magnitude;
        float off = Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, SkiBearing * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float open = withBowl ? 1f - Smooth((along - 380f) / 140f) : 0f;      // the bowl under the summit, all open
        float best = 0f;
        for (int run = 0; run < 3; run++)
        {
            float centre = RunOffset(run, along);
            float across = Mathf.Abs(off - centre) * along;      // metres from the run's middle
            float half = RunHalf(run, along);
            best = Mathf.Max(best, 1f - Smooth((across - half) / 6f));
        }
        best = Mathf.Max(best, CatTrack(along, off));
        return ski * Mathf.Max(open * (1f - Rib(p)), best);
    }
    // A cat track: a narrow groomed lane cut level across the forest from the left run to the right one, where the
    // middle pitch eases into the lower one (1 on it). It runs along a contour, so it is level, and cuts through the
    // spurs between the runs in a notch.
    public const float CatTrackAlong = 925f;
    public const float HalfPipeAlong = 1170f;      // where the terrain park's half-pipe begins, on the right-hand run
    static float CatTrack(float along, float off)
    {
        float lane = 1f - Smooth((Mathf.Abs(along - CatTrackAlong) - 3.5f) / 2.5f);
        if (lane <= 0f) return 0f;
        float left = RunOffset(0, along), right = RunOffset(2, along);
        return lane * Smooth((off - left) / 0.02f) * Smooth((right - off) / 0.02f);
    }
    // The middle of run 0, 1 or 2 at a distance from the peak, and its half-width there (the same sums as Piste)
    // The finish arch at the foot of the middle run: its middle, the way the run goes there, and the run's half-width
    public static Vector2 PisteFinish(out Vector2 direction, out float half)
    {
        Vector2 mid = RunCentre(1, SkiRadius - 110f, out half);
        direction = (RunCentre(1, SkiRadius - 104f, out _) - mid).normalized;
        return mid;
    }
    // A run's line across the face (radians off the face's bearing) and its half-width, at a distance from the peak:
    // two to four real bends of 15 to 30 degrees on the way down, the side runs drawing in over the last 600 m and
    // joining the middle one's line, which straightens for the run-out; wide on open pitches and narrow in places,
    // by a factor of two or three (the mountain-shots.sh camera follows run 1: keep its swing() in step)
    static float RunSwing(int run, float along) => 0.05f * Mathf.Sin(along / (run == 1 ? 115f : 100f + run * 12f) + run * 2.1f) * Smooth((along - 300f) / 200f);
    // (the two side runs join it at different places: the left one half-way down, the right one near the foot; joined
    // at the same place they drew a heart round the forest between)
    static float RunOffset(int run, float along)
    {
        float main = RunSwing(1, along) * (1f - Smooth((along - (SkiRadius - 230f)) / 130f));
        if (run == 1) return main;
        float join = run == 0 ? Smooth((1010f - along) / 380f) : Smooth((SkiRadius - 110f - along) / 420f);
        return (run == 0 ? -0.3f : 0.36f) * join + Mathf.Lerp(main, RunSwing(run, along), join);
    }
    // narrow throats and wide pitches: from a third to one and a half times the run's usual width
    static float RunHalf(int run, float along) => (run == 1 ? 20f : 14f) * (0.35f + 1.2f * Mathf.PerlinNoise(along * 0.0065f + run * 3.1f, 0.5f))
        + 8f * Smooth((along - (SkiRadius - 330f)) / 170f);
    public static Vector2 RunCentre(int run, float along, out float half)
    {
        float centre = RunOffset(run, along);
        half = RunHalf(run, along);
        float angle = SkiBearing - centre;      // Piste measures 'off' as bearing minus angle
        return Peak + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * along;
    }
    // 1 where the ski face is forest: below the tree line, off the pistes
    public static float SkiForest(Vector2 p, float height)
    {
        // the forest reaches higher on the spurs between the runs (tongues up the ridges) than beside them
        float along = (p - Peak).magnitude, clear = float.MaxValue;
        for (int run = 0; run < 3; run++) { Vector2 c = RunCentre(run, along, out float half); clear = Mathf.Min(clear, (p - c).magnitude - half); }
        float line = TreeLineAt(p) + 60f * Smooth((clear - 25f) / 45f) - 30f;
        float stand = 1f - Smooth((height - line + 120f) / 120f);
        float stragglers = 0.14f * (1f - Smooth((height - line - 10f) / 100f)) * Smooth((height - line + 40f) / 40f);      // a few stunted trees well above the edge
        return SkiFace(p) * (1f - Smooth((Piste(p) - 0.1f) / 0.3f)) * Mathf.Max(stand, stragglers) * Smooth((height - 6f) / 10f);
    }      // a hard edge at a piste, thinning out over the last 150 m below the tree line, and stragglers above it
    // The ski face's height, as a share of the peak's, at u from the summit (0) to the foot (1). Stepped, as a real
    // ski mountain is: under the summit a short slope and a band of rock, a headwall, a broad bowl, a bench at the
    // trees, a pitch through them, a long easier pitch and a flat run-out. Concave as a whole (each pitch gentler than
    // the one above it, the bench aside), so from the top the runs are seen dropping away to the foot. Each step is (metres out from the
    // summit's edge, slope in degrees, metres over which the slope changes to it); the drops are summed in a table
    // and scaled to the peak's height. 'gap' is the line the middle run takes through the band: there the band and the
    // headwall are one steep ramp, so a skier can get down from the top
    static readonly (float at, float degrees, float ramp)[] SkiSteps =
    {
        (0f, 28f, 0f), (35f, 50f, 6f), (60f, 36f, 6f), (150f, 27f, 14f), (420f, 13f, 14f), (470f, 24f, 14f), (900f, 15f, 20f), (1320f, 6f, 20f),
    };
    const float BandTop = 35f, BandFoot = 150f;      // where the gap's ramp begins and ends
    static float[] skiTable;
    static float SkiTable(float metres)
    {
        if (skiTable == null)
        {
            int n = Mathf.CeilToInt(SkiRadius) + 2;
            var drop = new float[n];
            float total = 0f;
            for (int m = 0; m < n - 1; m++)
            {
                float at = m + 0.5f, degrees = SkiSteps[0].degrees;
                for (int g = 1; g < SkiSteps.Length; g++)
                    degrees += (SkiSteps[g].degrees - SkiSteps[g - 1].degrees) * Smooth((at - SkiSteps[g].at + SkiSteps[g].ramp * 0.5f) / SkiSteps[g].ramp);
                if (m < SkiRadius) total += Mathf.Tan(degrees * Mathf.Deg2Rad);
                drop[m + 1] = total;
            }
            skiTable = new float[n];
            for (int m = 0; m < n; m++) skiTable[m] = Mathf.Max(0f, 1f - drop[m] / total);
        }
        float f = Mathf.Clamp(metres, 0f, skiTable.Length - 1.001f);
        int i = (int)f;
        return Mathf.Lerp(skiTable[i], skiTable[i + 1], f - i);
    }
    static float SkiProfile(float u, float gap = 0f)
    {
        float metres = Mathf.Clamp01(u) * SkiRadius;
        float share = SkiTable(metres);
        if (gap > 0f && metres > BandTop && metres < BandFoot)
            share = Mathf.Lerp(share, Mathf.Lerp(SkiTable(BandTop), SkiTable(BandFoot), (metres - BandTop) / (BandFoot - BandTop)), gap);
        return share;
    }
    static float MountainRadius(Vector2 p) => Mathf.Lerp(SteepRadius, SkiRadius, SkiFace(p));
    // how far the mountain's ground reaches at p (for the island's outline): the main cone or any lesser peak
    static float MountainReach(Vector2 p)
    {
        float reach = MountainRadius(p) - (p - Peak).magnitude;
        foreach (var lesser in LesserPeaks) reach = Mathf.Max(reach, lesser.radius - (p - Peak - lesser.off).magnitude);
        return reach;
    }
    static float SoftMax(float a, float b, float k) { float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k); return Mathf.Lerp(b, a, h) + k * h * (1f - h); }
    static float MountainHeight(Vector2 p)
    {
        float ski = SkiFace(p);
        // off the ski face the whole mass is pushed about by a slow noise, so no contour is a circle
        Vector2 q = p + new Vector2(Mathf.PerlinNoise(p.x * 0.0021f + 5.5f, p.y * 0.0021f + 1.1f) - 0.5f, Mathf.PerlinNoise(p.x * 0.0021f + 9.2f, p.y * 0.0021f + 6.6f) - 0.5f) * (230f * (1f - ski) * Smooth((p - Peak).magnitude / 260f));      // and not at the top, so the summit stays where Peak says
        Vector2 dq = q - Peak;
        // the top is a short crest across the head of the ski face, not a point: its middle is the summit, its ends
        // a little lower, with a notch or two
        var crestDir = new Vector2(-Mathf.Sin(SkiBearing), Mathf.Cos(SkiBearing));
        float alongCrest = Mathf.Clamp(Vector2.Dot(dq, crestDir), -95f, 85f);
        Vector2 dqc = dq - crestDir * alongCrest;
        float crestDrop = 26f * (alongCrest / 90f) * (alongCrest / 90f) + 9f * Mathf.Abs(Mathf.Sin(alongCrest * 0.07f + 1.1f)) * Smooth(Mathf.Abs(alongCrest) / 30f);
        float d = Mathf.Max(0f, dqc.magnitude - SummitFlat), radius = MountainRadius(p);
        float t = Mathf.Clamp01(1f - d / radius);
        float h = (PeakHeight - crestDrop) * Mathf.Pow(t, 1.45f);
        float skiOff = Mathf.DeltaAngle(Mathf.Atan2(p.y - Peak.y, p.x - Peak.x) * Mathf.Rad2Deg, SkiBearing * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        if (ski > 0f)
        {
            // the ski face is not one plane: a headwall under the summit, an upper pitch, a bench, the long middle
            // pitch and a gentle run-out (SkiProfile), the steps a little higher or lower across the face
            // (the steps a little higher or lower across the face, so no step is a straight line)
            float u = Mathf.Clamp01(d / SkiRadius + 0.02f * Mathf.Sin(skiOff * 5f + 1f) * Mathf.Sin(Mathf.Clamp01(d / SkiRadius) * Mathf.PI));
            float alongFace = dq.magnitude;
            float gap = 1f - Smooth((Mathf.Abs(skiOff * alongFace) - 16f) / 12f);      // the middle run's way through the rock band
            float skiH = PeakHeight * SkiProfile(u, gap);
            // rollers: low swells across the fall line every 70 to 100 m, a metre or two high, on the bowl and the
            // pitches (not on the bench or the run-out, which stay gentle)
            float benchHere = Smooth((alongFace - 440f) / 20f) * (1f - Smooth((alongFace - 500f) / 20f));
            skiH += 1.0f * Mathf.Sin(alongFace * (6.2831853f / 85f) + (Mathf.PerlinNoise(alongFace * 0.004f, skiOff * 2f + 3f) - 0.5f) * 4f)
                * Smooth((alongFace - 160f) / 60f) * (1f - Smooth((alongFace - 1290f) / 60f)) * (1f - 0.85f * benchHere)
                * (0.6f + 0.8f * Mathf.PerlinNoise(skiOff * 4f + 1.7f, alongFace * 0.003f));
            // the bowl's arms: a rocky ridge on each side, running out from under the summit and dying away into the
            // forest's spurs, so the bowl is a basin held between walls (the northern arm the higher, 110 m over the
            // bowl's floor at its highest, the southern 80), with notches along their crests
            // (they run on down beside the outer runs as forested ridges, lower as they go, so from the runs there is
            // always a ridge standing over the trees on either side)
            float armRise = Smooth((alongFace - 50f) / 110f) * (1f - Smooth((alongFace - 950f) / 320f)) * (1f - 0.55f * Smooth((alongFace - 450f) / 450f));
            skiH += 110f * Mathf.Exp(-Mathf.Pow((skiOff + 0.55f) / 0.11f, 2f)) * armRise * (0.72f + 0.5f * Mathf.PerlinNoise(alongFace * 0.011f, 4.2f));
            skiH += 80f * Mathf.Exp(-Mathf.Pow((skiOff - 0.57f) / 0.1f, 2f)) * armRise * (0.72f + 0.5f * Mathf.PerlinNoise(alongFace * 0.011f, 9.6f));
            h = Mathf.Lerp(h, skiH, ski);
        }
        // aretes and cirques: five spurs run out from the summit with bowls hollowed between them (steep sides only)
        float angle = Mathf.Atan2(dq.y, dq.x);
        float spur = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(angle * 5f + 1.3f + (Mathf.PerlinNoise(q.x * 0.003f, q.y * 0.003f) - 0.5f) * 3f), 2.2f);
        float carve = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);      // most half-way down, none at the top or the foot
        h *= 1f - (1f - spur) * 0.16f * carve * (1f - ski);      // (gentle: five even aretes read as a pattern)
        // couloirs and buttresses: a few deep gullies cut down each steep side, converging on the summit, each ending in
        // a fan of snow and fallen rock where it meets the gentler ground
        if (ski < 1f && h > 40f)
        {
            float r = dq.magnitude;
            foreach (var couloir in Couloirs)
            {
                float wander = couloir.bearing + 0.07f * Mathf.Sin(r / 95f + couloir.bearing * 3f);
                float lateral = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, wander * Mathf.Rad2Deg) * Mathf.Deg2Rad * r;
                float width = 8f + r * 0.045f;
                if (Mathf.Abs(lateral) > width * 4f) continue;
                float cut = couloir.depth * Smooth((r - 70f) / 150f) * (1f - Smooth((r - couloir.end) / 80f));
                h -= cut * Mathf.Exp(-Mathf.Pow(lateral / width, 2f)) * (1f - ski);
                float fan = 20f * Mathf.Exp(-Mathf.Pow(lateral / (width * 2.6f), 2f)) * Mathf.Exp(-Mathf.Pow((r - couloir.end - 60f) / 75f, 2f));
                h += fan * (1f - ski) * Smooth(h / 60f);
            }
        }
        // cirques: three great bowls bitten out of the steep sides between the lesser peaks, each with a level floor
        // and walls rising steeply to the ridges round it, so the ridges between are aretes
        foreach (var cirque in Cirques)
        {
            Vector2 at = Peak + new Vector2(Mathf.Cos(cirque.bearing), Mathf.Sin(cirque.bearing)) * cirque.distance;
            float cd = (q - at).magnitude;
            if (cd < cirque.radius * 1.4f)
            {
                float bowl = cirque.floor + Mathf.Pow(cd / cirque.radius, 2.4f) * 420f + (Mathf.PerlinNoise(q.x * 0.02f, q.y * 0.02f) - 0.5f) * 18f;
                h = Mathf.Lerp(h, Mathf.Min(h, bowl), 1f - ski);
            }
        }
        // the lesser peaks, melted into the main one, each its own shape: a level ridge, a rounded dome, a blunt knob
        // with one steep face (three alike read as three cones)
        for (int lp = 0; lp < LesserPeaks.Length; lp++)
        {
            var lesser = LesserPeaks[lp];
            Vector2 lrel = q - Peak - lesser.off;
            Vector2 outward = lesser.off.normalized, across = new Vector2(-outward.y, outward.x);
            float ld;
            if (lp == 0) ld = new Vector2(Vector2.Dot(lrel, outward), Vector2.Dot(lrel, across) / 2.3f).magnitude;      // a ridge lying across
            else if (lp == 2) ld = new Vector2(Vector2.Dot(lrel, outward) * (Vector2.Dot(lrel, outward) > 0f ? 1.8f : 0.85f), Vector2.Dot(lrel, across)).magnitude;      // steep on its outer side
            else ld = lrel.magnitude;
            float x = Mathf.Clamp01(ld / lesser.radius);
            float shape = lp == 1 ? Mathf.Pow(1f - x, 1.3f) * (1f - 0.06f * Mathf.Exp(-x * 14f))      // a peak with a small rocky top
                : lp == 0 ? 1f - Mathf.Pow(x, 0.85f) * (0.75f + 0.25f * x)        // a ridge with a broad top
                : Mathf.Pow(1f - x, 1.1f) * (1f - 0.12f * Mathf.Exp(-x * 9f));     // a knob, its top blunted
            float lesserH = lesser.height * shape * (1f - ski), lesserSoft = 45f * Smooth(lesserH / 60f);
            if (x < 1f) h = lesserSoft > 0.01f ? SoftMax(h, lesserH, lesserSoft) : Mathf.Max(h, lesserH);
            // ...and a ridge from the main summit out to it, its saddle a fifth lower than the lesser top, so the
            // three are one massif and not three cones
            Vector2 rel = q - Peak;
            float f = Mathf.Clamp01(Vector2.Dot(rel, lesser.off) / lesser.off.sqrMagnitude);
            float beside = (rel - lesser.off * f).magnitude;
            float crest = lesser.height * (1f - SaddleDrop[lp] * Mathf.Sin(f * Mathf.PI)) + (PeakHeight - lesser.height) * (1f - f) * (1f - f);
            crest += (Mathf.PerlinNoise(f * 7f + lesser.height, 3.7f) - 0.5f) * 56f * Mathf.Sin(f * Mathf.PI);      // a broken crest: notches and knobs
            // (a soft maximum adds a quarter of its width where the two are equal, so its width shrinks to nothing as
            // the ridge's height goes to nothing: otherwise it lifted all the low ground round it by several metres)
            float ridgeH = (crest - beside * 1.15f) * (1f - ski);
            float ridgeSoft = 30f * Smooth(ridgeH / 40f);
            h = ridgeSoft > 0.01f ? SoftMax(h, ridgeH, ridgeSoft) : Mathf.Max(h, ridgeH);
        }
        if (h <= 0.5f) return Mathf.Max(0f, h);
        // crags and spurs on the steep sides; the ski face only rolls a little
        // (big buttresses and couloirs; little of the finer ridged noise, which on a steep face ran down it in flutes)
        // (less of it than before: the couloirs carry the structure now, and finer ridged noise ran down the sides in
        // evenly spaced stripes)
        float rough = (Ridged(p.x * 0.006f + 11.3f, p.y * 0.006f + 4.9f) - 0.4f) * 42f + (Ridged(p.x * 0.017f + 2.1f, p.y * 0.017f + 7.7f) - 0.45f) * 4f + (Ridged(p.x * 0.05f + 6.3f, p.y * 0.05f + 1.2f) - 0.5f) * 1.5f;
        float roll = (Mathf.PerlinNoise(p.x * 0.004f + 3.3f, p.y * 0.004f + 8.8f) - 0.5f) * 6f;      // long rolls only (some cross-slope)
        float body = Smooth(h / 60f) * Smooth((p - Peak).magnitude / 150f);      // none at the foot or on the summit
        float rib = Rib(p) * Smooth((h - TreeLine + 40f) / 60f);      // rock ribs only above the trees; below, the face is even ground under forest
        // below the bowl each run lies in its own shallow valley: the forest between stands on spurs some metres higher
        if (ski > 0f)
        {
            float alongSpur = (p - Peak).magnitude;
            // (from the distance to the nearest run's middle, so the spur's sides are long gentle slopes under trees,
            // never a step at a piste's edge)
            float clearOfRuns = float.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                Vector2 c = RunCentre(run, alongSpur, out float half);
                clearOfRuns = Mathf.Min(clearOfRuns, (p - c).magnitude - half);
            }
            float spurH = 18f * Smooth((clearOfRuns - 8f) / 55f) * (0.6f + 0.8f * Mathf.PerlinNoise(p.x * 0.006f + 3.1f, p.y * 0.006f + 7.9f));
            spurH *= 1f - (1f - Smooth((Mathf.Abs(alongSpur - CatTrackAlong) - 6f) / 14f));      // the cat track cuts through in a notch
            h += ski * spurH * Smooth((alongSpur - 480f) / 160f) * (1f - Smooth((alongSpur - 1250f) / 120f));
        }
        float berm = 0f;      // (a bank along each piste's edge showed bare and drew a brown outline round every run)      // a low bank along each piste's edge
        return Mathf.Max(0f, h + Mathf.Lerp(rough, roll, ski * (1f - rib)) * body + rib * ski * 16f * body + berm);
    }

    static readonly List<(Vector2 at, float radius, float height)> knolls = new List<(Vector2, float, float)>();

    public override Vector2 Footprint => new Vector2(X1 - X0, Z1 - Z0);
    // The pack's root stays where it was first placed (it must be over the game's terrain square, which the island
    // now runs well past); everything is built relative to it
    public override Vector2? FixedSite => new Vector2(-869f, -909.5f);
    // Where puzzles stood before they were spread out (x, z, radius): kept as coast guards only, so the coast that grew
    // round them stays as it is (without them a new inlet would reach the railway near -828, -1502)
    static readonly Vector3[] FormerSites =
    {
        new Vector3(-890f, -650f, 14.87f), new Vector3(-890f, -1520f, 18.38f), new Vector3(-1070f, -2030f, 19.85f), new Vector3(-920f, -1220f, 16.97f),
        new Vector3(-500f, -860f, 17.69f), new Vector3(-605f, -816f, 16.28f), new Vector3(-761f, -890f, 21.21f), new Vector3(-520f, -960f, 21.21f),
        new Vector3(-583f, -756f, 13.89f), new Vector3(-640f, -770f, 11.40f), new Vector3(-520f, -790f, 14.87f), new Vector3(-780f, -800f, 19.80f),
        new Vector3(-710f, -1250f, 21.93f), new Vector3(-1777.426f, -1573.016f, 7.07f),
    };

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
        float underMountain = 1f - Smooth(MountainHeight(p) / 30f);      // (on the mountain the lowland's hummocks made dunes of the ski face)
        // little stream gullies winding through the lowland: the lines where a slow noise crosses its middle value,
        // cut a couple of metres deep with soft banks, a branch network from a second noise
        float g1 = Mathf.Abs(Mathf.PerlinNoise(x * 0.0055f + 61.1f, z * 0.0055f + 13.7f) - 0.5f), g2 = Mathf.Abs(Mathf.PerlinNoise(x * 0.011f + 7.3f, z * 0.011f + 88.8f) - 0.5f);
        float gully = Mathf.Max(1f - Smooth(g1 / 0.075f), 0.6f * (1f - Smooth(g2 / 0.055f)));      // (wide and shallow: narrow ones made V-trenches on the 3 m grid)
        gully = gully * gully * (3f - 2f * gully);
        lowland -= gully * 1.6f * Smooth(inside / 40f) * underMountain;
        lowland += (Mathf.Max(ridgelets, -1.5f) + hummocks - dells) * Smooth(inside / 45f) * underMountain + fine * Smooth(inside / 8f);
        // in places the lowland is benched: level shelves a couple of metres apart with a bank between
        float benched = Smooth((Mathf.PerlinNoise(x * 0.0065f + 91.7f, z * 0.0065f + 33.1f) - 0.5f) / 0.12f) * Smooth(inside / 40f);
        if (benched > 0f) lowland = Mathf.Lerp(lowland, Terrace(lowland, 2.4f, 0.25f), benched);
        float beachY = 0.35f + inside * 0.035f + fine * 0.3f;
        float y = Mathf.Lerp(lowland, Mathf.Min(lowland, beachY), beach * (1f - Smooth((inside - 14f) / 24f)));

        // the mountains: ridges, crested with crags, cut into cliff bands; straight from the sea where they meet it
        float high = RidgeHeight(p);
        float mountain = Mathf.Clamp01(high / 30f);
        high += (Ridged(x * 0.022f + 5.1f, z * 0.022f + 8.3f) - 0.45f) * 14f * mountain + (Ridged(x * 0.06f + 1.7f, z * 0.06f + 3.9f) - 0.5f) * 4f * mountain;
        high = Mathf.Max(0f, high);
        high *= Smooth(inside / Mathf.Max(24f, high * 0.9f));      // (down to the sea over a width that grows with its height: within 16 m it stood as a wall of flutes)
        y += Mathf.Lerp(high, Terrace(high, 8f, 0.32f), 0.25f);
        // the great mountain comes down to the sea over a width that grows with its height there (30 m for a low
        // flank, half its height for a high one), so its sea cliffs are steep, not a sheer drop of a hundred metres
        // drawn in long thin streaks
        float mountainHere = MountainHeight(p);
        y += mountainHere * Smooth(inside / Mathf.Max(30f, mountainHere * 1.0f));

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
    // The falling snow (and, with 'blizzard', the driving snow Winter Walk's WeatherDriver switches on in a blizzard:
    // four times as much, small and fast, blown flat by the wind)
    void Snowfall(PuzzleBuilder b, bool blizzard = false)
    {
        Piece fall = b.Empty(blizzard ? "Blizzard Snowfall" : "Snowfall", 0f, 0f, 0f, false);
        ParticleSystem ps = fall.Go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.duration = 10f;
        main.startLifetime = blizzard ? new ParticleSystem.MinMaxCurve(2.5f, 4f) : new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
        main.startColor = new Color(1f, 1f, 1f, 0.92f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = blizzard ? 16000 : 9000;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = blizzard ? 4200f : 1100f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(46f, 1f, 46f);

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = blizzard ? new ParticleSystem.MinMaxCurve(9f, 15f) : new ParticleSystem.MinMaxCurve(0.5f, 1.3f);       // the wind
        velocity.y = blizzard ? new ParticleSystem.MinMaxCurve(-4.5f, -2.5f) : new ParticleSystem.MinMaxCurve(-2.6f, -1.5f);     // falling
        velocity.z = blizzard ? new ParticleSystem.MinMaxCurve(-3f, 4f) : new ParticleSystem.MinMaxCurve(-0.2f, 0.5f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = blizzard ? 2.2f : 0.55f;
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
        if (blizzard) { renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = 0.012f; renderer.lengthScale = 1f; }      // streaks
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Component follow = b.AddCustomScript(fall, "SnowfallFollow");
        Sdk.Set(follow, "areaCentre", new Vector3((X0 + X1) / 2f, 0f, (Z0 + Z1) / 2f));
        Sdk.Set(follow, "areaSize", new Vector3(X1 - X0, 0f, Z1 - Z0));
        if (blizzard) { Sdk.Set(follow, "height", 7f); fall.Go.SetActive(false); }      // (Winter Walk switches it on)
    }

    // The freezing fog round the island, and the glow of its lights through it. Three ribbons of mist ring the island's
    // box, each further out and taller than the last (BWM/Veil: thick from afar, clear near the camera, so walking in
    // is gradual); and over the lowland, the lodge and the summit hang big soft blooms of red and green light
    // (BWM/VeilGlow) that show through the fog from the mainland and fade out as a player comes near.
    void Veil(PuzzleBuilder b, Vector3 root, System.Func<Vector2, float> heightAt)
    {
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        foreach (var (outBy, tall) in new[] { (40f, 190f), (110f, 260f), (190f, 330f) })
        {
            // a rounded rectangle round the box, a point every 40 m
            float x0 = X0 - outBy, x1 = X1 + outBy, z0 = Z0 - outBy, z1 = Z1 + outBy, r = 260f;
            var ring = new List<Vector2>();
            void Corner(Vector2 c, float from) { for (int k = 0; k <= 10; k++) { float a = from + Mathf.PI * 0.5f * k / 10f; ring.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); } }
            Corner(new Vector2(x1 - r, z1 - r), 0f); Corner(new Vector2(x0 + r, z1 - r), Mathf.PI * 0.5f);
            Corner(new Vector2(x0 + r, z0 + r), Mathf.PI); Corner(new Vector2(x1 - r, z0 + r), Mathf.PI * 1.5f);
            ring.Add(ring[0]);
            float along = outBy * 7f;
            for (int i = 0; i + 1 < ring.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((ring[i + 1] - ring[i]).magnitude / 40f));
                for (int st = 0; st < steps; st++)
                {
                    Vector2 a = Vector2.Lerp(ring[i], ring[i + 1], (float)st / steps), c = Vector2.Lerp(ring[i], ring[i + 1], (float)(st + 1) / steps);
                    float len = (c - a).magnitude;
                    int v0 = verts.Count;
                    verts.Add(new Vector3(a.x - root.x, -3f - root.y, a.y - root.z)); uvs.Add(new Vector2(along, 0f));
                    verts.Add(new Vector3(c.x - root.x, -3f - root.y, c.y - root.z)); uvs.Add(new Vector2(along + len, 0f));
                    verts.Add(new Vector3(c.x - root.x, tall - root.y, c.y - root.z)); uvs.Add(new Vector2(along + len, 1f));
                    verts.Add(new Vector3(a.x - root.x, tall - root.y, a.y - root.z)); uvs.Add(new Vector2(along, 1f));
                    tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
                    along += len;
                }
            }
        }
        var veil = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        veil.SetVertices(verts); veil.SetUVs(0, uvs); veil.SetTriangles(tris, 0); veil.RecalculateBounds();
        Piece veilPiece = b.MeshPiece("Fog Veil", veil, b.ShaderMaterial("BWM/Veil", "Veil"), false);
        veilPiece.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // the blooms: red and green by turns, over the places that are lit. Small (a third of the size they were first
        // made, at the user's word, 2026-11-22): the fog itself carries the red and green now (BWM/Veil)
        const float BloomSize = 0.3f;
        var gv = new List<Vector3>(); var gu = new List<Vector2>(); var gu2 = new List<Vector2>(); var gc = new List<Color>(); var gt = new List<int>();
        var places = new List<(Vector2 at, float up, float radius)>
        {
            (Plaza, 24f, 150f), (Plaza + new Vector2(120f, 40f), 18f, 110f), (Plaza + new Vector2(-110f, -90f), 18f, 110f), (Plaza + new Vector2(60f, -150f), 16f, 100f),
            (ShorePoint + (Plaza - ShorePoint).normalized * 60f, 14f, 90f), (Lake, 14f, 100f), (GondolaBottom, 22f, 90f),
            (new Vector2(-520f, -900f), 16f, 110f), (new Vector2(-760f, -1120f), 16f, 120f), (new Vector2(-880f, -1250f), 16f, 110f), (new Vector2(-640f, -1270f), 16f, 100f),
            (new Vector2(-860f, -660f), 16f, 100f), (new Vector2(-1040f, -960f), 18f, 110f),
            (PisteFinish(out _, out _), 26f, 140f), (SkiLiftBottom, 20f, 100f), (SkiLiftTop, 20f, 110f), (Summit, 26f, 170f),
        };
        if (WinterIsleMine.Stations.Count > 0) places.Add((WinterIsleMine.Tower, 20f, 110f));
        for (int i = 0; i < places.Count; i++)
        {
            float h = heightAt(places[i].at);
            if (float.IsNaN(h)) h = 0f;
            var centre = new Vector3(places[i].at.x - root.x, h + places[i].up - root.y, places[i].at.y - root.z);
            Color c = i % 2 == 0 ? new Color(1f, 0.16f, 0.1f) : new Color(0.12f, 1f, 0.3f);
            int v0 = gv.Count;
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            { gv.Add(centre); gu.Add(corner); gu2.Add(new Vector2(places[i].radius * BloomSize, i * 1.7f)); gc.Add(c); }
            gt.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
        }
        var glow = new Mesh();
        glow.SetVertices(gv); glow.SetUVs(0, gu); glow.SetUVs(1, gu2); glow.SetColors(gc); glow.SetTriangles(gt, 0);
        glow.bounds = new Bounds(new Vector3((X0 + X1) / 2f - root.x, 300f - root.y, (Z0 + Z1) / 2f - root.z), new Vector3(X1 - X0 + 800f, 1400f, Z1 - Z0 + 800f));      // (every vertex sits at its bloom's centre: the real extent is the shader's)
        Piece glowPiece = b.MeshPiece("Fog Glow", glow, b.ShaderMaterial("BWM/VeilGlow", "Veil Glow"), false);
        glowPiece.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Debug.Log("[BWM] Winter Isle: a fog veil of " + (tris.Count / 6) + " panels and " + places.Count + " blooms of light");
    }

    // The island's tiny lights. The mountain is strung like a Christmas tree: one string winds down it from the summit
    // in a spiral, a turn every 42 m of height, a bulb about every 6 m; and both sides of the railway carry bulbs
    // wherever it runs in the open. Each bulb is a bead on a short stake (BWM/TinyLight draws all of them as one mesh;
    // at night they are lit, and never smaller than a pixel or two, so the strings show from across the island). The
    // light they throw on the snow is baked into a map of the island the ground's shader adds at night, so the
    // mountain glows in bands of red, green and gold without a single real light.
    void Garlands(PuzzleBuilder b, Vector3 root, System.Func<Vector2, float> heightAt, Material ground)
    {
        var bulbs = new List<(Vector3 at, Color color)>();
        Color red = new Color(1f, 0.14f, 0.1f), green = new Color(0.12f, 0.95f, 0.28f), gold = new Color(1f, 0.74f, 0.22f);
        Color Pick(int n) { int run = n / 3 % 5; return run == 4 ? gold : run % 2 == 0 ? red : green; }      // runs of three: red, green, red, green, gold (so their light on the snow keeps its colour)
        var stakes = new MeshBuilder();
        var stakeColor = new Color(0.2f, 0.17f, 0.14f);
        void Bulb(Vector3 world, float stake)
        {
            if (stake > 0.05f) Kit.AddBox(stakes, new Vector3(world.x - root.x, world.y - stake - root.y, world.z - root.z), new Vector3(0.035f, stake - 0.03f, 0.035f), stakeColor);
            bulbs.Add((world, Pick(bulbs.Count)));
        }

        // the mountain's spiral
        int onMountain = 0;
        {
            const float Top = 580f, Bottom = 60f, Pitch = 42f, Spacing = 6f;
            float theta = 0f;
            Vector2 last = new Vector2(1e9f, 0f);
            float lastRadius = 40f;
            while (true)
            {
                float want = Top - Pitch * theta / (2f * Mathf.PI);
                if (want < Bottom) break;
                var dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                // outward from the summit to where the ground first drops below the string's height here
                float found = -1f, prev = heightAt(Peak + dir * 4f);
                for (float r = 12f; r < 1700f; r += 8f)
                {
                    float h = heightAt(Peak + dir * r);
                    if (float.IsNaN(h)) break;
                    if (prev >= want && h < want)
                    {
                        float lo = r - 8f, hi = r;
                        for (int it = 0; it < 5; it++) { float mid = (lo + hi) * 0.5f; if (heightAt(Peak + dir * mid) >= want) lo = mid; else hi = mid; }
                        found = (lo + hi) * 0.5f;
                        break;
                    }
                    prev = h;
                }
                if (found > 0f)
                {
                    Vector2 p = Peak + dir * found;
                    lastRadius = found;
                    if ((p - last).magnitude >= Spacing && !WinterIsleMine.InCut(p) && !WinterIsleRail.InCut(p))
                    {
                        float g = heightAt(p);
                        if (g > 2f) { Bulb(new Vector3(p.x, g + 0.8f, p.y), 0.8f); onMountain++; }
                        last = p;
                    }
                }
                theta += 1.5f / Mathf.Max(lastRadius, 30f);      // about a metre and a half of string a step
            }
        }

        // the railway: a bulb each side by turns, every point of open track (4 m)
        int onRail = 0;
        foreach (RailLine l in WinterIsleRail.Lines)
            for (int i = 0; i < l.N; i++)
            {
                if (l.Kind[i] == 1) continue;      // not in the tunnels
                Vector3 side = WinterIsleRail.Right(l, i) * (i % 2 == 0 ? 2.7f : -2.7f);
                Vector3 at = l.P[i] + side;
                float g = heightAt(new Vector2(at.x, at.z));
                // on the ground a stake in the snow; on a trestle a short post on the deck's edge
                float y = l.Kind[i] == 2 || float.IsNaN(g) ? l.P[i].y + 1.1f : Mathf.Max(g, l.P[i].y - 0.4f) + 0.9f;
                Bulb(new Vector3(at.x, y, at.z), l.Kind[i] == 2 ? 1.1f : 0.9f);
                onRail++;
            }

        // one mesh of squares, each vertex at its bulb's centre (the shader turns them to the camera)
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var uv2 = new List<Vector2>(); var colors = new List<Color>(); var tris = new List<int>();
        var random = new System.Random(1224);
        foreach (var bulb in bulbs)
        {
            int v0 = verts.Count;
            float phase = (float)random.NextDouble();
            var centre = new Vector3(bulb.at.x - root.x, bulb.at.y - root.y, bulb.at.z - root.z);
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            { verts.Add(centre); uvs.Add(corner); uv2.Add(new Vector2(phase, 0f)); colors.Add(bulb.color); }
            tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetUVs(1, uv2); mesh.SetColors(colors); mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(new Vector3((X0 + X1) / 2f - root.x, 300f - root.y, (Z0 + Z1) / 2f - root.z), new Vector3(X1 - X0 + 200f, 1400f, Z1 - Z0 + 200f));
        Piece lights = b.MeshPiece("Tiny Lights", mesh, b.ShaderMaterial("BWM/TinyLight", "Tiny Lights"), false);
        lights.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Piece stakePiece = b.MeshPiece("Tiny Light Stakes", stakes.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f)), false);
        stakePiece.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // the pools of light on the snow: each bulb's colour spread about 4 m round it, over the island's box
        const int W = 1024;
        int H = Mathf.CeilToInt(W * (Z1 - Z0) / (X1 - X0));
        var pool = new Vector3[W * H];
        float perX = W / (X1 - X0), perZ = H / (Z1 - Z0), sigma = 4.2f * perX;
        int reach = Mathf.CeilToInt(sigma * 2.6f);
        foreach (var bulb in bulbs)
        {
            float px = (bulb.at.x - X0) * perX - 0.5f, pz = (bulb.at.z - Z0) * perZ - 0.5f;
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int ix = Mathf.RoundToInt(px) + dx, iz = Mathf.RoundToInt(pz) + dz;
                    if (ix < 0 || iz < 0 || ix >= W || iz >= H) continue;
                    float ddx = ix - px, ddz = iz - pz;
                    float fall = Mathf.Exp(-(ddx * ddx + ddz * ddz) / (2f * sigma * sigma)) * 0.34f;
                    pool[iz * W + ix] += new Vector3(bulb.color.r, bulb.color.g, bulb.color.b) * fall;
                }
        }
        var texture = new Texture2D(W, H, TextureFormat.RGB24, false);
        var pixels = new Color[W * H];
        for (int k = 0; k < pixels.Length; k++) pixels[k] = new Color(Mathf.Min(1f, pool[k].x), Mathf.Min(1f, pool[k].y), Mathf.Min(1f, pool[k].z)).gamma;
        texture.SetPixels(pixels);
        texture.Apply();
        const string poolPath = "Assets/Puzzles/WinterIsle/LightPools.png";
        System.IO.File.WriteAllBytes(poolPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        UnityEditor.AssetDatabase.ImportAsset(poolPath, UnityEditor.ImportAssetOptions.ForceUpdate);
        var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(poolPath);
        importer.wrapMode = TextureWrapMode.Clamp; importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.maxTextureSize = 1024;
        importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
        ground.SetTexture("_GlowMap", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(poolPath));
        ground.SetVector("_GlowBox", new Vector4(X0, Z0, 1f / (X1 - X0), 1f / (Z1 - Z0)));
        UnityEditor.EditorUtility.SetDirty(ground);
        Debug.Log("[BWM] Winter Isle tiny lights: " + onMountain + " on the mountain's spiral, " + onRail + " along the railway; their light on the snow baked into a " + W + " x " + H + " map");
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
            foreach (Vector3 s in FormerSites) g = Mathf.Min(g, (p - new Vector2(s.x, s.y)).magnitude - s.z);
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
            shape = Mathf.Max(shape, MountainReach(p) + 80f);      // the mountain's own ground
            toEdge = Mathf.Min(toEdge, shape);
            float coves = Mathf.Lerp(CoastBroad(p), CoastFull(p), Smooth((Guard(p) - 30f) / 55f))
                + (Mathf.PerlinNoise(p.x * 0.05f + 6.6f, p.y * 0.05f + 2.4f) - 0.5f) * 14f * (1f - BeachAmount(p));      // rocky shores ragged, beaches smooth
            // ...but where the great mountain comes down to the sea, only the broad line: the mountain fades out by its
            // distance from the water, so every narrow inlet carved its flank into fins and spires
            if (MountainReach(p) > -150f)
            {
                float mountainHere = MountainHeight(p);
                if (mountainHere > 5f) coves = Mathf.Lerp(coves, CoastBroad(p), Smooth(mountainHere / 50f));
            }
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
        bool rail = WinterIsleRail.Load();      // the mountain railway's line (tools/railroute.py): benched in 3c
        if (!rail) Debug.LogWarning("[BWM] Winter Isle: no railway (unity/Land/WinterIsle.rail: run tools\\railroute.py)");
        var sites = new List<Vector2>();
        var flats = new List<(Vector2 at, float radius, float blend)> { (Plaza, PlazaRadius, 16f), (Summit, SummitRadius, 14f), (GondolaBottom, 10f, 9f) };
        // the mine's portals: where each tunnel's line meets the mountainside, with a level apron outside
        {
            Vector2 Bearing(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 finish = PisteFinish(out Vector2 finishDir, out float finishHalf);
            var wanted = new List<Vector2>
            {
                Peak + Bearing(0.52f) * 1330f,                                                      // the mine tower's, north of the pistes' foot
                finish - new Vector2(finishDir.y, -finishDir.x) * (finishHalf + 18f) + finishDir * 8f,    // by the finish and the lodge
                Plaza + new Vector2(0f, -(PlazaRadius + 24f)),
                Lake + (Plaza - Lake).normalized * (LakeRadius + 32f),
                Plaza + (ShorePoint - Plaza).normalized * 135f,                                     // towards the causeway
                Peak + Bearing(2.62f) * 735f, Peak + Bearing(4.45f) * 680f, Peak + Bearing(3.4f) * 725f,      // the mountain's far sides
            };
            bool StationAllowed(Vector2 p)
            {
                if ((p - Plaza).magnitude < PlazaRadius + 16f || (p - Lake).magnitude < LakeRadius + 18f || (p - GondolaBottom).magnitude < 26f || (p - Summit).magnitude < 60f) return false;
                if (MountainHeight(p) > 4f && Piste(p) > 0.05f) return false;
                foreach (Vector3 site in b.OtherSites) if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 18f) return false;
                if (rail && WinterIsleRail.Distance(p, 64f) < 45f) return false;
                if (WinterIsleAirport.Outside(p) < 30f || WinterIsleAirport.StripOutside(p) < 20f) return false;
                return true;
            }
            WinterIsleMine.PlaceStations(wanted, 13, p => p.x < X0 || p.x > X1 || p.y < Z0 || p.y > Z1 ? float.NaN : height[Cell_(p)], StationAllowed, X0, Z0, X1, Z1);
        }
        for (int station = 0; station < WinterIsleMine.Stations.Count; station++)
            flats.Add((WinterIsleMine.Stations[station], station == 0 ? WinterIsleMine.TowerStationRadius : WinterIsleMine.StationRadius, 8f));
        foreach (Vector3 site in b.OtherSites)
        {
            var at = new Vector2(site.x, site.y);
            if (at.x < X0 || at.x > X1 || at.y < Z0 || at.y > Z1 || !(height[Cell_(at)] > 0.2f)) continue;      // not on the island
            sites.Add(at);
            // A site on a slope is cut in as a terrace: the blend round its flat widens with the height it has to make
            // up at r + 12 (1.5 m of blend a metre, as the railway's banks), from 10 m on level ground to 36 m, so the
            // banks lie back instead of standing as cliffs. Sites on level ground keep the 10 m blend.
            float centre = Mathf.Round(height[Cell_(at)] * 2f) / 2f, drop = 0f;
            for (int a = 0; a < 24; a++)
            {
                Vector2 q = at + new Vector2(Mathf.Cos(a * Mathf.PI / 12f), Mathf.Sin(a * Mathf.PI / 12f)) * (site.z + 12f);
                if (q.x < X0 || q.x > X1 || q.y < Z0 || q.y > Z1) continue;
                float hq = height[Cell_(q)];
                if (hq > 0f) drop = Mathf.Max(drop, Mathf.Abs(hq - centre));
            }
            flats.Add((at, site.z + 2f, Mathf.Clamp(drop * 1.5f, 10f, 36f)));
        }
        if (rail) flats.Add((WinterIsleRail.TowerSpot(out _), 8f, 6f));      // the train tower's ground, beside the Plaza Halt
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
            float airport = WinterIsleAirport.Outside(p);      // the airport's runway and apron
            if (airport < WinterIsleAirport.Blend) height[k] = Mathf.Lerp(WinterIsleAirport.Level, height[k], Smooth(airport / WinterIsleAirport.Blend));
        }
        // 3b. No walls: nowhere is the ground steeper than about 58 degrees (the sea counting as height 0). Two sweeps
        //     each way of "no higher than a neighbour plus a step" erode any sheer drop, inland or at the shore,
        //     into a steep slope, whatever made it
        {
            float step = 1.6f * Cell, stepDiag = step * 1.41421f;
            int walled = 0;
            void Limit(int k, int n, float s)
            {
                float hn = float.IsNaN(height[n]) ? 0f : height[n];
                if (height[k] > hn + s) { height[k] = hn + s; walled++; }
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = 0; j <= nz; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        int k = j * row + i;
                        if (float.IsNaN(height[k])) continue;
                        if (i > 0) Limit(k, k - 1, step);
                        if (j > 0) { Limit(k, k - row, step); if (i > 0) Limit(k, k - row - 1, stepDiag); if (i < nx) Limit(k, k - row + 1, stepDiag); }
                    }
                for (int j = nz; j >= 0; j--)
                    for (int i = nx; i >= 0; i--)
                    {
                        int k = j * row + i;
                        if (float.IsNaN(height[k])) continue;
                        if (i < nx) Limit(k, k + 1, step);
                        if (j < nz) { Limit(k, k + row, step); if (i < nx) Limit(k, k + row + 1, stepDiag); if (i > 0) Limit(k, k + row - 1, stepDiag); }
                    }
            }
            Debug.Log("[BWM] Winter Isle: " + walled + " lowerings to take sheer drops back to 58 degrees");
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
                height[k] += facets * craggy[k] * 0.35f;
            }
            // facets: steep ground is broken into planar faces some 24 m across, each the plane the slope has at its
            // middle, so that neighbouring faces meet at creases and steps: buttresses and slabs, not smooth noise
            // (one face in four is laid back into a ledge that holds snow)
            var preFacet = (float[])height.Clone();
            float SampleH(Vector2 q)
            {
                float fx = Mathf.Clamp((q.x - X0) / Cell, 0f, nx - 0.001f), fz = Mathf.Clamp((q.y - Z0) / Cell, 0f, nz - 0.001f);
                int i0 = (int)fx, j0 = (int)fz, k0 = j0 * row + i0;
                float tx = fx - i0, tz = fz - j0;
                float a0 = preFacet[k0], a1 = preFacet[k0 + 1], a2 = preFacet[k0 + row], a3 = preFacet[k0 + row + 1];
                if (float.IsNaN(a0) || float.IsNaN(a1) || float.IsNaN(a2) || float.IsNaN(a3)) return preFacet[Cell_(q)];
                return Mathf.Lerp(Mathf.Lerp(a0, a1, tx), Mathf.Lerp(a2, a3, tx), tz);
            }
            const float FacetCell = 36f;
            var planes = new Dictionary<long, (Vector2 at, float h, Vector2 grad)>();
            (Vector2 at, float h, Vector2 grad) Plane(int ci, int cj)
            {
                long key = ((long)ci << 32) ^ (uint)cj;
                if (planes.TryGetValue(key, out var pl)) return pl;
                float jx = Mathf.PerlinNoise(ci * 0.71f + 3.3f, cj * 0.71f + 9.1f), jz = Mathf.PerlinNoise(ci * 0.71f + 17.7f, cj * 0.71f + 1.9f);
                var facetAt = new Vector2((ci + 0.05f + 0.9f * jx) * FacetCell, (cj + 0.05f + 0.9f * jz) * FacetCell);
                float hc = SampleH(facetAt);
                var grad = new Vector2(SampleH(facetAt + Vector2.right * 9f) - SampleH(facetAt - Vector2.right * 9f), SampleH(facetAt + Vector2.up * 9f) - SampleH(facetAt - Vector2.up * 9f)) / 18f;
                // (no faces laid back into ledges: every ledge's snow cap read as the same scallop stamped across the face;
                // the snow shader draws broken ledges of snow instead)
                pl = (facetAt, hc, grad);
                planes[key] = pl;
                return pl;
            }
            int faceted = 0;
            for (int k = 0; k < count; k++)
            {
                if (craggy[k] <= 0f || height[k] < 3f) continue;
                Vector2 p = At(k);
                if ((p - Summit).magnitude < SummitRadius + 30f) continue;
                float byShore = Smooth((inside[k] - 25f) / 45f);      // none at the sea's edge: there a plane fitted to the cliff top stood out as a row of teeth
                if (byShore <= 0f) continue;
                int ci = Mathf.FloorToInt(p.x / FacetCell), cj = Mathf.FloorToInt(p.y / FacetCell);
                float nearestD = float.MaxValue, secondD = float.MaxValue; (Vector2 at, float h, Vector2 grad) nearest = default, second = default;
                for (int oj = -1; oj <= 1; oj++)
                    for (int oi = -1; oi <= 1; oi++)
                    {
                        var facet = Plane(ci + oi, cj + oj);
                        float dd = (facet.at - p).sqrMagnitude;
                        if (dd < nearestD) { secondD = nearestD; second = nearest; nearestD = dd; nearest = facet; }
                        else if (dd < secondD) { secondD = dd; second = facet; }
                    }
                // where two faces meet, a short steep blend instead of a sheer step (sheer steps ran down a face in flutes)
                float planeH = nearest.h + Vector2.Dot(nearest.grad, p - nearest.at);
                float otherH = second.h + Vector2.Dot(second.grad, p - second.at);
                // (a face whose middle or slope was sampled out over the open sea has no height: leave the ground
                // as it is there, or the mesh had holes in the slopes above the shore)
                if (float.IsNaN(planeH)) continue;
                if (float.IsNaN(otherH)) otherH = planeH;
                float edge = Smooth((Mathf.Sqrt(secondD) - Mathf.Sqrt(nearestD)) / 7f);
                planeH = Mathf.Lerp((planeH + otherH) * 0.5f, planeH, edge);
                planeH = Mathf.Clamp(planeH, height[k] - 7f, height[k] + 7f);      // creases and steps, never a wall tens of metres high
                height[k] = Mathf.Lerp(height[k], planeH, craggy[k] * 0.9f * byShore);
                faceted++;
            }
            Debug.Log("[BWM] Winter Isle crags: " + faceted + " points of steep ground faceted into " + planes.Count + " faces");
            // cliff bands: on the mountain's upper steep sides the ground steps down in bands of rock 15 to 25 m tall and
            // steeper than 60 degrees, with snowy ledges between. The steps wander across the contours and break off
            // in places, so they are bands of cliff and not terraces
            int banded = 0;
            for (int k = 0; k < count; k++)
            {
                if (craggy[k] <= 0f || height[k] < 60f) continue;
                Vector2 p = At(k);
                float fromPeak = (p - Peak).magnitude;
                if (fromPeak > 1150f || fromPeak < SummitRadius + 40f || Piste(p) > 0.2f) continue;
                // (in patches a few hundred metres across, on about a third of the steep ground: bands everywhere at
                // one spacing read from the air as a terraced hillside)
                float patch = Smooth((Mathf.PerlinNoise(p.x * 0.0045f + 3.3f, p.y * 0.0045f + 8.1f) - 0.55f) / 0.1f);
                float band = craggy[k] * Smooth((height[k] - 110f) / 90f) * patch * Smooth((inside[k] - 40f) / 60f);
                if (band <= 0f) continue;
                float warp = (Mathf.PerlinNoise(p.x * 0.006f + 1.7f, p.y * 0.006f + 5.2f) - 0.5f) * 90f + (Mathf.PerlinNoise(p.x * 0.03f + 9.9f, p.y * 0.03f + 2.6f) - 0.5f) * 14f;
                float stepH = 45f + 25f * Mathf.PerlinNoise(p.x * 0.004f + 9.1f, p.y * 0.004f + 0.7f);      // two or three bands down a face, not ten
                float stepped = Terrace(height[k] + warp, stepH, 0.3f) - warp;
                height[k] = Mathf.Lerp(height[k], stepped, 0.62f * band);
                banded++;
            }
            Debug.Log("[BWM] Winter Isle cliff bands: " + banded + " points");
        }

        // 3c. The railway (WinterIsleRail): which stretches of the line run on the ground, in tunnels or on trestles,
        //     then the ground benched to the line where it runs on it, a level bed with cuttings and embankments either
        //     side (wider at the stations, and running on into each tunnel's portal). Each cell takes the bench of the
        //     nearest point of the line. Then how much room the dressing leaves round the line, per cell
        var railClear = new float[count];
        for (int k = 0; k < count; k++) railClear[k] = 1e6f;
        if (rail)
        {
            float GridHeight(Vector2 p)
            {
                if (p.x < X0 || p.x > X1 || p.y < Z0 || p.y > Z1) return float.NaN;
                float fx = Mathf.Clamp((p.x - X0) / Cell, 0f, nx - 0.001f), fz = Mathf.Clamp((p.y - Z0) / Cell, 0f, nz - 0.001f);
                int i0 = (int)fx, j0 = (int)fz, k0 = j0 * row + i0;
                float a0 = height[k0], a1 = height[k0 + 1], a2 = height[k0 + row], a3 = height[k0 + row + 1];
                if (float.IsNaN(a0) || float.IsNaN(a1) || float.IsNaN(a2) || float.IsNaN(a3)) return height[Cell_(p)];
                return Mathf.Lerp(Mathf.Lerp(a0, a1, fx - i0), Mathf.Lerp(a2, a3, fx - i0), fz - j0);
            }
            WinterIsleRail.Classify(GridHeight);
            int lineCount = WinterIsleRail.Line.Count;
            var nearestPoint = new Dictionary<int, (float d, int i)>();
            var gaps = new float[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                Vector3 lp = WinterIsleRail.Line[i];
                var at = new Vector2(lp.x, lp.z);
                float g = GridHeight(at);
                gaps[i] = float.IsNaN(g) ? 6f : g - lp.y;
                WinterIsleRail.Bench(i, gaps[i], out float half, out float bank, out _);
                float reach = Mathf.Max(half + bank, WinterIsleRail.Keep(i) + 6f);
                int r = Mathf.CeilToInt(reach / Cell) + 1, centre = Cell_(at);
                for (int oj = -r; oj <= r; oj++)
                    for (int oi = -r; oi <= r; oi++)
                    {
                        int ci = centre % row + oi, cj = centre / row + oj;
                        if (ci < 0 || cj < 0 || ci > nx || cj > nz) continue;
                        int k = cj * row + ci;
                        if (float.IsNaN(height[k])) continue;
                        float d = (At(k) - at).magnitude;
                        if (d > reach) continue;
                        if (nearestPoint.TryGetValue(k, out var had) && had.d <= d) continue;
                        nearestPoint[k] = (d, i);
                    }
            }
            int benched = 0;
            foreach (var cell in nearestPoint)
            {
                int k = cell.Key, i = cell.Value.i;
                float d = cell.Value.d;
                WinterIsleRail.Bench(i, gaps[i], out float half, out float bank, out float strength);
                float keep = WinterIsleRail.Keep(i);
                if (keep > 0f) railClear[k] = d - keep;
                if (strength <= 0f || d > half + bank) continue;
                float pull = strength * (1f - Smooth((d - half) / bank));
                height[k] = Mathf.Lerp(height[k], WinterIsleRail.Line[i].y - WinterIsleRail.BenchBelow, pull);
                benched++;
            }
            Debug.Log("[BWM] Winter Isle railway: " + benched + " points of ground benched to the line");
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
        foreach (Vector2 site in sites) if (site.x > -2300f) targets.Add(Cell_(site));      // (the far west is reached by train, mine or plane: a trail round the mountain's foot drew a dark ring)
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
        foreach (Vector2 station in WinterIsleMine.Stations)      // a mine station is somewhere to walk to (not those on the mountain's far sides: a trail round its foot drew a dark ring on the rock)
        {
            if (station.x < -1500f) continue;
            int apron = Cell_(station + (Plaza - station).normalized * 6f);
            if (reachable[apron]) trailEnds.Add(apron);
        }
        for (int s = 0; rail && s < WinterIsleRail.Stops.Count; s++)      // the railway's stations in the lowland: walked to from the plaza
        {
            Vector2 station = WinterIsleRail.Station(s);
            if (station.x < -1500f) continue;
            Vector2 toPlaza = (Plaza - station).normalized;
            int apron = Cell_(station + toPlaza * (WinterIsleRail.StationHalf + 3f));
            if (reachable[apron]) trailEnds.Add(apron);
        }
        {
            int apron = Cell_(WinterIsleAirport.ApronMiddle);      // the airport
            if (reachable[apron]) trailEnds.Add(apron);
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
        // the gondola's stations, the summit, and a corridor under the cable, kept clear of trees and rocks
        structures.Add((Summit, SummitRadius + 1f));
        structures.Add((GondolaBottom, 14f));
        for (int station = 0; station < WinterIsleMine.Stations.Count; station++)
            structures.Add((WinterIsleMine.Stations[station], (station == 0 ? WinterIsleMine.TowerStationRadius : WinterIsleMine.StationRadius) + 1f));
        for (float t = 0f; t <= 1f; t += 0.004f) structures.Add((Vector2.Lerp(GondolaBottom, GondolaTop, t), 10f));      // a lift line cut through the forest
        structures.Add((SkiLiftBottom, 16f));
        structures.Add((SkiLiftTop, 13f));
        if (rail)      // the summit lift's stations and line
        {
            Vector2 liftFoot = WinterIsleRail.SummitLiftFoot(), liftTop = Summit + (liftFoot - Summit).normalized * SummitLiftReach;
            structures.Add((liftFoot, 9f)); structures.Add((liftTop, 9f));
            for (float t = 0f; t <= 1f; t += 0.05f) structures.Add((Vector2.Lerp(liftFoot, liftTop, t), 6f));
        }
        for (float t = 0f; t <= 1f; t += 0.006f) structures.Add((Vector2.Lerp(SkiLiftBottom, SkiLiftTop, t), 8f));      // ...and the ski lift's
        {
            Vector2 finishAt = PisteFinish(out _, out _);
            for (float t = 0f; t <= 1f; t += 0.02f) structures.Add((Vector2.Lerp(SkiLiftBottom, finishAt, t), 6f));      // a clear way from the finish to the lift
        }
        bool Free(Vector2 p, float margin)      // clear of paths, the plaza, the lake, structures and every puzzle site
        {
            if (PathDistance(p) < 3.5f + margin || (p - Plaza).magnitude < PlazaRadius + margin || (p - Lake).magnitude < LakeRadius + 6f + margin) return false;
            if (MountainHeight(p) > 4f && Piste(p) > 0.3f) return false;      // the pistes are kept clear; the ribs between them take rocks and trees
            foreach (var s in structures) if ((p - s.at).magnitude < s.radius + margin) return false;
            if (railClear[Cell_(p)] < margin) return false;
            if (WinterIsleAirport.Outside(p) < 4f + margin || WinterIsleAirport.StripOutside(p) < 3f + margin) return false;
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

        foreach (var st in structures) Block(st.at, st.radius + 3f);
        for (int k = 0; k < count; k++) if (railClear[k] < 3f) blocked[k] = true;      // the railway's bed, banks and stations
        for (int k = 0; k < count; k++) if (WinterIsleAirport.Outside(At(k)) < 6f || WinterIsleAirport.StripOutside(At(k)) < 4f) blocked[k] = true;      // the airport, the summit's airstrip

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
        // The mine's line, routed under the finished ground; nothing is planted over its sheds
        {
            bool MineAvoid(Vector2 p)
            {
                if ((p - Plaza).magnitude < PlazaRadius + 14f || (p - Lake).magnitude < LakeRadius + 14f || (p - GondolaBottom).magnitude < 22f) return true;
                foreach (Vector3 site in b.OtherSites) if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 10f) return true;
                foreach (var c in cabins) if ((c.at - p).magnitude < 16f) return true;
                if (railClear[Cell_(p)] < 8f) return true;      // no shed across the railway
                if (WinterIsleAirport.Outside(p) < 10f) return true;      // ...or the airport
                return false;
            }
            WinterIsleMine.FindRoute(p => p.x < X0 || p.x > X1 || p.y < Z0 || p.y > Z1 ? float.NaN : HeightAt(p), MineAvoid, X0, Z0, X1, Z1);
            foreach (var kept in WinterIsleMine.KeptClear()) structures.Add(kept);
            // where the tunnel runs in a shed, the ground is heaped over its roof: a low bank of snow with gentle sides
            int bermed = 0;
            foreach (var shed in WinterIsleMine.ShedRoofs())
            {
                float need = shed.roof + 0.9f;
                int sci = Mathf.RoundToInt((shed.at.x - X0) / Cell), scj = Mathf.RoundToInt((shed.at.y - Z0) / Cell);
                for (int sdj = -4; sdj <= 4; sdj++)
                    for (int sdi = -4; sdi <= 4; sdi++)
                    {
                        int si = sci + sdi, sj = scj + sdj;
                        if (si < 0 || sj < 0 || si > nx || sj > nz) continue;
                        int sk = sj * row + si;
                        if (float.IsNaN(height[sk])) continue;
                        Vector2 sp = At(sk);
                        float wanted = need - Mathf.Max(0f, (sp - shed.at).magnitude - 4.5f) * 0.55f + (Mathf.PerlinNoise(sp.x * 0.08f, sp.y * 0.08f) - 0.5f) * 0.4f;
                        if (wanted > height[sk]) { height[sk] = wanted; bermed++; }
                    }
            }
            Debug.Log("[BWM] Winter Isle: ground heaped over the mine's sheds at " + bermed + " points");
        }
        // Woods: a few dense stands that the trails run through (alternately fir and birch), so that a walker is
        // sometimes among close trunks instead of always in the open
        var woods = new List<(Vector2 at, float radius, bool birch)>();
        foreach (var r in routes)
        {
            if (!r.trail) continue;
            for (int i = 6; i < r.points.Count - 6 && woods.Count < 28; i += 4)
            {
                Vector2 p = r.points[i];
                int k = Cell_(p);
                if (!(height[k] > 3f && height[k] < 120f) || Slope(k) > 0.4f || (p - Plaza).magnitude < 140f || (p - Lake).magnitude < LakeRadius + 60f) continue;
                bool clearOfSites = true;
                foreach (Vector3 site in b.OtherSites) if ((p - new Vector2(site.x, site.y)).magnitude < site.z + 70f) clearOfSites = false;
                if (!clearOfSites) continue;
                bool apart = true;
                foreach (var w in woods) if ((w.at - p).magnitude < 140f) apart = false;
                foreach (var c in cabins) if ((c.at - p).magnitude < 60f) apart = false;
                if (apart) woods.Add((p, 55f + 25f * Mathf.PerlinNoise(p.x * 0.01f, p.y * 0.01f), woods.Count % 3 == 1));
            }
        }
        var isletList = new List<(Vector2 at, float radius)>(islets);
        WinterIsleDressing.Dress(new WinterIsleDressing.Land
        {
            Forest = p => SkiForest(p, HeightAt(p)),
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

        // The ski face as built (after levelling, the no-walls limit and the facets): the slope down the middle run
        // every 25 m, and the ground across the bowl, so the profile can be checked against what it should be
        {
            var line = new System.Text.StringBuilder("[BWM] Winter Isle ski profile (metres from the peak: height, degrees):");
            for (float d = 40f; d <= SkiRadius + 10f; d += 25f)
            {
                Vector2 a0 = RunCentre(1, d, out _), a1 = RunCentre(1, d + 25f, out _);
                float h0 = HeightAt(a0), h1 = HeightAt(a1);
                line.Append($" {d:0}:{h0:0}/{Mathf.Atan2(h0 - h1, (a1 - a0).magnitude) * Mathf.Rad2Deg:0}");
            }
            Debug.Log(line.ToString());
            var across = new System.Text.StringBuilder("[BWM] Winter Isle bowl across at 250 m (radians off the face: height):");
            for (float o = -0.9f; o <= 0.91f; o += 0.1f)
            {
                float angle = SkiBearing - o;
                across.Append($" {o:0.0}:{HeightAt(Peak + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 250f):0}");
            }
            Debug.Log(across.ToString());
            int holes = 0;
            for (int k = 0; k < count; k++) if (float.IsNaN(height[k]) && inside[k] > 5f) holes++;
            if (holes > 0) Debug.Log("[BWM] Winter Isle: " + holes + " points well inside the coast have no height (holes in the ground)");
        }

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
        // ...and a negative x marks groomed piste (no conifer stands on one), which the ground shader draws as corduroy
        for (int k = 0; k < count; k++)
        {
            if (under[k].x > 0.02f || !(height[k] > 4f)) continue;
            Vector2 p = At(k);
            float piste = MountainHeight(p) > 4f ? Groomed(p) : 0f;      // (corduroy on the runs only: in the open bowl and on the banks it read as stripes everywhere)
            if (piste > 0f) under[k] = new Vector2(-piste, under[k].y);
        }
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
            // on steep rock, gullies and couloirs at a larger scale too (24 m round), where snow collects
            float form = 0.5f + convex * 0.11f;
            float steepRock = Smooth((slope - 0.7f) / 0.3f);
            if (steepRock > 0f)
            {
                float wide = 0f; int wn = 0;
                for (int o = -8; o <= 8; o += 16)
                {
                    if (i + o >= 0 && i + o <= nx && !float.IsNaN(height[k + o])) { wide += height[k + o]; wn++; }
                    if (j + o >= 0 && j + o <= nz && !float.IsNaN(height[k + o * row])) { wide += height[k + o * row]; wn++; }
                }
                if (wn > 0) form += (height[k] - wide / wn) * 0.035f * steepRock;
            }
            float heath = Smooth((Mathf.PerlinNoise(p.x * 0.035f + 7.7f, p.y * 0.035f + 2.2f) - 0.58f) / 0.12f) * 0.6f * (1f - Smooth((height[k] - 28f) / 14f));
            float shoreline = height[k] > 0f && height[k] < 1.4f ? 0.55f * (1f - beach[k]) * Smooth((Mathf.PerlinNoise(p.x * 0.022f + 8.8f, p.y * 0.022f + 1.9f) - 0.38f) / 0.25f) : 0f;      // in stretches: elsewhere snow lies to the water
            float underTrees = bareUnder[k] * (height[k] > 150f ? 0.25f : 0.8f);      // (high on the mountain the litter under the trees showed from the air as grey smears along the tree line)
            float bare = Mathf.Clamp01(Mathf.Clamp01((slope - 0.45f) * 1.2f) * 0.3f + Mathf.Clamp01(convex * 0.15f) + heath + underTrees + shoreline) * (1f - trodden[k]);
            float worn = trodden[k] * Smooth((Mathf.PerlinNoise(p.x * 0.08f + 3.1f, p.y * 0.08f + 7.3f) - 0.4f) / 0.3f) * 0.6f;      // trodden through to the ground in places
            // ...and beside the trails the snow lies thin in patches, the earth and litter showing (where walkers look)
            float beside = (1f - Smooth((PathDistance(p) - 2.5f) / 9f)) * (1f - trodden[k]) * Smooth((Mathf.PerlinNoise(p.x * 0.065f + 41.7f, p.y * 0.065f + 9.3f) - 0.48f) / 0.14f) * 0.8f;
            bare = Mathf.Max(bare, Mathf.Max(worn, beside));
            if (under[k].x < 0f) bare *= 1f + under[k].x;      // a groomed piste is snow all over
            else if (MountainHeight(At(k)) > 4f && Piste(At(k)) > 0.001f) bare *= 0.25f;      // ...and so is its edge under the trees
            float sand = beach[k] * (1f - Smooth((height[k] - 0.4f) / 0.8f));      // a strip by the water; above it the beach lies under snow
            tint[k] = new Color(1f - trodden[k], bare, sand, Mathf.Clamp01(form));      // alpha: hollow (0) .. crest (1)
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
                        Vector2 fineAt = new Vector2(X0 + (fi + 0.5f) * Cell / Sub, Z0 + (fj + 0.5f) * Cell / Sub);
                        if (WinterIsleMine.InCut(fineAt) || WinterIsleRail.InCut(fineAt)) continue;      // the mine's open cuts, the railway's portals
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
        Material groundMaterial = b.ShaderMaterial("BWM/Snow", "Snow", m => { m.SetFloat("_RockLine", 0.6f); m.SetFloat("_RockTiling", 0.3f); m.SetVector("_PisteCentre", new Vector4(Peak.x, 0f, Peak.y, 0f)); Kit.SetSnowMaps(m); });
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
                                Vector2 fineAt = new Vector2(X0 + (i * Sub + si + 0.5f) * Cell / Sub, Z0 + (j * Sub + sj + 0.5f) * Cell / Sub);
                                if (WinterIsleMine.InCut(fineAt) || WinterIsleRail.InCut(fineAt)) continue;
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
        //     None is solid. Drifts are built into the pack as a few large meshes by district; the rest are a few
        //     models drawn where the build's list says (see below).
        {
            var random = new System.Random(77031);
            float Rs(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            int Region(Vector2 p) => Mathf.Clamp((int)((p.y - Z0) / 290f), 0, 9) * 16 + Mathf.Clamp((int)((p.x - X0) / 290f), 0, 15);
            var lumpVerts = new Dictionary<int, List<Vector3>>();
            var lumpTris = new Dictionary<int, List<int>>();
            var lumpRims = new Dictionary<int, List<int>>();      // each drift's outer ring: its normals are the ground's, so no seam shows
            // Tussocks, sticks and stones are not built into the pack one by one (that was 49 million vertices, kept
            // twice in memory: 4.7 GB, and half of it video memory). The pack carries a few models of each, and
            // WinterIsle.scatter beside the Winter Walk plugin lists where each stands; Winter Walk (Scatter.cs) draws the
            // ones near the camera, instanced.
            var scatterModels = new List<(string name, MeshBuilder mesh, bool grass)>();
            var scatterPlaces = new List<(int model, Vector3 at, Quaternion turn, float scale)>();
            Vector3 World(Vector2 p, float lift) => new Vector3(p.x, HeightAt(p) + lift, p.y);
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
            // The models. Stones: 1 m across, placed at their real size (6 to 34 cm); their crowns catch snow
            const int StoneModels = 5, StickModels = 6, GrassHeights = 4;
            int firstStone = scatterModels.Count;
            for (int sv = 0; sv < StoneModels; sv++)
            {
                var mb = new MeshBuilder();
                float tall = Rs(0.5f, 0.9f), spin = Rs(0f, 6.28f);
                Vector3 foot = new Vector3(0f, -0.15f, 0f), crown = foot + Vector3.up * tall + new Vector3(Rs(-0.3f, 0.3f), 0f, Rs(-0.3f, 0.3f));
                Color col = granite * (0.8f + 0.35f * sv / (StoneModels - 1f));
                const int sides = 9;      // (round: five-sided stones read as flat chips)
                var rim = new Vector3[sides];
                var shoulder = new Vector3[sides];
                for (int sd = 0; sd < sides; sd++)
                {
                    float sa = spin + sd * 2f * Mathf.PI / sides;
                    rim[sd] = foot + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * Rs(0.88f, 1.1f);
                    shoulder[sd] = Vector3.Lerp(rim[sd], crown, 0.5f) + Vector3.up * tall * 0.32f;
                }
                for (int sd = 0; sd < sides; sd++)
                {
                    int nxt = (sd + 1) % sides;
                    mb.Quad(rim[sd], rim[nxt], shoulder[nxt], shoulder[sd], col);
                    mb.Tri(shoulder[sd], crown, shoulder[nxt], col * 1.05f);
                }
                scatterModels.Add(("Stone " + sv, mb, false));
            }
            // Sticks: 1 m long along x, with no twig, one or two; placed at their real length between two points of ground
            int firstStick = scatterModels.Count;
            for (int sv = 0; sv < StickModels; sv++)
            {
                var mb = new MeshBuilder();
                Vector3 a0 = new Vector3(-0.5f, 0f, 0f), a1 = new Vector3(0.5f, 0f, 0f);
                float thick = Rs(0.017f, 0.03f);
                Stick(mb, a0, a1, thick);
                for (int tw = 0; tw < sv % 3; tw++)
                {
                    Vector3 fork = Vector3.Lerp(a0, a1, Rs(0.3f, 0.8f));
                    Vector3 off = Quaternion.Euler(0f, Rs(25f, 60f) * (tw % 2 == 0 ? 1f : -1f), 0f) * Vector3.right;
                    Stick(mb, fork, fork + (off + Vector3.up * Rs(0.1f, 0.5f)).normalized * Rs(0.2f, 0.4f), thick * 0.5f);
                }
                scatterModels.Add(("Stick " + sv, mb, false));
            }
            // Dry grass: a tussock standing through the snow, a spray of blades. Three kinds (stiff upright blades,
            // arching grass, seed stalks) at four heights, placed at 0.7 to 1.3 times their size
            int firstGrass = scatterModels.Count;
            for (int gv = 0; gv < 3 * GrassHeights; gv++)
            {
                var gb = new MeshBuilder();
                int kind = gv % 3;
                float tallest = new[] { 0.2f, 0.34f, 0.48f, 0.62f }[gv / 3];
                Vector3 foot = new Vector3(0f, -0.04f, 0f);
                int blades = random.Next(24, 36);      // a full clump of thin blades, not a few flat ones
                if (kind == 1)
                {
                    for (int bl = 0; bl < blades; bl++)
                    {
                        float a = Rs(0f, Mathf.PI * 2f), tall = tallest * Rs(0.4f, 0.9f);
                        var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var edge = new Vector3(-outward.z, 0f, outward.x) * Rs(0.008f, 0.016f);
                        Vector3 root0 = foot + outward * Rs(0f, 0.08f);
                        Vector3 bend = root0 + Vector3.up * tall * 0.75f + outward * tall * Rs(0.2f, 0.45f);
                        Vector3 tip = bend + Vector3.down * tall * Rs(0.2f, 0.5f) + outward * tall * Rs(0.35f, 0.6f);
                        Color blade = Color.Lerp(straw, dryStraw, Rs(0f, 1f));
                        Color bleached = Color.Lerp(blade, new Color(0.92f, 0.86f, 0.7f), 0.45f);
                        gb.TriColors(root0 - edge, root0 + edge, bend + edge * 0.6f, blade * 0.55f, blade * 0.55f, blade);
                        gb.TriColors(root0 - edge, bend + edge * 0.6f, bend - edge * 0.6f, blade * 0.55f, blade, blade);
                        gb.TriColors(bend - edge * 0.6f, bend + edge * 0.6f, tip, blade, blade, bleached);
                    }
                }
                else if (kind == 2)
                {
                    int stalks = random.Next(8, 14);
                    for (int bl = 0; bl < stalks; bl++)
                    {
                        float a = Rs(0f, Mathf.PI * 2f), tall = tallest * Rs(0.9f, 1.5f);
                        var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var edge = new Vector3(-outward.z, 0f, outward.x) * 0.006f;
                        Vector3 root0 = foot + outward * Rs(0f, 0.12f), tip = root0 + Vector3.up * tall + outward * tall * Rs(0.05f, 0.3f);
                        Color stalk = Color.Lerp(straw, dryStraw, Rs(0f, 1f)) * 0.8f;
                        gb.TriColors(root0 - edge, root0 + edge, tip, stalk * 0.6f, stalk * 0.6f, stalk);
                        // a seed head: a slim pale tassel drooping from the top (a dark diamond read as a cut-out)
                        Vector3 droopTo = tip + ((tip - root0).normalized * 0.04f + outward * 0.07f + Vector3.down * 0.05f), side = edge * 1.6f;
                        Color seed = Color.Lerp(stalk, new Color(0.85f, 0.78f, 0.62f), 0.5f);
                        gb.TriColors(tip - side, tip + side, droopTo, seed, seed, seed * 0.9f);
                    }
                }
                else
                {
                    for (int bl = 0; bl < blades; bl++)
                    {
                        float a = Rs(0f, Mathf.PI * 2f), lean = Rs(0.15f, 1.3f), tall = tallest * Rs(0.35f, 1f);
                        var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var edge = new Vector3(-outward.z, 0f, outward.x) * Rs(0.006f, 0.014f);
                        Vector3 root0 = foot + outward * Rs(0f, 0.1f), tip = root0 + Vector3.up * tall + outward * (tall * lean);
                        Color blade = Color.Lerp(straw, dryStraw, Rs(0f, 1f));
                        gb.TriColors(root0 - edge, root0 + edge, tip, blade * 0.55f, blade * 0.55f, Color.Lerp(blade, new Color(0.92f, 0.86f, 0.7f), 0.45f));      // dark at the root, bleached at the tip; drawn from both sides (Cull off)
                    }
                }
                scatterModels.Add(("Grass " + gv, gb, true));
            }
            void PlaceStone(Vector2 at, float radius)
            {
                scatterPlaces.Add((firstStone + random.Next(StoneModels), World(at, 0f), Quaternion.Euler(0f, Rs(0f, 360f), 0f), radius));
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
                        int rings = length > 2.6f ? 6 : 5;
                        const int segments = 18;      // (round: a ten-sided drift showed its straight edges at a walker's feet)
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
                                float soft = (1f - t * t) * (1f - t * t);      // a heap runs out flat into the ground, with no lip
                                if (r == rings) lumpRims[region].Add(lv.Count);
                                lv.Add(On(q, tall * (ridge ? c * Mathf.Sqrt(c) : soft) - (r == rings ? 0.07f : 0f)));
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
                    float patch = Mathf.PerlinNoise(p.x * 0.05f + 5.9f, p.y * 0.05f + 77.3f);
                    // the wrack line: a band of driftwood and pebbles where the last high water left them
                    bool wrack = onSand && height[k] > 0.4f && height[k] < 0.8f;
                    float tuftChance = wrack ? 0.03f : (onSand ? 0.1f : 0.07f + 0.26f * Smooth((patch - 0.48f) / 0.2f) * (height[k] < 45f ? 1f : 0.3f)) * byPath;
                    if (roll2 < tuftChance)
                    {
                        // dry grass: a few tussocks standing through the snow
                        int clumps = random.Next(2, 6);
                        for (int c = 0; c < clumps; c++)
                        {
                            Vector2 at = p + new Vector2(Rs(-0.9f, 0.9f), Rs(-0.9f, 0.9f));
                            scatterPlaces.Add((firstGrass + random.Next(3 * GrassHeights), World(at, 0f), Quaternion.Euler(0f, Rs(0f, 360f), 0f), Rs(0.7f, 1.3f)));
                            tufts++;
                        }
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.4f : onSand ? 0.12f : 0.2f * byPath) && slope < 0.3f)
                    {
                        // a fallen stick, half sunk, with a twig or two; its top takes the snow
                        // (each end lies on the ground where it is, so a stick never juts out of a slope or hovers over a dip)
                        float a = Rs(0f, Mathf.PI * 2f), length = Rs(0.4f, 1.4f);
                        var lie = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length * 0.5f;
                        Vector3 a0 = World(p - lie, 0.005f), a1 = World(p + lie, 0.005f + Rs(0f, 0.05f));
                        Vector3 stickAlong = a1 - a0;
                        var stickFlat = new Vector3(stickAlong.x, 0f, stickAlong.z);
                        // (yaw then pitch, so a model's twigs stay on its upper side)
                        Quaternion turn = Quaternion.LookRotation(stickFlat.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(0f, 0f, Mathf.Atan2(stickAlong.y, stickFlat.magnitude) * Mathf.Rad2Deg);
                        scatterPlaces.Add((firstStick + random.Next(StickModels), (a0 + a1) * 0.5f, turn, stickAlong.magnitude));
                        sticks++;
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.55f : onSand ? 0.2f : 0.085f * byPath))
                    {
                        // a stone's top showing, its crown catching snow
                        PlaceStone(p, Rs(0.06f, 0.17f));      // small: anything bigger is the game's own textured rock
                        stones++;
                    }
                    else if (roll2 < tuftChance + (wrack ? 0.85f : onSand ? 0.32f : 0.105f * byPath))
                    {
                        // a patch of pebbles
                        int pebbles = random.Next(5, 12);
                        for (int pb = 0; pb < pebbles; pb++) PlaceStone(p + new Vector2(Rs(-0.7f, 0.7f), Rs(-0.7f, 0.7f)), Rs(0.04f, 0.11f));
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
            // The models ride in the pack switched off, as the firs' do ("Scatter Models"); their materials are set
            // up for instancing, which keeps the shader's instanced variants in the pack. Nothing here casts a shadow:
            // at their size it is not missed.
            litterMaterial.enableInstancing = true; grassMaterial.enableInstancing = true;
            var scatterHolder = new GameObject("Scatter Models");
            scatterHolder.transform.SetParent(b.Root.transform, false);
            scatterHolder.transform.localPosition = new Vector3(landing.x - root.x, -60f - root.y, landing.y - root.z);
            scatterHolder.SetActive(false);
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Puzzles/WinterIsle/Meshes" }))
            {
                string oldMesh = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);      // the merged meshes of earlier builds
                string oldName = System.IO.Path.GetFileNameWithoutExtension(oldMesh);
                if (oldName.StartsWith("Grass ") || oldName.StartsWith("Litter ")) UnityEditor.AssetDatabase.DeleteAsset(oldMesh);
            }
            for (int sm = 0; sm < scatterModels.Count; sm++)
            {
                var go = new GameObject("Scatter Model " + scatterModels[sm].name);
                go.transform.SetParent(scatterHolder.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = b.SaveMesh("Scatter " + scatterModels[sm].name, scatterModels[sm].mesh.ToMesh());
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = scatterModels[sm].grass ? grassMaterial : litterMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // WinterIsle.scatter: "BWSC", version, the models' names, then each place: model, position, turn, scale
            string scatterFile = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.scatter"));
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(scatterFile)))
            {
                w.Write(new[] { (byte)'B', (byte)'W', (byte)'S', (byte)'C' }); w.Write(1);
                w.Write(scatterModels.Count);
                foreach (var model in scatterModels) { w.Write(model.name); w.Write(model.grass); }
                w.Write(scatterPlaces.Count);
                foreach (var place in scatterPlaces)
                {
                    w.Write((byte)place.model);
                    w.Write(place.at.x); w.Write(place.at.y); w.Write(place.at.z);
                    w.Write((short)Mathf.RoundToInt(place.turn.x * 32767f)); w.Write((short)Mathf.RoundToInt(place.turn.y * 32767f));
                    w.Write((short)Mathf.RoundToInt(place.turn.z * 32767f)); w.Write((short)Mathf.RoundToInt(place.turn.w * 32767f));
                    w.Write(place.scale);
                }
            }
            Debug.Log("[BWM] Winter Isle scatter: " + lumps + " drifts, " + tufts + " tussocks, " + sticks + " sticks, " + stones + " stones and pebbles; " + scatterModels.Count + " models and " + scatterPlaces.Count + " places written to " + scatterFile + "; drifts in " + lumpVerts.Count + " districts");
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
                var floeSnow = new Color(0.97f, 0.98f, 1f);
                Vector3 at = new Vector3(p.x - root.x, -root.y, p.y - root.z);
                Vector2 rock = new Vector2(Rf(-5f, 5f), Rf(-5f, 5f));
                Kit.AddFloe(ice, at, radius, Rf(0.12f, 0.3f), rock, tries, iceColor, floeSnow);
                // (no floe pushed up onto another: from the air a small plate on a larger one read as a ring)
                floes++;
            }
            Debug.Log("[BWM] Winter Isle: " + coveList.Count + " coves, " + barList.Count + " sand bar, " + islets.Count + " islets, " + floes + " ice floes");
        }
        // shore ice: a ledge of ice frozen onto the shore all along the water's edge, broken into plates a few metres
        // across (snow ending straight in open water read as a hard edge)
        {
            var shoreRandom = new System.Random(4471);
            float Rs(float lo, float hi) => lo + (hi - lo) * (float)shoreRandom.NextDouble();
            var shoreSnow = new Color(0.96f, 0.97f, 1f);
            int shoreIce = 0;
            for (int sk = 0; sk < count && shoreIce < 12000; sk++)
            {
                float hk = height[sk];
                if (float.IsNaN(hk) || hk > 0.1f || hk < -0.9f) continue;
                int si = sk % row, sj = sk / row;
                bool byLand = false;
                for (int odj = -1; odj <= 1 && !byLand; odj++)
                    for (int odi = -1; odi <= 1; odi++)
                    {
                        int ni = si + odi, nj = sj + odj;
                        if (ni < 0 || nj < 0 || ni > nx || nj > nz) continue;
                        float hn = height[nj * row + ni];
                        if (!float.IsNaN(hn) && hn > 0.35f) { byLand = true; break; }
                    }
                if (!byLand || Rs(0f, 1f) > 0.72f) continue;
                Vector2 sp = At(sk) + new Vector2(Rs(-1.2f, 1.2f), Rs(-1.2f, 1.2f));
                if ((sp - Lake).magnitude < LakeRadius + 12f) continue;
                Kit.AddFloe(ice, new Vector3(sp.x - root.x, 0.02f - root.y, sp.y - root.z), Rs(1.3f, 3.2f), Rs(0.05f, 0.12f), new Vector2(Rs(-1.5f, 1.5f), Rs(-1.5f, 1.5f)), sk, iceColor, shoreSnow);
                shoreIce++;
            }
            Debug.Log("[BWM] Winter Isle: " + shoreIce + " plates of shore ice");
        }
        b.MeshPiece("Ice", ice.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Ice", m => { m.SetFloat("_SnowLine", 1.02f); m.SetFloat("_SnowNoise", 0.9f); m.SetFloat("_SnowEdge", 0.05f); }), false);

        // 9. Our own trees: firs, slim spruces and bare birches (WinterIsleDressing.TreeModels), each model with a
        // lighter version for distance; every such tree on the island is one of them, turned and scaled a little
        // (their textures: tools/treetex.py, a 4096 px atlas of snow-laden sprays and 2048 px tiling bark)
        Material firMaterial = b.ShaderMaterial("BWM/Fir", "Fir", m => Kit.SetTreeMaps(m, "FirBark"));
        Material birchMaterial = b.ShaderMaterial("BWM/Fir", "Birch", m => Kit.SetTreeMaps(m, "BirchBark"));
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
            models.Add((b.SaveMesh("Fir " + v, near.ToMesh(true)), b.SaveMesh("Fir " + v + " far", far.ToMesh(true)), tall, kind == "birch" ? birchMaterial : firMaterial));
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
                // the far version from about 300 m (a 15 m fir at the game's Very High LOD bias, 1.5), gone past about
                // 2.6 km: the near one's needle sprays cost most of the island's frame time (2026-11-22: 137 fps at the
                // east lowland with the old 0.012 / 0.002, 149 at these; the far one now reads the same at that range)
                lods[l] = new LOD(l == 0 ? 0.045f : 0.0075f, new Renderer[] { rend });
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
                    if (Piste(p) > 0.2f && MountainHeight(p) > 4f) { next += 40f; continue; }      // the pistes have their own review (tools/review/mountain-shots.sh)
                    views.Append(Sdk.F(p.x)).Append('\t').Append(Sdk.F(p.y)).Append('\t').Append(Sdk.F(dir.x)).Append('\t').Append(Sdk.F(dir.y)).Append('\n');
                    next += 130f;
                }
                run += seg;
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "world", "winter-viewpoints.tsv")), views.ToString());
        Debug.Log("[BWM] Winter Isle: " + cabins.Count + " cabins, " + lanterns + " lanterns, " + poles + " snow poles");

        // 11. The gondola: two stations, pylons and the cable between, and the cabins. Winter Walk moves the cabins
        //     (GondolaDriver): it reads the cable's line from the "Gondola Line" markers, P0 at the bottom station.
        //     Until The Tree's five plinths hold gourds they wait in the bottom station.
        {
            Vector2 dir2 = (GondolaTop - GondolaBottom).normalized, side2 = new Vector2(dir2.y, -dir2.x);
            float bottomDeck = HeightAt(GondolaBottom), topDeck = HeightAt(GondolaTop);
            const float CableOverDeck = 5.6f, Clearance = 7.5f;
            float length = (GondolaTop - GondolaBottom).magnitude;
            int spans = Mathf.Max(2, Mathf.CeilToInt(length / 80f));      // long spans: the line is nearly two kilometres
            // cable points: the stations, and a pylon top at each span's end, raised until every point of the cable
            // between clears the ground (and the trees beside it) by Clearance
            var line = new List<Vector3>();
            for (int i = 0; i <= spans; i++)
            {
                Vector2 p = Vector2.Lerp(GondolaBottom, GondolaTop, (float)i / spans);
                float y = i == 0 ? bottomDeck + CableOverDeck : i == spans ? topDeck + CableOverDeck : HeightAt(p) + 21f;      // (towers as tall as a real gondola's: at 12 m they looked squat for the spans)
                line.Add(new Vector3(p.x, y, p.y));
            }
            for (int pass = 0; pass < 20; pass++)
            {
                bool raised = false;
                for (int i = 0; i < spans; i++)
                    for (float t = 0.05f; t < 1f; t += 0.05f)
                    {
                        Vector3 c = Vector3.Lerp(line[i], line[i + 1], t);
                        float need = HeightAt(new Vector2(c.x, c.z)) + Clearance - c.y;
                        if (need <= 0f) continue;
                        if (i > 0) line[i] += Vector3.up * (need + 0.5f);
                        if (i + 1 < spans) line[i + 1] += Vector3.up * (need + 0.5f);
                        raised = true;
                    }
                if (!raised) break;
            }
            var markers = new GameObject("Gondola Line");
            markers.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < line.Count; i++)
            {
                var m = new GameObject("P" + i);
                m.transform.SetParent(markers.transform, false);
                m.transform.position = line[i];
            }

            var gm = new MeshBuilder();
            var red = new Color(0.72f, 0.09f, 0.08f); var green = new Color(0.1f, 0.33f, 0.18f); var gold = new Color(0.95f, 0.72f, 0.2f);
            var steel = new Color(0.32f, 0.33f, 0.36f); var timber = new Color(0.42f, 0.29f, 0.18f); var cream = new Color(0.9f, 0.86f, 0.76f);
            Vector3 L(Vector2 p, float y) => new Vector3(p.x - root.x, y - root.y, p.y - root.z);
            float yaw2 = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;
            // pylons: a tapering steel mast with a crossarm carrying both lines, a ladder of rungs, a lamp
            for (int i = 1; i < spans; i++)
            {
                Vector2 p = new Vector2(line[i].x, line[i].z);
                float foot = HeightAt(p) - 0.6f, mastTop = line[i].y + 0.6f;
                Kit.AddBox(gm, L(p, foot), new Vector3(2.6f, 0.8f, 2.6f), cream * 0.8f, yaw2);                // footing
                // mast: tapering in two lengths, with a collar between, as a tubular lift tower does
                float split = foot + 0.8f + (mastTop - foot - 0.8f) * 0.58f;
                Kit.AddBox(gm, L(p, foot + 0.8f), new Vector3(2.2f, split - foot - 0.8f, 2.2f), steel * 0.75f, yaw2);
                Kit.AddBox(gm, L(p, split - 0.3f), new Vector3(2.5f, 0.45f, 2.5f), steel * 0.6f, yaw2);
                Kit.AddBox(gm, L(p, split), new Vector3(1.45f, mastTop - split, 1.45f), steel * 0.75f, yaw2);
                Kit.AddBox(gm, L(p, mastTop), new Vector3(0.5f, 0.5f, 0.5f) + new Vector3(0f, 0f, 0f), steel, yaw2);
                Vector2 armA = p + side2 * (GondolaSide + 0.6f), armB = p - side2 * (GondolaSide + 0.6f);
                Kit.AddBox(gm, L(Vector2.Lerp(armA, armB, 0.5f), mastTop + 0.2f), new Vector3(2f * GondolaSide + 4.4f, 1.1f, 1.3f), red, yaw2 + 90f);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    Kit.AddBox(gm, L(p + side2 * GondolaSide * sgn, mastTop - 0.15f), new Vector3(0.8f, 0.5f, 3.4f), red, yaw2);    // sheave trains
                for (float y = foot + 1.5f; y < mastTop - 1f; y += 0.6f)
                    Kit.AddBox(gm, L(p + dir2 * 0.4f, y), new Vector3(0.45f, 0.05f, 0.05f), steel * 1.2f, yaw2 + 90f);       // rungs
                Kit.AddBall(gm, L(p, mastTop + 0.75f), 0.22f, gold);
                // a wreath on the mast, facing down the line
                Vector3 wreathAt = L(p - dir2 * 1.15f, split + 1.2f);
                for (int k = 0; k < 12; k++)
                {
                    float wa = 2f * Mathf.PI * k / 12f;
                    Vector3 off = new Vector3(side2.x, 0f, side2.y) * (Mathf.Cos(wa) * 0.55f) + Vector3.up * (Mathf.Sin(wa) * 0.55f);
                    Kit.AddBall(gm, wreathAt + off, 0.16f, green);
                }
                Kit.AddBall(gm, wreathAt + Vector3.down * 0.55f, 0.13f, red);
            }
            // stations: a timber platform under a pitched roof, open at the ends, the bullwheel overhead, garlands
            void Station(Vector2 at, float deck, bool bottom)
            {
                float w = 2f * GondolaSide + 4.2f, d = 13f, roofY = deck + CableOverDeck + 1.2f;
                Kit.AddBox(gm, L(at, deck - 1.2f), new Vector3(w + 1f, 1.2f, d + 1f), cream * 0.75f, yaw2);       // plinth
                Kit.AddBox(gm, L(at, deck), new Vector3(w, 0.25f, d), timber, yaw2);                              // deck
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        Kit.AddBox(gm, L(at + side2 * (w * 0.5f - 0.3f) * cx + dir2 * (d * 0.5f - 0.4f) * cz, deck + 0.25f), new Vector3(0.45f, roofY - deck - 0.25f, 0.45f), timber, yaw2);
                // roof: two slopes meeting over the middle
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector2 at2 = at + side2 * (w * 0.27f) * sgn;
                    for (int seg = 0; seg < 4; seg++)
                    {
                        Vector3 c = L(at2, roofY + 0.9f - 0.45f * Mathf.Abs(sgn));
                        Kit.AddBox(gm, c + Quaternion.Euler(0f, yaw2, 0f) * new Vector3(0f, 0f, (seg - 1.5f) * (d + 1.6f) / 4f), new Vector3(w * 0.6f, 0.25f, (d + 1.6f) / 4f + 0.05f), seg % 2 == 0 ? red : red * 0.9f, yaw2);
                    }
                }
                Kit.AddBox(gm, L(at, roofY + 1.25f), new Vector3(0.5f, 0.4f, d + 1.8f), gold, yaw2);              // ridge
                // the bullwheel at the far end, where the cable turns
                Vector2 wheel = at + dir2 * (d * 0.5f) * (bottom ? -1f : 1f);
                Kit.AddDisc(gm, L(wheel, deck + CableOverDeck - 0.1f), GondolaSide + 0.3f, 0.3f, steel, 20);
                // a sign board over the boarding side, and wreaths on the posts
                Kit.AddBox(gm, L(at - dir2 * (d * 0.5f - 0.4f) * (bottom ? -1f : 1f), roofY - 0.9f), new Vector3(w * 0.7f, 0.8f, 0.12f), green, yaw2);
                for (int cx = -1; cx <= 1; cx += 2)
                    Kit.AddBall(gm, L(at + side2 * (w * 0.5f - 0.3f) * cx - dir2 * (d * 0.5f - 0.1f) * (bottom ? -1f : 1f), deck + 2.1f), 0.32f, green);
            }
            Station(GondolaBottom, bottomDeck, true);
            Station(GondolaTop, topDeck, false);
            // lights round both stations' eaves
            foreach (var (at, deck, label) in new[] { (GondolaBottom, bottomDeck, "Bottom"), (GondolaTop, topDeck, "Top") })
            {
                float w = 2f * GondolaSide + 4.2f, d = 13f, eave = deck + CableOverDeck + 1.2f + 0.45f;
                var ring = new List<Vector3>();
                foreach (var (cx, cz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, -1f) })
                {
                    Vector2 q = at + side2 * ((w * 0.6f + 0.2f) * cx) + dir2 * ((d * 0.5f + 0.8f) * cz);
                    ring.Add(new Vector3(q.x - root.x, eave - root.y, q.y - root.z));
                }
                Kit.LightString(b, "Gondola " + label + " Station Lights", ring, 0.25f, 0.4f, false, false, label.Length);
            }
            // the cables: thin dark boxes run each line (and round the bullwheels)
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int i = 0; i < spans; i++)
                {
                    Vector3 a = line[i] + new Vector3(side2.x, 0f, side2.y) * GondolaSide * sgn, c = line[i + 1] + new Vector3(side2.x, 0f, side2.y) * GondolaSide * sgn;
                    Vector3 cableMid = (a + c) * 0.5f, run = c - a;
                    var turn = Quaternion.LookRotation(run.normalized, Vector3.up);
                    // a box from a to c, 6 cm thick
                    Vector3 u = turn * Vector3.right * 0.07f, v = turn * Vector3.up * 0.07f;
                    Vector3 a0 = a - root, c0 = c - root;
                    gm.Quad(a0 - u - v, c0 - u - v, c0 - u + v, a0 - u + v, steel * 0.5f);
                    gm.Quad(a0 + u + v, c0 + u + v, c0 + u - v, a0 + u - v, steel * 0.5f);
                    gm.Quad(a0 - u + v, c0 - u + v, c0 + u + v, a0 + u + v, steel * 0.5f);
                    gm.Quad(a0 + u - v, c0 + u - v, c0 - u - v, a0 - u - v, steel * 0.5f);
                }
            b.MeshPiece("Gondola", gm.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true);

            // cabins: an open-fronted car on a hanger, red with gold trim and a wreath, two chairlift seats facing
            // forward. Built facing +z with the grip at the origin; Winter Walk turns and moves them.
            var car = new MeshBuilder();
            const float Drop = 3.6f;      // grip to floor
            Kit.AddBox(car, new Vector3(0f, -Drop, 0f), new Vector3(1.9f, 0.12f, 1.5f), timber);                        // floor
            Kit.AddBox(car, new Vector3(0f, -Drop + 0.12f, -0.68f), new Vector3(1.9f, 1.75f, 0.12f), red);              // back wall
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Kit.AddBox(car, new Vector3(0.9f * sgn, -Drop + 0.12f, -0.1f), new Vector3(0.12f, 0.75f, 1.3f), red);  // low sides
                Kit.AddBox(car, new Vector3(0.9f * sgn, -Drop + 0.87f, 0.68f), new Vector3(0.1f, 1.0f, 0.1f), gold);   // front posts
                Kit.AddBox(car, new Vector3(0.9f * sgn, -Drop + 0.87f, -0.62f), new Vector3(0.1f, 1.0f, 0.1f), gold);
            }
            Kit.AddBox(car, new Vector3(0f, -Drop + 1.87f, 0f), new Vector3(2.1f, 0.14f, 1.7f), red);                   // roof
            Kit.AddBox(car, new Vector3(0f, -Drop + 2.01f, 0f), new Vector3(1.7f, 0.1f, 1.3f), gold);
            Kit.AddBox(car, new Vector3(0f, -Drop + 0.12f, -0.45f), new Vector3(1.7f, 0.42f, 0.42f), timber * 1.15f); // bench
            Kit.AddBox(car, new Vector3(0f, -Drop + 2.11f, -0.05f), new Vector3(0.14f, Drop - 2.11f, 0.14f), steel); // hanger
            Kit.AddBox(car, new Vector3(0f, -0.25f, -0.05f), new Vector3(0.3f, 0.35f, 0.5f), steel * 0.8f);         // grip
            Kit.AddBox(car, new Vector3(0f, -Drop + 0.5f, 0.66f), new Vector3(1.8f, 0.06f, 0.06f), gold);           // safety bar
            Kit.AddDisc(car, new Vector3(0f, -Drop + 1.2f, -0.75f), 0.3f, 0.06f, green, 12);                      // wreath (lies flat on the wall's back)
            Kit.AddBall(car, new Vector3(0f, -Drop + 0.98f, -0.78f), 0.07f, red);
            Mesh carMesh = b.SaveMesh("Gondola Cabin", car.ToMesh());
            Material carMaterial = b.ShaderMaterial("BWM/Snowy", "Cabin", m => m.SetFloat("_SnowLine", 0.8f));
            var cabinsRoot = new GameObject("Gondola Cabins");
            cabinsRoot.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < GondolaCabins; i++)
            {
                var cabin = new GameObject("Gondola Cabin " + i);
                cabin.transform.SetParent(cabinsRoot.transform, false);
                // parked in the bottom station until Winter Walk takes them
                cabin.transform.position = new Vector3(GondolaBottom.x, bottomDeck + Drop + 0.3f, GondolaBottom.y) + new Vector3(dir2.x, 0f, dir2.y) * ((i - GondolaCabins / 2f) * 0.2f) + Vector3.up * i * 0.01f;
                cabin.transform.rotation = Quaternion.Euler(0f, yaw2, 0f);
                var body = new GameObject("Body");
                body.transform.SetParent(cabin.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = carMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = carMaterial;
                body.AddComponent<MeshCollider>().sharedMesh = carMesh;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var seat = new Piece(new GameObject(sgn < 0 ? "Seat Left" : "Seat Right"));
                    seat.Transform.SetParent(cabin.transform, false);
                    seat.Transform.localPosition = new Vector3(0.42f * sgn, -Drop + 0.54f, -0.45f);
                    b.AddScript(seat, "PuzzleSeat");
                    seat.Set("PuzzleSeat", "pose", sgn < 0 ? 12 : 13);      // ChairliftLeft, ChairliftRight
                    seat.Set("PuzzleSeat", "clickSize", new Vector3(0.7f, 0.7f, 0.7f));
                }
            }
            Debug.Log("[BWM] Winter Isle gondola: " + Sdk.F(length) + " m, " + (spans - 1) + " pylons, cable from " + Sdk.F(line[0].y) + " to " + Sdk.F(line[spans].y) + " m, " + GondolaCabins + " cabins");
        }

        // 11b. The ski lift: a chairlift from beside the lodge up to the bench at the tree line. Built as the gondola
        //      is (a line of markers "Ski Lift Line" P0.., pylons, two stations, the cable, and chairs that Winter Walk
        //      moves round the loop), but plainer and lower: single steel towers, open two-seat chairs on a hanger.
        //      (Chairlift builds one; the summit lift, from the railway's Summit station up to the crest, is a second)
        void Chairlift(string liftName, Vector2 liftFrom, Vector2 liftTo, int chairCount)
        {
            Vector2 dir3 = (liftTo - liftFrom).normalized, side3 = new Vector2(dir3.y, -dir3.x);
            float bottomDeck = HeightAt(liftFrom) + 0.5f, topDeck = HeightAt(liftTo) + 0.5f;
            const float CableOver = 4.6f, Clear = 5.5f;
            float length = (liftTo - liftFrom).magnitude;
            int spans = Mathf.Max(2, Mathf.CeilToInt(length / 55f));
            var line = new List<Vector3>();
            for (int i = 0; i <= spans; i++)
            {
                Vector2 p = Vector2.Lerp(liftFrom, liftTo, (float)i / spans);
                float y = i == 0 ? bottomDeck + CableOver : i == spans ? topDeck + CableOver : HeightAt(p) + 11f;
                line.Add(new Vector3(p.x, y, p.y));
            }
            for (int pass = 0; pass < 20; pass++)
            {
                bool raised = false;
                for (int i = 0; i < spans; i++)
                    for (float t = 0.05f; t < 1f; t += 0.05f)
                    {
                        Vector3 c = Vector3.Lerp(line[i], line[i + 1], t);
                        float need = HeightAt(new Vector2(c.x, c.z)) + Clear - c.y;
                        if (need <= 0f) continue;
                        if (i > 0) line[i] += Vector3.up * (need + 0.4f);
                        if (i + 1 < spans) line[i + 1] += Vector3.up * (need + 0.4f);
                        raised = true;
                    }
                if (!raised) break;
            }
            var markers = new GameObject(liftName + " Line");
            markers.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < line.Count; i++)
            {
                var m = new GameObject("P" + i);
                m.transform.SetParent(markers.transform, false);
                m.transform.position = line[i];
            }
            var lm = new MeshBuilder();
            var steel = new Color(0.32f, 0.33f, 0.36f); var yellow = new Color(0.95f, 0.75f, 0.12f); var timber = new Color(0.42f, 0.29f, 0.18f);
            var red = new Color(0.72f, 0.09f, 0.08f); var green = new Color(0.1f, 0.33f, 0.18f); var cream = new Color(0.9f, 0.86f, 0.76f);
            Vector3 L(Vector2 p, float y) => new Vector3(p.x - root.x, y - root.y, p.y - root.z);
            float yaw3 = Mathf.Atan2(dir3.x, dir3.y) * Mathf.Rad2Deg;
            // towers: one round steel post (eight-sided), a yellow crossarm with the sheave trains at both ends
            for (int i = 1; i < spans; i++)
            {
                Vector2 p = new Vector2(line[i].x, line[i].z);
                float foot = HeightAt(p) - 0.4f, towerTop = line[i].y + 0.5f;
                Kit.AddBox(lm, L(p, foot), new Vector3(1.6f, 0.6f, 1.6f), cream * 0.8f, yaw3);
                Kit.AddBox(lm, L(p, foot + 0.6f), new Vector3(0.75f, towerTop - foot - 0.6f, 0.75f), steel, yaw3 + 22.5f);
                Kit.AddBox(lm, L(p, foot + 0.6f), new Vector3(0.75f, towerTop - foot - 0.6f, 0.75f), steel, yaw3 + 67.5f);
                Kit.AddBox(lm, L(p, towerTop), new Vector3(2f * SkiLiftSide + 1.6f, 0.45f, 0.5f), yellow, yaw3 + 90f);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    Kit.AddBox(lm, L(p + side3 * SkiLiftSide * sgn, towerTop - 0.35f), new Vector3(0.35f, 0.3f, 2.4f), yellow * 0.85f, yaw3);
            }
            // stations: a deck with a ramp down at the boarding end, posts and a flat roof over the bullwheel
            void Station(Vector2 at, float deck, bool bottom)
            {
                float w = 2f * SkiLiftSide + 3.4f, d = 9f, roofY = deck + CableOver + 1f;
                Kit.AddBox(lm, L(at, deck - 1.6f), new Vector3(w + 0.6f, 1.6f, d + 0.6f), cream * 0.75f, yaw3);
                Kit.AddBox(lm, L(at, deck), new Vector3(w, 0.2f, d), timber, yaw3);
                Vector2 rampAt = at - dir3 * (d * 0.5f + 1.6f) * (bottom ? 1f : -1f);
                Kit.AddBox(lm, L(rampAt, deck - 0.9f), new Vector3(w - 1f, 0.9f, 3.2f), cream * 0.7f, yaw3);      // a ramp of snow up to the deck
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        Kit.AddBox(lm, L(at + side3 * (w * 0.5f - 0.25f) * cx + dir3 * (d * 0.5f - 0.3f) * cz, deck + 0.2f), new Vector3(0.35f, roofY - deck - 0.2f, 0.35f), steel, yaw3);
                Kit.AddBox(lm, L(at, roofY), new Vector3(w + 0.8f, 0.3f, d + 0.8f), red, yaw3);
                Kit.AddBox(lm, L(at, roofY + 0.3f), new Vector3(w + 0.2f, 0.12f, d + 0.2f), green, yaw3);
                Vector2 wheel = at + dir3 * (d * 0.5f - 1f) * (bottom ? -1f : 1f);
                Kit.AddDisc(lm, L(wheel, deck + CableOver - 0.1f), SkiLiftSide + 0.25f, 0.25f, steel, 18);
                Kit.AddBox(lm, L(at - dir3 * (d * 0.5f) * (bottom ? 1f : -1f), roofY - 0.8f), new Vector3(w * 0.8f, 0.7f, 0.1f), green, yaw3);      // the lift's sign board
            }
            Station(liftFrom, bottomDeck, true);
            Station(liftTo, topDeck, false);
            foreach (var (at, deck, label) in new[] { (liftFrom, bottomDeck, "Bottom"), (liftTo, topDeck, "Top") })
            {
                float w = 2f * SkiLiftSide + 3.4f, d = 9f, eave = deck + CableOver + 1f + 0.2f;
                var ring = new List<Vector3>();
                foreach (var (cx, cz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, -1f) })
                {
                    Vector2 q = at + side3 * ((w * 0.5f + 0.4f) * cx) + dir3 * ((d * 0.5f + 0.4f) * cz);
                    ring.Add(new Vector3(q.x - root.x, eave - root.y, q.y - root.z));
                }
                Kit.LightString(b, liftName + " " + label + " Station Lights", ring, 0.2f, 0.4f, false, false, 7 + label.Length);
            }
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int i = 0; i < spans; i++)
                {
                    Vector3 a = line[i] + new Vector3(side3.x, 0f, side3.y) * SkiLiftSide * sgn, c = line[i + 1] + new Vector3(side3.x, 0f, side3.y) * SkiLiftSide * sgn;
                    var turn = Quaternion.LookRotation((c - a).normalized, Vector3.up);
                    Vector3 u = turn * Vector3.right * 0.05f, v = turn * Vector3.up * 0.05f, a0 = a - root, c0 = c - root;
                    lm.Quad(a0 - u - v, c0 - u - v, c0 - u + v, a0 - u + v, steel * 0.5f);
                    lm.Quad(a0 + u + v, c0 + u + v, c0 + u - v, a0 + u - v, steel * 0.5f);
                    lm.Quad(a0 - u + v, c0 - u + v, c0 + u + v, a0 + u + v, steel * 0.5f);
                    lm.Quad(a0 + u - v, c0 + u - v, c0 - u - v, a0 - u - v, steel * 0.5f);
                }
            b.MeshPiece(liftName, lm.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true);

            // the chairs: an open bench for two with a backrest, a footrest and a safety bar, on a hanger to the grip
            var chair = new MeshBuilder();
            const float Drop = 2.6f;
            Kit.AddBox(chair, new Vector3(0f, -Drop, 0f), new Vector3(1.4f, 0.1f, 0.5f), red);                        // seat
            Kit.AddBox(chair, new Vector3(0f, -Drop + 0.1f, -0.25f), new Vector3(1.4f, 0.6f, 0.08f), red);            // back
            Kit.AddBox(chair, new Vector3(0f, -Drop - 0.45f, 0.4f), new Vector3(1.2f, 0.05f, 0.15f), steel);          // footrest
            Kit.AddBox(chair, new Vector3(0f, -Drop + 0.55f, 0.45f), new Vector3(1.3f, 0.05f, 0.05f), steel);         // safety bar
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Kit.AddBox(chair, new Vector3(0.68f * sgn, -Drop, 0f), new Vector3(0.05f, 0.35f, 0.05f), steel);      // arms
                Kit.AddBox(chair, new Vector3(0.68f * sgn, -Drop + 0.33f, 0.1f), new Vector3(0.05f, 0.05f, 0.45f), steel);
            }
            Kit.AddBox(chair, new Vector3(0f, -Drop + 0.7f, -0.25f), new Vector3(0.08f, Drop - 0.7f, 0.08f), steel); // hanger
            Kit.AddBox(chair, new Vector3(0f, -0.2f, -0.25f), new Vector3(0.22f, 0.28f, 0.4f), steel * 0.8f);       // grip
            Mesh chairMesh = b.SaveMesh(liftName + " Chair", chair.ToMesh());
            Material chairMaterial = b.ShaderMaterial("BWM/Snowy", "Cabin", m => m.SetFloat("_SnowLine", 0.8f));
            var chairsRoot = new GameObject(liftName + " Chairs");
            chairsRoot.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < chairCount; i++)
            {
                var liftChair = new GameObject(liftName + " Chair " + i);
                liftChair.transform.SetParent(chairsRoot.transform, false);
                liftChair.transform.position = new Vector3(liftFrom.x, bottomDeck + Drop + 0.3f, liftFrom.y) + new Vector3(dir3.x, 0f, dir3.y) * ((i - chairCount / 2f) * 0.15f) + Vector3.up * i * 0.01f;
                liftChair.transform.rotation = Quaternion.Euler(0f, yaw3, 0f);
                var body = new GameObject("Body");
                body.transform.SetParent(liftChair.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = chairMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = chairMaterial;
                body.AddComponent<MeshCollider>().sharedMesh = chairMesh;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var seat = new Piece(new GameObject(sgn < 0 ? "Seat Left" : "Seat Right"));
                    seat.Transform.SetParent(liftChair.transform, false);
                    seat.Transform.localPosition = new Vector3(0.36f * sgn, -Drop + 0.08f, -0.02f);
                    b.AddScript(seat, "PuzzleSeat");
                    seat.Set("PuzzleSeat", "pose", sgn < 0 ? 12 : 13);      // ChairliftLeft, ChairliftRight
                    seat.Set("PuzzleSeat", "clickSize", new Vector3(0.6f, 0.6f, 0.6f));
                }
            }
            Debug.Log("[BWM] Winter Isle " + liftName.ToLowerInvariant() + ": " + Sdk.F(length) + " m from " + Sdk.F(bottomDeck) + " to " + Sdk.F(topDeck) + " m, " + (spans - 1) + " towers, " + chairCount + " chairs");
        }
        Chairlift("Ski Lift", SkiLiftBottom, SkiLiftTop, SkiLiftChairs);
        if (rail)      // the summit lift: from beyond the end of the railway, up the summit's last 60 m to the crest
        {
            Vector2 liftFoot = WinterIsleRail.SummitLiftFoot();
            Chairlift("Summit Lift", liftFoot, Summit + (liftFoot - Summit).normalized * SummitLiftReach, 10);
        }

        // 11c. The terrain park: kickers of packed snow down the right-hand run below the cat track (the Big Air game's
        //      big one is further down), with flags. Each kicker's collider is named as the pistes' sheet, so Winter
        //      Walk's riding carries a skier or boarder up it and into the air off its lip.
        {
            var park = new MeshBuilder();
            var flags = new MeshBuilder();
            var white = new Color(0.94f, 0.96f, 1f);
            int kickers = 0;
            foreach (var (parkAlong, offset, lip, length, width) in new[] { (985f, -0.25f, 1.0f, 6f, 4f), (1035f, 0.25f, 1.4f, 7f, 4.5f), (1075f, -0.2f, 0.8f, 5f, 3.5f) })
            {
                Vector2 parkMid = RunCentre(2, parkAlong, out float half);
                Vector2 down = (RunCentre(2, parkAlong + 6f, out _) - parkMid).normalized, across = new Vector2(down.y, -down.x);
                Vector2 foot = parkMid + across * (offset * half), lipAt = foot + down * length;
                const int Steps = 7;
                for (int st = 0; st < Steps; st++)
                {
                    float t0 = (float)st / Steps, t1 = (float)(st + 1) / Steps;
                    float h0 = lip * t0 * t0 + 0.12f, h1 = lip * t1 * t1 + 0.12f;
                    Vector2 a0 = Vector2.Lerp(foot, lipAt, t0), a1 = Vector2.Lerp(foot, lipAt, t1);
                    Vector3 P(Vector2 q, float y) => new Vector3(q.x - root.x, HeightAt(q) + y - root.y, q.y - root.z);
                    Vector3 aL = P(a0 - across * width / 2f, h0), aR = P(a0 + across * width / 2f, h0), cL = P(a1 - across * width / 2f, h1), cR = P(a1 + across * width / 2f, h1);
                    park.Quad(aL, cL, cR, aR, white); park.Quad(aL, aR, cR, cL, white);
                    park.Quad(P(a0 - across * width / 2f, -0.3f), P(a1 - across * width / 2f, -0.3f), cL, aL, white);
                    park.Quad(P(a0 + across * width / 2f, -0.3f), aR, cR, P(a1 + across * width / 2f, -0.3f), white);
                    if (st == Steps - 1) park.Quad(P(a1 - across * width / 2f, -0.3f), cL, cR, P(a1 + across * width / 2f, -0.3f), white);
                }
                // a flag either side of the lip, red on the left, blue on the right
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector2 f = lipAt + across * (sgn * (width / 2f + 1.2f));
                    var fp = new Vector3(f.x - root.x, HeightAt(f) - root.y, f.y - root.z);
                    Kit.AddBox(flags, fp, new Vector3(0.08f, 2.4f, 0.08f), new Color(0.3f, 0.3f, 0.32f));
                    Kit.AddBox(flags, fp + Vector3.up * 1.8f + new Vector3(down.x, 0f, down.y) * 0.35f, new Vector3(0.04f, 0.5f, 0.7f), sgn < 0 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.12f, 0.35f, 0.85f), Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg);
                }
                kickers++;
            }
            // (its object is named as the pistes' sheet, which is what Winter Walk rides on; its mesh keeps its own name, or
            // the sheet's own mesh, saved under that name later, would replace it)
            b.MeshPiece("Terrain Park", park.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true).Named("Piste Sheet");
            // the half-pipe, below the Big Air game's kicker: a channel of packed snow sixty metres long down the run, its
            // floor six metres across and its walls curving up to vertical three and a half metres up, with a deck along
            // each top. Riders swing from wall to wall (Winter Walk flies them off the lips and lands them back)
            var pipe = new MeshBuilder();
            const float PipeFrom = HalfPipeAlong, PipeTo = HalfPipeAlong + 60f, PipeFloor = 3f, PipeR = 3.5f, PipeDeck = 1.6f;
            float[] pipeAcross = { -(PipeFloor + PipeR + PipeDeck), -(PipeFloor + PipeR), -(PipeFloor + PipeR * 0.95f), -(PipeFloor + PipeR * 0.8f), -(PipeFloor + PipeR * 0.55f), -(PipeFloor + PipeR * 0.25f), -PipeFloor, PipeFloor, PipeFloor + PipeR * 0.25f, PipeFloor + PipeR * 0.55f, PipeFloor + PipeR * 0.8f, PipeFloor + PipeR * 0.95f, PipeFloor + PipeR, PipeFloor + PipeR + PipeDeck };
            float PipeUp(float x)
            {
                float t = Mathf.Abs(x) - PipeFloor;
                if (t <= 0f) return 0.15f;
                if (t >= PipeR) return PipeR + 0.15f;
                return PipeR - Mathf.Sqrt(PipeR * PipeR - t * t) + 0.15f;      // a quarter circle up to vertical
            }
            Vector3 PipeAt(float pipeAlong, float x)
            {
                Vector2 pm = RunCentre(2, pipeAlong, out _);
                Vector2 pd = (RunCentre(2, pipeAlong + 2f, out _) - pm).normalized, ps = new Vector2(pd.y, -pd.x);
                Vector2 q = pm + ps * x;
                // the floor follows the run's middle line down the slope, level across (not the ground under each point)
                return new Vector3(q.x - root.x, HeightAt(pm) + PipeUp(x) - root.y, q.y - root.z);
            }
            for (float pal = PipeFrom; pal < PipeTo; pal += 2f)
            {
                for (int k = 0; k + 1 < pipeAcross.Length; k++)
                {
                    Vector3 a0 = PipeAt(pal, pipeAcross[k]), a1 = PipeAt(pal, pipeAcross[k + 1]), c0 = PipeAt(pal + 2f, pipeAcross[k]), c1 = PipeAt(pal + 2f, pipeAcross[k + 1]);
                    pipe.Quad(a0, c0, c1, a1, white); pipe.Quad(a0, a1, c1, c0, white);
                }
                // the outer faces, pd to the ground
                foreach (float edge in new[] { pipeAcross[0], pipeAcross[pipeAcross.Length - 1] })
                {
                    Vector3 t0 = PipeAt(pal, edge), t1 = PipeAt(pal + 2f, edge);
                    Vector3 b0 = t0, b1 = t1;
                    b0.y = HeightAt(new Vector2(t0.x + root.x, t0.z + root.z)) - 0.4f - root.y;
                    b1.y = HeightAt(new Vector2(t1.x + root.x, t1.z + root.z)) - 0.4f - root.y;
                    pipe.Quad(b0, b1, t1, t0, white); pipe.Quad(b0, t0, t1, b1, white);
                }
            }
            b.MeshPiece("Half Pipe", pipe.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true).Named("Piste Sheet");
            Debug.Log("[BWM] Winter Isle terrain park: " + kickers + " kickers and a half-pipe");
        }

        // 14. The coal mine under the mountain (WinterIsleMine)
        WinterIsleMine.Build(b, root, HeightAt);
        if (rail) WinterIsleRail.Build(b, root, HeightAt);      // the mountain railway
        WinterIsleAirport.Build(b, root, HeightAt);              // the airport and the summit's airstrip
        if (rail)
        {
            float nearest = float.MaxValue;
            foreach (Vector3 lp in WinterIsleRail.Line) nearest = Mathf.Min(nearest, WinterIsleAirport.Outside(new Vector2(lp.x, lp.z)));
            Debug.Log("[BWM] Winter Isle: the railway passes " + Sdk.F(nearest) + " m from the airport's ground");
        }

        // 12. The pistes' surface: an unseen sheet a hand's breadth over the snow of the three runs and the bowl, with
        //     no friction at all, so that anyone who steps onto a piste slides down it. (A spike: whether the game's
        //     player slides on it is to be seen in the game.)
        {
            var sheet = new List<Vector3>();
            var sheetTris = new List<int>();
            var corner = new Dictionary<int, int>();
            int Corner(int k)
            {
                if (corner.TryGetValue(k, out int at)) return at;
                Vector2 p = At(k);
                corner[k] = sheet.Count;
                sheet.Add(new Vector3(p.x - root.x, height[k] + 0.08f - root.y, p.y - root.z));
                return corner[k];
            }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * row + i;
                    if (!Whole(i, j) || !(height[k] > 4f) || Piste(At(k) + new Vector2(Cell, Cell) * 0.5f) < 0.7f) continue;
                    int a = Corner(k), bb = Corner(k + 1), c = Corner(k + row), d = Corner(k + row + 1);
                    sheetTris.Add(a); sheetTris.Add(c); sheetTris.Add(bb);
                    sheetTris.Add(bb); sheetTris.Add(c); sheetTris.Add(d);
                }
            var sheetMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            sheetMesh.SetVertices(sheet);
            sheetMesh.SetTriangles(sheetTris, 0);
            sheetMesh.RecalculateBounds();
            sheetMesh = b.SaveMesh("Piste Sheet", sheetMesh);
            var slick = new PhysicsMaterial("Piste") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            UnityEditor.AssetDatabase.DeleteAsset("Assets/Puzzles/WinterIsle/Piste.physicMaterial");
            UnityEditor.AssetDatabase.CreateAsset(slick, "Assets/Puzzles/WinterIsle/Piste.physicMaterial");
            var sheetGo = new GameObject("Piste Sheet");
            sheetGo.transform.SetParent(b.Root.transform, false);
            MeshCollider sheetCollider = sheetGo.AddComponent<MeshCollider>();
            sheetCollider.sharedMesh = sheetMesh;
            sheetCollider.sharedMaterial = slick;
            Debug.Log("[BWM] Winter Isle pistes: " + (sheetTris.Count / 6 * Cell * Cell / 10000f).ToString("0.0") + " ha of run");
        }

        // 13. Ski gear: a pair of skis and a snowboard as models that Winter Walk hangs under a player (SkiDriver),
        //     and two racks by the top station: walking up to one puts that gear on.
        {
            Material gear = b.ShaderMaterial("BWM/Snowy", "Gear", m => m.SetFloat("_SnowLine", 2f));
            var gearModels = new GameObject("Gear Models");
            gearModels.transform.SetParent(b.Root.transform, false);
            // a ski: a long thin plank, its tip turned up; the board: one wide plank, both ends turned up
            void Plank(MeshBuilder mb, Vector3 mid, float length, float width, Color color, bool bothEnds)
            {
                const int parts = 8;
                for (int i = 0; i < parts; i++)
                {
                    float t0 = (float)i / parts, t1 = (float)(i + 1) / parts;
                    float Lift(float t) => 0.14f * Mathf.Pow(Mathf.Clamp01((t - 0.82f) / 0.18f), 2f) + (bothEnds ? 0.14f * Mathf.Pow(Mathf.Clamp01((0.18f - t) / 0.18f), 2f) : 0f);
                    float w0 = width * (0.5f - 0.12f * Mathf.Sin(t0 * Mathf.PI)), w1 = width * (0.5f - 0.12f * Mathf.Sin(t1 * Mathf.PI));
                    Vector3 a = mid + new Vector3(-w0, Lift(t0), (t0 - 0.5f) * length), bb = mid + new Vector3(w0, Lift(t0), (t0 - 0.5f) * length);
                    Vector3 c = mid + new Vector3(-w1, Lift(t1), (t1 - 0.5f) * length), d = mid + new Vector3(w1, Lift(t1), (t1 - 0.5f) * length);
                    mb.Quad(a, c, d, bb, color);                                               // the top
                    mb.Quad(a + Vector3.down * 0.02f, bb + Vector3.down * 0.02f, d + Vector3.down * 0.02f, c + Vector3.down * 0.02f, color * 0.6f);      // the sole
                }
            }
            var skis = new MeshBuilder();
            Plank(skis, new Vector3(-0.13f, 0f, 0.1f), 1.25f, 0.09f, new Color(0.85f, 0.12f, 0.1f), false);
            Plank(skis, new Vector3(0.13f, 0f, 0.1f), 1.25f, 0.09f, new Color(0.85f, 0.12f, 0.1f), false);
            var board = new MeshBuilder();
            Plank(board, new Vector3(0f, 0f, 0f), 1.15f, 0.3f, new Color(0.1f, 0.45f, 0.8f), true);
            foreach (var (name, gearMesh) in new[] { ("Ski Model", skis.ToMesh()), ("Board Model", board.ToMesh()) })
            {
                var go = new GameObject(name);
                go.transform.SetParent(gearModels.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = b.SaveMesh(name, gearMesh);
                go.AddComponent<MeshRenderer>().sharedMaterial = gear;
                go.SetActive(false);
            }
            // the racks: a rail on two posts with gear leaning on it, a pair either side of the way out of the gondola's
            // top station, and a pair at each of the ski lift's stations (Winter Walk finds them all: "Ski Rack",
            // "Ski Rack 2", ...)
            var rackMesh = new MeshBuilder();
            void Racks(Vector2 centre, Vector2 outDir, int index)
            {
            Vector2 sideDir = new Vector2(outDir.y, -outDir.x);
            for (int which = 0; which < 2; which++)
            {
                Vector2 at = centre + sideDir * (which == 0 ? -5f : 5f);
                var foot = new Vector3(at.x - root.x, HeightAt(at) - root.y, at.y - root.z);
                float rackYaw = Mathf.Atan2(sideDir.x, sideDir.y) * Mathf.Rad2Deg;
                var rackAlong = new Vector3(sideDir.x, 0f, sideDir.y);
                Kit.AddBox(rackMesh, foot - rackAlong * 1.1f, new Vector3(0.1f, 1.2f, 0.1f), new Color(0.4f, 0.28f, 0.18f), rackYaw);
                Kit.AddBox(rackMesh, foot + rackAlong * 1.1f, new Vector3(0.1f, 1.2f, 0.1f), new Color(0.4f, 0.28f, 0.18f), rackYaw);
                Kit.AddBox(rackMesh, foot + Vector3.up * 1.05f, new Vector3(0.08f, 0.08f, 2.4f), new Color(0.4f, 0.28f, 0.18f), rackYaw + 90f);
                for (int n = 0; n < 4; n++)
                    Kit.AddBox(rackMesh, foot + rackAlong * (-0.8f + n * 0.5f) + new Vector3(outDir.x, 0f, outDir.y) * 0.12f, which == 0 ? new Vector3(0.09f, 1.3f, 0.03f) : new Vector3(0.28f, 1.2f, 0.03f),
                        which == 0 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.1f, 0.45f, 0.8f), rackYaw);
                var mark = new GameObject((which == 0 ? "Ski Rack" : "Board Rack") + (index > 0 ? " " + (index + 1) : ""));
                mark.transform.SetParent(b.Root.transform, false);
                mark.transform.position = new Vector3(at.x, HeightAt(at) + 0.75f, at.y);
            }
            }
            Vector2 faceDown = new Vector2(Mathf.Cos(SkiBearing), Mathf.Sin(SkiBearing));
            Racks(Peak + faceDown * 16f, faceDown, 0);
            Vector2 liftDir = (SkiLiftTop - SkiLiftBottom).normalized;
            Racks(SkiLiftBottom - liftDir * 13f, -liftDir, 1);
            Racks(SkiLiftTop + liftDir * 12f, liftDir, 2);
            b.MeshPiece("Gear Racks", rackMesh.ToMesh(), gear, false);

            // piste furniture: orange marker poles down both edges of every run, slalom gates down the middle run
            // (red and blue flags alternately, set to one runSide then the other), a finish arch and a lodge at the foot
            var furniture = new MeshBuilder();
            Vector3 Ground3(Vector2 q) => new Vector3(q.x - root.x, HeightAt(q) - root.y, q.y - root.z);
            // A trail map: a board on two posts under a little roof, the mountain's three runs drawn on it as stripes from
            // a white peak in their poles' colours (blue, red, black), and the ski lift's line across
            void TrailMap(Vector2 boardAt, float boardYaw)
            {
                Quaternion bt = Quaternion.Euler(0f, boardYaw, 0f);
                Vector3 boardFoot = Ground3(boardAt);
                var post = new Color(0.36f, 0.24f, 0.15f);
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(-1.7f, 0f, 0f), new Vector3(0.18f, 3.4f, 0.18f), post, boardYaw);
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(1.7f, 0f, 0f), new Vector3(0.18f, 3.4f, 0.18f), post, boardYaw);
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(0f, 1.3f, 0.12f), new Vector3(3.6f, 2.1f, 0.1f), new Color(0.93f, 0.93f, 0.9f), boardYaw);
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(0f, 3.4f, 0.12f), new Vector3(4.0f, 0.3f, 0.5f), post, boardYaw);      // a little roof
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(0f, 2.75f, 0.19f), new Vector3(0.5f, 0.35f, 0.04f), new Color(0.55f, 0.6f, 0.7f), boardYaw);
                Color[] runColours = { new Color(0.12f, 0.32f, 0.85f), new Color(0.85f, 0.15f, 0.1f), new Color(0.08f, 0.08f, 0.1f) };
                for (int rc = 0; rc < 3; rc++)
                    for (int seg = 0; seg < 5; seg++)
                    {
                        float sx = (rc - 1) * (0.25f + seg * 0.18f) + Mathf.Sin(seg * 1.7f + rc) * 0.08f;
                        Kit.AddBox(furniture, boardFoot + bt * new Vector3(sx, 2.45f - seg * 0.3f, 0.19f), new Vector3(0.09f, 0.32f, 0.04f), runColours[rc], boardYaw);
                    }
                Kit.AddBox(furniture, boardFoot + bt * new Vector3(0f, 1.42f, 0.19f), new Vector3(3.0f, 0.1f, 0.04f), new Color(0.3f, 0.3f, 0.35f), boardYaw);      // the lift's line
            }
            // ...one by the gondola's top station, facing those leaving it, and one at the ski lift's top
            Vector2 faceDownMap = new Vector2(Mathf.Cos(SkiBearing), Mathf.Sin(SkiBearing)), faceSideMap = new Vector2(faceDownMap.y, -faceDownMap.x);
            TrailMap(Peak + faceDownMap * 21f + faceSideMap * 9f, Mathf.Atan2(-faceDownMap.x, -faceDownMap.y) * Mathf.Rad2Deg);
            Vector2 liftDirMap = (SkiLiftTop - SkiLiftBottom).normalized;
            TrailMap(SkiLiftTop + liftDirMap * 13f + new Vector2(liftDirMap.y, -liftDirMap.x) * 5f, Mathf.Atan2(-liftDirMap.x, -liftDirMap.y) * Mathf.Rad2Deg);
            int markers = 0, gates = 0;
            for (int run = 0; run < 3; run++)
                for (float d = 90f; d < SkiRadius - 90f; d += d < 560f ? 22.5f : 11.25f)      // (from the top: in the bowl too, the runs need marking)      // from where the forest begins: in the open bowl they were a scatter of sticks
                {
                    if (d < 560f && run != 1) continue;      // (in the open bowl three runs' poles stood scattered over it)
                    Vector2 runMid = RunCentre(run, d, out float runHalf), runNext = RunCentre(run, d + 6f, out _);
                    Vector2 runDir = (runNext - runMid).normalized, runSide = new Vector2(runDir.y, -runDir.x);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector2 q = runMid + runSide * (sgn * (runHalf + 4f));
                        if (!(HeightAt(q) > 3f)) continue;
                        if (Groomed(q) > 0.3f) continue;      // (where two runs meet, one's edge lies inside the other: no poles across a run)
                        // coloured by the run, as a resort's are (blue, red, black), with an orange band at the top
                        Color runColour = run == 0 ? new Color(0.12f, 0.32f, 0.85f) : run == 1 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.07f, 0.07f, 0.09f);
                        Kit.AddBox(furniture, Ground3(q), new Vector3(0.14f, 2.7f, 0.14f), runColour);
                        Kit.AddBox(furniture, Ground3(q) + Vector3.up * 2.7f, new Vector3(0.15f, 0.5f, 0.15f), new Color(0.95f, 0.45f, 0.08f));
                        Kit.AddBox(furniture, Ground3(q) + Vector3.up * 3.2f, new Vector3(0.22f, 0.12f, 0.22f), new Color(0.1f, 0.1f, 0.1f));
                        markers++;
                    }
                    if (run == 1 && (Mathf.RoundToInt(d / 11.25f) % 6 == 0))
                    {
                        // a gate: two poles and a flag between them, to one runSide of the run's middle
                        int flip = ((int)(d / 90f) % 2 == 0) ? 1 : -1;
                        Color flag = flip > 0 ? new Color(0.85f, 0.1f, 0.1f) : new Color(0.1f, 0.3f, 0.85f);
                        Vector2 g0 = runMid + runSide * (flip * runHalf * 0.4f), g1 = g0 + runSide * (flip * 2.4f);
                        if (!(HeightAt(g0) > 3f)) continue;
                        float gateYaw = Mathf.Atan2(runSide.x, runSide.y) * Mathf.Rad2Deg;
                        Kit.AddBox(furniture, Ground3(g0), new Vector3(0.1f, 2.4f, 0.1f), flag);
                        Kit.AddBox(furniture, Ground3(g1), new Vector3(0.1f, 2.4f, 0.1f), flag);
                        Kit.AddBox(furniture, Ground3((g0 + g1) * 0.5f) + Vector3.up * 1.2f, new Vector3(0.05f, 1.1f, 2.4f), flag, gateYaw);
                        gates++;
                    }
                }
            // the finish: an arch across the middle run near its foot, and a lodge beside it
            {
                Vector2 runMid = RunCentre(1, SkiRadius - 110f, out float runHalf), runNext = RunCentre(1, SkiRadius - 104f, out _);
                Vector2 runDir = (runNext - runMid).normalized, runSide = new Vector2(runDir.y, -runDir.x);
                float archYaw = Mathf.Atan2(runSide.x, runSide.y) * Mathf.Rad2Deg;
                // the arch: two red towers 9 m either side of the middle (not across the whole run-out), a beam and a
                // blue banner with a white band under it
                float archHalf = Mathf.Min(runHalf * 0.8f, 9f);
                float archFoot = Mathf.Min(HeightAt(runMid - runSide * archHalf), HeightAt(runMid + runSide * archHalf)) - root.y;
                Vector3 archMid = new Vector3(runMid.x - root.x, archFoot, runMid.y - root.z);
                foreach (int sgn in new[] { -1, 1 })
                {
                    Vector2 towerAt = runMid + runSide * (sgn * archHalf);
                    Kit.AddBox(furniture, Ground3(towerAt), new Vector3(1.1f, archFoot + 7.4f - (HeightAt(towerAt) - root.y), 1.1f), new Color(0.8f, 0.12f, 0.1f));
                }
                Kit.AddBox(furniture, archMid + Vector3.up * 6.2f, new Vector3(0.6f, 1.2f, archHalf * 2f + 1.1f), new Color(0.85f, 0.1f, 0.1f), archYaw);
                Kit.AddBox(furniture, archMid + Vector3.up * 4.3f, new Vector3(0.14f, 1.9f, archHalf * 2f - 1.1f), new Color(0.12f, 0.28f, 0.72f), archYaw);
                Kit.AddBox(furniture, archMid + Vector3.up * 4.95f, new Vector3(0.2f, 0.42f, archHalf * 2f - 3.5f), new Color(0.96f, 0.96f, 0.94f), archYaw);
                Kit.AddBox(furniture, archMid + Vector3.up * 4.1f, new Vector3(0.2f, 0.18f, archHalf * 2f - 6f), new Color(0.96f, 0.96f, 0.94f), archYaw);
                // the lodge stands off the run-out's edge, a little below the arch, where the view up from the foot
                // takes it in with the arch
                Vector2 lodge = runMid + runSide * (Mathf.Min(runHalf, 24f) + 17f) + runDir * 14f;
                // the trail map, beside the deck, facing up the run to skiers arriving
                TrailMap(runMid + runSide * (Mathf.Min(runHalf, 24f) + 5f) + runDir * 2f, Mathf.Atan2(-runDir.x, -runDir.y) * Mathf.Rad2Deg);
                float lodgeYaw = Mathf.Atan2(runDir.x, runDir.y) * Mathf.Rad2Deg;
                // the lodge: a big log A-frame facing the run, a long row of lit windows, a deck with benches in front,
                // and a smaller hut beside it
                var lodgeGlow = new MeshBuilder();
                float faceRun = Mathf.Atan2(-runSide.x, -runSide.y) * Mathf.Rad2Deg;
                Vector3 lodgeFoot = Ground3(lodge);
                Kit.AddCabin(furniture, lodgeGlow, lodgeFoot, new Vector2(12f, 18f), faceRun, 4401);
                Quaternion lt = Quaternion.Euler(0f, faceRun, 0f);
                var warm = new Color(1f, 0.72f, 0.36f);
                for (int wi = -3; wi <= 3; wi++)
                {
                    if (wi == -1) continue;      // the door
                    Kit.AddGlow(lodgeGlow, lodgeFoot + lt * new Vector3(wi * 1.6f, 1.6f, 9.03f), new Vector3(1.1f, 1.2f, 0.06f), faceRun, warm, wi * 0.13f + 0.5f);
                    Kit.AddGlow(lodgeGlow, lodgeFoot + lt * new Vector3(wi * 1.0f, 4.6f, 9.03f), new Vector3(0.7f, 0.9f, 0.06f), faceRun, warm, wi * 0.17f + 0.2f);
                }
                Kit.AddBox(furniture, lodgeFoot + lt * new Vector3(0f, 0f, 12f), new Vector3(14f, 0.35f, 5.5f), new Color(0.42f, 0.29f, 0.18f), faceRun);      // the deck
                for (int bi = -1; bi <= 1; bi += 2)
                {
                    Kit.AddBox(furniture, lodgeFoot + lt * new Vector3(bi * 4f, 0.35f, 13.5f), new Vector3(3f, 0.45f, 0.5f), new Color(0.36f, 0.24f, 0.15f), faceRun);      // benches
                    Kit.AddBox(furniture, lodgeFoot + lt * new Vector3(bi * 4f, 0.8f, 13.5f), new Vector3(1.4f, 0.08f, 0.9f), new Color(0.36f, 0.24f, 0.15f), faceRun);
                }
                Kit.AddCabin(furniture, lodgeGlow, Ground3(lodge + runDir * 16f + runSide * 4f), new Vector2(5f, 6f), faceRun + 12f, 4402);
                b.MeshPiece("Lodge Windows", lodgeGlow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
            }
            b.MeshPiece("Piste Furniture", furniture.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), false);
            Debug.Log("[BWM] Winter Isle pistes: " + markers + " marker poles, " + gates + " slalom gates, a finish arch and a lodge");
        }

        b.RegisterLand(X0, Z0, Cell, nx, nz, land, blocked);

        // 9. Causeway from the mainland shore to the landing
        Vector2 across_ = ShorePoint - landing, mid = (ShorePoint + landing) / 2f;
        float yaw = Mathf.Atan2(across_.x, across_.y) * Mathf.Rad2Deg;
        // a stone embankment: a cambered top under snow, sides of grey stone sloping into the water, and a band of
        // boulders tipped along both sides at the waterline (a flat pale box read as an unfinished plane)
        {
            var cw = new MeshBuilder();
            var cwRandom = new System.Random(6151);
            float Rc(float lo, float hi) => lo + (hi - lo) * (float)cwRandom.NextDouble();
            Vector2 alongC = across_.normalized, sideC = new Vector2(alongC.y, -alongC.x);
            float length = across_.magnitude + 24f;
            Vector3 L3(Vector2 q, float y) => new Vector3(q.x - root.x, y - root.y, q.y - root.z);
            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color c) { cw.Quad(p0, p1, p2, p3, c); cw.Quad(p0, p3, p2, p1, c); }
            var snowTop = new Color(0.9f, 0.92f, 0.96f);
            int segments = Mathf.CeilToInt(length / 5f);
            float[] footL = new float[segments + 1], footR = new float[segments + 1];
            for (int si = 0; si <= segments; si++) { footL[si] = Rc(5.2f, 6.2f); footR[si] = Rc(5.2f, 6.2f); }
            for (int si = 0; si < segments; si++)
            {
                Vector2 c0 = mid + alongC * (-length / 2f + si * length / segments), c1 = mid + alongC * (-length / 2f + (si + 1) * length / segments);
                var stone = new Color(0.44f, 0.45f, 0.49f) * Rc(0.88f, 1.08f);
                // the top, in two strips meeting at a low crown
                Face(L3(c0 - sideC * 3.5f, 0.95f), L3(c0, 1.06f), L3(c1, 1.06f), L3(c1 - sideC * 3.5f, 0.95f), snowTop);
                Face(L3(c0, 1.06f), L3(c0 + sideC * 3.5f, 0.95f), L3(c1 + sideC * 3.5f, 0.95f), L3(c1, 1.06f), snowTop);
                // the sides, down into the water
                Face(L3(c0 - sideC * 3.5f, 0.95f), L3(c1 - sideC * 3.5f, 0.95f), L3(c1 - sideC * footL[si + 1], -2.4f), L3(c0 - sideC * footL[si], -2.4f), stone);
                Face(L3(c0 + sideC * 3.5f, 0.95f), L3(c0 + sideC * footR[si], -2.4f), L3(c1 + sideC * footR[si + 1], -2.4f), L3(c1 + sideC * 3.5f, 0.95f), stone);
            }
            // the ends, closed
            foreach (int end in new[] { 0, segments })
            {
                Vector2 c = mid + alongC * (-length / 2f + end * length / segments);
                Face(L3(c - sideC * 3.5f, 0.95f), L3(c, 1.06f), L3(c, -2.4f), L3(c - sideC * footL[end], -2.4f), new Color(0.42f, 0.43f, 0.47f));
                Face(L3(c, 1.06f), L3(c + sideC * 3.5f, 0.95f), L3(c + sideC * footR[end], -2.4f), L3(c, -2.4f), new Color(0.42f, 0.43f, 0.47f));
            }
            int riprap = 0;
            for (int side = -1; side <= 1; side += 2)
                for (float d = -length / 2f + 1f; d < length / 2f - 1f; d += Rc(1.2f, 2.0f))
                {
                    Vector2 q = mid + alongC * d + sideC * (side * Rc(4.2f, 5.2f));
                    Kit.AddRock(cw, L3(q, Rc(-0.7f, 0.2f)), new Vector3(Rc(0.9f, 1.9f), Rc(0.7f, 1.3f), Rc(0.9f, 1.9f)), 9000 + riprap);
                    riprap++;
                }
            b.MeshPiece("Causeway", cw.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"));
        }

        Snowfall(b);
        Snowfall(b, true);
        Veil(b, root, HeightAt);
        Garlands(b, root, HeightAt, groundMaterial);

        // Christmas lights down both sides of the causeway, a post every 9 m
        var candy = new MeshBuilder();
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
            // the posts: candy-striped, red and white bands
            foreach (Vector3 post in posts)
                for (int band = 0; band < 9; band++)
                    Kit.AddBox(candy, new Vector3(post.x, 0.95f - root.y + band * 0.27f, post.z), new Vector3(0.14f, 0.27f, 0.14f), band % 2 == 0 ? new Color(0.85f, 0.1f, 0.1f) : new Color(0.96f, 0.95f, 0.92f), band * 12f);
        }
        // the gate at the island's end of the causeway: two candy-striped towers, a beam with a wreath and a star, and
        // lights along it
        {
            Vector2 gateAt = landing + along * 3f;
            float gateYaw = Mathf.Atan2(side_.x, side_.y) * Mathf.Rad2Deg;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector2 towerAt = gateAt + side_ * (sgn * 3.9f);
                for (int band = 0; band < 16; band++)
                    Kit.AddBox(candy, new Vector3(towerAt.x - root.x, 0.95f - root.y + band * 0.3f, towerAt.y - root.z), new Vector3(0.42f, 0.3f, 0.42f), band % 2 == 0 ? new Color(0.85f, 0.1f, 0.1f) : new Color(0.96f, 0.95f, 0.92f), band * 9f);
                Kit.AddBall(candy, new Vector3(towerAt.x - root.x, 0.95f - root.y + 4.95f, towerAt.y - root.z), 0.3f, new Color(0.95f, 0.72f, 0.2f));
            }
            var beam = new Vector3(gateAt.x - root.x, 0.95f - root.y + 4.5f, gateAt.y - root.z);
            Kit.AddBox(candy, beam, new Vector3(0.35f, 0.45f, 8.4f), new Color(0.1f, 0.33f, 0.18f), gateYaw + 90f);      // the garland beam
            Vector3 wreath = beam + Vector3.up * 1.1f - new Vector3(along.x, 0f, along.y) * 0.25f;
            for (int k = 0; k < 14; k++)
            {
                float a = 2f * Mathf.PI * k / 14f;
                Vector3 offset = new Vector3(side_.x, 0f, side_.y) * (Mathf.Cos(a) * 0.75f) + Vector3.up * (Mathf.Sin(a) * 0.75f);
                Kit.AddBall(candy, wreath + offset, 0.2f, new Color(0.1f, 0.36f, 0.18f));
            }
            Kit.AddBall(candy, wreath + Vector3.down * 0.75f, 0.16f, new Color(0.85f, 0.1f, 0.1f));      // its bow
            Kit.AddStar(candy, beam + Vector3.up * 2.4f, 0.55f, 0.1f, new Color(0.95f, 0.72f, 0.2f));
            Vector2 gl = gateAt - side_ * 3.9f, gr = gateAt + side_ * 3.9f;
            Kit.LightString(b, "Gate Lights", new List<Vector3> { new Vector3(gl.x - root.x, 5.3f - root.y, gl.y - root.z), new Vector3(gr.x - root.x, 5.3f - root.y, gr.y - root.z) }, 0.4f, 0.35f, false, false, 9);
        }
        b.MeshPiece("Candy Posts and Gate", candy.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f)), true);

        // Materials Winter Walk uses at runtime on copies of the game's own objects. A material only reaches the
        // game inside a pack if something in the pack uses it, so a speck deep under the island carries them.
        var speck = new MeshBuilder();
        Kit.AddBox(speck, new Vector3(landing.x - root.x, -30f - root.y, landing.y - root.z), Vector3.one * 0.01f, Color.white);
        b.MeshPiece("Material Store", speck.ToMesh(), b.ShaderMaterial("BWM/SnowOverlay", "SnowOverlay", Kit.SetSnowMaps), false);

        b.Empty("Debug Teleport", topAt.x - root.x, topAt.y - root.z, top + 2f - root.y, false);
        Sdk.Set(b.Puzzle, "debugTeleport", b.Root.transform.Find("Debug Teleport"));

        // Meshes that are only drawn are kept on the graphics card alone. A mesh left "readable" also stays in main
        // memory in the game, a second copy. Colliders and particle systems read theirs, so those stay as they are.
        {
            var read = new HashSet<Mesh>();
            foreach (MeshCollider mc in b.Root.GetComponentsInChildren<MeshCollider>(true)) if (mc.sharedMesh != null) read.Add(mc.sharedMesh);
            foreach (ParticleSystem ps in b.Root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.shape.mesh != null) read.Add(ps.shape.mesh);
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr != null && pr.mesh != null) read.Add(pr.mesh);
            }
            int drawnOnly = 0; long drawnVertices = 0, readVertices = 0;
            var seen = new HashSet<Mesh>();
            foreach (MeshFilter mf in b.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !seen.Add(m)) continue;
                if (read.Contains(m)) { readVertices += m.vertexCount; continue; }
                if (!UnityEditor.AssetDatabase.Contains(m) || !UnityEditor.AssetDatabase.GetAssetPath(m).StartsWith("Assets/Puzzles/WinterIsle/")) continue;      // ours only
                var so = new UnityEditor.SerializedObject(m);
                var readable = so.FindProperty("m_IsReadable");
                if (readable == null) continue;
                readable.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.EditorUtility.SetDirty(m);
                drawnOnly++; drawnVertices += m.vertexCount;
            }
            Debug.Log("[BWM] Winter Isle meshes: " + drawnOnly + " only drawn (" + drawnVertices / 1000 + "k vertices, on the graphics card alone), colliders' and particles' " + readVertices / 1000 + "k vertices kept readable");
        }

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
