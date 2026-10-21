using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PoseKart.Vehicle;
using PoseKart.AI;

namespace PoseKart.Race
{
    /// <summary>
    /// Detects when the player vehicle is traveling against the intended track direction.
    /// Uses the 280-waypoint RacingLine / Checkpoint track segment tangent to calculate
    /// genuine track-relative travel direction.
    /// Eliminates false positives (reversing out of walls, drifting, airborne stunts, collision spins).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartWrongWayDetector : MonoBehaviour
    {
        [Header("Vehicle & Track References")]
        [SerializeField] private CarController carController;
        [SerializeField] private Rigidbody carBody;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private RacingLine racingLine;
        [SerializeField] private CheckpointManager checkpointManager;

        [Header("Detection Thresholds (Inspector Configurable)")]
        [Tooltip("Tốc độ lùi/ngược chiều tối thiểu (km/h) để tính là đi ngược.")]
        [SerializeField, Range(5f, 30f)] private float minWrongWaySpeedKmh = 10f;
        [Tooltip("Thời gian duy trì đi ngược liên tục (giây) trước khi bật cảnh báo (chống nhấp nháy khi bị va chạm xoay xe).")]
        [SerializeField, Range(0.5f, 2.5f)] private float wrongWaySustainedDuration = 1.2f;
        [Tooltip("Thời gian duy trì đi đúng chiều liên tục (giây) để xác nhận hồi phục và tắt cảnh báo.")]
        [SerializeField, Range(0.2f, 1.5f)] private float recoveryDuration = 0.5f;
        [Tooltip("Ngưỡng góc ngược chiều (Dot product giữa hướng đầu xe và hướng track). -0.2 tương đương góc > 100 độ.")]
        [SerializeField, Range(-0.9f, 0.1f)] private float wrongHeadingThreshold = -0.2f;

        [Header("UI References (Editable Hierarchy)")]
        [SerializeField] private GameObject alertRoot;
        [SerializeField] private CanvasGroup alertCanvasGroup;
        [SerializeField] private TMP_Text alertText;
        [SerializeField] private RectTransform directionArrow;
        [SerializeField] private Image warningIcon;

        [Header("Warning Presentation & Animation")]
        [SerializeField] private float pulseSpeed = 4.5f;
        [SerializeField] private AudioClip warningSound;
        [SerializeField, Range(0f, 1f)] private float soundVolume = 0.85f;
        [SerializeField] private float audioRepeatInterval = 2.0f;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private bool isWrongWay = false;
        [SerializeField] private float wrongWayTimer = 0f;
        [SerializeField] private float recoveryTimer = 0f;
        [SerializeField] private float currentVelocityDot = 0f;
        [SerializeField] private float currentHeadingDot = 0f;
        [SerializeField] private float currentTrackSpeedKmh = 0f;
        [SerializeField] private string recommendedTurnDirection = "Straight";

        private int cachedWaypointIndex = 0;
        private float lastAudioPlayTime = -100f;

        public bool IsWrongWay => isWrongWay;
        public string RecommendedTurnDirection => recommendedTurnDirection;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResetState();
        }

        public void ResolveReferences()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (carBody == null && carController != null) carBody = carController.TargetBody;
            if (carBody == null) carBody = GetComponent<Rigidbody>();
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
            if (racingLine == null) racingLine = FindFirstObjectByType<RacingLine>();
            if (checkpointManager == null) checkpointManager = FindFirstObjectByType<CheckpointManager>();

