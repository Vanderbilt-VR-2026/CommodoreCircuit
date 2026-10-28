using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

// One shared network prefab survives scene changes and coordinates the lobby and player avatars.
[RequireComponent(typeof(NetworkObject))]
public class FusionPlayerSpawnerAndLobbyController : NetworkBehaviour, IStateAuthorityChanged
{
    // Access this client's copy after Spawned; Instance is null before the network object arrives.
    public static FusionPlayerSpawnerAndLobbyController Instance;
    public NetworkObject PlayerPrefab;
    public int LobbySceneBuildIndex = 0, GameSceneBuildIndex = 1;
    // Set to 1 to test Lobby → race alone with one headset; set back to 2 for real races.
    [Min(1)] public int MinimumPlayers = 2;
    // Seconds from the host pressing Start Race to the track loading, so everyone sees it coming.
    [Min(0.1f)] public float StartDelay = 3f;
    public float PlayerSpawnSpacing = 0.7f;
    // Fusion replicates this deadline to clients; compare it with Runner.SimulationTime, not local wall time.
    [Networked] public float CountdownEndTime { get; set; } // Zero means waiting for the host.
    // Player IDs, not avatar objects: each array index is a numbered spawn position.
    // NetworkArray has fixed slots, so use Set(index, player), not List.Add(player).
    // PlayerRef.None marks an empty slot; Capacity(20) supports rooms up to 20 players.
    // When updated slots reach another client, it can try spawning its local avatar.
    [Networked, Capacity(20), OnChangedRender(nameof(SpawnPlayer))]
    public NetworkArray<PlayerRef> SpawnPlayers => default;
    // How far below our spawn height the headset may drop before we are put back on the spawn spot.
    [Min(0.5f)] public float FallResetDistance = 3f;
    private NetworkEvents events;
    private bool loadingScene;
    // This client's rig and spawn spot in the current scene, for the fall safety net. Null on the track.
    private XROrigin localRig;
    private Vector3 spawnPosition;

    // Fusion calls this on every client when the shared coordinator becomes available.
    public override async void Spawned()
    {
        Instance = this;
        events = Runner.GetComponent<NetworkEvents>();
        events.PlayerJoined.AddListener(PlayersChanged);
        events.PlayerLeft.AddListener(PlayersChanged);
        events.OnSceneLoadStart.AddListener(SceneLoading);
        events.OnSceneLoadDone.AddListener(SceneLoaded);
        // The master's scene is ready when it spawns this controller, but a joining client's
        // scene may still be loading. Wait here too, so the first avatar attempt is not lost.
        while (this && Runner.IsSceneManagerBusy) await Task.Yield();
        if (!this || !Runner.IsRunning) return;
        // Include players whose join events happened before this controller arrived.
        PlayersChanged(Runner, Runner.LocalPlayer);
    }

    // Both join and leave events use this method. Rebuilding from ActivePlayers also
    // handles initial setup and a new master taking over; the event's player is not needed.
    private void PlayersChanged(NetworkRunner runner, PlayerRef player)
    {
        // Only the coordinator's state authority (the master) edits the shared slots.
        if (HasStateAuthority)
        {
            // First: is this slot's player still in the room? If not, empty the slot.
            // Leave everyone else's index unchanged so their spawn positions stay stable.
            for (int i = 0; i < SpawnPlayers.Length; i++)
                if (!runner.ActivePlayers.Contains(SpawnPlayers[i])) SpawnPlayers.Set(i, PlayerRef.None);
            // Second: does this active player already have a slot? If not, assign one.
            // These Contains calls ask different questions: who left, and who needs a slot?
            foreach (var joinedPlayer in runner.ActivePlayers)
                if (!SpawnPlayers.Contains(joinedPlayer))
                    SpawnPlayers.Set(SpawnPlayers.IndexOf(PlayerRef.None), joinedPlayer);
        }
        // With only one player left in Game, the scene authority returns the room to Lobby.
        // A solo test (Minimum Players = 1) is allowed to race alone, so it stays.
        if (MinimumPlayers > 1 && runner.IsSceneAuthority && !loadingScene && !runner.IsSceneManagerBusy &&
            SceneManager.GetActiveScene().buildIndex == GameSceneBuildIndex && runner.ActivePlayers.Count() == 1)
        {
            loadingScene = true;
            runner.LoadScene(SceneRef.FromIndex(LobbySceneBuildIndex), LoadSceneMode.Single);
            return;
        }
        SpawnPlayer();
    }

