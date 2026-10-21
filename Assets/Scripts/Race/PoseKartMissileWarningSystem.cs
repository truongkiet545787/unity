using System.Collections.Generic;
using PoseKart.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.Race
{
    /// <summary>
    /// Arcade Missile Lock-on & Warning System.
    /// Provides urgent audio/visual alerts when a homing missile targets the player:
    /// - Fast-paced arcade warning beeps ("Beep... Beep... BEEP-BEEP-BEEP!") that accelerate as missile nears.
    /// - Red HUD Warning Reticle & distance readout on screen.
    /// - 3D holographic world-space target reticle over the targeted racer.
    /// Supports custom UI elements assigned directly in the Inspector or a dedicated Prefab.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PoseKartMissileWarningSystem : MonoBehaviour
    {
        private static PoseKartMissileWarningSystem instance;
        public static PoseKartMissileWarningSystem Instance => instance;

        [Header("Editor Preview & Alignment (Bật để hiện lên Scene căn chỉnh)")]
        [Tooltip("Bật để hiển thị thanh cảnh báo rõ ràng trên Scene View khi thiết kế. Khi bấm Play game sẽ tự động ẩn đi.")]
        [SerializeField] private bool showInEditMode = true;

        [Header("Custom UI Bindings (Kéo thả UI của bạn vào đây)")]
        [Tooltip("CanvasGroup chứa thanh cảnh báo (dùng để chớp nháy hoặc bật/tắt).")]
        [SerializeField] private CanvasGroup warningGroup;
        [Tooltip("Khung RectTransform của thanh cảnh báo (dùng để scale/lắc khi nguy cấp).")]
        [SerializeField] private RectTransform warningBox;
        [Tooltip("Text TextMeshPro hiển thị tiêu đề MISSILE LOCK-ON!.")]
        [SerializeField] private TMP_Text warningLabel;
        [Tooltip("Text UI Legacy (nếu không dùng TextMeshPro).")]
        [SerializeField] private Text legacyWarningLabel;
        [Tooltip("Text TextMeshPro hiển thị khoảng cách (ví dụ: 35m).")]
        [SerializeField] private TMP_Text distanceLabel;
        [Tooltip("Text UI Legacy hiển thị khoảng cách.")]
        [SerializeField] private Text legacyDistanceLabel;
        [Tooltip("Icon cảnh báo / icon tên lửa (tùy chọn).")]
        [SerializeField] private Image warningIcon;
        [Tooltip("Khung viền cảnh báo chớp nháy màu đỏ.")]
        [SerializeField] private Image dangerBorder;

        [Header("Custom 3D Reticle Prefab (Tùy chọn)")]
        [Tooltip("Prefab vòng ngắm 3D lơ lửng trên nóc xe bị ngắm. Nếu để trống sẽ dùng vòng ngắm procedural.")]
        [SerializeField] private GameObject customWorldReticlePrefab;
        [SerializeField] private float reticleHeightOffset = 1.35f;

        [Header("Audio Alert Settings")]
        [SerializeField] private bool enableWarningAudio = true;
        [SerializeField, Range(0.1f, 1f)] private float warningVolume = 0.85f;
        [Tooltip("File âm thanh Beep cảnh báo tùy chọn (nếu để trống sẽ dùng procedural synth beep).")]
        [SerializeField] private AudioClip customWarningClip;
        [SerializeField, Min(0.01f)] private float beepMinInterval = 0.08f;
        [SerializeField, Min(0.1f)] private float beepMaxInterval = 0.55f;

        [Header("Target Reticle Colors & Text")]
        [SerializeField] private string warningTitle = "MISSILE LOCK-ON!";
        [SerializeField] private string distanceFormat = "{0:0}m";
        [SerializeField] private Color reticleColor = new Color(1f, 0.12f, 0.18f, 0.95f);
        [SerializeField] private Color warningTextColor = new Color(1f, 0.2f, 0.2f, 1f);

        private CarController playerCar;
        private AudioSource audioSource;
        private AudioClip proceduralBeepClip;
        private bool isCustomUIAssigned;

        // World-space 3D Reticle
        private GameObject worldReticleObject;
        private SpriteRenderer worldReticleRenderer;
        private Transform currentLockedTransform;

        // Beep timing
        private float nextBeepTime;
        private float lastClosestDistance = 999f;
        private bool isWarningActive;

        public bool IsWarningActive => isWarningActive;

        private void OnValidate()
        {
            if (!Application.isPlaying && warningGroup != null)
            {
                warningGroup.alpha = showInEditMode ? 1f : 0f;
                if (warningBox != null)
                {
                    warningBox.anchoredPosition = Vector2.zero;
                }
                if (warningLabel != null) warningLabel.text = warningTitle;
                if (legacyWarningLabel != null) legacyWarningLabel.text = warningTitle;
                if (distanceLabel != null) distanceLabel.text = "35m";
                if (legacyDistanceLabel != null) legacyDistanceLabel.text = "35m";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (FindFirstObjectByType<PoseKartMissileWarningSystem>() == null)
            {
                GameObject root = new GameObject("[PoseKart_MissileWarningSystem]");
                root.AddComponent<PoseKartMissileWarningSystem>();
                DontDestroyOnLoad(root);
            }
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                if (warningGroup != null)
                {
                    warningGroup.alpha = showInEditMode ? 1f : 0f;
                }
                return;
            }

            if (instance != null && instance != this)
            {
                if (!instance.isCustomUIAssigned && (warningGroup != null || warningBox != null))
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
            audioSource.volume = warningVolume;

            proceduralBeepClip = GenerateProceduralBeepClip();

            if (warningGroup != null || warningBox != null)
            {
                isCustomUIAssigned = true;
                if (warningGroup != null)
                {
                    warningGroup.alpha = 0f;
                }
            }
            else
            {
                BuildWarningHUD();
            }

            BuildWorldReticle();
        }

        private void Start()
        {
            if (Application.isPlaying && warningGroup != null)
            {
                warningGroup.alpha = 0f;
            }
        }

        private void Update()
        {
            if (playerCar == null || !playerCar.isActiveAndEnabled)
            {
                FindPlayerCar();
            }

            IReadOnlyList<HomingKartMissile> activeMissiles = HomingKartMissile.ActiveMissiles;

            // 1. Check for incoming missiles targeting the player
            HomingKartMissile incomingToPlayer = null;
            float closestDistanceToPlayer = float.MaxValue;

            // 2. Check for player missiles locking onto an opponent
            HomingKartMissile playerChasingTarget = null;

            for (int i = 0; i < activeMissiles.Count; i++)
            {
                HomingKartMissile missile = activeMissiles[i];
                if (missile == null || missile.IsSpent) continue;

                // Incoming threat to local player
                if (playerCar != null && missile.Target == playerCar)
                {
                    float d = Vector3.Distance(missile.transform.position, playerCar.transform.position);
                    if (d < closestDistanceToPlayer)
                    {
                        closestDistanceToPlayer = d;
                        incomingToPlayer = missile;
                    }
                }

                // Player's missile chasing enemy
                if (playerCar != null && missile.Owner == playerCar && missile.Target != null && missile.Target != playerCar)
                {
                    playerChasingTarget = missile;
                }
            }

            // --- PROCESS WARNING FOR INCOMING MISSILE ON PLAYER ---
            if (incomingToPlayer != null && playerCar != null)
            {
                isWarningActive = true;
                lastClosestDistance = closestDistanceToPlayer;

                // 1. Audio Warning Beep (accelerates as missile gets closer)
                ProcessWarningAudio(closestDistanceToPlayer);

                // 2. Visual HUD Warning on Screen
                UpdateWarningHUD(closestDistanceToPlayer, incomingToPlayer);

                // 3. World Reticle on player kart
                UpdateWorldReticle(playerCar.transform, isVictim: true);
            }
            else
            {
                isWarningActive = false;

                // Hide HUD
                if (warningGroup != null && warningGroup.alpha > 0f)
                {
                    warningGroup.alpha = Mathf.MoveTowards(warningGroup.alpha, 0f, Time.deltaTime * 6f);
                }

                // If player is attacking someone, show reticle on victim instead
                if (playerChasingTarget != null && playerChasingTarget.Target != null)
                {
                    UpdateWorldReticle(playerChasingTarget.Target.transform, isVictim: false);
                }
                else
                {
                    HideWorldReticle();
                }
            }
        }

        private void FindPlayerCar()
        {
            CarController[] cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            for (int i = 0; i < cars.Length; i++)
            {
                RaceParticipant participant = cars[i].GetComponent<RaceParticipant>();
                if (participant != null && participant.IsPlayer)
                {
                    playerCar = cars[i];
                    return;
                }

                if (cars[i].name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    playerCar = cars[i];
                    return;
                }
            }
        }

        private void ProcessWarningAudio(float distance)
        {
            if (!enableWarningAudio) return;

            // Normalize distance 60m down to 4m
            float t = Mathf.Clamp01((distance - 4f) / 56f);
            float interval = Mathf.Lerp(beepMinInterval, beepMaxInterval, t * t);

            if (Time.time >= nextBeepTime)
            {
                nextBeepTime = Time.time + interval;

                AudioClip clipToPlay = customWarningClip != null ? customWarningClip : proceduralBeepClip;
                if (clipToPlay != null && audioSource != null)
                {
                    // Pitch increases slightly as danger nears (1.0 up to 1.35)
                    audioSource.pitch = Mathf.Lerp(1.35f, 1.0f, t);
                    audioSource.PlayOneShot(clipToPlay, warningVolume);
                }
            }
        }

        private void UpdateWarningHUD(float distance, HomingKartMissile missile)
        {
            if (warningGroup != null)
            {
                // Flashing pulse effect
                float flash = Mathf.PingPong(Time.time * 9f, 1f);
                warningGroup.alpha = Mathf.Lerp(0.75f, 1f, flash);
            }

            if (warningBox != null)
            {
                // Subtle urgent shudder
                float shudder = (Random.value - 0.5f) * 4f;
                warningBox.anchoredPosition = new Vector2(shudder, warningBox.anchoredPosition.y);
            }

            string titleStr = warningTitle;
            string distStr = string.Format(distanceFormat, distance);

            if (warningLabel != null) warningLabel.text = titleStr;
            if (legacyWarningLabel != null) legacyWarningLabel.text = titleStr;

            if (distanceLabel != null) distanceLabel.text = distStr;
            if (legacyDistanceLabel != null) legacyDistanceLabel.text = distStr;

            if (dangerBorder != null)
            {
                dangerBorder.color = Color.Lerp(new Color(1f, 0.1f, 0.1f, 0.6f), new Color(1f, 0.3f, 0.3f, 1f), Mathf.PingPong(Time.time * 8f, 1f));
            }
        }

        private void UpdateWorldReticle(Transform targetTransform, bool isVictim)
        {
            if (worldReticleObject == null || targetTransform == null) return;

            worldReticleObject.SetActive(true);
            currentLockedTransform = targetTransform;

            Vector3 hoverPos = targetTransform.position + Vector3.up * reticleHeightOffset;
            worldReticleObject.transform.position = hoverPos;

            // Reticle spins and pulses
            worldReticleObject.transform.Rotate(Vector3.up, 180f * Time.deltaTime, Space.World);

            float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.15f;
            worldReticleObject.transform.localScale = Vector3.one * (1.6f * pulse);

            if (worldReticleRenderer != null)
            {
                Color c = isVictim ? reticleColor : new Color(0.2f, 0.85f, 1f, 0.9f);
                c.a = Mathf.PingPong(Time.time * 7f, 0.4f) + 0.6f;
                worldReticleRenderer.color = c;
            }
        }

        private void HideWorldReticle()
        {
            if (worldReticleObject != null && worldReticleObject.activeSelf)
            {
                worldReticleObject.SetActive(false);
            }
        }

        private void BuildWorldReticle()
        {
            if (customWorldReticlePrefab != null)
            {
                worldReticleObject = Instantiate(customWorldReticlePrefab);
                worldReticleObject.name = "[Custom_World_Reticle]";
                worldReticleObject.SetActive(false);
                return;
            }

            worldReticleObject = new GameObject("[Missile_World_Reticle]");
            worldReticleObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            worldReticleObject.SetActive(false);

            worldReticleRenderer = worldReticleObject.AddComponent<SpriteRenderer>();
            worldReticleRenderer.sprite = GenerateReticleRingSprite();
            worldReticleRenderer.color = reticleColor;
            worldReticleRenderer.sortingOrder = 500;
        }

        private void BuildWarningHUD()
        {
            GameObject canvasObj = new GameObject("MissileWarning_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObj.transform.SetParent(transform, false);

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2200;

            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            warningGroup = canvasObj.GetComponent<CanvasGroup>();
            warningGroup.alpha = 0f;
            warningGroup.blocksRaycasts = false;
            warningGroup.interactable = false;

            // Box container
            GameObject boxObj = new GameObject("Warning_Box", typeof(RectTransform), typeof(Image));
            boxObj.transform.SetParent(canvasObj.transform, false);
            warningBox = boxObj.GetComponent<RectTransform>();
            warningBox.anchorMin = new Vector2(0.5f, 0.82f);
            warningBox.anchorMax = new Vector2(0.5f, 0.82f);
            warningBox.pivot = new Vector2(0.5f, 0.5f);
            warningBox.sizeDelta = new Vector2(440f, 64f);
            warningBox.anchoredPosition = Vector2.zero;

            dangerBorder = boxObj.GetComponent<Image>();
            dangerBorder.color = new Color(0.6f, 0.05f, 0.05f, 0.85f);
            dangerBorder.raycastTarget = false;

            Outline outline = boxObj.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.15f, 0.15f, 0.95f);
            outline.effectDistance = new Vector2(3f, 3f);

            // Warning Title Label
            GameObject labelObj = new GameObject("Warning_Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObj.transform.SetParent(boxObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.04f, 0.1f);
            labelRect.anchorMax = new Vector2(0.72f, 0.9f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            warningLabel = labelObj.GetComponent<TextMeshProUGUI>();
            warningLabel.text = warningTitle;
            warningLabel.fontStyle = FontStyles.Bold;
            warningLabel.fontSize = 26f;
            warningLabel.alignment = TextAlignmentOptions.MidlineLeft;
            warningLabel.color = Color.white;
            warningLabel.raycastTarget = false;

            // Distance Label
            GameObject distObj = new GameObject("Distance_Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            distObj.transform.SetParent(boxObj.transform, false);
            RectTransform distRect = distObj.GetComponent<RectTransform>();
            distRect.anchorMin = new Vector2(0.72f, 0.1f);
            distRect.anchorMax = new Vector2(0.96f, 0.9f);
            distRect.offsetMin = Vector2.zero;
            distRect.offsetMax = Vector2.zero;

            distanceLabel = distObj.GetComponent<TextMeshProUGUI>();
            distanceLabel.text = "0m";
            distanceLabel.fontStyle = FontStyles.Bold;
            distanceLabel.fontSize = 28f;
            distanceLabel.alignment = TextAlignmentOptions.MidlineRight;
            distanceLabel.color = new Color(1f, 0.92f, 0.2f, 1f);
            distanceLabel.raycastTarget = false;
        }

        private Sprite GenerateReticleRingSprite()
        {
            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color transparent = new Color(1f, 1f, 1f, 0f);
            Color solid = Color.white;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float outerR = size * 0.46f;
            float innerR = size * 0.36f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= outerR && dist >= innerR)
                    {
                        // Crosshair tick marks
                        bool isCross = Mathf.Abs(x - center.x) <= 3f || Mathf.Abs(y - center.y) <= 3f;
                        tex.SetPixel(x, y, isCross ? transparent : solid);
                    }
                    else
                    {
                        tex.SetPixel(x, y, transparent);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private AudioClip GenerateProceduralBeepClip()
        {
            int sampleRate = 44100;
            float duration = 0.055f;
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Sin((float)i / count * Mathf.PI);
                samples[i] = Mathf.Sin(2f * Mathf.PI * 1150f * t) * envelope * 0.9f;
            }

            AudioClip clip = AudioClip.Create("Missile_Warning_Procedural_Beep", count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        [ContextMenu("Preview Warning HUD")]
        private void PreviewInEditor()
        {
            UpdateWarningHUD(35.5f, null);
            if (warningGroup != null) warningGroup.alpha = 1f;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            if (worldReticleObject != null)
                Destroy(worldReticleObject);
        }
    }
}
