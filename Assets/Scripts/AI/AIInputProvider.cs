using PoseKart.Input;
using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class AIInputProvider : MonoBehaviour, ICarInputProvider
    {
        [Header("Runtime Output (Read Only)")]
        [SerializeField, Range(-1f, 1f)] private float steering;
        [SerializeField, Range(0f, 1f)] private float throttle;
        [SerializeField, Range(0f, 1f)] private float brake;

        public float Steering => steering;
        public float Throttle => throttle;
        public float Brake => brake;
        public bool Boost => false;
        public bool IsInputValid => isActiveAndEnabled;

        public void SetInput(float steeringValue, float throttleValue, float brakeValue)
        {
            steering = Mathf.Clamp(steeringValue, -1f, 1f);
            throttle = Mathf.Clamp01(throttleValue);
            brake = Mathf.Clamp01(brakeValue);
        }

        private void OnDisable()
        {
            SetInput(0f, 0f, 0f);
        }
    }
}
