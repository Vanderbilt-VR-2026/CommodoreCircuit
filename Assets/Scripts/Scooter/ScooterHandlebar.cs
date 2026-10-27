using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Draws the handlebar the player steers with. It only reads state from
    /// ScooterController and never touches movement.
    ///
    /// The bars appear where the player's hands were when steering was last
    /// recentered, and the crossbar twists with the hands so the controller
    /// models always look like they are gripping it. Built by
    /// CommodoreCircuit > Add VR Handlebar to Selected Scooter.
    /// </summary>
    public class ScooterHandlebar : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;
        [Tooltip("Turns with the hands. Holds the crossbar and both grips.")]
        [SerializeField] private Transform bar;
        [SerializeField] private Transform crossbar;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [Tooltip("Column running from the bars down to the deck.")]
        [SerializeField] private Transform stem;

        [Header("Shape")]
        [SerializeField] private float stemBottomLocalY = 0.25f;
        [SerializeField] private float gripLength = 0.12f;
        [SerializeField] private float barThickness = 0.025f;
        [SerializeField] private float stemThickness = 0.035f;

        [Header("Feel")]
        [Tooltip("How quickly the drawn bars catch up with the hands.")]
        [SerializeField] private float followSpeed = 30f;
        [SerializeField] private float maxVisualAngle = 45f;
        [Tooltip("Grip color while both hands are holding the bars.")]
        [SerializeField] private Color grabbedColor = new Color(0.95f, 0.78f, 0.2f);

        private float shownAngle;
        private bool visible = true;
        private bool tinted;
        private MaterialPropertyBlock tintBlock;

        /// <summary>
        /// Builds the handlebar and a controller model on each hand at runtime.
        /// ScooterController calls this when the scene has no handlebar yet, so
        /// the editor menu command is optional. It copies a material the scooter
        /// already uses so no shader can be missing from the build.
        /// </summary>
        public static ScooterHandlebar CreateInScene(ScooterController scooter, Transform leftHand, Transform rightHand)
        {
            Material template = FindTemplateMaterial(scooter);
            if (template == null) return null;

            Material metal = MakeMaterial(template, new Color(0.62f, 0.64f, 0.68f), 0.7f, 0.65f);
            Material rubber = MakeMaterial(template, new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f);
            Material accent = MakeMaterial(template, new Color(0.95f, 0.78f, 0.2f), 0f, 0.5f);

            var root = new GameObject("Handlebar");
            root.transform.SetParent(scooter.transform, false);
            root.transform.localPosition = new Vector3(0f, 1f, 0.4f);

            Transform stem = Part("Stem", root.transform, metal, Vector3.zero, PrimitiveType.Cylinder);
            var barGO = new GameObject("Bar");
            barGO.transform.SetParent(root.transform, false);
            Transform crossbar = Part("Crossbar", barGO.transform, metal, new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);
            Transform leftGripPart = Part("LeftGrip", barGO.transform, rubber, new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);
            Transform rightGripPart = Part("RightGrip", barGO.transform, rubber, new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);

            var handlebar = root.AddComponent<ScooterHandlebar>();
            handlebar.scooter = scooter;
            handlebar.bar = barGO.transform;
            handlebar.crossbar = crossbar;
            handlebar.leftGrip = leftGripPart;
            handlebar.rightGrip = rightGripPart;
            handlebar.stem = stem;
            handlebar.visible = true;
            handlebar.SetVisible(false);

            AddControllerModel(leftHand, rubber, accent);
            AddControllerModel(rightHand, rubber, accent);
            return handlebar;
        }

        /// <summary>
        /// Puts a simple controller-shaped model on each hand anchor. Used when the
        /// scooter model supplies its own handlebar, so only the hands are needed.
        /// </summary>
        public static void AddControllerModels(ScooterController scooter, Transform leftHand, Transform rightHand)
        {
            Material template = FindTemplateMaterial(scooter);
            if (template == null) return;

            Material rubber = MakeMaterial(template, new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f);
            Material accent = MakeMaterial(template, new Color(0.95f, 0.78f, 0.2f), 0f, 0.5f);
            AddControllerModel(leftHand, rubber, accent);
            AddControllerModel(rightHand, rubber, accent);
        }

        // A solid material the scooter already uses, so no shader can be missing from the
        // build. Particle renderers are skipped because their material is additive.
        private static Material FindTemplateMaterial(ScooterController scooter)
        {
            foreach (MeshRenderer meshRenderer in scooter.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material candidate = meshRenderer.sharedMaterial;
                if (candidate != null && !candidate.IsKeywordEnabled("_EMISSION")) return candidate;
            }
            return null;
        }

        private static void AddControllerModel(Transform hand, Material body, Material accent)
        {
            if (hand.Find("ControllerModel") != null) return;

            var model = new GameObject("ControllerModel");
            model.transform.SetParent(hand, false);

            Transform handle = Part("Handle", model.transform, body, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
            handle.localPosition = new Vector3(0f, 0f, 0.03f);
            handle.localScale = new Vector3(0.04f, 0.07f, 0.04f);

            Transform ring = Part("Ring", model.transform, accent, Vector3.zero, PrimitiveType.Sphere);
            ring.localPosition = new Vector3(0f, 0.025f, 0.11f);
            ring.localScale = new Vector3(0.09f, 0.02f, 0.09f);
        }

        private static Transform Part(string name, Transform parent, Material material, Vector3 euler, PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            // Visual only. Colliders on children would get in the scooter's wall sweep.
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Material MakeMaterial(Material template, Color color, float metallic, float smoothness)
        {
            var material = new Material(template);
            material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private void Awake()
        {
            if (scooter == null) scooter = GetComponentInParent<ScooterController>();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (scooter == null) return;

            bool hasPose = scooter.HasHandlebarPose;
            SetVisible(hasPose);
            if (!hasPose) return;

            Vector3 center = scooter.HandlebarCenterLocal;
            transform.localPosition = center;
            transform.localRotation = Quaternion.identity;

            SetGrabTint(scooter.IsGrabbingHandlebar);

            float target = Mathf.Clamp(scooter.HandlebarAngle, -maxVisualAngle, maxVisualAngle);
            shownAngle = Mathf.LerpAngle(shownAngle, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            if (bar != null) bar.localRotation = Quaternion.Euler(0f, shownAngle, 0f);

            float width = scooter.HandlebarWidth;
            float halfWidth = width * 0.5f;

            // Cylinder primitives are 2 units tall, so a Y scale of half the
            // length gives that length. The crossbar and grips are rotated
            // 90 degrees so their length runs left to right.
            if (crossbar != null)
                crossbar.localScale = new Vector3(barThickness, halfWidth, barThickness);
            if (leftGrip != null)
            {
                leftGrip.localPosition = new Vector3(-halfWidth, 0f, 0f);
                leftGrip.localScale = new Vector3(barThickness * 1.6f, gripLength * 0.5f, barThickness * 1.6f);
            }
            if (rightGrip != null)
            {
                rightGrip.localPosition = new Vector3(halfWidth, 0f, 0f);
                rightGrip.localScale = new Vector3(barThickness * 1.6f, gripLength * 0.5f, barThickness * 1.6f);
            }

            if (stem != null)
            {
                float height = Mathf.Max(0.05f, center.y - stemBottomLocalY);
                stem.localPosition = new Vector3(0f, -height * 0.5f, 0f);
                stem.localScale = new Vector3(stemThickness, height * 0.5f, stemThickness);
            }
        }

        private void SetGrabTint(bool grabbed)
        {
            if (tinted == grabbed) return;
            tinted = grabbed;

            if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
            tintBlock.Clear();
            if (grabbed)
            {
                tintBlock.SetColor("_BaseColor", grabbedColor);
                tintBlock.SetColor("_Color", grabbedColor);
            }
            ApplyTint(leftGrip, grabbed);
            ApplyTint(rightGrip, grabbed);
        }

        private void ApplyTint(Transform grip, bool grabbed)
        {
            if (grip == null) return;
            Renderer r = grip.GetComponent<Renderer>();
            if (r != null) r.SetPropertyBlock(grabbed ? tintBlock : null);
        }

        private void SetVisible(bool show)
        {
            if (visible == show) return;
            visible = show;
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(show);
        }
    }
}
