// The Winter Isle's coal mine: one low single-track line that runs under the whole island and comes up into the
// open at a dozen or so stations spread across it, and one great hall under the mountain's summit, a pillar of
// coal left standing in its middle. Mantrips run round the line all day, stopping at every station and in the
// hall. Part of the island's pack: WinterIslePuzzle calls PlaceStations, FindRoute and Build.
//
// The tunnels are small: 2.9 m wide and 1.75 m to the crown, enough for the people sitting in a mantrip and
// no more (on foot it is a crouch). Only the hall is cavernous. The line is found, not drawn: the stations are
// put where the land allows, joined into one round by the shortest tour, and each leg is routed under ground
// high enough to cover it. The floor follows a few metres under the surface and ramps up to it at each station
// (at most 1 in 7). Where there is too little ground overhead the tunnel runs in a stone shed, and the island's
// mesh leaves the ground out there (InCut).
//
// Nothing from the sky lights the mine. Lamps hang in some stretches and not in others, and their light is
// worked out here and stored in the mesh's colours (shader BWM/Mine). Winter Walk's MineDriver moves the mantrips
// along the line, which Build writes beside the plugin as WinterIsle.mineline, and opens the gates when the mine
// tower's five plinths hold gourds (TheTreePuzzle builds that tower at the first station; it reads where from
// WinterIsle.mine, which Build also writes).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bwm;
using UnityEngine;

public static class WinterIsleMine
{
    public const float Floor = 30f;                 // the hall's level, metres above the sea
    public const float HalfWidth = 1.45f, WallHeight = 1.25f, Crown = 1.75f;      // a tunnel: just room for a mantrip and the people sitting in it
    public const float HallRadius = 26f, RingRadius = 12f, HallHeight = 13f, PillarRadius = 5f;
    public const float StationRadius = 9f, TowerStationRadius = 24f;      // the level ground at a station (the tower's holds the mining yard too)
    public const float LowestFloor = 1.6f, Cover = 3.4f, Grade = 0.14f;
    public const int Trips = 12;

    public static Vector2 Hub => WinterIslePuzzle.Summit;
    public static readonly List<Vector2> Stations = new List<Vector2>();      // the first is the mine tower's
    public static Vector2 Tower, TowerFacing = Vector2.up;                    // where the mine tower stands, and the way from it to the track

    // the line, a closed round sampled every two metres: where the floor's middle is, the level way to its right,
    // and what is there (0 open air at a station, 1 tunnel, 2 the hall)
    static readonly List<Vector3> line = new List<Vector3>();
    static readonly List<Vector2> lineRight = new List<Vector2>();
    static readonly List<int> lineKind = new List<int>();
    static readonly List<bool> lineCut = new List<bool>();
    static readonly List<int> stops = new List<int>();
    static readonly HashSet<long> cutCells = new HashSet<long>();
    static readonly List<Vector3> lampPlaces = new List<Vector3>();
    const float Step = 2f;

