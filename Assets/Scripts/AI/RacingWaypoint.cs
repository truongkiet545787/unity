using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class RacingWaypoint : MonoBehaviour
    {
        [SerializeField, Min(0)] private int index;
        [SerializeField, Range(0.5f, 1.1f)] private float recommendedSpeedFactor = 1f;

        public int Index => index;
        public float RecommendedSpeedFactor => recommendedSpeedFactor;

        public void Configure(int waypointIndex, float speedFactor = 1f)
        {
            index = Mathf.Max(0, waypointIndex);
            recommendedSpeedFactor = Mathf.Clamp(speedFactor, 0.5f, 1.1f);
        }
    }
}
