// Finding things under a transform, for scripts that run inside the game. The game is IL2CPP and its build has no
// GetComponentsInChildren<T>(bool) or GetComponentInChildren<T>(bool) (engine methods it never calls are stripped:
// "MissingMethodException" in the game's log), so these walk the children with GetChild and GetComponent<T>(),
// which it keeps. Inactive children are included, as with includeInactive = true.
using System.Collections.Generic;
using UnityEngine;

public static class BwmFind
{
    // t and everything under it
    public static List<Transform> All(Transform t)
    {
        var list = new List<Transform>();
        Collect(t, list);
        return list;
    }

    static void Collect(Transform t, List<Transform> into)
    {
        if (t == null) return;
        into.Add(t);
        int n = t.childCount;
        for (int i = 0; i < n; i++) Collect(t.GetChild(i), into);
    }

    // Every component of type T on t or under it
    public static List<T> Components<T>(Transform t) where T : Component
    {
        var found = new List<T>();
        foreach (Transform c in All(t))
        {
            T comp = c.GetComponent<T>();
            if (comp != null) found.Add(comp);
        }
        return found;
    }

    // The first component of type T on t or under it (depth first), or null
    public static T First<T>(Transform t) where T : Component
    {
        if (t == null) return null;
        T comp = t.GetComponent<T>();
        if (comp != null) return comp;
        int n = t.childCount;
        for (int i = 0; i < n; i++)
        {
            T found = First<T>(t.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }
}
