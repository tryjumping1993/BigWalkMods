// Slalom (a ski game on the great mountain): a race through the gates on the left-hand run, on skis or a board.
// Passing the start gate starts the clock; each gate passed lights a dot on the bar, and the finish gate the last
// one. The whole bar before the clock runs out wins it; out of time, the bar empties and it is back up to the start.
// Riders get their gear from the racks at the gondola's top station. No custom code: presence sensors as gates, a
// timer and a progress bar.
using Bwm;
using UnityEngine;

public sealed class SlalomPuzzle : PuzzleDef
{
    public override string Name => "Slalom";
    public override Vector2 Footprint => new Vector2(10f, 10f);
    public const int Run = 0;
    const float StartAlong = 610f, FinishAlong = 960f, Seconds = 50f;
    const int Gates = 8;
    // the start hut beside the run, above the first gate (a small flat the island levels)
    public override Vector2? FixedSite
    {
        get
        {
            Vector2 mid = WinterIslePuzzle.RunCentre(Run, StartAlong - 30f, out float half);
            Vector2 dir = (WinterIslePuzzle.RunCentre(Run, StartAlong - 24f, out _) - mid).normalized;
            return mid + new Vector2(dir.y, -dir.x) * (half + 9f);
        }
    }

    PuzzleBuilder b;
    Vector3 site;

    // a gate across the run at 'along', 'offset' of the half-width off its middle: two poles, a banner, a zone
    Piece Gate(string name, float along, float offset, Color colour, float width)
    {
        Vector2 mid = WinterIslePuzzle.RunCentre(Run, along, out float half);
        Vector2 dir = (WinterIslePuzzle.RunCentre(Run, along + 6f, out _) - mid).normalized, side = new Vector2(dir.y, -dir.x);
        Vector2 at = mid + side * (offset * half);
        float lx = at.x - site.x, lz = at.y - site.z, yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        var mb = new MeshBuilder();
        Vector2 l = IsleKit.Turn(-width / 2f, 0f, yaw), r = IsleKit.Turn(width / 2f, 0f, yaw);
        Vector3 groundL = b.WorldPoint(lx + l.x, lz + l.y) - b.WorldPoint(lx, lz), groundR = b.WorldPoint(lx + r.x, lz + r.y) - b.WorldPoint(lx, lz);
        Kit.AddBox(mb, new Vector3(l.x, groundL.y, l.y), new Vector3(0.1f, 2.2f, 0.1f), colour);
        Kit.AddBox(mb, new Vector3(r.x, groundR.y, r.y), new Vector3(0.1f, 2.2f, 0.1f), colour);
        Kit.AddBox(mb, new Vector3(0f, 1.6f, 0f), new Vector3(width, 0.5f, 0.05f), colour, yaw);
        Piece gate = b.MeshPiece(name, mb.ToMesh(), IsleKit.Bare(b), false);
        gate.Transform.position = b.WorldPoint(lx, lz);
        Piece zone = b.Part("Presence Sensor", lx, lz, 1.3f, yaw).Named(name + " Zone");
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, 3.2f, 1.6f);
        box.center = Vector3.zero;
        return zone;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        b.DebugTeleport(0f, 0f);
        b.ClearGround(10f, 10f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the hut: the start switch, the gourd, the clock and the bar on its front ----
        IsleKit.BoardOnPosts(b, "Race Board", 0f, 2f, 0f, 3f, 1.0f, 2.6f, IsleKit.Board);
        Piece timer = b.Part("Timer", -0.8f, 2f + IsleKit.FaceZ + 0.05f, 1.9f, 0f);
        timer.Set("PuzzleTimer", "duration", Seconds);
        Piece bar = b.Part("Progress Bar", 0.7f, 2f + IsleKit.FaceZ + 0.03f, 1.9f, 0f);
        bar.Set("PuzzleProgressBar", "total", Gates + 1);
        Component timerC = timer.Component("PuzzleTimer"), barC = bar.Component("PuzzleProgressBar");

        // ---- the course: the start gate, eight gates swinging side to side, the finish ----
        Piece start = Gate("Start Gate", StartAlong, 0f, IsleKit.Green, 7f);
        start.On("PuzzlePresenceSensor", "onPlayerEntered", barC, "Reset");
        start.On("PuzzlePresenceSensor", "onPlayerEntered", timerC, "ResetTimer");
        start.On("PuzzlePresenceSensor", "onPlayerEntered", timerC, "StartTimer");
        for (int g = 0; g < Gates; g++)
        {
            float along = Mathf.Lerp(StartAlong + 30f, FinishAlong - 30f, g / (Gates - 1f));
            Piece gate = Gate("Gate " + (g + 1), along, g % 2 == 0 ? -0.4f : 0.4f, g % 2 == 0 ? IsleKit.Red : new Color(0.15f, 0.35f, 0.85f), 4f);
            gate.On("PuzzlePresenceSensor", "onPlayerEntered", barC, "Increment");
        }
        Piece finish = Gate("Finish Gate", FinishAlong, 0f, IsleKit.Red, 8f);
        finish.On("PuzzlePresenceSensor", "onPlayerEntered", barC, "Increment");
        timer.On("PuzzleTimer", "onFinished", barC, "Fail");
        bar.On("PuzzleProgressBar", "onFull", timerC, "PauseTimer");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 0f, -2.5f, 180f);
    }
}
