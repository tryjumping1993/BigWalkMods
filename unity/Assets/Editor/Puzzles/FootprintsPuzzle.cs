// Footprints: the Signal Field. A fenced snowfield holds sixteen pads hidden under the snow, four rows by four
// columns, named only by stakes: row stakes along the side fences (red, green, gold, ice-blue, front to back) and
// column stakes along the front and back fences (short to tall, one to four white bands, left to right).
//
// A tall lamp board stands on the Signal Cab's roof at the field's front edge, facing the field: sixteen lamps, a
// row of them on each row's colour, a column over each column's bands. One lamp is lit. Find that pad from its two
// stakes and stand on it; after a moment it counts, the lamp goes out and another lights (Seek, in PuzzleScripts).
// Six pads win. There is no clock, treading on other pads costs nothing, and the only trace of where you have been
// is your own tracks in the snow. One player can do it alone; with more, somebody on the cab's platform sees the whole
// field and can call the walkers on.
// (Before the user's rules of 2026-11-24 the lamps were in a sealed, soundproof hut, heard through a microphone in
// the headphones of a chair in the cab, and each pad had 9 seconds: a three-link relay on a clock.)
//
// Coordinates: x right, z forward (towards the back of the field), heights above the ground; the cab's floor is
// 2.6 m up. Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z.
using Bwm;
using UnityEngine;

public sealed class FootprintsPuzzle : PuzzleDef
{
    public override string Name => "Footprints";
    public override Vector2 Footprint => new Vector2(24f, 40f);
    // A level shelf above a beach on the south-east coast (layout-final.json); the island's build levels and clears it
    public override Vector2? FixedSite => new Vector2(-798f, -1980f);

    const float CabFloor = 2.6f;          // the Signal Cab's floor, above the ground
    const int Rounds = 6;                 // pads to find
    const float Dwell = 1.5f;             // seconds on the lit pad before it counts
    const float BarScale = 0.6f;          // the bar inside the cab
    const float BarHalfDepth = 0.101f;    // a Progress Bar's back is 0.101 m behind its pivot (part-metrics)
    const float SwitchBack = 0.073f;      // a Switch Start's back is 0.073 m behind its pivot
    const float LiveBack = 0.006f;        // a Live Light's back is 0.006 m behind its pivot

    static readonly float[] PadX = { -6.3f, -2.1f, 2.1f, 6.3f };      // columns 1..4, left to right
    static readonly float[] PadZ = { 1.0f, 5.2f, 9.4f, 13.6f };       // rows 1..4, front (nearest the cab) to back
    // The lamp board on the cab's roof: its face, the lamps' columns (west to east, as the pads lie) and rows (the
    // front row at the bottom), heights above the ground
    const float BoardFace = -4.38f;
    static readonly float[] LampX = { -0.9f, -0.3f, 0.3f, 0.9f };
    static readonly float[] LampH = { CabFloor + 3.45f, CabFloor + 4.05f, CabFloor + 4.65f, CabFloor + 5.25f };
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one

    // Field fence: x -9.2..9.2, z -2.2..16.6; front gates 1.6 m wide centred at x = -4.2 and 4.2
    const float FenceX = 9.2f, FenceZ0 = -2.2f, FenceZ1 = 16.6f, FenceH = 1.8f, GateX = 4.2f, GateHalf = 0.8f;

