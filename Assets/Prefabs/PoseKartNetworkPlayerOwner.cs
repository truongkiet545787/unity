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
        private readonly NetworkVariable<FixedString64Bytes>
    networkDisplayName =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );
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


            if (IsOwner)
            {
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
                cinematicCam.SetTarget(body);
                cinematicCam.enabled = true;
            }

            // 2. Tat Intro Camera khi Online de tranh giat lag / quay goc vao xe cu
            PoseKartRaceIntroCamera introCam = FindFirstObjectByType<PoseKartRaceIntroCamera>();
            if (introCam != null)
            {
                introCam.StopAllCoroutines();
                introCam.enabled = false;
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

        public override void OnNetworkDespawn()
        {
            networkDisplayName.OnValueChanged -=
                OnNetworkDisplayNameChanged;

            SetLocalControl(false);

            base.OnNetworkDespawn();
        }
        private void OnNetworkDisplayNameChanged(
    FixedString64Bytes oldName,
    FixedString64Bytes newName)
        {
            ConfigureRaceParticipant();
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