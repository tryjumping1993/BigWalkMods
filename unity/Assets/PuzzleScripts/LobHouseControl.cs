// Summit Shot's Lob House: two small jobs the SDK's parts cannot do (spec: puzzle-specs/SummitShot.md, 4f).
//
// 1. Shuffle(panel): the lamp panels in the callers' huts. Each panel is an empty whose children are "pairs" (an empty
//    holding one lamp and, under it, a split-flap key that copies one basket's shape). Wired from each Press In Order's
//    On Begin with the panel's name, so every run (start, retry after a wrong basket, Again) deals the pairs out among
//    the panel's slots afresh: a slot means nothing from one run to the next, only the shape beside the lamp does. The
//    host picks the order and publishes it in one synced string ("West Panel 3=2,0,3,1;North Panel=1,2,0"); every PC
//    moves the pairs along the panel's local x from it (late joiners too: Changed runs for them).
// 2. The guard: nobody may stand in the glass gallery or on the lob wall's cap while the puzzle runs. The two guard
//    Presence Sensors stop the puzzle on entry by themselves; this catches the one case they miss (someone stays inside
//    and a friend flips the switch back on: the sensor is still held, so On Pressed never fires again).
//
// On an empty in the puzzle (b.AddCustomScript(empty, "LobHouseControl")); it finds everything by name under the
// puzzle's root, inactive objects included, and has no object-reference fields. Only the host decides; the synced
// string is made as a field, so every PC makes it in the same order.
//
// This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent and plain C#.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BigPuzzleSDK;
using UnityEngine;

public class LobHouseControl : MonoBehaviour
{
    // made as a field (synced variables can only be made while the pack is being built)
    private readonly SyncedString panels = new SyncedString("lobhouse panels");
    private readonly Dictionary<string, string> orders = new Dictionary<string, string>();

    // metres between neighbouring slots along a panel's local x
    [SerializeField] private float spacing = 0.6f;
    // names of the guard sensors (Player type) under the puzzle's root
    [SerializeField] private string galleryGuardName = "Gallery Guard";
    [SerializeField] private string wallGuardName = "Wall Guard";
    // the Press In Orders and their run clocks (comma-separated names), ended and reset if a run begins while the puzzle is off
    [SerializeField] private string pressInOrderNames = "PIO 2,PIO 3,PIO 4";
    [SerializeField] private string runClockNames = "Run Clock 2,Run Clock 3,Run Clock 4";

    private PuzzlePresenceSensor galleryGuard, wallGuard;
    private List<PuzzlePressInOrder> runs;
    private List<PuzzleTimer> clocks;
    private PuzzleSDK puzzle;
    private float next, nextLookup, nextRunLookup;
    private bool warnedGuards;

    void Awake()
    {
        panels.Changed += Apply;
    }

    void Start()
    {
        string now = panels.Value;
        if (!string.IsNullOrEmpty(now)) Apply(now);
    }

    // Host: a new order for one panel (wired from a Press In Order's On Begin with the panel's name)
    public void Shuffle(string panel)
    {
        if (!PuzzleSDK.IsHost) return;
        Transform t = Find(panel);
        if (t == null)
        {
            Debug.LogWarning("[LobHouseControl] " + name + ": no panel named '" + panel + "'");
            return;
        }
        int n = t.childCount;
        if (n == 0) return;
        int[] p = new int[n];
        for (int i = 0; i < n; i++) p[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int swap = p[i]; p[i] = p[j]; p[j] = swap;
        }
        string order = "";
        for (int i = 0; i < n; i++) order += (i > 0 ? "," : "") + p[i];
        orders[panel] = order;
        string text = "";
        foreach (KeyValuePair<string, string> kv in orders) text += kv.Key + "=" + kv.Value + ";";
        panels.Set(text);      // raises Changed on the host too, unless the text is unchanged
        Apply(text);           // so apply it here as well (an unchanged text is already in place, and this is idempotent)
        Debug.Log("[LobHouseControl] " + name + ": " + panel + " = " + order);
    }

    // Every PC: move each listed panel's pairs to their slots
    void Apply(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        string[] entries = s.Split(';');
        for (int e = 0; e < entries.Length; e++)
        {
            string entry = entries[e];
            int eq = entry.IndexOf('=');
            if (eq <= 0) continue;
            Transform t = Find(entry.Substring(0, eq));
            if (t == null) continue;
            string[] parts = entry.Substring(eq + 1).Split(',');
            int n = t.childCount;
            if (parts.Length != n) continue;
            for (int k = 0; k < n; k++)
            {
                int slot;
                if (!int.TryParse(parts[k], out slot) || slot < 0 || slot >= n) break;
                Transform pair = t.GetChild(k);
                Vector3 at = pair.localPosition;
                pair.localPosition = new Vector3((slot - (n - 1) / 2f) * spacing, at.y, 0f);
            }
        }
    }

