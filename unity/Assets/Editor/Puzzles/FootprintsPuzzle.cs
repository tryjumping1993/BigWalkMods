// Footprints (puzzle 16): nine hidden pads under the snow of a field, in three rows of three, staked out at the
// field's edges so the rows and columns can be named. Inside the lookout's hut, on its back wall where nobody in
// the field can see it, a board of nine lamps lights one pad at a time. The lookout calls it ("middle row, left"),
// someone in the field stands on that pad, and the next one lights. Eight in a row fill the bar; standing on the
// wrong pad starts again. No custom code: the SDK's Press In Order with presence sensors as its buttons.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class FootprintsPuzzle : PuzzleDef
{
    public override string Name => "Footprints";
    public override Vector2 Footprint => new Vector2(26f, 34f);
    public override Vector2? FixedSite => new Vector2(-850f, -830f);      // chosen on the island's lowland; its build levels and clears it

    const float Spacing = 4.6f, PadSize = 1.9f, FieldZ = 7f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -12f);
        b.ClearGround(26f, 34f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the field: nine pads, and stakes along two edges naming the rows and columns ----
        var buttons = new List<Object>();
        var stakes = new MeshBuilder();
        Color[] rowColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                float x = (col - 1) * Spacing, z = FieldZ + row * Spacing;
                Piece pad = IsleKit.Sensor(b, "Pad " + (row + 1) + "-" + (col + 1), x, z, new Vector3(PadSize, 2.6f, PadSize));
                buttons.Add(pad.Component("PuzzlePresenceSensor"));
            }
        for (int i = 0; i < 3; i++)
        {
            float z = FieldZ + i * Spacing, x = (i - 1) * Spacing;
            Kit.AddBox(stakes, new Vector3(-Spacing * 1.5f - 1.2f, 0f, z), new Vector3(0.14f, 1.3f, 0.14f), rowColour[i]);      // rows: red, green, gold up the field
            Kit.AddBox(stakes, new Vector3(Spacing * 1.5f + 1.2f, 0f, z), new Vector3(0.14f, 1.3f, 0.14f), rowColour[i]);
            Kit.AddBox(stakes, new Vector3(x, 0f, FieldZ - Spacing * 0.5f - 1.4f), new Vector3(0.12f, 0.8f + 0.25f * i, 0.12f), IsleKit.Wood);      // columns: short, middle, tall stakes along the front
        }
        b.MeshPiece("Stakes", stakes.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the lookout's hut, its open side away from the field, the lamp board on its back wall ----
        const float HutZ = -9f;
        var hut = new MeshBuilder();
        Kit.AddBox(hut, new Vector3(0f, 0f, 1.4f), new Vector3(4.2f, 2.6f, 0.2f), IsleKit.Wood);       // the back wall, towards the field
        Kit.AddBox(hut, new Vector3(-2.0f, 0f, 0f), new Vector3(0.2f, 2.6f, 3f), IsleKit.Wood);
        Kit.AddBox(hut, new Vector3(2.0f, 0f, 0f), new Vector3(0.2f, 2.6f, 3f), IsleKit.Wood);
        Kit.AddBox(hut, new Vector3(0f, 2.6f, 0f), new Vector3(4.8f, 0.18f, 3.6f), IsleKit.Red);       // roof
        Kit.AddBox(hut, new Vector3(0f, 0.8f, 1.27f), new Vector3(2.4f, 1.6f, 0.06f), IsleKit.Board);  // the lamp board, inside
        b.MeshPiece("Lookout Hut", hut.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, HutZ);
        var lamps = new Object[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                // seen from inside the hut (facing the field through the wall), so left is left
                Piece lamp = b.Part("Light 8", (col - 1) * 0.65f, HutZ + 1.24f, 1.05f + row * 0.5f, 180f).Named("Lamp " + (row + 1) + "-" + (col + 1));
                lamps[row * 3 + col] = lamp.Component("PuzzleLight");
            }
        // the count, on the hut's outside where the field can see it
        Piece bar = b.Part("Progress Bar", 0f, HutZ + 1.56f, 2.0f, 0f);
        bar.Set("PuzzleProgressBar", "total", 8);

        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", buttons.ToArray());
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 8);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 0f, -14f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 4f, 24f, 22f, 2.6f, true, 161);
    }
}
