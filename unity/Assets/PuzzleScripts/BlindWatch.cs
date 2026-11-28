// The blindfold guard for the blind-only rooms (Blind Carols' vestry, Blind Sculptor's court, any later one): the SDK's
// blind turnstile checks the blindfold only at entry, and no SDK part reacts to the hat slot, so this does. On an empty
// object in the puzzle (b.AddCustomScript(watch, "BlindWatch"), fields set with Sdk.Set / Sdk.Add), it watches a box in
// its own local frame. On the host, a few times a second, any online player inside the box who is not wearing a
// blindfold (hat null, or a name without "blind": night-vision goggles fail too) is made to drop what they hold
// (unless it is a blindfold: one taken off stays in their hand) and is teleported to 'putOut', outside the box.
//
// Only the host acts (PuzzleSDK.IsHost; Teleport and DropItem are host-only and reach every PC through the SDK).
// Nothing is synced: the host alone decides, and the other PCs see the player moved. This runs inside the game
// (IL2CPP): keep to Transform, Time and plain arithmetic.
using System;
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class BlindWatch : MonoBehaviour
{
    // The watched box's two corners, in this object's local frame (x across, y up, z forward, metres). Players are
    // tested by their origin, which rides about 0.75 m above what they stand on, so a box from just under the floor
    // to above the wall tops catches anyone in the room or perched on its walls.
    [SerializeField] private Vector3 boxMin = new Vector3(-3f, -0.5f, -3f);
    [SerializeField] private Vector3 boxMax = new Vector3(3f, 3f, 3f);
    // Where a player is put: an empty outside the box (and outside every gate's skip circle), facing the way out.
    // Its rotation is the way they will face.
    [SerializeField] private Transform putOut;
    // The room's turnstiles (fill with Sdk.Add): a player within 'gateSkip' metres of one, measured flat, is left
    // alone, since they may be part-way through a pass and a teleport would stand them out of it. 0 turns it off.
    [SerializeField] private List<Transform> gates = new List<Transform>();
    [SerializeField] private float gateSkip = 1.2f;
    // Seconds after putting a player out before they can be put out again (each Teleport queues a move, so polling
    // faster than the move lands would queue several).
    [SerializeField] private float cooldown = 1.5f;
    // Seconds between checks.
    [SerializeField] private float interval = 0.2f;
    // What a worn hat's name must contain to count as a blindfold (case-insensitive). A Vanilla Item blindfold's name
    // is its type, "Blindfold".
    [SerializeField] private string blindWord = "blind";
    // Drop what a sighted player holds before putting them out (so a tile carried in the room stays in the room).
    [SerializeField] private bool dropHeld = true;
    // Only guard while this puzzle is running (false: always, the usual choice: nobody sighted may arrange things
    // while the puzzle is off either).
    [SerializeField] private bool onlyWhileRunning = false;

    private readonly Dictionary<string, float> lastPut = new Dictionary<string, float>();
    private readonly HashSet<string> hatsSeen = new HashSet<string>();
    private float nextCheck;
    private PuzzleSDK puzzle;

    void Update()
    {
        if (!PuzzleSDK.IsHost) return;
        float now = Time.time;
        if (now < nextCheck) return;
        nextCheck = now + (interval > 0.05f ? interval : 0.05f);
        if (putOut == null) return;
        if (onlyWhileRunning)
        {
            if (puzzle == null) puzzle = PuzzleSDK.Of(transform);
            if (puzzle == null || !puzzle.IsRunning) return;
        }
        IReadOnlyList<PuzzlePlayer> players = PuzzleSDK.players;
        if (players == null) return;
        for (int i = 0; i < players.Count; i++)
        {
            PuzzlePlayer p = players[i];
            if (p == null || !p.isOnline) continue;
            Vector3 world = p.position;
            Vector3 local = transform.InverseTransformPoint(world);
            if (local.x < boxMin.x || local.x > boxMax.x || local.y < boxMin.y || local.y > boxMax.y || local.z < boxMin.z || local.z > boxMax.z) continue;
            string hat = p.hat;
            if (hat != null && hatsSeen.Add(hat)) Debug.Log("[BlindWatch] " + name + ": hat=" + hat);
            if (Has(hat)) continue;
            if (NearGate(world)) continue;
            string id = p.id ?? "";
            if (lastPut.TryGetValue(id, out float last) && now - last < cooldown) continue;
            if (dropHeld)
            {
                string held = p.held;
                if (held != null && !Has(held)) p.DropItem();
            }
            p.Teleport(putOut.position, putOut.rotation);
            lastPut[id] = now;
            Debug.Log("[BlindWatch] " + name + ": put " + p.name + " out (hat=" + (hat ?? "none") + ")");
        }
    }

    bool Has(string s) => s != null && !string.IsNullOrEmpty(blindWord) && s.IndexOf(blindWord, StringComparison.OrdinalIgnoreCase) >= 0;

    bool NearGate(Vector3 world)
    {
        if (gateSkip <= 0f || gates == null) return false;
        float r2 = gateSkip * gateSkip;
        for (int g = 0; g < gates.Count; g++)
        {
            Transform t = gates[g];
            if (t == null) continue;
            Vector3 at = t.position;
            float dx = world.x - at.x, dz = world.z - at.z;
            if (dx * dx + dz * dz < r2) return true;
        }
        return false;
    }
}
