// Glass Booth's tile reset: every time the puzzle starts, its peg tiles go back to where they were placed (their rack
// stalls), off any board slot they hang on. The SDK has no call that sends a peg tile home, and without one the relay
// can be short-cut by stocking the hut or hanging tiles before the start, and a tile carried off is lost for good.
// On an empty object under the puzzle's root (b.AddCustomScript(piece, "GlassBoothTileReset")). Any other puzzle may
// use it the same way: it resets the tiles of whatever puzzle it is in.
//
// Only the host acts (PuzzleSDK.IsHost), on the puzzle's switch from stopped to running. It does what the SDK's own
// host-side PuzzleItem.SendTo does for an item: the tile's prop is unpinned (ServerSetUnpinned, which also takes it
// off a board slot), moved to its stall and stilled. A tile someone holds (hand, belt or backpack) is left with them.
// Nothing is synced by this script: the host moves the tiles and the game carries them to the other PCs.
//
// The SDK's PuzzlePegTile keeps the real tile (a copy of the game's peg tile, a networked Prop) in a private field
// and lists every tile in a private static list; the game's Prop type is not in the SDK's stubs. So this reaches
// them by reflection, once, and does nothing (with one warning) if the SDK or the game ever renames them. In the
// editor the stubs have neither, so it never acts there.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BigPuzzleSDK;
using UnityEngine;

public class GlassBoothTileReset : MonoBehaviour
{
    // The puzzle's name in the log line
    [SerializeField] private string label = "GlassBooth";

    // static readonly, not const: an enum constant makes the SDK's plugin injector (Cecil) resolve netstandard, which fails
    private static readonly BindingFlags Any =BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private PuzzleSDK puzzle;
    private bool primed;
    private bool wasRunning;

    // Reflection, found once
    private static bool looked;
    private static bool broken;
    private static FieldInfo allField;       // PuzzlePegTile.all (static List<PuzzlePegTile>)
    private static FieldInfo tileField;      // PuzzlePegTile.tile (the spawned GameObject)
    private static Type propType;            // the game's Prop
    private static MethodInfo getProp;       // GameObject.GetComponent<Prop>()
    private static MethodInfo unpin;         // Prop.ServerSetUnpinned(...)

    void Update()
    {
        if (!PuzzleSDK.IsHost) { primed = false; return; }
        if (puzzle == null) puzzle = PuzzleSDK.Of(transform);
        if (puzzle == null) return;
        bool running = puzzle.IsRunning;
        if (!primed)
        {
            // the first look only learns the state: a puzzle found already running was started before we woke
            primed = true;
            wasRunning = running;
            return;
        }
        if (running && !wasRunning) ResetTiles();
        wasRunning = running;
    }

    void ResetTiles()
    {
        if (!Look()) return;
        int home = 0, held = 0, failed = 0;
        int root = transform.root.GetInstanceID();
        foreach (Component tile in Tiles())
        {
            if (tile == null || tile.transform.root.GetInstanceID() != root) continue;
            try
            {
                GameObject go = tileField.GetValue(tile) as GameObject;
                if (go == null) { failed++; continue; }
                object prop = getProp.Invoke(go, null);
                if (prop == null) { failed++; continue; }
                if (IsHeld(prop)) { held++; continue; }
                Invoke(unpin, prop);
                Vector3 position = tile.transform.position;
                Quaternion rotation = tile.transform.rotation;
                go.transform.position = position;
                go.transform.rotation = rotation;
                Rigidbody body = go.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = position;
                    body.rotation = rotation;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                home++;
            }
            catch (Exception ex)
            {
                failed++;
                Exception inner = ex.InnerException ?? ex;
                Debug.LogWarning("[TileReset] " + label + ": " + tile.name + ": " + inner.GetType().Name + ": " + inner.Message);
            }
        }
        Debug.Log("[TileReset] " + label + ": " + home + " tiles home, " + held + " held" + (failed > 0 ? ", " + failed + " failed" : ""));
    }

    // Every peg tile the SDK knows (its own list), else this puzzle's by search
    IEnumerable<Component> Tiles()
    {
        var found = new List<Component>();
        IEnumerable list = allField != null ? allField.GetValue(null) as IEnumerable : null;
        if (list != null)
        {
            foreach (object o in list) if (o is Component c) found.Add(c);
            return found;
        }
        foreach (PuzzlePegTile t in BwmFind.Components<PuzzlePegTile>(transform.root)) found.Add(t);
        return found;
    }

    // In someone's hands, belt or backpack (as the SDK's PuzzleItem reads it)
    static bool IsHeld(object prop)
    {
        if (Flag(prop, "_collidersAreHeld") || Flag(prop, "_isInInventory") || Flag(prop, "isInInventory")) return true;
        object holder = Member(prop, "exclusiveHolder");
        return holder != null;
    }

    static bool Flag(object o, string name) => Member(o, name) is bool b && b;

    static object Member(object o, string name)
    {
        try
        {
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, Any);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o);
            FieldInfo f = t.GetField(name, Any);
            if (f != null) return f.GetValue(o);
        }
        catch (Exception) { }
        return null;
    }

    // Calls a method whose parameters (if any) all have defaults
    static void Invoke(MethodInfo m, object target)
    {
        ParameterInfo[] ps = m.GetParameters();
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
        m.Invoke(target, args);
    }

    bool Look()
    {
        if (looked) return !broken;
        looked = true;
        Type tileType = typeof(PuzzlePegTile);
        allField = tileType.GetField("all", Any);
        tileField = tileType.GetField("tile", Any);
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t;
            try { t = a.GetType("Prop", false); } catch (Exception) { continue; }
            if (t != null && typeof(Component).IsAssignableFrom(t)) { propType = t; break; }
        }
        if (propType != null)
        {
            foreach (MethodInfo m in typeof(GameObject).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                {
                    getProp = m.MakeGenericMethod(propType);
                    break;
                }
            foreach (MethodInfo m in propType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != "ServerSetUnpinned") continue;
                bool allDefault = true;
                foreach (ParameterInfo p in m.GetParameters()) if (!p.HasDefaultValue) allDefault = false;
                if (allDefault) { unpin = m; break; }
            }
        }
        broken = tileField == null || getProp == null || unpin == null;
        if (broken)
            Debug.LogWarning("[TileReset] " + label + ": cannot reach the tiles (tile field " + (tileField != null) + ", Prop " + (propType != null)
                + ", GetComponent " + (getProp != null) + ", ServerSetUnpinned " + (unpin != null) + "): tiles will not be sent home");
        return !broken;
    }
}
