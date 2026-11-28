// Santa's List (puzzle 8): "he's making a list, he's checking it twice". A spot-the-difference game for a group, in
// the open, with no clock.
//
// Two lists of five pictures hang far apart. They are the same but for two places.
// - List A hangs on the far end wall of Santa's Long Hall, a sealed log gallery 31 m long with a flat ceiling, solid
//   gables and a sealed roof. Its only opening is a glazed keyhole into the Lookout's hut at the north end, where a
//   telescope looks down the hall. The hut is open to anyone.
// - List B hangs in the Post Office at the south end of the yard, over the counter hatch on the wall facing the
//   doorway: walk in and read it.
// - Under each list a row of plaques numbers its places with one to five pips (laid out as on a die), left to right
//   as a reader sees them.
// - The Checking Desk is the board behind the Sleigh: the same five plaques, a button under each and a lamp over it.
//   A press marks that place (its lamp lights), a second press unmarks it. Mark the two places where the lists differ
//   and press the button under the check screen: a tick wins (the gourd at the start corner, the confetti); a cross
//   costs nothing, and the lists stay as they are.
// The players compare the lists by calling the pictures across the yard (two walkie-talkies lie on the table), or by
// walking between them: about 50 m on foot from the telescope to the Post Office's door. One player can do it alone
// by walking back and forth; it is built for three or four.
// The lists are dealt by our script Differ (PuzzleScripts) a moment after each start, so the puzzle has no Peg Tiles.
// One Start Switch, at the start corner. Calm Air over the whole site: no jetpack flight onto the roofs.
// (Before the user's rules of 2026-11-24 it was a three-role relay: a Lookout at the telescope, a Postmaster shut in a
// soundproof, radio-dead booth who could only point at tiles through the glass, and a Packer hanging six tiles on a
// board, checked by sleigh-bell levers pulled together, with three strikes and a five-minute clock.)
//
// Coordinates: local metres from the root, x east, z north (+z forward); heights above the pad top (Floor above the
// root's level), every piece placed onGround: false on one level pad. Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SantasListPuzzle : PuzzleDef
{
    public override string Name => "SantasList";
    public override Vector2 Footprint => new Vector2(20f, 44f);
    public override Vector2? FixedSite => new Vector2(-816f, -1332f);      // layout-final: "Post Office by the station", East Lowland

    const float Floor = 0.25f;          // top of the pad, above the root's level
    const int Places = 5;               // pictures in a list
    // A five-flap Display's pivot is its flap 1 (a reader's far left); the other flaps follow 0.35 m apart to the
    // reader's right, and the frame ends 0.21 m beyond the first and the last (the game's 5-slot frame at the SDK's
    // scale of 0.7: world-index.tsv, MasterSequenceSplitFlap5Slot). Its back is 0.168 m behind the pivot.
    const float FlapPitch = 0.35f, DisplayBack = 0.17f;
    const float HallEndZ = -13.7f;      // the inner face of the hall's south end wall: List A's wall
    const float ListAX = 7.2f, ListAH = 1.5f;           // List A's flap 1 (the east end: a reader looks south), at the keyhole's height
    const float PostWallZ = -10.5f;     // the inner face of the Post Office's north wall: List B's wall
    const float ListBX = -2.2f, ListBH = 2.0f;          // List B's flap 1 (the west end: a reader looks north), over the hatch
    const float HatchSill = 0.5f, HatchTop = 1.45f;     // the Post Office's counter hatch
    // The Checking Desk: a board on the north face of the Sleigh's sack panel. Place 1 is the east end (a reader looks
    // south); each column is a button, its plaque and its lamp, bottom to top
    const float DeskZ = -2.32f;
    const float MarkX = -1.0f, MarkPitch = 0.5f;
    const float ButtonH = 1.15f, PlaqueH = 1.38f, LampH = 1.66f;
    const float CheckX = -3.75f, CheckH = 1.6f;         // the check screen; its own button hangs 0.444 m under it

    static readonly Color Pip = new Color(0.96f, 0.96f, 0.96f);
    static readonly Color DarkRed = new Color(0.45f, 0.06f, 0.06f);
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one

    PuzzleBuilder b;

    // ---------------------------------------------------------------- helpers

    // A block between two corners (local x, y above the pad top, z), corners in any order
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
    {
        float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1), za = Mathf.Min(z0, z1), zb = Mathf.Max(z0, z1);
        Kit.AddBox(mb, new Vector3((xa + xb) / 2f, Mathf.Min(y0, y1), (za + zb) / 2f), new Vector3(xb - xa, Mathf.Abs(y1 - y0), zb - za), color);
    }

    // A triangle wound to face away from 'centre' (the middle of the convex solid it belongs to)
    static void TriOut(MeshBuilder mb, Vector3 a, Vector3 p, Vector3 q, Vector3 centre, Color color)
    {
        Vector3 n = Vector3.Cross(p - a, q - a);
        if (Vector3.Dot(n, (a + p + q) / 3f - centre) < 0f) { Vector3 t = p; p = q; q = t; }
        mb.Tri(a, p, q, color);
    }

    // A convex prism: a flat convex outline (any plane) swept along 'along' (gables, roof slabs)
    static void Extrude(MeshBuilder mb, Vector3[] outline, Vector3 along, Color color)
    {
        int n = outline.Length;
        Vector3 centre = Vector3.zero;
        foreach (Vector3 p in outline) centre += p;
        centre = centre / n + along / 2f;
        for (int i = 1; i + 1 < n; i++)
        {
            TriOut(mb, outline[0], outline[i], outline[i + 1], centre, color);
            TriOut(mb, outline[0] + along, outline[i] + along, outline[i + 1] + along, centre, color);
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outline[i], c = outline[(i + 1) % n];
            TriOut(mb, a, c, c + along, centre, color);
            TriOut(mb, a, c + along, a + along, centre, color);
        }
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

    // An empty marker on the pad
    Piece E(string name, float x, float z, float height) => b.Empty(name, x, z, Floor + height, false);

    // The mark of one place (1 to 5) on a wall: a dark square plaque 'side' across centred on (cx, cy), its back on the
    // wall plane z = wallZ, facing +z (facing = 1) or -z (facing = -1), with that many white pips laid out as on a die
    static void AddPlaceMark(MeshBuilder mb, float cx, float cy, float wallZ, float facing, int place, float side)
    {
        // u runs to the viewer's right: a viewer facing a plaque that faces +z looks south, and their right is -x
        float X(float u) => cx - facing * u;
        float Zf(float d) => wallZ + facing * d;
        float half = side / 2f, step = side * 0.27f, radius = side * 0.11f;
        Block(mb, X(-half), X(half), cy - half, cy + half, Zf(0f), Zf(0.02f), IsleKit.Board);
        var pips = new List<Vector2>();
        if (place % 2 == 1) pips.Add(Vector2.zero);                                                        // 1, 3, 5: the middle
        if (place >= 2) { pips.Add(new Vector2(-step, step)); pips.Add(new Vector2(step, -step)); }        // 2 and up: one diagonal
        if (place >= 4) { pips.Add(new Vector2(-step, -step)); pips.Add(new Vector2(step, step)); }        // 4, 5: the other
        foreach (Vector2 pip in pips)
            Kit.AddBall(mb, new Vector3(X(pip.x), cy + pip.y, Zf(0.02f + radius * 0.3f)), radius, Pip);
    }

    // The marks of all five places in a row on a wall: place 1 centred on x = firstX, each next one 'pitch' further to
    // the reader's right
    static void AddPlaceMarks(MeshBuilder mb, float firstX, float pitch, float cy, float wallZ, float facing, float side)
    {
        for (int k = 1; k <= Places; k++)
            AddPlaceMark(mb, firstX - facing * pitch * (k - 1), cy, wallZ, facing, k, side);
    }

    // A wrapped present standing at (x, z) on a top at height y: a box 'w' wide, 'h' high and 0.22 deep, with a ribbon
    // round it both ways
    static void Present(MeshBuilder mb, float x, float y, float z, float w, float h, Color paper, Color ribbon)
    {
        const float d = 0.22f;
        Block(mb, x - w / 2f, x + w / 2f, y, y + h, z - d / 2f, z + d / 2f, paper);
        Block(mb, x - 0.02f, x + 0.02f, y, y + h + 0.006f, z - d / 2f - 0.006f, z + d / 2f + 0.006f, ribbon);
        Block(mb, x - w / 2f - 0.006f, x + w / 2f + 0.006f, y, y + h + 0.01f, z - 0.02f, z + 0.02f, ribbon);
    }

    // ---------------------------------------------------------------- the puzzle

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -6f, Floor + 1.5f);
        b.ClearGround(20f, 44f);
        Sdk.Set(b.Puzzle, "gourdColor", DarkRed);      // the red of Santa's coat
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        // No jetpacks over the site: nobody flies onto the roofs
        IsleKit.CalmAir(b, "Santas List", 0.5f, 3f, new Vector3(36f, 120f, 60f), 0f, -10f, false);

        // ---- the pad: one level slab under the yard, the Post Office, the hall and the hut, sunk so no gap ever shows ----
        var pad = new MeshBuilder();
        Block(pad, -9.7f, 9.7f, -1.75f, 0f, -16.2f, 21.9f, IsleKit.Stone);
        OnPad("Pad", pad, snowy);

        BuildHall(snowy);
        BuildHut(snowy);
        BuildPostOffice(snowy);
        BuildYard(snowy);

        // ---- List A: on the hall's south end wall, facing north up the hall; its places' marks under it; a lamp
        //      that is lit while the puzzle runs, and a room light so the wall is never dark ----
        P("Display", ListAX, HallEndZ + DisplayBack, ListAH, 0f).Named("List A")
            .Set("PuzzleDisplay", "size", 3).Set("PuzzleDisplay", "randomOnStart", true);      // Five; blank until Differ fills it, blanked by each stop
        var marksA = new MeshBuilder();
        AddPlaceMarks(marksA, ListAX, FlapPitch, ListAH - 0.39f, HallEndZ, 1f, 0.28f);
        OnPad("List A Marks", marksA, bare, false);
        Piece hallLamp = P("Light 2", 6.5f, -12.9f, 2.6f).Named("Hall Lamp");
        E("Room Light 2 FFE3B0 4.0 0.9", 6.5f, -12.3f, 2.5f);      // (RoomLights reads only numbers with a '.')

        // ---- the hut: the keyhole's glass and the telescope that looks through it down the hall ----
        P("Glass", 6.5f, 16.85f, 1.5f, 180f).Named("Keyhole Glass").Pane(0.45f, 0.4f)
            .Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0).Set("PuzzleGlass", "wire", false);
        // (the placeholder's foot is 1.1 m under its pivot; the eye should line up with the keyhole)
        P("Telescope", 6.5f, 18.1f, 1.1f, 180f).Named("Keyhole Telescope").Set("PuzzleTelescope", "invisibleInkView", false);
        E("Room Light 3 FFE3B0 5.0 1.0", 6.5f, 19.0f, 2.3f);

        // ---- List B: in the Post Office, on the north wall over the hatch, facing the doorway; its marks under it ----
        P("Display", ListBX, PostWallZ - DisplayBack, ListBH, 180f).Named("List B")
            .Set("PuzzleDisplay", "size", 3).Set("PuzzleDisplay", "randomOnStart", true);
        var marksB = new MeshBuilder();
        AddPlaceMarks(marksB, ListBX, FlapPitch, ListBH - 0.36f, PostWallZ, -1f, 0.22f);
        OnPad("List B Marks", marksB, bare, false);
        E("Room Light 1 FFE3B0 4.0 0.9", -1.5f, -12.2f, 2.6f);

        // ---- the Checking Desk: per place a button, its plaque and its lamp; the check screen with its own button;
        //      a red "not equal" sign over the lamps (mark the places where the lists differ) ----
        var deskMarks = new MeshBuilder();
        AddPlaceMarks(deskMarks, MarkX, MarkPitch, PlaqueH, DeskZ, 1f, 0.2f);
        const float signX = MarkX - MarkPitch * 2f, signY = 2.02f;
        Block(deskMarks, signX - 0.12f, signX + 0.12f, signY + 0.03f, signY + 0.07f, DeskZ, DeskZ + 0.01f, IsleKit.Red);
        Block(deskMarks, signX - 0.12f, signX + 0.12f, signY - 0.07f, signY - 0.03f, DeskZ, DeskZ + 0.01f, IsleKit.Red);
        Extrude(deskMarks, new[]
        {
            new Vector3(signX + 0.07f, signY - 0.13f, DeskZ), new Vector3(signX + 0.03f, signY - 0.13f, DeskZ),
            new Vector3(signX - 0.07f, signY + 0.13f, DeskZ), new Vector3(signX - 0.03f, signY + 0.13f, DeskZ),
        }, new Vector3(0f, 0f, 0.016f), IsleKit.Red);
        OnPad("Desk Marks", deskMarks, bare, false);

        var markButtons = new Piece[Places];
        for (int k = 1; k <= Places; k++)
        {
            float x = MarkX - MarkPitch * (k - 1);
            markButtons[k - 1] = P("Button Poke", x, DeskZ, ButtonH).Named("Mark Button " + k).Set("PuzzlePokeButton", "autoEnableSeconds", 0.4f);
            // (a Light 8's back is 0.1 m behind its pivot)
            P("Light 8", x, DeskZ + 0.1f, LampH).Named("Mark Lamp " + k).Set("PuzzleLight", "color", Warm);
        }
        // The Validator prefab carries its own poke button (a child 0.444 m under the screen, already its pokeButton):
        // one press checks. A right marking wins by itself; onIncorrect is not wired, so a wrong one only shows the cross.
        Piece check = P("Validator", CheckX, DeskZ, CheckH).Named("List Validator");
        Component checkC = check.Component("PuzzleValidator"), checkButton = check.Component("PuzzlePokeButton");
        checkButton.gameObject.name = "List Check Button";
        Sdk.Set(checkC, "pokeButton", checkButton);

        // ---- the walkie-talkies, and the corner: switch, live lamp, vice, pillow, confetti ----
        P("Vanilla Item", 2.4f, 4.2f, 0.9f + 0.15f).Named("Walkie 1")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        P("Vanilla Item", 2.8f, 4.2f, 0.9f + 0.15f).Named("Walkie 2")
            .Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        IsleKit.StartCorner(b, 2.6f, 1.0f, -90f, true, Floor, false);

        // ---- the rule (Differ.cs): each start deals two lists that differ in two places; a mark button flips its
        //      place's mark; the check screen is right exactly when the marks are the two places ----
        Component differ = b.AddCustomScript(E("Checking Twice", -2.0f, -1.5f, 0.5f), "Differ");
        Sdk.Set(differ, "listA", "List A");
        Sdk.Set(differ, "listB", "List B");
        Sdk.Set(differ, "places", Places);
        Sdk.Set(differ, "differ", 2);
        Sdk.Set(differ, "imageSet", 0);
        Sdk.Set(differ, "images", 6);
        Sdk.Set(differ, "markPrefix", "Mark Lamp ");
        Sdk.Set(differ, "validator", "List Validator");
        for (int k = 1; k <= Places; k++)
            markButtons[k - 1].On("PuzzlePokeButton", "onPressed", differ, "Press", k);

        Component hallLampC = hallLamp.Component("PuzzleLight");
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStart", differ, "Begin");
        root.On("PuzzleSDK", "onPuzzleStart", hallLampC, "TurnOn");
        root.On("PuzzleSDK", "onPuzzleStop", differ, "End");
        root.On("PuzzleSDK", "onPuzzleStop", hallLampC, "TurnOff");
    }

    // ---------------------------------------------------------------- structures

    // Santa's Long Hall: outer x 5.0..8.0, z -14.0..17.0, walls 0.3 sunk 0.5, a flat ceiling slab at 2.8 over all four
    // walls, solid gables, a sealed pitched roof overhanging 0.2; the only opening is the keyhole in the north end wall
    void BuildHall(Material snowy)
    {
        Color log = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.5f, top = 2.8f;
        var mb = new MeshBuilder();
        Block(mb, 5.0f, 5.3f, y0, top, -14.0f, 17.0f, log);                 // west
        Block(mb, 7.7f, 8.0f, y0, top, -14.0f, 17.0f, log);                 // east
        Block(mb, 5.3f, 7.7f, y0, top, -14.0f, HallEndZ, log);              // south end: List A's wall
        // north end, with the keyhole x 6.275..6.725, 1.3..1.7 high, through its whole thickness
        Block(mb, 5.3f, 6.275f, y0, top, 16.7f, 17.0f, log);
        Block(mb, 6.725f, 7.7f, y0, top, 16.7f, 17.0f, log);
        Block(mb, 6.275f, 6.725f, y0, 1.3f, 16.7f, 17.0f, log);
        Block(mb, 6.275f, 6.725f, 1.7f, top, 16.7f, 17.0f, log);
        // the ceiling slab: rests on all four walls to their outer faces, so the inside ends at 2.8 m whatever the roof does
        Block(mb, 5.0f, 8.0f, top, 3.0f, -14.0f, 17.0f, roof);
        // solid gables over both end walls, 3 m wide at 2.8, 3.6 at the ridge
        foreach (float gz in new[] { -14.0f, 16.7f })
            Extrude(mb, new[] { new Vector3(5.0f, top, gz), new Vector3(8.0f, top, gz), new Vector3(6.5f, 3.6f, gz) }, new Vector3(0f, 0f, 0.3f), log);
        // the roof: two slabs on the gables' slopes (extended 0.2 past the eaves), 0.15 thick, and a ridge cap
        Vector3 along = new Vector3(0f, 0f, 31.4f);
        Extrude(mb, new[] { new Vector3(4.8f, 2.693f, -14.2f), new Vector3(6.5f, 3.6f, -14.2f), new Vector3(6.5f, 3.75f, -14.2f), new Vector3(4.8f, 2.843f, -14.2f) }, along, roof);
        Extrude(mb, new[] { new Vector3(8.2f, 2.693f, -14.2f), new Vector3(6.5f, 3.6f, -14.2f), new Vector3(6.5f, 3.75f, -14.2f), new Vector3(8.2f, 2.843f, -14.2f) }, along, roof);
        Block(mb, 6.38f, 6.62f, 3.66f, 3.82f, -14.25f, 17.25f, roof);
        // a red sill band outside, for the look
        Block(mb, 4.96f, 5.0f, 0.0f, 0.35f, -14.0f, 17.0f, IsleKit.Red);
        Block(mb, 8.0f, 8.04f, 0.0f, 0.35f, -14.0f, 17.0f, IsleKit.Red);
        OnPad("Long Hall", mb, snowy);

        // fairy lights along both eaves
        var west = new List<Vector3>();
        var east = new List<Vector3>();
        for (int i = 0; i <= 6; i++)
        {
            float z = -14.2f + 31.4f * i / 6f;
            west.Add(new Vector3(4.75f, Floor + 2.6f, z));
            east.Add(new Vector3(8.25f, Floor + 2.6f, z));
        }
        Kit.LightString(b, "Hall Lights West", west, 0.1f, 0.5f, false, false, 81);
        Kit.LightString(b, "Hall Lights East", east, 0.1f, 0.5f, false, false, 82);
    }

    // The Lookout's hut: x 4.7..8.3, z 17.0..20.6 (its south wall is the hall's end wall), walls 0.2, 2.6 high, a doorway
    // 1.2 m wide in the east wall (z 18.2..19.4), a pitched roof with its ridge east-west at 3.4
    void BuildHut(Material snowy)
    {
        Color wall = IsleKit.Wood, roof = IsleKit.DarkWood;
        const float y0 = -0.1f, top = 2.6f;
        var mb = new MeshBuilder();
        Block(mb, 4.7f, 4.9f, y0, top, 16.7f, 20.6f, wall);                 // west
        Block(mb, 4.9f, 8.1f, y0, top, 20.4f, 20.6f, wall);                 // north
        Block(mb, 8.1f, 8.3f, y0, top, 16.7f, 18.2f, wall);                 // east, either side of the doorway
        Block(mb, 8.1f, 8.3f, y0, top, 19.4f, 20.6f, wall);
        Block(mb, 8.1f, 8.3f, 2.05f, top, 18.2f, 19.4f, wall);              // lintel
        Block(mb, 4.9f, 5.0f, y0, top, 16.7f, 17.0f, wall);                 // the corners against the hall
        Block(mb, 8.0f, 8.1f, y0, top, 16.7f, 17.0f, wall);
        // gables over the west and east walls, then the two roof slabs
        foreach (float gx in new[] { 4.7f, 8.1f })
            Extrude(mb, new[] { new Vector3(gx, top, 16.7f), new Vector3(gx, top, 20.6f), new Vector3(gx, 3.4f, 18.8f) }, new Vector3(0.2f, 0f, 0f), wall);
        Vector3 along = new Vector3(4.0f, 0f, 0f);
        Extrude(mb, new[] { new Vector3(4.5f, 2.511f, 16.8f), new Vector3(4.5f, 3.4f, 18.8f), new Vector3(4.5f, 3.55f, 18.8f), new Vector3(4.5f, 2.661f, 16.8f) }, along, roof);
        Extrude(mb, new[] { new Vector3(4.5f, 2.511f, 20.8f), new Vector3(4.5f, 3.4f, 18.8f), new Vector3(4.5f, 3.55f, 18.8f), new Vector3(4.5f, 2.661f, 20.8f) }, along, roof);
        Block(mb, 4.45f, 8.55f, 3.47f, 3.62f, 18.7f, 18.9f, roof);
        // a step and a red door frame at the doorway
        Block(mb, 8.3f, 8.9f, 0f, 0.08f, 18.0f, 19.6f, IsleKit.Stone);
        Block(mb, 8.3f, 8.36f, y0, 2.1f, 18.1f, 18.2f, IsleKit.Red);
        Block(mb, 8.3f, 8.36f, y0, 2.1f, 19.4f, 19.5f, IsleKit.Red);
        OnPad("Lookout Hut", mb, snowy);
    }

    // The Post Office: inside x -3.0..0.0, z -13.5..-10.5, walls 0.2 and 2.9 high, roof 0.2. An open doorway 1.24 m
    // wide in the south wall (lintel from 2.04) and an open counter hatch 2.2 m wide in the north wall, looking out on
    // the yard. List B hangs inside over the hatch, so it cannot be read from the yard: walk round and in.
    void BuildPostOffice(Material snowy)
    {
        Color wall = IsleKit.Red, roof = IsleKit.DarkWood, trim = IsleKit.Cream;
        const float y0 = -0.1f, top = 2.9f;
        var mb = new MeshBuilder();
        Block(mb, -3.0f, 0.0f, -0.13f, 0.02f, -13.5f, PostWallZ, IsleKit.Wood);     // floor
        Block(mb, -3.2f, -3.0f, y0, top, -13.7f, -10.3f, wall);                     // west
        Block(mb, 0.0f, 0.2f, y0, top, -13.7f, -10.3f, wall);                       // east
        // north wall with the hatch x -2.6..-0.4
        Block(mb, -3.0f, -2.6f, y0, top, PostWallZ, -10.3f, wall);
        Block(mb, -0.4f, 0.0f, y0, top, PostWallZ, -10.3f, wall);
        Block(mb, -2.6f, -0.4f, y0, HatchSill, PostWallZ, -10.3f, wall);
        Block(mb, -2.6f, -0.4f, HatchTop, top, PostWallZ, -10.3f, wall);
        // south wall with the doorway x -2.37..-1.13
        Block(mb, -3.0f, -2.37f, y0, top, -13.7f, -13.5f, wall);
        Block(mb, -1.13f, 0.0f, y0, top, -13.7f, -13.5f, wall);
        Block(mb, -2.37f, -1.13f, 2.04f, top, -13.7f, -13.5f, wall);
        Block(mb, -3.35f, 0.35f, top, top + 0.2f, -13.85f, -10.15f, roof);          // roof
        OnPad("Post Office", mb, snowy);

        // outside trim: the counter's ledge under the hatch, a sign board over it, corner posts, a board over the door
        var dress = new MeshBuilder();
        Block(dress, -2.65f, -0.35f, HatchSill - 0.08f, HatchSill, -10.3f, -10.12f, trim);
        Block(dress, -2.3f, -0.7f, 2.2f, 2.7f, -10.3f, -10.26f, trim);
        Block(dress, -2.2f, -0.8f, 2.4f, 2.5f, -10.26f, -10.24f, IsleKit.Red);
        foreach (float px in new[] { -3.25f, 0.25f })
            foreach (float pz in new[] { -13.75f, -10.25f })
                Block(dress, px - 0.06f, px + 0.06f, y0, top, pz - 0.06f, pz + 0.06f, trim);
        Block(dress, -2.47f, -1.03f, 2.04f, 2.16f, -13.76f, -13.7f, trim);
        OnPad("Post Office Trim", dress, snowy, false);

        // lights along the front eave
        Kit.LightString(b, "Post Office Lights",
            new List<Vector3> { new Vector3(-3.3f, Floor + 2.95f, -10.1f), new Vector3(0.3f, Floor + 2.95f, -10.1f) }, 0.15f, 0.45f, false, false, 83);
    }

    // The yard: the Toy Shelf with its presents, the Sleigh with its sack panel and the Checking Desk's board on it,
    // the walkie table, the lights round it all
    void BuildYard(Material snowy)
    {
        var mb = new MeshBuilder();
        // Toy Shelf, x -4.6..-1.2: a stepped block, lower tier top 0.55 (z -8.95..-8.65), upper top 0.95 (z -8.55..-8.25),
        // with a lip on the lower tier's front edge, and six presents on each tier waiting for the sleigh
        Block(mb, -4.6f, -1.2f, -0.05f, 0.55f, -8.95f, -8.55f, IsleKit.Wood);
        Block(mb, -4.6f, -1.2f, -0.05f, 0.95f, -8.55f, -8.25f, IsleKit.Wood);
        Block(mb, -4.6f, -1.2f, 0.55f, 0.61f, -8.97f, -8.93f, IsleKit.DarkWood);
        Block(mb, -4.7f, -4.6f, -0.05f, 1.1f, -8.97f, -8.25f, IsleKit.Red);           // end boards
        Block(mb, -1.2f, -1.1f, -0.05f, 1.1f, -8.97f, -8.25f, IsleKit.Red);
        Color[] paper = { IsleKit.Red, IsleKit.Green, IsleKit.Ice, IsleKit.Cream };
        for (int k = 0; k < 6; k++)
        {
            float px = -4.275f + 0.55f * k;
            Present(mb, px, 0.55f, -8.76f, 0.26f + 0.05f * (k % 3), 0.18f + 0.06f * ((k + 1) % 3), paper[k % 4], IsleKit.Gold);
            Present(mb, px, 0.95f, -8.4f, 0.24f + 0.05f * ((k + 2) % 3), 0.16f + 0.06f * (k % 3), paper[(k + 2) % 4], k % 2 == 0 ? IsleKit.Gold : IsleKit.Red);
        }

        // the Sleigh: a red tub on runners x -4.7..-0.1, z -3.8..-2.5, 1.2 high, gold rims, and the sack panel behind it
        // (z -2.46..-2.34, 2.8 high)
        foreach (float rz in new[] { -3.7f, -2.6f })
        {
            Block(mb, -5.0f, 0.1f, 0f, 0.1f, rz - 0.05f, rz + 0.05f, IsleKit.Gold);    // runner
            Block(mb, -5.1f, -5.0f, 0f, 0.45f, rz - 0.05f, rz + 0.05f, IsleKit.Gold);  // its curl
            foreach (float sx in new[] { -4.2f, -2.4f, -0.6f })
                Block(mb, sx - 0.04f, sx + 0.04f, 0.1f, 0.25f, rz - 0.04f, rz + 0.04f, IsleKit.DarkWood);
        }
        Block(mb, -4.7f, -0.1f, 0.25f, 1.2f, -3.8f, -2.5f, IsleKit.Red);
        Block(mb, -4.75f, -0.05f, 1.2f, 1.28f, -3.85f, -2.45f, IsleKit.Gold);
        Block(mb, -4.75f, -0.05f, 0.25f, 0.33f, -3.85f, -2.45f, IsleKit.Gold);
        Block(mb, -4.7f, -0.1f, 0f, 2.8f, -2.46f, -2.34f, IsleKit.Red);              // the sack panel
        Block(mb, -4.8f, 0.0f, 2.8f, 2.92f, -2.52f, -2.28f, IsleKit.Gold);
        // the Checking Desk's board on the panel's north face: cream, x -4.2..-0.55, 0.9..2.25 high, gold rails over
        // and under it; the buttons, plaques, lamps and the check screen sit on its face (DeskZ)
        Block(mb, -4.2f, -0.55f, 0.9f, 2.25f, -2.34f, DeskZ, IsleKit.Cream);
        Block(mb, -4.26f, -0.49f, 2.25f, 2.31f, -2.34f, -2.29f, IsleKit.Gold);
        Block(mb, -4.26f, -0.49f, 0.84f, 0.9f, -2.34f, -2.29f, IsleKit.Gold);
        // the sack itself, heaped in the tub
        Kit.AddBall(mb, new Vector3(-3.4f, 1.45f, -3.15f), 0.55f, new Color(0.62f, 0.47f, 0.3f));
        Kit.AddBall(mb, new Vector3(-1.6f, 1.35f, -3.15f), 0.4f, IsleKit.Green);

        // the walkie table at (2.6, 4.2), 1.0 x 0.5, top 0.9
        Block(mb, 2.1f, 3.1f, 0.84f, 0.9f, 3.95f, 4.45f, IsleKit.Wood);
        foreach (float lx in new[] { 2.17f, 3.03f })
            foreach (float lz in new[] { 4.02f, 4.38f })
                Block(mb, lx - 0.03f, lx + 0.03f, -0.05f, 0.84f, lz - 0.03f, lz + 0.03f, IsleKit.DarkWood);
        OnPad("Yard", mb, snowy);

        // the lights round the yard and the Post Office (west of the hall, which has its own on its eaves)
        Kit.LightRing(b, "Site Lights", -2.4f, -4.25f, 13.6f, 22.5f, Floor + 2.6f, false, 88);
    }
}
