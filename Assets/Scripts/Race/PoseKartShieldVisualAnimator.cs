using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Visual-only shield animation. It never changes the shield's gameplay
    /// state, colliders or the vehicle transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartShieldVisualAnimator : MonoBehaviour
    {
        [Header("Rotation")]
        [SerializeField] private float rotationSpeed = 50f;
        [SerializeField] private Transform secondaryVisual;
        [SerializeField] private float secondaryRotationSpeed = -45f;

        [Header("Pulse and Fade")]
        [SerializeField, Range(0f, 1f)] private float minimumOpacity = .16f;
        [SerializeField, Range(0f, 1f)] private float maximumOpacity = .28f;
        [SerializeField, Min(.01f)] private float pulseSpeed = 2f;
        [SerializeField, Min(.01f)] private float fadeInDuration = .2f;
        [SerializeField, Min(.01f)] private float fadeOutDuration = .3f;

        [Header("Impact Surface Reaction")]
        [Tooltip("Thời gian gợn/lõm trên shader khi projectile chạm khiên.")]
        [SerializeField, Min(.01f)] private float impactDisplacementDuration = .38f;
        [Tooltip("Biên độ displacement truyền vào shader nếu material có _DisplacementStrength.")]
        [SerializeField, Min(0f)] private float impactDisplacementMagnitude = .4f;
        [Tooltip("Mức dissolve cộng tạm thời nếu material có _Disolve hoặc _Dissolve.")]
        [SerializeField, Min(0f)] private float impactDissolveAmount = .12f;

        private Renderer[] renderers;
        private Material[] materials;
        private float[] baseGlow;
        private float[] baseDisplacement;
        private float[] baseDissolve;
        private Color configuredColor = new Color(.15f, .85f, 1f, 1f);
        private float fade;
        private bool fadingOut;
        private bool initialized;
        private float flashUntil;
        private float impactStartedAt = -1f;

        private void Awake()
        {
            CacheMaterials();
        }

        private void OnEnable()
        {
            if (!initialized)
                CacheMaterials();
            fade = 0f;
            fadingOut = false;
            ApplyVisual(0f);
        }

        public void Configure(Color color)
        {
            configuredColor = new Color(color.r, color.g, color.b, 1f);
            ApplyVisual(fade);
        }

        public void BeginFadeIn()
        {
            fadingOut = false;
            fade = 0f;
            ApplyVisual(0f);
        }

        public void FadeOutAndDestroy()
        {
            if (!isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }

            fadingOut = true;
        }

        private Vector3 initialLocalScale = Vector3.one;
        private bool baseScaleConfigured;
        public void SetBaseScale(Vector3 scale)
        {
            if (scale.sqrMagnitude <= 0.0001f)
                return;

            initialLocalScale = scale;
            baseScaleConfigured = true;
        }

        private void Start()
        {
            // Không ghi đè scale mà PoseKartShieldGeneratorVisual
            // đã cấu hình trước lần kích hoạt đầu tiên.
            if (!baseScaleConfigured &&
                transform.localScale.sqrMagnitude > 0.0001f)
            {
                initialLocalScale = transform.localScale;
            }
        }

        public void FlashImpact()
        {
            FlashImpact(transform.position);
        }

        public void FlashImpact(Vector3 impactPoint)
        {
            flashUntil = Mathf.Max(flashUntil, Time.time + .18f);
            impactStartedAt = Time.time;

            if (materials == null)
                return;

            // The sample Shield shader expects a world-space hit position.
            // Only write it when the assigned material actually supports it.
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null && material.HasProperty("_HitPos"))
                    material.SetVector("_HitPos", impactPoint);
            }
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
            if (secondaryVisual != null)
                secondaryVisual.Rotate(Vector3.up, secondaryRotationSpeed * Time.deltaTime, Space.Self);

            // Breathing pulse wave on shield surface
            float pulseScale = 1f + Mathf.Sin(Time.time * 6f) * 0.035f;
            transform.localScale = initialLocalScale * pulseScale;

            float duration = fadingOut ? fadeOutDuration : fadeInDuration;
            float direction = fadingOut ? -1f : 1f;
            fade = Mathf.Clamp01(fade + direction * Time.deltaTime / Mathf.Max(.01f, duration));

            float pulse = .5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 2f * pulseSpeed);
            float opacity = Mathf.Lerp(minimumOpacity, maximumOpacity, pulse) * fade;
            if (Time.time < flashUntil)
            {
                float flash = Mathf.InverseLerp(flashUntil - .18f, flashUntil, Time.time);
                opacity = Mathf.Clamp01(opacity + Mathf.Lerp(.45f, 0f, flash));
            }
            ApplyVisual(opacity);

            if (fadingOut && fade <= 0f)
                Destroy(gameObject);
        }

        private void CacheMaterials()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            int materialCount = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    materialCount += renderer.sharedMaterials.Length;
            }

            materials = new Material[materialCount];
            baseGlow = new float[materialCount];
            baseDisplacement = new float[materialCount];
            baseDissolve = new float[materialCount];
            int index = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] instances = renderer.materials;
                for (int i = 0; i < instances.Length; i++)
                {
                    Material material = instances[i];
                    materials[index] = material;
                    baseGlow[index] = material.HasProperty("_GlowIntensity")
                        ? material.GetFloat("_GlowIntensity") : 1f;
                    baseDisplacement[index] = material.HasProperty("_DisplacementStrength")
                        ? material.GetFloat("_DisplacementStrength") : 0f;
                    baseDissolve[index] = material.HasProperty("_Disolve")
                        ? material.GetFloat("_Disolve")
                        : (material.HasProperty("_Dissolve")
                            ? material.GetFloat("_Dissolve")
                            : 0f);
                    MakeTransparentIfSupported(material);
                    index++;
                }
            }

            initialized = true;
        }

        private void ApplyVisual(float opacity)
        {
            if (materials == null)
                return;

            float normalized = maximumOpacity > .001f ? Mathf.Clamp01(opacity / maximumOpacity) : 0f;
            float flashFactor = Time.time < flashUntil
                ? Mathf.Clamp01((flashUntil - Time.time) / 0.18f)
                : 0f;
            float impactFactor = 0f;
            if (impactStartedAt >= 0f)
            {
                float impactTime = Time.time - impactStartedAt;
                float normalizedImpact = impactTime / Mathf.Max(.01f, impactDisplacementDuration);
                if (normalizedImpact < 1f)
                {
                    // A short positive/negative wave reads as a dent followed
                    // by a ripple without creating a new material per frame.
                    impactFactor = Mathf.Sin(normalizedImpact * Mathf.PI * 2f)
                        * (1f - normalizedImpact);
                }
                else
                {
                    impactStartedAt = -1f;
                }
            }

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                // Flash brilliant pure white on impact
                Color color = Color.Lerp(configuredColor, Color.white, flashFactor * 0.85f);
                color.a = opacity;
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
                if (material.HasProperty("_GlowIntensity"))
                {
                    float glow = baseGlow[i] * Mathf.Lerp(.35f, 1f, normalized) + flashFactor * 2.5f;
                    material.SetFloat("_GlowIntensity", glow);
                }
                if (material.HasProperty("_DisplacementStrength"))
                {
                    material.SetFloat(
                        "_DisplacementStrength",
                        baseDisplacement[i] + impactFactor * impactDisplacementMagnitude);
                }
                if (material.HasProperty("_Disolve"))
                {
                    material.SetFloat(
                        "_Disolve",
                        baseDissolve[i] + flashFactor * impactDissolveAmount);
                }
                if (material.HasProperty("_Dissolve"))
                {
                    material.SetFloat(
                        "_Dissolve",
                        baseDissolve[i] + flashFactor * impactDissolveAmount);
                }
                if (material.HasProperty("_EmissionColor"))
                {
                    Color emission = Color.Lerp(configuredColor * Mathf.Lerp(.1f, .65f, normalized), Color.white * 3.5f, flashFactor);
                    material.SetColor("_EmissionColor", emission);
                }
            }
        }

        private static void MakeTransparentIfSupported(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
        }

        private void OnDestroy()
        {
            if (materials == null)
                return;

            foreach (Material material in materials)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }
}
