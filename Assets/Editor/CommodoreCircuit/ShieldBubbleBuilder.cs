using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Add Shield Bubble to Selected Scooter
    ///
    /// Adds the Commodore Card force field: a "Shield Bubble" egg shape around the
    /// whole scooter, wheels to handlebar grips (the rider's head and the HUD stay
    /// outside it), using the CommodoreCircuit/ShieldBubble shader, driven by
    /// ScooterShieldBubble. Hidden until a shield is picked up.
    ///
    /// Safe to run again: it replaces the old bubble. Scale the "Shield Bubble" object
    /// to resize it. The material is saved as Assets/Materials/ShieldBubble.mat.
    /// </summary>
    public static class ShieldBubbleBuilder
    {
        private const string BubbleName = "Shield Bubble";
        private const string MaterialPath = "Assets/Materials/ShieldBubble.mat";

        // Scooter-local centre and full size (diameters) of the bubble, in metres. Sized to
        // hold the grips (x 0.2, y 1.1, z 0.5), both tyres (z +-0.75) and the tail light.
        private static readonly Vector3 Centre = new Vector3(0f, 0.5f, 0f);
        private static readonly Vector3 Size = new Vector3(1.1f, 1.7f, 2.1f);

        [MenuItem("CommodoreCircuit/Add Shield Bubble to Selected Scooter")]
        private static void Build()
        {
            GameObject scooter = Selection.activeGameObject;

            foreach (Transform t in scooter.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == BubbleName) Undo.DestroyObjectImmediate(t.gameObject);

            Shader shader = Shader.Find("CommodoreCircuit/ShieldBubble");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Shader missing",
                    "Couldn't find CommodoreCircuit/ShieldBubble (Assets/Art/Shaders/CC_ShieldBubble.shader). Let Unity finish importing and try again.", "OK");
                return;
            }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            GameObject bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(bubble, "Add Shield Bubble");
            bubble.name = BubbleName;
            // Purely visual: a collider here could catch the scooter's own wall checks.
            Object.DestroyImmediate(bubble.GetComponent<Collider>());
            bubble.transform.SetParent(scooter.transform, false);
            bubble.transform.localPosition = Centre;
            bubble.transform.localScale = Size;

            var r = bubble.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;

            var field = bubble.AddComponent<ScooterShieldBubble>();
            var so = new SerializedObject(field);
            so.FindProperty("bubble").objectReferenceValue = r;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scooter.scene);
            Selection.activeGameObject = bubble;
            Debug.Log("Shield bubble added to " + scooter.name + ". Save the scene, then grab a Commodore Card.");
        }

        [MenuItem("CommodoreCircuit/Add Shield Bubble to Selected Scooter", true)]
        private static bool ValidateBuild()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }
    }
}
