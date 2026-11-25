// Whiteout Maze (puzzle 18), the maze of gates: a maze of snow walls seven cells by seven, too tall to see over, with
// three gates shut across the one way from its entrance to the star. A gate stands open only while somebody stands on
// one of its snow plates, somewhere else in the maze, and shuts when they step off:
//   the red plate (A), in a dead end before gate 1, holds gate 1;
//   the green plate (B), in a dead end between gates 1 and 2, holds gates 1 and 2;
//   the gold plate (C), in a dead end between gates 2 and 3, holds gates 2 and 3.
// A plate is a square kerb of its colour under a string of that colour's bulbs; a gate wears its plates' colours on
// its lintel, its cap and its lights. Beyond gate 3 is the star: walk into its ring of baubles and the gourd is won.
//
//        +---+---+---+-x-+---+---+---+
//        |           2   |           |      1 2 3   the gates
//        +   +---+   +   +---+   +---+      A B C   the plates (red, green, gold)
//        |     B |   |       |       |      *       the star
//        +   +---+   +---+   +---+   +      x       ways out (steps up to a drop: out only)
//        |   |       | C |   |       |
//        +---+   +---+   +   +   +   +      north at the top; the gantry runs east-west over
//        x       |           |   |   |      the wall line north of the middle row, to the stair
//        + 1 +---+   +---+---+   +---+      tower east of the maze
//        |   |           |           |
//        +   +---+---+ 3 +---+---+   +
//        |       | *                 |
//        +   +   +---+---+---+---+   +
//        | A |           |           |
//        +---+   +---+   +---+-x-+---+
//           west      the
//          gateway  entrance
//
// So a group leapfrogs. Two players: one stands on A while the other walks through gate 1 and on to B; B holds gate 1
// as well, so the first leaves A and follows through gates 1 and 2 to C; C holds gate 2 as well, so the second leaves
// B and walks through gates 2 and 3 to the star. Three or more: the same, and everybody but the one left on C reaches
// the star. One player alone cannot pass gate 1 (plate A is 6.5 m of walking from it). There is no clock and nothing
// to get wrong, and nobody is shut away: the walls do not block voices, and the gantry 9.5 m over the maze is a
// lookout anyone may climb, from which the whole maze, its plates and its gates show.
//
// A gate is open while ANY of its plates is stood on: the custom script HeldOpen (Assets/PuzzleScripts), one per
// gate, reads its plates and works the door. Nothing is wired to the doors, and they work whether the start switch is
// on or not. Nobody is left behind a shut gate: each stretch beyond a gate has a way out of its own through the
// outer wall (west beyond gate 1, north beyond gate 2, south beyond gate 3), where steps climb to a platform 1.6 m up
// with an open end. Off it and you are outside; no jump gets back up, so the ways out let nobody in.
//
// (Before the rework: a blindfolded walker was talked from cairn to cairn by a spotter on the gantry, from pictures a
// reader in a signal hut called out, against a clock, and a wrong dead end stopped the puzzle.)
//
// The maze is the one the old puzzle carved (depth-first, seed 2608), written out below as a map; its walls stand
// where they stood. Coordinates: design metres, maze centred at (0, 0), x east, z north; the build adds (OffX, OffZ)
// so the 34 x 46 footprint is centred on the root. Heights above the (levelled) ground.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WhiteoutMazePuzzle : PuzzleDef
{
    public override string Name => "WhiteoutMaze";
    public override Vector2 Footprint => new Vector2(34f, 46f);
    public override Vector2? FixedSite => new Vector2(-753f, -1251f);      // East Lowland flats (layout-final)

    // design -> build: the footprint runs x -14.5..19.5 and z -17.5..28.5 about the maze's centre (the ground north of
    // z 12 held the old signal hut and is empty now)
    const float OffX = -2.5f, OffZ = -5.5f;

    const int N = 7;
    const float CellSize = 2.6f, X0 = -N * CellSize / 2f;      // wall lines at X0 + 2.6 k (-9.1 .. 9.1)
    const float WallT = 0.45f, WallBody = 2.0f, Ridge = 0.35f;  // 2.35 m to the ridge: over a standing eye and a jump
    const float Sink = 0.3f, Eps = 0.001f;
    const float DeckH = 9.5f, DeckZ0 = 0.6f, DeckZ1 = 2.0f, DeckX0 = -9.4f, DeckX1 = 13.8f;
    const float FenceH = 2.6f;
    const float DoorHalf = 0.62f, DoorTop = 2.02f, LintelTop = 2.4f;      // the game's Door is 1.24 m wide, 2.02 high
    const float GatewayHalf = 1.06f;                            // the plain gateways are 2.12 m wide
    const float GatePost = 3.0f;                                // a gate's two posts, which carry its lights
    const float PlateSize = 2.0f, SensorH = 2.6f, StringH = 2.36f;
    const float DropH = 1.6f;                                   // the ways out: a jump is about 1 m
    const float StarH = 4.2f;

    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);

    // The maze, north at the top. Every cell is reached one way only (the maze is a tree). Cell (i, j) is column i
    // from the west, row j from the south.
    static readonly string[] Map =
    {
        "+---+---+---+---+---+---+---+",
        "|               |           |",      // row 6
        "+   +---+   +   +---+   +---+",
        "|       |   |       |       |",      // row 5
        "+   +---+   +---+   +---+   +",
        "|   |       |   |   |       |",      // row 4
        "+---+   +---+   +   +   +   +",
        "|       |           |   |   |",      // row 3
        "+   +---+   +---+---+   +---+",
        "|   |           |           |",      // row 2
        "+   +---+---+   +---+---+   +",
        "|       |                   |",      // row 1
        "+   +   +---+---+---+---+   +",
        "|   |           |           |",      // row 0
        "+---+---+---+---+---+---+---+",
    };

    // An opening in a wall line. 'across': the line runs east-west (z = Line(k)) and the opening is in column i;
    // otherwise the line runs north-south (x = Line(k)) and the opening is in row i. 'outward' (+1 or -1) is the way
    // out of the maze along z (or x), for the outer wall's openings.
    sealed class Opening
    {
        public readonly bool across;
        public readonly int k, i, outward;
        public readonly float half;

        public Opening(bool across, int k, int i, float half, int outward = 1)
        {
            this.across = across; this.k = k; this.i = i; this.half = half; this.outward = outward;
        }

        public float U => Mid(i);            // its middle, along the wall line
        public float Yaw => across ? 0f : 90f;
        // the design point u along the wall line and w out from it, y up
        public Vector3 At(float u, float y, float w) => across ? new Vector3(u, y, Line(k) + outward * w) : new Vector3(Line(k) + outward * w, y, u);
        // the cells on its two sides, and the one inside an outer wall's opening
        public Vector2Int Low => across ? new Vector2Int(i, k - 1) : new Vector2Int(k - 1, i);
        public Vector2Int High => across ? new Vector2Int(i, k) : new Vector2Int(k, i);
        public Vector2Int Inside => k == 0 ? High : Low;
        public bool Between(Vector2Int a, Vector2Int c) => (a == Low && c == High) || (a == High && c == Low);
    }

    // The two plain gateways in the south wall: the entrance, and the west gateway two cells along (the old west exit)
    static readonly Opening Entrance = new Opening(true, 0, 3, GatewayHalf, -1), WestGateway = new Opening(true, 0, 1, GatewayHalf, -1);
    static readonly Opening[] Gates =
    {
        new Opening(true, 3, 0, DoorHalf),       // gate 1, between (0, 2) and (0, 3)
        new Opening(false, 3, 6, DoorHalf),      // gate 2, between (2, 6) and (3, 6)
        new Opening(true, 2, 3, DoorHalf),       // gate 3, between (3, 2) and (3, 1)
    };
    // The way out of the stretch beyond each gate, in the gates' order
    static readonly Opening[] WaysOut =
    {
        new Opening(false, 0, 3, DoorHalf, -1),  // west, from (0, 3)
        new Opening(true, N, 3, DoorHalf, 1),    // north, from (3, 6)
        new Opening(true, 0, 5, DoorHalf, -1),   // south, from (5, 0)
    };
    static readonly string[] GateName = { "Gate 1", "Gate 2", "Gate 3" };         // HeldOpen finds the doors and
    static readonly string[] PlateName = { "Plate A", "Plate B", "Plate C" };     // the plates by these names
    static readonly Vector2Int[] Plates = { new Vector2Int(0, 0), new Vector2Int(1, 5), new Vector2Int(3, 4) };
    static readonly Color[] PlateColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };
    static readonly int[][] HeldBy = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2 } };      // the plates that hold each gate
    static readonly Vector2Int Star = new Vector2Int(2, 1);      // the dead end just beyond gate 3

    PuzzleBuilder b;

    static float Line(int k) => X0 + k * CellSize;
    static float Mid(int i) => X0 + CellSize / 2f + i * CellSize;
    static float X(float x) => x + OffX;
    static float Z(float z) => z + OffZ;
    Piece P(string prefab, float x, float z, float h = 0f, float yaw = 0f) => b.Part(prefab, X(x), Z(z), h, yaw);
    Piece E(string name, float x, float z, float h = 0f) => b.Empty(name, X(x), Z(z), h);
    // a design point as Kit.LightString takes a post: local x, height, local z
    static Vector3 Post(Vector3 design) => new Vector3(X(design.x), design.y, Z(design.z));

    // A mesh in design coordinates, placed at the maze's centre
    Piece AddMesh(string name, MeshBuilder mb, Material material, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(OffX, OffZ);
        return piece;
    }

    // ---- directions: 0 south (-z), 1 north (+z), 2 west (-x), 3 east (+x) ----
    static readonly int[] DX = { 0, 0, -1, 1 }, DZ = { -1, 1, 0, 0 };
    static int Opposite(int d) => d ^ 1;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(X(0f), Z(-15f));
        b.ClearGround(34f, 46f);
        // no jetpack thrust over the maze, the gantry or the tower (Winter Walk's JetpackDriver)
        IsleKit.CalmAir(b, "Whiteout Maze", 0f, 0f, new Vector3(64f, 150f, 76f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ================= the maze =================
        ReadMap(out bool[,] east, out bool[,] north);
        CheckLayout(east, north);
        var h = new List<Vector2>[N + 1];
        var v = new List<Vector2>[N + 1];
        for (int k = 0; k <= N; k++) { h[k] = Solids(true, k, east, north); v[k] = Solids(false, k, east, north); }
        BuildWalls(snowy, h, v);
        BuildGateways(snowy);
        BuildWaysOut(snowy);

        // ================= the gates: a Door, the script that holds it open, and its plates' colours =================
        var gateMarks = new MeshBuilder();
        for (int g = 0; g < Gates.Length; g++)
        {
            Opening o = Gates[g];
            Vector3 at = o.At(o.U, 0f, 0f);
            P("Door", at.x, at.z, 0f, o.Yaw).Named(GateName[g])
                .Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", false);      // Control With Functions Only: shut until told
            var holders = new List<string>();
            foreach (int p in HeldBy[g]) holders.Add(PlateName[p]);
            Component hold = b.AddCustomScript(E(GateName[g] + " Hold", at.x, at.z, GatePost), "HeldOpen");
            Sdk.Set(hold, "door", GateName[g]);
            Sdk.Set(hold, "plates", string.Join(";", holders));

            // half the lintel for each plate (all of it, with one): a band on both faces and a peaked cap, so nobody
            // stands on it and the colours show from the gantry
            int first = HeldBy[g][0], second = HeldBy[g][HeldBy[g].Length - 1];
            float u0 = o.U - DoorHalf + 0.01f, u1 = o.U + DoorHalf - 0.01f;
            foreach (float face in new[] { -1f, 1f })
            {
                Slab(gateMarks, o, u0, o.U, DoorTop + 0.06f, LintelTop - 0.06f, face * 0.24f, face * 0.285f, PlateColour[first]);
                Slab(gateMarks, o, o.U, u1, DoorTop + 0.06f, LintelTop - 0.06f, face * 0.24f, face * 0.285f, PlateColour[second]);
            }
            Prism(gateMarks, o.At(u0, LintelTop, 0f), o.At(o.U, LintelTop, 0f), 0.5f, 0.3f, PlateColour[first]);
            Prism(gateMarks, o.At(o.U, LintelTop, 0f), o.At(u1, LintelTop, 0f), 0.5f, 0.3f, PlateColour[second]);
            // six bulbs between the posts' tops, three of each plate's colour (Kit's bulb colours begin red, green, gold)
            float span = DoorHalf + 0.06f;
            Color bulb1 = Kit.BulbColors[first], bulb2 = Kit.BulbColors[second];
            Kit.LightString(b, GateName[g] + " Lights", new List<Vector3> { Post(o.At(o.U - span, GatePost, 0f)), Post(o.At(o.U + span, GatePost, 0f)) },
                0.1f, 2f * span / 6f, true, false, 201 + g, new[] { bulb1, bulb1, bulb1, bulb2, bulb2, bulb2 });
        }
        AddMesh("Gate Marks", gateMarks, bare);

        // ================= the plates: a sensor filling a dead end, a kerb of its colour, bulbs of its colour overhead =================
        var marks = new MeshBuilder();
        for (int p = 0; p < Plates.Length; p++)
        {
            float cx = Mid(Plates[p].x), cz = Mid(Plates[p].y);
            IsleKit.Sensor(b, PlateName[p], X(cx), Z(cz), new Vector3(PlateSize, SensorH, PlateSize))
                .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            Kerb(marks, cx, cz, PlateColour[p]);
            // from side wall to side wall, on their ridges
            bool opensNorthSouth = OpenSides(east, north, Plates[p].x, Plates[p].y)[0] <= 1;
            Vector3 reach = opensNorthSouth ? new Vector3(CellSize / 2f, 0f, 0f) : new Vector3(0f, 0f, CellSize / 2f);
            var middle = new Vector3(cx, StringH, cz);
            Kit.LightString(b, PlateName[p] + " Lights", new List<Vector3> { Post(middle - reach), Post(middle + reach) },
                0.15f, 0.3f, true, false, 211 + p, new[] { Kit.BulbColors[p] });
        }

        // ================= the star: the goal, in the dead end beyond gate 3 =================
        float sx = Mid(Star.x), sz = Mid(Star.y);
        Piece goal = IsleKit.Sensor(b, "Star", X(sx), Z(sz), new Vector3(PlateSize, SensorH, PlateSize))
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        goal.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");
        // a ring of baubles in the three plates' colours on the snow, and a gold star on a pole on the back wall, high
        // enough to show over the walls from outside the maze
        for (int n = 0; n < 12; n++)
        {
            float a = n * Mathf.PI / 6f;
            Kit.AddBall(marks, new Vector3(sx + Mathf.Cos(a) * 0.85f, 0.05f, sz + Mathf.Sin(a) * 0.85f), 0.1f, PlateColour[n % 3]);
        }
        int back = Opposite(OpenSides(east, north, Star.x, Star.y)[0]);
        float px = sx + DX[back] * CellSize / 2f, pz = sz + DZ[back] * CellSize / 2f;
        Blk(marks, px - 0.05f, px + 0.05f, WallBody - 0.1f, StarH - 0.3f, pz - 0.05f, pz + 0.05f, IsleKit.Cream);
        AddStar(marks, new Vector3(px, StarH, pz), 0.4f, 0.07f, 0f, IsleKit.Gold);
        AddStar(marks, new Vector3(px, StarH, pz), 0.4f, 0.07f, 90f, IsleKit.Gold);
        E("Room Light Night 1 FFD98A 4.0 1.2", sx, sz, 2.8f);      // a warm lamp over the star after dark (Winter Walk's RoomLights)
        AddMesh("Plate Marks", marks, bare, false);      // drawn only: nothing to trip on

        // ================= the gantry and its stair tower: a lookout =================
        BuildGantry(snowy, PostLattice(h, v));
        BuildTower(snowy);

        // ================= start corner and lights =================
        IsleKit.StartCorner(b, X(-5f), Z(-13.5f), 0f);
        Kit.LightRing(b, "Site Lights", X(2f), Z(5.5f), 32f, 43f, 2.6f, true, 181);
        foreach (float fz in new[] { DeckZ0, DeckZ1 })
        {
            var rail = new List<Vector3>();
            for (int s = 0; s <= 8; s++) rail.Add(new Vector3(X(Mathf.Lerp(DeckX0, DeckX1, s / 8f)), DeckH + FenceH + 0.2f, Z(fz)));
            Kit.LightString(b, fz < 1f ? "Deck Lights South" : "Deck Lights North", rail, 0.1f, 0.4f, true, false, fz < 1f ? 191 : 192);
        }
        Kit.LightString(b, "Deck Lights West", new List<Vector3> { new Vector3(X(DeckX0), DeckH + FenceH + 0.2f, Z(DeckZ0)), new Vector3(X(DeckX0), DeckH + FenceH + 0.2f, Z(DeckZ1)) },
            0.1f, 0.4f, true, false, 193);
        float arch = GatewayHalf + 0.34f;
        Kit.LightString(b, "Arch Lights", new List<Vector3> { Post(Entrance.At(Entrance.U - arch, 2.75f, 0.65f)), Post(Entrance.At(Entrance.U + arch, 2.75f, 0.65f)) },
            0.2f, 0.3f, true, false, 195);
    }

    // ================= the map =================

    // The map's passages: east[i, j] between cells (i, j) and (i + 1, j), north[i, j] between (i, j) and (i, j + 1)
    static void ReadMap(out bool[,] east, out bool[,] north)
    {
        if (Map.Length != 2 * N + 1) throw new System.Exception("WhiteoutMaze: the map needs " + (2 * N + 1) + " lines");
        foreach (string line in Map)
            if (line.Length != 4 * N + 1) throw new System.Exception("WhiteoutMaze: every line of the map needs " + (4 * N + 1) + " characters");
        east = new bool[N, N];
        north = new bool[N, N];
        for (int j = 0; j < N; j++)
        {
            int row = 2 * (N - 1 - j) + 1;
            for (int i = 0; i < N; i++)
            {
                if (i < N - 1) east[i, j] = Map[row][4 * (i + 1)] == ' ';
                if (j < N - 1) north[i, j] = Map[row - 1][4 * i + 2] == ' ';
            }
        }
    }

    // The open sides of a cell inside the maze (outer openings are not counted)
    static List<int> OpenSides(bool[,] east, bool[,] north, int i, int j)
    {
        var open = new List<int>();
        if (j > 0 && north[i, j - 1]) open.Add(0);
        if (j < N - 1 && north[i, j]) open.Add(1);
        if (i > 0 && east[i - 1, j]) open.Add(2);
        if (i < N - 1 && east[i, j]) open.Add(3);
        return open;
    }

    // Path lengths in cells from one cell to every other
    static int[,] Distances(bool[,] east, bool[,] north, Vector2Int from)
    {
        var d = new int[N, N];
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) d[i, j] = -1;
        var queue = new Queue<Vector2Int>();
        d[from.x, from.y] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (int dir in OpenSides(east, north, c.x, c.y))
            {
                var n = new Vector2Int(c.x + DX[dir], c.y + DZ[dir]);
                if (d[n.x, n.y] >= 0) continue;
                d[n.x, n.y] = d[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }
        return d;
    }

    // The stretch each cell is in: how many gates lie on the way to it from the entrance
    static int[,] Stretches(bool[,] east, bool[,] north)
    {
        var s = new int[N, N];
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) s[i, j] = -1;
        var queue = new Queue<Vector2Int>();
        Vector2Int start = Entrance.Inside;
        s[start.x, start.y] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (int dir in OpenSides(east, north, c.x, c.y))
            {
                var n = new Vector2Int(c.x + DX[dir], c.y + DZ[dir]);
                if (s[n.x, n.y] >= 0) continue;
                int gate = 0;
                foreach (Opening g in Gates) if (g.Between(c, n)) gate = 1;
                s[n.x, n.y] = s[c.x, c.y] + gate;
                queue.Enqueue(n);
            }
        }
        return s;
    }

    // The layout's rules, checked against the map on every build: every gate stands in a passage, plate A, B, C is a
    // dead end before gate 1, 2, 3 and at least 6 m of walking from each gate it holds, the star lies beyond gate 3,
    // and each way out leaves the stretch beyond its gate
    static void CheckLayout(bool[,] east, bool[,] north)
    {
        int[,] stretch = Stretches(east, north);
        string problem = null, walks = "";
        for (int g = 0; g < Gates.Length; g++)
        {
            Opening o = Gates[g];
            if (!(o.across ? north[o.i, o.k - 1] : east[o.k - 1, o.i])) problem = GateName[g] + " stands in a wall";
            if (stretch[WaysOut[g].Inside.x, WaysOut[g].Inside.y] != g + 1) problem = "way out " + (g + 1) + " does not leave the stretch beyond " + GateName[g];
            foreach (int p in HeldBy[g])
            {
                int[,] d = Distances(east, north, Plates[p]);
                float metres = (Mathf.Min(d[o.Low.x, o.Low.y], d[o.High.x, o.High.y]) + 0.5f) * CellSize;
                if (metres < 6f) problem = PlateName[p] + " is only " + Sdk.F(metres) + " m from " + GateName[g];
                walks += (walks.Length > 0 ? ", " : "") + PlateName[p] + " to " + GateName[g] + " " + Sdk.F(metres) + " m";
            }
        }
        for (int p = 0; p < Plates.Length; p++)
            if (stretch[Plates[p].x, Plates[p].y] != p || OpenSides(east, north, Plates[p].x, Plates[p].y).Count != 1)
                problem = PlateName[p] + " is not in a dead end with " + p + " gate(s) before it";
        if (stretch[Star.x, Star.y] != Gates.Length || OpenSides(east, north, Star.x, Star.y).Count != 1) problem = "the star is not in a dead end beyond the last gate";
        if (stretch[WestGateway.Inside.x, WestGateway.Inside.y] != 0) problem = "the west gateway opens beyond a gate";
        if (problem != null) throw new System.Exception("WhiteoutMaze: " + problem);
        Debug.Log("[BWM] WhiteoutMaze: gates, plates, star and ways out agree with the map; walks: " + walks);
    }

    // ================= walls =================

    static Opening OpeningAt(bool across, int k, int i)
    {
        if (Entrance.across == across && Entrance.k == k && Entrance.i == i) return Entrance;
        if (WestGateway.across == across && WestGateway.k == k && WestGateway.i == i) return WestGateway;
        foreach (Opening o in Gates) if (o.across == across && o.k == k && o.i == i) return o;
        foreach (Opening o in WaysOut) if (o.across == across && o.k == k && o.i == i) return o;
        return null;
    }

    // The solid stretches of one wall line (horizontal: z = Line(k), along x; vertical: x = Line(k), along z), merged
    // into runs. A gate's passage is walled too, but for its door; the gateways and ways out are gaps in the outer wall.
    static List<Vector2> Solids(bool horizontal, int k, bool[,] east, bool[,] north)
    {
        var parts = new List<Vector2>();
        for (int i = 0; i < N; i++)
        {
            Opening o = OpeningAt(horizontal, k, i);
            bool present = k == 0 || k == N || o != null || (horizontal ? !north[i, k - 1] : !east[k - 1, i]);
            if (!present) continue;
            if (o != null)
            {
                parts.Add(new Vector2(Line(i), o.U - o.half));
                parts.Add(new Vector2(o.U + o.half, Line(i + 1)));
            }
            else parts.Add(new Vector2(Line(i), Line(i + 1)));
        }
        var merged = new List<Vector2>();
        foreach (Vector2 p in parts)
        {
            if (merged.Count > 0 && Mathf.Abs(merged[merged.Count - 1].y - p.x) < Eps) merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, p.y);
            else merged.Add(p);
        }
        return merged;
    }

    static bool Through(List<Vector2> line, float u)
    {
        foreach (Vector2 s in line) if (s.x < u - Eps && s.y > u + Eps) return true;
        return false;
    }

    static bool Touches(List<Vector2> line, float u)
    {
        foreach (Vector2 s in line) if (s.x <= u + Eps && s.y >= u - Eps) return true;
        return false;
    }

    static int Lattice(float u)
    {
        int m = Mathf.RoundToInt((u - X0) / CellSize);
        return m >= 0 && m <= N && Mathf.Abs(Line(m) - u) < Eps ? m : -1;
    }

    // Snow-block walls: a body 2.0 m high (sunk 0.3) under a peaked 0.35 m ridge, so nobody stands on a wall top. Where
    // walls meet, a wall that another passes straight through stops at its face; at a corner the horizontal wall runs on
    // and the vertical one stops; a pyramid caps every junction no wall passes straight through. Gateway and door gaps
    // end in gables. A corridor is 2.15 m wide and a door 1.24, so the walls' stubs close the rest of a gate's passage.
    void BuildWalls(Material snowy, List<Vector2>[] h, List<Vector2>[] v)
    {
        var mb = new MeshBuilder();
        var caps = new HashSet<Vector2Int>();
        for (int pass = 0; pass < 2; pass++)
        {
            bool horizontal = pass == 0;
            List<Vector2>[] lines = horizontal ? h : v, across = horizontal ? v : h;
            for (int k = 0; k <= N; k++)
            {
                float c = Line(k);
                foreach (Vector2 run in lines[k])
                {
                    float bodyFrom = run.x, bodyTo = run.y;
                    for (int end = 0; end < 2; end++)
                    {
                        float u = end == 0 ? run.x : run.y;
                        int m = Lattice(u);
                        if (m < 0) continue;      // a gap's edge: ends exactly there
                        bool through = Through(across[m], c), touch = Touches(across[m], c);
                        float ext = through ? -WallT / 2f : touch ? (horizontal ? WallT / 2f : -WallT / 2f) : WallT / 2f;
                        if (end == 0) bodyFrom = u - ext; else bodyTo = u + ext;
                        if (!through) caps.Add(horizontal ? new Vector2Int(m, k) : new Vector2Int(k, m));
                    }
                    if (horizontal)
                    {
                        Blk(mb, bodyFrom, bodyTo, -Sink, WallBody, c - WallT / 2f, c + WallT / 2f, SnowBlock);
                        Prism(mb, new Vector3(run.x, WallBody, c), new Vector3(run.y, WallBody, c), WallT, Ridge, SnowBlock);
                    }
                    else
                    {
                        Blk(mb, c - WallT / 2f, c + WallT / 2f, -Sink, WallBody, bodyFrom, bodyTo, SnowBlock);
                        Prism(mb, new Vector3(c, WallBody, run.x), new Vector3(c, WallBody, run.y), WallT, Ridge, SnowBlock);
                    }
                }
            }
        }
        foreach (Vector2Int p in caps) Pyramid(mb, new Vector3(Line(p.x), WallBody, Line(p.y)), WallT / 2f, Ridge, SnowBlock);

        // timber frames: a gate's jambs stand on as two posts that carry its lights; a way out's lintel is capped with
        // a ridge, so nobody who jumps back from its steps stands over the doorway
        foreach (Opening o in Gates) Frame(mb, o, GatePost);
        foreach (Opening o in WaysOut)
        {
            Frame(mb, o, LintelTop);
            Prism(mb, o.At(o.U - DoorHalf - 0.13f, LintelTop, 0f), o.At(o.U + DoorHalf + 0.13f, LintelTop, 0f), 0.5f, 0.3f, IsleKit.DarkWood);
        }
        AddMesh("Maze Walls", mb, snowy);
    }

    // The timber frame of a door-sized opening: two jambs 'postTop' high and a lintel from the door's top to 2.4 m,
    // all a little thicker than the wall
    static void Frame(MeshBuilder mb, Opening o, float postTop)
    {
        foreach (float side in new[] { -1f, 1f })
            Slab(mb, o, o.U + side * (DoorHalf - 0.01f), o.U + side * (DoorHalf + 0.13f), -Sink, postTop, -0.25f, 0.25f, IsleKit.DarkWood);
        Slab(mb, o, o.U - DoorHalf + 0.01f, o.U + DoorHalf - 0.01f, DoorTop, LintelTop, -0.25f, 0.25f, IsleKit.DarkWood);
    }

    // Gantry posts on the z = 1.3 wall line (lattice row 4), only where a maze wall meets: the outer walls, and the
    // qualifying points nearest x = -3.9, 1.3 and 6.5. Returns lattice column indices.
    static List<int> PostLattice(List<Vector2>[] h, List<Vector2>[] v)
    {
        const int Row = 4;
        bool Qualifies(int k) => Touches(h[Row], Line(k)) || Touches(v[k], Line(Row));
        var posts = new List<int> { 0 };
        foreach (int target in new[] { 2, 4, 6 })
        {
            for (int d = 0; d <= N; d++)
            {
                int found = -1;
                foreach (int k in new[] { target - d, target + d })
                    if (k >= 1 && k <= N - 1 && !posts.Contains(k) && Qualifies(k)) { found = k; break; }
                if (found >= 0) { posts.Add(found); break; }
            }
        }
        posts.Add(N);
        posts.Sort();
        return posts;
    }

    // ================= gateways and ways out =================

    // The entrance's timber arch, half a metre out from the wall, and the west gateway's lower posts and lintel. Plain
    // timber: red, green and gold belong to the plates and their gates.
    void BuildGateways(Material snowy)
    {
        var mb = new MeshBuilder();
        foreach (float side in new[] { -1f, 1f })
        {
            Slab(mb, Entrance, Entrance.U + side * (GatewayHalf + 0.23f), Entrance.U + side * (GatewayHalf + 0.45f), -Sink, 2.8f, 0.39f, 0.61f, IsleKit.DarkWood);
            Slab(mb, WestGateway, WestGateway.U + side * (GatewayHalf + 0.25f), WestGateway.U + side * (GatewayHalf + 0.43f), -Sink, 2.4f, 0.41f, 0.59f, IsleKit.DarkWood);
        }
        Slab(mb, Entrance, Entrance.U - GatewayHalf - 0.45f, Entrance.U + GatewayHalf + 0.45f, 2.8f, 3.04f, 0.35f, 0.65f, IsleKit.Wood);
        Slab(mb, WestGateway, WestGateway.U - GatewayHalf - 0.43f, WestGateway.U + GatewayHalf + 0.43f, 2.4f, 2.58f, 0.4f, 0.6f, IsleKit.Wood);
        AddMesh("Gateways", mb, snowy);
    }

    // Outside each way out's doorway, timber steps climb between two tall snow cheeks to a platform DropH up with an
    // open end: step off it and you are outside the maze, and no jump gets back up. The cheeks (3 m and a ridge) keep
    // anyone on the steps from jumping anywhere but out, or back into the cell they came from.
    void BuildWaysOut(Material snowy)
    {
        var mb = new MeshBuilder();
        const int Risers = 6;
        const float Run = 0.3f, Platform = 1.2f, Half = 0.7f, Cheek = 0.26f, CheekTop = 3.0f, Foot = 0.25f;
        float rise = DropH / Risers, top = Foot + (Risers - 1) * Run, end = top + Platform;
        foreach (Opening o in WaysOut)
        {
            for (int k = 1; k < Risers; k++)
                Slab(mb, o, o.U - Half, o.U + Half, -Sink, k * rise, Foot + (k - 1) * Run, Foot + k * Run, IsleKit.Wood);
            Slab(mb, o, o.U - Half, o.U + Half, -Sink, DropH, top, end, IsleKit.Wood);
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = o.U + side * (Half - 0.01f), outer = inner + side * Cheek;
                Slab(mb, o, inner, outer, -Sink, CheekTop, 0.15f, end + 0.05f, SnowBlock);
                Prism(mb, o.At((inner + outer) / 2f, CheekTop, 0.15f), o.At((inner + outer) / 2f, CheekTop, end + 0.05f), Cheek, 0.25f, SnowBlock);
            }
        }
        AddMesh("Ways Out", mb, snowy);
    }

    // ================= the gantry =================

    // A deck 1.4 m wide along the z = 1.3 wall line, top 9.5 m, from x -9.4 to the tower's west face (13.8), on posts at
    // wall junctions (tie beams only high up, nothing in the corridors), with a 2.6 m baluster screen on both long sides
    // and the west end (5 cm balusters, 12 cm gaps) under a peaked capping rail
    void BuildGantry(Material snowy, List<int> posts)
    {
        var mb = new MeshBuilder();
        float zc = (DeckZ0 + DeckZ1) / 2f;
        for (int n = 0; n < posts.Count; n++)
        {
            float x = Line(posts[n]);
            Blk(mb, x - 0.15f, x + 0.15f, -Sink, DeckH - 0.25f, zc - 0.15f, zc + 0.15f, IsleKit.Wood);
            if (n + 1 < posts.Count)
            {
                float x2 = Line(posts[n + 1]);
                foreach (float y in new[] { 4.5f, 7.5f })
                    Blk(mb, x + 0.15f, x2 - 0.15f, y, y + 0.2f, zc - 0.08f, zc + 0.08f, IsleKit.Wood);
            }
        }
        Blk(mb, DeckX0, DeckX1, DeckH - 0.25f, DeckH, DeckZ0, DeckZ1, IsleKit.DarkWood);
        Blk(mb, DeckX0 - 0.05f, DeckX1, DeckH - 0.45f, DeckH - 0.25f, zc - 0.2f, zc + 0.2f, IsleKit.Wood);      // the stringer under it

        const float Bal = 0.05f, Pitch = 0.17f;
        float top = DeckH + FenceH;
        // long sides
        foreach (float z0 in new[] { DeckZ0, DeckZ1 - Bal })
        {
            for (float x = DeckX0; x + Bal <= DeckX1 + Eps; x += Pitch)
                Blk(mb, x, x + Bal, DeckH, top, z0, z0 + Bal, IsleKit.Wood);
            Blk(mb, DeckX0, DeckX1, DeckH, DeckH + 0.08f, z0 - 0.015f, z0 + Bal + 0.015f, IsleKit.DarkWood);
            Blk(mb, DeckX0, DeckX1, top, top + 0.06f, z0 - 0.015f, z0 + Bal + 0.015f, IsleKit.DarkWood);
            Prism(mb, new Vector3(DeckX0, top + 0.06f, z0 + Bal / 2f), new Vector3(DeckX1, top + 0.06f, z0 + Bal / 2f), Bal + 0.03f, 0.08f, IsleKit.DarkWood);
        }
        // the west end
        for (float z = DeckZ0 + Bal + 0.12f; z + Bal <= DeckZ1 - Bal - 0.1f; z += Pitch)
            Blk(mb, DeckX0, DeckX0 + Bal, DeckH, top, z, z + Bal, IsleKit.Wood);
        Blk(mb, DeckX0 - 0.015f, DeckX0 + Bal + 0.015f, top, top + 0.06f, DeckZ0, DeckZ1, IsleKit.DarkWood);
        Prism(mb, new Vector3(DeckX0 + Bal / 2f, top + 0.06f, DeckZ0), new Vector3(DeckX0 + Bal / 2f, top + 0.06f, DeckZ1), Bal + 0.03f, 0.08f, IsleKit.DarkWood);
        AddMesh("Gantry", mb, snowy);
    }

    // ================= the stair tower =================

    // 3 x 3 m, boarded solid to its roof at 13.7 m: a door at its foot in the east wall (away from the maze) and the
    // deck doorway in its west wall at the top. Inside, a winding stair climbs round a 0.8 m well in 0.9 m lanes: 52
    // rises of 0.183 m, a step on each side run of three and one on each corner, a revolution every 2.9 m (headroom
    // 2.6 m), arriving on the top floor over the west lane at 9.5 m, beside the deck doorway.
    void BuildTower(Material snowy)
    {
        var mb = new MeshBuilder();
        const float W0 = 13.8f, W1 = 16.8f, S0 = -0.2f, S1 = 2.8f, Th = 0.2f, Top = DeckH + 4.2f;
        float ix0 = W0 + Th, ix1 = W1 - Th, iz0 = S0 + Th, iz1 = S1 - Th;      // inside: x 14.0..16.6, z 0..2.6
        const float Lane = 0.9f;
        float wellX0 = ix0 + Lane, wellX1 = ix1 - Lane, wellZ0 = iz0 + Lane, wellZ1 = iz1 - Lane;
        // walls: west, round the deck doorway (z 0.85..1.75, 9.5..11.7)
        Blk(mb, W0, ix0, -Sink, Top, S0, 0.85f, IsleKit.Wood);
        Blk(mb, W0, ix0, -Sink, Top, 1.75f, S1, IsleKit.Wood);
        Blk(mb, W0, ix0, -Sink, DeckH, 0.85f, 1.75f, IsleKit.Wood);
        Blk(mb, W0, ix0, DeckH + 2.2f, Top, 0.85f, 1.75f, IsleKit.Wood);
        // east, round the foot door (z 0.9..1.7, 2.0 high, under the first revolution's east run)
        Blk(mb, ix1, W1, -Sink, Top, S0, wellZ0, IsleKit.Wood);
        Blk(mb, ix1, W1, -Sink, Top, wellZ1, S1, IsleKit.Wood);
        Blk(mb, ix1, W1, 2.0f, Top, wellZ0, wellZ1, IsleKit.Wood);
        Blk(mb, ix0, ix1, -Sink, Top, S0, iz0, IsleKit.Wood);       // south
        Blk(mb, ix0, ix1, -Sink, Top, iz1, S1, IsleKit.Wood);       // north
        Blk(mb, W0 - 0.2f, W1 + 0.2f, Top, Top + 0.2f, S0 - 0.2f, S1 + 0.2f, IsleKit.Red);      // roof
        Blk(mb, ix0, ix1, -Sink, 0.02f, iz0, iz1, IsleKit.DarkWood);                          // ground floor
        // the winding stair: per revolution north run (west-going), NW corner, west run (south-going), SW corner, south
        // run (east-going), SE corner, east run (north-going), NE corner
        const int Rises = 52;
        float rise = DeckH / Rises, run = (wellX1 - wellX0) / 3f;
        for (int n = 1; n < Rises; n++)      // the last rise is the top floor itself
        {
            int pos = (n - 1) % 16, s = pos % 4;
            float x0, x1, z0, z1;
            switch (pos / 4)
            {
                case 0:  // north side, then NW corner
                    z0 = wellZ1; z1 = iz1;
                    if (s < 3) { x1 = wellX1 - s * run; x0 = x1 - run; } else { x0 = ix0; x1 = wellX0; }
                    break;
                case 1:  // west side, then SW corner
                    x0 = ix0; x1 = wellX0;
                    if (s < 3) { z1 = wellZ1 - s * run; z0 = z1 - run; } else { z0 = iz0; z1 = wellZ0; }
                    break;
                case 2:  // south side, then SE corner
                    z0 = iz0; z1 = wellZ0;
                    if (s < 3) { x0 = wellX0 + s * run; x1 = x0 + run; } else { x0 = wellX1; x1 = ix1; }
                    break;
                default: // east side, then NE corner
                    x0 = wellX1; x1 = ix1;
                    if (s < 3) { z0 = wellZ0 + s * run; z1 = z0 + run; } else { z0 = wellZ1; z1 = iz1; }
                    break;
            }
            float t = n * rise;
            Blk(mb, x0, x1, t < 0.6f ? -Sink : t - 0.3f, t, z0, z1, IsleKit.DarkWood);
        }
        // the top floor over the west lane (the last rise lands on it at the NW corner), a rail along its open edge
        Blk(mb, ix0, wellX0, DeckH - 0.2f, DeckH, iz0, iz1, IsleKit.DarkWood);
        Blk(mb, wellX0, wellX0 + 0.06f, DeckH, DeckH + 1.0f, iz0, wellZ1, IsleKit.Wood);
        AddMesh("Stair Tower", mb, snowy);
    }

    // ================= mesh helpers =================

    // An axis-aligned block x0..x1, y0..y1, z0..z1
    static void Blk(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);

    // A block at an opening: from u0 to u1 along its wall line and from w0 to w1 out from the line (Opening.At)
    static void Slab(MeshBuilder mb, Opening o, float u0, float u1, float y0, float y1, float w0, float w1, Color c)
    {
        Vector3 a = o.At(u0, y0, w0), d = o.At(u1, y1, w1);
        Blk(mb, Mathf.Min(a.x, d.x), Mathf.Max(a.x, d.x), y0, y1, Mathf.Min(a.z, d.z), Mathf.Max(a.z, d.z), c);
    }

    // A plate's mark: a low kerb of its colour round the square the sensor fills
    static void Kerb(MeshBuilder mb, float cx, float cz, Color c)
    {
        const float Outer = PlateSize / 2f, Inner = Outer - 0.12f, Top = 0.09f;
        Blk(mb, cx - Outer, cx + Outer, -Sink, Top, cz - Outer, cz - Inner, c);
        Blk(mb, cx - Outer, cx + Outer, -Sink, Top, cz + Inner, cz + Outer, c);
        Blk(mb, cx - Outer, cx - Inner, -Sink, Top, cz - Inner, cz + Inner, c);
        Blk(mb, cx + Inner, cx + Outer, -Sink, Top, cz - Inner, cz + Inner, c);
    }

    // A ridge: a triangular prism from 'from' to 'to' (its base line, level), 'width' across and 'height' to its peak,
    // with both ends closed; wound as Kit.AddBox so it shows from outside
    static void Prism(MeshBuilder mb, Vector3 from, Vector3 to, float width, float height, Color c)
    {
        Vector3 u = to - from; u.y = 0f;
        if (u.sqrMagnitude < 1e-6f) return;
        u.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, u) * (width / 2f), up = Vector3.up * height;
        Vector3 a0 = from + r, a1 = to + r, b0 = from - r, b1 = to - r, t0 = from + up, t1 = to + up;
        mb.Quad(a0, a1, t1, t0, c);
        mb.Quad(b1, b0, t0, t1, c);
        mb.Tri(a1, t1, b1, c);
        mb.Tri(b0, t0, a0, c);
    }

    // A square pyramid cap on a wall junction: base 2 * half across at 'foot', peak 'height' above
    static void Pyramid(MeshBuilder mb, Vector3 foot, float half, float height, Color c)
    {
        Vector3 apex = foot + Vector3.up * height;
        var corner = new[]
        {
            foot + new Vector3(-half, 0f, -half), foot + new Vector3(half, 0f, -half),
            foot + new Vector3(half, 0f, half), foot + new Vector3(-half, 0f, half),
        };
        for (int i = 0; i < 4; i++) mb.Tri(corner[i], apex, corner[(i + 1) % 4], c);
    }

    // A five-pointed star standing upright (as Kit.AddStar), turned by 'yaw' about the vertical
    static void AddStar(MeshBuilder mb, Vector3 centre, float radius, float thickness, float yaw, Color color)
    {
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        var rim = new Vector3[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / 10f, r = i % 2 == 0 ? radius : radius * 0.42f;
            rim[i] = centre + turn * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
        }
        Vector3 front = centre + turn * Vector3.back * thickness, back = centre + turn * Vector3.forward * thickness;
        for (int i = 0; i < 10; i++)
        {
            mb.Tri(front, rim[i], rim[(i + 1) % 10], color);
            mb.Tri(back, rim[(i + 1) % 10], rim[i], color * 0.85f);
        }
    }
}
