// Stocking Sort: Christmas Eve at the Lakeside Cottage (spec: puzzle-specs/StockingSort.md, 2026-11-22).
//
// Presents go down the chimney and into the right stockings. Five stockings (six in a 4-player lobby) hang along the
// mantel of the cottage's sealed, soundproof Parlour, each under its own emblem; they are the slots of a Peg Board.
// The wishes are split: Santa's Letter Office, a sealed booth 22 m west, shows three of them on one-flap Displays
// (star, bell, moon); the Parlour shows the other two (tree, holly). The eight presents are Peg Tiles on the Wrapping
// Loft, the cottage's open roof deck, drawn from four features (paper, pattern, ribbon, shape) so that any two differ
// in at least two of them. The Reader's voice reaches only the headphones of the Wrapper's Chair on the Loft; the
// Hanger is heard there and on the Chimney Speaker; the Wrapper answers on the Loft Mic. The Wrapper posts presents
// down the chute beside the fireplace, saying whose each is, and the Hanger hangs them by emblem. A full board lights
// three green "board full" lamps; the check runs only while the office, Loft and Parlour N-Hold buttons are all held.
// Right wins; wrong shows its cross for 3 s and stops the puzzle (doors open, Displays blank, a fresh deal next start).
// An eight-minute mantel clock stops it too. Every room has a start knob, and turning any off opens every door.
//
// Lobby sizes: in 4p the open Ledger Booth (22 m east) shows holly and candle, and a six-slot board replaces the five;
// in 2p the Loft button and the Parlour door are left out and one Runner is both Wrapper and Hanger.
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
    const float Deck = 3.2f;            // the Loft's walking surface (top of the Loft Deck, on the Parlour's 3.16 roof slab)
    const float RoundSeconds = 480f;
    const float ResultSeconds = 3f;
    const float OfficeHigh = 2.8f;      // the offices' wall height
    const string SheetPath = "Assets/Puzzles/StockingSort/PresentSheet.png";

    // Emblems: 0 star, 1 bell, 2 tree, 3 moon, 4 holly, 5 candle, 6 letter (envelope), 7 stocking ("board full")
    const int Star = 0, Bell = 1, Tree = 2, Moon = 3, Holly = 4, Candle = 5, Letter = 6, Sock = 7;
    static readonly int[] Order5 = { Moon, Holly, Star, Tree, Bell };            // Board 5's slots, the Hanger's left to right
    static readonly int[] Order6 = { Moon, Holly, Candle, Star, Tree, Bell };    // Board 6's
    const float SlotPitch = 0.45f;      // PuzzlePegBoard slot spacing

    static readonly Color Brick = new Color(0.6f, 0.32f, 0.25f);
    static readonly Color Soot = new Color(0.1f, 0.08f, 0.08f);
    static readonly Color Warm = new Color(1f, 0.85f, 0.65f);
    static readonly Color ReadyGreen = new Color(0.3f, 1f, 0.4f);
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
            case Candle:
                RectOn(mb, fr, -0.1f * s, 0.1f * s, -0.38f * s, 0.16f * s, d, d + t, IsleKit.Cream);
                RectOn(mb, fr, -0.17f * s, 0.17f * s, -0.46f * s, -0.36f * s, d, d + 1.3f * t, IsleKit.Red);
                Poly(mb, fr, new[] { V(0f, 0.47f), V(0.075f, 0.33f), V(0.06f, 0.25f), V(0f, 0.2f), V(-0.06f, 0.25f), V(-0.075f, 0.33f) },
                    V(0f, 0.3f), d, d + t, IsleKit.Gold);
                break;
            case Letter:
                RectOn(mb, fr, -0.36f * s, 0.36f * s, -0.24f * s, 0.24f * s, d, d + t, IsleKit.Cream);
                BarOn(mb, fr, V(-0.33f, 0.21f), V(0f, -0.03f), 0.035f * s, d, d + 1.5f * t, Crimson);
                BarOn(mb, fr, V(0f, -0.03f), V(0.33f, 0.21f), 0.035f * s, d, d + 1.5f * t, Crimson);
                DiscOn(mb, fr, V(0f, -0.03f), 0.07f * s, d, d + 2f * t, IsleKit.Red);
                break;
            case Sock:
                RectOn(mb, fr, -0.12f * s, 0.12f * s, -0.2f * s, 0.3f * s, d, d + t, IsleKit.Red);
                RectOn(mb, fr, -0.12f * s, 0.3f * s, -0.4f * s, -0.2f * s, d, d + t, IsleKit.Red);
                RectOn(mb, fr, -0.16f * s, 0.16f * s, 0.24f * s, 0.4f * s, d, d + 1.5f * t, IsleKit.Cream);
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

    // A Radio Dead Zone box centred at (x, z, height above the pad top)
    void DeadZone(string name, float x, float z, float height, Vector3 size)
    {
        Piece dead = P("Radio Dead Zone", x, z, height).Named(name);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = size;
        zone.center = Vector3.zero;
    }

    // ================================================================ build

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(52f, 24f);
        b.DebugTeleport(2f, -6f, 2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Component sdk = b.Puzzle;
        Sdk.Set(sdk, "gourdColor", Crimson);      // a deep crimson gourd: a hard puzzle
        // No jetpacks over the yard: nobody flies onto the roofs, round the seals or out of the chute
        IsleKit.CalmAir(b, "Stocking Sort", 0f, 0f, new Vector3(70f, 120f, 40f), 0f, -10f, false);

        // ---- the pad: one level slab, sunk 1.5 m so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -26f, 26f, -1.75f, 0f, -12f, 12f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        Texture2D sheet = PresentSheet(SheetPath);

        BuildCottage(snowy, bare);
        BuildLetterOffice(snowy, bare);

        Piece lobby2 = IsleKit.Lobby(b, "Lobby 2", true, false, false);
        Piece lobby23 = IsleKit.Lobby(b, "Lobby 2-3", true, true, false);
        Piece lobby34 = IsleKit.Lobby(b, "Lobby 3-4", false, true, true);
        Piece lobby4 = IsleKit.Lobby(b, "Lobby 4", false, false, true);

        BuildLedgerBooth(snowy, bare, lobby4);
        BuildStockings(bare, lobby23, lobby4);

        // ================= the Parlour =================
        Piece board5 = P("Peg Board", -0.9f, 2.4f, 1.3f, 180f).Named("Board 5").Set("PuzzlePegBoard", "size", 4).Under(lobby23);      // Slots5
        Piece board6 = P("Peg Board", -1.125f, 2.4f, 1.3f, 180f).Named("Board 6").Set("PuzzlePegBoard", "size", 5).Under(lobby4);     // Slots6
        Piece validator = P("Validator", 3.4f, 3.25f, 1.55f, 180f).Named("Validator");
        Piece parlourButton = P("Button N Hold", -3.3f, 3.25f, 1.15f, 180f).Named("Parlour Button").Set("PuzzleButtonNHold", "group", "stockings");
        // Light 8 sits about 0.1 m behind its pivot, so its pivot stands 0.1 m out from the wall
        Piece lampParlour = P("Light 8", -3.3f, 3.15f, 1.5f, 180f).Named("Ready Lamp Parlour").Set("PuzzleLight", "color", ReadyGreen);
        // A Display's body runs 0.168 m behind its pivot: pivots 0.17 m out from the wall's face
        Piece parlourTree = P("Display", -4.08f, 0f, 1.9f, 90f).Named("Parlour Tree").Set("PuzzleDisplay", "size", 0);
        Piece parlourHolly = P("Display", -4.08f, 0.6f, 1.9f, 90f).Named("Parlour Holly").Set("PuzzleDisplay", "size", 0).Under(lobby23);
        Piece parlourMic = P("Intercom Mic", -4.23f, -1.0f, 1.35f, 90f).Named("Parlour Mic").Under(lobby34);
        Piece parlourMic2 = P("Intercom Mic", -4.23f, -1.0f, 1.35f, 90f).Named("Parlour Mic 2").Under(lobby2);
        Piece parlourSpeaker = P("Intercom Speaker", -4.15f, -1.9f, 1.9f, 90f).Named("Parlour Speaker");
        P("Switch Start", 1.3f, -3.18f, 1.25f, 0f).Named("Parlour Knob");
        P("Live Light", 1.3f, -3.25f, 1.42f, 0f).Named("Parlour Live Light");
        Piece mantelClock = P("Timer", 0f, 2.4f, 2.55f, 180f).Named("Mantel Clock").Set("PuzzleTimer", "duration", RoundSeconds);
        Piece parlourLamp = P("Light 2", 0f, -0.4f, 3.0f).Named("Parlour Lamp").Set("PuzzleLight", "color", Warm);
        P("Confetti", 0f, 0.8f, 0.2f).Named("Parlour Confetti");
        DeadZone("Parlour Dead Zone", 0f, 0f, 1.5f, new Vector3(8.5f, 3.0f, 6.5f));
        P("Door", 0f, -3.375f, 0f, 180f).Named("Parlour Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true).Under(lobby34);
        // Hidden inside the sunk pad: holds a wrong check's cross up for 3 s before the stop
        Piece resultDelay = P("Timer", 0f, 0f, -0.8f).Named("Result Delay").Set("PuzzleTimer", "duration", ResultSeconds);
        // The fire's glow: always on, so the Parlour is never pitch dark (Winter Walk's RoomLights)
        b.Empty("Room Light 2 FF8A3C 3.0 0.9", 0f, 2.1f, Floor + 0.35f, false);

        // ================= the Wrapping Loft =================
        var presents = new List<Piece>();
        for (int k = 0; k < 8; k++)
        {
            Piece tile = P("Peg Tile", -3.7f, -1.75f + 0.5f * k, Deck + 0.85f + 0.24f, 90f).Named("Present " + (k + 1));
            Component tileC = tile.Component("PuzzlePegTile");
            SetEnumByName(tileC, "imageSet", "Custom");
            Sdk.Set(tileC, "image", k + 1);
            Sdk.Set(tileC, "customSheet", sheet);
            Sdk.Set(tileC, "invisibleInkView", false);
            presents.Add(tile);
        }
        // The puzzle's one Listening Chair with no microphone: its headphones sit 3000 m under it, so only the sitter
        // hears what the Letters, Ledger and Parlour Mics play into it (spec, Review note 1: never add a second)
        Piece chair = P("Listening Chair", -0.6f, -1.6f, Deck, 0f).Named("Wrapper's Chair");
        Piece chimneySpeaker = P("Intercom Speaker", 1.8f, 2.2f, Deck + 1.75f, 180f).Named("Chimney Speaker");
        Piece loftMic = P("Intercom Mic", 2.62f, 2.55f, Deck + 1.35f, 90f).Named("Loft Mic").Under(lobby23);
        Piece loftMic4 = P("Intercom Mic", 2.62f, 2.55f, Deck + 1.35f, 90f).Named("Loft Mic 4").Under(lobby4);
        P("Button N Hold", 2.6f, 2.95f, Deck + 1.15f, 90f).Named("Loft Button").Set("PuzzleButtonNHold", "group", "stockings").Under(lobby34);
        Piece lampLoft = P("Light 8", 2.7f, 2.95f, Deck + 1.5f, 90f).Named("Ready Lamp Loft").Set("PuzzleLight", "color", ReadyGreen);
        P("Switch Start", 0.93f, 2.9f, Deck + 1.25f, -90f).Named("Loft Knob");
        P("Live Light", 1.0f, 2.9f, Deck + 1.42f, -90f).Named("Loft Live Light");
        Piece loftClock = P("Timer", 1.0f, 2.6f, Deck + 1.9f, -90f).Named("Loft Clock").Set("PuzzleTimer", "duration", RoundSeconds);
        Piece loftLamp = P("Light 1", 3.8f, -2.8f, Deck).Named("Loft Lamp").Set("PuzzleLight", "color", Warm);
        P("Confetti", 0.8f, 0.5f, Deck + 0.1f).Named("Loft Confetti");

        // ================= the Letter Office =================
        Piece lettersStar = P("Display", -23.38f, 6.85f, 1.9f, 90f).Named("Letters Star").Set("PuzzleDisplay", "size", 0);
        Piece lettersBell = P("Display", -23.38f, 7.45f, 1.9f, 90f).Named("Letters Bell").Set("PuzzleDisplay", "size", 0);
        Piece lettersMoon = P("Display", -23.38f, 8.05f, 1.9f, 90f).Named("Letters Moon").Set("PuzzleDisplay", "size", 0);
        Piece lettersMic = P("Intercom Mic", -23.53f, 8.65f, 1.35f, 90f).Named("Letters Mic");
        Piece lettersSpeaker = P("Intercom Speaker", -22.6f, 9.45f, 1.9f, 180f).Named("Letters Office Speaker");
        P("Button N Hold", -23.55f, 9.1f, 1.15f, 90f).Named("Letters Button").Set("PuzzleButtonNHold", "group", "stockings");
        Piece lampLetters = P("Light 8", -23.45f, 9.1f, 1.5f, 90f).Named("Ready Lamp Letters").Set("PuzzleLight", "color", ReadyGreen);
        P("Switch Start", -23.0f, 9.48f, 1.25f, 180f).Named("Letters Knob");
        P("Live Light", -23.0f, 9.55f, 1.42f, 180f).Named("Letters Live Light");
        P("Door", -20.325f, 7.3f, 0f, 90f).Named("Letters Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        DeadZone("Letters Dead Zone", -22f, 8f, 1.4f, new Vector3(3.1f, 2.8f, 3.1f));
        P("Confetti", -22.6f, 8.0f, 0.2f).Named("Letters Confetti");
        b.Empty("Room Light 1 FFE2B0 6.0 1.2", -22.6f, 8.0f, Floor + 2.5f, false);

        // ================= the Ledger Booth (4 players; open: no door, no dead zone, no button) =================
        Piece ledgerHolly = P("Display", 23.38f, 7.75f, 1.9f, -90f).Named("Ledger Holly").Set("PuzzleDisplay", "size", 0).Under(lobby4);
        Piece ledgerCandle = P("Display", 23.38f, 7.15f, 1.9f, -90f).Named("Ledger Candle").Set("PuzzleDisplay", "size", 0).Under(lobby4);
        Piece ledgerMic = P("Intercom Mic", 23.53f, 8.65f, 1.35f, -90f).Named("Ledger Mic").Under(lobby4);
        Piece ledgerSpeaker = P("Intercom Speaker", 22.6f, 9.45f, 1.9f, 180f).Named("Ledger Booth Speaker").Under(lobby4);
        P("Switch Start", 23.0f, 9.48f, 1.25f, 180f).Named("Ledger Knob").Under(lobby4);
        P("Live Light", 23.0f, 9.55f, 1.42f, 180f).Named("Ledger Live Light").Under(lobby4);
        P("Confetti", 22.6f, 8.0f, 0.2f).Named("Ledger Confetti").Under(lobby4);
        b.Empty("Room Light 3 FFE2B0 6.0 1.2", 22.6f, 8.0f, Floor + 2.5f, false).Under(lobby4);

        // ================= the yard: start corner, tree, lights =================
        IsleKit.StartCorner(b, -6f, -8f, 0f, true, Floor, false);
        BuildYard(snowy, bare);

        // ================= wiring =================
        Component chairC = chair.Component("PuzzleListeningChair");
        Component chimneyC = chimneySpeaker.Component("PuzzleIntercomSpeaker"), parlourSpeakerC = parlourSpeaker.Component("PuzzleIntercomSpeaker");
        Component lettersSpeakerC = lettersSpeaker.Component("PuzzleIntercomSpeaker"), ledgerSpeakerC = ledgerSpeaker.Component("PuzzleIntercomSpeaker");
        // The Readers are heard only in the chair's headphones; the Hanger in the headphones and on the Chimney Speaker
        lettersMic.Add("PuzzleIntercom", "speakers", chairC);
        ledgerMic.Add("PuzzleIntercom", "speakers", chairC);
        parlourMic.Add("PuzzleIntercom", "speakers", chairC, chimneyC);
        parlourMic2.Add("PuzzleIntercom", "speakers", chairC, chimneyC, lettersSpeakerC);        // 2p: the Reader hears the Runner in the Parlour too
        loftMic.Add("PuzzleIntercom", "speakers", parlourSpeakerC, lettersSpeakerC);
        loftMic4.Add("PuzzleIntercom", "speakers", parlourSpeakerC, lettersSpeakerC, ledgerSpeakerC);

        Component validatorC = validator.Component("PuzzleValidator");
        Component lampParlourC = lampParlour.Component("PuzzleLight"), lampLoftC = lampLoft.Component("PuzzleLight"), lampLettersC = lampLetters.Component("PuzzleLight");
        Component starC = lettersStar.Component("PuzzleDisplay"), bellC = lettersBell.Component("PuzzleDisplay"), moonC = lettersMoon.Component("PuzzleDisplay");
        Component treeC = parlourTree.Component("PuzzleDisplay"), hollyC = parlourHolly.Component("PuzzleDisplay");
        Component ledgerHollyC = ledgerHolly.Component("PuzzleDisplay"), candleC = ledgerCandle.Component("PuzzleDisplay");

        // Each slot follows the one-flap Display under its emblem's plaque
        Component board5C = board5.Component("PuzzlePegBoard"), board6C = board6.Component("PuzzlePegBoard");
        Sdk.Add(board5C, "answerFrom", moonC, hollyC, starC, treeC, bellC);
        Sdk.AddInts(board5C, "answerFromFlap", 1, 1, 1, 1, 1);
        Sdk.Add(board6C, "answerFrom", moonC, ledgerHollyC, candleC, starC, treeC, bellC);
        Sdk.AddInts(board6C, "answerFromFlap", 1, 1, 1, 1, 1, 1);
        foreach (Piece board in new[] { board5, board6 })
        {
            board.Set("PuzzlePegBoard", "validator", validatorC);
            // "board full": the board fires exactly one of these on every peg placed or taken off
            foreach (Component lamp in new[] { lampParlourC, lampLoftC, lampLettersC })
            {
                board.On("PuzzlePegBoard", "onCorrect", lamp, "TurnOn");
                board.On("PuzzlePegBoard", "onIncorrect", lamp, "TurnOn");
                board.On("PuzzlePegBoard", "onMissingPegs", lamp, "TurnOff");
            }
        }

        // The three-way hold checks (wired on the Parlour's button only: every member fires its events, and this one
        // exists in every lobby); a wrong answer shows its cross for 3 s, then stops the puzzle; so does the clock
        Component delayC = resultDelay.Component("PuzzleTimer");
        parlourButton.On("PuzzleButtonNHold", "onAllHeld", validatorC, "Check");
        validator.On("PuzzleValidator", "onIncorrect", delayC, "StartTimer");
        resultDelay.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
        mantelClock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");

        Piece root = b.RootPiece;
        Component mantelC = mantelClock.Component("PuzzleTimer"), loftClockC = loftClock.Component("PuzzleTimer");
        foreach (Component clock in new[] { mantelC, loftClockC })
        {
            root.On("PuzzleSDK", "onPuzzleStart", clock, "StartTimer");
            root.On("PuzzleSDK", "onPuzzleStop", clock, "ResetTimer");
            root.On("PuzzleSDK", "onWin", clock, "PauseTimer");
        }
        root.On("PuzzleSDK", "onPuzzleStop", delayC, "ResetTimer");
        foreach (Component lamp in new[] { parlourLamp.Component("PuzzleLight"), loftLamp.Component("PuzzleLight") })
        {
            root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
            root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        }
    }

    // ================================================================ the cottage: Parlour, chute, Loft, stair

    void BuildCottage(Material snowy, Material bare)
    {
        const float y0 = -0.1f, H = 3.0f, D = Deck;
        Color wall = IsleKit.Wood, slab = IsleKit.DarkWood;

        // ---- the soundproof shell: walls, floor, roof slab, chimney breast and stack (no walking surfaces, no parts) ----
        var shell = new MeshBuilder();
        Block(shell, -4.25f, 4.25f, -0.12f, 0.01f, -3.25f, 3.25f, slab);                     // floor (closes the path under the walls)
        Block(shell, -4.5f, -0.62f, y0, H, -3.5f, -3.25f, wall);                              // south, with the doorway x -0.62..0.62
        Block(shell, 0.62f, 4.5f, y0, H, -3.5f, -3.25f, wall);
        Block(shell, -0.62f, 0.62f, 2.04f, H, -3.5f, -3.25f, wall);
        Block(shell, -4.5f, 4.5f, y0, H, 3.25f, 3.5f, wall);                                  // north
        Block(shell, -4.5f, -4.25f, y0, H, -3.25f, 3.25f, wall);                              // west
        Block(shell, 4.25f, 4.5f, y0, H, -3.25f, 3.25f, wall);                                // east
        // the roof slab (top 3.16), with the shaft's hole x 1.41..2.19, z 2.46..3.25
        Block(shell, -4.5f, 1.41f, H, 3.16f, -3.5f, 3.5f, slab);
        Block(shell, 2.19f, 4.5f, H, 3.16f, -3.5f, 3.5f, slab);
        Block(shell, 1.41f, 2.19f, H, 3.16f, -3.5f, 2.46f, slab);
        Block(shell, 1.41f, 2.19f, H, 3.16f, 3.25f, 3.5f, slab);
        // the chimney breast x -2.3..2.3, z 2.4..3.25, round the shaft; open in front below 1.8 m (the log nook)
        Block(shell, -2.3f, 1.41f, y0, H, 2.4f, 3.25f, Brick);
        Block(shell, 2.19f, 2.3f, y0, H, 2.4f, 3.25f, Brick);
        Block(shell, 1.41f, 2.19f, 1.8f, H, 2.4f, 2.46f, Brick);
        // the stack on the deck, x 1.0..2.6, z 2.3..3.5, up to Deck + 2.4; the posting slot x 1.55..2.1 in its south face
        Block(shell, 1.0f, 2.6f, 3.16f, D + 0.8f, 2.3f, 2.46f, Brick);
        Block(shell, 1.0f, 1.55f, D + 0.8f, D + 1.5f, 2.3f, 2.46f, Brick);
        Block(shell, 2.1f, 2.6f, D + 0.8f, D + 1.5f, 2.3f, 2.46f, Brick);
        Block(shell, 1.0f, 2.6f, D + 1.5f, D + 2.4f, 2.3f, 2.46f, Brick);
        Block(shell, 1.0f, 2.6f, 3.16f, D + 2.4f, 3.25f, 3.5f, Brick);
        Block(shell, 1.0f, 1.41f, 3.16f, D + 2.4f, 2.46f, 3.25f, Brick);
        Block(shell, 2.19f, 2.6f, 3.16f, D + 2.4f, 2.46f, 3.25f, Brick);
        Block(shell, 0.95f, 2.65f, D + 2.4f, D + 2.55f, 2.25f, 3.55f, IsleKit.Stone);          // the cap
        Piece parlourShell = b.Empty("Parlour Shell", 0f, 0f, Floor, false);
        Sdk.Set(b.AddScript(parlourShell, "PuzzleSoundProof"), "material", 0);                // Wood
        OnPad("Parlour Walls", shell, snowy).Under(parlourShell);

        // ---- inside the Parlour: floorboards, mantel, firebox, hearths (ordinary colliders, no snow indoors) ----
        var inside = new MeshBuilder();
        Block(inside, -4.25f, 4.25f, 0.01f, 0.04f, -3.25f, 3.25f, IsleKit.Wood);              // floorboards
        Block(inside, -2.5f, 2.5f, 2.0f, 2.12f, 2.0f, 2.4f, IsleKit.DarkWood);                // the mantel shelf
        Block(inside, -0.7f, 0.7f, 0.04f, 0.36f, 2.38f, 2.4f, Soot);                          // the firebox
        Block(inside, -1.4f, 1.25f, 0.04f, 0.07f, 2.0f, 2.4f, IsleKit.Stone);                 // the hearth stone
        Block(inside, 1.3f, 2.4f, 0.04f, 0.10f, 1.7f, 2.4f, IsleKit.Stone);                   // the presents' hearth (0.06 high)
        Block(inside, 1.3f, 2.4f, 0.10f, 0.16f, 1.7f, 1.76f, IsleKit.Stone);                  // ... with a lip on its outer edge only
        OnPad("Parlour Fittings", inside, bare);

        // ---- the chute: a straight 0.7 x 0.7 shaft (x 1.45..2.15, z 2.5..3.2) from the slot to the floor, lined with
        //      smooth slabs that never narrow; its floor slopes 36 degrees down to the log nook, flush with the hearth ----
        var liner = new MeshBuilder();
        Color lin = IsleKit.Stone * 0.8f;
        Block(liner, 1.41f, 1.45f, 0.04f, D + 2.4f, 2.46f, 3.24f, lin);                       // west
        Block(liner, 2.15f, 2.19f, 0.04f, D + 2.4f, 2.46f, 3.24f, lin);                       // east
        Block(liner, 1.45f, 2.15f, 0.04f, D + 2.4f, 3.20f, 3.24f, lin);                       // back
        Block(liner, 1.41f, 2.19f, 1.8f, D + 0.8f, 2.46f, 2.50f, lin);                        // front, above the nook
        Block(liner, 1.41f, 2.19f, D + 1.5f, D + 2.4f, 2.46f, 2.50f, lin);                    // front, above the slot
        Block(liner, 1.41f, 1.55f, D + 0.8f, D + 1.5f, 2.46f, 2.50f, lin);                    // the slot's sides
        Block(liner, 2.1f, 2.19f, D + 0.8f, D + 1.5f, 2.46f, 2.50f, lin);
        Block(liner, 1.41f, 2.19f, 0.04f, 0.10f, 2.40f, 2.50f, lin);                          // the nook's sill, flush with the hearth
        PrismX(liner, 1.41f, 2.19f, new[] { new Vector2(2.50f, 0.04f), new Vector2(2.50f, 0.10f), new Vector2(3.24f, 0.64f), new Vector2(3.24f, 0.04f) }, lin);
        // the posting slot's sill: 35 degrees down and in across the 0.24 m passage, so nothing rests on it
        PrismX(liner, 1.55f, 2.1f, new[] { new Vector2(2.26f, D + 0.80f), new Vector2(2.26f, D + 1.0f), new Vector2(2.50f, D + 0.832f), new Vector2(2.50f, D + 0.80f) }, lin);
        OnPad("Chute Liner", liner, bare);

        // ---- the Loft: deck (on the slab), parapet, the bench, the chimney pot, the chair's sign ----
        var deck = new MeshBuilder();
        Block(deck, -4.3f, 1.0f, 3.16f, D, -3.3f, 3.3f, IsleKit.Wood);
        Block(deck, 2.6f, 4.3f, 3.16f, D, -3.3f, 3.3f, IsleKit.Wood);
        Block(deck, 1.0f, 2.6f, 3.16f, D, -3.3f, 2.3f, IsleKit.Wood);
        Block(deck, 4.3f, 4.52f, 3.16f, D, 0.7f, 1.9f, IsleKit.Wood);                         // across the parapet's gap to the stair
        OnPad("Loft Deck", deck, snowy);

        var parapet = new MeshBuilder();
        const float top = D + 1.1f;
        Block(parapet, -4.5f, 4.5f, 3.16f, top, -3.5f, -3.3f, wall);                          // south
        Block(parapet, -4.5f, -4.3f, 3.16f, top, -3.3f, 3.3f, wall);                          // west
        Block(parapet, -4.5f, 1.0f, 3.16f, top, 3.3f, 3.5f, wall);                            // north, either side of the stack
        Block(parapet, 2.6f, 4.5f, 3.16f, top, 3.3f, 3.5f, wall);
        Block(parapet, 4.3f, 4.5f, 3.16f, top, -3.3f, 0.7f, wall);                            // east, with the stair's gap z 0.7..1.9
        Block(parapet, 4.3f, 4.5f, 3.16f, top, 1.9f, 3.3f, wall);
        Kit.AddDisc(parapet, new Vector3(1.8f, D + 2.55f, 2.9f), 0.2f, 0.45f, Brick * 0.8f);   // the chimney pot
        Block(parapet, 0.11f, 0.19f, D, D + 1.2f, -2.44f, -2.36f, IsleKit.DarkWood);          // the chair's sign post
        OnPad("Loft Parapet", parapet, snowy);

        // the bench: top at Deck + 0.85, a 0.08 lip on the parapet side, 0.05 on the open side and at the ends
        var bench = new MeshBuilder();
        const float seat = D + 0.85f;
        Block(bench, -4.1f, -3.3f, seat - 0.08f, seat, -2.1f, 2.1f, IsleKit.Wood);
        Block(bench, -4.1f, -4.06f, seat, seat + 0.08f, -2.1f, 2.1f, IsleKit.DarkWood);
        Block(bench, -3.34f, -3.3f, seat, seat + 0.05f, -2.1f, 2.1f, IsleKit.DarkWood);
        Block(bench, -4.06f, -3.34f, seat, seat + 0.05f, -2.1f, -2.06f, IsleKit.DarkWood);
        Block(bench, -4.06f, -3.34f, seat, seat + 0.05f, 2.06f, 2.1f, IsleKit.DarkWood);
        foreach (float lx in new[] { -4.0f, -3.4f })
            foreach (float lz in new[] { -2.0f, 0f, 2.0f })
                Block(bench, lx - 0.04f, lx + 0.04f, D, seat - 0.08f, lz - 0.04f, lz + 0.04f, IsleKit.DarkWood);
        OnPad("Present Bench", bench, snowy);

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

        // ---- plaques: the Parlour's tree, the "board full" stockings by every lamp, the chair's letters sign ----
        var plaques = new MeshBuilder();
        Plaque(plaques, new Vector3(-4.25f, 2.35f, 0f), 90f, Tree, 0.28f);
        Plaque(plaques, new Vector3(-3.3f, 1.78f, 3.25f), 180f, Sock, 0.16f);
        Plaque(plaques, new Vector3(2.6f, D + 1.78f, 2.95f), 90f, Sock, 0.16f);
        Plaque(plaques, new Vector3(0.19f, D + 1.05f, -2.4f), 90f, Letter, 0.22f);
        Plaque(plaques, new Vector3(0.11f, D + 1.05f, -2.4f), -90f, Letter, 0.22f);
        OnPad("Cottage Plaques", plaques, bare, false);
    }

    // ================================================================ the stockings and their plaques (per lobby)

    void BuildStockings(Material bare, Piece lobby23, Piece lobby4)
    {
        void Set(string tag, int[] order, float firstX, Piece group)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < order.Length; i++)
            {
                float x = firstX + SlotPitch * i;
                Color sock = i % 2 == 0 ? IsleKit.Red : IsleKit.Green;
                Block(mb, x - 0.15f, x + 0.15f, 0.4f, 0.95f, 2.33f, 2.39f, sock);                 // leg
                Block(mb, x - 0.15f, x + 0.2f, 0.4f, 0.56f, 2.33f, 2.385f, sock);                 // foot
                Block(mb, x - 0.17f, x + 0.17f, 0.95f, 1.06f, 2.32f, 2.395f, IsleKit.Cream);      // cuff, below a hung tile (1.076)
                Emblem(mb, Wall(new Vector3(x, 1.005f, 2.32f), 180f), order[i], 0.09f, 0f, IsleKit.Cream);
                Plaque(mb, new Vector3(x, 1.78f, 2.4f), 180f, order[i], 0.28f);
            }
            OnPad("Stockings " + tag, mb, bare, false).Under(group);
        }
        Set("5", Order5, -0.9f, lobby23);
        Set("6", Order6, -1.125f, lobby4);

        // the Parlour's holly Display is only in 2- and 3-player lobbies
        var holly = new MeshBuilder();
        Plaque(holly, new Vector3(-4.25f, 2.35f, 0.6f), 90f, Holly, 0.28f);
        OnPad("Parlour Holly Plaque", holly, bare, false).Under(lobby23);
    }

    // ================================================================ the Letter Office (sealed) and the Ledger Booth (open)

    // One booth's walls, x outer 'xa'..'xb' (xa the doorway's side), z 6.2..9.8: a doorway z 6.68..7.92 in the wall
    // nearer the cottage, a screen wall from the south wall to z 8.4 that hides the Displays from the doorway
    static void BoothWalls(MeshBuilder mb, bool west, Color wall)
    {
        const float y0 = -0.1f, H = OfficeHigh;
        float s = west ? -1f : 1f;
        float outer = 23.8f * s, inner = 23.55f * s, door = 20.2f * s, doorIn = 20.45f * s, scr0 = 21.6f * s, scr1 = 21.75f * s;
        void B(float xa, float xb, float ya, float yb, float za, float zb, Color c) => Block(mb, Mathf.Min(xa, xb), Mathf.Max(xa, xb), ya, yb, za, zb, c);
        B(outer, door, y0, H, 6.2f, 6.45f, wall);                        // south
        B(outer, door, y0, H, 9.55f, 9.8f, wall);                        // north
        B(outer, inner, y0, H, 6.45f, 9.55f, wall);                      // back wall (the Displays' wall)
        B(door, doorIn, y0, H, 6.45f, 6.68f, wall);                      // the doorway's wall, doorway z 6.68..7.92
        B(door, doorIn, y0, H, 7.92f, 9.55f, wall);
        B(door, doorIn, 2.04f, H, 6.68f, 7.92f, wall);
        B(scr0, scr1, y0, H, 6.45f, 8.4f, wall);                         // the screen: a 1.15 m corridor turns round its north end
        B(outer + 0.15f * s, door - 0.15f * s, H, H + 0.15f, 6.05f, 9.95f, IsleKit.DarkWood);      // the flat roof, 0.15 m over every wall
    }

    // A booth's pitched decor roof, its floorboards and its sign
    static void BoothDressing(MeshBuilder roof, MeshBuilder floor, MeshBuilder sign, bool west, int emblem)
    {
        const float H = OfficeHigh;
        float s = west ? -1f : 1f;
        PrismX(roof, west ? -24.0f : 20.0f, west ? -20.0f : 24.0f, new[] { new Vector2(5.95f, H + 0.15f), new Vector2(8.0f, H + 1.0f), new Vector2(10.05f, H + 0.15f) }, IsleKit.DarkWood);
        Block(floor, west ? -23.55f : 20.45f, west ? -20.45f : 23.55f, 0.01f, 0.04f, 6.45f, 9.55f, IsleKit.Wood);
        // the sign beside the doorway, on the wall facing the cottage
        Frame fr = Wall(new Vector3(20.2f * s, 1.95f, 8.75f), west ? 90f : -90f);
        RectOn(sign, fr, -0.42f, 0.42f, -0.3f, 0.3f, 0f, 0.02f, IsleKit.Gold);
        RectOn(sign, fr, -0.38f, 0.38f, -0.26f, 0.26f, 0.02f, 0.03f, IsleKit.Board);
        Emblem(sign, fr, emblem, 0.62f, 0.03f, IsleKit.Board);
    }

    void BuildLetterOffice(Material snowy, Material bare)
    {
        var shell = new MeshBuilder();
        Block(shell, -23.55f, -20.45f, -0.12f, 0.01f, 6.45f, 9.55f, IsleKit.DarkWood);       // floor (closes the path under the walls)
        BoothWalls(shell, true, IsleKit.Red);
        Piece lettersShell = b.Empty("Letters Shell", -22f, 8f, Floor, false);
        Sdk.Set(b.AddScript(lettersShell, "PuzzleSoundProof"), "material", 0);                // Wood
        OnPad("Letters Walls", shell, snowy).Under(lettersShell);

        var roof = new MeshBuilder();
        var floor = new MeshBuilder();
        var sign = new MeshBuilder();
        BoothDressing(roof, floor, sign, true, Letter);
        OnPad("Letters Roof", roof, snowy);
        OnPad("Letters Floor", floor, bare);
        var plaques = new MeshBuilder();
        Plaque(plaques, new Vector3(-23.55f, 2.4f, 6.85f), 90f, Star, 0.28f);
        Plaque(plaques, new Vector3(-23.55f, 2.4f, 7.45f), 90f, Bell, 0.28f);
        Plaque(plaques, new Vector3(-23.55f, 2.4f, 8.05f), 90f, Moon, 0.28f);
        Plaque(plaques, new Vector3(-23.55f, 1.78f, 9.1f), 90f, Sock, 0.16f);                 // by the Letters button's lamp
        OnPad("Letters Plaques", plaques, bare, false);
        OnPad("Letters Sign", sign, bare, false);
    }

    void BuildLedgerBooth(Material snowy, Material bare, Piece lobby4)
    {
        var walls = new MeshBuilder();
        BoothWalls(walls, false, IsleKit.Green);
        var roof = new MeshBuilder();
        var floor = new MeshBuilder();
        var sign = new MeshBuilder();
        BoothDressing(roof, floor, sign, false, Candle);
        OnPad("Ledger Walls", walls, snowy).Under(lobby4);
        OnPad("Ledger Roof", roof, snowy).Under(lobby4);
        OnPad("Ledger Floor", floor, bare).Under(lobby4);
        var plaques = new MeshBuilder();
        Plaque(plaques, new Vector3(23.55f, 2.4f, 7.75f), -90f, Holly, 0.28f);
        Plaque(plaques, new Vector3(23.55f, 2.4f, 7.15f), -90f, Candle, 0.28f);
        OnPad("Ledger Plaques", plaques, bare, false).Under(lobby4);
        OnPad("Ledger Sign", sign, bare, false).Under(lobby4);
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

        // Christmas lights: the cottage's eaves, the parapet, the stair, a string on posts to the Letter Office, a
        // ring round the yard (no posts where the wire lies on a building)
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
        Kit.LightString(b, "Letters Path Lights", new List<Vector3>
            { new Vector3(-5.0f, F + 2.3f, 2.5f), new Vector3(-10f, F + 2.3f, 5.0f), new Vector3(-15f, F + 2.3f, 6.0f), new Vector3(-19.6f, F + 2.3f, 6.0f) },
            0.4f, 0.5f, false, true, 1315);
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
