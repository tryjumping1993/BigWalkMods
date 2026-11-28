// Lookouts (puzzle 27): an island-wide signal relay against the clock (spec: puzzle-specs\Lookouts.md).
//
// One player, the Keeper, works shut in the Keeper's hut on the Observatory, a round stone terrace on a lowland knoll.
// The hut is walled and roofed; its one door follows the round (PuzzleDoor CloseWhenPuzzleActive), so it is shut from
// the moment the Keeper turns the hut's knob, the only Start Switch that starts a round, until the round ends. Inside:
// the Peg Board with a colour-and-pip plaque over each slot, twelve Scribbles tiles on two tables, the Validator, a
// four-minute Timer, a three-strike bar, the Keeper's lever and the gourd's vice.
//
// The other players are Lookouts, one per beacon station on a high place 0.5 to 1.4 km away (North always, South for
// three or more players, Summit for four). Each station has a booth that opens away from the Observatory, with a
// split-flap Display (pips under each flap), a press-together lever, a live lamp and a stop-only knob. The Lookouts
// describe their pictures over walkie-talkies, the Keeper hangs the matching tiles, and when the board is full every
// lever is pulled within the same two seconds: that is the only thing that runs the Validator's check. A wrong board is
// a strike; three strikes or the Timer end the round (the door opens, the displays blank), and the next start re-deals.
//
// Parts face their local +z; "yaw" turns that: 90 faces +x, -90 faces -x, 180 faces -z.
using System.Collections.Generic;
using System.Linq;
using Bwm;
using UnityEngine;

public sealed class LookoutsPuzzle : PuzzleDef
{
    public override string Name => "Lookouts";
    public override Vector2 Footprint => new Vector2(22f, 22f);
    public override Vector2? FixedSite => new Vector2(-1010f, -1280f);      // layout-final: the central Observatory knoll

