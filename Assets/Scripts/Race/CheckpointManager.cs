using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class CheckpointManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody playerBody;
        [SerializeField] private LapTracker lapTracker;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private Checkpoint[] orderedCheckpoints;
        [SerializeField] private RaceParticipant[] racers;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private bool acceptingCheckpoints;

        public int CheckpointCount => orderedCheckpoints?.Length ?? 0;
        public bool IsConfigured =>
            raceManager != null
            && orderedCheckpoints != null
            && orderedCheckpoints.Length >= 2
            && (HasConfiguredRacers() || (playerBody != null && lapTracker != null));

        public void Configure(
            Rigidbody player,
            LapTracker tracker,
            RaceManager manager,
            Checkpoint[] checkpoints)
        {
            playerBody = player;
            lapTracker = tracker;
            raceManager = manager;
            orderedCheckpoints = checkpoints;
            racers = null;

            ConfigureCheckpointIndices();
        }

        public void ConfigureRacers(
            RaceManager manager,
            Checkpoint[] checkpoints,
            RaceParticipant[] participants)
        {
            raceManager = manager;
            orderedCheckpoints = checkpoints;
            racers = participants;

            RaceParticipant player = FindPlayer();
            playerBody = player != null ? player.Body : null;
            lapTracker = player != null ? player.LapTracker : null;
            ConfigureCheckpointIndices();
        }

        private void ConfigureCheckpointIndices()
        {
            if (orderedCheckpoints == null)
            {
                return;
            }

            for (int index = 0; index < orderedCheckpoints.Length; index++)
            {
                orderedCheckpoints[index].Configure(index, this);
            }
        }

        public void SetAcceptingCheckpoints(bool accepting)
        {
            acceptingCheckpoints = accepting;
        }

        public void TryPassCheckpoint(Checkpoint checkpoint, Collider other)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer)
            {
                return;
            }

            if (!acceptingCheckpoints || checkpoint == null || other.attachedRigidbody == null)
            {
                return;
            }

            int index = checkpoint.CheckpointIndex;
            if (index < 0 || index >= CheckpointCount || orderedCheckpoints[index] != checkpoint)
            {
                return;
            }

            RaceParticipant racer = FindRacer(other.attachedRigidbody);
            if (racer != null)
            {
                CheckpointProgressResult racerResult = racer.LapTracker.RegisterCheckpoint(index);
                Debug.Log(
    $"[Checkpoint] " +
    $"{racer.RacerName} | " +
    $"CP={index} | " +
    $"Result={racerResult} | " +
    $"Lap={racer.LapTracker.CurrentLap}/{racer.LapTracker.TotalLaps} | " +
    $"Next={racer.LapTracker.ExpectedCheckpoint}",
    racer
);
                if (racerResult == CheckpointProgressResult.RaceCompleted)
                {
                    raceManager.NotifyParticipantFinished(racer);
                }

                raceManager.PublishNetworkRaceProgress();

                return;
            }

            if (other.attachedRigidbody != playerBody || lapTracker == null)
            {
                return;
            }

            CheckpointProgressResult result = lapTracker.RegisterCheckpoint(index);
            if (result == CheckpointProgressResult.RaceCompleted)
            {
                acceptingCheckpoints = false;
                raceManager.CompleteRace();
            }

            raceManager.PublishNetworkRaceProgress();
        }

        public Checkpoint GetCheckpoint(int index)
        {
            return index >= 0 && index < CheckpointCount ? orderedCheckpoints[index] : null;
        }

        private bool HasConfiguredRacers()
        {
            if (racers == null || racers.Length == 0)
                return false;

            bool foundRacer = false;

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null)
                    continue;

                if (!racer.IsConfigured)
                    return false;

                foundRacer = true;
            }

            return foundRacer;
        }

        private RaceParticipant FindPlayer()
        {
            if (racers == null)
            {
                return null;
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer != null && racer.IsPlayer)
                {
                    return racer;
                }
            }

            return null;
        }

        private RaceParticipant FindRacer(Rigidbody body)
        {
            if (racers == null)
            {
                return null;
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer != null && racer.Body == body)
                {
                    return racer;
                }
            }

            return null;
        }
    }
}
