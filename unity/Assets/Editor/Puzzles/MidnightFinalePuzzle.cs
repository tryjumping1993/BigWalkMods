// Midnight Finale (puzzle 24): beside the plaza, where everyone gathers at the end, a ring of firework launchers
// round a tall star, and levers on posts round the ring, far enough apart that nobody reaches two. Everyone pulls
// their lever in the same second: the fireworks go up, the confetti goes off, and the last gourd comes free.
// Three levers, four in a lobby of four or more. No custom code: the SDK's "press together" levers and the game's
// flare launchers.
using Bwm;
using UnityEngine;

public sealed class MidnightFinalePuzzle : PuzzleDef
{
    public override string Name => "MidnightFinale";
    public override Vector2 Footprint => new Vector2(28f, 28f);
    public override Vector2? FixedSite => new Vector2(-612f, -846f);      // spread out from the plaza (tools/sites: a site checker and the layout it passed)

    const float LeverRing = 11f, LauncherRing = 4f;
    static readonly Color[] Colours = { new Color(0.95f, 0.2f, 0.15f), new Color(0.2f, 0.9f, 0.3f), new Color(0.3f, 0.5f, 1f), new Color(1f, 0.85f, 0.25f), new Color(0.9f, 0.3f, 0.9f), Color.white };

    PuzzleBuilder b;

    Piece Lever(string name, float angle, Piece parent)
    {
        float a = angle * Mathf.Deg2Rad, x = Mathf.Sin(a) * LeverRing, z = Mathf.Cos(a) * LeverRing;
        float inward = angle + 180f;
        var post = new MeshBuilder();
        Kit.AddBox(post, Vector3.zero, new Vector3(0.3f, 1.6f, 0.24f), IsleKit.Red, inward);
        Kit.AddBox(post, new Vector3(0f, 1.6f, 0f), new Vector3(0.5f, 0.1f, 0.4f), IsleKit.Gold, inward);
        Piece postPiece = b.MeshPiece(name, post.ToMesh(), IsleKit.Snowy(b));
        postPiece.Transform.position = b.WorldPoint(x, z);
        Vector2 face = IsleKit.Turn(0f, 0.14f, inward);
        Piece lever = b.Part("Button Sim", x + face.x, z + face.y, 1.15f, inward).Named(name + " Lever");
        lever.Set("PuzzleSimPressSwitch", "group", "finale").Set("PuzzleSimPressSwitch", "activeSeconds", 1.3f).Set("PuzzleSimPressSwitch", "timeoutSeconds", 3f);
        if (parent != null) { postPiece.Under(parent); lever.Under(parent); }
        return lever;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -13f);
        b.ClearGround(28f, 28f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the star on its pole, the launchers round it ----
        var middle = new MeshBuilder();
        Kit.AddDisc(middle, Vector3.zero, LauncherRing + 1f, 0.25f, IsleKit.Stone, 32);
        Kit.AddBox(middle, new Vector3(0f, 0.25f, 0f), new Vector3(0.35f, 6f, 0.35f), IsleKit.Cream);
        Kit.AddStar(middle, new Vector3(0f, 7f, 0f), 1f, 0.15f, IsleKit.Gold);
        b.MeshPiece("Star Pole", middle.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        var launchers = new Component[6];
        for (int i = 0; i < 6; i++)
        {
            float a = 2f * Mathf.PI * i / 6f;
            Piece launcher = b.Part("Firework Launcher", Mathf.Sin(a) * LauncherRing, Mathf.Cos(a) * LauncherRing, 0.25f, a * Mathf.Rad2Deg).Named("Launcher " + (i + 1));
            launcher.Set("PuzzleFireworkLauncher", "color", Colours[i]);
            launchers[i] = launcher.Component("PuzzleFireworkLauncher");
        }

        // ---- the levers: three always, a fourth in lobbies of four or more ----
        Piece first = Lever("Lever North", 0f, null);
        Lever("Lever South-East", 120f, null);
        Lever("Lever South-West", 240f, null);
        Lever("Lever East", 60f, IsleKit.Lobby(b, "Fourth Lever", false, false, true));
        foreach (Component launcher in launchers) first.On("PuzzleSimPressSwitch", "onSuccess", launcher, "Fire");
        first.On("PuzzleSimPressSwitch", "onSuccess", b.Puzzle, "Win");      // every lever in the group fires it: wire one

        IsleKit.StartCorner(b, 0f, -13.5f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 26f, 26f, 2.8f, true, 241);
    }
}
