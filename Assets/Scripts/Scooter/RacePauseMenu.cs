using System;
using System.Collections.Generic;
using CommodoreCircuit.Audio;
using CommodoreCircuit.Race;
using CommodoreCircuit.Scooter;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Race pause menu. The left controller's menu button (☰) or Esc opens it in front of the rider.
// Left stick or arrow keys move the highlight; A, the right trigger or Enter chooses.
// On the Music and Sound Effects rows, pushing left or right changes the volume (saved between launches).
// The Vignette row sets the comfort vignette (Off / Low / High, saved too) and previews it while highlighted.
// The View row switches between first-person and third-person (thumbstick steering), also saved.
// In a multiplayer race the pause is shared (NetworkRaceSession): pausing during the countdown or
// the race freezes everyone and stops the clock, so nobody gains time. Only the player who paused,
// or the host, can resume. Before the countdown and on the results screen it opens just for you.
// It adds itself to any scene with a scooter, so the campus track needs no setup.
public class RacePauseMenu : MonoBehaviour
{
    private const float MenuDistance = 1.2f;
    // Bright Gold from Design/DESIGN_LANGUAGE_1.md.
    private static readonly Color HighlightColor = new Color32(0xFF, 0xC7, 0x2C, 0xFF);
    private const int MaxOptions = 7;
    private const float VolumeStep = 0.1f;

    private ScooterController scooter;
    private InputAction toggleAction, stickAction, upAction, downAction, leftAction, rightAction, selectAction;
    private GameObject panel;
    private readonly List<TMP_Text> optionTexts = new List<TMP_Text>();
    // A row either does something when chosen, or (volume rows) changes with left/right: adjust gets -1 or +1.
    private readonly List<(Func<string> label, Action choose, Action<int> adjust)> options = new List<(Func<string>, Action, Action<int>)>();
    private TMP_Text hintText;
    private int selected;
    private bool stickHeldY, stickHeldX;
    // The menu is showing because the whole race is paused (it closes when the race resumes).
    private bool sharedMenu;
    // Solo only: our own race clock was stopped when the menu opened.
    private bool pausedClock;
    private int vignetteRow = -1;
    // After the view changes the camera moves a frame later, so keep re-placing the menu for a moment.
    private float replaceUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() => SceneManager.sceneLoaded += (scene, mode) =>
    {
        var scooter = FindAnyObjectByType<ScooterController>();
        if (!scooter || FindAnyObjectByType<RacePauseMenu>()) return;
        var menu = new GameObject("Race Pause Menu");
        // Keep it in the track's scene so it is removed along with the scooter on the next scene change.
        SceneManager.MoveGameObjectToScene(menu, scooter.gameObject.scene);
        menu.AddComponent<RacePauseMenu>().scooter = scooter;
    };

    private void Awake()
    {
        toggleAction = new InputAction("Pause", InputActionType.Button);
        toggleAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
        toggleAction.AddBinding("<Keyboard>/escape");

        // The scooter does not use the left stick, so it is free for the menu.
        stickAction = new InputAction("MenuStick", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
        upAction = new InputAction("MenuUp", InputActionType.Button, "<Keyboard>/upArrow");
        downAction = new InputAction("MenuDown", InputActionType.Button, "<Keyboard>/downArrow");
        leftAction = new InputAction("MenuLeft", InputActionType.Button, "<Keyboard>/leftArrow");
        rightAction = new InputAction("MenuRight", InputActionType.Button, "<Keyboard>/rightArrow");

        selectAction = new InputAction("MenuSelect", InputActionType.Button);
        selectAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}"); // A
        selectAction.AddBinding("<XRController>{RightHand}/{Trigger}");
        selectAction.AddBinding("<Keyboard>/enter");

        BuildPanel();
        panel.SetActive(false);
    }

    private void OnEnable()
    {
        ThirdPersonView.Changed += OnViewChanged;
        toggleAction.Enable();
        stickAction.Enable();
        upAction.Enable();
        downAction.Enable();
        leftAction.Enable();
        rightAction.Enable();
        selectAction.Enable();
    }

    private void OnDisable()
    {
        ThirdPersonView.Changed -= OnViewChanged;
        ThirdPersonView.Frozen = false;
        ComfortVignette.Preview = false;
        toggleAction.Disable();
        stickAction.Disable();
        upAction.Disable();
        downAction.Disable();
        leftAction.Disable();
        rightAction.Disable();
        selectAction.Disable();
    }

