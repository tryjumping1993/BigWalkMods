// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's turnstile: players pass through one at a time. With Blind Mask on it is the
// blindfold catwalk's turnstile instead of the plain one.
[PuzzleInfo("The game's turnstile. Lock() slides the red slat across so nobody can go through; Unlock() slides it back.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleNoItemsGate : MonoBehaviour {
    [Tooltip("On: players can only pass from the front to the back (red plaques). Off: both ways (green plaques).")]
    [SerializeField] private bool oneWay = true;

    [Tooltip("Off: the plain turnstile (Poet and Priest). On: the blindfold catwalk's turnstile.")]
    [SerializeField] private bool blindMask;

    [Tooltip("Starts locked: the slat is across the gate until Unlock() is called.")]
    [SerializeField] private bool defaultLocked = false;

    [Tooltip("A player started going through the gate in the direction a one-way gate points (front to back). Fires when they click to use it. Host fires it.")]
    [SerializeField] private UnityEvent onEnter;

    [Tooltip("A player started going through the gate the other way (back to front, two-way gates only). Fires when they click to use it.")]
    [SerializeField] private UnityEvent onExit;

    // Read-only for scripts: whether the gate is locked right now (the same on every PC)
    public bool IsLocked => false;

    // Host only
    public void Lock() { }
    public void Unlock() { }
}

}
