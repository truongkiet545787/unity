using System.Collections;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class RaceManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CarController playerController;
        [SerializeField] private CheckpointManager checkpointManager;
        [SerializeField] private LapTracker lapTracker;
        [SerializeField] private RaceParticipant[] racers;
        [SerializeField] private RacePositionCalculator positionCalculator;

        [Header("Race Configuration")]
        [SerializeField, Min(1)] private int totalLaps = 3;
        [SerializeField, Min(1)] private int countdownFrom = 3;
        [SerializeField, Min(0.1f)] private float countdownStepSeconds = 1f;
        [SerializeField, Min(0f)] private float goDisplaySeconds = 0.75f;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private RaceState state = RaceState.Waiting;
        [SerializeField] private double raceTimeSeconds;
        [SerializeField] private string countdownDisplay;

        private double raceStartTime;
        private int finishedRacerCount;

        public RaceState State => state;
        public double RaceTimeSeconds => raceTimeSeconds;
        public string CountdownDisplay => countdownDisplay;
        public int TotalLaps => totalLaps;
        public int CurrentLap => PlayerRacer != null
            ? PlayerRacer.LapTracker.CurrentLap
            : lapTracker != null ? lapTracker.CurrentLap : 1;
        public LapTracker LapTracker => lapTracker;
        public int RacerCount => HasRacers ? racers.Length : 1;
        public int PlayerPosition => PlayerRacer != null ? PlayerRacer.RacePosition : 1;
        public bool IsConfigured =>
            checkpointManager != null
            && checkpointManager.IsConfigured
            && (HasConfiguredRacers() || (playerController != null && lapTracker != null));

        private bool HasRacers => racers != null && racers.Length > 0;

        private RaceParticipant PlayerRacer
        {
            get
            {
                if (!HasRacers)
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
        }

        public void Configure(
            CarController player,
            CheckpointManager checkpoints,
            LapTracker tracker,
            int laps = 3)
        {
            playerController = player;
            checkpointManager = checkpoints;
            lapTracker = tracker;
            totalLaps = Mathf.Max(1, laps);
            racers = null;
            positionCalculator = null;
        }

        public void ConfigureRacers(
            CheckpointManager checkpoints,
            RaceParticipant[] participants,
            RacePositionCalculator calculator,
            int laps = 3)
        {
            checkpointManager = checkpoints;
            racers = participants;
            positionCalculator = calculator;
            totalLaps = Mathf.Max(1, laps);

            RaceParticipant player = PlayerRacer;
            playerController = player != null ? player.CarController : null;
            lapTracker = player != null ? player.LapTracker : null;
        }

        private void Start()
        {
            if (!IsConfigured)
            {
                Debug.LogError($"{nameof(RaceManager)} on {name} is not fully configured.", this);
                enabled = false;
                return;
            }

            PrepareRace();
            StartCoroutine(RunCountdown());
        }

        private void Update()
        {
            if (state == RaceState.Racing)
            {
                raceTimeSeconds = Time.timeAsDouble - raceStartTime;
            }
        }

        private void PrepareRace()
        {
            state = RaceState.Waiting;
            raceTimeSeconds = 0d;
            countdownDisplay = string.Empty;
            finishedRacerCount = 0;
            if (HasRacers)
            {
                foreach (RaceParticipant racer in racers)
                {
                    racer.PrepareRace(totalLaps, checkpointManager.CheckpointCount);
                    racer.CarController.SetControlPermission(false);
                }
            }
            else
            {
                lapTracker.Configure(totalLaps, checkpointManager.CheckpointCount);
                playerController.SetControlPermission(false);
            }

            checkpointManager.SetAcceptingCheckpoints(false);
        }

        private IEnumerator RunCountdown()
        {
            state = RaceState.Countdown;
            for (int count = countdownFrom; count > 0; count--)
            {
                countdownDisplay = count.ToString();
                yield return new WaitForSeconds(countdownStepSeconds);
            }

            state = RaceState.Racing;
            countdownDisplay = "GO!";
            raceStartTime = Time.timeAsDouble;
            raceTimeSeconds = 0d;
            checkpointManager.SetAcceptingCheckpoints(true);
            if (HasRacers)
            {
                foreach (RaceParticipant racer in racers)
                {
                    racer.CarController.SetControlPermission(true);
                }
            }
            else
            {
                playerController.SetControlPermission(true);
            }

            if (goDisplaySeconds > 0f)
            {
                yield return new WaitForSeconds(goDisplaySeconds);
            }

            if (state == RaceState.Racing)
            {
                countdownDisplay = string.Empty;
            }
        }

        public void CompleteRace()
        {
            if (HasRacers)
            {
                RaceParticipant player = PlayerRacer;
                if (player != null)
                {
                    NotifyParticipantFinished(player);
                }

                return;
            }

            if (state != RaceState.Racing)
            {
                return;
            }

            raceTimeSeconds = Time.timeAsDouble - raceStartTime;
            state = RaceState.Finished;
            countdownDisplay = "FINISH";
            checkpointManager.SetAcceptingCheckpoints(false);
            playerController.SetControlPermission(false, true);
        }

        public void NotifyParticipantFinished(RaceParticipant racer)
        {
            if (!HasRacers || racer == null || racer.Finished ||
                (state != RaceState.Racing && state != RaceState.Finished))
            {
                return;
            }

            finishedRacerCount++;
            double elapsed = Time.timeAsDouble - raceStartTime;
            racer.MarkFinished(finishedRacerCount, elapsed);
            racer.CarController.SetControlPermission(false, true);

            if (racer.IsPlayer && state == RaceState.Racing)
            {
                raceTimeSeconds = elapsed;
                state = RaceState.Finished;
                countdownDisplay = "FINISH";
            }

            if (finishedRacerCount >= racers.Length)
            {
                checkpointManager.SetAcceptingCheckpoints(false);
            }
        }

        private bool HasConfiguredRacers()
        {
            if (!HasRacers || positionCalculator == null || !positionCalculator.IsConfigured || PlayerRacer == null)
            {
                return false;
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null || !racer.IsConfigured)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
