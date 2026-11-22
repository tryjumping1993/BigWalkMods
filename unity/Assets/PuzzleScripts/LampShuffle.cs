// Fairy Lights' shuffle: on every start the glade's invisible pad sensors are dealt out afresh among the pad spots,
// and each lamp's picture on the Lodge board is set to the picture hanging over the spot its sensor moved to. So the
// lamp board's slots say nothing about where a pad is: only the picture does.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "LampShuffle")), found by name under the puzzle's root:
//   "Pad Sensor k"     the Press In Orders' k-th button (a Presence Sensor), k = 1..6; 5 and 6 only in a 4-player lobby
//   "Pad Spot k"       an empty at spot k (built where sensor k starts), which never moves
//   "Picture k"        the glade display hanging over spot k (dealt by the SDK's pile on every start), which never moves
//   "Board Picture k"  the Lodge board's display beside lamp k (Random On Start off: only this script sets it)
// Shuffle() is wired to the puzzle's On Puzzle Start (after the SDK's deal), Blank() to On Puzzle Stop.
//
// Only the host acts (PuzzleSDK.IsHost). Presence is judged only on the host (the sensors' Update returns at once on
// other PCs), so the sensors need to move only there; the board pictures are set with PuzzleDisplay.SetImage, which is
// host-only and reaches every player through the SDK's own sync. Nothing is synced by this script.
//
// This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent and plain C#.
using System;
using System.Runtime.CompilerServices;
using BigPuzzleSDK;
using UnityEngine;

public class LampShuffle : MonoBehaviour
{
    const int Count = 6;

    private readonly Transform[] sensors = new Transform[Count + 1];
    private readonly Transform[] spots = new Transform[Count + 1];
    private readonly Transform[] pictures = new Transform[Count + 1];
    private readonly Transform[] boards = new Transform[Count + 1];
    private bool shuffled;
    private bool syncFailed;

    // On Puzzle Start: deal the sensors out among the spots and fill the board's pictures
    public void Shuffle()
    {
        if (!PuzzleSDK.IsHost) return;
        Collect();

        // the pads in play: those whose sensor is active (4 in a 2- or 3-player lobby, 6 in a 4-player one)
        int[] active = new int[Count];
        int n = 0;
        for (int k = 1; k <= Count; k++)
        {
            if (sensors[k] == null || spots[k] == null || pictures[k] == null || boards[k] == null) continue;
            if (!sensors[k].gameObject.activeInHierarchy) continue;
            active[n++] = k;
        }
        if (n == 0)
        {
            Debug.LogWarning("[LampShuffle] " + name + ": no pad sensors, spots, pictures or board pictures found by name; nothing shuffled");
            return;
        }

        // a uniformly random permutation of the active pads (Fisher-Yates)
        int[] perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = active[i];
        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = perm[i]; perm[i] = perm[j]; perm[j] = t;
        }

        string text = "";
        for (int i = 0; i < n; i++)
        {
            int k = active[i], s = perm[i];
            sensors[k].position = spots[s].position;
            PuzzleDisplay picture = DisplayOn(pictures[s]);
            PuzzleDisplay board = DisplayOn(boards[k]);
            if (picture == null || board == null)
            {
                Debug.LogWarning("[LampShuffle] " + name + ": no PuzzleDisplay on '" + (picture == null ? pictures[s].name : boards[k].name) + "'");
                continue;
            }
            PuzzleImageSet set = picture.GetImageSet(1);
            int image = picture.GetImage(1);
            if (image <= 0) Debug.LogWarning("[LampShuffle] " + name + ": '" + pictures[s].name + "' is blank (was the deal short of tiles?)");
            board.SetImage(1, set, image);
            text += (text.Length > 0 ? ", " : "") + k + "->" + s + " (" + set + " " + image + ")";
        }
        SyncPhysics();
        shuffled = true;

        int online = 0;
        var players = PuzzleSDK.players;
        if (players != null) for (int i = 0; i < players.Count; i++) if (players[i] != null && players[i].isOnline) online++;
        Debug.Log("[LampShuffle] " + name + ": p = " + text);
        Debug.Log("[LampShuffle] " + name + ": " + n + " pads in play (" + (n >= 6 ? "4-player" : "2/3-player") + " build), " + online + " player(s) online");
    }

    // On Puzzle Stop: blank the board's pictures (the sensors stay where they are until the next start)
    public void Blank()
    {
        if (!PuzzleSDK.IsHost || !shuffled) return;
        for (int k = 1; k <= Count; k++)
        {
            if (boards[k] == null || !boards[k].gameObject.activeInHierarchy) continue;
            PuzzleDisplay board = DisplayOn(boards[k]);
            if (board != null) board.Clear();
        }
    }

    // Finds the named objects under the puzzle's root, walking the hierarchy by hand (inactive ones too)
    void Collect()
    {
        for (int k = 0; k <= Count; k++) { sensors[k] = null; spots[k] = null; pictures[k] = null; boards[k] = null; }
        Walk(transform.root);
    }

    void Walk(Transform t)
    {
        if (t == null) return;
        string n = t.name;
        for (int k = 1; k <= Count; k++)
        {
            string number = k.ToString();
            if (n == "Pad Sensor " + number) sensors[k] = t;
            else if (n == "Pad Spot " + number) spots[k] = t;
            else if (n == "Picture " + number) pictures[k] = t;
            else if (n == "Board Picture " + number) boards[k] = t;
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

    // The moved trigger boxes are where the sensors' ClosestPoint looks before their next check. Kept apart so a
    // stripped engine method fails only this call (physics catches up at the next fixed step anyway).
    void SyncPhysics()
    {
        if (syncFailed) return;
        try { SyncNow(); }
        catch (Exception e)
        {
            syncFailed = true;
            Debug.Log("[LampShuffle] Physics.SyncTransforms unavailable (" + e.GetType().Name + "); physics syncs at the next step");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SyncNow() => Physics.SyncTransforms();
}
