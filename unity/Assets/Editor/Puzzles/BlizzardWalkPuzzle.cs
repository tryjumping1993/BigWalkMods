// Blizzard Walk (puzzle 11, blind mask): a narrow plank walk zigzags a metre and a half over a pit of soft snow.
// The walker puts on the blindfold from the hook and goes through the blind turnstile onto it; the others stand at
// the pit's edge and talk them along: "stop... turn right... three steps". At the far end a pad under the arch wins
// it. A step off the planks is a soft fall into the snow, and steps lead back to the start: nobody gets stuck, and
// nothing is near the sea. No custom code.
using Bwm;
using UnityEngine;

public sealed class BlizzardWalkPuzzle : PuzzleDef
{
    public override string Name => "BlizzardWalk";
    public override Vector2 Footprint => new Vector2(28f, 34f);
    public override Vector2? FixedSite => new Vector2(-970f, -1070f);      // chosen on the island's lowland; its build levels and clears it

    const float Deck = 1.5f, Width = 0.75f;
    // the walk's corners, from the turnstile to the arch
    static readonly Vector2[] Path = { new Vector2(0f, -11f), new Vector2(0f, -6f), new Vector2(5f, -3f), new Vector2(5f, 2f), new Vector2(-3f, 5f), new Vector2(-3f, 9f), new Vector2(2f, 12f) };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -15f);
        b.ClearGround(28f, 34f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the pit: a low wall of snow blocks round a soft floor, the walk on posts over it ----
        var mb = new MeshBuilder();
        for (int i = 0; i + 1 < Path.Length; i++)
        {
            Vector2 a = Path[i], c = Path[i + 1], along = c - a;
            float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg, length = along.magnitude;
            Vector2 mid = (a + c) * 0.5f;
            Kit.AddBox(mb, new Vector3(mid.x, Deck - 0.12f, mid.y), new Vector3(Width, 0.12f, length + Width), IsleKit.Wood, yaw);      // planks
            for (float t = 0f; t <= 1.001f; t += 0.5f)
            {
                Vector2 q = Vector2.Lerp(a, c, t);
                Kit.AddBox(mb, new Vector3(q.x, 0f, q.y), new Vector3(0.16f, Deck - 0.12f, 0.16f), IsleKit.DarkWood);      // posts
            }
        }
        // the start platform with its turnstile and the blindfold's hook, and the steps up to it
        Kit.AddBox(mb, new Vector3(0f, 0f, -13f), new Vector3(3f, Deck, 3f), IsleKit.Stone);
        for (int s = 1; s <= 3; s++) Kit.AddBox(mb, new Vector3(0f, 0f, -14.5f - s * 0.5f), new Vector3(1.6f, Deck * (1f - s / 4f), 0.5f), IsleKit.Stone);
        // the far platform and the arch
        Kit.AddBox(mb, new Vector3(3f, 0f, 13.5f), new Vector3(3.2f, Deck, 3.2f), IsleKit.Stone);
        Kit.AddBox(mb, new Vector3(1.6f, Deck, 13.5f), new Vector3(0.22f, 2.6f, 0.22f), IsleKit.Red);
        Kit.AddBox(mb, new Vector3(4.4f, Deck, 13.5f), new Vector3(0.22f, 2.6f, 0.22f), IsleKit.Red);
        Kit.AddBox(mb, new Vector3(3f, Deck + 2.6f, 13.5f), new Vector3(3.2f, 0.25f, 0.3f), IsleKit.Red);
        for (int s = 1; s <= 3; s++) Kit.AddBox(mb, new Vector3(5.1f + s * 0.5f, 0f, 13.5f), new Vector3(0.5f, Deck * (1f - s / 4f), 1.6f), IsleKit.Stone);
        // the pit's rim: a low wall to stand at, with gaps to walk out of the pit
        for (int i = 0; i < 28; i++)
        {
            float a = 2f * Mathf.PI * i / 28f;
            if (i == 7 || i == 21) continue;      // gaps
            Vector2 q = new Vector2(Mathf.Cos(a) * 11.5f, Mathf.Sin(a) * 13.5f);
            Kit.AddBox(mb, new Vector3(q.x, 0f, q.y), new Vector3(2.2f, 0.7f, 0.6f), IsleKit.Cream, -a * Mathf.Rad2Deg + 90f);
        }
        b.MeshPiece("Walk", mb.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        // soft snow in the pit
        b.MeshPiece("Pit Snow", Kit.Mound(10.5f, 0.35f, 1101, 10, 28), b.ShaderMaterial("BWM/Snowy", "Drift"), true).Transform.position = b.WorldPoint(0f, 0f, -0.15f);

        // the blind turnstile at the top of the steps, one way onto the walk, and the blindfold on a hook beside it
        b.Part("Gate No items", 0f, -12f, Deck, 0f).Set("PuzzleNoItemsGate", "blindMask", true).Set("PuzzleNoItemsGate", "oneWay", true);
        var hookPost = new MeshBuilder();
        Kit.AddBox(hookPost, Vector3.zero, new Vector3(0.18f, 1.9f, 0.18f), IsleKit.Wood);
        b.MeshPiece("Hook Post", hookPost.ToMesh(), snowy).Transform.position = b.WorldPoint(-2.4f, -16f);
        Piece blindfold = b.Part("Vanilla Item", -2.4f, -16f + 0.4f, 1.3f, 0f).Named("Blindfold").Set("PuzzleVanillaItem", "itemType", 0).Set("PuzzleVanillaItem", "savesOnReload", false);
        Piece hook = b.Part("Hook", -2.4f, -16f + 0.12f, 1.55f, 0f);
        hook.Set("PuzzleHook", "defaultItem", blindfold.Component("PuzzleVanillaItem"));
        Sdk.SetFlags(hook.Component("PuzzleHook"), "acceptedVanilla", 1);      // the blindfold hangs back on it

        // the goal: a pad under the arch
        Piece goal = IsleKit.Sensor(b, "Goal", 3f, 13.5f, new Vector3(2.4f, 2.6f, 2.4f), Deck);
        goal.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 6f, -15f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 26f, 30f, 2.6f, true, 111);
    }
}
