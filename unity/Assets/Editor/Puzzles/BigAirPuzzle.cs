// Big Air (a snowboard and ski game on the great mountain): a kicker of packed snow on the right-hand run, and over
// the landing a ring of flags hanging in the air. Ride up the kicker fast enough and the jump carries you through
// the ring: three times through it fills the bar and wins it. Too slow and you land short, under it. The kicker is
// part of the pistes' sheet, so Winter Walk's riding carries players up it and into the air off its lip.
// No custom code in the pack: a presence sensor up in the air, and a progress bar.
using Bwm;
using UnityEngine;

public sealed class BigAirPuzzle : PuzzleDef
{
    public override string Name => "BigAir";
    public override Vector2 Footprint => new Vector2(10f, 10f);
    public const int Run = 2;
    const float KickerAlong = 1120f, KickerLen = 8f, Lip = 1.9f, Width = 5f;
    public override Vector2? FixedSite
    {
        get
        {
            Vector2 mid = WinterIslePuzzle.RunCentre(Run, KickerAlong - 10f, out float half);
            Vector2 dir = (WinterIslePuzzle.RunCentre(Run, KickerAlong - 4f, out _) - mid).normalized;
            return mid + new Vector2(dir.y, -dir.x) * (half + 9f);
        }
    }

    public override void Build(PuzzleBuilder b)
    {
        Vector3 site = b.Root.transform.position;
        b.DebugTeleport(0f, 0f);
        b.ClearGround(10f, 10f);
        Material snowy = IsleKit.Snowy(b);

        Vector2 Along(float along) => WinterIslePuzzle.RunCentre(Run, along, out _);
        Vector3 Local(Vector2 world, float height) => b.WorldPoint(world.x - site.x, world.y - site.z, height) - site;

        // ---- the kicker: a wedge of snow up the run's middle, its top rising to the lip; its collider is named as
        //      the pistes' sheet so riders ride it ----
        Vector2 foot = Along(KickerAlong), lip = Along(KickerAlong + KickerLen);
        Vector2 dir = (lip - foot).normalized, side = new Vector2(dir.y, -dir.x);
        var kicker = new MeshBuilder();
        var white = new Color(0.94f, 0.96f, 1f);
        const int Steps = 8;
        for (int s = 0; s < Steps; s++)
        {
            float t0 = (float)s / Steps, t1 = (float)(s + 1) / Steps;
            float h0 = Lip * t0 * t0 + 0.02f, h1 = Lip * t1 * t1 + 0.02f;      // curving up, steepest at the lip
            Vector2 a = Vector2.Lerp(foot, lip, t0), c = Vector2.Lerp(foot, lip, t1);
            Vector3 aL = Local(a - side * Width / 2f, h0), aR = Local(a + side * Width / 2f, h0), cL = Local(c - side * Width / 2f, h1), cR = Local(c + side * Width / 2f, h1);
            Vector3 aLg = Local(a - side * Width / 2f, -0.3f), aRg = Local(a + side * Width / 2f, -0.3f), cLg = Local(c - side * Width / 2f, -0.3f), cRg = Local(c + side * Width / 2f, -0.3f);
            kicker.Quad(aL, cL, cR, aR, white); kicker.Quad(aL, aR, cR, cL, white);      // the riding surface (both ways)
            kicker.Quad(aLg, cLg, cL, aL, white); kicker.Quad(aRg, aR, cR, cRg, white);   // its sides
        }
        Vector3 lL = Local(lip - side * Width / 2f, Lip + 0.02f), lR = Local(lip + side * Width / 2f, Lip + 0.02f);
        kicker.Quad(Local(lip - side * Width / 2f, -0.3f), lL, lR, Local(lip + side * Width / 2f, -0.3f), white);      // the lip's face
        Piece kickerPiece = b.MeshPiece("Piste Sheet", kicker.ToMesh(), snowy, true);
        kickerPiece.Transform.position = site;

        // ---- the ring in the air over the landing: flags on a frame, and the zone inside it ----
        Vector2 ringAt = Along(KickerAlong + KickerLen + 8f);
        float ringYaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        var ring = new MeshBuilder();
        Vector3 centre = Local(ringAt, 2.8f);
        for (int k = 0; k < 20; k++)
        {
            float a = 2f * Mathf.PI * k / 20f;
            Vector2 across = IsleKit.Turn(Mathf.Cos(a) * 3.2f, 0f, ringYaw);
            Kit.AddBox(ring, centre + new Vector3(across.x, Mathf.Sin(a) * 1.8f, across.y), new Vector3(0.3f, 0.4f, 0.05f), k % 2 == 0 ? IsleKit.Red : IsleKit.Gold, ringYaw);
        }
        foreach (float sgn in new[] { -1f, 1f })      // the frame's legs, out beside the landing
        {
            Vector2 legAt = ringAt + side * (sgn * 4.4f);
            Vector3 legFoot = Local(legAt, 0f);
            Kit.AddBox(ring, legFoot, new Vector3(0.2f, centre.y - legFoot.y + 2.2f, 0.2f), IsleKit.DarkWood);
            Kit.AddBox(ring, new Vector3((legFoot.x + centre.x) / 2f, centre.y + 2.0f, (legFoot.z + centre.z) / 2f), new Vector3(4.6f, 0.15f, 0.15f), IsleKit.DarkWood, ringYaw);
        }
        b.MeshPiece("Ring", ring.ToMesh(), IsleKit.Bare(b), false).Transform.position = site;
        Piece zone = b.Part("Presence Sensor", ringAt.x - site.x, ringAt.y - site.z, 2.8f, ringYaw).Named("Through The Ring");
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = new Vector3(6f, 3.4f, 2.4f);
        box.center = Vector3.zero;

        // ---- the hut beside the run: the bar, the start switch and the gourd ----
        IsleKit.BoardOnPosts(b, "Air Board", 0f, 2f, 0f, 2.6f, 1.2f, 2.6f, IsleKit.Board);
        Piece bar = b.Part("Progress Bar", 0f, 2f + IsleKit.FaceZ + 0.03f, 1.9f, 0f);
        bar.Set("PuzzleProgressBar", "total", 3);
        zone.On("PuzzlePresenceSensor", "onPlayerEntered", bar.Component("PuzzleProgressBar"), "Increment");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");
        IsleKit.StartCorner(b, 0f, -2.5f, 180f);
    }
}
