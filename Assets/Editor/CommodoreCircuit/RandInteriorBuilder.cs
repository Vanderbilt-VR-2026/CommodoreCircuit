using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Build Rand Interior
    ///
    /// Dresses the inside of the Rand shortcut (Assets/CampusTrack/rand_shortcut.json) to look like
    /// the real walk through Sarratt and Rand, in race order:
    ///   1. Sarratt atrium: in through glass doors onto dark slate tile with the thin yellow dashes,
    ///      under the gold ceiling full of round downlights. Its curved edge glows; past it, over the
    ///      bleacher steps on the left, the ceiling goes up to a skylight. Round grey pillars, dark
    ///      speckled carpet and lounge chairs on the right. The part of the steps next to the road
    ///      is a ramp you can ride up and over.
    ///   2. A brick column and a side door on the left (decoration: it's behind the wall, not a way out).
    ///   3. Rand dining concourse, straight on: beige tile laid long side across, bands of small dark
    ///      tile at the edges, yellow serving counters with stools on the right, wood tables on carpet
    ///      on the left, white columns with wall lights, square ceiling lights and hanging light bars.
    ///   4. Right turn into the hallway: package lockers on the right under a row of 2x4 ceiling
    ///      lights, a dark tile strip, freezer door and water fountain on the left.
    ///   5. Out through the glass doors: black mat, pebbly concrete and brick pavers under the
    ///      concrete overhang with its one amber light.
    ///
    /// Floors and ceilings are textured from patterns drawn here (Materials/RandInterior/Textures),
    /// sized to the real tiles. Lamps are bake-only, like the rest of the campus: they show after
    /// Generate Lighting, along with the light probes and reflection probes added here. To look at
    /// them before baking, use Rand Interior Lights: Realtime Preview (and switch back before baking).
    ///
    /// Everything but the floor finish and the ramp sits behind the shortcut's invisible walls,
    /// so only the ramp changes where you can drive. Where each part starts is the "interior"
    /// block of the JSON, in metres along the shortcut: tweak it there and run this again.
    ///
    /// Build Rand Shortcut runs this at the end. Running it again replaces "Rand Interior".
    /// </summary>
    public static class RandInteriorBuilder
    {
        private const string DataPath = "Assets/CampusTrack/rand_shortcut.json";
        private const string MeshDir = "Assets/CampusTrack/InteriorMeshes";
        private const string MatDir = "Assets/CampusTrack/Materials/RandInterior";
        private const string TexDir = MatDir + "/Textures";
        private const string RootName = "Rand Interior";

        // Floor finish sits just above the shortcut's floor (0.095) and worn strip (0.1), so inside
        // the buildings the dashed line takes over from the worn strip as the hint. Inlays go on top.
        private const float FloorY = 0.105f;
        private const float InlayY = 0.108f;
        private const float RampBaseY = 0.09f;

        // Light colours, read off the photos: warm gold atrium, neutral concourse, the amber porch light.
        private static readonly Color Warm = new Color(1f, 0.83f, 0.64f);
        private static readonly Color Neutral = new Color(1f, 0.92f, 0.8f);
        private static readonly Color Daylight = new Color(0.86f, 0.92f, 1f);
        private static readonly Color Amber = new Color(1f, 0.72f, 0.38f);

        [Serializable] private class Meta { public float wallOffset, tunnelHalfWidth, tunnelHeight; }
        [Serializable]
        private class Interior
        {
            public float sarrattDoor, atriumEnd, rampStart, rampUp, rampTop, rampDown, rampHeight, rampInner;
            public float hallwayStart, randDoor, buildingExit;
            public float concourseCeiling, hallwayCeiling, entryCeiling, hallwayHalfWidth;
        }
        [Serializable] private class Data { public Meta meta; public Interior interior; public float[] centerline; }

        private static Vector3[] path;
        private static float[] along;
        private static int meshCount;
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [MenuItem("CommodoreCircuit/Build Rand Interior")]
        private static void BuildMenu()
        {
            string msg = Build();
            Debug.Log("Rand interior: " + msg);
            EditorUtility.DisplayDialog("Rand interior", msg + "\n\nSave the scene (Cmd+S).", "OK");
        }

        /// <summary>Builds the interior in the open scene. Returns a one-line summary.</summary>
        public static string Build()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (json == null) return $"{DataPath} not found.";
            Data data = JsonUtility.FromJson<Data>(json.text);
            if (data.interior == null || data.interior.randDoor <= 0f) return $"No \"interior\" block in {DataPath}.";
            SetPath(data.centerline);

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder(MeshDir);
            EnsureFolder(TexDir);
            Materials.Clear();
            meshCount = 0;

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Rand Interior");

            Interior iv = data.interior;
            Meta m = data.meta;
            Atrium(Group(root, "1 Sarratt Atrium"), iv, m);
            Transition(Group(root, "2 Brick Column and Side Door"), iv, m);
            Concourse(Group(root, "3 Dining Concourse"), iv, m);
            Hallway(Group(root, "4 Hallway"), iv, m);
            Entrance(Group(root, "5 Covered Entrance"), iv, m);
            Probes(Group(root, "Probes"), iv, m);

            MarkStatic(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            int lamps = root.GetComponentsInChildren<Light>().Length;
            return $"Interior built along {along[along.Length - 1]:0} m of shortcut ({meshCount} meshes, {lamps} lamps). " +
                   "Lamps are bake-only: Generate Lighting to see them, or use Rand Interior Lights: Realtime Preview.";
        }

        // Static so the bake includes it. Floors, walls and ceilings (our meshes, with lightmap UVs)
        // take lightmaps, at double density so the pools of light under the fittings show; the small
        // props are lit by the light probes. Text stays dynamic.
        private static void MarkStatic(GameObject root)
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr.GetComponent<TMP_Text>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject, flags);
                Mesh mesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
                bool ours = mesh != null && AssetDatabase.GetAssetPath(mesh).StartsWith(MeshDir);
                mr.receiveGI = ours ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                if (!ours) continue;
                var so = new SerializedObject(mr);
                SerializedProperty scale = so.FindProperty("m_ScaleInLightmap");
                if (scale != null) { scale.floatValue = 2f; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        // ---- 1. Sarratt atrium ----

        private static void Atrium(Transform g, Interior iv, Meta m)
        {
            float s0 = iv.sarrattDoor, s1 = iv.atriumEnd, w = m.tunnelHalfWidth, top = m.tunnelHeight;
            const float gold = 4.3f;

            Doorway(g, "Sarratt Doors", s0, m.wallOffset + 0.2f, w, 2.5f, top, Mat("WallCream", "EAE4D2"));

            // Floor: big dark slate tiles with a sheen, carpet on the right with a metal edge, and the
            // thin yellow dashes down the middle (about 5 cm by 50 cm, a metre apart, like the real ones).
            Flat(g, "Floor Slate Tile", s0, s1, -w, w, FloorY, SarrattFloor());
            float c0 = s0 + 3f, c1 = s1 - 5f, cEdge = -m.wallOffset - 0.4f;
            Flat(g, "Carpet", c0, c1, -w, cEdge, InlayY, CarpetFloor());
            Flat(g, "Carpet Edge", c0, c1, cEdge - 0.02f, cEdge + 0.02f, InlayY + 0.001f, Mat("Railing", "C9CCD1", 0.7f));
            Material dash = Mat("DashYellow", "D9AE36", 0.3f);
            for (float s = s0 + 0.5f; s + 0.5f < s1; s += 1f)
                Box(g, "Dash", s + 0.25f, 0f, InlayY - 0.002f, new Vector3(0.06f, 0.004f, 0.5f), dash);

            // Ceiling: the gold lower ceiling over the road and the right, its curved edge out over the
            // bleachers, glowing along its face. Past it, the high white ceiling with the skylight.
            Func<float, float> edge = s => 1.5f + 2.5f * Mathf.Sin(Mathf.PI * Mathf.InverseLerp(s0, s1, s));
            Material goldMat = Mat("AtriumCeilingGold", "AE9852", 0.15f);
            Flat(g, "Ceiling High", s0, s1, -w, w, top - 0.05f, Mat("CeilingWhite", "E8E6E0"), true);
            Flat(g, "Ceiling Gold", s0 + 0.3f, s1, _ => -w, edge, gold, goldMat, true);
            Side(g, "Ceiling Gold Edge", s0 + 0.3f, s1, edge, gold, gold + 0.3f, goldMat, towardLeft: true);
            Side(g, "Cove Glow", s0 + 0.3f, s1, s => edge(s) + 0.005f, gold + 0.3f, gold + 0.45f, Mat("CoveGlow", "FFD98A", 0f, 1.6f), towardLeft: true);
            Flat(g, "Ceiling Gold Top", s0 + 0.3f, s1, _ => -w, edge, gold + 0.45f, goldMat);

            float skyA = 4.6f, skyB = w - 0.5f, sky0 = s0 + 4f, sky1 = s1 - 5f;
            Flat(g, "Skylight", sky0, sky1, skyA, skyB, top - 0.07f, Mat("Skylight", "DDEBF5", 0f, 1.4f), true);
            AreaDown(g, "Skylight Light", (sky0 + sky1) / 2f, (skyA + skyB) / 2f, top - 0.1f, new Vector2(skyB - skyA, sky1 - sky0), Daylight, 6f);

            // Round downlights all over the gold ceiling, each with a baked spot.
            Material downlight = Mat("Downlight", "FFF4DE", 0f, 2f);
            for (float s = s0 + 1.5f; s < s1 - 0.5f; s += 2.5f)
                for (float lat = -w + 1f; lat < edge(s) - 0.7f; lat += 2.2f)
                {
                    Cylinder(g, "Downlight", s, lat, gold - 0.02f, 0.11f, 0.02f, downlight);
                    SpotDown(g, s, lat, gold - 0.05f, Warm, 2.2f, 7f, 110f);
                }
            // The cove washes the high ceiling.
            for (float s = s0 + 3f; s < s1 - 1f; s += 4f)
                PointLight(g, "Cove Light", s, edge(s) + 0.4f, gold + 0.6f, Warm, 0.9f, 5f);

            // Right: cream wall with the photo mural, fire extinguisher, sign kiosk, lounge chairs on the carpet.
            Side(g, "Wall Right", s0, s1, -w + 0.05f, 0f, gold, Mat("WallCream", "EAE4D2"));
            Side(g, "Photo Mural", s1 - 7f, s1 - 0.5f, -w + 0.07f, 0.3f, 3.2f, Mat("Mural", "2E3442", 0.2f));
            Box(g, "Fire Extinguisher Cabinet", s1 - 8.5f, -w + 0.15f, 1.0f, new Vector3(0.2f, 0.7f, 0.35f), Mat("ExtinguisherRed", "B3282D", 0.4f));
            Box(g, "Sign Kiosk", s1 - 2.5f, -w + 0.6f, 0f, new Vector3(0.3f, 2.2f, 1.1f), Mat("KioskNavy", "1E2A44"));
            Box(g, "Sign Kiosk Screen", s1 - 2.5f, -w + 0.76f, 0.9f, new Vector3(0.02f, 1.1f, 0.9f), Mat("Screen", "B9C9E6", 0.6f, 0.8f));
            LoungeChair(g, s0 + 5f, -w + 1.1f);
            LoungeChair(g, s0 + 7.5f, -w + 1.1f);

            // Round pillars.
            Material pillar = Mat("PillarGrey", "A9AEA9", 0.25f);
            Cylinder(g, "Pillar", s0 + 1f, m.wallOffset + 0.8f, 0f, 0.45f, top, pillar);
            Cylinder(g, "Pillar", s0 + 3f, -m.wallOffset - 1f, 0f, 0.45f, gold, pillar);
            Cylinder(g, "Pillar", s1 - 4.5f, -m.wallOffset - 1f, 0f, 0.45f, gold, pillar);

            Bleachers(g, iv, m);
        }

        // The bleacher steps on the left. The run next to the road is a ramp (up, flat, down)
        // you can drive over; behind it the real steps climb to the brick wall.
        private static void Bleachers(Transform g, Interior iv, Meta m)
        {
            float r0 = iv.rampStart, r3 = r0 + iv.rampUp + iv.rampTop + iv.rampDown;
            float inner = iv.rampInner, outer = m.wallOffset;
            Func<float, float> height = s =>
            {
                if (s <= r0 || s >= r3) return 0f;
                if (s < r0 + iv.rampUp) return iv.rampHeight * Mathf.SmoothStep(0f, 1f, (s - r0) / iv.rampUp);
                if (s > r3 - iv.rampDown) return iv.rampHeight * Mathf.SmoothStep(0f, 1f, (r3 - s) / iv.rampDown);
                return iv.rampHeight;
            };

            Mesh ramp = RampMesh(r0, r3, inner, outer, height);
            var go = new GameObject("Bleacher Ramp");
            go.transform.SetParent(g, false);
            go.AddComponent<MeshFilter>().sharedMesh = ramp;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { Mat("BleacherDark", "2F3238"), Mat("BleacherWood", "A87A4E", 0.3f) };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = ramp;

            // Railing along the ramp's outer edge, so the edge reads from the saddle.
            Material rail = Mat("Railing", "C9CCD1", 0.7f);
            var tops = new List<Vector3>();
            for (float s = r0 + 1f; s <= r3 - 1f + 0.01f; s += 1.5f)
            {
                Vector3 foot = Point(s, outer + 0.15f, RampBaseY + height(s));
                Vector3 top = foot + Vector3.up * 1f;
                Rod(g, "Rail Post", foot, top, 0.04f, rail);
                tops.Add(top);
            }
            for (int i = 0; i + 1 < tops.Count; i++) Rod(g, "Rail", tops[i], tops[i + 1], 0.05f, rail);

            // Real steps behind the wall, rising toward the brick.
            Material riser = Mat("BleacherDark", "2F3238"), tread = Mat("BleacherWood", "A87A4E", 0.3f);
            float back = m.tunnelHalfWidth - 0.05f, start = outer + 0.4f, depth = (back - start) / 4f;
            for (int k = 0; k < 4; k++)
            {
                float a = start + depth * k, h = 0.45f * (k + 1);
                float lat = (a + back) / 2f, wide = back - a, mid = (r0 + r3) / 2f, len = r3 - r0 + 2f;
                Box(g, "Step", mid, lat, 0f, new Vector3(wide, h, len), riser);
                Box(g, "Step Tread", mid, a + 0.25f, h, new Vector3(0.5f, 0.03f, len), tread);
            }
        }

        // ---- 2. Brick column and side door ----

        private static void Transition(Transform g, Interior iv, Meta m)
        {
            float s = iv.atriumEnd, w = m.tunnelHalfWidth;
            Box(g, "Brick Column", s - 0.5f, m.wallOffset + 1.1f, 0f, new Vector3(1.0f, m.tunnelHeight, 1.0f), Brick());

            // A glass door in the left wall with a mat and an exit sign: there, but not for scooters.
            float d = s + 1.5f;
            Material frame = Mat("FrameBlack", "141416", 0.4f);
            Box(g, "Side Door Frame", d, w - 0.08f, 0f, new Vector3(0.12f, 2.5f, 2.0f), frame);
            Box(g, "Side Door Glass", d, w - 0.15f, 0.08f, new Vector3(0.02f, 2.3f, 1.8f), Glass());
            Box(g, "Side Door Bar", d, w - 0.25f, 1.0f, new Vector3(0.05f, 0.06f, 1.4f), Mat("Railing", "C9CCD1", 0.7f));
            Box(g, "Side Door Exit Sign", d, w - 0.12f, 2.75f, new Vector3(0.06f, 0.18f, 0.35f), Mat("ExitGreen", "2FD06A", 0f, 2f));
            Flat(g, "Side Door Mat", d - 1.1f, d + 1.1f, m.wallOffset + 0.4f, w - 0.2f, InlayY + 0.002f, EntryMat());
            PointLight(g, "Exit Sign Glow", d, w - 0.4f, 2.8f, new Color(0.5f, 1f, 0.6f), 0.3f, 2f);
        }

        // ---- 3. Dining concourse ----

        private static void Concourse(Transform g, Interior iv, Meta m)
        {
            float s0 = iv.atriumEnd, s1 = iv.hallwayStart, w = m.tunnelHalfWidth, ceil = iv.concourseCeiling;
            Material white = Mat("ColumnWhite", "EEEDE8"), cream = Mat("WallCream", "EAE4D2");

            // Floor: beige tile down the middle, a band of small dark tile each side (all the way to the
            // counters on the right), carpet under the tables on the left.
            const float band = 4.8f;
            Flat(g, "Floor Beige Tile", s0, s1, -band, band, FloorY, BeigeFloor());
            Flat(g, "Floor Dark Tile Right", s0, s1, -w, -band, FloorY, DarkTileFloor());
            Flat(g, "Floor Dark Tile Left", s0, s1, band, m.wallOffset + 0.4f, FloorY, DarkTileFloor());
            Flat(g, "Seating Carpet", s0, s1, m.wallOffset + 0.4f, w, FloorY, CarpetFloor());

            // Drop ceiling, and the wall above it where the atrium's tall ceiling ends.
            Flat(g, "Ceiling Tiles", s0, s1, -w, w, ceil, CeilingTiles(), true);
            Box(g, "Bulkhead", s0, 0f, ceil, new Vector3(2f * w, m.tunnelHeight - ceil, 0.2f), cream);
            Side(g, "Wall Left", s0, s1, w - 0.05f, 0f, ceil, cream);
            Side(g, "Wall Right", s0, s1, -w + 0.05f, 0f, ceil, cream);

            // Square 2x2 ceiling lights down the middle, hanging light bars over the sides.
            Material glow = Mat("LightPanel", "FFFDF4", 0f, 1.6f), rod = Mat("FrameBlack", "141416", 0.4f);
            for (float s = s0 + 2.4f; s < s1 - 0.5f; s += 4.8f)
            {
                Box(g, "Troffer", s, 0f, ceil - 0.02f, new Vector3(0.6f, 0.02f, 0.6f), glow);
                AreaDown(g, "Troffer Light", s, 0f, ceil - 0.04f, new Vector2(0.6f, 0.6f), Neutral, 7f);
            }
            for (float s = s0 + 2f; s < s1 - 1f; s += 4f)
                foreach (float lat in new[] { -3f, 3f })
                {
                    Box(g, "Light Bar", s, lat, ceil - 0.55f, new Vector3(0.12f, 0.06f, 1.6f), glow);
                    Rod(g, "Light Bar Rod", Point(s, lat, ceil - 0.5f), Point(s, lat, ceil), 0.015f, rod);
                    PointLight(g, "Light Bar Light", s, lat, ceil - 0.65f, Neutral, 1.1f, 6f);
                }

            // White square columns on both sides, each with a tall wall light on its aisle face.
            Material sconce = Mat("Sconce", "FFF6E6", 0f, 1.4f);
            for (float s = s0 + 2f; s < s1 - 1f; s += 5f)
                foreach (float side in new[] { -1f, 1f })
                {
                    float lat = side * (m.wallOffset + 0.75f), face = lat - side * 0.36f;
                    Box(g, "Column", s, lat, 0f, new Vector3(0.7f, ceil, 0.7f), white);
                    Box(g, "Wall Light", s, face, 1.9f, new Vector3(0.04f, 0.6f, 0.14f), sconce);
                    PointLight(g, "Wall Light Glow", s, face - side * 0.2f, 2.2f, Warm, 0.6f, 3.5f);
                }

            // Right: yellow serving stations with a counter ledge and stools facing them.
            Material yellow = Mat("StationYellow", "F2C318", 0.2f), counter = Mat("CounterGrey", "8C8D8F", 0.5f);
            Material stool = Mat("Wood", "BF9466", 0.25f);
            float back = -w + 0.05f, front = -m.wallOffset - 0.85f;
            for (float s = s0 + 4.5f; s + 2f < s1 - 6f; s += 5f)
            {
                Box(g, "Serving Station", s, (back + front) / 2f, 0f, new Vector3(front - back, 2.6f, 4.0f), yellow);
                Box(g, "Counter Ledge", s, front + 0.2f, 1.05f, new Vector3(0.45f, 0.05f, 3.8f), counter);
                for (float k = -1.2f; k <= 1.21f; k += 1.2f) Stool(g, s + k, front + 0.55f, stool);
            }
            Box(g, "Menu Sign", s1 - 7f, -m.wallOffset - 0.8f, 0f, new Vector3(0.25f, 2.0f, 0.9f), Mat("KioskNavy", "1E2A44"));
            Box(g, "Menu Sign Screen", s1 - 7f, -m.wallOffset - 0.66f, 1.0f, new Vector3(0.02f, 0.8f, 0.75f), Mat("Screen", "B9C9E6", 0.6f, 0.8f));

            // Left: wood tables and chairs, between the columns and level with the stations.
            for (float s = s0 + 4.5f; s < s1 - 6f; s += 5f)
                TableAndChairs(g, s, m.wallOffset + 1.4f);
        }

        // ---- 4. Hallway ----

        private static void Hallway(Transform g, Interior iv, Meta m)
        {
            float s0 = iv.hallwayStart, s1 = iv.randDoor, hw = iv.hallwayHalfWidth, ceil = iv.hallwayCeiling, w = m.tunnelHalfWidth;
            Material cream = Mat("WallCream", "EAE4D2");

            // Beige tile, with a strip of the small dark tile along the left wall.
            const float strip = 5.2f;
            Flat(g, "Floor Beige Tile", s0, s1, -hw, strip, FloorY, BeigeFloor());
            Flat(g, "Floor Dark Tile Strip", s0, s1, strip, hw, FloorY, DarkTileFloor());
            Flat(g, "Ceiling Tiles", s0, s1, -hw, hw, ceil, CeilingTiles(), true);
            Side(g, "Wall Left", s0, s1, hw, 0f, ceil, cream);
            Side(g, "Wall Right", s0, s1, -hw, 0f, ceil, cream);

            // Where the hallway leaves the concourse: wall returns either side and the step down in ceiling.
            float wide = iv.concourseCeiling;
            Box(g, "Opening Return Left", s0, (hw + w) / 2f, 0f, new Vector3(w - hw, wide, 0.2f), cream);
            Box(g, "Opening Return Right", s0, -(hw + w) / 2f, 0f, new Vector3(w - hw, wide, 0.2f), cream);
            Box(g, "Opening Bulkhead", s0, 0f, ceil, new Vector3(2f * hw, wide - ceil, 0.2f), cream);

            // 2x4 ceiling lights over the locker side, long side across; a round speaker now and then.
            Material glow = Mat("LightPanel", "FFFDF4", 0f, 1.6f), speaker = Mat("Speaker", "C8C2B4", 0.2f);
            for (float s = s0 + 1.5f; s < s1 - 0.5f; s += 3f)
            {
                Box(g, "Troffer", s, -2.5f, ceil - 0.02f, new Vector3(1.2f, 0.02f, 0.6f), glow);
                AreaDown(g, "Troffer Light", s, -2.5f, ceil - 0.04f, new Vector2(1.2f, 0.6f), Neutral, 6f);
            }
            for (float s = s0 + 3f; s < s1 - 1f; s += 6f)
                Cylinder(g, "Speaker", s, 1.5f, ceil - 0.02f, 0.2f, 0.02f, speaker);
            Box(g, "Exit Sign", s1 - 1.5f, 0f, ceil - 0.45f, new Vector3(0.35f, 0.18f, 0.06f), Mat("ExitGreen", "2FD06A", 0f, 2f));

            // Right: package lockers, a column of doors every 1.2 m, so they follow the turn.
            Material navy = Mat("LockerNavy", "1D2430", 0.3f), seam = Mat("LockerSeam", "0E1218");
            float lockerFace = -hw + 0.7f;
            float l0 = s0 + 3f, l1 = Mathf.Min(s0 + 15f, s1 - 3f);
            for (float s = l0; s + 1.2f <= l1; s += 1.2f)
            {
                float c = s + 0.6f;
                Box(g, "Locker Column", c, (lockerFace - hw) / 2f, 0f, new Vector3(0.65f, 2.1f, 1.18f), navy);
                for (float y = 0.35f; y < 2.1f; y += 0.35f)
                    Box(g, "Locker Seam", c, lockerFace + 0.005f, y, new Vector3(0.01f, 0.015f, 1.1f), seam);
            }
            Label(g, "Order.\nAnd pick\nup here.", l0 + 0.6f, lockerFace + 0.02f, 1.4f, Facing(l0 + 0.6f, -1f), 0.18f, Color.white);

            // Left: water fountain and the freezer door.
            Material steel = Mat("Steel", "B8BBBF", 0.6f);
            float wall = hw - 0.05f;
            Box(g, "Water Fountain", s0 + 2.5f, wall - 0.25f, 0.75f, new Vector3(0.5f, 0.25f, 0.6f), steel);
            Box(g, "Water Fountain Back", s0 + 2.5f, wall - 0.03f, 0.75f, new Vector3(0.06f, 0.6f, 0.6f), steel);
            float fd = Mathf.Min(s0 + 11f, s1 - 3f);
            Box(g, "Freezer Door Frame", fd, wall - 0.03f, 0f, new Vector3(0.06f, 2.45f, 1.4f), Mat("ColumnWhite", "EEEDE8"));
            Box(g, "Freezer Door", fd, wall - 0.08f, 0.03f, new Vector3(0.06f, 2.3f, 1.2f), steel);
            Box(g, "Freezer Door Kick Plate", fd, wall - 0.115f, 0.05f, new Vector3(0.01f, 1.0f, 1.1f), Mat("DiamondPlate", "D2D4D6", 0.8f));
            Box(g, "Freezer Door Window", fd, wall - 0.115f, 1.3f, new Vector3(0.01f, 0.8f, 0.35f), Mat("WindowDark", "1A1C20", 0.8f));
            Box(g, "Freezer Door Handle", fd - 0.45f, wall - 0.15f, 1.0f, new Vector3(0.05f, 0.25f, 0.05f), steel);
        }

        // ---- 5. Out the glass doors into the covered entrance ----

        private static void Entrance(Transform g, Interior iv, Meta m)
        {
            float s0 = iv.randDoor, s1 = iv.buildingExit, hw = iv.hallwayHalfWidth, ceil = iv.entryCeiling;

            Doorway(g, "Rand Doors", s0, m.wallOffset + 0.2f, hw, 2.4f, ceil, Brick());

            // Black mat, then pebbly concrete, then the band of brick pavers at the edge of the overhang.
            Flat(g, "Door Mat", s0, s0 + 2.2f, -hw, hw, FloorY, EntryMat());
            Flat(g, "Aggregate Concrete", s0 + 2.2f, s1 - 3.5f, -hw, hw, FloorY, AggregateFloor());
            Flat(g, "Brick Pavers", s1 - 3.5f, s1, -hw, hw, FloorY, PaverFloor());
            Flat(g, "Overhang Ceiling", s0, s1, -hw, hw, ceil, Mat("OverhangConcrete", "CFC6B4", 0.05f), true);
            Side(g, "Brick Wall Left", s0, s1, hw, 0f, ceil, Brick());
            Side(g, "Brick Wall Right", s0, s1, -hw, 0f, ceil, Brick());
            Box(g, "Overhang Edge", s1, 0f, ceil, new Vector3(2f * hw + 0.4f, m.tunnelHeight - ceil, 0.3f), Mat("OverhangConcrete", "CFC6B4", 0.05f));

            // The one square amber light in the overhang.
            float lamp = s0 + 3f;
            Box(g, "Overhang Light", lamp, 0f, ceil - 0.05f, new Vector3(0.45f, 0.05f, 0.45f), Mat("AmberGlow", "FFC860", 0f, 1.8f));
            SpotDown(g, lamp, 0f, ceil - 0.08f, Amber, 2.5f, 8f, 120f);

            // The "okay, we're open." board on the right, facing the way you're riding.
            float b = s1 - 5f, lat = -m.wallOffset - 0.5f;
            Transform board = Group(g, "Sandwich Board");
            board.SetPositionAndRotation(Point(b, lat, 0f), Facing(b, 0f));
            Material black = Mat("FrameBlack", "141416", 0.4f), whiteBoard = Mat("BoardWhite", "F4F4F2");
            foreach (float side in new[] { -1f, 1f })
            {
                Transform leaf = Group(board, side < 0 ? "Front" : "Back");
                leaf.localPosition = new Vector3(0f, 0f, side * 0.18f);
                leaf.localRotation = Quaternion.Euler(-side * 11f, 0f, 0f);
                LocalBox(leaf, "Frame", new Vector3(0f, 0.5f, 0f), new Vector3(0.66f, 1.0f, 0.03f), black);
                LocalBox(leaf, "Face", new Vector3(0f, 0.55f, side * 0.017f), new Vector3(0.58f, 0.8f, 0.005f), whiteBoard);
            }
            var text = new GameObject("Text").AddComponent<TextMeshPro>();
            text.transform.SetParent(board.Find("Front"), false);
            text.transform.localPosition = new Vector3(0f, 0.6f, -0.022f);
            SetText(text, "okay, we're\nopen.", 0.11f, Color.black, new Vector2(0.56f, 0.5f));
        }

        // ---- Light probes and reflection probes ----

        // Probes on a grid through the whole interior, so the scooter, riders and small props pick
        // up the baked light, and box-projected reflection probes so the slate and tile floors
        // reflect the fittings above them.
        private static void Probes(Transform g, Interior iv, Meta m)
        {
            var positions = new List<Vector3>();
            for (float s = iv.sarrattDoor; s <= iv.buildingExit; s += 3f)
            {
                bool narrow = s >= iv.hallwayStart;
                float ceil = s < iv.atriumEnd ? 4.0f : narrow ? iv.hallwayCeiling - 0.4f : iv.concourseCeiling - 0.4f;
                float half = (narrow ? iv.hallwayHalfWidth : m.tunnelHalfWidth) - 1f;
                foreach (float lat in new[] { -half, 0f, half })
                    foreach (float y in new[] { 0.8f, 2.0f, ceil })
                        positions.Add(Point(s, lat, y));
            }
            var probesGo = new GameObject("Light Probes");
            probesGo.transform.SetParent(g, false);
            probesGo.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();

            AddReflectionProbe(g, "Reflections Atrium", iv.sarrattDoor, iv.atriumEnd, m.tunnelHalfWidth, m.tunnelHeight);
            AddReflectionProbe(g, "Reflections Concourse", iv.atriumEnd, iv.hallwayStart, m.tunnelHalfWidth, iv.concourseCeiling);
            AddReflectionProbe(g, "Reflections Hallway", iv.hallwayStart, iv.randDoor, iv.hallwayHalfWidth, iv.hallwayCeiling);
        }

        // Reflection probes can't turn, so the box is the world-aligned bounds of this stretch.
        private static void AddReflectionProbe(Transform g, string name, float s0, float s1, float half, float height)
        {
            var bounds = new Bounds(Point(s0, 0f, 0f), Vector3.zero);
            foreach (float s in Steps(s0, s1))
                foreach (float lat in new[] { -half, half })
                {
                    bounds.Encapsulate(Point(s, lat, 0f));
                    bounds.Encapsulate(Point(s, lat, height));
                }
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.position = bounds.center;
            var probe = go.AddComponent<UnityEngine.ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = bounds.size;
            probe.resolution = 128;
            probe.importance = 2;
        }

        // ---- Lamps (bake-only: free at runtime, they show after Generate Lighting) ----

        private static void SpotDown(Transform g, float s, float lat, float y, Color color, float intensity, float range, float angle)
        {
            Light light = AddLight(g, "Downlight Spot", UnityEngine.LightType.Spot, Point(s, lat, y), DownRotation(s), color, intensity);
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.4f;
        }

        // Rectangle light facing down: a ceiling panel. Size is across, along.
        private static void AreaDown(Transform g, string name, float s, float lat, float y, Vector2 size, Color color, float intensity)
        {
            Light light = AddLight(g, name, UnityEngine.LightType.Rectangle, Point(s, lat, y), DownRotation(s), color, intensity);
            light.areaSize = size;
        }

        private static void PointLight(Transform g, string name, float s, float lat, float y, Color color, float intensity, float range)
        {
            AddLight(g, name, UnityEngine.LightType.Point, Point(s, lat, y), Quaternion.identity, color, intensity).range = range;
        }

        private static Light AddLight(Transform g, string name, UnityEngine.LightType type, Vector3 position, Quaternion rotation, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(position, rotation);
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;   // baked shadows cost nothing at runtime
            return light;
        }

        // Pointing straight down, with the light's up along the shortcut so an area light's size is across, along.
        private static Quaternion DownRotation(float s)
        {
            Frame(s, out _, out Vector3 forward, out _);
            return Quaternion.LookRotation(Vector3.down, forward);
        }

        [MenuItem("CommodoreCircuit/Rand Interior Lights: Realtime Preview")]
        private static void TogglePreview()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null) { EditorUtility.DisplayDialog("Rand interior", "No \"Rand Interior\" in this scene.", "OK"); return; }
            var lights = new List<Light>();
            foreach (Light l in root.GetComponentsInChildren<Light>())
                if (l.type != UnityEngine.LightType.Rectangle) lights.Add(l);   // area lights only bake
            bool toRealtime = lights.Exists(l => l.lightmapBakeType == LightmapBakeType.Baked);
            foreach (Light l in lights)
            {
                Undo.RecordObject(l, "Rand interior light preview");
                l.lightmapBakeType = toRealtime ? LightmapBakeType.Realtime : LightmapBakeType.Baked;
                l.shadows = toRealtime ? LightShadows.None : LightShadows.Soft;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorUtility.DisplayDialog("Rand interior", toRealtime
                ? $"{lights.Count} lamps are realtime now, so you can see them without baking. The ceiling panels and skylight only show once baked.\n\nRun this again to switch back before you bake or build: realtime lamps are too costly on Quest."
                : $"{lights.Count} lamps are back to bake-only.", "OK");
        }

        // ---- Props ----

        // Glass doors across the shortcut at s: open in the middle (the road), glass side panels out
        // to the walls, a transom over the top, and solid wall from there up to `top`.
        private static void Doorway(Transform g, string name, float s, float open, float half, float head, float top, Material wall)
        {
            Transform d = Group(g, name);
            Material frame = Mat("FrameBlack", "141416", 0.4f), glass = Glass();
            float transomTop = Mathf.Min(head + 0.9f, top - 0.1f);
            foreach (float side in new[] { -1f, 1f })
            {
                float a = open, b = half, mid = (a + b) / 2f;
                Box(d, "Post", s, side * a, 0f, new Vector3(0.12f, head, 0.15f), frame);
                Box(d, "Post", s, side * (b - 0.06f), 0f, new Vector3(0.12f, head, 0.15f), frame);
                Box(d, "Mullion", s, side * mid, 0f, new Vector3(0.08f, head, 0.12f), frame);
                Box(d, "Side Glass", s, side * mid, 0f, new Vector3(b - a, head, 0.02f), glass);
                Box(d, "Push Bar", s - 0.08f, side * (a + 0.4f), 1.0f, new Vector3(0.6f, 0.05f, 0.05f), frame);
            }
            Box(d, "Head", s, 0f, head, new Vector3(2f * half, 0.15f, 0.18f), frame);
            Box(d, "Transom Glass", s, 0f, head + 0.15f, new Vector3(2f * half, transomTop - head - 0.15f, 0.02f), glass);
            foreach (float lat in new[] { -half * 0.33f, half * 0.33f })
                Box(d, "Transom Mullion", s, lat, head + 0.15f, new Vector3(0.08f, transomTop - head - 0.15f, 0.12f), frame);
            Box(d, "Transom Head", s, 0f, transomTop, new Vector3(2f * half, 0.12f, 0.18f), frame);
            if (top - transomTop > 0.15f)
                Box(d, "Wall Above", s, 0f, transomTop + 0.12f, new Vector3(2f * half, top - transomTop - 0.12f, 0.3f), wall);
        }

        private static void LoungeChair(Transform g, float s, float lat)
        {
            Material leather = Mat("Leather", "4A4E57", 0.35f);
            Box(g, "Lounge Seat", s, lat, 0f, new Vector3(0.9f, 0.45f, 0.95f), leather);
            Box(g, "Lounge Back", s, lat - 0.4f, 0.45f, new Vector3(0.18f, 0.45f, 0.95f), leather);
            Box(g, "Lounge Arm", s - 0.42f, lat, 0.45f, new Vector3(0.9f, 0.2f, 0.14f), leather);
            Box(g, "Lounge Arm", s + 0.42f, lat, 0.45f, new Vector3(0.9f, 0.2f, 0.14f), leather);
        }

        private static void Stool(Transform g, float s, float lat, Material wood)
        {
            Cylinder(g, "Stool Seat", s, lat, 0.72f, 0.19f, 0.05f, wood);
            Box(g, "Stool Back", s, lat + 0.17f, 0.77f, new Vector3(0.03f, 0.35f, 0.34f), wood);
            foreach (float ds in new[] { -0.12f, 0.12f })
                foreach (float dl in new[] { -0.12f, 0.12f })
                    Box(g, "Stool Leg", s + ds, lat + dl, 0f, new Vector3(0.035f, 0.72f, 0.035f), wood);
        }

        private static void TableAndChairs(Transform g, float s, float lat)
        {
            Material wood = Mat("Wood", "BF9466", 0.25f);
            Box(g, "Table Top", s, lat, 0.72f, new Vector3(0.9f, 0.04f, 0.9f), wood);
            Box(g, "Table Leg", s, lat, 0f, new Vector3(0.08f, 0.72f, 0.08f), Mat("FrameBlack", "141416", 0.4f));
            foreach (float side in new[] { -1f, 1f })
            {
                float cs = s + side * 0.7f;
                Box(g, "Chair Seat", cs, lat, 0.44f, new Vector3(0.42f, 0.04f, 0.42f), wood);
                Box(g, "Chair Back", cs + side * 0.2f, lat, 0.48f, new Vector3(0.42f, 0.45f, 0.03f), wood);
                foreach (float ds in new[] { -0.18f, 0.18f })
                    foreach (float dl in new[] { -0.18f, 0.18f })
                        Box(g, "Chair Leg", cs + ds, lat + dl, 0f, new Vector3(0.03f, 0.44f, 0.03f), wood);
            }
        }

        private static void Label(Transform g, string text, float s, float lat, float y, Quaternion rotation, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(Point(s, lat, y), rotation);
            SetText(go.AddComponent<TextMeshPro>(), text, size, color, new Vector2(1.0f, 0.8f));
        }

        private static void SetText(TextMeshPro tmp, string text, float size, Color color, Vector2 box)
        {
            tmp.text = text;
            // Shrinks to fit the box if the text runs long. TMP's world-space size is roughly tenths of a metre.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = size * 10f;
            tmp.fontSizeMin = size * 2f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = box;
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---- Placing things along the shortcut ----

        private static void SetPath(float[] xz)
        {
            path = new Vector3[xz.Length / 2];
            for (int i = 0; i < path.Length; i++) path[i] = new Vector3(xz[i * 2], 0f, xz[i * 2 + 1]);
            along = new float[path.Length];
            for (int i = 1; i < path.Length; i++) along[i] = along[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        }

        // Position and direction of travel s metres along the shortcut; left is to the left of travel.
        private static void Frame(float s, out Vector3 pos, out Vector3 forward, out Vector3 left)
        {
            s = Mathf.Clamp(s, 0f, along[along.Length - 1]);
            int i = 0;
            while (i < path.Length - 2 && along[i + 1] < s) i++;
            float t = Mathf.InverseLerp(along[i], along[i + 1], s);
            pos = Vector3.Lerp(path[i], path[i + 1], t);
            // Blend toward the next segment's direction near its start, so props on curves turn smoothly.
            Vector3 a = path[i + 1] - path[i], b = i + 2 < path.Length ? path[i + 2] - path[i + 1] : a;
            forward = Vector3.Slerp(a.normalized, b.normalized, t * 0.5f).normalized;
            left = new Vector3(-forward.z, 0f, forward.x);
        }

        private static Vector3 Point(float s, float lateral, float y)
        {
            Frame(s, out Vector3 pos, out _, out Vector3 left);
            return pos + left * lateral + Vector3.up * y;
        }

        // Facing along the shortcut; turnToLeft = 1 faces left of travel, -1 right, 0 straight on.
        private static Quaternion Facing(float s, float turnToLeft)
        {
            Frame(s, out _, out Vector3 forward, out Vector3 left);
            Vector3 dir = turnToLeft == 0f ? forward : left * turnToLeft;
            return Quaternion.LookRotation(dir, Vector3.up);
        }

        private static Transform Group(GameObject parent, string name) => Group(parent.transform, name);

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // A box s metres along, `lateral` to the left (negative: right), its bottom y above the ground.
        // Size is across, up, along the shortcut.
        private static GameObject Box(Transform parent, string name, float s, float lateral, float y, Vector3 size, Material mat)
        {
            GameObject box = Primitive(PrimitiveType.Cube, parent, name, mat);
            box.transform.SetPositionAndRotation(Point(s, lateral, y + size.y / 2f), Facing(s, 0f));
            box.transform.localScale = size;
            return box;
        }

        private static GameObject LocalBox(Transform parent, string name, Vector3 localPosition, Vector3 size, Material mat)
        {
            GameObject box = Primitive(PrimitiveType.Cube, parent, name, mat);
            box.transform.localPosition = localPosition;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = size;
            return box;
        }

        private static GameObject Cylinder(Transform parent, string name, float s, float lateral, float y, float radius, float height, Material mat)
        {
            GameObject c = Primitive(PrimitiveType.Cylinder, parent, name, mat);
            c.transform.SetPositionAndRotation(Point(s, lateral, y + height / 2f), Facing(s, 0f));
            c.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            return c;
        }

        // A thin bar from a to b.
        private static void Rod(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            GameObject r = Primitive(PrimitiveType.Cube, parent, name, mat);
            Vector3 d = b - a;
            r.transform.SetPositionAndRotation((a + b) / 2f, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.99f ? Vector3.forward : Vector3.up));
            r.transform.localScale = new Vector3(thickness, thickness, d.magnitude);
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---- Meshes that follow the curve ----

        private static void Flat(Transform parent, string name, float s0, float s1, float latA, float latB, float y, Material mat, bool facesDown = false)
            => Flat(parent, name, s0, s1, _ => latA, _ => latB, y, mat, facesDown);

        // A flat strip from s0 to s1 between two lateral offsets (left positive), at height y.
        private static void Flat(Transform parent, string name, float s0, float s1, Func<float, float> latA, Func<float, float> latB, float y, Material mat, bool facesDown = false)
        {
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            foreach (float s in Steps(s0, s1))
            {
                a.Add(Point(s, latA(s), y));
                b.Add(Point(s, latB(s), y));
            }
            Part(parent, name, Strip(name, a, b, facesDown ? Vector3.down : Vector3.up), mat);
        }

        private static void Side(Transform parent, string name, float s0, float s1, float lateral, float y0, float y1, Material mat)
            => Side(parent, name, s0, s1, _ => lateral, y0, y1, mat, lateral < 0f);

        // An upright wall from s0 to s1 at a lateral offset, facing the middle of the shortcut
        // (or toward the left when towardLeft is set).
        private static void Side(Transform parent, string name, float s0, float s1, Func<float, float> lateral, float y0, float y1, Material mat, bool towardLeft)
        {
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            foreach (float s in Steps(s0, s1))
            {
                a.Add(Point(s, lateral(s), y0));
                b.Add(Point(s, lateral(s), y1));
            }
            Frame((s0 + s1) / 2f, out _, out _, out Vector3 left);
            Part(parent, name, Strip(name, a, b, towardLeft ? left : -left), mat);
        }

        private static IEnumerable<float> Steps(float s0, float s1)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 0.5f));
            for (int k = 0; k <= n; k++) yield return Mathf.Lerp(s0, s1, k / (float)n);
        }

        // Quads between two matching rows of points, turned so they face `normal`.
        // UVs are in metres: u across from row a, v along it, so the tile textures come out real size.
        private static Mesh Strip(string name, List<Vector3> a, List<Vector3> b, Vector3 normal)
        {
            int n = a.Count;
            var v = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) dist += Vector3.Distance(a[i], a[i - 1]);
                v[i * 2] = a[i];
                v[i * 2 + 1] = b[i];
                uv[i * 2] = new Vector2(0f, dist);
                uv[i * 2 + 1] = new Vector2(Vector3.Distance(a[i], b[i]), dist);
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int p = i * 2, q = (i + 1) * 2, k = i * 6;
                tris[k] = p; tris[k + 1] = q; tris[k + 2] = p + 1;
                tris[k + 3] = p + 1; tris[k + 4] = q; tris[k + 5] = q + 1;
            }
            Vector3 face = Vector3.Cross(v[2] - v[0], v[1] - v[0]);
            if (Vector3.Dot(face, normal) < 0f) Array.Reverse(tris);
            var mesh = new Mesh { name = name, vertices = v, uv = uv, triangles = tris };
            return Save(mesh);
        }

        // The ride-over part of the bleachers: a top that rises and falls with height(s), two sides,
        // and wood stripes on the top where the steps' treads would be (second submesh).
        private static Mesh RampMesh(float s0, float s1, float inner, float outer, Func<float, float> height)
        {
            var v = new List<Vector3>();
            var dark = new List<int>();
            var wood = new List<int>();
            const float step = 0.3f;
            int n = Mathf.CeilToInt((s1 - s0) / step);
            for (int k = 0; k < n; k++)
            {
                float sa = s0 + (s1 - s0) * k / n, sb = s0 + (s1 - s0) * (k + 1) / n;
                float ha = RampBaseY + height(sa), hb = RampBaseY + height(sb);
                Vector3 ia = Point(sa, inner, ha), oa = Point(sa, outer, ha), ib = Point(sb, inner, hb), ob = Point(sb, outer, hb);
                // Top: wood on every third band, the rest dark.
                Quad(v, k % 3 == 0 ? wood : dark, ia, ib, ob, oa, Vector3.up);
                // Sides, down to the floor.
                Vector3 inDir = Point(sa, inner - 1f, 0f) - Point(sa, inner, 0f);
                Quad(v, dark, Point(sa, inner, RampBaseY), Point(sb, inner, RampBaseY), ib, ia, inDir);
                Quad(v, dark, Point(sa, outer, RampBaseY), Point(sb, outer, RampBaseY), ob, oa, -inDir);
            }
            var mesh = new Mesh { name = "Bleacher Ramp", subMeshCount = 2 };
            mesh.SetVertices(v);
            mesh.SetTriangles(dark, 0);
            mesh.SetTriangles(wood, 1);
            return Save(mesh);
        }

        // Adds quad a-b-c-d (its own vertices, for flat shading) facing `normal`.
        private static void Quad(List<Vector3> v, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (flip) tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            else tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        private static Mesh Save(Mesh mesh)
        {
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            LightmapUVs.Generate(mesh);
            meshCount++;
            AssetDatabase.CreateAsset(mesh, $"{MeshDir}/{meshCount:000} {mesh.name}.asset");
            return mesh;
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---- Floors and ceilings: patterns drawn to the real tile sizes ----
        // Each texture covers a whole number of tiles so it repeats seamlessly. u runs across the
        // shortcut and v along it, so "across" below means side to side as you ride.

        // Sarratt: dark slate, 90 cm across by 45 cm, staggered, mottled, with a sheen.
        private static Material SarrattFloor() => TexMat("SarrattSlateFloor", 3.6f, 0.55f, () =>
            Tiles(1024, 256, 128, 2, Stagger.Rows, 11, new Color32(58, 61, 66, 255), 0.07f, new Color32(40, 42, 46, 255), 0.18f));

        // Rand: beige, 60 cm across by 30 cm, staggered, like the concourse and hallway.
        private static Material BeigeFloor() => TexMat("RandBeigeFloor", 2.4f, 0.45f, () =>
            Tiles(1024, 256, 128, 3, Stagger.Rows, 21, new Color32(217, 205, 182, 255), 0.04f, new Color32(166, 156, 138, 255), 0.06f));

        // The small dark brown tile at the edges: 15 cm across by 30 cm along, staggered along.
        private static Material DarkTileFloor() => TexMat("RandDarkTileFloor", 1.2f, 0.4f, () =>
            Tiles(512, 64, 128, 2, Stagger.Columns, 31, new Color32(80, 70, 63, 255), 0.08f, new Color32(52, 47, 43, 255), 0.1f));

        // Dark charcoal carpet flecked with short grey and tan streaks.
        private static Material CarpetFloor() => TexMat("SpeckledCarpet", 2f, 0.05f, () =>
        {
            const int size = 512;
            var rnd = new Random(41);
            var px = Fill(size, new Color32(42, 44, 50, 255), 0.12f, 8, rnd);
            Color32[] flecks = { new Color32(96, 96, 100, 255), new Color32(119, 112, 95, 255), new Color32(70, 74, 84, 255) };
            for (int i = 0; i < 900; i++)
            {
                int x = rnd.Next(size), y = rnd.Next(size), len = 6 + rnd.Next(30);
                Color32 c = flecks[rnd.Next(flecks.Length)];
                for (int k = 0; k < len; k++)
                    for (int t = 0; t < 2; t++)
                        px[((y + t) % size) * size + (x + k) % size] = c;
            }
            return px;
        });

        // Black ribbed entrance mat.
        private static Material EntryMat() => TexMat("EntryMatRibbed", 1f, 0.05f, () =>
        {
            const int size = 256;
            var px = Fill(size, new Color32(27, 28, 31, 255), 0.1f, 4, new Random(51));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x += 8)
                    for (int t = 0; t < 2; t++) px[y * size + x + t] = new Color32(40, 41, 45, 255);
            return px;
        });

        // Exposed-aggregate concrete: pale with lots of little pebbles.
        private static Material AggregateFloor() => TexMat("AggregateConcrete", 1f, 0.1f, () =>
        {
            const int size = 512;
            var rnd = new Random(61);
            var px = Fill(size, new Color32(185, 176, 162, 255), 0.08f, 16, rnd);
            for (int i = 0; i < 6000; i++)
            {
                int x = rnd.Next(size), y = rnd.Next(size), r = 1 + rnd.Next(3);
                int shade = 110 + rnd.Next(120);
                var c = new Color32((byte)shade, (byte)(shade * 0.95f), (byte)(shade * 0.88f), 255);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy <= r * r)
                            px[((y + dy + size) % size) * size + (x + dx + size) % size] = c;
            }
            return px;
        });

        // Red brick pavers, 20 cm across by 10 cm, staggered, in a range of reds with the odd dark one.
        private static Material PaverFloor() => TexMat("BrickPavers", 0.8f, 0.15f, () =>
        {
            var reds = new[] { new Color32(150, 72, 58, 255), new Color32(168, 88, 72, 255), new Color32(134, 62, 50, 255), new Color32(96, 60, 52, 255) };
            return Tiles(512, 128, 64, 4, Stagger.Rows, 71, reds, 0.08f, new Color32(150, 144, 132, 255), 0.12f);
        });

        // Suspended ceiling: 60 cm off-white tiles, speckled, in a white grid.
        private static Material CeilingTiles() => TexMat("CeilingTileGrid", 1.2f, 0.05f, () =>
        {
            const int size = 256;
            var rnd = new Random(81);
            var px = Tiles(size, 128, 128, 3, Stagger.None, 81, new Color32(226, 224, 216, 255), 0.02f, new Color32(244, 244, 240, 255), 0.04f);
            for (int i = 0; i < 2500; i++)
            {
                int x = rnd.Next(size), y = rnd.Next(size);
                if (x % 128 < 3 || y % 128 < 3) continue;
                px[y * size + x] = new Color32(196, 194, 186, 255);
            }
            return px;
        });

        private enum Stagger { None, Rows, Columns }

        private static Color32[] Tiles(int size, int tileU, int tileV, int grout, Stagger stagger, int seed,
            Color32 baseColor, float tileVariation, Color32 groutColor, float mottle)
            => Tiles(size, tileU, tileV, grout, stagger, seed, new[] { baseColor }, tileVariation, groutColor, mottle);

        // Tiles in a grid. Stagger.Rows shifts every other row (running across, u) half a tile;
        // Stagger.Columns shifts every other column along. Each tile gets its own shade (with several
        // colours, the last is the odd one out, about one tile in twelve), and the whole thing a soft
        // mottle, both repeating with the texture.
        private static Color32[] Tiles(int size, int tileU, int tileV, int grout, Stagger stagger, int seed,
            Color32[] colors, float tileVariation, Color32 groutColor, float mottle)
        {
            var rnd = new Random(seed);
            int cols = size / tileU, rows = size / tileV;
            var shade = new Color[cols * rows];
            for (int i = 0; i < shade.Length; i++)
            {
                Color c = colors.Length == 1 ? colors[0]
                    : rnd.NextDouble() < 0.08 ? colors[colors.Length - 1]
                    : colors[rnd.Next(colors.Length - 1)];
                shade[i] = c * (1f + (float)(rnd.NextDouble() * 2 - 1) * tileVariation);
            }
            float[] noise = PeriodicNoise(size, 32, rnd);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int u = x, v = y;
                    if (stagger == Stagger.Columns && (u / tileU) % 2 == 1) v = (v + tileV / 2) % size;
                    if (stagger == Stagger.Rows && (v / tileV) % 2 == 1) u = (u + tileU / 2) % size;
                    bool isGrout = u % tileU < grout || v % tileV < grout;
                    Color c = isGrout ? (Color)groutColor : shade[(v / tileV) * cols + u / tileU];
                    c *= 1f + (noise[y * size + x] - 0.5f) * mottle;
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            return px;
        }

        // A plain colour with a repeating mottle and fine grain.
        private static Color32[] Fill(int size, Color32 baseColor, float mottle, int cell, Random rnd)
        {
            float[] noise = PeriodicNoise(size, cell, rnd);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                Color c = (Color)baseColor * (1f + (noise[i] - 0.5f) * mottle + (float)(rnd.NextDouble() - 0.5) * mottle * 0.5f);
                c.a = 1f;
                px[i] = c;
            }
            return px;
        }

        // Smooth value noise that wraps at the texture edges, 0..1.
        private static float[] PeriodicNoise(int size, int cell, Random rnd)
        {
            int n = Mathf.Max(1, size / cell);
            var lattice = new float[n * n];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();
            var outp = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)cell, fy = y / (float)cell;
                    int x0 = (int)fx % n, y0 = (int)fy % n, x1 = (x0 + 1) % n, y1 = (y0 + 1) % n;
                    float tx = Mathf.SmoothStep(0f, 1f, fx - (int)fx), ty = Mathf.SmoothStep(0f, 1f, fy - (int)fy);
                    float top = Mathf.Lerp(lattice[y0 * n + x0], lattice[y0 * n + x1], tx);
                    float bottom = Mathf.Lerp(lattice[y1 * n + x0], lattice[y1 * n + x1], tx);
                    outp[y * size + x] = Mathf.Lerp(top, bottom, ty);
                }
            return outp;
        }

        // A material using a drawn texture that repeats every `metres`. The texture is redrawn each
        // build; the material is made once and then left alone, like the others.
        private static Material TexMat(string name, float metres, float smoothness, Func<Color32[]> draw)
        {
            if (Materials.TryGetValue(name, out Material cached)) return cached;
            Texture2D tex = SaveTexture(name, draw());
            string path = $"{MatDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", Vector2.one / metres);
                mat.mainTexture = tex;
                mat.mainTextureScale = Vector2.one / metres;
                AssetDatabase.CreateAsset(mat, path);
            }
            Materials[name] = mat;
            return mat;
        }

        private static Texture2D SaveTexture(string name, Color32[] px)
        {
            int size = (int)Mathf.Sqrt(px.Length);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;   // floors are seen at a glancing angle
            importer.mipmapEnabled = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- Materials (made once, then reused untouched so hand tweaks survive a rebuild) ----

        private static Material Mat(string name, string hex, float smoothness = 0.1f, float emission = 0f)
        {
            if (Materials.TryGetValue(name, out Material cached)) return cached;
            string path = $"{MatDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                ColorUtility.TryParseHtmlString("#" + hex, out Color color);
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * emission);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                AssetDatabase.CreateAsset(mat, path);
            }
            Materials[name] = mat;
            return mat;
        }

        // The campus brick, so the entrance matches the outside of the buildings.
        private static Material Brick()
        {
            var brick = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusScenery/Materials/Brick.mat");
            return brick != null ? brick : Mat("Brick", "8C4A3A");
        }

        private static Material Glass()
        {
            const string name = "DoorGlass";
            bool isNew = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat") == null;
            Material mat = Mat(name, "CFE3EC", 0.95f);
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
                tint.a = 0.15f;
                mat.SetColor("_BaseColor", tint);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
