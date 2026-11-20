// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's light switch.
[PuzzleInfo("A switch to toggle things. On or Off event is ran based on state on load.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleSwitch : MonoBehaviour {
    [Tooltip("Saves / Loads state between game restarts.")]
    [SerializeField] private bool savesOnReload = false;

    [Tooltip("State the switch starts in. true = on")]
    [SerializeField] private bool defaultState = false;

    [SerializeField] private UnityEvent onTurnedOn;
    [SerializeField] private UnityEvent onTurnedOff;

    // Flips the switch from a script. Host only: it moves for every player. callFunc: also fire On Turned On / On Turned Off.
    public void TurnOn(bool callFunc = true) { }
    public void TurnOff(bool callFunc = true) { }

    // Read-only for scripts
    public bool IsOn => false;
}

}
