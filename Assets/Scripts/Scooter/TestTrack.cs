using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Builds a simple oval the scooter can lap while testing drift.
    /// The corners are wider than a full-lock drift at top speed, and the
    /// walls are only high enough to keep the scooter on the asphalt.
    /// </summary>
    public class TestTrack : MonoBehaviour
    {
        [SerializeField] private float straightLength = 40f;
        [SerializeField] private float cornerRadius = 16f;
        [SerializeField] private float trackWidth = 10f;
        [SerializeField] private float surfaceThickness = 0.3f;
        [SerializeField] private float wallHeight = 1f;
        [SerializeField] private float wallThickness = 0.35f;
        [SerializeField] private int cornerSegments = 12;
        [SerializeField] private bool placeScooterAtStart = true;

        private void Awake()
        {
            ClearGeneratedPieces();
            Build();
            LowerExistingPlane();
            PlaceScooter();
        }

        private void ClearGeneratedPieces()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        private void Build()
        {
            float halfStraight = straightLength * 0.5f;
            Vector3 leftCenter = new Vector3(-halfStraight, 0f, 0f);
            Vector3 rightCenter = new Vector3(halfStraight, 0f, 0f);

            Vector3 bottomLeft = PointOnCorner(leftCenter, -90f);
            Vector3 bottomRight = PointOnCorner(rightCenter, -90f);
            Vector3 topLeft = PointOnCorner(leftCenter, 90f);
            Vector3 topRight = PointOnCorner(rightCenter, 90f);

            BuildStraight(bottomLeft, bottomRight, isStartStraight: true);
            BuildStraight(topRight, topLeft, isStartStraight: false);
            BuildCorner(rightCenter, -90f, 90f);
            BuildCorner(leftCenter, 90f, 270f);
        }

        private void BuildStraight(Vector3 from, Vector3 to, bool isStartStraight)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            Vector3 mid = (from + to) * 0.5f;
            Color road = isStartStraight
                ? new Color(0.28f, 0.28f, 0.3f)
                : new Color(0.16f, 0.16f, 0.16f);

            AddBox("Road", mid, rotation, new Vector3(trackWidth, surfaceThickness, length + 1f), road, -surfaceThickness * 0.5f);
            AddWalls(mid, rotation, length + 1f);

            if (!isStartStraight)
                return;

            GameObject startLine = AddBox(
                "Start Line",
                mid,
                rotation,
                new Vector3(trackWidth * 0.85f, 0.02f, 0.4f),
                new Color(0.95f, 0.8f, 0.15f),
                0.02f);
            Destroy(startLine.GetComponent<Collider>());
        }

        private void BuildCorner(Vector3 center, float startAngle, float endAngle)
        {
            float step = (endAngle - startAngle) / cornerSegments;
            float chord = 2f * cornerRadius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);

            for (int i = 0; i < cornerSegments; i++)
            {
                float angle = startAngle + step * (i + 0.5f);
                Vector3 point = PointOnCorner(center, angle);
                Vector3 tangent = new Vector3(-Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
                Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);

                AddBox("Road", point, rotation, new Vector3(trackWidth, surfaceThickness, chord * 1.25f), new Color(0.16f, 0.16f, 0.16f), -surfaceThickness * 0.5f);
                AddWalls(point, rotation, chord * 1.25f);
            }
        }

        private void AddWalls(Vector3 center, Quaternion rotation, float length)
        {
            Vector3 right = rotation * Vector3.right;
            float offset = trackWidth * 0.5f + wallThickness * 0.5f;
            Vector3 size = new Vector3(wallThickness, wallHeight, length);

            AddWall(center + right * offset, rotation, size, new Color(0.75f, 0.15f, 0.12f));
            AddWall(center - right * offset, rotation, size, new Color(0.85f, 0.85f, 0.85f));
        }

        private Vector3 PointOnCorner(Vector3 center, float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * cornerRadius;
        }

        private void AddWall(Vector3 center, Quaternion rotation, Vector3 size, Color color)
        {
            GameObject wall = AddBox("Wall", center, rotation, size, color, wallHeight * 0.5f);
            wall.AddComponent<TrackWall>();
        }

        private GameObject AddBox(string label, Vector3 center, Quaternion rotation, Vector3 size, Color color, float centerY)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = label;
            piece.transform.SetParent(transform, false);
            piece.transform.SetPositionAndRotation(center + Vector3.up * centerY, rotation);
            piece.transform.localScale = size;

            Renderer renderer = piece.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(color);
            return piece;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            return material;
        }

        private void LowerExistingPlane()
        {
            GameObject plane = GameObject.Find("Plane");
            if (plane == null)
                return;

            Vector3 position = plane.transform.position;
            position.y = -2f;
            plane.transform.position = position;
        }

        private void PlaceScooter()
        {
            if (!placeScooterAtStart)
                return;

            ScooterController scooter = FindAnyObjectByType<ScooterController>();
            if (scooter == null)
                return;

            Vector3 start = new Vector3(0f, 0.05f, -cornerRadius);
            Quaternion facing = Quaternion.Euler(0f, 90f, 0f);

            Rigidbody body = scooter.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = start;
                body.rotation = facing;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            scooter.transform.SetPositionAndRotation(start, facing);
        }
    }
}
