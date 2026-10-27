using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Build Campus Track
    ///
    /// Builds the Vanderbilt grey-box from Assets/CampusTrack/campus_track.json
    /// (traced from the map: road, walls, speed bumps, the Rand tunnel, grey
    /// buildings and lawns). Meshes and materials are saved as assets and the
    /// objects live in the scene, so nothing is rebuilt at runtime. Running it
    /// again replaces the old "Campus Track" object. The old test oval and
    /// plane are switched off, and the scooter is moved to the start line.
    /// </summary>
    public static class CampusTrackBuilder
    {
        private const string DataPath = "Assets/CampusTrack/campus_track.json";
        private const string MeshDir = "Assets/CampusTrack/Meshes";
        private const string MatDir = "Assets/CampusTrack/Materials";
        private const string RootName = "Campus Track";

        [Serializable] private class MeshData { public float[] verts; public int[] tris; }
        [Serializable] private class WallData { public string name; public string kind; public float[] verts; public int[] tris; }
        [Serializable] private class Meta { public float lapLength; public float baseWidth; public int bumps; public int sBends; public float tunnelStart; public float tunnelLength; }
        [Serializable] private class StartData { public float[] pos; public float yaw; }
        [Serializable] private class GroundData { public float[] center; public float[] size; }
        [Serializable]
        private class TrackData
        {
            public Meta meta;
            public StartData start;
            public GroundData ground;
            public MeshData roadAsphalt, roadConcrete, roadWhite, grass, dashes, startLine, bumps, tunnel, houses, civic, lawns;
            public WallData[] walls;
        }

        [MenuItem("CommodoreCircuit/Build Campus Track")]
        private static void Build()
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (json == null)
            {
                EditorUtility.DisplayDialog("Track data missing",
                    "Could not find " + DataPath + ".\nMake sure campus_track.json is inside Assets/CampusTrack.", "OK");
                return;
            }

            TrackData data = JsonUtility.FromJson<TrackData>(json.text);
            EnsureFolder("Assets/CampusTrack");

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            // Start from clean mesh assets so pieces from an older layout don't pile up.
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder(MeshDir);
            EnsureFolder(MatDir);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Campus Track");

            Material asphalt = Mat("Asphalt", new Color(0.22f, 0.23f, 0.25f));
            Material concrete = Mat("Concrete", new Color(0.74f, 0.71f, 0.66f));
            Material whiteRoad = Mat("RandWhiteRoad", new Color(0.95f, 0.95f, 0.93f));
            Material grassMat = Mat("RoughGrass", new Color(0.30f, 0.52f, 0.22f));
            Material hedge = Mat("Hedge", new Color(0.15f, 0.36f, 0.16f));
            Material startMat = Mat("StartLine", new Color(0.05f, 0.05f, 0.05f));
            Material white = Mat("Paint", new Color(0.95f, 0.95f, 0.92f));
            Material bump = Mat("SpeedBump", new Color(0.98f, 0.72f, 0.12f));
            Material wall = Mat("Wall", new Color(0.62f, 0.66f, 0.72f));
            Material tunnel = Mat("RandTunnel", new Color(0.86f, 0.80f, 0.66f));
            Material house = Mat("Building", new Color(0.60f, 0.62f, 0.65f));
            Material civic = Mat("Civic", new Color(0.88f, 0.82f, 0.68f));
            Material lawn = Mat("Lawn", new Color(0.55f, 0.76f, 0.52f));
            Material ground = Mat("Ground", new Color(0.80f, 0.82f, 0.78f));

            // Ground quad under everything.
            GameObject groundGo = Part(root, "Ground", Quad(data.ground), ground, true, false);
            groundGo.transform.position = new Vector3(0f, 0f, 0f);

            Part(root, "Lawns", Build("Lawns", data.lawns), lawn, false, false);
            Part(root, "Road_Asphalt", Build("RoadAsphalt", data.roadAsphalt), asphalt, true, false);
            Part(root, "Road_Concrete_AlumniLawn", Build("RoadConcrete", data.roadConcrete), concrete, true, false);
            Part(root, "Road_White_Rand", Build("RoadWhite", data.roadWhite), whiteRoad, true, false);

            // Rough grass beside the lawn path: slows the scooter down.
            GameObject grassGo = Part(root, "RoughGrass", Build("Grass", data.grass), grassMat, true, false);
            grassGo.AddComponent<TrackSurface>();

            Part(root, "CenterDashes", Build("Dashes", data.dashes), white, false, false);
            Part(root, "StartLine", Build("StartLine", data.startLine), startMat, false, false);
            Part(root, "SpeedBumps", Build("Bumps", data.bumps), bump, true, false);
            Part(root, "RandTunnel", Build("Tunnel", data.tunnel), tunnel, false, true);
            Part(root, "Buildings", Build("Houses", data.houses), house, false, true);
            Part(root, "CivicBuildings", Build("Civic", data.civic), civic, false, true);

            GameObject walls = new GameObject("Walls");
            walls.transform.SetParent(root.transform, false);
            foreach (WallData w in data.walls)
            {
                MeshData md = new MeshData { verts = w.verts, tris = w.tris };
                bool isHedge = w.kind == "hedge";
                GameObject piece = Part(walls, w.name, Build("Wall_" + w.name, md), isHedge ? hedge : wall, true, true);
                piece.AddComponent<TrackWall>();
            }

            DisableOld("Test Track");
            DisableOld("Plane");
            PlaceScooter(data.start);

            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log($"Campus track built: lap {data.meta.lapLength:0} m, {data.meta.bumps} bumps, tunnel {data.meta.tunnelLength:0} m. Save the scene (Cmd+S).");
            EditorUtility.DisplayDialog("Campus track built",
                $"Lap length about {data.meta.lapLength:0} m, {data.meta.bumps} speed bumps, Rand tunnel {data.meta.tunnelLength:0} m.\n\nThe scooter is on the start line facing the lap direction. Save the scene (Cmd+S), then press Play.",
                "OK");
        }

        private static void PlaceScooter(StartData s)
        {
            ScooterController scooter = UnityEngine.Object.FindAnyObjectByType<ScooterController>();
            if (scooter == null) return;
            Vector3 pos = new Vector3(s.pos[0], s.pos[1], s.pos[2]);
            Quaternion rot = Quaternion.Euler(0f, s.yaw, 0f);
            Undo.RecordObject(scooter.transform, "Place scooter");
            scooter.transform.SetPositionAndRotation(pos, rot);
            Rigidbody rb = scooter.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Undo.RecordObject(rb, "Place scooter");
                rb.position = pos;
                rb.rotation = rot;
            }
            EditorUtility.SetDirty(scooter);
        }

        private static void DisableOld(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go == null || !go.activeSelf) return;
            Undo.RecordObject(go, "Disable " + name);
            go.SetActive(false);
        }

        private static GameObject Part(GameObject parent, string name, Mesh mesh, Material mat, bool collider, bool castShadow)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static Mesh Build(string name, MeshData d)
        {
            Mesh m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            int n = d.verts.Length / 3;
            Vector3[] v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(d.verts[i * 3], d.verts[i * 3 + 1], d.verts[i * 3 + 2]);
            m.vertices = v;
            m.triangles = d.tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return SaveMesh(m);
        }

        private static Mesh Quad(GroundData g)
        {
            float cx = g.center[0], cz = g.center[1], hx = g.size[0] / 2f, hz = g.size[1] / 2f;
            Mesh m = new Mesh { name = "Ground" };
            m.vertices = new[]
            {
                new Vector3(cx - hx, 0, cz - hz), new Vector3(cx - hx, 0, cz + hz),
                new Vector3(cx + hx, 0, cz + hz), new Vector3(cx + hx, 0, cz - hz)
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return SaveMesh(m);
        }

        private static Mesh SaveMesh(Mesh m)
        {
            string path = MeshDir + "/" + m.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(m, existing);
                UnityEngine.Object.DestroyImmediate(m);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material Mat(string name, Color color)
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
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
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