    // Host: keep the puzzle stopped while anyone is in the gallery or on the cap
    void Update()
    {
        if (!PuzzleSDK.IsHost) return;
        float now = Time.time;
        if (now < next) return;
        next = now + 0.25f;
        if ((puzzle == null || galleryGuard == null || wallGuard == null) && now >= nextLookup)
        {
            nextLookup = now + 5f;
            if (puzzle == null) puzzle = PuzzleSDK.Of(transform);
            if (galleryGuard == null) galleryGuard = SensorOn(Find(galleryGuardName));
            if (wallGuard == null) wallGuard = SensorOn(Find(wallGuardName));
            if ((galleryGuard == null || wallGuard == null) && !warnedGuards)
            {
                warnedGuards = true;
                Debug.LogWarning("[LobHouseControl] " + name + ": guard sensors not found (gallery " + (galleryGuard != null) + ", wall " + (wallGuard != null) + ")");
            }
        }
        if (puzzle == null) return;
        if (!puzzle.IsRunning)
        {
            KeepRunsStopped();
            return;
        }
        bool inside = (galleryGuard != null && galleryGuard.PlayerCount > 0) || (wallGuard != null && wallGuard.PlayerCount > 0);
        if (inside)
        {
            puzzle.StopPuzzle();
            Debug.Log("[LobHouseControl] " + name + ": someone is in the gallery or on the wall; puzzle stopped");
        }
    }

    // Host, puzzle off: no Press In Order may be running. (A Poke Button pressed just before a stop re-arms itself after
    // its Auto Enable Seconds, after the stop greyed it out, and its press would Begin a run with the puzzle off.)
    void KeepRunsStopped()
    {
        if (runs == null && Time.time >= nextRunLookup)
        {
            nextRunLookup = Time.time + 5f;
            runs = new List<PuzzlePressInOrder>();
            clocks = new List<PuzzleTimer>();
            foreach (string n in Split(pressInOrderNames)) { PuzzlePressInOrder p = PioOn(Find(n)); if (p != null) runs.Add(p); }
            foreach (string n in Split(runClockNames)) { PuzzleTimer c = TimerOn(Find(n)); if (c != null) clocks.Add(c); }
            if (runs.Count == 0) runs = null;
        }
        if (runs == null) return;
        bool any = false;
        for (int i = 0; i < runs.Count; i++)
            if (runs[i] != null && runs[i].IsRunning) { runs[i].End(); any = true; }
        if (!any) return;
        for (int i = 0; i < clocks.Count; i++) if (clocks[i] != null) clocks[i].ResetTimer();
        Debug.Log("[LobHouseControl] " + name + ": a run began while the puzzle was off; ended it");
    }

    static string[] Split(string list) => string.IsNullOrEmpty(list) ? new string[0] : list.Split(',');

    Transform Find(string n) => string.IsNullOrEmpty(n) ? null : Search(transform.root, n);

    static Transform Search(Transform t, string n)
    {
        if (t == null) return null;
        if (t.name == n) return t;
        int children = t.childCount;
        for (int i = 0; i < children; i++)
        {
            Transform r = Search(t.GetChild(i), n);
            if (r != null) return r;
        }
        return null;
    }

    static PuzzlePresenceSensor SensorOn(Transform t)
    {
        if (t == null) return null;
        PuzzlePresenceSensor s = t.GetComponent<PuzzlePresenceSensor>();
        if (s != null) return s;
        try { return Deeper(t); } catch (Exception) { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static PuzzlePresenceSensor Deeper(Transform t) => BwmFind.First<PuzzlePresenceSensor>(t);

    static PuzzlePressInOrder PioOn(Transform t)
    {
        if (t == null) return null;
        try { return t.GetComponent<PuzzlePressInOrder>(); } catch (Exception) { return null; }
    }

    static PuzzleTimer TimerOn(Transform t)
    {
        if (t == null) return null;
        PuzzleTimer c = t.GetComponent<PuzzleTimer>();
        if (c != null) return c;
        try { return DeeperTimer(t); } catch (Exception) { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static PuzzleTimer DeeperTimer(Transform t) => BwmFind.First<PuzzleTimer>(t);
}
