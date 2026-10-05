using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class RacePositionCalculator : MonoBehaviour
    {
        [SerializeField] private CheckpointManager checkpointManager;
        [SerializeField] private RaceParticipant[] racers;

        private float[] progressScores;
        private int[] sortedIndices;

        public bool IsConfigured =>
            checkpointManager != null
            && racers != null
            && racers.Length > 0;

        public void Configure(CheckpointManager checkpoints, RaceParticipant[] participants)
        {
            checkpointManager = checkpoints;
            racers = participants;
            progressScores = new float[racers.Length];
            sortedIndices = new int[racers.Length];
        }

        private void LateUpdate()
        {
            if (!IsConfigured || checkpointManager.CheckpointCount < 2)
            {
                return;
            }

            EnsureBuffers();
            for (int index = 0; index < racers.Length; index++)
            {
                sortedIndices[index] = index;
                progressScores[index] = CalculateProgress(racers[index]);
            }

            for (int left = 0; left < sortedIndices.Length - 1; left++)
            {
                for (int right = left + 1; right < sortedIndices.Length; right++)
                {
                    if (progressScores[sortedIndices[right]] > progressScores[sortedIndices[left]])
                    {
                        (sortedIndices[left], sortedIndices[right]) = (sortedIndices[right], sortedIndices[left]);
                    }
                }
            }

            for (int rank = 0; rank < sortedIndices.Length; rank++)
            {
                racers[sortedIndices[rank]].SetRacePosition(rank + 1);
            }
        }

        private float CalculateProgress(RaceParticipant racer)
        {
            if (racer == null || !racer.IsConfigured)
            {
                return float.NegativeInfinity;
            }

            if (racer.Finished)
            {
                return 100000f - racer.FinishOrder;
            }

            LapTracker tracker = racer.LapTracker;
            int checkpointCount = checkpointManager.CheckpointCount;
            int expected = tracker.ExpectedCheckpoint;
            int passedThisLap = tracker.CheckpointsPassed;
            float baseProgress = (tracker.CurrentLap - 1) * checkpointCount + passedThisLap;
            if (expected < 0)
            {
                return baseProgress + 1f;
            }

            int previous = expected == 1
                ? 0
                : expected == 0
                    ? checkpointCount - 1
                    : expected - 1;
            Vector3 segmentStart = checkpointManager.GetCheckpoint(previous).transform.position;
            Vector3 segmentEnd = checkpointManager.GetCheckpoint(expected).transform.position;
            Vector3 segment = segmentEnd - segmentStart;
            float segmentProgress = segment.sqrMagnitude > 0.01f
                ? Mathf.Clamp01(Vector3.Dot(racer.Body.position - segmentStart, segment) / segment.sqrMagnitude)
                : 0f;
            return baseProgress + segmentProgress;
        }

        private void EnsureBuffers()
        {
            if (progressScores == null || progressScores.Length != racers.Length)
            {
                progressScores = new float[racers.Length];
                sortedIndices = new int[racers.Length];
            }
        }
    }
}
