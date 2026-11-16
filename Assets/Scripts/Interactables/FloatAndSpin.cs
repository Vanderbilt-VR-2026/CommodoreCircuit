using UnityEngine;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Power-up idle motion: bob, spin around world up, optional end-over-end flip,
    /// and an orbiting sparkle ring. Animates <see cref="target"/> (the model child)
    /// so the trigger collider on the root stays put. The spin is added on top of the
    /// model's own rest rotation, so a model stood on its edge (the cookie, the card)
    /// stays on its edge instead of being laid flat.
    /// </summary>
    public class FloatAndSpin : MonoBehaviour
    {
        public Transform target;

        [Header("Bob")]
        public float bobHeight = 0.08f;
        public float bobSpeed = 1.6f;

        [Header("Spin")]
        [Tooltip("Degrees per second around world up.")]
        public float spinSpeed = 90f;
        [Tooltip("Degrees per second end over end (around the model's right axis).")]
        public float flipSpeed = 0f;

        [Header("Sparkles")]
        public Transform sparkles;
        [Tooltip("Degrees per second the sparkle ring orbits around the model's forward axis.")]
        public float sparkleOrbitSpeed = 70f;

        Vector3 restPos;
        Quaternion restRot = Quaternion.identity;
        float phase;
        float yaw, flip;

        void Awake()
        {
            if (target == null && transform.childCount > 0) target = transform.GetChild(0);
            if (target != null)
            {
                restPos = target.localPosition;
                restRot = target.localRotation;
            }
            phase = Random.value * Mathf.PI * 2f;       // de-sync neighbours
            yaw = Random.value * 360f;
        }

        void Update()
        {
            if (target == null) return;
            float t = Time.time * bobSpeed + phase;
            target.localPosition = restPos + Vector3.up * (Mathf.Sin(t) * bobHeight);
            yaw += spinSpeed * Time.deltaTime;
            flip += flipSpeed * Time.deltaTime;
            target.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(flip, 0f, 0f) * restRot;
            if (sparkles != null) sparkles.Rotate(Vector3.forward, sparkleOrbitSpeed * Time.deltaTime, Space.Self);
        }
    }
}
