using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Scene-view drawing for BarrierPath, plus menu commands:
    ///   CommodoreCircuit > Barriers > New Barrier Path
    ///   CommodoreCircuit > Barriers > Build All Barrier Paths
    ///   CommodoreCircuit > Barriers > Make Scenery Drivable (Add Colliders)
    ///
    /// How to draw: select a Barrier Path, turn on "Draw Mode" in the Inspector,
    /// then click on the ground in the Scene view. Each click adds a point to the
    /// red line. Drag the red dots to move points. Press Build Barriers when done.
    /// </summary>
    [CustomEditor(typeof(BarrierPath))]
    public class BarrierPathEditor : Editor
    {
        private const string BarrierMatPath = "Assets/CampusTrack/Materials/Barrier.mat";
        private const string RootName = "Track Barriers";
        private static bool drawMode;

        public override void OnInspectorGUI()
        {
            BarrierPath path = (BarrierPath)target;

            EditorGUILayout.HelpBox(
                "1. Turn on Draw Mode.\n" +
                "2. Click on the ground in the Scene view to add points (top-down view is easiest).\n" +
                "3. Drag red dots to adjust. Hold Ctrl/Cmd and click a dot to delete it.\n" +
                "4. Press Build Barriers. Re-build any time after editing.",
                MessageType.Info);

            Color old = GUI.backgroundColor;
            GUI.backgroundColor = drawMode ? new Color(1f, 0.45f, 0.45f) : old;
            if (GUILayout.Button(drawMode ? "Draw Mode: ON (click in Scene view)" : "Draw Mode: OFF", GUILayout.Height(30)))
            {
                drawMode = !drawMode;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = old;

            EditorGUILayout.Space();
            DrawDefaultInspector();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"{path.points.Count} points, {path.SegmentCount} segments, {Length(path):0} m");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Remove Last Point") && path.points.Count > 0)
                {
                    Undo.RecordObject(path, "Remove barrier point");
                    path.points.RemoveAt(path.points.Count - 1);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Clear Points") && path.points.Count > 0
                    && EditorUtility.DisplayDialog("Clear points", "Remove every point on this red line?", "Clear", "Cancel"))
                {
                    Undo.RecordObject(path, "Clear barrier points");
                    path.points.Clear();
                    SceneView.RepaintAll();
                }
            }

            GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);
            if (GUILayout.Button("Build Barriers", GUILayout.Height(32)))
                Build(path);
            GUI.backgroundColor = old;

            if (GUILayout.Button("Remove Built Barriers"))
                ClearBuilt(path);
        }

        private void OnDisable()
        {
            // Leaving the object turns drawing off so stray clicks don't add points elsewhere.
            drawMode = false;
        }

        private void OnSceneGUI()
        {
            BarrierPath path = (BarrierPath)target;
            Event e = Event.current;

            // Move / delete existing points.
            for (int i = 0; i < path.points.Count; i++)
            {
                Vector3 world = path.WorldPoint(i);
                float size = HandleUtility.GetHandleSize(world) * 0.09f;
                Handles.color = Color.red;

                if ((e.control || e.command) && e.type == EventType.MouseDown && e.button == 0
                    && HandleUtility.DistanceToCircle(world, size) < 4f)
                {
                    Undo.RecordObject(path, "Delete barrier point");
                    path.points.RemoveAt(i);
                    e.Use();
                    return;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(path, "Move barrier point");
                    if (GroundUnderMouse(path, out Vector3 snapped)) moved = snapped;
                    path.points[i] = path.transform.InverseTransformPoint(moved);
                }

                Handles.Label(world + Vector3.up * size * 3f, i.ToString());
            }

            if (!drawMode) return;

            Handles.BeginGUI();
            GUI.Label(new Rect(10, 10, 420, 22), "BARRIER DRAW MODE: click to add points. Turn off in the Inspector.",
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.red } });
            Handles.EndGUI();

            // Keep clicks from selecting other objects while drawing.
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (GroundUnderMouse(path, out Vector3 hover))
            {
                Handles.color = new Color(1f, 0f, 0f, 0.6f);
                if (path.points.Count > 0)
                    Handles.DrawDottedLine(path.WorldPoint(path.points.Count - 1), hover, 4f);
                Handles.DrawWireDisc(hover, Vector3.up, 0.5f);
                SceneView.RepaintAll();

                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control && !e.command)
                {
                    Undo.RecordObject(path, "Add barrier point");
                    path.points.Add(path.transform.InverseTransformPoint(hover));
                    EditorUtility.SetDirty(path);
                    e.Use();
                }
            }
        }

        // Ground under the mouse: the first upward-facing collider, or the path's own height plane.
        private static bool GroundUnderMouse(BarrierPath path, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            point = Vector3.zero;
            bool found = false;
            foreach (RaycastHit h in hits)
            {
                if (h.normal.y < 0.6f) continue;
                if (h.collider.GetComponentInParent<BarrierPath>() != null) continue;
                if (h.distance < best) { best = h.distance; point = h.point; found = true; }
            }
            if (found) return true;

            Plane plane = new Plane(Vector3.up, path.transform.position);
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        private static float Length(BarrierPath path)
        {
            float total = 0f;
            for (int i = 0; i < path.SegmentCount; i++)
                total += Vector3.Distance(path.WorldPoint(i), path.WorldPoint((i + 1) % path.points.Count));
            return total;
        }

        // ------------------------------------------------------------------ build

        public static void Build(BarrierPath path)
        {
            if (path.SegmentCount == 0)
            {
                EditorUtility.DisplayDialog("Not enough points", "Add at least two points to the red line first.", "OK");
                return;
            }

            ClearBuilt(path);

            if (path.GetComponent<TrackWall>() == null)
                Undo.AddComponent<TrackWall>(path.gameObject);

            GameObject built = new GameObject(BarrierPath.BuiltChildName);
            Undo.RegisterCreatedObjectUndo(built, "Build barriers");
            built.transform.SetParent(path.transform, false);

            Material mat = path.visibleMaterial != null ? path.visibleMaterial : DefaultBarrierMaterial();
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            float thick = Mathf.Max(0.2f, path.thickness);
            float total = Mathf.Max(path.visibleHeight, path.totalHeight);

            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vector3 a = path.WorldPoint(i);
                Vector3 b = path.WorldPoint((i + 1) % path.points.Count);
                Vector3 flat = b - a;
                flat.y = 0f;
                float len = flat.magnitude;
                if (len < 0.05f) continue;

                // Lengthen each piece by the wall thickness so corners overlap with no gap.
                float pieceLen = len + thick;
                Quaternion rot = Quaternion.LookRotation(flat / len, Vector3.up);
                float baseY = Mathf.Min(a.y, b.y);
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, baseY, (a.z + b.z) * 0.5f);

                GameObject seg = new GameObject($"Barrier_{i:000}");
                seg.transform.SetParent(built.transform, false);
                seg.transform.SetPositionAndRotation(mid, rot);

                // One collider covers the visible half AND the invisible upper half.
                // It starts a little below the ground so slopes can't open a gap underneath.
                BoxCollider col = seg.AddComponent<BoxCollider>();
                col.size = new Vector3(thick, total + 0.5f, pieceLen);
                col.center = new Vector3(0f, (total - 0.5f) * 0.5f, 0f);

                if (path.showVisibleBarrier && path.visibleHeight > 0.01f)
                {
                    GameObject vis = new GameObject("Visible");
                    vis.transform.SetParent(seg.transform, false);
                    vis.transform.localPosition = new Vector3(0f, path.visibleHeight * 0.5f, 0f);
                    vis.transform.localScale = new Vector3(Mathf.Min(thick, path.visibleThickness), path.visibleHeight, pieceLen);
                    vis.AddComponent<MeshFilter>().sharedMesh = cube;
                    vis.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    GameObjectUtility.SetStaticEditorFlags(vis,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }
            }

            EditorSceneManager.MarkSceneDirty(path.gameObject.scene);
            Debug.Log($"Built {path.SegmentCount} barrier segments on '{path.name}' ({Length(path):0} m). Save the scene (Cmd+S).");
        }

        public static void ClearBuilt(BarrierPath path)
        {
            Transform old = path.transform.Find(BarrierPath.BuiltChildName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        private static Material DefaultBarrierMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(BarrierMatPath);
            if (mat != null) return mat;

            string dir = Path.GetDirectoryName(BarrierMatPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/CampusTrack")) AssetDatabase.CreateFolder("Assets", "CampusTrack");
                AssetDatabase.CreateFolder("Assets/CampusTrack", "Materials");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            Color concrete = new Color(0.84f, 0.82f, 0.78f);
            mat.color = concrete;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", concrete);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, BarrierMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("CommodoreCircuit/Barriers/New Barrier Path")]
        private static void NewPath()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "New barrier path");
            }

            GameObject go = new GameObject("Barrier Path " + (root.transform.childCount + 1));
            Undo.RegisterCreatedObjectUndo(go, "New barrier path");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<BarrierPath>();
            go.AddComponent<TrackWall>();

            Selection.activeGameObject = go;
            drawMode = true;
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        [MenuItem("CommodoreCircuit/Barriers/Build All Barrier Paths")]
        private static void BuildAll()
        {
            BarrierPath[] paths = Object.FindObjectsByType<BarrierPath>(FindObjectsSortMode.None);
            int built = 0;
            foreach (BarrierPath p in paths)
            {
                if (p.SegmentCount == 0) continue;
                Build(p);
                built++;
            }
            EditorUtility.DisplayDialog("Barriers built", $"Built {built} barrier path(s). Save the scene (Cmd+S).", "OK");
        }

        /// <summary>
        /// The Alumni Lawn scenery is visual only. This adds colliders so the scooter
        /// has ground to ride on and buildings to bounce off. Safe to run again.
        /// </summary>
        [MenuItem("CommodoreCircuit/Barriers/Make Scenery Drivable (Add Colliders)")]
        private static void MakeSceneryDrivable()
        {
            string[] groundGroups = { "Ground", "Roads", "Footpaths", "Lawns" };
            string[] solidGroups = { "Buildings" };
            int added = 0;

            List<GameObject> roots = new List<GameObject>();
            if (Selection.activeGameObject != null) roots.Add(Selection.activeGameObject);
            else roots.AddRange(EditorSceneManager.GetActiveScene().GetRootGameObjects());

            foreach (GameObject root in roots)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    bool ground = System.Array.IndexOf(groundGroups, t.name) >= 0;
                    bool solid = System.Array.IndexOf(solidGroups, t.name) >= 0;
                    if (!ground && !solid) continue;

                    foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                        MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                        mc.sharedMesh = mf.sharedMesh;
                        added++;
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Scenery colliders",
                $"Added {added} colliders (ground, roads, paths, lawns, buildings).\n\n" +
                "Next: put a scooter in this scene (copy the Lemon scooter from Campus Track Scene), " +
                "draw barrier paths, then save (Cmd+S).", "OK");
        }
    }
}
