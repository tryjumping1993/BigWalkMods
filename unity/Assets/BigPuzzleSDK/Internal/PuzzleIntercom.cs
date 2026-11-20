// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// The game's wall microphone. Players hold it to talk to the intercom speakers listed on it.
[PuzzleInfo("Hold to talk. Can stream audio to Intercom Speaker or Listening Chair.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleIntercom : MonoBehaviour {
    [Tooltip("Where this microphone plays to: Intercom Speakers, or Listening Chairs (through their headphones). Empty = it plays nowhere. Each can be listed on several microphones.")]
    [SerializeField] private List<PuzzleIntercomListener> speakers = new List<PuzzleIntercomListener>();
}

}
