// Summit Shot: the Lob House on the summit's crest, a puzzle for two with no clock.
//
// One player, the thrower, stands in the timber pen and lobs snowballs over a 3.3 m wall into four hanging baskets in
// the glass gallery beyond it. From the pen the baskets cannot be seen. The other, the spotter, stands on the deck
// outside the gallery's glass, sees the baskets and where every ball lands, and simply talks the thrower on: nothing
// here blocks or muffles a voice. Each basket counts once, in any order: the lamp beside it (on the side the deck
// sees) lights, and the bars on the deck and in the pen go up one. Four lamps win the gourd. A miss costs nothing: a
// ball that stays in the gallery, on the wall's cap or in the gallery's exit is laid back in the tray at the thrower's
// feet a moment later (ItemReturn; the game pins a prop that comes to rest, so the gallery's sloped floor rolls
// nothing back). Anyone walks into and out of the pen through its two porches. Nobody can walk into the gallery, so no
// ball is carried to a basket; its one door is a turnstile that only lets out.
// (The user's rules of 2026-11-24: fewer timers and sealed rooms, and some puzzles one or two players can finish.
// Before that the thrower was blindfolded behind turnstiles, a caller in a windowless hut read the wanted basket off a
// lamp panel into an intercom, and eight in a row had to be made against a clock.)
//
// Coordinates: the house's own (x east, z north, metres; heights above the pad's top, every piece onGround: false on
// the one level pad). The pad (x -24..12.5, z -8..24) is centred on (-5.75, 8); the root sits at that centre (the
// island levels a circle of radius Footprint.magnitude / 2 round the root), so the helpers add (OX, OZ) to turn house
// coordinates into the root's. Yaw: 0 faces +z, 90 +x, -90 -x, 180 -z.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SummitShotPuzzle : PuzzleDef
{
    public override string Name => "SummitShot";
    public override Vector2 Footprint => new Vector2(37f, 32f);
    public override Vector2? FixedSite => new Vector2(-2211.9f, -1922f);      // layout-final: the crest beside the summit

    // house coordinates + (OX, OZ) = the root's local coordinates
    const float OX = 5.75f, OZ = -8f;
    const float Floor = 0.25f;           // the pad's top above the root

    // ---- the throw. A change of layout is an edit here and a rebuild ----
    const float WallSolid = 3.0f;        // the lob wall's solid top (the cap's flat underside)
    const float WallTop = 3.3f;          // the cap's top at the gallery face
    const float CapFrontTop = 3.05f;     // the cap's top at its front edge, overhanging the pen
    const float CapOver = 0.3f;          // how far the cap overhangs the pen
    const float ToeZ = -2.0f;
    const float RoofH = 6.5f;            // pen and gallery
    static readonly float[] BX = { -3.0f, 2.6f, -0.6f, 3.2f };
    static readonly float[] BZ = { 2.8f, 3.6f, 4.6f, 5.8f };
    static readonly float[] BH = { 1.6f, 2.2f, 1.2f, 1.8f };      // rim heights above the pad
    const float RimR = 0.42f, RimW = 0.06f, SkirtDeep = 0.6f, SkirtBottomR = 0.32f;
    const float PostOut = RimR + RimW / 2f;      // a basket's post (6 cm square) east of the basket's centre

    // A basket's lamp is the game's round flood lamp (Light 6: 0.48 m across, its back 0.142 m behind its pivot, its
    // middle 0.017 m below it) on a board on the east face of the basket's post, just under the rim: the deck's side
    const float LampBoard = 0.56f, LampBoardThick = 0.04f;
    const float LampOut = PostOut + 0.03f + LampBoardThick + 0.142f + 0.01f;      // the lamp's pivot east of the basket's centre
    const float LampDown = RimW + 0.02f + LampBoard / 2f - 0.017f;                // and below the rim's top
    static readonly Color LampWhite = new Color(1f, 0.92f, 0.75f);      // near-white: dull when off, a bright disc when lit

    // ---- the house ----
    const float PenFlat = 0.2f;          // the pen's floor south of the toe mark
    const float PenDoorS = -3.8f, PenDoorN = -2.6f;      // the pen's two doorways, in its west and east walls (1.2 m wide)
    const float PorchH = 2.8f;           // porch and vestibule walls
    const float Deck = 1.4f;             // the spotter's deck top
    const float VestFloor = 0.55f;       // the gallery's exit vestibule floor
    static readonly Color Timber = new Color(0.45f, 0.32f, 0.2f);

    PuzzleBuilder b;

    // ---------------------------------------------------------------- helpers (house coordinates)

    Piece P(string prefab, float x, float z, float h, float yaw = 0f) => b.Part(prefab, x + OX, z + OZ, Floor + h, yaw, false);
    Piece E(string name, float x, float z, float h) => b.Empty(name, x + OX, z + OZ, Floor + h, false);

    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(OX, OZ, Floor, false);
        return piece;
    }

    // A block between two corners (x, height above the pad, z)
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A box with eight corners of its own: low and high in AddBox's order, (-x-z), (+x-z), (+x+z), (-x+z); same winding
    static void Hull(MeshBuilder mb, Vector3[] low, Vector3[] high, Color color)
    {
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }

    // A column over a rectangle from y0 up to a top with its own height at each corner (a sloped floor's cell)
    static void Column(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float t00, float t10, float t11, float t01, Color color)
    {
        Hull(mb,
            new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) },
            new[] { new Vector3(x0, t00, z0), new Vector3(x1, t10, z0), new Vector3(x1, t11, z1), new Vector3(x0, t01, z1) }, color);
    }

    // Both faces of a quad (thin shells balls hit from inside and players see from outside)
    static void TwoSided(MeshBuilder mb, Vector3 a, Vector3 c, Vector3 d, Vector3 e, Color color)
    {
        mb.Quad(a, c, d, e, color);
        mb.Quad(a, e, d, c, color);
    }

    // A Presence Sensor with a box collider: centre (x, z, height above the pad), size in Unity's (x, y, z)
    Piece Sensor(string name, float x, float z, float h, Vector3 size)
    {
        Piece zone = P("Presence Sensor", x, z, h).Named(name);
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        return zone;
    }

    // A catching box for ItemReturn over one space (two corners: x, height above the pad, z). The script has a single
    // box, read in the own frame of every object named "Catch ...": here that box is the unit cube, and each object
    // sits at the middle of its space, scaled to the space's size (as Calm Air and the "Indoors" markers are)
    void Catch(string name, float x0, float x1, float h0, float h1, float z0, float z1)
    {
        Piece box = E("Catch " + name, (x0 + x1) / 2f, (z0 + z1) / 2f, (h0 + h1) / 2f);
        box.Transform.localScale = new Vector3(x1 - x0, h1 - h0, z1 - z0);
    }

    // The pen floor's sloped band (z ToeZ..0): PenFlat along the toe mark, falling to 0.06 + 0.028|x| at the wall's foot
    static float PenY(float x, float z)
    {
        float foot = 0.06f + 0.028f * Mathf.Abs(x);
        return foot + (PenFlat - foot) * Mathf.Clamp01(-z / -ToeZ);
    }

    // The gallery floor: 5 % up towards the north, 4 % up away from x = 0 (0.03 at its south edge), and the gutter
    static float GalleryY(float x, float z) => 0.03f + 0.05f * (z - 0.6f) + 0.04f * Mathf.Abs(x);
    static float GutterY(float x) => 0.02f * Mathf.Abs(x);

    // ---------------------------------------------------------------- build

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(38f, 34f);
        b.DebugTeleport(8f + OX, 10.8f + OZ);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks over the house: nobody flies onto the roofs or over the lob wall into the gallery
        IsleKit.CalmAir(b, "Summit Shot", 0f, 0f, new Vector3(50f, 150f, 46f), 0f, -10f, false);

        // ---- the pad: one level slab, sunk 1.5 m ----
        var pad = new MeshBuilder();
        Block(pad, -24f, 12.5f, -1.75f, 0f, -8f, 24f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildFloors(bare);
        BuildShell(snowy);
        BuildGallery(bare);
        BuildDeck(snowy);
        OnPad("Baskets", Baskets(), snowy);

        // ---- snowballs: twelve, in the tray at the wall's foot. Carried in the hand only, and never saved: a reload
        // puts all twelve back in the tray ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Object>();
        for (int k = 0; k < 12; k++)
        {
            float x = -0.75f + 0.3f * (k % 6), z = k < 6 ? -0.3f : -0.65f;
            Piece ball = b.Item("Snowball " + (k + 1), Kit.AsLooseSnow(Kit.Ball(0.14f, 8, 12, 0.1f, 4100 + k)), snow, x + OX, z + OZ, Floor + 0.15f, 0.6f, false);
            Component item = ball.Component("PuzzleItem");
            Sdk.Set(item, "savesOnReload", false);
            balls.Add(item);
        }

        // ---- the tally: each basket counts once, in any order; four marked wins (Tally.cs finds the lamps
        // "Basket Lamp 1..4" and every "Tally Bar..." by name, and wins the puzzle itself) ----
        Component tally = b.AddCustomScript(E("Basket Tally", 0f, 4.1f, 0.5f), "Tally");
        Sdk.Set(tally, "count", BX.Length);
        Sdk.Set(tally, "lampPrefix", "Basket Lamp ");
        Sdk.Set(tally, "barPrefix", "Tally Bar");

        // ---- the baskets' zones and lamps ----
        for (int i = 0; i < BX.Length; i++)
        {
            // the zone: 0.2 tall, 0.15..0.35 below the rim, wholly inside the skirt; only these twelve snowballs count,
            // and one passing through is enough
            Piece zone = Sensor("Basket " + (i + 1), BX[i], BZ[i], BH[i] - 0.25f, new Vector3(0.5f, 0.2f, 0.5f))
                .Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            zone.Add("PuzzlePresenceSensor", "acceptedCustom", balls.ToArray());
            zone.On("PuzzlePresenceSensor", "onItemEntered", tally, "Hit", i + 1);
            // its lamp on the post's board, facing east to the deck: lit once the basket has been hit
            P("Light 6", BX[i] + LampOut, BZ[i], BH[i] - LampDown, 90f).Named("Basket Lamp " + (i + 1))
                .Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", 3f);
        }

        // ---- the count, one dot a basket: on the deck's board for the spotter, on the lob wall for the thrower ----
        P("Progress Bar", 7.5f, 1.6f, Deck + 2.0f, 0f).Named("Tally Bar Deck").Scaled(0.8f, 0.8f, 0.8f)
            .Set("PuzzleProgressBar", "total", BX.Length).Set("PuzzleProgressBar", "style", 0);
        P("Progress Bar", 0f, -0.092f, 2.3f, 180f).Named("Tally Bar Pen").Scaled(0.8f, 0.8f, 0.8f)
            .Set("PuzzleProgressBar", "total", BX.Length).Set("PuzzleProgressBar", "style", 0);

        IsleKit.StartCorner(b, 8f + OX, 13f + OZ, 180f, true, Floor, false);

        // ---- the gallery's only door: a turnstile that lets out, never in ----
        P("Gate No items", -0.5f, 9.36f, VestFloor, -90f).Named("Gallery Exit")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

        // ---- every ball comes back: one that stays 2.5 s anywhere no player can reach is laid in the tray again ----
        Catch("Gallery", -5f, 5f, -0.2f, RoofH - 0.05f, 0.1f, 8.0f);                           // the gallery's inside, from the slot under the wall
        Catch("Wall Top", -5f, 5f, WallSolid, 4.0f, -CapOver - 0.1f, 0.1f);                    // a ball lying on the wall's cap
        Catch("Vestibule", -1.6f, 4.6f, VestFloor - 0.1f, VestFloor + PorchH, 8.0f, 10.45f);   // one that flew out through the gallery's doorway
        Piece ballHome = E("Ball Home", 0f, -0.5f, 0.6f);
        Component back = b.AddCustomScript(E("Ball Return", 0f, -0.5f, 1.5f), "ItemReturn");
        Sdk.Set(back, "itemPrefix", "Snowball ");
        Sdk.Set(back, "boxPrefix", "Catch ");
        Sdk.Set(back, "boxMin", new Vector3(-0.5f, -0.5f, -0.5f));
        Sdk.Set(back, "boxMax", new Vector3(0.5f, 0.5f, 0.5f));
        Sdk.Set(back, "home", ballHome.Transform);
        Sdk.Set(back, "spread", 0.25f);
        Sdk.Set(back, "delay", 2.5f);

        // Where Winter Walk should not snow (MineDriver's "Indoors ..." convention)
        E("Indoors Lob House", 0f, 2f, RoofH / 2f).Transform.localScale = new Vector3(10.5f, RoofH, 12.5f);

        // Light for the rooms at night (Winter Walk's RoomLights): the pen's, high enough to shine over the wall, and
        // the gallery's under its roof
        E("Room Light 1 FFE2B8 7.0 1.1", 0f, -2.0f, 4.2f);
        E("Room Light 2 FFE2B8 8.0 1.0", 0f, 4.4f, 5.8f);

        // ---- wiring: a stop (and the spawn) clears the lamps and the bars ----
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", tally, "Clear");

        // ---- winter dressing ----
        var ring = new List<Vector3>();
        float[] ringX = { -23.5f, -17.5f, -11.5f, -5.75f, 0.25f, 6.25f, 12f };
        float[] ringZ = { -7.5f, -1.5f, 4.5f, 10.5f, 16.5f, 23.5f };
        foreach (float x in ringX) ring.Add(new Vector3(x + OX, Floor + 2.6f, -7.5f + OZ));
        for (int k = 1; k < ringZ.Length; k++) ring.Add(new Vector3(12f + OX, Floor + 2.6f, ringZ[k] + OZ));
        for (int k = ringX.Length - 2; k >= 0; k--) ring.Add(new Vector3(ringX[k] + OX, Floor + 2.6f, 23.5f + OZ));
        for (int k = ringZ.Length - 2; k >= 0; k--) ring.Add(new Vector3(-23.5f + OX, Floor + 2.6f, ringZ[k] + OZ));
        Kit.LightString(b, "Site Lights", ring, 0.15f, 0.5f, false, true, 311);
        Kit.LightString(b, "Deck Lights", new List<Vector3> { new Vector3(5.85f + OX, Floor + Deck + 2.5f, 1.4f + OZ), new Vector3(5.85f + OX, Floor + Deck + 2.5f, 8.1f + OZ) },
            0.1f, 0.4f, false, true, 312);
        Vector3[] drifts = { new Vector3(-20.2f, 1.3f, 0.8f), new Vector3(-17.4f, 6.8f, 0.9f), new Vector3(-23.6f, 7.0f, 1.0f), new Vector3(2.9f, 17.4f, 1.0f), new Vector3(-2.8f, 23.6f, 0.9f), new Vector3(10.5f, -3.0f, 1.1f) };
        for (int i = 0; i < drifts.Length; i++)
            b.MeshPiece("Lob House Drift " + (i + 1), Kit.Mound(drifts[i].z, 0.4f, 330 + i), snowy, false).Transform.position = b.WorldPoint(drifts[i].x + OX, drifts[i].y + OZ, Floor - 0.05f, false);
    }

    // ---------------------------------------------------------------- geometry

    // The pen's floor (flat south of the toe mark, the sloped band to the wall round the tray), the toe mark and the
    // strip leading to it, the porches' ramps; the gallery's floor and gutter; the vestibule's floor and steps
    void BuildFloors(Material bare)
    {
        var mb = new MeshBuilder();
        Color plank = Timber, mark = IsleKit.Gold;
        const float y0 = -0.05f;
        // pen: flat part and the doorways' sills
        Block(mb, -5f, 5f, y0, PenFlat, -4f, ToeZ, plank);
        Block(mb, -5.25f, -5f, y0, PenFlat, PenDoorS, PenDoorN, plank);
        Block(mb, 5f, 5.25f, y0, PenFlat, PenDoorS, PenDoorN, plank);
        // pen: the sloped band, leaving the tray (x -0.9..0.9, z -0.9..0) at the pad's level
        float[] xs = { -5f, -4f, -3f, -2f, -0.9f, 0f, 0.9f, 2f, 3f, 4f, 5f };
        float[] zs = { ToeZ, -1.5f, -0.9f, -0.45f, 0f };
        for (int i = 0; i + 1 < xs.Length; i++)
            for (int j = 0; j + 1 < zs.Length; j++)
            {
                float xa = xs[i], xb = xs[i + 1], za = zs[j], zb = zs[j + 1];
                if (xa >= -0.9f && xb <= 0.9f && za >= -0.9f) continue;      // the tray
                Column(mb, xa, xb, za, zb, y0, PenY(xa, za), PenY(xb, za), PenY(xb, zb), PenY(xa, zb), plank);
            }
        // the toe mark (two halves either side of a notch at x = 0) and a strip leading to it from the south wall
        Block(mb, -0.3f, -0.05f, PenFlat, PenFlat + 0.03f, ToeZ - 0.06f, ToeZ + 0.06f, mark);
        Block(mb, 0.05f, 0.3f, PenFlat, PenFlat + 0.03f, ToeZ - 0.06f, ToeZ + 0.06f, mark);
        Block(mb, -0.15f, 0.15f, PenFlat, PenFlat + 0.03f, -3.8f, ToeZ - 0.1f, mark);
        // the porches' floors rise to the pen's floor at its wall: a ramp 1.35 m long, the porch's whole depth
        Column(mb, -6.6f, -5.25f, -4f, -0.5f, y0, 0f, PenFlat, PenFlat, 0f, plank);
        Column(mb, 5.25f, 6.6f, -4f, -0.5f, y0, PenFlat, 0f, 0f, PenFlat, plank);

        // gallery: the gutter along the wall's foot, then the floor in 1 m cells (split at x = 0 for the cross-fall)
        float[] gx = { -5f, -4f, -3f, -2f, -1f, -0.6f, 0f, 0.6f, 1f, 2f, 3f, 4f, 5f };
        for (int i = 0; i + 1 < gx.Length; i++)
        {
            float xa = gx[i], xb = gx[i + 1];
            Column(mb, xa, xb, 0.25f, 0.6f, y0, GutterY(xa), GutterY(xb), GutterY(xb), GutterY(xa), plank);
        }
        float[] gz = { 0.6f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.25f, 8.0f };
        for (int i = 0; i + 1 < gx.Length; i++)
            for (int j = 0; j + 1 < gz.Length; j++)
            {
                float xa = gx[i], xb = gx[i + 1], za = gz[j], zb = gz[j + 1];
                Column(mb, xa, xb, za, zb, y0, GalleryY(xa, za), GalleryY(xb, za), GalleryY(xb, zb), GalleryY(xa, zb), plank);
            }
        // the slot's floor under the wall is the pad itself (x -0.6..0.6, z 0..0.25)

        // vestibule: floor level with the gallery's at the doorway, the doorway's sill, steps down at the open west end
        Block(mb, -1.6f, 4.6f, y0, VestFloor, 8.0f, 10.45f, plank);
        Block(mb, 3.4f, 4.4f, VestFloor, VestFloor + 0.25f, 8.0f, 8.25f, IsleKit.DarkWood);
        Block(mb, -2.0f, -1.6f, y0, VestFloor * 2f / 3f, 8.12f, 10.45f, IsleKit.Stone);
        Block(mb, -2.4f, -2.0f, y0, VestFloor / 3f, 8.12f, 10.45f, IsleKit.Stone);
        OnPad("House Floors", mb, bare);
    }

    // The timber shell: pen walls and roof, both porches (an open doorway in each one's south wall, another from the
    // porch into the pen), the gallery's exit vestibule with its baffle, roof and turnstile hood; and the lob wall
    // with its cap. Ordinary walls, which voices pass.
    void BuildShell(Material snowy)
    {
        var mb = new MeshBuilder();
        Color wall = Timber, roof = IsleKit.DarkWood, trim = IsleKit.Red;
        const float y0 = -0.1f;
        float doorTop = PenFlat + 2.2f;

        // pen: west and east walls with their doorways at the south end, the south wall, the roof
        foreach (float side in new[] { -1f, 1f })
        {
            float xa = side < 0 ? -5.25f : 5f, xb = side < 0 ? -5f : 5.25f;
            Block(mb, xa, xb, y0, RoofH, -4.25f, PenDoorS, wall);
            Block(mb, xa, xb, y0, RoofH, PenDoorN, 0.25f, wall);
            Block(mb, xa, xb, doorTop, RoofH, PenDoorS, PenDoorN, wall);
        }
        Block(mb, -5.25f, 5.25f, y0, RoofH, -4.25f, -4f, wall);
        Block(mb, -5.4f, 5.4f, RoofH, RoofH + 0.2f, -4.4f, 0.25f, roof);

        // porches: inside x -9.0..-5.25 (west) and 5.25..9.0 (east), z -4..-0.5. The way in is a doorway in the south
        // wall, |x| 6.9..8.3 less a red frame 8 cm wide that stands 4 cm proud of the wall (1.24 m clear, 2.22 m high)
        const float doorA = 6.9f, doorB = 8.3f, frame = 0.08f, porchDoor = 2.3f, proudS = -4.29f, proudN = -3.96f;
        foreach (float side in new[] { -1f, 1f })
        {
            float S(float x) => side * x;
            float Lo(float a, float c) => Mathf.Min(S(a), S(c));
            float Hi(float a, float c) => Mathf.Max(S(a), S(c));
            Block(mb, Lo(9.25f, 9.0f), Hi(9.25f, 9.0f), y0, PorchH, -4.25f, -0.25f, wall);           // outer side
            Block(mb, Lo(9.25f, 5.25f), Hi(9.25f, 5.25f), y0, PorchH, -0.5f, -0.25f, wall);          // north
            Block(mb, Lo(9.25f, doorB), Hi(9.25f, doorB), y0, PorchH, -4.25f, -4f, wall);            // south, either side of the doorway
            Block(mb, Lo(doorA, 5.25f), Hi(doorA, 5.25f), y0, PorchH, -4.25f, -4f, wall);
            Block(mb, Lo(doorA, doorB), Hi(doorA, doorB), porchDoor, PorchH, -4.25f, -4f, wall);     // and over it
            Block(mb, Lo(doorA, doorA + frame), Hi(doorA, doorA + frame), y0, porchDoor - frame, proudS, proudN, trim);
            Block(mb, Lo(doorB - frame, doorB), Hi(doorB - frame, doorB), y0, porchDoor - frame, proudS, proudN, trim);
            Block(mb, Lo(doorA, doorB), Hi(doorA, doorB), porchDoor - frame, porchDoor, proudS, proudN, trim);
            Block(mb, Lo(9.4f, 5.25f), Hi(9.4f, 5.25f), PorchH, PorchH + 0.2f, -4.4f, -0.1f, roof);
        }

        // the exit vestibule: inside x -1.6..4.6, z 8.12 (the gallery's glass) to 10.45, open to the west
        Block(mb, -1.6f, 4.85f, y0, VestFloor + PorchH, 10.45f, 10.7f, wall);                         // north
        Block(mb, 4.6f, 4.85f, y0, VestFloor + PorchH, 8.12f, 10.7f, wall);                           // east
        Block(mb, 1.5f, 1.65f, VestFloor, VestFloor + PorchH, 8.12f, 10.0f, wall);                    // the baffle
        Block(mb, -1.75f, 5.0f, VestFloor + PorchH, VestFloor + PorchH + 0.2f, 8.12f, 10.85f, roof);
        Block(mb, -1.2f, 0.2f, VestFloor + 2.3f, VestFloor + PorchH, 8.12f, 10.45f, wall);           // the turnstile's hood
        Block(mb, -1.2f, 0.2f, VestFloor, VestFloor + 2.3f, 10.37f, 10.45f, trim);                   // the stub to the north wall
        OnPad("Lob House Shell", mb, snowy);

        // the lob wall with the slot under it, and its sloped cap
        var inner = new MeshBuilder();
        Block(inner, -5f, -0.6f, y0, WallSolid, 0f, 0.25f, wall);
        Block(inner, 0.6f, 5f, y0, WallSolid, 0f, 0.25f, wall);
        Block(inner, -0.6f, 0.6f, 0.32f, WallSolid, 0f, 0.25f, wall);
        Hull(inner,
            new[] { new Vector3(-5f, WallSolid, -CapOver), new Vector3(5f, WallSolid, -CapOver), new Vector3(5f, WallSolid, 0.25f), new Vector3(-5f, WallSolid, 0.25f) },
            new[] { new Vector3(-5f, CapFrontTop, -CapOver), new Vector3(5f, CapFrontTop, -CapOver), new Vector3(5f, WallTop, 0.25f), new Vector3(-5f, WallTop, 0.25f) }, trim);
        // the tray's back: a gold band on the wall over the slot, so the slot reads from the pen
        Block(inner, -0.7f, 0.7f, 0.32f, 0.42f, -0.02f, 0f, IsleKit.Gold);
        OnPad("Lob Wall", inner, snowy);
    }

    // The gallery's glass and its frame. The panes are the game's clear glass to look at, but what keeps players and
    // snowballs out is an unseen plain wall of the same shape ("Gallery Walls"): the game muffles a voice that passes
    // through anything solid on its glass layer, the least a PuzzleGlass can be is "muffles", and the spotter on the
    // deck talks to the thrower straight through the east pane. Only the roof, which no voice crosses, is solid glass
    // (the SDK's checks want a collider on every PuzzleGlass).
    void BuildGallery(Material bare)
    {
        var glass = new MeshBuilder();
        var solid = new MeshBuilder();
        const float g = 0.12f, y0 = -0.1f;
        float sill = VestFloor, head = VestFloor + 2.2f;
        foreach (MeshBuilder mb in new[] { glass, solid })
        {
            Block(mb, -5f - g, -5f, y0, RoofH, 0.25f, 8.0f + g, Color.white);       // west
            Block(mb, 5f, 5f + g, y0, RoofH, 0.25f, 8.0f + g, Color.white);         // east
            Block(mb, -5f, 3.4f, y0, RoofH, 8.0f, 8.0f + g, Color.white);           // north, with the vestibule's doorway x 3.4..4.4
            Block(mb, 4.4f, 5f, y0, RoofH, 8.0f, 8.0f + g, Color.white);
            Block(mb, 3.4f, 4.4f, y0, sill, 8.0f, 8.0f + g, Color.white);
            Block(mb, 3.4f, 4.4f, head, RoofH, 8.0f, 8.0f + g, Color.white);
        }
        Block(glass, -5f - g, 5f + g, RoofH, RoofH + g, 0.25f, 8.0f + g, Color.white);      // roof
        Piece pane = OnPad("Gallery Glass", glass, bare, false);
        BoxCollider roof = pane.Go.AddComponent<BoxCollider>();
        roof.center = new Vector3(0f, RoofH + g / 2f, (0.25f + 8.0f + g) / 2f);
        roof.size = new Vector3(10f + 2f * g, g, 8.0f + g - 0.25f);
        Component glassC = b.AddScript(pane, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // clear
        Sdk.Set(glassC, "sound", 0);      // muffles (the roof only: see above)
        Sdk.Set(glassC, "wire", false);
        Piece walls = OnPad("Gallery Walls", solid, bare);
        Object.DestroyImmediate(walls.Go.GetComponent<MeshRenderer>());

        var frame = new MeshBuilder();
        foreach (float x in new[] { -5.06f, 5.06f })
        {
            Block(frame, x - 0.15f, x + 0.15f, y0, RoofH + 0.3f, 7.97f, 8.27f, IsleKit.Red);
            Block(frame, x - 0.1f, x + 0.1f, RoofH + g + 0.01f, RoofH + 0.3f, 0.25f, 8.24f, IsleKit.Gold);
        }
        Block(frame, -5.18f, 5.18f, RoofH + g + 0.01f, RoofH + 0.3f, 8.0f, 8.24f, IsleKit.Gold);
        Block(frame, 3.3f, 3.4f, sill, head, 7.97f, 8.15f, IsleKit.Red);      // the doorway's jambs
        Block(frame, 4.4f, 4.5f, sill, head, 7.97f, 8.15f, IsleKit.Red);
        OnPad("Gallery Frame", frame, IsleKit.Snowy(b));
    }

    // The four baskets: a rim of 16 thin segments, an open skirt narrowing downwards (both sides drawn), a post on the
    // east side, and on the post's east face the board its lamp is mounted on
    MeshBuilder Baskets()
    {
        var mb = new MeshBuilder();
        const int Segs = 16;
        for (int i = 0; i < BX.Length; i++)
        {
            float cx = BX[i], cz = BZ[i], rim = BH[i];
            for (int s = 0; s < Segs; s++)
            {
                float a = 360f * s / Segs, r = RimR + RimW / 2f;
                Vector3 foot = new Vector3(cx + r * Mathf.Cos(a * Mathf.Deg2Rad), rim - RimW, cz + r * Mathf.Sin(a * Mathf.Deg2Rad));
                Kit.AddBox(mb, foot, new Vector3(2f * Mathf.PI * r / Segs + 0.02f, RimW, RimW), IsleKit.Red, 90f - a);
            }
            float top = rim - RimW, bottom = rim - SkirtDeep;
            for (int s = 0; s < Segs; s++)
            {
                float a0 = 2f * Mathf.PI * s / Segs, a1 = 2f * Mathf.PI * (s + 1) / Segs;
                Vector3 u0 = new Vector3(cx + RimR * Mathf.Cos(a0), top, cz + RimR * Mathf.Sin(a0));
                Vector3 u1 = new Vector3(cx + RimR * Mathf.Cos(a1), top, cz + RimR * Mathf.Sin(a1));
                Vector3 d0 = new Vector3(cx + SkirtBottomR * Mathf.Cos(a0), bottom, cz + SkirtBottomR * Mathf.Sin(a0));
                Vector3 d1 = new Vector3(cx + SkirtBottomR * Mathf.Cos(a1), bottom, cz + SkirtBottomR * Mathf.Sin(a1));
                TwoSided(mb, d0, d1, u1, u0, IsleKit.Gold);
            }
            // the post: from the floor to the rim's underside, just outside the rim on the east
            float px = cx + PostOut;
            float floorY = GalleryY(px, cz) - 0.05f;
            Block(mb, px - 0.03f, px + 0.03f, floorY, top, cz - 0.03f, cz + 0.03f, IsleKit.DarkWood);
            // the lamp's board on the post's east face, its top 2 cm under the rim
            float boardTop = top - 0.02f;
            Block(mb, px + 0.03f, px + 0.03f + LampBoardThick, boardTop - LampBoard, boardTop, cz - LampBoard / 2f, cz + LampBoard / 2f, IsleKit.Board);
        }
        return mb;
    }

    // The spotter's deck: x 6..9, z 1.5..8, top 1.4; stairs down at the north end; an open rail along x 6, so the
    // gallery is seen over and through it; the count board on posts at the south end
    void BuildDeck(Material snowy)
    {
        var mb = new MeshBuilder();
        Color wood = Timber, dark = IsleKit.DarkWood, board = IsleKit.Board;
        Block(mb, 6f, 9f, -0.1f, Deck, 1.5f, 8.0f, wood);
        for (int k = 0; k < 5; k++)
            Block(mb, 6f, 9f, -0.1f, Deck * (5 - k) / 6f, 8.0f + 0.5f * k, 8.5f + 0.5f * k, wood);
        // the rail: posts and a top bar
        for (int k = 0; k <= 5; k++)
        {
            float z = Mathf.Lerp(1.55f, 7.95f, k / 5f);
            Block(mb, 5.92f, 6.0f, Deck, Deck + 1.0f, z - 0.04f, z + 0.04f, dark);
        }
        Block(mb, 5.92f, 6.0f, Deck + 0.94f, Deck + 1.02f, 1.5f, 8.0f, dark);
        // the count board on posts at the south end, facing north (its face at z 1.5)
        Block(mb, 6.0f, 9.0f, Deck + 1.72f, Deck + 2.28f, 1.38f, 1.5f, board);
        Block(mb, 6.0f, 6.12f, Deck, Deck + 2.4f, 1.38f, 1.5f, dark);
        Block(mb, 8.88f, 9.0f, Deck, Deck + 2.4f, 1.38f, 1.5f, dark);
        Block(mb, 5.9f, 9.1f, Deck + 2.4f, Deck + 2.5f, 1.2f, 1.7f, dark);
        OnPad("Spotter Deck", mb, snowy);
    }
}
