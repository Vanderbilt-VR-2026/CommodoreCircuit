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
    ///
    /// CommodoreCircuit > Interactables > Make Track Harder
    ///
    /// Retimes every proximity hazard in the scene so it reaches the middle of the road
    /// about when a racer gets there (before, a racer at full speed was usually past it
    /// before it finished its tell), and adds a second wave of hazards in the gaps of the
    /// starter layout, under "Interactables (Harder)". Running it again replaces that group.
    /// </summary>
    public static class InteractableTrackPlacer
    {
        const string GroupName = "Interactables";

        struct Slot
        {
            public string prefab;
            public float lapFraction;   // 0..1 around the lap, measured from the start line
            public float lateral;       // power-ups and the freshman: metres right (+) / left (-) of centre
            public bool flip;           // hazards: cross the road from the other side

            public Slot(string prefab, float lapFraction, float lateral = 0f, bool flip = false)
            {
                this.prefab = prefab;
                this.lapFraction = lapFraction;
                this.lateral = lateral;
                this.flip = flip;
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

        // The second wave, in the gaps of Layout. Squirrels come in pairs from opposite
        // sides, a freshman wanders off-centre so there's one clear side to pick, and the
        // two pipes are close enough that dodging the first can line you up with the second.
        static readonly Slot[] HarderLayout =
        {
            new Slot("StrayFrisbee", 0.15f, flip: true),
            new Slot("LostFreshman", 0.26f, lateral: -1.5f),
            new Slot("Squirrel", 0.36f),
            new Slot("Squirrel", 0.38f, flip: true),
            new Slot("StrayFrisbee", 0.47f),
            new Slot("ConstructionPipe", 0.57f, flip: true),
            new Slot("LostFreshman", 0.68f, lateral: 1.5f),
            new Slot("Squirrel", 0.77f, flip: true),
            new Slot("ConstructionPipe", 0.80f),
            new Slot("StrayFrisbee", 0.88f, flip: true),
        };

        const string HarderGroupName = "Interactables (Harder)";

        // Speed a hazard is timed for: a little over the scooter's top speed, for boosts.
        const float RacerSpeed = 15f;

        [MenuItem("CommodoreCircuit/Interactables/Place Interactables On Race Track")]
        static void Place()
        {
            var track = ReadyTrack();
            if (track == null) return;
            if (!ReplaceGroup(track.gameObject.scene, GroupName, out var group)) return;

            float startDistance = track.Project(track.StartPosition).Distance;
            int placed = 0;
            foreach (var slot in Layout)
                if (PlaceSlot(track, group.transform, startDistance, slot)) placed++;

            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[Commodore Circuit] Placed {placed} interactables around the {track.LapLength:F0} m lap. Drag waypoints to adjust hazard paths.", group);
        }

        [MenuItem("CommodoreCircuit/Interactables/Make Track Harder")]
        static void MakeHarder()
        {
            var track = ReadyTrack();
            if (track == null) return;
            if (!ReplaceGroup(track.gameObject.scene, HarderGroupName, out var group)) return;

            float startDistance = track.Project(track.StartPosition).Distance;
            int placed = 0;
            foreach (var slot in HarderLayout)
                if (PlaceSlot(track, group.transform, startDistance, slot)) placed++;

            int retimed = 0;
            foreach (var path in Object.FindObjectsByType<PathMover>(FindObjectsSortMode.None))
                if (Retime(path)) retimed++;

            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[Commodore Circuit] Added {placed} hazards and retimed {retimed} so they're mid-road when you arrive. Save the scene to keep it.", group);
        }

        // Proximity hazards: start the run early enough that the hazard is about halfway
        // across when a racer at full speed arrives. tell + half the crossing, times speed.
        static bool Retime(PathMover path)
        {
            if (path.activation != PathMover.Activation.Proximity || path.waypoints == null || path.speed <= 0f) return false;
            float length = 0f;
            for (int i = 1; i < path.waypoints.Length; i++)
                if (path.waypoints[i - 1] && path.waypoints[i])
                    length += Vector3.Distance(path.waypoints[i - 1].position, path.waypoints[i].position);
            float radius = (path.tellDuration + 0.5f * length / path.speed) * RacerSpeed;
            Undo.RecordObject(path, "Make Track Harder");
            path.triggerRadius = Mathf.Round(Mathf.Clamp(radius, 12f, 45f));
            // Shorter rests, so the next racer along doesn't get a free pass.
            path.cooldown = Mathf.Min(path.cooldown, 1.5f);
            if (PrefabUtility.IsPartOfPrefabInstance(path)) PrefabUtility.RecordPrefabInstancePropertyModifications(path);
            return true;
        }

        static RaceTrack ReadyTrack()
        {
            var track = Object.FindFirstObjectByType<RaceTrack>();
            if (track == null)
            {
                EditorUtility.DisplayDialog("No race track", "Open the race scene (it needs a RaceTrack component) and try again.", "OK");
                return null;
            }

            track.Load();   // Awake doesn't run in edit mode.
            if (!track.IsReady)
            {
                EditorUtility.DisplayDialog("Race track not ready", "The RaceTrack has no track data (campus_track.json) assigned.", "OK");
                return null;
            }
            return track;
        }

        // A fresh, empty root object called name. Asks before replacing one that's already there.
        static bool ReplaceGroup(UnityEngine.SceneManagement.Scene scene, string name, out GameObject group)
        {
            group = null;
            GameObject existing = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) { existing = root; break; }

            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Replace interactables?",
                        $"The scene already has an \"{name}\" object. Replace it with a fresh layout? (Undo works.)",
                        "Replace", "Cancel"))
                    return false;
                Undo.DestroyObjectImmediate(existing);
            }

            Physics.SyncTransforms();
            group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, "Place Interactables");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);
            return true;
        }

        static bool PlaceSlot(RaceTrack track, Transform group, float startDistance, Slot slot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{InteractablePaths.Prefabs}/{slot.prefab}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[Commodore Circuit] Missing prefab {slot.prefab}. Run CommodoreCircuit > Interactables > Build Interactable Prefabs first.");
                return false;
            }

            Vector3 centre = track.PositionAt(startDistance + slot.lapFraction * track.LapLength);
            RaceTrack.TrackPoint here = track.Project(centre);
            Vector3 forward = here.Forward.sqrMagnitude > 0.001f ? here.Forward : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            float lateral = Mathf.Clamp(slot.lateral, -(here.HalfWidth - 1f), here.HalfWidth - 1f);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            go.name = $"{slot.prefab} @ {slot.lapFraction:P0}";

            var path = go.GetComponent<PathMover>();
            if (path != null)
            {
                // Hazards: prefab waypoints run along the root's local X, so facing the
                // race direction makes them cross the road left to right (flipped: right to left).
                if (slot.flip) facing *= Quaternion.Euler(0f, 180f, 0f);
                go.transform.SetPositionAndRotation(Ground(centre + right * lateral), facing);
                float span = slot.prefab == "LostFreshman"
                    ? here.HalfWidth * 0.6f          // wanders in the road, doesn't cross it
                    : here.HalfWidth + 1.5f;         // starts just off the edge, out of the way
                SpanWaypoints(path, span);
            }
            else
            {
                go.transform.SetPositionAndRotation(Ground(centre + right * lateral) + Vector3.up * 0.85f, facing);
            }
            return true;
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
