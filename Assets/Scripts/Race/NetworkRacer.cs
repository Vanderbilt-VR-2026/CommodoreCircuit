using System.Collections.Generic;
using Fusion;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// Put on the Network Player prefab (next to FusionNetworkPlayer).
    /// Each client owns its own avatar in Shared mode, so each client writes its own
    /// race numbers here from its local <see cref="RaceProgress"/>, and every client
    /// reads everyone's to build the standings. No RPCs needed.
    /// (CommodoreCircuit > Race > Add NetworkRacer to Network Player Prefab does this.)
    /// </summary>
    public class NetworkRacer : NetworkBehaviour
    {
        public static readonly List<NetworkRacer> All = new List<NetworkRacer>();

        [Networked] public float Distance { get; set; }
        [Networked] public int CompletedLaps { get; set; }
        [Networked] public NetworkBool Finished { get; set; }
        [Networked] public float FinishTime { get; set; }
        [Networked] public float BestLap { get; set; }
        /// <summary>This player's race scene is loaded and their scooter is on the track.</summary>
        [Networked] public NetworkBool Ready { get; set; }

        public PlayerRef Player => Object ? Object.StateAuthority : PlayerRef.None;
        public bool IsLive => Object && Object.IsValid;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => All.Remove(this);

        private void OnDestroy() => All.Remove(this);

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var me = RaceProgress.Local;
            Ready = me != null && RaceTrack.Instance != null && RaceTrack.Instance.IsReady;
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
    }
}
