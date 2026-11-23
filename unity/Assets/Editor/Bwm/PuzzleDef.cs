// A puzzle is one class deriving from PuzzleDef (in Assets/Editor/Puzzles). Build() describes it with a
// PuzzleBuilder: SDK parts, blockout geometry, field values and event wiring. Nothing is placed by hand.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bwm
{
    public abstract class PuzzleDef
    {
        // Name of the root object, the scene and the exported .puzzlepack. Letters and digits only.
        public abstract string Name { get; }

        // Ground the puzzle needs, in metres (x by z). Used to find a flat, clear site the first time it is built.
        public virtual Vector2 Footprint => new Vector2(18f, 14f);

        // Where in the world (x, z) to look for a site near. Default: the spawn rooms.
        public virtual Vector2 Near => World.Spawn;

        // Set to pin the puzzle to an exact world position instead of searching (y is ignored; the terrain decides).
        public virtual Vector2? FixedSite => null;

        // Heading of the puzzle's local +z axis, in degrees
        public virtual float Yaw => 0f;

        // How far from Near (metres) the site search looks
        public virtual float MinDistance => 15f;
        public virtual float MaxDistance => 300f;

        // The SDK's puzzle type: 0 Puzzle (rewards a gourd), 1 Tower, 2 None (scenery, land, anything else)
        public virtual int PuzzleType => 0;
        // Parts of the puzzle far from its site (world x, z and a radius), which the island levels and keeps clear
        // like its site
        public virtual System.Collections.Generic.IEnumerable<Vector3> ExtraSites => System.Array.Empty<Vector3>();

        public abstract void Build(PuzzleBuilder b);
    }

    // One object of the puzzle being built
    public sealed class Piece
    {
        public readonly GameObject Go;
        public Piece(GameObject go) { Go = go; }
        public Transform Transform => Go.transform;

        // The SDK component of this type on the piece, e.g. Component("PuzzlePegBoard")
        public Component Component(string type) => Sdk.Get(Go, type);

        // Sets a serialized field of one of the piece's SDK components. Enums take their index.
        public Piece Set(string type, string field, object value) { Sdk.Set(Component(type), field, value); return this; }

        // Appends references to a list field, e.g. Add("PuzzlePressInOrder", "buttons", a.Component("PuzzleButton"))
        public Piece Add(string type, string field, params UnityEngine.Object[] items) { Sdk.Add(Component(type), field, items); return this; }

        // When this piece's event fires, call target.<method>(), or target.<method>(arg) for a bool, int, float or
        // string. The target can be a component or a GameObject, e.g. On("PuzzlePresenceSensor", "onPressed", thing.Go, "SetActive", true).
        public Piece On(string type, string eventField, UnityEngine.Object target, string method, object arg = null)
        {
            Sdk.Wire(Component(type), eventField, target, method, arg);
            return this;
        }

        public Piece Named(string name) { Go.name = name; return this; }

        // Makes this piece a child of another, keeping where it is (for grouping, e.g. walls under a Soundproof parent)
        public Piece Under(Piece parent) { Go.transform.SetParent(parent.Transform, true); return this; }

        // Only the SDK's Glass, Timer and Progress Bar keep a scale; the game draws every other part at its own size
        public Piece Scaled(float x, float y, float z) { Go.transform.localScale = new Vector3(x, y, z); return this; }
        // A Glass part sized in metres: 'width' across and 'height' up, facing the way its yaw turns it, 4 cm thick for
        // collisions (the prefab is a 10 m plane, tilted upright on its root, with a flat collider)
        public Piece Pane(float width, float height)
        {
            Go.transform.localScale = new Vector3(width / 10f, 0.18683f, height / 10f);
            BoxCollider box = Go.GetComponent<BoxCollider>();
            if (box != null) box.size = new Vector3(10f, 0.04f / 0.18683f, 10f);
            return this;
        }
    }

    public sealed class PuzzleBuilder
    {
        public readonly GameObject Root;
        public readonly Ground Ground;

        // Every other puzzle's remembered site as (x, z, radius), for packs that dress the land round them
        public List<Vector3> OtherSites = new List<Vector3>();
        readonly string assetDir;      // Assets/Puzzles/<Name>
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public PuzzleBuilder(GameObject root, Ground ground, string assetDir)
        {
            Root = root; Ground = ground; this.assetDir = assetDir;
        }

        // The root's PuzzleSDK component (puzzleType, gourdColor, onPuzzleStart, onPuzzleStop, onWin, Win(), ...)
        public Component Puzzle => Root.GetComponent(Sdk.T("PuzzleSDK"));
        public Piece RootPiece => new Piece(Root);

        // Local (x, z) in metres from the puzzle's centre, +z forward. 'height' is metres above the terrain under
        // that point, so pieces follow the ground; pass onGround: false to measure from the root's level instead.
        Vector3 Place(float x, float z, float height, bool onGround)
        {
            Vector3 world = Root.transform.TransformPoint(new Vector3(x, 0f, z));
            float baseY = Root.transform.position.y;
            if (onGround)
            {
                if (!Ground.At(world.x, world.z, out baseY))
                    throw new Exception("Nothing to stand on at local (" + x + ", " + z + "): that spot is a hole in the terrain");
            }
            return new Vector3(world.x, baseY + height, world.z);
        }

        // Where a local point is in the world (see Place); for kit code that builds its own geometry
        public Vector3 WorldPoint(float x, float z, float height = 0f, bool onGround = true) => Place(x, z, height, onGround);

        // Something players can pick up, carry and throw (the SDK's Custom Item): a mesh of ours, e.g. a snowball.
        // The game fits a box collider round the mesh by itself. 'height' is where its centre sits.
        public Piece Item(string name, Mesh mesh, Material material, float x, float z, float height, float mass = 1f, bool onGround = true)
        {
            Piece item = MeshPiece(name, mesh, material, false);
            item.Go.transform.position = Place(x, z, height, onGround);
            Component c = item.Go.AddComponent(Sdk.T("PuzzleItem"));
            Sdk.Set(c, "mass", mass);
            Sdk.Set(c, "canBelt", false);
            Sdk.Set(c, "canBackpack", false);
            return item;
        }

        // One of the SDK's prefabs by file name without extension, e.g. "Peg Board", "Switch Start", "Button Hold"
        public const float TimerScale = 0.28f;      // the Timer at 1.1 x 0.44 m
        public Piece Part(string prefab, float x, float z, float height = 0f, float yaw = 0f, bool onGround = true)
        {
            string path = "Assets/BigPuzzleSDK/" + prefab + ".prefab";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new Exception("SDK prefab not found: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, Root.transform);
            go.transform.position = Place(x, z, height, onGround);
            // (keeping the prefab's own turn: Glass is a plane stood upright by a 90 degree tilt on its root, and with only
            // the yaw every window lay flat, a sheet across the room; no other SDK part has a turn on its root)
            go.transform.rotation = Root.transform.rotation * Quaternion.Euler(0f, yaw, 0f) * asset.transform.localRotation;
            // The Timer is the game's countdown board, 3.92 x 1.57 m and 0.21 m deep at scale 1 (measured in the game: the SDK
            // has no preview model for it, so puzzles took it for a small clock and it stood out through their walls): a
            // wall clock's size by default; a puzzle's own Scaled(s) on a timer should be s * TimerScale
            if (prefab == "Timer") go.transform.localScale = Vector3.one * TimerScale;
            return new Piece(go);
        }

        // An empty marker object (teleport targets, parents for groups, ...)
        public Piece Empty(string name, float x, float z, float height = 0f, bool onGround = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = Place(x, z, height, onGround);
            return new Piece(go);
        }

        // Blockout geometry: a solid box players can stand on and bump into. 'height' is where its underside sits.
        public Piece Box(string name, float x, float z, Vector3 size, Color color, float height = 0f, float yaw = 0f, bool onGround = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(Root.transform, false);
            Vector3 p = Place(x, z, height, onGround);
            go.transform.position = new Vector3(p.x, p.y + size.y / 2f, p.z);
            go.transform.rotation = Root.transform.rotation * Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            return new Piece(go);
        }

        // Clears the game's trees, shrubs and grass from a rectangle (metres, centred on local x, z). The SDK map
        // and the previews show no vegetation, but most of the island is covered in it: call this for every
        // puzzle's working area. It also hides the loose boulders there (they are not in the SDK map either).
        // With cutHole it also cuts the terrain away (for underground rooms).
        public Piece ClearGround(float sizeX, float sizeZ, float x = 0f, float z = 0f, bool cutHole = false, bool removeRocks = true)
        {
            Piece hole = Part("Terrain Hole", x, z);
            hole.Go.name = "Clear Ground";
            BoxCollider box = hole.Go.GetComponent<BoxCollider>();
            box.size = new Vector3(sizeX, 16f, sizeZ);
            box.center = Vector3.zero;
            hole.Set("PuzzleTerrainHole", "cutHoles", cutHole).Set("PuzzleTerrainHole", "removeTrees", true)
                .Set("PuzzleTerrainHole", "removeShrubs", true).Set("PuzzleTerrainHole", "removeRocks", removeRocks);
            return hole;
        }

        // Custom geometry: a mesh built in code (land, ramps, anything a box cannot be), saved with the puzzle so
        // it is exported with it. Vertices are in the puzzle's local space, y measured from the root's level.
        // With collider, players can stand on it and bump into it.
        public Piece MeshPiece(string name, Mesh mesh, Color color, bool collider = true) => MeshPiece(name, mesh, Material(color), collider);

        public Piece MeshPiece(string name, Mesh mesh, Material material, bool collider = true)
        {
            string dir = assetDir + "/Meshes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(assetDir, "Meshes");
            string path = dir + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return new Piece(go);
        }

        // Saves a mesh with the puzzle (so pieces can share it) and returns it
        public Mesh SaveMesh(string name, Mesh mesh)
        {
            string dir = assetDir + "/Meshes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(assetDir, "Meshes");
            string path = dir + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Declares the land this pack adds, so later builds of other puzzles can be sited on it and snap to it.
        // Heights are world heights at the (nx + 1) x (nz + 1) grid points, row by row in z; NaN where there is none.
        // 'blocked' (optional, same grid) marks points no puzzle may be sited on: trees, rocks, paths.
        public void RegisterLand(float worldX0, float worldZ0, float cell, int nx, int nz, float[] worldHeights, bool[] blocked = null)
        {
            Ground.SaveLand(Root.name, worldX0, worldZ0, cell, nx, nz, worldHeights, blocked);
        }

        // Adds one of our own runtime scripts (a class in Assets/PuzzleScripts) to a piece. The SDK compiles that
        // folder into a plugin DLL exported beside the pack; a changed script needs a game restart, not a reload.
        public Component AddCustomScript(Piece piece, string className)
        {
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "WinterWalkScripts") continue;
                Type t = a.GetType(className);
                if (t != null) return piece.Go.AddComponent(t);
            }
            throw new Exception("No script class '" + className + "' in Assets/PuzzleScripts");
        }

        // Adds an SDK "Add Component" script (PuzzleHider, PuzzlePressInOrder, PuzzleItem, ...) to a piece
        public Component AddScript(Piece piece, string type) => piece.Go.AddComponent(Sdk.T(type));

        // Where Ctrl+F1 puts the tester
        public void DebugTeleport(float x, float z, float height = 1.5f)
        {
            Sdk.Set(Puzzle, "debugTeleport", Empty("Debug Teleport", x, z, height).Transform);
        }

        // A material using one of our own shaders (Assets/Shaders), saved with the puzzle. The game has no shader
        // of that name, so the pack carries it; 'setup' sets its properties. Rebuilt on every build.
        public Material ShaderMaterial(string shaderName, string materialName, Action<Material> setup = null)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Shader not found: " + shaderName + " (it should be in Assets/Shaders)");
            string dir = assetDir + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(assetDir, "Materials");
            string path = dir + "/" + materialName + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = shader;
                var defaults = new Material(shader);
                m.CopyPropertiesFromMaterial(defaults);
                UnityEngine.Object.DestroyImmediate(defaults);
            }
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // A plain URP material of this colour, saved with the puzzle so it is exported with it
        public Material Material(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGB(color);
            if (materials.TryGetValue(key, out Material m)) return m;
            string dir = assetDir + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(assetDir, "Materials");
            string path = dir + "/" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new Exception("URP/Lit shader not found: run Bwm.Cli.SetupProject once");
                m = new Material(shader);
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(m, path);
            }
            return materials[key] = m;
        }
    }
}
