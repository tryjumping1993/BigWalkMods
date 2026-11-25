// Fairy Lights (puzzle 20): the Lamplighter's Glade. "Light the whole glade." Seven lamp posts stand in a ring round
// the gold Star Ring in the middle of a woodland glade strung with fairy lights. Each post has a pendant lamp on an arm
// over the ring's inside and a button under it. A button flips its own lamp and the lamp on either side of it: a lit
// one goes dark, a dark one lights. Every start deals a new pattern with at least three lamps dark; when all seven are
// lit the star on the pole lights and the gourd drops. There is no clock and nothing is lost by a press: one player
// can solve it alone (every deal can be solved), and more can each take a few posts and talk it through.
//
// The logic is the LightsOut script (Assets/PuzzleScripts): it finds "Ring Lamp 1".."Ring Lamp 7" and every "Ring
// Bar..." by name, so nothing else here may have a name that starts so. The posts are numbered clockwise seen from
// above, post 1 due south (nearest the start corner). Gold fairy lights run from each lamp's arm to the next, so the
// strings show which lamps are neighbours; a board by the start corner counts the lit lamps and shows the rule.
// (Before the user's rules of 2026-11-24: a caller sealed in a windowless lodge named lit lamps' scribble pictures over
// a one-way intercom, and runners stood on pads under those pictures before a 7.5 s phase ran out, ten in a row.)
//
// Coordinates: local metres, x right, z forward (north, towards the glade), height above the (levelled) ground.
// Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class FairyLightsPuzzle : PuzzleDef
{
    public override string Name => "FairyLights";
    public override Vector2 Footprint => new Vector2(42f, 48f);
    public override Vector2? FixedSite => new Vector2(-860f, -2090f);      // the lowland woodland clearing (layout-final)

    const int Posts = 7;                  // not a multiple of 3: every deal can be solved, in one way only (LightsOut)
    const float RingRadius = 6.5f;        // the ring's middle to a post's middle
    const float PostTop = 2.7f;
    const float ArmUnder = 2.58f;         // the arm's underside
    const float LampIn = 0.75f;           // the arm's tip, in from its post's middle towards the star: the strings' tie
    const float LampH = 2.2f;             // the lamp disc's middle (Light 6, 0.48 m across), under the arm
    const float LampBoardIn = 0.11f;      // its backing board's middle, in from the post's middle (the post is 0.2 square)
    const float StringH = 2.7f;           // the fairy lights are tied to the arm over the lamp
    const float PlateFace = 0.13f;        // the button plate's face, in from the post's middle
    const float ButtonH = 1.15f;
    const float LampRange = 4f;

    // The rule board beside the start corner, facing the glade (+z): its middle, its face from BoardLow to BoardHigh
    const float BoardX = -2.6f, BoardZ = -6.4f, BoardWide = 3.0f, BoardLow = 0.7f, BoardHigh = 2.7f;
    const float BoardFace = 0.06f;        // the face, in front of the board's middle (IsleKit.BoardOnPosts: 0.12 m thick)
    const float BarScale = 0.75f;
    const float BarBack = 0.101f;         // a Progress Bar's back is 0.101 m behind its pivot (part-metrics)

    // near white: an unlit lamp is a dull disc, a lit one a bright one
    static readonly Color LampWhite = new Color(1f, 0.92f, 0.75f);
    static readonly Vector2 Star = new Vector2(0f, 6f);      // the ring's middle: the Star Ring and its pole

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(-8f, -3.5f);
        b.ClearGround(42f, 48f, 0f, 2f);
        // no jetpack thrust over the glade
        IsleKit.CalmAir(b, "Fairy Lights", 0f, 2f, new Vector3(80f, 150f, 86f), 0f, -10f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ---- the ring: seven timber posts, each with its lamp under an arm and its button on a plate ----
        var glade = new MeshBuilder();      // posts, arms, plates and the star pole (solid; snow lies on their tops)
        var marks = new MeshBuilder();      // paint on the plates (no snow, no collider)
        var buttons = new Piece[Posts + 1];
        for (int k = 1; k <= Posts; k++)
        {
            float bearing = Bearing(k), inward = bearing + 180f;
            // (the arm starts inside the post and the plate is let 1 cm into it: no two faces in one plane)
            Vector2 post = AtPost(k, 0f), arm = AtPost(k, 0.45f), plate = AtPost(k, PlateFace - 0.02f);
            Kit.AddBox(glade, new Vector3(post.x, -0.3f, post.y), new Vector3(0.2f, PostTop + 0.3f, 0.2f), IsleKit.Wood, bearing);
            Kit.AddBox(glade, new Vector3(arm.x, ArmUnder, arm.y), new Vector3(0.12f, 0.1f, 0.9f), IsleKit.DarkWood, bearing);
            Kit.AddBox(glade, new Vector3(plate.x, 0.92f, plate.y), new Vector3(0.34f, 0.5f, 0.04f), IsleKit.Board, bearing);
            // on the plate over the button: three gold dots in a row, joined (this button moves three lamps)
            for (int d = -1; d <= 1; d++)
            {
                Vector2 dot = AtPost(k, PlateFace + 0.003f, d * 0.1f);
                Kit.AddBox(marks, new Vector3(dot.x, 1.3f, dot.y), new Vector3(0.06f, 0.06f, 0.01f), IsleKit.Gold, bearing);
            }
            Vector2 link = AtPost(k, PlateFace + 0.001f);
            Kit.AddBox(marks, new Vector3(link.x, 1.322f, link.y), new Vector3(0.2f, 0.016f, 0.006f), IsleKit.Cream, bearing);

            // the lamp: a round disc on a dark board on the post's inner face, looking at the ring's middle. (A pendant
            // under the arm, Light 2, showed only its shade from across the ring: lit and unlit looked alike by day.)
            Vector2 back = AtPost(k, LampBoardIn), lamp = AtPost(k, LampBoardIn + 0.02f + 0.142f + 0.005f), button = AtPost(k, PlateFace + 0.005f);
            Kit.AddBox(glade, new Vector3(back.x, LampH - 0.3f, back.y), new Vector3(0.6f, 0.6f, 0.04f), IsleKit.DarkWood, bearing);
            b.Part("Light 6", lamp.x, lamp.y, LampH, inward).Named("Ring Lamp " + k)
                .Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", LampRange);
            buttons[k] = b.Part("Button Poke", button.x, button.y, ButtonH, inward).Named("Ring Button " + k)
                .Set("PuzzlePokeButton", "autoEnableSeconds", 0.4f);
        }

        // ---- the Star Ring in the middle: gold bulbs on the snow round a pole with a star, which lights at the win ----
        Kit.AddBox(glade, new Vector3(Star.x, -0.3f, Star.y), new Vector3(0.3f, 5.3f, 0.3f), IsleKit.Cream);
        Kit.AddBox(glade, new Vector3(Star.x, 5f, Star.y), new Vector3(0.08f, 0.35f, 0.08f), IsleKit.Gold);
        Kit.AddStar(glade, new Vector3(Star.x, 5.6f, Star.y), 0.55f, 0.08f, IsleKit.Gold);
        b.MeshPiece("Glade Posts", glade.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        b.MeshPiece("Post Marks", marks.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f);
        Kit.LightString(b, "Star Ring Bulbs", Circle(Star.x, Star.y, 1.5f, 12, 0.12f), 0.02f, 1f, true, false, 31, new[] { IsleKit.Gold });
        // a round lamp at the star's heart on each of its two faces (south and north): the star's half thickness, a
        // Light 6's back depth (0.142) and a gap out from the star's middle
        const float StarLampOut = 0.08f + 0.142f + 0.005f;
        Piece starLamp = b.Part("Light 6", Star.x, Star.y - StarLampOut, 5.6f, 180f).Named("Star Lamp")
            .Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", 12f);
        Piece starLampBack = b.Part("Light 6", Star.x, Star.y + StarLampOut, 5.6f, 0f).Named("Star Lamp Back")
            .Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", 12f);

        // ---- the rule board by the start corner: how many lamps are lit, and the rule as a picture under it ----
        IsleKit.StartCorner(b, -8f, -6f, 0f);
        IsleKit.BoardOnPosts(b, "Rule Board", BoardX, BoardZ, 0f, BoardWide, BoardLow, BoardHigh, IsleKit.Board);
        b.Part("Progress Bar", BoardX, BoardZ + BoardFace + BarBack * BarScale + 0.005f, 2.4f, 0f).Named("Ring Bar Board")
            .Scaled(BarScale, BarScale, BarScale).Set("PuzzleProgressBar", "total", Posts);
        b.MeshPiece("Rule Picture", RulePicture().ToMesh(), bare, false).Transform.position = b.WorldPoint(BoardX, BoardZ);

        // ---- the rule: a button flips its lamp and both neighbours; all seven lit win (LightsOut.cs) ----
        // Start deals a pattern, stop darkens the ring (a won ring stays lit); the script wins the puzzle itself
        Component ring = b.AddCustomScript(b.Empty("Glade Lights Out", Star.x, Star.y, 0.5f), "LightsOut");
        Sdk.Set(ring, "count", Posts);
        Sdk.Set(ring, "lampPrefix", "Ring Lamp ");
        Sdk.Set(ring, "barPrefix", "Ring Bar");
        for (int k = 1; k <= Posts; k++) buttons[k].On("PuzzlePokeButton", "onPressed", ring, "Press", k);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", ring, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", ring, "End");
        // the win lights the star. Lamps start dark, and like the won ring the star stays lit after the stop (the SDK's
        // checks also reject a lamp that the stop sets and the start does not)
        b.RootPiece.On("PuzzleSDK", "onWin", starLamp.Component("PuzzleLight"), "TurnOn");
        b.RootPiece.On("PuzzleSDK", "onWin", starLampBack.Component("PuzzleLight"), "TurnOn");

        // ---- fairy lights (no collisions): from the pole's top out to each lamp's arm, in gold from each arm to the
        // next round the ring (the neighbours a button moves), and round the glade ----
        for (int k = 1; k <= Posts; k++)
        {
            Vector2 here = AtPost(k, LampIn), next = AtPost(k % Posts + 1, LampIn);
            Kit.LightString(b, "Pole String " + k, new List<Vector3> { new Vector3(Star.x, 4.8f, Star.y), new Vector3(here.x, StringH, here.y) },
                0.25f, 0.45f, true, false, 200 + k);
            Kit.LightString(b, "Neighbour String " + k, new List<Vector3> { new Vector3(here.x, StringH, here.y), new Vector3(next.x, StringH, next.y) },
                0.3f, 0.3f, true, false, 220 + k, new[] { IsleKit.Gold });
        }
        Kit.LightRing(b, "Glade Ring", 0f, 10.5f, 39f, 29f, 2.6f, true, 21);
    }

    // ---- helpers ----

    // The bearing of post k (1..7) from the ring's middle, in degrees as a yaw: post 1 due south, the rest clockwise
    // seen from above (south, west, north, east). A part on the post that faces the middle has this yaw + 180.
    static float Bearing(int k) => 180f + (k - 1) * 360f / Posts;

    // A point 'inward' metres from post k's middle towards the ring's middle and 'across' metres to the side
    static Vector2 AtPost(int k, float inward, float across = 0f) => Star + IsleKit.Turn(across, RingRadius - inward, Bearing(k));

    // A closed ring of n posts (n + 1 points, the last on the first) at radius r, 'h' above the ground
    static List<Vector3> Circle(float cx, float cz, float r, int n, float h)
    {
        var points = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            float a = 2f * Mathf.PI * (i % n) / n;
            points.Add(new Vector3(cx + Mathf.Cos(a) * r, h, cz + Mathf.Sin(a) * r));
        }
        return points;
    }

    // A patch of paint on the rule board's face, in the frame of a piece standing at the board's foot (the board
    // faces +z): u across, v up from the ground, standing 'proud' of the face. Patches that overlap differ in 'proud'.
    static void Paint(MeshBuilder mb, float u0, float u1, float v0, float v1, Color colour, float proud)
        => Kit.AddBox(mb, new Vector3((u0 + u1) / 2f, v0, BoardFace + proud / 2f - 0.001f), new Vector3(u1 - u0, v1 - v0, proud + 0.002f), colour);

    // The rule as a picture: three lamps in a row on their posts, hung from one string, and the middle post's red
    // button wired in red to all three
    static MeshBuilder RulePicture()
    {
        var mb = new MeshBuilder();
        for (int i = -1; i <= 1; i++)
        {
            float u = i * 0.9f;
            Paint(mb, u - 0.035f, u + 0.035f, 0.9f, 1.7f, IsleKit.Cream, 0.006f);                      // the post
            Paint(mb, u - 0.14f, u + 0.14f, 1.7f, 1.98f, IsleKit.Gold, 0.012f);                        // its lamp
            Paint(mb, u - 0.015f, u + 0.015f, 1.98f, 2.05f, IsleKit.Cream, 0.006f);                    // the lamp's hanger
            if (i != 0) Paint(mb, u - 0.07f, u + 0.07f, 1.13f, 1.27f, IsleKit.Stone, 0.01f);           // a neighbour's own button
        }
        Paint(mb, -0.9f, 0.9f, 2.05f, 2.08f, IsleKit.Cream, 0.006f);                                   // the string
        Paint(mb, -0.1f, 0.1f, 1.1f, 1.3f, IsleKit.Red, 0.014f);                                       // the middle button
        Paint(mb, -0.025f, 0.025f, 1.3f, 1.7f, IsleKit.Red, 0.011f);                                   // up to its own lamp
        Paint(mb, -0.6f, 0.6f, 1.46f, 1.51f, IsleKit.Red, 0.01f);                                      // out to both sides
        foreach (float side in new[] { -1f, 1f })
        {
            Paint(mb, side * 0.6f - 0.025f, side * 0.6f + 0.025f, 1.46f, 1.865f, IsleKit.Red, 0.011f);                              // up
            Paint(mb, Mathf.Min(side * 0.6f, side * 0.76f), Mathf.Max(side * 0.6f, side * 0.76f), 1.815f, 1.865f, IsleKit.Red, 0.01f);   // into the lamp
        }
        return mb;
    }
}
