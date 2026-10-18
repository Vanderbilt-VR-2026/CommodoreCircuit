using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Set Up VR Comfort on Selected Scooter
    ///
    /// Adds a ScooterComfortVignette wired to the XR Interaction Toolkit's
    /// TunnelingVignette prefab, and softens the scooter's turning and
    /// acceleration to cut down on motion sickness. Safe to run again.
    /// Undo (Ctrl/Cmd+Z) puts the old values back.
    /// </summary>
    public static class ComfortSetup
    {
        private const float ComfortTurnRate = 70f;
        private const float ComfortAcceleration = 10f;
        private const float ComfortBoostAcceleration = 14f;
        private const float ComfortDriftSlideAngle = 20f;

        [MenuItem("CommodoreCircuit/Set Up VR Comfort on Selected Scooter")]
        private static void Setup()
        {
            GameObject scooter = Selection.activeGameObject;
            ScooterController controller = scooter.GetComponent<ScooterController>();

            GameObject prefab = FindVignettePrefab();
            if (prefab == null)
            {
                Debug.LogError("TunnelingVignette prefab not found. Import the XR Interaction Toolkit Starter Assets sample (Package Manager > XR Interaction Toolkit > Samples), then run this again.");
                return;
            }

            ScooterComfortVignette vignette = scooter.GetComponent<ScooterComfortVignette>();
            if (vignette == null)
                vignette = Undo.AddComponent<ScooterComfortVignette>(scooter);

            SerializedObject vignetteSo = new SerializedObject(vignette);
            vignetteSo.FindProperty("scooter").objectReferenceValue = controller;
            vignetteSo.FindProperty("vignettePrefab").objectReferenceValue = prefab;
            vignetteSo.ApplyModifiedProperties();

            SerializedObject controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("levelRiderView").boolValue = true;
            controllerSo.FindProperty("turnRate").floatValue = ComfortTurnRate;
            controllerSo.FindProperty("acceleration").floatValue = ComfortAcceleration;
            controllerSo.FindProperty("boostAcceleration").floatValue = ComfortBoostAcceleration;
            controllerSo.FindProperty("driftSlideAngle").floatValue = ComfortDriftSlideAngle;
            controllerSo.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scooter.scene);
            Debug.Log("VR comfort set up on " + scooter.name + ": level view on, comfort vignette added, Turn Rate "
                + ComfortTurnRate + ", Acceleration " + ComfortAcceleration + ", Boost Acceleration "
                + ComfortBoostAcceleration + ", Drift Slide Angle " + ComfortDriftSlideAngle + ". Save the scene.");
        }

        [MenuItem("CommodoreCircuit/Set Up VR Comfort on Selected Scooter", true)]
        private static bool ValidateSetup()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        private static GameObject FindVignettePrefab()
        {
            foreach (string guid in AssetDatabase.FindAssets("TunnelingVignette t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == "TunnelingVignette")
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }
    }
}
