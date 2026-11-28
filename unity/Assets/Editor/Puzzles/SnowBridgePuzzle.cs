// Snow Bridge: build the bridge. Two high banks of packed snow face each other across two gullies, with an iced stone
// pier between them; the gourd waits in its vice on the far bank. Neither gully has a span yet. Beside each gap is a
// cradle: a marked trough inside a timber kerb, with a sign of three white squares behind it. When three snow blocks
// lie in a cradle its span appears, solid, and the lamp over its sign lights; take a block out and the span is gone.
// There are six blocks, four on the near bank and two on the pier, so the first span has to be built and crossed and
// a fourth block carried over it before the second can stand. Whoever sets foot on the far bank wins. No clock, no
// roles and nothing to get wrong: one player can finish it, a group makes a chain.
// (Before the user's rules of 2026-11-24 this was the Survey Party: a spotter reading Displays in the gully walls, a
// lookout at an invisible-ink telescope, a builder hanging blank tiles on two Peg Boards, and a load test of N-Hold
// buttons.)
//
// Only the spans lead to the pier and the far bank. Both, like the near bank, stand 3.6 m sheer above the pad on
// every side, and only the near bank has stairs: a jump lifts 1.03 m, and all six blocks stacked as steps add 1.5 m
// (as two towers of two and four, 2 m). Each gully is 5.4 m across: a sprint (3.5 m/s) and a jump carry 3.2 m, 3.6 m
// from the top of a block. The gullies' floors are the pad itself, open at both ends, so whoever falls in walks out
// and round to the near stairs.
//
// Every block is needed, so a block that gets where nobody can fetch it is laid back on the plank yard on the near
// bank (ItemReturn, in PuzzleScripts): from the far bank's top, from a span's deck (the game pins a prop that has
// stopped, so a block left on a span may hang in the air when the span goes), from the top of a sign, from further
// than about 25 m off or 4 m under the site, and from the pier's deck while the first span is down and nobody stands
// on the pier (a block thrown over there; the pier's own cradle and pallet keep theirs).
//
// The spans and their lamps follow the cradles' sensors and nothing else: no start or stop event touches them, and
// the far bank wins whether or not the Start Switch is on.
//
// Coordinates are local metres, x east and z north in the puzzle's own frame (+z from the near bank towards the far
// bank); heights are from the root's level (the pad's top, the gullies' floor); Up is the top of the banks and the
// pier. Parts face their local +z; yaw 180 faces -z.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class SnowBridgePuzzle : PuzzleDef
{
    public override string Name => "SnowBridge";
    public override Vector2 Footprint => new Vector2(30f, 38f);
    public override Vector2? FixedSite => new Vector2(-3003f, -2178f);     // layout-final: the crossing above the West Cliffs
    // Local +z points west (towards the cliff) and +x north: the gullies' open ends run north and south along the
    // shelf, not towards the cliff, and the near bank and its stairs face the West Cliffs station to the east.
    public override float Yaw => -90f;

    const float Up = 3.6f;                 // banks and pier top
    const float Lip = 7.2f;                // the banks' lips are at z = -Lip and z = +Lip
    const float PierHalf = 1.8f;           // the pier's faces are at z = -PierHalf and +PierHalf: each gully is 5.4 m across
    const float SpanHalf = 1.2f;           // a span's deck is 2.4 m wide
    const float BlockSize = 0.5f;
    const float PlankHigh = 0.08f;         // the plank yard and the pallet
    // The cradles: both east of the spans, the first at the near bank's lip, the second at the pier's far edge
    const float CradleX = 2.6f, Cradle1Z = -8.4f, Cradle2Z = 0.8f;
    const float CradleLong = 2.4f, CradleDeep = 1.0f;      // the trough inside the kerb
    const float SignBehind = 0.8f;         // a cradle's sign stands this far beyond its middle, towards the gap
    const float YardX = -2.5f, YardZ = -12.3f;             // where a strayed block is laid down again
    const float DepthLight6 = 0.142f + 0.01f;              // a Light 6's pivot in front of its back (part-metrics.tsv), plus 1 cm

    static readonly Color SnowBlock = new Color(0.92f, 0.94f, 0.98f);
    static readonly Color IcedStone = new Color(0.62f, 0.70f, 0.78f);
    static readonly Color SpanDeck = new Color(0.88f, 0.92f, 0.98f);
    static readonly Color Warm = new Color(1f, 0.92f, 0.75f);      // near white: dull when off, a bright disc when lit
    // BWM/Snow is the ground's shader and reads a vertex colour as ground, not as paint: red 1 is fresh snow, green
    // bare earth, blue sand, and alpha how hollow the place is (0.5 level). Steep faces it draws as rock, except in a
    // hollow (alpha under 0.47), where snow collects: so a block's sides are snow as well as its top.
    static readonly Color LooseSnow = new Color(1f, 0f, 0f, 0.25f);

    PuzzleBuilder b;

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        b.ClearGround(30f, 38f);
        b.DebugTeleport(10f, -15.7f);      // on the ground at the foot of the near stairs
        // Winter Walk's jetpacks do not fire over the whole crossing, well past the banks, stairs and pier
        IsleKit.CalmAir(b, "Snow Bridge", 1.5f, -3f, new Vector3(46f, 120f, 50f), 0f, -10f, false);

        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        var works = new MeshBuilder();      // snow lies on its tops
        var paint = new MeshBuilder();      // planks, the cradles' floors, the signs' squares: never under snow
        BuildNearBank(works, paint);
        BuildPier(works, paint);
        BuildFarBank(works);
        Cradle(works, paint, Cradle1Z);
        Cradle(works, paint, Cradle2Z);
        b.MeshPiece("Snow Bridge Works", works.ToMesh(), snowy);
        b.MeshPiece("Snow Bridge Paint", paint.ToMesh(), bare);

        // ---- the spans: switched off in the built scene, and only their cradles switch them ----
        Piece span1 = Span("First Span", -Lip, -PierHalf, snowy);
        Piece span2 = Span("Second Span", PierHalf, Lip, snowy);
        span1.Go.SetActive(false);
        span2.Go.SetActive(false);

        for (int i = 0; i < 4; i++)      // soft drifts in the gullies, 0.5 m high and 2.7 m from either wall
        {
            float x = i % 2 == 0 ? -3.2f : 3.2f, z = (i < 2 ? -1f : 1f) * (PierHalf + Lip) / 2f;
            b.MeshPiece("Soft Snow " + (i + 1), Kit.Mound(1.8f, 0.5f, 2201 + i, 8, 20), b.ShaderMaterial("BWM/Snowy", "Drift"), true)
                .Transform.position = b.WorldPoint(x, z, 0f, false);
        }

        Object[] blocks = BuildBlocks();
        Piece cradle1 = CradleParts(1, Cradle1Z, blocks, span1);
        CradleParts(2, Cradle2Z, blocks, span2);

        // ---- the Start Switch and its Live Light at the head of the near stairs ----
        P("Switch Start", 3.0f, -16.8f + 0.173f, Up + 1.25f, 0f, "Switch Start");
        P("Live Light", 3.0f, -16.8f + 0.1f, Up + 1.42f, 0f, "Live Light");

        // ---- the gourd on the far bank, and the win: anyone standing anywhere on the far bank's top ----
        P("Gourd Vice", 0f, 11.0f, Up + 0.45f, 180f, "Gourd Vice");
        P("Pillow", -1.6f, 11.0f, Up + 0.13f, 0f, "Pillow");
        P("Confetti", 1.6f, 11.0f, Up + 0.2f, 0f, "Confetti");
        IsleKit.Sensor(b, "Far Bank Arrival", 0f, (Lip + 13.0f) / 2f, new Vector3(9.6f, 2.6f, 13.0f - Lip - 0.4f), Up, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1)
            .On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");

        BuildReturns(cradle1);
        BuildLights();
    }

    // ================================================================ geometry helpers

    // An axis-aligned block from its low and high corners (heights from the root's level)
    static void Block(MeshBuilder mb, float x0, float x1, float h0, float h1, float z0, float z1, Color c)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, h0, (z0 + z1) / 2f), new Vector3(x1 - x0, h1 - h0, z1 - z0), c);

    // A low deck of bare planks on a bank's or the pier's top, 'count' of them side by side
    static void Planks(MeshBuilder mb, float x0, float x1, float z0, float z1, int count)
    {
        float wide = (x1 - x0) / count;
        for (int i = 0; i < count; i++)
            Block(mb, x0 + i * wide, x0 + (i + 1) * wide, Up, Up + PlankHigh, z0, z1, i % 2 == 0 ? IsleKit.Wood : IsleKit.DarkWood);
    }

    // ================================================================ structures

    void BuildNearBank(MeshBuilder mb, MeshBuilder paint)
    {
        // the pad: one level slab for the whole site, sunk 1.5 m; its top is the gullies' floor
        Block(mb, -15f, 15f, -1.5f, 0f, -19f, 19f, SnowBlock);
        // the near bank, sheer on every side
        Block(mb, -5f, 5f, 0f, Up, -17.5f, -Lip, SnowBlock);
        // the only stairs of the crossing: on +x, from the ground up to the near bank's top
        StairsUp(mb, 5f, 8.6f, -16.5f, -14.9f, 12);
        Block(mb, 2.87f, 3.13f, Up, Up + 1.5f, -16.9f, -16.7f, IsleKit.Wood);        // Start Post
        // the plank yard: blocks 1 to 4 lie in a row along its far edge, and strayed blocks come back to its middle
        Planks(paint, -4.0f, -1.0f, -13.6f, -10.1f, 6);
    }

    void BuildPier(MeshBuilder mb, MeshBuilder paint)
    {
        Block(mb, -4f, 4f, 0f, Up, -PierHalf, PierHalf, IcedStone);
        Planks(paint, -3.3f, -1.7f, 0.3f, 1.3f, 4);      // the pallet under blocks 5 and 6, west of the second span's end
    }

    void BuildFarBank(MeshBuilder mb)
    {
        Block(mb, -5f, 5f, 0f, Up, Lip, 13.0f, SnowBlock);                             // no stairs: the second span is the only way up
        Block(mb, -0.55f, 0.55f, Up, Up + 0.45f, 10.45f, 11.55f, IsleKit.Stone);      // Gourd Stone
    }

    // Stone steps from the bank's side at x0 (highest) out to x1 (lowest), 'count' of them, between z0 and z1, each
    // solid to the ground. Rises are Up / (count + 1): 0.28 m for twelve, the last step 0.28 below the bank top.
    static void StairsUp(MeshBuilder mb, float x0, float x1, float z0, float z1, int count)
    {
        float tread = (x1 - x0) / count;
        for (int s = 0; s < count; s++)
            Block(mb, x0 + s * tread, x0 + (s + 1) * tread, 0f, Up * (count - s) / (count + 1f), z0, z1, IsleKit.Stone);
    }

    // A cradle on a top, its middle at (CradleX, cz): a trough 2.4 x 1.0 m with a pale floor inside a low timber kerb,
    // and beyond it, at the edge of the gap, its sign on two posts, facing back over the trough (-z): a dark board
    // with three white squares (three blocks are wanted), the plate its lamp hangs on, and a beam across the posts.
    // The board's top is 2.2 m up and the beam's 2.9 m: out of reach even from two stacked blocks.
    static void Cradle(MeshBuilder works, MeshBuilder paint, float cz)
    {
        float x0 = CradleX - CradleLong / 2f, x1 = CradleX + CradleLong / 2f, z0 = cz - CradleDeep / 2f, z1 = cz + CradleDeep / 2f;
        const float kerb = 0.1f, kerbHigh = 0.14f;
        Block(works, x0 - kerb, x1 + kerb, Up, Up + kerbHigh, z0 - kerb, z0, IsleKit.DarkWood);
        Block(works, x0 - kerb, x1 + kerb, Up, Up + kerbHigh, z1, z1 + kerb, IsleKit.DarkWood);
        Block(works, x0 - kerb, x0, Up, Up + kerbHigh, z0, z1, IsleKit.DarkWood);
        Block(works, x1, x1 + kerb, Up, Up + kerbHigh, z0, z1, IsleKit.DarkWood);
        Block(paint, x0, x1, Up, Up + 0.02f, z0, z1, IsleKit.Ice);

        float sign = cz + SignBehind;
        foreach (float post in new[] { x0 - 0.04f, x1 + 0.04f })
            Block(works, post - 0.06f, post + 0.06f, Up, Up + 2.8f, sign - 0.06f, sign + 0.06f, IsleKit.Wood);
        Block(works, x0 + 0.02f, x1 - 0.02f, Up + 1.5f, Up + 2.2f, sign - 0.04f, sign + 0.04f, IsleKit.Board);
        Block(works, CradleX - 0.3f, CradleX + 0.3f, Up + 2.2f, Up + 2.8f, sign - 0.04f, sign + 0.04f, IsleKit.Board);      // the lamp's plate
        Block(works, x0 - 0.16f, x1 + 0.16f, Up + 2.8f, Up + 2.9f, sign - 0.1f, sign + 0.1f, IsleKit.DarkWood);
        for (int i = -1; i <= 1; i++)
            Block(paint, CradleX + i * 0.7f - 0.22f, CradleX + i * 0.7f + 0.22f, Up + 1.63f, Up + 2.07f, sign - 0.052f, sign - 0.04f, SnowBlock);
    }

    // A span: a solid snow deck 2.4 m wide from one lip to the other, its top level with both, with low cream kerbs
    Piece Span(string name, float z0, float z1, Material snowy)
    {
        var mb = new MeshBuilder();
        Block(mb, -SpanHalf, SpanHalf, Up - 0.35f, Up, z0, z1, SpanDeck);
        Block(mb, -SpanHalf, -SpanHalf + 0.12f, Up, Up + 0.3f, z0, z1, IsleKit.Cream);
        Block(mb, SpanHalf - 0.12f, SpanHalf, Up, Up + 0.3f, z0, z1, IsleKit.Cream);
        return b.MeshPiece(name, mb.ToMesh(), snowy);
    }

    // ================================================================ parts and wiring

    Piece P(string prefab, float x, float z, float h, float yaw, string name) => b.Part(prefab, x, z, h, yaw, false).Named(name);

    // The six snow blocks: half-metre cubes for the hands only (b.Item keeps them off belts and out of backpacks), back
    // at their places on every reload. Blocks 1 to 4 in a row on the near bank's yard, 5 and 6 on the pier's pallet;
    // none starts inside a cradle (a sensor only counts what comes into it).
    Object[] BuildBlocks()
    {
        Material snow = b.ShaderMaterial("BWM/Snow", "Snow");
        var blocks = new List<Object>();
        for (int i = 0; i < 6; i++)
        {
            float x = i < 4 ? -3.55f + i * 0.7f : -2.85f + (i - 4) * 0.7f, z = i < 4 ? -10.55f : 0.8f;
            var cube = new MeshBuilder();
            Kit.AddBox(cube, new Vector3(0f, -BlockSize / 2f, 0f), Vector3.one * BlockSize, LooseSnow);
            Piece block = b.Item("Snow Block " + (i + 1), cube.ToMesh(), snow, x, z, Up + PlankHigh + BlockSize / 2f + 0.02f, 2f, false);
            Component item = block.Component("PuzzleItem");
            Sdk.Set(item, "savesOnReload", false);
            blocks.Add(item);
        }
        return blocks.ToArray();
    }

    // A cradle's sensor and lamp. The sensor counts the blocks in the trough: with three its span is switched on and
    // its lamp lit, with fewer both go again. These events reach every player's PC. Its box is the trough to 0.6 m up:
    // a block lying there counts, and one lying on another, but not one in a hand over it (a hand holds an item
    // 1.24 m up, so a held block's underside is at 0.99 m). Otherwise someone standing by the trough with the third
    // block would raise the span, walk on with the block and fall.
    Piece CradleParts(int number, float cz, Object[] blocks, Piece span)
    {
        Piece sensor = IsleKit.Sensor(b, "Cradle " + number, CradleX, cz, new Vector3(CradleLong, 0.6f, CradleDeep), Up, false)
            .Set("PuzzlePresenceSensor", "type", 2)                 // Specific Item
            .Set("PuzzlePresenceSensor", "requiredPlayers", 3)      // (for the item types: items needed)
            .Add("PuzzlePresenceSensor", "acceptedCustom", blocks);
        Component lamp = P("Light 6", CradleX, cz + SignBehind - 0.04f - DepthLight6, Up + 2.5f + 0.017f, 180f, "Cradle Lamp " + number)
            .Set("PuzzleLight", "color", Warm).Set("PuzzleLight", "range", 4f).Component("PuzzleLight");
        sensor.On("PuzzlePresenceSensor", "onPressed", span.Go, "SetActive", true)
              .On("PuzzlePresenceSensor", "onReleased", span.Go, "SetActive", false)
              .On("PuzzlePresenceSensor", "onPressed", lamp, "TurnOn")
              .On("PuzzlePresenceSensor", "onReleased", lamp, "TurnOff");
        return sensor;
    }

    // ================================================================ strayed blocks come back

    // An empty that ItemReturn reads as a catching box. The script's box is one unit in the empty's own frame, from
    // (-0.5, 0, -0.5) to (0.5, 1, 0.5), so the empty stands at the middle of the box's floor and its scale is the
    // box's size. A block whose middle stays in a box for the script's delay, in nobody's hands, is sent home.
    void Catch(string name, float x0, float x1, float h0, float h1, float z0, float z1)
        => b.Empty(name, (x0 + x1) / 2f, (z0 + z1) / 2f, h0, false).Transform.localScale = new Vector3(x1 - x0, h1 - h0, z1 - z0);

    void Return(Piece holder, string boxPrefix, Piece home, float delay)
    {
        Component back = b.AddCustomScript(holder, "ItemReturn");
        Sdk.Set(back, "itemPrefix", "Snow Block ");
        Sdk.Set(back, "boxPrefix", boxPrefix);
        Sdk.Set(back, "boxMin", new Vector3(-0.5f, 0f, -0.5f));
        Sdk.Set(back, "boxMax", new Vector3(0.5f, 1f, 0.5f));
        Sdk.Set(back, "home", home.Transform);
        Sdk.Set(back, "spread", 0.8f);
        Sdk.Set(back, "delay", delay);
    }

    void BuildReturns(Piece cradle1)
    {
        // home: over the open middle of the yard, clear of the row of four
        Piece home = b.Empty("Block Home", YardX, YardZ, Up + PlankHigh + 0.5f, false);

        // ---- always: the far bank's top, both spans' decks, anything higher than a stack of four on any top (a
        // sign's board or beam, a lamp), far off the site, far under it ----
        Return(b.Empty("Block Return", YardX, YardZ, Up + 2f, false), "Block Catch ", home, 5f);
        Catch("Block Catch Far Bank", -5.1f, 5.1f, Up, Up + 3f, Lip - 0.05f, 13.1f);
        Catch("Block Catch Span 1", -SpanHalf - 0.1f, SpanHalf + 0.1f, Up - 0.1f, Up + 3f, -Lip + 0.05f, -PierHalf - 0.05f);
        Catch("Block Catch Span 2", -SpanHalf - 0.1f, SpanHalf + 0.1f, Up - 0.1f, Up + 3f, PierHalf + 0.05f, Lip - 0.05f);
        Catch("Block Catch High", -5.1f, 5.1f, Up + 2.3f, Up + 12f, -17.6f, 13.1f);
        const float far = 600f, low = -400f, high = 800f;
        Catch("Block Catch Off Site 1", -far, -24f, low, high, -far, far);
        Catch("Block Catch Off Site 2", 24f, far, low, high, -far, far);
        Catch("Block Catch Off Site 3", -24f, 24f, low, high, -far, -28f);
        Catch("Block Catch Off Site 4", -24f, 24f, low, high, 24f, far);
        Catch("Block Catch Below", -24f, 24f, low, -4f, -28f, 24f);

        // ---- the pier's deck, but only while nobody could have carried a block there: the first span is down and
        // nobody stands on the pier. The script runs while its holder and the holder's parent are both switched on:
        // the first cradle switches the holder, a sensor over the pier's top the parent. The pier's cradle and its
        // pallet (z over 0.1, x -3.5 to -1.5 and x over 1.2) are in no box, so what lies there stays. ----
        Piece pierWatch = b.Empty("Pier Watch", 0f, 0f, Up + 2f, false);
        Piece pierReturn = b.Empty("Pier Return", 0f, 0f, Up + 2.2f, false).Under(pierWatch);
        Return(pierReturn, "Pier Catch ", home, 4f);
        Catch("Pier Catch South", -4.1f, 4.1f, Up, Up + 3f, -PierHalf - 0.05f, 0.1f);
        Catch("Pier Catch West", -4.1f, -3.5f, Up, Up + 3f, 0.1f, PierHalf + 0.05f);
        Catch("Pier Catch Middle", -1.5f, 1.2f, Up, Up + 3f, 0.1f, PierHalf + 0.05f);
        cradle1.On("PuzzlePresenceSensor", "onPressed", pierReturn.Go, "SetActive", false)
               .On("PuzzlePresenceSensor", "onReleased", pierReturn.Go, "SetActive", true);
        IsleKit.Sensor(b, "Pier Deck", 0f, 0f, new Vector3(8f, 2.6f, 2f * PierHalf), Up, false)
            .Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1)
            .On("PuzzlePresenceSensor", "onPressed", pierWatch.Go, "SetActive", false)
            .On("PuzzlePresenceSensor", "onReleased", pierWatch.Go, "SetActive", true);
    }

    // ================================================================ lights

    void BuildLights()
    {
        float h = Up + 1.0f, lip = Lip + 0.25f, edge = PierHalf - 0.15f;
        Kit.LightString(b, "Near Lights", new List<Vector3> { new Vector3(-4.8f, h, -17.2f), new Vector3(-4.8f, h, -13.95f), new Vector3(-4.8f, h, -10.7f),
            new Vector3(-4.8f, h, -lip), new Vector3(-3.05f, h, -lip), new Vector3(-1.3f, h, -lip) }, 0.4f, 0.5f, false, true, 31);
        Kit.LightString(b, "Near East Lights", new List<Vector3> { new Vector3(4.8f, h, -14.2f), new Vector3(4.8f, h, -10.9f), new Vector3(4.8f, h, -7.6f) }, 0.4f, 0.5f, false, true, 32);
        Kit.LightString(b, "Pier West Lights", new List<Vector3> { new Vector3(-1.3f, h, -edge), new Vector3(-3.85f, h, -edge), new Vector3(-3.85f, h, edge), new Vector3(-1.3f, h, edge) }, 0.4f, 0.5f, false, true, 33);
        Kit.LightString(b, "Pier East Lights", new List<Vector3> { new Vector3(1.3f, h, -edge), new Vector3(3.85f, h, -edge), new Vector3(3.85f, h, 0f) }, 0.4f, 0.5f, false, true, 34);
        Kit.LightString(b, "Far Lip West Lights", new List<Vector3> { new Vector3(-4.8f, h, lip), new Vector3(-3.05f, h, lip), new Vector3(-1.3f, h, lip) }, 0.4f, 0.5f, false, true, 36);
        Kit.LightString(b, "Far Lip East Lights", new List<Vector3> { new Vector3(1.3f, h, lip), new Vector3(3.05f, h, lip), new Vector3(4.8f, h, lip) }, 0.4f, 0.5f, false, true, 37);
        Kit.LightRing(b, "Site Lights", 0f, -2f, 28f, 34f, 2.6f, false, 21);
    }
}
