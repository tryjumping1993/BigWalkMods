// Advent Trail: the Advent Post, a game of telephone in three huts along a lantern trail (spec: puzzle-specs\AdventTrail.md,
// revision 2). At the top of the trail stands the Calendar House, sealed and soundproof; while the puzzle runs its door
// stays shut. Inside, round a short baffle wall, the Keeper sees three of today's five advent "doors" (1-flap displays
// over brass plates 5, 1, 3) and describes them aloud. Nobody hears them except whoever sits in Listening Chair 1 in the
// Listening Post halfway down the trail: the House Mic moves that sitter's ears into the house. The Listener sees the
// other two doors (4, 2), puts all five in day order and calls them out through the booth's wooden walls to the
// Postmaster in the yard, who picks five of twelve tiles, hangs them on the calendar board at days 1 to 5 and presses
// check. One way all along: the Keeper hears nobody, the seated Listener hears only the house.
// A 7.5-minute clock and three strikes (kept over a manual stop); running out of time, a third strike, or anyone leaving
// a hut mid-round stops the puzzle, which opens the doors, blanks the displays and re-deals on the next start.
// 4-lobby: the Post is sealed soundproof and a mic in it feeds Listening Chair 2 in the yard (the Runner, one more link).
// 2-lobby: the Post is an open booth (no door, turnstile or sensor), a memory-and-legwork version.
// No custom code. Coordinates: local metres, x right, z forward (towards the house), heights above the pad's top.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class AdventTrailPuzzle : PuzzleDef
{
    public override string Name => "AdventTrail";
    public override Vector2 Footprint => new Vector2(20f, 38f);
    public override Vector2? FixedSite => new Vector2(-614f, -712f);      // layout-final.json: the plaza hub, a quiet slope 101 m by path from the plaza

    const float Floor = 0.25f;      // the pad's top above the root's level (pad 1.75 thick, sunk 1.5)
    const float Clock = 450f;       // seven and a half minutes
    static readonly Color Brass = new Color(0.25f, 0.12f, 0.08f);
    static readonly Color Flag = new Color(0.42f, 0.42f, 0.44f);

    PuzzleBuilder b;

    // A solid block by its extents (x0..x1, z0..z1, y0..y1 above the pad's top) into a mesh built in pad space
    static void Block(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float y1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);

    // A gable roof with its ridge along x over x0..x1, its eaves at z0 and z1 at height 'eave', the ridge at 'ridge' over the
    // middle; slopes on top and underneath, gable ends at both x ends
    static void Gable(MeshBuilder mb, float x0, float x1, float z0, float z1, float eave, float ridge, Color roof, Color end)
    {
        float zc = (z0 + z1) / 2f;
        var s0 = new Vector3(x0, eave, z0); var s1 = new Vector3(x1, eave, z0);
        var n0 = new Vector3(x0, eave, z1); var n1 = new Vector3(x1, eave, z1);
        var r0 = new Vector3(x0, ridge, zc); var r1 = new Vector3(x1, ridge, zc);
        Color under = roof * 0.7f; under.a = 1f;
        mb.Quad(s0, s1, r1, r0, roof);      // south slope, seen from the south
        mb.Quad(n1, n0, r0, r1, roof);      // north slope, seen from the north
        mb.Quad(r0, r1, s1, s0, under);     // and their undersides
        mb.Quad(r1, r0, n0, n1, under);
        mb.Tri(s1, r1, n1, end);            // east gable, seen from the east
        mb.Tri(n0, r0, s0, end);            // west gable, seen from the west
    }

    // The old puzzle's pictures, now the lantern toppers: 0 bell, 1 star, 2 candle, 3 tree
    static void Topper(MeshBuilder mb, int which, Vector3 at)
    {
        if (which == 0) { Kit.AddBall(mb, at, 0.16f, IsleKit.Gold); Kit.AddBox(mb, at + Vector3.down * 0.2f, new Vector3(0.36f, 0.06f, 0.06f), IsleKit.Gold); }
        else if (which == 1) Kit.AddStar(mb, at, 0.22f, 0.04f, IsleKit.Gold);
        else if (which == 2) { Kit.AddBox(mb, at + Vector3.down * 0.22f, new Vector3(0.1f, 0.3f, 0.1f), IsleKit.Cream); Kit.AddBall(mb, at + Vector3.up * 0.14f, 0.05f, new Color(1f, 0.7f, 0.2f)); }
        else for (int t = 0; t < 3; t++) Kit.AddBox(mb, at + Vector3.down * (0.22f - t * 0.13f), new Vector3(0.36f - t * 0.1f, 0.13f, 0.06f), IsleKit.Green);
    }

    // PuzzleSign.size is a Vector2, which Sdk.Set has no case for (and Sdk.cs is not this puzzle's to change)
    static void SignSize(Piece sign, Vector2 size)
    {
        var so = new UnityEditor.SerializedObject(sign.Component("PuzzleSign"));
        UnityEditor.SerializedProperty p = so.FindProperty("size");
        if (p == null) throw new System.Exception("PuzzleSign has no field 'size'");
        p.vector2Value = size;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // A sign's text (as SummitDiscGolfPuzzle.SetText): the TextMeshPro child the prefab carries, made if missing
    static void SetText(Piece sign, string text, float fontSize, Vector2 box)
    {
        var tmp = sign.Go.GetComponentInChildren<TMPro.TextMeshPro>(true);
        if (tmp == null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(sign.Go.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
        }
        tmp.rectTransform.sizeDelta = box;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Brass;
    }

    // A brass advent-door number: a small sign (0.3 of the game's) with one digit
    Piece Plate(string name, float x, float z, float h, float yaw, string digit)
    {
        Piece plate = b.Part("Sign", x, z, Floor + h, yaw, false).Named(name);
        SignSize(plate, new Vector2(0.3f, 0.3f));
        SetText(plate, digit, 2.2f, new Vector2(0.2f, 0.28f));
        return plate;
    }

    Piece Display(string name, float x, float z, float h)
        => b.Part("Display", x, z, Floor + h, 0f, false).Named(name).Set("PuzzleDisplay", "size", 0).Set("PuzzleDisplay", "randomOnStart", true);

    Piece Timer(string name, float x, float z, float h, float yaw, float scale)
        => b.Part("Timer", x, z, Floor + h, yaw, false).Named(name).Scaled(scale * PuzzleBuilder.TimerScale, scale * PuzzleBuilder.TimerScale, scale * PuzzleBuilder.TimerScale).Set("PuzzleTimer", "duration", Clock);

    Piece Strikes(string name, float x, float z, float h, float yaw, float scaleX)
        => b.Part("Progress Bar", x, z, Floor + h, yaw, false).Named(name).Scaled(scaleX, 1f, 1f).Set("PuzzleProgressBar", "total", 3).Set("PuzzleProgressBar", "style", 0);

    Piece DeadZone(string name, float x, float z, Vector3 size)
    {
        Piece dead = b.Part("Radio Dead Zone", x, z, Floor + 1.5f, 0f, false).Named(name);
        BoxCollider zone = dead.Go.GetComponent<BoxCollider>();
        if (zone == null) { zone = dead.Go.AddComponent<BoxCollider>(); zone.isTrigger = true; }
        zone.size = size;
        zone.center = Vector3.zero;
        return dead;
    }

    // A directional mic standing on the floor (its pivot is 0.75 m above its foot, part-metrics), aimed at a head
    Piece Mic(string name, float x, float z, Vector3 head)
    {
        const float pivot = 0.75f;
        Piece mic = b.Part("Directional Mic", x, z, Floor + pivot, 0f, false).Named(name);
        Vector3 aim = new Vector3(head.x - x, head.y - pivot, head.z - z);
        mic.Transform.rotation = b.Root.transform.rotation * Quaternion.LookRotation(aim.normalized);
        return mic;
    }

    // A hut's walls (outer x0..x1, z0..z1, 'wall' thick, 'height' high, sunk 0.5 into the pad) with a door gap in the
    // south wall over gx0..gx1 (cut to the door's 1.24 m, its top at 2.02), a flat roof slab and a floor slab buried just
    // under the pad's top (no step, no z-fight; it closes the voice path under the walls on a terraced site)
    static void Hut(MeshBuilder mb, float x0, float x1, float z0, float z1, float wall, float height, float roof, float gx0, float gx1, Color c, Color roofColour)
    {
        const float sunk = -0.5f, doorTop = 2.02f;
        Block(mb, x0, gx0, z0, z0 + wall, sunk, height, c);                      // south wall, west of the door
        Block(mb, gx1, x1, z0, z0 + wall, sunk, height, c);                      // and east of it
        Block(mb, gx0, gx1, z0, z0 + wall, doorTop, height, c);                  // lintel
        Block(mb, x0, x1, z1 - wall, z1, sunk, height, c);                       // north
        Block(mb, x0, x0 + wall, z0 + wall, z1 - wall, sunk, height, c);         // west
        Block(mb, x1 - wall, x1, z0 + wall, z1 - wall, sunk, height, c);         // east
        Block(mb, x0, x1, z0, z1, height, height + roof, roofColour);           // roof slab
        Block(mb, x0, x1, z0, z1, -0.3f, -0.01f, c);                             // floor slab
    }

    // A roofed porch (interior x0..x1, z0..z1, plain wood) open only at its south end; its north end meets a hut whose
    // east wall is at hutEast (a stub closes the corner where the porch's east wall stands out past it)
    static void Porch(MeshBuilder mb, float x0, float x1, float z0, float z1, float hutEast, float hutWall)
    {
        const float wall = 0.2f, height = 2.5f;
        Block(mb, x0 - wall, x0, z0, z1, 0f, height, IsleKit.Wood);
        Block(mb, x1, x1 + wall, z0, z1, 0f, height, IsleKit.Wood);
        Block(mb, hutEast, x1 + wall, z1, z1 + hutWall, 0f, height, IsleKit.Wood);
        Block(mb, x0 - wall, x1 + wall, z0, z1, height, height + 0.15f, IsleKit.DarkWood);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(20f, 38f);
        b.DebugTeleport(0f, -13f, Floor + 1.2f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Vector3 padOrigin = b.WorldPoint(0f, 0f, Floor, false);      // meshes below are built in pad space (y = 0 the pad's top)
        Component sdk = b.Puzzle;

        // ---- the pad: one level slab for everything, sunk well in so uneven ground never shows a gap ----
        var pad = new MeshBuilder();
        Block(pad, -10f, 10f, -19f, 19f, -1.75f, 0f, IsleKit.Stone);
        b.MeshPiece("Advent Pad", pad.ToMesh(), snowy).Transform.position = padOrigin;
        IsleKit.CalmAir(b, "Advent Trail", 0f, 0f, new Vector3(36f, 120f, 54f), 0f, -10f);      // no jetpacks over the huts or the yard

        // ======== the Calendar House (the Keeper): centre (0, 16.1), sealed and soundproof ========
        Piece house = b.Empty("Calendar House", 0f, 16.1f, Floor, false);
        Sdk.Set(b.AddScript(house, "PuzzleSoundProof"), "material", 0);      // Wood
        var houseMb = new MeshBuilder();
        Hut(houseMb, -1.84f, 1.84f, 14.36f, 17.84f, 0.24f, 2.6f, 0.24f, 0.23f, 1.47f, IsleKit.Red, IsleKit.DarkWood);
        Block(houseMb, 0.04f, 0.16f, 14.6f, 15.9f, -0.5f, 2.6f, IsleKit.Red);      // the baffle: the door opens east of it, the displays are west
        Piece houseWalls = b.MeshPiece("Calendar House Walls", houseMb.ToMesh(), snowy);
        houseWalls.Transform.position = padOrigin;
        houseWalls.Under(house);

        b.Part("Door", 0.85f, 14.48f, Floor, 0f, false).Named("House Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);
        // the Keeper's three doors, on the inside of the door's own wall west of the baffle (no line from the doorway reaches them)
        Piece houseDoor5 = Display("House Door 5", -0.45f, 14.77f, 1.6f);
        Piece houseDoor1 = Display("House Door 1", -0.9f, 14.77f, 1.6f);
        Piece houseDoor3 = Display("House Door 3", -1.35f, 14.77f, 1.6f);
        Plate("House Plate 5", -0.45f, 14.63f, 1.22f, 0f, "5");
        Plate("House Plate 1", -0.9f, 14.63f, 1.22f, 0f, "1");
        Plate("House Plate 3", -1.35f, 14.63f, 1.22f, 0f, "3");
        // the House Mic, north of the displays, aimed at the Keeper's head as they read them (not under the soundproof parent)
        Piece houseMic = Mic("House Mic", -0.8f, 16.3f, new Vector3(-0.9f, 1.0f, 15.3f));
        Piece housePresence = IsleKit.Sensor(b, "House Presence", 0f, 16.1f, new Vector3(3.1f, 2.4f, 2.9f), Floor, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
        // flush on the north wall's inner face (z 17.6): the switch reaches 0.073 m behind its pivot, the lamp 0.006 m
        b.Part("Switch Start", 0.9f, 17.527f, Floor + 1.25f, 180f, false).Named("House Switch");      // the Keeper's quit
        b.Part("Live Light", 0.9f, 17.594f, Floor + 1.45f, 180f, false).Named("House Live");
        Piece houseClock = Timer("House Clock", -1.48f, 16.5f, 1.75f, 90f, 0.6f);
        Piece houseStrikes = Strikes("House Strikes", -1.48f, 16.5f, 2.25f, 90f, 0.3f);
        Piece houseLamp = b.Part("Light 2", 0f, 16.1f, Floor + 2.6f, 0f, false).Named("House Lamp");
        b.Part("Confetti", 1.0f, 15.5f, Floor + 0.1f, 0f, false).Named("House Confetti");
        DeadZone("House Dead Zone", 0f, 16.1f, new Vector3(4f, 3.4f, 3.8f));

        // ======== the Listening Post (the Listener): centre (0, 1.1) ========
        Piece listenChair = b.Part("Listening Chair", -0.6f, 1.4f, Floor, 180f, false).Named("Listening Chair 1");
        Sdk.Set(listenChair.Component("PuzzleListeningChair"), "microphone", houseMic.Component("PuzzleDirectionalMic"));
        Piece postDoor4 = Display("Post Door 4", -0.35f, -0.43f, 1.4f);
        Piece postDoor2 = Display("Post Door 2", -0.8f, -0.43f, 1.4f);
        Plate("Post Plate 4", -0.35f, -0.57f, 1.02f, 0f, "4");
        Plate("Post Plate 2", -0.8f, -0.57f, 1.02f, 0f, "2");
        b.Part("Switch Start", 1.58f, 1.8f, Floor + 1.2f, -90f, false).Named("Post Switch");      // the Listener's quit
        b.Part("Live Light", 1.59f, 2.2f, Floor + 1.45f, -90f, false).Named("Post Live");
        Piece postClock = Timer("Post Clock", -1.48f, 0.4f, 1.9f, 90f, 0.6f);
        Piece postLamp = b.Part("Light 2", 0f, 1.1f, Floor + 2.5f, 0f, false).Named("Post Lamp");
        DeadZone("Post Dead Zone", 0f, 1.1f, new Vector3(4.4f, 3.2f, 4.6f));

        // 3 and 4 players: the Post's sensor and turnstile
        Piece guarded = IsleKit.Lobby(b, "Post Guard", false, true, true);
        Piece postPresence = IsleKit.Sensor(b, "Post Presence", 0f, 1.1f, new Vector3(3.1f, 2.4f, 3.3f), Floor, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1).Under(guarded);
        b.Part("Gate No items", 0.9f, -2.7f, Floor, 0f, false).Named("Post Turnstile").Set("PuzzleNoItemsGate", "oneWay", false).Under(guarded);
        // 3 players: a plain door (the Listener's call carries through the wooden walls to the yard)
        Piece three = IsleKit.Lobby(b, "Post Plain Door", false, true, false);
        b.Part("Door", 0.9f, -0.7f, Floor, 0f, false).Named("Post Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", false).Under(three);
        // 2 and 3 players: the plain walls (2: no door at all, an open booth)
        Piece plain = IsleKit.Lobby(b, "Post Plain", true, true, false);
        var postMb = new MeshBuilder();
        Hut(postMb, -1.8f, 1.8f, -0.8f, 3.0f, 0.2f, 2.5f, 0.2f, 0.28f, 1.52f, IsleKit.Green, IsleKit.DarkWood);
        Piece postWalls = b.MeshPiece("Post Walls", postMb.ToMesh(), snowy);
        postWalls.Transform.position = padOrigin;
        postWalls.Under(plain);
        // 4 players: the same walls sealed soundproof, a soundproof door, the Post Mic feeding Listening Chair 2 in the yard
        Piece four = IsleKit.Lobby(b, "Post Sealed", false, false, true);
        Piece sealedParent = b.Empty("Post Walls Sealed", 0f, 1.1f, Floor, false).Under(four);
        Sdk.Set(b.AddScript(sealedParent, "PuzzleSoundProof"), "material", 0);
        var sealedMb = new MeshBuilder();
        Hut(sealedMb, -1.8f, 1.8f, -0.8f, 3.0f, 0.2f, 2.5f, 0.2f, 0.28f, 1.52f, IsleKit.Green, IsleKit.DarkWood);
        Piece sealedWalls = b.MeshPiece("Post Walls Sealed Mesh", sealedMb.ToMesh(), snowy);
        sealedWalls.Transform.position = padOrigin;
        sealedWalls.Under(sealedParent);
        b.Part("Door", 0.9f, -0.7f, Floor, 0f, false).Named("Post Door Sealed")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true).Under(four);
        Piece postMic = Mic("Post Mic", 1.1f, 2.4f, new Vector3(-0.6f, 0.9f, 1.4f)).Under(four);      // aimed at the seated Listener's head
        var bench = new MeshBuilder();
        Block(bench, 5.1f, 6.7f, -12.5f, -10.7f, 0f, 0.12f, IsleKit.Stone);
        Piece benchPiece = b.MeshPiece("Chair 2 Bench", bench.ToMesh(), snowy);
        benchPiece.Transform.position = padOrigin;
        benchPiece.Under(four);
        Piece runnerChair = b.Part("Listening Chair", 5.5f, -12f, Floor + 0.12f, 180f, false).Named("Listening Chair 2").Under(four);
        Sdk.Set(runnerChair.Component("PuzzleListeningChair"), "microphone", postMic.Component("PuzzleDirectionalMic"));

        // ---- porches (plain wood, every lobby), each with a two-way no-items turnstile at its mouth ----
        var porches = new MeshBuilder();
        Porch(porches, -0.25f, 2.0f, 11.7f, 14.36f, 1.84f, 0.24f);
        Porch(porches, -0.25f, 2.0f, -3.4f, -0.8f, 1.8f, 0.2f);
        b.MeshPiece("Porches", porches.ToMesh(), snowy).Transform.position = padOrigin;
        b.Part("Gate No items", 0.9f, 12.4f, Floor, 0f, false).Named("House Turnstile").Set("PuzzleNoItemsGate", "oneWay", false);

        // ---- the huts' pitched roofs, the wreath and the star (decoration, outside the soundproof parents) ----
        var roofs = new MeshBuilder();
        Gable(roofs, -2.04f, 2.04f, 14.16f, 18.04f, 2.84f, 3.7f, IsleKit.Red * 0.75f + IsleKit.DarkWood * 0.25f, IsleKit.Cream);
        Gable(roofs, -2.0f, 2.0f, -1.0f, 3.2f, 2.7f, 3.45f, IsleKit.DarkWood, IsleKit.Green);
        b.MeshPiece("Hut Roofs", roofs.ToMesh(), snowy).Transform.position = padOrigin;
        var trim = new MeshBuilder();
        for (int k = 0; k < 12; k++)
        {
            float a = 2f * Mathf.PI * k / 12f;
            Kit.AddBall(trim, new Vector3(0.85f + Mathf.Cos(a) * 0.15f, 2.3f + Mathf.Sin(a) * 0.15f, 14.29f), 0.06f, new Color(0.1f, 0.34f, 0.16f));
        }
        Kit.AddBall(trim, new Vector3(0.85f, 2.15f, 14.26f), 0.05f, IsleKit.Red);
        Kit.AddStar(trim, new Vector3(0f, 4.0f, 16.1f), 0.3f, 0.05f, IsleKit.Gold);

        // ======== the lantern trail between the huts: stones, lanterns with the old toppers, lights ========
        var trail = new MeshBuilder();
        var glow = new MeshBuilder();
        var rng = new System.Random(2311);
        for (float z = 3.5f; z < 11.4f; z += 0.62f)
            for (float x = -1.05f; x < 1.0f; x += 0.7f)
            {
                float w = 0.5f + 0.15f * (float)rng.NextDouble(), d = 0.45f + 0.1f * (float)rng.NextDouble();
                float shade = 0.9f + 0.2f * (float)rng.NextDouble();
                var stone = new Color(Flag.r * shade, Flag.g * shade, Flag.b * shade, 1f);
                Kit.AddBox(trail, new Vector3(x + 0.35f + 0.08f * ((float)rng.NextDouble() - 0.5f), 0.004f, z + 0.3f), new Vector3(w, 0.02f, d), stone, (float)rng.NextDouble() * 20f - 10f);
            }
        b.MeshPiece("Trail Stones", trail.ToMesh(), bare, false).Transform.position = padOrigin;
        var lanterns = new MeshBuilder();
        var west = new List<Vector3>();
        var east = new List<Vector3>();
        for (int k = 0; k < 10; k++)
        {
            float z = 3.6f + k * 0.8f;      // stops 0.9 m short of the house porch's mouth, clear of its turnstile
            foreach (float side in new[] { -1.6f, 1.6f })
            {
                Kit.AddLantern(lanterns, glow, new Vector3(side, 0f, z), k * 2 + (side > 0f ? 1 : 0));
                Topper(trim, (k * 2 + (side > 0f ? 1 : 0)) % 4, new Vector3(side, 2.82f, z));
                (side < 0f ? west : east).Add(new Vector3(side, Floor + 2.56f, z));      // the wire rests on the lanterns' hoods
            }
        }
        b.MeshPiece("Trail Lanterns", lanterns.ToMesh(), snowy).Transform.position = padOrigin;
        b.MeshPiece("Trail Lantern Glow", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false)
            .Transform.position = padOrigin;
        b.MeshPiece("Advent Trim", trim.ToMesh(), bare, false).Transform.position = padOrigin;
        Kit.LightString(b, "Trail Lights West", west, 0.25f, 0.4f, false, false, 233);
        Kit.LightString(b, "Trail Lights East", east, 0.25f, 0.4f, false, false, 234);

        // ======== the yard (the Postmaster): the calendar board, the tile table, the start corner ========
        Piece boardPiece = IsleKit.BoardOnPosts(b, "Calendar Board", 0f, -9f, 180f, 5.0f, 0.4f, 2.7f, IsleKit.Board, false);
        boardPiece.Transform.position = b.WorldPoint(0f, -9f, Floor, false);      // on the pad's top
        Piece check = b.Part("Validator", 1.7f, -9.16f, Floor + 1.9f, 180f, false).Named("Check Screen");
        Piece poke = b.Part("Button Poke", 1.7f, -9.16f, Floor + 1.2f, 180f, false).Named("Check Button");
        Component checkC = check.Component("PuzzleValidator");
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));
        Piece calendar = b.Part("Peg Board", -1.5f, -9.16f, Floor + 1.25f, 180f, false).Named("Calendar").Set("PuzzlePegBoard", "size", 4);      // five slots
        Component calendarC = calendar.Component("PuzzlePegBoard");
        // slot n is day n: House Door 1, Post Door 2, House Door 3, Post Door 4, House Door 5, each its display's only flap
        Sdk.Add(calendarC, "answerFrom", houseDoor1.Component("PuzzleDisplay"), postDoor2.Component("PuzzleDisplay"),
            houseDoor3.Component("PuzzleDisplay"), postDoor4.Component("PuzzleDisplay"), houseDoor5.Component("PuzzleDisplay"));
        Sdk.AddInts(calendarC, "answerFromFlap", 1, 1, 1, 1, 1);
        Sdk.Set(calendarC, "validator", checkC);
        for (int n = 1; n <= 5; n++) Plate("Day Plate " + n, -1.5f + 0.45f * (n - 1), -9.18f, 0.85f, 180f, n.ToString());
        Piece yardClock = Timer("Yard Clock", -1.9f, -9.17f, 2.3f, 180f, 0.8f);
        Piece yardStrikes = Strikes("Yard Strikes", 0f, -9.17f, 2.35f, 180f, 0.35f);

        // the tile table: twelve tiles in two rows, 8 Shapes and 4 Scribbles (imageSet index 3: PuzzlePegSet's values are
        // not its indices, and Sdk.Set writes the index)
        var table = new MeshBuilder();
        Block(table, -7.4f, -3.6f, -12.6f, -11.4f, 0.84f, 0.9f, IsleKit.Wood);
        foreach (float lx in new[] { -7.3f, -3.7f })
            foreach (float lz in new[] { -12.5f, -11.5f })
                Block(table, lx - 0.05f, lx + 0.05f, lz - 0.05f, lz + 0.05f, 0f, 0.84f, IsleKit.DarkWood);
        Block(table, -7.4f, -3.6f, -12.6f, -12.55f, 0.9f, 0.94f, IsleKit.DarkWood);      // the raised lip
        Block(table, -7.4f, -3.6f, -11.45f, -11.4f, 0.9f, 0.94f, IsleKit.DarkWood);
        Block(table, -7.4f, -7.35f, -12.55f, -11.45f, 0.9f, 0.94f, IsleKit.DarkWood);
        Block(table, -3.65f, -3.6f, -12.55f, -11.45f, 0.9f, 0.94f, IsleKit.DarkWood);
        b.MeshPiece("Tile Table", table.ToMesh(), snowy).Transform.position = padOrigin;
        for (int i = 0; i < 12; i++)
        {
            float tx = -7.0f + 0.6f * (i % 6), tz = i < 6 ? -12.3f : -11.7f;
            Piece tile = b.Part("Peg Tile", tx, tz, Floor + 0.9f + 0.24f, 180f, false).Named("Tile " + (i + 1));
            if (i < 8) tile.Set("PuzzlePegTile", "imageSet", 0).Set("PuzzlePegTile", "image", i + 1);
            else tile.Set("PuzzlePegTile", "imageSet", 3).Set("PuzzlePegTile", "image", i - 7);
        }

        IsleKit.StartCorner(b, 2f, -16f, 0f, true, Floor, false);
        Kit.LightRing(b, "Site Lights", 0f, -12f, 16f, 10f, Floor + 2.6f, false, 231);

        // ======== events ========
        Component houseClockC = houseClock.Component("PuzzleTimer"), postClockC = postClock.Component("PuzzleTimer"), yardClockC = yardClock.Component("PuzzleTimer");
        Component houseBar = houseStrikes.Component("PuzzleProgressBar"), yardBar = yardStrikes.Component("PuzzleProgressBar");
        foreach (Component clock in new[] { houseClockC, postClockC, yardClockC })
        {
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", clock, "StartTimer");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", clock, "ResetTimer");
        }
        foreach (Piece lamp in new[] { houseLamp, postLamp })
        {
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", lamp.Component("PuzzleLight"), "TurnOn");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", lamp.Component("PuzzleLight"), "TurnOff");
        }
        // a win stops the puzzle: both doors open (mode 1), the displays blank, the clocks reset
        b.RootPiece.On("PuzzleSDK", "onWin", sdk, "StopPuzzle");
        // anyone leaving a hut mid-round (walking, a teleport, a rejoin, a dinosaur) stops it; StopPuzzle does nothing when stopped
        housePresence.On("PuzzlePresenceSensor", "onPlayerLeft", sdk, "StopPuzzle");
        postPresence.On("PuzzlePresenceSensor", "onPlayerLeft", sdk, "StopPuzzle");
        // a time-out: the failure look on both bars (they empty after 1 s), then stop
        yardClock.On("PuzzleTimer", "onFinished", houseBar, "Fail");
        yardClock.On("PuzzleTimer", "onFinished", yardBar, "Fail");
        yardClock.On("PuzzleTimer", "onFinished", sdk, "StopPuzzle");
        // a wrong check is a strike on both bars; the third fails and stops. Strikes are never reset on start, so a manual
        // stop keeps them
        check.On("PuzzleValidator", "onIncorrect", houseBar, "Increment");
        check.On("PuzzleValidator", "onIncorrect", yardBar, "Increment");
        yardStrikes.On("PuzzleProgressBar", "onFull", houseBar, "Fail");
        yardStrikes.On("PuzzleProgressBar", "onFull", yardBar, "Fail");
        yardStrikes.On("PuzzleProgressBar", "onFull", sdk, "StopPuzzle");
    }
}
