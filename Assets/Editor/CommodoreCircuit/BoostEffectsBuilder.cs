using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Add Boost Effects to Selected Scooter
    ///
    /// Builds a "BoostEffects" child at the back of the selected scooter with
    /// two ParticleSystems (drift sparks off the rear wheel, boost flame from
    /// the exhaust), a soft particle texture and an additive material (saved
    /// under Assets/Materials), and wires them into a ScooterBoostEffects
    /// component. Safe to run again: it replaces the old BoostEffects child.
    /// Move the BoostEffects object (or the two children inside it) to adjust
    /// where the effects come out.
    /// </summary>
    public static class BoostEffectsBuilder
    {
        private const string EffectsName = "BoostEffects";
        private const string TexturePath = "Assets/Materials/SoftParticle.png";
        private const string MaterialPath = "Assets/Materials/BoostParticle.mat";

        [MenuItem("CommodoreCircuit/Add Boost Effects to Selected Scooter")]
        private static void Build()
        {
            GameObject scooter = Selection.activeGameObject;
            ScooterController controller = scooter.GetComponent<ScooterController>();

            Transform existing = scooter.transform.Find(EffectsName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            Material material = GetOrCreateMaterial();

            GameObject root = new GameObject(EffectsName);
            Undo.RegisterCreatedObjectUndo(root, "Add Boost Effects");
            root.transform.SetParent(scooter.transform, false);
            root.transform.localPosition = RearAnchorLocalPosition(scooter.transform, controller);
            root.transform.localRotation = Quaternion.identity;

            ParticleSystem sparks = CreateSparks(root.transform, material);
            ParticleSystem flame = CreateFlame(root.transform, material);

            ScooterBoostEffects effects = root.AddComponent<ScooterBoostEffects>();
            SerializedObject so = new SerializedObject(effects);
            so.FindProperty("scooter").objectReferenceValue = controller;
            so.FindProperty("driftSparks").objectReferenceValue = sparks;
            so.FindProperty("boostFlame").objectReferenceValue = flame;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scooter.scene);
            Selection.activeGameObject = root;
            Debug.Log("Boost effects added to " + scooter.name + ". Save the scene, then hold Space while turning to see them.");
        }

        [MenuItem("CommodoreCircuit/Add Boost Effects to Selected Scooter", true)]
        private static bool ValidateBuild()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        private static Vector3 RearAnchorLocalPosition(Transform scooter, ScooterController controller)
        {
            Vector3 axle = new Vector3(0f, 0.15f, -0.6f);

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty rearProp = so.FindProperty("rearWheel");
            Component rearWheel = rearProp != null ? rearProp.objectReferenceValue as Component : null;
            if (rearWheel != null)
                axle = scooter.InverseTransformPoint(rearWheel.transform.position);

            // A little behind and above the rear axle.
            return axle + new Vector3(0f, 0.1f, -0.15f);
        }

        private static ParticleSystem CreateSparks(Transform parent, Material material)
        {
            // Emits backward and a bit upward, low down near the rear wheel.
            ParticleSystem ps = CreateSystem("DriftSparks", parent, new Vector3(0f, -0.1f, 0f), Quaternion.Euler(-40f, 180f, 0f));

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.startColor = new Color(1f, 0.85f, 0.5f);
            main.gravityModifier = 1.2f;
            main.maxParticles = 150;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.03f;

            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = FadeOut();

            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = material;
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.08f;
            psr.lengthScale = 2.5f;
            return ps;
        }

        private static ParticleSystem CreateFlame(Transform parent, Material material)
        {
            // Straight out the back, like an exhaust.
            ParticleSystem ps = CreateSystem("BoostFlame", parent, Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startColor = new Color(1f, 0.55f, 0.1f);
            main.maxParticles = 120;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.04f;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = FadeOut();

            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = material;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        // Shared setup: looping, world space (so the streak trails behind the
        // moving scooter), and no emission until ScooterBoostEffects turns it on.
        private static ParticleSystem CreateSystem(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            return ps;
        }

        private static ParticleSystem.MinMaxGradient FadeOut()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Material GetOrCreateMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Mobile/Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            Material material = new Material(shader);
            Texture2D texture = GetOrCreateSoftTexture();
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);

            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static Texture2D GetOrCreateSoftTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (existing != null)
                return existing;

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            }
            texture.Apply();

            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }
    }
}