    private void Update()
    {
        // Everyone's menu follows the shared pause, whoever pressed it.
        var net = NetworkRaceSession.Instance;
        bool racePaused = net && net.IsPaused;
        if (racePaused && !sharedMenu) Open(shared: true);
        else if (!racePaused && sharedMenu) Close();

        if (toggleAction.WasPressedThisFrame())
        {
            if (sharedMenu)
            {
                if (net.CanResume) net.RequestPause(false);
            }
            else if (!panel.activeSelf && net && net.CanPause)
            {
                // The menu opens for everyone once the master pauses the race.
                net.RequestPause(true);
            }
            else if (panel.activeSelf) Close();
            else Open(shared: false);
            return;
        }
        if (!panel.activeSelf) return;
        if (sharedMenu) ShowPauseStatus(net);
        // Show the tunnel while the Vignette row is highlighted, so each level can be judged before resuming.
        ComfortVignette.Preview = selected == vignetteRow;

        Vector2 stick = stickAction.ReadValue<Vector2>();
        int step = StickStep(-stick.y, ref stickHeldY);
        if (upAction.WasPressedThisFrame()) step = -1;
        if (downAction.WasPressedThisFrame()) step = 1;
        if (step != 0)
        {
            selected = (selected + step + options.Count) % options.Count;
            Refresh();
        }

        int side = StickStep(stick.x, ref stickHeldX);
        if (leftAction.WasPressedThisFrame()) side = -1;
        if (rightAction.WasPressedThisFrame()) side = 1;
        if (side != 0 && options[selected].adjust != null)
        {
            options[selected].adjust(side);
            Refresh();
        }

        if (selectAction.WasPressedThisFrame() && options[selected].choose != null)
        {
            Action choose = options[selected].choose;
            // A shared pause closes by itself once the race resumes for everyone.
            if (!sharedMenu) Close();
            choose();
        }
    }

    // Switching between first and third person moves the camera, so bring an open menu along.
    private void OnViewChanged(bool thirdPerson) => replaceUntil = Time.unscaledTime + 0.5f;

    private void LateUpdate()
    {
        if (panel.activeSelf && Time.unscaledTime < replaceUntil) PlacePanel();
    }

    // One step per stick push: it has to come back toward centre before it moves again.
    private static int StickStep(float value, ref bool held)
    {
        int step = 0;
        if (Mathf.Abs(value) > 0.6f)
        {
            if (!held) step = value > 0f ? 1 : -1;
            held = true;
        }
        else if (Mathf.Abs(value) < 0.3f)
        {
            held = false;
        }
        return step;
    }

    // Freeze our scooter and show the menu level in front of where the rider is looking.
    private void Open(bool shared)
    {
        sharedMenu = shared;
        BuildOptions();
        selected = 0;
        Refresh();

        PlacePanel();
        panel.SetActive(true);
        // The third-person camera eases around after the scooter stops; hold it so the menu stays put.
        ThirdPersonView.Frozen = true;

        // The scooter moves itself in FixedUpdate, so disabling it holds it exactly where it is.
        if (scooter) scooter.enabled = false;
        // Solo, stop our own clock. Online the shared pause stops the race clock for everyone
        // instead, and a menu opened just for you (before GO, or on results) stops nothing.
        if (!pausedClock && !RaceSession.HasNetworkSession && RaceProgress.Local)
        {
            RaceProgress.Local.SetPaused(true);
            pausedClock = true;
        }
    }

    // Level in front of where the rider is looking.
    private void PlacePanel()
    {
        Transform head = Camera.main ? Camera.main.transform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        forward.Normalize();
        // A world-space canvas reads correctly when its forward points away from the viewer.
        panel.transform.SetPositionAndRotation(head.position + forward * MenuDistance, Quaternion.LookRotation(forward));
    }

    private void Close()
    {
        panel.SetActive(false);
        sharedMenu = false;
        ThirdPersonView.Frozen = false;
        ComfortVignette.Preview = false;
        if (scooter) scooter.enabled = true;
        if (pausedClock && RaceProgress.Local) RaceProgress.Local.SetPaused(false);
        pausedClock = false;
    }

