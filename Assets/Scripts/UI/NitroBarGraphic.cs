using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

namespace PoseKart.UI
{
    /// <summary>
    /// Asphalt 9-style trapezoid graphic for the Nitro HUD.
    /// Draws the dark slanted track, dynamic bi-linear gradient fill,
    /// an emphasized Perfect timing zone during Single Nitro,
    /// and an intense glowing flare tip synchronized with the vehicle's Nitro system.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class NitroBarGraphic : MaskableGraphic
    {
        [Header("Nitro System Sync")]
        [Tooltip("Auto-read nitro charge and stage directly from the player's car.")]
        [SerializeField] private CarController targetCar;
        [SerializeField] private bool autoSyncWithCar = true;

        [Header("Bar Geometry")]
        [SerializeField, Range(0f, 1f)] private float fillAmount;
        [SerializeField, Range(0f, 1f)] private float trailingFillAmount;
        [SerializeField, Min(0f)] private float slantWidth = 16f;
        [SerializeField, Min(0f)] private float edgeHighlightWidth = 8f;
        [SerializeField, Min(0f)] private float timingCursorWidth = 5f;
        [SerializeField, Min(0f)] private float timingDividerWidth = 3f;
        [SerializeField, Range(0f, 1f)] private float timingZoneAlpha = 0.92f;
        [SerializeField, Range(0f, 1f)] private float timingGlowAlpha = 0.3f;
        [SerializeField, Min(0f)] private float timingPulseSpeed = 12f;

        [Header("Armed Nitro Stage Band")]
        [Tooltip("Start of the large purple band while the existing CarController double-tap window can still activate Shockwave. This is visual placement only; it does not change timing.")]
        [SerializeField, Range(0f, 1f)] private float shockwaveArmedBandStart01 = 0.72f;

        [Header("Active Colors (Runtime)")]
        [SerializeField] private Color trackColor = new Color(0.04f, 0.05f, 0.08f, 0.88f);
        [SerializeField] private Color fillStartColor = new Color(1f, 0.70f, 0.05f, 1f);
        [SerializeField] private Color fillEndColor = new Color(1f, 0.92f, 0.15f, 1f);
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.85f, 0.95f);

        [Header("Asphalt 9 Stage Presets")]
        [Tooltip("Single Nitro (Yellow)")]
        [SerializeField] private Color singleStart = new Color(1f, 0.58f, 0.0f, 1f);
        [SerializeField] private Color singleEnd = new Color(1f, 0.92f, 0.05f, 1f);
        [SerializeField] private Color singleHighlight = new Color(1f, 1f, 0.85f, 1f);

        [Tooltip("Double Nitro (Red-Orange)")]
        [SerializeField] private Color doubleStart = new Color(0.92f, 0.18f, 0.0f, 1f);
        [SerializeField] private Color doubleEnd = new Color(1f, 0.52f, 0.02f, 1f);
        [SerializeField] private Color doubleHighlight = new Color(1f, 0.85f, 0.6f, 1f);

        [Tooltip("Perfect Nitro (Electric Cyan / Blue - Ảnh 3)")]
        [SerializeField] private Color perfectStart = new Color(0.0f, 0.48f, 0.98f, 1f);
        [SerializeField] private Color perfectEnd = new Color(0.05f, 0.92f, 1f, 1f);
        [SerializeField] private Color perfectHighlight = new Color(0.75f, 1f, 1f, 1f);

        [Tooltip("Shockwave / Triple Nitro (Neon Purple / Magenta - Ảnh 1)")]
        [SerializeField] private Color shockwaveStart = new Color(0.48f, 0.0f, 0.92f, 1f);
        [SerializeField] private Color shockwaveEnd = new Color(1f, 0.08f, 0.82f, 1f);
        [SerializeField] private Color shockwaveHighlight = new Color(1f, 0.82f, 1f, 1f);

        [Tooltip("Idle Fuel Available")]
        [SerializeField] private Color idleStart = new Color(0.95f, 0.65f, 0.05f, 1f);
        [SerializeField] private Color idleEnd = new Color(1f, 0.85f, 0.12f, 1f);

        [Header("Perfect Nitro Timing Section")]
        [Tooltip("Blue/cyan section for the Perfect Nitro timing window")]
        [FormerlySerializedAs("redSectionStart")]
        [SerializeField] private Color perfectSectionStart = new Color(0.08f, 0.82f, 1f, 1f);
        [FormerlySerializedAs("redSectionEnd")]
        [SerializeField] private Color perfectSectionEnd = new Color(0.35f, 1f, 1f, 1f);

        private float nextCarResolveTime;

        public float FillAmount
        {
            get => fillAmount;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(fillAmount, clamped))
                    return;

