using System;
using PoseKart.Input;
using PoseKart.Race;
using UnityEngine;

namespace PoseKart.Vehicle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        [Header("Nitro Charging")]
        [SerializeField, Min(0.1f)] private float maxNitroCharge = 2.5f;
        [SerializeField, Min(0f)] private float minimumNitroToStart = 0.7f;
        [SerializeField, Min(0f)] private float driftNitroPerSecond = 0.4f;
        [SerializeField, Min(0f)] private float airNitroPerSecond = 0.25f;
        [Tooltip("A short road bump must not count as Air Time.")]
        [SerializeField, Min(0f)] private float minimumRewardAirTime = 0.55f;
        [SerializeField, Min(0f)] private float maximumAirNitroReward = 0.45f;
        [SerializeField, Min(0f)] private float minimumTakeoffSpeed = 6f;

        [Header("Banana Stun")]
        [Tooltip("How strongly the steering swings left/right while stunned. 0 means no steering.")]
        [SerializeField, Range(0f, 1f)] private float bananaStunSteeringStrength = 1f;
        [Tooltip("Steering wobble frequency, in radians per second.")]
        [SerializeField, Min(0.1f)] private float bananaStunWobbleFrequency = 7f;
        [Tooltip("One-time sideways velocity kick when a banana hits the kart (m/s).")]
        [SerializeField, Min(0f)] private float bananaSideImpulse = 4f;

        [Header("Landing Recovery (triggered by KartLandingEffects)")]
        [Tooltip("Maximum fraction of horizontal velocity lost on a very hard landing.")]
        [SerializeField, Range(0f, 0.35f)] private float maximumLandingSpeedLoss = 0.15f;
        [Tooltip("Maximum time before full throttle response returns after a heavy landing.")]
        [SerializeField, Range(0.05f, 0.5f)] private float maximumLandingRecoveryTime = 0.24f;
        [Tooltip("Initial throttle fraction on a very hard landing. Steering and braking remain responsive.")]
        [SerializeField, Range(0f, 1f)] private float minimumLandingThrottleScale = 0.12f;

        [Header("Nitro Modes / Fuel Drain (charge per second)")]
        [SerializeField, Min(0f)] private float singleNitroDrain = 0.28f;
        [SerializeField, Min(0f)] private float doubleNitroDrain = 0.42f;
        [SerializeField, Min(0f)] private float tripleNitroDrain = 0.60f;
        [SerializeField, Min(0f)] private float perfectNitroDrain = 0.32f;

        [Header("Nitro Perfect Timing (seconds after first press)")]
        [SerializeField, Min(0f)] private float perfectTimingStartSeconds = 0.35f;
        [SerializeField, Min(0f)] private float perfectTimingEndSeconds = 0.58f;
        [SerializeField, Min(0.05f)] private float quickDoubleTapWindow = 0.3f;

        [Header("Shockwave Contact Knockdown")]
        [SerializeField, Min(0f)] private float shockwaveMinimumImpactSpeed = 3f;
        [SerializeField, Min(0.1f)] private float shockwaveHitCooldown = 0.4f;
        [SerializeField, Min(0f)] private float shockwavePush = 6f;
        [SerializeField, Min(0f)] private float shockwaveLift = 3f;
        [SerializeField, Min(0f)] private float shockwaveTorque = 5f;
        [SerializeField, Min(0f)] private float nitroPressCooldown = 0.08f;

        [Header("Nitro Acceleration")]
        [SerializeField, Min(1f)] private float singleNitroAccelerationMultiplier = 1.35f;
        [SerializeField, Min(1f)] private float doubleNitroAccelerationMultiplier = 1.55f;
        [SerializeField, Min(1f)] private float tripleNitroAccelerationMultiplier = 1.9f;
        [SerializeField, Min(1f)] private float perfectNitroAccelerationMultiplier = 1.7f;
        [SerializeField, Min(0f)] private float nitroAirAcceleration = 3f;
        [SerializeField, Min(0f)] private float singleNitroImpulse = 3f;
        [SerializeField, Min(0f)] private float doubleNitroImpulse = 4f;
        [SerializeField, Min(0f)] private float tripleNitroImpulse = 6f;
        [SerializeField, Min(0f)] private float perfectNitroImpulse = 4.5f;

        private bool nitroWasGrounded;
        private bool nitroAirborneActive;
        private float nitroAirborneSeconds;
        private float nitroAirAwarded;
        private float nitroStartedAt = float.NegativeInfinity;
        private float nextNitroPressTime = float.NegativeInfinity;

        [Header("References")]
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private MonoBehaviour inputProviderSource;

        [Header("Speed and acceleration")]
        [SerializeField, Min(0.1f)] private float maxForwardSpeed = 22f;
        [Header("Boost Speed Limits (km/h)")]
        [SerializeField, Min(1f)] private float boostLaneMaxSpeedKmh = 110f;
        [SerializeField, Min(1f)] private float itemBoostMaxSpeedKmh = 130f;
        [SerializeField, Min(1f)] private float singleNitroMaxSpeedKmh = 105f;
        [SerializeField, Min(1f)] private float doubleNitroMaxSpeedKmh = 115f;
        [SerializeField, Min(1f)] private float tripleNitroMaxSpeedKmh = 130f;
        [SerializeField, Min(1f)] private float perfectNitroMaxSpeedKmh = 125f;
        [Tooltip("How quickly the allowed speed falls after a boost ends, in m/s per second.")]
        [SerializeField, Min(0.1f)] private float boostSpeedLimitRelease = 11f;
        [SerializeField, Min(0.1f)] private float maxReverseSpeed = 10f;
        [SerializeField, Min(0f)] private float acceleration = 18f;
        [SerializeField, Min(0f)] private float reverseAcceleration = 15f;
        [SerializeField, Min(0f)] private float brakeDeceleration = 28f;
        [SerializeField, Min(0f)] private float coastingResistance = 1.5f;
        [Tooltip("Rigidbody drag. Keep this low because drive and braking values are accelerations in m/s squared.")]
        [SerializeField, Min(0f)] private float linearDamping = 0.03f;
        [SerializeField, Min(0f)] private float brakeToReverseSpeed = 0.75f;
        [SerializeField, Range(0.05f, 1f)] private float accelerationAtTopSpeed = 0.2f;
        [SerializeField, Min(0.25f)] private float accelerationFalloffPower = 2f;

        [Header("Steering")]
#pragma warning disable CS0414
        [SerializeField, Min(0f)] private float steeringDegreesPerSecond = 125f;
        [SerializeField, Range(0.1f, 1f)] private float steeringAtMaxSpeedMultiplier = 0.6f;
        [SerializeField, Min(0.01f)] private float steeringResponse = 7f;
        [SerializeField, Min(0f)] private float uphillAssist = 8f;
        [SerializeField, Min(0f)] private float minimumSteeringSpeed = 0.5f;
