// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// What a Presence Sensor detects. Must match the mod's enum, in the same order (Player first: it is what every older puzzle uses).
public enum PuzzlePresenceType { Player, AnyItem, SpecificItem }

// A button that players press by standing in it (or by items being inside it). Add a Box (or other) Collider sized to the area.
[PuzzleInfo("Presses when enough players (or items, see Type) are inside this object's collider(s), and releases when too few are left. For players, make the collider at least 2 m tall, starting at the floor. It becomes an invisible trigger in-game. It can be used anywhere a button can, including Press In Order.")]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzlePresenceSensor : PuzzleButtonBase {
    [Tooltip("What it detects. Player: players standing inside. AnyItem: any item a player can pick up (including vanilla items, pegs and gourds). SpecificItem: only the Vanilla Item types and Custom Items you choose below.")]
    [SerializeField] private PuzzlePresenceType type = PuzzlePresenceType.Player;

    [Tooltip("Number of players or items required in the collider for OnPressed to run.")]
    [Min(1)]
    [SerializeField] private int requiredPlayers = 1;

    [Tooltip("SpecificItem only. Which Vanilla Item types count.")]
    [SerializeField] private PuzzleHookAccepts acceptedVanilla = PuzzleHookAccepts.None;

    [Tooltip("SpecificItem only. Custom Items that count.")]
    [SerializeField] private List<PuzzleItem> acceptedCustom = new List<PuzzleItem>();

    [Tooltip("Enough players are inside. Like a button's On Pressed.")]
    [SerializeField] private UnityEvent onPressed;

    [Tooltip("Too few players are left inside. With Required Players 1, that is when the last player leaves.")]
    [SerializeField] private UnityEvent onReleased;

    [Tooltip("Fires once for each player that steps in.")]
    [SerializeField] private UnityEvent onPlayerEntered;

    [Tooltip("Fires once for each player that steps out.")]
    [SerializeField] private UnityEvent onPlayerLeft;

    [Tooltip("AnyItem / SpecificItem: fires once for each item that comes inside, even if it only passes through. Count these for things like 'N hits'.")]
    [SerializeField] private UnityEvent onItemEntered;

    [Tooltip("AnyItem / SpecificItem: fires once for each item that leaves.")]
    [SerializeField] private UnityEvent onItemLeft;

    // Read-only for scripts: players inside (the Player type only)
    public int PlayerCount => 0;

    private void OnDrawGizmos() {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
        PuzzleZoneGizmo.Draw(this);
    }
}


// Draws the colliders of a zone component (Presence Sensor, Radio Dead Zone, Reverb Zone) in the Scene view, in the colour already set
// (Gizmos.color). Each shape is drawn in the collider's own space, so it follows the object's rotation and scale (the world-space
// bounds box does not rotate).
internal static class PuzzleZoneGizmo {
    internal static void Draw(Component owner) {
        foreach (Collider c in owner.GetComponents<Collider>()) {
            Gizmos.matrix = c.transform.localToWorldMatrix;
            if (c is BoxCollider box) Gizmos.DrawCube(box.center, box.size);
            else if (c is SphereCollider sphere) Gizmos.DrawSphere(sphere.center, sphere.radius);
            else if (c is CapsuleCollider capsule) {
                float d = capsule.radius * 2f;
                Vector3 size = capsule.direction == 0 ? new Vector3(capsule.height, d, d) : capsule.direction == 1 ? new Vector3(d, capsule.height, d) : new Vector3(d, d, capsule.height);
                Gizmos.DrawCube(capsule.center, size);
            }
            else if (c is MeshCollider mesh && mesh.sharedMesh != null) Gizmos.DrawMesh(mesh.sharedMesh);
            else { Gizmos.matrix = Matrix4x4.identity; Gizmos.DrawCube(c.bounds.center, c.bounds.size); }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
}