                fillAmount = clamped;
                SetVerticesDirty();
            }
        }

        public CarController TargetCar
        {
            get => targetCar;
            set => targetCar = value;
        }

        public bool AutoSyncWithCar
        {
            get => autoSyncWithCar;
            set => autoSyncWithCar = value;
        }

        public float TrailingFillAmount
        {
            get => trailingFillAmount;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(trailingFillAmount, clamped))
                    return;

                trailingFillAmount = clamped;
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (Application.isPlaying && autoSyncWithCar)
            {
                ResolveTargetCar();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !autoSyncWithCar)
                return;

            if (!PoseKartLocalPlayerResolver.IsLocalHuman(targetCar))
            {
                if (Time.unscaledTime >= nextCarResolveTime)
                {
                    nextCarResolveTime = Time.unscaledTime + 0.5f;
                    ResolveTargetCar();
                }
            }

            if (targetCar != null && targetCar.isActiveAndEnabled)
            {
                SetNitroMode(targetCar.CurrentNitroMode, targetCar.NitroFill01, targetCar.IsNitroBoosting);
                TrailingFillAmount = targetCar.NitroFill01;
            }
        }

        private void ResolveTargetCar()
        {
            targetCar = PoseKartLocalPlayerResolver.FindLocalCar();
        }

        /// <summary>
        /// Synchronizes the bar colors and fill percentage with the active Nitro mode.
        /// </summary>
        public void SetNitroMode(CarController.NitroMode mode, float amount, bool isBoosting)
        {
            fillAmount = Mathf.Clamp01(amount);
            trailingFillAmount = Mathf.Max(fillAmount, trailingFillAmount);

            if (isBoosting)
            {
                switch (mode)
                {
                    case CarController.NitroMode.Single:
                        fillStartColor = singleStart;
                        fillEndColor = singleEnd;
                        highlightColor = singleHighlight;
                        break;

                    case CarController.NitroMode.Double:
                        fillStartColor = doubleStart;
                        fillEndColor = doubleEnd;
                        highlightColor = doubleHighlight;
                        break;

                    case CarController.NitroMode.Perfect:
                        // Electric Cyan / Blue (Màu xanh chuẩn Asphalt 9 - Ảnh 3)
                        fillStartColor = perfectStart;
                        fillEndColor = perfectEnd;
                        highlightColor = perfectHighlight;
                        break;

                    case CarController.NitroMode.Shockwave:
                        // Shockwave Purple / Magenta (Màu tím chuẩn Asphalt 9 - Ảnh 1)
                        fillStartColor = shockwaveStart;
                        fillEndColor = shockwaveEnd;
                        highlightColor = shockwaveHighlight;
                        break;

                    default:
                        fillStartColor = singleStart;
                        fillEndColor = singleEnd;
                        highlightColor = singleHighlight;
                        break;
                }
            }
            else
            {
                // Idle charging state
                fillStartColor = idleStart;
                fillEndColor = idleEnd;
                highlightColor = new Color(1f, 0.98f, 0.75f, 0.55f);
            }

            SetVerticesDirty();
        }

        /// <summary>
        /// Backwards-compatible API.
        /// </summary>
        public void SetVisuals(float amount, Color track, Color fill, Color highlight)
        {
            fillAmount = Mathf.Clamp01(amount);
            trailingFillAmount = fillAmount;
            trackColor = track;
            fillStartColor = Color.Lerp(fill, Color.black, 0.15f);
            fillEndColor = fill;
            highlightColor = highlight;
            SetVerticesDirty();
        }

        /// <summary>
        /// Full gradient API.
        /// </summary>
        public void SetVisuals(float amount, Color track, Color startColor, Color endColor, Color highlight)
        {
            fillAmount = Mathf.Clamp01(amount);
            trailingFillAmount = fillAmount;
            trackColor = track;
            fillStartColor = startColor;
            fillEndColor = endColor;
            highlightColor = highlight;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            float left = rect.xMin;
            float right = rect.xMax;
            float bottom = rect.yMin;
            float top = rect.yMax;
            float slant = Mathf.Clamp(slantWidth, 0f, Mathf.Max(0f, rect.width * 0.25f));

            // 1. Dark background track
            AddTrapezoid(vertexHelper, left, right, bottom, top, slant, trackColor);

            float amount = Mathf.Clamp01(fillAmount);
            float trailingAmount = Mathf.Max(amount, Mathf.Clamp01(trailingFillAmount));
            bool isTimingActive = targetCar != null && targetCar.IsNitroTimingActive;
            bool shockwaveArmed = isTimingActive &&
                targetCar != null && targetCar.IsShockwaveTimingNow;

            if (amount <= 0.0001f || rect.width <= 0.001f)
                return;

            // When pickup/drift charge is smoothed, show a restrained trailing
            // highlight for the real charge so the primary fill never feels
            // disconnected from gameplay.
            if (trailingAmount > amount + 0.0001f)
            {
                Color trailStart = Color.Lerp(fillStartColor, Color.white, 0.25f);
                Color trailEnd = Color.Lerp(fillEndColor, Color.white, 0.35f);
                trailStart.a *= 0.42f;
                trailEnd.a *= 0.42f;
                AddSlantedSlice(vertexHelper, amount, trailingAmount,
                    left, right, bottom, top, slant, trailStart, trailEnd);
            }

            // 3. Main fuel fill. A full idle tank remains yellow. Once the
            // existing CarController state arms the Shockwave double-tap window,
            // the filled portion is split into yellow -> purple so the player
            // gets a strong visual cue without changing gameplay timing.
            if (shockwaveArmed)
            {
                float purpleStart = Mathf.Clamp01(shockwaveArmedBandStart01);
                AddSlantedSlice(vertexHelper, 0f, Mathf.Min(amount, purpleStart),
                    left, right, bottom, top, slant,
                    fillStartColor, fillEndColor);
                AddSlantedSlice(vertexHelper, purpleStart, amount,
                    left, right, bottom, top, slant,
                    shockwaveStart, shockwaveEnd);
            }
            else if (isTimingActive)
            {
                float redMin = targetCar != null ? targetCar.PerfectZoneMin01 : 0.72f;
                float redMax = targetCar != null ? targetCar.PerfectZoneMax01 : 0.86f;

                // Draw yellow fuel from 0 up to min(amount, redMin)
                float yellowBaseEnd = Mathf.Min(amount, redMin);
                if (yellowBaseEnd > 0.001f)
                {
                    AddSlantedSlice(vertexHelper, 0f, yellowBaseEnd, left, right, bottom, top, slant, fillStartColor, fillEndColor);
                }

                // If fuel extends past the red section (amount > redMax):
                // draw yellow fuel from redMax to amount
                if (amount > redMax)
                {
                    AddSlantedSlice(vertexHelper, redMax, amount, left, right, bottom, top, slant, fillStartColor, fillEndColor);
                }

            }
            else
            {
                AddSlantedSlice(vertexHelper, 0f, amount, left, right, bottom, top, slant, fillStartColor, fillEndColor);
            }

            // Draw the Perfect section after the fuel so it remains visible
            // even when the current amount is above the timing window.
            if (isTimingActive && !shockwaveArmed)
            {
                float redMin = targetCar != null ? targetCar.PerfectZoneMin01 : 0.72f;
                float redMax = targetCar != null ? targetCar.PerfectZoneMax01 : 0.86f;
                bool isPerfect = targetCar != null && targetCar.IsPerfectTimingNow;

                if (redMax > redMin)
                {
                    Color rStart = perfectSectionStart;
                    Color rEnd = perfectSectionEnd;
                    rStart.a *= timingZoneAlpha;
                    rEnd.a *= timingZoneAlpha;

                    if (isPerfect)
                    {
                        float pulse = 0.5f + 0.5f *
                            Mathf.Sin(Time.unscaledTime * timingPulseSpeed);
                        rStart = Color.Lerp(rStart, Color.white, pulse * 0.75f);
                        rEnd = Color.Lerp(rEnd, Color.white, pulse * 0.9f);
                        rStart.a = Mathf.Lerp(rStart.a, 1f, pulse * timingGlowAlpha);
                        rEnd.a = Mathf.Lerp(rEnd.a, 1f, pulse * timingGlowAlpha);
                    }

                    // Never draw a timing band beyond the actual filled amount.
                    float visibleRedMax = Mathf.Min(redMax, amount);
                    AddSlantedSlice(vertexHelper, redMin, visibleRedMax,
                        left, right, bottom, top, slant, rStart, rEnd);

                    if (visibleRedMax > redMin)
                    {
                        Color dividerCol = new Color(1f, 1f, 1f, isPerfect ? 1f : 0.9f);
                        AddDividerLine(vertexHelper, redMin, left, right,
                            bottom, top, slant, dividerCol, timingDividerWidth);
                        AddDividerLine(vertexHelper, visibleRedMax, left, right,
                            bottom, top, slant, dividerCol, timingDividerWidth);
                    }
                }

                // Keep the current fuel cursor above the timing overlay.
                AddDividerLine(vertexHelper, amount, left, right,
                    bottom, top, slant, Color.white, timingCursorWidth);

                // A soft cyan halo makes the active timing cursor readable
                // without changing any gameplay timing.
                if (isPerfect)
                {
                    Color halo = perfectSectionEnd;
                    halo.a = timingGlowAlpha;
                    AddDividerLine(vertexHelper, amount, left, right,
                        bottom, top, slant, halo, timingCursorWidth * 2.2f);
                }
            }
            else if (shockwaveArmed)
            {
                float purpleStart = Mathf.Clamp01(shockwaveArmedBandStart01);
                Color pulseColor = shockwaveEnd;
                float pulse = 0.5f + 0.5f *
                    Mathf.Sin(Time.unscaledTime * timingPulseSpeed);
                pulseColor = Color.Lerp(pulseColor, Color.white, pulse * 0.65f);
                pulseColor.a = Mathf.Lerp(0.75f, 1f, pulse);

                AddDividerLine(vertexHelper, purpleStart,
                    left, right, bottom, top, slant,
                    pulseColor, timingDividerWidth * 1.25f);
                AddDividerLine(vertexHelper, amount,
                    left, right, bottom, top, slant,
                    shockwaveHighlight, timingCursorWidth);
            }

            // 4. Intense glowing flare head at the leading edge
            float glowWidth = Mathf.Clamp(edgeHighlightWidth, 0f, rect.width * 0.12f);
            if (glowWidth > 0.001f)
            {
                float edgeBottom = Mathf.Lerp(left, right, amount);
                float edgeTop = Mathf.Lerp(left + slant, right - slant, amount);

                Color glow = highlightColor;
                Color transparent = new Color(glow.r, glow.g, glow.b, 0f);
                Color intenseWhite = Color.Lerp(glow, Color.white, 0.75f);

                float glowBottomStart = Mathf.Max(left, edgeBottom - glowWidth);
                float glowTopStart = Mathf.Max(left + slant, edgeTop - glowWidth);

                AddGradientQuad(vertexHelper,
                    new Vector2(glowBottomStart, bottom),
                    new Vector2(edgeBottom, bottom),
                    new Vector2(edgeTop, top),
                    new Vector2(glowTopStart, top),
                    transparent, intenseWhite, intenseWhite, transparent);
            }
        }

        private static void AddTrapezoid(VertexHelper vertexHelper, float left, float right,
            float bottom, float top, float slant, Color color)
        {
            AddGradientQuad(vertexHelper,
                new Vector2(left, bottom),
                new Vector2(right, bottom),
                new Vector2(right - slant, top),
                new Vector2(left + slant, top),
                color, color, color, color);
        }

        private static void AddSlantedSlice(VertexHelper vertexHelper,
            float startNorm, float endNorm, float left, float right, float bottom, float top, float slant,
            Color startColor, Color endColor)
        {
            if (endNorm <= startNorm + 0.0001f)
                return;

            float bLeft = Mathf.Lerp(left, right, startNorm);
            float bRight = Mathf.Lerp(left, right, endNorm);
            float tLeft = Mathf.Lerp(left + slant, right - slant, startNorm);
            float tRight = Mathf.Lerp(left + slant, right - slant, endNorm);

            Color startTop = Color.Lerp(startColor, Color.white, 0.22f);
            Color endTop = Color.Lerp(endColor, Color.white, 0.30f);

            AddGradientQuad(vertexHelper,
                new Vector2(bLeft, bottom),
                new Vector2(bRight, bottom),
                new Vector2(tRight, top),
                new Vector2(tLeft, top),
                startColor, endColor, endTop, startTop);
        }

        private static void AddDividerLine(VertexHelper vertexHelper,
            float norm, float left, float right, float bottom, float top, float slant,
            Color color, float width = 2.5f)
        {
            float halfW = width * 0.5f;
            float bX = Mathf.Lerp(left, right, norm);
            float tX = Mathf.Lerp(left + slant, right - slant, norm);

            AddGradientQuad(vertexHelper,
                new Vector2(bX - halfW, bottom),
                new Vector2(bX + halfW, bottom),
                new Vector2(tX + halfW, top),
                new Vector2(tX - halfW, top),
                color, color, color, color);
        }

        private static void AddGradientQuad(VertexHelper vertexHelper,
            Vector2 bottomLeft, Vector2 bottomRight, Vector2 topRight, Vector2 topLeft,
            Color bottomLeftColor, Color bottomRightColor, Color topRightColor, Color topLeftColor)
        {
            int start = vertexHelper.currentVertCount;
            vertexHelper.AddVert(bottomLeft, bottomLeftColor, Vector2.zero);
            vertexHelper.AddVert(bottomRight, bottomRightColor, Vector2.zero);
            vertexHelper.AddVert(topRight, topRightColor, Vector2.zero);
            vertexHelper.AddVert(topLeft, topLeftColor, Vector2.zero);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
