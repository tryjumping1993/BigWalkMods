// Sleigh Run: pass the presents down the line. Four wrapped presents wait on a bench in Santa's workshop, at the south
// end of a roofed snow shed; the sleigh stands in its bay at the far end. Between them the shed is four rooms in a row
// (the workshop, two L-shaped chambers, and the sleigh's room: a third L, a short hall and the bay), with a solid wall
// between each pair and a hatch through it: a hole 0.7 m wide and 0.6 m high with a plank shelf through it a metre up,
// big enough to set a present on from one side and take it from the other, too small to climb through. Every room has
// its own doorway to the outside, a turnstile that lets players through and no presents (the game's No Items gate:
// without it four players would each walk a present round the shed), and none to the next room. A present goes hand
// to shelf to hand down the line
// and into the sleigh's bed; when all four are in it the puzzle is won (the gourd and the confetti at the start
// corner). Four players stand one to a room and the presents fly along; fewer walk round from doorway to doorway, and
// one player can do all of it alone, which takes a while. No clock, nothing to get wrong, and everyone can talk: a
// present dropped on a floor is picked up again, and one thrown onto a roof is laid back on the bench (ItemReturn).
// (The user's rules of 2026-11-24: fewer timers and sealed rooms. Before that this was a soundproof, radio-dead shed
// of doors, each opened from a pointsman's peg board outside while a runner inside described two scribbles over a
// one-way intercom, against the clock of the present, a timer ball.)
//
// One version for every lobby size: the old three-door layout's four rooms (the fourth door's frame is the bay's open
// arch). The Start Switch lights the lamps over the hatches and nothing else: the sleigh counts presents whether the
// puzzle is on or not, so nothing can be done in the wrong order.
//
// Hatches (the old doors' places): 1 workshop to chamber 1 at (-5.5, -10), 2 chamber 1 to chamber 2 at (0.2, -1.7),
// 3 chamber 2 to the sleigh's room at (8.5, 4). Doorways (a two-way No Items turnstile in each, under a little red porch): the workshop's
// in its south wall at x -7, facing the start corner; chamber 1's in its west wall at z -5.6; chamber 2's in its
// south wall at x 4.7; the sleigh's room's in its east wall at z 8.3.
//
// Coordinates: local metres from the root, x east, z north; heights above the pad top (Floor), everything placed
// onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SleighRunPuzzle : PuzzleDef
{
    public override string Name => "SleighRun";
    public override Vector2 Footprint => new Vector2(34f, 44f);
    public override Vector2? FixedSite => new Vector2(-1032f, -1908f);      // layout-final: a goods shed by the airport apron

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Ceil = 2.6f;            // inside height of the shed (roof slabs 2.6..2.9)
    const float RoofTop = 2.9f;
    const float Y0 = -0.1f;             // walls start a little under the pad top
    const float Boards = 0.04f;         // each room's floorboards lie this thick on the pad
    const float DoorW = 2.16f, DoorH = 2.32f;    // a room's doorway to the outside: the turnstile's width and height (2.12 x 2.3 m)
    const float WallT = 0.3f;           // the outer walls' thickness
    const float GateDeep = 0.705f;      // Gate No items: local x -1.11..+1.01, z -0.705..+0.706
    const float HatchHalf = 0.12f;      // a hatch wall is 0.24 m thick, centred on the old door's line
    const float HatchW = 0.7f, HatchH = 0.6f, Sill = 1.0f;      // the hole; its sill is the shelf's top
    const float ShelfOut = 0.5f, ShelfW = 0.9f, Plank = 0.05f, Lip = 0.04f;      // the shelf stands 0.5 m into each room, 0.1 m wider than the hole each side
    const float StripeTop = 2.0f;       // a hatch wall's stripes end here; the plain band above carries the lamp
    const float LampH = 2.05f;
    const float DepthLight8 = 0.1f + 0.01f;      // part-metrics.tsv: a Light 8's back is 0.1 m behind its pivot; 1 cm clear
    const float ArchHalf = 0.62f, ArchTop = 2.04f;      // the bay's arch: the old fourth door's opening
    const float BenchTop = 0.85f, BenchX = -3.35f, BenchZ = -13.6f, BenchStep = 0.6f;      // the presents' bench: the first one's place, and the next ones' along z
    const float BedTop = 0.73f;         // the sleigh's bed

    // The doorways: the middle of each on its wall's outer face
    static readonly Vector2 WorkshopDoor = new Vector2(-7f, -16.3f), Chamber1Door = new Vector2(-7f, -5.6f);
    static readonly Vector2 Chamber2Door = new Vector2(4.7f, -3.2f), SleighDoor = new Vector2(10f, 8.3f);

    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);
    static readonly Color Deal = new Color(0.55f, 0.42f, 0.29f);            // floorboards
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);               // near-white: a lamp's lens is dull when off, a bright disc when lit
    static readonly Vector3 PresentSize = new Vector3(0.4f, 0.3f, 0.4f);
    static readonly Color[] Wraps = { new Color(0.86f, 0.12f, 0.12f), new Color(0.12f, 0.62f, 0.25f), new Color(0.16f, 0.4f, 0.86f), new Color(0.96f, 0.76f, 0.2f) };
    static readonly Color[] Ribbons = { new Color(0.98f, 0.82f, 0.25f), new Color(0.95f, 0.93f, 0.88f), new Color(0.98f, 0.82f, 0.25f), new Color(0.82f, 0.1f, 0.1f) };

    PuzzleBuilder b;
    Material snowy, bare;

    // ---- small builders ----

    // A block between two corners (local x, y above the pad top, z)
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A block in bands of two colours up its height (candy-cane jambs and posts)
    static void Striped(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color a, Color c, float band)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt((y1 - y0) / band));
        float step = (y1 - y0) / n;
        for (int i = 0; i < n; i++) Block(mb, x0, x1, y0 + i * step, y0 + (i + 1) * step, z0, z1, i % 2 == 0 ? a : c);
    }

    // A mesh built in pad-top coordinates, placed on the pad
    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, Floor, false);
        return piece;
    }

    // An SDK part on the pad
    Piece P(string prefab, float x, float z, float height, float yaw = 0f) => b.Part(prefab, x, z, Floor + height, yaw, false);

    // An empty on the pad (lamp markers, the catching box)
    Piece E(string name, float x, float z, float height) => b.Empty(name, x, z, Floor + height, false);

    // A wall along x (z0..z1 thick) from x0 to x1, with a plain doorway (DoorW wide, DoorH high) centred at each x given,
    // in rising order; the wall goes on over each doorway to the ceiling
    static void WallX(MeshBuilder mb, float x0, float x1, float z0, float z1, Color color, params float[] doors)
    {
        float at = x0;
        foreach (float d in doors)
        {
            Block(mb, at, d - DoorW / 2f, Y0, Ceil, z0, z1, color);
            Block(mb, d - DoorW / 2f, d + DoorW / 2f, DoorH, Ceil, z0, z1, color);
            at = d + DoorW / 2f;
        }
        Block(mb, at, x1, Y0, Ceil, z0, z1, color);
    }

    static void WallZ(MeshBuilder mb, float z0, float z1, float x0, float x1, Color color, params float[] doors)
    {
        float at = z0;
        foreach (float d in doors)
        {
            Block(mb, x0, x1, Y0, Ceil, at, d - DoorW / 2f, color);
            Block(mb, x0, x1, DoorH, Ceil, d - DoorW / 2f, d + DoorW / 2f, color);
            at = d + DoorW / 2f;
        }
        Block(mb, x0, x1, Y0, Ceil, at, z1, color);
    }

    // A block in a doorway's own frame: u along the wall, w out from its outer face, 'door' the doorway's middle there
    // and 'yaw' the way out
    static void Out(MeshBuilder mb, Vector2 door, float yaw, float u0, float u1, float y0, float y1, float w0, float w1, Color color)
    {
        Vector2 c = door + IsleKit.Turn((u0 + u1) / 2f, (w0 + w1) / 2f, yaw);
        Kit.AddBox(mb, new Vector3(c.x, y0, c.y), new Vector3(u1 - u0, y1 - y0, w1 - w0), color, yaw);
    }

    // The porch over a doorway: a little red roof on two candy-striped posts, so a way in shows from across the site
    static void Porch(MeshBuilder mb, Vector2 door, float yaw)
    {
        const float top = DoorH + 0.1f, deep = 1.55f, half = DoorW / 2f + 0.3f;      // over the turnstile, which stands 1.11 m out
        Out(mb, door, yaw, -half, half, top, top + 0.12f, 0f, deep, IsleKit.Red);
        float step = (top - Y0) / 8f;
        foreach (float u in new[] { -half + 0.1f, half - 0.1f })
            for (int k = 0; k < 8; k++)
                Out(mb, door, yaw, u - 0.07f, u + 0.07f, Y0 + k * step, Y0 + (k + 1) * step, deep - 0.17f, deep - 0.03f, k % 2 == 0 ? IsleKit.Red : IsleKit.Cream);
    }

    // The turnstile in a doorway: the game's No Items gate, two-way and never locked, so players come and go and a
    // present stays in its room. Its inner end stands flush with the wall's inner face, the rest outside under the porch
    // (its pivot is 0.05 m off its middle).
    Piece Gate(string name, Vector2 door, float yaw)
    {
        Vector2 at = door + IsleKit.Turn(0.05f, GateDeep - WallT, yaw);
        return P("Gate No items", at.x, at.y, 0f, yaw).Named(name)
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
    }

    // A room's floorboards
    static void Floorboards(MeshBuilder mb, float x0, float x1, float z0, float z1) => Block(mb, x0, x1, 0f, Boards, z0, z1, Deal);

    // A hatch wall across a corridor, on an old door's line: solid from floor to ceiling but for the hatch (HatchW wide,
    // HatchH high, its sill at Sill), with the shelf through it: a plank that stands ShelfOut into the room on each
    // side, a low lip round its three open edges there. On each face a lamp in a wreath over the hole, lit while the
    // puzzle runs. alongX: the wall runs along x at z = line, from c0 to c1, else along z at x = line; 'centre' is the
    // hole's middle along the wall; lowRoom and highRoom name the rooms on its two sides (for the lamps' names).
    void HatchWall(int number, bool alongX, float line, float c0, float c1, float centre, Color colour, string lowRoom, string highRoom, List<Component> lamps)
    {
        var mb = new MeshBuilder();
        // a block from a0 to a1 along the wall and from t0 to t1 through it, measured from its line
        void Along(float a0, float a1, float y0, float y1, float t0, float t1, Color c)
        {
            if (alongX) Block(mb, a0, a1, y0, y1, line + t0, line + t1, c);
            else Block(mb, line + t0, line + t1, y0, y1, a0, a1, c);
        }
        // ...and one standing out from the wall's face on one side (-1 or +1), from out0 to out1
        void Proud(float side, float a0, float a1, float y0, float y1, float out0, float out1, Color c)
        {
            float t0 = side * (HatchHalf + out0), t1 = side * (HatchHalf + out1);
            Along(a0, a1, y0, y1, Mathf.Min(t0, t1), Mathf.Max(t0, t1), c);
        }
        // a point by the wall: 'a' along it, 'outward' from its face on one side
        Vector3 Point(float side, float a, float y, float outward)
        {
            float t = line + side * (HatchHalf + outward);
            return alongX ? new Vector3(a, y, t) : new Vector3(t, y, a);
        }

        // the wall: candy stripes either side of the hole, a wooden front under the shelf, plain above
        float h0 = centre - HatchW / 2f, h1 = centre + HatchW / 2f;
        foreach (Vector2 jamb in new[] { new Vector2(c0, h0), new Vector2(h1, c1) })
        {
            if (alongX) Striped(mb, jamb.x, jamb.y, Y0, StripeTop, line - HatchHalf, line + HatchHalf, colour, IsleKit.Cream, 0.3f);
            else Striped(mb, line - HatchHalf, line + HatchHalf, Y0, StripeTop, jamb.x, jamb.y, colour, IsleKit.Cream, 0.3f);
        }
        Along(h0, h1, Y0, Sill - Plank, -HatchHalf, HatchHalf, IsleKit.Wood);
        Along(h0, h1, Sill + HatchH, StripeTop, -HatchHalf, HatchHalf, colour);
        Along(c0, c1, StripeTop, Ceil, -HatchHalf, HatchHalf, colour);

        // the shelf: the plank through the hole, and on each side its wider end with the lip and two brackets under it
        Along(h0, h1, Sill - Plank, Sill, -HatchHalf, HatchHalf, IsleKit.Wood);
        float s0 = centre - ShelfW / 2f, s1 = centre + ShelfW / 2f;
        foreach (float side in new[] { -1f, 1f })
        {
            Proud(side, s0, s1, Sill - Plank, Sill, 0f, ShelfOut, IsleKit.Wood);
            Proud(side, s0, s1, Sill, Sill + Lip, ShelfOut - 0.03f, ShelfOut, IsleKit.DarkWood);
            Proud(side, s0, s0 + 0.03f, Sill, Sill + Lip, 0f, ShelfOut - 0.03f, IsleKit.DarkWood);
            Proud(side, s1 - 0.03f, s1, Sill, Sill + Lip, 0f, ShelfOut - 0.03f, IsleKit.DarkWood);
            foreach (float at in new[] { centre - 0.3f, centre + 0.3f })
                Proud(side, at - 0.03f, at + 0.03f, Sill - Plank - 0.28f, Sill - Plank, 0f, 0.3f, IsleKit.DarkWood);

            // the wreath over the hole, and the lamp in its middle
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Kit.AddBall(mb, Point(side, centre + Mathf.Cos(a) * 0.2f, LampH + Mathf.Sin(a) * 0.2f, 0.04f), 0.045f, IsleKit.Green);
            }
            Kit.AddBall(mb, Point(side, centre, LampH - 0.2f, 0.07f), 0.04f, IsleKit.Red);
            Vector3 at8 = Point(side, centre, LampH, DepthLight8);
            float yaw = alongX ? (side > 0f ? 0f : 180f) : (side > 0f ? 90f : -90f);
            Piece lamp = P("Light 8", at8.x, at8.z, LampH, yaw).Named("Hatch " + number + " Lamp " + (side < 0f ? lowRoom : highRoom))
                .Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 3f);
            lamps.Add(lamp.Component("PuzzleLight"));
        }
        OnPad("Hatch Wall " + number, mb, bare);
    }

    // A wrapped present: a box with a ribbon round it both ways, its middle at the item's origin (where it is held)
    static Mesh PresentMesh(Color wrap, Color ribbon)
    {
        var mb = new MeshBuilder();
        Vector3 s = PresentSize;
        Kit.AddBox(mb, new Vector3(0f, -s.y / 2f, 0f), s, wrap);
        Kit.AddBox(mb, new Vector3(0f, -s.y / 2f - 0.003f, 0f), new Vector3(s.x + 0.012f, s.y + 0.006f, 0.07f), ribbon);
        Kit.AddBox(mb, new Vector3(0f, -s.y / 2f - 0.003f, 0f), new Vector3(0.07f, s.y + 0.006f, s.z + 0.012f), ribbon);
        return mb.ToMesh();
    }

    // A reindeer silhouette (dress) at (x, z) facing +x or -x
    static void Reindeer(MeshBuilder mb, float x, float z, float dir)
    {
        Color c = IsleKit.DarkWood;
        Block(mb, x - 0.5f, x + 0.5f, 0.75f, 1.15f, z - 0.17f, z + 0.17f, IsleKit.Wood);                 // body
        foreach (float lx in new[] { x - 0.4f, x + 0.4f })
            foreach (float lz in new[] { z - 0.12f, z + 0.12f })
                Block(mb, lx - 0.04f, lx + 0.04f, 0f, 0.75f, lz - 0.04f, lz + 0.04f, c);              // legs
        float nx = x + dir * 0.5f;
        Block(mb, nx - 0.1f, nx + 0.1f, 1.05f, 1.55f, z - 0.08f, z + 0.08f, IsleKit.Wood);              // neck
        Block(mb, Mathf.Min(nx, nx + dir * 0.32f), Mathf.Max(nx, nx + dir * 0.32f), 1.5f, 1.68f, z - 0.09f, z + 0.09f, IsleKit.Wood);   // head
        Kit.AddBall(mb, new Vector3(nx + dir * 0.34f, 1.6f, z), 0.05f, IsleKit.Red);                    // the nose
        foreach (float az in new[] { z - 0.1f, z + 0.1f })
        {
            Block(mb, nx - 0.02f, nx + 0.02f, 1.68f, 2.0f, az - 0.02f, az + 0.02f, c);                 // antlers
            Block(mb, nx - dir * 0.15f - 0.02f, nx - dir * 0.15f + 0.02f, 1.8f, 1.95f, az - 0.02f, az + 0.02f, c);
        }
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(38f, 48f, 0f, -0.5f);
        b.DebugTeleport(-12f, -21f, 2f);
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        Piece root = b.RootPiece;
        // No jetpacks over the puzzle: nobody flies onto the shed's roof
        IsleKit.CalmAir(b, "Sleigh Shed", 0f, -0.5f, new Vector3(46f, 120f, 56f), 0f, -10f, false);

        // ---- the pad: one level slab under the shed, its yard and the start corner, sunk 1.5 m ----
        var pad = new MeshBuilder();
        Block(pad, -17f, 17f, -1.75f, 0f, -23f, 22f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildRooms();
        Gate("Workshop Gate", WorkshopDoor, 180f);
        Gate("Chamber 1 Gate", Chamber1Door, -90f);
        Gate("Chamber 2 Gate", Chamber2Door, 180f);
        Gate("Sleigh Bay Gate", SleighDoor, 90f);
        BuildFittings();
        BuildYard();

        // ---- the hatches, where the three doors stood ----
        var lamps = new List<Component>();
        HatchWall(1, true, -10f, -6.7f, -4.3f, -5.5f, IsleKit.Red, "Workshop", "Chamber 1", lamps);
        HatchWall(2, false, 0.2f, -2.9f, -0.5f, -1.7f, IsleKit.Green, "Chamber 1", "Chamber 2", lamps);
        HatchWall(3, true, 4.0f, 7.3f, 9.7f, 8.5f, IsleKit.Gold, "Chamber 2", "Sleigh Bay", lamps);

        // ---- the presents: four items in a row on the workshop's bench (never saved: a reload lays them there again) ----
        var presents = new List<Object>();
        for (int i = 0; i < Wraps.Length; i++)
        {
            Piece present = b.Item("Present " + (i + 1), PresentMesh(Wraps[i], Ribbons[i]), bare, BenchX, BenchZ + i * BenchStep,
                Floor + BenchTop + PresentSize.y / 2f + 0.02f, 1f, false);
            Component item = present.Component("PuzzleItem");
            Sdk.Set(item, "savesOnReload", false);
            presents.Add(item);
        }

        // ---- the sleigh's bed counts them: all four inside is the win ----
        Piece load = IsleKit.Sensor(b, "Sleigh Load", -2.9f, 12.3f, new Vector3(3.0f, 0.7f, 1.8f), Floor + BedTop + 0.02f, false)
            .Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", presents.Count);
        load.Add("PuzzlePresenceSensor", "acceptedCustom", presents.ToArray());

        // ---- a present that comes to rest on a roof or a porch is laid back on the bench (nobody can reach it there) ----
        E("Roof Catch", 0f, 0f, 2.45f);
        Piece benchHome = E("Bench Home", BenchX, BenchZ + 1.5f * BenchStep, BenchTop + 0.5f);
        Component back = b.AddCustomScript(E("Roof Return", 0f, 0f, 3.5f), "ItemReturn");
        Sdk.Set(back, "itemPrefix", "Present ");
        Sdk.Set(back, "boxPrefix", "Roof Catch");
        Sdk.Set(back, "boxMin", new Vector3(-12.5f, 0f, -18.5f));      // over every roof and porch, from just under the porches' tops
        Sdk.Set(back, "boxMax", new Vector3(12f, 6f, 16.5f));
        Sdk.Set(back, "home", benchHome.Transform);
        Sdk.Set(back, "spread", 0.15f);
        Sdk.Set(back, "delay", 2f);

        // ---- the start corner, south of the workshop's doorway, facing arriving players ----
        IsleKit.StartCorner(b, -7f, -21.2f, 180f, true, Floor, false);

        // ---- light: room lights inside (Winter Walk), strings under the ceilings, a ring round the site ----
        E("Room Light 1 FFE2B8 6.5 1.0", -7f, -13f, 2.3f);            // the workshop
        E("Room Light 2 FFE2B8 5.5 0.9", -5.5f, -6.5f, 2.3f);         // chamber 1
        E("Room Light 3 FFE2B8 5.0 0.9", -3.2f, -1.7f, 2.3f);
        E("Room Light 4 FFE2B8 5.5 0.9", 3.8f, -1.7f, 2.3f);          // chamber 2
        E("Room Light 5 FFE2B8 5.0 0.9", 8.5f, 0.6f, 2.3f);
        E("Room Light 6 FFE2B8 5.5 0.9", 8.5f, 8.6f, 2.3f);           // the sleigh's room
        E("Room Light 7 FFE2B8 5.5 0.9", 4.6f, 12.3f, 2.3f);
        E("Room Light 8 FFE2B8 6.5 1.1", -2.9f, 12.3f, 2.3f);         // the bay, over the sleigh
        float sh = Floor + 2.45f;
        Kit.LightString(b, "Workshop Lights", new List<Vector3> { new Vector3(-10.6f, sh, -13f), new Vector3(-3.4f, sh, -13f) }, 0.15f, 0.5f, false, false, 171);
        Kit.LightString(b, "Chamber 1 Lights", new List<Vector3> { new Vector3(-5.5f, sh, -9.5f), new Vector3(-5.5f, sh, -1.7f), new Vector3(-0.35f, sh, -1.7f) }, 0.1f, 0.5f, false, false, 172);
        Kit.LightString(b, "Chamber 2 Lights", new List<Vector3> { new Vector3(0.7f, sh, -1.7f), new Vector3(8.5f, sh, -1.7f), new Vector3(8.5f, sh, 3.45f) }, 0.1f, 0.5f, false, false, 173);
        Kit.LightString(b, "Chamber 3 Lights", new List<Vector3> { new Vector3(8.5f, sh, 4.6f), new Vector3(8.5f, sh, 12.3f), new Vector3(3.35f, sh, 12.3f) }, 0.1f, 0.5f, false, false, 174);
        Kit.LightString(b, "Bay Lights", new List<Vector3> { new Vector3(-5.6f, sh, 9.8f), new Vector3(-0.2f, sh, 9.8f), new Vector3(-0.2f, sh, 14.8f), new Vector3(-5.6f, sh, 14.8f) }, 0.15f, 0.5f, false, false, 175);
        Kit.LightString(b, "Hall Lights", new List<Vector3> { new Vector3(-0.6f, sh, 12.3f), new Vector3(2.3f, sh, 12.3f) }, 0.1f, 0.5f, false, false, 176);
        Kit.LightRing(b, "Site Lights", 0f, -0.5f, 33.4f, 44.6f, Floor + 2.7f, false, 177);

        // ---- wiring: the hatch lamps are lit while the puzzle runs; four presents in the sleigh win it ----
        foreach (Component lamp in lamps)
        {
            root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
            root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        }
        load.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");
    }

    // The shed's rooms: walls, roofs and porches, one mesh a room (the game lights a mesh from no more than four lamps,
    // so one mesh for the whole shed would leave most of it dark). Interior faces as the old corridor plan; walls 0.3 m.
    // The walls between rooms are the hatch walls, built apart.
    void BuildRooms()
    {
        Color wall = SnowBlock, roof = IsleKit.Red;

        // the workshop: x -11..-3, z -16..-10; its north wall is hatch 1's line, the hatch wall between its two parts
        var mb = new MeshBuilder();
        WallZ(mb, -16.3f, -10f - HatchHalf, -11.3f, -11f, wall);
        WallZ(mb, -16.3f, -10f - HatchHalf, -3f, -2.7f, wall);
        WallX(mb, -11f, -3f, -16.3f, -16f, wall, WorkshopDoor.x);
        Block(mb, -11.3f, -6.7f, Y0, Ceil, -10f - HatchHalf, -10f + HatchHalf, wall);
        Block(mb, -4.3f, -2.7f, Y0, Ceil, -10f - HatchHalf, -10f + HatchHalf, wall);
        Block(mb, -11.3f, -2.7f, Ceil, RoofTop, -16.3f, -10f + HatchHalf, roof);
        Porch(mb, WorkshopDoor, 180f);
        OnPad("Workshop Walls", mb, snowy);

        // chamber 1: first leg x -6.7..-4.3 north to z -0.5, second leg east along z -2.9..-0.5 to hatch 2 (x 0.2)
        mb = new MeshBuilder();
        WallZ(mb, -10f + HatchHalf, -0.2f, -7f, -6.7f, wall, Chamber1Door.y);
        WallZ(mb, -10f + HatchHalf, -2.9f, -4.3f, -4f, wall);
        WallX(mb, -7f, 0.2f, -0.5f, -0.2f, wall);
        WallX(mb, -4f, 0.2f, -3.2f, -2.9f, wall);
        Block(mb, -7f, -4f, Ceil, RoofTop, -10f + HatchHalf, -0.2f, roof);
        Block(mb, -4f, 0.2f, Ceil, RoofTop, -3.2f, -0.2f, roof);
        Porch(mb, Chamber1Door, -90f);
        OnPad("Chamber 1 Walls", mb, snowy);

        // chamber 2: first leg east along z -2.9..-0.5 to x 9.7, second leg north (x 7.3..9.7) to hatch 3 (z 4)
        mb = new MeshBuilder();
        WallX(mb, 0.2f, 7.3f, -0.5f, -0.2f, wall);
        WallX(mb, 0.2f, 9.7f, -3.2f, -2.9f, wall, Chamber2Door.x);
        WallZ(mb, -0.2f, 4f, 7f, 7.3f, wall);
        WallZ(mb, -3.2f, 4f, 9.7f, 10f, wall);
        Block(mb, 0.2f, 10f, Ceil, RoofTop, -3.2f, -0.2f, roof);
        Block(mb, 7f, 10f, Ceil, RoofTop, -0.2f, 4f, roof);
        Porch(mb, Chamber2Door, 180f);
        OnPad("Chamber 2 Walls", mb, snowy);

        // the sleigh's room: a leg north (x 7.3..9.7) from hatch 3 to z 13.5, west along z 11.1..13.5 through the bay's
        // arch (x 2.8) and the hall, into the bay (x -6..0.2, z 9.3..15.3), which is open to the hall on its east
        mb = new MeshBuilder();
        WallZ(mb, 4f, 10.8f, 7f, 7.3f, wall);
        WallZ(mb, 4f, 13.8f, 9.7f, 10f, wall, SleighDoor.y);
        WallX(mb, 0.2f, 7.3f, 10.8f, 11.1f, wall);
        WallX(mb, 0.2f, 9.7f, 13.5f, 13.8f, wall);
        WallZ(mb, 9f, 15.6f, -6.3f, -6f, wall);
        WallX(mb, -6f, 0.5f, 9f, 9.3f, wall);
        WallX(mb, -6f, 0.5f, 15.3f, 15.6f, wall);
        WallZ(mb, 9.3f, 10.8f, 0.2f, 0.5f, wall);
        WallZ(mb, 13.8f, 15.3f, 0.2f, 0.5f, wall);
        Striped(mb, 2.8f - HatchHalf, 2.8f + HatchHalf, Y0, ArchTop, 11.1f, 12.3f - ArchHalf, IsleKit.Ice, IsleKit.Cream, 0.3f);
        Striped(mb, 2.8f - HatchHalf, 2.8f + HatchHalf, Y0, ArchTop, 12.3f + ArchHalf, 13.5f, IsleKit.Ice, IsleKit.Cream, 0.3f);
        Block(mb, 2.8f - HatchHalf, 2.8f + HatchHalf, ArchTop, Ceil, 11.1f, 13.5f, IsleKit.Ice);
        Block(mb, 7f, 10f, Ceil, RoofTop, 4f, 13.8f, roof);
        Block(mb, 0.5f, 7f, Ceil, RoofTop, 10.8f, 13.8f, roof);
        Block(mb, -6.3f, 0.5f, Ceil, RoofTop, 9f, 15.6f, roof);
        Porch(mb, SleighDoor, 90f);
        OnPad("Sleigh Bay Walls", mb, snowy);
    }

    // What stands indoors, one mesh a room, of a material that takes no snow: each room's floorboards (to the walls' inner faces: the turnstiles stand on the pad), the workshop's benches and crates, the sleigh and its reindeer in the bay
    void BuildFittings()
    {
        // the workshop: the presents' bench on the east wall, a wrapping table on the west one, crates in the corner
        var mb = new MeshBuilder();
        Floorboards(mb, -11f, -3f, -16f, -10f - HatchHalf);
        Block(mb, -3.65f, -3f, 0f, BenchTop - 0.06f, -14.15f, -11.25f, IsleKit.Wood);
        Block(mb, -3.7f, -3f, BenchTop - 0.06f, BenchTop, -14.2f, -11.2f, IsleKit.DarkWood);
        Block(mb, -11f, -10.3f, 0f, 0.85f, -14.6f, -11.6f, IsleKit.Wood);
        Block(mb, -10.85f, -10.75f, 0.85f, 0.95f, -14.3f, -13.3f, IsleKit.Red);              // rolls of paper
        Block(mb, -10.7f, -10.6f, 0.85f, 0.95f, -14.2f, -13.2f, IsleKit.Green);
        Block(mb, -10.55f, -10.45f, 0.85f, 0.95f, -14.4f, -13.4f, IsleKit.Gold);
        Kit.AddDisc(mb, new Vector3(-10.65f, 0.85f, -12.3f), 0.12f, 0.1f, IsleKit.Red, 12);    // a spool of ribbon
        Block(mb, -10.8f, -10f, 0f, 0.7f, -15.8f, -15f, IsleKit.Wood);
        Block(mb, -9.9f, -9.2f, 0f, 0.6f, -15.8f, -15.1f, IsleKit.DarkWood);
        Block(mb, -10.7f, -10.1f, 0.7f, 1.2f, -15.7f, -15.1f, IsleKit.DarkWood);
        OnPad("Workshop Fittings", mb, bare);

        mb = new MeshBuilder();
        Floorboards(mb, -6.7f, -4.3f, -10f + HatchHalf, -0.5f);
        Floorboards(mb, -4.3f, 0.2f - HatchHalf, -2.9f, -0.5f);
        OnPad("Chamber 1 Floor", mb, bare);

        mb = new MeshBuilder();
        Floorboards(mb, 0.2f + HatchHalf, 9.7f, -2.9f, -0.5f);
        Floorboards(mb, 7.3f, 9.7f, -0.5f, 4f - HatchHalf);
        OnPad("Chamber 2 Floor", mb, bare);

        // the sleigh's room: the sleigh, along x at (-3, 12.3), facing the hall: runners, a tub whose top is the bed, a
        // high back, and a board along the front and each side, so the bed is a tray 2.6 x 1.6 m that keeps what is set in it
        mb = new MeshBuilder();
        Floorboards(mb, 7.3f, 9.7f, 4f + HatchHalf, 13.5f);
        Floorboards(mb, 0.2f, 7.3f, 11.1f, 13.5f);
        Floorboards(mb, -6f, 0.2f, 9.3f, 15.3f);
        Block(mb, -4.8f, -1.0f, 0f, 0.12f, 11.5f, 11.62f, IsleKit.Gold);
        Block(mb, -4.8f, -1.0f, 0f, 0.12f, 12.98f, 13.1f, IsleKit.Gold);
        Block(mb, -1.0f, -0.88f, 0f, 0.45f, 11.5f, 11.62f, IsleKit.Gold);
        Block(mb, -1.0f, -0.88f, 0f, 0.45f, 12.98f, 13.1f, IsleKit.Gold);
        Block(mb, -4.6f, -1.4f, 0.12f, BedTop, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -4.6f, -4.2f, BedTop, 1.45f, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -1.6f, -1.4f, BedTop, 0.95f, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -4.2f, -1.6f, BedTop, 0.95f, 11.4f, 11.5f, IsleKit.Gold);
        Block(mb, -4.2f, -1.6f, BedTop, 0.95f, 13.1f, 13.2f, IsleKit.Gold);
        Reindeer(mb, -5.2f, 9.8f, 1f);
        Reindeer(mb, -3.8f, 9.8f, 1f);
        Reindeer(mb, -2.4f, 9.8f, 1f);
        Reindeer(mb, -5.2f, 14.8f, 1f);
        OnPad("Sleigh Bay Fittings", mb, bare);

        // a gold star on the bay's south wall over the sleigh
        var star = new MeshBuilder();
        Kit.AddStar(star, new Vector3(-3.0f, 2.2f, 9.36f), 0.22f, 0.04f, IsleKit.Gold);
        OnPad("Bay Star", star, bare, false);
    }

    // Out of doors: the reindeer pen against the bay's north wall (a rail fence, open on its east side) and a few pines
    // where the pointsmen's stations stood
    void BuildYard()
    {
        var mb = new MeshBuilder();
        foreach (Vector2 post in new[] { new Vector2(-5.3f, 15.7f), new Vector2(-5.3f, 17.2f), new Vector2(-5.3f, 18.7f), new Vector2(-3.0f, 18.7f),
                                         new Vector2(-0.7f, 18.7f), new Vector2(-0.7f, 17.6f), new Vector2(-0.7f, 16.4f), new Vector2(-0.7f, 15.7f) })
            Block(mb, post.x - 0.06f, post.x + 0.06f, Y0, 1.1f, post.y - 0.06f, post.y + 0.06f, IsleKit.DarkWood);
        foreach (float y in new[] { 0.45f, 0.9f })
        {
            Block(mb, -5.33f, -5.27f, y, y + 0.1f, 15.6f, 18.7f, IsleKit.Wood);
            Block(mb, -5.3f, -0.7f, y, y + 0.1f, 18.67f, 18.73f, IsleKit.Wood);
            Block(mb, -0.73f, -0.67f, y, y + 0.1f, 17.6f, 18.7f, IsleKit.Wood);
            Block(mb, -0.73f, -0.67f, y, y + 0.1f, 15.6f, 16.4f, IsleKit.Wood);
        }
        Reindeer(mb, -4.0f, 17.7f, 1f);
        Reindeer(mb, -2.4f, 16.6f, -1f);

        // the pines are only drawn; a core inside each trunk is what a player bumps into
        var pines = new MeshBuilder();
        Vector3[] trees = { new Vector3(-14.2f, 4.6f, 3.5f), new Vector3(-12.5f, 3.8f, 13.5f), new Vector3(5.5f, 4.2f, 18.6f), new Vector3(14.2f, 5.0f, 16.5f), new Vector3(14.5f, 4.0f, -9.5f) };   // x, height, z
        for (int i = 0; i < trees.Length; i++)
        {
            Kit.AddPine(pines, new Vector3(trees[i].x, 0f, trees[i].z), trees[i].y, 310 + i);
            Block(mb, trees[i].x - 0.07f, trees[i].x + 0.07f, Y0, trees[i].y * 0.3f, trees[i].z - 0.07f, trees[i].z + 0.07f, IsleKit.DarkWood);
        }
        OnPad("Shed Yard", mb, snowy);
        OnPad("Shed Pines", pines, snowy, false);
    }
}
