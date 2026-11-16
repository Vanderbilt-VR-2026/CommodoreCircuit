using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Add Boost Canister to Selected Scooter
    ///
    /// Builds a "Boost Canister" in place of the white cover on the lower handlebar stem
    /// (StemCover): same spot, height and tilt, facing the rider. A smoky tube with gold
    /// caps, tick marks at the Mini and Super charge levels, and a glowing fill driven by
    /// ScooterBoostGauge. The white cover and its two lemon emblems are hidden, not
    /// deleted. It sits on the steering assembly, so it turns with the bars and spins
    /// with a spin-out. Scooters without a StemCover get it strapped beside the stem.
    ///
    /// Safe to run again: it replaces the old canister.
    /// CommodoreCircuit > Remove Boost Canister from Selected Scooter takes it off and
    /// brings the white cover back. Materials are saved under Assets/Materials.
    /// </summary>
    public static class BoostCanisterBuilder
    {
        private const string CanisterName = "Boost Canister";
        private const string CoverName = "StemCover";
        private const string EmblemPrefix = "Emblem";
        private const string MatDir = "Assets/Materials";

        // Used only when the scooter has no StemCover: scooter-local base beside the stem.
        private static readonly Vector3 FallbackBase = new Vector3(0.085f, 0.72f, 0.52f);
        private const float FallbackHeight = 0.3f;
        private const float FallbackRadius = 0.045f;

        [MenuItem("CommodoreCircuit/Add Boost Canister to Selected Scooter")]
        private static void Build()
        {
            GameObject scooter = Selection.activeGameObject;
            var controller = scooter.GetComponent<ScooterController>();
            var so = new SerializedObject(controller);

            RemoveCanister(scooter);

            Material tube = Mat("BoostCanister_Tube", "Universal Render Pipeline/Lit", new Color(0.12f, 0.13f, 0.16f), 0.85f);
            Material gold = Mat("BoostCanister_Gold", "Universal Render Pipeline/Lit", new Color(0.81f, 0.68f, 0.44f), 0.6f);
            Material glow = Mat("BoostCanister_Fill", "Universal Render Pipeline/Unlit", Color.white, 0f);
            Material tick = Mat("BoostCanister_Tick", "Universal Render Pipeline/Unlit", new Color(0.95f, 0.95f, 0.92f), 0f);

            var root = new GameObject(CanisterName);
            Undo.RegisterCreatedObjectUndo(root, "Add Boost Canister");

            float height, radius;
            Transform cover = FindDeep(scooter.transform, CoverName);
            if (cover != null)
            {
                // Take the cover's place: same parent (it turns with the bars), height and tilt,
                // as wide as the cover. The base sits at the bottom of the cover.
                height = cover.localScale.y;
                radius = Mathf.Max(cover.localScale.x, cover.localScale.z) * 0.5f;
                root.transform.SetParent(cover.parent, false);
                root.transform.localRotation = cover.localRotation;
                root.transform.localPosition = cover.localPosition - cover.localRotation * new Vector3(0f, height * 0.5f, 0f);
                SetCoverVisible(cover.parent, false);
            }
            else
            {
                height = FallbackHeight;
                radius = FallbackRadius;
                Transform mount = so.FindProperty("frontSteerAssembly").objectReferenceValue as Transform;
                root.transform.SetParent(mount != null ? mount : scooter.transform, false);
                root.transform.position = scooter.transform.TransformPoint(FallbackBase);
                root.transform.rotation = scooter.transform.rotation;
            }

            float d = radius * 2f;
            Part(root.transform, "Tube", PrimitiveType.Cylinder, new Vector3(0f, height / 2f, 0f), new Vector3(d, height / 2f, d), tube);

            var pivot = new GameObject("Fill Pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localScale = new Vector3(1f, 0.0001f, 1f);
            // A touch wider than the tube, so the glow covers it where it's filled.
            Renderer fill = Part(pivot, "Fill", PrimitiveType.Cylinder, new Vector3(0f, height / 2f, 0f), new Vector3(d * 1.06f, height / 2f, d * 1.06f), glow);
            fill.enabled = false;

            Part(root.transform, "Cap Top", PrimitiveType.Cylinder, new Vector3(0f, height + 0.008f, 0f), new Vector3(d * 1.3f, 0.012f, d * 1.3f), gold);
            Part(root.transform, "Cap Bottom", PrimitiveType.Cylinder, new Vector3(0f, -0.008f, 0f), new Vector3(d * 1.3f, 0.012f, d * 1.3f), gold);

            // Tick rings where Mini and Super are reached (Ultra is the top).
            float ultra = so.FindProperty("ultraChargeTime").floatValue;
            if (ultra > 0f)
            {
                float mini = so.FindProperty("miniChargeTime").floatValue / ultra;
                float super = so.FindProperty("superChargeTime").floatValue / ultra;
                Part(root.transform, "Tick Mini", PrimitiveType.Cylinder, new Vector3(0f, height * mini, 0f), new Vector3(d * 1.14f, 0.003f, d * 1.14f), tick);
                Part(root.transform, "Tick Super", PrimitiveType.Cylinder, new Vector3(0f, height * super, 0f), new Vector3(d * 1.14f, 0.003f, d * 1.14f), tick);
            }

            // Beside the stem it needs straps; in the cover's place the stem runs through it.
            if (cover == null)
                foreach (float y in new[] { height * 0.2f, height * 0.8f })
                    Part(root.transform, "Strap", PrimitiveType.Cube, new Vector3(-(radius + 0.012f), y, 0f), new Vector3(0.035f, 0.02f, 0.025f), gold);

            var gauge = root.AddComponent<ScooterBoostGauge>();
            var gso = new SerializedObject(gauge);
            gso.FindProperty("scooter").objectReferenceValue = controller;
            gso.FindProperty("fillPivot").objectReferenceValue = pivot;
            gso.FindProperty("fillRenderer").objectReferenceValue = fill;
            gso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scooter.scene);
            Selection.activeGameObject = root;
            Debug.Log("Boost canister added to " + scooter.name + (cover != null ? " in place of the white stem cover" : "") + ". Save the scene, then drift to fill it.");
        }

        [MenuItem("CommodoreCircuit/Remove Boost Canister from Selected Scooter")]
        private static void Remove()
        {
            GameObject scooter = Selection.activeGameObject;
            RemoveCanister(scooter);
            Transform cover = FindDeep(scooter.transform, CoverName);
            if (cover != null) SetCoverVisible(cover.parent, true);
            EditorSceneManager.MarkSceneDirty(scooter.scene);
            Debug.Log("Boost canister removed from " + scooter.name + "; the white stem cover is back. Save the scene.");
        }

        [MenuItem("CommodoreCircuit/Remove Boost Canister from Selected Scooter", true)]
        private static bool ValidateRemove() => ValidateBuild();

        private static void RemoveCanister(GameObject scooter)
        {
            foreach (Transform t in scooter.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == CanisterName) Undo.DestroyObjectImmediate(t.gameObject);
        }

        // The white cover and the lemon emblems on its sides, which the canister replaces.
        private static void SetCoverVisible(Transform assembly, bool visible)
        {
            foreach (Transform t in assembly)
            {
                if (t.name != CoverName && !t.name.StartsWith(EmblemPrefix)) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                Undo.RecordObject(r, visible ? "Show stem cover" : "Hide stem cover");
                r.enabled = visible;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        [MenuItem("CommodoreCircuit/Add Boost Canister to Selected Scooter", true)]
        private static bool ValidateBuild()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        private static Renderer Part(Transform parent, string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            // Purely visual: a collider here could catch the scooter's own wall checks.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        private static Material Mat(string name, string shaderName, Color color, float smoothness)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;   // reuse, so hand tweaks survive a rebuild
            Shader shader = Shader.Find(shaderName)
                ?? Shader.Find(shaderName.EndsWith("Unlit") ? "Unlit/Color" : "Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
