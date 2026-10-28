using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// A one-player race with no networking, for testing in Campus Track Scene (or in
    /// the editor with the keyboard). If a <see cref="NetworkRaceSession"/> comes online,
    /// or a Fusion runner is already running, this one steps aside, so the same scene
    /// can hold both.
    /// </summary>
    public class LocalRaceSession : MonoBehaviour, IRaceSession
    {
        [SerializeField, Min(1)] private int laps = 3;
        [Tooltip("Seconds after the scene starts before 'Get ready' (gives the headset time to place the rider and handlebar).")]
        [SerializeField, Min(0f)] private float startDelay = 2.5f;
        [Tooltip("How long 'Get ready' shows before 3-2-1.")]
        [SerializeField, Min(0f)] private float getReadySeconds = 2f;
        [SerializeField, Min(1f)] private float countdownSeconds = 3f;
        [Tooltip("Wait this long for a network session before running solo.")]
        [SerializeField, Min(0f)] private float networkGraceSeconds = 1f;

        private RacePhase phase = RacePhase.Waiting;
        private float goTime = float.MaxValue;
        private float finishedAt = -1f;
        private bool active;

        public RacePhase Phase => phase;
        public int Laps => laps;
        public float CountdownSeconds => countdownSeconds;
        public float TimeUntilGo => phase == RacePhase.Countdown ? goTime - Time.time : 0f;
        public float RaceTime => phase >= RacePhase.Racing ? Time.time - goTime : 0f;
        public bool IsNetworked => false;
        public float ResultsTimeRemaining => -1f;

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(networkGraceSeconds);
            if (RaceSession.HasNetworkSession || FusionIsRunning())
            {
                enabled = false;
                yield break;
            }

            active = true;
            RaceSession.Register(this, RaceSession.SoloPriority);
            yield return new WaitForSeconds(Mathf.Max(0f, startDelay - networkGraceSeconds));
            if (!active) yield break;
            BeginCountdown();
        }

        private void OnDisable()
        {
            active = false;
            RaceSession.Unregister(this);
        }

        private void BeginCountdown()
        {
            goTime = Time.time + getReadySeconds + countdownSeconds;
            phase = RacePhase.Countdown;
        }

        private void Update()
        {
            if (!active) return;
            if (RaceSession.HasNetworkSession)
            {
                // A network race arrived late; let it run the show.
                enabled = false;
                return;
            }

            switch (phase)
            {
                case RacePhase.Countdown:
                    if (Time.time >= goTime) phase = RacePhase.Racing;
                    break;
                case RacePhase.Racing:
                    var me = RaceProgress.Local;
                    if (me && me.Finished)
                    {
                        if (finishedAt < 0f) finishedAt = Time.time;
                        phase = RacePhase.Results;
                    }
                    break;
            }
        }

        public void GetStandings(List<RaceStanding> into)
        {
            into.Clear();
            var me = RaceProgress.Local;
            if (!me) return;
            into.Add(new RaceStanding
            {
                DisplayName = "You",
                Color = new Color(1f, 0.78f, 0.17f),
                IsLocal = true,
                Finished = me.Finished,
                FinishTime = me.FinishTime,
                BestLap = me.BestLapTime,
                CompletedLaps = me.CompletedLaps,
                Distance = me.RaceDistance,
            });
            RaceSession.Rank(into);
        }

        /// <summary>Reloads the scene for a fresh race.</summary>
        public void RequestRestart()
        {
            if (!active) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            // Campus Track Scene isn't in Build Settings, which LoadScene needs; the editor can still reload it.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Debug.LogWarning("[Race] Add this scene to Build Settings to restart it.");
#endif
        }

        private static bool FusionIsRunning()
        {
            foreach (var runner in NetworkRunner.Instances)
                if (runner && runner.IsRunning) return true;
            return false;
        }
    }
}
