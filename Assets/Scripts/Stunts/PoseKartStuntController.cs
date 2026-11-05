using System;
using PoseKart.AI;
using PoseKart.Input;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.Events;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Asphalt 8: Airborne Stunt System Controller for PoseKart.
    /// Manages Flat Spins, Barrel Rolls, Normal Jumps, Multi-revolutions, Air Loft,
    /// Clean vs Crash Landing detection, and anti-exploit Nitro rewards.
    /// Operates via visual pivot rotation while keeping Rigidbody physics and colliders completely stable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    [DefaultExecutionOrder(9001)]
    public sealed class PoseKartStuntController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Visual-only Transform to rotate during stunts. If unassigned, auto-finds 'StuntPivot' or first visual model.")]
        [SerializeField] private Transform visualPivot;
        [SerializeField] private CarController car;
        [SerializeField] private Rigidbody body;
        [SerializeField] private PoseKartCinematicCamera cinematicCamera;

        [Header("Stunt Rotation Tuning")]
        [Tooltip("Thời gian (giây) cho 1 vòng quay Flat Spin 360 độ (0.60s - 0.75s).")]
        [SerializeField, Min(0.2f)] private float flatSpinDuration = 0.68f;

        [Tooltip("Thời gian (giây) cho 1 vòng cuộn Barrel Roll 360 độ (0.65s - 0.85s).")]
        [SerializeField, Min(0.2f)] private float barrelRollDuration = 0.90f;

        [Tooltip("Thời gian (giây) cho 1 vòng lộn Flip 360 độ.")]
        [SerializeField, Min(0.2f)] private float flipDuration = 0.80f;

        [Tooltip("Cửa sổ thời gian (giây) trước/sau khi rời dốc để phát hiện động tác Drift kích hoạt Flat Spin.")]
        [SerializeField, Range(0.1f, 0.6f)] private float flatSpinTakeoffWindow = 0.35f;

        [Tooltip("Độ nghiêng xoắn ốc (Corkscrew tilt angle) để tạo đường bay 3D sống động thay vì xoay cứng nhắc.")]
        [SerializeField, Range(0f, 15f)] private float corkscrewPitchAngle = 5.0f;

        [Header("Asphalt 8 Arcade Flight & Landing")]
        [Tooltip("Lực nâng khí động học triệt tiêu một phần trọng lực khi đang bay biểu diễn, tạo đường bay bổng chuẩn Asphalt 8.")]
        [SerializeField] private bool enableAirLoft = true;
        [SerializeField, Range(0f, 0.75f)] private float airLoftGravityCompensation = 0.08f;

        [Tooltip("Thời gian (giây) làm mượt đưa thân xe về vị trí thăng bằng phẳng khi tiếp đất (chống giật hình snap).")]
        [SerializeField, Range(0.05f, 0.3f)] private float landingAlignDuration = 0.14f;

        [Tooltip("Góc lệch tối đa (độ) so với phương thẳng đứng để được công nhận là Tiếp Đất Hoàn Hảo (Clean Landing).")]
        [SerializeField, Range(20f, 75f)] private float cleanLandingAngleThreshold = 45f;

        [Tooltip("Thời gian bay tối thiểu (giây) để cú nhảy thường (Normal Jump) được nhận thưởng.")]
        [SerializeField, Min(0.2f)] private float minimumAirTimeForJump = 0.55f;

        [Header("Stunt Landing Recovery")]
        [Tooltip("Giảm trượt ngang sau khi Barrel Roll/Flip chạm đường thật. Không chỉnh Rigidbody khi chỉ chạm seam.")]
        [SerializeField] private bool enableStuntLandingRecovery = true;
        [Tooltip("Thời gian tối đa để triệt dần vận tốc ngang sau một stunt landing.")]
        [SerializeField, Min(0.05f)] private float stuntLandingRecoveryDuration = 0.38f;
        [Tooltip("Chỉ can thiệp khi vận tốc ngang so với tangent đường vượt ngưỡng này (m/s).")]
        [SerializeField, Min(0f)] private float lateralVelocityThreshold = 2.2f;
        [Tooltip("Tốc độ giảm vận tốc ngang, m/s mỗi giây. Tốc độ tiến được giữ nguyên.")]
        [SerializeField, Min(0f)] private float lateralVelocityDamping = 11f;
        [Tooltip("Chỉ hỗ trợ yaw khi lệch nặng hơn góc này; các cua bình thường không bị ép hướng.")]
        [SerializeField, Range(20f, 80f)] private float yawAssistMinAngle = 48f;
        [Tooltip("Không tự quay nếu lệch quá góc này; để người chơi tự cứu xe khỏi tình huống quay ngược.")]
        [SerializeField, Range(60f, 170f)] private float yawAssistMaxAngle = 105f;
        [Tooltip("Tốc độ yaw assist tối đa, độ/giây.")]
        [SerializeField, Min(0f)] private float yawAssistMaxDegreesPerSecond = 32f;
        [Tooltip("Khi người chơi đang bẻ lái, yaw assist nhường quyền hoàn toàn.")]
        [SerializeField, Range(0f, 0.4f)] private float yawAssistSteeringDeadZone = 0.12f;
        [Tooltip("Cho phép event landing thật đến trễ một chút sau khi ground probe đã báo chạm.")]
        [SerializeField, Min(0.05f)] private float landingConfirmationWindow = 0.55f;

        [Header("Air Stunt Control")]
        [Tooltip("Cho phép input của người chơi thay đổi tốc độ xoay trong không trung. Vẫn giữ auto-assist để stunt dễ hoàn thành.")]
        [SerializeField] private bool enableAirStuntControl = true;
        [Tooltip("Tốc độ xoay tối thiểu so với tốc độ chuẩn của ramp khi không có input.")]
        [SerializeField, Range(0.1f, 1f)] private float minimumAutoRotationFraction = 0.45f;
        [Tooltip("Giới hạn tốc độ xoay tối đa so với tốc độ chuẩn của ramp.")]
        [SerializeField, Range(1f, 2f)] private float maximumInputRotationMultiplier = 1.35f;
        [SerializeField, Range(0f, 0.4f)] private float airInputDeadZone = 0.08f;
        [SerializeField, Range(1f, 6f)] private float maximumRevolutionsPerJump = 4f;

        [Header("Nitro Rewards (Clamped & Anti-Exploit)")]
        [Tooltip("Tỷ lệ Nitro thưởng cho cú nhảy thường (Normal Jump) khi tiếp đất an toàn (+5%).")]
        [SerializeField, Range(0f, 0.2f)] private float normalJumpReward = 0.05f;

        [Tooltip("Tỷ lệ Nitro thưởng cho vòng quay Flat Spin đầu tiên (+10%).")]
        [SerializeField, Range(0f, 0.3f)] private float flatSpinFirstReward = 0.10f;

        [Tooltip("Tỷ lệ Nitro thưởng cho vòng cuộn Barrel Roll đầu tiên (+12%).")]
        [SerializeField, Range(0f, 0.3f)] private float barrelRollFirstReward = 0.12f;

        [Tooltip("Tỷ lệ Nitro thưởng cho mỗi vòng quay/cuộn hoàn thành tiếp theo (+8%).")]
        [SerializeField, Range(0f, 0.2f)] private float additionalRevolutionReward = 0.08f;

        [Tooltip("Tỷ lệ Nitro thưởng thêm khi Tiếp Đất Hoàn Hảo (Clean Landing) (+5%).")]
        [SerializeField, Range(0f, 0.2f)] private float cleanLandingReward = 0.05f;

        [Tooltip("Giới hạn tối đa tổng Nitro thưởng trong 1 cú nhảy (tránh cày điểm quá đà).")]
        [SerializeField, Range(0.2f, 1.0f)] private float maxRewardPerJump = 0.50f;

        [Header("Crash Recovery & Combo")]
        [Tooltip("Mức recovery áp dụng khi stunt tiếp đất lỗi.")]
        [SerializeField, Range(0f, 1f)] private float crashLandingRecoverySeverity = 0.7f;
        [Tooltip("Thời gian giữ combo sau một lần clean landing.")]
        [SerializeField, Min(0.1f)] private float comboWindow = 3f;

        [Header("Presentation Feedback (VFX & Audio)")]
        [Tooltip("Bật vệt khí khi xe đã xác nhận đang airborne. Chỉ là hiệu ứng hình ảnh.")]
        [SerializeField] private bool enableAirflowVfx = true;
        [Tooltip("Tốc độ mặt phẳng tối thiểu (m/s) để vệt khí bắt đầu hiện rõ.")]
        [SerializeField, Min(0f)] private float airflowMinSpeed = 8f;
        [Tooltip("Tốc độ mặt phẳng (m/s) đạt cường độ vệt khí tối đa.")]
        [SerializeField, Min(0.1f)] private float airflowFullSpeed = 32f;
        [Tooltip("Emission thấp nhất của mỗi trail khi xe vừa bay.")]
        [SerializeField, Min(0f)] private float airflowMinEmissionRate = 2f;
        [Tooltip("Emission cao nhất của mỗi trail khi xe bay nhanh. Giữ thấp để không che xe.")]
        [SerializeField, Min(0f)] private float airflowMaxEmissionRate = 24f;
        [Tooltip("Tự tìm Slipstream_VFX/LeftWindTrail và RightWindTrail nếu mảng bên dưới chưa được gán.")]
        [SerializeField] private bool autoFindAirflowTrails = true;
        [SerializeField] private ParticleSystem[] stuntWindTrails;
        [SerializeField] private ParticleSystem landingDustVfx;
        [SerializeField] private AudioClip takeoffAudioClip;
        [SerializeField] private AudioClip spinAudioClip;
        [SerializeField] private AudioClip revolutionAudioClip;
        [SerializeField] private AudioClip cleanLandingAudioClip;
        [SerializeField] private AudioClip crashLandingAudioClip;
        [Tooltip("AudioSource riêng cho tiếng takeoff/spin để có thể dừng ngay khi xe tiếp đất. Nếu để trống, controller tự tạo một source trên xe.")]
        [SerializeField] private AudioSource stuntAirborneAudioSource;

        [Header("Track Direction Source")]
        [Tooltip("RacingLine hiện có của scene. Nếu để trống, controller tự tìm RacingLine đang active.")]
        [SerializeField] private RacingLine racingLine;
        [Tooltip("Fallback theo checkpoint hiện có khi scene không có RacingLine.")]
        [SerializeField] private CheckpointManager checkpointManager;

        [Header("Runtime Stunt State (Read Only)")]
        [SerializeField] private StuntState state = StuntState.Grounded;
        [SerializeField] private StuntType activeStunt = StuntType.None;
        [SerializeField] private int completedRevolutions = 0;
        [SerializeField] private float totalAccumulatedAngle = 0f;
        [SerializeField] private float airborneTime = 0f;
        [SerializeField] private float totalRewardEarned = 0f;
        [SerializeField] private LandingQuality lastLandingQuality = LandingQuality.None;

        // Public Events
        public event Action<StuntType, int> RevolutionCompleted;
        public event Action<StuntType, LandingQuality, float, int> StuntLanded;
        public UnityEvent onCleanLanding = new UnityEvent();

        public StuntState State => state;
        public StuntType ActiveStunt => activeStunt;
        public int CompletedRevolutions => completedRevolutions;
        public bool IsStuntActive => state == StuntState.Airborne || state == StuntState.LandingAligning;
        public float TotalRewardEarned => totalRewardEarned;
        public LandingQuality LastLandingQuality => lastLandingQuality;
        public Transform VisualPivot => visualPivot;
        public int ComboCount => stuntComboCount;

        // Internal State
        private Quaternion neutralRotation = Quaternion.identity;
        private Quaternion baseRotation = Quaternion.identity;
        private Quaternion touchdownRotation = Quaternion.identity;
        private Vector3 previousPosition;
        private PoseKartStuntRamp armedRamp;
        // Kept separate so legacy barrel-roll ramps can arm without losing their type metadata.
        private PoseKartBarrelRollRamp armedLegacyBarrelRollRamp;
        private float rampArmDeadline;
        private float stuntStartTime;
        private float currentCycleDuration = 0.75f;
        private int rotationDirection = 1;
        private float rotationTravelDegrees;
        private float activeRewardMultiplier = 1f;
        private float lastDriftTime = float.NegativeInfinity;
        private float alignStartTime;
        private int lastAwardedRevolution = 0;
        private bool cleanLandingAwarded = false;
        private bool normalJumpAwarded = false;
        private int stuntComboCount;
        private float comboExpiresAt = float.NegativeInfinity;
        private Vector3 flightTrajectoryForward = Vector3.forward;
        private bool networkPresentationOnly;
        private KartLandingEffects landingEffects;
        private bool landingEventSubscribed;
        private bool airflowVfxResolved;
        private bool airflowVfxPlaying;
        private bool networkAirflowActive;
        private bool touchdownAwaitingConfirmation;
        private float landingConfirmationDeadline;
        private StuntType touchdownStuntType = StuntType.None;
        private bool stuntLandingRecoveryActive;
        private float stuntLandingRecoveryUntil;
        private Vector3 stuntLandingTrackTangent;
        private int cachedTrackWaypointIndex;

        public bool IsNetworkPresentationOnly => networkPresentationOnly;
        public bool HasNetworkStuntState => state == StuntState.Airborne || state == StuntState.LandingAligning;
        public float NetworkRotationAngle => totalAccumulatedAngle;
        public int NetworkCompletedRevolutions => completedRevolutions;

        private void Awake()
        {
            ResolveReferences();
            if (visualPivot != null)
            {
                neutralRotation = visualPivot.localRotation;
                baseRotation = neutralRotation;
            }
            previousPosition = transform.position;
        }

        public void ResolveReferences()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null && car != null) body = car.TargetBody;
            if (cinematicCamera == null) cinematicCamera = FindFirstObjectByType<PoseKartCinematicCamera>();
            if (racingLine == null) racingLine = FindFirstObjectByType<RacingLine>();
            if (checkpointManager == null) checkpointManager = FindFirstObjectByType<CheckpointManager>();
            if (landingEffects == null) landingEffects = GetComponent<KartLandingEffects>();
            ResolveAirflowTrails();
            ResolveStuntAirborneAudioSource();

            if (visualPivot == null)
            {
                visualPivot = AutoResolveVisualPivot();
            }
        }

        private void ResolveAirflowTrails()
        {
            if (airflowVfxResolved)
                return;

            airflowVfxResolved = true;
            if (!autoFindAirflowTrails || HasAirflowTrails())
                return;

            Transform vfxRoot = transform.Find("Slipstream_VFX");
            if (vfxRoot == null)
                return;

            ParticleSystem left = vfxRoot.Find("LeftWindTrail")?.GetComponent<ParticleSystem>();
            ParticleSystem right = vfxRoot.Find("RightWindTrail")?.GetComponent<ParticleSystem>();
            if (left == null && right == null)
                return;

            if (left != null && right != null)
            {
                stuntWindTrails = new[] { left, right };
            }
            else
            {
                stuntWindTrails = new[] { left != null ? left : right };
            }
        }

        private bool HasAirflowTrails()
        {
            if (stuntWindTrails == null)
                return false;

            for (int i = 0; i < stuntWindTrails.Length; i++)
            {
                if (stuntWindTrails[i] != null)
                    return true;
            }

            return false;
        }

        private void ResolveStuntAirborneAudioSource()
        {
            if (stuntAirborneAudioSource != null)
                return;

            if (takeoffAudioClip == null && spinAudioClip == null)
                return;

            Transform anchor = transform.Find("StuntAudio");
            if (anchor != null)
            {
                stuntAirborneAudioSource = anchor.GetComponent<AudioSource>();
            }

            if (stuntAirborneAudioSource == null)
            {
                stuntAirborneAudioSource = gameObject.AddComponent<AudioSource>();
            }

            stuntAirborneAudioSource.playOnAwake = false;
            stuntAirborneAudioSource.loop = false;
            stuntAirborneAudioSource.spatialBlend = 0.15f;
            stuntAirborneAudioSource.dopplerLevel = 0f;
        }

        private Transform AutoResolveVisualPivot()
        {
            // 1. Tìm StuntPivot chuyên dụng
            Transform pivot = transform.Find("StuntPivot");
            if (pivot != null) return pivot;

            // 2. Tìm tên chứa visual/model/body/mesh
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                string lowerName = child.name.ToLowerInvariant();
                if (lowerName.Contains("stunt") || lowerName.Contains("visual") ||
                    lowerName.Contains("model") || lowerName.Contains("body") || lowerName.Contains("mesh"))
                {
                    return child;
                }
            }

            // 3. Fallback: child đầu tiên có Renderer
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && r.transform != transform)
            {
                return r.transform;
            }

            return null;
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            SubscribeToLandingConfirmation();
            if (visualPivot != null && state == StuntState.Grounded)
            {
                baseRotation = neutralRotation;
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromLandingConfirmation();
            CancelStuntImmediate();
        }

        private void SubscribeToLandingConfirmation()
        {
            if (landingEffects == null)
                landingEffects = GetComponent<KartLandingEffects>();
            if (landingEffects != null && !landingEventSubscribed)
            {
                landingEffects.GenuineGameplayLandingConfirmed += OnGenuineGameplayLandingConfirmed;
                landingEventSubscribed = true;
            }
        }

        private void UnsubscribeFromLandingConfirmation()
        {
            if (landingEffects != null && landingEventSubscribed)
            {
                landingEffects.GenuineGameplayLandingConfirmed -= OnGenuineGameplayLandingConfirmed;
                landingEventSubscribed = false;
            }
        }

        /// <summary>
        /// Kích hoạt dốc biểu diễn khi xe chạm trigger dốc.
        /// </summary>
        public bool TryArmFromRamp(PoseKartStuntRamp ramp)
        {
            if (networkPresentationOnly || state != StuntState.Grounded || ramp == null || car == null || body == null || !car.ControlEnabled)
            {
                return false;
            }

            Vector3 planarVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            float forwardSpeed = Vector3.Dot(planarVel, transform.forward);
            if (forwardSpeed < ramp.MinimumForwardSpeed)
            {
                return false;
            }

            armedRamp = ramp;
            armedLegacyBarrelRollRamp = null;
            rampArmDeadline = Time.time + ramp.ArmWindow;
            state = StuntState.RampApproach;

            // Áp dụng lực trợ phóng nếu dốc có cấu hình
            if (ramp.ApplyLaunchImpulse)
            {
                float fwdChange = forwardSpeed > 20f ? ramp.ForwardVelocityChange * 0.35f : ramp.ForwardVelocityChange;
                Vector3 impulse = (Vector3.up * ramp.UpwardVelocityChange) +
                                  (transform.forward * fwdChange);
                body.AddForce(impulse, ForceMode.VelocityChange);
                car.BeginRampTakeoff();
            }

            if (ramp.TakeoffSound != null)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(ramp.TakeoffSound, 0.9f);
            }

            return true;
        }

        /// <summary>
        /// Tương thích ngược với các dốc PoseKartBarrelRollRamp cũ có sẵn trong scene.
        /// </summary>
        public bool TryArmFromBarrelRollRamp(PoseKartBarrelRollRamp ramp)
        {
            if (networkPresentationOnly || state != StuntState.Grounded || ramp == null || car == null || body == null || !car.ControlEnabled)
            {
                return false;
            }

            Vector3 planarVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (Vector3.Dot(planarVel, transform.forward) < ramp.MinimumForwardSpeed)
            {
                return false;
            }

            armedRamp = null;
            armedLegacyBarrelRollRamp = ramp;
            rampArmDeadline = Time.time + ramp.ArmWindow;
            state = StuntState.RampApproach;

            if (ramp.ApplyLaunchImpulse)
            {
                float fwdSpd = Vector3.Dot(planarVel, transform.forward);
                float fwdChange = fwdSpd > 20f ? ramp.ForwardVelocityChange * 0.35f : ramp.ForwardVelocityChange;
                Vector3 impulse = (Vector3.up * ramp.UpwardVelocityChange) +
                                  (transform.forward * fwdChange);
                body.AddForce(impulse, ForceMode.VelocityChange);
                car.BeginRampTakeoff();
            }

            return true;
        }

        private void FixedUpdate()
        {
            if (networkPresentationOnly)
            {
                return;
            }

            // Aerodynamic Air Loft: Nâng bổng xe trong không trung chuẩn Asphalt 8
            if (state == StuntState.Airborne && enableAirLoft && body != null && !body.isKinematic)
            {
                if (activeStunt == StuntType.BarrelRoll || activeStunt == StuntType.FlatSpin || activeStunt == StuntType.FrontFlip || activeStunt == StuntType.BackFlip)
                {
                    float counterGravity = Physics.gravity.magnitude * airLoftGravityCompensation;
                    body.AddForce(Vector3.up * counterGravity, ForceMode.Acceleration);
                }
            }

            ApplyStuntLandingRecovery();
        }

        private void Update()
        {
            if (networkPresentationOnly)
            {
                UpdateAirflowVfx(networkAirflowActive);
                return;
            }

            // Bảo vệ an toàn: hủy biểu diễn nếu bị dịch chuyển tức thời hoặc mất điều khiển
            if (car == null || body == null || !car.ControlEnabled ||
                Vector3.Distance(transform.position, previousPosition) > 25f)
            {
                CancelStuntImmediate();
                previousPosition = transform.position;
                return;
            }
            previousPosition = transform.position;

            if (stuntComboCount > 0 && Time.time > comboExpiresAt)
            {
                stuntComboCount = 0;
            }

            // Theo dõi trạng thái Drift gần đây
            if (car.IsGrounded)
            {
                if (car.IsDrifting || IsDriftInputHeld())
                {
                    lastDriftTime = Time.time;
                }
            }

            switch (state)
            {
                case StuntState.Grounded:
                    CheckNaturalLiftoff();
                    break;

                case StuntState.RampApproach:
                    UpdateRampApproach();
                    break;

                case StuntState.Airborne:
                    UpdateAirborneStunt();
                    break;

                case StuntState.LandingAligning:
                    UpdateLandingAlign();
                    break;
            }

            // The state transition above is the single source of truth for cosmetic airflow.
            UpdateAirflowVfx(state == StuntState.Airborne);
        }

        private bool IsDriftInputHeld()
        {
            if (car == null) return false;
            var provider = car.InputProviderSource;
            if (provider is IKartGestureInput gesture && gesture.Drift) return true;
            if (provider is AI.AIInputProvider ai && ai.Drift) return true;
            return false;
        }

        private void CheckNaturalLiftoff()
        {
            if (!car.IsGrounded && body.linearVelocity.y > 1.5f && body.linearVelocity.magnitude > 6.0f)
            {
                // Rời mặt đất tự nhiên ở tốc độ cao
                InitiateAirborne((PoseKartStuntRamp)null);
            }
        }

        private void UpdateRampApproach()
        {
            if (Time.time > rampArmDeadline)
            {
                state = StuntState.Grounded;
                armedRamp = null;
                armedLegacyBarrelRollRamp = null;
                return;
            }

            // Ngay khi bánh xe rời mặt đất -> Chuyển sang cất cánh biểu diễn
            if (!car.IsGrounded)
            {
                if (armedLegacyBarrelRollRamp != null)
                {
                    InitiateAirborneLegacy(armedLegacyBarrelRollRamp);
                }
                else
                {
                    InitiateAirborne(armedRamp);
                }

                armedRamp = null;
                armedLegacyBarrelRollRamp = null;
            }
        }

        private void InitiateAirborne(PoseKartStuntRamp ramp)
        {
            state = StuntState.Airborne;
            stuntStartTime = Time.time;
            airborneTime = 0f;
            completedRevolutions = 0;
            lastAwardedRevolution = 0;
            totalAccumulatedAngle = 0f;
            rotationTravelDegrees = 0f;
            totalRewardEarned = 0f;
            cleanLandingAwarded = false;
            normalJumpAwarded = false;
            lastLandingQuality = LandingQuality.None;

            if (visualPivot != null)
            {
                baseRotation = neutralRotation;
            }

            Vector3 planarVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            flightTrajectoryForward = planarVel.sqrMagnitude > 1f ? planarVel.normalized : transform.forward;

            // XÁC ĐỊNH LOẠI BIỂU DIỄN (STUNT TYPE)
            bool wasDriftingNearTakeoff = (Time.time - lastDriftTime) <= flatSpinTakeoffWindow;

            if (ramp != null)
            {
                currentCycleDuration = ramp.RotationDuration > 0.05f ? ramp.RotationDuration : 0.75f;
                rotationDirection = ramp.RotationDirection;
                activeRewardMultiplier = ramp.RewardMultiplier;

                switch (ramp.Type)
                {
                    case RampType.BarrelRollRamp:
                        activeStunt = StuntType.BarrelRoll;
                        if (ramp.RotationDuration <= 0.05f) currentCycleDuration = barrelRollDuration;
                        break;

                    case RampType.FlipRamp:
                        activeStunt = ramp.FlipType;
                        if (ramp.RotationDuration <= 0.05f) currentCycleDuration = flipDuration;
                        break;

                    case RampType.NormalRamp:
                    default:
                        if (wasDriftingNearTakeoff)
                        {
                            activeStunt = StuntType.FlatSpin;
                            if (ramp.RotationDuration <= 0.05f) currentCycleDuration = flatSpinDuration;
                            float steer = car.SteeringInput;
                            rotationDirection = steer < -0.05f ? -1 : 1;
                        }
                        else
                        {
                            activeStunt = StuntType.NormalJump;
                        }
                        break;
                }
            }
            else
            {
                // Nhảy tự nhiên không qua trigger dốc
                if (wasDriftingNearTakeoff)
                {
                    activeStunt = StuntType.FlatSpin;
                    currentCycleDuration = flatSpinDuration;
                    float steer = car.SteeringInput;
                    rotationDirection = steer < -0.05f ? -1 : 1;
                }
                else
                {
                    activeStunt = StuntType.NormalJump;
                    currentCycleDuration = 1.0f;
                    rotationDirection = 1;
                }
                activeRewardMultiplier = 1f;
            }

            // Kích hoạt Particle Wind Trails
            SetWindTrailsActive(true);

            // Âm thanh cất cánh
            if (takeoffAudioClip != null)
            {
                PlayStuntAirborneAudio(takeoffAudioClip, 0.85f);
            }
            else if (spinAudioClip != null && activeStunt != StuntType.NormalJump)
            {
                PlayStuntAirborneAudio(spinAudioClip, 0.8f);
            }
        }

        private void InitiateAirborneLegacy(PoseKartBarrelRollRamp ramp)
        {
            state = StuntState.Airborne;
            stuntStartTime = Time.time;
            airborneTime = 0f;
            completedRevolutions = 0;
            lastAwardedRevolution = 0;
            totalAccumulatedAngle = 0f;
            rotationTravelDegrees = 0f;
            totalRewardEarned = 0f;
            cleanLandingAwarded = false;
            normalJumpAwarded = false;
            lastLandingQuality = LandingQuality.None;

            if (visualPivot != null)
            {
                baseRotation = neutralRotation;
            }

            Vector3 planarVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            flightTrajectoryForward = planarVel.sqrMagnitude > 1f ? planarVel.normalized : transform.forward;

            activeStunt = StuntType.BarrelRoll;
            currentCycleDuration = ramp != null && ramp.RollDuration > 0.05f
                ? ramp.RollDuration
                : barrelRollDuration;
            rotationDirection = ramp != null ? ramp.RollDirection : 1;
            activeRewardMultiplier = ramp != null && barrelRollFirstReward > 0.001f
                ? ramp.NitroRewardFraction / barrelRollFirstReward
                : 1f;

            SetWindTrailsActive(true);
            if (takeoffAudioClip != null)
            {
                PlayStuntAirborneAudio(takeoffAudioClip, 0.85f);
            }
            else if (spinAudioClip != null)
            {
                PlayStuntAirborneAudio(spinAudioClip, 0.8f);
            }
        }

        private void UpdateAirborneStunt()
        {
            airborneTime = Time.time - stuntStartTime;

            // Nếu tiếp đất
            if (car.IsGrounded)
            {
                HandleTouchdown();
                return;
            }

            // Cập nhật góc xoay biểu diễn. Góc được tích lũy thật theo từng frame
            // thay vì lấy thẳng từ elapsed time, để input có thể tăng/giảm tốc độ stunt.
            if (activeStunt != StuntType.NormalJump && visualPivot != null)
            {
                float controlMultiplier = GetAirRotationMultiplier();
                float degreesPerSecond = 360f / Mathf.Max(0.2f, currentCycleDuration);
                float deltaAngle = degreesPerSecond * controlMultiplier * Time.deltaTime * rotationDirection;
                float maxTravel = maximumRevolutionsPerJump * 360f;
                rotationTravelDegrees = Mathf.Min(maxTravel, rotationTravelDegrees + Mathf.Abs(deltaAngle));
                totalAccumulatedAngle = Mathf.Clamp(totalAccumulatedAngle + deltaAngle, -maxTravel, maxTravel);
                int fullCycles = Mathf.FloorToInt(rotationTravelDegrees / 360f);

                // Kiểm tra hoàn thành thêm 1 vòng 360 độ mới
                if (fullCycles > lastAwardedRevolution)
                {
                    lastAwardedRevolution = fullCycles;
                    completedRevolutions = fullCycles;

                    // Trao thưởng Nitro cho vòng vừa hoàn tất
                    AwardRevolutionReward(activeStunt, fullCycles);
                    RevolutionCompleted?.Invoke(activeStunt, fullCycles);

                    if (revolutionAudioClip != null)
                    {
                        PoseKart.Audio.AudioManager.Instance.PlaySFX(revolutionAudioClip, 0.95f);
                    }
                }

                // Tính toán góc xoay Transform
                float currentAngle = totalAccumulatedAngle;
                Quaternion stuntRotation = Quaternion.identity;

                switch (activeStunt)
                {
                    case StuntType.FlatSpin:
                        stuntRotation = Quaternion.AngleAxis(currentAngle, Vector3.up);
                        break;

                    case StuntType.BarrelRoll:
                        stuntRotation = BuildBarrelRollRotation(currentAngle, rotationDirection);
                        break;

                    case StuntType.FrontFlip:
                        stuntRotation = Quaternion.AngleAxis(currentAngle, Vector3.right);
                        break;

                    case StuntType.BackFlip:
                        stuntRotation = Quaternion.AngleAxis(-currentAngle, Vector3.right);
                        break;
                }

                visualPivot.localRotation = baseRotation * stuntRotation;
            }
        }

        private void AwardRevolutionReward(StuntType type, int revCount)
        {
            float rewardFraction = 0f;
            if (revCount == 1)
            {
                rewardFraction = type == StuntType.BarrelRoll ? barrelRollFirstReward : flatSpinFirstReward;
            }
            else
            {
                rewardFraction = additionalRevolutionReward;
            }

            float finalReward = Mathf.Min(rewardFraction * Mathf.Max(0f, activeRewardMultiplier), maxRewardPerJump - totalRewardEarned);
            if (finalReward > 0f)
            {
                totalRewardEarned += finalReward;
                car.AddNitro(car.MaxNitroChargeAmount * finalReward);
            }
        }

        private void HandleTouchdown()
        {
            StopStuntAirborneAudio();
            SetWindTrailsActive(false);

            touchdownStuntType = activeStunt;
            touchdownAwaitingConfirmation = touchdownStuntType != StuntType.None;
            landingConfirmationDeadline = Time.time + landingConfirmationWindow;

            if (visualPivot != null)
            {
                touchdownRotation = visualPivot.localRotation;
            }
            alignStartTime = Time.time;
            state = StuntState.LandingAligning;

            // ĐÁNH GIÁ CHẤT LƯỢNG TIẾP ĐẤT (CLEAN LANDING VS CRASH LANDING)
            Vector3 vehicleUp = visualPivot != null ? visualPivot.up : transform.up;
            float tiltFromVertical = Vector3.Angle(vehicleUp, Vector3.up);

            bool isUpright = tiltFromVertical <= cleanLandingAngleThreshold;
            bool completedValidAction = false;

            if (activeStunt == StuntType.NormalJump)
            {
                completedValidAction = airborneTime >= minimumAirTimeForJump && isUpright;
                if (completedValidAction && !normalJumpAwarded)
                {
                    normalJumpAwarded = true;
                    float jumpReward = Mathf.Min(normalJumpReward * Mathf.Max(0f, activeRewardMultiplier), maxRewardPerJump - totalRewardEarned);
                    totalRewardEarned += jumpReward;
                    car.AddNitro(car.MaxNitroChargeAmount * jumpReward);
                }
            }
            else
            {
                completedValidAction = completedRevolutions >= 1 && isUpright;
            }

            if (completedValidAction && isUpright)
            {
                lastLandingQuality = LandingQuality.CleanLanding;
                if (!cleanLandingAwarded)
                {
                    cleanLandingAwarded = true;
                    float cleanReward = Mathf.Min(cleanLandingReward * Mathf.Max(0f, activeRewardMultiplier), maxRewardPerJump - totalRewardEarned);
                    totalRewardEarned += cleanReward;
                    car.AddNitro(car.MaxNitroChargeAmount * cleanReward);
                }

                if (cleanLandingAudioClip != null)
                {
                    PoseKart.Audio.AudioManager.Instance.PlaySFX(cleanLandingAudioClip, 0.9f);
                }

                if (landingDustVfx != null)
                {
                    landingDustVfx.Play(true);
                }

                if (cinematicCamera != null)
                {
                    cinematicCamera.PlayImpact(0.85f);
                }

                RegisterSuccessfulCombo();
                onCleanLanding.Invoke();
            }
            else
            {
                lastLandingQuality = LandingQuality.CrashLanding;
                stuntComboCount = 0;
                comboExpiresAt = float.NegativeInfinity;
                car.BeginLandingRecovery(crashLandingRecoverySeverity);
                if (crashLandingAudioClip != null)
                {
                    PoseKart.Audio.AudioManager.Instance.PlaySFX(crashLandingAudioClip, 0.9f);
                }

                if (cinematicCamera != null)
                {
                    cinematicCamera.PlayImpact(1.25f);
                }
            }

            StuntLanded?.Invoke(activeStunt, lastLandingQuality, totalRewardEarned, completedRevolutions);
        }

        private void UpdateLandingAlign()
        {
            float elapsed = Time.time - alignStartTime;
            float t = landingAlignDuration > 0.001f ? Mathf.Clamp01(elapsed / landingAlignDuration) : 1f;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (visualPivot != null)
            {
                visualPivot.localRotation = Quaternion.Slerp(touchdownRotation, neutralRotation, smoothT);
            }

            if (t >= 1f)
            {
                if (visualPivot != null)
                {
                    visualPivot.localRotation = neutralRotation;
                }
                state = StuntState.Grounded;
                activeStunt = StuntType.None;
            }

            if (touchdownAwaitingConfirmation && Time.time > landingConfirmationDeadline)
            {
                touchdownAwaitingConfirmation = false;
                touchdownStuntType = StuntType.None;
            }
        }

        private void OnGenuineGameplayLandingConfirmed(float impactSpeed)
        {
            if (networkPresentationOnly || !enableStuntLandingRecovery || car == null ||
                body == null || !car.ControlEnabled)
            {
                return;
            }

            bool stuntWasActive = state == StuntState.Airborne ||
                                  state == StuntState.LandingAligning ||
                                  touchdownAwaitingConfirmation;
            if (!stuntWasActive)
                return;

            BeginStuntLandingRecovery(impactSpeed);
            touchdownAwaitingConfirmation = false;
            touchdownStuntType = StuntType.None;
        }

        private void BeginStuntLandingRecovery(float impactSpeed)
        {
            if (!TryGetLocalTrackTangent(transform.position, out Vector3 tangent))
                return;

            stuntLandingTrackTangent = tangent;
            float impact01 = Mathf.InverseLerp(2.3f, 10f, impactSpeed);
            float duration = Mathf.Lerp(stuntLandingRecoveryDuration * 0.7f,
                stuntLandingRecoveryDuration, impact01);
            stuntLandingRecoveryUntil = Time.time + Mathf.Max(0.05f, duration);
            stuntLandingRecoveryActive = true;
        }

        private void ApplyStuntLandingRecovery()
        {
            if (!stuntLandingRecoveryActive || body == null || car == null)
                return;
            if (Time.time >= stuntLandingRecoveryUntil)
            {
                stuntLandingRecoveryActive = false;
                return;
            }
            if (!car.IsGrounded)
                return;

            Vector3 tangent = Vector3.ProjectOnPlane(stuntLandingTrackTangent, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.001f)
            {
                stuntLandingRecoveryActive = false;
                return;
            }

            Vector3 velocity = body.linearVelocity;
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float alongTrackSpeed = Vector3.Dot(horizontal, tangent);
            Vector3 lateral = horizontal - tangent * alongTrackSpeed;
            float lateralSpeed = lateral.magnitude;
            if (lateralSpeed > lateralVelocityThreshold && lateralVelocityDamping > 0f)
            {
                float recoveredLateralSpeed = Mathf.MoveTowards(
                    lateralSpeed,
                    0f,
                    lateralVelocityDamping * Time.fixedDeltaTime);
                Vector3 recoveredHorizontal = tangent * alongTrackSpeed +
                    lateral.normalized * recoveredLateralSpeed;
                body.linearVelocity = recoveredHorizontal + Vector3.up * velocity.y;
            }

            ApplyLimitedTrackYawAssist(tangent);
        }

        private void ApplyLimitedTrackYawAssist(Vector3 tangent)
        {
            if (yawAssistMaxDegreesPerSecond <= 0f ||
                Mathf.Abs(car.SteeringInput) > yawAssistSteeringDeadZone)
            {
                return;
            }

            Vector3 currentForward = Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized;
            if (currentForward.sqrMagnitude < 0.001f)
                return;

            float signedAngle = Vector3.SignedAngle(currentForward, tangent, Vector3.up);
            float absoluteAngle = Mathf.Abs(signedAngle);
            if (absoluteAngle < yawAssistMinAngle || absoluteAngle > yawAssistMaxAngle)
                return;

            float step = Mathf.Clamp(signedAngle,
                -yawAssistMaxDegreesPerSecond * Time.fixedDeltaTime,
                yawAssistMaxDegreesPerSecond * Time.fixedDeltaTime);
            body.MoveRotation(Quaternion.AngleAxis(step, Vector3.up) * body.rotation);
        }

        private bool TryGetLocalTrackTangent(Vector3 position, out Vector3 tangent)
        {
            if (racingLine == null)
                racingLine = FindFirstObjectByType<RacingLine>();

            if (racingLine != null && racingLine.Count >= 2)
            {
                racingLine.AdvanceProgress(ref cachedTrackWaypointIndex, position, 12f);
                Vector3 lineTangent = racingLine.GetTrackTangent(cachedTrackWaypointIndex);
                lineTangent.y = 0f;
                if (lineTangent.sqrMagnitude > 0.001f)
                {
                    tangent = lineTangent.normalized;
                    return true;
                }
            }

            if (checkpointManager == null)
                checkpointManager = FindFirstObjectByType<CheckpointManager>();

            if (checkpointManager != null && checkpointManager.CheckpointCount >= 2)
            {
                LapTracker tracker = GetComponent<LapTracker>();
                int expected = tracker != null ? tracker.ExpectedCheckpoint : 1;
                int previous = (expected - 1 + checkpointManager.CheckpointCount) % checkpointManager.CheckpointCount;
                Checkpoint previousCheckpoint = checkpointManager.GetCheckpoint(previous);
                Checkpoint nextCheckpoint = checkpointManager.GetCheckpoint(expected);
                if (previousCheckpoint != null && nextCheckpoint != null)
                {
                    Vector3 checkpointTangent = nextCheckpoint.transform.position - previousCheckpoint.transform.position;
                    checkpointTangent.y = 0f;
                    if (checkpointTangent.sqrMagnitude > 0.001f)
                    {
                        tangent = checkpointTangent.normalized;
                        return true;
                    }
                }
            }

            tangent = Vector3.zero;
            return false;
        }

        private void PlayStuntAirborneAudio(AudioClip clip, float volume)
        {
            if (clip == null)
                return;

            ResolveStuntAirborneAudioSource();
            if (stuntAirborneAudioSource == null)
                return;

            stuntAirborneAudioSource.Stop();
            stuntAirborneAudioSource.clip = clip;
            stuntAirborneAudioSource.volume = Mathf.Clamp01(volume);
            stuntAirborneAudioSource.Play();
        }

        private void StopStuntAirborneAudio()
        {
            if (stuntAirborneAudioSource != null && stuntAirborneAudioSource.isPlaying)
            {
                stuntAirborneAudioSource.Stop();
                stuntAirborneAudioSource.clip = null;
            }
        }

        private void SetWindTrailsActive(bool active)
        {
            if (active && !enableAirflowVfx)
            {
                airflowVfxPlaying = false;
                return;
            }

            ResolveAirflowTrails();
            if (!HasAirflowTrails())
            {
                airflowVfxPlaying = false;
                return;
            }

            for (int i = 0; i < stuntWindTrails.Length; i++)
            {
                if (stuntWindTrails[i] != null)
                {
                    if (active)
                    {
                        if (!stuntWindTrails[i].isPlaying)
                            stuntWindTrails[i].Play(true);
                    }
                    else if (stuntWindTrails[i].isPlaying)
                    {
                        // Let the existing particle lifetime fade the streaks out naturally.
                        stuntWindTrails[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }

            airflowVfxPlaying = active;
        }

        private void UpdateAirflowVfx(bool shouldBeActive)
        {
            if (!enableAirflowVfx || !shouldBeActive)
            {
                if (airflowVfxPlaying)
                    SetWindTrailsActive(false);
                return;
            }

            ResolveAirflowTrails();
            if (!HasAirflowTrails())
                return;

            if (!airflowVfxPlaying)
                SetWindTrailsActive(true);

            float speed = GetAirflowSpeed();
            float speed01 = Mathf.InverseLerp(
                airflowMinSpeed,
                Mathf.Max(airflowMinSpeed + 0.1f, airflowFullSpeed),
                speed);
            float emissionRate = Mathf.Lerp(
                airflowMinEmissionRate,
                Mathf.Max(airflowMinEmissionRate, airflowMaxEmissionRate),
                speed01);

            for (int i = 0; i < stuntWindTrails.Length; i++)
            {
                ParticleSystem trail = stuntWindTrails[i];
                if (trail == null)
                    continue;

                ParticleSystem.EmissionModule emission = trail.emission;
                emission.rateOverTime = emissionRate;
            }
        }

        private float GetAirflowSpeed()
        {
            if (body == null)
                return 0f;

            return Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        }

        public void SetNetworkPresentationOnly(bool presentationOnly)
        {
            networkPresentationOnly = presentationOnly;
            if (presentationOnly)
            {
                CancelStuntImmediate();
            }
            else
            {
                networkAirflowActive = false;
            }
        }

        public void ApplyNetworkStuntSnapshot(
            bool isActive,
            StuntType stuntType,
            float accumulatedAngle,
            int revolutions)
        {
            if (!networkPresentationOnly)
            {
                return;
            }

            completedRevolutions = Mathf.Max(0, revolutions);
            if (!isActive)
            {
                networkAirflowActive = false;
                SetWindTrailsActive(false);
                if (visualPivot != null)
                {
                    visualPivot.localRotation = neutralRotation;
                }
                return;
            }

            networkAirflowActive = true;
            SetWindTrailsActive(true);
            if (visualPivot == null)
            {
                return;
            }

            Quaternion stuntRotation = Quaternion.identity;
            switch (stuntType)
            {
                case StuntType.FlatSpin:
                    stuntRotation = Quaternion.AngleAxis(accumulatedAngle, Vector3.up);
                    break;
                case StuntType.BarrelRoll:
                    stuntRotation = BuildBarrelRollRotation(
                        accumulatedAngle,
                        Mathf.Sign(accumulatedAngle) == 0f ? 1 : Mathf.Sign(accumulatedAngle));
                    break;
                case StuntType.FrontFlip:
                    stuntRotation = Quaternion.AngleAxis(accumulatedAngle, Vector3.right);
                    break;
                case StuntType.BackFlip:
                    stuntRotation = Quaternion.AngleAxis(-accumulatedAngle, Vector3.right);
                    break;
            }

            visualPivot.localRotation = neutralRotation * stuntRotation;
        }

        private Quaternion BuildBarrelRollRotation(float angle, float direction)
        {
            Quaternion roll = Quaternion.AngleAxis(angle, Vector3.forward);
            if (corkscrewPitchAngle <= 0.01f)
            {
                return roll;
            }

            float pitch = Mathf.Sin(angle * Mathf.Deg2Rad) * corkscrewPitchAngle * direction;
            return roll * Quaternion.Euler(pitch, 0f, 0f);
        }

        public void CancelStuntImmediate()
        {
            networkAirflowActive = false;
            stuntLandingRecoveryActive = false;
            touchdownAwaitingConfirmation = false;
            touchdownStuntType = StuntType.None;
            StopStuntAirborneAudio();
            SetWindTrailsActive(false);
            if (visualPivot != null)
            {
                visualPivot.localRotation = neutralRotation;
            }
            state = StuntState.Grounded;
            activeStunt = StuntType.None;
            completedRevolutions = 0;
            totalAccumulatedAngle = 0f;
            rotationTravelDegrees = 0f;
            armedRamp = null;
            armedLegacyBarrelRollRamp = null;
        }

        private float GetAirRotationMultiplier()
        {
            if (!enableAirStuntControl || car == null)
                return 1f;

            ICarInputProvider provider = car.InputProviderSource as ICarInputProvider;
            if (provider == null || !provider.IsInputValid)
                return 1f;

            float inputMagnitude;
            switch (activeStunt)
            {
                case StuntType.FlatSpin:
                case StuntType.BarrelRoll:
                    inputMagnitude = Mathf.Abs(Mathf.Clamp(provider.Steering, -1f, 1f));
                    if (activeStunt == StuntType.FlatSpin && inputMagnitude > airInputDeadZone)
                    {
                        rotationDirection = provider.Steering < 0f ? -1 : 1;
                    }
                    break;
                case StuntType.FrontFlip:
                case StuntType.BackFlip:
                    inputMagnitude = Mathf.Abs(Mathf.Clamp(provider.Throttle - provider.Brake, -1f, 1f));
                    break;
                default:
                    inputMagnitude = 0f;
                    break;
            }

            return Mathf.Lerp(minimumAutoRotationFraction,
                Mathf.Max(minimumAutoRotationFraction, maximumInputRotationMultiplier),
                Mathf.InverseLerp(airInputDeadZone, 1f, inputMagnitude));
        }

        private void RegisterSuccessfulCombo()
        {
            if (Time.time <= comboExpiresAt)
                stuntComboCount++;
            else
                stuntComboCount = 1;

            comboExpiresAt = Time.time + comboWindow;
        }
    }
}
