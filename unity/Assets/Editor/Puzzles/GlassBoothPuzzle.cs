// Glass Booth: a communication puzzle for two or more players, with no custom code.
//
// One player (the reader) goes into the booth and flips the start switch. The door shuts, and a display above
// the booth's window, which only the reader can see, shows three pictures. Outside the window is a table of
// six tiles standing with their pictures towards the booth, so the people outside see only their backs. The
// booth is soundproof: the reader has to point out the right tiles, in order, through the glass. The others
// carry them round the far side of a wall, out of the reader's sight, hang them on the peg board there and
// press the validator. Flipping the switch off opens the door again, so nobody can be locked in.
//
// Parts face their local +z; "yaw" turns that: 90 faces +x (east), -90 faces -x (west), 180 faces -z.
using Bwm;
using UnityEngine;

public sealed class GlassBoothPuzzle : PuzzleDef
{
    public override string Name => "GlassBooth";
    public override Vector2 Footprint => new Vector2(24f, 14f);

    const float Floor = 0.25f;      // top of the pad, above the root's level
    static readonly Color Concrete = new Color(0.72f, 0.70f, 0.66f);
    static readonly Color WallColor = new Color(0.83f, 0.42f, 0.25f);
    static readonly Color Wood = new Color(0.55f, 0.38f, 0.24f);
    static readonly Color Plaster = new Color(0.9f, 0.88f, 0.82f);

    PuzzleBuilder b;

    // A solid block given by its low corner and size, standing on the pad
    Piece Block(string name, float x0, float z0, float sizeX, float sizeZ, float y0, float height, Color color)
        => b.Box(name, x0 + sizeX / 2f, z0 + sizeZ / 2f, new Vector3(sizeX, height, sizeZ), color, Floor + y0, 0f, false);

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(26f, 16f, 1f, 0f);
        // One level pad for everything, sunk well into the ground so uneven terrain never shows a gap
        b.Box("Pad", 1f, 0f, new Vector3(22f, 1.75f, 11f), Concrete, -1.5f, 0f, false);
        b.DebugTeleport(1f, -3.5f, Floor + 1.2f);

        // ---- booth: inside is x -7..-3, z -1.5..1.5; walls 0.2 thick, 2.6 high; window in the east wall ----
        Piece booth = b.Empty("Booth", -5f, 0f, Floor, false);
        b.AddScript(booth, "PuzzleSoundProof");
        Block("Wall South", -7.2f, -1.7f, 4.4f, 0.2f, 0f, 2.6f, WallColor).Under(booth);
        Block("Wall North", -7.2f, 1.5f, 4.4f, 0.2f, 0f, 2.6f, WallColor).Under(booth);
        Block("Wall West A", -7.2f, -1.5f, 0.2f, 0.85f, 0f, 2.6f, WallColor).Under(booth);
        Block("Wall West B", -7.2f, 0.65f, 0.2f, 0.85f, 0f, 2.6f, WallColor).Under(booth);
        Block("Wall West Lintel", -7.2f, -0.65f, 0.2f, 1.3f, 2.05f, 0.55f, WallColor).Under(booth);
        Block("Wall East Sill", -3f, -1.5f, 0.2f, 3f, 0f, 1f, WallColor).Under(booth);
        Block("Wall East Top", -3f, -1.5f, 0.2f, 3f, 2f, 0.6f, WallColor).Under(booth);
        Block("Wall East A", -3f, -1.5f, 0.2f, 0.7f, 1f, 1f, WallColor).Under(booth);
        Block("Wall East B", -3f, 0.8f, 0.2f, 0.7f, 1f, 1f, WallColor).Under(booth);
        Block("Roof", -7.2f, -1.7f, 4.4f, 3.4f, 2.6f, 0.2f, WallColor).Under(booth);

        // Door in the west wall: shuts while the puzzle runs (its default mode), and blocks voices
        b.Part("Door", -7.1f, 0f, Floor, 90f, false).Set("PuzzleDoor", "soundproof", true);
        // Window: 1.6 wide, 1 high, soundproof glass (the Glass prefab is a 2.087 x 1.868 pane)
        b.Part("Glass", -2.9f, 0f, Floor + 1.5f, 90f, false).Scaled(1.6f / 2.087f, 1f / 1.868f, 1f)
            .Set("PuzzleGlass", "style", 0).Set("PuzzleGlass", "sound", 2);
        // Inside: the start switch on the south wall, and the display above the window, facing into the room
        b.Part("Switch Start", -5f, -1.42f, Floor + 1.3f, 0f, false);
        b.Part("Display", -3.18f, 0f, Floor + 2.3f, -90f, false).Set("PuzzleDisplay", "size", 2);      // three flaps
        b.Part("Live Light", -2.79f, 1.15f, Floor + 2.3f, 90f, false);
        // A pendant lamp that lights the booth while the puzzle runs (the SDK's checks want start and stop to differ)
        Piece lamp = b.Part("Light 2", -5.2f, 0f, Floor + 2.6f, 0f, false);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", lamp.Component("PuzzleLight"), "TurnOn");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", lamp.Component("PuzzleLight"), "TurnOff");

        // ---- tile table outside the window: six tiles, pictures towards the booth ----
        Block("Table", -2.7f, -1.6f, 0.7f, 3.2f, 0f, 0.9f, Wood);
        for (int i = 0; i < 6; i++)
            b.Part("Peg Tile", -2.35f, -1.25f + i * 0.5f, Floor + 0.9f + 0.24f, -90f, false)
                .Named("Peg Tile " + (i + 1)).Set("PuzzlePegTile", "image", i + 1);

        // ---- board wall, 10 m east: the peg board and validator are on its far side ----
        Block("Board Wall", 7f, -2.2f, 0.3f, 4.4f, 0f, 2.4f, Plaster);
        b.Part("Peg Board", 7.3f, -0.9f, Floor + 1.3f, 90f, false).Set("PuzzlePegBoard", "size", 2);    // three slots
        b.Part("Validator", 7.3f, 1.4f, Floor + 1.5f, 90f, false);
        Block("Plinth", 9.4f, -0.6f, 1.2f, 1.2f, 0f, 0.45f, Concrete);
        b.Part("Gourd Vice", 10f, 0f, Floor + 0.45f, 90f, false);
        b.Part("Pillow", 10f, 2.2f, Floor + 0.13f, 0f, false);
        b.Part("Confetti", 10f, -1.6f, Floor + 0.1f, 0f, false);
    }
}
