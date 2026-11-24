// Tally: a set of targets that each count once, in any order (a hoop hit, a window flown through, a plot filled).
// When every target is marked the puzzle is won. For puzzles with no clock and nothing to lose: a miss costs nothing.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "Tally")). Its parts are found by name under the puzzle's root:
//   count        how many targets there are (1..32)
//   lampPrefix   target k's lamp is the object named lampPrefix + k (k from 1), a PuzzleLight; "" for no lamps
//   barPrefix    every PuzzleProgressBar on an object whose name starts with this goes up one with each new target
//                (the build sets their totals to count); "" for none
// Wire each target's event to Hit(k) with k as the event's argument, and for targets that can be undone (things set
// down and picked up again) the opposite event to Unhit(k). Wire On Puzzle Stop to Clear() (it also fires when the
// puzzle spawns; the SDK's checks reject a start and a stop wired to the same call, and a stop always comes first).
//
// Only the host decides (PuzzleSDK.IsHost); lamps and bars are the SDK's own and reach every PC by themselves.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class Tally : MonoBehaviour
{
    [SerializeField] private int count = 6;
    [SerializeField] private string lampPrefix = "";
    [SerializeField] private string barPrefix = "";

    private PuzzleSDK sdk;
    private bool[] done;
    private PuzzleLight[] lamps;
    private readonly List<PuzzleProgressBar> bars = new List<PuzzleProgressBar>();
    private bool found, warned;

    // Wired to a target's event, with the target's number (from 1) as the argument
    public void Hit(int k)
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (k < 1 || k > count || done[k - 1]) return;
        if (!sdk.IsRunning || sdk.IsWon) return;
        done[k - 1] = true;
        if (lamps[k - 1] != null) lamps[k - 1].TurnOn();
        for (int i = 0; i < bars.Count; i++) bars[i].Increment();
        int marked = 0;
        for (int i = 0; i < count; i++) if (done[i]) marked++;
        Debug.Log("[Tally] " + name + ": target " + k + " marked, " + marked + " of " + count);
        if (marked == count) sdk.Win();
    }

    // For targets that can be undone: the mark comes off again (nothing happens once the puzzle is won)
    public void Unhit(int k)
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (k < 1 || k > count || !done[k - 1] || sdk.IsWon) return;
        done[k - 1] = false;
        if (lamps[k - 1] != null) lamps[k - 1].TurnOff();
        for (int i = 0; i < bars.Count; i++) bars[i].Decrement();
        Debug.Log("[Tally] " + name + ": target " + k + " unmarked");
    }

    // Wired to On Puzzle Stop: every mark off, lamps dark, bars empty
    public void Clear()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (sdk.IsWon) return;
        for (int i = 0; i < count; i++)
        {
            done[i] = false;
            if (lamps[i] != null) lamps[i].TurnOff();
        }
        for (int i = 0; i < bars.Count; i++) bars[i].Reset();
    }

    // Finds the puzzle, the lamps and the bars once (again while the puzzle itself is missing: the mod registers its
    // PuzzleSDK in its own Awake, which may run after this script's first call)
    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Tally] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        if (count < 1) count = 1;
        if (count > 32) count = 32;
        done = new bool[count];
        lamps = new PuzzleLight[count];
        int lampsFound = 0;
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (lampPrefix.Length > 0 && n.StartsWith(lampPrefix))
            {
                int k;
                if (int.TryParse(n.Substring(lampPrefix.Length), out k) && k >= 1 && k <= count)
                {
                    PuzzleLight light = t.GetComponent<PuzzleLight>();
                    if (light != null && lamps[k - 1] == null) { lamps[k - 1] = light; lampsFound++; }
                }
            }
            if (barPrefix.Length > 0 && n.StartsWith(barPrefix))
            {
                PuzzleProgressBar bar = t.GetComponent<PuzzleProgressBar>();
                if (bar != null) bars.Add(bar);
            }
        }
        found = true;
        Debug.Log("[Tally] " + name + ": " + count + " targets, " + lampsFound + " lamps, " + bars.Count + " bars");
        return true;
    }
}