    // the lookout stations (world x, z), in the order lobbies get them: North always, South for 3+, Summit for 4
    // (Far West was dropped by the redesign: four players now use three stations)
    public static readonly Vector2[] Stations = { new Vector2(-1400f, -980f), new Vector2(-1250f, -2000f), new Vector2(-2244.5f, -1915f) };
    static readonly string[] Names = { "North", "South", "Summit" };
    static readonly Color[] Colours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.12f, 0.6f, 0.25f), new Color(0.98f, 0.75f, 0.2f) };
    const float StationRadius = 7f;
    // a station's beacon mast: its top platform above the station's floor, and the beacon lamp's middle
    const float MastTop = 24f, BeaconUp = MastTop + 1.8f;
    // each lookout's ground, for the island to level and keep clear (not the summit's: the summit is level ground
    // already, and a flat there would cut into its plateau)
    public override IEnumerable<Vector3> ExtraSites => Stations.Where((s, i) => i != 2).Select(s => new Vector3(s.x, s.y, StationRadius));

    const float Floor = 0.3f;      // the terrace top, above the root's level
    static readonly Color White = new Color(0.95f, 0.95f, 0.93f);

    // ---- geometry still to be measured in game (spec risks 3a and 3b): one line each to fix ----
    // Peg Board slots: the board's pivot is at one end and it runs to its own -x (2 m for five slots); at yaw 180 that
    // is world +x. Slot k (1-based) is taken to sit SlotFirst + SlotPitch * (k - 1) from the pivot, slot 1 at the pivot end.
    const float PegX = -1.9f, PegZ = 3.34f, SlotFirst = 0.2f, SlotPitch = 0.4f;
    // Which physical flap the game calls flap k, as an offset along the station's u axis (the Display's local +x).
    // Defaults: the SDK's single-frame fallback, which lays frames at (k - 1 - (n - 1) / 2) * 0.6 m along local +x
    // (Mod/PuzzleDisplay.cs SingleFrameWidth). Replace per station and per Display once measured.
    static float[] FlapU(int station, int flaps) => flaps == 3 ? new[] { -0.6f, 0f, 0.6f } : new[] { -0.3f, 0.3f };

    // the boards' answers: (station, flap) per slot, left to right as the slots are numbered
    static readonly (int s, int f)[] AnswersTwo = { (0, 3), (0, 1), (0, 2) };
    static readonly (int s, int f)[] AnswersThree = { (1, 2), (0, 1), (1, 3), (0, 3), (1, 1), (0, 2) };
    static readonly (int s, int f)[] AnswersFour = { (2, 1), (1, 2), (0, 1), (2, 2), (0, 2), (1, 1) };

    PuzzleBuilder b;
    Vector3 site;
    readonly Piece[] display3 = new Piece[3], display2 = new Piece[3];

    Vector2 Local(Vector2 world) => new Vector2(world.x - site.x, world.y - site.z);
    static float Bearing(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;
    // a point in a station's frame (u across, v away from the Observatory, y up), relative to the station's centre
    static Vector3 P(float u, float v, float y, float th) { Vector2 t = IsleKit.Turn(u, v, th); return new Vector3(t.x, y, t.y); }

    static void Lever(Piece lever) => lever.Set("PuzzleSimPressSwitch", "group", "lookouts").Set("PuzzleSimPressSwitch", "activeSeconds", 2f).Set("PuzzleSimPressSwitch", "timeoutSeconds", 4f);

    // White pips (0.04 m cubes) under each flap of one Display: n pips under the flap the game calls flap n
    Piece Pips(int i, int flaps, Vector2 c, float baseH, float th)
    {
        var mb = new MeshBuilder();
        float[] us = FlapU(i, flaps);
        for (int f = 1; f <= flaps; f++)
            for (int k = 0; k < f; k++)
                Kit.AddBox(mb, P(us[f - 1] + (k - (f - 1) / 2f) * 0.07f, 0.68f, 1.93f, th), new Vector3(0.04f, 0.04f, 0.04f), White, th);
        Piece pips = b.MeshPiece("Lookout " + Names[i] + " Pips " + flaps, mb.ToMesh(), IsleKit.Bare(b), false);
        pips.Transform.position = b.WorldPoint(c.x, c.y, baseH, false);
        return pips;
    }

    Piece StationDisplay(int i, int flaps, Vector2 c, float baseH, float th)
    {
        Vector2 o = IsleKit.Turn(0f, 0.83f, th);
        return b.Part("Display", c.x + o.x, c.y + o.y, baseH + 2.25f, th, false).Named("Lookout " + Names[i] + " Display " + flaps)
            .Set("PuzzleDisplay", "size", flaps == 3 ? 2 : 1);      // PuzzleDisplaySize { One, Two, Three, Five }
    }

    // A lookout station at Stations[i]: a deep stone drum, a three-sided roofed booth opening away from the
    // Observatory with the Display, pips, lever, stop-only knob and live lamp on its coloured back board, a beacon mast
    // behind it, lights round it. Everything in the station frame at one base height (the drum's top), onGround false.
    void Station(int i, Piece group)
    {
        Vector2 at = Stations[i], c = Local(at);
        float th = Bearing(new Vector2(site.x, site.z), at);      // faces away from the Observatory
        if (!b.Ground.At(at.x, at.y, out float gy)) throw new System.Exception("Lookouts: no ground under the " + Names[i] + " station");
        float baseH = gy + 0.3f - site.y;
        var pieces = new List<Piece>();
        Piece Keep(Piece p) { if (p != null) pieces.Add(p); return p; }

        // ---- drum, booth and mast (one mesh) ----
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, new Vector3(0f, -3.3f, 0f), 4.6f, 3.3f, IsleKit.Stone, 20);      // a deep drum, so it never floats on a slope
        Kit.AddBox(mb, P(0f, 0.42f, 0f, th), new Vector3(3.2f, 2.9f, 0.2f), IsleKit.Stone * 0.9f, th);      // closed back, v 0.32..0.52
        Kit.AddBox(mb, P(0f, 0.6f, 0.9f, th), new Vector3(2.6f, 2.0f, 0.12f), Colours[i], th);       // coloured board, face at v 0.66
        for (int s = -1; s <= 1; s += 2)
        {
            Kit.AddBox(mb, P(s * 1.5f, 1.66f, 0f, th), new Vector3(0.2f, 2.9f, 2.68f), IsleKit.Stone * 0.9f, th);      // side walls, v 0.32..3.0
            Kit.AddBox(mb, P(s * 1.5f, 1.66f, 2.9f, th), new Vector3(0.26f, 0.1f, 2.74f), IsleKit.Wood, th);           // timber caps
        }
        Kit.AddBox(mb, P(0f, 1.7f, 2.9f, th), new Vector3(3.6f, 0.18f, 3.0f), IsleKit.DarkWood, th);      // roof, v 0.2..3.2
        Kit.AddBox(mb, P(0f, 1.7f, 3.08f, th), new Vector3(2.2f, 0.14f, 3.0f), IsleKit.DarkWood, th);     // a stepped ridge
        Vector3 mast = P(-3.0f, -2.0f, 0f, th);
        // the beacon mast stands above the firs (which reach about 16 m), so it shows from far along the way there
        Kit.AddBox(mb, mast, new Vector3(0.5f, MastTop, 0.5f), IsleKit.Wood);
        Kit.AddBox(mb, mast + Vector3.up * MastTop, new Vector3(2.4f, 0.25f, 2.4f), IsleKit.DarkWood);
        Kit.AddBox(mb, mast + Vector3.up * (MastTop + 3.3f), new Vector3(2.8f, 0.25f, 2.8f), IsleKit.DarkWood);
        Kit.AddBox(mb, mast + new Vector3(0f, MastTop - 5f, 0f), new Vector3(3.2f, 4.2f, 0.12f), Colours[i], th);      // a banner of the station's colour under the lamp
        Piece booth = Keep(b.MeshPiece("Lookout " + Names[i] + " Booth", mb.ToMesh(), IsleKit.Snowy(b)));
        booth.Transform.position = b.WorldPoint(c.x, c.y, baseH, false);

        var glow = new MeshBuilder();
        Kit.AddGlow(glow, mast + Vector3.up * BeaconUp, new Vector3(2.0f, 2.8f, 2.0f), 0f, Colours[i], i * 0.25f);      // the beacon
        Piece lamp = Keep(b.MeshPiece("Lookout " + Names[i] + " Beacon", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false));
        lamp.Transform.position = b.WorldPoint(c.x, c.y, baseH, false);

        // ---- the Displays and their pips: three flaps in the 2- and 3-player lobbies, two in the 4-player one ----
        // (the hider groups are top level, not nested under the station's group: spec risk 7's flattened form)
        if (i < 2)
        {
            Piece three = IsleKit.Lobby(b, Names[i] + " Three Flaps", i == 0, true, false);
            Piece two = IsleKit.Lobby(b, Names[i] + " Two Flaps", false, false, true);
            display3[i] = StationDisplay(i, 3, c, baseH, th).Under(three);
            Pips(i, 3, c, baseH, th).Under(three);
            display2[i] = StationDisplay(i, 2, c, baseH, th).Under(two);
            Pips(i, 2, c, baseH, th).Under(two);
        }
        else
        {
            display2[i] = Keep(StationDisplay(i, 2, c, baseH, th));
            Keep(Pips(i, 2, c, baseH, th));
        }

        // ---- lever, stop-only knob, live lamp, confetti, lamp ----
        Vector2 o = IsleKit.Turn(0.55f, 0.68f, th);
        Piece lever = Keep(b.Part("Button Sim", c.x + o.x, c.y + o.y, baseH + 1.25f, th, false).Named("Lookout " + Names[i] + " Lever"));
        Lever(lever);
        o = IsleKit.Turn(-0.6f, 0.735f, th);
        // a plain switch (togglePuzzleState false): it never starts or re-deals a round; either flip stops one
        Piece stop = Keep(b.Part("Switch Start", c.x + o.x, c.y + o.y, baseH + 1.25f, th, false).Named("Lookout " + Names[i] + " Stop"));
        stop.Set("PuzzleStartSwitch", "togglePuzzleState", false)
            .On("PuzzleStartSwitch", "onTurnedOn", b.Puzzle, "StopPuzzle")
            .On("PuzzleStartSwitch", "onTurnedOff", b.Puzzle, "StopPuzzle");
        o = IsleKit.Turn(-0.6f, 0.67f, th);
        Keep(b.Part("Live Light", c.x + o.x, c.y + o.y, baseH + 1.5f, th, false).Named("Lookout " + Names[i] + " Live"));
        o = IsleKit.Turn(0f, 2.6f, th);
        Keep(b.Part("Confetti", c.x + o.x, c.y + o.y, baseH + 0.2f, 0f, false).Named("Lookout " + Names[i] + " Confetti"));
        o = IsleKit.Turn(0f, 2.2f, th);
        Keep(b.Empty("Room Light " + (2 + i) + " FFE3B0 6.0 1.2", c.x + o.x, c.y + o.y, baseH + 2.7f, false));      // RoomLights reads numbers with a '.'
        Keep(IsleKit.CalmAir(b, "Lookout " + Names[i], c.x, c.y, new Vector3(14f, 50f, 14f), th, baseH - 10f, false));
        Keep(Kit.LightRing(b, "Lookout " + Names[i] + " Lights", c.x, c.y, 9.8f, 9.8f, 2.2f, true, 60 + i));      // (sizes across, not radii)

        if (group != null) foreach (Piece p in pieces) p.Under(group);
    }

    // One Peg Board (in its lobby group) answering from the displays named by 'answers', with a plaque in each slot's
    // lookout colour over it, carrying as many white pips as the flap number
    void Board(string tag, Piece group, int sizeIndex, (int s, int f)[] answers, Component validator)
    {
        Piece pegs = b.Part("Peg Board", PegX, PegZ, Floor + 1.3f, 180f, false).Named("Signal Pegs " + tag)
            .Set("PuzzlePegBoard", "size", sizeIndex)      // PuzzlePegBoardSize { Slots1, Slots2, Slots3, Slots4, Slots5, Slots6, ... }
            .Set("PuzzlePegBoard", "validator", validator);
        Component board = pegs.Component("PuzzlePegBoard");
        var plaques = new MeshBuilder();
        for (int k = 0; k < answers.Length; k++)
        {
            (int s, int f) = answers[k];
            Piece display = tag == "4" ? display2[s] : display3[s];
            Sdk.Add(board, "answerFrom", display.Component("PuzzleDisplay"));
            Sdk.AddInts(board, "answerFromFlap", f);
            float x = PegX + SlotFirst + SlotPitch * k;
            Kit.AddBox(plaques, new Vector3(x, Floor + 1.56f, PegZ - 0.015f), new Vector3(0.3f, 0.18f, 0.03f), Colours[s]);
            for (int p = 0; p < f; p++)
                Kit.AddBox(plaques, new Vector3(x + (p - (f - 1) / 2f) * 0.07f, Floor + 1.63f, PegZ - 0.05f), new Vector3(0.04f, 0.04f, 0.04f), White);
        }
        b.MeshPiece("Signal Plaques " + tag, plaques.ToMesh(), IsleKit.Bare(b), false).Under(group);
        pegs.Under(group);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        b.ClearGround(24f, 24f);
        b.DebugTeleport(0f, -6f);
        Material snowy = IsleKit.Snowy(b);
        IsleKit.CalmAir(b, "Lookouts Observatory", 0f, 0f, new Vector3(30f, 120f, 30f), 0f, -10f, false);

        // ---- the stations: North in every lobby, South for three or more players, Summit for four ----
        Station(0, null);
        Piece south = IsleKit.Lobby(b, "South Lookout", false, true, true);
        Station(1, south);
        Piece summit = IsleKit.Lobby(b, "Summit Lookout", false, false, true);
        Station(2, summit);

        // ---- the Observatory: a round stone terrace with a parapet broken by four gaps ----
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, new Vector3(0f, -0.5f, 0f), 9f, 0.8f, IsleKit.Stone, 28);
        for (int k = 0; k < 28; k++)
        {
            float a = 2f * Mathf.PI * k / 28f;
            if (k % 7 == 3) continue;      // gaps in the parapet to walk through
            Kit.AddBox(mb, new Vector3(Mathf.Sin(a) * 8.6f, Floor, Mathf.Cos(a) * 8.6f), new Vector3(1.9f, 0.7f, 0.5f), IsleKit.Stone * 0.9f, a * Mathf.Rad2Deg);
        }
        // the walkie-talkie stand, outside the hut south-east of its door
        Kit.AddBox(mb, new Vector3(3.9f, Floor + 0.8f, -5.6f), new Vector3(2.0f, 0.1f, 0.5f), IsleKit.Wood);
        Kit.AddBox(mb, new Vector3(3.1f, Floor, -5.6f), new Vector3(0.15f, 0.8f, 0.4f), IsleKit.DarkWood);
        Kit.AddBox(mb, new Vector3(4.7f, Floor, -5.6f), new Vector3(0.15f, 0.8f, 0.4f), IsleKit.DarkWood);
        b.MeshPiece("Observatory", mb.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // ---- the Keeper's hut: interior x -3.6..3.6, z -1.8..3.9, walls 0.25 m to a sealed roof at 3.6 m; one doorway
        //      in the east wall (1.24 x 2.04 m, centred z -0.8) with a solid lintel above it; no windows ----
        var hut = new MeshBuilder();
        Color wall = IsleKit.Stone * 0.95f;
        float wallH = Floor + 3.6f;      // from the root's level (inside the terrace) to the roof
        Kit.AddBox(hut, new Vector3(-3.725f, 0f, 1.05f), new Vector3(0.25f, wallH, 6.2f), wall);                // west
        Kit.AddBox(hut, new Vector3(0f, 0f, 4.025f), new Vector3(7.2f, wallH, 0.25f), wall);                    // north (back)
        Kit.AddBox(hut, new Vector3(0f, 0f, -1.925f), new Vector3(7.2f, wallH, 0.25f), wall);                   // south (front)
        Kit.AddBox(hut, new Vector3(3.725f, 0f, -1.735f), new Vector3(0.25f, wallH, 0.63f), wall);              // east, south of the door
        Kit.AddBox(hut, new Vector3(3.725f, 0f, 1.985f), new Vector3(0.25f, wallH, 4.33f), wall);               // east, north of the door
        Kit.AddBox(hut, new Vector3(3.725f, Floor + 2.04f, -0.8f), new Vector3(0.25f, 1.56f, 1.24f), wall);     // lintel
        Kit.AddBox(hut, new Vector3(0f, Floor + 3.6f, 1.05f), new Vector3(8.3f, 0.2f, 6.8f), IsleKit.DarkWood); // roof slab, 0.3 m eaves
        // the tile tables along the side walls: stone slabs on two legs, top 0.9 m, z 0.2..3.0
        for (int s = -1; s <= 1; s += 2)
        {
            Kit.AddBox(hut, new Vector3(s * 3.0f, Floor + 0.8f, 1.6f), new Vector3(0.8f, 0.1f, 2.8f), IsleKit.Stone);
            Kit.AddBox(hut, new Vector3(s * 3.0f, Floor, 0.5f), new Vector3(0.6f, 0.8f, 0.2f), IsleKit.Stone * 0.85f);
            Kit.AddBox(hut, new Vector3(s * 3.0f, Floor, 2.7f), new Vector3(0.6f, 0.8f, 0.2f), IsleKit.Stone * 0.85f);
        }
        b.MeshPiece("Keeper Hut", hut.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        b.Part("Door", 3.73f, -0.8f, Floor, 90f, false).Named("Keeper Hut Door")
            .Set("PuzzleDoor", "mode", 1)      // CloseWhenPuzzleActive: shut exactly while the round runs
            .Set("PuzzleDoor", "soundproof", false);
        b.Empty("Room Light 1 FFE3B0 6.0 1.2", 0f, 1.0f, Floor + 3.0f, false);      // the hut's lamp
        // (no effect until Winter Walk's snowfall cut-out looks beyond "Indoors Pizzeria"; harmless meanwhile)
        b.Empty("Indoors Keeper Hut", 0f, 1.05f, Floor + 1.8f, false).Transform.localScale = new Vector3(7.2f, 3.6f, 5.7f);

        // the start corner inside the hut along the front wall, facing the board: the only Start Switch that starts a round
        IsleKit.StartCorner(b, 0f, -0.9f, 0f, true, Floor, false);

        // ---- the signal board against the back wall, facing into the hut ----
        Piece signal = IsleKit.BoardOnPosts(b, "Signal Board", 0f, 3.4f, 180f, 4.4f, 0.8f, 2.8f, IsleKit.Board, false);
        signal.Transform.position = b.WorldPoint(0f, 3.4f, Floor, false);
        Piece validator = b.Part("Validator", 1.5f, 3.34f, Floor + 2.1f, 180f, false).Named("Signal Validator");      // no poke button: only the levers check
        Component check = validator.Component("PuzzleValidator");
        Board("2", IsleKit.Lobby(b, "Board Two", true, false, false), 2, AnswersTwo, check);
        Board("3", IsleKit.Lobby(b, "Board Three", false, true, false), 5, AnswersThree, check);
        Board("4", IsleKit.Lobby(b, "Board Four", false, false, true), 5, AnswersFour, check);
        Piece obsLever = b.Part("Button Sim", 1.5f, 3.32f, Floor + 1.2f, 180f, false).Named("Observatory Lever");
        Lever(obsLever);
        Piece timer = b.Part("Timer", -0.9f, 3.32f, Floor + 2.45f, 180f, false).Named("Signal Timer").Set("PuzzleTimer", "duration", 240f);
        Piece strikes = b.Part("Progress Bar", 1.5f, 3.31f, Floor + 2.6f, 180f, false).Named("Strikes").Scaled(0.3f, 0.6f, 1f)
            .Set("PuzzleProgressBar", "total", 3).Set("PuzzleProgressBar", "style", 0);

        // ---- twelve Scribbles tiles on the two tables (never under a Hider: CheckPile counts 3+2+3+2+2 = 12 flaps) ----
        for (int k = 0; k < 12; k++)
        {
            bool west = k < 6;
            int row = k % 6;
            b.Part("Peg Tile", west ? -3.0f : 3.0f, 0.45f + row * 0.45f, Floor + 0.9f + 0.24f, west ? 90f : -90f, false)
                .Named("Tile " + (k + 1))
                .Set("PuzzlePegTile", "imageSet", 3)      // index of Scribbles in PuzzlePegSet { Shapes, Peg, Emotes, Scribbles, Custom } (value 6)
                .Set("PuzzlePegTile", "image", k + 1);
        }

        // ---- four walkie-talkies on the stand outside ----
        for (int k = 0; k < 4; k++)
            b.Part("Vanilla Item", 3.25f + k * 0.45f, -5.6f, Floor + 0.9f + 0.15f, 0f, false).Named("Walkie-Talkie " + (k + 1))
                .Set("PuzzleVanillaItem", "itemType", 4)      // WalkieTalkie
                .Set("PuzzleVanillaItem", "savesOnReload", false);

        // ---- a pennant at the rim towards each lookout, in its colour, with an arrow board under it ----
        // (Telescopes stood here until 2026-11-24, sunk to the knee and looking into the parapet: "the telescopes
        // don't point to anything". Raised and aimed they still would not: the build measured each line of sight, and
        // the ground between stands 20 m above the line to the North beacon, 18 m above the Summit's and just over the
        // South's. So there are none: the pennants say which way to walk, and the 24 m beacon masts show from nearer.)
        for (int i = 0; i < Stations.Length; i++)
        {
            float bearing = Bearing(new Vector2(site.x, site.z), Stations[i]);
            Vector2 flag = IsleKit.Turn(0f, 6.9f, bearing), side = IsleKit.Turn(0.4f, 0f, bearing), tip = IsleKit.Turn(0f, 0.55f, bearing);
            var pennant = new MeshBuilder();
            Kit.AddBox(pennant, new Vector3(flag.x, Floor, flag.y), new Vector3(0.1f, 3.2f, 0.1f), IsleKit.DarkWood);
            Kit.AddBox(pennant, new Vector3(flag.x + side.x, Floor + 2.5f, flag.y + side.y), new Vector3(0.8f, 0.55f, 0.03f), Colours[i], bearing + 90f);
            // the arrow: a bar along the bearing at chest height with a block at its far end, in the station's colour
            Kit.AddBox(pennant, new Vector3(flag.x + tip.x * 0.5f, Floor + 1.3f, flag.y + tip.y * 0.5f), new Vector3(0.1f, 0.14f, 1.1f), Colours[i], bearing);
            Kit.AddBox(pennant, new Vector3(flag.x + tip.x * 1.1f, Floor + 1.22f, flag.y + tip.y * 1.1f), new Vector3(0.34f, 0.3f, 0.2f), Colours[i], bearing);
            Piece flagPiece = b.MeshPiece("Observatory Pennant " + Names[i], pennant.ToMesh(), IsleKit.Bare(b));
            flagPiece.Transform.position = b.WorldPoint(0f, 0f, 0f, false);
            Piece group = i == 1 ? IsleKit.Lobby(b, "Observatory South", false, true, true) : i == 2 ? summit : null;
            if (group != null) flagPiece.Under(group);
        }
        Kit.LightRing(b, "Observatory Lights", 0f, 0f, 19.6f, 19.6f, 2.4f, true, 71);

        // ---- wiring ----
        Component sdkTimer = timer.Component("PuzzleTimer"), bar = strikes.Component("PuzzleProgressBar");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", sdkTimer, "StartTimer")      // 1: the round's clock
            .On("PuzzleSDK", "onPuzzleStart", bar, "Reset")                       // 2: fresh strikes
            .On("PuzzleSDK", "onPuzzleStop", sdkTimer, "ResetTimer")              // 3
            .On("PuzzleSDK", "onPuzzleStop", bar, "Reset")                        // 4: Reset, not Fail (stop also fires at spawn)
            .On("PuzzleSDK", "onWin", sdkTimer, "PauseTimer");                    // 5: a won round never times out
        timer.On("PuzzleTimer", "onFinished", b.Puzzle, "StopPuzzle");            // 6: time up ends the round, opens the door
        obsLever.On("PuzzleSimPressSwitch", "onSuccess", check, "Check");         // 7: the levers are the "send" key (every member fires it: wire one)
        validator.On("PuzzleValidator", "onIncorrect", bar, "Increment");         // 8: one strike per wrong check
        strikes.On("PuzzleProgressBar", "onFull", b.Puzzle, "StopPuzzle");        // 9: the third strike ends the round
        // 10, 11: each booth's stop-only knob (in Station)

        Debug.Log("[BWM] Lookouts: " + string.Join(", ", Stations.Select((s, i) => Names[i] + " at " + Sdk.F(s.x) + ", " + Sdk.F(s.y) + " (" + Sdk.F((s - new Vector2(site.x, site.z)).magnitude) + " m)")));
    }
}
