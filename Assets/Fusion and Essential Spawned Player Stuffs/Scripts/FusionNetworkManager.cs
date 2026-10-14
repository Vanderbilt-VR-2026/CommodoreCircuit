using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;

// Creates or joins a room by lobby code and sets up the persistent controller, UI, and voice.
[RequireComponent(typeof(NetworkRunner), typeof(NetworkEvents), typeof(NetworkSceneManagerDefault))]
[RequireComponent(typeof(Recorder), typeof(FusionVoiceClient), typeof(VoiceLogger))]
public class FusionNetworkManager : MonoBehaviour
{
    // Menu: not connected yet. Connecting: StartGame is running. InLobby: in a room with a code.
    public enum LobbyState { Menu, Connecting, InLobby }
    // Codes skip I, L, O, 0 and 1 so nobody mixes them up when reading the board aloud.
    public const string CodeCharacters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 4;
    // Room property set only by the creator. Shared mode joins OR creates by name, so a joiner
    // that lands in a room without it has just created an empty room: the code was wrong.
    private const string HostKey = "host";
    // A local shortcut to the surviving manager; this static reference is not networked.
    public static FusionNetworkManager Instance;
    // Why we last returned to the menu, e.g. "That lobby is full." Static, so it survives the scene reload.
    public static string MenuMessage;
    [Range(2, 20)] public int MaxPlayers = 4;
    public NetworkObject PlayerSpawnerPrefab;
    public TMP_Text StatusText, PlayerCountText, CountdownText;
    public Transform LobbyCanvas;
    public Camera LobbyCamera;
    [Min(0.1f)] public float LobbyCanvasDistance = 1f;
    [UnityEngine.Serialization.FormerlySerializedAs("LobbyCanvasHeight")]
    [Min(0f)] public float LobbyCanvasHeightAboveGround = 1f;
    public string Status { get; private set; } = "";
    public LobbyState State { get; private set; } = LobbyState.Menu;
    public string LobbyCode { get; private set; } = "";
    private NetworkRunner runner;
    private Recorder recorder;
    private FusionVoiceClient voice;
    private bool microphoneAllowed;
    private bool leaving;
    private bool lobbyCanvasPlaced;
    private int lobbySceneIndex;

    // Unity calls Awake before Start: prepare the local connection, UI, and voice components.
    private void Awake()
    {
        if (PlayerCountText) PlayerCountText.text = $"0 / {MaxPlayers} players joined";
        if (CountdownText) CountdownText.text = "Waiting for players...";
        if (Instance && Instance != this)
        {
            // Returning to Lobby creates fresh UI. Keep the running manager,
            // but give it this scene's Inspector references before removing the duplicate.
            Instance.StatusText = StatusText;
            Instance.PlayerCountText = PlayerCountText;
            Instance.CountdownText = CountdownText;
            Instance.LobbyCanvas = LobbyCanvas;
            Instance.LobbyCamera = LobbyCamera;
            Instance.LobbyCanvasDistance = LobbyCanvasDistance;
            Instance.LobbyCanvasHeightAboveGround = LobbyCanvasHeightAboveGround;
            Instance.lobbyCanvasPlaced = false; // Place the newly loaded lobby panel once.
            Instance.SetStatus(Instance.Status);
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        lobbySceneIndex = gameObject.scene.buildIndex;
        // Keep this client's connection and voice components alive when Lobby becomes Game.
        DontDestroyOnLoad(gameObject);
        runner = GetComponent<NetworkRunner>();
        recorder = GetComponent<Recorder>();
        voice = GetComponent<FusionVoiceClient>();
        // Tell Photon Voice which microphone recorder supplies this client's outgoing audio.
        voice.PrimaryRecorder = recorder;
        // Voice is optional: no Voice App ID means no microphone request or voice connection.
        voice.AutoConnectAndJoin = !string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdVoice);
        // LateUpdate enables recording only when permission and both connections are ready.
        recorder.RecordWhenJoined = false;
        recorder.RecordingEnabled = false;
        if (voice.AutoConnectAndJoin) StartCoroutine(RequestMicrophonePermission());
    }

