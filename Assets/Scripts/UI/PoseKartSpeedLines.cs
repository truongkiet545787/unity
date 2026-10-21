using System;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.UI
{
    /// <summary>
    /// Camera-anchored arcade speed lines. Visual only: does not change vehicle
    /// speed, physics, input, Nitro, AI takeover, or camera transform.
    /// Assign one continuously looping ParticleSystem prefab per speed tier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartSpeedLines : MonoBehaviour
    {
        [Serializable]
        private sealed class SpeedTier
        {
            [Min(0f)] public float minSpeedKmh;
            [Tooltip("A looping speed-lines ParticleSystem prefab (may contain child systems).")]
#pragma warning disable 0649
            public GameObject particlePrefab;
#pragma warning restore 0649
        }

        [Header("References")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private CarController playerCar;
        [Tooltip("Prefer an empty child of Main Camera. Prefab local offsets are preserved.")]
        [SerializeField] private Transform effectAnchor;

        [Header("Speed Tiers (km/h)")]
        [Tooltip("Highest eligible minimum speed wins. Unassigned tiers are skipped. " +
                 "If this component already exists in a scene, set its Inspector thresholds manually.")]
        [SerializeField]
        private SpeedTier[] speedTiers =
        {
            new SpeedTier { minSpeedKmh = 50f },
            new SpeedTier { minSpeedKmh = 90f },
            new SpeedTier { minSpeedKmh = 125f },
            new SpeedTier { minSpeedKmh = 155f }
        };

        [Header("Arcade Feel")]
        [Tooltip("Use horizontal Rigidbody speed so sideways drift still looks fast. Reverse motion stays hidden.")]
        [SerializeField] private bool usePlanarRigidbodySpeed = true;
        [Tooltip("Smooth the speed reading before selecting a tier (higher = faster response).")]
        [SerializeField, Min(0.1f)] private float speedResponsiveness = 13f;
        [Tooltip("Extra speed required to go UP a tier; same margin below threshold to go DOWN.")]
        [SerializeField, Min(0f)] private float speedHysteresisKmh = 3f;
        [Tooltip("Speed lines fade away during the finish cinematic even if the AI keeps driving.")]
        [SerializeField] private bool hideAfterPlayerFinish = true;

        [Header("VFX Blend / Performance")]
        [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.16f;
        [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.24f;
        [SerializeField, Range(0f, 2f)] private float masterEmissionIntensity = 0.85f;
        [Tooltip("Particles stay attached to the camera instead of leaving world-space trails.")]
        [SerializeField] private bool forceLocalSimulation = true;
        [Tooltip("Caps each prefab particle system; a lower number reduces overdraw on slower GPUs.")]
        [SerializeField, Min(16)] private int maxParticlesPerSystem = 160;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private float rawSpeedKmh;
        [SerializeField] private float currentSpeedKmh;
        [SerializeField] private int activeTierIndex = -1;

        // Kept for compatibility with existing serialized scene data.
        private GameObject[] spawnedEffects;
        private TierRuntime[] runtimeTiers;
        private float nextReferenceRetryAt;

        private sealed class TierRuntime
        {
            public readonly GameObject effect;
            public readonly ParticleSystem[] particles;
            public readonly float[] timeRates;
            public readonly float[] distanceRates;
            public float blend;
            public bool emitting;

            public TierRuntime(GameObject effect, ParticleSystem[] particles)
            {
                this.effect = effect;
                this.particles = particles;
                timeRates = new float[particles.Length];
                distanceRates = new float[particles.Length];
            }
        }

        private void Awake()
        {
            if (effectAnchor == null)
                effectAnchor = transform;

            TryResolveReferences();
            CreateEffects();
        }

        private void Update()
        {
            // Only retry scene-wide lookup once a second if the reference is missing.
            // A manually assigned RaceManager / Player Car avoids that lookup entirely.
            if ((raceManager == null || playerCar == null) &&
                Time.unscaledTime >= nextReferenceRetryAt)
            {
                TryResolveReferences();
            }

            rawSpeedKmh = ReadForwardVisualSpeedKmh();
            float dt = Mathf.Max(0f, Time.deltaTime);
            float speedAlpha = 1f - Mathf.Exp(-speedResponsiveness * dt);
            currentSpeedKmh = Mathf.Lerp(currentSpeedKmh, rawSpeedKmh, speedAlpha);
            if (Mathf.Abs(currentSpeedKmh - rawSpeedKmh) < 0.02f)
                currentSpeedKmh = rawSpeedKmh;

            bool suppressForFinish = hideAfterPlayerFinish &&
                                     raceManager != null && raceManager.PlayerFinished;
            int desiredTier = suppressForFinish ? -1 : FindTier(currentSpeedKmh);
            desiredTier = ApplyHysteresis(desiredTier, currentSpeedKmh);

            // Finish suppression overrides the hysteresis: don't retain lines
            // over the results screen just because the car is still moving.
            if (suppressForFinish)
                desiredTier = -1;

            activeTierIndex = desiredTier;
            UpdateEffects(dt);
        }

        private void TryResolveReferences()
        {
            nextReferenceRetryAt = Time.unscaledTime + 1f;
            if (raceManager == null)
                raceManager = FindFirstObjectByType<RaceManager>();
            if (playerCar == null && raceManager != null)
                playerCar = raceManager.PlayerController;
            if (playerCar == null)
                playerCar = GetComponentInParent<CarController>();
        }

        private float ReadForwardVisualSpeedKmh()
        {
            if (playerCar == null)
                return 0f;

            // Don't show a forward speed tunnel while reversing or stationary.
            float forward = Mathf.Max(0f, playerCar.ForwardSpeed);
            if (forward <= 0.5f)
                return 0f;

            Rigidbody body = playerCar.TargetBody;
            if (!usePlanarRigidbodySpeed || body == null)
                return forward * 3.6f;

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f; // Jump/fall velocity must not trigger the highest tier.
            return velocity.magnitude * 3.6f;
        }

        private void CreateEffects()
        {
            int count = speedTiers != null ? speedTiers.Length : 0;
            spawnedEffects = new GameObject[count];
            runtimeTiers = new TierRuntime[count];

            for (int i = 0; i < count; i++)
            {
                SpeedTier tier = speedTiers[i];
                if (tier == null || tier.particlePrefab == null)
                    continue;

                GameObject instance = Instantiate(tier.particlePrefab, effectAnchor, false);
                instance.name = $"SpeedLines_{tier.minSpeedKmh:0}_Kmh";
                instance.SetActive(false);

                ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                if (particles.Length == 0)
                {
                    Debug.LogWarning($"[PoseKartSpeedLines] {tier.particlePrefab.name} has no ParticleSystem.", this);
                    Destroy(instance);
                    continue;
                }

                TierRuntime runtime = new TierRuntime(instance, particles);
                for (int p = 0; p < particles.Length; p++)
                {
                    ParticleSystem particle = particles[p];
                    var main = particle.main;
                    main.playOnAwake = false;
                    if (forceLocalSimulation)
                        main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    if (main.maxParticles > maxParticlesPerSystem)
                        main.maxParticles = maxParticlesPerSystem;

                    var emission = particle.emission;
                    runtime.timeRates[p] = emission.rateOverTimeMultiplier;
                    runtime.distanceRates[p] = emission.rateOverDistanceMultiplier;
                    emission.rateOverTimeMultiplier = 0f;
                    emission.rateOverDistanceMultiplier = 0f;
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                spawnedEffects[i] = instance;
                runtimeTiers[i] = runtime;
            }
        }

        private int FindTier(float speedKmh)
        {
            if (speedTiers == null || runtimeTiers == null)
                return -1;

            int result = -1;
            float highest = float.NegativeInfinity;
            for (int i = 0; i < speedTiers.Length && i < runtimeTiers.Length; i++)
            {
                SpeedTier tier = speedTiers[i];
                if (tier == null || runtimeTiers[i] == null)
                    continue; // An empty prefab slot must never produce a blank tier.
                if (speedKmh >= tier.minSpeedKmh && tier.minSpeedKmh >= highest)
                {
                    highest = tier.minSpeedKmh;
                    result = i;
                }
            }
            return result;
        }

        private int ApplyHysteresis(int desired, float speedKmh)
        {
            int current = activeTierIndex;
            if (current < 0 || current >= speedTiers.Length ||
                current >= runtimeTiers.Length || runtimeTiers[current] == null || desired == current)
                return desired;

            float currentThreshold = speedTiers[current].minSpeedKmh;
            if (desired < 0)
                return speedKmh > currentThreshold - speedHysteresisKmh ? current : -1;

            float nextThreshold = speedTiers[desired].minSpeedKmh;
            if (nextThreshold > currentThreshold &&
                speedKmh < nextThreshold + speedHysteresisKmh)
                return current;
            if (nextThreshold < currentThreshold &&
                speedKmh > currentThreshold - speedHysteresisKmh)
                return current;
            return desired;
        }

        private void UpdateEffects(float dt)
        {
            if (runtimeTiers == null)
                return;

            float riseAlpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, fadeInSeconds));
            float fallAlpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, fadeOutSeconds));

            for (int i = 0; i < runtimeTiers.Length; i++)
            {
                TierRuntime runtime = runtimeTiers[i];
                if (runtime == null)
                    continue;

                float target = 0f;
                if (i == activeTierIndex)
                {
                    // Within each tier, gradually increase the density as speed rises.
                    float overThreshold = Mathf.Max(0f, currentSpeedKmh - speedTiers[i].minSpeedKmh);
                    target = 0.65f + 0.35f * Mathf.Clamp01(overThreshold / 30f);
                }

                // Inactive tiers have zero work until selected again.
                if (target <= 0f && runtime.blend <= 0f && !runtime.effect.activeSelf)
                    continue;

                if (target > 0f && !runtime.emitting)
                    BeginEmission(runtime);

                runtime.blend = Mathf.Lerp(runtime.blend, target,
                    target > runtime.blend ? riseAlpha : fallAlpha);
                if (Mathf.Abs(runtime.blend - target) < 0.005f)
                    runtime.blend = target;

                if (runtime.effect.activeSelf)
                    SetEmissionWeight(runtime, runtime.blend * masterEmissionIntensity);

                if (target <= 0f && runtime.blend <= 0.005f)
                {
                    if (runtime.emitting)
                        EndEmission(runtime);
                    if (runtime.effect.activeSelf && !AnyParticlesAlive(runtime))
                        runtime.effect.SetActive(false);
                }
            }
        }

        private static void BeginEmission(TierRuntime runtime)
        {
            // Reuse the same prefab instance. Never Instantiate on a tier change.
            bool wasInactive = !runtime.effect.activeSelf;
            if (wasInactive)
                runtime.effect.SetActive(true);

            for (int p = 0; p < runtime.particles.Length; p++)
            {
                ParticleSystem particle = runtime.particles[p];
                if (particle == null || !particle.gameObject.activeInHierarchy)
                    continue;
                if (wasInactive)
                    particle.Clear(false);
                particle.Play(false);
            }
            runtime.emitting = true;
        }

        private static void EndEmission(TierRuntime runtime)
        {
            runtime.emitting = false;
            for (int p = 0; p < runtime.particles.Length; p++)
            {
                ParticleSystem particle = runtime.particles[p];
                if (particle != null)
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static bool AnyParticlesAlive(TierRuntime runtime)
        {
            for (int p = 0; p < runtime.particles.Length; p++)
            {
                ParticleSystem particle = runtime.particles[p];
                if (particle != null && particle.IsAlive(false))
                    return true;
            }
            return false;
        }

        private static void SetEmissionWeight(TierRuntime runtime, float weight)
        {
            for (int p = 0; p < runtime.particles.Length; p++)
            {
                ParticleSystem particle = runtime.particles[p];
                if (particle == null)
                    continue;
                var emission = particle.emission;
                emission.rateOverTimeMultiplier = runtime.timeRates[p] * weight;
                emission.rateOverDistanceMultiplier = runtime.distanceRates[p] * weight;
            }
        }

        private void OnDisable()
        {
            activeTierIndex = -1;
            rawSpeedKmh = 0f;
            currentSpeedKmh = 0f;
            if (runtimeTiers == null)
                return;

            for (int i = 0; i < runtimeTiers.Length; i++)
            {
                TierRuntime runtime = runtimeTiers[i];
                if (runtime == null)
                    continue;
                runtime.blend = 0f;
                runtime.emitting = false;
                SetEmissionWeight(runtime, 0f);
                for (int p = 0; p < runtime.particles.Length; p++)
                {
                    ParticleSystem particle = runtime.particles[p];
                    if (particle != null)
                        particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (runtime.effect != null)
                    runtime.effect.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (spawnedEffects == null)
                return;
            for (int i = 0; i < spawnedEffects.Length; i++)
            {
                if (spawnedEffects[i] != null)
                    Destroy(spawnedEffects[i]);
            }
        }
    }
}
