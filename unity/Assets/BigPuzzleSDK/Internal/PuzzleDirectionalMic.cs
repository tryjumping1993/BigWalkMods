// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's studio microphone. A Listening Chair that picks it hears what it hears.
[PuzzleInfo("Stream audio to a listening chair")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleDirectionalMic : MonoBehaviour {
}

}
