using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Input
{
    [DisallowMultipleComponent]
    public sealed class KeyboardInputProvider : MonoBehaviour, ICarInputProvider, IKartGestureInput
    {
        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Keyboard Detected")]
        [SerializeField] private bool keyboardAvailable;
        [InspectorName("Current Steering")]
        [SerializeField, Range(-1f, 1f)] private float debugSteering;
        [InspectorName("Current Throttle")]
        [SerializeField, Range(0f, 1f)] private float debugThrottle;
        [InspectorName("Current Brake")]
        [SerializeField, Range(0f, 1f)] private float debugBrake;

        public float Steering
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null)
                {
                    return 0f;
                }

                float left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? -1f : 0f;
                float right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f;
                return left + right;
            }
        }

        public float Throttle
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && Brake<=0f ? 1f : 0f;
            }
        }

        public float Brake
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                    ? 1f
                    : 0f;
            }
        }

        public bool Boost => false;
        public bool Drift => Keyboard.current!=null&&(Keyboard.current.leftShiftKey.isPressed||Keyboard.current.rightShiftKey.isPressed);

        public bool IsInputValid => isActiveAndEnabled && Keyboard.current != null;

        private void OnEnable()
        {
            RefreshDebugValues();
        }

        private void Update()
        {
            RefreshDebugValues();
        }

        private void RefreshDebugValues()
        {
            keyboardAvailable = Keyboard.current != null;
            debugSteering = Steering;
            debugThrottle = Throttle;
            debugBrake = Brake;
        }

        private void OnDisable()
        {
            ClearDebugValues();
        }

        private void ClearDebugValues()
        {
            keyboardAvailable = false;
            debugSteering = 0f;
            debugThrottle = 0f;
            debugBrake = 0f;
        }
    }
}
