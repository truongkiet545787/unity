using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.Race
{
    /// <summary>
    /// Displays an arcade pop-up banner ("BLOCKED!" / "PARRY!") when the player's shield
    /// successfully deflects an incoming missile, hazard, or vehicle collision.
    /// Supports custom UI elements assigned directly in the Inspector or a dedicated Prefab.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PoseKartShieldBlockedUI : MonoBehaviour
    {
        [ContextMenu("Apply Editor Preview")]
        private void ApplyEditorPreview()
        {
            if (Application.isPlaying)
                return;

            if (bannerGroup != null)
                bannerGroup.alpha = showInEditMode ? 1f : 0f;

            if (bannerRect != null)
            {
                bannerRect.anchoredPosition = baseAnchoredPos;
                bannerRect.localScale = Vector3.one;
            }
        }
        private static PoseKartShieldBlockedUI instance;

        [Header("Editor Preview & Alignment (Bật để hiện lên Scene căn chỉnh)")]
        [Tooltip("Bật để hiển thị banner rõ ràng trên Scene View khi thiết kế. Khi bấm Play game sẽ tự động ẩn đi.")]
        [SerializeField] private bool showInEditMode = true;

        [Header("Custom UI Bindings (Kéo thả UI của bạn vào đây)")]
        [Tooltip("CanvasGroup chứa toàn bộ banner để làm mờ dần khi ẩn.")]
        [SerializeField] private CanvasGroup bannerGroup;
        [Tooltip("RectTransform của khung banner hiển thị (dùng để phóng to và bay lên).")]
        [SerializeField] private RectTransform bannerRect;
        [Tooltip("Text TextMeshPro hiển thị chữ BLOCKED! hoặc PARRY!.")]
        [SerializeField] private TMP_Text bannerText;
        [Tooltip("Text UI Legacy (nếu không dùng TextMeshPro).")]
        [SerializeField] private Text legacyBannerText;
        [Tooltip("Hình nền / Khung viền của Banner (tùy chọn).")]
        [SerializeField] private Image bannerBg;

        [Header("Style & Positioning")]
        [SerializeField] private string defaultCalloutText = "BLOCKED!";
        [SerializeField] private Vector2 baseAnchoredPos = new Vector2(0f, 140f);
        [SerializeField] private Color defaultTextColor = new Color(1f, 0.92f, 0.25f, 1f);

        [Header("Animation Settings")]
        [SerializeField, Min(0.1f)] private float displayDuration = 0.85f;
        [SerializeField, Min(1f)] private float punchScale = 1.45f;
        [SerializeField, Min(0f)] private float floatUpSpeed = 28f;
        [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.3f;

        [Header("Audio (Tùy chọn)")]
        [Tooltip("Âm thanh kim loại / PING khi chặn đòn (nếu để trống sẽ dùng âm thanh procedural).")]
        [SerializeField] private AudioClip customDeflectAudio;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.95f;

        private float showUntil;
        private bool isCustomUIAssigned;
        private AudioSource audioSource;

        private void OnValidate()
        {
            displayDuration = Mathf.Max(0.1f, displayDuration);
            punchScale = Mathf.Max(1f, punchScale);
            floatUpSpeed = Mathf.Max(0f, floatUpSpeed);
            fadeOutDuration = Mathf.Max(0.05f, fadeOutDuration);

            // Không thay đổi CanvasGroup, RectTransform
            // hoặc UI hierarchy tại đây.
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (FindFirstObjectByType<PoseKartShieldBlockedUI>() == null)
            {
                GameObject root = new GameObject("[PoseKart_ShieldBlockedUI]");
                root.AddComponent<PoseKartShieldBlockedUI>();
                DontDestroyOnLoad(root);
            }
        }

        public static void ShowBlocked(string calloutText = null)
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<PoseKartShieldBlockedUI>();
                if (instance == null)
                {
                    AutoInitialize();
                }
            }

            if (instance != null)
            {
                instance.TriggerCallout(string.IsNullOrEmpty(calloutText) ? instance.defaultCalloutText : calloutText);
            }
        }

        private void Awake()
        {

            if (!Application.isPlaying)
                return;

            if (instance != null && instance != this)
            {
                // If there's an existing instance that is procedural fallback, prefer the scene-configured instance
                if (!instance.isCustomUIAssigned && (bannerRect != null || bannerGroup != null))
                {
                    Destroy(instance.gameObject);
                    instance = this;
                }
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                instance = this;
            }

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }

            // Check if user wired up custom UI in Inspector
            if (bannerRect != null || bannerGroup != null)
            {
                isCustomUIAssigned = true;
                if (bannerGroup != null)
                {
                    bannerGroup.alpha = 0f;
                }
            }
            else
            {
                BuildDefaultUI();
            }
        }

        private void Start()
        {
            if (Application.isPlaying && bannerGroup != null)
            {
                bannerGroup.alpha = 0f;
            }
        }

        public void TriggerCallout(string text)
        {
            SetText(text);

            if (customDeflectAudio != null && audioSource != null)
            {
                audioSource.PlayOneShot(customDeflectAudio, audioVolume);
            }

            showUntil = Time.time + displayDuration;
            if (bannerGroup != null)
            {
                bannerGroup.alpha = 1f;
            }

            if (bannerRect != null)
            {
                bannerRect.anchoredPosition = baseAnchoredPos;
                bannerRect.localScale = Vector3.one * punchScale;
            }
        }

        private void SetText(string text)
        {
            if (bannerText != null)
            {
                bannerText.text = text;
            }
            if (legacyBannerText != null)
            {
                legacyBannerText.text = text;
            }
        }

        private void Update()
        {
            if (bannerGroup == null || bannerGroup.alpha <= 0f) return;

            float remaining = showUntil - Time.time;
            if (remaining <= 0f)
            {
                bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, 0f, Time.deltaTime * 6f);
                return;
            }

            // Punch scale settling to 1.0
            if (bannerRect != null)
            {
                bannerRect.localScale = Vector3.MoveTowards(bannerRect.localScale, Vector3.one, Time.deltaTime * 3.5f);
                bannerRect.anchoredPosition += Vector2.up * (floatUpSpeed * Time.deltaTime);
            }

            // Fade out in last segment
            if (remaining < fadeOutDuration)
            {
                bannerGroup.alpha = remaining / fadeOutDuration;
            }
        }

        private void BuildDefaultUI()
        {
            GameObject canvasObj = new GameObject("ShieldBlocked_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObj.transform.SetParent(transform, false);

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2250;

            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            bannerGroup = canvasObj.GetComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;

            // Banner container
            GameObject bannerObj = new GameObject("Blocked_Banner", typeof(RectTransform), typeof(Image));
            bannerObj.transform.SetParent(canvasObj.transform, false);
            bannerRect = bannerObj.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(340f, 62f);
            bannerRect.anchoredPosition = baseAnchoredPos;

            bannerBg = bannerObj.GetComponent<Image>();
            bannerBg.color = new Color(0.02f, 0.05f, 0.12f, 0.88f);
            bannerBg.raycastTarget = false;

            Outline outline = bannerObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.92f, 1f, 0.95f);
            outline.effectDistance = new Vector2(2.5f, 2.5f);

            // Banner Text
            GameObject textObj = new GameObject("Blocked_Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(bannerObj.transform, false);
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            bannerText = textObj.GetComponent<TextMeshProUGUI>();
            bannerText.text = defaultCalloutText;
            bannerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            bannerText.fontSize = 32f;
            bannerText.alignment = TextAlignmentOptions.Center;
            bannerText.color = defaultTextColor;
            bannerText.raycastTarget = false;
        }

        [ContextMenu("Preview Blocked Animation")]
        private void PreviewInEditor()
        {
            TriggerCallout(defaultCalloutText);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
