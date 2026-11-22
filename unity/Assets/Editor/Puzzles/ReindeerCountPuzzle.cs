// Reindeer Count: Roll Call in the Reindeer Shed (spec: puzzle-specs/ReindeerCount.md, revision 2, 2026-11-22).
//
// A sealed, roofed timber shed (32 x 32 m inside) holds twenty reindeer stalls round a decorated fir. Each of three
// walls (West, East, North) has one long window band of glass, 1 m tall, and outside each runs a raised gallery. Each
// closed stall is a roofed box open on one side only, aimed at one gallery, with a red "nose" lamp on its back wall:
// the West gallery sees only west-facing stalls, the East only east-facing ones, the North (4-player lobbies) only
// north-facing ones; two open pavilions show to West and East. No one place sees two kinds of stall.
//
// South of the shed stands the Tally Barn, windowless and sealed, its door shut while the puzzle runs. On its floor is
// a 1:3 plan of the shed with a pressure pad at every stall's spot. Press In Order in its timed mode lights one nose
// at a time; the spotters work out who can see it and describe where it is; when the phase runs out the presser must
// be standing on that stall's pad and on no other. Thirteen in a row win (15 in 4p, 10 in 2p); one miss empties the
// bar. The lists for the other lobby sizes are switched off (PuzzleHider), so only one is ever live.
//
// The stalls are placed at build time by a seeded search, and the build proves its sight lines: every lamp seen from
// its own gallery, no lamp from any other gallery, no point outside the shed (ground to 40 m up, 60 m out) that sees
// two kinds, the barn sealed but for its door, and the guessing odds of a short team under the spec's limits. Any
// failure fails the build. No custom code.
//
// Coordinates: local metres, x east, z north (+z forward), heights above the top of one level pad (Floor above the
// root); every piece is placed onGround: false on that pad. Yaw: 0 faces +z, 90 +x, -90 -x, 180 -z.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bwm;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ReindeerCountPuzzle : PuzzleDef
{
    public override string Name => "ReindeerCount";
    public override Vector2 Footprint => new Vector2(54f, 70f);
    // layout-final: a big farm flat on the north shore by Northwest and North Shore stations, its north side open
    public override Vector2? FixedSite => new Vector2(-1590f, -720f);

    const float Floor = 0.2f;           // the pad's top, above the root's level
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);
    static readonly Color NoseRed = new Color(0.95f, 0.1f, 0.08f);

    PuzzleBuilder b;
    readonly List<Solid> solids = new List<Solid>();

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(56f, 72f, 0f, -1f);
        b.DebugTeleport(0f, -30f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks anywhere over the puzzle (Winter Walk): no hopping between galleries or onto the roofs
        IsleKit.CalmAir(b, "Reindeer Count", 0f, -1f, new Vector3(70f, 150f, 86f), 0f, -10f, false);

        // ---- the layout: placed and proved by the seeded search (throws if no seed passes) ----
        var log = new List<string>();
        Layout plan = Find(log);
        foreach (string line in log) Debug.Log("[BWM] Reindeer Count: " + line);
        solids.AddRange(plan.Solids);

        Piece eastGroup = IsleKit.Lobby(b, "East Gallery", false, true, true);
        Piece northGroup = IsleKit.Lobby(b, "North Gallery", false, false, true);

        // ---- meshes, one per group of solids ----
        Emit("Ground Pad", "Pad", snowy, true, null);
        Emit("Reindeer Shed", "Shed", snowy, true, null);
        Emit("Shed Inside", "Inside", bare, true, null);
        Emit("West Gallery Deck", "West Gallery", snowy, true, null);
        Emit("East Gallery Deck", "East Gallery", snowy, true, eastGroup);
        Emit("North Gallery Deck", "North Gallery", snowy, true, northGroup);
        Emit("Way Out Vestibule", "Vestibule", snowy, true, null);
        Emit("Tally Barn", "Barn", snowy, true, null);
        Emit("Barn Floor", "Barn Floor", bare, true, null);

        // the fir in the middle of the shed (indoors: no snow on it), its warm white and gold lights (no red: red is
        // the noses' colour)
        var fir = new MeshBuilder();
        Kit.AddPine(fir, new Vector3(HX, 0.02f, HZ), 4.2f, 2512);
        OnPad("The Shed Tree", fir, bare, false);
        var spiral = new List<Vector3>();
        for (int k = 0; k <= 20; k++)
        {
            float t = k / 20f, h = 0.9f + 3.1f * t, r = 1.25f * (4.3f - h) / 3.6f + 0.05f, a = t * 3f * 2f * Mathf.PI;
            spiral.Add(new Vector3(HX + r * Mathf.Cos(a), Floor + h, HZ + r * Mathf.Sin(a)));
        }
        Kit.LightString(b, "Shed Tree Lights", spiral, 0.05f, 0.3f, false, false, 2513, new[] { Warm, IsleKit.Gold });

        BuildPlan(plan, bare, northGroup);

        // ================= the shed: lamps, glass, guard, way out, room lights =================
        var lamps = new Dictionary<string, Component>();
        var pads = new Dictionary<string, Component>();
        foreach (Stall s in plan.Stalls)
        {
            Piece lamp = s.Kind == Kind.P
                ? b.Part("Light 2", s.C.x, s.C.y, Floor + PavPivot, 0f, false)
                : b.Part("Light 6", s.Lamp.x, s.Lamp.z, Floor + LampH, s.Yaw, false);
            lamp.Named("Nose " + s.Name).Set("PuzzleLight", "color", NoseRed).Set("PuzzleLight", "range", 1.5f);
            lamps[s.Name] = lamp.Component("PuzzleLight");
            Vector2 pad = PlanPoint(s.C);
            pads[s.Name] = IsleKit.Sensor(b, "Pad " + s.Name, pad.x, pad.y, new Vector3(1f, 2.6f, 1f), Floor + Slab, false)
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1)
                .Component("PuzzlePresenceSensor");
        }

        // eight panes per window band, flush with the wall's outer face (no ledge outside), between the mullions
        for (int k = 0; k < 8; k++)
        {
            float along = WinZ0 + k * (PaneW + Mullion) + PaneW / 2f;
            Pane("Window West " + (k + 1), -ShedOut + 0.02f, along, 90f);
            Pane("Window East " + (k + 1), ShedOut - 0.02f, along, 90f);
            Pane("Window North " + (k + 1), -NWinX + k * (PaneW + Mullion) + PaneW / 2f, ShedZ1 - 0.02f, 0f);
        }

        // Backstop only: nobody can be inside the sealed shed. If a glitch puts someone there, a running run ends.
        Piece guard = IsleKit.Sensor(b, "Shed Guard", HX, HZ, new Vector3(32f, 4.6f, 32f), Floor, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        // One way, from the shed out into the vestibule (front is the inside), for anyone a glitch puts inside
        b.Part("Gate No items", GateX, GateZ, Floor, 180f, false).Named("Way Out")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        int light = 1;
        foreach (float dx in new[] { -8f, 8f })
            foreach (float dz in new[] { -8f, 8f })
                b.Empty("Room Light " + light++ + " FFE6C4 12 1.1", HX + dx, HZ + dz, Floor + 4.2f, false);

        // ================= the Tally Barn =================
        b.Part("Door", 0f, (BarnZ0 + BarnInZ0) / 2f, Floor + Slab, 0f, false).Named("Barn Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false);      // voices from the galleries must come in
        b.Part("Switch Start", -2.0f, BarnInZ0 + SwitchBack, Floor + Slab + 1.25f, 0f, false).Named("Start Barn");
        b.Part("Live Light", -2.35f, BarnInZ0 + LiveBack, Floor + Slab + 1.42f, 0f, false).Named("Live Barn");
        float barnBarZ = BarnInZ1 - BarBack;
        Piece barnRounds = b.Part("Progress Bar", 0f, barnBarZ, Floor + Slab + 2.6f, 180f, false).Named("Barn Rounds")
            .Set("PuzzleProgressBar", "total", 13);
        Piece barnPhase = b.Part("Progress Bar", 0f, barnBarZ, Floor + Slab + 1.9f, 180f, false).Named("Barn Phase")
            .Set("PuzzleProgressBar", "total", 7);
        b.Part("Confetti", 0f, -12.0f, Floor + Slab + 0.2f, 0f, false).Named("Barn Confetti");
        float speakerZ = BarnInZ1 - SpeakerBack;
        Piece speakerWest = b.Part("Intercom Speaker", -4f, speakerZ, Floor + Slab + 2.4f, 180f, false).Named("Speaker West");
        Piece speakerEast = b.Part("Intercom Speaker", 4f, speakerZ, Floor + Slab + 2.4f, 180f, false).Named("Speaker East").Under(eastGroup);
        Piece speakerNorth = b.Part("Intercom Speaker", -4f, speakerZ, Floor + Slab + 1.6f, 180f, false).Named("Speaker North").Under(northGroup);
        b.Empty("Room Light 5 FFE6C4 8 1.0", 0f, -14.5f, Floor + 2.9f, false);
        b.Empty("Room Light 6 FFE6C4 8 1.0", 0f, -20.5f, Floor + 2.9f, false);

        // ================= the galleries: bars on the solid wall at one end of each window, a mic at the other =================
        Piece Bar(string name, float x, float z, float height, float yaw, int total)
            => b.Part("Progress Bar", x, z, Floor + Deck + height, yaw, false).Named(name).Scaled(GalleryBar, GalleryBar, GalleryBar)
                .Set("PuzzleProgressBar", "total", total);
        float wx = -ShedOut - BarBack * GalleryBar, ex = ShedOut + BarBack * GalleryBar, nz = ShedZ1 + BarBack * GalleryBar;
        Piece westRounds = Bar("West Rounds", wx, SideBarZ, 0.95f, -90f, 13), westPhase = Bar("West Phase", wx, SideBarZ, 0.65f, -90f, 7);
        Piece eastRounds = Bar("East Rounds", ex, SideBarZ, 0.95f, 90f, 13).Under(eastGroup), eastPhase = Bar("East Phase", ex, SideBarZ, 0.65f, 90f, 7).Under(eastGroup);
        Piece northRounds = Bar("North Rounds", NorthBarX, nz, 0.95f, 0f, 15).Under(northGroup), northPhase = Bar("North Phase", NorthBarX, nz, 0.65f, 0f, 7).Under(northGroup);
        // the window runs from just above the deck, so each mic hangs on the solid wall just past the window's end
        Piece micWest = b.Part("Intercom Mic", -ShedOut - MicBack, MicSideZ, Floor + Deck + 0.45f, -90f, false).Named("Mic West");
        Piece micEast = b.Part("Intercom Mic", ShedOut + MicBack, MicSideZ, Floor + Deck + 0.45f, 90f, false).Named("Mic East").Under(eastGroup);
        Piece micNorth = b.Part("Intercom Mic", MicNorthX, ShedZ1 + MicBack, Floor + Deck + 0.45f, 0f, false).Named("Mic North").Under(northGroup);
        micWest.Add("PuzzleIntercom", "speakers", speakerWest.Component("PuzzleIntercomSpeaker"));
        micEast.Add("PuzzleIntercom", "speakers", speakerEast.Component("PuzzleIntercomSpeaker"));
        micNorth.Add("PuzzleIntercom", "speakers", speakerNorth.Component("PuzzleIntercomSpeaker"));
        b.Part("Confetti", -17.3f, 17.5f, Floor + Deck + 0.2f, 0f, false).Named("West Confetti");
        b.Part("Confetti", 17.3f, 17.5f, Floor + Deck + 0.2f, 0f, false).Named("East Confetti").Under(eastGroup);
        b.Part("Confetti", 8.5f, 26.3f, Floor + Deck + 0.2f, 0f, false).Named("North Confetti").Under(northGroup);

        // ================= the start corner outside the barn door, facing arriving players =================
        IsleKit.StartCorner(b, -4.0f, -27.5f, 180f, true, Floor, false);
        // a U of lights open to the south: nothing on the approach to the barn door, every post 7 m or more from a stair
        var site = new List<Vector3>();
        foreach (Vector2 p in new[] { new Vector2(-26f, -33f), new Vector2(-26f, -1f), new Vector2(-26f, 31f), new Vector2(0f, 31f),
                                      new Vector2(26f, 31f), new Vector2(26f, -1f), new Vector2(26f, -33f) })
            site.Add(new Vector3(p.x, Floor + 2.6f, p.y));
        Kit.LightString(b, "Site Lights", site, 0.4f, 0.5f, false, true, 2514);

        // ================= Press In Order: one list per lobby size, each on its own Lobby group =================
        Component bar(Piece p) => p.Component("PuzzleProgressBar");
        var lists = new List<RollCall>
        {
            new RollCall("Roll Call 2", true, false, false, new[] { Kind.W, Kind.P }, 10, 8f,
                new[] { bar(barnRounds), bar(westRounds) }, new[] { bar(barnPhase), bar(westPhase) }),
            new RollCall("Roll Call 3", false, true, false, new[] { Kind.W, Kind.E, Kind.P }, 13, 7f,
                new[] { bar(barnRounds), bar(westRounds), bar(eastRounds) }, new[] { bar(barnPhase), bar(westPhase), bar(eastPhase) }),
            new RollCall("Roll Call 4", false, false, true, new[] { Kind.W, Kind.E, Kind.N }, 15, 7f,
                new[] { bar(barnRounds), bar(westRounds), bar(eastRounds), bar(northRounds) },
                new[] { bar(barnPhase), bar(westPhase), bar(eastPhase), bar(northPhase) }),
        };
        var oddsLists = new List<(string name, List<Kind> pairs, int rounds, int lobby)>();
        Piece root = b.RootPiece;
        foreach (RollCall rc in lists)
        {
            Piece group = IsleKit.Lobby(b, rc.Name, rc.Two, rc.Three, rc.Four);
            Component pio = b.AddScript(group, "PuzzlePressInOrder");
            var pairs = new List<Kind>();
            foreach (Kind kind in rc.Kinds)
                foreach (Stall s in plan.Stalls)
                    if (s.Kind == kind)
                    {
                        Sdk.Add(pio, "lights", lamps[s.Name]);
                        Sdk.Add(pio, "buttons", pads[s.Name]);
                        pairs.Add(kind);
                    }
            foreach (Component c in rc.Bars) Sdk.Add(pio, "progressBars", c);
            foreach (Component c in rc.Phases) Sdk.Add(pio, "phaseBars", c);
            Sdk.Set(pio, "rounds", rc.Rounds);
            Sdk.Set(pio, "holdAtEnd", true);
            Sdk.Set(pio, "phaseSeconds", rc.Phase);
            root.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
            root.On("PuzzleSDK", "onPuzzleStop", pio, "End");
            guard.On("PuzzlePresenceSensor", "onPressed", pio, "End");
            oddsLists.Add((rc.Name, pairs, rc.Rounds, rc.Two ? 2 : rc.Three ? 3 : 4));
        }
        barnRounds.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");      // Win on one bar only

        // ---- check 7: the guessing odds of a team short of a spotter, from the lists just built ----
        bool oddsOk = GuessingOdds(oddsLists, out List<string> oddsLines);
        foreach (string line in oddsLines) Debug.Log("[BWM] Reindeer Count: " + line);
        if (!oddsOk) throw new Exception("Reindeer Count: a short team's guessing odds are over the spec's limits (see the table above)");
    }

    // One Press In Order list
    sealed class RollCall
    {
        public readonly string Name; public readonly bool Two, Three, Four; public readonly Kind[] Kinds;
        public readonly int Rounds; public readonly float Phase; public readonly Component[] Bars, Phases;
        public RollCall(string name, bool two, bool three, bool four, Kind[] kinds, int rounds, float phase, Component[] bars, Component[] phases)
        { Name = name; Two = two; Three = three; Four = four; Kinds = kinds; Rounds = rounds; Phase = phase; Bars = bars; Phases = phases; }
    }

    // ---- part sizes behind their pivots (world\part-metrics.tsv) ----
    const float SwitchBack = 0.073f, LiveBack = 0.006f, BarBack = 0.101f, SpeakerBack = 0.1f, MicBack = 0.02f;
    const float GalleryBar = 0.4f;
    const float SideBarZ = 0.25f;       // a 0.4 bar is 1.43 m long: z -0.47..0.97, on the solid wall before the window (z 1)
    const float NorthBarX = -8.85f;     // x -9.57..-8.13, before the window (x -8)

    // A clear glass pane in a window band, centred at (x, z) half way up the band; yaw 90 on the side walls, 0 on the
    // north (PuzzleBuilder.Part keeps the Glass prefab upright; Pane sizes it in metres)
    void Pane(string name, float x, float z, float yaw)
    {
        b.Part("Glass", x, z, Floor + (Sill + Head) / 2f, yaw, false).Named(name)
            .Pane(PaneW, (Head - Sill)).Set("PuzzleGlass", "style", 2);
    }

    // A mesh built in pad-top coordinates, placed on the pad
    Piece OnPad(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, Floor, false);
        return piece;
    }

    // Every solid of one group into one mesh piece
    void Emit(string name, string group, Material material, bool collider, Piece parent)
    {
        var mb = new MeshBuilder();
        foreach (Solid s in solids)
            if (s.Group == group) AddSolid(mb, s);
        if (mb.VertexCount == 0) throw new Exception("Reindeer Count: nothing in group " + group);
        Piece piece = OnPad(name, mb, material, collider);
        if (parent != null) piece.Under(parent);
    }

    // A convex solid's faces, flat-shaded, each wound to face out (Make oriented them)
    static void AddSolid(MeshBuilder mb, Solid s)
    {
        foreach (int[] f in s.F)
            for (int k = 1; k + 1 < f.Length; k++)
                mb.Tri(s.P[f[0]], s.P[f[k]], s.P[f[k + 1]], s.Colour);
    }

    // ---- the floor plan in the barn: the shed at 1:3, north up, centred at (0, -17.5) ----
    void BuildPlan(Layout plan, Material bare, Piece northGroup)
    {
        const float y0 = Slab, yIce = Slab + 0.01f, yLine = Slab + 0.02f, yTop = Slab + 0.03f;
        Color shedLine = IsleKit.DarkWood, stallLine = IsleKit.DarkWood, pane = new Color(0.45f, 0.7f, 0.9f);
        Color westMark = IsleKit.Green, eastMark = IsleKit.Gold, northMark = new Color(0.25f, 0.45f, 0.8f);

        // the cream ground, walkable
        var ground = new MeshBuilder();
        Kit.AddBox(ground, new Vector3(0f, y0, PlanCentre.y), new Vector3(12.0f, 0.01f, 12.0f), IsleKit.Cream);
        OnPad("Floor Plan Ground", ground, bare);

        var mb = new MeshBuilder();
        void Strip(Vector2 a, Vector2 c, float width, float y, float h, Color colour)
        {
            Vector2 d = c - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 mid = (a + c) / 2f;
            Kit.AddBox(mb, new Vector3(mid.x, y, mid.y), new Vector3(width, h, len + width), colour, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }
        Vector2 P(float x, float z) => PlanPoint(new Vector2(x, z));

        // the shed's outline (its wall centre lines) with the three window bands marked over it
        float wi = (ShedOut + ShedIn) / 2f, s0 = (ShedZ0 - 0.15f), s1 = (ShedZ1 - 0.15f);
        Strip(P(-wi, s0), P(wi, s0), 0.06f, yLine, 0.01f, shedLine);
        Strip(P(-wi, s1), P(wi, s1), 0.06f, yLine, 0.01f, shedLine);
        Strip(P(-wi, s0), P(-wi, s1), 0.06f, yLine, 0.01f, shedLine);
        Strip(P(wi, s0), P(wi, s1), 0.06f, yLine, 0.01f, shedLine);
        Strip(P(-wi, WinZ0), P(-wi, WinZ1), 0.1f, yTop, 0.01f, pane);
        Strip(P(wi, WinZ0), P(wi, WinZ1), 0.1f, yTop, 0.01f, pane);
        Strip(P(-NWinX, s1), P(NWinX, s1), 0.1f, yTop, 0.01f, pane);

        // each pad's square in pale ice, each stall's outline as a U (its gap on the open side), pavilions as four dots
        foreach (Stall s in plan.Stalls)
        {
            Vector2 c = PlanPoint(s.C);
            Kit.AddBox(mb, new Vector3(c.x, yIce, c.y), new Vector3(0.96f, 0.01f, 0.96f), IsleKit.Ice);
            if (s.Kind == Kind.P)
            {
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        Vector2 post = PlanPoint(s.At(sx * PavHalf, sz * PavHalf));
                        Kit.AddBox(mb, new Vector3(post.x, yLine, post.y), new Vector3(0.09f, 0.01f, 0.09f), stallLine);
                    }
                continue;
            }
            Vector2 bl = PlanPoint(s.At(-StallW / 2f, -StallD / 2f)), br = PlanPoint(s.At(StallW / 2f, -StallD / 2f));
            Vector2 fl = PlanPoint(s.At(-StallW / 2f, StallD / 2f)), fr = PlanPoint(s.At(StallW / 2f, StallD / 2f));
            Strip(bl, br, 0.05f, yLine, 0.01f, stallLine);
            Strip(bl, fl, 0.05f, yLine, 0.01f, stallLine);
            Strip(br, fr, 0.05f, yLine, 0.01f, stallLine);
        }

        // the landmarks: the fir (a green disc with a gold star), the hay (gold), the trough (ice), the sledge (green and gold)
        Vector2 firAt = P(HX, HZ);
        Kit.AddDisc(mb, new Vector3(firAt.x, yLine, firAt.y), 1.8f / 3f, 0.01f, IsleKit.Green);
        Kit.AddDisc(mb, new Vector3(firAt.x, yTop, firAt.y), 0.12f, 0.01f, IsleKit.Gold, 10);
        foreach (Mark m in plan.Marks)
        {
            Vector2 c = PlanPoint(m.C);
            Color colour = m.Name == "Hay" ? IsleKit.Gold : m.Name == "Trough" ? IsleKit.Ice : IsleKit.Green;
            Kit.AddBox(mb, new Vector3(c.x, yLine, c.y), new Vector3(m.Size.x / 3f, 0.01f, m.Size.z / 3f), colour, m.Yaw);
            if (m.Name == "Sledge")
                Kit.AddBox(mb, new Vector3(c.x, yTop, c.y), new Vector3(m.Size.x / 3f * 0.7f, 0.01f, 0.04f), IsleKit.Gold, m.Yaw);
        }

        // the West and East galleries as strips outside the outline, each with its letter south of it
        Strip(P(-17.3f, 0f), P(-17.3f, 18f), 0.6f, yLine, 0.01f, westMark);
        Strip(P(17.3f, 0f), P(17.3f, 18f), 0.6f, yLine, 0.01f, eastMark);
        Letter(Strip, "W", P(-17.3f, 0f) + new Vector2(0f, -0.75f), 0.5f, yLine, westMark);
        Letter(Strip, "E", P(17.3f, 0f) + new Vector2(0f, -0.75f), 0.5f, yLine, eastMark);
        OnPad("Floor Plan", mb, bare, false);

        // the North gallery's mark, in 4-player lobbies only
        var north = new MeshBuilder();
        mb = north;
        Vector2 na = P(-9f, 26.3f), nb = P(9f, 26.3f);
        na.y = Mathf.Min(na.y, BarnInZ1 - 0.3f); nb.y = na.y;
        Strip(na, nb, 0.5f, yLine, 0.01f, northMark);
        Letter(Strip, "N", new Vector2(na.x - 0.55f, na.y), 0.45f, yLine, northMark);
        OnPad("Floor Plan North", north, bare, false).Under(northGroup);
    }

    // A capital W, E or N drawn in strips, centred at 'c', 'size' tall, read facing north
    static void Letter(Action<Vector2, Vector2, float, float, float, Color> strip, string letter, Vector2 c, float size, float y, Color colour)
    {
        float[][] strokes = letter == "W" ? new[] { new[] { 0f, 1f, 0.25f, 0f }, new[] { 0.25f, 0f, 0.5f, 0.6f }, new[] { 0.5f, 0.6f, 0.75f, 0f }, new[] { 0.75f, 0f, 1f, 1f } }
            : letter == "E" ? new[] { new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 1f, 0.8f, 1f }, new[] { 0f, 0.5f, 0.65f, 0.5f }, new[] { 0f, 0f, 0.8f, 0f } }
            : new[] { new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 1f, 0.8f, 0f }, new[] { 0.8f, 0f, 0.8f, 1f } };
        foreach (float[] s in strokes)
            strip(c + new Vector2((s[0] - 0.5f) * size, (s[1] - 0.5f) * size), c + new Vector2((s[2] - 0.5f) * size, (s[3] - 0.5f) * size), 0.05f, y, 0.01f, colour);
    }

    // ==== PURE BEGIN: geometry, placement and sight checks (no editor calls; tested offline against stubs) ====

    public enum Kind { W, E, N, P }
    public const int Gw = 0, Ge = 1, Gn = 2;          // the galleries
    public static string GalleryName(int g) => g == Gw ? "West" : g == Ge ? "East" : "North";
    // Whether gallery g is meant to see a stall of this kind
    public static bool Sees(int g, Kind k) => k == Kind.P ? g != Gn : (int)k == g;

    // ---- the shed (local metres, heights above the pad top) ----
    public const float HX = 0f, HZ = 9f;                                      // the shed's centre H
    public const float ShedOut = 16.3f, ShedIn = 16f, ShedZ0 = -7.3f, ShedZ1 = 25.3f, ShedInZ0 = -7f, ShedInZ1 = 25f;
    public const float Eaves = 4.6f, RoofTop = 4.9f, Ridge = 6.0f, Sill = 3.0f, Head = 4.0f;
    public const float WinZ0 = 1f, WinZ1 = 17f, NWinX = 8f, Mullion = 0.05f, PaneW = (16f - 7f * Mullion) / 8f;
    public const float GateX = 10f, GateZ = -7.15f, GateX0 = 8.99f, GateX1 = 11.11f, GateTop = 2.3f;   // the Way Out's frame
    // The decks stand 2.9 m up (the spec had 2.5): players' eyes are only 0.6 m above their feet, so a standing eye is at
    // 3.5 m, mid-window, where the spec's sight lines put it. Eye points: crouch 0.4, stand 0.6, 1.0, jump 1.6 above the deck.
    public const float Deck = 2.9f;
    public const int StairSteps = 10, Stand = 1;
    public static readonly float[] EyeUp = { 0.4f, 0.6f, 1.0f, 1.6f };
    public const float MicSideZ = 17.5f, MicNorthX = 8.5f;     // mics on the solid wall just past each window's end, at eye height
    // ---- the stalls ----
    public const float StallW = 2.0f, StallD = 2.8f, StallWall = 0.15f, StallRoof = 2.65f, LampH = 2.2f;
    public const float LampOut = 0.142f, DiscOut = 0.222f;      // a Light 6's pivot and face in front of its back (part-metrics)
    public const float PavHalf = 1.2f, PavPost = 0.15f, PavBulb = 2.25f, PavPivot = 2.6f;
    public const float MinApart = 4.8f, FirClear = 4.5f, MarkClear = 3.6f, Spread = 13.5f;
    // Regions: North stalls z <= 13 (13 m or more from the North deck); West, East and pavilions anywhere. The spec's
    // West x >= -4 and East x <= 4 left almost no layout that passes check 3 (a stall's lit side walls show up to about
    // 45 degrees off its axis), and the checks themselves enforce what those regions were for
    public const float NorthMaxZ = 13f;
    public const int Tries = 600, SampleStep = 3;      // candidates per stall; every third exterior point is checked while drawing
    public static readonly int[] Rejects = new int[6];   // diagnostics: candidates refused for (own sight, hiding another, seen from W, E, N, outside)
    public static string LastFail = "";     // which stall the last failed draw could not place
    // ---- the barn ----
    public const float BarnX = 6.5f, BarnIn = 6.2f, BarnZ0 = -24f, BarnZ1 = -11f, BarnInZ0 = -23.7f, BarnInZ1 = -11.3f;
    public const float Slab = 0.15f, BarnWall = 3.2f, BarnRidge = 5.2f, DoorHalf = 0.63f, DoorTop = Slab + 2.05f;
    public static readonly Vector2 PlanCentre = new Vector2(0f, -17.5f);
    // ---- the search ----
    public const int SeedBase = 2512, SeedSpan = 2000;
    public const int FoundSeed = 2570;                 // the seed the last search found: tried first, so builds are stable
    // Pavilions against the North gallery: false (the default) checks them only against West and East, since they are
    // dark in 4-player lobbies, the only ones with a North gallery; true also forbids the North gallery to see them.
    public static readonly bool StrictPavilions = false;

    public static Vector2 Turn(float x, float z, float yaw)
    {
        float a = yaw * Mathf.Deg2Rad;
        return new Vector2(x * Mathf.Cos(a) + z * Mathf.Sin(a), -x * Mathf.Sin(a) + z * Mathf.Cos(a));
    }
    public static float Aim(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;
    public static Vector2 PlanPoint(Vector2 shed) => new Vector2(shed.x / 3f, PlanCentre.y + (shed.y - HZ) / 3f);
    static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);
    static float Ax(Vector3 v, int k) => k == 0 ? v.x : k == 1 ? v.y : v.z;

    // A convex solid: its corners, faces (wound to face out) and planes. The same list draws the meshes and blocks
    // the sight checks, so the two cannot drift apart.
    public sealed class Solid
    {
        public Vector3[] P; public int[][] F; public Vector3[] N; public float[] D;
        public Vector3 Min, Max;
        public Color Colour; public string Group;  // the mesh it is drawn in ("" = none: a check volume only)
        public bool Check = true;                  // takes part in the sight checks
        public bool Sure = true;                   // surely blocks sight (false: rails, bars, generous volumes; these only make "must see" harder)
        public int Owner = -1;                     // the stall it belongs to; -2 the fir and landmarks; -1 fixed

        public static Solid Make(Vector3[] p, int[][] f, Color colour, string group)
        {
            var s = new Solid { P = p, F = f, Colour = colour, Group = group, N = new Vector3[f.Length], D = new float[f.Length] };
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in p) c += v;
            c /= p.Length;
            s.Min = p[0]; s.Max = p[0];
            foreach (Vector3 v in p) { s.Min = Vector3.Min(s.Min, v); s.Max = Vector3.Max(s.Max, v); }
            for (int i = 0; i < f.Length; i++)
            {
                Vector3 a = p[f[i][0]], b2 = p[f[i][1]], e = p[f[i][2]];
                Vector3 n = Vector3.Cross(b2 - a, e - a).normalized;
                float d = Vector3.Dot(n, a);
                if (Vector3.Dot(n, c) - d > 0f) { n = -n; d = -d; Array.Reverse(f[i]); }
                s.N[i] = n; s.D[i] = d;
            }
            return s;
        }

        // A box standing on 'foot' (x, z) from height y0, size (across, up, along), turned by yaw
        public static Solid Box(Vector2 foot, float y0, Vector3 size, float yaw, Color colour, string group)
        {
            float hx = size.x / 2f, hz = size.z / 2f;
            Vector2[] q = { Turn(-hx, -hz, yaw), Turn(hx, -hz, yaw), Turn(hx, hz, yaw), Turn(-hx, hz, yaw) };
            var p = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                p[i] = new Vector3(foot.x + q[i].x, y0, foot.y + q[i].y);
                p[i + 4] = new Vector3(foot.x + q[i].x, y0 + size.y, foot.y + q[i].y);
            }
            return Make(p, new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } }, colour, group);
        }

        // An axis-aligned block between two corners
        public static Solid Block(float x0, float x1, float y0, float y1, float z0, float z1, Color colour, string group)
            => Box(new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f), y0, new Vector3(Mathf.Abs(x1 - x0), y1 - y0, Mathf.Abs(z1 - z0)), 0f, colour, group);

        // A convex profile in (z, y), in order round it, run along x from x0 to x1 (gables, roof slopes, the ridge)
        public static Solid ExtrudeX(float x0, float x1, Vector2[] zy, Color colour, string group)
        {
            int n = zy.Length;
            var p = new Vector3[2 * n];
            for (int i = 0; i < n; i++) { p[i] = new Vector3(x0, zy[i].y, zy[i].x); p[n + i] = new Vector3(x1, zy[i].y, zy[i].x); }
            var f = new List<int[]>();
            var cap0 = new int[n]; var cap1 = new int[n];
            for (int i = 0; i < n; i++) { cap0[i] = i; cap1[i] = n + i; }
            f.Add(cap0); f.Add(cap1);
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; f.Add(new[] { i, j, n + j, n + i }); }
            return Make(p, f.ToArray(), colour, group);
        }

        // An eight-sided frustum about (x, z), radius r0 at y0 to r1 at y1 (the fir's foliage, as a check volume)
        public static Solid Frustum(Vector2 c, float y0, float r0, float y1, float r1, string group)
        {
            var p = new Vector3[16];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                p[i] = new Vector3(c.x + r0 * Mathf.Cos(a), y0, c.y + r0 * Mathf.Sin(a));
                p[8 + i] = new Vector3(c.x + r1 * Mathf.Cos(a), y1, c.y + r1 * Mathf.Sin(a));
            }
            var f = new List<int[]> { new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 8, 9, 10, 11, 12, 13, 14, 15 } };
            for (int i = 0; i < 8; i++) { int j = (i + 1) % 8; f.Add(new[] { i, j, 8 + j, 8 + i }); }
            return Make(p, f.ToArray(), Color.white, group);
        }

        // Whether the segment a .. a + d passes through the solid's inside (touching a face does not count)
        public bool Hits(Vector3 a, Vector3 d)
        {
            float t0 = 0f, t1 = 1f;
            for (int k = 0; k < 3; k++)
            {
                float ak = Ax(a, k), dk = Ax(d, k), lo = Ax(Min, k), hi = Ax(Max, k);
                if (Mathf.Abs(dk) < 1e-9f) { if (ak < lo || ak > hi) return false; continue; }
                float ta = (lo - ak) / dk, tb = (hi - ak) / dk;
                if (ta > tb) { float t = ta; ta = tb; tb = t; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            t0 = 0f; t1 = 1f;
            for (int i = 0; i < N.Length; i++)
            {
                float den = Vector3.Dot(N[i], d), num = D[i] - Vector3.Dot(N[i], a);
                if (Mathf.Abs(den) < 1e-9f) { if (num < 0f) return false; continue; }
                float t = num / den;
                if (den < 0f) { if (t > t0) t0 = t; }
                else if (t < t1) t1 = t;
                if (t0 >= t1) return false;
            }
            return t1 - t0 > 1e-5f && t0 < 1f - 1e-5f && t1 > 1e-5f;
        }

        public bool Contains(Vector3 p)
        {
            if (p.x < Min.x || p.x > Max.x || p.y < Min.y || p.y > Max.y || p.z < Min.z || p.z > Max.z) return false;
            for (int i = 0; i < N.Length; i++) if (Vector3.Dot(N[i], p) - D[i] > 0f) return false;
            return true;
        }

        // A lower bound on the distance from p to the solid (exact off a face, short of the truth off an edge or corner)
        public float Distance(Vector3 p)
        {
            float d = 0f;
            for (int i = 0; i < N.Length; i++) d = Mathf.Max(d, Vector3.Dot(N[i], p) - D[i]);
            return d;
        }
    }

    public sealed class Stall
    {
        public Kind Kind; public int Number, Id; public Vector2 C; public float Yaw;     // Id: the order it was placed in (its solids' Owner)
        public Vector3 Lamp;                                   // the lamp part's pivot (x, height, z)
        public readonly List<Vector3> Targets = new List<Vector3>();     // [0] the lamp's disc or bulb, then points its light reaches
        public readonly int[] Seen = new int[3];               // eye points of each gallery that see the lamp
        public string Name => Kind.ToString() + Number;
        public Vector2 At(float right, float forward) => C + Turn(right, forward, Yaw);
    }

    public sealed class Mark { public string Name; public Vector2 C; public float Yaw; public Vector3 Size; }

    public sealed class Layout
    {
        public int Seed;
        public readonly List<Stall> Stalls = new List<Stall>();
        public readonly List<Mark> Marks = new List<Mark>();
        public readonly List<Solid> Solids = new List<Solid>();
    }

    // ---- the search: seeds from FoundSeed upwards (wrapping round to SeedBase), the first that passes every check ----
    public static Layout Find(List<string> log)
    {
        var watch = Stopwatch.StartNew();
        List<Solid> fixedSolids = FixedSolids();
        if (!BarnSealed(fixedSolids, out string barnWhy)) throw new Exception("Reindeer Count: check 6 (barn seal) failed: " + barnWhy);
        log.Add("check 6: the barn is sealed but for its door (rays from a grid inside, 64 directions)");
        List<Vector3> outside = ExteriorPoints(fixedSolids), sample = new List<Vector3>();
        for (int k = 0; k < outside.Count; k += SampleStep) sample.Add(outside[k]);
        int unplaced = 0, spacing = 0, gallery = 0, exterior = 0;
        string lastWhy = "";
        for (int n = 0; n <= SeedSpan; n++)
        {
            int seed = FoundSeed + n;
            if (seed > SeedBase + SeedSpan) seed -= SeedSpan + 1;
            Layout layout = Draw(seed, fixedSolids, sample);
            if (layout == null) { unplaced++; continue; }
            if (!Spacing(layout, out lastWhy)) { spacing++; continue; }
            if (!Galleries(layout, out lastWhy)) { gallery++; continue; }
            if (!Exterior(layout, outside, out lastWhy, out List<string> table)) { exterior++; log.Add("seed " + seed + " failed check 4: " + lastWhy); continue; }
            log.Add("seed " + seed + (seed == FoundSeed ? " (FoundSeed)" : " passed: set ReindeerCountPuzzle.FoundSeed = " + seed + " so later builds start there")
                + "; tried " + (n + 1) + " seeds (" + unplaced + " would not place, " + spacing + " failed spacing, " + gallery + " the galleries, "
                + exterior + " the exterior) in " + (watch.ElapsedMilliseconds / 1000f).ToString("0.0") + " s");
            log.Add("check 5: no other stall, landmark or the fir within 1.6 m of a lamp; pads 1.6 m or more apart");
            log.Add("check 3 and 8: stalls (kind, centre, yaw; eye points of " + (21 * EyeUp.Length) + " per gallery that see its lamp: West / East / North):");
            foreach (Stall s in layout.Stalls)
                log.Add("  " + s.Name + "  (" + s.C.x.ToString("0.0") + ", " + s.C.y.ToString("0.0") + ") yaw " + s.Yaw.ToString("0") + ": "
                    + s.Seen[Gw] + " / " + s.Seen[Ge] + " / " + s.Seen[Gn]);
            log.AddRange(table);
            return layout;
        }
        throw new Exception("Reindeer Count: no seed from " + SeedBase + " to " + (SeedBase + SeedSpan) + " passes (" + unplaced + " would not place, "
            + spacing + " failed spacing, " + gallery + " the galleries, " + exterior + " the exterior; last: " + lastWhy + "; last unplaced: " + LastFail
            + "; candidates refused for own sight " + Rejects[0] + ", hiding another " + Rejects[1] + ", seen from W/E/N " + Rejects[2] + "/" + Rejects[3] + "/" + Rejects[4]
            + ", outside " + Rejects[5] + ")");
    }

    // Draws one layout: the landmarks, then N, W and E stalls in their regions and the pavilions anywhere, by rejection
    public static Layout Draw(int seed, List<Solid> fixedSolids, List<Vector3> drawPoints = null)
    {
        var rng = new System.Random(seed);
        float R(float lo, float hi) => lo + (hi - lo) * (float)rng.NextDouble();
        var layout = new Layout { Seed = seed };
        var h = new Vector2(HX, HZ);
        var marks = new[] { ("Hay", new Vector3(2f, 1.2f, 2f)), ("Trough", new Vector3(2.4f, 0.6f, 0.6f)), ("Sledge", new Vector3(1.6f, 0.8f, 0.8f)) };
        foreach (var (name, size) in marks)
        {
            bool placed = false;
            for (int tries = 0; tries < 300 && !placed; tries++)
            {
                var c = new Vector2(HX + R(-12f, 12f), HZ + R(-12f, 12f));
                if ((c - h).magnitude < 3.6f) continue;
                bool ok = true;
                foreach (Mark m in layout.Marks) if ((m.C - c).magnitude < 3.5f) ok = false;
                if (!ok) continue;
                layout.Marks.Add(new Mark { Name = name, C = c, Yaw = R(0f, 180f), Size = size });
                placed = true;
            }
            if (!placed) return null;
        }
        layout.Solids.AddRange(fixedSolids);
        AddLandmarks(layout);

        // A candidate is kept only if its own gallery still sees its lamp from three standing points or more, it takes
        // no stall already placed below three, and no other gallery sees its light (more stalls only hide more, so a
        // candidate that passes now still passes at the end; the full checks run again on the finished layout)
        Vector3[][,,] eyes = { Eyes(Gw), Eyes(Ge), Eyes(Gn) };
        var seen = new List<bool[][]>();          // per placed stall and gallery: which of the 7 standing points see its lamp
        int[] kindsSeen = drawPoints == null ? null : new int[drawPoints.Count];      // per sample point: the kinds it sees so far
        Kind[] order = { Kind.N, Kind.W, Kind.E, Kind.P };
        int[] counts = { 6, 6, 6, 2 };
        for (int o = 0; o < order.Length; o++)
            for (int i = 0; i < counts[o]; i++)
            {
                Kind kind = order[o];
                bool placed = false;
                Solid[] all = Pick(layout, false), sure = Pick(layout, true);
                for (int tries = 0; tries < Tries && !placed; tries++)
                {
                    var c = new Vector2(HX + R(-Spread, Spread), HZ + R(-Spread, Spread));
                    if (kind == Kind.N && c.y > NorthMaxZ) continue;
                    if ((c - h).magnitude < FirClear) continue;
                    bool ok = true;
                    foreach (Stall s in layout.Stalls) if ((s.C - c).magnitude < MinApart) { ok = false; break; }
                    foreach (Mark m in layout.Marks) if ((m.C - c).magnitude < MarkClear) ok = false;
                    if (!ok) continue;
                    float yaw = kind == Kind.W ? Aim(c, new Vector2(-17.3f, 9f)) : kind == Kind.E ? Aim(c, new Vector2(17.3f, 9f))
                        : kind == Kind.N ? Aim(c, new Vector2(0f, 26.3f)) : 0f;
                    var st = new Stall { Kind = kind, C = c, Yaw = yaw, Id = layout.Stalls.Count };
                    var mine = new List<Solid>();
                    AddStall(mine, st);
                    var mineCheck = mine.FindAll(s => s.Check);
                    Solid[] withAll = Concat(all, mineCheck, false), withSure = Concat(sure, mineCheck, true);
                    // its own galleries see it
                    var vis = new bool[3][];
                    for (int g = 0; g < 3 && ok; g++)
                    {
                        if (!Sees(g, kind)) continue;
                        vis[g] = new bool[7];
                        int count = 0;
                        for (int k = 0; k < 7; k++) if (vis[g][k] = Visible(eyes[g][1, k, Stand], st.Targets[0], withAll)) count++;
                        if (count < 3) { ok = false; Rejects[0]++; }
                    }
                    // it hides no placed stall below three points
                    for (int j = 0; j < layout.Stalls.Count && ok; j++)
                        for (int g = 0; g < 3 && ok; g++)
                        {
                            if (seen[j][g] == null) continue;
                            int count = 0;
                            for (int k = 0; k < 7; k++) if (seen[j][g][k] && !HitsAny(mineCheck, eyes[g][1, k, Stand], layout.Stalls[j].Targets[0])) count++;
                            if (count < 3) { ok = false; Rejects[1]++; }
                        }
                    // no other gallery sees its light
                    for (int g = 0; g < 3 && ok; g++)
                    {
                        if (Sees(g, kind) || (kind == Kind.P && g == Gn && !StrictPavilions)) continue;
                        foreach (Vector3 eye in eyes[g])
                        {
                            foreach (Vector3 t in st.Targets) if (Visible(eye, t, withSure)) { ok = false; Rejects[2 + g]++; break; }
                            if (!ok) break;
                        }
                    }
                    // no point outside (a sample of check 4's) that sees it already sees a kind it must not go with: each
                    // placed stall's footprint was taken with fewer stalls in the way, so footprints only shrink later
                    bool[] foot = null;
                    if (ok && drawPoints != null)
                    {
                        foot = new bool[drawPoints.Count];
                        int bit = 1 << (int)kind;
                        for (int k = 0; k < drawPoints.Count && ok; k++)
                        {
                            foreach (Vector3 t in st.Targets)
                                if (Visible(drawPoints[k], t, withSure)) { foot[k] = true; break; }
                            if (foot[k] && Conflict(kindsSeen[k] | bit)) { ok = false; Rejects[5]++; }
                        }
                    }
                    if (!ok) continue;
                    if (foot != null)
                        for (int k = 0; k < foot.Length; k++) if (foot[k]) kindsSeen[k] |= 1 << (int)kind;
                    for (int j = 0; j < layout.Stalls.Count; j++)
                        for (int g = 0; g < 3; g++)
                            if (seen[j][g] != null)
                                for (int k = 0; k < 7; k++) if (seen[j][g][k] && HitsAny(mineCheck, eyes[g][1, k, Stand], layout.Stalls[j].Targets[0])) seen[j][g][k] = false;
                    layout.Stalls.Add(st);
                    layout.Solids.AddRange(mine);
                    seen.Add(vis);
                    placed = true;
                }
                if (!placed) { LastFail = kind + " stall " + (i + 1); return null; }
            }
        // number each kind from north to south, then west to east, so the names read in order on the plan
        layout.Stalls.Sort((p, q) => p.Kind != q.Kind ? p.Kind.CompareTo(q.Kind) : p.C.y != q.C.y ? q.C.y.CompareTo(p.C.y) : p.C.x.CompareTo(q.C.x));
        for (int i = 0, n = 0; i < layout.Stalls.Count; i++)
        {
            if (i > 0 && layout.Stalls[i].Kind != layout.Stalls[i - 1].Kind) n = 0;
            layout.Stalls[i].Number = ++n;
        }
        return layout;
    }

    static Solid[] Concat(Solid[] a, List<Solid> extra, bool sureOnly)
    {
        var list = new List<Solid>(a);
        foreach (Solid s in extra) if (!sureOnly || s.Sure) list.Add(s);
        return list.ToArray();
    }

    static bool HitsAny(List<Solid> solids, Vector3 a, Vector3 b2)
    {
        Vector3 d = b2 - a;
        foreach (Solid s in solids) if (s.Hits(a, d)) return true;
        return false;
    }

    static Solid Add(List<Solid> into, Solid s, int owner, bool check = true, bool sure = true)
    {
        s.Owner = owner; s.Check = check; s.Sure = sure;
        into.Add(s);
        return s;
    }

    // A stall's walls, roof, lamp and sight targets, and its reindeer
    static void AddStall(List<Solid> into, Stall s)
    {
        const string g = "Inside";
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        float back = -StallD / 2f + StallWall;                   // the back wall's inner face (-1.25)
        if (s.Kind != Kind.P)
        {
            Add(into, Solid.Box(s.At(0f, -StallD / 2f + StallWall / 2f), 0f, new Vector3(StallW, StallRoof, StallWall), s.Yaw, wall, g), s.Id);
            float side = StallW / 2f - StallWall / 2f, len = StallD - StallWall;
            Add(into, Solid.Box(s.At(-side, back + len / 2f), 0f, new Vector3(StallWall, StallRoof, len), s.Yaw, wall, g), s.Id);
            Add(into, Solid.Box(s.At(side, back + len / 2f), 0f, new Vector3(StallWall, StallRoof, len), s.Yaw, wall, g), s.Id);
            Add(into, Solid.Box(s.At(0f, 0f), StallRoof, new Vector3(StallW, 0.15f, StallD), s.Yaw, roof, g), s.Id);
            s.Lamp = V(s.At(0f, back + LampOut), LampH);
            s.Targets.Add(V(s.At(0f, back + DiscOut + 0.01f), LampH));                     // the lamp's disc
            s.Targets.Add(V(s.At(-0.6f, back + 0.02f), LampH));                            // the back wall beside it
            s.Targets.Add(V(s.At(0.6f, back + 0.02f), LampH));
            float inner = StallW / 2f - StallWall - 0.02f;
            s.Targets.Add(V(s.At(-inner, back + 1.0f), LampH));                            // the side walls' inner faces
            s.Targets.Add(V(s.At(inner, back + 1.0f), LampH));
            s.Targets.Add(V(s.At(0f, back + 0.8f), StallRoof - 0.02f));                    // the roof's underside
        }
        else
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Add(into, Solid.Box(s.At(sx * PavHalf, sz * PavHalf), 0f, new Vector3(PavPost, StallRoof, PavPost), s.Yaw, wall, g), s.Id);
            Add(into, Solid.Box(s.At(0f, 0f), StallRoof, new Vector3(2 * PavHalf + 0.3f, 0.15f, 2 * PavHalf + 0.3f), s.Yaw, roof, g), s.Id);
            s.Lamp = V(s.C, PavPivot);
            s.Targets.Add(V(s.C, PavBulb));
            float inner = PavHalf - PavPost / 2f - 0.02f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    s.Targets.Add(V(s.At(sx * inner, sz * inner), 2.0f));
        }
        Reindeer(into, s);
    }

    // A reindeer lying down, its head towards the back wall, 0.35 m off the lamp's axis; top of the antlers 1.6 m.
    // Drawn from small boxes; for the checks, its body surely blocks and a generous box makes "must see" harder.
    static void Reindeer(List<Solid> into, Stall s)
    {
        const string g = "Inside";
        var brown = new Color(0.45f, 0.3f, 0.18f);
        var dark = new Color(0.16f, 0.11f, 0.08f);
        float x = 0.35f;
        void Part(float right, float forward, float y0, Vector3 size, Color c) =>
            Add(into, Solid.Box(s.At(x + right, forward), y0, size, s.Yaw, c, g), s.Id, false);
        Part(0f, 0.25f, 0.02f, new Vector3(0.5f, 0.42f, 1.1f), brown);                    // body
        foreach (float side in new[] { -0.2f, 0.2f })
        {
            Part(side, 0.6f, 0.02f, new Vector3(0.12f, 0.12f, 0.35f), brown * 0.85f);     // folded legs
            Part(side, -0.1f, 0.02f, new Vector3(0.12f, 0.12f, 0.35f), brown * 0.85f);
            Part(side * 0.5f, -0.42f, 1.0f, new Vector3(0.04f, 0.6f, 0.04f), IsleKit.Cream);    // antlers
            Part(side * 0.9f, -0.42f, 1.35f, new Vector3(0.16f, 0.04f, 0.04f), IsleKit.Cream);
        }
        Part(0f, -0.38f, 0.3f, new Vector3(0.16f, 0.55f, 0.18f), brown);                  // neck
        Part(0f, -0.5f, 0.82f, new Vector3(0.2f, 0.2f, 0.36f), brown);                    // head
        Part(0f, -0.7f, 0.86f, new Vector3(0.08f, 0.08f, 0.06f), dark);                   // nose (dark: red is the lamps')
        Part(0f, 0.82f, 0.3f, new Vector3(0.1f, 0.1f, 0.08f), IsleKit.Cream);             // tail
        Add(into, Solid.Box(s.At(x, 0.25f), 0.02f, new Vector3(0.5f, 0.42f, 1.1f), s.Yaw, Color.white, ""), s.Id);
        Add(into, Solid.Box(s.At(x, 0.05f), 0f, new Vector3(0.6f, 1.7f, 1.6f), s.Yaw, Color.white, ""), s.Id, true, false);
    }

    // The hay stack, the frozen trough and the sledge (drawn and checked), and the fir as check volumes
    static void AddLandmarks(Layout layout)
    {
        const string g = "Inside";
        foreach (Mark m in layout.Marks)
        {
            if (m.Name == "Hay")
            {
                Add(layout.Solids, Solid.Box(m.C, 0f, m.Size, m.Yaw, new Color(0.82f, 0.68f, 0.34f), g), -2);
                Add(layout.Solids, Solid.Box(m.C, m.Size.y, new Vector3(1.6f, 0.25f, 1.6f), m.Yaw, new Color(0.78f, 0.64f, 0.3f), g), -2, false);
            }
            else if (m.Name == "Trough")
            {
                Add(layout.Solids, Solid.Box(m.C, 0f, m.Size, m.Yaw, IsleKit.Wood, g), -2);
                Add(layout.Solids, Solid.Box(m.C, m.Size.y, new Vector3(m.Size.x - 0.16f, 0.02f, m.Size.z - 0.16f), m.Yaw, IsleKit.Ice, g), -2, false);
            }
            else
            {
                Add(layout.Solids, Solid.Box(m.C, 0.15f, new Vector3(m.Size.x, 0.45f, m.Size.z), m.Yaw, IsleKit.Green, g), -2);
                foreach (float side in new[] { -0.32f, 0.32f })
                {
                    Vector2 off = Turn(0f, side, m.Yaw);      // the runners along the sledge's length (its local x)
                    Add(layout.Solids, Solid.Box(m.C + off, 0f, new Vector3(m.Size.x + 0.2f, 0.15f, 0.06f), m.Yaw, IsleKit.Gold, g), -2, false);
                }
                Add(layout.Solids, Solid.Box(m.C, 0f, new Vector3(m.Size.x, 0.8f, m.Size.z), m.Yaw, Color.white, ""), -2, true, false);
            }
        }
        // the fir: its sure core, and a generous cone for "must see"
        Add(layout.Solids, Solid.Frustum(new Vector2(HX, HZ), 0.7f, 0.85f, 4.2f, 0.1f, ""), -2);
        Add(layout.Solids, Solid.Frustum(new Vector2(HX, HZ), 0.5f, 1.3f, 4.35f, 0.15f, ""), -2, true, false);
    }

    // Everything that never moves: the shed's shell and floor, the galleries, the vestibule, the barn and the pad
    public static List<Solid> FixedSolids()
    {
        var list = new List<Solid>();
        Solid Add2(Solid s, bool check = true, bool sure = true) { s.Check = check; s.Sure = sure; list.Add(s); return s; }
        Color wood = IsleKit.Wood, dark = IsleKit.DarkWood;
        const float low = -0.3f;
        float o = ShedOut, i = ShedIn;

        Add2(Solid.Block(-27f, 27f, -1.5f, 0f, -35f, 33f, IsleKit.Stone, "Pad"), false);

        // ---- the shed: walls with the three window bands and the Way Out's frame, mullions, roof and ridge ----
        const string sh = "Shed";
        foreach (float sx in new[] { -1f, 1f })
        {
            float a = sx * o, c = sx * i;
            Add2(Solid.Block(a, c, low, Eaves, ShedZ0, WinZ0, wood, sh));
            Add2(Solid.Block(a, c, low, Eaves, WinZ1, ShedZ1, wood, sh));
            Add2(Solid.Block(a, c, low, Sill, WinZ0, WinZ1, wood, sh));
            Add2(Solid.Block(a, c, Head, Eaves, WinZ0, WinZ1, wood, sh));
            for (int k = 1; k < 8; k++)
            {
                float m1 = WinZ0 + k * (PaneW + Mullion), m0 = m1 - Mullion;
                Add2(Solid.Block(a, c, Sill, Head, m0, m1, dark, sh));
            }
        }
        Add2(Solid.Block(-i, -NWinX, low, Eaves, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(NWinX, i, low, Eaves, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(-NWinX, NWinX, low, Sill, ShedInZ1, ShedZ1, wood, sh));
        Add2(Solid.Block(-NWinX, NWinX, Head, Eaves, ShedInZ1, ShedZ1, wood, sh));
        for (int k = 1; k < 8; k++)
        {
            float m1 = -NWinX + k * (PaneW + Mullion), m0 = m1 - Mullion;
            Add2(Solid.Block(m0, m1, Sill, Head, ShedInZ1, ShedZ1, dark, sh));
        }
        Add2(Solid.Block(-i, GateX0, low, Eaves, ShedZ0, ShedInZ0, wood, sh));
        Add2(Solid.Block(GateX1, i, low, Eaves, ShedZ0, ShedInZ0, wood, sh));
        Add2(Solid.Block(GateX0, GateX1, GateTop, Eaves, ShedZ0, ShedInZ0, wood, sh));
        Add2(Solid.Block(-o - 0.15f, o + 0.15f, Eaves, RoofTop, ShedZ0 - 0.15f, ShedZ1 + 0.15f, dark, sh));
        Add2(Solid.ExtrudeX(-o - 0.15f, o + 0.15f, new[] { new Vector2(ShedZ0 - 0.15f, RoofTop), new Vector2(ShedZ1 + 0.15f, RoofTop), new Vector2(HZ, Ridge) }, dark, sh));
        Add2(Solid.Block(-i, i, -0.1f, 0.02f, ShedInZ0, ShedInZ1, dark, "Inside"));

        // ---- the galleries (rails and posts only make "must see" harder: they are not counted on to block) ----
        foreach (int gal in new[] { Gw, Ge })
        {
            float sx = gal == Gw ? -1f : 1f;
            string g = gal == Gw ? "West Gallery" : "East Gallery";
            float wallX = sx * o, outX = sx * (o + 2f);
            Add2(Solid.Block(wallX, outX, Deck - 0.2f, Deck, 0f, 18f, wood, g), true, false);
            foreach (float z in new[] { 0.1f, 3f, 6f, 9f, 12f, 15f, 17.9f })
            {
                Add2(Solid.Block(outX - sx * 0.15f, outX, low, Deck + 1.0f, z - 0.075f, z + 0.075f, dark, g), true, false);    // outer posts, up to the top rail
                Add2(Solid.Block(wallX + sx * 0.05f, wallX + sx * 0.2f, low, Deck - 0.2f, z - 0.075f, z + 0.075f, dark, g), true, false);
            }
            foreach (float r in new[] { 0.45f, 0.95f })
            {
                Add2(Solid.Block(outX - sx * 0.08f, outX, Deck + r - 0.05f, Deck + r + 0.05f, 0f, 18f, wood, g), true, false);          // outer edge
                Add2(Solid.Block(wallX, outX, Deck + r - 0.05f, Deck + r + 0.05f, 17.92f, 18f, wood, g), true, false);                 // north end
                Add2(Solid.Block(wallX, sx * (o + 0.8f), Deck + r - 0.05f, Deck + r + 0.05f, 0f, 0.08f, wood, g), true, false);        // south end, beside the stair
            }
            Add2(Solid.Block(sx * (o + 0.8f) - sx * 0.15f, sx * (o + 0.8f), Deck - 0.2f, Deck + 1.0f, 0f, 0.15f, dark, g), true, false);   // post beside the stair
            for (int s = 0; s < StairSteps; s++)
                Add2(Solid.Block(sx * (o + 0.8f), outX, low, Deck * (s + 1) / StairSteps, -0.3f * StairSteps + 0.3f * s, -0.3f * StairSteps + 0.3f * (s + 1), IsleKit.Stone, g), true, false);
            // the bars and the mic on the wall
            float bx0 = wallX, bx1 = wallX + sx * BarBackPure * 0.4f;
            Add2(Solid.Block(bx0, bx1, Deck + 0.55f, Deck + 1.05f, -0.47f, 0.97f, Color.white, ""), true, false);
            Add2(Solid.Block(wallX, wallX + sx * 0.08f, Deck + 0.29f, Deck + 0.61f, MicSideZ - 0.1f, MicSideZ + 0.1f, Color.white, ""), true, false);
        }
        {
            const string g = "North Gallery";
            Add2(Solid.Block(-9f, 9f, Deck - 0.2f, Deck, ShedZ1, ShedZ1 + 2f, wood, g), true, false);
            foreach (float x in new[] { -8.9f, -6f, -3f, 0f, 3f, 6f, 8.9f })
            {
                Add2(Solid.Block(x - 0.075f, x + 0.075f, low, Deck + 1.0f, ShedZ1 + 1.85f, ShedZ1 + 2f, dark, g), true, false);
                Add2(Solid.Block(x - 0.075f, x + 0.075f, low, Deck - 0.2f, ShedZ1 + 0.05f, ShedZ1 + 0.2f, dark, g), true, false);
            }
            foreach (float r in new[] { 0.45f, 0.95f })
            {
                Add2(Solid.Block(-9f, 9f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1 + 1.92f, ShedZ1 + 2f, wood, g), true, false);      // north edge
                Add2(Solid.Block(8.92f, 9f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1, ShedZ1 + 2f, wood, g), true, false);           // east end
                Add2(Solid.Block(-9f, -8.92f, Deck + r - 0.05f, Deck + r + 0.05f, ShedZ1, 26.1f, wood, g), true, false);               // west end, beside the stair
            }
            Add2(Solid.Block(-9f, -8.85f, Deck - 0.2f, Deck + 1.0f, 25.95f, 26.1f, dark, g), true, false);
            for (int s = 0; s < StairSteps; s++)
                Add2(Solid.Block(-9f - 0.3f * StairSteps + 0.3f * s, -9f - 0.3f * StairSteps + 0.3f * (s + 1), low, Deck * (s + 1) / StairSteps, 26.1f, 27.3f, IsleKit.Stone, g), true, false);
            Add2(Solid.Block(NorthBarXPure - 0.72f, NorthBarXPure + 0.72f, Deck + 0.55f, Deck + 1.05f, ShedZ1, ShedZ1 + 0.04f, Color.white, ""), true, false);
            Add2(Solid.Block(MicNorthX - 0.1f, MicNorthX + 0.1f, Deck + 0.29f, Deck + 0.61f, ShedZ1, ShedZ1 + 0.08f, Color.white, ""), true, false);
        }

        // ---- the Way Out's vestibule: a baffle and a roof, so no straight line runs from outside to the gate ----
        {
            const string g = "Vestibule";
            Add2(Solid.Block(8.5f, 8.65f, low, 3.0f, -10.7f, ShedZ0, wood, g));
            Add2(Solid.Block(8.5f, 8.65f, 2.4f, 3.0f, -12.15f, -10.7f, wood, g));             // over the exit
            Add2(Solid.Block(8.5f, 12.5f, low, 3.0f, -12.3f, -12.15f, wood, g));
            Add2(Solid.Block(12.35f, 12.5f, low, 3.0f, -12.15f, ShedZ0, wood, g));
            Add2(Solid.Block(8.65f, 11.0f, low, 3.0f, -9.675f, -9.525f, wood, g));             // the baffle
            Add2(Solid.Block(8.4f, 12.6f, 3.0f, 3.2f, -12.4f, ShedZ0 + 0.25f, dark, g));       // roof, into the shed's wall
        }

        // ---- the Tally Barn: walls, a flat ceiling under the gable (the seal), closed gables, roof, floor slab ----
        {
            const string g = "Barn";
            const float top = BarnWall + 0.05f;
            Add2(Solid.Block(-BarnX, BarnX, low, top, BarnInZ1, BarnZ1, wood, g));
            Add2(Solid.Block(-BarnX, -DoorHalf, low, top, BarnZ0, BarnInZ0, wood, g));
            Add2(Solid.Block(DoorHalf, BarnX, low, top, BarnZ0, BarnInZ0, wood, g));
            Add2(Solid.Block(-DoorHalf, DoorHalf, DoorTop, top, BarnZ0, BarnInZ0, wood, g));   // lintel
            Add2(Solid.Block(-DoorHalf, DoorHalf, low, Slab, BarnZ0, BarnInZ0, wood, g));      // under the door
            Add2(Solid.Block(-BarnX, -BarnIn, low, top, BarnInZ0, BarnInZ1, wood, g));
            Add2(Solid.Block(BarnIn, BarnX, low, top, BarnInZ0, BarnInZ1, wood, g));
            Add2(Solid.Block(-BarnX, BarnX, BarnWall, BarnWall + 0.15f, BarnZ0, BarnZ1, dark, g));
            float mid = (BarnZ0 + BarnZ1) / 2f;
            foreach (float sx in new[] { -1f, 1f })
                Add2(Solid.ExtrudeX(Mathf.Min(sx * BarnX, sx * BarnIn), Mathf.Max(sx * BarnX, sx * BarnIn),
                    new[] { new Vector2(BarnZ0, BarnWall + 0.1f), new Vector2(BarnZ1, BarnWall + 0.1f), new Vector2(mid, BarnRidge + 0.05f) }, wood, g));
            float slope = (BarnRidge - BarnWall) / (mid - BarnZ0), eave = BarnWall - 0.4f * slope;
            Add2(Solid.ExtrudeX(-BarnX - 0.3f, BarnX + 0.3f, new[] { new Vector2(BarnZ0 - 0.4f, eave), new Vector2(mid, BarnRidge), new Vector2(mid, BarnRidge + 0.2f), new Vector2(BarnZ0 - 0.4f, eave + 0.2f) }, dark, g));
            Add2(Solid.ExtrudeX(-BarnX - 0.3f, BarnX + 0.3f, new[] { new Vector2(BarnZ1 + 0.4f, eave), new Vector2(mid, BarnRidge), new Vector2(mid, BarnRidge + 0.2f), new Vector2(BarnZ1 + 0.4f, eave + 0.2f) }, dark, g));
            Add2(Solid.Block(-BarnIn, BarnIn, -0.1f, Slab, BarnInZ0, BarnInZ1, IsleKit.Wood, "Barn Floor"));
        }
        return list;
    }
    const float BarBackPure = 0.101f, NorthBarXPure = -8.85f;

    // Where a sight line from outside enters the shed: true only through a window band or the Way Out's frame (the
    // glass is clear and the gate is counted as an open hole). The roof, walls and ridge stop every other line.
    static bool EntersByOpening(Vector3 a, Vector3 b2)
    {
        Vector3 d = b2 - a;
        float tEnter = -1f; int axis = -1;
        float[] lo = { -ShedOut, -10f, ShedZ0 }, hi = { ShedOut, RoofTop, ShedZ1 };
        for (int k = 0; k < 3; k++)
        {
            float ak = Ax(a, k), dk = Ax(d, k);
            if (ak >= lo[k] && ak <= hi[k]) continue;                  // inside this slab already
            if (Mathf.Abs(dk) < 1e-9f) return false;
            float t = ((ak < lo[k]) ? lo[k] - ak : hi[k] - ak) / dk;
            if (t > tEnter) { tEnter = t; axis = k; }
        }
        if (axis < 0 || tEnter < 0f || tEnter > 1f) return false;
        Vector3 p = a + d * tEnter;
        if (axis == 0) return p.z >= WinZ0 && p.z <= WinZ1 && p.y >= Sill && p.y <= Head;
        if (axis == 2 && a.z > ShedZ1) return p.x >= -NWinX && p.x <= NWinX && p.y >= Sill && p.y <= Head;
        if (axis == 2 && a.z < ShedZ0) return p.x >= GateX0 && p.x <= GateX1 && p.y >= 0f && p.y <= GateTop;
        return false;
    }

    static bool Visible(Vector3 eye, Vector3 target, Solid[] obstacles)
    {
        if (!EntersByOpening(eye, target)) return false;
        Vector3 d = target - eye;
        foreach (Solid s in obstacles) if (s.Hits(eye, d)) return false;
        return true;
    }

    // The galleries' eye points: 3 across the deck, 7 along it, at the heights in EyeUp above it
    public static Vector3[,,] Eyes(int g)
    {
        var e = new Vector3[3, 7, EyeUp.Length];
        float[] up = EyeUp;
        for (int a = 0; a < 3; a++)
            for (int i = 0; i < 7; i++)
                for (int h = 0; h < up.Length; h++)
                {
                    float along = 0.5f + 17f * i / 6f, across = (a - 1) * 0.7f, y = Deck + up[h];
                    e[a, i, h] = g == Gw ? new Vector3(-17.3f + across, y, along) : g == Ge ? new Vector3(17.3f - across, y, along)
                        : new Vector3(-8.5f + 17f * i / 6f, y, 26.3f - across);
                }
        return e;
    }

    static Solid[] Pick(Layout layout, bool sureOnly)
    {
        var list = new List<Solid>();
        foreach (Solid s in layout.Solids) if (s.Check && (!sureOnly || s.Sure)) list.Add(s);
        return list.ToArray();
    }

    // Check 5: nothing of another stall, the fir or a landmark within 1.6 m of a lamp; pads apart and inside the barn
    public static bool Spacing(Layout layout, out string why)
    {
        why = "";
        for (int i = 0; i < layout.Stalls.Count; i++)
        {
            Stall s = layout.Stalls[i];
            Vector3 lamp = s.Kind == Kind.P ? V(s.C, PavBulb) : s.Targets[0];
            foreach (Solid o in layout.Solids)
            {
                if (o.Owner == s.Id || o.Owner == -1 || !o.Sure) continue;      // not its own stall, the shell, or generous check volumes
                if (o.Distance(lamp) < 1.6f) { why = s.Name + "'s lamp is within 1.6 m of " + (o.Owner >= 0 ? layout.Stalls.Find(q => q.Id == o.Owner).Name : "a landmark"); return false; }
            }
            Vector2 pad = PlanPoint(s.C);
            if (Mathf.Abs(pad.x) > BarnIn - 0.6f - 0.5f || pad.y < BarnInZ0 + 1.1f || pad.y > BarnInZ1 - 1.1f) { why = "pad " + s.Name + " is too near the barn's wall"; return false; }
            for (int j = 0; j < i; j++)
                if ((PlanPoint(layout.Stalls[j].C) - pad).magnitude < 1.6f - 1e-3f) { why = "pads " + layout.Stalls[j].Name + " and " + s.Name + " are under 1.6 m apart"; return false; }
        }
        return true;
    }

    // Check 3: every lamp seen from its own gallery (the disc, from the stand eye on the deck's centre line, at three
    // points along it or more), and no light of it from any eye point of another gallery
    public static bool Galleries(Layout layout, out string why)
    {
        why = "";
        Solid[] all = Pick(layout, false), sure = Pick(layout, true);
        var eyes = new[] { Eyes(Gw), Eyes(Ge), Eyes(Gn) };
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                if (!Sees(g, s.Kind)) continue;
                int count = 0;
                for (int i = 0; i < 7; i++) if (Visible(eyes[g][1, i, Stand], s.Targets[0], all)) count++;
                if (count < 3) { why = s.Name + " is seen from only " + count + " of 7 standing points on the " + GalleryName(g) + " gallery"; return false; }
            }
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                if (Sees(g, s.Kind) || (s.Kind == Kind.P && g == Gn && !StrictPavilions)) continue;
                foreach (Vector3 eye in eyes[g])
                    foreach (Vector3 t in s.Targets)
                        if (Visible(eye, t, sure)) { why = s.Name + "'s light is seen from the " + GalleryName(g) + " gallery at (" + eye.x.ToString("0.0") + ", " + eye.y.ToString("0.0") + ", " + eye.z.ToString("0.0") + ")"; return false; }
            }
        // for the table: eye points of each gallery that see each lamp's disc
        foreach (Stall s in layout.Stalls)
            for (int g = 0; g < 3; g++)
            {
                s.Seen[g] = 0;
                foreach (Vector3 eye in eyes[g]) if (Visible(eye, s.Targets[0], all)) s.Seen[g]++;
            }
        return true;
    }

    // The exterior eye points: outside the shed's box and every sure solid, from H - 60 to H + 60, 0.6 to 40 m up;
    // 1 m apart (0.5 m in height) within 6 m of a window band or the Way Out's frame, 3 m (2 m) beyond
    public static List<Vector3> ExteriorPoints(List<Solid> fixedSolids)
    {
        var sure = new List<Solid>();
        foreach (Solid s in fixedSolids) if (s.Check && s.Sure) sure.Add(s);
        var points = new List<Vector3>();
        bool Keep(Vector3 p)
        {
            if (p.x > -ShedOut && p.x < ShedOut && p.z > ShedZ0 && p.z < ShedZ1 && p.y < Ridge + 0.1f) return false;
            foreach (Solid s in sure) if (s.Contains(p)) return false;
            return true;
        }
        for (int i = 0; i <= 40; i++)
            for (int j = 0; j <= 40; j++)
                for (int k = 0; k <= 20; k++)
                {
                    var p = new Vector3(HX - 60f + 3f * i, k == 20 ? 40f : 0.6f + 2f * k, HZ - 60f + 3f * j);
                    if (Keep(p)) points.Add(p);
                }
        // near each opening: the window bands (side walls, north wall) and the gate's frame
        void Near(Vector3 c0, Vector3 c1, Vector3 outward)
        {
            for (int u = 0; u <= 6; u++)
                for (float a = -6f; a <= 6f + Vector3.Distance(c0, c1) + 1e-3f; a += 1f)
                    for (float y = 0.6f; y <= Mathf.Max(c0.y, c1.y) + 6f + 1e-3f; y += 0.5f)
                    {
                        Vector3 along = (c1 - c0); along.y = 0f; along = along.normalized;
                        Vector3 p = new Vector3(c0.x, y, c0.z) + along * a + outward * (0.05f + u);
                        // the distance to the opening's rectangle
                        float len = new Vector2(c1.x - c0.x, c1.z - c0.z).magnitude, s = Mathf.Clamp(a, 0f, len);
                        float dy = y < Mathf.Min(c0.y, c1.y) ? Mathf.Min(c0.y, c1.y) - y : y > Mathf.Max(c0.y, c1.y) ? y - Mathf.Max(c0.y, c1.y) : 0f;
                        float dist = Mathf.Sqrt((a - s) * (a - s) + (0.05f + u) * (0.05f + u) + dy * dy);
                        if (dist <= 6f && Keep(p)) points.Add(p);
                    }
        }
        Near(new Vector3(-ShedOut, Sill, WinZ0), new Vector3(-ShedOut, Head, WinZ1), new Vector3(-1f, 0f, 0f));
        Near(new Vector3(ShedOut, Sill, WinZ0), new Vector3(ShedOut, Head, WinZ1), new Vector3(1f, 0f, 0f));
        Near(new Vector3(-NWinX, Sill, ShedZ1), new Vector3(NWinX, Head, ShedZ1), new Vector3(0f, 0f, 1f));
        Near(new Vector3(GateX0, 0f, ShedZ0), new Vector3(GateX1, GateTop, ShedZ0), new Vector3(0f, 0f, -1f));
        return points;
    }

    // Kinds seen together that would leak an answer in some lobby: W with E (3 and 4 players), or two of W, E and N
    // (4 players). Pavilions are dark in 4-player lobbies, so they go with either W or E; StrictPavilions also forbids
    // a pavilion with N.
    static bool Conflict(int mask)
    {
        bool w = (mask & 1) != 0, e = (mask & 2) != 0, n = (mask & 4) != 0, p = (mask & 8) != 0;
        if (w && e) return true;
        if (n && (w || e)) return true;
        return StrictPavilions && n && p;
    }

    // Check 4: no exterior point sees two kinds; counts per kind the points that see it
    public static bool Exterior(Layout layout, List<Vector3> points, out string why, out List<string> table)
    {
        why = "";
        table = new List<string>();
        Solid[] sure = Pick(layout, true);
        int[] perKind = new int[4];
        int conflicts = 0;
        foreach (Vector3 eye in points)
        {
            int mask = 0;
            foreach (Stall s in layout.Stalls)
            {
                int bit = 1 << (int)s.Kind;
                if ((mask & bit) != 0) continue;
                foreach (Vector3 t in s.Targets)
                    if (Visible(eye, t, sure)) { mask |= bit; break; }
            }
            for (int k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) perKind[k]++;
            if (Conflict(mask))
            {
                if (conflicts == 0) why = "the point (" + eye.x.ToString("0.0") + ", " + eye.y.ToString("0.0") + ", " + eye.z.ToString("0.0") + ") sees kinds " + MaskName(mask);
                conflicts++;
            }
        }
        table.Add("check 4: " + points.Count + " exterior points; seeing W " + perKind[0] + ", E " + perKind[1] + ", N " + perKind[2] + ", P " + perKind[3]
            + "; seeing two kinds: " + conflicts);
        return conflicts == 0;
    }

    static string MaskName(int mask)
    {
        string s = "";
        for (int k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) s += ((Kind)k).ToString();
        return s;
    }

    // Check 6: rays from a 1 m grid inside the barn, 0.4 to 2.8 m above the slab, in 64 directions: none leaves the
    // barn except through the door's frame
    public static bool BarnSealed(List<Solid> fixedSolids, out string why)
    {
        why = "";
        var barn = new List<Solid>();
        foreach (Solid s in fixedSolids) if (s.Group == "Barn" || s.Group == "Barn Floor") barn.Add(s);
        var dirs = new Vector3[64];
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < 64; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / 64f, r = Mathf.Sqrt(1f - y * y), a = golden * i;
            dirs[i] = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
        }
        for (float x = -5.5f; x <= 5.51f; x += 1f)
            for (float z = BarnInZ0 + 0.7f; z <= BarnInZ1 - 0.69f; z += 1f)
                for (float h = 0.4f; h <= 2.81f; h += 0.6f)
                {
                    var p = new Vector3(x, Slab + h, z);
                    foreach (Vector3 dir in dirs)
                    {
                        Vector3 d = dir * 30f;
                        bool blocked = false;
                        foreach (Solid s in barn) if (s.Hits(p, d)) { blocked = true; break; }
                        if (blocked) continue;
                        if (d.z >= 0f) { why = "a ray from (" + x + ", " + (Slab + h) + ", " + z + ") gets out away from the door"; return false; }
                        float t = (BarnZ0 - p.z) / d.z;
                        Vector3 q = p + d * t;
                        if (Mathf.Abs(q.x) > DoorHalf + 1e-3f || q.y < Slab - 1e-3f || q.y > DoorTop + 1e-3f)
                        { why = "a ray from (" + x + ", " + (Slab + h) + ", " + z + ") gets out at (" + q.x.ToString("0.00") + ", " + q.y.ToString("0.00") + ")"; return false; }
                    }
                }
        return true;
    }

    // Check 7: per list, the chance a team short of one spotter clears a run by guessing the stalls nobody sees.
    // Press In Order picks uniformly and only a wrong hold at the phase end fails, so a round is won with chance
    // (seen + 1) / pairs (the seen ones, or one right guess among the unseen). Limits: 0.5 % (3-player list, either
    // spotter missing) and 1 % (4-player list, any one spotter missing).
    public static bool GuessingOdds(List<(string name, List<Kind> pairs, int rounds, int lobby)> lists, out List<string> lines)
    {
        lines = new List<string> { "guessing odds (list, team, chance per round, rounds, chance per run):" };
        bool ok = true;
        foreach (var list in lists)
        {
            var galleries = new List<int> { Gw };
            if (list.lobby >= 3) galleries.Add(Ge);
            if (list.lobby >= 4) galleries.Add(Gn);
            int n = list.pairs.Count;
            if (galleries.Count == 1) { lines.Add("  " + list.name + ": one spotter + presser is the intended team (" + n + " pairs, " + list.rounds + " rounds)"); continue; }
            // every set of galleries with at least one spotter missing and at least one present
            for (int mask = 1; mask < (1 << galleries.Count) - 1; mask++)
            {
                var present = new List<int>();
                for (int i = 0; i < galleries.Count; i++) if ((mask & (1 << i)) != 0) present.Add(galleries[i]);
                int seen = 0;
                foreach (Kind k in list.pairs)
                    foreach (int g in present) if (Sees(g, k)) { seen++; break; }
                int unseen = n - seen;
                double round = (seen + (unseen > 0 ? 1.0 : 0.0)) / n, run = Math.Pow(round, list.rounds);
                int missing = galleries.Count - present.Count;
                string team = string.Join("+", present.ConvertAll(g => GalleryName(g))) + " spotter(s) + presser";
                lines.Add("  " + list.name + ": " + team + ": " + round.ToString("0.000") + " x " + list.rounds + " = " + (run * 100).ToString("0.0000") + " %");
                if (missing == 1 && list.lobby == 3 && run > 0.005) ok = false;
                if (missing == 1 && list.lobby == 4 && run > 0.01) ok = false;
            }
        }
        return ok;
    }

    // ==== PURE END ====
}
