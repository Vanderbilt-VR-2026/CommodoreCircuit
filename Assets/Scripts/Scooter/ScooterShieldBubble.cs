using CommodoreCircuit.Interactables;
using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// The force field from a Commodore Card (MealSwipe): a glowing bubble around the
    /// scooter's lower half while the shield is up.
    ///
    ///   - Pops in when the card is collected, then pulses gently.
    ///   - Blinks for the last couple of seconds, so you know it's about to run out.
    ///   - When it blocks a hazard it flashes white and bursts outward, then it's gone.
    ///
    /// It wraps the whole scooter but stops below the rider's head, so it never fills the view.
    /// Built by CommodoreCircuit > Add Shield Bubble to Selected Scooter.
    /// </summary>
    public class ScooterShieldBubble : MonoBehaviour
    {
        [SerializeField] private Renderer bubble;
        [SerializeField] private Color color = new Color(0.35f, 0.85f, 1f, 1f);

        [Header("Timing")]
        [Tooltip("Seconds to pop in when the shield starts.")]
        [SerializeField] private float popIn = 0.3f;
        [Tooltip("Seconds to shrink away when the shield runs out.")]
        [SerializeField] private float popOut = 0.2f;
        [Tooltip("Seconds of white flash and burst when the shield blocks a hit.")]
        [SerializeField] private float blockBurst = 0.4f;
        [Tooltip("Start blinking this many seconds before the shield runs out.")]
        [SerializeField] private float warnSeconds = 2f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private RacerEffects effects;
        private MaterialPropertyBlock block;
        private Vector3 fullScale;
        private float shownSince = -1f;
        private float blockedAt = -1f;
        private float scale;

        private void Awake()
        {
            if (bubble == null) bubble = GetComponent<Renderer>();
            fullScale = bubble != null ? bubble.transform.localScale : Vector3.one;
            block = new MaterialPropertyBlock();
            if (bubble != null) bubble.enabled = false;
        }

        // RacerEffects is added by ScooterController's Awake, so look for it once everything is awake.
        private void Start()
        {
            effects = GetComponentInParent<RacerEffects>();
            if (effects == null) return;
            effects.onShieldStart.AddListener(OnShieldStart);
            effects.onShieldBlocked.AddListener(OnShieldBlocked);
        }

        private void OnDestroy()
        {
            if (effects == null) return;
            effects.onShieldStart.RemoveListener(OnShieldStart);
            effects.onShieldBlocked.RemoveListener(OnShieldBlocked);
        }

        private void OnShieldStart()
        {
            shownSince = Time.time;
            blockedAt = -1f;
        }

        private void OnShieldBlocked() => blockedAt = Time.time;

        private void LateUpdate()
        {
            if (bubble == null || effects == null) return;
            float now = Time.time;
            float intensity;
            Color c = color;

            if (blockedAt >= 0f && now - blockedAt < blockBurst)
            {
                // Burst: grows outward while fading from white back to nothing.
                float t = (now - blockedAt) / blockBurst;
                scale = 1f + 0.4f * t;
                intensity = 3f * (1f - t);
                c = Color.Lerp(Color.white, color, t);
            }
            else if (effects.IsShielded)
            {
                // Pop in with a little overshoot, then a slow breathing pulse.
                float t = shownSince >= 0f ? Mathf.Clamp01((now - shownSince) / popIn) : 1f;
                scale = EaseOutBack(t);
                intensity = 1f + 0.2f * Mathf.Sin(now * 5f);
                if (effects.ShieldTimeLeft < warnSeconds && Mathf.Sin(now * 22f) < 0f) intensity *= 0.25f;
            }
            else
            {
                // After a block the burst was the goodbye; otherwise shrink away.
                if (blockedAt >= 0f) scale = 0f;
                scale = Mathf.MoveTowards(scale, 0f, Time.deltaTime / Mathf.Max(0.01f, popOut));
                intensity = scale;
            }

            bool visible = scale > 0.01f && intensity > 0.01f;
            bubble.enabled = visible;
            if (!visible) return;

            bubble.transform.localScale = fullScale * scale;
            bubble.GetPropertyBlock(block);
            block.SetColor(ColorId, c);
            block.SetFloat(IntensityId, intensity);
            bubble.SetPropertyBlock(block);
        }

        private static float EaseOutBack(float t)
        {
            const float s = 1.7f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }
}
