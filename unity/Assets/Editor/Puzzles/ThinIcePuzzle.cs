// Thin Ice (puzzle 21, blind mask): a frozen pond in a stone basin, its ice in squares. Some squares are sound;
// the rest are thin, darker and cracked, and give way: whoever steps on one drops a metre into the cold black water
// (a floor, painted so) and climbs out by the steps at the near end. The crosser wears the blindfold and goes
// through the blind turnstile onto the first square; the caller on the bank sees which squares are sound and calls
// every step. The far bank's pad wins it. No custom code: the thin squares are simply not solid.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class ThinIcePuzzle : PuzzleDef
{
    public override string Name => "ThinIce";
    public override Vector2 Footprint => new Vector2(26f, 30f);
    public override Vector2? FixedSite => new Vector2(-1272f, -1260f);      // spread out from the plaza (tools/sites: a site checker and the layout it passed)

    const int Cols = 6, Rows = 8;
    const float Tile = 1.7f, Deck = 1.2f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -14f);
        b.ClearGround(26f, 30f);
        Material snowy = IsleKit.Snowy(b);

        // ---- which squares are sound: a winding way across from a fixed seed, and a few false friends ----
        var random = new System.Random(2112);
        var sound = new bool[Cols, Rows];
        int c = Cols / 2;
        for (int r = 0; r < Rows; r++)
        {
            sound[c, r] = true;
            if (r < Rows - 1 && random.NextDouble() < 0.6)
            {
                int step = random.NextDouble() < 0.5 ? -1 : 1;
                if (c + step < 0 || c + step >= Cols) step = -step;
                c += step;
                sound[c, r] = true;      // a sideways step on the same row before going on
            }
        }
        for (int k = 0; k < 6; k++) sound[random.Next(Cols), random.Next(1, Rows - 1)] = true;      // dead ends

        float x0 = -Cols * Tile / 2f, z0 = -Rows * Tile / 2f;
        var basin = new MeshBuilder();
        // the basin's rim: stone walls up to the ice's level, the water's floor inside
        float w = Cols * Tile, d = Rows * Tile;
        Kit.AddBox(basin, new Vector3(0f, 0f, z0 - 2f), new Vector3(w + 2f, Deck, 4f), IsleKit.Stone);         // near bank
        Kit.AddBox(basin, new Vector3(0f, 0f, -z0 + 2f), new Vector3(w + 2f, Deck, 4f), IsleKit.Stone);        // far bank
        Kit.AddBox(basin, new Vector3(x0 - 0.5f, 0f, 0f), new Vector3(1f, Deck, d), IsleKit.Stone);            // sides
        Kit.AddBox(basin, new Vector3(-x0 + 0.5f, 0f, 0f), new Vector3(1f, Deck, d), IsleKit.Stone);
        for (int s = 1; s <= 3; s++)      // steps up onto the near bank from outside, and out of the water at the near end
        {
            Kit.AddBox(basin, new Vector3(0f, 0f, z0 - 4f - s * 0.45f), new Vector3(2f, Deck * (1f - s / 4f), 0.45f), IsleKit.Stone);
            Kit.AddBox(basin, new Vector3(x0 + 0.5f, 0f, z0 + s * 0.45f - 0.2f), new Vector3(1f, Deck * (1f - s / 4f) - 0.05f, 0.45f), IsleKit.Stone);
            Kit.AddBox(basin, new Vector3(0f, 0f, -z0 + 4f + s * 0.45f), new Vector3(2f, Deck * (1f - s / 4f), 0.45f), IsleKit.Stone);      // and down off the far bank
        }
        Kit.AddBox(basin, new Vector3(0f, 0f, 0f), new Vector3(w, 0.05f, d), new Color(0.05f, 0.1f, 0.16f));      // black water
        b.MeshPiece("Basin", basin.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- the squares: sound ones solid and white, thin ones grey and cracked, and not solid ----
        var solid = new MeshBuilder();
        var thin = new MeshBuilder();
        for (int i = 0; i < Cols; i++)
            for (int j = 0; j < Rows; j++)
            {
                var at = new Vector3(x0 + (i + 0.5f) * Tile, Deck - 0.1f, z0 + (j + 0.5f) * Tile);
                if (sound[i, j]) Kit.AddBox(solid, at, new Vector3(Tile - 0.06f, 0.1f, Tile - 0.06f), IsleKit.Ice);
                else
                {
                    Kit.AddBox(thin, at, new Vector3(Tile - 0.06f, 0.05f, Tile - 0.06f), new Color(0.42f, 0.55f, 0.66f));
                    Kit.AddBox(thin, at + new Vector3(0f, 0.051f, 0f), new Vector3(Tile * 0.8f, 0.01f, 0.05f), new Color(0.2f, 0.28f, 0.36f), random.Next(180));      // a crack
                }
            }
        b.MeshPiece("Sound Ice", solid.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Ice", m => m.SetFloat("_SnowLine", 2f)), true).Transform.position = b.WorldPoint(0f, 0f);
        b.MeshPiece("Thin Ice", thin.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Ice", m => m.SetFloat("_SnowLine", 2f)), false).Transform.position = b.WorldPoint(0f, 0f);

        // ---- in: the blindfold and the turnstile on the near bank; out: the pad on the far bank ----
        IsleKit.BlindfoldHook(b, -3.5f, z0 - 3f, 0f, Deck, true);
        IsleKit.BlindGate(b, x0 + (Cols / 2 + 0.5f) * Tile, z0 - 0.9f, 0f, Deck, true);
        Piece goal = IsleKit.Sensor(b, "Far Bank", 0f, -z0 + 1.6f, new Vector3(w, 2.6f, 2.4f), Deck);
        goal.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");

        IsleKit.StartCorner(b, 7.5f, z0 - 4f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 0f, 24f, 28f, 2.6f, true, 211);
    }
}
