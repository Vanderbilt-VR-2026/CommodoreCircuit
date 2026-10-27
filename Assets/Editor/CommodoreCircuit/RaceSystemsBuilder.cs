using System.IO;
using CommodoreCircuit.Race;
using CommodoreCircuit.Race.UI;
using CommodoreCircuit.Scooter;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Race > Add Race Systems to Open Scene
    ///
    /// Adds the countdown, race timer, wrong-way warning and results board to the open
    /// scene and wires them up. The scene needs a ScooterController and, ideally, the
    /// "Campus Track" from Build Campus Track. Running it again rebuilds the UI.
    ///
    ///   Campus Track         + RaceTrack (reads campus_track.json)
    ///   [scooter]            + RaceProgress
    ///   Race Systems         LocalRaceSession (solo / editor testing)
    ///     Network Race Session   NetworkObject (Master Client Object) + NetworkRaceSession
    ///     Race Audio
    ///     Dashboard HUD      timer, lap, position badge (rides above the handlebar)
    ///     Race Callouts      GET READY / 3-2-1-GO! / LAP 2 / FINAL LAP!
    ///     Wrong Way Warning
    ///     Results Board
    ///
    /// Also adds NetworkRacer to the Network Player prefab so standings work online.
    /// </summary>
    public static class RaceSystemsBuilder
    {
        private const string RootName = "Race Systems";
        private const string TrackRootName = "Campus Track";
        private const string TrackDataPath = "Assets/CampusTrack/campus_track.json";
        private const string SpriteDir = "Assets/Art/UI/Race";
        private const string FontDir = "Assets/Fonts";
        private const string GeneratedFontDir = "Assets/Fonts/Race SDF";
        private const string OverlayShaderName = "CommodoreCircuit/UI Overlay";
        private const string TextOverlayShaderName = "TextMeshPro/Mobile/Distance Field Overlay";
        private const string PlayerPrefabPath = "Assets/Fusion and Essential Spawned Player Stuffs/Prefabs/Network Player.prefab";
        private const int ResultRows = 4;   // FusionNetworkManager.MaxPlayers is 4; raise both together.

        // Commodore Circuit palette (Design/DESIGN_LANGUAGE_1.md)
        private static readonly Color Black = Hex("141414");
        private static readonly Color Gold = Hex("FFC72C");
        private static readonly Color White = Color.white;
        private static readonly Color Muted = new Color(0.72f, 0.72f, 0.72f);

        private struct Fonts
        {
            public TMP_FontAsset Display, Accent, Body, Numerals;           // Anton, Luckiest Guy, Poppins, Rajdhani
            public Material DisplayMat, AccentMat, AccentOutlineMat, BodyMat, BodyOutlineMat, NumeralsMat;
        }

        private struct Art
        {
            public Sprite Plate, WrongWay, Finish, Winner, Panel, Dot;
            public Sprite[] Positions;
            public Material Overlay;
        }

        [MenuItem("CommodoreCircuit/Race/Add Race Systems to Open Scene")]
        private static void Build()
        {
            var scooter = Object.FindAnyObjectByType<ScooterController>(FindObjectsInactive.Include);
            if (scooter == null)
            {
                EditorUtility.DisplayDialog("No scooter in this scene",
                    "Open the race scene (e.g. Campus Track Scene) first. The race needs a ScooterController.", "OK");
                return;
            }

            var fonts = LoadFonts();
            var art = LoadArt();

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            // ---- Track + racer ----
            string trackNote = SetUpTrack();
            // (Not ??: Unity's "fake null" for missing components would skip the Add.)
            var progress = scooter.GetComponent<RaceProgress>();
            if (!progress) progress = Undo.AddComponent<RaceProgress>(scooter.gameObject);
            SetRef(progress, "scooter", scooter);

            // ---- Sessions ----
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Race Systems");
            root.AddComponent<LocalRaceSession>();

            var net = new GameObject("Network Race Session");
            net.transform.SetParent(root.transform, false);
            var netObj = net.AddComponent<NetworkObject>();
            netObj.Flags |= NetworkObjectFlags.MasterClientObject;   // master owns the race clock; passes on if they leave
            net.AddComponent<NetworkRaceSession>();

            var audioGo = new GameObject("Race Audio");
            audioGo.transform.SetParent(root.transform, false);
            var audio = audioGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;   // UI sounds: same in both ears

            // ---- UI ----
            var callouts = BuildCallouts(root.transform, scooter.transform, fonts, out var lapCallout, out var lapCalloutText);
            BuildDashboard(root.transform, scooter.transform, fonts, art, audio, lapCallout, lapCalloutText);
            Wire(callouts, audio);
            BuildWrongWay(root.transform, scooter.transform, art, audio);
            BuildResults(root.transform, scooter.transform, fonts, art, audio);

            string prefabNote = AddNetworkRacerToPlayerPrefab(false);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Race] Race systems added. {trackNote} {prefabNote}");
            EditorUtility.DisplayDialog("Race systems added",
                "Countdown, race timer, wrong-way warning and results board are in the scene under \"Race Systems\".\n\n" +
                trackNote + "\n" + prefabNote + "\n\n" +
                "Save the scene (Cmd+S), then press Play. Solo races start by themselves; in a Fusion session " +
                "the Network Race Session takes over.", "OK");
        }

        [MenuItem("CommodoreCircuit/Race/Add NetworkRacer to Network Player Prefab")]
        private static void AddNetworkRacerMenu()
        {
            EditorUtility.DisplayDialog("Network Player prefab", AddNetworkRacerToPlayerPrefab(true), "OK");
        }

        // ------------------------------------------------------------------ track

        private static string SetUpTrack()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(TrackDataPath);
            var trackRoot = GameObject.Find(TrackRootName);
            if (trackRoot == null)
            {
                var existing = Object.FindAnyObjectByType<RaceTrack>();
                if (existing) trackRoot = existing.gameObject;
            }
            string note;
            if (trackRoot == null)
            {
                trackRoot = new GameObject("Race Track");
                Undo.RegisterCreatedObjectUndo(trackRoot, "Add Race Track");
                note = "No \"Campus Track\" found, so a Race Track was added at the origin (run Build Campus Track for the real one).";
            }
            else note = $"Race line taken from \"{trackRoot.name}\".";

            var track = trackRoot.GetComponent<RaceTrack>();
            if (!track) track = Undo.AddComponent<RaceTrack>(trackRoot);
            SetRef(track, "trackData", json);
            if (json == null) note += " campus_track.json is missing!";
            return note;
        }

        private static string AddNetworkRacerToPlayerPrefab(bool verbose)
        {
            string path = PlayerPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                path = null;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go && go.GetComponent<FusionNetworkPlayer>()) { path = p; break; }
                }
            }
            if (path == null) return "No Network Player prefab found; add NetworkRacer to the player prefab by hand.";

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (contents.GetComponent<NetworkRacer>())
                    return verbose ? $"{Path.GetFileName(path)} already has NetworkRacer." : "Network Player already has NetworkRacer.";
                contents.AddComponent<NetworkRacer>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return $"Added NetworkRacer to {Path.GetFileName(path)}.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ------------------------------------------------------------------ dashboard

        private static void BuildDashboard(Transform root, Transform vehicle, Fonts f, Art art, AudioSource audio,
            CanvasGroup lapCallout, TMP_Text lapCalloutText)
        {
            // 1 unit = 1 mm. Rides 0.75 m ahead and 0.42 m below the eyes: just above the bars.
            var canvas = MakeCanvas(root, "Dashboard HUD", new Vector2(560, 190), vehicle, 0.75f, -0.42f, 0);
            var content = Group(canvas.transform, "Content");

            var plate = Img(content, "Plate", art.Plate, art.Overlay, new Vector2(30, 0), new Vector2(460, 137));
            plate.preserveAspect = true;

            var badge = Img(content, "Position Badge", art.Positions[0], art.Overlay, new Vector2(-218, 8), new Vector2(124, 124));
            badge.preserveAspect = true;
            var posText = Txt(content, "Position Text", "1ST", f.Accent, f.AccentOutlineMat, 46, White,
                new Vector2(-218, 8), new Vector2(130, 70), TextAlignmentOptions.Center);
            posText.enabled = false;

            var time = Txt(content, "Time", "00:00.00", f.Numerals, f.NumeralsMat, 70, Gold,
                new Vector2(55, 16), new Vector2(330, 74), TextAlignmentOptions.Center);
            var lap = Txt(content, "Lap", "LAP 1/3", f.Display, f.DisplayMat, 30, White,
                new Vector2(-30, -38), new Vector2(150, 36), TextAlignmentOptions.Center, FontStyles.Italic);
            var best = Txt(content, "Best Lap", "BEST --:--.--", f.Numerals, f.NumeralsMat, 26, Muted,
                new Vector2(118, -38), new Vector2(200, 32), TextAlignmentOptions.Center);

            var hud = canvas.gameObject.AddComponent<RaceTimerHud>();
            SetRef(hud, "group", content.GetComponent<CanvasGroup>());
            SetRef(hud, "timeText", time);
            SetRef(hud, "lapText", lap);
            SetRef(hud, "bestLapText", best);
            SetRef(hud, "positionBadge", badge);
            SetArray(hud, "positionBadges", art.Positions);
            SetRef(hud, "positionText", posText);
            SetRef(hud, "calloutGroup", lapCallout);
            SetRef(hud, "calloutText", lapCalloutText);
            SetRef(hud, "audioSource", audio);
        }

        // ------------------------------------------------------------------ countdown + callouts

        private static RaceCountdownView BuildCallouts(Transform root, Transform vehicle, Fonts f,
            out CanvasGroup lapCallout, out TMP_Text lapCalloutText)
        {
            // 4 m ahead, a little above the eyes: big and readable without craning.
            var canvas = MakeCanvas(root, "Race Callouts", new Vector2(3200, 1800), vehicle, 4f, 0.25f, 10);

            var countdown = Group(canvas.transform, "Countdown");
            var number = Txt(countdown, "Number", "3", f.Accent, f.AccentOutlineMat, 820, White,
                new Vector2(0, 80), new Vector2(1600, 900), TextAlignmentOptions.Center);
            var caption = Txt(countdown, "Caption", "GET READY!", f.Accent, f.AccentOutlineMat, 190, White,
                new Vector2(0, -520), new Vector2(3000, 420), TextAlignmentOptions.Center);
            caption.lineSpacing = -10f;

            var callout = Group(canvas.transform, "Lap Callout");
            lapCalloutText = Txt(callout, "Text", "FINAL LAP!", f.Accent, f.AccentOutlineMat, 300, Gold,
                new Vector2(0, 260), new Vector2(3000, 420), TextAlignmentOptions.Center);
            lapCallout = callout.GetComponent<CanvasGroup>();

            var view = canvas.gameObject.AddComponent<RaceCountdownView>();
            SetRef(view, "group", countdown.GetComponent<CanvasGroup>());
            SetRef(view, "numberText", number);
            SetRef(view, "captionText", caption);
            return view;
        }

        private static void Wire(RaceCountdownView view, AudioSource audio) => SetRef(view, "audioSource", audio);

        // ------------------------------------------------------------------ wrong way

        private static void BuildWrongWay(Transform root, Transform vehicle, Art art, AudioSource audio)
        {
            var canvas = MakeCanvas(root, "Wrong Way Warning", new Vector2(1100, 1100), vehicle, 3f, 0.05f, 20);
            var content = Group(canvas.transform, "Content");
            var sign = Img(content, "Sign", art.WrongWay, art.Overlay, Vector2.zero, new Vector2(960, 915));
            sign.preserveAspect = true;

            var view = canvas.gameObject.AddComponent<WrongWayWarning>();
            SetRef(view, "group", content.GetComponent<CanvasGroup>());
            SetRef(view, "sign", sign.rectTransform);
            SetRef(view, "audioSource", audio);
        }

        // ------------------------------------------------------------------ results

        private static void BuildResults(Transform root, Transform vehicle, Fonts f, Art art, AudioSource audio)
        {
            var canvas = MakeCanvas(root, "Results Board", new Vector2(1800, 1400), vehicle, 2.4f, -0.05f, 30);

            // FINISH banner that pops first.
            var bannerGroup = Group(canvas.transform, "Finish Banner");
            var banner = Img(bannerGroup, "Banner", art.Finish, art.Overlay, new Vector2(0, 120), new Vector2(1600, 452));
            banner.preserveAspect = true;

            // The board.
            var board = Group(canvas.transform, "Board");
            var frame = Img(board, "Gold Frame", art.Panel, art.Overlay, Vector2.zero, new Vector2(1420, 1020));
            frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 0.12f; frame.color = Gold;
            var panel = Img(board, "Panel", art.Panel, art.Overlay, Vector2.zero, new Vector2(1396, 996));
            panel.type = Image.Type.Sliced; panel.pixelsPerUnitMultiplier = 0.12f; panel.color = new Color(Black.r, Black.g, Black.b, 0.96f);

            var header = Img(board, "Finish Header", art.Finish, art.Overlay, new Vector2(0, 470), new Vector2(760, 215));
            header.preserveAspect = true;
            var title = Txt(board, "Title", "FINAL STANDINGS", f.Display, f.DisplayMat, 64, Gold,
                new Vector2(0, 312), new Vector2(1200, 80), TextAlignmentOptions.Center, FontStyles.Italic);

            const float posX = -575f, dotX = -462f, nameX = -430f, timeX = 255f, bestX = 515f;
            Txt(board, "Col Pos", "POS", f.Display, f.DisplayMat, 36, Muted, new Vector2(posX, 225), new Vector2(150, 50), TextAlignmentOptions.Center);
            Txt(board, "Col Racer", "RACER", f.Display, f.DisplayMat, 36, Muted, new Vector2(nameX + 200, 225), new Vector2(400, 50), TextAlignmentOptions.Left);
            Txt(board, "Col Time", "TIME", f.Display, f.DisplayMat, 36, Muted, new Vector2(timeX, 225), new Vector2(300, 50), TextAlignmentOptions.Center);
            Txt(board, "Col Best", "BEST LAP", f.Display, f.DisplayMat, 36, Muted, new Vector2(bestX, 225), new Vector2(260, 50), TextAlignmentOptions.Center);

            var rows = new RaceResultRow[ResultRows];
            for (int i = 0; i < ResultRows; i++)
            {
                float y = 120f - i * 135f;
                var row = new GameObject($"Row {i + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(board, false);
                row.anchoredPosition = new Vector2(0, y);
                row.sizeDelta = new Vector2(1320, 120);

                var bg = Img(row, "Highlight", art.Panel, art.Overlay, Vector2.zero, new Vector2(1320, 120));
                bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.25f; bg.color = Color.clear;
                var badge = Img(row, "Badge", art.Positions[Mathf.Min(i, art.Positions.Length - 1)], art.Overlay, new Vector2(posX, 0), new Vector2(108, 108));
                badge.preserveAspect = true;
                var posText = Txt(row, "Position", RaceFormat.Ordinal(i + 1), f.Accent, f.AccentOutlineMat, 44, White,
                    new Vector2(posX, 0), new Vector2(150, 70), TextAlignmentOptions.Center);
                var dot = Img(row, "Color", art.Dot, art.Overlay, new Vector2(dotX, 0), new Vector2(38, 38));
                var name = Txt(row, "Name", $"Player {i + 1}", f.Body, f.BodyMat, 50, White,
                    new Vector2(nameX + 260, 0), new Vector2(520, 70), TextAlignmentOptions.Left);
                var time = Txt(row, "Time", "00:00.00", f.Numerals, f.NumeralsMat, 68, Gold,
                    new Vector2(timeX, 0), new Vector2(300, 80), TextAlignmentOptions.Center);
                var best = Txt(row, "Best Lap", "--:--.--", f.Numerals, f.NumeralsMat, 50, White,
                    new Vector2(bestX, 0), new Vector2(260, 70), TextAlignmentOptions.Center);

                var r = row.gameObject.AddComponent<RaceResultRow>();
                r.background = bg; r.positionBadge = badge; r.positionText = posText; r.colorDot = dot;
                r.nameText = name; r.timeText = time; r.bestLapText = best;
                rows[i] = r;
            }

            var footer = Txt(board, "Footer", "Back to lobby in 20", f.Body, f.BodyMat, 40, Muted,
                new Vector2(0, -445), new Vector2(1200, 60), TextAlignmentOptions.Center);

            var winner = Img(board, "Winner Badge", art.Winner, art.Overlay, new Vector2(585, 470), new Vector2(440, 200));
            winner.preserveAspect = true;
            winner.rectTransform.localRotation = Quaternion.Euler(0, 0, -10);

            var view = canvas.gameObject.AddComponent<RaceResultsBoard>();
            SetRef(view, "finishBanner", bannerGroup.GetComponent<CanvasGroup>());
            SetRef(view, "board", board.GetComponent<CanvasGroup>());
            SetRef(view, "winnerBadge", winner.gameObject);
            SetRef(view, "titleText", title);
            SetArray(view, "rows", rows);
            SetArray(view, "positionBadges", art.Positions);
            SetRef(view, "footerText", footer);
            SetRef(view, "audioSource", audio);
        }

        // ------------------------------------------------------------------ building blocks

        private static Canvas MakeCanvas(Transform parent, string name, Vector2 sizeMm, Transform vehicle,
            float distance, float height, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeMm;
            rt.localScale = Vector3.one * 0.001f;   // 1 UI unit = 1 mm
            // Place it roughly in front of the start for editing; RiderViewAnchor moves it at runtime.
            rt.position = vehicle.position + vehicle.forward * distance + Vector3.up * (1.7f + height);
            rt.rotation = Quaternion.LookRotation(vehicle.forward, Vector3.up);

            var anchor = go.AddComponent<RiderViewAnchor>();
            anchor.vehicle = vehicle;
            anchor.distance = distance;
            anchor.height = height;
            return canvas;
        }

        private static RectTransform Group(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var cg = go.GetComponent<CanvasGroup>();
            cg.interactable = false; cg.blocksRaycasts = false;
            return rt;
        }

        private static Image Img(Transform parent, string name, Sprite sprite, Material mat, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.material = mat;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Txt(Transform parent, string name, string text, TMP_FontAsset font, Material mat,
            float size, Color color, Vector2 pos, Vector2 box, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            if (mat) t.fontSharedMaterial = mat;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ------------------------------------------------------------------ fonts

        private static Fonts LoadFonts()
        {
            var f = new Fonts
            {
                Display = FontAsset("Anton-Regular.ttf"),
                Accent = FontAsset("LuckiestGuy-Regular.ttf"),
                Body = FontAsset("Poppins-SemiBold.ttf"),
                Numerals = FontAsset("Rajdhani-Bold.ttf"),
            };
            f.DisplayMat = OverlayMaterial(f.Display, null, 0f);
            f.AccentMat = OverlayMaterial(f.Accent, null, 0f);
            f.AccentOutlineMat = OverlayMaterial(f.Accent, Black, 0.28f);
            f.BodyMat = OverlayMaterial(f.Body, null, 0f);
            f.BodyOutlineMat = OverlayMaterial(f.Body, Black, 0.2f);
            f.NumeralsMat = OverlayMaterial(f.Numerals, null, 0f);
            return f;
        }

        /// <summary>A dynamic SDF font asset for a TTF in Assets/Fonts, created once and reused.</summary>
        private static TMP_FontAsset FontAsset(string ttf)
        {
            EnsureFolder(GeneratedFontDir);
            string baseName = Path.GetFileNameWithoutExtension(ttf);
            string path = $"{GeneratedFontDir}/{baseName} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing) return existing;

            var source = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{ttf}");
            if (source == null)
            {
                Debug.LogWarning($"[Race] {FontDir}/{ttf} not found; using the TMP default font.");
                return TMP_Settings.defaultFontAsset;
            }

            // Same steps as TMP's own "Create > TextMeshPro > Font Asset" menu.
            var asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            if (asset == null)
            {
                Debug.LogWarning($"[Race] Couldn't make a font asset from {ttf} (is 'Include Font Data' on?); using the TMP default font.");
                return TMP_Settings.defaultFontAsset;
            }
            asset.name = $"{baseName} SDF";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = $"{baseName} SDF Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = $"{baseName} SDF Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            // Pre-bake what the HUD always shows so the first frame doesn't hitch on Quest.
            asset.TryAddCharacters("0123456789:.-/!?() ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", out _);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Copy of a font's material on the overlay (draw-on-top) shader, optionally outlined.</summary>
        private static Material OverlayMaterial(TMP_FontAsset font, Color? outline, float width)
        {
            if (font == null) return null;
            string suffix = outline.HasValue ? "Overlay Outline" : "Overlay";
            string path = $"{GeneratedFontDir}/{font.name} {suffix}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(font.material);
                var shader = Shader.Find(TextOverlayShaderName);
                if (shader) mat.shader = shader;
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
            if (outline.HasValue)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, outline.Value);
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, width * 0.5f);
            }
            ShaderUtilities.UpdateShaderRatios(mat);   // TMP needs this after outline changes made from code
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ art

        private static Art LoadArt()
        {
            var art = new Art
            {
                Plate = SpriteAt("race_hud_plate"),
                WrongWay = SpriteAt("race_wrong_way_sign"),
                Finish = SpriteAt("race_finish_banner"),
                Winner = SpriteAt("race_winner_badge"),
                Positions = new[] { SpriteAt("race_position_1"), SpriteAt("race_position_2"), SpriteAt("race_position_3"), SpriteAt("race_position_4") },
                Panel = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                Dot = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            };

            string matPath = $"{SpriteDir}/RaceUIOverlay.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find(OverlayShaderName);
            if (mat == null && shader != null)
            {
                mat = new Material(shader) { name = "RaceUIOverlay" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            art.Overlay = mat;
            return art;
        }

        private static Sprite SpriteAt(string name)
        {
            string path = $"{SpriteDir}/{name}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti &&
                (ti.textureType != TextureImporterType.Sprite || !ti.mipmapEnabled))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;            // stops shimmer on world-space UI in the headset
                ti.filterMode = FilterMode.Trilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[Race] Missing sprite {path}");
            return sprite;
        }

        // ------------------------------------------------------------------ helpers

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[Race] {target.GetType().Name} has no field '{field}'."); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[Race] {target.GetType().Name} has no field '{field}'."); return; }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
    }
}
