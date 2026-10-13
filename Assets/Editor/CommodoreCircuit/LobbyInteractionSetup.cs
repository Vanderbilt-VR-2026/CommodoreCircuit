using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Sets up hands, grab range and turning for the classroom lobby.
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Set Up Hands, Grab Range and Turning" -
    ///     1. In the XR Origin (XR Rig) prefab (used by Lobby and Game Scene):
    ///        turns off far casting on every Near-Far Interactor, so objects
    ///        can only be grabbed within hand reach (NearGrabRadius sets how
    ///        far around the hand that reaches), and turns off "Enable
    ///        Turn Around" on the Snap and Continuous Turn Providers, so
    ///        pushing the right stick down no longer spins the player 180°.
    ///     2. In Lobby Scene: puts Left/Right Hand Presence back under the
    ///        rig's Left/Right Controller, the same way Game Scene has them.
    ///        Running it again skips hands that are already there.
    ///     There is no Undo across prefab saves and scene loads; use git to revert.
    /// </summary>
    public static class LobbyInteractionSetup
    {
        private const string RigPrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        private const string LobbyPath = "Assets/Scenes/Lobby Scene.unity";
        private const string HandPrefabFolder = "Assets/Fusion and Essential Spawned Player Stuffs/Prefabs/";
        // FusionPlayerSpawnerAndLobbyController finds the rig by this exact name.
        private const string RigName = "XR Origin (XR Rig)";
        // Radius in metres of the grab zone around each hand (XRI default is 0.1). Raise it for more reach.
        private const float NearGrabRadius = 0.2f;

        [MenuItem("CommodoreCircuit/Set Up Hands, Grab Range and Turning")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode", "Stop Play Mode and run this command again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!UpdateRigPrefab())
                return;

            AddHandsToLobby();
        }

        // Edit the prefab asset so Lobby and Game Scene both get the same grab range and turning.
        private static bool UpdateRigPrefab()
        {
            GameObject rig = PrefabUtility.LoadPrefabContents(RigPrefabPath);
            if (rig == null)
            {
                EditorUtility.DisplayDialog("Rig prefab not found", $"Couldn't load:\n{RigPrefabPath}\n\nNothing was changed.", "OK");
                return false;
            }

            try
            {
                int interactors = 0;
                foreach (NearFarInteractor interactor in rig.GetComponentsInChildren<NearFarInteractor>(true))
                {
                    interactor.enableFarCasting = false;
                    if (interactor.nearInteractionCaster is SphereInteractionCaster nearCaster)
                        nearCaster.castRadius = NearGrabRadius;
                    interactors++;
                }

                int turnProviders = 0;
                foreach (SnapTurnProvider snap in rig.GetComponentsInChildren<SnapTurnProvider>(true))
                {
                    snap.enableTurnAround = false;
                    turnProviders++;
                }
                foreach (ContinuousTurnProvider continuous in rig.GetComponentsInChildren<ContinuousTurnProvider>(true))
                {
                    continuous.enableTurnAround = false;
                    turnProviders++;
                }

                PrefabUtility.SaveAsPrefabAsset(rig, RigPrefabPath);
                Debug.Log($"LobbyInteractionSetup: far casting off and grab radius {NearGrabRadius} m on {interactors} Near-Far Interactor(s), " +
                          $"turn around off on {turnProviders} turn provider(s) in {RigPrefabPath}.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(rig);
            }
        }

        // Game Scene already has the hands; Lobby Scene lost them, so add them back as children of the controllers.
        private static void AddHandsToLobby()
        {
            Scene lobby = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Single);
            GameObject rig = null;
            foreach (GameObject root in lobby.GetRootGameObjects())
            {
                if (root.name == RigName)
                    rig = root;
            }

            if (rig == null)
            {
                EditorUtility.DisplayDialog("Rig not found", $"\"{RigName}\" is missing from {LobbyPath}. Hands were not added.", "OK");
                return;
            }

            bool leftAdded = AddHand(rig.transform, "Left");
            bool rightAdded = AddHand(rig.transform, "Right");

            if (leftAdded || rightAdded)
            {
                EditorSceneManager.MarkSceneDirty(lobby);
                EditorSceneManager.SaveScene(lobby);
            }

            Debug.Log($"LobbyInteractionSetup: Left Hand Presence {(leftAdded ? "added" : "already there")}, " +
                      $"Right Hand Presence {(rightAdded ? "added" : "already there")} in {LobbyPath}.");
        }

        private static bool AddHand(Transform rig, string side)
        {
            string controllerPath = $"Camera Offset/{side} Controller";
            string handName = $"{side} Hand Presence";

            Transform controller = rig.Find(controllerPath);
            if (controller == null)
            {
                Debug.LogWarning($"LobbyInteractionSetup: \"{controllerPath}\" not found under {RigName}, skipped {handName}.");
                return false;
            }

            if (controller.Find(handName) != null)
                return false;

            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabFolder + handName + ".prefab");
            if (handPrefab == null)
            {
                Debug.LogWarning($"LobbyInteractionSetup: {HandPrefabFolder}{handName}.prefab not found, skipped.");
                return false;
            }

            // Zero local pose, matching Game Scene, so the hand sits exactly on the tracked controller.
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, controller);
            hand.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return true;
        }
    }
}
