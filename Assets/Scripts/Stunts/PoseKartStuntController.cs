using System;
using PoseKart.Input;
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
        [SerializeField, Min(0.2f)] private float barrelRollDuration = 0.75f;

        [Tooltip("Thời gian (giây) cho 1 vòng lộn Flip 360 độ.")]
        [SerializeField, Min(0.2f)] private float flipDuration = 0.80f;

        [Tooltip("Cửa sổ thời gian (giây) trước/sau khi rời dốc để phát hiện động tác Drift kích hoạt Flat Spin.")]
        [SerializeField, Range(0.1f, 0.6f)] private float flatSpinTakeoffWindow = 0.35f;

        [Tooltip("Độ nghiêng xoắn ốc (Corkscrew tilt angle) để tạo đường bay 3D sống động thay vì xoay cứng nhắc.")]
        [SerializeField, Range(0f, 15f)] private float corkscrewPitchAngle = 5.0f;

        [Header("Asphalt 8 Arcade Flight & Landing")]
        [Tooltip("Lực nâng khí động học triệt tiêu một phần trọng lực khi đang bay biểu diễn, tạo đường bay bổng chuẩn Asphalt 8.")]
        [SerializeField] private bool enableAirLoft = true;
        [SerializeField, Range(0f, 0.75f)] private float airLoftGravityCompensation = 0.20f;

        [Tooltip("Thời gian (giây) làm mượt đưa thân xe về vị trí thăng bằng phẳng khi tiếp đất (chống giật hình snap).")]
        [SerializeField, Range(0.05f, 0.3f)] private float landingAlignDuration = 0.14f;

        [Tooltip("Góc lệch tối đa (độ) so với phương thẳng đứng để được công nhận là Tiếp Đất Hoàn Hảo (Clean Landing).")]
        [SerializeField, Range(20f, 75f)] private float cleanLandingAngleThreshold = 45f;

        [Tooltip("Thời gian bay tối thiểu (giây) để cú nhảy thường (Normal Jump) được nhận thưởng.")]
        [SerializeField, Min(0.2f)] private float minimumAirTimeForJump = 0.55f;

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

        [Header("Presentation Feedback (VFX & Audio)")]
        [SerializeField] private ParticleSystem[] stuntWindTrails;
        [SerializeField] private ParticleSystem landingDustVfx;
        [SerializeField] private AudioClip takeoffAudioClip;
        [SerializeField] private AudioClip spinAudioClip;
        [SerializeField] private AudioClip revolutionAudioClip;
        [SerializeField] private AudioClip cleanLandingAudioClip;
        [SerializeField] private AudioClip crashLandingAudioClip;

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

        // Internal State
        private Quaternion neutralRotation = Quaternion.identity;
        private Quaternion baseRotation = Quaternion.identity;
        private Quaternion touchdownRotation = Quaternion.identity;
        private Vector3 previousPosition;
        private PoseKartStuntRamp armedRamp;
        private float rampArmDeadline;
        private float stuntStartTime;
        private float currentCycleDuration = 0.75f;
        private int rotationDirection = 1;
        private float lastDriftTime = float.NegativeInfinity;
        private float alignStartTime;
        private int lastAwardedRevolution = 0;
        private bool cleanLandingAwarded = false;
        private bool normalJumpAwarded = false;
        private Vector3 flightTrajectoryForward = Vector3.forward;

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

            if (visualPivot == null)
            {
                visualPivot = AutoResolveVisualPivot();
            }
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
            if (visualPivot != null && state == StuntState.Grounded)
            {
                baseRotation = neutralRotation;
            }
        }

        private void OnDisable()
        {
            CancelStuntImmediate();
        }

        /// <summary>
        /// Kích hoạt dốc biểu diễn khi xe chạm trigger dốc.
        /// </summary>
        public bool TryArmFromRamp(PoseKartStuntRamp ramp)
        {
            if (ramp == null || car == null || body == null || !car.ControlEnabled)
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
            rampArmDeadline = Time.time + ramp.ArmWindow;
            state = StuntState.RampApproach;

            // Áp dụng lực trợ phóng nếu dốc có cấu hình
            if (ramp.ApplyLaunchImpulse)
            {
                float fwdChange = forwardSpeed > 20f ? ramp.ForwardVelocityChange * 0.35f : ramp.ForwardVelocityChange;
                Vector3 impulse = (Vector3.up * ramp.UpwardVelocityChange) +
                                  (transform.forward * fwdChange);
                body.AddForce(impulse, ForceMode.VelocityChange);
                if (body.linearVelocity.magnitude > 24f)
                {
                    body.linearVelocity = body.linearVelocity.normalized * 24f;
                }
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
            if (ramp == null || car == null || body == null || !car.ControlEnabled)
            {
                return false;
            }

            Vector3 planarVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (Vector3.Dot(planarVel, transform.forward) < ramp.MinimumForwardSpeed)
            {
                return false;
            }

            rampArmDeadline = Time.time + ramp.ArmWindow;
            state = StuntState.RampApproach;

            if (ramp.ApplyLaunchImpulse)
            {
                float fwdSpd = Vector3.Dot(planarVel, transform.forward);
                float fwdChange = fwdSpd > 20f ? ramp.ForwardVelocityChange * 0.35f : ramp.ForwardVelocityChange;
                Vector3 impulse = (Vector3.up * ramp.UpwardVelocityChange) +
                                  (transform.forward * fwdChange);
                body.AddForce(impulse, ForceMode.VelocityChange);
                if (body.linearVelocity.magnitude > 24f)
                {
                    body.linearVelocity = body.linearVelocity.normalized * 24f;
                }
            }

            return true;
        }

        private void FixedUpdate()
        {
            // Aerodynamic Air Loft: Nâng bổng xe trong không trung chuẩn Asphalt 8
            if (state == StuntState.Airborne && enableAirLoft && body != null && !body.isKinematic)
            {
                if (activeStunt == StuntType.BarrelRoll || activeStunt == StuntType.FlatSpin || activeStunt == StuntType.FrontFlip || activeStunt == StuntType.BackFlip)
                {
                    float counterGravity = Physics.gravity.magnitude * airLoftGravityCompensation;
                    body.AddForce(Vector3.up * counterGravity, ForceMode.Acceleration);
                }
            }
        }

        private void Update()
        {
            // Bảo vệ an toàn: hủy biểu diễn nếu bị dịch chuyển tức thời hoặc mất điều khiển
            if (car == null || body == null || !car.ControlEnabled ||
                Vector3.Distance(transform.position, previousPosition) > 25f)
            {
                CancelStuntImmediate();
                previousPosition = transform.position;
                return;
            }
            previousPosition = transform.position;

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
                InitiateAirborne(null);
            }
        }

        private void UpdateRampApproach()
        {
            if (Time.time > rampArmDeadline)
            {
                state = StuntState.Grounded;
                armedRamp = null;
                return;
            }

            // Ngay khi bánh xe rời mặt đất -> Chuyển sang cất cánh biểu diễn
            if (!car.IsGrounded)
            {
                InitiateAirborne(armedRamp);
                armedRamp = null;
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

                switch (ramp.Type)
                {
                    case RampType.BarrelRollRamp:
                        activeStunt = StuntType.BarrelRoll;
                        if (ramp.RotationDuration <= 0.05f) currentCycleDuration = barrelRollDuration;
                        break;

                    case RampType.SpecialFlipRamp:
                        activeStunt = ramp.SpecialFlipType;
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
            }

            // Kích hoạt Particle Wind Trails
            SetWindTrailsActive(true);

            // Âm thanh cất cánh
            if (takeoffAudioClip != null)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(takeoffAudioClip, 0.85f);
            }
            else if (spinAudioClip != null && activeStunt != StuntType.NormalJump)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(spinAudioClip, 0.8f);
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

            // Cập nhật góc xoay biểu diễn
            if (activeStunt != StuntType.NormalJump && visualPivot != null)
            {
                float cycleProgress = airborneTime / currentCycleDuration;
                totalAccumulatedAngle = cycleProgress * 360f;
                int fullCycles = Mathf.FloorToInt(cycleProgress);

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
                float currentAngle = totalAccumulatedAngle * rotationDirection;
                Quaternion stuntRotation = Quaternion.identity;

                switch (activeStunt)
                {
                    case StuntType.FlatSpin:
                        stuntRotation = Quaternion.AngleAxis(currentAngle, Vector3.up);
                        break;

                    case StuntType.BarrelRoll:
                        Quaternion roll = Quaternion.AngleAxis(currentAngle, Vector3.forward);
                        Quaternion corkscrew = Quaternion.identity;
                        if (corkscrewPitchAngle > 0.01f)
                        {
                            float pitch = Mathf.Sin(totalAccumulatedAngle * Mathf.Deg2Rad) * corkscrewPitchAngle * rotationDirection;
                            corkscrew = Quaternion.Euler(pitch, 0f, 0f);
                        }
                        stuntRotation = roll * corkscrew;
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

            float finalReward = Mathf.Min(rewardFraction, maxRewardPerJump - totalRewardEarned);
            if (finalReward > 0f)
            {
                totalRewardEarned += finalReward;
                car.AddNitro(car.MaxNitroChargeAmount * finalReward);
            }
        }

        private void HandleTouchdown()
        {
            SetWindTrailsActive(false);

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
                    float jumpReward = Mathf.Min(normalJumpReward, maxRewardPerJump - totalRewardEarned);
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
                    float cleanReward = Mathf.Min(cleanLandingReward, maxRewardPerJump - totalRewardEarned);
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

                onCleanLanding.Invoke();
            }
            else
            {
                lastLandingQuality = LandingQuality.CrashLanding;
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
        }

        private void SetWindTrailsActive(bool active)
        {
            if (stuntWindTrails == null) return;
            for (int i = 0; i < stuntWindTrails.Length; i++)
            {
                if (stuntWindTrails[i] != null)
                {
                    if (active) stuntWindTrails[i].Play(true);
                    else stuntWindTrails[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        public void CancelStuntImmediate()
        {
            SetWindTrailsActive(false);
            if (visualPivot != null)
            {
                visualPivot.localRotation = neutralRotation;
            }
            state = StuntState.Grounded;
            activeStunt = StuntType.None;
            completedRevolutions = 0;
            totalAccumulatedAngle = 0f;
            armedRamp = null;
        }
    }
}
