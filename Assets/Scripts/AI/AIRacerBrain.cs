using System;
using System.Collections.Generic;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class AIRacerBrain : MonoBehaviour
    {
        public enum AIState
        {
            Racing,
            ApexAttack,
            SlipstreamHunting,
            Overtaking,
            DefensiveRacing,
            NitroPursuit,
            Combat,
            CollisionRecovery,
            EmergencyTrackRecovery,
            RampAssault,
            AirborneStunt
        }

        public enum OvertakeLane
        {
            None,
            Left,
            Right
        }

        [Header("References")]
        [SerializeField] private RacingLine racingLine;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CarController carController;
        [SerializeField] private AIInputProvider inputProvider;
        [SerializeField] private RaceParticipant participant;
        [SerializeField] private KartItemInventory inventory;
        [SerializeField] private RaceManager raceManager;

        [Header("Triple Demon Skill & Speed")]
        [Tooltip("Kỹ năng tổng thể của Demon AI (1.0 = Esports Pro).")]
        [SerializeField, Range(0.8f, 1f)] private float skill = 1f;
        [Tooltip("Hệ số tốc độ mục tiêu (1.15 = chạy tốc độ cao, bám sát giới hạn vật lý).")]
        [SerializeField, Range(0.9f, 1.3f)] private float targetSpeedMultiplier = 1.15f;
        [Tooltip("Mức độ thận trọng khi vào cua (0.65-0.75 = ôm cua gắt, hạn chế phanh thừa).")]
        [SerializeField, Range(0.4f, 1.2f)] private float cornerCaution = 0.72f;
        [Tooltip("Tốc độ phản hồi đánh lái góc cua.")]
        [SerializeField, Min(0.1f)] private float steeringResponse = 7.5f;
        [SerializeField, Range(0.6f, 1.5f)] private float lookAheadMultiplier = 1.05f;
        [SerializeField, Range(-1f, 1f)] private float avoidanceBias = 0f;

        [Header("Driving & Look-Ahead")]
        [SerializeField, Min(1f)] private float baseLookAheadDistance = 8.5f;
        [SerializeField, Min(0f)] private float speedLookAheadDistance = 0.65f;
        [SerializeField, Min(1f)] private float waypointReachDistance = 4.2f;
        [SerializeField, Min(1)] private int cornerPreviewSegments = 4;
        [SerializeField, Range(0.2f, 0.9f)] private float minimumCornerSpeedFactor = 0.55f;
        [SerializeField, Min(0f)] private float brakeSpeedMargin = 1.6f;

        [Header("Extreme Apex Cornering (In-Out-In)")]
        [Tooltip("Kích hoạt chiến thuật cua ngoài - trong - ngoài (Outside-Inside-Outside).")]
        [SerializeField] private bool enableApexCutting = true;
        [Tooltip("Khoảng cách cắt vào góc cua trong tâm (Apex).")]
        [SerializeField, Range(0.5f, 4.0f)] private float apexCutDistance = 2.4f;
        [Tooltip("Độ dạt ra ngoài trước khi vào cua (Corner Entry Outside Bias).")]
        [SerializeField, Range(0f, 2.5f)] private float cornerEntryOutsideBias = 1.2f;
        [Tooltip("Thời gian dự đoán đánh lái sớm trước khi chạm cua.")]
        [SerializeField, Range(0.1f, 0.8f)] private float apexSteerAnticipation = 0.35f;
        [Tooltip("Cho phép Drift để vừa ép góc cua gắt vừa nạp Nitro cực nhanh.")]
        [SerializeField] private bool enableDriftCornering = true;

        [Header("Track-Boundary Protection (4-Layer Safety)")]
        [Tooltip("Hệ thống bảo vệ biên đường đua 4 lớp ngăn chặn việc lao ra ngoài hoặc rơi vực.")]
        [SerializeField] private bool enableTrackBoundaryProtection = true;
        [Tooltip("Bề rộng hành lang an toàn quanh tim đường đua (m).")]
        [SerializeField, Range(1.5f, 8.0f)] private float boundarySafetyCorridor = 5.5f;
        [Tooltip("Khoảng cách quét tia cảm biến phát hiện vách tường/vực thẳm phía trước.")]
        [SerializeField, Min(4f)] private float boundaryProbeDistance = 12f;
        [Tooltip("Bán kính hình cầu kiểm tra mặt đất hợp lệ.")]
        [SerializeField, Min(0.2f)] private float boundaryProbeRadius = 0.6f;
        [Tooltip("Độ nhạy phanh khẩn cấp khi sắp rời khỏi đường đua.")]
        [SerializeField, Range(0.3f, 1.2f)] private float emergencyBrakeSensitivity = 0.70f;
        [Tooltip("Độ sâu chênh lệch mặt đất coi là vực thẳm.")]
        [SerializeField, Min(1f)] private float cliffDropThreshold = 3.5f;

        [Header("Ruthless Nitro Management")]
        [Tooltip("Tự động bấm đúng nhịp thanh Cyan để kích hoạt Perfect Nitro (185 km/h).")]
        [SerializeField] private bool enablePerfectNitro = true;
        [Tooltip("Tự động nâng cấp chuỗi Double -> Triple Nitro / Shockwave khi đuổi bắt hoặc trên đường thẳng.")]
        [SerializeField] private bool enableTripleNitro = true;
        [Tooltip("Ngưỡng Nitro tối thiểu để kích hoạt (Demon AI không tiếc Nitro).")]
        [SerializeField, Range(0.1f, 0.6f)] private float nitroTacticalThreshold = 0.18f;
        [Tooltip("Cài đặt tùy chọn: Demon AI được hồi Nitro thụ động (chỉ áp dụng cho AI, không ảnh hưởng Player).")]
        [SerializeField] private bool unlimitedDemonNitro = false;
        [SerializeField, Range(0.01f, 0.2f)] private float demonPassiveNitroRate = 0.05f;

        [Header("Predictive Slipstream & Overtaking")]
        [Tooltip("Chủ động tìm kiếm và hút gió đối thủ (Player lẫn AI khác) rồi bắn vượt.")]
        [SerializeField] private bool enableSlipstreamAndOvertake = true;
        [SerializeField, Min(2f)] private float slipstreamMinDistance = 3.0f;
        [SerializeField, Min(5f)] private float slipstreamMaxDistance = 20.0f;
        [SerializeField, Range(10f, 40f)] private float slipstreamAngleTolerance = 24.0f;
        [SerializeField, Range(1.05f, 1.35f)] private float slipstreamSpeedBonus = 1.18f;
        [Tooltip("Thời gian dự đoán vị trí đối thủ phía trước (0.5 - 1.0 giây).")]
        [SerializeField, Range(0.3f, 1.5f)] private float overtakePredictionHorizon = 0.75f;
        [Tooltip("Khoảng cách tiếp cận để bắt đầu bẻ lái tách làn bắn vượt (m).")]
        [SerializeField, Min(3f)] private float overtakeTriggerDistance = 7.5f;
        [Tooltip("Thời gian cam kết giữ làn vượt (tránh rung lắc tay lái liên tục).")]
        [SerializeField, Min(0.4f)] private float overtakeCommitmentDuration = 1.1f;
        [Tooltip("Độ lệch ngang khi vượt mặt.")]
        [SerializeField, Range(1.0f, 3.5f)] private float overtakeLateralOffset = 1.8f;
        [Tooltip("Cho phép va chạm thân xe có kiểm soát khi đua song song.")]
        [SerializeField] private bool doorToDoorContactAllowed = true;

        [Header("Demon Combat AI")]
        [Tooltip("Khai thác toàn diện Tên lửa tầm nhiệt, Vỏ chuối, Khiên phản đòn và Mực.")]
        [SerializeField] private bool enableTacticalCombat = true;
        [SerializeField, Min(5f)] private float missileDefenseRange = 34.0f;
        [SerializeField, Min(2f)] private float bananaRearAmbushRange = 9.0f;
        [Tooltip("Thời gian phản xạ bật khiên siêu nhanh của Demon (0.08 - 0.15s).")]
        [SerializeField, Range(0.05f, 0.3f)] private float shieldReactionDelay = 0.10f;

        [Header("Dynamic Competitive Pressure (Rubberbanding)")]
        [Tooltip("Duy trì áp lực cạnh tranh sát sao mà không gian lận tốc độ phi lý.")]
        [SerializeField] private bool enableCompetitivePressure = true;
        [SerializeField, Range(1f, 1.25f)] private float chaseSpeedBonus = 1.16f;
        [SerializeField, Range(1f, 1.2f)] private float defensiveSpeedFactor = 1.08f;

        [Header("Advanced Recovery")]
        [SerializeField, Min(0.1f)] private float stuckSpeedThreshold = 1.2f;
        [SerializeField, Min(0.5f)] private float reverseAfterSeconds = 2.0f;
        [SerializeField, Min(0.25f)] private float reverseDuration = 1.2f;
        [SerializeField, Min(2f)] private float hardResetAfterSeconds = 4.5f;
        [SerializeField, Range(30f, 90f)] private float spinRecoveryAngleThreshold = 55f;

        [Header("Demon Debug Inspector (Read Only)")]
        [SerializeField] private AIState aiState = AIState.Racing;
        [SerializeField] private CarController targetOpponent;
        [SerializeField] private OvertakeLane overtakeLane = OvertakeLane.None;
        [SerializeField] private float cornerSeverity;
        [SerializeField] private float desiredSpeed;
        [SerializeField] private CarController slipstreamTarget;
        [SerializeField] private bool slipstreamActive;
        [SerializeField] private string nitroDecision = "Idle";
        [SerializeField] private string combatAction = "Standby";
        [SerializeField] private float trackBoundaryRisk;
        [SerializeField] private string recoveryState = "Normal";
        [SerializeField] private int actualRacePosition = 1;

        // Internal Runtime State
        private int nextWaypointIndex;
        private float smoothedSteering;
        private float stuckTimer;
        private float wrongWayTimer;
        private float reverseTimerRemaining;
        private bool isReversing;
        private float previousCornerSeverity;
        private float overtakeCommitTimer;
        private float pendingShieldTime = -1f;
        private float nextCombatScanTime;
        private float nextOpponentScanTime;

        // Ramp & Aerial Awareness Cache
        private PoseKart.Stunts.PoseKartStuntController stuntController;
        private PoseKart.Stunts.PoseKartStuntRamp[] cachedStuntRamps;
        private PoseKart.Stunts.PoseKartBarrelRollRamp[] cachedBarrelRollRamps;

        // Cached Racers (Performance Optimization)
        private readonly List<RaceParticipant> cachedOpponents = new List<RaceParticipant>(4);
        private RaceParticipant cachedPlayerParticipant;

        public bool IsConfigured =>
            racingLine != null
            && racingLine.IsConfigured
            && body != null
            && carController != null
            && inputProvider != null;

        public int NextWaypointIndex => nextWaypointIndex;
        public AIState CurrentState => aiState;
        public int RacePosition => participant != null ? participant.RacePosition : actualRacePosition;

        public void Configure(
            RacingLine line,
            Rigidbody racerBody,
            CarController controller,
            AIInputProvider provider,
            float racerSkill,
            float speedMultiplier,
            float caution,
            float steerResponse,
            float lookAhead,
            float sideBias)
        {
            racingLine = line;
            body = racerBody;
            carController = controller;
            inputProvider = provider;
            skill = Mathf.Clamp(racerSkill, 0.8f, 1f);
            targetSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.9f, 1.3f);
            cornerCaution = Mathf.Clamp(caution, 0.4f, 1.2f);
            steeringResponse = Mathf.Max(0.1f, steerResponse);
            lookAheadMultiplier = Mathf.Clamp(lookAhead, 0.6f, 1.5f);
            avoidanceBias = Mathf.Clamp(sideBias, -1f, 1f);
        }

        public void ApplyDemonPreset(float seedOffset = 0f)
        {
            skill = 1.0f;
            targetSpeedMultiplier = 1.15f;
            cornerCaution = 0.72f;
            steeringResponse = 7.5f;
            lookAheadMultiplier = 1.05f;
            baseLookAheadDistance = 8.5f;
            speedLookAheadDistance = 0.65f;
            waypointReachDistance = 4.2f;
            cornerPreviewSegments = 4;
            brakeSpeedMargin = 1.6f;

            enableApexCutting = true;
            apexCutDistance = 2.4f;
            cornerEntryOutsideBias = 1.2f;
            apexSteerAnticipation = 0.35f;
            enableDriftCornering = true;

            enableTrackBoundaryProtection = true;
            boundarySafetyCorridor = 5.5f;
            boundaryProbeDistance = 12.0f;
            boundaryProbeRadius = 0.6f;
            emergencyBrakeSensitivity = 0.70f;
            cliffDropThreshold = 3.5f;

            enablePerfectNitro = true;
            enableTripleNitro = true;
            nitroTacticalThreshold = 0.18f;
            unlimitedDemonNitro = false;

            enableSlipstreamAndOvertake = true;
            slipstreamMinDistance = 3.0f;
            slipstreamMaxDistance = 20.0f;
            slipstreamAngleTolerance = 24.0f;
            slipstreamSpeedBonus = 1.18f;
            overtakePredictionHorizon = 0.75f;
            overtakeTriggerDistance = 7.5f;
            overtakeCommitmentDuration = 1.1f;
            overtakeLateralOffset = 1.8f;
            doorToDoorContactAllowed = true;

            enableTacticalCombat = true;
            missileDefenseRange = 34.0f;
            bananaRearAmbushRange = 9.0f;
            shieldReactionDelay = Mathf.Clamp(0.10f + seedOffset * 0.03f, 0.07f, 0.15f);

            enableCompetitivePressure = true;
            chaseSpeedBonus = 1.16f;
            defensiveSpeedFactor = 1.08f;

            stuckSpeedThreshold = 1.2f;
            reverseAfterSeconds = 2.0f;
            reverseDuration = 1.2f;
            hardResetAfterSeconds = 4.5f;
            spinRecoveryAngleThreshold = 55f;

            avoidanceBias = Mathf.Clamp(seedOffset * 0.25f, -0.5f, 0.5f);
        }

        private void Awake()
        {
            participant = GetComponent<RaceParticipant>();
            if ((participant != null && participant.IsPlayer) ||
                gameObject.name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (carController == null) carController = GetComponent<CarController>();
            if (inputProvider == null) inputProvider = GetComponent<AIInputProvider>();
            if (participant == null) participant = GetComponent<RaceParticipant>();
            if (inventory == null) inventory = GetComponent<KartItemInventory>();
            if (stuntController == null) stuntController = GetComponent<PoseKart.Stunts.PoseKartStuntController>();
            if (racingLine == null) racingLine = FindFirstObjectByType<RacingLine>();
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();

            if (cachedStuntRamps == null || cachedStuntRamps.Length == 0)
                cachedStuntRamps = FindObjectsByType<PoseKart.Stunts.PoseKartStuntRamp>(FindObjectsSortMode.None);
            if (cachedBarrelRollRamps == null || cachedBarrelRollRamps.Length == 0)
                cachedBarrelRollRamps = FindObjectsByType<PoseKart.Stunts.PoseKartBarrelRollRamp>(FindObjectsSortMode.None);
        }

        private void Start()
        {
            ResolveReferences();
            if (!IsConfigured)
            {
                Debug.LogError($"{nameof(AIRacerBrain)} trên {name} chưa được cấu hình đầy đủ.", this);
                enabled = false;
                return;
            }

            // Tự động bảo đảm áp dụng cấu hình Demon AI hoàn chỉnh ngay tại runtime
            if (skill < 0.99f || steeringResponse < 6f || boundarySafetyCorridor <= 0.1f)
            {
                float microSeed = 0f;
                if (participant != null)
                {
                    if (participant.RacerName.IndexOf("Sunset", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = -0.2f;
                    else if (participant.RacerName.IndexOf("Azure", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = 0.0f;
                    else if (participant.RacerName.IndexOf("Coral", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = 0.2f;
                }
                ApplyDemonPreset(microSeed);
            }

            nextWaypointIndex = racingLine.FindStartingWaypoint(body.position);
            RefreshOpponentsCache();
        }

        private void RefreshOpponentsCache()
        {
            cachedOpponents.Clear();
            cachedPlayerParticipant = null;

            if (raceManager != null && raceManager.Racers != null && raceManager.Racers.Length > 0)
            {
                for (int i = 0; i < raceManager.Racers.Length; i++)
                {
                    RaceParticipant p = raceManager.Racers[i];
                    if (p == null || p == participant || p.CarController == carController) continue;

                    cachedOpponents.Add(p);
                    if (p.IsPlayer)
                    {
                        cachedPlayerParticipant = p;
                    }
                }
            }
            else
            {
                RaceParticipant[] all = FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    RaceParticipant p = all[i];
                    if (p == null || p == participant || p.CarController == carController) continue;

                    cachedOpponents.Add(p);
                    if (p.IsPlayer)
                    {
                        cachedPlayerParticipant = p;
                    }
                }
            }
        }

        private void Update()
        {
            if (unlimitedDemonNitro && carController != null && carController.ControlEnabled)
            {
                if (carController.NitroFill01 < 1f)
                {
                    carController.AddNitro(demonPassiveNitroRate * Time.deltaTime * carController.MaxNitroChargeAmount);
                }
            }

            // Tactical Combat Scanning (20Hz)
            if (Time.time >= nextCombatScanTime)
            {
                nextCombatScanTime = Time.time + 0.05f;
                UpdateCombatTactics();
            }

            // Delayed Shield Execution
            if (pendingShieldTime > 0f && Time.time >= pendingShieldTime)
            {
                pendingShieldTime = -1f;
                if (inventory != null && inventory.HasItem && inventory.CurrentItem == KartItemPickup.ItemType.Shield)
                {
                    combatAction = "Shield: Missile Deflected!";
                    inventory.UseItem();
                }
            }
        }

        private void FixedUpdate()
        {
            if (inputProvider == null) return;
            inputProvider.SetDrift(false);

            if (carController == null || !carController.ControlEnabled)
            {
                inputProvider.SetInput(0f, 0f, 0f);
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                reverseTimerRemaining = 0f;
                isReversing = false;
                aiState = AIState.Racing;
                recoveryState = "Control Disabled";
                return;
            }

            // Định kỳ làm mới danh sách đối thủ (mỗi 2.0 giây)
            if (Time.time >= nextOpponentScanTime)
            {
                nextOpponentScanTime = Time.time + 2.0f;
                RefreshOpponentsCache();
            }

            // Đồng bộ thứ hạng hiện tại
            actualRacePosition = participant != null ? participant.RacePosition : 1;

            // 0. CHECK RAMP STATUS
            bool onRamp = IsOnOrApproachingRamp(out bool isRampAirborne);

            // Nếu đang bay trên không do dốc: Giữ thẳng lái và 100% ga để hoàn thành quỹ đạo mượt mà
            if (isRampAirborne && !carController.IsGrounded)
            {
                smoothedSteering = 0f;
                inputProvider.SetInput(0f, 1f, 0f);
                inputProvider.SetDrift(false);
                aiState = AIState.AirborneStunt;
                recoveryState = "Airborne Stunt Flight";
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                return;
            }

            // 1. TIẾN TRIỂN WAYPOINTS TRÊN RACING LINE
            racingLine.AdvanceProgress(ref nextWaypointIndex, body.position, waypointReachDistance);
            float speed = Mathf.Abs(carController.ForwardSpeed);
            float lookAhead = (baseLookAheadDistance + speed * speedLookAheadDistance) * lookAheadMultiplier;
            Vector3 baseTarget = racingLine.GetLookAheadPoint(nextWaypointIndex, body.position, lookAhead);
            cornerSeverity = racingLine.GetCornerSeverity(nextWaypointIndex, cornerPreviewSegments);
            Vector3 trackTangent = racingLine.GetTrackTangent(nextWaypointIndex);

            // 2. PHÁT HIỆN & XỬ LÝ LÁI NGƯỢC CHIỀU (WRONG-WAY RECOVERY)
            float headingDot = Vector3.Dot(transform.forward, trackTangent);
            bool isWrongWay = headingDot < -0.22f; // Đầu xe đang hướng ngược hơn 103 độ so với chiều đường đua
            if (isWrongWay && !onRamp)
            {
                wrongWayTimer += Time.fixedDeltaTime;
                if (wrongWayTimer >= 2.4f)
                {
                    HardRecoverToRacingLine();
                    wrongWayTimer = 0f;
                    return;
                }

                float turnSign = Mathf.Sign(Vector3.Cross(transform.forward, trackTangent).y);
                if (Mathf.Abs(turnSign) < 0.01f) turnSign = 1f;
                smoothedSteering = turnSign;

                if (carController.ForwardSpeed > 3.0f)
                {
                    // Đang lao ngược chiều: phanh gấp để triệt tiêu đà ngược
                    inputProvider.SetInput(smoothedSteering, 0f, 1f);
                }
                else
                {
                    // Tốc độ thấp: vần hết lái và nhấp ga 0.45 để quay xe đúng chiều đua
                    inputProvider.SetInput(smoothedSteering, 0.45f, 0f);
                }

                inputProvider.SetDrift(false);
                aiState = AIState.EmergencyTrackRecovery;
                recoveryState = $"Wrong Way Turnaround ({wrongWayTimer:0.0}s)";
                return;
            }
            else
            {
                wrongWayTimer = 0f;
            }

            // 3. TÍNH TOÁN ĐƯỜNG ĐUA APEX TỐI ƯU (OUTSIDE-INSIDE-OUTSIDE)
            Vector3 target = CalculateApexTrajectory(baseTarget, cornerSeverity);

            // 4. TÍNH GÓC LÁI CƠ SỞ ĐẾN ĐIỂM MỤC TIÊU
            Vector3 localTarget = transform.InverseTransformPoint(target);
            float desiredSteering;
            if (localTarget.z > 0.1f)
            {
                float targetAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
                desiredSteering = Mathf.Clamp(targetAngle / 30f, -1f, 1f);
            }
            else
            {
                desiredSteering = localTarget.x >= 0f ? 1f : -1f;
            }

            // 5. TÍNH VẬN TỐC MỤC TIÊU CƠ SỞ
            float cautionSeverity = Mathf.Clamp01(cornerSeverity * cornerCaution);
            float skillFactor = Mathf.Lerp(0.96f, 1.10f, skill);
            float cornerFactor = Mathf.Lerp(1f, minimumCornerSpeedFactor, cautionSeverity);
            desiredSpeed = carController.MaxForwardSpeed
                           * targetSpeedMultiplier
                           * skillFactor
                           * cornerFactor
                           * racingLine.GetRecommendedSpeedFactor(nextWaypointIndex);

            // 6. ÁP LỰC ĐUA CẠNH TRANH (RUBBERBANDING VÀ PHÒNG THỦ)
            UpdateCompetitivePressure(ref desiredSpeed);

            // 7. NÚP GIÓ & BẮN VƯỢT (SLIPSTREAM & SLINGSHOT OVERTAKING)
            if (!onRamp)
            {
                UpdateSlipstreamAndOvertake(ref desiredSteering, ref desiredSpeed);
            }

            // 8. TRÁNH CHƯỚNG NGẠI VẬT & ĐUA SÁT THÂN XE
            if (!onRamp)
            {
                ApplySmartObstacleAwareness(ref desiredSteering, ref desiredSpeed);
            }

            // 9. HỆ THỐNG BẢO VỆ BIÊN ĐƯỜNG ĐUA
            bool boundaryEmergency = ApplyTrackBoundaryProtection(ref desiredSteering, ref desiredSpeed);

            // 10. HỒI PHỤC VĂNG XOAY XE DO VA CHẠM
            bool spinRecovering = ApplyCollisionSpinRecovery(ref desiredSteering, ref desiredSpeed);

            // 11. ĐIỀU CHỈNH KHI TIẾP CẬN DỐC PHÓNG
            if (onRamp)
            {
                boundaryEmergency = false;
                trackBoundaryRisk = 0f;
                spinRecovering = false;
                desiredSpeed = Mathf.Min(desiredSpeed, carController.MaxForwardSpeed * 0.95f);
                desiredSteering = Mathf.Clamp(desiredSteering, -0.2f, 0.2f); // Căn thẳng tim dốc
            }

            // 12. LÀM MƯỢT TAY LÁI
            smoothedSteering = Mathf.MoveTowards(
                smoothedSteering,
                desiredSteering,
                steeringResponse * Time.fixedDeltaTime);

            // 13. XỬ LÝ KẸT XE & PHỤC HỒI LÙI
            if (UpdateStuckRecovery(localTarget, speed, isRampAirborne))
            {
                return;
            }

            // 14. ĐIỀU PHỐI GA & PHANH
            float throttle = 1f;
            float brake = 0f;

            if (onRamp)
            {
                throttle = 1f;
                brake = 0f;
            }
            else if (boundaryEmergency)
            {
                throttle = 0.25f;
                brake = Mathf.Clamp01(trackBoundaryRisk * emergencyBrakeSensitivity);
            }
            else if (spinRecovering)
            {
                throttle = 0.35f;
                brake = 0f;
            }
            else if (carController.IsBoosting || carController.IsNitroBoosting)
            {
                throttle = 1f;
                brake = 0f;
            }
            else
            {
                if (speed < desiredSpeed - brakeSpeedMargin)
                {
                    throttle = 1f;
                    brake = 0f;
                }
                else if (speed < desiredSpeed)
                {
                    throttle = 0.70f;
                    brake = 0f;
                }
                else if (speed > desiredSpeed + brakeSpeedMargin + 2f)
                {
                    throttle = 0f;
                    brake = Mathf.InverseLerp(desiredSpeed + brakeSpeedMargin, desiredSpeed + 5f, speed);
                }
                else
                {
                    throttle = 0.35f;
                    brake = 0f;
                }
            }

            inputProvider.SetInput(smoothedSteering, throttle, brake);

            // 15. QUẢN LÝ NITRO DEMON RUTHLESS
            UpdateRuthlessNitro(speed, cornerSeverity, smoothedSteering, throttle, brake, onRamp);

            // 16. DRIFT ÔM CUA ĐẲNG CẤP & NẠP NITRO
            bool wantsDrift = enableDriftCornering &&
                              carController.IsGrounded &&
                              !onRamp &&
                              speed > 10.5f &&
                              cautionSeverity > 0.38f &&
                              Mathf.Abs(smoothedSteering) > 0.32f &&
                              brake <= 0.01f &&
                              !isReversing;

            inputProvider.SetDrift(wantsDrift);

            // Cập nhật State debug Inspector
            UpdateDebugAIState(boundaryEmergency, spinRecovering, wantsDrift, onRamp, isRampAirborne);
        }

        #region Ramp & Aerial Awareness
        /// <summary>
        /// Phát hiện xe đang ở trên dốc phóng, chuẩn bị bay, hoặc đang lơ lửng trên không.
        /// </summary>
        private bool IsOnOrApproachingRamp(out bool isRampAirborne)
        {
            isRampAirborne = false;

            // 1. Kiểm tra trạng thái StuntController
            if (stuntController != null)
            {
                if (stuntController.State == PoseKart.Stunts.StuntState.RampApproach ||
                    stuntController.IsStuntActive)
                {
                    isRampAirborne = stuntController.State == PoseKart.Stunts.StuntState.Airborne;
                    return true;
                }
            }

            // 2. Xe đang bay trên không (Airborne)
            if (carController != null && !carController.IsGrounded)
            {
                isRampAirborne = true;
                return true;
            }

            Vector3 pos = body.position;
            Vector3 fwd = transform.forward;

            // 3. Quét cự ly gần với các dốc biểu diễn trong Scene
            if (cachedStuntRamps != null)
            {
                for (int i = 0; i < cachedStuntRamps.Length; i++)
                {
                    PoseKart.Stunts.PoseKartStuntRamp r = cachedStuntRamps[i];
                    if (r == null) continue;

                    Vector3 delta = r.transform.position - pos;
                    float distSqr = delta.sqrMagnitude;
                    if (distSqr < 625f) // Trong bán kính 25m
                    {
                        if (distSqr < 100f || Vector3.Dot(delta.normalized, fwd) > 0.25f)
                        {
                            return true;
                        }
                    }
                }
            }

            if (cachedBarrelRollRamps != null)
            {
                for (int i = 0; i < cachedBarrelRollRamps.Length; i++)
                {
                    PoseKart.Stunts.PoseKartBarrelRollRamp r = cachedBarrelRollRamps[i];
                    if (r == null) continue;

                    Vector3 delta = r.transform.position - pos;
                    float distSqr = delta.sqrMagnitude;
                    if (distSqr < 625f)
                    {
                        if (distSqr < 100f || Vector3.Dot(delta.normalized, fwd) > 0.25f)
                        {
                            return true;
                        }
                    }
                }
            }

            // 4. Quét mặt đất dưới gầm xe xem có đang đứng trên mặt dốc phóng không
            if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit groundHit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsRampCollider(groundHit.collider))
                {
                    return true;
                }
            }

            // 5. Quét trước mũi xe (khoảng 12m)
            Vector3 fwdOrigin = pos + fwd * 1.5f + Vector3.up * 0.4f;
            if (Physics.SphereCast(fwdOrigin, 0.75f, fwd, out RaycastHit fwdHit, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsRampCollider(fwdHit.collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRampCollider(Collider col)
        {
            if (col == null) return false;
            if (col.GetComponentInParent<PoseKart.Stunts.PoseKartStuntRamp>() != null) return true;
            if (col.GetComponentInParent<PoseKart.Stunts.PoseKartBarrelRollRamp>() != null) return true;

            string colName = col.name;
            if (colName.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                colName.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            Transform parent = col.transform.parent;
            while (parent != null)
            {
                string pName = parent.name;
                if (pName.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pName.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                if (parent.GetComponent<PoseKart.Stunts.PoseKartStuntRamp>() != null ||
                    parent.GetComponent<PoseKart.Stunts.PoseKartBarrelRollRamp>() != null)
                {
                    return true;
                }
                parent = parent.parent;
            }

            return false;
        }
        #endregion

        #region Extreme Apex Cornering (In-Out-In)
        private Vector3 CalculateApexTrajectory(Vector3 baseTarget, float severity)
        {
            if (!enableApexCutting || severity <= 0.08f || racingLine.Count < 4)
            {
                return baseTarget;
            }

            // Khi tiếp cận hoặc đang trên dốc phóng, bám thẳng tim đường
            if (IsOnOrApproachingRamp(out _))
            {
                return baseTarget;
            }

            int prevIdx = (nextWaypointIndex - 1 + racingLine.Count) % racingLine.Count;
            int nextIdx = (nextWaypointIndex + 1) % racingLine.Count;

            Vector3 pPrev = racingLine.GetPosition(prevIdx);
            Vector3 pCurr = racingLine.GetPosition(nextWaypointIndex);
            Vector3 pNext = racingLine.GetPosition(nextIdx);

            Vector3 vIn = (pCurr - pPrev).normalized;
            Vector3 vOut = (pNext - pCurr).normalized;
            Vector3 turnCross = Vector3.Cross(vIn, vOut);
            float turnSign = Mathf.Sign(turnCross.y);
            Vector3 trackRight = Vector3.Cross(Vector3.up, vIn).normalized;

            float distToWaypoint = Vector3.Distance(body.position, pCurr);
            float speed = carController != null ? Mathf.Max(0f, carController.ForwardSpeed) : 0f;
            float anticipationThreshold = 10f + speed * apexSteerAnticipation;
            float apexFactor;

            if (distToWaypoint > anticipationThreshold)
            {
                apexFactor = turnSign * cornerEntryOutsideBias * Mathf.Clamp01(severity * 1.2f);
            }
            else
            {
                apexFactor = -turnSign * apexCutDistance * Mathf.Clamp01(severity * 1.5f);
            }

            Vector3 candidateTarget = baseTarget + trackRight * apexFactor;

            if (enableTrackBoundaryProtection)
            {
                float corridor = boundarySafetyCorridor * 0.85f;
                Vector3 offset = candidateTarget - pCurr;
                if (Vector3.Dot(offset, trackRight) > corridor)
                {
                    candidateTarget = pCurr + trackRight * corridor;
                }
                else if (Vector3.Dot(offset, trackRight) < -corridor)
                {
                    candidateTarget = pCurr - trackRight * corridor;
                }
            }

            return candidateTarget;
        }
        #endregion

        #region Track-Boundary Protection (4 Layers)
        private bool ApplyTrackBoundaryProtection(ref float desiredSteering, ref float targetSpeed)
        {
            if (!enableTrackBoundaryProtection)
            {
                trackBoundaryRisk = 0f;
                return false;
            }

            if (IsOnOrApproachingRamp(out _))
            {
                trackBoundaryRisk = 0f;
                return false;
            }

            Vector3 pos = body.position;
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            Vector3 up = transform.up;

            int nearestIdx = racingLine.FindClosestWaypoint(pos);
            Vector3 trackPos = racingLine.GetPosition(nearestIdx);
            Vector3 trackTangent = racingLine.GetTrackTangent(nearestIdx);
            Vector3 trackRight = Vector3.Cross(Vector3.up, trackTangent).normalized;

            // 1. Kiểm tra độ lệch khỏi hành lang an toàn của track
            Vector3 fromTrack = pos - trackPos;
            float lateralOffset = Vector3.Dot(fromTrack, trackRight);
            float normalizedCorridorOffset = Mathf.Abs(lateralOffset) / boundarySafetyCorridor;

            float risk = 0f;
            if (normalizedCorridorOffset > 1.0f)
            {
                risk = Mathf.Clamp01((normalizedCorridorOffset - 1.0f) * 1.2f);
                float steerCorrection = -Mathf.Sign(lateralOffset) * Mathf.Lerp(0.25f, 0.75f, risk);
                desiredSteering = Mathf.Clamp(desiredSteering + steerCorrection, -1f, 1f);
            }

            // 2. Quét trước quỹ đạo để phát hiện rơi vực hoặc húc tường
            float currentSpeed = Mathf.Max(5f, carController.ForwardSpeed);
            float probeDist = Mathf.Min(boundaryProbeDistance, currentSpeed * 0.65f);
            Vector3 probeCenter = pos + fwd * probeDist + up * 0.8f;

            int lookAheadWp = (nearestIdx + 3) % racingLine.Count;
            float trackElevationChange = pos.y - racingLine.GetPosition(lookAheadWp).y;

            if (Physics.Raycast(probeCenter, -Vector3.up, out RaycastHit groundHit, cliffDropThreshold + 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                float drop = probeCenter.y - groundHit.point.y;
                if (drop > cliffDropThreshold + 1.0f && trackElevationChange < 2.0f)
                {
                    risk = Mathf.Max(risk, 0.85f);
                    float pullToTrack = Vector3.Dot((trackPos - pos).normalized, right);
                    desiredSteering = Mathf.Clamp(desiredSteering + Mathf.Sign(pullToTrack) * 0.65f, -1f, 1f);
                    targetSpeed *= 0.65f;
                }
            }
            else
            {
                if (trackElevationChange < 2.5f)
                {
                    risk = 0.95f;
                    float pullToTrack = Vector3.Dot((trackPos - pos).normalized, right);
                    desiredSteering = Mathf.Clamp(desiredSteering + Mathf.Sign(pullToTrack) * 0.8f, -1f, 1f);
                    targetSpeed *= 0.5f;
                }
            }

            // Quét râu hai bên phát hiện chướng ngại vật
            Vector3 leftWhisker = (fwd - right * 0.45f).normalized;
            Vector3 rightWhisker = (fwd + right * 0.45f).normalized;
            float whiskerRange = probeDist * 0.6f;

            if (Physics.SphereCast(pos + up * 0.4f, boundaryProbeRadius, leftWhisker, out RaycastHit leftHit, whiskerRange, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsStaticObstacle(leftHit.collider))
                {
                    desiredSteering = Mathf.Clamp(desiredSteering + 0.35f, -1f, 1f);
                    risk = Mathf.Max(risk, 1f - leftHit.distance / whiskerRange);
                }
            }

            if (Physics.SphereCast(pos + up * 0.4f, boundaryProbeRadius, rightWhisker, out RaycastHit rightHit, whiskerRange, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsStaticObstacle(rightHit.collider))
                {
                    desiredSteering = Mathf.Clamp(desiredSteering - 0.35f, -1f, 1f);
                    risk = Mathf.Max(risk, 1f - rightHit.distance / whiskerRange);
                }
            }

            trackBoundaryRisk = risk;
            return risk >= 0.80f;
        }

        private static bool IsStaticObstacle(Collider col)
        {
            if (col == null) return false;
            if (IsRampCollider(col)) return false;
            CarController otherCar = col.GetComponentInParent<CarController>();
            return otherCar == null;
        }
        #endregion

        #region Collision Spin Recovery
        private bool ApplyCollisionSpinRecovery(ref float desiredSteering, ref float targetSpeed)
        {
            if (carController == null || racingLine == null) return false;

            int nearestIdx = racingLine.FindClosestWaypoint(body.position);
            Vector3 trackTangent = racingLine.GetTrackTangent(nearestIdx);

            Vector3 carFwd = transform.forward;
            float headingAngle = Vector3.Angle(carFwd, trackTangent);

            if (headingAngle > spinRecoveryAngleThreshold)
            {
                recoveryState = "Spin Counter-Steer";
                float turnSign = Mathf.Sign(Vector3.Cross(carFwd, trackTangent).y);
                if (Mathf.Abs(turnSign) < 0.01f) turnSign = 1f;
                desiredSteering = turnSign * 1.0f;
                targetSpeed *= 0.75f;
                return true;
            }

            return false;
        }
        #endregion

        #region Ruthless Nitro Management
        private void UpdateRuthlessNitro(float speed, float severity, float steering, float throttle, float brake, bool onRamp)
        {
            if (carController == null || !carController.ControlEnabled || (!carController.IsGrounded && !onRamp) || isReversing)
            {
                nitroDecision = "Locked";
                return;
            }

            // Khi tiếp cận hoặc ở trên dốc, không xả Nitro để tránh phóng quá tốc độ cho phép
            if (onRamp)
            {
                nitroDecision = "Holding for Ramp";
                return;
            }

            // 1. PERFECT NITRO TIMING
            if (carController.CurrentNitroMode == CarController.NitroMode.Single)
            {
                if (enablePerfectNitro && carController.IsPerfectTimingNow)
                {
                    nitroDecision = "PERFECT NITRO!";
                    carController.UseStoredNitro();
                    return;
                }
            }
            // 2. TRIPLE NITRO / SHOCKWAVE
            else if (enableTripleNitro &&
                     (carController.CurrentNitroMode == CarController.NitroMode.Double || carController.CurrentNitroMode == CarController.NitroMode.Perfect))
            {
                bool isOvertakingOrChasing = aiState == AIState.Overtaking || aiState == AIState.SlipstreamHunting || actualRacePosition > 1;
                if (isOvertakingOrChasing && severity < 0.16f && carController.NitroFill01 > 0.12f)
                {
                    nitroDecision = "TRIPLE SHOCKWAVE!";
                    carController.UseStoredNitro();
                    return;
                }
            }

            // 3. KÍCH HOẠT NITRO CƠ SỞ
            if (!carController.IsBoosting && carController.NitroReady && carController.NitroFill01 >= nitroTacticalThreshold)
            {
                bool isStraight = severity < 0.18f && Mathf.Abs(steering) < 0.25f && throttle >= 0.7f && brake <= 0.01f;
                bool isCornerExit = severity < 0.15f && previousCornerSeverity > 0.30f && speed > 9f;
                bool isOvertaking = aiState == AIState.Overtaking || slipstreamActive;
                bool isChasingLeader = actualRacePosition > 1 && severity < 0.30f;

                if (isStraight || isCornerExit || isOvertaking || isChasingLeader)
                {
                    nitroDecision = isCornerExit ? "Exit Corner Nitro" : isOvertaking ? "Overtake Nitro" : "Pursuit Nitro";
                    carController.UseStoredNitro();
                }
                else
                {
                    nitroDecision = "Ready (Holding for apex exit)";
                }
            }
            else
            {
                nitroDecision = carController.IsNitroBoosting ? "Boosting" : "Charging";
            }

            previousCornerSeverity = severity;
        }
        #endregion

        #region Predictive Slipstream & Overtaking
        private void UpdateSlipstreamAndOvertake(ref float desiredSteering, ref float targetSpeed)
        {
            if (!enableSlipstreamAndOvertake)
            {
                slipstreamActive = false;
                slipstreamTarget = null;
                targetOpponent = null;
                return;
            }

            CarController leadCar = FindBestDraftingTarget(slipstreamMaxDistance, slipstreamAngleTolerance);
            if (leadCar != null && leadCar.TargetBody != null)
            {
                targetOpponent = leadCar;
                Vector3 toLeader = leadCar.TargetBody.worldCenterOfMass - body.position;
                float distance = toLeader.magnitude;
                Vector3 localLead = transform.InverseTransformPoint(leadCar.TargetBody.worldCenterOfMass);

                // 1. VÙNG HÚT GIÓ KHÍ ĐỘNG
                if (distance >= slipstreamMinDistance && distance <= slipstreamMaxDistance && Mathf.Abs(localLead.x) < 3.2f)
                {
                    slipstreamActive = true;
                    slipstreamTarget = leadCar;
                    targetSpeed *= slipstreamSpeedBonus;

                    float draftSteer = Mathf.Clamp(localLead.x * 0.10f, -0.15f, 0.15f);
                    desiredSteering += draftSteer;

                    // 2. BẮN VƯỢT
                    if (distance <= overtakeTriggerDistance)
                    {
                        if (overtakeCommitTimer <= 0f)
                        {
                            overtakeLane = EvaluateBestOvertakeLane(leadCar);
                            overtakeCommitTimer = overtakeCommitmentDuration;
                        }

                        overtakeCommitTimer -= Time.fixedDeltaTime;
                        float sideSign = overtakeLane == OvertakeLane.Left ? -1f : 1f;

                        // Bẻ lái nhẹ nhàng sang làn vượt
                        desiredSteering = Mathf.Clamp(desiredSteering + sideSign * 0.25f, -1f, 1f);

                        if (carController.NitroReady && !carController.IsBoosting)
                        {
                            carController.UseStoredNitro();
                        }
                    }
                    else
                    {
                        overtakeLane = OvertakeLane.None;
                        overtakeCommitTimer = 0f;
                    }
                }
                else
                {
                    slipstreamActive = false;
                    slipstreamTarget = null;
                    overtakeLane = OvertakeLane.None;
                    overtakeCommitTimer = 0f;
                }
            }
            else
            {
                slipstreamActive = false;
                slipstreamTarget = null;
                targetOpponent = null;
                overtakeLane = OvertakeLane.None;
                overtakeCommitTimer = 0f;
            }
        }

        private OvertakeLane EvaluateBestOvertakeLane(CarController leader)
        {
            Vector3 pos = body.position;
            Vector3 trackRight = transform.right;

            Vector3 leftProbePos = pos - trackRight * overtakeLateralOffset;
            Vector3 rightProbePos = pos + trackRight * overtakeLateralOffset;

            bool leftClear = Physics.Raycast(leftProbePos + Vector3.up * 1.5f, -Vector3.up, 3.5f, ~0, QueryTriggerInteraction.Ignore);
            bool rightClear = Physics.Raycast(rightProbePos + Vector3.up * 1.5f, -Vector3.up, 3.5f, ~0, QueryTriggerInteraction.Ignore);

            if (leftClear && !rightClear) return OvertakeLane.Left;
            if (!leftClear && rightClear) return OvertakeLane.Right;

            Vector3 predictedLeaderPos = leader.TargetBody != null
                ? leader.TargetBody.worldCenterOfMass + leader.TargetBody.linearVelocity * overtakePredictionHorizon
                : leader.transform.position;
            Vector3 localLead = transform.InverseTransformPoint(predictedLeaderPos);
            return localLead.x >= 0f ? OvertakeLane.Left : OvertakeLane.Right;
        }

        private CarController FindBestDraftingTarget(float maxDist, float maxAngle)
        {
            float maxDistSqr = maxDist * maxDist;
            Vector3 fwd = transform.forward;
            CarController best = null;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < cachedOpponents.Count; i++)
            {
                RaceParticipant other = cachedOpponents[i];
                if (other == null || other.CarController == null || other.Finished || other.CarController.TargetBody == null) continue;

                Vector3 delta = other.CarController.TargetBody.worldCenterOfMass - body.position;
                if (Mathf.Abs(delta.y) > 4.0f) continue;

                delta.y = 0f;
                float sqrDist = delta.sqrMagnitude;
                if (sqrDist < 0.5f || sqrDist > maxDistSqr) continue;

                float angle = Vector3.Angle(fwd, delta.normalized);
                if (angle <= maxAngle && sqrDist < bestDist)
                {
                    bestDist = sqrDist;
                    best = other.CarController;
                }
            }

            return best;
        }
        #endregion

        #region Smart Obstacle Awareness
        private void ApplySmartObstacleAwareness(ref float steering, ref float targetSpeed)
        {
            if (IsOnOrApproachingRamp(out _))
            {
                return;
            }

            Vector3 origin = body.position + transform.forward * 2f + transform.up * 0.35f;
            if (!Physics.SphereCast(
                    origin,
                    0.65f,
                    transform.forward,
                    out RaycastHit hit,
                    7.5f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (IsRampCollider(hit.collider) || Vector3.Dot(hit.normal, Vector3.up) > 0.35f)
            {
                return;
            }

            CarController otherKart = hit.collider.GetComponentInParent<CarController>();
            bool isKart = otherKart != null;

            Vector3 localHit = transform.InverseTransformPoint(hit.point);
            float avoidDir = Mathf.Abs(localHit.x) > 0.15f
                ? -Mathf.Sign(localHit.x)
                : avoidanceBias != 0f ? Mathf.Sign(avoidanceBias) : 1f;

            float urgency = 1f - Mathf.Clamp01(hit.distance / 7.5f);

            if (isKart)
            {
                steering = Mathf.Clamp(steering + avoidDir * 0.45f * urgency, -1f, 1f);
                if (hit.distance < 2.0f && !doorToDoorContactAllowed)
                {
                    targetSpeed *= 0.92f;
                }
            }
            else
            {
                steering = Mathf.Clamp(steering + avoidDir * 0.70f * urgency, -1f, 1f);
                targetSpeed *= Mathf.Lerp(1f, 0.70f, urgency);
            }
        }
        #endregion

        #region Competitive Pressure & Rubberbanding
        private void UpdateCompetitivePressure(ref float targetSpeed)
        {
            if (!enableCompetitivePressure) return;

            if (cachedPlayerParticipant != null && cachedPlayerParticipant.RacePosition < actualRacePosition)
            {
                targetSpeed *= chaseSpeedBonus;
            }
            else if (actualRacePosition == 1)
            {
                targetSpeed *= defensiveSpeedFactor;
            }
        }
        #endregion

        #region Demon Tactical Combat
        private void UpdateCombatTactics()
        {
            if (!enableTacticalCombat || inventory == null || !inventory.HasItem || carController == null || !carController.ControlEnabled)
            {
                combatAction = "Standby";
                return;
            }

            var item = inventory.CurrentItem;

            if (item == KartItemPickup.ItemType.Shield)
            {
                if (IsIncomingMissileThreat())
                {
                    if (pendingShieldTime < 0f)
                    {
                        pendingShieldTime = Time.time + shieldReactionDelay;
                        combatAction = "Shield: Priming Reflex...";
                    }
                    return;
                }

                if (inventory.ItemTimeRemaining < 1.0f)
                {
                    combatAction = "Shield: Expiry Use";
                    inventory.UseItem();
                    return;
                }

                combatAction = "Shield: Armed (Standby)";
                return;
            }

            if (item == KartItemPickup.ItemType.BananaTrap)
            {
                CarController rearOpponent = FindOpponentBehind(bananaRearAmbushRange, 40f);
                if (rearOpponent != null)
                {
                    combatAction = $"Banana: Dropped on {rearOpponent.name}!";
                    inventory.UseItem();
                    return;
                }

                if (cornerSeverity > 0.38f && Mathf.Abs(smoothedSteering) > 0.22f)
                {
                    combatAction = "Banana: Apex Ambush Drop";
                    inventory.UseItem();
                    return;
                }

                if (actualRacePosition == 1 && cornerSeverity > 0.25f)
                {
                    combatAction = "Banana: Leader Defense Drop";
                    inventory.UseItem();
                    return;
                }

                if (inventory.ItemTimeRemaining < 1.0f)
                {
                    combatAction = "Banana: Expiry Drop";
                    inventory.UseItem();
                    return;
                }

                combatAction = "Banana: Armed";
                return;
            }

            if (item == KartItemPickup.ItemType.HomingMissile || item == KartItemPickup.ItemType.InkShot)
            {
                if (cornerSeverity > 0.35f)
                {
                    combatAction = "Waiting for straight to fire";
                    return;
                }

                float range = item == KartItemPickup.ItemType.HomingMissile ? inventory.MissileTargetRange : inventory.InkTargetRange;
                float angle = item == KartItemPickup.ItemType.HomingMissile ? inventory.MissileTargetAngle : inventory.InkTargetAngle;

                CarController target = FindOpponentAheadInCone(range, angle);
                if (target != null)
                {
                    combatAction = $"Firing {item} at {target.name}!";
                    inventory.UseItem();
                    return;
                }

                if (inventory.ItemTimeRemaining < 0.9f)
                {
                    inventory.UseItem();
                }
            }
        }

        private bool IsIncomingMissileThreat()
        {
            HomingKartMissile[] missiles = FindObjectsByType<HomingKartMissile>(FindObjectsSortMode.None);
            for (int i = 0; i < missiles.Length; i++)
            {
                HomingKartMissile missile = missiles[i];
                if (missile == null || !missile.isActiveAndEnabled) continue;
                if (missile.Target == carController)
                {
                    float distance = Vector3.Distance(missile.transform.position, body.position);
                    if (distance <= missileDefenseRange)
                        return true;
                }
            }
            return false;
        }

        private CarController FindOpponentBehind(float maxDistance, float maxAngle)
        {
            float maxDistSqr = maxDistance * maxDistance;
            Vector3 rearDir = -transform.forward;

            for (int i = 0; i < cachedOpponents.Count; i++)
            {
                RaceParticipant other = cachedOpponents[i];
                if (other == null || other.CarController == null || other.CarController.TargetBody == null) continue;

                Vector3 delta = other.CarController.TargetBody.worldCenterOfMass - body.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > maxDistSqr || delta.sqrMagnitude < 0.2f) continue;

                float angle = Vector3.Angle(rearDir, delta.normalized);
                if (angle <= maxAngle) return other.CarController;
            }
            return null;
        }

        private CarController FindOpponentAheadInCone(float maxDistance, float maxAngle)
        {
            float maxDistSqr = maxDistance * maxDistance;
            Vector3 fwdDir = transform.forward;
            CarController best = null;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < cachedOpponents.Count; i++)
            {
                RaceParticipant other = cachedOpponents[i];
                if (other == null || other.CarController == null || other.CarController.TargetBody == null) continue;

                Vector3 delta = other.CarController.TargetBody.worldCenterOfMass - body.position;
                delta.y = 0f;
                float sqrDist = delta.sqrMagnitude;
                if (sqrDist > maxDistSqr || sqrDist < 0.2f) continue;

                float angle = Vector3.Angle(fwdDir, delta.normalized);
                if (angle <= maxAngle && sqrDist < bestDist)
                {
                    bestDist = sqrDist;
                    best = other.CarController;
                }
            }

            return best;
        }
        #endregion

        #region Advanced Stuck Recovery & Hard Reset
        private bool UpdateStuckRecovery(Vector3 localTarget, float speed, bool isRampAirborne)
        {
            // Chỉ bỏ qua stuck nếu xe thực sự đang bay tự do trên không trung
            if (isRampAirborne && !carController.IsGrounded)
            {
                stuckTimer = 0f;
                reverseTimerRemaining = 0f;
                isReversing = false;
                return false;
            }

            if (reverseTimerRemaining > 0f)
            {
                reverseTimerRemaining -= Time.fixedDeltaTime;
                stuckTimer += Time.fixedDeltaTime;
                isReversing = true;
                recoveryState = $"Reversing ({reverseTimerRemaining:0.0}s)";

                if (stuckTimer >= hardResetAfterSeconds)
                {
                    HardRecoverToRacingLine();
                    stuckTimer = 0f;
                    reverseTimerRemaining = 0f;
                    isReversing = false;
                    return true;
                }

                float recoverySteering = -Mathf.Sign(localTarget.x != 0 ? localTarget.x : 1f);
                inputProvider.SetInput(recoverySteering, 0f, 1f);
                return true;
            }

            isReversing = false;
            if (carController.IsGrounded && speed < stuckSpeedThreshold)
            {
                stuckTimer += Time.fixedDeltaTime;
                recoveryState = $"Low Speed Stuck ({stuckTimer:0.0}s)";

                if (stuckTimer >= hardResetAfterSeconds)
                {
                    HardRecoverToRacingLine();
                    stuckTimer = 0f;
                    return true;
                }

                if (stuckTimer >= reverseAfterSeconds)
                {
                    reverseTimerRemaining = reverseDuration;
                    inputProvider.SetInput(-Mathf.Sign(localTarget.x != 0 ? localTarget.x : 1f), 0f, 1f);
                    return true;
                }
            }
            else if (speed > stuckSpeedThreshold * 1.5f)
            {
                stuckTimer = 0f;
                recoveryState = "Normal";
            }

            return false;
        }

        private void HardRecoverToRacingLine()
        {
            recoveryState = "Hard Reset Triggered";
            int recoveryIndex = racingLine.FindNextWaypoint(body.position);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = racingLine.GetPosition(recoveryIndex) + Vector3.up * 0.85f;
            body.rotation = racingLine.GetForwardRotation(recoveryIndex);
            nextWaypointIndex = (recoveryIndex + 1) % racingLine.Count;
            smoothedSteering = 0f;
            wrongWayTimer = 0f;
            stuckTimer = 0f;
            reverseTimerRemaining = 0f;
            isReversing = false;
            inputProvider.SetInput(0f, 0f, 0f);
        }
        #endregion

        #region Debug & Diagnostics
        private void UpdateDebugAIState(bool boundaryEmergency, bool spinRecovering, bool drifting, bool onRamp, bool isAirborne)
        {
            if (isReversing || boundaryEmergency)
            {
                aiState = AIState.EmergencyTrackRecovery;
            }
            else if (onRamp)
            {
                aiState = isAirborne ? AIState.AirborneStunt : AIState.RampAssault;
            }
            else if (spinRecovering)
            {
                aiState = AIState.CollisionRecovery;
            }
            else if (combatAction.StartsWith("Firing") || combatAction.StartsWith("Shield: Missile"))
            {
                aiState = AIState.Combat;
            }
            else if (overtakeLane != OvertakeLane.None)
            {
                aiState = AIState.Overtaking;
            }
            else if (slipstreamActive)
            {
                aiState = AIState.SlipstreamHunting;
            }
            else if (cornerSeverity > 0.22f)
            {
                aiState = AIState.ApexAttack;
            }
            else if (carController.IsNitroBoosting)
            {
                aiState = AIState.NitroPursuit;
            }
            else if (actualRacePosition == 1)
            {
                aiState = AIState.DefensiveRacing;
            }
            else
            {
                aiState = AIState.Racing;
            }
        }
        #endregion
    }
}
