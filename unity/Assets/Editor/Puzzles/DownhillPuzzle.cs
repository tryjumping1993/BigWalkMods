// Downhill (puzzle 5): a race against the clock down the great mountain. The game's timer ball comes out of a
// dispenser beside the gondola's top station; taking it starts its countdown, and it has to be in the holder
// past the finish arch at the foot of the pistes before the time is up. On foot it cannot be done: the run is
// a kilometre and a half, so the carrier has to ski or snowboard it (racks stand by the top station), and the
// others can ride down ahead to open the gate of lights and call the line. No custom code: the SDK's Timer
// Spawner and Timer Goal do it.
using Bwm;
using UnityEngine;

public sealed class DownhillPuzzle : PuzzleDef
{
    public override string Name => "Downhill";
    public override Vector2 Footprint => new Vector2(14f, 14f);
    // In the run-out below the finish arch. A fixed place: the island's build levels it (rebuild WinterIsle after moving it)
    public override Vector2? FixedSite
    {
        get
        {
            Vector2 mid = WinterIslePuzzle.PisteFinish(out Vector2 direction, out float half);
            var side = new Vector2(direction.y, -direction.x);
            return mid + direction * 34f - side * (half * 0.2f);
        }
    }

    const float Seconds = 150f;      // skiing the run takes about a hundred; walking it, six minutes
    static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color Stone = new Color(0.50f, 0.52f, 0.56f);
    static readonly Color Red = new Color(0.8f, 0.12f, 0.1f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -3.5f);
        Material snowy = b.ShaderMaterial("BWM/Snowy", "Snowy");
        b.ClearGround(14f, 14f);

        // ---- the finish: the ball's holder on a plinth, the start switch, the gourd ----
        var furniture = new MeshBuilder();
        Kit.AddBox(furniture, new Vector3(0f, 0f, 0f), new Vector3(1.2f, 0.5f, 1.2f), Stone);                 // under the holder
        Kit.AddBox(furniture, new Vector3(2.4f, 0f, 0f), new Vector3(0.26f, 1.5f, 0.2f), Wood);              // the switch's post
        Kit.AddBox(furniture, new Vector3(-2.6f, 0f, 0f), new Vector3(1.1f, 0.45f, 1.1f), Stone);            // under the gourd
        // a chequered board behind, so the finish reads from up the run
        Kit.AddBox(furniture, new Vector3(-1.5f, 0f, -2.6f), new Vector3(0.2f, 3.2f, 0.2f), Red);
        Kit.AddBox(furniture, new Vector3(1.5f, 0f, -2.6f), new Vector3(0.2f, 3.2f, 0.2f), Red);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 2; j++)
                Kit.AddBox(furniture, new Vector3(-1.25f + i * 0.5f, 2.2f + j * 0.5f, -2.6f), new Vector3(0.5f, 0.5f, 0.08f), (i + j) % 2 == 0 ? new Color(0.1f, 0.1f, 0.12f) : new Color(0.95f, 0.95f, 0.92f));
        b.MeshPiece("Finish", furniture.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        Piece goal = b.Part("Timer Goal", 0f, 0f, 0.5f + 0.12f, 0f);
        b.Part("Switch Start", 2.4f, 0.1f + 0.073f, 1.25f, 0f);
        b.Part("Live Light", 2.4f, 0.1f, 1.42f, 0f);
        b.Part("Gourd Vice", -2.6f, 0f, 0.45f, 0f);
        b.Part("Pillow", -4.2f, 0f, 0.13f);
        b.Part("Confetti", 0f, 1.5f, 0.2f);
        goal.On("PuzzleTimerGoal", "onTimerPlaced", b.Puzzle, "Win");
        Kit.LightRing(b, "Finish Lights", 0f, 0f, 11f, 9f, 2.6f, true, 17);

        // ---- the start: the ball's dispenser at the summit, on the pistes' side of the gondola's top station ----
        Vector3 site = b.Root.transform.position;
        Vector2 down = (WinterIslePuzzle.PisteFinish(out _, out _) - WinterIslePuzzle.Summit).normalized;
        Vector2 top = WinterIslePuzzle.Summit + down * 17f - new Vector2(site.x, site.z);
        float yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg;
        var startMesh = new MeshBuilder();
        Kit.AddBox(startMesh, new Vector3(0f, 0f, 0f), new Vector3(1.4f, 0.35f, 1.4f), Stone, yaw);
        Kit.AddBox(startMesh, new Vector3(-1.6f, 0f, 0f), new Vector3(0.2f, 3f, 0.2f), Red, yaw);
        Kit.AddBox(startMesh, new Vector3(1.6f, 0f, 0f), new Vector3(0.2f, 3f, 0.2f), Red, yaw);
        b.MeshPiece("Start", startMesh.ToMesh(), snowy).Transform.position = b.WorldPoint(top.x, top.y);
        Piece spawner = b.Part("Timer Spawner", top.x, top.y, 0.35f + 0.3f, yaw);
        spawner.Set("PuzzleTimerSpawner", "time", Seconds);
    }
}
