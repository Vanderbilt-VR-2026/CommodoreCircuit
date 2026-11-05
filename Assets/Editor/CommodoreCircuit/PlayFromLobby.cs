using UnityEditor;
using UnityEditor.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Always Play From Lobby
    ///
    /// Play normally runs whichever scene is open, so pressing it inside the race
    /// scene skips the chalkboard. With this ticked, Play always starts in the
    /// first scene of the build list (the Lobby Scene), like the real game does.
    /// The setting is saved per computer, so it doesn't change anyone else's editor.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromLobby
    {
        private const string MenuPath = "CommodoreCircuit/Always Play From Lobby";
        private const string PrefKey = "CommodoreCircuit.PlayFromLobby";

        static PlayFromLobby() => EditorApplication.delayCall += Apply;

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(PrefKey, !EditorPrefs.GetBool(PrefKey, false));
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PrefKey, false));
            return true;
        }

        private static void Apply()
        {
            SceneAsset lobby = null;
            if (EditorPrefs.GetBool(PrefKey, false) && EditorBuildSettings.scenes.Length > 0)
                lobby = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);
            EditorSceneManager.playModeStartScene = lobby;
        }
    }
}
