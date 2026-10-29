using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class KartItemDefense : MonoBehaviour
    {
        [Header("Shield")]
        [SerializeField] private GameObject shieldVisualPrefab;
        [Tooltip("Prefab Particle hiệu ứng hào quang/vòng bảo vệ quanh xe khi bật khiên (tùy chọn).")]
        [SerializeField] private GameObject shieldParticlePrefab;
        [SerializeField, Min(0.1f)] private float shieldDuration = 5f;
        [SerializeField, Min(1)] private int maximumBlockedHits = 1;
        [SerializeField] private Color shieldColor = new Color(0.2f, 0.9f, 1f, 0.2f);

        [Header("Impact Protection")]
        [SerializeField] private bool protectAgainstMissiles = true;
        [SerializeField] private bool protectAgainstBanana = true;
        [SerializeField] private bool protectAgainstEnvironmentCollisions = true;
        [SerializeField] private bool protectAgainstVehicleCollisions = true;
        [SerializeField, Min(0f)] private float minimumMajorCollisionSpeed = 5f;
        [SerializeField, Min(0f)] private float collisionBlockCooldown = .4f;
        [SerializeField] private bool shieldImpactFlash = true;

        [Header("Runtime Shield Debug")]
        [SerializeField] private bool shieldActive;
        [SerializeField] private int blockedHitsRemaining;
        [SerializeField] private float shieldTimeRemaining;

        private GameObject shieldVisual;
        private GameObject activeShieldParticle;
        private GameObject shieldForceField;
        private Material generatedMaterial;
        private float shieldUntil;
        private float lastCollisionBlockTime = float.NegativeInfinity;

        // Client online chỉ hiển thị visual.
        // Thời gian/gameplay thật do Server quyết định.
        private bool visualOnlyShieldMode;

        public event System.Action OnShieldActivated;
        public event System.Action<Vector3> OnShieldBlocked;
        public event System.Action<bool> OnShieldDeactivated;

        public bool IsShieldActive => shieldActive;
        public float ShieldTimeRemaining => shieldTimeRemaining;
        public float ShieldDuration => shieldDuration;
        public int BlockedHitsRemaining => blockedHitsRemaining;
        public GameObject ShieldParticlePrefab { get => shieldParticlePrefab; set => shieldParticlePrefab = value; }

        public bool ProtectAgainstMissiles => protectAgainstMissiles;
        public bool ProtectAgainstBanana => protectAgainstBanana;
        public bool ProtectAgainstEnvironmentCollisions => protectAgainstEnvironmentCollisions;
        public bool ProtectAgainstVehicleCollisions => protectAgainstVehicleCollisions;

        public void ConfigureShield(
            GameObject visualPrefab,
            float duration,
            int hitCapacity,
            Color color)
        {
            ConfigureShield(visualPrefab, shieldParticlePrefab, duration, hitCapacity, color);
        }

        public void ConfigureShield(
            GameObject visualPrefab,
            GameObject particlePrefab,
            float duration,
            int hitCapacity,
            Color color)
        {
            shieldVisualPrefab = visualPrefab;
            shieldParticlePrefab = particlePrefab;
            shieldDuration = Mathf.Max(0.1f, duration);
            maximumBlockedHits = Mathf.Max(1, hitCapacity);
            shieldColor = color;
        }

        public void ActivateShieldVisualOnly()
        {
            RemoveShieldVisual();

            // Client chỉ mirror visual từ NetworkVariable.
            // KHÔNG được tự chạy timer hết hạn.
            visualOnlyShieldMode = true;
            shieldActive = true;
            shieldTimeRemaining = 0f;

            PoseKartShieldGeneratorVisual genVisual = GetComponent<PoseKartShieldGeneratorVisual>();
            bool hasCustomShieldVisual = genVisual != null && genVisual.HasForceShieldPrefab;

            if (!hasCustomShieldVisual)
            {
                shieldVisual = shieldVisualPrefab != null
                    ? Instantiate(shieldVisualPrefab, transform)
                    : CreateFallbackShieldVisual();
                if (shieldVisual != null)
                {
                    if (shieldVisual.transform.parent != transform)
                    {
                        shieldVisual.transform.SetParent(transform, false);
                    }
                    shieldVisual.name = "Active Shield Bubble";
                    shieldVisual.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                    shieldVisual.transform.localRotation = Quaternion.identity;

                    PoseKartShieldVisualAnimator animator = shieldVisual.GetComponent<PoseKartShieldVisualAnimator>();
                    if (animator == null)
                        animator = shieldVisual.AddComponent<PoseKartShieldVisualAnimator>();
                    animator.Configure(shieldColor);
                    animator.BeginFadeIn();
                }
            }
            else
            {
                genVisual.ActivateForceShield();
            }

            if (shieldParticlePrefab != null)
            {
                activeShieldParticle = Instantiate(shieldParticlePrefab, transform);
                activeShieldParticle.name = "Active Shield Particle";
                activeShieldParticle.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                activeShieldParticle.transform.localRotation = Quaternion.identity;
            }

            PoseKart.Audio.AudioManager.PlayShieldActivate(transform.position);
        }

        public void DeactivateShieldVisualOnly()
        {
            visualOnlyShieldMode = false;
            shieldActive = false;
            shieldTimeRemaining = 0f;

            // Custom Shield Generator có thể đang nghe event này
            // để thu Force Shield visual xuống.
            OnShieldDeactivated?.Invoke(false);

            RemoveShieldVisual();
        }

        public void ActivateShield()
        {
            RemoveShieldVisual();

            visualOnlyShieldMode = false;
            shieldActive = true;
            blockedHitsRemaining = Mathf.Max(1, maximumBlockedHits);
            shieldUntil = Time.time + Mathf.Max(0.1f, shieldDuration);
            Debug.Log(
    $"[SHIELD TRACE] ACTIVATE | {name} | " +
    $"t={Time.time:F2} | until={shieldUntil:F2} | " +
    $"duration={shieldDuration:F2} | hits={blockedHitsRemaining}",
    this
);
            if (PoseKartNetworkItemBridge.IsOnlineRace && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                GetComponent<PoseKartNetworkItemBridge>()?.ServerSetShieldActive(true);
            }

            // Check if dedicated Force Shield Generator visual is managing the energy dome
            PoseKartShieldGeneratorVisual genVisual = GetComponent<PoseKartShieldGeneratorVisual>();
            bool hasCustomShieldVisual = genVisual != null && genVisual.HasForceShieldPrefab;

            // 1. Visual Bubble (Spawn fallback only if no custom Force Shield prefab is assigned)
            if (!hasCustomShieldVisual)
            {
                shieldVisual = shieldVisualPrefab != null
                    ? Instantiate(shieldVisualPrefab, transform)
                    : CreateFallbackShieldVisual();
                if (shieldVisual != null)
                {
                    if (shieldVisual.transform.parent != transform)
                    {
                        shieldVisual.transform.SetParent(transform, false);
                    }
                    shieldVisual.name = "Active Shield Bubble";
                    shieldVisual.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                    shieldVisual.transform.localRotation = Quaternion.identity;

                    PoseKartShieldVisualAnimator animator = shieldVisual.GetComponent<PoseKartShieldVisualAnimator>();
                    if (animator == null)
                        animator = shieldVisual.AddComponent<PoseKartShieldVisualAnimator>();
                    animator.Configure(shieldColor);
                    animator.BeginFadeIn();
                }
            }
            else
            {
                genVisual.ActivateForceShield();
            }

            // 2. Optional Particle Prefab (Aura / Rings / Sparks)
            if (shieldParticlePrefab != null)
            {
                activeShieldParticle = Instantiate(shieldParticlePrefab, transform);
                activeShieldParticle.name = "Active Shield Particle";
                activeShieldParticle.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                activeShieldParticle.transform.localRotation = Quaternion.identity;
            }

            // 3. Trigger Force-Field (Physical repel against opposing cars without propping the kart off the ground)
            shieldForceField = new GameObject("Active Shield ForceField");
            shieldForceField.transform.SetParent(transform, false);
            shieldForceField.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            shieldForceField.transform.localRotation = Quaternion.identity;
            shieldForceField.transform.localScale = Vector3.one;

            KartShieldPhysicalCollider physicalCollider = shieldForceField.AddComponent<KartShieldPhysicalCollider>();
            CarController car = GetComponent<CarController>();
            PoseKartShieldVisualAnimator activeAnimator = shieldVisual != null ? shieldVisual.GetComponent<PoseKartShieldVisualAnimator>() : null;
            physicalCollider.Initialize(car, this, activeAnimator);

            PoseKart.Audio.AudioManager.PlayShieldActivate(transform.position);

            OnShieldActivated?.Invoke();
        }

        public bool TryBlockIncomingItem(Vector3 impactPoint)
        {
            return TryConsumeShield(impactPoint, protectAgainstBanana);
        }

        public bool TryBlockMissileExplosion(Vector3 impactPoint)
        {
            return TryConsumeShield(impactPoint, protectAgainstMissiles);
        }

        public bool TryBlockMajorCollision(Vector3 impactPoint, bool vehicleImpact, float relativeSpeed)
        {
            if (relativeSpeed < minimumMajorCollisionSpeed)
                return false;
            if (vehicleImpact ? !protectAgainstVehicleCollisions : !protectAgainstEnvironmentCollisions)
                return false;
            if (Time.time < lastCollisionBlockTime + collisionBlockCooldown)
                return false;

            if (!TryConsumeShield(impactPoint, true))
                return false;

            lastCollisionBlockTime = Time.time;
            return true;
        }

        public void ResetForRace()
        {
            Debug.LogWarning(
    $"[SHIELD TRACE] RESET FOR RACE | {name} | t={Time.time:F2}",
    this
);
            lastCollisionBlockTime = float.NegativeInfinity;
            DeactivateShield(false, false);
        }

        private bool TryConsumeShield(Vector3 impactPoint, bool protectionEnabled)
        {
            // Nitro protection must not spend a charge from a separately equipped shield.
            CarController car = GetComponent<CarController>();
            if (car != null && car.IsShockwaveActive) return true;
            if (!protectionEnabled || !shieldActive || blockedHitsRemaining <= 0 || Time.time >= shieldUntil)
                return false;
            Debug.LogWarning(
    $"[SHIELD TRACE] CONSUME | {name} | " +
    $"t={Time.time:F2} | hitsBefore={blockedHitsRemaining} | " +
    $"point={impactPoint}",
    this
);
            blockedHitsRemaining--;

            // 1. Procedural metallic "PING! / DEFLECT!" sound + centralized audio manager
            PlayDeflectPingSound(impactPoint);
            PoseKart.Audio.AudioManager.PlayShieldBlock(impactPoint);

            // 2. Pure brilliant white flash on active shield visual dome
            if (shieldImpactFlash && shieldVisual != null)
            {
                PoseKartShieldVisualAnimator animator = shieldVisual.GetComponent<PoseKartShieldVisualAnimator>();
                if (animator != null)
                    animator.FlashImpact();
            }

            // 3. Energy sparks burst & radial deflect shockwave pushing outwards
            Color energyCyan = new Color(0.18f, 0.92f, 1f, 1f);
            KartParticleEffect.Burst(impactPoint, energyCyan, 28);
            KartParticleEffect.Burst(impactPoint, Color.white, 14);
            SpawnDeflectShockwave(impactPoint, energyCyan);

            // 4. Arcade HUD callout banner ("BLOCKED!") for the player
            if (IsPlayerKart())
            {
                PoseKartShieldBlockedUI.ShowBlocked("BLOCKED!");
            }

            OnShieldBlocked?.Invoke(impactPoint);

            // 5. Shatter into energy shards if shield capacity is exhausted
            if (blockedHitsRemaining <= 0)
            {
                SpawnShatterShards();
                DeactivateShield(false);
            }

            return true;
        }

        private bool IsPlayerKart()
        {
            RaceParticipant participant = GetComponent<RaceParticipant>();
            if (participant != null)
                return participant.IsPlayer;
            return gameObject.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SpawnShatterShards()
        {
            Vector3 center = transform.position + Vector3.up * 0.65f;
            Color shardCyan = new Color(0.2f, 0.95f, 1f, 1f);
            KartParticleEffect.Burst(center, shardCyan, 42);
            KartParticleEffect.Burst(center, Color.white, 20);
            SpawnDeflectShockwave(center, shardCyan);
        }

        private static void SpawnDeflectShockwave(Vector3 point, Color color)
        {
            GameObject waveObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            waveObj.name = "Shield_Deflect_Shockwave";
            waveObj.transform.position = point + Vector3.up * 0.12f;
            waveObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            waveObj.transform.localScale = Vector3.one * 0.45f;

            Collider col = waveObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            MeshRenderer mr = waveObj.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null && mr != null)
            {
                Material mat = new Material(shader);
                mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                mr.sharedMaterial = mat;
            }

            ShockwaveRingAnimator anim = waveObj.AddComponent<ShockwaveRingAnimator>();
            anim.Initialize(6.5f, 0.35f);
        }

        private static AudioClip deflectPingClip;
        public static void PlayDeflectPingSoundAt(Vector3 position) => PlayDeflectPingSound(position);
        private static void PlayDeflectPingSound(Vector3 position)
        {
            if (deflectPingClip == null)
            {
                int sampleRate = 44100;
                float duration = 0.32f;
                int count = Mathf.CeilToInt(sampleRate * duration);
                float[] samples = new float[count];

                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / sampleRate;
                    float envelope = Mathf.Exp(-t * 15f); // Fast metallic exponential ring decay
                    // Inharmonic metallic resonance modes:
                    float f1 = Mathf.Sin(2f * Mathf.PI * 1850f * t);
                    float f2 = Mathf.Sin(2f * Mathf.PI * 2920f * t) * 0.6f;
                    float f3 = Mathf.Sin(2f * Mathf.PI * 4580f * t) * 0.4f;
                    float f4 = Mathf.Sin(2f * Mathf.PI * 6800f * t) * 0.22f;
                    samples[i] = (f1 + f2 + f3 + f4) * envelope * 0.75f;
                }

                deflectPingClip = AudioClip.Create("Shield_Deflect_Ping_Procedural", count, 1, sampleRate, false);
                deflectPingClip.SetData(samples, 0);
            }

            PoseKart.Audio.AudioManager audioManager =
                PoseKart.Audio.AudioManager.Instance;

            if (audioManager != null)
            {
                audioManager.PlaySFXAtPosition(
                    deflectPingClip,
                    position,
                    0.95f,
                    1f,
                    3f,
                    100f
                );
            }
            else
            {
                AudioSource.PlayClipAtPoint(
                    deflectPingClip,
                    position,
                    0.95f
                );
            }
        }

        private void Update()
        {
            if (!shieldActive)
                return;

            // Client online chỉ mirror visual.
            // Server sẽ quyết định lúc nào Shield tắt
            // thông qua networkShieldActive.
            if (visualOnlyShieldMode)
                return;

            shieldTimeRemaining =
                Mathf.Max(0f, shieldUntil - Time.time);

            if (Time.time >= shieldUntil)
            {
                DeactivateShield(true);
            }
        }

        private void DeactivateShield(bool expired, bool animate = true)
        {
            Debug.LogWarning(
    $"[SHIELD TRACE] DEACTIVATE | {name} | " +
    $"expired={expired} | t={Time.time:F2} | " +
    $"until={shieldUntil:F2} | hits={blockedHitsRemaining}",
    this
);
            if (!shieldActive && shieldVisual == null && activeShieldParticle == null && shieldForceField == null)
            {
                return;
            }

            shieldActive = false;
            blockedHitsRemaining = 0;
            shieldTimeRemaining = 0f;

            PoseKartNetworkItemBridge networkBridge = null;
            if (PoseKartNetworkItemBridge.IsOnlineRace && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                networkBridge = GetComponent<PoseKartNetworkItemBridge>();
                networkBridge?.ServerSetShieldActive(false);
            }

            if (expired)
            {
                PoseKart.Audio.AudioManager.PlayShieldExpire(transform.position);
                networkBridge?.ServerBroadcastShieldExpiredSfx(transform.position);
            }

            OnShieldDeactivated?.Invoke(expired);

            // Cleanup physical force field trigger
            if (shieldForceField != null)
            {
                Destroy(shieldForceField);
                shieldForceField = null;
            }

            // Cleanup particle prefab instance
            if (activeShieldParticle != null)
            {
                ParticleSystem[] systems = activeShieldParticle.GetComponentsInChildren<ParticleSystem>();
                if (systems != null && systems.Length > 0)
                {
                    for (int i = 0; i < systems.Length; i++)
                    {
                        if (systems[i] != null)
                            systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    Destroy(activeShieldParticle, 1.2f);
                }
                else
                {
                    Destroy(activeShieldParticle);
                }
                activeShieldParticle = null;
            }

            // Cleanup visual bubble
            GameObject visual = shieldVisual;
            shieldVisual = null;
            if (animate && visual != null)
            {
                PoseKartShieldVisualAnimator animator = visual.GetComponent<PoseKartShieldVisualAnimator>();
                if (animator != null)
                    animator.FadeOutAndDestroy();
                else
                    Destroy(visual);
            }
            else if (visual != null)
            {
                Destroy(visual);
            }
        }

        private GameObject CreateFallbackShieldVisual()
        {
            GameObject bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = bubble.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            bubble.transform.localScale = Vector3.one * 3.8f;

            Renderer renderer = bubble.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (renderer != null && shader != null)
            {
                generatedMaterial = new Material(shader);
                generatedMaterial.color = shieldColor;
                generatedMaterial.SetColor("_BaseColor", shieldColor);
                generatedMaterial.SetColor("_EmissionColor", shieldColor * 1.5f);
                generatedMaterial.SetFloat("_Surface", 1f);
                generatedMaterial.SetFloat("_Blend", 0f);
                generatedMaterial.SetFloat("_ZWrite", 0f);
                generatedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                generatedMaterial.renderQueue = 3000;
                renderer.sharedMaterial = generatedMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return bubble;
        }

        private void RemoveShieldVisual()
        {
            if (shieldForceField != null)
            {
                Destroy(shieldForceField);
                shieldForceField = null;
            }

            if (activeShieldParticle != null)
            {
                Destroy(activeShieldParticle);
                activeShieldParticle = null;
            }

            if (shieldVisual != null)
            {
                Destroy(shieldVisual);
                shieldVisual = null;
            }

            if (generatedMaterial != null)
            {
                Destroy(generatedMaterial);
                generatedMaterial = null;
            }
        }

        private void OnDestroy()
        {
            RemoveShieldVisual();
        }
    }

    /// <summary>
    /// Trigger-based protective force-field for the active Energy Shield.
    /// Uses isTrigger = true so it NEVER pushes against the ground or lifts the kart into mid-air,
    /// while detecting other karts to repel them violently and blocking hazards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartShieldPhysicalCollider : MonoBehaviour
    {
        [Header("Physical Shield Settings")]
        [SerializeField] private float repelForce = 15f;

        private CarController ownerCar;
        private KartItemDefense defense;
        private PoseKartShieldVisualAnimator animator;
        private CapsuleCollider shieldCollider;

        public void Initialize(CarController car, KartItemDefense defenseComponent, PoseKartShieldVisualAnimator visualAnimator)
        {
            ownerCar = car;
            defense = defenseComponent;
            animator = visualAnimator;

            // Trigger capsule collider: Never collides with road/ground mesh, preventing levitation!
            shieldCollider = GetComponent<CapsuleCollider>();
            if (shieldCollider == null)
                shieldCollider = gameObject.AddComponent<CapsuleCollider>();

            shieldCollider.direction = 2; // Z-axis lengthwise
            shieldCollider.center = Vector3.zero;
            shieldCollider.radius = 1.45f;
            shieldCollider.height = 3.8f;
            shieldCollider.isTrigger = true; // MUST be trigger so physics solver doesn't prop vehicle into the air!
            shieldCollider.enabled = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleInteraction(other, isContinuous: false);
        }

        private void OnTriggerStay(Collider other)
        {
            HandleInteraction(other, isContinuous: true);
        }

        private void HandleInteraction(Collider other, bool isContinuous)
        {
            if (other == null || ownerCar == null)
                return;

            // Ignore own vehicle parts
            if (other.transform.IsChildOf(ownerCar.transform))
                return;

            // 1. CAR-TO-CAR INTERACTION: Repel opposing karts away violently
            CarController otherCar = other.GetComponentInParent<CarController>();
            if (otherCar != null && otherCar != ownerCar)
            {
                Rigidbody otherBody = otherCar.TargetBody != null ? otherCar.TargetBody : otherCar.GetComponent<Rigidbody>();
                if (otherBody != null && !otherBody.isKinematic)
                {
                    Vector3 repelDir = (otherBody.worldCenterOfMass - transform.position);
                    repelDir.y = 0.2f;
                    Vector3 normalDir = repelDir.sqrMagnitude > 0.001f ? repelDir.normalized : transform.forward;
                    float pushSpeed = isContinuous ? repelForce * 0.45f * Time.fixedDeltaTime : repelForce;
                    otherBody.AddForce(normalDir * pushSpeed, ForceMode.VelocityChange);
                }

                if (!isContinuous)
                {
                    Vector3 contactPoint =
                        other.ClosestPoint(transform.position);

                    // Vehicle collision chỉ tạo phản hồi Shield.
                    // KHÔNG consume Shield charge.
                    PoseKart.Audio.AudioManager.PlayShieldBlock(
                        contactPoint
                    );

                    if (PoseKartNetworkItemBridge.IsOnlineRace &&
                        Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsServer)
                    {
                        defense?
                            .GetComponent<PoseKartNetworkItemBridge>()?
                            .ServerBroadcastShieldContactSfx(contactPoint);
                    }

                    KartParticleEffect.Burst(
                        contactPoint,
                        new Color(0.18f, 0.88f, 1f, 1f),
                        20
                    );

                    if (animator != null)
                    {
                        animator.FlashImpact();
                    }
                }
                return;
            }

            // 2. ITEM & HAZARD PROTECTION: Block Banana Traps and direct hazards
            if (!isContinuous)
            {
                KartBananaTrap banana = other.GetComponentInParent<KartBananaTrap>();
                if (banana != null)
                {
                    Vector3 contactPoint = other.ClosestPoint(transform.position);
                    if (defense != null && defense.TryBlockIncomingItem(contactPoint))
                    {
                        Destroy(banana.gameObject);
                    }
                }
            }
        }
    }
}
