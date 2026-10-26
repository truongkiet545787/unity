using PoseKart.AI;
using PoseKart.Race;
using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;
namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PoseKartNetworkAI : NetworkBehaviour
    {
        [SerializeField]
        private CarResetController resetController;

        [SerializeField] private Rigidbody body;
        [Header("AI Components")]
        [SerializeField] private AIRacerBrain aiBrain;
        [SerializeField] private AIInputProvider aiInput;
        [SerializeField] private CarController carController;
        [SerializeField] private RaceParticipant raceParticipant;

        private readonly NetworkVariable<PoseKartRaceProgressState>
            networkRaceProgress =
                new NetworkVariable<PoseKartRaceProgressState>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );

        private readonly NetworkVariable<float>
            networkNitroFill01 =
                new NetworkVariable<float>(
                    0f,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );

        private readonly NetworkVariable<byte>
            networkNitroMode =
                new NetworkVariable<byte>(
                    0,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );

        private float nextNitroReplicationTime;
        private float lastPublishedNitroFill = -1f;
        private byte lastPublishedNitroMode = byte.MaxValue;

        private void Awake()
        {
            if (resetController == null)
            {
                resetController =
                    GetComponent<CarResetController>();
            }
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (aiBrain == null)
                aiBrain = GetComponent<AIRacerBrain>();

            if (aiInput == null)
                aiInput = GetComponent<AIInputProvider>();

            if (carController == null)
                carController = GetComponent<CarController>();
            if (raceParticipant == null)
                raceParticipant = GetComponent<RaceParticipant>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkRaceProgress.OnValueChanged +=
                OnNetworkRaceProgressChanged;
            networkNitroFill01.OnValueChanged +=
                OnNetworkNitroStateChanged;
            networkNitroMode.OnValueChanged +=
                OnNetworkNitroStateChanged;
            ApplyNetworkRaceProgress(networkRaceProgress.Value);
            ApplyNetworkNitroState();

            bool runAI = IsServer;
            if (resetController != null)
                resetController.enabled = runAI;
            if (body == null)
                body = GetComponent<Rigidbody>();

            if (aiBrain == null)
                aiBrain = GetComponent<AIRacerBrain>();

            if (aiInput == null)
                aiInput = GetComponent<AIInputProvider>();

            if (carController == null)
                carController = GetComponent<CarController>();

            if (resetController != null)
                resetController.enabled = runAI;
            // HOST chạy physics + AI.
            // CLIENT chỉ nhận transform từ Host.
            if (body != null)
                body.isKinematic = !runAI;


            if (aiBrain != null)
                aiBrain.enabled = runAI;

            if (aiInput != null)
                aiInput.enabled = runAI;

            if (carController != null)
                carController.enabled = runAI;


            if (runAI && aiBrain != null)
            {
                RacingLine line =
                    FindFirstObjectByType<RacingLine>();

                aiBrain.BindNetworkRuntime(line);
            }


            Debug.Log(
                $"[NetworkAI] {name} | " +
                $"IsServer={IsServer} | " +
                $"AI Active={runAI}",
                this
            );
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || carController == null)
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

        public override void OnNetworkDespawn()
        {
            networkRaceProgress.OnValueChanged -=
                OnNetworkRaceProgressChanged;
            networkNitroFill01.OnValueChanged -=
                OnNetworkNitroStateChanged;
            networkNitroMode.OnValueChanged -=
                OnNetworkNitroStateChanged;
            if (carController != null)
                carController.ClearRemoteNitroPresentation();
            base.OnNetworkDespawn();
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
            if (!IsServer)
                ApplyNetworkNitroState();
        }

        private void OnNetworkNitroStateChanged(byte oldValue, byte newValue)
        {
            if (!IsServer)
                ApplyNetworkNitroState();
        }

        private void ApplyNetworkNitroState()
        {
            if (IsServer || carController == null)
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

    }
}
