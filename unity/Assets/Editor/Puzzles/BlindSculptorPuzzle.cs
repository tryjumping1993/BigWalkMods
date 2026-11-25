// Blind Sculptor (puzzle 15, blind mask): the Snow Sculptor's Court. A blindfold game for a group, in the open, with
// no clock.
//
// A walled court holds a snow plinth with a five-slot peg board (the "sculpture") and twelve picture tiles lying in
// four sloped racks. Starting the puzzle deals the commission: five of the twelve pictures, in order, on a split-flap
// Display on the board at the watchtower's south rail. Two copies of it hang outside the court: on the south wall
// between the turnstiles, and on a board at the north end of the guides' step.
//   - The sculptor puts on a blindfold and goes in through the blind turnstile. The court is for the blindfolded
//     only: BlindWatch (a host script) puts anyone inside it who is not wearing a blindfold straight back outside.
//   - Everyone else guides. From the watchtower's deck, or from the stone step along the court's east railing, they
//     see the commission, the tiles' pictures and the board, and talk the sculptor to each of the five tiles and onto
//     the right slot: the commission's leftmost picture goes on the board's leftmost slot (the end with the marker),
//     and so on along. The telescope on the deck is a plain one, for reading the far racks.
//   - The sculptor presses the check button on the plinth. Right wins the gourd; wrong only shows the cross, and
//     they carry on.
// Two players can finish it (one blind, one guiding); it is built for three or four, who take turns under the
// blindfold. Nobody is shut in and nothing muffles a voice: the court's east side is a railing of bars, not glass.
// (Before the user's rules of 2026-11-24: tiles in invisible ink that only the tower's ink telescope showed, the
// commission hidden in a kiosk, an eight-minute clock, and two wrong checks stopped the puzzle and dealt again.)
// The picture set is the Peg set (index 1, the game's set B), reserved for this puzzle island-wide: the board
// matches tiles by picture only, so no other island puzzle may use Peg tiles.
//
// Local coordinates: x right, z forward (+z north at yaw 0). Court parts stand on the level pad (onGround: false,
// measured from the root's level, pad top at Floor); the tower stands on the ground under its centre.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BlindSculptorPuzzle : PuzzleDef
{
    public override string Name => "BlindSculptor";
    public override Vector2 Footprint => new Vector2(38f, 32f);
    public override Vector2? FixedSite => new Vector2(-1674f, -798f);      // layout-final: north shore, a sheltered court on its own flat
    public override float Yaw => 0f;      // the railing side (+x) to the open ground east, where the watchtower stands

    const float Floor = 0.25f;            // the pad's top, above the root's level
    const float WallTop = 3.0f;           // the stone walls (west, north, south), above the pad
    const float RailTop = 2.0f;           // the east railing, above the pad
    const float StepTop = 0.6f;           // the guides' step outside the railing, above the pad
    const float Deck = 4.2f;              // the watchtower's platform, above the ground under it
    static readonly Color SnowWhite = new Color(0.93f, 0.95f, 0.98f);

    // A five-flap Display's pivot is the middle of flap 1 (its reader's far left) and its frame runs on 1.4 m to the
    // reader's right, so the frame's middle is 0.7 m along from the pivot; its body is 0.168 m deep behind the pivot
    const float FlapsMid = 0.7f, DisplayBack = 0.168f;

    // Racks: centre (x, z) and the Peg image in each bay (z - 0.65, z, z + 0.65)
    static readonly Vector2[] RackAt = { new Vector2(-4.6f, 6.4f), new Vector2(1.2f, 6.5f), new Vector2(-4.6f, -1.9f), new Vector2(3.8f, 3.0f) };
    static readonly string[] RackName = { "North-West", "North", "South-West", "East" };
    static readonly int[][] RackTiles = { new[] { 1, 7, 4 }, new[] { 10, 2, 8 }, new[] { 5, 11, 3 }, new[] { 12, 6, 9 } };

    // Rack profile (x relative to the rack's centre; heights above the pad): the face slopes 55 degrees, rising west
    const float RackHalfX = 0.35f, RackHalfZ = 1.0f;
    const float SlopeLowX = 0.30f, SlopeLowH = 0.55f, SlopeHighH = 1.10f;      // the lip takes x 0.30..0.35
    static readonly float SlopeHighX = SlopeLowX - (SlopeHighH - SlopeLowH) / Mathf.Tan(55f * Mathf.Deg2Rad);      // about -0.085
    const float LipHeight = 0.12f;

    // Letterbox through the south wall (centre x, width, slot height, outer and inner mouth bottoms above the pad):
    // a tile that has left the court (in a pocket, or carried out through the way out) is posted back in here
    const float SlotX = -1.0f, SlotW = 0.6f, SlotH = 0.32f, SlotOuter = 1.2f, SlotInner = 0.95f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(-1.5f, -11f);
        b.ClearGround(38f, 30f, 5f, -2f);
        Material snowy = IsleKit.Snowy(b);
        Material bare = IsleKit.Bare(b);
        float rootY = b.Root.transform.position.y;
        float tower = b.WorldPoint(13.4f, 2.0f).y - rootY;      // ground under the watchtower, above the root's level

        // ---- the court: pad, stone walls (3 m, sheer) on three sides, the railing on the east, the plinth and the
        //      four racks ----
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
        // the plaque on the south wall's outer face, between the letterbox and the way out, for a copy of the commission
        Block(court, -0.45f, 1.75f, -4.36f, -4.3f, Floor + 1.4f, Floor + 2.1f, IsleKit.Board);
        // east side, where the guides look in: a stone sill to 0.9 m and corner piers as high as the walls; between
        // them a railing to RailTop, slim bars 0.3 m apart (no player passes) between stone piers. Bars, not glass:
        // the game's glass muffles voices. The railing is low so that the watchtower's deck sees the board and the
        // north and west racks over it in clear air (its top rail lies between those sight lines and the east rack's)
        Block(court, 5.85f, 6.15f, -4.3f, 8.3f, Floor, Floor + 0.9f, IsleKit.Stone);
        Block(court, 5.85f, 6.15f, -4.3f, -3.85f, Floor + 0.9f, Floor + WallTop, IsleKit.Stone);      // corner piers
        Block(court, 5.85f, 6.15f, 7.85f, 8.3f, Floor + 0.9f, Floor + WallTop, IsleKit.Stone);
        for (int k = 0; k < 5; k++)
        {
            float z = -2.0f + 2f * k;      // piers between the bays (bay centres -3, -1, 1, 3, 5, 7)
            Block(court, 5.85f, 6.15f, z - 0.15f, z + 0.15f, Floor + 0.9f, Floor + RailTop + 0.1f, IsleKit.Stone);
        }
        for (int bay = 0; bay < 6; bay++)
        {
            float z = -3f + 2f * bay;
            Block(court, 5.95f, 6.05f, z - 0.85f, z + 0.85f, Floor + RailTop - 0.06f, Floor + RailTop, IsleKit.DarkWood);      // top rail
            for (int k = 0; k < 4; k++)
            {
                float bz = z + (k - 1.5f) * 0.34f;
                Block(court, 5.98f, 6.02f, bz - 0.02f, bz + 0.02f, Floor + 0.9f, Floor + RailTop - 0.06f, IsleKit.DarkWood);
            }
        }
        // the guides' step along the outside of the railing: a player's eyes are only 0.6 m above their feet and the
        // sill's top is 1.15 m above the ground, so they stand on this to look in (three risers of 0.28 m)
        float rise = (Floor + StepTop) / 3f;
        float[] treadX = { 6.15f, 7.15f, 7.5f, 7.85f };
        for (int s = 0; s < 3; s++)
            Block(court, treadX[s], treadX[s + 1], -4.3f, 8.3f, Floor - 1.75f, Floor + StepTop - s * rise, IsleKit.Stone);
        // a board on two posts just past the step's north end, facing south along it, for the guides' copy of the commission
        Block(court, 6.15f, 6.27f, 8.72f, 8.84f, Floor - 0.75f, Floor + 2.2f, IsleKit.DarkWood);
        Block(court, 8.43f, 8.55f, 8.72f, 8.84f, Floor - 0.75f, Floor + 2.2f, IsleKit.DarkWood);
        Block(court, 6.27f, 8.43f, 8.75f, 8.81f, Floor + 1.4f, Floor + 2.1f, IsleKit.Board);
        Block(court, 6.0f, 8.7f, 8.5f, 9.0f, Floor + 2.2f, Floor + 2.3f, IsleKit.Red);      // a little roof over it
        // the sculpture plinth: 0.6 x 4.2 x 1.6 m of packed snow, the board, check screen and button on its east face
        Block(court, -1.5f, -0.9f, 0.1f, 4.3f, Floor, Floor + 1.6f, SnowWhite);
        // the racks: a sloped face rising west (so it looks east at the guides), a lip on its low edge, three bays
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

        // Christmas lights along the three stone walls' tops, corner pier to corner pier (no posts: the wire lies
        // just over the coping); none across the railing, where the guides look in
        Kit.LightString(b, "Court Lights", new List<Vector3>
        {
            new Vector3(6.0f, Floor + WallTop + 0.15f, -4.15f), new Vector3(-6.15f, Floor + WallTop + 0.15f, -4.15f),
            new Vector3(-6.15f, Floor + WallTop + 0.15f, 8.15f), new Vector3(6.0f, Floor + WallTop + 0.15f, 8.15f),
        }, 0f, 0.5f, false, false, 157);

        // ---- gates: blind entry (locked while the puzzle is off), plain one-way exit (never locked) ----
        Piece blindEntry = IsleKit.BlindGate(b, -3.0f, -4.0f, 0f, Floor, false).Named("Blind Entry")
            .Set("PuzzleNoItemsGate", "defaultLocked", true);
        Piece wayOut = b.Part("Gate No items", 3.0f, -4.0f, Floor, 180f, false).Named("Way Out")      // front (-z turned by 180) is the court side
            .Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "oneWay", true).Set("PuzzleNoItemsGate", "defaultLocked", false);
        // inside stop switch and its live lamp, on the plaque by the exit: quitting from inside
        b.Part("Switch Start", 0.0f, -3.775f, Floor + 1.25f, 0f, false).Named("Inside Stop Switch");
        b.Part("Live Light", 0.35f, -3.82f, Floor + 1.42f, 0f, false).Named("Inside Live Light");

        // ---- the twelve tiles, lying face up the slope in their bays (face east and 35 degrees up), their pictures
        //      plain to everyone ----
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
                    .Set("PuzzlePegTile", "image", image);
            }

        // ---- the watchtower, the guides' high viewpoint: deck 4.2 m, west edge 6 m from the railing, the stair
        //      climbing from the east ----
        var tw = new MeshBuilder();
        float t0 = tower;
        foreach (float lx in new[] { 12.15f, 14.65f })
            foreach (float lz in new[] { 0.75f, 3.25f })
                Block(tw, lx - 0.13f, lx + 0.13f, lz - 0.13f, lz + 0.13f, t0 - 0.4f, t0 + Deck - 0.2f, IsleKit.DarkWood);      // legs
        Block(tw, 12.0f, 14.8f, 0.6f, 3.4f, t0 + Deck - 0.2f, t0 + Deck, IsleKit.Wood);      // the deck
        Block(tw, 12.0f, 12.15f, 0.6f, 3.4f, t0 + Deck, t0 + Deck + 0.3f, IsleKit.DarkWood);      // a 0.3 m kerb on the west edge only: nothing between the guides and the court
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
        // the commission board on the south rail, facing north across the deck (a little thicker than the rail and
        // its posts, which run through it)
        Block(tw, 12.2f, 14.6f, 0.56f, 0.70f, t0 + Deck + 0.7f, t0 + Deck + 2.1f, IsleKit.Board);
        Block(tw, 12.1f, 12.2f, 0.58f, 0.68f, t0 + Deck, t0 + Deck + 2.25f, IsleKit.DarkWood);
        Block(tw, 14.6f, 14.7f, 0.58f, 0.68f, t0 + Deck, t0 + Deck + 2.25f, IsleKit.DarkWood);
        Block(tw, 12.0f, 14.8f, 0.45f, 0.95f, t0 + Deck + 2.25f, t0 + Deck + 2.35f, IsleKit.Red);      // a little roof over it
        b.MeshPiece("Watchtower", tw.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        Kit.LightString(b, "Tower Lights", new List<Vector3>
        {
            new Vector3(12.05f, t0 + Deck + 1.15f, 3.37f), new Vector3(14.75f, t0 + Deck + 1.15f, 3.37f),
            new Vector3(18.95f, t0 + 1.4f, 2.65f),
        }, 0.15f, 0.45f, false, false, 158);

        // a plain telescope on the deck (its pillar's foot is 1.1 m under its pivot), looking west into the court
        b.Part("Telescope", 12.5f, 2.0f, t0 + Deck + 1.1f, -90f, false).Named("Tower Telescope")
            .Set("PuzzleTelescope", "invisibleInkView", false);

        // ---- the commission: five flaps on the tower's board, 1.7 m above the deck, facing north: a guide at the
        //      north rail has it in front and the court on their right. Flap 1 is its reader's far left (the east
        //      end). The copies always show what it shows: one on the south wall's outer plaque, one on the board
        //      at the step's north end, both facing south, 2 m above the ground ----
        Piece commission = b.Part("Display", 13.4f + FlapsMid, 0.70f + DisplayBack, t0 + Deck + 1.7f, 0f, false).Named("Commission")
            .Set("PuzzleDisplay", "size", 3);      // Five
        Component commissionC = commission.Component("PuzzleDisplay");
        b.Part("Display", 0.65f - FlapsMid, -4.36f - DisplayBack, Floor + 1.75f, 180f, false).Named("Commission Copy Entry")
            .Set("PuzzleDisplay", "size", 3).Set("PuzzleDisplay", "copyDisplay", commissionC);
        b.Part("Display", 7.35f - FlapsMid, 8.75f - DisplayBack, Floor + 1.75f, 180f, false).Named("Commission Copy Step")
            .Set("PuzzleDisplay", "size", 3).Set("PuzzleDisplay", "copyDisplay", commissionC);
        // a warm lamp before each of the three, lit only as far as it is night (Winter Walk's RoomLights)
        b.Empty("Room Light Night 1 FFE2B0 4.0 1.2", 13.4f, 1.7f, t0 + Deck + 2.0f, false);
        b.Empty("Room Light Night 2 FFE2B0 4.0 1.2", 0.65f, -5.3f, Floor + 2.2f, false);
        b.Empty("Room Light Night 3 FFE2B0 4.0 1.2", 7.35f, 7.8f, Floor + 2.2f, false);

        // ---- the sculpture: a board, its check screen and button on the plinth's east face (slot 1 at the pivot
        //      end, z 0.9: facing the board, from the court or from the tower, slot 1 is at the far left) ----
        Piece board = b.Part("Peg Board", -0.9f, 0.9f, Floor + 1.1f, 90f, false).Named("Sculpture Board");
        Piece check = b.Part("Validator", -0.9f, 3.7f, Floor + 1.35f, 90f, false).Named("Check Screen");
        Piece poke = b.Part("Button Poke", -0.9f, 3.7f, Floor + 0.65f, 90f, false).Named("Check Button");
        Component boardC = board.Component("PuzzlePegBoard");
        Component checkC = check.Component("PuzzleValidator");
        Sdk.Set(boardC, "size", 4);      // Slots5
        Sdk.Set(boardC, "startMarker", true);
        for (int flap = 1; flap <= 5; flap++)
        {
            Sdk.Add(boardC, "answerFrom", commissionC);
            Sdk.AddInts(boardC, "answerFromFlap", flap);
        }
        Sdk.Set(boardC, "validator", checkC);
        Sdk.Set(checkC, "pokeButton", poke.Component("PuzzlePokeButton"));

        // ---- events: the blind entry is open while the puzzle runs. The only win is the Validator's; a wrong check
        //      is wired to nothing, so the cross is all it costs ----
        Component entryGate = blindEntry.Component("PuzzleNoItemsGate");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", entryGate, "Unlock");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", entryGate, "Lock");

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
        // no jetpacks over the court, the step or the tower: from 10 m under the ground to 140 m up, 60 x 56 m
        IsleKit.CalmAir(b, "Blind Sculptor", 4f, 0f, new Vector3(60f, 150f, 56f), 0f, -10f);
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