#pragma warning restore CS0414
        [SerializeField, Range(0f, 1f)] private float cornerPenaltySteeringThreshold = 0.2f;
        [SerializeField, Min(0f)] private float cornerPenaltyStartSpeed = 5f;
        [SerializeField, Min(0f)] private float maximumCornerDeceleration = 8f;
        [SerializeField, Min(0.25f)] private float cornerPenaltyPower = 1.5f;

        [Header("Arcade stability")]
        [SerializeField, Min(0f)] private float lateralGrip = 7f;
        [SerializeField, Min(0f)] private float maximumLateralSpeed = 5f;
        [SerializeField, Min(0f)] private float baseDownforce;
        [SerializeField, Min(0f)] private float speedDownforce = 0.35f;
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.9f;
        [SerializeField, Min(0f)] private float maximumGroundedUpwardSpeed = 0.75f;
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0f);
        [SerializeField] private bool lockPitchAndRoll = true;

        [Header("Arcade Air Control")]
        [Tooltip("Allows gentle player/input steering while the vehicle is airborne.")]
        [SerializeField] private bool enableAirControl = true;
        [Tooltip("Maximum yaw acceleration applied while airborne, in radians per second squared.")]
        [SerializeField, Min(0f)] private float airYawStrength = 8f;
        [Tooltip("Optional pitch control from throttle and brake input. Disabled by default.")]
        [SerializeField] private bool airPitchControl;
        [Tooltip("Optional roll control from the existing steering input. Disabled by default.")]
        [SerializeField] private bool airRollControl;
        [Tooltip("How quickly airborne rotation responds to the existing steering input.")]
        [SerializeField, Min(0.01f)] private float airSteeringResponsiveness = 6f;
        [Tooltip("Maximum yaw/pitch/roll rate allowed from arcade air control, in degrees per second.")]
        [SerializeField, Min(1f)] private float maximumAirRotationSpeed = 90f;
        [Tooltip("Optional sideways acceleration assist while airborne. Disabled by default.")]
        [SerializeField] private bool airLateralAssist;
        [SerializeField, Min(0f)] private float airLateralAssistStrength = 1.5f;

        [Header("Control Permission")]
        [SerializeField] private bool controlEnabled = true;
        [SerializeField] private bool brakeAllowedWhenControlDisabled;

        [Header("Arcade Collision Speed Loss")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 3f;
        [SerializeField, Min(0f)] private float collisionSpeedLossMultiplier = 0.45f;
        [SerializeField, Min(0f)] private float maximumCollisionSpeedLoss = 8f;
        [SerializeField, Min(0f)] private float collisionLossCooldown = 0.2f;
        [SerializeField, Range(0f, 1f)] private float sideImpactLossMultiplier = 0.15f;

        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Banana Stunned")]
        [SerializeField] private bool debugBananaStunned;
        [InspectorName("Landing Recovery Active")]
        [SerializeField] private bool debugLandingRecovery;
        [InspectorName("Last Landing Severity (0-1)")]
        [SerializeField, Range(0f, 1f)] private float debugLastLandingSeverity;
        [InspectorName("Input Provider Valid")]
        [SerializeField] private bool inputProviderValid;
        [InspectorName("Current Steering")]
        [SerializeField] private float debugSteering;
        [InspectorName("Current Throttle")]
        [SerializeField] private float debugThrottle;
        [InspectorName("Current Brake")]
        [SerializeField] private float debugBrake;
        [InspectorName("Rigidbody Velocity")]
        [SerializeField] private Vector3 debugRigidbodyVelocity;
        [InspectorName("Current Speed")]
        [SerializeField] private float debugSpeed;
        [InspectorName("Forward Speed")]
        [SerializeField] private float debugForwardSpeed;
        [InspectorName("Speed Normalized")]
        [SerializeField] private float debugSpeedNormalized;
        [InspectorName("Acceleration Factor")]
        [SerializeField] private float debugAccelerationFactor = 1f;
        [InspectorName("Steering Speed Penalty")]
        [SerializeField] private float debugSteeringSpeedPenalty;
        [InspectorName("Last Collision Impact")]
        [SerializeField] private float debugLastCollisionImpact;
        [InspectorName("Last Collision Speed Loss")]
        [SerializeField] private float debugLastCollisionSpeedLoss;
        [InspectorName("Last Contact Collider")]
        [SerializeField] private string debugLastContactCollider;
        [InspectorName("Last Contact Impulse")]
        [SerializeField] private float debugLastContactImpulse;
        [InspectorName("Last Contact Up Dot")]
        [SerializeField] private float debugLastContactUpDot;
        [InspectorName("Observed Speed Drop")]
        [SerializeField] private float debugObservedSpeedDrop;
        [InspectorName("Observed Vertical Delta")]
        [SerializeField] private float debugObservedVerticalDelta;
        [InspectorName("Airborne")]
        [SerializeField] private bool debugAirborne;
        [InspectorName("Air Control Active")]
        [SerializeField] private bool debugAirControlActive;
        [InspectorName("Air Steering")]
        [SerializeField] private float debugAirSteering;
        [InspectorName("Air Yaw Rate")]
        [SerializeField] private float debugAirYawRate;
        [InspectorName("Impact Recovery Active")]
        [SerializeField] private bool debugImpactRecovery;
        [InspectorName("Last Missile Impact Delta")]
        [SerializeField] private Vector3 debugLastMissileImpactDelta;

        private ICarInputProvider inputProvider;
        private float smoothedSteering;
        private float lastCollisionLossTime = float.NegativeInfinity;
        private float impactRecoveryUntil = float.NegativeInfinity;
        private readonly RaycastHit[] groundProbeHits = new RaycastHit[8];
        private Vector3 previousObservedVelocity;
        private bool hasPreviousObservedVelocity;
        private float previousSteering;
        private float previousThrottle;
        private float previousBrake;
        private string recentContactCollider = "None";
        private float recentContactImpulse;
        private float recentContactUpDot = 1f;
        private float nextAnomalyLogTime;
        private float itemBoostUntil;
        private float padBoostUntil;
        private float smoothedSpeedLimit;
        private float oilSlipUntil;
        private float bananaStunUntil;
        private float bananaStunStartedAt;
        private float bananaStunDirection = 1f;
        private float landingRecoveryStartedAt;
        private float landingRecoveryUntil;
        private float landingRecoveryDuration;
        private float landingRecoveryStrength;
        private float pendingLandingSpeedLoss;
        private float launchedUntil;
        private bool grounded;
        private Vector3 groundNormal = Vector3.up;
        private Vector3 driveForward = Vector3.forward;
        private float yawRate;
        private float stuntStart, stuntDuration;
        private Quaternion stuntHeading;
        private Vector3 stuntRight;
        private float airLateralSpeed;
        private float airLateralOffset;
        private float driftCharge;
        private float storedNitro;

        public enum NitroMode
        {
            None,
            Single,
            Double,
            Shockwave = 3,
            [Obsolete("Use Shockwave. Kept for existing serialized references.")]
            Triple = Shockwave,
            Perfect
        }

        public NitroMode CurrentNitroMode { get; private set; }
        public float StoredNitro => storedNitro;
        public float MaxNitroChargeAmount => Mathf.Max(0.1f, maxNitroCharge);
        public float NitroFill01 => Mathf.Clamp01(storedNitro / MaxNitroChargeAmount);
        public bool NitroReady => storedNitro >= minimumNitroToStart && storedNitro > 0f;
        public bool IsNitroBoosting => CurrentNitroMode != NitroMode.None;
        public bool IsBananaStunned => Time.time < bananaStunUntil;
        public bool IsLandingRecovering => Time.time < landingRecoveryUntil;
        public bool IsPadBoosting => Time.time < padBoostUntil;
        public bool IsItemBoosting => Time.time < itemBoostUntil;
        public bool IsBoosting => IsNitroBoosting || IsPadBoosting || IsItemBoosting;

        /// <summary>
        /// Raised after a missile impact has been applied to the vehicle. This
        /// is intentionally a notification only; visual systems must not alter
        /// the Rigidbody from this event.
        /// </summary>
        public event Action<Vector3> MissileImpactEvent;

        // Both HUDs and input use the same fuel-space zone, frozen on activation.
        public bool IsNitroTimingActive => CurrentNitroMode == NitroMode.Single && storedNitro > 0f;
        public float NitroTimingProgress01 => NitroFill01;
        public float NitroPerfectStart01 => PerfectZoneMin01;
        public float NitroPerfectEnd01 => PerfectZoneMax01;
        public float NitroShockwaveEnd01 => 1f;
        public bool IsPerfectTimingNow => IsNitroTimingActive &&
            NitroFill01 >= PerfectZoneMin01 && NitroFill01 <= PerfectZoneMax01;
        public bool IsShockwaveTimingNow => IsNitroTimingActive && nitroStartedFull &&
            Time.time - nitroStartedAt <= quickDoubleTapWindow;
        private float nitroActivatedFill;
        private bool nitroStartedFull;
        private float nextShockwaveHitTime;
        private float perfectZoneMin;
        private float perfectZoneMax;
        public bool IsShockwaveActive => CurrentNitroMode == NitroMode.Shockwave;
        public bool IsPerfectNitro => CurrentNitroMode == NitroMode.Perfect;
        public float NitroActivatedFill => nitroActivatedFill;
        public float NitroDrainRate01 => singleNitroDrain / MaxNitroChargeAmount;
        public float PerfectZoneMin01 => perfectZoneMin;
        public float PerfectZoneMax01 => perfectZoneMax;

        public bool IsDrifting { get; private set; }
        public float DriftCharge => driftCharge;
        public bool IsAirAction => !grounded || stuntDuration > 0f || Time.time < launchedUntil;
        public bool IsGrounded => grounded;
        public float SteeringInput => smoothedSteering;
        public float ForwardSpeed { get; private set; }
        public Rigidbody TargetBody => targetBody;
        public MonoBehaviour InputProviderSource => inputProviderSource;
        public bool ControlEnabled => controlEnabled;
        public float MaxForwardSpeed => maxForwardSpeed;

        private float CurrentNitroSpeedLimit
        {
            get
            {
                switch (CurrentNitroMode)
                {
                    case NitroMode.Single: return singleNitroMaxSpeedKmh / 3.6f;
                    case NitroMode.Double: return doubleNitroMaxSpeedKmh / 3.6f;
                    case NitroMode.Shockwave: return tripleNitroMaxSpeedKmh / 3.6f;
                    case NitroMode.Perfect: return perfectNitroMaxSpeedKmh / 3.6f;
                    default: return maxForwardSpeed;
                }
            }
        }

        private float slipstreamSpeedBonusKmh;
        public float SlipstreamBonusKmh => slipstreamSpeedBonusKmh;

        public void SetSlipstreamBonus(float bonusKmh)
        {
            slipstreamSpeedBonusKmh = Mathf.Clamp(bonusKmh, 0f, 20f);
        }

        // Concurrent boosts do not add their top speeds together; the strongest limit wins.
        private float SpeedLimit
        {
            get
            {
                float limit = maxForwardSpeed;
                if (!IsBoosting && grounded)
                    limit += Mathf.Clamp(-driveForward.y * 22f, 0f, 8f);
                if (slipstreamSpeedBonusKmh > 0f)
                    limit += slipstreamSpeedBonusKmh / 3.6f;
                if (IsPadBoosting)
                    limit = Mathf.Max(limit, boostLaneMaxSpeedKmh / 3.6f);
                if (IsItemBoosting)
                    limit = Mathf.Max(limit, itemBoostMaxSpeedKmh / 3.6f);
                if (IsNitroBoosting)
                    limit = Mathf.Max(limit, CurrentNitroSpeedLimit);
                return limit;
            }
        }

        private void UpdateSpeedLimitTransition()
        {
            float desiredLimit = SpeedLimit;
            if (smoothedSpeedLimit <= 0f || desiredLimit >= smoothedSpeedLimit)
                smoothedSpeedLimit = desiredLimit;
            else
                smoothedSpeedLimit = Mathf.MoveTowards(smoothedSpeedLimit, desiredLimit,
                    boostSpeedLimitRelease * Time.fixedDeltaTime);
        }

        private float BoostAccelerationMultiplier
        {
            get
            {
                float multiplier = (IsPadBoosting || IsItemBoosting) ? 1.7f : (slipstreamSpeedBonusKmh > 0f ? 1.25f : 1f);
                switch (CurrentNitroMode)
                {
                    case NitroMode.Single: return Mathf.Max(multiplier, singleNitroAccelerationMultiplier);
                    case NitroMode.Double: return Mathf.Max(multiplier, doubleNitroAccelerationMultiplier);
                    case NitroMode.Shockwave: return Mathf.Max(multiplier, tripleNitroAccelerationMultiplier);
                    case NitroMode.Perfect: return Mathf.Max(multiplier, perfectNitroAccelerationMultiplier);
                    default: return multiplier;
                }
            }
        }

        // Shared API for drift, air time, and future Nitro pickups.
        public void AddNitro(float amount)
        {
            if (float.IsNaN(amount) || amount <= 0f)
                return;
            storedNitro = Mathf.Clamp(storedNitro + amount, 0f, MaxNitroChargeAmount);
        }

        private void Update()
        {
            if (NitroBrakeHeld && IsNitroBoosting) StopNitro();
            // Chỉ xe của Player mới được nhận input phím E từ bàn phím!
            if (inputProviderSource is PoseKart.AI.AIInputProvider) return;
            var racer = GetComponent<PoseKart.Race.RaceParticipant>();
            if (racer != null && !racer.IsPlayer) return;

            // Bấm E ăn ngay cho phím Keyboard
            bool ePressed = false;
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                ePressed = UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame;
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!ePressed)
            {
                ePressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            }
#endif

            if (ePressed)
            {
                UseStoredNitro();
            }
        }

        // Can also be called by AI or a mobile UI button.
        private bool NitroBrakeHeld =>
            (inputProviderSource as ICarInputProvider) is ICarInputProvider provider &&
            provider.IsInputValid && provider.Brake > 0.01f;

        public void UseStoredNitro()
        {
            if (!controlEnabled || IsBananaStunned || targetBody == null ||
                Time.timeScale <= 0f || NitroBrakeHeld || Time.time < nextNitroPressTime)
                return;

            if (CurrentNitroMode == NitroMode.None)
            {
                if (!NitroReady) return;
                nitroActivatedFill = NitroFill01;
                // Snapshot before Single consumes fuel; 99% is not a full tank.
                nitroStartedFull = storedNitro >= MaxNitroChargeAmount - 0.00001f;
                perfectZoneMin = Mathf.Clamp01(nitroActivatedFill -
                    NitroDrainRate01 * Mathf.Max(perfectTimingStartSeconds, perfectTimingEndSeconds));
                perfectZoneMax = Mathf.Clamp01(nitroActivatedFill -
                    NitroDrainRate01 * Mathf.Max(0f, perfectTimingStartSeconds));
                nitroStartedAt = Time.time;
                CurrentNitroMode = NitroMode.Single;
                ApplyNitroImpulse(singleNitroImpulse);
            }
            else if (CurrentNitroMode == NitroMode.Single)
            {
                if (IsShockwaveTimingNow)
                {
                    CurrentNitroMode = NitroMode.Shockwave;
                    ApplyNitroImpulse(tripleNitroImpulse);
                }
                else if (IsPerfectTimingNow)
                {
                    CurrentNitroMode = NitroMode.Perfect;
                    ApplyNitroImpulse(perfectNitroImpulse);
                }
                else
                {
                    CurrentNitroMode = NitroMode.Double;
                    ApplyNitroImpulse(doubleNitroImpulse);
                }
            }
            else
            {
                StopNitro();
            }
            // Keep debouncing even when a press switches Nitro off.
            nextNitroPressTime = Time.time + Mathf.Min(nitroPressCooldown, quickDoubleTapWindow * 0.5f);
        }

        private void ApplyNitroImpulse(float impulse)
        {
            if (impulse <= 0f || targetBody == null)
                return;

            Vector3 direction = grounded
                ? Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized
                : Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f)
                return;

            float currentSpeed = Vector3.Dot(targetBody.linearVelocity, direction);
            float remaining = Mathf.Max(0f, CurrentNitroSpeedLimit - currentSpeed);
            targetBody.AddForce(direction * Mathf.Min(impulse, remaining), ForceMode.VelocityChange);
        }

        private void UpdateNitroConsumption()
        {
            if (!IsNitroBoosting)
                return;
            if (!controlEnabled || NitroBrakeHeld || storedNitro <= 0f)
            {
                StopNitro();
                return;
            }

            float drain = 0f;
            switch (CurrentNitroMode)
            {
                case NitroMode.Single: drain = singleNitroDrain; break;
                case NitroMode.Double: drain = doubleNitroDrain; break;
                case NitroMode.Shockwave: drain = tripleNitroDrain; break;
                case NitroMode.Perfect: drain = perfectNitroDrain; break;
            }
            storedNitro = Mathf.Max(0f, storedNitro - drain * Time.fixedDeltaTime);
            if (storedNitro <= 0f)
                StopNitro();
        }

        private void ApplyNitroAirAcceleration()
        {
            if (!controlEnabled || grounded || !IsNitroBoosting || nitroAirAcceleration <= 0f)
                return;

            // Follow horizontal travel, not transform.forward: a scripted stunt rotates the kart.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up);
            if (planarVelocity.sqrMagnitude < 0.001f ||
                planarVelocity.magnitude >= CurrentNitroSpeedLimit)
                return;
            targetBody.AddForce(planarVelocity.normalized * nitroAirAcceleration, ForceMode.Acceleration);
        }

        public void StopNitro()
        {
            nitroStartedFull = false;
            CurrentNitroMode = NitroMode.None;
            nitroStartedAt = float.NegativeInfinity;
            nextNitroPressTime = float.NegativeInfinity;
        }

        // Kept for existing item scripts. This is NOT Nitro fuel or Nitro mode.
        public void ApplySpeedBoost(float duration = 2f, float impulse = 8f)
        {
            if (targetBody == null)
                return;
            itemBoostUntil = Mathf.Max(itemBoostUntil, Time.time + Mathf.Max(0f, duration));
            targetBody.AddForce(transform.forward * Mathf.Max(0f, impulse), ForceMode.VelocityChange);
        }

        public void ApplyPadBoost(float duration = 1.5f, float impulse = 3f)
        {
            if (targetBody == null)
                return;
            padBoostUntil = Mathf.Max(padBoostUntil, Time.time + Mathf.Max(0f, duration));
            // The lane timer is retained, but a Nitro-powered car gets no extra pad impulse.
            if (!IsNitroBoosting)
                targetBody.AddForce(transform.forward * Mathf.Max(0f, impulse), ForceMode.VelocityChange);
        }

        public void ApplyHomingHit()
        {
            if (GetComponent<KartItemDefense>() != null
                && GetComponent<KartItemDefense>().TryBlockMissileExplosion(transform.position))
            {
                return;
            }

            ApplyMissileExplosionImpact(-transform.forward * 3f + Vector3.up * 2f, .7f);
        }

        public void ApplyMissileExplosionImpact(Vector3 deltaVelocity, float recoveryDuration)
        {
            ApplyMissileExplosionImpact(
                deltaVelocity,
                recoveryDuration,
                targetBody != null ? targetBody.worldCenterOfMass : transform.position,
                12f);
        }

        public void ApplyMissileExplosionImpact(
            Vector3 deltaVelocity,
            float recoveryDuration,
            Vector3 impactPoint,
            float impactTorqueStrength)
        {
            if (targetBody == null || IsShockwaveActive)
                return;

            Vector3 horizontal = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(deltaVelocity, Vector3.up), 18f);
            float vertical = Mathf.Clamp(deltaVelocity.y, -14f, 14f);
            Vector3 boundedDelta = horizontal + Vector3.up * vertical;
            targetBody.AddForce(boundedDelta, ForceMode.VelocityChange);

            // A force applied at the centre of mass only launches the kart. Add
            // a controlled angular impulse around the axis perpendicular to the
            // blast direction so front hits pitch and side hits roll.
            Vector3 blastDirection = Vector3.ProjectOnPlane(impactPoint - targetBody.worldCenterOfMass, Vector3.up);
            if (blastDirection.sqrMagnitude < 0.0001f)
                blastDirection = Vector3.ProjectOnPlane(-boundedDelta, Vector3.up);
            if (blastDirection.sqrMagnitude < 0.0001f)
                blastDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            blastDirection.Normalize();

            Vector3 flipAxis = Vector3.Cross(Vector3.up, blastDirection);
            if (flipAxis.sqrMagnitude < 0.0001f)
                flipAxis = transform.right;
            flipAxis.Normalize();

            float impulseStrength = Mathf.Clamp01(boundedDelta.magnitude / 8f);
            float torqueImpulse = Mathf.Max(0f, impactTorqueStrength) * Mathf.Lerp(.65f, 1f, impulseStrength);
            targetBody.maxAngularVelocity = Mathf.Max(targetBody.maxAngularVelocity, 18f);
            if (torqueImpulse > 0f)
                targetBody.AddTorque(flipAxis * torqueImpulse, ForceMode.VelocityChange);
            targetBody.angularVelocity = Vector3.ClampMagnitude(targetBody.angularVelocity, 18f);
            debugLastMissileImpactDelta = boundedDelta;
            MissileImpactEvent?.Invoke(boundedDelta);
            impactRecoveryUntil = Mathf.Max(
                impactRecoveryUntil,
                Time.time + Mathf.Max(.05f, recoveryDuration));
            debugImpactRecovery = true;

            // Give a grounded car a short airborne window only when the explosion
            // actually contains an upward component. The Rigidbody remains fully
            // physical; this only prevents the drive controller from cancelling it.
            if (vertical > .1f)
            {
                launchedUntil = Mathf.Max(
                    launchedUntil,
                    Time.time + Mathf.Min(.35f, Mathf.Max(.05f, recoveryDuration) * .5f));
                grounded = false;
            }
            stuntDuration = 0f;
            yawRate = 0f;
        }

        public void LaunchStunt(Vector3 landingPoint)
        {
            if (!grounded || !controlEnabled) return;
            Vector3 delta = landingPoint - targetBody.position;
            // Bounded launch height: never derive an arbitrarily large upward impulse from distance.
            float gravity = Mathf.Max(.1f, -Physics.gravity.y), upSpeed = 3.6f;
            float flight = (upSpeed + Mathf.Sqrt(Mathf.Max(0f, upSpeed * upSpeed - 2f * gravity * delta.y))) / gravity;
            flight = Mathf.Clamp(flight, 1.2f, 3f);
            targetBody.useGravity = true;
            Vector3 planarDelta = new Vector3(delta.x, 0f, delta.z);
            float planarDist = planarDelta.magnitude;
            float planarSpeed = Mathf.Min(planarDist / flight, 22f);
            Vector3 planarDir = planarDist > 0.001f ? planarDelta / planarDist : transform.forward;
            targetBody.linearVelocity = planarDir * planarSpeed + Vector3.up * upSpeed;
            targetBody.angularVelocity = Vector3.zero;
            stuntHeading = Quaternion.LookRotation(Vector3.ProjectOnPlane(delta, Vector3.up).normalized);
            stuntRight = stuntHeading * Vector3.right; airLateralSpeed = 0f; airLateralOffset = 0f;
            stuntStart = Time.time; stuntDuration = flight;
            launchedUntil = Time.time + flight * .8f; grounded = false;
            padBoostUntil = Mathf.Max(
                padBoostUntil,
                Time.time + flight + 0.8f);
        }

        // Called once by KartLandingEffects after a real, sufficiently hard road contact.
        // It never moves the chassis or changes its vertical velocity.
        public void BeginLandingRecovery(float severity01)
        {
            if (!controlEnabled || targetBody == null || IsBananaStunned)
                return;

            float severity = Mathf.Clamp01(severity01);
            if (severity <= 0f)
                return;

            landingRecoveryStrength = severity;
            landingRecoveryDuration = Mathf.Lerp(0.08f, maximumLandingRecoveryTime, severity);
            landingRecoveryStartedAt = Time.time;
            landingRecoveryUntil = Time.time + landingRecoveryDuration;
            pendingLandingSpeedLoss = Mathf.Max(pendingLandingSpeedLoss,
                maximumLandingSpeedLoss * severity);
            debugLastLandingSeverity = severity;
        }

        private float LandingThrottleScale
        {
            get
            {
                if (!IsLandingRecovering)
                    return 1f;
                float progress = Mathf.Clamp01((Time.time - landingRecoveryStartedAt) /
                    Mathf.Max(0.01f, landingRecoveryDuration));
                float initialScale = Mathf.Lerp(1f, minimumLandingThrottleScale,
                    landingRecoveryStrength);
                return Mathf.Lerp(initialScale, 1f, Mathf.SmoothStep(0f, 1f, progress));
            }
        }

        private void ApplyPendingLandingSpeedLoss()
        {
            if (pendingLandingSpeedLoss <= 0f || targetBody == null)
                return;
            Vector3 velocity = targetBody.linearVelocity;
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            // Preserve the contact solver's vertical result; reduce horizontal momentum only.
            targetBody.linearVelocity = velocity - horizontal * pendingLandingSpeedLoss;
            pendingLandingSpeedLoss = 0f;
        }

        private void ResetLandingRecovery()
        {
            landingRecoveryUntil = 0f;
            landingRecoveryDuration = 0f;
            landingRecoveryStrength = 0f;
            pendingLandingSpeedLoss = 0f;
        }

        public void CancelAirAction()
        {
            ResetLandingRecovery();
            stuntDuration = 0f;
            launchedUntil = 0f;
            itemBoostUntil = 0f;
            padBoostUntil = 0f;
            StopNitro();
            bananaStunUntil = 0f; // A respawn/air-action reset clears banana stun.
            yawRate = 0f;
            IsDrifting = false;
            driftCharge = 0f;
            impactRecoveryUntil = float.NegativeInfinity;
            debugImpactRecovery = false;
            nitroAirborneActive = false;
            nitroAirborneSeconds = 0f;
            nitroAirAwarded = 0f;
            nitroWasGrounded = grounded;
        }

        public void ApplyOilSlip(float duration = 2f)
        {
            if (GetComponent<KartItemDefense>() != null
                && GetComponent<KartItemDefense>().TryBlockIncomingItem(transform.position))
            {
                return;
            }

            oilSlipUntil = Mathf.Max(oilSlipUntil, Time.time + duration);
            targetBody.AddForce(transform.right * (UnityEngine.Random.value < .5f ? -4f : 4f), ForceMode.VelocityChange);
        }

        // Returns false if the shield blocks the banana. The trap is consumed either way.
        public bool TryApplyBananaStun(float duration = 1.5f)
        {
            if (!controlEnabled || targetBody == null || IsShockwaveActive)
                return false;

            KartItemDefense defense = GetComponent<KartItemDefense>();
            if (defense != null && defense.TryBlockIncomingItem(transform.position))
                return false;

            float stunDuration = Mathf.Max(0.1f, duration);
            bananaStunUntil = Mathf.Max(bananaStunUntil, Time.time + stunDuration);
            bananaStunStartedAt = Time.time;
            bananaStunDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;

            // A banana interrupts the active Nitro mode, but does not delete saved fuel.
            StopNitro();
            IsDrifting = false;
            driftCharge = 0f;
            ResetNitroAirTime();

            if (bananaSideImpulse > 0f)
                targetBody.AddForce(transform.right * (bananaStunDirection * bananaSideImpulse),
                    ForceMode.VelocityChange);
            return true;
        }

        // Compatibility for older scripts that still call ApplyBananaSlip().
        public void ApplyBananaSlip(float duration = 1f)
        {
            TryApplyBananaStun(duration);
        }

        private void Reset()
        {
            targetBody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            SeamlessRoadSurface.EnsureBuilt();

            if (targetBody == null)
            {
                targetBody = GetComponent<Rigidbody>();
            }

            ApplyRigidbodySetup();
            ResolveInputProvider();
            if (inputProviderSource is KeyboardInputProvider)
            {
                if (GetComponent<PoseKart.Input.PoseKartHandInputSwitcher>() == null) gameObject.AddComponent<PoseKart.Input.PoseKartHandInputSwitcher>();
                if (GetComponent<PoseKart.Input.PoseKartHandControlDebugUI>() == null) gameObject.AddComponent<PoseKart.Input.PoseKartHandControlDebugUI>();
                if (GetComponent<PoseKart.Race.PoseKartRocketStart>() == null) gameObject.AddComponent<PoseKart.Race.PoseKartRocketStart>();
                if (GetComponent<PoseKart.Race.PoseKartWrongWayDetector>() == null) gameObject.AddComponent<PoseKart.Race.PoseKartWrongWayDetector>();
                if (GetComponent<PoseKartPlayerSlipstream>() == null) gameObject.AddComponent<PoseKartPlayerSlipstream>();
            }
            if (GetComponent<KartDriveEffects>() == null) gameObject.AddComponent<KartDriveEffects>();
            if (GetComponent<PoseKart.Race.KartItemInventory>() == null) gameObject.AddComponent<PoseKart.Race.KartItemInventory>();
            if (GetComponent<KartItemDefense>() == null) gameObject.AddComponent<KartItemDefense>();
            if (GetComponent<PoseKartVehicleSquashStretch>() == null) gameObject.AddComponent<PoseKartVehicleSquashStretch>();
            if (GetComponent<PoseKart.Stunts.PoseKartStuntController>() == null) gameObject.AddComponent<PoseKart.Stunts.PoseKartStuntController>();
            if (GetComponent<PoseKart.Race.PoseKartBananaTripleLauncher>() == null) gameObject.AddComponent<PoseKart.Race.PoseKartBananaTripleLauncher>();
            if (GetComponent<PoseKart.Race.PoseKartShieldGeneratorVisual>() == null) gameObject.AddComponent<PoseKart.Race.PoseKartShieldGeneratorVisual>();
        }

        private void FixedUpdate()
        {
            ApplyPendingLandingSpeedLoss();
            debugLandingRecovery = IsLandingRecovering;
            debugBananaStunned = IsBananaStunned;
            debugImpactRecovery = Time.time < impactRecoveryUntil;
            RaycastHit support = default;
            grounded = Time.time >= launchedUntil && VehicleGroundProbe.Cast(targetBody, targetBody.worldCenterOfMass + Vector3.up * .3f, 1.7f, out support);
            if (grounded) groundNormal = support.normal; else groundNormal = Vector3.up;
            if (stuntDuration > 0f)
            {
                float progress = Mathf.Clamp01((Time.time - stuntStart) / (stuntDuration * .78f));
                targetBody.angularVelocity = Vector3.zero;
                targetBody.MoveRotation(stuntHeading * Quaternion.Euler(-360f * progress, 0, 0));
                if (grounded || Time.time - stuntStart > stuntDuration + 1f) stuntDuration = 0f;
            }
            driveForward = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;
            // Editor-generated scenes can enter Play Mode without a full scene/domain reload.
            // Rehydrate the non-serialized interface cache from its serialized component reference.
            if (inputProvider == null && inputProviderSource != null)
            {
                inputProvider = inputProviderSource as ICarInputProvider;
            }

            Vector3 localVelocity = transform.InverseTransformDirection(targetBody.linearVelocity);
            ForwardSpeed = localVelocity.z;
            UpdateNitroAirTime();
            UpdateNitroConsumption();
            ApplyNitroAirAcceleration();
            UpdateSpeedLimitTransition();
            debugRigidbodyVelocity = targetBody.linearVelocity;
            debugSpeed = targetBody.linearVelocity.magnitude;
            debugForwardSpeed = ForwardSpeed;
            debugSpeedNormalized = Mathf.Clamp01(Mathf.Max(0f, ForwardSpeed) / maxForwardSpeed);
            inputProviderValid = inputProvider != null && inputProvider.IsInputValid;

            if (!inputProviderValid)
            {
                debugSteering = 0f;
                debugThrottle = 0f;
                debugBrake = 0f;
                debugAirborne = !grounded;
                debugAirControlActive = false;
                debugAirSteering = 0f;
                debugAirYawRate = debugAirborne
                    ? Vector3.Dot(targetBody.angularVelocity, Vector3.up) * Mathf.Rad2Deg
                    : 0f;
                RecordMotionSample(localVelocity, 0f, 0f, 0f);
                smoothedSteering = Mathf.MoveTowards(
                    smoothedSteering,
                    0f,
                    steeringResponse * Time.fixedDeltaTime);
                ApplyStability(localVelocity);
                LimitGroundContactImpulses();
                ClampPlanarSpeed();
                return;
            }

            float steering = controlEnabled
                ? Mathf.Clamp(inputProvider.Steering, -1f, 1f)
                : 0f;
            bool bananaStunned = IsBananaStunned;
            if (bananaStunned)
            {
                // Ignore player/AI steering and force an unstable left/right wobble.
                float elapsed = Time.time - bananaStunStartedAt;
                steering = Mathf.Sin(elapsed * bananaStunWobbleFrequency)
                    * bananaStunSteeringStrength * bananaStunDirection;
            }
            else if (Time.time < oilSlipUntil)
            {
                steering = Mathf.Clamp(steering + Mathf.Sin(Time.time * 13f) * .75f, -1f, 1f);
            }

            float throttle = controlEnabled
                ? Mathf.Clamp01(inputProvider.Throttle)
                : 0f;
            float brake = controlEnabled || brakeAllowedWhenControlDisabled
                ? Mathf.Clamp01(inputProvider.Brake)
                : 0f;
            if (bananaStunned)
            {
                throttle = 0f;
                brake = 0f;
            }
            debugSteering = steering;
            debugThrottle = throttle;
            debugBrake = brake;
            RecordMotionSample(localVelocity, steering, throttle, brake);

            smoothedSteering = Mathf.MoveTowards(
                smoothedSteering,
                steering,
                7f * Time.fixedDeltaTime);

            if (!bananaStunned)
            {
                AssistLowStep(support, throttle, brake);
                ApplyDrive(throttle, brake, ForwardSpeed, controlEnabled);
            }
            else
            {
                // Keep some forward momentum instead of applying the normal coasting brake.
                debugAccelerationFactor = 0f;
            }

            bool driftHeld =
                (inputProviderSource is IKartGestureInput gestures && gestures.Drift)
                || (inputProviderSource is PoseKart.AI.AIInputProvider ai && ai.Drift);
            bool nextDrift = controlEnabled && !bananaStunned && grounded && ForwardSpeed > 6f
                && driftHeld && Mathf.Abs(smoothedSteering) > .15f && brake <= 0f;
            IsDrifting = nextDrift;
            driftCharge = IsDrifting ? Mathf.Min(MaxNitroChargeAmount, driftCharge + Time.fixedDeltaTime) : 0f;
            if (IsDrifting)
                AddNitro(driftNitroPerSecond * Time.fixedDeltaTime);
            ApplyCornerSpeedLoss(smoothedSteering, ForwardSpeed);
            ApplySteering(ForwardSpeed, throttle, brake);
            ApplyStability(localVelocity);
            LimitGroundContactImpulses();
            ClampPlanarSpeed();
        }

        // Air Time fills the Nitro bar WHILE flying, after a short bump filter.
        // No landing bonus is awarded, so respawn/reset cannot duplicate the reward.
        private void UpdateNitroAirTime()
        {
            if (!controlEnabled || IsBananaStunned)
            {
                ResetNitroAirTime();
                return;
            }

            if (nitroWasGrounded && !grounded)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up);
                nitroAirborneActive = planarVelocity.magnitude > minimumTakeoffSpeed;
                nitroAirborneSeconds = 0f;
                nitroAirAwarded = 0f;
            }

            if (!grounded && nitroAirborneActive)
            {
                nitroAirborneSeconds += Time.fixedDeltaTime;
                float eligibleSeconds = Mathf.Max(0f, nitroAirborneSeconds - minimumRewardAirTime);
                float totalReward = Mathf.Min(maximumAirNitroReward,
                    eligibleSeconds * airNitroPerSecond);
                float delta = totalReward - nitroAirAwarded;
                if (delta > 0f)
                    AddNitro(delta);
                nitroAirAwarded = totalReward;
            }

            if (grounded && !nitroWasGrounded)
            {
                nitroAirborneActive = false;
                nitroAirborneSeconds = 0f;
                nitroAirAwarded = 0f;
            }
            nitroWasGrounded = grounded;
        }

        private void ResetNitroAirTime()
        {
            nitroAirborneActive = false;
            nitroAirborneSeconds = 0f;
            nitroAirAwarded = 0f;
            nitroWasGrounded = grounded;
        }

        public void Configure(Rigidbody body, MonoBehaviour provider)
        {
            targetBody = body;
            SetInputProvider(provider);
            ApplyRigidbodySetup();
        }

        public void SetInputProvider(MonoBehaviour provider)
        {
            if (provider != null && !(provider is ICarInputProvider))
            {
                Debug.LogError($"{provider.GetType().Name} does not implement {nameof(ICarInputProvider)}.", this);
                return;
            }

            inputProviderSource = provider;
            inputProvider = provider as ICarInputProvider;
            enabled = inputProvider != null;
        }

        public void SetControlPermission(bool allowDriving, bool allowBrakingWhenDisabled = false)
        {
            controlEnabled = allowDriving;
            brakeAllowedWhenControlDisabled = !allowDriving && allowBrakingWhenDisabled;
            if (!allowDriving)
            {
                StopNitro();
                ResetNitroAirTime();
                bananaStunUntil = 0f;
                ResetLandingRecovery();
            }
        }

        private void ApplyDrive(float throttle, float brake, float forwardSpeed, bool allowReverse)
        {
            debugAccelerationFactor = 0f;
            if (!grounded) return;
            // Assist uphill travel, but retain gravity acceleration on downhill sections.
            if ((throttle > 0f && driveForward.y > 0f) || (brake > 0f && driveForward.y < 0f)) targetBody.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, groundNormal) * .9f, ForceMode.Acceleration);
            if (brake > 0f)
            {
                if (forwardSpeed > brakeToReverseSpeed)
                {
                    targetBody.AddForce(-driveForward * (brake * brakeDeceleration), ForceMode.Acceleration);
                }
                else if (forwardSpeed < -brakeToReverseSpeed && !allowReverse)
                {
                    targetBody.AddForce(driveForward * (brake * brakeDeceleration), ForceMode.Acceleration);
                }
                else if (allowReverse && forwardSpeed > -maxReverseSpeed)
                {
                    targetBody.AddForce(-driveForward * (brake * reverseAcceleration), ForceMode.Acceleration);
                }

                return;
            }

            if (throttle > 0f && forwardSpeed < SpeedLimit)
            {
                float normalizedForwardSpeed = Mathf.Clamp01(Mathf.Max(0f, forwardSpeed) / SpeedLimit);
                float falloff = Mathf.Pow(normalizedForwardSpeed, accelerationFalloffPower);
                debugAccelerationFactor = Mathf.Lerp(1f, accelerationAtTopSpeed, falloff);
                // A hard landing briefly limits engine response without disabling braking or steering.
                targetBody.AddForce(driveForward * (throttle * LandingThrottleScale * acceleration *
                    debugAccelerationFactor * BoostAccelerationMultiplier), ForceMode.Acceleration);
            }
            else if (throttle <= 0f && Mathf.Abs(forwardSpeed) > 0.05f)
            {
                targetBody.AddForce(-transform.forward * (forwardSpeed * coastingResistance), ForceMode.Acceleration);
            }
        }

        // Small arcade tire-climb allowance, not a jump or an obstacle teleport.
        private void AssistLowStep(RaycastHit support, float throttle, float brake)
        {
            if (!grounded || !controlEnabled || stuntDuration > 0f || Time.time < launchedUntil) return;
            float direction = brake > 0f && ForwardSpeed < .75f ? -1f : throttle > 0f ? 1f : 0f;
            if (direction == 0f) return;
            Vector3 travel = Vector3.ProjectOnPlane(transform.forward * direction, Vector3.up).normalized;
            // Sweep the chassis to detect the actual leading edge, including in reverse.
            float reach = Mathf.Clamp(Mathf.Abs(ForwardSpeed) * Time.fixedDeltaTime + .18f, .18f, .8f);
            if (!targetBody.SweepTest(travel, out RaycastHit obstacle, reach, QueryTriggerInteraction.Ignore)) return;
            if (obstacle.collider.attachedRigidbody != null || Mathf.Abs(obstacle.normal.y) > .45f) return;
            if (obstacle.collider.GetComponentInParent<TrackReturnBoundary>() != null) return;
            Vector3 sample = obstacle.point + travel * .18f;
            sample.y = support.point.y + .5f;
            if (!VehicleGroundProbe.Cast(targetBody, sample, .55f, out RaycastHit top) || top.normal.y < .75f) return;
            float rise = top.point.y - support.point.y;
            if (rise < .025f || rise > .35f) return;
            // Require support beyond the lip; never climb a tall wall or launch ramp.
            if (!VehicleGroundProbe.Cast(targetBody, sample + travel * .25f, .55f, out RaycastHit beyond)
                || Mathf.Abs(beyond.point.y - top.point.y) > .12f) return;
            float lift = rise + .025f;
            if (targetBody.SweepTest(Vector3.up, out _, lift, QueryTriggerInteraction.Ignore)) return;
            targetBody.position += Vector3.up * lift;
        }

        private void ApplyCornerSpeedLoss(float steering, float forwardSpeed)
        {
            if (!grounded) return;
            float steeringFactor = Mathf.InverseLerp(
                cornerPenaltySteeringThreshold,
                1f,
                Mathf.Abs(steering));
            float speedFactor = Mathf.InverseLerp(
                cornerPenaltyStartSpeed,
                maxForwardSpeed,
                Mathf.Abs(forwardSpeed));
            debugSteeringSpeedPenalty = Mathf.Min(maximumCornerDeceleration, 2.5f)
                                        * Mathf.Pow(steeringFactor, cornerPenaltyPower)
                                        * speedFactor;

            if (debugSteeringSpeedPenalty <= 0f || Mathf.Abs(forwardSpeed) <= 0.05f)
            {
                return;
            }

            float travelDirection = Mathf.Sign(forwardSpeed);
            targetBody.AddForce(
                -transform.forward * (travelDirection * debugSteeringSpeedPenalty),
                ForceMode.Acceleration);
        }

        private void ApplySteering(float forwardSpeed, float throttle, float brake)
        {
            if (!grounded)
            {
                yawRate = 0f;
                debugAirborne = true;
                debugAirSteering = smoothedSteering;
                debugAirControlActive = enableAirControl && controlEnabled && stuntDuration <= 0f;
                if (debugAirControlActive)
                {
                    ApplyAirControl(throttle, brake);
                }
                else
                {
                    debugAirYawRate = Vector3.Dot(targetBody.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
                }
                if (stuntDuration > 0f && controlEnabled)
                {
                    float desired = smoothedSteering * 7f;
                    float next = Mathf.MoveTowards(airLateralSpeed, desired, 20f * Time.fixedDeltaTime);
                    next = Mathf.Clamp(next, (-4.5f - airLateralOffset) / Time.fixedDeltaTime, (4.5f - airLateralOffset) / Time.fixedDeltaTime);
                    targetBody.linearVelocity += stuntRight * (next - airLateralSpeed);
                    airLateralOffset += next * Time.fixedDeltaTime;
                    airLateralSpeed = next;
                }
                return;
            }
            debugAirborne = false;
            debugAirControlActive = false;
            debugAirSteering = 0f;
            debugAirYawRate = 0f;
            float absoluteSpeed = Mathf.Abs(forwardSpeed);
            float normalizedSpeed = Mathf.Clamp01(absoluteSpeed / maxForwardSpeed);
            // Linear response: the old bicycle formula saturated even at small inputs.
            float desiredRate = smoothedSteering * (IsDrifting ? 112f : 82f) * Mathf.Deg2Rad * Mathf.Clamp01(absoluteSpeed / 4f) * Mathf.Sign(forwardSpeed);
            yawRate = Mathf.MoveTowards(yawRate, desiredRate, 7f * Time.fixedDeltaTime);
            // Smooth terrain alignment only: smoothing the complete rotation also attenuates
            // every frame's steering increment, removing most of the requested turn rate.
            Vector3 up = Vector3.Slerp(transform.up, groundNormal, 1f - Mathf.Exp(-14f * Time.fixedDeltaTime)).normalized;
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            heading = Quaternion.AngleAxis(yawRate * Mathf.Rad2Deg * Time.fixedDeltaTime, up) * heading;
            targetBody.MoveRotation(Quaternion.LookRotation(heading, up));
        }

        private void ApplyAirControl(float throttle, float brake)
        {
            float maxRate = Mathf.Max(1f, maximumAirRotationSpeed) * Mathf.Deg2Rad;
            float currentYawRate = Vector3.Dot(targetBody.angularVelocity, Vector3.up);
            float desiredYawRate = Mathf.Clamp(smoothedSteering, -1f, 1f) * maxRate;
            float yawRateForCorrection = Mathf.Clamp(currentYawRate, -maxRate, maxRate);
            float yawError = desiredYawRate - yawRateForCorrection;
            float responsiveness = Mathf.Max(0.01f, airSteeringResponsiveness);
            float yawAcceleration = Mathf.Clamp(
                yawError * responsiveness,
                -Mathf.Max(0f, airYawStrength),
                Mathf.Max(0f, airYawStrength));

            // A torque-based correction preserves the Rigidbody's airborne motion while
            // preventing steering from building an uncontrolled spin rate.
            targetBody.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);
            debugAirYawRate = currentYawRate * Mathf.Rad2Deg;

            if (airPitchControl)
            {
                float pitchInput = Mathf.Clamp(throttle - brake, -1f, 1f);
                ApplyAirAxisTorque(transform.right, -pitchInput * maxRate * 0.5f, responsiveness, airYawStrength * 0.5f, maxRate * 0.65f);
            }

            if (airRollControl)
            {
                ApplyAirAxisTorque(transform.forward, -smoothedSteering * maxRate * 0.5f, responsiveness, airYawStrength * 0.5f, maxRate * 0.65f);
            }

            if (airLateralAssist && airLateralAssistStrength > 0f)
            {
                Vector3 lateralAxis = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                if (lateralAxis.sqrMagnitude > 0.001f)
                {
                    targetBody.AddForce(
                        lateralAxis * (smoothedSteering * airLateralAssistStrength),
                        ForceMode.Acceleration);
                }
            }
        }

        private void ApplyAirAxisTorque(
            Vector3 axis,
            float desiredRate,
            float responsiveness,
            float maximumAcceleration,
            float maximumRate)
        {
            float currentRate = Vector3.Dot(targetBody.angularVelocity, axis);
            float clampedRate = Mathf.Clamp(currentRate, -maximumRate, maximumRate);
            float acceleration = Mathf.Clamp(
                (desiredRate - clampedRate) * responsiveness,
                -Mathf.Max(0f, maximumAcceleration),
                Mathf.Max(0f, maximumAcceleration));
            targetBody.AddTorque(axis * acceleration, ForceMode.Acceleration);
        }

        private void ApplyStability(Vector3 localVelocity)
        {
            if (!grounded) return;
            Vector3 right = Vector3.Cross(groundNormal, driveForward).normalized;
            float sideSpeed = Vector3.Dot(targetBody.linearVelocity, right);
            bool impactRecovering = Time.time < impactRecoveryUntil;
            // During banana stun, do not instantly straighten the kart's sideways motion.
            // The previous arcade stability correction erased most of the banana impulse.
            bool bananaStunned = IsBananaStunned;
            if (!impactRecovering && !bananaStunned && Time.time >= oilSlipUntil)
            {
                Vector3 planar = Vector3.ProjectOnPlane(targetBody.linearVelocity, groundNormal);
                float slipAngle = IsDrifting ? -smoothedSteering * 10f : 0f;
                Vector3 travel = Quaternion.AngleAxis(slipAngle, groundNormal) * driveForward * Mathf.Sign(ForwardSpeed);
                Vector3 desired = travel * planar.magnitude;
                Vector3 blended = Vector3.Slerp(planar, desired, 1f - Mathf.Exp(-(IsDrifting ? 14f : 18f) * Time.fixedDeltaTime));
                targetBody.linearVelocity = blended + groundNormal * Vector3.Dot(targetBody.linearVelocity, groundNormal);
            }
            else if (!impactRecovering && !bananaStunned && Mathf.Abs(localVelocity.x) > 0.01f)
            {
                targetBody.AddForce(
                    -right * (sideSpeed * lateralGrip * .12f),
                    ForceMode.Acceleration);
            }

            float planarSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            float downforceAcceleration = baseDownforce + planarSpeed * speedDownforce;
            if (!impactRecovering && downforceAcceleration > 0f)
            {
                targetBody.AddForce(
                    -groundNormal * downforceAcceleration,
                    ForceMode.Acceleration);
            }
        }

        private void ClampPlanarSpeed()
        {
            if (!grounded) return;
            Vector3 velocity = targetBody.linearVelocity;
            Vector3 right = Vector3.Cross(groundNormal, driveForward).normalized;
            // Release boost speed progressively rather than snapping 130 -> 80 km/h.
            float forwardLimit = Mathf.Max(SpeedLimit, smoothedSpeedLimit);
            float forward = Mathf.Clamp(Vector3.Dot(velocity, driveForward), -maxReverseSpeed, forwardLimit);
            float lateralLimit = IsDrifting ? 18f : Mathf.Max(8f, maximumLateralSpeed);
            float lateral = Mathf.Clamp(Vector3.Dot(velocity, right), -lateralLimit, lateralLimit);
            targetBody.linearVelocity = driveForward * forward + right * lateral + groundNormal * Vector3.Dot(velocity, groundNormal);
        }

        private void LimitGroundContactImpulses()
        {
            if (Time.time < launchedUntil || !grounded) return;
            Vector3 velocity = targetBody.linearVelocity;
            float separatingSpeed = Vector3.Dot(velocity, groundNormal);
            if (separatingSpeed <= maximumGroundedUpwardSpeed)
            {
                return;
            }

            // Thin, overlapping road pieces can briefly generate a large separation impulse
            // at a seam. Preserve normal bumps, but prevent that contact impulse from launching
            // an arcade kart that is still directly above a support surface.
            velocity -= groundNormal * (separatingSpeed - maximumGroundedUpwardSpeed);
            targetBody.linearVelocity = velocity;
        }

        private bool IsNearSupportSurface()
        {
            int hitCount = Physics.RaycastNonAlloc(
                targetBody.worldCenterOfMass + Vector3.up * 0.05f,
                Vector3.down,
                groundProbeHits,
                groundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = groundProbeHits[index];
                if (hit.collider == null || hit.collider.attachedRigidbody == targetBody)
                {
                    continue;
                }

                if (Vector3.Dot(hit.normal, Vector3.up) >= 0.6f)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordMotionSample(Vector3 localVelocity, float steering, float throttle, float brake)
        {
            Vector3 observedVelocity = targetBody.linearVelocity;
            if (hasPreviousObservedVelocity)
            {
                Vector3 previousPlanar = Vector3.ProjectOnPlane(previousObservedVelocity, Vector3.up);
                Vector3 currentPlanar = Vector3.ProjectOnPlane(observedVelocity, Vector3.up);
                debugObservedSpeedDrop = Mathf.Max(0f, previousPlanar.magnitude - currentPlanar.magnitude);
                debugObservedVerticalDelta = observedVelocity.y - previousObservedVelocity.y;
                debugLastContactCollider = recentContactCollider;
                debugLastContactImpulse = recentContactImpulse;
                debugLastContactUpDot = recentContactUpDot;

            }

            previousObservedVelocity = observedVelocity;
            hasPreviousObservedVelocity = true;
            previousSteering = steering;
            previousThrottle = throttle;
            previousBrake = brake;
            recentContactCollider = "None";
            recentContactImpulse = 0f;
            recentContactUpDot = 1f;
        }

        private void CaptureCollisionSnapshot(Collision collision)
        {
            float impulse = collision.impulse.magnitude;
            if (impulse < recentContactImpulse)
            {
                return;
            }

            float minimumUpDot = 1f;
            for (int index = 0; index < collision.contactCount; index++)
            {
                minimumUpDot = Mathf.Min(
                    minimumUpDot,
                    Vector3.Dot(collision.GetContact(index).normal, Vector3.up));
            }

            recentContactCollider = collision.collider != null ? collision.collider.name : "Unknown";
            recentContactImpulse = impulse;
            recentContactUpDot = minimumUpDot;
        }

        private void OnCollisionEnter(Collision collision)
        {
            CaptureCollisionSnapshot(collision);
            if (IsShockwaveActive)
            {
                CarController victim = collision.collider.GetComponentInParent<CarController>();
                if (victim != null && victim != this && !victim.IsShockwaveActive &&
                    Time.time >= nextShockwaveHitTime && collision.relativeVelocity.magnitude >= shockwaveMinimumImpactSpeed)
                {
                    nextShockwaveHitTime = Time.time + shockwaveHitCooldown;
                    Vector3 point = GetCollisionPoint(collision);
                    KartItemDefense victimDefense = victim.GetComponent<KartItemDefense>();
                    if (victimDefense == null || !victimDefense.TryBlockMissileExplosion(point))
                    {
                        Vector3 away = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up).normalized;
                        victim.ApplyMissileExplosionImpact(away * shockwavePush + Vector3.up * shockwaveLift, 0.6f, point, shockwaveTorque);
                    }
                }
                debugLastCollisionSpeedLoss = 0f;
                return;
            }

            // Support contacts must never be interpreted as an arcade crash.  Road pieces
            // are identified by hierarchy name because the generated ML5 track has no tag
            // or layer dependency, while the normal test also protects other flat ground.
            if (IsDrivableSurface(collision.collider) || IsGroundContact(collision))
            {
                return;
            }

            Vector3 planarRelativeVelocity = Vector3.ProjectOnPlane(collision.relativeVelocity, transform.up);
            float planarImpactSpeed = planarRelativeVelocity.magnitude;
            if (planarImpactSpeed <= 0.01f)
            {
                return;
            }

            float frontalAlignment = Mathf.Abs(
                Vector3.Dot(planarRelativeVelocity / planarImpactSpeed, transform.forward));
            float impactWeight = Mathf.Lerp(
                sideImpactLossMultiplier,
                1f,
                frontalAlignment * frontalAlignment);
            float weightedImpactSpeed = planarImpactSpeed * impactWeight;
            debugLastCollisionImpact = weightedImpactSpeed;

            KartItemDefense defense = GetComponent<KartItemDefense>();
            CarController otherCar = collision.collider != null
                ? collision.collider.GetComponentInParent<CarController>()
                : null;
            if (defense != null && defense.TryBlockMajorCollision(
                    GetCollisionPoint(collision),
                    otherCar != null && otherCar != this,
                    collision.relativeVelocity.magnitude))
            {
                debugLastCollisionSpeedLoss = 0f;
                lastCollisionLossTime = Time.time;
                return;
            }

            if (weightedImpactSpeed < minimumImpactSpeed ||
                Time.time < lastCollisionLossTime + collisionLossCooldown)
            {
                return;
            }

            float speedLoss = Mathf.Min(
                maximumCollisionSpeedLoss,
                (weightedImpactSpeed - minimumImpactSpeed) * collisionSpeedLossMultiplier);
            if (speedLoss <= 0f)
            {
                return;
            }

            Vector3 velocity = targetBody.linearVelocity;
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            localVelocity.z = Mathf.MoveTowards(localVelocity.z, 0f, speedLoss);
            localVelocity.x = Mathf.MoveTowards(localVelocity.x, 0f, speedLoss * 0.25f);
            Vector3 reducedPlanarVelocity = transform.TransformDirection(
                new Vector3(localVelocity.x, 0f, localVelocity.z));
            targetBody.linearVelocity = new Vector3(
                reducedPlanarVelocity.x,
                velocity.y,
                reducedPlanarVelocity.z);

            debugLastCollisionSpeedLoss = speedLoss;
            lastCollisionLossTime = Time.time;
        }

        private void OnCollisionStay(Collision collision)
        {
            CaptureCollisionSnapshot(collision);
        }

        private static Vector3 GetCollisionPoint(Collision collision)
        {
            return collision != null && collision.contactCount > 0
                ? collision.GetContact(0).point
                : Vector3.zero;
        }

        private static bool IsDrivableSurface(Collider col)
        {
            if (col == null)
            {
                return false;
            }

            Transform current = col.transform;
            while (current != null)
            {
                string currentName = current.name;
                if (currentName.StartsWith("Road", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Track", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Ground", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Sand", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Ramp", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("SmallJumpRamp", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("BoostPad", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private bool IsGroundContact(Collision collision)
        {
            int contactCount = collision.contactCount;
            if (contactCount == 0)
            {
                return false;
            }

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (Vector3.Dot(contact.normal, transform.up) <= 0.6f)
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyRigidbodySetup()
        {
            if (targetBody == null)
            {
                return;
            }

            targetBody.centerOfMass = centerOfMassOffset;
            targetBody.linearDamping = linearDamping;
            ApplyLowFrictionColliderSetup();
            if (lockPitchAndRoll)
            {
                targetBody.constraints &= ~(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ);
            }
        }

        private void ApplyLowFrictionColliderSetup()
        {
            foreach (Collider vehicleCollider in GetComponentsInChildren<Collider>(true))
            {
                if (vehicleCollider.isTrigger)
                {
                    continue;
                }

                PhysicsMaterial material = vehicleCollider.material;
                material.staticFriction = 0f;
                material.dynamicFriction = 0f;
                material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }

        private void ResolveInputProvider()
        {
            inputProvider = inputProviderSource as ICarInputProvider;
            if (inputProvider == null)
            {
                Debug.LogError(
                    $"{nameof(CarController)} on {name} requires a component that implements {nameof(ICarInputProvider)}.",
                    this);
                enabled = false;
            }
        }

        private void OnValidate()
        {
            if (inputProviderSource != null && !(inputProviderSource is ICarInputProvider))
            {
                Debug.LogError(
                    $"Assigned input provider must implement {nameof(ICarInputProvider)}.",
                    this);
            }
        }
    }
}