    // If the master leaves, the new master also checks whether to return to Lobby.
    public void StateAuthorityChanged() => PlayersChanged(Runner, Runner.LocalPlayer);

    // Before leaving a scene, each client removes its own avatar; the shared controller stays alive.
    // The avatar follows this scene's XR rig; the next scene has a new rig to bind to.
    private void SceneLoading(NetworkRunner runner)
    {
        loadingScene = true; // Prevent spawning while the old scene is being replaced.
        var avatar = runner.GetPlayerObject(runner.LocalPlayer);
        // There may be no avatar yet during the initial scene load.
        // Despawn removes a network object for everyone; it does not disconnect the player.
        if (avatar) runner.Despawn(avatar);
    }

    // After loading, prepare a fresh lobby if needed and spawn our avatar using the new scene's rig.
    private async void SceneLoaded(NetworkRunner runner)
    {
        // Fusion raises this event just before its scene manager finishes loading.
        // Yield lets that work finish without blocking Unity, before we use the new XR rig.
        while (this && runner.IsSceneManagerBusy) await Task.Yield();
        if (!this || !runner.IsRunning) return; // We may have disconnected while waiting.
        loadingScene = false;
        if (HasStateAuthority && SceneManager.GetActiveScene().buildIndex == LobbySceneBuildIndex)
        {
            // Returning to Lobby starts a fresh wait, frees the old slots, and allows joins by code again.
            // The room stays invisible in public lists; only the code gets players in.
            CountdownEndTime = 0;
            SpawnPlayers.Clear();
            runner.SessionInfo.IsOpen = true;
        }
        // Refresh the slots and create our avatar in the new scene.
        // Other clients may receive their slot later; OnChangedRender retries for them.
        PlayersChanged(runner, runner.LocalPlayer);
    }

    // Create at most one avatar for the local player once its assigned slot and scene are ready.
    private void SpawnPlayer()
    {
        // IndexOf returns -1 if the master's slot assignment has not reached this client yet.
        int slot = SpawnPlayers.IndexOf(Runner.LocalPlayer);
        // Wait for our slot and scene, and never create a second local avatar.
        if (slot < 0 || loadingScene || Runner.IsSceneManagerBusy || Runner.GetPlayerObject(Runner.LocalPlayer)) return;
        // The campus track rides on the scooter's own camera rig, which has no XR Origin to follow yet.
        var rigObject = GameObject.Find("XR Origin (XR Rig)");
        if (!rigObject)
        {
            Debug.LogWarning($"No \"XR Origin (XR Rig)\" in {SceneManager.GetActiveScene().name}; no avatar spawned.", this);
            return;
        }
        var rig = rigObject.GetComponent<XROrigin>();
        // Spread numbered slots across the rig's right direction, centered on its starting point.
        float offset = (slot - (Runner.SessionInfo.MaxPlayers - 1) * 0.5f) * PlayerSpawnSpacing;
        // Project onto the horizontal plane so the spacing never pushes players upward or downward.
        Vector3 position = rig.Origin.transform.position +
            Vector3.ProjectOnPlane(rig.Origin.transform.right, Vector3.up).normalized * offset;
        position.y = rig.Camera.transform.position.y;
        // Move the rig so the headset reaches this position, accounting for its offset inside the rig.
        rig.MoveCameraToWorldLocation(position);
        localRig = rig;
        spawnPosition = position;
        // This spawn creates THIS CLIENT'S visible player avatar. Fusion shows it to everyone.
        // The manager's separate Spawn call creates the persistent shared controller instead.
        var avatar = Runner.Spawn(PlayerPrefab, rig.transform.position, rig.transform.rotation);
        // Remember which avatar belongs to us, so we can find it and avoid spawning duplicates.
        Runner.SetPlayerObject(Runner.LocalPlayer, avatar);
        Debug.Log($"Spawned avatar for {Runner.LocalPlayer} in {SceneManager.GetActiveScene().name}, slot {slot}.", this);
    }

