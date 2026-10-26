using PoseKart.Race;
using PoseKart.Vehicle;
using TMPro;
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

        [Header("Race References")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private Text positionText;
        [SerializeField] private Text lapText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text raceStatusText;

        [Header("Nitro Meter (Independent of Legacy HUD)")]
        [Tooltip("Optional. If empty, uses RaceManager.PlayerController automatically.")]
        [SerializeField] private CarController nitroPlayerCar;
        [Tooltip("Drag NitroMeterUI/Bar/Fill here. Keep its Source Image sprite.")]
        [SerializeField] private Image nitroFillImage;
        [Tooltip("Assign NitroMeterUI/Percentage here if it uses UI Legacy Text.")]
        [SerializeField] private Text nitroPercentageText;
        [Tooltip("Alternatively assign Percentage here if it uses TextMeshPro UI.")]
        [SerializeField] private TMP_Text nitroPercentageTMP;
        [Tooltip("How quickly earned Nitro is animated. Spending always follows the real fuel amount.")]
        [SerializeField, Min(0.01f)] private float nitroFillSpeed = 0.65f;
        [Tooltip("Optional. If empty, NitroMeterUI/Label is used (Legacy Text).")]
        [SerializeField] private Text nitroModeText;
        [Tooltip("Optional TextMeshPro alternative for the mode label.")]
        [SerializeField] private TMP_Text nitroModeTMP;
        [Tooltip("Creates the Perfect Timing zone and moving cursor over the existing Bar at runtime.")]
        [SerializeField] private bool autoCreateNitroTimingUI = true;

        private float lastTimeRefresh = -1f;
        private int lastPosition = int.MinValue;
        private int lastRacerCount = int.MinValue;
        private int lastLap = int.MinValue;
        private RaceState lastState = (RaceState)(-1);
        private bool presentationReady;

        private Image preparedNitroFillImage;
        private float displayedNitroFill;
        private bool nitroDisplayInitialized;
        private int lastNitroPercentage = int.MinValue;
        private CarController.NitroMode lastNitroMode = (CarController.NitroMode)(-1);
        private RectTransform nitroTimingOverlay;
        private RectTransform nitroPerfectZone;
        private RectTransform nitroTimingCursor;

        // Bỏ kiểm tra countdownText khỏi IsConfigured
        public bool IsConfigured =>
            raceManager != null
            && positionText != null
            && lapText != null
            && timeText != null
            && raceStatusText != null;

        public void Configure(
            RaceManager manager,
            Text position,
            Text lap,
            Text time,
            Text raceStatus)
        {
            raceManager = manager;
            positionText = position;
            lapText = lap;
            timeText = time;
            raceStatusText = raceStatus;

            SetupPresentation();
            SetupNitroPresentation();
            RefreshNitroMeter();
            Refresh();
        }

        private void Awake()
        {
            if (raceManager == null)
                raceManager = FindFirstObjectByType<RaceManager>();
            SetupPresentation();
            SetupNitroPresentation();
        }

        private void Update()
        {
            SetupPresentation();
            SetupNitroPresentation();

            RefreshNitroMeter();
            Refresh();
        }

        private void TryAutoBindNitroUI()
        {
            if (HasDedicatedNitroMeter())
            {
                nitroFillImage = null;
                nitroPercentageText = null;
                nitroPercentageTMP = null;
                nitroModeText = null;
                nitroModeTMP = null;
                return;
            }

            Transform meter = transform.Find("NitroMeterUI");
            if (meter == null)
                return;

            if (nitroFillImage == null)
            {
                Transform fill = meter.Find("Bar/Fill");
                if (fill != null)
                    nitroFillImage = fill.GetComponent<Image>();
            }

            if (nitroPercentageText == null && nitroPercentageTMP == null)
            {
                Transform percentage = meter.Find("Percentage");
                if (percentage != null)
                {
                    nitroPercentageTMP = percentage.GetComponent<TMP_Text>();
                    if (nitroPercentageTMP == null)
                        nitroPercentageText = percentage.GetComponent<Text>();
                    lastNitroPercentage = int.MinValue;
                }
            }

            if (nitroModeText == null && nitroModeTMP == null)
            {
                Transform label = meter.Find("Label");
                if (label != null)
                {
                    nitroModeTMP = label.GetComponent<TMP_Text>();
                    if (nitroModeTMP == null)
                        nitroModeText = label.GetComponent<Text>();
                    lastNitroMode = (CarController.NitroMode)(-1);
                }
            }
        }

        private void SetupNitroPresentation()
        {
            TryAutoBindNitroUI();
            if (nitroFillImage == null)
                return;

            if (preparedNitroFillImage != nitroFillImage)
            {
                nitroFillImage.type = Image.Type.Filled;
                nitroFillImage.fillMethod = Image.FillMethod.Horizontal;
                nitroFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                nitroFillImage.fillAmount = 0f;
                preparedNitroFillImage = nitroFillImage;
                nitroDisplayInitialized = false;
                lastNitroPercentage = int.MinValue;
                SetNitroPercentage(0f);
            }

            if (autoCreateNitroTimingUI && nitroTimingOverlay == null)
                SetupTimingOverlay();
        }

        private void SetupTimingOverlay()
        {
            Transform bar = nitroFillImage.transform.parent;
            if (bar == null)
                return;

            nitroTimingOverlay = GetOrCreateUIRect(bar, "NitroTimingOverlay");
            StretchToParent(nitroTimingOverlay);
            nitroTimingOverlay.SetAsLastSibling();

            nitroPerfectZone = GetOrCreateUIRect(nitroTimingOverlay, "PerfectZone");
            EnsureTimingImage(nitroPerfectZone, new Color(0.12f, 0.95f, 1f, 0.75f));

            nitroTimingCursor = GetOrCreateUIRect(nitroTimingOverlay, "TimingCursor");
            EnsureTimingImage(nitroTimingCursor, Color.white);
            nitroTimingCursor.SetAsLastSibling();
            nitroTimingOverlay.gameObject.SetActive(false);
        }

        private static RectTransform GetOrCreateUIRect(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            if (existing != null)
                return existing as RectTransform;

            GameObject created = new GameObject(objectName,
                typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureTimingImage(RectTransform rect, Color defaultColor)
        {
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
                image.color = defaultColor;
            }
            image.raycastTarget = false;
        }

        private void RefreshNitroMeter()
        {
            if (HasDedicatedNitroMeter())
                return;

            if (!PoseKartLocalPlayerResolver.IsLocalHuman(nitroPlayerCar))
                nitroPlayerCar = PoseKartLocalPlayerResolver.FindLocalCar(raceManager);

            if (nitroPlayerCar == null)
            {
                if (nitroTimingOverlay != null)
                    nitroTimingOverlay.gameObject.SetActive(false);
                return;
            }

            float targetFill = Mathf.Clamp01(nitroPlayerCar.NitroFill01);
            if (!nitroDisplayInitialized)
            {
                displayedNitroFill = targetFill;
                nitroDisplayInitialized = true;
            }
            else if (nitroPlayerCar.IsNitroBoosting || targetFill < displayedNitroFill)
            {
                displayedNitroFill = targetFill;
            }
            else
            {
                displayedNitroFill = Mathf.MoveTowards(displayedNitroFill, targetFill,
                    nitroFillSpeed * Time.unscaledDeltaTime);
            }

            if (nitroFillImage != null)
            {
                nitroFillImage.fillAmount = displayedNitroFill;
                if (nitroPlayerCar.IsNitroBoosting)
                {
                    switch (nitroPlayerCar.CurrentNitroMode)
                    {
                        case CarController.NitroMode.Single:
                            nitroFillImage.color = new Color(1f, 0.85f, 0.15f, 1f);
                            break;
                        case CarController.NitroMode.Double:
                            nitroFillImage.color = new Color(1f, 0.45f, 0.05f, 1f);
                            break;
                        case CarController.NitroMode.Perfect:
                            nitroFillImage.color = new Color(0.12f, 0.88f, 1f, 1f);
                            break;
                        case CarController.NitroMode.Shockwave:
                            nitroFillImage.color = new Color(0.82f, 0.22f, 1f, 1f);
                            break;
                        default:
                            nitroFillImage.color = new Color(1f, 0.78f, 0.08f, 1f);
                            break;
                    }
                }
                else
                {
                    nitroFillImage.color = new Color(1f, 0.78f, 0.08f, 1f);
                }
            }
            SetNitroPercentage(displayedNitroFill);
            RefreshNitroMode();
            RefreshNitroTiming();
        }

        private bool HasDedicatedNitroMeter()
        {
            // The dedicated meter is a child of RaceHUD in the online scenes.
            // Include inactive children so RaceHUD never creates/binds a second
            // legacy bar during scene load or domain reload.
            return GetComponentInChildren<PoseKartNitroMeterUI>(true) != null;
        }

        private void RefreshNitroMode()
        {
            CarController.NitroMode mode = nitroPlayerCar.CurrentNitroMode;
            if (mode == lastNitroMode)
                return;

            lastNitroMode = mode;
            string label;
            Color modeColor;
            switch (mode)
            {
                case CarController.NitroMode.Single:
                    label = "SINGLE NITRO";
                    modeColor = new Color(1f, 0.85f, 0.15f, 1f);
                    break;
                case CarController.NitroMode.Double:
                    label = "DOUBLE NITRO";
                    modeColor = new Color(1f, 0.45f, 0.05f, 1f);
                    break;
                case CarController.NitroMode.Shockwave:
                    label = "SHOCKWAVE";
                    modeColor = new Color(0.82f, 0.22f, 1f, 1f);
                    break;
                case CarController.NitroMode.Perfect:
                    label = "PERFECT NITRO";
                    modeColor = new Color(0.12f, 0.88f, 1f, 1f);
                    break;
                default:
                    label = "NITRO";
                    modeColor = Color.white;
                    break;
            }

            if (nitroModeText != null)
            {
                nitroModeText.text = label;
                nitroModeText.color = modeColor;
            }
            if (nitroModeTMP != null)
            {
                nitroModeTMP.text = label;
                nitroModeTMP.color = modeColor;
            }
        }

        private void RefreshNitroTiming()
        {
            if (nitroTimingOverlay == null || nitroPerfectZone == null || nitroTimingCursor == null)
                return;

            bool active = nitroPlayerCar.IsNitroTimingActive;
            if (nitroTimingOverlay.gameObject.activeSelf != active)
                nitroTimingOverlay.gameObject.SetActive(active);
            if (!active)
                return;

            float start = nitroPlayerCar.NitroPerfectStart01;
            float end = nitroPlayerCar.NitroPerfectEnd01;
            nitroPerfectZone.anchorMin = new Vector2(start, 0f);
            nitroPerfectZone.anchorMax = new Vector2(end, 1f);
            nitroPerfectZone.offsetMin = Vector2.zero;
            nitroPerfectZone.offsetMax = Vector2.zero;

            float progress = nitroPlayerCar.NitroTimingProgress01;
            nitroTimingCursor.anchorMin = new Vector2(progress, 0f);
            nitroTimingCursor.anchorMax = new Vector2(progress, 1f);
            nitroTimingCursor.anchoredPosition = Vector2.zero;
            nitroTimingCursor.sizeDelta = new Vector2(4f, 0f);
        }

        private void SetNitroPercentage(float fill01)
        {
            int percentage = Mathf.Clamp(Mathf.RoundToInt(fill01 * 100f), 0, 100);
            if (percentage == lastNitroPercentage)
                return;

            lastNitroPercentage = percentage;
            string display = $"{percentage:000}%";

            if (nitroPercentageText != null)
                nitroPercentageText.text = display;

            if (nitroPercentageTMP != null)
                nitroPercentageTMP.text = display;
        }

        private void SetupPresentation()
        {
            if (presentationReady)
                return;

            StyleText(positionText, Yellow, 30, TextAnchor.UpperLeft);
            StyleText(lapText, Cyan, 30, TextAnchor.UpperLeft);
            StyleText(timeText, White, 30, TextAnchor.UpperCenter);
            StyleText(raceStatusText, Cyan, 24, TextAnchor.UpperRight);

            presentationReady = true;
        }

        private static void StyleText(Text text, Color color, int fontSize, TextAnchor alignment)
        {
            if (text == null)
                return;

            text.color = color;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;

            Outline outline = text.GetComponent<Outline>();
            if (outline == null)
                outline = text.gameObject.AddComponent<Outline>();

            outline.effectColor = Navy;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        private void Refresh()
        {
            if (!IsConfigured)
                return;

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

            if (lastState != raceManager.State)
            {
                raceStatusText.text = $"RACE STATUS\n{raceManager.State.ToString().ToUpperInvariant()}";
                lastState = raceManager.State;
            }
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
