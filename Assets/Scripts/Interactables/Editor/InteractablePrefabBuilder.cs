#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using static CommodoreCircuit.Interactables.InteractableUtil;
using Object = UnityEngine.Object;

namespace CommodoreCircuit.Interactables.EditorTools
{
    /// <summary>
    /// Tools > Commodore Circuit > Build Interactable Prefabs
    ///
    /// Turns the six FBX models into ready-to-place prefabs: shared toon material,
    /// colliders, Rigidbody, gameplay scripts, default paths and tuning values.
    /// The FBX stays a nested prefab inside each one, so re-exporting a model from
    /// Blender updates every prefab automatically.
    /// </summary>
    public static class InteractablePrefabBuilder
    {
        const string Menu = "Tools/Commodore Circuit/";

        [MenuItem(Menu + "Build Interactable Prefabs (keep existing)")]
        static void BuildMissing() => Build(false);

        [MenuItem(Menu + "Rebuild All Interactable Prefabs (overwrite)")]
        static void RebuildAll()
        {
            if (EditorUtility.DisplayDialog("Rebuild interactable prefabs?",
                    "This overwrites every prefab in " + InteractablePaths.Prefabs +
                    ", including any tuning changes made to them.", "Rebuild", "Cancel"))
                Build(true);
        }

        static void Build(bool overwrite)
        {
            EnsureFolder(InteractablePaths.Prefabs);
            var mat = EnsureMaterial();
            var trailMat = EnsureTrailMaterial();

            int made = 0;
            made += Make("Squirrel", overwrite, mat, (root, model) => Squirrel(root, model));
            made += Make("ConstructionPipe", overwrite, mat, (root, model) => Pipe(root, model));
            made += Make("StrayFrisbee", overwrite, mat, (root, model) => Frisbee(root, model, trailMat));
            made += Make("LostFreshman", overwrite, mat, (root, model) => Freshman(root, model));
            made += Make("SpeedCookie", overwrite, mat, (root, model) => Cookie(root, model));
            made += Make("MealSwipe", overwrite, mat, (root, model) => MealSwipe(root, model));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Commodore Circuit] Built {made} interactable prefab(s) in {InteractablePaths.Prefabs}.");
        }

        // ------------------------------------------------------------------ models

        static void Squirrel(GameObject root, GameObject model)
        {
            var mover = Mover(root, model, out var path, new Vector3(0, 0, 0), new Vector3(6, 0, 0));
            path.activation = PathMover.Activation.Proximity;
            path.triggerRadius = 18f;
            path.tellDuration = 0.6f;
            path.speed = 6f;
            path.endBehavior = PathMover.EndBehavior.PingPong;
            path.cooldown = 2.5f;

            var col = mover.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.38f, 0);
            col.radius = 0.28f;
            col.height = 0.76f;

