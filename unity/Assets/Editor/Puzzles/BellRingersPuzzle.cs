// Bell Ringers (puzzle 4): bell stands far apart, each with a lever. Every lever has to be pulled within the
// same second or so; one pulled alone goes red and locks for a few seconds. The stands are too far apart to
// watch each other's hands, so the ringers have to agree a count and keep it. Three stands for three players,
// a fourth appears for four or more. No custom code: the SDK's "press together" switches do it.
using Bwm;
using UnityEngine;

public sealed class BellRingersPuzzle : PuzzleDef
{
    public override string Name => "BellRingers";
    public override Vector2 Footprint => new Vector2(30f, 30f);
    public override Vector2 Near => new Vector2(-760f, -850f);
    public override float MinDistance => 0f;
    public override float MaxDistance => 220f;

    const float Spread = 12f;
    static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color Roof = new Color(0.55f, 0.13f, 0.12f);
    static readonly Color Stone = new Color(0.50f, 0.52f, 0.56f);
    static readonly Color Brass = new Color(0.95f, 0.72f, 0.22f);

    PuzzleBuilder b;
    Material snowy, bare;

    // A roofed stand with a bell under it and the lever on its inner post; returns the lever
    Piece Stand(string name, float angle, Piece parent)
    {
        float a = angle * Mathf.Deg2Rad, x = Mathf.Sin(a) * Spread, z = Mathf.Cos(a) * Spread;
        float inward = angle + 180f;
        var frame = new MeshBuilder();
        Kit.AddBox(frame, new Vector3(-0.9f, 0f, 0f), new Vector3(0.2f, 2.5f, 0.2f), Wood);
        Kit.AddBox(frame, new Vector3(0.9f, 0f, 0f), new Vector3(0.2f, 2.5f, 0.2f), Wood);
        Kit.AddBox(frame, new Vector3(0f, 2.5f, 0f), new Vector3(2.6f, 0.16f, 1.5f), Roof);
        Piece stand = b.MeshPiece(name, frame.ToMesh(), snowy);
        stand.Transform.position = b.WorldPoint(x, z);
        stand.Transform.rotation = Quaternion.Euler(0f, inward, 0f);

        Piece bell = b.MeshPiece(name + " Bell", Kit.Ball(0.34f, 8, 12), bare, false);
        bell.Transform.position = b.WorldPoint(x, z, 1.95f);
        bell.Transform.localScale = new Vector3(1f, 1.15f, 1f);
        bell.Go.GetComponent<MeshRenderer>().sharedMaterial = b.ShaderMaterial("BWM/Snowy", "Brass", m => { m.SetFloat("_SnowLine", 2f); m.SetColor("_Tint", Brass); });

        // the lever, on the left post's face that looks at the middle
        Quaternion turn = Quaternion.Euler(0f, inward, 0f);
        Vector3 at = stand.Transform.position + turn * new Vector3(-0.9f, 1.2f, 0.1f + 0.02f);
        Piece lever = b.Part("Button Sim", 0f, 0f, 0f, inward).Named(name + " Lever");
        lever.Transform.position = at;
        lever.Set("PuzzleSimPressSwitch", "group", "bells").Set("PuzzleSimPressSwitch", "activeSeconds", 1.2f).Set("PuzzleSimPressSwitch", "timeoutSeconds", 3f);
        if (parent != null) { stand.Under(parent); bell.Under(parent); lever.Under(parent); }
        return lever;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -3.5f);
        snowy = b.ShaderMaterial("BWM/Snowy", "Snowy");
        bare = b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f));

        Piece first = Stand("Stand North", 0f, null);
        Stand("Stand South-East", 120f, null);
        Stand("Stand South-West", 240f, null);
        // Four or more players get a fourth stand to man
        Piece extra = b.Empty("Fourth Stand", 0f, 0f, 0f);
        Component hider = b.AddScript(extra, "PuzzleHider");
        Sdk.Set(hider, "show2", false);
        Sdk.Set(hider, "show3", false);
        Stand("Stand South", 180f, extra);

        first.On("PuzzleSimPressSwitch", "onSuccess", b.Puzzle, "Win");      // every lever in the group fires it: wire one

        // ---- the middle: start switch, live lamp and the gourd ----
        var furniture = new MeshBuilder();
        Kit.AddBox(furniture, new Vector3(1.6f, 0f, 0f), new Vector3(0.26f, 1.5f, 0.2f), Wood);
        Kit.AddBox(furniture, new Vector3(-1.2f, 0f, 0f), new Vector3(1.1f, 0.45f, 1.1f), Stone);
        b.MeshPiece("Furniture", furniture.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        b.Part("Switch Start", 1.6f, -0.1f - 0.073f, 1.25f, 180f);
        b.Part("Live Light", 1.6f, -0.1f, 1.42f, 180f);
        b.Part("Gourd Vice", -1.2f, 0f, 0.45f, 180f);
        b.Part("Pillow", -2.8f, 0f, 0.13f);
        b.Part("Confetti", 0f, 1.2f, 0.2f);

        Kit.LightRing(b, "Site Lights", 0f, 0f, 9f, 8f, 2.6f, true, 13);
    }
}
