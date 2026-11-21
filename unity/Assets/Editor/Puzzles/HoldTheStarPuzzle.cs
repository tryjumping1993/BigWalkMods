// Hold the Star (puzzle 5): a tall fir with a star on top, the gourd in a glass box at its foot. Holders stand
// round the tree, far apart, and the box opens only while every one of them holds their button at once; then it
// stays open for a few seconds. So everybody holds, the star lights, and one of them has to let go and run for
// the gourd before the lid comes down. Three holders, four in a lobby of four or more. No custom code: the SDK's
// N-hold buttons, a timer and the box do it.
using Bwm;
using UnityEngine;

public sealed class HoldTheStarPuzzle : PuzzleDef
{
    public override string Name => "HoldTheStar";
    public override Vector2 Footprint => new Vector2(36f, 36f);
    public override Vector2? FixedSite => new Vector2(-2975f, -1565f);      // chosen on the island's lowland; its build levels and clears it

    const float Ring = 15f, OpenSeconds = 7f;

    PuzzleBuilder b;

    // A holder's stand: a post facing out from the tree with the N-hold button on it, so whoever holds it has their
    // back to the box
    Piece Holder(string name, float angle, Piece parent)
    {
        float a = angle * Mathf.Deg2Rad, x = Mathf.Sin(a) * Ring, z = Mathf.Cos(a) * Ring;
        var mb = new MeshBuilder();
        Kit.AddBox(mb, Vector3.zero, new Vector3(0.3f, 1.7f, 0.24f), IsleKit.Wood, angle);
        Kit.AddBox(mb, new Vector3(0f, 1.7f, 0f), new Vector3(0.9f, 0.1f, 0.6f), IsleKit.DarkWood, angle);
        Piece post = b.MeshPiece(name, mb.ToMesh(), IsleKit.Snowy(b));
        post.Transform.position = b.WorldPoint(x, z);
        Vector2 face = IsleKit.Turn(0f, 0.13f, angle);
        Piece button = b.Part("Button N Hold", x + face.x, z + face.y, 1.15f, angle).Named(name + " Button");
        button.Set("PuzzleButtonNHold", "group", "star");
        if (parent != null) { post.Under(parent); button.Under(parent); }
        return button;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.DebugTeleport(0f, -10f);
        b.ClearGround(36f, 36f);

        // ---- the tree, its star, and the gourd's box on a stone at its foot ----
        var tree = new MeshBuilder();
        Kit.AddFir(tree, Vector3.zero, 10f, 4242, 1.2f);
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(true), IsleKit.Fir(b), false);
        pine.Transform.position = b.WorldPoint(0f, 0f);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * 2f; trunk.radius = 0.6f; trunk.height = 4f;
        var star = new MeshBuilder();
        Kit.AddStar(star, new Vector3(0f, 10.6f, 0f), 0.75f, 0.12f, IsleKit.Gold);
        b.MeshPiece("Star", star.ToMesh(), IsleKit.Bare(b), false).Transform.position = b.WorldPoint(0f, 0f);
        var plinth = new MeshBuilder();
        Kit.AddBox(plinth, Vector3.zero, new Vector3(1.3f, 0.45f, 1.3f), IsleKit.Stone);
        Kit.AddBox(plinth, new Vector3(0f, 0f, -1.6f), new Vector3(0.2f, 1.6f, 0.2f), IsleKit.Wood);      // the timer's post
        b.MeshPiece("Plinth", plinth.ToMesh(), IsleKit.Snowy(b)).Transform.position = b.WorldPoint(0f, -3.2f);
        Piece box = b.Part("Box", 0f, -3.2f, 0.45f, 180f);
        Piece timer = b.Part("Timer", 0f, -4.8f - 0.12f, 1.35f, 180f);
        timer.Set("PuzzleTimer", "duration", OpenSeconds);

        // a lamp that lights the star while everyone holds
        Piece lamp = b.Part("Light 6", 0f, -1.4f, 9.4f, 180f);
        lamp.Set("PuzzleLight", "color", IsleKit.Gold);

        // ---- the holders: three always, a fourth in lobbies of four or more ----
        Piece first = Holder("Holder North", 0f, null);
        Holder("Holder South-East", 120f, null);
        Holder("Holder South-West", 240f, null);
        Holder("Holder West", 300f, IsleKit.Lobby(b, "Fourth Holder", false, false, true));

        // every button in the group fires the group's events: wire one
        Component boxC = box.Component("PuzzleBox"), timerC = timer.Component("PuzzleTimer"), lampC = lamp.Component("PuzzleLight");
        first.On("PuzzleButtonNHold", "onAllHeld", boxC, "Open");
        first.On("PuzzleButtonNHold", "onAllHeld", timerC, "StartTimer");
        first.On("PuzzleButtonNHold", "onAllHeld", lampC, "TurnOn");
        first.On("PuzzleButtonNHold", "onAllReleased", lampC, "TurnOff");
        timer.On("PuzzleTimer", "onFinished", boxC, "Close");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", boxC, "Close");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", timerC, "ResetTimer");

        IsleKit.StartCorner(b, 0f, -11f, 0f, false);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 34f, 34f, 2.7f, true, 51);
    }
}
