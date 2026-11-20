// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's wall speaker that plays what is said into an intercom.
// Which microphones play to it is set on each Intercom (its Speakers list).
[PuzzleInfo("Play audio from an intercom mic")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleIntercomSpeaker : PuzzleIntercomListener {
}

}
