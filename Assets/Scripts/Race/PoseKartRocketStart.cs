using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using TMPro;
using PoseKart.Vehicle;
using PoseKart.Input;
using PoseKart.UI;
using PoseKart.AI;

namespace PoseKart.Race
{
    public enum RocketStartQuality
    {
        None = 0,
        TooEarly = 1,
        DisqualifiedEarly = TooEarly,
        Good = 2,
        Perfect = 3,
        TooLate = 4,
        NoBoost = 5
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

        [Header("AI Rocket Start")]
        [Tooltip("Allows server-owned AI to enter the same authoritative Rocket Start flow as human racers.")]
        [SerializeField] private bool enableAiRocketStart = true;
        [Tooltip("0 = inconsistent/no boost, 1 = highly reliable. AI still has a chance to miss and will not always Perfect.")]
        [SerializeField, Range(0f, 1f)] private float aiSkill = 0.55f;
        [Tooltip("Maximum deterministic timing variation applied to AI attempts.")]
        [SerializeField, Range(0f, 0.15f)] private float aiTimingJitter = 0.05f;

        [Header("Visual & Effects References")]
        [SerializeField] private ParticleSystem[] exhaustParticles;
        [Tooltip("Optional one-shot/looping Rocket Start VFX prefab. It is instantiated locally under the anchor when the Server-approved result arrives.")]
        [SerializeField] private GameObject rocketStartVfxPrefab;
        [Tooltip("Muzzle/wheel anchor for the separate Rocket Start VFX prefab. Falls back to this vehicle transform when empty.")]
        [SerializeField] private Transform rocketStartVfxAnchor;
        [SerializeField, Min(0.1f)] private float rocketStartVfxLifetime = 1.25f;

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
        private PoseKartNetworkItemBridge networkBridge;
        private bool countdownCycleActive;
        private bool authoritativeInputRegistered;
        private float authoritativeSecondsBeforeGo = -1f;
        private bool resultPublished;
        private bool resultApplied;
        private bool aiWillAttempt;
        private float aiTargetSecondsBeforeGo;
        private RaceManager subscribedRaceManager;
        private GameObject activeRocketStartVfx;
        private bool preparingVisualActive;

        public RocketStartQuality Quality => quality;
        public bool IsPreparingVisual =>
            preparingVisualActive && !resultPublished && !resultApplied;

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
            SubscribeRaceManager();
            ResetState();
        }

        private void OnDisable()
        {
            if (subscribedRaceManager != null)
            {
                subscribedRaceManager.RaceStartedEvent -= HandleRaceStarted;
                subscribedRaceManager = null;
            }

            StopRocketStartVfx();
        }

        public void ResolveReferences()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
            if (networkBridge == null) networkBridge = GetComponent<PoseKartNetworkItemBridge>();
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
            authoritativeInputRegistered = false;
            authoritativeSecondsBeforeGo = -1f;
            resultPublished = false;
            resultApplied = false;
            aiWillAttempt = false;
            aiTargetSecondsBeforeGo = -1f;
            preparingVisualActive = false;
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

