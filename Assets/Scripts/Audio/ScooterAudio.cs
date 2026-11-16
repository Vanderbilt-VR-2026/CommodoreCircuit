using CommodoreCircuit.Scooter;
using UnityEngine;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// Sounds for the player's own scooter. Put it on the scooter next to
    /// ScooterController. It only listens to the scooter and never touches movement.
    ///
    ///   - Wall bump: louder the faster the scooter was going into the wall.
    ///   - Drift, like Mario Kart: sparks crackle at a steady level the whole
    ///     time drift is held, and a ting plays each time the charge reaches the
    ///     next level (Mini, Super, Ultra).
    ///   - Boost: a whoosh when the drift is released that lasts as long as the
    ///     boost does, so an Ultra whoosh is longer than a Mini one.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ScooterAudio : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;

        [Header("Wall bump")]
        [SerializeField] private AudioClip wallBumpClip;
        [Tooltip("Hits slower than this (m/s into the wall) make no sound.")]
        [SerializeField] private float minImpactSpeed = 0.5f;
        [Tooltip("Hits at this speed or faster play at full volume.")]
        [SerializeField] private float fullVolumeImpactSpeed = 8f;
        [SerializeField, Range(0f, 1f)] private float minVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;
        [Tooltip("Ignore another hit this soon after the last one, so one crash doesn't stutter.")]
        [SerializeField] private float cooldown = 0.2f;

        [Header("Drift charge")]
        [Tooltip("Plays each time the charge reaches the next level.")]
        [SerializeField] private AudioClip chargeTingClip;
        [SerializeField, Range(0f, 1f)] private float tingVolume = 0.7f;
        [Tooltip("Ting pitch at Mini, Super and Ultra.")]
        [SerializeField] private Vector3 tingPitchPerTier = new Vector3(1f, 1.19f, 1.41f);

        [Tooltip("Optional: plays when a drift is broken by steering the other way (a fizzle or pop works well).")]
        [SerializeField] private AudioClip driftBreakClip;
        [SerializeField, Range(0f, 1f)] private float driftBreakVolume = 0.6f;

        [Header("Drift sparks")]
        [Tooltip("Crackles the whole time drift is held. Leave empty for no sparks.")]
        [SerializeField] private AudioClip sparkClip;
        [SerializeField, Range(0f, 1f)] private float sparkVolume = 0.25f;
        [Tooltip("A new copy of the spark clip starts after this fraction of it has played, so copies overlap and the crackle never drops out. Lower is denser.")]
        [SerializeField, Range(0.2f, 1f)] private float sparkOverlap = 0.5f;
        [Tooltip("Seconds to fade the sparks in and out.")]
        [SerializeField] private float sparkFade = 0.12f;

        [Header("Boost")]
        [SerializeField] private AudioClip boostClip;
        [Tooltip("Boost volume at Mini, Super and Ultra.")]
        [SerializeField] private Vector3 boostVolumePerTier = new Vector3(0.6f, 0.8f, 1f);
        [Tooltip("Boost pitch at Mini, Super and Ultra.")]
        [SerializeField] private Vector3 boostPitchPerTier = new Vector3(1.1f, 1f, 0.9f);
        [Tooltip("When the boost ends, the whoosh fades out over this many seconds. A whoosh shorter than the boost just ends on its own.")]
        [SerializeField] private float boostFadeOut = 0.25f;

        private AudioSource bumpSource;
        private AudioSource tingSource;
        private AudioSource boostSource;
        private AudioSource sparkSource;
        private float lastBumpTime = -1f;
        private float nextSparkTime;
        private bool boostSounding;
        private ScooterController.BoostTier heardTier = ScooterController.BoostTier.None;

        private void Awake()
        {
            if (scooter == null)
                scooter = GetComponentInParent<ScooterController>();

            // Each sound gets its own source, because pitch and volume are per source:
            // a ting still ringing would bend if the boost changed a shared source.
            bumpSource = SetUp(GetComponent<AudioSource>());
            tingSource = SetUp(gameObject.AddComponent<AudioSource>());
            boostSource = SetUp(gameObject.AddComponent<AudioSource>());
            sparkSource = SetUp(gameObject.AddComponent<AudioSource>());
            sparkSource.volume = 0f;
        }

        private static AudioSource SetUp(AudioSource source)
        {
            GameAudio.Route(source, GameAudio.Channel.Sfx);
            source.playOnAwake = false;
            source.spatialBlend = 0f; // only the rider hears their own scooter
            return source;
        }

        private void OnEnable()
        {
            if (scooter == null) return;
            scooter.WallHit += HandleWallHit;
            scooter.BoostStarted += HandleBoostStarted;
            scooter.DriftBroken += HandleDriftBroken;
        }

        private void OnDisable()
        {
            if (scooter == null) return;
            scooter.WallHit -= HandleWallHit;
            scooter.BoostStarted -= HandleBoostStarted;
            scooter.DriftBroken -= HandleDriftBroken;
            sparkSource.Stop();
            sparkSource.volume = 0f;
            boostSource.Stop();
            boostSounding = false;
            heardTier = ScooterController.BoostTier.None;
        }

        private void Update()
        {
            if (scooter == null) return;

            var tier = scooter.ChargeTier;
            if (tier > heardTier)
                PlayOneShot(tingSource, chargeTingClip, tingVolume, ForTier(tingPitchPerTier, tier));
            heardTier = tier;

            UpdateSparks();
            UpdateBoostFade();
        }

        // The spark clip is short and doesn't loop cleanly, so instead of looping it,
        // a new copy starts before the last one ends. The copies overlap into one
        // steady crackle, and the source volume fades the whole thing in and out.
        private void UpdateSparks()
        {
            if (sparkClip == null) return;

            // The pause menu freezes the scooter by disabling it, mid-drift or not.
            bool drifting = scooter.IsDrifting && scooter.enabled;
            float step = sparkFade > 0f ? Time.deltaTime / sparkFade : 1f;
            sparkSource.volume = Mathf.MoveTowards(sparkSource.volume, drifting ? sparkVolume : 0f, step);

            if (drifting)
            {
                if (Time.time >= nextSparkTime)
                {
                    sparkSource.PlayOneShot(sparkClip);
                    nextSparkTime = Time.time + sparkClip.length * sparkOverlap;
                }
            }
            else if (sparkSource.volume <= 0f && nextSparkTime > 0f)
            {
                // Faded out: cut any copies still playing so the next drift starts clean.
                sparkSource.Stop();
                nextSparkTime = 0f;
            }
        }

        private void HandleBoostStarted(ScooterController.BoostTier tier)
        {
            if (boostClip == null) return;
            boostSource.Stop();
            boostSource.volume = 1f;
            boostSource.pitch = ForTier(boostPitchPerTier, tier);
            boostSource.PlayOneShot(boostClip, ForTier(boostVolumePerTier, tier));
            boostSounding = true;
        }

        // The whoosh holds while the boost runs and fades out once it ends, so its
        // length follows the boost level (and a wall hit that cancels the boost cuts it short).
        private void UpdateBoostFade()
        {
            if (!boostSounding || scooter.IsBoosting) return;

            float step = boostFadeOut > 0f ? Time.deltaTime / boostFadeOut : 1f;
            boostSource.volume = Mathf.MoveTowards(boostSource.volume, 0f, step);
            if (boostSource.volume <= 0f)
            {
                boostSource.Stop();
                boostSounding = false;
            }
        }

        private void HandleDriftBroken()
        {
            PlayOneShot(tingSource, driftBreakClip, driftBreakVolume, 1f);
        }

        private void HandleWallHit(float impactSpeed)
        {
            if (impactSpeed < minImpactSpeed) return;
            if (Time.time - lastBumpTime < cooldown) return;
            lastBumpTime = Time.time;

            float t = Mathf.InverseLerp(minImpactSpeed, fullVolumeImpactSpeed, impactSpeed);
            PlayOneShot(bumpSource, wallBumpClip, Mathf.Lerp(minVolume, maxVolume, t), Random.Range(0.95f, 1.05f));
        }

        private static void PlayOneShot(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

        // x = Mini, y = Super, z = Ultra. None uses Mini's value.
        private static float ForTier(Vector3 perTier, ScooterController.BoostTier tier)
        {
            switch (tier)
            {
                case ScooterController.BoostTier.Ultra: return perTier.z;
                case ScooterController.BoostTier.Super: return perTier.y;
                default: return perTier.x;
            }
        }
    }
}
