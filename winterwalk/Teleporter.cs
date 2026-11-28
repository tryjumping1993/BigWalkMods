// Winter Walk's teleporters: a pad a player stands on, and is set down somewhere else.
//
// The packs mark them with empty objects: "Teleport Pad <name> to <place>" at a pad's middle and "Teleport Arrival
// <place>" where its travellers arrive (facing the arrival's +z). The first are the pads beside the gear stations at the
// landing and the plaza, which go straight to Cast Iron Pizza's door on the summit, and the one on its terrace that goes
// back to the plaza (the user, 2026-11-24). Standing on a pad for HoldSeconds does it: this PC moves its own player,
// as the dinosaurs' catch does, and the game tells the others. A pad does nothing again until the player has stepped
// off every pad, so nobody arriving on or beside one is sent straight back.
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WinterWalk
{
    public class Teleporter : MonoBehaviour
    {
        public Teleporter(IntPtr ptr) : base(ptr) { }

        public static bool Off;
        public static float Radius = 0.95f;         // metres from a pad's middle
        public static float HoldSeconds = 1.2f;     // standing on it this long
        public static string State = "not started";
        public static int TestPad = -1;             // for tests: use this pad now (0, 1, 2: see the log's list)

        sealed class Pad { public Transform mark; public float x, y, z; public string place; }
        readonly List<Pad> pads = new List<Pad>();
        readonly Dictionary<string, Transform> arrivals = new Dictionary<string, Transform>();
        float nextScan, onSince = -1f;
        bool mustStepOff;
        int onPad = -1;

        void Scan()
        {
            pads.Clear(); arrivals.Clear();
            foreach (BigPuzzleSDK.PuzzleSDK sdk in FindObjectsOfType<BigPuzzleSDK.PuzzleSDK>())
            {
                if (sdk == null) continue;
                foreach (Component c in sdk.transform.root.GetComponentsInChildren(Il2CppType.Of<Transform>(), false))
                {
                    Transform t = c != null ? c.TryCast<Transform>() : null;
                    if (t == null) continue;
                    string n = t.name;
                    if (n.StartsWith("Teleport Arrival ", StringComparison.Ordinal)) arrivals[n.Substring(17).Trim()] = t;
                    else if (n.StartsWith("Teleport Pad ", StringComparison.Ordinal))
                    {
                        int to = n.LastIndexOf(" to ", StringComparison.Ordinal);
                        if (to < 0) continue;
                        Vector3 p = t.position;
                        pads.Add(new Pad { mark = t, x = p.x, y = p.y, z = p.z, place = n.Substring(to + 4).Trim() });
                    }
                }
            }
            if (pads.Count > 0 || arrivals.Count > 0)
            {
                string list = "";
                for (int i = 0; i < pads.Count; i++) list += (i > 0 ? "; " : "") + i + " " + pads[i].mark.name + " at " + pads[i].x.ToString("0") + ", " + pads[i].y.ToString("0") + ", " + pads[i].z.ToString("0");
                Plugin.L.LogInfo("Winter Walk: teleporters: " + pads.Count + " pads (" + list + "), arrivals at " + string.Join(", ", arrivals.Keys));
            }
        }

        void Go(PlayerCharacter pc, string place, string from)
        {
            if (!arrivals.TryGetValue(place, out Transform at) || at == null) { State = "no arrival named " + place; return; }
            Vector3 to = at.position + Vector3.up * 0.9f;
            Diag.Touched("teleporter");
            pc.transform.position = to;
            pc.transform.rotation = Quaternion.Euler(0f, at.eulerAngles.y, 0f);
            if (pc.rb != null) { pc.rb.position = to; pc.rb.linearVelocity = Vector3.zero; pc.rb.angularVelocity = Vector3.zero; }
            mustStepOff = true; onSince = -1f; onPad = -1;
            State = "went to " + place;
            Diag.Event("teleporter: " + from + " to " + place + " at " + to.x.ToString("0") + ", " + to.y.ToString("0") + ", " + to.z.ToString("0"));
        }

        void Update()
        {
            if (Off) return;
            using var diag = Diag.Time("teleporter");
            try
            {
                Mirror.NetworkIdentity me = Mirror.NetworkClient.localPlayer;
                if (me == null) { State = "no player"; return; }
                float now = Time.unscaledTime;
                bool stale = false;
                foreach (Pad p in pads) if (p.mark == null) { stale = true; break; }
                if (stale || (pads.Count == 0 && now >= nextScan)) { nextScan = now + 5f; Scan(); }
                if (pads.Count == 0) { State = "no pads in the loaded packs"; return; }

                PlayerCharacter pc = null;
                if (TestPad >= 0)
                {
                    int i = TestPad; TestPad = -1;
                    pc = me.GetComponent<PlayerCharacter>();
                    if (pc != null && i < pads.Count) Go(pc, pads[i].place, "test of " + pads[i].mark.name);
                    return;
                }
                Vector3 at = me.transform.position;
                int on = -1;
                for (int i = 0; i < pads.Count; i++)
                {
                    Pad p = pads[i];
                    float dx = at.x - p.x, dz = at.z - p.z, dy = at.y - p.y;
                    if (dx * dx + dz * dz < Radius * Radius && dy > -0.5f && dy < 2.2f) { on = i; break; }
                }
                if (on < 0) { mustStepOff = false; onSince = -1f; onPad = -1; State = pads.Count + " pads"; return; }
                if (mustStepOff) { State = "arrived: step off the pad to use it again"; return; }
                if (on != onPad) { onPad = on; onSince = now; }
                State = "on the pad to " + pads[on].place;
                if (now - onSince < HoldSeconds) return;
                pc = me.GetComponent<PlayerCharacter>();
                if (pc != null) Go(pc, pads[on].place, pads[on].mark.name);
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: teleporter stopped: " + e.GetType().Name + ": " + e.Message);
                Off = true;
            }
        }
    }
}
