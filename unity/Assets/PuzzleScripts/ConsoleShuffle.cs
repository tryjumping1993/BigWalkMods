// Midnight Finale's console shuffle (on the pack's "Console Shuffle" object, a direct child of the puzzle's root). The
// Firing Room's console is made of units, each an empty named "Console Unit <k>" holding a conductor lamp and the
// split-flap display beside it. At every start of the puzzle (MidnightFinalePuzzle wires Shuffle() to On Puzzle Start)
// the host moves every unit shown in this lobby to a random one of the console's slots, so a console position learned
// in one start ("top middle") means nothing in the next; only the scribble on the display links a lamp to a button.
//
// The slots are the places every unit was built at, shown or hidden (nine; a lobby of three shows six units, so three
// slots stand empty, a different three each start). They are recorded the first time they are needed, in the root's
// own frame, so the loader moving the puzzle after it was made does not matter. The units are found by name under the
// root, hidden ones included, and sorted by name, so every PC numbers them alike.
//
// The order is a synced string set by the host only ("3,0,8,1,4,2": the slot for each shown unit, in name order); every
// PC moves its own units from it, and a player who joins later gets the latest. Text that does not fit (another
// count, a slot out of range or used twice) is ignored. A restart after a misfire (Begin() again) keeps the order: only
// a new start re-shuffles, as only a new start re-deals the tiles.
//
// This runs inside the game (IL2CPP): plain C#, Transform, GetChild and GetComponent only (BwmFind).
using System;
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class ConsoleShuffle : MonoBehaviour
{
    // What a unit's name starts with
    [SerializeField] private string unitPrefix = "Console Unit ";

    // made as a field, so every PC makes it in the same order
    private readonly SyncedString order = new SyncedString("console order");

    private readonly List<Transform> units = new List<Transform>();     // every unit, hidden or shown, sorted by name
    private readonly List<Vector3> slots = new List<Vector3>();         // where each was built, in the root's frame
    private Transform frame;
    private bool prepared;
    private string applied = "";

    void Awake()
    {
        order.Changed += Apply;
    }

    // The frame the slots are kept in: the puzzle's root (this object's parent)
    Transform Frame()
    {
        if (frame == null) frame = transform.parent != null ? transform.parent : transform;
        return frame;
    }

    void Prepare()
    {
        if (prepared) return;
        prepared = true;
        Transform root = Frame();
        List<Transform> found = BwmFind.All(root);
        for (int i = 0; i < found.Count; i++)
        {
            Transform t = found[i];
            if (t != null && t.name != null && t.name.StartsWith(unitPrefix, StringComparison.Ordinal)) units.Add(t);
        }
        units.Sort((a, c) => string.CompareOrdinal(a.name, c.name));
        for (int i = 0; i < units.Count; i++) slots.Add(root.InverseTransformPoint(units[i].position));
        Debug.Log("[ConsoleShuffle] " + name + ": " + units.Count + " console units");
    }

    // The units shown in this lobby, in name order
    List<Transform> Shown()
    {
        var shown = new List<Transform>();
        for (int i = 0; i < units.Count; i++) if (units[i] != null && units[i].gameObject.activeInHierarchy) shown.Add(units[i]);
        return shown;
    }

    // Wired from On Puzzle Start: the host picks a new place for every shown unit and tells everyone
    public void Shuffle()
    {
        if (!PuzzleSDK.IsHost) return;
        Prepare();
        List<Transform> shown = Shown();
        if (shown.Count == 0 || slots.Count < shown.Count) return;
        var pick = new List<int>();
        for (int i = 0; i < slots.Count; i++) pick.Add(i);
        for (int i = pick.Count - 1; i > 0; i--)       // Fisher-Yates
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int keep = pick[i]; pick[i] = pick[j]; pick[j] = keep;
        }
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(pick[i]);
        }
        string value = text.ToString();
        order.Set(value);
        Apply(value);       // the host applies its own value (Set raises nothing when the text is unchanged)
    }

    void Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Prepare();
        List<Transform> shown = Shown();
        string[] parts = text.Split(',');
        if (parts.Length != shown.Count) return;
        var perm = new int[parts.Length];
        var used = new HashSet<int>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int slot) || slot < 0 || slot >= slots.Count || !used.Add(slot)) return;
            perm[i] = slot;
        }
        Transform root = Frame();
        for (int i = 0; i < shown.Count; i++) shown[i].position = root.TransformPoint(slots[perm[i]]);
        if (text != applied) Debug.Log("[ConsoleShuffle] " + name + ": units moved to slots " + text);
        applied = text;
    }
}
