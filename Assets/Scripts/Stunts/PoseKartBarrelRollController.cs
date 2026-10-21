using System;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.Events;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Asphalt 8-style Barrel Roll stunt controller.
    /// Rotates a visual-only pivot while airborne off stunt ramps, keeping Rigidbody physics & colliders intact.
    /// Supports dynamic multi-rolls (1x, 2x, 3x...), aerodynamic loft, smooth auto-upright landing recovery,
    /// and stacked Nitro rewards.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    public sealed class PoseKartBarrelRollController : MonoBehaviour
    {
        private enum State
        {
            Idle,
            Armed,
            Rolling,
            LandingAligning
        }

        [Header("References")]
        [Tooltip("Visual-only transform to rotate. If unassigned, auto-finds 'StuntPivot' or first visual child.")]
        [SerializeField] private Transform visualPivot;
        [SerializeField] private CarController car;
        [SerializeField] private Rigidbody body;

        [Header("Visual Roll Dynamics")]
        [Tooltip("Local roll axis (usually Vector3.forward).")]
        [SerializeField] private Vector3 localRollAxis = Vector3.forward;
        [Tooltip("Evaluation curve for each 360-degree rotation cycle.")]
        [UnityEngine.Serialization.FormerlySerializedAs("rollCurve")]
        [SerializeField] private AnimationCurve cycleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("Subtle corkscrew tilt angle to give an authentic 3D arc instead of a rigid spit-spin.")]
        [SerializeField] private bool enableCorkscrewTilt = true;
        [SerializeField, Range(0f, 15f)] private float corkscrewPitchAngle = 5f;

        [Header("Asphalt 8 Arcade Flight")]
        [Tooltip("Upward loft counteracting part of gravity while rolling, giving that iconic soaring flight arc.")]
        [SerializeField] private bool enableAirLoft = true;
        [SerializeField, Range(0f, 0.75f)] private float airLoftGravityCompensation = 0.35f;
        [Tooltip("Duration in seconds to smoothly ease wheels flat upright upon touching ground, preventing visual snaps.")]
        [SerializeField, Range(0.05f, 0.3f)] private float landingAlignDuration = 0.12f;

        [Header("Safety & Limits")]
        [SerializeField, Min(1f)] private float maxStuntSeconds = 5f;
        [SerializeField, Min(1f)] private float teleportCancelDistance = 25f;
        [SerializeField] private bool cancelIfControlDisabled = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onSuccessfulLanding = new UnityEvent();

        public event Action<PoseKartBarrelRollController> BarrelRollLanded;
        public event Action<int> RollStepCompleted;

        public float LastRewardFraction { get; private set; }
        public int CompletedRolls { get; private set; }
        public bool IsRolling => state == State.Rolling || state == State.LandingAligning;

        private State state = State.Idle;
        private PoseKartBarrelRollRamp ramp;
        private Quaternion baseRotation = Quaternion.identity;
        private Quaternion landingContactRotation = Quaternion.identity;
        private Vector3 previousPosition;

        private float armDeadline;
        private float rollStartTime;
        private float rollDurationPerCycle = 0.8f;
        private int rollDirection = 1;

        private float totalRollProgress;
        private int lastFiredRollCount;
        private float alignStartTime;

        private void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null && car != null) body = car.TargetBody;

            // Auto-resolve visual pivot if not manually set in Inspector
            if (visualPivot == null)
            {
                visualPivot = AutoResolveVisualPivot();
            }

            if (visualPivot != null)
            {
                baseRotation = visualPivot.localRotation;
            }

            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            if (visualPivot != null && state == State.Idle)
            {
                baseRotation = visualPivot.localRotation;
            }
        }

        private Transform AutoResolveVisualPivot()
        {
            // 1. Look for dedicated StuntPivot child
            Transform pivot = transform.Find("StuntPivot");
            if (pivot != null) return pivot;

            // 2. Search common model/visual child names
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

            // 3. Fallback: first child that has a renderer
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && r.transform != transform)
            {
                return r.transform;
            }

            return null;
        }

        /// <summary>
        /// Arms the vehicle for a barrel roll when entering a stunt ramp trigger.
        /// </summary>
        public bool TryArm(PoseKartBarrelRollRamp source)
        {
            if (state != State.Idle || !isActiveAndEnabled || source == null ||
                car == null || body == null || !car.IsGrounded || !car.ControlEnabled)
            {
                return false;
            }

            if (visualPivot == null)
            {
                visualPivot = AutoResolveVisualPivot();
                if (visualPivot == null)
                {
                    Debug.LogWarning("[PoseKartBarrelRollController] Cannot arm: No visualPivot found on " + name, this);
                    return false;
                }
            }

            Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (Vector3.Dot(horizontal, transform.forward) < source.MinimumForwardSpeed)
            {
                return false;
            }

            ramp = source;
            armDeadline = Time.time + source.ArmWindow;
            rollDurationPerCycle = Mathf.Max(0.3f, source.RollDuration);
            rollDirection = source.RollDirection;
            baseRotation = visualPivot.localRotation;

            state = State.Armed;
            previousPosition = transform.position;
            CompletedRolls = 0;
            lastFiredRollCount = 0;
            totalRollProgress = 0f;

            // Apply launch assist if requested by ramp
            if (source.ApplyLaunchImpulse)
            {
                Vector3 impulse = (Vector3.up * source.UpwardVelocityChange) +
                                  (transform.forward * source.ForwardVelocityChange);
                body.AddForce(impulse, ForceMode.VelocityChange);
            }

            return true;
        }

        private void FixedUpdate()
        {
            // Aerodynamic air loft: Counteracts portion of gravity to produce soaring Asphalt 8 flight arc
            if (state == State.Rolling && enableAirLoft && body != null && !body.isKinematic)
            {
                float counterGravity = Physics.gravity.magnitude * airLoftGravityCompensation;
                body.AddForce(Vector3.up * counterGravity, ForceMode.Acceleration);
            }
        }

        private void Update()
        {
            if (state == State.Idle)
            {
                previousPosition = transform.position;
                return;
            }

            // Safety guards (teleport, control disabled, missing references)
            if (visualPivot == null || car == null || body == null ||
                (cancelIfControlDisabled && !car.ControlEnabled) ||
                Vector3.Distance(transform.position, previousPosition) > teleportCancelDistance)
            {
                CancelStunt();
                previousPosition = transform.position;
                return;
            }
            previousPosition = transform.position;

            switch (state)
            {
                case State.Armed:
                    UpdateArmedState();
                    break;

                case State.Rolling:
                    UpdateRollingState();
                    break;

                case State.LandingAligning:
                    UpdateLandingAlignState();
                    break;
            }
        }

        private void UpdateArmedState()
        {
            if (Time.time > armDeadline)
            {
                CancelStunt();
                return;
            }

            // Once wheels leave ground, immediately initiate roll
            if (!car.IsGrounded)
            {
                baseRotation = visualPivot.localRotation;
                rollStartTime = Time.time;
                totalRollProgress = 0f;
                CompletedRolls = 0;
                lastFiredRollCount = 0;
                state = State.Rolling;
            }
        }

        private void UpdateRollingState()
        {
            if (Time.time - rollStartTime > maxStuntSeconds)
            {
                CancelStunt();
                return;
            }

            // Check for landing contact
            if (car.IsGrounded)
            {
                HandleTouchdown();
                return;
            }

            // Progress continuously based on time in air
            float elapsed = Time.time - rollStartTime;
            totalRollProgress = elapsed / rollDurationPerCycle;

            int currentCompletedCycles = Mathf.FloorToInt(totalRollProgress);
            if (currentCompletedCycles > lastFiredRollCount)
            {
                lastFiredRollCount = currentCompletedCycles;
                CompletedRolls = currentCompletedCycles;
                RollStepCompleted?.Invoke(CompletedRolls);
            }

            // Calculate current rotation
            float cycleFraction = totalRollProgress % 1.0f;
            float evaluatedFraction = cycleCurve != null ? Mathf.Clamp01(cycleCurve.Evaluate(cycleFraction)) : cycleFraction;
            float totalAngle = (currentCompletedCycles + evaluatedFraction) * 360f * rollDirection;

            Vector3 axis = localRollAxis.sqrMagnitude > 0.0001f ? localRollAxis.normalized : Vector3.forward;
            Quaternion rollQuat = Quaternion.AngleAxis(totalAngle, axis);

            // Optional corkscrew dynamic pitch wobble for realistic 3D feel
            Quaternion corkscrewQuat = Quaternion.identity;
            if (enableCorkscrewTilt && corkscrewPitchAngle > 0.01f)
            {
                float pitchWobble = Mathf.Sin(totalAngle * Mathf.Deg2Rad) * corkscrewPitchAngle * rollDirection;
                corkscrewQuat = Quaternion.Euler(pitchWobble, 0f, 0f);
            }

            visualPivot.localRotation = baseRotation * rollQuat * corkscrewQuat;
        }

        private void HandleTouchdown()
        {
            landingContactRotation = visualPivot.localRotation;
            alignStartTime = Time.time;

            if (CompletedRolls >= 1)
            {
                // Successful multi-roll landing!
                float baseFraction = ramp != null ? ramp.NitroRewardFraction : 0.15f;
                LastRewardFraction = Mathf.Clamp01(baseFraction * CompletedRolls);

                if (LastRewardFraction > 0f)
                {
                    car.AddNitro(car.MaxNitroChargeAmount * LastRewardFraction);
                }

                onSuccessfulLanding.Invoke();
                BarrelRollLanded?.Invoke(this);
            }
            else
            {
                // Landed before finishing 1st roll: incomplete stunt, no reward
                LastRewardFraction = 0f;
            }

            state = State.LandingAligning;
        }

        private void UpdateLandingAlignState()
        {
            float elapsed = Time.time - alignStartTime;
            float t = landingAlignDuration > 0.001f ? Mathf.Clamp01(elapsed / landingAlignDuration) : 1f;

            // Smooth ease out to flat upright base rotation
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            visualPivot.localRotation = Quaternion.Slerp(landingContactRotation, baseRotation, smoothT);

            if (t >= 1f)
            {
                visualPivot.localRotation = baseRotation;
                state = State.Idle;
                ramp = null;
            }
        }

        /// <summary>
        /// Immediately cancels stunt and restores base visual rotation safely.
        /// </summary>
        public void CancelStunt()
        {
            if (visualPivot != null && (state == State.Rolling || state == State.LandingAligning || state == State.Armed))
            {
                visualPivot.localRotation = baseRotation;
            }
            state = State.Idle;
            ramp = null;
            CompletedRolls = 0;
            lastFiredRollCount = 0;
            totalRollProgress = 0f;
        }

        private void OnDisable()
        {
            CancelStunt();
        }
    }
}
