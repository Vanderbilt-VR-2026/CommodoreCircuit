using CommodoreCircuit.Scooter;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// What you see of another racer: a copy of the scooter model plus a simple rider
    /// (body and head) in that player's color, which matches their scoreboard row.
    /// <see cref="NetworkRacer"/> adds this to other players' racers only, at runtime.
    ///
    /// The scooter is copied from this client's own scooter, so it always matches the
    /// Lemon model in the scene with no prefab to keep in sync. The copy has no colliders
    /// and no scripts: it can't block the ground raycast, walls, power-ups or hazards.
    /// To use a Mixamo rider later, replace BuildRider.
    /// </summary>
    public class RemoteRacerVisual : MonoBehaviour
    {
        private const float DeckHeight = 0.25f;
        private const float NeckDrop = 0.22f;

        private Transform head;
        private Color color;
        private Transform body;
        private bool built;
        private float nextTry;

        public void Init(Transform headTarget, Color playerColor)
        {
            head = headTarget;
            color = playerColor;
        }

        private void LateUpdate()
        {
            if (!built)
            {
                // Our scooter may not exist for a moment while the scene finishes loading.
                if (Time.unscaledTime < nextTry) return;
                nextTry = Time.unscaledTime + 0.5f;
                built = TryBuild();
                if (!built) return;
            }
            PoseBody();
        }

        private bool TryBuild()
        {
            ScooterController source = RaceProgress.Local ? RaceProgress.Local.Scooter : null;
            if (!source) source = FindAnyObjectByType<ScooterController>();
            if (!source) return false;

            Material sample = CopyScooterModel(source.transform);
            BuildRider(sample);
            return true;
        }

        // A flat copy of every visible mesh on our scooter, posed the same relative to the
        // scooter root. The rider's rig, camera and controller models are left out.
        private Material CopyScooterModel(Transform scooter)
        {
            var model = new GameObject("Scooter Model").transform;
            model.SetParent(transform, false);
            Matrix4x4 toScooter = scooter.worldToLocalMatrix;
            Material sample = null;

            foreach (var source in scooter.GetComponentsInChildren<MeshRenderer>())
            {
                if (!source.enabled || IsRiderPart(source.transform, scooter)) continue;
                var filter = source.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;

                Matrix4x4 m = toScooter * source.transform.localToWorldMatrix;
                var part = new GameObject(source.name).transform;
                part.SetParent(model, false);
                part.localPosition = m.GetColumn(3);
                part.localRotation = m.rotation;
                part.localScale = m.lossyScale;

                part.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = part.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = source.sharedMaterials;
                copy.shadowCastingMode = source.shadowCastingMode;
                if (!sample) sample = source.sharedMaterial;
            }
            return sample;
        }

        // The camera, the VR rig with the hands and controller models, and any HUD canvas.
        private static bool IsRiderPart(Transform t, Transform scooter)
        {
            for (; t && t != scooter; t = t.parent)
            {
                if (t.GetComponent<Camera>() || t.GetComponent<Canvas>() || t.GetComponent<ScooterHandlebar>()) return true;
                if (t.name == "VRRig" || t.name.EndsWith("HandAnchor")) return true;
            }
            return false;
        }

        private void BuildRider(Material sample)
        {
            Material skin = Tinted(sample, color);
            Material visor = Tinted(sample, new Color(0.08f, 0.08f, 0.1f));

            body = Primitive(PrimitiveType.Capsule, "Rider Body", transform, skin);

            Transform headParent = head ? head : transform;
            var headBall = Primitive(PrimitiveType.Sphere, "Rider Head", headParent, skin);
            headBall.localScale = Vector3.one * 0.24f;
            if (!head) headBall.localPosition = Vector3.up * 1.7f;

            // A dark strip on the face, so you can tell which way they're looking.
            var face = Primitive(PrimitiveType.Cube, "Rider Visor", headBall, visor);
            face.localPosition = new Vector3(0f, 0.08f, 0.42f);
            face.localScale = new Vector3(0.75f, 0.28f, 0.2f);
        }

        // Stretch the body from the deck up to just under the head, leaning as the rider leans.
        private void PoseBody()
        {
            if (!body) return;
            Vector3 bottom = new Vector3(0f, DeckHeight, -0.1f);
            Vector3 top = head ? transform.InverseTransformPoint(head.position) - Vector3.up * NeckDrop
                               : new Vector3(0f, 1.7f - NeckDrop, 0f);
            Vector3 span = top - bottom;
            // Before the first head update arrives the head sits at the root; keep a sane shape.
            if (span.y < 0.4f) span = new Vector3(0f, 1.2f, 0f);

            body.localPosition = bottom + span * 0.5f;
            body.localRotation = Quaternion.FromToRotation(Vector3.up, span);
            body.localScale = new Vector3(0.34f, span.magnitude * 0.5f, 0.26f);
        }

        private static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            // Remove the collider now, not at the end of the frame: our scooter's ground
            // raycast would otherwise hit it for one physics step.
            DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (material) renderer.sharedMaterial = material;
            return go.transform;
        }

        // Copy a scooter material, so the rider uses a shader that's already in the build.
        private static Material Tinted(Material sample, Color c)
        {
            if (!sample) return null;
            var m = new Material(sample) { name = "Rider " + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            return m;
        }

        private void OnDestroy()
        {
            // The tinted materials are ours; the copied scooter materials are shared assets.
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial && r.sharedMaterial.name.StartsWith("Rider ")) Destroy(r.sharedMaterial);
        }
    }
}
