using UnityEngine;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Attach to a dedicated trigger at the ramp EXIT to arm and launch cars into Asphalt 8-style Barrel Rolls.
    /// Supports launch impulse assist, roll direction, and Editor visualization gizmos.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PoseKartBarrelRollRamp : MonoBehaviour
    {
        [Header("Activation")]
        [Tooltip("Minimum forward speed required to trigger the roll.")]
        [SerializeField, Min(0f)] private float minimumForwardSpeed = 8f;
        [Tooltip("Time window in seconds after passing trigger to detect liftoff.")]
        [SerializeField, Min(0.1f)] private float armWindow = 1.2f;
        [Tooltip("Duration in seconds for one complete 360-degree rotation cycle.")]
        [SerializeField, Min(0.25f)] private float rollDuration = 0.75f;
        [Tooltip("True for clockwise roll, false for counter-clockwise.")]
        [SerializeField] private bool clockwise = true;

        [Header("Arcade Launch Assist")]
        [Tooltip("Applies an upward and forward impulse at ramp exit to ensure a soaring flight arc.")]
        [SerializeField] private bool applyLaunchImpulse = true;
        [SerializeField, Min(0f)] private float upwardVelocityChange = 2.5f;
        [SerializeField, Min(0f)] private float forwardVelocityChange = 1.2f;

        [Header("Reward")]
        [Tooltip("Nitro percentage rewarded per completed 360-degree roll (e.g. 0.15 = 15%). Multiplied by total rolls completed.")]
        [SerializeField, Range(0f, 0.5f)] private float nitroRewardFraction = 0.15f;

        public float MinimumForwardSpeed => minimumForwardSpeed;
        public float ArmWindow => armWindow;
        public float RollDuration => rollDuration;
        public int RollDirection => clockwise ? 1 : -1;
        public bool ApplyLaunchImpulse => applyLaunchImpulse;
        public float UpwardVelocityChange => upwardVelocityChange;
        public float ForwardVelocityChange => forwardVelocityChange;
        public float NitroRewardFraction => nitroRewardFraction;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PoseKartStuntController stunt = other.GetComponentInParent<PoseKartStuntController>();
            if (stunt != null)
            {
                stunt.TryArmFromBarrelRollRamp(this);
            }

            PoseKartBarrelRollController roll = other.GetComponentInParent<PoseKartBarrelRollController>();
            if (roll != null)
            {
                roll.TryArm(this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw launch direction and trajectory visualization in Unity Editor
            Gizmos.color = clockwise ? new Color(0f, 0.9f, 1f, 0.8f) : new Color(1f, 0.5f, 0f, 0.8f);
            Vector3 start = transform.position;
            Vector3 forward = transform.forward * 4f;
            Vector3 up = Vector3.up * 2.5f;
            Gizmos.DrawRay(start, forward + up);
            Gizmos.DrawWireSphere(start + forward + up, 0.5f);
        }
    }
}
