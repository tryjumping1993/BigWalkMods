// Summit Disc Golf (a scenery pack): two portable disc golf baskets and twelve frisbees on the pizzeria's terrace at the
// summit, all of them things players can pick up, carry and put down anywhere, so a group can lay out its own holes
// (or carry a basket down the mountain). The frisbees wait on a rack beside a tee mat and a sign; the baskets stand
// in front of the shop and down the alley beside it to begin with. Nothing to win. Items are not saved: a reload puts them back.
// Winter Walk's DiscGolf driver does the rest: a basket put down stands upright, a thrown frisbee flies as a disc
// does, and frisbees left lying come back to the rack, so they never run out.
// The pack also holds the summit end of the teleporters (Winter Walk's Teleporter): where travellers from the landing's
// and the plaza's pads arrive, in front of the pizzeria's door, and the pad that takes them back down to the plaza.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SummitDiscGolfPuzzle : PuzzleDef
{
    public override string Name => "SummitDiscGolf";
    public override int PuzzleType => 2;
    public override Vector2 Footprint => new Vector2(6f, 6f);
    public override Vector2? FixedSite => WinterIslePuzzle.Summit;

    static readonly Color Steel = new Color(0.62f, 0.64f, 0.68f), Yellow = new Color(0.98f, 0.8f, 0.1f), Chain = new Color(0.78f, 0.79f, 0.83f);
    const int Discs = 12;
    static readonly Color[] DiscColours =
    {
        new Color(0.9f, 0.15f, 0.12f), new Color(0.15f, 0.45f, 0.95f), new Color(0.98f, 0.85f, 0.15f),
        new Color(0.2f, 0.75f, 0.3f), new Color(1f, 0.5f, 0.1f), new Color(0.9f, 0.35f, 0.75f),
    };

    // A basket: a weighted foot, a pole, a tray with a low wall, chains hanging inside a yellow top band. Its foot at
    // the origin, 1.45 m tall
    static Mesh BasketMesh()
    {
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, Vector3.zero, 0.32f, 0.06f, Steel * 0.75f, 16);
        Kit.AddBox(mb, new Vector3(0f, 0.06f, 0f), new Vector3(0.05f, 1.36f, 0.05f), Steel);
        Kit.AddDisc(mb, new Vector3(0f, 0.55f, 0f), 0.34f, 0.03f, Steel, 18);
        for (int k = 0; k < 18; k++)
        {
            float a = 2f * Mathf.PI * k / 18f;
            Kit.AddBox(mb, new Vector3(Mathf.Cos(a) * 0.33f, 0.55f, Mathf.Sin(a) * 0.33f), new Vector3(0.12f, 0.24f, 0.015f), Steel * 0.9f, -(a * Mathf.Rad2Deg + 90f));
        }
        for (int k = 0; k < 12; k++)
        {
            float a = 2f * Mathf.PI * (k + 0.5f) / 12f;
            Kit.AddBox(mb, new Vector3(Mathf.Cos(a) * 0.2f, 0.84f, Mathf.Sin(a) * 0.2f), new Vector3(0.02f, 0.46f, 0.02f), Chain);
        }
        for (int k = 0; k < 18; k++)
        {
            float a = 2f * Mathf.PI * k / 18f;
            Kit.AddBox(mb, new Vector3(Mathf.Cos(a) * 0.3f, 1.3f, Mathf.Sin(a) * 0.3f), new Vector3(0.11f, 0.08f, 0.02f), Yellow, -(a * Mathf.Rad2Deg + 90f));
        }
        Kit.AddDisc(mb, new Vector3(0f, 1.42f, 0f), 0.06f, 0.03f, Yellow, 10);
        return mb.ToMesh();
    }

    static Mesh DiscMesh(Color colour)
    {
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, Vector3.zero, 0.11f, 0.012f, colour * 0.8f, 20);           // the rim
        Kit.AddDisc(mb, new Vector3(0f, 0.012f, 0f), 0.104f, 0.014f, colour, 20);   // the dome
        Kit.AddDisc(mb, new Vector3(0f, 0.026f, 0f), 0.05f, 0.003f, Color.white, 12);      // a stamp in the middle
        return mb.ToMesh();
    }

    // A pair of skis lying flat, tips towards +z: two planks with turned-up tips and a binding each
    static Mesh SkisMesh(Color colour)
    {
        var mb = new MeshBuilder();
        foreach (float x in new[] { -0.09f, 0.09f })
        {
            Kit.AddBox(mb, new Vector3(x, 0f, -0.05f), new Vector3(0.085f, 0.018f, 1.5f), colour);
            Kit.AddBox(mb, new Vector3(x, 0.012f, 0.76f), new Vector3(0.085f, 0.018f, 0.12f), colour);          // the tip, stepped up
            Kit.AddBox(mb, new Vector3(x, 0.03f, 0.86f), new Vector3(0.075f, 0.018f, 0.1f), colour * 0.9f);
            Kit.AddBox(mb, new Vector3(x, 0.018f, -0.1f), new Vector3(0.07f, 0.05f, 0.26f), new Color(0.12f, 0.12f, 0.14f));      // the binding
        }
        return mb.ToMesh();
    }

    // A snowboard lying flat, nose towards +z
    static Mesh BoardMesh(Color colour)
    {
        var mb = new MeshBuilder();
        Kit.AddBox(mb, new Vector3(0f, 0f, 0f), new Vector3(0.27f, 0.02f, 1.2f), colour);
        foreach (float z in new[] { -0.66f, 0.66f })
        {
            Kit.AddBox(mb, new Vector3(0f, 0.01f, z), new Vector3(0.24f, 0.02f, 0.12f), colour);
            Kit.AddBox(mb, new Vector3(0f, 0.025f, z * 1.14f), new Vector3(0.18f, 0.02f, 0.08f), colour * 0.9f);
        }
        foreach (float z in new[] { -0.26f, 0.26f })
            Kit.AddBox(mb, new Vector3(0f, 0.02f, z), new Vector3(0.24f, 0.06f, 0.14f), new Color(0.12f, 0.12f, 0.14f));          // the bindings
        return mb.ToMesh();
    }

    static void SetText(Piece sign, string text, float size)
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
        tmp.fontSize = size;
        tmp.color = new Color(0.98f, 0.92f, 0.8f);
    }

    public override void Build(PuzzleBuilder b)
    {
        Material bare = IsleKit.Bare(b), snowy = IsleKit.Snowy(b);
        // all on the pizzeria's terrace (CastIronPizzaPuzzle), level with the summit: this pack's root is at the summit
        // too, so heights here are from the terrace's top (onGround false)
        Vector2 Here(float u, float v) => CastIronPizzaPuzzle.At(u, v) - WinterIslePuzzle.Summit;
        int n = 0;
        foreach (Vector2 at in new[] { Here(22f, -2f), Here(36f, -11f) })      // in front of the shop, and down the alley beside it
            b.Item("Disc Golf Basket " + (++n), BasketMesh(), bare, at.x, at.y, 0.05f, 8f, false);
        // the tee: a mat, a sign on posts, and a rack with the frisbees on it, near the terrace's head
        Vector2 tee = Here(13.5f, -9f), aim = Here(22f, -2f) - tee;
        float yaw = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;      // the mat points at the first basket
        var mb = new MeshBuilder();
        Kit.AddBox(mb, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.05f, 3.2f), new Color(0.18f, 0.3f, 0.2f), yaw);     // the tee mat
        Vector2 rack = IsleKit.Turn(-2.2f, -1.2f, yaw);
        Kit.AddBox(mb, new Vector3(rack.x, 0f, rack.y), new Vector3(1.8f, 0.85f, 0.7f), IsleKit.Wood, yaw);                   // the rack's table
        Kit.AddBox(mb, new Vector3(rack.x, 0.85f, rack.y), new Vector3(1.9f, 0.06f, 0.8f), IsleKit.DarkWood, yaw);
        Piece furniture = b.MeshPiece("Disc Golf Tee", mb.ToMesh(), snowy);
        furniture.Transform.position = b.WorldPoint(tee.x, tee.y, 0f, false);
        for (int i = 0; i < Discs; i++)
        {
            Vector2 at = tee + IsleKit.Turn(-2.2f - 0.66f + (i % 4) * 0.44f, -1.2f - 0.24f + (i / 4) * 0.24f, yaw);
            b.Item("Frisbee " + (i + 1).ToString("00"), DiscMesh(DiscColours[i % DiscColours.Length]), bare, at.x, at.y, 0.93f, 0.25f, false)
                .Set("PuzzleItem", "savesOnReload", false);
        }
        // the teleporters' summit end: arriving in front of the pizzeria's door (shop x -2.4, 4 m out from it), facing
        // it; and the way back down, a pad at the terrace's side
        Vector2 arrive = Here(CastIronPizzaPuzzle.ShopFront - 4f, 2.4f), back = Here(19.5f, -11f);      // (on the open snow by the railing: the terrace's other side is all tables)
        IsleKit.TeleportArrival(b, "Pizzeria", arrive.x, arrive.y, Mathf.Atan2(CastIronPizzaPuzzle.Out.x, CastIronPizzaPuzzle.Out.y) * Mathf.Rad2Deg, false);
        IsleKit.TeleportBooth(b, "Summit", "Plaza", "ToPlaza", back.x, back.y, Mathf.Atan2(CastIronPizzaPuzzle.Side.x, CastIronPizzaPuzzle.Side.y) * Mathf.Rad2Deg, false);
        Vector2 board = tee + IsleKit.Turn(2.3f, -1.4f, yaw);
        IsleKit.BoardOnPosts(b, "Disc Golf Sign", board.x, board.y, yaw, 1.6f, 1.2f, 2.0f, IsleKit.Green, false);
        Vector2 face = board + IsleKit.Turn(0f, IsleKit.FaceZ + 0.06f, yaw);
        IsleKit.PaintedWords(b, "Disc Golf Words", "DiscGolf", board.x + IsleKit.Turn(0f, 0.07f, yaw).x, board.y + IsleKit.Turn(0f, 0.07f, yaw).y, yaw, 1.6f, 1.2f, 0.8f, false);
        Kit.LightString(b, "Disc Golf Lights", new List<Vector3>
        {
            new Vector3(board.x + IsleKit.Turn(-1.0f, 0f, yaw).x, 2.4f, board.y + IsleKit.Turn(-1.0f, 0f, yaw).y),
            new Vector3(board.x + IsleKit.Turn(1.0f, 0f, yaw).x, 2.4f, board.y + IsleKit.Turn(1.0f, 0f, yaw).y),
        }, 0.2f, 0.3f, false, false, 97);
        // Skis and snowboards for the taking (the user, 2026-11-24: "free access to skiing equipment at the summit ...
        // intuitive to use as is most of the equipment in the base game"): things to pick up, on a low table under a
        // sign. Whoever holds a pair has it on their feet (Winter Walk's SkiDriver), and pairs left lying come back.
        {
            Vector2 stand = Here(24.5f, -9.5f);      // on the open snow in front of the shop, by the railing
            float sy = Mathf.Atan2(CastIronPizzaPuzzle.Side.x, CastIronPizzaPuzzle.Side.y) * Mathf.Rad2Deg;      // its front to the terrace's middle
            var table = new MeshBuilder();
            Kit.AddBox(table, new Vector3(0f, 0f, 0f), new Vector3(4.6f, 0.42f, 2.1f), IsleKit.Wood, sy);
            Kit.AddBox(table, new Vector3(0f, 0.42f, 0f), new Vector3(4.7f, 0.05f, 2.2f), IsleKit.DarkWood, sy);
            Piece tablePiece = b.MeshPiece("Ski Stand", table.ToMesh(), snowy);
            tablePiece.Transform.position = b.WorldPoint(stand.x, stand.y, 0f, false);
            Color[] skiColours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.95f, 0.75f, 0.1f), new Color(0.15f, 0.6f, 0.3f) };
            Color[] boardColours = { new Color(0.1f, 0.45f, 0.8f), new Color(0.6f, 0.2f, 0.7f) };
            for (int i = 0; i < 6; i++)
            {
                Vector2 at = stand + IsleKit.Turn(-2.0f + i * 0.36f, 0f, sy);
                Piece skis = b.Item("Skis " + (i + 1), SkisMesh(skiColours[i % 3]), bare, at.x, at.y, 0.5f, 3f, false).Set("PuzzleItem", "savesOnReload", false);
                skis.Transform.rotation = Quaternion.Euler(0f, sy, 0f);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector2 at = stand + IsleKit.Turn(0.45f + i * 0.5f, 0f, sy);
                Piece snowboard = b.Item("Snowboard " + (i + 1), BoardMesh(boardColours[i % 2]), bare, at.x, at.y, 0.5f, 3f, false).Set("PuzzleItem", "savesOnReload", false);
                snowboard.Transform.rotation = Quaternion.Euler(0f, sy, 0f);
            }
            Vector2 behind = stand + IsleKit.Turn(0f, -1.6f, sy);
            IsleKit.BoardOnPosts(b, "Ski Stand Sign", behind.x, behind.y, sy, 2.4f, 1.5f, 2.2f, IsleKit.Red, false);
            Vector2 signFace = behind + IsleKit.Turn(0f, IsleKit.FaceZ + 0.06f, sy);
            IsleKit.PaintedWords(b, "Ski Stand Words", "SkiStand", behind.x + IsleKit.Turn(0f, 0.07f, sy).x, behind.y + IsleKit.Turn(0f, 0.07f, sy).y, sy, 2.4f, 1.5f, 0.7f, false);
        }
        Debug.Log("[BWM] Summit Disc Golf: 2 baskets and " + Discs + " frisbees on the summit");
    }
}
