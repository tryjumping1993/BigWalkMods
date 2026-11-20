// BigPuzzleSDK: reads Big Walk's world (the level2 scene in data.unity3d) on the creator's own computer, for the
// "Import BigWalk Map" reference map: the four terrains and every visible mesh with where it stands.
// Nothing it reads is ever exported with a puzzle.
//
// Layouts are Unity 6000.3's (see BigWalkWorldLayouts). Every object is read field by field; the fields that
// aren't needed are skipped using their sizes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK.GameAssets {

public sealed class WorldTerrain {
    public string name;
    public Vector3 position;     // world position of the terrain's corner
    public int resolution;       // height samples per side (2049)
    public Vector3 scale;        // metres per sample in x and z; scale.y is the terrain's height range
    public short[] heights;      // resolution * resolution, row = z
    public byte[] holes;         // holeResolution * holeResolution, row = z: 0 where the game has cut a hole in the terrain (under a tower, in a rock...), 255 elsewhere; null if none
    public int holeResolution;
    public int colorResolution;  // the colour map's size (the game's splat map: 1024)
    public Color32[] colors;     // the terrain's average colour at each splat-map pixel (row = z), or null
}

// One object of the game's scene tree (a game object and its transform): the preview rebuilds the same tree
public sealed class WorldNode {
    public long father;          // the id of its parent node, 0 for a root (or one that isn't in the table)
    public string name;
    public Vector3 position, scale;   // local to the parent
    public Quaternion rotation;
}

public sealed class WorldMeshObject {
    public bool alwaysSmall;     // it's inside the World-Scatter folder: always goes in Small Details
    public bool deathFloor;      // it is (part of) the game's death floor: the preview puts it in its own folder
    public long nodeId;          // its place in the tree (a key of the nodes returned by Read)
    public Matrix4x4 worldMatrix;   // its true local-to-world matrix (for a static-batched object, matrix is the identity but this is not)
    public string name;          // the object's name
    public string group;         // its name without numbers and LOD suffixes: objects of one kind share it
    public Matrix4x4 matrix;     // local to world
    public AssetEntry mesh;      // null for one of Unity's own shapes (see builtinMesh)
    public RippedMesh generated; // a mesh the SDK builds itself (the train track): used instead of mesh
    public Texture2D colorTexture;   // the map room's miniature world: the colours the game paints it with, to be drawn with the mesh's UVs (otherwise null)
    public bool hasColorOverride;   // the game gives this object its own colour (a PropertyBlockHelper on it): that colour is used
    public Color colorOverride;
    public long builtinMesh;     // Unity's built-in shapes live in the engine, not in the game files: 10202 cube, 10206 cylinder, 10207 sphere, 10208 capsule, 10209 plane, 10210 quad
    public string[] materials;   // keys for the Func returned by Read: one per material of the renderer
    public int firstSubMesh, subMeshCount;   // static-batched: only these parts of the (combined) mesh belong to this object, and the mesh is already in world space (matrix is then the identity)
}

public static class BigWalkWorld {
    private const string SceneName = "level2";
    // Objects (and everything below them) that are left out of the preview; empty now that batched meshes are placed correctly
    // What the last Read left out and why (shown in the Console after an import)
    public static string LastReport = "";

    // Objects that are shown even though the scene file has them switched off (the game turns them on itself); their parents must still be on
    // Height of the water; ground below it is tinted like it is under water. (A guess: the water isn't an object in the scene file I could read.)
    private const float SeaLevel = 0f;

    private static readonly HashSet<string> ShownEvenIfOff = new HashSet<string> {
        "EndingWall_Gate_Door_tempframe", "EndingWall_Gate_Plinth",
    };

    // The game's death floor: the big surface under the world that players fall onto. It gets its own folder in the preview (see WorldMeshObject.deathFloor)
    // and is shown whether or not the game has it switched on
    // Everything inside this goes in the Small Details folder whatever its size
    private const string SmallFolderName = "World-Scatter";

    private const string DeathFloorName = "DepthTerrainOccluder";

    private static readonly HashSet<string> HiddenNames = new HashSet<string> {
        "Litter_Log_Wet_Large", "Litter_Log_Wet", "Impostor", "EndingWall_WallSection", "SecretZone", "PositionerVoidRoom",
    };

    // Anything whose name starts with one of these (and everything below it) is left out: the train carriages ("Carrriage" is how the game spells some of them)
    private static readonly string[] HiddenPrefixes = { "Carriage", "Carrriage" };

    // Versions of things made for 2 and 3 players are left out (the 4 player version is the one shown): an object, or anything it sits in, with one of these in its name
    private static readonly string[] OtherPlayerCounts = { "PlayerCount2", "PlayerCount3", "2Player", "3Player", "2 Player", "3 Player" };

    private const int GameObjectClass = 1, TransformClass = 4, MeshRendererClass = 23, MeshFilterClass = 33, TerrainClass = 218, TerrainDataClass = 156;

    private sealed class TransformInfo {
        public long gameObject, father;
        public Vector3 position, scale;
        public Quaternion rotation;
    }

    private sealed class RawArray { public byte[] data; }

