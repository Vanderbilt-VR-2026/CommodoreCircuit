using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Set Open Scene As The Only Build Scene
    ///
    /// Build & Run only puts scenes from the build list into the app. If the
    /// list holds a different scene (for example the empty SampleScene), the
    /// headset loads that one: no scooter, no tracked camera, and a view that
    /// does not follow your head. Open the scooter scene, run this, then build.
    /// </summary>
    public static class BuildSceneSetup
    {
        [MenuItem("CommodoreCircuit/Set Open Scene As The Only Build Scene")]
        private static void SetOpenSceneAsBuildScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Save the scene first",
                    "The open scene has not been saved yet. Save it (File > Save As), then run this again.",
                    "OK");
                return;
            }

            if (scene.isDirty) EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };
            Debug.Log("Build list now contains only: " + scene.path);
            EditorUtility.DisplayDialog("Build scene set",
                "The app will now start in:\n" + scene.path + "\n\nBuild and run to your headset again.",
                "OK");
        }
    }
}
