using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// One-time migration: swap the lightweight hand-tracking rig for the
    /// proven XR Interaction Toolkit rig copied over from "VR Design
    /// Project" (Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets),
    /// make sure the scooter's scene is actually the one that gets built,
    /// and switch the build target to Android - a Mac desktop build can
    /// never show up in a headset, only Build & Run to Android over USB can.
    /// </summary>
    public static class AndroidVRSetup
    {
        private const string RigPrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [MenuItem("CommodoreCircuit/Migrate To XRI Rig + Android Build Target")]
        public static void Run()
        {
            GameObject scooterGO = Selection.activeGameObject;
            ScooterController controller = scooterGO != null ? scooterGO.GetComponent<ScooterController>() : null;

            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Select the scooter first",
                    "Select your scooter's root GameObject in the Hierarchy (the one with the " +
                    "ScooterController component) and run this command again.",
                    "OK");
                return;
            }

            // 1. Remove the old lightweight hand-tracking rig, if present.
            Transform oldRig = scooterGO.transform.Find("VRRig");
            if (oldRig != null)
            {
                Undo.DestroyObjectImmediate(oldRig.gameObject);
            }

            // 2. Instantiate the proven XRI rig as a child of the scooter,
            // roughly where a rider would stand on the deck.
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Rig prefab not found",
                    "Couldn't find:\n" + RigPrefabPath + "\n\nMake sure the Starter Assets sample copy finished and the project has recompiled.",
                    "OK");
                return;
            }

            GameObject rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scooterGO.transform);
            Undo.RegisterCreatedObjectUndo(rigInstance, "Instantiate XRI Rig");
            rigInstance.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            rigInstance.transform.localRotation = Quaternion.identity;

            // 3. Wire the rig's tracked controller transforms into
            // ScooterController's hand anchors (steering only reads their
            // position, so this works with or without the rest of XRI).
            Transform leftController = FindDeep(rigInstance.transform, "Left Controller");
            Transform rightController = FindDeep(rigInstance.transform, "Right Controller");

            var so = new SerializedObject(controller);
            so.FindProperty("leftHandAnchor").objectReferenceValue = leftController;
            so.FindProperty("rightHandAnchor").objectReferenceValue = rightController;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 4. Make sure this scene is the one that actually gets built.
            Scene activeScene = SceneManager.GetActiveScene();
            var scenes = EditorBuildSettings.scenes.ToList();
            bool alreadyListed = scenes.Any(s => s.path == activeScene.path);
            if (!alreadyListed)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(activeScene.path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            else
            {
                foreach (var s in scenes) { }
                EditorBuildSettings.scenes = scenes
                    .Select(s => s.path == activeScene.path ? new EditorBuildSettingsScene(s.path, true) : s)
                    .ToArray();
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);

            Selection.activeGameObject = scooterGO;

            // 5. Switch the active build target to Android. This can take a
            // while (asset reimport + recompile) and will show its own
            // progress bar.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            EditorUtility.DisplayDialog(
                "Migration started",
                "Old hand-tracking rig removed, XRI rig attached to " + scooterGO.name + ", scene added to Build Settings, " +
                "and the build target is switching to Android now (watch the bottom status bar / a progress popup - " +
                "this can take a minute or two the first time).\n\n" +
                "Once it's done: File > Build Settings > Build And Run, with your Quest connected over USB and " +
                "Developer Mode + USB debugging allowed on the headset.",
                "OK");
        }

        [MenuItem("CommodoreCircuit/Migrate To XRI Rig + Android Build Target", true)]
        public static bool Validate()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform result = FindDeep(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
