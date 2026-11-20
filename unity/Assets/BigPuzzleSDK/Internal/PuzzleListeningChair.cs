// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's listening chair: sit in it and you can't hear the outside world, only what comes through the
// headphones. The headphones hear the Directional Mic and any Intercom that list this chair.
[PuzzleInfo("Listen to audio from a directional mic or intercom mic")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleListeningChair : PuzzleIntercomListener {
    [Tooltip("The Directional Mic the sitter hears through the headphones. Several chairs can pick the same one. None = they only hear Intercom Mics that list this chair.")]
    [SerializeField] private PuzzleDirectionalMic microphone;
}

}
