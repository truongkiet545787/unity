using System;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Lightweight, force-based suspension for PoseKart vehicles.
    ///
    /// This is intentionally separate from VehicleGroundProbe: the probe still
    /// decides whether CarController is grounded, while this component supplies
    /// per-wheel support forces. It does not move the Rigidbody transform and it
    /// never animates the chassis used by KartLandingEffects.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PoseKartMultiRaySuspension : MonoBehaviour
    {
        private const int WheelCount = 4;
        private const int MaxRaysPerWheel = 5;

        [Header("Wheel anchors")]
        [Tooltip("Center anchor for the front axle. Do not assign a wheel mesh.")]
        [SerializeField] private Transform frontAxle;
        [Tooltip("Center anchor for the rear axle. Do not assign a wheel mesh.")]
        [SerializeField] private Transform rearAxle;
        [Tooltip("Half the distance from the vehicle center to each wheel.")]
        [SerializeField, Min(0.05f)] private float wheelTrackHalfWidth = 0.62f;

        [Header("Ground sampling")]
        [SerializeField, Range(1, MaxRaysPerWheel)] private int raysPerWheel = 3;
        [Tooltip("Longitudinal spacing of the rays around each wheel center.")]
        [SerializeField, Min(0f)] private float sampleHalfLength = 0.10f;
        [Tooltip("Lateral spacing of the rays around each wheel center.")]
        [SerializeField, Min(0f)] private float sampleHalfWidth = 0.11f;
        [SerializeField, Min(0.01f)] private float rayStartHeight = 0.36f;
        [SerializeField, Min(0.05f)] private float suspensionRestLength = 0.50f;
        [SerializeField, Min(0f)] private float suspensionTravel = 0.24f;
        [SerializeField, Min(0f)] private float maxGroundSlope = 65f;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private bool ignoreOtherVehicles = true;

        [Header("Spring and damping")]
        [Tooltip("Force per metre of compression, per wheel.")]
        [SerializeField, Min(0f)] private float springStrength = 22000f;
        [Tooltip("Damping force per metre/second, per wheel.")]
        [SerializeField, Min(0f)] private float dampingStrength = 2600f;
        [SerializeField, Min(0f)] private float maximumWheelForce = 6500f;
        [SerializeField, Min(0f)] private float maximumTotalSuspensionForce = 14000f;

        [Header("Bump bounce")]
        [Tooltip("Extra spring response only when a wheel is deeply compressed while moving into the surface.")]
        [SerializeField, Range(0f, 1f)] private float bumpBounceStrength = 0.32f;
        [Tooltip("Compression required before a physical bump can kick the vehicle upward.")]
        [SerializeField, Min(0f)] private float minimumBounceCompression = 0.14f;
        [Tooltip("Minimum downward suspension speed required to arm the bump response.")]
        [SerializeField, Min(0f)] private float minimumCompressionSpeed = 0.75f;

        [Header("Optional wheel visual alignment")]
        [Tooltip("Optional mesh-only wheel transforms in order: front left, front right, rear left, rear right. Leave empty to let KartVisualJuice handle the existing wheel visuals.")]
        [SerializeField] private Transform[] wheelVisuals = Array.Empty<Transform>();
        [SerializeField, Min(0.05f)] private float wheelRadius = 0.38f;
        [SerializeField] private bool updateWheelVisualPositions = false;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private Color contactRayColor = Color.green;
        [SerializeField] private Color noContactRayColor = Color.red;

        private readonly WheelContact[] contacts = new WheelContact[WheelCount];
        private readonly Vector3[] wheelCenters = new Vector3[WheelCount];
        private Rigidbody body;
        private NetworkObject networkObject;
        private bool hasNetworkAuthority;

        /// <summary>
        /// Lets CarController preserve a deliberate suspension takeoff instead
        /// of treating it as a road-seam separation impulse.
        /// </summary>
        public bool AllowNaturalBounce { get; private set; }

        private struct WheelContact
        {
            public bool valid;
            public Vector3 point;
            public Vector3 normal;
            public float suspensionLength;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkObject = GetComponent<NetworkObject>();
            ResolveAnchors();
        }

        private void OnEnable()
        {
            hasNetworkAuthority = false;
        }

        private void FixedUpdate()
        {
            AllowNaturalBounce = false;
            if (!ShouldSimulate())
            {
                return;
            }

            ResolveAnchors();
            if (frontAxle == null || rearAxle == null)
            {
                return;
            }

            raysPerWheel = Mathf.Clamp(raysPerWheel, 1, MaxRaysPerWheel);
            BuildWheelCenters();

            float totalForce = 0f;
            for (int wheelIndex = 0; wheelIndex < WheelCount; wheelIndex++)
            {
                contacts[wheelIndex] = SampleWheel(wheelIndex);
                if (!contacts[wheelIndex].valid)
                {
                    continue;
                }

                WheelContact contact = contacts[wheelIndex];
                Vector3 contactVelocity = body.GetPointVelocity(contact.point);
                float normalSpeed = Vector3.Dot(contactVelocity, contact.normal);
                float compression = Mathf.Clamp(
                    suspensionRestLength - contact.suspensionLength,
                    0f,
                    suspensionTravel);
                float compressionGate = Mathf.InverseLerp(
                    minimumBounceCompression,
                    Mathf.Max(minimumBounceCompression + 0.001f, suspensionTravel),
                    compression);
                float speedGate = Mathf.InverseLerp(
                    minimumCompressionSpeed,
                    Mathf.Max(minimumCompressionSpeed + 0.001f, minimumCompressionSpeed * 2f),
                    -normalSpeed);
                float bounceGate = compressionGate * speedGate;
                float springMultiplier = 1f + bumpBounceStrength * bounceGate;
                float force = springStrength * compression * springMultiplier - dampingStrength * normalSpeed;
                force = Mathf.Clamp(force, 0f, maximumWheelForce);

                if (bounceGate > 0.05f && force > 0f)
                {
                    AllowNaturalBounce = true;
                }

                float availableForce = Mathf.Max(0f, maximumTotalSuspensionForce - totalForce);
                force = Mathf.Min(force, availableForce);
                if (force <= 0f)
                {
                    continue;
                }

                body.AddForceAtPosition(contact.normal * force, contact.point, ForceMode.Force);
                totalForce += force;
            }

        }

        private void LateUpdate()
        {
            if (!updateWheelVisualPositions || wheelVisuals == null || wheelVisuals.Length < WheelCount)
            {
                return;
            }

            // Only explicitly assigned mesh transforms are moved. Frontwheels,
            // Backwheel and the chassis remain available to KartVisualJuice and
            // KartLandingEffects without competing writes.
            for (int wheelIndex = 0; wheelIndex < WheelCount; wheelIndex++)
            {
                Transform visual = wheelVisuals[wheelIndex];
                if (visual == null || !contacts[wheelIndex].valid)
                {
                    continue;
                }

                visual.position = contacts[wheelIndex].point + contacts[wheelIndex].normal * wheelRadius;
            }
        }

        private bool ShouldSimulate()
        {
            if (body == null || body.isKinematic || !enabled)
            {
                return false;
            }

            if (networkObject == null)
            {
                return true;
            }

            if (!networkObject.IsSpawned)
            {
                // The offline vehicle can still use the network prefab without
                // ever spawning it through NGO. Online objects wait until NGO
                // has assigned ownership before applying physics.
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening;
            }

            // Human player objects use owner-authoritative physics. Scene AI is
            // server-owned, so only the Server applies its suspension forces.
            NetworkManager managerForAuthority = NetworkManager.Singleton;
            hasNetworkAuthority = networkObject.IsPlayerObject
                ? networkObject.IsOwner
                : managerForAuthority != null && managerForAuthority.IsServer;
            return hasNetworkAuthority;
        }

        private void ResolveAnchors()
        {
            if (frontAxle == null)
            {
                frontAxle = FindChildByName("Frontwheels", "FrontWheel", "front_wheel", "frontwheel");
            }

            if (rearAxle == null)
            {
                rearAxle = FindChildByName("Backwheel", "RearWheel", "rear_wheel", "rearwheel");
            }
        }

        private void BuildWheelCenters()
        {
            Vector3 right = transform.right * wheelTrackHalfWidth;
            wheelCenters[0] = frontAxle.position - right;
            wheelCenters[1] = frontAxle.position + right;
            wheelCenters[2] = rearAxle.position - right;
            wheelCenters[3] = rearAxle.position + right;
        }

        private WheelContact SampleWheel(int wheelIndex)
        {
            WheelContact result = default;
            Vector3 center = wheelCenters[wheelIndex];
            Vector3 forwardOffset = transform.forward * sampleHalfLength;
            Vector3 lateralOffset = transform.right * sampleHalfWidth;

            int rayCount = Mathf.Clamp(raysPerWheel, 1, MaxRaysPerWheel);
            int validCount = 0;
            Vector3 pointSum = Vector3.zero;
            Vector3 normalSum = Vector3.zero;
            float lengthSum = 0f;

            for (int rayIndex = 0; rayIndex < rayCount; rayIndex++)
            {
                Vector3 offset = GetSampleOffset(rayIndex, rayCount, forwardOffset, lateralOffset);
                Vector3 origin = center + offset + Vector3.up * rayStartHeight;
                float rayLength = rayStartHeight + suspensionRestLength + suspensionTravel;
                bool hitFound = Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    rayLength,
                    groundLayers,
                    QueryTriggerInteraction.Ignore);

                if (!hitFound || !IsUsableHit(hit))
                {
                    DrawRay(origin, Vector3.down * rayLength, false);
                    continue;
                }

                pointSum += hit.point;
                normalSum += hit.normal;
                lengthSum += Mathf.Max(0f, hit.distance - rayStartHeight);
                validCount++;
                DrawRay(origin, Vector3.down * hit.distance, true);
            }

            if (validCount == 0)
            {
                return result;
            }

            result.valid = true;
            result.point = pointSum / validCount;
            result.normal = (normalSum / validCount).normalized;
            result.suspensionLength = lengthSum / validCount;
            return result;
        }

        private Vector3 GetSampleOffset(
            int rayIndex,
            int rayCount,
            Vector3 forwardOffset,
            Vector3 lateralOffset)
        {
            if (rayCount <= 1)
            {
                return Vector3.zero;
            }

            if (rayCount == 3)
            {
                return rayIndex switch
                {
                    0 => Vector3.zero,
                    1 => forwardOffset,
                    _ => -forwardOffset
                };
            }

            if (rayCount == 4)
            {
                return rayIndex switch
                {
                    0 => forwardOffset + lateralOffset,
                    1 => forwardOffset - lateralOffset,
                    2 => -forwardOffset + lateralOffset,
                    _ => -forwardOffset - lateralOffset
                };
            }

            return rayIndex switch
            {
                0 => Vector3.zero,
                1 => forwardOffset,
                2 => -forwardOffset,
                3 => lateralOffset,
                _ => -lateralOffset
            };
        }

        private bool IsUsableHit(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                return false;
            }

            if (hit.collider.attachedRigidbody == body)
            {
                return false;
            }

            if (ignoreOtherVehicles && hit.collider.GetComponentInParent<CarController>() != null)
            {
                return false;
            }

            float minNormalY = Mathf.Cos(maxGroundSlope * Mathf.Deg2Rad);
            return Vector3.Dot(hit.normal, Vector3.up) >= minNormalY;
        }

        private Transform FindChildByName(params string[] names)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                string candidate = children[i].name;
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(candidate, names[nameIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        return children[i];
                    }
                }
            }

            return null;
        }

        private void DrawRay(Vector3 origin, Vector3 direction, bool hasContact)
        {
            if (debugRays)
            {
                Debug.DrawRay(origin, direction, hasContact ? contactRayColor : noContactRayColor, Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            raysPerWheel = Mathf.Clamp(raysPerWheel, 1, MaxRaysPerWheel);
            maxGroundSlope = Mathf.Clamp(maxGroundSlope, 0f, 89f);
        }
    }
}
