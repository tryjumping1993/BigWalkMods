// Snow Globe: a one-way telephone game (spec: puzzle-specs/SnowGlobe.md, 2026-11-22).
//
// While the puzzle runs, one player, the Keeper, is sealed inside a giant soundproof glass globe with a tiny toy
// village, eight hand-drawn "winter scribble" tiles (this puzzle's own Custom sheet, drawn below in C#) and a peg
// board. The pictures to match are far off, on the back wall of a narrow "toy shop" shed at the end of a 12 m tunnel,
// readable only from a narrow strip in front of it, best through its telescope 47 m away. The Reader at the telescope
// calls them across 28 m of snow to the Relay at a lectern by the globe; only the Relay's voice gets into the globe,
// through one intercom; the Keeper cannot talk out at all and answers with gestures through the clear glass. One
// Radio Dead Zone covers the whole site, so no walkie-talkie or megaphone works anywhere on it. A seven-minute clock
// stops the run (the door opens and the pictures blank; the next start re-deals). In a four-player lobby a second
// shed (one flap) and telescope appear to the east, and the Keeper's board grows a sixth slot for it.
//
// The door stands in the front wall of a closed soundproof wooden porch that reaches through the gap cut in the
// glass; a two-way No Items turnstile fills the porch's back opening, so nothing is carried in or out by hand. Two
// Start Switches (the start corner's outside and one on the Keeper's shelf wall) move together, so the Keeper can
// always quit; the clock also stops a forgotten run.
//
// Coordinates: local metres from the globe's centre, x east, z north (+z forward), the door to the south. At the globe,
// heights are above Floor (the top of the pad) and pieces are placed onGround: false; the sheds, telescopes and lectern
// stand on their own ground (measured once at their centre). Yaw: 0 faces +z, 180 -z, 90 +x, -90 -x.
using System;
using System.Collections.Generic;
using Bwm;
using UnityEditor;
using UnityEngine;

public sealed class SnowGlobePuzzle : PuzzleDef
{
    public override string Name => "SnowGlobe";
    public override Vector2 Footprint => new Vector2(60f, 72f);                 // layout-final.json: 60 x 72, centred on the globe
    public override Vector2? FixedSite => new Vector2(-1464f, -693f);         // layout-final: the north shore's clean flat, open to the north
    public override float Yaw => 0f;

    // ---- the globe ----
    const float Floor = 0.3f;           // top of the pad (and the sphere's centre), above the root's level
    const float Radius = 5.5f;          // the glass sphere
    const int Rings = 12, Segments = 40;
    const int DoorRings = 3;            // the door gap: rings 0-2 of segments 19 and 20 (up to h 2.105)
    const float RoundSeconds = 420f;    // seven minutes
    const float WallZ = 1.9f;           // the shelf wall's south face (wall z 1.9..2.1)
    // The door stop goes on the face of the door the leaves do not swing towards (risk 4: watch the door open). True:
    // the outside face (z -5.91..-5.87); false: the porch's face (z -5.63..-5.59).
    const bool StopOutside = true;

    // ---- the toy shops (sight hoods) and telescopes ----
    const float ShopX = 24f;            // the West shop at -24, the East (4p) at +24
    const float ShopCentreZ = 27.1f;
    const float FrontZ = 20.8f, MouthZ = 21.0f, BackZ = 33.0f;     // trim, tunnel mouth, the back wall's inner face
    const float MouthLow = 0.8f, MouthHigh = 2.0f, ShopTop = 3.0f;
    const float DisplayH = 1.4f;        // the Display's centre above the shop's ground
    const float DrumZ = -14.6f, ScopeZ = -14.0f;

    // ---- the lectern and the start corner ----
    const float LecternX = 3.5f, LecternZ = -9.0f;

    // ---- the picture sheet ----
    const string SheetPath = "Assets/Textures/SnowGlobe/SnowGlobeSheet.png";
    const string SheetVersion = "snowglobe-sheet-1";

    static readonly Color Ink = new Color(0.10f, 0.09f, 0.14f);
    static readonly Color Paper = new Color(0.95f, 0.92f, 0.84f);
    static readonly Color Outline = new Color(0.13f, 0.09f, 0.07f);

    PuzzleBuilder b;

    // ================================================================ helpers

