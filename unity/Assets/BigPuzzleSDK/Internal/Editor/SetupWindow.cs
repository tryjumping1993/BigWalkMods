// BigPuzzleSDK: Tools > Big Puzzle SDK > Setup. Import Big Walk's models (the editor previews use them,
// read from your own copy of the game into this project's Library folder, never into Assets), and set up
// the folder your puzzle's own scripts go in.
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK.GameAssets {

public class SetupWindow : EditorWindow {
    private string path;
    private bool importingModels, importingMap;

    [MenuItem("Tools/Big Puzzle SDK/Setup", false, 1)]
    public static void Open() {
        var window = GetWindow<SetupWindow>(true, "Setup");
        window.minSize = new Vector2(460, 400);
    }

    private void OnEnable() {
        path = BigWalkGameModels.SavedOrDefaultPath();
    }

    private void OnGUI() {
        GUILayout.Label("Setup", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
        EditorGUILayout.Space();
        GUILayout.Label("Import models", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Shows the game's models in editor. They're read from your own copy of Big Walk and are not exported with your puzzles.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Big Walk's data.unity3d");
        EditorGUILayout.BeginHorizontal();
        path = EditorGUILayout.TextField(path);
        if (GUILayout.Button("Browse...", GUILayout.Width(80))) {
            string picked = BigWalkGameModels.Browse(path);
            if (picked != null) { path = picked; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Reset to Steam default", EditorStyles.miniButton, GUILayout.Width(150))) {
            path = BigWalkGameModels.DefaultGamePath;
            GUI.FocusControl(null);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(importingModels)) {
            if (GUILayout.Button(importingModels ? "Importing..." : "Import Components", GUILayout.Height(30))) {
                importingModels = true;
                Repaint();
                EditorApplication.delayCall += () => {
                    try {
                        DoImport();
                        if (BigWalkGameModels.Ready) BigWalkMapImporter.AutoImportFirstTime();      // the map comes along with the models, the first time only
                    }
                    finally { importingModels = false; Repaint(); }
                };   // delayed so "Importing..." is drawn first
            }
        }
        using (new EditorGUI.DisabledScope(importingMap)) {
            if (GUILayout.Button(new GUIContent(importingMap ? "Importing..." : "Import Map", "Builds a reference copy of Big Walk's whole world (terrain and objects) in the scene, to place puzzles against. Not saved with the scene; never exported."), GUILayout.Height(30))) {
                string mapPath = path;
                importingMap = true;
                Repaint();
                EditorApplication.delayCall += () => {
                    try { BigWalkMapImporter.Import(mapPath); }
                    finally { importingMap = false; Repaint(); }
                };   // delayed so "Importing..." is drawn first
            }
        }

        EditorGUILayout.Space(14);
        GUILayout.Label("Setup scripts folder", EditorStyles.boldLabel);
        bool exists = ScriptsFolderSetup.Exists;
        using (new EditorGUI.DisabledScope(exists)) {
            if (GUILayout.Button(exists ? "Scripts folder is set up" : "Set up scripts folder", GUILayout.Height(30)))
                ScriptsFolderSetup.Create(true);
        }
        EditorGUILayout.LabelField(
            "Custom scripts go in " + ScriptsFolderSetup.FolderPath + " with an assembly definition.",
            EditorStyles.wordWrappedMiniLabel);
    }

    private void DoImport() {
        if (BigWalkGameModels.Import(path)) return;
        // Not there (or not readable): ask where it is
        bool found = BigWalkGameModels.Resolve(path) != null;
        string message = found
            ? "Big Walk's models couldn't be read from:\n" + path + "\n\nSee the Console for details. Pick a different data.unity3d?"
            : "Big Walk wasn't found at:\n" + path + "\n\nPick its data.unity3d file (in the 'Big Walk_Data' folder of the game).";
        if (!EditorUtility.DisplayDialog("Setup", message, "Locate...", "Cancel")) return;
        string picked = BigWalkGameModels.Browse(path);
        if (picked == null) return;
        path = picked;
        if (!BigWalkGameModels.Import(path))
            EditorUtility.DisplayDialog("Setup", "That isn't Big Walk's data.unity3d, or it couldn't be read. See the Console for details.", "OK");
    }
}

}
