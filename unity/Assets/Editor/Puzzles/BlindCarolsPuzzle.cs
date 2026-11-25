// Blind Carols (puzzle 9, blind mask): the Carol Chapel, a blindfold-and-listening game in the open, with no clock.
//
// Four bell cotes stand round the chapel yard, 10 m and more apart, each with a painted bell, bands of its colour (red,
// green, gold, blue) and a speaker on its face. On every start each speaker is dealt a different one of the game's nine
// sound-peg tunes and plays it out loud, so whoever stands at a cote hears that cote's carol above the others. In the
// vestry at the yard's north end a bin holds nine speaking tiles, one of each tune, and a peg board has four slots under
// the cotes' colours. The vestry is for the blindfolded only: its turnstile lets in only a player wearing the blindfold,
// and BlindWatch puts anyone in there without one out in the porch. Its south wall has an open hatch with a shelf behind
// it (big enough to hold a tile up in, too small to climb through) and four narrow slits to see in by.
//
// The blind player carries a tile from the bin to the hatch. The sighted players walk between the cotes and the hatch,
// compare the tile by ear with the carols, say which colour it is (or "take it back") and talk the blind player's hands
// to that colour's slot. Everybody talks freely: nothing is soundproof. Four right tiles and a poke of the validator's
// button win the gourd; a wrong board only shows the cross. Two can finish it (one blind, one listening); with three or
// four, listeners can wait at the cotes and hum across the yard.
// (Before the user's rules of 2026-11-24 this was a three-role relay on a 10-minute clock: carols heard only in pews'
// headphones from speakers buried under the yard, a guide with an intercom at a soundproof window, a sealed vestry.)
//
// Coordinates: X east, Z north, h above the levelled pad (every part is placed on one plane, the ground at the
// centre). Walls are 0.24 thick and centred on the room's lines (Z 6 and 11, X -1.5, 5.5 and 9.5).
using Bwm;
using UnityEngine;

public sealed class BlindCarolsPuzzle : PuzzleDef
{
    public override string Name => "BlindCarols";
    public override Vector2 Footprint => new Vector2(30f, 26f);
    public override Vector2? FixedSite => new Vector2(-2724f, -1557f);      // layout-final: a chapel in a quiet far-west wood (Cirque Gate, M13)

    const float T = 0.12f;           // half a wall's thickness
    const float WallH = 2.8f;
    // The hatch in the vestry's south wall, 1.0 x 0.6 m: a tile (0.45 m) can be held up in it, a player cannot get through
    const float HatchX0 = 1.5f, HatchX1 = 2.5f, HatchSill = 0.8f, HatchTop = 1.4f;
    // The slits beside it, 0.26 m wide: narrower than a tile's thin side (0.34 m); the room is seen through them from
    // any eye height
    const float SlitHalf = 0.13f, SlitSill = 0.4f, SlitTop = 2.1f;

    PuzzleBuilder b;
    float baseH;                     // the pad's level above the root's

    Piece P(string prefab, float x, float z, float h, float yaw) => b.Part(prefab, x, z, baseH + h, yaw, false);
    Piece E(string name, float x, float z, float h) => b.Empty(name, x, z, baseH + h, false);
    Piece MeshAt(string name, MeshBuilder mb, Material m, bool collider = true)
    {
        Piece p = b.MeshPiece(name, mb.ToMesh(), m, collider);
        p.Transform.position = b.WorldPoint(0f, 0f, baseH, false);
        return p;
    }