    // "Player 2 paused the race. Resumes by itself in 42s."
    private void ShowPauseStatus(NetworkRaceSession net)
    {
        bool mine = net.PausedByPlayer == net.Runner.LocalPlayer;
        string status = mine ? "You paused the race for everyone." : $"Player {net.PausedByPlayer.PlayerId} paused the race.";
        float left = net.PauseTimeRemaining;
        if (left >= 0f) status += $" Resumes by itself in {Mathf.CeilToInt(left)}s.";
        if (!net.CanResume) status += "\nOnly they or the host can resume.";
        hintText.text = status;
    }

    // Restart and Back to Lobby move the whole room, so only the host gets them online.
    private void BuildOptions()
    {
        options.Clear();
        vignetteRow = -1;
        var net = NetworkRaceSession.Instance;
        if (!sharedMenu) AddChoice("Resume", () => { });
        else if (net.CanResume) AddChoice("Resume", () => net.RequestPause(false));

        var controller = FusionPlayerSpawnerAndLobbyController.Instance;
        var manager = FusionNetworkManager.Instance;
        bool online = controller && manager && manager.State == FusionNetworkManager.LobbyState.InLobby;
        if (!online)
        {
            // Playing the track on its own in the Editor, without a lobby.
            AddChoice("Restart Race", () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));
            AddChoice("Back to Lobby", () => SceneManager.LoadScene(0));
            hintText.text = "Left stick: move, left/right to change a setting    A / trigger: select    Menu: resume";
        }
        else if (controller.IsHost)
        {
            AddChoice("Restart Race", controller.RestartRace);
            AddChoice("Back to Lobby", controller.ReturnToLobby);
            hintText.text = "You're the host: Restart and Back to Lobby apply to everyone.";
        }
        else
        {
            AddChoice("Leave Race", () => manager.ReturnToMenu("You left the race."));
            hintText.text = "Only the host can restart the race or return everyone to the lobby.";
        }

        AddVolume("Music", GameAudio.Channel.Music);
        AddVolume("Sound Effects", GameAudio.Channel.Sfx);
        AddVignette();
        AddView();

        for (int i = 0; i < optionTexts.Count; i++)
            optionTexts[i].gameObject.SetActive(i < options.Count);
    }

    private void AddChoice(string label, Action choose) => options.Add((() => label, choose, null));

    // Shows as "Music   < 80% >" and moves in 10% steps.
    private void AddVolume(string label, GameAudio.Channel channel)
    {
        options.Add((
            () => $"{label}   < {Mathf.RoundToInt(GameAudio.GetVolume(channel) * 100f)}% >",
            null,
            side => GameAudio.SetVolume(channel, Mathf.Round((GameAudio.GetVolume(channel) + side * VolumeStep) * 10f) / 10f)));
    }

    // Shows as "View   < Third Person >". Left or right switches.
    private void AddView()
    {
        options.Add((
            () => $"View   < {(ThirdPersonView.Enabled ? "Third Person" : "First Person")} >",
            null,
            side => ThirdPersonView.SetEnabled(!ThirdPersonView.Enabled)));
    }

    // Shows as "Vignette   < High >". Off / Low / High; left/right steps and stops at the ends.
    private void AddVignette()
    {
        vignetteRow = options.Count;
        options.Add((
            () => $"Vignette   < {ComfortVignette.CurrentLevel} >",
            null,
            side => ComfortVignette.SetLevel((ComfortVignette.Level)Mathf.Clamp((int)ComfortVignette.CurrentLevel + side, 0, 2))));
    }

    private void Refresh()
    {
        for (int i = 0; i < options.Count; i++)
        {
            optionTexts[i].text = (i == selected ? "> " : "") + options[i].label();
            optionTexts[i].color = i == selected ? HighlightColor : Color.white;
        }
    }

    // 500 x 480 canvas units at 2 mm each: a 1 m x 0.96 m dark panel. Seven rows fit between the title and the hint.
    private void BuildPanel()
    {
        panel = new GameObject("Pause Canvas", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)panel.transform;
        rect.sizeDelta = new Vector2(500f, 480f);
        rect.localScale = Vector3.one * 0.002f;
        panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        CreateText("Title", "PAUSED", 205f, 44f);
        for (int i = 0; i < MaxOptions; i++)
            optionTexts.Add(CreateText("Option " + (i + 1), "", 140f - i * 40f, 28f));
        hintText = CreateText("Hint", "", -210f, 15f);
    }

    private TMP_Text CreateText(string name, string content, float y, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panel.transform, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        text.rectTransform.sizeDelta = new Vector2(470f, fontSize * 1.5f);
        return text;
    }
}
