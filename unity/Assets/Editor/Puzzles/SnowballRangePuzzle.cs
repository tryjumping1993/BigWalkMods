// Snowball Range (puzzle 22, blind mask, snow): four hoops stand down a range at different distances, each with a
// lamp on top. One lamp lights; the thrower, blindfolded in the throwing box, has to put a snowball through that
// hoop, aimed by the spotter's voice ("left a bit, harder"). Then another lights. Six in a row fill the bar; the
// wrong hoop starts again. The spotter gathers the snowballs and passes them back over the box's low wall.
// No custom code: snowballs are items, each hoop a zone that notices them, and Press In Order runs it.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowballRangePuzzle : PuzzleDef
{
    public override string Name => "SnowballRange";
    public override Vector2 Footprint => new Vector2(26f, 36f);
    public override Vector2? FixedSite => new Vector2(-610f, -1270f);      // chosen on the island's lowland; its build levels and clears it

    static readonly Vector3[] Hoops = { new Vector3(-4.5f, 1.4f, 2f), new Vector3(3.5f, 1.9f, 6f), new Vector3(-2f, 2.4f, 10f), new Vector3(5f, 1.6f, 13f) };      // x, centre height, z
    static readonly Color[] Colours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.15f, 0.6f, 0.25f), new Color(0.15f, 0.4f, 0.9f), new Color(0.95f, 0.75f, 0.2f) };
    const float HoopR = 0.9f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -16f);
        b.ClearGround(26f, 36f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the throwing box: low walls, the blind turnstile in, a plain one out ----
        var box = new MeshBuilder();
        Kit.AddBox(box, new Vector3(0f, 0f, -9f), new Vector3(4f, 1.0f, 0.3f), IsleKit.Wood);       // front wall (balls go over it)
        Kit.AddBox(box, new Vector3(-2f, 0f, -11f), new Vector3(0.3f, 1.0f, 4f), IsleKit.Wood);
        Kit.AddBox(box, new Vector3(2f, 0f, -9.72f), new Vector3(0.3f, 1.0f, 1.45f), IsleKit.Wood);      // east wall, either side of the way out
        Kit.AddBox(box, new Vector3(2f, 0f, -12.85f), new Vector3(0.3f, 1.0f, 0.3f), IsleKit.Wood);
        Kit.AddBox(box, new Vector3(-1.6f, 0f, -9.6f), new Vector3(0.6f, 0.7f, 0.5f), IsleKit.DarkWood);      // the snowballs' trough
        b.MeshPiece("Throwing Box", box.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        IsleKit.BlindGate(b, 0f, -13f, 0f);
        b.Part("Gate No items", 2f, -11.6f, 0f, 90f).Named("Way Out").Set("PuzzleNoItemsGate", "oneWay", true);
        IsleKit.BlindfoldHook(b, -3f, -15f, 0f);
        // the turnstiles need wings so the box is only entered through them
        var wings = new MeshBuilder();
        Kit.AddBox(wings, new Vector3(-1.6f, 0f, -13f), new Vector3(0.9f, 1.0f, 0.3f), IsleKit.Wood);
        Kit.AddBox(wings, new Vector3(1.6f, 0f, -13f), new Vector3(0.9f, 1.0f, 0.3f), IsleKit.Wood);
        b.MeshPiece("Wings", wings.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the snowballs, a dozen, in the trough ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Piece>();
        for (int i = 0; i < 12; i++)
            balls.Add(b.Item("Snowball " + (i + 1), Kit.Ball(0.13f, 8, 12, 0.1f, 2200 + i), snow, -1.85f + (i % 3) * 0.17f, -9.75f + (i / 3) * 0.12f, 0.85f + (i / 6) * 0.1f, 0.6f));

        // ---- the hoops: a ring on two legs, a lamp on top, a thin zone in the ring, a snow wall behind ----
        var hoops = new MeshBuilder();
        var lamps = new Object[Hoops.Length];
        var zones = new List<Object>();
        for (int h = 0; h < Hoops.Length; h++)
        {
            Vector3 c = Hoops[h];
            for (int k = 0; k < 16; k++)
            {
                float a = 2f * Mathf.PI * k / 16f;
                var at = new Vector3(c.x + Mathf.Cos(a) * HoopR, c.y + Mathf.Sin(a) * HoopR - 0.06f, c.z);
                Kit.AddBox(hoops, at, new Vector3(0.38f, 0.12f, 0.12f), Colours[h], 0f);
            }
            Kit.AddBox(hoops, new Vector3(c.x - HoopR, 0f, c.z), new Vector3(0.1f, c.y, 0.1f), IsleKit.Wood);
            Kit.AddBox(hoops, new Vector3(c.x + HoopR, 0f, c.z), new Vector3(0.1f, c.y, 0.1f), IsleKit.Wood);
            Kit.AddBox(hoops, new Vector3(c.x, 0f, c.z + 2.5f), new Vector3(3f, 2.6f, 0.4f), new Color(0.92f, 0.94f, 0.98f));      // backstop
            Piece lamp = b.Part("Light 6", c.x, c.z - 0.1f, c.y + HoopR + 0.25f, 180f).Named("Hoop Lamp " + (h + 1));
            lamp.Set("PuzzleLight", "color", Colours[h]);
            lamps[h] = lamp.Component("PuzzleLight");
            Piece zone = b.Part("Presence Sensor", c.x, c.z, c.y, 0f).Named("Hoop " + (h + 1));
            BoxCollider col = zone.Go.GetComponent<BoxCollider>();
            if (col == null) col = zone.Go.AddComponent<BoxCollider>();
            col.size = new Vector3(HoopR * 1.5f, HoopR * 1.5f, 0.35f);
            col.center = Vector3.zero;
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            foreach (Piece ball in balls) zone.Add("PuzzlePresenceSensor", "acceptedCustom", ball.Component("PuzzleItem"));
            zones.Add(zone.Component("PuzzlePresenceSensor"));
        }
        b.MeshPiece("Hoops", hoops.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the count, beside the box where the spotter stands ----
        IsleKit.BoardOnPosts(b, "Count Board", -6f, -10f, 45f, 2.4f, 1.3f, 2.4f, IsleKit.Board);
        Vector2 face = IsleKit.Turn(0f, IsleKit.FaceZ + 0.03f, 45f);
        Piece bar = b.Part("Progress Bar", -6f + face.x, -10f + face.y, 1.85f, 45f);
        bar.Set("PuzzleProgressBar", "total", 6);
        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", zones.ToArray());
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 6);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 6f, -15f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 24f, 34f, 2.6f, true, 221);
    }
}
