// Echo: a memory game on a set of spots, each with a lamp. The lamps play a sequence, one after another; then the
// players step on the spots in the same order. A round done adds one to the sequence; a wrong step only plays the
// same round's sequence again. So many rounds win. No clock: the players take as long as they like between steps.
// (Bell Ringers: the belfry rings a peal on its lamps and the ringers ring it back on the bell mats.)
//
// On an empty in the puzzle (b.AddCustomScript(empty, "Echo")). Its parts are found by name under the puzzle's root:
//   sensorPrefix   spot k is the PuzzlePresenceSensor on the object named sensorPrefix + k (k from 1)
//   lampPrefix     its lamp is the PuzzleLight on the object named lampPrefix + k
//   barPrefix      every PuzzleProgressBar on an object whose name starts with this counts the rounds done (the
//                  build sets their totals to the number of rounds, last - first + 1); "" for none
//   count          how many spots
//   first, last    the sequence's length in the first and in the last round
//   pairSpot       a spot that takes two players at once (0 for none); pairNeed is how many
//   showSeconds    how long each lamp of the sequence stays lit; gapSeconds between two lamps
// A step counts when a spot that was empty has held its players for a third of a second, so crossing a mat's corner
// on the way to another does not. While the sequence plays, steps are not read.
// Wire On Puzzle Start to Begin() and On Puzzle Stop to End().
//
// Only the host decides (PuzzleSDK.IsHost); lamps and bars are the SDK's own and reach every PC by themselves.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent, Time and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class Echo : MonoBehaviour
{
    [SerializeField] private string sensorPrefix = "Mat ";
    [SerializeField] private string lampPrefix = "Mat Lamp ";
    [SerializeField] private string barPrefix = "";
    [SerializeField] private int count = 6;
    [SerializeField] private int first = 3;
    [SerializeField] private int last = 6;
    [SerializeField] private int pairSpot = 0;
    [SerializeField] private int pairNeed = 2;
    [SerializeField] private float showSeconds = 0.9f;
    [SerializeField] private float gapSeconds = 0.4f;

    private PuzzleSDK sdk;
    private PuzzlePresenceSensor[] spots;
    private PuzzleLight[] lamps;
    private readonly List<PuzzleProgressBar> bars = new List<PuzzleProgressBar>();
    private bool found, warned;

    private int[] sequence = new int[0];
    private int length, at;             // this round's length; how many steps of it are done
    private int mode;                   // 0 off, 1 waiting to play, 2 a lamp is lit, 3 the gap after a lamp, 4 the players' turn
    private int showing;                // which step of the sequence is being shown
    private float until;                // when the current wait, lamp or gap ends
    private float[] held;               // how long each spot has been occupied
    private bool[] counted;             // the spot's present occupation has been read as a step
    private int flash = -1;             // the lamp lit for a moment after a right step
    private float flashUntil;
    private float lastTick;

    // Wired to On Puzzle Start
    public void Begin()
    {
        if (!PuzzleSDK.IsHost || !Resolve() || sdk.IsWon) return;
        for (int i = 0; i < bars.Count; i++) bars[i].Reset();
        length = first;
        Deal(1.5f);
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns)
    public void End()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        mode = 0;
        Dark();
        if (!sdk.IsWon) for (int i = 0; i < bars.Count; i++) bars[i].Reset();
    }

    // A new sequence of the round's length (never the same spot twice running), played after 'wait' seconds
    void Deal(float wait)
    {
        sequence = new int[length];
        int before = -1;
        for (int i = 0; i < length; i++)
        {
            int pick = UnityEngine.Random.Range(0, count);
            if (count > 1 && pick == before) pick = (pick + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
            sequence[i] = pick;
            before = pick;
        }
        Replay(wait);
        string text = "";
        for (int i = 0; i < length; i++) text += (i > 0 ? " " : "") + (sequence[i] + 1);
        Debug.Log("[Echo] " + name + ": round of " + length + ": " + text);
    }

    void Replay(float wait)
    {
        Dark();
        at = 0;
        showing = 0;
        mode = 1;
        until = Time.time + wait;
    }

    void Update()
    {
        if (mode == 0 || !PuzzleSDK.IsHost) return;
        float now = Time.time, dt = now - lastTick;
        lastTick = now;
        if (dt > 0.5f) dt = 0.5f;
        if (flash >= 0 && now >= flashUntil)
        {
            if (lamps[flash] != null) lamps[flash].TurnOff();
            flash = -1;
        }
        if (mode == 1)
        {
            if (now < until) return;
            Light(sequence[showing]);
            mode = 2;
            until = now + showSeconds;
        }
        else if (mode == 2)
        {
            if (now < until) return;
            if (lamps[sequence[showing]] != null) lamps[sequence[showing]].TurnOff();
            mode = 3;
            until = now + gapSeconds;
        }
        else if (mode == 3)
        {
            if (now < until) return;
            showing++;
            if (showing < length) { Light(sequence[showing]); mode = 2; until = now + showSeconds; return; }
            // the players' turn: whoever stands on a spot now has to step off and on again
            for (int i = 0; i < count; i++) { held[i] = 0f; counted[i] = Occupied(i); }
            mode = 4;
        }
        else if (mode == 4)
        {
            for (int i = 0; i < count; i++)
            {
                if (!Occupied(i)) { held[i] = 0f; counted[i] = false; continue; }
                if (counted[i]) continue;
                held[i] += dt;
                if (held[i] < 0.33f) continue;
                counted[i] = true;
                Step(i);
                if (mode != 4) return;
            }
        }
    }

    void Step(int spot)
    {
        if (spot != sequence[at])
        {
            Debug.Log("[Echo] " + name + ": spot " + (spot + 1) + " stepped, wanted " + (sequence[at] + 1) + ": the round plays again");
            Replay(2.0f);
            return;
        }
        at++;
        if (flash >= 0 && lamps[flash] != null) lamps[flash].TurnOff();
        Light(spot);
        flash = spot;
        flashUntil = Time.time + 0.45f;
        if (at < length) return;
        for (int i = 0; i < bars.Count; i++) bars[i].Increment();
        Debug.Log("[Echo] " + name + ": round of " + length + " done");
        if (length >= last) { mode = 0; sdk.Win(); return; }
        length++;
        Deal(2.0f);
    }

    bool Occupied(int i)
    {
        if (spots[i] == null) return false;
        int need = (i + 1 == pairSpot) ? pairNeed : 1;
        return spots[i].PlayerCount >= need;
    }

    void Light(int i) { if (lamps[i] != null) lamps[i].TurnOn(); }

    void Dark()
    {
        flash = -1;
        for (int i = 0; i < count; i++) if (lamps[i] != null) lamps[i].TurnOff();
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Echo] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        if (count < 2) count = 2;
        if (count > 16) count = 16;
        if (first < 1) first = 1;
        if (last < first) last = first;
        spots = new PuzzlePresenceSensor[count];
        lamps = new PuzzleLight[count];
        held = new float[count];
        counted = new bool[count];
        int spotsFound = 0, lampsFound = 0;
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            int k;
            // the lamp's prefix may begin with the sensor's ("Mat " and "Mat Lamp "): a name that does not end in a
            // number after the prefix is simply not that kind
            if (lampPrefix.Length > 0 && n.StartsWith(lampPrefix) && int.TryParse(n.Substring(lampPrefix.Length), out k) && k >= 1 && k <= count && lamps[k - 1] == null)
            {
                lamps[k - 1] = t.GetComponent<PuzzleLight>();
                if (lamps[k - 1] != null) lampsFound++;
            }
            else if (sensorPrefix.Length > 0 && n.StartsWith(sensorPrefix) && int.TryParse(n.Substring(sensorPrefix.Length), out k) && k >= 1 && k <= count && spots[k - 1] == null)
            {
                spots[k - 1] = t.GetComponent<PuzzlePresenceSensor>();
                if (spots[k - 1] != null) spotsFound++;
            }
            if (barPrefix.Length > 0 && n.StartsWith(barPrefix))
            {
                PuzzleProgressBar bar = t.GetComponent<PuzzleProgressBar>();
                if (bar != null) bars.Add(bar);
            }
        }
        found = true;
        Debug.Log("[Echo] " + name + ": " + spotsFound + " of " + count + " spots, " + lampsFound + " lamps, " + bars.Count + " bars, rounds of " + first + " to " + last);
        return true;
    }
}
