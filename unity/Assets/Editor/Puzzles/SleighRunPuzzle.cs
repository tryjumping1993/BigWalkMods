// Sleigh Run: the Sleigh Shed (spec: puzzle-specs/SleighRun.md, revision 2 with its review notes, 2026-11-22).
//
// A present (the game's timer ball) has to get from Santa's workshop to the sleigh through a roofed, soundproof,
// radio-dead snow shed: a staircase of L-shaped chambers behind doors (two in a 2-player lobby, three for 3, four for
// 4+). Above each door a split-flap display shows two Scribbles, dealt afresh on every start. Each door opens only
// while the matching two tiles hang on its peg board, at a pointsman's station outside the shed (West, East, and North
// in 4-player lobbies). The runner inside reads the display and talks into the intercom beside the door; the voice
// comes out of the speaker above that door's board. Every door is one-shot: when anyone steps into the chamber beyond
// it, a sensor shuts it, covers its board for the rest of the attempt and uncovers the next board and display. Door 1
// can be called before the present is taken; every later door is against the ball's clock. The win is the bay's
// Validator, checked when the ball goes into the sleigh's holder (the gourd and the confetti at the start corner).
//
// Quitting: the Start Switch at the start corner, or walking out of any chamber's exit porch (its inner turnstile
// stops the puzzle). Every space between doors has its own exit porch (two one-way turnstiles at a right angle, never
// locked), the bay has the reindeer pen. While the puzzle is off every door is closed (the switch's own events) and
// every board sits behind a red stop shutter that only the root's events move, so nothing can be opened while off.
// No custom code: SDK parts and GameObject.SetActive wiring only.
//
// Coordinates: local metres from the root, x east, z north; heights above the pad top (Floor), everything placed
// onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x. A gate's yaw is the way it lets players
// through (front, its local -z, to back).
using System.Collections.Generic;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class SleighRunPuzzle : PuzzleDef
{
    public override string Name => "SleighRun";
    public override Vector2 Footprint => new Vector2(34f, 44f);
    public override Vector2? FixedSite => new Vector2(-1032f, -1908f);      // layout-final: a goods shed by the airport apron

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const float Ceil = 2.6f;            // inside height of the shed and its porches (roof slabs 2.6..2.9)
    const float RoofTop = 2.9f;
    const float Y0 = -0.1f;             // walls start a little under the pad top
    const float DoorTop = 2.04f;        // the game's door is 1.24 x 2.04
    const float DoorHalf = 0.62f;
    const float FrameHalf = 0.12f;      // door frames are 0.24 m thick, centred on the door's line (the door's own depth)
    const float GateTop = 2.3f;         // Gate No items: local x -1.11..+1.01, z -0.705..+0.706, 2.3 tall
    const float GateDepth = 0.705f;
    const float Fin = 0.06f;
    const float DisplayH = 2.3f, MicH = 1.3f;
    const float DisplayOff = 0.125f;    // a display's pivot is its face; its body runs 0.168 m behind, into the frame
    const float MicOff = 0.14f;         // a mic's back is 0.02 m behind its pivot
    const float Seconds2 = 50f, Seconds3 = 70f, Seconds4 = 90f;      // the present's clock per lobby size (check 14 tunes them)
    const float TileStep = 0.45f;

    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);
    static readonly Color Warm = new Color(1f, 0.85f, 0.62f);
    static readonly Color Door1 = IsleKit.Red, Door2 = IsleKit.Green, Door3 = IsleKit.Gold, Door4 = IsleKit.Ice;

    PuzzleBuilder b;
    Material snowy, bare;

    // ---- small builders ----

    // A block between two corners (local x, y above the pad top, z)
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A block in bands of two colours up its height (candy-cane jambs and posts)
    static void Striped(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color a, Color c, float band)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt((y1 - y0) / band));
        float step = (y1 - y0) / n;
        for (int i = 0; i < n; i++) Block(mb, x0, x1, y0 + i * step, y0 + (i + 1) * step, z0, z1, i % 2 == 0 ? a : c);
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

    // A gate's opening along its wall: (low, high) of x for yaw 0 / 180, of z for yaw 90 / -90 (the spec's table: the
    // gate's frame is off-centre, local x -1.11..+1.01)
    static Vector2 GateSpan(float c, float yaw)
    {
        int y = Mathf.RoundToInt(yaw);
        if (y == 0) return new Vector2(c - 1.11f, c + 1.01f);
        if (y == 180 || y == -180) return new Vector2(c - 1.01f, c + 1.11f);
        if (y == 90) return new Vector2(c - 1.01f, c + 1.11f);
        return new Vector2(c - 1.11f, c + 1.01f);      // -90
    }

    // The mouth round a gate at (cx, cz): thin fins outside its frame along its whole depth (so nothing slips round its
    // sides) and a lintel from its 2.3 m top to the ceiling over its whole depth. The wall itself stops at the frame's
    // edges (built by the caller from GateSpan).
    static void GateMouth(MeshBuilder mb, float cx, float cz, float yaw, Color color)
    {
        bool alongX = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) < 0.5f;      // yaw 0 / 180: the wall runs along x
        Vector2 s = GateSpan(alongX ? cx : cz, yaw);
        if (alongX)
        {
            Block(mb, s.x - Fin, s.x, Y0, GateTop, cz - GateDepth, cz + GateDepth, color);
            Block(mb, s.y, s.y + Fin, Y0, GateTop, cz - GateDepth, cz + GateDepth, color);
            Block(mb, s.x - Fin, s.y + Fin, GateTop, Ceil, cz - GateDepth, cz + GateDepth, color);
        }
        else
        {
            Block(mb, cx - GateDepth, cx + GateDepth, Y0, GateTop, s.x - Fin, s.x, color);
            Block(mb, cx - GateDepth, cx + GateDepth, Y0, GateTop, s.y, s.y + Fin, color);
            Block(mb, cx - GateDepth, cx + GateDepth, GateTop, Ceil, s.x - Fin, s.y + Fin, color);
        }
    }

    // A wall along x (z0..z1 thick) from x0 to x1, with each gate opening given (low, high) left out, and the gate's
    // fins beside it: GateMouth fills the opening's sides and the lintel over it across the wall's thickness
    static void WallX(MeshBuilder mb, float x0, float x1, float z0, float z1, Color color, params Vector2[] gaps)
    {
        float at = x0;
        foreach (Vector2 g in gaps)
        {
            if (g.x - Fin > at) Block(mb, at, g.x - Fin, Y0, Ceil, z0, z1, color);
            at = g.y + Fin;
        }
        if (x1 > at) Block(mb, at, x1, Y0, Ceil, z0, z1, color);
    }

    static void WallZ(MeshBuilder mb, float z0, float z1, float x0, float x1, Color color, params Vector2[] gaps)
    {
        float at = z0;
        foreach (Vector2 g in gaps)
        {
            if (g.x - Fin > at) Block(mb, x0, x1, Y0, Ceil, at, g.x - Fin, color);
            at = g.y + Fin;
        }
        if (z1 > at) Block(mb, x0, x1, Y0, Ceil, at, z1, color);
    }

    // A one-way No Items turnstile, never locked
    Piece Gate(string name, float x, float z, float yaw)
        => P("Gate No items", x, z, 0f, yaw).Named(name)
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

    // A door's frame across a corridor: 0.24 m thick on the door's line, striped jambs of 0.58 m, a lintel from the
    // door's top to the ceiling. acrossX: the frame runs along x (door yaw 0), else along z (door yaw +-90).
    Piece Frame(string name, bool acrossX, float line, float c0, float c1, float centre, Color color)
    {
        var mb = new MeshBuilder();
        float d0 = centre - DoorHalf, d1 = centre + DoorHalf;
        if (acrossX)
        {
            Striped(mb, c0, d0, Y0, DoorTop, line - FrameHalf, line + FrameHalf, color, IsleKit.Cream, 0.3f);
            Striped(mb, d1, c1, Y0, DoorTop, line - FrameHalf, line + FrameHalf, color, IsleKit.Cream, 0.3f);
            Block(mb, c0, c1, DoorTop, Ceil, line - FrameHalf, line + FrameHalf, color);
        }
        else
        {
            Striped(mb, line - FrameHalf, line + FrameHalf, Y0, DoorTop, c0, d0, color, IsleKit.Cream, 0.3f);
            Striped(mb, line - FrameHalf, line + FrameHalf, Y0, DoorTop, d1, c1, color, IsleKit.Cream, 0.3f);
            Block(mb, line - FrameHalf, line + FrameHalf, DoorTop, Ceil, c0, c1, color);
        }
        return OnPad(name, mb, snowy);
    }

    // The cover over a display: a shallow box over its face and sides (h 2.06..2.55, under the ceiling and above the
    // door), on the frame face of a door's line seen from side 's' (-1 or +1 along the frame's normal), with a wreath
    Piece DisplayCover(string name, bool acrossX, float line, float s, float centre)
    {
        var mb = new MeshBuilder();
        float f0 = line + s * (FrameHalf + 0.003f), f1 = line + s * (FrameHalf + 0.14f);
        float lo = Mathf.Min(f0, f1), hi = Mathf.Max(f0, f1);
        const float y0 = 2.06f, y1 = 2.55f;
        float front = line + s * (FrameHalf + 0.16f), cy = (y0 + y1) / 2f;
        if (acrossX) Block(mb, centre - 0.25f, centre + 0.25f, y0, y1, lo, hi, IsleKit.Wood);
        else Block(mb, lo, hi, y0, y1, centre - 0.25f, centre + 0.25f, IsleKit.Wood);
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.PI * 2f / 10f, u = Mathf.Cos(a) * 0.15f, v = Mathf.Sin(a) * 0.15f;
            Vector3 p = acrossX ? new Vector3(centre + u, cy + v, front) : new Vector3(front, cy + v, centre + u);
            Kit.AddBall(mb, p, 0.045f, IsleKit.Green);
        }
        Kit.AddBall(mb, acrossX ? new Vector3(centre, cy - 0.15f, front) : new Vector3(front, cy - 0.15f, centre), 0.04f, IsleKit.Red);
        return OnPad(name, mb, snowy);
    }

    // ---- stations: a snow wall facing 'Yaw', its face's middle at O; u runs along the face (the parts' local x), w out
    //      from it (the parts' local z) ----
    sealed class Station
    {
        public string Name;
        public Vector2 O;
        public float Yaw;
        public Vector2 W(float u, float w) => O + IsleKit.Turn(u, w, Yaw);
    }

    static void SBox(MeshBuilder mb, Station st, float u0, float u1, float y0, float y1, float w0, float w1, Color color)
    {
        Vector2 c = st.W((u0 + u1) / 2f, (w0 + w1) / 2f);
        Kit.AddBox(mb, new Vector3(c.x, y0, c.y), new Vector3(u1 - u0, y1 - y0, w1 - w0), color, st.Yaw);
    }

    const float StationHalf = 3.7f;      // the wall: 7.4 m wide, 0.7 m thick, 2.6 m tall
    const float NicheHalf = 0.8f, NicheLow = 1.0f, NicheHigh = 1.7f, NicheBack = -0.55f;
    const float RackFront = 1.75f, RackStep = 2.2f, RackBack = 2.75f, ShelfLow = 0.9f, ShelfHigh = 1.4f;      // the back row clears the front row's tops

    // The station's wall with a niche per board (centred at u = niches[i], painted colours[i]), its roof on striped
    // posts, and a stepped rack table in front of each niche (two rows of eight tiles)
    Piece StationMesh(Station st, float[] niches, Color[] colours)
    {
        var mb = new MeshBuilder();
        SBox(mb, st, -StationHalf, StationHalf, Y0, Ceil, -0.7f, NicheBack, SnowBlock);              // back layer
        SBox(mb, st, -StationHalf, StationHalf, Y0, NicheLow, NicheBack, 0f, SnowBlock);              // under the niches
        SBox(mb, st, -StationHalf, StationHalf, NicheHigh, Ceil, NicheBack, 0f, SnowBlock);            // over them
        float at = -StationHalf;
        var order = (float[])niches.Clone();
        System.Array.Sort(order);
        foreach (float n in order)
        {
            SBox(mb, st, at, n - NicheHalf, NicheLow, NicheHigh, NicheBack, 0f, SnowBlock);
            at = n + NicheHalf;
        }
        SBox(mb, st, at, StationHalf, NicheLow, NicheHigh, NicheBack, 0f, SnowBlock);
        for (int i = 0; i < niches.Length; i++)
        {
            float n = niches[i];
            SBox(mb, st, n - NicheHalf, n + NicheHalf, NicheLow, NicheHigh, NicheBack, NicheBack + 0.01f, colours[i]);   // the niche's back, in the door's colour
            SBox(mb, st, n - 1.0f, n + 1.0f, 2.42f, 2.56f, 0f, 0.02f, colours[i]);                                       // a band over it
            // the rack table: a low front shelf and a higher back one, with lips
            SBox(mb, st, n - 1.85f, n + 1.85f, Y0, ShelfLow, RackFront, RackStep, IsleKit.Wood);
            SBox(mb, st, n - 1.85f, n + 1.85f, Y0, ShelfHigh, RackStep, RackBack, IsleKit.Wood);
            SBox(mb, st, n - 1.85f, n + 1.85f, ShelfLow, ShelfLow + 0.05f, RackFront, RackFront + 0.04f, IsleKit.DarkWood);
            SBox(mb, st, n - 1.85f, n + 1.85f, ShelfHigh, ShelfHigh + 0.08f, RackBack - 0.04f, RackBack, IsleKit.DarkWood);
        }
        // the roof over the wall and the racks, on two posts at the front, striped red and gold
        SBox(mb, st, -StationHalf - 0.25f, StationHalf + 0.25f, Ceil, Ceil + 0.15f, -0.85f, RackBack + 0.2f, IsleKit.Red);
        foreach (float pu in new[] { -StationHalf - 0.1f, StationHalf + 0.1f })
        {
            Vector2 c = st.W(pu, RackBack + 0.1f);
            for (int k = 0; k < 9; k++)
            {
                float y0 = Y0 + k * (Ceil - Y0) / 9f;
                Kit.AddBox(mb, new Vector3(c.x, y0, c.y), new Vector3(0.16f, (Ceil - Y0) / 9f, 0.16f), k % 2 == 0 ? IsleKit.Red : IsleKit.Gold, st.Yaw);
            }
        }
        return OnPad(st.Name, mb, snowy);
    }

    // A station's board k at niche u: the Peg Board (pivot at slot 1, slot 2 0.45 m to its left), its "Loaded" screen,
    // the speaker over it, its run cover and stop shutter, and its rack of sixteen Scribbles (two of each). Every piece
    // goes under 'group' when one is given.
    sealed class BoardSet
    {
        public Piece Board, Loaded, Speaker, Cover, Shutter;
    }

    BoardSet StationBoard(Station st, string tag, float u, Color colour, Piece display, Piece group)
    {
        var set = new BoardSet();
        var parts = new List<Piece>();
        Vector2 at = st.W(u + 0.225f, NicheBack + 0.01f);
        set.Board = P("Peg Board", at.x, at.y, (NicheLow + NicheHigh) / 2f, st.Yaw).Named("Board " + tag).Set("PuzzlePegBoard", "size", 1);   // Slots2
        Vector2 sideAt = st.W(u > 0f ? u + 1.45f : u - 1.45f, 0f);
        if (Mathf.Abs(u) < 0.01f) sideAt = st.W(u + 1.45f, 0f);
        set.Loaded = P("Validator", sideAt.x, sideAt.y, 1.6f, st.Yaw).Named("Loaded " + tag);
        Vector2 spAt = st.W(u, 0.1f);
        set.Speaker = P("Intercom Speaker", spAt.x, spAt.y, 2.07f, st.Yaw).Named("Speaker " + tag);
        Component boardC = set.Board.Component("PuzzlePegBoard"), displayC = display.Component("PuzzleDisplay");
        Sdk.Add(boardC, "answerFrom", displayC, displayC);
        Sdk.AddInts(boardC, "answerFromFlap", 1, 2);
        Sdk.Set(boardC, "validator", set.Loaded.Component("PuzzleValidator"));
        parts.Add(set.Board); parts.Add(set.Loaded); parts.Add(set.Speaker);

        // run cover: 2.0 x 1.1 m, 0.4 m proud, overlapping the niche by 0.2 m all round; stop shutter: 2.3 x 1.4, 0.5 proud
        var cover = new MeshBuilder();
        SBox(cover, st, u - 1.0f, u + 1.0f, 0.8f, 1.9f, 0f, 0.4f, IsleKit.Wood);
        SBox(cover, st, u - 0.9f, u + 0.9f, 1.2f, 1.5f, 0.4f, 0.42f, colour);
        set.Cover = OnPad("Board Cover " + tag, cover, snowy);
        var shutter = new MeshBuilder();
        SBox(shutter, st, u - 1.15f, u + 1.15f, 0.65f, 2.05f, 0f, 0.5f, IsleKit.Red);
        SBox(shutter, st, u - 1.0f, u + 1.0f, 1.3f, 1.4f, 0.5f, 0.52f, IsleKit.Cream);
        set.Shutter = OnPad("Board Shutter " + tag, shutter, snowy);
        parts.Add(set.Cover); parts.Add(set.Shutter);

        // the rack: front row images 1..8, back row 1..8, pictures towards the wall (where the pointsman stands)
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < 8; i++)
            {
                float w = row == 0 ? 1.86f : 2.28f, h = (row == 0 ? ShelfLow : ShelfHigh) + 0.24f;
                Vector2 tp = st.W(u + (i - 3.5f) * TileStep, w);
                Piece tile = P("Peg Tile", tp.x, tp.y, h, st.Yaw + 180f).Named("Tile " + tag + "-" + (i + 1) + (row == 0 ? "a" : "b"));
                Component tileC = tile.Component("PuzzlePegTile");
                SetEnumByName(tileC, "imageSet", "Scribbles");
                Sdk.Set(tileC, "image", i + 1);
                parts.Add(tile);
            }
        if (group != null) foreach (Piece p in parts) p.Under(group);
        return set;
    }

    Piece StationLamp(Station st, string name)
    {
        Vector2 at = st.W(0f, 1.2f);
        return P("Light 2", at.x, at.y, Ceil, st.Yaw).Named(name).Set("PuzzleLight", "color", Warm);
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

    // A chamber sensor: box a (centre ax, az, size) carries the part; box b (if any) is a second collider on it
    Piece ChamberSensor(string name, float ax, float az, Vector3 sizeA, float bx, float bz, Vector3 sizeB)
    {
        Piece zone = IsleKit.Sensor(b, name, ax, az, sizeA, Floor, false).Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        if (sizeB != Vector3.zero)
        {
            BoxCollider second = zone.Go.AddComponent<BoxCollider>();
            second.size = sizeB;
            second.center = new Vector3(bx - ax, 0f, bz - az);      // the root's yaw is 0 and the part's too: local axes are the root's
        }
        return zone;
    }

    // A reindeer silhouette (dress) at (x, z) facing +x or -x
    static void Reindeer(MeshBuilder mb, float x, float z, float dir)
    {
        Color c = IsleKit.DarkWood;
        Block(mb, x - 0.5f, x + 0.5f, 0.75f, 1.15f, z - 0.17f, z + 0.17f, IsleKit.Wood);                 // body
        foreach (float lx in new[] { x - 0.4f, x + 0.4f })
            foreach (float lz in new[] { z - 0.12f, z + 0.12f })
                Block(mb, lx - 0.04f, lx + 0.04f, 0f, 0.75f, lz - 0.04f, lz + 0.04f, c);              // legs
        float nx = x + dir * 0.5f;
        Block(mb, nx - 0.1f, nx + 0.1f, 1.05f, 1.55f, z - 0.08f, z + 0.08f, IsleKit.Wood);              // neck
        Block(mb, Mathf.Min(nx, nx + dir * 0.32f), Mathf.Max(nx, nx + dir * 0.32f), 1.5f, 1.68f, z - 0.09f, z + 0.09f, IsleKit.Wood);   // head
        Kit.AddBall(mb, new Vector3(nx + dir * 0.34f, 1.6f, z), 0.05f, IsleKit.Red);                    // the nose
        foreach (float az in new[] { z - 0.1f, z + 0.1f })
        {
            Block(mb, nx - 0.02f, nx + 0.02f, 1.68f, 2.0f, az - 0.02f, az + 0.02f, c);                 // antlers
            Block(mb, nx - dir * 0.15f - 0.02f, nx - dir * 0.15f + 0.02f, 1.8f, 1.95f, az - 0.02f, az + 0.02f, c);
        }
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(38f, 48f, 0f, -0.5f);
        b.DebugTeleport(-12f, -21f, 2f);
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        Piece root = b.RootPiece;
        Component sdk = b.Puzzle;
        // No jetpacks over the puzzle: the roof has no openings, but nobody flies onto it or over the stations
        IsleKit.CalmAir(b, "Sleigh Shed", 0f, -0.5f, new Vector3(46f, 120f, 56f), 0f, -10f, false);

        // ---- the pad: one level slab under the shed, its porches, the stations and the start corner, sunk 1.5 m ----
        var pad = new MeshBuilder();
        Block(pad, -17f, 17f, -1.75f, 0f, -23f, 22f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        // ---- lobby groups ----
        Piece g2 = IsleKit.Lobby(b, "Lobby 2", true, false, false);
        Piece g3 = IsleKit.Lobby(b, "Lobby 3", false, true, false);
        Piece g4 = IsleKit.Lobby(b, "Lobby 4", false, false, true);
        Piece g34 = IsleKit.Lobby(b, "Lobby 3 Plus", false, true, true);

        BuildShell();
        BuildDress();

        // ---- door frames (always: in smaller lobbies a frame with no door is the open archway) ----
        Frame("Door Frame 1", true, -10f, -6.7f, -4.3f, -5.5f, Door1);
        Frame("Door Frame 2", false, 0.2f, -2.9f, -0.5f, -1.7f, Door2);
        Frame("Door Frame 3", true, 4.0f, 7.3f, 9.7f, 8.5f, Door3);
        Frame("Door Frame 4", false, 2.8f, 11.1f, 13.5f, 12.3f, Door4);

        // ---- doors: worked only by functions, soundproof ----
        Piece a1 = P("Door", -5.5f, -10f, 0f, 0f).Named("Door 1").Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", true);
        Piece a2 = P("Door", 0.2f, -1.7f, 0f, 90f).Named("Door 2").Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", true);
        Piece a3 = P("Door", 8.5f, 4.0f, 0f, 0f).Named("Door 3").Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", true).Under(g34);
        Piece a4 = P("Door", 2.8f, 12.3f, 0f, -90f).Named("Door 4").Set("PuzzleDoor", "mode", 2).Set("PuzzleDoor", "soundproof", true).Under(g4);

        // ---- displays above the doors (two Scribbles each), intercom mics on the jambs, display covers ----
        Piece d1 = P("Display", -5.5f, -10f - DisplayOff, DisplayH, 180f).Named("Display 1").Set("PuzzleDisplay", "size", 1);
        Piece d2 = P("Display", 0.2f - DisplayOff, -1.7f, DisplayH, -90f).Named("Display 2").Set("PuzzleDisplay", "size", 1);
        Piece d3 = P("Display", 8.5f, 4.0f - DisplayOff, DisplayH, 180f).Named("Display 3").Set("PuzzleDisplay", "size", 1).Under(g34);
        Piece d4 = P("Display", 2.8f + DisplayOff, 12.3f, DisplayH, 90f).Named("Display 4").Set("PuzzleDisplay", "size", 1).Under(g4);
        Piece m1 = P("Intercom Mic", -4.59f, -10f - MicOff, MicH, 180f).Named("Intercom 1");
        Piece m2 = P("Intercom Mic", 0.2f - MicOff, -0.79f, MicH, -90f).Named("Intercom 2");
        Piece m3 = P("Intercom Mic", 9.41f, 4.0f - MicOff, MicH, 180f).Named("Intercom 3").Under(g34);
        Piece m4 = P("Intercom Mic", 2.8f + MicOff, 13.21f, MicH, 90f).Named("Intercom 4").Under(g4);
        Piece dc2 = DisplayCover("Display Cover 2", false, 0.2f, -1f, -1.7f);
        Piece dc3 = DisplayCover("Display Cover 3", true, 4.0f, -1f, 8.5f).Under(g34);
        Piece dc4 = DisplayCover("Display Cover 4", false, 2.8f, 1f, 12.3f).Under(g4);

        // ---- the workshop: the present dispenser (one per lobby size) on its stone plinth ----
        P("Timer Spawner", -9.8f, -11.2f, 0.4f, 90f).Named("Present Dispenser 2p").Set("PuzzleTimerSpawner", "style", 0).Set("PuzzleTimerSpawner", "time", Seconds2).Under(g2);
        P("Timer Spawner", -9.8f, -11.2f, 0.4f, 90f).Named("Present Dispenser 3p").Set("PuzzleTimerSpawner", "style", 0).Set("PuzzleTimerSpawner", "time", Seconds3).Under(g3);
        P("Timer Spawner", -9.8f, -11.2f, 0.4f, 90f).Named("Present Dispenser 4p").Set("PuzzleTimerSpawner", "style", 0).Set("PuzzleTimerSpawner", "time", Seconds4).Under(g4);

        // ---- the bay: the sleigh's holder and the check screen on the west wall ----
        Piece holder = P("Timer Goal", -3.0f, 12.3f, 0.85f, 90f).Named("Sleigh Holder");
        Piece check = P("Validator", -5.99f, 12.3f, 1.6f, 90f).Named("Sleigh Check");

        // ---- chamber sensors: each starts 1.2 m past its door; the second leg is a second box on the same object ----
        Piece s1 = ChamberSensor("Sensor 1", -5.5f, -4.65f, new Vector3(2.4f, 2.6f, 8.3f), -2.05f, -1.7f, new Vector3(4.5f, 2.6f, 2.4f));
        Piece s2 = ChamberSensor("Sensor 2", 5.55f, -1.7f, new Vector3(8.3f, 2.6f, 2.4f), 8.5f, 1.75f, new Vector3(2.4f, 2.6f, 4.5f));
        Piece s3 = ChamberSensor("Sensor 3", 8.5f, 9.35f, new Vector3(2.4f, 2.6f, 8.3f), 5.05f, 12.3f, new Vector3(4.5f, 2.6f, 2.4f)).Under(g34);
        Piece s4 = ChamberSensor("Sensor 4", -2.2f, 12.3f, new Vector3(7.6f, 2.6f, 6.0f), 0f, 0f, Vector3.zero).Under(g4);

        // ---- exits: every porch two one-way turnstiles at a right angle; leaving a chamber stops the puzzle ----
        Gate("Shed Entrance Out", -10.6f, -18.0f, 90f);
        Gate("Shed Entrance In", -9.1f, -16.0f, 0f);
        Piece exitW = Gate("Workshop Exit", -4.5f, -16.0f, 180f);
        Gate("Workshop Exit Out", -3.0f, -18.0f, 90f);
        Piece exit1 = Gate("Chamber Exit 1", -6.7f, -5.6f, -90f);
        Gate("Chamber Exit 1 Out", -8.65f, -4.1f, 0f);
        Piece exit2 = Gate("Chamber Exit 2", 4.7f, -2.9f, 180f);
        Gate("Chamber Exit 2 Out", 6.2f, -4.9f, 90f);
        Piece exit3 = Gate("Chamber Exit 3", 9.7f, 8.3f, 90f);
        Gate("Chamber Exit 3 Out", 11.65f, 6.8f, 180f);
        Gate("Pen Gate In", -2.5f, 15.3f, 0f);
        Gate("Pen Gate Out", -5.0f, 17.3f, -90f);

        // ---- stations ----
        var west = new Station { Name = "West Station", O = new Vector2(-14.95f, -4f), Yaw = 90f };      // wall centre (-15.3, -4)
        var east = new Station { Name = "East Station", O = new Vector2(14.95f, 0f), Yaw = -90f };       // (15.3, 0)
        var north = new Station { Name = "North Station", O = new Vector2(6f, 19.25f), Yaw = 180f };     // (6, 19.6)
        const float NicheA = 1.9f, NicheB = -1.9f;      // West: board 1 at u +1.9 (z -5.9), board 3 / 2 (W) at u -1.9 (z -2.1)
        StationMesh(west, new[] { NicheA, NicheB }, new[] { Door1, Door3 });
        StationMesh(east, new[] { 0f }, new[] { Door2 }).Under(g34);
        StationMesh(north, new[] { 0f }, new[] { Door4 }).Under(g4);
        BoardSet b1 = StationBoard(west, "1", NicheA, Door1, d1, null);
        BoardSet b3 = StationBoard(west, "3", NicheB, Door3, d3, g34);
        BoardSet b2w = StationBoard(west, "2W", NicheB, Door2, d2, g2);
        BoardSet b2 = StationBoard(east, "2", 0f, Door2, d2, g34);
        BoardSet b4 = StationBoard(north, "4", 0f, Door4, d4, g4);
        Piece lampW = StationLamp(west, "West Lamp");
        Piece lampE = StationLamp(east, "East Lamp").Under(g34);
        Piece lampN = StationLamp(north, "North Lamp").Under(g4);

        // ---- intercoms: each mic plays to the speaker over its door's board ----
        Sdk.Add(m1.Component("PuzzleIntercom"), "speakers", b1.Speaker.Component("PuzzleIntercomSpeaker"));
        Sdk.Add(m2.Component("PuzzleIntercom"), "speakers", b2.Speaker.Component("PuzzleIntercomSpeaker"), b2w.Speaker.Component("PuzzleIntercomSpeaker"));
        Sdk.Add(m3.Component("PuzzleIntercom"), "speakers", b3.Speaker.Component("PuzzleIntercomSpeaker"));
        Sdk.Add(m4.Component("PuzzleIntercom"), "speakers", b4.Speaker.Component("PuzzleIntercomSpeaker"));

        // ---- sound and radio: the shell is soundproof (BuildShell); radios are dead over the shed and the stations ----
        DeadZone("Radio Dead Zone", 0f, -0.35f, 2f, new Vector3(33.4f, 6f, 44.1f));      // x -16.7..16.7, z -22.4..21.7, h -1..5

        // ---- the start corner, south of the porches facing arriving players ----
        IsleKit.StartCorner(b, -7f, -21.2f, 180f, true, Floor, false);
        Transform switchT = b.Root.transform.Find("Switch Start");
        if (switchT == null) throw new System.Exception("SleighRun: the start corner's Switch Start was not found under the root");
        Piece sw = new Piece(switchT.gameObject).Named("Start Switch");

        // ---- light: room lights inside, strings under the ceilings, a ring round the site ----
        int lamp = 0;
        foreach (Vector2 at in new[] { new Vector2(-7f, -13f), new Vector2(-5.5f, -1.7f), new Vector2(8.5f, -1.7f), new Vector2(8.5f, 12.3f),
                                        new Vector2(-4.5f, 12.3f), new Vector2(-1.0f, 12.3f) })
            b.Empty("Room Light " + (++lamp) + " FFD9A0 6 1.2", at.x, at.y, Floor + 2.4f, false);
        float sh = Floor + 2.45f;
        Kit.LightString(b, "Workshop Lights", new List<Vector3> { new Vector3(-10.6f, sh, -13f), new Vector3(-3.4f, sh, -13f) }, 0.15f, 0.5f, false, false, 171);
        Kit.LightString(b, "Chamber 1 Lights", new List<Vector3> { new Vector3(-5.5f, sh, -9.5f), new Vector3(-5.5f, sh, -1.7f), new Vector3(-0.35f, sh, -1.7f) }, 0.1f, 0.5f, false, false, 172);
        Kit.LightString(b, "Chamber 2 Lights", new List<Vector3> { new Vector3(0.7f, sh, -1.7f), new Vector3(8.5f, sh, -1.7f), new Vector3(8.5f, sh, 3.45f) }, 0.1f, 0.5f, false, false, 173);
        Kit.LightString(b, "Chamber 3 Lights", new List<Vector3> { new Vector3(8.5f, sh, 4.6f), new Vector3(8.5f, sh, 12.3f), new Vector3(3.35f, sh, 12.3f) }, 0.1f, 0.5f, false, false, 174);
        Kit.LightString(b, "Bay Lights", new List<Vector3> { new Vector3(-5.6f, sh, 9.8f), new Vector3(-0.2f, sh, 9.8f), new Vector3(-0.2f, sh, 14.8f), new Vector3(-5.6f, sh, 14.8f) }, 0.15f, 0.5f, false, false, 175);
        Kit.LightString(b, "Hall Lights", new List<Vector3> { new Vector3(-0.6f, sh, 12.3f), new Vector3(2.3f, sh, 12.3f) }, 0.1f, 0.5f, false, false, 176);
        Kit.LightRing(b, "Site Lights", 0f, -0.5f, 33.4f, 44.6f, Floor + 2.7f, false, 177);

        // ---- wiring ----
        Component c1 = a1.Component("PuzzleDoor"), c2 = a2.Component("PuzzleDoor"), c3 = a3.Component("PuzzleDoor"), c4 = a4.Component("PuzzleDoor");
        Component[] doors = { c1, c2, c3, c4 };
        // Every start and every stop (however it happens: the switch, or a script stop from an exit turnstile, which
        // moves the switch with its events) closes every door. From the switch, not the root: the SDK's checks reject
        // Close on the root's start and stop alike.
        foreach (Component door in doors)
        {
            sw.On("PuzzleStartSwitch", "onTurnedOn", door, "Close");
            sw.On("PuzzleStartSwitch", "onTurnedOff", door, "Close");
        }

        Piece[] runCovers = { b1.Cover, b2.Cover, b2w.Cover, b3.Cover, b4.Cover };
        Piece[] laterCovers = { b2.Cover, b2w.Cover, b3.Cover, b4.Cover, dc2, dc3, dc4 };
        Piece[] displayCovers = { dc2, dc3, dc4 };
        Piece[] shutters = { b1.Shutter, b2.Shutter, b2w.Shutter, b3.Shutter, b4.Shutter };
        Component[] lamps = { lampW.Component("PuzzleLight"), lampE.Component("PuzzleLight"), lampN.Component("PuzzleLight") };
        Component checkC = check.Component("PuzzleValidator");

        root.On("PuzzleSDK", "onPuzzleStart", b1.Cover.Go, "SetActive", false);
        foreach (Piece c in laterCovers) root.On("PuzzleSDK", "onPuzzleStart", c.Go, "SetActive", true);
        foreach (Piece s in shutters) root.On("PuzzleSDK", "onPuzzleStart", s.Go, "SetActive", false);
        root.On("PuzzleSDK", "onPuzzleStart", checkC, "SetCorrect", false);
        foreach (Component l in lamps) root.On("PuzzleSDK", "onPuzzleStart", l, "TurnOn");
        foreach (Piece c in runCovers) root.On("PuzzleSDK", "onPuzzleStop", c.Go, "SetActive", true);
        foreach (Piece c in displayCovers) root.On("PuzzleSDK", "onPuzzleStop", c.Go, "SetActive", true);
        foreach (Piece s in shutters) root.On("PuzzleSDK", "onPuzzleStop", s.Go, "SetActive", true);
        foreach (Component l in lamps) root.On("PuzzleSDK", "onPuzzleStop", l, "TurnOff");

        // Boards open their door while right, close it otherwise
        void BoardToDoor(BoardSet set, Component door)
        {
            set.Board.On("PuzzlePegBoard", "onCorrect", door, "Open");
            set.Board.On("PuzzlePegBoard", "onIncorrect", door, "Close");
            set.Board.On("PuzzlePegBoard", "onMissingPegs", door, "Close");
        }
        BoardToDoor(b1, c1);
        BoardToDoor(b2, c2);
        BoardToDoor(b2w, c2);
        BoardToDoor(b3, c3);
        BoardToDoor(b4, c4);

        // Stepping into chamber k shuts door k, covers its board, and uncovers the next board and display
        void Sense(Piece sensor, Component door, Piece[] cover, Piece[] uncover)
        {
            sensor.On("PuzzlePresenceSensor", "onPlayerEntered", door, "Close");
            foreach (Piece c in cover) sensor.On("PuzzlePresenceSensor", "onPlayerEntered", c.Go, "SetActive", true);
            foreach (Piece c in uncover) sensor.On("PuzzlePresenceSensor", "onPlayerEntered", c.Go, "SetActive", false);
        }
        Sense(s1, c1, new[] { b1.Cover }, new[] { b2.Cover, b2w.Cover, dc2 });
        Sense(s2, c2, new[] { b2.Cover, b2w.Cover }, new[] { b3.Cover, dc3 });
        Sense(s3, c3, new[] { b3.Cover }, new[] { b4.Cover, dc4 });
        Sense(s4, c4, new[] { b4.Cover }, new Piece[0]);

        // The present in the sleigh: the check screen is set right and checked; after its 5 s it wins the puzzle
        holder.On("PuzzleTimerGoal", "onTimerPlaced", checkC, "SetCorrect", true);
        holder.On("PuzzleTimerGoal", "onTimerPlaced", checkC, "Check");

        // Leaving the workshop or any chamber is a quit (the pen is left alone: stopping would cancel a win being checked)
        foreach (Piece g in new[] { exitW, exit1, exit2, exit3 })
            g.On("PuzzleNoItemsGate", "onEnter", sdk, "StopPuzzle");
    }

    // The shed's shell: walls, roofs, porches and the gates' mouths, the only child of "Shed" (PuzzleSoundProof takes
    // every collider under it, so no SDK part goes there). Interior faces as in the spec's corridor plan; walls 0.3 m.
    void BuildShell()
    {
        Color wall = SnowBlock, roof = IsleKit.Red, trim = IsleKit.Cream;
        var mb = new MeshBuilder();

        // gate openings (the spec's per-yaw table)
        Vector2 entIn = GateSpan(-9.1f, 0f), wexIn = GateSpan(-4.5f, 180f), c1In = GateSpan(-5.6f, -90f), c2In = GateSpan(4.7f, 180f);
        Vector2 c3In = GateSpan(8.3f, 90f), penIn = GateSpan(-2.5f, 0f);
        Vector2 entOut = GateSpan(-18.0f, 90f), wexOut = GateSpan(-18.0f, 90f), c1Out = GateSpan(-8.65f, 0f), c2Out = GateSpan(-4.9f, 90f);
        Vector2 c3Out = GateSpan(11.65f, 180f), penOut = GateSpan(17.3f, -90f);

        // workshop: x -11..-3, z -16..-10; its north wall is the door 1 line (0.24 m thick), the frame between
        WallZ(mb, -16.3f, -10f - FrameHalf, -11.3f, -11f, wall);
        WallZ(mb, -16.3f, -10f - FrameHalf, -3f, -2.7f, wall);
        WallX(mb, -11f, -3f, -16.3f, -16f, wall, entIn, wexIn);
        Block(mb, -11.3f, -6.7f, Y0, Ceil, -10f - FrameHalf, -10f + FrameHalf, wall);
        Block(mb, -4.3f, -2.7f, Y0, Ceil, -10f - FrameHalf, -10f + FrameHalf, wall);

        // chamber 1: first leg x -6.7..-4.3 north to z -0.5, second leg east along z -2.9..-0.5 to door 2 (x 0.2)
        WallZ(mb, -10f + FrameHalf, -0.2f, -7f, -6.7f, wall, c1In);
        WallZ(mb, -10f + FrameHalf, -2.9f, -4.3f, -4f, wall);
        WallX(mb, -7f, 7.3f, -0.5f, -0.2f, wall);                          // north of chamber 1's second leg and chamber 2's first
        WallX(mb, -4f, 9.7f, -3.2f, -2.9f, wall, c2In);                   // south of both
        // chamber 2: first leg east to x 9.7, second leg north (x 7.3..9.7) to door 3 (z 4); chamber 3 north to z 13.5
        // then west along z 11.1..13.5 to door 4 (x 2.8) and the hall to the bay
        WallZ(mb, -0.2f, 10.8f, 7f, 7.3f, wall);                           // west side of chamber 2's second leg and chamber 3's first
        WallZ(mb, -3.2f, 13.8f, 9.7f, 10f, wall, c3In);                   // the east side, from chamber 2 to chamber 3's corner
        WallX(mb, 0.2f, 7.3f, 10.8f, 11.1f, wall);                         // south of chamber 3's second leg and the hall
        WallX(mb, 0.2f, 9.7f, 13.5f, 13.8f, wall);                         // north of them
        // the bay: x -6..0.2, z 9.3..15.3, open to the hall on its east
        WallZ(mb, 9f, 15.6f, -6.3f, -6f, wall);
        WallX(mb, -6f, 0.5f, 9f, 9.3f, wall);
        WallX(mb, -6f, 0.5f, 15.3f, 15.6f, wall, penIn);
        WallZ(mb, 9.3f, 10.8f, 0.2f, 0.5f, wall);
        WallZ(mb, 13.8f, 15.3f, 0.2f, 0.5f, wall);

        // porches: 3.0 x 3.1 m rooms (the pen 4.0 x 3.1), the outer gate in a side wall
        WallZ(mb, -19.4f, -16.3f, -10.9f, -10.6f, wall, entOut);           // entrance: x -10.6..-7.6, z -19.1..-16
        WallZ(mb, -19.4f, -16.3f, -7.6f, -7.3f, wall);
        WallX(mb, -10.6f, -7.6f, -19.4f, -19.1f, wall);
        WallZ(mb, -19.4f, -16.3f, -6.3f, -6f, wall);                       // workshop exit: x -6..-3, z -19.1..-16
        WallZ(mb, -19.4f, -16.3f, -3f, -2.7f, wall, wexOut);
        WallX(mb, -6f, -3f, -19.4f, -19.1f, wall);
        WallZ(mb, -7.4f, -3.8f, -10.1f, -9.8f, wall);                      // chamber 1 exit: x -9.8..-6.7, z -7.1..-4.1
        WallX(mb, -9.8f, -7f, -7.4f, -7.1f, wall);
        WallX(mb, -9.8f, -7f, -4.1f, -3.8f, wall, c1Out);
        WallZ(mb, -6.3f, -3.2f, 2.9f, 3.2f, wall);                         // chamber 2 exit: x 3.2..6.2, z -6..-2.9
        WallX(mb, 3.2f, 6.2f, -6.3f, -6f, wall);
        WallZ(mb, -6.3f, -3.2f, 6.2f, 6.5f, wall, c2Out);
        WallX(mb, 10f, 13.1f, 9.8f, 10.1f, wall);                          // chamber 3 exit: x 9.7..12.8, z 6.8..9.8
        WallZ(mb, 6.5f, 9.8f, 12.8f, 13.1f, wall);
        WallX(mb, 10f, 12.8f, 6.5f, 6.8f, wall, c3Out);
        WallZ(mb, 15.6f, 18.7f, -5.3f, -5f, wall, penOut);                 // reindeer pen: x -5..-1, z 15.3..18.4
        WallZ(mb, 15.6f, 18.7f, -1f, -0.7f, wall);
        WallX(mb, -5f, -1f, 18.4f, 18.7f, wall);

        // every gate's mouth: fins along its depth and a lintel from its top to the ceiling
        GateMouth(mb, -9.1f, -16f, 0f, trim);
        GateMouth(mb, -10.6f, -18f, 90f, trim);
        GateMouth(mb, -4.5f, -16f, 180f, trim);
        GateMouth(mb, -3f, -18f, 90f, trim);
        GateMouth(mb, -6.7f, -5.6f, -90f, trim);
        GateMouth(mb, -8.65f, -4.1f, 0f, trim);
        GateMouth(mb, 4.7f, -2.9f, 180f, trim);
        GateMouth(mb, 6.2f, -4.9f, 90f, trim);
        GateMouth(mb, 9.7f, 8.3f, 90f, trim);
        GateMouth(mb, 11.65f, 6.8f, 180f, trim);
        GateMouth(mb, -2.5f, 15.3f, 0f, trim);
        GateMouth(mb, -5f, 17.3f, -90f, trim);

        // roofs: 0.3 m slabs over every space and its walls, none overlapping (red: the eaves' band, snow on top)
        Block(mb, -11.3f, -2.7f, Ceil, RoofTop, -16.3f, -10f + FrameHalf, roof);      // workshop
        Block(mb, -7f, -4f, Ceil, RoofTop, -10f + FrameHalf, -0.2f, roof);             // chamber 1, first leg
        Block(mb, -4f, 10f, Ceil, RoofTop, -3.2f, -0.2f, roof);                        // chamber 1 second leg, chamber 2 first
        Block(mb, 7f, 10f, Ceil, RoofTop, -0.2f, 13.8f, roof);                         // chamber 2 second leg, chamber 3 first
        Block(mb, 0.5f, 7f, Ceil, RoofTop, 10.8f, 13.8f, roof);                        // chamber 3 second leg, the hall
        Block(mb, -6.3f, 0.5f, Ceil, RoofTop, 9f, 15.6f, roof);                        // the bay
        Block(mb, -10.9f, -7.3f, Ceil, RoofTop, -19.4f, -16.3f, roof);                 // porches
        Block(mb, -6.3f, -2.7f, Ceil, RoofTop, -19.4f, -16.3f, roof);
        Block(mb, -10.1f, -7f, Ceil, RoofTop, -7.4f, -3.8f, roof);
        Block(mb, 2.9f, 6.5f, Ceil, RoofTop, -6.3f, -3.2f, roof);
        Block(mb, 10f, 13.1f, Ceil, RoofTop, 6.5f, 10.1f, roof);
        Block(mb, -5.3f, -0.7f, Ceil, RoofTop, 15.6f, 18.7f, roof);

        Piece shed = b.Empty("Shed", 0.9f, -0.35f, Floor, false);
        b.AddScript(shed, "PuzzleSoundProof");
        OnPad("Shed Shell", mb, snowy).Under(shed);
    }

    // Dress: the dispenser's plinth, presents and a workbench in the workshop; the sleigh and reindeer in the bay
    void BuildDress()
    {
        var mb = new MeshBuilder();
        Block(mb, -10.5f, -9.1f, 0f, 0.4f, -11.9f, -10.5f, IsleKit.Stone);              // the dispenser's plinth
        Block(mb, -3.6f, -3.0f, 0f, 0.85f, -14.0f, -11.4f, IsleKit.Wood);              // workbench on the east wall
        Color[] wraps = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice };
        int k = 0;
        foreach (Vector2 at in new[] { new Vector2(-10.5f, -14.6f), new Vector2(-10.4f, -13.4f), new Vector2(-7.4f, -15.5f), new Vector2(-6.4f, -15.5f) })
        {
            Block(mb, at.x - 0.3f, at.x + 0.3f, 0f, 0.5f, at.y - 0.3f, at.y + 0.3f, wraps[k++ % 4]);
            Block(mb, at.x - 0.2f, at.x + 0.2f, 0.5f, 0.85f, at.y - 0.2f, at.y + 0.2f, wraps[k % 4]);
        }
        // the sleigh, along x at (-3, 12.3), facing the hall: runners, a tub whose seat holds the present, a high back
        Block(mb, -4.8f, -1.0f, 0f, 0.12f, 11.5f, 11.62f, IsleKit.Gold);
        Block(mb, -4.8f, -1.0f, 0f, 0.12f, 12.98f, 13.1f, IsleKit.Gold);
        Block(mb, -1.0f, -0.88f, 0f, 0.45f, 11.5f, 11.62f, IsleKit.Gold);
        Block(mb, -1.0f, -0.88f, 0f, 0.45f, 12.98f, 13.1f, IsleKit.Gold);
        Block(mb, -4.6f, -1.4f, 0.12f, 0.73f, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -4.6f, -4.2f, 0.73f, 1.45f, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -1.6f, -1.4f, 0.73f, 1.1f, 11.4f, 13.2f, IsleKit.Red);
        Block(mb, -4.2f, -1.6f, 0.73f, 0.95f, 11.4f, 11.5f, IsleKit.Gold);
        Block(mb, -4.2f, -1.6f, 0.73f, 0.95f, 13.1f, 13.2f, IsleKit.Gold);
        Kit.AddBall(mb, new Vector3(-3.9f, 1.05f, 12.85f), 0.3f, IsleKit.DarkWood);        // the sack
        Reindeer(mb, -5.2f, 9.8f, 1f);
        Reindeer(mb, -3.8f, 9.8f, 1f);
        Reindeer(mb, -2.4f, 9.8f, 1f);
        Reindeer(mb, -5.2f, 14.8f, 1f);
        OnPad("Shed Dress", mb, snowy);

        // a gold star on the bay's south wall over the sleigh (never takes snow)
        var star = new MeshBuilder();
        Kit.AddStar(star, new Vector3(-3.0f, 2.2f, 9.36f), 0.22f, 0.04f, IsleKit.Gold);
        OnPad("Bay Star", star, bare, false);
    }
}
