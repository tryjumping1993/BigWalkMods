// Sleigh Run (puzzle 17): the present is the game's timer ball. Taking it from the dispenser starts its clock, and
// it has to reach the sleigh at the far end of a walled lane before it runs out. Doors cross the lane, shut; each
// opens only while someone outside the wall holds its button. So the runner calls, the holders hold, and the doors
// open just ahead of the present. Two doors for three players, three for four or more. No custom code: the timer
// ball, doors worked by hold buttons, and the ball's holder.
using Bwm;
using UnityEngine;

public sealed class SleighRunPuzzle : PuzzleDef
{
    public override string Name => "SleighRun";
    public override Vector2 Footprint => new Vector2(18f, 48f);
    public override Vector2? FixedSite => new Vector2(-870f, -1070f);      // chosen on the island's lowland; its build levels and clears it

    const float Half = 2.2f, WallH = 2.3f, Start = -20f, End = 21f, Seconds = 26f;
    static readonly float[] DoorZ = { -10f, 1f, 12f };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, Start - 4f);
        b.ClearGround(18f, 48f);
        Material snowy = IsleKit.Snowy(b);
        var snowBlock = new Color(0.92f, 0.94f, 0.98f);

        // ---- the lane: snow walls the whole way, the doors across it ----
        var lane = new MeshBuilder();
        Kit.AddBox(lane, new Vector3(-Half - 0.25f, 0f, (Start + End) / 2f), new Vector3(0.5f, WallH, End - Start), snowBlock);
        Kit.AddBox(lane, new Vector3(Half + 0.25f, 0f, (Start + End) / 2f), new Vector3(0.5f, WallH, End - Start), snowBlock);
        b.MeshPiece("Lane", lane.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        Piece fourPlus = IsleKit.Lobby(b, "Third Door", false, false, true);
        for (int d = 0; d < DoorZ.Length; d++)
        {
            Piece parent = d == 2 ? fourPlus : null;
            float z = DoorZ[d];
            var frame = new MeshBuilder();      // the wall across the lane either side of the door, and a lintel
            const float DoorHalf = 0.63f;      // (the game's door is 1.24 m across)
            Kit.AddBox(frame, new Vector3(-(DoorHalf + Half) / 2f, 0f, z), new Vector3(Half - DoorHalf, WallH, 0.4f), IsleKit.Red);
            Kit.AddBox(frame, new Vector3((DoorHalf + Half) / 2f, 0f, z), new Vector3(Half - DoorHalf, WallH, 0.4f), IsleKit.Red);
            Kit.AddBox(frame, new Vector3(0f, WallH, z), new Vector3(Half * 2f + 1f, 0.3f, 0.5f), IsleKit.Red);
            Piece framePiece = b.MeshPiece("Door Frame " + (d + 1), frame.ToMesh(), snowy);
            framePiece.Transform.position = b.WorldPoint(0f, 0f);
            Piece door = b.Part("Door", 0f, z, 0f, 0f).Named("Door " + (d + 1)).Set("PuzzleDoor", "mode", 2);      // worked only by its button
            // its button: on a post outside the lane's wall, a good way off, alternately east and west
            float side = d % 2 == 0 ? 1f : -1f;
            var post = new MeshBuilder();
            Kit.AddBox(post, Vector3.zero, new Vector3(0.26f, 1.5f, 0.26f), IsleKit.Wood);
            Piece postPiece = b.MeshPiece("Button Post " + (d + 1), post.ToMesh(), snowy);
            postPiece.Transform.position = b.WorldPoint(side * (Half + 5f), z - 4f);
            Piece button = b.Part("Button Hold", side * (Half + 5f) - side * 0.15f, z - 4f, 1.1f, side > 0f ? -90f : 90f).Named("Door Button " + (d + 1));
            Component doorC = door.Component("PuzzleDoor");
            button.On("PuzzleButton", "onPressed", doorC, "Open");
            button.On("PuzzleButton", "onReleased", doorC, "Close");
            if (parent != null) { framePiece.Under(parent); door.Under(parent); postPiece.Under(parent); button.Under(parent); }
        }

        // ---- the start: the dispenser; the end: the sleigh with the ball's holder in it ----
        var dispenserBase = new MeshBuilder();
        Kit.AddBox(dispenserBase, Vector3.zero, new Vector3(1.4f, 0.4f, 1.4f), IsleKit.Stone);
        b.MeshPiece("Dispenser Base", dispenserBase.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, Start + 1.5f);
        b.Part("Timer Spawner", 0f, Start + 1.5f, 0.4f, 0f).Set("PuzzleTimerSpawner", "time", Seconds);
        var sleigh = new MeshBuilder();
        Kit.AddBox(sleigh, new Vector3(0f, 0.25f, 0f), new Vector3(1.8f, 0.6f, 3f), IsleKit.Red);
        Kit.AddBox(sleigh, new Vector3(0f, 0.85f, -1.2f), new Vector3(1.8f, 0.9f, 0.4f), IsleKit.Red);      // its back
        Kit.AddBox(sleigh, new Vector3(-0.8f, 0f, 0.2f), new Vector3(0.12f, 0.25f, 3.6f), IsleKit.Gold);      // runners
        Kit.AddBox(sleigh, new Vector3(0.8f, 0f, 0.2f), new Vector3(0.12f, 0.25f, 3.6f), IsleKit.Gold);
        b.MeshPiece("Sleigh", sleigh.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, End + 2f);
        Piece goal = b.Part("Timer Goal", 0f, End + 2.2f, 0.85f, 180f);
        goal.On("PuzzleTimerGoal", "onTimerPlaced", b.Puzzle, "Win");

        IsleKit.StartCorner(b, -5.5f, Start - 2f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 16f, 46f, 2.7f, true, 171);
    }
}
