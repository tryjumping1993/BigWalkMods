// BigPuzzleSDK: the "Import Map" button in Tools > Big Puzzle SDK > Setup. Builds a reference copy of Big Walk's world (terrain and
// every object) from the game's own files, on this computer, so puzzles can be placed against it. The map is never
// saved with the scene, so it is built again whenever the project or a scene is opened (once it has been imported), and it never goes into an exported puzzle.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using BigPuzzleSDK.GameAssets;

namespace BigPuzzleSDK {

[InitializeOnLoad]
public static class BigWalkMapImporter {
    private const string RootName = "BigWalk Map";
    // The preview's meshes are named with this in front, so the exporter can tell a copy of a map object (whose mesh can't be exported) from the user's own
    internal const string MeshPrefix = "BigWalk Map: ";
    private const float SmallSize = 5f;      // objects whose longest side is under this many metres go in the "Small Details" folder
    private const int TerrainStep = 4;      // every 4th height sample (samples are 0.5 m apart): a point every 2 m

    // The map is built on its own just once, the first time the SDK is set up in a project (right after the game's models are first read), so everyone
    // finds it; after that, the Setup window's Import Map button builds it. The marker is in the project's Library folder, so each project gets it once
    private static string AutoMarkerFile { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "BigPuzzleSDK", "MapAutoImported")); } }

    // Once the map has been built in a project it comes back by itself whenever the project or a scene is opened (it isn't saved with the scene, so it would
    // otherwise be gone each time). This file, in the project's Library folder, is what remembers that.
    private static string KeepMarkerFile { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "BigPuzzleSDK", "MapKept")); } }
    private static bool importing;

    // ---- the map's own scene ----

    // The map lives in a scene of its own, "Map", that is open next to the user's scene (an "additive" scene). After it is built it is saved as Map.unity in the
    // SDK's Internal folder (this computer only: it holds the game's meshes, so it is never committed or exported), and from then on it is opened like any other
    // scene, with nothing to import or rebuild. The Small Details folder is saved empty and filled from its saved copy the first time it is switched on.
    private static Scene mapScene;

    // Assets/.../BigPuzzleSDK/Internal/Map.unity (next to the folder this script is in), or null if it can't be worked out
    private static string MapScenePath {
        get {
            foreach (string guid in AssetDatabase.FindAssets("BigWalkMapImporter t:MonoScript")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Editor/BigWalkMapImporter.cs", StringComparison.Ordinal) && path.StartsWith("Assets/", StringComparison.Ordinal))
                    return path.Substring(0, path.Length - "/Editor/BigWalkMapImporter.cs".Length) + "/Map.unity";
            }
            return null;
        }
    }

    // Puts the map in a new, empty scene next to the open one (used while it is being built, and when only the old saved copy is there)
    private static void PlaceMap(GameObject root) {
        if (root == null) return;
        try {
            Scene active = SceneManager.GetActiveScene();
            if (!mapScene.IsValid() || !mapScene.isLoaded) mapScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (active.IsValid() && SceneManager.GetActiveScene() != active) SceneManager.SetActiveScene(active);      // the user's scene stays the one new objects go in
            if (root.scene != mapScene) SceneManager.MoveGameObjectToScene(root, mapScene);
            MoveSceneToTop(mapScene);
        } catch (Exception ex) {
            Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: couldn't use a separate scene (" + ex.Message + ").");
        }
    }

    // The Map scene goes to the top of the Hierarchy's list of scenes when it is added, so it is out of the way of the user's own
    private static void MoveSceneToTop(Scene scene) {
        try {
            if (!scene.IsValid() || SceneManager.sceneCount < 2) return;
            Scene first = SceneManager.GetSceneAt(0);
            if (first != scene) EditorSceneManager.MoveSceneBefore(scene, first);
        } catch (Exception) { }
    }

    private static void CloseMapScene() {
        try {
            if (mapScene.IsValid() && mapScene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(mapScene, true);
        } catch (Exception) { }
        mapScene = default(Scene);
    }

    // Nothing was changed in the scene as far as the user is concerned, so it shouldn't ask to be saved
    private static void ClearDirty(Scene scene) {
        try {
            var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method != null && scene.IsValid()) method.Invoke(null, new object[] { scene });
        } catch (Exception) { }
    }

    // The lock is saved in Map.unity with everything else (it is put on before the scene is first saved). This also covers a map saved without one.
    private static BigWalkMapLock AddLock(GameObject root) {
        BigWalkMapLock l = root.GetComponent<BigWalkMapLock>();
        if (l == null) {
            l = root.AddComponent<BigWalkMapLock>();
            ClearDirty(root.scene);
        }
        return l;
    }

    // Takes the "don't save" mark off the map so it is saved with its scene (the meshes, materials and textures are saved inside the scene file)
    private static void MakeSaveable(GameObject root) {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
            t.gameObject.hideFlags = HideFlags.None;
            foreach (MeshFilter mf in t.GetComponents<MeshFilter>()) if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh)) mf.sharedMesh.hideFlags = HideFlags.None;
            foreach (Renderer r in t.GetComponents<Renderer>())
                foreach (Material m in r.sharedMaterials) {
                    if (m == null) continue;
                    if (!EditorUtility.IsPersistent(m)) m.hideFlags = HideFlags.None;
                    foreach (string name in m.GetTexturePropertyNames()) {
                        Texture tex = m.GetTexture(name);
                        if (tex != null && !EditorUtility.IsPersistent(tex)) tex.hideFlags = HideFlags.None;
                    }
                }
        }
    }

    // Saves the whole built map, Small Details included (that folder is switched off, so switching it on is all it takes), as Map.unity.
    // If that can't be done there is simply no saved scene.
    private static void SaveMapScene(GameObject root) {
        string path = MapScenePath;
        if (path == null || !mapScene.IsValid()) return;
        try {
            AddLock(root);
            MakeSaveable(root);
            if (!EditorSceneManager.SaveScene(mapScene, path)) throw new Exception("Unity couldn't save " + path);
        } catch (Exception ex) {
            Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: couldn't save the map as a scene (" + ex.Message + "). It will be built again next time.");
            try { AssetDatabase.DeleteAsset(path); } catch (Exception) { }
        } finally {
            ClearDirty(mapScene);
        }
    }

    // Opens the saved Map.unity next to the open scene. False (with nothing left behind) if there isn't a good one.
    private static bool TryOpenMapScene(string game) {
        string path = MapScenePath;
        try {
            if (path == null || !System.IO.File.Exists(path) || !System.IO.File.Exists(CacheKeyFile) || System.IO.File.ReadAllText(CacheKeyFile) != CacheKey(game)) return false;
        } catch (Exception) { return false; }
        if (importing) return false;
        importing = true;
        try {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Remove();
            Scene active = SceneManager.GetActiveScene();
            Scene opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (active.IsValid() && SceneManager.GetActiveScene() != active) SceneManager.SetActiveScene(active);
            GameObject root = null;
            foreach (GameObject g in opened.GetRootGameObjects()) if (g.name == RootName) { root = g; break; }
            if (root == null) { EditorSceneManager.CloseScene(opened, true); return false; }
            mapScene = opened;
            MoveSceneToTop(opened);
            mapRoot = root;
            smallFolder = root.transform.Find(SmallFolderName);
            AddLock(root);
            ApplySky();
            Debug.Log("[BigPuzzleSDK] BigWalk Map opened in " + timer.ElapsedMilliseconds + " ms (Small Details load when you switch that folder on).");
            return true;
        } catch (Exception) {
            Remove();
            return false;
        } finally {
            importing = false; Quiet();
        }
    }

    // ---- keeping the user's own objects out of the map ----

    // Anything created in the Map scene, or dragged into it from another scene, is taken back out straight away with an explanation: it is reference only,
    // and puzzles belong in another scene (anything put here would be lost with the map). Changes the SDK makes itself (building or loading the map) are
    // ignored: those objects are marked "don't save", and for a few seconds after each build or load nothing is checked, as the changes are only reported a bit later.
    private static double quietUntil;
    private static bool rejecting;
    private static void Quiet() { quietUntil = EditorApplication.timeSinceStartup + 5.0; }

