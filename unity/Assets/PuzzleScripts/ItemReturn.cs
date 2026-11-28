// ItemReturn: sends items that have come to rest inside a catching box back to a home spot: the snowballs that land
// in Snowball Range's baskets go back to the bin. The game pins a prop that stops moving, so no sloped floor rolls a
// ball out again (on 15 and on 48 degrees they stayed where they landed), and a basket that kept every ball could use
// the whole supply up.
//
// On an empty in the puzzle (b.AddCustomScript(empty, "ItemReturn"), fields set with Sdk.Set):
//   itemPrefix   every Custom Item (PuzzleItem) under the puzzle's root whose object's name starts with this is watched
//   boxPrefix    every object under the root whose name starts with this is a catching box, in that object's own frame
//   boxMin/Max   the box's two corners in that frame (x across, y up, z forward, metres)
//   home         where a caught item is laid down again (an empty over the bin); 'spread' scatters them a little
//   delay        seconds an item must stay in a box before it is sent home (so a hit is seen to land first)
//
// Only the host acts. An item's place is read from the SDK mod's own PuzzleItem (its private 'prop', the game's Prop)
// and it is moved with the mod's PuzzleItem.SendTo, both by reflection: the editor's stub of PuzzleItem shows neither.
// If the mod changes them the log says so once and nothing is sent home. This runs inside the game (IL2CPP): keep to
// Transform, Time and plain C#.
using System;
using System.Collections.Generic;
using System.Reflection;
using BigPuzzleSDK;
using UnityEngine;

public class ItemReturn : MonoBehaviour
{
    [SerializeField] private string itemPrefix = "Snowball ";
    [SerializeField] private string boxPrefix = "Basket ";
    [SerializeField] private Vector3 boxMin = new Vector3(-0.5f, -0.5f, -0.5f);
    [SerializeField] private Vector3 boxMax = new Vector3(0.5f, 0.3f, 0.5f);
    [SerializeField] private Transform home;
    [SerializeField] private float spread = 0.3f;
    [SerializeField] private float delay = 1.5f;

    private PuzzleSDK sdk;
    private readonly List<PuzzleItem> items = new List<PuzzleItem>();
    private readonly List<Transform> boxes = new List<Transform>();
    private float[] since;
    private bool found, broken, warned;
    private float nextCheck;
    private int sent;

    private static FieldInfo propField;      // PuzzleItem.prop (the game's Prop)
    private static MethodInfo sendTo;        // PuzzleItem.SendTo(Vector3)

    void Update()
    {
        if (broken || !PuzzleSDK.IsHost || home == null) return;
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (!Resolve()) return;
        for (int i = 0; i < items.Count; i++)
        {
            PuzzleItem item = items[i];
            if (item == null || item.IsHeld || !Place(item, out Vector3 at) || !Caught(at)) { since[i] = -1f; continue; }
            if (since[i] < 0f) { since[i] = Time.time; continue; }
            if (Time.time - since[i] < delay) continue;
            since[i] = -1f;
            sent++;
            float a = sent * 2.4f;      // a different corner of the bin each time, so they do not land on one another
            Vector3 to = home.position + new Vector3(Mathf.Cos(a) * spread, 0f, Mathf.Sin(a) * spread);
            try { sendTo.Invoke(item, new object[] { to }); }
            catch (Exception e) { Fail("SendTo failed: " + e.GetType().Name + ": " + e.Message); return; }
        }
    }

    bool Caught(Vector3 world)
    {
        for (int k = 0; k < boxes.Count; k++)
        {
            Transform box = boxes[k];
            if (box == null) continue;
            Vector3 p = box.InverseTransformPoint(world);
            if (p.x >= boxMin.x && p.x <= boxMax.x && p.y >= boxMin.y && p.y <= boxMax.y && p.z >= boxMin.z && p.z <= boxMax.z) return true;
        }
        return false;
    }

    // Where the item's prop is now
    bool Place(PuzzleItem item, out Vector3 at)
    {
        at = Vector3.zero;
        try
        {
            Component prop = propField.GetValue(item) as Component;
            if (prop == null) return false;
            at = prop.transform.position;
            return true;
        }
        catch (Exception) { return false; }
    }

    bool Resolve()
    {
        if (found) return true;
        if (sdk == null) sdk = PuzzleSDK.Of(transform);
        if (sdk == null) return false;
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        propField = typeof(PuzzleItem).GetField("prop", any);
        sendTo = typeof(PuzzleItem).GetMethod("SendTo", any, null, new[] { typeof(Vector3) }, null);
        if (propField == null || sendTo == null) { Fail("the SDK's PuzzleItem has no 'prop' (" + (propField != null) + ") or no SendTo(Vector3) (" + (sendTo != null) + ")"); return false; }
        List<Transform> all = BwmFind.All(sdk.transform);
        for (int i = 0; i < all.Count; i++)
        {
            Transform t = all[i];
            string n = t.name;
            if (itemPrefix.Length > 0 && n.StartsWith(itemPrefix))
            {
                PuzzleItem item = t.GetComponent<PuzzleItem>();
                if (item != null) items.Add(item);
            }
            if (boxPrefix.Length > 0 && n.StartsWith(boxPrefix)) boxes.Add(t);
        }
        since = new float[items.Count];
        for (int i = 0; i < since.Length; i++) since[i] = -1f;
        found = true;
        Debug.Log("[ItemReturn] " + name + ": watching " + items.Count + " items and " + boxes.Count + " boxes");
        return true;
    }

    void Fail(string why)
    {
        broken = true;
        if (!warned) { warned = true; Debug.LogWarning("[ItemReturn] " + name + ": " + why + ": caught items will stay where they land"); }
    }
}
