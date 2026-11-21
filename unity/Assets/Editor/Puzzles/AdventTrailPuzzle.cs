// Advent Trail (puzzle 23): twenty-four little signs, numbered for the days of Advent, stand across the whole island,
// one by every other puzzle, each with a picture on top: a bell, a star, a candle or a tree. By the plaza a board
// asks how many of them carry a bell. Nobody counts that in one walk: it is noticed over the whole game, or a team
// splits up and sweeps the island. No custom code: the answer is set when the puzzle starts.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class AdventTrailPuzzle : PuzzleDef
{
    public override string Name => "AdventTrail";
    public override Vector2 Footprint => new Vector2(16f, 14f);
    public override Vector2? FixedSite => new Vector2(-690f, -720f);      // chosen on the island's lowland; its build levels and clears it

    // where the signs stand: by every other puzzle on the island (their sites), a little way towards the plaza
    static readonly Vector2[] Sites =
    {
        new Vector2(-583f, -756f), new Vector2(-605f, -816f), new Vector2(-640f, -770f), new Vector2(-761f, -890f), new Vector2(-950f, -1459f),
        new Vector2(-630f, -900f), new Vector2(-520f, -960f), new Vector2(-500f, -860f), new Vector2(-520f, -790f), new Vector2(-760f, -730f),
        new Vector2(-850f, -830f), new Vector2(-890f, -650f), new Vector2(-1060f, -950f), new Vector2(-970f, -1070f), new Vector2(-750f, -1030f),
        new Vector2(-870f, -1070f), new Vector2(-720f, -1120f), new Vector2(-880f, -1180f), new Vector2(-750f, -1190f), new Vector2(-700f, -1250f),
        new Vector2(-770f, -1270f), new Vector2(-860f, -1270f), new Vector2(-610f, -1270f), new Vector2(-780f, -800f),
    };
    // the picture on each day's sign: 0 bell, 1 star, 2 candle, 3 tree (seven bells)
    static readonly int[] Picture = { 1, 0, 3, 2, 0, 1, 3, 0, 2, 1, 3, 0, 1, 2, 3, 0, 1, 3, 2, 0, 1, 2, 0, 3 };
    static readonly Vector2 Plaza = new Vector2(-705f, -795f);

    static void Topper(MeshBuilder mb, int which, Vector3 at)
    {
        if (which == 0) { Kit.AddBall(mb, at, 0.16f, IsleKit.Gold); Kit.AddBox(mb, at + Vector3.down * 0.2f, new Vector3(0.36f, 0.06f, 0.06f), IsleKit.Gold); }
        else if (which == 1) Kit.AddStar(mb, at, 0.22f, 0.04f, IsleKit.Gold);
        else if (which == 2) { Kit.AddBox(mb, at + Vector3.down * 0.22f, new Vector3(0.1f, 0.3f, 0.1f), IsleKit.Cream); Kit.AddBall(mb, at + Vector3.up * 0.14f, 0.05f, new Color(1f, 0.7f, 0.2f)); }
        else for (int t = 0; t < 3; t++) Kit.AddBox(mb, at + Vector3.down * (0.22f - t * 0.13f), new Vector3(0.36f - t * 0.1f, 0.13f, 0.06f), IsleKit.Green);
    }

    static void SetText(Piece sign, string text)
    {
        var tmp = sign.Go.GetComponentInChildren<TMPro.TextMeshPro>(true);
        if (tmp == null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(sign.Go.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(1.2f, 0.5f);
        }
        tmp.text = text;
        tmp.fontSize = 3f;
        tmp.color = new Color(0.25f, 0.12f, 0.08f);
    }

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -5f);
        b.ClearGround(16f, 14f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        Vector3 site = b.Root.transform.position;

        // ---- the twenty-four signs, out across the island ----
        int bells = 0;
        var toppers = new MeshBuilder();
        for (int day = 0; day < Sites.Length; day++)
        {
            Vector2 s = Sites[day], toPlaza = (Plaza - s).normalized;
            Vector2 at = s + toPlaza * 27f;
            if ((at - Plaza).magnitude < 34f) at = s - toPlaza * 27f;      // (not on the plaza: the far side instead)
            if (!b.Ground.At(at.x, at.y, out float landY) || landY < 0.5f) at = s - toPlaza * 27f;      // (nor in the sea)
            if (!b.Ground.At(at.x, at.y, out landY) || landY < 0.5f) at = s + new Vector2(toPlaza.y, -toPlaza.x) * 27f;      // (nor in the sea that way either: beside the site)
            float lx = at.x - site.x, lz = at.y - site.z, yaw = Mathf.Atan2(-toPlaza.x, -toPlaza.y) * Mathf.Rad2Deg + 180f;
            Piece sign = b.Part("Sign", lx, lz, 1.2f, yaw).Named("Advent " + (day + 1));
            SetText(sign, "Day " + (day + 1));
            var post = new MeshBuilder();
            Kit.AddBox(post, Vector3.zero, new Vector3(0.12f, 1.0f, 0.12f), IsleKit.Wood);
            b.MeshPiece("Advent Post " + (day + 1), post.ToMesh(), snowy).Transform.position = b.WorldPoint(lx, lz);
            Vector3 top = b.WorldPoint(lx, lz, 1.95f) - site;
            Topper(toppers, Picture[day], top);
            if (Picture[day] == 0) bells++;
        }
        b.MeshPiece("Advent Pictures", toppers.ToMesh(), bare, false).Transform.position = site;

        // ---- the question, by the plaza: a counter, its check screen and button ----
        IsleKit.BoardOnPosts(b, "Question Board", 0f, 2f, 180f, 3.6f, 0.6f, 2.8f, IsleKit.Board);
        Piece question = b.Part("Sign", 0f, 2f - IsleKit.FaceZ - 0.06f, 2.45f, 180f).Named("Question");
        SetText(question, "How many bells?");
        Piece input = b.Part("Count Input", -0.8f, 2f - IsleKit.FaceZ - 0.05f, 1.4f, 180f);
        Piece check = b.Part("Count Validator", 0.8f, 2f - IsleKit.FaceZ - 0.05f, 1.75f, 180f);
        Piece poke = b.Part("Button Poke", 0.8f, 2f - IsleKit.FaceZ - 0.05f, 1.1f, 180f);
        Component checkC = check.Component("PuzzleCountValidator");
        Sdk.Set(checkC, "input", input.Component("PuzzleCountInput"));
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", checkC, "SetAnswer", bells);

        IsleKit.StartCorner(b, 0f, -4f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 14f, 12f, 2.6f, true, 231);
    }
}