    // materialColor: the average colour of a material (a key from WorldMeshObject.materials); only valid while 'assets' is open
    public static void Read(GameAssets assets, Action<string, float> progress,
                            out List<WorldTerrain> terrains, out List<WorldMeshObject> objects, out Dictionary<long, WorldNode> nodes, out Func<string, Color> materialColor, out Func<string, Texture2D> materialTexture) {
        SerializedFile scene = assets.files.Find(f => f.name == SceneName);
        if (scene == null) throw new GameAssetException("The game's world scene (" + SceneName + ") wasn't found in data.unity3d.");
        if (scene.bigEndian) throw new GameAssetException("The world scene is big-endian, which isn't supported.");

        TreeNode goTree = TreeNode.Parse(WorldLayouts.GameObject), transformTree = TreeNode.Parse(WorldLayouts.Transform),
                 filterTree = TreeNode.Parse(WorldLayouts.MeshFilter), rendererTree = TreeNode.Parse(WorldLayouts.MeshRenderer),
                 terrainTree = TreeNode.Parse(WorldLayouts.Terrain), dataTree = TreeNode.Parse(WorldLayouts.TerrainData);
        var goFields = new HashSet<string> { "m_Name", "m_IsActive" };
        var transformFields = new HashSet<string> { "m_GameObject", "m_LocalRotation", "m_LocalPosition", "m_LocalScale", "m_Father" };
        var filterFields = new HashSet<string> { "m_GameObject", "m_Mesh" };
        var rendererFields = new HashSet<string> { "m_GameObject", "m_Enabled", "m_Materials", "m_StaticBatchInfo" };
        var terrainFields = new HashSet<string> { "m_GameObject", "m_TerrainData" };

        progress?.Invoke("Reading the world scene...", 0.02f);
        byte[] bytes = scene.bundle.Read(scene.node, 0, (int)scene.node.size);
        var r = new DataReader(bytes, false);

        var gameObjects = new Dictionary<long, KeyValuePair<string, bool>>();
        var transforms = new Dictionary<long, TransformInfo>();
        var transformOfObject = new Dictionary<long, long>();
        var filters = new List<KeyValuePair<long, KeyValuePair<int, long>>>();     // game object -> (mesh file, mesh id)
        var rendererOn = new Dictionary<long, bool>();
        var rendererBatch = new Dictionary<long, KeyValuePair<int, int>>();   // static batching: first sub-mesh, sub-mesh count
        var rendererMaterials = new Dictionary<long, List<KeyValuePair<int, long>>>();
        var terrainObjects = new List<KeyValuePair<long, KeyValuePair<int, long>>>();
        var rendererObject = new Dictionary<long, long>();          // mesh renderer -> its game object
        var rendererColor = new Dictionary<long, Color>();          // mesh renderer -> colour set by a PropertyBlockHelper
        var helperScripts = new Dictionary<string, bool>();         // script (file#id) -> is it PropertyBlockHelper
        var bigBehaviours = new List<SerializedFile.ObjectInfo>();  // scripts' data of a size that can hold a spline (see AddTrainTrack)

        int n = 0, total = scene.objects.Count;
        foreach (SerializedFile.ObjectInfo o in scene.objects) {
            if (++n % 20000 == 0) progress?.Invoke("Reading the world scene...", 0.02f + 0.3f * n / total);
            if (o.classId == 114 && o.byteSize >= 60 && o.byteSize <= 400 && (o.byteStart & 3) == 0)
                ReadColorHelper(bytes, o, assets, scene, helperScripts, rendererColor);
            if (o.classId == 114 && o.byteSize >= 600 && o.byteSize <= 20000 && (o.byteStart & 3) == 0) bigBehaviours.Add(o);
            TreeNode tree;
            HashSet<string> fields;
            switch (o.classId) {
                case GameObjectClass: tree = goTree; fields = goFields; break;
                case TransformClass: tree = transformTree; fields = transformFields; break;
                case MeshFilterClass: tree = filterTree; fields = filterFields; break;
                case MeshRendererClass: tree = rendererTree; fields = rendererFields; break;
                case TerrainClass: tree = terrainTree; fields = terrainFields; break;
                default: continue;
            }
            if ((o.byteStart & 3) != 0) throw new GameAssetException("An object in the world scene isn't 4-byte aligned.");
            r.Position = (int)o.byteStart;
            Dictionary<string, object> d = ReadWanted(tree, r, fields, false);
            if (r.Position != o.byteStart + o.byteSize)
                throw new GameAssetException("A " + tree.type + " in the world scene didn't read cleanly. The game was probably updated to a newer Unity; the SDK needs an update.");

            switch (o.classId) {
                case GameObjectClass:
                    gameObjects[o.pathId] = new KeyValuePair<string, bool>((string)d["m_Name"], (bool)d["m_IsActive"]);
                    break;
                case TransformClass: {
                    long go = PathId(d["m_GameObject"]);
                    var t = new TransformInfo {
                        gameObject = go,
                        father = PathId(d["m_Father"]),
                        rotation = Quat(d["m_LocalRotation"]),
                        position = Vec(d["m_LocalPosition"]),
                        scale = Vec(d["m_LocalScale"]),
                    };
                    transforms[o.pathId] = t;
                    transformOfObject[go] = o.pathId;
                    break;
                }
                case MeshFilterClass: {
                    var mesh = (Dictionary<string, object>)d["m_Mesh"];
                    filters.Add(new KeyValuePair<long, KeyValuePair<int, long>>(PathId(d["m_GameObject"]),
                        new KeyValuePair<int, long>((int)Convert.ToInt64(mesh["m_FileID"]), Convert.ToInt64(mesh["m_PathID"]))));
                    break;
                }
                case MeshRendererClass:
                    rendererObject[o.pathId] = PathId(d["m_GameObject"]);
                    rendererOn[PathId(d["m_GameObject"])] = Convert.ToInt64(d["m_Enabled"]) != 0;
                    var mats = new List<KeyValuePair<int, long>>();
                    foreach (object m in (List<object>)d["m_Materials"]) {
                        var pm = (Dictionary<string, object>)m;
                        mats.Add(new KeyValuePair<int, long>((int)Convert.ToInt64(pm["m_FileID"]), Convert.ToInt64(pm["m_PathID"])));
                    }
                    rendererMaterials[PathId(d["m_GameObject"])] = mats;
                    if (d.TryGetValue("m_StaticBatchInfo", out object sbi) && sbi is Dictionary<string, object> sb)
                        rendererBatch[PathId(d["m_GameObject"])] = new KeyValuePair<int, int>((int)Convert.ToInt64(sb["firstSubMesh"]), (int)Convert.ToInt64(sb["subMeshCount"]));
                    break;
                case TerrainClass: {
                    var data = (Dictionary<string, object>)d["m_TerrainData"];
                    terrainObjects.Add(new KeyValuePair<long, KeyValuePair<int, long>>(PathId(d["m_GameObject"]),
                        new KeyValuePair<int, long>((int)Convert.ToInt64(data["m_FileID"]), Convert.ToInt64(data["m_PathID"]))));
                    break;
                }
            }
        }

        var worlds = new Dictionary<long, Matrix4x4>();
        var activeCache = new Dictionary<long, bool>();

        var colors = new Colors(assets);
        materialColor = colors.Material;
        materialTexture = colors.RockTexture;

        // ---- terrains ----
        terrains = new List<WorldTerrain>();
        foreach (var t in terrainObjects) {
            progress?.Invoke("Reading terrain heights...", 0.4f + 0.1f * terrains.Count);
            SerializedFile dataFile = FileOf(assets, scene, t.Value.Key);
            if (dataFile == null) continue;
            SerializedFile.ObjectInfo? info = null;
            foreach (SerializedFile.ObjectInfo o in dataFile.objects)
                if (o.pathId == t.Value.Value && o.classId == TerrainDataClass) { info = o; break; }
            if (info == null) continue;
            byte[] raw = dataFile.ReadObject(info.Value.byteStart, info.Value.byteSize);
            var dr = new DataReader(raw, dataFile.bigEndian);
            Dictionary<string, object> td = ReadWanted(dataTree, dr, new HashSet<string> { "m_Name", "m_Heightmap" }, true);
            dr.Position = 0;
            Dictionary<string, object> splat = ReadWanted(dataTree, dr, new HashSet<string> { "m_SplatDatabase" }, false);
            var hm = (Dictionary<string, object>)td["m_Heightmap"];
            var rawHeights = (RawArray)hm["m_Heights"];
            int res = (int)Convert.ToInt64(hm["m_Resolution"]);
            var heights = new short[rawHeights.data.Length / 2];
            Buffer.BlockCopy(rawHeights.data, 0, heights, 0, rawHeights.data.Length);
            if (res * res != heights.Length) throw new GameAssetException("A terrain's height map has an unexpected size.");
            byte[] holes = null; int holeRes = 0;
            if (hm.TryGetValue("m_Holes", out object holeObj) && holeObj is RawArray rawHoles && rawHoles.data.Length > 0) {
                holeRes = (int)Math.Round(Math.Sqrt(rawHoles.data.Length));
                if (holeRes * holeRes == rawHoles.data.Length) holes = rawHoles.data;
            }
            Vector3 position = Vector3.zero;
            if (transformOfObject.TryGetValue(t.Key, out long trId)) position = World(trId, transforms, worlds).GetColumn(3);
            var terrain = new WorldTerrain {
                name = (string)td["m_Name"], position = position, resolution = res, scale = Vec(hm["m_Scale"]), heights = heights, holes = holes, holeResolution = holeRes,
            };
            try { colors.PaintTerrain(terrain, dataFile, (Dictionary<string, object>)splat["m_SplatDatabase"]); }
            catch (Exception) { /* no colours for this terrain: it stays plain */ }
            terrains.Add(terrain);
        }

        // ---- meshes ----
        progress?.Invoke("Placing the world's objects...", 0.55f);
        var meshIndex = new Dictionary<string, AssetEntry>();
        foreach (List<AssetEntry> list in assets.meshes.Values)
            foreach (AssetEntry e in list) meshIndex[e.file.name + "#" + e.pathId] = e;

        var colorOfObject = new Dictionary<long, Color>();
        foreach (var rc in rendererColor)
            if (rendererObject.TryGetValue(rc.Key, out long ownerGo)) colorOfObject[ownerGo] = rc.Value;

        objects = new List<WorldMeshObject>();
        int done = 0;
        int skipOff = 0, skipLod = 0, skipInactive = 0, skipMesh = 0;
        var inactiveNames = new HashSet<string>();
        var missingMeshes = new HashSet<string>();
        AssetEntry wallModel = null, miniTerrainModel = null;
        string[] wallMaterials = null;      // the map room's miniature of the world and of the big wall (see AddBigWall)
        foreach (var f in filters) {
            if (++done % 5000 == 0) progress?.Invoke("Placing the world's objects...", 0.55f + 0.4f * done / filters.Count);
            long go = f.Key;
            bool floor = transformOfObject.TryGetValue(go, out long floorTr) && IsUnderName(floorTr, transforms, gameObjects, DeathFloorName);
            if (!floor && (!rendererOn.TryGetValue(go, out bool on) || !on)) { skipOff++; continue; }   // no renderer, or switched off
            if (!transformOfObject.TryGetValue(go, out long trId)) continue;
            if (!gameObjects.TryGetValue(go, out var info)) continue;
            string name = info.Key;
            if (!floor && (name == "collider" || IsLowerDetail(name))) { skipLod++; continue; }
            if (!floor && !ActiveInHierarchy(trId, transforms, gameObjects, activeCache) && !(ShownEvenIfOff.Contains(GroupOf(name)) && transforms[trId].father != 0 && transforms.ContainsKey(transforms[trId].father) && ActiveInHierarchy(transforms[trId].father, transforms, gameObjects, activeCache))) { skipInactive++; if (inactiveNames.Count < 60) inactiveNames.Add(GroupOf(name)); continue; }
            if (IsHidden(trId, transforms, gameObjects)) continue;

            string fileName = f.Value.Key == 0 ? scene.name : (f.Value.Key - 1 < scene.externals.Count ? System.IO.Path.GetFileName(scene.externals[f.Value.Key - 1]) : null);
            AssetEntry mesh = null;
            long builtin = 0;
            if (fileName == "unity default resources" && f.Value.Value >= 10202 && f.Value.Value <= 10210) builtin = f.Value.Value;
            else if (fileName == null || !meshIndex.TryGetValue(fileName + "#" + f.Value.Value, out mesh)) { skipMesh++; if (missingMeshes.Count < 40) missingMeshes.Add(GroupOf(name) + " [" + fileName + "]"); continue; }

            if (mesh != null && mesh.name == "EndingWall" && wallModel == null) wallModel = mesh;
            if (mesh != null && mesh.name == "MaproomTerrainMesh" && miniTerrainModel == null) miniTerrainModel = mesh;
            Matrix4x4 world = World(trId, transforms, worlds);

            var materialKeys = new List<string>();
            if (rendererMaterials.TryGetValue(go, out var refs))
                foreach (var m in refs) {
                    SerializedFile mf = FileOf(assets, scene, m.Key);
                    materialKeys.Add(mf == null ? "" : mf.name + "#" + m.Value);
                }
            Texture2D miniature = mesh != null && mesh.name == "MaproomTerrainMesh" ? colors.Maproom() : null;
            var entry = new WorldMeshObject { colorTexture = miniature, deathFloor = floor, alwaysSmall = IsUnderName(trId, transforms, gameObjects, SmallFolderName), nodeId = trId, worldMatrix = world, name = name, group = GroupOf(name), matrix = world, mesh = mesh, builtinMesh = builtin, materials = materialKeys.ToArray() };
            if (mesh != null && mesh.name == "EndingWall" && wallMaterials == null) wallMaterials = entry.materials;
            // The game merges ("statically batches") many objects into big combined meshes whose vertices are already in world space;
            // each object then owns just a few sub-meshes of it and must not be moved by its own transform again
            if (rendererBatch.TryGetValue(go, out var batch) && batch.Value > 0) {
                entry.firstSubMesh = batch.Key;
                entry.subMeshCount = batch.Value;
                entry.matrix = Matrix4x4.identity;
            }
            if (colorOfObject.TryGetValue(go, out Color overrideColor)) { entry.hasColorOverride = true; entry.colorOverride = overrideColor; }
            objects.Add(entry);
        }
        var syntheticNodes = new Dictionary<long, WorldNode>();
        AddBigWall(objects, syntheticNodes, terrains, wallModel, miniTerrainModel, wallMaterials);
        AddTrainTrack(objects, syntheticNodes, terrains, bytes, bigBehaviours, gameObjects, transformOfObject, transforms, worlds);
        // The tree the placed objects sit in: each of them and all of their parents
        nodes = new Dictionary<long, WorldNode>();
        foreach (WorldMeshObject placed in objects) {
            long id = placed.nodeId;
            while (id != 0 && !nodes.ContainsKey(id)) {
                if (syntheticNodes.TryGetValue(id, out WorldNode made)) { nodes[id] = made; id = made.father; continue; }
                if (!transforms.TryGetValue(id, out TransformInfo ti)) break;
                nodes[id] = new WorldNode {
                    father = ti.father != 0 && transforms.ContainsKey(ti.father) ? ti.father : 0,
                    name = gameObjects.TryGetValue(ti.gameObject, out var gi) ? gi.Key : "?",
                    position = ti.position, rotation = ti.rotation, scale = ti.scale,
                };
                id = nodes[id].father;
            }
        }
        LastReport = "Placed " + objects.Count + " objects (" + colorOfObject.Count + " coloured by the game's own overrides). Left out: " + skipOff + " with the renderer off or missing, " + skipLod + " lower-detail copies or colliders, "
                   + skipInactive + " switched off in the game (e.g. " + string.Join(", ", inactiveNames) + "), " + skipMesh + " whose mesh wasn't found (e.g. " + string.Join(", ", missingMeshes) + ").";
    }

