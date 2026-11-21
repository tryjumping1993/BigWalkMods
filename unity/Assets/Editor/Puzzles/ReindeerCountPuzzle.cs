// Reindeer Count (puzzle 6): a fenced snowfield with reindeer standing about in it, among tall screens of packed
// snow. From the lookout on the west side some are hidden behind the screens, from the lookout on the east others;
// every one can be seen from one lookout or the other, some from both. The counter is by the start: set it to how
// many reindeer there are and press check. Two lookouts and a counter need three people, and the two counts have to
// be put together without counting a reindeer twice (each wears a coloured collar to tell them apart).
// No custom code: the answer is set when the puzzle starts, by the start event itself.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class ReindeerCountPuzzle : PuzzleDef
{
    public override string Name => "ReindeerCount";
    public override Vector2 Footprint => new Vector2(38f, 36f);
    public override Vector2? FixedSite => new Vector2(-1580f, -710f);      // chosen on the island's lowland; its build levels and clears it

    const int Reindeer = 13;
    const float FieldX = 11f, FieldZ = 12f, ScreenH = 3.4f, LookoutUp = 2.6f;
    static readonly Vector2 West = new Vector2(-16f, 0f), East = new Vector2(16f, 0f);

    static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
        return Cross(a, b, c) * Cross(a, b, d) < 0f && Cross(c, d, a) * Cross(c, d, b) < 0f;
    }

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -16f);
        b.ClearGround(38f, 36f);
        Material snowy = IsleKit.Snowy(b);
        var random = new System.Random(2512);
        float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();

        // ---- the screens: walls of packed snow standing about the field ----
        var screens = new List<(Vector2 a, Vector2 b)>();
        var mb = new MeshBuilder();
        var snowBlock = new Color(0.92f, 0.94f, 0.98f);
        while (screens.Count < 7)
        {
            var mid = new Vector2(R(-FieldX + 2f, FieldX - 2f), R(-FieldZ + 2f, FieldZ - 2f));
            float yaw = R(-35f, 35f), len = R(3f, 4.5f);
            Vector2 dir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)) * (len / 2f);      // mostly running north-south, so they hide from one side
            bool clear = true;
            foreach (var s in screens) if (((s.a + s.b) / 2f - mid).magnitude < 4f) clear = false;
            if (!clear) continue;
            screens.Add((mid - dir, mid + dir));
            Kit.AddBox(mb, new Vector3(mid.x, 0f, mid.y), new Vector3(0.6f, ScreenH, len), snowBlock, yaw);
        }
        // ---- the reindeer: each where at least one lookout can see it ----
        Color[] collars = { IsleKit.Red, IsleKit.Green, IsleKit.Gold, new Color(0.15f, 0.35f, 0.85f), new Color(0.6f, 0.15f, 0.6f) };
        var brown = new Color(0.45f, 0.3f, 0.18f);
        var placed = new List<Vector2>();
        int tries = 0;
        while (placed.Count < Reindeer && tries++ < 5000)
        {
            var p = new Vector2(R(-FieldX + 1f, FieldX - 1f), R(-FieldZ + 1f, FieldZ - 1f));
            bool ok = true;
            foreach (Vector2 q in placed) if ((q - p).magnitude < 2.6f) ok = false;
            foreach (var s in screens) if (Vector2.Distance(p, (s.a + s.b) / 2f) < 2.4f) ok = false;
            if (!ok) continue;
            bool fromWest = true, fromEast = true;
            foreach (var s in screens) { if (Crosses(West, p, s.a, s.b)) fromWest = false; if (Crosses(East, p, s.a, s.b)) fromEast = false; }
            if (!fromWest && !fromEast) continue;
            placed.Add(p);
            float yaw = R(0f, 360f);
            Quaternion t = Quaternion.Euler(0f, yaw, 0f);
            Vector3 at = new Vector3(p.x, 0f, p.y);
            Kit.AddBox(mb, at + t * new Vector3(0f, 0.75f, 0f), new Vector3(0.45f, 0.5f, 1.2f), brown, yaw);      // body
            foreach (var leg in new[] { new Vector3(-0.15f, 0f, 0.45f), new Vector3(0.15f, 0f, 0.45f), new Vector3(-0.15f, 0f, -0.45f), new Vector3(0.15f, 0f, -0.45f) })
                Kit.AddBox(mb, at + t * leg, new Vector3(0.1f, 0.78f, 0.1f), brown * 0.8f, yaw);
            Kit.AddBox(mb, at + t * new Vector3(0f, 1.1f, 0.6f), new Vector3(0.18f, 0.5f, 0.18f), brown, yaw);       // neck
            Kit.AddBox(mb, at + t * new Vector3(0f, 1.5f, 0.75f), new Vector3(0.22f, 0.24f, 0.4f), brown, yaw);      // head
            Kit.AddBox(mb, at + t * new Vector3(0f, 1.38f, 0.66f), new Vector3(0.26f, 0.1f, 0.26f), collars[placed.Count % collars.Length], yaw);      // collar
            Kit.AddBox(mb, at + t * new Vector3(-0.16f, 1.72f, 0.65f), new Vector3(0.05f, 0.45f, 0.05f), IsleKit.Cream, yaw);      // antlers
            Kit.AddBox(mb, at + t * new Vector3(0.16f, 1.72f, 0.65f), new Vector3(0.05f, 0.45f, 0.05f), IsleKit.Cream, yaw);
        }
        if (placed.Count != Reindeer) throw new System.Exception("Reindeer Count: placed only " + placed.Count + " reindeer");
        // ---- the fence round the field, too high to climb in ----
        for (float x = -FieldX - 1f; x <= FieldX + 1.01f; x += 2f)
            foreach (float z in new[] { -FieldZ - 1f, FieldZ + 1f })
                Kit.AddBox(mb, new Vector3(x, 0f, z), new Vector3(0.16f, 2.4f, 0.16f), IsleKit.DarkWood);
        for (float z = -FieldZ - 1f; z <= FieldZ + 1.01f; z += 2f)
            foreach (float x in new[] { -FieldX - 1f, FieldX + 1f })
                Kit.AddBox(mb, new Vector3(x, 0f, z), new Vector3(0.16f, 2.4f, 0.16f), IsleKit.DarkWood);
        foreach (float h in new[] { 0.7f, 1.5f, 2.3f })
        {
            Kit.AddBox(mb, new Vector3(0f, h, -FieldZ - 1f), new Vector3(FieldX * 2f + 2f, 0.12f, 0.08f), IsleKit.Wood);
            Kit.AddBox(mb, new Vector3(0f, h, FieldZ + 1f), new Vector3(FieldX * 2f + 2f, 0.12f, 0.08f), IsleKit.Wood);
            Kit.AddBox(mb, new Vector3(-FieldX - 1f, h, 0f), new Vector3(0.08f, 0.12f, FieldZ * 2f + 2f), IsleKit.Wood);
            Kit.AddBox(mb, new Vector3(FieldX + 1f, h, 0f), new Vector3(0.08f, 0.12f, FieldZ * 2f + 2f), IsleKit.Wood);
        }
        // ---- the two lookouts, platforms with steps, outside the fence on the west and east ----
        foreach (Vector2 l in new[] { West, East })
        {
            Kit.AddBox(mb, new Vector3(l.x, 0f, l.y), new Vector3(2.6f, LookoutUp, 2.6f), IsleKit.Stone);
            float outward = Mathf.Sign(l.x);
            for (int s = 1; s <= 6; s++)
                Kit.AddBox(mb, new Vector3(l.x + outward * (1.3f + s * 0.45f - 0.2f), 0f, l.y), new Vector3(0.45f, LookoutUp * (1f - s / 7f), 1.4f), IsleKit.Stone);
            Kit.AddBox(mb, new Vector3(l.x - outward * 1.25f, LookoutUp, l.y), new Vector3(0.1f, 1f, 2.6f), IsleKit.Wood);
        }
        b.MeshPiece("Field", mb.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the counter, its check screen and button, by the start ----
        IsleKit.BoardOnPosts(b, "Counter Board", 0f, -16.5f, 0f, 3.4f, 0.6f, 2.4f, IsleKit.Board);
        Piece input = b.Part("Count Input", -0.7f, -16.5f + IsleKit.FaceZ + 0.05f, 1.4f, 0f);
        Piece check = b.Part("Count Validator", 0.8f, -16.5f + IsleKit.FaceZ + 0.05f, 1.8f, 0f);
        Piece poke = b.Part("Button Poke", 0.8f, -16.5f + IsleKit.FaceZ + 0.05f, 1.15f, 0f);
        Component checkC = check.Component("PuzzleCountValidator");
        Sdk.Set(checkC, "input", input.Component("PuzzleCountInput"));
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", checkC, "SetAnswer", Reindeer);

        IsleKit.StartCorner(b, -6f, -16f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 36f, 34f, 2.6f, true, 61);
    }
}