#if UNITY_2021_2_OR_NEWER
    private static List<int> strayCreated = new List<int>();
    private static List<int> strayMoved = new List<int>();

    private static void WatchMap(ref ObjectChangeEventStream stream) {
        if (importing || loadingSmall || rejecting || EditorApplication.timeSinceStartup < quietUntil || !mapScene.IsValid() || !mapScene.isLoaded) return;
        bool found = false;
        for (int i = 0; i < stream.length; i++) {
            ObjectChangeKind kind = stream.GetEventType(i);
            if (kind == ObjectChangeKind.CreateGameObjectHierarchy) {
                stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs e);
                if (e.scene != mapScene) continue;
                var go = EditorUtility.InstanceIDToObject(e.instanceId) as GameObject;
                if (go == null || (go.hideFlags & HideFlags.DontSave) != 0) continue;
                strayCreated.Add(e.instanceId);
                found = true;
            } else if (kind == ObjectChangeKind.ChangeGameObjectParent) {
                stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs e);
                if (e.newScene != mapScene || e.previousScene == mapScene) continue;
                strayMoved.Add(e.instanceId);
                found = true;
            }
        }
        if (!found) return;
        rejecting = true;
        EditorApplication.delayCall += RejectStrays;      // not in the middle of the editor's own update
    }

    private static void RejectStrays() {
        try {
            // Dragged in from another scene: undo the drag, and if that didn't take it out, put it back in the open scene
            if (strayMoved.Count > 0) {
                Undo.PerformUndo();
                foreach (int id in strayMoved) {
                    var go = EditorUtility.InstanceIDToObject(id) as GameObject;
                    Scene active = SceneManager.GetActiveScene();
                    if (go != null && go.scene == mapScene && active.IsValid() && active != mapScene) { go.transform.SetParent(null, false); SceneManager.MoveGameObjectToScene(go, active); }
                }
            }
            // Made in the Map scene: removed
            foreach (int id in strayCreated) {
                var go = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (go != null && go.scene == mapScene) UnityEngine.Object.DestroyImmediate(go);
            }
        } catch (Exception) {
        } finally {
            strayCreated.Clear();
            strayMoved.Clear();
            ClearDirty(mapScene);
            Quiet();
            rejecting = false;
        }
        EditorUtility.DisplayDialog("BigWalk Map", "The BigWalk Map is for reference only, so nothing can be added to it. What you just added has been removed.\n\nUse a different scene for your puzzle.", "OK");
    }
