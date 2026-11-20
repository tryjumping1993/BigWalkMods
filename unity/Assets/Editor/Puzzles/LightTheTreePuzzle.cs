// Light the Tree (puzzle 3): six posts ring a small tree, each with a button and a lamp. One lamp lights at a
// time and its button must be pressed, eight times running; a wrong press starts the count again.
// The catch: each button's lamp is on the post on the far side of the tree, facing away, so the person at a
// button can never see whether it is theirs. The others have to call it. Six buttons need at least three
// people moving between them. No custom code: the SDK's Press In Order runs it.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class LightTheTreePuzzle : PuzzleDef
{
    public override string Name => "LightTheTree";
    public override Vector2 Footprint => new Vector2(22f, 24f);
    public override Vector2 Near => new Vector2(-640f, -830f);
    public override float MinDistance => 0f;
    public override float MaxDistance => 220f;

    const int Stations = 6;
    const float Ring = 5.5f;
    static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    static readonly Color Board = new Color(0.20f, 0.22f, 0.28f);
    static readonly Color Stone = new Color(0.50f, 0.52f, 0.56f);

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -8f);
        Material snowy = b.ShaderMaterial("BWM/Snowy", "Snowy");

        // ---- the tree in the middle: it is what hides each lamp from its own button ----
        var tree = new MeshBuilder();
        Kit.AddPine(tree, Vector3.zero, 7f, 303);
        Piece pine = b.MeshPiece("Tree", tree.ToMesh(), snowy, false);
        pine.Transform.position = b.WorldPoint(0f, 0f);
        CapsuleCollider trunk = pine.Go.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * 1.6f; trunk.radius = 0.9f; trunk.height = 3.2f;

        // ---- six posts: button i here, and the lamp for the button opposite ----
        var buttons = new List<UnityEngine.Object>();
        var lamps = new UnityEngine.Object[Stations];
        var posts = new MeshBuilder();
        Kit.AddBox(posts, Vector3.zero, new Vector3(0.26f, 1.75f, 0.2f), Wood);
        Mesh postMesh = posts.ToMesh();
        for (int i = 0; i < Stations; i++)
        {
            float a = 2f * Mathf.PI * i / Stations;
            float x = Mathf.Sin(a) * Ring, z = Mathf.Cos(a) * Ring;
            float outward = a * Mathf.Rad2Deg;                              // facing away from the tree
            Piece post = b.MeshPiece("Post " + (i + 1), Object.Instantiate(postMesh), snowy);
            post.Transform.position = b.WorldPoint(x, z);
            post.Transform.rotation = Quaternion.Euler(0f, outward, 0f);
            float fx = Mathf.Sin(a) * (Ring + 0.1f), fz = Mathf.Cos(a) * (Ring + 0.1f);
            Piece button = b.Part("Button Poke", fx, fz, 1.1f, outward).Named("Button " + (i + 1));
            button.Set("PuzzlePokeButton", "autoEnableSeconds", 1.5f);
            buttons.Add(button.Component("PuzzlePokeButton"));
            int pair = (i + Stations / 2) % Stations;
            Piece lamp = b.Part("Light 8", fx, fz, 1.5f, outward).Named("Lamp " + (pair + 1));
            lamps[pair] = lamp.Component("PuzzleLight");
        }

        // ---- the count: a progress bar on a board everyone can see from the ring ----
        var frame = new MeshBuilder();
        Kit.AddBox(frame, new Vector3(-2.1f, 0f, 0f), new Vector3(0.2f, 3.2f, 0.2f), Wood);
        Kit.AddBox(frame, new Vector3(2.1f, 0f, 0f), new Vector3(0.2f, 3.2f, 0.2f), Wood);
        Kit.AddBox(frame, new Vector3(0f, 2.2f, 0.06f), new Vector3(4.2f, 0.9f, 0.12f), Board);
        b.MeshPiece("Board", frame.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, -10.5f);
        Piece bar = b.Part("Progress Bar", 0f, -10.5f - 0.03f, 2.65f, 0f);
        bar.Transform.position += Vector3.back * 0.0f;
        bar.Set("PuzzleProgressBar", "total", 8);

        Piece order = b.Empty("Press In Order", 0f, 0f, 0.5f);
        Component pio = b.AddScript(order, "PuzzlePressInOrder");
        Sdk.Add(pio, "lights", lamps);
        Sdk.Add(pio, "buttons", buttons.ToArray());
        Sdk.Add(pio, "progressBars", bar.Component("PuzzleProgressBar"));
        Sdk.Set(pio, "rounds", 8);

        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
        bar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        // ---- start switch, live lamp and the gourd, by the board ----
        var furniture = new MeshBuilder();
        Kit.AddBox(furniture, new Vector3(3.6f, 0f, 0f), new Vector3(0.26f, 1.5f, 0.2f), Wood);
        Kit.AddBox(furniture, new Vector3(-3.8f, 0f, 0f), new Vector3(1.1f, 0.45f, 1.1f), Stone);
        b.MeshPiece("Furniture", furniture.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, -10.5f);
        b.Part("Switch Start", 3.6f, -10.5f + 0.1f + 0.073f, 1.25f, 0f);
        b.Part("Live Light", 3.6f, -10.5f + 0.1f, 1.42f, 0f);
        b.Part("Gourd Vice", -3.8f, -10.5f, 0.45f, 0f);
        b.Part("Pillow", -5.4f, -10.5f, 0.13f);
        b.Part("Confetti", 0f, -9.2f, 0.2f);

        Kit.LightRing(b, "Site Lights", 0f, -1.5f, 19f, 20f, 2.7f, true, 11);
    }
}
