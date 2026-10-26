using UnityEngine;
using PoseKart.Multiplayer;

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

        public void ApplyNetworkProgress(PoseKartRaceProgressState state)
        {
            currentLap = Mathf.Clamp(state.currentLap, 1, totalLaps);
            expectedCheckpoint = state.expectedCheckpoint;
            checkpointsPassed = Mathf.Max(0, state.checkpointsPassed);
            raceCompleted = state.finished;
            lastValidCheckpoint = expectedCheckpoint < 0
                ? 0
                : Mathf.Max(-1, expectedCheckpoint - 1);
        }

        public CheckpointProgressResult RegisterCheckpoint(int checkpointIndex)
        {
            if (raceCompleted)
            {
                return CheckpointProgressResult.Ignored;
            }

            // Normal progression
            if (checkpointIndex == expectedCheckpoint)
            {
                lastValidCheckpoint = checkpointIndex;

                if (checkpointIndex == 0)
                {
                    // Checkpoint 0 is the start/finish line. The lap shown in the HUD is
                    // one-based (the car starts on lap 1), so reaching the line while on
                    // the final lap completes the race rather than creating an extra lap.
                    currentLap = Mathf.Clamp(currentLap, 1, totalLaps);
                    if (currentLap >= totalLaps)
                    {
                        currentLap = totalLaps;
                        checkpointsPassed = Mathf.Max(0, checkpointCount - 1);
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

            // Fallback 1: Reaching the start/finish line (checkpoint 0).
            // If the racer has covered at least half the track (or last valid checkpoint was >= checkpointCount / 2),
            // complete the lap even if an intermediate checkpoint was slightly missed on a wide drift.
            if (checkpointIndex == 0 && (checkpointsPassed >= Mathf.Max(2, checkpointCount / 2) || lastValidCheckpoint >= checkpointCount / 2))
            {
                lastValidCheckpoint = 0;
                currentLap = Mathf.Clamp(currentLap, 1, totalLaps);
                if (currentLap >= totalLaps)
                {
                    currentLap = totalLaps;
                    checkpointsPassed = Mathf.Max(0, checkpointCount - 1);
                    raceCompleted = true;
                    expectedCheckpoint = -1;
                    return CheckpointProgressResult.RaceCompleted;
                }

                currentLap++;
                checkpointsPassed = 0;
                expectedCheckpoint = 1;
                return CheckpointProgressResult.LapCompleted;
            }

            // Fallback 2: If a racer missed a single checkpoint by drifting wide (e.g. hit checkpointIndex == expectedCheckpoint + 1),
            // advance forward rather than locking them out of all remaining checkpoints for the entire race.
            if (expectedCheckpoint > 0 && checkpointIndex == expectedCheckpoint + 1)
            {
                lastValidCheckpoint = checkpointIndex;
                checkpointsPassed += 2;
                expectedCheckpoint = checkpointIndex + 1 < checkpointCount
                    ? checkpointIndex + 1
                    : 0;
                return CheckpointProgressResult.Advanced;
            }

            return CheckpointProgressResult.Ignored;
        }
    }
}
