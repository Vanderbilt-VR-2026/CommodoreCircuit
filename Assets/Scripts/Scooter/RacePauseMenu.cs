using System;
using System.Collections.Generic;
using CommodoreCircuit.Scooter;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Race pause menu. The left controller's menu button (☰) or Esc opens it in front of the rider.
// Left stick or arrow keys move the highlight; A, the right trigger or Enter chooses.
// Pausing freezes only your own scooter: in a multiplayer race everyone else keeps riding.
// It adds itself to any scene with a scooter, so the campus track needs no setup.
public class RacePauseMenu : MonoBehaviour
{
    private const float MenuDistance = 1.2f;
    // Bright Gold from Design/DESIGN_LANGUAGE_1.md.
    private static readonly Color HighlightColor = new Color32(0xFF, 0xC7, 0x2C, 0xFF);
    private const int MaxOptions = 3;

    private ScooterController scooter;
    private InputAction toggleAction, stickAction, upAction, downAction, selectAction;
    private GameObject panel;
    private readonly List<TMP_Text> optionTexts = new List<TMP_Text>();
    private readonly List<(string label, Action choose)> options = new List<(string, Action)>();
    private TMP_Text hintText;
    private int selected;
    private bool stickHeld;

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

        selectAction = new InputAction("MenuSelect", InputActionType.Button);
        selectAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}"); // A
        selectAction.AddBinding("<XRController>{RightHand}/{Trigger}");
        selectAction.AddBinding("<Keyboard>/enter");

        BuildPanel();
        panel.SetActive(false);
    }

    private void OnEnable()
    {
        toggleAction.Enable();
        stickAction.Enable();
        upAction.Enable();
        downAction.Enable();
        selectAction.Enable();
    }

    private void OnDisable()
    {
        toggleAction.Disable();
        stickAction.Disable();
        upAction.Disable();
        downAction.Disable();
        selectAction.Disable();
    }

    private void Update()
    {
        if (toggleAction.WasPressedThisFrame())
        {
            if (panel.activeSelf) Close();
            else Open();
            return;
        }
        if (!panel.activeSelf) return;

        // One step per stick push: it has to come back toward centre before it moves again.
        int step = 0;
        float y = stickAction.ReadValue<Vector2>().y;
        if (Mathf.Abs(y) > 0.6f)
        {
            if (!stickHeld) step = y > 0f ? -1 : 1;
            stickHeld = true;
        }
        else if (Mathf.Abs(y) < 0.3f)
        {
            stickHeld = false;
        }
        if (upAction.WasPressedThisFrame()) step = -1;
        if (downAction.WasPressedThisFrame()) step = 1;
        if (step != 0)
        {
            selected = (selected + step + options.Count) % options.Count;
            Refresh();
        }

        if (selectAction.WasPressedThisFrame())
        {
            Action choose = options[selected].choose;
            Close();
            choose();
        }
    }

    // Freeze our scooter and show the menu level in front of where the rider is looking.
    private void Open()
    {
        BuildOptions();
        selected = 0;
        Refresh();

        Transform head = Camera.main ? Camera.main.transform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        forward.Normalize();
        // A world-space canvas reads correctly when its forward points away from the viewer.
        panel.transform.SetPositionAndRotation(head.position + forward * MenuDistance, Quaternion.LookRotation(forward));
        panel.SetActive(true);

        // The scooter moves itself in FixedUpdate, so disabling it holds it exactly where it is.
        if (scooter) scooter.enabled = false;
    }

    private void Close()
    {
        panel.SetActive(false);
        if (scooter) scooter.enabled = true;
    }

    // Restart and Back to Lobby move the whole room, so only the host gets them online.
    private void BuildOptions()
    {
        options.Clear();
        options.Add(("Resume", () => { }));

        var controller = FusionPlayerSpawnerAndLobbyController.Instance;
        var manager = FusionNetworkManager.Instance;
        bool online = controller && manager && manager.State == FusionNetworkManager.LobbyState.InLobby;
        if (!online)
        {
            // Playing the track on its own in the Editor, without a lobby.
            options.Add(("Restart Race", () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex)));
            options.Add(("Back to Lobby", () => SceneManager.LoadScene(0)));
            hintText.text = "Left stick: move    A / trigger: select    Menu: resume";
        }
        else if (controller.IsHost)
        {
            options.Add(("Restart Race", controller.RestartRace));
            options.Add(("Back to Lobby", controller.ReturnToLobby));
            hintText.text = "You're the host: Restart and Back to Lobby apply to everyone.";
        }
        else
        {
            options.Add(("Leave Race", () => manager.ReturnToMenu("You left the race.")));
            hintText.text = "Only the host can restart the race or return everyone to the lobby.";
        }

        for (int i = 0; i < optionTexts.Count; i++)
            optionTexts[i].gameObject.SetActive(i < options.Count);
    }

    private void Refresh()
    {
        for (int i = 0; i < options.Count; i++)
        {
            optionTexts[i].text = (i == selected ? "> " : "") + options[i].label;
            optionTexts[i].color = i == selected ? HighlightColor : Color.white;
        }
    }

    // 500 x 320 canvas units at 2 mm each: a 1 m x 0.64 m dark panel.
    private void BuildPanel()
    {
        panel = new GameObject("Pause Canvas", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)panel.transform;
        rect.sizeDelta = new Vector2(500f, 320f);
        rect.localScale = Vector3.one * 0.002f;
        panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        CreateText("Title", "PAUSED", 110f, 44f);
        for (int i = 0; i < MaxOptions; i++)
            optionTexts.Add(CreateText("Option " + (i + 1), "", 40f - i * 50f, 30f));
        hintText = CreateText("Hint", "", -125f, 15f);
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
