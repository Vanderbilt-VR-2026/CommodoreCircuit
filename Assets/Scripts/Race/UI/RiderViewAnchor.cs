using UnityEngine;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>
    /// Keeps a world-space canvas in front of the rider: ahead along the SCOOTER's
    /// heading (so it doesn't swim when you look around), at a height relative to the
    /// rider's EYES (so it suits tall and short players), and turned to face them.
    /// Head-locked UI makes people sick in VR; this rides with the scooter instead.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the scooter and XR rig have moved this frame
    public class RiderViewAnchor : MonoBehaviour
    {
        [Tooltip("The scooter. Its heading decides 'forward'. If empty, the camera's heading is used.")]
        public Transform vehicle;
        [Tooltip("Metres ahead of the eyes.")]
        public float distance = 3f;
        [Tooltip("Metres above (+) or below (-) eye level.")]
        public float height = 0f;
        [Tooltip("Metres to the right (+) or left (-).")]
        public float sideways = 0f;
        [Tooltip("Turn to face the eyes. Off keeps the panel upright and square to the scooter.")]
        public bool faceRider = true;

        private Camera cachedCamera;

        private void LateUpdate()
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled) cachedCamera = Camera.main;
            if (cachedCamera == null) return;

            Transform eye = cachedCamera.transform;
            Vector3 forward = Flat(vehicle ? vehicle.forward : eye.forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 pos = eye.position + forward * distance + Vector3.up * height + right * sideways;
            transform.position = pos;

            // A UI canvas reads correctly when its +Z points away from the viewer.
            Vector3 look = faceRider ? pos - eye.position : forward;
            if (look.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
