using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Builds the chalkboard lobby: a Create / Join menu with a code keypad, then the
    /// lobby code, status line, "2 / 4 players joined" and countdown, in a chalk-style font.
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Set Up Chalkboard Lobby Display" -
    ///     1. Imports TMP Essential Resources if they are missing. Without them every
    ///        TextMeshPro text has no font and draws nothing. The command re-runs
    ///        itself once the import finishes.
    ///     2. Creates Assets/Fonts/CabinSketch-Bold SDF.asset from CabinSketch-Bold.ttf
    ///        (OFL licence in CabinSketch-OFL.txt) with a chalk-white material.
    ///        An existing asset is reused, so tweaks to its material are kept.
    ///     3. In Lobby Scene: moves the world-space Canvas flat onto the Whiteboard
    ///        surface (1 canvas unit = 1 cm, 400 x 150 = the 4 m x 1.5 m board), removes
    ///        CanvasController so it stops turning toward the camera, sets Max Players to 4
    ///        and clears the manager's Lobby Canvas field so it no longer moves the canvas.
    ///     4. Builds the LobbyMenu panels: Main (Create / Join), Join (keypad, Join, Back)
    ///        and the lobby display in Waiting Room Panel (code, Leave). Adds a Tracked Device
    ///        Graphic Raycaster so the rig's Poke Interactors can press the buttons by finger.
    ///     5. Fixes the Photon region to "us" if none is set, so everyone's codes refer to
    ///        rooms on the same server (rooms in different regions cannot see each other).
    ///     Running it again rebuilds the menu and re-applies the same layout.
    ///     There is no Undo across scene loads; use git to revert.
    /// </summary>
    public static class ChalkboardLobbySetup
    {
        private const string LobbyPath = "Assets/Scenes/Lobby Scene.unity";
        private const string FontPath = "Assets/Fonts/CabinSketch-Bold.ttf";
        private const string FontAssetPath = "Assets/Fonts/CabinSketch-Bold SDF.asset";
        private const string EssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        // Whiteboard centre is (0, 1.6, 3.95); its surface's front face is at z = 3.92, facing -Z into the room.
        // Canvas y = 0 is 1.6 m above the floor and y = -60 is 1.0 m, so buttons sit at hand height.
        private static readonly Vector3 BoardCanvasPosition = new Vector3(0f, 1.6f, 3.9f);
        private static readonly Vector2 BoardCanvasSize = new Vector2(400f, 150f);
        private const int MaxPlayers = 4;
        private const string Region = "us";
        private static readonly Color ChalkColor = new Color32(0xF2, 0xEF, 0xE6, 0xFF);
        private const string MainPanelName = "Main Menu Panel";
        private const string JoinPanelName = "Join Panel";

        [MenuItem("CommodoreCircuit/Set Up Chalkboard Lobby Display")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode", "Stop Play Mode and run this command again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                // ImportPackage finishes asynchronously; carry on once its assets exist.
                AssetDatabase.importPackageCompleted += ContinueAfterImport;
                AssetDatabase.ImportPackage(Path.GetFullPath(EssentialsPackage), false);
                return;
            }

            TMP_FontAsset chalkFont = GetOrCreateChalkFont();
            if (chalkFont == null)
                return;

            SetUpLobbyScene(chalkFont);
            FixPhotonRegion();
        }

        private static void ContinueAfterImport(string packageName)
        {
            AssetDatabase.importPackageCompleted -= ContinueAfterImport;
            Debug.Log("ChalkboardLobbySetup: imported TMP Essential Resources.");
            EditorApplication.delayCall += Run;
        }

        private static TMP_FontAsset GetOrCreateChalkFont()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
                return existing;

            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                EditorUtility.DisplayDialog("Font not found", $"{FontPath} is missing. Nothing was changed.", "OK");
                return null;
            }

            // Dynamic keeps the .ttf linked, so any character not pre-baked below is still added when shown.
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            fontAsset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
            // Bake printable ASCII now so the board does not stutter adding glyphs on the headset.
            string ascii = "";
            for (char c = ' '; c <= '~'; c++)
                ascii += c;
            fontAsset.TryAddCharacters(ascii);

            Material material = fontAsset.material;
            material.name = fontAsset.name + " Material";
            material.SetColor(ShaderUtilities.ID_FaceColor, ChalkColor);
            // A little dilate and softness gives a dusty chalk edge instead of crisp vector text.
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);
            material.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.05f);

            Texture2D atlas = fontAsset.atlasTexture;
            atlas.name = fontAsset.name + " Atlas";

            // The atlas and material live inside the font asset, as TMP's Font Asset Creator saves them.
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            AssetDatabase.AddObjectToAsset(material, fontAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"ChalkboardLobbySetup: created {FontAssetPath}.");
            return fontAsset;
        }

        private static void SetUpLobbyScene(TMP_FontAsset chalkFont)
        {
            Scene lobby = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Single);

            FusionNetworkManager manager = Object.FindFirstObjectByType<FusionNetworkManager>(FindObjectsInactive.Include);
            if (manager == null || manager.PlayerCountText == null)
            {
                EditorUtility.DisplayDialog("Lobby UI not found",
                    $"The Fusion Network Manager or its Player Count Text is missing from {LobbyPath}. Nothing was changed.", "OK");
                return;
            }

            Canvas canvas = manager.PlayerCountText.GetComponentInParent<Canvas>(true).rootCanvas;

            // CanvasController would keep turning the board text toward the player.
            CanvasController turner = canvas.GetComponent<CanvasController>();
            if (turner != null)
                Object.DestroyImmediate(turner);

            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.SetPositionAndRotation(BoardCanvasPosition, Quaternion.identity);
            canvasRect.sizeDelta = BoardCanvasSize;
            canvasRect.localScale = Vector3.one * 0.01f;

            // XR pokes and rays reach UI through this raycaster; the plain GraphicRaycaster stays for the mouse.
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            // The texts sit under one panel; stretch it over the canvas at normal scale.
            var lobbyPanel = (RectTransform)manager.PlayerCountText.transform.parent;
            if (lobbyPanel == canvasRect)
            {
                EditorUtility.DisplayDialog("Lobby panel not found",
                    "Player Count Text should be inside a panel under the Canvas. Nothing was changed.", "OK");
                return;
            }
            Stretch(lobbyPanel);
            // Let the green board show through instead of a panel background.
            Image background = lobbyPanel.GetComponent<Image>();
            if (background != null)
                background.enabled = false;

            LayOut(manager.StatusText, chalkFont, new Vector2(0f, 30f), new Vector2(380f, 20f), 12f);
            LayOut(manager.PlayerCountText, chalkFont, new Vector2(0f, 0f), new Vector2(380f, 40f), 36f);
            LayOut(manager.CountdownText, chalkFont, new Vector2(0f, -32f), new Vector2(380f, 28f), 20f);

            BuildMenu(canvas, lobbyPanel, chalkFont);

            manager.MaxPlayers = MaxPlayers;
            // With no Lobby Canvas, the manager's LateUpdate leaves the board canvas where it is.
            manager.LobbyCanvas = null;
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
            Selection.activeGameObject = canvas.gameObject;

            Debug.Log($"ChalkboardLobbySetup: chalkboard menu and lobby display built in {chalkFont.name}, Max Players = {MaxPlayers}.");
        }

        // Builds the Create / Join and keypad panels, adds the code and Leave button to the lobby panel,
        // and wires every button to LobbyMenu so the On Click lists show what each one does.
        private static void BuildMenu(Canvas canvas, RectTransform lobbyPanel, TMP_FontAsset font)
        {
            // Remove what an earlier run built, so the menu is not stacked twice.
            DestroyChild(canvas.transform, MainPanelName);
            DestroyChild(canvas.transform, JoinPanelName);
            DestroyChild(lobbyPanel, "Lobby Code Text");
            DestroyChild(lobbyPanel, "Leave Button");

            LobbyMenu menu = canvas.GetComponent<LobbyMenu>();
            if (menu == null)
                menu = canvas.gameObject.AddComponent<LobbyMenu>();

            // Main: title, the reason we came back (if any), Create and Join.
            RectTransform main = CreatePanel(MainPanelName, canvas.transform);
            CreateText("Title Text", main, font, "Commodore Circuit", new Vector2(0f, 45f), new Vector2(380f, 40f), 30f);
            menu.MainMessageText = CreateText("Message Text", main, font, "", new Vector2(0f, 15f), new Vector2(380f, 20f), 11f);
            Wire(CreateButton("Create Button", main, font, "Create Lobby", new Vector2(-75f, -25f), new Vector2(130f, 40f), 18f),
                menu.CreateLobby);
            Wire(CreateButton("Join Button", main, font, "Join Lobby", new Vector2(75f, -25f), new Vector2(130f, 40f), 18f),
                menu.ShowJoin);

            // Join: typed code, a hint line, an 8 x 4 keypad (31 code characters + Del), Join and Back.
            RectTransform join = CreatePanel(JoinPanelName, canvas.transform);
            CreateText("Title Text", join, font, "Enter Lobby Code", new Vector2(0f, 58f), new Vector2(380f, 24f), 16f);
            menu.CodeEntryText = CreateText("Code Entry Text", join, font, "_ _ _ _", new Vector2(0f, 32f), new Vector2(200f, 34f), 32f);
            menu.JoinMessageText = CreateText("Message Text", join, font, "", new Vector2(0f, 10f), new Vector2(380f, 16f), 10f);
            const int columns = 8;
            string keys = FusionNetworkManager.CodeCharacters;
            for (int i = 0; i <= keys.Length; i++)
            {
                // Keys are 20 x 14 cm on a 22 x 16 cm grid, left of centre to leave room for Join and Back.
                var position = new Vector2(-40f + (i % columns - (columns - 1) * 0.5f) * 22f, -8f - i / columns * 16f);
                if (i == keys.Length)
                {
                    Wire(CreateButton("Key Del", join, font, "Del", position, new Vector2(20f, 14f), 7f), menu.DeleteCharacter);
                    break;
                }
                string key = keys[i].ToString();
                Button button = CreateButton("Key " + key, join, font, key, position, new Vector2(20f, 14f), 10f);
                UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(menu.AddCharacter), key);
            }
            Wire(CreateButton("Join Button", join, font, "Join", new Vector2(125f, -16f), new Vector2(90f, 30f), 16f), menu.JoinLobby);
            Wire(CreateButton("Back Button", join, font, "Back", new Vector2(125f, -50f), new Vector2(90f, 24f), 12f), menu.ShowMain);

            // Lobby: the code at the top for others to type in, and a way back out.
            menu.LobbyCodeText = CreateText("Lobby Code Text", lobbyPanel, font, "Lobby code: ----",
                new Vector2(0f, 55f), new Vector2(380f, 30f), 24f);
            Wire(CreateButton("Leave Button", lobbyPanel, font, "Leave Lobby", new Vector2(0f, -60f), new Vector2(90f, 20f), 11f),
                menu.LeaveLobby);

            menu.MainPanel = main.gameObject;
            menu.JoinPanel = join.gameObject;
            menu.LobbyPanel = lobbyPanel.gameObject;
            join.gameObject.SetActive(false);
            lobbyPanel.gameObject.SetActive(false);
            EditorUtility.SetDirty(menu);
        }

        private static void FixPhotonRegion()
        {
            PhotonAppSettings settings = PhotonAppSettings.Global;
            if (settings == null || !string.IsNullOrEmpty(settings.AppSettings.FixedRegion))
                return;
            settings.AppSettings.FixedRegion = Region;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"ChalkboardLobbySetup: Photon Fixed Region set to \"{Region}\".");
        }

        private static RectTransform CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, string content,
            Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            // Labels must not catch the poke, or the button behind them would never be pressed.
            text.raycastTarget = false;
            LayOut(text, font, position, size, fontSize);
            return text;
        }

        // A faint chalk-smudge rectangle that brightens when a finger hovers and presses it.
        private static Button CreateButton(string name, Transform parent, TMP_FontAsset font, string label,
            Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white; // The button's colour tint below sets the smudge strength.

            var button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.12f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.3f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.55f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.05f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            TMP_Text text = CreateText("Label", rect, font, label, Vector2.zero, size, fontSize);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Wire(Button button, UnityAction action) =>
            UnityEventTools.AddVoidPersistentListener(button.onClick, action);

        private static void LayOut(TMP_Text text, TMP_FontAsset font, Vector2 position, Vector2 size, float fontSize)
        {
            if (text == null)
                return;

            var rect = text.rectTransform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white; // The font material supplies the chalk tint.
            EditorUtility.SetDirty(text);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }
    }
}
