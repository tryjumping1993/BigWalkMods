// Blind Carols (puzzle 9, blind mask): inside a snug hut three little speakers each play a carol's tune, one above
// each slot of a board, and six speaking tiles on the bench play tunes too. The sorter wears the blindfold and goes
// in through the blind turnstile: they can hear which tile matches which speaker, but cannot see the board or the
// bench. Their partner stands at the hut's soundproof window: they see everything and hear nothing (no tune, no
// voice through the glass), and talk the sorter's hands to the bench and the slots over a pair of walkie-talkies.
// Right on the board wins it; a plain turnstile lets the sorter out. No custom code: sound pegs, sound peg
// answers, a peg board, soundproof glass, the blind turnstile.
using Bwm;
using UnityEngine;

public sealed class BlindCarolsPuzzle : PuzzleDef
{
    public override string Name => "BlindCarols";
    public override Vector2 Footprint => new Vector2(22f, 20f);
    public override Vector2? FixedSite => new Vector2(-520f, -790f);      // chosen on the island's lowland; its build levels and clears it

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -8f);
        b.ClearGround(22f, 20f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the hut: soundproof timber walls, a big window of soundproof glass on its south side ----
        Piece hut = b.Empty("Hut", 0f, 0f, 0f);
        b.AddScript(hut, "PuzzleSoundProof");
        var walls = new MeshBuilder();
        Kit.AddBox(walls, new Vector3(0f, 0f, 2.5f), new Vector3(6.4f, 2.8f, 0.24f), IsleKit.Wood);       // north (the board's wall)
        Kit.AddBox(walls, new Vector3(-3.1f, 0f, 1.08f), new Vector3(0.24f, 2.8f, 2.84f), IsleKit.Wood); // west, beside the way out
        Kit.AddBox(walls, new Vector3(-3.1f, 2.3f, -1.4f), new Vector3(0.24f, 0.5f, 2.2f), IsleKit.Wood); // over the way out
        Kit.AddBox(walls, new Vector3(3.1f, 0f, 1.08f), new Vector3(0.24f, 2.8f, 2.84f), IsleKit.Wood);  // east, beside the turnstile
        Kit.AddBox(walls, new Vector3(3.1f, 2.3f, -1.4f), new Vector3(0.24f, 0.5f, 2.2f), IsleKit.Wood); // over the turnstile
        Kit.AddBox(walls, new Vector3(-2.2f, 0f, -2.5f), new Vector3(2f, 2.8f, 0.24f), IsleKit.Wood);    // south, either side of the window
        Kit.AddBox(walls, new Vector3(2.2f, 0f, -2.5f), new Vector3(2f, 2.8f, 0.24f), IsleKit.Wood);
        Kit.AddBox(walls, new Vector3(0f, 0f, -2.5f), new Vector3(2.4f, 0.9f, 0.24f), IsleKit.Wood);     // under the window
        Kit.AddBox(walls, new Vector3(0f, 2.3f, -2.5f), new Vector3(2.4f, 0.5f, 0.24f), IsleKit.Wood);   // over it
        Kit.AddBox(walls, new Vector3(0f, 2.8f, 0f), new Vector3(7f, 0.24f, 5.6f), IsleKit.Red);         // roof
        Kit.AddBox(walls, new Vector3(-1.6f, 0f, -1.4f), new Vector3(2.2f, 0.75f, 0.6f), IsleKit.DarkWood); // the bench
        Piece hutMesh = b.MeshPiece("Hut Walls", walls.ToMesh(), snowy);
        hutMesh.Transform.position = b.WorldPoint(0f, 0f);
        hutMesh.Under(hut);
        b.Part("Glass", 0f, -2.5f, 1.6f, 0f).Scaled(2.4f / 2.087f, 1.4f / 1.868f, 1f).Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 2);
        IsleKit.BlindGate(b, 3.1f, -1.4f, -90f);      // in from the east, blindfolded
        b.Part("Gate No items", -3.1f, -1.4f, 0f, -90f).Named("Way Out").Set("PuzzleNoItemsGate", "oneWay", true);      // and out to the west

        // ---- the board on the north wall with its check screen; the three carols each on a wall of its own (red on
        //      the west, green on the north, gold on the east), so the sorter can walk to each and hear it alone, and
        //      a coloured mark over each slot says which carol goes there ----
        Piece board = b.Part("Peg Board", -0.65f, 2.5f - 0.12f - 0.06f, 1.2f, 180f).Set("PuzzlePegBoard", "size", 2);      // three slots (its pivot is at one end)
        Component boardC = board.Component("PuzzlePegBoard");
        Color[] carolColour = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };
        Vector3[] carolAt = { new Vector3(-2.95f, 1.6f, 1.2f), new Vector3(2.0f, 1.9f, 2.35f), new Vector3(2.95f, 1.6f, 1.2f) };
        float[] carolYaw = { 90f, 180f, -90f };
        var marks = new MeshBuilder();
        for (int i = 0; i < 3; i++)
        {
            Piece speaker = b.Part("Peg Sound Mounted", carolAt[i].x, carolAt[i].z, carolAt[i].y, carolYaw[i]).Named("Carol " + (i + 1));
            Sdk.Add(boardC, "answerFrom", speaker.Component("PuzzleSoundPegAnswer"));
            Sdk.AddInts(boardC, "answerFromFlap", 1);
            Vector2 back = IsleKit.Turn(0f, -0.08f, carolYaw[i]);
            Kit.AddBox(marks, new Vector3(carolAt[i].x + back.x, carolAt[i].y - 0.55f, carolAt[i].z + back.y), new Vector3(0.6f, 0.25f, 0.05f), carolColour[i], carolYaw[i]);      // under its speaker
            Kit.AddBox(marks, new Vector3((i - 1) * 0.43f, 1.45f, 2.36f), new Vector3(0.3f, 0.12f, 0.05f), carolColour[i]);      // over its slot
        }
        b.MeshPiece("Carol Marks", marks.ToMesh(), IsleKit.Bare(b), false).Transform.position = b.WorldPoint(0f, 0f);
        b.Part("Validator", 1.0f, 2.5f - 0.12f - 0.06f, 1.6f, 180f);

        // ---- the six speaking tiles on the bench, and a walkie-talkie pair to talk through the wall ----
        for (int i = 0; i < 6; i++)
            b.Part("Peg Sound", -2.5f + i * 0.36f, -1.4f, 0.75f + 0.24f, 0f).Named("Speaking Tile " + (i + 1)).Set("PuzzleSoundPeg", "sound", i + 2);
        b.Part("Vanilla Item", -0.3f, -1.4f, 0.75f + 0.15f, 0f).Named("Walkie-Talkie In").Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);
        var stand = new MeshBuilder();
        Kit.AddBox(stand, Vector3.zero, new Vector3(0.9f, 0.9f, 0.6f), IsleKit.DarkWood);
        b.MeshPiece("Outside Stand", stand.ToMesh(), snowy).Transform.position = b.WorldPoint(-2.6f, -4.2f);
        b.Part("Vanilla Item", -2.6f, -4.2f, 0.9f + 0.15f, 0f).Named("Walkie-Talkie Out").Set("PuzzleVanillaItem", "itemType", 4).Set("PuzzleVanillaItem", "savesOnReload", false);

        IsleKit.BlindfoldHook(b, 5.6f, -3.6f, 0f);
        IsleKit.StartCorner(b, 0f, -8f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, -1f, 20f, 16f, 2.6f, true, 91);
    }
}
