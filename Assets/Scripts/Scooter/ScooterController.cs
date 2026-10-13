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
    /// Keyboard: W accelerate, S brake/reverse, A/D steer, Space drift.
    /// VR: right trigger accelerate, left trigger brake, either grip drift,
    /// handlebar hand pose steers.
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

        [Header("VR Hand Anchors (optional)")]
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;

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
        [SerializeField] private float steerRange = 0.35f;
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

        private Vector3 leftHandNeutralLocal;
        private Vector3 rightHandNeutralLocal;
        private bool isCalibrated;

        private float smoothedSteer;
        private float currentSlip;
        private float currentLean;
        private float leanVelocity;
        private float wheelSpinAngle;
        private float currentSpeed;
        private float headingYaw;
        private Transform frontSteerPivot;
        private bool wasAgainstWall;
        private float wallPenaltyTimer;

        public bool IsDrifting { get; private set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
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
            float steerInput = Mathf.Clamp(ComputeHandSteerInput() + keyboardSteerAction.ReadValue<float>(), -1f, 1f);

            smoothedSteer = Mathf.MoveTowards(smoothedSteer, steerInput, steerSmoothSpeed * Time.fixedDeltaTime);

            // Speed is kept here, not read back off the rigidbody. Reading it
            // back mixed the drift slide into the speed and made the drift
            // flicker, which is what shook the camera.
            float signedSpeed = currentSpeed;

            IsDrifting = driftInput > driftTriggerThreshold
                && Mathf.Abs(signedSpeed) > minDriftSpeed
                && Mathf.Abs(smoothedSteer) > 0.08f;

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
                    grounded = true;
                }
            }

            return grounded;
        }

        private float ComputeHandSteerInput()
        {
            if (!isCalibrated || leftHandAnchor == null || rightHandAnchor == null) return 0f;

            Vector3 leftDelta = transform.InverseTransformPoint(leftHandAnchor.position) - leftHandNeutralLocal;
            Vector3 rightDelta = transform.InverseTransformPoint(rightHandAnchor.position) - rightHandNeutralLocal;

            float raw = (leftDelta.y - rightDelta.y) + (leftDelta.z - rightDelta.z);
            return Mathf.Clamp(raw / steerRange, -1f, 1f);
        }

        private void Update()
        {
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
