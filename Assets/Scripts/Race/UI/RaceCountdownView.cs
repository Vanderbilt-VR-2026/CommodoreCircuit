using TMPro;
using UnityEngine;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>
    /// Pre-race countdown: "GET READY" → 3 → 2 → 1 → GO!
    /// Each number punches in big and settles, with a beep and a light controller tick;
    /// GO! is gold, higher-pitched and buzzes harder. Scooters stay locked until GO
    /// (RaceProgress does that). Reads the shared clock, so every player sees the
    /// same number at the same moment.
    /// </summary>
    public class RaceCountdownView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [Tooltip("The big 3 / 2 / 1 / GO!")]
        [SerializeField] private TMP_Text numberText;
        [Tooltip("'GET READY' and the lap count line.")]
        [SerializeField] private TMP_Text captionText;
        [SerializeField] private AudioSource audioSource;

        [Header("Look")]
        [SerializeField] private Color numberColor = Color.white;
        [SerializeField] private Color goColor = new Color(1f, 0.78f, 0.17f);   // Bright Gold #FFC72C
        [SerializeField] private string goWord = "GO!";
        [SerializeField] private float goShowSeconds = 1.1f;
        [Tooltip("Scale a number starts at before settling to 1.")]
        [SerializeField] private float punchScale = 1.7f;
        [SerializeField] private float punchSeconds = 0.28f;

        [Header("Sound (leave empty to use built-in beeps)")]
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip goClip;

        private int shown = int.MinValue;   // 3,2,1 = number, 0 = GO, -1 = get ready, -2 = waiting for racers, MinValue = hidden
        private float shownAt;

        private void Awake()
        {
            if (tickClip == null) tickClip = RaceTones.Get(0.16f, 523f);
            if (goClip == null) goClip = RaceTones.Get(0.12f, 784f, 1047f, 1568f);
            SetVisible(false);
        }

        private void Update()
        {
            var session = RaceSession.Current;
            int state = int.MinValue;
            if (session != null)
            {
                if (session.Phase == RacePhase.Waiting && session.IsNetworked)
                {
                    state = -2;
                }
                else if (session.Phase == RacePhase.Countdown)
                {
                    float t = session.TimeUntilGo;
                    state = t > session.CountdownSeconds ? -1 : Mathf.Clamp(Mathf.CeilToInt(t), 1, 9);
                }
                else if (session.Phase == RacePhase.Racing && session.RaceTime < goShowSeconds)
                {
                    state = 0;
                }
            }

            if (state != shown) Show(state, session);
            if (shown == int.MinValue) return;

            // Punch: big → normal with a little overshoot; fade the last bit of each second.
            float k = Mathf.Clamp01((Time.time - shownAt) / punchSeconds);
            float ease = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);   // ease-out-back
            float scale = Mathf.LerpUnclamped(punchScale, 1f, ease);
            if (shown >= 0) numberText.rectTransform.localScale = Vector3.one * scale;

            if (shown == 0)
                group.alpha = Mathf.Clamp01((goShowSeconds - session.RaceTime) / 0.3f);
        }

        private void Show(int state, IRaceSession session)
        {
            shown = state;
            shownAt = Time.time;
            if (state == int.MinValue) { SetVisible(false); return; }

            SetVisible(true);
            group.alpha = 1f;
            int laps = session != null ? session.Laps : 3;

            if (state == -2)
            {
                numberText.text = "";
                captionText.text = "<size=70%>WAITING FOR RACERS...</size>";
                return;
            }

            if (state == -1)
            {
                numberText.text = "";
                captionText.text = $"GET READY!\n<size=60%>{laps} LAPS - HOLD YOUR LINE</size>";
                return;
            }

            captionText.text = state > 0 ? $"<size=60%>LAP 1 OF {laps}</size>" : "";
            numberText.text = state > 0 ? state.ToString() : goWord;
            numberText.color = state > 0 ? numberColor : goColor;
            numberText.rectTransform.localScale = Vector3.one * punchScale;

            if (audioSource) audioSource.PlayOneShot(state > 0 ? tickClip : goClip);
            RaceHaptics.Pulse(state > 0 ? 0.25f : 0.7f, state > 0 ? 0.05f : 0.2f);
        }

        private void SetVisible(bool visible) => RaceUi.Show(group, this, visible);
    }
}
