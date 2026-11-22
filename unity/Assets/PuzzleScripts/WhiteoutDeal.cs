// Whiteout Maze's deal: on every start the maze's eight hidden pad sensors (one per lamp on the hut's board) are dealt
// out afresh among the eight cairn alcoves, and every cairn gets a new picture; each lamp's picture on the board is then
// set to the picture of the cairn its pad moved to. So neither the board's layout nor past pictures say where a lamp's
// cairn is: only the spotter, reading the face-up cairn pictures from the gantry, can find it. 0.6 s after the deal it
// calls Begin() on the Press In Orders itself (so the moved trigger boxes are in place before a press counts, and a
// player standing where a pad lands is ignored: a sensor fires only when its state changes).
//
// On an empty in the puzzle (b.AddCustomScript(empty, "WhiteoutDeal")), finding by name under the puzzle's root:
//   "Board Picture k"   the board's display beside lamp k, k = 1..8 (Random On Start off: only this script sets it)
//   "Cairn Picture j"   the face-up display over cairn j, j = 1..8 (Random On Start off)
//   "Alcove j"          an empty in cairn j's alcove at a pad's centre height, turned with the alcove (never moves)
//   "Pad k"             rig A's k-th button (a Presence Sensor, one player), moved to an alcove on every deal
//   "Pad k Pair"        rig B's k-th button (two players, 4-player lobbies), moved with Pad k
//   "Press In Order A"  rig A (2- and 3-player lobbies); "Press In Order B" rig B (4-player lobbies)
// Deal() is wired to the puzzle's On Puzzle Start, Clear() to On Puzzle Stop.
//
// Only the host acts (PuzzleSDK.IsHost). Presence is judged only on the host (the sensors' Update returns at once on
// other PCs) and Press In Order decides only there, so the pads need to move only there; the pictures are set with
// PuzzleDisplay.SetImage / Clear, which are host-only and reach every player through the SDK's own sync. Nothing is
// synced by this script. The rig hidden in this lobby (PuzzleHider) was never set up, so its Begin() returns at once.
//
// This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent and plain C#.
using System;
using System.Runtime.CompilerServices;
using BigPuzzleSDK;
using UnityEngine;

public class WhiteoutDeal : MonoBehaviour
{
    const int Count = 8;

    // The game's image set the pictures are drawn from (it needs at least Count pictures)
    [SerializeField] private PuzzleImageSet imageSet = PuzzleImageSet.A;
    // Seconds from the deal to the first lamp's Begin()
    [SerializeField] private float beginDelay = 0.6f;

    private readonly Transform[] boards = new Transform[Count + 1];
    private readonly Transform[] cairns = new Transform[Count + 1];
    private readonly Transform[] alcoves = new Transform[Count + 1];
    private readonly Transform[] padsA = new Transform[Count + 1];
    private readonly Transform[] padsB = new Transform[Count + 1];
    private Transform rigA, rigB;
    private float beginAt = -1f;
    private bool syncFailed;

    // On Puzzle Start: deal pads and pictures, then Begin() the rigs a moment later (see Update)
    public void Deal()
    {
        if (!PuzzleSDK.IsHost) return;
        Collect();

        int[] alcoveOf = Shuffled();      // lamp / pad k (0-based) -> alcove alcoveOf[k] (0-based)
        int[] picOf = Shuffled();         // alcove j (0-based) shows picture picOf[j] + 1

        for (int j = 1; j <= Count; j++)
        {
            PuzzleDisplay cairn = DisplayOn(cairns[j]);
            if (cairn != null) cairn.SetImage(1, imageSet, picOf[j - 1] + 1);
            else Debug.LogWarning("[WhiteoutDeal] " + name + ": no display named 'Cairn Picture " + j + "'");
        }

        string text = "";
        int moved = 0;
        for (int k = 1; k <= Count; k++)
        {
            int a = alcoveOf[k - 1] + 1;
            Transform to = alcoves[a];
            if (to != null)
            {
                if (Move(padsA[k], to)) moved++;
                Move(padsB[k], to);
            }
            else Debug.LogWarning("[WhiteoutDeal] " + name + ": no 'Alcove " + a + "'");
            PuzzleDisplay board = DisplayOn(boards[k]);
            if (board != null) board.SetImage(1, imageSet, picOf[a - 1] + 1);
            else Debug.LogWarning("[WhiteoutDeal] " + name + ": no display named 'Board Picture " + k + "'");
            text += (text.Length > 0 ? ", " : "") + "lamp " + k + "->alcove " + a + " (picture " + (picOf[a - 1] + 1) + ")";
        }
        SyncPhysics();
        beginAt = Time.time + (beginDelay > 0.1f ? beginDelay : 0.1f);
        Debug.Log("[WhiteoutDeal] " + name + ": " + imageSet + "; " + text + "; " + moved + " of " + Count + " pads moved");
    }

