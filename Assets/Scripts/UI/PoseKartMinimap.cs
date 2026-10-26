using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PoseKart.Race;
using PoseKart.Vehicle;
using PoseKart.AI;

namespace PoseKart.UI
{
    /// <summary>
    /// GTA / Asphalt-style Circular Dynamic Racing Minimap.
    /// Rotates and translates the track relative to the player's world position and heading.
    /// Encased in a sleek circular radar HUD with outer glowing ring, compass ticks, and dark radial backing.
    /// Clips all road geometry and icons neatly inside the circular viewport.
    /// The player icon stays at a fixed position slightly below the center, always pointing UP.
    /// Features smooth speed-based dynamic zoom, rotation/position smoothing, and live AI positions.
    /// Compatible with URP, 100% camera-free procedural UI mesh rendering.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class PoseKartMinimap : MaskableGraphic
    {
        public enum MinimapMode
        {
            DynamicPlayerCentric, // GTA / Asphalt style: player-facing top, track rotates & translates
            StaticNorthUp         // Legacy mode: whole track overview, north-up
        }

        public enum MinimapShape
        {
            CircularGTA,          // GTA style circular radar with ring border & compass marks
            RectangularAsphalt    // Asphalt style chamfered / rectangular panel
        }

        [Serializable]
        public class Path
        {
            public Vector3[] points;
            public bool closed;
        }

        [Header("Minimap Navigation & Shape Mode")]
        [Tooltip("DynamicPlayerCentric gives the GTA/Asphalt experience (camera-free, rotating track).")]
        [SerializeField] private MinimapMode mode = MinimapMode.DynamicPlayerCentric;
        [Tooltip("CircularGTA encases the minimap inside a circular radar disc with metallic/cyan ring like GTA.")]
        [SerializeField] private MinimapShape shape = MinimapShape.CircularGTA;

        [Header("Existing Race Data - Automatically Configured / Resolved")]
        [SerializeField] private Path[] roads;
        [SerializeField] private RaceParticipant[] racers;
        [SerializeField] private Vector2 minimum;
        [SerializeField] private Vector2 maximum;

        [Header("Player Tracking & References")]
        [SerializeField] private RaceParticipant playerRacer;
        [SerializeField] private CarController playerCar;

        [Header("Asphalt / GTA Dynamic Centering & Zoom")]
        [Tooltip("Vertical offset of the player icon from UI center as a fraction of map height. -0.16 places player comfortably below center inside the circle.")]
        [SerializeField, Range(-0.35f, 0.35f)] private float playerCenterOffsetY = -0.15f;
        [Tooltip("Camera view radius in world units at standstill / low speed.")]
        [SerializeField, Range(30f, 250f)] private float viewRadiusLowSpeed = 70f;
        [Tooltip("Camera view radius in world units at cruising / normal speed (~130 km/h).")]
        [SerializeField, Range(40f, 300f)] private float viewRadiusNormalSpeed = 100f;
        [Tooltip("Camera view radius in world units at max Nitro / boost (>175 km/h).")]
        [SerializeField, Range(60f, 450f)] private float viewRadiusHighSpeed = 145f;
        [Tooltip("How smoothly the map zoom transitions when accelerating/boosting.")]
        [SerializeField, Range(1f, 15f)] private float zoomSmoothing = 4.5f;

        [Header("Motion Smoothing (Anti-Jitter)")]
        [Tooltip("Smoothing factor for player world position on the minimap.")]
        [SerializeField, Range(2f, 30f)] private float positionSmoothing = 14f;
        [Tooltip("Smoothing factor for vehicle heading rotation on the minimap.")]
        [SerializeField, Range(2f, 30f)] private float rotationSmoothing = 12f;

        [Header("GTA Circular Frame & Styling")]
        [SerializeField] private bool showDarkBackground = true;
        [SerializeField] private Color backgroundColor = new Color(0.012f, 0.024f, 0.045f, 0.90f);
        [SerializeField] private Color borderColor = new Color(0.20f, 0.85f, 0.98f, 0.90f); // Vibrant Cyan Border
        [SerializeField] private Color borderOuterGlowColor = new Color(0.05f, 0.40f, 0.65f, 0.45f); // Soft outer halo
        [SerializeField, Range(1.5f, 10f)] private float circularBorderWidth = 4f;
        [SerializeField] private bool showCompassCardinals = true;
        [SerializeField] private Color cardinalMarkColor = new Color(0.85f, 0.95f, 1f, 0.80f);

        [Header("Road & Icon Colors")]
        [SerializeField] private Color roadOutlineColor = new Color(0.01f, 0.03f, 0.07f, 1f);
        [SerializeField] private Color mainRoadColor = new Color(0.95f, 0.98f, 1f, 1f);
        [SerializeField] private Color sideRoadColor = new Color(1f, 0.82f, 0.20f, 0.95f); // Gold shortcut branch
        [SerializeField] private Color playerColor = new Color(0.05f, 0.92f, 1f, 1f);
        [SerializeField] private Color markerOutlineColor = new Color(0.012f, 0.024f, 0.045f, 1f);
        [SerializeField]
        private Color[] aiColors =
{
    new Color(1f, 0.80f, 0.16f),  // AI 1 - Gold
    new Color(1f, 0.30f, 0.35f),  // AI 2 - Red
    new Color(0.37f, 1f, 0.55f),  // AI 3 - Green
    new Color(0.35f, 0.65f, 1f),  // AI 4 - Blue
    new Color(0.85f, 0.35f, 1f),  // AI 5 - Purple
    new Color(1f, 0.45f, 0.85f),  // AI 6 - Pink
    new Color(1f, 0.55f, 0.18f)   // AI 7 - Orange
};

        [Header("Map Readability & Stroke Thickness")]
        [SerializeField, Range(0f, 48f)] private float padding = 12f;
        [SerializeField, Range(2f, 20f)] private float mainRoadWidth = 9.5f;
        [SerializeField, Range(1f, 16f)] private float sideRoadWidth = 6.5f;
        [SerializeField, Range(3f, 18f)] private float aiMarkerRadius = 7f;
        [SerializeField, Range(5f, 24f)] private float playerMarkerSize = 13.5f;
        [SerializeField, Range(0.5f, 2.5f)] private float mapZoom = 1f;
        [SerializeField] private bool showFinishLine = true;
        [SerializeField] private bool showSecondaryRoads = true;

        [Header("Culling & Geometry")]
        [Tooltip("Cull road segments further than this multiplier times the current view radius.")]
        [SerializeField, Range(1.1f, 3.0f)] private float cullingDistanceMultiplier = 1.75f;
        [SerializeField, Range(10f, 60f)] private float markerRefreshRate = 60f;

        // Runtime smoothed state
        private Vector2 smoothedPlayerPos;
        private float smoothedHeadingAngle;
        private float currentViewRadius;
        private bool isTrackingInitialized;
        private float nextMarkerRefresh;

        private const int MaxVertices = 65000;
        private const float MinSegmentPixelsSqr = 0.5f * 0.5f;
        private const int CircleSegments = 64;

        public MinimapMode ActiveMode
        {
            get => mode;
            set { mode = value; SetVerticesDirty(); }
        }

        public MinimapShape ActiveShape
        {
            get => shape;
            set { shape = value; SetVerticesDirty(); }
        }

        public void Configure(Path[] paths, RaceParticipant[] participants)
        {
            roads = paths;
            racers = participants == null ? null : (RaceParticipant[])participants.Clone();
            raycastTarget = false;

            CalculateStaticBounds();
            ResolvePlayer();

            if (racers != null)
            {
                Array.Sort(racers, (a, b) => string.CompareOrdinal(
                    a == null ? "" : a.name,
                    b == null ? "" : b.name));
            }

            SetVerticesDirty();
        }
        public void ConfigureRuntimeRacers(
    RaceParticipant[] participants)
        {
            racers =
                participants == null
                    ? null
                    : (RaceParticipant[])participants.Clone();

            // Xóa reference player offline cũ.
            playerRacer = null;
            playerCar = null;

            if (racers != null)
            {
                Array.Sort(
                    racers,
                    (a, b) => string.CompareOrdinal(
                        a == null ? "" : a.name,
                        b == null ? "" : b.name
                    )
                );
            }

            // Tìm lại đúng local player
            // từ IsPlayer của runtime roster.
            ResolvePlayer();

            isTrackingInitialized = false;

            Debug.Log(
                $"[Minimap] Runtime racers configured | " +
                $"Racers={(racers == null ? 0 : racers.Length)} | " +
                $"Player={(playerRacer != null ? playerRacer.RacerName : "NONE")}",
                this
            );

            SetVerticesDirty();
        }
        private void CalculateStaticBounds()
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            if (roads != null)
            {
                foreach (Path path in roads)
                {
                    if (path == null || path.points == null) continue;
                    foreach (Vector3 p in path.points)
                    {
                        Vector2 point = new Vector2(p.x, p.z);
                        minimum = Vector2.Min(minimum, point);
                        maximum = Vector2.Max(maximum, point);
                    }
                }
            }

            if (float.IsInfinity(minimum.x) || float.IsInfinity(maximum.x))
            {
                minimum = Vector2.zero;
                maximum = Vector2.one;
            }
        }

