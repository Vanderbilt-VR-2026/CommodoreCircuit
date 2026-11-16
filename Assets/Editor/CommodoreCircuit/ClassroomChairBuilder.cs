using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// The grey stacking chair from our classroom (photo: Images/Classroom 2.HEIC).
    ///
    /// Menu commands under CommodoreCircuit > Chairs:
    ///   - "Build Classroom Chair" - (re)builds Environment/Prefabs/ClassroomChair.prefab:
    ///       solid grey seat with a rolled front edge, a curved back panel with a grid of
    ///       square holes, and a thin chrome four-leg frame whose back legs run up to hold
    ///       the back. Real size: seat 0.46 m high, back top 0.84 m.
    ///       The holes are a cutout texture on one curved panel, not modelled, so 40+
    ///       chairs stay cheap on Quest. One box collider on the root.
    ///   - "Swap Lobby Chairs to Classroom Chair" - with Lobby Scene open, replaces every
    ///       Kenney chair with the new one in the same spot, facing the same way. Nothing
    ///       else in the room is touched. Undo (Cmd+Z) reverts it. Bake lighting again after.
    /// Build Classroom Lobby uses the new chair too, so a full rebuild keeps it.
    /// The chair faces local -Z (back panel at +Z), like the Kenney chair it replaces.
    /// </summary>
    public static class ClassroomChairBuilder
    {
        public const string PrefabPath = "Assets/CommodoreCircuit/Environment/Prefabs/ClassroomChair.prefab";
        private const string AssetFolder = "Assets/CommodoreCircuit/Environment/Chair";
        private const string KenneyChairPath = "Assets/Third Party/Kenney (models)/kenney_furniture-kit/Models/FBX format/chair.fbx";

        // Seat (metres). Pivot is on the floor under the middle of the chair.
        private const float SeatTop = 0.46f;
        private const float SeatWidth = 0.47f;
        private const float SeatDepth = 0.42f;
        private const float SeatThickness = 0.03f;
        private const float SeatCenterZ = -0.02f;

        // Back panel: curved so its edges wrap forward around the sitter, tilted back a little.
        private const float BackWidth = 0.44f;
        private const float BackBottom = 0.54f;
        private const float BackTop = 0.84f;
        private const float BackBottomZ = 0.19f;
        private const float BackTilt = 0.06f;
        private const float BackWrap = 0.03f;

        // Frame: 18 mm tube.
        private const float TubeRadius = 0.009f;
        private const float FrameHalfWidth = 0.215f;

        // Holes in the back, in millimetres on the panel.
        private const float HolePitchMm = 24f;
        private const float HoleSizeMm = 15f;
        private const float BorderMm = 20f;
        private const float CornerRadiusMm = 30f;
        private const int TextureSize = 512;

        private static readonly Color ShellColor = new Color(0.62f, 0.64f, 0.67f);
        private static readonly Color ChromeColor = new Color(0.78f, 0.80f, 0.82f);

        [MenuItem("CommodoreCircuit/Chairs/Build Classroom Chair")]
        private static void BuildMenu()
        {
            GameObject prefab = Build();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        [MenuItem("CommodoreCircuit/Chairs/Swap Lobby Chairs to Classroom Chair")]
        private static void SwapMenu()
        {
            GameObject prefab = LoadOrBuild();
            Scene scene = SceneManager.GetActiveScene();

            var oldChairs = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == KenneyChairPath)
                        oldChairs.Add(t.gameObject);

            if (oldChairs.Count == 0)
            {
                EditorUtility.DisplayDialog("Swap Lobby Chairs", $"No Kenney chairs found in {scene.name}.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Swap Lobby Chairs");
            int group = Undo.GetCurrentGroup();
            foreach (GameObject old in oldChairs)
            {
                // The Kenney chair pivots at a corner, so take its spot from its bounds.
                Bounds bounds = RendererBounds(old);
                Transform parent = old.transform.parent;
                int sibling = old.transform.GetSiblingIndex();
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(old);

                var chair = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(chair, "Swap Lobby Chairs");
                chair.name = "Chair";
                chair.transform.SetSiblingIndex(sibling);
                chair.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                    Quaternion.Euler(0f, old.transform.eulerAngles.y, 0f));
                chair.SetActive(old.activeSelf);
                ApplyLobbyLighting(chair, flags);

                Undo.DestroyObjectImmediate(old);
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog("Swap Lobby Chairs",
                $"Replaced {oldChairs.Count} chairs in {scene.name}.\n\n" +
                "Save the scene, then bake again (Window > Rendering > Lighting > Generate Lighting) " +
                "so the new chairs cast their shadows.", "OK");
        }

        /// <summary>The chair prefab, built first if it doesn't exist yet.</summary>
        public static GameObject LoadOrBuild()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab ? prefab : Build();
        }

        /// <summary>
        /// Same lighting setup Build Classroom Lobby gives chairs: static for the bake and batching,
        /// but lit by light probes instead of taking lightmap space.
        /// </summary>
        public static void ApplyLobbyLighting(GameObject chair, StaticEditorFlags flags)
        {
            foreach (Transform t in chair.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            foreach (MeshRenderer r in chair.GetComponentsInChildren<MeshRenderer>(true))
                r.receiveGI = ReceiveGI.LightProbes;
        }

        public static GameObject Build()
        {
            EnsureFolder(AssetFolder);
            Material shell = Mat("ChairShell", ShellColor, 0.3f, 0f);
            Material chrome = Mat("ChairChrome", ChromeColor, 0.8f, 0.9f);
            Material perforated = PerforatedMaterial();

            var root = new GameObject("ClassroomChair");
            try
            {
                // Seat, with the front edge rolled down so it doesn't read as a flat plank.
                float seatY = SeatTop - SeatThickness / 2f;
                Box("Seat", root.transform, new Vector3(0f, seatY, SeatCenterZ), new Vector3(SeatWidth, SeatThickness, SeatDepth), shell);
                GameObject lip = Box("Seat Front Edge", root.transform, Vector3.zero, new Vector3(SeatWidth, SeatThickness, 0.06f), shell);
                lip.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                lip.transform.localPosition = new Vector3(0f, seatY - 0.012f, SeatCenterZ - SeatDepth / 2f - 0.018f);

                // Back panel.
                var back = new GameObject("Back");
                back.transform.SetParent(root.transform, false);
                back.AddComponent<MeshFilter>().sharedMesh = BackMesh();
                back.AddComponent<MeshRenderer>().sharedMaterial = perforated;

                // Frame. F = top of a front leg, R = top of a back leg (both just under the seat).
                float underSeat = SeatTop - SeatThickness - TubeRadius;
                Transform frame = new GameObject("Frame").transform;
                frame.SetParent(root.transform, false);
                foreach (float side in new[] { -1f, 1f })
                {
                    var front = new Vector3(side * FrameHalfWidth, underSeat, SeatCenterZ - SeatDepth / 2f + 0.04f);
                    var rear = new Vector3(side * FrameHalfWidth, underSeat, SeatCenterZ + SeatDepth / 2f - 0.02f);
                    // The uprights sit just behind the panel's side edges.
                    Vector3 top = BackPoint(side * 0.9f, 0.93f) + new Vector3(0f, 0f, 0.016f);

                    Tube("Front Leg", frame, new Vector3(side * 0.24f, 0f, -0.25f), front, chrome);
                    Tube("Back Leg", frame, new Vector3(side * 0.245f, 0f, 0.27f), rear, chrome);
                    Tube("Seat Rail", frame, front, rear, chrome);
                    Tube("Back Upright", frame, rear, top, chrome);
                    Joint(frame, front, chrome);
                    Joint(frame, rear, chrome);
                    Joint(frame, top, chrome);
                }
                Tube("Front Rail", frame, new Vector3(-FrameHalfWidth, underSeat, SeatCenterZ - SeatDepth / 2f + 0.04f),
                    new Vector3(FrameHalfWidth, underSeat, SeatCenterZ - SeatDepth / 2f + 0.04f), chrome);

                // One collider for the whole chair so players can't walk through it.
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, BackTop / 2f, 0.01f);
                collider.size = new Vector3(0.5f, BackTop, 0.54f);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"ClassroomChairBuilder: saved {PrefabPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- back panel

        // A point on the back panel. across: -1 (left edge) to 1 (right edge); up: 0 (bottom) to 1 (top).
        private static Vector3 BackPoint(float across, float up)
        {
            return new Vector3(
                across * BackWidth / 2f,
                Mathf.Lerp(BackBottom, BackTop, up),
                BackBottomZ + up * BackTilt - BackWrap * across * across);
        }

        // The curved panel, with a face on each side so it shows from the front and behind.
        private static Mesh BackMesh()
        {
            const int columns = 12, rows = 2;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int face = 0; face < 2; face++)
            {
                int start = vertices.Count;
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= columns; i++)
                    {
                        float u = i / (float)columns, v = j / (float)rows;
                        vertices.Add(BackPoint(u * 2f - 1f, v));
                        uvs.Add(new Vector2(u, v));
                    }

                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < columns; i++)
                    {
                        int a = start + j * (columns + 1) + i, b = a + 1, c = a + columns + 1, d = c + 1;
                        if (face == 0) triangles.AddRange(new[] { a, c, b, b, c, d });
                        else triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
            }

            var mesh = new Mesh { name = "ChairBack" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            // Each face has its own vertices, so each gets its own normals.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string path = $"{AssetFolder}/ChairBack.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Grey plastic with square holes cut out by alpha clipping.
        private static Material PerforatedMaterial()
        {
            Texture2D holes = HoleTexture();
            Material mat = Mat("ChairBackMesh", ShellColor, 0.3f, 0f);
            mat.SetTexture("_BaseMap", holes);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        // White with alpha 0 in the holes and outside the panel's rounded corners.
        private static Texture2D HoleTexture()
        {
            string path = $"{AssetFolder}/ChairBackHoles.png";
            float widthMm = BackWidth * 1000f, heightMm = (BackTop - BackBottom) * 1000f;
            float startX = GridStart(widthMm), startY = GridStart(heightMm);

            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float mx = (x + 0.5f) / TextureSize * widthMm, my = (y + 0.5f) / TextureSize * heightMm;
                    bool hole = InHole(mx, startX, widthMm) && InHole(my, startY, heightMm);
                    bool solid = InsideRoundedRect(mx, my, widthMm, heightMm) && !hole;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, solid ? 1f : 0f));
                }
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            // Keeps the holes from closing up in the distance as the texture shrinks.
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Where the first hole starts, so the grid is centred inside the solid border.
        private static float GridStart(float lengthMm)
        {
            float inner = lengthMm - 2f * BorderMm;
            int count = Mathf.FloorToInt((inner - HoleSizeMm) / HolePitchMm) + 1;
            float used = (count - 1) * HolePitchMm + HoleSizeMm;
            return BorderMm + (inner - used) / 2f;
        }

        private static bool InHole(float mm, float start, float lengthMm)
        {
            if (mm < start || mm > lengthMm - start) return false;
            return (mm - start) % HolePitchMm < HoleSizeMm;
        }

        private static bool InsideRoundedRect(float x, float y, float width, float height)
        {
            float cx = Mathf.Clamp(x, CornerRadiusMm, width - CornerRadiusMm);
            float cy = Mathf.Clamp(y, CornerRadiusMm, height - CornerRadiusMm);
            return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= CornerRadiusMm * CornerRadiusMm;
        }

        // ---------------------------------------------------------------- helpers

        // Created once and reused after that, so colour tweaks made in the editor are kept.
        private static Material Mat(string name, Color color, float smoothness, float metallic)
        {
            string path = $"{AssetFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return box;
        }

        // A round tube from a to b.
        private static void Tube(string name, Transform parent, Vector3 a, Vector3 b, Material mat)
        {
            GameObject tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tube.name = name;
            Object.DestroyImmediate(tube.GetComponent<Collider>());
            tube.transform.SetParent(parent, false);
            tube.transform.localPosition = (a + b) / 2f;
            tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            // The cylinder primitive is 2 units tall and 1 wide.
            tube.transform.localScale = new Vector3(TubeRadius * 2f, (b - a).magnitude / 2f, TubeRadius * 2f);
            tube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Rounds off the corner where two tubes meet.
        private static void Joint(Transform parent, Vector3 at, Material mat)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Joint";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(parent, false);
            ball.transform.localPosition = at;
            ball.transform.localScale = Vector3.one * TubeRadius * 2f;
            ball.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
