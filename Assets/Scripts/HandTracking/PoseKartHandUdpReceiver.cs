using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PoseKart.Input
{
    /// <summary>
    /// Thread-safe UDP Receiver listening on localhost:5052 for Python MediaPipe hand tracking packets.
    /// Runs on a dedicated background thread, safely passing data to Unity's main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartHandUdpReceiver : MonoBehaviour
    {
        [Header("Network Configuration")]
        [SerializeField] private int port = 5052;
        [Tooltip("Seconds without packet before marking tracking as lost.")]
        [SerializeField, Range(0.1f, 2f)] private float packetTimeoutSeconds = 0.5f;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private bool isConnected;
        [SerializeField] private bool isTracking;
        [SerializeField] private float trackingConfidence;
        [SerializeField] private float steering;
        [SerializeField] private float throttle;
        [SerializeField] private float brake;
        [SerializeField] private float wheelAngleDeg;
        [SerializeField] private int nitroPressId;
        [SerializeField] private int itemPressId;
        [SerializeField] private bool drift;
        [SerializeField] private int lastSequence;
        [SerializeField] private float timeSinceLastPacket;

        // Background thread communication
        private UdpClient udpClient;
        private Thread receiveThread;
        private volatile bool isRunning;
        private readonly object stateLock = new object();
        private readonly System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        private volatile string lastWorkerError = null;

        // Raw received state protected by lock
        private PacketData latestPacket = new PacketData();
        private float lastPacketReceiveTime = -100f;
        private bool hasReceivedAnyPacket = false;

#pragma warning disable 0649
        [Serializable]
        private struct PacketData
        {
            public int sequence;
            public bool tracking;
            public float confidence;
            public float steering;
            public float throttle;
            public float brake;
            public float wheelAngleDeg;
            public int nitroPressId;
            public int itemPressId;
            public bool drift;
        }
#pragma warning restore 0649

        // Public properties consumed by Input Provider and UI
        public bool IsConnected => isConnected;
        public bool IsTracking => isTracking;
        public float Confidence => trackingConfidence;
        public float Steering => steering;
        public float Throttle => throttle;
        public float Brake => brake;
        public float WheelAngleDeg => wheelAngleDeg;
        public int NitroPressId => nitroPressId;
        public int ItemPressId => itemPressId;
        public bool Drift => drift;
        public int LastSequence => lastSequence;
        public float TimeSinceLastPacket => timeSinceLastPacket;

        private void OnEnable()
        {
            StartReceiver();
        }

        private void Start()
        {
            if (!isRunning)
            {
                StartReceiver();
            }
        }

        public void StartReceiver()
        {
            if (isRunning) return;

            try
            {
                if (!stopwatch.IsRunning)
                {
                    stopwatch.Start();
                }

                isRunning = true;
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                receiveThread = new Thread(ReceiveWorkerLoop)
                {
                    IsBackground = true,
                    Name = "PoseKart_UDP_Receiver"
                };
                receiveThread.Start();
                Debug.Log($"[PoseKartHandUdpReceiver] Đã mở cổng UDP {port} lắng nghe tín hiệu Python MediaPipe.");
            }
            catch (Exception ex)
            {
                isRunning = false;
                Debug.LogWarning($"[PoseKartHandUdpReceiver] Không thể mở cổng UDP {port}: {ex.Message}");
            }
        }

        private void ReceiveWorkerLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                try
                {
                    if (udpClient == null || udpClient.Client == null) break;

                    byte[] data = udpClient.Receive(ref remoteEndPoint);
                    if (data == null || data.Length == 0) continue;

                    string json = Encoding.UTF8.GetString(data);
                    PacketData parsed = JsonUtility.FromJson<PacketData>(json);

                    // Kiểm tra tính hợp lệ của dữ liệu số (loại bỏ NaN và Infinity)
                    if (!IsFinite(parsed.steering) || !IsFinite(parsed.throttle) ||
                        !IsFinite(parsed.brake) || !IsFinite(parsed.wheelAngleDeg))
                    {
                        continue;
                    }

                    float receiveTime = (float)stopwatch.Elapsed.TotalSeconds;

                    lock (stateLock)
                    {
                        // Chấp nhận gói nếu sequence mới hơn, hoặc nếu sequence reset khi Python khởi động lại
                        if (parsed.sequence >= latestPacket.sequence || 
                            (latestPacket.sequence - parsed.sequence) > 10 || 
                            (receiveTime - lastPacketReceiveTime) > 1f)
                        {
                            latestPacket = parsed;
                            lastPacketReceiveTime = receiveTime;
                            hasReceivedAnyPacket = true;
                        }
                    }
                }
                catch (SocketException)
                {
                    // Socket đóng khi thoát ứng dụng
                    break;
                }
                catch (Exception ex)
                {
                    lastWorkerError = ex.Message;
                }
            }
        }

        private void Update()
        {
            float now = (float)stopwatch.Elapsed.TotalSeconds;
            PacketData current;
            float packetTime;

            if (lastWorkerError != null)
            {
                Debug.LogWarning($"[PoseKartHandUdpReceiver] Lỗi xử lý gói UDP: {lastWorkerError}");
                lastWorkerError = null;
            }

            lock (stateLock)
            {
                current = latestPacket;
                packetTime = lastPacketReceiveTime;
            }

            timeSinceLastPacket = now - packetTime;
            isConnected = hasReceivedAnyPacket && (timeSinceLastPacket < 3f);

            // Kiểm tra timeout mất dấu (tracking loss)
            if (timeSinceLastPacket > packetTimeoutSeconds || !current.tracking)
            {
                isTracking = false;
                trackingConfidence = 0f;
                // Trả dần góc lái về 0 khi mất dấu
                steering = Mathf.MoveTowards(steering, 0f, Time.unscaledDeltaTime * 3.5f);
                throttle = 0f;
                brake = 0f;
                drift = false;
                wheelAngleDeg = 0f;
            }
            else
            {
                isTracking = true;
                trackingConfidence = current.confidence;
                steering = Mathf.Clamp(current.steering, -1f, 1f);
                throttle = Mathf.Clamp01(current.throttle);
                brake = Mathf.Clamp01(current.brake);
                wheelAngleDeg = current.wheelAngleDeg;
                nitroPressId = current.nitroPressId;
                itemPressId = current.itemPressId;
                drift = current.drift;
                lastSequence = current.sequence;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void StopReceiver()
        {
            isRunning = false;

            if (udpClient != null)
            {
                try
                {
                    udpClient.Close();
                    udpClient.Dispose();
                }
                catch { }
                udpClient = null;
            }

            if (receiveThread != null && receiveThread.IsAlive)
            {
                try
                {
                    receiveThread.Join(150);
                }
                catch { }
                receiveThread = null;
            }
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnDestroy()
        {
            StopReceiver();
        }

        private void OnApplicationQuit()
        {
            StopReceiver();
        }
    }
}
