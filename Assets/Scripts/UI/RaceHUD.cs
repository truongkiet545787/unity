using PoseKart.Race;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    [DisallowMultipleComponent]
    public sealed class RaceHUD : MonoBehaviour
    {
        private static readonly Color Navy = new Color(0.015f, 0.055f, 0.12f, 0.96f);
        private static readonly Color Cyan = new Color(0.12f, 0.92f, 0.92f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.82f, 0.22f, 1f);
        private static readonly Color White = new Color(0.96f, 0.99f, 1f, 1f);

        [SerializeField] private RaceManager raceManager;
        [SerializeField] private Text positionText;
        [SerializeField] private Text lapText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text countdownText;
        [SerializeField] private Text raceStatusText;

        [Header("Countdown Audio")]
        [Tooltip("Assign a short, non-looping beep. It is played once when RaceManager changes to 3, 2 or 1.")]
        [SerializeField] private AudioClip countdownBeepClip;
        [Tooltip("Assign a distinct short, non-looping GO sound. It is played once when RaceManager changes to GO!.")]
        [SerializeField] private AudioClip countdownGoClip;
        [SerializeField, Range(0f, 1f)] private float countdownAudioVolume = 0.85f;

        [Header("Countdown Presentation")]
        [SerializeField, Min(0.05f)] private float countdownPunchDuration = 0.28f;
        [SerializeField, Min(1f)] private float countdownPunchScale = 1.22f;

        private AudioSource countdownAudioSource;
        private CanvasGroup countdownCanvasGroup;
        private RectTransform countdownRect;
        private string lastCountdownDisplay;
        private float countdownAnimationTime;
        private float lastTimeRefresh = -1f;
        private int lastPosition = int.MinValue;
        private int lastRacerCount = int.MinValue;
        private int lastLap = int.MinValue;
        private RaceState lastState = (RaceState)(-1);
        private bool presentationReady;

        public bool IsConfigured =>
            raceManager != null
            && positionText != null
            && lapText != null
            && timeText != null
            && countdownText != null
            && raceStatusText != null;

        public void Configure(
            RaceManager manager,
            Text position,
            Text lap,
            Text time,
            Text countdown,
            Text raceStatus)
        {
            raceManager = manager;
            positionText = position;
            lapText = lap;
            timeText = time;
            countdownText = countdown;
            raceStatusText = raceStatus;
            SetupPresentation();
            Refresh();
        }

        private void Awake()
        {
            SetupPresentation();
        }

        private void Update()
        {
            SetupPresentation();
            Refresh();
            AnimateCountdown(Time.unscaledDeltaTime);
        }

        private void SetupPresentation()
        {
            if (presentationReady)
            {
                return;
            }

            if (countdownAudioSource == null)
            {
                countdownAudioSource = GetComponent<AudioSource>();
                if (countdownAudioSource == null)
                {
                    countdownAudioSource = gameObject.AddComponent<AudioSource>();
                }

                countdownAudioSource.playOnAwake = false;
                countdownAudioSource.loop = false;
                countdownAudioSource.spatialBlend = 0f;
                countdownAudioSource.volume = countdownAudioVolume;
            }
            else
            {
                countdownAudioSource.volume = countdownAudioVolume;
            }

            if (countdownText != null)
            {
                countdownRect = countdownText.rectTransform;
                countdownCanvasGroup = countdownText.GetComponent<CanvasGroup>();
                if (countdownCanvasGroup == null)
                {
                    countdownCanvasGroup = countdownText.gameObject.AddComponent<CanvasGroup>();
                }

                StyleText(positionText, Yellow, 30, TextAnchor.UpperLeft);
                StyleText(lapText, Cyan, 30, TextAnchor.UpperLeft);
                StyleText(timeText, White, 30, TextAnchor.UpperCenter);
                StyleText(raceStatusText, Cyan, 24, TextAnchor.UpperRight);
                StyleText(countdownText, Yellow, 96, TextAnchor.MiddleCenter);

                if (lastCountdownDisplay == null)
                {
                    lastCountdownDisplay = countdownText.text;
                    countdownCanvasGroup.alpha = string.IsNullOrEmpty(lastCountdownDisplay) ? 0f : 1f;
                }

                presentationReady = true;
            }
        }

        private static void StyleText(Text text, Color color, int fontSize, TextAnchor alignment)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;

            Outline outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Navy;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        private void Refresh()
        {
            if (!IsConfigured)
            {
                return;
            }

            int position = raceManager.PlayerPosition;
            int racerCount = raceManager.RacerCount;
            if (position != lastPosition || racerCount != lastRacerCount)
            {
                positionText.text = $"POSITION\n{position}/{racerCount}";
                lastPosition = position;
                lastRacerCount = racerCount;
            }

            int lap = raceManager.CurrentLap;
            if (lap != lastLap)
            {
                lapText.text = $"LAP\n{lap}/{raceManager.TotalLaps}";
                lastLap = lap;
            }

            if (Time.unscaledTime >= lastTimeRefresh + 0.05f || lastState != raceManager.State)
            {
                timeText.text = $"TIME\n{FormatTime(raceManager.RaceTimeSeconds)}";
                lastTimeRefresh = Time.unscaledTime;
            }

            string countdown = raceManager.CountdownDisplay;
            if (countdown != lastCountdownDisplay)
            {
                HandleCountdownChanged(countdown);
            }

            if (lastState != raceManager.State)
            {
                raceStatusText.text = $"RACE STATUS\n{raceManager.State.ToString().ToUpperInvariant()}";
                lastState = raceManager.State;
            }
        }

        private void HandleCountdownChanged(string display)
        {
            lastCountdownDisplay = display;
            countdownAnimationTime = 0f;

            if (countdownText != null)
            {
                countdownText.text = display;
            }

            if (countdownCanvasGroup != null)
            {
                countdownCanvasGroup.alpha = string.IsNullOrEmpty(display) ? 0f : 1f;
            }

            if (string.IsNullOrEmpty(display) || countdownAudioSource == null)
            {
                return;
            }

            AudioClip clip = display == "GO!" ? countdownGoClip : countdownBeepClip;
            if (clip != null)
            {
                countdownAudioSource.PlayOneShot(clip, countdownAudioVolume);
            }
        }

        private void AnimateCountdown(float deltaTime)
        {
            if (countdownRect == null || countdownCanvasGroup == null || string.IsNullOrEmpty(lastCountdownDisplay))
            {
                return;
            }

            countdownAnimationTime += deltaTime;
            float normalized = Mathf.Clamp01(countdownAnimationTime / countdownPunchDuration);
            float punch = 1f + (countdownPunchScale - 1f) * (1f - Mathf.SmoothStep(0f, 1f, normalized));
            countdownRect.localScale = new Vector3(punch, punch, 1f);
            countdownCanvasGroup.alpha = Mathf.Lerp(1f, 0.92f, normalized);
        }

        private static string FormatTime(double totalSeconds)
        {
            totalSeconds = System.Math.Max(0d, totalSeconds);
            int minutes = (int)(totalSeconds / 60d);
            int seconds = (int)(totalSeconds % 60d);
            int milliseconds = (int)((totalSeconds - System.Math.Floor(totalSeconds)) * 1000d);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }
    }
}
