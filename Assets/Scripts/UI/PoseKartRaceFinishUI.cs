using PoseKart.Race;
using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.UI
{
    [DisallowMultipleComponent]
    public sealed class PoseKartRaceFinishUI : MonoBehaviour
    {
        private static readonly Color Navy = new Color(0.015f, 0.055f, 0.12f, 0.96f);
        private static readonly Color Overlay = new Color(0.005f, 0.018f, 0.05f, 0.76f);
        private static readonly Color Cyan = new Color(0.12f, 0.92f, 0.92f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.82f, 0.22f, 1f);
        private static readonly Color White = new Color(0.96f, 0.99f, 1f, 1f);
        private static readonly Color Silver = new Color(0.82f, 0.88f, 0.94f, 1f);
        private static readonly Color Bronze = new Color(0.84f, 0.52f, 0.28f, 1f);

        [Header("Race References")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private string vehicleSelectScene = "PoseKart_VehicleSelect";

        [Header("Optional Finish Audio")]
        [SerializeField] private AudioClip finishSoundClip;
        [SerializeField] private AudioClip victorySoundClip;
        [SerializeField] private AudioClip resultsRevealClip;
        [SerializeField] private AudioClip buttonClickClip;
        [SerializeField] private AudioMixerGroup uiMixerGroup;
        [SerializeField, Range(0f, 1f)] private float finishAudioVolume = 0.9f;

        [Header("Presentation")]
        [SerializeField, Min(0.5f)] private float finishDuration = 1.15f;
        [SerializeField, Min(0.1f)] private float resultsRevealDuration = 0.5f;
        [SerializeField, Min(0f)] private float buttonUnlockDelay = 0.25f;

        private GameObject finishRoot;
        private CanvasGroup finishGroup;
        private CanvasGroup resultsGroup;
        private RectTransform finishRect;
        private RectTransform resultsRect;
        private Text finishText;
        private Text resultHeading;
        private Text finalPositionText;
        private Text finalPositionCaption;
        private Text timeText;
        private Text lapsText;
        private Text resultHint;
        private Button playAgainButton;
        private Button selectVehicleButton;
        private AudioSource audioSource;
        private EventSystem eventSystem;
        private float presentationTime;
        private bool presentationStarted;
        private bool resultsShown;

        private void Awake()
        {
            if (raceManager == null)
            {
                raceManager = FindFirstObjectByType<RaceManager>();
            }

            BuildInterface();
        }

        private void OnEnable()
        {
            if (raceManager != null)
            {
                raceManager.PlayerFinishedEvent += HandlePlayerFinished;
            }
        }

        private void Start()
        {
            if (raceManager != null && raceManager.PlayerFinished)
            {
                HandlePlayerFinished(raceManager);
            }
        }

        private void OnDisable()
        {
            if (raceManager != null)
            {
                raceManager.PlayerFinishedEvent -= HandlePlayerFinished;
            }
        }

        private void Update()
        {
            if (!presentationStarted)
            {
                return;
            }

            presentationTime += Time.unscaledDeltaTime;
            if (!resultsShown)
            {
                AnimateFinish();
                if (presentationTime >= finishDuration)
                {
                    RevealResults();
                }

                return;
            }

            AnimateResults();
        }

        private void HandlePlayerFinished(RaceManager manager)
        {
            if (presentationStarted || manager == null || !manager.PlayerFinished)
            {
                return;
            }

            presentationStarted = true;
            resultsShown = false;
            presentationTime = 0f;
            finishRoot.SetActive(true);
            finishGroup.alpha = 1f;
            resultsGroup.alpha = 0f;
            resultsGroup.transform.localScale = Vector3.one * 0.94f;
            finishRect.localScale = Vector3.one * 0.78f;
            BuildResultValues(manager);
            SetButtonsInteractable(false);
            PlaySound(finishSoundClip);
        }

        private void BuildInterface()
        {
            finishRoot = CreateObject("RaceFinishUI", transform);
            RectTransform rootRect = finishRoot.GetComponent<RectTransform>();
            SetFullScreen(rootRect);
            finishGroup = finishRoot.AddComponent<CanvasGroup>();

            Image overlay = finishRoot.AddComponent<Image>();
            overlay.color = Overlay;
            overlay.raycastTarget = true;

            GameObject finishLayer = CreateObject("FinishAnimation", finishRoot.transform);
            finishRect = finishLayer.GetComponent<RectTransform>();
            SetCentered(finishRect, new Vector2(1100f, 260f), new Vector2(0f, 55f));
            finishGroup = finishLayer.AddComponent<CanvasGroup>();
            finishText = CreateText("FINISH!", finishLayer.transform, 116, Yellow, TextAnchor.MiddleCenter);
            finishText.rectTransform.sizeDelta = new Vector2(1100f, 220f);
            AddOutline(finishText, Navy, new Vector2(3f, -3f));

            GameObject resultsLayer = CreateObject("ResultsPanel", finishRoot.transform);
            resultsRect = resultsLayer.GetComponent<RectTransform>();
            SetCentered(resultsRect, new Vector2(760f, 670f), new Vector2(0f, 0f));
            Image panel = resultsLayer.AddComponent<Image>();
            panel.color = new Color(Navy.r, Navy.g, Navy.b, 0.94f);
            panel.raycastTarget = true;
            resultsGroup = resultsLayer.AddComponent<CanvasGroup>();

            resultHeading = CreateText("RACE COMPLETE", resultsLayer.transform, 34, White, TextAnchor.MiddleCenter);
            SetAnchored(resultHeading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(680f, 64f));
            AddOutline(resultHeading, Navy, new Vector2(2f, -2f));

            finalPositionCaption = CreateText("FINISH POSITION", resultsLayer.transform, 19, Cyan, TextAnchor.MiddleCenter);
            SetAnchored(finalPositionCaption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(680f, 36f));
            finalPositionText = CreateText("1ST", resultsLayer.transform, 112, Yellow, TextAnchor.MiddleCenter);
            SetAnchored(finalPositionText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(680f, 150f));
            AddOutline(finalPositionText, Navy, new Vector2(4f, -4f));

            timeText = CreateText("TOTAL TIME\n00:00.000", resultsLayer.transform, 24, White, TextAnchor.MiddleLeft);
            SetAnchored(timeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -335f), new Vector2(290f, 82f));
            lapsText = CreateText("LAPS\n1 / 1", resultsLayer.transform, 24, White, TextAnchor.MiddleLeft);
            SetAnchored(lapsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-80f, -335f), new Vector2(290f, 82f));
            resultHint = CreateText("OFFICIAL RESULT", resultsLayer.transform, 16, Cyan, TextAnchor.MiddleCenter);
            SetAnchored(resultHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -415f), new Vector2(680f, 32f));

            playAgainButton = CreateButton("PLAY AGAIN", resultsLayer.transform, new Vector2(-155f, -530f));
            selectVehicleButton = CreateButton("SELECT VEHICLE", resultsLayer.transform, new Vector2(155f, -530f));
            playAgainButton.onClick.AddListener(PlayAgain);
            selectVehicleButton.onClick.AddListener(SelectVehicle);
            ConfigureButtonNavigation();

            CreateAudioSource();
            finishRoot.SetActive(false);
        }

        private void BuildResultValues(RaceManager manager)
        {
            int position = manager.PlayerFinishPosition;
            finalPositionText.text = PositionLabel(position);
            finalPositionText.color = PositionColor(position);
            resultHeading.text = position == 1 ? "VICTORY!" : "RACE COMPLETE";
            timeText.text = $"TOTAL TIME\n{FormatTime(manager.PlayerFinishTimeSeconds)}";
            lapsText.text = $"LAPS\n{manager.CurrentLap} / {manager.TotalLaps}";
            resultHint.text = $"OFFICIAL RESULT  •  {position} / {manager.RacerCount}";
        }

        private void AnimateFinish()
        {
            float t = Mathf.Clamp01(presentationTime / Mathf.Max(0.01f, finishDuration));
            float punch = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(presentationTime / 0.3f));
            float scale = Mathf.Lerp(0.78f, 1f, punch);
            if (t > 0.72f)
            {
                scale = Mathf.Lerp(1f, 0.94f, (t - 0.72f) / 0.28f);
            }

            finishRect.localScale = Vector3.one * scale;
            finishGroup.alpha = t > 0.78f ? Mathf.Lerp(1f, 0f, (t - 0.78f) / 0.22f) : 1f;
        }

        private void RevealResults()
        {
            resultsShown = true;
            presentationTime = 0f;
            finishText.text = string.Empty;
            finishGroup.alpha = 0f;
            resultsGroup.alpha = 0f;
            resultsGroup.transform.localScale = Vector3.one * 0.94f;
            PlaySound(resultsRevealClip);
            if (raceManager != null && raceManager.PlayerFinishPosition == 1)
            {
                PlaySound(victorySoundClip);
            }
        }

        private void AnimateResults()
        {
            float reveal = Mathf.Clamp01(presentationTime / Mathf.Max(0.01f, resultsRevealDuration));
            float eased = Mathf.SmoothStep(0f, 1f, reveal);
            resultsGroup.alpha = eased;
            resultsRect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
            bool unlocked = presentationTime >= resultsRevealDuration + buttonUnlockDelay;
            SetButtonsInteractable(unlocked);
            if (unlocked && eventSystem == null)
            {
                eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(playAgainButton.gameObject);
                }
            }
        }

        private void PlayAgain()
        {
            PlaySound(buttonClickClip);
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void SelectVehicle()
        {
            PlaySound(buttonClickClip);
            if (!Application.CanStreamedLevelBeLoaded(vehicleSelectScene))
            {
                resultHint.text = $"SCENE NOT IN BUILD SETTINGS: {vehicleSelectScene}";
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(vehicleSelectScene);
        }

        private void CreateAudioSource()
        {
            GameObject audioObject = CreateObject("RaceFinishAudio", transform);
            audioSource = audioObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = finishAudioVolume;
            audioSource.outputAudioMixerGroup = uiMixerGroup;
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip, finishAudioVolume);
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (playAgainButton != null)
            {
                playAgainButton.interactable = interactable;
            }

            if (selectVehicleButton != null)
            {
                selectVehicleButton.interactable = interactable;
            }
        }

        private void ConfigureButtonNavigation()
        {
            if (playAgainButton == null || selectVehicleButton == null)
            {
                return;
            }

            Navigation playNavigation = playAgainButton.navigation;
            playNavigation.mode = Navigation.Mode.Automatic;
            playAgainButton.navigation = playNavigation;
            Navigation selectNavigation = selectVehicleButton.navigation;
            selectNavigation.mode = Navigation.Mode.Automatic;
            selectVehicleButton.navigation = selectNavigation;
        }

        private static GameObject CreateObject(string objectName, Transform parent)
        {
            GameObject result = new GameObject(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static Text CreateText(string value, Transform parent, int size, Color color, TextAnchor alignment)
        {
            GameObject objectRoot = CreateObject("Text", parent);
            Text text = objectRoot.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(string label, Transform parent, Vector2 position)
        {
            GameObject objectRoot = CreateObject(label.Replace(" ", string.Empty), parent);
            RectTransform rect = objectRoot.GetComponent<RectTransform>();
            SetCentered(rect, new Vector2(250f, 68f), position);
            Image image = objectRoot.AddComponent<Image>();
            image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
            Button button = objectRoot.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
            colors.highlightedColor = new Color(Yellow.r, Yellow.g, Yellow.b, 0.34f);
            colors.pressedColor = new Color(Yellow.r, Yellow.g, Yellow.b, 0.52f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.12f);
            button.colors = colors;
            Text text = CreateText(label, objectRoot.transform, 19, White, TextAnchor.MiddleCenter);
            SetFullScreen(text.rectTransform);
            AddOutline(text, Navy, new Vector2(2f, -2f));
            return button;
        }

        private static void AddOutline(Text text, Color color, Vector2 distance)
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private static void SetFullScreen(RectTransform rect)
        {
            SetAnchored(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void SetCentered(RectTransform rect, Vector2 size, Vector2 position)
        {
            SetAnchored(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static string PositionLabel(int position)
        {
            int lastTwo = position % 100;

            if (lastTwo is 11 or 12 or 13)
            {
                return $"{position}TH";
            }

            return (position % 10) switch
            {
                1 => $"{position}ST",
                2 => $"{position}ND",
                3 => $"{position}RD",
                _ => $"{position}TH"
            };
        }

        private static Color PositionColor(int position)
        {
            return position == 1 ? Yellow : position == 2 ? Silver : position == 3 ? Bronze : White;
        }

        private static string FormatTime(double totalSeconds)
        {
            totalSeconds = Math.Max(0d, totalSeconds);
            int minutes = (int)(totalSeconds / 60d);
            int seconds = (int)(totalSeconds % 60d);
            int milliseconds = (int)((totalSeconds - Math.Floor(totalSeconds)) * 1000d);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }
    }
}
