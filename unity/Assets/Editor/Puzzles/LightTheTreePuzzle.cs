// Light the Tree: the Lantern Ring. A great fir stands dark in a clearing; 17 m round it stand three lantern shelters
// (two in a 2-player lobby), roofed timber booths open outward, away from the tree. Each shelter's own Hold Buttons are
// on its left wall under split-flap pictures; the lamps for those buttons hang on the back wall of the NEXT shelter
// clockwise, each above a picture, deep in its recess, so nobody ever sees their own lamps. The SDK's timed Press In
// Order lights one lamp every phase: when the phase ends its owner must be holding the button under the same picture,
// and nobody any other. The watcher calls the picture over the intercom. Twelve phases in a row (ten in Ring 2) light
// the tree and drop the gourd; a slip stops the puzzle by itself. At every start the pictures are re-dealt and the
// BoardShuffle script re-hangs each board's lamp+picture slots, so only the picture links lamp and button.
//
// Shelter-local coordinates (x', z'): origin at the centre of the back wall's inner face, Ring m from the centre; z'
// points out of the mouth, x' to the right looking out. Parts face their local +z; yaw θ faces out of the mouth,
// θ + 90 faces across from the left wall, θ - 90 across from the right wall.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class LightTheTreePuzzle : PuzzleDef
{
    public override string Name => "LightTheTree";
    public override Vector2 Footprint => new Vector2(48f, 48f);
    // A terraced clearing in the woods between the ski runs, south-east (layout-final, 2026-11-22)
    public override Vector2? FixedSite => new Vector2(-1257f, -1746f);
    public override float Yaw => 0f;

    const float Floor = 0.25f;          // top of the pad above the root's level; every part stands on it
    const float Ring = 17f;             // centre to a shelter's back-wall inner face
    const float PadRadius = 23f;
    const float Phase2 = 5f;            // seconds per phase (playtest may move these to 6)
    const float Phase3 = 5f;
    const int Rounds2 = 10;
    const int Rounds3 = 12;
    const float CountdownSeconds = 20f;
    const float LampRange = 0.25f;      // board lamps: no light reaches any outside face (nearest is 0.35 m away)
    const float ShelterLampRange = 3f;
    const float ShelterLampIntensity = 0.6f;    // low, so the booth is lit, not floodlit (tune in the game)
    const float TreeHeight = 9f;
    const float PlinthHeight = 0.45f;
    const int TreeSeed = 303;

    static readonly Color BoardLampColor = new Color(1f, 0.86f, 0.6f);
    static readonly string[] Letters = { "A", "B", "C" };

    PuzzleBuilder b;
    Material snowy;

    // Everything the root's events reach, gathered from both lobby groups
    readonly List<Component> shelterTimers = new List<Component>();
    readonly List<Component> shelterLamps = new List<Component>();
    readonly List<Component> pios = new List<Component>();

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        snowy = IsleKit.Snowy(b);
        b.ClearGround(48f, 48f);
        b.DebugTeleport(0f, -10f, 1.8f);
        // No jetpack thrust over the whole clearing (structures, standing room, well above the roofs)
        IsleKit.CalmAir(b, "Light The Tree", 0f, 0f, new Vector3(64f, 130f, 64f), 0f, -10f, false);

        // ---- the pad: one level disc for everything, sunk well into the ground ----
        var pad = new MeshBuilder();
        Kit.AddDisc(pad, new Vector3(0f, Floor - 1.75f, 0f), PadRadius, 1.75f, IsleKit.Stone, 48);
        b.MeshPiece("Pad", pad.ToMesh(), snowy);

        // ---- the great tree on its stone bed ----
        var plinth = new MeshBuilder();
        Kit.AddDisc(plinth, new Vector3(0f, Floor - 0.1f, 0f), 3.2f, PlinthHeight + 0.1f, IsleKit.Stone, 32);
        b.MeshPiece("Tree Plinth", plinth.ToMesh(), snowy);
        float treeFoot = Floor + PlinthHeight;
        var tree = new MeshBuilder();
        Kit.AddPine(tree, new Vector3(0f, treeFoot, 0f), TreeHeight, TreeSeed);
        Piece pine = b.MeshPiece("Great Tree", tree.ToMesh(), snowy, false);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = new Vector3(0f, treeFoot + 1.6f, 0f);
        trunk.radius = 0.9f;
        trunk.height = 3.2f;

        // The tree's lights: dark until the win
        var treeLamps = new List<Component>();
        Color[] treeColors =
        {
            new Color(1f, 0.15f, 0.1f), new Color(0.2f, 1f, 0.3f), new Color(1f, 0.75f, 0.2f), new Color(0.25f, 0.45f, 1f),
            new Color(1f, 0.15f, 0.1f), new Color(0.2f, 1f, 0.3f), new Color(1f, 0.75f, 0.2f),
        };
        for (int i = 0; i < 7; i++)
        {
            float h = 1.8f + 0.8f * i;
            float angle = i * 137.5f + 20f;
            float r = TreeSurface(h - PlinthHeight) * 1.15f + 0.12f;
            float a = angle * Mathf.Deg2Rad;
            Piece lamp = b.Part("Light 6", Mathf.Sin(a) * r, Mathf.Cos(a) * r, Floor + h, angle, false).Named("Tree Lamp " + (i + 1))
                .Set("PuzzleLight", "color", treeColors[i]);
            treeLamps.Add(lamp.Component("PuzzleLight"));
        }
        Piece star = b.Part("Light 6", 0f, 0f, treeFoot + TreeHeight * 1.03f + 0.3f, 180f, false).Named("Tree Star")
            .Set("PuzzleLight", "color", new Color(1f, 0.8f, 0.25f));
        treeLamps.Add(star.Component("PuzzleLight"));

        // ---- the Ornament Case: a sealed legend of the eight pictures, two rows of four, at the tree's foot ----
        BuildCase();

        // ---- the Count Board: the rounds spiral and the countdown ----
        CountBoard("Count Board", 0f, -7f, 180f, 3.0f, 0.9f, 2.8f, IsleKit.Board, Floor);
        Piece mainBar = b.Part("Progress Bar", -0.75f, -7.16f, Floor + 2.0f, 180f, false).Named("Rounds Bar Main")
            .Set("PuzzleProgressBar", "style", 1).Set("PuzzleProgressBar", "total", Rounds3).Scaled(0.4f, 0.4f, 1f);
        Piece countdown = b.Part("Timer", 0.95f, -7.13f, Floor + 2.0f, 180f, false).Named("Countdown")
            .Set("PuzzleTimer", "duration", CountdownSeconds);
        Component mainBarC = mainBar.Component("PuzzleProgressBar");
        Component countdownC = countdown.Component("PuzzleTimer");

        IsleKit.StartCorner(b, 4.5f, -7f, 180f, true, Floor, false);

        // ---- the board shuffle (host script, re-hangs the slots at every start) ----
        Piece shuffle = b.Empty("Board Shuffle", 0f, 0f, Floor, false);
        Component shuffleC = b.AddCustomScript(shuffle, "BoardShuffle");

        // ---- the two lobby rings: each a whole puzzle with its own Press In Order ----
        Piece ring2 = IsleKit.Lobby(b, "Ring 2", true, false, false);
        BuildRing(ring2, "2", new[] { 0f, 180f }, new[] { -0.8f, 0f, 0.8f }, new[] { 0.6f, 1.3f, 2.0f }, 1.3f, Rounds2, Phase2, mainBarC);
        Piece ring3 = IsleKit.Lobby(b, "Ring 3", false, true, true);
        BuildRing(ring3, "3", new[] { 0f, 120f, 240f }, new[] { -0.6f, 0.6f }, new[] { 0.8f, 1.7f }, 1.25f, Rounds3, Phase3, mainBarC);

        // ---- events ----
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", shuffleC, "Shuffle");
        root.On("PuzzleSDK", "onPuzzleStart", countdownC, "StartTimer");
        root.On("PuzzleSDK", "onPuzzleStop", countdownC, "ResetTimer");
        foreach (Component t in shelterTimers)
        {
            root.On("PuzzleSDK", "onPuzzleStart", t, "StartTimer");
            root.On("PuzzleSDK", "onPuzzleStop", t, "ResetTimer");
        }
        foreach (Component l in shelterLamps)
        {
            root.On("PuzzleSDK", "onPuzzleStart", l, "TurnOn");
            root.On("PuzzleSDK", "onPuzzleStop", l, "TurnOff");
        }
        foreach (Component pio in pios)
        {
            root.On("PuzzleSDK", "onPuzzleStop", pio, "End");
            Sdk.Wire(countdownC, "onFinished", pio, "Begin");
            Sdk.Wire(pio, "onFail", b.Puzzle, "StopPuzzle");
        }
        mainBar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");
        foreach (Component l in treeLamps) root.On("PuzzleSDK", "onWin", l, "TurnOn");
    }

    // ---------------------------------------------------------------------------------------------------------------

    // A shelter-local point (x', z') round the ring at angle theta, as root-local (x, z)
    static Vector2 At(float xs, float zs, float theta) => IsleKit.Turn(xs, Ring + zs, theta);

    // An SDK part at shelter-local (x', z'), 'h' above the pad, facing 'yaw'
    Piece PartAt(string prefab, float theta, float xs, float zs, float h, float yaw)
    {
        Vector2 p = At(xs, zs, theta);
        return b.Part(prefab, p.x, p.y, Floor + h, yaw, false);
    }

    // A block in shelter-local bounds, into a mesh in the root's local space
    static void ShelterBox(MeshBuilder mb, float theta, float x0, float x1, float z0, float z1, float h0, float h1, Color c)
    {
        Vector2 p = At((x0 + x1) / 2f, (z0 + z1) / 2f, theta);
        Kit.AddBox(mb, new Vector3(p.x, Floor + h0, p.y), new Vector3(x1 - x0, h1 - h0, z1 - z0), c, theta);
    }

    // How far the pine's boughs reach at height y above its foot (the nominal cone of Kit.AddPine)
    static float TreeSurface(float y)
    {
        float best = 0f;
        for (int k = 0; k < 4; k++)
        {
            float baseY = TreeHeight * (0.16f + 0.19f * k);
            float tierH = TreeHeight * (0.36f - 0.02f * k);
            float radius = TreeHeight * 0.27f * (1f - 0.21f * k);
            float r;
            if (y < baseY) r = k == 0 ? radius : 0f;            // under the lowest skirt: its rim
            else if (y > baseY + tierH) r = 0f;
            else r = radius * (1f - (y - baseY) / tierH);
            if (r > best) best = r;
        }
        return Mathf.Max(best, 0.6f);
    }

    // A board on two posts like IsleKit.BoardOnPosts, standing 'baseHeight' above the root's level (on the pad)
    Piece CountBoard(string name, float cx, float cz, float yaw, float width, float bottom, float top, Color face, float baseHeight)
    {
        var mb = new MeshBuilder();
        Vector2 left = IsleKit.Turn(-width / 2f - 0.12f, 0f, yaw), right = IsleKit.Turn(width / 2f + 0.12f, 0f, yaw);
        Kit.AddBox(mb, new Vector3(left.x, 0f, left.y), new Vector3(0.2f, top + 0.3f, 0.2f), IsleKit.Wood, yaw);
        Kit.AddBox(mb, new Vector3(right.x, 0f, right.y), new Vector3(0.2f, top + 0.3f, 0.2f), IsleKit.Wood, yaw);
        Kit.AddBox(mb, new Vector3(0f, bottom, 0f), new Vector3(width, top - bottom, 0.12f), face, yaw);
        Vector2 roof = IsleKit.Turn(0f, 0.05f, yaw);
        Kit.AddBox(mb, new Vector3(roof.x, top + 0.3f, roof.y), new Vector3(width + 0.7f, 0.12f, 0.6f), IsleKit.DarkWood, yaw);
        Piece piece = b.MeshPiece(name, mb.ToMesh(), snowy);
        piece.Transform.position = b.WorldPoint(cx, cz, baseHeight, false);
        return piece;
    }

    // The sealed Ornament Case at (0, -4.2): floor box to 0.70, a shelf to 1.20, back, sides, a lid from 1.70; glass front
    void BuildCase()
    {
        const float cz = -4.2f;
        var mb = new MeshBuilder();
        Kit.AddBox(mb, new Vector3(0f, Floor - 0.1f, cz), new Vector3(2.5f, 0.80f, 0.7f), IsleKit.Wood);              // floor box, top 0.70
        Kit.AddBox(mb, new Vector3(0f, Floor + 0.70f, -3.90f), new Vector3(2.5f, 1.0f, 0.1f), IsleKit.Wood);          // back
        Kit.AddBox(mb, new Vector3(-1.2f, Floor + 0.70f, -4.25f), new Vector3(0.1f, 1.0f, 0.6f), IsleKit.Wood);       // sides
        Kit.AddBox(mb, new Vector3(1.2f, Floor + 0.70f, -4.25f), new Vector3(0.1f, 1.0f, 0.6f), IsleKit.Wood);
        Kit.AddBox(mb, new Vector3(0f, Floor + 1.70f, cz), new Vector3(2.6f, 0.12f, 0.8f), IsleKit.DarkWood);         // lid
        Kit.AddBox(mb, new Vector3(0f, Floor + 1.17f, -4.125f), new Vector3(2.3f, 0.03f, 0.35f), IsleKit.Wood);       // shelf, top 1.20
        b.MeshPiece("Ornament Case", mb.ToMesh(), snowy);

        // Glass front: covers h 0.68-1.72 and x ±1.16, overlapping the floor box, lid and sides (no gaps)
        b.Part("Glass", 0f, -4.52f, Floor + 1.20f, 180f, false).Named("Case Glass")
            .Pane(2.32f, 1.04f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0);

        for (int i = 0; i < 8; i++)
        {
            int row = i / 4, col = i % 4;
            float h = row == 0 ? 0.94f : 1.44f;     // 0.24 above the case floor (0.70) / the shelf (1.20)
            Piece tile = b.Part("Peg Tile", -0.75f + 0.5f * col, cz, Floor + h, 180f, false).Named("Tile " + (i + 1));
            Component c = tile.Component("PuzzlePegTile");
            Sdk.SetFlags(c, "imageSet", 6);         // Scribbles: an enum with explicit values, so set by value, not index
            Sdk.Set(c, "image", i + 1);
        }
    }

    // One lobby ring under 'group': the shelters at 'angles' (clockwise), each with slotX.Length buttons; shelter X's back
    // wall carries the lamps and pictures of next(X), and its left wall X's own buttons under copies of those pictures
    void BuildRing(Piece group, string tag, float[] angles, float[] slotX, float[] buttonZ, float holdBarZ, int rounds, float phase, Component mainBar)
    {
        int n = angles.Length, slots = slotX.Length;
        var displays = new Component[n, slots];     // Display Xk: the picture over Lamp Xk, on prev(X)'s back wall
        var lamps = new Component[n, slots];
        var speakers = new Component[n];
        var mics = new Component[n];
        var roundBars = new List<Component> { mainBar };
        var phaseBars = new List<Component>();
        var structure = new MeshBuilder();

        // Pass 1: the shelters, their boards (next station's lamps) and their own fittings
        for (int s = 0; s < n; s++)
        {
            float theta = angles[s];
            string x = Letters[s];
            int y = (s + 1) % n;
            string yName = Letters[y];

            // the booth: walls 3.0 high on three sides, a flat roof resting on them with 0.4 m over the mouth
            ShelterBox(structure, theta, -1.6f, 1.6f, -0.2f, -0.05f, -0.1f, 3.0f, IsleKit.Wood);       // back wall, outer layer
            ShelterBox(structure, theta, -1.4f, 1.4f, -0.05f, 0f, -0.1f, 3.0f, IsleKit.Board);         // back wall, board face
            ShelterBox(structure, theta, -1.6f, -1.4f, -0.2f, 2.6f, -0.1f, 3.0f, IsleKit.Wood);        // left wall
            ShelterBox(structure, theta, 1.4f, 1.6f, -0.2f, 2.6f, -0.1f, 3.0f, IsleKit.Wood);          // right wall
            ShelterBox(structure, theta, -1.6f, 1.6f, -0.2f, 3.0f, 3.0f, 3.15f, IsleKit.DarkWood);     // roof

            // the board: next(X)'s lamps over their pictures, each pair in a slot BoardShuffle re-hangs
            for (int k = 0; k < slots; k++)
            {
                Vector2 sp = At(slotX[k], 0f, theta);
                Piece slot = b.Empty("Slot " + tag + yName + "-" + (k + 1), sp.x, sp.y, Floor, false).Under(group);
                slot.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, theta, 0f);
                Piece display = PartAt("Display", theta, slotX[k], 0.17f, 1.45f, theta).Named("Display " + yName + (k + 1))
                    .Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", true).Under(slot);
                Piece lamp = PartAt("Light 6", theta, slotX[k], 0.15f, 2.05f, theta).Named("Lamp " + yName + (k + 1))
                    .Set("PuzzleLight", "color", BoardLampColor).Set("PuzzleLight", "range", LampRange).Under(slot);
                displays[y, k] = display.Component("PuzzleDisplay");
                lamps[y, k] = lamp.Component("PuzzleLight");
            }

            // back wall: the phase bar over the board
            Piece phaseBar = PartAt("Progress Bar", theta, 0f, 0.10f, 2.55f, theta).Named("Phase Bar " + x)
                .Set("PuzzleProgressBar", "style", 0).Scaled(0.75f, 0.75f, 1f).Under(group);
            phaseBars.Add(phaseBar.Component("PuzzleProgressBar"));

            // left wall: the holder's bar and the speaker
            Piece holdBar = PartAt("Progress Bar", theta, -1.30f, holdBarZ, 2.10f, theta + 90f).Named("Hold Bar " + x)
                .Set("PuzzleProgressBar", "style", 0).Scaled(0.45f, 0.45f, 1f).Under(group);
            phaseBars.Add(holdBar.Component("PuzzleProgressBar"));
            Piece speaker = PartAt("Intercom Speaker", theta, -1.30f, 1.25f, 2.35f, theta + 90f).Named("Speaker " + x).Under(group);
            speakers[s] = speaker.Component("PuzzleIntercomSpeaker");

            // right wall: rounds bar, countdown, intercom mic, quit knob and live lamp
            Piece roundsBar = PartAt("Progress Bar", theta, 1.30f, 1.3f, 2.40f, theta - 90f).Named("Rounds Bar " + x)
                .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", rounds).Scaled(0.6f, 0.6f, 1f).Under(group);
            roundBars.Add(roundsBar.Component("PuzzleProgressBar"));
            Piece timer = PartAt("Timer", theta, 1.32f, 1.3f, 1.75f, theta - 90f).Named("Countdown " + x)
                .Set("PuzzleTimer", "duration", CountdownSeconds).Scaled(0.5f, 0.5f, 1f).Under(group);
            shelterTimers.Add(timer.Component("PuzzleTimer"));
            Piece mic = PartAt("Intercom Mic", theta, 1.38f, 0.5f, 1.25f, theta - 90f).Named("Intercom " + x).Under(group);
            mics[s] = mic.Component("PuzzleIntercom");
            PartAt("Switch Start", theta, 1.327f, 2.1f, 1.25f, theta - 90f).Named("Quit Knob " + x).Under(group);
            PartAt("Live Light", theta, 1.394f, 2.1f, 1.42f, theta - 90f).Named("Live " + x).Under(group);

            // a low lamp under the roof while the puzzle runs
            Piece shelterLamp = PartAt("Light 2", theta, 0f, 1.3f, 2.85f, theta).Named("Shelter Lamp " + x)
                .Set("PuzzleLight", "range", ShelterLampRange).Set("PuzzleLight", "intensity", ShelterLampIntensity).Under(group);
            shelterLamps.Add(shelterLamp.Component("PuzzleLight"));

            // Christmas lights along the roof's front edge
            Vector2 l0 = At(-1.6f, 3.0f, theta), l1 = At(1.6f, 3.0f, theta);
            Kit.LightString(b, "Shelter Lights " + tag + x,
                new List<Vector3> { new Vector3(l0.x, Floor + 2.98f, l0.y), new Vector3(l1.x, Floor + 2.98f, l1.y) },
                0.12f, 0.3f, false, false, 40 + s + 10 * n).Under(group);

            // light-string arc on the pad's rim to the next shelter, 15 degrees clear of both shelters' axes
            float a0 = theta + 15f, a1 = theta + 360f / n - 15f;
            int steps = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / 10f));
            var arc = new List<Vector3>();
            for (int j = 0; j <= steps; j++)
            {
                float a = (a0 + (a1 - a0) * j / steps) * Mathf.Deg2Rad;
                float px = Mathf.Sin(a) * 22f, pz = Mathf.Cos(a) * 22f;
                arc.Add(new Vector3(px, Floor + 2.7f, pz));
                Kit.AddBox(structure, new Vector3(px, Floor - 0.1f, pz), new Vector3(0.1f, 2.88f, 0.1f), IsleKit.DarkWood);
            }
            Kit.LightString(b, "Site Lights " + tag + x, arc, 0.4f, 0.5f, false, false, 60 + s + 10 * n).Under(group);
        }

        b.MeshPiece("Ring " + tag + " Shelters", structure.ToMesh(), snowy).Under(group);

        // Pass 2: each shelter's own buttons on its left wall, under copies of their pictures; the intercoms
        var buttons = new Component[n, slots];
        for (int s = 0; s < n; s++)
        {
            float theta = angles[s];
            string x = Letters[s];
            for (int k = 0; k < slots; k++)
            {
                Piece button = PartAt("Button Hold", theta, -1.4f, buttonZ[k], 1.05f, theta + 90f).Named("Button " + x + (k + 1)).Under(group);
                buttons[s, k] = button.Component("PuzzleButton");
                PartAt("Display", theta, -1.23f, buttonZ[k], 1.70f, theta + 90f).Named("Copy " + x + (k + 1))
                    .Set("PuzzleDisplay", "copyDisplay", displays[s, k]).Under(group);
            }
            for (int o = 0; o < n; o++) if (o != s) Sdk.Add(mics[s], "speakers", speakers[o]);
        }

        // The ring's Press In Order: Lamp Xk pairs with Button Xk (the lamp hangs on prev(X)'s board)
        Piece order = b.Empty("Press In Order " + tag, 0f, 0f, Floor + 0.5f, false).Under(group);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        for (int s = 0; s < n; s++)
            for (int k = 0; k < slots; k++)
            {
                Sdk.Add(pio, "lights", lamps[s, k]);
                Sdk.Add(pio, "buttons", buttons[s, k]);
            }
        Sdk.Add(pio, "progressBars", roundBars.ToArray());
        Sdk.Add(pio, "phaseBars", phaseBars.ToArray());
        Sdk.Set(pio, "rounds", rounds);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", phase);
        pios.Add(pio);
    }
}
