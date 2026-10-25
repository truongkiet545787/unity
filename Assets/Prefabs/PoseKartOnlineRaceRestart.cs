using PoseKart.Race;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PoseKartOnlineRaceRestart
        : NetworkBehaviour
    {
        [Header("Race")]
        [SerializeField]
        private RaceManager raceManager;

        [SerializeField]
        private string raceSceneName =
            "PoseKart_ReferenceIsland";


        private bool restartRequested;
        private bool loading;


        private void Awake()
        {
            if (raceManager == null)
            {
                raceManager =
                    FindFirstObjectByType<RaceManager>();
            }
        }


        // =====================================================
        // BUTTON ENTRY
        // =====================================================

        public void RequestPlayAgain()
        {
            Time.timeScale = 1f;

            NetworkManager nm =
                NetworkManager.Singleton;


            // Offline -> behavior cũ.
            if (nm == null ||
                !nm.IsListening)
            {
                SceneManager.LoadScene(
                    raceSceneName
                );

                return;
            }


            // Host tự request.
            if (IsServer)
            {
                restartRequested = true;

                Debug.Log(
                    "[OnlineRestart] HOST requested replay.",
                    this
                );

                return;
            }


            // Client gửi request lên Host.
            RequestRestartRpc();
        }


        // =====================================================
        // CLIENT -> HOST
        // =====================================================

        [Rpc(
            SendTo.Server,
            RequireOwnership = false
        )]
        private void RequestRestartRpc(
            RpcParams rpcParams = default)
        {
            restartRequested = true;

            Debug.Log(
                $"[OnlineRestart] Replay requested " +
                $"by Client " +
                $"{rpcParams.Receive.SenderClientId}",
                this
            );
        }


        // =====================================================
        // HOST
        // =====================================================

        private void Update()
        {
            if (!IsServer ||
                !restartRequested ||
                loading)
            {
                return;
            }


            // Không restart khi những xe khác
            // vẫn chưa finish.
            if (!AllRacersFinished())
                return;


            StartNetworkReplay();
        }


        private bool AllRacersFinished()
        {
            if (raceManager == null)
            {
                raceManager =
                    FindFirstObjectByType<RaceManager>();
            }


            if (raceManager == null ||
                raceManager.Racers == null ||
                raceManager.Racers.Length == 0)
            {
                return false;
            }


            RaceParticipant[] racers =
                raceManager.Racers;


            for (int i = 0;
                 i < racers.Length;
                 i++)
            {
                RaceParticipant racer =
                    racers[i];

                if (racer != null &&
                    !racer.Finished)
                {
                    return false;
                }
            }


            return true;
        }


        private void StartNetworkReplay()
        {
            NetworkManager nm =
                NetworkManager.Singleton;


            if (nm == null ||
                !nm.IsServer ||
                nm.SceneManager == null)
            {
                return;
            }


            loading = true;


            SceneEventProgressStatus result =
                nm.SceneManager.LoadScene(
                    raceSceneName,
                    LoadSceneMode.Single
                );


            if (result !=
                SceneEventProgressStatus.Started)
            {
                Debug.LogError(
                    $"[OnlineRestart] " +
                    $"Replay scene load failed: {result}",
                    this
                );

                loading = false;
                restartRequested = false;

                return;
            }


            Debug.Log(
                "[OnlineRestart] " +
                "Reloading race for ALL players.",
                this
            );
        }
    }
}