    // A block between two corners (local x, y above the mesh's base, z) added to a mesh being built
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A ridge roof over x0..x1 (ridge along z at the middle), from y0 at the eaves to y1 at the ridge, z0..z1
    static void Gable(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
    {
        float xm = (x0 + x1) / 2f;
        // Quad takes its corners counter-clockwise as seen from outside (as Kit.AddBox's sides do)
        mb.Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(xm, y1, z0), new Vector3(xm, y1, z1), color);     // west slope
        mb.Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(xm, y1, z1), new Vector3(xm, y1, z0), color);     // east slope
        // Tri takes them clockwise as seen from outside
        mb.Tri(new Vector3(x0, y0, z0), new Vector3(xm, y1, z0), new Vector3(x1, y0, z0), color);      // south gable
        mb.Tri(new Vector3(x1, y0, z1), new Vector3(xm, y1, z1), new Vector3(x0, y0, z1), color);      // north gable
    }

    // A mesh built in local coordinates, placed 'baseHeight' above the root's level
    Piece Place(string name, MeshBuilder mb, Material material, float baseHeight, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, baseHeight, false);
        return piece;
    }

    // An SDK part at the globe, 'height' above the pad top
    Piece P(string prefab, float x, float z, float height, float yaw = 0f) => b.Part(prefab, x, z, Floor + height, yaw, false);

    // The ground's height at a local point, above the root's level
    float GroundAt(float x, float z) => b.WorldPoint(x, z).y - b.Root.transform.position.y;

    // Sets an enum field by its name (Sdk.Set takes the index; PuzzlePegSet's values have gaps)
    static void SetEnumByName(Component c, string field, string value)
    {
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.propertyType != SerializedPropertyType.Enum) throw new Exception(c.GetType().Name + " has no enum '" + field + "'");
        int index = Array.IndexOf(p.enumNames, value);
        if (index < 0) throw new Exception(c.GetType().Name + "." + field + " has no value '" + value + "'");
        p.enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================ build

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(60f, 54f, 0f, 9f);              // z -18..36: the globe, both shops, both drums, the lectern, the corner
        b.DebugTeleport(6f, -11f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Component sdk = b.Puzzle;
        Piece root = b.RootPiece;

        // No jetpacks anywhere on the site: nobody ferries between the sight strips and the lectern by air, or onto
        // the shops' roofs. Wider than the dead zone and the clearing, from 10 m under the ground to 130 m up.
        IsleKit.CalmAir(b, "Snow Globe", 0f, 8.5f, new Vector3(72f, 140f, 66f), 0f, -10f);

        // One Radio Dead Zone over the whole site, inside the globe and out (one only: two overlapping zones switch a
        // radio live again as it leaves the inner one). x -30..30, z -22..36, -2..14 m above the ground at (0, 7).
        Piece dead = b.Part("Radio Dead Zone", 0f, 7f, 6f).Named("Site Dead Zone");
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) zone = dead.Go.AddComponent<BoxCollider>();
        zone.size = new Vector3(60f, 16f, 58f);
        zone.center = Vector3.zero;

        Texture2D sheet = ScribbleSheet();

        BuildPad(snowy);
        BuildDome(bare);
        BuildPorch(snowy);
        BuildInterior(snowy, bare);

        // ---- the door, the turnstile, the Keeper's shelf wall ----
        Piece door = P("Door", 0f, -5.75f, 0f, 0f).Named("Globe Door").Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        P("Gate No items", 0.05f, -3.795f, 0f, 0f).Named("Globe Turnstile")
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);

        P("Switch Start", -1.85f, WallZ - 0.073f, 1.25f, 180f).Named("Inside Switch");
        P("Live Light", -1.85f, WallZ, 1.42f, 180f).Named("Inside Live");
        Piece speaker = P("Intercom Speaker", 0f, WallZ - 0.1f, 1.85f, 180f).Named("Globe Speaker");
        Piece check = P("Validator", 1.9f, WallZ, 1.75f, 180f).Named("Globe Validator");
        Component checkC = check.Component("PuzzleValidator");
        // The Validator prefab carries its own poke button (a child, already its pokeButton): that is the Keeper's Check
        Component pokeC = check.Component("PuzzlePokeButton");
        pokeC.gameObject.name = "Globe Check";
        Sdk.Set(checkC, "pokeButton", pokeC);
        Piece clock = P("Timer", 0f, WallZ - 0.01f, 2.45f, 180f).Named("Globe Clock").Set("PuzzleTimer", "duration", RoundSeconds);

        // ---- the eight tiles, one of each picture, on the two benches ----
        float[] tileZ = { -1.95f, -1.2f, -0.45f, 0.3f };
        for (int i = 0; i < 8; i++)
        {
            bool west = i < 4;
            Piece tile = P("Peg Tile", west ? -3.3f : 3.3f, tileZ[i % 4], 0.85f + 0.24f, west ? 90f : -90f).Named("Tile " + (i + 1));
            Component tileC = tile.Component("PuzzlePegTile");
            SetEnumByName(tileC, "imageSet", "Custom");
            Sdk.Set(tileC, "image", i + 1);
            Sdk.Set(tileC, "customSheet", sheet);
        }

        // ---- the West shop (every lobby) and the East shop (four players only) ----
        Piece westDisplay = BuildShop("West", -ShopX, 3, null, snowy, 401);      // five flaps
        Piece fourth = IsleKit.Lobby(b, "Fourth Watch", false, false, true);
        Piece eastDisplay = BuildShop("East", ShopX, 0, fourth, snowy, 402);     // one flap
        Component wd = westDisplay.Component("PuzzleDisplay"), ed = eastDisplay.Component("PuzzleDisplay");

        // ---- the boards: five slots for two or three players, six for four (slot 6 is the East flap) ----
        Piece threeKeeper = IsleKit.Lobby(b, "Three Keeper", true, true, false);
        Piece board5 = P("Peg Board", -1.3f, WallZ, 1.3f, 180f).Named("Globe Board 5").Set("PuzzlePegBoard", "size", 4).Under(threeKeeper);
        Component board5C = board5.Component("PuzzlePegBoard");
        Sdk.Add(board5C, "answerFrom", wd, wd, wd, wd, wd);
        Sdk.AddInts(board5C, "answerFromFlap", 1, 2, 3, 4, 5);
        Sdk.Set(board5C, "validator", checkC);

        Piece fourKeeper = IsleKit.Lobby(b, "Four Keeper", false, false, true);
        Piece board6 = P("Peg Board", -1.3f, WallZ, 1.3f, 180f).Named("Globe Board 6").Set("PuzzlePegBoard", "size", 5).Under(fourKeeper);
        Component board6C = board6.Component("PuzzlePegBoard");
        Sdk.Add(board6C, "answerFrom", wd, wd, wd, wd, wd, ed);
        Sdk.AddInts(board6C, "answerFromFlap", 1, 2, 3, 4, 5, 1);
        Sdk.Set(board6C, "validator", checkC);

        // ---- the lectern and its intercom: the Relay stands south of it, facing the globe ----
        Piece mic = BuildLectern(snowy);
        mic.Add("PuzzleIntercom", "speakers", speaker.Component("PuzzleIntercomSpeaker"));

        // ---- the corner (outside Start Switch, gourd vice, pillow, confetti), lights ----
        IsleKit.StartCorner(b, 8f, -13f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 9f, 60f, 54f, 2.6f, true, 191);
        // a string round the globe's base ring, open at the door
        var ring = new List<Vector3>();
        for (float a = 200f; a <= 520.01f; a += 10f)
            ring.Add(new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 6.3f, Floor + 0.1f, Mathf.Cos(a * Mathf.Deg2Rad) * 6.3f));
        Kit.LightString(b, "Globe Base Lights", ring, 0f, 0.4f, false, false, 192);
        // the globe's own lamp, so the Keeper and the board can be seen at night (Winter Walk's RoomLights)
        b.Empty("Room Light Globe FFEED8 8.0 1.6", 0f, 0.5f, Floor + 4.3f, false);

        // ---- wiring ----
        Component clockC = clock.Component("PuzzleTimer"), doorC = door.Component("PuzzleDoor");
        root.On("PuzzleSDK", "onPuzzleStart", clockC, "StartTimer");
        root.On("PuzzleSDK", "onPuzzleStop", clockC, "ResetTimer");
        clock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");       // time up: the switches turn back, the door opens
        root.On("PuzzleSDK", "onWin", doorC, "Open");                   // a first win lets the Keeper out
        root.On("PuzzleSDK", "onWin", clockC, "PauseTimer");            // and the clock no longer stops a won run
    }

    // ================================================================ the pad and the glass

    // One level pad: a stone disc sunk 1.5 m (r 6.6, top 6 cm under the floor) under a dark wood ring (r 6.0) whose top
    // is the floor, inside the globe and out to the porch's front
    void BuildPad(Material snowy)
    {
        var pad = new MeshBuilder();
        Kit.AddDisc(pad, new Vector3(0f, -Floor - 1.5f, 0f), 6.6f, 1.5f + Floor - 0.06f, IsleKit.Stone, 48);
        Kit.AddDisc(pad, new Vector3(0f, -Floor - 0.2f, 0f), 6.0f, Floor + 0.2f, IsleKit.DarkWood, 48);
        Place("Pad", pad, snowy, Floor);
    }

    // The globe: a half sphere of clear soundproof glass, centred at the floor (r 5.5), both sides, with the door gap
    // cut on its south side, and a vertical skirt that buries the rim in the pad
    void BuildDome(Material bare)
    {
        var dome = new MeshBuilder();
        Vector3 S(int ring, int seg)
        {
            float lat = Mathf.PI * 0.5f * ring / Rings, lon = 2f * Mathf.PI * seg / Segments;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon)) * Radius;
        }
        Color white = Color.white;
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                if (r < DoorRings && (s == Segments / 2 - 1 || s == Segments / 2)) continue;      // the gap facing south (-z)
                if (r == Rings - 1)
                {
                    Vector3 top = Vector3.up * Radius;
                    dome.Tri(S(r, s), S(r, s + 1), top, white);
                    dome.Tri(S(r, s), top, S(r, s + 1), white);
                    continue;
                }
                dome.Quad(S(r, s), S(r, s + 1), S(r + 1, s + 1), S(r + 1, s), white);
                dome.Quad(S(r, s), S(r + 1, s), S(r + 1, s + 1), S(r, s + 1), white);
            }
        // the skirt: radius 5.5 from 0.6 m under the floor up to it
        for (int s = 0; s < Segments; s++)
        {
            Vector3 a = S(0, s), c = S(0, s + 1), down = Vector3.down * 0.6f;
            dome.Quad(a + down, c + down, c, a, white);
            dome.Quad(a + down, a, c, c + down, white);
        }
        Piece glass = Place("Dome", dome, bare, Floor, true);
        Component glassC = b.AddScript(glass, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // Clear
        Sdk.Set(glassC, "sound", 2);      // Soundproof
        Sdk.Set(glassC, "wire", false);
    }

    // The porch: cheeks, roof, front wall (jambs and lintel), door stop and gold trim, all under the Door Frame's
    // PuzzleSoundProof (Wood), so every straight line out of the glass's door gap meets the door or soundproof wood.
    // The cheeks (|x| 0.85..1.15) run z -5.9..-4.5 and cut through the gap's edges (half-width 0.80-0.86) at every
    // height; the roof (2.3..2.65) crosses the glass above the gap's top (2.105); the back opening at z -4.5 lies inside
    // the glass and is filled by the turnstile.
    void BuildPorch(Material snowy)
    {
        var mb = new MeshBuilder();
        const float y0 = -0.05f;
        Color wood = IsleKit.Wood, gold = IsleKit.Gold;
        // cheeks
        Block(mb, -1.15f, -0.85f, y0, 2.6f, -5.9f, -4.5f, wood);
        Block(mb, 0.85f, 1.15f, y0, 2.6f, -5.9f, -4.5f, wood);
        // roof (overhanging the cheeks, 5 cm proud of their tops so no faces are shared)
        Block(mb, -1.2f, 1.2f, 2.3f, 2.65f, -5.95f, -4.45f, IsleKit.DarkWood);
        // front wall: jambs flush with the door's edges (x ±0.62), lintel over the door (2.04)
        Block(mb, -0.85f, -0.62f, y0, 2.3f, -5.9f, -5.6f, wood);
        Block(mb, 0.62f, 0.85f, y0, 2.3f, -5.9f, -5.6f, wood);
        Block(mb, -0.62f, 0.62f, 2.04f, 2.3f, -5.9f, -5.6f, wood);
        // door stop: 4 cm strips overlapping the leaves' edges by 6 cm, on one face (see StopOutside)
        float s0 = StopOutside ? -5.91f : -5.63f, s1 = StopOutside ? -5.87f : -5.59f;
        Block(mb, -0.62f, -0.56f, y0, 2.04f, s0, s1, IsleKit.Red);
        Block(mb, 0.56f, 0.62f, y0, 2.04f, s0, s1, IsleKit.Red);
        Block(mb, -0.56f, 0.56f, 1.96f, 2.04f, s0, s1, IsleKit.Red);
        // gold trim proud of the front face, round the doorway and up the cheeks' fronts
        Block(mb, -0.74f, -0.62f, y0, 2.16f, -5.94f, -5.91f, gold);
        Block(mb, 0.62f, 0.74f, y0, 2.16f, -5.94f, -5.91f, gold);
        Block(mb, -0.62f, 0.62f, 2.06f, 2.16f, -5.94f, -5.91f, gold);
        Block(mb, -1.15f, -1.03f, y0, 2.3f, -5.94f, -5.9f, gold);
        Block(mb, 1.03f, 1.15f, y0, 2.3f, -5.94f, -5.9f, gold);

        Piece frame = b.Empty("Door Frame", 0f, -5.75f, Floor, false);
        Component proof = b.AddScript(frame, "PuzzleSoundProof");
        Sdk.Set(proof, "material", 0);      // Wood
        Place("Porch", mb, snowy, Floor).Under(frame);
    }

    // ================================================================ inside the globe

    // The shelf wall (board, validator, speaker, inside switch, clock), the two tile benches with an outline under each
    // tile's place, and the toy village behind the shelf
    void BuildInterior(Material snowy, Material bare)
    {
        var mb = new MeshBuilder();
        const float y0 = -0.05f;
        // shelf wall, facing south, and the little pediment the clock hangs on
        Block(mb, -2.2f, 2.2f, y0, 2.2f, WallZ, WallZ + 0.2f, IsleKit.Wood);
        Block(mb, -0.45f, 0.45f, 2.2f, 2.75f, WallZ, WallZ + 0.2f, IsleKit.Red);
        Block(mb, -2.3f, 2.3f, 2.2f, 2.28f, WallZ - 0.05f, WallZ + 0.25f, IsleKit.Gold);
        // benches (x ±2.95..±3.65, z -2.5..1.3, 0.85 high) with a back lip the tiles lean on
        foreach (float sign in new[] { -1f, 1f })
        {
            float inner = 2.95f * sign, outer = 3.65f * sign;
            Block(mb, Mathf.Min(inner, outer), Mathf.Max(inner, outer), y0, 0.85f, -2.5f, 1.3f, IsleKit.Cream);
            float l0 = 3.58f * sign, l1 = 3.65f * sign;
            Block(mb, Mathf.Min(l0, l1), Mathf.Max(l0, l1), 0.85f, 1.2f, -2.5f, 1.3f, IsleKit.Red);
            // an outline 0.34 (x) by 0.5 (z) under each tile's place: four dark strips 6 mm proud of the bench top
            float cx = 3.385f * sign;
            foreach (float tz in new[] { -1.95f, -1.2f, -0.45f, 0.3f })
            {
                const float hx = 0.17f, hz = 0.25f, w = 0.03f, top = 0.856f;
                Block(mb, cx - hx, cx + hx, 0.85f, top, tz - hz, tz - hz + w, Outline);
                Block(mb, cx - hx, cx + hx, 0.85f, top, tz + hz - w, tz + hz, Outline);
                Block(mb, cx - hx, cx - hx + w, 0.85f, top, tz - hz + w, tz + hz - w, Outline);
                Block(mb, cx + hx - w, cx + hx, 0.85f, top, tz - hz + w, tz + hz - w, Outline);
            }
        }
        Place("Toy Shop Fittings", mb, bare, Floor);

        // the village behind the shelf, seen over it: a fir and two little houses, snowy
        var village = new MeshBuilder();
        Kit.AddPine(village, new Vector3(0f, 0f, 3.7f), 1.8f, 1901);
        Kit.AddBox(village, new Vector3(-2.9f, 0f, 3.2f), new Vector3(0.9f, 0.7f, 0.8f), IsleKit.Cream);
        Kit.AddBox(village, new Vector3(-2.9f, 0.7f, 3.2f), new Vector3(1.1f, 0.22f, 1.0f), IsleKit.Red);
        Kit.AddBox(village, new Vector3(2.9f, 0f, 3.2f), new Vector3(0.8f, 0.65f, 0.75f), IsleKit.Cream);
        Kit.AddBox(village, new Vector3(2.9f, 0.65f, 3.2f), new Vector3(1.0f, 0.2f, 0.95f), IsleKit.Green);
        Kit.AddStar(village, new Vector3(0f, 1.95f, 3.7f), 0.14f, 0.05f, IsleKit.Gold);
        Place("Village", village, snowy, Floor);
    }

    // ================================================================ the shops and the telescopes

    // A toy shop: a closed shed 3.2 x 12.2 m (z 20.8..33.4) with a 2.4 x 1.2 m tunnel (0.8..2.0 high) bored 12 m to
    // the Display on its back wall, which can only be seen from a narrow strip in front of the mouth (within 10.6
    // degrees of the shed's axis); a Room Light by the Display; the telescope on its drum at the strip's far end, due
    // south on the shed's axis. 'sizeIndex' is the Display's size (0 One, 3 Five). Returns the Display.
    Piece BuildShop(string tag, float cx, int sizeIndex, Piece group, Material snowy, int seed)
    {
        float sb = GroundAt(cx, ShopCentreZ);
        float x0 = cx - 1.6f, x1 = cx + 1.6f, t0 = cx - 1.2f, t1 = cx + 1.2f;
        var mb = new MeshBuilder();
        Color wood = IsleKit.Wood;
        Block(mb, x0, x1, -1.2f, MouthLow, MouthZ, BackZ, IsleKit.Stone);            // the floor under the tunnel
        Block(mb, x0, t0, MouthLow, ShopTop, MouthZ, BackZ, wood);                    // side walls
        Block(mb, t1, x1, MouthLow, ShopTop, MouthZ, BackZ, wood);
        Block(mb, t0, t1, MouthHigh, ShopTop, MouthZ, BackZ, wood);                   // the tunnel's ceiling
        Block(mb, x0, x1, -1.2f, ShopTop, BackZ, BackZ + 0.4f, wood);                 // back wall
        // red and green trim round the mouth
        Block(mb, x0, t0, -0.2f, ShopTop, FrontZ, MouthZ, IsleKit.Red);
        Block(mb, t1, x1, -0.2f, ShopTop, FrontZ, MouthZ, IsleKit.Green);
        Block(mb, t0, t1, MouthHigh, ShopTop, FrontZ, MouthZ, IsleKit.Green);
        Block(mb, t0, t1, -0.2f, MouthLow, FrontZ, MouthZ, IsleKit.Red);
        // eaves and a ridge roof for the snow
        Block(mb, x0 - 0.3f, x1 + 0.3f, ShopTop - 0.02f, ShopTop + 0.12f, FrontZ - 0.2f, BackZ + 0.6f, IsleKit.DarkWood);
        Gable(mb, x0 - 0.3f, x1 + 0.3f, ShopTop + 0.12f, ShopTop + 1.1f, FrontZ - 0.2f, BackZ + 0.6f, IsleKit.Red);
        Piece shop = Place(tag + " Toy Shop", mb, snowy, sb);

        // the Display: its face is its pivot, its body 0.168 m behind it, so its back meets the back wall
        Piece display = b.Part("Display", cx, BackZ - 0.168f, sb + DisplayH, 180f, false).Named(tag + " Display")
            .Set("PuzzleDisplay", "size", sizeIndex).Set("PuzzleDisplay", "randomOnStart", true);
        Piece lamp = b.Empty("Room Light " + tag + " Shop FFE6B4 6.0 1.8", cx, BackZ - 1.4f, sb + 1.7f, false);

        // the telescope's drum (r 1.5, 0.3 high) with bulbs round its rim, and the telescope aimed due north
        float db = GroundAt(cx, DrumZ);
        var drum = new MeshBuilder();
        Kit.AddDisc(drum, new Vector3(cx, -0.5f, DrumZ), 1.5f, 0.8f, IsleKit.Stone, 32);
        Piece drumPiece = Place(tag + " Telescope Drum", drum, snowy, db);
        Piece scope = b.Part("Telescope", cx, ScopeZ, db + 0.3f + 1.1f, 0f, false).Named(tag + " Telescope")
            .Set("PuzzleTelescope", "invisibleInkView", false);
        var rim = new List<Vector3>();
        for (int k = 0; k <= 12; k++)
        {
            float a = k * 30f * Mathf.Deg2Rad;
            rim.Add(new Vector3(cx + Mathf.Sin(a) * 1.53f, db + 0.27f, DrumZ + Mathf.Cos(a) * 1.53f));
        }
        Piece rimLights = Kit.LightString(b, tag + " Drum Lights", rim, 0f, 0.4f, false, false, seed);

        if (group != null)
            foreach (Piece p in new[] { shop, display, lamp, drumPiece, scope, rimLights }) p.Under(group);
        return display;
    }

    // The lectern: a single post with a small backing plate for the Intercom Mic, which faces south so the Relay holds
    // it standing south of the lectern, facing the globe 11 m away; a little string of lights beside it. Returns the mic.
    Piece BuildLectern(Material snowy)
    {
        float lb = GroundAt(LecternX, LecternZ);
        float x = LecternX, z = LecternZ;
        var mb = new MeshBuilder();
        Block(mb, x - 0.2f, x + 0.2f, -0.1f, 0.06f, z - 0.15f, z + 0.25f, IsleKit.Stone);       // foot
        Block(mb, x - 0.06f, x + 0.06f, 0.06f, 0.82f, z + 0.01f, z + 0.13f, IsleKit.Wood);      // post
        Block(mb, x - 0.14f, x + 0.14f, 0.82f, 1.18f, z, z + 0.05f, IsleKit.DarkWood);          // backing plate (south face at z)
        Block(mb, x - 0.2f, x + 0.2f, 1.18f, 1.24f, z - 0.1f, z + 0.1f, IsleKit.Red);           // cap
        Place("Intercom Lectern", mb, snowy, lb);
        Piece mic = b.Part("Intercom Mic", x, z - 0.02f, lb + 1.0f, 180f, false).Named("Lectern Mic");
        Kit.LightString(b, "Lectern Lights", new List<Vector3> { new Vector3(x - 0.8f, 1.25f, z + 0.1f), new Vector3(x + 0.8f, 1.25f, z + 0.1f) },
            0.3f, 0.3f, true, true, 193);
        return mic;
    }

    // ================================================================ the scribble sheet

    // The eight winter scribbles as a custom peg sheet: 2048 px, a 4 x 4 grid, top-left cell blank, scribbles 1..8 in
    // cells 1..8 (cells 9..15 blank paper). Dark ink on cream; each is built from winter strokes (curls, zigzags, loops,
    // dots, knots) at one line weight, abstract enough that it has to be described stroke by stroke. Drawn here (not by
    // a Python tool) and only redrawn when SheetVersion changes.
    static Texture2D ScribbleSheet()
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", SheetPath));
        var existing = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
        if (!System.IO.File.Exists(full) || existing == null || existing.userData != SheetVersion)
        {
            const int Size = 2048, Cell = Size / 4;
            var cover = new float[Size * Size];
            for (int design = 1; design <= 8; design++)
            {
                int col = design % 4, rowFromTop = design / 4;
                int ox = col * Cell, oy = (3 - rowFromTop) * Cell;
                List<List<Vector2>> strokes = Strokes(design, out List<Vector3> dots);
                const float Pen = 0.027f;
                foreach (List<Vector2> line in strokes)
                    for (int i = 0; i + 1 < line.Count; i++)
                        Stamp(cover, Size, ox, oy, Cell, line[i], line[i + 1], Pen);
                foreach (Vector3 d in dots)
                    Stamp(cover, Size, ox, oy, Cell, new Vector2(d.x, d.y), new Vector2(d.x, d.y), d.z);
            }
            var px = new Color[Size * Size];
            for (int k = 0; k < px.Length; k++) px[k] = Color.Lerp(Paper, Ink, cover[k]);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            if (!AssetDatabase.IsValidFolder("Assets/Textures")) AssetDatabase.CreateFolder("Assets", "Textures");
            if (!AssetDatabase.IsValidFolder("Assets/Textures/SnowGlobe")) AssetDatabase.CreateFolder("Assets/Textures", "SnowGlobe");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.userData = SheetVersion;
            importer.SaveAndReimport();
        }
        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        if (sheet == null) throw new Exception("SnowGlobe: could not make " + SheetPath);
        return sheet;
    }

    // Inks a round-ended line from a to b (cell units, v up) of radius r into one cell, antialiased
    static void Stamp(float[] cover, int size, int ox, int oy, int cell, Vector2 a, Vector2 b2, float r)
    {
        Vector2 pa = a * cell, pb = b2 * cell;
        float rp = r * cell;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) - rp - 1f)), x1 = Mathf.Min(cell - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x) + rp + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) - rp - 1f)), y1 = Mathf.Min(cell - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y) + rp + 1f));
        Vector2 ab = pb - pa;
        float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / len2);
                float d = (p - (pa + ab * t)).magnitude;
                float c = Mathf.Clamp01(rp + 0.5f - d);
                if (c <= 0f) continue;
                int k = (oy + y) * size + ox + x;
                if (c > cover[k]) cover[k] = c;
            }
    }

    // A hand's wobble: a small, smooth, repeatable offset along a stroke
    static Vector2 Wobble(float t, int design) => new Vector2(Mathf.Sin(t * 19f + design * 1.7f), Mathf.Cos(t * 13f + design * 2.3f)) * 0.006f;

    // The strokes of one scribble (polylines in cell units, u right and v up, inside a 0.1 margin) and its dots
    // (u, v, radius)
    static List<List<Vector2>> Strokes(int design, out List<Vector3> dots)
    {
        var lines = new List<List<Vector2>>();
        dots = new List<Vector3>();
        List<Vector2> line;
        const int N = 360;
        switch (design)
        {
            case 1:     // frost curl: a spiral of two and a quarter turns from the middle out, then a tail sweeping down to the left
            {
                line = new List<Vector2>();
                var c = new Vector2(0.56f, 0.58f);
                for (int i = 0; i <= N; i++)
                {
                    float t = (float)i / N, ang = 0.6f + t * 4.5f * Mathf.PI, r = 0.025f + 0.235f * t;
                    line.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r + Wobble(t, design));
                }
                Vector2 from = line[line.Count - 1];
                Vector2 bend = new Vector2(0.28f, 0.26f), to = new Vector2(0.12f, 0.14f);
                for (int i = 1; i <= 40; i++)
                {
                    float t = i / 40f;
                    line.Add(Vector2.Lerp(Vector2.Lerp(from, bend, t), Vector2.Lerp(bend, to, t), t));
                }
                lines.Add(line);
                break;
            }
            case 2:     // icicle zigzag: a ragged row of five teeth of unequal length hanging from a line, a small loop at its right end
            {
                line = new List<Vector2> { new Vector2(0.11f, 0.74f) };
                float[] drop = { 0.24f, 0.42f, 0.18f, 0.47f, 0.3f };
                for (int k = 0; k < 5; k++)
                {
                    float xa = 0.11f + 0.14f * k;
                    line.Add(new Vector2(xa + 0.07f, 0.74f - drop[k]));
                    line.Add(new Vector2(xa + 0.14f, 0.74f));
                }
                var lc = new Vector2(0.81f, 0.81f);
                for (int i = 0; i <= 60; i++)
                {
                    float ang = -Mathf.PI * 0.5f - i / 60f * 2f * Mathf.PI;
                    line.Add(lc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.07f);
                }
                lines.Add(line);
                break;
            }
            case 3:     // drift loops: three cursive loops running up to the right, the last one larger
            {
                line = new List<Vector2>();
                for (int i = 0; i <= N; i++)
                {
                    float t = (float)i / N, s = t * 6f * Mathf.PI;
                    float bb = 0.075f + 0.04f * t;
                    float x = 0.16f + 0.62f * t - bb * Mathf.Sin(s);
                    float y = 0.34f + 0.2f * t + bb * Mathf.Cos(s) - bb;
                    line.Add(new Vector2(x, y) + Wobble(t, design));
                }
                lines.Add(line);
                break;
            }
            case 4:     // sled-track knot: a three-lobed knot drawn in one stroke
            {
                line = new List<Vector2>();
                for (int i = 0; i <= N; i++)
                {
                    float t = (float)i / N, a = t * 2f * Mathf.PI;
                    float x = Mathf.Sin(a) + 2f * Mathf.Sin(2f * a), y = Mathf.Cos(a) - 2f * Mathf.Cos(2f * a);
                    line.Add(new Vector2(0.5f + x * 0.115f, 0.55f + y * 0.115f) + Wobble(t, design));
                }
                lines.Add(line);
                break;
            }
            case 5:     // wind wave: a wave dying away to the right with three dots of falling snow above it
            {
                line = new List<Vector2>();
                for (int i = 0; i <= N; i++)
                {
                    float t = (float)i / N;
                    float amp = 0.16f * (1f - 0.65f * t);
                    line.Add(new Vector2(0.12f + 0.76f * t, 0.38f + amp * Mathf.Sin(t * 5f * Mathf.PI)) + Wobble(t, design));
                }
                lines.Add(line);
                dots.Add(new Vector3(0.24f, 0.78f, 0.04f));
                dots.Add(new Vector3(0.47f, 0.84f, 0.04f));
                dots.Add(new Vector3(0.66f, 0.73f, 0.04f));
                break;
            }
            case 6:     // double curl: two small spirals turning opposite ways, joined by a sagging line
            {
                // the first spiral from its centre out to its rim (turning anticlockwise)
                var joined = new List<Vector2>();
                var ca = new Vector2(0.3f, 0.62f);
                for (int i = 0; i <= N / 2; i++)
                {
                    float t = (float)i / (N / 2), ang = t * 3.2f * Mathf.PI, r = 0.02f + 0.15f * t;
                    joined.Add(ca + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r + Wobble(t, design));
                }
                // the second from its rim in to its centre (turning clockwise), reached by a sagging bridge
                var cb = new Vector2(0.7f, 0.38f);
                Vector2 rimA = joined[joined.Count - 1];
                var outerB = new List<Vector2>();
                for (int i = N / 2; i >= 0; i--)
                {
                    float t = (float)i / (N / 2), ang = Mathf.PI - t * 3.2f * Mathf.PI, r = 0.02f + 0.15f * t;
                    outerB.Add(cb + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r + Wobble(t + 3f, design));
                }
                Vector2 rimB = outerB[0];
                var mid = new Vector2((rimA.x + rimB.x) / 2f, Mathf.Min(rimA.y, rimB.y) - 0.12f);
                for (int i = 1; i < 30; i++)
                {
                    float t = i / 30f;
                    joined.Add(Vector2.Lerp(Vector2.Lerp(rimA, mid, t), Vector2.Lerp(mid, rimB, t), t));
                }
                joined.AddRange(outerB);
                lines.Add(joined);
                break;
            }
            case 7:     // falling zigzag: a jagged line down from the top right, ending in a hook that curls round to the right
            {
                line = new List<Vector2>
                {
                    new Vector2(0.66f, 0.89f), new Vector2(0.42f, 0.7f), new Vector2(0.6f, 0.6f),
                    new Vector2(0.34f, 0.42f), new Vector2(0.5f, 0.34f),
                };
                var hc = new Vector2(0.6f, 0.24f);
                for (int i = 0; i <= 90; i++)
                {
                    float t = i / 90f, ang = Mathf.PI * 0.75f + t * 2.6f * Mathf.PI, r = 0.13f * (1f - 0.6f * t);
                    line.Add(hc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f) * r);
                }
                lines.Add(line);
                break;
            }
            default:    // 8, looped snow: a curve with a small loop inside a larger one, its tail running out up to the right with a dot beyond
            {
                line = new List<Vector2>();
                var c = new Vector2(0.4f, 0.46f);
                for (int i = 0; i <= N; i++)
                {
                    float t = (float)i / N, th = t * 2f * Mathf.PI;
                    float r = 0.1f + 0.2f * Mathf.Cos(th);
                    line.Add(c + new Vector2(Mathf.Cos(th + 0.5f), Mathf.Sin(th + 0.5f)) * r + Wobble(t, design));
                }
                Vector2 from = line[line.Count - 1], bend = new Vector2(0.78f, 0.5f), to = new Vector2(0.8f, 0.8f);
                for (int i = 1; i <= 40; i++)
                {
                    float t = i / 40f;
                    line.Add(Vector2.Lerp(Vector2.Lerp(from, bend, t), Vector2.Lerp(bend, to, t), t));
                }
                lines.Add(line);
                dots.Add(new Vector3(0.66f, 0.86f, 0.035f));
                break;
            }
        }
        return lines;
    }
}
