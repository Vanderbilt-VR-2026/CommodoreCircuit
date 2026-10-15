using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Add VR Handlebar to Selected Scooter
    ///
    /// Adds a "Handlebar" child to the scooter (a stem, crossbar and two grips
    /// driven by ScooterHandlebar) and puts a simple controller-shaped model
    /// on each hand anchor so the player sees themselves gripping the bars.
    /// Run "Add VR Rig to Selected Scooter" first so the hand anchors exist.
    /// Safe to run again: it replaces the old handlebar and controller models.
    /// </summary>
    public static class HandlebarBuilder
    {
        private const string HandlebarName = "Handlebar";
        private const string ControllerModelName = "ControllerModel";
        private const string MaterialFolder = "Assets/Materials";

        [MenuItem("CommodoreCircuit/Add VR Handlebar to Selected Scooter")]
        private static void AddHandlebar()
        {
            GameObject scooterGO = Selection.activeGameObject;
            ScooterController controller = scooterGO != null ? scooterGO.GetComponent<ScooterController>() : null;
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Select the scooter",
                    "Select the scooter's root object in the Hierarchy (the one with the ScooterController component) and run this command again.",
                    "OK");
                return;
            }

            var so = new SerializedObject(controller);
            Transform leftHand = so.FindProperty("leftHandAnchor").objectReferenceValue as Transform;
            Transform rightHand = so.FindProperty("rightHandAnchor").objectReferenceValue as Transform;
            if (leftHand == null || rightHand == null)
            {
                EditorUtility.DisplayDialog("No hand anchors yet",
                    "This scooter has no hand anchors. Run CommodoreCircuit > Add VR Rig to Selected Scooter first, then run this again.",
                    "OK");
                return;
            }

            Material metal = GetOrCreateMaterial("HandlebarMetal", new Color(0.62f, 0.64f, 0.68f), 0.7f, 0.65f);
            Material rubber = GetOrCreateMaterial("HandlebarGrip", new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f);
            Material accent = GetOrCreateMaterial("ControllerAccent", new Color(0.95f, 0.78f, 0.2f), 0f, 0.5f);

            RemoveChildren(scooterGO.transform, HandlebarName);
            RemoveChildren(leftHand, ControllerModelName);
            RemoveChildren(rightHand, ControllerModelName);

            // Handlebar: Handlebar (positioned each frame) > Stem, Bar > Crossbar, grips.
            var root = new GameObject(HandlebarName);
            Undo.RegisterCreatedObjectUndo(root, "Create Handlebar");
            root.transform.SetParent(scooterGO.transform, false);
            root.transform.localPosition = new Vector3(0f, 1f, 0.4f);

            Transform stem = CreatePart("Stem", root.transform, metal, Vector3.zero);
            Transform bar = new GameObject("Bar").transform;
            Undo.RegisterCreatedObjectUndo(bar.gameObject, "Create Handlebar Bar");
            bar.SetParent(root.transform, false);
            Transform crossbar = CreatePart("Crossbar", bar, metal, new Vector3(0f, 0f, 90f));
            Transform leftGrip = CreatePart("LeftGrip", bar, rubber, new Vector3(0f, 0f, 90f));
            Transform rightGrip = CreatePart("RightGrip", bar, rubber, new Vector3(0f, 0f, 90f));

            var handlebar = root.AddComponent<ScooterHandlebar>();
            var hso = new SerializedObject(handlebar);
            hso.FindProperty("scooter").objectReferenceValue = controller;
            hso.FindProperty("bar").objectReferenceValue = bar;
            hso.FindProperty("crossbar").objectReferenceValue = crossbar;
            hso.FindProperty("leftGrip").objectReferenceValue = leftGrip;
            hso.FindProperty("rightGrip").objectReferenceValue = rightGrip;
            hso.FindProperty("stem").objectReferenceValue = stem;
            hso.ApplyModifiedPropertiesWithoutUndo();

            BuildControllerModel(leftHand, rubber, accent);
            BuildControllerModel(rightHand, rubber, accent);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scooterGO.scene);
            Selection.activeGameObject = root;

            Debug.Log("Handlebar added under " + scooterGO.name + " and a controller model added to each hand. " +
                      "Save the scene. In play mode the bars appear where your hands are 1.5 seconds after start. " +
                      "Squeeze both grips with your hands on the bars to grab them and steer; the grips turn yellow while held. " +
                      "Click a thumbstick (or press R) to move the bars to where your hands are now. " +
                      "Tune 'Handlebar Full Lock Angle' and 'Grab Radius' on the ScooterController.");
        }

        [MenuItem("CommodoreCircuit/Add VR Handlebar to Selected Scooter", true)]
        private static bool ValidateAddHandlebar()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        // A hand-sized stand-in for a Quest controller: a handle and a ring. Swap in
        // a real controller model under each hand anchor later if the team wants one.
        private static void BuildControllerModel(Transform hand, Material body, Material accent)
        {
            var model = new GameObject(ControllerModelName);
            Undo.RegisterCreatedObjectUndo(model, "Create Controller Model");
            model.transform.SetParent(hand, false);

            Transform handle = CreatePart("Handle", model.transform, body, new Vector3(90f, 0f, 0f));
            handle.localPosition = new Vector3(0f, 0f, 0.03f);
            handle.localScale = new Vector3(0.04f, 0.07f, 0.04f);

            Transform ring = CreatePart("Ring", model.transform, accent, Vector3.zero, PrimitiveType.Sphere);
            ring.localPosition = new Vector3(0f, 0.025f, 0.11f);
            ring.localScale = new Vector3(0.09f, 0.02f, 0.09f);
        }

        private static Transform CreatePart(string name, Transform parent, Material material, Vector3 euler,
            PrimitiveType type = PrimitiveType.Cylinder)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.name = name;

            // Visual only. Colliders on children would get in the scooter's wall sweep.
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static void RemoveChildren(Transform parent, string childName)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets", "Materials");
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
