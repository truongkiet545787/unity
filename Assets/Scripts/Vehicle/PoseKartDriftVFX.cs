using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Visual-only arcade drift feedback for the existing CarController.
    /// Three timed spark tiers, tier flash and release burst. Never changes
    /// Rigidbody velocity, Nitro fuel, race state or the active camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartDriftVFX : MonoBehaviour
    {
        [Header("Drift VFX")]
        [SerializeField] private bool enableDriftVFX = true;
        [SerializeField, Min(0f)] private float minimumDriftSpeedKmh = 12f;
        [SerializeField, Min(0.01f)] private float emissionResponse = 10f;

        [Header("Rear Wheel Attachments")]
        [SerializeField] private Transform rearLeftEmitter;
        [SerializeField] private Transform rearRightEmitter;
        [SerializeField] private ParticleSystem[] dustParticleSystems = new ParticleSystem[0];
        [SerializeField] private ParticleSystem[] sparkParticleSystems = new ParticleSystem[0];

        [Header("Optional Prefab Sources")]
        [SerializeField] private Transform wheelParticlesRoot;
        [SerializeField] private Transform dashBurstParticlesRoot;
        [SerializeField] private GameObject wheelParticlesPrefab;
        [SerializeField] private GameObject dashBurstParticlesPrefab;
        [SerializeField] private Material smokeMaterial;
        [SerializeField] private Material sparkMaterial;

        [Header("Sparks - Existing Scene Settings")]
        [Tooltip("Bật/tắt tia lửa drift của PoseKartDriftVFX. Tắt đi nếu muốn KartVisualJuice độc quyền quản lý 3 cấp độ tia lửa.")]
        [SerializeField] private bool enableSparks = true;
        [Tooltip("Tự động nhường việc quản lý tia lửa và dash burst cho KartVisualJuice nếu tìm thấy trên xe để tránh xung đột.")]
        [SerializeField] private bool autoDelegateToVisualJuice = true;
        [Tooltip("Cố định hạt tia lửa bám sát vào bánh xe (SimulationSpace.Local), di chuyển theo xe chứ không bị tụt lại kéo thành vệt dài phía sau.")]
        [SerializeField] private bool pinSparksToWheels = true;
        [SerializeField, Min(0f)] private float sparkEmissionRate = 22f;
        [SerializeField, Min(0f)] private float sustainedSparkEmissionRate = 42f;
        [SerializeField] private Color sparkColor = new Color(1f, 0.9f, 0.65f, 1f); // Tier 1: yellow
        [SerializeField] private Color chargedSparkColor = new Color(0.2f, 0.9f, 1f, 1f); // Tier 2: cyan
        [SerializeField, Min(0.1f)] private float chargedDriftTime = 1.25f;
        [SerializeField, Min(0f)] private float sparkSize = 0.075f;
        [SerializeField, Min(0f)] private float chargedSparkSize = 0.12f;

        [Header("NEW - 3-Stage Arcade Drift")]
        [SerializeField] private bool enableStageFeedback = true;
        [SerializeField, Min(0.1f)] private float stageOneSeconds = 0.5f;
        [SerializeField, Min(0.15f)] private float stageTwoSeconds = 1.2f;
        [SerializeField, Min(0.2f)] private float stageThreeSeconds = 2.2f;
        [SerializeField] private Color ultraSparkColor = new Color(0.67f, 0.32f, 1f, 1f); // Tier 3: purple
        [SerializeField, Min(0f)] private float stageTwoEmissionMultiplier = 1.2f;
        [SerializeField, Min(0f)] private float stageThreeEmissionMultiplier = 1.5f;
        [SerializeField] private bool enableStageFlash = true;
        [SerializeField, Range(1, 20)] private int stageFlashParticlesPerWheel = 7;
        [SerializeField, Range(0.25f, 1f)] private float particleBudgetMultiplier = 1f;

        [Header("Dust")]
        [Tooltip("Emit assigned Dust_Left/Dust_Right systems during normal grounded driving, too.")]
        [SerializeField] private bool enableDrivingDust = true;
        [SerializeField, Min(0f)] private float minimumDrivingDustSpeedKmh = 5f;
        [SerializeField, Min(0.01f)] private float dustResponse = 8f;
        [SerializeField, Min(0f)] private float dustEmissionRate = 18f;
        [SerializeField, Min(0f)] private float dustSizeMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float dustOpacity = 0.55f;
        [SerializeField] private Color dustColor = new Color(0.42f, 0.27f, 0.14f, 0.42f);

        [Header("Rear Wheel Offsets")]
        [SerializeField] private Vector3 rearLeftOffset;
        [SerializeField] private Vector3 rearRightOffset;

        [Header("Drift Skidmarks")]
        [SerializeField] private bool enableDriftSkidmarks = true;
        [SerializeField] private Skidmarks skidmarksController;
        [SerializeField, Min(0.05f)] private float skidmarkWidth = 0.32f;

        [Header("Dash Burst")]
        [SerializeField] private bool enableDashBurst = true;
        [SerializeField] private ParticleSystem dashBurstParticleSystem;
        [SerializeField, Min(0.05f)] private float dashBurstDuration = 0.28f;
        [SerializeField, Min(0f)] private float dashBurstMinimumDriftTime = 0.35f;
        [SerializeField, Min(1)] private int dashBurstParticleCount = 18;

        [Header("Optional Visual-Only Body Lean")]
        [Tooltip("OFF by default. Use only a dedicated visual child NOT animated by KartDriveEffects or another script.")]
        [SerializeField] private bool enableBodyLean;
        [SerializeField] private Transform visualLeanPivot;
        [SerializeField, Range(0f, 20f)] private float maximumLeanDegrees = 9f;
        [SerializeField, Min(0.1f)] private float leanResponsiveness = 9f;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private bool isDrifting;
        [SerializeField] private bool isGrounded;
        [SerializeField] private float speedKmh;
        [SerializeField] private float driftDuration;
        [SerializeField, Range(0f, 1f)] private float effectBlend;
        [SerializeField, Range(0, 3)] private int currentDriftStage;

        private CarController car;
        private KartVisualJuice cachedVisualJuice;
        private float dustEffectBlend;
        private bool wasValidDrift;
        private bool dustWasEmitting;
        private bool sparksWereEmitting;
        private int appliedSparkStage = -1;
        private ParticleSystem[] dashBurstParticleSystems = new ParticleSystem[0];
        private readonly List<GameObject> runtimePrefabInstances = new List<GameObject>();
        private Transform cachedLeanPivot;
        private Quaternion originalLeanRotation;
        private bool leanWasApplied;
        private bool leanPivotIsSafe;
        private int lastLeftSkid = -1;
        private int lastRightSkid = -1;
        private float lastSkidUpdateTime;

        /// <summary>0 = none, 1 = yellow, 2 = cyan, 3 = purple. Visual state only.</summary>
        public int CurrentDriftStage => currentDriftStage;
        /// <summary>Optional hook for an existing camera/audio system; no camera is modified here.</summary>
        public event Action<int> DriftStageChanged;

        /// <summary>Returns true if PoseKartDriftVFX should actively control spark particle systems.</summary>
        public bool ShouldManageSparks
        {
            get
            {
                if (!enableSparks) return false;
                if (autoDelegateToVisualJuice)
                {
                    if (cachedVisualJuice == null && car != null)
                        cachedVisualJuice = car.GetComponent<KartVisualJuice>();
                    if (cachedVisualJuice != null && cachedVisualJuice.isActiveAndEnabled)
                        return false;
                }
                return true;
            }
        }

        private void OnValidate()
        {
            stageOneSeconds = Mathf.Max(0.1f, stageOneSeconds);
            stageTwoSeconds = Mathf.Max(stageOneSeconds + 0.05f, stageTwoSeconds);
            stageThreeSeconds = Mathf.Max(stageTwoSeconds + 0.05f, stageThreeSeconds);
        }

        private void Awake()
        {
            car = GetComponentInParent<CarController>();
            ResolveEmitters();
            EnsureParticleSystems();
            ConfigureParticleSystems();
            CacheLeanRestPose();
            ResetForRace();
        }

        private void OnEnable()
        {
            // Re-enable safely after a temporary VFX disable.
            if (car != null) ResetForRace();
        }

        private void Update()
        {
            if (car == null)
                car = GetComponentInParent<CarController>();
            if (car == null)
                return;

            float dt = Mathf.Max(0f, Time.deltaTime);
            speedKmh = car.TargetBody != null ? car.TargetBody.linearVelocity.magnitude * 3.6f : 0f;
            isDrifting = car.IsDrifting;
            isGrounded = car.IsGrounded;

            bool validDrift = enableDriftVFX && isDrifting && isGrounded &&
                              speedKmh >= minimumDriftSpeedKmh;
            bool validDrivingDust = enableDriftVFX && enableDrivingDust && isGrounded &&
                                    speedKmh >= minimumDrivingDustSpeedKmh;

            if (validDrift)
            {
                if (!wasValidDrift && ShouldManageSparks)
                    ApplySparkAppearance(0);

                driftDuration += dt;
                int nextStage = GetStage(driftDuration);
                if (nextStage != currentDriftStage)
                {
                    currentDriftStage = nextStage;
                    if (ShouldManageSparks)
                    {
                        ApplySparkAppearance(currentDriftStage);
                        if (currentDriftStage > 0 && enableStageFlash)
                            PlayStageFlash(currentDriftStage);
                    }
                    DriftStageChanged?.Invoke(currentDriftStage);
                }
            }
            else
            {
                // A valid drift RELEASE can burst; loss of ground/speed cannot.
                if (wasValidDrift && !isDrifting && isGrounded &&
                    driftDuration >= dashBurstMinimumDriftTime && ShouldManageSparks)
                    PlayDashBurst(currentDriftStage);

                driftDuration = 0f;
                if (currentDriftStage != 0)
                {
                    currentDriftStage = 0;
                    DriftStageChanged?.Invoke(0);
                }
            }

            effectBlend = !isGrounded ? 0f : Mathf.MoveTowards(effectBlend,
                validDrift ? 1f : 0f, emissionResponse * dt);
            dustEffectBlend = Mathf.MoveTowards(dustEffectBlend,
                validDrivingDust ? 1f : 0f, dustResponse * dt);

            UpdateContinuousSystems(effectBlend, dustEffectBlend);
            if (!validDrift && effectBlend <= 0.01f && appliedSparkStage != 0 && ShouldManageSparks)
                ApplySparkAppearance(0);
            wasValidDrift = validDrift;
        }

        private int GetStage(float seconds)
        {
            if (!enableStageFeedback) return 0;
            if (seconds >= stageThreeSeconds) return 3;
            if (seconds >= stageTwoSeconds) return 2;
            if (seconds >= stageOneSeconds) return 1;
            return 0;
        }

        private Color GetStageColor(int stage)
        {
            if (stage >= 3) return ultraSparkColor;
            if (stage == 2) return chargedSparkColor;
            return sparkColor;
        }

        private float GetStageSize(int stage)
        {
            float baseSize = Mathf.Max(0.005f, sparkSize);
            if (stage >= 3) return Mathf.Max(baseSize, chargedSparkSize) * 1.35f;
            if (stage == 2) return Mathf.Max(chargedSparkSize, baseSize * 1.15f);
            return stage == 1 ? baseSize : baseSize * 0.7f;
        }

        private void ApplySparkAppearance(int stage)
        {
            if (appliedSparkStage == stage) return;
            appliedSparkStage = stage;
            Color color = GetStageColor(stage);
            float size = GetStageSize(stage);
            for (int i = 0; i < sparkParticleSystems.Length; i++)
            {
                ParticleSystem system = sparkParticleSystems[i];
                if (system == null) continue;
                var main = system.main;
                main.startColor = color;
                main.startSize = new ParticleSystem.MinMaxCurve(size * 0.65f, size);
            }
        }

        private void PlayStageFlash(int stage)
        {
            Color color = GetStageColor(stage);
            float size = GetStageSize(stage);
            int count = Mathf.Max(1, Mathf.RoundToInt(stageFlashParticlesPerWheel * particleBudgetMultiplier));
            for (int i = 0; i < sparkParticleSystems.Length; i++)
            {
                ParticleSystem system = sparkParticleSystems[i];
                if (system == null) continue;
                if (!system.isPlaying) system.Play(false);
                Vector3 origin = GetSparkOrigin(system, i);
                for (int p = 0; p < count; p++)
                {
                    float side = (p & 1) == 0 ? -1f : 1f;
                    Vector3 sparkVel = pinSparksToWheels
                        ? (transform.right * (side * UnityEngine.Random.Range(1.2f, 2.8f)) + Vector3.up * UnityEngine.Random.Range(0.8f, 2.4f))
                        : (-transform.forward * UnityEngine.Random.Range(1.2f, 3.4f) +
                           transform.right * (side * UnityEngine.Random.Range(1.1f, 3.2f)) +
                           Vector3.up * UnityEngine.Random.Range(1.0f, 3.0f));

                    var particle = new ParticleSystem.EmitParams
                    {
                        position = origin,
                        velocity = sparkVel,
                        startColor = color,
                        startSize = size * UnityEngine.Random.Range(1.15f, 1.65f)
                    };
                    system.Emit(particle, 1);
                }
            }
        }

        private Vector3 GetSparkOrigin(ParticleSystem system, int index)
        {
            if (rearLeftEmitter != null && system.transform.IsChildOf(rearLeftEmitter))
                return rearLeftEmitter.TransformPoint(rearLeftOffset);
            if (rearRightEmitter != null && system.transform.IsChildOf(rearRightEmitter))
                return rearRightEmitter.TransformPoint(rearRightOffset);
            // For a legacy externally assigned system, its OWN position is reliable;
            // array parity is not a reliable left/right mapping.
            return system.transform.position;
        }

        private void ResolveEmitters()
        {
            if (rearLeftEmitter == null) rearLeftEmitter = transform.Find("Drift_Left");
            if (rearRightEmitter == null) rearRightEmitter = transform.Find("Drift_Right");
            Transform vehicleRoot = transform.parent;
            if (vehicleRoot == null) return;
            if (wheelParticlesRoot == null) wheelParticlesRoot = vehicleRoot.Find("WheelParticles");
            if (dashBurstParticlesRoot == null) dashBurstParticlesRoot = vehicleRoot.Find("Particles");
        }

        private void EnsureParticleSystems()
        {
            // Existing scene/Inspector references always take precedence.
            if (!HasParticleSystems(dustParticleSystems))
            {
                ParticleSystem[] found = FindParticleSystems(rearLeftEmitter, rearRightEmitter, false);
                if (!HasParticleSystems(found) && wheelParticlesRoot != null)
                    found = FilterByName(wheelParticlesRoot.GetComponentsInChildren<ParticleSystem>(true), false);
                if (HasParticleSystems(found))
                    dustParticleSystems = found;
                else if (wheelParticlesPrefab != null)
                {
                    GameObject instance = Instantiate(wheelParticlesPrefab, transform);
                    instance.name = "Drift_WheelParticles_Prefab";
                    runtimePrefabInstances.Add(instance);
                    dustParticleSystems = FilterByName(instance.GetComponentsInChildren<ParticleSystem>(true), false);
                }
                if (!HasParticleSystems(dustParticleSystems) && rearLeftEmitter != null && rearRightEmitter != null)
                    dustParticleSystems = CreateFallbackDustSystems();
            }

            if (!HasParticleSystems(sparkParticleSystems) && ShouldManageSparks)
            {
                ParticleSystem[] found = FindParticleSystems(rearLeftEmitter, rearRightEmitter, true);
                if (!HasParticleSystems(found) && wheelParticlesRoot != null)
                    found = FilterByName(wheelParticlesRoot.GetComponentsInChildren<ParticleSystem>(true), true);
                if (HasParticleSystems(found))
                    sparkParticleSystems = found;
                else if (rearLeftEmitter != null && rearRightEmitter != null)
                    sparkParticleSystems = CreateFallbackSparkSystems();
            }

            // IMPORTANT: never select ALL particles below a generic "Particles" root.
            // That would let ResetForRace disable Nitro, item and other effects.
            if (dashBurstParticleSystem != null)
            {
                dashBurstParticleSystems = new[] { dashBurstParticleSystem };
            }
            else
            {
                if (dashBurstParticlesRoot != null)
                {
                    ParticleSystem[] candidates = dashBurstParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i] != null && IsDashName(candidates[i].name))
                        {
                            dashBurstParticleSystem = candidates[i];
                            break;
                        }
                    }
                }
                if (dashBurstParticleSystem == null && dashBurstParticlesPrefab != null)
                {
                    GameObject instance = Instantiate(dashBurstParticlesPrefab, transform);
                    instance.name = "Drift_DashBurst_Prefab";
                    runtimePrefabInstances.Add(instance);
                    ParticleSystem[] candidates = instance.GetComponentsInChildren<ParticleSystem>(true);
                    if (candidates.Length > 0) dashBurstParticleSystem = candidates[0];
                }
                if (dashBurstParticleSystem == null)
                {
                    Transform burstParent = transform.Find("Drift_DashBurst");
                    if (burstParent == null)
                    {
                        GameObject holder = new GameObject("Drift_DashBurst");
                        holder.transform.SetParent(transform, false);
                        burstParent = holder.transform;
                        runtimePrefabInstances.Add(holder);
                    }
                    dashBurstParticleSystem = KartParticleEffect.Create("Drift Dash Burst", burstParent,
                        chargedSparkColor, dashBurstDuration, chargedSparkSize);
                }
                dashBurstParticleSystems = dashBurstParticleSystem != null
                    ? new[] { dashBurstParticleSystem } : new ParticleSystem[0];
            }

            for (int i = 0; i < dashBurstParticleSystems.Length; i++)
            {
                ParticleSystem system = dashBurstParticleSystems[i];
                if (system == null) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = false;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = Mathf.Max(64, dashBurstParticleCount * 2);
                ApplyMaterial(system, sparkMaterial);
                var emission = system.emission;
                emission.enabled = false; // manual burst only
            }
        }

        private static bool IsDashName(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains("dash") || lower.Contains("drift") || lower.Contains("burst");
        }

        private ParticleSystem[] CreateFallbackDustSystems()
        {
            return new[]
            {
                GetOrCreateSystem(rearLeftEmitter, "Soft Dust Left", false),
                GetOrCreateSystem(rearRightEmitter, "Soft Dust Right", false)
            };
        }

        private ParticleSystem[] CreateFallbackSparkSystems()
        {
            return new[]
            {
                GetOrCreateSystem(rearLeftEmitter, "Arcade Sparks Left", true),
                GetOrCreateSystem(rearRightEmitter, "Arcade Sparks Right", true)
            };
        }

        private static bool HasParticleSystems(ParticleSystem[] systems)
        {
            if (systems == null) return false;
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) return true;
            return false;
        }

        private static ParticleSystem[] FindParticleSystems(Transform left, Transform right, bool sparks)
        {
            var matches = new List<ParticleSystem>(4);
            if (left != null) AddMatchingSystems(matches, left.GetComponentsInChildren<ParticleSystem>(true), sparks);
            if (right != null) AddMatchingSystems(matches, right.GetComponentsInChildren<ParticleSystem>(true), sparks);
            return matches.ToArray();
        }

        private static ParticleSystem[] FilterByName(ParticleSystem[] candidates, bool sparks)
        {
            var matches = new List<ParticleSystem>(candidates.Length);
            AddMatchingSystems(matches, candidates, sparks);
            return matches.ToArray();
        }

        private static void AddMatchingSystems(List<ParticleSystem> matches, ParticleSystem[] systems, bool sparks)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null) continue;
                string name = system.name.ToLowerInvariant();
                bool looksLikeSpark = name.Contains("spark") || name.Contains("flash");
                bool looksLikeDust = name.Contains("dust") || name.Contains("smoke");
                if (sparks ? looksLikeSpark : looksLikeDust)
                    matches.Add(system);
            }
        }

        private ParticleSystem GetOrCreateSystem(Transform parent, string label, bool sparks)
        {
            Transform existing = parent.Find(label);
            ParticleSystem system = existing != null ? existing.GetComponent<ParticleSystem>() : null;
            if (system != null) return system;
            Color color = sparks ? sparkColor : dustColor;
            float lifetime = sparks ? 0.34f : 0.7f;
            float size = sparks ? sparkSize : 0.42f;
            system = KartParticleEffect.Create(label, parent, color, lifetime, size);
            var main = system.main;
            main.startSpeed = sparks
                ? new ParticleSystem.MinMaxCurve(1.5f, 4.5f)
                : new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.gravityModifier = sparks ? 0.35f : 0.12f;
            var emission = system.emission;
            emission.enabled = false;
            return system;
        }

        private void ConfigureParticleSystems()
        {
            for (int i = 0; i < dustParticleSystems.Length; i++)
            {
                ParticleSystem system = dustParticleSystems[i];
                if (system == null) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = WithOpacity(dustColor, dustOpacity);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f * dustSizeMultiplier, 0.48f * dustSizeMultiplier);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = 0f;
                main.maxParticles = 320;
                var emission = system.emission;
                emission.enabled = false;
                var shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 22f;
                shape.radius = 0.08f;
                ConfigureDustLifetimeModules(system);
                ApplyMaterial(system, smokeMaterial);
                ConfigureRenderer(system, false);
            }

            for (int i = 0; i < sparkParticleSystems.Length; i++)
            {
                ParticleSystem system = sparkParticleSystems[i];
                if (system == null) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = pinSparksToWheels ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
                main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
                main.startLifetime = pinSparksToWheels ? new ParticleSystem.MinMaxCurve(0.12f, 0.22f) : new ParticleSystem.MinMaxCurve(0.2f, 0.36f);
                main.startSpeed = pinSparksToWheels ? new ParticleSystem.MinMaxCurve(1f, 3f) : new ParticleSystem.MinMaxCurve(2f, 5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = pinSparksToWheels ? 0f : 0.15f;
                main.maxParticles = 180;
                var emission = system.emission;
                emission.enabled = false;
                var shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.04f;
                ConfigureSparkLifetimeModules(system);
                ApplyMaterial(system, sparkMaterial);
                ConfigureRenderer(system, true);
            }
            ApplySparkAppearance(0);
        }

        private void UpdateContinuousSystems(float sparkBlend, float dustBlend)
        {
            bool emitDust = dustBlend > 0.01f;
            bool emitSparks = sparkBlend > 0.01f && ShouldManageSparks;
            if (emitDust && !dustWasEmitting) PlaySystems(dustParticleSystems);
            if (emitSparks && !sparksWereEmitting) PlaySystems(sparkParticleSystems);

            float sustained = Mathf.Clamp01(driftDuration / Mathf.Max(0.1f, chargedDriftTime));
            float multiplier = currentDriftStage >= 3 ? stageThreeEmissionMultiplier :
                               currentDriftStage == 2 ? stageTwoEmissionMultiplier : 1f;
            float sparkRate = Mathf.Lerp(sparkEmissionRate, sustainedSparkEmissionRate, sustained) *
                              multiplier * sparkBlend * particleBudgetMultiplier;
            float dustRate = dustEmissionRate * dustBlend * particleBudgetMultiplier;

            for (int i = 0; i < dustParticleSystems.Length; i++)
            {
                ParticleSystem system = dustParticleSystems[i];
                if (system == null) continue;
                var emission = system.emission;
                if (emission.enabled != emitDust) emission.enabled = emitDust;
                if (emitDust) emission.rateOverTime = dustRate;
                // Do not reposition particles every frame: existing scene systems
                // are already children of Drift_Left / Drift_Right.
            }

            if (ShouldManageSparks)
            {
                for (int i = 0; i < sparkParticleSystems.Length; i++)
                {
                    ParticleSystem system = sparkParticleSystems[i];
                    if (system == null) continue;
                    var emission = system.emission;
                    if (emission.enabled != emitSparks) emission.enabled = emitSparks;
                    if (emitSparks) emission.rateOverTime = sparkRate;
                }

                if (!emitSparks && sparksWereEmitting) StopEmittingSystems(sparkParticleSystems);
                sparksWereEmitting = emitSparks;
            }

            if (!emitDust && dustWasEmitting) StopEmittingSystems(dustParticleSystems);
            dustWasEmitting = emitDust;

            UpdateDriftSkidmarks(sparkBlend);
        }

        private void UpdateDriftSkidmarks(float blend)
        {
            if (!enableDriftSkidmarks) return;
            if (skidmarksController == null)
            {
                skidmarksController = Skidmarks.GetOrCreateInstance();
                if (skidmarksController == null) return;
            }

            bool shouldSkid = isGrounded && (isDrifting || (car != null && car.IsBananaStunned)) && blend > 0.05f;
            if (shouldSkid && car != null && car.TargetBody != null)
            {
                float opacity = Mathf.Clamp01(blend * 0.9f);
                Vector3 vel = car.TargetBody.linearVelocity;
                float dt = Time.time - lastSkidUpdateTime;

                if (rearLeftEmitter != null && ProbeWheelGround(rearLeftEmitter.position, out Vector3 leftPt, out Vector3 leftNorm))
                {
                    Vector3 skidPt = leftPt + vel * dt;
                    lastLeftSkid = skidmarksController.AddSkidMark(skidPt, leftNorm, opacity, lastLeftSkid, skidmarkWidth);
                }
                else
                {
                    lastLeftSkid = -1;
                }

                if (rearRightEmitter != null && ProbeWheelGround(rearRightEmitter.position, out Vector3 rightPt, out Vector3 rightNorm))
                {
                    Vector3 skidPt = rightPt + vel * dt;
                    lastRightSkid = skidmarksController.AddSkidMark(skidPt, rightNorm, opacity, lastRightSkid, skidmarkWidth);
                }
                else
                {
                    lastRightSkid = -1;
                }

                lastSkidUpdateTime = Time.time;
            }
            else
            {
                lastLeftSkid = -1;
                lastRightSkid = -1;
            }
        }

        private bool ProbeWheelGround(Vector3 origin, out Vector3 pt, out Vector3 norm)
        {
            pt = Vector3.zero;
            norm = Vector3.up;
            Rigidbody b = car != null ? car.TargetBody : null;
            if (b != null && VehicleGroundProbe.Cast(b, origin + Vector3.up * 0.2f, 0.75f, out RaycastHit probeHit))
            {
                pt = probeHit.point;
                norm = probeHit.normal;
                return true;
            }
            if (Physics.Raycast(origin + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 0.75f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (b == null || hit.rigidbody != b)
                {
                    pt = hit.point;
                    norm = hit.normal;
                    return true;
                }
            }
            return false;
        }

        private void PlayDashBurst(int stage)
        {
            if (!enableDashBurst || dashBurstParticleSystems == null) return;
            Color color = GetStageColor(stage);
            float size = GetStageSize(stage);
            Vector3 left = rearLeftEmitter != null
                ? rearLeftEmitter.TransformPoint(rearLeftOffset) : transform.position;
            Vector3 right = rearRightEmitter != null
                ? rearRightEmitter.TransformPoint(rearRightOffset) : transform.position;
            Vector3 origin = (left + right) * 0.5f;
            int count = Mathf.Max(1, Mathf.RoundToInt(dashBurstParticleCount * particleBudgetMultiplier));

            for (int systemIndex = 0; systemIndex < dashBurstParticleSystems.Length; systemIndex++)
            {
                ParticleSystem system = dashBurstParticleSystems[systemIndex];
                if (system == null) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
                for (int i = 0; i < count; i++)
                {
                    float side = (i & 1) == 0 ? -1f : 1f;
                    var particle = new ParticleSystem.EmitParams
                    {
                        position = origin,
                        velocity = -transform.forward * UnityEngine.Random.Range(2.5f, 6f) +
                                   transform.right * (side * UnityEngine.Random.Range(0.5f, 2f)) +
                                   Vector3.up * UnityEngine.Random.Range(0.5f, 2.5f),
                        startColor = Color.Lerp(color, Color.white, UnityEngine.Random.value * 0.3f),
                        startSize = size * UnityEngine.Random.Range(0.8f, 1.35f)
                    };
                    system.Emit(particle, 1);
                }
            }
        }

        private void CacheLeanRestPose()
        {
            if (cachedLeanPivot == visualLeanPivot) return;
            RestoreLean(); // Restore an old pivot if reassigned during Play Mode.
            cachedLeanPivot = visualLeanPivot;
            leanPivotIsSafe = visualLeanPivot != null && visualLeanPivot != transform &&
                              visualLeanPivot.GetComponent<Rigidbody>() == null;
            if (!leanPivotIsSafe) return;
            originalLeanRotation = visualLeanPivot.localRotation;
        }

        private void LateUpdate()
        {
            CacheLeanRestPose();
            // Refuse to rotate the physics chassis even if assigned by mistake.
            if (!leanPivotIsSafe || (car != null && visualLeanPivot == car.transform)) return;

            // If KartVisualJuice is handling visual chassis lean & hop, yield to prevent jitter
            if (!ShouldManageSparks && cachedVisualJuice != null)
            {
                RestoreLean();
                return;
            }

            if (!enableBodyLean)
            {
                RestoreLean();
                return;
            }
            // This is VISUAL ONLY. Never assign the Rigidbody or the car root.
            float steer = car != null && isDrifting && isGrounded ? car.SteeringInput : 0f;
            Quaternion desired = originalLeanRotation *
                Quaternion.Euler(0f, 0f, -steer * maximumLeanDegrees);
            float t = 1f - Mathf.Exp(-leanResponsiveness * Time.deltaTime);
            visualLeanPivot.localRotation = Quaternion.Slerp(visualLeanPivot.localRotation, desired, t);
            leanWasApplied = true;
        }

        private void RestoreLean()
        {
            if (cachedLeanPivot != null && leanWasApplied)
                cachedLeanPivot.localRotation = originalLeanRotation;
            leanWasApplied = false;
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            color.a *= Mathf.Clamp01(opacity);
            return color;
        }

        private static void ConfigureDustLifetimeModules(ParticleSystem system)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.86f, 0.82f, 0.74f), 0.55f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.72f, 0.58f),
                    new GradientAlphaKey(0f, 1f)
                });
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.65f, 1f, 1.6f));
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f);
        }

        private static void ConfigureSparkLifetimeModules(ParticleSystem system)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.95f, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.12f));
        }

        private static void ApplyMaterial(ParticleSystem system, Material material)
        {
            if (system == null || material == null) return;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private void ConfigureRenderer(ParticleSystem system, bool sparks)
        {
            if (system == null) return;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;
            renderer.renderMode = sparks 
                ? (pinSparksToWheels ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch) 
                : ParticleSystemRenderMode.Billboard;
            if (sparks)
            {
                renderer.velocityScale = pinSparksToWheels ? 0f : 0.18f;
                renderer.lengthScale = pinSparksToWheels ? 1f : 2.2f;
            }
        }

        public void ResetForRace()
        {
            effectBlend = 0f;
            dustEffectBlend = 0f;
            driftDuration = 0f;
            wasValidDrift = false;
            dustWasEmitting = false;
            sparksWereEmitting = false;
            currentDriftStage = 0;
            appliedSparkStage = -1;
            ClearSystems(dustParticleSystems);
            if (ShouldManageSparks)
            {
                ClearSystems(sparkParticleSystems);
                ClearSystems(dashBurstParticleSystems);
                ApplySparkAppearance(0);
            }
            RestoreLean();
            lastLeftSkid = -1;
            lastRightSkid = -1;
        }

        private static void ClearSystems(ParticleSystem[] systems)
        {
            if (systems == null) return;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                var emission = systems[i].emission;
                emission.enabled = false;
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void PlaySystems(ParticleSystem[] systems)
        {
            if (systems == null) return;
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null && !systems[i].isPlaying) systems[i].Play(false);
        }

        private static void StopEmittingSystems(ParticleSystem[] systems)
        {
            if (systems == null) return;
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnDisable()
        {
            ClearSystems(dustParticleSystems);
            ClearSystems(sparkParticleSystems);
            ClearSystems(dashBurstParticleSystems);
            dustWasEmitting = false;
            sparksWereEmitting = false;
            wasValidDrift = false;
            RestoreLean();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimePrefabInstances.Count; i++)
                if (runtimePrefabInstances[i] != null) Destroy(runtimePrefabInstances[i]);
        }
    }
}
