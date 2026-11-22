// Blind Sculptor (puzzle 15, blind mask, invisible ink): the Snow Sculptor's Court. A walled court holds a snow
// plinth with a five-slot peg board (the "sculpture") and twelve carving tiles lying in four sloped racks. The tiles
// are painted in invisible ink: blank to everyone except the player looking through the ink telescope on the
// watchtower outside the court's glass east wall. The commission (five pictures) is dealt at each start onto a
// split-flap Display inside a kiosk whose closed back faces the court and the tower.
//   - The sculptor wears a blindfold and goes in through the blind turnstile.
//   - The patron reads the commission in the kiosk and calls it out to the watchtower.
//   - The spotter, at the ink telescope, finds those pictures among the twelve blank-looking tiles and talks the
//     blind sculptor to each tile and onto the right slot. The sculptor presses the check button on the plinth.
// Eight minutes on the tower's clock; two wrong checks (or the clock) stop the puzzle and the next start re-deals.
// In 4-player lobbies the commission is split over two kiosks (three flaps and two). The court is blind-only:
// BlindWatch (a host script) puts anyone inside it who is not wearing a blindfold straight back outside.
// The picture set is the Peg set (index 1, the game's set B), reserved for this puzzle island-wide: the board
// matches tiles by picture only, so no other island puzzle may use Peg tiles.
//
// Spec: Z:\Temp\claude\BigWalkMods\puzzle-specs\BlindSculptor.md (revision 2 and its review notes).
// Local coordinates: x right, z forward (+z north at yaw 0). Court parts stand on the level pad (onGround: false,
// measured from the root's level, pad top at Floor); the tower and the kiosks stand on the ground under their centres.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BlindSculptorPuzzle : PuzzleDef
{
    public override string Name => "BlindSculptor";
    public override Vector2 Footprint => new Vector2(38f, 32f);
    public override Vector2? FixedSite => new Vector2(-1674f, -798f);      // layout-final: north shore, a sheltered court on its own flat
    public override float Yaw => 0f;      // glass side (+x) to the open ground east; kiosk openings face south, away from the tower

    const float Floor = 0.25f;            // the pad's top, above the root's level
    const float WallTop = 3.0f;           // court walls, above the pad
    const float Deck = 4.2f;              // the watchtower's platform, above the ground under it
    static readonly Color SnowWhite = new Color(0.93f, 0.95f, 0.98f);

    // Racks: centre (x, z) and the Peg image in each bay (z - 0.65, z, z + 0.65)
    static readonly Vector2[] RackAt = { new Vector2(-4.6f, 6.4f), new Vector2(1.2f, 6.5f), new Vector2(-4.6f, -1.9f), new Vector2(3.8f, 3.0f) };
    static readonly string[] RackName = { "North-West", "North", "South-West", "East" };
    static readonly int[][] RackTiles = { new[] { 1, 7, 4 }, new[] { 10, 2, 8 }, new[] { 5, 11, 3 }, new[] { 12, 6, 9 } };

    // Rack profile (x relative to the rack's centre; heights above the pad): the face slopes 55 degrees, rising west
    const float RackHalfX = 0.35f, RackHalfZ = 1.0f;
    const float SlopeLowX = 0.30f, SlopeLowH = 0.55f, SlopeHighH = 1.10f;      // the lip takes x 0.30..0.35
    static readonly float SlopeHighX = SlopeLowX - (SlopeHighH - SlopeLowH) / Mathf.Tan(55f * Mathf.Deg2Rad);      // about -0.085
    const float LipHeight = 0.12f;

    // Letterbox through the south wall (centre x, width, slot height, outer and inner mouth bottoms above the pad)
    const float SlotX = -1.0f, SlotW = 0.6f, SlotH = 0.32f, SlotOuter = 1.2f, SlotInner = 0.95f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(-1.5f, -11f);
        b.ClearGround(38f, 30f, 5f, -2f);
        Material snowy = IsleKit.Snowy(b);
        Material bare = IsleKit.Bare(b);
        float rootY = b.Root.transform.position.y;
        float tower = b.WorldPoint(13.4f, 2.0f).y - rootY;      // ground under the watchtower, above the root's level
        float kioskA = b.WorldPoint(16f, -8f).y - rootY;
        float kioskB = b.WorldPoint(4f, -13.5f).y - rootY;

        // ---- the court: pad, stone walls (3 m, sheer), the glass east wall, the plinth and the four racks ----
        var court = new MeshBuilder();
        Kit.AddBox(court, new Vector3(0f, Floor - 1.75f, 2f), new Vector3(13.5f, 1.75f, 13.5f), IsleKit.Stone);      // the level pad, sunk 1.5 m
        Block(court, -6.3f, -6.0f, -4.3f, 8.3f, Floor, Floor + WallTop, IsleKit.Stone);      // west
        Block(court, -6.3f, 5.85f, 8.0f, 8.3f, Floor, Floor + WallTop, IsleKit.Stone);       // north
        // south wall: two turnstile openings (x -4.1..-1.9 and 1.9..4.1) with lintels over them, and the letterbox
        Block(court, -6.3f, -4.1f, -4.3f, -4.0f, Floor, Floor + WallTop, IsleKit.Stone);
        Block(court, -4.1f, -1.9f, -4.3f, -4.0f, Floor + 2.35f, Floor + WallTop, IsleKit.Stone);      // lintel over the blind entry
        Block(court, -1.9f, SlotX - SlotW / 2f, -4.3f, -4.0f, Floor, Floor + WallTop, IsleKit.Stone);
        Block(court, SlotX + SlotW / 2f, 1.9f, -4.3f, -4.0f, Floor, Floor + WallTop, IsleKit.Stone);
        Block(court, 1.9f, 4.1f, -4.3f, -4.0f, Floor + 2.35f, Floor + WallTop, IsleKit.Stone);        // lintel over the way out
        Block(court, 4.1f, 5.85f, -4.3f, -4.0f, Floor, Floor + WallTop, IsleKit.Stone);
        // the letterbox's column: wall under and over a slot that slopes down into the court
        float sx0 = SlotX - SlotW / 2f, sx1 = SlotX + SlotW / 2f;
        Hex(court, sx0, sx1, -4.3f, -4.0f,
            Floor, Floor, Floor, Floor,
            Floor + SlotOuter, Floor + SlotOuter, Floor + SlotInner, Floor + SlotInner, IsleKit.Stone);
        Hex(court, sx0, sx1, -4.3f, -4.0f,
            Floor + SlotOuter + SlotH, Floor + SlotOuter + SlotH, Floor + SlotInner + SlotH, Floor + SlotInner + SlotH,
            Floor + WallTop, Floor + WallTop, Floor + WallTop, Floor + WallTop, IsleKit.Stone);
        // its lined bottom plate, running on past the inner face so a posted tile slides off into the court
        float drop = (SlotOuter - SlotInner) / 0.3f;      // metres down per metre in
        Hex(court, sx0 + 0.03f, sx1 - 0.03f, -4.32f, -3.72f,
            Floor + SlotOuter + 0.02f * drop - 0.03f, Floor + SlotOuter + 0.02f * drop - 0.03f, Floor + SlotInner - 0.28f * drop - 0.03f, Floor + SlotInner - 0.28f * drop - 0.03f,
            Floor + SlotOuter + 0.02f * drop + 0.005f, Floor + SlotOuter + 0.02f * drop + 0.005f, Floor + SlotInner - 0.28f * drop + 0.005f, Floor + SlotInner - 0.28f * drop + 0.005f, IsleKit.DarkWood);
        // the plaque on the south wall's inner face for the inside stop switch and its live lamp
        Block(court, -0.3f, 0.6f, -4.0f, -3.83f, Floor + 0.95f, Floor + 1.65f, IsleKit.Board);
        // east wall: a stone sill to 0.9 m, six panes of clear glass between stone posts, a cap from 2.9 to 3.1 m
        Block(court, 5.85f, 6.15f, -4.3f, 8.3f, Floor, Floor + 0.9f, IsleKit.Stone);
        Block(court, 5.85f, 6.15f, -4.3f, 8.3f, Floor + 2.9f, Floor + 3.1f, IsleKit.Stone);
        Block(court, 5.85f, 6.15f, -4.3f, -3.85f, Floor + 0.9f, Floor + 2.9f, IsleKit.Stone);      // end posts
        Block(court, 5.85f, 6.15f, 7.85f, 8.3f, Floor + 0.9f, Floor + 2.9f, IsleKit.Stone);
        for (int k = 0; k < 5; k++)
        {
            float z = -2.0f + 2f * k;      // posts between the panes (pane centres -3, -1, 1, 3, 5, 7)
            Block(court, 5.85f, 6.15f, z - 0.15f, z + 0.15f, Floor + 0.9f, Floor + 2.9f, IsleKit.Stone);
        }
        // the sculpture plinth: 0.6 x 4.2 x 1.6 m of packed snow, the board, check screen and button on its east face
        Block(court, -1.5f, -0.9f, 0.1f, 4.3f, Floor, Floor + 1.6f, SnowWhite);
        // the racks: a sloped face rising west (so it looks east at the tower), a lip on its low edge, three bays
        for (int r = 0; r < RackAt.Length; r++) Rack(court, RackAt[r]);
        Piece courtMesh = b.MeshPiece("Court", court.ToMesh(), snowy);
        courtMesh.Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // a painted tile outline over the letterbox's outer mouth (never takes snow)
        var outline = new MeshBuilder();
        float oy0 = Floor + SlotOuter - 0.06f, oy1 = Floor + SlotOuter + SlotH + 0.06f;
        Block(outline, sx0 - 0.08f, sx1 + 0.08f, -4.33f, -4.3f, oy0 - 0.05f, oy0, IsleKit.Gold);
        Block(outline, sx0 - 0.08f, sx1 + 0.08f, -4.33f, -4.3f, oy1, oy1 + 0.05f, IsleKit.Gold);
        Block(outline, sx0 - 0.08f, sx0 - 0.03f, -4.33f, -4.3f, oy0, oy1, IsleKit.Gold);
        Block(outline, sx1 + 0.03f, sx1 + 0.08f, -4.33f, -4.3f, oy0, oy1, IsleKit.Gold);
        Block(outline, SlotX - 0.11f, SlotX + 0.11f, -4.33f, -4.3f, oy1 + 0.1f, oy1 + 0.32f, IsleKit.Cream);      // a little tile above it
        b.MeshPiece("Letterbox Mark", outline.ToMesh(), bare, false).Transform.position = b.WorldPoint(0f, 0f, 0f, false);

        // Glass panes: clear, muffling (the spotter must still be heard), 1.7 x 2.0 m, centred 1.9 m up
        for (int k = 0; k < 6; k++)
            b.Part("Glass", 6.0f, -3f + 2f * k, Floor + 1.9f, -90f, false).Named("Court Glass " + (k + 1))
                .Pane(1.7f, 2.0f).Set("PuzzleGlass", "style", 2).Set("PuzzleGlass", "sound", 0);

        // Christmas lights along the wall tops (no posts: the wire lies just over the coping)
        Kit.LightString(b, "Court Lights", new List<Vector3>
        {
            new Vector3(-6.15f, Floor + WallTop + 0.15f, -4.15f), new Vector3(-6.15f, Floor + WallTop + 0.15f, 8.15f),
            new Vector3(6.0f, Floor + 3.1f + 0.15f, 8.15f), new Vector3(6.0f, Floor + 3.1f + 0.15f, -4.15f),
            new Vector3(-6.15f, Floor + WallTop + 0.15f, -4.15f),
        }, 0f, 0.5f, false, false, 157);

        // ---- gates: blind entry (locked while the puzzle is off), plain one-way exit (never locked) ----
        Piece blindEntry = IsleKit.BlindGate(b, -3.0f, -4.0f, 0f, Floor, false).Named("Blind Entry")
            .Set("PuzzleNoItemsGate", "defaultLocked", true);
        Piece wayOut = b.Part("Gate No items", 3.0f, -4.0f, Floor, 180f, false).Named("Way Out")      // front (-z turned by 180) is the court side
            .Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "defaultLocked", false);
        // inside stop switch and its live lamp, on the plaque by the exit: quitting from inside
        b.Part("Switch Start", 0.0f, -3.775f, Floor + 1.25f, 0f, false).Named("Inside Stop Switch");
        b.Part("Live Light", 0.35f, -3.82f, Floor + 1.42f, 0f, false).Named("Inside Live Light");

        // ---- the twelve inked tiles, lying face up the slope in their bays (face east and 35 degrees up) ----
        Vector2 n = new Vector2(Mathf.Cos(35f * Mathf.Deg2Rad), Mathf.Sin(35f * Mathf.Deg2Rad));      // face normal: (east, up)
        float midX = (SlopeLowX + SlopeHighX) / 2f, midH = (SlopeLowH + SlopeHighH) / 2f;
        for (int r = 0; r < RackAt.Length; r++)
            for (int bay = 0; bay < 3; bay++)
            {
                int image = RackTiles[r][bay];
                float x = RackAt[r].x + midX + n.x * 0.25f, z = RackAt[r].y + (bay - 1) * 0.65f, h = midH + n.y * 0.25f;      // its back (0.24 m deep) clear of the slope
                Piece tile = b.Part("Peg Tile", x, z, Floor + h, 90f, false).Named("Tile " + image + " (" + RackName[r] + ")");
                tile.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(-35f, 90f, 0f);
                tile.Set("PuzzlePegTile", "imageSet", 1)      // Peg (the game's set B): reserved for this puzzle on the island
                    .Set("PuzzlePegTile", "image", image)
                    .Set("PuzzlePegTile", "invisibleInkView", true);
            }

        // ---- the watchtower: deck 4.2 m, west edge 6 m from the glass, the stair climbing from the east ----
        var tw = new MeshBuilder();
        float t0 = tower;
        foreach (float lx in new[] { 12.15f, 14.65f })
            foreach (float lz in new[] { 0.75f, 3.25f })
                Block(tw, lx - 0.13f, lx + 0.13f, lz - 0.13f, lz + 0.13f, t0 - 0.4f, t0 + Deck - 0.2f, IsleKit.DarkWood);      // legs
        Block(tw, 12.0f, 14.8f, 0.6f, 3.4f, t0 + Deck - 0.2f, t0 + Deck, IsleKit.Wood);      // the deck
        Block(tw, 12.0f, 12.15f, 0.6f, 3.4f, t0 + Deck, t0 + Deck + 0.3f, IsleKit.DarkWood);      // a 0.3 m kerb on the west edge only
        foreach (float rz in new[] { 0.63f, 3.37f })      // rails 1.1 m on the south and north edges
        {
            foreach (float px in new[] { 12.05f, 13.4f, 14.75f })
                Block(tw, px - 0.05f, px + 0.05f, rz - 0.05f, rz + 0.05f, t0 + Deck, t0 + Deck + 1.1f, IsleKit.DarkWood);
            Block(tw, 12.0f, 14.8f, rz - 0.03f, rz + 0.03f, t0 + Deck + 1.04f, t0 + Deck + 1.1f, IsleKit.Wood);
            Block(tw, 12.0f, 14.8f, rz - 0.03f, rz + 0.03f, t0 + Deck + 0.5f, t0 + Deck + 0.56f, IsleKit.Wood);
        }
        // short top rails either side of the stair's mouth (between the corner posts and the stair's own posts)
        Block(tw, 14.72f, 14.78f, 0.63f, 1.35f, t0 + Deck + 1.04f, t0 + Deck + 1.1f, IsleKit.Wood);
        Block(tw, 14.72f, 14.78f, 2.65f, 3.37f, t0 + Deck + 1.04f, t0 + Deck + 1.1f, IsleKit.Wood);
        // the stair: 12 steps of 0.35 m, x 19.0 (bottom) to 14.8 (the deck), z 1.4..2.6, rails both sides
        for (int s = 1; s <= 12; s++)
            Block(tw, 19.0f - s * 0.35f, 19.0f - (s - 1) * 0.35f, 1.4f, 2.6f, t0 - 0.4f, t0 + s * 0.35f, IsleKit.Wood);
        foreach (float rz in new[] { 1.35f, 2.65f })
        {
            Block(tw, 18.9f, 19.0f, rz - 0.05f, rz + 0.05f, t0, t0 + 0.35f + 1.0f, IsleKit.DarkWood);
            Block(tw, 14.8f, 14.9f, rz - 0.05f, rz + 0.05f, t0, t0 + Deck + 1.0f, IsleKit.DarkWood);
            Hex(tw, 14.8f, 19.0f, rz - 0.03f, rz + 0.03f,
                t0 + Deck + 0.94f, t0 + 0.35f + 0.94f, t0 + 0.35f + 0.94f, t0 + Deck + 0.94f,
                t0 + Deck + 1.0f, t0 + 0.35f + 1.0f, t0 + 0.35f + 1.0f, t0 + Deck + 1.0f, IsleKit.Wood);
        }
        // the tally board on the south rail, facing north: the clock above, the strike bar below
        Block(tw, 12.2f, 14.6f, 0.6f, 0.66f, t0 + Deck + 0.7f, t0 + Deck + 2.1f, IsleKit.Board);
        Block(tw, 12.1f, 12.2f, 0.58f, 0.68f, t0 + Deck, t0 + Deck + 2.25f, IsleKit.DarkWood);
        Block(tw, 14.6f, 14.7f, 0.58f, 0.68f, t0 + Deck, t0 + Deck + 2.25f, IsleKit.DarkWood);
        Block(tw, 12.0f, 14.8f, 0.45f, 0.95f, t0 + Deck + 2.25f, t0 + Deck + 2.35f, IsleKit.Red);      // a little roof over it
        b.MeshPiece("Watchtower", tw.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        Kit.LightString(b, "Tower Lights", new List<Vector3>
        {
            new Vector3(12.05f, t0 + Deck + 1.15f, 3.37f), new Vector3(14.75f, t0 + Deck + 1.15f, 3.37f),
            new Vector3(18.95f, t0 + 1.4f, 2.65f),
        }, 0.15f, 0.45f, false, false, 158);

        // the ink telescope stands on the deck (its pillar's foot is 1.1 m under its pivot), looking west into the court
        b.Part("Telescope", 12.5f, 2.0f, t0 + Deck + 1.1f, -90f, false).Named("Ink Telescope")
            .Set("PuzzleTelescope", "invisibleInkView", true);
        Piece clock = b.Part("Timer", 13.4f, 0.80f, t0 + Deck + 1.55f, 0f, false).Named("Court Clock")
            .Set("PuzzleTimer", "duration", 480f);
        Piece strikes = b.Part("Progress Bar", 13.4f, 0.80f, t0 + Deck + 0.95f, 0f, false).Named("Strikes")
            .Scaled(0.5f, 1f, 1f).Set("PuzzleProgressBar", "total", 2).Set("PuzzleProgressBar", "style", 0);
        Component clockC = clock.Component("PuzzleTimer");
        Component strikesC = strikes.Component("PuzzleProgressBar");

        // ---- lobby groups: 2-3 players get one five-flap kiosk, 4 players two kiosks (three flaps and two) ----
        Piece three = IsleKit.Lobby(b, "Three Players", true, true, false);
        Piece four = IsleKit.Lobby(b, "Four Players", false, false, true);

        // Kiosk A (all lobbies) and Kiosk B (4 players only): closed north, east, west and roof; open to the south
        b.MeshPiece("Kiosk A", Kiosk(16f, -8f, kioskA).ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        Piece kioskBMesh = b.MeshPiece("Kiosk B", Kiosk(4f, -13.5f, kioskB).ToMesh(), snowy);
        kioskBMesh.Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        kioskBMesh.Under(four);
        // warm lamps in each kiosk (Winter Walk's RoomLights; it skips inactive objects, so Kiosk B is lit only with it)
        b.Empty("Room Light 1 FFE2B0 4.0 1.2", 16f, -8f, kioskA + 2.4f, false);
        b.Empty("Room Light 2 FFE2B0 4.0 1.2", 4f, -13.5f, kioskB + 2.4f, false).Under(four);

        // Displays on the kiosks' inner north walls, facing south into the kiosk
        Piece commission5 = b.Part("Display", 16f, -7.37f, kioskA + 1.7f, 180f, false).Named("Commission 5")
            .Set("PuzzleDisplay", "size", 3).Under(three);      // Five
        Piece commission3 = b.Part("Display", 16f, -7.37f, kioskA + 1.7f, 180f, false).Named("Commission 3")
            .Set("PuzzleDisplay", "size", 2).Under(four);       // Three
        Piece commission2 = b.Part("Display", 4f, -12.87f, kioskB + 1.7f, 180f, false).Named("Commission 2")
            .Set("PuzzleDisplay", "size", 1).Under(four);       // Two
        Component c5 = commission5.Component("PuzzleDisplay"), c3 = commission3.Component("PuzzleDisplay"), c2 = commission2.Component("PuzzleDisplay");

        // The sculpture: a board, its check screen and button on the plinth's east face, one set per lobby group at
        // the same place (slot 1 at the pivot end, z 0.9; facing the board, slot 1 is at the sculptor's far left)
        Sculpture(b, three, "3", strikesC, new[] { c5, c5, c5, c5, c5 }, new[] { 1, 2, 3, 4, 5 });
        Sculpture(b, four, "4", strikesC, new[] { c3, c3, c3, c2, c2 }, new[] { 1, 2, 3, 1, 2 });

        // ---- events: the clock, the strikes and the blind entry follow the puzzle; the only win is the Validator ----
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", clockC, "StartTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", blindEntry.Component("PuzzleNoItemsGate"), "Unlock");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", clockC, "ResetTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", strikesC, "Reset");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", blindEntry.Component("PuzzleNoItemsGate"), "Lock");
        b.RootPiece.On("PuzzleSDK", "onWin", clockC, "PauseTimer");
        clock.On("PuzzleTimer", "onFinished", b.Puzzle, "StopPuzzle");       // out of time: stop, so the next start re-deals
        strikes.On("PuzzleProgressBar", "onFull", b.Puzzle, "StopPuzzle");   // second wrong check

        // ---- BlindWatch: the court is blind-only. Anyone inside its box (wall outer faces, from just under the pad
        //      to 4.5 m up: wall tops, plinth and racks included) without a blindfold is put out at the Exit Point ----
        Piece watch = b.Empty("Court Watch", 0f, 2.0f, Floor, false);
        Piece exitPoint = b.Empty("Exit Point", 3.0f, -6.6f, 1.0f);
        exitPoint.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, 180f, 0f);      // facing south, away from the court
        Component bw = b.AddCustomScript(watch, "BlindWatch");
        Sdk.Set(bw, "boxMin", new Vector3(-6.3f, -0.5f, -6.3f));
        Sdk.Set(bw, "boxMax", new Vector3(6.15f, 4.5f, 6.3f));
        Sdk.Set(bw, "putOut", exitPoint.Transform);
        Sdk.Add(bw, "gates", blindEntry.Transform, wayOut.Transform);
        Sdk.Set(bw, "gateSkip", 1.2f);
        Sdk.Set(bw, "cooldown", 1.5f);
        Sdk.Set(bw, "interval", 0.25f);
        Sdk.Set(bw, "dropHeld", true);
        Sdk.Set(bw, "onlyWhileRunning", false);

        // ---- outside: three blindfolds on a rack west of the entry, the start corner, lights, calm air ----
        IsleKit.BlindfoldHook(b, -6.6f, -5.8f, 90f, 0f, true, " A");
        IsleKit.BlindfoldHook(b, -6.6f, -6.8f, 90f, 0f, true, " B");
        IsleKit.BlindfoldHook(b, -6.6f, -7.8f, 90f, 0f, true, " C");
        IsleKit.StartCorner(b, -1.5f, -8.5f, 0f);
        Kit.LightRing(b, "Site Lights", 5f, -2f, 36f, 28f, 2.6f, true, 151);
        // no jetpacks over the court, the tower or the kiosks: from 10 m under the ground to 140 m up, 60 x 56 m
        IsleKit.CalmAir(b, "Blind Sculptor", 4f, 0f, new Vector3(60f, 150f, 56f), 0f, -10f);
    }

    // The peg board, check screen and check button of one lobby group, on the plinth's east face
    static void Sculpture(PuzzleBuilder b, Piece group, string tag, Component strikes, Component[] answerFrom, int[] flaps)
    {
        Piece board = b.Part("Peg Board", -0.9f, 0.9f, Floor + 1.1f, 90f, false).Named("Sculpture Board " + tag).Under(group);
        Piece check = b.Part("Validator", -0.9f, 3.7f, Floor + 1.35f, 90f, false).Named("Check Screen " + tag).Under(group);
        Piece poke = b.Part("Button Poke", -0.9f, 3.7f, Floor + 0.65f, 90f, false).Named("Check Button " + tag).Under(group);
        Component boardC = board.Component("PuzzlePegBoard");
        Component checkC = check.Component("PuzzleValidator");
        Sdk.Set(boardC, "size", 4);      // Slots5
        Sdk.Set(boardC, "startMarker", true);
        for (int i = 0; i < answerFrom.Length; i++)
        {
            Sdk.Add(boardC, "answerFrom", answerFrom[i]);
            Sdk.AddInts(boardC, "answerFromFlap", flaps[i]);
        }
        Sdk.Set(boardC, "validator", checkC);
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));
        check.On("PuzzleValidator", "onIncorrect", strikes, "Increment");
    }

    // A kiosk at (cx, cz) on ground 'g' (above the root's level): inside 3.0 x 1.6 m, walls 2.6 m, open to the south
    static MeshBuilder Kiosk(float cx, float cz, float g)
    {
        var mb = new MeshBuilder();
        float x0 = cx - 1.5f, x1 = cx + 1.5f, z0 = cz - 0.8f, z1 = cz + 0.8f, w = 0.15f;
        Block(mb, x0 - w, x1 + w, z1, z1 + w, g - 0.3f, g + 2.6f, IsleKit.Wood);      // north (the display's wall)
        Block(mb, x0 - w, x0, z0, z1, g - 0.3f, g + 2.6f, IsleKit.Wood);              // west
        Block(mb, x1, x1 + w, z0, z1, g - 0.3f, g + 2.6f, IsleKit.Wood);              // east
        Block(mb, x0 - 0.35f, x1 + 0.35f, z0 - 0.45f, z1 + 0.3f, g + 2.6f, g + 2.75f, IsleKit.Red);      // roof, reaching out over the opening
        Block(mb, x0 - w, x1 + w, z0 - 0.05f, z0 + 0.05f, g + 2.35f, g + 2.6f, IsleKit.Green);          // fascia over the opening
        Block(mb, x0, x1, z0, z1, g - 0.3f, g + 0.05f, IsleKit.DarkWood);            // floor boards
        return mb;
    }

    // One rack at centre c: body with the 55 degree face, lip, back shelf, end walls and two dividers
    static void Rack(MeshBuilder mb, Vector2 c)
    {
        float x = c.x, z0 = c.y - RackHalfZ, z1 = c.y + RackHalfZ;
        float hi = Floor + SlopeHighH, lo = Floor + SlopeLowH;
        // body: sloped top, high (west, x SlopeHighX) to low (east, x SlopeLowX)
        Hex(mb, x + SlopeHighX, x + SlopeLowX, z0, z1, Floor, Floor, Floor, Floor, hi, lo, lo, hi, IsleKit.DarkWood);
        Block(mb, x - RackHalfX, x + SlopeHighX, z0, z1, Floor, hi, IsleKit.DarkWood);                       // back shelf
        Block(mb, x + SlopeLowX, x + RackHalfX, z0, z1, Floor, lo + LipHeight, IsleKit.Wood);               // lip on the low edge
        // end walls and dividers: thin plates following the slope 0.1 m above it
        foreach (float dz in new[] { -RackHalfZ + 0.02f, -0.325f, 0.325f, RackHalfZ - 0.02f })
            Hex(mb, x + SlopeHighX, x + SlopeLowX, c.y + dz - 0.02f, c.y + dz + 0.02f, Floor, Floor, Floor, Floor,
                hi + 0.1f, lo + 0.1f, lo + 0.1f, hi + 0.1f, IsleKit.Wood);
    }

    // An axis-aligned block from (x0, y0, z0) to (x1, y1, z1), in the root's local space
    static void Block(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float y1, Color color)
        => Kit.AddBox(mb, new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), color);

    // A block over the rectangle x0..x1, z0..z1 whose bottom and top heights are given at each corner, in the order
    // (x0, z0), (x1, z0), (x1, z1), (x0, z1): slopes and wedges. Faces wound as Kit.AddBox winds them.
    static void Hex(MeshBuilder mb, float x0, float x1, float z0, float z1,
        float b00, float b10, float b11, float b01, float t00, float t10, float t11, float t01, Color color)
    {
        var low = new[] { new Vector3(x0, b00, z0), new Vector3(x1, b10, z0), new Vector3(x1, b11, z1), new Vector3(x0, b01, z1) };
        var high = new[] { new Vector3(x0, t00, z0), new Vector3(x1, t10, z0), new Vector3(x1, t11, z1), new Vector3(x0, t01, z1) };
        for (int i = 0; i < 4; i++) mb.Quad(low[i], low[(i + 1) % 4], high[(i + 1) % 4], high[i], color);
        mb.Tri(high[0], high[3], high[2], color); mb.Tri(high[0], high[2], high[1], color);
        mb.Tri(low[0], low[1], low[2], color); mb.Tri(low[0], low[2], low[3], color);
    }
}
