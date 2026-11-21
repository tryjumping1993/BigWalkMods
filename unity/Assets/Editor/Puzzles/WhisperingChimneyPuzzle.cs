// Whispering Chimney (puzzle 10): a little cabin stands sealed inside a ring of fence, no door, a tune playing in
// it. The only way to hear it is the chimney: a microphone hangs down it, and its wire runs to a listening chair
// forty metres off, where whoever sits hears the cabin and nothing else. They hum what they hear; the others try
// the speaking tiles on the table until one plays the same tune, and hang it on the board. No custom code: a sound
// peg answer, a directional microphone, a listening chair, sound pegs and a peg board.
using Bwm;
using UnityEngine;

public sealed class WhisperingChimneyPuzzle : PuzzleDef
{
    public override string Name => "WhisperingChimney";
    public override Vector2 Footprint => new Vector2(22f, 20f);
    public override Vector2? FixedSite => new Vector2(-890f, -650f);      // chosen on the island's lowland; its build levels and clears it

    const float CabinZ = 34f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -6f);
        b.ClearGround(22f, 20f);
        b.ClearGround(26f, 26f, 0f, CabinZ);
        Material snowy = IsleKit.Snowy(b);

        // ---- the cabin: four walls and a roof, sealed (soundproof), with a chimney; the tune plays inside ----
        Piece cabin = b.Empty("Cabin", 0f, CabinZ, 0f);
        b.AddScript(cabin, "PuzzleSoundProof");
        var walls = new MeshBuilder();
        Kit.AddBox(walls, new Vector3(0f, 0f, -2f), new Vector3(4.4f, 2.6f, 0.24f), IsleKit.Wood);
        Kit.AddBox(walls, new Vector3(0f, 0f, 2f), new Vector3(4.4f, 2.6f, 0.24f), IsleKit.Wood);
        Kit.AddBox(walls, new Vector3(-2.1f, 0f, 0f), new Vector3(0.24f, 2.6f, 4f), IsleKit.Wood);
        Kit.AddBox(walls, new Vector3(2.1f, 0f, 0f), new Vector3(0.24f, 2.6f, 4f), IsleKit.Wood);
        Kit.AddBox(walls, new Vector3(0f, 2.6f, 0f), new Vector3(5f, 0.24f, 4.8f), IsleKit.Red);
        Kit.AddBox(walls, new Vector3(1.1f, 2.84f, 0.8f), new Vector3(0.8f, 1.4f, 0.8f), IsleKit.Stone);      // the chimney
        Piece cabinMesh = b.MeshPiece("Cabin Walls", walls.ToMesh(), snowy);
        cabinMesh.Transform.position = b.WorldPoint(0f, CabinZ);
        cabinMesh.Under(cabin);
        Piece tune = b.Part("Peg Sound Mounted", 0f, CabinZ + 1.85f, 1.3f, 180f).Named("The Tune");
        // the microphone, hanging in the chimney's mouth and aimed down at the speaker
        Piece mic = b.Part("Directional Mic", 1.1f, CabinZ + 0.8f, 2.4f, 0f).Named("Chimney Mic");
        mic.Transform.rotation = b.Root.transform.rotation * Quaternion.LookRotation(new Vector3(-1.1f, -1.1f, 1.05f).normalized);
        // a ring of fence well out from it, too high to climb, so nobody stands at its walls to listen
        var fence = new MeshBuilder();
        for (int i = 0; i < 40; i++)
        {
            float a = 2f * Mathf.PI * i / 40f;
            Kit.AddBox(fence, new Vector3(Mathf.Cos(a) * 11f, 0f, Mathf.Sin(a) * 11f), new Vector3(0.2f, 2.8f, 0.2f), IsleKit.DarkWood);
            float a2 = a + Mathf.PI / 40f;
            Kit.AddBox(fence, new Vector3(Mathf.Cos(a2) * 11f, 0.5f, Mathf.Sin(a2) * 11f), new Vector3(1.75f, 0.14f, 0.08f), IsleKit.Wood, -a2 * Mathf.Rad2Deg + 90f);
            Kit.AddBox(fence, new Vector3(Mathf.Cos(a2) * 11f, 1.6f, Mathf.Sin(a2) * 11f), new Vector3(1.75f, 0.14f, 0.08f), IsleKit.Wood, -a2 * Mathf.Rad2Deg + 90f);
            Kit.AddBox(fence, new Vector3(Mathf.Cos(a2) * 11f, 2.6f, Mathf.Sin(a2) * 11f), new Vector3(1.75f, 0.14f, 0.08f), IsleKit.Wood, -a2 * Mathf.Rad2Deg + 90f);
        }
        b.MeshPiece("Fence", fence.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, CabinZ);

        // ---- the listening chair, and the table of speaking tiles with the board ----
        Piece chair = b.Part("Listening Chair", 0f, 6f, 0f, 0f);
        Sdk.Set(chair.Component("PuzzleListeningChair"), "microphone", mic.Component("PuzzleDirectionalMic"));
        var table = new MeshBuilder();
        Kit.AddBox(table, Vector3.zero, new Vector3(4.4f, 0.9f, 0.9f), IsleKit.Wood);
        b.MeshPiece("Table", table.ToMesh(), snowy).Transform.position = b.WorldPoint(-5f, 0f);
        for (int i = 0; i < 7; i++)
            b.Part("Peg Sound", -5f - 1.8f + i * 0.6f, 0f, 0.9f + 0.24f, 0f).Named("Speaking Tile " + (i + 1)).Set("PuzzleSoundPeg", "sound", i);
        IsleKit.BoardOnPosts(b, "Peg Board Wall", 5f, 0f, 0f, 3.4f, 0.4f, 2.4f, IsleKit.Board);
        Piece board = b.Part("Peg Board", 5f, IsleKit.FaceZ + 0.06f, 1.3f, 0f).Set("PuzzlePegBoard", "size", 0);      // one slot
        Sdk.Add(board.Component("PuzzlePegBoard"), "answerFrom", tune.Component("PuzzleSoundPegAnswer"));
        Sdk.AddInts(board.Component("PuzzlePegBoard"), "answerFromFlap", 1);
        b.Part("Validator", 5f + 1.0f, IsleKit.FaceZ + 0.06f, 2.0f, 0f);

        IsleKit.StartCorner(b, 0f, -6f, 0f);
        Kit.LightRing(b, "Site Lights", 0f, 1f, 20f, 16f, 2.6f, true, 101);
    }
}
