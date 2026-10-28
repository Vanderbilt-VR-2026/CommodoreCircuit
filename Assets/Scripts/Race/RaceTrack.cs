using System;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// The lap, as a closed centerline read from Assets/CampusTrack/campus_track.json
    /// (the same file CommodoreCircuit > Build Campus Track uses). It answers one
    /// question: "how far along the lap is this point, and which way is forward here?"
    /// Everything else (laps, standings, wrong way) is built on that.
    ///
    /// Put it on the "Campus Track" root. The JSON is in that object's local space,
    /// so moving or rotating the track moves the race line with it.
    /// </summary>
    public class RaceTrack : MonoBehaviour
    {
        public static RaceTrack Instance { get; private set; }

        [Tooltip("campus_track.json. Uses its 'centerline' (x,z pairs in race order), 'widths' and 'start'.")]
        [SerializeField] private TextAsset trackData;

        [Tooltip("How many centerline points either side of the last match to search (points are ~1.5 m apart).")]
        [SerializeField, Min(4)] private int searchWindow = 40;

        [Tooltip("If the nearest point in the window is further than this (m), search the whole lap. Handles respawns and teleports.")]
        [SerializeField, Min(1f)] private float relocateDistance = 20f;

        [SerializeField] private bool drawGizmos = true;

        private Vector2[] points;     // local XZ
        private float[] cumulative;   // distance from the start line to points[i]
        private float[] widths;
        private float lapLength;
        private Vector3 startLocal;
        private float startYaw;

        public bool IsReady => points != null && points.Length >= 3;
        public float LapLength => lapLength;
        public int PointCount => points?.Length ?? 0;

        /// <summary>Start line position and facing in world space.</summary>
        public Vector3 StartPosition => transform.TransformPoint(startLocal);
        public Vector3 StartForward => transform.TransformDirection(Quaternion.Euler(0f, startYaw, 0f) * Vector3.forward);

        public struct TrackPoint
        {
            /// <summary>Index of the segment (points[i] to points[i+1]). Pass back in as the next hint.</summary>
            public int Segment;
            /// <summary>Metres from the start line along the lap, 0..LapLength.</summary>
            public float Distance;
            /// <summary>Race direction here (world, flat).</summary>
            public Vector3 Forward;
            /// <summary>Signed metres from the centerline, + is right of the race direction.</summary>
            public float Lateral;
            /// <summary>Half the road width here.</summary>
            public float HalfWidth;
        }

        [Serializable] private class StartData { public float[] pos; public float yaw; }
        [Serializable] private class Data { public float[] centerline; public float[] widths; public StartData start; }

        private void Awake()
        {
            Instance = this;
            Load();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetData(TextAsset json)
        {
            trackData = json;
            Load();
        }

        public void Load()
        {
            points = null;
            if (trackData == null)
            {
                Debug.LogWarning("[Race] RaceTrack has no track data assigned (campus_track.json).", this);
                return;
            }

            Data data = JsonUtility.FromJson<Data>(trackData.text);
            if (data?.centerline == null || data.centerline.Length < 6)
            {
                Debug.LogError("[Race] campus_track.json has no centerline.", this);
                return;
            }

            int n = data.centerline.Length / 2;
            points = new Vector2[n];
            widths = new float[n];
            for (int i = 0; i < n; i++)
            {
                points[i] = new Vector2(data.centerline[2 * i], data.centerline[2 * i + 1]);
                widths[i] = data.widths != null && i < data.widths.Length ? data.widths[i] : 9f;
            }

            // The JSON repeats nothing at the end: the lap closes from the last point back to the first.
            cumulative = new float[n + 1];
            for (int i = 0; i < n; i++)
                cumulative[i + 1] = cumulative[i] + Vector2.Distance(points[i], points[(i + 1) % n]);
            lapLength = cumulative[n];

            if (data.start?.pos != null && data.start.pos.Length >= 3)
                startLocal = new Vector3(data.start.pos[0], data.start.pos[1], data.start.pos[2]);
            else
                startLocal = new Vector3(points[0].x, 0f, points[0].y);
            startYaw = data.start != null ? data.start.yaw : 0f;
        }

        /// <summary>
        /// Where <paramref name="world"/> sits along the lap. Pass the previous result's
        /// Segment as <paramref name="hint"/> so only nearby road is searched; that keeps a
        /// racer from snapping to a different part of the campus that happens to be close.
        /// </summary>
        public TrackPoint Project(Vector3 world, int hint = -1)
        {
            if (!IsReady) return default;

            Vector3 local = transform.InverseTransformPoint(world);
            var p = new Vector2(local.x, local.z);
            int n = points.Length;

            int best = -1;
            float bestDistSq = float.MaxValue, bestT = 0f;

            if (hint >= 0)
            {
                for (int k = -searchWindow; k <= searchWindow; k++)
                    TrySegment(Wrap(hint + k, n), p, ref best, ref bestDistSq, ref bestT);
            }
            if (best < 0 || bestDistSq > relocateDistance * relocateDistance)
            {
                for (int i = 0; i < n; i++)
                    TrySegment(i, p, ref best, ref bestDistSq, ref bestT);
            }

            Vector2 a = points[best], b = points[(best + 1) % n];
            Vector2 dir = (b - a).normalized;
            Vector2 closest = Vector2.Lerp(a, b, bestT);
            Vector2 offset = p - closest;
            // Right of travel in XZ is (dir.y, -dir.x).
            float lateral = offset.x * dir.y - offset.y * dir.x;

            return new TrackPoint
            {
                Segment = best,
                Distance = cumulative[best] + bestT * (cumulative[best + 1] - cumulative[best]),
                Forward = transform.TransformDirection(new Vector3(dir.x, 0f, dir.y)).normalized,
                Lateral = lateral,
                HalfWidth = 0.5f * Mathf.Lerp(widths[best], widths[(best + 1) % n], bestT),
            };
        }

        /// <summary>World position on the centerline at <paramref name="distance"/> metres from the start.</summary>
        public Vector3 PositionAt(float distance)
        {
            if (!IsReady) return transform.position;
            distance = Mathf.Repeat(distance, lapLength);
            int lo = 0, hi = points.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (cumulative[mid] <= distance) lo = mid; else hi = mid - 1;
            }
            float seg = cumulative[lo + 1] - cumulative[lo];
            float t = seg > 0f ? (distance - cumulative[lo]) / seg : 0f;
            Vector2 q = Vector2.Lerp(points[lo], points[(lo + 1) % points.Length], t);
            return transform.TransformPoint(new Vector3(q.x, startLocal.y, q.y));
        }

        private void TrySegment(int i, Vector2 p, ref int best, ref float bestDistSq, ref float bestT)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length];
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
            float d = (a + ab * t - p).sqrMagnitude;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                best = i;
                bestT = t;
            }
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (!IsReady && trackData != null && !Application.isPlaying) Load();
            if (!IsReady) return;

            Gizmos.color = new Color(1f, 0.78f, 0.17f);
            float y = startLocal.y + 0.3f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                Gizmos.DrawLine(transform.TransformPoint(new Vector3(a.x, y, a.y)), transform.TransformPoint(new Vector3(b.x, y, b.y)));
            }

            // Start line, and an arrow showing race direction.
            Gizmos.color = Color.white;
            Vector3 s = StartPosition + Vector3.up * 0.3f;
            Vector3 f = StartForward;
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Gizmos.DrawLine(s - r * 4.5f, s + r * 4.5f);
            Gizmos.DrawLine(s, s + f * 6f);
            Gizmos.DrawLine(s + f * 6f, s + f * 4.5f + r * 1.2f);
            Gizmos.DrawLine(s + f * 6f, s + f * 4.5f - r * 1.2f);
        }
    }
}
