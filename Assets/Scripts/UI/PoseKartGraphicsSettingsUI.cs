using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// Runtime graphics settings panel for the Vehicle Select canvas.
    /// The panel is created once and uses PlayerPrefs so the same settings are
    /// applied after switching to the lobby or race scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartGraphicsSettingsUI : MonoBehaviour
    {
        private const string RootName = "PoseKartGraphicsSettingsRoot";
        private const string PanelName = "GraphicsSettingsPanel";

        private Canvas canvas;
        private GameObject panel;
        private Button openButton;
        private Button motionBlurButton;
        private Button postProcessingButton;
        private Button bloomButton;
        private Button shadowButton;
        private TMP_Text motionBlurValue;
        private TMP_Text postProcessingValue;
        private TMP_Text bloomValue;
        private TMP_Text shadowValue;
        private TMP_FontAsset sharedFont;

        public static void AttachToCanvas(Canvas targetCanvas)
        {
            if (targetCanvas == null)
                return;

            PoseKartGraphicsSettingsUI existing =
                targetCanvas.GetComponent<PoseKartGraphicsSettingsUI>();

            if (existing == null)
                existing = targetCanvas.gameObject.AddComponent<PoseKartGraphicsSettingsUI>();

            existing.Initialize(targetCanvas);
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            PoseKartGraphicsSettingsRuntime.Ensure();
        }

        private void Start()
        {
            BuildPanelIfNeeded();
        }

        private void Initialize(Canvas targetCanvas)
        {
            canvas = targetCanvas;
            BuildPanelIfNeeded();
        }

        private void BuildPanelIfNeeded()
        {
            if (canvas == null)
                canvas = GetComponent<Canvas>();

            if (canvas == null || panel != null)
                return;

            Transform existingRoot = canvas.transform.Find(RootName);
            if (existingRoot != null)
            {
                panel = existingRoot.Find(PanelName)?.gameObject;
                if (panel != null)
                    return;
            }

            TMP_Text sample = FindFirstObjectByType<TMP_Text>();
            sharedFont = sample != null ? sample.font : null;

            GameObject root = CreateObject(RootName, canvas.transform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);

            openButton = CreateButton(root.transform, "GRAPHICS", new Vector2(150f, 68f));
            SetAnchored(openButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(150f, 70f), new Vector2(240f, 90f));
            openButton.onClick.AddListener(OpenPanel);

            panel = CreateObject(PanelName, root.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(860f, 600f);

            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.035f, 0.03f, 0.025f, 0.96f);

            TMP_Text title = CreateLabel(panel.transform, "Graphics", 40f, TextAlignmentOptions.Center);
            SetAnchored(title.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(700f, 60f));

            TMP_Text subtitle = CreateLabel(panel.transform,
                "Adjust visual effects for performance and comfort.",
                20f, TextAlignmentOptions.Center);
            subtitle.color = new Color(1f, 0.78f, 0.35f, 0.85f);
            SetAnchored(subtitle.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -122f), new Vector2(700f, 34f));

            motionBlurValue = CreateOption(panel.transform, "Motion Blur", -190f,
                out motionBlurButton);
            postProcessingValue = CreateOption(panel.transform, "Post Processing", -275f,
                out postProcessingButton);
            bloomValue = CreateOption(panel.transform, "Bloom", -360f,
                out bloomButton);
            shadowValue = CreateOption(panel.transform, "Shadow Quality", -445f,
                out shadowButton);

            motionBlurButton.onClick.AddListener(CycleMotionBlur);
            postProcessingButton.onClick.AddListener(CyclePostProcessing);
            bloomButton.onClick.AddListener(CycleBloom);
            shadowButton.onClick.AddListener(CycleShadows);

            Button close = CreateButton(panel.transform, "CLOSE", new Vector2(180f, 54f));
            SetAnchored(close.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 42f), new Vector2(180f, 54f));
            close.onClick.AddListener(ClosePanel);

            RefreshLabels();
            panel.SetActive(false);
        }

        private TMP_Text CreateOption(Transform parent, string label, float y,
            out Button valueButton)
        {
            TMP_Text labelText = CreateLabel(parent, label, 28f, TextAlignmentOptions.MidlineLeft);
            SetAnchored(labelText.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(120f, y), new Vector2(310f, 55f));

            valueButton = CreateButton(parent, string.Empty, new Vector2(220f, 54f));
            SetAnchored(valueButton.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-120f, y), new Vector2(220f, 54f));

            TMP_Text valueText = valueButton.GetComponentInChildren<TMP_Text>();
            valueText.alignment = TextAlignmentOptions.Center;
            return valueText;
        }

        private Button CreateButton(Transform parent, string label, Vector2 size)
        {
            GameObject buttonObject = CreateObject(label + "Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.13f, 0.08f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.13f, 0.08f, 0.95f);
            colors.highlightedColor = new Color(0.38f, 0.25f, 0.1f, 1f);
            colors.pressedColor = new Color(0.65f, 0.42f, 0.12f, 1f);
            button.colors = colors;

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            TMP_Text text = CreateLabel(buttonObject.transform, label, 24f,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.color = new Color(1f, 0.82f, 0.42f, 1f);
            return button;
        }

        private TMP_Text CreateLabel(Transform parent, string text, float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateObject(text + "Label", parent);
            TMP_Text label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = new Color(1f, 0.9f, 0.65f, 1f);
            label.raycastTarget = false;

            if (sharedFont != null)
                label.font = sharedFont;

            return label;
        }

        private static GameObject CreateObject(string objectName, Transform parent)
        {
            GameObject result = new GameObject(objectName,
                typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OpenPanel()
        {
            BuildPanelIfNeeded();
            if (panel == null)
                return;

            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            RefreshLabels();
        }

        private void ClosePanel()
        {
            if (panel != null)
                panel.SetActive(false);
        }

        private void CycleMotionBlur()
        {
            int value = (PlayerPrefs.GetInt(PoseKartGraphicsSettingsRuntime.MotionBlurKey, 1) + 1) % 4;
            PlayerPrefs.SetInt(PoseKartGraphicsSettingsRuntime.MotionBlurKey, value);
            SaveAndApply();
        }

        private void CyclePostProcessing()
        {
            int value = (PlayerPrefs.GetInt(PoseKartGraphicsSettingsRuntime.PostProcessingKey, 2) + 1) % 4;
            PlayerPrefs.SetInt(PoseKartGraphicsSettingsRuntime.PostProcessingKey, value);
            SaveAndApply();
        }

        private void CycleBloom()
        {
            int value = PlayerPrefs.GetInt(PoseKartGraphicsSettingsRuntime.BloomKey, 1) == 0 ? 1 : 0;
            PlayerPrefs.SetInt(PoseKartGraphicsSettingsRuntime.BloomKey, value);
            SaveAndApply();
        }

        private void CycleShadows()
        {
            int value = (PlayerPrefs.GetInt(PoseKartGraphicsSettingsRuntime.ShadowsKey, 2) + 1) % 4;
            PlayerPrefs.SetInt(PoseKartGraphicsSettingsRuntime.ShadowsKey, value);
            SaveAndApply();
        }

        private void SaveAndApply()
        {
            PlayerPrefs.Save();
            PoseKartGraphicsSettingsRuntime.ApplySavedSettings();
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (motionBlurValue == null)
                return;

            motionBlurValue.text = PoseKartGraphicsSettingsRuntime.MotionBlurName;
            postProcessingValue.text = PoseKartGraphicsSettingsRuntime.PostProcessingName;
            bloomValue.text = PoseKartGraphicsSettingsRuntime.BloomEnabled ? "ON" : "OFF";
            shadowValue.text = PoseKartGraphicsSettingsRuntime.ShadowsName;
        }
    }

    /// <summary>
    /// Applies saved graphics settings to URP and survives scene changes.
    /// </summary>
    public sealed class PoseKartGraphicsSettingsRuntime : MonoBehaviour
    {
        public const string MotionBlurKey = "PoseKart.Graphics.MotionBlur";
        public const string PostProcessingKey = "PoseKart.Graphics.PostProcessing";
        public const string BloomKey = "PoseKart.Graphics.Bloom";
        public const string ShadowsKey = "PoseKart.Graphics.Shadows";

        private static PoseKartGraphicsSettingsRuntime instance;

        private Volume volume;
        private VolumeProfile profile;
        private MotionBlur motionBlur;
        private Bloom bloom;

        public static bool BloomEnabled => PlayerPrefs.GetInt(BloomKey, 1) != 0;

        public static string MotionBlurName =>
            LevelName(PlayerPrefs.GetInt(MotionBlurKey, 1));

        public static string PostProcessingName =>
            LevelName(PlayerPrefs.GetInt(PostProcessingKey, 2));

        public static string ShadowsName =>
            LevelName(PlayerPrefs.GetInt(ShadowsKey, 2));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            Ensure();
        }

        public static void Ensure()
        {
            if (instance == null)
                instance = FindFirstObjectByType<PoseKartGraphicsSettingsRuntime>();

            if (instance != null)
                return;

            GameObject root = new GameObject("PoseKartGraphicsSettingsRuntime");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<PoseKartGraphicsSettingsRuntime>();
        }

        public static void ApplySavedSettings()
        {
            Ensure();
            instance.ApplySettings();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            CreateVolume();
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySettings();
        }

        private void CreateVolume()
        {
            GameObject volumeObject = new GameObject("PoseKartGraphicsSettingsVolume");
            volumeObject.transform.SetParent(transform, false);

            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.weight = 1f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PoseKart_GraphicsSettings_RuntimeProfile";
            volume.profile = profile;
            motionBlur = profile.Add<MotionBlur>(true);
            bloom = profile.Add<Bloom>(true);
        }

        private void ApplySettings()
        {
            if (volume == null || motionBlur == null || bloom == null)
                return;

            int post = Mathf.Clamp(PlayerPrefs.GetInt(PostProcessingKey, 2), 0, 3);
            int blur = Mathf.Clamp(PlayerPrefs.GetInt(MotionBlurKey, 1), 0, 3);
            int shadows = Mathf.Clamp(PlayerPrefs.GetInt(ShadowsKey, 2), 0, 3);

            volume.weight = post == 0 ? 0f : 1f;

            motionBlur.active = post > 0 && blur > 0;
            motionBlur.mode.Override(MotionBlurMode.CameraOnly);
            motionBlur.quality.Override(blur >= 3
                ? MotionBlurQuality.High
                : blur == 2 ? MotionBlurQuality.Medium : MotionBlurQuality.Low);
            motionBlur.intensity.Override(blur >= 3
                ? 0.45f : blur == 2 ? 0.28f : 0.14f);

            bloom.active = post > 0 && BloomEnabled;
            bloom.intensity.Override(post >= 3 ? 0.45f : post == 2 ? 0.3f : 0.16f);
            bloom.threshold.Override(1f);

            QualitySettings.shadows = shadows == 0
                ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = shadows >= 3
                ? UnityEngine.ShadowResolution.VeryHigh
                : shadows == 2 ? UnityEngine.ShadowResolution.High : UnityEngine.ShadowResolution.Medium;
            QualitySettings.shadowDistance = shadows == 0
                ? 0f : shadows == 1 ? 45f : shadows == 2 ? 75f : 110f;

            ApplyCameraPostProcessing(post > 0);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplySettings();
        }

        private static void ApplyCameraPostProcessing(bool enabled)
        {
            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
                return;

            UniversalAdditionalCameraData data =
                mainCamera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
                data.renderPostProcessing = enabled;
        }

        private static string LevelName(int value)
        {
            switch (Mathf.Clamp(value, 0, 3))
            {
                case 0: return "OFF";
                case 1: return "LOW";
                case 2: return "MEDIUM";
                default: return "HIGH";
            }
        }
    }
}
