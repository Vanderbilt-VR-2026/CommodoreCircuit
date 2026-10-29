using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Reference implementation of <see cref="IRacerEffects"/>. Add it to the player's
    /// scooter root. It does NOT move the scooter itself: whoever owns the scooter
    /// controller multiplies their target speed by <see cref="SpeedMultiplier"/>.
    ///
    /// VR comfort: hits use controller haptics plus the onHit event (hook a brief
    /// screen-edge vignette and a sound to it). Never shake the camera.
    /// </summary>
    public class RacerEffects : MonoBehaviour, IRacerEffects
    {
        public static readonly List<RacerEffects> All = new List<RacerEffects>();

        [Header("Knockback (optional)")]
        [Tooltip("If set, hazard knockback is applied to this body as a velocity change.")]
        public Rigidbody body;

        [Header("Haptics")]
        [Range(0f, 1f)] public float hitHapticAmplitude = 0.8f;
        public float hitHapticDuration = 0.25f;
        [Range(0f, 1f)] public float pickupHapticAmplitude = 0.35f;
        public float pickupHapticDuration = 0.08f;

        [Header("Events (hook vignette, audio, UI here)")]
        public UnityEvent<HazardHit> onHit = new UnityEvent<HazardHit>();
        public UnityEvent<float> onBoost = new UnityEvent<float>();
        public UnityEvent onShieldStart = new UnityEvent();
        public UnityEvent onShieldBlocked = new UnityEvent();
        public UnityEvent onShieldEnd = new UnityEvent();

        float slowUntil, slowMultiplier = 1f;
        float boostUntil, boostMultiplier = 1f;
        float shieldUntil;
        bool shieldWasActive;

        static readonly List<InputDevice> Controllers = new List<InputDevice>();

        public bool IsShielded => Time.time < shieldUntil;
        public bool IsSlowed => Time.time < slowUntil;
        public bool IsBoosting => Time.time < boostUntil;

        /// <summary>Multiply the scooter's target speed by this every frame.</summary>
        public float SpeedMultiplier
        {
            get
            {
                float m = 1f;
                if (IsSlowed) m *= slowMultiplier;
                if (IsBoosting) m *= boostMultiplier;
                return m;
            }
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            bool active = IsShielded;
            if (shieldWasActive && !active) onShieldEnd.Invoke();
            shieldWasActive = active;
        }

        public bool ApplyHazard(HazardHit hit)
        {
            if (IsShielded)
            {
                shieldUntil = 0f;               // one-hit shield
                Pulse(pickupHapticAmplitude, pickupHapticDuration);
                onShieldBlocked.Invoke();
                return false;
            }

            slowUntil = Time.time + hit.slowDuration;
            slowMultiplier = Mathf.Clamp01(hit.slowMultiplier);
            boostUntil = 0f;                    // a hit cancels any active boost
            if (body != null && hit.knockback != Vector3.zero && !body.isKinematic)
                body.AddForce(hit.knockback, ForceMode.VelocityChange);

            Pulse(hitHapticAmplitude, hitHapticDuration);
            onHit.Invoke(hit);
            return true;
        }

        public void ApplyBoost(float speedMultiplier, float duration)
        {
            slowUntil = 0f;                     // a boost clears an active slowdown
            boostMultiplier = Mathf.Max(1f, speedMultiplier);
            boostUntil = Time.time + duration;
            Pulse(pickupHapticAmplitude, pickupHapticDuration);
            onBoost.Invoke(duration);
        }

        public void GrantShield(float duration)
        {
            shieldUntil = Time.time + duration;
            Pulse(pickupHapticAmplitude, pickupHapticDuration);
            onShieldStart.Invoke();
        }

        static void Pulse(float amplitude, float duration)
        {
            Controllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, Controllers);
            foreach (var device in Controllers)
            {
                if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    device.SendHapticImpulse(0u, amplitude, duration);
            }
        }
    }
}
