using UnityEngine;

namespace CommodoreCircuit.Race.UI
{
    internal static class RaceUi
    {
        /// <summary>
        /// Show or hide a panel. Deactivates it when it is a child of the view (no draw
        /// cost while hidden); if it is the view's own object, only fades it, so the
        /// view's Update keeps running.
        /// </summary>
        public static void Show(CanvasGroup group, MonoBehaviour owner, bool visible)
        {
            if (!group) return;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            if (group.gameObject != owner.gameObject && group.gameObject.activeSelf != visible)
                group.gameObject.SetActive(visible);
        }

        /// <summary>Gentle "breathing" scale, 1 ± amount.</summary>
        public static float Pulse(float hz, float amount) => 1f + Mathf.Sin(Time.time * hz * Mathf.PI * 2f) * amount;
    }
}
