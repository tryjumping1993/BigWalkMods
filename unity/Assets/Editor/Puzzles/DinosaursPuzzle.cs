// Dinosaurs (a scenery pack): friendly walking dinosaurs on the Winter Isle, built in code, that Winter Walk's
// DinoDriver walks about their homes. They stay friendly until a player makes enough noise near them (voice, sprinting,
// jumping, waving, a megaphone or cowbell); then they chase that player at 95 % of a player's sprinting speed, and a
// player they catch drops everything they carry where they stand and wakes at the last tower they visited.
// Each dinosaur is "Dino <n> <Species>" with child parts that the driver animates: "Body", "Head" (neck and head,
// pivoting at the shoulders), "Tail" (pivoting at the hips), "Leg FL/FR/BL/BR" (pivoting at the hip), and "Angry Eyes"
// (glowing red, hidden until it chases). "Dino Control" carries the DinoControl script (host orders, players' reports).
// The homes are on open lowland away from puzzle sites; each dinosaur wanders within its home's radius.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class DinosaursPuzzle : PuzzleDef
{
    public override string Name => "Dinosaurs";
    public override int PuzzleType => 2;
    public override Vector2 Footprint => new Vector2(6f, 6f);
    public override Vector2? FixedSite => WinterIslePuzzle.Plaza;

    // home (world x, z), how far it wanders, species: 0 long-neck, 1 plated, 2 three-horned
    public static readonly (Vector2 at, float radius, int kind)[] Homes =
    {
        (new Vector2(-1000f, -1110f), 55f, 0),      // the lowland west of the lake
        (new Vector2(-1240f, -1160f), 50f, 1),      // the shelf below Northwest
        (new Vector2(-1450f, -1230f), 45f, 2),      // the central ridge
        (new Vector2(-1150f, -1600f), 50f, 1),      // the forest between the runs and the east lowland
        (new Vector2(-1700f, -2350f), 45f, 0),      // the south shore, between South Shore and Seal Point
        (new Vector2(-1255f, -2150f), 45f, 2),      // the slopes above South Shore (moved from -1180, -2200 on 2026-11-22: Bell Ringers' churchyard came within 39 m of its edge; now 98 m from every site)
        (new Vector2(-1560f, -930f), 40f, 1),       // the north flank above North Shore
        (new Vector2(-2860f, -1420f), 40f, 0),      // the far west, by Cirque Gate
    };
    static readonly string[] Species = { "Longneck", "Plated", "Horned" };

    static Color B(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f).linear;

    public override void Build(PuzzleBuilder b)
    {
        Material bare = IsleKit.Bare(b);
        Material glowMat = b.ShaderMaterial("BWM/FairyLight", "Dino Eyes", m => { m.SetFloat("_Twinkle", 0.2f); m.SetFloat("_Glow", 2.5f); m.SetFloat("_Speed", 6f); });
        Vector3 root = b.Root.transform.position;
        Color[] skins = { B(96, 150, 92), B(70, 132, 128), B(176, 116, 64), B(120, 150, 70) };
        for (int i = 0; i < Homes.Length; i++)
        {
            var (at, _, kind) = Homes[i];
            var dino = new GameObject("Dino " + i + " " + Species[kind]);
            dino.transform.SetParent(b.Root.transform, false);
            float y = b.Ground.At(at.x, at.y, out float gy) ? gy : root.y;
            dino.transform.position = new Vector3(at.x, y, at.y);
            Color skin = skins[(i + kind) % skins.Length], belly = Color.Lerp(skin, B(230, 220, 190), 0.55f), dark = skin * 0.6f;
            dark.a = 1f;
            DinoParts parts = kind == 0 ? Longneck(skin, belly, dark) : kind == 1 ? Plated(skin, belly, dark) : Horned(skin, belly, dark);
            Part(b, dino, "Body", parts.body, Vector3.zero, bare);
            Part(b, dino, "Head", parts.head, parts.headPivot, bare);
            Part(b, dino, "Tail", parts.tail, parts.tailPivot, bare);
            string[] legs = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int l = 0; l < 4; l++) Part(b, dino, legs[l], parts.leg[l], parts.hips[l], bare);
            GameObject eyes = Part(b, dino.transform.Find("Head").gameObject, "Angry Eyes", parts.eyes, Vector3.zero, glowMat);
            eyes.SetActive(false);
            dino.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
        }
        var control = new Piece(new GameObject("Dino Control"));
        control.Transform.SetParent(b.Root.transform, false);
        b.AddCustomScript(control, "DinoControl");
        Debug.Log("[BWM] Dinosaurs: " + Homes.Length + " at their homes");
    }

    static GameObject Part(PuzzleBuilder b, GameObject parent, string name, MeshBuilder mb, Vector3 pivot, Material mat)
    {
        Piece p = b.MeshPiece("Dino " + parent.name + " " + name, mb.ToMesh(), mat, false);
        p.Go.name = name;
        p.Transform.SetParent(parent.transform, false);
        p.Transform.localPosition = pivot;
        p.Transform.localRotation = Quaternion.identity;
        return p.Go;
    }

    sealed class DinoParts
    {
        public MeshBuilder body = new MeshBuilder(), head = new MeshBuilder(), tail = new MeshBuilder(), eyes = new MeshBuilder();
        public MeshBuilder[] leg = { new MeshBuilder(), new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
        public Vector3 headPivot, tailPivot;
        public Vector3[] hips = new Vector3[4];
    }

    // ---- shapes, each part built about its own pivot ----
    static void Quad(MeshBuilder mb, Vector3 a, Vector3 bq, Vector3 c, Vector3 d, Vector3 outward, Color col)
    {
        if (Vector3.Dot(Vector3.Cross(d - a, bq - a), outward) >= 0f) mb.Quad(a, bq, c, d, col);
        else mb.Quad(a, d, c, bq, col);
    }
    // an ellipsoid, lighter underneath
    static void Blob(MeshBuilder mb, Vector3 c, Vector3 r, Color top, Color under, int seg = 10, int rings = 7)
    {
        for (int j = 0; j < rings; j++)
            for (int k = 0; k < seg; k++)
            {
                Vector3 P(int jj, int kk)
                {
                    float th = Mathf.PI * jj / rings, ph = 2f * Mathf.PI * kk / seg;
                    return c + new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * r.x, Mathf.Cos(th) * r.y, Mathf.Sin(th) * Mathf.Sin(ph) * r.z);
                }
                Vector3 a = P(j, k), bq = P(j, k + 1), cc = P(j + 1, k + 1), d = P(j + 1, k);
                Vector3 mid = (a + bq + cc + d) * 0.25f;
                Color col = mid.y < c.y - r.y * 0.25f ? under : top;
                Quad(mb, a, bq, cc, d, mid - c, col);
            }
    }
    // a tapered limb or neck from a to b, radii ra to rb
    static void Tube(MeshBuilder mb, Vector3 a, Vector3 bEnd, float ra, float rb, Color col, int seg = 8)
    {
        Vector3 axis = (bEnd - a).normalized;
        Vector3 p1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, p2 = Vector3.Cross(p1, axis);
        for (int k = 0; k < seg; k++)
        {
            float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
            Vector3 d0 = p1 * Mathf.Cos(a0) + p2 * Mathf.Sin(a0), d1 = p1 * Mathf.Cos(a1) + p2 * Mathf.Sin(a1);
            Quad(mb, a + d0 * ra, a + d1 * ra, bEnd + d1 * rb, bEnd + d0 * rb, (d0 + d1) * 0.5f, col);
        }
        Blob(mb, bEnd, Vector3.one * rb, col, col, 6, 4);
    }
    static void Cone(MeshBuilder mb, Vector3 foot, Vector3 tip, float r, Color col, int seg = 8)
    {
        Vector3 axis = (tip - foot).normalized;
        Vector3 p1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, p2 = Vector3.Cross(p1, axis);
        for (int k = 0; k < seg; k++)
        {
            float a0 = 2f * Mathf.PI * k / seg, a1 = 2f * Mathf.PI * (k + 1) / seg;
            Vector3 d0 = p1 * Mathf.Cos(a0) + p2 * Mathf.Sin(a0), d1 = p1 * Mathf.Cos(a1) + p2 * Mathf.Sin(a1);
            Quad(mb, foot + d0 * r, foot + d1 * r, tip, tip, (d0 + d1) * 0.5f, col);
        }
    }
    static void Eyes(DinoParts p, Vector3 headCentre, float spread, float size, MeshBuilder visible)
    {
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 e = headCentre + new Vector3(s * spread, size * 0.6f, size * 0.9f);
            Kit.AddBox(visible, e - Vector3.up * size * 0.5f, Vector3.one * size, new Color(0.03f, 0.03f, 0.03f), 0f);
            Kit.AddGlow(p.eyes, e + new Vector3(0f, 0f, size * 0.15f), Vector3.one * size * 1.15f, 0f, new Color(1f, 0.08f, 0.04f), s);
        }
    }
    static void Legs(DinoParts p, float hipY, float halfX, float frontZ, float backZ, float len, float thick, Color col)
    {
        Vector3[] hips = { new Vector3(-halfX, hipY, frontZ), new Vector3(halfX, hipY, frontZ), new Vector3(-halfX, hipY, backZ), new Vector3(halfX, hipY, backZ) };
        for (int l = 0; l < 4; l++)
        {
            p.hips[l] = hips[l];
            float t = l < 2 ? thick : thick * 1.15f;
            Tube(p.leg[l], Vector3.zero, new Vector3(0f, -len + t, 0f), t, t * 0.9f, col);
            Blob(p.leg[l], new Vector3(0f, -len + t * 0.6f, t * 0.25f), new Vector3(t * 1.15f, t * 0.6f, t * 1.4f), col * 0.85f, col * 0.85f, 8, 4);      // the foot
        }
    }
    static void Scarf(MeshBuilder mb, Vector3 at, Vector3 along, float r)
    {
        Tube(mb, at - along * 0.12f, at + along * 0.12f, r * 1.12f, r * 1.12f, B(200, 30, 35), 10);
        Tube(mb, at + Vector3.down * r * 0.5f + Vector3.right * r * 0.6f, at + Vector3.down * r * 2.2f + Vector3.right * r * 0.9f + Vector3.back * 0.1f, r * 0.25f, r * 0.2f, B(200, 30, 35), 6);
        for (int k = 0; k < 6; k++) Kit.AddBox(mb, at + Vector3.down * (r * 0.5f + k * 0.28f * r) + Vector3.right * r * 0.75f + Vector3.forward * (r * 0.26f), new Vector3(r * 0.3f, r * 0.08f, 0.02f), Color.white, 0f);
    }

    static DinoParts Longneck(Color skin, Color belly, Color dark)
    {
        var p = new DinoParts();
        Blob(p.body, new Vector3(0f, 2.9f, 0f), new Vector3(1.2f, 1.15f, 2.1f), skin, belly, 12, 8);
        for (int k = 0; k < 7; k++) Blob(p.body, new Vector3(0f, 3.9f, -1.4f + k * 0.5f), new Vector3(0.18f, 0.14f, 0.2f), dark, dark, 6, 4);      // spine ridge
        p.headPivot = new Vector3(0f, 3.4f, 1.6f);
        Tube(p.head, Vector3.zero, new Vector3(0f, 2.6f, 1.3f), 0.55f, 0.32f, skin);
        Blob(p.head, new Vector3(0f, 2.75f, 1.65f), new Vector3(0.38f, 0.33f, 0.62f), skin, belly);
        Eyes(p, new Vector3(0f, 2.75f, 1.65f), 0.3f, 0.12f, p.head);
        Scarf(p.head, new Vector3(0f, 0.9f, 0.45f), new Vector3(0f, 0.9f, 0.45f).normalized, 0.48f);
        Cone(p.head, new Vector3(0f, 3.02f, 1.55f), new Vector3(0f, 3.75f, 1.2f), 0.33f, B(205, 30, 35), 10);      // a Santa hat
        Blob(p.head, new Vector3(0f, 3.75f, 1.2f), Vector3.one * 0.12f, Color.white, Color.white, 6, 4);
        Tube(p.head, new Vector3(0f, 3.0f, 1.55f) + Vector3.left * 0.34f, new Vector3(0f, 3.0f, 1.55f) + Vector3.right * 0.34f, 0.08f, 0.08f, Color.white, 8);
        p.tailPivot = new Vector3(0f, 2.8f, -1.9f);
        Tube(p.tail, Vector3.zero, new Vector3(0f, -0.6f, -1.8f), 0.6f, 0.35f, skin);
        Tube(p.tail, new Vector3(0f, -0.6f, -1.8f), new Vector3(0f, -1.3f, -3.6f), 0.35f, 0.08f, skin);
        Legs(p, 2.4f, 0.75f, 1.2f, -1.2f, 2.4f, 0.36f, skin);
        return p;
    }

    static DinoParts Plated(Color skin, Color belly, Color dark)
    {
        var p = new DinoParts();
        Blob(p.body, new Vector3(0f, 1.75f, 0f), new Vector3(0.95f, 0.95f, 1.8f), skin, belly, 12, 8);
        for (int k = 0; k < 7; k++)
        {
            float z = -1.3f + k * 0.42f, h = 0.45f + 0.35f * Mathf.Sin(Mathf.PI * (k + 0.5f) / 7f);
            Vector3 foot = new Vector3(0f, 2.55f + 0.1f * Mathf.Sin(Mathf.PI * (k + 0.5f) / 7f), z);
            Quad(p.body, foot + new Vector3(0f, 0f, -0.22f), foot + new Vector3(0f, 0f, 0.22f), foot + new Vector3(0f, h, 0.05f), foot + new Vector3(0f, h, -0.05f), Vector3.right, B(196, 110, 60));
            Quad(p.body, foot + new Vector3(0f, 0f, -0.22f), foot + new Vector3(0f, 0f, 0.22f), foot + new Vector3(0f, h, 0.05f), foot + new Vector3(0f, h, -0.05f), Vector3.left, B(176, 96, 52));
        }
        p.headPivot = new Vector3(0f, 1.7f, 1.5f);
        Tube(p.head, Vector3.zero, new Vector3(0f, -0.5f, 0.75f), 0.4f, 0.3f, skin);
        Blob(p.head, new Vector3(0f, -0.55f, 1.0f), new Vector3(0.3f, 0.28f, 0.48f), skin, belly);
        Eyes(p, new Vector3(0f, -0.55f, 1.0f), 0.22f, 0.09f, p.head);
        Scarf(p.head, new Vector3(0f, -0.1f, 0.18f), new Vector3(0f, -0.5f, 0.75f).normalized, 0.4f);
        p.tailPivot = new Vector3(0f, 1.6f, -1.6f);
        Tube(p.tail, Vector3.zero, new Vector3(0f, -0.3f, -1.6f), 0.5f, 0.16f, skin);
        foreach (int s in new[] { -1, 1 }) foreach (float z in new[] { -1.2f, -1.5f }) Cone(p.tail, new Vector3(0.1f * s, -0.22f, z), new Vector3(0.55f * s, 0.05f, z - 0.2f), 0.07f, B(225, 215, 190), 6);
        Legs(p, 1.3f, 0.6f, 1.0f, -1.0f, 1.3f, 0.27f, skin);
        return p;
    }

    static DinoParts Horned(Color skin, Color belly, Color dark)
    {
        var p = new DinoParts();
        Blob(p.body, new Vector3(0f, 1.6f, 0f), new Vector3(1.0f, 0.95f, 1.65f), skin, belly, 12, 8);
        p.headPivot = new Vector3(0f, 1.75f, 1.45f);
        Blob(p.head, new Vector3(0f, -0.05f, 0.7f), new Vector3(0.45f, 0.42f, 0.62f), skin, belly);
        // the frill, a broad disc behind the head, and the three horns
        Vector3 frillC = new Vector3(0f, 0.35f, 0.25f);
        for (int k = 0; k < 12; k++)
        {
            float a0 = Mathf.PI * (0.05f + 0.9f * k / 12f), a1 = Mathf.PI * (0.05f + 0.9f * (k + 1) / 12f);
            Vector3 e0 = frillC + new Vector3(Mathf.Cos(a0) * 1.0f, Mathf.Sin(a0) * 0.85f, -0.1f), e1 = frillC + new Vector3(Mathf.Cos(a1) * 1.0f, Mathf.Sin(a1) * 0.85f, -0.1f);
            Quad(p.head, frillC, e0, e1, e1, Vector3.forward, dark);
            Quad(p.head, frillC, e0, e1, e1, Vector3.back, dark * 0.8f);
        }
        foreach (int s in new[] { -1, 1 }) Cone(p.head, new Vector3(0.22f * s, 0.25f, 0.85f), new Vector3(0.3f * s, 0.75f, 1.55f), 0.09f, B(235, 225, 200), 8);
        Cone(p.head, new Vector3(0f, -0.05f, 1.25f), new Vector3(0f, 0.25f, 1.6f), 0.08f, B(235, 225, 200), 8);
        Eyes(p, new Vector3(0f, 0.0f, 0.75f), 0.3f, 0.1f, p.head);
        Scarf(p.head, new Vector3(0f, -0.05f, 0.05f), Vector3.forward, 0.5f);
        p.tailPivot = new Vector3(0f, 1.5f, -1.5f);
        Tube(p.tail, Vector3.zero, new Vector3(0f, -0.4f, -1.4f), 0.48f, 0.12f, skin);
        Legs(p, 1.2f, 0.62f, 0.95f, -0.95f, 1.2f, 0.28f, skin);
        return p;
    }
}
