// The dinosaurs' link between the host and the players (on the Dinosaurs pack's "Dino Control" object). Winter Walk's
// DinoDriver does the work; this only carries messages, through the SDK's synced variables (made during pack load, so
// they line up on every PC):
//  - "dino orders": the host's text of what every dinosaur is doing (calm, chasing whom, where, how fast), which every
//    PC reads to place them (late joiners get the latest);
//  - "dino reports": what a player's PC tells the host: how much noise its player is making ("noise <netId> <amount>
//    <safe>"), and that a dinosaur caught its player ("caught <netId> <dino>"). On the host its own reports are queued
//    directly.
// DinoDriver reaches these statics by reflection (it is in another assembly).
using System.Collections.Generic;
using BigPuzzleSDK;
using UnityEngine;

public class DinoControl : MonoBehaviour
{
    private readonly SyncedString orders = new SyncedString("dino orders");
    private readonly SyncedEvent<string> reports = new SyncedEvent<string>("dino reports");

    public static string Orders = "";
    public static int Version;
    public static readonly Queue<string> Reports = new Queue<string>();      // host: reports from every player, its own too
    static DinoControl live;

    void Awake()
    {
        live = this;
        orders.Changed += OnOrders;
        reports.FiredFromClient += OnReport;
    }

    void OnDestroy() { if (live == this) live = null; }

    void OnOrders(string text) { Orders = text ?? ""; Version++; }

    void OnReport(PuzzlePlayer player, string text) { if (text != null && Reports.Count < 512) Reports.Enqueue(text); }

    // host: send the orders to everyone
    public static void Publish(string text)
    {
        if (live == null) return;
        live.orders.Set(text);
        Orders = text;
        Version++;
    }

    // any PC: tell the host
    public static void Report(string text)
    {
        if (live == null || text == null) return;
        if (PuzzleSDK.IsHost) { if (Reports.Count < 512) Reports.Enqueue(text); }
        else live.reports.Fire(text);
    }

    public static bool Present => live != null;
}
