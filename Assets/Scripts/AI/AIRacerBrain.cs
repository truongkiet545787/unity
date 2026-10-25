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

        [Header("Stable Lane Preset")]
        [Tooltip("Luôn ưu tiên bám racing line và chạy ổn định, ghi đè các giá trị AI cũ đã serialize trong Scene.")]
        [SerializeField] private bool forceStableLanePreset = true;

        [Header("AI Skill & Stable Speed")]
        [Tooltip("Kỹ năng tổng thể của Demon AI (1.0 = Esports Pro).")]
        [SerializeField, Range(0f, 1f)] private float skill = 0.9f;
        [Tooltip("Hệ số tốc độ mục tiêu (1.15 = chạy tốc độ cao, bám sát giới hạn vật lý).")]
        [SerializeField, Range(0.7f, 1.15f)] private float targetSpeedMultiplier = 0.98f;
        [Tooltip("Mức độ thận trọng khi vào cua (0.65-0.75 = ôm cua gắt, hạn chế phanh thừa).")]
        [SerializeField, Range(0.5f, 1.5f)] private float cornerCaution = 1.08f;
        [Tooltip("Tốc độ phản hồi đánh lái góc cua.")]
        [SerializeField, Min(0.1f)] private float steeringResponse = 3.5f;
        [SerializeField, Range(0.6f, 1.5f)] private float lookAheadMultiplier = 1.0f;
        [SerializeField, Range(-1f, 1f)] private float avoidanceBias = 0f;

        [Header("Driving & Look-Ahead")]
        [SerializeField, Min(1f)] private float baseLookAheadDistance = 7f;
        [SerializeField, Min(0f)] private float speedLookAheadDistance = 0.55f;
        [SerializeField, Min(1f)] private float waypointReachDistance = 4f;
        [SerializeField, Min(1)] private int cornerPreviewSegments = 3;
        [SerializeField, Range(0.2f, 0.9f)] private float minimumCornerSpeedFactor = 0.45f;
        [SerializeField, Min(0f)] private float brakeSpeedMargin = 1.5f;

        [Header("Extreme Apex Cornering (In-Out-In)")]
        [Tooltip("Kích hoạt chiến thuật cua ngoài - trong - ngoài (Outside-Inside-Outside).")]
        [SerializeField] private bool enableApexCutting = false;
        [Tooltip("Khoảng cách cắt vào góc cua trong tâm (Apex).")]
        [SerializeField, Range(0.5f, 4.0f)] private float apexCutDistance = 2.4f;
        [Tooltip("Độ dạt ra ngoài trước khi vào cua (Corner Entry Outside Bias).")]
        [SerializeField, Range(0f, 2.5f)] private float cornerEntryOutsideBias = 1.2f;
        [Tooltip("Thời gian dự đoán đánh lái sớm trước khi chạm cua.")]
        [SerializeField, Range(0.1f, 0.8f)] private float apexSteerAnticipation = 0.35f;
        [Tooltip("Cho phép Drift để vừa ép góc cua gắt vừa nạp Nitro cực nhanh.")]
        [SerializeField] private bool enableDriftCornering = false;

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

        [Header("Track Centering Stability")]
        [Tooltip("Nửa bề rộng thực tế AI được phép sử dụng quanh tim racing line. Giá trị này còn đóng vai trò cap cho Boundary Safety Corridor cũ.")]
        [SerializeField, Range(1.5f, 6f)] private float effectiveTrackHalfWidth = 3.6f;
        [Tooltip("Bắt đầu kéo nhẹ xe về tim đường khi đã dùng hết tỉ lệ này của nửa bề rộng cho phép.")]
        [SerializeField, Range(0.25f, 0.9f)] private float centerAssistStart = 0.80f;
        [Tooltip("Mức ưu tiên của hướng quay về tim đường khi gần mép.")]
        [SerializeField, Range(0.2f, 1f)] private float centerAssistStrength = 0.30f;
        [Tooltip("Hệ số tốc độ còn lại khi AI tiến sát mép đường.")]
        [SerializeField, Range(0.35f, 1f)] private float edgeSpeedFactor = 0.65f;
        [Tooltip("Dự đoán vị trí xe sau bao nhiêu giây để tránh mép sớm thay vì chờ chạm curb.")]
        [SerializeField, Range(0f, 1f)] private float boundaryPredictionSeconds = 0.18f;

        [Header("Ruthless Nitro Management")]
        [Tooltip("Tự động bấm đúng nhịp thanh Cyan để kích hoạt Perfect Nitro (185 km/h).")]
        [SerializeField] private bool enablePerfectNitro = true;
        [Tooltip("Tự động nâng cấp chuỗi Double -> Triple Nitro / Shockwave khi đuổi bắt hoặc trên đường thẳng.")]
        [SerializeField] private bool enableTripleNitro = false;
        [Tooltip("Ngưỡng Nitro tối thiểu để kích hoạt (Demon AI không tiếc Nitro).")]
        [SerializeField, Range(0.1f, 0.6f)] private float nitroTacticalThreshold = 0.28f;
        [Tooltip("Cài đặt tùy chọn: Demon AI được hồi Nitro thụ động (chỉ áp dụng cho AI, không ảnh hưởng Player).")]
        [SerializeField] private bool unlimitedDemonNitro = false;
        [SerializeField, Range(0.01f, 0.2f)] private float demonPassiveNitroRate = 0.05f;

        [Header("Predictive Slipstream & Overtaking")]
        [Tooltip("Chủ động tìm kiếm và hút gió đối thủ (Player lẫn AI khác) rồi bắn vượt.")]
        [SerializeField] private bool enableSlipstreamAndOvertake = false;
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
        [SerializeField] private bool doorToDoorContactAllowed = false;

        [Header("Demon Combat AI")]
        [Tooltip("Khai thác toàn diện Tên lửa tầm nhiệt, Vỏ chuối, Khiên phản đòn và Mực.")]
        [SerializeField] private bool enableTacticalCombat = true;
        [SerializeField, Min(5f)] private float missileDefenseRange = 34.0f;
        [SerializeField, Min(2f)] private float bananaRearAmbushRange = 9.0f;
        [Tooltip("Thời gian phản xạ bật khiên siêu nhanh của Demon (0.08 - 0.15s).")]
        [SerializeField, Range(0.05f, 0.3f)] private float shieldReactionDelay = 0.10f;

        [Header("Dynamic Competitive Pressure (Rubberbanding)")]
        [Tooltip("Duy trì áp lực cạnh tranh sát sao mà không gian lận tốc độ phi lý.")]
        [SerializeField] private bool enableCompetitivePressure = false;
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
        private float obstacleAvoidHoldTimer;
        private float obstacleAvoidDirection;
        private bool rampContextActive;

        // Ramp & Aerial Awareness
        private PoseKart.Stunts.PoseKartStuntController stuntController;

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
            skill = Mathf.Clamp01(racerSkill);
            targetSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.7f, 1.15f);
            cornerCaution = Mathf.Clamp(caution, 0.5f, 1.5f);
            steeringResponse = Mathf.Max(0.1f, steerResponse);
            lookAheadMultiplier = Mathf.Clamp(lookAhead, 0.6f, 1.5f);
            avoidanceBias = Mathf.Clamp(sideBias, -1f, 1f);
        }
        public void BindNetworkRuntime(
    RacingLine line)
        {
            racingLine = line;

            if (body == null)
                body = GetComponent<Rigidbody>();

            if (carController == null)
                carController = GetComponent<CarController>();

            if (inputProvider == null)
                inputProvider = GetComponent<AIInputProvider>();

            if (racingLine != null &&
                racingLine.IsConfigured)
            {
                nextWaypointIndex =
                    racingLine.FindStartingWaypoint(
                        transform.position
                    );
            }

            Debug.Log(
                $"[AIRacerBrain] Network runtime bound | " +
                $"Line={(racingLine != null)} | " +
                $"Body={(body != null)} | " +
                $"Controller={(carController != null)} | " +
                $"Input={(inputProvider != null)}",
                this
            );
        }
        public void ApplyDemonPreset(float seedOffset = 0f)
        {
            // Giữ API cũ để các script/scene hiện tại không bị vỡ.
            ApplyStableLanePreset(seedOffset);
        }

        private void ApplyStableLanePreset(float seedOffset = 0f)
        {
            // Mục tiêu: bám racing line chắc, vào cua đúng, không tự tranh steering.
            skill = 0.90f;
            targetSpeedMultiplier = 0.98f;
            cornerCaution = 1.08f;
            steeringResponse = 8.5f;
            lookAheadMultiplier = 1.0f;
            baseLookAheadDistance = 7f;
            speedLookAheadDistance = 0.55f;
            waypointReachDistance = 4f;
            cornerPreviewSegments = 3;
            minimumCornerSpeedFactor = 0.45f;
            brakeSpeedMargin = 1.5f;

            // Không tự cắt apex/drift: RacingLine là nguồn điều hướng chính.
            enableApexCutting = false;
            enableDriftCornering = false;

            // Boundary chỉ làm lưới an toàn, không tranh tay lái với racing line.
            enableTrackBoundaryProtection = true;
            boundarySafetyCorridor = 5.5f;
            boundaryProbeDistance = 10f;
            boundaryProbeRadius = 0.55f;
            emergencyBrakeSensitivity = 0.35f;
            cliffDropThreshold = 3.5f;
            effectiveTrackHalfWidth = 5.2f;
            centerAssistStart = 0.80f;
            centerAssistStrength = 0.30f;
            edgeSpeedFactor = 0.75f;
            boundaryPredictionSeconds = 0.18f;

            // Nitro vẫn dùng được nhưng bảo thủ hơn và không cố chain shockwave.
            enablePerfectNitro = true;
            enableTripleNitro = false;
            nitroTacticalThreshold = 0.28f;
            unlimitedDemonNitro = false;

            // Tắt các hệ thống dễ làm xe rời racing line.
            enableSlipstreamAndOvertake = false;
            slipstreamMinDistance = 3f;
            slipstreamMaxDistance = 18f;
            slipstreamAngleTolerance = 20f;
            slipstreamSpeedBonus = 1.08f;
            overtakePredictionHorizon = 0.65f;
            overtakeTriggerDistance = 6.5f;
            overtakeCommitmentDuration = 1.0f;
            overtakeLateralOffset = 1.5f;
            doorToDoorContactAllowed = false;

            // Item vẫn dùng bình thường, nhưng không ảnh hưởng logic giữ làn.
            enableTacticalCombat = true;
            missileDefenseRange = 30f;
            bananaRearAmbushRange = 8f;
            shieldReactionDelay = Mathf.Clamp(0.12f + seedOffset * 0.02f, 0.09f, 0.16f);

            enableCompetitivePressure = false;
            chaseSpeedBonus = 1.0f;
            defensiveSpeedFactor = 1.0f;

            stuckSpeedThreshold = 0.85f;
            reverseAfterSeconds = 2.8f;
            reverseDuration = 1.25f;
            hardResetAfterSeconds = 7.0f;
            spinRecoveryAngleThreshold = 60f;

            avoidanceBias = Mathf.Clamp(seedOffset * 0.15f, -0.25f, 0.25f);
        }

        private void Awake()
        {
            participant = GetComponent<RaceParticipant>();

            if (participant != null && participant.IsPlayer)
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

            // Scene cũ có thể đang giữ các giá trị AI đã serialize.
            // Khi bật tùy chọn này, luôn áp dụng preset bám làn ổn định lúc runtime.
            if (forceStableLanePreset)
            {
                float microSeed = 0f;
                if (participant != null)
                {
                    if (participant.RacerName.IndexOf("Sunset", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = -0.15f;
                    else if (participant.RacerName.IndexOf("Azure", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = 0f;
                    else if (participant.RacerName.IndexOf("Coral", StringComparison.OrdinalIgnoreCase) >= 0) microSeed = 0.15f;
                }
                ApplyStableLanePreset(microSeed);
            }

            // Tự động vô hiệu hóa các collider trang trí của vạch đích (Finish checker) nếu có
            DisableFinishCheckerColliders();

            nextWaypointIndex = racingLine.FindStartingWaypoint(body.position);
            RefreshOpponentsCache();
        }

        private static bool finishCheckersCleaned;
        private static void DisableFinishCheckerColliders()
        {
            if (finishCheckersCleaned) return;
            finishCheckersCleaned = true;

            var allColliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            for (int i = 0; i < allColliders.Length; i++)
            {
                var c = allColliders[i];
                if (c != null && c.name.IndexOf("checker", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    c.enabled = false;
                }
            }
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
                carController?.SetSlipstreamBonus(0f);
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                reverseTimerRemaining = 0f;
                isReversing = false;
                obstacleAvoidHoldTimer = 0f;
                obstacleAvoidDirection = 0f;
                aiState = AIState.Racing;
                recoveryState = "Control Disabled";
                return;
            }

            // Xe này đã hoàn thành cuộc đua: dừng xe mượt mà, không giật hay lùi xe
            if (participant != null && participant.Finished)
            {
                inputProvider.SetInput(0f, 0f, 0.4f);
                carController?.SetSlipstreamBonus(0f);
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                reverseTimerRemaining = 0f;
                isReversing = false;
                obstacleAvoidHoldTimer = 0f;
                obstacleAvoidDirection = 0f;
                aiState = AIState.Racing;
                recoveryState = "Race Finished";
                return;
            }

            // Refresh đối thủ định kỳ, tránh FindObjects mỗi physics tick.
            if (Time.time >= nextOpponentScanTime)
            {
                nextOpponentScanTime = Time.time + 2.0f;
                RefreshOpponentsCache();
            }

            actualRacePosition = participant != null ? participant.RacePosition : 1;

            // 0. RAMP / AIRBORNE
            bool onRamp = IsOnOrApproachingRamp(out bool isRampAirborne);
            rampContextActive = onRamp;
            if (isRampAirborne && !carController.IsGrounded)
            {
                smoothedSteering = Mathf.MoveTowards(smoothedSteering, 0f, 8f * Time.fixedDeltaTime);
                inputProvider.SetInput(smoothedSteering, 1f, 0f);
                inputProvider.SetDrift(false);
                aiState = AIState.AirborneStunt;
                recoveryState = "Airborne Stunt Flight";
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                return;
            }

            // 1. RACING LINE / LOOK-AHEAD
            racingLine.AdvanceProgress(ref nextWaypointIndex, body.position, waypointReachDistance);

            float speed = Mathf.Abs(carController.ForwardSpeed);
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(1f, carController.MaxForwardSpeed));

            float lookAhead = (baseLookAheadDistance + speed * speedLookAheadDistance) * lookAheadMultiplier;
            Vector3 baseTarget = racingLine.GetLookAheadPoint(nextWaypointIndex, body.position, lookAhead);
            cornerSeverity = racingLine.GetCornerSeverity(nextWaypointIndex, cornerPreviewSegments);
            Vector3 trackTangent = racingLine.GetTrackTangent(nextWaypointIndex);

            // 2. WRONG-WAY RECOVERY
            float headingDot = Vector3.Dot(transform.forward, trackTangent);
            bool isWrongWay = headingDot < -0.22f;
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

                smoothedSteering = Mathf.MoveTowards(
                    smoothedSteering,
                    turnSign,
                    steeringResponse * 1.4f * Time.fixedDeltaTime);

                if (carController.ForwardSpeed > 3.0f)
                {
                    inputProvider.SetInput(smoothedSteering, 0f, 1f);
                }
                else
                {
                    inputProvider.SetInput(smoothedSteering, 0.45f, 0f);
                }

                inputProvider.SetDrift(false);
                aiState = AIState.EmergencyTrackRecovery;
                recoveryState = $"Wrong Way Turnaround ({wrongWayTimer:0.0}s)";
                return;
            }

            wrongWayTimer = 0f;

            // 3. APEX TARGET
            Vector3 target = CalculateApexTrajectory(baseTarget, cornerSeverity);
            Vector3 localTarget = transform.InverseTransformPoint(target);

            // 4. BASE STEERING: tốc độ càng cao thì góc full-steer càng lớn -> bớt giật.
            float desiredSteering;
            if (localTarget.z > 0.1f)
            {
                float targetAngle = Mathf.Atan2(localTarget.x, Mathf.Max(0.5f, localTarget.z)) * Mathf.Rad2Deg;
                float steeringAngleRange = Mathf.Lerp(45f, 52f, speed01);
                desiredSteering = Mathf.Clamp(targetAngle / steeringAngleRange, -1f, 1f);
            }
            else
            {
                desiredSteering = localTarget.x >= 0f ? 1f : -1f;
            }

            // 5. BASE TARGET SPEED
            float cautionSeverity = Mathf.Clamp01(cornerSeverity * cornerCaution);
            float skillFactor = Mathf.Lerp(0.86f, 1.03f, skill);
            float cornerFactor = Mathf.Lerp(1f, minimumCornerSpeedFactor, cautionSeverity);

            desiredSpeed = carController.MaxForwardSpeed
                           * targetSpeedMultiplier
                           * skillFactor
                           * cornerFactor
                           * racingLine.GetRecommendedSpeedFactor(nextWaypointIndex);

            // Cua gắt + steering lớn: hạ thêm tốc độ trước khi các hệ thống khác can thiệp.
            if (cornerSeverity > 0.30f)
            {
                float steeringLoad = Mathf.InverseLerp(0.30f, 1.0f, Mathf.Abs(desiredSteering));
                desiredSpeed *= Mathf.Lerp(1f, 0.86f, steeringLoad * cautionSeverity);
            }

            // 6. COMPETITIVE PRESSURE
            UpdateCompetitivePressure(ref desiredSpeed);

            // 7. SLIPSTREAM / OVERTAKE - hạn chế vượt trong cua gắt.
            if (!onRamp && cornerSeverity < 0.22f && trackBoundaryRisk < 0.20f)
            {
                UpdateSlipstreamAndOvertake(ref desiredSteering, ref desiredSpeed);
            }
            else
            {
                ResetOvertakeIntent();
            }

            // Không để các multiplier cộng dồn khiến AI chạy 150-170% tốc độ thường.
            if (!carController.IsBoosting && !carController.IsNitroBoosting)
            {
                float normalCapFactor = 1.00f;
                desiredSpeed = Mathf.Min(desiredSpeed, carController.MaxForwardSpeed * normalCapFactor);
            }

            // 8. OBSTACLE AWARENESS
            if (!onRamp)
            {
                ApplySmartObstacleAwareness(ref desiredSteering, ref desiredSpeed);
            }

            // 9. TRACK BOUNDARY - có quyền ưu tiên cao hơn overtaking/avoidance.
            bool boundaryEmergency = ApplyTrackBoundaryProtection(ref desiredSteering, ref desiredSpeed);

            // 10. COLLISION SPIN RECOVERY
            bool spinRecovering = ApplyCollisionSpinRecovery(ref desiredSteering, ref desiredSpeed);

            // 11. RAMP: bám tim, không né/vượt lung tung.
            if (onRamp)
            {
                boundaryEmergency = false;
                trackBoundaryRisk = 0f;
                spinRecovering = false;
                desiredSpeed = Mathf.Min(desiredSpeed, carController.MaxForwardSpeed * 0.95f);
                desiredSteering = Mathf.Clamp(desiredSteering, -0.20f, 0.20f);
            }

            // 12. STEERING SMOOTHING
            float steerResponseNow = Mathf.Lerp(steeringResponse, steeringResponse * 0.70f, speed01);
            if (boundaryEmergency || spinRecovering)
            {
                steerResponseNow = steeringResponse * 1.35f;
            }

            smoothedSteering = Mathf.MoveTowards(
                smoothedSteering,
                Mathf.Clamp(desiredSteering, -1f, 1f),
                steerResponseNow * Time.fixedDeltaTime);

            // 13. STUCK RECOVERY
            if (UpdateStuckRecovery(localTarget, speed, isRampAirborne))
            {
                return;
            }

            // 14. THROTTLE / BRAKE
            float throttle = 1f;
            float brake = 0f;

            if (onRamp)
            {
                throttle = 1f;
            }
            else if (boundaryEmergency)
            {
                // Khi xe ở ngoài mép đường, ưu tiên lái xe quay trở lại tim đường.
                // TUYỆT ĐỐI KHÔNG đạp phanh khi xe đang chạy chậm hoặc xuất phát, vì phanh trong CarController
                // sẽ kích hoạt số lùi (reverse) và triệt tiêu toàn bộ ga, làm xe đứng chết một chỗ!
                if (speed > desiredSpeed + brakeSpeedMargin)
                {
                    throttle = 0f;
                    brake = Mathf.Clamp(trackBoundaryRisk * emergencyBrakeSensitivity, 0f, 0.40f);
                }
                else
                {
                    throttle = 1f;
                    brake = 0f;
                }
            }
            else if (spinRecovering)
            {
                throttle = 0.35f;
            }
            else if (carController.IsBoosting || carController.IsNitroBoosting)
            {
                throttle = 1f;
            }
            else if (speed < desiredSpeed - brakeSpeedMargin)
            {
                throttle = 1f;
            }
            else if (speed < desiredSpeed)
            {
                throttle = 0.65f;
            }
            else if (speed > desiredSpeed + brakeSpeedMargin + 2f)
            {
                throttle = 0f;
                brake = Mathf.InverseLerp(
                    desiredSpeed + brakeSpeedMargin,
                    desiredSpeed + 5f,
                    speed);
            }
            else
            {
                throttle = 0.30f;
            }

            inputProvider.SetInput(smoothedSteering, throttle, brake);

            // 15. NITRO
            UpdateRuthlessNitro(speed, cornerSeverity, smoothedSteering, throttle, brake, onRamp);

            // 16. DRIFT
            bool wantsDrift = enableDriftCornering
                              && carController.IsGrounded
                              && !onRamp
                              && speed > 10.5f
                              && cautionSeverity > 0.38f
                              && Mathf.Abs(smoothedSteering) > 0.32f
                              && brake <= 0.01f
                              && !isReversing
                              && !boundaryEmergency
                              && !spinRecovering;

            inputProvider.SetDrift(wantsDrift);
            UpdateDebugAIState(boundaryEmergency, spinRecovering, wantsDrift, onRamp, isRampAirborne);
        }

        #region Ramp & Aerial Awareness
        /// <summary>
        /// Phát hiện xe đang ở trên dốc phóng, chuẩn bị bay, hoặc đang lơ lửng trên không.
        /// </summary>
        private bool IsOnOrApproachingRamp(out bool isRampAirborne)
        {
            isRampAirborne = false;

            // Kiểm tra StuntController: chỉ coi là ramp airborne khi đang thực hiện stunt hoặc trong trạng thái Ramp Airborne
            if (stuntController != null &&
                (stuntController.State == PoseKart.Stunts.StuntState.RampApproach ||
                 stuntController.State == PoseKart.Stunts.StuntState.Airborne ||
                 stuntController.IsStuntActive))
            {
                isRampAirborne = stuntController.State == PoseKart.Stunts.StuntState.Airborne || stuntController.IsStuntActive;
                return true;
            }

            Vector3 pos = body.position;
            Vector3 forward = transform.forward;

            // Tìm ramp ngay dưới xe nhưng bỏ qua collider của chính xe.
            RaycastHit[] groundHits = Physics.RaycastAll(
                pos + Vector3.up * 0.8f,
                Vector3.down,
                3.2f,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < groundHits.Length; i++)
            {
                Collider col = groundHits[i].collider;
                if (IsSelfCollider(col)) continue;
                if (IsRampCollider(col))
                {
                    if (carController != null && !carController.IsGrounded)
                    {
                        isRampAirborne = true;
                    }
                    return true;
                }
            }

            // Chỉ nhận ramp thật sự nằm trong hành lang trước mũi xe.
            Vector3 origin = pos + forward * 1.6f + Vector3.up * 0.5f;
            RaycastHit[] forwardHits = Physics.SphereCastAll(
                origin,
                0.55f,
                forward,
                7.0f,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < forwardHits.Length; i++)
            {
                Collider col = forwardHits[i].collider;
                if (IsSelfCollider(col)) continue;
                if (IsRampCollider(col)) return true;
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
            if (rampContextActive)
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
                // Không cho apex target chạm sát mép đường. Dùng corridor thực tế nhỏ hơn
                // giữa giá trị cũ trong scene và giới hạn ổn định mới.
                float corridor = GetEffectiveTrackHalfWidth() * 0.72f;
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
            if (!enableTrackBoundaryProtection || racingLine == null || racingLine.Count < 2)
            {
                trackBoundaryRisk = 0f;
                return false;
            }

            if (rampContextActive)
            {
                trackBoundaryRisk = 0f;
                return false;
            }

            float corridor = GetEffectiveTrackHalfWidth();
            if (!TryGetClosestTrackFrame(
                    body.position,
                    out Vector3 centerPoint,
                    out Vector3 tangent,
                    out Vector3 trackRight,
                    out float lateralOffset))
            {
                trackBoundaryRisk = 0f;
                return false;
            }

            // Dự đoán vị trí sắp tới. Đây là phần quan trọng để AI quay vào TRƯỚC
            // khi bánh xe thực sự chạm curb/mép đường.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 predictedPosition = body.position + planarVelocity * boundaryPredictionSeconds;

            float predictedOffset = lateralOffset;
            if (TryGetClosestTrackFrame(
                    predictedPosition,
                    out _,
                    out _,
                    out _,
                    out float futureOffset) && Mathf.Abs(futureOffset) > Mathf.Abs(predictedOffset))
            {
                predictedOffset = futureOffset;
            }

            float usedOffset = Mathf.Abs(predictedOffset) >= Mathf.Abs(lateralOffset)
                ? predictedOffset
                : lateralOffset;

            float absoluteOffset = Mathf.Abs(usedOffset);
            float softStart = corridor * Mathf.Clamp(centerAssistStart, 0.25f, 0.9f);
            float risk = Mathf.InverseLerp(softStart, corridor, absoluteOffset);
            risk = Mathf.SmoothStep(0f, 1f, risk);

            if (risk > 0f)
            {
                // Dùng chính racing line phía trước làm recovery target. Không cộng một
                // steer trái/phải cố định vì kiểu đó gây ping-pong ở mép đường.
                float speed = Mathf.Abs(carController.ForwardSpeed);
                float recoveryLookAhead = Mathf.Lerp(5.0f, 10.0f,
                    Mathf.Clamp01(speed / Mathf.Max(1f, carController.MaxForwardSpeed)));
                Vector3 recoveryTarget = racingLine.GetLookAheadPoint(
                    nextWaypointIndex,
                    body.position,
                    recoveryLookAhead);

                Vector3 localRecovery = transform.InverseTransformPoint(recoveryTarget);
                float recoveryAngle = Mathf.Atan2(localRecovery.x, Mathf.Max(0.1f, localRecovery.z)) * Mathf.Rad2Deg;
                float recoverySteering = Mathf.Clamp(recoveryAngle / 45f, -1f, 1f);

                float blend = Mathf.Clamp01(risk * risk * centerAssistStrength);
                desiredSteering = Mathf.Lerp(desiredSteering, recoverySteering, blend);
                targetSpeed *= Mathf.Lerp(1f, edgeSpeedFactor, risk);

                // Khi đã gần mép thì bỏ ý định vượt/núp gió để racing line có toàn quyền.
                if (risk >= 0.65f)
                {
                    ResetOvertakeIntent();
                    carController.SetSlipstreamBonus(0f);
                }
            }

            // Nếu thực sự vượt corridor, ép mạnh hơn về tim đường nhưng vẫn dùng
            // một recovery target liên tục, không đổi hướng đột ngột theo nearest waypoint.
            if (absoluteOffset > corridor)
            {
                float overflow = Mathf.Clamp01((absoluteOffset - corridor) / Mathf.Max(0.5f, corridor * 0.5f));
                Vector3 hardTarget = centerPoint + tangent * Mathf.Lerp(4f, 8f, overflow);
                Vector3 localHardTarget = transform.InverseTransformPoint(hardTarget);
                float hardAngle = Mathf.Atan2(localHardTarget.x, Mathf.Max(0.1f, localHardTarget.z)) * Mathf.Rad2Deg;
                float hardSteer = Mathf.Clamp(hardAngle / 26f, -1f, 1f);

                desiredSteering = Mathf.Lerp(desiredSteering, hardSteer, Mathf.Lerp(0.78f, 0.95f, overflow));
                targetSpeed *= Mathf.Lerp(0.72f, 0.52f, overflow);
                risk = 1f;
            }

            trackBoundaryRisk = risk;
            return absoluteOffset > corridor * 1.20f;
        }

        private float GetEffectiveTrackHalfWidth()
        {
            float legacyCorridor = Mathf.Max(1.5f, boundarySafetyCorridor);
            return Mathf.Max(1.5f, Mathf.Min(legacyCorridor, effectiveTrackHalfWidth));
        }

        /// <summary>
        /// Tìm điểm gần nhất trên POLYLINE của racing line thay vì waypoint gần nhất.
        /// Kết quả thay đổi liên tục khi xe chạy qua ranh giới giữa hai waypoint,
        /// tránh việc boundary correction đổi dấu/đổi tangent đột ngột.
        /// Tìm kiếm cục bộ quanh nextWaypointIndex với kiểm tra cao độ 3D để không nhầm đoạn đường khác.
        /// </summary>
        private bool TryGetClosestTrackFrame(
            Vector3 position,
            out Vector3 centerPoint,
            out Vector3 tangent,
            out Vector3 trackRight,
            out float lateralOffset)
        {
            centerPoint = position;
            tangent = transform.forward;
            trackRight = transform.right;
            lateralOffset = 0f;

            if (racingLine == null || racingLine.Count < 2)
            {
                return false;
            }

            int count = racingLine.Count;
            Vector3 planarPosition = new Vector3(position.x, 0f, position.z);
            float bestSqrDistance = float.PositiveInfinity;
            int bestSegment = 0;
            float bestT = 0f;

            // 1. Quét cục bộ xung quanh nextWaypointIndex (10 segment phía sau đến 22 segment phía trước)
            if (nextWaypointIndex >= 0 && nextWaypointIndex < count)
            {
                int searchStart = (nextWaypointIndex - 10 + count) % count;
                int searchCount = Mathf.Min(32, count);

                for (int step = 0; step < searchCount; step++)
                {
                    int i = (searchStart + step) % count;
                    Vector3 a3 = racingLine.GetPosition(i);
                    Vector3 b3 = racingLine.GetPosition((i + 1) % count);

                    // Kiểm tra độ chênh lệch cao độ để không nhầm cầu vượt/đoạn dốc/đường ngầm
                    float midY = (a3.y + b3.y) * 0.5f;
                    if (Mathf.Abs(position.y - midY) > 8f) continue;

                    Vector3 a = new Vector3(a3.x, 0f, a3.z);
                    Vector3 b = new Vector3(b3.x, 0f, b3.z);
                    Vector3 ab = b - a;
                    float lengthSqr = ab.sqrMagnitude;
                    if (lengthSqr < 0.0001f) continue;

                    float t = Mathf.Clamp01(Vector3.Dot(planarPosition - a, ab) / lengthSqr);
                    Vector3 projected = a + ab * t;
                    float sqrDistance = (planarPosition - projected).sqrMagnitude;

                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        bestSegment = i;
                        bestT = t;
                    }
                }
            }

            // 2. Fallback quét toàn bộ nếu tìm kiếm cục bộ không thấy điểm nào gần (< 30m)
            if (bestSqrDistance > 900f)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 a3 = racingLine.GetPosition(i);
                    Vector3 b3 = racingLine.GetPosition((i + 1) % count);
                    float midY = (a3.y + b3.y) * 0.5f;
                    float yDiff = position.y - midY;

                    Vector3 a = new Vector3(a3.x, 0f, a3.z);
                    Vector3 b = new Vector3(b3.x, 0f, b3.z);
                    Vector3 ab = b - a;
                    float lengthSqr = ab.sqrMagnitude;
                    if (lengthSqr < 0.0001f) continue;

                    float t = Mathf.Clamp01(Vector3.Dot(planarPosition - a, ab) / lengthSqr);
                    Vector3 projected = a + ab * t;
                    // Phạt nặng chênh lệch chiều cao khi tính khoảng cách
                    float sqrDistance = (planarPosition - projected).sqrMagnitude + yDiff * yDiff * 4f;

                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        bestSegment = i;
                        bestT = t;
                    }
                }
            }

            Vector3 p0 = racingLine.GetPosition(bestSegment);
            Vector3 p1 = racingLine.GetPosition((bestSegment + 1) % count);
            centerPoint = Vector3.Lerp(p0, p1, bestT);

            Vector3 dir = p1 - p0;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            tangent = dir.normalized;
            trackRight = Vector3.Cross(Vector3.up, tangent).normalized;
            lateralOffset = Vector3.Dot(position - centerPoint, trackRight);
            return true;
        }

        private static bool IsStaticObstacle(Collider col)
        {
            if (col == null || col.isTrigger) return false;
            if (IsRampCollider(col)) return false;
            string colName = col.name;
            if (colName.IndexOf("checker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                colName.IndexOf("finish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                colName.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 ||
                colName.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0 ||
                colName.IndexOf("asphalt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
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
                previousCornerSeverity = severity;
                return;
            }

            if (onRamp || brake > 0.01f || severity > 0.34f)
            {
                nitroDecision = onRamp ? "Holding for Ramp" : "Holding for Control";
                previousCornerSeverity = severity;
                return;
            }

            bool isStraight = severity < 0.18f && Mathf.Abs(steering) < 0.25f && throttle >= 0.65f;
            bool isCornerExit = severity < 0.15f && previousCornerSeverity > 0.30f && speed > 9f;
            bool isOvertaking = overtakeLane != OvertakeLane.None || slipstreamActive;
            bool isChasing = actualRacePosition > 1 && severity < 0.24f;
            bool aggressiveWindow = isStraight || isCornerExit || isOvertaking || isChasing;

            // CarController's nitro state machine is:
            // None -> Single; pressing again while Single chooses Shockwave/Perfect/Double.
            // Pressing again while already Double/Perfect/Shockwave stops Nitro, so never spam UseStoredNitro().
            switch (carController.CurrentNitroMode)
            {
                case CarController.NitroMode.None:
                    if (carController.NitroReady &&
                        carController.NitroFill01 >= nitroTacticalThreshold &&
                        aggressiveWindow)
                    {
                        nitroDecision = isCornerExit ? "Start Nitro: Corner Exit"
                            : isOvertaking ? "Start Nitro: Overtake"
                            : "Start Nitro: Pursuit";
                        carController.UseStoredNitro();
                    }
                    else
                    {
                        nitroDecision = carController.NitroReady ? "Ready (Holding)" : "Charging";
                    }
                    break;

                case CarController.NitroMode.Single:
                    if (enableTripleNitro && carController.IsShockwaveTimingNow && aggressiveWindow)
                    {
                        nitroDecision = "SHOCKWAVE!";
                        carController.UseStoredNitro();
                    }
                    else if (enablePerfectNitro && carController.IsPerfectTimingNow && aggressiveWindow)
                    {
                        nitroDecision = "PERFECT NITRO!";
                        carController.UseStoredNitro();
                    }
                    else if (!carController.IsPerfectTimingNow &&
                             carController.NitroFill01 < carController.NitroPerfectStart01 &&
                             aggressiveWindow)
                    {
                        nitroDecision = "DOUBLE NITRO";
                        carController.UseStoredNitro();
                    }
                    else
                    {
                        nitroDecision = "Single: Timing...";
                    }
                    break;

                case CarController.NitroMode.Double:
                    nitroDecision = "Double Boosting";
                    break;

                case CarController.NitroMode.Shockwave:
                    nitroDecision = "Shockwave Boosting";
                    break;

                case CarController.NitroMode.Perfect:
                    nitroDecision = "Perfect Boosting";
                    break;
            }

            previousCornerSeverity = severity;
        }
        #endregion

        #region Predictive Slipstream & Overtaking
        private void UpdateSlipstreamAndOvertake(ref float desiredSteering, ref float targetSpeed)
        {
            if (!enableSlipstreamAndOvertake)
            {
                ResetOvertakeIntent();
                targetOpponent = null;
                return;
            }

            CarController leadCar = FindBestDraftingTarget(slipstreamMaxDistance, slipstreamAngleTolerance);
            if (leadCar == null || leadCar.TargetBody == null)
            {
                ResetOvertakeIntent();
                targetOpponent = null;
                return;
            }

            targetOpponent = leadCar;

            Vector3 predictedLeaderPos = leadCar.TargetBody.worldCenterOfMass
                                       + leadCar.TargetBody.linearVelocity * Mathf.Min(overtakePredictionHorizon, 0.65f);
            Vector3 toLeader = predictedLeaderPos - body.position;
            float distance = toLeader.magnitude;
            Vector3 localLead = transform.InverseTransformPoint(predictedLeaderPos);

            if (distance < slipstreamMinDistance ||
                distance > slipstreamMaxDistance ||
                Mathf.Abs(localLead.x) > 3.4f)
            {
                ResetOvertakeIntent();
                return;
            }

            slipstreamActive = true;
            slipstreamTarget = leadCar;

            // Use CarController's real slipstream system so the physical speed limit
            // and acceleration agree with the AI's desired speed.
            float slipstreamBonusKmh = Mathf.Clamp(
                carController.MaxForwardSpeed * 3.6f * Mathf.Max(0f, slipstreamSpeedBonus - 1f),
                0f,
                20f);
            carController.SetSlipstreamBonus(slipstreamBonusKmh);

            targetSpeed = Mathf.Min(
                targetSpeed + slipstreamBonusKmh / 3.6f,
                carController.MaxForwardSpeed * 1.12f);

            // Khi còn xa, bám sau xe trước nhẹ nhàng thay vì giật thẳng vào tâm nó.
            if (distance > overtakeTriggerDistance)
            {
                float draftSteer = Mathf.Clamp(localLead.x * 0.07f, -0.10f, 0.10f);
                desiredSteering = Mathf.Clamp(desiredSteering + draftSteer, -1f, 1f);
                overtakeLane = OvertakeLane.None;
                overtakeCommitTimer = 0f;
                return;
            }

            // Không bắn vượt ở cua gắt; giảm nhẹ tốc độ để không tông đuôi.
            if (cornerSeverity > 0.30f)
            {
                overtakeLane = OvertakeLane.None;
                overtakeCommitTimer = 0f;
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(3f, Mathf.Abs(leadCar.ForwardSpeed) * 0.98f));
                return;
            }

            if (overtakeLane == OvertakeLane.None)
            {
                if (overtakeCommitTimer > 0f)
                {
                    overtakeCommitTimer -= Time.fixedDeltaTime;
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(3f, Mathf.Abs(leadCar.ForwardSpeed) * 0.96f));
                    return;
                }

                overtakeLane = EvaluateBestOvertakeLane(leadCar);
                overtakeCommitTimer = overtakeLane == OvertakeLane.None ? 0.25f : overtakeCommitmentDuration;
            }
            else
            {
                overtakeCommitTimer -= Time.fixedDeltaTime;
                if (overtakeCommitTimer <= 0f)
                {
                    overtakeLane = EvaluateBestOvertakeLane(leadCar);
                    overtakeCommitTimer = overtakeLane == OvertakeLane.None ? 0.25f : overtakeCommitmentDuration;
                }
            }

            if (overtakeLane == OvertakeLane.None)
            {
                // Hai bên đều bí: đừng lao vào đuôi xe trước.
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(3f, Mathf.Abs(leadCar.ForwardSpeed) * 0.96f));
                return;
            }

            float sideSign = overtakeLane == OvertakeLane.Left ? -1f : 1f;
            float laneSteer = sideSign * Mathf.Lerp(0.18f, 0.32f, Mathf.InverseLerp(overtakeTriggerDistance, 2f, distance));
            desiredSteering = Mathf.Clamp(desiredSteering + laneSteer, -1f, 1f);

            if (carController.NitroReady && !carController.IsBoosting && cornerSeverity < 0.18f)
            {
                carController.UseStoredNitro();
            }
        }

        private OvertakeLane EvaluateBestOvertakeLane(CarController leader)
        {
            bool leftClear = IsOvertakeLaneClear(-1f);
            bool rightClear = IsOvertakeLaneClear(1f);

            if (leftClear && !rightClear) return OvertakeLane.Left;
            if (!leftClear && rightClear) return OvertakeLane.Right;
            if (!leftClear && !rightClear) return OvertakeLane.None;

            Vector3 predictedLeaderPos = leader.TargetBody != null
                ? leader.TargetBody.worldCenterOfMass + leader.TargetBody.linearVelocity * overtakePredictionHorizon
                : leader.transform.position;

            Vector3 localLead = transform.InverseTransformPoint(predictedLeaderPos);

            // Nếu xe trước lệch phải thì vượt trái và ngược lại.
            if (Mathf.Abs(localLead.x) > 0.25f)
            {
                return localLead.x >= 0f ? OvertakeLane.Left : OvertakeLane.Right;
            }

            // Khi xe trước nằm giữa, dùng bias riêng của từng AI để tránh cả 3 chọn cùng một bên.
            if (Mathf.Abs(avoidanceBias) > 0.01f)
            {
                return avoidanceBias < 0f ? OvertakeLane.Left : OvertakeLane.Right;
            }

            return actualRacePosition % 2 == 0 ? OvertakeLane.Left : OvertakeLane.Right;
        }

        private bool IsOvertakeLaneClear(float sideSign)
        {
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            Vector3 lanePos = body.position + right * sideSign * overtakeLateralOffset;

            // Lane phải còn mặt đường.
            bool hasGround = false;
            RaycastHit[] groundHits = Physics.RaycastAll(
                lanePos + Vector3.up * 1.8f,
                Vector3.down,
                4.5f,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < groundHits.Length; i++)
            {
                Collider col = groundHits[i].collider;
                if (IsSelfCollider(col)) continue;
                if (col != null && col.GetComponentInParent<CarController>() != null) continue;
                if (Vector3.Dot(groundHits[i].normal, Vector3.up) < 0.40f) continue;

                hasGround = true;
                break;
            }

            if (!hasGround) return false;

            // Kiểm tra hành lang trước lane: tường/xe khác đều được coi là bị chặn.
            Vector3 probeOrigin = lanePos + Vector3.up * 0.45f;
            return !TryGetBlockingHit(
                probeOrigin,
                forward,
                Mathf.Max(6f, overtakeTriggerDistance),
                0.70f,
                true,
                out _);
        }

        private void ResetOvertakeIntent()
        {
            slipstreamActive = false;
            slipstreamTarget = null;
            overtakeLane = OvertakeLane.None;
            overtakeCommitTimer = 0f;
            if (carController != null)
            {
                carController.SetSlipstreamBonus(0f);
            }
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
            // Giữ tinh thần script cũ: chỉ một cảm biến phía trước, ảnh hưởng nhẹ.
            // Dùng TryGetBlockingHit để bỏ collider của chính xe và mặt đường.
            float speed = Mathf.Abs(carController.ForwardSpeed);
            float lookAhead = Mathf.Lerp(6.5f, 8.5f,
                Mathf.Clamp01(speed / Mathf.Max(1f, carController.MaxForwardSpeed)));

            Vector3 origin = body.position + transform.forward * 2f + transform.up * 0.30f;
            if (!TryGetBlockingHit(
                    origin,
                    transform.forward,
                    lookAhead,
                    0.70f,
                    true,
                    out RaycastHit hit))
            {
                obstacleAvoidHoldTimer = 0f;
                obstacleAvoidDirection = 0f;
                return;
            }

            Vector3 localHit = transform.InverseTransformPoint(hit.point);
            float avoidDirection = Mathf.Abs(localHit.x) > 0.20f
                ? -Mathf.Sign(localHit.x)
                : Mathf.Abs(avoidanceBias) > 0.01f
                    ? Mathf.Sign(avoidanceBias)
                    : (actualRacePosition % 2 == 0 ? -1f : 1f);

            float urgency = 1f - Mathf.Clamp01(hit.distance / lookAhead);

            // Không giật mạnh khỏi racing line. Đây là điểm khác biệt quan trọng.
            float influence = Mathf.Lerp(0.12f, 0.42f, urgency);
            steering = Mathf.Clamp(steering + avoidDirection * influence, -1f, 1f);

            // Vật cản càng gần càng giảm tốc, giống bản cũ nhưng ít cực đoan hơn.
            targetSpeed *= Mathf.Lerp(1f, 0.58f, urgency);
        }
        #endregion

        private bool TryGetBlockingHit(
            Vector3 origin,
            Vector3 direction,
            float distance,
            float radius,
            bool includeCars,
            out RaycastHit bestHit)
        {
            bestHit = new RaycastHit();
            bool found = false;
            float bestDistance = float.PositiveInfinity;

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                radius,
                direction.normalized,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                Collider col = hit.collider;
                if (col == null || IsSelfCollider(col) || IsRampCollider(col)) continue;

                CarController hitCar = col.GetComponentInParent<CarController>();
                if (hitCar != null && !includeCars) continue;

                // Bỏ qua trigger hoặc các decal / vạch kẻ đường / finish line
                if (col.isTrigger) continue;
                string colName = col.name;
                if (colName.IndexOf("checker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    colName.IndexOf("finish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    colName.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    colName.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    colName.IndexOf("asphalt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                // Bỏ mặt đường/dốc nhẹ; sensor chỉ quan tâm vật chắn phía trước.
                if (hitCar == null && Vector3.Dot(hit.normal, Vector3.up) > 0.55f) continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }

            return found;
        }

        private bool IsSelfCollider(Collider col)
        {
            if (col == null) return false;
            CarController owner = col.GetComponentInParent<CarController>();
            return owner != null && owner == carController;
        }

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

                // Brake becomes reverse acceleration automatically at low speed in CarController.
                // CarController also flips steering response when ForwardSpeed is negative.
                float recoverySteering = -Mathf.Sign(Mathf.Abs(localTarget.x) > 0.05f ? localTarget.x : 1f);
                inputProvider.SetInput(recoverySteering, 0f, 1f);
                inputProvider.SetDrift(false);
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
                    float reverseSteering = -Mathf.Sign(Mathf.Abs(localTarget.x) > 0.05f ? localTarget.x : 1f);
                    inputProvider.SetInput(reverseSteering, 0f, 1f);
                    inputProvider.SetDrift(false);
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
            carController?.CancelAirAction();
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

        private void OnDisable()
        {
            if (inputProvider != null)
            {
                inputProvider.SetInput(0f, 0f, 0f);
                inputProvider.SetDrift(false);
            }

            if (carController != null)
            {
                carController.SetSlipstreamBonus(0f);
            }
        }

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
