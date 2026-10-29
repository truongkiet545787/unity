using System.Collections;
using PoseKart.Audio;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public enum ShieldVisualState
    {
        Hidden,
        Deploying,
        Equipped,
        Activating,
        ShieldActive,
        Retracting
    }

    /// <summary>
    /// Visual controller for the Force Shield Generator weapon system in PoseKart.
    /// Manages independent attachment points for the physical Shield Generator and Force Shield energy dome.
    /// Follows the complete sequence:
    /// Collect Shield -> Generator Deploys -> Press Item -> Force Shield Activates -> Blocks Attacks -> Shield Expires -> Generator Retracts.
    /// Used identically across player kart and all Demon AI racers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartShieldGeneratorVisual : MonoBehaviour
    {
        [Header("Prefab References")]
        [Tooltip("Prefab thiết bị phát khiên vật lý gắn trên xe (Shield Generator). Kéo thả prefab của bạn vào đây.")]
        [SerializeField] private GameObject shieldGeneratorPrefab;
        [Tooltip("Prefab quả cầu / vòm khiên năng lượng bao quanh xe (Force Shield). Kéo thả prefab của bạn vào đây.")]
        [SerializeField] private GameObject forceShieldPrefab;

        [Header("Attachment Points")]
        [Tooltip("Điểm gắn thiết bị phát khiên trên xe (ShieldGeneratorMount).")]
        [SerializeField] private Transform generatorMountPoint;
        [Tooltip("Điểm tâm bao quanh của khiên năng lượng (ShieldCenter).")]
        [SerializeField] private Transform shieldCenterPoint;

        [Header("Transform Offsets")]
        [Tooltip("Độ lệch vị trí cục bộ của thiết bị phát khiên so với GeneratorMountPoint.")]
        [SerializeField] private Vector3 generatorLocalOffset = Vector3.zero;
        [Tooltip("Góc xoay cục bộ của thiết bị phát khiên.")]
        [SerializeField] private Vector3 generatorLocalEuler = Vector3.zero;
        [Tooltip("Tỉ lệ kích thước tương đối của máy phát khiên (Vector3.one để giữ nguyên tỉ lệ gốc của prefab).")]
        [SerializeField] private Vector3 generatorLocalScale = Vector3.one;

        [Tooltip("Độ lệch vị trí cục bộ của quả cầu khiên so với ShieldCenterPoint.")]
        [SerializeField] private Vector3 shieldLocalOffset = Vector3.zero;
        [Tooltip("Góc xoay cục bộ của quả cầu khiên.")]
        [SerializeField] private Vector3 shieldLocalEuler = Vector3.zero;
        [Tooltip("Tỉ lệ kích thước của quả cầu khiên (Vector3.one để giữ nguyên tỉ lệ gốc của prefab).")]
        [SerializeField] private Vector3 shieldVisualScale = Vector3.one;

        [Header("Generator Animation")]
        [Tooltip("Animator tùy chọn trên prefab máy phát khiên (nếu prefab có chứa Animator).")]
        [SerializeField] private Animator generatorAnimator;
        [Tooltip("Tên trigger kích hoạt mở/mọc máy phát khiên trong Animator.")]
        [SerializeField] private string animatorDeployTrigger = "Deploy";
        [Tooltip("Tên trigger kích hoạt phát năng lượng khiên trong Animator.")]
        [SerializeField] private string animatorActivateTrigger = "Activate";
        [Tooltip("Tên trigger thu hồi máy phát khiên trong Animator.")]
        [SerializeField] private string animatorRetractTrigger = "Retract";
        [Tooltip("Thời gian mọc máy phát khiên (giây).")]
        [SerializeField, Range(0.1f, 1.5f)] private float deployDuration = 0.35f;
        [Tooltip("Khoảng cách trồi lên từ bệ gắn khi mọc (Transform-based animation).")]
        [SerializeField, Range(0f, 1.5f)] private float deployDistance = 0.40f;
        [Tooltip("Thời gian thu hồi máy phát khiên (giây).")]
        [SerializeField, Range(0.1f, 1.5f)] private float retractDuration = 0.30f;

        [Header("Shield Visual Behavior")]
        [Tooltip("Thời gian hiệu ứng bung nở phóng to khi kích hoạt khiên (giây).")]
        [SerializeField, Range(0.05f, 0.8f)] private float activationDuration = 0.22f;
        [Tooltip("Thời gian hiệu ứng thu nhỏ biến mất khi khiên tắt (giây).")]
        [SerializeField, Range(0.05f, 0.8f)] private float deactivationDuration = 0.20f;
        [Tooltip("Thời gian cảnh báo sắp hết khiên trước khi tắt (nhấp nháy/nhịp thở, tính bằng giây).")]
        [SerializeField, Range(0.2f, 3.0f)] private float expirationWarningTime = 1.2f;

        [Header("Audio")]
        [Tooltip("Âm thanh khi thiết bị phát khiên mọc lên.")]
        [SerializeField] private AudioClip deployClip;
        [Tooltip("Âm thanh khi kích hoạt năng lượng khiên.")]
        [SerializeField] private AudioClip activationClip;
        [Tooltip("Âm thanh khi khiên chặn đứng đòn tấn công.")]
        [SerializeField] private AudioClip impactClip;
        [Tooltip("Âm thanh khi khiên hết hạn / sụp đổ.")]
        [SerializeField] private AudioClip expirationClip;
        [Tooltip("Âm thanh khi thiết bị phát khiên thu hồi.")]
        [SerializeField] private AudioClip retractClip;
        [Tooltip("AudioSource phát âm thanh khiên rè rầm lặp lại (tùy chọn).")]
        [SerializeField] private AudioSource shieldAudioSource;
        [Tooltip("Âm thanh lặp lại (loop) trong suốt thời gian khiên hoạt động.")]
        [SerializeField] private AudioClip shieldLoopClip;

        [Header("VFX")]
        [Tooltip("Hiệu ứng lóe sáng tại máy phát khiên lúc bấm kích hoạt.")]
        [SerializeField] private GameObject activationVfxPrefab;
        [Tooltip("Hiệu ứng tia lửa / xung kích nổ tại điểm chặn đòn tấn công.")]
        [SerializeField] private GameObject shieldImpactVfxPrefab;
        [Tooltip("Hiệu ứng tan biến / sụp đổ khi khiên hết hạn.")]
        [SerializeField] private GameObject expirationVfxPrefab;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private ShieldVisualState currentState = ShieldVisualState.Hidden;
        [SerializeField] private bool isGeneratorEquipped;
        [SerializeField] private bool isShieldActive;

        // Instance GameObjects
        private GameObject generatorInstance;
        private GameObject forceShieldInstance;

        // Cached Components
        private CarController car;
        private KartItemInventory inventory;
        private KartItemDefense defense;

        // Coroutines
        private Coroutine activeGeneratorAnim;
        private Coroutine activeShieldAnim;
        private Coroutine activeImpactAnim;

        // Base transform references
        private Vector3 generatorRestingLocalPos;
        private Vector3 generatorTargetScale;
        private Vector3 forceShieldTargetScale;

        public bool HasForceShieldPrefab => forceShieldPrefab != null;
        public bool HasGeneratorPrefab => shieldGeneratorPrefab != null;
        public ShieldVisualState CurrentState => currentState;
        public bool IsGeneratorEquipped => isGeneratorEquipped;
        public bool IsShieldActive => isShieldActive;

        public GameObject ShieldGeneratorPrefab { get => shieldGeneratorPrefab; set => shieldGeneratorPrefab = value; }
        public GameObject ForceShieldPrefab { get => forceShieldPrefab; set => forceShieldPrefab = value; }
        public Transform GeneratorMountPoint { get => generatorMountPoint; set => generatorMountPoint = value; }
        public Transform ShieldCenterPoint { get => shieldCenterPoint; set => shieldCenterPoint = value; }

        private void Awake()
        {
            SetupHierarchy();
            EnsureReferences();
        }

        private void OnEnable()
        {
            EnsureReferences();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        public void EnsureReferences()
        {
            if (car == null) car = GetComponent<CarController>();

            if (inventory == null)
            {
                inventory = GetComponent<KartItemInventory>();
                if (inventory != null)
                {
                    inventory.ItemCollected -= OnItemCollected;
                    inventory.ItemUsed -= OnItemUsed;
                    inventory.ItemExpired -= OnItemExpired;

                    inventory.ItemCollected += OnItemCollected;
                    inventory.ItemUsed += OnItemUsed;
                    inventory.ItemExpired += OnItemExpired;
                }
            }

            if (defense == null)
            {
                defense = GetComponent<KartItemDefense>();
                if (defense != null)
                {
                    defense.OnShieldActivated -= OnShieldActivated;
                    defense.OnShieldBlocked -= OnShieldBlocked;
                    defense.OnShieldDeactivated -= OnShieldDeactivated;

                    defense.OnShieldActivated += OnShieldActivated;
                    defense.OnShieldBlocked += OnShieldBlocked;
                    defense.OnShieldDeactivated += OnShieldDeactivated;
                }
            }
        }

        private void Start()
        {
            // Initial hide state
            if (generatorInstance != null)
            {
                generatorInstance.SetActive(false);
            }
            if (forceShieldInstance != null)
            {
                forceShieldInstance.SetActive(false);
            }
            currentState = ShieldVisualState.Hidden;
            isGeneratorEquipped = false;
            isShieldActive = false;
        }

        private void SubscribeEvents()
        {
            if (inventory != null)
            {
                inventory.ItemCollected -= OnItemCollected;
                inventory.ItemUsed -= OnItemUsed;
                inventory.ItemExpired -= OnItemExpired;

                inventory.ItemCollected += OnItemCollected;
                inventory.ItemUsed += OnItemUsed;
                inventory.ItemExpired += OnItemExpired;
            }

            if (defense != null)
            {
                defense.OnShieldActivated -= OnShieldActivated;
                defense.OnShieldBlocked -= OnShieldBlocked;
                defense.OnShieldDeactivated -= OnShieldDeactivated;

                defense.OnShieldActivated += OnShieldActivated;
                defense.OnShieldBlocked += OnShieldBlocked;
                defense.OnShieldDeactivated += OnShieldDeactivated;
            }
        }

        private void UnsubscribeEvents()
        {
            if (inventory != null)
            {
                inventory.ItemCollected -= OnItemCollected;
                inventory.ItemUsed -= OnItemUsed;
                inventory.ItemExpired -= OnItemExpired;
            }

            if (defense != null)
            {
                defense.OnShieldActivated -= OnShieldActivated;
                defense.OnShieldBlocked -= OnShieldBlocked;
                defense.OnShieldDeactivated -= OnShieldDeactivated;
            }
        }

        public void SetupHierarchy()
        {
            // 1. Ensure Generator Mount Point
            if (generatorMountPoint == null)
            {
                Transform existing = transform.Find("ShieldGeneratorMount");
                if (existing != null)
                {
                    generatorMountPoint = existing;
                }
                else
                {
                    GameObject mount = new GameObject("ShieldGeneratorMount");
                    mount.transform.SetParent(transform, false);
                    mount.transform.localPosition = new Vector3(0f, 1.05f, -0.45f);
                    mount.transform.localRotation = Quaternion.identity;
                    generatorMountPoint = mount.transform;
                }
            }

            // 2. Ensure Shield Center Point
            if (shieldCenterPoint == null || (generatorMountPoint != null && shieldCenterPoint.IsChildOf(generatorMountPoint)))
            {
                Transform existing = transform.Find("ShieldCenter");
                if (existing != null)
                {
                    shieldCenterPoint = existing;
                }
                else
                {
                    GameObject center = new GameObject("ShieldCenter");
                    center.transform.SetParent(transform, false);
                    center.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                    center.transform.localRotation = Quaternion.identity;
                    shieldCenterPoint = center.transform;
                }
            }

            // 3. Ensure Generator Instance
            if (generatorInstance == null && generatorMountPoint != null)
            {
                if (generatorMountPoint.childCount > 0)
                {
                    generatorInstance = generatorMountPoint.GetChild(0).gameObject;
                }
                else if (shieldGeneratorPrefab != null)
                {
                    generatorInstance = Instantiate(shieldGeneratorPrefab, generatorMountPoint);
                    generatorInstance.name = "GeneratorInstance";
                }
            }

            if (generatorInstance != null)
            {
                generatorInstance.transform.localPosition = generatorLocalOffset;
                generatorInstance.transform.localRotation = Quaternion.Euler(generatorLocalEuler);
                generatorRestingLocalPos = generatorInstance.transform.localPosition;

                Vector3 pScale = shieldGeneratorPrefab != null ? shieldGeneratorPrefab.transform.localScale : generatorInstance.transform.localScale;
                if (pScale.sqrMagnitude < 0.001f) pScale = Vector3.one;
                generatorTargetScale = Vector3.Scale(pScale, generatorLocalScale);
                generatorInstance.transform.localScale = generatorTargetScale;

                if (generatorAnimator == null)
                {
                    generatorAnimator = generatorInstance.GetComponentInChildren<Animator>();
                }
            }

            // 4. Ensure Force Shield Instance
            if (forceShieldInstance == null && shieldCenterPoint != null)
            {
                if (shieldCenterPoint.childCount > 0)
                {
                    forceShieldInstance = shieldCenterPoint.GetChild(0).gameObject;
                }
                else if (forceShieldPrefab != null)
                {
                    forceShieldInstance = Instantiate(forceShieldPrefab, shieldCenterPoint);
                    forceShieldInstance.name = "ForceShieldInstance";
                }
            }

            if (forceShieldInstance != null)
            {
                forceShieldInstance.transform.localPosition = shieldLocalOffset;
                forceShieldInstance.transform.localRotation = Quaternion.Euler(shieldLocalEuler);

                RecalculateShieldScale();
                forceShieldInstance.transform.localScale = forceShieldTargetScale;

                // Ensure no non-kinematic Rigidbody causes instability
                Rigidbody rb = forceShieldInstance.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                }

                // Ensure visual colliders do not block kart movement or driving
                Collider[] cols = forceShieldInstance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && !cols[i].isTrigger)
                    {
                        cols[i].isTrigger = true;
                    }
                }
            }

            // 5. Ensure AudioSource
            if (shieldAudioSource == null)
            {
                // Do not reuse the kart engine AudioSource: assigning the
                // shield loop there would replace/stop the engine clip.
                shieldAudioSource = gameObject.AddComponent<AudioSource>();
            }

            shieldAudioSource.playOnAwake = false;
            shieldAudioSource.loop = true;
            shieldAudioSource.spatialBlend = 1f;
            shieldAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            shieldAudioSource.minDistance = 3f;
            shieldAudioSource.maxDistance = 70f;
            shieldAudioSource.dopplerLevel = 0f;
            shieldAudioSource.volume = 0.45f;
        }

        public void RecalculateShieldScale()
        {
            if (forceShieldInstance == null) return;

            Vector3 pScale = forceShieldPrefab != null ? forceShieldPrefab.transform.localScale : forceShieldInstance.transform.localScale;
            if (pScale.sqrMagnitude < 0.001f) pScale = Vector3.one;

            Vector3 desiredWorldScale = Vector3.Scale(pScale, shieldVisualScale);
            Transform parent = forceShieldInstance.transform.parent;
            Vector3 parentLossy = parent != null ? parent.lossyScale : Vector3.one;

            forceShieldTargetScale = new Vector3(
                Mathf.Abs(parentLossy.x) > 0.0001f ? desiredWorldScale.x / parentLossy.x : desiredWorldScale.x,
                Mathf.Abs(parentLossy.y) > 0.0001f ? desiredWorldScale.y / parentLossy.y : desiredWorldScale.y,
                Mathf.Abs(parentLossy.z) > 0.0001f ? desiredWorldScale.z / parentLossy.z : desiredWorldScale.z
            );

            PoseKartShieldVisualAnimator anim = forceShieldInstance.GetComponent<PoseKartShieldVisualAnimator>();
            if (anim != null)
            {
                anim.SetBaseScale(forceShieldTargetScale);
            }
        }

        private void Update()
        {
            // Expiration warning pulse
            if (currentState == ShieldVisualState.ShieldActive &&
                isShieldActive &&
                defense != null)
            {
                float remaining = defense.ShieldTimeRemaining;

                if (remaining <= expirationWarningTime &&
                    remaining > 0f)
                {
                    float pulseSpeed = 16f;
                    float pulse =
                        0.88f +
                        Mathf.PingPong(
                            Time.time * pulseSpeed,
                            0.12f
                        );

                    if (forceShieldInstance != null)
                    {
                        forceShieldInstance.transform.localScale =
                            forceShieldTargetScale * pulse;
                    }
                }
            }
        }

        #region Event Callbacks

        private void OnItemCollected(KartItemPickup.ItemType item)
        {
            if (item == KartItemPickup.ItemType.Shield)
            {
                DeployGenerator();
            }
            else
            {
                if (currentState != ShieldVisualState.Hidden && !isShieldActive)
                {
                    RetractGenerator();
                }
            }
        }

        private void OnItemUsed(KartItemPickup.ItemType item)
        {
            if (item != KartItemPickup.ItemType.Shield && !isShieldActive && currentState != ShieldVisualState.Hidden)
            {
                RetractGenerator();
            }
        }

        private void OnItemExpired(KartItemPickup.ItemType item)
        {
            if (!isShieldActive && currentState != ShieldVisualState.Hidden)
            {
                RetractGenerator();
            }
        }

        private void OnShieldActivated()
        {
            ActivateForceShield();
        }

        private void OnShieldBlocked(Vector3 impactPoint)
        {
            HandleShieldBlocked(impactPoint);
        }

        private void OnShieldDeactivated(bool expired)
        {
            Debug.LogWarning(
    $"[SHIELD TRACE] VISUAL OnShieldDeactivated | " +
    $"expired={expired} | state={currentState} | t={Time.time:F2}",
    this
);
            DeactivateForceShield(expired);
        }

        #endregion

        #region Generator Deployment & Retraction

        public void DeployGenerator()
        {
            EnsureReferences();

            if (generatorInstance == null)
            {
                SetupHierarchy();
            }

            if (generatorInstance == null)
            {
                Debug.LogWarning($"[PoseKart Shield] Shield Generator Prefab is unassigned on {name}. Gameplay will function normally without visual generator.", this);
                return;
            }

            if (activeGeneratorAnim != null)
                StopCoroutine(activeGeneratorAnim);

            activeGeneratorAnim = StartCoroutine(DeployGeneratorCoroutine());
        }

        private IEnumerator DeployGeneratorCoroutine()
        {
            currentState = ShieldVisualState.Deploying;
            isGeneratorEquipped = true;
            generatorInstance.SetActive(true);

            // Audio
            if (deployClip != null)
            {
                AudioSource.PlayClipAtPoint(deployClip, generatorMountPoint.position);
            }
            else
            {
                AudioManager.PlayLauncherDeploy(generatorMountPoint.position);
            }

            // Animator trigger
            if (generatorAnimator != null && !string.IsNullOrEmpty(animatorDeployTrigger))
            {
                generatorAnimator.SetTrigger(animatorDeployTrigger);
            }

            // Transform animation: Rise up from recessed position
            Vector3 startPos = generatorRestingLocalPos - Vector3.up * deployDistance;
            Vector3 targetPos = generatorRestingLocalPos;
            generatorInstance.transform.localPosition = startPos;
            generatorInstance.transform.localScale = Vector3.zero;

            float elapsed = 0f;
            while (elapsed < deployDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / deployDuration);
                // Elastic overshoot curve
                float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.15f;

                generatorInstance.transform.localPosition = Vector3.Lerp(startPos, targetPos, t);
                generatorInstance.transform.localScale = Vector3.LerpUnclamped(Vector3.zero, generatorTargetScale * overshoot, t);
                yield return null;
            }

            generatorInstance.transform.localPosition = targetPos;
            generatorInstance.transform.localScale = generatorTargetScale;

            currentState = ShieldVisualState.Equipped;
            activeGeneratorAnim = null;
        }

        public void RetractGenerator()
        {
            if (generatorInstance == null || !generatorInstance.activeSelf)
            {
                currentState = ShieldVisualState.Hidden;
                isGeneratorEquipped = false;
                return;
            }

            if (activeGeneratorAnim != null)
                StopCoroutine(activeGeneratorAnim);

            activeGeneratorAnim = StartCoroutine(RetractGeneratorCoroutine());
        }

        private IEnumerator RetractGeneratorCoroutine()
        {
            currentState = ShieldVisualState.Retracting;
            isGeneratorEquipped = false;

            if (retractClip != null)
            {
                AudioSource.PlayClipAtPoint(retractClip, generatorMountPoint.position);
            }

            if (generatorAnimator != null && !string.IsNullOrEmpty(animatorRetractTrigger))
            {
                generatorAnimator.SetTrigger(animatorRetractTrigger);
            }

            Vector3 startPos = generatorInstance.transform.localPosition;
            Vector3 targetPos = generatorRestingLocalPos - Vector3.up * deployDistance;
            Vector3 startScale = generatorInstance.transform.localScale;

            float elapsed = 0f;
            while (elapsed < retractDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / retractDuration);
                float easeIn = t * t;

                generatorInstance.transform.localPosition = Vector3.Lerp(startPos, targetPos, easeIn);
                generatorInstance.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, easeIn);
                yield return null;
            }

            generatorInstance.transform.localPosition = targetPos;
            generatorInstance.transform.localScale = Vector3.zero;
            generatorInstance.SetActive(false);

            currentState = ShieldVisualState.Hidden;
            activeGeneratorAnim = null;
        }

        #endregion

        #region Force Shield Activation & Impact

        public void ActivateForceShield()
        {
            EnsureReferences();

            // =====================================================
            // Chống double activation.
            //
            // KartItemDefense hiện có thể:
            // 1. gọi ActivateForceShield() trực tiếp
            // 2. sau đó phát OnShieldActivated
            //    -> gọi ActivateForceShield() lần nữa.
            //
            // Không được restart animation lần hai.
            // =====================================================
            if (currentState == ShieldVisualState.Activating ||
                currentState == ShieldVisualState.ShieldActive ||
                isShieldActive)
            {
                return;
            }

            if (forceShieldInstance == null)
            {
                SetupHierarchy();
            }

            if (forceShieldInstance == null)
            {
                Debug.LogWarning(
                    $"[PoseKart Shield] Force Shield Prefab is unassigned on {name}. " +
                    "Gameplay will function normally using fallback protection.",
                    this
                );

                return;
            }

            // =====================================================
            // Nếu người chơi bấm Shield khi generator vẫn đang mọc,
            // stop deploy coroutine và snap generator về vị trí Equipped.
            //
            // Không cho DeployCoroutine và ShieldCoroutine cùng tranh
            // currentState.
            // =====================================================
            if (activeGeneratorAnim != null)
            {
                StopCoroutine(activeGeneratorAnim);
                activeGeneratorAnim = null;
            }

            if (generatorInstance != null)
            {
                generatorInstance.SetActive(true);
                generatorInstance.transform.localPosition =
                    generatorRestingLocalPos;

                generatorInstance.transform.localScale =
                    generatorTargetScale;

                isGeneratorEquipped = true;
            }

            if (activeShieldAnim != null)
            {
                StopCoroutine(activeShieldAnim);
                activeShieldAnim = null;
            }

            activeShieldAnim =
                StartCoroutine(ActivateForceShieldCoroutine());
        }

        private IEnumerator ActivateForceShieldCoroutine()
        {
            currentState = ShieldVisualState.Activating;
            isShieldActive = true;
            // Generator Activation animation / pulse
            if (generatorAnimator != null && !string.IsNullOrEmpty(animatorActivateTrigger))
            {
                generatorAnimator.SetTrigger(animatorActivateTrigger);
            }

            // Activation VFX
            if (activationVfxPrefab != null)
            {
                GameObject vfx = Instantiate(activationVfxPrefab, generatorMountPoint.position, generatorMountPoint.rotation);
                Destroy(vfx, 2.5f);
            }

            // KartItemDefense owns one-shot Shield audio. Keeping audio out
            // of the visual path prevents duplicate Host/Client playback.

            // Looping Audio
            if (shieldAudioSource != null && shieldLoopClip != null)
            {
                shieldAudioSource.clip = shieldLoopClip;
                shieldAudioSource.loop = true;
                shieldAudioSource.Play();
            }

            // Expand Force Shield
            RecalculateShieldScale();
            PoseKartShieldVisualAnimator anim = forceShieldInstance.GetComponent<PoseKartShieldVisualAnimator>();
            if (anim != null)
            {
                anim.SetBaseScale(forceShieldTargetScale);
            }

            forceShieldInstance.SetActive(true);
            forceShieldInstance.transform.localScale = Vector3.zero;

            if (anim != null)
            {
                anim.BeginFadeIn();
            }

            float elapsed = 0f;
            while (elapsed < activationDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / activationDuration);
                float ease = Mathf.SmoothStep(0f, 1f, t);

                forceShieldInstance.transform.localScale = Vector3.Lerp(Vector3.zero, forceShieldTargetScale, ease);
                yield return null;
            }

            forceShieldInstance.transform.localScale = forceShieldTargetScale;
            currentState = ShieldVisualState.ShieldActive;
            isShieldActive = true;
            activeShieldAnim = null;
        }

        public void HandleShieldBlocked(Vector3 impactPoint)
        {
            // Impact VFX
            Vector3 vfxPos = impactPoint != Vector3.zero
                ? impactPoint
                : (shieldCenterPoint != null ? shieldCenterPoint.position : transform.position + Vector3.up * 0.65f);

            if (shieldImpactVfxPrefab != null)
            {
                GameObject vfx = Instantiate(shieldImpactVfxPrefab, vfxPos, Quaternion.identity);
                Destroy(vfx, 2.0f);
            }

            // KartItemDefense / its network bridge owns impact audio.

            // Impact Pulse Animation
            if (forceShieldInstance != null && forceShieldInstance.activeSelf)
            {
                if (activeImpactAnim != null)
                    StopCoroutine(activeImpactAnim);
                activeImpactAnim = StartCoroutine(ImpactPulseCoroutine());
            }
        }

        private IEnumerator ImpactPulseCoroutine()
        {
            if (forceShieldInstance == null) yield break;

            Vector3 punchScale = forceShieldTargetScale * 1.05f;
            float punchDuration = 0.05f;
            float elapsed = 0f;

            while (elapsed < punchDuration)
            {
                elapsed += Time.deltaTime;
                forceShieldInstance.transform.localScale = Vector3.Lerp(forceShieldTargetScale, punchScale, elapsed / punchDuration);
                yield return null;
            }

            float returnDuration = 0.08f;
            elapsed = 0f;
            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                forceShieldInstance.transform.localScale = Vector3.Lerp(punchScale, forceShieldTargetScale, elapsed / returnDuration);
                yield return null;
            }

            forceShieldInstance.transform.localScale = forceShieldTargetScale;
            activeImpactAnim = null;
        }

        public void DeactivateForceShield(bool expired)
        {
            if (activeShieldAnim != null)
                StopCoroutine(activeShieldAnim);

            activeShieldAnim = StartCoroutine(DeactivateForceShieldCoroutine(expired));
        }

        private IEnumerator DeactivateForceShieldCoroutine(bool expired)
        {
            isShieldActive = false;

            // Stop looping audio
            if (shieldAudioSource != null && shieldAudioSource.isPlaying)
            {
                shieldAudioSource.Stop();
            }

            // Expiration VFX
            Vector3 center = shieldCenterPoint != null ? shieldCenterPoint.position : transform.position + Vector3.up * 0.65f;
            if (expirationVfxPrefab != null)
            {
                GameObject vfx = Instantiate(expirationVfxPrefab, center, Quaternion.identity);
                Destroy(vfx, 2.5f);
            }

            // KartItemDefense / its network bridge owns expiration audio.

            // Collapse Force Shield
            if (forceShieldInstance != null && forceShieldInstance.activeSelf)
            {
                Vector3 startScale = forceShieldInstance.transform.localScale;
                float elapsed = 0f;
                while (elapsed < deactivationDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / deactivationDuration);
                    forceShieldInstance.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t * t);
                    yield return null;
                }

                forceShieldInstance.transform.localScale = Vector3.zero;
                forceShieldInstance.SetActive(false);
            }

            // Retract Generator
            yield return RetractGeneratorCoroutine();

            currentState = ShieldVisualState.Hidden;
            isShieldActive = false;
            activeShieldAnim = null;
        }

        public void ResetVisualsImmediate()
        {
            if (activeGeneratorAnim != null) StopCoroutine(activeGeneratorAnim);
            if (activeShieldAnim != null) StopCoroutine(activeShieldAnim);
            if (activeImpactAnim != null) StopCoroutine(activeImpactAnim);
            activeGeneratorAnim = null;
            activeShieldAnim = null;
            activeImpactAnim = null;
            if (shieldAudioSource != null && shieldAudioSource.isPlaying)
            {
                shieldAudioSource.Stop();
            }

            if (forceShieldInstance != null)
            {
                forceShieldInstance.transform.localScale = Vector3.zero;
                forceShieldInstance.SetActive(false);
            }

            if (generatorInstance != null)
            {
                generatorInstance.transform.localScale = Vector3.zero;
                generatorInstance.transform.localPosition = generatorRestingLocalPos - Vector3.up * deployDistance;
                generatorInstance.SetActive(false);
            }

            currentState = ShieldVisualState.Hidden;
            isGeneratorEquipped = false;
            isShieldActive = false;
        }

        #endregion
    }
}
