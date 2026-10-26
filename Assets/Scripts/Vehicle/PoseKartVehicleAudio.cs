using PoseKart.Input;
using PoseKart.Audio;
using PoseKart.Race;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Layered arcade-racing audio for player and AI vehicles.
    /// The controller reads real vehicle input/state; it never changes vehicle physics.
    /// Missing clips are valid and simply leave that layer silent.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PoseKartVehicleAudio : MonoBehaviour
    {
        public enum VehicleAudioStyle { Auto, PlayerRally, Sport, Buggy, AI }
        public enum EngineAudioMode
        {
            AutomaticFallback,
            LegacyOnly,
            MultiSampleRPMBank,
            SpeedSynchronizedArcade,
            SpeedLockedRPM
        }

        [Header("Vehicle profile")]
        [SerializeField] private VehicleAudioStyle style = VehicleAudioStyle.Auto;
        [SerializeField] private bool isPlayer;

        [Header("Engine clips")]
        [Tooltip("Legacy single-loop slot. It is also used as a safe fallback for the idle layer.")]
        [SerializeField] private AudioClip engineLoop;
        [SerializeField] private AudioClip engineIdleClip;
        [SerializeField] private AudioClip engineLowRpmClip;
        [SerializeField] private AudioClip engineHighRpmClip;
        [SerializeField] private AudioClip accelerationClip;
        [Header("16-step RPM sound bank")]
        [SerializeField] private EngineAudioMode engineAudioMode = EngineAudioMode.AutomaticFallback;
        [SerializeField] private PoseKartEngineSoundBank engineSoundBank;
        [SerializeField, Min(1)] private int minimumSampleRpm = 1000;
        [SerializeField, Min(1)] private int maximumSampleRpm = 8500;
        [SerializeField, Min(1)] private int rpmStep = 500;
        [SerializeField, Range(0.5f, 1.5f)] private float minimumPitchCorrection = 0.80f;
        [SerializeField, Range(0.5f, 1.5f)] private float maximumPitchCorrection = 1.25f;
        [SerializeField, Min(0.1f)] private float pitchSmoothing = 8f;
        [SerializeField, Range(0f, 1f)] private float engineVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float accelerationLayerVolume = 0.28f;
        [SerializeField, Min(1f)] private float maxSpeedKmh = 140f;
        [InspectorName("Idle RPM")]
        [SerializeField, Min(400f)] private float idleRpm = 1000f;
        [InspectorName("Maximum Audio RPM")]
        [SerializeField, Min(1000f)] private float maximumRpm = 8500f;
        [SerializeField, Min(0.1f)] private float rpmRiseSpeed = 4.8f;
        [SerializeField, Min(0.1f)] private float rpmFallSpeed = 2.7f;
        [SerializeField, Range(0f, 1f)] private float throttleRpmInfluence = 0.68f;
        [SerializeField, Range(0f, 1f)] private float speedRpmInfluence = 0.42f;
        [SerializeField, Min(0.1f)] private float crossfadeResponse = 7f;
        [SerializeField, Range(0.5f, 2f)] private float minimumEnginePitch = 0.82f;
        [SerializeField, Range(0.5f, 2.5f)] private float maximumEnginePitch = 1.55f;

        [Header("Speed-synchronized arcade RPM")]
        [Tooltip("Smooths Rigidbody speed before it drives the audio RPM target.")]
        [SerializeField, Min(0.01f)] private float speedSmoothingTime = 0.05f;
        [Tooltip("Smooth acceleration of the audio RPM target without changing vehicle physics.")]
        [SerializeField, Min(0.01f)] private float rpmRiseSmoothingTime = 0.12f;
        [Tooltip("Smooth deceleration/coasting of the audio RPM target.")]
        [SerializeField, Min(0.01f)] private float rpmFallSmoothingTime = 0.20f;
        [Tooltip("Small throttle-only RPM addition while the vehicle is moving. Speed remains the primary driver.")]
        [SerializeField, Range(0f, 0.25f)] private float movingThrottleBoost = 0.12f;
        [Tooltip("Maximum simulated RPM while stationary and holding throttle.")]
        [SerializeField, Min(1000f)] private float stationaryRevLimit = 3200f;
        [SerializeField, Min(0f)] private float stationarySpeedThresholdKmh = 1f;

        [Header("Reverse engine audio")]
        [Tooltip("Lower RPM floor used only while the Rigidbody is actually moving backwards.")]
        [SerializeField, Min(400f)] private float reverseAudioRpmMinimum = 1000f;
        [Tooltip("Upper RPM cap used only while the Rigidbody is actually moving backwards.")]
        [SerializeField, Min(500f)] private float reverseAudioRpmMaximum = 2000f;
        [Tooltip("Reverse speed in km/h that reaches Reverse Audio RPM Maximum.")]
        [SerializeField, Min(1f)] private float reverseSpeedReferenceKmh = 35f;
        [Tooltip("Signed-speed dead zone used to avoid forward/reverse chatter near rest.")]
        [SerializeField, Min(0f)] private float directionChangeDeadZoneKmh = 1.5f;
        [Tooltip("Smoothing time used when entering or leaving the reverse RPM range.")]
        [SerializeField, Min(0.01f)] private float reverseTransitionSmoothing = 0.24f;

        [Header("Race countdown audio")]
        [Tooltip("When a configured RaceManager is waiting or counting down, hold the engine at its idle sample until GO.")]
        [SerializeField] private bool lockRpmDuringRaceCountdown = true;

        [Header("Nitro clips")]
        [SerializeField] private AudioClip nitroStartClip;
        [SerializeField] private AudioClip nitroLoopClip;
        [SerializeField] private AudioClip nitroEndClip;
        [Header("Nitro Mode Specific Clips")]
        [Tooltip("Played when Single Nitro activates (Yellow)")]
        [SerializeField] private AudioClip singleNitroClip;
        [Tooltip("Played when Double Nitro activates (Orange)")]
        [SerializeField] private AudioClip doubleNitroClip;
        [Tooltip("Played when Perfect Nitro activates (Cyan / Blue)")]
        [SerializeField] private AudioClip perfectNitroClip;
        [Tooltip("Played when Shockwave / Triple Nitro activates (Purple / Magenta)")]
        [SerializeField] private AudioClip shockwaveNitroClip;
        [Header("AI Nitro Mix")]
        [Tooltip("AI Nitro nhỏ hơn Player để nhiều xe boost cùng lúc không quá ồn.")]
        [SerializeField, Range(0f, 1f)]
        private float nitroVolume = 0.8f;

        [SerializeField, Range(0f, 1f)]
        private float nitroEngineBoost = 0.22f;

        [SerializeField, Range(0f, 1f)]
        private float aiNitroVolumeMultiplier = 0.22f;

        [SerializeField, Min(0.1f)]
        private float aiNitroMinDistance = 2.5f;

        [SerializeField, Min(1f)]
        private float aiNitroMaxDistance = 28f;

        [Header("Tire / Drift Audio")]
        [InspectorName("Tire Skid Clip")]
        [SerializeField] private AudioClip tireSkidClip;
        [InspectorName("Brake Skid Clip")]
        [SerializeField] private AudioClip brakeSkidClip;
        [SerializeField] private AudioClip windRushClip;
        [InspectorName("Tire Volume")]
        [SerializeField, Range(0f, 1f)] private float tireVolume = 0.55f;
        [InspectorName("Brake Volume")]
        [SerializeField, Range(0f, 1f)] private float brakeVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float windVolume = 0.3f;
        [InspectorName("Minimum Skid Speed (km/h)")]
        [SerializeField, Min(0f)] private float minimumSkidSpeedKmh = 8f;
        [InspectorName("Brake Slip Threshold")]
        [SerializeField, Min(0f)] private float brakeSlipThreshold = 0.28f;
        [InspectorName("Drift Slip Threshold")]
        [SerializeField, Min(0f)] private float skidStartSlip = 0.18f;
        [SerializeField, Min(0f)] private float skidFullSlip = 0.65f;
        [InspectorName("Skid Fade Speed")]
        [SerializeField, Min(0.1f)] private float skidResponse = 8f;
        [Tooltip("Minimum brake deceleration required before the separate brake squeal can become audible.")]
        [SerializeField, Min(0f)] private float brakeDecelerationThreshold = 9f;
        [Tooltip("Brake input must exceed this value before brake squeal is considered.")]
        [SerializeField, Range(0f, 1f)] private float brakeInputThreshold = 0.45f;

        [Header("Collision clips")]
        [SerializeField] private AudioClip collisionLightClip;
        [SerializeField] private AudioClip collisionMediumClip;
        [SerializeField] private AudioClip collisionHeavyClip;
        [Tooltip("Legacy fallback list retained for existing scene data.")]
        [SerializeField] private AudioClip[] collisionClips;
        [SerializeField, Min(0f)] private float minImpactSpeed = 3f;
        [SerializeField, Min(0f)] private float mediumImpactSpeed = 7f;
        [SerializeField, Min(0f)] private float heavyImpactSpeed = 14f;
        [SerializeField, Min(0f)] private float crashCooldown = 0.25f;
        [SerializeField, Range(0f, 1f)] private float collisionVolume = 0.75f;

        [Header("3D audio")]
        [SerializeField, Range(0f, 1f)] private float playerSpatialBlend = 0.2f;
        [SerializeField, Range(0f, 1f)] private float aiSpatialBlend = 1f;
        [SerializeField, Min(0.01f)] private float engineMinDistance = 3f;
        [SerializeField, Min(0.01f)] private float engineMaxDistance = 45f;
        [SerializeField, Min(0.01f)] private float collisionMaxDistance = 35f;

        [Header("Runtime debug")]
        [SerializeField, Range(0f, 1f)] private float rpmNormalized;
        [SerializeField] private float currentRpm;
        [SerializeField] private float targetAudioRpm;
        [SerializeField] private float actualSpeedKmh;
        [InspectorName("Signed Speed")]
        [SerializeField] private float signedSpeedKmh;
        [InspectorName("Movement Direction")]
        [SerializeField] private string movementDirectionLabel = "Stationary";
        [SerializeField] private float hudSpeedKmh;
        [SerializeField, Range(0f, 1f)] private float skidNormalized;
        [SerializeField, Range(0f, 1f)] private float brakeSkidNormalized;
        [SerializeField] private float brakeDeceleration;
        [SerializeField, Range(0f, 1f)] private float throttle;
        [SerializeField] private bool nitroActive;
        [SerializeField] private string activeAudioMode = "Legacy fallback";
        [SerializeField] private int lowerActiveSampleRpm;
        [SerializeField] private int upperActiveSampleRpm;
        [SerializeField, Range(0f, 1f)] private float sampleBlend;
        [SerializeField] private int engineSourcesPlaying;
        [SerializeField] private bool legacyEngineAudioActive;
        [SerializeField] private int sampleTransitionEvents;
        [SerializeField] private int rpmPlayCalls;
        [SerializeField] private int rpmStopCalls;
        [InspectorName("Gameplay Nitro Active")]
        [SerializeField] private bool gameplayNitroActive;
        [InspectorName("Audio Nitro Active")]
        [SerializeField] private bool audioNitroActive;
        [InspectorName("Nitro Loop Is Playing")]
        [SerializeField] private bool nitroLoopIsPlaying;
        [InspectorName("Nitro Loop Volume")]
        [SerializeField, Range(0f, 1f)] private float nitroLoopVolume;
        [SerializeField] private int nitroPlayCalls;
        [SerializeField] private int nitroStopCalls;
        [SerializeField] private bool debugOverlayEnabled;
        [InspectorName("Current Race State")]
        [SerializeField] private string currentRaceState = "No RaceManager";
        [InspectorName("Is Countdown Active")]
        [SerializeField] private bool countdownActive;
        [InspectorName("Is Race Started")]
        [SerializeField] private bool raceStarted;
        [InspectorName("RPM Lock Active")]
        [SerializeField] private bool rpmLockActive;

        private Rigidbody body;
        private CarController car;
        private ICarInputProvider inputProvider;
        private WheelCollider[] wheels;
        private AudioSource idleSource;
        private AudioSource lowRpmSource;
        private AudioSource highRpmSource;
        private AudioSource accelerationSource;
        private AudioSource nitroLoopSource;
        private AudioSource effectsSource;
        private AudioSource skidSource;
        private AudioSource brakeSkidSource;
        private AudioSource windSource;
        private AudioSource collisionSource;
        private float lastCrashTime = float.NegativeInfinity;
        private bool wasBoosting;
        private CarController.NitroMode lastPlayedNitroMode = (CarController.NitroMode)(-1);
        private float smoothedSpeedKmh;
        private float speedSmoothVelocity;
        private float smoothedSignedSpeedKmh;
        private float signedSpeedSmoothVelocity;
        private float rpmSmoothVelocity;
        private float previousSpeedKmh;
        private float smoothedDeceleration;
        private bool hasPreviousSpeedSample;
        private int movementDirection;
        private RaceManager raceManager;
        private bool raceManagerLookupAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToSceneCars()
        {
            foreach (CarController controller in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (controller.GetComponent<PoseKartVehicleAudio>() == null)
                {
                    PoseKartVehicleAudio audio =
    controller.gameObject.AddComponent<PoseKartVehicleAudio>();

                    RaceParticipant participant =
                        controller.GetComponent<RaceParticipant>();

                    audio.isPlayer = participant != null
                        ? participant.IsPlayer
                        : IsPlayerName(controller.name);
                }
            }
        }

        private void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();

            RaceParticipant participant = GetComponent<RaceParticipant>();

            if (participant != null)
            {
                isPlayer = participant.IsPlayer;
            }
            else
            {
                isPlayer |= car != null && IsPlayerName(car.name);
            }

            if (style == VehicleAudioStyle.Auto)
            {
                style = isPlayer
                    ? VehicleAudioStyle.PlayerRally
                    : VehicleAudioStyle.AI;
            }

            inputProvider = car == null
                ? null
                : car.InputProviderSource as ICarInputProvider;

            wheels = GetComponentsInChildren<WheelCollider>(true);

            raceManager = FindFirstObjectByType<RaceManager>();
            raceManagerLookupAttempted = true;

            ConfigureSources();

            if (speedSmoothingTime > 0.08f)
                speedSmoothingTime = 0.05f;

            if (rpmRiseSmoothingTime > 0.16f)
                rpmRiseSmoothingTime = 0.12f;

            if (rpmFallSmoothingTime > 0.25f)
                rpmFallSmoothingTime = 0.20f;

            wasBoosting = car != null && car.IsNitroBoosting;

            GetAudioRpmRange(out float minimumRpm, out _);

            currentRpm = minimumRpm;
            targetAudioRpm = minimumRpm;
        }

        private void Update()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (inputProvider == null && car != null) inputProvider = car.InputProviderSource as ICarInputProvider;

            UpdateRaceState();

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float speedKmh = GetSpeedKmh();
            float currentSignedSpeedKmh = GetSignedSpeedKmh();
            float measuredDeceleration = hasPreviousSpeedSample
                ? Mathf.Max(0f, previousSpeedKmh - speedKmh) / dt
                : 0f;
            smoothedDeceleration = Mathf.MoveTowards(
                smoothedDeceleration,
                measuredDeceleration,
                40f * dt);
            brakeDeceleration = smoothedDeceleration;
            previousSpeedKmh = speedKmh;
            hasPreviousSpeedSample = true;
            actualSpeedKmh = speedKmh;
            signedSpeedKmh = currentSignedSpeedKmh;
            hudSpeedKmh = speedKmh;
            smoothedSpeedKmh = Mathf.SmoothDamp(smoothedSpeedKmh, speedKmh, ref speedSmoothVelocity,
                Mathf.Max(0.01f, speedSmoothingTime), Mathf.Infinity, dt);
            smoothedSignedSpeedKmh = Mathf.SmoothDamp(smoothedSignedSpeedKmh, currentSignedSpeedKmh,
                ref signedSpeedSmoothVelocity, Mathf.Max(0.01f, reverseTransitionSmoothing),
                Mathf.Infinity, dt);
            movementDirection = ResolveMovementDirection(currentSignedSpeedKmh);
            movementDirectionLabel = movementDirection < 0
                ? "Reversing"
                : movementDirection > 0 ? "Forward" : "Stationary";
            float speedNormalized = Mathf.Clamp01(smoothedSpeedKmh / Mathf.Max(1f, maxSpeedKmh));
            throttle = inputProvider == null ? 0f : Mathf.Clamp01(inputProvider.Throttle);
            float brake = inputProvider == null ? 0f : Mathf.Clamp01(inputProvider.Brake);
            bool grounded = car == null || car.IsGrounded;
            bool boost = car != null && car.IsVisualNitroBoosting;
            gameplayNitroActive = boost;
            nitroActive = gameplayNitroActive;

            GetAudioRpmRange(out float audioMinimumRpm, out float audioMaximumRpm);
            if (rpmLockActive)
            {
                ForceCountdownIdleRpm(audioMinimumRpm);
            }
            else if (ShouldUseRpmBank()
                || engineAudioMode == EngineAudioMode.SpeedSynchronizedArcade
                || engineAudioMode == EngineAudioMode.SpeedLockedRPM)
            {
                UpdateSpeedSynchronizedRpm(smoothedSpeedKmh, smoothedSignedSpeedKmh, throttle, dt,
                    audioMinimumRpm, audioMaximumRpm, engineAudioMode == EngineAudioMode.SpeedLockedRPM, grounded);
            }
            else
            {
                UpdateLegacyRpm(speedNormalized, throttle, brake, boost, grounded, dt,
                    audioMinimumRpm, audioMaximumRpm);
            }

            UpdateEngineLayers(currentRpm, boost, dt);
            engineSourcesPlaying = CountPlayingEngineSources();
            UpdateNitro(boost, dt);
            nitroLoopIsPlaying = nitroLoopSource != null && nitroLoopSource.isPlaying;
            nitroLoopVolume = nitroLoopSource == null ? 0f : nitroLoopSource.volume;
            audioNitroActive = gameplayNitroActive && nitroLoopIsPlaying && nitroLoopVolume > 0.001f;
            UpdateTireSkid(grounded, brake, smoothedDeceleration, dt);
            CarController.NitroMode currentNitroMode = car != null ? car.VisualNitroMode : CarController.NitroMode.None;
            if (boost)
            {
                if (currentNitroMode != lastPlayedNitroMode)
                {
                    AudioClip modeClip = null;
                    switch (currentNitroMode)
                    {
                        case CarController.NitroMode.Single:
                            modeClip = singleNitroClip != null ? singleNitroClip : nitroStartClip;
                            break;
                        case CarController.NitroMode.Double:
                            modeClip = doubleNitroClip != null ? doubleNitroClip : nitroStartClip;
                            break;
                        case CarController.NitroMode.Perfect:
                            modeClip = perfectNitroClip != null ? perfectNitroClip : nitroStartClip;
                            break;
                        case CarController.NitroMode.Shockwave:
                            modeClip = shockwaveNitroClip != null ? shockwaveNitroClip : nitroStartClip;
                            break;
                        default:
                            modeClip = nitroStartClip;
                            break;
                    }
                    if (modeClip != null)
                    {
                        PlayOneShot(effectsSource, modeClip, 1f, 0.98f, 1.04f);
                    }
                    lastPlayedNitroMode = currentNitroMode;
                }
            }
            else
            {
                if (wasBoosting)
                {
                    PlayOneShot(effectsSource, nitroEndClip, 0.8f, 0.96f, 1.04f);
                }
                lastPlayedNitroMode = CarController.NitroMode.None;
            }
            wasBoosting = boost;
        }

        private void UpdateRaceState()
        {
            if (!raceManagerLookupAttempted)
            {
                raceManager = FindFirstObjectByType<RaceManager>();
                raceManagerLookupAttempted = true;
            }

            bool hasActiveRaceManager = raceManager != null
                && raceManager.isActiveAndEnabled
                && raceManager.IsConfigured;
            if (!hasActiveRaceManager)
            {
                currentRaceState = raceManager == null ? "No RaceManager" : "No active countdown";
                countdownActive = false;
                raceStarted = true;
                rpmLockActive = false;
                return;
            }

            RaceState state = raceManager.State;
            currentRaceState = state.ToString();
            countdownActive = state == RaceState.Countdown;
            raceStarted = state == RaceState.Racing || state == RaceState.Finished;
            // Waiting is included so the engine is already at idle on the grid,
            // before RunCountdown changes the state to Countdown.
            rpmLockActive = lockRpmDuringRaceCountdown
                && (state == RaceState.Waiting || state == RaceState.Countdown);
        }

        private void ForceCountdownIdleRpm(float minimumRpm)
        {
            // The configured bank starts at 1000 RPM. Use the legacy idle value
            // only for scenes that do not have an RPM bank.
            float lockedRpm = engineSoundBank != null
                ? engineSoundBank.MinimumSampleRpm
                : Mathf.Max(minimumRpm, idleRpm);
            targetAudioRpm = lockedRpm;
            currentRpm = lockedRpm;
            rpmNormalized = 0f;
            rpmSmoothVelocity = 0f;
        }

        private void UpdateEngineLayers(float simulatedRpm, bool boost, float dt)
        {
            bool usingRpmBank = ShouldUseRpmBank();
            legacyEngineAudioActive = !usingRpmBank;
            if (usingRpmBank)
            {
                activeAudioMode = engineAudioMode == EngineAudioMode.SpeedLockedRPM
                    ? "Speed locked RPM + 16-step RPM bank"
                    : engineAudioMode == EngineAudioMode.SpeedSynchronizedArcade
                        ? "Speed synchronized arcade + 16-step RPM bank"
                        : "16-step RPM bank";
                UpdateRpmBankEngine(simulatedRpm, boost, dt);
                UpdateLoop(idleSource, null, 0f, 1f, dt);
            }
            else
            {
                activeAudioMode = engineAudioMode == EngineAudioMode.SpeedLockedRPM
                    ? "Speed locked RPM (legacy clips)"
                    : engineAudioMode == EngineAudioMode.SpeedSynchronizedArcade
                        ? "Speed synchronized arcade (legacy clips)"
                        : "Legacy fallback";
                lowerActiveSampleRpm = 0;
                upperActiveSampleRpm = 0;
                sampleBlend = 0f;
                UpdateLegacyEngineLayers(boost, dt);
            }

            if (usingRpmBank)
            {
                // The 16-step bank is the complete engine system. Do not layer the
                // legacy acceleration loop over the two bank sources.
                UpdateLoop(accelerationSource, null, 0f, 1f, dt);
            }
            else
            {
                float pitch = Mathf.Lerp(minimumEnginePitch, maximumEnginePitch, rpmNormalized) + GetStylePitchOffset();
                UpdateLoop(accelerationSource, accelerationClip,
                    throttle * Mathf.Lerp(0.2f, accelerationLayerVolume, rpmNormalized), pitch * 1.03f, dt);
            }
        }

        private void UpdateLegacyRpm(float speedNormalized, float currentThrottle, float brake, bool boost,
            bool grounded, float dt, float minimumRpm, float maximumRpmValue)
        {
            float targetNormalized = Mathf.Clamp01(currentThrottle * throttleRpmInfluence
                + speedNormalized * speedRpmInfluence
                + (boost ? nitroEngineBoost : 0f) + brake * 0.06f);
            if (!grounded && currentThrottle <= 0.01f) targetNormalized *= 0.8f;
            rpmNormalized = Mathf.MoveTowards(rpmNormalized, targetNormalized,
                (targetNormalized >= rpmNormalized ? rpmRiseSpeed : rpmFallSpeed) * dt);
            targetAudioRpm = Mathf.Lerp(minimumRpm, Mathf.Max(minimumRpm + rpmStep, maximumRpmValue), targetNormalized);
            currentRpm = Mathf.Lerp(minimumRpm, Mathf.Max(minimumRpm + rpmStep, maximumRpmValue), rpmNormalized);
        }

        private void UpdateSpeedSynchronizedRpm(float speedKmh, float signedSpeedKmhValue,
            float currentThrottle, float dt, float minimumRpm, float maximumRpmValue, bool speedLocked, bool grounded = true)
        {
            // Reverse direction audio: applies whenever the vehicle is actively reversing
            if (movementDirection < 0)
            {
                float reverseMinimum = Mathf.Clamp(reverseAudioRpmMinimum, minimumRpm, maximumRpmValue);
                float reverseMaximum = Mathf.Clamp(Mathf.Max(reverseAudioRpmMaximum, reverseMinimum),
                    reverseMinimum, maximumRpmValue);
                float reverse01 = Mathf.Clamp01(Mathf.Abs(signedSpeedKmhValue)
                    / Mathf.Max(1f, reverseSpeedReferenceKmh));
                targetAudioRpm = Mathf.Lerp(reverseMinimum, reverseMaximum, reverse01);
                currentRpm = Mathf.SmoothDamp(currentRpm, targetAudioRpm, ref rpmSmoothVelocity,
                    Mathf.Max(0.01f, reverseTransitionSmoothing), Mathf.Infinity, dt);
                currentRpm = Mathf.Clamp(currentRpm, minimumRpm, maximumRpmValue);
                rpmNormalized = Mathf.InverseLerp(minimumRpm, maximumRpmValue, currentRpm);
                return;
            }

            // Determine normal forward top speed vs maximum boost/nitro speed
            float normalTopSpeed = car != null ? Mathf.Max(30f, car.MaxForwardSpeed * 3.6f) : Mathf.Min(maxSpeedKmh, 80f);
            float boostTopSpeed = Mathf.Max(normalTopSpeed * 1.5f, 130f);

            // Map speed to RPM across the entire sound bank:
            // 0 -> normalTopSpeed maps smoothly from minimumRpm (1000) to peak normal powerband (~7200 RPM)
            // normalTopSpeed -> boostTopSpeed maps during Nitro/Boost from 7200 RPM to maximumRpmValue (8500 RPM)
            float normalTopRpm = Mathf.Lerp(minimumRpm, maximumRpmValue, 0.82f);
            float baseRpm;
            if (speedKmh <= normalTopSpeed)
            {
                float t = Mathf.Clamp01(speedKmh / Mathf.Max(1f, normalTopSpeed));
                baseRpm = Mathf.Lerp(minimumRpm, normalTopRpm, t);
            }
            else
            {
                float tBoost = Mathf.Clamp01((speedKmh - normalTopSpeed) / Mathf.Max(1f, boostTopSpeed - normalTopSpeed));
                baseRpm = Mathf.Lerp(normalTopRpm, maximumRpmValue, tBoost);
            }

            float rpmRange = Mathf.Max(0f, maximumRpmValue - minimumRpm);
            float throttleRpm;
            if (speedLocked)
            {
                // Speed Locked RPM follows only the vehicle forward speed
                throttleRpm = 0f;
            }
            else
            {
                // Smoothly blend stationary burnout rev into moving throttle response across 0-12 km/h launch
                float stationaryThrottleRpm = Mathf.Lerp(0f,
                    Mathf.Max(0f, Mathf.Min(stationaryRevLimit, maximumRpmValue) - minimumRpm), currentThrottle);
                float movingThrottleRpm = currentThrottle * movingThrottleBoost * rpmRange;
                float launchBlend = Mathf.Clamp01(speedKmh / 12f);
                throttleRpm = Mathf.Lerp(stationaryThrottleRpm, movingThrottleRpm, launchBlend);
            }

            targetAudioRpm = Mathf.Clamp(baseRpm + throttleRpm, minimumRpm, maximumRpmValue);

            // Airborne handling: holding throttle revs engine freely; releasing throttle glides down
            if (!grounded && speedKmh > 5f)
            {
                if (currentThrottle > 0.1f)
                {
                    targetAudioRpm = Mathf.Max(targetAudioRpm, Mathf.Lerp(targetAudioRpm, maximumRpmValue * 0.90f, currentThrottle));
                }
                else
                {
                    targetAudioRpm = Mathf.Lerp(targetAudioRpm, minimumRpm, 0.5f);
                }
            }

            float smoothingTime = targetAudioRpm >= currentRpm ? rpmRiseSmoothingTime : rpmFallSmoothingTime;
            currentRpm = Mathf.SmoothDamp(currentRpm, targetAudioRpm, ref rpmSmoothVelocity,
                Mathf.Max(0.01f, smoothingTime), Mathf.Infinity, dt);
            currentRpm = Mathf.Clamp(currentRpm, minimumRpm, maximumRpmValue);
            rpmNormalized = Mathf.InverseLerp(minimumRpm, maximumRpmValue, currentRpm);
        }

        private void GetAudioRpmRange(out float minimumRpm, out float maximumRpmValue)
        {
            bool useBankRange = engineSoundBank != null && engineAudioMode != EngineAudioMode.LegacyOnly;
            minimumRpm = useBankRange ? engineSoundBank.MinimumSampleRpm : Mathf.Max(idleRpm, minimumSampleRpm);
            maximumRpmValue = useBankRange
                ? engineSoundBank.MaximumSampleRpm
                : Mathf.Max(maximumRpm, maximumSampleRpm);
            maximumRpmValue = Mathf.Max(minimumRpm + 1f, maximumRpmValue);
        }

        private int CountPlayingEngineSources()
        {
            int count = 0;
            if (IsAudible(engineSource: idleSource)) count++;
            if (IsAudible(engineSource: lowRpmSource)) count++;
            if (IsAudible(engineSource: highRpmSource)) count++;
            if (IsAudible(engineSource: accelerationSource)) count++;
            return count;
        }

        private static bool IsAudible(AudioSource engineSource)
        {
            return engineSource != null && engineSource.isPlaying && engineSource.volume > 0.01f;
        }

        private void UpdateLegacyEngineLayers(bool boost, float dt)
        {
            float high = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.92f, rpmNormalized));
            float low = Mathf.Clamp01(1f - Mathf.Abs(rpmNormalized - 0.38f) * 2.45f);
            float idle = Mathf.Clamp01(1f - rpmNormalized * 1.45f);
            float pitch = Mathf.Lerp(minimumEnginePitch, maximumEnginePitch, rpmNormalized) + GetStylePitchOffset();
            UpdateLoop(idleSource, GetIdleClip(), idle * engineVolume, pitch, dt);
            UpdateLoop(lowRpmSource, engineLowRpmClip, low * engineVolume * 0.9f, pitch * 0.96f, dt);
            UpdateLoop(highRpmSource, engineHighRpmClip, high * engineVolume, pitch * 1.02f, dt);
            if (boost && highRpmSource != null) highRpmSource.volume = Mathf.Clamp01(highRpmSource.volume + nitroEngineBoost * 0.45f);
        }

        private bool ShouldUseRpmBank()
        {
            return engineAudioMode != EngineAudioMode.LegacyOnly
                && engineSoundBank != null
                && engineSoundBank.HasAnyClip;
        }

        private void UpdateRpmBankEngine(float simulatedRpm, bool boost, float dt)
        {
            if (!engineSoundBank.TryGetPair(simulatedRpm, out AudioClip lowerClip, out int lowerRpm,
                    out AudioClip upperClip, out int upperRpm))
            {
                activeAudioMode = "Legacy fallback (bank has no clips)";
                UpdateLegacyEngineLayers(boost, dt);
                return;
            }

            sampleBlend = lowerRpm == upperRpm
                ? 0f
                : Mathf.Clamp01(Mathf.InverseLerp(lowerRpm, upperRpm, simulatedRpm));
            if (lowerActiveSampleRpm != lowerRpm || upperActiveSampleRpm != upperRpm)
                sampleTransitionEvents++;
            lowerActiveSampleRpm = lowerRpm;
            upperActiveSampleRpm = upperRpm;

            ResolveRpmSources(lowerClip, upperClip, out AudioSource lowerSource, out AudioSource upperSource);

            float lowerGain = lowerClip == upperClip ? 1f : Mathf.Cos(sampleBlend * Mathf.PI * 0.5f);
            float upperGain = lowerClip == upperClip ? 0f : Mathf.Sin(sampleBlend * Mathf.PI * 0.5f);
            float boostGain = boost ? 1f + nitroEngineBoost * 0.35f : 1f;
            ApplyRpmSource(lowerSource, lowerClip, lowerGain * engineVolume * boostGain,
                GetRpmCorrectedPitch(simulatedRpm, lowerRpm), dt);
            if (lowerClip == upperClip)
            {
                ApplyRpmSource(OtherRpmSource(lowerSource), null, 0f, 1f, dt);
            }
            else
            {
                ApplyRpmSource(upperSource, upperClip, upperGain * engineVolume * boostGain,
                    GetRpmCorrectedPitch(simulatedRpm, upperRpm), dt);
            }
        }

        private void ResolveRpmSources(AudioClip lowerClip, AudioClip upperClip,
            out AudioSource lowerSource, out AudioSource upperSource)
        {
            lowerSource = null;
            upperSource = null;
            if (lowerClip == upperClip)
            {
                lowerSource = FindRpmSource(lowerClip) ?? ChooseRpmSourceForReplacement();
                return;
            }

            lowerSource = FindRpmSource(lowerClip);
            upperSource = FindRpmSource(upperClip);
            if (lowerSource != null && upperSource != null && lowerSource != upperSource) return;
            if (lowerSource != null)
            {
                upperSource = OtherRpmSource(lowerSource);
                return;
            }
            if (upperSource != null)
            {
                lowerSource = OtherRpmSource(upperSource);
                return;
            }

            lowerSource = ChooseRpmSourceForReplacement();
            upperSource = OtherRpmSource(lowerSource);
        }

        private AudioSource FindRpmSource(AudioClip clip)
        {
            if (clip == null) return null;
            if (lowRpmSource != null && lowRpmSource.clip == clip) return lowRpmSource;
            if (highRpmSource != null && highRpmSource.clip == clip) return highRpmSource;
            return null;
        }

        private AudioSource OtherRpmSource(AudioSource source)
        {
            return source == lowRpmSource ? highRpmSource : lowRpmSource;
        }

        private AudioSource ChooseRpmSourceForReplacement()
        {
            if (lowRpmSource == null) return highRpmSource;
            if (highRpmSource == null) return lowRpmSource;
            if (lowRpmSource.clip == null) return lowRpmSource;
            if (highRpmSource.clip == null) return highRpmSource;
            return lowRpmSource.volume <= highRpmSource.volume ? lowRpmSource : highRpmSource;
        }

        private void ApplyRpmSource(AudioSource source, AudioClip clip, float targetVolume, float targetPitch, float dt)
        {
            if (source == null) return;
            if (source.clip == null && !source.isPlaying) source.volume = 0f;
            if (source.clip != clip)
            {
                // Never replace an audible loop. First fade it out, then swap
                // the clip and start the new sample at zero volume.
                source.volume = Mathf.MoveTowards(source.volume, 0f, dt * crossfadeResponse);
                if (source.volume > 0.001f) return;
                if (source.isPlaying)
                {
                    source.Stop();
                    rpmStopCalls++;
                }
                source.clip = clip;
                source.volume = 0f;
                if (clip != null)
                {
                    source.Play();
                    rpmPlayCalls++;
                }
            }
            if (clip == null)
            {
                source.volume = 0f;
                return;
            }
            if (!source.isPlaying)
            {
                source.Play();
                rpmPlayCalls++;
            }
            source.pitch = Mathf.MoveTowards(source.pitch, targetPitch, pitchSmoothing * dt);
            source.volume = Mathf.MoveTowards(source.volume, Mathf.Clamp01(targetVolume), crossfadeResponse * dt);
        }

        private float GetRpmCorrectedPitch(float simulatedRpm, int sampleRpm)
        {
            float correction = sampleRpm <= 0 ? 1f : simulatedRpm / sampleRpm;
            float minPitch = Mathf.Min(minimumPitchCorrection, 0.80f);
            float maxPitch = Mathf.Max(maximumPitchCorrection, 1.25f);
            correction = Mathf.Clamp(correction, minPitch, maxPitch);
            float styleMultiplier = 1f + GetStylePitchOffset() * 0.25f;
            return Mathf.Clamp(correction * styleMultiplier, 0.1f, 3f);
        }

        private void UpdateNitro(bool boost, float dt)
        {
            if (nitroLoopSource == null)
                return;

            bool shouldPlay = boost && nitroLoopClip != null;

            float targetVolume = isPlayer
                ? nitroVolume
                : nitroVolume * aiNitroVolumeMultiplier;

            if (shouldPlay)
            {
                nitroLoopSource.mute = false;
                nitroLoopSource.enabled = true;

                if (nitroLoopSource.clip != nitroLoopClip)
                {
                    if (nitroLoopSource.isPlaying)
                    {
                        nitroLoopSource.volume =
                            Mathf.MoveTowards(
                                nitroLoopSource.volume,
                                0f,
                                dt * 8f
                            );

                        if (nitroLoopSource.volume > 0.001f)
                            return;

                        nitroLoopSource.Stop();
                        nitroStopCalls++;
                    }

                    nitroLoopSource.clip = nitroLoopClip;
                    nitroLoopSource.volume = 0f;
                }

                if (!nitroLoopSource.isPlaying)
                {
                    nitroLoopSource.Play();
                    nitroPlayCalls++;
                }

                nitroLoopSource.volume =
                    Mathf.MoveTowards(
                        nitroLoopSource.volume,
                        targetVolume,
                        dt * 5f
                    );

                nitroLoopSource.pitch =
                    Mathf.Lerp(
                        0.92f,
                        1.12f,
                        rpmNormalized
                    );
            }
            else
            {
                nitroLoopSource.volume =
                    Mathf.MoveTowards(
                        nitroLoopSource.volume,
                        0f,
                        dt * 7f
                    );

                if (nitroLoopSource.volume <= 0.001f &&
                    nitroLoopSource.isPlaying)
                {
                    nitroLoopSource.Stop();
                    nitroStopCalls++;
                }
            }
        }

        private void UpdateTireSkid(bool grounded, float brakeInput, float deceleration, float dt)
        {
            float slip = 0f;
            bool gotWheelSlip = false;
            if (grounded && wheels != null)
            {
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i] == null) continue;
                    if (wheels[i].GetGroundHit(out WheelHit hit))
                    {
                        gotWheelSlip = true;
                        slip = Mathf.Max(slip, Mathf.Abs(hit.sidewaysSlip));
                        slip = Mathf.Max(slip, Mathf.Abs(hit.forwardSlip) * 0.65f);
                    }
                }
            }
            if (!gotWheelSlip && grounded && body != null)
            {
                Vector3 localVelocity = transform.InverseTransformDirection(body.linearVelocity);
                float planarSpeed = Mathf.Max(1f, new Vector2(localVelocity.x, localVelocity.z).magnitude);
                slip = Mathf.Abs(localVelocity.x) / planarSpeed;
                if (car != null && car.IsDrifting) slip = Mathf.Max(slip, 0.38f);
            }
            float speed = smoothedSpeedKmh;
            float driftTarget = grounded && speed >= minimumSkidSpeedKmh && car != null && car.IsDrifting
                ? Mathf.InverseLerp(skidStartSlip, Mathf.Max(skidStartSlip + 0.01f, skidFullSlip), slip)
                : 0f;
            if (car != null && car.IsDrifting && speed >= minimumSkidSpeedKmh)
            {
                // CarController.IsDrifting is the authoritative gameplay state.
                // The fallback slip value only controls how loud the loop becomes.
                driftTarget = Mathf.Max(driftTarget, 0.35f);
            }

            float brakeTarget = 0f;
            bool movingFastEnoughToBrake = grounded
                && signedSpeedKmh > minimumSkidSpeedKmh
                && brakeInput >= brakeInputThreshold;
            if (movingFastEnoughToBrake)
            {
                float slipIntensity = Mathf.InverseLerp(
                    brakeSlipThreshold,
                    Mathf.Max(brakeSlipThreshold + 0.01f, skidFullSlip),
                    slip);
                float decelerationIntensity = Mathf.InverseLerp(
                    brakeDecelerationThreshold,
                    Mathf.Max(brakeDecelerationThreshold + 0.01f, brakeDecelerationThreshold * 2.2f),
                    deceleration);
                if (slip >= brakeSlipThreshold || deceleration >= brakeDecelerationThreshold)
                {
                    brakeTarget = Mathf.Clamp01(Mathf.Max(slipIntensity, decelerationIntensity * brakeInput));
                }
            }

            skidNormalized = Mathf.MoveTowards(skidNormalized, driftTarget, skidResponse * dt);
            brakeSkidNormalized = Mathf.MoveTowards(brakeSkidNormalized, brakeTarget, skidResponse * dt);

            // Drift and hard-brake sounds share the same tire event. Keep the dominant
            // layer full-volume and duck the other one to avoid a harsh double squeal.
            bool brakeDominant = brakeSkidNormalized > skidNormalized * 0.85f;
            float driftGain = brakeDominant ? 0.18f : 1f;
            float brakeGain = brakeDominant ? 1f : 0.35f;
            UpdateLoop(skidSource, tireSkidClip, skidNormalized * tireVolume * driftGain,
                Mathf.Lerp(0.9f, 1.12f, skidNormalized), dt);
            UpdateLoop(brakeSkidSource, brakeSkidClip, brakeSkidNormalized * brakeVolume * brakeGain,
                Mathf.Lerp(0.86f, 1.04f, brakeSkidNormalized), dt);
        }

        private void UpdateWind(float speedNormalized, bool boost, float dt)
        {
            float target = Mathf.Clamp01(Mathf.InverseLerp(0.55f, 1f, speedNormalized)) * windVolume;
            if (boost) target = Mathf.Clamp01(target + 0.12f);
            UpdateLoop(windSource, windRushClip, target, Mathf.Lerp(0.88f, 1.18f, speedNormalized), dt);
        }

        private void UpdateLoop(AudioSource source, AudioClip clip, float targetVolume, float pitch, float dt)
        {
            if (source == null) return;
            if (clip == null)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0f, dt * crossfadeResponse);
                if (source.volume > 0.001f) return;
                if (source.isPlaying) source.Stop();
                source.clip = null; source.volume = 0f; return;
            }
            if (source.clip != clip)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0f, dt * crossfadeResponse);
                if (source.volume > 0.001f) return;
                if (source.isPlaying) source.Stop();
                source.clip = clip; source.volume = 0f; source.Play();
            }
            else if (!source.isPlaying) source.Play();
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            source.volume = Mathf.MoveTowards(source.volume, Mathf.Clamp01(targetVolume), dt * crossfadeResponse);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collisionSource == null || Time.time - lastCrashTime < crashCooldown || collision == null) return;
            if (IsSoftGroundContact(collision)) return;
            float impactSpeed = collision.relativeVelocity.magnitude;
            float normalImpact = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                normalImpact = Mathf.Max(normalImpact, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            impactSpeed = Mathf.Max(impactSpeed, normalImpact);
            if (impactSpeed < minImpactSpeed) return;
            AudioClip clip = impactSpeed >= heavyImpactSpeed ? collisionHeavyClip
                : impactSpeed >= mediumImpactSpeed ? collisionMediumClip : collisionLightClip;
            if (clip == null && collisionClips != null && collisionClips.Length > 0)
                clip = collisionClips[Random.Range(0, collisionClips.Length)];
            if (clip == null) return;
            float volume = collisionVolume * Mathf.InverseLerp(minImpactSpeed, Mathf.Max(minImpactSpeed + 0.1f, heavyImpactSpeed), impactSpeed);
            PlayOneShot(collisionSource, clip, Mathf.Clamp01(volume), 0.92f, 1.08f);
            lastCrashTime = Time.time;
        }

        private bool IsSoftGroundContact(Collision collision)
        {
            if (collision.contactCount == 0) return false;
            float minimumUpDot = 1f;
            for (int i = 0; i < collision.contactCount; i++)
                minimumUpDot = Mathf.Min(minimumUpDot, Vector3.Dot(collision.GetContact(i).normal, transform.up));
            return minimumUpDot > 0.72f && collision.relativeVelocity.magnitude < mediumImpactSpeed;
        }

        private void ConfigureSources()
        {
            float spatial = isPlayer
                ? playerSpatialBlend
                : aiSpatialBlend;

            idleSource = GetOrCreateSource(
                "Engine Idle",
                true,
                spatial,
                engineMinDistance,
                engineMaxDistance
            );

            lowRpmSource = GetOrCreateSource(
                "Engine Low RPM",
                true,
                spatial,
                engineMinDistance,
                engineMaxDistance
            );

            highRpmSource = GetOrCreateSource(
                "Engine High RPM",
                true,
                spatial,
                engineMinDistance,
                engineMaxDistance
            );

            accelerationSource = GetOrCreateSource(
                "Engine Acceleration",
                true,
                spatial,
                engineMinDistance,
                engineMaxDistance
            );

            // Nitro Player giữ nguyên.
            // Nitro AI nhỏ hơn + 3D + phạm vi nghe ngắn hơn.
            float nitroSpatial = isPlayer
                ? playerSpatialBlend
                : 1f;

            float nitroMinDistance = isPlayer
                ? engineMinDistance
                : aiNitroMinDistance;

            float nitroMaxDistance = isPlayer
                ? engineMaxDistance
                : aiNitroMaxDistance;

            nitroLoopSource = GetOrCreateSource(
                "Nitro Loop",
                true,
                nitroSpatial,
                nitroMinDistance,
                nitroMaxDistance
            );

            nitroLoopSource.mute = false;
            nitroLoopSource.volume = 0f;

            effectsSource = GetOrCreateSource(
                "Vehicle Effects",
                false,
                nitroSpatial,
                nitroMinDistance,
                nitroMaxDistance
            );

            skidSource = GetOrCreateSource(
                "Tire Skid",
                true,
                isPlayer ? 0.25f : 1f,
                engineMinDistance,
                engineMaxDistance
            );

            brakeSkidSource = GetOrCreateSource(
                "Brake Skid",
                true,
                isPlayer ? 0.25f : 1f,
                engineMinDistance,
                engineMaxDistance
            );

            windSource = GetOrCreateSource(
                "Wind Rush",
                true,
                isPlayer ? 0.35f : 1f,
                engineMinDistance,
                engineMaxDistance
            );

            collisionSource = GetOrCreateSource(
                "Collision Audio",
                false,
                spatial,
                engineMinDistance,
                collisionMaxDistance
            );
        }

        private AudioSource GetOrCreateSource(string sourceName, bool loop, float spatialBlend, float minDistance, float maxDistance)
        {
            Transform child = transform.Find(sourceName);
            GameObject go;
            if (child != null)
            {
                go = child.gameObject;
            }
            else
            {
                go = new GameObject(sourceName);
                go.transform.SetParent(transform, false);
            }
            AudioSource source = go.GetComponent<AudioSource>();
            if (source == null) source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.spatialize = spatialBlend > 0.8f;
            return source;
        }

        private void PlayOneShot(
    AudioSource source,
    AudioClip clip,
    float volume,
    float minimumPitch,
    float maximumPitch)
        {
            if (source == null || clip == null)
                return;

            source.pitch =
                Random.Range(
                    minimumPitch,
                    maximumPitch
                );

            if (!isPlayer && source == effectsSource)
            {
                volume *= aiNitroVolumeMultiplier;
            }

            source.PlayOneShot(
                clip,
                Mathf.Clamp01(volume)
            );
        }

        private AudioClip GetIdleClip() { return engineIdleClip != null ? engineIdleClip : engineLoop; }

        private float GetSpeedKmh()
        {
            if (car != null)
            {
                return Mathf.Abs(car.ForwardSpeed) * 3.6f;
            }
            if (body != null)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.y = 0f;
                return velocity.magnitude * 3.6f;
            }
            return 0f;
        }

        private float GetSignedSpeedKmh()
        {
            if (car != null)
            {
                return car.ForwardSpeed * 3.6f;
            }
            if (body != null)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (planarForward.sqrMagnitude > 0.0001f)
                {
                    return Vector3.Dot(planarVelocity, planarForward.normalized) * 3.6f;
                }
            }

            return 0f;
        }

        private int ResolveMovementDirection(float currentSignedSpeedKmh)
        {
            float deadZone = Mathf.Max(0f, directionChangeDeadZoneKmh);
            if (movementDirection > 0)
            {
                if (currentSignedSpeedKmh < -deadZone) return -1;
                if (currentSignedSpeedKmh <= deadZone) return 0;
                return 1;
            }

            if (movementDirection < 0)
            {
                if (currentSignedSpeedKmh > deadZone) return 1;
                if (currentSignedSpeedKmh >= -deadZone) return 0;
                return -1;
            }

            if (currentSignedSpeedKmh > deadZone) return 1;
            if (currentSignedSpeedKmh < -deadZone) return -1;
            return 0;
        }

        private void OnGUI()
        {
            if (!debugOverlayEnabled || !isPlayer) return;
            GUI.Box(new Rect(16f, 16f, 360f, 320f),
                $"POSEKART AUDIO\nMode: {activeAudioMode}\n"
                + $"Race: {currentRaceState}  Countdown: {countdownActive}  Started: {raceStarted}\n"
                + $"Speed Rigidbody: {actualSpeedKmh:0.0} km/h\nHUD speed: {hudSpeedKmh:0.0} km/h\n"
                + $"Signed speed: {signedSpeedKmh:0.0} km/h  Direction: {movementDirectionLabel}\n"
                + $"Throttle: {throttle:0.00}   Nitro: {nitroActive}\n"
                + $"Target RPM: {targetAudioRpm:0}\nSmoothed RPM: {currentRpm:0}\n"
                + $"Samples: {lowerActiveSampleRpm}/{upperActiveSampleRpm}  Blend: {sampleBlend:0.00}\n"
                + $"Engine sources audible: {engineSourcesPlaying}\n"
                + $"RPM lock active: {rpmLockActive}\n"
                + $"Transitions: {sampleTransitionEvents}\n"
                + $"RPM Play/Stop: {rpmPlayCalls}/{rpmStopCalls}\n"
                + $"Legacy engine active: {legacyEngineAudioActive}\n"
                + $"Gameplay Nitro: {gameplayNitroActive}\n"
                + $"Audio Nitro: {audioNitroActive}\n"
                + $"Nitro Loop: {nitroLoopIsPlaying}  Vol: {nitroLoopVolume:0.00}\n"
                + $"Nitro Play/Stop: {nitroPlayCalls}/{nitroStopCalls}");
        }

        private float GetStylePitchOffset()
        {
            switch (style)
            {
                case VehicleAudioStyle.Sport: return 0.08f;
                case VehicleAudioStyle.Buggy: return -0.06f;
                case VehicleAudioStyle.AI: return -0.02f;
                default: return 0f;
            }
        }

        private static bool IsPlayerName(string vehicleName)
        {
            return !string.IsNullOrEmpty(vehicleName)
                && (vehicleName.Equals("Buggy_Player", System.StringComparison.OrdinalIgnoreCase)
                    || vehicleName.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void OnDisable()
        {
            StopLoop(idleSource); StopLoop(lowRpmSource); StopLoop(highRpmSource); StopLoop(accelerationSource);
            StopLoop(nitroLoopSource); StopLoop(skidSource); StopLoop(brakeSkidSource); StopLoop(windSource);
        }

        private static void StopLoop(AudioSource source)
        {
            if (source == null) return;
            source.Stop(); source.volume = 0f;
        }

        private void OnValidate()
        {
            mediumImpactSpeed = Mathf.Max(minImpactSpeed, mediumImpactSpeed);
            heavyImpactSpeed = Mathf.Max(mediumImpactSpeed, heavyImpactSpeed);
            skidFullSlip = Mathf.Max(skidStartSlip + 0.01f, skidFullSlip);
            minimumSkidSpeedKmh = Mathf.Max(0f, minimumSkidSpeedKmh);
            brakeSlipThreshold = Mathf.Max(0f, brakeSlipThreshold);
            brakeDecelerationThreshold = Mathf.Max(0f, brakeDecelerationThreshold);
            brakeInputThreshold = Mathf.Clamp01(brakeInputThreshold);
            minimumSampleRpm = Mathf.Max(1, minimumSampleRpm);
            maximumSampleRpm = Mathf.Max(minimumSampleRpm, maximumSampleRpm);
            rpmStep = Mathf.Max(1, rpmStep);
            maximumPitchCorrection = Mathf.Max(minimumPitchCorrection, maximumPitchCorrection);
            maximumRpm = Mathf.Max(idleRpm + 100f, maximumRpm);
            speedSmoothingTime = Mathf.Max(0.01f, speedSmoothingTime);
            rpmRiseSmoothingTime = Mathf.Max(0.01f, rpmRiseSmoothingTime);
            rpmFallSmoothingTime = Mathf.Max(0.01f, rpmFallSmoothingTime);
            stationaryRevLimit = Mathf.Max(idleRpm, stationaryRevLimit);
            stationarySpeedThresholdKmh = Mathf.Max(0f, stationarySpeedThresholdKmh);
        }
    }
}
