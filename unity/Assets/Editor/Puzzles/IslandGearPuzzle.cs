// Island Gear: the game's own tools laid out for the taking at five gear stations round the Winter Isle, so a group
// can kit itself out the way it would on the mainland: binoculars, walkie-talkies, a megaphone, torches, a compass,
// the GPS and the map, laser pointers, night-vision goggles, and a backpack and belt on hooks to carry it all in.
// Each station is a little roofed table with lights along its eave: at the causeway's landing, beside the plaza,
// at the gondola's top station, by the ski lift at the lodge, and at the mining yard. A scenery pack (nothing to
// win); its items are not saved, so a reload puts every one back on its table.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class IslandGearPuzzle : PuzzleDef
{
    public override string Name => "IslandGear";
    public override int PuzzleType => 2;
    public override Vector2 Footprint => new Vector2(4f, 4f);
    public override Vector2? FixedSite => WinterIslePuzzle.Plaza;

    // the game's item types (PuzzleVanillaItemType, by index)
    const int Torch = 2, FmRadio = 3, Walkie = 4, Binoculars = 5, Clock = 7, Laser = 8, Megaphone = 9, Belt = 15, Backpack = 16, Gps = 17, Map = 18, Compass = 19, NightVision = 29;

    PuzzleBuilder b;
    Vector3 site;

    // The nearest spot to 'want' (world x, z) within 'reach' metres that is island ground, level enough and not taken by
    // the island's trees, rocks or paths
    // (first clear of anything the island has marked as taken; failing that, any level ground: the island marks its
    // paths, levelled flats and structures as taken too, and the landing, the summit and the mining yard are all that)
    bool FindSpot(Vector2 want, float reach, out Vector2 spot)
    {
        foreach (bool strict in new[] { true, false })
            for (float r = 0f; r <= reach; r += 2f)
                for (int k = 0; k < (r == 0f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 2f)); k++)
                {
                    float a = r == 0f ? 0f : 2f * Mathf.PI * k / Mathf.CeilToInt(2f * Mathf.PI * r / 2f);
                    var p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    float lo, hi;
                    if (strict ? !b.Ground.Range(p.x, p.y, 3f, 2.2f, out lo, out hi) : !Level(p, out lo, out hi)) continue;
                    if (hi - lo > 1.2f || lo < 1f) continue;
                    spot = p;
                    return true;
                }
        spot = want;
        return false;
    }
    bool Level(Vector2 p, out float lo, out float hi)
    {
        lo = float.MaxValue; hi = float.MinValue;
        for (float dz = -2.2f; dz <= 2.21f; dz += 1.1f)
            for (float dx = -3f; dx <= 3.01f; dx += 1f)
            {
                if (!b.Ground.At(p.x + dx, p.y + dz, out float y)) return false;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
        return true;
    }

    // A station at world (x, z) facing 'yaw': posts, a pitched roof, a table with the items on it, a back rail with a
    // backpack and a belt on hooks, and lights along the front eave
    void Station(string name, Vector2 at, float yaw, int[] items)
    {
        float lx = at.x - site.x, lz = at.y - site.z;
        Quaternion t = Quaternion.Euler(0f, yaw, 0f);
        Vector3 L(float x, float y, float z) => t * new Vector3(x, y, z);
        var mb = new MeshBuilder();
        var timber = new Color(0.42f, 0.29f, 0.18f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                Kit.AddBox(mb, L(sx * 2.1f, 0f, sz * 1.1f), new Vector3(0.18f, sz < 0 ? 2.3f : 2.7f, 0.18f), timber, yaw);
        Kit.AddBox(mb, L(0f, 2.35f, -0.35f), new Vector3(4.8f, 0.12f, 1.6f), new Color(0.62f, 0.12f, 0.1f), yaw);      // the roof, front half
        Kit.AddBox(mb, L(0f, 2.62f, 0.75f), new Vector3(4.8f, 0.12f, 1.2f), new Color(0.55f, 0.1f, 0.08f), yaw);       // and back
        Kit.AddBox(mb, L(0f, 0f, 0.15f), new Vector3(3.8f, 0.85f, 0.8f), timber * 1.1f, yaw);                          // the table
        Kit.AddBox(mb, L(0f, 0.9f, 1.05f), new Vector3(4.2f, 1.2f, 0.08f), timber * 0.9f, yaw);                        // the back board
        Kit.AddBox(mb, L(0f, 2.75f, -1.2f), new Vector3(2.2f, 0.4f, 0.06f), new Color(0.1f, 0.33f, 0.18f), yaw);       // the sign over the front
        Piece shelter = b.MeshPiece(name, mb.ToMesh(), IsleKit.Snowy(b));
        shelter.Transform.position = b.WorldPoint(lx, lz);
        // the tools on the table, in a row
        for (int i = 0; i < items.Length; i++)
        {
            Vector3 p = L(-1.6f + i * (3.2f / Mathf.Max(1, items.Length - 1)), 0.85f + 0.12f, 0.05f);
            b.Part("Vanilla Item", lx + p.x, lz + p.z, p.y, yaw + 180f).Named(name + " " + i)
                .Set("PuzzleVanillaItem", "itemType", items[i]).Set("PuzzleVanillaItem", "savesOnReload", false);
        }
        // a backpack and a belt on hooks on the back board
        foreach (var (x, type, flag) in new[] { (-1.2f, Backpack, 1 << 16), (1.2f, Belt, 1 << 15) })
        {
            Vector3 hookAt = L(x, 1.75f, 0.98f), itemAt = L(x, 1.4f, 0.8f);
            Piece item = b.Part("Vanilla Item", lx + itemAt.x, lz + itemAt.z, itemAt.y, yaw + 180f).Named(name + (type == Backpack ? " Backpack" : " Belt"))
                .Set("PuzzleVanillaItem", "itemType", type).Set("PuzzleVanillaItem", "savesOnReload", false);
            Piece hook = b.Part("Hook", lx + hookAt.x, lz + hookAt.z, hookAt.y, yaw + 180f).Named(name + " Hook " + (type == Backpack ? "1" : "2"));
            hook.Set("PuzzleHook", "defaultItem", item.Component("PuzzleVanillaItem"));
            Sdk.SetFlags(hook.Component("PuzzleHook"), "acceptedVanilla", flag);
        }
        Vector3 e0 = L(-2.3f, 2.3f, -1.15f), e1 = L(2.3f, 2.3f, -1.15f);
        Kit.LightString(b, name + " Lights", new List<Vector3> { new Vector3(lx + e0.x, e0.y, lz + e0.z), new Vector3(lx + e1.x, e1.y, lz + e1.z) }, 0.2f, 0.35f, true, false, name.Length);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        var all = new[] { Binoculars, Walkie, Walkie, Megaphone, Torch, Compass, Gps, Map, Laser };
        var wanted = new List<(string name, Vector2 at, int[] items)>
        {
            ("Landing Gear", new Vector2(-470f, -772f), all),
            ("Plaza Gear", WinterIslePuzzle.Plaza + new Vector2(22f, -24f), new[] { Binoculars, Walkie, Walkie, Megaphone, Torch, Clock, Map, Laser, NightVision }),
            ("Summit Gear", WinterIslePuzzle.GondolaTop + (WinterIslePuzzle.GondolaTop - WinterIslePuzzle.Summit).normalized * 22f, new[] { Binoculars, Binoculars, Walkie, Walkie, Megaphone, Gps, Compass, Laser }),
            ("Lodge Gear", WinterIslePuzzle.SkiLiftBottom + (WinterIslePuzzle.PisteFinish(out _, out _) - WinterIslePuzzle.SkiLiftBottom).normalized * 18f, new[] { Walkie, Walkie, Binoculars, Megaphone, Gps, Map, FmRadio, Torch }),
        };
        if (WinterIsleMine.Load())
            wanted.Add(("Mine Gear", WinterIsleMine.Tower + new Vector2(WinterIsleMine.TowerFacing.y, -WinterIsleMine.TowerFacing.x) * 14f, new[] { Torch, Torch, Torch, NightVision, NightVision, Walkie, Walkie, Map, Clock }));
        // the Winter Isle's close-up for the game's folding map (tools/islemap.py draws it; Winter Walk's MapDriver
        // stamps it onto the map): carried on a speck under the plaza, since a picture only reaches the game on a material
        {
            var picture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Map/WinterIsleMap.png");
            if (picture == null) throw new System.Exception("No Assets/Textures/Map/WinterIsleMap.png: run tools\\islemap.py");
            var speck = new MeshBuilder();
            Kit.AddBox(speck, new Vector3(0f, -40f, 0f), Vector3.one * 0.01f, Color.white);
            b.MeshPiece("Map Overlay", speck.ToMesh(), b.ShaderMaterial("BWM/MapStamp", "Map Stamp", m => m.SetTexture("_MainTex", picture)), false);
        }
        int built = 0;
        foreach (var w in wanted)
        {
            if (!FindSpot(w.at, 34f, out Vector2 spot)) { Debug.LogWarning("[BWM] Island Gear: no room for " + w.name + " near " + w.at); continue; }
            // facing the plaza for those in the lowland, down the slope on the mountain: towards where people come from
            Vector2 toward = (WinterIslePuzzle.Plaza - spot).sqrMagnitude > 1f ? (WinterIslePuzzle.Plaza - spot).normalized : Vector2.up;
            float yaw = Mathf.Atan2(-toward.x, -toward.y) * Mathf.Rad2Deg;      // the open front (-z) towards them
            Station(w.name, spot, yaw, w.items);
            built++;
            Debug.Log("[BWM] Island Gear: " + w.name + " at " + Sdk.F(spot.x) + ", " + Sdk.F(spot.y) + " (" + Sdk.F((spot - w.at).magnitude) + " m from where it was wanted)");
        }
        Debug.Log("[BWM] Island Gear: " + built + " gear stations");
    }
}