    static string LandFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Land", "WinterIsle.mine"));
    static string LineFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.mineline"));

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
        }
    }
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // Where the stations go. 'fixedPlaces' are the ones the island wants (the first is the mine tower's); more are
    // added wherever the land is furthest from any station, until there are 'want'. 'ground' is the height grid
    // (NaN off the island); 'allowed' keeps them off the plaza, the lake, the puzzle sites and the pistes.
    public static void PlaceStations(IList<Vector2> fixedPlaces, int want, Func<Vector2, float> ground, Func<Vector2, bool> allowed, float x0, float z0, float x1, float z1)
    {
        Stations.Clear();
        bool Good(Vector2 p)
        {
            float h = ground(p);
            if (float.IsNaN(h) || h < 3f || !allowed(p)) return false;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f, n = ground(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f);
                if (float.IsNaN(n) || Mathf.Abs(n - h) > 4.5f) return false;
            }
            return true;
        }
        foreach (Vector2 place in fixedPlaces)
        {
            // the nearest good ground to where it was asked for
            Vector2 best = place; bool found = false;
            for (float r = 0f; r <= 60f && !found; r += 6f)
                for (int k = 0; k < 12 && !found; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    Vector2 q = place + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Good(q)) { best = q; found = true; }
                }
            bool apart = true;
            foreach (Vector2 s in Stations) if ((s - best).magnitude < 150f) apart = false;
            if ((found || Stations.Count == 0) && apart) Stations.Add(best);
        }
        while (Stations.Count < want)
        {
            Vector2 best = Vector2.zero; float bestD = 0f;
            for (float z = z0 + 30f; z < z1 - 30f; z += 12f)
                for (float x = x0 + 30f; x < x1 - 30f; x += 12f)
                {
                    var p = new Vector2(x, z);
                    float h = ground(p);
                    if (float.IsNaN(h) || h < 5f || h > 90f) continue;
                    float d = (p - Hub).magnitude * 0.7f;
                    foreach (Vector2 s in Stations) d = Mathf.Min(d, (s - p).magnitude);
                    if (d > bestD && Good(p)) { bestD = d; best = p; }
                }
            if (bestD < 170f) break;
            Stations.Add(best);
        }
    }

    // The line: the shortest round through the hall and every station, each leg routed under ground that covers
    // it. 'heightAt' is the finished ground; 'avoid' is ground a tunnel should not pass under near the surface.
    // 'shut' is ground no tunnel may pass under at all; 'ceiling' the highest a tunnel's floor may be at a point (NaN:
    // as the ground allows), which is how the line is taken under the railway wherever it crosses it.
    public static void FindRoute(Func<Vector2, float> heightAt, Func<Vector2, bool> avoid, float x0, float z0, float x1, float z1, Func<Vector2, bool> shut = null, Func<Vector2, float> ceiling = null)
    {
        // ---- the order: nearest neighbour from the hall, then uncrossed (2-opt). Node 0 is the hall ----
        var nodes = new List<Vector2> { Hub };
        nodes.AddRange(Stations);
        int n = nodes.Count;
        var order = new List<int> { 0 };
        var used = new bool[n];
        used[0] = true;
        while (order.Count < n)
        {
            int last = order[order.Count - 1], best = -1; float bestD = float.MaxValue;
            for (int k = 0; k < n; k++)
                if (!used[k] && (nodes[k] - nodes[last]).magnitude < bestD) { bestD = (nodes[k] - nodes[last]).magnitude; best = k; }
            used[best] = true; order.Add(best);
        }
        for (bool better = true; better;)
        {
            better = false;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 a = nodes[order[i]], b2 = nodes[order[i + 1]], c = nodes[order[j]], d = nodes[order[(j + 1) % n]];
                    if ((a - c).magnitude + (b2 - d).magnitude < (a - b2).magnitude + (c - d).magnitude - 0.01f) { order.Reverse(i + 1, j - i); better = true; }
                }
        }

        // ---- each leg, by the cheapest way over a 6 m grid: low ground costs dear, the sea is shut ----
        const float G = 6f;
        int gx = Mathf.CeilToInt((x1 - x0) / G) + 1, gz = Mathf.CeilToInt((z1 - z0) / G) + 1;
        var gh = new float[gx * gz];
        var bad = new bool[gx * gz];
        for (int k = 0; k < gh.Length; k++)
        {
            var p = new Vector2(x0 + (k % gx) * G, z0 + (k / gx) * G);
            gh[k] = heightAt(p);
            bad[k] = !float.IsNaN(gh[k]) && avoid(p);
        }
        var closed = new bool[gx * gz];
        var byStation = new bool[gx * gz];      // within 28 m of any station: no other leg comes this near
        for (int k = 0; k < gh.Length; k++)
        {
            if (float.IsNaN(gh[k])) continue;
            var p = new Vector2(x0 + (k % gx) * G, z0 + (k / gx) * G);
            closed[k] = shut != null && shut(p);
            foreach (Vector2 s in Stations) if ((s - p).sqrMagnitude < 28f * 28f) { byStation[k] = true; break; }
        }
        int CellOf(Vector2 p) => Mathf.Clamp(Mathf.RoundToInt((p.y - z0) / G), 0, gz - 1) * gx + Mathf.Clamp(Mathf.RoundToInt((p.x - x0) / G), 0, gx - 1);
        Vector2 PlaceOf(int k) => new Vector2(x0 + (k % gx) * G, z0 + (k / gx) * G);
        var cost = new float[gx * gz];
        var from = new int[gx * gz];
        int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
        // A leg leaves its station straight on and comes into the next one straight on: the ground behind each end
        // (within Loop of the station, and not ahead of where the leg starts or ends) costs dear, and so does coming
        // near any other station. Until 2026-11-24 a leg could turn straight back: at the station by the bridge the
        // line reversed within 4 m and the returning tunnel's shed stood on the station's own track.
        const float Loop = 34f;
        List<Vector2> Leg(Vector2 start, Vector2 end, Vector2 startStation, Vector2 startDir, Vector2 endStation, Vector2 endDir)
        {
            bool hasStart = !float.IsNaN(startStation.x), hasEnd = !float.IsNaN(endStation.x);
            int s = CellOf(start), e = CellOf(end);
            for (int k = 0; k < cost.Length; k++) { cost[k] = float.MaxValue; from[k] = -1; }
            var open = new SortedSet<(float cost, int cell)> { (0f, s) };
            cost[s] = 0f;
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top);
                if (top.cell == e) break;
                if (top.cost > cost[top.cell]) continue;
                int ci = top.cell % gx, cj = top.cell / gx;
                for (int k = 0; k < 8; k++)
                {
                    int ni = ci + di[k], nj = cj + dj[k];
                    if (ni < 0 || nj < 0 || ni >= gx || nj >= gz) continue;
                    int nk = nj * gx + ni;
                    float h = gh[nk];
                    if (float.IsNaN(h) || h < 2.6f) continue;
                    Vector2 q = PlaceOf(nk);
                    bool nearEnd = (q - start).magnitude < 40f || (q - end).magnitude < 40f;
                    if (closed[nk] && (q - start).magnitude > 24f && (q - end).magnitude > 24f) continue;
                    float weight = 1f + (nearEnd ? 0f : (h < LowestFloor + Cover + Crown ? 14f : 0f) + (bad[nk] ? 40f : 0f));
                    bool ownStart = hasStart && (q - startStation).sqrMagnitude < Loop * Loop, ownEnd = hasEnd && (q - endStation).sqrMagnitude < Loop * Loop;
                    if (ownStart && Vector2.Dot(q - startStation, startDir) < 9f) weight += 200f;
                    if (ownEnd && Vector2.Dot(q - endStation, endDir) > -9f) weight += 200f;
                    if (byStation[nk] && !ownStart && !ownEnd) weight += 200f;
                    float c2 = top.cost + (k < 4 ? 1f : 1.41421f) * weight;
                    if (c2 < cost[nk]) { cost[nk] = c2; from[nk] = top.cell; open.Add((c2, nk)); }
                }
            }
            var cells = new List<Vector2>();
            if (from[e] < 0 && e != s) return new List<Vector2> { start, end };      // no way found: a straight line, and the build will say so
            for (int k = e; k >= 0; k = from[k]) cells.Add(PlaceOf(k));
            cells.Reverse();
            var path = new List<Vector2> { start };
            for (int k = 4; k < cells.Count - 4; k += 4) path.Add(cells[k]);      // every 24 m, so the smoothing has room
            path.Add(end);
            return path;
        }

        // ---- the round as points: station, leg, station ... with the hall's ring where node 0 comes ----
        var round = new List<Vector2>();
        var stopPlaces = new List<Vector2>();
        Vector2 MouthOf(Vector2 towards) => Hub + (towards - Hub).normalized * (HallRadius + 8f);
        for (int i = 0; i < n; i++)
        {
            int here = order[i], next = order[(i + 1) % n], prev = order[(i + n - 1) % n];
            if (here == 0)
            {
                // in by the mouth facing the last station, anticlockwise round the pillar, out by the mouth facing the next
                float aIn = Mathf.Atan2(nodes[prev].y - Hub.y, nodes[prev].x - Hub.x), aOut = Mathf.Atan2(nodes[next].y - Hub.y, nodes[next].x - Hub.x);
                while (aOut < aIn + 1.2f) aOut += 2f * Mathf.PI;
                round.Add(Hub + new Vector2(Mathf.Cos(aIn), Mathf.Sin(aIn)) * 19f);
                int steps = Mathf.CeilToInt((aOut - aIn - 0.9f) / 0.2f);
                for (int k = 0; k <= steps; k++)
                {
                    float a = Mathf.Lerp(aIn + 0.45f, aOut - 0.45f, (float)k / steps);
                    round.Add(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingRadius);
                }
                float aMid = (aIn + aOut) / 2f;
                stopPlaces.Add(Hub + new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid)) * RingRadius);
                round.Add(Hub + new Vector2(Mathf.Cos(aOut), Mathf.Sin(aOut)) * 19f);
            }
            else
            {
                // straight through the station: a point either side of it on the line from the last node to the next
                Vector2 through = (nodes[next] - nodes[prev]).normalized;
                round.Add(nodes[here] - through * 11f);
                round.Add(nodes[here]);
                round.Add(nodes[here] + through * 11f);
                stopPlaces.Add(nodes[here]);
            }
            Vector2 legStart = here == 0 ? MouthOf(nodes[next]) : nodes[here] + (nodes[next] - nodes[prev]).normalized * 11f;
            int after = order[(i + 2) % n];
            Vector2 legEnd = next == 0 ? MouthOf(nodes[here]) : nodes[next] - (nodes[after] - nodes[here]).normalized * 11f;
            var none = new Vector2(float.NaN, float.NaN);
            List<Vector2> leg = Leg(legStart, legEnd,
                here == 0 ? none : nodes[here], here == 0 ? Vector2.zero : (nodes[next] - nodes[prev]).normalized,
                next == 0 ? none : nodes[next], next == 0 ? Vector2.zero : (nodes[after] - nodes[here]).normalized);
            if (here == 0) round.Add(legStart);
            for (int k = 1; k + 1 < leg.Count; k++) round.Add(leg[k]);
            if (next == 0) round.Add(legEnd);
        }
        for (int pass = 0; pass < 3; pass++)
        {
            var smooth = new List<Vector2>();
            for (int k = 0; k < round.Count; k++)
            {
                Vector2 p = round[k], q = round[(k + 1) % round.Count];
                smooth.Add(Vector2.Lerp(p, q, 0.25f));
                smooth.Add(Vector2.Lerp(p, q, 0.75f));
            }
            round = smooth;
        }
        // ...every two metres
        var flat = new List<Vector2>();
        float carry = 0f;
        for (int k = 0; k < round.Count; k++)
        {
            Vector2 p = round[k], q = round[(k + 1) % round.Count];
            float length = (q - p).magnitude;
            for (; carry < length; carry += Step) flat.Add(Vector2.Lerp(p, q, carry / Mathf.Max(length, 0.001f)));
            carry -= length;
        }
        int count = flat.Count;
        stops.Clear();
        foreach (Vector2 place in stopPlaces)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int k = 0; k < count; k++) { float d = (flat[k] - place).sqrMagnitude; if (d < bestD) { bestD = d; best = k; } }
            stops.Add(best);
        }

        // ---- what each point is, and the floor's height: the hall's level in the hall, the ground at a station,
        //      otherwise as far under the ground as covers the tunnel, never steeper than Grade ----
        var kind = new int[count];
        var right = new Vector2[count];
        var surface = new float[count];      // the lowest ground over the tunnel's width
        var floor = new float[count];
        var held = new bool[count];
        for (int k = 0; k < count; k++)
        {
            Vector2 t = (flat[(k + 1) % count] - flat[(k + count - 1) % count]).normalized;
            right[k] = new Vector2(t.y, -t.x);
            kind[k] = (flat[k] - Hub).magnitude < HallRadius - 0.6f ? 2 : 1;
            float low = float.MaxValue;
            for (int side = -1; side <= 1; side++)
            {
                float h = heightAt(flat[k] + right[k] * (side * 2.2f));
                low = Mathf.Min(low, float.IsNaN(h) ? 0f : h);
            }
            surface[k] = low;
        }
        for (int s = 0; s < stops.Count; s++)
        {
            if (kind[stops[s]] == 2) continue;
            for (int o = -4; o <= 4; o++) kind[(stops[s] + o + count) % count] = 0;
        }
        for (int k = 0; k < count; k++)
        {
            held[k] = kind[k] != 1;
            float centre = heightAt(flat[k]);
            floor[k] = kind[k] == 2 ? Floor : kind[k] == 0 ? (float.IsNaN(centre) ? surface[k] : centre) + 0.02f : Mathf.Max(LowestFloor, surface[k] - Cover);
        }
        // ...and no higher than the ceiling (under the railway); the passes below then ramp the floor down to it
        var cap = new float[count];
        int capped = 0;
        for (int k = 0; k < count; k++)
        {
            cap[k] = float.NaN;
            if (ceiling == null || kind[k] != 1) continue;
            float limit = ceiling(flat[k]);
            if (float.IsNaN(limit)) continue;
            cap[k] = Mathf.Max(LowestFloor, limit);
            if (floor[k] > cap[k]) { floor[k] = cap[k]; capped++; }
        }
        float rise = Grade * Step;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < count * 2; k++) { int i = k % count, before = (i + count - 1) % count; if (!held[i]) floor[i] = Mathf.Min(floor[i], floor[before] + rise); }
            for (int k = count * 2 - 1; k >= 0; k--) { int i = k % count, behind = (i + 1) % count; if (!held[i]) floor[i] = Mathf.Min(floor[i], floor[behind] + rise); }
        }
        for (int pass = 0; pass < 2; pass++)      // ...and up to meet a station or the hall
        {
            for (int k = 0; k < count * 2; k++) { int i = k % count, before = (i + count - 1) % count; if (!held[i]) floor[i] = Mathf.Max(floor[i], floor[before] - rise); }
            for (int k = count * 2 - 1; k >= 0; k--) { int i = k % count, behind = (i + 1) % count; if (!held[i]) floor[i] = Mathf.Max(floor[i], floor[behind] - rise); }
        }
        for (int pass = 0; pass < 3; pass++)      // round the corners of the ramps
        {
            var eased = (float[])floor.Clone();
            for (int k = 0; k < count; k++)
                if (!held[k]) eased[k] = (floor[(k + count - 1) % count] + floor[k] * 2f + floor[(k + 1) % count]) / 4f;
            floor = eased;
        }
        for (int k = 0; k < count; k++) if (!float.IsNaN(cap[k]) && floor[k] > cap[k]) floor[k] = cap[k];      // (the easing may not lift it back)
        if (ceiling != null) Debug.Log("[BWM] Winter Isle mine: the floor taken down under the railway at " + capped + " points of the line");

        line.Clear(); lineRight.Clear(); lineKind.Clear(); lineCut.Clear(); cutCells.Clear();
        for (int k = 0; k < count; k++)
        {
            line.Add(new Vector3(flat[k].x, floor[k], flat[k].y));
            lineRight.Add(right[k]);
            lineKind.Add(kind[k]);
            bool cut = kind[k] == 1 && surface[k] < floor[k] + Crown + 1.1f;
            lineCut.Add(cut);
        }
        // the ground is left out over the cuts, and a little way past each end of one
        for (int k = 0; k < count; k++)
        {
            bool near = false;
            for (int o = -1; o <= 1; o++) if (lineCut[(k + o + count) % count]) near = true;
            if (!near) continue;
            Vector2 t = new Vector2(-right[k].y, right[k].x);
            for (float along = -1f; along <= 1.01f; along += 0.5f)
                for (float lateral = -2.4f; lateral <= 2.41f; lateral += 0.4f)
                    cutCells.Add(CutKey(flat[k] + t * along + right[k] * lateral));
        }
        // the tower: beside the first station's track, on the side with the more level ground
        {
            int at = stops[StopOfStation(0, stopPlaces)];
            Vector2 here = new Vector2(line[at].x, line[at].z);
            float onRight = heightAt(here + lineRight[at] * 8f), onLeft = heightAt(here - lineRight[at] * 8f);
            float sign = Mathf.Abs(onRight - line[at].y) <= Mathf.Abs(onLeft - line[at].y) ? 1f : -1f;
            Tower = here + lineRight[at] * (sign * 8f);
            TowerFacing = -lineRight[at] * sign;
        }
    }
    static int StopOfStation(int station, List<Vector2> stopPlaces)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int k = 0; k < stopPlaces.Count; k++) { float d = (stopPlaces[k] - Stations[station]).sqrMagnitude; if (d < bestD) { bestD = d; best = k; } }
        return best;
    }

    // Ground the island keeps clear of trees and rocks: over the sheds and at the stations (place, radius)
    public static IEnumerable<(Vector2 at, float radius)> KeptClear()
    {
        for (int k = 0; k < line.Count; k += 2)
            if (lineCut[k] || lineKind[k] == 0) yield return (new Vector2(line[k].x, line[k].z), lineKind[k] == 0 ? 5.5f : 3.2f);
    }
    // The open stretch of track at every station: the line's points in the open air (y is the floor, 0.17 m under the rail)
    public static IEnumerable<Vector3> StationTrack()
    {
        for (int k = 0; k < line.Count; k++) if (lineKind[k] == 0) yield return line[k];
    }
    // Where the tunnel runs in a shed, and the height of the shed's roof: the island heaps its ground over each one
    // (a low bank of snow, not a grey strip of roof across the forest)
    public static IEnumerable<(Vector2 at, float roof)> ShedRoofs()
    {
        for (int k = 0; k < line.Count; k++)
        {
            bool near = false;
            for (int o = -1; o <= 1; o++) if (lineCut[(k + o + line.Count) % line.Count]) near = true;
            if (near) yield return (new Vector2(line[k].x, line[k].z), line[k].y + Crown + 0.55f);
        }
    }

    static long CutKey(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 1.5f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 1.5f);
    // Whether the island's ground is left out at p. Never now: the ground is heaped over the sheds instead (ShedRoofs)
    public static bool InCut(Vector2 p) => false;

    public static void Save()
    {
        File.WriteAllLines(LandFile, new[] { F(Tower.x) + " " + F(Tower.y) + " " + F(TowerFacing.x) + " " + F(TowerFacing.y) });
        var text = new StringBuilder("# the line: x y z kind(0 open, 1 tunnel, 2 hall) stop; lamps: L x y z\n");
        foreach (Vector3 lamp in lampPlaces) text.Append("L ").Append(F(lamp.x)).Append(' ').Append(F(lamp.y)).Append(' ').Append(F(lamp.z)).Append('\n');
        for (int k = 0; k < line.Count; k++)
            text.Append(F(line[k].x)).Append(' ').Append(F(line[k].y + 0.17f)).Append(' ').Append(F(line[k].z)).Append(' ').Append(lineKind[k]).Append(' ').Append(stops.Contains(k) ? 1 : 0).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(LineFile));
        File.WriteAllText(LineFile, text.ToString());
    }

    // For the other packs' builds (The Tree): where the island's last build put the mine tower
    public static bool Load()
    {
        if (!File.Exists(LandFile)) return false;
        string[] parts = File.ReadAllLines(LandFile)[0].Split(' ');
        if (parts.Length < 4) return false;
        Tower = new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
        TowerFacing = new Vector2(float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture));
        return true;
    }

    static readonly Color Rock = new Color(0.78f, 0.72f, 0.64f), Coal = new Color(0.13f, 0.13f, 0.15f), Ballast = new Color(0.55f, 0.5f, 0.45f);
    static readonly Color Timber = new Color(0.55f, 0.39f, 0.22f), Steel = new Color(0.5f, 0.5f, 0.54f), Tie = new Color(0.26f, 0.19f, 0.13f);
    static readonly Color LampGlass = new Color(1f, 0.74f, 0.4f), Stone = new Color(0.56f, 0.56f, 0.58f);

    // A tunnel's shape across: upright walls in two panels each, and a round arch of six over them
    static Vector2[] Profile(float w, float wall, float crown)
    {
        var points = new List<Vector2> { new Vector2(-w, 0f), new Vector2(-w, wall * 0.52f), new Vector2(-w, wall) };
        for (int k = 1; k < 6; k++)
        {
            float a = Mathf.PI * (1f - k / 6f);
            points.Add(new Vector2(w * Mathf.Cos(a), wall + (crown - wall) * Mathf.Sin(a)));
        }
        points.Add(new Vector2(w, wall)); points.Add(new Vector2(w, wall * 0.52f)); points.Add(new Vector2(w, 0f));
        return points.ToArray();
    }

    public static void Build(PuzzleBuilder b, Vector3 root, Func<Vector2, float> heightAt)
    {
        int count = line.Count;
        var lamps = new List<Vector3>();
        var solid = new MeshBuilder();
        Vector3 W3(Vector2 p, float up) => new Vector3(p.x, Floor + up, p.y);      // in the hall
        Vector3 Loc(Vector3 w) => w - root;
        Vector3 Side(int i) => new Vector3(lineRight[i].x, 0f, lineRight[i].y);
        Vector2 Flat(int i) => new Vector2(line[i].x, line[i].z);
        int Wrap(int i) => ((i % count) + count) % count;

        // how far along the line each point is from the open air (a station), for daylight in the tunnel mouths
        var toOpen = new float[count];
        for (int k = 0; k < count; k++) toOpen[k] = lineKind[k] == 0 ? 0f : 1e6f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < count * 2; k++) { int i = k % count; toOpen[i] = Mathf.Min(toOpen[i], toOpen[Wrap(i - 1)] + Step); }
            for (int k = count * 2 - 1; k >= 0; k--) { int i = k % count; toOpen[i] = Mathf.Min(toOpen[i], toOpen[Wrap(i + 1)] + Step); }
        }
        float Day(int i) { float d = Smooth(1f - toOpen[Wrap(i)] / 26f); return d * d; }

        // ---- lamps: by the stations and the hall, and in a stretch now and then between; dark otherwise ----
        bool LitStretch(int i) => toOpen[i] < 70f || Mathf.Repeat(i * Step, 190f) < 70f;
        var lampAt = new List<int>();
        for (int k = 0; k < count; k += 7)
            if (lineKind[k] == 1 && LitStretch(k)) { lampAt.Add(k); lamps.Add(line[k] + Side(k) * (HalfWidth - 0.35f) + Vector3.up * 1.2f); }
        var hallLamps = new List<Vector3>();
        for (int k = 0; k < 18; k++)
        {
            float a = 2f * Mathf.PI * k / 18f;
            hallLamps.Add(W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HallRadius - 0.9f), 3.7f));
        }
        for (int k = 0; k < 6; k++)
        {
            float a = 2f * Mathf.PI * (k + 0.5f) / 6f;
            hallLamps.Add(W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (PillarRadius + 0.35f), 3.6f));
        }
        lamps.AddRange(hallLamps);
        // the lamps in 24 m squares, so a point only looks at the ones near it
        var lampGrid = new Dictionary<long, List<Vector3>>();
        long LampKey(float x, float z) => ((long)Mathf.FloorToInt(x / 24f) << 32) ^ (uint)Mathf.FloorToInt(z / 24f);
        void GridLamp(Vector3 lamp)
        {
            long key = LampKey(lamp.x, lamp.z);
            if (!lampGrid.TryGetValue(key, out List<Vector3> list)) lampGrid[key] = list = new List<Vector3>();
            list.Add(lamp);
        }
        foreach (Vector3 lamp in lamps) GridLamp(lamp);
        Color Lit(Vector3 w, Color albedo, float day = 0f)
        {
            float r = 0.62f, g = 0.62f, bl = 0.64f;      // (the game's own light falls on this too; these colours are a tint)
            for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    if (!lampGrid.TryGetValue(LampKey(w.x + ox * 24f, w.z + oz * 24f), out List<Vector3> list)) continue;
                    foreach (Vector3 lamp in list)
                    {
                        float dx = lamp.x - w.x, dy = lamp.y - w.y, dz = lamp.z - w.z;
                        float d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 > 576f) continue;
                        float fade = 1f - Mathf.Sqrt(d2) / 24f;
                        float light = 0.12f * fade * fade / (1f + d2 / 5f);      // only a hint: Winter Walk puts real lights at the lamps
                        r += light; g += light * 0.74f; bl += light * 0.42f;
                    }
                }
            r += 0.34f * day; g += 0.35f * day; bl += 0.38f * day;
            // a slow, smooth change of tone from place to place (a shade of its own on every panel read as a patchwork)
            float tone = 0.9f + 0.2f * Mathf.PerlinNoise(w.x * 0.21f + w.y * 0.17f + 40f, w.z * 0.21f - w.y * 0.13f + 90f);
            return new Color(albedo.r * r * tone, albedo.g * g * tone, albedo.b * bl * tone, 1f);
        }
        var props = new MeshBuilder();        // timber, crates, lamp stems: flat colours
        var glass = new MeshBuilder();        // lamp glass: glows (shader BWM/FairyLight)
        // A quad, facing 'seen' (or away from it, with 'away'): the game's own materials draw one side only. Its
        // texture coordinates are metres, laid flat along whichever axis the quad faces.
        void Q(MeshBuilder mb, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color albedo, float day, Vector3 seen, bool away = false, bool plain = false)
        {
            Vector3 normal = Vector3.Cross(p2 - p0, p1 - p0);
            if (normal.sqrMagnitude < 1e-10f) normal = Vector3.Cross(p3 - p0, p1 - p0);
            if ((Vector3.Dot(normal, seen - (p0 + p2) * 0.5f) < 0f) != away) { Vector3 swap = p1; p1 = p3; p3 = swap; normal = -normal; }
            float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
            Vector2 UV(Vector3 p) => ay >= ax && ay >= az ? new Vector2(p.x, p.z) : ax >= az ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
            if (plain) mb.QuadUV(Loc(p0), Loc(p1), Loc(p2), Loc(p3), UV(p0), UV(p1), UV(p2), UV(p3), albedo, albedo, albedo, albedo);
            else mb.QuadUV(Loc(p0), Loc(p1), Loc(p2), Loc(p3), UV(p0), UV(p1), UV(p2), UV(p3), Lit(p0, albedo, day), Lit(p1, albedo, day), Lit(p2, albedo, day), Lit(p3, albedo, day));
        }
        void Solid(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            solid.Quad(Loc(p0), Loc(p1), Loc(p2), Loc(p3), Color.white);
            solid.Quad(Loc(p3), Loc(p2), Loc(p1), Loc(p0), Color.white);
        }
        // a box standing on 'foot' (world), its z along 'forward'; glowing ones are lamp glass
        void Box(MeshBuilder unused, Vector3 foot, Vector3 size, Vector2 forward, Color albedo, float day = 0f, float glow = 0f, float phase = 0f)
        {
            var f = new Vector3(forward.x, 0f, forward.y) * (size.z / 2f);
            var s = new Vector3(forward.y, 0f, -forward.x) * (size.x / 2f);
            Vector3 up = Vector3.up * size.y, middle = foot + up * 0.5f;
            Vector3[] low = { foot - s - f, foot + s - f, foot + s + f, foot - s + f };
            if (glow > 0.5f)
            {
                var uv = new Vector2(1f, phase);
                void G(Vector3 a, Vector3 b2, Vector3 c, Vector3 d) { glass.QuadUV(Loc(a), Loc(b2), Loc(c), Loc(d), uv, uv, uv, uv, albedo, albedo, albedo, albedo); }
                for (int k = 0; k < 4; k++) G(low[k], low[(k + 1) % 4], low[(k + 1) % 4] + up, low[k] + up);
                G(low[0] + up, low[1] + up, low[2] + up, low[3] + up);
                G(low[0], low[3], low[2], low[1]);
                return;
            }
            for (int k = 0; k < 4; k++) Q(props, low[k], low[(k + 1) % 4], low[(k + 1) % 4] + up, low[k] + up, albedo, day, middle, true);
            Q(props, low[0] + up, low[1] + up, low[2] + up, low[3] + up, albedo, day, middle, true);
            Q(props, low[0], low[3], low[2], low[1], albedo, day, middle, true);
        }
        void Lamp(MeshBuilder mb, Vector3 at, int seed)
        {
            Box(mb, at + Vector3.up * 0.1f, new Vector3(0.04f, 0.3f, 0.04f), Vector2.up, Tie);
            Box(mb, at - Vector3.up * 0.14f, new Vector3(0.2f, 0.24f, 0.2f), Vector2.up, LampGlass, 0f, 1f, Hash(5, seed, 3));
        }
        // a heap of coal: a lumpy cone on 'foot'
        void Heap(MeshBuilder mb, Vector3 foot, float radius, float height, int seed)
        {
            Vector3 top = foot + Vector3.up * height;
            for (int k = 0; k < 9; k++)
            {
                float a0 = 2f * Mathf.PI * k / 9f, a1 = 2f * Mathf.PI * (k + 1) / 9f;
                float r0 = radius * (0.75f + 0.5f * Hash(seed, k, 1)), r1 = radius * (0.75f + 0.5f * Hash(seed, (k + 1) % 9, 1));
                Vector3 p0 = foot + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r0, p1 = foot + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r1;
                Vector3 m0 = Vector3.Lerp(p0, top, 0.55f) + Vector3.up * 0.12f * Hash(seed, k, 2), m1 = Vector3.Lerp(p1, top, 0.55f) + Vector3.up * 0.12f * Hash(seed, (k + 1) % 9, 2);
                Q(mb, p0, p1, m1, m0, Coal, 0f, foot + Vector3.up * 0.2f, true);
                Q(mb, m0, m1, top, top, Coal, 0f, foot + Vector3.up * 0.2f, true);
            }
        }
        // the wall across a tube's end, between its profile and a rectangle (half-width 'wide', height 'top')
        void Bulkhead(MeshBuilder mb, Vector3 centre, Vector3 rightways, Vector2[] prof, float wide, float top, Color albedo, float day, Vector3 seen)
        {
            Vector3 V(float lateral, float y) => centre + rightways * lateral + Vector3.up * y;
            Vector3 tl = V(-wide, top), tr = V(wide, top);
            int apex = 0;
            for (int k = 1; k < prof.Length; k++) if (prof[k].y > prof[apex].y) apex = k;
            void T(Vector3 p0, Vector3 p1, Vector3 p2) { Q(mb, p0, p1, p2, p2, albedo, day, seen); Solid(p0, p1, p2, p2); }
            for (int k = 0; k < apex; k++) T(tl, V(prof[k].x, prof[k].y), V(prof[k + 1].x, prof[k + 1].y));
            T(tl, V(prof[apex].x, prof[apex].y), tr);
            for (int k = apex; k + 1 < prof.Length; k++) T(tr, V(prof[k].x, prof[k].y), V(prof[k + 1].x, prof[k + 1].y));
            T(V(-wide, 0f), V(prof[0].x, 0f), tl);
            T(V(wide, 0f), tr, V(prof[prof.Length - 1].x, 0f));
        }
        // a rock tube through 'centres' (the floor's middle at each ring), 'rights' the level way to the right at
        // each; 'open' leaves wall panels out (ring, panel); 'day' is the daylight at each ring
        void Tube(MeshBuilder mb, IList<Vector3> centres, IList<Vector3> rights, Vector2[] prof, int seed, Func<int, int, bool> open, Func<int, float> day, bool capEnd)
        {
            int n = centres.Count - 1, last = prof.Length - 1;
            bool OpenNear(int ring, int k)
            {
                if (open == null) return false;
                for (int dr = -1; dr <= 0; dr++)
                    for (int dk = -1; dk <= 0; dk++)
                    {
                        int rr = ring + dr, kk = k + dk;
                        if (rr < 0 || rr >= n || kk < 0 || kk >= last) continue;
                        if (open(rr, kk)) return true;
                    }
                return false;
            }
            Vector3 P(int ring, int k)
            {
                Vector2 pr = prof[k];
                bool plain = ring == 0 || ring == n || OpenNear(ring, k);
                float rough = plain ? 0f : (Hash(seed, ring, k) - 0.5f) * 0.1f;      // nearly true: the game's tunnels are smooth
                if (k == 0 || k == last) rough = Mathf.Abs(rough) * 0.5f;
                Vector2 outward = (pr - new Vector2(0f, 0.7f)).normalized * rough;
                if (k == 0 || k == last) outward.y = 0f;
                Vector3 along = ring < n ? centres[ring + 1] - centres[ring] : centres[ring] - centres[ring - 1];
                float slip = 0f;
                return centres[ring] + rights[ring] * (pr.x + outward.x) + Vector3.up * (pr.y + outward.y) + along.normalized * slip;
            }
            for (int ring = 0; ring < n; ring++)
            {
                float light = (day(ring) + day(ring + 1)) * 0.5f;
                for (int k = 0; k < last; k++)
                {
                    if (open != null && open(ring, k)) continue;
                    bool upright = Mathf.Abs(prof[k].x - prof[k + 1].x) < 0.01f;
                    bool seam = upright && (k == 1 || k == last - 2);
                    Color albedo = seam ? Coal : Rock;
                    Vector3 p0 = P(ring, k), p1 = P(ring + 1, k), p2 = P(ring + 1, k + 1), p3 = P(ring, k + 1);
                    Q(mb, p0, p1, p2, p3, albedo, light, centres[ring] + Vector3.up * 0.8f);
                    Solid(p0, p1, p2, p3);      // the roof too: it is low enough to bump a head on
                }
                Vector3 fl = P(ring, 0), fr = P(ring, last), fl2 = P(ring + 1, 0), fr2 = P(ring + 1, last);
                Q(mb, fl, fr, fr2, fl2, Ballast, light, centres[ring] + Vector3.up * 0.8f);
                Solid(fl, fr, fr2, fl2);
            }
            if (capEnd)
            {
                Vector3 centre = centres[n] + (centres[n] - centres[n - 1]).normalized * 0.3f + Vector3.up * 0.7f;
                for (int k = 0; k < last; k++)
                {
                    Q(mb, P(n, k), P(n, k + 1), centre, centre, Coal, 0f, centres[n - 1] + Vector3.up * 0.7f);
                    Solid(P(n, k), P(n, k + 1), centre, centre);
                }
            }
        }

        Material mineMaterial = b.ShaderMaterial("BWM/Mine", "Mine");
        Vector2[] main = Profile(HalfWidth, WallHeight, Crown), small = Profile(1.1f, 1.0f, 1.4f);
        int crossCuts = 0, sets = 0, runs = 0, sheds = 0;
        float leastCover = float.MaxValue, lowest = float.MaxValue, tunnelLength = 0f;
        var shed = new MeshBuilder();
        var mouths = new MeshBuilder();
        var gatePlaces = new List<(Vector3 at, float yaw)>();
        var hallMouths = new List<float>();      // bearings from the hub where the line comes through the hall's wall

        // ---- the tunnels: every unbroken run of tunnel points, a point of overlap into what lies at each end ----
        int start0 = 0;
        while (start0 < count && lineKind[start0] == 1) start0++;      // begin at something that is not tunnel
        if (start0 == count) start0 = 0;
        var pieces = new List<MeshBuilder>();
        for (int walked = 0; walked < count;)
        {
            int first = Wrap(start0 + walked);
            if (lineKind[first] != 1) { walked++; continue; }
            int length = 0;
            while (length < count && lineKind[Wrap(first + length)] == 1) length++;
            walked += length;
            // from the last point before the run to the first after it, so the tube meets the station or the hall
            int from = first - 1, to = first + length;
            int ringCount = to - from;
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            for (int k = from; k <= to; k++) { centres.Add(line[Wrap(k)]); rights.Add(Side(Wrap(k))); }
            // cross-cuts: a short dead end to a coal face, alternately left and right, where there is deep rock and light
            var cuts = new List<(int ring, int side)>();
            for (int ring = 30; ring < ringCount - 30; ring += 56)
            {
                int i = Wrap(from + ring);
                float over = heightAt(Flat(i)) - line[i].y;
                bool straight = Vector2.Dot(lineRight[Wrap(i - 4)], lineRight[Wrap(i + 4)]) > 0.995f;
                if (LitStretch(i) && over > 9f && straight && !lineCut[i]) cuts.Add((ring, cuts.Count % 2 == 0 ? -1 : 1));
            }
            bool Open(int ring, int k)
            {
                foreach (var cut in cuts)
                    if ((ring == cut.ring || ring == cut.ring - 1) && (cut.side < 0 ? k <= 1 : k >= main.Length - 3)) return true;
                return false;
            }
            var mb = new MeshBuilder();
            Tube(mb, centres, rights, main, 100 + runs, Open, ring => Day(from + ring), false);
            foreach (var cut in cuts)
            {
                int i = Wrap(from + cut.ring);
                Vector3 into = Side(i) * cut.side, mouth = line[i] + into * (HalfWidth - 0.05f);
                Vector3 across = new Vector3(into.z, 0f, -into.x);
                Bulkhead(mb, mouth, across, small, 2.3f, WallHeight + 0.4f, Rock * 0.9f, 0f, mouth - into + Vector3.up * 0.7f);
                var cc = new List<Vector3>(); var cr = new List<Vector3>();
                for (int k = 0; k <= 5; k++) { cc.Add(mouth + into * (k * 2f)); cr.Add(across); }
                lamps.Add(mouth + into * 5f + Vector3.up * 1.1f);
                GridLamp(mouth + into * 5f + Vector3.up * 1.1f);
                Tube(mb, cc, cr, small, 200 + crossCuts, null, ring => 0f, true);
                Heap(mb, mouth + into * 8f + across * 0.2f, 0.9f, 0.7f, 300 + crossCuts);
                Heap(mb, mouth + into * 6f - across * 0.5f, 0.5f, 0.4f, 340 + crossCuts);
                Lamp(mb, mouth + into * 5f + Vector3.up * 1.2f, 60 + crossCuts);
                crossCuts++;
            }
            // each end: a wall round the mouth, and a gate just inside a mouth that opens to the air
            foreach (bool atStart in new[] { true, false })
            {
                int end = Wrap(atStart ? from : to), inner = Wrap(atStart ? from + 1 : to - 1);
                Vector3 outwards = (line[end] - line[inner]).normalized;
                if (lineKind[end] == 2)
                {
                    Bulkhead(mb, line[end] + outwards * 0.02f, Side(end), main, 4.4f, 2.5f, Rock, 0f, line[end] + outwards * 3f + Vector3.up);
                    hallMouths.Add(Mathf.Atan2(line[end].z - Hub.y, line[end].x - Hub.x));
                }
                else
                {
                    float top = Mathf.Max(Crown + 0.6f, heightAt(Flat(inner)) + 0.4f - line[end].y);
                    Bulkhead(mouths, line[end] + outwards * 0.03f, Side(end), main, 3.3f, top, Stone * 1.05f, 1f, line[end] + outwards * 3f + Vector3.up);
                    foreach (int side in new[] { -1, 1 })
                        Box(mouths, line[end] + outwards * 0.25f + Side(end) * (side * (HalfWidth + 0.12f)), new Vector3(0.3f, WallHeight + 0.35f, 0.3f), new Vector2(outwards.x, outwards.z), Timber, 1f);
                    Box(mouths, line[end] + outwards * 0.25f + Vector3.up * (WallHeight + 0.35f), new Vector3(HalfWidth * 2f + 0.9f, 0.32f, 0.34f), new Vector2(outwards.x, outwards.z), Timber * 0.9f, 1f);
                    int gateAt = Wrap(atStart ? from + 2 : to - 2);
                    gatePlaces.Add((line[gateAt], Mathf.Atan2(outwards.x, outwards.z) * Mathf.Rad2Deg));
                }
            }
            // timber sets every six metres, a lamp on the wall under some of them
            for (int ring = 2; ring < ringCount - 1; ring += 3)
            {
                int i = Wrap(from + ring);
                Vector2 forward = new Vector2(-lineRight[i].y, lineRight[i].x);
                float light = Day(i);
                foreach (int side in new[] { -1, 1 })
                    Box(mb, line[i] + Side(i) * (side * (HalfWidth - 0.2f)), new Vector3(0.2f, WallHeight + 0.12f, 0.2f), forward, Timber * (0.8f + 0.3f * Hash(40, i, side + 1)), light);
                Box(mb, line[i] + Vector3.up * (WallHeight + 0.12f), new Vector3(HalfWidth * 2f - 0.1f, 0.2f, 0.22f), forward, Timber * (0.8f + 0.3f * Hash(41, i, 0)), light);
                sets++;
            }
            foreach (int i in lampAt)
            {
                int ring = Wrap(i - from);
                if (ring > 0 && ring < ringCount) Lamp(mb, line[i] + Side(i) * (HalfWidth - 0.35f) + Vector3.up * 1.2f, i);
            }
            // where there is too little ground overhead: a stone shed, its walls down into the ground either side
            for (int ring = 0; ring < ringCount; ring++)
            {
                int i = Wrap(from + ring), j = Wrap(i + 1);
                float over = heightAt(Flat(i)) - line[i].y - Crown;
                if (!float.IsNaN(over) && !lineCut[i]) leastCover = Mathf.Min(leastCover, over);
                lowest = Mathf.Min(lowest, line[i].y);
                tunnelLength += Step;
                if (!lineCut[i] && !lineCut[j]) continue;
                Vector3 mid = (line[i] + line[j]) * 0.5f;
                Vector3 along = line[j] - line[i];
                // (the roof lies under the bank of ground the island heaps over it: ShedRoofs)
                float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg, top = mid.y + Crown + 0.55f;
                float stretch = along.magnitude + 0.06f;
                foreach (int side in new[] { -1, 1 })
                    Kit.AddBox(shed, mid + Side(i) * (side * 2.62f) + Vector3.down * 3f - root, new Vector3(1.5f, top - mid.y + 3f, stretch), Stone * (0.9f + 0.15f * Hash(70, i, side + 1)), yaw);
                Kit.AddBox(shed, mid + Vector3.up * (Crown + 0.3f) - root, new Vector3(6.6f, top - mid.y - Crown - 0.3f, stretch), Stone * (0.92f + 0.12f * Hash(71, i, 0)), yaw);
                sheds++;
            }
            pieces.Add(mb);
            runs++;
        }
        // rock: with tangents for the game's normal map, and casting shadows both ways so the sun stays out
        Piece RockPiece(string name, MeshBuilder mb)
        {
            Mesh mesh = mb.ToMesh();
            // smooth shading: every corner takes the mean of the faces meeting there that turn less than about 50
            // degrees from its own (the arch and the walls round off; a wall's foot and a bulkhead's edge stay sharp)
            Vector3[] at = mesh.vertices, normals = mesh.normals;
            var meet = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < at.Length; v++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(at[v].x * 200f), Mathf.RoundToInt(at[v].y * 200f), Mathf.RoundToInt(at[v].z * 200f));
                if (!meet.TryGetValue(key, out List<int> list)) meet[key] = list = new List<int>();
                list.Add(v);
            }
            var smoothed = new Vector3[at.Length];
            foreach (List<int> list in meet.Values)
                foreach (int v in list)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (int o in list) if (Vector3.Dot(normals[v], normals[o]) > 0.64f) sum += normals[o];
                    smoothed[v] = sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[v];
                }
            mesh.normals = smoothed;
            mesh.RecalculateTangents();
            Piece piece = b.MeshPiece(name, mesh, mineMaterial, false);
            piece.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return piece;
        }
        for (int k = 0; k < pieces.Count; k++) RockPiece("Mine Tunnel " + (k + 1), pieces[k]);

        // ---- the hall: a round room under the summit, a pillar of coal left standing in the middle ----
        {
            var mb = new MeshBuilder();
            const int N = 72;
            float[] ringR = { HallRadius, HallRadius, HallRadius, 24f, 19f, 11f, PillarRadius * 0.6f };
            float[] ringY = { 0f, 2.2f, 4.4f, 8f, 11f, 12.6f, HallHeight };
            Vector3 HP(int seg, int ring)
            {
                seg %= N;
                float a = 2f * Mathf.PI * seg / N;
                float rr = ringR[ring] + (ring >= 1 && ring <= 5 ? (Hash(91, seg, ring) - 0.5f) * 0.7f : 0f);
                return W3(Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, ringY[ring]);
            }
            for (int seg = 0; seg < N; seg++)
            {
                float a = 2f * Mathf.PI * (seg + 0.5f) / N;
                bool mouth = false;
                foreach (float bearing in hallMouths) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, bearing * Mathf.Rad2Deg)) * Mathf.Deg2Rad * HallRadius < 2.6f) mouth = true;
                for (int ring = 0; ring < 6; ring++)
                {
                    if (ring < 1 && mouth) continue;
                    Color albedo = ring == 0 ? Coal : Rock;
                    Q(mb, HP(seg, ring), HP(seg + 1, ring), HP(seg + 1, ring + 1), HP(seg, ring + 1), albedo, 0f, W3(Hub, 4f));
                    if (ring < 2) Solid(HP(seg, ring), HP(seg + 1, ring), HP(seg + 1, ring + 1), HP(seg, ring + 1));
                }
                float[] floorR = { PillarRadius - 0.5f, 9f, 13f, 17f, 21f, HallRadius + 0.5f };
                float a0 = 2f * Mathf.PI * seg / N, a1 = 2f * Mathf.PI * (seg + 1) / N;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                for (int k = 0; k + 1 < floorR.Length; k++)
                    Q(mb, W3(Hub + d0 * floorR[k], 0f), W3(Hub + d1 * floorR[k], 0f), W3(Hub + d1 * floorR[k + 1], 0f), W3(Hub + d0 * floorR[k + 1], 0f), Ballast, 0f, W3(Hub, 4f));
                Solid(W3(Hub + d0 * floorR[0], 0f), W3(Hub + d1 * floorR[0], 0f), W3(Hub + d1 * floorR[5], 0f), W3(Hub + d0 * floorR[5], 0f));
            }
            float[] pillarY = { 0f, 3f, 6f, 10f, HallHeight };
            for (int k = 0; k < 12; k++)
            {
                float a0 = 2f * Mathf.PI * k / 12f, a1 = 2f * Mathf.PI * (k + 1) / 12f;
                for (int level = 0; level + 1 < pillarY.Length; level++)
                {
                    float r0 = PillarRadius + (Hash(94, k, level) - 0.5f) * 0.6f, r1 = PillarRadius + (Hash(94, (k + 1) % 12, level) - 0.5f) * 0.6f;
                    float r2 = PillarRadius + (Hash(94, (k + 1) % 12, level + 1) - 0.5f) * 0.6f, r3 = PillarRadius + (Hash(94, k, level + 1) - 0.5f) * 0.6f;
                    Vector3 p0 = W3(Hub + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0, pillarY[level]), p1 = W3(Hub + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r1, pillarY[level]);
                    Vector3 p2 = W3(Hub + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r2, pillarY[level + 1]), p3 = W3(Hub + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r3, pillarY[level + 1]);
                    Q(mb, p0, p1, p2, p3, Coal, 0f, W3(Hub, (pillarY[level] + pillarY[level + 1]) * 0.5f), true);
                    if (level == 0) Solid(p0, p1, p2, p3);
                }
            }
            for (int k = 0; k < hallLamps.Count; k++) Lamp(mb, hallLamps[k] + Vector3.up * 0.2f, 500 + k);
            // coal heaps and crates against the wall, clear of the mouths
            for (int k = 0; k < 12; k++)
            {
                float a = 2f * Mathf.PI * (k + 0.3f) / 12f;
                bool mouth = false;
                foreach (float bearing in hallMouths) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, bearing * Mathf.Rad2Deg)) * Mathf.Deg2Rad * HallRadius < 7f) mouth = true;
                if (mouth) continue;
                Vector2 at = Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HallRadius - 3.2f);
                if (k % 3 == 1) Box(mb, W3(at, 0f), new Vector3(1.1f, 0.9f, 1.1f), new Vector2(Mathf.Cos(a + 0.4f), Mathf.Sin(a + 0.4f)), Timber * 0.9f);
                else Heap(mb, W3(at, 0f), 1.9f + 0.6f * Hash(96, k, 0), 1.3f + 0.5f * Hash(96, k, 1), 400 + k);
            }
            RockPiece("Mine Hall", mb);
        }

        // ---- the track: two rails and sleepers all the way round ----
        float trackLength = 0f;
        {
            var mb = new MeshBuilder();
            for (int k = 0; k < count; k++)
            {
                int j = Wrap(k + 1);
                Vector3 p = line[k], q = line[j];
                float day = lineKind[k] == 0 ? 1f : lineKind[k] == 2 ? 0f : Day(k);
                foreach (float side in new[] { -0.45f, 0.45f })
                {
                    Vector3 pi = p + Side(k) * (side - 0.04f), po = p + Side(k) * (side + 0.04f), qi = q + Side(j) * (side - 0.04f), qo = q + Side(j) * (side + 0.04f);
                    Vector3 up = Vector3.up * 0.17f, lowUp = Vector3.up * 0.05f;
                    Vector3 core = p + Side(k) * side + Vector3.up * 0.1f;
                    Q(mb, pi + up, po + up, qo + up, qi + up, Steel, day, core, true);
                    Q(mb, pi + lowUp, pi + up, qi + up, qi + lowUp, Steel * 0.6f, day, core, true);
                    Q(mb, po + lowUp, po + up, qo + up, qo + lowUp, Steel * 0.6f, day, core, true);
                }
                Vector3 t = (q - p).normalized * 0.11f, w = Side(k) * 0.75f, lift = Vector3.up * 0.08f;
                foreach (float along in new[] { 0f, 0.5f })
                {
                    Vector3 c = Vector3.Lerp(p, q, along) + lift;
                    Q(mb, c - w - t, c + w - t, c + w + t, c - w + t, Tie, day, c + Vector3.up);
                }
                trackLength += (q - p).magnitude;
            }
            b.MeshPiece("Mine Track", mb.ToMesh(), mineMaterial, false);
        }

        b.MeshPiece("Mine Sheds", shed.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"));
        RockPiece("Mine Mouths", mouths);
        b.MeshPiece("Mine Props", props.ToMesh(), mineMaterial, false);
        b.MeshPiece("Mine Lamps", glass.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Mine Lamps", m => { m.SetFloat("_Twinkle", 0.1f); m.SetFloat("_Glow", 2.4f); m.SetFloat("_Speed", 2.1f); }), false);

        // ---- the gates: bars across every mouth that opens to the air, until the mine is open ----
        {
            var gate = new MeshBuilder();
            foreach (int side in new[] { -1, 1 })
                Kit.AddBox(gate, new Vector3(side * (HalfWidth - 0.2f), 0f, 0f), new Vector3(0.22f, Crown - 0.1f, 0.22f), Timber);
            for (int k = 0; k < 4; k++) Kit.AddBox(gate, new Vector3(0f, 0.15f + k * 0.4f, 0f), new Vector3(HalfWidth * 2f - 0.2f, 0.26f, 0.1f), Timber * (0.8f + 0.25f * Hash(80, k, 0)));
            Kit.AddBox(gate, new Vector3(0f, 0.6f, -0.08f), new Vector3(1f, 0.5f, 0.05f), new Color(0.85f, 0.2f, 0.15f));
            Mesh gateMesh = b.SaveMesh("Mine Gate", gate.ToMesh());
            Material gateMaterial = b.ShaderMaterial("BWM/Snowy", "Mine Timber", m => m.SetFloat("_SnowLine", 2f));
            var gates = new GameObject("Mine Gates");
            gates.transform.SetParent(b.Root.transform, false);
            for (int k = 0; k < gatePlaces.Count; k++)
            {
                var go = new GameObject("Mine Gate " + (k + 1));
                go.transform.SetParent(gates.transform, false);
                go.transform.position = gatePlaces[k].at;
                go.transform.rotation = Quaternion.Euler(0f, gatePlaces[k].yaw, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = gateMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = gateMaterial;
                go.AddComponent<MeshCollider>().sharedMesh = gateMesh;
            }
        }

        // ---- the portals: a heavy timber frame round every mouth that opens to the air, a little roof over it, and a
        //      string of Christmas lights along its lintel ----
        {
            var portal = new MeshBuilder();
            for (int k = 0; k < gatePlaces.Count; k++)
            {
                Vector3 at = gatePlaces[k].at - root;
                Quaternion turn = Quaternion.Euler(0f, gatePlaces[k].yaw, 0f);
                Vector3 right = turn * Vector3.right, back = turn * Vector3.forward;
                float postH = Crown + 0.5f;
                foreach (int side in new[] { -1, 1 })
                    Kit.AddBox(portal, at + right * (side * (HalfWidth + 0.3f)) - back * 0.2f, new Vector3(0.45f, postH, 0.45f), Timber * 0.85f, gatePlaces[k].yaw);
                Kit.AddBox(portal, at + Vector3.up * postH - back * 0.2f, new Vector3(HalfWidth * 2f + 1.3f, 0.45f, 0.5f), Timber * 0.75f, gatePlaces[k].yaw);      // the lintel
                Kit.AddBox(portal, at + Vector3.up * (postH + 0.45f) + back * 0.3f, new Vector3(HalfWidth * 2f + 1.9f, 0.14f, 1.5f), Timber * 0.6f, gatePlaces[k].yaw);      // the hood
                Kit.AddBox(portal, at + Vector3.up * (postH + 0.05f) + back * 0.08f, new Vector3(1.4f, 0.35f, 0.04f), new Color(0.9f, 0.86f, 0.76f), gatePlaces[k].yaw);      // a name board
                Vector3 l = gatePlaces[k].at + right * (HalfWidth + 0.55f) + back * 0.2f + Vector3.up * (postH + 0.3f), r = gatePlaces[k].at - right * (HalfWidth + 0.55f) + back * 0.2f + Vector3.up * (postH + 0.3f);
                Kit.LightString(b, "Portal Lights " + (k + 1), new List<Vector3> { new Vector3(l.x - root.x, l.y - root.y, l.z - root.z), new Vector3(r.x - root.x, r.y - root.y, r.z - root.z) }, 0.25f, 0.3f, false, false, 400 + k);
            }
            b.MeshPiece("Mine Portals", portal.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true);
        }

        Piece walls = b.MeshPiece("Mine Walls", solid.ToMesh(), mineMaterial);
        walls.Go.GetComponent<MeshRenderer>().enabled = false;

        // ---- the mantrips, after the user's reference (docs/reference/mantrip.jpg): a long, very low white carrier on
        //      four road wheels, a broad flat deck at the nose and a shorter one at the tail, a sunken blue well
        //      between with black seats and a steering wheel, and a chain on short posts along each side. Built
        //      facing +z; the origin is the rail's top (the wheels stand on the floor either side of the track).
        //      Winter Walk moves them (MineDriver) ----
        {
            var car = new MeshBuilder();
            var glow = new MeshBuilder();
            Color white = new Color(0.93f, 0.93f, 0.9f), blue = new Color(0.14f, 0.22f, 0.78f), black = new Color(0.07f, 0.07f, 0.08f), chain = new Color(0.62f, 0.63f, 0.66f);
            const float Down = -0.17f;      // the floor, below the rail's top
            void Wheel(Vector3 centre, float radius, float width, Color color)
            {
                const int Sides = 12;
                for (int k = 0; k < Sides; k++)
                {
                    float a0 = 2f * Mathf.PI * k / Sides, a1 = 2f * Mathf.PI * (k + 1) / Sides;
                    Vector3 r0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * radius, r1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1)) * radius;
                    Vector3 l = Vector3.left * (width / 2f), r = Vector3.right * (width / 2f);
                    car.Quad(centre + l + r0, centre + r + r0, centre + r + r1, centre + l + r1, color);
                    car.Quad(centre + l + r1, centre + r + r1, centre + r + r0, centre + l + r0, color);
                    car.Tri(centre + l, centre + l + r0, centre + l + r1, color); car.Tri(centre + l, centre + l + r1, centre + l + r0, color);
                    car.Tri(centre + r, centre + r + r1, centre + r + r0, color); car.Tri(centre + r, centre + r + r0, centre + r + r1, color);
                }
            }
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    var hub = new Vector3(sx * 0.86f, Down + 0.36f, sz * 1.95f);
                    Wheel(hub, 0.36f, 0.26f, black);
                    Wheel(hub + Vector3.right * sx * 0.04f, 0.2f, 0.24f, white);
                }
            // the nose: a broad deck over the front axle, drawn in to a blunt point; the tail: a shorter deck
            Kit.AddBox(car, new Vector3(0f, Down + 0.66f, 1.85f), new Vector3(1.96f, 0.1f, 1.3f), white);
            Kit.AddBox(car, new Vector3(0f, Down + 0.66f, 2.62f), new Vector3(1.6f, 0.1f, 0.26f), white);
            Kit.AddBox(car, new Vector3(0f, Down + 0.66f, 2.84f), new Vector3(1.05f, 0.1f, 0.2f), white);
            Kit.AddBox(car, new Vector3(0f, Down + 0.26f, 1.9f), new Vector3(1.2f, 0.4f, 1.5f), white * 0.9f);
            Kit.AddBox(car, new Vector3(0f, Down + 0.66f, -2.05f), new Vector3(1.96f, 0.1f, 0.95f), white);
            Kit.AddBox(car, new Vector3(0f, Down + 0.26f, -2.05f), new Vector3(1.2f, 0.4f, 0.95f), white * 0.9f);
            // the well: a blue floor slung low between the axles, white sides with a blue stripe
            Kit.AddBox(car, new Vector3(0f, Down + 0.16f, -0.2f), new Vector3(1.8f, 0.1f, 2.8f), blue);
            foreach (int sx in new[] { -1, 1 })
            {
                Kit.AddBox(car, new Vector3(sx * 0.93f, Down + 0.16f, -0.2f), new Vector3(0.1f, 0.5f, 2.8f), white);
                Kit.AddBox(car, new Vector3(sx * 0.985f, Down + 0.5f, -0.2f), new Vector3(0.02f, 0.09f, 2.8f), blue);
                foreach (float pz in new[] { -1.55f, -0.2f, 1.15f })
                    Kit.AddBox(car, new Vector3(sx * 0.93f, Down + 0.66f, pz), new Vector3(0.05f, 0.42f, 0.05f), white);
                foreach (float cz in new[] { -0.875f, 0.475f })
                {
                    Kit.AddBox(car, new Vector3(sx * 0.93f, Down + 0.94f, cz - 0.33f), new Vector3(0.03f, 0.03f, 0.68f), chain);
                    Kit.AddBox(car, new Vector3(sx * 0.93f, Down + 0.97f, cz + 0.33f), new Vector3(0.03f, 0.03f, 0.68f), chain);
                }
            }
            Kit.AddBox(car, new Vector3(0f, Down + 0.26f, 1.16f), new Vector3(1.8f, 0.4f, 0.1f), white);            // the well's ends
            Kit.AddBox(car, new Vector3(0f, Down + 0.26f, -1.56f), new Vector3(1.8f, 0.4f, 0.1f), white);
            foreach (float bz in new[] { -0.95f, 0.25f })
                foreach (int sx in new[] { -1, 1 })
                {
                    Kit.AddBox(car, new Vector3(sx * 0.42f, Down + 0.26f, bz), new Vector3(0.56f, 0.2f, 0.5f), black);
                    Kit.AddBox(car, new Vector3(sx * 0.42f, Down + 0.46f, bz - 0.27f), new Vector3(0.56f, 0.46f, 0.1f), black);
                }
            Kit.AddBox(car, new Vector3(-0.42f, Down + 0.26f, 0.9f), new Vector3(0.05f, 0.62f, 0.05f), black);            // the steering column and wheel
            Wheel(new Vector3(-0.42f, Down + 0.9f, 0.86f), 0.17f, 0.04f, black);
            Kit.AddBox(car, new Vector3(0.5f, Down + 0.76f, 1.5f), new Vector3(0.3f, 0.16f, 0.2f), new Color(0.85f, 0.12f, 0.1f));      // the red box on the deck
            foreach (int sx in new[] { -1, 1 })
            {
                Kit.AddGlow(glow, new Vector3(sx * 0.4f, Down + 0.46f, 2.66f), new Vector3(0.2f, 0.14f, 0.04f), 0f, new Color(1f, 0.92f, 0.7f), 0.3f);
                Kit.AddGlow(glow, new Vector3(sx * 0.45f, Down + 0.46f, -2.535f), new Vector3(0.16f, 0.1f, 0.04f), 0f, new Color(1f, 0.15f, 0.1f), 0.6f);
            }
            Mesh carMesh = b.SaveMesh("Mantrip", car.ToMesh());
            Mesh glowMesh = b.SaveMesh("Mantrip Lamps", glow.ToMesh());
            Material carMaterial = b.ShaderMaterial("BWM/Snowy", "Mantrip", m => m.SetFloat("_SnowLine", 2f));
            Material glowMaterial = b.ShaderMaterial("BWM/FairyLight", "Mantrip Lamps", m => { m.SetFloat("_Twinkle", 0.05f); m.SetFloat("_Glow", 3f); });
            var tripsRoot = new GameObject("Mine Trips");
            tripsRoot.transform.SetParent(b.Root.transform, false);
            for (int i = 0; i < Trips; i++)
            {
                var trip = new GameObject("Mantrip " + i);
                trip.transform.SetParent(tripsRoot.transform, false);
                trip.transform.position = W3(Hub + new Vector2(Mathf.Cos(i * 0.7f), Mathf.Sin(i * 0.7f)) * RingRadius, 0.17f);
                var body = new GameObject("Body");
                body.transform.SetParent(trip.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = carMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = carMaterial;
                // boxes to stand on and bump into, not the mesh: twelve moving mesh colliders would cost every frame
                foreach (var box in new[] { (new Vector3(0f, 0.04f, -0.2f), new Vector3(1.9f, 0.14f, 2.8f)), (new Vector3(0f, 0.3f, 1.95f), new Vector3(1.96f, 0.5f, 1.6f)), (new Vector3(0f, 0.3f, -2.05f), new Vector3(1.96f, 0.5f, 0.95f)),
                    (new Vector3(-0.93f, 0.25f, -0.2f), new Vector3(0.1f, 0.5f, 2.8f)), (new Vector3(0.93f, 0.25f, -0.2f), new Vector3(0.1f, 0.5f, 2.8f)) })
                {
                    BoxCollider collider = body.AddComponent<BoxCollider>();
                    collider.center = box.Item1; collider.size = box.Item2;
                }
                var lampsGo = new GameObject("Lamps");
                lampsGo.transform.SetParent(trip.transform, false);
                lampsGo.AddComponent<MeshFilter>().sharedMesh = glowMesh;
                lampsGo.AddComponent<MeshRenderer>().sharedMaterial = glowMaterial;
                int seatNumber = 0;
                foreach (float bz in new[] { -0.95f, 0.25f })
                    foreach (int sx in new[] { -1, 1 })
                    {
                        var seat = new Piece(new GameObject("Seat " + (++seatNumber)));
                        seat.Transform.SetParent(trip.transform, false);
                        seat.Transform.localPosition = new Vector3(0.42f * sx, -0.17f + 0.52f, bz + 0.04f);
                        b.AddScript(seat, "PuzzleSeat");
                        seat.Set("PuzzleSeat", "pose", sx < 0 ? 12 : 13);      // ChairliftLeft, ChairliftRight
                        seat.Set("PuzzleSeat", "clickSize", new Vector3(0.6f, 0.7f, 0.6f));
                        seat.Set("PuzzleSeat", "seatOffset", new Vector3(0f, Kit.SeatUp, 0f));
                    }
            }
        }

        lampPlaces.Clear(); lampPlaces.AddRange(lamps);
        Save();
        Debug.Log("[BWM] Winter Isle mine: " + Stations.Count + " stations and the hall on a round of " + Sdk.F(trackLength) + " m (" + Sdk.F(tunnelLength) + " m of it in " + runs + " tunnels, "
            + (sheds * Step).ToString("0") + " m under sheds), floor from " + Sdk.F(lowest) + " m to " + Sdk.F(Floor) + " m, least rock overhead " + Sdk.F(leastCover) + " m; " + crossCuts + " cross-cuts, "
            + sets + " timber sets, " + lamps.Count + " lamps, " + gatePlaces.Count + " gates, " + Trips + " mantrips");
        Yard(b, root, heightAt);
    }

    // The mining yard round the mine tower (whose headframe The Tree builds, at Tower): a coal tipple on a trestle over a
    // short siding with three loaded tubs on it, heaps of coal under snow, the lamp house with its lit windows, a sign
    // with the crossed pick and hammer, barrels and a stack of pit props, and lights strung round it all
    static void Yard(PuzzleBuilder b, Vector3 root, Func<Vector2, float> heightAt)
    {
        if (Stations.Count == 0) return;
        Vector2 away = -TowerFacing, along = new Vector2(TowerFacing.y, -TowerFacing.x);
        Vector2 Y(float out_, float side) => Tower + away * out_ + along * side;
        Vector3 G(Vector2 q, float up = 0f) => new Vector3(q.x - root.x, heightAt(q) + up - root.y, q.y - root.z);
        float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg, facing = Mathf.Atan2(TowerFacing.x, TowerFacing.y) * Mathf.Rad2Deg;
        var mb = new MeshBuilder();
        var glow = new MeshBuilder();
        Color timber = new Color(0.45f, 0.31f, 0.19f), iron = new Color(0.22f, 0.22f, 0.25f), rust = new Color(0.45f, 0.22f, 0.12f), coal = new Color(0.08f, 0.08f, 0.09f);
        // the siding: two rails on sleepers, parallel to the line, with three tubs on it
        Vector2 sidingA = Y(6f, -11f), sidingB = Y(6f, 11f);
        for (float t = 0f; t <= 1.001f; t += 1f / 22f)
            Kit.AddBox(mb, G(Vector2.Lerp(sidingA, sidingB, t), -0.05f), new Vector3(1.5f, 0.12f, 0.22f), timber * 0.8f, yaw + 90f);
        foreach (int sgn in new[] { -1, 1 })
        {
            Vector2 ra = sidingA + away * (sgn * 0.45f), rb = sidingB + away * (sgn * 0.45f);
            Kit.AddBox(mb, (G(ra, 0.07f) + G(rb, 0.07f)) * 0.5f, new Vector3(0.07f, 0.1f, (rb - ra).magnitude), iron, yaw);
        }
        for (int i = 0; i < 3; i++)
        {
            Vector2 tub = Vector2.Lerp(sidingA, sidingB, 0.2f + i * 0.17f);
            Vector3 f = G(tub, 0.12f);
            Kit.AddBox(mb, f + Vector3.up * 0.25f, new Vector3(0.95f, 0.65f, 1.4f), rust * (0.9f + 0.1f * i), yaw);      // the tub
            Kit.AddBox(mb, f + Vector3.up * 0.88f, new Vector3(0.85f, 0.18f, 1.25f), coal, yaw);                          // its load
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Kit.AddBox(mb, f + Quaternion.Euler(0f, yaw, 0f) * new Vector3(sx * 0.48f, 0f, sz * 0.45f), new Vector3(0.08f, 0.3f, 0.3f), iron, yaw);      // wheels
        }
        // the tipple: a timber trestle over the siding's far end with a hopper on it and a chute down to the tubs
        Vector2 tip = Y(6f, 8f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                Kit.AddBox(mb, G(tip + away * (sx * 1.4f) + along * (sz * 1.6f)), new Vector3(0.3f, 4.2f, 0.3f), timber, yaw);
        Kit.AddBox(mb, G(tip, 4.2f), new Vector3(3.4f, 0.25f, 3.8f), timber * 0.85f, yaw);
        Kit.AddBox(mb, G(tip, 4.45f), new Vector3(2.4f, 1.6f, 2.4f), timber * 0.75f, yaw);                       // the hopper
        Kit.AddBox(mb, G(tip, 6.05f), new Vector3(2.9f, 0.2f, 2.9f), new Color(0.55f, 0.16f, 0.13f), yaw);      // its roof
        Kit.AddBox(mb, G(tip - along * 1.6f, 2.4f), new Vector3(0.7f, 0.25f, 2.2f), iron, yaw + 25f);           // the chute
        // coal heaps, black under a cap of snow
        Material coalMaterial = b.ShaderMaterial("BWM/Snowy", "Coal", m => m.SetColor("_Tint", new Color(0.12f, 0.12f, 0.13f)));
        foreach (var (o, sd, r, h) in new[] { (11f, 6f, 3.2f, 1.8f), (12.5f, 10f, 2.4f, 1.3f), (13f, 2f, 2.0f, 1.0f) })
        {
            Vector2 at = Y(o, sd);
            Piece heap = b.MeshPiece("Coal Heap", Kit.Mound(r, h, (int)(o * 7 + sd), 8, 18), coalMaterial, true);
            heap.Transform.position = b.Root.transform.position + G(at, -0.1f);
        }
        // the lamp house: a cabin facing the line, its windows lit
        Kit.AddCabin(mb, glow, G(Y(11f, -7f), -0.05f), new Vector2(5f, 6.5f), facing, 6061);
        WinterIslePuzzle.Lamp(root + Kit.AddPorchLamp(mb, glow, G(Y(11f, -7f), -0.05f), new Vector2(5f, 6.5f), facing), 0.8f);
        // the sign: a red board on two posts by the siding, a pick and a hammer crossed on it
        Vector2 sign = Y(2.5f, -12f);
        Vector3 sf = G(sign);
        Quaternion st = Quaternion.Euler(0f, facing, 0f);
        Kit.AddBox(mb, sf + st * new Vector3(-1.1f, 0f, 0f), new Vector3(0.16f, 2.6f, 0.16f), timber, facing);
        Kit.AddBox(mb, sf + st * new Vector3(1.1f, 0f, 0f), new Vector3(0.16f, 2.6f, 0.16f), timber, facing);
        Kit.AddBox(mb, sf + st * new Vector3(0f, 1.5f, 0.1f), new Vector3(2.4f, 1.0f, 0.1f), new Color(0.6f, 0.12f, 0.1f), facing);
        Kit.AddBox(mb, sf + st * new Vector3(0f, 1.62f, 0.17f), new Vector3(0.08f, 0.75f, 0.04f), new Color(0.9f, 0.86f, 0.76f), facing + 0f);      // the handles
        Kit.AddBox(mb, sf + st * new Vector3(0f, 1.62f, 0.18f) + Vector3.up * 0.0f, new Vector3(0.75f, 0.08f, 0.04f), new Color(0.9f, 0.86f, 0.76f), facing);
        Kit.AddBox(mb, sf + st * new Vector3(-0.3f, 2.25f, 0.19f), new Vector3(0.45f, 0.12f, 0.05f), new Color(0.9f, 0.86f, 0.76f), facing);      // the pick's head
        Kit.AddBox(mb, sf + st * new Vector3(0.35f, 1.48f, 0.19f), new Vector3(0.12f, 0.3f, 0.05f), new Color(0.9f, 0.86f, 0.76f), facing);       // the hammer's
        // barrels and a stack of pit props by the lamp house
        for (int i = 0; i < 4; i++) Kit.AddDisc(mb, G(Y(8.5f, -10.5f + i * 0.7f)), 0.32f, 0.9f, i % 2 == 0 ? rust : iron, 12);
        for (int i = 0; i < 9; i++)
        {
            int row = i < 4 ? 0 : i < 7 ? 1 : 2;
            int inRow = row == 0 ? i : row == 1 ? i - 4 : i - 7;
            Vector2 prop = Y(14.5f, -4f + inRow * 0.32f + row * 0.16f);
            Kit.AddBox(mb, G(prop, 0.15f + row * 0.28f), new Vector3(0.28f, 0.28f, 3f), timber * 1.1f, facing);
        }
        for (int i = 0; i < 10; i++)      // the posts for the lights strung round the yard (below)
        {
            float a = Mathf.PI * 2f * i / 10f;
            Kit.AddBox(mb, G(Y(8f, 0f) + away * (Mathf.Cos(a) * 9f) + along * (Mathf.Sin(a) * 14f)), new Vector3(0.12f, 2.68f, 0.12f), timber);
        }
        b.MeshPiece("Mining Yard", mb.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"), true);
        b.MeshPiece("Mining Yard Windows", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        // lights strung round the yard on posts
        var ring = new List<Vector3>();
        for (int i = 0; i <= 10; i++)
        {
            float a = Mathf.PI * 2f * i / 10f;
            Vector2 q = Y(8f, 0f) + away * (Mathf.Cos(a) * 9f) + along * (Mathf.Sin(a) * 14f);
            ring.Add(new Vector3(q.x - root.x, heightAt(q) + 2.6f - root.y, q.y - root.z));      // (from the island's own heights: the framework's ground is last build's)
        }
        Kit.LightString(b, "Yard Lights", ring, 0.35f, 0.5f, false, false, 61);
    }
}
