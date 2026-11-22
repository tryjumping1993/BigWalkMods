using System;
using System.Collections.Generic;
using System.Text;
using BigPuzzleSDK;
using UnityEngine;

// Light the Tree's board shuffle: at every start the host re-hangs each lamp board's lamp+picture slots in a new order,
// so a lamp's place on its board says nothing about which button it belongs to; only the picture under it does.
//
// The puzzle's build names every slot "Slot <board>-<k>" (k = 1, 2, 3; <board> names one board, e.g. "3B" for Ring 3's
// board of station B's lamps) and puts the board's Display and Lamp under it. The game makes its own lamp and display as
// children of those markers, so moving a slot moves what players see. Awake records each slot's local position as the
// home of place k on its board. Shuffle (wired to the puzzle's start) runs on the host: for every board whose slots are
// shown (the lobby group in use) it draws a permutation and publishes "<count>|<board>:p1,p2[,p3];..." through a synced
// string; every PC (the host too, through Changed) moves slot k to the home of place p_k. The Press In Order pairs follow
// the objects, not the places, so nothing about winning changes here.
//
// This runs inside the game (IL2CPP): keep to plain C#, Transform and the SDK's types.
public class BoardShuffle : MonoBehaviour
{
    // made as a field, so every PC makes it in the same order
    private readonly SyncedString order = new SyncedString("light the tree slots");

    private sealed class Board
    {
        public string name;
        public readonly Dictionary<int, Transform> slots = new Dictionary<int, Transform>();
        public readonly Dictionary<int, Vector3> home = new Dictionary<int, Vector3>();
        public readonly List<int> keys = new List<int>();      // the slot numbers, ascending
    }

    private readonly List<Board> boards = new List<Board>();
    private readonly Dictionary<string, Board> byName = new Dictionary<string, Board>();
    private int attempts;
    private bool warnedParse;

    void Awake()
    {
        Transform top = transform.parent != null ? transform.parent : transform;
        Walk(top, 0);
        for (int i = 0; i < boards.Count; i++) boards[i].keys.Sort();
        Debug.Log("[BoardShuffle] " + boards.Count + " boards");
        order.Changed += Apply;
        Apply(order.Value);
    }

    void Walk(Transform t, int depth)
    {
        if (depth > 12) return;
        int n = t.childCount;
        for (int i = 0; i < n; i++)
        {
            Transform c = t.GetChild(i);
            Record(c);
            Walk(c, depth + 1);
        }
    }

    void Record(Transform t)
    {
        string n = t.name;
        if (n == null || !n.StartsWith("Slot ", StringComparison.Ordinal)) return;
        int dash = n.LastIndexOf('-');
        if (dash <= 5) return;
        int k;
        if (!int.TryParse(n.Substring(dash + 1), out k)) return;
        string boardName = n.Substring(5, dash - 5);
        Board board;
        if (!byName.TryGetValue(boardName, out board))
        {
            board = new Board { name = boardName };
            byName[boardName] = board;
            boards.Add(board);
        }
        if (board.slots.ContainsKey(k))
        {
            Debug.LogWarning("[BoardShuffle] two slots named '" + n + "': the second is left where it is");
            return;
        }
        board.slots[k] = t;
        board.home[k] = t.localPosition;
        board.keys.Add(k);
    }

    // Host: re-hang every shown board (wired to the puzzle's start)
    public void Shuffle()
    {
        if (!PuzzleSDK.IsHost) return;
        attempts++;
        var text = new StringBuilder();
        text.Append(attempts).Append('|');
        bool first = true;
        for (int i = 0; i < boards.Count; i++)
        {
            Board board = boards[i];
            if (!Shown(board)) continue;
            var places = new List<int>(board.keys);
            for (int j = places.Count - 1; j > 0; j--)
            {
                int r = UnityEngine.Random.Range(0, j + 1);
                int tmp = places[j]; places[j] = places[r]; places[r] = tmp;
            }
            if (!first) text.Append(';');
            first = false;
            text.Append(board.name).Append(':');
            for (int j = 0; j < places.Count; j++)
            {
                if (j > 0) text.Append(',');
                text.Append(places[j]);
            }
        }
        order.Set(text.ToString());
    }

    static bool Shown(Board board)
    {
        foreach (KeyValuePair<int, Transform> kv in board.slots)
            if (kv.Value == null || !kv.Value.gameObject.activeInHierarchy) return false;
        return board.slots.Count > 1;
    }

    // Every PC: move each named board's slots to the places the text gives
    void Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            int bar = text.IndexOf('|');
            string body = bar >= 0 ? text.Substring(bar + 1) : text;
            if (body.Length == 0) return;
            string[] parts = body.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int colon = part.IndexOf(':');
                if (colon <= 0) { Warn(text); continue; }
                Board board;
                if (!byName.TryGetValue(part.Substring(0, colon), out board)) continue;
                string[] numbers = part.Substring(colon + 1).Split(',');
                if (numbers.Length != board.keys.Count) { Warn(text); continue; }
                var places = new int[numbers.Length];
                var used = new HashSet<int>();
                bool ok = true;
                for (int j = 0; j < numbers.Length; j++)
                {
                    int p;
                    if (!int.TryParse(numbers[j], out p) || !board.home.ContainsKey(p) || !used.Add(p)) { ok = false; break; }
                    places[j] = p;
                }
                if (!ok) { Warn(text); continue; }
                for (int j = 0; j < places.Length; j++)
                {
                    Transform slot = board.slots[board.keys[j]];
                    if (slot != null) slot.localPosition = board.home[places[j]];
                }
            }
        }
        catch (Exception e)
        {
            if (!warnedParse) { warnedParse = true; Debug.LogWarning("[BoardShuffle] could not apply '" + text + "': " + e.Message); }
        }
    }

    void Warn(string text)
    {
        if (warnedParse) return;
        warnedParse = true;
        Debug.LogWarning("[BoardShuffle] could not read '" + text + "': slots left where they are");
    }
}
