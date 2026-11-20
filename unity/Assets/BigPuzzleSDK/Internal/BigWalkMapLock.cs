#if UNITY_EDITOR
// BigPuzzleSDK: sits on the "BigWalk Map" object and keeps people from clicking, dragging or moving the preview in the
// Scene view, because changing it does nothing in the game. Turn the component off or remove it to unlock the map (to move things around
// in the editor anyway). The map is never saved, so this is only a speed bump: Import Map builds it all again.
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK {

[ExecuteAlways, DisallowMultipleComponent]
public class BigWalkMapLock : MonoBehaviour {
    private void OnEnable() { SetLocked(true); }
    private void OnDisable() { SetLocked(false); }

    private void SetLocked(bool locked) {
        if (this == null) return;
        GameObject root = gameObject;
        if (root == null) return;
        // Scene view: the same switch as the pick icons on the left of the Hierarchy
        if (locked) SceneVisibilityManager.instance.DisablePicking(root, true);
        else SceneVisibilityManager.instance.EnablePicking(root, true);
        // Inspector: Transforms greyed out (the on/off checkbox on each object still works)
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
            if (t == null) continue;
            if (locked) t.hideFlags |= HideFlags.NotEditable;
            else t.hideFlags &= ~HideFlags.NotEditable;
        }
    }
}

}
#endif
