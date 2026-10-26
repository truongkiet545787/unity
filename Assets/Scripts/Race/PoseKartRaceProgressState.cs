using PoseKart.Race;
using System;
using Unity.Netcode;

namespace PoseKart.Multiplayer
{
    /// <summary>
    /// Server-authored race progress carried by the existing networked racer
    /// objects. It changes on checkpoint/rank/finish updates, not every frame.
    /// </summary>
    public struct PoseKartRaceProgressState : INetworkSerializable, IEquatable<PoseKartRaceProgressState>
    {
        public int currentLap;
        public int expectedCheckpoint;
        public int checkpointsPassed;
        public int racePosition;
        public bool finished;
        public int finishOrder;
        public double finishTime;

        public static PoseKartRaceProgressState From(RaceParticipant racer)
        {
            LapTracker tracker = racer != null ? racer.LapTracker : null;
            return new PoseKartRaceProgressState
            {
                currentLap = tracker != null ? tracker.CurrentLap : 1,
                expectedCheckpoint = tracker != null ? tracker.ExpectedCheckpoint : 1,
                checkpointsPassed = tracker != null ? tracker.CheckpointsPassed : 0,
                racePosition = racer != null ? racer.RacePosition : 1,
                finished = racer != null && racer.Finished,
                finishOrder = racer != null ? racer.FinishOrder : 0,
                finishTime = racer != null ? racer.FinishTime : 0d
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref currentLap);
            serializer.SerializeValue(ref expectedCheckpoint);
            serializer.SerializeValue(ref checkpointsPassed);
            serializer.SerializeValue(ref racePosition);
            serializer.SerializeValue(ref finished);
            serializer.SerializeValue(ref finishOrder);
            serializer.SerializeValue(ref finishTime);
        }

        public bool Equals(PoseKartRaceProgressState other)
        {
            return currentLap == other.currentLap &&
                   expectedCheckpoint == other.expectedCheckpoint &&
                   checkpointsPassed == other.checkpointsPassed &&
                   racePosition == other.racePosition &&
                   finished == other.finished &&
                   finishOrder == other.finishOrder &&
                   finishTime.Equals(other.finishTime);
        }

        public override bool Equals(object obj)
        {
            return obj is PoseKartRaceProgressState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = currentLap;
                hash = hash * 31 + expectedCheckpoint;
                hash = hash * 31 + checkpointsPassed;
                hash = hash * 31 + racePosition;
                hash = hash * 31 + (finished ? 1 : 0);
                hash = hash * 31 + finishOrder;
                hash = hash * 31 + finishTime.GetHashCode();
                return hash;
            }
        }
    }
}
