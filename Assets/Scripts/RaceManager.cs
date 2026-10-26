using PoseKart.Multiplayer;
using PoseKart.Vehicle;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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

        [Tooltip("Places all configured racers into a two-column starting grid before the countdown.")]
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


        [SerializeField] private bool clearVelocitiesOnGridPlacement = true;



        [Header("Runtime Debug (Read Only)")]
        [Header("Online Runtime")]
        [SerializeField, Min(1)]
        private int onlineExpectedRacers = 8;

        [SerializeField, Min(1f)]
        private float onlineRosterTimeoutSeconds = 10f;

        [SerializeField]
        private bool onlineRuntimeRace;
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

        private bool onlineRaceStartBlocked;



        public RaceState State => state;

        public double RaceTimeSeconds => raceTimeSeconds;

        public string CountdownDisplay => countdownDisplay;

        public bool PlayerFinished => playerFinished;

        public double PlayerFinishTimeSeconds => playerFinishTimeSeconds;

        public int PlayerFinishPosition => playerFinishPosition > 0 ? playerFinishPosition : PlayerPosition;

        public float SecondsUntilGo
        {
            get
            {
                if (onlineRuntimeRace &&
                    networkRaceClock != null &&
                    networkRaceClock.HasGoTime)
                {
                    return Mathf.Max(
                        0f,
                        (float)(networkRaceClock.GoServerTime -
                                networkRaceClock.ServerNow)
                    );
                }

                return targetGoTime > 0f
                    ? Mathf.Max(0f, targetGoTime - Time.time)
                    : 0f;
            }
        }

        public bool IsInCountdown => state == RaceState.Countdown;
        private PoseKartNetworkRaceClock networkRaceClock;
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



        private IEnumerator Start()
        {
            onlineRuntimeRace =
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening;

            // =========================================
            // ONLINE
            // =========================================

            if (onlineRuntimeRace)
            {
                Debug.Log(
                    "[RaceManager] ONLINE race detected. " +
                    "Waiting for network racers...",
                    this
                );

                yield return WaitForOnlineRacers();

                yield break;
            }


            // =========================================
            // OFFLINE - GIỮ FLOW CŨ
            // =========================================

            if (!IsConfigured)
            {
                Debug.LogError(
                    $"{nameof(RaceManager)} on {name} is not fully configured.",
                    this
                );

                enabled = false;

                yield break;
            }


            PrepareRace();

            StartCoroutine(
                RunRaceStartSequence()
            );
        }

        private IEnumerator WaitForOnlineRacers()
        {
            float deadline =
                Time.realtimeSinceStartup +
                onlineRosterTimeoutSeconds;

            RaceParticipant[] onlineRacers = null;
            RaceParticipant localPlayer = null;


            // =====================================================
            // 1. ĐỢI ĐỦ NETWORK RACER
            // =====================================================

            while (Time.realtimeSinceStartup < deadline)
            {
                NetworkObject[] networkObjects =
                    FindObjectsByType<NetworkObject>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None
                    );

                List<RaceParticipant> valid =
                    new List<RaceParticipant>();

                localPlayer = null;


                foreach (NetworkObject netObject in networkObjects)
                {
                    if (netObject == null ||
                        !netObject.IsSpawned)
                    {
                        continue;
                    }


                    // RaceParticipant có thể nằm root hoặc child.
                    RaceParticipant racer =
                        netObject.GetComponent<RaceParticipant>();

                    if (racer == null)
                    {
                        racer =
                            netObject.GetComponentInChildren<RaceParticipant>(
                                true
                            );
                    }


                    // Không phải xe đua -> bỏ.
                    if (racer == null)
                        continue;


                    // =================================================
                    // TỰ BIND LẠI RUNTIME REFERENCES
                    // =================================================

                    Rigidbody body =
                        racer.GetComponent<Rigidbody>();

                    if (body == null)
                    {
                        body =
                            racer.GetComponentInParent<Rigidbody>();
                    }


                    CarController controller =
                        racer.GetComponent<CarController>();

                    if (controller == null)
                    {
                        controller =
                            racer.GetComponentInParent<CarController>();
                    }


                    LapTracker tracker =
                        racer.GetComponent<LapTracker>();

                    if (tracker == null)
                    {
                        tracker =
                            racer.GetComponentInParent<LapTracker>();
                    }


                    if (body == null ||
                        controller == null ||
                        tracker == null)
                    {
                        Debug.LogWarning(
                            $"[RaceManager] Skip {netObject.name} | " +
                            $"Body={body != null} | " +
                            $"Car={controller != null} | " +
                            $"Lap={tracker != null}",
                            netObject
                        );

                        continue;
                    }


                    // =================================================
                    // PLAYER LOCAL CHỈ LÀ NETWORKOBJECT MÀ MÁY NÀY OWNER
                    // =================================================

                    bool isLocalPlayer =
                        netObject.IsPlayerObject &&
                        netObject.IsOwner;


                    string racerName;

                    if (netObject.IsPlayerObject)
                    {
                        PoseKartNetworkPlayerOwner networkPlayer =
                            netObject.GetComponent<PoseKartNetworkPlayerOwner>();

                        racerName = networkPlayer != null
                            ? networkPlayer.DisplayName
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(racerName))
                        {
                            racerName = string.IsNullOrWhiteSpace(racer.RacerName)
                                ? $"Player {netObject.OwnerClientId + 1}"
                                : racer.RacerName;
                        }
                    }
                    else
                    {
                        racerName =
                            string.IsNullOrWhiteSpace(racer.RacerName)
                                ? netObject.name
                                : racer.RacerName;
                    }


                    racer.Configure(
                        racerName,
                        isLocalPlayer,
                        body,
                        controller,
                        tracker
                    );


                    valid.Add(racer);


                    if (isLocalPlayer)
                    {
                        localPlayer = racer;
                    }
                }


                Debug.Log(
                    $"[RaceManager] Searching roster... " +
                    $"Found={valid.Count}/{onlineExpectedRacers} | " +
                    $"LocalPlayer={(localPlayer != null ? localPlayer.RacerName : "NONE")}",
                    this
                );
                if (valid.Count >= onlineExpectedRacers &&
                    localPlayer != null)
                {
                    onlineRacers =
                        valid.ToArray();

                    break;
                }


                yield return null;
            }


            // =====================================================
            // 2. TIMEOUT
            // =====================================================

            if (onlineRacers == null ||
                onlineRacers.Length < onlineExpectedRacers)
            {
                Debug.LogError(
                    $"[RaceManager] ONLINE ROSTER TIMEOUT | " +
                    $"Expected={onlineExpectedRacers} | " +
                    $"Found={(onlineRacers == null ? 0 : onlineRacers.Length)}",
                    this
                );

                AbortOnlineRaceStart(
                    "Network racer roster did not complete before timeout."
                );

                yield break;
            }


            if (localPlayer == null)
            {
                Debug.LogError(
                    "[RaceManager] LOCAL PLAYER NOT FOUND.",
                    this
                );

                AbortOnlineRaceStart(
                    "Local player object was not available."
                );

                yield break;
            }


            PoseKart.UI.PoseKartMinimap minimap =
                FindFirstObjectByType<PoseKart.UI.PoseKartMinimap>();

            if (minimap != null)
            {
                minimap.ConfigureRuntimeRacers(
                    onlineRacers
                );

                Debug.Log(
                    $"[RaceManager] Minimap bound to online racers | " +
                    $"Racers={onlineRacers.Length} | " +
                    $"Player={localPlayer.RacerName}",
                    minimap
                );
            }


            // =====================================================
            // 3. CHECKPOINTS
            // =====================================================

            if (checkpointManager == null)
            {
                Debug.LogError(
                    "[RaceManager] CheckpointManager NULL.",
                    this
                );

                AbortOnlineRaceStart(
                    "CheckpointManager was not available."
                );

                yield break;
            }


            Checkpoint[] checkpoints =
                new Checkpoint[
                    checkpointManager.CheckpointCount
                ];


            for (int i = 0;
                 i < checkpoints.Length;
                 i++)
            {
                checkpoints[i] =
                    checkpointManager.GetCheckpoint(i);
            }


            // =====================================================
            // 4. RACE MANAGER
            // =====================================================

            ConfigureRacers(
                checkpointManager,
                onlineRacers,
                positionCalculator,
                totalLaps
            );


            // =====================================================
            // 5. CHECKPOINT MANAGER
            // =====================================================

            checkpointManager.ConfigureRacers(
                this,
                checkpoints,
                onlineRacers
            );


            // =====================================================
            // 6. POSITION
            // =====================================================

            if (positionCalculator != null)
            {
                positionCalculator.Configure(
                    checkpointManager,
                    onlineRacers
                );
            }


            // =====================================================
            // 7. INTRO CAMERA
            // =====================================================

            if (raceIntroCamera != null &&
                localPlayer.Body != null)
            {
                raceIntroCamera.SetPlayerCar(
                    localPlayer.Body
                );
            }


            Debug.Log(
                $"[RaceManager] ONLINE READY | " +
                $"Racers={onlineRacers.Length} | " +
                $"Player={localPlayer.RacerName}",
                this
            );


            // =====================================================
            // DEBUG RACERS
            // =====================================================

            for (int i = 0;
                 i < onlineRacers.Length;
                 i++)
            {
                RaceParticipant racer =
                    onlineRacers[i];

                Debug.Log(
                    $"[RaceManager] RACER {i} | " +
                    $"Name={racer.RacerName} | " +
                    $"IsPlayer={racer.IsPlayer}",
                    racer
                );
            }


            yield return null;


            // =====================================================
            // 8. START
            // =====================================================

            Debug.Log(
                "[RaceManager] PREPARE RACE START",
                this
            );


            PrepareRace();


            Debug.Log(
                $"[RaceManager] PREPARE RACE DONE | " +
                $"State={state}",
                this
            );

            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer)
            {
                yield return WaitForOnlineRaceParticipantsReady();

                if (onlineRaceStartBlocked)
                {
                    Debug.LogError(
                        "[RaceManager] ONLINE START BLOCKED. " +
                        "Countdown was not armed because scene/player " +
                        "readiness did not complete.",
                        this
                    );

                    yield break;
                }
            }


            yield return RunRaceStartSequence();


            Debug.Log(
                $"[RaceManager] START SEQUENCE DONE | " +
                $"State={state}",
                this
            );
        }

        private IEnumerator WaitForOnlineRaceParticipantsReady()
        {
            if (!onlineRuntimeRace ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsServer)
            {
                yield break;
            }

            PoseKartOnlineRaceBootstrap bootstrap =
                FindFirstObjectByType<PoseKartOnlineRaceBootstrap>();

            float deadline =
                Time.realtimeSinceStartup +
                onlineRosterTimeoutSeconds;

            bool loggedWaiting = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                NetworkManager network = NetworkManager.Singleton;

                if (network == null || !network.IsListening)
                {
                    onlineRaceStartBlocked = true;
                    Debug.LogError(
                        "[RaceManager] ONLINE START BLOCKED: " +
                        "network stopped before all players were ready.",
                        this
                    );

                    yield break;
                }

                if (bootstrap == null)
                {
                    bootstrap =
                        FindFirstObjectByType<PoseKartOnlineRaceBootstrap>();

                    if (!loggedWaiting)
                    {
                        Debug.Log(
                            "[RaceManager] WAITING: race bootstrap is " +
                            "not available yet.",
                            this
                        );
                        loggedWaiting = true;
                    }

                    yield return null;
                    continue;
                }

                if (bootstrap.NetworkSceneLoadTimedOut)
                {
                    onlineRaceStartBlocked = true;
                    Debug.LogError(
                        "[RaceManager] ONLINE START BLOCKED: " +
                        "NGO scene loading reported timed-out clients.",
                        bootstrap
                    );

                    yield break;
                }

                if (!bootstrap.NetworkSceneLoadCompleted)
                {
                    if (!loggedWaiting)
                    {
                        Debug.Log(
                            "[RaceManager] WAITING: NGO scene load " +
                            "has not completed for every expected client.",
                            this
                        );
                        loggedWaiting = true;
                    }

                    yield return null;
                    continue;
                }

                if (!AreAllOnlineRaceParticipantsReady())
                {
                    if (!loggedWaiting)
                    {
                        Debug.Log(
                            "[RaceManager] WAITING: all Network Player " +
                            "Objects are present, but local race setup is " +
                            "not ready on every expected client.",
                            this
                        );
                        loggedWaiting = true;
                    }

                    yield return null;
                    continue;
                }

                Debug.Log(
                    "[RaceManager] ALL ONLINE PLAYERS READY. " +
                    "Server may arm the shared countdown.",
                    this
                );

                yield break;
            }

            onlineRaceStartBlocked = true;
            AbortOnlineRaceStart(
                "Expected network players did not become ready before timeout."
            );
            Debug.LogError(
                "[RaceManager] ONLINE READINESS TIMEOUT. " +
                "Race remains locked; GO was not scheduled.",
                this
            );
        }

        private bool AreAllOnlineRaceParticipantsReady()
        {
            NetworkManager network = NetworkManager.Singleton;

            if (network == null ||
                !network.IsServer ||
                racers == null ||
                racers.Length < onlineExpectedRacers)
            {
                return false;
            }

            PoseKartOnlineSession session =
                PoseKartOnlineSession.Instance;

            int expectedHumans =
                session != null && session.PlayerCount > 0
                    ? session.PlayerCount
                    : network.ConnectedClientsIds.Count;

            if (network.ConnectedClientsIds.Count < expectedHumans)
                return false;

            for (int i = 0;
                 i < network.ConnectedClientsIds.Count;
                 i++)
            {
                ulong clientId =
                    network.ConnectedClientsIds[i];

                if (!network.ConnectedClients.TryGetValue(
                        clientId,
                        out NetworkClient client) ||
                    client == null ||
                    client.PlayerObject == null)
                {
                    return false;
                }

                PoseKartNetworkPlayerOwner networkPlayer =
                    client.PlayerObject.GetComponent<PoseKartNetworkPlayerOwner>();

                if (networkPlayer == null ||
                    !networkPlayer.IsRaceSetupReady)
                {
                    return false;
                }
            }

            for (int i = 0; i < racers.Length; i++)
            {
                RaceParticipant racer = racers[i];
                if (racer == null || !racer.IsConfigured)
                    return false;

                NetworkObject networkObject =
                    racer.GetComponentInParent<NetworkObject>();

                if (networkObject == null || !networkObject.IsSpawned)
                    return false;
            }

            return true;
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



        private double GetRaceClockTime()
        {
            if (onlineRuntimeRace &&
                networkRaceClock != null &&
                networkRaceClock.IsSpawned)
            {
                return networkRaceClock.ServerNow;
            }

            return Time.timeAsDouble;
        }


        private double GetElapsedRaceTime()
        {
            if (raceStartTime <= 0d)
                return 0d;

            return System.Math.Max(
                0d,
                GetRaceClockTime() -
                raceStartTime
            );
        }

        private void Update()
        {
            if (state == RaceState.Racing)
            {
                raceTimeSeconds =
                    GetElapsedRaceTime();
            }
        }

        public void PublishNetworkRaceProgress()
        {
            if (!onlineRuntimeRace ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsServer ||
                racers == null)
            {
                return;
            }

            for (int i = 0; i < racers.Length; i++)
            {
                RaceParticipant racer = racers[i];
                if (racer == null)
                    continue;

                PoseKartNetworkPlayerOwner playerSync =
                    racer.GetComponent<PoseKartNetworkPlayerOwner>();
                if (playerSync != null)
                {
                    playerSync.PublishRaceProgress(racer);
                    continue;
                }

                PoseKartNetworkAI aiSync =
                    racer.GetComponent<PoseKartNetworkAI>();
                aiSync?.PublishRaceProgress(racer);
            }
        }

        public void ApplyNetworkParticipantProgress(
            RaceParticipant racer,
            PoseKartRaceProgressState progress)
        {
            if (racer == null ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening ||
                NetworkManager.Singleton.IsServer ||
                !progress.finished ||
                !racer.IsPlayer ||
                playerFinished)
            {
                return;
            }

            raceTimeSeconds = progress.finishTime;
            state = RaceState.Finished;
            countdownDisplay = "FINISH";
            checkpointManager?.SetAcceptingCheckpoints(false);
            racer.CarController?.SetControlPermission(false, true);
            CompletePlayerFinish(progress.finishTime, progress.finishOrder);
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

            // Đồng bộ lại bảng xếp hạng với toàn bộ racer hiện tại.
            // Quan trọng khi tăng từ 4 lên 8 xe.
            if (positionCalculator != null && checkpointManager != null && racers != null)
            {
                positionCalculator.Configure(checkpointManager, racers);
            }

            if (HasRacers)
            {
                // Offline dùng Starting Grid cũ.
                // Online đã được OnlineRaceBootstrap đặt vào Spawn_1..8.
                if (!onlineRuntimeRace)
                {
                    ApplyStartingGrid();
                }

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

            PublishNetworkRaceProgress();

        }



        private void ApplyStartingGrid()
        {
            if (!useStartingGrid || racers == null || racers.Length == 0)
                return;

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
                // Ưu tiên vị trí Player làm mốc.
                Transform referenceTransform = null;

                RaceParticipant player = PlayerRacer;

                if (player != null && player.Body != null)
                {
                    referenceTransform = player.Body.transform;
                }
                else
                {
                    // Nếu không tìm được Player thì lấy racer hợp lệ đầu tiên.
                    foreach (RaceParticipant racer in racers)
                    {
                        if (racer != null && racer.Body != null)
                        {
                            referenceTransform = racer.Body.transform;
                            break;
                        }
                    }
                }

                if (referenceTransform == null)
                {
                    Debug.LogWarning(
                        $"{nameof(RaceManager)} has no valid racer transform " +
                        "to use as starting-grid reference.",
                        this);

                    return;
                }

                // Căn grid theo RacingLine nếu có.
                var racingLine =
                    FindFirstObjectByType<PoseKart.AI.RacingLine>();

                if (racingLine != null && racingLine.Count > 0)
                {
                    int wp =
                        racingLine.FindClosestWaypoint(
                            referenceTransform.position);

                    origin = racingLine.GetPosition(wp);

                    Vector3 trackTangent =
                        racingLine.GetTrackTangent(wp);

                    gridRotation =
                        GetTrackAlignedRotation(trackTangent);
                }
                else
                {
                    origin = referenceTransform.position;

                    gridRotation =
                        GetTrackAlignedRotation(
                            referenceTransform.forward);
                }

                fallbackGridPosition = origin;
                fallbackGridRotation = gridRotation;
                cachedFallbackGridReference = true;
            }

            float frontSpacing =
                Mathf.Max(0.5f, frontRowLateralSpacing);

            float normalSpacing =
                Mathf.Max(0.5f, backRowLateralSpacing);

            float separation =
                Mathf.Max(0.5f, rowSeparation);

            Vector3 forward =
                gridRotation * Vector3.forward;

            Vector3 right =
                gridRotation * Vector3.right;

            Vector3 heightOffset =
                Vector3.up * startingHeightOffset;


            // ==================================
            // PLAYER LUÔN Ở SLOT ĐẦU
            // ==================================

            int slot = 0;

            RaceParticipant playerRacer = PlayerRacer;

            if (playerRacer != null)
            {
                PlaceRacerInGrid(
                    playerRacer,
                    slot++,
                    origin,
                    forward,
                    right,
                    gridRotation,
                    heightOffset,
                    frontSpacing,
                    normalSpacing,
                    separation);
            }


            // ==================================
            // SAU ĐÓ XẾP TẤT CẢ AI
            // ==================================

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null)
                    continue;

                // Player đã xếp rồi.
                if (racer == playerRacer)
                    continue;

                PlaceRacerInGrid(
                    racer,
                    slot++,
                    origin,
                    forward,
                    right,
                    gridRotation,
                    heightOffset,
                    frontSpacing,
                    normalSpacing,
                    separation);
            }

            Physics.SyncTransforms();
        }

        private void PlaceRacerInGrid(
    RaceParticipant racer,
    int slot,
    Vector3 origin,
    Vector3 forward,
    Vector3 right,
    Quaternion rotation,
    Vector3 heightOffset,
    float frontSpacing,
    float normalSpacing,
    float rowSeparation)
        {
            if (racer == null)
                return;

            // 2 xe mỗi hàng.
            int row = slot / 2;
            int column = slot % 2;

            float spacing =
                row == 0
                    ? frontSpacing
                    : normalSpacing;

            // Xe trái = -0.5
            // Xe phải = +0.5
            float side =
                column == 0
                    ? -0.5f
                    : 0.5f;

            float lateralOffset =
                side * spacing;

            // Hàng 2, 4... lệch nhẹ sang phải,
            // tạo kiểu starting grid so le.
            if ((row & 1) == 1)
                lateralOffset += backRowStagger;

            Vector3 position =
                origin
                - forward * (row * rowSeparation)
                + right * lateralOffset
                + heightOffset;

            ApplyGridSlot(
                racer,
                position,
                rotation);
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
            // =====================================================
            // ONLINE - DÙNG CÙNG 1 SERVER CLOCK
            // =====================================================

            if (onlineRuntimeRace)
            {
                networkRaceClock =
                    PoseKartNetworkRaceClock.Instance;

                float clockDeadline =
                    Time.realtimeSinceStartup +
                    onlineRosterTimeoutSeconds;

                while (Time.realtimeSinceStartup < clockDeadline &&
                       (networkRaceClock == null ||
                        !networkRaceClock.IsSpawned))
                {
                    networkRaceClock =
                        PoseKartNetworkRaceClock.Instance;

                    yield return null;
                }

                if (networkRaceClock == null ||
                    !networkRaceClock.IsSpawned)
                {
                    state = RaceState.Waiting;
                    countdownDisplay = string.Empty;

                    Debug.LogError(
                        "[RaceManager] ONLINE COUNTDOWN ABORTED: " +
                        "network race clock was not spawned.",
                        this
                    );

                    yield break;
                }


                // Tổng thời gian countdown.
                double countdownDuration =
                    skipCountdown
                        ? 0.25d
                        : countdownFrom *
                          countdownStepSeconds;


                // Chỉ HOST được quyết định timestamp GO.
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsServer)
                {
                    networkRaceClock.ArmRaceStart(
                        countdownDuration + 0.5d
                    );
                }


                // Client đợi Host phát GO timestamp xuống.
                float goDeadline =
                    Time.realtimeSinceStartup +
                    onlineRosterTimeoutSeconds;

                while (Time.realtimeSinceStartup < goDeadline &&
                       !networkRaceClock.HasGoTime &&
                       !networkRaceClock.RaceStartAborted)
                {
                    yield return null;
                }

                if (networkRaceClock.RaceStartAborted)
                {
                    state = RaceState.Waiting;
                    countdownDisplay = string.Empty;

                    Debug.LogWarning(
                        "[RaceManager] ONLINE COUNTDOWN CANCELLED: " +
                        "server did not start the race.",
                        this
                    );

                    yield break;
                }

                if (!networkRaceClock.HasGoTime)
                {
                    state = RaceState.Waiting;
                    countdownDisplay = string.Empty;

                    Debug.LogError(
                        "[RaceManager] ONLINE COUNTDOWN ABORTED: " +
                        "server did not publish a GO timestamp before timeout.",
                        this
                    );

                    yield break;
                }


                double sharedGoTime =
                    networkRaceClock.GoServerTime;


                Debug.Log(
                    $"[RaceManager] NETWORK GO TIME = " +
                    $"{sharedGoTime:0.000}",
                    this
                );


                // =============================================
                // NETWORK COUNTDOWN
                // =============================================

                if (!skipCountdown)
                {
                    state = RaceState.Countdown;

                    int lastCount =
                        -1;


                    while (
                        networkRaceClock.ServerNow <
                        sharedGoTime)
                    {
                        double remaining =
                            sharedGoTime -
                            networkRaceClock.ServerNow;


                        int count =
                            Mathf.CeilToInt(
                                (float)(
                                    remaining /
                                    countdownStepSeconds
                                )
                            );


                        count =
                            Mathf.Clamp(
                                count,
                                1,
                                countdownFrom
                            );


                        if (count != lastCount)
                        {
                            lastCount =
                                count;

                            countdownDisplay =
                                count.ToString();

                            CountdownTickEvent?.Invoke(
                                count,
                                countdownDisplay
                            );

                            PoseKart.Audio.AudioManager
                                .PlayCountdown(
                                    countdownDisplay
                                );
                        }


                        yield return null;
                    }
                }
                else
                {
                    while (
                        networkRaceClock.ServerNow <
                        sharedGoTime)
                    {
                        yield return null;
                    }
                }


                // =============================================
                // SHARED GO
                // =============================================

                state =
                    RaceState.Racing;

                targetGoTime =
                    0f;

                countdownDisplay =
                    "GO!";


                CountdownTickEvent?.Invoke(
                    0,
                    "GO!"
                );


                PoseKart.Audio.AudioManager
                    .PlayCountdown(
                        "GO!"
                    );


                // QUAN TRỌNG:
                // Không lấy time hiện tại.
                // Dùng đúng timestamp Host đã phát.
                raceStartTime =
                    sharedGoTime;


                raceTimeSeconds =
                    GetElapsedRaceTime();


                checkpointManager
                    .SetAcceptingCheckpoints(
                        true
                    );


                if (HasRacers)
                {
                    foreach (
                        RaceParticipant racer
                        in racers)
                    {
                        if (ShouldEnableOnlineControl(racer))
                        {
                            racer.CarController
                                .SetControlPermission(
                                    true
                                );
                        }
                    }
                }
                else if (NetworkManager.Singleton == null ||
                         NetworkManager.Singleton.IsServer)
                {
                    playerController
                        .SetControlPermission(
                            true
                        );
                }


                Debug.Log(
                    $"[RaceManager] " +
                    $"NETWORK GO -> RACING | " +
                    $"Start={raceStartTime:0.000} | " +
                    $"Now={networkRaceClock.ServerNow:0.000}",
                    this
                );


                RaceStartedEvent?.Invoke();


                if (goDisplaySeconds > 0f)
                {
                    yield return new WaitForSeconds(
                        goDisplaySeconds
                    );
                }


                if (state ==
                    RaceState.Racing)
                {
                    countdownDisplay =
                        string.Empty;
                }


                yield break;
            }

            // =====================================================
            // OFFLINE - GIỮ NGUYÊN FLOW CŨ
            // =====================================================

            if (!skipCountdown)
            {
                state =
                    RaceState.Countdown;

                targetGoTime =
                    Time.time +
                    countdownFrom *
                    countdownStepSeconds;


                for (
                    int count = countdownFrom;
                    count > 0;
                    count--)
                {
                    countdownDisplay =
                        count.ToString();

                    CountdownTickEvent?.Invoke(
                        count,
                        countdownDisplay
                    );

                    PoseKart.Audio.AudioManager
                        .PlayCountdown(
                            countdownDisplay
                        );


                    float stepEnd =
                        Time.time +
                        countdownStepSeconds;


                    while (
                        Time.time <
                        stepEnd)
                    {
                        yield return null;
                    }
                }
            }


            state =
                RaceState.Racing;

            targetGoTime =
                0f;

            countdownDisplay =
                "GO!";

            CountdownTickEvent?.Invoke(
                0,
                "GO!"
            );

            PoseKart.Audio.AudioManager
                .PlayCountdown(
                    "GO!"
                );


            raceStartTime =
                Time.timeAsDouble;

            raceTimeSeconds =
                0d;


            checkpointManager
                .SetAcceptingCheckpoints(
                    true
                );


            if (HasRacers)
            {
                foreach (
                    RaceParticipant racer
                    in racers)
                {
                    racer.CarController
                        .SetControlPermission(
                            true
                        );
                }
            }
            else
            {
                playerController
                    .SetControlPermission(
                        true
                    );
            }


            RaceStartedEvent?.Invoke();


            if (goDisplaySeconds > 0f)
            {
                yield return new WaitForSeconds(
                    goDisplaySeconds
                );
            }


            if (state ==
                RaceState.Racing)
            {
                countdownDisplay =
                    string.Empty;
            }
        }

        private bool ShouldEnableOnlineControl(RaceParticipant racer)
        {
            if (!onlineRuntimeRace)
                return true;

            NetworkManager network = NetworkManager.Singleton;

            if (network == null || racer == null)
                return false;

            NetworkObject networkObject =
                racer.GetComponentInParent<NetworkObject>();

            if (networkObject == null || !networkObject.IsSpawned)
                return false;

            if (networkObject.IsPlayerObject)
                return networkObject.IsOwner;

            // AI is server-owned. Remote clients keep their AI controls locked.
            return network.IsServer;
        }

        private void AbortOnlineRaceStart(string reason)
        {
            if (!onlineRuntimeRace ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            onlineRaceStartBlocked = true;

            PoseKartNetworkRaceClock clock =
                PoseKartNetworkRaceClock.Instance;

            if (clock != null && clock.IsSpawned)
            {
                clock.AbortRaceStart(reason);
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



            raceTimeSeconds =
    GetElapsedRaceTime();

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

            double elapsed =
    GetElapsedRaceTime();

            racer.MarkFinished(finishedRacerCount, elapsed);

            Debug.Log(
    $"[RaceManager] FINISH | " +
    $"Racer={racer.RacerName} | " +
    $"Order={finishedRacerCount}/{racers.Length} | " +
    $"Time={elapsed:0.000} | " +
    $"IsLocalPlayer={racer.IsPlayer}",
    racer
);
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

            PublishNetworkRaceProgress();

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

