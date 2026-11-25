// HeldOpen: a Door that stands open while somebody stands on any one of its plates, and shuts when all are empty.
// The SDK's events alone cannot do "this plate or that one": a plate's On Released would shut a gate another plate
// still holds. (Whiteout Maze's gates, each held by one or two snow plates.)
//
// On an empty in the puzzle, one per gate (b.AddCustomScript(empty, "HeldOpen"), fields set with Sdk.Set):
//   door     the name of the object carrying the PuzzleDoor (its mode Control With Functions Only, so it starts shut)
//   plates   the names of the Presence Sensors that hold it open, separated by ';' ("Plate A;Plate B")
// Nothing needs wiring. The plates are read five times a second.
//
// Only the host decides (PuzzleSDK.IsHost); the door is the SDK's own and moves on every PC by itself.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent, Time and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class HeldOpen : MonoBehaviour
{
    [SerializeField] private string door = "Gate 1";
    [SerializeField] private string plates = "Plate A";

    private PuzzleSDK sdk;
    private PuzzleDoor gate;
    private readonly List<PuzzlePresenceSensor> sensors = new List<PuzzlePresenceSensor>();
    private bool found, warned, open;
    private float nextCheck;

    void Update()
    {
        if (!PuzzleSDK.IsHost || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        if (!Resolve()) return;
        bool held = false;
        for (int i = 0; i < sensors.Count; i++)
            if (sensors[i] != null && sensors[i].PlayerCount > 0) { held = true; break; }
        if (held == open) return;
        open = held;
        if (open) gate.Open(); else gate.Close();
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null) return false;
        string[] names = plates.Split(';');
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (gate == null && n == door) gate = t.GetComponent<PuzzleDoor>();
            for (int k = 0; k < names.Length; k++)
                if (n == names[k].Trim())
                {
                    PuzzlePresenceSensor sensor = t.GetComponent<PuzzlePresenceSensor>();
                    if (sensor != null) sensors.Add(sensor);
                }
        }
        if (gate == null || sensors.Count == 0)
        {
            if (!warned) { warned = true; Debug.LogWarning("[HeldOpen] " + name + ": door '" + door + "' " + (gate != null ? "found" : "MISSING") + ", " + sensors.Count + " of " + names.Length + " plates"); }
            return false;
        }
        found = true;
        Debug.Log("[HeldOpen] " + name + ": door '" + door + "' held by " + sensors.Count + " plate(s)");
        return true;
    }
}
