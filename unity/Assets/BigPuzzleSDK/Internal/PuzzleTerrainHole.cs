// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Cuts a hole in the world's Terrain wherever this object's collider(s) touch it, and removes the
// trees and rocks they overlap, so a puzzle can sit underground. Everything is put back when the
// puzzle unloads. The collider becomes an invisible trigger and any renderer here is hidden.
[PuzzleInfo("Cuts a hole in the terrain where its collider touches it, and removes the trees and rocks inside, for underground puzzles.")]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTerrainHole : MonoBehaviour {
    [Tooltip("Cut a hole in the terrain where the collider touches the surface.")]
    [SerializeField] private bool cutHoles = true;
    [Tooltip("Remove the terrain's trees that the collider reaches.")]
    [SerializeField] private bool removeTrees = true;
    [Tooltip("Hide separate rock objects the collider overlaps (found by name: rock, boulder, stone, cliff).")]
    [SerializeField] private bool removeRocks = true;
    [Tooltip("Remove the terrain's shrubs and grass (its detail layers) that the collider reaches. Cut Holes also hides them inside the hole.")]
    [SerializeField] private bool removeShrubs = true;

    private void OnDrawGizmos() {
        Gizmos.color = new Color(0.55f, 0.35f, 0.1f, 0.3f);
        PuzzleZoneGizmo.Draw(this);
    }
}

}
