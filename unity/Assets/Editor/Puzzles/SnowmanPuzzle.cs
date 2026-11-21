// Build a Snowman: the first snow-building puzzle, and the test site for loose snow.
//
// Three snowballs of different sizes lie half-buried in a drift. Players carry them to the marked spot and set
// them down there; snap points hold each ball where it belongs, so the three stack into a snowman. When all
// three are in the build zone the snowman gets its hat, nose and arms, the confetti fires and the gourd is
// released. No custom code: the SDK's item sensor counts the snowballs and wins the puzzle.
//
// What a person has to try: do the snowballs handle well, do the snap points take them, does the zone count
// them reliably. The finished puzzle (14 in the plan) adds a blueprint only a far player can see.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowmanPuzzle : PuzzleDef
{
    public override string Name => "Snowman";
    public override Vector2 Footprint => new Vector2(18f, 14f);
    public override Vector2? FixedSite => new Vector2(-2556f, -1542f);      // spread out from the plaza (tools/sites)

    // On the Winter Isle, on the shelf west of the Glass Booth
    public override Vector2 Near => new Vector2(-640f, -770f);
    public override float MinDistance => 0f;
    public override float MaxDistance => 150f;

    static readonly Color Coal = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color Carrot = new Color(0.95f, 0.45f, 0.1f);
    static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color HatRed = new Color(0.75f, 0.1f, 0.12f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -5.5f);
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");

        // ---- the drift, with the three snowballs resting in it ----
        b.MeshPiece("Drift", Kit.Mound(2.4f, 1.0f, 3), snow).Transform.position = b.WorldPoint(-5f, 0.5f);
        float[] radius = { 0.46f, 0.34f, 0.24f };
        string[] names = { "Snowball Large", "Snowball Medium", "Snowball Small" };
        var balls = new List<Piece>();
        Vector2[] rest = { new Vector2(-4.1f, -0.9f), new Vector2(-5.9f, -0.6f), new Vector2(-4.8f, 1.9f) };
        for (int i = 0; i < 3; i++)
            balls.Add(b.Item(names[i], Kit.Ball(radius[i], 10, 14, 0.12f, i + 1), snow, rest[i].x, rest[i].y, radius[i] * 0.8f, 3f - i));

        // ---- the build spot: a packed-snow disc, and a snap point for each ball, stacked ----
        b.Box("Build Spot", 4f, 0f, new Vector3(2.2f, 0.12f, 2.2f), new Color(0.78f, 0.84f, 0.93f));
        float[] centre = { 0.12f + radius[0], 0.12f + radius[0] * 2f + radius[1] * 0.85f, 0.12f + radius[0] * 2f + radius[1] * 1.7f + radius[2] * 0.85f };
        for (int i = 0; i < 3; i++)
        {
            Piece hook = b.Part("Hook", 4f, 0f, centre[i]).Named("Snap " + names[i]);
            hook.Set("PuzzleHook", "hookType", 2);                      // Invisible
            Sdk.Set(hook.Component("PuzzleHook"), "acceptedVanilla", 0);
            hook.Add("PuzzleHook", "acceptedCustom", balls[i].Go.GetComponent(Sdk.T("PuzzleItem")));
        }

        // ---- trimmings that appear when the snowman is complete ----
        Piece trim = b.Empty("Trimmings", 4f, 0f, 0f);
        float head = centre[2];
        b.Box("Hat Brim", 4f, 0f, new Vector3(0.5f, 0.04f, 0.5f), Coal, head + radius[2] * 0.75f).Under(trim);
        b.Box("Hat", 4f, 0f, new Vector3(0.32f, 0.3f, 0.32f), Coal, head + radius[2] * 0.75f + 0.04f).Under(trim);
        b.Box("Hat Band", 4f, 0f, new Vector3(0.34f, 0.07f, 0.34f), HatRed, head + radius[2] * 0.75f + 0.05f).Under(trim);
        b.Box("Nose", 4f, -radius[2] - 0.08f, new Vector3(0.06f, 0.06f, 0.26f), Carrot, head - 0.03f).Under(trim);
        b.Box("Arm Left", 4f - radius[1] - 0.3f, 0f, new Vector3(0.7f, 0.05f, 0.05f), Wood, centre[1] + 0.05f).Under(trim);
        b.Box("Arm Right", 4f + radius[1] + 0.3f, 0f, new Vector3(0.7f, 0.05f, 0.05f), Wood, centre[1] + 0.05f).Under(trim);
        trim.Go.SetActive(false);

        // ---- the zone that counts snowballs: all three inside wins ----
        Piece zone = b.Part("Presence Sensor", 4f, 0f, 1.3f).Named("Build Zone");
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = new Vector3(2.4f, 2.8f, 2.4f);
        box.center = Vector3.zero;
        zone.Set("PuzzlePresenceSensor", "type", 2);                    // Specific Item
        zone.Set("PuzzlePresenceSensor", "requiredPlayers", 3);         // for item types: items needed inside
        foreach (Piece ball in balls) zone.Add("PuzzlePresenceSensor", "acceptedCustom", ball.Go.GetComponent(Sdk.T("PuzzleItem")));
        zone.On("PuzzlePresenceSensor", "onPressed", trim.Go, "SetActive", true);
        zone.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", trim.Go, "SetActive", false);
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", trim.Go, "SetActive", false);

        // ---- the usual parts, on a post and a plinth ----
        b.Box("Switch Post", -1f, -3f, new Vector3(0.25f, 1.45f, 0.18f), Wood);
        b.Part("Switch Start", -1f, -3.1f - 0.073f, 1.25f, 180f);
        b.Part("Live Light", -1f, -3.09f, 1.38f, 180f);
        b.Box("Plinth", 0.5f, 3.5f, new Vector3(1.1f, 0.45f, 1.1f), new Color(0.72f, 0.70f, 0.66f));
        b.Part("Gourd Vice", 0.5f, 3.5f, 0.45f, 180f);
        b.Part("Pillow", 2.2f, 3.5f, 0.13f);
        b.Part("Confetti", 4f, 1.6f, 0.2f);

        // ---- Christmas lights round the site ----
        Kit.LightRing(b, "Site Lights", 0f, 0f, 15f, 10.5f, 2.6f, true, 7);
    }
}