    // Subscribe to Fusion events. Nothing connects until LobbyMenu calls CreateLobby or JoinLobby.
    private void Start()
    {
        // NetworkEvents raises these callbacks when something happens in the room.
        // Each lambda (r => ...) is the small action to run when that event fires.
        var events = GetComponent<NetworkEvents>();
        events.OnConnectedToServer.AddListener(r => SetStatus("Connected. Joining lobby..."));
        events.PlayerJoined.AddListener((r, player) => SetStatus($"Player {player.PlayerId} joined. Waiting for others..."));
        events.PlayerLeft.AddListener((r, player) => SetStatus($"Player {player.PlayerId} left."));
        events.OnSceneLoadDone.AddListener(r =>
        {
            if (State == LobbyState.InLobby && SceneManager.GetActiveScene().buildIndex == lobbySceneIndex)
                SetStatus("Waiting for others to join...");
        });
        events.OnConnectFailed.AddListener((r, address, reason) => ReturnToMenu($"Connection failed: {reason}."));
        events.OnDisconnectedFromServer.AddListener((r, reason) => ReturnToMenu($"Disconnected: {reason}."));
        events.OnShutdown.AddListener((r, reason) => ReturnToMenu(Describe(reason)));
        if (!PlayerSpawnerPrefab) SetStatus("Assign the Fusion Player Spawner and Lobby Controller prefab.");
    }