    // A block from its two corners (x0..x1, z0..z1, h0..h1), into a mesh in the pad's frame
    static void Blk(MeshBuilder mb, float x0, float x1, float z0, float z1, float h0, float h1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) * 0.5f, h0, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h1 - h0, z1 - z0), c);

    // A bell cote at (x, z), its face turned to 'yaw' (0 north, 90 east, -90 west): a little cream tower on a stone
    // plinth with a listening step in front of it, bands of its colour all round, a painted gold bell on the face and
    // under the bell the carol's speaker (a Sound Peg Answer: it plays out loud while the puzzle runs). A warm lamp
    // lights the face by night (Winter Walk's RoomLights), so the colour can be read. Each cote is a mesh of its own,
    // so that it takes its own lamp. Returns the speaker's answer component.
    Component Cote(string colour, Color band, float x, float z, float yaw, int lamp, Material snowy, Material bare)
    {
        // a point 'across' to the face's right and 'ahead' of the cote's middle, h above the pad
        Vector3 At(float across, float h, float ahead)
        {
            Vector2 o = IsleKit.Turn(across, ahead, yaw);
            return new Vector3(x + o.x, h, z + o.y);
        }
        var body = new MeshBuilder();
        Kit.AddBox(body, At(0f, -0.6f, 0f), new Vector3(1.6f, 1.0f, 1.6f), IsleKit.Stone, yaw);          // plinth, top at 0.4
        Kit.AddBox(body, At(0f, -0.3f, 1.5f), new Vector3(2.0f, 0.45f, 1.4f), IsleKit.Stone, yaw);        // listening step, top at 0.15
        Kit.AddBox(body, At(0f, 0.4f, 0f), new Vector3(1.0f, 2.2f, 1.0f), IsleKit.Cream, yaw);
        Kit.AddBox(body, At(0f, 2.6f, 0f), new Vector3(1.2f, 0.15f, 1.2f), IsleKit.DarkWood, yaw);
        MeshAt("Cote " + colour, body, snowy);
        var paint = new MeshBuilder();
        Kit.AddBox(paint, At(0f, 2.28f, 0f), new Vector3(1.06f, 0.17f, 1.06f), band, yaw);
        Kit.AddBox(paint, At(0f, 0.7f, 0f), new Vector3(1.06f, 0.1f, 1.06f), band, yaw);
        Kit.AddBall(paint, At(0f, 1.9f, 0.5f), 0.2f, IsleKit.Gold);
        MeshAt("Cote " + colour + " Paint", paint, bare, false);
        Vector3 face = At(0f, 1.3f, 0.5f), lampAt = At(0f, 2.4f, 1.0f);
        E("Room Light Night " + lamp + " FFE2B8 4.0 1.2", lampAt.x, lampAt.z, lampAt.y);
        return P("Peg Sound Mounted", face.x, face.z, face.y, yaw).Named("Carol " + colour).Component("PuzzleSoundPegAnswer");
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        baseH = b.WorldPoint(0f, 0f).y - b.Root.transform.position.y;
        b.ClearGround(30f, 26f);
        b.DebugTeleport(2f, 3f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // no jetpacks over the chapel (Winter Walk's Calm Air): the whole yard, from 10 m under the pad to 120 m above
        IsleKit.CalmAir(b, "Blind Carols", 0f, 0f, new Vector3(40f, 130f, 36f), 0f, baseH - 10f, false);

        // ================= the vestry and its porch (room X -1.5..5.5, porch 5.5..9.5, Z 6..11) =================
        var walls = new MeshBuilder();
        float xw = -1.5f - T, xe = 9.5f + T;
        Blk(walls, xw, xe, 6f - T, 11f + T, -0.6f, 0f, IsleKit.Stone);                     // footing
        // south wall: solid but for the hatch and two slits each side of it, a sill under and a lintel over each
        float built = xw;
        void Opening(float x0, float x1, float sill, float top)
        {
            Blk(walls, built, x0, 6f - T, 6f + T, 0f, WallH, IsleKit.Wood);
            Blk(walls, x0, x1, 6f - T, 6f + T, 0f, sill, IsleKit.Wood);
            Blk(walls, x0, x1, 6f - T, 6f + T, top, WallH, IsleKit.Wood);
            built = x1;
        }
        foreach (float slit in new[] { -0.3f, 0.7f }) Opening(slit - SlitHalf, slit + SlitHalf, SlitSill, SlitTop);
        Opening(HatchX0, HatchX1, HatchSill, HatchTop);
        foreach (float slit in new[] { 3.3f, 4.3f }) Opening(slit - SlitHalf, slit + SlitHalf, SlitSill, SlitTop);
        Blk(walls, built, xe, 6f - T, 6f + T, 0f, WallH, IsleKit.Wood);
        // the hatch's frame on the outer face, and a sill board
        Blk(walls, HatchX0 - 0.08f, HatchX0, 5.84f, 6f - T, HatchSill, HatchTop, IsleKit.DarkWood);
        Blk(walls, HatchX1, HatchX1 + 0.08f, 5.84f, 6f - T, HatchSill, HatchTop, IsleKit.DarkWood);
        Blk(walls, HatchX0 - 0.08f, HatchX1 + 0.08f, 5.84f, 6f - T, HatchTop, HatchTop + 0.08f, IsleKit.DarkWood);
        Blk(walls, HatchX0 - 0.08f, HatchX1 + 0.08f, 5.78f, 6f - T, HatchSill - 0.06f, HatchSill, IsleKit.DarkWood);
        // north and west walls
        Blk(walls, xw, xe, 11f - T, 11f + T, 0f, WallH, IsleKit.Wood);
        Blk(walls, xw, -1.5f + T, 6f + T, 11f - T, 0f, WallH, IsleKit.Wood);
        // the room's east wall, with the opening into the porch at Z 7.9..9.1, h 0..2.3
        Blk(walls, 5.5f - T, 5.5f + T, 6f + T, 7.9f, 0f, WallH, IsleKit.Wood);
        Blk(walls, 5.5f - T, 5.5f + T, 9.1f, 11f - T, 0f, WallH, IsleKit.Wood);
        Blk(walls, 5.5f - T, 5.5f + T, 7.9f, 9.1f, 2.3f, WallH, IsleKit.Wood);
        // the porch's outer wall: the two gates fill it (Z 6.04..8.16 and 8.84..10.96), a pier between, lintels over
        Blk(walls, 9.5f - T, 9.5f + T, 8.16f, 8.84f, 0f, 2.3f, IsleKit.Wood);
        Blk(walls, 9.5f - T, 9.5f + T, 6f + T, 11f - T, 2.3f, WallH, IsleKit.Wood);
        // the baffle in the porch: no straight line from either gate reaches the room's opening
        Blk(walls, 7.6f - T, 7.6f + T, 6.7f, 10.3f, 0f, WallH, IsleKit.Wood);
        // the roof over both
        Blk(walls, -1.7f, 9.7f, 5.7f, 11.3f, WallH, WallH + 0.24f, IsleKit.Red);
        MeshAt("Vestry Walls", walls, snowy);

        // inside (never snowy): the floor, the shelf behind the hatch at its sill's height with a lip on the room's
        // side (a tile set down there plays in the hatch), the tile bin in the NW corner
        var fit = new MeshBuilder();
        Blk(fit, -1.5f + T, 9.5f - T, 6f + T, 11f - T, 0f, 0.04f, IsleKit.Wood);
        Blk(fit, HatchX0 - 0.2f, HatchX1 + 0.2f, 6f + T, 6.62f, 0f, HatchSill, IsleKit.DarkWood);
        Blk(fit, HatchX0 - 0.2f, HatchX1 + 0.2f, 6.59f, 6.62f, HatchSill, HatchSill + 0.03f, IsleKit.DarkWood);
        const float binX0 = -1.32f, binX1 = 0.45f, binZ0 = 9.4f, binZ1 = 10.84f, binSide = 0.06f;
        Blk(fit, binX0, binX1, binZ0, binZ1, 0f, 0.6f, IsleKit.Wood);
        Blk(fit, binX0, binX1, binZ0, binZ0 + binSide, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX0, binX1, binZ1 - binSide, binZ1, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX0, binX0 + binSide, binZ0 + binSide, binZ1 - binSide, 0.6f, 0.95f, IsleKit.Wood);
        Blk(fit, binX1 - binSide, binX1, binZ0 + binSide, binZ1 - binSide, 0.6f, 0.95f, IsleKit.Wood);
        MeshAt("Vestry Fittings", fit, bare);

        // nine speaking tiles jumbled in the bin: which tile plays which tune is shuffled on every build; they stand in
        // a 3 x 3 grid (cells 0.55 x 0.44 m for a 0.448 x 0.337 m tile) with a little play in place and turn (up to
        // 10 degrees), so none starts inside another or the bin; a tile's body sits 0.0685 m behind its pivot
        var rng = new System.Random((int)(System.DateTime.Now.Ticks & 0x7fffffff));
        int[] tunes = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        for (int i = tunes.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = tunes[i]; tunes[i] = tunes[j]; tunes[j] = t; }
        float cellX = (binX1 - binX0 - 2f * binSide) / 3f, cellZ = (binZ1 - binZ0 - 2f * binSide) / 3f;
        for (int i = 0; i < 9; i++)
        {
            float x = binX0 + binSide + cellX * (i % 3 + 0.5f) + (float)(rng.NextDouble() - 0.5) * 0.04f;
            float z = binZ0 + binSide + cellZ * (i / 3 + 0.5f) + 0.0685f + (float)(rng.NextDouble() - 0.5) * 0.02f;
            float yaw = (float)(rng.NextDouble() - 0.5) * 20f;
            P("Peg Sound", x, z, 0.85f, yaw).Named("Speaking Tile " + (i + 1)).Set("PuzzleSoundPeg", "sound", tunes[i]);
        }

        // the gates, neither ever locked: in blindfolded (one way, east to west), out (one way, west to east)
        Piece blindGate = IsleKit.BlindGate(b, 9.5f, 7.1f, -90f, baseH, false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        (float outX, float outZ, float outYaw) = IsleKit.OneWay(9.5f, 9.9f, 90f);      // walked west to east
        Piece wayOut = P("Gate No items", outX, outZ, 0f, outYaw).Named("Way Out")
            .Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

        // a hook by the Way Out for a blindfold the plain gate will not let through, and three on the rack outside
        Piece back = P("Hook", 8.3f, 11f - T - 0.03f, 1.55f, 180f).Named("Blindfold Return");
        Sdk.SetFlags(back.Component("PuzzleHook"), "acceptedVanilla", 1);
        IsleKit.BlindfoldHook(b, 12.6f, 4.6f, -90f, baseH, false, " A");
        IsleKit.BlindfoldHook(b, 12.6f, 5.8f, -90f, baseH, false, " B");
        IsleKit.BlindfoldHook(b, 12.6f, 7.0f, -90f, baseH, false, " C");

        // BlindWatch: anyone in the room or the inner porch without a blindfold drops what they hold and is put in the
        // porch (outside its box and 1.2 m clear of both gates), from where only the Way Out leads on
        Piece watch = E("Blind Watch", 2f, 8.5f, 0f);
        Piece spill = E("Porch Spill", 8.45f, 8.5f, 0.1f);
        spill.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        Component bw = b.AddCustomScript(watch, "BlindWatch");
        Sdk.Set(bw, "boxMin", new Vector3(-3.4f, -0.5f, -2.4f));      // X -1.4, Z 6.1
        Sdk.Set(bw, "boxMax", new Vector3(6.1f, 2.6f, 2.4f));         // X 8.1, Z 10.9
        Sdk.Set(bw, "putOut", spill.Transform);
        Sdk.Add(bw, "gates", blindGate.Transform, wayOut.Transform);
        Sdk.Set(bw, "gateSkip", 1.2f);
        Sdk.Set(bw, "cooldown", 1.5f);

        // warm room lamps so the others can see in at night (Winter Walk's RoomLights)
        E("Room Light 1 FFC88C 6.0 0.8", 0.5f, 8.5f, 2.5f);
        E("Room Light 2 FFC88C 6.0 0.8", 4.0f, 8.5f, 2.5f);

        // ================= the yard: the step under the hatch and the slits =================
        var yard = new MeshBuilder();
        Blk(yard, -0.8f, 4.8f, 4.25f, 5.75f, -0.3f, 0.15f, IsleKit.Stone);
        MeshAt("Chapel Yard", yard, snowy);

        // ================= the four bell cotes, each playing its carol, facing the yard's middle =================
        // their speakers are 10.1 m (red to green, gold to blue), 12 m (green to gold) and 23 m (red to blue) apart,
        // and the nearest (blue) is 11.7 m from the hatch
        Component red = Cote("Red", IsleKit.Red, -12f, -1f, 90f, 1, snowy, bare);
        Component green = Cote("Green", IsleKit.Green, -6f, -10f, 0f, 2, snowy, bare);
        Component gold = Cote("Gold", IsleKit.Gold, 6f, -10f, 0f, 3, snowy, bare);
        Component blue = Cote("Blue", IsleKit.Ice, 12f, -1f, -90f, 4, snowy, bare);

        // ================= the peg board on the vestry's north wall, its validator, a colour bar over each slot =================
        // The board's pivot is at its west end and its slots run 0.45 m apart towards +X: red, green, gold, blue. Each
        // slot's answer is that cote's speaker (answerFromFlap 1 for each: a speaker has only the one). A right check
        // wins; nothing is wired to a wrong one, so the cross is all it costs.
        const float pivotX = 1.025f;
        Piece validator = P("Validator", 3.5f, 10.86f, 1.65f, 180f).Named("Validator");
        Component boardC = P("Peg Board", pivotX, 10.86f, 1.3f, 180f).Named("Carol Board").Component("PuzzlePegBoard");
        Sdk.Set(boardC, "size", 3);      // Slots4
        Sdk.Add(boardC, "answerFrom", red, green, gold, blue);
        Sdk.AddInts(boardC, "answerFromFlap", 1, 1, 1, 1);
        Sdk.Set(boardC, "validator", validator.Component("PuzzleValidator"));
        Color[] colours = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, IsleKit.Ice };
        var marks = new MeshBuilder();
        for (int i = 0; i < colours.Length; i++)
        {
            float x = pivotX + 0.45f * i;
            Blk(marks, x - 0.15f, x + 0.15f, 10.85f, 10.88f, 1.56f, 1.68f, colours[i]);
        }
        MeshAt("Slot Marks", marks, bare, false);

        // ================= the start corner and the lights =================
        IsleKit.StartCorner(b, -5f, 0.5f, 90f, true, baseH, false);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 28f, 24f, 2.6f, true, 91);
    }
}
