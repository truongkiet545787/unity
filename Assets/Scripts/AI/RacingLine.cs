using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class RacingLine : MonoBehaviour
    {
        [SerializeField] private RacingWaypoint[] waypoints;

        public int Count => waypoints?.Length ?? 0;
        public bool IsConfigured => waypoints != null && waypoints.Length >= 3;

        public void Configure(RacingWaypoint[] orderedWaypoints)
        {
            waypoints = orderedWaypoints;
        }

        public int FindStartingWaypoint(Vector3 position)
        {
            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < Count; index++)
            {
                float distance = (waypoints[index].transform.position - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = index;
                }
            }

            return (nearest + 1) % Count;
        }

        public void AdvanceProgress(ref int nextWaypointIndex, Vector3 position, float reachDistance)
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                int previousIndex = (nextWaypointIndex - 1 + Count) % Count;
                Vector3 previous = GetPosition(previousIndex);
                Vector3 target = GetPosition(nextWaypointIndex);
                Vector3 segmentDirection = (target - previous).normalized;
                bool passedTarget = Vector3.Dot(position - target, segmentDirection) > 0f;
                bool reachedTarget = Vector3.Distance(position, target) <= reachDistance;
                if (!passedTarget && !reachedTarget)
                {
                    break;
                }

                nextWaypointIndex = (nextWaypointIndex + 1) % Count;
            }
        }

        public Vector3 GetLookAheadPoint(int nextWaypointIndex, Vector3 position, float lookAheadDistance)
        {
            Vector3 cursor = position;
            float remaining = Mathf.Max(0f, lookAheadDistance);
            int index = nextWaypointIndex;

            for (int iteration = 0; iteration < Count; iteration++)
            {
                Vector3 target = GetPosition(index);
                float segmentLength = Vector3.Distance(cursor, target);
                if (segmentLength >= remaining && segmentLength > 0.01f)
                {
                    return Vector3.Lerp(cursor, target, remaining / segmentLength);
                }

                remaining -= segmentLength;
                cursor = target;
                index = (index + 1) % Count;
            }

            return cursor;
        }

        public float GetCornerSeverity(int nextWaypointIndex, int previewSegments)
        {
            float maximumSeverity = 0f;
            int previewCount = Mathf.Clamp(previewSegments, 1, Count - 1);
            for (int offset = 0; offset < previewCount; offset++)
            {
                int a = (nextWaypointIndex + offset - 1 + Count) % Count;
                int b = (nextWaypointIndex + offset) % Count;
                int c = (nextWaypointIndex + offset + 1) % Count;
                Vector3 incoming = (GetPosition(b) - GetPosition(a)).normalized;
                Vector3 outgoing = (GetPosition(c) - GetPosition(b)).normalized;
                maximumSeverity = Mathf.Max(maximumSeverity, Mathf.Clamp01(Vector3.Angle(incoming, outgoing) / 90f));
            }

            return maximumSeverity;
        }

        public float GetRecommendedSpeedFactor(int waypointIndex)
        {
            return waypoints[waypointIndex % Count].RecommendedSpeedFactor;
        }

        public Vector3 GetPosition(int index)
        {
            return waypoints[(index + Count) % Count].transform.position;
        }

        public Quaternion GetForwardRotation(int index)
        {
            Vector3 direction = GetPosition(index + 1) - GetPosition(index);
            return direction.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
        }
    }
}
