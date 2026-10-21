using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Input
{
    /// <summary>
    /// HUD Debug thời gian thực hiển thị trạng thái kết nối UDP, góc vô lăng ảo,
    /// cử chỉ nhận diện, Nitro, Item, và Drift Assist.
    /// Có thể bật/tắt bằng phím F1. Đặt ở góc màn hình không che khuất tầm nhìn lái xe.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartHandControlDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PoseKartHandUdpReceiver receiver;
        [SerializeField] private PoseKartHandInputSwitcher switcher;
        [SerializeField] private PoseKartHandInputProvider handProvider;

        [Header("Display Settings")]
        [SerializeField] private bool showHud = true;
        [SerializeField] private bool showWhenInKeyboardMode = true;
        [SerializeField] private Key toggleKey = Key.F1;
        [Tooltip("Vị trí neo trên màn hình (chọn LowerLeft, LowerCenter, hoặc LowerRight để nằm xuống dưới)")]
        [SerializeField] private TextAnchor screenPosition = TextAnchor.LowerLeft;
        [Tooltip("Dịch chuyển vị trí bù trừ (X: ngang, Y: dọc)")]
        [SerializeField] private Vector2 customOffset = Vector2.zero;
        [Tooltip("Cho phép kéo thả HUD bằng chuột khi đang trong Play Mode")]
        [SerializeField] private bool draggable = true;
        [SerializeField, Range(0.5f, 1.5f)] private float uiScale = 1.0f;

        // Visual flash timers for discrete events
        private float nitroFlashTimer = 0f;
        private float itemFlashTimer = 0f;
        private int lastRecordedNitroId = 0;
        private int lastRecordedItemId = 0;

        // GUI Styles and Textures (cached)
        private GUIStyle boxStyle;
        private GUIStyle headerStyle;
        private GUIStyle labelStyle;
        private GUIStyle badgeStyle;
        private Texture2D bgTexture;
        private Texture2D greenTexture;
        private Texture2D redTexture;
        private Texture2D yellowTexture;
        private Texture2D blueTexture;
        private Texture2D orangeTexture;
        private Texture2D whiteTexture;

        public bool ShowHud
        {
            get => showHud;
            set => showHud = value;
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (receiver == null) receiver = FindFirstObjectByType<PoseKartHandUdpReceiver>();
            if (switcher == null) switcher = FindFirstObjectByType<PoseKartHandInputSwitcher>();
            if (handProvider == null) handProvider = FindFirstObjectByType<PoseKartHandInputProvider>();
        }

        private void Update()
        {
            if (receiver == null || switcher == null || handProvider == null)
            {
                ResolveReferences();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[toggleKey].wasPressedThisFrame)
                {
                    showHud = !showHud;
                }
            }

            if (receiver != null)
            {
                if (receiver.NitroPressId > lastRecordedNitroId)
                {
                    lastRecordedNitroId = receiver.NitroPressId;
                    nitroFlashTimer = 0.5f;
                }
                if (receiver.ItemPressId > lastRecordedItemId)
                {
                    lastRecordedItemId = receiver.ItemPressId;
                    itemFlashTimer = 0.5f;
                }
            }

            if (nitroFlashTimer > 0f) nitroFlashTimer -= Time.deltaTime;
            if (itemFlashTimer > 0f) itemFlashTimer -= Time.deltaTime;
        }

        private void OnGUI()
        {
            if (!showHud) return;

            PoseKartInputMode activeMode = switcher != null ? switcher.CurrentMode : PoseKartHandInputSwitcher.GlobalMode;
            if (!showWhenInKeyboardMode && activeMode == PoseKartInputMode.Keyboard) return;

            InitTexturesAndStyles();

            float originalWidth = 320f;
            float originalHeight = 240f;
            float padding = 12f;

            float scaledWidth = originalWidth * uiScale;
            float scaledHeight = originalHeight * uiScale;

            float x = padding;
            float y = padding;

            switch (screenPosition)
            {
                case TextAnchor.UpperLeft:
                    x = padding;
                    y = padding;
                    break;
                case TextAnchor.UpperCenter:
                    x = (Screen.width - scaledWidth) * 0.5f;
                    y = padding;
                    break;
                case TextAnchor.UpperRight:
                    x = Screen.width - scaledWidth - padding;
                    y = padding;
                    break;
                case TextAnchor.MiddleLeft:
                    x = padding;
                    y = (Screen.height - scaledHeight) * 0.5f;
                    break;
                case TextAnchor.MiddleCenter:
                    x = (Screen.width - scaledWidth) * 0.5f;
                    y = (Screen.height - scaledHeight) * 0.5f;
                    break;
                case TextAnchor.MiddleRight:
                    x = Screen.width - scaledWidth - padding;
                    y = (Screen.height - scaledHeight) * 0.5f;
                    break;
                case TextAnchor.LowerLeft:
                    x = padding;
                    y = Screen.height - scaledHeight - padding;
                    break;
                case TextAnchor.LowerCenter:
                    x = (Screen.width - scaledWidth) * 0.5f;
                    y = Screen.height - scaledHeight - padding;
                    break;
                case TextAnchor.LowerRight:
                    x = Screen.width - scaledWidth - padding;
                    y = Screen.height - scaledHeight - padding;
                    break;
            }

            x += customOffset.x;
            y += customOffset.y;

            Rect windowRect = new Rect(x, y, scaledWidth, scaledHeight);
            if (draggable)
            {
                HandleDragging(windowRect);
            }

            GUI.matrix = Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.identity, new Vector3(uiScale, uiScale, 1));
            Rect localRect = new Rect(0, 0, originalWidth, originalHeight);

            // Background panel
            GUI.Box(localRect, GUIContent.none, boxStyle);

            GUILayout.BeginArea(new Rect(10, 10, originalWidth - 20, originalHeight - 20));

            // Header: Title & Control Mode
            GUILayout.BeginHorizontal();
            GUILayout.Label("POSEKART CONTROL HUD", headerStyle);
            GUILayout.FlexibleSpace();
            string modeName = activeMode == PoseKartInputMode.HandControl ? "HAND [F2]" : "KEYBOARD [F2]";
            GUI.color = activeMode == PoseKartInputMode.HandControl ? new Color(0.3f, 0.9f, 1f) : new Color(0.9f, 0.9f, 0.9f);
            GUILayout.Label(modeName, badgeStyle);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // UDP & Tracking Connection Status
            bool connected = receiver != null && receiver.IsConnected;
            bool tracking = receiver != null && receiver.IsTracking;
            float confidence = receiver != null ? receiver.Confidence : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Trạng thái:", labelStyle, GUILayout.Width(80));

            if (!connected)
            {
                DrawBadge("CHƯA KẾT NỐI UDP", redTexture, Color.white);
            }
            else if (!tracking)
            {
                DrawBadge("MẤT DẤU TAY", yellowTexture, Color.black);
            }
            else
            {
                string confText = $"TRACKING OK ({Mathf.RoundToInt(confidence * 100)}%)";
                DrawBadge(confText, greenTexture, Color.black);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Steering Gauge
            float steering = receiver != null && tracking ? receiver.Steering : 0f;
            float angleDeg = receiver != null && tracking ? receiver.WheelAngleDeg : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Góc lái: {angleDeg:+0.0;-0.0;0.0}°", labelStyle, GUILayout.Width(110));
            GUILayout.Label($"[{steering:+0.00;-0.00;0.00}]", labelStyle);
            GUILayout.EndHorizontal();

            DrawSteeringGauge(steering);

            GUILayout.Space(6);

            // Throttle & Brake Gauges
            float throttle = receiver != null && tracking ? receiver.Throttle : 0f;
            float brake = receiver != null && tracking ? receiver.Brake : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Ga: {Mathf.RoundToInt(throttle * 100)}%", labelStyle, GUILayout.Width(75));
            DrawBar(throttle, greenTexture, 65, 12);
            GUILayout.Space(10);
            GUILayout.Label($"Phanh: {Mathf.RoundToInt(brake * 100)}%", labelStyle, GUILayout.Width(80));
            DrawBar(brake, redTexture, 65, 12);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Detected Gesture Label & Discrete Event Badges
            string gestureLabel = "Không có";
            if (tracking)
            {
                if (brake > 0.5f) gestureLabel = "PHANH (2 tay mở)";
                else if (nitroFlashTimer > 0f) gestureLabel = "NITRO (Tay phải mở)";
                else if (itemFlashTimer > 0f) gestureLabel = "DÙNG ITEM (Nắm tay phải)";
                else gestureLabel = "LÁI XE (Auto Throttle)";
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cử chỉ:", labelStyle, GUILayout.Width(50));
            GUILayout.Label(gestureLabel, labelStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Event Badges: Nitro, Item, Drift
            GUILayout.BeginHorizontal();
            if (nitroFlashTimer > 0f)
            {
                DrawBadge("★ NITRO!", blueTexture, Color.white);
            }
            if (itemFlashTimer > 0f)
            {
                DrawBadge("✦ ITEM!", orangeTexture, Color.white);
            }
            bool drift = handProvider != null && handProvider.Drift;
            if (drift)
            {
                DrawBadge("DRIFT ASSIST", orangeTexture, Color.black);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Phím tắt: F1 ẩn HUD | F2 đổi chế độ", labelStyle);

            GUILayout.EndArea();

            // Reset matrix
            GUI.matrix = Matrix4x4.identity;
        }

        private void DrawSteeringGauge(float value)
        {
            Rect gaugeRect = GUILayoutUtility.GetRect(280, 14);
            GUI.DrawTexture(gaugeRect, bgTexture);

            // Center mark
            float centerX = gaugeRect.x + gaugeRect.width * 0.5f;
            GUI.DrawTexture(new Rect(centerX - 1, gaugeRect.y, 2, gaugeRect.height), whiteTexture);

            // Steering needle / bar
            float clamped = Mathf.Clamp(value, -1f, 1f);
            float barWidth = (gaugeRect.width * 0.5f) * Mathf.Abs(clamped);
            Rect barRect;
            if (clamped < 0f)
            {
                barRect = new Rect(centerX - barWidth, gaugeRect.y + 2, barWidth, gaugeRect.height - 4);
            }
            else
            {
                barRect = new Rect(centerX, gaugeRect.y + 2, barWidth, gaugeRect.height - 4);
            }

            Texture2D steerColor = Mathf.Abs(clamped) > 0.7f ? orangeTexture : greenTexture;
            GUI.DrawTexture(barRect, steerColor);
        }

        private void DrawBar(float percent, Texture2D texture, float width, float height)
        {
            Rect bgRect = GUILayoutUtility.GetRect(width, height);
            GUI.DrawTexture(bgRect, bgTexture);
            float fillWidth = Mathf.Clamp01(percent) * width;
            if (fillWidth > 0f)
            {
                GUI.DrawTexture(new Rect(bgRect.x, bgRect.y, fillWidth, height), texture);
            }
        }

        private void DrawBadge(string text, Texture2D bg, Color textColor)
        {
            Color old = GUI.color;
            GUIStyle badge = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            badge.normal.textColor = textColor;
            badge.normal.background = bg;
            badge.padding = new RectOffset(6, 6, 2, 2);

            GUIContent content = new GUIContent(text);
            Vector2 size = badge.CalcSize(content);
            GUILayout.Label(content, badge, GUILayout.Width(size.x), GUILayout.Height(size.y));
            GUI.color = old;
        }

        private void InitTexturesAndStyles()
        {
            if (boxStyle != null) return;

            bgTexture = MakeSolidTexture(1, 1, new Color(0.08f, 0.08f, 0.1f, 0.85f));
            greenTexture = MakeSolidTexture(1, 1, new Color(0.2f, 0.85f, 0.3f, 1f));
            redTexture = MakeSolidTexture(1, 1, new Color(0.9f, 0.25f, 0.25f, 1f));
            yellowTexture = MakeSolidTexture(1, 1, new Color(0.95f, 0.8f, 0.2f, 1f));
            blueTexture = MakeSolidTexture(1, 1, new Color(0.2f, 0.55f, 1f, 1f));
            orangeTexture = MakeSolidTexture(1, 1, new Color(1f, 0.55f, 0.1f, 1f));
            whiteTexture = MakeSolidTexture(1, 1, new Color(0.7f, 0.7f, 0.7f, 0.8f));

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = bgTexture;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            headerStyle.normal.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal
            };
            labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);

            badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
        }

        private Texture2D MakeSolidTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private bool isDragging = false;
        private Vector2 dragStartMouse;
        private Vector2 dragStartOffset;

        private void HandleDragging(Rect rect)
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                isDragging = true;
                dragStartMouse = e.mousePosition;
                dragStartOffset = customOffset;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDragging)
            {
                customOffset = dragStartOffset + (e.mousePosition - dragStartMouse);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && isDragging)
            {
                isDragging = false;
                e.Use();
            }
        }

        private void OnDestroy()
        {
            if (bgTexture != null) Destroy(bgTexture);
            if (greenTexture != null) Destroy(greenTexture);
            if (redTexture != null) Destroy(redTexture);
            if (yellowTexture != null) Destroy(yellowTexture);
            if (blueTexture != null) Destroy(blueTexture);
            if (orangeTexture != null) Destroy(orangeTexture);
            if (whiteTexture != null) Destroy(whiteTexture);
        }
    }
}
