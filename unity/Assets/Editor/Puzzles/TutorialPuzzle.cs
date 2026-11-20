// The SDK's "first puzzle": a display shows three pictures, players find the matching tiles, hang them on the
// peg board, and the validator checks the answer. The board links to the display and validator by itself
// (one board, one display of the same size, one validator). Kept as the framework's smoke test.
using Bwm;
using UnityEngine;

public sealed class TutorialPuzzle : PuzzleDef
{
    public override string Name => "BwmTutorial";
    public override Vector2 Footprint => new Vector2(18f, 14f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -6f);
        b.ClearGround(20f, 16f);

        b.Part("Switch Start", -6f, 0f);
        b.Part("Live Light", -7.5f, 0f);
        b.Part("Peg Board", -2f, 0f).Set("PuzzlePegBoard", "size", 2);      // Slots3
        b.Part("Display", 1f, 0f).Set("PuzzleDisplay", "size", 2);          // Three
        b.Part("Validator", 3.5f, 0f);
        for (int i = 0; i < 4; i++)
            b.Part("Peg Tile", -3f + i * 0.7f, -2f, 0.5f).Named("Peg Tile " + (i + 1)).Set("PuzzlePegTile", "image", i + 1);
        // Blockout geometry and event wiring, exercised here so the smoke test covers them
        b.Box("Plinth", -2f, 4f, new Vector3(1.2f, 0.4f, 1.2f), new Color(0.75f, 0.72f, 0.65f));
        b.Part("Gourd Vice", -2f, 4f, 0.4f);
        b.Part("Pillow", 0f, 4f);
        b.Part("Confetti", -4f, 4f);
        Piece door = b.Part("Door", 7f, 0f).Set("PuzzleDoor", "mode", 2);    // ControlWithFunctionsOnly
        b.RootPiece.On("PuzzleSDK", "onWin", door.Component("PuzzleDoor"), "Open");
    }
}
