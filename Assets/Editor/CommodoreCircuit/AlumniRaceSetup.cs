using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CommodoreCircuit.Race;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Set Up Alumni Lawn Race
    ///
    /// Makes "Alumni Lawn Scenery" a playable race scene and the one the lobby loads:
    ///   - copies the scooter, XR Interaction Manager and Race Systems (sessions, HUD,
    ///     countdown, wrong way, results) from Campus Track Scene,
    ///   - puts a RaceTrack on "Alumni Track" reading alumni_track_route.json,
    ///   - turns off the scene's own Main Camera (the scooter brings its own),
    ///   - swaps Campus Track Scene for Alumni Lawn Scenery in Build Settings.
    /// Campus Track Scene is not changed. Safe to run again: anything already there is kept.
    /// </summary>
    public static class AlumniRaceSetup
    {
        private const string AlumniScene = "Assets/Scenes/Alumni Lawn Scenery.unity";
        private const string CampusScene = "Assets/Scenes/Campus Track Scene.unity";
        private const string TrackDataPath = "Assets/CampusTrack/alumni_track_route.json";
        private const string TrackRoot = "Alumni Track";
        private static readonly string[] CopiedRoots = { "XR Interaction Manager", "Race Systems", "Scooter" };

        [MenuItem("CommodoreCircuit/Set Up Alumni Lawn Race")]
        private static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene alumni = EditorSceneManager.OpenScene(AlumniScene, OpenSceneMode.Single);
            Scene campus = EditorSceneManager.OpenScene(CampusScene, OpenSceneMode.Additive);

            // Moving (not copying) keeps every reference between these objects intact.
            // Campus is then closed without saving, so it keeps its own originals.
            var copied = new List<string>();
            foreach (string name in CopiedRoots)
            {
                if (FindRoot(alumni, name) != null) continue;
                GameObject source = FindRoot(campus, name);
                if (source == null) { Debug.LogWarning("[Race setup] Campus Track Scene has no '" + name + "'."); continue; }
                SceneManager.MoveGameObjectToScene(source, alumni);
                copied.Add(name);
            }
            EditorSceneManager.CloseScene(campus, true);
            SceneManager.SetActiveScene(alumni);

            GameObject root = FindRoot(alumni, TrackRoot);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Track missing",
                    "No '" + TrackRoot + "' in " + AlumniScene + ". Run CommodoreCircuit > Build Alumni Lawn Track first.", "OK");
                return;
            }
            RaceTrack track = AddRaceTrack(root, AssetDatabase.LoadAssetAtPath<TextAsset>(TrackDataPath));

            GameObject sceneCamera = FindRoot(alumni, "Main Camera");
            if (sceneCamera != null && sceneCamera.activeSelf) sceneCamera.SetActive(false);

            // Pole position, so pressing Play here starts on the grid.
            ScooterController scooter = Object.FindAnyObjectByType<ScooterController>();
            if (scooter != null && track.IsReady)
            {
                Pose pole = track.GridPose(0);
                scooter.transform.SetPositionAndRotation(pole.position + Vector3.up * 0.05f, pole.rotation);
            }

            EditorSceneManager.MarkSceneDirty(alumni);
            EditorSceneManager.SaveScene(alumni);
            UseAlumniInBuild();

            string msg = (copied.Count > 0 ? "Copied from Campus: " + string.Join(", ", copied) + ".\n" : "Race objects were already here.\n")
                + "Lap " + track.LapLength.ToString("0") + " m.\n\nBuild Settings now load Alumni Lawn Scenery instead of Campus Track Scene.";
            Debug.Log("[Race setup] " + msg);
            EditorUtility.DisplayDialog("Alumni Lawn race ready", msg, "OK");
        }

        /// <summary>Adds (or reuses) the RaceTrack on the track root and points it at the route.</summary>
        public static RaceTrack AddRaceTrack(GameObject root, TextAsset data)
        {
            RaceTrack track = root.GetComponent<RaceTrack>();
            if (track == null) track = Undo.AddComponent<RaceTrack>(root);
            var so = new SerializedObject(track);
            so.FindProperty("trackData").objectReferenceValue = data;
            so.ApplyModifiedProperties();
            track.Load();
            return track;
        }

        // Same slot in the build list, so the lobby's GameSceneBuildIndex (1) still lands on the race.
        private static void UseAlumniInBuild()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == AlumniScene);
            int campus = scenes.FindIndex(s => s.path == CampusScene);
            if (campus >= 0)
            {
                scenes[campus].enabled = false;
                scenes.Insert(campus + 1, new EditorBuildSettingsScene(AlumniScene, true));
            }
            else scenes.Add(new EditorBuildSettingsScene(AlumniScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }
    }
}
