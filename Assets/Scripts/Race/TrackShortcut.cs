using System.Collections.Generic;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// A side path that leaves the lap and rejoins it further on (the Rand Hall shortcut).
    ///
    /// Race progress normally comes from the nearest point on the lap's centerline. Inside
    /// a shortcut that point jumps between the two parts of the lap it cuts across, and on
    /// the way out the racer would skip ahead too far in one go to be counted. So while a
    /// racer is on a shortcut (and off the main road), RaceTrack.ProjectWithShortcuts asks
    /// this instead: progress runs smoothly from where the shortcut leaves the lap to where
    /// it rejoins, and "forward" follows the shortcut, so the wrong-way warning stays quiet.
    ///
    /// Built by CommodoreCircuit > Build Rand Shortcut. The path is in this object's local space,
    /// in race order, and must start and end on the lap's centerline.
    /// </summary>
    public class TrackShortcut : MonoBehaviour
    {
        public static readonly List<TrackShortcut> All = new List<TrackShortcut>();

        [Tooltip("Centerline of the shortcut in race order, local space. Starts and ends on the lap.")]
        [SerializeField] private List<Vector3> path = new List<Vector3>();
        [Tooltip("Half the drivable width (m). A racer further than this (plus a little) from the path isn't on it.")]
        [SerializeField, Min(0.5f)] private float halfWidth = 5.4f;
        [SerializeField] private bool drawGizmos = true;

        private float[] cumulative;
        private float length;
        private RaceTrack preparedFor;
        private float entryDistance, span;

        public void SetPath(IList<Vector3> localPoints, float width)
        {
            path = new List<Vector3>(localPoints);
            halfWidth = width;
            preparedFor = null;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>
        /// True if <paramref name="world"/> is on this shortcut. Then <paramref name="lapDistance"/>
        /// is how far along the lap it counts as, and <paramref name="forward"/> the race direction there.
        /// </summary>
        public bool TryProject(RaceTrack track, Vector3 world, out float lapDistance, out Vector3 forward)
        {
            lapDistance = 0f;
            forward = Vector3.forward;
            if (path.Count < 2 || !Prepare(track)) return false;

            Vector3 local = transform.InverseTransformPoint(world);
            var p = new Vector2(local.x, local.z);
            int best = -1;
            float bestDistSq = float.MaxValue, bestT = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 a = XZ(path[i]), ab = XZ(path[i + 1]) - a;
                float lenSq = ab.sqrMagnitude;
                float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = i; bestT = t; }
            }
            float reach = halfWidth + 0.5f;
            if (best < 0 || bestDistSq > reach * reach) return false;

            float along = (cumulative[best] + bestT * (cumulative[best + 1] - cumulative[best])) / length;
            lapDistance = Mathf.Repeat(entryDistance + along * span, track.LapLength);
            Vector3 dir = path[best + 1] - path[best];
            dir.y = 0f;
            forward = transform.TransformDirection(dir).normalized;
            return true;
        }

        // Where the shortcut leaves and rejoins the lap, worked out once per track.
        private bool Prepare(RaceTrack track)
        {
            if (preparedFor == track && cumulative != null) return true;
            if (track == null || !track.IsReady) return false;

            cumulative = new float[path.Count];
            for (int i = 1; i < path.Count; i++)
                cumulative[i] = cumulative[i - 1] + Vector2.Distance(XZ(path[i - 1]), XZ(path[i]));
            length = Mathf.Max(0.01f, cumulative[path.Count - 1]);

            entryDistance = track.Project(transform.TransformPoint(path[0])).Distance;
            float exitDistance = track.Project(transform.TransformPoint(path[path.Count - 1])).Distance;
            span = Mathf.Repeat(exitDistance - entryDistance, track.LapLength);
            preparedFor = track;
            return true;
        }

        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        private void OnDrawGizmos()
        {
            if (!drawGizmos || path.Count < 2) return;
            Gizmos.color = new Color(1f, 0.4f, 0.85f);
            Vector3 lift = Vector3.up * 0.3f;
            for (int i = 0; i + 1 < path.Count; i++)
                Gizmos.DrawLine(transform.TransformPoint(path[i]) + lift, transform.TransformPoint(path[i + 1]) + lift);
        }
    }
}