    public override void Build(PuzzleBuilder b)
    {
        b.ClearGround(24f, 40f);
        b.DebugTeleport(3f, -11f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Color[] rowColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice };

        // Jetpacks do not fire anywhere over the puzzle
        IsleKit.CalmAir(b, "Footprints", 0f, 0f, new Vector3(44f, 150f, 60f), 0f, -10f);

        // ================= the field =================
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                IsleKit.Sensor(b, "Pad " + (r + 1) + "-" + (c + 1), PadX[c], PadZ[r], new Vector3(2.0f, 2.6f, 2.0f))
                    .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        BuildFence(b, snowy, rowColour);
        BuildStakes(b, snowy, rowColour);

        // the back board, behind the back fence, facing the field and the cab: the count
        IsleKit.BoardOnPosts(b, "Back Board", 0f, 18f, 180f, 3.8f, 1.9f, 3.2f, IsleKit.Board);
        b.Part("Progress Bar", 0f, 17.87f, 2.55f, 180f).Named("Seek Bar Back").Set("PuzzleProgressBar", "total", Rounds);

        // the field corner's start switch, the gourd's vice, the pillow and the confetti
        IsleKit.StartCorner(b, 6.5f, -7.5f, 0f);

        // ================= the Signal Cab: a covered platform that looks over the whole field =================
        // platform x -2.2..2.2, z -7.6..-4.4, floor 2.6 m; inside x -2.0..2.0, z -7.4..-4.6; steps up behind it
        const float CabZ = -6f;
        Place(b, "Signal Cab", CabMesh(), snowy, 0f, CabZ);
        float cabWest = -2.0f + BarHalfDepth * BarScale, cabEast = 2.0f;
        b.Part("Switch Start", cabEast - SwitchBack, -6.9f, CabFloor + 1.25f, -90f).Named("Cab Switch");
        b.Part("Live Light", cabEast - LiveBack, -6.97f, CabFloor + 1.42f, -90f).Named("Cab Live");
        b.Part("Progress Bar", cabWest, -6.0f, CabFloor + 1.9f, 90f).Named("Seek Bar Cab")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", Rounds);
        b.Empty("Room Light Cab FFE2B8 2.4 0.8", -0.5f, -6.4f, CabFloor + 2.3f);

        // ================= the lamp board on the cab's roof, facing the field =================
        // One lamp per pad, laid out as the pads lie seen from the field: west to the west, the front row (nearest
        // the cab) at the bottom. Each row of lamps sits on its row's colour and each column over its column's bands,
        // as on the stakes.
        var sign = new MeshBuilder();
        float signLow = CabFloor + 2.8f, signTop = CabFloor + 6.35f;
        Block(sign, 0f, CabZ, -1.5f, 1.5f, signLow, signTop, BoardFace - 0.12f, BoardFace, IsleKit.Board);
        foreach (float px in new[] { -1.4f, 1.4f })
            Block(sign, 0f, CabZ, px - 0.08f, px + 0.08f, signLow, signTop - 0.4f, BoardFace - 0.9f, BoardFace - 0.12f, IsleKit.DarkWood);      // braces behind
        Block(sign, 0f, CabZ, -1.7f, 1.7f, signTop, signTop + 0.12f, BoardFace - 0.4f, BoardFace + 0.25f, IsleKit.DarkWood);      // a little roof
        Place(b, "Lamp Board", sign, snowy, 0f, CabZ);
        var paint = new MeshBuilder();
        for (int r = 0; r < 4; r++)
            Block(paint, 0f, CabZ, -1.42f, 1.42f, LampH[r] - 0.27f, LampH[r] + 0.27f, BoardFace, BoardFace + 0.01f, rowColour[r]);
        for (int c = 0; c < 4; c++)
            for (int band = 0; band <= c; band++)
            {
                float y = signLow + 0.08f + band * 0.07f;
                Block(paint, 0f, CabZ, LampX[c] - 0.12f, LampX[c] + 0.12f, y, y + 0.045f, BoardFace, BoardFace + 0.01f, IsleKit.Cream);
            }
        Piece paintPiece = b.MeshPiece("Lamp Board Paint", paint.ToMesh(), bare, false);
        paintPiece.Transform.position = b.WorldPoint(0f, CabZ);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                b.Part("Light 6", LampX[c], BoardFace + 0.142f + 0.012f, LampH[r], 0f).Named("Lamp " + (r + 1) + "-" + (c + 1))
                    .Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 3f);
        b.Part("Progress Bar", 0f, BoardFace + BarHalfDepth + 0.012f, CabFloor + 5.95f, 0f).Named("Seek Bar Board")
            .Scaled(0.75f, 0.75f, 0.75f).Set("PuzzleProgressBar", "total", Rounds);

