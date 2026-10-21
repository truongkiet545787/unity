using PoseKart.Race;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.UI
{
    public enum FinishEditorPreviewMode
    {
        ResultsPanel,
        FinishBanner,
        Both,
        Hidden
    }

    [ExecuteAlways]
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

        [Header("Editor Preview & Alignment (Bật để hiện lên Scene căn chỉnh)")]
        [Tooltip("Bật để hiển thị màn kết thúc trực tiếp trên Scene View trong Editor để bạn dễ nhìn và căn chỉnh layout.")]
        [SerializeField] private bool showInEditMode = true;
        [Tooltip("Chọn xem bạn muốn hiển thị Bảng kết quả (ResultsPanel) hay Chữ FINISH to hay cả hai trên Scene View.")]
        [SerializeField] private FinishEditorPreviewMode editorPreviewMode = FinishEditorPreviewMode.ResultsPanel;

        [Header("Race References")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private string vehicleSelectScene = "PoseKart_VehicleSelect";

        [Header("Custom UI Bindings (Kéo thả UI của bạn vào đây)")]
        [Tooltip("Bật nếu bạn muốn dùng UI riêng do bạn tự thiết kế trên Canvas.")]
        [SerializeField] private bool useCustomUI = false;
        [Tooltip("GameObject Root bao bọc toàn bộ màn Finish.")]
        [SerializeField] private GameObject customFinishRoot;
        [Tooltip("CanvasGroup của lớp hoạt ảnh FINISH! to ban đầu.")]
        [SerializeField] private CanvasGroup customFinishGroup;
        [Tooltip("RectTransform của chữ FINISH! (dùng cho hiệu ứng zoom nảy to).")]
        [SerializeField] private RectTransform customFinishRect;
        [Tooltip("Text TextMeshPro hiển thị chữ FINISH! to.")]
        [SerializeField] private TMP_Text customFinishText;
        [Tooltip("Text UI Legacy hiển thị chữ FINISH!.")]
        [SerializeField] private Text customLegacyFinishText;

        [Header("Custom Results Panel (Bảng kết quả)")]
        [Tooltip("CanvasGroup của bảng kết quả hiện ra sau chữ FINISH!.")]
        [SerializeField] private CanvasGroup customResultsGroup;
        [Tooltip("RectTransform của bảng kết quả.")]
        [SerializeField] private RectTransform customResultsRect;
        [Tooltip("Tiêu đề kết quả (VICTORY! hoặc RACE COMPLETE).")]
        [SerializeField] private TMP_Text customResultHeading;
        [SerializeField] private Text customLegacyResultHeading;
        [Tooltip("Thứ hạng về đích (1ST, 2ND, 3RD, v.v.).")]
        [SerializeField] private TMP_Text customPositionText;
        [SerializeField] private Text customLegacyPositionText;
        [Tooltip("Chữ phụ thứ hạng (FINISH POSITION).")]
        [SerializeField] private TMP_Text customPositionCaption;
        [SerializeField] private Text customLegacyPositionCaption;
        [Tooltip("Thời gian đua (TOTAL TIME).")]
        [SerializeField] private TMP_Text customTimeText;
        [SerializeField] private Text customLegacyTimeText;
        [Tooltip("Số vòng (LAPS).")]
        [SerializeField] private TMP_Text customLapsText;
        [SerializeField] private Text customLegacyLapsText;
        [Tooltip("Dòng thông tin phụ (OFFICIAL RESULT).")]
        [SerializeField] private TMP_Text customResultHint;
        [SerializeField] private Text customLegacyResultHint;

        [Header("Custom Buttons (Nút bấm)")]
        [Tooltip("Nút Chơi Lại (Play Again).")]
        [SerializeField] private Button customPlayAgainButton;
        [Tooltip("Nút Quay Về Chọn Xe / Menu.")]
        [SerializeField] private Button customSelectVehicleButton;

        [Header("Presentation & Timing")]
        [SerializeField, Min(0.5f)] private float finishDuration = 1.15f;
        [SerializeField, Min(0.1f)] private float resultsRevealDuration = 0.5f;
        [SerializeField, Min(0f)] private float buttonUnlockDelay = 0.25f;

        // Fallback procedural fields (used only when custom UI is not assigned)
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

        private EventSystem eventSystem;
        private float presentationTime;
        private bool presentationStarted;
        private bool resultsShown;

        private bool HasCustomUI => useCustomUI || customFinishRoot != null || customResultsGroup != null;

        [ContextMenu("Auto-Wire UI Elements")]
        public void AutoWireHierarchy()
        {
            Transform rootT = transform;
            if (customFinishRoot != null)
            {
                rootT = customFinishRoot.transform;
            }
            else
            {
                Transform childFinish = rootT.Find("RaceFinishUI");
                if (childFinish != null && childFinish != rootT)
                {
                    customFinishRoot = childFinish.gameObject;
                    rootT = childFinish;
                }
                else
                {
                    customFinishRoot = gameObject;
                }
            }

            // Find FinishAnimation
            Transform finAnim = rootT.Find("FinishAnimation");
            if (finAnim != null)
            {
                customFinishGroup = finAnim.GetComponent<CanvasGroup>();
                customFinishRect = finAnim.GetComponent<RectTransform>();
                Transform ft = finAnim.Find("FinishText");
                if (ft != null)
                {
                    customFinishText = ft.GetComponent<TMP_Text>();
                    customLegacyFinishText = ft.GetComponent<Text>();
                }
            }

            // Find ResultsPanel
            Transform resPanel = rootT.Find("ResultsPanel");
            if (resPanel != null)
            {
                customResultsGroup = resPanel.GetComponent<CanvasGroup>();
                customResultsRect = resPanel.GetComponent<RectTransform>();

                Transform heading = resPanel.Find("Heading");
                if (heading != null)
                {
                    customResultHeading = heading.GetComponent<TMP_Text>();
                    customLegacyResultHeading = heading.GetComponent<Text>();
                }

                Transform cap = resPanel.Find("PositionCaption");
                if (cap != null)
                {
                    customPositionCaption = cap.GetComponent<TMP_Text>();
                    customLegacyPositionCaption = cap.GetComponent<Text>();
                }

                Transform pos = resPanel.Find("PositionValue");
                if (pos != null)
                {
                    customPositionText = pos.GetComponent<TMP_Text>();
                    customLegacyPositionText = pos.GetComponent<Text>();
                }

                Transform time = resPanel.Find("TotalTime");
                if (time != null)
                {
                    customTimeText = time.GetComponent<TMP_Text>();
                    customLegacyTimeText = time.GetComponent<Text>();
                }

                Transform laps = resPanel.Find("Laps");
                if (laps != null)
                {
                    customLapsText = laps.GetComponent<TMP_Text>();
                    customLegacyLapsText = laps.GetComponent<Text>();
                }

                Transform hint = resPanel.Find("ResultHint");
                if (hint != null)
                {
                    customResultHint = hint.GetComponent<TMP_Text>();
                    customLegacyResultHint = hint.GetComponent<Text>();
                }

                Transform playAgain = resPanel.Find("PlayAgain") ?? resPanel.Find("PlayAgain_Button");
                if (playAgain != null)
                {
                    customPlayAgainButton = playAgain.GetComponent<Button>();
                }

                Transform selectVehicle = resPanel.Find("SelectVehicle") ?? resPanel.Find("SelectVehicle_Button");
                if (selectVehicle != null)
                {
                    customSelectVehicleButton = selectVehicle.GetComponent<Button>();
                }
            }

            if (customResultsGroup != null || customFinishGroup != null)
            {
                useCustomUI = true;
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                if (customFinishRoot == null || customResultsGroup == null)
                {
                    AutoWireHierarchy();
                }
                ApplyEditorPreview();
            }
        }

        public void ApplyEditorPreview()
        {
            GameObject root = customFinishRoot != null ? customFinishRoot : finishRoot;
            CanvasGroup resGroup = customResultsGroup != null ? customResultsGroup : resultsGroup;
            CanvasGroup finGroup = customFinishGroup != null ? customFinishGroup : finishGroup;

            if (root != null)
            {
                root.SetActive(showInEditMode && editorPreviewMode != FinishEditorPreviewMode.Hidden);
            }

            if (resGroup != null)
            {
                bool showRes = showInEditMode && (editorPreviewMode == FinishEditorPreviewMode.ResultsPanel || editorPreviewMode == FinishEditorPreviewMode.Both);
                resGroup.alpha = showRes ? 1f : 0f;
                resGroup.blocksRaycasts = Application.isPlaying && resultsShown;
            }

            if (finGroup != null)
            {
                bool showFin = showInEditMode && (editorPreviewMode == FinishEditorPreviewMode.FinishBanner || editorPreviewMode == FinishEditorPreviewMode.Both);
                finGroup.alpha = showFin ? 1f : 0f;
            }
        }

        private void Awake()
        {
            if (raceManager == null)
            {
                raceManager = FindFirstObjectByType<RaceManager>();
            }

            AutoWireHierarchy();

            if (!HasCustomUI)
            {
                BuildInterface();
            }
            else
            {
                InitCustomUI();
            }

            if (!Application.isPlaying)
            {
                ApplyEditorPreview();
            }
        }

        private void OnEnable()
        {
            if (!HasCustomUI)
            {
                BuildInterface();
            }
            else
            {
                InitCustomUI();
            }

            if (!Application.isPlaying)
            {
                ApplyEditorPreview();
                return;
            }

            if (raceManager != null)
            {
                raceManager.PlayerFinishedEvent += HandlePlayerFinished;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (raceManager != null && raceManager.PlayerFinished)
            {
                HandlePlayerFinished(raceManager);
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (raceManager != null)
            {
                raceManager.PlayerFinishedEvent -= HandlePlayerFinished;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !presentationStarted)
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

        private void InitCustomUI()
        {
            if (customFinishRoot != null)
            {
                customFinishRoot.SetActive(!Application.isPlaying);
            }

            if (customPlayAgainButton != null)
            {
                customPlayAgainButton.onClick.RemoveListener(PlayAgain);
                customPlayAgainButton.onClick.AddListener(PlayAgain);
            }

            if (customSelectVehicleButton != null)
            {
                customSelectVehicleButton.onClick.RemoveListener(SelectVehicle);
                customSelectVehicleButton.onClick.AddListener(SelectVehicle);
            }
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

            if (HasCustomUI)
            {
                if (customFinishRoot != null) customFinishRoot.SetActive(true);
                if (customFinishGroup != null) customFinishGroup.alpha = 1f;
                if (customResultsGroup != null)
                {
                    customResultsGroup.alpha = 0f;
                    customResultsGroup.transform.localScale = Vector3.one * 0.94f;
                }
                if (customFinishRect != null) customFinishRect.localScale = Vector3.one * 0.78f;
                BuildResultValuesCustom(manager);
                SetButtonsInteractable(false);
            }
            else
            {
                if (finishRoot != null) finishRoot.SetActive(true);
                if (finishGroup != null) finishGroup.alpha = 1f;
                if (resultsGroup != null)
                {
                    resultsGroup.alpha = 0f;
                    resultsGroup.transform.localScale = Vector3.one * 0.94f;
                }
                if (finishRect != null) finishRect.localScale = Vector3.one * 0.78f;
                BuildResultValues(manager);
                SetButtonsInteractable(false);
            }

            PoseKart.Audio.AudioManager.PlayRaceFinish();
        }

        private void BuildResultValuesCustom(RaceManager manager)
        {
            int position = manager.PlayerFinishPosition;
            string posLabel = PositionLabel(position);
            Color posColor = PositionColor(position);

            SetText(customPositionText, customLegacyPositionText, posLabel);
            if (customPositionText != null) customPositionText.color = posColor;
            if (customLegacyPositionText != null) customLegacyPositionText.color = posColor;

            string heading = position == 1 ? "VICTORY!" : "RACE COMPLETE";
            SetText(customResultHeading, customLegacyResultHeading, heading);

            string timeStr = $"TOTAL TIME\n{FormatTime(manager.PlayerFinishTimeSeconds)}";
            SetText(customTimeText, customLegacyTimeText, timeStr);

            string lapsStr = $"LAPS\n{manager.CurrentLap} / {manager.TotalLaps}";
            SetText(customLapsText, customLegacyLapsText, lapsStr);

            string hintStr = $"OFFICIAL RESULT  •  {position} / {manager.RacerCount}";
            SetText(customResultHint, customLegacyResultHint, hintStr);
        }

        private static void SetText(TMP_Text tmp, Text legacy, string text)
        {
            if (tmp != null) tmp.text = text;
            if (legacy != null) legacy.text = text;
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

            RectTransform targetRect = HasCustomUI ? customFinishRect : finishRect;
            CanvasGroup targetGroup = HasCustomUI ? customFinishGroup : finishGroup;

            if (targetRect != null) targetRect.localScale = Vector3.one * scale;
            if (targetGroup != null) targetGroup.alpha = t > 0.78f ? Mathf.Lerp(1f, 0f, (t - 0.78f) / 0.22f) : 1f;
        }

        private void RevealResults()
        {
            resultsShown = true;
            presentationTime = 0f;

            if (HasCustomUI)
            {
                if (customFinishGroup != null) customFinishGroup.alpha = 0f;
                if (customResultsGroup != null)
                {
                    customResultsGroup.alpha = 0f;
                    customResultsGroup.transform.localScale = Vector3.one * 0.94f;
                }
            }
            else
            {
                if (finishText != null) finishText.text = string.Empty;
                if (finishGroup != null) finishGroup.alpha = 0f;
                if (resultsGroup != null)
                {
                    resultsGroup.alpha = 0f;
                    resultsGroup.transform.localScale = Vector3.one * 0.94f;
                }
            }

            PoseKart.Audio.AudioManager.PlayResultsReveal();
            if (raceManager != null && raceManager.PlayerFinishPosition == 1)
            {
                PoseKart.Audio.AudioManager.PlayVictory();
            }
        }

        private void AnimateResults()
        {
            float reveal = Mathf.Clamp01(presentationTime / Mathf.Max(0.01f, resultsRevealDuration));
            float eased = Mathf.SmoothStep(0f, 1f, reveal);

            CanvasGroup targetGroup = HasCustomUI ? customResultsGroup : resultsGroup;
            RectTransform targetRect = HasCustomUI ? customResultsRect : resultsRect;

            if (targetGroup != null) targetGroup.alpha = eased;
            if (targetRect != null) targetRect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);

            bool unlocked = presentationTime >= resultsRevealDuration + buttonUnlockDelay;
            SetButtonsInteractable(unlocked);
            if (unlocked && eventSystem == null)
            {
                eventSystem = EventSystem.current;
                Button defaultButton = HasCustomUI ? customPlayAgainButton : playAgainButton;
                if (eventSystem != null && defaultButton != null)
                {
                    eventSystem.SetSelectedGameObject(defaultButton.gameObject);
                }
            }
        }

        public void PlayAgain()
        {
            PoseKart.Audio.AudioManager.PlayButtonClick();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void SelectVehicle()
        {
            PoseKart.Audio.AudioManager.PlayButtonClick();
            if (!Application.CanStreamedLevelBeLoaded(vehicleSelectScene))
            {
                string msg = $"SCENE NOT IN BUILD SETTINGS: {vehicleSelectScene}";
                if (resultHint != null) resultHint.text = msg;
                if (customResultHint != null) customResultHint.text = msg;
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(vehicleSelectScene);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            Button btn1 = HasCustomUI ? customPlayAgainButton : playAgainButton;
            Button btn2 = HasCustomUI ? customSelectVehicleButton : selectVehicleButton;

            if (btn1 != null) btn1.interactable = interactable;
            if (btn2 != null) btn2.interactable = interactable;
        }

        private void BuildResultValues(RaceManager manager)
        {
            int position = manager.PlayerFinishPosition;
            if (finalPositionText != null)
            {
                finalPositionText.text = PositionLabel(position);
                finalPositionText.color = PositionColor(position);
            }
            if (resultHeading != null) resultHeading.text = position == 1 ? "VICTORY!" : "RACE COMPLETE";
            if (timeText != null) timeText.text = $"TOTAL TIME\n{FormatTime(manager.PlayerFinishTimeSeconds)}";
            if (lapsText != null) lapsText.text = $"LAPS\n{manager.CurrentLap} / {manager.TotalLaps}";
            if (resultHint != null) resultHint.text = $"OFFICIAL RESULT  •  {position} / {manager.RacerCount}";
        }

        private void BuildInterface()
        {
            if (HasCustomUI || transform.Find("ResultsPanel") != null || transform.Find("FinishAnimation") != null || gameObject.name == "RaceFinishUI")
            {
                AutoWireHierarchy();
                return;
            }

            bool rootCreated;
            finishRoot = GetOrCreateChild(transform, "RaceFinishUI", out rootCreated);
            RectTransform rootRect = finishRoot.GetComponent<RectTransform>();
            if (rootCreated)
            {
                SetFullScreen(rootRect);
            }

            GetOrAddComponent<CanvasGroup>(finishRoot);
            Image overlay = GetOrAddComponent<Image>(finishRoot);
            if (rootCreated)
            {
                overlay.color = Overlay;
            }
            overlay.raycastTarget = true;

            bool finishLayerCreated;
            GameObject finishLayer = GetOrCreateChild(finishRoot.transform, "FinishAnimation", out finishLayerCreated);
            finishRect = finishLayer.GetComponent<RectTransform>();
            if (finishLayerCreated)
            {
                SetCentered(finishRect, new Vector2(1100f, 260f), new Vector2(0f, 55f));
            }
            finishGroup = GetOrAddComponent<CanvasGroup>(finishLayer);
            bool finishTextCreated;
            finishText = GetOrCreateText("FinishText", "FINISH!", finishLayer.transform, 116,
                Yellow, TextAnchor.MiddleCenter, out finishTextCreated);
            if (finishTextCreated)
            {
                finishText.rectTransform.sizeDelta = new Vector2(1100f, 220f);
                AddOutline(finishText, Navy, new Vector2(3f, -3f));
            }

            bool resultsLayerCreated;
            GameObject resultsLayer = GetOrCreateChild(finishRoot.transform, "ResultsPanel", out resultsLayerCreated);
            resultsRect = resultsLayer.GetComponent<RectTransform>();
            if (resultsLayerCreated)
            {
                SetCentered(resultsRect, new Vector2(760f, 670f), new Vector2(0f, 0f));
            }
            Image panel = GetOrAddComponent<Image>(resultsLayer);
            if (resultsLayerCreated)
            {
                panel.color = new Color(Navy.r, Navy.g, Navy.b, 0.94f);
            }
            panel.raycastTarget = true;
            resultsGroup = GetOrAddComponent<CanvasGroup>(resultsLayer);

            bool headingCreated;
            resultHeading = GetOrCreateText("Heading", "RACE COMPLETE", resultsLayer.transform, 34,
                White, TextAnchor.MiddleCenter, out headingCreated);
            if (headingCreated)
            {
                SetAnchored(resultHeading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -48f), new Vector2(680f, 64f));
                AddOutline(resultHeading, Navy, new Vector2(2f, -2f));
            }

            bool captionCreated;
            finalPositionCaption = GetOrCreateText("PositionCaption", "FINISH POSITION", resultsLayer.transform, 19,
                Cyan, TextAnchor.MiddleCenter, out captionCreated);
            if (captionCreated)
            {
                SetAnchored(finalPositionCaption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -120f), new Vector2(680f, 36f));
            }

            bool positionCreated;
            finalPositionText = GetOrCreateText("PositionValue", "1ST", resultsLayer.transform, 112,
                Yellow, TextAnchor.MiddleCenter, out positionCreated);
            if (positionCreated)
            {
                SetAnchored(finalPositionText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -220f), new Vector2(680f, 150f));
                AddOutline(finalPositionText, Navy, new Vector2(4f, -4f));
            }

            bool timeCreated;
            timeText = GetOrCreateText("TotalTime", "TOTAL TIME\n00:00.000", resultsLayer.transform, 24,
                White, TextAnchor.MiddleLeft, out timeCreated);
            if (timeCreated)
            {
                SetAnchored(timeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(80f, -335f), new Vector2(290f, 82f));
            }

            bool lapsCreated;
            lapsText = GetOrCreateText("Laps", "LAPS\n1 / 1", resultsLayer.transform, 24,
                White, TextAnchor.MiddleLeft, out lapsCreated);
            if (lapsCreated)
            {
                SetAnchored(lapsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-80f, -335f), new Vector2(290f, 82f));
            }

            bool hintCreated;
            resultHint = GetOrCreateText("ResultHint", "OFFICIAL RESULT", resultsLayer.transform, 16,
                Cyan, TextAnchor.MiddleCenter, out hintCreated);
            if (hintCreated)
            {
                SetAnchored(resultHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -415f), new Vector2(680f, 32f));
            }

            playAgainButton = GetOrCreateButton("PlayAgain", "PLAY AGAIN", resultsLayer.transform,
                new Vector2(-155f, -530f));
            selectVehicleButton = GetOrCreateButton("SelectVehicle", "SELECT VEHICLE", resultsLayer.transform,
                new Vector2(155f, -530f));
            playAgainButton.onClick.RemoveListener(PlayAgain);
            selectVehicleButton.onClick.RemoveListener(SelectVehicle);
            playAgainButton.onClick.AddListener(PlayAgain);
            selectVehicleButton.onClick.AddListener(SelectVehicle);

            finishRoot.SetActive(!Application.isPlaying);
        }

        private static string PositionLabel(int position)
        {
            switch (position)
            {
                case 1: return "1ST";
                case 2: return "2ND";
                case 3: return "3RD";
                default: return $"{position}TH";
            }
        }

        private static Color PositionColor(int position)
        {
            switch (position)
            {
                case 1: return Yellow;
                case 2: return Silver;
                case 3: return Bronze;
                default: return White;
            }
        }

        private static string FormatTime(double totalSeconds)
        {
            if (totalSeconds <= 0d) return "00:00.000";
            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
        }

        private static GameObject GetOrCreateChild(Transform parent, string objectName, out bool created)
        {
            Transform existing = parent != null ? parent.Find(objectName) : null;
            if (existing != null)
            {
                created = false;
                return existing.gameObject;
            }

            GameObject result = new GameObject(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            created = true;
            return result;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target != null ? target.GetComponent<T>() : null;
            return component != null ? component : target != null ? target.AddComponent<T>() : null;
        }

        private static Text GetOrCreateText(string objectName, string value, Transform parent, int size,
            Color color, TextAnchor alignment, out bool created)
        {
            GameObject objectRoot = GetOrCreateChild(parent, objectName, out created);
            Text text = GetOrAddComponent<Text>(objectRoot);
            if (created)
            {
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = size;
                text.fontStyle = FontStyle.Bold;
                text.alignment = alignment;
                text.color = color;
                text.text = value;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return text;
        }

        private static Button GetOrCreateButton(string objectName, string label, Transform parent, Vector2 position)
        {
            bool created;
            GameObject objectRoot = GetOrCreateChild(parent, objectName, out created);
            RectTransform rect = objectRoot.GetComponent<RectTransform>();
            if (created)
            {
                SetCentered(rect, new Vector2(250f, 68f), position);
            }
            Image image = GetOrAddComponent<Image>(objectRoot);
            if (created)
            {
                image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
            }
            Button button = GetOrAddComponent<Button>(objectRoot);
            button.targetGraphic = image;

            bool textCreated;
            Text text = GetOrCreateText("Label", label, objectRoot.transform, 19,
                White, TextAnchor.MiddleCenter, out textCreated);
            if (textCreated)
            {
                SetFullScreen(text.rectTransform);
                AddOutline(text, Navy, new Vector2(2f, -2f));
            }

            return button;
        }

        private static void SetFullScreen(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetCentered(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void AddOutline(Text text, Color color, Vector2 distance)
        {
            Outline outline = GetOrAddComponent<Outline>(text.gameObject);
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        [ContextMenu("Preview Finish Screen In Editor")]
        private void PreviewFinishInEditor()
        {
            if (customFinishRoot != null) customFinishRoot.SetActive(true);
            if (finishRoot != null) finishRoot.SetActive(true);
        }

        [ContextMenu("Hide Finish Screen In Editor")]
        private void HideFinishInEditor()
        {
            if (customFinishRoot != null) customFinishRoot.SetActive(false);
            if (finishRoot != null) finishRoot.SetActive(false);
        }
    }
}
