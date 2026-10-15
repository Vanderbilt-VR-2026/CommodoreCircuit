using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Drives the particle effects at the back of the scooter. It only reads
    /// ScooterController state and never touches movement.
    ///
    ///   - Drift sparks stream off the rear wheel while drifting and change
    ///     color as the boost meter reaches Mini (blue), Super (orange) and
    ///     Ultra (purple).
    ///   - Boost flame fires from the exhaust when a drift is released with
    ///     enough charge, with a short burst at the start, and keeps
    ///     streaming for as long as the boost lasts.
    ///
    /// The ParticleSystems themselves (shape, size, lifetime) are built by
    /// CommodoreCircuit > Add Boost Effects to Selected Scooter and can be
    /// tweaked freely in the Inspector. This script only turns emission on
    /// and off and picks the color.
    /// </summary>
    public class ScooterBoostEffects : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;
        [SerializeField] private ParticleSystem driftSparks;
        [SerializeField] private ParticleSystem boostFlame;

        [Header("Drift sparks")]
        [SerializeField] private float sparkRate = 60f;
        [Tooltip("Extra spark density per charge level, as a fraction of the base rate.")]
        [SerializeField] private float sparkRateGainPerTier = 0.5f;

        [Header("Boost flame")]
        [SerializeField] private float flameRate = 90f;
        [Tooltip("Particles fired all at once when the boost starts.")]
        [SerializeField] private int flameBurstCount = 30;

        [Header("Colors")]
        [SerializeField] private Color noChargeColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private Color miniColor = new Color(0.3f, 0.65f, 1f);
        [SerializeField] private Color superColor = new Color(1f, 0.55f, 0.1f);
        [SerializeField] private Color ultraColor = new Color(0.78f, 0.35f, 1f);

        private ScooterController.BoostTier shownSparkTier = (ScooterController.BoostTier)(-1);
        private ScooterController.BoostTier shownFlameTier = (ScooterController.BoostTier)(-1);

        private void Awake()
        {
            if (scooter == null)
                scooter = GetComponentInParent<ScooterController>();

            StartSystem(driftSparks);
            StartSystem(boostFlame);
        }

        private void OnEnable()
        {
            if (scooter != null)
                scooter.BoostStarted += HandleBoostStarted;
        }

        private void OnDisable()
        {
            if (scooter != null)
                scooter.BoostStarted -= HandleBoostStarted;
        }

        private void Update()
        {
            if (scooter == null)
                return;

            UpdateSparks();
            UpdateFlame();
        }

        private void UpdateSparks()
        {
            if (driftSparks == null)
                return;

            ScooterController.BoostTier tier = scooter.ChargeTier;
            float rate = scooter.IsDrifting
                ? sparkRate * (1f + sparkRateGainPerTier * (int)tier)
                : 0f;
            SetRate(driftSparks, rate);

            if (tier != shownSparkTier)
            {
                SetStartColor(driftSparks, ColorFor(tier));
                shownSparkTier = tier;
            }
        }

        private void UpdateFlame()
        {
            if (boostFlame == null)
                return;

            bool boosting = scooter.IsBoosting;
            SetRate(boostFlame, boosting ? flameRate : 0f);

            ScooterController.BoostTier tier = scooter.ActiveBoostTier;
            if (boosting && tier != shownFlameTier)
            {
                SetStartColor(boostFlame, ColorFor(tier));
                shownFlameTier = tier;
            }
        }

        private void HandleBoostStarted(ScooterController.BoostTier tier)
        {
            if (boostFlame == null)
                return;

            SetStartColor(boostFlame, ColorFor(tier));
            shownFlameTier = tier;
            boostFlame.Emit(flameBurstCount);
        }

        private Color ColorFor(ScooterController.BoostTier tier)
        {
            switch (tier)
            {
                case ScooterController.BoostTier.Mini: return miniColor;
                case ScooterController.BoostTier.Super: return superColor;
                case ScooterController.BoostTier.Ultra: return ultraColor;
                default: return noChargeColor;
            }
        }

        private static void StartSystem(ParticleSystem ps)
        {
            if (ps == null)
                return;

            SetRate(ps, 0f);
            if (!ps.isPlaying)
                ps.Play();
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private static void SetStartColor(ParticleSystem ps, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = color;
        }
    }
}
