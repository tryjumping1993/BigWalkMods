// Seek: one of a set of spots is lit at a time. When enough players have stood on the lit spot for a moment it counts,
// its lamp goes out and another spot lights. So many spots found win the puzzle. There is no clock and nothing is lost
// by stepping anywhere else on the way, which is why this is not the SDK's Press In Order: untimed, that fails on the
// first wrong sensor a walker crosses, and timed, it needs a phase clock.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "Seek")). Its parts are found by name under the puzzle's root:
//   sensorPrefix   every PuzzlePresenceSensor on an object whose name starts with this is a spot; the rest of the name
//                  is the spot's tag ("Floe 3-2" with the prefix "Floe " has the tag "3-2")
//   lampPrefix     a spot's lamp is the PuzzleLight on the object named lampPrefix + tag (a spot may have none)
//   barPrefix      every PuzzleProgressBar on an object whose name starts with this goes up one for each spot found
//                  (the build sets their totals to 'rounds'); "" for none
//   rounds         spots to find
//   dwell          seconds the lit spot must stay occupied before it counts
//   need           players who must stand on it at once
// Wire On Puzzle Start to Begin() and On Puzzle Stop to End().
//
// Only the host decides (PuzzleSDK.IsHost); lamps and bars are the SDK's own and reach every PC by themselves.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent, Time and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class Seek : MonoBehaviour
{
    [SerializeField] private string sensorPrefix = "Spot ";
    [SerializeField] private string lampPrefix = "Spot Lamp ";
    [SerializeField] private string barPrefix = "";
    [SerializeField] private int rounds = 6;
    [SerializeField] private float dwell = 1.5f;
    [SerializeField] private int need = 1;

    private PuzzleSDK sdk;
    private readonly List<PuzzlePresenceSensor> spots = new List<PuzzlePresenceSensor>();
    private readonly List<PuzzleLight> lamps = new List<PuzzleLight>();
    private readonly List<PuzzleProgressBar> bars = new List<PuzzleProgressBar>();
    private bool found, warned, running;
    private int target = -1, done;
    private float held, nextCheck;

    // Wired to On Puzzle Start
    public void Begin()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        if (sdk.IsWon) return;
        Dark();
        for (int i = 0; i < bars.Count; i++) bars[i].Reset();
        done = 0;
        running = true;
        Next();
    }

    // Wired to On Puzzle Stop (which also fires when the puzzle spawns)
    public void End()
    {
        if (!PuzzleSDK.IsHost || !Resolve()) return;
        running = false;
        target = -1;
        Dark();
        if (!sdk.IsWon) for (int i = 0; i < bars.Count; i++) bars[i].Reset();
    }

    void Update()
    {
        if (!running || !PuzzleSDK.IsHost) return;
        if (Time.time < nextCheck) return;
        float step = 0.2f;
        nextCheck = Time.time + step;
        if (target < 0 || target >= spots.Count || spots[target] == null) return;
        if (spots[target].PlayerCount >= need) held += step; else held = 0f;
        if (held < dwell) return;
        done++;
        if (lamps[target] != null) lamps[target].TurnOff();
        for (int i = 0; i < bars.Count; i++) bars[i].Increment();
        Debug.Log("[Seek] " + name + ": spot '" + spots[target].name + "' found, " + done + " of " + rounds);
        if (done >= rounds)
        {
            running = false;
            target = -1;
            sdk.Win();
            return;
        }
        Next();
    }

    // Lights another spot: never the one just found
    void Next()
    {
        held = 0f;
        if (spots.Count == 0) { target = -1; return; }
        int pick = UnityEngine.Random.Range(0, spots.Count);
        if (spots.Count > 1 && pick == target) pick = (pick + 1 + UnityEngine.Random.Range(0, spots.Count - 1)) % spots.Count;
        target = pick;
        if (lamps[target] != null) lamps[target].TurnOn();
        Debug.Log("[Seek] " + name + ": spot '" + spots[target].name + "' is lit");
    }

    void Dark()
    {
        for (int i = 0; i < lamps.Count; i++) if (lamps[i] != null) lamps[i].TurnOff();
    }

    // Finds the puzzle, the spots, their lamps and the bars once (again while the puzzle itself is missing: the mod
    // registers its PuzzleSDK in its own Awake, which may run after this script's first call)
    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null)
        {
            if (!warned) { warned = true; Debug.LogWarning("[Seek] " + name + ": no puzzle found above this object yet"); }
            return false;
        }
        List<Transform> all = BwmFind.All(sdk.transform);
        var tags = new List<string>();
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (sensorPrefix.Length > 0 && n.StartsWith(sensorPrefix))
            {
                PuzzlePresenceSensor sensor = t.GetComponent<PuzzlePresenceSensor>();
                if (sensor != null && t.gameObject.activeInHierarchy) { spots.Add(sensor); tags.Add(n.Substring(sensorPrefix.Length)); }
            }
            if (barPrefix.Length > 0 && n.StartsWith(barPrefix))
            {
                PuzzleProgressBar bar = t.GetComponent<PuzzleProgressBar>();
                if (bar != null) bars.Add(bar);
            }
        }
        int lampsFound = 0;
        for (int s = 0; s < spots.Count; s++)
        {
            PuzzleLight lamp = null;
            string wanted = lampPrefix + tags[s];
            for (int i = 0; i < all.Count && lamp == null; i++)
                if (all[i].name == wanted) lamp = all[i].GetComponent<PuzzleLight>();
            lamps.Add(lamp);
            if (lamp != null) lampsFound++;
        }
        if (rounds < 1) rounds = 1;
        if (need < 1) need = 1;
        found = true;
        Debug.Log("[Seek] " + name + ": " + spots.Count + " spots, " + lampsFound + " lamps, " + bars.Count + " bars, " + rounds + " to find");
        return true;
    }
}
