using System;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Wheel skidmark emitter.
    /// Can be attached directly to:
    /// 1. A WheelCollider (standard Unity vehicle physics).
    /// 2. A visual wheel Transform / emitter (arcade raycast-based kart physics like PoseKart).
    /// 3. The vehicle root (automatically detects and manages all wheels / rear wheels).
    /// </summary>
    [DisallowMultipleComponent]
    public class WheelSkid : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Vehicle Rigidbody. Auto-detected in parent if unassigned.")]
        [SerializeField] private Rigidbody rb;
        [Tooltip("Skidmarks controller. Auto-found or created in scene if unassigned.")]
        [SerializeField] private Skidmarks skidmarksController;
        [Tooltip("Optional WheelCollider. If null, raycast ground probing is used.")]
        [SerializeField] private WheelCollider wheelCollider;
        [Tooltip("Optional CarController for drift, brake and spinout detection.")]
        [SerializeField] private CarController carController;

        [Header("Skid Settings")]
        [Tooltip("Minimum side-slip or drift speed in m/s to start laying skidmarks.")]
        [SerializeField] private float skidFxSpeed = 0.5f;
        [Tooltip("Slip speed in m/s at which skid opacity reaches maximum intensity.")]
        [SerializeField] private float maxSkidIntensity = 15.0f;
        [Tooltip("Multiplier for wheelspin/burnout slip.")]
        [SerializeField] private float wheelSlipMultiplier = 10.0f;
        [Tooltip("Skidmark line width for this wheel (0 uses default controller width).")]
        [SerializeField, Min(0f)] private float markWidth = 0.32f;

        [Header("Arcade Raycast Probing (When no WheelCollider is used)")]
        [SerializeField, Min(0.1f)] private float wheelRadius = 0.38f;
        [SerializeField, Min(0.1f)] private float groundCheckDistance = 0.65f;
        [SerializeField] private LayerMask groundLayer = ~0;

        [Header("Multi-Wheel Root Support")]
        [Tooltip("If this component is placed on the kart root, assign wheels here or leave empty to auto-detect.")]
        [SerializeField] private Transform[] childWheelTransforms;

        private WheelHit wheelHitInfo;
        private int lastSkid = -1;
        private int[] multiLastSkid;
        private float lastFixedUpdateTime;
        private bool isRootController;

        protected void Awake()
        {
            if (wheelCollider == null) wheelCollider = GetComponent<WheelCollider>();
            if (rb == null) rb = GetComponentInParent<Rigidbody>();
            if (carController == null) carController = GetComponentInParent<CarController>();

            // If on root without a WheelCollider, check if we should drive multiple wheels
            if (wheelCollider == null && (childWheelTransforms == null || childWheelTransforms.Length == 0))
            {
                // If attached to root vehicle
                if (GetComponent<CarController>() != null || GetComponent<Rigidbody>() != null)
                {
                    AutoDetectChildWheels();
                }
            }

            if (childWheelTransforms != null && childWheelTransforms.Length > 0)
            {
                isRootController = true;
                multiLastSkid = new int[childWheelTransforms.Length];
                for (int i = 0; i < multiLastSkid.Length; i++)
                {
                    multiLastSkid[i] = -1;
                }
            }

            lastFixedUpdateTime = Time.time;
        }

        private void AutoDetectChildWheels()
        {
            // Prefer rear wheels for drifting, or all wheels if found
            Transform[] allWheels = GetComponentsInChildren<Transform>(true);
            var foundList = new System.Collections.Generic.List<Transform>();

            foreach (Transform t in allWheels)
            {
                if (t == transform) continue;
                string lower = t.name.ToLowerInvariant();
                if (lower.Contains("wheel") && (lower.Contains("rear") || lower.Contains("back") || lower.Contains("left") || lower.Contains("right")))
                {
                    if (t.GetComponent<Renderer>() != null || t.childCount > 0)
                    {
                        foundList.Add(t);
                    }
                }
            }

            if (foundList.Count > 0)
            {
                childWheelTransforms = foundList.ToArray();
            }
        }

        protected void Start()
        {
            EnsureSkidmarksController();
        }

        private void EnsureSkidmarksController()
        {
            if (skidmarksController == null)
            {
                skidmarksController = Skidmarks.GetOrCreateInstance();
            }
        }

        protected void FixedUpdate()
        {
            lastFixedUpdateTime = Time.time;
        }

        protected void LateUpdate()
        {
            if (skidmarksController == null)
            {
                EnsureSkidmarksController();
                if (skidmarksController == null) return;
            }

            if (isRootController && childWheelTransforms != null && childWheelTransforms.Length > 0)
            {
                UpdateMultiWheels();
            }
            else if (wheelCollider != null)
            {
                UpdateWheelCollider();
            }
            else
            {
                UpdateArcadeSingleWheel();
            }
        }

        /// <summary>
        /// Updates skidmarks for standard WheelCollider vehicle setups.
        /// </summary>
        private void UpdateWheelCollider()
        {
            if (wheelCollider.GetGroundHit(out wheelHitInfo))
            {
                Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
                Vector3 localVelocity = transform.InverseTransformDirection(vel);
                float skidTotal = Mathf.Abs(localVelocity.x);

                float wheelAngularVelocity = wheelCollider.radius * ((2f * Mathf.PI * wheelCollider.rpm) / 60f);
                float carForwardVel = Vector3.Dot(vel, transform.forward);
                float wheelSpin = Mathf.Abs(carForwardVel - wheelAngularVelocity) * wheelSlipMultiplier;

                wheelSpin = Mathf.Max(0f, wheelSpin * (10f - Mathf.Abs(carForwardVel)));
                skidTotal += wheelSpin;

                if (skidTotal >= skidFxSpeed)
                {
                    float intensity = Mathf.Clamp01(skidTotal / maxSkidIntensity);
                    Vector3 skidPoint = wheelHitInfo.point + (vel * (Time.time - lastFixedUpdateTime));
                    lastSkid = skidmarksController.AddSkidMark(skidPoint, wheelHitInfo.normal, intensity, lastSkid, markWidth);
                }
                else
                {
                    lastSkid = -1;
                }
            }
            else
            {
                lastSkid = -1;
            }
        }

        /// <summary>
        /// Updates skidmarks for arcade karts without WheelColliders (using Raycast / CarController).
        /// </summary>
        private void UpdateArcadeSingleWheel()
        {
            Vector3 contactPoint;
            Vector3 contactNormal;

            if (ProbeGround(transform.position, out contactPoint, out contactNormal))
            {
                float skidTotal = CalculateArcadeSkidTotal();

                if (skidTotal >= skidFxSpeed)
                {
                    float intensity = Mathf.Clamp01(skidTotal / maxSkidIntensity);
                    Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
                    Vector3 skidPoint = contactPoint + (vel * (Time.time - lastFixedUpdateTime));
                    lastSkid = skidmarksController.AddSkidMark(skidPoint, contactNormal, intensity, lastSkid, markWidth);
                }
                else
                {
                    lastSkid = -1;
                }
            }
            else
            {
                lastSkid = -1;
            }
        }

        /// <summary>
        /// Updates multiple child wheels when WheelSkid is placed on vehicle root.
        /// </summary>
        private void UpdateMultiWheels()
        {
            float skidTotal = CalculateArcadeSkidTotal();
            bool shouldSkid = skidTotal >= skidFxSpeed;
            float intensity = Mathf.Clamp01(skidTotal / maxSkidIntensity);
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;

            for (int i = 0; i < childWheelTransforms.Length; i++)
            {
                Transform wheel = childWheelTransforms[i];
                if (wheel == null) continue;

                Vector3 contactPoint;
                Vector3 contactNormal;

                if (shouldSkid && ProbeGround(wheel.position, out contactPoint, out contactNormal))
                {
                    Vector3 skidPoint = contactPoint + (vel * (Time.time - lastFixedUpdateTime));
                    multiLastSkid[i] = skidmarksController.AddSkidMark(skidPoint, contactNormal, intensity, multiLastSkid[i], markWidth);
                }
                else
                {
                    multiLastSkid[i] = -1;
                }
            }
        }

        private float CalculateArcadeSkidTotal()
        {
            if (rb == null) return 0f;

            Transform refTransform = carController != null ? carController.transform : rb.transform;
            Vector3 localVel = refTransform.InverseTransformDirection(rb.linearVelocity);
            float lateralSpeed = Mathf.Abs(localVel.x);
            float forwardSpeed = localVel.z;

            float skidTotal = lateralSpeed;

            // PoseKart active drifting
            if (carController != null && carController.IsDrifting && forwardSpeed > 3f)
            {
                skidTotal = Mathf.Max(skidTotal, 3.2f + lateralSpeed * 1.4f);
            }

            // PoseKart spinouts (banana / oil slip)
            if (carController != null && (carController.IsBananaStunned))
            {
                skidTotal = Mathf.Max(skidTotal, 7.0f);
            }

            return skidTotal;
        }

        private bool ProbeGround(Vector3 originPos, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;

            Vector3 rayStart = originPos + Vector3.up * 0.25f;
            float dist = wheelRadius + groundCheckDistance;

            if (rb != null && VehicleGroundProbe.Cast(rb, rayStart, dist, out RaycastHit probeHit))
            {
                point = probeHit.point;
                normal = probeHit.normal;
                return true;
            }

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, dist, groundLayer, QueryTriggerInteraction.Ignore))
            {
                if (rb == null || hit.rigidbody != rb)
                {
                    point = hit.point;
                    normal = hit.normal;
                    return true;
                }
            }

            return false;
        }
    }
}
