using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PoseKart.Race
{
    /// <summary>
    /// Procedural non-interactive screen splats for the PLAYER only.
    /// Manages ink windshield splats and cinematic URP Post-Processing:
    /// - Heavy edge Chromatic Aberration (color splitting).
    /// - Deep inky Vignette (tunnel vision).
    /// - Disorienting Lens Distortion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartInkScreenOverlay : MonoBehaviour
    {
        private static KartInkScreenOverlay instance;
        private CanvasGroup group;
        private Texture2D inkTexture;
        private float visibleUntil;

        // URP Post-Processing
        private Volume postVolume;
        private VolumeProfile postProfile;
        private ChromaticAberration chromaticAberration;
        private Vignette vignette;
        private LensDistortion lensDistortion;

        public static void ShowUntil(float worldTime)
        {
            if (instance == null)
            {
                GameObject root = new GameObject("PoseKart_Ink_Screen", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
                instance = root.AddComponent<KartInkScreenOverlay>();
            }
            instance.visibleUntil = Mathf.Max(instance.visibleUntil, worldTime);
        }

        public static void Hide()
        {
            if (instance != null)
                instance.visibleUntil = Time.time;
        }

        private void Awake()
        {
            instance = this;
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2200;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // Setup dedicated URP Post-Processing Volume for Blur & Chromatic Aberration
            postVolume = gameObject.AddComponent<Volume>();
            postVolume.isGlobal = true;
            postVolume.priority = 100f;
            postVolume.weight = 0f;

            postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            postProfile.name = "Ink_Screen_PostProcess_RuntimeProfile";

            // 1. Chromatic Aberration: Edge color separation
            if (!postProfile.TryGet(out chromaticAberration))
                chromaticAberration = postProfile.Add<ChromaticAberration>(true);
            chromaticAberration.intensity.overrideState = true;
            chromaticAberration.intensity.value = 0.95f;

            // 2. Vignette: Inky dark purple/black border
            if (!postProfile.TryGet(out vignette))
                vignette = postProfile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.48f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.65f;
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.04f, 0.015f, 0.08f, 1f);

            // 3. Lens Distortion: Subtle disorienting barrel curve
            if (!postProfile.TryGet(out lensDistortion))
                lensDistortion = postProfile.Add<LensDistortion>(true);
            lensDistortion.intensity.overrideState = true;
            lensDistortion.intensity.value = -0.22f;
            lensDistortion.scale.overrideState = true;
            lensDistortion.scale.value = 1.04f;

            postVolume.profile = postProfile;

            // Tăng độ phân giải lên 256 để nét mực sắc và nham nhở hơn khi phóng to
            inkTexture = BuildInkTexture(256);

            // Mở rộng kích thước UI SizeDelta và thêm vết mực phủ kín hơn
            AddSplat("Ink_Left", new Vector2(0.12f, 0.68f), new Vector2(950f, 900f), 28f);
            AddSplat("Ink_Top", new Vector2(0.50f, 0.88f), new Vector2(900f, 750f), -15f);
            AddSplat("Ink_Right", new Vector2(0.88f, 0.42f), new Vector2(1000f, 950f), 72f);
            AddSplat("Ink_Bottom", new Vector2(0.38f, 0.08f), new Vector2(920f, 780f), -35f);
            AddSplat("Ink_Middle", new Vector2(0.52f, 0.50f), new Vector2(850f, 800f), -46f);
            AddSplat("Ink_Extra_Splash", new Vector2(0.28f, 0.32f), new Vector2(700f, 650f), 115f);
        }

        private void AddSplat(string label, Vector2 anchor, Vector2 size, float angle)
        {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(RawImage));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            RawImage image = go.GetComponent<RawImage>();
            image.texture = inkTexture;
            // Màu mực tối đậm, xám đen đặc trưng arcade
            image.color = new Color(0.02f, 0.025f, 0.04f, 0.98f);
            image.raycastTarget = false;
        }

        private void Update()
        {
            float target = Time.time < visibleUntil ? 1f : 0f;
            float seconds = target > 0f ? 0.12f : 0.65f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime / seconds);
            if (postVolume != null)
            {
                postVolume.weight = group.alpha;
            }
        }

        private static Texture2D BuildInkTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "PoseKart_InkSplat_Procedural";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size - 0.5f;

                    float angle = Mathf.Atan2(v, u);
                    float radius = Mathf.Sqrt(u * u + v * v);

                    // Tạo đường viền biến dạng cực kỳ nham nhở với nhiều tầng tần số
                    float boundary = 0.25f
                        + 0.065f * Mathf.Sin(angle * 5f + 1.4f)
                        + 0.045f * Mathf.Sin(angle * 11f - 0.8f)
                        + 0.030f * Mathf.Sin(angle * 23f + 2.1f)
                        + 0.018f * Mathf.Sin(angle * 37f - 1.2f); // Tần số cao tạo các dầm mực rách rưới

                    // Tăng độ sắc ở rìa vết mực (chuyển tiếp cứng hơn)
                    float alpha = Mathf.Clamp01((boundary - radius) * 80f);

                    // Thêm các giọt bắn rải rác xung quanh vết mực chính (Messy droplets)
                    alpha = Mathf.Max(alpha, Circle(u, v, -0.32f, 0.28f, 0.070f));
                    alpha = Mathf.Max(alpha, Circle(u, v, 0.36f, 0.22f, 0.062f));
                    alpha = Mathf.Max(alpha, Circle(u, v, 0.24f, -0.35f, 0.058f));
                    alpha = Mathf.Max(alpha, Circle(u, v, -0.22f, -0.39f, 0.045f));
                    alpha = Mathf.Max(alpha, Circle(u, v, -0.10f, 0.42f, 0.038f));
                    alpha = Mathf.Max(alpha, Circle(u, v, 0.41f, -0.08f, 0.050f));
                    alpha = Mathf.Max(alpha, Circle(u, v, -0.42f, -0.12f, 0.040f));

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float Circle(float u, float v, float cx, float cy, float radius)
        {
            float distance = Vector2.Distance(new Vector2(u, v), new Vector2(cx, cy));
            return Mathf.Clamp01((radius - distance) * 90f);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
            if (inkTexture != null)
                Destroy(inkTexture);
            if (postProfile != null)
                Destroy(postProfile);
        }
    }
}