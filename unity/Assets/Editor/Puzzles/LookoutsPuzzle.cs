// Lookouts (puzzle 27): the Winter Isle's sister to the game's press-together challenge, spread across the whole
// island. Lookout stations stand on four high places, each with a telescope, a beacon mast lit in its own colour
// and a press-together lever. Every lever has to be pulled within the same two seconds; one pulled alone goes red and
// locks for a while. The lookouts are a kilometre and more apart, far beyond shouting, so the walkers split up, find
// their stations (the observatory's telescopes are aimed at them, each beside a pennant of its colour) and count
// down together over the walkie-talkies from the gear stations. Two lookouts for two players, a third for three, a
// fourth for four: North on the hill west of the Plaza Halt, South on the hills above the airport, Summit on the
// summit's plateau (by the gondola), and Far West on a shelf above the West Cliffs station.
using System.Collections.Generic;
using System.Linq;
using Bwm;
using UnityEngine;

public sealed class LookoutsPuzzle : PuzzleDef
{
    public override string Name => "Lookouts";
    public override Vector2 Footprint => new Vector2(26f, 22f);
    public override Vector2? FixedSite => new Vector2(-1010f, -1280f);

    // the lookout stations (world x, z), in the order players get them: two, then a third, then a fourth
    public static readonly Vector2[] Stations = { new Vector2(-1400f, -980f), new Vector2(-1250f, -2000f), new Vector2(-2244.5f, -1915f), new Vector2(-2750f, -2120f) };      // (the third on the summit's plateau, beside the sanctuary)
    static readonly string[] Names = { "North", "South", "Summit", "Far West" };
    static readonly Color[] Colours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.12f, 0.6f, 0.25f), new Color(0.98f, 0.75f, 0.2f), new Color(0.2f, 0.4f, 0.95f) };
    const float StationRadius = 7f;
    // each lookout's ground, for the island to level and keep clear
    // (not the summit's: the summit is level ground already, and a flat there would cut into its plateau)
    public override IEnumerable<Vector3> ExtraSites => Stations.Where((s, i) => i != 2).Select(s => new Vector3(s.x, s.y, StationRadius));

    PuzzleBuilder b;
    Vector3 site;

    Vector2 Local(Vector2 world) => new Vector2(world.x - site.x, world.y - site.z);
    static float Bearing(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;

    // A lookout at world 'at': a round stone platform, a telescope aimed at the observatory, the lever on a post
    // beside it, and a beacon mast with a lantern of the lookout's colour and lights wound round it; under 'parent'
    // (a lobby group) when given. Returns the lever
    Piece Station(int i, Piece parent)
    {
        Vector2 at = Stations[i], c = Local(at);
        float toObservatory = Bearing(at, new Vector2(site.x, site.z));
        var mb = new MeshBuilder();
        var glow = new MeshBuilder();
        Kit.AddDisc(mb, new Vector3(0f, -3.0f, 0f), 4.6f, 3.3f, IsleKit.Stone, 20);      // a deep drum, so it never floats on a slope
        Vector2 mast = IsleKit.Turn(-2.8f, -1.6f, toObservatory), post = IsleKit.Turn(1.3f, 0.6f, toObservatory);
        Kit.AddBox(mb, new Vector3(mast.x, 0.3f, mast.y), new Vector3(0.34f, 11f, 0.34f), IsleKit.Wood);
        Kit.AddBox(mb, new Vector3(mast.x, 11.3f, mast.y), new Vector3(1.4f, 0.2f, 1.4f), IsleKit.DarkWood);
        Kit.AddBox(mb, new Vector3(mast.x, 12.8f, mast.y), new Vector3(1.6f, 0.2f, 1.6f), IsleKit.DarkWood);
        Kit.AddGlow(glow, new Vector3(mast.x, 12.05f, mast.y), new Vector3(1.0f, 1.3f, 1.0f), 0f, Colours[i], i * 0.25f);      // the beacon
        Kit.AddBox(mb, new Vector3(post.x, 0.3f, post.y), new Vector3(0.24f, 1.5f, 0.2f), IsleKit.Wood, toObservatory);
        Kit.AddBox(mb, new Vector3(post.x, 1.8f, post.y), new Vector3(0.9f, 0.5f, 0.06f), Colours[i], toObservatory);      // a board of its colour
        Piece platform = b.MeshPiece("Lookout " + Names[i], mb.ToMesh(), IsleKit.Snowy(b));
        platform.Transform.position = b.WorldPoint(c.x, c.y, 0f);
        Piece lamp = b.MeshPiece("Lookout " + Names[i] + " Beacon", glow.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        lamp.Transform.position = b.WorldPoint(c.x, c.y, 0f);
        Vector2 scope = IsleKit.Turn(-0.6f, 1.2f, toObservatory);
        Piece telescope = b.Part("Telescope", c.x + scope.x, c.y + scope.y, 0.3f, toObservatory).Named("Lookout " + Names[i] + " Telescope");
        Vector2 face = IsleKit.Turn(1.3f, 0.6f + 0.12f, toObservatory);
        Piece lever = b.Part("Button Sim", c.x + face.x, c.y + face.y, 1.25f, toObservatory).Named("Lookout " + Names[i] + " Lever");
        lever.Set("PuzzleSimPressSwitch", "group", "lookouts").Set("PuzzleSimPressSwitch", "activeSeconds", 2f).Set("PuzzleSimPressSwitch", "timeoutSeconds", 4f);
        Piece lights = Kit.LightRing(b, "Lookout " + Names[i] + " Lights", c.x, c.y, 9.8f, 9.8f, 2.2f, true, 60 + i);      // (sizes across, not radii)
        if (parent != null) { platform.Under(parent); lamp.Under(parent); telescope.Under(parent); lever.Under(parent); if (lights != null) lights.Under(parent); }
        return lever;
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        b.DebugTeleport(0f, -6f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the lookouts: two always, the third for three or more players, the fourth for four ----
        Piece first = Station(0, null);
        Station(1, null);
        Station(2, IsleKit.Lobby(b, "Third Lookout", false, true, true));
        Station(3, IsleKit.Lobby(b, "Fourth Lookout", false, false, true));
        first.On("PuzzleSimPressSwitch", "onSuccess", b.Puzzle, "Win");      // every lever in the group fires it: wire one

        // ---- the observatory: a round stone terrace with a parapet, a telescope at its rim aimed at each lookout
        //      beside a pennant of that lookout's colour, the start corner, lights ----
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, new Vector3(0f, -0.5f, 0f), 9f, 0.8f, IsleKit.Stone, 28);
        for (int k = 0; k < 28; k++)
        {
            float a = 2f * Mathf.PI * k / 28f;
            if (k % 7 == 3) continue;      // gaps in the parapet to walk through
            Kit.AddBox(mb, new Vector3(Mathf.Sin(a) * 8.6f, 0.3f, Mathf.Cos(a) * 8.6f), new Vector3(1.9f, 0.7f, 0.5f), IsleKit.Stone * 0.9f, a * Mathf.Rad2Deg);
        }
        b.MeshPiece("Observatory", mb.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);
        for (int i = 0; i < Stations.Length; i++)
        {
            float bearing = Bearing(new Vector2(site.x, site.z), Stations[i]);
            Vector2 at = IsleKit.Turn(0f, 6.6f, bearing), flag = IsleKit.Turn(1.3f, 6.6f, bearing);
            Piece telescope = b.Part("Telescope", at.x, at.y, 0.3f, bearing).Named("Observatory Telescope " + Names[i]);
            var pennant = new MeshBuilder();
            Kit.AddBox(pennant, new Vector3(flag.x, 0.3f, flag.y), new Vector3(0.08f, 2.6f, 0.08f), IsleKit.DarkWood);
            Kit.AddBox(pennant, new Vector3(flag.x, 2.3f, flag.y) + new Vector3(IsleKit.Turn(0.35f, 0f, bearing).x, 0f, IsleKit.Turn(0.35f, 0f, bearing).y), new Vector3(0.7f, 0.45f, 0.03f), Colours[i], bearing + 90f);
            Piece flagPiece = b.MeshPiece("Observatory Pennant " + Names[i], pennant.ToMesh(), IsleKit.Bare(b));
            flagPiece.Transform.position = b.WorldPoint(0f, 0f);
            if (i >= 2)
            {
                Piece group = IsleKit.Lobby(b, "Observatory " + Names[i], false, i == 2, true);
                telescope.Under(group); flagPiece.Under(group);
            }
        }
        IsleKit.StartCorner(b, 0f, -3.2f, 180f);
        Kit.LightRing(b, "Observatory Lights", 0f, 0f, 19.6f, 19.6f, 2.4f, true, 71);
        Debug.Log("[BWM] Lookouts: " + string.Join(", ", Stations.Select((s, i) => Names[i] + " at " + Sdk.F(s.x) + ", " + Sdk.F(s.y) + " (" + Sdk.F((s - new Vector2(site.x, site.z)).magnitude) + " m)")));
    }
}
