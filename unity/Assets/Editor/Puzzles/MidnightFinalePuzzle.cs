// Midnight Finale (puzzle 24): the island's fireworks show, fired by its crew. A communication puzzle for three
// players (four with a fourth bay), built from the redesign spec of 2026-11-22.
//
// One player, the Show Caller, is shut in the Firing Room: a small soundproof box with no windows, a door
// that shuts while the puzzle runs, and a console of conductor lamps, each beside a split-flap display showing a scribble. The others
// are Firers in open-fronted bays 20 m away on either side (a third bay to the north in lobbies of four or more), each
// bay with three hold buttons under copies of three of the console's displays. Every five seconds one console lamp
// lights; the Caller describes its scribble over a push-to-talk intercom (the firers cannot answer), and when the phase
// ends the firer whose button carries that scribble must be holding it, and nobody any other (the SDK's Press In Order
// in its timed mode). Twelve clean calls fill the programme: the rockets go up, and within 15 s every player must pull
// their own lever in the same 1.5 s (the finale, through a Validator that is powered only then). A misfire starts the
// show again from the top. The tiles are re-dealt and the console's units moved to new slots at every start
// (ConsoleShuffle, the only custom script), so nothing can be learned from one start to the next.
//
// Coordinates are local metres from the Firing Room's centre, x east, z north. Everything stands on one level pad
// (top at Floor), placed onGround: false at Floor + h. Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 -z.
// A wall-mounted part stands at its wall's plane plus -minZ from world\part-metrics.tsv, so its back touches the wall.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class MidnightFinalePuzzle : PuzzleDef
{
    public override string Name => "MidnightFinale";
    // The levelled circle round the root has radius Footprint.magnitude / 2 = 36.8 m, enough for the pad's corners
    public override Vector2 Footprint => new Vector2(52f, 52f);
    // An open headland by the plaza hub (puzzle-specs\layout-final.json; sitecheck PASS)
    public override Vector2? FixedSite => new Vector2(-555f, -815f);
    public override float Yaw => 0f;

    const float Floor = 0.25f;                  // top of the pad, above the root's level
    const float Phase = 5f;                     // seconds per call
    const int Rounds = 12;                      // clean calls in a row
    const float FinaleSeconds = 15f, RestartSeconds = 6f;
    const float LeverActive = 1.5f, LeverTimeout = 2.5f;
    static readonly Vector2 Star = new Vector2(0f, 10f);
    const float LauncherRing = 4f;
    static readonly Color[] Colours = { new Color(0.95f, 0.2f, 0.15f), new Color(0.2f, 0.9f, 0.3f), new Color(0.3f, 0.5f, 1f), new Color(1f, 0.85f, 0.25f), new Color(0.9f, 0.3f, 0.9f), Color.white };

    // The console: columns from the Caller's left to right (they face north), rows up the wall
    static readonly float[] ColumnX = { -0.9f, 0f, 0.9f };
    static readonly float[] RowH = { 1.3f, 1.75f, 2.2f };
    // Each unit's built slot (row, column), 1-based; ConsoleShuffle moves them among the slots at every start
    static readonly int[,] UnitSlot = { { 1, 2 }, { 2, 3 }, { 3, 1 }, { 1, 1 }, { 2, 2 }, { 3, 3 }, { 1, 3 }, { 2, 1 }, { 3, 2 } };

    PuzzleBuilder b;

    // A block from its low corner to its high corner (x, z, and h above the pad), into a mesh in the root's frame
    static void Block(MeshBuilder mb, float x0, float x1, float z0, float z1, float h0, float h1, Color colour)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, Floor + h0, (z0 + z1) / 2f), new Vector3(x1 - x0, h1 - h0, z1 - z0), colour);

    // A block in a bay's own frame (w across, d from the bay's centre towards its open side), turned by the bay's yaw
    static void BayBlock(MeshBuilder mb, Vector2 centre, float yaw, float w0, float w1, float d0, float d1, float h0, float h1, Color colour)
    {
        Vector2 at = centre + IsleKit.Turn((w0 + w1) / 2f, (d0 + d1) / 2f, yaw);
        Kit.AddBox(mb, new Vector3(at.x, Floor + h0, at.y), new Vector3(w1 - w0, h1 - h0, d1 - d0), colour, yaw);
    }

    // A wall part at local (x, z), h above the pad, facing 'yaw'
    Piece Wall(string prefab, string name, float x, float z, float h, float yaw)
        => b.Part(prefab, x, z, Floor + h, yaw, false).Named(name);

    // A firing bay: three-sided, its back wall towards the room (inner face 1.25 m behind its centre), open outwards
    // along the bay's yaw; walls 2.7 m, a roof with an overhang
    Piece BayShell(string name, Vector2 centre, float yaw, Material snowy)
    {
        var mb = new MeshBuilder();
        BayBlock(mb, centre, yaw, -1.8f, 1.8f, -1.4f, -1.25f, 0f, 2.7f, IsleKit.Red);           // back wall
        BayBlock(mb, centre, yaw, -1.8f, -1.65f, -1.4f, 1.4f, 0f, 2.7f, IsleKit.Wood);          // side walls
        BayBlock(mb, centre, yaw, 1.65f, 1.8f, -1.4f, 1.4f, 0f, 2.7f, IsleKit.Wood);
        BayBlock(mb, centre, yaw, -2.2f, 2.2f, -1.8f, 1.8f, 2.7f, 2.85f, IsleKit.Red);          // roof
        BayBlock(mb, centre, yaw, -2.25f, 2.25f, 1.75f, 1.85f, 2.6f, 2.75f, IsleKit.Gold);      // gold fascia over the open side
        return b.MeshPiece(name, mb.ToMesh(), snowy);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(52f, 42f, 0f, 5f);
        b.DebugTeleport(0f, -12f, Floor + 1.5f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the pad: one level stone slab for the whole site, sunk 1.5 m, snow on its top ----
        var pad = new MeshBuilder();
        Kit.AddBox(pad, new Vector3(0f, -1.5f, 5f), new Vector3(50f, 1.5f + Floor, 40f), IsleKit.Stone);
        b.MeshPiece("Pad", pad.ToMesh(), snowy);

        // ---- jetpacks do not fire over the site (a firer flying between bays), from under the pad to far above ----
        IsleKit.CalmAir(b, "Midnight Finale", 0f, 5f, new Vector3(70f, 150f, 60f), 0f, -10f, false);

        // ---- radios are dead over the site; the fixed intercom is not a prop, so it keeps working ----
        Piece dead = b.Part("Radio Dead Zone", 0f, 6f, Floor + 8f, 0f, false).Named("Dead Zone");
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(60f, 20f, 52f);
        zone.center = Vector3.zero;

        // ---- lobby groups: the fourth crew's bay and console units, and each lobby's own Press In Order ----
        Piece fourth = IsleKit.Lobby(b, "Fourth Crew", false, false, true);
        Piece three = IsleKit.Lobby(b, "Three Crew", true, true, false);

        // ---- the Firing Room: inside x -1.8..1.8, z -1.6..1.2, 2.8 m high; walls and roof 0.2 thick ----
        // Only its own mesh goes under the soundproof parent (PuzzleSoundProof takes every collider beneath it and turns
        // triggers solid): every SDK part of the room stays under the root.
        Piece room = b.Empty("Firing Room", 0f, -0.2f, Floor, false);
        Component proof = b.AddScript(room, "PuzzleSoundProof");
        Sdk.Set(proof, "material", 0);      // Wood
        var shell = new MeshBuilder();
        Block(shell, -2.0f, -1.8f, -1.8f, 1.4f, 0f, 2.8f, IsleKit.Red);         // west wall
        Block(shell, 1.8f, 2.0f, -1.8f, 1.4f, 0f, 2.8f, IsleKit.Red);           // east wall
        Block(shell, -1.8f, 1.8f, 1.21f, 1.4f, 0f, 2.8f, IsleKit.Red);          // north wall
        Block(shell, -1.8f, 1.8f, 1.2f, 1.21f, 0f, 2.8f, IsleKit.Board);        // the console panel: the north wall's inner face
        Block(shell, -1.8f, -0.62f, -1.8f, -1.6f, 0f, 2.8f, IsleKit.Red);       // south wall, west of the door
        Block(shell, 0.62f, 1.8f, -1.8f, -1.6f, 0f, 2.8f, IsleKit.Red);         // south wall, east of the door
        Block(shell, -0.62f, 0.62f, -1.8f, -1.6f, 2.02f, 2.8f, IsleKit.Red);    // lintel: the doorway is exactly the door
        // (No baffle inside the door any more. It left a passage 0.6 m deep behind the door and 0.7 m wide at each end,
        // which a player had to shuffle through sideways: "you need to maneuver through the geometry in an unnatural
        // manner", the user, 2026-11-24. It hid the console from the doorway, which the door does itself: it is shut,
        // and soundproof, for as long as the puzzle runs, and the console shows nothing before that.)
        Block(shell, -2.0f, 2.0f, -1.8f, 1.4f, 2.8f, 3.0f, IsleKit.Red);        // roof
        Block(shell, -1.8f, 1.8f, -1.6f, 1.2f, 0f, 0.03f, IsleKit.Wood);        // floor
        b.MeshPiece("Firing Room Shell", shell.ToMesh(), snowy).Under(room);
        // the show box's trim: a gold cornice and a little star on the roof (no collider, outside the soundproof parent)
        var trim = new MeshBuilder();
        Block(trim, -2.06f, 2.06f, -1.86f, 1.46f, 2.72f, 2.9f, IsleKit.Gold);
        Block(trim, -0.04f, 0.04f, -0.24f, -0.16f, 3.0f, 3.35f, IsleKit.Gold);
        Kit.AddStar(trim, new Vector3(0f, Floor + 3.65f, -0.2f), 0.4f, 0.1f, IsleKit.Gold);
        b.MeshPiece("Firing Room Trim", trim.ToMesh(), snowy, false);

        // The door: shut while the puzzle runs, soundproof, centred in the south wall's thickness
        Piece door = b.Part("Door", 0f, -1.7f, Floor, 0f, false).Named("Firing Room Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        // The inner start switch and its live lamp on the east wall (every start switch follows the others)
        Wall("Switch Start", "Inner Start", 1.727f, -0.4f, 1.3f, -90f);
        Wall("Live Light", "Inner Live Light", 1.79f, -0.4f, 1.5f, -90f);
        Piece pendant = Wall("Light 2", "Room Pendant", 0f, 0.2f, 2.8f, 0f);
        // a dim warm light that is always on (Winter Walk's RoomLights), so the Caller finds the switch at night
        b.Empty("Room Light 1 FFD9A0 3 0.35", 0f, 0.2f, Floor + 2.5f, false);

        // ---- the console: nine units (six in lobbies of two or three), each a display with its lamp to its left ----
        var consoleDisplay = new Piece[9];
        var consoleLamp = new Component[9];
        for (int k = 0; k < 9; k++)
        {
            float cx = ColumnX[UnitSlot[k, 1] - 1], h = RowH[UnitSlot[k, 0] - 1];
            Piece unit = b.Empty("Console Unit " + (k + 1), cx, 1.032f, Floor + h, false);
            Piece display = Wall("Display", "Console Display " + (k + 1), cx, 1.032f, h, 180f).Set("PuzzleDisplay", "size", 0);
            Piece lamp = Wall("Light 8", "Console Lamp " + (k + 1), cx - 0.34f, 1.10f, h, 180f).Set("PuzzleLight", "range", 1.5f);
            display.Under(unit);
            lamp.Under(unit);
            if (k >= 6) unit.Under(fourth);
            consoleDisplay[k] = display;
            consoleLamp[k] = lamp.Component("PuzzleLight");
        }
        Piece mic = Wall("Intercom Mic", "Caller Mic", 0f, 1.18f, 0.9f, 180f);
        Piece roomProgramme = Wall("Progress Bar", "Programme Bar (Room)", 0f, 1.10f, 2.6f, 180f).Scaled(0.6f, 0.6f, 1f)
            .Set("PuzzleProgressBar", "total", Rounds).Set("PuzzleProgressBar", "style", 0);
        Piece roomPhase = Wall("Progress Bar", "Phase Bar (Room)", 0f, 1.10f, 0.5f, 180f).Scaled(0.6f, 0.6f, 1f)
            .Set("PuzzleProgressBar", "total", 5);
        Piece callerLever = Wall("Button Sim", "Caller Lever", 1.78f, 0.5f, 1.15f, -90f);
        // the finale's Validator, flush on the west wall; its own poke button is unlinked and hidden, so only the levers
        // (through Check) can ever reach Win
        Piece validator = Wall("Validator", "Finale Validator", -1.795f, 0.35f, 1.6f, 90f);
        Component validatorC = validator.Component("PuzzleValidator");
        Sdk.Set(validatorC, "pokeButton", null);
        foreach (Component poke in validator.Go.GetComponentsInChildren(Sdk.T("PuzzlePokeButton"), true))
            if (poke.gameObject != validator.Go) poke.gameObject.SetActive(false);
        Piece restart = Wall("Timer", "Restart Timer", -1.79f, -0.4f, 1.75f, 90f).Set("PuzzleTimer", "duration", RestartSeconds);
        Piece finale = Wall("Timer", "Finale Timer", -1.79f, -0.4f, 1.3f, 90f).Set("PuzzleTimer", "duration", FinaleSeconds);
        Piece shufflePiece = b.Empty("Console Shuffle", 0f, 0f, Floor, false);
        Component shuffle = b.AddCustomScript(shufflePiece, "ConsoleShuffle");

        // ---- the firing bays: East and West always, North in lobbies of four or more ----
        BayShell("East Bay", new Vector2(20f, 0f), 90f, snowy);
        BayShell("West Bay", new Vector2(-20f, 0f), -90f, snowy);
        BayShell("North Bay", new Vector2(0f, 22f), 0f, snowy).Under(fourth);

        // East: back wall's inner face x = 18.75, parts face +x; S, M, N are z -1, 0, +1
        Piece eastS = Wall("Button Hold", "East Button S", 18.755f, -1f, 1.05f, 90f);
        Piece eastM = Wall("Button Hold", "East Button M", 18.755f, 0f, 1.05f, 90f);
        Piece eastN = Wall("Button Hold", "East Button N", 18.755f, 1f, 1.05f, 90f);
        Copy("East Copy S", 18.918f, -1f, 90f, consoleDisplay[0], null);      // unit 1
        Copy("East Copy M", 18.918f, 0f, 90f, consoleDisplay[4], null);       // unit 5
        Copy("East Copy N", 18.918f, 1f, 90f, consoleDisplay[2], null);       // unit 3
        Piece eastPhase = Wall("Progress Bar", "East Phase Bar", 18.85f, 0f, 2.0f, 90f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", 5);
        Piece eastProgramme = Wall("Progress Bar", "East Programme Bar", 18.85f, 0f, 2.4f, 90f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", Rounds);
        Piece eastSpeaker = Wall("Intercom Speaker", "East Speaker", 20f, 1.55f, 2.1f, 180f);
        Lever("East Lever", 20f, -1.63f, 0f, null);
        Piece eastLamp = Wall("Light 2", "East Lamp", 20f, 0f, 2.7f, 0f);

        // West: the mirror image, parts on the back wall face -x
        Piece westS = Wall("Button Hold", "West Button S", -18.755f, -1f, 1.05f, -90f);
        Piece westM = Wall("Button Hold", "West Button M", -18.755f, 0f, 1.05f, -90f);
        Piece westN = Wall("Button Hold", "West Button N", -18.755f, 1f, 1.05f, -90f);
        Copy("West Copy S", -18.918f, -1f, -90f, consoleDisplay[1], null);    // unit 2
        Copy("West Copy M", -18.918f, 0f, -90f, consoleDisplay[5], null);     // unit 6
        Copy("West Copy N", -18.918f, 1f, -90f, consoleDisplay[3], null);     // unit 4
        Piece westPhase = Wall("Progress Bar", "West Phase Bar", -18.85f, 0f, 2.0f, -90f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", 5);
        Piece westProgramme = Wall("Progress Bar", "West Programme Bar", -18.85f, 0f, 2.4f, -90f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", Rounds);
        Piece westSpeaker = Wall("Intercom Speaker", "West Speaker", -20f, 1.55f, 2.1f, 180f);
        Lever("West Lever", -20f, -1.63f, 0f, null);
        Piece westLamp = Wall("Light 2", "West Lamp", -20f, 0f, 2.7f, 0f);

        // North (Fourth Crew): back wall's inner face z = 20.75, parts face +z; W, M, E are x -1, 0, +1
        Piece northW = Wall("Button Hold", "North Button W", -1f, 20.755f, 1.05f, 0f).Under(fourth);
        Piece northM = Wall("Button Hold", "North Button M", 0f, 20.755f, 1.05f, 0f).Under(fourth);
        Piece northE = Wall("Button Hold", "North Button E", 1f, 20.755f, 1.05f, 0f).Under(fourth);
        Copy("North Copy W", -1f, 20.918f, 0f, consoleDisplay[7], fourth);    // unit 8
        Copy("North Copy M", 0f, 20.918f, 0f, consoleDisplay[6], fourth);     // unit 7
        Copy("North Copy E", 1f, 20.918f, 0f, consoleDisplay[8], fourth);     // unit 9
        Piece northPhase = Wall("Progress Bar", "North Phase Bar", 0f, 20.85f, 2.0f, 0f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", 5).Under(fourth);
        Piece northProgramme = Wall("Progress Bar", "North Programme Bar", 0f, 20.85f, 2.4f, 0f).Scaled(0.75f, 0.75f, 1f).Set("PuzzleProgressBar", "total", Rounds).Under(fourth);
        Piece northSpeaker = Wall("Intercom Speaker", "North Speaker", -1.55f, 22f, 2.1f, 90f).Under(fourth);
        Lever("North Lever", 1.63f, 22f, -90f, fourth);
        Piece northLamp = Wall("Light 2", "North Lamp", 0f, 22f, 2.7f, 0f).Under(fourth);

        // The Caller's mic plays in every bay's speaker (the hidden North one in lobbies of two or three is never set up)
        mic.Add("PuzzleIntercom", "speakers", eastSpeaker.Component("PuzzleIntercomSpeaker"), westSpeaker.Component("PuzzleIntercomSpeaker"),
            northSpeaker.Component("PuzzleIntercomSpeaker"));
        // Caller Lever: the finale group's lever in the room (East, West and North are made by Lever())
        callerLever.Set("PuzzleSimPressSwitch", "group", "finale").Set("PuzzleSimPressSwitch", "activeSeconds", LeverActive)
            .Set("PuzzleSimPressSwitch", "timeoutSeconds", LeverTimeout);

        // ---- the programmes: Press In Order in its timed mode, one per lobby size (the hidden one never starts) ----
        Component[] button =
        {
            eastS.Component("PuzzleButton"), westS.Component("PuzzleButton"), eastN.Component("PuzzleButton"),
            westN.Component("PuzzleButton"), eastM.Component("PuzzleButton"), westM.Component("PuzzleButton"),
            northM.Component("PuzzleButton"), northW.Component("PuzzleButton"), northE.Component("PuzzleButton"),
        };
        var programmeBars = new List<Object> { roomProgramme.Component("PuzzleProgressBar"), eastProgramme.Component("PuzzleProgressBar"), westProgramme.Component("PuzzleProgressBar") };
        var phaseBars = new List<Object> { roomPhase.Component("PuzzleProgressBar"), eastPhase.Component("PuzzleProgressBar"), westPhase.Component("PuzzleProgressBar") };
        Component p3 = Programme("Programme 3", three, 6, consoleLamp, button, programmeBars, phaseBars);
        programmeBars.Add(northProgramme.Component("PuzzleProgressBar"));
        phaseBars.Add(northPhase.Component("PuzzleProgressBar"));
        Component p4 = Programme("Programme 4", fourth, 9, consoleLamp, button, programmeBars, phaseBars);

        // ---- the show's centrepiece: the star on its pole, six launchers round it ----
        var middle = new MeshBuilder();
        Kit.AddDisc(middle, new Vector3(Star.x, Floor, Star.y), LauncherRing + 1f, 0.25f, IsleKit.Stone, 32);
        Kit.AddBox(middle, new Vector3(Star.x, Floor + 0.25f, Star.y), new Vector3(0.35f, 7f, 0.35f), IsleKit.Cream);
        Kit.AddStar(middle, new Vector3(Star.x, Floor + 7.8f, Star.y), 1f, 0.15f, IsleKit.Gold);
        b.MeshPiece("Star Pole", middle.ToMesh(), snowy);
        var launchers = new Component[6];
        for (int i = 0; i < 6; i++)
        {
            float a = 2f * Mathf.PI * i / 6f;
            Piece launcher = b.Part("Firework Launcher", Star.x + Mathf.Sin(a) * LauncherRing, Star.y + Mathf.Cos(a) * LauncherRing,
                Floor + 0.25f + 0.201f, a * Mathf.Rad2Deg, false).Named("Launcher " + (i + 1));
            launcher.Set("PuzzleFireworkLauncher", "color", Colours[i]);
            launchers[i] = launcher.Component("PuzzleFireworkLauncher");
        }

        // ---- the start corner (the outer switch, the only vice, pillow and confetti) and the Spare Shells cabinet ----
        IsleKit.StartCorner(b, 0f, -9f, 0f, true, Floor, false);
        SpareShells(snowy);

        // ---- lights: fuse lines from the room's roof to each bay's roof, and a ring round the site ----
        Kit.LightString(b, "East Fuse", new List<Vector3> { new Vector3(2.0f, Floor + 3.0f, 0f), new Vector3(18.6f, Floor + 2.85f, 0f) },
            0.12f, 0.5f, false, false, 243);
        Kit.LightString(b, "West Fuse", new List<Vector3> { new Vector3(-2.0f, Floor + 3.0f, 0f), new Vector3(-18.6f, Floor + 2.85f, 0f) },
            0.12f, 0.5f, false, false, 244);
        var pole = new MeshBuilder();       // the north fuse runs round the star on a pole of its own
        Kit.AddBox(pole, new Vector3(-6.5f, Floor, 10f), new Vector3(0.14f, 3.4f, 0.14f), IsleKit.Wood);
        b.MeshPiece("North Fuse Pole", pole.ToMesh(), snowy).Under(fourth);
        Kit.LightString(b, "North Fuse", new List<Vector3> { new Vector3(0f, Floor + 3.0f, 1.4f), new Vector3(-6.5f, Floor + 3.4f, 10f), new Vector3(0f, Floor + 2.85f, 20.6f) },
            0.12f, 0.5f, false, false, 245).Under(fourth);
        Kit.LightRing(b, "Site Lights", 0f, 5f, 48f, 38f, Floor + 2.8f, false, 241);

        // ---- events ----
        Component validatorTarget = validatorC;
        Component restartC = restart.Component("PuzzleTimer"), finaleC = finale.Component("PuzzleTimer");
        Component pendantC = pendant.Component("PuzzleLight"), eastLampC = eastLamp.Component("PuzzleLight"),
            westLampC = westLamp.Component("PuzzleLight"), northLampC = northLamp.Component("PuzzleLight");
        Piece root = b.RootPiece;

        // start: move the console units, begin the programme, light the room and the bays
        root.On("PuzzleSDK", "onPuzzleStart", shuffle, "Shuffle");
        root.On("PuzzleSDK", "onPuzzleStart", p3, "Begin");
        root.On("PuzzleSDK", "onPuzzleStart", p4, "Begin");
        foreach (Component lamp in new[] { pendantC, eastLampC, westLampC, northLampC }) root.On("PuzzleSDK", "onPuzzleStart", lamp, "TurnOn");
        // stop: end the programme, reset both timers, clear the Validator, put the lamps out (the door follows its mode)
        root.On("PuzzleSDK", "onPuzzleStop", p3, "End");
        root.On("PuzzleSDK", "onPuzzleStop", p4, "End");
        root.On("PuzzleSDK", "onPuzzleStop", restartC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", finaleC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", validatorTarget, "SetCorrect", false);
        foreach (Component lamp in new[] { pendantC, eastLampC, westLampC, northLampC }) root.On("PuzzleSDK", "onPuzzleStop", lamp, "TurnOff");
        // win: rockets again, timers reset, the door opens at once
        foreach (Component launcher in launchers) root.On("PuzzleSDK", "onWin", launcher, "Fire");
        root.On("PuzzleSDK", "onWin", finaleC, "ResetTimer");
        root.On("PuzzleSDK", "onWin", restartC, "ResetTimer");
        root.On("PuzzleSDK", "onWin", door.Component("PuzzleDoor"), "Open");

        foreach (Component p in new[] { p3, p4 })
        {
            // every (re)start of the programme powers the Validator down: only a full programme turns it on
            Sdk.Wire(p, "onBegin", validatorTarget, "TurnOff");
            Sdk.Wire(p, "onBegin", validatorTarget, "SetCorrect", false);
            Sdk.Wire(p, "onBegin", finaleC, "ResetTimer");
            // a misfire: the show starts again from the top after the Restart Timer
            Sdk.Wire(p, "onFail", restartC, "StartTimer");
        }
        restart.On("PuzzleTimer", "onFinished", p3, "Begin");
        restart.On("PuzzleTimer", "onFinished", p4, "Begin");

        // the programme is full: the Validator on first (which cancels any check left from a lever sync just before),
        // set right, the finale's clock started, the rockets up
        roomProgramme.On("PuzzleProgressBar", "onFull", validatorTarget, "TurnOn");
        roomProgramme.On("PuzzleProgressBar", "onFull", validatorTarget, "SetCorrect", true);
        roomProgramme.On("PuzzleProgressBar", "onFull", finaleC, "StartTimer");
        foreach (Component launcher in launchers) roomProgramme.On("PuzzleProgressBar", "onFull", launcher, "Fire");

        // the finale: every lever in the group fires onSuccess, so only the Caller's is wired
        callerLever.On("PuzzleSimPressSwitch", "onSuccess", finaleC, "PauseTimer");
        callerLever.On("PuzzleSimPressSwitch", "onSuccess", validatorTarget, "Check");
        // too slow: the Validator off, and the show from the top
        finale.On("PuzzleTimer", "onFinished", validatorTarget, "TurnOff");
        finale.On("PuzzleTimer", "onFinished", validatorTarget, "SetCorrect", false);
        finale.On("PuzzleTimer", "onFinished", restartC, "StartTimer");
        // a backstop (with the gating above it cannot fire): end the programmes and start again
        validator.On("PuzzleValidator", "onIncorrect", p3, "End");
        validator.On("PuzzleValidator", "onIncorrect", p4, "End");
        validator.On("PuzzleValidator", "onIncorrect", finaleC, "ResetTimer");
        validator.On("PuzzleValidator", "onIncorrect", restartC, "StartTimer");
    }

    // A copy of a console display above a bay's button
    void Copy(string name, float x, float z, float yaw, Piece original, Piece parent)
    {
        Piece copy = Wall("Display", name, x, z, 1.5f, yaw).Set("PuzzleDisplay", "size", 0)
            .Set("PuzzleDisplay", "copyDisplay", original.Component("PuzzleDisplay"));
        if (parent != null) copy.Under(parent);
    }

    // A finale lever ("press together", group "finale") on a bay's side wall
    void Lever(string name, float x, float z, float yaw, Piece parent)
    {
        Piece lever = Wall("Button Sim", name, x, z, 1.15f, yaw);
        lever.Set("PuzzleSimPressSwitch", "group", "finale").Set("PuzzleSimPressSwitch", "activeSeconds", LeverActive)
            .Set("PuzzleSimPressSwitch", "timeoutSeconds", LeverTimeout);
        if (parent != null) lever.Under(parent);
    }

    // A timed Press In Order over the first 'pairs' console units and their buttons, under its lobby group
    Component Programme(string name, Piece group, int pairs, Component[] lamps, Component[] buttons, List<Object> programmeBars, List<Object> phaseBars)
    {
        Piece piece = b.Empty(name, 0f, 0f, Floor + 0.5f, false).Under(group);
        Component pio = b.AddScript(piece, "PuzzlePressInOrder");
        var l = new Object[pairs];
        var bt = new Object[pairs];
        for (int i = 0; i < pairs; i++) { l[i] = lamps[i]; bt[i] = buttons[i]; }
        Sdk.Add(pio, "lights", l);
        Sdk.Add(pio, "buttons", bt);
        Sdk.Add(pio, "progressBars", programmeBars.ToArray());
        Sdk.Add(pio, "phaseBars", phaseBars.ToArray());
        Sdk.Set(pio, "rounds", Rounds);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", Phase);
        return pio;
    }

    // "Spare Shells": a glass-fronted cabinet on legs west of the start corner, the nine scribble tiles standing on two
    // shelves with their faces out. The team names the scribbles here; the deal reads these tiles' pictures.
    void SpareShells(Material snowy)
    {
        const float X0 = -7.7f, X1 = -4.3f, Z0 = -9.25f, Z1 = -8.75f;
        var mb = new MeshBuilder();
        foreach (float lx in new[] { X0 + 0.06f, X1 - 0.06f })
            foreach (float lz in new[] { Z0 + 0.06f, Z1 - 0.06f })
                Block(mb, lx - 0.04f, lx + 0.04f, lz - 0.04f, lz + 0.04f, 0f, 0.5f, IsleKit.DarkWood);     // legs
        Block(mb, X0, X1, Z0, Z1, 0.5f, 0.6f, IsleKit.Wood);                // bottom (the lower shelf's top at 0.6)
        Block(mb, X0, X1, Z0, Z1, 1.7f, 1.75f, IsleKit.Wood);               // top (the ceiling at 1.7)
        Block(mb, X0, X1, Z0, -9.2f, 0.6f, 1.7f, IsleKit.Wood);             // back
        Block(mb, X0, -7.6f, -9.2f, Z1, 0.6f, 1.7f, IsleKit.Wood);          // ends
        Block(mb, -4.4f, X1, -9.2f, Z1, 0.6f, 1.7f, IsleKit.Wood);
        Block(mb, -7.6f, -4.4f, -9.2f, -8.8f, 1.11f, 1.15f, IsleKit.Cream); // the upper shelf
        Block(mb, -7.6f, -4.4f, -9.2f, -9.18f, 0.6f, 1.7f, IsleKit.Board);  // dark lining behind the tiles
        b.MeshPiece("Spare Shells", mb.ToMesh(), snowy);
        for (int i = 0; i < 9; i++)
        {
            bool upper = i < 5;
            float x = upper ? -7.2f + 0.6f * i : -6.9f + 0.6f * (i - 5);
            b.Part("Peg Tile", x, -8.88f, Floor + (upper ? 1.38f : 0.83f), 0f, false).Named("Shell " + (i + 1))
                .Set("PuzzlePegTile", "imageSet", 3).Set("PuzzlePegTile", "image", i + 1);      // Scribbles
        }
        b.Part("Glass", -6f, -8.79f, Floor + 1.15f, 0f, false).Named("Spare Shells Glass").Pane(3.2f, 1.1f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0);
    }
}
