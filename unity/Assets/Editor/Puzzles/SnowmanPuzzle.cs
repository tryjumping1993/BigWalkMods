// The Snowman Contest (spec: puzzle-specs/Snowman.md, revision 2, 2026-11-22).
//
// A round snow fort holds six empty snowman plots, low snow bowls in a ring, each with a faceless snowman head on a pole
// behind it whose face is a split-flap picture (an Emote). Outside the fort stands the Judges' Box, a closed,
// windowless hut. Inside it six small lamps sit beside copies of the six faces, in a scrambled grid on a free-standing
// partition, hidden from the doorway by an entry baffle. On start the Box's door shuts on the Judge, the faces are dealt
// from eight Emote stamps sealed in a chest outside, and every 21 s one lamp lights. By the end of those 21 s the
// builders in the fort must have all three snowballs set down in the plot whose head wears that face, and in no other
// plot. Eight clean judgings win (ten in a four-player lobby); a miss stops the contest and opens the door.
//
// Under the hood: the SDK's timed Press In Order (holdAtEnd) with six item-counting Presence Sensors as its buttons,
// each needing all three snowballs. One Press In Order per lobby size (Hider groups); only the shown one ever runs.
// No custom code.
//
// Coordinates: local metres from the root, which is the fort's centre: x east, z north (+z forward). Heights are above
// the ground. Each structure is built on the ground under its own anchor (the fort's centre, the Box's centre, each
// plot, each head post), and the SDK parts on a structure are placed from that same level (onGround: false), so a few
// centimetres of unevenness in the levelled site never put a part off its wall. Yaw: 0 faces +z, 90 +x, -90 -x, 180 -z.
using System;
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowmanPuzzle : PuzzleDef
{
    public override string Name => "Snowman";
    public override Vector2 Footprint => new Vector2(26f, 44f);
    // layout-final: a village green on the High Shelf, 64 m north of the summit spur's High Shelf station (the Box's
    // door faces south, towards it)
    public override Vector2? FixedSite => new Vector2(-2553f, -1530f);
    public override float Yaw => 0f;

    const int Rounds3 = 8;
    const int Rounds4 = 10;
    const float PhaseSeconds = 21f;     // the floor: two builders, opposite plot (about 18 s) plus 3 s. Never lower.

    // ---- the fort ----
    const float PlotRadius = 7.0f;
    const float PostRadius = 8.95f;
    const float FaceRadius = 8.62f;
    const float FortIn = 11.0f, FortOut = 11.8f, FortHigh = 3.0f;
    static readonly string[] PlotNames = { "E", "NE", "NW", "W", "SW", "SE" };      // math angles 0, 60, ... 300

    // Card i (top row left to right, then the bottom row, as the Judge faces north) belongs to this plot
    static readonly int[] CardPlot = { 5, 3, 1, 0, 2, 4 };      // SE, W, NE, E, NW, SW
    static readonly float[] CardX = { -0.75f, 0f, 0.75f };
    static readonly float[] CardH = { 1.95f, 1.20f };
    const float LampAbove = 0.36f;
    const float CardZ = -16.87f, LampZ = -16.80f;

    // ---- the Judges' Box ----
    const float BoxX = 2.9f, BoxIn = 2.5f;                  // outer / inner half-width
    const float BoxS = -21.0f, BoxSIn = -20.6f;             // south wall's outer and inner faces
    const float BoxN = -14.9f, BoxNIn = -15.3f;
    const float BoxHigh = 2.7f;                             // underside of the roof
    const float DoorHalf = 0.62f, DoorHigh = 2.04f;         // the Door part's exact size

    static readonly Color SnowWhite = new Color(0.93f, 0.95f, 0.98f);
    static readonly Color Coal = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color Carrot = new Color(0.95f, 0.45f, 0.1f);
    static readonly Color Gold = new Color(1.0f, 0.8f, 0.3f);
    static readonly Color Warm = new Color(1f, 0.85f, 0.6f);

    PuzzleBuilder b;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(26f, 44f);
        b.DebugTeleport(-5f, -16f);
        // No jetpacks over the contest: nobody flies balls about, over the fort's walls or onto the Box
        IsleKit.CalmAir(b, "Snowman", 0f, -4.5f, new Vector3(40f, 120f, 52f), 0f, -10f);

        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Material ballSnow = b.ShaderMaterial("BWM/Snow", "Snow");
        float fortBase = GroundAt(0f, 0f);
        float boxBase = GroundAt(0f, -17.95f);

        // ================= the fort, its plots and heads =================
        BuildFort(snowy, fortBase);

        var plotSensors = new Component[6];
        var heads = new Piece[6];
        var headsLow = new Piece[6];
        var bowls = new MeshBuilder();
        var headMesh = new MeshBuilder();
        var headTrim = new MeshBuilder();
        for (int k = 0; k < 6; k++)
        {
            float theta = k * 60f * Mathf.Deg2Rad;
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            Vector2 plot = new Vector2(PlotRadius * c, PlotRadius * s);
            float headYaw = Mathf.Atan2(-c, -s) * Mathf.Rad2Deg;     // faces the centre

            // the bowl: a floor disc 2.2 m across, 3 cm proud, inside a 12-sided rim (inner 2.0 m, 0.25 m thick, 0.20 m high)
            float pb = GroundAt(plot.x, plot.y);
            Kit.AddDisc(bowls, new Vector3(plot.x, pb - 0.1f, plot.y), 1.1f, 0.13f, new Color(0.84f, 0.88f, 0.95f));
            Ring(bowls, new Vector3(plot.x, 0f, plot.y), pb - 0.1f, pb + 0.20f, 1.0f, 1.25f, 12, 15f, null, SnowWhite);

            // the plot's sensor: a shallow slab level with the rim, counting only the three snowballs ("set it down")
            plotSensors[k] = IsleKit.Sensor(b, "Plot " + PlotNames[k], plot.x, plot.y, new Vector3(2.0f, 0.22f, 2.0f))
                .Set("PuzzlePresenceSensor", "type", 2)                      // SpecificItem
                .Set("PuzzlePresenceSensor", "requiredPlayers", 3)
                .Component("PuzzlePresenceSensor");

            // the head on its post: a snow head with its face (the Display) in front, a hat above, a carrot below
            Vector2 post = new Vector2(PostRadius * c, PostRadius * s);
            float hb = GroundAt(post.x, post.y);
            Kit.AddBox(headMesh, new Vector3(post.x, hb - 0.5f, post.y), new Vector3(0.2f, 2.4f, 0.2f), IsleKit.Wood, headYaw);
            Vector3 headCentre = new Vector3(9.28f * c, hb + 1.70f, 9.28f * s);
            Kit.AddBall(headMesh, headCentre, 0.475f, SnowWhite);
            // a dark backing behind the low copy, so it does not float in front of the post
            Vector2 back = new Vector2(8.82f * c, 8.82f * s);
            Kit.AddBox(headMesh, new Vector3(back.x, hb + 0.87f, back.y), new Vector3(0.46f, 0.46f, 0.06f), IsleKit.Board, headYaw);
            Vector3 hatFoot = new Vector3(9.15f * c, hb + 2.13f, 9.15f * s);
            Kit.AddDisc(headTrim, hatFoot, 0.40f, 0.04f, Coal, 12);
            Kit.AddDisc(headTrim, hatFoot + Vector3.up * 0.04f, 0.24f, 0.34f, Coal, 12);
            Kit.AddDisc(headTrim, hatFoot + Vector3.up * 0.06f, 0.25f, 0.07f, IsleKit.Red, 12);
            // the carrot runs from inside the head (at this height its front is at about 8.9 m) out past the face's foot
            Vector2 nose = new Vector2(8.62f * c, 8.62f * s);
            Kit.AddBox(headTrim, new Vector3(nose.x, hb + 1.40f, nose.y), new Vector3(0.06f, 0.06f, 0.5f), Carrot, headYaw);

            Vector2 face = new Vector2(FaceRadius * c, FaceRadius * s);
            heads[k] = b.Part("Display", face.x, face.y, hb + 1.70f, headYaw, false).Named("Head " + PlotNames[k]).Set("PuzzleDisplay", "size", 0);
            headsLow[k] = b.Part("Display", face.x, face.y, hb + 1.10f, headYaw, false).Named("Head Low " + PlotNames[k]).Set("PuzzleDisplay", "size", 0);
        }
        Place("Plot Bowls", bowls, snowy, 0f);
        Place("Snowman Heads", headMesh, snowy, 0f);
        Place("Snowman Hats", headTrim, bare, 0f);

        // ---- the snowballs, in the fort's centre ----
        float[] radius = { 0.46f, 0.34f, 0.24f };
        string[] ballNames = { "Snowball Large", "Snowball Medium", "Snowball Small" };
        Vector2[] rest = { new Vector2(-0.6f, 0.35f), new Vector2(0.6f, 0.35f), new Vector2(0f, -0.6f) };
        var balls = new UnityEngine.Object[3];
        for (int i = 0; i < 3; i++)
        {
            Piece ball = b.Item(ballNames[i], Kit.Ball(radius[i], 10, 14, 0.12f, i + 1), ballSnow, rest[i].x, rest[i].y, radius[i] * 0.8f, 3f - i);
            Component item = ball.Go.GetComponent(Sdk.T("PuzzleItem"));
            Sdk.Set(item, "savesOnReload", false);      // a held ball is never carried off to the spawn area by a save
            Sdk.Set(item, "sounds", 3);                 // Football: rolls with a sound
            balls[i] = item;
        }
        foreach (Component sensor in plotSensors) Sdk.Add(sensor, "acceptedCustom", balls);

        // ---- the fort board on the north wall: two bars, the knob, its live lamp, the north speaker ----
        FP("Switch Start", 1.60f, 10.83f, fortBase + 1.25f, 180f).Named("Fort Knob");
        FP("Live Light", 1.60f, 10.85f, fortBase + 1.42f, 180f).Named("Fort Live");
        Piece roundsFort = FP("Progress Bar", 0f, 10.80f, fortBase + 2.30f, 180f).Named("Rounds Fort").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", Rounds3);
        Piece phaseFort = FP("Progress Bar", 0f, 10.80f, fortBase + 1.75f, 180f).Named("Phase Fort").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", 1);
        Piece speakerN = FP("Intercom Speaker", 0f, 10.80f, fortBase + 2.62f, 180f).Named("Fort Speaker N");
        Piece speakerS = FP("Intercom Speaker", 0f, -10.90f, fortBase + 2.55f, 0f).Named("Fort Speaker S");       // on the gateway arch

        // ================= the Judges' Box =================
        BuildBox(snowy, boxBase);
        Piece door = FP("Door", 0f, (BoxS + BoxSIn) / 2f, boxBase, 0f).Named("Judges' Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);      // CloseWhenPuzzleActive
        FP("Switch Start", 1.00f, -19.53f, boxBase + 1.25f, 0f).Named("Box Knob");       // on the baffle's inner face
        FP("Live Light", 1.00f, -19.59f, boxBase + 1.42f, 0f).Named("Box Live");
        Piece roundsBox = FP("Progress Bar", -2.40f, -18.15f, boxBase + 2.15f, 90f).Named("Rounds Box").Scaled(0.5f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", Rounds3);
        Piece phaseBox = FP("Progress Bar", -2.40f, -18.15f, boxBase + 1.60f, 90f).Named("Phase Box").Scaled(0.5f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", 1);
        Piece mic = FP("Intercom Mic", 1.15f, -16.72f, boxBase + 1.35f, 180f).Named("Judges' Mic");
        mic.Add("PuzzleIntercom", "speakers", speakerN.Component("PuzzleIntercomSpeaker"), speakerS.Component("PuzzleIntercomSpeaker"));
        // Winter Walk's room light, so the cards read at night (constant: it tells nothing)
        b.Empty("Room Light 1 FFD9A0 6.0 1.2", 0f, -18.2f, boxBase + 2.3f, false);

        // the cards and their lamps on the partition's south face; each head (both heights) copies its plot's card
        var lamps = new UnityEngine.Object[6];
        var buttons = new UnityEngine.Object[6];
        for (int i = 0; i < 6; i++)
        {
            float x = CardX[i % 3], h = CardH[i / 3];
            Piece card = FP("Display", x, CardZ, boxBase + h, 180f).Named("Card " + (i + 1)).Set("PuzzleDisplay", "size", 0);
            Piece lamp = FP("Light 8", x, LampZ, boxBase + h + LampAbove, 180f).Named("Lamp " + (i + 1))
                .Set("PuzzleLight", "color", Gold).Set("PuzzleLight", "range", 0.3f);
            lamps[i] = lamp.Component("PuzzleLight");
            int plot = CardPlot[i];
            buttons[i] = plotSensors[plot];
            Component cardC = card.Component("PuzzleDisplay");
            heads[plot].Set("PuzzleDisplay", "copyDisplay", cardC);
            headsLow[plot].Set("PuzzleDisplay", "copyDisplay", cardC);
        }

        // ---- the face stamps: eight Emote tiles sealed in the Stamp Chest, only there to be the deal pile ----
        for (int i = 0; i < 8; i++)
            FP("Peg Tile", -3.275f, -20.05f + 0.35f * i, boxBase + 0.335f, 0f).Named("Face Stamp " + (i + 1))
                .Set("PuzzlePegTile", "imageSet", 2)       // enum index 2 = Emotes (Shapes, Peg, Emotes, Scribbles, Custom)
                .Set("PuzzlePegTile", "image", i + 1);

        // ================= start corner and the Champion Snowman =================
        IsleKit.StartCorner(b, 6.5f, -18.0f, 180f);
        Piece trimmings = BuildChampion(snowy, bare, 10.5f, -17.0f);

        // ================= the judging: one Press In Order per lobby size =================
        Component roundsBoxC = roundsBox.Component("PuzzleProgressBar"), roundsFortC = roundsFort.Component("PuzzleProgressBar");
        Component phaseBoxC = phaseBox.Component("PuzzleProgressBar"), phaseFortC = phaseFort.Component("PuzzleProgressBar");
        Piece three = IsleKit.Lobby(b, "Three Players", true, true, false);
        Piece four = IsleKit.Lobby(b, "Four Players", false, false, true);
        Component judging3 = Judging("Judging 3", three, lamps, buttons, roundsBoxC, roundsFortC, phaseBoxC, phaseFortC, Rounds3);
        Component judging4 = Judging("Judging 4", four, lamps, buttons, roundsBoxC, roundsFortC, phaseBoxC, phaseFortC, Rounds4);

        // ================= events =================
        Component sdk = b.Puzzle;
        Piece root = b.RootPiece;
        foreach (Component judging in new[] { judging3, judging4 })
        {
            root.On("PuzzleSDK", "onPuzzleStart", judging, "Begin");
            root.On("PuzzleSDK", "onPuzzleStop", judging, "End");
            Sdk.Wire(judging, "onFail", sdk, "StopPuzzle");      // a miss visibly ends the attempt: knobs off, door open, faces blank
        }
        roundsFort.On("PuzzleProgressBar", "onFull", sdk, "Win");     // eight (ten) clean rounds; only a running contest fills it
        root.On("PuzzleSDK", "onWin", door.Component("PuzzleDoor"), "Open");
        root.On("PuzzleSDK", "onWin", trimmings.Go, "SetActive", true);

        // ================= Christmas lights: round the fort's top and along the Box's eaves =================
        var fortPosts = new List<Vector3>();
        for (int k = 0; k <= 15; k++)
        {
            float a = (-78.75f + 22.5f * k) * Mathf.Deg2Rad;      // from the gateway's east end anticlockwise to its west end
            float r = 11.4f / Mathf.Cos(11.25f * Mathf.Deg2Rad);
            fortPosts.Add(new Vector3(r * Mathf.Cos(a), fortBase + FortHigh + 0.35f, r * Mathf.Sin(a)));
        }
        Kit.LightString(b, "Fort Lights", fortPosts, 0.15f, 0.5f, false, false, 181);
        float eave = boxBase + BoxHigh - 0.05f;
        Kit.LightString(b, "Box Lights", new List<Vector3>
        {
            new Vector3(-3.1f, eave, -21.2f), new Vector3(3.1f, eave, -21.2f), new Vector3(3.1f, eave, -14.7f),
            new Vector3(-3.1f, eave, -14.7f), new Vector3(-3.1f, eave, -21.2f),
        }, 0.2f, 0.5f, false, false, 182);
    }

    // ---- the judging ----

    Component Judging(string name, Piece group, UnityEngine.Object[] lamps, UnityEngine.Object[] buttons,
        Component roundsBox, Component roundsFort, Component phaseBox, Component phaseFort, int rounds)
    {
        Piece holder = b.Empty(name, 0f, 0f, 0.5f).Under(group);
        Component pio = b.AddScript(holder, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", buttons);
        Sdk.Add(pio, "progressBars", roundsBox, roundsFort);
        Sdk.Add(pio, "phaseBars", phaseBox, phaseFort);
        Sdk.Set(pio, "rounds", rounds);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", PhaseSeconds);
        return pio;
    }

    // ---- structures ----

    // The fort: a 16-sided ring wall (0.8 m thick, 3 m high, sheer both sides) with its south segment left out as an
    // open gateway, a snow cap, the arch over the gateway, and the dark board on the north wall's inner face
    void BuildFort(Material snowy, float fortBase)
    {
        var mb = new MeshBuilder();
        Func<int, bool> gateway = k => k == 8;          // segment centres from 90 deg: k = 8 is 270, the south
        Ring(mb, Vector3.zero, fortBase - 0.8f, fortBase + FortHigh, FortIn, FortOut, 16, 90f, gateway, IsleKit.Cream);
        Ring(mb, Vector3.zero, fortBase + FortHigh, fortBase + FortHigh + 0.12f, FortIn - 0.06f, FortOut + 0.06f, 16, 90f, gateway, SnowWhite);
        // the arch over the gateway: from 2.35 m to the wall's top, through the wall's depth
        Block(mb, -2.5f, 2.5f, fortBase + 2.35f, fortBase + FortHigh, -FortOut, -FortIn, IsleKit.Cream);
        Block(mb, -2.56f, 2.56f, fortBase + FortHigh, fortBase + FortHigh + 0.13f, -FortOut - 0.06f, -FortIn + 0.06f, SnowWhite);      // 1 cm over the ring's cap: no coplanar tops
        // the fort board: 2.6 x 1.9 m, from 1.0 to 2.9 m, on the north segment's inner face (z 11.0)
        Block(mb, -1.3f, 1.3f, fortBase + 1.0f, fortBase + 2.9f, 10.9f, FortIn, IsleKit.Board);
        Place("Fort Wall", mb, snowy, 0f);
    }

    // The Judges' Box: 0.4 m walls, no windows, a door opening exactly the Door part's size with a lintel to the roof,
    // a roof slab with eaves, the card partition and the entry baffle (both floor to roof), a floor, and the sealed
    // Stamp Chest against the west wall outside
    void BuildBox(Material snowy, float bb)
    {
        var mb = new MeshBuilder();
        Color wall = IsleKit.Wood, trim = IsleKit.DarkWood;
        float y0 = bb - 0.6f, top = bb + BoxHigh;
        // south wall with the doorway
        Block(mb, -BoxX, -DoorHalf, y0, top, BoxS, BoxSIn, wall);
        Block(mb, DoorHalf, BoxX, y0, top, BoxS, BoxSIn, wall);
        Block(mb, -DoorHalf, DoorHalf, bb + DoorHigh, top, BoxS, BoxSIn, wall);
        Block(mb, -DoorHalf, DoorHalf, y0, bb - 0.02f, BoxS, BoxSIn, wall);            // the sill under the door, below the snow
        // north, west, east
        Block(mb, -BoxX, BoxX, y0, top, BoxNIn, BoxN, wall);
        Block(mb, -BoxX, -BoxIn, y0, top, BoxSIn, BoxNIn, wall);
        Block(mb, BoxIn, BoxX, y0, top, BoxSIn, BoxNIn, wall);
        // corner posts, a little proud
        foreach (float cx in new[] { -BoxX, BoxX })
            foreach (float cz in new[] { BoxS, BoxN })
                Block(mb, cx - 0.12f, cx + 0.12f, y0, top, cz - 0.12f, cz + 0.12f, trim);
        // roof with eaves, sitting on the walls (no gap: a slit under the roof would show the board)
        Block(mb, -BoxX - 0.25f, BoxX + 0.25f, top, top + 0.25f, BoxS - 0.25f, BoxN + 0.25f, trim);
        // floor (2 cm proud of the snow), partition (cards side south), baffle
        Block(mb, -BoxIn, BoxIn, bb - 0.3f, bb + 0.02f, BoxSIn, BoxNIn, IsleKit.Wood);
        Block(mb, -1.3f, 1.3f, bb + 0.02f, top, -16.7f, -16.5f, IsleKit.Board);
        Block(mb, -1.5f, 1.5f, bb + 0.02f, top, -19.8f, -19.6f, wall);
        // the Stamp Chest: outer x -3.65..-2.92, z -20.4..-17.3, h 0..0.8; walls, floor and lid 10 cm; sealed
        Color stone = IsleKit.Stone;
        Block(mb, -3.65f, -2.92f, bb - 0.3f, bb + 0.1f, -20.4f, -17.3f, stone);        // floor
        Block(mb, -3.65f, -2.92f, bb + 0.7f, bb + 0.8f, -20.4f, -17.3f, stone);        // lid
        Block(mb, -3.65f, -3.55f, bb + 0.1f, bb + 0.7f, -20.4f, -17.3f, stone);        // west side
        Block(mb, -3.0f, -2.92f, bb + 0.1f, bb + 0.7f, -20.4f, -17.3f, stone);         // east side, against the Box
        Block(mb, -3.55f, -3.0f, bb + 0.1f, bb + 0.7f, -20.4f, -20.3f, stone);         // south end
        Block(mb, -3.55f, -3.0f, bb + 0.1f, bb + 0.7f, -17.4f, -17.3f, stone);         // north end
        Place("Judges Box", mb, snowy, 0f);
    }

    // The Champion Snowman by the start corner: three stacked snow spheres on a plinth (always there), and its
    // trimmings (hat, band, scarf, carrot, coal, stick arms) under "Trimmings", shown only by a win. It faces south.
    Piece BuildChampion(Material snowy, Material bare, float cx, float cz)
    {
        float g = GroundAt(cx, cz);
        float c1 = 0.3f + 0.6f, c2 = c1 + (0.6f + 0.45f) * 0.85f, c3 = c2 + (0.45f + 0.32f) * 0.85f;
        var body = new MeshBuilder();
        Kit.AddDisc(body, new Vector3(cx, g - 0.3f, cz), 0.9f, 0.6f, IsleKit.Stone);
        Kit.AddBall(body, new Vector3(cx, g + c1, cz), 0.6f, SnowWhite);
        Kit.AddBall(body, new Vector3(cx, g + c2, cz), 0.45f, SnowWhite);
        Kit.AddBall(body, new Vector3(cx, g + c3, cz), 0.32f, SnowWhite);
        Place("Champion Snowman", body, snowy, 0f);

        var trim = new MeshBuilder();
        float brim = g + c3 + 0.32f * 0.8f;
        Kit.AddDisc(trim, new Vector3(cx, brim, cz), 0.36f, 0.04f, Coal, 14);
        Kit.AddDisc(trim, new Vector3(cx, brim + 0.04f, cz), 0.22f, 0.32f, Coal, 14);
        Kit.AddDisc(trim, new Vector3(cx, brim + 0.06f, cz), 0.23f, 0.07f, IsleKit.Red, 14);
        Ring(trim, new Vector3(cx, 0f, cz), g + c3 - 0.33f, g + c3 - 0.19f, 0.19f, 0.29f, 10, 0f, null, IsleKit.Red);      // scarf
        Kit.AddBox(trim, new Vector3(cx + 0.12f, g + c3 - 0.62f, cz - 0.27f), new Vector3(0.12f, 0.36f, 0.04f), IsleKit.Red);    // its tail
        Kit.AddBox(trim, new Vector3(cx, g + c3 - 0.03f, cz - 0.42f), new Vector3(0.06f, 0.06f, 0.25f), Carrot);                // nose
        foreach (float ex in new[] { -0.1f, 0.1f })
            Kit.AddBall(trim, new Vector3(cx + ex, g + c3 + 0.08f, cz - 0.29f), 0.035f, Coal);
        foreach (float by in new[] { -0.12f, 0.1f })
            Kit.AddBall(trim, new Vector3(cx, g + c2 + by, cz - 0.43f), 0.045f, Coal);
        Kit.AddBox(trim, new Vector3(cx - 0.45f - 0.3f, g + c2 + 0.08f, cz), new Vector3(0.7f, 0.05f, 0.05f), IsleKit.Wood);
        Kit.AddBox(trim, new Vector3(cx + 0.45f + 0.3f, g + c2 + 0.08f, cz), new Vector3(0.7f, 0.05f, 0.05f), IsleKit.Wood);

        Piece group = b.Empty("Trimmings", cx, cz, 0f);
        Place("Champion Trimmings", trim, bare, 0f, false).Under(group);
        group.Go.SetActive(false);      // nothing hides it again: a win's trimmings stay until the pack reloads
        return group;
    }

    // ---- helpers ----

    // The ground's height at a local point, above the root's level
    float GroundAt(float x, float z) => b.WorldPoint(x, z).y - b.Root.transform.position.y;

    // An SDK part placed from the root's level (heights already include the structure's ground level)
    Piece FP(string prefab, float x, float z, float height, float yaw) => b.Part(prefab, x, z, height, yaw, false);

    // A mesh whose vertices are in the root's local frame (y above the root's level), placed at the root
    Piece Place(string name, MeshBuilder mb, Material material, float height, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, height, false);
        return piece;
    }

    // A block between two corners in the root's local frame
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A triangle wound so that it faces 'outward' (Unity draws a face whose corners run clockwise as seen from in front)
    static void Face(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Vector3 outward, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(b2 - a, c - a), outward) >= 0f) mb.Tri(a, b2, c, color);
        else mb.Tri(a, c, b2, color);
    }

    // A planar quad, corners in order round its edge, facing 'outward'
    static void Face4(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Vector3 d, Vector3 outward, Color color)
    {
        Face(mb, a, b2, c, outward, color);
        Face(mb, a, c, d, outward, color);
    }

    // A ring wall of 'sides' straight segments about 'centre' (only its x and z are used), between apothems 'inner' and
    // 'outer' (the flat faces' distances from the centre), from y0 to y1. Segment k is centred on the math angle
    // firstDeg + k * 360 / sides (from +x towards +z); segments for which 'skip' is true are left out, and the open
    // ends beside them are closed.
    static void Ring(MeshBuilder mb, Vector3 centre, float y0, float y1, float inner, float outer, int sides, float firstDeg,
        Func<int, bool> skip, Color color)
    {
        float step = 360f / sides, half = step / 2f * Mathf.Deg2Rad;
        float rIn = inner / Mathf.Cos(half), rOut = outer / Mathf.Cos(half);
        bool Skipped(int k) => skip != null && skip(((k % sides) + sides) % sides);
        for (int k = 0; k < sides; k++)
        {
            if (Skipped(k)) continue;
            float mid = (firstDeg + k * step) * Mathf.Deg2Rad, a0 = mid - half, a1 = mid + half;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            Vector3 tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));      // towards a1
            Vector3 c = new Vector3(centre.x, 0f, centre.z), lo = Vector3.up * y0, hi = Vector3.up * y1;
            Vector3 i0 = c + d0 * rIn, i1 = c + d1 * rIn, o0 = c + d0 * rOut, o1 = c + d1 * rOut;
            Face4(mb, o0 + hi, o1 + hi, i1 + hi, i0 + hi, Vector3.up, color);
            Face4(mb, o0 + lo, o1 + lo, i1 + lo, i0 + lo, Vector3.down, color);
            Face4(mb, o0 + lo, o1 + lo, o1 + hi, o0 + hi, radial, color);
            Face4(mb, i0 + lo, i1 + lo, i1 + hi, i0 + hi, -radial, color);
            if (Skipped(k - 1)) Face4(mb, i0 + lo, o0 + lo, o0 + hi, i0 + hi, -tangent, color);
            if (Skipped(k + 1)) Face4(mb, i1 + lo, o1 + lo, o1 + hi, i1 + hi, tangent, color);
        }
    }
}
