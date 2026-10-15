using UnityEditor;
using UnityEngine;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Builds the classroom furniture for the Lobby scene.
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Build Lobby Furniture" - adds a whiteboard on the front wall (+Z),
    ///     a teacher desk in front of it, and a 2x3 grid of student desks.
    ///     Everything is placed from the bounds of the "Room" object, so it
    ///     still lines up if the room is moved or resized. Running it again
    ///     replaces the old "Furniture" object instead of stacking a copy.
    ///     Undo (Cmd+Z) removes it.
    /// </summary>
    public static class LobbyBuilder
    {
        private const string MaterialFolder = "Assets/Materials/Lobby/";
        private const string FurnitureName = "Furniture";

        [MenuItem("CommodoreCircuit/Build Lobby Furniture")]
        public static void BuildFurniture()
        {
            GameObject room = GameObject.Find("Room");
            if (room == null || room.GetComponent<Renderer>() == null)
            {
                EditorUtility.DisplayDialog(
                    "Room not found",
                    "Open the Lobby scene and make sure the classroom object is named \"Room\", " +
                    "then run this command again.",
                    "OK");
                return;
            }

            Bounds bounds = room.GetComponent<Renderer>().bounds;
            float floorY = bounds.min.y;
            float frontZ = bounds.max.z;
            float centerX = bounds.center.x;

            Material boardMat = LoadMaterial("Board");
            Material deskMat = LoadMaterial("Desk");

            GameObject old = GameObject.Find(FurnitureName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            GameObject furniture = new GameObject(FurnitureName);
            Undo.RegisterCreatedObjectUndo(furniture, "Build Lobby Furniture");

            // Whiteboard, flush against the inside of the front wall.
            GameObject board = new GameObject("Whiteboard");
            board.transform.SetParent(furniture.transform, false);
            board.transform.position = new Vector3(centerX, floorY + 1.6f, frontZ - 0.05f);
            CreateBox("Frame", board.transform, Vector3.zero, new Vector3(4.2f, 1.7f, 0.04f), deskMat, false);
            CreateBox("Surface", board.transform, new Vector3(0f, 0f, -0.02f), new Vector3(4f, 1.5f, 0.02f), boardMat, false);
            CreateBox("Tray", board.transform, new Vector3(0f, -0.8f, -0.06f), new Vector3(4f, 0.03f, 0.1f), deskMat, false);
            AddBoxCollider(board, new Vector3(0f, 0f, -0.02f), new Vector3(4.2f, 1.7f, 0.12f));

            // Teacher desk, solid block facing the class.
            Vector3 teacherDeskSize = new Vector3(1.6f, 0.75f, 0.8f);
            CreateBox(
                "TeacherDesk",
                furniture.transform,
                new Vector3(centerX, floorY + teacherDeskSize.y / 2f, frontZ - 1.2f),
                teacherDeskSize,
                deskMat,
                true);

            // Student desks, 2 columns x 3 rows. These double as player spawn spots later.
            GameObject desks = new GameObject("StudentDesks");
            desks.transform.SetParent(furniture.transform, false);
            float[] columnsX = { -1.5f, 1.5f };
            float[] rowsZ = { frontZ - 4f, frontZ - 5.5f, frontZ - 7f };
            int index = 0;
            foreach (float z in rowsZ)
            {
                foreach (float x in columnsX)
                {
                    CreateStudentDesk("Desk_" + index, desks.transform, new Vector3(centerX + x, floorY, z), deskMat);
                    index++;
                }
            }

            Selection.activeGameObject = furniture;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(furniture.scene);
        }

        private static void CreateStudentDesk(string name, Transform parent, Vector3 floorPosition, Material mat)
        {
            const float width = 1f;
            const float depth = 0.6f;
            const float height = 0.75f;
            const float top = 0.04f;
            const float leg = 0.05f;

            GameObject desk = new GameObject(name);
            desk.transform.SetParent(parent, false);
            desk.transform.position = floorPosition;

            CreateBox("Top", desk.transform, new Vector3(0f, height - top / 2f, 0f), new Vector3(width, top, depth), mat, false);

            float legHeight = height - top;
            float legX = width / 2f - leg;
            float legZ = depth / 2f - leg;
            CreateBox("Leg_FL", desk.transform, new Vector3(-legX, legHeight / 2f, legZ), new Vector3(leg, legHeight, leg), mat, false);
            CreateBox("Leg_FR", desk.transform, new Vector3(legX, legHeight / 2f, legZ), new Vector3(leg, legHeight, leg), mat, false);
            CreateBox("Leg_BL", desk.transform, new Vector3(-legX, legHeight / 2f, -legZ), new Vector3(leg, legHeight, leg), mat, false);
            CreateBox("Leg_BR", desk.transform, new Vector3(legX, legHeight / 2f, -legZ), new Vector3(leg, legHeight, leg), mat, false);

            // One collider for the whole desk so the player can't walk through it.
            AddBoxCollider(desk, new Vector3(0f, height / 2f, 0f), new Vector3(width, height, depth));
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 size, Material mat, bool keepCollider)
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

        private static void AddBoxCollider(GameObject target, Vector3 center, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static Material LoadMaterial(string name)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
            if (mat == null)
                Debug.LogWarning($"LobbyBuilder: {MaterialFolder}{name}.mat not found, using the default material.");
            return mat;
        }
    }
}
