using CommodoreCircuit.Interactables;
using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// The boost canister on the handlebar stem: a glowing fill that shows the drift
    /// charge where a VR rider can see it, instead of sparks behind the scooter.
    ///
    ///   - While drifting it fills from the bottom, reaching the tick marks at Mini and
    ///     Super and the top at Ultra, in that level's color (blue, orange, purple).
    ///   - Each new level gives a quick flash and bulge, so it registers at a glance.
    ///   - When the boost fires, the fill drains as the boost runs out.
    ///   - A Speed Cookie fills it to the top in green, draining over the cookie's boost.
    ///     Charging a new drift takes over the display; otherwise the newest boost shows.
    ///
    /// Built by CommodoreCircuit > Add Boost Canister to Selected Scooter. Move or scale
    /// the "Boost Canister" object freely to put it somewhere else on the scooter.
    /// </summary>
    public class ScooterBoostGauge : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;
        [Tooltip("Scaled from 0 (empty) to 1 (full) along its Y axis. Its child is the glowing fill.")]
        [SerializeField] private Transform fillPivot;
        [SerializeField] private Renderer fillRenderer;

        [Header("Colors")]
        [SerializeField] private Color chargingColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private Color miniColor = new Color(0.3f, 0.65f, 1f);
        [SerializeField] private Color superColor = new Color(1f, 0.55f, 0.1f);
        [SerializeField] private Color ultraColor = new Color(0.78f, 0.35f, 1f);
        [SerializeField] private Color cookieColor = new Color(0.68f, 0.92f, 0f);   // Lime Green, like a new best lap

        [Header("Feel")]
        [Tooltip("How quickly the fill catches up with the charge. Higher is snappier.")]
        [SerializeField] private float followSpeed = 14f;
        [Tooltip("Seconds of flash and bulge when a new level is reached.")]
        [SerializeField] private float levelUpPulse = 0.25f;
        [Tooltip("How much wider the fill bulges on a level up.")]
        [SerializeField] private float pulseBulge = 0.25f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock block;
        private float shown;
        private float pulseUntil = -1f;
        private ScooterController.BoostTier lastTier = ScooterController.BoostTier.None;
        private ScooterController.BoostTier boostTier = ScooterController.BoostTier.None;
        private RacerEffects effects;
        private float driftBoostStart = -1f;
        private float cookieStart = -1f, cookieDuration;

        private void Awake()
        {
            if (scooter == null) scooter = GetComponentInParent<ScooterController>();
            block = new MaterialPropertyBlock();
            if (scooter != null) scooter.BoostStarted += OnBoostStarted;
        }

        // RacerEffects is added by ScooterController's Awake, so look for it once everything is awake.
        private void Start()
        {
            effects = GetComponentInParent<RacerEffects>();
            if (effects != null) effects.onBoost.AddListener(OnCookie);
        }

        private void OnDestroy()
        {
            if (scooter != null) scooter.BoostStarted -= OnBoostStarted;
            if (effects != null) effects.onBoost.RemoveListener(OnCookie);
        }

        private void OnBoostStarted(ScooterController.BoostTier tier)
        {
            boostTier = tier;
            driftBoostStart = Time.time;
            pulseUntil = Time.time + levelUpPulse;
        }

        private void OnCookie(float duration)
        {
            cookieStart = Time.time;
            cookieDuration = Mathf.Max(0.01f, duration);
            pulseUntil = Time.time + levelUpPulse;
        }

        private void LateUpdate()
        {
            if (scooter == null || fillPivot == null) return;

            // Charging shows the charge; boosting shows what's left of the newest boost.
            // A hazard hit cancels a cookie boost, and RacerEffects stops boosting then too.
            ScooterController.BoostTier tier = scooter.ChargeTier;
            bool cookie = effects != null && effects.IsBoosting && cookieStart >= 0f;
            bool driftBoost = scooter.IsBoosting;
            float target;
            Color color;
            if (scooter.DriftCharge01 <= 0f && cookie && (!driftBoost || cookieStart > driftBoostStart))
            {
                target = Mathf.Clamp01(1f - (Time.time - cookieStart) / cookieDuration);
                color = cookieColor;
            }
            else if (driftBoost && scooter.DriftCharge01 <= 0f)
            {
                target = scooter.BoostRemaining01 * FullFor(boostTier);
                color = ColorFor(boostTier);
            }
            else
            {
                target = scooter.DriftCharge01;
                color = ColorFor(tier);
                if (tier > lastTier) pulseUntil = Time.time + levelUpPulse;
            }
            lastTier = tier;

            shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            if (target <= 0f && shown < 0.005f) shown = 0f;

            // Flash toward white and bulge a little right after a level up or a boost.
            float pulse = Time.time < pulseUntil ? (pulseUntil - Time.time) / Mathf.Max(0.01f, levelUpPulse) : 0f;
            float width = 1f + pulseBulge * pulse;
            fillPivot.localScale = new Vector3(width, Mathf.Max(0.0001f, shown), width);

            if (fillRenderer != null)
            {
                fillRenderer.enabled = shown > 0.005f;
                Color c = Color.Lerp(color, Color.white, 0.6f * pulse);
                fillRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                fillRenderer.SetPropertyBlock(block);
            }
        }

        // A boost drains from the level it was charged to.
        private float FullFor(ScooterController.BoostTier tier)
        {
            switch (tier)
            {
                case ScooterController.BoostTier.Ultra: return 1f;
                case ScooterController.BoostTier.Super: return scooter.SuperCharge01;
                case ScooterController.BoostTier.Mini: return scooter.MiniCharge01;
                default: return 0f;
            }
        }

        private Color ColorFor(ScooterController.BoostTier tier)
        {
            switch (tier)
            {
                case ScooterController.BoostTier.Mini: return miniColor;
                case ScooterController.BoostTier.Super: return superColor;
                case ScooterController.BoostTier.Ultra: return ultraColor;
                default: return chargingColor;
            }
        }
    }
}
