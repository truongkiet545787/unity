using UnityEngine;
using UnityEngine.InputSystem;
using PoseKart.Vehicle;
using PoseKart.Race;

namespace PoseKart.Input
{
    /// <summary>
    /// Implements ICarInputProvider and IKartGestureInput for MediaPipe hand tracking.
    /// Bridges the UDP receiver with existing CarController, KartItemInventory, and Drift systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartHandInputProvider : MonoBehaviour, ICarInputProvider, IKartGestureInput
    {
        [Header("Receiver Reference")]
        [SerializeField] private PoseKartHandUdpReceiver receiver;

        [Header("Vehicle References")]
        [SerializeField] private CarController carController;
        [SerializeField] private KartItemInventory itemInventory;
        [SerializeField] private CarResetController resetController;

        [Header("Drift Assist Configuration")]
        [Tooltip("Tự động kích hoạt Drift khi ôm cua gắt ở tốc độ cao.")]
        [SerializeField] private bool enableDriftAssist = true;
        [Tooltip("Tốc độ tối thiểu (km/h) để kích hoạt Drift Assist.")]
        [SerializeField, Min(10f)] private float driftMinimumSpeedKmh = 65f;
        [Tooltip("Ngưỡng đánh lái tuyệt đối (0.0 - 1.0) để kích hoạt Drift Assist.")]
        [SerializeField, Range(0.5f, 0.95f)] private float driftSteeringThreshold = 0.70f;
        [Tooltip("Thời gian giữ góc lái gắt liên tục (giây) trước khi bắt đầu Drift.")]
        [SerializeField, Range(0.1f, 1f)] private float driftHoldDuration = 0.30f;

        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Receiver Active")]
        [SerializeField] private bool debugReceiverActive;
        [InspectorName("Steering Output")]
        [SerializeField, Range(-1f, 1f)] private float debugSteering;
        [InspectorName("Throttle Output")]
        [SerializeField, Range(0f, 1f)] private float debugThrottle;
        [InspectorName("Brake Output")]
        [SerializeField, Range(0f, 1f)] private float debugBrake;
        [InspectorName("Drift Active")]
        [SerializeField] private bool debugDrift;
        [InspectorName("Last Nitro Event ID")]
        [SerializeField] private int debugLastNitroId;
        [InspectorName("Last Item Event ID")]
        [SerializeField] private int debugLastItemId;

        // Sequence trackers for discrete triggers
        private int lastNitroId = 0;
        private int lastItemId = 0;
        private float sharpTurnTimer = 0f;
        private bool driftAssistActive = false;

        // ICarInputProvider Contract
        public float Steering => receiver != null && receiver.IsTracking ? receiver.Steering : 0f;
        public float Throttle => receiver != null && receiver.IsTracking ? receiver.Throttle : 0f;
        public float Brake => receiver != null && receiver.IsTracking ? receiver.Brake : 0f;
        public bool Boost => false;
        public bool IsInputValid => isActiveAndEnabled && receiver != null && receiver.IsTracking;

        // IKartGestureInput Contract
        public bool Drift
        {
            get
            {
                if (!IsInputValid) return false;
                return (receiver != null && receiver.Drift) || driftAssistActive;
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            // Đảm bảo đồng bộ ID mới nhất khi bật component để không kích hoạt cử chỉ cũ
            if (receiver != null)
            {
                lastNitroId = receiver.NitroPressId;
                lastItemId = receiver.ItemPressId;
            }
        }

        private void ResolveReferences()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (itemInventory == null) itemInventory = GetComponent<KartItemInventory>();
            if (resetController == null) resetController = GetComponent<CarResetController>();

            if (receiver == null)
            {
                // Tìm receiver trên chính GameObject, hoặc trên đối tượng HandTrackingSystem
                receiver = GetComponent<PoseKartHandUdpReceiver>();
                if (receiver == null)
                {
                    receiver = FindFirstObjectByType<PoseKartHandUdpReceiver>();
                }
            }
        }

        private void Update()
        {
            // 1. Phím R dự phòng trên bàn phím để hồi phục xe khi lật hoặc kẹt
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                if (resetController != null)
                {
                    resetController.ResetVehicle();
                }
            }

            if (receiver == null)
            {
                receiver = FindFirstObjectByType<PoseKartHandUdpReceiver>();
                if (receiver == null) return;
            }

            // 2. Xử lý sự kiện kích hoạt Nitro (Discrete Event từ Python)
            if (receiver.NitroPressId > lastNitroId)
            {
                lastNitroId = receiver.NitroPressId;
                if (carController != null && carController.ControlEnabled && receiver.IsTracking)
                {
                    carController.UseStoredNitro();
                }
            }
            else if (receiver.NitroPressId < lastNitroId)
            {
                // Reset ID nếu Python server khởi động lại
                lastNitroId = receiver.NitroPressId;
            }

            // 3. Xử lý sự kiện dùng vật phẩm (Discrete Event từ Python)
            if (receiver.ItemPressId > lastItemId)
            {
                lastItemId = receiver.ItemPressId;
                if (itemInventory != null && receiver.IsTracking)
                {
                    itemInventory.UseItem();
                }
            }
            else if (receiver.ItemPressId < lastItemId)
            {
                // Reset ID nếu Python server khởi động lại
                lastItemId = receiver.ItemPressId;
            }

            // 4. Tính toán tính năng Drift Assist (Hỗ trợ trượt bánh khi ôm cua gắt)
            UpdateDriftAssist();

            // 5. Cập nhật các trường hiển thị trong Inspector
            RefreshDebugValues();
        }

        private void UpdateDriftAssist()
        {
            if (!enableDriftAssist || carController == null || !receiver.IsTracking)
            {
                sharpTurnTimer = 0f;
                driftAssistActive = false;
                return;
            }

            float currentSpeedKmh = carController.ForwardSpeed * 3.6f;
            float absSteer = Mathf.Abs(Steering);

            if (currentSpeedKmh >= driftMinimumSpeedKmh && absSteer >= driftSteeringThreshold && Brake <= 0.05f)
            {
                sharpTurnTimer += Time.deltaTime;
                if (sharpTurnTimer >= driftHoldDuration)
                {
                    driftAssistActive = true;
                }
            }
            else
            {
                sharpTurnTimer = 0f;
                driftAssistActive = false;
            }
        }

        private void RefreshDebugValues()
        {
            debugReceiverActive = receiver != null && receiver.IsConnected;
            debugSteering = Steering;
            debugThrottle = Throttle;
            debugBrake = Brake;
            debugDrift = Drift;
            debugLastNitroId = lastNitroId;
            debugLastItemId = lastItemId;
        }
    }
}
