using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// The shared race clock for a networked race. Put one in the race scene on a
    /// NetworkObject with "Is Master Client Object" turned on (the race builder does this).
    ///
    /// Same pattern as MatchScoreboard: the master client moves the race from phase to
    /// phase and stores shared deadlines; every client draws its own UI from them.
    /// Times compare against Runner.SimulationTime, like the lobby countdown.
    ///
    ///   Waiting   -> every player's NetworkRacer reports Ready (or Max Wait runs out)
    ///   Countdown -> "Get ready", then 3-2-1
    ///   Racing    -> until everyone finishes, or Finish Grace after the first finisher
    ///   Results   -> standings, then back to the lobby after Results Seconds
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkRaceSession : NetworkBehaviour, IRaceSession
    {
        public static NetworkRaceSession Instance { get; private set; }

        [SerializeField, Min(1)] private int laps = 3;
        [Tooltip("'Get ready' time before 3-2-1 starts.")]
        [SerializeField, Min(0f)] private float getReadySeconds = 2.5f;
        [SerializeField, Min(1f)] private float countdownSeconds = 3f;
        [Tooltip("Start anyway after this long if someone's scene is slow to load.")]
        [SerializeField, Min(1f)] private float maxWaitForPlayers = 15f;
        [Tooltip("After the first racer finishes, everyone else has this long before they're marked DNF.")]
        [SerializeField, Min(5f)] private float finishGraceSeconds = 45f;
        [SerializeField, Min(3f)] private float resultsSeconds = 20f;
        [SerializeField] private bool returnToLobby = true;

        [Networked] private RacePhase NetPhase { get; set; }
        [Networked] private float WaitStart { get; set; }
        [Networked] private float GoTime { get; set; }
        [Networked] private float FirstFinishTime { get; set; }
        [Networked] private float ResultsStart { get; set; }
        [Networked] private int NetLaps { get; set; }

        private bool loadingLobby;
        private bool Live => Object && Object.IsValid;
        private float Now => Runner ? Runner.SimulationTime : 0f;

        // ---- IRaceSession ----
        public RacePhase Phase => Live ? NetPhase : RacePhase.Waiting;
        public int Laps => Live && NetLaps > 0 ? NetLaps : laps;
        public float CountdownSeconds => countdownSeconds;
        public float TimeUntilGo => Phase == RacePhase.Countdown ? GoTime - Now : 0f;
        public float RaceTime => Phase >= RacePhase.Racing ? Mathf.Max(0f, Now - GoTime) : 0f;
        public bool IsNetworked => true;
        public float ResultsTimeRemaining => Phase == RacePhase.Results && returnToLobby
            ? Mathf.Max(0f, ResultsStart + resultsSeconds - Now) : -1f;

        public override void Spawned()
        {
            Instance = this;
            RaceSession.Register(this, RaceSession.NetworkPriority);
            if (HasStateAuthority && NetPhase == RacePhase.Waiting && WaitStart <= 0f)
            {
                WaitStart = Now;
                NetLaps = laps;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            RaceSession.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            RaceSession.Unregister(this);
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            // Only the master advances the race. If the master leaves, Fusion hands this
            // object to the new master and it carries on from the stored deadlines.
            if (!HasStateAuthority) return;
            float now = Now;

            switch (NetPhase)
            {
                case RacePhase.Waiting:
                {
                    if (WaitStart <= 0f) WaitStart = now;
                    int players = Runner.ActivePlayers.Count();
                    int ready = Racers().Count(r => r.Ready);
                    if (players > 0 && (ready >= players || now - WaitStart >= maxWaitForPlayers))
                    {
                        GoTime = now + getReadySeconds + countdownSeconds;
                        NetPhase = RacePhase.Countdown;
                    }
                    break;
                }

                case RacePhase.Countdown:
                    if (now >= GoTime) NetPhase = RacePhase.Racing;
                    break;

                case RacePhase.Racing:
                {
                    int racing = 0, finished = 0;
                    foreach (var r in Racers())
                    {
                        racing++;
                        if (r.Finished) finished++;
                    }
                    if (finished > 0 && FirstFinishTime <= 0f) FirstFinishTime = now;
                    bool everyoneDone = racing > 0 && finished >= racing;
                    bool graceOver = FirstFinishTime > 0f && now - FirstFinishTime >= finishGraceSeconds;
                    if (everyoneDone || graceOver)
                    {
                        ResultsStart = now;
                        NetPhase = RacePhase.Results;
                    }
                    break;
                }

                case RacePhase.Results:
                    if (returnToLobby && !loadingLobby && now - ResultsStart >= resultsSeconds
                        && Runner.IsSceneAuthority && !Runner.IsSceneManagerBusy)
                    {
                        loadingLobby = true;
                        int lobby = FusionPlayerSpawnerAndLobbyController.Instance
                            ? FusionPlayerSpawnerAndLobbyController.Instance.LobbySceneBuildIndex : 0;
                        Runner.LoadScene(SceneRef.FromIndex(lobby), LoadSceneMode.Single);
                    }
                    break;
            }
        }

        /// <summary>Racers whose player is still in the room.</summary>
        private IEnumerable<NetworkRacer> Racers()
        {
            foreach (var r in NetworkRacer.All)
                if (r && r.IsLive && Runner.ActivePlayers.Contains(r.Player))
                    yield return r;
        }

        public void GetStandings(List<RaceStanding> into)
        {
            into.Clear();
            if (!Live) return;
            foreach (var r in Racers())
            {
                PlayerRef p = r.Player;
                into.Add(new RaceStanding
                {
                    DisplayName = $"Player {p.PlayerId}",
                    Color = FusionNetworkPlayer.ColorForPlayer(p),
                    IsLocal = p == Runner.LocalPlayer,
                    Finished = r.Finished,
                    FinishTime = r.FinishTime,
                    BestLap = r.BestLap,
                    CompletedLaps = r.CompletedLaps,
                    Distance = r.Distance,
                });
            }
            RaceSession.Rank(into);
        }

        public void RequestRestart()
        {
            // Over the network the master decides when to leave Results (back to the lobby).
        }
    }
}
