// Snowball Range (spec: puzzle-specs/SnowballRange.md, revision 2, 2026-11-22): a walled, roofed shooting range with six
// striped hoops, split three ways by sight.
//
// The Thrower stands in a roofed throwing box and lobs snowballs through a curtained chute, the only way into the range;
// they never see a target. The Spotter watches the range from a glassed perch up a covered ramp and calls corrections,
// reading the pictures on the hoops' signs through a telescope. The Caller stands in a windowless hut 25 m away whose
// board lights one lamp at a time; beside each lamp a copy display shows the picture on one hoop's sign. The board's
// six cells (lamp and copy together) change places at every Begin (RangeBoard), so only the picture says which hoop.
// Six hits with eighteen snowballs inside six minutes win; a ball through the wrong hoop, the clock running out or
// anyone entering the range ends the run (the door opens, the team gathers the balls and readies again).
//
// Everyone holds a Ready button at their own station (N-Hold group "Ready": box, perch, and the hut's in a lobby of
// three or more) to begin; RangeBoard.BeginIfOn begins only while the puzzle is switched on and nobody is in the range
// or at its door. The range is sealed: a 4.5 m palisade, a solid ceiling, a door shut from switch-on and during a run
// (Let Me Out inside opens it at any time), a floor sensor over the whole inside and an apron sensor outside the door.
//
// Coordinates: local metres from the root, x east, z north (downrange, +z forward); heights above the pad top (Floor
// above the root), every piece onGround: false on one level pad. Walls are 0.3 m thick, centred on the spec's lines.
// Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowballRangePuzzle : PuzzleDef
{
    public override string Name => "SnowballRange";
    public override Vector2 Footprint => new Vector2(42f, 36f);
    public override Vector2? FixedSite => new Vector2(-2418f, -1620f);      // layout-final: a broad, quiet High Shelf meadow, off the paths

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Deck = 2.4f;            // the perch's deck above the pad
    const float Palisade = 4.5f;        // range wall height; the ceiling slab sits on it (4.5..5.0)
    const float Y0 = -0.1f;             // walls start just under the pad top, so no crack shows at their foot
    const float RoofPitch = 50f;        // every gable: too steep to stand on
    const int Balls = 18;
    const float RunSeconds = 360f;

    // The hoops: centre x, centre height, centre z, and the ring's (and zone's) yaw, which faces the chute at (-6, -5)
    static readonly Vector4[] Hoops =
    {
        new Vector4(-11.54f, 1.6f, 2.09f, 142f), new Vector4(-11.62f, 2.4f, 8.91f, 158f), new Vector4(-6.85f, 1.4f, 1.95f, 173f),
        new Vector4(-4.19f, 2.8f, 7.87f, 188f), new Vector4(-1.70f, 2.0f, 5.13f, 203f), new Vector4(3.85f, 1.8f, 7.61f, 218f),
    };
    // Each hoop's sign: the display's x, z and its yaw (it faces the perch)
    static readonly Vector3[] Signs =
    {
        new Vector3(-12.53f, 1.32f, 180f), new Vector3(-12.78f, 8.44f, 179f), new Vector3(-8.09f, 1.80f, -152f),
        new Vector3(-5.43f, 8.05f, -154f), new Vector3(-2.85f, 5.61f, -141f), new Vector3(2.87f, 8.38f, -134f),
    };
    const float RingR = 0.7f;
    // The board's six slots on the hut's north wall (a gap at the divider, x 12.9..13.1)
    static readonly float[] SlotX = { 10.0f, 11.3f, 12.6f, 13.4f, 14.7f, 16.0f };
    const float BoardFace = -12.18f;    // the board's face: 3 cm proud of the north wall's inside face (-12.15)

    // Part depths from part-metrics.tsv (how far the pivot stands in front of the part's back), plus 1 cm clear
    const float DepthDisplay = 0.168f + 0.01f, DepthLight6 = 0.142f + 0.01f, DepthBar = 0.101f + 0.01f, DepthSpeaker = 0.1f + 0.01f, DepthMic = 0.02f + 0.01f;

    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);
    static readonly Color Sack = new Color(0.13f, 0.11f, 0.09f);
    static readonly Color White = new Color(0.94f, 0.93f, 0.9f);

    PuzzleBuilder b;

    // ---- geometry helpers ----

    // A block between two corners (local x, y above the pad top, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A closed box of any orientation: its centre, size along its own axes, and its rotation. The corners and faces are
    // in Kit.AddBox's order, so with a turn about y alone it is the same box AddBox makes.
    static void OBox(MeshBuilder mb, Vector3 centre, Vector3 size, Quaternion rot, Color color)
    {
        float hx = size.x / 2f, hy = size.y / 2f, hz = size.z / 2f;
        float[] sx = { -1f, 1f, 1f, -1f }, sz = { -1f, -1f, 1f, 1f };
        var low = new Vector3[4];
        var high = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            low[i] = centre + rot * new Vector3(sx[i] * hx, -hy, sz[i] * hz);
            high[i] = centre + rot * new Vector3(sx[i] * hx, hy, sz[i] * hz);
        }
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }

    // Both sides of a face, so it shows and blocks from either side whichever way it winds
    static void TwoSided(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Color color) { mb.Tri(a, b2, c, color); mb.Tri(a, c, b2, color); }

    // A solid gable roof: a triangular prism on the rectangle x0..x1, z0..z1 at height y, its ridge along x (alongX) or
    // along z, pitched at RoofPitch. Its ends close the gables; inside, the room has a flat ceiling at y.
    static void Gable(MeshBuilder mb, float x0, float x1, float z0, float z1, float y, bool alongX, Color roof, Color end)
    {
        float half = alongX ? (z1 - z0) / 2f : (x1 - x0) / 2f;
        float top = y + half * Mathf.Tan(RoofPitch * Mathf.Deg2Rad);
        var p00 = new Vector3(x0, y, z0); var p10 = new Vector3(x1, y, z0); var p11 = new Vector3(x1, y, z1); var p01 = new Vector3(x0, y, z1);
        TwoSided(mb, p00, p10, p11, end); TwoSided(mb, p00, p11, p01, end);      // underside
        if (alongX)
        {
            float zm = (z0 + z1) / 2f;
            var r0 = new Vector3(x0, top, zm); var r1 = new Vector3(x1, top, zm);
            TwoSided(mb, p00, p10, r1, roof); TwoSided(mb, p00, r1, r0, roof);      // south slope
            TwoSided(mb, p01, p11, r1, roof); TwoSided(mb, p01, r1, r0, roof);      // north slope
            TwoSided(mb, p00, p01, r0, end); TwoSided(mb, p10, p11, r1, end);       // gable ends
        }
        else
        {
            float xm = (x0 + x1) / 2f;
            var r0 = new Vector3(xm, top, z0); var r1 = new Vector3(xm, top, z1);
            TwoSided(mb, p00, p01, r1, roof); TwoSided(mb, p00, r1, r0, roof);      // west slope
            TwoSided(mb, p10, p11, r1, roof); TwoSided(mb, p10, r1, r0, roof);      // east slope
            TwoSided(mb, p00, p10, r0, end); TwoSided(mb, p01, p11, r1, end);       // gable ends
        }
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

    // An empty on the pad (lamp markers, parents)
    Piece E(string name, float x, float z, float height) => b.Empty(name, x, z, Floor + height, false);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(42f, 36f);
        b.DebugTeleport(0f, -15.5f, 2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks over the range: nobody flies onto the ceiling, the roofs or over the hut (Winter Walk's Calm Air)
        IsleKit.CalmAir(b, "Snowball Range", -1f, -0.5f, new Vector3(56f, 120f, 50f), 0f, Floor - 10f, false);

        // ---- the pad: one level slab, sunk 1.5 m so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -21f, 19f, -1.75f, 0f, -17.5f, 16.5f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildRange(snowy, bare);
        BuildBox(snowy);
        BuildPerch(snowy);
        BuildHut(snowy);

        // ---- snowballs: eighteen in one layer of 3 x 6 on the bin's floor, 0.28 m apart (a ball is 0.26 across) ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Object>();
        for (int i = 0; i < Balls; i++)
        {
            float x = -7.61f + (i % 3) * 0.28f, z = -8.60f + (i / 3) * 0.28f;
            Piece ball = b.Item("Snowball " + (i + 1), Kit.Ball(0.13f, 8, 12, 0.1f, 2200 + i), snow, x, z, Floor + 0.20f, 0.6f, false);
            Component item = ball.Component("PuzzleItem");
            Sdk.Set(item, "savesOnReload", false);
            Sdk.Set(item, "canBelt", true);
            Sdk.Set(item, "canBackpack", true);
            balls.Add(item);
        }

        // ---- the hoops: a striped ring with no collider, two posts that meet its sides, a deep zone inside it ----
        var rings = new MeshBuilder();
        var posts = new MeshBuilder();
        var zones = new List<Object>();
        var signs = new Component[Hoops.Length];
        for (int h = 0; h < Hoops.Length; h++)
        {
            Vector4 c = Hoops[h];
            Quaternion face = Quaternion.Euler(0f, c.w, 0f);
            var centre = new Vector3(c.x, c.y, c.z);
            const int segments = 24;
            float chord = 2f * RingR * Mathf.Sin(Mathf.PI / segments) * 1.12f;
            for (int k = 0; k < segments; k++)
            {
                float a = 2f * Mathf.PI * k / segments;
                Vector3 at = centre + face * new Vector3(Mathf.Cos(a) * RingR, Mathf.Sin(a) * RingR, 0f);
                OBox(rings, at, new Vector3(chord, 0.08f, 0.08f), face * Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f), (k / 2) % 2 == 0 ? IsleKit.Red : White);
            }
            foreach (float side in new[] { -0.75f, 0.75f })
            {
                Vector2 p = IsleKit.Turn(side, 0f, c.w);
                Kit.AddBox(posts, new Vector3(c.x + p.x, Y0, c.z + p.y), new Vector3(0.1f, c.y - Y0, 0.1f), IsleKit.Wood, c.w);
            }
            Piece zone = P("Presence Sensor", c.x, c.z, c.y, c.w).Named("Hoop " + (h + 1));
            BoxCollider col = zone.Go.GetComponent<BoxCollider>();
            if (col == null) col = zone.Go.AddComponent<BoxCollider>();
            col.size = new Vector3(1.0f, 1.0f, 0.8f);
            col.center = Vector3.zero;
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            zone.Add("PuzzlePresenceSensor", "acceptedCustom", balls.ToArray());
            zones.Add(zone.Component("PuzzlePresenceSensor"));

            // the hoop's sign beside it: a post, a small board with a roof, and the display facing the perch
            Vector3 s = Signs[h];
            Vector2 back = IsleKit.Turn(0f, -1f, s.z);
            Kit.AddBox(posts, new Vector3(s.x + back.x * 0.30f, Y0, s.y + back.y * 0.30f), new Vector3(0.12f, 2.6f - Y0, 0.12f), IsleKit.Wood, s.z);
            Kit.AddBox(posts, new Vector3(s.x + back.x * (DepthDisplay + 0.03f), 2.0f, s.y + back.y * (DepthDisplay + 0.03f)), new Vector3(0.6f, 0.6f, 0.06f), IsleKit.Board, s.z);
            Kit.AddBox(posts, new Vector3(s.x + back.x * 0.2f, 2.6f, s.y + back.y * 0.2f), new Vector3(0.8f, 0.06f, 0.42f), IsleKit.DarkWood, s.z);
            Piece sign = P("Display", s.x, s.y, 2.3f, s.z).Named("Hoop Sign " + (h + 1))
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", true);
            signs[h] = sign.Component("PuzzleDisplay");
        }
        OnPad("Hoop Rings", rings, bare, false);
        OnPad("Hoop Posts", posts, snowy);

        // ---- the picture pile: eight Shapes tiles sealed in a chest by the start corner (six are dealt each start) ----
        var chest = new MeshBuilder();
        Block(chest, 0f, 4f, 0f, 0.05f, -11.275f, -10.725f, IsleKit.DarkWood);          // floor
        Block(chest, 0f, 4f, 0.6f, 0.65f, -11.275f, -10.725f, IsleKit.DarkWood);        // lid
        Block(chest, 0f, 4f, 0f, 0.6f, -11.275f, -11.225f, IsleKit.Wood);               // south
        Block(chest, 0f, 4f, 0f, 0.6f, -10.775f, -10.725f, IsleKit.Wood);               // north
        Block(chest, 0f, 0.05f, 0f, 0.6f, -11.225f, -10.775f, IsleKit.Wood);            // west
        Block(chest, 3.95f, 4f, 0f, 0.6f, -11.225f, -10.775f, IsleKit.Wood);            // east
        OnPad("Tile Chest", chest, snowy);
        for (int i = 0; i < 8; i++)
            P("Peg Tile", 0.3f + 0.48f * i, -10.97f, 0.05f + 0.224f + 0.03f, 0f).Named("Range Tile " + (i + 1))
                .Set("PuzzlePegTile", "imageSet", 0).Set("PuzzlePegTile", "image", i + 1);      // index 0 = Shapes

        // ---- the throwing box's parts: Ready, the hit bar and the clock's bar over the chute, its lamp ----
        Piece readyBox = P("Button N Hold", -4.15f, -7.5f, 1.15f, -90f).Named("Ready Box").Set("PuzzleButtonNHold", "group", "Ready");
        Piece hitsBox = P("Progress Bar", -6f, -5.15f - DepthBar, 1.78f, 180f).Named("Hits Box").Scaled(0.9f, 1f, 1f).Set("PuzzleProgressBar", "total", 6);
        Piece timeBox = P("Progress Bar", -6f, -5.15f - DepthBar, 2.26f, 180f).Named("Time Box").Scaled(0.9f, 1f, 1f).Set("PuzzleProgressBar", "total", 10);
        E("Room Light 3 FFE2B8 4.0 0.8", -6f, -7f, 2.3f);

        // ---- the perch's parts: window, telescope, Ready, speaker, clock, hit bar, lamp ----
        P("Glass", -12.5f, -5.0f, Deck + 0.925f, 0f).Named("Perch Window").Pane(2.2f, 1.35f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0).Set("PuzzleGlass", "wire", false);
        P("Telescope", -12.5f, -5.75f, Deck + 0.4f, 0f).Named("Perch Telescope").Set("PuzzleTelescope", "invisibleInkView", false);
        P("Button N Hold", -11.15f, -6.2f, Deck + 1.15f, -90f).Named("Ready Perch").Set("PuzzleButtonNHold", "group", "Ready");
        Piece perchSpeaker = P("Intercom Speaker", -11.15f - DepthSpeaker, -7.3f, Deck + 1.6f, -90f).Named("Perch Speaker");
        Piece clock = P("Timer", -13.84f, -6.0f, Deck + 1.7f, 90f).Named("Range Clock").Set("PuzzleTimer", "duration", RunSeconds);
        Piece hitsPerch = P("Progress Bar", -13.85f + DepthBar, -6.75f, Deck + 1.05f, 90f).Named("Hits Perch").Scaled(0.5f, 1f, 1f).Set("PuzzleProgressBar", "total", 6);
        E("Room Light 4 FFE2B8 4.0 0.8", -12.5f, -7f, Deck + 2.0f);

        // ---- the hut's parts: the board's six cells (lamp + copy), mics, hit bar, Ready (3+), lamps ----
        var lamps = new Object[6];
        for (int k = 0; k < 6; k++)
        {
            Piece cell = E("Board Cell " + (k + 1), SlotX[k], BoardFace, 0f);
            Piece lamp = P("Light 6", SlotX[k], BoardFace - DepthLight6, 1.95f, 180f).Named("Board Lamp " + (k + 1)).Set("PuzzleLight", "color", Warm);
            Piece copy = P("Display", SlotX[k], BoardFace - DepthDisplay, 1.4f, 180f).Named("Board Copy " + (k + 1))
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "copyDisplay", signs[k]);
            lamp.Transform.SetParent(cell.Transform, true);
            copy.Transform.SetParent(cell.Transform, true);
            lamps[k] = lamp.Component("PuzzleLight");
        }
        Piece micWest = P("Intercom Mic", 10.6f, -15.85f + DepthMic, 1.4f, 0f).Named("Hut Mic West");
        Piece micEast = P("Intercom Mic", 15.4f, -15.85f + DepthMic, 1.4f, 0f).Named("Hut Mic East");
        micWest.Add("PuzzleIntercom", "speakers", perchSpeaker.Component("PuzzleIntercomSpeaker"));
        micEast.Add("PuzzleIntercom", "speakers", perchSpeaker.Component("PuzzleIntercomSpeaker"));
        Piece hitsHut = P("Progress Bar", 11.6f, -15.85f + DepthBar, 2.1f, 0f).Named("Hits Hut").Scaled(0.55f, 1f, 1f).Set("PuzzleProgressBar", "total", 6);
        Piece threePlus = IsleKit.Lobby(b, "Caller Three Plus", false, true, true);
        P("Button N Hold", 12.0f, -15.85f, 1.15f, 0f).Named("Ready Hut").Set("PuzzleButtonNHold", "group", "Ready").Under(threePlus);
        E("Room Light 1 FFE2B8 5.0 1.0", 11f, -14f, 2.3f);
        E("Room Light 6 FFE2B8 5.0 1.0", 15f, -14f, 2.3f);
        Piece board = E("Range Board", 13f, -14f, 0.5f);
        Component rangeBoard = b.AddCustomScript(board, "RangeBoard");

        // ---- the range's door, Let Me Out, the floor and apron sensors ----
        Piece door = P("Door", 7f, -1f, 0f, 90f).Named("Range Door").Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", false);
        var plaque = new MeshBuilder();
        Block(plaque, 6.79f, 6.85f, 0.95f, 1.45f, 0.05f, 0.55f, IsleKit.Red);
        OnPad("Let Me Out Board", plaque, snowy);
        Piece letMeOut = P("Button Poke", 6.79f, 0.3f, 1.2f, -90f).Named("Let Me Out").Set("PuzzlePokeButton", "autoEnableSeconds", 1f);
        Piece rangeFloor = IsleKit.Sensor(b, "Range Floor", -6f, 5f, new Vector3(25.7f, 4.2f, 19.7f), Floor, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        Piece apron = IsleKit.Sensor(b, "Door Apron", 8.2f, -1f, new Vector3(2.1f, 3.0f, 2.4f), Floor, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);

        // ---- Press In Order: lamp k pairs with hoop k; the three hit bars fill together ----
        Piece order = E("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", zones.ToArray());
        Sdk.Add(pio, "progressBars", hitsBox.Component("PuzzleProgressBar"), hitsPerch.Component("PuzzleProgressBar"), hitsHut.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 6);
        Sdk.Set(pio, "holdAtEnd", false);
        Sdk.Add(clock.Component("PuzzleTimer"), "progressBars", timeBox.Component("PuzzleProgressBar"));

        // ---- the start corner behind the box, facing south (the approach) ----
        IsleKit.StartCorner(b, 0f, -12.5f, 180f, true, Floor, false);

        // ---- light strings: round the palisade's top outside, along the hut's and the box's eaves ----
        float wire = Floor + 4.35f;
        Kit.LightString(b, "Range Lights", new List<Vector3>
        {
            new Vector3(-19.45f, wire, -5.3f), new Vector3(-19.45f, wire, 0f), new Vector3(-19.45f, wire, 5f), new Vector3(-19.45f, wire, 10f),
            new Vector3(-19.45f, wire, 15.45f), new Vector3(-13f, wire, 15.45f), new Vector3(-6f, wire, 15.45f), new Vector3(1f, wire, 15.45f),
            new Vector3(7.45f, wire, 15.45f), new Vector3(7.45f, wire, 10f), new Vector3(7.45f, wire, 5f), new Vector3(7.45f, wire, 0f),
            new Vector3(7.45f, wire, -5.3f),
        }, 0.12f, 0.5f, false, false, 223);
        Kit.LightString(b, "Hut Lights", new List<Vector3>
        {
            new Vector3(8.7f, Floor + 2.45f, -16.47f), new Vector3(11.5f, Floor + 2.45f, -16.47f), new Vector3(14.5f, Floor + 2.45f, -16.47f), new Vector3(17.3f, Floor + 2.45f, -16.47f),
        }, 0.15f, 0.5f, false, false, 224);
        Kit.LightString(b, "Box Lights", new List<Vector3>
        {
            new Vector3(-8.3f, Floor + 2.42f, -9.35f), new Vector3(-6f, Floor + 2.42f, -9.35f), new Vector3(-3.7f, Floor + 2.42f, -9.35f),
        }, 0.12f, 0.5f, false, false, 225);

        // ---- wiring ----
        Component sdk = b.Puzzle;
        Component doorC = door.Component("PuzzleDoor"), clockC = clock.Component("PuzzleTimer");
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", doorC, "Close");
        root.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        root.On("PuzzleSDK", "onPuzzleStop", clockC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", doorC, "Open");
        readyBox.On("PuzzleButtonNHold", "onAllHeld", rangeBoard, "BeginIfOn");      // every member fires: wire one
        Sdk.Wire(pio, "onBegin", doorC, "Close");
        Sdk.Wire(pio, "onBegin", clockC, "StartTimer");
        Sdk.Wire(pio, "onEnd", doorC, "Open");
        Sdk.Wire(pio, "onEnd", clockC, "ResetTimer");
        clock.On("PuzzleTimer", "onFinished", pio, "End");
        hitsBox.On("PuzzleProgressBar", "onFull", sdk, "Win");
        rangeFloor.On("PuzzlePresenceSensor", "onPlayerEntered", pio, "End");
        apron.On("PuzzlePresenceSensor", "onPlayerEntered", pio, "End");
        letMeOut.On("PuzzlePokeButton", "onPressed", pio, "End");
        letMeOut.On("PuzzlePokeButton", "onPressed", doorC, "Open");
    }

    // The range: palisade (with the chute's bore, the perch's window bay and the door), the ceiling, rafters, the lamp
    // poles inside, and the chute with its curtain
    void BuildRange(Material snowy, Material bare)
    {
        Color wood = IsleKit.Wood, dark = IsleKit.DarkWood;
        var mb = new MeshBuilder();
        // west, back
        Block(mb, -19.15f, -18.85f, Y0, Palisade, -5.15f, 15.15f, wood);
        Block(mb, -18.85f, 6.85f, Y0, Palisade, 14.85f, 15.15f, wood);
        // east, with the door's opening z -1.62..-0.38 (the door is 1.24 m wide, 2.02 high) and a lintel over it
        Block(mb, 6.85f, 7.15f, Y0, Palisade, -0.38f, 15.15f, wood);
        Block(mb, 6.85f, 7.15f, Y0, Palisade, -5.15f, -1.62f, wood);
        Block(mb, 6.85f, 7.15f, 2.04f, Palisade, -1.62f, -0.38f, wood);
        // front (z -5.15..-4.85): west of the perch; the perch's front, to 4.8 with the window bay x -13.6..-11.4,
        // 2.65..4.0 (deck + 0.25..1.6); east of the perch, with the chute's bore x -6.5..-5.5, 0.5..1.5
        Block(mb, -18.85f, -14.15f, Y0, Palisade, -5.15f, -4.85f, wood);
        Block(mb, -14.15f, -10.85f, Y0, Deck + 0.25f, -5.15f, -4.85f, wood);
        Block(mb, -14.15f, -10.85f, Deck + 1.6f, 4.8f, -5.15f, -4.85f, wood);
        Block(mb, -14.15f, -13.6f, Deck + 0.25f, Deck + 1.6f, -5.15f, -4.85f, wood);
        Block(mb, -11.4f, -10.85f, Deck + 0.25f, Deck + 1.6f, -5.15f, -4.85f, wood);
        Block(mb, -10.85f, -6.5f, Y0, Palisade, -5.15f, -4.85f, wood);
        Block(mb, -5.5f, 6.85f, Y0, Palisade, -5.15f, -4.85f, wood);
        Block(mb, -6.5f, -5.5f, Y0, 0.5f, -5.15f, -4.85f, wood);
        Block(mb, -6.5f, -5.5f, 1.5f, Palisade, -5.15f, -4.85f, wood);
        // posts on the outside faces, for the boarded look (snow on their tops)
        for (float z = -2.5f; z <= 12.6f; z += 2.5f)
        {
            Block(mb, -19.37f, -19.15f, Y0, Palisade + 0.12f, z - 0.11f, z + 0.11f, dark);
            if (z < -2.4f || z > 0.4f) Block(mb, 7.15f, 7.37f, Y0, Palisade + 0.12f, z - 0.11f, z + 0.11f, dark);
        }
        for (float x = -16.5f; x <= 6f; x += 2.5f) Block(mb, x - 0.11f, x + 0.11f, Y0, Palisade + 0.12f, 15.15f, 15.37f, dark);
        OnPad("Range Palisade", mb, snowy);

        // the ceiling: one solid slab on the palisade tops, a convex collider so its 0.5 m is solid; pale netting look
        var ceiling = new MeshBuilder();
        Block(ceiling, -19.15f, 7.15f, Palisade, Palisade + 0.5f, -5.15f, 15.15f, Color.white);
        Material netting = b.ShaderMaterial("BWM/Picture", "Range Netting", m =>
        {
            m.SetFloat("_AlphaMode", 1f); m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Indoor", 0f); m.renderQueue = 3000; m.SetFloat("_Cull", 0f);
            m.SetColor("_Tint", new Color(0.82f, 0.85f, 0.88f, 0.15f)); m.SetFloat("_Gloss", 0.3f); m.SetFloat("_Shine", 0.3f);
        });
        Piece slab = OnPad("Range Ceiling", ceiling, netting);
        slab.Go.GetComponent<MeshCollider>().convex = true;

        // rafters every 2 m across, three ties along: visual only, their tops 2 cm under the slab
        var rafters = new MeshBuilder();
        for (float z = -3f; z <= 13.1f; z += 2f) Block(rafters, -18.85f, 6.85f, Palisade - 0.2f, Palisade - 0.02f, z - 0.08f, z + 0.08f, dark);
        foreach (float x in new[] { -13f, -6f, 1f }) Block(rafters, x - 0.07f, x + 0.07f, Palisade - 0.16f, Palisade - 0.04f, -4.85f, 14.85f, dark);
        OnPad("Range Rafters", rafters, snowy, false);

        // two lamp poles inside the front corners, with Winter Walk's room lights at their tops
        var poles = new MeshBuilder();
        Block(poles, -17.06f, -16.94f, Y0, 4.0f, -4.06f, -3.94f, dark);
        Block(poles, 4.94f, 5.06f, Y0, 4.0f, -4.06f, -3.94f, dark);
        OnPad("Range Poles", poles, snowy);
        E("Room Light 2 FFF1DC 22.0 1.4", -17f, -4f, 4.05f);
        E("Room Light 5 FFF1DC 22.0 1.4", 5f, -4f, 4.05f);

        // the chute: a flared tunnel of closed slabs from the bore (x -6.5..-5.5, z -5.15) to the outer mouth
        // (x -7.3..-4.7, z -3.8), 0.5..1.5 high
        var chute = new MeshBuilder();
        // floor and roof from the palisade's inner face (inside the wall the bore's own faces are the tunnel's; starting
        // them further back would lay coplanar faces over the bore's and z-fight)
        Block(chute, -7.35f, -4.65f, 0.47f, 0.5f, -4.85f, -3.8f, dark);      // floor
        Block(chute, -7.35f, -4.65f, 1.5f, 1.53f, -4.85f, -3.8f, dark);      // roof
        foreach (float sign in new[] { -1f, 1f })
        {
            var a = new Vector2(-6f + 0.5f * sign, -5.15f);
            var c = new Vector2(-6f + 1.3f * sign, -3.8f);
            Vector2 d = c - a;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 outward = rot * Vector3.right * sign;      // away from the tunnel's inside
            var mid = new Vector3((a.x + c.x) / 2f, 1.0f, (a.y + c.y) / 2f) + outward * 0.025f;
            OBox(chute, mid, new Vector3(0.05f, 1.06f, d.magnitude), rot, dark);
        }
        OnPad("Chute", chute, snowy);

        // the curtain: dark sackcloth just beyond the outer mouth, overhanging it all round; balls pass, sight does not
        var curtain = new MeshBuilder();
        Block(curtain, -7.45f, -4.55f, 0.4f, 1.6f, -3.75f, -3.72f, Sack);
        OnPad("Chute Curtain", curtain, bare, false);
    }

    // The throwing box: walls 2.5 m with a solid roof slab against the palisade, a back doorway, the snowball bin
    void BuildBox(Material snowy)
    {
        Color wood = IsleKit.Wood;
        var mb = new MeshBuilder();
        Block(mb, -8.15f, -7.85f, Y0, 2.5f, -9.15f, -5.15f, wood);           // west
        Block(mb, -4.15f, -3.85f, Y0, 2.5f, -9.15f, -5.15f, wood);           // east
        Block(mb, -8.15f, -6.6f, Y0, 2.5f, -9.15f, -8.85f, wood);            // back, with the doorway x -6.6..-5.4, 2.2 high
        Block(mb, -5.4f, -3.85f, Y0, 2.5f, -9.15f, -8.85f, wood);
        Block(mb, -6.6f, -5.4f, 2.2f, 2.5f, -9.15f, -8.85f, wood);
        Block(mb, -8.3f, -3.7f, 2.5f, 2.7f, -9.3f, -5.15f, IsleKit.DarkWood); // roof slab, butting the palisade's back face
        // the bin: a hollow trough 2 cm off the west wall (outer x -7.83..-6.83, z -8.8..-7.0), floor top at 0.05,
        // sides 0.45 high; inside 0.9 x 1.7
        Color bin = IsleKit.DarkWood;
        Block(mb, -7.83f, -6.83f, 0f, 0.05f, -8.8f, -7.0f, bin);
        Block(mb, -7.83f, -7.78f, 0f, 0.45f, -8.8f, -7.0f, bin);
        Block(mb, -6.88f, -6.83f, 0f, 0.45f, -8.8f, -7.0f, bin);
        Block(mb, -7.78f, -6.88f, 0f, 0.45f, -8.8f, -8.75f, bin);
        Block(mb, -7.78f, -6.88f, 0f, 0.45f, -7.05f, -7.0f, bin);
        OnPad("Throwing Box", mb, snowy);
    }

    // The perch (solid base to the deck, walls to 4.8, gable) and its covered ramp (walls 4.5 above the pad all along,
    // gable, a sloped floor from the pad at z -14.5 to the deck)
    void BuildPerch(Material snowy)
    {
        Color wood = IsleKit.Wood, roof = IsleKit.Red;
        var mb = new MeshBuilder();
        Block(mb, -14.15f, -10.85f, Y0, Deck, -8.15f, -5.15f, IsleKit.Stone);         // base, its top the deck
        Block(mb, -14.15f, -13.85f, Deck, 4.8f, -8.15f, -5.15f, wood);                 // west
        Block(mb, -11.15f, -10.85f, Deck, 4.8f, -8.15f, -5.15f, wood);                 // east
        Block(mb, -13.85f, -13.25f, Deck, 4.8f, -8.15f, -7.85f, wood);                 // back, open to the ramp x -13.25..-12.15
        Block(mb, -12.15f, -11.15f, Deck, 4.8f, -8.15f, -7.85f, wood);
        Block(mb, -13.25f, -12.15f, Palisade, 4.8f, -8.15f, -7.85f, wood);
        Gable(mb, -14.35f, -10.65f, -8.35f, -4.85f, 4.8f, false, roof, wood);

        // the ramp
        Block(mb, -13.55f, -13.25f, Y0, Palisade, -14.5f, -8.15f, wood);
        Block(mb, -12.15f, -11.85f, Y0, Palisade, -14.5f, -8.15f, wood);
        Block(mb, -13.25f, -12.15f, 2.2f, Palisade, -14.5f, -14.2f, wood);            // over the entrance
        Gable(mb, -13.75f, -11.65f, -14.7f, -8.15f, Palisade, false, roof, wood);
        // the floor: from the pad at z -14.5 to the deck's height where the perch's base begins (z -8.15), about 21 degrees
        float run = 14.5f - 8.15f, slope = Mathf.Atan2(Deck, run);
        Quaternion tilt = Quaternion.Euler(-slope * Mathf.Rad2Deg, 0f, 0f);
        Vector3 along = tilt * Vector3.forward, up = tilt * Vector3.up;
        float length = Mathf.Sqrt(run * run + Deck * Deck);
        Vector3 topMid = new Vector3(-12.7f, Deck / 2f, (-14.5f - 8.15f) / 2f) - along * 0.15f;      // run 0.3 m on under the pad at the foot
        OBox(mb, topMid - up * 0.15f, new Vector3(1.1f, 0.3f, length + 0.3f), tilt, IsleKit.DarkWood);
        OnPad("Perch", mb, snowy);
    }

    // The Caller's hut: windowless, gabled, doorways in both end walls, the board's backing on the north wall, and the
    // four-player divider
    void BuildHut(Material snowy)
    {
        Color wood = IsleKit.Wood;
        var mb = new MeshBuilder();
        Block(mb, 8.85f, 17.15f, Y0, 2.6f, -12.15f, -11.85f, wood);         // north
        Block(mb, 8.85f, 17.15f, Y0, 2.6f, -16.15f, -15.85f, wood);         // south
        foreach (float x in new[] { 9f, 17f })                               // the end walls, doorway z -14.6..-13.4, 2.2 high
        {
            Block(mb, x - 0.15f, x + 0.15f, Y0, 2.6f, -15.85f, -14.6f, wood);
            Block(mb, x - 0.15f, x + 0.15f, Y0, 2.6f, -13.4f, -12.15f, wood);
            Block(mb, x - 0.15f, x + 0.15f, 2.2f, 2.6f, -14.6f, -13.4f, wood);
        }
        Gable(mb, 8.6f, 17.4f, -16.4f, -11.6f, 2.6f, true, IsleKit.Green, wood);
        Block(mb, 9.6f, 12.85f, 1.0f, 2.35f, BoardFace, -12.15f, IsleKit.Board);      // the board's backing, either side of the divider
        Block(mb, 13.15f, 16.4f, 1.0f, 2.35f, BoardFace, -12.15f, IsleKit.Board);
        OnPad("Caller Hut", mb, snowy);

        // In a four-player lobby a wall splits the hut: slots 1..3 west, 4..6 east, each half with its own door and mic
        Piece four = IsleKit.Lobby(b, "Caller Four", false, false, true);
        var divider = new MeshBuilder();
        Block(divider, 12.9f, 13.1f, Y0, 2.6f, -15.85f, -12.15f, wood);
        OnPad("Hut Divider", divider, snowy).Under(four);
    }
}
