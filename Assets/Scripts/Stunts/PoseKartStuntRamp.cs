using UnityEngine;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Asphalt 8-style Stunt Ramp metadata and launch trigger.
    /// Supports Normal Jump (with Drift-initiated Flat Spin), Barrel Roll ramps, and Special Flip ramps.
    /// Provides arcade launch assist, custom rotation speed/direction, and visual gizmos.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PoseKartStuntRamp : MonoBehaviour
    {
        [Header("Ramp Classification")]
        [Tooltip("Loại dốc: NormalRamp (Nhảy thường / Flat Spin khi Drift), BarrelRollRamp (Cuộn lốc xoáy), SpecialFlipRamp (Lộn nhào).")]
        [SerializeField] private RampType rampType = RampType.NormalRamp;

        [Header("Activation & Dynamics")]
        [Tooltip("Tốc độ tối thiểu (m/s) của xe khi đi vào dốc để kích hoạt bay biểu diễn.")]
        [SerializeField, Min(0f)] private float minimumForwardSpeed = 7.5f;

        [Tooltip("Cửa sổ thời gian (giây) sau khi chạm dốc để phát hiện xe rời mặt đất.")]
        [SerializeField, Min(0.1f)] private float armWindow = 1.4f;

        [Tooltip("Thời gian (giây) để hoàn thành 1 chu kỳ xoay 360 độ (0.65s - 0.85s).")]
        [SerializeField, Min(0.25f)] private float rotationDuration = 0.75f;

        [Tooltip("Chiều xoay: True là theo chiều kim đồng hồ, False là ngược chiều.")]
        [SerializeField] private bool clockwise = true;

        [Tooltip("Loại lộn nhào (chỉ áp dụng nếu RampType là SpecialFlipRamp).")]
        [SerializeField] private StuntType specialFlipType = StuntType.FrontFlip;

        [Header("Arcade Launch Assist")]
        [Tooltip("Tác động xung lực trợ lực bay để tạo quỹ đạo bổng cao vút chuẩn Asphalt 8.")]
        [SerializeField] private bool applyLaunchImpulse = true;
        [SerializeField, Min(0f)] private float upwardVelocityChange = 3.0f;
        [SerializeField, Min(0f)] private float forwardVelocityChange = 1.2f;

        [Header("Reward Multiplier")]
        [Tooltip("Hệ số nhân thưởng Nitro của dốc này (1.0 = chuẩn).")]
        [SerializeField, Range(0.5f, 2.5f)] private float rewardMultiplier = 1.0f;

        [Header("Presentation Feedback (Optional)")]
        [SerializeField] private AudioClip takeoffSound;
        [SerializeField] private ParticleSystem takeoffVfx;

        public RampType Type => rampType;
        public float MinimumForwardSpeed => minimumForwardSpeed;
        public float ArmWindow => armWindow;
        public float RotationDuration => rotationDuration;
        public int RotationDirection => clockwise ? 1 : -1;
        public StuntType SpecialFlipType => specialFlipType;
        public bool ApplyLaunchImpulse => applyLaunchImpulse;
        public float UpwardVelocityChange => upwardVelocityChange;
        public float ForwardVelocityChange => forwardVelocityChange;
        public float RewardMultiplier => rewardMultiplier;
        public AudioClip TakeoffSound => takeoffSound;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PoseKartStuntController stunt = other.GetComponentInParent<PoseKartStuntController>();
            if (stunt != null)
            {
                stunt.TryArmFromRamp(this);
                if (takeoffVfx != null)
                {
                    takeoffVfx.Play(true);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Color gizmoColor;
            switch (rampType)
            {
                case RampType.BarrelRollRamp:
                    gizmoColor = clockwise ? new Color(1f, 0.85f, 0f, 0.85f) : new Color(1f, 0.45f, 0f, 0.85f);
                    break;
                case RampType.SpecialFlipRamp:
                    gizmoColor = new Color(1f, 0.2f, 0.6f, 0.85f);
                    break;
                default:
                    gizmoColor = new Color(0f, 0.9f, 1f, 0.85f);
                    break;
            }

            Gizmos.color = gizmoColor;
            Vector3 origin = transform.position;
            Vector3 fwd = transform.forward * (applyLaunchImpulse ? forwardVelocityChange * 0.8f : 4f);
            Vector3 up = Vector3.up * (applyLaunchImpulse ? upwardVelocityChange * 0.4f : 2.5f);
            Vector3 target = origin + fwd + up;

            Gizmos.DrawRay(origin, fwd + up);
            Gizmos.DrawWireSphere(target, 0.6f);
        }
    }
}