    // ---- the big wall ----

    // The game builds its big wall while it runs (from a spline), so there is no wall mesh in the world scene. The map room's miniature has one, though: the same
    // wall at about 1/69 the size, lined up with the miniature of the terrain. Knowing where the miniature terrain sits on the real terrain (their edges and their
    // lowest and highest points), the miniature wall can be scaled up to stand where the real one does.
    private static void AddBigWall(List<WorldMeshObject> objects, Dictionary<long, WorldNode> synthetic, List<WorldTerrain> terrains, AssetEntry wallModel, AssetEntry miniTerrainModel, string[] wallMaterials) {
        if (wallModel == null || miniTerrainModel == null || terrains.Count == 0) return;
        try {
            RippedMesh wall = MeshReader.Read(wallModel);
            RippedMesh mini = MeshReader.Read(miniTerrainModel);
            if (wall.positions == null || mini.positions == null || wall.vertexCount == 0 || mini.vertexCount == 0) return;

            // The miniature terrain's size, and the real terrain's
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < mini.vertexCount; i++) {
                var p = new Vector3(mini.positions[i * 3], mini.positions[i * 3 + 1], mini.positions[i * 3 + 2]);
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            float wx0 = float.MaxValue, wx1 = float.MinValue, wz0 = float.MaxValue, wz1 = float.MinValue, wy0 = float.MaxValue, wy1 = float.MinValue;
            foreach (WorldTerrain t in terrains) {
                wx0 = Mathf.Min(wx0, t.position.x); wx1 = Mathf.Max(wx1, t.position.x + (t.resolution - 1) * t.scale.x);
                wz0 = Mathf.Min(wz0, t.position.z); wz1 = Mathf.Max(wz1, t.position.z + (t.resolution - 1) * t.scale.z);
                short low = short.MaxValue, high = short.MinValue;
                foreach (short h in t.heights) { if (h < low) low = h; if (h > high) high = h; }
                wy0 = Mathf.Min(wy0, t.position.y + low * (t.scale.y / 32766f)); wy1 = Mathf.Max(wy1, t.position.y + high * (t.scale.y / 32766f));
            }
            if (hi.x - lo.x < 1e-3f || hi.y - lo.y < 1e-3f || hi.z - lo.z < 1e-3f) return;
            float kx = (wx1 - wx0) / (hi.x - lo.x), kz = (wz1 - wz0) / (hi.y - lo.y), ky = (wy1 - wy0) / (hi.z - lo.z);
            float cx = (lo.x + hi.x) * 0.5f, cy = (lo.y + hi.y) * 0.5f, wcx = (wx0 + wx1) * 0.5f, wcz = (wz0 + wz1) * 0.5f;

            // The miniature's mesh is in the modelling program's axes (z up, y away); the world's are x, y up, z. The map-room object is turned a quarter turn
            // to fit, so x stays, mesh z becomes height and mesh y becomes -z.
            var big = new RippedMesh { name = "BigWall", vertexCount = wall.vertexCount, positions = new float[wall.positions.Length] };
            for (int i = 0; i < wall.vertexCount; i++) {
                float x = wall.positions[i * 3], y = wall.positions[i * 3 + 1], z = wall.positions[i * 3 + 2];
                big.positions[i * 3] = wcx + (x - cx) * kx;
                big.positions[i * 3 + 1] = wy0 + (z - lo.z) * ky;
                big.positions[i * 3 + 2] = wcz - (y - cy) * kz;
            }
            big.colors = wall.colors;      // the wall's own colours (dark and green), drawn with the same game material as the miniature's wall
            foreach (RippedSubMesh sub in wall.subMeshes) big.subMeshes.Add(new RippedSubMesh { indices = sub.indices, topology = sub.topology });

            const long id = -2000000;
            synthetic[id] = new WorldNode { father = 0, name = "BigWall", position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one };
            objects.Add(new WorldMeshObject {
                nodeId = id, worldMatrix = Matrix4x4.identity, matrix = Matrix4x4.identity, name = "BigWall", group = "BigWall",
                generated = big, materials = wallMaterials ?? new string[0],
            });
        } catch (Exception) { /* no wall in the preview if the miniature isn't laid out as expected */ }
    }

    // ---- the train track ----

