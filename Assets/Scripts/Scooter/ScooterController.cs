using UnityEngine;
using UnityEngine.InputSystem;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Core scooter driving physics.
    ///
    /// Controls - two input paths are wired at once, so this works with just a
    /// keyboard in the Editor (for fast iteration) and later with a headset,
    /// with no code changes needed:
    ///
    ///   Keyboard:
    ///     - W / Up Arrow      -> accelerate
    ///     - S / Down Arrow    -> brake while moving, reverse once stopped
    ///     - Space             -> drift (hold while turning)
    ///     - A/D or Left/Right -> steer
    ///
    ///   VR controllers (read directly via the Input System's generic XR
    ///   device bindings - no XR Interaction Toolkit required):
    ///     - Right trigger    -> accelerate
    ///     - Left trigger     -> brake while moving, reverse once stopped
    ///     - Either hand grip -> drift (first guess - squeeze the handlebar
    ///                           grip to drift; not yet tried in headset, easy
    ///                           to rebind below if it doesn't feel right)
    ///     - Hand position    -> steering, modeled like real handlebars: raise
    ///                           the left hand and pull the right hand back to
    ///                           turn right, and the mirror image to turn left.
    ///                           (Only active once leftHandAnchor/rightHandAnchor
    ///                           are assigned - safe to leave unassigned for
    ///                           keyboard-only testing.)
    ///
    /// Hand-gesture steering is measured relative to a calibrated neutral hand
    /// pose (set on Start, or manually via RecenterSteering()), and is added
    /// on top of the keyboard steer axis, so either input method works alone
    /// or together.
    ///
    /// Drift: hold Drift while turning above a minimum speed and the rear
    /// tires lose grip. The actual curving of the path is driven directly by
    /// ApplyDriftGlideTurn, which rotates the scooter's travel direction at a
    /// constant, steer-proportional rate (DriftGlideTurnRate) instead of
    /// relying on WheelCollider tire forces - a constant rate can't
    /// accelerate or run out, so the scooter glides through a turn of any
    /// length at a steady radius instead of spiraling tighter the longer
    /// Drift is held. On top of that, a held slip-angle controller
    /// (ApplyDriftSlideTorque) actively targets and holds an offset between
    /// where the scooter points and where it's actually traveling
    /// (DriftSlideAngle/HoldStrength/Damping), so the sideways kart-drift
    /// stance persists instead of decaying back to straight.
    ///
    /// While drifting, a mini-turbo charge meter fills (faster if steering
    /// hard into the turn, slower otherwise). Releasing Drift after enough
    /// charge fires a forward speed boost, in three tiers (Mini/Super/Ultra),
    /// same shape as Mario Kart 8's drift-charge system - see the "Drift
    /// Charge / Mini-Turbo Boost" section below for the tunables and
    /// ReleaseDriftBoost() for the tier logic. IsDrifting, IsBoosting and
    /// LastBoostTier are all exposed publicly for VFX/audio hookup later.
    ///
    /// Stability: a two-wheel-in-line vehicle (front + rear, no side-by-side
    /// wheel track) has nothing physically stopping it from tipping sideways
    /// - a real scooter stays up because the rider actively balances it. We
    /// fake that with a spring-damper "gyroscope" torque that constantly
    /// pulls the scooter back upright, tunable in the Stability section.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ScooterController : MonoBehaviour
    {
        [Header("Wheels (physics)")]
        [SerializeField] private WheelCollider frontWheel;
        [SerializeField] private WheelCollider rearWheel;

        [Header("Wheels (visual placeholders)")]
        [SerializeField] private Transform frontWheelVisual;
        [SerializeField] private Transform rearWheelVisual;
        [Tooltip("Extra local rotation applied on top of the WheelCollider's pose, to align the visual mesh's own roll axis with the wheel's roll axis (local X). The placeholder cylinders need 90 degrees on Z; a real wheel model authored with X as its axle will need (0,0,0).")]
        [SerializeField] private Vector3 frontWheelVisualEulerOffset = new Vector3(0f, 0f, 90f);
        [SerializeField] private Vector3 rearWheelVisualEulerOffset = new Vector3(0f, 0f, 90f);

        [Header("VR Hand Anchors (optional - leave empty for keyboard-only testing)")]
        [Tooltip("Transform tracking the player's left controller (world space).")]
        [SerializeField] private Transform leftHandAnchor;
        [Tooltip("Transform tracking the player's right controller (world space).")]
        [SerializeField] private Transform rightHandAnchor;

        [Header("Acceleration")]
        [SerializeField] private float maxMotorTorque = 900f;
        [SerializeField] private float maxSpeed = 14f; // m/s (~50 km/h)

        [Header("Braking / Reverse")]
        [SerializeField] private float maxBrakeTorque = 3500f;
        [Tooltip("Motor torque used to drive backward once the scooter has slowed to a stop.")]
        [SerializeField] private float reverseMotorTorque = 500f;
        [Tooltip("Top reverse speed (m/s) - kept slower than forward top speed.")]
        [SerializeField] private float reverseMaxSpeed = 6f;
        [Tooltip("Below this forward speed (m/s), holding brake reverses instead of braking.")]
        [SerializeField] private float brakeToReverseSpeedThreshold = 0.5f;

        [Header("Drift")]
        [SerializeField, Range(0f, 1f)] private float driftTriggerThreshold = 0.15f;
        [SerializeField] private float minDriftSpeed = 2f;
        [SerializeField, Range(0.05f, 1f)] private float driftSidewaysStiffnessMultiplier = 0.4f;
        [Tooltip("How far sideways (degrees) the scooter's heading aims away from its actual travel direction while drifting. Bigger = more dramatic power-slide. This is the main knob for slide amount now.")]
        [SerializeField] private float driftSlideAngle = 25f;
        [Tooltip("How strongly the drift snaps to and holds the slide angle above. Higher = grabs the slide angle faster and holds it more rigidly instead of letting it decay back to straight.")]
        [SerializeField] private float driftSlideHoldStrength = 25f;
        [Tooltip("Resists oscillation/overshoot while holding the slide. Raise this first if the drift feels wobbly, snappy, or overshoots and wiggles.")]
        [SerializeField] private float driftSlideDamping = 4f;
        [Tooltip("Front wheel steer authority while drifting, as a fraction of normal. Kept low because the real turning now comes from Drift Glide Turn Rate below - this is mostly just how much the front wheel visually turns and adds a little extra bite, not the main driver of the curve anymore.")]
        [SerializeField, Range(0f, 1f)] private float driftSteerAngleMultiplier = 0.25f;
        [Tooltip("The actual turn rate (deg/sec, at full steer) of the scooter's TRAVEL DIRECTION while drifting - this directly drives the glide/curve, instead of relying on WheelCollider tire forces, which is what let the turn keep accelerating the longer Drift was held. Because it's a constant rate, the drift can be held indefinitely without ever needing to cap out or snap straight - it just keeps gliding around the curve at this steady rate. Lower = wider, lazier glide; higher = tighter, snappier turn.")]
        [SerializeField] private float driftGlideTurnRate = 70f;
        [Tooltip("Caps how fast the scooter's HEADING (not its travel direction) can rotate while drifting, so the sideways slide-angle stance doesn't overshoot or wobble - a stabilizer for the visual stance, not the thing steering the car anymore.")]
        [SerializeField] private float driftMaxYawRate = 120f;

        [Header("Drift Charge / Mini-Turbo Boost")]
        [Tooltip("Starting values borrowed from Mario Kart 8's drift-charge ratios (community-documented, not exact game code): steering past this magnitude while drifting counts as the 'optimal' angle and charges faster.")]
        [SerializeField, Range(0f, 1f)] private float driftChargeOptimalSteerThreshold = 0.7f;
        [Tooltip("Charge meter fill rate (seconds of charge per real second) while at the optimal steer angle.")]
        [SerializeField] private float driftChargeRateOptimal = 1f;
        [Tooltip("Charge meter fill rate at a shallow steer angle - MK8's ratio is roughly 2/5 of the optimal rate.")]
        [SerializeField] private float driftChargeRateShallow = 0.4f;
        [Tooltip("Seconds of optimal-rate charge needed for a Mini-Turbo (tier 1, blue sparks in MK8).")]
        [SerializeField] private float miniTurboChargeTime = 0.7f;
        [Tooltip("Seconds of optimal-rate charge needed for a Super Mini-Turbo (tier 2, orange sparks in MK8).")]
        [SerializeField] private float superTurboChargeTime = 1.7f;
        [Tooltip("Seconds of optimal-rate charge needed for an Ultra Mini-Turbo (tier 3, purple sparks in MK8 Deluxe).")]
        [SerializeField] private float ultraTurboChargeTime = 2.6f;
        [Tooltip("Base forward acceleration applied during the boost, before the per-tier multiplier below.")]
        [SerializeField] private float driftBoostForce = 8f;
        [SerializeField] private float miniTurboBoostMultiplier = 1f;
        [SerializeField] private float superTurboBoostMultiplier = 1.5f;
        [SerializeField] private float ultraTurboBoostMultiplier = 1.8f;
        [Tooltip("Hard ceiling (m/s) the boost accelerates toward - the push tapers off as forward speed nears this, the same way normal acceleration tapers toward maxSpeed. Without this the boost force/duration below had nothing capping them, so it could shove the scooter to absurd speeds for the whole duration - this is the actual fix for a boost that felt too strong for too long. Sits a bit above maxSpeed so a boost still feels like a real speed advantage.")]
        [SerializeField] private float maxBoostSpeed = 18f;
        [Tooltip("How long each boost tier's forward push lasts, in seconds. Shortened from MK8's literal durations (~0.6/1.7/2.6s), which read as way too long at this scooter's scale once the boost was also properly speed-capped below.")]
        [SerializeField] private float miniTurboBoostDuration = 0.35f;
        [SerializeField] private float superTurboBoostDuration = 0.8f;
        [SerializeField] private float ultraTurboBoostDuration = 1.3f;

        [Header("Steering (handlebar gesture + keyboard)")]
        [SerializeField] private float maxSteerAngle = 28f;
        [SerializeField] private float steerRange = 0.35f; // meters of hand travel for full steer lock
        [SerializeField] private float steerSmoothSpeed = 8f; // higher = snappier

        [Header("Stability")]
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, 0.05f, 0f);
        [SerializeField] private float downforce = 15f;
        [Tooltip("How hard the scooter self-rights when it leans. Higher = snappier recovery, but too high feels rigid/twitchy.")]
        [SerializeField] private float uprightSpringStrength = 60f;
        [Tooltip("Resists the wobble/oscillation the spring above would otherwise cause. Raise this first if the scooter feels jittery or shakes side to side.")]
        [SerializeField] private float uprightSpringDamping = 8f;

        private Rigidbody rb;
        private InputAction accelerateAction;
        private InputAction brakeAction;
        private InputAction driftAction;
        private InputAction keyboardSteerAction;

        private Vector3 leftHandNeutralLocal;
        private Vector3 rightHandNeutralLocal;
        private bool isCalibrated;

        private WheelFrictionCurve rearSidewaysFrictionNormal;
        private WheelFrictionCurve rearSidewaysFrictionDrift;

        private float currentSteerAngle;

        public enum MiniTurboTier { None, Mini, Super, Ultra }

        /// <summary>True while the rear tires are in the loosened drift state.</summary>
        public bool IsDrifting { get; private set; }
        /// <summary>True while a mini-turbo boost from a released drift is still pushing the scooter forward.</summary>
        public bool IsBoosting => boostTimeRemaining > 0f;
        /// <summary>Tier of the most recently released drift boost (None if the last drift never charged far enough).</summary>
        public MiniTurboTier LastBoostTier { get; private set; } = MiniTurboTier.None;

        private float driftChargeTimer;
        private float boostTimeRemaining;
        private float boostForceCurrent;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = centerOfMassOffset;

            accelerateAction = new InputAction("Accelerate", InputActionType.Value, "<XRController>{RightHand}/trigger");
            accelerateAction.AddBinding("<Keyboard>/w");
            accelerateAction.AddBinding("<Keyboard>/upArrow");

            brakeAction = new InputAction("Brake", InputActionType.Value, "<XRController>{LeftHand}/trigger");
            brakeAction.AddBinding("<Keyboard>/s");
            brakeAction.AddBinding("<Keyboard>/downArrow");

            driftAction = new InputAction("Drift", InputActionType.Value);
            driftAction.AddBinding("<Keyboard>/space");
            driftAction.AddBinding("<XRController>{LeftHand}/grip");
            driftAction.AddBinding("<XRController>{RightHand}/grip");

            keyboardSteerAction = new InputAction("KeyboardSteer", InputActionType.Value);
            keyboardSteerAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/d")
                .With("Positive", "<Keyboard>/rightArrow");

            if (rearWheel != null)
            {
                rearSidewaysFrictionNormal = rearWheel.sidewaysFriction;
                rearSidewaysFrictionDrift = rearSidewaysFrictionNormal;
                rearSidewaysFrictionDrift.stiffness *= driftSidewaysStiffnessMultiplier;
            }
        }

        private void OnEnable()
        {
            accelerateAction.Enable();
            brakeAction.Enable();
            driftAction.Enable();
            keyboardSteerAction.Enable();
        }

        private void OnDisable()
        {
            accelerateAction.Disable();
            brakeAction.Disable();
            driftAction.Disable();
            keyboardSteerAction.Disable();
        }

        private void Start()
        {
            RecenterSteering();
        }

        /// <summary>
        /// Re-zeroes the hand-gesture steering to the player's current hand
        /// pose. Bind this to a button (e.g. press both triggers together) so
        /// players can recalibrate their natural "hands on the handlebar"
        /// position at any time. Irrelevant for keyboard steering.
        /// </summary>
        public void RecenterSteering()
        {
            if (leftHandAnchor == null || rightHandAnchor == null) return;

            leftHandNeutralLocal = transform.InverseTransformPoint(leftHandAnchor.position);
            rightHandNeutralLocal = transform.InverseTransformPoint(rightHandAnchor.position);
            isCalibrated = true;
        }

        private void FixedUpdate()
        {
            float accelInput = accelerateAction.ReadValue<float>();
            float brakeInput = brakeAction.ReadValue<float>();
            float driftInput = driftAction.ReadValue<float>();

            float handSteer = ComputeHandSteerInput();
            float keyboardSteer = keyboardSteerAction.ReadValue<float>();
            float steerInput = Mathf.Clamp(handSteer + keyboardSteer, -1f, 1f);

            ApplySteering(steerInput);
            ApplyDriveAndBrake(accelInput, brakeInput);
            ApplyDrift(driftInput, steerInput);
            ApplyDownforce();
            ApplyUprightStabilization();
        }

        private float ComputeHandSteerInput()
        {
            if (!isCalibrated || leftHandAnchor == null || rightHandAnchor == null) return 0f;

            Vector3 leftDelta = transform.InverseTransformPoint(leftHandAnchor.position) - leftHandNeutralLocal;
            Vector3 rightDelta = transform.InverseTransformPoint(rightHandAnchor.position) - rightHandNeutralLocal;

            // Left hand up + right hand back -> positive -> turn right.
            // Right hand up + left hand back -> negative -> turn left.
            float raw = (leftDelta.y - rightDelta.y) + (leftDelta.z - rightDelta.z);
            return Mathf.Clamp(raw / steerRange, -1f, 1f);
        }

        private void ApplySteering(float steerInput)
        {
            // While drifting, the held slip-angle controller is what should
            // be doing most of the turning - full front-wheel steering on
            // top of that is what caused the turn to keep tightening the
            // longer Drift was held.
            float steerAuthority = IsDrifting ? driftSteerAngleMultiplier : 1f;
            float targetAngle = steerInput * maxSteerAngle * steerAuthority;
            currentSteerAngle = Mathf.MoveTowards(
                currentSteerAngle,
                targetAngle,
                steerSmoothSpeed * maxSteerAngle * Time.fixedDeltaTime);

            if (frontWheel != null)
            {
                frontWheel.steerAngle = currentSteerAngle;
            }
        }

        /// <summary>
        /// Accelerate forward on accelInput, and on brakeInput either brake
        /// (while still moving forward past brakeToReverseSpeedThreshold) or
        /// drive backward (once slowed to near-stop or already reversing) -
        /// the classic "S also reverses" car-game behavior.
        /// </summary>
        private void ApplyDriveAndBrake(float accelInput, float brakeInput)
        {
            if (rearWheel == null) return;

            float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
            bool isBraking = brakeInput > 0f && forwardSpeed > brakeToReverseSpeedThreshold;
            bool isReversing = brakeInput > 0f && !isBraking;

            float motorTorque = 0f;

            if (accelInput > 0f)
            {
                float speed = rb.linearVelocity.magnitude;
                float speedTaper = Mathf.Clamp01(1f - speed / maxSpeed);
                motorTorque += accelInput * maxMotorTorque * speedTaper;
            }

            if (isReversing)
            {
                float reverseTaper = Mathf.Clamp01(1f - Mathf.Abs(forwardSpeed) / reverseMaxSpeed);
                motorTorque -= brakeInput * reverseMotorTorque * reverseTaper;
            }

            rearWheel.motorTorque = motorTorque;
            rearWheel.brakeTorque = isBraking ? brakeInput * maxBrakeTorque : 0f;

            if (frontWheel != null)
            {
                frontWheel.brakeTorque = isBraking ? brakeInput * maxBrakeTorque * 0.3f : 0f;
            }
        }

        // Which way steerInput has to be nudged so the target slide angle
        // points the tail out the correct way. If the drift ever slides
        // toward the OUTSIDE of the turn instead of the inside (nose aiming
        // away from the corner), flip this to -1f.
        private const float DriftSlideSign = 1f;

        private void ApplyDrift(float driftInput, float steerInput)
        {
            float speed = rb.linearVelocity.magnitude;
            bool wasDrifting = IsDrifting;

            IsDrifting = driftInput > driftTriggerThreshold
                && speed > minDriftSpeed
                && Mathf.Abs(steerInput) > 0.05f;

            if (rearWheel != null)
            {
                rearWheel.sidewaysFriction = IsDrifting ? rearSidewaysFrictionDrift : rearSidewaysFrictionNormal;
            }

            if (IsDrifting)
            {
                ApplyDriftGlideTurn(steerInput);
                ApplyDriftSlideTorque(steerInput);
                UpdateDriftCharge(steerInput);
            }
            else if (wasDrifting)
            {
                ReleaseDriftBoost();
            }

            ApplyDriftBoostForce();
        }

        // Directly rotates the scooter's actual travel direction (its flat
        // velocity vector) at a constant, steer-proportional rate. This is
        // now what drives the curve of a drift - not WheelCollider tire
        // forces, which build up unpredictably over time (that's what made
        // the turn keep tightening the longer Drift was held) and not a
        // total-rotation cap, which is what made it feel like hitting a wall
        // and then snapping straight. A constant rate never accelerates and
        // never runs out, so the scooter can glide through a turn of any
        // length at a steady, predictable radius for as long as Drift and
        // steer are held.
        private void ApplyDriftGlideTurn(float steerInput)
        {
            Vector3 flatVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, transform.up);
            float planarSpeed = flatVelocity.magnitude;
            if (planarSpeed < 0.1f)
            {
                return;
            }

            float turnDegreesThisStep = steerInput * driftGlideTurnRate * Time.fixedDeltaTime;
            Quaternion turnStep = Quaternion.AngleAxis(turnDegreesThisStep, transform.up);

            Vector3 verticalVelocity = rb.linearVelocity - flatVelocity;
            rb.linearVelocity = (turnStep * flatVelocity) + verticalVelocity;
        }

        private void ApplyDriftSlideTorque(float steerInput)
        {
            // Hold a sideways slip angle - the angle between where the
            // scooter is pointed and where it's actually traveling - instead
            // of only adding yaw torque. A constant torque lets the tires'
            // remaining grip slowly drag the heading back in line with
            // velocity, which is why the old drift "straightened out" on its
            // own. This actively targets and holds an offset angle for as
            // long as Drift is held, like a Mario Kart power-slide. Because
            // ApplyDriftGlideTurn above now turns the velocity smoothly and
            // at a bounded rate, this heading target moves smoothly too, so
            // the slide stays fluid instead of chasing a jumpy target.
            Vector3 flatVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, transform.up);
            if (flatVelocity.sqrMagnitude < 0.01f)
            {
                return;
            }

            float currentSlipAngle = Vector3.SignedAngle(transform.forward, flatVelocity, transform.up);
            float targetSlipAngle = DriftSlideSign * Mathf.Sign(steerInput) * driftSlideAngle;

            float angleErrorRad = Mathf.DeltaAngle(currentSlipAngle, targetSlipAngle) * Mathf.Deg2Rad;
            float slideTorque = angleErrorRad * driftSlideHoldStrength - rb.angularVelocity.y * driftSlideDamping;

            rb.AddTorque(transform.up * slideTorque, ForceMode.Acceleration);

            // Gentle safety cap so the heading itself can't overshoot/wobble
            // past a sane rotation speed while catching up to the (now
            // smoothly moving) target slip angle.
            float maxYawRateRad = driftMaxYawRate * Mathf.Deg2Rad;
            Vector3 angularVelocity = rb.angularVelocity;
            angularVelocity.y = Mathf.Clamp(angularVelocity.y, -maxYawRateRad, maxYawRateRad);
            rb.angularVelocity = angularVelocity;
        }

        // Fills a Mario Kart-style mini-turbo meter while drifting. Steering
        // hard into the turn (>= driftChargeOptimalSteerThreshold) charges
        // faster than a shallow, barely-held drift - same idea as MK8's
        // 5-units/frame vs 2-units/frame split, just expressed in seconds.
        private void UpdateDriftCharge(float steerInput)
        {
            bool optimalAngle = Mathf.Abs(steerInput) >= driftChargeOptimalSteerThreshold;
            float rate = optimalAngle ? driftChargeRateOptimal : driftChargeRateShallow;
            driftChargeTimer += rate * Time.fixedDeltaTime;
        }

        // Called the instant Drift is released (or the drift is broken by
        // slowing down / stopping steering). Picks the highest charge tier
        // reached and kicks off that tier's forward boost.
        private void ReleaseDriftBoost()
        {
            MiniTurboTier tier;
            float multiplier;
            float duration;

            if (driftChargeTimer >= ultraTurboChargeTime)
            {
                tier = MiniTurboTier.Ultra;
                multiplier = ultraTurboBoostMultiplier;
                duration = ultraTurboBoostDuration;
            }
            else if (driftChargeTimer >= superTurboChargeTime)
            {
                tier = MiniTurboTier.Super;
                multiplier = superTurboBoostMultiplier;
                duration = superTurboBoostDuration;
            }
            else if (driftChargeTimer >= miniTurboChargeTime)
            {
                tier = MiniTurboTier.Mini;
                multiplier = miniTurboBoostMultiplier;
                duration = miniTurboBoostDuration;
            }
            else
            {
                tier = MiniTurboTier.None;
                multiplier = 0f;
                duration = 0f;
            }

            LastBoostTier = tier;
            driftChargeTimer = 0f;

            if (tier != MiniTurboTier.None)
            {
                boostForceCurrent = driftBoostForce * multiplier;
                boostTimeRemaining = duration;
            }
        }

        // Runs every FixedUpdate (not just while drifting) so the boost keeps
        // pushing the scooter forward for its full duration after Drift is
        // released, same as a mini-turbo firing off the end of a slide.
        private void ApplyDriftBoostForce()
        {
            if (boostTimeRemaining <= 0f)
            {
                return;
            }

            // Taper the push out as forward speed approaches maxBoostSpeed,
            // same idea as the accelerate taper toward maxSpeed - this is
            // what actually caps how strong the boost can feel, regardless
            // of how driftBoostForce/duration are tuned.
            float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
            float speedTaper = Mathf.Clamp01(1f - forwardSpeed / maxBoostSpeed);

            rb.AddForce(transform.forward * boostForceCurrent * speedTaper, ForceMode.Acceleration);
            boostTimeRemaining -= Time.fixedDeltaTime;
        }

        private void ApplyDownforce()
        {
            rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude, ForceMode.Force);
        }

        /// <summary>
        /// Spring-damper torque that keeps the scooter's up vector pointed at
        /// world up, so it doesn't topple over sideways or forward/back.
        /// Without this, two in-line wheels give the Rigidbody nothing to
        /// balance on and it falls over almost immediately.
        /// </summary>
        private void ApplyUprightStabilization()
        {
            Vector3 currentUp = transform.up;
            Vector3 targetUp = Vector3.up;

            Vector3 axis = Vector3.Cross(currentUp, targetUp);
            float angle = Vector3.Angle(currentUp, targetUp) * Mathf.Deg2Rad;

            Vector3 correctiveTorque = Vector3.zero;
            if (axis.sqrMagnitude > 0.0001f)
            {
                correctiveTorque = axis.normalized * angle * uprightSpringStrength;
            }

            Vector3 dampingTorque = -rb.angularVelocity * uprightSpringDamping;

            rb.AddTorque(correctiveTorque + dampingTorque, ForceMode.Acceleration);
        }

        private void Update()
        {
            UpdateWheelVisual(frontWheel, frontWheelVisual, frontWheelVisualEulerOffset);
            UpdateWheelVisual(rearWheel, rearWheelVisual, rearWheelVisualEulerOffset);
        }

        private static void UpdateWheelVisual(WheelCollider wheelCollider, Transform visual, Vector3 eulerOffset)
        {
            if (wheelCollider == null || visual == null) return;
            wheelCollider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            visual.SetPositionAndRotation(position, rotation * Quaternion.Euler(eulerOffset));
        }
    }
}
