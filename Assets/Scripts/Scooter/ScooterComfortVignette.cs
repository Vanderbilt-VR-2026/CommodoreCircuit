using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Comfort vignette for VR. Darkens the edges of the rider's view while the
    /// scooter turns or changes speed, which cuts down the motion peripheral
    /// vision sees and helps against motion sickness. Uses the XR Interaction
    /// Toolkit's TunnelingVignette prefab for the look, but drives its size
    /// from the scooter instead of from a locomotion provider.
    ///
    /// Add with CommodoreCircuit > Set Up VR Comfort on Selected Scooter.
    /// Only appears with a headset, so keyboard play in the editor is unchanged.
    /// </summary>
    public class ScooterComfortVignette : MonoBehaviour
    {
        [SerializeField] private ScooterController scooter;
        [Tooltip("XR Interaction Toolkit Starter Assets > TunnelingVignette prefab.")]
        [SerializeField] private GameObject vignettePrefab;

        [Header("Strength")]
        [Tooltip("Turn off to remove the vignette.")]
        [SerializeField] private bool vignetteEnabled = true;
        [Tooltip("Size of the clear circle at full effect. 1 is no vignette, lower is a smaller window.")]
        [SerializeField, Range(0f, 1f)] private float minAperture = 0.6f;
        [Tooltip("How soft the vignette's edge is.")]
        [SerializeField, Range(0f, 1f)] private float feathering = 0.25f;
        [Tooltip("Turning speed, in degrees per second, that gives the full effect.")]
        [SerializeField] private float turnRateForFullEffect = 70f;
        [Tooltip("Speed change, in m/s per second, that gives the full effect.")]
        [SerializeField] private float accelerationForFullEffect = 10f;
        [Tooltip("Seconds for the vignette to close in.")]
        [SerializeField] private float easeInTime = 0.2f;
        [Tooltip("Seconds for the vignette to open back up.")]
        [SerializeField] private float easeOutTime = 0.5f;

        private static readonly int ApertureSizeId = Shader.PropertyToID("_ApertureSize");
        private static readonly int FeatheringEffectId = Shader.PropertyToID("_FeatheringEffect");
        private static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");
        private static readonly int VignetteColorBlendId = Shader.PropertyToID("_VignetteColorBlend");

        private MeshRenderer vignetteRenderer;
        private MaterialPropertyBlock block;
        private float strength;

        private void Awake()
        {
            if (scooter == null)
                scooter = GetComponentInParent<ScooterController>();
            block = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (vignetteRenderer == null && !TryCreateVignette()) return;

            float target = 0f;
            if (vignetteEnabled && scooter != null)
            {
                // The scooter's own turning and speed change, not the player's
                // head, so looking around never triggers the vignette.
                target = Mathf.Max(
                    Mathf.Clamp01(scooter.ViewTurnRate / Mathf.Max(1f, turnRateForFullEffect)),
                    Mathf.Clamp01(Mathf.Abs(scooter.SpeedChangeRate) / Mathf.Max(0.1f, accelerationForFullEffect)));
            }

            float easeTime = target > strength ? easeInTime : easeOutTime;
            strength = Mathf.MoveTowards(strength, target, Time.deltaTime / Mathf.Max(0.01f, easeTime));

            vignetteRenderer.enabled = strength > 0.001f;
            vignetteRenderer.GetPropertyBlock(block);
            block.SetFloat(ApertureSizeId, Mathf.Lerp(1f, minAperture, strength));
            block.SetFloat(FeatheringEffectId, feathering);
            block.SetColor(VignetteColorId, Color.black);
            block.SetColor(VignetteColorBlendId, Color.black);
            vignetteRenderer.SetPropertyBlock(block);
        }

        // Puts the vignette on the headset camera once the headset is running.
        private bool TryCreateVignette()
        {
            if (vignettePrefab == null || !UnityEngine.XR.XRSettings.isDeviceActive) return false;
            Camera head = Camera.main;
            if (head == null) return false;

            GameObject vignette = Instantiate(vignettePrefab, head.transform, false);
            vignette.name = "ComfortVignette";

            // The prefab's own controller would fight over the material values.
            foreach (MonoBehaviour behaviour in vignette.GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().Name != "TunnelingVignetteController") continue;
                behaviour.enabled = false;
                Destroy(behaviour);
            }

            vignetteRenderer = vignette.GetComponent<MeshRenderer>();
            if (vignetteRenderer == null)
            {
                Destroy(vignette);
                return false;
            }

            // The hemisphere must sit past the near clip plane or it is cut away.
            MeshFilter filter = vignette.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                float radius = filter.sharedMesh.bounds.extents.magnitude;
                float needed = head.nearClipPlane * 2f;
                if (radius > 0.0001f && radius < needed)
                    vignette.transform.localScale = Vector3.one * (needed / radius);
            }

            vignetteRenderer.enabled = false;
            return true;
        }
    }
}
