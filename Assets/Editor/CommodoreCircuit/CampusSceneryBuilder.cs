using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Build Alumni Lawn Scenery (new scene)
    ///
    /// Creates a new scene ("Assets/Alumni Lawn Scenery.unity") containing ONLY scenery built from
    /// OpenStreetMap data (Assets/CampusScenery/alumni_scenery.json): Alumni Lawn, the real building
    /// footprints around it, roads, footpaths, trees, lamp posts and benches.
    ///
    /// Every piece is its own GameObject (one per building, road, path, lawn, tree, lamp post and
    /// bench) with its pivot at its own center, sorted into folders: Buildings, Roads, Footpaths,
    /// Lawns, Trees, LampPosts, Benches. Select one and move/rotate it freely.
    /// Trees, lamp posts and benches share one mesh each, so they are cheap and easy to duplicate.
    ///
    /// Visual only: no colliders, no scooter, no scripts. Everything is marked Batching Static so
    /// the many objects are merged into few draw calls in a build (good for the Quest).
    /// Re-running REPLACES the scene file, so manual moves are lost: duplicate the scene first if
    /// you want to keep a hand-edited version.
    /// Map data (c) OpenStreetMap contributors (ODbL).
    /// </summary>
    public static class CampusSceneryBuilder
    {
        private const string DataPath = "Assets/CampusScenery/alumni_scenery.json";
        private const string MeshDir = "Assets/CampusScenery/Meshes";
        private const string MatDir = "Assets/CampusScenery/Materials";
        private const string ScenePath = "Assets/Alumni Lawn Scenery.unity";
        private const string RootName = "Alumni Lawn Scenery";

        [Serializable] private class Sub { public string mat; public int[] tris; }
        [Serializable] private class Obj { public string name; public string group; public float x, z; public float[] verts; public Sub[] subs; }
        [Serializable] private class Inst { public float x, z, rot, scale; public int mesh, canopy; }
        [Serializable] private class GroundData { public float[] center; public float[] size; }
        [Serializable] private class LawnInfo { public float area; public float[] centroid; public float[] bounds; }
        [Serializable]
        private class Meta
        {
            public double originLat, originLon;
            public float radius;
            public string credit;
            public int buildings, trees, lamps, benches, roads, totalTris;
            public LawnInfo alumniLawn;
            public float[] cameraPos, cameraTarget;
        }
        [Serializable]
        private class SceneryData
        {
            public Meta meta;
            public GroundData ground;
            public Obj[] objects;
            public Obj[] treeMeshes;
            public Inst[] trees;
            public Obj lampMesh;
            public Inst[] lamps;
            public Obj benchMesh;
            public Inst[] benches;
        }

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static readonly HashSet<string> UsedMeshNames = new HashSet<string>();
        private static Material[] canopyMats;

        [MenuItem("CommodoreCircuit/Build Alumni Lawn Scenery (new scene)")]
        private static void Build()
        {
            AssetDatabase.ImportAsset(DataPath, ImportAssetOptions.ForceUpdate); // make sure we read the newest data file
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (json == null)
            {
                EditorUtility.DisplayDialog("Scenery data missing",
                    "Could not find " + DataPath + ".\nMake sure alumni_scenery.json is inside Assets/CampusScenery.", "OK");
                return;
            }

            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Replace the scenery scene?",
                    "This rebuilds " + ScenePath + " from the data file.\nAnything you moved or edited by hand in that scene will be lost.\n\nTip: duplicate the scene first (Cmd+D in the Project window) to keep a hand-edited copy.",
                    "Rebuild", "Cancel"))
                return;

            // Never lose unsaved work in whatever scene is open now.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            SceneryData data = JsonUtility.FromJson<SceneryData>(json.text);
            if (data == null || data.objects == null || data.objects.Length == 0)
            {
                EditorUtility.DisplayDialog("Scenery data is the old format",
                    DataPath + " has no 'objects' list, so it is an older version of the file. Nothing was changed. Ask Claude for the newest alumni_scenery.json.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder("Assets/CampusScenery");
            EnsureFolder(MeshDir);
            EnsureFolder(MatDir);
            UsedMeshNames.Clear();
            Mats.Clear();
            MakeMaterials();

            // Create every mesh folder up front (folders can't be created reliably inside the batch below).
            if (data.objects != null)
                foreach (Obj o in data.objects) FolderFor(o.group);
            EnsureFolder(MeshDir + "/Props");

            GameObject root = new GameObject(RootName);
            var groups = new Dictionary<string, Transform>();
            int built = 0;

            AssetDatabase.StartAssetEditing(); // batch the many mesh assets: much faster
            try
            {
                Part(Group(root, groups, "Ground"), "Ground", Quad(data.ground), new[] { Mats["Ground"] }, Vector3.zero, 0f, 1f);

                if (data.objects != null)
                {
                    foreach (Obj o in data.objects)
                    {
                        Mesh m = MakeMesh(o.name, MeshDir + "/" + FolderFor(o.group), o);
                        Part(Group(root, groups, o.group), o.name, m, MatsFor(o), new Vector3(o.x, 0f, o.z), 0f, 1f);
                        built++;
                    }
                }

                // Trees: a few shared shapes, one object per tree (position, yaw, size, canopy color).
                if (data.treeMeshes != null && data.trees != null)
                {
                    Mesh[] shapes = new Mesh[data.treeMeshes.Length];
                    for (int i = 0; i < shapes.Length; i++)
                        shapes[i] = MakeMesh(data.treeMeshes[i].name, MeshDir + "/Props", data.treeMeshes[i]);
                    Transform parent = Group(root, groups, "Trees");
                    for (int i = 0; i < data.trees.Length; i++)
                    {
                        Inst t = data.trees[i];
                        Obj def = data.treeMeshes[Mathf.Clamp(t.mesh, 0, shapes.Length - 1)];
                        Material[] ms = new Material[def.subs.Length];
                        for (int s = 0; s < ms.Length; s++)
                            ms[s] = def.subs[s].mat == "Canopy" ? canopyMats[Mathf.Clamp(t.canopy, 0, 2)] : Mats[def.subs[s].mat];
                        Part(parent, "Tree_" + (i + 1).ToString("000"), shapes[Mathf.Clamp(t.mesh, 0, shapes.Length - 1)], ms,
                            new Vector3(t.x, 0f, t.z), t.rot, t.scale <= 0f ? 1f : t.scale);
                        built++;
                    }
                }

                built += Scatter(root, groups, "LampPosts", "LampPost_", data.lampMesh, data.lamps);
                built += Scatter(root, groups, "Benches", "Bench_", data.benchMesh, data.benches);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Credit required by the OpenStreetMap licence (ODbL). Also put it in the game's credits screen.
            GameObject credit = new GameObject("Map data (c) OpenStreetMap contributors");
            credit.transform.SetParent(root.transform, false);

            // Viewing spot: on the lawn edge, looking across Alumni Lawn.
            Camera cam = Camera.main;
            if (cam != null && data.meta.cameraPos != null && data.meta.cameraPos.Length == 3)
            {
                Vector3 p = new Vector3(data.meta.cameraPos[0], data.meta.cameraPos[1], data.meta.cameraPos[2]);
                Vector3 t = new Vector3(data.meta.cameraTarget[0], data.meta.cameraTarget[1], data.meta.cameraTarget[2]);
                cam.transform.position = p;
                cam.transform.rotation = Quaternion.LookRotation((t - p).normalized, Vector3.up);
                cam.fieldOfView = 70f;
                cam.farClipPlane = 600f;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null && data.meta.alumniLawn != null && data.meta.alumniLawn.centroid != null)
            {
                Vector3 c = new Vector3(data.meta.alumniLawn.centroid[0], 0f, data.meta.alumniLawn.centroid[1]);
                SceneView.lastActiveSceneView.LookAt(c, Quaternion.Euler(38f, -25f, 0f), 170f);
            }

            Debug.Log($"Alumni Lawn scenery built: {built} separate objects ({data.meta.buildings} buildings, {data.meta.roads} road pieces, {data.meta.trees} trees, {data.meta.lamps} lamp posts). Saved to {ScenePath}.");
            EditorUtility.DisplayDialog("Alumni Lawn scenery built",
                $"New scene saved: {ScenePath}\n\n{built} separate objects: {data.meta.buildings} buildings, {data.meta.roads} road pieces, {data.meta.trees} trees, {data.meta.lamps} lamp posts, {data.meta.benches} benches.\n\nOpen the folders in the Hierarchy and move any piece on its own. No colliders and no scooter yet.",
                "OK");
        }

        // ------------------------------------------------------------------ building blocks

        private static int Scatter(GameObject root, Dictionary<string, Transform> groups, string group, string prefix, Obj def, Inst[] insts)
        {
            if (def == null || def.verts == null || insts == null || insts.Length == 0) return 0;
            Mesh shape = MakeMesh(def.name, MeshDir + "/Props", def);
            Material[] ms = MatsFor(def);
            Transform parent = Group(root, groups, group);
            for (int i = 0; i < insts.Length; i++)
                Part(parent, prefix + (i + 1).ToString("000"), shape, ms, new Vector3(insts[i].x, 0f, insts[i].z), insts[i].rot, insts[i].scale <= 0f ? 1f : insts[i].scale);
            return insts.Length;
        }

        private static Transform Group(GameObject root, Dictionary<string, Transform> groups, string name)
        {
            if (!groups.TryGetValue(name, out Transform t))
            {
                GameObject g = new GameObject(name);
                g.transform.SetParent(root.transform, false);
                groups[name] = t = g.transform;
            }
            return t;
        }

        private static string FolderFor(string group)
        {
            string f = string.IsNullOrEmpty(group) ? "Other" : group;
            string path = MeshDir + "/" + f;
            EnsureFolder(path);
            return f;
        }

        private static Material[] MatsFor(Obj o)
        {
            Material[] ms = new Material[o.subs.Length];
            for (int i = 0; i < ms.Length; i++)
            {
                if (!Mats.TryGetValue(o.subs[i].mat, out ms[i]) || ms[i] == null)
                {
                    Debug.LogWarning("Scenery: unknown material '" + o.subs[i].mat + "' on " + o.name);
                    ms[i] = Mats["Ground"];
                }
            }
            return ms;
        }

        private static GameObject Part(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 pos, float yaw, float scale)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off; // keep it light for the Quest
            mr.receiveShadows = false;
            // Many small objects are merged into few draw calls in a build. Pieces stay individually movable in the editor.
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go; // deliberately NO collider: visual only
        }

        private static Mesh MakeMesh(string name, string folder, Obj d)
        {
            int n = d.verts.Length / 3;
            Mesh m = new Mesh { name = name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            Vector3[] v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(d.verts[i * 3], d.verts[i * 3 + 1], d.verts[i * 3 + 2]);
            m.vertices = v;
            m.subMeshCount = d.subs.Length;
            for (int i = 0; i < d.subs.Length; i++) m.SetTriangles(d.subs[i].tris, i);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return SaveMesh(m, folder);
        }

        private static Mesh Quad(GroundData g)
        {
            // Tiled grid instead of one giant quad (huge triangles cause lighting/physics warnings later).
            float cx = g.center[0], cz = g.center[1], sx = g.size[0], sz = g.size[1];
            int nx = Mathf.Max(1, Mathf.CeilToInt(sx / 60f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(sz / 60f));
            Vector3[] verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int zi = 0; zi <= nz; zi++)
                for (int xi = 0; xi <= nx; xi++)
                    verts[zi * (nx + 1) + xi] = new Vector3(cx - sx / 2f + sx * xi / nx, 0f, cz - sz / 2f + sz * zi / nz);
            int[] tris = new int[nx * nz * 6];
            int t = 0;
            for (int zi = 0; zi < nz; zi++)
            {
                for (int xi = 0; xi < nx; xi++)
                {
                    int a = zi * (nx + 1) + xi, b = a + 1, c = a + (nx + 1), d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d; // same winding as the campus track ground (faces up)
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                }
            }
            Mesh m = new Mesh { name = "Ground", indexFormat = IndexFormat.UInt32 };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return SaveMesh(m, MeshDir);
        }

        private static Mesh SaveMesh(Mesh m, string folder)
        {
            string safe = m.name;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            safe = safe.Replace('/', '_').Replace('\\', '_');
            string key = folder + "/" + safe;
            string unique = key;
            int k = 2;
            while (!UsedMeshNames.Add(unique)) unique = key + "_" + k++;
            AssetDatabase.CreateAsset(m, unique + ".asset");
            return m;
        }

        // ------------------------------------------------------------------ materials

        private static void MakeMaterials()
        {
            Mats["Ground"] = Mat("Ground", new Color(0.42f, 0.56f, 0.33f), 0.0f);
            Mats["Lawn"] = Mat("AlumniLawn", new Color(0.48f, 0.70f, 0.32f), 0.0f);
            Mats["Flowerbed"] = Mat("Flowerbed", new Color(0.55f, 0.30f, 0.36f), 0.0f);
            Mats["Pavement"] = Mat("Pavement", new Color(0.78f, 0.75f, 0.69f), 0.05f);
            Mats["Asphalt"] = Mat("Asphalt", new Color(0.22f, 0.23f, 0.25f), 0.05f);
            Mats["RoadLine"] = Mat("RoadLine", new Color(0.90f, 0.80f, 0.20f), 0.05f);
            Mats["Brick"] = Mat("Brick", new Color(0.60f, 0.29f, 0.22f), 0.05f);
            Mats["TanBrick"] = Mat("TanBrick", new Color(0.80f, 0.70f, 0.52f), 0.05f);
            Mats["Limestone"] = Mat("Limestone", new Color(0.80f, 0.78f, 0.72f), 0.05f);
            Mats["WhitePaint"] = Mat("WhitePaint", new Color(0.92f, 0.91f, 0.87f), 0.05f);
            Mats["Windows"] = Mat("Windows", new Color(0.16f, 0.22f, 0.28f), 0.7f);
            Mats["RoofSlate"] = Mat("RoofSlate", new Color(0.26f, 0.28f, 0.32f), 0.1f);
            Mats["RoofFlat"] = Mat("RoofFlat", new Color(0.42f, 0.42f, 0.44f), 0.05f);
            Mats["Trunk"] = Mat("Trunk", new Color(0.33f, 0.23f, 0.15f), 0.0f);
            Mats["LampPost"] = Mat("LampPost", new Color(0.10f, 0.11f, 0.11f), 0.3f);
            Mats["LampHead"] = Mat("LampHead", new Color(1.00f, 0.92f, 0.60f), 0.2f);
            Mats["Bench"] = Mat("Bench", new Color(0.40f, 0.28f, 0.18f), 0.1f);
            canopyMats = new[]
            {
                Mat("CanopyGreen", new Color(0.22f, 0.44f, 0.17f), 0.0f),
                Mat("CanopyLight", new Color(0.30f, 0.52f, 0.20f), 0.0f),
                Mat("CanopyAutumn", new Color(0.82f, 0.58f, 0.15f), 0.0f),
            };
            Material head = Mats["LampHead"];
            if (head.HasProperty("_EmissionColor"))
            {
                head.EnableKeyword("_EMISSION");
                head.SetColor("_EmissionColor", new Color(1.0f, 0.85f, 0.5f) * 1.2f); // warm glow
            }
        }

        private static Material Mat(string name, Color color, float smoothness)
        {
            string path = MatDir + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
