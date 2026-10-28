using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Arcade scooter for VR. Movement is applied directly to the rigidbody
    /// so the wheel colliders cannot bounce the rider. Turning leans the
    /// scooter into the corner. Holding drift slides the nose into the turn
    /// while the scooter itself takes a wider line.
    ///
    /// Keyboard: W accelerate, S brake/reverse, A/D steer, Space drift, R recenter.
    /// VR: left trigger go, right trigger brake then reverse, A or X drift,
    /// squeeze both grips on the handlebar to steer by twisting your hands,
    /// click a thumbstick to recenter the bars.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ScooterController : MonoBehaviour
    {
        [Header("Wheels (visual only)")]
        [SerializeField] private WheelCollider frontWheel;
        [SerializeField] private WheelCollider rearWheel;
        [SerializeField] private Transform frontWheelVisual;
        [SerializeField] private Transform rearWheelVisual;
        [SerializeField] private Vector3 frontWheelVisualEulerOffset = new Vector3(0f, 0f, 90f);
        [SerializeField] private Vector3 rearWheelVisualEulerOffset = new Vector3(0f, 0f, 90f);
        [SerializeField] private float wheelRadius = 0.15f;

        [Header("Lemon Model (optional)")]
        [Tooltip("Fork, stem and handlebar. Moved onto the front wheel's steering pivot so they turn with it.")]
        [SerializeField] private Transform frontSteerAssembly;
        [Tooltip("Center of the handlebar. When set, VR players grab the model's real handlebar.")]
        [SerializeField] private Transform steeringBarsAnchor;

        [Header("VR Hand Anchors (optional)")]
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;

        [Header("VR Handlebar")]
        [Tooltip("Handlebar twist, in degrees, that counts as full steering lock.")]
        [SerializeField] private float handlebarFullLockAngle = 30f;
        [Tooltip("Twist, in degrees, ignored around center so small hand wobble does not steer.")]
        [SerializeField] private float handlebarDeadzone = 2f;
        [Tooltip("Seconds after the headset starts before the bars appear in front of the rider. Click a thumbstick (or press R) any time to move the bars to your hands.")]
        [SerializeField] private float autoRecenterDelay = 1.5f;
        [Tooltip("If you squeeze both grips away from the bars, move the bars to your hands and grab them. Turn off to make players reach the bars.")]
        [SerializeField] private bool grabAnywhere = true;
        [Tooltip("Where the bars first appear, relative to the rider's head, in meters (down and forward).")]
        [SerializeField] private float barsBelowHead = 0.6f;
        [SerializeField] private float barsAheadOfHead = 0.5f;
        [Tooltip("How close each hand must be to its grip on the bars, in meters, to grab them.")]
        [SerializeField] private float grabRadius = 0.3f;
        [Tooltip("Grip squeeze (0-1) that grabs the bars.")]
        [SerializeField, Range(0f, 1f)] private float gripPressThreshold = 0.6f;
        [Tooltip("Grip squeeze (0-1) below which the bars are let go.")]
        [SerializeField, Range(0f, 1f)] private float gripReleaseThreshold = 0.3f;

        [Header("VR Rider")]
        [Tooltip("Puts the player on the scooter: the headset origin is set to eye level and recentered at start, and the rig is raised to Rider Eye Height.")]
        [SerializeField] private bool setUpRiderOnStart = true;
        [Tooltip("Height of the player's eyes above the scooter's origin, in meters.")]
        [SerializeField] private float riderEyeHeight = 1.7f;
        [Tooltip("With the scooter stopped, hold the right thumbstick down to lower yourself on the scooter and up to raise yourself. The height is remembered between launches.")]
        [SerializeField] private bool allowRiderHeightAdjust = true;
        [Tooltip("How fast the rider moves while the thumbstick is held, in meters per second.")]
        [SerializeField] private float riderHeightAdjustSpeed = 0.3f;
        [SerializeField] private float riderHeightMin = 0.8f;
        [SerializeField] private float riderHeightMax = 2.2f;

        [Header("Speed")]
        [SerializeField] private float maxSpeed = 14f;
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float brakeDeceleration = 28f;
        [SerializeField] private float coastDeceleration = 4f;
        [SerializeField] private float reverseMaxSpeed = 3f;
        [SerializeField] private float reverseAcceleration = 8f;
        [SerializeField] private float brakeToReverseSpeedThreshold = 0.5f;

        [Header("Turning")]
        [Tooltip("Degrees per second at full steer, once up to speed.")]
        [SerializeField] private float turnRate = 100f;
        [Tooltip("Unused since handlebar-twist steering. Kept so saved scenes keep their value.")]
        [SerializeField] private float steerSmoothSpeed = 6f;
        [Tooltip("How far the front wheel mesh visually turns.")]
        [SerializeField] private float maxSteerAngle = 28f;

        [Header("Lean")]
        [Tooltip("Roll into a normal turn, in degrees. Kept small for VR.")]
        [SerializeField] private float turnLeanAngle = 10f;
        [Tooltip("Extra roll while drifting.")]
        [SerializeField] private float driftLeanAngle = 18f;
        [SerializeField] private float leanSmoothTime = 0.2f;

        [Header("Drift")]
        [SerializeField, Range(0f, 1f)] private float driftTriggerThreshold = 0.15f;
        [SerializeField] private float minDriftSpeed = 4f;
        [Tooltip("How hard the slide turns. Lower than Turning / Turn Rate, so a drift takes a wider line.")]
        [SerializeField] private float driftTurnRate = 48f;
        [Tooltip("How far the nose points into the corner, away from the direction of travel.")]
        [SerializeField] private float driftSlideAngle = 35f;
        [Tooltip("How quickly the slide angle settles in and back out.")]
        [SerializeField] private float driftSlideCatchup = 90f;

        [Header("Drift Boost")]
        [Tooltip("Steer amount (0-1) that counts as steering hard into the drift. Hard steering fills the boost meter faster.")]
        [SerializeField, Range(0f, 1f)] private float chargeOptimalSteer = 0.7f;
        [Tooltip("Meter fill speed while steering hard (seconds of charge per second).")]
        [SerializeField] private float chargeRateOptimal = 1f;
        [Tooltip("Meter fill speed while steering gently.")]
        [SerializeField] private float chargeRateShallow = 0.4f;
        [Tooltip("Seconds of charge needed for each boost level.")]
        [SerializeField] private float miniChargeTime = 0.7f;
        [SerializeField] private float superChargeTime = 1.7f;
        [SerializeField] private float ultraChargeTime = 2.6f;
        [Tooltip("How much faster than Max Speed each boost level pushes you (m/s).")]
        [SerializeField] private float miniBoostSpeedBonus = 2.5f;
        [SerializeField] private float superBoostSpeedBonus = 3.5f;
        [SerializeField] private float ultraBoostSpeedBonus = 4.5f;
        [Tooltip("How long each boost level lasts (seconds).")]
        [SerializeField] private float miniBoostDuration = 0.4f;
        [SerializeField] private float superBoostDuration = 0.8f;
        [SerializeField] private float ultraBoostDuration = 1.3f;
        [Tooltip("How fast speed surges up to the boost speed (m/s per second).")]
        [SerializeField] private float boostAcceleration = 30f;
        [Tooltip("How gently speed eases back down to Max Speed after a boost ends (m/s per second). Lower is a softer fade.")]
        [SerializeField] private float boostFadeDeceleration = 6f;

        [Header("Walls")]
        [Tooltip("Fraction of speed kept after a wall hit.")]
        [SerializeField, Range(0.05f, 1f)] private float wallSpeedRetention = 0.35f;
        [Tooltip("Small push straight back off the wall, in meters.")]
        [SerializeField] private float wallBumpDistance = 0.12f;
        [Tooltip("Acceleration is multiplied by this after a wall hit. Lower means a bigger drop.")]
        [SerializeField, Range(0.05f, 1f)] private float wallAccelerationMultiplier = 0.2f;
        [SerializeField] private float wallAccelerationPenaltyDuration = 1.2f;

        [Header("Ground")]
        [SerializeField] private float rideHeight = 0f;

        private Rigidbody rb;
        private BoxCollider bodyCollider;
        private InputAction accelerateAction;
        private InputAction brakeAction;
        private InputAction driftAction;
        private InputAction keyboardSteerAction;

        private InputAction recenterAction;
        private InputAction leftGripAction;
        private InputAction rightGripAction;
        private InputAction riderHeightAction;
        private Transform riderRig;
        private bool riderHeightDirty;
        private const string RiderHeightPrefKey = "CommodoreCircuit.RiderEyeHeight";
        private bool leftGripHeld;
        private bool rightGripHeld;
        private bool isGrabbingHandlebar;

        private Vector3 barsRestLocal;
        private bool hasBarsRest;
        private float neutralHandlebarAngle;
        private Vector3 handlebarCenterLocal;
        private float handlebarWidth = 0.5f;
        private float handlebarAngle;
        private bool isCalibrated;

        private float smoothedSteer;
        private float currentSlip;
        private float currentLean;
        private float leanVelocity;
        private float wheelSpinAngle;
        private float currentSpeed;
        private TrackSurface currentSurface;
        private float headingYaw;
        private Transform frontSteerPivot;
        private bool wasAgainstWall;
        private float wallPenaltyTimer;

        public enum BoostTier { None, Mini, Super, Ultra }

        public bool IsDrifting { get; private set; }
        /// <summary>True while the ground under the scooter is rough (grass).</summary>
        public bool IsOnRoughSurface => currentSurface != null && currentSurface.SpeedMultiplier < 0.999f;

        /// <summary>True while both hands are squeezing their grips on the handlebar.</summary>
        public bool IsGrabbingHandlebar => isGrabbingHandlebar;
        /// <summary>True once the bars have been set to the player's hands.</summary>
        public bool HasHandlebarPose => isCalibrated;
        /// <summary>Where the player's hands rested when recentered, in this scooter's local space.</summary>
        public Vector3 HandlebarCenterLocal => handlebarCenterLocal;
        /// <summary>Distance between the hands when recentered, in meters.</summary>
        public float HandlebarWidth => handlebarWidth;
        /// <summary>Current twist of the hands in degrees. Positive turns right.</summary>
        public float HandlebarAngle => handlebarAngle;
        /// <summary>True while a drift boost is pushing the scooter past top speed.</summary>
        public bool IsBoosting => boostTimer > 0f;
        /// <summary>Boost level the current drift has charged so far (None until the first threshold).</summary>
        public BoostTier ChargeTier { get; private set; } = BoostTier.None;
        /// <summary>Level of the boost that is running now, or the last one that ran.</summary>
        public BoostTier ActiveBoostTier { get; private set; } = BoostTier.None;
        /// <summary>Raised the moment a drift is released with enough charge for a boost.</summary>
        public event System.Action<BoostTier> BoostStarted;
        /// <summary>
        /// While true, throttle, brake and drift are ignored and the scooter coasts.
        /// Steering and recentering still work. The race sets this during the
        /// countdown and after the finish (RaceProgress).
        /// </summary>
        public bool InputLocked { get; set; }

        private float driftCharge;
        private float boostTimer;
        private float boostTargetSpeed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            ResolveHandAnchors();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            headingYaw = transform.eulerAngles.y;

            if (frontWheel != null) frontWheel.enabled = false;
            if (rearWheel != null) rearWheel.enabled = false;
            SetupWheelVisuals();

            // Lift the body box off the asphalt so track seams cannot kick the scooter.
            bodyCollider = GetComponent<BoxCollider>();
            if (bodyCollider != null)
            {
                bodyCollider.center = new Vector3(0f, 0.7f, 0f);
                bodyCollider.size = new Vector3(0.35f, 0.6f, 1.4f);
            }

            accelerateAction = new InputAction("Accelerate", InputActionType.Value, "<XRController>{LeftHand}/trigger");
            accelerateAction.AddBinding("<Keyboard>/w");
            accelerateAction.AddBinding("<Keyboard>/upArrow");

            brakeAction = new InputAction("Brake", InputActionType.Value, "<XRController>{RightHand}/trigger");
            brakeAction.AddBinding("<Keyboard>/s");
            brakeAction.AddBinding("<Keyboard>/downArrow");

            driftAction = new InputAction("Drift", InputActionType.Value);
            driftAction.AddBinding("<Keyboard>/space");
            driftAction.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");   // X
            driftAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");  // A

            leftGripAction = new InputAction("LeftGrip", InputActionType.Value, "<XRController>{LeftHand}/grip");
            rightGripAction = new InputAction("RightGrip", InputActionType.Value, "<XRController>{RightHand}/grip");

            riderHeightAction = new InputAction("RiderHeight", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");

            recenterAction = new InputAction("RecenterSteering", InputActionType.Button);
            recenterAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
            recenterAction.AddBinding("<XRController>{RightHand}/{Primary2DAxisClick}");
            recenterAction.AddBinding("<Keyboard>/r");

            keyboardSteerAction = new InputAction("KeyboardSteer", InputActionType.Value);
            keyboardSteerAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/d")
                .With("Positive", "<Keyboard>/rightArrow");
        }

        private void OnEnable()
        {
            accelerateAction.Enable();
            brakeAction.Enable();
            driftAction.Enable();
            keyboardSteerAction.Enable();
            leftGripAction.Enable();
            rightGripAction.Enable();
            riderHeightAction.Enable();
            recenterAction.performed += OnRecenterPerformed;
            recenterAction.Enable();
        }

        private void OnDisable()
        {
            accelerateAction.Disable();
            brakeAction.Disable();
            driftAction.Disable();
            keyboardSteerAction.Disable();
            leftGripAction.Disable();
            rightGripAction.Disable();
            riderHeightAction.Disable();
            SaveRiderHeight();
            recenterAction.performed -= OnRecenterPerformed;
            recenterAction.Disable();
        }

        private void Start()
        {
            // Remember where the real handlebar sits while the wheel is still straight.
            if (steeringBarsAnchor != null)
            {
                barsRestLocal = transform.InverseTransformPoint(steeringBarsAnchor.position);
                hasBarsRest = true;
            }

            if (leftHandAnchor != null && rightHandAnchor != null)
                StartCoroutine(SetUpRider());
        }

        // The hand anchors are normally wired in the Inspector. Scenes built
        // before that was saved have them empty, which silently turned off all
        // hand steering, so look them up by name under the scooter.
        private void ResolveHandAnchors()
        {
            if (leftHandAnchor != null && rightHandAnchor != null) return;

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (leftHandAnchor == null && child.name == "LeftHandAnchor") leftHandAnchor = child;
                else if (rightHandAnchor == null && child.name == "RightHandAnchor") rightHandAnchor = child;
            }
        }

        // Waits for the headset, puts the player on the scooter, then puts the
        // bars in front of them. Does nothing when there is no headset, so
        // keyboard play in the editor is unchanged.
        private IEnumerator SetUpRider()
        {
            float waited = 0f;
            while (!UnityEngine.XR.XRSettings.isDeviceActive && waited < 8f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!UnityEngine.XR.XRSettings.isDeviceActive) yield break;

            // The head and both hands must share one parent so they stay in the same
            // tracking space. In this scene the camera sat directly under the scooter
            // while the hands sat under the VR rig, which left the hands floating.
            Transform trackingRig = leftHandAnchor.parent;
            Camera headCamera = Camera.main;
            if (trackingRig != null && trackingRig != transform && headCamera != null && headCamera.transform.parent != trackingRig)
                headCamera.transform.SetParent(trackingRig, false);

            // Controller models on the hands, and a floating handlebar unless the scooter
            // model has its own real one.
            if (hasBarsRest)
                ScooterHandlebar.AddControllerModels(this, leftHandAnchor, rightHandAnchor);
            else if (GetComponentInChildren<ScooterHandlebar>(true) == null)
                ScooterHandlebar.CreateInScene(this, leftHandAnchor, rightHandAnchor);

            if (setUpRiderOnStart)
            {
                // Eye level origin: the headset starts where the player is sitting or
                // standing, so it lands on the scooter instead of wherever the room's
                // floor origin happens to be.
                UnityEngine.XR.InputDevice head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                UnityEngine.XR.XRInputSubsystem tracking = head.isValid ? head.subsystem : null;
                if (tracking != null)
                {
                    tracking.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Device);
                    tracking.TryRecenter();
                }

                // A height the player set on an earlier launch wins over the default.
                if (allowRiderHeightAdjust && PlayerPrefs.HasKey(RiderHeightPrefKey))
                    riderEyeHeight = Mathf.Clamp(PlayerPrefs.GetFloat(RiderHeightPrefKey), riderHeightMin, riderHeightMax);

                Transform rig = leftHandAnchor.parent;
                if (rig != null && rig != transform)
                {
                    Vector3 rigPosition = rig.localPosition;
                    rigPosition.y = riderEyeHeight;
                    rig.localPosition = rigPosition;
                    riderRig = rig;
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0.5f, autoRecenterDelay));
            PlaceBarsInFrontOfRider();
        }

        private void PlaceBarsInFrontOfRider()
        {
            if (hasBarsRest)
            {
                // The scooter model's real handlebar.
                handlebarCenterLocal = barsRestLocal;
            }
            else
            {
                Transform head = Camera.main != null ? Camera.main.transform : null;
                if (head == null) return;

                Vector3 headLocal = transform.InverseTransformPoint(head.position);
                handlebarCenterLocal = headLocal + new Vector3(0f, -barsBelowHead, barsAheadOfHead);
            }
            handlebarWidth = 0.5f;
            neutralHandlebarAngle = 0f;
            isCalibrated = true;
        }

        private void OnRecenterPerformed(InputAction.CallbackContext context)
        {
            RecenterSteering();
        }

        /// <summary>
        /// Treats wherever the hands are right now as the straight-ahead
        /// handlebar position. Does nothing until both controllers are tracked.
        /// </summary>
        public void RecenterSteering()
        {
            if (leftHandAnchor == null || rightHandAnchor == null) return;

            Vector3 left = transform.InverseTransformPoint(leftHandAnchor.position);
            Vector3 right = transform.InverseTransformPoint(rightHandAnchor.position);
            Vector3 across = right - left;
            across.y = 0f;
            if (across.magnitude < 0.05f) return;

            neutralHandlebarAngle = Vector3.SignedAngle(Vector3.right, across, Vector3.up);
            handlebarCenterLocal = (left + right) * 0.5f;
            handlebarWidth = Mathf.Clamp(across.magnitude, 0.3f, 0.8f);
            isCalibrated = true;
        }

        private void FixedUpdate()
        {
            float accelInput = accelerateAction.ReadValue<float>();
            float brakeInput = brakeAction.ReadValue<float>();
            float driftInput = driftAction.ReadValue<float>();
            float steerInput = Mathf.Clamp(ComputeHandSteerInput() + keyboardSteerAction.ReadValue<float>(), -1f, 1f);
            if (InputLocked)
            {
                accelInput = 0f;
                brakeInput = 0f;
                driftInput = 0f;
            }

            smoothedSteer = Mathf.MoveTowards(smoothedSteer, steerInput, steerSmoothSpeed * Time.fixedDeltaTime);

            // Speed is kept here, not read back off the rigidbody. Reading it
            // back mixed the drift slide into the speed and made the drift
            // flicker, which is what shook the camera.
            float signedSpeed = currentSpeed;

            bool driftHeld = driftInput > driftTriggerThreshold
                && Mathf.Abs(signedSpeed) > minDriftSpeed;
            IsDrifting = driftHeld && Mathf.Abs(smoothedSteer) > 0.08f;
            UpdateDriftCharge(driftHeld);

            float targetSlip = IsDrifting ? Mathf.Sign(smoothedSteer) * driftSlideAngle : 0f;
            currentSlip = Mathf.MoveTowards(currentSlip, targetSlip, driftSlideCatchup * Time.fixedDeltaTime);

            float rate = IsDrifting ? driftTurnRate : turnRate;
            float speedFactor = Mathf.Clamp01(Mathf.Abs(signedSpeed) / 4f);
            headingYaw += smoothedSteer * rate * speedFactor * Time.fixedDeltaTime;

            Vector3 nose = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;

            float targetSpeed = 0f;
            float speedChange = coastDeceleration;
            bool isBraking = brakeInput > 0.01f && signedSpeed > brakeToReverseSpeedThreshold;

            if (wallPenaltyTimer > 0f)
                wallPenaltyTimer -= Time.fixedDeltaTime;

            if (accelInput > 0.01f)
            {
                targetSpeed = maxSpeed * accelInput;
                speedChange = acceleration;
                if (wallPenaltyTimer > 0f)
                    speedChange *= wallAccelerationMultiplier;
            }
            else if (isBraking)
            {
                targetSpeed = 0f;
                speedChange = brakeDeceleration;
            }
            else if (brakeInput > 0.01f)
            {
                targetSpeed = -reverseMaxSpeed * brakeInput;
                speedChange = reverseAcceleration;
            }

            if (boostTimer > 0f)
            {
                boostTimer -= Time.fixedDeltaTime;
                if (!isBraking)
                {
                    targetSpeed = Mathf.Max(targetSpeed, boostTargetSpeed);
                    speedChange = Mathf.Max(speedChange, boostAcceleration);
                }
            }
            else if (signedSpeed > maxSpeed && !isBraking)
            {
                // Boost is over: ease back to normal top speed instead of snapping.
                speedChange = Mathf.Min(speedChange, boostFadeDeceleration);
            }

            // Rough ground (grass): lower top speed, slower pickup, and the
            // scooter sheds speed above the new cap instead of keeping it.
            if (currentSurface != null && currentSurface.SpeedMultiplier < 0.999f)
            {
                float cap = maxSpeed * currentSurface.SpeedMultiplier;
                if (targetSpeed > cap)
                    targetSpeed = cap;
                if (signedSpeed > cap && !isBraking)
                    speedChange = currentSurface.DragDeceleration;
                else if (targetSpeed > signedSpeed)
                    speedChange *= currentSurface.AccelerationMultiplier;
            }

            currentSpeed = Mathf.MoveTowards(signedSpeed, targetSpeed, speedChange * Time.fixedDeltaTime);

            // Positive slip yaws the nose to the left of travel. Negating it
            // puts the travel direction on the outside of the turn.
            Vector3 travel = Quaternion.AngleAxis(-currentSlip, Vector3.up) * nose;

            float leanTarget = 0f;
            if (Mathf.Abs(smoothedSteer) > 0.01f && Mathf.Abs(signedSpeed) > 0.5f)
            {
                float leanAmount = IsDrifting ? driftLeanAngle : turnLeanAngle;
                leanTarget = -smoothedSteer * leanAmount * Mathf.Clamp01(Mathf.Abs(signedSpeed) / 6f);
            }

            currentLean = Mathf.SmoothDamp(currentLean, leanTarget, ref leanVelocity, leanSmoothTime);

            Vector3 delta = travel.sqrMagnitude > 0.0001f
                ? travel.normalized * currentSpeed * Time.fixedDeltaTime
                : Vector3.zero;
            delta.y = 0f;
            Vector3 next = rb.position + BounceOffWalls(delta);
            if (TryGetGroundY(out float groundY))
                next.y = groundY;

            Vector3 facing = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;
            rb.MovePosition(next);
            rb.MoveRotation(Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(0f, 0f, currentLean));
        }

        // Fills the boost meter while drifting. Steering hard into the turn
        // fills it faster. If the drift button is still held but steering
        // passes through center, the charge simply waits instead of firing.
        private void UpdateDriftCharge(bool driftHeld)
        {
            if (IsDrifting)
            {
                bool steeringHard = Mathf.Abs(smoothedSteer) >= chargeOptimalSteer;
                driftCharge += (steeringHard ? chargeRateOptimal : chargeRateShallow) * Time.fixedDeltaTime;
                ChargeTier = TierForCharge(driftCharge);
            }
            else if (!driftHeld && driftCharge > 0f)
            {
                ReleaseBoost();
            }
        }

        private BoostTier TierForCharge(float charge)
        {
            if (charge >= ultraChargeTime) return BoostTier.Ultra;
            if (charge >= superChargeTime) return BoostTier.Super;
            if (charge >= miniChargeTime) return BoostTier.Mini;
            return BoostTier.None;
        }

        private void ReleaseBoost()
        {
            BoostTier tier = TierForCharge(driftCharge);
            driftCharge = 0f;
            ChargeTier = BoostTier.None;
            if (tier == BoostTier.None) return;

            float bonus = miniBoostSpeedBonus;
            float duration = miniBoostDuration;
            if (tier == BoostTier.Super)
            {
                bonus = superBoostSpeedBonus;
                duration = superBoostDuration;
            }
            else if (tier == BoostTier.Ultra)
            {
                bonus = ultraBoostSpeedBonus;
                duration = ultraBoostDuration;
            }

            boostTargetSpeed = maxSpeed + bonus;
            boostTimer = duration;
            ActiveBoostTier = tier;
            BoostStarted?.Invoke(tier);
        }

        private Vector3 BounceOffWalls(Vector3 delta)
        {
            float distance = delta.magnitude;
            if (distance < 0.0001f || bodyCollider == null)
            {
                wasAgainstWall = false;
                return delta;
            }

            if (!rb.SweepTest(delta / distance, out RaycastHit hit, distance, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<TrackWall>() == null)
            {
                wasAgainstWall = false;
                return delta;
            }

            Vector3 normal = hit.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.0001f)
                normal = -delta / distance;
            normal.Normalize();

            float allowed = Mathf.Max(0f, hit.distance - 0.02f);
            Vector3 stopped = delta.normalized * Mathf.Min(distance, allowed);
            if (Vector3.Dot(stopped, normal) < 0f)
                stopped = Vector3.ProjectOnPlane(stopped, normal);

            if (wasAgainstWall)
                return stopped;

            currentSpeed *= wallSpeedRetention;
            boostTimer = 0f;
            wallPenaltyTimer = wallAccelerationPenaltyDuration;
            wasAgainstWall = true;
            return stopped + normal * wallBumpDistance;
        }

        private void SetupWheelVisuals()
        {
            Vector3 frontAxle = frontWheel != null
                ? frontWheel.transform.localPosition
                : new Vector3(0f, wheelRadius, 0.6f);
            Vector3 rearAxle = rearWheel != null
                ? rearWheel.transform.localPosition
                : new Vector3(0f, wheelRadius, -0.6f);

            frontSteerPivot = CreateSteerPivot("FrontWheel_Steer", frontAxle);
            AttachWheel(frontWheelVisual, frontSteerPivot);
            AttachWheel(rearWheelVisual, CreateSteerPivot("RearWheel_Hub", rearAxle));

            // The fork, stem and handlebar turn with the front wheel.
            if (frontSteerAssembly != null)
                frontSteerAssembly.SetParent(frontSteerPivot, true);
        }

        private Transform CreateSteerPivot(string pivotName, Vector3 localPosition)
        {
            Transform existing = transform.Find(pivotName);
            if (existing != null)
                return existing;

            GameObject pivot = new GameObject(pivotName);
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = localPosition;
            pivot.transform.localRotation = Quaternion.identity;
            return pivot.transform;
        }

        private static void AttachWheel(Transform visual, Transform pivot)
        {
            if (visual == null)
                return;

            visual.SetParent(pivot, false);
            visual.localPosition = Vector3.zero;
        }

        private bool TryGetGroundY(out float groundY)
        {
            groundY = rb.position.y;
            currentSurface = null;
            Vector3 origin = rb.position + Vector3.up * 1.5f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float closest = float.MaxValue;
            bool grounded = false;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].normal.y < 0.6f)
                    continue;

                Transform hitTransform = hits[i].collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                    continue;
                if (hits[i].distance < closest)
                {
                    closest = hits[i].distance;
                    groundY = hits[i].point.y + rideHeight;
                    currentSurface = hits[i].collider.GetComponent<TrackSurface>();
                    grounded = true;
                }
            }

            return grounded;
        }

        // Handlebar twist: while both grips are squeezed with the hands on the
        // bars, steering follows the angle of the line between the two hands as
        // seen from above, like turning real scooter bars. Right hand back and
        // left hand forward turns right.
        private float ComputeHandSteerInput()
        {
            handlebarAngle = 0f;
            if (leftHandAnchor == null || rightHandAnchor == null)
            {
                isGrabbingHandlebar = false;
                return 0f;
            }

            float leftSqueeze = leftGripAction.ReadValue<float>();
            float rightSqueeze = rightGripAction.ReadValue<float>();
            leftGripHeld = leftGripHeld ? leftSqueeze > gripReleaseThreshold : leftSqueeze > gripPressThreshold;
            rightGripHeld = rightGripHeld ? rightSqueeze > gripReleaseThreshold : rightSqueeze > gripPressThreshold;

            Vector3 left = transform.InverseTransformPoint(leftHandAnchor.position);
            Vector3 right = transform.InverseTransformPoint(rightHandAnchor.position);
            Vector3 across = right - left;
            across.y = 0f;
            bool handsApart = across.sqrMagnitude >= 0.0025f;

            if (isGrabbingHandlebar)
            {
                // Keep hold for as long as both grips stay squeezed.
                isGrabbingHandlebar = leftGripHeld && rightGripHeld && handsApart;
            }
            else if (leftGripHeld && rightGripHeld && handsApart)
            {
                // Grab when each hand is on its end of the bars. If the hands are
                // somewhere else, bring the bars to them so a squeeze always works.
                bool onBars = isCalibrated && HandsAreOnBars(left, right);
                if (!onBars && grabAnywhere)
                {
                    RecenterSteering();
                    onBars = isCalibrated;
                }
                if (onBars)
                {
                    isGrabbingHandlebar = true;
                    neutralHandlebarAngle = Vector3.SignedAngle(Vector3.right, across, Vector3.up);
                }
            }

            if (!isGrabbingHandlebar) return 0f;

            handlebarAngle = Mathf.DeltaAngle(neutralHandlebarAngle, Vector3.SignedAngle(Vector3.right, across, Vector3.up));

            float beyondDeadzone = Mathf.Max(0f, Mathf.Abs(handlebarAngle) - handlebarDeadzone);
            float fullRange = Mathf.Max(1f, handlebarFullLockAngle - handlebarDeadzone);
            return Mathf.Clamp(Mathf.Sign(handlebarAngle) * beyondDeadzone / fullRange, -1f, 1f);
        }

        private bool HandsAreOnBars(Vector3 leftLocal, Vector3 rightLocal)
        {
            float halfWidth = handlebarWidth * 0.5f;
            Vector3 leftSpot = handlebarCenterLocal + Vector3.left * halfWidth;
            Vector3 rightSpot = handlebarCenterLocal + Vector3.right * halfWidth;
            float radiusSqr = grabRadius * grabRadius;
            return (leftLocal - leftSpot).sqrMagnitude <= radiusSqr
                && (rightLocal - rightSpot).sqrMagnitude <= radiusSqr;
        }

        // Positioning aid: with the scooter stopped, the right thumbstick moves
        // the rider down (stick back) or up (stick forward).
        private void UpdateRiderHeight()
        {
            if (!allowRiderHeightAdjust || riderRig == null || riderHeightAction == null) return;

            Vector2 stick = riderHeightAction.ReadValue<Vector2>();
            bool adjusting = Mathf.Abs(currentSpeed) < 0.5f
                && Mathf.Abs(stick.y) > 0.6f
                && Mathf.Abs(stick.y) > Mathf.Abs(stick.x);

            if (!adjusting)
            {
                if (riderHeightDirty) SaveRiderHeight();
                return;
            }

            riderEyeHeight = Mathf.Clamp(
                riderEyeHeight + Mathf.Sign(stick.y) * riderHeightAdjustSpeed * Time.deltaTime,
                riderHeightMin, riderHeightMax);
            Vector3 rigPosition = riderRig.localPosition;
            rigPosition.y = riderEyeHeight;
            riderRig.localPosition = rigPosition;
            riderHeightDirty = true;
        }

        private void SaveRiderHeight()
        {
            if (!riderHeightDirty) return;
            PlayerPrefs.SetFloat(RiderHeightPrefKey, riderEyeHeight);
            PlayerPrefs.Save();
            riderHeightDirty = false;
        }

        private void Update()
        {
            UpdateRiderHeight();
            float speed = Mathf.Abs(currentSpeed);
            wheelSpinAngle += speed / Mathf.Max(0.05f, wheelRadius) * Time.deltaTime * Mathf.Rad2Deg;

            if (frontSteerPivot != null)
                frontSteerPivot.localRotation = Quaternion.Euler(0f, smoothedSteer * maxSteerAngle, 0f);

            Quaternion spin = Quaternion.AngleAxis(wheelSpinAngle, Vector3.right);
            if (frontWheelVisual != null)
                frontWheelVisual.localRotation = spin * Quaternion.Euler(frontWheelVisualEulerOffset);
            if (rearWheelVisual != null)
                rearWheelVisual.localRotation = spin * Quaternion.Euler(rearWheelVisualEulerOffset);
        }
    }
}
