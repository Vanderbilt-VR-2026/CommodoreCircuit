using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// The Network Racer prefab, spawned once per player on the race track.
    /// Each client owns its own racer in Shared mode, so each client:
    ///   - puts its scooter on its own grid slot (the slot index the lobby already gave it),
    ///   - writes its race numbers here from its local <see cref="RaceProgress"/>,
    ///   - copies its scooter's pose onto this object and its headset onto <see cref="head"/>.
    /// NetworkTransforms on the root and the Head child send those poses to everyone else,
    /// where <see cref="RemoteRacerVisual"/> draws a scooter and rider in the player's color.
    /// Every client reads everyone's numbers to build the standings. No RPCs needed.
    /// (CommodoreCircuit > Race > Set Up Network Racer Prefab adds the Head and NetworkTransforms.)
    /// </summary>
    public class NetworkRacer : NetworkBehaviour
    {
        public static readonly List<NetworkRacer> All = new List<NetworkRacer>();

        [Tooltip("Child with its own NetworkTransform. The owner moves it to their headset.")]
        [SerializeField] private Transform head;

        [Networked] public float Distance { get; set; }
        [Networked] public int CompletedLaps { get; set; }
        [Networked] public NetworkBool Finished { get; set; }
        [Networked] public float FinishTime { get; set; }
        [Networked] public float BestLap { get; set; }
        /// <summary>This player's race scene is loaded and their scooter is on its grid slot.</summary>
        [Networked] public NetworkBool Ready { get; set; }

        public PlayerRef Player => Object ? Object.StateAuthority : PlayerRef.None;
        public bool IsLive => Object && Object.IsValid;

        private bool placedOnGrid;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            // Everyone else's racer gets a visible scooter and rider. Our own stays invisible:
            // we're already sitting on the real scooter. The lobby avatar also carries a
            // NetworkRacer, but it has its own body, so it's skipped.
            if (!HasStateAuthority && !GetComponent<FusionNetworkPlayer>() && !GetComponent<RemoteRacerVisual>())
                gameObject.AddComponent<RemoteRacerVisual>().Init(head, FusionNetworkPlayer.ColorForPlayer(Player));
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => All.Remove(this);

        private void OnDestroy() => All.Remove(this);

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var me = RaceProgress.Local;
            bool onTrack = me != null && RaceTrack.Instance != null && RaceTrack.Instance.IsReady;
            if (onTrack && !placedOnGrid) PlaceOnGrid(me);
            Ready = onTrack && placedOnGrid;
            if (me != null) FollowRider(me);
            if (me == null || Finished) return;   // once finished the result is final

            Distance = me.RaceDistance;
            CompletedLaps = me.CompletedLaps;
            BestLap = me.BestLapTime;
            if (me.Finished)
            {
                FinishTime = me.FinishTime;
                Finished = true;
            }
        }

        // Every client loads the same scene, so every scooter starts on the same start line.
        // Move ours to the grid slot matching our lobby slot, before the countdown.
        private void PlaceOnGrid(RaceProgress me)
        {
            placedOnGrid = true;
            // Joined after GO (the race didn't wait for us): don't teleport someone mid-race.
            var session = RaceSession.Current;
            if (session != null && session.Phase > RacePhase.Countdown) return;
            if (me.Scooter == null) return;

            var lobby = FusionPlayerSpawnerAndLobbyController.Instance;
            int slot = lobby ? lobby.SpawnPlayers.IndexOf(Runner.LocalPlayer) : 0;
            Pose pose = RaceTrack.Instance.GridPose(Mathf.Max(0, slot));
            me.Scooter.Teleport(pose.position, pose.rotation);
            Debug.Log($"[Race] {Runner.LocalPlayer} starts on grid slot {slot}.", this);
        }

        // Copy our scooter and headset onto this object; the NetworkTransforms do the rest.
        private void FollowRider(RaceProgress me)
        {
            Transform scooter = me.Scooter ? me.Scooter.transform : me.transform;
            transform.SetPositionAndRotation(scooter.position, scooter.rotation);

            Camera eyes = Camera.main;
            if (head && eyes) head.SetPositionAndRotation(eyes.transform.position, eyes.transform.rotation);
        }
    }
}
