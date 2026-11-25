// LightsOut: a ring of lamps, each with a button. A button flips its own lamp and the lamp on either side of it.
// Every lamp lit wins. A logic puzzle one player can do, with no clock and nothing to lose.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "LightsOut")). Its parts are found by name under the root:
//   count        how many lamps stand in the ring (not a multiple of 3: then every deal can be solved, one way only)
//   lampPrefix   lamp k is the PuzzleLight on the object named lampPrefix + k (k from 1, going round the ring)
//   barPrefix    every PuzzleProgressBar on an object whose name starts with this shows how many lamps are lit
//                (the build sets their totals to count); "" for none
// Wire each button's On Pressed to Press(k) with k as the event's int argument, On Puzzle Start to Begin() and On
// Puzzle Stop to End(). Begin deals a new pattern (some lamps lit, never all).
//
// Only the host decides (PuzzleSDK.IsHost); lamps and bars are the SDK's own and reach every PC by themselves.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class LightsOut : MonoBehaviour
{
    [SerializeField] private int count = 7;
    [SerializeField] private string lampPrefix = "Ring Lamp ";
    [SerializeField] private string barPrefix = "";

    private PuzzleSDK sdk;
    private PuzzleLight[] lamps;
    private bool[] lit;
    private readonly List<PuzzleProgressBar> bars = new List<PuzzleProgressBar>();
    private bool found, warned, running;

    // Wired to On Puzzle Start: a new pattern, made by pressing buttons at random from "all lit", so it can be solved
    public void Begin()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        int tries = 0;
        do
        {
            for (int i = 0; i < count; i++) lit[i] = true;
            for (int i = 0; i < count; i++) if (UnityEngine.Random.Range(0, 2) == 1) Flip(i);
            tries++;
        } while (Lit() > count - 3 && tries < 50);      // at least three lamps dark to begin with
        running = true;
        Show();
        Debug.Log("[LightsOut] " + name + ": dealt, " + Lit() + " of " + count + " lit " + Pattern());
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns): every lamp dark
    public void End()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        running = false;
        if (sdk.IsWon) return;
        for (int i = 0; i < count; i++) { lit[i] = false; if (lamps[i] != null) lamps[i].TurnOff(); }
        for (int i = 0; i < bars.Count; i++) bars[i].Reset();
    }

    // Wired to button k's On Pressed, with k (from 1) as the argument
    public void Press(int k)
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (!running || k < 1 || k > count || sdk.IsWon || !sdk.IsRunning) return;
        Flip(k - 1);
        Show();
        int n = Lit();
        Debug.Log("[LightsOut] " + name + ": button " + k + ", " + n + " of " + count + " lit " + Pattern());
        if (n == count) { running = false; sdk.Win(); }
    }

    // The ring as text for the log: 'o' a lit lamp, '.' a dark one, lamp 1 first
    string Pattern()
    {
        string text = "";
        for (int i = 0; i < count; i++) text += lit[i] ? "o" : ".";
        return text;
    }

    void Flip(int i)
    {
        lit[(i + count - 1) % count] = !lit[(i + count - 1) % count];
        lit[i] = !lit[i];
        lit[(i + 1) % count] = !lit[(i + 1) % count];
    }

    int Lit()
    {
        int n = 0;
        for (int i = 0; i < count; i++) if (lit[i]) n++;
        return n;
    }

    void Show()
    {
        for (int i = 0; i < count; i++)
        {
            if (lamps[i] == null) continue;
            if (lit[i]) lamps[i].TurnOn(); else lamps[i].TurnOff();
        }
        int n = Lit();
        for (int i = 0; i < bars.Count; i++) bars[i].SetProgress(n);
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[LightsOut] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        if (count < 3) count = 3;
        if (count > 32) count = 32;
        lamps = new PuzzleLight[count];
        lit = new bool[count];
        int lampsFound = 0;
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (lampPrefix.Length > 0 && n.StartsWith(lampPrefix))
            {
                int k;
                if (int.TryParse(n.Substring(lampPrefix.Length), out k) && k >= 1 && k <= count && lamps[k - 1] == null)
                {
                    lamps[k - 1] = t.GetComponent<PuzzleLight>();
                    if (lamps[k - 1] != null) lampsFound++;
                }
            }
            if (barPrefix.Length > 0 && n.StartsWith(barPrefix))
            {
                PuzzleProgressBar bar = t.GetComponent<PuzzleProgressBar>();
                if (bar != null) bars.Add(bar);
            }
        }
        found = true;
        Debug.Log("[LightsOut] " + name + ": " + count + " lamps in the ring (" + lampsFound + " found), " + bars.Count + " bars");
        return true;
    }
}
