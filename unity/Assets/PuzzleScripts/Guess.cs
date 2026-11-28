// Guess: a deduction game on a Peg Board. A hidden answer says which tile belongs in each slot. Whenever every slot
// holds a tile, a row of lamps shows how many slots are right, never which. All right wins. No clock, and a wrong
// guess costs nothing: change a tile or two and read the lamps again.
// (Stocking Sort: which present goes in which stocking; the mantel's lamps count the stockings that are right.)
//
// On an empty in the puzzle (b.AddCustomScript(empty, "Guess")). Its parts are found by name under the root:
//   board        the name of the object carrying the PuzzlePegBoard
//   lampPrefix   lamp k is the PuzzleLight on the object named lampPrefix + k (k from 1); with n slots right the
//                first n lamps are lit
//   images       the tiles' picture numbers run from 1 to this; the answer is one of them for each slot, no picture
//                twice (so there must be at least as many pictures as slots)
// The board's Answer From stays empty: this script holds the answer, and wins the puzzle itself.
// Wire On Puzzle Start to Begin() (a new answer) and On Puzzle Stop to End().
//
// Only the host decides (PuzzleSDK.IsHost); the lamps are the SDK's own and reach every PC by themselves. The board
// is read twice a second. This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class Guess : MonoBehaviour
{
    [SerializeField] private string board = "Guess Board";
    [SerializeField] private string lampPrefix = "Right Lamp ";
    [SerializeField] private int images = 8;

    private PuzzleSDK sdk;
    private PuzzlePegBoard pegs;
    private PuzzleLight[] lamps = new PuzzleLight[0];
    private int[] answer = new int[0];
    private bool found, warned, running;
    private float nextCheck;
    private int shown = -1;             // how many lamps are lit now (-1: not yet set)
    private string lastGuess = "";

    // Wired to On Puzzle Start: a new hidden answer
    public void Begin()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        int slots = pegs.SlotCount;
        if (images < slots) images = slots;
        int[] deck = new int[images];
        for (int i = 0; i < images; i++) deck[i] = i + 1;
        for (int i = images - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = deck[i]; deck[i] = deck[j]; deck[j] = t;
        }
        answer = new int[slots];
        for (int i = 0; i < slots; i++) answer[i] = deck[i];
        running = true;
        lastGuess = "";
        Show(0);
        Debug.Log("[Guess] " + name + ": a new answer for " + slots + " slots from " + images + " pictures");
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns)
    public void End()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        running = false;
        if (!sdk.IsWon) Show(0);
    }

    void Update()
    {
        if (!running || !PuzzleSDK.IsHost) return;
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (pegs == null || answer.Length == 0) return;
        int slots = answer.Length, right = 0;
        bool full = true;
        string guess = "";
        for (int i = 0; i < slots; i++)
        {
            PegSlot slot = pegs.GetSlot(i);
            if (slot.Type != PuzzlePegType.Image) { full = false; guess += "-"; continue; }
            guess += slot.Image + " ";
            if (slot.Image == answer[i]) right++;
        }
        if (!full) { Show(0); lastGuess = ""; return; }
        Show(right);
        if (guess != lastGuess)
        {
            lastGuess = guess;
            Debug.Log("[Guess] " + name + ": " + right + " of " + slots + " right");
        }
        if (right == slots) { running = false; sdk.Win(); }
    }

    void Show(int n)
    {
        if (n == shown) return;
        shown = n;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null) continue;
            if (i < n) lamps[i].TurnOn(); else lamps[i].TurnOff();
        }
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Guess] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        List<Transform> all = BwmFind.All(sdk.transform);
        var numbered = new Dictionary<int, PuzzleLight>();
        int most = 0;
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (pegs == null && n == board) pegs = t.GetComponent<PuzzlePegBoard>();
            int k;
            if (lampPrefix.Length > 0 && n.StartsWith(lampPrefix) && int.TryParse(n.Substring(lampPrefix.Length), out k) && k >= 1 && k <= 32)
            {
                PuzzleLight lamp = t.GetComponent<PuzzleLight>();
                if (lamp != null && !numbered.ContainsKey(k)) { numbered[k] = lamp; if (k > most) most = k; }
            }
        }
        lamps = new PuzzleLight[most];
        foreach (KeyValuePair<int, PuzzleLight> pair in numbered) lamps[pair.Key - 1] = pair.Value;
        if (pegs == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Guess] " + name + ": no Peg Board named '" + board + "'"); }
            return false;
        }
        found = true;
        Debug.Log("[Guess] " + name + ": board found, " + most + " lamps");
        return true;
    }
}
