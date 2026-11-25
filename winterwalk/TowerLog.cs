// Winter Walk's log of gourds on the towers' plinths, for the debug log (players' and ours).
//
// Once a second it looks at every plinth in the walk ("Tree Plinth 3", "Mine Plinth 1", "Summit Plinth 12"...: the
// tower is the name before " Plinth") and writes a line when one changes: a gourd placed or taken, by the SDK's flag
// (PuzzleGourdPlinth.HasGourd) and by the game's slot (what it holds), the tower's count after it, which player was
// nearest, and when a tower's plinths are all filled. When a walk loads it writes every tower's count once, and a line
// whenever the flag and the slot disagree (a plinth whose flag was missed, which Towers.Filled still counts).
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WinterWalk
{
    public class TowerLog : MonoBehaviour
    {
        public TowerLog(IntPtr ptr) : base(ptr) { }

        public static bool Off;                 // live: stops the log
        public static string State = "not started";

        sealed class Seen { public BigPuzzleSDK.PuzzleGourdPlinth plinth; public string name, tower; public bool flag, slot; public string prop; public float x, y, z; }
        readonly List<Seen> seen = new List<Seen>();
        readonly Dictionary<string, int> towerSize = new Dictionary<string, int>();
        readonly HashSet<string> complete = new HashSet<string>();
        float nextLook, nextScan;
        int lastCount = -1;

        static string TowerOf(string name)
        {
            int i = name.IndexOf(" Plinth", StringComparison.Ordinal);
            return i > 0 ? name.Substring(0, i) : name;
        }

        void Scan(BigPuzzleSDK.PuzzleGourdPlinth[] all)
        {
            seen.Clear(); towerSize.Clear(); complete.Clear();
            foreach (BigPuzzleSDK.PuzzleGourdPlinth p in all)
            {
                if (p == null) continue;
                var s = new Seen { plinth = p, name = p.gameObject.name };
                s.tower = TowerOf(s.name);
                Vector3 at = p.transform.position; s.x = at.x; s.y = at.y; s.z = at.z;
                Read(s, out s.flag, out s.slot, out s.prop);
                seen.Add(s);
                towerSize[s.tower] = towerSize.TryGetValue(s.tower, out int n) ? n + 1 : 1;
            }
            seen.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var line = new StringBuilder("plinths in this walk: " + seen.Count);
            foreach (KeyValuePair<string, int> t in towerSize)
            {
                int filled = Filled(t.Key);
                line.Append("; " + t.Key + " " + filled + " of " + t.Value);
                if (filled == t.Value) complete.Add(t.Key);
            }
            Diag.Event(line.ToString());
            foreach (Seen s in seen) if (s.flag != s.slot) Diag.Event("plinth \"" + s.name + "\": the SDK's flag says " + (s.flag ? "a gourd" : "empty") + ", the slot " + (s.slot ? "holds " + s.prop : "is empty") + " (counted as filled)");
        }

        static void Read(Seen s, out bool flag, out bool slot, out string prop)
        {
            flag = false; slot = false; prop = "";
            try { flag = s.plinth.HasGourd; } catch (Exception) { }
            try
            {
                PropHome home = Towers.Slot(s.plinth);
                var held = home != null ? home.pinnedProp : null;
                if (held != null) { slot = true; prop = held.gameObject.name; }
            }
            catch (Exception) { }
        }

        int Filled(string tower)
        {
            int n = 0;
            foreach (Seen s in seen) if (s.tower == tower && (s.flag || s.slot)) n++;
            return n;
        }

        // which player is nearest the plinth, and how far (each PC sees every player)
        static string Nearest(Seen s)
        {
            string who = "nobody"; double best = double.MaxValue;
            try
            {
                var all = PlayerCharacter.allPlayerCharacters;
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                IntPtr mine = me != null ? me.gameObject.Pointer : IntPtr.Zero;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        PlayerCharacter pc = all[i];
                        if (pc == null) continue;
                        Vector3 q = pc.transform.position;
                        double dx = q.x - s.x, dy = q.y - s.y, dz = q.z - s.z, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (d < best) { best = d; who = pc.gameObject.Pointer == mine ? "this PC's player" : "another player"; }
                    }
            }
            catch (Exception) { }
            return best == double.MaxValue ? who : who + " " + best.ToString("0.0") + " m away";
        }

        void Update()
        {
            if (Off) return;
            float now = Time.unscaledTime;
            if (now < nextLook) return;
            nextLook = now + 1f;
            using var diag = Diag.Time("tower log");
            try
            {
                // a walk loads or the packs reload: the plinths are new objects
                bool stale = false;
                foreach (Seen s in seen) if (s.plinth == null) { stale = true; break; }
                if (stale || now >= nextScan)
                {
                    nextScan = now + 10f;
                    var all = FindObjectsOfType<BigPuzzleSDK.PuzzleGourdPlinth>();
                    int count = all != null ? all.Length : 0;
                    if (stale || count != lastCount) { lastCount = count; if (count > 0) Scan(all); else seen.Clear(); }
                }

                foreach (Seen s in seen)
                {
                    Read(s, out bool flag, out bool slot, out string prop);
                    bool was = s.flag || s.slot, isNow = flag || slot;
                    if (flag == s.flag && slot == s.slot && prop == s.prop) continue;
                    string before = s.prop;
                    s.flag = flag; s.slot = slot; s.prop = prop;
                    int filled = Filled(s.tower), size = towerSize[s.tower];
                    if (isNow && !was)
                        Diag.Event("gourd placed on \"" + s.name + "\" (" + (slot ? prop : "no prop in the slot") + ", flag " + (flag ? "on" : "off") + "), " + Nearest(s) + "; " + s.tower + " now " + filled + " of " + size);
                    else if (!isNow && was)
                        Diag.Event("gourd taken from \"" + s.name + "\" (" + (before.Length > 0 ? before : "flag only") + "), " + Nearest(s) + "; " + s.tower + " now " + filled + " of " + size);
                    else
                        Diag.Event("plinth \"" + s.name + "\" changed: flag " + (flag ? "on" : "off") + ", slot " + (slot ? "holds " + prop : "empty") + "; " + s.tower + " " + filled + " of " + size);
                    if (filled == size && complete.Add(s.tower)) Diag.Event(s.tower + " plinths all filled (" + size + " of " + size + ")");
                    else if (filled < size && complete.Remove(s.tower)) Diag.Event(s.tower + " plinths no longer all filled (" + filled + " of " + size + ")");
                }
                int total = 0; foreach (Seen s in seen) if (s.flag || s.slot) total++;
                State = seen.Count == 0 ? "no plinths" : total + " of " + seen.Count + " plinths hold gourds";
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: tower log stopped: " + e.GetType().Name + ": " + e.Message);
                Off = true;
            }
        }
    }
}
