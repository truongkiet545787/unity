using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PoseKartNetworkRaceClock : NetworkBehaviour
    {
        public static PoseKartNetworkRaceClock Instance
        {
            get;
            private set;
        }

        private readonly NetworkVariable<double>
            goServerTime =
                new NetworkVariable<double>(
                    0d,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );

        private readonly NetworkVariable<bool>
            raceStartAborted =
                new NetworkVariable<bool>(
                    false,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server
                );


        public bool HasGoTime =>
            goServerTime.Value > 0d;

        public double GoServerTime =>
            goServerTime.Value;

        public bool RaceStartAborted =>
            raceStartAborted.Value;


        public double ServerNow
        {
            get
            {
                if (NetworkManager != null &&
                    NetworkManager.IsListening)
                {
                    return NetworkManager.ServerTime.Time;
                }

                return Time.timeAsDouble;
            }
        }


        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Instance = this;

            Debug.Log(
                $"[RaceClock] Spawned | " +
                $"Server={IsServer} | " +
                $"Client={IsClient}",
                this
            );
        }


        public override void OnNetworkDespawn()
        {
            if (Instance == this)
                Instance = null;

            base.OnNetworkDespawn();
        }


        public void ArmRaceStart(
            double secondsUntilGo)
        {
            if (!IsServer)
                return;

            if (goServerTime.Value > 0d ||
                raceStartAborted.Value)
                return;

            secondsUntilGo =
                System.Math.Max(
                    0.1d,
                    secondsUntilGo
                );

            goServerTime.Value =
                NetworkManager.ServerTime.Time +
                secondsUntilGo;

            Debug.Log(
                $"[RaceClock] GO scheduled | " +
                $"ServerTime={goServerTime.Value:0.000}",
                this
            );
        }

        public void AbortRaceStart(string reason)
        {
            if (!IsServer ||
                goServerTime.Value > 0d ||
                raceStartAborted.Value)
            {
                return;
            }

            raceStartAborted.Value = true;

            Debug.LogError(
                $"[RaceClock] RACE START ABORTED | {reason}",
                this
            );
        }
    }
}
