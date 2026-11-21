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

    // where the signs stand: by the 24 puzzles nearest the plaza (their sites as last built, from their site.json),
    // a little way towards the plaza; not the far west, which no path reaches
    [System.Serializable] class SiteJson { public float x, z, yaw; }
    static List<Vector2> SignSites()
    {
        var found = new List<(float d, string name, Vector2 at)>();
        foreach (string dir in System.IO.Directory.GetDirectories(System.IO.Path.Combine(Application.dataPath, "Puzzles")))
        {
            string name = System.IO.Path.GetFileName(dir), file = System.IO.Path.Combine(dir, "site.json");
            if (!System.IO.File.Exists(file) || name == "WinterIsle" || name == "BwmTutorial" || name == "IslandGear" || name == "TheTree" || name == "AdventTrail") continue;
            var s = JsonUtility.FromJson<SiteJson>(System.IO.File.ReadAllText(file));
            var at = new Vector2(s.x, s.z);
            if (at.x < -2300f || at.x > -360f) continue;
            found.Add(((at - Plaza).magnitude, name, at));
        }
        found.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : string.CompareOrdinal(a.name, c.name));
        var sites = new List<Vector2>();
        for (int i = 0; i < found.Count && i < 24; i++) sites.Add(found[i].at);
        return sites;
    }
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
        List<Vector2> signSites = SignSites();
        for (int day = 0; day < signSites.Count; day++)
        {
            Vector2 s = signSites[day], toPlaza = (Plaza - s).normalized;
            // towards the plaza; failing that (the plaza itself, the sea, no ground) the far side, then round the site
            Vector2 at = s + toPlaza * 27f;
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f;
                Vector2 dir = new Vector2(toPlaza.x * Mathf.Cos(a) - toPlaza.y * Mathf.Sin(a), toPlaza.x * Mathf.Sin(a) + toPlaza.y * Mathf.Cos(a));
                Vector2 tryAt = s + dir * 27f;
                if ((tryAt - Plaza).magnitude < 34f || !b.Ground.At(tryAt.x, tryAt.y, out float landY) || landY < 0.5f) continue;
                at = tryAt;
                break;
            }
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
