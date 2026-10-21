using System;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using PoseKart.Vehicle;
using PoseKart.Input;
using PoseKart.UI;

namespace PoseKart.Race
{
    public enum RocketStartQuality
    {
        None = 0,
        DisqualifiedEarly = 1,
        Good = 2,
        Perfect = 3
    }

    /// <summary>
    /// Arcade-style Rocket Start mechanic inspired by Mario Kart and ZingSpeed.
    /// Synchronizes strictly with RaceManager's real countdown and executes at GO.
    /// Supports both Keyboard and MediaPipe Hand Control modes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartRocketStart : MonoBehaviour
    {
        [Header("Vehicle & Race References")]
        [SerializeField] private CarController carController;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private PoseKartHandInputProvider handProvider;
        [SerializeField] private PoseKartHandUdpReceiver udpReceiver;
        [SerializeField] private PoseKartCinematicCamera cinematicCamera;

        [Header("Timing Windows (Seconds before GO)")]
        [Tooltip("Cửa sổ bấm hoàn hảo (VD: 0.15s cuối cùng trước khi chuyển sang GO!).")]
        [SerializeField, Range(0.05f, 0.30f)] private float perfectWindow = 0.15f;
        [Tooltip("Cửa sổ bấm đạt (VD: 0.40s trước GO!, không bao gồm Perfect).")]
        [SerializeField, Range(0.15f, 0.70f)] private float goodWindow = 0.40f;
        [Tooltip("Nếu đè ga quá sớm trước khi cửa sổ mở sẽ bị huỷ Rocket Start (tránh exploit giữ lì phím).")]
        [SerializeField] private bool penalizeEarlyHold = true;

        [Header("Launch Physics Presets")]
        [SerializeField, Min(1f)] private float perfectImpulse = 13.5f;
        [SerializeField, Min(0.1f)] private float perfectDuration = 1.25f;
        [SerializeField, Min(1f)] private float goodImpulse = 7.5f;
        [SerializeField, Min(0.1f)] private float goodDuration = 0.95f;

        [Header("Visual & Effects References")]
        [SerializeField] private ParticleSystem[] exhaustParticles;

        [Header("UI Feedback (Editable Hierarchy)")]
        [SerializeField] private GameObject feedbackRoot;
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private CanvasGroup feedbackGroup;
        [SerializeField, Min(0.2f)] private float feedbackDisplayDuration = 1.4f;

        [Header("Audio Feedback")]
        [SerializeField] private AudioClip perfectStartClip;
        [SerializeField] private AudioClip goodStartClip;
        [SerializeField] private AudioClip stallClip;
        [SerializeField, Range(0f, 1f)] private float launchAudioVolume = 0.9f;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private RocketStartQuality quality = RocketStartQuality.None;
        [SerializeField] private bool heldTooEarly = false;
        [SerializeField] private bool inputRegistered = false;
        [SerializeField] private float registeredTimeBeforeGo = -1f;

        private int lastHandNitroId = 0;
        private bool wasHandThrottleHigh = false;
        private float feedbackFadeTimer = 0f;

        public RocketStartQuality Quality => quality;

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
            if (raceManager != null)
            {
                raceManager.RaceStartedEvent += HandleRaceStarted;
            }
            ResetState();
        }

        private void OnDisable()
        {
            if (raceManager != null)
            {
                raceManager.RaceStartedEvent -= HandleRaceStarted;
            }
        }

        public void ResolveReferences()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
            if (handProvider == null) handProvider = GetComponent<PoseKartHandInputProvider>();
            if (udpReceiver == null) udpReceiver = FindFirstObjectByType<PoseKartHandUdpReceiver>();
            if (cinematicCamera == null) cinematicCamera = FindFirstObjectByType<PoseKartCinematicCamera>();

            if (exhaustParticles == null || exhaustParticles.Length == 0)
            {
                exhaustParticles = GetComponentsInChildren<ParticleSystem>(true);
            }

