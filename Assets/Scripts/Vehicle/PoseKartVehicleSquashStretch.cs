using System;
using PoseKart.Race;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Visual-only arcade feedback for a kart. It deliberately operates on the
    /// visual child instead of the Rigidbody root, so colliders and vehicle
    /// physics remain unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PoseKart/Vehicle Squash & Stretch")]
    public sealed class PoseKartVehicleSquashStretch : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform hood;
        [SerializeField] private Light[] headlights = Array.Empty<Light>();
        [SerializeField] private Transform[] frontWheels = Array.Empty<Transform>();

        [Header("Effect")]
        [SerializeField] private bool enableEffects = true;
        [SerializeField, Min(0f)] private float accelerationStretch = 0.045f;
        [SerializeField, Min(0f)] private float brakingSquash = 0.1f;
        [SerializeField, Min(0f)] private float landingSquash = 0.12f;
        [SerializeField, Min(0.05f)] private float recoverySpeed = 8f;

        [Header("Idle Bounce")]
        [SerializeField] private bool enableIdleBounce = true;
        [SerializeField, Min(0f)] private float idleBounceHeight = 0.025f;
        [SerializeField, Min(0f)] private float idleBreathingScale = 0.018f;
        [SerializeField, Min(0.1f)] private float idleFrequency = 1.8f;

        [Header("Impact Expression")]
        [SerializeField] private bool enableImpactExpression = true;
        [SerializeField, Min(0f)] private float impactShakeAngle = 8f;
        [SerializeField, Min(0f)] private float impactShakeDuration = 0.35f;
        [SerializeField, Min(0f)] private float hoodOpenAngle = 18f;
        [SerializeField, Min(0f)] private float headlightBlinkDuration = 0.45f;
        [SerializeField, Min(0f)] private float wheelReactionAngle = 10f;

        [Header("Runtime Debug")]
        [InspectorName("Idle Animation Active")]
        [SerializeField] private bool debugIdleActive;
        [InspectorName("Grounded")]
        [SerializeField] private bool debugGrounded;
        [InspectorName("Forward Acceleration")]
        [SerializeField] private float debugAcceleration;
        [InspectorName("Current Deformation")]
        [SerializeField] private float debugDeformation;
        [InspectorName("Impact Animation")]
        [SerializeField] private float debugImpact;

        private CarController car;
        private Rigidbody body;
        private RaceManager raceManager;
        private Vector3 baseVisualPosition;
        private Vector3 baseVisualScale;
        private Quaternion baseVisualRotation;
        private Quaternion baseHoodRotation;
        private Quaternion[] baseWheelRotations = Array.Empty<Quaternion>();
        private bool[] baseHeadlightStates = Array.Empty<bool>();
        private float previousForwardSpeed;
        private float accelerationBlend;
        private float brakingBlend;
        private float landingPulse;
        private float impactPulse;
        private float impactTimer;
        private float blinkTimer;
        private float impactSide;
        private bool previousGrounded;
        private bool initialized;

        private void Awake()
        {
            car = GetComponent<CarController>();
            body = car != null ? car.TargetBody : GetComponent<Rigidbody>();
            raceManager = FindFirstObjectByType<RaceManager>();

            ResolveReferences();
            if (visualRoot == null)
                return;

            baseVisualPosition = visualRoot.localPosition;
            baseVisualScale = visualRoot.localScale;
            baseVisualRotation = visualRoot.localRotation;
            if (hood != null)
                baseHoodRotation = hood.localRotation;

            baseWheelRotations = new Quaternion[frontWheels.Length];
            for (int i = 0; i < frontWheels.Length; i++)
            {
                if (frontWheels[i] != null)
                    baseWheelRotations[i] = frontWheels[i].localRotation;
            }

            baseHeadlightStates = new bool[headlights.Length];
            for (int i = 0; i < headlights.Length; i++)
            {
                if (headlights[i] != null)
                    baseHeadlightStates[i] = headlights[i].enabled;
            }

            previousGrounded = car == null || car.IsGrounded;
            previousForwardSpeed = GetForwardSpeed();
            initialized = true;
        }

        private void OnEnable()
        {
            if (car == null)
                car = GetComponent<CarController>();
            if (car != null)
                car.MissileImpactEvent += OnMissileImpact;
        }

        private void OnDisable()
        {
            if (car != null)
                car.MissileImpactEvent -= OnMissileImpact;
            RestoreVisuals();
        }

        private void Update()
        {
            if (!initialized || !enableEffects || visualRoot == null || body == null)
                return;

            float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
            bool grounded = car == null || car.IsGrounded;
            float forwardSpeed = GetForwardSpeed();
            float acceleration = (forwardSpeed - previousForwardSpeed) / deltaTime;
            previousForwardSpeed = forwardSpeed;

            float accelerationTarget = Mathf.Clamp01(Mathf.Max(0f, acceleration) / 12f);
            float brakingTarget = Mathf.Clamp01(Mathf.Max(0f, -acceleration) / 18f);
            accelerationBlend = Damp(accelerationBlend, accelerationTarget, 8f, deltaTime);
            brakingBlend = Damp(brakingBlend, brakingTarget, 10f, deltaTime);

            if (!previousGrounded && grounded)
                landingPulse = 1f;
            previousGrounded = grounded;
            landingPulse = Mathf.MoveTowards(landingPulse, 0f, deltaTime * 5f);

            impactTimer = Mathf.Max(0f, impactTimer - deltaTime);
            impactPulse = impactShakeDuration > 0f
                ? Mathf.Clamp01(impactTimer / impactShakeDuration)
                : 0f;
            blinkTimer = Mathf.Max(0f, blinkTimer - deltaTime);

            bool idle = IsIdleAnimationActive(forwardSpeed);
            float idleWave = idle ? Mathf.Sin(Time.time * Mathf.PI * 2f * idleFrequency) : 0f;
            float idleBreath = idle ? Mathf.Sin(Time.time * Mathf.PI * idleFrequency) * idleBreathingScale : 0f;
            float deformation = Mathf.Clamp01(Mathf.Max(brakingBlend * brakingSquash, landingPulse * landingSquash));

            Vector3 targetScale = baseVisualScale;
            targetScale.x *= 1f + deformation * 0.55f + idleBreath;
            targetScale.y *= 1f - deformation + idleBreath * 0.35f;
            targetScale.z *= 1f + accelerationBlend * accelerationStretch - deformation * 0.15f;

            Vector3 targetPosition = baseVisualPosition;
            targetPosition.y += idleWave * idleBounceHeight;
            targetPosition.y -= deformation * 0.035f;
            targetPosition.z -= accelerationBlend * 0.02f;

            Quaternion targetRotation = baseVisualRotation;
            if (enableImpactExpression && impactPulse > 0f)
            {
                float shake = Mathf.Sin(Time.time * 42f) * impactPulse * impactShakeAngle;
                targetRotation *= Quaternion.Euler(shake * 0.55f, shake, -shake * 0.8f);
            }

            float blend = 1f - Mathf.Exp(-recoverySpeed * deltaTime);
            visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, targetScale, blend);
            visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, targetPosition, blend);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetRotation, blend);

            UpdateHood(blend);
            UpdateWheels(blend);
            UpdateHeadlights();

            debugIdleActive = idle;
            debugGrounded = grounded;
            debugAcceleration = acceleration;
            debugDeformation = deformation;
            debugImpact = impactPulse;
        }

        private void ResolveReferences()
        {
            if (visualRoot == null)
                visualRoot = FindChildByName(transform, "visual");
            if (visualRoot == null)
                visualRoot = FindDescendantContaining(transform, "visual", "body", "mesh", "model");
            if (visualRoot == null)
                visualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;

            if (hood == null)
                hood = FindDescendantContaining(transform, "hood", "bonnet", "capot");

            if (headlights == null || headlights.Length == 0)
                headlights = GetComponentsInChildren<Light>(true);

            if (frontWheels == null || frontWheels.Length == 0)
                frontWheels = FindFrontWheels(transform);
        }

        private bool IsIdleAnimationActive(float forwardSpeed)
        {
            if (!enableIdleBounce || Mathf.Abs(forwardSpeed) > 1.5f)
                return false;
            if (raceManager == null)
                return car == null || !car.ControlEnabled;
            return raceManager.State == RaceState.Waiting || raceManager.State == RaceState.Countdown;
        }

        private float GetForwardSpeed()
        {
            if (body == null)
                return 0f;
            return Vector3.Dot(body.linearVelocity, transform.forward);
        }

        private void OnMissileImpact(Vector3 deltaVelocity)
        {
            if (!enableImpactExpression)
                return;
            impactTimer = Mathf.Max(impactTimer, impactShakeDuration);
            landingPulse = Mathf.Max(landingPulse, 0.45f);
            blinkTimer = Mathf.Max(blinkTimer, headlightBlinkDuration);
            impactSide = Mathf.Sign(Vector3.Dot(transform.right, deltaVelocity));
        }

        private void UpdateHood(float blend)
        {
            if (hood == null)
                return;
            Quaternion target = baseHoodRotation;
            if (enableImpactExpression && impactPulse > 0f)
                target = baseHoodRotation * Quaternion.Euler(-impactPulse * hoodOpenAngle, 0f, 0f);
            hood.localRotation = Quaternion.Slerp(hood.localRotation, target, blend);
        }

        private void UpdateWheels(float blend)
        {
            if (!enableImpactExpression || frontWheels == null)
                return;
            float angle = impactSide * impactPulse * wheelReactionAngle;
            for (int i = 0; i < frontWheels.Length; i++)
            {
                Transform wheel = frontWheels[i];
                if (wheel == null)
                    continue;
                Quaternion target = baseWheelRotations[i] * Quaternion.Euler(0f, angle, 0f);
                wheel.localRotation = Quaternion.Slerp(wheel.localRotation, target, blend);
            }
        }

        private void UpdateHeadlights()
        {
            if (!enableImpactExpression || headlights == null || blinkTimer <= 0f)
                return;
            bool visible = Mathf.Sin(Time.time * 55f) > 0f;
            for (int i = 0; i < headlights.Length; i++)
            {
                if (headlights[i] != null)
                    headlights[i].enabled = visible && baseHeadlightStates[i];
            }
        }

        private void RestoreVisuals()
        {
            if (!initialized || visualRoot == null)
                return;
            visualRoot.localPosition = baseVisualPosition;
            visualRoot.localScale = baseVisualScale;
            visualRoot.localRotation = baseVisualRotation;
            if (hood != null)
                hood.localRotation = baseHoodRotation;
            for (int i = 0; i < frontWheels.Length; i++)
            {
                if (frontWheels[i] != null)
                    frontWheels[i].localRotation = baseWheelRotations[i];
            }
            for (int i = 0; i < headlights.Length; i++)
            {
                if (headlights[i] != null)
                    headlights[i].enabled = baseHeadlightStates[i];
            }
        }

        private static float Damp(float current, float target, float speed, float deltaTime)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * deltaTime));
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
                Transform nested = FindChildByName(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static Transform FindDescendantContaining(Transform root, params string[] tokens)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                string lowerName = child.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
                {
                    if (lowerName.Contains(tokens[tokenIndex]))
                        return child;
                }
                Transform nested = FindDescendantContaining(child, tokens);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static Transform[] FindFrontWheels(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                string name = all[i].name.ToLowerInvariant();
                if (name.Contains("wheel") && name.Contains("front"))
                    count++;
            }
            if (count == 0)
                return Array.Empty<Transform>();

            Transform[] result = new Transform[count];
            int resultIndex = 0;
            for (int i = 0; i < all.Length; i++)
            {
                string name = all[i].name.ToLowerInvariant();
                if (name.Contains("wheel") && name.Contains("front"))
                    result[resultIndex++] = all[i];
            }
            return result;
        }
    }
}
