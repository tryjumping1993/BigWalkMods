// Differ: "checking it twice". Two lists of pictures hang far apart; they are the same but for a few places. The
// players compare them (calling them across, or walking between) and mark the places that differ; a Validator then
// checks the marks. No clock, and a wrong check only shows the cross.
// (Santa's List: the Nice List and the Naughty List's copy of it disagree in two lines.)
//
// On an empty in the puzzle (b.AddCustomScript(empty, "Differ")). Its parts are found by name under the root:
//   listA, listB   the names of the two objects carrying a PuzzleDisplay each (both with 'places' flaps)
//   places         how many pictures in a list (the displays' flap count: 1, 2, 3 or 5)
//   differ         how many places differ
//   imageSet       the game's picture set both lists show, by number (0 to 8, as PuzzleImageSet counts them)
//   images         that set's pictures used: 1 to this (at least places + 1)
//   markPrefix     the lamp of place k is the PuzzleLight on the object named markPrefix + k: lit while place k is marked
//   validator      the name of the object carrying the PuzzleValidator
// Wire each place's Poke Button to Press(k) with k (from 1) as the event's int argument: a press marks the place, a
// second press unmarks it. Wire On Puzzle Start to Begin() and On Puzzle Stop to End(). The puzzle needs no Peg
// Tiles: the SDK's own deal at the start leaves the displays blank, and this script fills them a moment later.
//
// Only the host decides (PuzzleSDK.IsHost); displays, lamps and the validator are the SDK's own and reach every PC by
// themselves. This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent, Time and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class Differ : MonoBehaviour
{
    [SerializeField] private string listA = "List A";
    [SerializeField] private string listB = "List B";
    [SerializeField] private int places = 5;
    [SerializeField] private int differ = 2;
    [SerializeField] private int imageSet = 0;
    [SerializeField] private int images = 6;
    [SerializeField] private string markPrefix = "Mark Lamp ";
    [SerializeField] private string validator = "List Validator";

    private PuzzleSDK sdk;
    private PuzzleDisplay a, b;
    private PuzzleValidator check;
    private PuzzleLight[] lamps = new PuzzleLight[0];
    private bool[] differs = new bool[0], marked = new bool[0];
    private bool found, warned, running, pending;
    private float dealAt;

    // Wired to On Puzzle Start: the lists are filled a moment later, after the SDK's own deal has run
    public void Begin()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        pending = true;
        running = false;
        dealAt = Time.time + 0.7f;
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns; the SDK blanks the displays itself)
    public void End()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        pending = false;
        running = false;
        if (sdk.IsWon) return;
        for (int i = 0; i < places; i++) { marked[i] = false; if (lamps[i] != null) lamps[i].TurnOff(); }
    }

    // Wired to place k's Poke Button
    public void Press(int k)
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (!running || k < 1 || k > places || sdk.IsWon) return;
        marked[k - 1] = !marked[k - 1];
        if (lamps[k - 1] != null) { if (marked[k - 1]) lamps[k - 1].TurnOn(); else lamps[k - 1].TurnOff(); }
        Judge();
    }

    void Update()
    {
        if (!pending || !PuzzleSDK.IsHost || Time.time < dealAt) return;
        pending = false;
        Deal();
    }

    void Deal()
    {
        // list A: 'places' different pictures of the set
        int[] deck = new int[images];
        for (int i = 0; i < images; i++) deck[i] = i + 1;
        for (int i = images - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = deck[i]; deck[i] = deck[j]; deck[j] = t;
        }
        // the places that differ
        int[] order = new int[places];
        for (int i = 0; i < places; i++) { order[i] = i; differs[i] = false; marked[i] = false; }
        for (int i = places - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }
        for (int i = 0; i < differ && i < places; i++) differs[order[i]] = true;
        PuzzleImageSet set = (PuzzleImageSet)imageSet;
        string text = "";
        for (int i = 0; i < places; i++)
        {
            int picture = deck[i], other = picture;
            if (differs[i])
            {
                // any other picture of the set (it may be one that stands elsewhere in the list)
                other = 1 + UnityEngine.Random.Range(0, images - 1);
                if (other >= picture) other++;
            }
            a.SetImage(i + 1, set, picture);
            b.SetImage(i + 1, set, other);
            if (lamps[i] != null) lamps[i].TurnOff();
            text += (differs[i] ? "x" : ".");
        }
        running = true;
        Judge();
        Debug.Log("[Differ] " + name + ": lists dealt, they differ at " + text);
    }

    // The validator is right exactly when the marks are the places that differ
    void Judge()
    {
        bool right = true;
        for (int i = 0; i < places; i++) if (marked[i] != differs[i]) right = false;
        if (check == null) return;
        check.SetCorrect(right);
        check.TurnOn();
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Differ] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        if (places < 1) places = 1;
        if (places > 5) places = 5;
        if (images < places + 1) images = places + 1;
        if (differ < 1) differ = 1;
        if (differ > places) differ = places;
        lamps = new PuzzleLight[places];
        differs = new bool[places];
        marked = new bool[places];
        int lampsFound = 0;
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (a == null && n == listA) a = t.GetComponent<PuzzleDisplay>();
            if (b == null && n == listB) b = t.GetComponent<PuzzleDisplay>();
            if (check == null && n == validator) check = t.GetComponent<PuzzleValidator>();
            int k;
            if (markPrefix.Length > 0 && n.StartsWith(markPrefix) && int.TryParse(n.Substring(markPrefix.Length), out k) && k >= 1 && k <= places && lamps[k - 1] == null)
            {
                lamps[k - 1] = t.GetComponent<PuzzleLight>();
                if (lamps[k - 1] != null) lampsFound++;
            }
        }
        if (a == null || b == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Differ] " + name + ": list displays missing ('" + listA + "' " + (a != null) + ", '" + listB + "' " + (b != null) + ")"); }
            return false;
        }
        found = true;
        Debug.Log("[Differ] " + name + ": two lists of " + places + ", " + lampsFound + " mark lamps, validator " + (check != null ? "found" : "MISSING"));
        return true;
    }
}