            var hazard = mover.AddComponent<Hazard>();
            hazard.slowDuration = 1f;
            hazard.slowMultiplier = 0.4f;
            mover.AddComponent<SquirrelAnimator>().model = model.transform;
            Triggers(mover);
        }

        static void Pipe(GameObject root, GameObject model)
        {
            var mover = Mover(root, model, out var path, new Vector3(0, 0, 0), new Vector3(10, 0, 0));
            path.activation = PathMover.Activation.Proximity;
            path.triggerRadius = 22f;
            path.tellDuration = 1f;
            path.speed = 3.5f;
            path.endBehavior = PathMover.EndBehavior.ResetToStart;
            path.cooldown = 2f;
            path.roller = FindDeep(model.transform, "Roller");
            path.rollRadius = 0.467f;

            var col = mover.AddComponent<CapsuleCollider>();
            col.direction = 0;                         // along X = the pipe's axis
            col.center = new Vector3(0, 0.467f, 0);
            col.radius = 0.47f;
            col.height = 1.42f;

            var hazard = mover.AddComponent<Hazard>();
            hazard.slowDuration = 2f;
            hazard.slowMultiplier = 0.25f;
            hazard.knockback = 3f;
            Triggers(mover);
        }

        static void Frisbee(GameObject root, GameObject model, Material trailMat)
        {
            // Handlebar height: path at 0.7 m, arc peaks at ~1.05 m. Never at face height.
            var mover = Mover(root, model, out var path, new Vector3(0, 0.7f, 0), new Vector3(9, 0.7f, 0));
            path.activation = PathMover.Activation.Proximity;
            path.triggerRadius = 20f;
            path.tellDuration = 0.8f;
            path.speed = 7f;
            path.arcHeight = 0.35f;
            path.endBehavior = PathMover.EndBehavior.ResetToStart;
            path.cooldown = 3f;
            path.spinner = FindDeep(model.transform, "Disc");
            path.spinnerSpeed = 900f;

            var col = mover.AddComponent<SphereCollider>();
            col.radius = 0.34f;

            var trail = mover.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = 0.3f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var gold = new Color(1f, 0.78f, 0.17f);
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(gold, 0f), new GradientColorKey(gold, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            trail.sharedMaterial = trailMat;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            path.trail = trail;

            var hazard = mover.AddComponent<Hazard>();
            hazard.slowDuration = 0.5f;
            hazard.slowMultiplier = 0.6f;
            Triggers(mover);
        }

        static void Freshman(GameObject root, GameObject model)
        {
            var mover = Mover(root, model, out var path, new Vector3(0, 0, 0), new Vector3(5, 0, 0));
            path.activation = PathMover.Activation.Continuous;
            path.tellDuration = 0f;                    // the floating "?" is the tell
            path.speed = 0.7f;
            path.endBehavior = PathMover.EndBehavior.PingPong;
            path.cooldown = 2.5f;                      // stops at each end to stare at the map
            path.zigzagAmplitude = 0.4f;
            path.zigzagFrequency = 0.25f;
            path.turnSpeed = 120f;

            var col = mover.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.85f, 0);
            col.radius = 0.38f;
            col.height = 1.7f;

            var hazard = mover.AddComponent<Hazard>();
            hazard.slowDuration = 1f;
            hazard.slowMultiplier = 0.4f;
            hazard.rehitCooldown = 3f;
            mover.AddComponent<FreshmanAnimator>().model = model.transform;
            Triggers(mover);
        }

        static void Cookie(GameObject root, GameObject model)
        {
            PowerUpRoot(root, model, PowerUpType.SpeedBoost);
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.45f;
            var fs = root.AddComponent<FloatAndSpin>();
            fs.target = model.transform;
            fs.sparkles = FindDeep(model.transform, "Sparkles");
            fs.spinSpeed = 90f;
            Triggers(root);
        }

        static void MealSwipe(GameObject root, GameObject model)
        {
            PowerUpRoot(root, model, PowerUpType.Shield);
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.9f, 0.65f, 0.4f);
            var fs = root.AddComponent<FloatAndSpin>();
            fs.target = model.transform;
            fs.sparkles = FindDeep(model.transform, "Sparkles");
            fs.spinSpeed = 70f;
            fs.flipSpeed = 100f;                       // flips end over end
            Triggers(root);
        }

        // ------------------------------------------------------------------ helpers

        static int Make(string name, bool overwrite, Material mat, Action<GameObject, GameObject> configure)
        {
            string path = $"{InteractablePaths.Prefabs}/{name}.prefab";
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return 0;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{InteractablePaths.Models}/{name}.fbx");
            if (fbx == null)
            {
                Debug.LogError($"[Commodore Circuit] Missing model {InteractablePaths.Models}/{name}.fbx");
                return 0;
            }

            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.transform.SetParent(root.transform, false);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterial = mat;
                configure(root, model);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return 1;
        }

        static GameObject Mover(GameObject root, GameObject model, out PathMover path, params Vector3[] points)
        {
            var mover = new GameObject("Mover");
            mover.transform.SetParent(root.transform, false);
            mover.transform.localPosition = points[0];
            model.transform.SetParent(mover.transform, false);
            var rb = mover.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            path = root.AddComponent<PathMover>();
            path.mover = mover.transform;
            path.waypoints = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var w = new GameObject($"Waypoint_{i}");
                w.transform.SetParent(root.transform, false);
                w.transform.localPosition = points[i];
                path.waypoints[i] = w.transform;
            }
            return mover;
        }

        static void PowerUpRoot(GameObject root, GameObject model, PowerUpType type)
        {
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var p = root.AddComponent<PowerUp>();
            p.type = type;
            p.visuals = model;
        }

        static void Triggers(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
        }

        static Material EnsureMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(InteractablePaths.Material);
            if (mat != null) return mat;

            var shader = Shader.Find(InteractablePaths.ToonShader);
            bool toon = shader != null && shader.isSupported;
            if (!toon)
            {
                Debug.LogWarning("[Commodore Circuit] Toon shader unavailable (is the project on URP?). Falling back to Lit.");
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            mat = new Material(shader) { name = "CC_Interactables", enableInstancing = true };
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(InteractablePaths.Palette);
            var emisTex = AssetDatabase.LoadAssetAtPath<Texture2D>(InteractablePaths.PaletteEmission);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTex);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emisTex);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white * 2.5f);
            if (!toon)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            }

            EnsureFolder(InteractablePaths.Materials);
            AssetDatabase.CreateAsset(mat, InteractablePaths.Material);
            return mat;
        }

        static Material EnsureTrailMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(InteractablePaths.TrailMaterial);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = "CC_FrisbeeTrail" };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            EnsureFolder(InteractablePaths.Materials);
            AssetDatabase.CreateAsset(mat, InteractablePaths.TrailMaterial);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
#endif
