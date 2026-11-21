// Snow Bridge (puzzle 2, snow): two high banks of packed snow with a stone pillar between them, the gourd on the
// far bank, and gaps too wide to jump. On the near bank lie the snowballs. Three of them packed into the hollow at
// the near bank's edge raise the first span, to the pillar; three more carried over it and packed into the hollow
// on the pillar raise the second, to the far bank. Falling in is a three-metre drop into soft snow and a walk back
// round to the steps. No custom code: zones that count snowballs switch the spans on.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowBridgePuzzle : PuzzleDef
{
    public override string Name => "SnowBridge";
    public override Vector2 Footprint => new Vector2(22f, 34f);
    public override Vector2? FixedSite => new Vector2(-860f, -1270f);      // chosen on the island's lowland; its build levels and clears it

    const float Up = 3f, Gap = 4.2f, BankLen = 8f, Pillar = 2.4f, BankW = 6f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -16f);
        b.ClearGround(22f, 34f);
        Material snowy = IsleKit.Snowy(b);
        var snowBlock = new Color(0.92f, 0.94f, 0.98f);

        // ---- the banks and the pillar, steps up to the near bank and down from the far one ----
        float nearEdge = -Pillar / 2f - Gap, farEdge = Pillar / 2f + Gap;
        var banks = new MeshBuilder();
        Kit.AddBox(banks, new Vector3(0f, 0f, nearEdge - BankLen / 2f), new Vector3(BankW, Up, BankLen), snowBlock);
        Kit.AddBox(banks, new Vector3(0f, 0f, farEdge + BankLen / 2f), new Vector3(BankW, Up, BankLen), snowBlock);
        Kit.AddBox(banks, Vector3.zero, new Vector3(Pillar, Up, Pillar), IsleKit.Stone);
        for (int s = 1; s <= 7; s++)
        {
            Kit.AddBox(banks, new Vector3(BankW / 2f + s * 0.45f - 0.2f, 0f, nearEdge - BankLen + 1.2f), new Vector3(0.45f, Up * (1f - s / 8f), 1.6f), IsleKit.Stone);
            Kit.AddBox(banks, new Vector3(BankW / 2f + s * 0.45f - 0.2f, 0f, farEdge + BankLen - 1.2f), new Vector3(0.45f, Up * (1f - s / 8f), 1.6f), IsleKit.Stone);
        }
        // the hollows the snowballs go in: a low ring at the near bank's edge and on the pillar
        foreach (var at in new[] { new Vector3(0f, Up, nearEdge - 1.1f), new Vector3(0f, Up, 0f) })
            for (int k = 0; k < 10; k++)
            {
                float a = 2f * Mathf.PI * k / 10f;
                Kit.AddBox(banks, at + new Vector3(Mathf.Cos(a) * 0.75f, 0f, Mathf.Sin(a) * 0.75f), new Vector3(0.36f, 0.18f, 0.14f), IsleKit.Cream, -a * Mathf.Rad2Deg);
            }
        b.MeshPiece("Banks", banks.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        b.MeshPiece("Soft Snow", Kit.Mound(6f, 0.5f, 2201, 8, 24), b.ShaderMaterial("BWM/Snowy", "Drift"), true).Transform.position = b.WorldPoint(0f, 0f, -0.2f);

        // ---- the snowballs: six on the near bank, one or two spare ----
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var balls = new List<Piece>();
        for (int i = 0; i < 8; i++)
            balls.Add(b.Item("Snowball " + (i + 1), Kit.Ball(0.3f, 10, 14, 0.12f, 2300 + i), snow, -1.8f + (i % 4) * 1.2f, nearEdge - BankLen + 2.2f + (i / 4) * 1.3f, Up + 0.3f, 2f));

        // ---- the spans: hidden until their hollow holds three snowballs ----
        Piece Span(string name, float z0, float z1)
        {
            var mb = new MeshBuilder();
            Kit.AddBox(mb, new Vector3(0f, Up - 0.25f, (z0 + z1) / 2f), new Vector3(1.8f, 0.25f, z1 - z0 + 0.6f), new Color(0.88f, 0.92f, 0.98f));
            Kit.AddBox(mb, new Vector3(-0.95f, Up, (z0 + z1) / 2f), new Vector3(0.12f, 0.45f, z1 - z0 + 0.6f), IsleKit.Cream);      // a kerb each side
            Kit.AddBox(mb, new Vector3(0.95f, Up, (z0 + z1) / 2f), new Vector3(0.12f, 0.45f, z1 - z0 + 0.6f), IsleKit.Cream);
            Piece span = b.MeshPiece(name, mb.ToMesh(), snowy);
            span.Transform.position = b.WorldPoint(0f, 0f);
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", span.Go, "SetActive", false);
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", span.Go, "SetActive", false);      // (as the Snowman's trimmings: gone until built)
            span.Go.SetActive(false);
            return span;
        }
        Piece first = Span("First Span", nearEdge, -Pillar / 2f), second = Span("Second Span", Pillar / 2f, farEdge);
        Piece Hollow(string name, float z)
        {
            Piece zone = IsleKit.Sensor(b, name, 0f, z, new Vector3(1.5f, 1.2f, 1.5f), Up);
            zone.Set("PuzzlePresenceSensor", "type", 2).Set("PuzzlePresenceSensor", "requiredPlayers", 3);
            foreach (Piece ball in balls) zone.Add("PuzzlePresenceSensor", "acceptedCustom", ball.Component("PuzzleItem"));
            return zone;
        }
        Hollow("Near Hollow", nearEdge - 1.1f).On("PuzzlePresenceSensor", "onPressed", first.Go, "SetActive", true);
        Hollow("Pillar Hollow", 0f).On("PuzzlePresenceSensor", "onPressed", second.Go, "SetActive", true);

        // ---- the gourd on the far bank, and the start by the near steps ----
        var plinth = new MeshBuilder();
        Kit.AddBox(plinth, Vector3.zero, new Vector3(1.1f, 0.45f, 1.1f), IsleKit.Stone);
        b.MeshPiece("Gourd Stone", plinth.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, farEdge + 3f, Up);
        b.Part("Gourd Vice", 0f, farEdge + 3f, Up + 0.45f, 180f);
        b.Part("Confetti", 1.6f, farEdge + 3f, Up + 0.2f, 0f);
        b.Part("Pillow", -1.6f, farEdge + 3f, Up + 0.13f, 0f);
        var startPost = new MeshBuilder();
        Kit.AddBox(startPost, Vector3.zero, new Vector3(0.26f, 1.5f, 0.2f), IsleKit.Wood);
        b.MeshPiece("Start Post", startPost.ToMesh(), snowy).Transform.position = b.WorldPoint(-5f, -15.5f);
        b.Part("Switch Start", -5f, -15.5f + 0.173f, 1.25f, 0f);
        b.Part("Live Light", -5f, -15.5f + 0.1f, 1.42f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 20f, 32f, 2.6f, true, 21);
    }
}
