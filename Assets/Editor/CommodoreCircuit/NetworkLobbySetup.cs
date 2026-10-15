using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Turns the Fusion tutorial's "Lobby Scene" into the classroom lobby.
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Set Up Networked Lobby" - moves the classroom (Room, Furniture,
    ///     FloorCollider) from Assets/Lobby.unity into Assets/Scenes/Lobby Scene.unity,
    ///     which already has the Fusion Network Manager, waiting-room UI and
    ///     hand-presence XR rig wired up. The tutorial's floor plane and
    ///     invisible walls are disabled (not deleted), the rig is moved to the
    ///     classroom's player spot, and Build Settings become
    ///     0 = Lobby Scene, 1 = Game Scene, which the Fusion spawner expects.
    ///     Assets/Lobby.unity itself is left unchanged on disk.
    ///     Running it again replaces the old classroom copy instead of stacking.
    ///     There is no Undo across scene loads; use git to revert.
    /// </summary>
    public static class NetworkLobbySetup
    {
        private const string NetworkLobbyPath = "Assets/Scenes/Lobby Scene.unity";
        private const string GamePath = "Assets/Scenes/Game Scene.unity";
        private const string ClassroomPath = "Assets/Lobby.unity";
        // FusionPlayerSpawnerAndLobbyController and FusionNetworkPlayer find the rig by this exact name.
        private const string RigName = "XR Origin (XR Rig)";

        private static readonly string[] ClassroomObjects = { "Room", "Furniture", "FloorCollider" };
        private static readonly string[] TutorialObjects = { "Plane", "Invisible Walls" };

        [MenuItem("CommodoreCircuit/Set Up Networked Lobby")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode", "Stop Play Mode and run this command again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene lobby = EditorSceneManager.OpenScene(NetworkLobbyPath, OpenSceneMode.Single);
            Scene classroom = EditorSceneManager.OpenScene(ClassroomPath, OpenSceneMode.Additive);

            GameObject lobbyRig = FindRoot(lobby, RigName);
            GameObject classroomRig = FindAnywhere(classroom, RigName);
            if (lobbyRig == null)
            {
                EditorSceneManager.CloseScene(classroom, true);
                EditorUtility.DisplayDialog(
                    "Rig not found",
                    $"\"{RigName}\" is missing from {NetworkLobbyPath}. Nothing was changed.",
                    "OK");
                return;
            }

            // Remove a copy left by an earlier run, so the classroom isn't stacked twice.
            foreach (string name in ClassroomObjects)
            {
                GameObject old = FindRoot(lobby, name);
                if (old != null)
                    Object.DestroyImmediate(old);
            }

            // Moving (not copying) keeps ProBuilder meshes and material links intact.
            // The classroom scene is closed without saving, so its file on disk is untouched.
            foreach (string name in ClassroomObjects)
            {
                GameObject go = FindRoot(classroom, name);
                if (go == null)
                    Debug.LogWarning($"NetworkLobbySetup: \"{name}\" not found in {ClassroomPath}, skipped.");
                else
                    SceneManager.MoveGameObjectToScene(go, lobby);
            }

            // Players spawn in a row along the rig's right side, so put the rig where the
            // classroom's own player stood (back of the room, facing the whiteboard).
            if (classroomRig != null)
                lobbyRig.transform.SetPositionAndRotation(classroomRig.transform.position, classroomRig.transform.rotation);

            foreach (string name in TutorialObjects)
            {
                GameObject go = FindRoot(lobby, name);
                if (go != null)
                    go.SetActive(false);
            }

            EditorSceneManager.CloseScene(classroom, true);

            // FusionPlayerSpawnerAndLobbyController uses LobbySceneBuildIndex = 0, GameSceneBuildIndex = 1.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(NetworkLobbyPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };

            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
            Selection.activeGameObject = lobbyRig;

            Debug.Log($"NetworkLobbySetup: classroom moved into {NetworkLobbyPath}, rig at {lobbyRig.transform.position}, " +
                      "Build Settings set to 0 = Lobby Scene, 1 = Game Scene.");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }
            return null;
        }

        private static GameObject FindAnywhere(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                        return t.gameObject;
                }
            }
            return null;
        }
    }
}
