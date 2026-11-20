// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's "live" lamp. By default it lights by itself while the puzzle is running.
[PuzzleInfo("Lights up while the puzzle is running (a Start Switch is on). Untick Toggle With Puzzle State to control it yourself with TurnOn() / TurnOff().")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleLiveSignal : MonoBehaviour {
    // When on (the default; packs from older SDK builds behave as if it were on) the lamp follows the puzzle's running state
    [SerializeField] private bool toggleWithPuzzleState = true;

    // Lights the lamp. Host only: it reaches every player.
    public void TurnOn() { }

    // Puts the lamp out. Host only.
    public void TurnOff() { }

    // Read-only for scripts
    public bool IsOn => false;
}

}
