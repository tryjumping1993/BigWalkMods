// Stocking Sort: Christmas Eve at the Lakeside Cottage. Which present goes in which stocking?
//
// Five stockings hang along the mantel of the cottage's Parlour, each under its own emblem (moon, holly, star, tree,
// bell): the five slots of a Peg Board. Eight wrapped presents (Peg Tiles with our own pictures) stand on two
// sideboards by the door, opposite the mantel. Every stocking has one right present and nobody is shown which: the
// Guess script (PuzzleScripts) draws a new hidden answer at every start. Hang a present in each of the five stockings
// and the row of five lamps over the mantel shows how many stockings are right, never which. Swap a present or two
// and read the lamps again; five lit lamps win. There is no clock and a wrong guess costs nothing. One player can work
// it out alone; more can share the carrying, the remembering and the arguing. The Parlour is an ordinary open room: a
// plain doorway and nothing soundproof.
// (Before the user's rules of 2026-11-24 it was a three-room relay on an eight-minute clock: a Reader sealed in a
// Letter Office read the wishes into the headphones of a chair on the roof, a Wrapper there posted presents down a
// chimney chute, and a Hanger sealed in the Parlour hung them; three buttons held at once checked the board, and a
// wrong check stopped the puzzle.)
//
// The presents are drawn from four features (paper red or green, dots or stripes, a gold or silver ribbon, tall or
// flat) so that any two differ in at least two of them: each is easy to name aloud. Their pictures are images 1 to 8
// of the puzzle's own sheet (PresentSheet.png, the SDK's Custom set). The game draws a Peg Tile's picture in grey, so
// in play the paper's colour does not show: dots or stripes, a dark or a light ribbon and tall or flat are what a
// player sees, and those three still tell all eight apart (any two differ in two of the four features, so never only
// in the paper).
//
// The roof deck and its outside stair are a lookout now, with the chimney (solid: nothing goes down it) at its back.
//
// Coordinates: local metres from the cottage's centre, x east, z north; heights above the pad top (Floor above the
// root), every piece placed onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using System.IO;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class StockingSortPuzzle : PuzzleDef
{
    public override string Name => "StockingSort";
    public override Vector2 Footprint => new Vector2(52f, 24f);
    public override Vector2? FixedSite => new Vector2(-951f, -1350f);      // layout-final: a level east-west shelf by East Lowland, woods behind, 133 m from the coast

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Deck = 3.2f;            // the roof deck's walking surface (on the Parlour's 3.16 roof slab)
    const string SheetPath = "Assets/Puzzles/StockingSort/PresentSheet.png";

    // Emblems: the stockings' (0 star, 1 bell, 2 tree, 3 moon, 4 holly) and the count board's (5 stocking, 6 tick)
    const int Star = 0, Bell = 1, Tree = 2, Moon = 3, Holly = 4, Sock = 5, Tick = 6;
    static readonly int[] Stockings = { Moon, Holly, Star, Tree, Bell };      // the board's slots, a player's left to right
    const float FirstSlotX = -0.9f;     // the Peg Board's pivot, at its first slot (the board runs 2 m east from it)
    const float SlotPitch = 0.45f;      // PuzzlePegBoard slot spacing

    // The count board over the mantel: its face (the mantel shelf's front edge is at z 2.0), its lamps' height and
    // their spacing (closer than the stockings', so no lamp reads as one stocking's own)
    const float CountFace = 2.1f, CountHigh = 2.45f, LampPitch = 0.3f;
    // The present sideboards against the south wall, x ShelfIn..ShelfOut either side of the doorway
    const float ShelfTop = 0.85f, ShelfFront = -2.5f, ShelfIn = 1.0f, ShelfOut = 3.3f;

    static readonly Color Brick = new Color(0.6f, 0.32f, 0.25f);
    static readonly Color Soot = new Color(0.1f, 0.08f, 0.08f);
    static readonly Color Warm = new Color(1f, 0.85f, 0.65f);
    static readonly Color CountLamp = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one
    static readonly Color Crimson = new Color(0.55f, 0.10f, 0.12f);
    static readonly Color Berry = new Color(0.8f, 0.08f, 0.1f);
    static readonly Color Leaf = new Color(0.08f, 0.38f, 0.15f);

    PuzzleBuilder b;

    // ================================================================ mesh helpers (pad-top coordinates)

    // A block between two corners (local x, y above the pad top, z)
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A triangle wound so its front faces along 'facing' (Unity's front is the side Cross(b - a, c - a) points to)
    static void TriF(MeshBuilder mb, Vector3 a, Vector3 bb, Vector3 c, Vector3 facing, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(bb - a, c - a), facing) < 0f) mb.Tri(a, c, bb, color);
        else mb.Tri(a, bb, c, color);
    }

    // A closed solid: a convex polygon in the (z, y) plane, extruded along x from x0 to x1 (wedges, sloped floors, ramps)
    static void PrismX(MeshBuilder mb, float x0, float x1, Vector2[] zy, Color color)
    {
        Vector2 c = Vector2.zero;
        foreach (Vector2 p in zy) c += p;
        c /= zy.Length;
        Vector3 V(float x, Vector2 p) => new Vector3(x, p.y, p.x);
        for (int i = 0; i < zy.Length; i++)
        {
            Vector2 a = zy[i], d = zy[(i + 1) % zy.Length], mid = (a + d) / 2f;
            TriF(mb, V(x1, c), V(x1, a), V(x1, d), Vector3.right, color);
            TriF(mb, V(x0, c), V(x0, a), V(x0, d), Vector3.left, color);
            var outward = new Vector3(0f, mid.y - c.y, mid.x - c.x);
            TriF(mb, V(x0, a), V(x1, a), V(x1, d), outward, color);
            TriF(mb, V(x0, a), V(x1, d), V(x0, d), outward, color);
        }
    }

    // A flat frame on a wall: origin on the wall's face, R the viewer's right, U up, F out of the wall towards the viewer
    struct Frame { public Vector3 O, R, U, F; }

    // The frame at 'onWall' for something facing 'faceYaw' (as parts: 0 faces +z, 90 +x, 180 -z, -90 -x)
    static Frame Wall(Vector3 onWall, float faceYaw)
    {
        float a = faceYaw * Mathf.Deg2Rad;
        var f = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        return new Frame { O = onWall, F = f, U = Vector3.up, R = Vector3.Cross(Vector3.up, -f) };
    }

    static Vector3 At(Frame fr, Vector2 p, float depth) => fr.O + fr.R * p.x + fr.U * p.y + fr.F * depth;

    // A flat shape standing out of a wall from depth d0 to d1: a polygon star-shaped about 'c' (front and sides; the
    // back lies on the wall)
    static void Poly(MeshBuilder mb, Frame fr, Vector2[] pts, Vector2 c, float d0, float d1, Color color)
    {
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i], d = pts[(i + 1) % pts.Length], mid = (a + d) / 2f;
            TriF(mb, At(fr, c, d1), At(fr, a, d1), At(fr, d, d1), fr.F, color);
            Vector3 outward = fr.R * (mid.x - c.x) + fr.U * (mid.y - c.y);
            TriF(mb, At(fr, a, d0), At(fr, d, d0), At(fr, d, d1), outward, color * 0.85f);
            TriF(mb, At(fr, a, d0), At(fr, d, d1), At(fr, a, d1), outward, color * 0.85f);
        }
    }

    static Vector2[] Rect(float x0, float x1, float y0, float y1)
        => new[] { new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0) };

    static void RectOn(MeshBuilder mb, Frame fr, float x0, float x1, float y0, float y1, float d0, float d1, Color color)
        => Poly(mb, fr, Rect(x0, x1, y0, y1), new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f), d0, d1, color);

    static Vector2[] Ellipse(Vector2 c, float rx, float ry, float turnDeg, int segments = 14)
    {
        var pts = new Vector2[segments];
        float t = turnDeg * Mathf.Deg2Rad;
        for (int i = 0; i < segments; i++)
        {
            float a = 2f * Mathf.PI * i / segments;
            float x = Mathf.Cos(a) * rx, y = Mathf.Sin(a) * ry;
            pts[i] = c + new Vector2(x * Mathf.Cos(t) - y * Mathf.Sin(t), x * Mathf.Sin(t) + y * Mathf.Cos(t));
        }
        return pts;
    }

    static void DiscOn(MeshBuilder mb, Frame fr, Vector2 c, float r, float d0, float d1, Color color)
        => Poly(mb, fr, Ellipse(c, r, r, 0f, 12), c, d0, d1, color);

    // A straight bar from a to b, 'width' wide
    static void BarOn(MeshBuilder mb, Frame fr, Vector2 a, Vector2 d, float width, float d0, float d1, Color color)
    {
        Vector2 along = (d - a).normalized, n = new Vector2(-along.y, along.x) * width / 2f;
        Poly(mb, fr, new[] { a - n, a + n, d + n, d - n }, (a + d) / 2f, d0, d1, color);
    }

    // An emblem 'size' across centred on the frame's origin, standing out of the wall from depth d; 'bg' is the colour
    // behind it (the moon's bite)
    static void Emblem(MeshBuilder mb, Frame fr, int which, float size, float d, Color bg)
    {
        float t = Mathf.Max(0.004f, size * 0.06f), s = size;
        Vector2 V(float x, float y) => new Vector2(x * s, y * s);
        switch (which)
        {
            case Star:
            {
                var pts = new Vector2[10];
                for (int i = 0; i < 10; i++)
                {
                    float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / 10f, r = (i % 2 == 0 ? 0.5f : 0.21f) * s;
                    pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                }
                Poly(mb, fr, pts, Vector2.zero, d, d + t, IsleKit.Gold);
                break;
            }
            case Bell:
            {
                var pts = new[]
                {
                    V(0f, 0.45f), V(0.12f, 0.42f), V(0.2f, 0.32f), V(0.24f, 0.1f), V(0.28f, -0.15f), V(0.4f, -0.3f), V(0.4f, -0.36f),
                    V(-0.4f, -0.36f), V(-0.4f, -0.3f), V(-0.28f, -0.15f), V(-0.24f, 0.1f), V(-0.2f, 0.32f), V(-0.12f, 0.42f),
                };
                Poly(mb, fr, pts, Vector2.zero, d, d + t, IsleKit.Gold);
                DiscOn(mb, fr, V(0f, -0.42f), 0.08f * s, d, d + 1.5f * t, IsleKit.Gold * 0.8f);
                break;
            }
            case Tree:
                for (int k = 0; k < 3; k++)
                {
                    float y0 = -0.3f + 0.22f * k, y1 = y0 + 0.38f, hw = 0.42f - 0.11f * k;
                    Poly(mb, fr, new[] { V(-hw, y0), V(0f, y1), V(hw, y0) }, V(0f, y0 + 0.12f), d, d + t * (1f + 0.15f * k), IsleKit.Green);
                }
                RectOn(mb, fr, -0.06f * s, 0.06f * s, -0.46f * s, -0.3f * s, d, d + t, IsleKit.Wood);
                break;
            case Moon:
                DiscOn(mb, fr, Vector2.zero, 0.4f * s, d, d + t, IsleKit.Cream);
                DiscOn(mb, fr, V(0.17f, 0.09f), 0.33f * s, d, d + 1.4f * t, bg);
                break;
            case Holly:
                Poly(mb, fr, Ellipse(V(-0.17f, -0.08f), 0.28f * s, 0.11f * s, 25f), V(-0.17f, -0.08f), d, d + t, Leaf);
                Poly(mb, fr, Ellipse(V(0.17f, -0.08f), 0.28f * s, 0.11f * s, -25f), V(0.17f, -0.08f), d, d + t, Leaf);
                DiscOn(mb, fr, V(-0.07f, 0.06f), 0.075f * s, d, d + 2f * t, Berry);
                DiscOn(mb, fr, V(0.07f, 0.06f), 0.075f * s, d, d + 2f * t, Berry);
                DiscOn(mb, fr, V(0f, 0.18f), 0.075f * s, d, d + 2f * t, Berry);
                break;
            case Sock:
                RectOn(mb, fr, -0.12f * s, 0.12f * s, -0.2f * s, 0.3f * s, d, d + t, IsleKit.Red);
                RectOn(mb, fr, -0.12f * s, 0.3f * s, -0.4f * s, -0.2f * s, d, d + t, IsleKit.Red);
                RectOn(mb, fr, -0.16f * s, 0.16f * s, 0.24f * s, 0.4f * s, d, d + 1.5f * t, IsleKit.Cream);
                break;
            case Tick:
                BarOn(mb, fr, V(-0.3f, 0f), V(-0.08f, -0.28f), 0.13f * s, d, d + t, IsleKit.Cream);
                BarOn(mb, fr, V(-0.08f, -0.28f), V(0.32f, 0.3f), 0.13f * s, d, d + 1.15f * t, IsleKit.Cream);
                break;
        }
    }

    // A gold-framed dark plaque 'size' across with an emblem on it, on a wall at 'onWall' facing 'faceYaw'
    static void Plaque(MeshBuilder mb, Vector3 onWall, float faceYaw, int which, float size)
    {
        Frame fr = Wall(onWall, faceYaw);
        float h = size / 2f, inner = size * 0.43f;
        RectOn(mb, fr, -h, h, -h, h, 0f, 0.012f, IsleKit.Gold);
        RectOn(mb, fr, -inner, inner, -inner, inner, 0.012f, 0.02f, IsleKit.Board);
        Emblem(mb, fr, which, size * 0.72f, 0.02f, IsleKit.Board);
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

    // Sets an enum field by its name (Sdk.Set takes the index, and PuzzlePegSet's values have gaps)
    static void SetEnumByName(Component c, string field, string value)
    {
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.propertyType != SerializedPropertyType.Enum) throw new System.Exception(c.GetType().Name + " has no enum '" + field + "'");
        int index = System.Array.IndexOf(p.enumNames, value);
        if (index < 0) throw new System.Exception(c.GetType().Name + "." + field + " has no value '" + value + "'");
        p.enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================ build

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(52f, 24f);
        b.DebugTeleport(2f, -6f, 2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Sdk.Set(b.Puzzle, "gourdColor", Crimson);      // a deep crimson gourd: a hard puzzle
        // No jetpacks over the yard: nobody flies onto the roof
        IsleKit.CalmAir(b, "Stocking Sort", 0f, 0f, new Vector3(70f, 120f, 40f), 0f, -10f, false);

        // ---- the pad: one level slab, sunk 1.5 m so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -26f, 26f, -1.75f, 0f, -12f, 12f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        Texture2D sheet = PresentSheet(SheetPath);

        BuildCottage(snowy, bare);
        BuildMantel(bare);

        // ================= the Parlour =================
        // The stockings: a five-slot Peg Board on the chimney breast. Its Answer From stays empty: Guess holds the answer
        Piece stockingBoard = P("Peg Board", FirstSlotX, 2.4f, 1.3f, 180f).Named("Stocking Board").Set("PuzzlePegBoard", "size", 4);      // Slots5
        // The SDK's build check refuses a Peg Board with no Answer From ("can never be correct"), so the board is
        // given five one-flap Displays buried 2 m under the pad, where nobody sees them. Nothing reads them: the Guess
        // script holds the real answer and wins the puzzle itself, and the board has no Validator.
        var buried = new Object[5];
        for (int k = 0; k < 5; k++)
            buried[k] = P("Display", -1f + 0.5f * k, 0f, -2.5f, 0f).Named("Buried Answer " + (k + 1)).Set("PuzzleDisplay", "size", 0).Component("PuzzleDisplay");
        Sdk.Add(stockingBoard.Component("PuzzlePegBoard"), "answerFrom", buried);
        Sdk.AddInts(stockingBoard.Component("PuzzlePegBoard"), "answerFromFlap", 1, 1, 1, 1, 1);
        // The count: five lamps in a tight row on the board over the mantel, lit from the left. A Light 8's back is
        // 0.1 m behind its pivot, and the board's paint is 0.02 m thick; a short range, since they are there to be read
        for (int k = 0; k < 5; k++)
            P("Light 8", LampPitch * (k - 2), CountFace - 0.02f - 0.1f, CountHigh, 180f).Named("Right Lamp " + (k + 1))
                .Set("PuzzleLight", "color", CountLamp).Set("PuzzleLight", "range", 0.4f);
        // The presents stand on the sideboards facing the room, 0.24 m above the tops: 1 to 4 (the red papers) east of
        // the door and 5 to 8 (the green) west of it, in order from the left for a player facing them
        for (int k = 0; k < 8; k++)
        {
            float x = k < 4 ? ShelfIn + 0.4f + 0.5f * (3 - k) : -(ShelfIn + 0.4f + 0.5f * (k - 4));
            Piece tile = P("Peg Tile", x, ShelfFront - 0.35f, ShelfTop + 0.24f, 0f).Named("Present " + (k + 1));
            Component tileC = tile.Component("PuzzlePegTile");
            SetEnumByName(tileC, "imageSet", "Custom");
            Sdk.Set(tileC, "image", k + 1);
            Sdk.Set(tileC, "customSheet", sheet);
            Sdk.Set(tileC, "invisibleInkView", false);
        }
        // A second start knob just inside the door, the room's lamp (lit while the puzzle runs) and confetti for the win
        P("Switch Start", 0.81f, -3.18f, 1.25f, 0f).Named("Parlour Knob");
        P("Live Light", 0.81f, -3.25f, 1.42f, 0f).Named("Parlour Live Light");
        Piece parlourLamp = P("Light 2", 0f, -0.4f, 3.0f).Named("Parlour Lamp").Set("PuzzleLight", "color", Warm);
        P("Confetti", 0f, 0.8f, 0.2f).Named("Parlour Confetti");
        // Always on (Winter Walk's RoomLights), so the Parlour can be read at night: a ceiling light for the whole room,
        // and the fire's glow, kept to the hearth so that it does not colour the presents in the stockings
        b.Empty("Room Light 1 FFE2B8 8.0 1.2", 0f, 0f, Floor + 2.6f, false);
        b.Empty("Room Light 2 FF8A3C 0.9 0.9", 0f, 2.15f, Floor + 0.2f, false);

        // ================= the yard: start corner, tree, lights =================
        IsleKit.StartCorner(b, -6f, -8f, 0f, true, Floor, false);
        BuildYard(snowy, bare);

        // ================= the rule: a hidden answer, lamps that count, five right win (Guess.cs) =================
        Component guess = b.AddCustomScript(b.Empty("Stocking Guess", 0f, 0f, Floor + 0.5f, false), "Guess");
        Sdk.Set(guess, "board", "Stocking Board");
        Sdk.Set(guess, "lampPrefix", "Right Lamp ");
        Sdk.Set(guess, "images", 8);
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", guess, "Begin");
        root.On("PuzzleSDK", "onPuzzleStop", guess, "End");
        Component lampC = parlourLamp.Component("PuzzleLight");
        root.On("PuzzleSDK", "onPuzzleStart", lampC, "TurnOn");
        root.On("PuzzleSDK", "onPuzzleStop", lampC, "TurnOff");
    }

    // ================================================================ the cottage: Parlour, roof deck, stair

    void BuildCottage(Material snowy, Material bare)
    {
        const float y0 = -0.1f, H = 3.0f, D = Deck;
        Color wall = IsleKit.Wood, slab = IsleKit.DarkWood;

        // ---- the walls (ordinary ones: voices pass), the roof slab, the chimney breast and the stack on the roof ----
        var shell = new MeshBuilder();
        Block(shell, -4.5f, -0.62f, y0, H, -3.5f, -3.25f, wall);                              // south, with the open doorway x -0.62..0.62
        Block(shell, 0.62f, 4.5f, y0, H, -3.5f, -3.25f, wall);
        Block(shell, -0.62f, 0.62f, 2.04f, H, -3.5f, -3.25f, wall);
        Block(shell, -4.5f, 4.5f, y0, H, 3.25f, 3.5f, wall);                                  // north
        Block(shell, -4.5f, -4.25f, y0, H, -3.25f, 3.25f, wall);                              // west
        Block(shell, 4.25f, 4.5f, y0, H, -3.25f, 3.25f, wall);                                // east
        Block(shell, -4.5f, 4.5f, H, 3.16f, -3.5f, 3.5f, slab);                               // the roof slab (top 3.16)
        Block(shell, -2.3f, 2.3f, y0, H, 2.4f, 3.25f, Brick);                                 // the chimney breast
        Block(shell, 1.0f, 2.6f, 3.16f, D + 2.4f, 2.3f, 3.5f, Brick);                         // the stack on the deck
        Block(shell, 0.95f, 2.65f, D + 2.4f, D + 2.55f, 2.25f, 3.55f, IsleKit.Stone);         // its cap
        OnPad("Parlour Walls", shell, snowy);

        // ---- inside the Parlour (no snow indoors): floorboards, mantel, firebox, hearth, overmantel, sideboards ----
        var inside = new MeshBuilder();
        Block(inside, -4.25f, 4.25f, 0f, 0.04f, -3.25f, 3.25f, IsleKit.Wood);                 // floorboards
        Block(inside, -2.5f, 2.5f, 2.0f, 2.12f, 2.0f, 2.4f, IsleKit.DarkWood);                // the mantel shelf
        Block(inside, -0.7f, 0.7f, 0.04f, 0.36f, 2.38f, 2.4f, Soot);                          // the firebox
        Block(inside, -1.4f, 1.4f, 0.04f, 0.07f, 2.0f, 2.4f, IsleKit.Stone);                  // the hearth stone
        Block(inside, -1.15f, 1.15f, 2.12f, 2.78f, CountFace, 2.4f, IsleKit.DarkWood);        // the overmantel: the count board stands on the shelf
        // the present sideboards: a cupboard to the floor (nothing rolls under it), a crimson top at ShelfTop against
        // the wall, a 0.05 lip along the front and at the ends
        foreach (float x0 in new[] { -ShelfOut, ShelfIn })
        {
            float x1 = x0 + ShelfOut - ShelfIn;
            Block(inside, x0 + 0.05f, x1 - 0.05f, 0.04f, ShelfTop - 0.06f, -3.25f, ShelfFront - 0.06f, IsleKit.DarkWood);
            Block(inside, x0, x1, ShelfTop - 0.06f, ShelfTop, -3.25f, ShelfFront, Crimson);
            Block(inside, x0, x1, ShelfTop, ShelfTop + 0.05f, ShelfFront - 0.04f, ShelfFront, IsleKit.DarkWood);
            Block(inside, x0, x0 + 0.04f, ShelfTop, ShelfTop + 0.05f, -3.25f, ShelfFront - 0.04f, IsleKit.DarkWood);
            Block(inside, x1 - 0.04f, x1, ShelfTop, ShelfTop + 0.05f, -3.25f, ShelfFront - 0.04f, IsleKit.DarkWood);
        }
        OnPad("Parlour Fittings", inside, bare);

        // ---- the roof deck (on the slab) and its parapet, the chimney pot ----
        var deck = new MeshBuilder();
        Block(deck, -4.3f, 1.0f, 3.16f, D, -3.3f, 3.3f, IsleKit.Wood);
        Block(deck, 2.6f, 4.3f, 3.16f, D, -3.3f, 3.3f, IsleKit.Wood);
        Block(deck, 1.0f, 2.6f, 3.16f, D, -3.3f, 2.3f, IsleKit.Wood);
        Block(deck, 4.3f, 4.52f, 3.16f, D, 0.7f, 1.9f, IsleKit.Wood);                         // across the parapet's gap to the stair
        OnPad("Roof Deck", deck, snowy);

        var parapet = new MeshBuilder();
        const float top = D + 1.1f;
        Block(parapet, -4.5f, 4.5f, 3.16f, top, -3.5f, -3.3f, wall);                          // south
        Block(parapet, -4.5f, -4.3f, 3.16f, top, -3.3f, 3.3f, wall);                          // west
        Block(parapet, -4.5f, 1.0f, 3.16f, top, 3.3f, 3.5f, wall);                            // north, either side of the stack
        Block(parapet, 2.6f, 4.5f, 3.16f, top, 3.3f, 3.5f, wall);
        Block(parapet, 4.3f, 4.5f, 3.16f, top, -3.3f, 0.7f, wall);                            // east, with the stair's gap z 0.7..1.9
        Block(parapet, 4.3f, 4.5f, 3.16f, top, 1.9f, 3.3f, wall);
        Kit.AddDisc(parapet, new Vector3(1.8f, D + 2.55f, 2.9f), 0.2f, 0.45f, Brick * 0.8f);   // the chimney pot
        OnPad("Roof Parapet", parapet, snowy);

        // ---- the outside stair on the east side: 12 steps and a landing at Deck, rising north from z -5.6 (25 degrees);
        //      its collider is a ramp through the treads' middles, so no step needs climbing ----
        const float z0 = -5.6f, zLand = 0.7f;
        float tread = (zLand - z0) / 12f, rise = D / 13f;
        var steps = new MeshBuilder();
        for (int k = 0; k < 12; k++)
            Block(steps, 4.6f, 5.8f, 0f, (k + 1) * rise, z0 + k * tread, z0 + (k + 1) * tread, IsleKit.Stone);
        Piece stair = OnPad("Stair", steps, snowy);
        var ramp = new MeshBuilder();
        float slope = rise / tread, low = rise / 2f;
        PrismX(ramp, 4.6f, 5.8f, new[] { new Vector2(z0, 0f), new Vector2(z0, low), new Vector2(zLand, low + slope * (zLand - z0)), new Vector2(zLand, 0f) }, IsleKit.Stone);
        stair.Go.GetComponent<MeshCollider>().sharedMesh = b.SaveMesh("Stair Ramp", ramp.ToMesh());

        var walls = new MeshBuilder();
        Block(walls, 4.52f, 5.8f, 0f, D, zLand, 1.9f, IsleKit.Stone);                         // the landing
        PrismX(walls, 5.8f, 5.95f, new[] { new Vector2(z0, 0f), new Vector2(z0, 1.0f), new Vector2(zLand, D + 1.0f), new Vector2(2.05f, D + 1.0f), new Vector2(2.05f, 0f) }, IsleKit.Stone);
        PrismX(walls, 4.52f, 4.6f, new[] { new Vector2(z0, 0f), new Vector2(z0, 1.0f), new Vector2(zLand, D + 1.0f), new Vector2(zLand, 0f) }, IsleKit.Stone);
        Block(walls, 4.52f, 5.8f, 0f, D + 1.0f, 1.9f, 2.05f, IsleKit.Stone);                 // the landing's north rail
        OnPad("Stair Walls", walls, snowy);
    }

    // ================================================================ the mantel: stockings, emblems, the count board

    // Painted reliefs with no collider: a stocking under each of the Peg Board's slots with its emblem on the cuff and
    // on a plaque above, and the count board's face on the overmantel (a stocking, room for the five lamps, a tick)
    void BuildMantel(Material bare)
    {
        var mb = new MeshBuilder();
        for (int i = 0; i < Stockings.Length; i++)
        {
            float x = FirstSlotX + SlotPitch * i;
            Color sock = i % 2 == 0 ? IsleKit.Red : IsleKit.Green;
            Block(mb, x - 0.15f, x + 0.15f, 0.4f, 0.95f, 2.33f, 2.39f, sock);                 // leg
            Block(mb, x - 0.15f, x + 0.2f, 0.4f, 0.56f, 2.33f, 2.385f, sock);                 // foot
            Block(mb, x - 0.17f, x + 0.17f, 0.95f, 1.06f, 2.32f, 2.395f, IsleKit.Cream);      // cuff, below a hung tile (1.076)
            Emblem(mb, Wall(new Vector3(x, 1.005f, 2.32f), 180f), Stockings[i], 0.09f, 0f, IsleKit.Cream);
            Plaque(mb, new Vector3(x, 1.78f, 2.4f), 180f, Stockings[i], 0.28f);
        }
        Frame count = Wall(new Vector3(0f, CountHigh, CountFace), 180f);
        RectOn(mb, count, -1.08f, 1.08f, -0.26f, 0.26f, 0f, 0.012f, IsleKit.Gold);
        RectOn(mb, count, -1.04f, 1.04f, -0.22f, 0.22f, 0.012f, 0.02f, IsleKit.Board);
        Emblem(mb, Wall(new Vector3(-0.86f, CountHigh, CountFace), 180f), Sock, 0.24f, 0.02f, IsleKit.Board);
        Emblem(mb, Wall(new Vector3(0.86f, CountHigh, CountFace), 180f), Tick, 0.24f, 0.02f, IsleKit.Board);
        OnPad("Mantel Paint", mb, bare, false);
    }

    // ================================================================ the yard

    void BuildYard(Material snowy, Material bare)
    {
        var tree = new MeshBuilder();
        Kit.AddPine(tree, Vector3.zero, 3.4f, 1301);
        b.MeshPiece("Yard Tree", tree.ToMesh(), snowy, false).Transform.position = b.WorldPoint(-7f, -2f, Floor, false);
        // wrapped boxes under it (scenery, not items)
        var gifts = new MeshBuilder();
        var rng = new System.Random(1302);
        Color[] papers = { IsleKit.Red, IsleKit.Green, new Color(0.15f, 0.35f, 0.85f), IsleKit.Gold, IsleKit.Cream };
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f + 0.3f, r = 1.1f + (float)rng.NextDouble() * 0.4f;
            float w = 0.28f + (float)rng.NextDouble() * 0.2f, h = 0.2f + (float)rng.NextDouble() * 0.25f, yaw = (float)rng.NextDouble() * 90f;
            var foot = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Color paper = papers[i % papers.Length], ribbon = i % 2 == 0 ? IsleKit.Gold : IsleKit.Cream;
            Kit.AddBox(gifts, foot, new Vector3(w, h, w), paper, yaw);
            Kit.AddBox(gifts, foot, new Vector3(w + 0.01f, h + 0.01f, 0.05f), ribbon, yaw);
            Kit.AddBox(gifts, foot, new Vector3(0.05f, h + 0.01f, w + 0.01f), ribbon, yaw);
        }
        b.MeshPiece("Yard Presents", gifts.ToMesh(), bare, false).Transform.position = b.WorldPoint(-7f, -2f, Floor, false);

        // Christmas lights: the cottage's eaves, the parapet, the stair (no posts: the wire lies on the building), and a
        // ring on posts round the yard
        float F = Floor;
        Kit.LightString(b, "Eaves Lights", new List<Vector3> { new Vector3(-4.5f, F + 2.95f, -3.56f), new Vector3(0f, F + 2.95f, -3.56f), new Vector3(4.5f, F + 2.95f, -3.56f) },
            0.2f, 0.5f, false, false, 1311);
        Kit.LightString(b, "Parapet Lights", new List<Vector3>
            {
                new Vector3(-4.55f, F + Deck + 1.15f, 3.55f), new Vector3(-4.55f, F + Deck + 1.15f, -3.55f),
                new Vector3(4.55f, F + Deck + 1.15f, -3.55f), new Vector3(4.55f, F + Deck + 1.15f, 0.6f),
            }, 0.12f, 0.5f, false, false, 1312);
        Kit.LightString(b, "Parapet Lights North", new List<Vector3>
            { new Vector3(4.55f, F + Deck + 1.15f, 2.0f), new Vector3(4.55f, F + Deck + 1.15f, 3.55f), new Vector3(2.65f, F + Deck + 1.15f, 3.55f) },
            0.12f, 0.5f, false, false, 1313);
        Kit.LightString(b, "Stair Lights", new List<Vector3>
            { new Vector3(5.95f, F + 1.05f, -5.6f), new Vector3(5.95f, F + Deck + 1.05f, 0.7f), new Vector3(5.95f, F + Deck + 1.05f, 2.05f) },
            0.1f, 0.5f, false, false, 1314);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 50f, 22f, F + 2.6f, false, 133);
    }

    // ================================================================ the presents' picture sheet

    // Draws the eight presents (spec section 3's table: even-parity words over paper, pattern, ribbon, shape) on a
    // 2048 px sheet in the SDK's custom format: a 4 x 4 grid read across then down from the top-left, cell 0 blank,
    // image N in cell N. Written only when it changes, so a build does not re-import it every time.
    static Texture2D PresentSheet(string assetPath)
    {
        const int Size = 2048, Cell = Size / 4;
        var px = new Color[Size * Size];
        var cream = new Color(0.95f, 0.92f, 0.84f);
        for (int i = 0; i < px.Length; i++) px[i] = cream;

        // rows from the top: cell r (from the top) covers pixel rows (3 - r) * Cell .. (4 - r) * Cell (pixel 0 is the bottom)
        for (int image = 1; image <= 8; image++)
        {
            int col = image % 4, row = image / 4;
            DrawPresent(px, Size, col * Cell, (3 - row) * Cell, Cell, image);
        }

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        bool changed = !File.Exists(full) || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(full), png);
        if (changed)
        {
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer == null) throw new System.Exception("Could not import " + assetPath);
        bool dirty = false;
        if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; dirty = true; }
        if (!importer.sRGBTexture) { importer.sRGBTexture = true; dirty = true; }
        if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; dirty = true; }
        if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (importer.alphaIsTransparency) { importer.alphaIsTransparency = false; dirty = true; }
        if (importer.maxTextureSize != Size) { importer.maxTextureSize = Size; dirty = true; }
        if (importer.textureCompression != TextureImporterCompression.CompressedHQ) { importer.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
        if (dirty) importer.SaveAndReimport();
        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (sheet == null) throw new System.Exception("No texture at " + assetPath);
        return sheet;
    }

    // One present in the cell whose bottom-left pixel is (ox, oy): paper red (1-4) or green (5-8); dots (1, 2, 5, 6) or
    // stripes; a gold (odd) or silver (even) ribbon with a bow; tall (2, 3, 5, 8) or flat (1, 4, 6, 7)
    static void DrawPresent(Color[] px, int size, int ox, int oy, int cell, int image)
    {
        bool red = image <= 4, dots = image == 1 || image == 2 || image == 5 || image == 6, gold = image % 2 == 1;
        bool tall = image == 2 || image == 3 || image == 5 || image == 8;
        Color paper = red ? new Color(0.82f, 0.1f, 0.1f) : new Color(0.07f, 0.5f, 0.2f);
        Color white = new Color(0.98f, 0.97f, 0.94f), ink = new Color(0.15f, 0.1f, 0.1f);
        Color ribbon = gold ? new Color(0.98f, 0.78f, 0.18f) : new Color(0.86f, 0.88f, 0.93f);
        Color edge = gold ? new Color(0.72f, 0.52f, 0.08f) : new Color(0.5f, 0.53f, 0.62f);
        float s = cell / 512f;      // drawn at 512 px a cell, scaled

        int w = Mathf.RoundToInt((tall ? 250 : 380) * s), h = Mathf.RoundToInt((tall ? 330 : 220) * s), bow = Mathf.RoundToInt(90 * s);
        int x0 = ox + (cell - w) / 2, y0 = oy + (cell - (h + bow)) / 2, x1 = x0 + w, y1 = y0 + h;
        int cx = ox + cell / 2, cy = (y0 + y1) / 2;

        void Put(int x, int y, Color c) { if (x >= ox && x < ox + cell && y >= oy && y < oy + cell) px[y * size + x] = c; }
        void Box(int ax, int ay, int bx, int by, Color c) { for (int y = ay; y < by; y++) for (int x = ax; x < bx; x++) Put(x, y, c); }
        void Oval(float ex, float ey, float rx, float ry, Color c)
        {
            for (int y = Mathf.FloorToInt(ey - ry); y <= Mathf.CeilToInt(ey + ry); y++)
                for (int x = Mathf.FloorToInt(ex - rx); x <= Mathf.CeilToInt(ex + rx); x++)
                {
                    float dx = (x - ex) / rx, dy = (y - ey) / ry;
                    if (dx * dx + dy * dy <= 1f) Put(x, y, c);
                }
        }

        // a soft shadow, the outline, the paper
        Oval(cx, y0 - 4 * s, w * 0.6f, 18 * s, new Color(0.8f, 0.76f, 0.68f));
        int o = Mathf.RoundToInt(7 * s);
        Box(x0 - o, y0 - o, x1 + o, y1 + o, ink);
        Box(x0, y0, x1, y1, paper);
        // the pattern, in white
        float period = 64f * s;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float u = x - x0, v = y - y0;
                bool on;
                if (dots)
                {
                    int rowN = Mathf.FloorToInt(v / period);
                    float du = Mathf.Repeat(u + (rowN % 2) * period / 2f, period) - period / 2f, dv = Mathf.Repeat(v, period) - period / 2f;
                    on = du * du + dv * dv <= 20f * s * 20f * s;
                }
                else on = Mathf.Repeat(u + v, period) < 22f * s;
                if (on) Put(x, y, white);
            }
        // the ribbon's cross, with darker edges
        int band = Mathf.RoundToInt(50 * s), e = Mathf.RoundToInt(5 * s);
        Box(cx - band / 2 - e, y0, cx + band / 2 + e, y1, edge);
        Box(cx - band / 2, y0, cx + band / 2, y1, ribbon);
        Box(x0, cy - band / 2 - e, x1, cy + band / 2 + e, edge);
        Box(x0, cy - band / 2, x1, cy + band / 2, ribbon);
        Box(cx - band / 2, cy - band / 2, cx + band / 2, cy + band / 2, ribbon);
        // the bow: two loops and a knot on top
        float loopY = y1 + 38 * s;
        foreach (int side in new[] { -1, 1 })
        {
            Oval(cx + side * 50 * s, loopY, 56 * s, 38 * s, ink);
            Oval(cx + side * 50 * s, loopY, 48 * s, 30 * s, edge);
            Oval(cx + side * 50 * s, loopY, 42 * s, 25 * s, ribbon);
            Oval(cx + side * 56 * s, loopY, 18 * s, 10 * s, edge);
        }
        Oval(cx, y1 + 16 * s, 24 * s, 24 * s, ink);
        Oval(cx, y1 + 16 * s, 18 * s, 18 * s, ribbon);
    }
}