            if (alertRoot == null)
            {
                GameObject hud = GameObject.Find("ArcadeHUD_Root");
                Transform found = hud != null ? hud.transform.Find("WrongWayAlert") : null;
                if (found != null)
                {
                    alertRoot = found.gameObject;
                    alertCanvasGroup = found.GetComponent<CanvasGroup>();
                    alertText = found.Find("WrongWayText")?.GetComponent<TMP_Text>();
                    directionArrow = found.Find("DirectionArrow")?.GetComponent<RectTransform>();
                    warningIcon = found.Find("WarningIcon")?.GetComponent<Image>();
                }
            }
        }

        public void ConfigureUI(GameObject root, CanvasGroup group, TMP_Text text, RectTransform arrow, Image icon)
        {
            alertRoot = root;
            alertCanvasGroup = group;
            alertText = text;
            directionArrow = arrow;
            warningIcon = icon;
        }

        private void ResetState()
        {
            isWrongWay = false;
            wrongWayTimer = 0f;
            recoveryTimer = 0f;
            SetAlertUIActive(false);
        }

        private void Update()
        {
            // 1. Chỉ kiểm tra trong khi cuộc đua đang diễn ra
            if (raceManager == null || raceManager.State != RaceState.Racing || raceManager.PlayerFinished)
            {
                if (isWrongWay)
                {
                    isWrongWay = false;
                    SetAlertUIActive(false);
                }
                wrongWayTimer = 0f;
                recoveryTimer = 0f;
                return;
            }

            if (carController == null || carBody == null)
            {
                return;
            }

            // 2. Loại trừ các trường hợp ngoại lệ (False Positives)
            // - Đang bị ăn chuối xoay xe
            // - Đang bay trên không / thực hiện Stunt thùng xoay
            if (carController.IsBananaStunned || !carController.IsGrounded || carController.IsAirAction)
            {
                wrongWayTimer = 0f;
                return;
            }

            // 3. Tính toán vector tiếp tuyến của đường đua tại vị trí hiện tại
            Vector3 trackTangent = GetLocalTrackTangent(carBody.position);
            if (trackTangent.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 carForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 carVelocity = carBody.linearVelocity;
            carVelocity.y = 0f;

            // Vận tốc chiếu dọc theo chiều đường ray (dương = chạy xuôi, âm = chạy ngược)
            float speedAlongTrack = Vector3.Dot(carVelocity, trackTangent);
            currentTrackSpeedKmh = speedAlongTrack * 3.6f;
            currentHeadingDot = Vector3.Dot(carForward, trackTangent);
            currentVelocityDot = carVelocity.magnitude > 0.5f ? Vector3.Dot(carVelocity.normalized, trackTangent) : currentHeadingDot;

            float minSpeedMs = minWrongWaySpeedKmh / 3.6f;

            // 4. Đánh giá trạng thái chạy ngược
            // Điều kiện ngược: Xe đang quay đầu ngược lại (currentHeadingDot < threshold)
            // VÀ thực sự đang di chuyển ngược chiều đường ray với tốc độ >= minWrongWaySpeedKmh
            bool currentlyMovingWrongWay = (currentHeadingDot < wrongHeadingThreshold) && (speedAlongTrack < -minSpeedMs);

            if (currentlyMovingWrongWay)
            {
                recoveryTimer = 0f;
                wrongWayTimer += Time.deltaTime;

                if (wrongWayTimer >= wrongWaySustainedDuration)
                {
                    if (!isWrongWay)
                    {
                        isWrongWay = true;
                        SetAlertUIActive(true);
                        PlayWarningAudio();
                    }
                }
            }
            else
            {
                wrongWayTimer = 0f;

                // Điều kiện phục hồi: Xe quay lại đúng chiều (currentHeadingDot > 0.3)
                // và đang di chuyển xuôi chiều track
                bool currentlyRecovering = currentHeadingDot > 0.3f && speedAlongTrack > 1.5f;

                if (currentlyRecovering)
                {
                    recoveryTimer += Time.deltaTime;
                    if (recoveryTimer >= recoveryDuration)
                    {
                        if (isWrongWay)
                        {
                            isWrongWay = false;
                            SetAlertUIActive(false);
                        }
                    }
                }
                else
                {
                    recoveryTimer = 0f;
                }
            }

            // 5. Cập nhật hiệu ứng UI và mũi tên chỉ đường khi cảnh báo đang bật
            if (isWrongWay)
            {
                UpdateAlertPresentation(trackTangent, carForward);

                if (Time.time - lastAudioPlayTime >= audioRepeatInterval)
                {
                    PlayWarningAudio();
                }
            }
        }

        /// <summary>
        /// Lấy hướng tiếp tuyến chuẩn của đường đua tại vị trí xe bằng RacingLine (hoặc CheckpointManager dự phòng).
        /// </summary>
        private Vector3 GetLocalTrackTangent(Vector3 position)
        {
            if (racingLine != null && racingLine.Count >= 2)
            {
                // Cập nhật vị trí waypoint gần nhất
                racingLine.AdvanceProgress(ref cachedWaypointIndex, position, 12f);
                int next = (cachedWaypointIndex + 1) % racingLine.Count;
                Vector3 p0 = racingLine.GetPosition(cachedWaypointIndex);
                Vector3 p1 = racingLine.GetPosition(next);
                Vector3 dir = p1 - p0;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    return dir.normalized;
                }
            }

            if (checkpointManager != null && checkpointManager.CheckpointCount >= 2)
            {
                LapTracker tracker = carController.GetComponent<LapTracker>();
                int exp = tracker != null ? tracker.ExpectedCheckpoint : 1;
                int prev = (exp - 1 + checkpointManager.CheckpointCount) % checkpointManager.CheckpointCount;
                Checkpoint cpPrev = checkpointManager.GetCheckpoint(prev);
                Checkpoint cpNext = checkpointManager.GetCheckpoint(exp);
                if (cpPrev != null && cpNext != null)
                {
                    Vector3 dir = cpNext.transform.position - cpPrev.transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        return dir.normalized;
                    }
                }
            }

            return transform.forward;
        }

        private void UpdateAlertPresentation(Vector3 trackTangent, Vector3 carForward)
        {
            // Hiệu ứng nhấp nháy chữ đỏ
            if (alertCanvasGroup != null)
            {
                float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
                alertCanvasGroup.alpha = Mathf.Lerp(0.35f, 1f, pulse);
            }

            // Tính hướng quay đầu tối ưu (Rẽ Trái hay Rẽ Phải để quay lại track)
            Vector3 cross = Vector3.Cross(carForward, trackTangent);
            float steerSide = cross.y; // > 0: rẽ phải, < 0: rẽ trái

            if (directionArrow != null)
            {
                // Xoay mũi tên chỉ về hướng cần bẻ lái
                float targetZAngle = steerSide >= 0f ? -90f : 90f;
                directionArrow.localRotation = Quaternion.Euler(0f, 0f, targetZAngle);
            }

            recommendedTurnDirection = steerSide >= 0f ? "Turn Right" : "Turn Left";
        }

        private void SetAlertUIActive(bool active)
        {
            if (alertRoot != null)
            {
                alertRoot.SetActive(active);
            }
            if (alertCanvasGroup != null)
            {
                alertCanvasGroup.alpha = active ? 1f : 0f;
            }
        }

        private void PlayWarningAudio()
        {
            lastAudioPlayTime = Time.time;
            if (warningSound != null)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(warningSound, soundVolume);
            }
            else
            {
                // Fallback roulette tick sound as an urgent beep
                PoseKart.Audio.AudioManager.PlayRouletteTick();
            }
        }
    }
}