    // The game builds its train track while it runs, from a spline: a list of points with curve handles, which IS saved in the scene (the game object "TrainSpline").
    // This reads those points and builds a simple track along them: beam and the pillars under it. The real track's exact size isn't saved, so these are estimates.
    private const float TrackBeamWidth = 1.2f, TrackBeamHeight = 0.9f, TrackPillarWidth = 0.7f, TrackStep = 1.5f;

    private sealed class MeshBuilder {
        public readonly List<float> pos = new List<float>();
        public readonly List<int> idx = new List<int>();
        public int Add(Vector3 p) { pos.Add(p.x); pos.Add(p.y); pos.Add(p.z); return pos.Count / 3 - 1; }
        public void Quad(int pi, int pj, int qj, int qi) { idx.Add(pi); idx.Add(pj); idx.Add(qj); idx.Add(pi); idx.Add(qj); idx.Add(qi); }
        public RippedMesh Build(string name) {
            var m = new RippedMesh { name = name, vertexCount = pos.Count / 3, positions = pos.ToArray() };
            m.subMeshes.Add(new RippedSubMesh { indices = idx.ToArray(), topology = 0 });
            return m;
        }
    }

    private static void AddTrainTrack(List<WorldMeshObject> objects, Dictionary<long, WorldNode> synthetic, List<WorldTerrain> terrains, byte[] bytes, List<SerializedFile.ObjectInfo> behaviours,
                                      Dictionary<long, KeyValuePair<string, bool>> gameObjects, Dictionary<long, long> transformOfObject,
                                      Dictionary<long, TransformInfo> transforms, Dictionary<long, Matrix4x4> worlds) {
        try {
            long goId = 0;
            foreach (var g in gameObjects) if (g.Value.Key == "TrainSpline") { goId = g.Key; break; }
            if (goId == 0 || !transformOfObject.TryGetValue(goId, out long trId)) return;
            List<Vector3[]> knots = null;
            foreach (SerializedFile.ObjectInfo o in behaviours) {
                int s = (int)o.byteStart;
                if (BitConverter.ToInt32(bytes, s) != 0 || BitConverter.ToInt64(bytes, s + 4) != goId) continue;
                knots = ReadKnots(bytes, s, (int)o.byteSize);
                if (knots != null) break;
            }
            if (knots == null || knots.Count < 3) return;

            // Closed if the gap from the last point back to the first is like the gaps between the others
            float gaps = 0f;
            for (int i = 1; i < knots.Count; i++) gaps += Vector3.Distance(knots[i - 1][0], knots[i][0]);
            bool closed = Vector3.Distance(knots[knots.Count - 1][0], knots[0][0]) < 1.6f * gaps / (knots.Count - 1);

            var centers = new List<Vector3>();
            var forwards = new List<Vector3>();
            int segments = closed ? knots.Count : knots.Count - 1;
            Vector3 lastForward = Vector3.forward;
            for (int sIdx = 0; sIdx < segments; sIdx++) {
                Vector3[] a = knots[sIdx], b = knots[(sIdx + 1) % knots.Count];
                Vector3 p0 = a[0], p1 = a[0] + a[2], p2 = b[0] + b[1], p3 = b[0];
                float length = 0f;
                Vector3 prev = p0;
                for (int k = 1; k <= 24; k++) { Vector3 q = Bezier(p0, p1, p2, p3, k / 24f); length += Vector3.Distance(prev, q); prev = q; }
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / TrackStep));
                for (int k = 0; k < steps || (!closed && sIdx == segments - 1 && k == steps); k++) {
                    float t = (float)k / steps;
                    Vector3 d = BezierSlope(p0, p1, p2, p3, t);
                    if (d.sqrMagnitude > 1e-8f) lastForward = d.normalized;
                    centers.Add(Bezier(p0, p1, p2, p3, t));
                    forwards.Add(lastForward);
                }
            }
            if (centers.Count < 4) return;

            // The game's track is a blue box beam held up by thin blue pillars, with the train riding on top of it
            var beam = new MeshBuilder();
            var pillars = new MeshBuilder();
            AddRibbon(beam, centers, forwards, closed, 0f, TrackBeamWidth * 0.5f, -TrackBeamHeight, 0f, true);

            Matrix4x4 world = World(trId, transforms, worlds);
            Matrix4x4 inverse = world.inverse;
            Vector3 localUp = inverse.MultiplyVector(Vector3.up).normalized;
            foreach (Vector3[] knot in knots) {
                Vector3 top = knot[0] - localUp * TrackBeamHeight;
                Vector3 w = world.MultiplyPoint3x4(top);
                float ground;
                if (!GroundAt(terrains, w.x, w.z, out ground) || w.y - ground < 3f) continue;      // no ground there, or the beam is low enough not to need one
                Vector3 bottom = inverse.MultiplyPoint3x4(new Vector3(w.x, ground - 0.5f, w.z));
                Vector3 fwd = knot[2].sqrMagnitude > 1e-6f ? knot[2].normalized : Vector3.forward;
                Frame(fwd, out Vector3 side, out Vector3 up);
                Vector3[] corners = { -side - fwd, -side + fwd, side + fwd, side - fwd };
                var low = new int[4]; var high = new int[4];
                for (int k = 0; k < 4; k++) { low[k] = pillars.Add(bottom + corners[k] * (TrackPillarWidth * 0.5f)); high[k] = pillars.Add(top + corners[k] * (TrackPillarWidth * 0.5f)); }
                for (int k = 0; k < 4; k++) {
                    int k2 = (k + 1) % 4;
                    pillars.Quad(low[k], high[k], high[k2], low[k2]);
                    pillars.Quad(low[k], low[k2], high[k2], high[k]);      // and its back, so it doesn't matter which way round it is
                }
            }

