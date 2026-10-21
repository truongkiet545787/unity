using System;
using UnityEngine;
using UnityEngine.InputSystem;
using PoseKart.Vehicle;

namespace PoseKart.Input
{
    public enum PoseKartInputMode
    {
        Keyboard = 0,
        HandControl = 1
    }

    /// <summary>
    /// Cho phép chuyển đổi linh hoạt giữa điều khiển bằng Bàn phím (Keyboard)
    /// và Điều khiển bằng Cử chỉ tay webcam (MediaPipe Hand Control).
    /// Hỗ trợ phím tắt F2 để chuyển đổi nhanh khi đang đua.
    /// Lưu trạng thái vào PlayerPrefs để duy trì lựa chọn của người chơi.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class PoseKartHandInputSwitcher : MonoBehaviour
    {
        public const string ControlModePrefKey = "PoseKart_ControlMode";

        [Header("Vehicle References")]
        [SerializeField] private CarController carController;
        [SerializeField] private KeyboardInputProvider keyboardProvider;
        [SerializeField] private PoseKartHandInputProvider handProvider;

        [Header("Active Control Mode")]
        [SerializeField] private PoseKartInputMode currentMode = PoseKartInputMode.Keyboard;

        [Header("Shortcut Settings")]
        [Tooltip("Bật phím tắt F2 để đổi chế độ lái ngay trong game.")]
        [SerializeField] private bool enableF2Shortcut = true;

        public static event Action<PoseKartInputMode> OnControlModeChanged;

        public PoseKartInputMode CurrentMode => currentMode;
        public static PoseKartInputMode GlobalMode => (PoseKartInputMode)PlayerPrefs.GetInt(ControlModePrefKey, (int)PoseKartInputMode.Keyboard);

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
            // Đọc chế độ đã lưu trong PlayerPrefs
            PoseKartInputMode savedMode = GlobalMode;
            ApplyControlMode(savedMode, savePref: false);
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (carController == null)
            {
                carController = GetComponent<CarController>();
            }

            if (keyboardProvider == null)
            {
                keyboardProvider = GetComponent<KeyboardInputProvider>();
                if (keyboardProvider == null && carController != null && carController.InputProviderSource is KeyboardInputProvider kb)
                {
                    keyboardProvider = kb;
                }
            }

            if (handProvider == null)
            {
                handProvider = GetComponent<PoseKartHandInputProvider>();
                if (handProvider == null)
                {
                    handProvider = gameObject.AddComponent<PoseKartHandInputProvider>();
                }
            }

            // Đảm bảo có PoseKartHandUdpReceiver trên xe hoặc trong Scene
            if (FindFirstObjectByType<PoseKartHandUdpReceiver>() == null)
            {
                gameObject.AddComponent<PoseKartHandUdpReceiver>();
            }
        }

        private void Update()
        {
            if (enableF2Shortcut)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
                {
                    ToggleControlMode();
                }
            }
        }

        /// <summary>
        /// Chuyển qua lại giữa Keyboard và Hand Control.
        /// </summary>
        public void ToggleControlMode()
        {
            PoseKartInputMode newMode = (currentMode == PoseKartInputMode.Keyboard)
                ? PoseKartInputMode.HandControl
                : PoseKartInputMode.Keyboard;

            SetControlMode(newMode);
        }

        /// <summary>
        /// Đặt chế độ điều khiển cho xe người chơi.
        /// </summary>
        public void SetControlMode(PoseKartInputMode mode)
        {
            ApplyControlMode(mode, savePref: true);
        }

        private void ApplyControlMode(PoseKartInputMode mode, bool savePref)
        {
            currentMode = mode;
            ResolveReferences();

            if (mode == PoseKartInputMode.HandControl)
            {
                if (keyboardProvider != null)
                {
                    keyboardProvider.enabled = false;
                }

                if (handProvider != null)
                {
                    handProvider.enabled = true;
                    if (carController != null)
                    {
                        carController.SetInputProvider(handProvider);
                    }
                }
            }
            else // Keyboard
            {
                if (handProvider != null)
                {
                    handProvider.enabled = false;
                }

                if (keyboardProvider != null)
                {
                    keyboardProvider.enabled = true;
                    if (carController != null)
                    {
                        carController.SetInputProvider(keyboardProvider);
                    }
                }
            }

            if (savePref)
            {
                PlayerPrefs.SetInt(ControlModePrefKey, (int)mode);
                PlayerPrefs.Save();
            }

            OnControlModeChanged?.Invoke(currentMode);
        }

        /// <summary>
        /// Phương thức static để đổi chế độ từ bất kỳ UI nào (MainMenu, Settings, Calibration).
        /// </summary>
        public static void SetGlobalControlMode(PoseKartInputMode mode)
        {
            PlayerPrefs.SetInt(ControlModePrefKey, (int)mode);
            PlayerPrefs.Save();

            PoseKartHandInputSwitcher activeSwitcher = FindFirstObjectByType<PoseKartHandInputSwitcher>();
            if (activeSwitcher != null)
            {
                activeSwitcher.SetControlMode(mode);
            }
            else
            {
                OnControlModeChanged?.Invoke(mode);
            }
        }
    }
}