    // Make a new room with a random code. The code is shown on the board for others to type in.
    public void CreateLobby()
    {
        var code = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++) code[i] = CodeCharacters[Random.Range(0, CodeCharacters.Length)];
        StartLobby(new string(code), true);
    }

    // Join an existing room. A code with no room behind it returns to the menu with a message.
    public void JoinLobby(string code) => StartLobby(code.Trim().ToUpperInvariant(), false);

    // Leave the room and go back to the Create / Join menu.
    public void LeaveLobby() => ReturnToMenu("You left the lobby.");

    // Join or create the Fusion room named by the code, then let the master spawn the shared controller once.
    private async void StartLobby(string code, bool create)
    {
        // Fusion runners are single-use, so only start from the menu.
        if (State != LobbyState.Menu || leaving || !PlayerSpawnerPrefab) return;
        State = LobbyState.Connecting;
        LobbyCode = code;
        MenuMessage = null;
        SetStatus(create ? $"Creating lobby {code}..." : $"Joining lobby {code}...");
        // The creator stamps the room with a random number to recognise it as its own afterwards.
        int hostId = Random.Range(1, int.MaxValue);
        // Shared mode lets each client control its own avatar. The code is the session name;
        // PlayerCount sets its capacity, and the scene manager synchronizes scene loading.
        // IsVisible = false keeps the room out of public lists, so the code is the only way in.
        var startGameArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = code,
            PlayerCount = MaxPlayers,
            IsVisible = false,
            SessionProperties = create ? new Dictionary<string, SessionProperty> { [HostKey] = hostId } : null,
            Scene = SceneRef.FromIndex(lobbySceneIndex),
            SceneManager = GetComponent<NetworkSceneManagerDefault>()
        };
        try
        {
            // Join/create the Fusion room. StartGame does not spawn a player avatar.
            var result = await runner.StartGame(startGameArgs);
            // After an await, this component may have been destroyed or we may be leaving.
            if (!this || leaving) return;
            if (!result.Ok)
            {
                ReturnToMenu(Describe(result.ShutdownReason));
                return;
            }
            // Did we land in the room we meant to? A creator must find its own number,
            // and a joiner must find a creator's number, not an empty room it just made.
            bool hasHost = runner.SessionInfo.Properties.TryGetValue(HostKey, out var host) && host.IsInt;
            if (create && (!hasHost || (int)host.PropertyValue != hostId))
            {
                ReturnToMenu($"Code {code} was already in use. Press Create Lobby again.");
                return;
            }
            if (!create && !hasHost)
            {
                ReturnToMenu($"No lobby with code {code}. Check the code and try again.");
                return;
            }
            // Joining may finish before the initial scene is ready. Yield without freezing Unity.
            while (this && runner.IsSceneManagerBusy) await Task.Yield();
            if (!this || leaving) return;
            State = LobbyState.InLobby;
            SetStatus("Waiting for others to join...");
            // Spawn ONE shared controller, not a player avatar. Fusion sends a copy to every client.
            // The flags keep it across scenes and transfer authority if the master leaves.
            // The controller separately spawns each client's PlayerPrefab.
            if (runner.IsSharedModeMasterClient && !FusionPlayerSpawnerAndLobbyController.Instance)
                runner.Spawn(PlayerSpawnerPrefab, flags: NetworkSpawnFlags.DontDestroyOnLoad |
                    NetworkSpawnFlags.SharedModeStateAuthMasterClient);
        }
        catch (System.Exception error) { if (this) ReturnToMenu(error.Message); }
    }

    // Turn Fusion's shutdown reason into a message a player understands.
    private static string Describe(ShutdownReason reason) => reason switch
    {
        ShutdownReason.GameNotFound => "No lobby with that code. Check the code and try again.",
        ShutdownReason.GameIsFull => "That lobby is full.",
        ShutdownReason.GameClosed => "That lobby has already started its race.",
        ShutdownReason.ConnectionRefused or ShutdownReason.ConnectionTimeout or ShutdownReason.OperationTimeout or
            ShutdownReason.PhotonCloudTimeout => "Could not reach the server. Check the internet and try again.",
        _ => $"Disconnected: {reason}."
    };

    // Keep the latest connection message, show it when the lobby UI exists, and log it for debugging.
    public void SetStatus(string message)
    {
        Status = message;
        if (StatusText) StatusText.text = message;
        Debug.Log(message, this);
    }

    // Refresh voice each frame, and place each new lobby panel once after the camera updates.
    private void LateUpdate()
    {
        if (!runner) return;
        // Recording begins only after permission and both the game and voice connections are ready.
        recorder.RecordingEnabled = !leaving && microphoneAllowed && runner.IsRunning && voice.Client.InRoom;
        // Game has no lobby panel. Unity object checks also detect references destroyed by a scene change.
        if (!LobbyCanvas || !LobbyCamera || lobbyCanvasPlaced) return;
        // Use only the headset's left/right heading; looking up or down must not move the panel vertically.
        // Place it once, then let CanvasController turn it toward the camera without moving it.
        var cameraTransform = LobbyCamera.transform;
        var heading = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
        var position = cameraTransform.position + heading * Vector3.forward * LobbyCanvasDistance;
        position.y = LobbyCanvasHeightAboveGround; // Height above this scene's floor at world y = 0.
        LobbyCanvas.SetPositionAndRotation(position, heading);
        lobbyCanvasPlaced = true;
    }

    // Leave the room (on purpose or after a failure) and reload Lobby, which opens on the menu.
    public async void ReturnToMenu(string message)
    {
        if (leaving) return; // Several callbacks can report the same failure.
        leaving = true;
        // The menu in the reloaded scene shows this, e.g. "That lobby is full."
        MenuMessage = message;
        SetStatus(message);
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
        // Finish network cleanup first; we explicitly destroy the manager GameObject below.
        try { await runner.Shutdown(destroyGameObject: false); }
        catch (System.Exception error) { Debug.LogException(error); }
        if (!this) return;
        // Fusion runners are single-use. Reload Lobby to get a fresh manager and runner at the menu.
        Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(lobbySceneIndex);
    }

    // Unity calls this when the component becomes inactive: stop recording and disconnect voice.
    private void OnDisable()
    {
        if (!recorder || !voice) return;
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
    }

    // Ask the operating system for microphone access. This coroutine can wait for the user
    // without blocking the game; #if selects the code compiled for the current platform.
    private IEnumerator RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
        if (!microphoneAllowed)
        {
            // Android reports approval later through this callback. Denial leaves recording disabled.
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += permission => { if (this) microphoneAllowed = true; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
        }
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // On Apple platforms, wait for the permission request to finish, then read the result.
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        // Other platforms use their normal microphone access; this script makes no permission request.
        microphoneAllowed = true;
#endif
        yield break;
    }

    // Recheck permission when the user returns, for example after changing it in device settings.
    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !recorder) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
    }

    // Clear the shortcut only if THIS manager owns it; a discarded duplicate must not clear it.
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
