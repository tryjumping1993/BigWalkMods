// BigPuzzleSDK: the note shown on the BigWalkMapLock component (see BigWalkMapLock.cs)
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK {

[CustomEditor(typeof(BigWalkMapLock))]
public class BigWalkMapLockEditor : Editor {
    public override void OnInspectorGUI() {
        EditorGUILayout.HelpBox("Editing anything here does NOT save and does NOT apply in game.\n\n" +
            "Build your puzzle in a different scene.", MessageType.Info);
    }
}

}
