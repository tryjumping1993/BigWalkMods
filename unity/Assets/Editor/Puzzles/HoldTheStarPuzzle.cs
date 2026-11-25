// Hold the Star: hold the star alight. A short, gentle puzzle for a group, with no clock and nothing to get wrong.
//
// A great snowy fir with a gold star on top stands in a clearing. Round it stand star posts, 9 m from the trunk and
// 120 degrees apart: a stout timber post with a gold star on top and an N-Hold button on the side facing the tree.
// While every star button is held at once, the star on the tree blazes and the glass box at the tree's foot stands
// open; one more player lifts the gourd out of it, and that completes the puzzle (the Box holds the gourd: there is
// no Gourd Vice). Let go and the star goes dark and the box shuts again. The first time the star blazes the confetti
// fires. Nobody is shut in and everyone can talk.
//
// Lobby sizes: one star post for two players, two for three, three for four, so there is always one free pair of
// hands. The posts are 15.6 m from each other and 7.8 m or more from the box, so nobody holds two buttons, or holds
// one and reaches the gourd. Posts 2 and 3 (post, star, lights and button) are in IsleKit.Lobby groups: an N-Hold
// button counts only the switched-on buttons of its group ("Star"), so the hidden ones drop out. Post 1's button is
// always there and carries the wiring (every button of a group fires its events, so only one is wired).
//
// The start switch (at the start corner, with the pillow and the confetti) only lights its live lamp: the star
// buttons work whether it is on or not. No custom code: three N-Hold buttons, two lamps and the Box.
// (Before the user's rules of 2026-11-24: a Reader shut in a soundproof Star Chamber at the tree's foot called
// colours over a one-way intercom to holders at stations 22 m out, ten timed rounds in a row, then a six-second
// finale on the star buttons.)
//
// Coordinates: local metres from the tree, x east, z north (+z forward), heights above the ground. The start corner
// is to the south. Parts face their local +z; yaw 90 faces +x, -90 faces -x, 180 faces -z.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class HoldTheStarPuzzle : PuzzleDef
{
    public override string Name => "HoldTheStar";
    public override Vector2 Footprint => new Vector2(52f, 50f);
    public override Vector2? FixedSite => new Vector2(-3003f, -1548f);      // layout-final: a coastal flat at the west end, by Cirque Gate and M13

    const float StarHeight = 10.6f;       // the middle of the tree's star
    const float StarLampOut = 0.172f;     // a star lamp's pivot from the star's middle: a Light 6's back is 0.142 m behind its pivot, sunk into the star
    const float StarLampRange = 12f;      // the star lamps' light reaches down the crown (tune in the game)
    const float PostOut = 9f;             // a star post's middle from the tree's trunk
    const float PostTall = 1.4f, PostWidth = 0.3f;
    const float ButtonHeight = 1.1f;
    const float ButtonTurn = 0f;          // 0: a button is on its post's side facing the tree; 180 puts it on the outer side, so its holder faces the tree
    const float BoxOut = 4.5f;            // the box, south of the trunk: clear of the lowest boughs, in line between posts 1 and 2 (7.8 m from each)
    const float PlinthHeight = 0.45f;

    // Where the posts stand, as bearings from the tree (degrees clockwise from +z): post 1 south-east, post 2
    // south-west (either side of the box), post 3 north, behind the tree
    static readonly float[] PostBearing = { 120f, 240f, 0f };
    static readonly Color LampColor = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one
    static readonly Color[] StarBulbs = { Kit.BulbColors[2], Kit.BulbColors[4] };      // gold and warm white

    PuzzleBuilder b;
    Material snowy, bare;

    // The four corners of a square of fairy lights 'half' out from (cx, cz), turned by 'yaw', the wire 'height' up,
    // ending where it began
    static List<Vector3> Square(float cx, float cz, float half, float yaw, float height)
    {
        var points = new List<Vector3>();
        foreach (var (x, z) in new[] { (-half, -half), (half, -half), (half, half), (-half, half), (-half, -half) })
        {
            Vector2 p = IsleKit.Turn(x, z, yaw);
            points.Add(new Vector3(cx + p.x, height, cz + p.y));
        }
        return points;
    }

    // One star post, PostOut from the tree on 'bearing': a stout timber post on a stone foot with a cap that takes the
    // snow, a gold star on a stem over it (its face to the tree), a collar of fairy lights round the cap so the post
    // shows at night, and the N-Hold button. All of it goes under 'group' (a lobby group) if there is one. Returns the
    // button.
    Piece StarPost(int number, float bearing, Piece group)
    {
        string name = "Star Post " + number;
        Vector2 at = IsleKit.Turn(0f, PostOut, bearing);
        Vector3 foot = b.WorldPoint(at.x, at.y);
        Quaternion turn = b.Root.transform.rotation * Quaternion.Euler(0f, bearing, 0f);      // the post's own +z looks away from the tree

        var timber = new MeshBuilder();
        Kit.AddBox(timber, new Vector3(0f, -0.3f, 0f), new Vector3(0.46f, 0.42f, 0.46f), IsleKit.Stone);                     // the foot, top 0.12
        Kit.AddBox(timber, new Vector3(0f, 0.1f, 0f), new Vector3(PostWidth, PostTall - 0.1f, PostWidth), IsleKit.Wood);
        Kit.AddBox(timber, new Vector3(0f, PostTall, 0f), new Vector3(0.42f, 0.06f, 0.42f), IsleKit.DarkWood);               // the cap
        Kit.AddBox(timber, new Vector3(0f, PostTall + 0.06f, 0f), new Vector3(0.05f, 0.28f, 0.05f), IsleKit.DarkWood);       // the star's stem, up to its lower notch
        Piece post = b.MeshPiece(name, timber.ToMesh(), snowy);
        post.Transform.SetPositionAndRotation(foot, turn);

        var gold = new MeshBuilder();
        Kit.AddStar(gold, new Vector3(0f, PostTall + 0.46f, 0f), 0.3f, 0.06f, IsleKit.Gold);
        Piece star = b.MeshPiece(name + " Star", gold.ToMesh(), bare, false);
        star.Transform.SetPositionAndRotation(foot, turn);

        // (the wire runs 4 cm outside the cap; its bulbs hang clear of the button's top)
        Piece lights = Kit.LightString(b, name + " Lights", Square(at.x, at.y, 0.25f, bearing, PostTall + 0.02f), 0.05f, 0.17f, true, false, 60 + number, StarBulbs);

        // the button's back on the post's face (1 cm proud), looking the way it faces
        float face = bearing + 180f + ButtonTurn;
        Vector2 press = at + IsleKit.Turn(0f, PostWidth / 2f + 0.01f, face);
        Piece button = b.Part("Button N Hold", press.x, press.y, ButtonHeight, face).Named("Star Button " + number)
            .Set("PuzzleButtonNHold", "group", "Star");

        if (group != null) { post.Under(group); star.Under(group); lights.Under(group); button.Under(group); }
        return button;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        snowy = IsleKit.Snowy(b);
        bare = IsleKit.Bare(b);
        b.ClearGround(52f, 50f, 0f, 0f);
        b.DebugTeleport(0f, -12f);
        // no jetpacks over the puzzle (Winter Walk): the tree, the posts and the whole clearing
        IsleKit.CalmAir(b, "Hold The Star", 0f, 0f, new Vector3(64f, 120f, 62f), 0f, -10f);

        // ---- the tree and its star ----
        var tree = new MeshBuilder();
        Kit.AddFir(tree, Vector3.zero, 10f, 4242, 1.2f);
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(true), IsleKit.Fir(b), false);
        pine.Transform.position = b.WorldPoint(0f, 0f);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * 2f; trunk.radius = 0.6f; trunk.height = 4f;
        var topper = new MeshBuilder();
        Kit.AddStar(topper, new Vector3(0f, StarHeight, 0f), 0.75f, 0.12f, IsleKit.Gold);
        b.MeshPiece("Star", topper.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f);

        // the star's lamps: one at the heart of each face of it (the star stands across z, so one looks south over
        // the box and the start corner, one north), dark until every star button is held
        var starLamps = new List<Component>();
        foreach (float side in new[] { -1f, 1f })
        {
            Piece lamp = b.Part("Light 6", 0f, side * StarLampOut, StarHeight + 0.017f, side < 0f ? 180f : 0f)
                .Named(side < 0f ? "Star Lamp South" : "Star Lamp North")
                .Set("PuzzleLight", "color", LampColor).Set("PuzzleLight", "range", StarLampRange);
            starLamps.Add(lamp.Component("PuzzleLight"));
        }

        // ---- the gourd's box at the tree's foot, on a stone plinth; its lid hinges on the tree's side, so the gourd
        //      is lifted out from the south. A rim of fairy lights round the plinth, so it shows at night ----
        var plinth = new MeshBuilder();
        Kit.AddBox(plinth, new Vector3(0f, -0.3f, 0f), new Vector3(1.3f, PlinthHeight + 0.3f, 1.3f), IsleKit.Stone);
        b.MeshPiece("Plinth", plinth.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, -BoxOut);
        Piece box = b.Part("Box", 0f, -BoxOut, PlinthHeight, 180f).Named("Box");
        Component boxC = box.Component("PuzzleBox");
        Kit.LightString(b, "Box Lights", Square(0f, -BoxOut, 0.69f, 0f, PlinthHeight + 0.04f), 0.05f, 0.23f, true, false, 55, StarBulbs);

        // ---- the star posts: post 1 always, post 2 for three and four players, post 3 for four ----
        Piece second = IsleKit.Lobby(b, "Second Star Post", false, true, true);
        Piece third = IsleKit.Lobby(b, "Third Star Post", false, false, true);
        Piece button = StarPost(1, PostBearing[0], null);
        StarPost(2, PostBearing[1], second);
        StarPost(3, PostBearing[2], third);

        IsleKit.StartCorner(b, 4.5f, -14.5f, 0f, false);

        // ---- wiring: post 1's button speaks for the group ----
        button.On("PuzzleButtonNHold", "onAllHeld", boxC, "Open");
        button.On("PuzzleButtonNHold", "onAllReleased", boxC, "Close");
        foreach (Component lamp in starLamps)
        {
            button.On("PuzzleButtonNHold", "onAllHeld", lamp, "TurnOn");
            button.On("PuzzleButtonNHold", "onAllReleased", lamp, "TurnOff");
        }
        // the first time the star blazes counts as the win (once: the confetti); taking the gourd out is what
        // completes the puzzle. The companion's do=win shows the same: the box open and the star lit
        button.On("PuzzleButtonNHold", "onAllHeld", b.Puzzle, "Win");
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onWin", boxC, "Open");
        foreach (Component lamp in starLamps) root.On("PuzzleSDK", "onWin", lamp, "TurnOn");

        // ---- lights: a ring round the clearing ----
        Kit.LightRing(b, "Site Lights", 0f, 0f, 50f, 46f, 2.7f, true, 51);
    }
}
