using System;
using System.Collections.Generic;
using CommodoreCircuit.Scooter;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// The local player's race: distance along the lap, laps, lap times, finish and
    /// wrong-way. Lives on this client's scooter (the scooter is local; only the avatar
    /// is networked). <see cref="NetworkRacer"/> copies these numbers onto the network
    /// so everyone can rank everyone.
    ///
    /// Laps are counted from distance, not from a finish-line trigger: driving backwards
    /// subtracts distance, so you can't farm laps by rocking across the line, and the
    /// start grid can sit a little behind the line without costing anyone a lap.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RaceProgress : MonoBehaviour
    {
        public static RaceProgress Local { get; private set; }

        [SerializeField] private ScooterController scooter;

        [Tooltip("Stop throttle, brake and drift until GO, and after the finish (the scooter coasts to a stop). Steering and handlebar recentering still work.")]
        [SerializeField] private bool lockScooterOutsideRace = true;

        [Header("Wrong way")]
        [Tooltip("Facing/moving against the race direction for this long shows the warning (s).")]
        [SerializeField] private float wrongWayDelay = 1.0f;
        [Tooltip("Pointing the right way for this long hides it again (s).")]
        [SerializeField] private float rightWayDelay = 0.4f;
        [Tooltip("cos of the angle that counts as backwards. -0.35 = more than ~110 degrees off the race line.")]
        [SerializeField, Range(-1f, 0f)] private float wrongWayDot = -0.35f;
        [Tooltip("cos of the angle that counts as the right way again. 0.2 = within ~78 degrees.")]
        [SerializeField, Range(-0.5f, 1f)] private float rightWayDot = 0.2f;
        [Tooltip("Below this speed (m/s) the scooter's facing decides, above it the direction of travel does.")]
        [SerializeField] private float movingSpeed = 1.5f;

        [Header("Safety")]
        [Tooltip("Ignore progress jumps bigger than this in one frame (m), e.g. a respawn.")]
        [SerializeField] private float maxJumpPerFrame = 30f;

        // ---- Read by the HUD and NetworkRacer ----
        /// <summary>Metres since the start line, across laps. Negative on the grid behind the line.</summary>
        public float RaceDistance { get; private set; }
        public int CompletedLaps { get; private set; }
        /// <summary>1-based lap the racer is on, clamped to the race's lap count.</summary>
        public int CurrentLap => Mathf.Clamp(CompletedLaps + 1, 1, Mathf.Max(1, Laps));
        public bool Finished { get; private set; }
        public float FinishTime { get; private set; }
        public float LastLapTime { get; private set; }
        public float BestLapTime { get; private set; }
        public IReadOnlyList<float> LapTimes => lapTimes;
        public bool IsWrongWay { get; private set; }
        /// <summary>0..1 through the current lap.</summary>
        public float LapFraction => track && track.LapLength > 0f
            ? Mathf.Clamp01((RaceDistance - CompletedLaps * track.LapLength) / track.LapLength) : 0f;
        public int Laps => RaceSession.Current?.Laps ?? 3;
        public ScooterController Scooter => scooter;

        // ---- Events (fired on this client only) ----
        /// <summary>Arg: laps completed so far (1 after the first lap). Not raised for the final lap; Finished is.</summary>
        public event Action<int> LapCompleted;
        public event Action RaceFinished;
        public event Action<bool> WrongWayChanged;

        private readonly List<float> lapTimes = new List<float>();
        private RaceTrack track;
        private int segmentHint = -1;
        private float lastLapDistance = float.NaN;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private float lapStartTime;
        private float wrongTimer, rightTimer;
        private RacePhase lastPhase = RacePhase.Waiting;

        private void Awake()
        {
            if (scooter == null) scooter = GetComponent<ScooterController>();
        }

        private void OnEnable() => Local = this;

        private void OnDisable()
        {
            if (Local == this) Local = null;
            if (scooter) scooter.InputLocked = false;
        }

        private void Update()
        {
            if (track == null) track = RaceTrack.Instance;
            var session = RaceSession.Current;
            if (track == null || !track.IsReady || session == null) return;

            if (session.Phase != lastPhase)
            {
                if (session.Phase <= RacePhase.Countdown) ResetRace();
                lastPhase = session.Phase;
            }

            var here = track.Project(transform.position, segmentHint);
            segmentHint = here.Segment;
            TrackDistance(here.Distance, session.Phase);

            if (session.Phase == RacePhase.Racing && !Finished)
                CheckLaps(session);

            UpdateWrongWay(here, session.Phase == RacePhase.Racing && !Finished);

            if (lockScooterOutsideRace && scooter)
                scooter.InputLocked = session.Phase != RacePhase.Racing || Finished;
        }

        private void ResetRace()
        {
            CompletedLaps = 0;
            Finished = false;
            FinishTime = 0f;
            LastLapTime = 0f;
            BestLapTime = 0f;
            lapTimes.Clear();
            lapStartTime = 0f;
            SetWrongWay(false);
        }

        // Turn "metres along this lap" (0..L, wraps at the line) into one continuous number.
        private void TrackDistance(float lapDistance, RacePhase phase)
        {
            float L = track.LapLength;
            if (phase <= RacePhase.Countdown || float.IsNaN(lastLapDistance))
            {
                // On the grid: a racer just behind the line is at -2 m, not 978 m.
                RaceDistance = lapDistance > L * 0.5f ? lapDistance - L : lapDistance;
                lastLapDistance = lapDistance;
                return;
            }

            float delta = lapDistance - lastLapDistance;
            if (delta > L * 0.5f) delta -= L;        // crossed the line backwards
            else if (delta < -L * 0.5f) delta += L;  // crossed the line forwards
            lastLapDistance = lapDistance;
            if (Mathf.Abs(delta) > maxJumpPerFrame) return;
            RaceDistance += delta;
        }

        private void CheckLaps(IRaceSession session)
        {
            float L = track.LapLength;
            int laps = Mathf.Max(1, session.Laps);
            int reached = Mathf.FloorToInt(RaceDistance / L);
            // Only a NEW furthest lap counts, so reversing over the line and back is not a lap.
            while (reached > CompletedLaps && !Finished)
            {
                float now = session.RaceTime;
                float lapTime = now - lapStartTime;
                lapStartTime = now;
                lapTimes.Add(lapTime);
                LastLapTime = lapTime;
                if (BestLapTime <= 0f || lapTime < BestLapTime) BestLapTime = lapTime;
                CompletedLaps++;

                if (CompletedLaps >= laps)
                {
                    Finished = true;
                    FinishTime = now;
                    SetWrongWay(false);
                    RaceFinished?.Invoke();
                }
                else
                {
                    LapCompleted?.Invoke(CompletedLaps);
                }
            }
        }

        private void UpdateWrongWay(RaceTrack.TrackPoint here, bool active)
        {
            Vector3 pos = transform.position;
            Vector3 velocity = hasLastPosition && Time.deltaTime > 0f ? (pos - lastPosition) / Time.deltaTime : Vector3.zero;
            lastPosition = pos;
            hasLastPosition = true;
            velocity.y = 0f;

            if (!active)
            {
                wrongTimer = rightTimer = 0f;
                SetWrongWay(false);
                return;
            }

            Vector3 heading = velocity.magnitude > movingSpeed ? velocity.normalized : Flat(transform.forward);
            float dot = Vector3.Dot(heading, here.Forward);

            if (dot < wrongWayDot) { wrongTimer += Time.deltaTime; rightTimer = 0f; }
            else if (dot > rightWayDot) { rightTimer += Time.deltaTime; wrongTimer = 0f; }

            if (!IsWrongWay && wrongTimer >= wrongWayDelay) SetWrongWay(true);
            else if (IsWrongWay && rightTimer >= rightWayDelay) SetWrongWay(false);
        }

        private void SetWrongWay(bool value)
        {
            if (IsWrongWay == value) return;
            IsWrongWay = value;
            WrongWayChanged?.Invoke(value);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
