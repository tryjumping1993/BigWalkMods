// Snowball Range's board: shuffles the Caller's six board cells (a lamp and its copy display, moved together) among the
// six slots at every Begin, and begins a run only while the puzzle is switched on and nobody is in the range or on the
// door's apron. So a lamp's place on the board says nothing about which hoop it is: only the picture beside it does.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "RangeBoard")). No object-reference fields: its parts are found
// by name under the puzzle's root, which the build keeps unique:
//   "Press In Order"    the PuzzlePressInOrder that runs the six rounds
//   "Range Floor"       the Presence Sensor (players) over the whole inside of the range
//   "Door Apron"        the Presence Sensor (players) outside the range door
//   "Board Cell k"      k = 1..6: an empty holding Board Lamp k and Board Copy k, built in slot k (direct children of
//                       the root, so their local positions are the slots)
// BeginIfOn() is wired to the Ready group's On All Held (button events run on the host).
//
// Only the host decides (PuzzleSDK.IsHost): it picks the arrangement and publishes it as a SyncedString; every PC moves
// its own cells from that text, and a late joiner takes the current value. Parts are looked up lazily (on first use and
// again while any is missing), since the mod registers its PuzzleSDK in its own Awake, which may run after this one's.
//
// This runs inside the game (IL2CPP): keep to Transform, GetChild, the generic GetComponent and plain C#.
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class RangeBoard : MonoBehaviour
{
    const int Count = 6;

    // made as a field, so every PC makes it in the same order. Text: Count digits, cell k (0-based) sits in slot text[k].
    private readonly SyncedString layout = new SyncedString("snowball range board");

    private PuzzleSDK sdk;
    private PuzzlePressInOrder order;
    private PuzzlePresenceSensor floor, apron;
    private readonly Transform[] cells = new Transform[Count];
    private Vector3[] slots;          // each cell's local position as built (slot k = where cell k started)
    private bool warned, found;
    private string applied = "";

    void Awake()
    {
        layout.Changed += OnChanged;
    }

    void Start()
    {
        Resolve();
        Apply(layout.Value);          // a late joiner takes the current arrangement
    }

    void OnChanged(string text) { Apply(text); }

    // Wired to the Ready group's On All Held
    public void BeginIfOn()
    {
        if (!PuzzleSDK.IsHost) return;
        if (!Resolve()) return;                                   // fail closed: no parts, no run
        if (!sdk.IsRunning) { Debug.Log("[RangeBoard] not begun: the puzzle is switched off"); return; }
        if (floor.PlayerCount > 0 || apron.PlayerCount > 0)
        {
            Debug.Log("[RangeBoard] not begun: someone is in the range (" + floor.PlayerCount + ") or at the door (" + apron.PlayerCount + ")");
            return;
        }
        int[] p = new int[Count];
        for (int i = 0; i < Count; i++) p[i] = i;
        for (int i = Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = p[i]; p[i] = p[j]; p[j] = t;
        }
        string s = "";
        for (int i = 0; i < Count; i++) s += (char)('0' + p[i]);
        layout.Set(s);
        Apply(s);
        Debug.Log("[RangeBoard] begun, cells in slots " + s);
        order.Begin();
    }

    // Finds every part once; true when all are there. Captures the slots the first time all six cells are found.
    bool Resolve()
    {
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        Transform root = sdk != null ? sdk.transform : transform.root;
        if (order == null) { Transform t = Named(root, "Press In Order"); if (t != null) order = t.GetComponent<PuzzlePressInOrder>(); }
        if (floor == null) { Transform t = Named(root, "Range Floor"); if (t != null) floor = t.GetComponent<PuzzlePresenceSensor>(); }
        if (apron == null) { Transform t = Named(root, "Door Apron"); if (t != null) apron = t.GetComponent<PuzzlePresenceSensor>(); }
        bool allCells = true;
        for (int k = 0; k < Count; k++)
        {
            if (cells[k] == null) cells[k] = Named(root, "Board Cell " + (k + 1));
            if (cells[k] == null) allCells = false;
        }
        if (allCells && slots == null)
        {
            slots = new Vector3[Count];
            for (int k = 0; k < Count; k++) slots[k] = cells[k].localPosition;
        }
        bool ok = sdk != null && order != null && floor != null && apron != null && allCells && slots != null;
        if (ok && !found)
        {
            found = true;
            Debug.Log("[RangeBoard] parts found, slots captured");
        }
        if (!ok && !warned)
        {
            warned = true;
            Debug.LogWarning("[RangeBoard] missing parts (puzzle " + (sdk != null) + ", order " + (order != null) + ", floor " + (floor != null)
                + ", apron " + (apron != null) + ", cells " + allCells + "): the Snowball Range will not begin until they are found");
        }
        return ok;
    }

    void Apply(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != Count || s == applied) return;
        if (slots == null) Resolve();
        if (slots == null) return;
        var used = new HashSet<int>();
        for (int k = 0; k < Count; k++)
        {
            int slot = s[k] - '0';
            if (slot < 0 || slot >= Count || !used.Add(slot)) return;      // not a permutation of the slots: ignore it
        }
        for (int k = 0; k < Count; k++) cells[k].localPosition = slots[s[k] - '0'];
        applied = s;
    }

    // Depth-first search by exact name (inactive objects too)
    static Transform Named(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        int children = t.childCount;
        for (int i = 0; i < children; i++)
        {
            Transform found = Named(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