            long baseId = -1000000;
            var blue = new Color(0.18f, 0.28f, 0.85f);
            var parts = new[] {
                new KeyValuePair<string, KeyValuePair<MeshBuilder, Color>>("TrainTrack_Beam", new KeyValuePair<MeshBuilder, Color>(beam, blue)),
                new KeyValuePair<string, KeyValuePair<MeshBuilder, Color>>("TrainTrack_Pillars", new KeyValuePair<MeshBuilder, Color>(pillars, blue)),
            };
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i].Value.Key.idx.Count == 0) continue;
                long id = baseId - i;
                synthetic[id] = new WorldNode { father = trId, name = parts[i].Key, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one };
                objects.Add(new WorldMeshObject {
                    nodeId = id, worldMatrix = world, matrix = world, name = parts[i].Key, group = parts[i].Key,
                    generated = parts[i].Value.Key.Build(parts[i].Key), materials = new string[0],
                    hasColorOverride = true, colorOverride = parts[i].Value.Value,
                });
            }
        } catch (Exception) { /* no track in the preview if the spline isn't laid out as expected */ }
    }

    private static void Frame(Vector3 forward, out Vector3 side, out Vector3 up) {
        side = Vector3.Cross(Vector3.up, forward);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
        side.Normalize();
        up = Vector3.Cross(forward, side).normalized;
    }

    // A strip along the path: its cross-section is a rectangle (the underside is left out), centred 'offset' to the side
    private static void AddRibbon(MeshBuilder b, List<Vector3> centers, List<Vector3> forwards, bool closed, float offset, float halfWidth, float bottom, float top, bool underside = false) {
        int count = centers.Count;
        int first = b.pos.Count / 3;
        for (int i = 0; i < count; i++) {
            Frame(forwards[i], out Vector3 side, out Vector3 up);
            Vector3 c = centers[i] + side * offset;
            b.Add(c - side * halfWidth + up * bottom);
            b.Add(c - side * halfWidth + up * top);
            b.Add(c + side * halfWidth + up * top);
            b.Add(c + side * halfWidth + up * bottom);
        }
        int last = closed ? count : count - 1;
        for (int i = 0; i < last; i++) {
            int a = first + i * 4, c = first + ((i + 1) % count) * 4;
            for (int k = 0; k < (underside ? 4 : 3); k++) b.Quad(a + k, c + k, c + (k + 1) % 4, a + (k + 1) % 4);
        }
    }

    // The height of the ground at a world position (the nearest height sample of the terrain under it), false if no terrain is there
    private static bool GroundAt(List<WorldTerrain> terrains, float x, float z, out float height) {
        foreach (WorldTerrain t in terrains) {
            int xi = Mathf.RoundToInt((x - t.position.x) / t.scale.x), zi = Mathf.RoundToInt((z - t.position.z) / t.scale.z);
            if (xi < 0 || zi < 0 || xi >= t.resolution || zi >= t.resolution) continue;
            height = t.position.y + t.heights[zi * t.resolution + xi] * (t.scale.y / 32766f);
            return true;
        }
        height = 0f;
        return false;
    }

    private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    private static Vector3 BezierSlope(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
        float u = 1f - t;
        return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
    }

    // The points of a Unity spline component (a SplineContainer): each is a position, two handles (in the point's own rotated space) and a rotation: 13 floats.
    // Returns position, handle in, handle out (handles turned to the container's space), or null if no list of points is found
    private static List<Vector3[]> ReadKnots(byte[] b, int start, int size) {
        int end = start + size;
        for (int p = start + 32; p <= start + 160 && p + 4 <= end; p += 4) {
            int n = BitConverter.ToInt32(b, p);
            if (n < 3 || n > 4000 || p + 4 + n * 52 > end) continue;
            var list = new List<Vector3[]>(n);
            bool ok = true;
            for (int i = 0; i < n && ok; i++) {
                int q = p + 4 + i * 52;
                var f = new float[13];
                for (int j = 0; j < 13; j++) {
                    f[j] = BitConverter.ToSingle(b, q + j * 4);
                    if (float.IsNaN(f[j]) || Mathf.Abs(f[j]) > 100000f) { ok = false; break; }
                }
                if (!ok) break;
                float len = Mathf.Sqrt(f[9] * f[9] + f[10] * f[10] + f[11] * f[11] + f[12] * f[12]);
                if (Mathf.Abs(len - 1f) > 0.02f) { ok = false; break; }
                var rot = new Quaternion(f[9], f[10], f[11], f[12]);
                list.Add(new[] { new Vector3(f[0], f[1], f[2]), rot * new Vector3(f[3], f[4], f[5]), rot * new Vector3(f[6], f[7], f[8]) });
            }
            if (ok) return list;
        }
        return null;
    }

    // ---- per-object colours ----

    // The game gives many objects their own colour with a PropertyBlockHelper component (target renderer + a list of named values).
    // Reads one, if the MonoBehaviour at o is one, and remembers its colour for the renderer it targets.
    private static void ReadColorHelper(byte[] bytes, SerializedFile.ObjectInfo o, GameAssets assets, SerializedFile scene,
                                        Dictionary<string, bool> helperScripts, Dictionary<long, Color> rendererColor) {
        try {
            int start = (int)o.byteStart, end = start + (int)o.byteSize;
            int scriptFile = BitConverter.ToInt32(bytes, start + 16);
            long scriptId = BitConverter.ToInt64(bytes, start + 20);
            string key = scriptFile + "#" + scriptId;
            if (!helperScripts.TryGetValue(key, out bool isHelper)) {
                isHelper = false;
                SerializedFile sf = FileOf(assets, scene, scriptFile);
                if (sf != null) {
                    foreach (SerializedFile.ObjectInfo si in sf.objects) {
                        if (si.pathId != scriptId) continue;
                        byte[] raw = sf.ReadObject(si.byteStart, si.byteSize);
                        int p = 0;
                        int nameLen = BitConverter.ToInt32(raw, p); p += 4 + ((nameLen + 3) & ~3);   // m_Name
                        p += 4 + 16;                                                                 // execution order, properties hash
                        int classLen = BitConverter.ToInt32(raw, p);
                        isHelper = classLen == 19 && System.Text.Encoding.ASCII.GetString(raw, p + 4, classLen) == "PropertyBlockHelper";
                        break;
                    }
                }
                helperScripts[key] = isHelper;
            }
            if (!isHelper) return;
            if (BitConverter.ToInt32(bytes, start + 32) != 0) return;               // target renderer lives in another file
            long renderer = BitConverter.ToInt64(bytes, start + 36);

            // The named values: a length-prefixed name starting with '_' then its value; only colour-like names are used
            string[] wanted = { "_TintColor", "_RColor", "_MainColor" };
            int best = int.MaxValue;
            Color found = Color.white;
            for (int i = start + 44; i < end - 24; i += 4) {
                int len = BitConverter.ToInt32(bytes, i);
                if (len < 3 || len > 40 || bytes[i + 4] != (byte)'_' || i + 4 + len + 16 > end) continue;
                string name = System.Text.Encoding.ASCII.GetString(bytes, i + 4, len);
                int idx = Array.IndexOf(wanted, name);
                if (idx < 0 || idx >= best) continue;
                int v = i + 4 + ((len + 3) & ~3);
                if (v + 16 > end) continue;
                Color c = new Color(BitConverter.ToSingle(bytes, v), BitConverter.ToSingle(bytes, v + 4), BitConverter.ToSingle(bytes, v + 8), 1f);
                if (float.IsNaN(c.r + c.g + c.b) || c.r < 0f || c.g < 0f || c.b < 0f || c.r > 4f || c.g > 4f || c.b > 4f) continue;
                best = idx; found = c;
            }
            if (best != int.MaxValue) rendererColor[renderer] = found;
        } catch (Exception) { /* an object that doesn't look as expected is skipped */ }
    }

    // ---- colours ----

    // Average colours of materials (their albedo texture times their tint) and of the terrain's layers, read from the game's own files
    private sealed class Colors {
        private const int TerrainLayerClass = 1953259897, MaterialClass = 21;
        private static readonly string[] AlbedoNames = { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap", "_Albedo", "_MainAlbedoTex" };

        private readonly GameAssets assets;
        private readonly Dictionary<string, KeyValuePair<SerializedFile, SerializedFile.ObjectInfo>> objects = new Dictionary<string, KeyValuePair<SerializedFile, SerializedFile.ObjectInfo>>();
        private readonly Dictionary<string, AssetEntry> textures = new Dictionary<string, AssetEntry>();
        private readonly Dictionary<string, Color> materialCache = new Dictionary<string, Color>();
        private readonly Dictionary<string, Color?> textureCache = new Dictionary<string, Color?>();
        private readonly TreeNode materialTree = TreeNode.Parse(WorldLayouts.Material), layerTree = TreeNode.Parse(WorldLayouts.TerrainLayer);

        public Colors(GameAssets assets) {
            this.assets = assets;
            foreach (SerializedFile f in assets.files)
                foreach (SerializedFile.ObjectInfo o in f.objects)
                    if (o.classId == MaterialClass || o.classId == TerrainLayerClass)
                        objects[f.name + "#" + o.pathId] = new KeyValuePair<SerializedFile, SerializedFile.ObjectInfo>(f, o);
            foreach (List<AssetEntry> list in assets.textures.Values)
                foreach (AssetEntry e in list) textures[e.file.name + "#" + e.pathId] = e;
        }

        private static readonly Color Grey = new Color(0.7f, 0.7f, 0.7f);

        public Color Material(string key) {
            if (string.IsNullOrEmpty(key)) return Grey;
            Color c;
            if (materialCache.TryGetValue(key, out c)) return c;
            try { c = ReadMaterial(key); } catch (Exception) { c = Grey; }
            materialCache[key] = c;
            return c;
        }

        // The granite texture of one of the game's rock materials ("Rock-..." ones; the rest return null), as a Unity texture
        private readonly Dictionary<string, Texture2D> rockTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> rockTextureByKey = new Dictionary<string, Texture2D>();

        public Texture2D RockTexture(string key) {
            if (string.IsNullOrEmpty(key)) return null;
            Texture2D t;
            if (rockTextures.TryGetValue(key, out t)) return t;
            try { t = ReadRockTexture(key); } catch (Exception) { t = null; }
            rockTextures[key] = t;
            return t;
        }

        private Texture2D ReadRockTexture(string key) {
            KeyValuePair<SerializedFile, SerializedFile.ObjectInfo> found;
            if (!objects.TryGetValue(key, out found) || found.Value.classId != MaterialClass) return null;
            SerializedFile file = found.Key;
            byte[] raw = file.ReadObject(found.Value.byteStart, found.Value.byteSize);
            Dictionary<string, object> d = ReadWanted(materialTree, new DataReader(raw, file.bigEndian), new HashSet<string> { "m_Name", "m_SavedProperties" }, false);
            string materialName = d.TryGetValue("m_Name", out object mn) ? mn as string : null;
            if (materialName == null || !materialName.StartsWith("Rock-", StringComparison.OrdinalIgnoreCase)) return null;
            var props = (Dictionary<string, object>)d["m_SavedProperties"];
            foreach (object o in (List<object>)props["m_TexEnvs"]) {
                var pair = (Dictionary<string, object>)o;
                if ((string)pair["first"] != "_MainTex") continue;
                var ptr = (Dictionary<string, object>)((Dictionary<string, object>)pair["second"])["m_Texture"];
                long id = Convert.ToInt64(ptr["m_PathID"]);
                if (id == 0) return null;
                SerializedFile tf = FileOf(assets, file, (int)Convert.ToInt64(ptr["m_FileID"]));
                if (tf == null) return null;
                string textureKey = tf.name + "#" + id;
                Texture2D tex;
                if (rockTextureByKey.TryGetValue(textureKey, out tex)) return tex;
                AssetEntry e;
                if (!textures.TryGetValue(textureKey, out e)) return null;
                RippedTexture r = TextureReader.Read(e);
                if (r.data == null || r.data.Length == 0 || r.width <= 0 || r.height <= 0) return null;
                tex = new Texture2D(r.width, r.height, (TextureFormat)r.format, r.mipCount, false) { name = "BigWalk Map: " + e.name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                tex.LoadRawTextureData(r.data);
                tex.Apply(false, true);
                rockTextureByKey[textureKey] = tex;
                return tex;
            }
            return null;
        }

        // ---- the map room's miniature world ----
        // The game paints it with MicroSplat (a terrain add-on that runs while the game plays): 12 ground textures blended by three weight maps.
        // This does the same blend once, on the CPU, using one colour per ground type, and gives back a colour map for the mesh's UVs.

        private Texture2D maproom;
        private bool maproomDone;

        public Texture2D Maproom() {
            if (maproomDone) return maproom;
            maproomDone = true;
            try { maproom = BuildMaproom(); } catch (Exception) { maproom = null; }
            return maproom;
        }

        private Texture2D BuildMaproom() {
            string[] names = { "SplatControl0-Modified", "SplatControl1", "SplatControl2" };
            var weights = new Color32[3][];
            var sizes = new int[3];
            for (int i = 0; i < 3; i++) {
                AssetEntry found = null;
                foreach (List<AssetEntry> list in assets.textures.Values)
                    foreach (AssetEntry e in list)
                        if (found == null && e.name == names[i] && e.file.name == "sharedassets2.assets") found = e;
                if (found == null) return null;
                RippedTexture r = TextureReader.Read(found);
                if (r.data == null || r.width <= 0 || r.height <= 0) return null;
                var tex = new Texture2D(r.width, r.height, (TextureFormat)r.format, r.mipCount, true) { hideFlags = HideFlags.HideAndDontSave };
                try {
                    tex.LoadRawTextureData(r.data);
                    tex.Apply(false, false);
                    weights[i] = GpuPixels(tex, r.width, r.height, true);
                } finally { UnityEngine.Object.DestroyImmediate(tex); }
                if (weights[i] == null) return null;
                sizes[i] = r.width;
            }
            Color[] ground = new Color[GroundPalette.Length];
            for (int g = 0; g < ground.Length; g++) ground[g] = GroundPalette[g];

            const int size = 512;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float sr = 0, sg = 0, sb = 0, sum = 0;
                    for (int c = 0; c < 3; c++) {
                        int cx = Mathf.Min(sizes[c] - 1, x * sizes[c] / size), cy = Mathf.Min(sizes[c] - 1, y * sizes[c] / size);
                        Color32 w = weights[c][cy * sizes[c] + cx];
                        float[] ch = { w.r / 255f, w.g / 255f, w.b / 255f, w.a / 255f };
                        for (int k = 0; k < 4; k++) {
                            Color g = ground[c * 4 + k];
                            sr += g.r * ch[k]; sg += g.g * ch[k]; sb += g.b * ch[k]; sum += ch[k];
                        }
                    }
                    pixels[y * size + x] = sum > 0.0001f ? new Color32((byte)(255f * sr / sum), (byte)(255f * sg / sum), (byte)(255f * sb / sum), 255) : new Color32(128, 128, 128, 255);
                }
            var result = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BigWalk Map: miniature colours", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            result.SetPixels32(pixels);
            result.Apply(true, false);
            return result;
        }

        // The game tints its 12 ground textures when it runs, and the tints aren't saved in the files, so these are picked by eye to match how the
        // miniature looks in the game. The index is the weight map channel: 0-3 = SplatControl0, 4-7 = SplatControl1, 8-11 = SplatControl2.
        private static readonly Color32[] GroundPalette = {
            new Color32(35, 65, 140, 255),    // 0  rivers
            new Color32(170, 188, 202, 255),  // 1  sand (also the sea floor: the game lays dark water over it)
            new Color32(100, 110, 70, 255),   // 2  dry grass
            new Color32(110, 110, 115, 255),  // 3  rock
            new Color32(125, 102, 80, 255),   // 4  bare earth
            new Color32(150, 160, 170, 255),  // 5  shore and scree
            new Color32(70, 85, 50, 255),     // 6  grass
            new Color32(60, 90, 65, 255),     // 7  moss
            new Color32(95, 85, 80, 255),     // 8  stony ground
            new Color32(85, 100, 70, 255),    // 9  meadow
            new Color32(125, 100, 95, 255),   // 10 reddish soil
            new Color32(55, 85, 55, 255),     // 11 forest floor
        };

        // The texture's pixels as the graphics card decodes them (compressed formats included). 'linear' for data textures (weights), else the on-screen colours.
        private static Color32[] GpuPixels(Texture2D tex, int w, int h, bool linear) {
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                readback = new Texture2D(w, h, TextureFormat.RGBA32, false, linear) { hideFlags = HideFlags.HideAndDontSave };
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                return readback.GetPixels32();
            } catch (Exception) {
                return null;
            } finally {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private Color ReadMaterial(string key) {
            KeyValuePair<SerializedFile, SerializedFile.ObjectInfo> found;
            if (!objects.TryGetValue(key, out found) || found.Value.classId != MaterialClass) return Grey;
            SerializedFile file = found.Key;
            byte[] raw = file.ReadObject(found.Value.byteStart, found.Value.byteSize);
            Dictionary<string, object> d = ReadWanted(materialTree, new DataReader(raw, file.bigEndian), new HashSet<string> { "m_Name", "m_SavedProperties" }, false);
            var props = (Dictionary<string, object>)d["m_SavedProperties"];

            // Glass (the game's hhGlass materials and its one-way mirrors) is returned as a pale blue with an alpha of 0.25: the preview draws it see-through
            string materialName = d.TryGetValue("m_Name", out object mn) ? mn as string : null;
            if (materialName != null && (materialName.StartsWith("hhGlass", StringComparison.OrdinalIgnoreCase) || materialName.IndexOf("Mirror_OneWay", StringComparison.OrdinalIgnoreCase) >= 0))
                return new Color(0.62f, 0.76f, 0.86f, 0.25f);

            // The game's own "Better Lit" shader keeps the colour of a material in _BaseColour (and paints the texture as detail on top)
            foreach (object o in (List<object>)props["m_Colors"]) {
                var pair = (Dictionary<string, object>)o;
                if ((string)pair["first"] != "_BaseColour") continue;
                var col = (Dictionary<string, object>)pair["second"];
                var v = new Color(Convert.ToSingle(col["r"]), Convert.ToSingle(col["g"]), Convert.ToSingle(col["b"]), 1f);
                if (v.r + v.g + v.b >= 0.05f) return v;
            }

            Color tint = Color.white;
            bool hasTint = false;
            foreach (string name in new[] { "_BaseColor", "_Color" }) {
                foreach (object o in (List<object>)props["m_Colors"]) {
                    var pair = (Dictionary<string, object>)o;
                    if ((string)pair["first"] != name) continue;
                    var col = (Dictionary<string, object>)pair["second"];
                    Color v = new Color(Convert.ToSingle(col["r"]), Convert.ToSingle(col["g"]), Convert.ToSingle(col["b"]), 1f);
                    if (v.r + v.g + v.b < 0.05f) continue;      // black: not a real tint
                    tint = v; hasTint = true;
                    break;
                }
                if (hasTint) break;
            }

            Color? albedo = null;
            var texEnvs = (List<object>)props["m_TexEnvs"];
            foreach (string name in AlbedoNames) {
                foreach (object o in texEnvs) {
                    var pair = (Dictionary<string, object>)o;
                    if ((string)pair["first"] != name) continue;
                    var ptr = (Dictionary<string, object>)((Dictionary<string, object>)pair["second"])["m_Texture"];
                    long id = Convert.ToInt64(ptr["m_PathID"]);
                    if (id == 0) continue;
                    SerializedFile tf = FileOf(assets, file, (int)Convert.ToInt64(ptr["m_FileID"]));
                    if (tf != null) albedo = TextureAverage(tf.name + "#" + id);
                    if (albedo.HasValue) break;
                }
                if (albedo.HasValue) break;
            }
            if (albedo.HasValue) return new Color(tint.r * albedo.Value.r, tint.g * albedo.Value.g, tint.b * albedo.Value.b, 1f);
            return hasTint && tint.r + tint.g + tint.b < 2.9f ? tint : Grey;
        }

        private Color? TextureAverage(string key) {
            Color? c;
            if (textureCache.TryGetValue(key, out c)) return c;
            c = null;
            AssetEntry e;
            if (textures.TryGetValue(key, out e)) {
                try { c = Average(TextureReader.Read(e)); } catch (Exception) { }
            }
            textureCache[key] = c;
            return c;
        }

        // The texture's average colour: the last (smallest) mip when it has mips, else a spread of samples.
        // DXT1 (10), DXT5 (12) and RGBA32 (4) are worked out here; other formats are decoded by the GPU.
        private static Color? Average(RippedTexture t) {
            byte[] data = t.data;
            if (data == null || data.Length == 0) return null;
            float r = 0, g = 0, b = 0;
            int n = 0;
            if (t.format == 10 || t.format == 12) {
                int blockSize = t.format == 10 ? 8 : 16, blocks = data.Length / blockSize;
                if (blocks == 0) return null;
                int samples = t.mipCount > 1 ? 1 : Math.Min(256, blocks);
                for (int i = 0; i < samples; i++) {
                    int block = t.mipCount > 1 ? blocks - 1 : (int)((long)i * blocks / samples);
                    BlockAverage(data, block * blockSize, t.format == 12, ref r, ref g, ref b);
                    n++;
                }
            } else if (t.format == 4) {
                int pixels = data.Length / 4;
                int samples = t.mipCount > 1 ? 1 : Math.Min(1024, pixels);
                for (int i = 0; i < samples; i++) {
                    int p = t.mipCount > 1 ? pixels - 1 : (int)((long)i * pixels / samples);
                    r += data[p * 4] / 255f; g += data[p * 4 + 1] / 255f; b += data[p * 4 + 2] / 255f;
                    n++;
                }
            } else return GpuAverage(t);
            return n == 0 ? (Color?)null : new Color(r / n, g / n, b / n, 1f);
        }

        // Any other format (BC7 and so on): let the GPU decode it. The texture is drawn onto one pixel, which picks its smallest mip.
        private static Color? GpuAverage(RippedTexture t) {
            if (t.format == 1 || t.mipCount <= 1 || t.width <= 0 || t.height <= 0) return null;     // Alpha8 is a mask, not a colour
            Texture2D tex = null;
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            try {
                tex = new Texture2D(t.width, t.height, (TextureFormat)t.format, t.mipCount, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
                tex.LoadRawTextureData(t.data);
                tex.Apply(false, true);
                rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var one = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                Color c = one.GetPixel(0, 0);
                UnityEngine.Object.DestroyImmediate(one);
                return new Color(c.r, c.g, c.b, 1f);
            } catch (Exception) {
                return null;
            } finally {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // Adds the average colour of one 4x4 DXT block (the colour part: after the 8 alpha bytes in DXT5)
        private static void BlockAverage(byte[] d, int offset, bool dxt5, ref float r, ref float g, ref float b) {
            int o = dxt5 ? offset + 8 : offset;
            int c0 = d[o] | d[o + 1] << 8, c1 = d[o + 2] | d[o + 3] << 8;
            uint indices = (uint)(d[o + 4] | d[o + 5] << 8 | d[o + 6] << 16 | d[o + 7] << 24);
            Vector3 p0 = Rgb565(c0), p1 = Rgb565(c1);
            var palette = new Vector3[4];
            bool four = c0 > c1 || dxt5;
            palette[0] = p0; palette[1] = p1;
            palette[2] = four ? (2 * p0 + p1) / 3f : (p0 + p1) / 2f;
            palette[3] = (p0 + 2 * p1) / 3f;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < 16; i++) {
                int index = (int)((indices >> (2 * i)) & 3);
                if (!four && index == 3) continue;          // transparent
                sum += palette[index];
                count++;
            }
            if (count == 0) return;
            r += sum.x / count; g += sum.y / count; b += sum.z / count;
        }

        private static Vector3 Rgb565(int v) {
            return new Vector3(((v >> 11) & 31) / 31f, ((v >> 5) & 63) / 63f, (v & 31) / 31f);
        }

        // The terrain's colour at each splat-map pixel: each layer's average colour weighted by how much of that layer is painted there
        public void PaintTerrain(WorldTerrain terrain, SerializedFile dataFile, Dictionary<string, object> splat) {
            var layers = (List<object>)splat["m_TerrainLayers"];
            var alphas = (List<object>)splat["m_AlphaTextures"];
            int res = (int)Convert.ToInt64(splat["m_AlphamapResolution"]);
            if (layers.Count == 0 || alphas.Count == 0 || res <= 0) return;

            var layerColors = new Vector3[layers.Count];
            for (int i = 0; i < layers.Count; i++) {
                layerColors[i] = new Vector3(0.5f, 0.5f, 0.5f);
                var ptr = (Dictionary<string, object>)layers[i];
                SerializedFile lf = FileOf(assets, dataFile, (int)Convert.ToInt64(ptr["m_FileID"]));
                KeyValuePair<SerializedFile, SerializedFile.ObjectInfo> found;
                if (lf == null || !objects.TryGetValue(lf.name + "#" + Convert.ToInt64(ptr["m_PathID"]), out found) || found.Value.classId != TerrainLayerClass) continue;
                byte[] raw = found.Key.ReadObject(found.Value.byteStart, found.Value.byteSize);
                Dictionary<string, object> d = ReadWanted(layerTree, new DataReader(raw, found.Key.bigEndian), new HashSet<string> { "m_Name", "m_DiffuseTexture" }, false);
                var tex = (Dictionary<string, object>)d["m_DiffuseTexture"];
                long id = Convert.ToInt64(tex["m_PathID"]);
                SerializedFile tf = id == 0 ? null : FileOf(assets, found.Key, (int)Convert.ToInt64(tex["m_FileID"]));
                Color? c = tf == null ? null : TextureAverage(tf.name + "#" + id);
                if (c.HasValue) layerColors[i] = new Vector3(c.Value.r, c.Value.g, c.Value.b);
                // The sand layers' textures are nearly neutral grey-white; in the game they read as warm sand, so give them that tint
                if (d.TryGetValue("m_Name", out object layerName) && layerName is string ln && ln.IndexOf("sand", StringComparison.OrdinalIgnoreCase) >= 0)
                    layerColors[i] = new Vector3(Mathf.Min(1f, layerColors[i].x * 1.12f), layerColors[i].y * 0.95f, layerColors[i].z * 0.70f);
            }

            var maps = new byte[alphas.Count][];
            for (int i = 0; i < alphas.Count; i++) {
                var ptr = (Dictionary<string, object>)alphas[i];
                SerializedFile af = FileOf(assets, dataFile, (int)Convert.ToInt64(ptr["m_FileID"]));
                AssetEntry e;
                if (af != null && textures.TryGetValue(af.name + "#" + Convert.ToInt64(ptr["m_PathID"]), out e)) {
                    RippedTexture t = TextureReader.Read(e);
                    if (t.format == 4 && t.data.Length >= res * res * 4) maps[i] = t.data;
                }
            }

            var pixels = new Color32[res * res];
            for (int p = 0; p < pixels.Length; p++) {
                Vector3 sum = Vector3.zero;
                float weight = 0f;
                for (int l = 0; l < layers.Count; l++) {
                    byte[] map = l / 4 < maps.Length ? maps[l / 4] : null;
                    if (map == null) continue;
                    float w = map[p * 4 + l % 4] / 255f;
                    sum += layerColors[l] * w;
                    weight += w;
                }
                Vector3 c = weight > 0.001f ? sum / weight : new Vector3(0.5f, 0.5f, 0.5f);
                // The game has water over the low ground (the preview doesn't), which makes the pale sand look blue-green and darker with depth
                if (terrain.heights != null && terrain.resolution > 1) {
                    int hx = Mathf.Min(terrain.resolution - 1, (p % res) * (terrain.resolution - 1) / Mathf.Max(1, res - 1));
                    int hz = Mathf.Min(terrain.resolution - 1, (p / res) * (terrain.resolution - 1) / Mathf.Max(1, res - 1));
                    float y = terrain.position.y + terrain.heights[hz * terrain.resolution + hx] * terrain.scale.y / 32766f;
                    float depth = Mathf.Clamp01((SeaLevel - y) / 25f);
                    if (y < SeaLevel) {
                        var water = new Vector3(0.10f, 0.30f, 0.35f);
                        float amount = 0.35f + 0.45f * depth;
                        c = Vector3.Lerp(c * (1f - 0.3f * depth), water, amount);
                    }
                }
                pixels[p] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(c.x * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.y * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.z * 255f), 0, 255), 255);
            }
            terrain.colorResolution = res;
            terrain.colors = pixels;
        }
    }

    // ---- hierarchy ----

    private static Matrix4x4 World(long id, Dictionary<long, TransformInfo> transforms, Dictionary<long, Matrix4x4> cache) {
        if (cache.TryGetValue(id, out Matrix4x4 m)) return m;
        TransformInfo t = transforms[id];
        Matrix4x4 local = Matrix4x4.TRS(t.position, t.rotation, t.scale);
        m = t.father != 0 && transforms.ContainsKey(t.father) ? World(t.father, transforms, cache) * local : local;
        cache[id] = m;
        return m;
    }

    // True when the object or any of its parents has this name
    private static bool IsUnderName(long transformId, Dictionary<long, TransformInfo> transforms, Dictionary<long, KeyValuePair<string, bool>> gameObjects, string name) {
        for (int depth = 0; depth < 200 && transforms.TryGetValue(transformId, out TransformInfo t); depth++) {
            if (gameObjects.TryGetValue(t.gameObject, out var go) && go.Key == name) return true;
            if (t.father == 0) break;
            transformId = t.father;
        }
        return false;
    }

    // True when the object or any of its parents is one of HiddenNames
    private static bool IsHidden(long transformId, Dictionary<long, TransformInfo> transforms, Dictionary<long, KeyValuePair<string, bool>> gameObjects) {
        for (int depth = 0; depth < 200 && transforms.TryGetValue(transformId, out TransformInfo t); depth++) {
            if (gameObjects.TryGetValue(t.gameObject, out var go)) {
                if (HiddenNames.Contains(GroupOf(go.Key))) return true;
                foreach (string prefix in HiddenPrefixes)
                    if (go.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (string other in OtherPlayerCounts)
                    if (go.Key.IndexOf(other, StringComparison.Ordinal) >= 0) return true;
            }
            if (t.father == 0) break;
            transformId = t.father;
        }
        return false;
    }

    private static bool ActiveInHierarchy(long transformId, Dictionary<long, TransformInfo> transforms,
                                          Dictionary<long, KeyValuePair<string, bool>> gameObjects, Dictionary<long, bool> cache) {
        if (cache.TryGetValue(transformId, out bool known)) return known;
        TransformInfo t = transforms[transformId];
        bool active = gameObjects.TryGetValue(t.gameObject, out var go) && go.Value;
        if (active && t.father != 0 && transforms.ContainsKey(t.father)) active = ActiveInHierarchy(t.father, transforms, gameObjects, cache);
        cache[transformId] = active;
        return active;
    }

    private static SerializedFile FileOf(GameAssets assets, SerializedFile scene, int fileId) {
        string name = fileId == 0 ? scene.name : (fileId - 1 < scene.externals.Count ? System.IO.Path.GetFileName(scene.externals[fileId - 1]) : null);
        return name == null ? null : assets.files.Find(f => f.name == name);
    }

    // The game keeps several copies of an object for different distances (name_LOD0, _LOD1, ...): only LOD0 is shown
    private static bool IsLowerDetail(string name) {
        int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
        return i >= 0 && i + 4 < name.Length && name[i + 4] != '0';
    }

    // "rock-bullet (5)" and "rock-bullet_LOD0" -> "rock-bullet"
    private static string GroupOf(string name) {
        int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
        if (i > 0) name = name.Substring(0, i);
        i = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (i > 0 && name.EndsWith(")", StringComparison.Ordinal)) name = name.Substring(0, i);
        return name;
    }

    // ---- serialized values ----

    private static long PathId(object pptr) { return Convert.ToInt64(((Dictionary<string, object>)pptr)["m_PathID"]); }

    private static Vector3 Vec(object v) {
        var d = (Dictionary<string, object>)v;
        return new Vector3(Convert.ToSingle(d["x"]), Convert.ToSingle(d["y"]), Convert.ToSingle(d["z"]));
    }

    private static Quaternion Quat(object v) {
        var d = (Dictionary<string, object>)v;
        return new Quaternion(Convert.ToSingle(d["x"]), Convert.ToSingle(d["y"]), Convert.ToSingle(d["z"]), Convert.ToSingle(d["w"]));
    }

    // Reads the wanted top-level fields of an object and skips the rest
    private static Dictionary<string, object> ReadWanted(TreeNode root, DataReader r, HashSet<string> wanted, bool rawFixed) {
        var result = new Dictionary<string, object>();
        foreach (TreeNode child in root.children) {
            if (wanted.Contains(child.name)) result[child.name] = ReadValue(child, r, rawFixed);
            else Skip(child, r);
        }
        if (root.Aligned) r.Align(4);
        return result;
    }

    // Like TreeReader.Read; with rawFixed, arrays of numbers come back as the raw bytes (a RawArray)
    private static object ReadValue(TreeNode node, DataReader r, bool rawFixed) {
        object value;
        bool align = node.Aligned;
        switch (node.type) {
            case "SInt8": value = (long)(sbyte)r.U8(); break;
            case "UInt8": case "char": value = (long)r.U8(); break;
            case "short": case "SInt16": value = (long)r.I16(); break;
            case "unsigned short": case "UInt16": value = (long)r.U16(); break;
            case "int": case "SInt32": value = (long)r.I32(); break;
            case "unsigned int": case "UInt32": case "Type*": value = (long)r.U32(); break;
            case "long long": case "SInt64": value = r.I64(); break;
            case "unsigned long long": case "UInt64": case "FileSize": value = (long)r.U64(); break;
            case "float": value = (double)r.F32(); break;
            case "double": value = r.F64(); break;
            case "bool": value = r.Bool(); break;
            case "string": value = r.AlignedString(); break;
            default:
                if (node.children.Count > 0 && node.children[0].type == "Array") {
                    TreeNode array = node.children[0];
                    if (array.Aligned) align = true;
                    int count = r.I32();
                    if (count < 0) throw new GameAssetException("Negative array size.");
                    TreeNode item = array.children[1];
                    int itemSize = NumberSize(item.type);
                    if (rawFixed && itemSize > 0) {
                        value = new RawArray { data = r.Bytes(checked(count * itemSize)) };
                        if (item.Aligned) r.Align(4);
                    } else {
                        var list = new List<object>(count);
                        for (int i = 0; i < count; i++) list.Add(ReadValue(item, r, rawFixed));
                        value = list;
                    }
                } else {
                    var dict = new Dictionary<string, object>();
                    foreach (TreeNode child in node.children) dict[child.name] = ReadValue(child, r, rawFixed);
                    value = dict;
                }
                break;
        }
        if (align) r.Align(4);
        return value;
    }

    private static int NumberSize(string type) {
        switch (type) {
            case "SInt8": case "UInt8": case "char": case "bool": return 1;
            case "short": case "SInt16": case "unsigned short": case "UInt16": return 2;
            case "int": case "SInt32": case "unsigned int": case "UInt32": case "float": return 4;
            case "long long": case "SInt64": case "unsigned long long": case "UInt64": case "double": return 8;
            default: return 0;
        }
    }

    private static void Skip(TreeNode node, DataReader r) {
        bool align = node.Aligned;
        int size = NumberSize(node.type);
        if (size > 0) r.Skip(size);
        else if (node.type == "string") { int len = r.I32(); r.Skip(len); align = true; }
        else if (node.type == "TypelessData") { int len = r.I32(); r.Skip(len); }
        else if (node.byteSize > 0) r.Skip(node.byteSize);
        else if (node.children.Count > 0 && node.children[0].type == "Array") {
            TreeNode array = node.children[0];
            if (array.Aligned) align = true;
            int count = r.I32();
            if (count < 0) throw new GameAssetException("Negative array size.");
            TreeNode item = array.children[1];
            if (item.byteSize > 0 && item.type != "string") r.Skip(checked(count * item.byteSize));
            else for (int i = 0; i < count; i++) Skip(item, r);
        } else {
            foreach (TreeNode child in node.children) Skip(child, r);
        }
        if (align) r.Align(4);
    }
}

}
