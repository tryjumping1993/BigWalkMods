// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Which of the game's timer ball dispensers to use
public enum PuzzleTimerSpawnerStyle { Obby, Cannon }

// The game's timer ball dispenser (Obby). Picking up the ball starts its countdown; when it runs
// out the ball goes back into the dispenser, ready to be taken again.
[PuzzleInfo("The game's timer ball dispenser.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTimerSpawner : MonoBehaviour {
    [Tooltip("Obby: the dispenser from the Obby. Cannon: the pitching cannon with a poke button (from the secret zone).")]
    [SerializeField] private PuzzleTimerSpawnerStyle style = PuzzleTimerSpawnerStyle.Obby;

    [Tooltip("Cannon only: how hard the ball is shot out, in metres per second. 5 is a gentle toss; some of the game's cannons shoot much harder.")]
    [Min(0f)]
    [SerializeField] private float throwStrength = 5f;

    [Tooltip("Seconds the timer ball lasts. When it runs out the ball goes back into the dispenser, so this is also the wait before a new one can be taken. The game's is 10.")]
    [Min(0.5f)]
    [SerializeField] private float time = 10f;
}

}
