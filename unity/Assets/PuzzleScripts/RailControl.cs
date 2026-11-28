using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

// The Winter Isle railway's shared state, kept by the host and sent to every player through the SDK's synced
// variables. Winter Walk's TrainDriver does the work: on the host it runs the trains (departures, the five-minute
// stops, the throttles, the gap to the train ahead) and publishes each train's current order here; on every PC it
// reads the orders back and moves the trains from them on Mirror's shared clock. This script only carries the text
// and the throttle presses, because synced variables can only be made by a pack's own scripts.
//
// The throttle in each locomotive and coach is a push button wired to Throttle(train) by the island's build; the
// SDK runs button events on the host only, which is the PC that decides.
//
// This runs inside the game (IL2CPP): keep to plain C# and the SDK's types.
public class RailControl : MonoBehaviour
{
    // made as fields, so every PC makes them in the same order
    private readonly SyncedString orders = new SyncedString("rail orders");

    // The latest orders on this PC (the host's own, or what it sent)
    public static string Orders = "";
    // Host: trains whose throttle was pushed, by index, waiting for TrainDriver to take them
    public static readonly Queue<int> Throttles = new Queue<int>();
    public static int Version;          // counts changes to Orders, so a reader can tell it changed

    static RailControl live;

    void Awake()
    {
        live = this;
        orders.Changed += OnChanged;
    }

    void OnChanged(string text)
    {
        Orders = text ?? "";
        Version++;
    }

    void OnDestroy() { if (live == this) live = null; }

    // A throttle was pushed (button events run on the host)
    public void Throttle(int train)
    {
        if (Throttles.Count < 64) Throttles.Enqueue(train);
    }

    // Host: the trains' new orders, for every player (ignored on other PCs by the SDK)
    public static void Publish(string text)
    {
        if (live == null) return;
        live.orders.Set(text);
        Orders = text;
        Version++;
    }

    public static bool Present => live != null;
}
