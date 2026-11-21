// Blind Sculptor (puzzle 15, blind mask, snow): a fenced yard with three lumps of snow lying about in it and three
// rings marked on the snow where a sculpture is to stand. The sculptor wears the blindfold and goes in through the
// blind turnstile; the others lean on the fence and talk them to each lump and then to a ring. Each ring with a
// lump in it lights a third of the bar; all three wins it. A plain turnstile lets the sculptor out.
// No custom code: three zones that each want one lump, and a progress bar.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BlindSculptorPuzzle : PuzzleDef
{
    public override string Name => "BlindSculptor";
    public override Vector2 Footprint => new Vector2(26f, 26f);
    public override Vector2? FixedSite => new Vector2(-890f, -1520f);      // chosen on the island's lowland; its build levels and clears it

    const float Yard = 8f;
    static readonly Vector2[] Lumps = { new Vector2(-5.5f, 4.5f), new Vector2(6f, -2f), new Vector2(-2f, -5.8f) };
    static readonly Vector2[] Rings = { new Vector2(-3f, 1.5f), new Vector2(0f, 3.5f), new Vector2(3f, 1.5f) };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -12f);
        b.ClearGround(26f, 26f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the yard: a fence a little over waist high (the talkers lean on it), turnstiles in and out ----
        var yard = new MeshBuilder();
        for (float t = -Yard; t <= Yard + 0.01f; t += 2f)
        {
            foreach (float e in new[] { -Yard, Yard })
            {
                if (!(e < 0f && Mathf.Abs(t) < 1.5f)) Kit.AddBox(yard, new Vector3(t, 0f, e), new Vector3(0.14f, 1.3f, 0.14f), IsleKit.DarkWood);
                if (!(e > 0f && Mathf.Abs(t) < 1.5f)) Kit.AddBox(yard, new Vector3(e, 0f, t), new Vector3(0.14f, 1.3f, 0.14f), IsleKit.DarkWood);
            }
        }
        foreach (float h in new[] { 0.5f, 1.15f })
        {
            Kit.AddBox(yard, new Vector3(-(Yard + 1.1f) / 2f, h, -Yard), new Vector3(Yard - 1.1f, 0.12f, 0.08f), IsleKit.Wood);      // south, either side of the turnstile
            Kit.AddBox(yard, new Vector3((Yard + 1.1f) / 2f, h, -Yard), new Vector3(Yard - 1.1f, 0.12f, 0.08f), IsleKit.Wood);
            Kit.AddBox(yard, new Vector3(0f, h, Yard), new Vector3(Yard * 2f, 0.12f, 0.08f), IsleKit.Wood);
            Kit.AddBox(yard, new Vector3(-Yard, h, 0f), new Vector3(0.08f, 0.12f, Yard * 2f), IsleKit.Wood);
            Kit.AddBox(yard, new Vector3(Yard, h, -(Yard + 1.1f) / 2f), new Vector3(0.08f, 0.12f, Yard - 1.1f), IsleKit.Wood);      // east, either side of the way out
            Kit.AddBox(yard, new Vector3(Yard, h, (Yard + 1.1f) / 2f), new Vector3(0.08f, 0.12f, Yard - 1.1f), IsleKit.Wood);
        }
        // the rings on the snow, where the lumps are to go
        foreach (Vector2 r in Rings)
            for (int k = 0; k < 12; k++)
            {
                float a = 2f * Mathf.PI * k / 12f;
                Kit.AddBox(yard, new Vector3(r.x + Mathf.Cos(a) * 0.8f, 0f, r.y + Mathf.Sin(a) * 0.8f), new Vector3(0.4f, 0.1f, 0.14f), IsleKit.Red, -a * Mathf.Rad2Deg);
            }
        b.MeshPiece("Yard", yard.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        IsleKit.BlindGate(b, 0f, -Yard, 0f);
        b.Part("Gate No items", Yard, 0f, 0f, 90f).Named("Way Out").Set("PuzzleNoItemsGate", "oneWay", true);
        IsleKit.BlindfoldHook(b, -3f, -Yard - 3f, 0f);

        // ---- the lumps, and a zone in each ring that wants one ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var lumps = new List<Piece>();
        for (int i = 0; i < Lumps.Length; i++)
            lumps.Add(b.Item("Lump " + (i + 1), Kit.Ball(0.32f, 10, 14, 0.18f, 1500 + i), snow, Lumps[i].x, Lumps[i].y, 0.3f, 2.5f));
        IsleKit.BoardOnPosts(b, "Count Board", 0f, Yard + 2.5f, 180f, 2.6f, 1.4f, 2.4f, IsleKit.Board);
        Piece bar = b.Part("Progress Bar", 0f, Yard + 2.5f - IsleKit.FaceZ - 0.03f, 1.9f, 180f);
        bar.Set("PuzzleProgressBar", "total", 3);
        Component barC = bar.Component("PuzzleProgressBar");
        for (int i = 0; i < Rings.Length; i++)
        {
            Piece zone = IsleKit.Sensor(b, "Ring " + (i + 1), Rings[i].x, Rings[i].y, new Vector3(1.5f, 1.4f, 1.5f));
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            foreach (Piece lump in lumps) zone.Add("PuzzlePresenceSensor", "acceptedCustom", lump.Component("PuzzleItem"));
            zone.On("PuzzlePresenceSensor", "onPressed", barC, "Increment");
            zone.On("PuzzlePresenceSensor", "onReleased", barC, "Decrement");
        }
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", barC, "Reset");

        IsleKit.StartCorner(b, 6f, -Yard - 3.5f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 24f, 24f, 2.6f, true, 151);
    }
}
