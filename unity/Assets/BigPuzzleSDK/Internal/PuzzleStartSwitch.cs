// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's start knob. By default flipping it starts the puzzle (PuzzleSDK.onPuzzleStart); flipping it back
// stops it. Untick Toggle Puzzle State to use it as a plain switch that only fires its own events.
[PuzzleInfo("A switch to toggle things. On or Off event is ran based on state on load.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleStartSwitch : MonoBehaviour {
    // When on (the default; packs from older SDK builds behave as if it were on) flipping this switch starts and stops the puzzle
    [Tooltip("Call PuzzleSDK.OnPuzzleStart / OnPuzzleStop when flipped.")]
    [SerializeField] private bool togglePuzzleState = true;

    [Tooltip("Saves / Loads state between game restarts.")]
    [SerializeField] private bool savesOnReload = false;

    [Tooltip("State the switch starts in. true = on")]
    [SerializeField] private bool defaultState = false;

    // Fire whenever the switch is flipped, whether or not Toggle Puzzle State is ticked
    [SerializeField] private UnityEvent onTurnedOn;
    [SerializeField] private UnityEvent onTurnedOff;

    // Flips the switch on / off from a script. Host only: the knob moves for every player. With Toggle Puzzle State ticked this
    // also starts / stops the puzzle, like a player flipping it. callFunc: also fire On Turned On / On Turned Off.
    public void TurnOn(bool callFunc = true) { }
    public void TurnOff(bool callFunc = true) { }

    // Read-only for scripts
    public bool IsOn => false;
}

}
