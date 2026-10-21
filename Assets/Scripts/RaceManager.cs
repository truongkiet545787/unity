using System;
using System.Collections;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class RaceManager : MonoBehaviour
    {
        [Header("Cinematic Race Start")]
        [SerializeField] private global::PoseKartRaceIntroCamera raceIntroCamera;
        [Header("References")]
        [SerializeField] private CarController playerController;
        [SerializeField] private CheckpointManager checkpointManager;
        [SerializeField] private LapTracker lapTracker;
        [SerializeField] private RaceParticipant[] racers;
        [SerializeField] private RacePositionCalculator positionCalculator;

        [Header("Race Configuration")]
        
        [SerializeField, Min(1)] private int totalLaps = 3;
        [SerializeField] private bool skipCountdown = true;
        [SerializeField, Min(1)] private int countdownFrom = 3;
        [SerializeField, Min(0.1f)] private float countdownStepSeconds = 1f;
        [SerializeField, Min(0f)] private float goDisplaySeconds = 0.75f;

        [Header("Asphalt Starting Grid")]
        [Tooltip("Places the four configured racers into a two-row grid once before the countdown.")]
        [SerializeField] private bool useStartingGrid = true;
        [Tooltip("Optional track-aligned reference. Its position is the front-left slot and its forward axis points down the track. If empty, the player's scene spawn is used.")]
        [SerializeField] private Transform startingGridReference;
        [SerializeField, Min(0.5f)] private float frontRowLateralSpacing = 3.2f;
        [SerializeField, Min(0.5f)] private float backRowLateralSpacing = 3.2f;
        [SerializeField, Min(0.5f)] private float rowSeparation = 5.5f;
        [Tooltip("Lateral offset applied to both back-row slots. Positive values stagger them to the right of the front row.")]
        [SerializeField] private float backRowStagger = 0.45f;
        [SerializeField] private float startingHeightOffset = 0.06f;
        [Tooltip("Explicit assignments are recommended. Empty fields are resolved from the existing player/AI names.")]
        [SerializeField] private RaceParticipant frontLeftRacer;
        [SerializeField] private RaceParticipant frontRightRacer;
        [SerializeField] private RaceParticipant backLeftRacer;
        [SerializeField] private RaceParticipant backRightRacer;
        [SerializeField] private bool clearVelocitiesOnGridPlacement = true;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private RaceState state = RaceState.Waiting;
        [SerializeField] private double raceTimeSeconds;
        [SerializeField] private string countdownDisplay;
        [SerializeField] private double playerFinishTimeSeconds;
        [SerializeField] private int playerFinishPosition;
        [SerializeField] private bool playerFinished;

        private double raceStartTime;
        private int finishedRacerCount;
        private bool cachedFallbackGridReference;
        private Vector3 fallbackGridPosition;
        private Quaternion fallbackGridRotation;
        private float targetGoTime;

        public RaceState State => state;
        public double RaceTimeSeconds => raceTimeSeconds;
        public string CountdownDisplay => countdownDisplay;
        public bool PlayerFinished => playerFinished;
        public double PlayerFinishTimeSeconds => playerFinishTimeSeconds;
        public int PlayerFinishPosition => playerFinishPosition > 0 ? playerFinishPosition : PlayerPosition;
        public float SecondsUntilGo => targetGoTime > 0f ? Mathf.Max(0f, targetGoTime - Time.time) : 0f;
        public bool IsInCountdown => state == RaceState.Countdown;
        public event Action<int, string> CountdownTickEvent;
        public event Action RaceStartedEvent;
        public int TotalLaps => totalLaps;
        public CarController PlayerController => playerController != null
            ? playerController
            : PlayerRacer != null ? PlayerRacer.CarController : null;
        public int CurrentLap => PlayerRacer != null
            ? PlayerRacer.LapTracker.CurrentLap
            : lapTracker != null ? lapTracker.CurrentLap : 1;
        public LapTracker LapTracker => lapTracker;
        public int RacerCount => HasRacers ? racers.Length : 1;
        public RaceParticipant[] Racers => racers;
        public int PlayerPosition => PlayerRacer != null ? PlayerRacer.RacePosition : 1;
        public event Action<RaceManager> PlayerFinishedEvent;
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
            StartCoroutine(RunRaceStartSequence());
        }


        private IEnumerator RunRaceStartSequence()
        {
            // Keep the race locked while the cinematic intro plays.
            state = RaceState.Waiting;
            countdownDisplay = string.Empty;

            // Let the starting grid finish its initial setup.
            yield return null;

            // Wait for the ENTIRE cinematic, including the chase-camera blend.
            if (raceIntroCamera != null)
            {
                yield return raceIntroCamera.PlayIntro();
            }
            else
            {
                Debug.LogWarning(
                    "[RaceManager] Race Intro Camera is not assigned. " +
                    "Starting the normal countdown instead.",
                    this
                );
            }

            // Only now begin the existing HUD countdown.
            yield return RunCountdown();
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
            playerFinishTimeSeconds = 0d;
            playerFinishPosition = 0;
            playerFinished = false;
            finishedRacerCount = 0;
            if (HasRacers)
            {
                ApplyStartingGrid();
                foreach (RaceParticipant racer in racers)
                {
                    racer.PrepareRace(totalLaps, checkpointManager.CheckpointCount);
                    racer.CarController.SetControlPermission(false);
                    racer.CarController.GetComponent<KartItemInventory>()?.ResetForRace();
                    racer.CarController.GetComponentInChildren<PoseKart.Vehicle.PoseKartDriftVFX>()?.ResetForRace();
                }
            }
            else
            {
                lapTracker.Configure(totalLaps, checkpointManager.CheckpointCount);
                playerController.SetControlPermission(false);
                playerController.GetComponent<KartItemInventory>()?.ResetForRace();
                playerController.GetComponentInChildren<PoseKart.Vehicle.PoseKartDriftVFX>()?.ResetForRace();
            }

            checkpointManager.SetAcceptingCheckpoints(false);
        }

        private void ApplyStartingGrid()
        {
            if (!useStartingGrid || racers == null || racers.Length != 4)
            {
                if (useStartingGrid && racers != null && racers.Length != 4)
                {
                    Debug.LogWarning($"{nameof(RaceManager)} starting grid expects exactly four racers; found {racers.Length}. Grid placement was skipped.", this);
                }

                return;
            }

            ResolveStartingGridAssignments();
            if (frontLeftRacer == null || frontRightRacer == null || backLeftRacer == null || backRightRacer == null)
            {
                Debug.LogWarning($"{nameof(RaceManager)} could not resolve all four starting-grid racers. Assign the grid slots in the Inspector.", this);
                return;
            }

            if (AreDuplicateGridAssignments())
            {
                Debug.LogWarning($"{nameof(RaceManager)} has duplicate starting-grid racer assignments. Grid placement was skipped.", this);
                return;
            }

            Transform reference = startingGridReference;
            Vector3 origin;
            Quaternion gridRotation;
            if (reference != null)
            {
                origin = reference.position;
                gridRotation = GetTrackAlignedRotation(reference.forward);
            }
            else if (cachedFallbackGridReference)
            {
                origin = fallbackGridPosition;
                gridRotation = fallbackGridRotation;
            }
            else
            {
                Transform playerTransform = frontLeftRacer.Body != null
                    ? frontLeftRacer.Body.transform
                    : playerController != null ? playerController.transform : null;
                if (playerTransform == null)
                {
                    Debug.LogWarning($"{nameof(RaceManager)} has no transform to use as a starting-grid reference.", this);
                    return;
                }

                origin = playerTransform.position;
                gridRotation = GetTrackAlignedRotation(playerTransform.forward);
                fallbackGridPosition = origin;
                fallbackGridRotation = gridRotation;
                cachedFallbackGridReference = true;
            }

            float frontSpacing = Mathf.Max(0.5f, frontRowLateralSpacing);
            float backSpacing = Mathf.Max(0.5f, backRowLateralSpacing);
            float separation = Mathf.Max(0.5f, rowSeparation);
            Vector3 forward = gridRotation * Vector3.forward;
            Vector3 right = gridRotation * Vector3.right;
            Vector3 heightOffset = Vector3.up * startingHeightOffset;

            ApplyGridSlot(frontLeftRacer, origin + heightOffset, gridRotation);
            ApplyGridSlot(frontRightRacer, origin + right * frontSpacing + heightOffset, gridRotation);
            ApplyGridSlot(backLeftRacer, origin - forward * separation + right * backRowStagger + heightOffset, gridRotation);
            ApplyGridSlot(backRightRacer, origin - forward * separation + right * (backRowStagger + backSpacing) + heightOffset, gridRotation);
            Physics.SyncTransforms();
        }

        private void ResolveStartingGridAssignments()
        {
            frontLeftRacer ??= PlayerRacer;
            frontRightRacer ??= FindRacerByKey("AI_Coral");
            backLeftRacer ??= FindRacerByKey("AI_Azure");
            backRightRacer ??= FindRacerByKey("AI_Sunset");
        }

        private RaceParticipant FindRacerByKey(string key)
        {
            if (racers == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null)
                {
                    continue;
                }

                if (string.Equals(racer.RacerName, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(racer.name, key, StringComparison.OrdinalIgnoreCase) ||
                    (racer.CarController != null && string.Equals(racer.CarController.name, key, StringComparison.OrdinalIgnoreCase)))
                {
                    return racer;
                }
            }

            return null;
        }

        private bool AreDuplicateGridAssignments()
        {
            return frontLeftRacer == frontRightRacer || frontLeftRacer == backLeftRacer ||
                frontLeftRacer == backRightRacer || frontRightRacer == backLeftRacer ||
                frontRightRacer == backRightRacer || backLeftRacer == backRightRacer;
        }

        private static Quaternion GetTrackAlignedRotation(Vector3 forward)
        {
            Vector3 flattenedForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flattenedForward.sqrMagnitude < 0.001f)
            {
                flattenedForward = Vector3.forward;
            }

            return Quaternion.LookRotation(flattenedForward.normalized, Vector3.up);
        }

        private void ApplyGridSlot(RaceParticipant racer, Vector3 position, Quaternion rotation)
        {
            if (racer == null || racer.Body == null)
            {
                return;
            }

            Rigidbody body = racer.Body;
            body.position = position;
            body.rotation = rotation;
            if (clearVelocitiesOnGridPlacement)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
                body.WakeUp();
            }

            body.GetComponent<CarResetController>()?.SetStartingSnapshot(position, rotation);
        }

        private IEnumerator RunCountdown()
        {
            // Chỉ đếm ngược nếu KHÔNG bật skipCountdown
            if (!skipCountdown)
            {
                state = RaceState.Countdown;
                targetGoTime = Time.time + (countdownFrom * countdownStepSeconds);

                for (int count = countdownFrom; count > 0; count--)
                {
                    countdownDisplay = count.ToString();
                    CountdownTickEvent?.Invoke(count, countdownDisplay);
                    PoseKart.Audio.AudioManager.PlayCountdown(countdownDisplay);

                    float stepEnd = Time.time + countdownStepSeconds;
                    while (Time.time < stepEnd)
                    {
                        yield return null;
                    }
                }
            }

            // Phần mở khoá xe, bật checkpoint và bắt đầu tính giờ giữ nguyên:
            state = RaceState.Racing;
            targetGoTime = 0f;
            countdownDisplay = "GO!";
            CountdownTickEvent?.Invoke(0, "GO!");
            PoseKart.Audio.AudioManager.PlayCountdown("GO!");
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

            RaceStartedEvent?.Invoke();

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
            if (playerFinished)
            {
                return;
            }

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
            CompletePlayerFinish(raceTimeSeconds, 1);
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
                CompletePlayerFinish(elapsed, racer.RacePosition);
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

        private void CompletePlayerFinish(double finishTime, int finishPosition)
        {
            if (playerFinished)
            {
                return;
            }

            playerFinished = true;
            playerFinishTimeSeconds = Math.Max(0d, finishTime);
            playerFinishPosition = Mathf.Max(1, finishPosition);
            PlayerFinishedEvent?.Invoke(this);
        }
    }
}
