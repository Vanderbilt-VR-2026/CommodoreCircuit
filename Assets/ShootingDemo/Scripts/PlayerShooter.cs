using Fusion;
using UnityEngine;

// Add this to Network Player. The existing FusionNetworkPlayer supplies hands and input actions.
[RequireComponent(typeof(FusionNetworkPlayer))]
public class PlayerShooter : NetworkBehaviour
{
    public NetworkObject BulletPrefab;
    [Min(0.05f)] public float ShotDelay = 0.25f;
    [Min(0f)] public float MuzzleDistance = 0.12f;

    private FusionNetworkPlayer player;
    private bool fireLeft, fireRight;
    private TickTimer leftCooldown, rightCooldown;

    private void Awake() => player = GetComponent<FusionNetworkPlayer>();

    // Remember short presses between network ticks. Holding a trigger does not repeatedly fire.
    private void Update()
    {
        if (!Object || !Object.IsValid || !HasStateAuthority) return;
        if (!MatchScoreboard.Instance || !MatchScoreboard.Instance.Object.IsValid)
        {
            fireLeft = fireRight = false; // There is no scoreboard in Lobby, so no shooting there.
            return;
        }
        fireLeft |= player.LeftActivateAction.action.WasPressedThisFrame();
        fireRight |= player.RightActivateAction.action.WasPressedThisFrame();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !MatchScoreboard.Instance || !BulletPrefab) return;
        if (fireLeft && leftCooldown.ExpiredOrNotRunning(Runner))
        {
            Fire(player.leftHand);
            leftCooldown = TickTimer.CreateFromSeconds(Runner, ShotDelay);
        }
        if (fireRight && rightCooldown.ExpiredOrNotRunning(Runner))
        {
            Fire(player.rightHand);
            rightCooldown = TickTimer.CreateFromSeconds(Runner, ShotDelay);
        }
        fireLeft = fireRight = false;
    }

    private void Fire(Transform hand)
    {
        // Shared Mode gives the spawning player authority over this bullet. No transfer is needed.
        Runner.Spawn(BulletPrefab, hand.position + hand.forward * MuzzleDistance, hand.rotation);
    }
}
