// Island Gear: the game's own tools laid out for the taking at five gear stations round the Winter Isle, so a group
// can kit itself out the way it would on the mainland: binoculars, walkie-talkies, a megaphone, torches, a compass,
// the GPS and the map, laser pointers, night-vision goggles, and a backpack and belt on hooks to carry it all in.
// Each station is a little roofed table with lights along its eave: at the causeway's landing, beside the plaza,
// at the gondola's top station, by the ski lift at the lodge, and at the mining yard. A scenery pack (nothing to
// win); its items are not saved, so a reload puts every one back on its table.
//
// Firework posts (the user, 2026-11-24: "fire-work stations similar to the base game that lets players who got lost
// ... provide a location that the other players can easily see"): the game's flare launcher on a striped post with a
// button that fires it, under a sign with a gold burst and a lamp. One stands beside every gear station and one by
// each of the railway's outlying stations, so wherever a walker is lost there is one to walk to.
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
    int lamps, posts;

    // A firework post at world (x, z), its button towards 'yaw' + 180 (the front is -z, as a gear station's is)
    void FireworkPost(string name, Vector2 at, float yaw)
    {
        float lx = at.x - site.x, lz = at.y - site.z;
        Quaternion t = Quaternion.Euler(0f, yaw, 0f);
        Vector3 L(float x, float y, float z) => t * new Vector3(x, y, z);
        var mb = new MeshBuilder();
        Color timber = new Color(0.42f, 0.29f, 0.18f), red = new Color(0.72f, 0.09f, 0.08f), white = new Color(0.93f, 0.92f, 0.88f), gold = new Color(0.95f, 0.72f, 0.2f);
        Kit.AddBox(mb, L(0f, -0.3f, 0f), new Vector3(0.24f, 1.5f, 0.24f), timber, yaw);                    // the post, 1.2 m out of the ground
        for (int band = 0; band < 4; band++)                                                               // painted red and white, as a firework is
            Kit.AddBox(mb, L(0f, 0.62f + band * 0.14f, 0f), new Vector3(0.27f, 0.14f, 0.27f), band % 2 == 0 ? red : white, yaw);
        Kit.AddBox(mb, L(0f, 1.2f, 0f), new Vector3(0.56f, 0.05f, 0.56f), timber * 0.85f, yaw);            // the launcher's tray
        Kit.AddBox(mb, L(0f, -0.3f, 0.36f), new Vector3(0.1f, 2.95f, 0.1f), timber, yaw);                   // a mast behind it for the sign
        Kit.AddBox(mb, L(0f, 2.1f, 0.3f), new Vector3(0.84f, 0.56f, 0.05f), red, yaw);                      // the sign's board
        // on the board, a rocket's trail and its burst, in gold
        Kit.AddBox(mb, L(0f, 2.15f, 0.265f), new Vector3(0.035f, 0.2f, 0.02f), gold, yaw);
        Kit.AddBox(mb, L(0f, 2.37f, 0.265f), new Vector3(0.07f, 0.07f, 0.02f), gold, yaw);
        for (int ray = 0; ray < 8; ray++)
        {
            float a = ray * Mathf.PI / 4f + Mathf.PI / 8f;
            foreach (float r in new[] { 0.12f, 0.2f })
                Kit.AddBox(mb, L(Mathf.Cos(a) * r, 2.405f + Mathf.Sin(a) * r - 0.02f, 0.265f), new Vector3(0.04f, 0.04f, 0.02f), r > 0.15f ? white : gold, yaw);
        }
        Piece post = b.MeshPiece(name, mb.ToMesh(), IsleKit.Snowy(b));
        post.Transform.position = b.WorldPoint(lx, lz);
        // a lamp under the sign, lit at night, so the post and its button can be found in the dark
        var lampGlass = new MeshBuilder();
        Kit.AddGlow(lampGlass, L(0f, 1.98f, 0.3f), new Vector3(0.18f, 0.2f, 0.18f), yaw, new Color(1f, 0.78f, 0.42f), (name.Length * 0.17f) % 1f);
        Piece lamp = b.MeshPiece(name + " Lamp", lampGlass.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        lamp.Transform.position = post.Transform.position;
        var lampMark = new GameObject("Room Light Night " + (++lamps) + " FFCB8C 6.0 1.1");
        lampMark.transform.SetParent(post.Go.transform, false);
        lampMark.transform.localPosition = L(0f, 1.85f, 0.05f);
        // the game's flare launcher on the tray, and the button on the post's front that fires it. (Fire() runs on the
        // host whoever presses; the launcher has its own cool-down, and the button comes back after it)
        Vector3 lp = L(0f, 1.25f + 0.21f, 0f), bp = L(0f, 1.0f, -0.135f);
        Piece launcher = b.Part("Firework Launcher", lx + lp.x, lz + lp.z, lp.y, yaw + 180f).Named(name + " Launcher");
        // The game's launcher aims 53 degrees up, forwards, and its flare's trail then runs off across the sky; tilted
        // back the rest of the way it goes straight up, and the trail stands over the post for all to see where it is
        launcher.Transform.rotation = Quaternion.Euler(-37f, yaw + 180f, 0f);
        Piece button = b.Part("Button Poke", lx + bp.x, lz + bp.z, bp.y, yaw + 180f).Named(name + " Button").Set("PuzzlePokeButton", "autoEnableSeconds", ButtonBack);
        button.On("PuzzlePokeButton", "onPressed", launcher.Component("PuzzleFireworkLauncher"), "Fire");
        posts++;
    }
    // Seconds until a pressed button can be pressed again: the launcher's own cool-down is 40 s (measured in the game,
    // 2026-11-24) and a press during it does nothing, so the button stays dark until the launcher is ready
    const float ButtonBack = 41f;

    // The island's trees, rocks and plants (WinterIsle.dressing, written by the island's build beside the Winter Walk
    // plugin): the island does not know where this pack's posts stand, so a post looks for ground they left free.
    // (The first post stood with its sign in a bush's branches.)
    List<(Vector2 at, float keep)> dressing;
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
                    // (a rock is about 1.3 m from its middle to its edge for each unit of its scale: the largest, at 20 and more, are cliffs 50 m long)
                    dressing.Add((new Vector2(x, z), f[0].StartsWith("rock") ? 1.2f + scale * 1.3f : f[0].StartsWith("fir") ? 3f : f[0].Contains("Litter") || f[0].Contains("Grass") || f[0].Contains("Rush") ? 1.4f : 2.4f));
                }
            else Debug.LogWarning("[BWM] Island Gear: no WinterIsle.dressing (build WinterIsle first): the firework posts cannot keep clear of the island's trees and rocks");
        }
        foreach (var d in dressing) if ((d.at - p).sqrMagnitude < d.keep * d.keep) return false;
        return true;
    }
    // Whether a firework post can stand at p: level island ground that the dressing left free
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
        // a lamp under the roof, lit at night (Winter Walk's RoomLights: "Night" in the marker's name), so the torches
        // can be found in the dark
        var lampGlass = new MeshBuilder();
        Kit.AddGlow(lampGlass, L(0f, 2.2f, -0.35f), new Vector3(0.22f, 0.3f, 0.22f), yaw, new Color(1f, 0.78f, 0.42f), (name.Length * 0.13f) % 1f);
        Piece lamp = b.MeshPiece(name + " Lamp", lampGlass.ToMesh(), b.ShaderMaterial("BWM/FairyLight", "Lantern Glow", m => { m.SetFloat("_Twinkle", 0.12f); m.SetFloat("_Glow", 2.2f); m.SetFloat("_Speed", 3.1f); }), false);
        lamp.Transform.position = shelter.Transform.position;
        var lampMark = new GameObject("Room Light Night " + (++lamps) + " FFCB8C 8.0 1.3");
        lampMark.transform.SetParent(shelter.Go.transform, false);
        lampMark.transform.localPosition = L(0f, 1.9f, -0.35f);
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
        // a firework post beside it: at either end of the shelter, wherever the island's trees and rocks leave room
        Vector2 postAt = Vector2.zero;
        bool postPlaced = false;
        var places = new List<(float px, float pz)> { (3.7f, -0.5f), (-3.7f, -0.5f) };
        foreach (float ring in new[] { 4.6f, 5.8f, 7.2f, 8.8f })      // ...or a little further off, round it
            for (int step = 0; step < 16; step++)
            {
                float a = step * Mathf.PI / 8f;
                places.Add((Mathf.Cos(a) * ring, Mathf.Sin(a) * ring * 0.8f - 0.5f));
            }
        foreach (var (px, pz) in places)
        {
            if (Mathf.Abs(px) < 3.2f && Mathf.Abs(pz) < 2.2f) continue;      // (the shelter itself)
            Vector3 beside = L(px, 0f, pz);
            var candidate = new Vector2(at.x + beside.x, at.y + beside.z);
            if (!postPlaced) postAt = candidate;      // (failing all, the first)
            if (!PostSpot(candidate)) continue;
            postAt = candidate;
            postPlaced = true;
            break;
        }
        if (!postPlaced) Debug.LogWarning("[BWM] Island Gear: no clear ground for a firework post beside " + name + "; it stands at its first place");
        FireworkPost(name.Replace(" Gear", "") + " Fireworks", postAt, yaw);
        // The teleporter to Cast Iron Pizza on the summit (the user, 2026-11-24), at the landing and at the plaza: a
        // signboard facing the way the station does, its pad in front of it, on ground the island's trees and rocks
        // left free and clear of the firework post. Its summit end, and the pad back, are in the Summit Disc Golf pack;
        // travellers coming back arrive in front of the plaza's pad.
        if (name == "Landing Gear" || name == "Plaza Gear")
        {
            Vector3 frontDir = L(0f, 0f, -1f);
            var front2 = new Vector2(frontDir.x, frontDir.z);
            Vector2 boothAt = Vector2.zero;
            bool boothPlaced = false;
            var spots = new List<(float px, float pz)> { (5.4f, -0.3f), (-5.4f, -0.3f), (6.8f, -0.3f), (-6.8f, -0.3f) };
            foreach (float ring in new[] { 7.5f, 9f, 11f, 13f })
                for (int step = 0; step < 16; step++)
                {
                    float a = step * Mathf.PI / 8f;
                    spots.Add((Mathf.Cos(a) * ring, Mathf.Sin(a) * ring * 0.8f - 0.5f));
                }
            foreach (var (px, pz) in spots)
            {
                Vector3 beside = L(px, 0f, pz);
                var candidate = new Vector2(at.x + beside.x, at.y + beside.z);
                if (!boothPlaced && boothAt == Vector2.zero) boothAt = candidate;      // (failing all, the first)
                if ((candidate - postAt).magnitude < 3.5f || (candidate + front2 * 1.4f - postAt).magnitude < 3f) continue;
                if (!PostSpot(candidate) || !PostSpot(candidate + front2 * 1.4f) || !PostSpot(candidate + front2 * 3.6f)) continue;
                boothAt = candidate;
                boothPlaced = true;
                break;
            }
            if (!boothPlaced) Debug.LogWarning("[BWM] Island Gear: no clear ground for the teleporter beside " + name + "; it stands at its first place");
            string stop = name.Replace(" Gear", "");
            IsleKit.TeleportBooth(b, stop, "Pizzeria", "ToPizzeria", boothAt.x - site.x, boothAt.y - site.z, yaw + 180f);
            if (stop == "Plaza")
            {
                Vector2 arrive = boothAt + front2 * 3.6f;
                IsleKit.TeleportArrival(b, "Plaza", arrive.x - site.x, arrive.y - site.z, yaw);      // facing the board they came from
            }
            Debug.Log("[BWM] Island Gear: teleporter to the pizzeria beside " + name + " at " + Sdk.F(boothAt.x) + ", " + Sdk.F(boothAt.y));
        }
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
        // A teleporter from the training hall to the Winter Isle's bridge, for convenience (the user, 2026-11-24): a pad
        // on the open floor of the game's spawn building, in the aisle between its teaching stations (floor at 32.25 m,
        // found from where players walk there and seen in the game), facing those who come from the spawn rooms; it sets
        // them down on the covered bridge's mainland end (its deck at 1.35 m), facing the island.
        IsleKit.TeleportBooth(b, "Training", "Bridge", "ToWinterIsle", -229.5f - site.x, -517.4f - site.z, 0f, false, 32.25f - site.y);
        IsleKit.TeleportArrival(b, "Bridge", -384.9f - site.x, -719.7f - site.z, -109f, false, 1.35f - site.y);
        // a firework post by each of the railway's stations out from the plaza (the Plaza Halt is near the plaza's own)
        if (WinterIsleRail.Load() && WinterIsleRail.Lines.Count > 0)
        {
            RailLine loop = WinterIsleRail.Lines[0];
            for (int s = 1; s < loop.Stops.Count; s++)
            {
                int i = loop.Stops[s];
                Vector3 here = loop.P[i], ahead = loop.P[loop.W(i + 2)];
                Vector2 station = new Vector2(here.x, here.z), along = new Vector2(ahead.x - here.x, ahead.z - here.z).normalized, across = new Vector2(along.y, -along.x);
                if (Vector2.Dot(across, WinterIslePuzzle.Plaza - station) < 0f) across = -across;
                string name = loop.Names[s] + " Fireworks";
                // on the station's level ground beyond an end of the shelter (it is 14 m long, the level ground 11 m
                // either side of the line), where it shows from the platform; on the plaza's side first
                Vector2 spot = Vector2.zero, facing = -across;
                bool found = false;
                foreach (float side in new[] { 1f, -1f })
                {
                    foreach (float far in new[] { 8.5f, 7.2f, 9.8f })
                        foreach (float along2 in new[] { 12f, -12f, 16f, -16f, 20f, -20f, 26f, -26f })
                        {
                            Vector2 candidate = station + along * along2 + across * (side * far);
                            if (found || !PostSpot(candidate)) continue;
                            spot = candidate; facing = -across * side; found = true;
                        }
                    if (found) break;
                }
                if (!found)
                {
                    if (!FindSpot(station + across * 13f, 30f, out spot)) { Debug.LogWarning("[BWM] Island Gear: no room for " + name + " near " + station); continue; }
                    facing = (station - spot).sqrMagnitude > 1f ? (station - spot).normalized : Vector2.up;
                    Debug.LogWarning("[BWM] Island Gear: " + name + " found no clear ground on its station's flat; it stands where the gear stations' search put it");
                }
                FireworkPost(name, spot, Mathf.Atan2(-facing.x, -facing.y) * Mathf.Rad2Deg);
                Debug.Log("[BWM] Island Gear: " + name + " at " + Sdk.F(spot.x) + ", " + Sdk.F(spot.y) + " (" + Sdk.F((spot - station).magnitude) + " m from its station's middle)");
            }
        }
        Debug.Log("[BWM] Island Gear: " + built + " gear stations, " + posts + " firework posts");
    }
}
