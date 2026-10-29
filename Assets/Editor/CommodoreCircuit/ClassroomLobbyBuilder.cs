using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Rebuilds the Lobby as our real classroom (photos + plan in the "Classroom Lobby Sketches" page).
    ///
    /// Menu command under CommodoreCircuit:
    ///   - "Build Classroom Lobby" - with Lobby Scene open, builds a "Classroom" object:
    ///       8 m wide (x) x 12 m deep (z) x 3 m tall. Front wall at z = +4, so the
    ///       chalkboard (Furniture/Whiteboard) and the lobby Canvas on it stay where they are.
    ///       Back wall at z = -8 is the blue accent wall.
    ///     Front:  projection screen housing centred above the board, lectern, angled TV (right)
    ///     Right:  three windows (real openings in the wall) with half-lowered roller shades
    ///     Back:   door, whiteboard, stack of spare chairs
    ///     Left:   door at the front end, whiteboard behind it, long whiteboard near the back
    ///     Floor:  tan, curved blue-grey zone on the left, charcoal stripe near the back
    ///     Room:   5 rows of tables on each side of a centre aisle, 2 chairs per table,
    ///             ceiling tile grid with 8 square panel lights and 6 round downlights,
    ///             three wall-to-wall light bars, projector over the aisle
    ///             by row 2, and a TV over each side between rows 2 and 3, angled inward
    ///     Light:  baked only (for Quest): area lights on the light bars and windows, light probes,
    ///             a reflection probe, static flags and lightmap settings. Bake with Generate Lighting.
    ///   It also removes the old StudentDesks, TeacherDesk and loose chairs, hides the old
    ///   Room, Plane, Invisible Walls and FloorCollider (the new shell has its own colliders),
    ///   and puts the two apples on the new furniture.
    ///   Running it again replaces "Classroom". Undo (Cmd+Z) reverts the whole build.
    ///   Materials are created once in Assets/Materials/Lobby/Classroom and reused after that,
    ///   so colour tweaks made in the editor are kept.
    /// </summary>
    public static class ClassroomLobbyBuilder
    {
        private const string LobbyPath = "Assets/Scenes/Lobby Scene.unity";
        private const string RootName = "Classroom";
        private const string MaterialFolder = "Assets/Materials/Lobby/Classroom";
        private const string ChairModelPath = "Assets/Third Party/Kenney (models)/kenney_furniture-kit/Models/FBX format/chair.fbx";

        // Room interior. +Z is the front (chalkboard), matching LobbyBuilder and ChalkboardLobbySetup.
        private const float HalfWidth = 4f;
        private const float FrontZ = 4f;
        private const float BackZ = -8f;
        private const float Height = 3f;
        private const float WallThickness = 0.2f;
        private static float CenterZ => (FrontZ + BackZ) / 2f;
        private static float Depth => FrontZ - BackZ;

        // Tables: two per row on each side, leaving a 2.7 m aisle for the spawn line at x = 0.
        private const float TableWidth = 1.1f;
        private const float TableDepth = 0.5f;
        private const float TableHeight = 0.74f;
        private static readonly float[] TableColumnsX = { -3.0f, -1.9f, 1.9f, 3.0f };
        private static readonly float[] TableRowsZ = { 1.2f, -0.3f, -1.8f, -3.3f, -4.8f };
        private const float ChairHeight = 0.85f;

        // Right wall windows: (centre along the Right Wall group's local x, width). Front one is the narrow one.
        private static readonly Vector2[] RightWindows = { new Vector2(-4.7f, 1.2f), new Vector2(-1.6f, 3.2f), new Vector2(2.5f, 3.2f) };
        private const float WindowBottom = 0.9f;
        private const float WindowTop = 2.6f;

        // Right-side TVs turn this far so their screens face back and toward the centre aisle; left-side ones use the negative.
        private const float TvInwardYaw = 18f;

        // Baked lighting (Quest: nothing here costs anything at runtime). Raise or lower these, then bake again.
        private const string LightingSettingsPath = "Assets/Scenes/Lobby Scene Lighting.lighting";
        private const float LightBarDownIntensity = 8f;
        private const float LightBarUpIntensity = 1f;
        private const float WindowIntensity = 2f;
        private const float PanelLightIntensity = 6f;
        private const float DownlightIntensity = 1.5f;
        // Sun from outside the window wall, 35 degrees up, so patches of sunlight land on the right-hand tables.
        private static readonly Vector3 SunRotation = new Vector3(35f, -110f, 0f);
        private const string OutsideName = "Outside";
        private static readonly Color LightBarColor = new Color(1f, 0.96f, 0.9f);
        private static readonly Color DaylightColor = new Color(0.86f, 0.92f, 1f);

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [MenuItem("CommodoreCircuit/Build Classroom Lobby")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode", "Stop Play Mode and run this command again.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != LobbyPath)
            {
                EditorUtility.DisplayDialog("Open the Lobby first", $"Open {LobbyPath} and run this command again.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build Classroom Lobby");
            int undoGroup = Undo.GetCurrentGroup();
            Materials.Clear();
            EnsureFolder(MaterialFolder);

            GameObject old = FindRoot(scene, RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            string cleanup = RemoveOldClassroom(scene);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Classroom Lobby");

            BuildShell(root.transform);
            BuildFloorZones(root.transform);
            BuildFrontWall(Group(root.transform, "Front Wall", new Vector3(0f, 0f, FrontZ), 0f), root.transform);
            BuildRightWall(Group(root.transform, "Right Wall", new Vector3(HalfWidth, 0f, CenterZ), 90f));
            BuildBackWall(Group(root.transform, "Back Wall", new Vector3(0f, 0f, BackZ), 180f), root.transform);
            BuildLeftWall(Group(root.transform, "Left Wall", new Vector3(-HalfWidth, 0f, CenterZ), -90f));
            BuildCeiling(root.transform);
            Vector3 lecternTop = BuildLectern(root.transform);
            Vector3 frontTableTop = BuildTables(root.transform);
            string apples = MoveApples(scene, frontTableTop, lecternTop);
            string lighting = BuildLighting(scene, root.transform);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Lobby] Classroom built. {cleanup} {apples} {lighting}");
            EditorUtility.DisplayDialog("Classroom built",
                "The classroom is under \"Classroom\" in the Hierarchy.\n\n" + cleanup + "\n" + apples + "\n" + lighting +
                "\n\nThe room stays dim until you bake: Window > Rendering > Lighting > Generate Lighting." +
                "\n\nSave the scene (Cmd+S) to keep it, or Undo (Cmd+Z) to go back.", "OK");
        }

        // ---------------------------------------------------------------- old lobby

        private static string RemoveOldClassroom(Scene scene)
        {
            int removed = 0;
            GameObject furniture = FindRoot(scene, "Furniture");
            if (furniture != null)
            {
                foreach (string name in new[] { "StudentDesks", "TeacherDesk" })
                {
                    Transform child = furniture.transform.Find(name);
                    if (child == null) continue;
                    Undo.DestroyObjectImmediate(child.gameObject);
                    removed++;
                }
            }

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name != "chair" && !go.name.StartsWith("chair (")) continue;
                Undo.DestroyObjectImmediate(go);
                removed++;
            }

            // Hidden, not deleted: other setup scripts still look these up by name.
            int hidden = 0;
            foreach (string name in new[] { "Room", "Plane", "Invisible Walls", "FloorCollider" })
            {
                GameObject go = FindRoot(scene, name);
                if (go == null || !go.activeSelf) continue;
                Undo.RecordObject(go, "Build Classroom Lobby");
                go.SetActive(false);
                hidden++;
            }

            return $"Removed {removed} old desks/chairs and hid {hidden} old room objects.";
        }

        private static string MoveApples(Scene scene, Vector3 tableTop, Vector3 lecternTop)
        {
            int moved = 0;
            moved += MoveTo(FindRoot(scene, "apple"), tableTop + new Vector3(0.2f, 0.01f, 0.05f)) ? 1 : 0;
            moved += MoveTo(FindRoot(scene, "apple (2)"), lecternTop + new Vector3(0.1f, 0.01f, 0f)) ? 1 : 0;
            return moved > 0 ? $"Moved {moved} apple(s) onto the new furniture." : "";
        }

        private static bool MoveTo(GameObject go, Vector3 position)
        {
            if (go == null) return false;
            Undo.RecordObject(go.transform, "Build Classroom Lobby");
            go.transform.position = position;
            return true;
        }

        // ---------------------------------------------------------------- shell

        private static void BuildShell(Transform root)
        {
            Transform shell = Child(root, "Shell");
            float w = HalfWidth * 2f + WallThickness * 2f;

            // Floor slab: top face at y = 0. Its collider replaces the old FloorCollider.
            Box("Floor", shell, new Vector3(0f, -0.1f, CenterZ), new Vector3(w, 0.2f, Depth + WallThickness * 2f), Mat("FloorTan", "CDB48C"), true);

            Material cream = Mat("WallCream", "E5DDCB");
            Box("Front", shell, new Vector3(0f, Height / 2f, FrontZ + WallThickness / 2f), new Vector3(w, Height, WallThickness), cream, true);
            Box("Back", shell, new Vector3(0f, Height / 2f, BackZ - WallThickness / 2f), new Vector3(w, Height, WallThickness), Mat("AccentWallBlue", "4F6A85"), true);
            Box("Left", shell, new Vector3(-HalfWidth - WallThickness / 2f, Height / 2f, CenterZ), new Vector3(WallThickness, Height, Depth), cream, true);
            BuildRightShell(shell, cream);
        }

        // The right wall in pieces around the window openings, so daylight really comes through.
        private static void BuildRightShell(Transform shell, Material mat)
        {
            // Window centres are along the Right Wall group's local x, which runs toward the back (world z = CenterZ - x).
            var openings = new List<Vector2>();
            foreach (Vector2 w in RightWindows)
                openings.Add(new Vector2(CenterZ - w.x - w.y / 2f, CenterZ - w.x + w.y / 2f));
            openings.Sort((a, b) => a.x.CompareTo(b.x));

            float x = HalfWidth + WallThickness / 2f;
            void Piece(float z0, float z1, float y0, float y1) =>
                Box("Right", shell, new Vector3(x, (y0 + y1) / 2f, (z0 + z1) / 2f), new Vector3(WallThickness, y1 - y0, z1 - z0), mat, true);

            float z = BackZ;
            foreach (Vector2 o in openings)
            {
                if (o.x > z) Piece(z, o.x, 0f, Height);
                Piece(o.x, o.y, 0f, WindowBottom);
                Piece(o.x, o.y, WindowTop, Height);
                z = o.y;
            }
            if (z < FrontZ) Piece(z, FrontZ, 0f, Height);
        }

        private static void BuildFloorZones(Transform root)
        {
            Transform floor = Child(root, "Floor Inlays");

            // Curved blue-grey zone on the left, traced from the plan (front-left corner to the back).
            var blue = new List<Vector2> { new Vector2(-HalfWidth, FrontZ), new Vector2(-HalfWidth, BackZ), new Vector2(-1.45f, BackZ) };
            Vector2 p0 = new Vector2(-1.45f, BackZ), p1 = new Vector2(-0.36f, -5.5f), p2 = new Vector2(-0.73f, -0.2f), p3 = new Vector2(0f, FrontZ);
            for (int i = 1; i <= 16; i++)
            {
                float t = i / 16f, u = 1f - t;
                blue.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }
            FloorPatch("Blue Zone", floor, blue, new Vector2(-2.4f, -2f), 0.003f, Mat("FloorBlueGrey", "98A9B8"));

            // Charcoal stripe across the back, in front of the blue wall.
            var stripe = new List<Vector2> { new Vector2(-1.45f, -7.5f), new Vector2(HalfWidth, -7.3f), new Vector2(HalfWidth, -7.52f), new Vector2(-1.45f, -7.72f) };
            FloorPatch("Charcoal Stripe", floor, stripe, new Vector2(1.3f, -7.5f), 0.005f, Mat("FloorCharcoal", "5D595B"));
        }

        // ---------------------------------------------------------------- walls
        // Each wall group sits on the wall's inner face. Local +X is to your right as you face
        // the wall from inside the room, local -Z points into the room, y = 0 is the floor.

        private static void BuildFrontWall(Transform wall, Transform root)
        {
            Baseboard(wall, HalfWidth * 2f);

            // Rolled-up projection screen centred above the chalkboard (the chalkboard and lobby UI are left alone).
            Box("Projection Screen Housing", wall, new Vector3(0f, 2.72f, -0.08f), new Vector3(3.4f, 0.12f, 0.14f), Mat("Housing", "D9D9D6"), false);

            // The exit sign for the left-wall door hangs here, on the front wall near the left corner.
            ExitSign(wall, new Vector3(-3.5f, 2.4f, 0f));

            // Ceiling projector over the centre aisle, just in front of the second row.
            Transform projector = Child(root, "Projector");
            Box("Body", projector, new Vector3(0f, 2.62f, 0.35f), new Vector3(0.4f, 0.12f, 0.34f), Mat("Housing", "D9D9D6"), false);
            Pole(projector, new Vector3(0f, 2.68f, 0.35f));

            // TV on a ceiling pole, front-right corner, turned like the hanging TVs.
            Tv(root, "TV Front", new Vector3(3.15f, 2.3f, FrontZ - 0.6f), new Vector3(1.2f, 0.7f, 0.06f), TvInwardYaw);
        }

        private static void BuildRightWall(Transform wall)
        {
            Baseboard(wall, Depth);
            foreach (Vector2 w in RightWindows)
                Window(wall, w.x, w.y);
        }

        private static void BuildBackWall(Transform wall, Transform root)
        {
            Baseboard(wall, HalfWidth * 2f);
            Door(wall, 3.3f, true);
            Whiteboard(wall, 1.0f, 1.45f, 2.6f, 1.2f);

            // Spare chairs stacked in the back-right corner.
            Transform stack = Child(root, "Chair Stack");
            for (int i = 0; i < 4; i++)
                Chair(stack, new Vector3(3.45f, 0.07f * i, BackZ + 0.45f + 0.03f * i), 180f);
        }

        private static void BuildLeftWall(Transform wall)
        {
            Baseboard(wall, Depth);
            // Local +X runs toward the front here, so the door sits at the front end of the wall.
            Door(wall, 5.4f, false);
            Whiteboard(wall, 4.0f, 1.45f, 1.4f, 1.2f);
            Whiteboard(wall, -3.3f, 1.45f, 4.0f, 1.2f);
        }

        private static void BuildCeiling(Transform root)
        {
            Transform ceiling = Child(root, "Ceiling");

            // A quad facing down, so the tile texture maps 0.6 m tiles straight onto x and z.
            GameObject tiles = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tiles.name = "Tiles";
            Object.DestroyImmediate(tiles.GetComponent<Collider>());
            tiles.transform.SetParent(ceiling, false);
            tiles.transform.localPosition = new Vector3(0f, Height, CenterZ);
            tiles.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            tiles.transform.localScale = new Vector3(HalfWidth * 2f, Depth, 1f);
            MeshRenderer tileRenderer = tiles.GetComponent<MeshRenderer>();
            tileRenderer.sharedMaterial = CeilingMaterial();
            // A quad only blocks light from its front, so cast from both sides or the sun leaks in through the top.
            tileRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            // Three long hanging light bars, each running wall to wall, lighting the tables below
            // and washing the ceiling above like the real fixtures.
            Material light = Mat("LightBar", "F6F4EE", 0.5f, 1.4f, true);
            // Stop 0.5 m short of each wall; a light right against a wall just floods that corner.
            Vector2 barLightSize = new Vector2(HalfWidth * 2f - 1f, 0.25f);
            foreach (float z in new[] { 1.5f, -2.0f, -5.5f })
            {
                GameObject bar = Cylinder("Light Bar", ceiling, new Vector3(0f, 2.55f, z), new Vector3(0.1f, HalfWidth - 0.02f, 0.1f), light);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Pole(ceiling, new Vector3(-2.5f, 2.6f, z));
                Pole(ceiling, new Vector3(2.5f, 2.6f, z));
                AreaLight("Downlight", ceiling, new Vector3(0f, 2.49f, z), Quaternion.Euler(90f, 0f, 0f), barLightSize, LightBarColor, LightBarDownIntensity);
                AreaLight("Uplight", ceiling, new Vector3(0f, 2.61f, z), Quaternion.Euler(-90f, 0f, 0f), barLightSize, LightBarColor, LightBarUpIntensity);
            }

            // Recessed fixtures in the tiles, softer than the bars so the daylight still leads.
            // On Quest nothing glows brighter than a well-lit white tile, so the grey trim is what makes them read.
            Material fixture = Mat("CeilingFixture", "F8F8F4", 0.3f, 1.5f, true);
            Material trim = Mat("FixtureTrim", "B9BCBF", 0.55f);

            // 0.6 m square panels, each replacing one tile: two per table block between each pair of bars.
            // The x and z values are tile-cell centres (the grid starts at the left and front walls).
            foreach (float x in new[] { -3.1f, -1.9f, 1.7f, 2.9f })
            {
                foreach (float z in new[] { -0.5f, -3.5f })
                {
                    // Aluminium frame with the glowing diffuser set just inside it.
                    Transform panel = Child(ceiling, "Panel Light");
                    panel.localPosition = new Vector3(x, Height, z);
                    Box("Diffuser", panel, new Vector3(0f, -0.015f, 0f), new Vector3(0.54f, 0.01f, 0.54f), fixture, false);
                    foreach (float side in new[] { -0.29f, 0.29f })
                    {
                        Box("Frame", panel, new Vector3(side, -0.0125f, 0f), new Vector3(0.04f, 0.025f, 0.62f), trim, false);
                        Box("Frame", panel, new Vector3(0f, -0.0125f, side), new Vector3(0.54f, 0.025f, 0.04f), trim, false);
                    }
                    AreaLight("Panel Light Glow", ceiling, new Vector3(x, Height - 0.03f, z), Quaternion.Euler(90f, 0f, 0f),
                        new Vector2(0.55f, 0.55f), LightBarColor, PanelLightIntensity);
                }
            }

            // Round downlights along the side walls (over the left whiteboards and the matching spots
            // on the window side) and two in front of the screen.
            Vector3[] downlights =
            {
                new Vector3(-3.5f, 0f, 2.0f), new Vector3(-3.5f, 0f, -5.3f),
                new Vector3(3.5f, 0f, 2.0f), new Vector3(3.5f, 0f, -5.3f),
                new Vector3(-1.2f, 0f, FrontZ - 0.6f), new Vector3(1.2f, 0f, FrontZ - 0.6f),
            };
            foreach (Vector3 p in downlights)
            {
                // Grey trim ring with the glowing lens just below it.
                Cylinder("Downlight Trim", ceiling, new Vector3(p.x, Height - 0.01f, p.z), new Vector3(0.2f, 0.01f, 0.2f), trim);
                Cylinder("Downlight", ceiling, new Vector3(p.x, Height - 0.023f, p.z), new Vector3(0.14f, 0.003f, 0.14f), fixture);
                Transform spot = Child(ceiling, "Downlight Glow");
                spot.localPosition = new Vector3(p.x, Height - 0.03f, p.z);
                spot.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light spotLight = spot.gameObject.AddComponent<Light>();
                spotLight.type = UnityEngine.LightType.Spot;
                spotLight.lightmapBakeType = LightmapBakeType.Baked;
                spotLight.shadows = LightShadows.Soft;
                spotLight.spotAngle = 80f;
                spotLight.innerSpotAngle = 30f;
                spotLight.range = 4f;
                spotLight.color = LightBarColor;
                spotLight.intensity = DownlightIntensity;
            }

            // A TV over each block of tables between rows 2 and 3, mirrored and turned toward the centre.
            float tvZ = (TableRowsZ[1] + TableRowsZ[2]) / 2f;
            Vector3 tvSize = new Vector3(1.2f, 0.7f, 0.06f);
            Tv(root, "TV Hanging Right", new Vector3(3.3f, 2.3f, tvZ), tvSize, TvInwardYaw);
            Tv(root, "TV Hanging Left", new Vector3(-3.3f, 2.3f, tvZ), tvSize, -TvInwardYaw);
        }

        // ---------------------------------------------------------------- lighting
        // Everything in the room is baked, which suits standalone Quest. The area lights are added
        // with their fixtures (light bars, windows); this adds the probes, marks the room static,
        // sets the sun to baked and tunes the lightmap settings. Generate Lighting does the rest.

        private static string BuildLighting(Scene scene, Transform root)
        {
            Transform lighting = Child(root, "Lighting");

            // Light probes light the chairs and anything that moves (players, scooters):
            // a 1.2 m x 1.4 m grid at seat, standing and head-above height.
            var positions = new List<Vector3>();
            for (int i = 0; i <= 6; i++)
                for (int j = 0; j <= 8; j++)
                    foreach (float y in new[] { 0.5f, 1.4f, 2.3f })
                        positions.Add(new Vector3(-3.6f + 1.2f * i, y, FrontZ - 0.4f - 1.4f * j));
            Child(lighting, "Light Probes").gameObject.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();

            // One room-sized probe so whiteboards, TVs, glass and tabletops reflect the room, not the sky.
            Transform probeTransform = Child(lighting, "Reflection Probe");
            probeTransform.localPosition = new Vector3(0f, Height / 2f, CenterZ);
            ReflectionProbe reflection = probeTransform.gameObject.AddComponent<ReflectionProbe>();
            reflection.mode = ReflectionProbeMode.Baked;
            reflection.size = new Vector3(HalfWidth * 2f, Height, Depth);
            reflection.boxProjection = true;
            reflection.resolution = 128;

            // Static so the bake includes them; batching static also cuts draw calls on Quest.
            const StaticEditorFlags flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            // Chairs still cast baked shadows but take their own light from the probes:
            // 40+ of them would crowd the lightmap for no visible gain.
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (InsideChair(r.transform, root))
                    r.receiveGI = ReceiveGI.LightProbes;

            // The backdrops outside the windows are only scenery: keep them out of the bake.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == OutsideName)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

            // The sun shines in through the windows. Baked, it costs nothing on Quest.
            string sun = "";
            GameObject sunObject = FindRoot(scene, "Directional Light");
            Light sunLight = sunObject != null ? sunObject.GetComponent<Light>() : null;
            if (sunLight != null)
            {
                Undo.RecordObject(sunLight, "Build Classroom Lobby");
                Undo.RecordObject(sunLight.transform, "Build Classroom Lobby");
                sunLight.lightmapBakeType = LightmapBakeType.Baked;
                sunLight.transform.rotation = Quaternion.Euler(SunRotation);
                sun = "Set the Directional Light to Baked and aimed it through the windows. ";
            }

            return sun + ConfigureLightmapping();
        }

        private static bool InsideChair(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name == "Chair") return true;
            return false;
        }

        private static string ConfigureLightmapping()
        {
            if (!Lightmapping.TryGetLightingSettings(out LightingSettings settings))
            {
                settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
                if (settings == null)
                {
                    settings = new LightingSettings { name = Path.GetFileNameWithoutExtension(LightingSettingsPath) };
                    AssetDatabase.CreateAsset(settings, LightingSettingsPath);
                }
                Lightmapping.lightingSettings = settings;
            }

            Undo.RecordObject(settings, "Build Classroom Lobby");
            settings.bakedGI = true;
            settings.realtimeGI = false;
            // 12 texels per metre with compressed, non-directional maps keeps the bake small for Quest.
            settings.lightmapResolution = 12f;
            settings.lightmapMaxSize = 1024;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            // Baked contact shadows in corners and under furniture.
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.maxBounces = 2;
            EditorUtility.SetDirty(settings);
            return "Added light probes and a reflection probe, and set lightmapping up for Quest.";
        }

        // ---------------------------------------------------------------- furniture

        private static Vector3 BuildLectern(Transform root)
        {
            Transform lectern = Child(root, "Lectern");
            lectern.position = new Vector3(2.8f, 0f, FrontZ - 0.8f);
            Material oak = Mat("Oak", "B57A45", 0.35f);
            Box("Body", lectern, new Vector3(0f, 0.52f, 0f), new Vector3(0.6f, 1.04f, 0.45f), oak, false);
            // Flat top so the apple placed on it stays put.
            Box("Top", lectern, new Vector3(0f, 1.06f, 0f), new Vector3(0.66f, 0.04f, 0.5f), oak, false);
            AddBoxCollider(lectern.gameObject, new Vector3(0f, 0.55f, 0f), new Vector3(0.66f, 1.1f, 0.5f));
            return lectern.position + new Vector3(0f, 1.1f, 0f);
        }

        private static Vector3 BuildTables(Transform root)
        {
            Transform tables = Child(root, "Tables");
            Material top = Mat("Tabletop", "A9ACB1", 0.3f);
            Material frame = Mat("TableFrame", "3E4247", 0.4f);
            int index = 0;
            foreach (float z in TableRowsZ)
            {
                foreach (float x in TableColumnsX)
                {
                    Transform table = Table("Table_" + index++, tables, new Vector3(x, 0f, z), top, frame);
                    // Two chairs behind each table, facing the front.
                    Chair(table, new Vector3(x - 0.27f, 0f, z - 0.45f), 180f);
                    Chair(table, new Vector3(x + 0.27f, 0f, z - 0.45f), 180f);
                }
            }
            return new Vector3(TableColumnsX[2], TableHeight, TableRowsZ[0]);
        }

        private static Transform Table(string name, Transform parent, Vector3 floorPosition, Material topMat, Material frameMat)
        {
            Transform table = Child(parent, name);
            table.position = floorPosition;
            Box("Top", table, new Vector3(0f, TableHeight - 0.015f, 0f), new Vector3(TableWidth, 0.03f, TableDepth), topMat, false);
            foreach (float x in new[] { -0.45f, 0.45f })
            {
                Box("Post", table, new Vector3(x, (TableHeight - 0.03f) / 2f, 0f), new Vector3(0.06f, TableHeight - 0.03f, 0.06f), frameMat, false);
                Box("Foot", table, new Vector3(x, 0.015f, 0f), new Vector3(0.06f, 0.03f, TableDepth - 0.05f), frameMat, false);
            }
            // One collider for the whole table so players can't walk through it.
            AddBoxCollider(table.gameObject, new Vector3(0f, TableHeight / 2f, 0f), new Vector3(TableWidth, TableHeight, TableDepth));
            return table;
        }

        private static void Chair(Transform parent, Vector3 floorCenter, float yaw)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ChairModelPath);
            if (model == null)
            {
                Debug.LogWarning($"ClassroomLobbyBuilder: {ChairModelPath} not found, skipping chairs.");
                return;
            }

            var chair = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            chair.name = "Chair";
            chair.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            chair.transform.localScale = Vector3.one;

            // Kenney models pivot at a corner, so size and place the chair from its bounds.
            Bounds bounds = RendererBounds(chair);
            if (bounds.size.y > 0f)
                chair.transform.localScale = Vector3.one * (ChairHeight / bounds.size.y);
            bounds = RendererBounds(chair);
            chair.transform.position += new Vector3(floorCenter.x - bounds.center.x, floorCenter.y - bounds.min.y, floorCenter.z - bounds.center.z);

            // Grey like the real mesh chairs.
            Material grey = Mat("ChairGrey", "BCC1C6", 0.3f);
            foreach (Renderer r in chair.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = grey;
                r.sharedMaterials = mats;
            }
        }

        // ---------------------------------------------------------------- wall fixtures

        private static void Whiteboard(Transform wall, float x, float centerY, float width, float height)
        {
            Transform board = Child(wall, "Whiteboard");
            board.localPosition = new Vector3(x, centerY, 0f);
            Material oak = Mat("Oak", "B57A45", 0.35f);
            Box("Frame", board, new Vector3(0f, 0f, -0.015f), new Vector3(width + 0.1f, height + 0.1f, 0.03f), oak, false);
            Box("Surface", board, new Vector3(0f, 0f, -0.032f), new Vector3(width, height, 0.01f), Mat("Whiteboard", "F4F5F3", 0.8f), false);
            Box("Tray", board, new Vector3(0f, -height / 2f - 0.07f, -0.05f), new Vector3(width, 0.03f, 0.08f), oak, false);
        }

        private static void Window(Transform wall, float x, float width)
        {
            Transform window = Child(wall, "Window");
            window.localPosition = new Vector3(x, 0f, 0f);
            Material frame = Mat("WindowFrame", "E9E9E4", 0.4f);

            // See-through glass in the middle of the wall opening. It keeps players in but casts
            // no shadow, so sun and sky reach the room in the bake.
            GameObject glass = Box("Glass", window, new Vector3(0f, (WindowBottom + WindowTop) / 2f, WallThickness / 2f),
                new Vector3(width, WindowTop - WindowBottom, 0.01f), WindowGlassMaterial(), true);
            glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Bright backdrop a little way outside instead of a modelled outdoors. No shadows, so it doesn't block the sun.
            GameObject outside = Box(OutsideName, window, new Vector3(0f, 1.75f, 1.5f), new Vector3(width + 2f, 2.6f, 0.01f), Mat("Daylight", "EAF4FF", 0.9f, 1.2f), false);
            outside.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Box("Frame Top", window, new Vector3(0f, 2.6f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.05f), frame, false);
            Box("Frame Bottom", window, new Vector3(0f, 0.9f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.05f), frame, false);
            Box("Frame Left", window, new Vector3(-width / 2f, 1.75f, -0.03f), new Vector3(0.06f, 1.76f, 0.05f), frame, false);
            Box("Frame Right", window, new Vector3(width / 2f, 1.75f, -0.03f), new Vector3(0.06f, 1.76f, 0.05f), frame, false);
            Box("Mullion", window, new Vector3(0f, 1.75f, -0.03f), new Vector3(0.05f, 1.7f, 0.04f), frame, false);
            Box("Sill", window, new Vector3(0f, 0.86f, -0.1f), new Vector3(width + 0.15f, 0.04f, 0.2f), Mat("Oak", "B57A45", 0.35f), false);

            // Roller shade, about half down.
            Box("Shade Housing", window, new Vector3(0f, 2.68f, -0.08f), new Vector3(width + 0.1f, 0.12f, 0.12f), frame, false);
            Box("Shade", window, new Vector3(0f, 2.2f, -0.07f), new Vector3(width, 0.84f, 0.01f), Mat("RollerShade", "A8A7A2", 0.1f), false);
            Box("Shade Bar", window, new Vector3(0f, 1.78f, -0.07f), new Vector3(width, 0.03f, 0.03f), Mat("TableFrame", "3E4247", 0.4f), false);

            // Soft skylight through the open glass below the shade, shining into the room (local -Z).
            // The sun itself comes through the opening in the bake.
            AreaLight("Daylight Light", window, new Vector3(0f, 1.35f, -0.08f), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(width - 0.1f, 0.8f), DaylightColor, WindowIntensity);
        }

        // Bake-only rectangle light. It shines along its local +Z and costs nothing at runtime.
        private static void AreaLight(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size, Color color, float intensity)
        {
            Transform t = Child(parent, name);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            Light light = t.gameObject.AddComponent<Light>();
            light.type = UnityEngine.LightType.Rectangle;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.areaSize = size;
            light.color = color;
            light.intensity = intensity;
        }

        private static void Door(Transform wall, float x, bool exitSign)
        {
            Transform door = Child(wall, "Door");
            door.localPosition = new Vector3(x, 0f, 0f);
            Material frame = Mat("DoorFrame", "3F4A55");
            Box("Door", door, new Vector3(0f, 1.05f, -0.025f), new Vector3(0.9f, 2.1f, 0.05f), Mat("Oak", "B57A45", 0.35f), false);
            Box("Window", door, new Vector3(0.22f, 1.4f, -0.052f), new Vector3(0.14f, 0.9f, 0.005f), Mat("Glass", "CFE3EC", 0.9f), false);
            Box("Handle", door, new Vector3(-0.35f, 1.0f, -0.07f), new Vector3(0.14f, 0.03f, 0.04f), Mat("Housing", "D9D9D6"), false);
            Box("Jamb Left", door, new Vector3(-0.48f, 1.08f, -0.03f), new Vector3(0.06f, 2.16f, 0.07f), frame, false);
            Box("Jamb Right", door, new Vector3(0.48f, 1.08f, -0.03f), new Vector3(0.06f, 2.16f, 0.07f), frame, false);
            Box("Header", door, new Vector3(0f, 2.13f, -0.03f), new Vector3(1.02f, 0.06f, 0.07f), frame, false);
            if (exitSign)
                ExitSign(door, new Vector3(0f, 2.4f, 0f));
        }

        // The screen faces local -Z (toward the back of the room at yaw 0).
        private static void Tv(Transform parent, string name, Vector3 localCenter, Vector3 size, float yaw = 0f)
        {
            Transform tv = Child(parent, name);
            tv.localPosition = localCenter;
            tv.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box("Bezel", tv, Vector3.zero, size, Mat("TvBezel", "1B1C1E", 0.5f), false);
            Box("Screen", tv, new Vector3(0f, 0f, -size.z / 2f - 0.001f), new Vector3(size.x - 0.04f, size.y - 0.04f, 0.002f), Mat("TvScreen", "0B0C0E", 0.9f), false);
            Pole(tv, new Vector3(0f, size.y / 2f, 0f), Height - (tv.position.y + size.y / 2f));
        }

        private static void Baseboard(Transform wall, float length)
        {
            Box("Baseboard", wall, new Vector3(0f, 0.05f, -0.008f), new Vector3(length, 0.1f, 0.015f), Mat("Baseboard", "6B6E6A", 0.3f), false);
        }

        // ---------------------------------------------------------------- materials

        // bakeGlow: the glowing surface also lights the room when baking (light bars, ceiling panels).
        private static Material Mat(string name, string hex, float smoothness = 0.2f, float emission = 0f, bool bakeGlow = false)
        {
            if (Materials.TryGetValue(name, out Material cached)) return cached;
            string path = $"{MaterialFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                ColorUtility.TryParseHtmlString("#" + hex, out Color color);
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * emission);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                AssetDatabase.CreateAsset(mat, path);
            }

            // The glow only shows with the _EMISSION keyword on, and it was not being saved with new
            // materials. Turn it on here every build so older copies of these materials get fixed too.
            var emissionKeyword = new LocalKeyword(mat.shader, "_EMISSION");
            if (emission > 0f && !mat.IsKeywordEnabled(emissionKeyword))
            {
                mat.SetKeyword(emissionKeyword, true);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }

            MaterialGlobalIlluminationFlags gi = bakeGlow ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.None;
            if (emission > 0f && mat.globalIlluminationFlags != gi)
            {
                mat.globalIlluminationFlags = gi;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }

            Materials[name] = mat;
            return mat;
        }

        // URP Lit set to transparent: a faint tint with sharp reflections. Like the others, made once and then reused.
        private static Material WindowGlassMaterial()
        {
            const string name = "WindowGlass";
            bool isNew = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat") == null;
            Material mat = Mat(name, "DCEBF2", 0.95f);
            if (isNew)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                Color tint = mat.GetColor("_BaseColor");
                tint.a = 0.12f;
                mat.SetColor("_BaseColor", tint);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        // Wall-mounted exit sign, 11.6" x 7.5" x 1.7" like the real one: white housing with a printed face.
        private static void ExitSign(Transform wall, Vector3 localCenter)
        {
            Transform sign = Child(wall, "Exit Sign");
            sign.localPosition = localCenter;
            Box("Housing", sign, new Vector3(0f, 0f, -0.022f), new Vector3(0.295f, 0.19f, 0.044f), Mat("ExitSignHousing", "F2F1EC", 0.4f), false);

            // A quad shows its face toward local -Z, into the room, so the text reads the right way round.
            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(sign, false);
            face.transform.localPosition = new Vector3(0f, 0f, -0.0445f);
            face.transform.localScale = new Vector3(0.285f, 0.18f, 1f);
            face.GetComponent<MeshRenderer>().sharedMaterial = ExitSignMaterial();
        }

        // The sign face: the art texture as the base, and a second texture that makes only the letters glow.
        private static Material ExitSignMaterial()
        {
            const string name = "ExitSignFace";
            string facePath = $"{MaterialFolder}/{name}.png";
            string glowPath = $"{MaterialFolder}/{name}Glow.png";
            if (!File.Exists(facePath) || !File.Exists(glowPath))
                WriteExitSignTextures(facePath, glowPath);

            bool isNew = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat") == null;
            Material mat = Mat(name, "FFFFFF", 0.4f, 1f);
            if (isNew)
            {
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(facePath));
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(glowPath));
                mat.SetColor("_EmissionColor", Color.white * 1.5f);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        // Draws the face from simple shapes, traced from the reference photo: red block letters with
        // a pinched X, and faint embossed chevrons either side.
        private static void WriteExitSignTextures(string facePath, string glowPath)
        {
            const int w = 512, h = 330;
            Color housing = new Color(0.95f, 0.945f, 0.925f);
            Color red = new Color(0.9f, 0.12f, 0.05f);
            Color chevron = new Color(0.83f, 0.83f, 0.81f);

            // Sign space: u runs 0..1 left to right, v runs 0..1 top to bottom. Letters are centred.
            var letters = new List<Vector2[]>
            {
                // E
                Quad2D(0.170f, 0.12f, 0.240f, 0.88f), Quad2D(0.170f, 0.12f, 0.320f, 0.22f),
                Quad2D(0.170f, 0.45f, 0.305f, 0.55f), Quad2D(0.170f, 0.78f, 0.320f, 0.88f),
                // X: two crossing strokes
                new[] { new Vector2(0.340f, 0.12f), new Vector2(0.400f, 0.12f), new Vector2(0.500f, 0.88f), new Vector2(0.440f, 0.88f) },
                new[] { new Vector2(0.440f, 0.12f), new Vector2(0.500f, 0.12f), new Vector2(0.400f, 0.88f), new Vector2(0.340f, 0.88f) },
                // I
                Quad2D(0.530f, 0.12f, 0.600f, 0.88f),
                // T
                Quad2D(0.640f, 0.12f, 0.830f, 0.23f), Quad2D(0.700f, 0.12f, 0.770f, 0.88f),
            };
            var left = new[]
            {
                new Vector2(0.11f, 0.36f), new Vector2(0.03f, 0.5f), new Vector2(0.11f, 0.64f),
                new Vector2(0.135f, 0.64f), new Vector2(0.055f, 0.5f), new Vector2(0.135f, 0.36f),
            };
            var right = new Vector2[left.Length];
            for (int i = 0; i < left.Length; i++) right[i] = new Vector2(1f - left[i].x, left[i].y);

            var face = new Texture2D(w, h);
            var glow = new Texture2D(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x + 0.5f) / w, 1f - (y + 0.5f) / h);
                    bool letter = false;
                    foreach (Vector2[] shape in letters) letter |= InsidePolygon(shape, p);
                    bool arrow = !letter && (InsidePolygon(left, p) || InsidePolygon(right, p));
                    face.SetPixel(x, y, letter ? red : arrow ? chevron : housing);
                    glow.SetPixel(x, y, letter ? red : Color.black);
                }
            }

            foreach ((Texture2D texture, string path) in new[] { (face, facePath), (glow, glowPath) })
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
        }

        private static Vector2[] Quad2D(float u0, float v0, float u1, float v1) =>
            new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };

        // Even-odd test, so concave shapes like the chevrons work.
        private static bool InsidePolygon(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static Material CeilingMaterial()
        {
            const string name = "CeilingTile";
            string texturePath = $"{MaterialFolder}/{name}.png";
            if (!File.Exists(texturePath))
            {
                // One 0.6 m tile with a thin grey grid line on two edges; tiling draws the rest.
                const int size = 128;
                var texture = new Texture2D(size, size);
                Color tile = new Color(0.95f, 0.95f, 0.93f), line = new Color(0.74f, 0.74f, 0.72f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        texture.SetPixel(x, y, x < 3 || y < 3 ? line : tile);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            bool isNew = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat") == null;
            Material mat = Mat(name, "FFFFFF", 0.1f);
            if (isNew)
            {
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                mat.SetTextureScale("_BaseMap", new Vector2(HalfWidth * 2f / 0.6f, Depth / 0.6f));
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        // ---------------------------------------------------------------- helpers

        private static Transform Group(Transform parent, string name, Vector3 position, float yaw)
        {
            Transform group = Child(parent, name);
            group.localPosition = position;
            group.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return group;
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material mat, bool keepCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            if (!keepCollider)
                Object.DestroyImmediate(box.GetComponent<BoxCollider>());
            if (mat != null)
                box.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return box;
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material mat)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return cylinder;
        }

        // Thin rod from a point up to the ceiling. Length defaults to "the rest of the way up".
        private static void Pole(Transform parent, Vector3 localBottom, float length = -1f)
        {
            float worldBottom = parent.TransformPoint(localBottom).y;
            if (length < 0f) length = Height - worldBottom;
            if (length <= 0.01f) return;
            GameObject pole = Cylinder("Pole", parent, Vector3.zero, new Vector3(0.025f, length / 2f, 0.025f), Mat("TableFrame", "3E4247", 0.4f));
            pole.transform.position = parent.TransformPoint(localBottom) + Vector3.up * (length / 2f);
            pole.transform.rotation = Quaternion.identity;
        }

        // Flat floor overlay, fanned out from a point that can see every edge. No collider.
        private static void FloorPatch(string name, Transform parent, List<Vector2> outline, Vector2 fanCenter, float y, Material mat)
        {
            var vertices = new List<Vector3> { new Vector3(fanCenter.x, y, fanCenter.y) };
            foreach (Vector2 p in outline) vertices.Add(new Vector3(p.x, y, p.y));

            var triangles = new List<int>();
            for (int i = 0; i < outline.Count; i++)
            {
                int a = 0, b = 1 + i, c = 1 + (i + 1) % outline.Count;
                // Keep every triangle facing up, whichever way the outline winds.
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (n.y < 0f) (b, c) = (c, b);
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Lightmap UVs, so the patch can take baked light like the rest of the floor.
            Unwrapping.GenerateSecondaryUVSet(mesh);

            string meshPath = $"{MaterialFolder}/{name.Replace(" ", "")}.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void AddBoxCollider(GameObject target, Vector3 center, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