            StopRocketStartVfx();
        }

        private void BeginCountdownCycle()
        {
            ResetState();
            countdownCycleActive = true;

            if (IsAiRacer())
                PlanAiAttempt();
        }

        private void Update()
        {
            UpdateFeedbackAnimation();

            if (raceManager == null)
                ResolveReferences();
            if (raceManager == null)
                return;
            SubscribeRaceManager();

            if (!raceManager.IsInCountdown)
            {
                // Waiting means a new race start may be armed later. Keep the
                // component safe for restart/cancel without touching Racing.
                if (raceManager.State != RaceState.Racing && countdownCycleActive)
                {
                    ResetState();
                    countdownCycleActive = false;
                }
                return;
            }

            if (!countdownCycleActive)
                BeginCountdownCycle();

            if (!CanReadLocalInput())
                return;

            float secondsUntilGo = raceManager.SecondsUntilGo;

            if (IsAiRacer())
            {
                if (enableAiRocketStart && aiWillAttempt && !inputRegistered &&
                    secondsUntilGo <= aiTargetSecondsBeforeGo && secondsUntilGo > 0f)
                {
                    SubmitAttempt();
                }
                return;
            }

            // Holding the existing accelerator is intentional. The first
            // observed hold is sent once; the Server measures its own receipt
            // time against the shared countdown clock.
            if (!inputRegistered && IsAccelerationHeld())
                SubmitAttempt();
        }

        private void SubscribeRaceManager()
        {
            if (subscribedRaceManager == raceManager)
                return;

            if (subscribedRaceManager != null)
                subscribedRaceManager.RaceStartedEvent -= HandleRaceStarted;

            subscribedRaceManager = raceManager;
            if (subscribedRaceManager != null)
                subscribedRaceManager.RaceStartedEvent += HandleRaceStarted;
        }

        private bool IsAiRacer()
        {
            // The human NetworkPlayer prefab also carries an AIInputProvider
            // component for reuse, but it is disabled there. Only an enabled
            // provider identifies a server-driven AI racer.
            AIInputProvider aiProvider = GetComponent<AIInputProvider>();
            return aiProvider != null && aiProvider.enabled;
        }

        private void SubmitAttempt()
        {
            if (inputRegistered || resultPublished)
                return;

            inputRegistered = true;

            // Bật phản hồi bánh xe ngay trên máy người bấm để không phải chờ
            // round-trip tới Server. Đây chỉ là visual preparation; kết quả
            // Rocket Start và boost gameplay vẫn do Server xác nhận.
            SetPreparingVisual(true);

            if (networkBridge != null &&
                networkBridge.IsSpawned &&
                PoseKartNetworkItemBridge.IsOnlineRace)
            {
                networkBridge.RequestRocketStartInput();
                return;
            }

            RegisterAuthoritativeInput(GetRaceClockNow());
        }

        /// <summary>
        /// Kiểm tra xem người chơi có đang giữ bất kỳ phím/cử chỉ ga nào ở frame hiện tại không.
        /// </summary>
        private bool IsAccelerationHeld()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed || kb.spaceKey.isPressed)
                {
                    return true;
                }
            }

            Gamepad pad = Gamepad.current;
            if (pad != null && pad.rightTrigger.isPressed)
            {
                return true;
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

        private double GetRaceClockNow()
        {
            PoseKart.Multiplayer.PoseKartNetworkRaceClock clock =
                PoseKart.Multiplayer.PoseKartNetworkRaceClock.Instance;
            if (clock != null && clock.IsSpawned)
                return clock.ServerNow;

            return Time.timeAsDouble;
        }

        /// <summary>
        /// ĐƯỢC GỌI DUY NHẤT TẠI THỜI KHẮC RACE BẮT ĐẦU (GO!).
        /// </summary>
        private void HandleRaceStarted()
        {
            bool isOnline = NetworkManager.Singleton != null &&
                            NetworkManager.Singleton.IsListening;

            // Only the Server finalizes network results. Offline keeps the
            // same component and event flow without creating another start path.
            if (!isOnline || NetworkManager.Singleton.IsServer)
                FinalizeAtGo();
        }

        private void PlanAiAttempt()
        {
            aiWillAttempt = false;
            aiTargetSecondsBeforeGo = -1f;

            if (!enableAiRocketStart)
                return;

            float firstRoll = Deterministic01(17);
            float noBoostChance = Mathf.Lerp(0.48f, 0.04f, aiSkill);
            if (firstRoll < noBoostChance)
                return;

            float perfectChance = Mathf.Lerp(0.08f, 0.72f, aiSkill);
            float secondRoll = Deterministic01(31);
            bool perfect = secondRoll < perfectChance;
            float jitter = (Deterministic01(47) * 2f - 1f) * aiTimingJitter;

            if (perfect)
            {
                aiTargetSecondsBeforeGo = Mathf.Clamp(
                    perfectWindow * 0.55f + jitter,
                    0.02f,
                    Mathf.Max(0.02f, perfectWindow));
            }
            else
            {
                aiTargetSecondsBeforeGo = Mathf.Clamp(
                    Mathf.Lerp(perfectWindow + 0.03f, goodWindow - 0.03f, Deterministic01(61)) + jitter,
                    perfectWindow + 0.01f,
                    Mathf.Max(perfectWindow + 0.01f, goodWindow));
            }

            aiWillAttempt = true;
        }

        private float Deterministic01(int salt)
        {
            uint hash = (uint)(GetInstanceID() * 397 ^ salt);
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
                hash ^= (uint)networkObject.NetworkObjectId;

            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777215f;
        }

        /// <summary>
        /// Called only by the authoritative Server-side bridge or the offline
        /// local path. The caller supplies no client tier or boost values.
        /// </summary>
        public bool RegisterAuthoritativeInput(double serverTime)
        {
            if (resultPublished || authoritativeInputRegistered || raceManager == null)
                return false;

            if (!raceManager.IsInCountdown)
                return false;

            float secondsBeforeGo = raceManager.SecondsUntilGo;
            if (float.IsNaN(secondsBeforeGo) || float.IsInfinity(secondsBeforeGo))
                return false;

            authoritativeInputRegistered = true;
            authoritativeSecondsBeforeGo = Mathf.Max(0f, secondsBeforeGo);
            registeredTimeBeforeGo = authoritativeSecondsBeforeGo;

            Debug.Log(
                $"[RocketStart] input accepted | racer={name} | " +
                $"serverTime={serverTime:0.000} | " +
                $"secondsBeforeGo={authoritativeSecondsBeforeGo:0.000}",
                this);
            return true;
        }

        public void SetPreparingVisual(bool active)
        {
            preparingVisualActive = active &&
                                    !resultPublished &&
                                    !resultApplied;
        }

        public void FinalizeAtGo()
        {
            if (resultPublished)
                return;

            resultPublished = true;
            preparingVisualActive = false;

            RocketStartQuality finalQuality = RocketStartQuality.NoBoost;
            float impulse = 0f;
            float duration = 0f;

            if (authoritativeInputRegistered)
            {
                if (authoritativeSecondsBeforeGo > goodWindow)
                {
                    finalQuality = RocketStartQuality.TooEarly;
                    heldTooEarly = true;
                }
                else if (authoritativeSecondsBeforeGo <= 0.001f)
                {
                    finalQuality = RocketStartQuality.TooLate;
                }
                else if (authoritativeSecondsBeforeGo <= perfectWindow)
                {
                    finalQuality = RocketStartQuality.Perfect;
                    impulse = perfectImpulse;
                    duration = perfectDuration;
                }
                else
                {
                    finalQuality = RocketStartQuality.Good;
                    impulse = goodImpulse;
                    duration = goodDuration;
                }
            }

            quality = finalQuality;

            Debug.Log(
                $"[RocketStart] result | racer={name} | tier={finalQuality} | " +
                $"authoritative=true | boost={(impulse > 0f ? "yes" : "no")}",
                this);

            if (networkBridge != null && networkBridge.IsSpawned && networkBridge.IsServer)
            {
                networkBridge.ServerPublishRocketStartResult(
                    finalQuality,
                    impulse,
                    duration);
            }
            else
            {
                ApplyReplicatedResult(finalQuality, impulse, duration, true);
            }
        }

        /// <summary>
        /// Applies a Server-approved result. Gameplay is applied only on the
        /// physics authority; all peers may still show the launch presentation.
        /// </summary>
        public void ApplyReplicatedResult(
            RocketStartQuality finalQuality,
            float impulse,
            float duration,
            bool applyGameplay)
        {
            if (resultApplied)
                return;

            resultApplied = true;
            preparingVisualActive = false;
            quality = finalQuality;
            heldTooEarly = finalQuality == RocketStartQuality.TooEarly;

            bool isLocalHuman = IsLocalHumanOwner();
            bool hasBoost = finalQuality == RocketStartQuality.Perfect ||
                            finalQuality == RocketStartQuality.Good;

            if (applyGameplay && hasBoost && carController != null)
            {
                carController.ApplySpeedBoost(duration, impulse);
            }

            PlayLaunchPresentation(finalQuality, isLocalHuman);
        }

        private bool CanReadLocalInput()
        {
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
                return true;

            if (networkObject.IsPlayerObject)
                return networkObject.IsOwner;

            // AI is server-owned and must be evaluated only by the Server.
            return NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsServer;
        }

        private bool IsLocalHumanOwner()
        {
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
                return !IsAiRacer();

            return networkObject.IsPlayerObject && networkObject.IsOwner;
        }

        private void PlayLaunchPresentation(
            RocketStartQuality finalQuality,
            bool showLocalFeedback)
        {
            string bannerText = null;
            Color textColor = Color.white;
            AudioClip sound = null;

            switch (finalQuality)
            {
                case RocketStartQuality.Perfect:
                    bannerText = "PERFECT START!";
                    textColor = new Color(0.15f, 0.95f, 1f);
                    sound = perfectStartClip;
                    break;
                case RocketStartQuality.Good:
                    bannerText = "GOOD START!";
                    textColor = new Color(1f, 0.85f, 0.2f);
                    sound = goodStartClip;
                    break;
                case RocketStartQuality.TooEarly:
                    bannerText = "TOO EARLY!";
                    textColor = new Color(1f, 0.35f, 0.35f);
                    sound = stallClip;
                    break;
                case RocketStartQuality.TooLate:
                    bannerText = "TOO LATE!";
                    textColor = new Color(1f, 0.55f, 0.25f);
                    sound = stallClip;
                    break;
                case RocketStartQuality.NoBoost:
                    bannerText = "NO BOOST";
                    textColor = new Color(0.75f, 0.75f, 0.75f);
                    break;
            }

            bool launchSucceeded = finalQuality == RocketStartQuality.Perfect ||
                                   finalQuality == RocketStartQuality.Good;

            // Prefer the separately assigned Rocket Start prefab. The existing
            // exhaust list remains a compatibility fallback for vehicles that
            // have not been given the new prefab yet.
            if (launchSucceeded && rocketStartVfxPrefab != null)
            {
                PlayRocketStartVfxPrefab();
            }
            else if (launchSucceeded && exhaustParticles != null)
            {
                for (int i = 0; i < exhaustParticles.Length; i++)
                {
                    ParticleSystem ps = exhaustParticles[i];
                    if (ps != null &&
                        (ps.name.IndexOf("exhaust", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ps.name.IndexOf("tube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ps.name.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        ps.Play(true);
                    }
                }
            }

            if (sound != null && PoseKart.Audio.AudioManager.Instance != null)
            {
                PoseKart.Audio.AudioManager.Instance.PlaySFX(sound, launchAudioVolume);
            }

            if (showLocalFeedback)
            {
                if (cinematicCamera != null)
                    cinematicCamera.PlayImpact(0.85f);

                if (!string.IsNullOrEmpty(bannerText))
                    ShowFeedback(bannerText, textColor, null);
            }
        }

        private void PlayRocketStartVfxPrefab()
        {
            StopRocketStartVfx();

            Transform anchor = rocketStartVfxAnchor != null
                ? rocketStartVfxAnchor
                : transform;

            activeRocketStartVfx = Instantiate(
                rocketStartVfxPrefab,
                anchor,
                false);
            activeRocketStartVfx.transform.localPosition = Vector3.zero;
            activeRocketStartVfx.transform.localRotation = Quaternion.identity;

            if (rocketStartVfxLifetime > 0f)
            {
                Destroy(activeRocketStartVfx, rocketStartVfxLifetime);
            }
        }

        private void StopRocketStartVfx()
        {
            if (activeRocketStartVfx == null)
                return;

            Destroy(activeRocketStartVfx);
            activeRocketStartVfx = null;
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
