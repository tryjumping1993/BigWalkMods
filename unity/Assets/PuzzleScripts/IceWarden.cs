// Thin Ice's warden: the rule the SDK's parts cannot enforce. While any of the puzzle's Press In Order maps is running,
// anyone on the pond (the basin's deck, rim and pits, the exit jetty, and the air above them) who is not wearing the
// blindfold ends the run, and so does anyone inside a manned hut (the lodge, the Far Hut) who holds a walkie-talkie or
// a megaphone. "Ends the run" is PuzzlePressInOrder.End(): the lamps go out, the bars show the failure look, and the
// team restarts with any start switch, as after a missed phase.
//
// On an empty directly under the puzzle's root (b.AddCustomScript(empty, "IceWarden"), fields set with Sdk.Set). It
// finds what it needs by name under the root, walking the hierarchy by hand (inactive objects too):
//   "Ice Map (2)", "Ice Map (3)", "Ice Map (4)"   the Press In Order maps (only one is shown in any lobby)
//   "Lodge Walls", "Far Hut Walls"               the huts' soundproof shells; a hut whose shell is hidden is skipped
// Boxes are centre and size in this object's local frame (x across, y up, z forward, metres), so the puzzle's yaw is
// handled. Players are tested by their origin, about 0.75 m above what they stand on.
//
// Only the host acts (PuzzleSDK.IsHost; End() is host-only and reaches every PC through the SDK). Nothing is synced.
// This runs inside the game (IL2CPP): keep to Transform, GetChild, GetComponent, Time and plain C#.
using System;
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class IceWarden : MonoBehaviour
{
    // The pond: the basin (deck, rim, the five pits) from 0.5 m above the root (a player standing in a pit rides at
    // about 0.8) to 31 m up, so a jetpacker over the ice is inside it
    [SerializeField] private Vector3 pondCentre = new Vector3(0f, 15.75f, 4f);
    [SerializeField] private Vector3 pondSize = new Vector3(13f, 30.5f, 13f);
    // The exit jetty: its steps and platform, from just under the first step (2.1) up; the ground below it is outside
    [SerializeField] private Vector3 jettyCentre = new Vector3(0f, 16.4f, 11.7f);
    [SerializeField] private Vector3 jettySize = new Vector3(1.6f, 29.2f, 2.4f);
    // The huts: their outer walls, floor to roof
    [SerializeField] private Vector3 lodgeCentre = new Vector3(-13.25f, 1.4f, -2f);
    [SerializeField] private Vector3 lodgeSize = new Vector3(4.9f, 2.8f, 3.6f);
    [SerializeField] private Vector3 farCentre = new Vector3(13.25f, 1.4f, 12f);
    [SerializeField] private Vector3 farSize = new Vector3(4.9f, 2.8f, 3.6f);
    // Seconds between checks
    [SerializeField] private float interval = 0.3f;
    // The blindfold rule (false is the fallback if a game update renames the head slot and every run ends at once)
    [SerializeField] private bool checkHat = true;
    // The radio rule in the huts
    [SerializeField] private bool checkRadios = true;
    // What a worn hat's name must contain to count as a blindfold (case-insensitive): a Vanilla Item blindfold reads
    // "Blindfold", a game-native one its object's name
    [SerializeField] private string blindWord = "blind";

    private static readonly string[] MapNames = { "Ice Map (2)", "Ice Map (3)", "Ice Map (4)" };
    private static readonly string[] RadioWords = { "walkie", "megaphone" };

    private readonly PuzzlePressInOrder[] maps = new PuzzlePressInOrder[3];
    private readonly Transform[] mapObjects = new Transform[3];
    private Transform lodgeWalls, farWalls;
    private readonly HashSet<string> hatsSeen = new HashSet<string>();
    private bool collected;
    private float nextCheck, nextCollect;

    void Update()
    {
        if (!PuzzleSDK.IsHost) return;
        float now = Time.time;
        if (now < nextCheck) return;
        nextCheck = now + (interval > 0.05f ? interval : 0.05f);

        if (!collected || (now >= nextCollect && FoundMaps() == 0)) Collect(now);

        bool running = false;
        for (int m = 0; m < maps.Length; m++)
            if (maps[m] != null && maps[m].IsRunning) running = true;
        if (!running) return;

        IReadOnlyList<PuzzlePlayer> players = PuzzleSDK.players;
        if (players == null) return;
        for (int i = 0; i < players.Count; i++)
        {
            PuzzlePlayer p = players[i];
            if (p == null || !p.isOnline) continue;
            Vector3 local = transform.InverseTransformPoint(p.position);
            string reason = null;

            if (checkHat && (Inside(local, pondCentre, pondSize) || Inside(local, jettyCentre, jettySize)))
            {
                string hat = p.hat;
                if (hat != null && hatsSeen.Add(hat)) Debug.Log("[IceWarden] " + name + ": hat on the ice=" + hat);
                if (!Contains(hat, blindWord)) reason = "on the ice wearing '" + (hat ?? "nothing") + "'";
            }
            if (reason == null && checkRadios)
            {
                string held = p.held;
                if (IsRadio(held))
                {
                    if (Shown(lodgeWalls) && Inside(local, lodgeCentre, lodgeSize)) reason = "in the lodge holding '" + held + "'";
                    else if (Shown(farWalls) && Inside(local, farCentre, farSize)) reason = "in the Far Hut holding '" + held + "'";
                }
            }
            if (reason == null) continue;

            for (int m = 0; m < maps.Length; m++)
                if (maps[m] != null && maps[m].IsRunning) maps[m].End();
            Debug.Log("[IceWarden] ended the run: " + p.name + " " + reason);
            return;
        }
    }

    static bool Inside(Vector3 p, Vector3 centre, Vector3 size)
    {
        Vector3 d = p - centre;
        return d.x >= -size.x / 2f && d.x <= size.x / 2f && d.y >= -size.y / 2f && d.y <= size.y / 2f && d.z >= -size.z / 2f && d.z <= size.z / 2f;
    }

    static bool Contains(string s, string word) => s != null && !string.IsNullOrEmpty(word) && s.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

    static bool IsRadio(string held)
    {
        if (held == null) return false;
        for (int i = 0; i < RadioWords.Length; i++) if (Contains(held, RadioWords[i])) return true;
        return false;
    }

    static bool Shown(Transform t) => t != null && t.gameObject.activeInHierarchy;

    int FoundMaps()
    {
        int n = 0;
        for (int m = 0; m < maps.Length; m++) if (maps[m] != null) n++;
        return n;
    }

    // Finds the maps and the huts' shells under the puzzle's root (this object's parent)
    void Collect(float now)
    {
        collected = true;
        nextCollect = now + 5f;      // with no map found, look again every 5 s
        for (int m = 0; m < maps.Length; m++) { maps[m] = null; mapObjects[m] = null; }
        lodgeWalls = null; farWalls = null;
        Transform root = transform.parent != null ? transform.parent : transform.root;
        Walk(root);
        string found = "";
        for (int m = 0; m < maps.Length; m++)
        {
            if (mapObjects[m] != null) maps[m] = mapObjects[m].GetComponent<PuzzlePressInOrder>();
            if (maps[m] != null) found += (found.Length > 0 ? ", " : "") + MapNames[m] + (mapObjects[m].gameObject.activeInHierarchy ? "" : " (hidden)");
        }
        Debug.Log("[IceWarden] " + name + ": maps " + (found.Length > 0 ? found : "none") + "; huts "
            + (lodgeWalls != null ? "Lodge" + (Shown(lodgeWalls) ? "" : " (hidden)") : "no Lodge") + ", "
            + (farWalls != null ? "Far Hut" + (Shown(farWalls) ? "" : " (hidden)") : "no Far Hut"));
    }

    void Walk(Transform t)
    {
        if (t == null) return;
        string n = t.name;
        for (int m = 0; m < MapNames.Length; m++) if (n == MapNames[m] && mapObjects[m] == null) mapObjects[m] = t;
        if (n == "Lodge Walls" && lodgeWalls == null) lodgeWalls = t;
        else if (n == "Far Hut Walls" && farWalls == null) farWalls = t;
        int children = t.childCount;
        for (int i = 0; i < children; i++) Walk(t.GetChild(i));
    }
}
