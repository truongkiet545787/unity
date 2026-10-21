using System;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Put this on the same Rigidbody GameObject as CarController.
    /// Detects a real upward-facing PHYSICS contact after a genuine airborne interval.
    /// Only a mesh-only visualBody is animated; the Rigidbody, colliders and wheels are untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    [DefaultExecutionOrder(9000)]
    public sealed class KartLandingEffects : MonoBehaviour
    {
        [Header("Suspension Visual (optional but recommended)")]
        [Tooltip("Drag a mesh-only BODY/CHASSIS pivot. NEVER drag the Rigidbody root or a pivot containing wheel meshes/colliders.")]
        [SerializeField] private Transform visualBody;
        [SerializeField, Range(0f, 0.35f)] private float maximumBodyCompression = 0.16f;
        [SerializeField, Range(0f, 12f)] private float maximumNoseDipDegrees = 4.5f;
        [SerializeField, Range(1f, 12f)] private float suspensionFrequency = 5f;
        [SerializeField, Range(0.1f, 25f)] private float suspensionDamping = 8f;
        [SerializeField, Range(0.1f, 1f)] private float suspensionAnimationDuration = 0.55f;

        [Header("Real Landing Detection")]
        [Tooltip("Ignore road seams and very short hops.")]
        [SerializeField, Min(0.05f)] private float minimumAirborneSeconds = 0.22f;
        [Tooltip("Vertical impact speed (m/s) required for a landing effect.")]
        [SerializeField, Min(0.1f)] private float minimumLandingSpeed = 2.3f;
        [SerializeField, Min(0.1f)] private float mediumLandingSpeed = 4.0f;
        [SerializeField, Min(0.1f)] private float sparksLandingSpeed = 7.2f;
        [SerializeField, Min(0.1f)] private float fullLandingSpeed = 9.5f;
        [SerializeField, Range(0f, 1f)] private float minimumRoadUpDot = 0.55f;
        [SerializeField, Min(0f)] private float landingCooldown = 0.32f;
        [SerializeField] private bool debugLandingLogs;

        [Header("Tire Contact Anchors (optional)")]
        [Tooltip("Four empty transforms placed at the BOTTOM of the four tires. If empty, use the physics contact point.")]
        [SerializeField] private Transform[] tireContactPoints = new Transform[4];

        [Header("Particles")]
        [Tooltip("Optional existing dust ParticleSystem. The script emits at the tire anchors / contact point.")]
        [SerializeField] private ParticleSystem tireDust;
        [Tooltip("Optional existing sparks ParticleSystem. Sparks only fire on a very hard underside impact.")]
        [SerializeField] private ParticleSystem undersideSparks;
        [SerializeField] private bool createFallbackParticles = true;
        [SerializeField, Range(1, 20)] private int dustPerTire = 7;
        [SerializeField, Range(1, 40)] private int sparksOnHeavyLanding = 16;
        [Tooltip("Arcade option: add a few sparks at each tire contact on a very hard landing.")]
        [SerializeField] private bool heavyLandingTireSparks = true;

        [Header("Gameplay and Camera")]
        [SerializeField] private bool enableLandingRecovery = true;
        [Tooltip("Uses KartCollisionCameraShake.PlayShake only if that component is on THIS kart (normally Player).")]
        [SerializeField] private bool enableLandingCameraShake = true;

        private CarController car;
        private Rigidbody body;
        private KartCollisionCameraShake cameraShake;
        private bool airborneCandidate;
        private bool landingConsumed;
        private float airborneSeconds;
        private float groundedGraceSeconds;
        private float recentDownwardSpeed;
        private float nextLandingTime;

        private bool springActive;
        private float springStartTime;
        private float springStrength;
        private bool hasAppliedVisualOffset;
        private Vector3 lastAppliedLocalPosition;
        private Quaternion lastAppliedLocalRotation;
        private Vector3 lastPositionOffset;
        private Quaternion lastRotationOffset = Quaternion.identity;

        private Material generatedDustMaterial;
        private Material generatedSparksMaterial;
        private Texture2D generatedDustTexture;
        private Texture2D generatedSparkTexture;

        private void Awake()
        {
            car = GetComponent<CarController>();
            body = car != null && car.TargetBody != null ? car.TargetBody : GetComponent<Rigidbody>();
            cameraShake = GetComponent<KartCollisionCameraShake>();

            if (visualBody != null &&
                (visualBody == transform || !visualBody.IsChildOf(transform) ||
                 (body != null && visualBody == body.transform) ||
                 visualBody.GetComponentInChildren<Rigidbody>(true) != null ||
                 visualBody.GetComponentInChildren<Collider>(true) != null))
            {
                Debug.LogWarning("[Landing] Visual Body must be a mesh-only CHILD with NO Rigidbody/Collider beneath it. Visual animation disabled.", this);
                visualBody = null;
            }

            if (createFallbackParticles)
            {
                if (tireDust == null)
                    tireDust = CreateParticleSystem("__LandingDust", false);
                if (undersideSparks == null)
                    undersideSparks = CreateParticleSystem("__LandingSparks", true);
            }
        }

        private void FixedUpdate()
        {
            if (car == null || body == null)
                return;

            // The CarController ground probe can see a surface shortly before the actual
            // physical collision. Keep the airborne candidate for a short grace window.
            if (!car.IsGrounded)
            {
                if (!airborneCandidate)
                {
                    airborneCandidate = true;
                    landingConsumed = false;
                    airborneSeconds = 0f;
                    recentDownwardSpeed = 0f;
                }
                airborneSeconds += Time.fixedDeltaTime;
                groundedGraceSeconds = 0f;
                float downward = Mathf.Max(0f, -Vector3.Dot(body.linearVelocity, Vector3.up));
                recentDownwardSpeed = Mathf.Max(downward,
                    Mathf.MoveTowards(recentDownwardSpeed, downward, 20f * Time.fixedDeltaTime));
            }
            else if (airborneCandidate)
            {
                groundedGraceSeconds += Time.fixedDeltaTime;
                if (groundedGraceSeconds >= 0.50f)
                    ResetFlightTracking();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryHandleLandingContact(collision);
        }

        // Handles tracks where the car was already touching an overlapping road collider
        // during a scripted launch. landingConsumed prevents repeated firing.
        private void OnCollisionStay(Collision collision)
        {
            TryHandleLandingContact(collision);
        }

        private void TryHandleLandingContact(Collision collision)
        {
            if (collision == null || car == null || body == null || !car.ControlEnabled ||
                !airborneCandidate || landingConsumed ||
                airborneSeconds < minimumAirborneSeconds || Time.time < nextLandingTime)
                return;

            // A collision with another kart's roof is not a road landing.
            if (collision.collider != null && collision.collider.GetComponentInParent<CarController>() != null)
                return;

            float bestUpDot = minimumRoadUpDot;
            Vector3 contactNormal = Vector3.up;
            Vector3 contactPoint = body.worldCenterOfMass;
            bool foundRoadContact = false;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                float upDot = Vector3.Dot(contact.normal, Vector3.up);
                if (upDot >= bestUpDot)
                {
                    bestUpDot = upDot;
                    contactNormal = contact.normal;
                    contactPoint = contact.point;
                    foundRoadContact = true;
                }
            }
            if (!foundRoadContact || Vector3.Dot(transform.up, Vector3.up) < 0.35f)
                return;

            // Collision.relativeVelocity gives the impact at contact; the cached downward
            // velocity protects against the solver already reducing body.linearVelocity.
            // Do not mistake an upward launch brushing a road collider for a landing.
            float currentDownward = Mathf.Max(0f, -Vector3.Dot(body.linearVelocity, contactNormal));
            if (Mathf.Max(recentDownwardSpeed, currentDownward) < 0.4f)
                return;
            float normalImpact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contactNormal));
            float impactSpeed = Mathf.Max(recentDownwardSpeed, normalImpact);
            landingConsumed = true;
            nextLandingTime = Time.time + landingCooldown;

            if (debugLandingLogs)
                Debug.Log($"[Landing] {name}: air={airborneSeconds:F2}s, impact={impactSpeed:F2}m/s, normalUp={bestUpDot:F2}", this);
            if (impactSpeed < minimumLandingSpeed)
                return;

            PlayLanding(impactSpeed, contactPoint, contactNormal, true);
        }

        private void PlayLanding(float impactSpeed, Vector3 contactPoint, Vector3 contactNormal, bool gameplay)
        {
            float severity = Mathf.InverseLerp(minimumLandingSpeed,
                Mathf.Max(minimumLandingSpeed + 0.1f, fullLandingSpeed), impactSpeed);
            float visualStrength = Mathf.Lerp(0.27f, 1f, severity);
            springStrength = visualStrength;
            springStartTime = Time.time;
            springActive = true;

            if (gameplay && enableLandingRecovery && impactSpeed >= mediumLandingSpeed)
            {
                // Light landings never interrupt driving. Medium/heavy landings briefly
                // reduce horizontal momentum and ramp throttle back in.
                float recoverySeverity = Mathf.Lerp(0.35f, 1f,
                    Mathf.InverseLerp(mediumLandingSpeed,
                        Mathf.Max(mediumLandingSpeed + 0.1f, fullLandingSpeed), impactSpeed));
                car.BeginLandingRecovery(recoverySeverity);
            }

            if (tireDust != null)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(dustPerTire * (0.5f + severity)));
                bool emittedAtAnchor = false;
                if (tireContactPoints != null)
                {
                    foreach (Transform anchor in tireContactPoints)
                    {
                        if (anchor == null) continue;
                        EmitBurst(tireDust, anchor.position + contactNormal * 0.035f,
                            contactNormal, count, false);
                        emittedAtAnchor = true;
                    }
                }
                if (!emittedAtAnchor)
                {
                    // Visible left/right puffs even before four tire anchors are configured.
                    EmitBurst(tireDust, contactPoint - transform.right * 0.5f + contactNormal * 0.035f,
                        contactNormal, count, false);
                    EmitBurst(tireDust, contactPoint + transform.right * 0.5f + contactNormal * 0.035f,
                        contactNormal, count, false);
                }
            }

            // Very hard landings scrape the underbody. Optional arcade sparks at the tire
            // contact anchors add the stylized tire-spark look without firing on light hops.
            if (impactSpeed >= sparksLandingSpeed && undersideSparks != null)
            {
                EmitBurst(undersideSparks, contactPoint + contactNormal * 0.05f,
                    contactNormal, Mathf.Max(1, Mathf.RoundToInt(sparksOnHeavyLanding * visualStrength)), true);
                if (heavyLandingTireSparks && tireContactPoints != null)
                {
                    foreach (Transform anchor in tireContactPoints)
                    {
                        if (anchor != null)
                            EmitBurst(undersideSparks, anchor.position + contactNormal * 0.025f,
                                contactNormal, Mathf.Max(1, sparksOnHeavyLanding / 6), true);
                    }
                }
            }

            PlayLandingAudio(impactSpeed, severity);
            if (gameplay && enableLandingCameraShake && impactSpeed >= mediumLandingSpeed && cameraShake != null)
                cameraShake.PlayShake(Mathf.Lerp(0.22f, 0.82f, severity));
        }

        private void PlayLandingAudio(float impactSpeed, float severity)
        {
            PoseKart.Audio.AudioManager.PlayLanding(transform.position, impactSpeed, severity);
        }

        private static void EmitBurst(ParticleSystem emitter, Vector3 worldPosition,
            Vector3 surfaceNormal, int count, bool sparks)
        {
            if (emitter == null || count <= 0)
                return;
            ParticleSystem.MainModule main = emitter.main;
            bool worldSpace = main.simulationSpace == ParticleSystemSimulationSpace.World;
            Transform simulationTransform = main.simulationSpace == ParticleSystemSimulationSpace.Custom &&
                main.customSimulationSpace != null ? main.customSimulationSpace : emitter.transform;
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = UnityEngine.Random.insideUnitSphere;
                Vector3 velocity = sparks
                    ? Vector3.ProjectOnPlane(scatter, surfaceNormal) * UnityEngine.Random.Range(2.2f, 5f)
                      + surfaceNormal * UnityEngine.Random.Range(0.8f, 2.5f)
                    : Vector3.ProjectOnPlane(scatter, surfaceNormal) * UnityEngine.Random.Range(0.45f, 1.6f)
                      + surfaceNormal * UnityEngine.Random.Range(0.4f, 1.25f);
                ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
                {
                    position = worldSpace ? worldPosition : simulationTransform.InverseTransformPoint(worldPosition),
                    velocity = worldSpace ? velocity : simulationTransform.InverseTransformDirection(velocity)
                };
                emitter.Emit(particle, 1);
            }
        }

        private ParticleSystem CreateParticleSystem(string objectName, bool sparks)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            ParticleSystem emitter = go.AddComponent<ParticleSystem>();
            emitter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = emitter.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 320;
            main.startLifetime = sparks
                ? new ParticleSystem.MinMaxCurve(0.12f, 0.32f)
                : new ParticleSystem.MinMaxCurve(0.3f, 0.62f);
            main.startSpeed = 0f; // EmitParams supplies world-space scatter velocity.
            main.startSize = sparks
                ? new ParticleSystem.MinMaxCurve(0.035f, 0.075f)
                : new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startColor = sparks
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.66f, 0.12f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.67f, 0.58f, 0.72f));
            main.gravityModifier = sparks ? 1.2f : 0.12f;
            ParticleSystem.EmissionModule emission = emitter.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = emitter.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule colorLife = emitter.colorOverLifetime;
            colorLife.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorLife.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystemRenderer particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            if (sparks)
            {
                particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                particleRenderer.lengthScale = 2.5f;
                particleRenderer.velocityScale = 0.18f;
            }
            else
                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Material material = new Material(shader);
                Texture2D texture = CreateParticleTexture(sparks);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                material.renderQueue = 3000;
                particleRenderer.sharedMaterial = material;
                if (sparks)
                {
                    generatedSparksMaterial = material;
                    generatedSparkTexture = texture;
                }
                else
                {
                    generatedDustMaterial = material;
                    generatedDustTexture = texture;
                }
            }
            emitter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return emitter;
        }

        private static Texture2D CreateParticleTexture(bool sparks)
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = sparks ? "Landing Spark Soft Dot" : "Landing Dust Soft Dot";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), sparks ? 0.4f : 1.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        private void LateUpdate()
        {
            if (visualBody == null)
                return;
            RestorePreviousVisualOffset();
            if (!springActive)
                return;

            float t = Mathf.Max(0f, Time.time - springStartTime);
            if (t >= suspensionAnimationDuration)
            {
                springActive = false;
                return;
            }
            float fade = 1f - Mathf.SmoothStep(0f, 1f,
                t / Mathf.Max(0.1f, suspensionAnimationDuration));
            float oscillation = Mathf.Sin(t * suspensionFrequency * 2f * Mathf.PI);
            float compression = oscillation >= 0f ? oscillation : oscillation * 0.28f;
            float amount = springStrength * Mathf.Exp(-suspensionDamping * t) * fade * compression;
            lastPositionOffset = Vector3.down * (maximumBodyCompression * amount);
            lastRotationOffset = Quaternion.Euler(maximumNoseDipDegrees * amount, 0f, 0f);
            visualBody.localPosition += lastPositionOffset;
            visualBody.localRotation *= lastRotationOffset;
            lastAppliedLocalPosition = visualBody.localPosition;
            lastAppliedLocalRotation = visualBody.localRotation;
            hasAppliedVisualOffset = true;
        }

        private void RestorePreviousVisualOffset()
        {
            if (!hasAppliedVisualOffset || visualBody == null)
                return;
            // If an Animator or another script wrote a new pose, use that as the new base.
            bool stillOurPose =
                (visualBody.localPosition - lastAppliedLocalPosition).sqrMagnitude < 0.00000001f &&
                Quaternion.Angle(visualBody.localRotation, lastAppliedLocalRotation) < 0.01f;
            if (stillOurPose)
            {
                visualBody.localPosition -= lastPositionOffset;
                visualBody.localRotation *= Quaternion.Inverse(lastRotationOffset);
            }
            hasAppliedVisualOffset = false;
        }

        private void ResetFlightTracking()
        {
            airborneCandidate = false;
            landingConsumed = false;
            airborneSeconds = 0f;
            groundedGraceSeconds = 0f;
            recentDownwardSpeed = 0f;
        }

        [ContextMenu("Test HEAVY Landing (Play Mode)")]
        private void TestHeavyLanding()
        {
            if (Application.isPlaying)
                PlayLanding(sparksLandingSpeed + 1.5f,
                    transform.position - Vector3.up * 0.5f, Vector3.up, false);
        }

        [ContextMenu("Test MEDIUM Landing (Play Mode)")]
        private void TestMediumLanding()
        {
            if (Application.isPlaying)
                PlayLanding(mediumLandingSpeed + 0.6f,
                    transform.position - Vector3.up * 0.5f, Vector3.up, false);
        }

        private void OnDisable()
        {
            RestorePreviousVisualOffset();
            springActive = false;
            ResetFlightTracking();
        }

        private void OnDestroy()
        {
            if (generatedDustMaterial != null) Destroy(generatedDustMaterial);
            if (generatedSparksMaterial != null) Destroy(generatedSparksMaterial);
            if (generatedDustTexture != null) Destroy(generatedDustTexture);
            if (generatedSparkTexture != null) Destroy(generatedSparkTexture);
        }

        private void OnValidate()
        {
            mediumLandingSpeed = Mathf.Max(minimumLandingSpeed + 0.1f, mediumLandingSpeed);
            sparksLandingSpeed = Mathf.Max(mediumLandingSpeed + 0.1f, sparksLandingSpeed);
            fullLandingSpeed = Mathf.Max(sparksLandingSpeed + 0.1f, fullLandingSpeed);
        }
    }
}
