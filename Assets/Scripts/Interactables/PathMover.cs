using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Moves a hazard along its waypoints with a telegraph ("tell") before each run.
    ///
    /// Prefab layout (made by the prefab builder):
    ///   Squirrel            &lt;- PathMover (stays put; drag it into the scene)
    ///     Waypoint_0..N     &lt;- move these in the scene to lay out the path
    ///     Mover             &lt;- Rigidbody, trigger collider, Hazard, animator, model
    ///
    /// Waypoints are children of THIS object (not of Mover), so they stay fixed
    /// while the Mover travels.
    /// </summary>
    public class PathMover : MonoBehaviour
    {
        public enum Activation { Proximity, Interval, Continuous, Manual }
        public enum EndBehavior { PingPong, ResetToStart, Loop }
        public enum MoveState { Idle, Tell, Moving, Cooldown }

        [Header("Parts")]
        public Transform mover;
        public Transform[] waypoints;

        [Header("When it moves")]
        public Activation activation = Activation.Proximity;
        [Tooltip("Proximity: a racer within this many metres starts a run.")]
        public float triggerRadius = 18f;
        [Tooltip("Interval: seconds between runs.")]
        public float interval = 5f;
        [Tooltip("Pause after each run before it can trigger again.")]
        public float cooldown = 2f;

        [Header("Telegraph (VR readability: always give 0.5-1 s)")]
        public float tellDuration = 0.6f;
        public AudioSource tellAudio;

        [Header("How it moves")]
        public float speed = 6f;
        public EndBehavior endBehavior = EndBehavior.PingPong;
        [Tooltip("Parabolic arc height over a run (frisbee). Keep the peak below face height.")]
        public float arcHeight = 0f;
        [Tooltip("Side-to-side wander amplitude in metres (lost freshman).")]
        public float zigzagAmplitude = 0f;
        public float zigzagFrequency = 0.3f;
        public bool faceDirection = true;
        public float turnSpeed = 720f;

        [Header("Rolling / spinning parts (optional)")]
        [Tooltip("Rotated around the mover's right axis by distance / radius (construction pipe).")]
        public Transform roller;
        public float rollRadius = 0.467f;
        [Tooltip("Spun around its own up axis while moving (frisbee).")]
        public Transform spinner;
        public float spinnerSpeed = 900f;
        [Tooltip("Cleared and switched on for each run, off between runs (so resets don't streak).")]
        public TrailRenderer trail;

        [Header("Events")]
        public UnityEvent onTell = new UnityEvent();
        public UnityEvent onRunStart = new UnityEvent();
        public UnityEvent onRunEnd = new UnityEvent();

        public MoveState State { get; private set; } = MoveState.Idle;
        /// <summary>0..1 progress of the current run.</summary>
        public float RunProgress { get; private set; }
        public float CurrentSpeed { get; private set; }

        bool forward = true;
        float nextInterval;
        bool manualTrigger;
        float rollAngle;
        Quaternion rollerBase = Quaternion.identity;

        public void Trigger() => manualTrigger = true;

        void Reset()
        {
            var m = transform.Find("Mover");
            if (m != null) mover = m;
        }

        void OnEnable()
        {
            if (mover == null || waypoints == null || waypoints.Length < 2)
            {
                Debug.LogWarning($"{name}: PathMover needs a mover and at least 2 waypoints.", this);
                enabled = false;
                return;
            }
            if (roller != null) rollerBase = roller.localRotation;
            if (trail != null) trail.emitting = false;
            PlaceAt(0f, true);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            nextInterval = Time.time + interval;
            while (true)
            {
                // Wait for activation.
                State = MoveState.Idle;
                CurrentSpeed = 0f;
                while (!ShouldStart()) yield return null;
                manualTrigger = false;

                if (tellDuration > 0f)
                {
                    State = MoveState.Tell;
                    onTell.Invoke();
                    if (tellAudio != null) tellAudio.Play();
                    yield return new WaitForSeconds(tellDuration);
                }

                State = MoveState.Moving;
                if (trail != null) { trail.Clear(); trail.emitting = true; }
                onRunStart.Invoke();
                float length = PathLength();
                float d = 0f;
                CurrentSpeed = speed;
                while (d < length)
                {
                    float step = speed * Time.deltaTime;
                    d = Mathf.Min(length, d + step);
                    RunProgress = d / length;
                    PlaceAt(RunProgress, false);
                    if (roller != null && rollRadius > 0f)
                    {
                        rollAngle += step / rollRadius * Mathf.Rad2Deg;
                        roller.localRotation = Quaternion.Euler(rollAngle, 0f, 0f) * rollerBase;  // parent-space X = pipe axis
                    }
                    if (spinner != null) spinner.Rotate(Vector3.up, spinnerSpeed * Time.deltaTime, Space.Self);
                    yield return null;
                }
                if (trail != null) trail.emitting = false;
                onRunEnd.Invoke();

                if (endBehavior == EndBehavior.PingPong) forward = !forward;
                State = MoveState.Cooldown;
                CurrentSpeed = 0f;
                if (cooldown > 0f) yield return new WaitForSeconds(cooldown);
                if (endBehavior != EndBehavior.PingPong) PlaceAt(0f, true);  // ResetToStart / Loop
                nextInterval = Time.time + interval;
            }
        }

        bool ShouldStart()
        {
            switch (activation)
            {
                case Activation.Continuous: return true;
                case Activation.Manual: return manualTrigger;
                case Activation.Interval: return Time.time >= nextInterval || manualTrigger;
                default: return manualTrigger || RacerWithin(triggerRadius);
            }
        }

        bool RacerWithin(float radius)
        {
            float r2 = radius * radius;
            Vector3 c = mover.position;
            foreach (var racer in RacerEffects.All)
                if ((racer.transform.position - c).sqrMagnitude < r2) return true;
            // Fallback when the team's racer doesn't use RacerEffects: the VR camera.
            if (RacerEffects.All.Count == 0 && Camera.main != null)
                return (Camera.main.transform.position - c).sqrMagnitude < r2;
            return false;
        }

        float PathLength()
        {
            float total = 0f;
            for (int i = 1; i < waypoints.Length; i++)
                total += Vector3.Distance(waypoints[i - 1].position, waypoints[i].position);
            return Mathf.Max(total, 0.01f);
        }

        /// <summary>Place the mover at progress t (0..1) of the current run direction.</summary>
        void PlaceAt(float t, bool snapRotation)
        {
            float u = forward ? t : 1f - t;
            float target = u * PathLength();
            int seg = 1;
            float acc = 0f;
            Vector3 a = waypoints[0].position, b = waypoints[1].position;
            for (; seg < waypoints.Length; seg++)
            {
                a = waypoints[seg - 1].position;
                b = waypoints[seg].position;
                float len = Vector3.Distance(a, b);
                if (acc + len >= target || seg == waypoints.Length - 1)
                {
                    float k = len > 1e-5f ? Mathf.Clamp01((target - acc) / len) : 0f;
                    Vector3 pos = Vector3.Lerp(a, b, k);
                    Vector3 dir = (forward ? b - a : a - b);
                    dir.y = 0f;
                    if (zigzagAmplitude > 0f && dir.sqrMagnitude > 1e-6f)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
                        pos += side * (Mathf.Sin(Time.time * zigzagFrequency * Mathf.PI * 2f) * zigzagAmplitude);
                    }
                    if (arcHeight > 0f) pos += Vector3.up * (4f * arcHeight * t * (1f - t));
                    mover.position = pos;

                    if (faceDirection && dir.sqrMagnitude > 1e-6f)
                    {
                        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
                        mover.rotation = snapRotation
                            ? want
                            : Quaternion.RotateTowards(mover.rotation, want, turnSpeed * Time.deltaTime);
                    }
                    return;
                }
                acc += len;
            }
        }

        void OnDrawGizmos()
        {
            if (waypoints == null) return;
            Gizmos.color = new Color(1f, 0.78f, 0.17f);
            for (int i = 1; i < waypoints.Length; i++)
                if (waypoints[i - 1] != null && waypoints[i] != null)
                    Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
            if (activation == Activation.Proximity)
            {
                Gizmos.color = new Color(1f, 0.19f, 0.19f, 0.25f);
                Gizmos.DrawWireSphere(mover != null ? mover.position : transform.position, triggerRadius);
            }
        }
    }
}
