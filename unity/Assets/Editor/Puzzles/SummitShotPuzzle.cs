// Summit Shot (the summit's own puzzle, blind mask, snow): a great glass cube on the crest beside the summit, four
// throwing lanes inside it, a hoop at the far end of each. Every thrower wears the blindfold and goes in through
// their lane's blind turnstile; nothing can be carried in, so the snowballs wait in a trough at each lane's head.
// Outside, above each hoop, a lamp: one lights at a time, and only that lane's thrower may score, by throwing or
// kicking a snowball through their hoop. Then another lights: the players go in turns, in an order nobody can
// predict. Eight in a row fill the bar; a snowball through the wrong hoop empties it. The spotters outside see the
// lamps, the hoops and the balls, but the glass muffles them: they have to shout, and be understood. Each lane has
// five snowballs, and a blind thrower who has thrown them all must be talked to wherever they lie. With three
// players one of them works two lanes. No custom code: Press In Order, zones that notice snowballs, the game's glass.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SummitShotPuzzle : PuzzleDef
{
    public override string Name => "SummitShot";
    public override Vector2 Footprint => new Vector2(18f, 24f);
    // on the shoulder south-east of the summit, twenty metres below it, a short walk from the gondola's top station (the island levels it)
    public override Vector2? FixedSite => WinterIslePuzzle.Peak + new Vector2(0.866f, -0.5f) * 44f;

    const float X0 = -6f, X1 = 6f, Z0 = -5f, Z1 = 9f, High = 5f, Glass = 0.12f;
    const int Lanes = 4;
    const float LaneWidth = 3f, HoopZ = 5f, HoopR = 0.5f, GateHalf = 1.07f;
    static readonly float[] HoopHeight = { 1.3f, 2.0f, 1.6f, 2.3f };
    static readonly Color[] Colours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.15f, 0.6f, 0.25f), new Color(0.15f, 0.4f, 0.9f), new Color(0.95f, 0.75f, 0.2f) };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -9f);
        b.ClearGround(18f, 24f);
        Material snowy = IsleKit.Snowy(b);
        float LaneX(int lane) => X0 + LaneWidth * (lane + 0.5f);

        // ---- the cube: glass walls and roof, gaps in the south wall for the four turnstiles and one in the north for the way out ----
        var glass = new MeshBuilder();
        void Pane(float xa, float xb, float za, float zb, float ya, float yb)
            => Kit.AddBox(glass, new Vector3((xa + xb) / 2f, ya, (za + zb) / 2f), new Vector3(Mathf.Max(xb - xa, Glass), yb - ya, Mathf.Max(zb - za, Glass)), Color.white);
        float edge = X0;
        for (int lane = 0; lane < Lanes; lane++)      // the south wall, between the turnstiles
        {
            Pane(edge, LaneX(lane) - GateHalf, Z0, Z0, 0f, 2.35f);
            edge = LaneX(lane) + GateHalf;
        }
        Pane(edge, X1, Z0, Z0, 0f, 2.35f);
        Pane(X0, X1, Z0, Z0, 2.35f, High);                                   // ...and over them
        const float ExitX = 4.5f;
        Pane(X0, ExitX - GateHalf, Z1, Z1, 0f, 2.35f); Pane(ExitX + GateHalf, X1, Z1, Z1, 0f, 2.35f); Pane(X0, X1, Z1, Z1, 2.35f, High);      // north
        Pane(X0, X0, Z0, Z1, 0f, High); Pane(X1, X1, Z0, Z1, 0f, High);     // west and east
        Pane(X0, X1, Z0, Z1, High, High + Glass);                            // the roof
        Piece cube = b.MeshPiece("Glass Cube", glass.ToMesh(), IsleKit.Bare(b), true);
        cube.Transform.position = b.WorldPoint(0f, 0f);
        Component glassC = b.AddScript(cube, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // clear
        Sdk.Set(glassC, "sound", 0);      // muffles: the spotters must shout

        // ---- its frame, the lanes' dividers, the troughs, the hoops on their legs ----
        var frame = new MeshBuilder();
        foreach (float x in new[] { X0, X1 })
            foreach (float z in new[] { Z0, Z1 })
                Kit.AddBox(frame, new Vector3(x, 0f, z), new Vector3(0.3f, High + 0.3f, 0.3f), IsleKit.Red);
        Kit.AddBox(frame, new Vector3(0f, High + 0.12f, Z0), new Vector3(X1 - X0 + 0.3f, 0.2f, 0.3f), IsleKit.Gold);
        Kit.AddBox(frame, new Vector3(0f, High + 0.12f, Z1), new Vector3(X1 - X0 + 0.3f, 0.2f, 0.3f), IsleKit.Gold);
        Kit.AddBox(frame, new Vector3(X0, High + 0.12f, (Z0 + Z1) / 2f), new Vector3(0.3f, 0.2f, Z1 - Z0), IsleKit.Gold);
        Kit.AddBox(frame, new Vector3(X1, High + 0.12f, (Z0 + Z1) / 2f), new Vector3(0.3f, 0.2f, Z1 - Z0), IsleKit.Gold);
        for (int d = 1; d < Lanes; d++)      // dividers a blind hand can follow, stopping short of the way out along the north wall
            Kit.AddBox(frame, new Vector3(X0 + LaneWidth * d, 0f, (Z0 + 6.4f) / 2f), new Vector3(0.14f, 1.25f, 6.4f - Z0), IsleKit.Wood);
        var lamps = new Object[Lanes];
        var zones = new List<Object>();
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Piece>();
        for (int lane = 0; lane < Lanes; lane++)
        {
            float x = LaneX(lane);
            Kit.AddBox(frame, new Vector3(x, 0f, Z0 + 2.2f), new Vector3(1.6f, 0.55f, 0.6f), IsleKit.DarkWood);      // the trough's stand
            Kit.AddBox(frame, new Vector3(x, 0f, Z0 + 0.25f), new Vector3(0.3f, 0.06f, 0.3f), Colours[lane]);        // a mark of the lane's colour inside its turnstile
            for (int k = 0; k < 14; k++)      // the hoop
            {
                float a = 2f * Mathf.PI * k / 14f;
                Kit.AddBox(frame, new Vector3(x + Mathf.Cos(a) * HoopR, HoopHeight[lane] + Mathf.Sin(a) * HoopR - 0.05f, HoopZ), new Vector3(0.26f, 0.1f, 0.1f), Colours[lane]);
            }
            Kit.AddBox(frame, new Vector3(x - HoopR, 0f, HoopZ), new Vector3(0.08f, HoopHeight[lane], 0.08f), IsleKit.Wood);
            Kit.AddBox(frame, new Vector3(x + HoopR, 0f, HoopZ), new Vector3(0.08f, HoopHeight[lane], 0.08f), IsleKit.Wood);
            for (int i = 0; i < 5; i++)
                balls.Add(b.Item("Snowball " + (lane + 1) + "-" + (i + 1), Kit.Ball(0.14f, 8, 12, 0.1f, 3100 + lane * 10 + i), snow, x - 0.56f + i * 0.28f, Z0 + 2.2f, 0.55f + 0.16f, 0.6f));
        }
        b.MeshPiece("Cube Frame", frame.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        for (int lane = 0; lane < Lanes; lane++)
        {
            float x = LaneX(lane);
            IsleKit.BlindGate(b, x, Z0, 0f).Named("Blind Turnstile " + (lane + 1));
            // the lamp: outside, over the south wall above its lane, facing the spotters
            Piece lamp = b.Part("Light 6", x, Z0 - 0.2f, High + 0.75f, 180f).Named("Lane Lamp " + (lane + 1));
            lamp.Set("PuzzleLight", "color", Colours[lane]);
            lamps[lane] = lamp.Component("PuzzleLight");
            Piece zone = b.Part("Presence Sensor", x, HoopZ, HoopHeight[lane], 0f).Named("Hoop " + (lane + 1));
            BoxCollider col = zone.Go.GetComponent<BoxCollider>();
            if (col == null) col = zone.Go.AddComponent<BoxCollider>();
            col.size = new Vector3(HoopR * 1.6f, HoopR * 1.6f, 0.3f);
            col.center = Vector3.zero;
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            foreach (Piece ball in balls) zone.Add("PuzzlePresenceSensor", "acceptedCustom", ball.Component("PuzzleItem"));
            zones.Add(zone.Component("PuzzlePresenceSensor"));
        }
        b.Part("Gate No items", ExitX, Z1, 0f, 0f).Named("Way Out").Set("PuzzleNoItemsGate", "oneWay", true);

        // ---- outside: the blindfolds, the count, the start ----
        for (int i = 0; i < Lanes; i++) IsleKit.BlindfoldHook(b, -7.5f + i * 1.1f, Z0 - 4.5f, 0f, 0f, true, " " + (i + 1));
        IsleKit.BoardOnPosts(b, "Count Board", 7.8f, Z0 - 3f, -30f, 2.6f, 1.3f, 2.5f, IsleKit.Board);
        Vector2 face = IsleKit.Turn(0f, IsleKit.FaceZ + 0.03f, -30f + 180f);
        Piece bar = b.Part("Progress Bar", 7.8f + face.x, Z0 - 3f + face.y, 1.9f, 150f);
        bar.Set("PuzzleProgressBar", "total", 8);
        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", zones.ToArray());
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 8);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 0f, Z0 - 6.5f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 1f, 17f, 23f, 2.7f, true, 311);
    }
}
