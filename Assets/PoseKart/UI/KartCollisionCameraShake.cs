using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Attach to the PLAYER kart's Rigidbody GameObject, then assign Main Camera.
    /// Detects real physics impacts and adds a short, damped camera shake.
    /// Does not require edits to CarController or the existing camera follow script.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(10000)] // Apply the shake after ordinary camera-follow LateUpdate calls.
    public sealed class KartCollisionCameraShake : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Drag Main Camera here. If empty, Camera.main is used at runtime.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Impact Detection")]
        [Tooltip("Minimum horizontal impact speed (m/s) before a shake is triggered.")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 3.5f;
        [Tooltip("An impact at this speed (m/s) produces a full-strength shake.")]
        [SerializeField, Min(0.1f)] private float fullImpactSpeed = 13f;
        [Tooltip("Ignore mostly horizontal surfaces. 0.45 means at least some wall-like contact is required.")]
        [SerializeField, Range(0f, 1f)] private float minimumWallContact = 0.45f;
        [SerializeField, Min(0f)] private float impactCooldown = 0.14f;

        [Header("Angry Birds-style Shake")]
        [SerializeField, Range(0.05f, 1f)] private float shakeDuration = 0.42f;
        [SerializeField, Range(0f, 0.5f)] private float positionAmplitude = 0.11f;
        [SerializeField, Range(0f, 8f)] private float rotationAmplitude = 2.2f;
        [SerializeField, Range(1f, 35f)] private float oscillationsPerSecond = 16f;
        [SerializeField, Range(0f, 25f)] private float damping = 7f;
        [SerializeField] private bool debugImpactLogs;

        private Rigidbody kartBody;
        private float nextImpactTime;
        private float shakeElapsed = float.PositiveInfinity;
        private float shakeStrength;
        private float shakeSign = 1f;

        // Remember only the offset WE applied, so a camera-follow script can
        // keep moving the camera without accumulating positional drift.
        private bool hasAppliedOffset;
        private Vector3 lastAppliedPosition;
        private Quaternion lastAppliedRotation;
        private Vector3 lastPositionOffset;
        private Quaternion lastRotationOffset = Quaternion.identity;

        private void Awake()
        {
            kartBody = GetComponent<Rigidbody>();
            ResolveCamera();
        }

        private void ResolveCamera()
        {
            if (cameraTransform == null && UnityEngine.Camera.main != null)
                cameraTransform = UnityEngine.Camera.main.transform;
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time < nextImpactTime || collision == null)
                return;

            // A landing or a regular road contact must not shake the camera.
            // Wall, obstacle and other-kart contacts have a significant sideways normal.
            float wallContact = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                wallContact = Mathf.Max(wallContact, 1f - Mathf.Abs(Vector3.Dot(normal, Vector3.up)));
            }

            if (wallContact < minimumWallContact)
                return;

            float horizontalRelativeSpeed = Vector3.ProjectOnPlane(
                collision.relativeVelocity, Vector3.up).magnitude;
            float horizontalImpulseSpeed = Vector3.ProjectOnPlane(
                collision.impulse, Vector3.up).magnitude / Mathf.Max(0.1f, kartBody.mass);
            float impactSpeed = Mathf.Max(horizontalRelativeSpeed, horizontalImpulseSpeed);

            if (impactSpeed < minimumImpactSpeed)
                return;

            float intensity = Mathf.Lerp(0.28f, 1f,
                Mathf.InverseLerp(minimumImpactSpeed, Mathf.Max(minimumImpactSpeed + 0.1f, fullImpactSpeed), impactSpeed));

            PlayShake(intensity);
            nextImpactTime = Time.time + impactCooldown;

            if (debugImpactLogs)
                Debug.Log($"[CameraShake] {name} hit {collision.collider.name}: {impactSpeed:F1} m/s, strength {intensity:F2}", this);
        }

        /// <summary>
        /// Manually shake the camera for banana traps, missiles, explosions, etc.
        /// strength01: 0 = no shake, 1 = maximum configured shake.
        /// </summary>
        public void PlayShake(float strength01 = 1f)
        {
            strength01 = Mathf.Clamp01(strength01);
            if (strength01 <= 0f)
                return;

            ResolveCamera();
            if (cameraTransform == null)
                return;

            // A weaker second hit cannot cancel a stronger shake already in progress.
            float remainingStrength = shakeElapsed >= shakeDuration
                ? 0f
                : shakeStrength * Mathf.Exp(-damping * Mathf.Max(0f, shakeElapsed));
            shakeStrength = Mathf.Max(remainingStrength, strength01);
            shakeElapsed = 0f;
            shakeSign = -shakeSign;
        }

        [ContextMenu("Test Camera Shake (Play Mode)")]
        private void TestCameraShake()
        {
            if (Application.isPlaying)
                PlayShake(1f);
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (cameraTransform == null)
                return;

            RestorePreviousOffsetIfStillPresent();

            if (shakeElapsed >= shakeDuration || shakeStrength <= 0f)
                return;

            shakeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp(shakeElapsed, 0f, shakeDuration);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, shakeDuration));
            float envelope = shakeStrength * Mathf.Exp(-damping * t) * fade;

            if (envelope <= 0.0001f)
                return;

            // Damped impact oscillation: a noticeable initial jolt followed by
            // smaller bounces, rather than unrelated random jitter every frame.
            float phase = t * oscillationsPerSecond * Mathf.PI * 2f;
            float x = Mathf.Sin(phase + 1.2f) * shakeSign;
            float y = Mathf.Sin(phase * 1.31f + 0.4f);

            lastPositionOffset = new Vector3(x, y * 0.45f, 0f) * positionAmplitude * envelope;
            lastRotationOffset = Quaternion.Euler(
                y * rotationAmplitude * 0.35f * envelope,
                x * rotationAmplitude * 0.45f * envelope,
                x * rotationAmplitude * envelope);

            cameraTransform.localPosition += lastPositionOffset;
            cameraTransform.localRotation *= lastRotationOffset;

            lastAppliedPosition = cameraTransform.localPosition;
            lastAppliedRotation = cameraTransform.localRotation;
            hasAppliedOffset = true;
        }

        private void RestorePreviousOffsetIfStillPresent()
        {
            if (!hasAppliedOffset || cameraTransform == null)
                return;

            // If a camera-follow script already wrote a new pose this frame,
            // that new pose is the base; do not subtract the old offset twice.
            bool stillOurPose =
                (cameraTransform.localPosition - lastAppliedPosition).sqrMagnitude < 0.00000001f &&
                Quaternion.Angle(cameraTransform.localRotation, lastAppliedRotation) < 0.01f;

            if (stillOurPose)
            {
                cameraTransform.localPosition -= lastPositionOffset;
                cameraTransform.localRotation *= Quaternion.Inverse(lastRotationOffset);
            }

            hasAppliedOffset = false;
        }

        private void OnDisable()
        {
            RestorePreviousOffsetIfStillPresent();
            shakeElapsed = float.PositiveInfinity;
            shakeStrength = 0f;
        }

        private void OnValidate()
        {
            fullImpactSpeed = Mathf.Max(minimumImpactSpeed + 0.1f, fullImpactSpeed);
        }
    }
}
