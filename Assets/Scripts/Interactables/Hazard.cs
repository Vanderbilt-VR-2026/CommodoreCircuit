using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Costs a racer time on contact. Needs a trigger collider and (because hazards
    /// move) a kinematic Rigidbody on the same GameObject; the prefab builder sets
    /// both up. Racers are found via <see cref="IRacerEffects"/> on the other
    /// collider or any of its parents.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Hazard : MonoBehaviour
    {
        [Header("Penalty")]
        [Tooltip("Seconds the racer stays slowed.")]
        public float slowDuration = 1f;
        [Tooltip("Speed multiplier while slowed (0.4 = 40% speed).")]
        [Range(0.05f, 1f)] public float slowMultiplier = 0.4f;
        [Tooltip("Horizontal knockback speed in m/s (0 = none). Only used if the racer has a Rigidbody.")]
        public float knockback = 0f;
        [Tooltip("The same racer can't be hit again by this hazard for this long.")]
        public float rehitCooldown = 1.5f;

        [Header("Feedback")]
        public AudioSource hitAudio;
        public UnityEvent onHit = new UnityEvent();

        /// <summary>Raised when a hit lands (not when a shield blocks it). Arg = racer transform.</summary>
        public event Action<Transform> Hit;

        readonly Dictionary<IRacerEffects, float> lastHitTime = new Dictionary<IRacerEffects, float>();

        void Reset()
        {
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var racer = other.GetComponentInParent<IRacerEffects>();
            if (racer == null) return;

            if (lastHitTime.TryGetValue(racer, out float t) && Time.time - t < rehitCooldown) return;
            lastHitTime[racer] = Time.time;

            Vector3 away = other.transform.position - transform.position;
            away.y = 0f;
            Vector3 push = away.sqrMagnitude > 1e-4f ? away.normalized * knockback : Vector3.zero;
            if (knockback > 0f) push += Vector3.up * (knockback * 0.25f);

            bool landed = racer.ApplyHazard(new HazardHit
            {
                slowDuration = slowDuration,
                slowMultiplier = slowMultiplier,
                knockback = push,
                source = this,
            });
            if (!landed) return;

            if (hitAudio != null) hitAudio.Play();
            onHit.Invoke();
            Hit?.Invoke(other.transform);
        }
    }
}