#endif

    // The map's root object, even if it has been switched off in the Hierarchy
    private static GameObject FindRoot() {
        if (mapRoot != null && mapRoot.scene.IsValid()) return mapRoot;      // quick: the one found before
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.parent == null && t.name == RootName && t.gameObject.scene.IsValid()) { if (t.gameObject.scene != SceneManager.GetActiveScene()) mapScene = t.gameObject.scene; return t.gameObject; }
        return null;
    }

    private static void EnsureSoon() { EditorApplication.delayCall += Ensure; }

    // Builds the map again if it was built before in this project and the open scene doesn't have it
    private static void Ensure() {
        if (importing || Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(KeepMarkerFile)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Ensure; return; }      // try again when Unity is idle
        GameObject open = FindRoot();
        if (open != null) {      // already there: Unity reopens the scenes that were open last time
            mapRoot = open;
            smallFolder = open.transform.Find(SmallFolderName);
            AddLock(open);
            return;
        }
        string game = BigWalkGameModels.Resolve(BigWalkGameModels.SavedOrDefaultPath()) ?? BigWalkGameModels.AutoDetect();
        if (game == null) return;
        if (TryOpenMapScene(game)) return;
        if (!TryLoadCache(game)) Import(game);      // the saved copy if there is a good one, otherwise a fresh read of the game's files
    }

    // ---- the saved copy ----

    // After a map is built, a copy of it is saved to a file in the project's Library folder (which is local to this computer and never committed or exported).
    // Opening the project or a scene then loads that copy, which is much quicker than reading the game's files again. The Import Map button always reads
    // the game's files afresh (and saves a new copy). The copy is only used if it was made from this very game file and by this version of the SDK's code.
    private const string CacheVersion = "4";      // change this whenever the way the map is built changes, so every saved copy is built again
    private static string CacheFile { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "BigPuzzleSDK", "BigWalkMap.cache")); } }
    private static string CacheKeyFile { get { return CacheFile + ".key"; } }

    private static string CacheKey(string game) {
        string key = CacheVersion + "|" + game;
        try { var f = new System.IO.FileInfo(game); if (f.Exists) key += "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks; } catch (Exception) { }
        return key;
    }

    private static void DeleteCache() {
        try { if (System.IO.File.Exists(CacheFile)) System.IO.File.Delete(CacheFile); if (System.IO.File.Exists(CacheKeyFile)) System.IO.File.Delete(CacheKeyFile); } catch (Exception) { }
    }

    private const string SmallFolderName = "Small Details";
    private static string SmallCacheFile { get { return CacheFile + ".small"; } }
    private static GameObject mapRoot;           // the map in the open scene (found again after a recompile)
    private static Transform smallFolder;        // its Small Details folder: empty until it is switched on (when the map was loaded from the saved copy)
    private static bool loadingSmall;
    private static bool smallFailed;           // the small details couldn't be loaded: not tried again until the map is imported again

    private static void DeleteSmallCache() {
        try { if (System.IO.File.Exists(SmallCacheFile)) System.IO.File.Delete(SmallCacheFile); } catch (Exception) { }
    }

    // Saves the built map to two files: the map without the contents of its Small Details folder (loaded whenever the project or a scene is opened), and those
    // contents (loaded the first time the folder is switched on; they are most of the objects, and it starts off). Neither includes the lock component.
    // Anything that goes wrong just means there are no saved copies. Each object's "don't save" mark is stored with it, so the loaded copy comes back marked.
    private static void SaveCache(GameObject root, Transform small, string game) {
        int index = small.GetSiblingIndex();
        GameObject holder = null;
        try {
            DeleteCache();
            DeleteSmallCache();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CacheFile));
            small.SetParent(null, false);      // the folder and what is in it, on their own
            SaveObjects(small.gameObject, SmallCacheFile);
            holder = new GameObject(SmallFolderName) { hideFlags = HideFlags.DontSave };      // an empty folder in its place
            holder.transform.SetParent(root.transform, false);
            holder.transform.SetSiblingIndex(index);
            holder.SetActive(false);
            SaveObjects(root, CacheFile);
            System.IO.File.WriteAllText(CacheKeyFile, CacheKey(game));
        } catch (Exception) {
            DeleteCache();
            DeleteSmallCache();
        } finally {
            if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
            small.SetParent(root.transform, false);
            small.SetSiblingIndex(index);
        }
    }

    private static void SaveObjects(GameObject top, string file) {
        var objects = new List<UnityEngine.Object>();
        foreach (UnityEngine.Object o in EditorUtility.CollectDependencies(new UnityEngine.Object[] { top }))
            if (o != null && !EditorUtility.IsPersistent(o)) objects.Add(o);
        string temp = file + ".tmp";
        UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(objects.ToArray(), temp, false);
        if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
        System.IO.File.Move(temp, file);
    }

    // A copy, in the open scene, of the object with this name from a cache file (it shares the loaded meshes and materials)
    private static GameObject LoadFromCache(string file, string name) {
        UnityEngine.Object[] loaded = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(file);
        GameObject source = null;
        foreach (UnityEngine.Object o in loaded) {
            var g = o as GameObject;
            if (g != null && g.name == name && g.transform.parent == null) { source = g; break; }
        }
        if (source == null) throw new Exception("The saved map has no " + name + ".");
        GameObject made = UnityEngine.Object.Instantiate(source);
        made.name = name;
        foreach (UnityEngine.Object o in loaded) {
            var g = o as GameObject;
            if (g != null && g != made && g.transform.parent == null) UnityEngine.Object.DestroyImmediate(g);
        }
        try {
            // Nothing in the copy may ever be saved with the scene. The marks come back with the file; if they somehow didn't, they are put on again.
            made.hideFlags = HideFlags.DontSave;
            Transform[] parts = made.GetComponentsInChildren<Transform>(true);
            if (parts.Length > 2 && (parts[parts.Length / 2].gameObject.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) {
                foreach (Transform t in parts) t.gameObject.hideFlags = HideFlags.DontSave;
                foreach (MeshFilter mf in made.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh)) mf.sharedMesh.hideFlags = HideFlags.DontSave;
                foreach (Renderer r in made.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials) if (m != null && !EditorUtility.IsPersistent(m)) m.hideFlags = HideFlags.DontSave;
            }
            // A quick check that the meshes came with it
            int meshes = 0;
            foreach (MeshFilter mf in made.GetComponentsInChildren<MeshFilter>(true)) {
                if (mf.sharedMesh == null) throw new Exception("The saved map is missing a mesh.");
                if (++meshes >= 200) break;
            }
            if (meshes == 0 && name == RootName) throw new Exception("The saved map has no meshes.");
        } catch (Exception) {
            UnityEngine.Object.DestroyImmediate(made);      // not left behind in the open scene
            throw;
        }
        return made;
    }

    // Puts the saved copy in the scene. False (with nothing left behind) if there isn't a good one.
    private static bool TryLoadCache(string game) {
        try {
            if (!System.IO.File.Exists(CacheFile) || !System.IO.File.Exists(CacheKeyFile) || System.IO.File.ReadAllText(CacheKeyFile) != CacheKey(game)) return false;
        } catch (Exception) { return false; }
        if (importing) return false;
        importing = true;
        try {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            EditorUtility.DisplayProgressBar("BigWalk Map", "Loading the map...", 0.5f);
            Remove();
            GameObject made = LoadFromCache(CacheFile, RootName);
            PlaceMap(made);
            AddLock(made);
            mapRoot = made;
            smallFolder = made.transform.Find(SmallFolderName);
            ApplySky();
            Debug.Log("[BigPuzzleSDK] BigWalk Map loaded from the saved copy in " + timer.ElapsedMilliseconds + " ms" + " (unsaved copy in its own scene)" + " (Small Details load when you switch that folder on).");
            return true;
        } catch (Exception) {
            Remove();
            DeleteCache();      // it will be built afresh, and saved again
            DeleteSmallCache();
            return false;
        } finally {
            importing = false; Quiet();
            EditorUtility.ClearProgressBar();
        }
    }

    // When the Small Details folder of a loaded map is switched on and is still empty, its contents are loaded from the saved copy
    private static void CheckSmallDetails() {
        if (loadingSmall || smallFailed || importing || smallFolder == null || mapRoot == null || smallFolder.childCount > 0 || !smallFolder.gameObject.activeSelf) return;
        loadingSmall = true;
        EditorApplication.delayCall += LoadSmallDetails;      // not in the middle of the Hierarchy's own update
    }

    private static void LoadSmallDetails() {
        try {
            if (smallFolder == null || mapRoot == null || smallFolder.childCount > 0) return;
            string game = BigWalkGameModels.Resolve(BigWalkGameModels.SavedOrDefaultPath()) ?? BigWalkGameModels.AutoDetect();
            if (game == null || !System.IO.File.Exists(SmallCacheFile) || !System.IO.File.Exists(CacheKeyFile) || System.IO.File.ReadAllText(CacheKeyFile) != CacheKey(game)) {
                smallFailed = true;
                Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: the small details aren't saved for this map. Use Import Map in the Setup window to build them.");
                return;
            }
            EditorUtility.DisplayProgressBar("BigWalk Map", "Loading the small details...", 0.5f);
            GameObject made = LoadFromCache(SmallCacheFile, SmallFolderName);
            int index = smallFolder.GetSiblingIndex();
            UnityEngine.Object.DestroyImmediate(smallFolder.gameObject);
            if (made.scene != mapRoot.scene) SceneManager.MoveGameObjectToScene(made, mapRoot.scene);
            made.transform.SetParent(mapRoot.transform, false);
            made.transform.SetSiblingIndex(index);
            made.SetActive(true);
            smallFolder = made.transform;
            BigWalkMapLock l = mapRoot.GetComponent<BigWalkMapLock>();
            if (l != null && l.enabled) { l.enabled = false; l.enabled = true; }      // lock the new objects too
            ClearDirty(mapRoot.scene);
        } catch (Exception ex) {
            smallFailed = true;
            Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: couldn't load the small details (" + ex.Message + "). Use Import Map in the Setup window to build them.");
        } finally {
            loadingSmall = false; Quiet();
            EditorUtility.ClearProgressBar();
        }
    }

    internal static void AutoImportFirstTime() {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || System.IO.File.Exists(AutoMarkerFile)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoImportFirstTime; return; }      // try again when Unity is idle
        string game = BigWalkGameModels.Resolve(BigWalkGameModels.SavedOrDefaultPath()) ?? BigWalkGameModels.AutoDetect();
        if (game == null) return;      // no game yet: try again next time, once the Setup window has found it
        try {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AutoMarkerFile));
            System.IO.File.WriteAllText(AutoMarkerFile, "The BigWalk map was built automatically once for this project. Delete this file to have it do that again.");
        } catch (Exception) { return; }      // can't remember it: better not to rebuild the map on every start
        if (FindRoot() == null) Import(game);
    }

    static BigWalkMapImporter() {
        EditorApplication.delayCall += () => EditorApplication.delayCall += Ensure;      // when Unity starts (after the models have loaded)
        EditorApplication.hierarchyChanged += CheckSmallDetails;
        EditorApplication.delayCall += () => { mapRoot = FindRoot(); smallFolder = mapRoot != null ? mapRoot.transform.Find(SmallFolderName) : null; };      // after a recompile
#if UNITY_2021_2_OR_NEWER
        ObjectChangeEvents.changesPublished += WatchMap;
#endif
        EditorSceneManager.sceneOpened += (scene, mode) => EnsureSoon();
        EditorSceneManager.newSceneCreated += (scene, setup, mode) => EnsureSoon();
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EnsureSoon(); };
        EditorApplication.delayCall += () => EditorApplication.delayCall += AutoImportFirstTime;      // after the models have loaded
    }

    // From the Setup window: builds the map from the game at this path (the one in the Setup window)
    public static void Import(string gamePath) {
        string game = BigWalkGameModels.Resolve(gamePath) ?? BigWalkGameModels.Resolve(BigWalkGameModels.SavedOrDefaultPath()) ?? BigWalkGameModels.AutoDetect();
        if (game == null) {
            EditorUtility.DisplayDialog("BigWalk Map", "Big Walk wasn't found. Point the Setup window at the game's data.unity3d first.", "OK");
            return;
        }

        if (importing) return;
        importing = true;
        Remove();
        GameObject root = new GameObject(RootName);
        root.hideFlags = HideFlags.DontSave;
        PlaceMap(root);
        try {
            EditorUtility.DisplayProgressBar("BigWalk Map", "Opening the game's data...", 0f);
            using (var assets = new GameAssets.GameAssets(game, (what, p) => EditorUtility.DisplayProgressBar("BigWalk Map", what, p * 0.3f))) {
                List<WorldTerrain> terrains;
                List<WorldMeshObject> objects;
                Func<string, Color> materialColor;
                Dictionary<long, WorldNode> nodes;
                Func<string, Texture2D> materialTexture;
                BigWalkWorld.Read(assets, (what, p) => EditorUtility.DisplayProgressBar("BigWalk Map", what, 0.3f + p * 0.4f), out terrains, out objects, out nodes, out materialColor, out materialTexture);
                var big = new GameObject("Big parts") { hideFlags = HideFlags.DontSave };
                big.transform.SetParent(root.transform, false);
                var small = new GameObject("Small Details") { hideFlags = HideFlags.DontSave };
                small.transform.SetParent(root.transform, false);
                foreach (WorldTerrain t in terrains) BuildTerrain(t, big.transform, TerrainStep);
                var floor = new GameObject("Death Floor") { hideFlags = HideFlags.DontSave };
                floor.transform.SetParent(root.transform, false);
                int built = BuildObjects(objects, nodes, big.transform, small.transform, floor.transform, materialColor, materialTexture);
                floor.SetActive(false);      // off to start with: it's ugly, but switch it on to see where players die when building underground
                small.SetActive(false);      // off to start with: switch it on in the Hierarchy to see them
                // Debug.Log("[BigWalk map] " + BigWalkWorld.LastReport + " Built " + built + ".");
                SaveCache(root, small.transform, game);      // the small details, and a fallback copy
                SaveMapScene(root);      // Map.unity: opened like any scene from now on
                mapRoot = root;
                smallFolder = small.transform;
                AddLock(root);      // explains the map is reference only and locks it; remove or turn off to unlock
                ApplySky();
                try {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(KeepMarkerFile));
                    System.IO.File.WriteAllText(KeepMarkerFile, "The BigWalk map was built in this project, so it is built again whenever the project or a scene is opened. Delete this file to stop that.");
                } catch (Exception) { /* it just won't come back by itself */ }
                Debug.Log("[BigPuzzleSDK] BigWalk Map imported: " + terrains.Count + " terrain(s) and " + built + " object(s).");
            }
        } catch (GameAssetException ex) {
            Remove();
            Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: couldn't read the game's world: " + ex.Message);
        } catch (Exception ex) {
            Remove();
            Debug.LogException(ex);
        } finally {
            importing = false; Quiet();
            EditorUtility.ClearProgressBar();
        }
    }

    // ---- sky ----

    // The scene's sky and lighting before the map changed them, so they can be put back
    private static bool skyChanged;
    private static Material oldSkybox;
    private static UnityEngine.Rendering.AmbientMode oldAmbientMode;
    private static Color oldSky, oldEquator, oldGround;
    private static Material skyMaterial;

    // The game draws its sky with a time-of-day system (Enviro) that runs while the game plays and isn't something Unity can copy, so this
    // gives the scene a similar clear daytime sky and the game's ambient light colours (blue from above, brown from the ground)
    private static void ApplySky() {
        try {
            Shader custom = Shader.Find("Hidden/BigPuzzleSDK/MapSky");
            Shader shader = custom != null ? custom : Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            if (!skyChanged) {
                oldSkybox = RenderSettings.skybox;
                oldAmbientMode = RenderSettings.ambientMode;
                oldSky = RenderSettings.ambientSkyColor;
                oldEquator = RenderSettings.ambientEquatorColor;
                oldGround = RenderSettings.ambientGroundColor;
                skyChanged = true;
            }
            if (skyMaterial == null || skyMaterial.shader != shader) skyMaterial = new Material(shader) { name = "BigWalk Map sky", hideFlags = HideFlags.DontSave };
            if (custom == null) {      // the built-in sky, if the custom one isn't there
                skyMaterial.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.8f, 1f));
                skyMaterial.SetColor("_GroundColor", new Color(0.45f, 0.4f, 0.35f, 1f));
                skyMaterial.SetFloat("_SunSize", 0.04f);
                skyMaterial.SetFloat("_AtmosphereThickness", 1f);
                skyMaterial.SetFloat("_Exposure", 1.2f);
            }
            RenderSettings.skybox = skyMaterial;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.64f, 0.78f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.55f, 0.7f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.5f, 0.4f, 0.35f, 1f);
        } catch (Exception ex) {
            Debug.LogWarning("[BigPuzzleSDK] BigWalk Map: couldn't set the sky: " + ex.Message);
        }
    }

    private static void RestoreSky() {
        if (!skyChanged) return;
        skyChanged = false;
        RenderSettings.skybox = oldSkybox;
        RenderSettings.ambientMode = oldAmbientMode;
        RenderSettings.ambientSkyColor = oldSky;
        RenderSettings.ambientEquatorColor = oldEquator;
        RenderSettings.ambientGroundColor = oldGround;
    }

    public static void Remove() {
        smallFailed = false;
        RestoreSky();
        GameObject old = FindRoot();
        while (old != null) { UnityEngine.Object.DestroyImmediate(old); old = FindRoot(); }
        CloseMapScene();
    }

    // See-through (alpha-blended) for URP/Lit or the built-in Standard shader
    private static void MakeTransparent(Material m) {
        if (m.HasProperty("_Surface")) {                       // URP/Lit
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f);                        // SrcAlpha
            m.SetFloat("_DstBlend", 10f);                       // OneMinusSrcAlpha
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
        } else if (m.HasProperty("_Mode")) {                   // Standard
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", 5);
            m.SetInt("_DstBlend", 10);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.SetOverrideTag("RenderType", "Transparent");
        }
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);          // draw both faces: glass is one flat sheet and is seen from either side
        if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 0f);
        m.renderQueue = 3000;
    }

    private static Material MakeMaterial(Color c, bool vertexColors = false) {
        bool glass = c.a < 0.99f;
        float alpha = c.a;
        if (glass) vertexColors = false;
        Shader shader = null;
        if (vertexColors) { shader = Shader.Find("Hidden/BigPuzzleSDK/MapPreview"); if (shader != null && !shader.isSupported) shader = null; }
        if (shader == null) { vertexColors = false; shader = Shader.Find("Universal Render Pipeline/Lit"); }
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { hideFlags = HideFlags.DontSave };
        // Lamps and other glowing things have HDR tints above 1, which makes the scene view's bloom flare white; keep every colour below that
        // A colour that isn't a number (or is infinite) makes the scene view's post-processing smear white blobs, so it becomes plain grey
        if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b)) c = new Color(0.5f, 0.5f, 0.5f, 1f);
        c = new Color(Mathf.Clamp(c.r, 0f, 8f), Mathf.Clamp(c.g, 0f, 8f), Mathf.Clamp(c.b, 0f, 8f), 1f);
        float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (peak > 0.85f) c = new Color(c.r * 0.85f / peak, c.g * 0.85f / peak, c.b * 0.85f / peak, 1f);
        c.a = 1f;
        // The colours read from the game are as they look on screen (sRGB); in a Linear project shader colours are linear
        if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.linear;
        if (glass) c.a = alpha;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (glass) MakeTransparent(m);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
        // Fully matte, so no highlights or reflections glint while the camera moves
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
        if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
        if (m.HasProperty("_EnvironmentReflections")) { m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
        return m;
    }

    // A rock: its granite texture, projected from three sides in world space, with the game's slightly warm white tint
    private static Material MakeRockMaterial(Texture2D texture) {
        Shader shader = Shader.Find("Hidden/BigPuzzleSDK/MapRock");
        if (shader == null || !shader.isSupported) return MakeMaterial(new Color(0.68f, 0.61f, 0.55f));
        var m = new Material(shader) { hideFlags = HideFlags.DontSave };
        m.SetTexture("_MainTex", texture);
        Color tint = new Color(1f, 0.95f, 0.9f, 1f);
        m.SetColor("_BaseColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint);
        m.SetFloat("_TileSize", 6f);
        return m;
    }

    // ---- terrain ----

    // True if the game's hole map has a hole under this stretch of terrain (the hole map has one cell per height sample, so a quad covers step x step cells)
    private static bool IsHole(WorldTerrain t, int x, int z, int step) {
        if (t.holes == null) return false;
        int r = t.holeResolution, holeCells = 0, total = 0;
        for (int dz = 0; dz < step; dz++)
            for (int dx = 0; dx < step; dx++) {
                int cx = x + dx, cz = z + dz;
                if (cx >= r || cz >= r) continue;
                total++;
                if (t.holes[cz * r + cx] < 128) holeCells++;
            }
        return total > 0 && holeCells * 4 >= total;      // a quarter or more of it is open
    }

    private static void BuildTerrain(WorldTerrain t, Transform parent, int step) {
        int n = (t.resolution - 1) / step, side = n + 1;
        var verts = new Vector3[side * side];
        float heightScale = t.scale.y / 32766f;
        for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++) {
                int x = i * step, z = j * step;
                verts[j * side + i] = new Vector3(t.position.x + x * t.scale.x, t.position.y + t.heights[z * t.resolution + x] * heightScale, t.position.z + z * t.scale.z);
            }
        var tris = new int[n * n * 6];
        int k = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) {
                if (IsHole(t, i * step, j * step, step)) continue;      // the game cut a hole here (tower bases, rocks...)
                int v00 = j * side + i, v10 = v00 + 1, v01 = v00 + side, v11 = v01 + 1;
                tris[k++] = v00; tris[k++] = v01; tris[k++] = v10;
                tris[k++] = v10; tris[k++] = v01; tris[k++] = v11;
            }
        System.Array.Resize(ref tris, k);
        var uvs = new Vector2[side * side];
        for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++) uvs[j * side + i] = new Vector2((float)i / n, (float)j / n);
        var mesh = new Mesh { name = MeshPrefix + t.name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(t.name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        Material material = MakeMaterial(new Color(0.45f, 0.5f, 0.35f));
        if (t.colors != null) {
            var tex = new Texture2D(t.colorResolution, t.colorResolution, TextureFormat.RGBA32, false) { name = t.name + " colours", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(t.colors);
            tex.Apply(false, true);
            material = MakeMaterial(Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
        }
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---- objects ----

    private static int BuildObjects(List<WorldMeshObject> objects, Dictionary<long, WorldNode> nodes, Transform bigParent, Transform smallParent, Transform floorParent, Func<string, Color> materialColor, Func<string, Texture2D> materialTexture) {
        var meshes = new Dictionary<string, Mesh>();
        var built = new Dictionary<string, Transform>();      // the game objects made so far: "b:id" / "s:id" (one tree in each folder)
        var materials = new Dictionary<string, Material>();   // by colour: objects with the same colour share one
        // A game object in the game's own tree: its parents are made first; there is one tree in each of the two folders
        Transform Node(long id, int folder) {      // folder: 0 big parts, 1 small details, 2 death floor
            string key = folder + ":" + id;
            if (built.TryGetValue(key, out Transform made)) return made;
            WorldNode n = nodes[id];
            var gameObject = new GameObject(n.name) { hideFlags = HideFlags.DontSave };
            Transform parent = n.father != 0 && nodes.ContainsKey(n.father) ? Node(n.father, folder) : (folder == 2 ? floorParent : folder == 1 ? smallParent : bigParent);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = n.position;
            gameObject.transform.localRotation = n.rotation;
            gameObject.transform.localScale = n.scale;
            built[key] = gameObject.transform;
            return gameObject.transform;
        }

        var hasColors = new Dictionary<Mesh, bool>();
        var doubleSided = new Dictionary<string, Mesh>();
        var fulls = new Dictionary<Mesh, FullMesh>();
        var placed = new HashSet<string>();                   // the same mesh twice in the same place flickers (two surfaces fight), so only the first stays
        int count = 0, seen = 0, dupes = 0, unreadable = 0, badSlices = 0;
        var unreadableNames = new HashSet<string>();
        foreach (WorldMeshObject o in objects) {
            if (++seen % 500 == 0) EditorUtility.DisplayProgressBar("BigWalk Map", "Building objects... " + count, 0.7f + 0.3f * seen / objects.Count);
            Vector3 pos = o.matrix.GetColumn(3);

            string key = o.generated != null ? "generated#" + o.name : o.builtinMesh != 0 ? "builtin#" + o.builtinMesh : o.mesh.file.name + "#" + o.mesh.pathId;
            Mesh mesh;
            if (!meshes.TryGetValue(key, out mesh)) {
                mesh = null;
                try { mesh = o.generated != null ? ToUnity(o.generated) : o.builtinMesh != 0 ? BuiltinMesh(o.builtinMesh) : ToUnity(MeshReader.Read(o.mesh)); } catch (Exception e) { /* a mesh that can't be read is left out */ if (unreadableNames.Count < 15) unreadableNames.Add((o.mesh != null ? o.mesh.name : "built-in " + o.builtinMesh) + ": " + e.Message); }
                meshes[key] = mesh;
            }
            if (mesh == null) { unreadable++; continue; }
            Quaternion rot = o.matrix.rotation;
            if (!placed.Add(key + "#" + o.firstSubMesh + "+" + o.subMeshCount + "@" + Mathf.RoundToInt(pos.x * 20) + "," + Mathf.RoundToInt(pos.y * 20) + "," + Mathf.RoundToInt(pos.z * 20)
                            + "r" + Mathf.RoundToInt(rot.x * 50) + "," + Mathf.RoundToInt(rot.y * 50) + "," + Mathf.RoundToInt(rot.z * 50) + "," + Mathf.RoundToInt(rot.w * 50))) { dupes++; continue; }

            if (o.subMeshCount > 0) {
                if (!fulls.TryGetValue(mesh, out FullMesh fm))
                    fulls[mesh] = fm = new FullMesh { mesh = mesh, vertices = mesh.vertices, colors = mesh.colors, indices = new int[mesh.subMeshCount][] };
                if (Mathf.Abs(o.worldMatrix.determinant) < 1e-12f) { badSlices++; continue; }
                mesh = Subset(fm, o.firstSubMesh, o.subMeshCount, o.worldMatrix.inverse);
                if (mesh == null) { badSlices++; continue; }
            }
            // Small things (buttons, knobs, wires...) go in their own folder, which is switched off so they don't clutter the map
            Vector3 scale = o.worldMatrix.lossyScale;
            Vector3 extent = mesh.bounds.size;
            float longest = Mathf.Max(Mathf.Abs(extent.x * scale.x), Mathf.Max(Mathf.Abs(extent.y * scale.y), Mathf.Abs(extent.z * scale.z)));
            bool small = o.alwaysSmall || longest < SmallSize;
            // Glass is a single flat sheet that has to be seen from both sides: give its triangles a reversed twin
            if (!o.hasColorOverride && o.materials != null && o.materials.Length > 0) {
                bool[] glass = null;
                for (int m = 0; m < mesh.subMeshCount; m++)
                    if (materialColor(o.materials[Mathf.Min(m, o.materials.Length - 1)]).a < 0.99f) { if (glass == null) glass = new bool[mesh.subMeshCount]; glass[m] = true; }
                if (glass != null) {
                    string dk = mesh.GetInstanceID() + ":";
                    foreach (bool gl in glass) dk += gl ? "1" : "0";
                    if (!doubleSided.TryGetValue(dk, out Mesh twin)) doubleSided[dk] = twin = DoubleSided(mesh, glass);
                    mesh = twin;
                }
            }
            bool vertexColors = hasColors.TryGetValue(mesh, out bool hc) ? hc : (hasColors[mesh] = mesh.colors.Length > 0);
            if (o.hasColorOverride) vertexColors = false;      // the game colours this object itself: draw it in that colour
            var sharedMaterials = new Material[mesh.subMeshCount];
            for (int m = 0; m < sharedMaterials.Length; m++) {
                Color c = o.materials != null && o.materials.Length > 0 ? materialColor(o.materials[Mathf.Min(m, o.materials.Length - 1)]) : new Color(0.7f, 0.7f, 0.7f);
                if (o.hasColorOverride) c = o.colorOverride;
                if (float.IsNaN(c.r + c.g + c.b) || float.IsInfinity(c.r + c.g + c.b)) c = new Color(0.5f, 0.5f, 0.5f, 1f);
                if (o.colorTexture != null) {      // the map room's miniature world: painted with its colour map
                    string tk = "t" + o.colorTexture.GetInstanceID();
                    if (!materials.TryGetValue(tk, out Material tm)) {
                        tm = MakeMaterial(Color.white);
                        if (tm.HasProperty("_BaseMap")) tm.SetTexture("_BaseMap", o.colorTexture);
                        if (tm.HasProperty("_MainTex")) tm.SetTexture("_MainTex", o.colorTexture);
                        materials[tk] = tm;
                    }
                    sharedMaterials[m] = tm;
                    continue;
                }
                string matKey = o.materials != null && o.materials.Length > 0 ? o.materials[Mathf.Min(m, o.materials.Length - 1)] : null;
                Texture2D rockTexture = o.hasColorOverride ? null : materialTexture(matKey);      // the game's rocks: draw them with their granite texture
                if (rockTexture != null) {
                    string rk = "r" + rockTexture.GetInstanceID();
                    if (!materials.TryGetValue(rk, out Material rockMat)) { rockMat = MakeRockMaterial(rockTexture); materials[rk] = rockMat; }
                    sharedMaterials[m] = rockMat;
                    continue;
                }
                string ck = (vertexColors ? "v" : "") + (c.a < 0.99f ? "g" : "") + Mathf.RoundToInt(c.r * 31) + "," + Mathf.RoundToInt(c.g * 31) + "," + Mathf.RoundToInt(c.b * 31);
                Material mat;
                if (!materials.TryGetValue(ck, out mat)) { mat = MakeMaterial(c, vertexColors); materials[ck] = mat; }
                sharedMaterials[m] = mat;
            }

            GameObject go = Node(o.nodeId, o.deathFloor ? 2 : small ? 1 : 0).gameObject;     // the object's own place in the game's tree (it keeps its true position there)
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sharedMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // shadows on this much geometry shimmer as the view moves
            renderer.receiveShadows = false;
            count++;
        }
        // Debug.Log("[BigWalk map] Not built: " + dupes + " duplicates, " + unreadable + " with a mesh that couldn't be read" + (unreadableNames.Count > 0 ? " (" + string.Join("; ", unreadableNames) + ")" : "") + ", " + badSlices + " with a bad batch range.");
        return count;
    }

    // A copy of the mesh where the marked sub-meshes also contain every triangle the other way round (so they show from behind too)
    private static Mesh DoubleSided(Mesh source, bool[] sub) {
        Mesh copy = UnityEngine.Object.Instantiate(source);
        copy.hideFlags = HideFlags.DontSave;
        for (int s = 0; s < sub.Length; s++) {
            if (!sub[s] || copy.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] idx = copy.GetTriangles(s);
            var both = new int[idx.Length * 2];
            Array.Copy(idx, both, idx.Length);
            for (int i = 0; i + 2 < idx.Length; i += 3) {
                both[idx.Length + i] = idx[i];
                both[idx.Length + i + 1] = idx[i + 2];
                both[idx.Length + i + 2] = idx[i + 1];
            }
            copy.SetTriangles(both, s, false);
        }
        return copy;
    }

    // The given sub-meshes of a mesh as a mesh of their own (only the vertices they use)
    // Unity's own cube, cylinder, sphere, capsule, plane and quad (what the game's engine provides to every scene)
    private static Mesh BuiltinMesh(long id) {
        PrimitiveType type;
        switch (id) {
            case 10202: type = PrimitiveType.Cube; break;
            case 10206: type = PrimitiveType.Cylinder; break;
            case 10207: type = PrimitiveType.Sphere; break;
            case 10208: type = PrimitiveType.Capsule; break;
            case 10209: type = PrimitiveType.Plane; break;
            case 10210: type = PrimitiveType.Quad; break;
            default: return null;
        }
        // Unity's own mesh, taken straight from its built-in resources (making a primitive object in a scene just to get at it logs errors now that the map has a scene of its own)
        Mesh builtin = Resources.GetBuiltinResource<Mesh>(type + ".fbx");
        if (builtin != null) {
            Mesh copy = UnityEngine.Object.Instantiate(builtin);      // a copy: the saved copies of the map only hold objects that aren't Unity's own assets
            copy.name = MeshPrefix + type;
            copy.hideFlags = HideFlags.DontSave;
            return copy;
        }
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temp);
        return mesh;
    }

    private sealed class FullMesh {
        public Mesh mesh;
        public Vector3[] vertices;
        public Color[] colors;
        public int[][] indices;
    }

    private static Mesh Subset(FullMesh f, int first, int count, Matrix4x4 toLocal) {
        Mesh full = f.mesh;
        if (first < 0 || first + count > full.subMeshCount) return null;
        Vector3[] vertices = f.vertices;
        Color[] colors = f.colors;
        var remap = new Dictionary<int, int>();
        var newVertices = new List<Vector3>();
        var newColors = new List<Color>();
        var indices = new int[count][];
        for (int s = 0; s < count; s++) {
            if (f.indices[first + s] == null) f.indices[first + s] = full.GetIndices(first + s);
            int[] old = f.indices[first + s];
            indices[s] = new int[old.Length];
            for (int i = 0; i < old.Length; i++) {
                int o = old[i];
                if (!remap.TryGetValue(o, out int n)) {
                    n = newVertices.Count;
                    remap[o] = n;
                    newVertices.Add(toLocal.MultiplyPoint3x4(vertices[o]));
                    if (colors.Length > o) newColors.Add(colors[o]);
                }
                indices[s][i] = n;
            }
        }
        var mesh = new Mesh { name = MeshPrefix + full.name, hideFlags = HideFlags.DontSave };
        if (newVertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(newVertices);
        if (newColors.Count == newVertices.Count) mesh.SetColors(newColors);
        mesh.subMeshCount = count;
        for (int s = 0; s < count; s++) mesh.SetTriangles(indices[s], s, false);
        mesh.RecalculateNormals();
        FixNormals(mesh);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh ToUnity(RippedMesh r) {
        var mesh = new Mesh { name = MeshPrefix + r.name, hideFlags = HideFlags.DontSave };
        int n = r.vertexCount;
        if (n > 65535) mesh.indexFormat = IndexFormat.UInt32;
        var v = new Vector3[n];
        for (int i = 0; i < n; i++) {
            var p = new Vector3(r.positions[i * 3], r.positions[i * 3 + 1], r.positions[i * 3 + 2]);
            if (float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z)) p = Vector3.zero;
            v[i] = p;
        }
        mesh.vertices = v;
        if (r.uv0 != null && r.uv0.Length == n * 2) {
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++) uvs[i] = new Vector2(r.uv0[i * 2], r.uv0[i * 2 + 1]);
            mesh.uv = uvs;
        }
        mesh.subMeshCount = r.subMeshes.Count;
        for (int s = 0; s < r.subMeshes.Count; s++) {
            RippedSubMesh sub = r.subMeshes[s];
            mesh.SetIndices(sub.indices, sub.topology == 0 ? MeshTopology.Triangles : (MeshTopology)sub.topology, s, false);
        }
        if (r.colors != null && r.colors.Length == n * 4) {
            var cols = new Color[n];
            for (int i = 0; i < n; i++) cols[i] = new Color(r.colors[i * 4], r.colors[i * 4 + 1], r.colors[i * 4 + 2], 1f);
            mesh.colors = cols;
        }
        mesh.RecalculateNormals();
        // Triangles with no area (and vertices that aren't numbers) leave normals of zero length or NaN, which light up as white blobs; give them an upward normal
        FixNormals(mesh);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void FixNormals(Mesh mesh) {
        Vector3[] normals = mesh.normals;
        bool fixedAny = false;
        for (int i = 0; i < normals.Length; i++) {
            Vector3 nm = normals[i];
            float len = nm.sqrMagnitude;
            if (float.IsNaN(len) || float.IsInfinity(len) || len < 0.25f) { normals[i] = Vector3.up; fixedAny = true; }
        }
        if (fixedAny) mesh.normals = normals;
    }
}

}
