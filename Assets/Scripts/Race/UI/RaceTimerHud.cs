using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>
    /// The dashboard plate (hud-empty-plate-frame art) that rides just above the handlebar:
    ///   [position badge]  00:42.17
    ///                     LAP 2/3      BEST 00:31.20
    /// plus a big "LAP 2" / "FINAL LAP!" callout up in view when a lap is completed.
    /// The timer freezes on your finish time.
    /// </summary>
    public class RaceTimerHud : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text lapText;
        [SerializeField] private TMP_Text bestLapText;

        [Header("Position")]
        [SerializeField] private Image positionBadge;
        [Tooltip("Badge art for 1st, 2nd, 3rd, 4th... Positions past the end use the text instead.")]
        [SerializeField] private Sprite[] positionBadges;
        [SerializeField] private TMP_Text positionText;
        [Tooltip("Hide position in solo races (always 1st).")]
        [SerializeField] private bool hidePositionWhenSolo = true;

        [Header("Lap callout")]
        [SerializeField] private CanvasGroup calloutGroup;
        [SerializeField] private TMP_Text calloutText;
        [SerializeField] private float calloutSeconds = 1.8f;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip lapClip;
        [SerializeField] private AudioClip finalLapClip;

        [Header("Colors")]
        [SerializeField] private Color timeColor = new Color(1f, 0.78f, 0.17f);       // Bright Gold
        [SerializeField] private Color newBestColor = new Color(0.68f, 0.92f, 0f);    // Lime Green #AEEA00
        [SerializeField] private Color bestColor = new Color(0.8f, 0.8f, 0.8f);

        private readonly List<RaceStanding> standings = new List<RaceStanding>();
        private RaceProgress subscribed;
        private float calloutUntil = -1f;
        private float bestFlashUntil = -1f;
        private int lastPosition = -1;
        private float standingsTimer;

        private void Awake()
        {
            if (lapClip == null) lapClip = RaceTones.Get(0.1f, 659f, 988f);
            if (finalLapClip == null) finalLapClip = RaceTones.Get(0.1f, 659f, 880f, 988f, 1319f);
            RaceUi.Show(calloutGroup, this, false);
        }

        private void OnDisable() => Subscribe(null);

        private void Update()
        {
            var session = RaceSession.Current;
            var me = RaceProgress.Local;
            Subscribe(me);

            bool show = session != null && me != null && session.Phase != RacePhase.Waiting;
            RaceUi.Show(group, this, show);
            UpdateCallout();
            if (!show) return;

            float t = me.Finished ? me.FinishTime : session.RaceTime;
            timeText.text = RaceFormat.Time(t);
            timeText.color = me.Finished ? newBestColor : timeColor;

            lapText.text = me.Finished ? "FINISHED" : $"LAP {me.CurrentLap}/{session.Laps}";

            bool flashing = Time.time < bestFlashUntil;
            bestLapText.text = me.BestLapTime > 0f ? $"BEST {RaceFormat.Time(me.BestLapTime)}" : $"BEST {RaceFormat.NoTime}";
            bestLapText.color = flashing ? newBestColor : bestColor;

            // Standings don't change every frame; 5 Hz is plenty and avoids garbage.
            standingsTimer -= Time.deltaTime;
            if (standingsTimer <= 0f)
            {
                standingsTimer = 0.2f;
                UpdatePosition(session);
            }
        }

        private void UpdatePosition(IRaceSession session)
        {
            session.GetStandings(standings);
            int pos = 0;
            foreach (var s in standings) if (s.IsLocal) { pos = s.Position; break; }

            bool hide = pos <= 0 || (hidePositionWhenSolo && standings.Count <= 1);
            if (positionBadge) positionBadge.enabled = !hide && positionBadges != null && pos <= positionBadges.Length && positionBadges[pos - 1];
            if (positionText) positionText.enabled = !hide && (positionBadge == null || !positionBadge.enabled);
            if (hide || pos == lastPosition) return;

            lastPosition = pos;
            if (positionBadge && positionBadge.enabled) positionBadge.sprite = positionBadges[pos - 1];
            if (positionText) positionText.text = RaceFormat.Ordinal(pos);
        }

        private void Subscribe(RaceProgress me)
        {
            if (subscribed == me) return;
            if (subscribed) subscribed.LapCompleted -= OnLapCompleted;
            subscribed = me;
            if (subscribed) subscribed.LapCompleted += OnLapCompleted;
        }

        private void OnLapCompleted(int completed)
        {
            var session = RaceSession.Current;
            int laps = session?.Laps ?? 3;
            bool final = completed == laps - 1;
            if (calloutText) calloutText.text = final ? "FINAL LAP!" : $"LAP {completed + 1}";
            calloutUntil = Time.time + calloutSeconds;

            var me = RaceProgress.Local;
            if (me && me.LastLapTime > 0f && Mathf.Approximately(me.LastLapTime, me.BestLapTime) && completed > 1)
                bestFlashUntil = Time.time + 2f;

            if (audioSource) audioSource.PlayOneShot(final ? finalLapClip : lapClip);
            RaceHaptics.Pulse(0.4f, 0.12f);
        }

        private void UpdateCallout()
        {
            bool on = Time.time < calloutUntil;
            RaceUi.Show(calloutGroup, this, on);
            if (!on || !calloutGroup) return;
            float remaining = calloutUntil - Time.time;
            float age = calloutSeconds - remaining;
            calloutGroup.alpha = Mathf.Clamp01(remaining / 0.35f);
            calloutGroup.transform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(age / 0.2f));
        }
    }
}
