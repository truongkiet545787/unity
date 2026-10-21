using PoseKart.Input;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Temporary read-only wrapper around the AI's existing input provider.
    /// AIRacerBrain keeps writing to AIInputProvider; this wrapper adds ink wobble
    /// without modifying AIInputProvider, CarController or the racing line.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartInkSteeringProxy : MonoBehaviour, ICarInputProvider
    {
        private ICarInputProvider original;
        private KartInkStatus ink;

        public void Configure(MonoBehaviour source, KartInkStatus status)
        {
            original = source as ICarInputProvider;
            ink = status;
        }

        public float Steering
        {
            get
            {
                if (original == null) return 0f;
                float baseValue = original.Steering;
                if (ink == null || !ink.IsInkActive) return baseValue;
                float wobble = Mathf.Sin(Time.time * 9.5f) * ink.AISteeringWobble;
                return Mathf.Clamp(baseValue + wobble, -1f, 1f);
            }
        }

        public float Throttle => original == null ? 0f :
            (ink != null && ink.IsInkActive ? original.Throttle * ink.AIThrottleMultiplier : original.Throttle);
        public float Brake => original != null ? original.Brake : 0f;
        public bool Boost => original != null && original.Boost;
        public bool IsInputValid => isActiveAndEnabled && original != null && original.IsInputValid;
    }
}
