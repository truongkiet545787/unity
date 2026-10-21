using System.Collections.Generic;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// One-shot arcade banana trap. The trigger is only one of two detection paths:
    /// the four racers are also checked directly, including their swept movement
    /// between physics ticks. Does not depend on the Physics layer collision matrix.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class KartBananaTrap : MonoBehaviour
    {
        [Header("Arcade Hit Detection")]
        [Tooltip("World-space trigger radius. Racer BoxCollider contact is also checked directly.")]
        [SerializeField, Min(0.5f)] private float detectionRadius = 1.05f;
        [Tooltip("Height of the detection sphere above the placement surface.")]
        [SerializeField, Min(0.1f)] private float sensorHeight = 0.58f;
        [Tooltip("Extra radius for the swept kart-center test, accounting for the kart's body width.")]
        [SerializeField, Min(0f)] private float sweepExtraRadius = 0.65f;
        [Tooltip("Do not hit a racer passing on a bridge or jumping well above the banana.")]
        [SerializeField, Min(0.5f)] private float maximumHeightDifference = 1.8f;

        [Header("Impact Feel")]
        [Tooltip("Fraction of horizontal speed retained on a successful hit. CarController supplies the stun/wobble.")]
        [SerializeField, Range(0.3f, 1f)] private float impactPlanarSpeedMultiplier = 0.68f;
        [Tooltip("Additional sideways kick, on top of CarController's Banana Side Impulse.")]
        [SerializeField, Min(0f)] private float extraSideImpulse = 1.2f;

        [Header("Dizzy VFX (Optional)")]
        [Tooltip("Assign your existing Dizzy Particle prefab here on the BANANA prefab. The ParticleSpin script can stay on that prefab.")]
        [SerializeField] private GameObject dizzyParticlePrefab;
        [Tooltip("Position relative to the kart root, unless the kart has a direct child named DizzyAnchor (then the anchor is used instead).")]
        [SerializeField] private Vector3 dizzyLocalOffset = new Vector3(0f, 1.55f, 0f);
        [Tooltip("Additional scale multiplier for the original prefab size.")]
        [SerializeField, Min(0.01f)] private float dizzyScaleMultiplier = 1f;

        [Header("Orientation")]
        [Tooltip("Góc xoay bù thêm (Euler) so với mặt đường. Dùng để tinh chỉnh hướng hoặc độ nghiêng của vỏ chuối trong Inspector nếu muốn dựng thẳng đứng hoặc nghiêng theo ý.")]
        [SerializeField] private Vector3 surfaceRotationOffset = Vector3.zero;

        public Vector3 SurfaceRotationOffset
        {
            get => surfaceRotationOffset;
            set => surfaceRotationOffset = value;
        }

        [Header("Debug")]
        [SerializeField] private bool debugBanana = true;
        [SerializeField] private int trackedRacerCount;
        [SerializeField] private bool armed;
        [SerializeField] private bool spent;

        private sealed class RacerTrack
        {
            public CarController car;
            public Rigidbody body;
            public Collider[] colliders;
            public Vector3 lastCenter;
        }

        private readonly List<RacerTrack> racers = new List<RacerTrack>(4);
        private SphereCollider sensor;
        private CarController owner;
        private Vector3 groundPoint;
        private Vector3 groundNormal = Vector3.up;
        private Vector3 sensorCenterWorld;
        private float ownerSafeUntil;
        private float stunDuration;
        private float nextRacerRefreshAt;
        private Material fallbackMaterial;

        // Compatibility with existing KartItemPickup implementations.
        public void Configure(CarController source, float trapLifetime, float slipTime, float ownerImmunity)
        {
            Configure(source, trapLifetime, slipTime, ownerImmunity, transform.position, Vector3.up);
        }

        public void Configure(CarController source, float trapLifetime, float slipTime,
            float ownerImmunity, Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            owner = source;
            groundPoint = surfacePoint;
            groundNormal = surfaceNormal.sqrMagnitude > 0.001f ? surfaceNormal.normalized : Vector3.up;
            stunDuration = Mathf.Max(0.1f, slipTime);
            ownerSafeUntil = Time.time + Mathf.Max(0f, ownerImmunity);
            spent = false;
            armed = false;

            // Align the actual mesh, not merely its prefab pivot. The sensor stays
            // anchored to the road point even when a model has an offset pivot.
            AlignVisualToSurface();
            sensorCenterWorld = groundPoint + groundNormal * sensorHeight;
            PrepareSensorAndPrefabColliders();
            RefreshRacers();
            Physics.SyncTransforms();
            armed = true;

            CreatePlacementBeacon();

            if (debugBanana)
                Debug.Log($"[Banana] ARMED at {groundPoint} | sensor={sensorCenterWorld} | radius={detectionRadius:0.00}m | racers={trackedRacerCount} | owner={(owner != null ? owner.name : "None")}", this);

            Destroy(gameObject, Mathf.Max(0.5f, trapLifetime));
        }

        private void AlignVisualToSurface()
        {
            if (surfaceRotationOffset.sqrMagnitude > 0.0001f)
            {
                transform.rotation *= Quaternion.Euler(surfaceRotationOffset);
            }

            transform.position = groundPoint + groundNormal * 0.035f;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Bounds visualBounds = default;
            bool hasVisual = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer || renderer is LineRenderer)
                    continue;

                if (!hasVisual)
                {
                    visualBounds = renderer.bounds;
                    hasVisual = true;
                }
                else
                {
                    visualBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasVisual)
                return;

            // Imported banana prefabs often have a visual pivot offset from the root.
            Vector3 horizontalCorrection = new Vector3(
                groundPoint.x - visualBounds.center.x, 0f,
                groundPoint.z - visualBounds.center.z);
            horizontalCorrection = Vector3.ClampMagnitude(horizontalCorrection, 2.5f);
            float verticalCorrection = Mathf.Clamp(
                groundPoint.y + 0.015f - visualBounds.min.y, -2.5f, 2.5f);
            transform.position += horizontalCorrection + Vector3.up * verticalCorrection;
        }

        private void PrepareSensorAndPrefabColliders()
        {
            sensor = GetComponent<SphereCollider>();
            if (sensor == null)
                sensor = gameObject.AddComponent<SphereCollider>();

            // A banana is a trigger trap, not a physical obstacle. Disable imported
            // mesh/box colliders that could shove the car or hide the real sensor.
            Collider[] bananaColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < bananaColliders.Length; i++)
            {
                Collider collider = bananaColliders[i];
                if (collider != null && collider != sensor)
                    collider.enabled = false;
            }

            // A kinematic Rigidbody makes trigger messages reliable even with
            // unusual prefab hierarchy. The direct scan below works without them.
            Rigidbody[] bananaBodies = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bananaBodies.Length; i++)
            {
                Rigidbody body = bananaBodies[i];
                if (body == null) continue;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = false;
                body.isKinematic = true;
            }
            if (GetComponent<Rigidbody>() == null)
            {
                Rigidbody body = gameObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
            }

            // Keep the prefab's original scale: compensate for it in the sensor.
            Vector3 scale = transform.lossyScale;
            float maxScale = Mathf.Max(0.001f,
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            sensor.center = transform.InverseTransformPoint(sensorCenterWorld);
            sensor.radius = detectionRadius / maxScale;
            sensor.isTrigger = true;
            sensor.enabled = true;
        }

        private void RefreshRacers()
        {
            racers.Clear();
            CarController[] all = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                CarController car = all[i];
                if (car == null || car.TargetBody == null) continue;
                racers.Add(new RacerTrack
                {
                    car = car,
                    body = car.TargetBody,
                    colliders = car.GetComponentsInChildren<Collider>(true),
                    lastCenter = car.TargetBody.worldCenterOfMass
                });
            }
            trackedRacerCount = racers.Count;
            nextRacerRefreshAt = Time.time + 1f;
        }

        private void OnTriggerEnter(Collider other) => TryHit(FindCar(other), "TriggerEnter");
        private void OnTriggerStay(Collider other) => TryHit(FindCar(other), "TriggerStay");

        private static CarController FindCar(Collider other)
        {
            if (other == null) return null;
            Rigidbody body = other.attachedRigidbody;
            CarController car = body != null ? body.GetComponentInParent<CarController>() : null;
            return car != null ? car : other.GetComponentInParent<CarController>();
        }

        private void FixedUpdate()
        {
            if (!armed || spent) return;

            // Scene racers are normally static; this fallback only handles a trap
            // configured before the cars have finished spawning.
            if (racers.Count == 0 && Time.time >= nextRacerRefreshAt)
                RefreshRacers();

            for (int i = 0; i < racers.Count; i++)
            {
                RacerTrack track = racers[i];
                if (track.car == null || track.body == null) continue;

                Vector3 current = track.body.worldCenterOfMass;
                Vector3 previous = track.lastCenter;
                track.lastCenter = current;

                if (!track.car.isActiveAndEnabled || !track.car.ControlEnabled ||
                    (track.car == owner && Time.time < ownerSafeUntil))
                    continue;

                if (TouchesCarCollider(track.colliders))
                {
                    TryHit(track.car, "ColliderClosestPoint");
                }
                else if (CrossedSensor(previous, current))
                {
                    TryHit(track.car, "SweptKartPath");
                }

                if (spent) break;
            }
        }

        private bool TouchesCarCollider(Collider[] carColliders)
        {
            if (carColliders == null) return false;
            float radiusSquared = detectionRadius * detectionRadius;
            for (int i = 0; i < carColliders.Length; i++)
            {
                Collider collider = carColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger ||
                    !collider.gameObject.activeInHierarchy)
                    continue;
                Vector3 closest = collider.ClosestPoint(sensorCenterWorld);
                if ((closest - sensorCenterWorld).sqrMagnitude <= radiusSquared)
                    return true;
            }
            return false;
        }

        private bool CrossedSensor(Vector3 previous, Vector3 current)
        {
            // Closest point to the kart's movement segment, measured in the XZ
            // plane; this catches a fast kart that crosses between FixedUpdates.
            Vector2 a = new Vector2(previous.x, previous.z);
            Vector2 b = new Vector2(current.x, current.z);
            Vector2 point = new Vector2(sensorCenterWorld.x, sensorCenterWorld.z);
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude)
                : 0f;
            Vector2 closest = a + segment * t;
            float radius = detectionRadius + sweepExtraRadius;
            if ((closest - point).sqrMagnitude > radius * radius)
                return false;

            float kartHeight = Mathf.Lerp(previous.y, current.y, t);
            return Mathf.Abs(kartHeight - groundPoint.y) <= maximumHeightDifference;
        }

        private void TryHit(CarController car, string source)
        {
            if (!armed || spent || car == null || !car.isActiveAndEnabled ||
                car.TargetBody == null || !car.ControlEnabled)
                return;
            if (car == owner && Time.time < ownerSafeUntil)
                return;

            spent = true; // One trap, one racer. A shield also consumes the trap.
            armed = false;
            bool stunned = car.TryApplyBananaStun(stunDuration);
            if (stunned)
            {
                Rigidbody body = car.TargetBody;
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(
                    velocity.x * impactPlanarSpeedMultiplier,
                    velocity.y,
                    velocity.z * impactPlanarSpeedMultiplier);

                if (extraSideImpulse > 0f)
                {
                    float lateral = Vector3.Dot(body.linearVelocity, car.transform.right);
                    float direction = Mathf.Abs(lateral) > 0.05f
                        ? Mathf.Sign(lateral)
                        : (Random.value < 0.5f ? -1f : 1f);
                    body.AddForce(car.transform.right * (direction * extraSideImpulse),
                        ForceMode.VelocityChange);
                }

                // Spin wobble torque: xe quay tít xoắn ốc khi dẫm phải chuối
                car.TargetBody.AddTorque(car.transform.up * (Random.value < 0.5f ? 12.5f : -12.5f), ForceMode.Impulse);

                PoseKart.Audio.AudioManager.PlayBananaSlip(transform.position);

                // VFX: Vết trượt lốp xe màu vàng/đen xoắn quẩy trên mặt đường
                SpawnBananaSkidmarks();

                // Only show Dizzy when the banana actually stuns the kart.
                // Shield-blocked hits must never spawn the effect.
                GameObject dizzyPrefab = dizzyParticlePrefab;
                if (dizzyPrefab == null)
                {
#if UNITY_EDITOR
                    dizzyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/PoseKart/Asset Them Tu Be Ngoai/DIzzyEffect/Dizzy Effect.prefab");
#endif
                }

                if (dizzyPrefab != null)
                {
                    PoseKartBananaDizzyVFX dizzy = car.GetComponent<PoseKartBananaDizzyVFX>();
                    if (dizzy == null)
                        dizzy = car.gameObject.AddComponent<PoseKartBananaDizzyVFX>();
                    dizzy.Play(car, dizzyPrefab, dizzyLocalOffset, dizzyScaleMultiplier);
                }
            }

            if (debugBanana)
                Debug.Log($"[Banana] HIT {car.name} via {source} | stunned={stunned} | " +
                    (stunned ? $"duration={stunDuration:0.00}s" : "blocked (usually Shield)"), this);

            // Giọt dầu/mực vàng và đen bắn tung tóe xung quanh
            KartParticleEffect.Burst(groundPoint + groundNormal * 0.25f,
                stunned ? new Color(1f, 0.82f, 0.08f, 1f) : new Color(0.25f, 0.9f, 1f, 1f), stunned ? 34 : 20);
            if (stunned)
            {
                KartParticleEffect.Burst(groundPoint + groundNormal * 0.15f, new Color(0.12f, 0.08f, 0.02f, 1f), 22);
            }

            if (sensor != null) sensor.enabled = false;
            Destroy(gameObject);
        }

        private void SpawnBananaSkidmarks()
        {
            if (Skidmarks.Instance == null) return;

            int lastIdx = -1;
            for (int s = 0; s < 10; s++)
            {
                float angle = s * 36f * Mathf.Deg2Rad;
                float r = 0.45f + s * 0.08f;
                Vector3 skidPos = groundPoint + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                Color32 skidColor = s % 2 == 0 ? new Color32(235, 195, 25, 220) : new Color32(25, 20, 15, 230);
                lastIdx = Skidmarks.Instance.AddSkidMark(skidPos, groundNormal, skidColor, lastIdx, 0.45f);
            }
        }

        private void CreatePlacementBeacon()
        {
            // Viền sáng mềm mại dưới đất quanh vỏ chuối (Stylized Outline Glow)
            GameObject beaconObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            beaconObj.name = "Banana_Ground_Beacon";
            beaconObj.transform.SetParent(transform, false);
            beaconObj.transform.position = groundPoint + groundNormal * 0.025f;
            beaconObj.transform.rotation = Quaternion.FromToRotation(Vector3.forward, groundNormal);
            beaconObj.transform.localScale = Vector3.one * 1.5f;

            Collider col = beaconObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            MeshRenderer mr = beaconObj.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null && mr != null)
            {
                Material mat = new Material(shader);
                Color yellowGlow = new Color(1f, 0.82f, 0.12f, 0.38f);
                mat.color = yellowGlow;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellowGlow);
                mr.sharedMaterial = mat;
            }

            beaconObj.AddComponent<BananaBeaconAnimator>();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = armed
                ? sensorCenterWorld
                : transform.position + transform.up * sensorHeight;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(center, detectionRadius);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, center - transform.up * sensorHeight);
        }

        private void OnDestroy()
        {
            if (fallbackMaterial != null)
                Destroy(fallbackMaterial);
        }

        public static GameObject CreateFallbackVisual()
        {
            GameObject root = new GameObject("Banana Trap");
            root.AddComponent<SphereCollider>().isTrigger = true;
            GameObject peel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Collider peelCollider = peel.GetComponent<Collider>();
            if (peelCollider != null) Destroy(peelCollider);
            peel.transform.SetParent(root.transform, false);
            peel.transform.localPosition = Vector3.up * 0.18f;
            peel.transform.localScale = new Vector3(0.24f, 0.36f, 0.24f);
            peel.transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
            Renderer renderer = peel.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                Material material = new Material(shader);
                Color yellow = new Color(1f, 0.76f, 0.07f, 1f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", yellow);
                if (material.HasProperty("_Color")) material.SetColor("_Color", yellow);
                renderer.sharedMaterial = material;
                // The trap owns the runtime material and releases it on destruction.
                KartBananaTrap trap = root.AddComponent<KartBananaTrap>();
                trap.fallbackMaterial = material;
            }
            return root;
        }
    }

    internal sealed class BananaBeaconAnimator : MonoBehaviour
    {
        private Material mat;
        private Color baseColor;

        private void Start()
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mat = mr.material;
                baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
            }
        }

        private void Update()
        {
            float pulse = Mathf.Sin(Time.time * 7f) * 0.15f + 0.85f;
            transform.localScale = Vector3.one * (1.5f * pulse);

            if (mat != null)
            {
                float a = Mathf.Sin(Time.time * 7f) * 0.16f + 0.32f;
                Color c = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                else mat.color = c;
            }
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
