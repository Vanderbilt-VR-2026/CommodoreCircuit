using UnityEngine;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>
    /// Shows the WRONG WAY sign (hud-wrong-way-sign art) when RaceProgress says the
    /// rider has been facing or driving against the race line for about a second.
    /// The sign pops in, gently pulses and wobbles, and an alarm chirp + soft controller
    /// buzz repeat while it's up. No red screen flashes: those are harsh in a headset.
    /// </summary>
    public class WrongWayWarning : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [Tooltip("The sign image. Pulses and wobbles.")]
        [SerializeField] private RectTransform sign;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip alarmClip;

        [SerializeField] private float fadeSeconds = 0.15f;
        [SerializeField] private float repeatSeconds = 1.2f;
        [SerializeField] private float pulseHz = 2.5f;
        [SerializeField] private float pulseAmount = 0.05f;
        [SerializeField] private float wobbleDegrees = 3f;
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.3f;

        private float alpha;
        private float nextAlarm;
        private bool wasWrong;

        private void Awake()
        {
            if (alarmClip == null) alarmClip = RaceTones.Get(0.14f, 880f, 587f);
            RaceUi.Show(group, this, false);
        }

        private void Update()
        {
            var me = RaceProgress.Local;
            bool wrong = me != null && me.IsWrongWay;

            if (wrong && !wasWrong) nextAlarm = Time.time;   // chirp immediately
            wasWrong = wrong;

            alpha = Mathf.MoveTowards(alpha, wrong ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            RaceUi.Show(group, this, alpha > 0f);
            if (alpha <= 0f) return;
            group.alpha = alpha;

            if (sign)
            {
                float pop = Mathf.Lerp(0.6f, 1f, alpha);
                sign.localScale = Vector3.one * pop * RaceUi.Pulse(pulseHz, pulseAmount);
                sign.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * pulseHz * Mathf.PI) * wobbleDegrees);
            }

            if (wrong && Time.time >= nextAlarm)
            {
                nextAlarm = Time.time + repeatSeconds;
                if (audioSource && alarmClip) audioSource.PlayOneShot(alarmClip);
                RaceHaptics.Pulse(hapticAmplitude, 0.12f);
            }
        }
    }
}
