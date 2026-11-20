// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's image sets.
public enum PuzzleImageSet { A, B, C, D, Scout, Circle, Pontiff, Camo, LargeA, Custom }

// How many flaps the display has (the game's 1-, 2-, 3- and 5-slot frames)
internal enum PuzzleDisplaySize { One, Two, Three, Five }

// The game's split-flap image display.
[PuzzleInfo("The game's split-flap image display. A Peg Board can use it as its answer (Answer From). When the puzzle starts it takes random pictures and goes blank when it stops, unless Random On Start is unticked.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleDisplay : MonoBehaviour {
    [Tooltip("How many flaps the frame has: 1, 2, 3 or 5. Each flap shows its own picture, numbered from the left (flap 1 is the far left). A Peg Board picks which flap each of its slots follows. Ignored when Copy Display is set: a copy always has the size of the display it follows.")]
    [SerializeField] private PuzzleDisplaySize size = PuzzleDisplaySize.One;

    [Tooltip("On (the default): when the puzzle starts, each flap takes a random Peg Tile and the display goes blank when the puzzle stops. Off: the display is left alone, and only SetImage / Clear change it. A display that copies another ignores this.")]
    [SerializeField] private bool randomOnStart = true;

    [Tooltip("Optional. Always shows whatever the other display shows, with the same flip. It doesn't take a tile of its own.")]
    [SerializeField] private PuzzleDisplay copyDisplay;

    // Read-only for scripts: the image number being shown on flap 1 (0 = blank)
    public int Image => 0;

    // Read-only for scripts: the image number being shown on a flap (1 is the far left; 0 = blank)
    public int GetImage(int flap) { return 0; }

    // Read-only for scripts: the set being shown
    public PuzzleImageSet ImageSet => PuzzleImageSet.A;

    // Read-only for scripts: how many flaps the display has (1, 2, 3 or 5)
    public int FlapCount => size == PuzzleDisplaySize.Two ? 2 : size == PuzzleDisplaySize.Three ? 3 : size == PuzzleDisplaySize.Five ? 5 : 1;

    // Read-only for scripts: the set shown on a flap (1 is the far left)
    public PuzzleImageSet GetImageSet(int flap) { return PuzzleImageSet.A; }

    // Host only. Flips a flap (1 is the far left) to an image of the set it shows (0 = blank). It flips in the world for every player. A display with Copy Display set does nothing. The puzzle's start deals new pictures and its stop blanks them.
    public void SetImage(int flap, int image) { }

    // Host only. As above, and switches the flap to another of the game's sets (not Custom).
    public void SetImage(int flap, PuzzleImageSet set, int image) { }

    // Host only. Every flap goes blank.
    public void Clear() { }

    // Host only. One flap (1 is the far left) goes blank.
    public void Clear(int flap) { }

}

}