    // Safety net: if our rig ever slips through a wall and falls out of the room, put it back on its spawn spot.
    // Unity object checks treat the rig as missing once its scene has been replaced.
    private void Update()
    {
        if (localRig && localRig.Camera.transform.position.y < spawnPosition.y - FallResetDistance)
            localRig.MoveCameraToWorldLocation(spawnPosition);
    }

    // The host is the coordinator's state authority: the lobby creator, or whoever Fusion
    // hands authority to if the creator leaves. Only the host may start the race.
    public bool IsHost => Object && Object.IsValid && HasStateAuthority;

    // True while the host is in the lobby, enough players are here, and no countdown is running yet.
    public bool CanStartRace => IsHost && !loadingScene && CountdownEndTime == 0 &&
        SceneManager.GetActiveScene().buildIndex == LobbySceneBuildIndex &&
        Runner.ActivePlayers.Count() >= MinimumPlayers;

    // The host's Start Race button calls this. The short shared countdown runs in FixedUpdateNetwork.
    public void StartRace()
    {
        if (CanStartRace) CountdownEndTime = Runner.SimulationTime + StartDelay;
    }

    // Pause menu, host only: reload the track for everyone, or bring everyone back to the classroom.
    public void RestartRace() => LoadSceneForEveryone(GameSceneBuildIndex);
    public void ReturnToLobby() => LoadSceneForEveryone(LobbySceneBuildIndex);

    private void LoadSceneForEveryone(int buildIndex)
    {
        if (!IsHost || loadingScene || Runner.IsSceneManagerBusy ||
            SceneManager.GetActiveScene().buildIndex != GameSceneBuildIndex) return;
        // The scene callbacks replace avatars; returning to Lobby also reopens the room to joins.
        loadingScene = true;
        Runner.LoadScene(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);
    }

    // The master runs the countdown the host started, then moves everyone to the race.
    public override void FixedUpdateNetwork()
    {
        // Zero means nobody has pressed Start Race; players can hang out in the lobby as long as they like.
        if (!HasStateAuthority || loadingScene || Runner.IsSceneManagerBusy || CountdownEndTime == 0 ||
            SceneManager.GetActiveScene().buildIndex != LobbySceneBuildIndex) return;
        // Cancel if players leave during the countdown; the host can press Start Race again.
        if (Runner.ActivePlayers.Count() < MinimumPlayers)
        {
            CountdownEndTime = 0;
            return;
        }
        if (Runner.SimulationTime < CountdownEndTime) return;
        loadingScene = true;
        // Stop new joins and move everyone together; the scene callbacks replace their avatars.
        Runner.SessionInfo.IsOpen = false;
        Runner.LoadScene(SceneRef.FromIndex(GameSceneBuildIndex), LoadSceneMode.Single);
    }

    // Fusion calls Render for visual updates: display shared room data without modifying it.
    public override void Render()
    {
        // The scene's UI references stay on the manager; the spawner supplies the room data.
        var manager = FusionNetworkManager.Instance;
        if (!manager) return;
        int count = Runner.ActivePlayers.Count();
        if (manager.PlayerCountText)
            manager.PlayerCountText.text = $"{count} / {Runner.SessionInfo.MaxPlayers} players joined";
        if (!manager.CountdownText) return;
        // Round remaining seconds up for the display and clamp at zero while the scene starts loading.
        if (CountdownEndTime > 0)
            manager.CountdownText.text = $"Race starts in {Mathf.CeilToInt(Mathf.Max(0, CountdownEndTime - Runner.SimulationTime))}";
        else if (count < MinimumPlayers)
            manager.CountdownText.text = $"Need {MinimumPlayers - count} more to start";
        else
            manager.CountdownText.text = IsHost ? "Press Start Race when everyone's ready" : "Waiting for the host to start...";
    }

    // Remove this coordinator's event subscriptions when Unity destroys it.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (!events) return;
        // Stop the runner from calling methods on a coordinator that has been destroyed.
        events.PlayerJoined.RemoveListener(PlayersChanged);
        events.PlayerLeft.RemoveListener(PlayersChanged);
        events.OnSceneLoadStart.RemoveListener(SceneLoading);
        events.OnSceneLoadDone.RemoveListener(SceneLoaded);
    }
}
