// Snow Globe (puzzle 19): a great glass dome with a little snowy village inside, and a door that shuts while the
// puzzle runs. Inside, round the village's fir, six coloured lamps; outside, round the dome, six coloured buttons
// on posts. One lamp lights at a time and its button must be pressed, eight times running. The glass is
// soundproof: the one inside sees the lamps and nobody outside can hear them, so it is all pointing, waving and
// holding up fingers. No custom code: a mesh made into the game's soundproof glass, a door, Press In Order.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowGlobePuzzle : PuzzleDef
{
    public override string Name => "SnowGlobe";
    public override Vector2 Footprint => new Vector2(24f, 24f);
    public override Vector2? FixedSite => new Vector2(-1152f, -1434f);      // spread out from the plaza (tools/sites: a site checker and the layout it passed)

    const float Radius = 4.6f, PostRing = 7.6f;
    static readonly Color[] Colours =
    {
        new Color(0.85f, 0.12f, 0.1f), new Color(0.95f, 0.55f, 0.1f), new Color(0.95f, 0.85f, 0.2f),
        new Color(0.15f, 0.6f, 0.25f), new Color(0.15f, 0.4f, 0.9f), new Color(0.6f, 0.2f, 0.7f),
    };

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -10f);
        b.ClearGround(24f, 24f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the dome: a half sphere of glass, with a gap for the door on its south side ----
        var dome = new MeshBuilder();
        const int Rings = 10, Segments = 36;
        Vector3 P(int ring, int seg)
        {
            float lat = Mathf.PI * 0.5f * ring / Rings, lon = 2f * Mathf.PI * seg / Segments;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon)) * Radius;
        }
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                bool door = r < 3 && (s == Segments / 2 || s == Segments / 2 - 1);      // the gap facing south (-z)
                if (door) continue;
                dome.Quad(P(r, s), P(r, s + 1), P(r + 1, s + 1), P(r + 1, s), Color.white);
                dome.Quad(P(r, s), P(r + 1, s), P(r + 1, s + 1), P(r, s + 1), Color.white);      // both sides
            }
        Piece glass = b.MeshPiece("Dome", dome.ToMesh(), IsleKit.Bare(b), true);
        glass.Transform.position = b.WorldPoint(0f, 0f);
        Component glassC = b.AddScript(glass, "PuzzleGlass");
        Sdk.Set(glassC, "style", 2);      // clear
        Sdk.Set(glassC, "sound", 2);      // soundproof
        // the base ring and the door's frame, then the door: shut while the puzzle runs
        var frame = new MeshBuilder();
        Kit.AddDisc(frame, Vector3.zero, Radius + 0.3f, 0.3f, IsleKit.DarkWood, 40);
        Kit.AddBox(frame, new Vector3(-0.75f, 0f, -Radius + 0.15f), new Vector3(0.2f, 2.3f, 0.3f), IsleKit.Gold);
        Kit.AddBox(frame, new Vector3(0.75f, 0f, -Radius + 0.15f), new Vector3(0.2f, 2.3f, 0.3f), IsleKit.Gold);
        Kit.AddBox(frame, new Vector3(0f, 2.3f, -Radius + 0.15f), new Vector3(1.7f, 0.25f, 0.3f), IsleKit.Gold);
        b.MeshPiece("Base", frame.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        b.Part("Door", 0f, -Radius + 0.15f, 0.3f, 0f).Set("PuzzleDoor", "soundproof", true);      // (closed while the puzzle is on)

        // ---- the village inside: a fir with the six lamps round it, two tiny houses ----
        var village = new MeshBuilder();
        Kit.AddPine(village, new Vector3(0f, 0.3f, 1.2f), 2.6f, 1901);
        Kit.AddBox(village, new Vector3(-2.2f, 0.3f, 1.4f), new Vector3(1f, 0.8f, 0.9f), IsleKit.Cream);
        Kit.AddBox(village, new Vector3(-2.2f, 1.1f, 1.4f), new Vector3(1.2f, 0.25f, 1.1f), IsleKit.Red);
        Kit.AddBox(village, new Vector3(2.2f, 0.3f, 1.2f), new Vector3(0.9f, 0.7f, 0.8f), IsleKit.Cream);
        Kit.AddBox(village, new Vector3(2.2f, 1.0f, 1.2f), new Vector3(1.1f, 0.22f, 1.0f), IsleKit.Green);
        Kit.AddBox(village, new Vector3(0f, 0.3f, -0.4f), new Vector3(2.8f, 0.9f, 0.2f), IsleKit.Board);      // the lamp rail, facing the door
        b.MeshPiece("Village", village.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        var lamps = new Object[6];
        var buttons = new List<Object>();
        var posts = new MeshBuilder();
        for (int i = 0; i < 6; i++)
        {
            Piece lamp = b.Part("Light 8", -1.15f + i * 0.46f, -0.52f, 0.95f, 180f).Named("Lamp " + (i + 1));
            lamp.Set("PuzzleLight", "color", Colours[i]);
            lamps[i] = lamp.Component("PuzzleLight");
            // its button outside, on a post of its colour, round the dome (none in front of the door)
            float a = (40f + i * 56f) * Mathf.Deg2Rad;
            float px = Mathf.Sin(a) * PostRing, pz = -Mathf.Cos(a) * PostRing;
            float facing = Mathf.Atan2(-px, -pz) * Mathf.Rad2Deg + 180f;      // its face away from the dome
            Kit.AddBox(posts, new Vector3(px, 0f, pz), new Vector3(0.3f, 1.5f, 0.3f), Colours[i], facing);
            Vector2 face = IsleKit.Turn(0f, 0.16f, facing);
            Piece button = b.Part("Button Poke", px + face.x, pz + face.y, 1.15f, facing).Named("Button " + (i + 1));
            button.Set("PuzzlePokeButton", "autoEnableSeconds", 1.5f);
            buttons.Add(button.Component("PuzzlePokeButton"));
        }
        b.MeshPiece("Button Posts", posts.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the count, on a board outside facing the dome's door, and the order ----
        IsleKit.BoardOnPosts(b, "Count Board", 0f, -9.5f, 180f, 3f, 1.5f, 2.6f, IsleKit.Board);
        Piece bar = b.Part("Progress Bar", 0f, -9.5f - IsleKit.FaceZ - 0.03f, 2.05f, 180f);
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

        IsleKit.StartCorner(b, 5.5f, -10.5f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 22f, 22f, 2.6f, true, 191);
    }
}
