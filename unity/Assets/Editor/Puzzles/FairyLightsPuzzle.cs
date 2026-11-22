// Fairy Lights (puzzle 20): the Lamplighter's Glade. A woodland glade strung with fairy lights, a gold Star Ring in its
// middle and two stations of two bulb-ringed pads on its west and east sides (a third, north, in a 4-player lobby).
// Over each pad hangs a split-flap picture, an abstract Scribble dealt fresh on every start. The lamps that say which
// pad is wanted are not in the glade: they are on a board inside the Lamplighter's Lodge, a sealed log hut south of the
// glade with a dog-leg entrance on its far side and no windows. Each lamp on the board sits under a picture, the one
// hanging over the pad it wants. The Lamplighter holds the intercom and names the lit lamp's scribble; the runner who
// owns that picture must be on its pad when the phase runs out, and nobody on any other. When the gold star lamp
// lights, every runner must stand in the Star Ring together. Ten right phases in a row win; one wrong phase stops it.
//
// The pads look identical, and on every start the custom script LampShuffle (Assets/PuzzleScripts) deals their hidden
// sensors out afresh among the pad spots and fills the board's pictures to match, so a board slot never maps to a
// fixed place in the glade: only the picture names the pad. The rules are two Press In Orders in timed mode, one per
// lobby size (Rules 2-3 with four pads and a 2-player star, Rules 4 with six pads and a 3-player star).
//
// Coordinates: local metres, x right, z forward (north, towards the glade), height above the (levelled) ground.
// Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z. Spec: puzzle-specs\FairyLights.md.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class FairyLightsPuzzle : PuzzleDef
{
    public override string Name => "FairyLights";
    public override Vector2 Footprint => new Vector2(42f, 48f);
    public override Vector2? FixedSite => new Vector2(-860f, -2090f);      // the lowland woodland clearing (layout-final)

    const float PhaseSeconds = 7.5f;      // ship value; tune towards 6.5 after the first playtests (Risk 7)
    const int Rounds = 10;
    const float SensorH = 2.6f;           // sensor boxes 2.6 m tall, their centres 1.3 m up

    static readonly Color Warm = new Color(1f, 0.85f, 0.6f);          // every pad lamp, and the pads' bulbs
    static readonly Color StarLamp = new Color(1f, 0.75f, 0.2f);
    static readonly Color LogA = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color LogB = new Color(0.43f, 0.31f, 0.21f);
    static readonly Color Plank = new Color(0.55f, 0.40f, 0.27f);

    // Pad spots 1..6 (A1, A2 west; B1, B2 east; C1, C2 north), the picture over each, and which way it faces
    static readonly Vector2[] Spot = { Vector2.zero, new Vector2(-15f, 2f), new Vector2(-15f, 10f), new Vector2(15f, 2f), new Vector2(15f, 10f), new Vector2(-4f, 21f), new Vector2(4f, 21f) };
    static readonly Vector2[] PictureAt = { Vector2.zero, new Vector2(-17f, 2f), new Vector2(-17f, 10f), new Vector2(17f, 2f), new Vector2(17f, 10f), new Vector2(-4f, 23f), new Vector2(4f, 23f) };
    static readonly float[] PictureYaw = { 0f, 90f, 90f, -90f, -90f, 180f, 180f };
    // The Lodge board's slot for each lamp (k = 1..6); the star lamp is at x 0
    static readonly float[] SlotX = { 0f, -1.2f, -0.6f, 0.6f, 1.2f, -1.8f, 1.8f };
    static readonly Vector2 Star = new Vector2(0f, 6f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(-8f, -3.5f);
        b.ClearGround(42f, 48f, 0f, 2f);
        // no jetpack thrust over the glade and the Lodge (this also closes the spec's accepted 2-player jetpack route)
        IsleKit.CalmAir(b, "Fairy Lights", 0f, 2f, new Vector3(80f, 150f, 86f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        Piece rules23 = IsleKit.Lobby(b, "Rules 2-3", true, true, false);
        Piece rules4 = IsleKit.Lobby(b, "Rules 4", false, false, true);

        // ---- the glade: pad spots, their hidden sensors, the pictures over them, the bulb rings ----
        var sensors = new Component[7];
        for (int k = 1; k <= 6; k++)
        {
            bool north = k >= 5;
            Vector2 s = Spot[k];
            Piece sensor = IsleKit.Sensor(b, "Pad Sensor " + k, s.x, s.y, new Vector3(2.8f, SensorH, 2.8f))
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            sensors[k] = sensor.Component("PuzzlePresenceSensor");
            // where sensor k starts, for good: LampShuffle moves the sensors between these (same height as a sensor's centre)
            Piece spot = b.Empty("Pad Spot " + k, s.x, s.y, SensorH / 2f);
            Vector2 p = PictureAt[k];
            Piece picture = b.Part("Display", p.x, p.y, 1.7f, PictureYaw[k]).Named("Picture " + k)
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", true);
            Piece ring = Kit.LightString(b, "Pad Ring " + k, Circle(s.x, s.y, 1.2f, 10, 0.12f), 0.02f, 1f, true, false, 40 + k, new[] { Warm });
            if (north) { sensor.Under(rules4); spot.Under(rules4); picture.Under(rules4); ring.Under(rules4); }
        }

        // the Star Ring: one sensor per lobby size, gold bulbs inside it, and marker stones by how many must stand there
        Piece star2 = IsleKit.Sensor(b, "Star Ring 2", Star.x, Star.y, new Vector3(3.4f, SensorH, 3.4f))
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 2).Under(rules23);
        Piece star3 = IsleKit.Sensor(b, "Star Ring 3", Star.x, Star.y, new Vector3(3.4f, SensorH, 3.4f))
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 3).Under(rules4);
        Kit.LightString(b, "Star Ring Bulbs", Circle(Star.x, Star.y, 1.5f, 12, 0.12f), 0.02f, 1f, true, false, 31, new[] { IsleKit.Gold });
        MarkerStones(b, snowy, "Star Stones 2", 2, 0f).Under(rules23);
        MarkerStones(b, snowy, "Star Stones 3", 3, 30f).Under(rules4);

        // posts: the star pole, a picture post behind each picture, a speaker post with its bar board at each station
        var glade = new MeshBuilder();
        Kit.AddBox(glade, new Vector3(Star.x, 0f, Star.y), new Vector3(0.3f, 5f, 0.3f), IsleKit.Cream);
        Kit.AddBox(glade, new Vector3(Star.x, 5f, Star.y), new Vector3(0.08f, 0.35f, 0.08f), IsleKit.Gold);
        Kit.AddStar(glade, new Vector3(Star.x, 5.6f, Star.y), 0.55f, 0.08f, IsleKit.Gold);
        for (int k = 1; k <= 4; k++) PicturePost(glade, PictureAt[k], PictureYaw[k]);
        SpeakerPost(glade, new Vector2(-17.7f, 6f), 90f);
        SpeakerPost(glade, new Vector2(17.7f, 6f), -90f);
        b.MeshPiece("Glade Posts", glade.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        var northPosts = new MeshBuilder();
        for (int k = 5; k <= 6; k++) PicturePost(northPosts, PictureAt[k], PictureYaw[k]);
        SpeakerPost(northPosts, new Vector2(0f, 23.7f), 180f);
        Piece northMesh = b.MeshPiece("North Posts", northPosts.ToMesh(), snowy);
        northMesh.Transform.position = b.WorldPoint(0f, 0f);
        northMesh.Under(rules4);

        // the intercom's speakers: one on each station's post, one on the star pole's south face, one north
        Piece speakerWest = b.Part("Intercom Speaker", -17.5f, 6f, 2.3f, 90f).Named("Speaker West");
        Piece speakerEast = b.Part("Intercom Speaker", 17.5f, 6f, 2.3f, -90f).Named("Speaker East");
        Piece speakerStar = b.Part("Intercom Speaker", Star.x, Star.y - 0.25f, 3.4f, 180f).Named("Speaker Star");
        Piece speakerNorth = b.Part("Intercom Speaker", 0f, 23.5f, 2.3f, 180f).Named("Speaker North").Under(rules4);

        // the bars on the speaker posts' boards, scaled to fit them (Progress Bar is one of the three scalable parts)
        Piece roundsWest = Bar(b, "Rounds West", -17.5f, 6f, 1.95f, 90f, true);
        Piece phaseWest = Bar(b, "Phase West", -17.5f, 6f, 1.45f, 90f, true);
        Piece roundsEast = Bar(b, "Rounds East", 17.5f, 6f, 1.95f, -90f, true);
        Piece phaseEast = Bar(b, "Phase East", 17.5f, 6f, 1.45f, -90f, true);
        Piece roundsNorth = Bar(b, "Rounds North", 0f, 23.5f, 1.95f, 180f, true).Under(rules4);
        Piece phaseNorth = Bar(b, "Phase North", 0f, 23.5f, 1.45f, 180f, true).Under(rules4);

        // ---- the Lamplighter's Lodge: x -4..4, z -19..-13, sealed, its door gap on the far (south) side ----
        BuildLodge(b, snowy, bare);

        // the lamp board on the north wall's inside (its face at z -13.38): seven slots at 0.6 m pitch round the mic
        const float Face = -13.38f;
        var lamps = new Component[7];
        for (int k = 1; k <= 6; k++)
        {
            Piece lamp = b.Part("Light 8", SlotX[k], Face - 0.1f, 1.45f, 180f).Named("Lamp " + k).Set("PuzzleLight", "color", Warm);
            lamps[k] = lamp.Component("PuzzleLight");
            Piece boardPicture = b.Part("Display", SlotX[k], Face - 0.168f, 1.95f, 180f).Named("Board Picture " + k)
                .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", false);
            if (k >= 5) { lamp.Under(rules4); boardPicture.Under(rules4); }
        }
        Piece lampStar = b.Part("Light 6", 0f, Face - 0.142f, 1.7f, 180f).Named("Lamp Star").Set("PuzzleLight", "color", StarLamp);
        Piece roundsLodge = Bar(b, "Rounds Lodge", 0.3f, Face - 0.1f, 2.45f, 180f, false);
        Piece phaseLodge = Bar(b, "Phase Lodge", 0.3f, Face - 0.1f, 0.7f, 180f, false);
        b.Part("Intercom Mic", 0f, Face - 0.02f, 1.15f, 180f).Named("Lamplighter Intercom")
            .Add("PuzzleIntercom", "speakers", speakerWest.Component("PuzzleIntercomSpeaker"), speakerEast.Component("PuzzleIntercomSpeaker"),
                 speakerStar.Component("PuzzleIntercomSpeaker"), speakerNorth.Component("PuzzleIntercomSpeaker"));
        b.Part("Switch Start", 2.4f, Face - 0.08f, 1.2f, 180f).Named("Lodge Switch");      // defaults: togglePuzzleState on, defaultState off
        b.Part("Live Light", 2.4f, Face - 0.02f, 1.5f, 180f).Named("Lodge Live");
        // the room's own light (Winter Walk's RoomLights: colour, range, intensity; numbers need a '.')
        b.Empty("Room Light 1 FFD9A0 6.0 1.2", 0f, -15.5f, 2.5f);

        // ---- start corner and the Ornament Rack of the six Scribble tiles the pictures are dealt from ----
        IsleKit.StartCorner(b, -8f, -6f, 0f);
        BuildRack(b, snowy);
        for (int i = 0; i < 6; i++)
            b.Part("Peg Tile", 8f - 1.43f + i * 0.57f, -6f, 0.9f + 0.24f, 180f).Named("Scribble " + (i + 1))
                .Set("PuzzlePegTile", "imageSet", 3)      // enum index 3 = Scribbles (value 6)
                .Set("PuzzlePegTile", "image", i + 1);

        // ---- the rules: one Press In Order per lobby size; only the shown one ever runs ----
        Piece order3 = b.Empty("Press In Order 3", Star.x, Star.y, 0.5f).Under(rules23);
        Component pio3 = b.AddScript(order3, "PuzzlePressInOrder");
        Sdk.Add(pio3, "lights", lamps[1], lamps[2], lamps[3], lamps[4], lampStar.Component("PuzzleLight"));
        Sdk.Add(pio3, "buttons", sensors[1], sensors[2], sensors[3], sensors[4], star2.Component("PuzzlePresenceSensor"));
        Sdk.Add(pio3, "progressBars", roundsWest.Component("PuzzleProgressBar"), roundsEast.Component("PuzzleProgressBar"), roundsLodge.Component("PuzzleProgressBar"));
        Sdk.Add(pio3, "phaseBars", phaseWest.Component("PuzzleProgressBar"), phaseEast.Component("PuzzleProgressBar"), phaseLodge.Component("PuzzleProgressBar"));
        Sdk.Set(pio3, "rounds", Rounds);
        Sdk.Set(pio3, "holdAtEnd", true);
        Sdk.Set(pio3, "phaseSeconds", PhaseSeconds);

        Piece order4 = b.Empty("Press In Order 4", Star.x, Star.y, 0.6f).Under(rules4);
        Component pio4 = b.AddScript(order4, "PuzzlePressInOrder");
        Sdk.Add(pio4, "lights", lamps[1], lamps[2], lamps[3], lamps[4], lamps[5], lamps[6], lampStar.Component("PuzzleLight"));
        Sdk.Add(pio4, "buttons", sensors[1], sensors[2], sensors[3], sensors[4], sensors[5], sensors[6], star3.Component("PuzzlePresenceSensor"));
        Sdk.Add(pio4, "progressBars", roundsWest.Component("PuzzleProgressBar"), roundsEast.Component("PuzzleProgressBar"),
                roundsNorth.Component("PuzzleProgressBar"), roundsLodge.Component("PuzzleProgressBar"));
        Sdk.Add(pio4, "phaseBars", phaseWest.Component("PuzzleProgressBar"), phaseEast.Component("PuzzleProgressBar"),
                phaseNorth.Component("PuzzleProgressBar"), phaseLodge.Component("PuzzleProgressBar"));
        Sdk.Set(pio4, "rounds", Rounds);
        Sdk.Set(pio4, "holdAtEnd", true);
        Sdk.Set(pio4, "phaseSeconds", PhaseSeconds);

        // the shuffle (host only), at the root so it is in every lobby
        Piece shuffle = b.Empty("Lamp Shuffle", 0f, -10f, 0.5f);
        Component lampShuffle = b.AddCustomScript(shuffle, "LampShuffle");

        // ---- events: the shuffle runs after the SDK's deal and before the first lamp ----
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", lampShuffle, "Shuffle");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio3, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio4, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", lampShuffle, "Blank");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio3, "End");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio4, "End");
        // a win or a fail stops the whole puzzle visibly: switches back, live lamps off, every picture blank
        order3.On("PuzzlePressInOrder", "onEnd", b.Puzzle, "StopPuzzle");
        order4.On("PuzzlePressInOrder", "onEnd", b.Puzzle, "StopPuzzle");
        roundsWest.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        // ---- decoration: strings from the pole's top to the picture posts, round the glade, along the eaves ----
        for (int k = 1; k <= 6; k++)
        {
            Vector2 post = PictureAt[k] - Facing(PictureYaw[k]) * 0.25f;
            Piece line = Kit.LightString(b, "Pole String " + k, new List<Vector3> { new Vector3(Star.x, 4.8f, Star.y), new Vector3(post.x, 2.0f, post.y) },
                0.25f, 0.45f, true, false, 200 + k);
            if (k >= 5) line.Under(rules4);
        }
        Kit.LightRing(b, "Glade Ring", 0f, 10.5f, 39f, 29f, 2.6f, true, 21);
        Kit.LightString(b, "Eaves North", new List<Vector3> { new Vector3(-4.4f, 2.38f, -12.55f), new Vector3(4.4f, 2.38f, -12.55f) }, 0.15f, 0.4f, true, false, 61);
        Kit.LightString(b, "Eaves South", new List<Vector3> { new Vector3(-4.4f, 2.38f, -19.45f), new Vector3(4.4f, 2.38f, -19.45f) }, 0.15f, 0.4f, true, false, 62);
    }

    // ---- helpers ----

    // The way a part at 'yaw' faces, as a local (x, z) direction
    static Vector2 Facing(float yaw) => IsleKit.Turn(0f, 1f, yaw);

    // A closed ring of n posts (n + 1 points, the last on the first) at radius r, 'h' above the ground
    static List<Vector3> Circle(float cx, float cz, float r, int n, float h)
    {
        var points = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            float a = 2f * Mathf.PI * (i % n) / n;
            points.Add(new Vector3(cx + Mathf.Cos(a) * r, h, cz + Mathf.Sin(a) * r));
        }
        return points;
    }

    // A Progress Bar facing 'yaw', total 10 (Press In Order sets it to Rounds anyway); 'small' scales it to a post board
    static Piece Bar(PuzzleBuilder b, string name, float x, float z, float h, float yaw, bool small)
    {
        Piece bar = b.Part("Progress Bar", x, z, h, yaw).Named(name).Set("PuzzleProgressBar", "total", Rounds);
        if (small) bar.Scaled(0.36f, 1f, 1f);
        return bar;
    }

    // A picture post: a 0.16 m post whose face is 0.17 m behind the display's pivot, a backing plank and a little roof
    static void PicturePost(MeshBuilder mb, Vector2 display, float yaw)
    {
        Vector2 f = Facing(yaw);
        Vector2 c = display - f * 0.25f;
        Kit.AddBox(mb, new Vector3(c.x, 0f, c.y), new Vector3(0.16f, 2.1f, 0.16f), IsleKit.Wood, yaw);
        Kit.AddBox(mb, new Vector3(c.x, 1.35f, c.y), new Vector3(0.6f, 0.7f, 0.16f), IsleKit.DarkWood, yaw);
        Vector2 r = c + f * 0.05f;
        Kit.AddBox(mb, new Vector3(r.x, 2.1f, r.y), new Vector3(0.75f, 0.06f, 0.4f), IsleKit.DarkWood, yaw);
    }

    // A station's speaker post at 'centre' facing 'yaw': a 2.8 m post, a 1.4 x 1.5 board (h 1.15..2.65) whose face is
    // 0.1 m in front of the post's centre, and a roof over it
    static void SpeakerPost(MeshBuilder mb, Vector2 centre, float yaw)
    {
        Vector2 f = Facing(yaw);
        Kit.AddBox(mb, new Vector3(centre.x, 0f, centre.y), new Vector3(0.2f, 2.8f, 0.2f), IsleKit.Wood, yaw);
        Vector2 board = centre + f * 0.06f;
        Kit.AddBox(mb, new Vector3(board.x, 1.15f, board.y), new Vector3(1.4f, 1.5f, 0.08f), IsleKit.Board, yaw);
        Vector2 roof = centre + f * 0.1f;
        Kit.AddBox(mb, new Vector3(roof.x, 2.8f, roof.y), new Vector3(1.7f, 0.08f, 0.5f), IsleKit.DarkWood, yaw);
    }

    // 'count' stones round the Star Ring, outside its sensor: how many runners it wants
    static Piece MarkerStones(PuzzleBuilder b, Material snowy, string name, int count, float startDeg)
    {
        var mb = new MeshBuilder();
        for (int i = 0; i < count; i++)
        {
            float a = (startDeg + 270f + i * 360f / count) * Mathf.Deg2Rad;
            Kit.AddRock(mb, new Vector3(Star.x + Mathf.Cos(a) * 2.4f, 0f, Star.y + Mathf.Sin(a) * 2.4f), new Vector3(0.55f, 0.4f, 0.5f), 70 + count * 10 + i);
        }
        Piece stones = b.MeshPiece(name, mb.ToMesh(), snowy);
        stones.Transform.position = b.WorldPoint(0f, 0f);
        return stones;
    }

    // A log wall: the axis-aligned block x0..x1, z0..z1, from h0 to h1, laid as logs of alternating shade
    static void LogWall(MeshBuilder mb, float x0, float z0, float x1, float z1, float h0, float h1)
    {
        const float Log = 0.35f;
        int i = 0;
        for (float y = h0; y < h1 - 0.001f; y += Log, i++)
        {
            float top = Mathf.Min(y + Log, h1);
            Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), new Vector3(x1 - x0, top - y, z1 - z0), i % 2 == 0 ? LogA : LogB);
        }
    }

    // A slab whose underside is the quad 'low' (corners in the order AddBox uses: x-z-, x+z-, x+z+, x-z+) and whose top
    // is 'thick' above it: the same faces and winding as Kit.AddBox, so it shows from outside like any box
    static void AddSlab(MeshBuilder mb, Vector3[] low, float thick, Color color)
    {
        var high = new Vector3[4];
        for (int i = 0; i < 4; i++) high[i] = low[i] + Vector3.up * thick;
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }

    // The Lodge: log walls 0.3 thick and 2.8 high round x -4..4, z -19..-13; a 1.3 m door gap in the south wall at
    // x 2.0..3.3; a full-height screen wall at z -17.5 (x -0.6..3.85) making a vestibule that enters the room at its west
    // end; a plank ceiling at 2.8; a roof pitched to 4.2 whose underside overlaps the wall tops by 0.3; closed gables.
    // No window, no opening above: nothing outside sees the board.
    static void BuildLodge(PuzzleBuilder b, Material snowy, Material bare)
    {
        const float Wall = 2.8f, Ridge = 4.2f;
        var walls = new MeshBuilder();
        LogWall(walls, -4f, -13.3f, 4f, -13f, 0f, Wall);              // north (the board's wall)
        LogWall(walls, -4f, -18.7f, -3.7f, -13.3f, 0f, Wall);         // west
        LogWall(walls, 3.7f, -18.7f, 4f, -13.3f, 0f, Wall);           // east
        LogWall(walls, -4f, -19f, 2.0f, -18.7f, 0f, Wall);            // south, west of the door
        LogWall(walls, 3.3f, -19f, 4f, -18.7f, 0f, Wall);             // south, east of the door
        LogWall(walls, 2.0f, -19f, 3.3f, -18.7f, 2.2f, Wall);         // over the door
        LogWall(walls, -0.6f, -17.625f, 3.85f, -17.375f, 0f, Wall);   // the screen wall, wall to ceiling
        foreach (Vector2 c in new[] { new Vector2(-4f, -13f), new Vector2(4f, -13f), new Vector2(-4f, -19f), new Vector2(4f, -19f) })
            Kit.AddBox(walls, new Vector3(c.x, 0f, c.y), new Vector3(0.36f, 2.55f, 0.36f), IsleKit.DarkWood);      // (under the roof's eaves)
        Kit.AddBox(walls, new Vector3(1.95f, 0f, -18.85f), new Vector3(0.12f, 2.2f, 0.36f), IsleKit.DarkWood);     // door frame
        Kit.AddBox(walls, new Vector3(3.35f, 0f, -18.85f), new Vector3(0.12f, 2.2f, 0.36f), IsleKit.DarkWood);
        Kit.AddBox(walls, new Vector3(0f, Wall, -16f), new Vector3(7.4f, 0.1f, 5.4f), Plank);                     // ceiling, between the walls' tops (inside the roof slab)

        // the roof: two slabs from the ridge (z -16, underside 4.2) out past the walls; the underside passes the walls'
        // inner faces at 2.8 and so sits 0.16 m into their tops at the outer faces
        float slope = (Ridge - Wall) / 2.7f;
        float U(float d) => Wall + d * slope;          // underside height at d metres in from an inner face line
        const float X = 4.4f, Thick = 0.22f;
        AddSlab(walls, new[] { new Vector3(-X, U(2.7f), -16f), new Vector3(X, U(2.7f), -16f), new Vector3(X, U(-0.7f), -12.6f), new Vector3(-X, U(-0.7f), -12.6f) }, Thick, IsleKit.DarkWood);
        AddSlab(walls, new[] { new Vector3(-X, U(-0.7f), -19.4f), new Vector3(X, U(-0.7f), -19.4f), new Vector3(X, U(2.7f), -16f), new Vector3(-X, U(2.7f), -16f) }, Thick, IsleKit.DarkWood);
        Kit.AddBox(walls, new Vector3(0f, Ridge + 0.1f, -16f), new Vector3(2f * X + 0.1f, 0.2f, 0.5f), IsleKit.DarkWood);   // ridge cap
        // the gables: log infill from the wall tops up into the roof, each log as long as the roof is wide at its foot
        int g = 0;
        for (float y = Wall; y < Ridge; y += 0.15f, g++)
        {
            float d = (y - Wall) / slope, length = 5.4f - 2f * d;
            if (length < 0.15f) break;
            Color shade = g % 2 == 0 ? LogA : LogB;
            Kit.AddBox(walls, new Vector3(-3.85f, y, -16f), new Vector3(0.3f, 0.15f, length), shade);
            Kit.AddBox(walls, new Vector3(3.85f, y, -16f), new Vector3(0.3f, 0.15f, length), shade);
        }
        b.MeshPiece("Lodge", walls.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // inside, where no snow may settle: the plank floor, the lamp board (x -2.1..2.7, h 0.4..2.75, face z -13.38)
        // and the gold star plaque behind the star lamp
        var inside = new MeshBuilder();
        Kit.AddBox(inside, new Vector3(0f, -0.3f, -16f), new Vector3(7.4f, 0.35f, 5.4f), Plank);
        Kit.AddBox(inside, new Vector3(0.3f, 0.4f, -13.34f), new Vector3(4.8f, 2.35f, 0.08f), IsleKit.Board);
        Kit.AddStar(inside, new Vector3(0f, 1.7f, -13.38f), 0.38f, 0.012f, IsleKit.Gold);
        b.MeshPiece("Lodge Inside", inside.ToMesh(), bare).Transform.position = b.WorldPoint(0f, 0f);
    }

    // The Ornament Rack at (8, -6): a shelf 3.6 x 0.6 (top at 0.9) on two trestles, a back board, a little roof on two
    // posts, and a short string of lights along the roof
    static void BuildRack(PuzzleBuilder b, Material snowy)
    {
        var mb = new MeshBuilder();
        Kit.AddBox(mb, new Vector3(8f, 0.84f, -6f), new Vector3(3.6f, 0.06f, 0.6f), Plank);
        foreach (float x in new[] { 6.5f, 9.5f })
        {
            Kit.AddBox(mb, new Vector3(x, 0f, -6.2f), new Vector3(0.1f, 0.84f, 0.1f), IsleKit.Wood);
            Kit.AddBox(mb, new Vector3(x, 0f, -5.8f), new Vector3(0.1f, 0.84f, 0.1f), IsleKit.Wood);
            Kit.AddBox(mb, new Vector3(x, 0.45f, -6f), new Vector3(0.1f, 0.08f, 0.5f), IsleKit.Wood);
        }
        Kit.AddBox(mb, new Vector3(8f, 0.9f, -5.68f), new Vector3(3.6f, 0.6f, 0.04f), IsleKit.DarkWood);
        foreach (float x in new[] { 6.25f, 9.75f })
            Kit.AddBox(mb, new Vector3(x, 0f, -5.62f), new Vector3(0.1f, 2.1f, 0.1f), IsleKit.Wood);
        Kit.AddBox(mb, new Vector3(8f, 2.1f, -5.95f), new Vector3(4.0f, 0.08f, 0.9f), IsleKit.DarkWood);
        b.MeshPiece("Ornament Rack", mb.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        Kit.LightString(b, "Rack String", new List<Vector3> { new Vector3(6.0f, 2.04f, -6.4f), new Vector3(10.0f, 2.04f, -6.4f) }, 0.2f, 0.4f, true, false, 71);
    }
}
