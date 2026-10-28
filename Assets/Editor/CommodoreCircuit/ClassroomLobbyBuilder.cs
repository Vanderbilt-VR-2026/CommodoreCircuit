using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Rebuilds the Lobby as our real classroom (photos + plan in the "Classroom Lobby Sketches" page).
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Build Classroom Lobby" - with Lobby Scene open, builds a "Classroom" object:
    ///       8 m wide (x) x 12 m deep (z) x 3 m tall. Front wall at z = +4, so the
    ///       chalkboard (Furniture/Whiteboard) and the lobby Canvas on it stay where they are.
    ///       Back wall at z = -8 is the blue accent wall.
    ///     Front:  door + glass panel (left), projection screen housing above the board, lectern, TV
    ///     Right:  whiteboard, two windows with half-lowered roller shades
    ///     Back:   door, whiteboard, stack of spare chairs
    ///     Left:   whiteboard + hanging TV near the front, long whiteboard near the back
    ///     Floor:  tan, curved blue-grey zone on the left, charcoal stripe near the back
    ///     Room:   5 rows of tables on each side of a centre aisle, 2 chairs per table,
    ///             ceiling tile grid, two hanging light bars
    ///   It also removes the old StudentDesks, TeacherDesk and loose chairs, hides the old
    ///   Room, Plane, Invisible Walls and FloorCollider (the new shell has its own colliders),
    ///   and puts the two apples on the new furniture.
    ///   Running it again replaces "Classroom". Undo (Cmd+Z) reverts the whole build.
    ///   Materials are created once in Assets/Materials/Lobby/Classroom and reused after that,
    ///   so colour tweaks made in the editor are kept.
    /// </summary>
    public static class ClassroomLobbyBuilder
    {
        private const string LobbyPath = "Assets/Scenes/Lobby Scene.unity";
        private const string RootName = "Classroom";
        private const string MaterialFolder = "Assets/Materials/Lobby/Classroom";
        private const string ChairModelPath = "Assets/Third Party/Kenney (models)/kenney_furniture-kit/Models/FBX format/chair.fbx";

        // Room interior. +Z is the front (chalkboard), matching LobbyBuilder and ChalkboardLobbySetup.
        private const float HalfWidth = 4f;
        private const float FrontZ = 4f;
        private const float BackZ = -8f;
        private const float Height = 3f;
        private const float WallThickness = 0.2f;
        private static float CenterZ => (FrontZ + BackZ) / 2f;
        private static float Depth => FrontZ - BackZ;

        // Tables: two per row on each side, leaving a 2.7 m aisle for the spawn line at x = 0.
        private const float TableWidth = 1.1f;
        private const float TableDepth = 0.5f;
        private const float TableHeight = 0.74f;
        private static readonly float[] TableColumnsX = { -3.0f, -1.9f, 1.9f, 3.0f };
        private static readonly float[] TableRowsZ = { 1.2f, -0.3f, -1.8f, -3.3f, -4.8f };
        private const float ChairHeight = 0.85f;

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [MenuItem("CommodoreCircuit/Build Classroom Lobby")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode", "Stop Play Mode and run this command again.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != LobbyPath)
            {
                EditorUtility.DisplayDialog("Open the Lobby first", $"Open {LobbyPath} and run this command again.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build Classroom Lobby");
            int undoGroup = Undo.GetCurrentGroup();
            Materials.Clear();
            EnsureFolder(MaterialFolder);

            GameObject old = FindRoot(scene, RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            string cleanup = RemoveOldClassroom(scene);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Classroom Lobby");

            BuildShell(root.transform);
            BuildFloorZones(root.transform);
            BuildFrontWall(Group(root.transform, "Front Wall", new Vector3(0f, 0f, FrontZ), 0f), root.transform);
            BuildRightWall(Group(root.transform, "Right Wall", new Vector3(HalfWidth, 0f, CenterZ), 90f));
            BuildBackWall(Group(root.transform, "Back Wall", new Vector3(0f, 0f, BackZ), 180f), root.transform);
            BuildLeftWall(Group(root.transform, "Left Wall", new Vector3(-HalfWidth, 0f, CenterZ), -90f));
            BuildCeiling(root.transform);
            Vector3 lecternTop = BuildLectern(root.transform);
            Vector3 frontTableTop = BuildTables(root.transform);
            string apples = MoveApples(scene, frontTableTop, lecternTop);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Lobby] Classroom built. {cleanup} {apples}");
            EditorUtility.DisplayDialog("Classroom built",
                "The classroom is under \"Classroom\" in the Hierarchy.\n\n" + cleanup + "\n" + apples +
                "\n\nSave the scene (Cmd+S) to keep it, or Undo (Cmd+Z) to go back.", "OK");
        }

        // ---------------------------------------------------------------- old lobby

        private static string RemoveOldClassroom(Scene scene)
        {
            int removed = 0;
            GameObject furniture = FindRoot(scene, "Furniture");
            if (furniture != null)
            {
                foreach (string name in new[] { "StudentDesks", "TeacherDesk" })
                {
                    Transform child = furniture.transform.Find(name);
                    if (child == null) continue;
                    Undo.DestroyObjectImmediate(child.gameObject);
                    removed++;
                }
            }

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name != "chair" && !go.name.StartsWith("chair (")) continue;
                Undo.DestroyObjectImmediate(go);
                removed++;
            }

            // Hidden, not deleted: other setup scripts still look these up by name.
            int hidden = 0;
            foreach (string name in new[] { "Room", "Plane", "Invisible Walls", "FloorCollider" })
            {
                GameObject go = FindRoot(scene, name);
                if (go == null || !go.activeSelf) continue;
                Undo.RecordObject(go, "Build Classroom Lobby");
                go.SetActive(false);
                hidden++;
            }

            return $"Removed {removed} old desks/chairs and hid {hidden} old room objects.";
        }

        private static string MoveApples(Scene scene, Vector3 tableTop, Vector3 lecternTop)
        {
            int moved = 0;
            moved += MoveTo(FindRoot(scene, "apple"), tableTop + new Vector3(0.2f, 0.01f, 0.05f)) ? 1 : 0;
            moved += MoveTo(FindRoot(scene, "apple (2)"), lecternTop + new Vector3(0.1f, 0.01f, 0f)) ? 1 : 0;
            return moved > 0 ? $"Moved {moved} apple(s) onto the new furniture." : "";
        }

        private static bool MoveTo(GameObject go, Vector3 position)
        {
            if (go == null) return false;
            Undo.RecordObject(go.transform, "Build Classroom Lobby");
            go.transform.position = position;
            return true;
        }

        // ---------------------------------------------------------------- shell

        private static void BuildShell(Transform root)
        {
            Transform shell = Child(root, "Shell");
            float w = HalfWidth * 2f + WallThickness * 2f;

            // Floor slab: top face at y = 0. Its collider replaces the old FloorCollider.
            Box("Floor", shell, new Vector3(0f, -0.1f, CenterZ), new Vector3(w, 0.2f, Depth + WallThickness * 2f), Mat("FloorTan", "CDB48C"), true);

            Material cream = Mat("WallCream", "E5DDCB");
            Box("Front", shell, new Vector3(0f, Height / 2f, FrontZ + WallThickness / 2f), new Vector3(w, Height, WallThickness), cream, true);
            Box("Back", shell, new Vector3(0f, Height / 2f, BackZ - WallThickness / 2f), new Vector3(w, Height, WallThickness), Mat("AccentWallBlue", "4F6A85"), true);
            Box("Left", shell, new Vector3(-HalfWidth - WallThickness / 2f, Height / 2f, CenterZ), new Vector3(WallThickness, Height, Depth), cream, true);
            Box("Right", shell, new Vector3(HalfWidth + WallThickness / 2f, Height / 2f, CenterZ), new Vector3(WallThickness, Height, Depth), cream, true);
        }

        private static void BuildFloorZones(Transform root)
        {
            Transform floor = Child(root, "Floor Inlays");

            // Curved blue-grey zone on the left, traced from the plan (front-left corner to the back).
            var blue = new List<Vector2> { new Vector2(-HalfWidth, FrontZ), new Vector2(-HalfWidth, BackZ), new Vector2(-1.45f, BackZ) };
            Vector2 p0 = new Vector2(-1.45f, BackZ), p1 = new Vector2(-0.36f, -5.5f), p2 = new Vector2(-0.73f, -0.2f), p3 = new Vector2(0f, FrontZ);
            for (int i = 1; i <= 16; i++)
            {
                float t = i / 16f, u = 1f - t;
                blue.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }
            FloorPatch("Blue Zone", floor, blue, new Vector2(-2.4f, -2f), 0.003f, Mat("FloorBlueGrey", "98A9B8"));

            // Charcoal stripe across the back, in front of the blue wall.
            var stripe = new List<Vector2> { new Vector2(-1.45f, -7.5f), new Vector2(HalfWidth, -7.3f), new Vector2(HalfWidth, -7.52f), new Vector2(-1.45f, -7.72f) };
            FloorPatch("Charcoal Stripe", floor, stripe, new Vector2(1.3f, -7.5f), 0.005f, Mat("FloorCharcoal", "5D595B"));
        }

        // ---------------------------------------------------------------- walls
        // Each wall group sits on the wall's inner face. Local +X is to your right as you face
        // the wall from inside the room, local -Z points into the room, y = 0 is the floor.

        private static void BuildFrontWall(Transform wall, Transform root)
        {
            Baseboard(wall, HalfWidth * 2f);
            Door(wall, -2.75f, true);
            // Glass panel beside the door.
            Box("Glass Panel", wall, new Vector3(-3.58f, 1.05f, -0.02f), new Vector3(0.55f, 2.1f, 0.03f), Mat("Glass", "CFE3EC", 0.9f), false);
            Box("Glass Frame", wall, new Vector3(-3.58f, 2.13f, -0.03f), new Vector3(0.61f, 0.06f, 0.06f), Mat("DoorFrame", "3F4A55"), false);

            // Rolled-up projection screen above the chalkboard (the chalkboard and lobby UI are left alone).
            Box("Projection Screen Housing", wall, new Vector3(0f, 2.72f, -0.08f), new Vector3(3.4f, 0.12f, 0.14f), Mat("Housing", "D9D9D6"), false);

            // Ceiling projector in front of the board.
            Transform projector = Child(root, "Projector");
            Box("Body", projector, new Vector3(0f, 2.62f, 1.4f), new Vector3(0.4f, 0.12f, 0.34f), Mat("Housing", "D9D9D6"), false);
            Pole(projector, new Vector3(0f, 2.68f, 1.4f));

            // TV on a ceiling pole, front-right corner, facing the class.
            Tv(root, "TV Front", new Vector3(3.15f, 2.3f, FrontZ - 0.6f), new Vector3(1.2f, 0.7f, 0.06f));
        }

        private static void BuildRightWall(Transform wall)
        {
            Baseboard(wall, Depth);
            Whiteboard(wall, -4.7f, 1.4f, 1.2f, 1.0f);
            Window(wall, -1.6f, 3.2f);
            Window(wall, 2.5f, 3.2f);
        }

        private static void BuildBackWall(Transform wall, Transform root)
        {
            Baseboard(wall, HalfWidth * 2f);
            Door(wall, 3.3f, true);
            Whiteboard(wall, 1.0f, 1.45f, 2.6f, 1.2f);

            // Spare chairs stacked in the back-right corner.
            Transform stack = Child(root, "Chair Stack");
            for (int i = 0; i < 4; i++)
                Chair(stack, new Vector3(3.45f, 0.07f * i, BackZ + 0.45f + 0.03f * i), 180f);
        }

        private static void BuildLeftWall(Transform wall)
        {
            Baseboard(wall, Depth);
            Whiteboard(wall, 4.4f, 1.45f, 1.6f, 1.2f);
            Whiteboard(wall, -3.3f, 1.45f, 4.0f, 1.2f);
            Tv(wall, "TV Left", new Vector3(4.4f, 2.5f, -0.25f), new Vector3(1.2f, 0.7f, 0.06f));
        }

        private static void BuildCeiling(Transform root)
        {
            Transform ceiling = Child(root, "Ceiling");

            // A quad facing down, so the tile texture maps 0.6 m tiles straight onto x and z.
            GameObject tiles = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tiles.name = "Tiles";
            Object.DestroyImmediate(tiles.GetComponent<Collider>());
            tiles.transform.SetParent(ceiling, false);
            tiles.transform.localPosition = new Vector3(0f, Height, CenterZ);
            tiles.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            tiles.transform.localScale = new Vector3(HalfWidth * 2f, Depth, 1f);
            tiles.GetComponent<MeshRenderer>().sharedMaterial = CeilingMaterial();

            // Two long hanging light bars across the room.
            Material light = Mat("LightBar", "F6F4EE", 0.5f, 1.4f);
            foreach (float z in new[] { 0.8f, -4.4f })
            {
                GameObject bar = Cylinder("Light Bar", ceiling, new Vector3(0f, 2.55f, z), new Vector3(0.1f, 3.5f, 0.1f), light);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Pole(ceiling, new Vector3(-2.5f, 2.6f, z));
                Pole(ceiling, new Vector3(2.5f, 2.6f, z));
            }
        }

        // ---------------------------------------------------------------- furniture

        private static Vector3 BuildLectern(Transform root)
        {
            Transform lectern = Child(root, "Lectern");
            lectern.position = new Vector3(2.8f, 0f, FrontZ - 0.8f);
            Material oak = Mat("Oak", "B57A45", 0.35f);
            Box("Body", lectern, new Vector3(0f, 0.52f, 0f), new Vector3(0.6f, 1.04f, 0.45f), oak, false);
            // Flat top so the apple placed on it stays put.
            Box("Top", lectern, new Vector3(0f, 1.06f, 0f), new Vector3(0.66f, 0.04f, 0.5f), oak, false);
            AddBoxCollider(lectern.gameObject, new Vector3(0f, 0.55f, 0f), new Vector3(0.66f, 1.1f, 0.5f));
            return lectern.position + new Vector3(0f, 1.1f, 0f);
        }

        private static Vector3 BuildTables(Transform root)
        {
            Transform tables = Child(root, "Tables");
            Material top = Mat("Tabletop", "A9ACB1", 0.3f);
            Material frame = Mat("TableFrame", "3E4247", 0.4f);
            int index = 0;
            foreach (float z in TableRowsZ)
            {
                foreach (float x in TableColumnsX)
                {
                    Transform table = Table("Table_" + index++, tables, new Vector3(x, 0f, z), top, frame);
                    // Two chairs behind each table, facing the front.
                    Chair(table, new Vector3(x - 0.27f, 0f, z - 0.45f), 180f);
                    Chair(table, new Vector3(x + 0.27f, 0f, z - 0.45f), 180f);
                }
            }
            return new Vector3(TableColumnsX[2], TableHeight, TableRowsZ[0]);
        }

        private static Transform Table(string name, Transform parent, Vector3 floorPosition, Material topMat, Material frameMat)
        {
            Transform table = Child(parent, name);
            table.position = floorPosition;
            Box("Top", table, new Vector3(0f, TableHeight - 0.015f, 0f), new Vector3(TableWidth, 0.03f, TableDepth), topMat, false);
            foreach (float x in new[] { -0.45f, 0.45f })
            {
                Box("Post", table, new Vector3(x, (TableHeight - 0.03f) / 2f, 0f), new Vector3(0.06f, TableHeight - 0.03f, 0.06f), frameMat, false);
                Box("Foot", table, new Vector3(x, 0.015f, 0f), new Vector3(0.06f, 0.03f, TableDepth - 0.05f), frameMat, false);
            }
            // One collider for the whole table so players can't walk through it.
            AddBoxCollider(table.gameObject, new Vector3(0f, TableHeight / 2f, 0f), new Vector3(TableWidth, TableHeight, TableDepth));
            return table;
        }

        private static void Chair(Transform parent, Vector3 floorCenter, float yaw)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ChairModelPath);
            if (model == null)
            {
                Debug.LogWarning($"ClassroomLobbyBuilder: {ChairModelPath} not found, skipping chairs.");
                return;
            }

            var chair = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            chair.name = "Chair";
            chair.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            chair.transform.localScale = Vector3.one;

            // Kenney models pivot at a corner, so size and place the chair from its bounds.
            Bounds bounds = RendererBounds(chair);
            if (bounds.size.y > 0f)
                chair.transform.localScale = Vector3.one * (ChairHeight / bounds.size.y);
            bounds = RendererBounds(chair);
            chair.transform.position += new Vector3(floorCenter.x - bounds.center.x, floorCenter.y - bounds.min.y, floorCenter.z - bounds.center.z);

            // Grey like the real mesh chairs.
            Material grey = Mat("ChairGrey", "BCC1C6", 0.3f);
            foreach (Renderer r in chair.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = grey;
                r.sharedMaterials = mats;
            }
        }

        // ---------------------------------------------------------------- wall fixtures

        private static void Whiteboard(Transform wall, float x, float centerY, float width, float height)
        {
            Transform board = Child(wall, "Whiteboard");
            board.localPosition = new Vector3(x, centerY, 0f);
            Material oak = Mat("Oak", "B57A45", 0.35f);
            Box("Frame", board, new Vector3(0f, 0f, -0.015f), new Vector3(width + 0.1f, height + 0.1f, 0.03f), oak, false);
            Box("Surface", board, new Vector3(0f, 0f, -0.032f), new Vector3(width, height, 0.01f), Mat("Whiteboard", "F4F5F3", 0.8f), false);
            Box("Tray", board, new Vector3(0f, -height / 2f - 0.07f, -0.05f), new Vector3(width, 0.03f, 0.08f), oak, false);
        }

        private static void Window(Transform wall, float x, float width)
        {
            Transform window = Child(wall, "Window");
            window.localPosition = new Vector3(x, 0f, 0f);
            Material frame = Mat("WindowFrame", "E9E9E4", 0.4f);

            // Bright panel instead of a modelled outside: reads as daylight through the glass.
            Box("Daylight", window, new Vector3(0f, 1.75f, -0.005f), new Vector3(width, 1.7f, 0.01f), Mat("Daylight", "EAF4FF", 0.9f, 1.2f), false);
            Box("Frame Top", window, new Vector3(0f, 2.6f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.05f), frame, false);
            Box("Frame Bottom", window, new Vector3(0f, 0.9f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.05f), frame, false);
            Box("Frame Left", window, new Vector3(-width / 2f, 1.75f, -0.03f), new Vector3(0.06f, 1.76f, 0.05f), frame, false);
            Box("Frame Right", window, new Vector3(width / 2f, 1.75f, -0.03f), new Vector3(0.06f, 1.76f, 0.05f), frame, false);
            Box("Mullion", window, new Vector3(0f, 1.75f, -0.03f), new Vector3(0.05f, 1.7f, 0.04f), frame, false);
            Box("Sill", window, new Vector3(0f, 0.86f, -0.1f), new Vector3(width + 0.15f, 0.04f, 0.2f), Mat("Oak", "B57A45", 0.35f), false);

            // Roller shade, about half down.
            Box("Shade Housing", window, new Vector3(0f, 2.68f, -0.08f), new Vector3(width + 0.1f, 0.12f, 0.12f), frame, false);
            Box("Shade", window, new Vector3(0f, 2.2f, -0.07f), new Vector3(width, 0.84f, 0.01f), Mat("RollerShade", "A8A7A2", 0.1f), false);
            Box("Shade Bar", window, new Vector3(0f, 1.78f, -0.07f), new Vector3(width, 0.03f, 0.03f), Mat("TableFrame", "3E4247", 0.4f), false);
        }

        private static void Door(Transform wall, float x, bool exitSign)
        {
            Transform door = Child(wall, "Door");
            door.localPosition = new Vector3(x, 0f, 0f);
            Material frame = Mat("DoorFrame", "3F4A55");
            Box("Door", door, new Vector3(0f, 1.05f, -0.025f), new Vector3(0.9f, 2.1f, 0.05f), Mat("Oak", "B57A45", 0.35f), false);
            Box("Window", door, new Vector3(0.22f, 1.4f, -0.052f), new Vector3(0.14f, 0.9f, 0.005f), Mat("Glass", "CFE3EC", 0.9f), false);
            Box("Handle", door, new Vector3(-0.35f, 1.0f, -0.07f), new Vector3(0.14f, 0.03f, 0.04f), Mat("Housing", "D9D9D6"), false);
            Box("Jamb Left", door, new Vector3(-0.48f, 1.08f, -0.03f), new Vector3(0.06f, 2.16f, 0.07f), frame, false);
            Box("Jamb Right", door, new Vector3(0.48f, 1.08f, -0.03f), new Vector3(0.06f, 2.16f, 0.07f), frame, false);
            Box("Header", door, new Vector3(0f, 2.13f, -0.03f), new Vector3(1.02f, 0.06f, 0.07f), frame, false);
            if (exitSign)
                Box("Exit Sign", door, new Vector3(0f, 2.4f, -0.05f), new Vector3(0.32f, 0.15f, 0.08f), Mat("ExitSign", "D7261E", 0.5f, 1.5f), false);
        }

        private static void Tv(Transform parent, string name, Vector3 localCenter, Vector3 size)
        {
            Transform tv = Child(parent, name);
            tv.localPosition = localCenter;
            Box("Bezel", tv, Vector3.zero, size, Mat("TvBezel", "1B1C1E", 0.5f), false);
            Box("Screen", tv, new Vector3(0f, 0f, -size.z / 2f - 0.001f), new Vector3(size.x - 0.04f, size.y - 0.04f, 0.002f), Mat("TvScreen", "0B0C0E", 0.9f), false);
            Pole(tv, new Vector3(0f, size.y / 2f, 0f), Height - (tv.position.y + size.y / 2f));
        }

        private static void Baseboard(Transform wall, float length)
        {
            Box("Baseboard", wall, new Vector3(0f, 0.05f, -0.008f), new Vector3(length, 0.1f, 0.015f), Mat("Baseboard", "6B6E6A", 0.3f), false);
        }

        // ---------------------------------------------------------------- materials

        private static Material Mat(string name, string hex, float smoothness = 0.2f, float emission = 0f)
        {
            if (Materials.TryGetValue(name, out Material cached)) return cached;
            string path = $"{MaterialFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                ColorUtility.TryParseHtmlString("#" + hex, out Color color);
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * emission);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                AssetDatabase.CreateAsset(mat, path);
            }
            Materials[name] = mat;
            return mat;
        }

        private static Material CeilingMaterial()
        {
            const string name = "CeilingTile";
            string texturePath = $"{MaterialFolder}/{name}.png";
            if (!File.Exists(texturePath))
            {
                // One 0.6 m tile with a thin grey grid line on two edges; tiling draws the rest.
                const int size = 128;
                var texture = new Texture2D(size, size);
                Color tile = new Color(0.95f, 0.95f, 0.93f), line = new Color(0.74f, 0.74f, 0.72f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        texture.SetPixel(x, y, x < 3 || y < 3 ? line : tile);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            bool isNew = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat") == null;
            Material mat = Mat(name, "FFFFFF", 0.1f);
            if (isNew)
            {
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                mat.SetTextureScale("_BaseMap", new Vector2(HalfWidth * 2f / 0.6f, Depth / 0.6f));
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        // ---------------------------------------------------------------- helpers

        private static Transform Group(Transform parent, string name, Vector3 position, float yaw)
        {
            Transform group = Child(parent, name);
            group.localPosition = position;
            group.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return group;
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material mat, bool keepCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            if (!keepCollider)
                Object.DestroyImmediate(box.GetComponent<BoxCollider>());
            if (mat != null)
                box.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return box;
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material mat)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return cylinder;
        }

        // Thin rod from a point up to the ceiling. Length defaults to "the rest of the way up".
        private static void Pole(Transform parent, Vector3 localBottom, float length = -1f)
        {
            float worldBottom = parent.TransformPoint(localBottom).y;
            if (length < 0f) length = Height - worldBottom;
            if (length <= 0.01f) return;
            GameObject pole = Cylinder("Pole", parent, Vector3.zero, new Vector3(0.025f, length / 2f, 0.025f), Mat("TableFrame", "3E4247", 0.4f));
            pole.transform.position = parent.TransformPoint(localBottom) + Vector3.up * (length / 2f);
            pole.transform.rotation = Quaternion.identity;
        }

        // Flat floor overlay, fanned out from a point that can see every edge. No collider.
        private static void FloorPatch(string name, Transform parent, List<Vector2> outline, Vector2 fanCenter, float y, Material mat)
        {
            var vertices = new List<Vector3> { new Vector3(fanCenter.x, y, fanCenter.y) };
            foreach (Vector2 p in outline) vertices.Add(new Vector3(p.x, y, p.y));

            var triangles = new List<int>();
            for (int i = 0; i < outline.Count; i++)
            {
                int a = 0, b = 1 + i, c = 1 + (i + 1) % outline.Count;
                // Keep every triangle facing up, whichever way the outline winds.
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (n.y < 0f) (b, c) = (c, b);
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string meshPath = $"{MaterialFolder}/{name.Replace(" ", "")}.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void AddBoxCollider(GameObject target, Vector3 center, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
