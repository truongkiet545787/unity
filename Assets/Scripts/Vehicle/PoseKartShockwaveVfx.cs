using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Cosmetic Shockwave mode and impact presentation. The component owns its
    /// small set of line/particle/light objects for the lifetime of the kart;
    /// it never changes Rigidbody, Nitro, collision or network state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartShockwaveVfx : MonoBehaviour
    {
        [Header("Shockwave Mode")]
        [SerializeField] private bool enableModeVfx = true;
        [SerializeField] private Color shockwaveColor = new Color(0.86f, 0.12f, 1f, 1f);
        [SerializeField, Min(0.1f)] private float modeRingRadius = 1.35f;
        [SerializeField, Min(0.1f)] private float modeRingPulseSpeed = 7f;
        [SerializeField, Min(0.1f)] private float modeStreakLength = 1.35f;
        [SerializeField, Range(0f, 1f)] private float modeRingAlpha = 0.28f;
        [SerializeField, Range(0f, 1f)] private float modeStreakAlpha = 0.42f;
        [SerializeField, Min(0f)] private float modeGlowIntensity = 0.8f;

        [Header("Shockwave Impact")]
        [SerializeField, Min(0.05f)] private float impactDuration = 0.38f;
        [SerializeField, Min(0.1f)] private float impactRingRadius = 2.2f;
        [SerializeField, Min(0.01f)] private float impactRingWidth = 0.075f;
        [SerializeField, Range(1, 64)] private int impactParticleCount = 20;
        [SerializeField, Min(0.01f)] private float impactParticleLifetime = 0.28f;
        [SerializeField, Min(0f)] private float impactFlashIntensity = 3.5f;

        private CarController car;
        private GameObject modeRoot;
        private LineRenderer modeRing;
        private LineRenderer[] modeStreaks;
        private Light modeGlow;
        private LineRenderer impactRing;
        private Light impactFlash;
        private ParticleSystem impactParticles;
        private Material lineMaterial;
        private Material particleMaterial;
        private float impactUntil = float.NegativeInfinity;
        private float impactStrength;
        private Vector3 impactPoint;
        private Vector3 previousVehiclePosition;
        private bool hasPreviousVehiclePosition;
        private bool resetVfxSuppressed;
        private CarController.NitroMode resetSuppressionMode;
        private bool modeVfxActive;
        private bool impactVfxActive;

        private const int ModeRingSegments = 24;
        private const int ImpactRingSegments = 32;

        private void Awake()
        {
            car = GetComponent<CarController>() ??
                GetComponentInParent<CarController>();
            CreateVfxObjects();
            SetModeVfxActive(false);
            SetImpactVfxActive(false);
        }

        private void LateUpdate()
        {
            if (car == null)
                car = GetComponent<CarController>() ??
                    GetComponentInParent<CarController>();

            if (hasPreviousVehiclePosition &&
                (transform.position - previousVehiclePosition).sqrMagnitude > 16f)
            {
                resetVfxSuppressed = true;
                resetSuppressionMode = car != null
                    ? car.VisualNitroMode
                    : CarController.NitroMode.None;
            }
            previousVehiclePosition = transform.position;
            hasPreviousVehiclePosition = true;

            if (resetVfxSuppressed && car != null &&
                (!car.IsVisualNitroBoosting ||
                 car.VisualNitroMode != resetSuppressionMode))
            {
                resetVfxSuppressed = false;
            }

            bool shockwaveActive = enableModeVfx &&
                !resetVfxSuppressed &&
                car != null &&
                car.IsVisualNitroBoosting &&
                car.VisualNitroMode == CarController.NitroMode.Shockwave;

            SetModeVfxActive(shockwaveActive);
            if (shockwaveActive)
                UpdateModeVfx();

            UpdateImpactVfx();
        }

        /// <summary>
        /// Called by the authoritative Shockwave presentation RPC on every
        /// observing peer. The effect is point-accurate and cosmetic only.
        /// </summary>
        public void PlayImpact(Vector3 worldPoint, float strength)
        {
            if (!IsFinite(worldPoint))
                return;

            impactStrength = Mathf.Clamp01(strength);
            impactPoint = worldPoint;
            impactUntil = Time.time + Mathf.Max(0.05f, impactDuration);
            impactRing.transform.position = worldPoint;
            impactRing.transform.rotation = Quaternion.identity;
            impactFlash.transform.position = worldPoint;
            impactParticles.transform.position = worldPoint;
            SetImpactVfxActive(true);

            ParticleSystem.MainModule main = impactParticles.main;
            main.startColor = WithAlpha(shockwaveColor, 0.95f);
            impactParticles.Clear(true);
            impactParticles.Emit(BuildImpactEmitParams(worldPoint),
                Mathf.Max(1, Mathf.RoundToInt(
                    impactParticleCount * Mathf.Lerp(0.55f, 1f, impactStrength))));

        }

        private ParticleSystem.EmitParams BuildImpactEmitParams(Vector3 point)
        {
            return new ParticleSystem.EmitParams
            {
                position = point,
                velocity = Random.onUnitSphere * Mathf.Lerp(2.5f, 5.5f, impactStrength) +
                    Vector3.up * Mathf.Lerp(0.5f, 1.5f, impactStrength)
            };
        }

        private void CreateVfxObjects()
        {
            lineMaterial = CreateMaterial("PoseKart Shockwave Line");
            particleMaterial = CreateMaterial("PoseKart Shockwave Particle");

            modeRoot = new GameObject("Shockwave Mode VFX");
            modeRoot.transform.SetParent(transform, false);

            modeRing = CreateLine(
                "Shockwave Energy Ring",
                modeRoot.transform,
                ModeRingSegments,
                lineMaterial,
                0.04f);

            modeStreaks = new LineRenderer[4];
            for (int index = 0; index < modeStreaks.Length; index++)
            {
                modeStreaks[index] = CreateLine(
                    "Shockwave Energy Streak " + index,
                    modeRoot.transform,
                    3,
                    lineMaterial,
                    0.045f);
            }

            GameObject glowObject = new GameObject("Shockwave Mode Glow");
            glowObject.transform.SetParent(modeRoot.transform, false);
            modeGlow = glowObject.AddComponent<Light>();
            modeGlow.type = LightType.Point;
            modeGlow.range = 3.5f;
            modeGlow.shadows = LightShadows.None;

            GameObject impactRoot = new GameObject("Shockwave Impact VFX");
            impactRoot.transform.SetParent(transform, false);

            impactRing = CreateLine(
                "Shockwave Impact Ring",
                impactRoot.transform,
                ImpactRingSegments,
                lineMaterial,
                impactRingWidth);

            GameObject flashObject = new GameObject("Shockwave Impact Flash");
            flashObject.transform.SetParent(impactRoot.transform, false);
            impactFlash = flashObject.AddComponent<Light>();
            impactFlash.type = LightType.Point;
            impactFlash.range = 4f;
            impactFlash.shadows = LightShadows.None;

            GameObject particleObject = new GameObject("Shockwave Impact Particles");
            particleObject.transform.SetParent(impactRoot.transform, false);
            impactParticles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = impactParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = impactParticleLifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startSpeed = 0f;
            main.maxParticles = 64;
            ParticleSystem.EmissionModule emission = impactParticles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = impactParticles.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = impactParticles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMaterial;
        }

        private LineRenderer CreateLine(
            string objectName,
            Transform parent,
            int positionCount,
            Material sharedMaterial,
            float width)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = objectName.IndexOf("Ring", System.StringComparison.Ordinal) >= 0;
            line.positionCount = positionCount;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.widthMultiplier = width;
            line.sharedMaterial = sharedMaterial;
            return line;
        }

        private Material CreateMaterial(string label)
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                name = label
            };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", shockwaveColor);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", shockwaveColor);
            return material;
        }

        private void SetModeVfxActive(bool active)
        {
            if (modeVfxActive == active)
                return;

            modeVfxActive = active;
            if (modeRoot != null)
                modeRoot.SetActive(active);
            if (!active && modeGlow != null)
                modeGlow.intensity = 0f;
        }

        private void UpdateModeVfx()
        {
            float pulse = 0.5f + 0.5f *
                Mathf.Sin(Time.time * Mathf.Max(0.1f, modeRingPulseSpeed));
            float radius = modeRingRadius * Mathf.Lerp(0.92f, 1.08f, pulse);
            SetRing(modeRing, radius, 0.16f + pulse * 0.12f,
                WithAlpha(shockwaveColor, modeRingAlpha));

            float streakPulse = Mathf.Lerp(0.82f, 1.12f, pulse);
            for (int index = 0; index < modeStreaks.Length; index++)
            {
                float side = index % 2 == 0 ? -1f : 1f;
                float lane = index < 2 ? 0.43f : 0.72f;
                float offset = index * 1.7f;
                LineRenderer streak = modeStreaks[index];
                streak.startColor = WithAlpha(shockwaveColor, modeStreakAlpha);
                streak.endColor = WithAlpha(shockwaveColor, 0f);
                streak.SetPosition(0, new Vector3(side * lane, 0.28f, -0.35f));
                streak.SetPosition(1, new Vector3(
                    side * (lane + 0.08f * Mathf.Sin(Time.time * 10f + offset)),
                    0.31f,
                    -0.75f));
                streak.SetPosition(2, new Vector3(
                    side * (lane + 0.12f * Mathf.Sin(Time.time * 8f + offset)),
                    0.34f,
                    -modeStreakLength * streakPulse));
            }

            if (modeGlow != null)
            {
                modeGlow.color = shockwaveColor;
                modeGlow.intensity = modeGlowIntensity * Mathf.Lerp(0.7f, 1.15f, pulse);
            }
        }

        private void UpdateImpactVfx()
        {
            if (!impactVfxActive)
                return;

            // The reusable impact object stays owned by the kart, but the
            // visual must remain at the confirmed world-space contact point
            // while the vehicle continues moving.
            impactRing.transform.position = impactPoint;
            impactRing.transform.rotation = Quaternion.identity;
            impactFlash.transform.position = impactPoint;

            float remaining = Mathf.Clamp01(
                (impactUntil - Time.time) / Mathf.Max(0.05f, impactDuration));
            float progress = 1f - remaining;
            float radius = Mathf.Lerp(0.2f, impactRingRadius, progress);
            SetRing(
                impactRing,
                radius,
                Mathf.Lerp(0.85f, 0f, progress) * Mathf.Lerp(0.55f, 1f, impactStrength),
                WithAlpha(shockwaveColor, Mathf.Lerp(0.9f, 0f, progress)));

            if (impactFlash != null)
            {
                impactFlash.color = shockwaveColor;
                impactFlash.intensity = impactFlashIntensity * remaining *
                    Mathf.Lerp(0.65f, 1f, impactStrength);
            }

            if (remaining <= 0f)
                SetImpactVfxActive(false);
        }

        private void SetRing(
            LineRenderer ring,
            float radius,
            float y,
            Color color)
        {
            if (ring == null)
                return;

            ring.startColor = color;
            ring.endColor = WithAlpha(color, 0f);
            int count = ring.positionCount;
            for (int index = 0; index < count; index++)
            {
                float angle = (index / (float)count) * Mathf.PI * 2f;
                ring.SetPosition(index, new Vector3(
                    Mathf.Cos(angle) * radius,
                    y,
                    Mathf.Sin(angle) * radius));
            }
        }

        private void SetImpactVfxActive(bool active)
        {
            impactVfxActive = active;
            if (impactRing != null)
                impactRing.gameObject.SetActive(active);
            if (impactFlash != null)
                impactFlash.gameObject.SetActive(active);
            if (impactParticles != null)
                impactParticles.gameObject.SetActive(active);
            if (!active && impactParticles != null)
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void OnDisable()
        {
            SetModeVfxActive(false);
            SetImpactVfxActive(false);
            impactUntil = float.NegativeInfinity;
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
                Destroy(lineMaterial);
            if (particleMaterial != null)
                Destroy(particleMaterial);
        }
    }
}
