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
        private string lobbySceneName =
            "PoseKart_OnlineLobby";


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
                    SceneManager.GetActiveScene().name
                );

                return;
            }


            // Only the Host may return the whole room to the Lobby.
            if (!IsServer)
            {
                Debug.LogWarning(
                    "[OnlineRestart] Client cannot activate Play Again. " +
                    "Only the Host may return the room to Lobby.",
                    this
                );

                return;
            }

            restartRequested = true;

            Debug.Log(
                "[OnlineRestart] HOST requested return to Lobby.",
                this
            );
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
            Debug.Log(
                $"[OnlineRestart] Ignored client Play Again request " +
                $"from Client " +
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
                    lobbySceneName,
                    LoadSceneMode.Single
                );


            if (result !=
                SceneEventProgressStatus.Started)
            {
                Debug.LogError(
                    $"[OnlineRestart] " +
                    $"Lobby scene load failed: {result}",
                    this
                );

                loading = false;
                restartRequested = false;

                return;
            }


            Debug.Log(
                "[OnlineRestart] " +
                "Returning ALL players to the existing Lobby session.",
                this
            );
        }
    }
}
