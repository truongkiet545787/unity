using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PoseKart.UI
{
    /// <summary>
    /// UI Controller đơn giản điều khiển bật/tắt bảng hướng dẫn (ManualDialog).
    /// Toàn bộ cấu trúc giao diện đã được dựng sẵn trong Scene Hierarchy và Prefab.
    /// Không tự động sinh GameObject hoặc can thiệp layout lúc runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartManualDialog : MonoBehaviour
    {
        [Header("Dialog Root")]
        [Tooltip("Root GameObject của ManualDialog để bật/tắt khi mở/đóng")]
        [SerializeField] private GameObject manualDialogRoot;

        [Header("Button References")]
        [Tooltip("Nút mở bảng (nằm trên MainMenu hoặc Canvas)")]
        [SerializeField] private Button manualButton;

        [Tooltip("Nút đóng hình chữ X trên Header")]
        [SerializeField] private Button closeButton;

        [Tooltip("Nút OK / Đã hiểu ở Footer")]
        [SerializeField] private Button okButton;

        [Header("Audio (Optional)")]
        [Tooltip("AudioSource phát tiếng click (tùy chọn). Tuyệt đối không tự động tìm AudioSource trong scene.")]
        [SerializeField] private AudioSource clickAudio;

        public GameObject ManualDialogRoot
        {
            get => manualDialogRoot != null ? manualDialogRoot : gameObject;
            set => manualDialogRoot = value;
        }

        public Button ManualButton
        {
            get => manualButton;
            set => manualButton = value;
        }

        public Button CloseButton
        {
            get => closeButton;
            set => closeButton = value;
        }

        public Button OKButton
        {
            get => okButton;
            set => okButton = value;
        }

        public AudioSource ClickAudio
        {
            get => clickAudio;
            set => clickAudio = value;
        }

        private void Reset()
        {
            if (manualDialogRoot == null)
            {
                manualDialogRoot = gameObject;
            }
        }

        private void Awake()
        {
            if (manualDialogRoot == null)
            {
                manualDialogRoot = gameObject;
            }

            BindManualButton();
        }

        private void OnEnable()
        {
            BindDialogButtons();
        }

        private void Start()
        {
            BindManualButton();
            BindDialogButtons();
        }

        private void BindManualButton()
        {
            if (manualButton != null)
            {
                manualButton.onClick.RemoveListener(Open);
                manualButton.onClick.AddListener(Open);
            }
        }

        private void BindDialogButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (okButton != null)
            {
                okButton.onClick.RemoveListener(Close);
                okButton.onClick.AddListener(Close);
            }
        }

        private void Update()
        {
            // Chỉ kiểm tra phím ESC khi bảng hướng dẫn đang hiển thị
            GameObject target = ManualDialogRoot;
            if (target != null && target.activeInHierarchy)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    Close();
                }
            }
        }

        /// <summary>
        /// Mở bảng hướng dẫn.
        /// </summary>
        public void Open()
        {
            GameObject target = ManualDialogRoot;
            if (target != null)
            {
                target.SetActive(true);
                target.transform.SetAsLastSibling(); // Luôn hiển thị trên cùng trong Canvas
            }

            PlayClickAudio();
        }

        /// <summary>
        /// Đóng bảng hướng dẫn.
        /// </summary>
        public void Close()
        {
            GameObject target = ManualDialogRoot;
            if (target != null)
            {
                target.SetActive(false);
            }

            PlayClickAudio();
        }

        private void PlayClickAudio()
        {
            // CHỈ phát âm thanh nếu clickAudio được gán rõ ràng trong Inspector.
            // TUYỆT ĐỐI không tìm AudioSource bất kỳ trong scene.
            if (clickAudio != null)
            {
                clickAudio.Play();
            }
        }
    }
}
