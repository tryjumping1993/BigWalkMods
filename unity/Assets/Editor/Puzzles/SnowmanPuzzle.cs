// Build Three Snowmen. A round snow fort with one gateway holds three snowman plots: low snow bowls, each with a board
// on a post behind it that shows a snowman's outline as three white discs, a lamp in the middle of each, under a hat
// and a scarf of the plot's own colour. Nine snowballs, three large, three medium and three small, lie about the
// place: four inside the fort and five on the snow round it, among the drifts. Most are in plain sight; one is tucked
// between a drift and the wall, and two sit up on low snow ledges of the wall, one inside and one round the back.
//
// Find the balls and carry them in. A plot wants a whole snowman: one large, one medium and one small ball set down in
// its bowl, side by side or stacked. The three lamps on its board show which sizes it has, big at the bottom to small
// at the top, and a ball taken out again puts its lamp out. Nine lamps lit win the gourd. There is no clock and
// nothing to get wrong: a ball in the wrong plot is only a ball to move. One player can finish it; a group splits up
// to search and carry. The same in every lobby size.
// (Before the user's rules of 2026-11-24 this was the Snowman Contest: six plots under split-flap faces, a Judge shut
// in a windowless box who saw which face was lit, and 21 seconds to move all three balls there, eight times running.)
//
// Under the hood: three item-counting Presence Sensors over each bowl, one a size, each taking only that size's three
// balls, wired to Tally (PuzzleScripts), which lights "Plot Lamp 1..9", fills the fort board's bar and wins at nine.
// ItemReturn (PuzzleScripts) lays a ball that comes to rest on top of the wall, out of anyone's reach, back in the
// middle of the fort.
//
// Coordinates: local metres from the root, which is the fort's centre: x east, z north (+z forward). Heights are above
// the ground. Each structure is built on the ground under its own anchor (the fort's centre, each plot, each post),
// and the SDK parts on a structure are placed from that same level (onGround: false), so a few centimetres of
// unevenness in the levelled site never put a part off its board. Yaw: 0 faces +z, 90 +x, -90 -x, 180 -z.
using System;
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowmanPuzzle : PuzzleDef
{
    public override string Name => "Snowman";
    public override Vector2 Footprint => new Vector2(26f, 44f);
    // layout-final: a village green on the High Shelf, 64 m north of the summit spur's High Shelf station (the fort's
    // gateway faces south, towards it)
    public override Vector2? FixedSite => new Vector2(-2553f, -1530f);
    public override float Yaw => 0f;

    // ---- the fort ----
    const float FortIn = 11.0f, FortOut = 11.8f, FortHigh = 3.0f;
    const float LedgeHigh = 0.8f, LedgeTop = 0.85f;     // the wall's two ledges and the snow on them: a jump (1 m) reaches a ball there

    // ---- the plots ----
    const float PlotRadius = 7.0f;                      // their middles, from the fort's
    const float PostRadius = 8.95f;                     // their posts, behind them
    static readonly float[] PlotAngle = { 180f, 60f, 300f };      // plots 1 to 3: west, north-east, south-east (math angles, from +x towards +z)
    static readonly Color[] PlotColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };      // each plot's hat band and scarf
    // A plot's sensors reach from the bowl's floor to over the head of a stacked snowman (2.08 m), so its top ball counts
    const float SensorHigh = 2.4f;
    // A plot's board, on the fort's side of its post: 8 cm thick, with the outline's lowest point OutlineFoot up
    const float BoardThick = 0.08f, BoardWide = 1.08f, BoardFoot = 0.22f, BoardTop = 2.36f, OutlineFoot = 0.32f;
    // Part depths from part-metrics.tsv: how far a lamp's pivot stands in front of its back; a Light 6's middle is
    // 1.7 cm under its pivot
    const float DepthLight6 = 0.142f, DropLight6 = 0.017f, DepthLight8 = 0.1f;
    static readonly float[] LampRange = { 0.9f, 0.65f, 0.5f };      // a lit lamp lights its own disc, not its neighbours'

    // ---- the snowballs ----
    static readonly float[] Radius = { 0.46f, 0.34f, 0.24f };
    static readonly string[] SizeName = { "Large", "Medium", "Small" };
    // Where the nine start, a row for each size: local x and z, and in y how high what they lie on stands above the
    // fort's ground (0: the snow itself). None is within 5 m of a plot's middle (CheckStarts).
    static readonly Vector3[][] BallStart =
    {
        new[]
        {
            new Vector3(1.0f, 0f, -0.8f),               // by the lantern in the middle of the fort
            new Vector3(-9.0f, 0f, -15.0f),             // outside, south-west of the gateway, in the open
            new Vector3(11.5f, 0f, 14.5f),              // outside, north-east, between two drifts
        },
        new[]
        {
            new Vector3(-5.2f, 0f, 9.0f),               // inside, north-west, in the gap between a tall drift and the wall
            new Vector3(-4.6f, 0f, -8.0f),              // inside, south-west, by the wall
            new Vector3(-16.0f, 0f, 5.0f),              // outside, west, beside a drift
        },
        new[]
        {
            new Vector3(10.55f, LedgeTop, 0.6f),        // on the ledge inside the east wall
            new Vector3(0.7f, LedgeTop, 12.25f),        // on the ledge outside the north wall, round the back
            new Vector3(13.5f, 0f, -11.0f),             // outside, south-east, beside a drift
        },
    };
    // Drifts: local x, z, radius, height. The first hides a medium ball from the middle of the fort; the next two are
    // inside too, the rest outside.
    static readonly Vector4[] Drifts =
    {
        new Vector4(-4.05f, 7.0f, 1.4f, 1.0f), new Vector4(-6.3f, -7.0f, 1.0f, 0.5f), new Vector4(8.3f, 3.6f, 1.1f, 0.6f),
        new Vector4(-11.2f, -16.3f, 1.3f, 0.7f), new Vector4(9.0f, 15.2f, 1.4f, 0.9f), new Vector4(13.6f, 16.0f, 1.2f, 0.7f),
        new Vector4(-14.2f, 6.3f, 1.2f, 0.8f), new Vector4(15.2f, -12.2f, 1.0f, 0.55f), new Vector4(-5.5f, 15.5f, 1.5f, 0.9f),
    };
    // Lanterns, lit at night, so the balls can be found in the dark: one in the middle of the fort and one by each of
    // the four balls on the snow outside (the ledge round the back is under the fort's own lights)
    static readonly Vector2[] Lanterns =
    {
        new Vector2(0f, 0f), new Vector2(-7.0f, -17.0f), new Vector2(10.0f, 17.0f), new Vector2(-17.5f, 3.0f), new Vector2(15.5f, -9.0f),
    };

    static readonly Color SnowWhite = new Color(0.93f, 0.95f, 0.98f);
    static readonly Color Coal = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color Carrot = new Color(0.95f, 0.45f, 0.1f);
    static readonly Color LampWhite = new Color(1f, 0.92f, 0.75f);      // near white: a lit lamp is a bright disc, an unlit one a dull one

    PuzzleBuilder b;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        CheckStarts();
        b.ClearGround(26f, 44f);
        b.DebugTeleport(-5f, -16f);
        // No jetpacks over the fort: nobody flies a ball in over its wall
        IsleKit.CalmAir(b, "Snowman", 0f, -4.5f, new Vector3(40f, 120f, 52f), 0f, -10f);

        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Material ballSnow = b.ShaderMaterial("BWM/Snow", "Snow");
        float fortBase = GroundAt(0f, 0f);
        var paint = new MeshBuilder();      // painted, so no snow lies on it: the outlines, hats and scarves, the fort board's picture

        // ================= the fort =================
        BuildFort(snowy, paint, fortBase);

        // ================= the nine snowballs =================
        var balls = new UnityEngine.Object[3][];
        for (int size = 0; size < 3; size++)
        {
            balls[size] = new UnityEngine.Object[3];
            for (int n = 0; n < 3; n++)
            {
                Vector3 at = BallStart[size][n];
                bool ledge = at.y > 0f;
                // its middle a radius (and 2 cm) above what it lies on: the ground under it, or a ledge's snow
                float height = Radius[size] + 0.02f + (ledge ? fortBase + at.y : 0f);
                Piece ball = b.Item("Snowball " + SizeName[size] + " " + (n + 1), Kit.AsLooseSnow(Kit.Ball(Radius[size], 10, 14, 0.12f, size + 1 + 3 * n)), ballSnow,
                    at.x, at.z, height, 3f - size, !ledge);
                Component item = ball.Component("PuzzleItem");
                Sdk.Set(item, "savesOnReload", false);      // a held ball is never carried off to the spawn area by a save
                Sdk.Set(item, "sounds", 3);                 // Football: rolls with a sound
                balls[size][n] = item;
            }
        }

        // ================= the tally: nine targets, a plot's large, medium and small; all nine marked wins =================
        // Tally finds "Plot Lamp 1..9" and every "Tally Bar..." by name. A stop clears it (a stop also runs when the
        // puzzle spawns; the start is not wired, since the SDK's checks reject a start and a stop that do the same thing)
        Component tally = b.AddCustomScript(b.Empty("Snowman Tally", 0f, 0f, 0.5f), "Tally");
        Sdk.Set(tally, "count", 9);
        Sdk.Set(tally, "lampPrefix", "Plot Lamp ");
        Sdk.Set(tally, "barPrefix", "Tally Bar");

        // ================= the three plots =================
        var bowls = new MeshBuilder();
        var posts = new MeshBuilder();
        for (int p = 0; p < PlotAngle.Length; p++)
        {
            float theta = PlotAngle[p] * Mathf.Deg2Rad, c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            Vector2 plot = new Vector2(PlotRadius * c, PlotRadius * s);
            Vector3 inward = new Vector3(-c, 0f, -s);               // from the post to the fort's middle: the way the board faces
            float yaw = Mathf.Atan2(-c, -s) * Mathf.Rad2Deg;

            // the bowl: a floor disc 2.2 m across, 3 cm proud, inside a 12-sided rim (inner 2.0 m, 0.25 m thick, 0.20 m high)
            float pb = GroundAt(plot.x, plot.y);
            Kit.AddDisc(bowls, new Vector3(plot.x, pb - 0.1f, plot.y), 1.1f, 0.13f, new Color(0.84f, 0.88f, 0.95f));
            Ring(bowls, new Vector3(plot.x, 0f, plot.y), pb - 0.1f, pb + 0.20f, 1.0f, 1.25f, 12, 15f, null, SnowWhite);

            // the post behind it and the dark board on the post's front, at 'radius' from the fort's middle
            float hb = GroundAt(PostRadius * c, PostRadius * s);
            Vector3 At(float radius, float height) => new Vector3(radius * c, hb + height, radius * s);
            float boardFace = PostRadius - 0.1f - BoardThick;
            Kit.AddBox(posts, At(PostRadius, -0.5f), new Vector3(0.2f, BoardTop + 0.5f, 0.2f), IsleKit.Wood, yaw);
            Kit.AddBox(posts, At(boardFace + BoardThick / 2f, BoardFoot), new Vector3(BoardWide, BoardTop - BoardFoot, BoardThick), IsleKit.Board, yaw);

            for (int size = 0; size < 3; size++)
            {
                int k = p * 3 + size + 1;      // Tally's target: plot 1 has 1 (large), 2 (medium), 3 (small); plot 2 has 4 to 6; plot 3 has 7 to 9

                // the outline's disc of this ball's own size, and the lamp in its middle
                float y = DiscHeight(size), proud = 0.02f + 0.005f * size;
                Roundel(paint, At(boardFace, y), inward, Radius[size], proud, SnowWhite, 20);
                float lampBack = boardFace - proud - 0.004f;
                Piece lamp = size == 0
                    ? FP("Light 6", (lampBack - DepthLight6) * c, (lampBack - DepthLight6) * s, hb + y + DropLight6, yaw)
                    : FP("Light 8", (lampBack - DepthLight8) * c, (lampBack - DepthLight8) * s, hb + y, yaw);
                lamp.Named("Plot Lamp " + k).Set("PuzzleLight", "color", LampWhite).Set("PuzzleLight", "range", LampRange[size]);

                // the sensor for this size: the bowl's own width, pressed while one or more of that size's balls is in it
                IsleKit.Sensor(b, "Plot " + (p + 1) + " " + SizeName[size], plot.x, plot.y, new Vector3(2.0f, SensorHigh, 2.0f))
                    .Set("PuzzlePresenceSensor", "type", 2)                      // SpecificItem
                    .Set("PuzzlePresenceSensor", "requiredPlayers", 1)
                    .Add("PuzzlePresenceSensor", "acceptedCustom", balls[size])
                    .On("PuzzlePresenceSensor", "onPressed", tally, "Hit", k)
                    .On("PuzzlePresenceSensor", "onReleased", tally, "Unhit", k);
            }

            // a hat on the board's top, and a scarf across the outline's neck with its tail down one side
            Vector3 hatFoot = At(PostRadius - 0.09f, BoardTop);
            Kit.AddDisc(paint, hatFoot, 0.30f, 0.04f, Coal, 12);
            Kit.AddDisc(paint, hatFoot + Vector3.up * 0.04f, 0.19f, 0.28f, Coal, 12);
            Kit.AddDisc(paint, hatFoot + Vector3.up * 0.06f, 0.20f, 0.07f, PlotColour[p], 12);
            float neck = DiscHeight(2) - Radius[2] * 0.95f;
            Vector3 across = Vector3.Cross(Vector3.up, inward);
            Kit.AddBox(paint, At(boardFace - 0.02f, neck - 0.045f), new Vector3(0.5f, 0.09f, 0.06f), PlotColour[p], yaw);
            Kit.AddBox(paint, At(boardFace - 0.02f, neck - 0.33f) + across * 0.23f, new Vector3(0.1f, 0.3f, 0.06f), PlotColour[p], yaw);
        }
        Place("Snowman Plots", bowls, snowy, 0f);
        Place("Snowman Posts", posts, snowy, 0f);

        // ---- the fort board on the north wall: the tally's bar over the picture, a start knob and its live lamp ----
        FP("Progress Bar", 0f, 10.80f, fortBase + 2.4f, 180f).Named("Tally Bar Fort").Scaled(0.6f, 1f, 1f)
            .Set("PuzzleProgressBar", "style", 0).Set("PuzzleProgressBar", "total", 9);
        FP("Switch Start", 1.15f, 10.83f, fortBase + 1.25f, 180f).Named("Fort Knob");
        FP("Live Light", 1.15f, 10.895f, fortBase + 1.42f, 180f).Named("Fort Live");

        // ================= start corner and the Champion Snowman, outside the gateway =================
        IsleKit.StartCorner(b, 6.5f, -18.0f, 180f);
        Piece trimmings = BuildChampion(snowy, bare, 10.5f, -17.0f);

        // ================= drifts and lanterns =================
        for (int i = 0; i < Drifts.Length; i++)
        {
            Vector4 d = Drifts[i];
            b.MeshPiece("Fort Drift " + (i + 1), Kit.Mound(d.z, d.w, 460 + i), snowy).Transform.position = b.WorldPoint(d.x, d.y, -0.05f);
        }
        var lanterns = new MeshBuilder();
        var glow = new MeshBuilder();
        for (int i = 0; i < Lanterns.Length; i++)
        {
            Vector2 at = Lanterns[i];
            Kit.AddLantern(lanterns, glow, new Vector3(at.x, GroundAt(at.x, at.y), at.y), 30 + i);
            b.Empty("Room Light Night " + (i + 1) + " FFCB8C 8.0 1.3", at.x, at.y, 2.18f);      // Winter Walk's lamp in its glass
        }
        Place("Fort Lanterns", lanterns, snowy, 0f);
        Place("Fort Lantern Glow", glow, b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), 0f, false);
        Place("Snowman Paint", paint, bare, 0f, false);

        // ================= a ball thrown onto the wall's top comes back =================
        // The top is 3 m up and nobody can climb it, and the game pins a ball where it comes to rest. ItemReturn lays
        // any ball that has lain 2 s in one of these boxes (one over each of the wall's sixteen sides, the gateway's
        // arch among them: 5.2 m along the wall, 2 m through it, from just under its top to 1.6 m over it) back on the
        // snow in the middle of the fort.
        for (int k = 0; k < 16; k++)
        {
            float a = (90f + 22.5f * k) * Mathf.Deg2Rad, mid = (FortIn + FortOut) / 2f;
            Piece box = b.Empty("Wall Top " + (k + 1), mid * Mathf.Cos(a), mid * Mathf.Sin(a), fortBase + FortHigh, false);
            box.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Rad2Deg, 0f);      // its z through the wall
        }
        Piece ballHome = b.Empty("Ball Home", -1.4f, 0.6f, 0.7f);
        Component back = b.AddCustomScript(b.Empty("Ball Return", 0f, 0f, 1.5f), "ItemReturn");
        Sdk.Set(back, "itemPrefix", "Snowball ");
        Sdk.Set(back, "boxPrefix", "Wall Top ");
        Sdk.Set(back, "boxMin", new Vector3(-2.6f, -0.2f, -1.0f));
        Sdk.Set(back, "boxMax", new Vector3(2.6f, 1.6f, 1.0f));
        Sdk.Set(back, "home", ballHome.Transform);
        Sdk.Set(back, "spread", 0.6f);
        Sdk.Set(back, "delay", 2f);

        // ...and so does a ball that ends up off the site (thrown over the cliff, carried off and dropped): all nine are
        // needed. Five big boxes, each a unit cube scaled: everything more than 8 m under the fort, and everything
        // further than 45 m from its middle. A ball in a player's hands is never taken (ItemReturn asks).
        void Lost(string side, Vector3 centre, Vector3 size)
            => b.Empty("Lost Box " + side, centre.x, centre.z, centre.y, false).Transform.localScale = size;
        Lost("Below", new Vector3(0f, -208f, 0f), new Vector3(900f, 400f, 900f));
        Lost("West", new Vector3(-245f, 92f, 0f), new Vector3(400f, 200f, 900f));
        Lost("East", new Vector3(245f, 92f, 0f), new Vector3(400f, 200f, 900f));
        Lost("South", new Vector3(0f, 92f, -245f), new Vector3(900f, 200f, 400f));
        Lost("North", new Vector3(0f, 92f, 245f), new Vector3(900f, 200f, 400f));
        Component lost = b.AddCustomScript(b.Empty("Lost Return", 0f, 0f, 2.5f), "ItemReturn");
        Sdk.Set(lost, "itemPrefix", "Snowball ");
        Sdk.Set(lost, "boxPrefix", "Lost Box ");
        Sdk.Set(lost, "boxMin", new Vector3(-0.5f, -0.5f, -0.5f));
        Sdk.Set(lost, "boxMax", new Vector3(0.5f, 0.5f, 0.5f));
        Sdk.Set(lost, "home", ballHome.Transform);
        Sdk.Set(lost, "spread", 0.6f);
        Sdk.Set(lost, "delay", 5f);

        // ================= events =================
        Piece root = b.RootPiece;
        root.On("PuzzleSDK", "onPuzzleStop", tally, "Clear");
        // balls already lying in a bowl when the puzzle starts count at once (every plot has Hit and Unhit)
        root.On("PuzzleSDK", "onPuzzleStart", tally, "Begin");
        root.On("PuzzleSDK", "onWin", trimmings.Go, "SetActive", true);

        // ================= Christmas lights: round the fort's top, and behind the start corner =================
        var fortPosts = new List<Vector3>();
        for (int k = 0; k <= 15; k++)
        {
            float a = (-78.75f + 22.5f * k) * Mathf.Deg2Rad;      // from the gateway's east end anticlockwise to its west end
            float r = 11.4f / Mathf.Cos(11.25f * Mathf.Deg2Rad);
            fortPosts.Add(new Vector3(r * Mathf.Cos(a), fortBase + FortHigh + 0.35f, r * Mathf.Sin(a)));
        }
        Kit.LightString(b, "Fort Lights", fortPosts, 0.15f, 0.5f, false, false, 181);
        Kit.LightString(b, "Corner Lights", new List<Vector3>
        {
            new Vector3(3.4f, 2.4f, -16.4f), new Vector3(7.8f, 2.4f, -15.8f), new Vector3(12.3f, 2.4f, -16.4f),
        }, 0.3f, 0.5f, true, true, 182);
    }

    // ---- checks ----

    // No ball may start within 5 m of a plot's middle
    static void CheckStarts()
    {
        for (int size = 0; size < 3; size++)
            for (int n = 0; n < 3; n++)
                for (int p = 0; p < PlotAngle.Length; p++)
                {
                    float a = PlotAngle[p] * Mathf.Deg2Rad;
                    Vector3 at = BallStart[size][n];
                    float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(PlotRadius * Mathf.Cos(a), PlotRadius * Mathf.Sin(a)));
                    if (d < 5f)
                        throw new Exception("Snowman: Snowball " + SizeName[size] + " " + (n + 1) + " starts " + d.ToString("0.0") + " m from plot " + (p + 1) + "; it must be 5 m or more");
                }
    }

    // ---- structures ----

    // The fort: a 16-sided ring wall (0.8 m thick, 3 m high, sheer both sides) with its south segment left out as an
    // open gateway, a snow cap, the arch over the gateway, two low snow ledges (inside against the east side, outside
    // against the north side), and the dark board on the north wall's inner face with its picture: three snowmen,
    // each in its plot's scarf
    void BuildFort(Material snowy, MeshBuilder paint, float fortBase)
    {
        var mb = new MeshBuilder();
        Func<int, bool> gateway = k => k == 8;          // segment centres from 90 deg: k = 8 is 270, the south
        Ring(mb, Vector3.zero, fortBase - 0.8f, fortBase + FortHigh, FortIn, FortOut, 16, 90f, gateway, IsleKit.Cream);
        Ring(mb, Vector3.zero, fortBase + FortHigh, fortBase + FortHigh + 0.12f, FortIn - 0.06f, FortOut + 0.06f, 16, 90f, gateway, SnowWhite);
        // the arch over the gateway: from 2.35 m to the wall's top, through the wall's depth
        Block(mb, -2.5f, 2.5f, fortBase + 2.35f, fortBase + FortHigh, -FortOut, -FortIn, IsleKit.Cream);
        Block(mb, -2.56f, 2.56f, fortBase + FortHigh, fortBase + FortHigh + 0.13f, -FortOut - 0.06f, -FortIn + 0.06f, SnowWhite);      // 1 cm over the ring's cap: no coplanar tops
        // the ledges: 3.4 m long and 0.9 m deep, built 0.2 m into the wall, with 5 cm of snow on top
        Block(mb, FortIn - 0.9f, FortIn + 0.2f, fortBase - 0.8f, fortBase + LedgeHigh, -1.7f, 1.7f, IsleKit.Cream);
        Block(mb, FortIn - 0.93f, FortIn + 0.2f, fortBase + LedgeHigh, fortBase + LedgeTop, -1.73f, 1.73f, SnowWhite);
        Block(mb, -1.7f, 1.7f, fortBase - 0.8f, fortBase + LedgeHigh, FortOut - 0.2f, FortOut + 0.9f, IsleKit.Cream);
        Block(mb, -1.73f, 1.73f, fortBase + LedgeHigh, fortBase + LedgeTop, FortOut - 0.2f, FortOut + 0.93f, SnowWhite);
        // the fort board: 3.0 x 1.9 m, from 1.0 to 2.9 m, on the north segment's inner face (z 11.0)
        Block(mb, -1.5f, 1.5f, fortBase + 1.0f, fortBase + 2.9f, 10.9f, FortIn, IsleKit.Board);
        Place("Fort Wall", mb, snowy, 0f);

        float[] r = { 0.15f, 0.11f, 0.08f };
        for (int p = 0; p < PlotColour.Length; p++)
        {
            float x = -0.8f + 0.6f * p, y = fortBase + 1.12f;
            for (int i = 0; i < 3; i++)
            {
                y += i == 0 ? r[0] : (r[i - 1] + r[i]) * 0.9f;
                Roundel(paint, new Vector3(x, y, 10.9f), Vector3.back, r[i], 0.012f + 0.004f * i, SnowWhite, 14);
            }
            float neck = y - r[2] * 0.95f;
            Block(paint, x - 0.1f, x + 0.1f, neck - 0.02f, neck + 0.02f, 10.87f, 10.91f, PlotColour[p]);
        }
    }

    // The Champion Snowman by the start corner: three stacked snow spheres on a plinth (always there), and its
    // trimmings (hat, band, scarf, carrot, coal, stick arms) under "Trimmings", shown only by a win. It faces south.
    Piece BuildChampion(Material snowy, Material bare, float cx, float cz)
    {
        float g = GroundAt(cx, cz);
        float c1 = 0.3f + 0.6f, c2 = c1 + (0.6f + 0.45f) * 0.85f, c3 = c2 + (0.45f + 0.32f) * 0.85f;
        var body = new MeshBuilder();
        Kit.AddDisc(body, new Vector3(cx, g - 0.3f, cz), 0.9f, 0.6f, IsleKit.Stone);
        Kit.AddBall(body, new Vector3(cx, g + c1, cz), 0.6f, SnowWhite);
        Kit.AddBall(body, new Vector3(cx, g + c2, cz), 0.45f, SnowWhite);
        Kit.AddBall(body, new Vector3(cx, g + c3, cz), 0.32f, SnowWhite);
        Place("Champion Snowman", body, snowy, 0f);

        var trim = new MeshBuilder();
        float brim = g + c3 + 0.32f * 0.8f;
        Kit.AddDisc(trim, new Vector3(cx, brim, cz), 0.36f, 0.04f, Coal, 14);
        Kit.AddDisc(trim, new Vector3(cx, brim + 0.04f, cz), 0.22f, 0.32f, Coal, 14);
        Kit.AddDisc(trim, new Vector3(cx, brim + 0.06f, cz), 0.23f, 0.07f, IsleKit.Red, 14);
        Ring(trim, new Vector3(cx, 0f, cz), g + c3 - 0.33f, g + c3 - 0.19f, 0.19f, 0.29f, 10, 0f, null, IsleKit.Red);      // scarf
        Kit.AddBox(trim, new Vector3(cx + 0.12f, g + c3 - 0.62f, cz - 0.27f), new Vector3(0.12f, 0.36f, 0.04f), IsleKit.Red);    // its tail
        Kit.AddBox(trim, new Vector3(cx, g + c3 - 0.03f, cz - 0.42f), new Vector3(0.06f, 0.06f, 0.25f), Carrot);                // nose
        foreach (float ex in new[] { -0.1f, 0.1f })
            Kit.AddBall(trim, new Vector3(cx + ex, g + c3 + 0.08f, cz - 0.29f), 0.035f, Coal);
        foreach (float by in new[] { -0.12f, 0.1f })
            Kit.AddBall(trim, new Vector3(cx, g + c2 + by, cz - 0.43f), 0.045f, Coal);
        Kit.AddBox(trim, new Vector3(cx - 0.45f - 0.3f, g + c2 + 0.08f, cz), new Vector3(0.7f, 0.05f, 0.05f), IsleKit.Wood);
        Kit.AddBox(trim, new Vector3(cx + 0.45f + 0.3f, g + c2 + 0.08f, cz), new Vector3(0.7f, 0.05f, 0.05f), IsleKit.Wood);

        Piece group = b.Empty("Trimmings", cx, cz, 0f);
        Place("Champion Trimmings", trim, bare, 0f, false).Under(group);
        group.Go.SetActive(false);      // nothing hides it again: a win's trimmings stay until the pack reloads
        return group;
    }

    // ---- helpers ----

    // How high the middle of an outline's disc stands above the ground: the large one's foot at OutlineFoot, each of
    // the others overlapping the one below by a tenth, as a snowman's balls sit into one another
    static float DiscHeight(int size)
    {
        float y = OutlineFoot + Radius[0];
        for (int i = 1; i <= size; i++) y += (Radius[i - 1] + Radius[i]) * 0.9f;
        return y;
    }

    // The ground's height at a local point, above the root's level
    float GroundAt(float x, float z) => b.WorldPoint(x, z).y - b.Root.transform.position.y;

    // An SDK part placed from the root's level (heights already include the structure's ground level)
    Piece FP(string prefab, float x, float z, float height, float yaw) => b.Part(prefab, x, z, height, yaw, false);

    // A mesh whose vertices are in the root's local frame (y above the root's level), placed at the root
    Piece Place(string name, MeshBuilder mb, Material material, float height, bool collider = true)
    {
        Piece piece = b.MeshPiece(name, mb.ToMesh(), material, collider);
        piece.Transform.position = b.WorldPoint(0f, 0f, height, false);
        return piece;
    }

    // A block between two corners in the root's local frame
    static void Block(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A triangle wound so that it faces 'outward' (Unity draws a face whose corners run clockwise as seen from in front)
    static void Face(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Vector3 outward, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(b2 - a, c - a), outward) >= 0f) mb.Tri(a, b2, c, color);
        else mb.Tri(a, c, b2, color);
    }

    // A planar quad, corners in order round its edge, facing 'outward'
    static void Face4(MeshBuilder mb, Vector3 a, Vector3 b2, Vector3 c, Vector3 d, Vector3 outward, Color color)
    {
        Face(mb, a, b2, c, outward, color);
        Face(mb, a, c, d, outward, color);
    }

    // A painted disc standing upright on a board: 'centre' is its middle on the board's face, 'facing' (level) the way
    // the board looks; it stands 'proud' of the face
    static void Roundel(MeshBuilder mb, Vector3 centre, Vector3 facing, float radius, float proud, Color color, int segments)
    {
        Vector3 side = Vector3.Cross(Vector3.up, facing).normalized, front = centre + facing * proud;
        for (int s = 0; s < segments; s++)
        {
            float a0 = 2f * Mathf.PI * s / segments, a1 = 2f * Mathf.PI * (s + 1) / segments;
            Vector3 d0 = (side * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * radius, d1 = (side * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * radius;
            Face(mb, front, front + d0, front + d1, facing, color);
            Face4(mb, centre + d0, centre + d1, front + d1, front + d0, d0 + d1, color);
        }
    }

    // A ring wall of 'sides' straight segments about 'centre' (only its x and z are used), between apothems 'inner' and
    // 'outer' (the flat faces' distances from the centre), from y0 to y1. Segment k is centred on the math angle
    // firstDeg + k * 360 / sides (from +x towards +z); segments for which 'skip' is true are left out, and the open
    // ends beside them are closed.
    static void Ring(MeshBuilder mb, Vector3 centre, float y0, float y1, float inner, float outer, int sides, float firstDeg,
        Func<int, bool> skip, Color color)
    {
        float step = 360f / sides, half = step / 2f * Mathf.Deg2Rad;
        float rIn = inner / Mathf.Cos(half), rOut = outer / Mathf.Cos(half);
        bool Skipped(int k) => skip != null && skip(((k % sides) + sides) % sides);
        for (int k = 0; k < sides; k++)
        {
            if (Skipped(k)) continue;
            float mid = (firstDeg + k * step) * Mathf.Deg2Rad, a0 = mid - half, a1 = mid + half;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            Vector3 tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));      // towards a1
            Vector3 c = new Vector3(centre.x, 0f, centre.z), lo = Vector3.up * y0, hi = Vector3.up * y1;
            Vector3 i0 = c + d0 * rIn, i1 = c + d1 * rIn, o0 = c + d0 * rOut, o1 = c + d1 * rOut;
            Face4(mb, o0 + hi, o1 + hi, i1 + hi, i0 + hi, Vector3.up, color);
            Face4(mb, o0 + lo, o1 + lo, i1 + lo, i0 + lo, Vector3.down, color);
            Face4(mb, o0 + lo, o1 + lo, o1 + hi, o0 + hi, radial, color);
            Face4(mb, i0 + lo, i1 + lo, i1 + hi, i0 + hi, -radial, color);
            if (Skipped(k - 1)) Face4(mb, i0 + lo, o0 + lo, o0 + hi, i0 + hi, -tangent, color);
            if (Skipped(k + 1)) Face4(mb, i1 + lo, o1 + lo, o1 + hi, i1 + hi, tangent, color);
        }
    }
}
