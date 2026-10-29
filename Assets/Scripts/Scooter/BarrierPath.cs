using System.Collections.Generic;
using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// A line of track barriers you draw in the Scene view (shown as a red line).
    ///
    /// Each segment becomes:
    ///   - a VISIBLE low barrier (Visible Height tall, e.g. a concrete jersey barrier), and
    ///   - an INVISIBLE wall stacked on top, up to Total Height, so nothing gets over it.
    /// Both are thick box colliders tagged as TrackWall, so the scooter bounces off them.
    ///
    /// Create one with CommodoreCircuit > New Barrier Path, then click in the
    /// Scene view to add points and press Build Barriers in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class BarrierPath : MonoBehaviour
    {
        [Tooltip("Points of the red line, in this object's local space. Edit them in the Scene view.")]
        public List<Vector3> points = new List<Vector3>();

        [Tooltip("Join the last point back to the first.")]
        public bool closedLoop;

        [Header("Barrier Shape")]
        [Tooltip("Height of the part players can see, in meters.")]
        public float visibleHeight = 0.9f;
        [Tooltip("Height of the whole wall including the invisible upper part, in meters.")]
        public float totalHeight = 3f;
        [Tooltip("Thickness of the walls, in meters. Thick walls are much harder to slip through.")]
        public float thickness = 0.8f;
        [Tooltip("Thickness of the visible barrier mesh only. The collider always uses Thickness.")]
        public float visibleThickness = 0.5f;
        [Tooltip("Material for the visible barrier. Leave empty to use the default concrete barrier material.")]
        public Material visibleMaterial;
        [Tooltip("Show the visible half. Turn off for a fully invisible wall.")]
        public bool showVisibleBarrier = true;

        public const string BuiltChildName = "__Built Barriers";

        /// <summary>Point i in world space.</summary>
        public Vector3 WorldPoint(int i) => transform.TransformPoint(points[i]);

        public int SegmentCount => points.Count < 2 ? 0 : (closedLoop ? points.Count : points.Count - 1);

        private void OnDrawGizmos()
        {
            DrawLine(new Color(1f, 0.1f, 0.1f, 0.9f));
        }

        private void OnDrawGizmosSelected()
        {
            DrawLine(Color.red);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
            for (int i = 0; i < SegmentCount; i++)
            {
                Vector3 a = WorldPoint(i);
                Vector3 b = WorldPoint((i + 1) % points.Count);
                Vector3 mid = (a + b) * 0.5f + Vector3.up * totalHeight * 0.5f;
                Vector3 dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) continue;
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(mid, Quaternion.LookRotation(dir.normalized, Vector3.up), Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(thickness, totalHeight, dir.magnitude));
                Gizmos.matrix = old;
            }
        }

        private void DrawLine(Color color)
        {
            if (points == null || points.Count == 0) return;
            Gizmos.color = color;
            Vector3 lift = Vector3.up * 0.15f;
            for (int i = 0; i < SegmentCount; i++)
            {
                Vector3 a = WorldPoint(i) + lift;
                Vector3 b = WorldPoint((i + 1) % points.Count) + lift;
                // Draw a few parallel lines so it reads as a thick red stroke.
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 o = new Vector3(k * 0.08f, 0f, k * 0.08f);
                    Gizmos.DrawLine(a + o, b + o);
                }
            }
            for (int i = 0; i < points.Count; i++)
                Gizmos.DrawSphere(WorldPoint(i) + lift, 0.35f);
        }
    }
}