            if (feedbackRoot == null)
            {
                GameObject hud = GameObject.Find("ArcadeHUD_Root");
                Transform found = hud != null ? hud.transform.Find("RocketStart_Feedback") : null;
                if (found != null)
                {
                    feedbackRoot = found.gameObject;
                    feedbackText = found.GetComponentInChildren<TMP_Text>();
                    feedbackGroup = found.GetComponent<CanvasGroup>();
                }
            }
        }

        public void ConfigureUI(GameObject root, TMP_Text text, CanvasGroup group)
        {
            feedbackRoot = root;
            feedbackText = text;
            feedbackGroup = group;
        }

        private void ResetState()
        {
            quality = RocketStartQuality.None;
            heldTooEarly = false;
            inputRegistered = false;
            registeredTimeBeforeGo = -1f;
            wasHandThrottleHigh = false;
            if (udpReceiver != null)
            {
                lastHandNitroId = udpReceiver.NitroPressId;
            }
            if (feedbackGroup != null)
            {
                feedbackGroup.alpha = 0f;
            }
            if (feedbackRoot != null)
            {
                feedbackRoot.SetActive(false);
            }
        }

        private void Update()
        {
            UpdateFeedbackAnimation();

            if (raceManager == null) return;

            // Chỉ lắng nghe và phân tích input trong giai đoạn Countdown
            if (!raceManager.IsInCountdown)
            {
                return;
            }

            float secondsUntilGo = raceManager.SecondsUntilGo;

            // 1. Kiểm tra giữ ga quá sớm trước khi cửa sổ mở
            if (penalizeEarlyHold && !heldTooEarly)
            {
                if (secondsUntilGo > goodWindow && IsAcceleratingCurrentFrame())
                {
                    heldTooEarly = true;
                    quality = RocketStartQuality.DisqualifiedEarly;
                }
            }

            // 2. Nếu đã bị phạm quy giữ sớm, không cho nhận thêm
            if (heldTooEarly)
            {
                return;
            }

            // 3. Kiểm tra cú bấm tăng tốc đúng nhịp trong cửa sổ hợp lệ
            if (!inputRegistered && secondsUntilGo <= goodWindow && secondsUntilGo > 0f)
            {
                if (WasAccelerationTriggeredThisFrame())
                {
                    inputRegistered = true;
                    registeredTimeBeforeGo = secondsUntilGo;

                    if (secondsUntilGo <= perfectWindow)
                    {
                        quality = RocketStartQuality.Perfect;
                    }
                    else
                    {
                        quality = RocketStartQuality.Good;
                    }
                }
            }
        }

        /// <summary>
        /// Kiểm tra xem người chơi có đang giữ bất kỳ phím/cử chỉ ga nào ở frame hiện tại không.
        /// </summary>
        private bool IsAcceleratingCurrentFrame()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed || kb.spaceKey.isPressed)
                {
                    return true;
                }
            }

            if (udpReceiver != null && udpReceiver.IsTracking)
            {
                if (udpReceiver.Throttle > 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Phát hiện cú nhấn ga mới toanh (fresh press / surge) trong frame này.
        /// Hỗ trợ cả Bàn phím lẫn MediaPipe webcam gesture.
        /// </summary>
        private bool WasAccelerationTriggeredThisFrame()
        {
            // Keyboard Trigger
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                {
                    return true;
                }
            }

            // MediaPipe Hand Control Trigger
            if (udpReceiver != null && udpReceiver.IsTracking)
            {
                // Cách 1: Nắm tay (Fist / Nitro gesture event)
                if (udpReceiver.NitroPressId > lastHandNitroId)
                {
                    lastHandNitroId = udpReceiver.NitroPressId;
                    return true;
                }

                // Cách 2: Vung tay ga (Throttle surge từ < 0.25 vọt lên >= 0.65)
                float handThrottle = udpReceiver.Throttle;
                bool isHigh = handThrottle >= 0.65f;
                if (isHigh && !wasHandThrottleHigh)
                {
                    wasHandThrottleHigh = true;
                    return true;
                }
                if (handThrottle < 0.30f)
                {
                    wasHandThrottleHigh = false;
                }
            }

            return false;
        }

        /// <summary>
        /// ĐƯỢC GỌI DUY NHẤT TẠI THỜI KHẮC RACE BẮT ĐẦU (GO!).
        /// </summary>
        private void HandleRaceStarted()
        {
            if (quality == RocketStartQuality.Perfect)
            {
                ExecuteLaunch(perfectImpulse, perfectDuration, "PERFECT START!", perfectStartClip, new Color(0.15f, 0.95f, 1f));
            }
            else if (quality == RocketStartQuality.Good)
            {
                ExecuteLaunch(goodImpulse, goodDuration, "GOOD START!", goodStartClip, new Color(1f, 0.85f, 0.2f));
            }
            else if (heldTooEarly)
            {
                ShowFeedback("TOO EARLY!", new Color(1f, 0.35f, 0.35f), stallClip);
            }
        }

        private void ExecuteLaunch(float impulse, float duration, string bannerText, AudioClip sound, Color textColor)
        {
            if (carController != null)
            {
                // Áp dụng tăng tốc an toàn qua kiến trúc speed boost hiện có của CarController
                carController.ApplySpeedBoost(duration, impulse);
            }

            // Kích hoạt hiệu ứng ống xả
            if (exhaustParticles != null)
            {
                for (int i = 0; i < exhaustParticles.Length; i++)
                {
                    ParticleSystem ps = exhaustParticles[i];
                    if (ps != null && ps.name.IndexOf("exhaust", StringComparison.OrdinalIgnoreCase) >= 0
                        || (ps != null && ps.name.IndexOf("tube", StringComparison.OrdinalIgnoreCase) >= 0)
                        || (ps != null && ps.name.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        ps.Play(true);
                    }
                }
            }

            // Kích hoạt camera punch
            if (cinematicCamera != null)
            {
                cinematicCamera.PlayImpact(0.85f);
            }

            // Hiển thị chữ chúc mừng và âm thanh
            ShowFeedback(bannerText, textColor, sound);
        }

        private void ShowFeedback(string text, Color color, AudioClip clip)
        {
            if (feedbackText != null)
            {
                feedbackText.text = text;
                feedbackText.color = color;
            }

            if (feedbackRoot != null)
            {
                feedbackRoot.SetActive(true);
            }

            if (feedbackGroup != null)
            {
                feedbackGroup.alpha = 1f;
            }

            feedbackFadeTimer = feedbackDisplayDuration;

            if (clip != null)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(clip, launchAudioVolume);
            }
        }

        private void UpdateFeedbackAnimation()
        {
            if (feedbackFadeTimer > 0f)
            {
                feedbackFadeTimer -= Time.deltaTime;
                if (feedbackGroup != null)
                {
                    if (feedbackFadeTimer < 0.4f)
                    {
                        feedbackGroup.alpha = Mathf.Clamp01(feedbackFadeTimer / 0.4f);
                    }
                    else
                    {
                        feedbackGroup.alpha = 1f;
                    }
                }

                if (feedbackFadeTimer <= 0f)
                {
                    if (feedbackRoot != null)
                    {
                        feedbackRoot.SetActive(false);
                    }
                }
            }
        }
    }
}
