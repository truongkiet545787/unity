using PoseKart.Race;
using PoseKart.Vehicle;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Multiplayer

{
    [DisallowMultipleComponent]
    public sealed class PoseKartNetworkPlayerOwner : NetworkBehaviour
    {
        [Header("Race")]
        [SerializeField] private RaceParticipant raceParticipant;
        [SerializeField] private LapTracker lapTracker;
        [SerializeField] private CarController carController;
        [Header("Local Owner Only")]
        [SerializeField]
        private Behaviour[] ownerOnlyBehaviours;

        [SerializeField]
        private GameObject[] ownerOnlyObjects;

        [Header("Physics")]
        [SerializeField]
        private Rigidbody body;

        [Header("Local Camera")]
        [Tooltip("Drag the FirstPersonAnchor child from this vehicle prefab here.")]
        [SerializeField] private Transform firstPersonAnchor;
        private readonly NetworkVariable<FixedString64Bytes>
    networkDisplayName =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        public string DisplayName => networkDisplayName.Value.ToString();

        private readonly NetworkVariable<PoseKartRaceProgressState>
            networkRaceProgress =
                new NetworkVariable<PoseKartRaceProgressState>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );

        private readonly NetworkVariable<bool>
            networkRaceSetupReady =
                new NetworkVariable<bool>(
                    false,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Owner
                );

        // Nitro is still simulated by the local physics owner. These two
        // owner-written values only replicate fuel/mode state so remote VFX
        // and audio can present the same boost without taking physics authority.
        private readonly NetworkVariable<float>
            networkNitroFill01 =
                new NetworkVariable<float>(
                    0f,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Owner
                );

        private readonly NetworkVariable<byte>
            networkNitroMode =
                new NetworkVariable<byte>(
                    0,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Owner
                );

        public bool IsRaceSetupReady =>
            networkRaceSetupReady.Value;

        private Coroutine raceSetupReadyRoutine;
        private float nextNitroReplicationTime;
        private float lastPublishedNitroFill = -1f;
        private byte lastPublishedNitroMode = byte.MaxValue;

        private void Awake()
        {
            if (raceParticipant == null)
                raceParticipant = GetComponent<RaceParticipant>();

            if (lapTracker == null)
            {
                lapTracker = GetComponent<LapTracker>();

                if (lapTracker == null)
                    lapTracker = GetComponentInChildren<LapTracker>(true);
            }

            if (carController == null)
                carController = GetComponent<CarController>();
            if (body == null)
                body = GetComponent<Rigidbody>();

            SetLocalControl(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkDisplayName.OnValueChanged +=
                OnNetworkDisplayNameChanged;
            networkRaceProgress.OnValueChanged +=
                OnNetworkRaceProgressChanged;
            networkNitroFill01.OnValueChanged +=
                OnNetworkNitroStateChanged;
            networkNitroMode.OnValueChanged +=
                OnNetworkNitroStateChanged;

            if (IsOwner)
                networkRaceSetupReady.Value = false;


            // Owner tự đưa tên lobby của mình lên NetworkVariable.
            if (IsOwner)
            {
                string actualName =
                    PoseKartOnlineSession.Instance != null
                        ? PoseKartOnlineSession.Instance.LocalPlayerName
                        : $"Player {OwnerClientId + 1}";

                networkDisplayName.Value =
                    new FixedString64Bytes(
                        actualName
                    );
            }


            SetLocalControl(IsOwner);

            ConfigureRaceParticipant();
            ApplyNetworkRaceProgress(networkRaceProgress.Value);
            ApplyNetworkNitroState();


            if (IsOwner)
            {
                raceSetupReadyRoutine = StartCoroutine(
                    ReportRaceSetupReadyWhenConfigured()
                );

                StartCoroutine(
                    WaitForRaceStartThenEnableControl()
                );

                StartCoroutine(
                    BindLocalCameraDelayed()
                );
            }


            Debug.Log(
                $"[NetworkPlayer] " +
                $"OwnerClient={OwnerClientId} | " +
                $"LocalClient={NetworkManager.LocalClientId} | " +
                $"IsOwner={IsOwner} | " +
                $"Name={networkDisplayName.Value}",
                this
            );
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || carController == null)
                return;

            if (Time.unscaledTime < nextNitroReplicationTime &&
                (byte)carController.CurrentNitroMode == lastPublishedNitroMode)
            {
                return;
            }

            float fill01 = carController.NitroFill01;
            byte mode = (byte)carController.CurrentNitroMode;
            bool modeChanged = mode != lastPublishedNitroMode;
            bool fillChanged = Mathf.Abs(fill01 - lastPublishedNitroFill) >= 0.01f;
            if (!modeChanged && !fillChanged)
                return;

            networkNitroFill01.Value = fill01;
            networkNitroMode.Value = mode;
            lastPublishedNitroFill = fill01;
            lastPublishedNitroMode = mode;
            nextNitroReplicationTime = Time.unscaledTime + 0.1f;
        }
        private void ConfigureRaceParticipant()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();

            if (raceParticipant == null)
                raceParticipant = GetComponent<RaceParticipant>();

            if (lapTracker == null)
            {
                lapTracker = GetComponent<LapTracker>();

                if (lapTracker == null)
                    lapTracker = GetComponentInChildren<LapTracker>(true);
            }

            if (carController == null)
                carController = GetComponent<CarController>();

            if (raceParticipant == null ||
                body == null ||
                carController == null ||
                lapTracker == null)
            {
                Debug.LogError(
                    "[NetworkPlayer] RaceParticipant configuration missing.",
                    this
                );

                return;
            }

            string displayName =
    GetNetworkDisplayName();

            raceParticipant.Configure(
                displayName,
                IsOwner,
                body,
                carController,
                lapTracker
            );

            Debug.Log(
                $"[NetworkPlayer] RaceParticipant -> " +
                $"{displayName} | IsPlayer={IsOwner}",
                this
            );
        }

        private IEnumerator ReportRaceSetupReadyWhenConfigured()
        {
            if (!IsOwner)
                yield break;

            while (IsSpawned)
            {
                RaceManager raceManager =
                    FindFirstObjectByType<RaceManager>();

                if (raceManager != null &&
                    raceManager.IsConfigured &&
                    raceManager.State == RaceState.Waiting &&
                    raceManager.Racers != null &&
                    raceParticipant != null)
                {
                    for (int i = 0;
                         i < raceManager.Racers.Length;
                         i++)
                    {
                        if (raceManager.Racers[i] == raceParticipant)
                        {
                            networkRaceSetupReady.Value = true;

                            Debug.Log(
                                "[NetworkPlayer] RACE SETUP READY | " +
                                $"Owner={OwnerClientId}",
                                this
                            );

                            yield break;
                        }
                    }
                }

                yield return null;
            }
        }

        private IEnumerator WaitForRaceStartThenEnableControl()
        {
            CarController car =
                GetComponent<CarController>();

            if (car == null)
            {
                Debug.LogError(
                    "[NetworkPlayer] CarController not found.",
                    this
                );

                yield break;
            }

            // =========================================
            // 1. KHÓA NGAY LẬP TỨC
            // =========================================

            car.SetControlPermission(
                false
            );

            Debug.Log(
                "[NetworkPlayer] CONTROL LOCKED - waiting for GO",
                this
            );


            // =========================================
            // 2. TÌM RACE MANAGER
            // =========================================

            RaceManager raceManager = null;

            while (raceManager == null)
            {
                raceManager =
                    FindFirstObjectByType<RaceManager>();

                yield return null;
            }


            // =========================================
            // 3. GIỮ KHÓA TRONG WAITING + COUNTDOWN
            // =========================================

            while (
                raceManager.State != RaceState.Racing &&
                raceManager.State != RaceState.Finished)
            {
                car.SetControlPermission(
                    false
                );

                yield return null;
            }


            // =========================================
            // 4. GO!
            // =========================================

            if (raceManager.State == RaceState.Racing)
            {
                car.SetControlPermission(
                    true
                );

                Debug.Log(
                    "[NetworkPlayer] GO! CONTROL ENABLED",
                    this
                );
            }
        }
        private IEnumerator BindLocalCameraDelayed()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();

            // Đợi scene chạy Awake/Start xong.
            yield return null;
            yield return null;

            PoseKartCinematicCamera chaseCamera =
                FindFirstObjectByType<PoseKartCinematicCamera>();

            PoseKartRaceIntroCamera introCamera =
                FindFirstObjectByType<PoseKartRaceIntroCamera>();

            PoseKartRaceFinishCamera finishCamera =
                FindFirstObjectByType<PoseKartRaceFinishCamera>();


            // =========================================
            // 1. INTRO CAMERA PHẢI BIẾT LOCAL PLAYER
            // =========================================

            if (introCamera != null)
            {
                introCamera.SetPlayerCar(body);

                Debug.Log(
                    $"[NetworkPlayer] INTRO CAMERA -> {name}",
                    this
                );
            }


            // =========================================
            // 2. CHASE CAMERA FOLLOW LOCAL PLAYER
            // =========================================

            if (chaseCamera != null)
            {
                chaseCamera.SetLocalOwner(IsOwner);
                chaseCamera.SetFirstPersonAnchor(ResolveFirstPersonAnchor());
                chaseCamera.SetTarget(body);

                Debug.Log(
                    $"[NetworkPlayer] CHASE CAMERA -> {name}",
                    this
                );
            }
            else
            {
                Debug.LogError(
                    "[NetworkPlayer] PoseKartCinematicCamera NOT FOUND",
                    this
                );
            }


            // =========================================
            // 3. FINISH CAMERA
            // =========================================

            if (finishCamera != null)
            {
                finishCamera.SetPlayerCar(body);
            }


            // Đợi thêm vài frame rồi set lại chase camera.
            // Phòng trường hợp Intro/RaceManager vừa chạy Start().
            for (int i = 0; i < 5; i++)
            {
                yield return null;

                if (chaseCamera != null)
                    chaseCamera.SetTarget(body);
            }


            Debug.Log(
                $"[NetworkPlayer] LOCAL CAMERA READY | " +
                $"Owner={OwnerClientId} | " +
                $"Local={NetworkManager.LocalClientId} | " +
                $"Body={body.name}",
                this
            );
        }
        private void BindLocalCameraAndHUD()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();

            var carController = GetComponent<PoseKart.Vehicle.CarController>();

            // 1. Bind Main Camera
            PoseKartCinematicCamera cinematicCam = FindFirstObjectByType<PoseKartCinematicCamera>();
            if (cinematicCam != null && body != null)
            {
                cinematicCam.SetLocalOwner(IsOwner);
                cinematicCam.SetFirstPersonAnchor(ResolveFirstPersonAnchor());
                cinematicCam.SetTarget(body);
                cinematicCam.enabled = true;
            }

            // 2. Tat Intro Camera khi Online de tranh giat lag / quay goc vao xe cu
            PoseKartRaceIntroCamera introCam = FindFirstObjectByType<PoseKartRaceIntroCamera>();
            if (introCam != null)
            {
                introCam.StopAllCoroutines();
                introCam.enabled = false;

                if (cinematicCam != null)
                    cinematicCam.SetIntroHandedOff(true);
            }

            // 3. Bind vao Arcade HUD de hien thi KM/H, Nitro
            PoseKart.UI.PoseKartArcadeHUD arcadeHud = FindFirstObjectByType<PoseKart.UI.PoseKartArcadeHUD>();
            if (arcadeHud != null && carController != null)
            {
                // Set thong qua reflection hoac method de HUD nhan xe moi
                arcadeHud.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            }

            // 4. Cho phep lai xe ngay lap tuc
            if (carController != null)
            {
                carController.SetControlPermission(true);
            }
        }

        private Transform ResolveFirstPersonAnchor()
        {
            if (firstPersonAnchor != null)
                return firstPersonAnchor;

            if (body == null)
                return null;

            Transform[] children = body.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child != null && child.name == "FirstPersonAnchor")
                    return child;
            }

            return null;
        }

        public override void OnNetworkDespawn()
        {
            if (raceSetupReadyRoutine != null)
            {
                StopCoroutine(raceSetupReadyRoutine);
                raceSetupReadyRoutine = null;
            }

            networkDisplayName.OnValueChanged -=
                OnNetworkDisplayNameChanged;
            networkRaceProgress.OnValueChanged -=
                OnNetworkRaceProgressChanged;
            networkNitroFill01.OnValueChanged -=
                OnNetworkNitroStateChanged;
            networkNitroMode.OnValueChanged -=
                OnNetworkNitroStateChanged;

            if (carController != null)
                carController.ClearRemoteNitroPresentation();

            if (IsOwner)
            {
                PoseKartCinematicCamera camera =
                    FindFirstObjectByType<PoseKartCinematicCamera>();
                if (camera != null)
                    camera.SetLocalOwner(false);
            }

            SetLocalControl(false);

            base.OnNetworkDespawn();
        }
        private void OnNetworkDisplayNameChanged(
    FixedString64Bytes oldName,
    FixedString64Bytes newName)
        {
            ConfigureRaceParticipant();
        }

        private void OnNetworkRaceProgressChanged(
            PoseKartRaceProgressState oldState,
            PoseKartRaceProgressState newState)
        {
            if (!IsServer)
                ApplyNetworkRaceProgress(newState);
        }

        private void OnNetworkNitroStateChanged(float oldValue, float newValue)
        {
            if (!IsOwner)
                ApplyNetworkNitroState();
        }

        private void OnNetworkNitroStateChanged(byte oldValue, byte newValue)
        {
            if (!IsOwner)
                ApplyNetworkNitroState();
        }

        private void ApplyNetworkNitroState()
        {
            if (IsOwner || carController == null)
                return;

            CarController.NitroMode mode =
                (CarController.NitroMode)networkNitroMode.Value;
            if (!System.Enum.IsDefined(typeof(CarController.NitroMode), mode))
                mode = CarController.NitroMode.None;

            carController.ApplyRemoteNitroPresentation(
                networkNitroFill01.Value,
                mode);
        }

        private void ApplyNetworkRaceProgress(PoseKartRaceProgressState state)
        {
            if (raceParticipant == null)
                raceParticipant = GetComponent<RaceParticipant>();

            if (raceParticipant == null)
                return;

            raceParticipant.ApplyNetworkProgress(state);

            RaceManager raceManager = FindFirstObjectByType<RaceManager>();
            if (raceManager != null)
                raceManager.ApplyNetworkParticipantProgress(raceParticipant, state);
        }

        public void PublishRaceProgress(RaceParticipant racer)
        {
            if (!IsServer || racer == null)
                return;

            PoseKartRaceProgressState next =
                PoseKartRaceProgressState.From(racer);

            if (!networkRaceProgress.Value.Equals(next))
                networkRaceProgress.Value = next;
        }


        private string GetNetworkDisplayName()
        {
            string value =
                networkDisplayName.Value.ToString();

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return $"Player {OwnerClientId + 1}";
        }
        private void SetLocalControl(bool localOwner)
        {
            if (ownerOnlyBehaviours != null)
            {
                foreach (Behaviour behaviour in ownerOnlyBehaviours)
                {
                    if (behaviour != null)
                        behaviour.enabled = localOwner;
                }
            }

            if (ownerOnlyObjects != null)
            {
                foreach (GameObject target in ownerOnlyObjects)
                {
                    if (target != null)
                        target.SetActive(localOwner);
                }
            }
        }
    }
}
