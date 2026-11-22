// Winter Walk's puzzle sleeper: far-away puzzles stop costing frames.
//
// Every one of the SDK's puzzle components (lamps, displays, sensors, seats, timers, the copies of the game's own
// parts...) runs an Update every frame, wherever the players are, and in this game (IL2CPP, the SDK's components
// injected through Il2CppInterop) each call is dear: measured on 2026-11-22, the island's 29 puzzles carried 2,667
// of them and cost about 3 ms a frame from anywhere on the map (98 fps against 150 with them switched off, at the
// base game's island 2 km away). Switching a component off (Behaviour.enabled) stops only its Update: it is still
// drawn, still solid, keeps its state and its network ids, and these components have no OnEnable/OnDisable.
//
// So: a component sleeps while no player (any player in the walk, not just this PC's) is within Range of it and its
// puzzle is not running, and wakes as soon as one comes within range or the puzzle starts. Every PC decides for
// itself from the same player positions. Left awake always: the types with enable/disable handlers or that do work
// at load (radio dead zones, reverb zones, sim-press switches, terrain holes), and everything to do with gourds
// (vices, plinths, boxes, tower buttons, the puzzle's own PuzzleSDK).
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WinterWalk
{
    public class PuzzleSleep : MonoBehaviour
    {
        public PuzzleSleep(IntPtr ptr) : base(ptr) { }

        public static bool Off;                 // live: true wakes everything and stops sleeping
        public static float Range = 200f;       // metres from the nearest player
        public static int Asleep, Managed;
        public static string State = "not started";

        static readonly HashSet<string> Never = new HashSet<string>
        {
            "PuzzleSDK", "PuzzleRadioDeadZone", "PuzzleReverbZone", "PuzzleSimPressSwitch", "PuzzleTerrainHole",
            "PuzzleGourdVice", "PuzzleGourdPlinth", "PuzzleBox", "PuzzlePillow", "TowerButton",
        };

        sealed class Part { public Behaviour b; public float x, z; public bool slept; }
        sealed class Puzzle { public BigPuzzleSDK.PuzzleSDK sdk; public IntPtr root; public readonly List<Part> parts = new List<Part>(); public float x0, x1, z0, z1; }
        readonly List<Puzzle> puzzles = new List<Puzzle>();
        float nextScan, nextTick;
        int rootsSeen = -1;
        readonly List<Vector3> players = new List<Vector3>();
        bool wasOff;

        void Scan()
        {
            WakeAll();
            puzzles.Clear();
            Managed = 0;
            foreach (BigPuzzleSDK.PuzzleSDK sdk in FindObjectsOfType<BigPuzzleSDK.PuzzleSDK>())
            {
                if (sdk == null) continue;
                var p = new Puzzle { sdk = sdk, root = sdk.transform.root.Pointer, x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue };
                foreach (Component c in sdk.transform.root.GetComponentsInChildren(Il2CppType.Of<Behaviour>(), true))
                {
                    Behaviour b = c != null ? c.TryCast<Behaviour>() : null;
                    if (b == null || !b.enabled) continue;
                    string type = b.GetIl2CppType().Name;
                    if (!type.StartsWith("Puzzle", StringComparison.Ordinal) || Never.Contains(type)) continue;
                    if (b.GetIl2CppType().Namespace != "BigPuzzleSDK") continue;      // only the SDK's own (not our scripts)
                    Vector3 at = b.transform.position;
                    p.parts.Add(new Part { b = b, x = at.x, z = at.z });
                    if (at.x < p.x0) p.x0 = at.x; if (at.x > p.x1) p.x1 = at.x;
                    if (at.z < p.z0) p.z0 = at.z; if (at.z > p.z1) p.z1 = at.z;
                }
                if (p.parts.Count == 0) continue;
                puzzles.Add(p);
                Managed += p.parts.Count;
            }
            Plugin.L.LogInfo("Winter Walk: puzzle sleeper watches " + Managed + " components in " + puzzles.Count + " puzzles; they sleep beyond " + Range + " m from every player while their puzzle is not running");
        }

        void WakeAll()
        {
            foreach (Puzzle p in puzzles)
                foreach (Part part in p.parts)
                    if (part.slept) { part.slept = false; try { if (part.b != null) part.b.enabled = true; } catch (Exception) { } }
            Asleep = 0;
        }

        void Update()
        {
            using var diag = Diag.Time("puzzle sleep");
            try
            {
                if (Off) { if (!wasOff) { WakeAll(); wasOff = true; State = "off"; } return; }
                wasOff = false;
                float now = Time.unscaledTime;
                if (now >= nextScan)
                {
                    nextScan = now + 5f;
                    // the packs were loaded or reloaded: the puzzles are new objects
                    int roots = 0;
                    foreach (BigPuzzleSDK.PuzzleSDK sdk in FindObjectsOfType<BigPuzzleSDK.PuzzleSDK>()) if (sdk != null) roots++;
                    bool stale = roots != rootsSeen;
                    foreach (Puzzle p in puzzles) if (p.sdk == null) { stale = true; break; }
                    if (stale) { rootsSeen = roots; Scan(); }
                }
                if (now < nextTick || puzzles.Count == 0) return;
                nextTick = now + 0.25f;

                players.Clear();
                var all = PlayerCharacter.allPlayerCharacters;
                if (all != null) for (int i = 0; i < all.Count; i++) { PlayerCharacter pc = all[i]; if (pc != null) players.Add(pc.transform.position); }
                Camera cam = Camera.main;
                if (cam != null) players.Add(cam.transform.position);      // (a spectating camera counts too)
                if (players.Count == 0) return;

                float r2 = Range * Range;
                int asleep = 0;
                foreach (Puzzle p in puzzles)
                {
                    if (p.sdk == null) continue;
                    bool running = p.sdk.IsRunning;
                    // quick test against the puzzle's box: nobody near any of it, or someone well inside it
                    float far = float.MaxValue;
                    foreach (Vector3 q in players)
                    {
                        float dx = Math.Max(0f, Math.Max(p.x0 - q.x, q.x - p.x1)), dz = Math.Max(0f, Math.Max(p.z0 - q.z, q.z - p.z1));
                        float d = dx * dx + dz * dz;
                        if (d < far) far = d;
                    }
                    bool allAwake = running || far == 0f, allAsleep = !running && far > r2;
                    foreach (Part part in p.parts)
                    {
                        bool sleep;
                        if (allAwake) sleep = false;
                        else if (allAsleep) sleep = true;
                        else
                        {
                            sleep = true;
                            foreach (Vector3 q in players) { float dx = part.x - q.x, dz = part.z - q.z; if (dx * dx + dz * dz < r2) { sleep = false; break; } }
                        }
                        if (sleep != part.slept)
                        {
                            if (part.b == null) continue;
                            part.slept = sleep;
                            part.b.enabled = !sleep;
                        }
                        if (part.slept) asleep++;
                    }
                }
                Asleep = asleep;
                State = asleep + " of " + Managed + " puzzle components asleep";
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: puzzle sleeper stopped: " + e.GetType().Name + ": " + e.Message);
                WakeAll();
                Off = true;
            }
        }
    }
}
