// Hold the Star: a one-way call-and-hold puzzle round a great fir with a gold star on top.
//
// One player, the Reader, passes empty-handed through a two-way No Items Gate in a roofed vestibule into the Star
// Chamber at the tree's foot (soundproof, windowless, radio-dead) and starts the puzzle there. The door shuts. On the
// chamber's back wall is a lamp map: a cluster of red, green and gold lamps per holder station, framed in that
// station's colour (West blue, East purple, North white). Every five seconds a lamp lights (Press In Order in timed
// mode): when its phase ends, the holder at that station must be holding the button of that colour and nobody any
// other. The Reader can only call out over a hold-to-talk intercom; the holders 22 m out cannot answer. Ten clean
// rounds in a row (8 with two players, 12 with four) open the chamber; then everyone, the Reader on the porch, holds
// their star button together for six seconds, the gauge fills, Win() fires the confetti and the glass box opens.
//
// Lobby sizes: two players have the West station only (its lamps on a centred map panel), three West and East, four
// add North. No custom code: Press In Order, hold and N-hold buttons, an intercom, a soundproof door, a No Items Gate,
// a radio dead zone, timers and progress bars. Spec: puzzle-specs\HoldTheStar.md.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class HoldTheStarPuzzle : PuzzleDef
{
    public override string Name => "HoldTheStar";
    public override Vector2 Footprint => new Vector2(52f, 50f);
    public override Vector2? FixedSite => new Vector2(-3003f, -1548f);      // layout-final: a coastal flat at the west end, by Cirque Gate and M13

    const float StationOut = 22f, PhaseSeconds = 5f;

    // Station colours, as vertex colours (read as linear, so .linear makes them look as written)
    static readonly Color Blue = new Color(0.231f, 0.435f, 0.847f).linear;       // #3B6FD8
    static readonly Color Purple = new Color(0.541f, 0.310f, 0.784f).linear;     // #8A4FC8
    static readonly Color White = new Color(0.949f, 0.949f, 0.933f).linear;      // #F2F2EE
    static readonly Color MapPanel = new Color(0.08f, 0.08f, 0.1f);

    PuzzleBuilder b;
    float slab;      // the chamber's floor top, above the root's level

    // One holder station's parts that the wiring needs
    sealed class Station
    {
        public Component Red, Green, Gold;          // PuzzleButton
        public Component Time, Count;               // PuzzleProgressBar
        public Component Speaker;                   // PuzzleIntercomSpeaker
    }

    // ---- small mesh helpers ----

    // A flat disc facing along 'yaw' (its front looks along +z turned by yaw), one-sided
    static void AddFacingDisc(MeshBuilder mb, Vector3 centre, float radius, float yaw, Color color, int segments = 14)
    {
        Vector3 right = -(Quaternion.Euler(0f, yaw, 0f) * Vector3.right);      // screen right for someone looking at its front
        for (int s = 0; s < segments; s++)
        {
            float a0 = 2f * Mathf.PI * s / segments, a1 = 2f * Mathf.PI * (s - 1) / segments;
            Vector3 p0 = centre + (right * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * radius;
            Vector3 p1 = centre + (right * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * radius;
            mb.Tri(centre, p0, p1, color);      // clockwise as seen from the front
        }
    }

    // Kit.AddStar turned by a yaw (the tree-topper's shape, the same triangle order)
    static void AddStarYaw(MeshBuilder mb, Vector3 centre, float radius, float thickness, Color color, float yaw)
    {
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        var rim = new Vector3[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / 10f, r = i % 2 == 0 ? radius : radius * 0.42f;
            rim[i] = centre + turn * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
        }
        Vector3 front = centre + turn * (Vector3.back * thickness), back = centre + turn * (Vector3.forward * thickness);
        for (int i = 0; i < 10; i++)
        {
            Vector3 a = rim[i], c = rim[(i + 1) % 10];
            mb.Tri(front, a, c, color);
            mb.Tri(back, c, a, color * 0.85f);
        }
    }

    // A box given by its two corners (root-local metres, y from the root's level)
    static void AddSpan(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A light string whose posts go into a lobby group with it (Kit.LightString makes them as separate boxes)
    void GroupedLightString(string name, List<Vector3> posts, Color bulb, int seed, Piece group)
    {
        Piece wire = Kit.LightString(b, name, posts, 0.4f, 0.5f, true, true, seed, new[] { bulb });
        if (group == null) return;
        wire.Under(group);
        for (int i = 1; i <= posts.Count; i++)
        {
            Transform post = b.Root.transform.Find(name + " Post " + i);
            if (post != null) post.SetParent(group.Transform, true);
        }
    }

    // ---- a holder station ----
    // Station-local: x across the board face, z out of it (away from the tree); the holder stands outside facing the
    // tree, so local +x is on their left. Red, green, gold run left to right as the holder sees them.
    Station BuildStation(string st, float cx, float cz, float yaw, Color colour, Piece group, bool frame)
    {
        var s = new Station();
        Vector2 P(float x, float z) { Vector2 t = IsleKit.Turn(x, z, yaw); return new Vector2(cx + t.x, cz + t.y); }
        Vector3 L(float x, float h, float z) { Vector2 t = IsleKit.Turn(x, z, yaw); return new Vector3(t.x, h, t.y); }      // mesh offsets about the centre
        var parts = new List<Piece>();

        parts.Add(IsleKit.BoardOnPosts(b, st + " Board", cx, cz, yaw, 3.0f, 0.8f, 2.6f, colour));
        if (frame)
        {
            // a dark wood frame behind the white face, so the board reads against the snow
            var fm = new MeshBuilder();
            Kit.AddBox(fm, L(0f, 0.7f, -0.08f), new Vector3(3.2f, 2.0f, 0.1f), IsleKit.DarkWood, yaw);
            Piece fp = b.MeshPiece(st + " Board Frame", fm.ToMesh(), IsleKit.Snowy(b));
            fp.Transform.position = b.WorldPoint(cx, cz);
            parts.Add(fp);
        }

        // the three hold buttons, a bauble of each colour above them
        Vector2 pr = P(0.8f, 0.13f), pg = P(0f, 0.13f), py = P(-0.8f, 0.13f);
        Piece red = b.Part("Button Hold", pr.x, pr.y, 1.15f, yaw).Named(st + " Red");
        Piece green = b.Part("Button Hold", pg.x, pg.y, 1.15f, yaw).Named(st + " Green");
        Piece gold = b.Part("Button Hold", py.x, py.y, 1.15f, yaw).Named(st + " Gold");
        parts.Add(red); parts.Add(green); parts.Add(gold);
        s.Red = red.Component("PuzzleButton"); s.Green = green.Component("PuzzleButton"); s.Gold = gold.Component("PuzzleButton");
        var baubles = new MeshBuilder();
        AddFacingDisc(baubles, L(0.8f, 1.45f, 0.08f), 0.14f, yaw, IsleKit.Red);
        AddFacingDisc(baubles, L(0f, 1.45f, 0.08f), 0.14f, yaw, IsleKit.Green);
        AddFacingDisc(baubles, L(-0.8f, 1.45f, 0.08f), 0.14f, yaw, IsleKit.Gold);
        Piece bp = b.MeshPiece(st + " Baubles", baubles.ToMesh(), IsleKit.Bare(b), false);
        bp.Transform.position = b.WorldPoint(cx, cz);
        parts.Add(bp);

        // the phase bar and the count bar
        Vector2 pb = P(0f, 0.17f);
        Piece time = b.Part("Progress Bar", pb.x, pb.y, 1.86f, yaw).Named(st + " Time").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "total", 10);
        Piece count = b.Part("Progress Bar", pb.x, pb.y, 2.33f, yaw).Named(st + " Count").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", 10);
        parts.Add(time); parts.Add(count);
        s.Time = time.Component("PuzzleProgressBar"); s.Count = count.Component("PuzzleProgressBar");

        // the speaker on its shelf post (on the holder's right), the star post with its flag (on their left)
        var posts = new MeshBuilder();
        Kit.AddBox(posts, L(-2.0f, 0f, 0f), new Vector3(0.2f, 1.2f, 0.2f), IsleKit.Wood, yaw);
        Kit.AddBox(posts, L(-2.0f, 1.2f, 0f), new Vector3(0.4f, 0.06f, 0.35f), IsleKit.DarkWood, yaw);
        Kit.AddBox(posts, L(2.6f, 0f, 0f), new Vector3(0.3f, 1.7f, 0.24f), IsleKit.Wood, yaw);
        Kit.AddBox(posts, L(3.1f, 0f, 0f), new Vector3(0.06f, 2.4f, 0.06f), IsleKit.DarkWood, yaw);      // flagpole
        Kit.AddBox(posts, L(3.58f, 1.75f, 0f), new Vector3(0.9f, 0.6f, 0.03f), colour, yaw);              // flag
        Piece pp = b.MeshPiece(st + " Posts", posts.ToMesh(), IsleKit.Snowy(b));
        pp.Transform.position = b.WorldPoint(cx, cz);
        parts.Add(pp);
        var star = new MeshBuilder();
        AddStarYaw(star, L(2.6f, 2.0f, 0f), 0.28f, 0.06f, IsleKit.Gold, yaw);
        Piece sp = b.MeshPiece(st + " Star Cap", star.ToMesh(), IsleKit.Bare(b), false);
        sp.Transform.position = b.WorldPoint(cx, cz);
        parts.Add(sp);

        Vector2 pk = P(-2.0f, 0f);
        Piece speaker = b.Part("Intercom Speaker", pk.x, pk.y, 1.26f, yaw).Named(st + " Speaker");
        parts.Add(speaker);
        s.Speaker = speaker.Component("PuzzleIntercomSpeaker");

        Vector2 ps = P(2.6f, 0.13f);
        Piece starButton = b.Part("Button N Hold", ps.x, ps.y, 1.15f, yaw).Named(st + " Star Button")
            .Set("PuzzleButtonNHold", "group", "star");
        parts.Add(starButton);

        if (group != null) foreach (Piece p in parts) p.Under(group);
        return s;
    }

    // ---- one cluster of the chamber's lamp map: a dark panel on the north wall, a frame in the station's colour,
    // a coloured rim behind each lamp, and the three lamps (range 0.25 m: their light ends inside the wall) ----
    Component[] MapCluster(string tag, string meshName, float x, Color frameColour, Piece group)
    {
        const float zWall = -5.2f, lampZ = -5.31f, h = 1.6f;
        float y0 = slab + 1.35f, y1 = slab + 1.85f;
        var mb = new MeshBuilder();
        AddSpan(mb, x - 0.45f, x + 0.45f, y0, y1, zWall - 0.005f, zWall, MapPanel);
        AddSpan(mb, x - 0.51f, x + 0.51f, y1, y1 + 0.06f, zWall - 0.01f, zWall, frameColour);      // top
        AddSpan(mb, x - 0.51f, x + 0.51f, y0 - 0.06f, y0, zWall - 0.01f, zWall, frameColour);      // bottom
        AddSpan(mb, x - 0.51f, x - 0.45f, y0, y1, zWall - 0.01f, zWall, frameColour);              // left
        AddSpan(mb, x + 0.45f, x + 0.51f, y0, y1, zWall - 0.01f, zWall, frameColour);              // right
        Color[] colours = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };
        string[] names = { "Red", "Green", "Gold" };
        var lamps = new Component[3];
        for (int i = 0; i < 3; i++)
        {
            float lx = x - 0.3f + 0.3f * i;
            AddFacingDisc(mb, new Vector3(lx, slab + h, zWall - 0.01f), 0.13f, 180f, colours[i]);
            Piece lamp = b.Part("Light 8", lx, lampZ, slab + h, 180f, false).Named("Lamp " + tag + " " + names[i])
                .Set("PuzzleLight", "color", colours[i]).Set("PuzzleLight", "range", 0.25f);
            lamp.Under(group);
            lamps[i] = lamp.Component("PuzzleLight");
        }
        Piece panel = b.MeshPiece(meshName, mb.ToMesh(), IsleKit.Bare(b), false);
        panel.Under(group);
        return lamps;
    }

    // One Press In Order for one lobby size
    Component Order(string name, Piece group, int rounds, Component[] lamps, Station[] stations, Component chamberTime, Component chamberCount)
    {
        Piece holder = b.Empty(name, 0f, -7.1f, slab, false).Under(group);
        Component c = b.AddScript(holder, "PuzzlePressInOrder");
        Sdk.Add(c, "lights", lamps);
        foreach (Station s in stations) Sdk.Add(c, "buttons", s.Red, s.Green, s.Gold);      // pairs by index: same order as the lamps
        Sdk.Add(c, "progressBars", chamberCount);
        foreach (Station s in stations) Sdk.Add(c, "progressBars", s.Count);
        Sdk.Add(c, "phaseBars", chamberTime);
        foreach (Station s in stations) Sdk.Add(c, "phaseBars", s.Time);
        Sdk.Set(c, "rounds", rounds);
        Sdk.Set(c, "holdAtEnd", true);
        Sdk.Set(c, "phaseSeconds", PhaseSeconds);
        return c;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(52f, 50f, 0f, 0f);
        b.DebugTeleport(0f, -12f);
        slab = b.WorldPoint(0f, -7.1f).y - b.Root.transform.position.y + 0.15f;
        // no jetpacks over the puzzle (Winter Walk): structures, stations and the whole play space
        IsleKit.CalmAir(b, "Hold The Star", 0f, 0f, new Vector3(64f, 120f, 62f), 0f, -10f);

        // ---- lobby groups ----
        Piece eastGroup = IsleKit.Lobby(b, "East Station", false, true, true);
        Piece northGroup = IsleKit.Lobby(b, "North Station", false, false, true);
        Piece westSide = IsleKit.Lobby(b, "West Map Side", false, true, true);
        Piece westCentre = IsleKit.Lobby(b, "West Map Centre", true, false, false);
        Piece order2Group = IsleKit.Lobby(b, "Order 2 Lobby", true, false, false);
        Piece order3Group = IsleKit.Lobby(b, "Order 3 Lobby", false, true, false);
        Piece order4Group = IsleKit.Lobby(b, "Order 4 Lobby", false, false, true);

        // ---- the tree and its star (kept) ----
        var tree = new MeshBuilder();
        Kit.AddFir(tree, Vector3.zero, 10f, 4242, 1.2f);
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(true), IsleKit.Fir(b), false);
        pine.Transform.position = b.WorldPoint(0f, 0f);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * 2f; trunk.radius = 0.6f; trunk.height = 4f;
        var star = new MeshBuilder();
        Kit.AddStar(star, new Vector3(0f, 10.6f, 0f), 0.75f, 0.12f, IsleKit.Gold);
        b.MeshPiece("Star", star.ToMesh(), IsleKit.Bare(b), false).Transform.position = b.WorldPoint(0f, 0f);
        Piece starLamp = b.Part("Light 6", 0f, -1.4f, 9.4f, 180f).Named("Star Lamp");
        starLamp.Set("PuzzleLight", "color", IsleKit.Gold);
        Component starLampC = starLamp.Component("PuzzleLight");

        // ---- the holder stations ----
        Station west = BuildStation("West", -StationOut, 0f, -90f, Blue, null, false);
        Station east = BuildStation("East", StationOut, 0f, 90f, Purple, eastGroup, false);
        Station north = BuildStation("North", 0f, 20f, 0f, White, northGroup, true);

        // ---- the Star Chamber: floor slab (and the vestibule's floor at the same level) ----
        float y0 = slab - 0.3f;
        var floor = new MeshBuilder();
        AddSpan(floor, -2.0f, 2.0f, y0, slab, -9.2f, -5.0f, IsleKit.Stone);
        AddSpan(floor, -1.28f, 1.28f, y0, slab, -10.9f, -9.2f, IsleKit.Stone);
        b.MeshPiece("Chamber Floor", floor.ToMesh(), IsleKit.Bare(b));      // under a roof: no snow

        // walls and roof under a soundproof parent; the south wall's door gap is 1.26 wide, the lintel 1 cm over the door
        Piece chamber = b.Empty("Star Chamber", 0f, -7.1f, slab, false);
        b.AddScript(chamber, "PuzzleSoundProof");
        Sdk.Set(chamber.Component("PuzzleSoundProof"), "material", 0);      // Wood
        float top = slab + 3.0f;
        var walls = new MeshBuilder();
        AddSpan(walls, -2.0f, 2.0f, y0, top, -5.2f, -5.0f, IsleKit.Wood);              // north (the lamp wall)
        AddSpan(walls, -2.0f, -1.8f, y0, top, -9.0f, -5.2f, IsleKit.Wood);             // west
        AddSpan(walls, 1.8f, 2.0f, y0, top, -9.0f, -5.2f, IsleKit.Wood);               // east
        AddSpan(walls, -2.0f, -0.63f, y0, top, -9.2f, -9.0f, IsleKit.Wood);            // south, either side of the door
        AddSpan(walls, 0.63f, 2.0f, y0, top, -9.2f, -9.0f, IsleKit.Wood);
        AddSpan(walls, -0.63f, 0.63f, slab + 2.03f, top, -9.2f, -9.0f, IsleKit.Wood);  // lintel
        b.MeshPiece("Chamber Walls", walls.ToMesh(), IsleKit.Snowy(b)).Under(chamber);
        var roof = new MeshBuilder();
        AddSpan(roof, -2.3f, 2.3f, top, top + 0.2f, -9.5f, -4.7f, IsleKit.DarkWood);
        b.MeshPiece("Chamber Roof", roof.ToMesh(), IsleKit.Snowy(b)).Under(chamber);

        Piece door = b.Part("Door", 0f, -9.1f, slab, 180f, false).Named("Chamber Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);      // CloseWhenPuzzleActive

        // the vestibule (roofed, not soundproof) with the two-way No Items Gate filling it
        var vest = new MeshBuilder();
        AddSpan(vest, -1.28f, -1.08f, y0, slab + 2.6f, -10.75f, -9.2f, IsleKit.Wood);
        AddSpan(vest, 1.08f, 1.28f, y0, slab + 2.6f, -10.75f, -9.2f, IsleKit.Wood);
        AddSpan(vest, -1.4f, 1.4f, slab + 2.6f, slab + 2.75f, -10.9f, -9.1f, IsleKit.DarkWood);
        b.MeshPiece("Vestibule", vest.ToMesh(), IsleKit.Snowy(b));
        b.Part("Gate No items", 0.05f, -9.92f, slab, 0f, false).Named("Chamber Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false);

        // the radio dead zone: room, doorway, vestibule and the porch's edge (x -2..2, z -11.0..-4.8)
        Piece dead = b.Part("Radio Dead Zone", 0f, -7.9f, slab + 1.5f, 0f, false).Named("Chamber Dead Zone");
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(4.0f, 3.0f, 6.2f);
        zone.center = Vector3.zero;

        // inside: the start switch and live lamp on the east wall by the door, the pendant, the lectern and the mic
        b.Part("Switch Start", 1.73f, -8.3f, slab + 1.25f, -90f, false).Named("Inside Start Switch");
        b.Part("Live Light", 1.74f, -8.3f, slab + 1.45f, -90f, false).Named("Inside Live Light");
        Piece pendant = b.Part("Light 2", 0f, -7.1f, slab + 2.95f, 0f, false).Named("Chamber Pendant")
            .Set("PuzzleLight", "intensity", 0.4f);
        Component pendantC = pendant.Component("PuzzleLight");
        var lectern = new MeshBuilder();
        AddSpan(lectern, -0.25f, 0.25f, slab, slab + 1.0f, -7.625f, -7.275f, IsleKit.DarkWood);
        b.MeshPiece("Lectern", lectern.ToMesh(), IsleKit.Bare(b));
        Piece mic = b.Part("Intercom Mic", 0f, -7.64f, slab + 0.95f, 180f, false).Named("Reader Mic");
        mic.Add("PuzzleIntercom", "speakers", west.Speaker, east.Speaker, north.Speaker);

        // the lamp map: left blue (West), middle white (North), right purple (East); a centred West for two players
        Component[] lampsW = MapCluster("W", "Map West", -1.15f, Blue, westSide);
        Component[] lampsN = MapCluster("N", "Map North", 0f, White, northGroup);
        Component[] lampsE = MapCluster("E", "Map East", 1.15f, Purple, eastGroup);
        Component[] lampsW2 = MapCluster("W2", "Map West 2", 0f, Blue, westCentre);

        Piece chamberTime = b.Part("Progress Bar", 0f, -5.31f, slab + 2.16f, 180f, false).Named("Chamber Time")
            .Scaled(0.9f, 1f, 1f).Set("PuzzleProgressBar", "total", 10);
        Piece chamberCount = b.Part("Progress Bar", 0f, -5.31f, slab + 2.66f, 180f, false).Named("Chamber Count")
            .Scaled(0.9f, 1f, 1f).Set("PuzzleProgressBar", "total", 10);
        Component chamberTimeC = chamberTime.Component("PuzzleProgressBar"), chamberCountC = chamberCount.Component("PuzzleProgressBar");
        Piece retry = b.Part("Timer", -1.75f, -6.4f, slab + 2.2f, 90f, false).Named("Retry Clock")
            .Set("PuzzleTimer", "duration", 3f);
        Component retryC = retry.Component("PuzzleTimer");

        // ---- the porch: the star board, the finale, and the gourd's box ----
        IsleKit.BoardOnPosts(b, "Star Board", 0f, -13.0f, 0f, 2.6f, 0.8f, 2.7f, IsleKit.Board);
        Piece porchStar = b.Part("Button N Hold", 0f, -12.87f, 1.15f, 0f).Named("Porch Star Button")
            .Set("PuzzleButtonNHold", "group", "star");
        Piece starClock = b.Part("Timer", 0.85f, -12.87f, 1.6f, 0f).Named("Star Clock").Set("PuzzleTimer", "duration", 6f);
        Piece gauge = b.Part("Progress Bar", 0f, -12.83f, 2.35f, 0f).Named("Star Gauge").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "total", 10000);      // unarmed until the count fills
        Piece porchLamp = b.Part("Light 6", -0.85f, -12.79f, 1.6f, 0f).Named("Porch Lamp").Set("PuzzleLight", "color", IsleKit.Gold);
        Piece lampOff = b.Part("Timer", 0f, -13.1f, 0.5f, 0f).Named("Lamp Off Clock").Set("PuzzleTimer", "duration", 0.1f);
        Component starClockC = starClock.Component("PuzzleTimer"), gaugeC = gauge.Component("PuzzleProgressBar");
        Component porchLampC = porchLamp.Component("PuzzleLight"), lampOffC = lampOff.Component("PuzzleTimer");

        var plinth = new MeshBuilder();
        Kit.AddBox(plinth, Vector3.zero, new Vector3(1.3f, 0.45f, 1.3f), IsleKit.Stone);
        b.MeshPiece("Plinth", plinth.ToMesh(), IsleKit.Snowy(b)).Transform.position = b.WorldPoint(-3.0f, -11.5f);
        Piece box = b.Part("Box", -3.0f, -11.5f, 0.45f, 0f).Named("Box");
        Component boxC = box.Component("PuzzleBox");

        IsleKit.StartCorner(b, 4.5f, -14.5f, 0f, false);

        // ---- the three Press In Order copies, one per lobby size ----
        var lamps2 = new List<Component>(lampsW2);
        var lamps3 = new List<Component>(lampsW); lamps3.AddRange(lampsE);
        var lamps4 = new List<Component>(lampsW); lamps4.AddRange(lampsN); lamps4.AddRange(lampsE);
        Component order2 = Order("Order 2", order2Group, 8, lamps2.ToArray(), new[] { west }, chamberTimeC, chamberCountC);
        Component order3 = Order("Order 3", order3Group, 10, lamps3.ToArray(), new[] { west, east }, chamberTimeC, chamberCountC);
        Component order4 = Order("Order 4", order4Group, 12, lamps4.ToArray(), new[] { west, north, east }, chamberTimeC, chamberCountC);
        Component[] orders = { order2, order3, order4 };

        // ---- wiring (persistent calls run in list order) ----
        Piece root = b.RootPiece;
        foreach (Component o in orders) root.On("PuzzleSDK", "onPuzzleStart", o, "Begin");
        root.On("PuzzleSDK", "onPuzzleStart", pendantC, "TurnOn");
        root.On("PuzzleSDK", "onPuzzleStart", gaugeC, "Reset");
        foreach (Component o in orders) root.On("PuzzleSDK", "onPuzzleStop", o, "End");
        root.On("PuzzleSDK", "onPuzzleStop", pendantC, "TurnOff");
        root.On("PuzzleSDK", "onPuzzleStop", retryC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", starClockC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", gaugeC, "SetTotal", 10000);
        root.On("PuzzleSDK", "onPuzzleStop", lampOffC, "ResetTimer");
        root.On("PuzzleSDK", "onPuzzleStop", lampOffC, "StartTimer");
        root.On("PuzzleSDK", "onWin", boxC, "Open");      // so the companion's do=win also opens it; the gauge is the real path
        lampOff.On("PuzzleTimer", "onFinished", porchLampC, "TurnOff");

        foreach (Component o in orders)
        {
            Sdk.Wire(o, "onBegin", porchLampC, "TurnOff");
            Sdk.Wire(o, "onFail", retryC, "StartTimer");
            retry.On("PuzzleTimer", "onFinished", o, "Begin");
        }

        // the count filling arms the finale: the door opens, the porch lamp lights, the gauge is reset then set to 1
        chamberCount.On("PuzzleProgressBar", "onFull", door.Component("PuzzleDoor"), "Open");
        chamberCount.On("PuzzleProgressBar", "onFull", porchLampC, "TurnOn");
        chamberCount.On("PuzzleProgressBar", "onFull", gaugeC, "Reset");
        chamberCount.On("PuzzleProgressBar", "onFull", gaugeC, "SetTotal", 1);

        // the finale: every star button held at once (hidden ones drop out of the group), six unbroken seconds
        porchStar.On("PuzzleButtonNHold", "onAllHeld", starClockC, "StartTimer");
        porchStar.On("PuzzleButtonNHold", "onAllHeld", starLampC, "TurnOn");
        porchStar.On("PuzzleButtonNHold", "onAllReleased", starClockC, "ResetTimer");
        porchStar.On("PuzzleButtonNHold", "onAllReleased", starLampC, "TurnOff");
        starClock.On("PuzzleTimer", "onFinished", gaugeC, "Increment");
        gauge.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");
        gauge.On("PuzzleProgressBar", "onFull", boxC, "Open");

        // ---- lights: a ring round the site, and a string from each station to the chamber's roof in its colour ----
        Kit.LightRing(b, "Site Lights", 0f, 0f, 50f, 46f, 2.7f, true, 51);
        // (posts 1.4 m behind each board on the tree side, and just outside the chamber's eaves; North's string bends
        // round the east of the tree)
        GroupedLightString("West Lights", new List<Vector3> { new Vector3(-20.6f, 2.4f, -1.2f), new Vector3(-2.4f, 3.3f, -4.6f) }, Blue, 52, null);
        GroupedLightString("East Lights", new List<Vector3> { new Vector3(20.6f, 2.4f, 1.2f), new Vector3(2.4f, 3.3f, -4.6f) }, Purple, 53, eastGroup);
        GroupedLightString("North Lights", new List<Vector3> { new Vector3(1.2f, 2.4f, 18.6f), new Vector3(8f, 3.0f, 4f), new Vector3(1.0f, 3.3f, -4.6f) }, White, 54, northGroup);
    }
}
