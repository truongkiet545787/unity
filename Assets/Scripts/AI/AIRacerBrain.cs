using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class AIRacerBrain : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RacingLine racingLine;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CarController carController;
        [SerializeField] private AIInputProvider inputProvider;

        [Header("Difficulty")]
        [SerializeField, Range(0f, 1f)] private float skill = 0.8f;
        [SerializeField, Range(0.7f, 1.15f)] private float targetSpeedMultiplier = 1f;
        [SerializeField, Range(0.5f, 1.5f)] private float cornerCaution = 1f;
        [SerializeField, Min(0.1f)] private float steeringResponse = 3.5f;
        [SerializeField, Range(0.6f, 1.5f)] private float lookAheadMultiplier = 1f;
        [SerializeField, Range(-1f, 1f)] private float avoidanceBias;

        [Header("Driving")]
        [SerializeField, Min(1f)] private float baseLookAheadDistance = 7f;
        [SerializeField, Min(0f)] private float speedLookAheadDistance = 0.55f;
        [SerializeField, Min(1f)] private float waypointReachDistance = 4f;
        [SerializeField, Min(1)] private int cornerPreviewSegments = 3;
        [SerializeField, Range(0.2f, 0.9f)] private float minimumCornerSpeedFactor = 0.45f;
        [SerializeField, Min(0f)] private float brakeSpeedMargin = 1.5f;

        [Header("Obstacle Awareness")]
        [SerializeField, Min(0f)] private float obstacleLookAhead = 8f;
        [SerializeField, Min(0.1f)] private float obstacleCastRadius = 0.7f;
        [SerializeField, Range(0f, 1f)] private float obstacleSteeringInfluence = 0.65f;

        [Header("Stuck Recovery")]
        [SerializeField, Min(0.1f)] private float stuckSpeedThreshold = 0.75f;
        [SerializeField, Min(0.5f)] private float reverseAfterSeconds = 3f;
        [SerializeField, Min(0.25f)] private float reverseDuration = 1.25f;
        [SerializeField, Min(2f)] private float hardResetAfterSeconds = 8f;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private int nextWaypointIndex;
        [SerializeField] private float cornerSeverity;
        [SerializeField] private float desiredSpeed;
        [SerializeField] private float smoothedSteering;
        [SerializeField] private float stuckTime;
        [SerializeField] private bool reversingToRecover;

        private float reverseTimeRemaining;

        public bool IsConfigured =>
            racingLine != null
            && racingLine.IsConfigured
            && body != null
            && carController != null
            && inputProvider != null;

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

        private void Start()
        {
            if (!IsConfigured)
            {
                Debug.LogError($"{nameof(AIRacerBrain)} on {name} is not fully configured.", this);
                enabled = false;
                return;
            }

            nextWaypointIndex = racingLine.FindStartingWaypoint(body.position);
        }

        private void FixedUpdate()
        {
            if (!carController.ControlEnabled)
            {
                inputProvider.SetInput(0f, 0f, 0f);
                stuckTime = 0f;
                reverseTimeRemaining = 0f;
                reversingToRecover = false;
                return;
            }

            racingLine.AdvanceProgress(ref nextWaypointIndex, body.position, waypointReachDistance);
            float speed = Mathf.Abs(carController.ForwardSpeed);
            float lookAhead = (baseLookAheadDistance + speed * speedLookAheadDistance) * lookAheadMultiplier;
            Vector3 target = racingLine.GetLookAheadPoint(nextWaypointIndex, body.position, lookAhead);
            Vector3 localTarget = transform.InverseTransformPoint(target);
            float targetAngle = Mathf.Atan2(localTarget.x, Mathf.Max(0.5f, localTarget.z)) * Mathf.Rad2Deg;
            float desiredSteering = Mathf.Clamp(targetAngle / 45f, -1f, 1f);

            cornerSeverity = racingLine.GetCornerSeverity(nextWaypointIndex, cornerPreviewSegments);
            float cautionSeverity = Mathf.Clamp01(cornerSeverity * cornerCaution);
            float skillSpeedFactor = Mathf.Lerp(0.86f, 1.03f, skill);
            float cornerSpeedFactor = Mathf.Lerp(1f, minimumCornerSpeedFactor, cautionSeverity);
            desiredSpeed = carController.MaxForwardSpeed
                           * targetSpeedMultiplier
                           * skillSpeedFactor
                           * cornerSpeedFactor
                           * racingLine.GetRecommendedSpeedFactor(nextWaypointIndex);

            if(carController.IsBoosting)desiredSpeed+=14f;
            ApplyObstacleAwareness(ref desiredSteering, ref desiredSpeed);
            smoothedSteering = Mathf.MoveTowards(
                smoothedSteering,
                desiredSteering,
                steeringResponse * Time.fixedDeltaTime);

            if (UpdateStuckRecovery(localTarget, speed))
            {
                return;
            }

            float throttle = speed < desiredSpeed - brakeSpeedMargin
                ? 1f
                : speed < desiredSpeed ? 0.3f : 0f;
            float brake = speed > desiredSpeed + brakeSpeedMargin
                ? Mathf.InverseLerp(desiredSpeed + brakeSpeedMargin, desiredSpeed + 5f, speed)
                : 0f;
            inputProvider.SetInput(smoothedSteering, throttle, brake);
        }

        private void ApplyObstacleAwareness(ref float steering, ref float targetSpeed)
        {
            Vector3 origin = body.position + transform.forward * 2f + transform.up * 0.25f;
            if (!Physics.SphereCast(
                    origin,
                    obstacleCastRadius,
                    transform.forward,
                    out RaycastHit hit,
                    obstacleLookAhead,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Vector3 localHit = transform.InverseTransformPoint(hit.point);
            float avoidanceDirection = Mathf.Abs(localHit.x) > 0.2f
                ? -Mathf.Sign(localHit.x)
                : avoidanceBias != 0f ? Mathf.Sign(avoidanceBias) : 1f;
            float urgency = 1f - Mathf.Clamp01(hit.distance / obstacleLookAhead);
            steering = Mathf.Clamp(
                steering + avoidanceDirection * obstacleSteeringInfluence * urgency,
                -1f,
                1f);
            targetSpeed *= Mathf.Lerp(1f, 0.45f, urgency);
        }

        private bool UpdateStuckRecovery(Vector3 localTarget, float speed)
        {
            if (reverseTimeRemaining > 0f)
            {
                reverseTimeRemaining -= Time.fixedDeltaTime;
                stuckTime += Time.fixedDeltaTime;
                reversingToRecover = true;

                if (stuckTime >= hardResetAfterSeconds)
                {
                    HardRecoverToRacingLine();
                    stuckTime = 0f;
                    reverseTimeRemaining = 0f;
                    reversingToRecover = false;
                    return true;
                }

                float recoverySteering = -Mathf.Sign(localTarget.x);
                inputProvider.SetInput(recoverySteering, 0f, 1f);
                return true;
            }

            reversingToRecover = false;
            if (speed < stuckSpeedThreshold)
            {
                stuckTime += Time.fixedDeltaTime;
                if (stuckTime >= hardResetAfterSeconds)
                {
                    HardRecoverToRacingLine();
                    stuckTime = 0f;
                    return true;
                }

                if (stuckTime >= reverseAfterSeconds)
                {
                    reverseTimeRemaining = reverseDuration;
                    inputProvider.SetInput(-Mathf.Sign(localTarget.x), 0f, 1f);
                    return true;
                }
            }
            else if (speed > stuckSpeedThreshold * 2f)
            {
                stuckTime = 0f;
            }

            return false;
        }

        private void HardRecoverToRacingLine()
        {
            int recoveryIndex = (nextWaypointIndex - 1 + racingLine.Count) % racingLine.Count;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = racingLine.GetPosition(recoveryIndex) + Vector3.up * 0.8f;
            body.rotation = racingLine.GetForwardRotation(recoveryIndex);
            nextWaypointIndex = (recoveryIndex + 1) % racingLine.Count;
            inputProvider.SetInput(0f, 0f, 0f);
        }
    }
}
