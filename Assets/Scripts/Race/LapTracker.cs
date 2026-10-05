using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class LapTracker : MonoBehaviour
    {
        [Header("Race Configuration")]
        [SerializeField, Min(1)] private int totalLaps = 3;
        [SerializeField, Min(2)] private int checkpointCount = 2;

        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Current Lap")]
        [SerializeField] private int currentLap = 1;
        [InspectorName("Expected Checkpoint")]
        [SerializeField] private int expectedCheckpoint = 1;
        [InspectorName("Last Valid Checkpoint")]
        [SerializeField] private int lastValidCheckpoint = -1;
        [InspectorName("Checkpoints Passed")]
        [SerializeField] private int checkpointsPassed;
        [SerializeField] private bool raceCompleted;

        public int TotalLaps => totalLaps;
        public int CurrentLap => currentLap;
        public int ExpectedCheckpoint => expectedCheckpoint;
        public int LastValidCheckpoint => lastValidCheckpoint;
        public int CheckpointsPassed => checkpointsPassed;
        public bool RaceCompleted => raceCompleted;

        public void Configure(int laps, int orderedCheckpointCount)
        {
            totalLaps = Mathf.Max(1, laps);
            checkpointCount = Mathf.Max(2, orderedCheckpointCount);
            ResetProgress();
        }

        public void ResetProgress()
        {
            currentLap = 1;
            expectedCheckpoint = 1;
            lastValidCheckpoint = -1;
            checkpointsPassed = 0;
            raceCompleted = false;
        }

        public CheckpointProgressResult RegisterCheckpoint(int checkpointIndex)
        {
            if (raceCompleted || checkpointIndex != expectedCheckpoint)
            {
                return CheckpointProgressResult.Ignored;
            }

            lastValidCheckpoint = checkpointIndex;

            if (checkpointIndex == 0)
            {
                if (currentLap >= totalLaps)
                {
                    raceCompleted = true;
                    expectedCheckpoint = -1;
                    return CheckpointProgressResult.RaceCompleted;
                }

                currentLap++;
                checkpointsPassed = 0;
                expectedCheckpoint = 1;
                return CheckpointProgressResult.LapCompleted;
            }

            checkpointsPassed++;
            expectedCheckpoint = checkpointIndex + 1 < checkpointCount
                ? checkpointIndex + 1
                : 0;
            return CheckpointProgressResult.Advanced;
        }
    }
}
