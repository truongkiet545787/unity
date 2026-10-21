using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PoseKart.Input
{
    /// <summary>
    /// Giao diện cấu hình và hiệu chuẩn cử chỉ tay (Hand Control Calibration UI).
    /// Cho phép chuyển đổi chế độ [ KEYBOARD ] / [ HAND CONTROL ] ở sảnh chờ/menu,
    /// kiểm tra góc lái và hướng dẫn hiệu chuẩn bằng phím bấm Python ('C', 'R', 'I').
    /// Hỗ trợ cả Unity UI Canvas (nếu gán trong Inspector) lẫn IMGUI OnGUI tự động.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartHandCalibrationUI : MonoBehaviour
    {
        [Header("Receiver Reference")]
        [SerializeField] private PoseKartHandUdpReceiver receiver;

        [Header("Scene Navigation")]
        [SerializeField] private string raceSceneName = "PoseKart_ReferenceIsland";

        [Header("Optional Unity UI References")]
        [SerializeField] private Button keyboardModeButton;
        [SerializeField] private Button handControlModeButton;
        [SerializeField] private Text modeStatusText;
        [SerializeField] private Text trackingStatusText;
        [SerializeField] private Text steeringAngleText;
        [SerializeField] private Button startRaceButton;

        [Header("IMGUI Fallback Display")]
        [SerializeField] private bool enableImGuiOverlay = true;
        [SerializeField, Range(0.5f, 1.5f)] private float uiScale = 1.0f;

        private GUIStyle panelStyle;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private Texture2D panelBg;
        private Texture2D greenTex;
        private Texture2D redTex;
        private Texture2D yellowTex;
        private Texture2D blueTex;

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
            BindUiEvents();
            UpdateUiElements();
        }

        private void ResolveReferences()
        {
            if (receiver == null)
            {
                receiver = FindFirstObjectByType<PoseKartHandUdpReceiver>();
                if (receiver == null)
                {
                    receiver = gameObject.AddComponent<PoseKartHandUdpReceiver>();
                }
            }
        }

        private void BindUiEvents()
        {
            if (keyboardModeButton != null)
            {
                keyboardModeButton.onClick.RemoveAllListeners();
                keyboardModeButton.onClick.AddListener(() => SetMode(PoseKartInputMode.Keyboard));
            }

            if (handControlModeButton != null)
            {
                handControlModeButton.onClick.RemoveAllListeners();
                handControlModeButton.onClick.AddListener(() => SetMode(PoseKartInputMode.HandControl));
            }

            if (startRaceButton != null)
            {
                startRaceButton.onClick.RemoveAllListeners();
                startRaceButton.onClick.AddListener(OnStartRaceClicked);
            }

            PoseKartHandInputSwitcher.OnControlModeChanged += OnControlModeChanged;
        }

        private void OnDestroy()
        {
            PoseKartHandInputSwitcher.OnControlModeChanged -= OnControlModeChanged;
            CleanTextures();
        }

        private void OnControlModeChanged(PoseKartInputMode newMode)
        {
            UpdateUiElements();
        }

        private void Update()
        {
            UpdateUiElements();
        }

        private void SetMode(PoseKartInputMode mode)
        {
            PoseKartHandInputSwitcher.SetGlobalControlMode(mode);
            UpdateUiElements();
        }

        private void OnStartRaceClicked()
        {
            if (!string.IsNullOrEmpty(raceSceneName))
            {
                SceneManager.LoadScene(raceSceneName);
            }
        }

        private void UpdateUiElements()
        {
            PoseKartInputMode currentMode = PoseKartHandInputSwitcher.GlobalMode;

            if (modeStatusText != null)
            {
                modeStatusText.text = $"Chế độ: {(currentMode == PoseKartInputMode.HandControl ? "CỬ CHỈ TAY" : "BÀN PHÍM")}";
            }

            if (receiver != null)
            {
                if (trackingStatusText != null)
                {
                    if (!receiver.IsConnected) trackingStatusText.text = "UDP: Chưa kết nối (Chạy run_hand_control.bat)";
                    else if (!receiver.IsTracking) trackingStatusText.text = "Camera: Đang tìm 2 bàn tay...";
                    else trackingStatusText.text = $"Camera: Đã nhận diện (Tín cậy: {Mathf.RoundToInt(receiver.Confidence * 100)}%)";
                }

                if (steeringAngleText != null)
                {
                    steeringAngleText.text = $"Góc lái: {receiver.WheelAngleDeg:+0.0;-0.0;0.0}° (Output: {receiver.Steering:+0.00;-0.00;0.00})";
                }
            }
        }

        private void OnGUI()
        {
            if (!enableImGuiOverlay) return;

            InitStyles();

            PoseKartInputMode activeMode = PoseKartHandInputSwitcher.GlobalMode;

            float panelWidth = 440f;
            float panelHeight = 420f;
            float x = (Screen.width - (panelWidth * uiScale)) * 0.5f;
            float y = 50f;

            GUI.matrix = Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.identity, new Vector3(uiScale, uiScale, 1));
            Rect localRect = new Rect(0, 0, panelWidth, panelHeight);

            GUI.Box(localRect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(16, 16, panelWidth - 32, panelHeight - 32));

            GUILayout.Label("CẤU HÌNH ĐIỀU KHIỂN POSEKART", headerStyle);
            GUILayout.Label("CHỌN CHẾ ĐỘ ĐIỀU KHIỂN (CONTROL MODE)", subHeaderStyle);
            GUILayout.Space(8);

            // Button Mode Selection: [ KEYBOARD ] [ HAND CONTROL ]
            GUILayout.BeginHorizontal();
            bool isKb = activeMode == PoseKartInputMode.Keyboard;
            if (GUILayout.Button("BÀN PHÍM (KEYBOARD)", isKb ? activeButtonStyle : buttonStyle, GUILayout.Height(36)))
            {
                SetMode(PoseKartInputMode.Keyboard);
            }
            bool isHand = activeMode == PoseKartInputMode.HandControl;
            if (GUILayout.Button("CỬ CHỈ TAY (HAND CONTROL)", isHand ? activeButtonStyle : buttonStyle, GUILayout.Height(36)))
            {
                SetMode(PoseKartInputMode.HandControl);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);

            // Calibration & Webcam Status Section (Show when Hand Control is selected)
            if (isHand)
            {
                GUILayout.Label("TRẠNG THÁI WEBCAM & HIỆU CHUẨN TAY", subHeaderStyle);
                GUILayout.Space(4);

                bool connected = receiver != null && receiver.IsConnected;
                bool tracking = receiver != null && receiver.IsTracking;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Kết nối UDP (Port 5052):", labelStyle, GUILayout.Width(180));
                GUI.color = connected ? Color.green : Color.red;
                GUILayout.Label(connected ? "ĐÃ KẾT NỐI" : "CHƯA KẾT NỐI (Chạy run_hand_control.bat)", labelStyle);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Nhận diện bàn tay:", labelStyle, GUILayout.Width(180));
                GUI.color = tracking ? Color.green : (connected ? Color.yellow : Color.gray);
                GUILayout.Label(tracking ? $"2 TAY OK ({Mathf.RoundToInt(receiver.Confidence * 100)}%)" : "ĐANG TÌM 2 TAY...", labelStyle);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                float steerAngle = receiver != null && tracking ? receiver.WheelAngleDeg : 0f;
                float steerVal = receiver != null && tracking ? receiver.Steering : 0f;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Góc nghiêng vô lăng:", labelStyle, GUILayout.Width(180));
                GUILayout.Label($"{steerAngle:+0.0;-0.0;0.0}°  [Output: {steerVal:+0.00;-0.00;0.00}]", labelStyle);
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("HƯỚNG DẪN HIỆU CHUẨN (Trong cửa sổ Camera Python):", subHeaderStyle);
                GUILayout.Label("• Giữ 2 tay ngang tự nhiên rồi bấm 'C' để lưu điểm lái thẳng (0°).", labelStyle);
                GUILayout.Label("• Bấm 'R' để đặt lại (Reset) hiệu chuẩn về mặc định.", labelStyle);
                GUILayout.Label("• Bấm 'I' nếu muốn đảo chiều vô lăng (Invert Steering).", labelStyle);
                GUILayout.Label("• Bấm 'Q' trong cửa sổ Python để đóng ứng dụng camera.", labelStyle);
            }
            else
            {
                GUILayout.Label("HƯỚNG DẪN BÀN PHÍM:", subHeaderStyle);
                GUILayout.Label("• A / D hoặc Mũi tên Trái / Phải: Đánh lái", labelStyle);
                GUILayout.Label("• Phím S hoặc Mũi tên Xuống: Phanh / Lùi", labelStyle);
                GUILayout.Label("• Shift Trái: Drift (Trượt bánh)", labelStyle);
                GUILayout.Label("• Phím R: Hồi phục / Lật xe khi kẹt", labelStyle);
            }

            GUILayout.FlexibleSpace();

            // Start Race Button
            if (GUILayout.Button("VÀO TRẬN ĐUA (START RACE)", activeButtonStyle, GUILayout.Height(40)))
            {
                OnStartRaceClicked();
            }

            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        private void InitStyles()
        {
            if (panelStyle != null) return;

            panelBg = MakeSolidTexture(1, 1, new Color(0.06f, 0.08f, 0.12f, 0.94f));
            greenTex = MakeSolidTexture(1, 1, new Color(0.18f, 0.72f, 0.35f, 1f));
            redTex = MakeSolidTexture(1, 1, new Color(0.85f, 0.22f, 0.22f, 1f));
            yellowTex = MakeSolidTexture(1, 1, new Color(0.9f, 0.75f, 0.15f, 1f));
            blueTex = MakeSolidTexture(1, 1, new Color(0.2f, 0.5f, 0.9f, 1f));

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelBg;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            headerStyle.normal.textColor = Color.white;

            subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            subHeaderStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal
            };
            labelStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f);

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            activeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            activeButtonStyle.normal.background = blueTex;
            activeButtonStyle.normal.textColor = Color.white;
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

        private void CleanTextures()
        {
            if (panelBg != null) Destroy(panelBg);
            if (greenTex != null) Destroy(greenTex);
            if (redTex != null) Destroy(redTex);
            if (yellowTex != null) Destroy(yellowTex);
            if (blueTex != null) Destroy(blueTex);
        }
    }
}
