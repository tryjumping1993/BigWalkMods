// Summit Disc Golf (a scenery pack): two portable disc golf baskets and six frisbees on the pizzeria's terrace at the
// summit, all of them things players can pick up, carry and put down anywhere, so a group can lay out its own holes
// (or carry a basket down the mountain). The frisbees wait on a rack beside a tee mat and a sign; the baskets stand
// in front of the shop and down the alley beside it to begin with. Nothing to win. Items are not saved: a reload puts them back.
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
        for (int i = 0; i < DiscColours.Length; i++)
        {
            Vector2 at = tee + IsleKit.Turn(-2.2f - 0.75f + (i % 3) * 0.75f, -1.2f - 0.18f + (i / 3) * 0.36f, yaw);
            b.Item("Frisbee " + (i + 1), DiscMesh(DiscColours[i]), bare, at.x, at.y, 0.93f, 0.25f, false);
        }
        Vector2 board = tee + IsleKit.Turn(2.3f, -1.4f, yaw);
        IsleKit.BoardOnPosts(b, "Disc Golf Sign", board.x, board.y, yaw, 1.6f, 1.2f, 2.0f, IsleKit.Green, false);
        Vector2 face = board + IsleKit.Turn(0f, IsleKit.FaceZ + 0.06f, yaw);
        SetText(b.Part("Sign", face.x, face.y, 1.6f, yaw, false).Named("Disc Golf Words"), "SUMMIT DISC GOLF", 2.6f);
        Kit.LightString(b, "Disc Golf Lights", new List<Vector3>
        {
            new Vector3(board.x + IsleKit.Turn(-1.0f, 0f, yaw).x, 2.4f, board.y + IsleKit.Turn(-1.0f, 0f, yaw).y),
            new Vector3(board.x + IsleKit.Turn(1.0f, 0f, yaw).x, 2.4f, board.y + IsleKit.Turn(1.0f, 0f, yaw).y),
        }, 0.2f, 0.3f, false, false, 97);
        Debug.Log("[BWM] Summit Disc Golf: 2 baskets and " + DiscColours.Length + " frisbees on the summit");
    }
}
