
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// Editable angular UI panel with an optional colored border.
    /// Can be used for the minimap frame and other arcade HUD panels.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PoseKartHUDPanelGraphic : Graphic
    {
        [Header("Corner Cuts")]
        [SerializeField, Min(0f)] private float topLeftCut = 12f;
        [SerializeField, Min(0f)] private float topRightCut = 12f;
        [SerializeField, Min(0f)] private float bottomRightCut = 12f;
        [SerializeField, Min(0f)] private float bottomLeftCut = 0f;

        [Header("Optional Border")]
        [Tooltip("Set to 0 to preserve the original panel appearance.")]
        [SerializeField, Min(0f)] private float borderWidth = 0f;

        [SerializeField]
        private Color borderColor =
            new Color(0.16f, 0.85f, 0.94f, 1f);

        public void ConfigureCuts(
            float topLeft,
            float topRight,
            float bottomRight,
            float bottomLeft)
        {
            topLeftCut = Mathf.Max(0f, topLeft);
            topRightCut = Mathf.Max(0f, topRight);
            bottomRightCut = Mathf.Max(0f, bottomRight);
            bottomLeftCut = Mathf.Max(0f, bottomLeft);

            SetVerticesDirty();
        }

        public void ConfigureBorder(float width, Color tint)
        {
            borderWidth = Mathf.Max(0f, width);
            borderColor = tint;

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();

            if (rect.width <= 0f || rect.height <= 0f)
                return;

            float tl = Mathf.Max(0f, topLeftCut);
            float tr = Mathf.Max(0f, topRightCut);
            float br = Mathf.Max(0f, bottomRightCut);
            float bl = Mathf.Max(0f, bottomLeftCut);

            ClampCuts(
                ref tl, ref tr, ref br, ref bl,
                rect.width, rect.height);

            Vector2[] outer = BuildPoints(
                rect, tl, tr, br, bl);

            float thickness = Mathf.Min(
                Mathf.Max(0f, borderWidth),
                rect.width * 0.45f,
                rect.height * 0.45f);

            // Original appearance: solid panel without a border.
            if (thickness <= 0.001f)
            {
                AddFilledPolygon(vh, outer, color);
                return;
            }

            Rect innerRect = new Rect(
                rect.xMin + thickness,
                rect.yMin + thickness,
                rect.width - thickness * 2f,
                rect.height - thickness * 2f);

            // Reduce the corner cuts for the inset polygon.
            float innerTL = Mathf.Max(0f, tl - thickness);
            float innerTR = Mathf.Max(0f, tr - thickness);
            float innerBR = Mathf.Max(0f, br - thickness);
            float innerBL = Mathf.Max(0f, bl - thickness);

            ClampCuts(
                ref innerTL,
                ref innerTR,
                ref innerBR,
                ref innerBL,
                innerRect.width,
                innerRect.height);

            Vector2[] inner = BuildPoints(
                innerRect,
                innerTL,
                innerTR,
                innerBR,
                innerBL);

            // Respect the alpha of the inherited Graphic color.
            Color outlineTint = borderColor;
            outlineTint.a *= color.a;

            // First eight vertices: outer border.
            for (int i = 0; i < outer.Length; i++)
            {
                vh.AddVert(
                    outer[i],
                    outlineTint,
                    Vector2.zero);
            }

            // Next eight vertices: inner fill.
            for (int i = 0; i < inner.Length; i++)
            {
                vh.AddVert(
                    inner[i],
                    color,
                    Vector2.zero);
            }

            // Draw the ring between the outer and inner polygons.
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;

                vh.AddTriangle(i, next, 8 + next);
                vh.AddTriangle(i, 8 + next, 8 + i);
            }

            // Fill the inside of the panel.
            for (int i = 1; i < 7; i++)
            {
                vh.AddTriangle(8, 8 + i, 8 + i + 1);
            }
        }

        private static Vector2[] BuildPoints(
            Rect rect,
            float tl,
            float tr,
            float br,
            float bl)
        {
            float left = rect.xMin;
            float right = rect.xMax;
            float bottom = rect.yMin;
            float top = rect.yMax;

            return new Vector2[]
            {
                new Vector2(left + bl, bottom),
                new Vector2(right - br, bottom),
                new Vector2(right, bottom + br),
                new Vector2(right, top - tr),
                new Vector2(right - tr, top),
                new Vector2(left + tl, top),
                new Vector2(left, top - tl),
                new Vector2(left, bottom + bl)
            };
        }

        private static void AddFilledPolygon(
            VertexHelper vh,
            Vector2[] points,
            Color32 tint)
        {
            for (int i = 0; i < points.Length; i++)
            {
                vh.AddVert(points[i], tint, Vector2.zero);
            }

            for (int i = 1; i < points.Length - 1; i++)
            {
                vh.AddTriangle(0, i, i + 1);
            }
        }

        private static void ClampCuts(
            ref float tl,
            ref float tr,
            ref float br,
            ref float bl,
            float width,
            float height)
        {
            float maxHorizontal = Mathf.Max(tl + tr, bl + br);
            float maxVertical = Mathf.Max(tl + bl, tr + br);

            float scale = 1f;

            if (maxHorizontal > 0f)
                scale = Mathf.Min(scale, width / maxHorizontal);

            if (maxVertical > 0f)
                scale = Mathf.Min(scale, height / maxVertical);

            scale = Mathf.Clamp01(scale);

            tl *= scale;
            tr *= scale;
            br *= scale;
            bl *= scale;
        }
    }
}