    // On Puzzle Stop (and once at spawn): cancel a pending Begin() and blank every picture
    public void Clear()
    {
        if (!PuzzleSDK.IsHost) return;
        beginAt = -1f;
        Collect();
        for (int k = 1; k <= Count; k++)
        {
            PuzzleDisplay board = DisplayOn(boards[k]);
            if (board != null) board.Clear();
            PuzzleDisplay cairn = DisplayOn(cairns[k]);
            if (cairn != null) cairn.Clear();
        }
    }

    void Update()
    {
        if (beginAt < 0f) return;
        if (!PuzzleSDK.IsHost) { beginAt = -1f; return; }
        if (Time.time < beginAt) return;
        beginAt = -1f;
        PuzzleSDK puzzle = PuzzleSDK.Of(transform);
        if (puzzle == null || !puzzle.IsRunning)
        {
            Debug.Log("[WhiteoutDeal] " + name + ": the puzzle stopped before the first lamp; no Begin()");
            return;
        }
        int begun = Begin(rigA) + Begin(rigB);
        Debug.Log("[WhiteoutDeal] " + name + ": Begin() on " + begun + " rig(s)");
    }

    static int Begin(Transform rig)
    {
        if (rig == null) return 0;
        PuzzlePressInOrder order = rig.GetComponent<PuzzlePressInOrder>();
        if (order == null) return 0;
        order.Begin();      // returns at once on the rig this lobby hides (it was never set up)
        return 1;
    }

    // A uniformly random permutation of 0 .. Count-1 (Fisher-Yates)
    static int[] Shuffled()
    {
        int[] p = new int[Count];
        for (int i = 0; i < Count; i++) p[i] = i;
        for (int i = Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = p[i]; p[i] = p[j]; p[j] = t;
        }
        return p;
    }

    // Puts a pad where an alcove's anchor is, turned as it is (the alcoves face different ways)
    static bool Move(Transform pad, Transform to)
    {
        if (pad == null) return false;
        pad.position = to.position;
        pad.rotation = to.rotation;
        return true;
    }

    // Finds the named objects under the puzzle's root, walking the hierarchy by hand (inactive ones too)
    void Collect()
    {
        for (int k = 0; k <= Count; k++) { boards[k] = null; cairns[k] = null; alcoves[k] = null; padsA[k] = null; padsB[k] = null; }
        rigA = null; rigB = null;
        Walk(transform.parent != null ? transform.parent : transform.root);      // this empty is a direct child of the puzzle's root
    }

    void Walk(Transform t)
    {
        if (t == null) return;
        string n = t.name;
        if (n == "Press In Order A") rigA = t;
        else if (n == "Press In Order B") rigB = t;
        else
        {
            for (int k = 1; k <= Count; k++)
            {
                string number = k.ToString();
                if (n == "Board Picture " + number) boards[k] = t;
                else if (n == "Cairn Picture " + number) cairns[k] = t;
                else if (n == "Alcove " + number) alcoves[k] = t;
                else if (n == "Pad " + number) padsA[k] = t;
                else if (n == "Pad " + number + " Pair") padsB[k] = t;
            }
        }
        int children = t.childCount;
        for (int i = 0; i < children; i++) Walk(t.GetChild(i));
    }

    static PuzzleDisplay DisplayOn(Transform t)
    {
        if (t == null) return null;
        PuzzleDisplay d = t.GetComponent<PuzzleDisplay>();
        if (d != null) return d;
        try { return Deeper(t); } catch (Exception) { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static PuzzleDisplay Deeper(Transform t) => BwmFind.First<PuzzleDisplay>(t);

    // The moved trigger boxes are where the sensors look before their next check. Kept apart so a stripped engine
    // method fails only this call (physics catches up at the next fixed step anyway, well inside beginDelay).
    void SyncPhysics()
    {
        if (syncFailed) return;
        try { SyncNow(); }
        catch (Exception e)
        {
            syncFailed = true;
            Debug.Log("[WhiteoutDeal] Physics.SyncTransforms unavailable (" + e.GetType().Name + "); physics syncs at the next step");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SyncNow() => Physics.SyncTransforms();
}
