using Fusion;
using UnityEngine;

// Only the shooter moves this bullet and reports hits. NetworkTransform shares the movement.
[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
public class NetworkBullet : NetworkBehaviour
{
    [Min(0.1f)] public float Speed = 5f;
    [Min(0.1f)] public float Lifetime = 3f;
    [Min(0.001f)] public float Radius = 0.035f;
    public LayerMask HitLayers = ~0;

    [Networked] public PlayerRef Shooter { get; set; }
    [Networked] public NetworkBool HasHit { get; set; }
    private PlayerRef victim;
    private TickTimer expiry, reportRetry;
    private Renderer sphere;

    public override void Spawned()
    {
        sphere = GetComponentInChildren<Renderer>();
        if (HasStateAuthority)
        {
            Shooter = Object.StateAuthority;
            expiry = TickTimer.CreateFromSeconds(Runner, Lifetime);
        }
        if (sphere)
        {
            var color = new MaterialPropertyBlock();
            color.SetColor("_BaseColor", FusionNetworkPlayer.ColorForPlayer(Object.StateAuthority));
            color.SetColor("_Color", FusionNetworkPlayer.ColorForPlayer(Object.StateAuthority));
            sphere.SetPropertyBlock(color);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        var board = MatchScoreboard.Instance;
        if (!board || !board.Object.IsValid || expiry.Expired(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        if (HasHit)
        {
            // Keep the network object alive until the scorekeeper has processed its report.
            // Repeating the same report is safe: the scoreboard remembers accepted bullet IDs.
            if (board.AcceptedHits.ContainsKey(Object.Id)) Runner.Despawn(Object);
            else if (reportRetry.ExpiredOrNotRunning(Runner))
            {
                board.RPC_ReportHit(Object.Id, victim);
                reportRetry = TickTimer.CreateFromSeconds(Runner, 0.5f);
            }
            return;
        }

        // Check the starting overlap and the whole path so a fast sphere cannot skip a torso.
        Physics.SyncTransforms();
        float distance = Speed * Runner.DeltaTime;
        Collider closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (var overlap in Physics.OverlapSphere(transform.position, Radius, HitLayers, QueryTriggerInteraction.Collide))
            if (CanHit(overlap)) { closest = overlap; closestDistance = 0; break; }
        foreach (var hit in Physics.SphereCastAll(transform.position, Radius, transform.forward,
                     distance, HitLayers, QueryTriggerInteraction.Collide))
            if (hit.distance < closestDistance && CanHit(hit.collider))
            { closest = hit.collider; closestDistance = hit.distance; }

        if (!closest)
        {
            transform.position += transform.forward * distance;
            return;
        }

        var target = closest.GetComponentInParent<FusionNetworkPlayer>();
        if (!target) { Runner.Despawn(Object); return; } // Walls and tables stop bullets, but do not score.
        victim = target.Object.StateAuthority;
        HasHit = true;
        expiry = TickTimer.CreateFromSeconds(Runner, 5f); // Briefly allow delivery/retry before cleanup.
        board.RPC_ReportHit(Object.Id, victim);
        reportRetry = TickTimer.CreateFromSeconds(Runner, 0.5f);
    }

    private bool CanHit(Collider other)
    {
        if (other.GetComponentInParent<NetworkBullet>()) return false;
        var target = other.GetComponentInParent<FusionNetworkPlayer>();
        if (!target) return !other.isTrigger; // Ignore unrelated interaction triggers.
        // The existing Neck object is the torso. Ignore our own avatar, heads, hands and floor markers.
        return target.Object && target.Object.IsValid && target.Object.StateAuthority != Shooter &&
               target.Neck && other.transform.IsChildOf(target.Neck.transform);
    }

    public override void Render()
    {
        if (sphere) sphere.enabled = !HasHit;
    }
}
