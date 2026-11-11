#if UNITY_EDITOR
using CommodoreCircuit.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommodoreCircuit.Interactables.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Interactables > Place Interactables On Race Track
    ///
    /// Drops a starter layout of hazards and power-ups around the lap of the
    /// RaceTrack in the open scene, under one "Interactables" object. Hazards are
    /// turned to cross the road (their waypoints span it edge to edge); power-ups
    /// float in lanes. It's a first pass to playtest from: move, delete or
    /// duplicate anything afterwards. Running it again replaces the group.
    /// </summary>
    public static class InteractableTrackPlacer
    {
        const string GroupName = "Interactables";

        struct Slot
        {
            public string prefab;
            public float lapFraction;   // 0..1 around the lap, measured from the start line
            public float lateral;       // power-ups only: metres right (+) / left (-) of centre

            public Slot(string prefab, float lapFraction, float lateral = 0f)
            {
                this.prefab = prefab;
                this.lapFraction = lapFraction;
                this.lateral = lateral;
            }
        }

        // Starts gentle (cookies, a squirrel), builds to the pipe mid-lap, and gives a
        // shield before the frisbee and squirrel near the end. Nothing in the first
        // 8% of the lap, so the starting grid stays clear.
        static readonly Slot[] Layout =
        {
            new Slot("SpeedCookie", 0.10f, -2f),
            new Slot("SpeedCookie", 0.10f, 0f),
            new Slot("SpeedCookie", 0.10f, 2f),
            new Slot("Squirrel", 0.20f),
            new Slot("LostFreshman", 0.32f),
            new Slot("MealSwipe", 0.42f),
            new Slot("ConstructionPipe", 0.52f),
            new Slot("SpeedCookie", 0.63f, -1.5f),
            new Slot("SpeedCookie", 0.63f, 1.5f),
            new Slot("StrayFrisbee", 0.72f),
            new Slot("Squirrel", 0.84f),
            new Slot("MealSwipe", 0.92f),
        };

        [MenuItem("CommodoreCircuit/Interactables/Place Interactables On Race Track")]
        static void Place()
        {
            var track = Object.FindFirstObjectByType<RaceTrack>();
            if (track == null)
            {
                EditorUtility.DisplayDialog("No race track", "Open the race scene (it needs a RaceTrack component) and try again.", "OK");
                return;
            }

            track.Load();   // Awake doesn't run in edit mode.
            if (!track.IsReady)
            {
                EditorUtility.DisplayDialog("Race track not ready", "The RaceTrack has no track data (campus_track.json) assigned.", "OK");
                return;
            }

            var scene = track.gameObject.scene;
            GameObject existing = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == GroupName) { existing = root; break; }

            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Replace interactables?",
                        $"The scene already has an \"{GroupName}\" object. Replace it with a fresh layout? (Undo works.)",
                        "Replace", "Cancel"))
                    return;
                Undo.DestroyObjectImmediate(existing);
            }

            Physics.SyncTransforms();
            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "Place Interactables");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);

            float startDistance = track.Project(track.StartPosition).Distance;
            int placed = 0;

            foreach (var slot in Layout)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{InteractablePaths.Prefabs}/{slot.prefab}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[Commodore Circuit] Missing prefab {slot.prefab}. Run CommodoreCircuit > Interactables > Build Interactable Prefabs first.");
                    continue;
                }

                Vector3 centre = track.PositionAt(startDistance + slot.lapFraction * track.LapLength);
                RaceTrack.TrackPoint here = track.Project(centre);
                Vector3 forward = here.Forward.sqrMagnitude > 0.001f ? here.Forward : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                go.name = $"{slot.prefab} @ {slot.lapFraction:P0}";

                var path = go.GetComponent<PathMover>();
                if (path != null)
                {
                    // Hazards: prefab waypoints run along the root's local X, so facing the
                    // race direction makes them cross the road left to right.
                    go.transform.SetPositionAndRotation(Ground(centre), facing);
                    float span = slot.prefab == "LostFreshman"
                        ? here.HalfWidth * 0.6f          // wanders in the road, doesn't cross it
                        : here.HalfWidth + 1.5f;         // starts just off the edge, out of the way
                    SpanWaypoints(path, span);
                }
                else
                {
                    float lateral = Mathf.Clamp(slot.lateral, -(here.HalfWidth - 1f), here.HalfWidth - 1f);
                    go.transform.SetPositionAndRotation(Ground(centre + right * lateral) + Vector3.up * 0.85f, facing);
                }
                placed++;
            }

            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Commodore Circuit] Placed {placed} interactables around the {track.LapLength:F0} m lap. Drag waypoints to adjust hazard paths.", group);
        }

        static void SpanWaypoints(PathMover path, float span)
        {
            var wps = path.waypoints;
            if (wps == null || wps.Length < 2) return;
            for (int i = 0; i < wps.Length; i++)
            {
                if (wps[i] == null) continue;
                Undo.RecordObject(wps[i], "Place Interactables");
                Vector3 local = wps[i].localPosition;
                local.x = Mathf.Lerp(-span, span, i / (float)(wps.Length - 1));
                local.z = 0f;
                wps[i].localPosition = local;
            }
            if (path.mover != null && wps[0] != null)
            {
                Undo.RecordObject(path.mover, "Place Interactables");
                path.mover.localPosition = wps[0].localPosition;
            }
        }

        // Drops a point onto whatever solid ground is under it. Starts only 2 m up so it
        // doesn't land on the roof of the Carmichael overhang.
        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }
    }
}
#endif
