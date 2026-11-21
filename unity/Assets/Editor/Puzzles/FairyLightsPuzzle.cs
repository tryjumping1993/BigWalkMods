// Fairy Lights (puzzle 20): four strings of lights, red, green, blue and gold, run out from a tall pole to four
// coloured pads in the snow, thirty metres apart. Every few seconds one colour lights at the top of the pole, and
// when its time runs out somebody must be standing on that colour's pad, and nobody on any other. Eight in a row
// fill the bar. With four pads and three people, someone always has to run, and everyone has to step off in time.
// No custom code: Press In Order in its timed mode, with presence sensors for pads.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class FairyLightsPuzzle : PuzzleDef
{
    public override string Name => "FairyLights";
    public override Vector2 Footprint => new Vector2(36f, 36f);
    public override Vector2? FixedSite => new Vector2(-860f, -2090f);      // chosen on the island's lowland; its build levels and clears it

    const float Out = 14f, PoleH = 7f, Phase = 7f;
    static readonly Color[] Colours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.15f, 0.6f, 0.25f), new Color(0.15f, 0.4f, 0.9f), new Color(0.95f, 0.75f, 0.2f) };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -6f);
        b.ClearGround(36f, 36f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the pole in the middle, its four lamps at the top, one facing each pad ----
        var pole = new MeshBuilder();
        Kit.AddBox(pole, Vector3.zero, new Vector3(0.4f, PoleH, 0.4f), IsleKit.Cream);
        Kit.AddStar(pole, new Vector3(0f, PoleH + 0.8f, 0f), 0.6f, 0.1f, IsleKit.Gold);
        var lamps = new Object[4];
        var pads = new List<Object>();
        for (int i = 0; i < 4; i++)
        {
            float yaw = 45f + i * 90f, a = yaw * Mathf.Deg2Rad;
            float px = Mathf.Sin(a) * Out, pz = Mathf.Cos(a) * Out;
            Vector2 face = IsleKit.Turn(0f, 0.22f, yaw);
            Piece lamp = b.Part("Light 6", face.x, face.y, PoleH - 0.6f, yaw).Named("Lamp " + (i + 1));
            lamp.Set("PuzzleLight", "color", Colours[i]);
            lamps[i] = lamp.Component("PuzzleLight");
            // the pad: a ring of its colour in the snow, a post with a flag, and the sensor
            for (int k = 0; k < 10; k++)
            {
                float ka = 2f * Mathf.PI * k / 10f;
                Kit.AddBox(pole, new Vector3(px + Mathf.Cos(ka) * 1.3f, 0f, pz + Mathf.Sin(ka) * 1.3f), new Vector3(0.5f, 0.12f, 0.2f), Colours[i], -ka * Mathf.Rad2Deg);
            }
            Vector2 flagAt = new Vector2(px, pz) * 1.14f;
            Kit.AddBox(pole, new Vector3(flagAt.x, 0f, flagAt.y), new Vector3(0.1f, 2.6f, 0.1f), IsleKit.Wood);
            Kit.AddBox(pole, new Vector3(flagAt.x, 2.0f, flagAt.y), new Vector3(0.05f, 0.5f, 0.8f), Colours[i], yaw + 90f);
            pads.Add(IsleKit.Sensor(b, "Pad " + (i + 1), px, pz, new Vector3(2.4f, 2.6f, 2.4f)).Component("PuzzlePresenceSensor"));
            // the string of lights from the pole's top out to the flag
            Kit.LightString(b, "String " + (i + 1), new List<Vector3> { new Vector3(0f, PoleH - 0.3f, 0f), new Vector3(flagAt.x, 2.5f, flagAt.y) }, 0.9f, 0.45f, true, false, 200 + i);      // (local x, height, local z)
        }
        b.MeshPiece("Pole And Pads", pole.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the bars: the count of rounds, and the phase draining, on a board by the start ----
        IsleKit.BoardOnPosts(b, "Count Board", 0f, -7f, 0f, 3.2f, 1.0f, 2.8f, IsleKit.Board);
        Piece bar = b.Part("Progress Bar", 0f, -7f + IsleKit.FaceZ + 0.03f, 2.35f, 0f);
        bar.Set("PuzzleProgressBar", "total", 8);
        Piece phaseBar = b.Part("Progress Bar", 0f, -7f + IsleKit.FaceZ + 0.03f, 1.5f, 0f).Named("Phase Bar");
        phaseBar.Set("PuzzleProgressBar", "total", 7);
        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", pads.ToArray());
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Add(pio, "phaseBars", phaseBar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 8);
        Sdk.Set(pio, "holdAtEnd", true);
        Sdk.Set(pio, "phaseSeconds", Phase);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 5.5f, -7f, 0f);
    }
}
