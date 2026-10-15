using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Put this on a ground collider to change how the scooter drives over it.
    /// The scooter reads it from the surface it is standing on. The campus
    /// track puts it on the grass bands beside the Alumni Lawn path.
    /// </summary>
    public class TrackSurface : MonoBehaviour
    {
        [Tooltip("Top speed on this surface as a fraction of max speed. 1 = normal, 0.45 = grass.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float speedMultiplier = 0.45f;

        [Tooltip("How fast the scooter sheds speed (m/s per second) when it is above this surface's top speed.")]
        [SerializeField] private float dragDeceleration = 14f;

        [Tooltip("Scales acceleration while on this surface. 1 = normal.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float accelerationMultiplier = 0.6f;

        public float SpeedMultiplier => speedMultiplier;
        public float DragDeceleration => dragDeceleration;
        public float AccelerationMultiplier => accelerationMultiplier;
    }
}
