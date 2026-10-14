using TMPro;
using UnityEngine;

// The chalkboard menu: Create Lobby or Join Lobby with a code, then the lobby's status and code.
// Buttons call these methods through their On Click lists; pressing them with a finger works
// because the canvas has a Tracked Device Graphic Raycaster and the rig has Poke Interactors.
public class LobbyMenu : MonoBehaviour
{
    // MainPanel: Create / Join. JoinPanel: code keypad. LobbyPanel: code, player count, countdown.
    public GameObject MainPanel, JoinPanel, LobbyPanel;
    public TMP_Text MainMessageText, CodeEntryText, JoinMessageText, LobbyCodeText;
    private string typedCode = "";

    // The scene loads on the menu, unless it was reloaded while already connecting or in a lobby.
    private void Start()
    {
        ShowMain();
        Update();
    }

    // Swap between the menu and the lobby display as the network manager's state changes.
    private void Update()
    {
        var manager = FusionNetworkManager.Instance;
        bool inLobby = manager && manager.State != FusionNetworkManager.LobbyState.Menu;
        if (inLobby)
        {
            MainPanel.SetActive(false);
            JoinPanel.SetActive(false);
            LobbyCodeText.text = $"Lobby code: {manager.LobbyCode}";
        }
        LobbyPanel.SetActive(inLobby);
    }

    // Show Create / Join, with the reason we came back here (e.g. "That lobby is full.") if any.
    public void ShowMain()
    {
        MainPanel.SetActive(true);
        JoinPanel.SetActive(false);
        MainMessageText.text = FusionNetworkManager.MenuMessage ?? "";
    }

    // Show the keypad with an empty code.
    public void ShowJoin()
    {
        MainPanel.SetActive(false);
        JoinPanel.SetActive(true);
        typedCode = "";
        JoinMessageText.text = "";
        RefreshCode();
    }

    // Each keypad key passes its own character, set in that button's On Click list.
    public void AddCharacter(string character)
    {
        if (typedCode.Length >= FusionNetworkManager.CodeLength) return;
        typedCode += character;
        JoinMessageText.text = "";
        RefreshCode();
    }

    public void DeleteCharacter()
    {
        if (typedCode.Length == 0) return;
        typedCode = typedCode.Substring(0, typedCode.Length - 1);
        RefreshCode();
    }

    public void CreateLobby()
    {
        if (FusionNetworkManager.Instance) FusionNetworkManager.Instance.CreateLobby();
    }

    public void JoinLobby()
    {
        if (typedCode.Length < FusionNetworkManager.CodeLength)
        {
            JoinMessageText.text = $"Enter all {FusionNetworkManager.CodeLength} characters.";
            return;
        }
        if (FusionNetworkManager.Instance) FusionNetworkManager.Instance.JoinLobby(typedCode);
    }

    public void LeaveLobby()
    {
        if (FusionNetworkManager.Instance) FusionNetworkManager.Instance.LeaveLobby();
    }

    // Show typed characters with blanks for the rest, e.g. "A 7 _ _".
    private void RefreshCode()
    {
        string padded = typedCode.PadRight(FusionNetworkManager.CodeLength, '_');
        CodeEntryText.text = string.Join(" ", padded.ToCharArray());
    }
}
