// Advent Trail: the Advent Post. A puzzle of going places, for any number of players, with no clock.
//
// At the Advent Post (the puzzle's site, a short walk from the plaza) stands the calendar board: five doors, one
// picture over each, dealt anew at every start, and a peg slot under each. The five parcels that carry those pictures
// are not here. They wait in five red post boxes across the east of the island: at the landing, and on the flat of
// the Plaza, East Lowland, Northwest and North Shore railway stations. A fingerpost at the Post points an arrow at
// each, with a pip for every hundred metres, and every box carries a striped mast with a gold star. Fetch
// the parcels, on foot, by jetpack, by mantrip or by train, and hang each under its own picture; then press check.
// A wrong board only shows the cross. One player can finish it; a group splits up and is back in a few minutes.
// (Before the user's rules of 2026-11-24 this was a game of telephone: a Keeper sealed in a soundproof house, a
// microphone into the headphones of a Listener in a booth, and a Postmaster in the yard, on a clock with strikes.)
//
// The post boxes look for their own ground, as Island Gear's firework posts do: level island ground on each
// station's flat that the island's dressing left free (WinterIsle.dressing beside the Winter Walk plugin), clear of
// where that station's firework post stands. So build WinterIsle first, and this pack again after the island's
// dressing changes. The site's coordinates are local metres (x east, z north), heights above the ground.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class AdventTrailPuzzle : PuzzleDef
{
    public override string Name => "AdventTrail";
    public override Vector2 Footprint => new Vector2(20f, 38f);
    public override Vector2? FixedSite => new Vector2(-614f, -712f);      // layout-final: a quiet slope 101 m by path from the plaza

    const float StandZ = 3f, StandFace = StandZ - 0.06f;     // the calendar board faces south
    const float SlotOne = -0.9f, SlotPitch = 0.45f;          // a Peg Board's pivot is its first slot; the rest follow 0.45 m apart
    const float DisplayBack = 0.168f;                        // a Display's back is this far behind its pivot (part-metrics)
    static readonly Color Brass = new Color(0.86f, 0.68f, 0.3f);
    static readonly Vector2 Landing = new Vector2(-482f, -760f);      // beside the island end of the covered bridge

    PuzzleBuilder b;
    Vector3 site;
    List<(Vector2 at, float keep)> dressing;
    int lamps;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        b.ClearGround(20f, 38f);
        b.DebugTeleport(0f, -6f);
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);

        // ================= the calendar board: five doors, a picture over each, a slot under each =================
        IsleKit.BoardOnPosts(b, "Calendar Board", 0f, StandZ, 180f, 4.6f, 0.7f, 2.7f, IsleKit.Board);
        var pictures = new Object[5];
        var marks = new MeshBuilder();
        for (int k = 0; k < 5; k++)
        {
            float x = SlotOne + SlotPitch * k;
            pictures[k] = b.Part("Display", x, StandFace - DisplayBack - 0.005f, 2.05f, 180f).Named("Door " + (k + 1))
                .Set("PuzzleDisplay", "size", 0).Component("PuzzleDisplay");      // one flap
            // the door's number in brass pips between its picture and its slot
            for (int pip = 0; pip <= k; pip++)
                Kit.AddBox(marks, new Vector3(x + (pip - k / 2f) * 0.07f, 1.64f, -0.075f), new Vector3(0.045f, 0.045f, 0.02f), Brass);
            // and a red door frame round the picture
            Kit.AddBox(marks, new Vector3(x, 2.3f, -0.072f), new Vector3(0.44f, 0.03f, 0.02f), IsleKit.Red);
            Kit.AddBox(marks, new Vector3(x - 0.22f, 1.8f, -0.072f), new Vector3(0.02f, 0.53f, 0.02f), IsleKit.Red);
            Kit.AddBox(marks, new Vector3(x + 0.22f, 1.8f, -0.072f), new Vector3(0.02f, 0.53f, 0.02f), IsleKit.Red);
        }
        b.MeshPiece("Calendar Marks", marks.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, StandZ);
        Piece pegs = b.Part("Peg Board", SlotOne, StandFace, 1.3f, 180f).Named("Calendar Pegs").Set("PuzzlePegBoard", "size", 4);      // five slots
        Piece check = b.Part("Validator", 1.95f, StandFace, 1.6f, 180f).Named("Calendar Check Screen");
        Component pegsC = pegs.Component("PuzzlePegBoard"), checkC = check.Component("PuzzleValidator");
        Sdk.Add(pegsC, "answerFrom", pictures);
        Sdk.AddInts(pegsC, "answerFromFlap", 1, 1, 1, 1, 1);
        Sdk.Set(pegsC, "validator", checkC);
        // The Validator prefab carries its own poke button: one press checks. A right board wins by itself, and
        // onIncorrect is not wired, so a wrong one only shows the cross.
        Component poke = check.Component("PuzzlePokeButton");
        poke.gameObject.name = "Calendar Check";
        Sdk.Set(checkC, "pokeButton", poke);

        IsleKit.StartCorner(b, 5.5f, -1.5f, 180f);
        Kit.LightRing(b, "Post Lights", 0f, 1f, 14f, 12f, 2.6f, true, 71);

        // ================= the five post boxes =================
        var places = new List<(string name, Vector2 at, Vector2 toward)>();
        if (Near(Landing, 30f, null, out Vector2 landing)) places.Add(("Landing", landing, Landing));
        else Debug.LogWarning("[BWM] Advent Trail: no room for a post box at the landing");
        if (WinterIsleRail.Load() && WinterIsleRail.Lines.Count > 0)
        {
            RailLine loop = WinterIsleRail.Lines[0];
            foreach (string wanted in new[] { "Plaza", "East Lowland", "Northwest", "North Shore" })
            {
                int s = loop.Names.IndexOf(wanted);
                if (s < 0) { Debug.LogWarning("[BWM] Advent Trail: the railway has no station '" + wanted + "'"); continue; }
                int i = loop.Stops[s];
                Vector3 here = loop.P[i], ahead = loop.P[loop.W(i + 2)];
                Vector2 station = new Vector2(here.x, here.z), along = new Vector2(ahead.x - here.x, ahead.z - here.z).normalized, across = new Vector2(along.y, -along.x);
                if (Vector2.Dot(across, WinterIslePuzzle.Plaza - station) < 0f) across = -across;
                // where Island Gear stands this station's firework post: the first of its candidates that is free
                Vector2? fire = null;
                foreach (float side in new[] { 1f, -1f })
                {
                    foreach (float far in new[] { 8.5f, 7.2f, 9.8f })
                        foreach (float step in new[] { 12f, -12f, 16f, -16f, 20f, -20f, 26f, -26f })
                            if (fire == null && PostSpot(station + along * step + across * (side * far))) fire = station + along * step + across * (side * far);
                    if (fire != null) break;
                }
                // the post box: on the flat too, the far side from the plaza first, 6 m or more from the firework post
                Vector2 spot = Vector2.zero;
                bool found = false;
                foreach (float side in new[] { -1f, 1f })
                    foreach (float far in new[] { 8.5f, 7.2f, 9.8f })
                        foreach (float step in new[] { -14f, 14f, -18f, 18f, -22f, 22f, -28f, 28f })
                        {
                            Vector2 candidate = station + along * step + across * (side * far);
                            if (found || !PostSpot(candidate) || (fire != null && (candidate - fire.Value).magnitude < 6f)) continue;
                            spot = candidate; found = true;
                        }
                if (!found && !Near(station + across * -14f, 30f, fire, out spot)) { Debug.LogWarning("[BWM] Advent Trail: no room for a post box at " + wanted); continue; }
                places.Add((wanted, spot, station));
            }
        }
        else Debug.LogWarning("[BWM] Advent Trail: no railway network: only the landing's post box is built");

        string list = "";
        for (int k = 0; k < places.Count; k++)
        {
            (string name, Vector2 at, Vector2 toward) = places[k];
            Vector2 facing = (toward - at).sqrMagnitude > 1f ? (toward - at).normalized : Vector2.up;
            PostBox(k + 1, name, at, Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, snowy);
            list += (k > 0 ? "; " : "") + name + " " + Sdk.F(at.x) + ", " + Sdk.F(at.y) + " (" + Sdk.F((at - new Vector2(site.x, site.z)).magnitude) + " m)";
        }
        Debug.Log("[BWM] Advent Trail: " + places.Count + " post boxes: " + list);
        if (places.Count < 5) Debug.LogWarning("[BWM] Advent Trail: only " + places.Count + " of 5 post boxes found ground; the board cannot be filled");

        // ================= the fingerpost: where the post boxes are =================
        // One red arrow for each post box, pointing straight at it, with a gold pip on its top edge for every hundred
        // metres of the way (text does not show in the game: the SDK's sign has no material for it there).
        const float postX = -5.2f, postZ = 1.2f;
        var finger = new MeshBuilder();
        Kit.AddBox(finger, new Vector3(0f, -0.3f, 0f), new Vector3(0.16f, 3.5f, 0.16f), IsleKit.DarkWood);
        Kit.AddStar(finger, new Vector3(0f, 3.4f, 0f), 0.22f, 0.05f, IsleKit.Gold);
        for (int k = 0; k < places.Count; k++)
        {
            Vector2 to = places[k].at - new Vector2(site.x + postX, site.z + postZ);
            float yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg, h = 2.75f - 0.36f * k;
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Kit.AddBox(finger, turn * new Vector3(0f, 0f, 0.62f) + Vector3.up * h, new Vector3(0.05f, 0.22f, 1.08f), IsleKit.Red, yaw);      // the board
            Kit.AddBox(finger, turn * new Vector3(0f, 0f, 1.21f) + Vector3.up * (h + 0.04f), new Vector3(0.05f, 0.14f, 0.1f), IsleKit.Red, yaw);   // and its point
            Kit.AddBox(finger, turn * new Vector3(0f, 0f, 1.31f) + Vector3.up * (h + 0.08f), new Vector3(0.05f, 0.06f, 0.1f), IsleKit.Red, yaw);
            int pips = Mathf.Clamp(Mathf.RoundToInt(to.magnitude / 100f), 1, 11);
            for (int pip = 0; pip < pips; pip++)
                Kit.AddBox(finger, turn * new Vector3(0f, 0f, 0.16f + 0.092f * pip) + Vector3.up * (h + 0.22f), new Vector3(0.07f, 0.05f, 0.05f), Brass, yaw);
        }
        b.MeshPiece("Fingerpost", finger.ToMesh(), bare).Transform.position = b.WorldPoint(postX, postZ);
    }

    // One post box at a world place, its open front facing 'yaw': a red box, a shelf with the parcel on it, a back
    // board and a little roof, a striped mast with a star, and a lantern lit at night (Winter Walk's RoomLights)
    void PostBox(int k, string place, Vector2 at, float yaw, Material snowy)
    {
        float lx = at.x - site.x, lz = at.y - site.z;
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        Vector3 L(float x, float y, float z) => turn * new Vector3(x, y, z);
        var mb = new MeshBuilder();
        Kit.AddBox(mb, L(0f, 0f, 0f), new Vector3(0.7f, 0.95f, 0.55f), IsleKit.Red, yaw);                  // the box
        Kit.AddBox(mb, L(0f, 0.95f, 0f), new Vector3(0.8f, 0.05f, 0.65f), IsleKit.DarkWood, yaw);          // its top: the parcel's shelf
        Kit.AddBox(mb, L(0f, 1.0f, -0.3f), new Vector3(0.8f, 0.75f, 0.05f), IsleKit.Wood, yaw);            // the back board
        Kit.AddBox(mb, L(-0.38f, 1.0f, -0.05f), new Vector3(0.04f, 0.75f, 0.5f), IsleKit.Wood, yaw);       // and sides
        Kit.AddBox(mb, L(0.38f, 1.0f, -0.05f), new Vector3(0.04f, 0.75f, 0.5f), IsleKit.Wood, yaw);
        Kit.AddBox(mb, L(0f, 1.75f, 0f), new Vector3(1.05f, 0.08f, 0.85f), IsleKit.DarkWood, yaw);         // the roof
        Kit.AddBox(mb, L(0f, 0.42f, 0.28f), new Vector3(0.36f, 0.06f, 0.02f), Brass, yaw);                 // the letter slot
        // a candy-striped mast with a gold star behind it, to be seen from across the station
        for (int band = 0; band < 8; band++)
            Kit.AddBox(mb, L(0f, 1.83f + 0.3f * band, -0.36f), new Vector3(0.1f, 0.3f, 0.1f), band % 2 == 0 ? IsleKit.Red : IsleKit.Cream, yaw);
        Kit.AddStar(mb, L(0f, 4.45f, -0.36f), 0.3f, 0.06f, IsleKit.Gold);
        Piece box = b.MeshPiece("Post Box " + place, mb.ToMesh(), snowy);
        box.Transform.position = b.WorldPoint(lx, lz);
        Vector3 shelf = L(0f, 0f, 0.02f);
        // the parcel: a Peg Tile, picture k of the Shapes set, standing on the shelf facing out
        b.Part("Peg Tile", lx + shelf.x, lz + shelf.z, 1.0f + 0.224f + 0.01f, yaw).Named("Parcel " + k)
            .Set("PuzzlePegTile", "imageSet", 0).Set("PuzzlePegTile", "image", k);
        Vector3 lamp = L(0f, 0f, 0.6f);
        b.Empty("Room Light Night " + (++lamps) + " FFCB8C 6.0 1.2", lx + lamp.x, lz + lamp.z, 1.9f);
    }

    // The nearest spot to 'want' (world x, z) within 'reach' metres where a post box can stand, 6 m or more from 'avoid'
    bool Near(Vector2 want, float reach, Vector2? avoid, out Vector2 spot)
    {
        for (float r = 0f; r <= reach; r += 2f)
        {
            int n = r == 0f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 2f);
            for (int k = 0; k < n; k++)
            {
                float a = 2f * Mathf.PI * k / n;
                Vector2 p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (!PostSpot(p) || (avoid != null && (p - avoid.Value).magnitude < 6f)) continue;
                spot = p;
                return true;
            }
        }
        spot = want;
        return false;
    }

    // Whether a post can stand at p: level island ground that the dressing left free (Island Gear's rule, so the
    // firework posts' places can be told from here)
    bool PostSpot(Vector2 p)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var o in new[] { Vector2.zero, Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            if (!b.Ground.At(p.x + o.x, p.y + o.y, out float y)) return false;
            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        return lo >= 1f && hi - lo <= 0.6f && DressingClear(p);
    }

    bool DressingClear(Vector2 p)
    {
        if (dressing == null)
        {
            dressing = new List<(Vector2, float)>();
            string file = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.dressing"));
            if (System.IO.File.Exists(file))
                foreach (string line in System.IO.File.ReadLines(file))
                {
                    if (line.Length == 0 || line[0] == '#' || line[0] == '@') continue;
                    string[] f = line.Split('\t');
                    if (f.Length < 8) continue;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (!float.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out float x) || !float.TryParse(f[3], System.Globalization.NumberStyles.Float, inv, out float z)
                        || !float.TryParse(f[7], System.Globalization.NumberStyles.Float, inv, out float scale)) continue;
                    dressing.Add((new Vector2(x, z), f[0].StartsWith("rock") ? 1.2f + scale * 1.3f : f[0].StartsWith("fir") ? 3f : f[0].Contains("Litter") || f[0].Contains("Grass") || f[0].Contains("Rush") ? 1.4f : 2.4f));
                }
            else Debug.LogWarning("[BWM] Advent Trail: no WinterIsle.dressing (build WinterIsle first): the post boxes cannot keep clear of the island's trees and rocks");
        }
        foreach (var d in dressing) if ((d.at - p).sqrMagnitude < d.keep * d.keep) return false;
        return true;
    }
}
