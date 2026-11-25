// CountDeal: lights some of a set of lamps at random and tells a Count Validator how many, for a counting puzzle
// (Reindeer Count: so many noses glow, each window shows only part of the shed, count them all). No clock.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "CountDeal")). Its parts are found by name under the root:
//   lampPrefix   every PuzzleLight on an object whose name starts with this is one of the lamps
//   validator    the name of the object carrying the PuzzleCountValidator (the answer is set on it at every deal)
//   least, most  how many lamps a deal lights, at least and at most
// Wire On Puzzle Start to Deal(), On Puzzle Stop to Dark(), and the validator's On Incorrect to Redeal(): a wrong
// count deals again (the validator counts up to the right number as it checks, so the same lamps could simply be
// entered again). Redeal() waits redealSeconds first: a new answer blanks the validator's screen at once, and
// dealt in the same frame the cross was never seen.
//
// Only the host decides (PuzzleSDK.IsHost); the lamps and the validator are the SDK's own and reach every PC by
// themselves. This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class CountDeal : MonoBehaviour
{
    [SerializeField] private string lampPrefix = "Nose ";
    [SerializeField] private string validator = "Count Validator";
    [SerializeField] private int least = 6;
    [SerializeField] private int most = 12;
    [SerializeField] private float redealSeconds = 3f;

    private PuzzleSDK sdk;
    private readonly List<PuzzleLight> lamps = new List<PuzzleLight>();
    private PuzzleCountValidator check;
    private bool found, warned;
    private float redealAt;             // when the deal after a wrong count is due; 0 = none

    // Wired to the validator's On Incorrect: a new deal a few seconds later, once the cross has been seen
    public void Redeal()
    {
        if (!PuzzleSDK.IsHost) return;
        redealAt = Time.time + redealSeconds;
    }

    void Update()
    {
        if (redealAt <= 0f || !PuzzleSDK.IsHost || Time.time < redealAt) return;
        redealAt = 0f;
        if (Resolve() && sdk.IsRunning) Deal();
    }

    // Wired to On Puzzle Start
    public void Deal()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        int n = lamps.Count;
        int lo = Mathf.Clamp(least, 0, n), hi = Mathf.Clamp(most, lo, n);
        int want = UnityEngine.Random.Range(lo, hi + 1);
        // a random order of the lamps; the first 'want' are lit
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }
        for (int i = 0; i < n; i++)
        {
            PuzzleLight lamp = lamps[order[i]];
            if (lamp == null) continue;
            if (i < want) lamp.TurnOn(); else lamp.TurnOff();
        }
        if (check != null) check.SetAnswer(want);
        Debug.Log("[CountDeal] " + name + ": " + want + " of " + n + " lit");
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns): every lamp dark
    public void Dark()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        redealAt = 0f;
        for (int i = 0; i < lamps.Count; i++) if (lamps[i] != null) lamps[i].TurnOff();
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[CountDeal] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (lampPrefix.Length > 0 && n.StartsWith(lampPrefix))
            {
                PuzzleLight lamp = t.GetComponent<PuzzleLight>();
                if (lamp != null) lamps.Add(lamp);
            }
            if (check == null && n == validator) check = t.GetComponent<PuzzleCountValidator>();
        }
        found = true;
        Debug.Log("[CountDeal] " + name + ": " + lamps.Count + " lamps, validator " + (check != null ? "found" : "MISSING"));
        return true;
    }
}
