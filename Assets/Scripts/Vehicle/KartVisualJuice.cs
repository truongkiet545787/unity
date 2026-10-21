using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;
using Cinemachine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Arcade visual feedback & juice controller for PoseKart vehicles.
    /// Ported and modernized from arcade KartController reference:
    /// - Model & wheel animations (steering wheel, wheel steering, wheel rolling, dynamic body lean)
    /// - 3-tier drift turbo particle system with tier colors, flash bursts, and Cinemachine camera impulse
    /// - DOTween juice: drift start hop (DOPunchPosition), soft return on boost (DOLocalRotate)
    /// - Boost release: exhaust tube flame particles and URP Chromatic Aberration wind-tear flash.
    /// 
    /// STRICT RULE: Modifies ONLY visual models and effects. Never alters Rigidbody physics,
    /// Ground Probe, or ICarInputProvider on CarController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    public class KartVisualJuice : MonoBehaviour
    {
        [Header("Vehicle References")]
        [SerializeField] private CarController car;
        [SerializeField] private Rigidbody body;

        [Header("Model Parts")]
        [Tooltip("Main visual chassis/model to tilt, hop and shake. Auto-resolved if null.")]
        [SerializeField] private Transform kartModel;
        [Tooltip("Front wheel transform (or axle) that turns with steering.")]
        [SerializeField] private Transform frontWheels;
        [Tooltip("Rear wheel transform (or axle) that rolls with speed.")]
        [SerializeField] private Transform backWheels;
        [Tooltip("Steering wheel inside the cockpit.")]
        [SerializeField] private Transform steeringWheel;

        [Header("Model Animation Settings")]
        [SerializeField, Range(5f, 45f)] private float frontWheelsMaxSteerAngle = 24f;
        [SerializeField, Range(15f, 90f)] private float steeringWheelMaxAngle = 55f;
        [SerializeField, Min(0.05f)] private float wheelRadius = 0.38f;
        [SerializeField, Range(0f, 25f)] private float normalSteerLeanAngle = 7f;
        [SerializeField, Range(0f, 25f)] private float normalSteerYawAngle = 5f;
        [SerializeField, Range(0f, 35f)] private float driftYawAngle = 18f;
        [SerializeField, Range(0f, 25f)] private float driftLeanAngle = 10f;
        [SerializeField, Range(0f, 20f)] private float counterSteerYawAngle = 8f;
        [SerializeField, Min(0.1f)] private float leanSmoothSpeed = 10f;

        [Header("DOTween Hop & Juice")]
        [Tooltip("Vertical hop height when initiating a drift.")]
        [SerializeField, Min(0f)] private float hopPunchHeight = 0.22f;
        [SerializeField, Min(0.05f)] private float hopDuration = 0.28f;
        [SerializeField, Range(1, 10)] private int hopVibrato = 5;
        [SerializeField, Range(0f, 1f)] private float hopElasticity = 0.8f;
        [SerializeField, Min(0.1f)] private float modelReturnDuration = 0.45f;

        [Header("3-Tier Drift Turbo Particles")]
        [Tooltip("Cố định hạt tia lửa bám sát vào bánh xe (SimulationSpace.Local), di chuyển theo xe chứ không bị tụt lại kéo thành vệt dài phía sau.")]
        [SerializeField] private bool pinParticlesToWheels = true;
        [SerializeField] private Transform wheelParticles;
        [SerializeField] private Transform flashParticles;
        [SerializeField] private List<ParticleSystem> primaryParticles = new List<ParticleSystem>();
        [SerializeField] private List<ParticleSystem> secondaryParticles = new List<ParticleSystem>();
        [Tooltip("Initial spark color immediately visible when drift begins (before Tier 1).")]
        [SerializeField] private Color baseDriftColor = new Color(1f, 0.92f, 0.65f, 0.9f);
        [SerializeField] private Color[] turboColors = new Color[]
        {
            new Color(0.25f, 0.75f, 1.0f, 1f), // Tier 1: Electric Blue / Cyan
            new Color(1.0f, 0.65f, 0.12f, 1f), // Tier 2: Blaze Orange / Gold
            new Color(0.88f, 0.22f, 1.0f, 1f)  // Tier 3: Neon Purple / Ultra
        };
        [SerializeField, Min(1f)] private float tier1Threshold = 15f;
        [SerializeField, Min(5f)] private float tier2Threshold = 45f;
        [SerializeField, Min(10f)] private float tier3Threshold = 85f;
        [SerializeField, Min(1f)] private float driftChargeRate = 40f;

        [Header("Exhaust Flame Boost (Tube001 / Tube002)")]
        [SerializeField] private ParticleSystem[] exhaustParticles;
        [SerializeField] private bool applyCarSpeedBoost = true;
        [SerializeField, Min(0.1f)] private float boostDurationPerTier = 0.45f;
        [SerializeField, Min(0.5f)] private float boostImpulsePerTier = 3.5f;
        [SerializeField] private bool awardNitroReward = true;
        [SerializeField, Range(0f, 0.2f)] private float nitroRewardFractionPerTier = 0.08f;

        [Header("URP Post Processing (Chromatic Aberration)")]
        [SerializeField] private Volume postVolume;
        [SerializeField, Range(0f, 1f)] private float maxChromaticAberration = 0.85f;
        [SerializeField, Min(0.1f)] private float chromaticEffectDuration = 0.5f;

        [Header("Camera Shake & Impulse")]
        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private bool enableFallbackCameraShake = true;
        [SerializeField, Range(0.05f, 1f)] private float baseImpulseStrength = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLog = true;

        // Runtime State
        private Quaternion baseModelRotation = Quaternion.identity;
        private Quaternion baseFrontWheelsRotation = Quaternion.identity;
        private Quaternion baseBackWheelsRotation = Quaternion.identity;
        private Quaternion baseSteeringWheelRotation = Quaternion.identity;
        private Vector3 baseModelLocalPosition = Vector3.zero;

        private float wheelSpinAngle;
        private bool wasDrifting;
        private int driftDirection = 1;
        private float driftPower;
        private int currentDriftTier;
        private bool tier1Reached, tier2Reached, tier3Reached;

        private ChromaticAberration chromaticAberration;
        private Tween chromaticTween;

        public int CurrentDriftTier => currentDriftTier;
        public float DriftPower => driftPower;

        protected void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null && car != null) body = car.TargetBody;

            ResolveModelParts();
            CacheBaseRotations();
            ResolveParticles();
            ResolveExhaustTubes();
            ResolvePostProcessing();
            ResolveCinemachineImpulse();
        }

        private void ResolveModelParts()
        {
            if (kartModel == null)
            {
                // Look for common model names
                Transform found = transform.Find("Visual");
                if (found == null) found = transform.Find("Model");
                if (found == null) found = transform.Find("kartModel");
                if (found == null) found = transform.Find("StuntPivot");
                if (found == null)
                {
                    Renderer r = GetComponentInChildren<Renderer>();
                    if (r != null && r.transform != transform) found = r.transform;
                }
                kartModel = found != null ? found : transform;
            }

            if (frontWheels == null)
            {
                frontWheels = FindChildByName(transform, "frontwheel");
                if (frontWheels == null) frontWheels = FindChildByName(transform, "front_wheel");
            }

            if (backWheels == null)
            {
                backWheels = FindChildByName(transform, "backwheel");
                if (backWheels == null) backWheels = FindChildByName(transform, "rear_wheel");
            }

            if (steeringWheel == null)
            {
                steeringWheel = FindChildByName(transform, "steer");
            }
        }

        private Transform FindChildByName(Transform root, string part)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
                Transform sub = FindChildByName(c, part);
                if (sub != null) return sub;
            }
            return null;
        }

        private void CacheBaseRotations()
        {
            if (kartModel != null)
            {
                baseModelRotation = kartModel.localRotation;
                baseModelLocalPosition = kartModel.localPosition;
            }
            if (frontWheels != null) baseFrontWheelsRotation = frontWheels.localRotation;
            if (backWheels != null) baseBackWheelsRotation = backWheels.localRotation;
            if (steeringWheel != null) baseSteeringWheelRotation = steeringWheel.localRotation;
        }

        private void ResolveParticles()
        {
            if (wheelParticles == null)
            {
                Transform found = transform.Find("DriftVFX");
                if (found == null) found = transform.Find("WheelParticles");
                if (found == null) found = transform.Find("Particles");
                if (found != null) wheelParticles = found;
            }

            if (wheelParticles != null && primaryParticles.Count == 0)
            {
                foreach (ParticleSystem ps in wheelParticles.GetComponentsInChildren<ParticleSystem>(true))
                {
                    string lower = ps.name.ToLowerInvariant();
                    // Do not treat tire dust / smoke as turbo sparks
                    if (lower.Contains("dust") || lower.Contains("smoke")) continue;
                    primaryParticles.Add(ps);
                }
            }

            if (primaryParticles.Count == 0)
            {
                // Fallback: search for spark particle systems anywhere on the kart
                foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    string lower = ps.name.ToLowerInvariant();
                    if (lower.Contains("spark") && !primaryParticles.Contains(ps))
                    {
                        primaryParticles.Add(ps);
                    }
                }
            }

            if (flashParticles != null && secondaryParticles.Count == 0)
            {
                foreach (ParticleSystem ps in flashParticles.GetComponentsInChildren<ParticleSystem>(true))
                {
                    secondaryParticles.Add(ps);
                }
            }

            if (secondaryParticles.Count == 0)
            {
                // Auto-detect tier upgrade flash or dash burst particles
                foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    string lower = ps.name.ToLowerInvariant();
                    if ((lower.Contains("flash") || lower.Contains("dashburst") || lower.Contains("burst"))
                        && !primaryParticles.Contains(ps)
                        && !secondaryParticles.Contains(ps))
                    {
                        secondaryParticles.Add(ps);
                    }
                }
            }

            if (pinParticlesToWheels)
            {
                for (int i = 0; i < primaryParticles.Count; i++)
                    EnforceLocalSimulation(primaryParticles[i]);
                for (int i = 0; i < secondaryParticles.Count; i++)
                    EnforceLocalSimulation(secondaryParticles[i]);
            }
        }

        private void EnforceLocalSimulation(ParticleSystem ps)
        {
            if (ps == null || !pinParticlesToWheels) return;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Stretch)
            {
                // Disable stretching with vehicle speed to avoid long streak lines
                renderer.velocityScale = 0f;
            }
        }

        private void ResolveExhaustTubes()
        {
            if (exhaustParticles == null || exhaustParticles.Length == 0)
            {
                var list = new List<ParticleSystem>();
                if (kartModel != null)
                {
                    Transform tube1 = kartModel.Find("Tube001");
                    if (tube1 != null) list.AddRange(tube1.GetComponentsInChildren<ParticleSystem>(true));

                    Transform tube2 = kartModel.Find("Tube002");
                    if (tube2 != null) list.AddRange(tube2.GetComponentsInChildren<ParticleSystem>(true));

                    if (list.Count == 0)
                    {
                        foreach (ParticleSystem ps in kartModel.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            string lower = ps.name.ToLowerInvariant();
                            if (lower.Contains("tube") || lower.Contains("exhaust") || lower.Contains("muffler") || lower.Contains("flame"))
                            {
                                list.Add(ps);
                            }
                        }
                    }
                }

                if (list.Count > 0)
                {
                    exhaustParticles = list.ToArray();
                }
            }
        }

        private void ResolvePostProcessing()
        {
            if (postVolume == null)
            {
                postVolume = FindFirstObjectByType<Volume>();
                if (postVolume == null)
                {
                    GameObject volObj = new GameObject("Kart_PostProcess_Volume");
                    postVolume = volObj.AddComponent<Volume>();
                    postVolume.isGlobal = true;
                    postVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                }
            }

            if (postVolume != null && postVolume.profile != null)
            {
                if (!postVolume.profile.TryGet(out chromaticAberration))
                {
                    chromaticAberration = postVolume.profile.Add<ChromaticAberration>(true);
                    chromaticAberration.intensity.overrideState = true;
                    chromaticAberration.intensity.value = 0f;
                }
            }
        }

        private void ResolveCinemachineImpulse()
        {
            if (impulseSource == null)
            {
                impulseSource = GetComponentInChildren<CinemachineImpulseSource>();
                if (impulseSource == null)
                {
                    impulseSource = FindFirstObjectByType<CinemachineImpulseSource>();
                }
            }
        }

        protected void Update()
        {
            if (car == null) return;

            float steerInput = car.SteeringInput;
            float forwardSpeed = car.ForwardSpeed;
            bool isGrounded = car.IsGrounded;

            // Robust drift detection: matches CarController drift state OR responsive player drift input
            bool isDrifting = car.IsDrifting || (isGrounded && forwardSpeed > 2.5f && IsDriftInputHeld() && Mathf.Abs(steerInput) > 0.1f);

            // Handle Drift State Transitions
            if (isDrifting && !wasDrifting && isGrounded)
            {
                StartDriftHop(steerInput);
            }
            else if (!isDrifting && wasDrifting)
            {
                ReleaseDriftBoost();
            }

            // Continuous Drift Power & Tier Updates
            if (isDrifting && isGrounded)
            {
                UpdateDriftPower(steerInput);
            }

            // Model & Wheel Animations
            UpdateWheelAnimations(steerInput, forwardSpeed);
            UpdateKartModelLean(isDrifting, steerInput);

            wasDrifting = isDrifting;
        }

        private bool IsDriftInputHeld()
        {
            try
            {
                if (UnityEngine.Input.GetKey(KeyCode.Space) ||
                    UnityEngine.Input.GetKey(KeyCode.LeftShift) ||
                    UnityEngine.Input.GetButton("Jump"))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void StartDriftHop(float steerInput)
        {
            driftDirection = steerInput >= 0f ? 1 : -1;
            driftPower = 0f;
            currentDriftTier = 0;
            tier1Reached = false;
            tier2Reached = false;
            tier3Reached = false;

            if (showDebugLog)
            {
                Debug.Log($"[KartVisualJuice] Drift HOP started! Direction: {driftDirection}, Steering: {steerInput:F2}");
            }

            // DOTween hop punch (Juice)
            if (kartModel != null)
            {
                kartModel.DOComplete();
                kartModel.DOPunchPosition(Vector3.up * hopPunchHeight, hopDuration, hopVibrato, hopElasticity);
            }

            // Immediately start primary particles with visible base spark color (NOT invisible!)
            for (int i = 0; i < primaryParticles.Count; i++)
            {
                ParticleSystem ps = primaryParticles[i];
                if (ps == null) continue;
                EnforceLocalSimulation(ps);
                var main = ps.main;
                main.startColor = baseDriftColor;
                var emission = ps.emission;
                emission.enabled = true;
                if (!ps.isPlaying) ps.Play();
            }
        }

        private void UpdateDriftPower(float steerInput)
        {
            // Turning harder into the drift charges faster; counter-steering charges slower
            float steerFactor = (driftDirection == 1)
                ? Mathf.Lerp(0.35f, 1.35f, (steerInput + 1f) * 0.5f)
                : Mathf.Lerp(1.35f, 0.35f, (steerInput + 1f) * 0.5f);

            driftPower += steerFactor * driftChargeRate * Time.deltaTime;

            Color activeColor = baseDriftColor;

            if (driftPower >= tier3Threshold && !tier3Reached)
            {
                tier3Reached = true;
                currentDriftTier = 3;
                activeColor = GetTurboColor(2);
                PlayTierUpgradeEffect(activeColor, 3);
            }
            else if (driftPower >= tier2Threshold && !tier2Reached)
            {
                tier2Reached = true;
                currentDriftTier = 2;
                activeColor = GetTurboColor(1);
                PlayTierUpgradeEffect(activeColor, 2);
            }
            else if (driftPower >= tier1Threshold && !tier1Reached)
            {
                tier1Reached = true;
                currentDriftTier = 1;
                activeColor = GetTurboColor(0);
                PlayTierUpgradeEffect(activeColor, 1);
            }
            else
            {
                if (currentDriftTier > 0)
                {
                    activeColor = GetTurboColor(currentDriftTier - 1);
                }
            }

            // Continuously update color on primary wheel particles
            for (int i = 0; i < primaryParticles.Count; i++)
            {
                ParticleSystem ps = primaryParticles[i];
                if (ps == null) continue;
                var main = ps.main;
                main.startColor = activeColor;
            }
        }

        private Color GetTurboColor(int index)
        {
            if (turboColors != null && index >= 0 && index < turboColors.Length)
                return turboColors[index];
            return Color.cyan;
        }

        private void PlayTierUpgradeEffect(Color tierColor, int tier)
        {
            if (showDebugLog)
            {
                Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(tierColor)}>[KartVisualJuice] ★ DRIFT TIER {tier} REACHED! (Power: {driftPower:F1})</color>");
            }

            // 1. Cinemachine Impulse & Camera Shake
            TriggerCameraImpulse(tier);

            // 2. Secondary Flash Particles Burst
            for (int i = 0; i < secondaryParticles.Count; i++)
            {
                ParticleSystem ps = secondaryParticles[i];
                if (ps == null) continue;
                EnforceLocalSimulation(ps);
                var main = ps.main;
                main.startColor = tierColor;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
                ps.Emit(16);
            }
        }

        private void TriggerCameraImpulse(int tier)
        {
            float intensity = baseImpulseStrength * tier;

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulse(intensity);
            }
            else if (enableFallbackCameraShake && UnityEngine.Camera.main != null)
            {
                UnityEngine.Camera.main.transform.DOComplete();
                UnityEngine.Camera.main.transform.DOShakePosition(0.18f, 0.10f * tier, 14, 90f, false, true);
            }
        }

        private void ReleaseDriftBoost()
        {
            if (currentDriftTier > 0)
            {
                if (showDebugLog)
                {
                    Debug.Log($"<color=#00FF88>[KartVisualJuice] 🚀 DRIFT BOOST RELEASED! Tier: {currentDriftTier}</color>");
                }

                // 1. Exhaust flame particles (Tube001 / Tube002)
                if (exhaustParticles != null)
                {
                    for (int i = 0; i < exhaustParticles.Length; i++)
                    {
                        if (exhaustParticles[i] != null)
                        {
                            exhaustParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            exhaustParticles[i].Play(true);
                            exhaustParticles[i].Emit(24);
                        }
                    }
                }

                // 2. URP Chromatic Aberration Wind Flash
                TriggerChromaticAberrationFlash();

                // 3. Apply Speed Boost to CarController
                if (applyCarSpeedBoost && car != null)
                {
                    float duration = boostDurationPerTier * currentDriftTier;
                    float impulse = boostImpulsePerTier * currentDriftTier;
                    car.ApplySpeedBoost(duration, impulse);
                }

                // 4. Award Nitro
                if (awardNitroReward && car != null)
                {
                    float nitroAmount = car.MaxNitroChargeAmount * (nitroRewardFractionPerTier * currentDriftTier);
                    car.AddNitro(nitroAmount);
                }

                // 5. DOTween Soft Model Return
                if (kartModel != null)
                {
                    kartModel.DOComplete();
                    kartModel.DOLocalRotate(baseModelRotation.eulerAngles, modelReturnDuration).SetEase(Ease.OutBack);
                }
            }

            // Reset particles and state
            for (int i = 0; i < primaryParticles.Count; i++)
            {
                ParticleSystem ps = primaryParticles[i];
                if (ps == null) continue;
                var emission = ps.emission;
                emission.enabled = false;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            driftPower = 0f;
            currentDriftTier = 0;
            tier1Reached = false;
            tier2Reached = false;
            tier3Reached = false;
        }

        private void TriggerChromaticAberrationFlash()
        {
            if (chromaticAberration == null && postVolume != null && postVolume.profile != null)
            {
                postVolume.profile.TryGet(out chromaticAberration);
            }

            if (chromaticAberration != null)
            {
                chromaticAberration.intensity.overrideState = true;
                if (chromaticTween != null && chromaticTween.IsActive()) chromaticTween.Kill();

                float half = chromaticEffectDuration * 0.5f;
                chromaticTween = DOVirtual.Float(0f, maxChromaticAberration, half, v => chromaticAberration.intensity.value = v)
                    .OnComplete(() =>
                    {
                        chromaticTween = DOVirtual.Float(maxChromaticAberration, 0f, half, v => chromaticAberration.intensity.value = v);
                    });
            }
        }

        private void UpdateWheelAnimations(float steerInput, float forwardSpeed)
        {
            // Spin wheels around X pitch axis based on vehicle speed
            float rollDelta = (forwardSpeed / Mathf.Max(0.05f, wheelRadius)) * Mathf.Rad2Deg * Time.deltaTime;
            wheelSpinAngle = (wheelSpinAngle + rollDelta) % 360f;

            // Front wheels: Steer yaw + roll spin
            if (frontWheels != null)
            {
                float steerAngle = steerInput * frontWheelsMaxSteerAngle;
                Quaternion steerRot = Quaternion.Euler(0f, steerAngle, 0f);
                Quaternion spinRot = Quaternion.Euler(wheelSpinAngle, 0f, 0f);
                frontWheels.localRotation = baseFrontWheelsRotation * steerRot * spinRot;
            }

            // Back wheels: Roll spin only
            if (backWheels != null)
            {
                Quaternion spinRot = Quaternion.Euler(wheelSpinAngle, 0f, 0f);
                backWheels.localRotation = baseBackWheelsRotation * spinRot;
            }

            // Steering wheel: Turn cockpit wheel with steering input
            if (steeringWheel != null)
            {
                Quaternion steerRot = Quaternion.Euler(0f, 0f, -steerInput * steeringWheelMaxAngle);
                steeringWheel.localRotation = baseSteeringWheelRotation * steerRot;
            }
        }

        private void UpdateKartModelLean(bool isDrifting, float steerInput)
        {
            if (kartModel == null) return;

            Quaternion targetRotation;

            if (!isDrifting)
            {
                // Normal driving: gentle lean into corners
                float targetRoll = -steerInput * normalSteerLeanAngle;
                float targetYaw = steerInput * normalSteerYawAngle;
                targetRotation = baseModelRotation * Quaternion.Euler(0f, targetYaw, targetRoll);
            }
            else
            {
                // Drift stance: kart body angles into the drift with counter-steering responsiveness
                float driftRoll = -driftDirection * driftLeanAngle;
                float driftYaw = (driftDirection * driftYawAngle) + (steerInput * counterSteerYawAngle);
                targetRotation = baseModelRotation * Quaternion.Euler(0f, driftYaw, driftRoll);
            }

            kartModel.localRotation = Quaternion.Slerp(kartModel.localRotation, targetRotation, Time.deltaTime * leanSmoothSpeed);
        }

        protected void OnDisable()
        {
            if (kartModel != null)
            {
                kartModel.DOKill();
                kartModel.localRotation = baseModelRotation;
                kartModel.localPosition = baseModelLocalPosition;
            }

            if (chromaticTween != null && chromaticTween.IsActive())
            {
                chromaticTween.Kill();
            }

            if (chromaticAberration != null)
            {
                chromaticAberration.intensity.value = 0f;
            }
        }
    }
}
