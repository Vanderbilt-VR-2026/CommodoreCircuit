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
    /// CommodoreCircuit > Build Alumni Lawn Track
    ///
    /// Builds the race track in the OPEN scene (meant for "Alumni Lawn Scenery") from
    /// Assets/CampusTrack/alumni_track_route.json, the route Tifa drew on the map:
    ///   - a 12 m road you drive on (with collider),
    ///   - a wider run-off strip out to the barriers (with collider),
    ///   - barriers on both sides, 17 m apart: visible low barrier + invisible wall on top,
    ///   - a start/finish line, and moves the scooter onto it if the scene has one.
    ///
    /// The barriers are normal Barrier Paths ("Barrier Left" / "Barrier Right"), so you can
    /// drag their red points and press Build Barriers to fine-tune. Running this again
    /// replaces the "Alumni Track" object (hand edits to it are lost).
    /// </summary>
    public static class AlumniTrackBuilder
    {
        private const string DataPath = "Assets/CampusTrack/alumni_track_route.json";
        private const string MeshDir = "Assets/CampusTrack/AlumniMeshes";
        private const string MatDir = "Assets/CampusTrack/Materials";
        private const string RootName = "Alumni Track";

        // Heights sit just above the scenery roads (0.05) so nothing flickers.
        private const float ShoulderY = 0.08f;
        private const float RoadY = 0.11f;
        private const float LineY = 0.13f;

        [Serializable] private class Meta { public float lapLength; public float roadWidth; public float barrierSpacing; public float tunnelHalfWidth; public float tunnelHeight; }
        [Serializable]
        private class RouteData
        {
            public Meta meta;
            public float[] centerline;
            // Per-point distances from the centerline, in meters. Narrower where the
            // route squeezes between buildings, so future building colliders never
            // poke into the track.
            public float[] barrierLeft, barrierRight, roadLeft, roadRight;
        }

        [MenuItem("CommodoreCircuit/Build Alumni Lawn Track")]
        private static void Build()
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (json == null)
            {
                EditorUtility.DisplayDialog("Route missing", "Could not find " + DataPath, "OK");
                return;
            }
            RouteData data = JsonUtility.FromJson<RouteData>(json.text);
            int n = data.centerline.Length / 2;
            Vector3[] center = new Vector3[n];
            for (int i = 0; i < n; i++) center[i] = new Vector3(data.centerline[i * 2], 0f, data.centerline[i * 2 + 1]);

            float roadHalf = data.meta.roadWidth * 0.5f;
            float barrierHalf = data.meta.barrierSpacing * 0.5f;
            Vector3[] left = new Vector3[n];   // unit vectors pointing to the left of travel
            for (int i = 0; i < n; i++)
            {
                Vector3 t = center[(i + 1) % n] - center[(i - 1 + n) % n];
                t.y = 0f;
                t.Normalize();
                left[i] = new Vector3(-t.z, 0f, t.x);
            }

            float[] bL = Widths(data.barrierLeft, n, barrierHalf);
            float[] bR = Widths(data.barrierRight, n, barrierHalf);
            float[] rL = Widths(data.roadLeft, n, roadHalf);
            float[] rR = Widths(data.roadRight, n, roadHalf);
            float[] runL = new float[n], runR = new float[n];
            for (int i = 0; i < n; i++) { runL[i] = bL[i] + 0.6f; runR[i] = bR[i] + 0.6f; }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder("Assets/CampusTrack");
            EnsureFolder(MeshDir);
            EnsureFolder(MatDir);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Alumni Track");

            Material asphalt = Mat("Asphalt", new Color(0.22f, 0.23f, 0.25f));
            Material runoff = Mat("RunOff", new Color(0.62f, 0.60f, 0.56f));
            Material startMat = Mat("FinishLineWhite", new Color(0.95f, 0.95f, 0.92f));

            Part(root, "Run-off", Ribbon("AlumniRunOff", center, left, runL, runR, ShoulderY, true), runoff);
            Part(root, "Road", Ribbon("AlumniRoad", center, left, rL, rR, RoadY, true), asphalt);

            // Start / finish line across the road at point 0.
            Vector3 fwd = (center[1] - center[0]).normalized;
            Vector3[] lineC = { center[0] - fwd * 0.6f, center[0] + fwd * 0.6f };
            Vector3[] lineL = { left[0], left[0] };
            Part(root, "StartLine", Ribbon("AlumniStartLine", lineC, lineL, new[] { rL[0], rL[0] }, new[] { rR[0], rR[0] }, LineY, false), startMat, false);

            // Barriers: two editable Barrier Paths.
            GameObject barriers = new GameObject("Barriers");
            barriers.transform.SetParent(root.transform, false);
            MakeBarrier(barriers, "Barrier Left", center, left, bL, 1f);
            MakeBarrier(barriers, "Barrier Right", center, left, bR, -1f);

            PlaceScooter(center[0], fwd);

            // Carmichael has a drive-through overhang in real life: cut one where the track passes.
            string tunnelNote = BuildingTunnel.Cut("Oliver C. Carmichael College", center, left,
                data.meta.tunnelHalfWidth > 0f ? data.meta.tunnelHalfWidth : barrierHalf + 0.6f,
                data.meta.tunnelHeight > 0f ? data.meta.tunnelHeight : 6.1f, MeshDir) ? "\nCarmichael overhang cut." : "";
            if (tunnelNote.Length > 0) Debug.Log("Cut the drive-through overhang in Oliver C. Carmichael College.");

            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            string msg = $"Lap about {data.meta.lapLength:0} m, counter-clockwise. Road {data.meta.roadWidth:0} m wide and barriers {data.meta.barrierSpacing:0} m apart, narrower only where it squeezes between buildings.{tunnelNote}\n\nSave the scene (Cmd+S).";
            Debug.Log("Alumni Lawn track built. " + msg);
            EditorUtility.DisplayDialog("Alumni Lawn track built", msg, "OK");
        }

        private static float[] Widths(float[] fromFile, int n, float fallback)
        {
            float[] w = new float[n];
            for (int i = 0; i < n; i++) w[i] = fromFile != null && fromFile.Length == n ? fromFile[i] : fallback;
            return w;
        }

        private static void MakeBarrier(GameObject parent, string name, Vector3[] center, Vector3[] left, float[] offsets, float sign)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            BarrierPath path = go.AddComponent<BarrierPath>();
            go.AddComponent<TrackWall>();
            path.closedLoop = true;
            path.points.Clear();

            // Keep a point only where the line bends (or every 15 m), so straight
            // stretches become one long piece instead of dozens of short ones.
            int n = center.Length;
            Vector3 lastKept = center[0] + left[0] * offsets[0] * sign;
            path.points.Add(lastKept);
            for (int i = 1; i < n; i++)
            {
                Vector3 p = center[i] + left[i] * offsets[i] * sign;
                Vector3 next = center[(i + 1) % n] + left[(i + 1) % n] * offsets[(i + 1) % n] * sign;
                Vector3 dirIn = (p - lastKept).normalized;
                Vector3 dirOut = (next - p).normalized;
                if (Vector3.Angle(dirIn, dirOut) > 4f || Vector3.Distance(p, lastKept) > 15f)
                {
                    path.points.Add(p);
                    lastKept = p;
                }
            }
            BarrierPathEditor.Build(path);
        }

        private static void PlaceScooter(Vector3 pos, Vector3 forward)
        {
            ScooterController scooter = UnityEngine.Object.FindAnyObjectByType<ScooterController>();
            if (scooter == null)
            {
                Debug.Log("No scooter in this scene yet. Copy the Lemon scooter in from Campus Track Scene, then run Build Alumni Lawn Track again (or place it on the start line).");
                return;
            }
            pos.y = RoadY;
            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
            Undo.RecordObject(scooter.transform, "Place scooter");
            scooter.transform.SetPositionAndRotation(pos, rot);
            EditorUtility.SetDirty(scooter);
        }

        // A flat strip of the given half-width along the centerline.
        private static Mesh Ribbon(string name, Vector3[] center, Vector3[] left, float[] halfLeft, float[] halfRight, float y, bool closed)
        {
            int n = center.Length;
            Vector3[] v = new Vector3[n * 2];
            Vector2[] uv = new Vector2[n * 2];
            float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) dist += Vector3.Distance(center[i], center[i - 1]);
                Vector3 c = new Vector3(center[i].x, y, center[i].z);
                v[i * 2] = c + left[i] * halfLeft[i];
                v[i * 2 + 1] = c - left[i] * halfRight[i];
                uv[i * 2] = new Vector2(0f, dist / 4f);
                uv[i * 2 + 1] = new Vector2(1f, dist / 4f);
            }

            int segs = closed ? n : n - 1;
            int[] tris = new int[segs * 6];
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2, b = ((i + 1) % n) * 2;
                // Winding chosen so the faces point up for either lap direction.
                int k = i * 6;
                tris[k] = a; tris[k + 1] = b; tris[k + 2] = a + 1;
                tris[k + 3] = a + 1; tris[k + 4] = b; tris[k + 5] = b + 1;
            }

            Mesh m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.vertices = v;
            m.uv = uv;
            m.triangles = tris;
            m.RecalculateNormals();
            if (m.normals.Length > 0 && m.normals[0].y < 0f)
            {
                Array.Reverse(tris);
                m.triangles = tris;
                m.RecalculateNormals();
            }
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, MeshDir + "/" + name + ".asset");
            return m;
        }

        private static GameObject Part(GameObject parent, string name, Mesh mesh, Material mat, bool collider = true)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static Material Mat(string name, Color color)
        {
            string path = MatDir + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;   // reuse existing materials untouched
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(mat, path);
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
