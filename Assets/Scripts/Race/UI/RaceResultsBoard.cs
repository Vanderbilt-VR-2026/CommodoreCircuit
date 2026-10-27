using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CommodoreCircuit.Race.UI
{
    /// <summary>
    /// Final standings. When YOU cross the line: the FINISH banner pops (hud-finish-banner),
    /// then the board slides in with live standings: finished racers show their time, those
    /// still out show their lap, and anyone who runs out of time shows DNF. If you won,
    /// the WINNER! badge (ui-winner-badge) sits on the corner.
    /// Racers who don't finish in time see the board when the race ends.
    ///
    /// Footer: networked = "Back to lobby in 14"; solo = "Press A to race again".
    /// </summary>
    public class RaceResultsBoard : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private CanvasGroup finishBanner;
        [SerializeField] private CanvasGroup board;
        [SerializeField] private GameObject winnerBadge;

        [Header("Board")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RaceResultRow[] rows;
        [SerializeField] private Sprite[] positionBadges;
        [SerializeField] private TMP_Text footerText;

        [Header("Timing")]
        [SerializeField] private float bannerSeconds = 2.2f;
        [SerializeField] private float boardFadeSeconds = 0.35f;

        [Header("Colors")]
        [SerializeField] private Color highlight = new Color(1f, 0.78f, 0.17f, 0.22f);
        [SerializeField] private Color timeColor = new Color(1f, 0.78f, 0.17f);
        [SerializeField] private Color mutedColor = new Color(0.75f, 0.75f, 0.75f);

        [Header("Sound")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip finishClip;
        [SerializeField] private AudioClip winClip;

        private readonly List<RaceStanding> standings = new List<RaceStanding>();
        private float shownAt = -1f;
        private bool celebrated;
        private float refreshTimer;
        private InputAction restartAction;

        private void Awake()
        {
            if (finishClip == null) finishClip = RaceTones.Get(0.13f, 523f, 659f, 784f, 1047f);
            if (winClip == null) winClip = RaceTones.Get(0.11f, 784f, 988f, 1175f, 1568f, 1568f);

            restartAction = new InputAction("RaceAgain", InputActionType.Button);
            restartAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");   // A
            restartAction.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");    // X
            restartAction.AddBinding("<Keyboard>/enter");

            RaceUi.Show(finishBanner, this, false);
            RaceUi.Show(board, this, false);
            if (winnerBadge) winnerBadge.SetActive(false);
        }

        private void OnEnable() => restartAction.Enable();
        private void OnDisable() => restartAction.Disable();
        private void OnDestroy() => restartAction.Dispose();

        private void Update()
        {
            var session = RaceSession.Current;
            var me = RaceProgress.Local;
            bool iFinished = me != null && me.Finished;
            bool raceOver = session != null && session.Phase == RacePhase.Results;

            if (session == null || !(iFinished || raceOver))
            {
                shownAt = -1f;
                celebrated = false;
                RaceUi.Show(finishBanner, this, false);
                RaceUi.Show(board, this, false);
                return;
            }

            if (shownAt < 0f)
            {
                shownAt = Time.time;
                // Didn't finish in time: skip the FINISH banner, go straight to the board.
                if (!iFinished) shownAt -= bannerSeconds;
                else if (audioSource) audioSource.PlayOneShot(finishClip);
                if (iFinished) RaceHaptics.Pulse(0.8f, 0.3f);
            }

            float age = Time.time - shownAt;

            // FINISH banner: pop in, hold, fade.
            bool bannerOn = age < bannerSeconds;
            RaceUi.Show(finishBanner, this, bannerOn);
            if (bannerOn && finishBanner)
            {
                float pop = Mathf.Clamp01(age / 0.25f);
                finishBanner.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, 1f - (1f - pop) * (1f - pop));
                finishBanner.alpha = Mathf.Clamp01((bannerSeconds - age) / 0.3f);
            }

            bool boardOn = age >= bannerSeconds - 0.15f;
            RaceUi.Show(board, this, boardOn);
            if (!boardOn) return;
            board.alpha = Mathf.Clamp01((age - bannerSeconds + 0.15f) / boardFadeSeconds);

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.25f;
                Refresh(session, raceOver);
            }

            if (!session.IsNetworked && age > bannerSeconds + 1.5f && restartAction.WasPressedThisFrame())
                session.RequestRestart();
        }

        private void Refresh(IRaceSession session, bool raceOver)
        {
            session.GetStandings(standings);
            int laps = session.Laps;

            if (titleText)
                titleText.text = raceOver ? "FINAL STANDINGS" : "STANDINGS";

            bool iWon = false;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!rows[i]) continue;
                if (i < standings.Count)
                {
                    var s = standings[i];
                    rows[i].Set(s, laps, raceOver, positionBadges, highlight, timeColor, mutedColor);
                    if (s.IsLocal && s.Position == 1 && s.Finished) iWon = true;
                }
                else rows[i].Hide();
            }

            // Solo races are always "1st"; only celebrate a real win.
            bool showWinner = iWon && standings.Count > 1;
            if (winnerBadge && winnerBadge.activeSelf != showWinner)
            {
                winnerBadge.SetActive(showWinner);
                if (showWinner && !celebrated)
                {
                    celebrated = true;
                    if (audioSource) audioSource.PlayOneShot(winClip);
                }
            }

            if (footerText)
            {
                float remaining = session.ResultsTimeRemaining;
                if (!session.IsNetworked)
                    footerText.text = "Press <b>A</b> to race again";
                else if (remaining >= 0f)
                    footerText.text = $"Back to lobby in {Mathf.CeilToInt(remaining)}";
                else
                    footerText.text = "Waiting for the other racers...";
            }
        }
    }
}
