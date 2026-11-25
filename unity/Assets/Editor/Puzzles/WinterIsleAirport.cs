// The Winter Isle's airport and the summit's airstrip, and the biplanes parked at them.
//
// The airport is on a shelf above the island's south-east shore: a 320 m runway of packed snow running north and
// south with lights along its edges, an apron on its seaward (east) side, a hangar facing the runway with three biplanes inside
// behind its doors ("Airport Gates") and a fourth outside, a little terminal, a windsock and a fuel tank. The control
// tower, with the five plinths that open the airport, is The Tree's (its tower list), at TowerSpot.
// The summit's airstrip is a timber deck on stilts running out north-west from the summit's crest over the falling
// mountainside, with two more biplanes at its inner end: take off by running off its end.
//
// The island's build levels the airport's ground (Outside) and keeps trees, rocks and puzzles off it; Winter Walk's
// PlaneDriver flies the planes ("Biplanes/Biplane Stand N/Model", the propeller "Propeller" under it).
using System;
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public static class WinterIsleAirport
{
    public static readonly Vector2 Runway = new Vector2(-870f, -1855f);
    public const float Heading = 180f;                      // the runway's bearing, degrees from north (+z) towards east (+x): it runs south
    public const float RunwayHalfLength = 160f, RunwayHalfWidth = 22f;
    public const float Level = 21f;                         // the levelled ground's height
    // the apron and the hangar's ground, in the runway's frame: u along the runway (south), v across (west; the apron
    // is on the east, at negative v)
    const float ApronU0 = -80f, ApronU1 = 75f, ApronV0 = -100f, ApronV1 = -20f;
    public const float Blend = 18f;                         // metres over which the levelled ground meets the land

    static Vector2 F => new Vector2(Mathf.Sin(Heading * Mathf.Deg2Rad), Mathf.Cos(Heading * Mathf.Deg2Rad));
    static Vector2 R => new Vector2(F.y, -F.x);
    public static Vector2 At(float u, float v) => Runway + F * u + R * v;
    static void UV(Vector2 p, out float u, out float v) { Vector2 d = p - Runway; u = Vector2.Dot(d, F); v = Vector2.Dot(d, R); }

    // How far outside the airport's levelled ground 'p' is (negative inside)
    public static float Outside(Vector2 p)
    {
        UV(p, out float u, out float v);
        float runway = Mathf.Max(Mathf.Abs(u) - RunwayHalfLength, Mathf.Abs(v) - RunwayHalfWidth);
        float apron = Mathf.Max(Mathf.Max(ApronU0 - u, u - ApronU1), Mathf.Max(ApronV0 - v, v - ApronV1));
        return Mathf.Min(runway, apron);
    }

    // The control tower (The Tree builds it, with its plinths) and the way it faces: across the apron to the runway
    public static Vector2 TowerSpot => At(58f, -48f);
    public static Vector2 TowerFacing => R;
    public static Vector2 ApronMiddle => At(-5f, -40f);

    // ---- the summit's airstrip ----
    public const float StripBearing = 308f, StripFrom = 34f, StripTo = 168f, StripHalf = 10f;
    static Vector2 StripDir => new Vector2(Mathf.Sin(StripBearing * Mathf.Deg2Rad), Mathf.Cos(StripBearing * Mathf.Deg2Rad));
    static Vector2 StripRight => new Vector2(StripDir.y, -StripDir.x);
    public static Vector2 StripAt(float r, float side) => WinterIslePuzzle.Summit + StripDir * r + StripRight * side;
    public static float StripOutside(Vector2 p)
    {
        Vector2 d = p - WinterIslePuzzle.Summit;
        float r = Vector2.Dot(d, StripDir), s = Vector2.Dot(d, StripRight);
        return Mathf.Max(Mathf.Max(StripFrom - r, r - StripTo), Mathf.Abs(s) - StripHalf);
    }

    // ---- building ----
    static readonly Color Red = new Color(0.72f, 0.1f, 0.08f), Green = new Color(0.12f, 0.36f, 0.2f), Gold = new Color(0.95f, 0.72f, 0.2f);
    static readonly Color Cream = new Color(0.92f, 0.88f, 0.78f), Dark = new Color(0.16f, 0.16f, 0.18f), Timber = new Color(0.45f, 0.32f, 0.2f);
    static readonly Color Steel = new Color(0.52f, 0.54f, 0.58f), Blue = new Color(0.16f, 0.26f, 0.5f);

    // A biplane facing +z with its wheels on the ground at the origin, into 'body' (and its propeller, about its own
    // middle, into 'prop'): a fuselage tapering to the tail, an engine and cowling, two wings with struts, a tailplane
    // and fin, the undercarriage. 7 m long, 9.2 m across; the pilot sits at (0, 1.55, 0)
    public static void AddBiplane(MeshBuilder body, MeshBuilder prop, Color hull, Color wings, Color trim)
    {
        // the fuselage: sections from the cockpit back to the tail, narrowing and rising
        for (int k = 0; k < 8; k++)
        {
            float z0 = 1.5f - k * 0.75f, t = k / 7f;
            float w = Mathf.Lerp(1.05f, 0.3f, t), h = Mathf.Lerp(1.0f, 0.4f, t), y = Mathf.Lerp(0.9f, 1.5f, t);
            Kit.AddBox(body, new Vector3(0f, y, z0 - 0.375f), new Vector3(w, h, 0.77f), k == 2 ? trim : hull);
        }
        Kit.AddBox(body, new Vector3(0f, 0.85f, 1.9f), new Vector3(1.2f, 1.15f, 0.8f), Dark);      // the engine's cowling
        Kit.AddBox(body, new Vector3(0f, 1.3f, 2.35f), new Vector3(0.3f, 0.3f, 0.2f), trim);       // the hub
        Kit.AddBox(body, new Vector3(0f, 1.9f, 0.75f), new Vector3(0.6f, 0.16f, 0.05f), new Color(0.7f, 0.85f, 0.95f));      // a low windscreen
        // wings: lower through the fuselage's foot, upper high over the cockpit's front, held only by struts out
        // near the tips (the game is seen from the pilot's eyes, just over the cockpit's rim: nothing in front of them)
        Kit.AddBox(body, new Vector3(0f, 0.9f, 1.2f), new Vector3(8.4f, 0.12f, 1.5f), wings);
        Kit.AddBox(body, new Vector3(0f, 3.0f, 1.45f), new Vector3(9.2f, 0.13f, 1.55f), wings);
        foreach (int sx in new[] { -1, 1 })
        {
            Kit.AddBox(body, new Vector3(sx * 4.2f, 3.0f, 1.45f), new Vector3(0.6f, 0.14f, 1.57f), trim);      // the wingtips
            foreach (float z in new[] { 0.85f, 1.85f })
                Kit.AddBox(body, new Vector3(sx * 3.1f, 1.0f, z), new Vector3(0.08f, 2.0f, 0.08f), Dark);       // interplane struts
            Kit.AddBox(body, new Vector3(sx * 0.9f, 0f, 1.0f), new Vector3(0.22f, 0.72f, 0.72f), Dark);        // a wheel
            Kit.AddBox(body, new Vector3(sx * 0.6f, 0.36f, 1.0f), new Vector3(0.08f, 0.6f, 0.08f), Dark);      // its leg
        }
        Kit.AddBox(body, new Vector3(0f, 0.36f, 1.0f), new Vector3(1.9f, 0.08f, 0.08f), Dark);                  // the axle
        Kit.AddBox(body, new Vector3(0f, 1.6f, -4.55f), new Vector3(3.4f, 0.08f, 0.95f), wings);                // the tailplane
        Kit.AddBox(body, new Vector3(0f, 1.6f, -4.7f), new Vector3(0.08f, 1.2f, 1.0f), trim);                   // the fin and rudder
        Kit.AddBox(body, new Vector3(0f, 0.2f, -4.6f), new Vector3(0.08f, 1.2f, 0.1f), Dark);                   // the tail skid
        // the propeller: two blades about the origin, turning about z
        Kit.AddBox(prop, new Vector3(0f, -1.25f, 0f), new Vector3(0.2f, 2.5f, 0.06f), Timber * 0.8f);
        Kit.AddBox(prop, new Vector3(0f, -0.05f, -0.02f), new Vector3(0.16f, 0.1f, 0.1f), Gold);
    }

    public static void Build(PuzzleBuilder b, Vector3 root, Func<Vector2, float> heightAt)
    {
        Vector3 Pt(float u, float v, float y) { Vector2 w = At(u, v); return new Vector3(w.x - root.x, Level + y - root.y, w.y - root.z); }
        float yawAlong = Heading, yawAcross = Heading + 90f;      // a thing facing along the runway; facing across it to the south-east
        var mb = new MeshBuilder();      // buildings (take snow)
        var marks = new MeshBuilder();   // the runway's markings (bare)
        var glow = new MeshBuilder();
        Material snowy = b.ShaderMaterial("BWM/Snowy", "Snowy"), bare = b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f));

        // ---- the runway: threshold bars at both ends, a dashed centre line, lights along both edges ----
        foreach (int end in new[] { -1, 1 })
        {
            for (int k = 0; k < 8; k++)
                Kit.AddBox(marks, Pt(end * (RunwayHalfLength - 9f), -15.75f + k * 4.5f, 0.02f), new Vector3(1.8f, 0.04f, 14f), Dark, yawAlong);
            for (float v = -RunwayHalfWidth; v <= RunwayHalfWidth + 0.1f; v += 4f)      // green lights across the ends
                Kit.AddGlow(glow, Pt(end * (RunwayHalfLength + 1.5f), v, 0.35f), new Vector3(0.35f, 0.25f, 0.35f), yawAlong, new Color(0.25f, 1f, 0.35f), v * 0.07f);
        }
        for (float u = -RunwayHalfLength + 30f; u <= RunwayHalfLength - 30f; u += 30f)
            Kit.AddBox(marks, Pt(u, 0f, 0.02f), new Vector3(0.9f, 0.04f, 14f), Dark, yawAlong);
        for (float u = -RunwayHalfLength; u <= RunwayHalfLength + 0.1f; u += 20f)
            foreach (int side in new[] { -1, 1 })
            {
                Kit.AddBox(mb, Pt(u, side * (RunwayHalfWidth + 1f), 0f), new Vector3(0.14f, 0.5f, 0.14f), Dark, yawAlong);
                Kit.AddGlow(glow, Pt(u, side * (RunwayHalfWidth + 1f), 0.6f), new Vector3(0.3f, 0.22f, 0.3f), yawAlong, new Color(1f, 0.9f, 0.6f), u * 0.013f);
            }

        // ---- the hangar: walls, a gabled roof, the doors across its front ----
        const float HU = -12f, HV = -80f, HalfW = 23f, HalfD = 16f, Wall = 8f, Ridge = 13.5f, DoorTop = 7.4f;
        Color wall = new Color(0.5f, 0.24f, 0.18f), roof = new Color(0.32f, 0.34f, 0.36f);
        Kit.AddBox(mb, Pt(HU, HV - HalfD + 0.25f, 0f), new Vector3(HalfW * 2f, Wall, 0.5f), wall, yawAcross);                 // the back wall
        foreach (int side in new[] { -1, 1 })
            Kit.AddBox(mb, Pt(HU + side * (HalfW - 0.25f), HV, 0f), new Vector3(HalfD * 2f, Wall, 0.5f), wall, yawAlong);       // side walls
        Kit.AddBox(mb, Pt(HU, HV + HalfD - 0.25f, DoorTop), new Vector3(HalfW * 2f, Wall - DoorTop, 0.5f), wall * 0.9f, yawAcross);      // over the doors
        foreach (int side in new[] { -1, 1 })
            Kit.AddBox(mb, Pt(HU + side * (HalfW - 1.0f), HV + HalfD - 0.25f, 0f), new Vector3(2f, DoorTop, 0.6f), wall * 0.85f, yawAcross);      // the door posts
        // the roof: two slopes from the eaves to the ridge (running front to back), and the gable ends
        {
            float run = HalfW + 0.8f, rise = Ridge - Wall, slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 eave = Pt(HU + side * (HalfW + 0.8f), HV, Wall), ridge = Pt(HU, HV, Ridge);
                Vector3 mid = (eave + ridge) * 0.5f;
                Quaternion turn = Quaternion.Euler(0f, yawAcross, 0f) * Quaternion.Euler(0f, 0f, side * slope);
                // a slab: length along the hangar's depth (v), width up the slope
                Vector3 rr = turn * Vector3.right, uu = turn * Vector3.up, ff = turn * Vector3.forward;
                float halfSlope = Mathf.Sqrt(run * run + rise * rise) * 0.5f, halfDepth = HalfD + 1.2f;
                Vector3[] c = { mid - rr * halfSlope - ff * halfDepth, mid + rr * halfSlope - ff * halfDepth, mid + rr * halfSlope + ff * halfDepth, mid - rr * halfSlope + ff * halfDepth };
                Vector3 up = uu * 0.25f;
                mb.Quad(c[0] + up, c[1] + up, c[2] + up, c[3] + up, roof);
                mb.Quad(c[0], c[3], c[2], c[1], roof);
                for (int i = 0; i < 4; i++) mb.Quad(c[i], c[(i + 1) % 4], c[(i + 1) % 4] + up, c[i] + up, roof);
            }
            foreach (int end in new[] { -1, 1 })
            {
                Vector3 l = Pt(HU - HalfW, HV + end * (HalfD - 0.25f), Wall), rgt = Pt(HU + HalfW, HV + end * (HalfD - 0.25f), Wall), top = Pt(HU, HV + end * (HalfD - 0.25f), Ridge);
                mb.Tri(l, top, rgt, wall); mb.Tri(l, rgt, top, wall);
            }
            Kit.AddBox(mb, Pt(HU, HV + HalfD + 0.4f, DoorTop + 0.2f), new Vector3(10f, 1.2f, 0.12f), Green, yawAcross);      // a sign over the doors
            for (int k = 0; k < 7; k++) Kit.AddBox(mb, Pt(HU - 3.6f + k * 1.2f, HV + HalfD + 0.48f, DoorTop + 0.45f), new Vector3(0.7f, 0.7f, 0.05f), Gold, yawAcross);
        }
        b.MeshPiece("Airport Buildings Hangar", mb.ToMesh(), snowy, true);
        // the doors: four panels across the front, a pack piece of their own that Winter Walk takes away when the
        // airport opens
        {
            var doors = new MeshBuilder();
            for (int k = 0; k < 4; k++)
            {
                float u = HU - HalfW + 2f + (k + 0.5f) * ((HalfW - 2f) * 2f / 4f);
                Kit.AddBox(doors, Pt(u, HV + HalfD - 0.6f, 0f), new Vector3((HalfW - 2f) * 2f / 4f - 0.08f, DoorTop - 0.05f, 0.3f), k % 2 == 0 ? Steel : Steel * 0.9f, yawAcross);
                Kit.AddBox(doors, Pt(u, HV + HalfD - 0.42f, 3.4f), new Vector3(2.4f, 2.4f, 0.04f), Red, yawAcross);      // a wreath-red panel on each
            }
            b.MeshPiece("Airport Gates", doors.ToMesh(), bare, true);
        }
        Kit.LightString(b, "Airport Hangar Lights", new List<Vector3> { Pt(HU - HalfW, HV + HalfD + 0.6f, Wall + 0.2f), Pt(HU, HV + HalfD + 0.6f, Ridge + 0.1f), Pt(HU + HalfW, HV + HalfD + 0.6f, Wall + 0.2f) }, 0.3f, 0.45f, false, false, 901);

        // ---- the terminal, a windsock and a fuel tank ----
        {
            var t = new MeshBuilder();
            Vector3 foot = Pt(-62f, -44f, 0f);
            Kit.AddCabin(t, glow, foot, new Vector2(12f, 8f), yawAcross, 77);
            WinterIslePuzzle.Lamp(root + Kit.AddPorchLamp(t, glow, foot, new Vector2(12f, 8f), yawAcross));
            Vector3 sock = Pt(140f, -34f, 0f);
            Kit.AddBox(t, sock, new Vector3(0.18f, 7f, 0.18f), Steel, 0f);
            for (int k = 0; k < 5; k++)      // the sock streaming south-east (downwind of the north-west wind), in orange and white bands
            {
                Vector2 dir = new Vector2(0.707f, -0.707f) * (0.6f + k * 0.75f);
                Kit.AddBox(t, sock + new Vector3(dir.x, 6.3f - k * 0.08f, dir.y), new Vector3(0.75f - k * 0.1f, 0.75f - k * 0.1f, 0.78f), k % 2 == 0 ? new Color(0.95f, 0.45f, 0.1f) : Cream, 135f);
            }
            Vector3 tank = Pt(30f, -90f, 0f);
            Kit.AddDisc(t, tank, 3.2f, 5f, new Color(0.75f, 0.76f, 0.78f), 20);
            Kit.AddDisc(t, tank + Vector3.up * 5f, 3.4f, 0.3f, Red, 20);
            b.MeshPiece("Airport Buildings Terminal", t.ToMesh(), snowy, true);
        }

        // ---- the biplanes ----
        var planes = new GameObject("Biplanes");
        planes.transform.SetParent(b.Root.transform, false);
        Material planeMat = b.ShaderMaterial("BWM/Snowy", "Biplane", m => m.SetFloat("_SnowLine", 0.85f));
        var schemes = new[] { (Red, Cream, Gold), (Green, Red, Gold), (Cream, Red, Green), (Blue, Cream, Red), (Red, Gold, Cream), (Gold, Green, Red) };
        var meshes = new List<(Mesh body, Mesh prop)>();
        for (int s = 0; s < schemes.Length; s++)
        {
            var body = new MeshBuilder(); var prop = new MeshBuilder();
            AddBiplane(body, prop, schemes[s].Item1, schemes[s].Item2, schemes[s].Item3);
            meshes.Add((b.SaveMesh("Biplane " + (s + 1), body.ToMesh()), b.SaveMesh("Biplane Propeller " + (s + 1), prop.ToMesh())));
        }
        int stands = 0;
        void Stand(Vector3 localFoot, float yaw)
        {
            var stand = new GameObject("Biplane Stand " + (stands + 1));
            stand.transform.SetParent(planes.transform, false);
            stand.transform.localPosition = localFoot;
            stand.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var model = new GameObject("Model");
            model.transform.SetParent(stand.transform, false);
            var (body, prop) = meshes[stands % meshes.Count];
            model.AddComponent<MeshFilter>().sharedMesh = body;
            model.AddComponent<MeshRenderer>().sharedMaterial = planeMat;
            model.AddComponent<MeshCollider>().sharedMesh = body;
            var propeller = new GameObject("Propeller");
            propeller.transform.SetParent(model.transform, false);
            propeller.transform.localPosition = new Vector3(0f, 1.3f, 2.5f);
            propeller.AddComponent<MeshFilter>().sharedMesh = prop;
            propeller.AddComponent<MeshRenderer>().sharedMaterial = planeMat;
            stands++;
        }
        for (int k = -1; k <= 1; k++) Stand(Pt(HU + k * 13.5f, HV - 3f, 0f), yawAcross);      // in the hangar, noses to the doors
        Stand(Pt(28f, -38f, 0f), yawAlong);                                                 // on the apron, ready to roll
        // two more on the apron, in the open (the user, 2026-11-24: "there should be several planes available";
        // with the hangar shut only one could be seen), a wingspan and more apart and clear of the tower at u 58
        Stand(Pt(44f, -38f, 0f), yawAlong);
        Stand(Pt(28f, -56f, 0f), yawAlong);

        // ---- the summit's airstrip: a deck on stilts, level with the crest where it starts ----
        {
            var deck = new MeshBuilder();
            var deckMarks = new MeshBuilder();
            Vector2 start = StripAt(StripFrom, 0f);
            float deckY = heightAt(start);
            if (float.IsNaN(deckY)) deckY = 575f;
            Vector3 D(float r, float side, float up) { Vector2 w = StripAt(r, side); return new Vector3(w.x - root.x, deckY + up - root.y, w.y - root.z); }
            float yawStrip = StripBearing;
            for (float r = StripFrom; r < StripTo; r += 10f)
            {
                float len = Mathf.Min(10f, StripTo - r);
                Kit.AddBox(deck, D(r + len * 0.5f, 0f, -0.6f), new Vector3(StripHalf * 2f, 0.6f, len + 0.05f), Timber, yawStrip);       // the deck
                foreach (int side in new[] { -1, 1 })
                {
                    Kit.AddBox(deck, D(r + len * 0.5f, side * (StripHalf - 0.15f), 0f), new Vector3(0.3f, 0.35f, len), Timber * 0.8f, yawStrip);      // a kerb
                    Kit.AddGlow(glow, D(r, side * (StripHalf - 0.15f), 0.5f), new Vector3(0.3f, 0.22f, 0.3f), yawStrip, new Color(1f, 0.9f, 0.6f), r * 0.03f);
                }
                Kit.AddBox(deckMarks, D(r + len * 0.5f, 0f, 0.01f), new Vector3(0.7f, 0.03f, len * 0.5f), Cream, yawStrip);      // the centre line
                // a bent of stilts under every other joint, down to the mountainside, braced (one under every joint
                // read as a cage)
                if (Mathf.RoundToInt((r - StripFrom) / 10f) % 2 == 0 && r + len < StripTo - 0.5f) continue;
                foreach (float side in new[] { -StripHalf + 1.2f, 0f, StripHalf - 1.2f })
                {
                    Vector2 w = StripAt(r + len, side);
                    float ground = heightAt(w);
                    if (float.IsNaN(ground)) ground = deckY - 80f;
                    float tall = deckY - 0.6f - ground + 1f;
                    if (tall < 0.6f) continue;
                    Kit.AddBox(deck, new Vector3(w.x - root.x, ground - 1f - root.y, w.y - root.z), new Vector3(0.7f, tall, 0.7f), Timber * 0.9f, yawStrip);
                }
                float lowGround = Mathf.Min(heightAt(StripAt(r + len, -StripHalf)), heightAt(StripAt(r + len, StripHalf)));
                for (float y = deckY - 10f; !float.IsNaN(lowGround) && y > lowGround + 1.5f; y -= 10f)
                    Kit.AddBox(deck, D(r + len, 0f, y - deckY), new Vector3(StripHalf * 2f - 1.2f, 0.25f, 0.25f), Timber * 0.85f, yawStrip + 0f);
            }
            for (int k = 0; k < 8; k++)      // threshold bars at the outer end
                Kit.AddBox(deckMarks, D(StripTo - 6f, -7f + k * 2f, 0.01f), new Vector3(1f, 0.03f, 8f), Cream, yawStrip);
            b.MeshPiece("Summit Airstrip", deck.ToMesh(), snowy, true);
            b.MeshPiece("Summit Airstrip Marks", deckMarks.ToMesh(), bare, false);
            // (posts drawn here, from the deck: LightString's own start at the pack's root, the sea floor)
            foreach (var (side, h) in new[] { (-StripHalf, 2.4f), (0f, 3.2f), (StripHalf, 2.4f) }) Kit.AddBox(deck, D(StripFrom, side, 0f), new Vector3(0.12f, h + 0.08f, 0.12f), Timber, yawStrip);
            Kit.LightString(b, "Summit Airstrip Lights", new List<Vector3> { D(StripFrom, -StripHalf, 2.4f), D(StripFrom, 0f, 3.2f), D(StripFrom, StripHalf, 2.4f) }, 0.3f, 0.45f, false, false, 911);
            foreach (int side in new[] { -1, 1 }) Stand(D(StripFrom + 9f, side * 5f, 0f), yawStrip);
        }
        b.MeshPiece("Airport Marks", marks.ToMesh(), bare, false);
        b.MeshPiece("Airport Lamps", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        Debug.Log("[BWM] Winter Isle airport: runway at " + Sdk.F(Runway.x) + ", " + Sdk.F(Runway.y) + " heading " + Heading + ", " + stands + " biplanes (two on the summit's airstrip)");
    }
}
