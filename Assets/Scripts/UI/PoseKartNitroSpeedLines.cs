using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// One lightweight screen-space mesh for the Nitro airflow effect.
    /// Requires a CanvasRenderer because MaskableGraphic renders via Unity UI.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class PoseKartNitroSpeedLines : MaskableGraphic
    {
        [Header("Gameplay references")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private CarController playerCar;

        [Header("Effect")]
        [SerializeField, Range(24, 96)] private int lineCount = 60;
        [SerializeField, Range(0.05f, 1f)] private float fadeInSeconds = 0.15f;
        [SerializeField, Range(0.05f, 1f)] private float fadeOutSeconds = 0.3f;
        [SerializeField, Range(0f, 1f)] private float maximumOpacity = 0.48f;
        [SerializeField, Range(0.1f, 3f)] private float movementSpeed = 1.2f;
        [SerializeField, Range(0f, 1f)] private float speedIntensity = 0.7f;
        [SerializeField, Range(0.05f, 0.5f)] private float minimumLineWidth = 0.7f;
        [SerializeField, Range(0.05f, 0.8f)] private float maximumLineWidth = 2.6f;
        [SerializeField] private Color whiteColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color cyanColor = new Color(0.15f, 0.9f, 1f, 0.75f);
        [SerializeField] private Color purpleColor = new Color(0.72f, 0.3f, 1f, 0.65f);
        [SerializeField, Range(0f, 1f)] private float accentChance = 0.2f;

        [Header("Composition")]
        [SerializeField, Range(0f, 1f)] private float vanishingPointY = 0.44f;
        [SerializeField, Range(0.1f, 1f)] private float edgeStartRadius = 0.34f;
        [SerializeField, Range(0.1f, 1f)] private float edgeConcentration = 0.72f;

        [Header("Runtime debug (Read Only)")]
        [SerializeField] private bool gameplayNitroActive;
        [SerializeField, Range(0f, 1f)] private float effectBlend;
        [SerializeField] private float actualSpeedKmh;
        [SerializeField] private int activeLineCount;

        private struct SpeedLine
        {
            public float angle;
            public float progress;
            public float length;
            public float speed;
            public float width;
            public Color color;
        }

        private SpeedLine[] lines;
        private CanvasRenderer cachedCanvasRenderer;
        private uint randomState = 0x9E3779B9u;
        private float nextResolveTime;
        private bool initialized;

        protected override void Awake()
        {
            // A runtime builder can create this graphic before adding a CanvasRenderer.
            // Resolve/add it BEFORE attempting to set the graphic's alpha.
            EnsureCanvasRenderer();
            base.Awake();
            raycastTarget = false;
            maskable = true;
            EnsureLines();
        }

        protected override void OnEnable()
        {
            EnsureCanvasRenderer();
            base.OnEnable();
            raycastTarget = false;
            EnsureLines();
        }

        private void EnsureCanvasRenderer()
        {
            if (cachedCanvasRenderer != null)
                return;

            cachedCanvasRenderer = GetComponent<CanvasRenderer>();
            if (cachedCanvasRenderer == null)
                cachedCanvasRenderer = gameObject.AddComponent<CanvasRenderer>();
        }

        private void SetRenderAlpha(float alpha)
        {
            if (cachedCanvasRenderer == null)
                EnsureCanvasRenderer();

            if (cachedCanvasRenderer != null)
                cachedCanvasRenderer.SetAlpha(Mathf.Clamp01(alpha));
        }

        private void Update()
        {
            // Reinitialize if lineCount was changed in the Inspector during Play Mode.
            EnsureLines();
            ResolvePlayerCar();
            float dt = Mathf.Max(0f, Time.deltaTime);
            gameplayNitroActive = playerCar != null && playerCar.IsBoosting;
            float targetBlend = gameplayNitroActive ? 1f : 0f;
            float response = targetBlend > effectBlend
                ? 1f / Mathf.Max(0.01f, fadeInSeconds)
                : 1f / Mathf.Max(0.01f, fadeOutSeconds);
            effectBlend = Mathf.MoveTowards(effectBlend, targetBlend, response * dt);

            actualSpeedKmh = playerCar != null && playerCar.TargetBody != null
                ? playerCar.TargetBody.linearVelocity.magnitude * 3.6f
                : 0f;
            if (effectBlend <= 0.001f)
            {
                activeLineCount = 0;
                SetRenderAlpha(0f);
                return;
            }

            float speedFactor = playerCar != null
                ? Mathf.Clamp01(actualSpeedKmh / Mathf.Max(1f, playerCar.MaxForwardSpeed * 3.6f))
                : 0f;
            float intensity = effectBlend * Mathf.Lerp(1f - speedIntensity, 1f, speedFactor);
            SetRenderAlpha(intensity * maximumOpacity);

            if (lines == null)
                return;

            for (int i = 0; i < lines.Length; i++)
            {
                SpeedLine line = lines[i];
                line.progress += dt * movementSpeed * line.speed * Mathf.Lerp(0.7f, 1.35f, speedFactor);
                if (line.progress > 1f)
                    line.progress -= Mathf.Floor(line.progress);
                lines[i] = line;
            }

            activeLineCount = lines.Length;
            SetVerticesDirty();
        }

        private void ResolvePlayerCar()
        {
            if (playerCar != null && playerCar.isActiveAndEnabled)
                return;
            if (Time.unscaledTime < nextResolveTime)
                return;

            nextResolveTime = Time.unscaledTime + 0.5f;
            if (raceManager == null)
                raceManager = FindFirstObjectByType<RaceManager>();
            playerCar = raceManager != null
                ? raceManager.PlayerController
                : FindFirstObjectByType<CarController>();
        }

        private void EnsureLines()
        {
            int requested = Mathf.Clamp(lineCount, 24, 96);
            if (initialized && lines != null && lines.Length == requested)
                return;

            lines = new SpeedLine[requested];
            for (int i = 0; i < lines.Length; i++)
            {
                float angle = Next01() < 0.58f
                    ? Mathf.Lerp(Mathf.PI, Mathf.PI * 2f, Next01())
                    : Next01() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(edgeStartRadius, 1f, Mathf.Pow(Next01(), edgeConcentration));
                lines[i] = new SpeedLine
                {
                    angle = angle,
                    progress = radius,
                    length = Mathf.Lerp(0.06f, 0.22f, Next01()),
                    speed = Mathf.Lerp(0.72f, 1.35f, Next01()),
                    width = Mathf.Lerp(minimumLineWidth, maximumLineWidth, Next01()),
                    color = PickColor()
                };
            }

            initialized = true;
            SetRenderAlpha(0f);
            SetVerticesDirty();
        }

        private Color PickColor()
        {
            float roll = Next01();
            if (roll < accentChance * 0.55f) return cyanColor;
            if (roll < accentChance) return purpleColor;
            return whiteColor;
        }

        private float Next01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00FFFFFFu) / 16777216f;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (lines == null || lines.Length == 0)
                return;

            Rect rect = rectTransform.rect;
            float width = Mathf.Max(1f, rect.width);
            float height = Mathf.Max(1f, rect.height);
            Vector2 vanishingPoint = new Vector2(0f, (vanishingPointY - 0.5f) * height);

            for (int i = 0; i < lines.Length; i++)
            {
                SpeedLine line = lines[i];
                Vector2 direction = new Vector2(Mathf.Cos(line.angle), Mathf.Sin(line.angle));
                float startRadius = Mathf.Lerp(edgeStartRadius, 1.18f, line.progress);
                float endRadius = startRadius + line.length;
                Vector2 start = vanishingPoint + new Vector2(
                    direction.x * width * 0.5f * startRadius,
                    direction.y * height * 0.5f * startRadius);
                Vector2 end = vanishingPoint + new Vector2(
                    direction.x * width * 0.5f * endRadius,
                    direction.y * height * 0.5f * endRadius);
                Vector2 perpendicular = new Vector2(-direction.y, direction.x) * line.width;
                Color startColor = new Color(line.color.r, line.color.g, line.color.b, 0f);
                Color endColor = new Color(line.color.r, line.color.g, line.color.b, line.color.a);

                UIVertex a = UIVertex.simpleVert;
                a.position = start - perpendicular * 0.18f;
                a.color = startColor;
                UIVertex b = UIVertex.simpleVert;
                b.position = start + perpendicular * 0.18f;
                b.color = startColor;
                UIVertex c = UIVertex.simpleVert;
                c.position = end + perpendicular * 0.04f;
                c.color = endColor;
                UIVertex d = UIVertex.simpleVert;
                d.position = end - perpendicular * 0.04f;
                d.color = endColor;

                int vertexIndex = vh.currentVertCount;
                vh.AddVert(a);
                vh.AddVert(b);
                vh.AddVert(c);
                vh.AddVert(d);
                vh.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
                vh.AddTriangle(vertexIndex, vertexIndex + 2, vertexIndex + 3);
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        protected override void OnValidate()
        {
            raycastTarget = false;
            lineCount = Mathf.Clamp(lineCount, 24, 96);
            fadeInSeconds = Mathf.Max(0.05f, fadeInSeconds);
            fadeOutSeconds = Mathf.Max(0.05f, fadeOutSeconds);
            minimumLineWidth = Mathf.Min(minimumLineWidth, maximumLineWidth);
            initialized = false;
        }
    }
}
