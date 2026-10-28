using UnityEngine;
using UnityEngine.Events;

namespace CommodoreCircuit.Interactables
{
    public enum PowerUpType { SpeedBoost, Shield }

    /// <summary>
    /// Collected on contact, hides, then respawns. Needs a trigger collider and a
    /// kinematic Rigidbody (set up by the prefab builder).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PowerUp : MonoBehaviour
    {
        public PowerUpType type = PowerUpType.SpeedBoost;

        [Header("Speed cookie")]
        public float boostMultiplier = 1.4f;
        public float boostDuration = 2f;

        [Header("Meal swipe")]
        public float shieldDuration = 8f;

        [Header("Respawn")]
        [Tooltip("Seconds until it reappears. 0 or less = never.")]
        public float respawnSeconds = 8f;

        [Header("Feedback")]
        [Tooltip("Hidden while collected. Defaults to the first child.")]
        public GameObject visuals;
        public AudioSource pickupAudio;
        public ParticleSystem pickupBurst;
        public UnityEvent onCollected = new UnityEvent();

        Collider[] colliders;
        bool available = true;

        void Awake()
        {
            colliders = GetComponentsInChildren<Collider>();
            if (visuals == null && transform.childCount > 0) visuals = transform.GetChild(0).gameObject;
        }

        void Reset()
        {
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!available) return;
            var racer = other.GetComponentInParent<IRacerEffects>();
            if (racer == null) return;

            if (type == PowerUpType.SpeedBoost) racer.ApplyBoost(boostMultiplier, boostDuration);
            else racer.GrantShield(shieldDuration);

            SetAvailable(false);
            if (pickupAudio != null) pickupAudio.Play();
            if (pickupBurst != null) pickupBurst.Play();
            onCollected.Invoke();
            if (respawnSeconds > 0f) Invoke(nameof(Respawn), respawnSeconds);
        }

        void Respawn() => SetAvailable(true);

        void SetAvailable(bool value)
        {
            available = value;
            foreach (var c in colliders) c.enabled = value;
            if (visuals != null) visuals.SetActive(value);
        }
    }
}