        // ================= the rule: go to the lit pad; six found win (Seek.cs). Start deals, stop clears =================
        Component seek = b.AddCustomScript(b.Empty("Field Seek", 0f, 7f, 0.5f), "Seek");
        Sdk.Set(seek, "sensorPrefix", "Pad ");
        Sdk.Set(seek, "lampPrefix", "Lamp ");
        Sdk.Set(seek, "barPrefix", "Seek Bar");
        Sdk.Set(seek, "rounds", Rounds);
        Sdk.Set(seek, "dwell", Dwell);
        Sdk.Set(seek, "need", 1);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", seek, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", seek, "End");

        Kit.LightRing(b, "Site Lights", 0f, 0f, 22f, 38f, 2.6f, true, 161);
    }

    // ---- helpers ----

    // A block given by its bounds in the puzzle's local metres (x, y up, z), added to a mesh whose piece stands at
    // local (ox, oz)
    static void Block(MeshBuilder mb, float ox, float oz, float x0, float x1, float y0, float y1, float z0, float z1, Color colour)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f - ox, y0, (z0 + z1) / 2f - oz), new Vector3(x1 - x0, y1 - y0, z1 - z0), colour);

    // Adds a mesh piece standing on the ground at local (ox, oz)
    static Piece Place(PuzzleBuilder b, string name, MeshBuilder mb, Material material, float ox, float oz)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material);
        piece.Transform.position = b.WorldPoint(ox, oz);
        return piece;
    }

    // The field fence: solid boards along the sides and back, vertical slats along the front (the cab sees through,
    // nobody passes) with two gate gaps, posts about every 2 m, and a colour stripe on the side boards behind each
    // row stake (inside and out)
    static void BuildFence(PuzzleBuilder b, Material snowy, Color[] rowColour)
    {
        float oz = (FenceZ0 + FenceZ1) / 2f;
        var mb = new MeshBuilder();
        // side boards (x ±9.1..9.2) and the back boards (z 16.5..16.6)
        Block(mb, 0f, oz, -FenceX, -FenceX + 0.1f, -0.1f, FenceH, FenceZ0, FenceZ1, IsleKit.Wood);
        Block(mb, 0f, oz, FenceX - 0.1f, FenceX, -0.1f, FenceH, FenceZ0, FenceZ1, IsleKit.Wood);
        Block(mb, 0f, oz, -FenceX, FenceX, -0.1f, FenceH, FenceZ1 - 0.1f, FenceZ1, IsleKit.Wood);

        // front: slats 0.06 wide every 0.25 m, rails top and bottom, all left out across the two gates
        bool InGate(float x0, float x1) => (x1 > -GateX - GateHalf && x0 < -GateX + GateHalf) || (x1 > GateX - GateHalf && x0 < GateX + GateHalf);
        for (float x = -FenceX + 0.125f; x < FenceX; x += 0.25f)
            if (!InGate(x - 0.03f, x + 0.03f))
                Block(mb, 0f, oz, x - 0.03f, x + 0.03f, -0.1f, FenceH, FenceZ0, FenceZ0 + 0.06f, IsleKit.Wood);
        float[] railFrom = { -FenceX, -GateX + GateHalf, GateX + GateHalf };
        float[] railTo = { -GateX - GateHalf, GateX - GateHalf, FenceX };
        for (int i = 0; i < 3; i++)
        {
            Block(mb, 0f, oz, railFrom[i], railTo[i], 0.15f, 0.25f, FenceZ0 + 0.06f, FenceZ0 + 0.12f, IsleKit.DarkWood);
            Block(mb, 0f, oz, railFrom[i], railTo[i], FenceH - 0.12f, FenceH - 0.02f, FenceZ0 + 0.06f, FenceZ0 + 0.12f, IsleKit.DarkWood);
        }

        // posts: about every 2 m round the fence, none in a gate gap, and one on each side of each gate
        const float P = 0.07f;
        for (int i = 0; i <= 10; i++)
        {
            float z = Mathf.Lerp(FenceZ0, FenceZ1, i / 10f);
            Block(mb, 0f, oz, -FenceX - P, -FenceX + P, -0.1f, FenceH + 0.1f, z - P, z + P, IsleKit.DarkWood);
            Block(mb, 0f, oz, FenceX - P, FenceX + P, -0.1f, FenceH + 0.1f, z - P, z + P, IsleKit.DarkWood);
            float x = Mathf.Lerp(-FenceX, FenceX, i / 10f);
            Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.1f, FenceZ1 - P, FenceZ1 + P, IsleKit.DarkWood);
            if (!InGate(x - P - 0.05f, x + P + 0.05f))
                Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.1f, FenceZ0 - P + 0.03f, FenceZ0 + P + 0.03f, IsleKit.DarkWood);
        }
        foreach (float g in new[] { -GateX, GateX })
            foreach (float side in new[] { -1f, 1f })
            {
                float x = g + side * (GateHalf + P);
                Block(mb, 0f, oz, x - P, x + P, -0.1f, FenceH + 0.25f, FenceZ0 - P + 0.03f, FenceZ0 + P + 0.03f, IsleKit.DarkWood);
            }

        // a 0.3 m colour stripe on the side boards behind each row's stakes, inside and outside
        for (int r = 0; r < 4; r++)
            foreach (float s in new[] { -1f, 1f })
            {
                float inner = s * (FenceX - 0.1f), outer = s * FenceX;
                Block(mb, 0f, oz, Mathf.Min(inner, inner - s * 0.02f), Mathf.Max(inner, inner - s * 0.02f), 0.2f, 1.7f, PadZ[r] - 0.15f, PadZ[r] + 0.15f, rowColour[r]);
                Block(mb, 0f, oz, Mathf.Min(outer, outer + s * 0.02f), Mathf.Max(outer, outer + s * 0.02f), 0.2f, 1.7f, PadZ[r] - 0.15f, PadZ[r] + 0.15f, rowColour[r]);
            }
        Place(b, "Field Fence", mb, snowy, 0f, oz);
    }

    // Row stakes (red, green, gold, ice-blue, front to back) inside both side fences; column stakes (0.6 to 1.5 m,
    // one to four white bands, left to right) inside the front and back fences
    static void BuildStakes(PuzzleBuilder b, Material snowy, Color[] rowColour)
    {
        float oz = (FenceZ0 + FenceZ1) / 2f;
        var mb = new MeshBuilder();
        for (int r = 0; r < 4; r++)
            foreach (float x in new[] { -8.5f, 8.5f })
                Block(mb, 0f, oz, x - 0.08f, x + 0.08f, 0f, 1.5f, PadZ[r] - 0.08f, PadZ[r] + 0.08f, rowColour[r]);
        for (int c = 0; c < 4; c++)
        {
            float h = 0.6f + 0.3f * c;
            foreach (float z in new[] { -1.6f, 16.0f })
            {
                Block(mb, 0f, oz, PadX[c] - 0.07f, PadX[c] + 0.07f, 0f, h, z - 0.07f, z + 0.07f, IsleKit.Wood);
                for (int band = 0; band <= c; band++)
                {
                    float top = h - 0.06f - band * 0.14f;
                    Block(mb, 0f, oz, PadX[c] - 0.085f, PadX[c] + 0.085f, top - 0.07f, top, z - 0.085f, z + 0.085f, IsleKit.Cream);
                }
            }
        }
        Place(b, "Stakes", mb, snowy, 0f, oz);
    }

    // The Signal Cab, in its own frame (its piece stands at local (0, -6)): the platform, the walls with the slatted
    // front window and the open doorway at the back, the roof (the lamp board stands on it), and the steps up
    static MeshBuilder CabMesh()
    {
        const float oz = -6f, f = CabFloor, top = CabFloor + 2.7f;
        var mb = new MeshBuilder();
        Block(mb, 0f, oz, -2.2f, 2.2f, -0.3f, f, -7.6f, -4.4f, IsleKit.Stone);                  // platform
        // front wall: solid to 0.4 m above the floor, a window to 2.2 m filled with slats, solid above
        Block(mb, 0f, oz, -2.2f, 2.2f, f, f + 0.4f, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -2.2f, 2.2f, f + 2.2f, top, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -2.2f, -1.8f, f + 0.4f, f + 2.2f, -4.6f, -4.4f, IsleKit.Wood);
        Block(mb, 0f, oz, 1.8f, 2.2f, f + 0.4f, f + 2.2f, -4.6f, -4.4f, IsleKit.Wood);
        for (int k = 1; k <= 14; k++)
        {
            float x = -1.8f + 0.25f * k;
            Block(mb, 0f, oz, x - 0.03f, x + 0.03f, f + 0.4f, f + 2.2f, -4.54f, -4.46f, IsleKit.DarkWood);
        }
        // back wall: a 1.24 m door opening at x = 0 with a lintel from 2.02 m above the floor
        Block(mb, 0f, oz, -2.2f, -0.62f, f, top, -7.6f, -7.4f, IsleKit.Wood);
        Block(mb, 0f, oz, 0.62f, 2.2f, f, top, -7.6f, -7.4f, IsleKit.Wood);
        Block(mb, 0f, oz, -0.62f, 0.62f, f + 2.02f, top, -7.6f, -7.4f, IsleKit.Wood);
        // side walls
        Block(mb, 0f, oz, -2.2f, -2.0f, f, top, -7.4f, -4.6f, IsleKit.Wood);
        Block(mb, 0f, oz, 2.0f, 2.2f, f, top, -7.4f, -4.6f, IsleKit.Wood);
        // roof 4.8 x 3.6
        Block(mb, 0f, oz, -2.4f, 2.4f, f + 2.6f, f + 2.8f, -7.8f, -4.2f, IsleKit.Red);
        // steps: 9 rises up to the floor, 0.3 m treads, from z -10.3 to the door at -7.6
        for (int i = 0; i < 9; i++)
        {
            float z0 = -10.3f + 0.3f * i;
            Block(mb, 0f, oz, -0.8f, 0.8f, -0.3f, f * (i + 1) / 9f, z0, z0 + 0.3f, IsleKit.Stone);
        }
        return mb;
    }
}