        private void ResolvePlayer()
        {
            if (playerRacer != null && playerCar != null) return;

            if (racers != null && racers.Length > 0)
            {
                for (int i = 0; i < racers.Length; i++)
                {
                    if (racers[i] != null && racers[i].IsPlayer)
                    {
                        playerRacer = racers[i];
                        break;
                    }
                }
            }

            if (playerRacer == null)
            {
                RaceParticipant[] allParticipants = FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None);
                if (allParticipants != null)
                {
                    for (int i = 0; i < allParticipants.Length; i++)
                    {
                        if (allParticipants[i] != null && allParticipants[i].IsPlayer)
                        {
                            playerRacer = allParticipants[i];
                            break;
                        }
                    }
                }
            }

            if (playerRacer != null && playerCar == null)
            {
                playerCar =
                    playerRacer.CarController ??
                    playerRacer.GetComponent<CarController>() ??
                    playerRacer.GetComponentInParent<CarController>();
            }
        }

        private void AutoPopulateRoadsIfEmpty()
        {
            if (roads != null && roads.Length > 0) return;

            // 1. Try finding RacingLine waypoints
            RacingLine racingLine = FindFirstObjectByType<RacingLine>();
            if (racingLine != null && racingLine.Count >= 3)
            {
                Vector3[] linePoints = new Vector3[racingLine.Count];
                for (int i = 0; i < racingLine.Count; i++)
                {
                    linePoints[i] = racingLine.GetPosition(i);
                }
                roads = new Path[]
                {
                    new Path { points = linePoints, closed = true }
                };
                CalculateStaticBounds();
                return;
            }

            // 2. Try finding CheckpointManager
            CheckpointManager cpManager = FindFirstObjectByType<CheckpointManager>();
            if (cpManager != null && cpManager.CheckpointCount >= 3)
            {
                Vector3[] cpPoints = new Vector3[cpManager.CheckpointCount];
                for (int i = 0; i < cpManager.CheckpointCount; i++)
                {
                    Checkpoint cp = cpManager.GetCheckpoint(i);
                    cpPoints[i] = cp != null ? cp.transform.position : Vector3.zero;
                }
                roads = new Path[]
                {
                    new Path { points = cpPoints, closed = true }
                };
                CalculateStaticBounds();
            }
        }

        private void AutoPopulateRacersIfEmpty()
        {
            RaceParticipant[] foundRacers =
                FindObjectsByType<RaceParticipant>(
                    FindObjectsSortMode.None);

            if (foundRacers == null || foundRacers.Length == 0)
                return;

            racers = foundRacers;

            Array.Sort(racers, (a, b) => string.CompareOrdinal(
                a == null ? "" : a.name,
                b == null ? "" : b.name));

            ResolvePlayer();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            ResolvePlayer();
            AutoPopulateRoadsIfEmpty();
            AutoPopulateRacersIfEmpty();
            isTrackingInitialized = false;
            currentViewRadius = viewRadiusNormalSpeed;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            if (racers == null || racers.Length == 0)
            {
                AutoPopulateRacersIfEmpty();
            }

            if (roads == null || roads.Length == 0)
            {
                AutoPopulateRoadsIfEmpty();
            }

            if (playerRacer == null)
            {
                ResolvePlayer();
            }

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);

            if (mode == MinimapMode.DynamicPlayerCentric && playerRacer != null)
            {
                Vector3 rawPos = playerRacer.transform.position;
                Vector2 targetPos2D = new Vector2(rawPos.x, rawPos.z);

                Vector3 forward3D = playerRacer.transform.forward;
                Vector2 fwd2D = new Vector2(forward3D.x, forward3D.z);
                float targetAngle = fwd2D.sqrMagnitude > 0.001f
                    ? Mathf.Atan2(fwd2D.x, fwd2D.y) * Mathf.Rad2Deg
                    : 0f;

                // Speed-based dynamic zoom
                float currentSpeed = 0f;
                bool isBoosting = false;
                if (playerCar != null)
                {
                    currentSpeed = playerCar.ForwardSpeed;
                    isBoosting = playerCar.IsBoosting;
                }
                else if (playerRacer.Body != null)
                {
                    currentSpeed = playerRacer.Body.linearVelocity.magnitude;
                }

                float targetRadius = viewRadiusNormalSpeed;
                if (isBoosting || currentSpeed > 38f) // > 136 km/h
                {
                    float boostT = Mathf.Clamp01((currentSpeed - 36f) / 16f);
                    targetRadius = Mathf.Lerp(viewRadiusNormalSpeed, viewRadiusHighSpeed, Mathf.Max(boostT, isBoosting ? 0.7f : 0f));
                }
                else if (currentSpeed < 10f) // < 36 km/h
                {
                    float slowT = Mathf.Clamp01(currentSpeed / 10f);
                    targetRadius = Mathf.Lerp(viewRadiusLowSpeed, viewRadiusNormalSpeed, slowT);
                }

                if (!isTrackingInitialized)
                {
                    smoothedPlayerPos = targetPos2D;
                    smoothedHeadingAngle = targetAngle;
                    currentViewRadius = targetRadius;
                    isTrackingInitialized = true;
                }
                else
                {
                    float posT = 1f - Mathf.Exp(-positionSmoothing * dt);
                    smoothedPlayerPos = Vector2.Lerp(smoothedPlayerPos, targetPos2D, posT);

                    float rotT = 1f - Mathf.Exp(-rotationSmoothing * dt);
                    smoothedHeadingAngle = Mathf.LerpAngle(smoothedHeadingAngle, targetAngle, rotT);

                    float zoomT = 1f - Mathf.Exp(-zoomSmoothing * dt);
                    currentViewRadius = Mathf.Lerp(currentViewRadius, targetRadius, zoomT);
                }

                // Re-render minimap geometry every frame for ultra-smooth 60+ FPS dynamic tracking
                SetVerticesDirty();
            }
            else
            {
                if (Time.unscaledTime >= nextMarkerRefresh)
                {
                    nextMarkerRefresh = Time.unscaledTime + 1f / Mathf.Max(5f, markerRefreshRate);
                    SetVerticesDirty();
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            Vector2 uiCenter = rect.center;
            float halfDimension = Mathf.Min(rect.width, rect.height) * 0.5f;
            float circleRadius = Mathf.Max(10f, halfDimension - padding);

            // 1. Draw GTA Circular Background or Rectangular Panel
            if (showDarkBackground)
            {
                if (shape == MinimapShape.CircularGTA)
                {
                    // Dark radial disc interior
                    Disc(vh, uiCenter, circleRadius, backgroundColor);
                }
                else
                {
                    Quad(vh, rect.xMin, rect.yMin, rect.xMax, rect.yMax, backgroundColor);
                    float border = 1.25f;
                    Line(vh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), border, borderColor);
                    Line(vh, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), border, borderColor);
                    Line(vh, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), border, borderColor);
                    Line(vh, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), border, borderColor);
                }
            }

            // 2. Populate Track & Racers
            if (mode == MinimapMode.DynamicPlayerCentric && (playerRacer != null || isTrackingInitialized))
            {
                PopulateDynamicMinimap(vh, rect, uiCenter, circleRadius);
            }
            else
            {
                PopulateStaticMinimap(vh, rect, uiCenter, circleRadius);
            }

            // 3. Draw GTA Circular Frame on top (Inner Ring, Outer Ring, Glowing Edge & Compass Cardinals)
            if (shape == MinimapShape.CircularGTA)
            {
                float bWidth = circularBorderWidth;
                // Outer subtle glow halo
                Ring(vh, uiCenter, circleRadius + bWidth * 0.5f, circleRadius + bWidth * 1.5f, borderOuterGlowColor);
                // Main vibrant border ring
                Ring(vh, uiCenter, circleRadius - bWidth * 0.5f, circleRadius + bWidth * 0.5f, borderColor);
                // Thin inner rim
                Ring(vh, uiCenter, circleRadius - bWidth * 0.8f, circleRadius - bWidth * 0.5f, new Color(1f, 1f, 1f, 0.45f));

                if (showCompassCardinals)
                {
                    DrawGTACompassMarks(vh, uiCenter, circleRadius, bWidth);
                }
            }
        }

        private void DrawGTACompassMarks(VertexHelper vh, Vector2 center, float radius, float borderWidth)
        {
            // Cardinal tick marks at North, East, South, West (relative to player heading in dynamic mode)
            float tickLen = 6f;
            float tickWidth = 2.2f;

            // In dynamic mode, true North rotates around the dial:
            float northAngleRad = (-smoothedHeadingAngle + 90f) * Mathf.Deg2Rad;

            for (int i = 0; i < 4; i++)
            {
                float angle = northAngleRad + i * (Mathf.PI * 0.5f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 pOuter = center + dir * (radius - 1f);
                Vector2 pInner = center + dir * (radius - 1f - (i == 0 ? tickLen * 1.4f : tickLen));

                // North tick is highlighted red/cyan, others white/grey
                Color col = (i == 0) ? new Color(1f, 0.28f, 0.32f, 0.95f) : cardinalMarkColor;
                Line(vh, pInner, pOuter, i == 0 ? tickWidth * 1.3f : tickWidth, col);
            }
        }

        #region Dynamic Minimap Rendering (GTA / Asphalt Player-Centric)

        private void PopulateDynamicMinimap(VertexHelper vh, Rect rect, Vector2 uiCenter, float circleRadius)
        {
            // Player is placed slightly below center so upcoming track in front is broad & clear
            Vector2 playerUIPos = uiCenter + new Vector2(0f, rect.height * playerCenterOffsetY);

            float effectiveWorldRadius = Mathf.Max(10f, currentViewRadius / Mathf.Max(0.1f, mapZoom));
            float pixelsPerWorldUnit = circleRadius / effectiveWorldRadius;

            float scale = Mathf.Clamp(Mathf.Min(rect.width, rect.height) / 260f, 0.85f, 1.8f);
            float mainWidth = mainRoadWidth * scale;
            float secondaryWidth = sideRoadWidth * scale;

            float headingRad = smoothedHeadingAngle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(headingRad);
            float sin = Mathf.Sin(headingRad);

            float cullDistSqr = (effectiveWorldRadius * cullingDistanceMultiplier) * (effectiveWorldRadius * cullingDistanceMultiplier);
            float clipRadiusSqr = (circleRadius - 1.5f) * (circleRadius - 1.5f);

            // Draw roads
            if (roads != null && roads.Length > 0)
            {
                // Pass 1: Outlines
                for (int k = 0; k < roads.Length; k++)
                {
                    if (k > 0 && !showSecondaryRoads) break;
                    float width = (k == 0 ? mainWidth + 3.8f * scale : secondaryWidth + 2.8f * scale);
                    DrawDynamicPath(vh, roads[k], width, roadOutlineColor, smoothedPlayerPos, cos, sin, playerUIPos,
                        pixelsPerWorldUnit, cullDistSqr, uiCenter, clipRadiusSqr, shape == MinimapShape.CircularGTA);
                }

                // Pass 2: Main route (white) and secondary/shortcuts (gold)
                for (int k = 0; k < roads.Length; k++)
                {
                    if (k > 0 && !showSecondaryRoads) break;
                    float width = k == 0 ? mainWidth : secondaryWidth;
                    Color color = k == 0 ? mainRoadColor : sideRoadColor;
                    DrawDynamicPath(vh, roads[k], width, color, smoothedPlayerPos, cos, sin, playerUIPos,
                        pixelsPerWorldUnit, cullDistSqr, uiCenter, clipRadiusSqr, shape == MinimapShape.CircularGTA);
                }

                // Finish line marker on road 0
                if (showFinishLine && roads[0] != null && roads[0].points != null && roads[0].points.Length > 0)
                {
                    Vector2 finishWorld = new Vector2(roads[0].points[0].x, roads[0].points[0].z);
                    if ((finishWorld - smoothedPlayerPos).sqrMagnitude <= cullDistSqr)
                    {
                        Vector2 finishUI = ProjectDynamic(finishWorld, smoothedPlayerPos, cos, sin, playerUIPos, pixelsPerWorldUnit);
                        if (shape != MinimapShape.CircularGTA || (finishUI - uiCenter).sqrMagnitude <= clipRadiusSqr)
                        {
                            float s = 3.2f * scale;
                            for (int y = 0; y < 3; y++)
                                for (int x = 0; x < 4; x++)
                                {
                                    Vector2 tile = finishUI + new Vector2((x - 1.5f) * s, (y - 1f) * s);
                                    Square(vh, tile, s, ((x + y) & 1) == 0 ? Color.white : Color.black);
                                }
                        }
                    }
                }
            }

            // Draw AI racers
            if (racers != null && racers.Length > 0)
            {
                int aiIndex = 0;
                for (int i = 0; i < racers.Length; i++)
                {
                    RaceParticipant racer = racers[i];
                    if (racer == null || racer.IsPlayer) continue;

                    Vector3 racerPos = racer.transform.position;
                    Vector2 racerWorld = new Vector2(racerPos.x, racerPos.z);
                    Vector2 aiUIPos = ProjectDynamic(racerWorld, smoothedPlayerPos, cos, sin, playerUIPos, pixelsPerWorldUnit);

                    // Clamp/clip to circular frame border like GTA blips when far away
                    if (shape == MinimapShape.CircularGTA)
                    {
                        Vector2 fromCenter = aiUIPos - uiCenter;
                        float dist = fromCenter.magnitude;
                        float maxBlipRadius = circleRadius - 9f * scale;
                        if (dist > maxBlipRadius)
                        {
                            aiUIPos = uiCenter + (fromCenter / Mathf.Max(0.001f, dist)) * maxBlipRadius;
                        }
                    }

                    Vector3 aiFwd3D = racer.transform.forward;
                    Vector2 aiFwd2D = new Vector2(aiFwd3D.x, aiFwd3D.z);
                    Vector2 aiRelativeFwd = RotatePoint(aiFwd2D, cos, sin).normalized;

                    float r = aiMarkerRadius * scale;
                    Color aiColor = aiColors != null && aiColors.Length > 0
                        ? aiColors[aiIndex % aiColors.Length]
                        : new Color(1f, 0.7f, 0.2f);

                    // Draw distinctive AI icon: circle with directional arrowhead
                    Dot(vh, aiUIPos, r + 2.5f * scale, markerOutlineColor);
                    Dot(vh, aiUIPos, r, aiColor);
                    if (aiRelativeFwd.sqrMagnitude > 0.001f)
                    {
                        Arrow(vh, aiUIPos, aiRelativeFwd, r * 1.35f, Color.white);
                    }

                    aiIndex++;
                }
            }

            // Draw Player Icon: Fixed position, always pointing straight up (Vector2.up)
            float pSize = playerMarkerSize * scale;
            Arrow(vh, playerUIPos, Vector2.up, pSize + 3f * scale, markerOutlineColor);
            Arrow(vh, playerUIPos, Vector2.up, pSize, playerColor);
            Dot(vh, playerUIPos - Vector2.up * (pSize * 0.2f), 3f * scale, Color.white);
        }

        private static Vector2 RotatePoint(Vector2 p, float cos, float sin)
        {
            return new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
        }

        private static Vector2 ProjectDynamic(Vector2 worldPos, Vector2 playerWorldPos, float cos, float sin, Vector2 playerUIPos, float scale)
        {
            Vector2 delta = worldPos - playerWorldPos;
            Vector2 rotated = RotatePoint(delta, cos, sin);
            return playerUIPos + rotated * scale;
        }

        private static void DrawDynamicPath(VertexHelper vh, Path path, float width, Color color,
            Vector2 playerWorldPos, float cos, float sin, Vector2 playerUIPos, float scale, float cullDistSqr,
            Vector2 mapCenter, float clipRadiusSqr, bool useCircleClipping)
        {
            if (path == null || path.points == null || path.points.Length < 2) return;

            Vector3[] points = path.points;
            int count = points.Length;

            Vector2? previousUIPos = null;
            Vector2? firstUIPos = null;

            for (int i = 0; i < count; i++)
            {
                Vector2 worldPoint = new Vector2(points[i].x, points[i].z);
                bool withinRange = (worldPoint - playerWorldPos).sqrMagnitude <= cullDistSqr;

                Vector2 currentUIPos = ProjectDynamic(worldPoint, playerWorldPos, cos, sin, playerUIPos, scale);

                if (i == 0)
                {
                    firstUIPos = currentUIPos;
                }

                if (previousUIPos.HasValue)
                {
                    if (withinRange || (i > 0 && (new Vector2(points[i - 1].x, points[i - 1].z) - playerWorldPos).sqrMagnitude <= cullDistSqr))
                    {
                        if ((currentUIPos - previousUIPos.Value).sqrMagnitude >= MinSegmentPixelsSqr || i == count - 1)
                        {
                            Vector2 a = previousUIPos.Value;
                            Vector2 b = currentUIPos;

                            if (useCircleClipping)
                            {
                                bool aInside = (a - mapCenter).sqrMagnitude <= clipRadiusSqr;
                                bool bInside = (b - mapCenter).sqrMagnitude <= clipRadiusSqr;

                                if (aInside && bInside)
                                {
                                    Line(vh, a, b, width, color);
                                    Dot(vh, a, width * 0.5f, color);
                                }
                                else if (aInside || bInside)
                                {
                                    // Clip segment against the circle boundary
                                    if (ClipSegmentToCircle(ref a, ref b, mapCenter, Mathf.Sqrt(clipRadiusSqr)))
                                    {
                                        Line(vh, a, b, width, color);
                                    }
                                }
                            }
                            else
                            {
                                Line(vh, a, b, width, color);
                                Dot(vh, a, width * 0.5f, color);
                            }
                        }
                    }
                }

                previousUIPos = currentUIPos;
            }

            if (path.closed && previousUIPos.HasValue && firstUIPos.HasValue)
            {
                Vector2 a = previousUIPos.Value;
                Vector2 b = firstUIPos.Value;
                if (useCircleClipping)
                {
                    if (ClipSegmentToCircle(ref a, ref b, mapCenter, Mathf.Sqrt(clipRadiusSqr)))
                    {
                        Line(vh, a, b, width, color);
                    }
                }
                else
                {
                    Line(vh, a, b, width, color);
                    Dot(vh, a, width * 0.5f, color);
                    Dot(vh, b, width * 0.5f, color);
                }
            }
        }

        private static bool ClipSegmentToCircle(ref Vector2 p0, ref Vector2 p1, Vector2 center, float radius)
        {
            Vector2 d = p1 - p0;
            Vector2 f = p0 - center;

            float a = Vector2.Dot(d, d);
            float b = 2f * Vector2.Dot(f, d);
            float c = Vector2.Dot(f, f) - radius * radius;

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f || a < 0.0001f)
            {
                return false;
            }

            discriminant = Mathf.Sqrt(discriminant);
            float t1 = (-b - discriminant) / (2f * a);
            float t2 = (-b + discriminant) / (2f * a);

            float tStart = Mathf.Max(0f, Mathf.Min(t1, t2));
            float tEnd = Mathf.Min(1f, Mathf.Max(t1, t2));

            if (tStart > tEnd) return false;

            Vector2 origP0 = p0;
            p0 = origP0 + d * tStart;
            p1 = origP0 + d * tEnd;
            return true;
        }

        #endregion

        #region Static North-Up Minimap Rendering (Fallback / Overview)

        private void PopulateStaticMinimap(VertexHelper vh, Rect rect, Vector2 uiCenter, float circleRadius)
        {
            if (roads == null || roads.Length == 0) return;

            Vector2 extent = maximum - minimum;
            extent.x = Mathf.Max(extent.x, 0.01f);
            extent.y = Mathf.Max(extent.y, 0.01f);
            float availableWidth = Mathf.Max(1f, rect.width - padding * 2f);
            float availableHeight = Mathf.Max(1f, rect.height - padding * 2f);
            float pixelsPerWorldUnit = Mathf.Min(availableWidth / extent.x, availableHeight / extent.y) * mapZoom;
            Vector2 worldCenter = (minimum + maximum) * 0.5f;

            float scale = Mathf.Clamp(Mathf.Min(rect.width, rect.height) / 260f, 0.85f, 1.7f);
            float mainWidth = mainRoadWidth * scale;
            float secondaryWidth = sideRoadWidth * scale;

            for (int k = 0; k < roads.Length; k++)
            {
                if (k > 0 && !showSecondaryRoads) break;
                DrawStaticPath(vh, roads[k], k == 0 ? mainWidth + 4f * scale : secondaryWidth + 3f * scale,
                    roadOutlineColor, worldCenter, uiCenter, pixelsPerWorldUnit);
            }
            for (int k = 0; k < roads.Length; k++)
            {
                if (k > 0 && !showSecondaryRoads) break;
                DrawStaticPath(vh, roads[k], k == 0 ? mainWidth : secondaryWidth,
                    k == 0 ? mainRoadColor : sideRoadColor,
                    worldCenter, uiCenter, pixelsPerWorldUnit);
            }

            if (showFinishLine && roads[0] != null && roads[0].points != null && roads[0].points.Length > 0)
            {
                Vector2 finish = ProjectStatic(roads[0].points[0], worldCenter, uiCenter, pixelsPerWorldUnit);
                float s = 3f * scale;
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        Vector2 tile = finish + new Vector2((x - 1.5f) * s, (y - 1f) * s);
                        Square(vh, tile, s, ((x + y) & 1) == 0 ? Color.white : Color.black);
                    }
            }

            if (racers == null) return;

            int ai = 0;
            foreach (RaceParticipant racer in racers)
            {
                if (racer == null || racer.IsPlayer) continue;
                Vector2 point = ProjectStatic(racer.transform.position, worldCenter, uiCenter, pixelsPerWorldUnit);
                float r = aiMarkerRadius * scale;
                Dot(vh, point, r + 2.5f * scale, markerOutlineColor);
                Color aiColor = aiColors != null && aiColors.Length > 0
                    ? aiColors[ai % aiColors.Length]
                    : new Color(1f, 0.7f, 0.2f);
                Dot(vh, point, r, aiColor);
                ai++;
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null || !racer.IsPlayer) continue;
                Vector2 point = ProjectStatic(racer.transform.position, worldCenter, uiCenter, pixelsPerWorldUnit);
                Vector3 facing3D = racer.transform.forward;
                Vector2 forward = new Vector2(facing3D.x, facing3D.z);
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.up;
                Arrow(vh, point, forward, (playerMarkerSize + 2.5f) * scale, markerOutlineColor);
                Arrow(vh, point, forward, playerMarkerSize * scale, playerColor);
            }
        }

        private static Vector2 ProjectStatic(Vector3 world, Vector2 worldCenter, Vector2 uiCenter, float scale)
        {
            return uiCenter + (new Vector2(world.x, world.z) - worldCenter) * scale;
        }

        private static void DrawStaticPath(VertexHelper vh, Path path, float width, Color color,
            Vector2 worldCenter, Vector2 uiCenter, float scale)
        {
            if (path == null || path.points == null || path.points.Length < 2) return;
            Vector2 first = ProjectStatic(path.points[0], worldCenter, uiCenter, scale);
            Vector2 previous = first;
            int count = path.points.Length;

            for (int i = 1; i < count; i++)
            {
                Vector2 current = ProjectStatic(path.points[i], worldCenter, uiCenter, scale);
                if ((current - previous).sqrMagnitude < MinSegmentPixelsSqr && i < count - 1) continue;

                Line(vh, previous, current, width, color);
                Dot(vh, previous, width * 0.5f, color);
                previous = current;
            }

            if (path.closed)
            {
                Line(vh, previous, first, width, color);
            }
            Dot(vh, previous, width * 0.5f, color);
            Dot(vh, first, width * 0.5f, color);
        }

        #endregion

        #region Mesh Primitives

        private static void Quad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color color)
        {
            if (vh.currentVertCount + 4 >= MaxVertices) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector2(xMin, yMin), color, Vector2.zero);
            vh.AddVert(new Vector2(xMin, yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(xMax, yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(xMax, yMin), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            if (vh.currentVertCount + 4 >= MaxVertices || (b - a).sqrMagnitude < 0.000001f) return;
            Vector2 delta = (b - a).normalized;
            Vector2 normal = new Vector2(-delta.y, delta.x) * width * 0.5f;
            int i = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero);
            vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero);
            vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Dot(VertexHelper vh, Vector2 point, float radius, Color color)
        {
            const int segments = 16;
            if (vh.currentVertCount + segments + 1 >= MaxVertices) return;
            int first = vh.currentVertCount;
            vh.AddVert(point, color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vh.AddVert(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                vh.AddTriangle(first, first + 1 + i, first + 1 + ((i + 1) % segments));
            }
        }

        private static void Disc(VertexHelper vh, Vector2 center, float radius, Color color)
        {
            if (vh.currentVertCount + CircleSegments + 1 >= MaxVertices) return;
            int first = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            }
            for (int i = 0; i < CircleSegments; i++)
            {
                vh.AddTriangle(first, first + 1 + i, first + 1 + ((i + 1) % CircleSegments));
            }
        }

        private static void Ring(VertexHelper vh, Vector2 center, float innerRadius, float outerRadius, Color color)
        {
            if (vh.currentVertCount + CircleSegments * 2 >= MaxVertices) return;
            int start = vh.currentVertCount;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vh.AddVert(center + new Vector2(cos, sin) * innerRadius, color, Vector2.zero);
                vh.AddVert(center + new Vector2(cos, sin) * outerRadius, color, Vector2.zero);
            }
            for (int i = 0; i < CircleSegments; i++)
            {
                int next = (i + 1) % CircleSegments;
                int i0 = start + i * 2;
                int i1 = start + i * 2 + 1;
                int n0 = start + next * 2;
                int n1 = start + next * 2 + 1;

                vh.AddTriangle(i0, i1, n1);
                vh.AddTriangle(i0, n1, n0);
            }
        }

        private static void Square(VertexHelper vh, Vector2 center, float size, Color color)
        {
            float h = size * 0.5f;
            Quad(vh, center.x - h, center.y - h, center.x + h, center.y + h, color);
        }

        private static void Arrow(VertexHelper vh, Vector2 center, Vector2 forward, float size, Color color)
        {
            if (vh.currentVertCount + 4 >= MaxVertices) return;
            Vector2 right = new Vector2(forward.y, -forward.x);
            int i = vh.currentVertCount;
            vh.AddVert(center + forward * size, color, Vector2.zero);
            vh.AddVert(center - forward * size * 0.6f + right * size * 0.62f, color, Vector2.zero);
            vh.AddVert(center - forward * size * 0.24f, color, Vector2.zero);
            vh.AddVert(center - forward * size * 0.6f - right * size * 0.62f, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        #endregion
    }
}
