using CommodoreCircuit.Interactables;
using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Kart-style spin-out: when a hazard hit lands (squirrel, freshman, pipe, frisbee),
    /// the scooter model spins a full turn under the rider. Wall bumps don't trigger it,
    /// and neither does a hit the Commodore Card shield blocks.
    ///
    /// Only the model spins. The camera, the hands and the HUD stay put, because spinning
    /// the player's view on its own is a quick way to make someone sick in VR. Movement
    /// isn't touched either: the scooter keeps its heading and the hazard's slowdown does
    /// the rest.
    ///
    /// Put it on the Scooter, next to ScooterController. Untick it to compare without,
    /// or remove it to go back to how it was.
    /// </summary>
    public class ScooterSpinOut : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;
        [Tooltip("How long the spin takes, in seconds.")]
        [SerializeField, Min(0.1f)] private float spinSeconds = 0.8f;
        [Tooltip("Full turns per spin.")]
        [SerializeField, Min(1)] private int turns = 1;

        private RacerEffects effects;
        private Transform pivot;
        private float spinStart = -1f;
        private float direction = 1f;

        private void Start()
        {
            if (scooter == null) scooter = GetComponent<ScooterController>();
            // ScooterController adds RacerEffects in its Awake, so it exists by now.
            effects = GetComponentInParent<RacerEffects>();
            if (effects != null) effects.onHit.AddListener(OnHit);
        }

        private void OnDestroy()
        {
            if (effects != null) effects.onHit.RemoveListener(OnHit);
        }

        private void OnDisable()
        {
            // Unticked mid-spin: put the model straight again.
            spinStart = -1f;
            if (pivot != null) pivot.localRotation = Quaternion.identity;
        }

        private void OnHit(HazardHit hit)
        {
            if (!isActiveAndEnabled) return;
            EnsurePivot();

            // Spin away from whatever was hit: a hazard on the left spins you clockwise.
            direction = 1f;
            if (hit.source != null)
            {
                Vector3 toHazard = hit.source.transform.position - transform.position;
                direction = Vector3.Cross(transform.forward, toHazard).y > 0f ? -1f : 1f;
            }
            spinStart = Time.time;
        }

        private void LateUpdate()
        {
            if (pivot == null || spinStart < 0f) return;

            float t = (Time.time - spinStart) / spinSeconds;
            if (t >= 1f)
            {
                pivot.localRotation = Quaternion.identity;
                spinStart = -1f;
                return;
            }
            // Fast at first, easing into the last part of the turn.
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            pivot.localRotation = Quaternion.Euler(0f, direction * 360f * turns * eased, 0f);
        }

        // Done at the first hit rather than at start, because the scooter moves its wheels
        // and camera into place during its own setup. Everything visible on the scooter goes
        // under one pivot at its centre; the rider's parts (camera, hands, handlebar guide,
        // HUD) and the wheel colliders stay where they are.
        private void EnsurePivot()
        {
            if (pivot != null) return;
            pivot = new GameObject("Spin Pivot").transform;
            pivot.SetParent(transform, false);

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == pivot || IsRiderPart(child)) continue;
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                child.SetParent(pivot, true);
            }
        }

        // Same rule RemoteRacerVisual uses to tell the rider apart from the scooter.
        private static bool IsRiderPart(Transform t)
        {
            if (t.GetComponentInChildren<Camera>(true) || t.GetComponentInChildren<Canvas>(true)
                || t.GetComponentInChildren<ScooterHandlebar>(true)) return true;
            return t.name == "VRRig" || t.name.EndsWith("HandAnchor");
        }
    }
}
