using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using TMPro;
using UnityEngine;

// Put one in Game Scene and enable Is Master Client Object on its NetworkObject.
// The master writes scores; every client draws its own text from the synchronized values.
[RequireComponent(typeof(NetworkObject))]
public class MatchScoreboard : NetworkBehaviour
{
    public static MatchScoreboard Instance { get; private set; }
    public TMP_Text ScoreText;

    [Networked, Capacity(20)] public NetworkDictionary<PlayerRef, int> Scores => default;
    // An acknowledgement also prevents the same bullet from scoring twice, including after a master change.
    [Networked, Capacity(128)] public NetworkDictionary<NetworkId, NetworkBool> AcceptedHits => default;
    private readonly List<NetworkId> finishedBullets = new List<NetworkId>();
    private readonly StringBuilder display = new StringBuilder();

    public override void Spawned()
    {
        Instance = this;
        RegisterPlayers();
        DrawScores();
    }

    private void RegisterPlayers()
    {
        if (!HasStateAuthority) return;
        foreach (var player in Runner.ActivePlayers)
            if (!Scores.ContainsKey(player) && Scores.Count < Scores.Capacity) Scores.Add(player, 0);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        RegisterPlayers();
        // Once a bullet has despawned, it cannot submit another valid hit. Free its acknowledgement slot.
        finishedBullets.Clear();
        foreach (var hit in AcceptedHits)
            if (!Runner.TryFindObject(hit.Key, out _)) finishedBullets.Add(hit.Key);
        foreach (var id in finishedBullets) AcceptedHits.Remove(id);
    }

    // RPC = a request. All players may report; only THIS SCOREBOARD'S authority executes the method.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ReportHit(NetworkId bulletId, PlayerRef victim, RpcInfo info = default)
    {
        if (!HasStateAuthority || AcceptedHits.ContainsKey(bulletId)) return;
        if (!Runner.TryFindObject(bulletId, out var bulletObject)) return;
        var bullet = bulletObject.GetComponent<NetworkBullet>();
        var shooter = info.Source; // Fusion supplies the sender; do not trust a caller-supplied shooter ID.
        if (!bullet || bulletObject.StateAuthority != shooter || shooter == victim) return;
        if (!Runner.ActivePlayers.Contains(shooter) || !Runner.ActivePlayers.Contains(victim)) return;
        RegisterPlayers();
        if (!Scores.ContainsKey(shooter) || !Scores.ContainsKey(victim) || AcceptedHits.Count >= AcceptedHits.Capacity) return;

        // This teaching demo accepts the shooter's collision report. It is not server-verified combat.
        Scores.Set(shooter, Scores[shooter] + 1);
        Scores.Set(victim, Scores[victim] - 1);
        AcceptedHits.Add(bulletId, true);
    }

    public override void Render() => DrawScores();

    private void DrawScores()
    {
        if (!ScoreText) return;
        display.Clear();
        foreach (var player in Runner.ActivePlayers.OrderBy(p => p.PlayerId))
        {
            int score = Scores.TryGet(player, out var value) ? value : 0;
            string color = ColorUtility.ToHtmlStringRGB(FusionNetworkPlayer.ColorForPlayer(player));
            display.Append($"<color=#{color}>Player {player.PlayerId}</color>");
            if (player == Runner.LocalPlayer) display.Append(" (you)");
            display.Append($"    {score}\n");
        }
        string text = display.ToString();
        if (ScoreText.text != text) ScoreText.text = text;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
