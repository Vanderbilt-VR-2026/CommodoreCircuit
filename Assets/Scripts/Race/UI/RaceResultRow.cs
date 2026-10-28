using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>One line of the results board: badge, colour dot, name, time, best lap.</summary>
    public class RaceResultRow : MonoBehaviour
    {
        public Image background;
        public Image positionBadge;
        public TMP_Text positionText;
        public Image colorDot;
        public TMP_Text nameText;
        public TMP_Text timeText;
        public TMP_Text bestLapText;

        public void Set(in RaceStanding s, int laps, bool raceOver, Sprite[] badges,
            Color highlight, Color timeColor, Color mutedColor)
        {
            gameObject.SetActive(true);

            bool hasBadge = badges != null && s.Position >= 1 && s.Position <= badges.Length && badges[s.Position - 1];
            if (positionBadge)
            {
                positionBadge.enabled = hasBadge;
                if (hasBadge) positionBadge.sprite = badges[s.Position - 1];
            }
            if (positionText)
            {
                positionText.enabled = !hasBadge;
                positionText.text = RaceFormat.Ordinal(s.Position);
            }

            if (colorDot) colorDot.color = s.Color;
            if (nameText) nameText.text = s.IsLocal && s.DisplayName != "You" ? $"{s.DisplayName} <size=75%>(you)</size>" : s.DisplayName;
            if (background) background.color = s.IsLocal ? highlight : Color.clear;

            if (timeText)
            {
                if (s.Finished) { timeText.text = RaceFormat.Time(s.FinishTime); timeText.color = timeColor; }
                else if (raceOver) { timeText.text = "DNF"; timeText.color = mutedColor; }
                else { timeText.text = $"LAP {Mathf.Clamp(s.CompletedLaps + 1, 1, laps)}/{laps}"; timeText.color = mutedColor; }
            }
            if (bestLapText) bestLapText.text = s.BestLap > 0f ? RaceFormat.Time(s.BestLap) : RaceFormat.NoTime;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
