using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PoseKartOnlineLobbyStart : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private PoseKartOnlineSession online;

        [SerializeField]
        private Button startRaceButton;

        [SerializeField]
        private TMP_Text startRaceButtonText;

        [SerializeField]
        private TMP_Text statusText;


        [Header("Race")]
        [SerializeField]
        private string raceSceneName = "PoseKart_ReferenceIsland";

        [SerializeField, Min(3f)]
        private float connectionTimeout = 15f;


        private bool startingRace;


        private void Start()
        {
            ResolveReferences();

            if (startRaceButton != null)
            {
                startRaceButton.onClick.RemoveListener(StartRace);
                startRaceButton.onClick.AddListener(StartRace);
            }

            if (online != null)
            {
                online.SessionChanged -= Refresh;
                online.SessionChanged += Refresh;

                online.StatusChanged -= SetStatus;
                online.StatusChanged += SetStatus;
            }

            Refresh();
        }


        private void OnDestroy()
        {
            if (online != null)
            {
                online.SessionChanged -= Refresh;
                online.StatusChanged -= SetStatus;
            }

            if (startRaceButton != null)
            {
                startRaceButton.onClick.RemoveListener(StartRace);
            }
        }


        private void ResolveReferences()
        {
            if (online == null)
            {
                online = PoseKartOnlineSession.Instance;
            }

            if (online == null)
            {
                online =
                    FindFirstObjectByType<PoseKartOnlineSession>();
            }
        }


        // =========================================================
        // UI
        // =========================================================

        private void Refresh()
        {
            if (startRaceButton == null)
                return;


            // Chưa vào room
            if (online == null || !online.InSession)
            {
                startRaceButton.gameObject.SetActive(false);
                return;
            }


            // Chỉ HOST thấy START
            startRaceButton.gameObject.SetActive(
                online.IsHost
            );


            if (!online.IsHost)
                return;


            bool allReady =
                online.AllPlayersReady;


            startRaceButton.interactable =
                allReady && !startingRace;


            if (startRaceButtonText == null)
                return;


            if (startingRace)
            {
                startRaceButtonText.text =
                    "STARTING...";
            }
            else if (!allReady)
            {
                startRaceButtonText.text =
                    $"WAITING " +
                    $"{online.ReadyPlayerCount}/" +
                    $"{online.PlayerCount}";
            }
            else
            {
                startRaceButtonText.text =
                    $"START RACE  " +
                    $"[{online.PlayerCount}P + " +
                    $"{online.AICount} AI]";
            }
        }


        // =========================================================
        // START
        // =========================================================

        private async void StartRace()
        {
            if (startingRace)
                return;

            if (online == null ||
                !online.InSession)
            {
                SetStatus(
                    "NO ACTIVE ROOM"
                );

                return;
            }

            if (!online.IsHost)
            {
                SetStatus(
                    "ONLY HOST CAN START"
                );

                return;
            }

            if (!online.AllPlayersReady)
            {
                SetStatus(
                    "WAITING FOR READY"
                );

                return;
            }

            if (NetworkManager.Singleton == null)
            {
                SetStatus(
                    "NETWORK MANAGER NOT FOUND"
                );

                return;
            }

            startingRace = true;

            Refresh();

            bool networkStarted =
                await online.StartGameplayNetworkAsync();

            if (!networkStarted)
            {
                startingRace = false;
                Refresh();
                return;
            }

            Debug.Log(
                "[PoseKartOnlineLobbyStart] " +
                $"START pressed. " +
                $"Lobby players = {online.PlayerCount}, " +
                $"NGO connected = " +
                $"{NetworkManager.Singleton.ConnectedClientsIds.Count}"
            );

            StartCoroutine(
                WaitForNetworkThenLoadRace()
            );
        }


        // =========================================================
        // WAIT FOR NGO
        // =========================================================

        private IEnumerator WaitForNetworkThenLoadRace()
        {
            SetStatus(
                "CHECKING PLAYERS..."
            );

            float deadline =
                Time.realtimeSinceStartup +
                connectionTimeout;

            // ==========================================
            // 1. ĐỢI NETWORK MANAGER START
            // ==========================================

            while (
                (NetworkManager.Singleton == null ||
                 !NetworkManager.Singleton.IsListening)
                &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (NetworkManager.Singleton == null)
            {
                FailStart(
                    "NETWORK MANAGER NOT FOUND"
                );

                yield break;
            }

            if (!NetworkManager.Singleton.IsListening)
            {
                FailStart(
                    "NETWORK NOT LISTENING"
                );

                yield break;
            }

            // Chỉ Host mới được load network scene.
            if (!NetworkManager.Singleton.IsServer)
            {
                FailStart(
                    "HOST IS NOT SERVER"
                );

                yield break;
            }


            // ==========================================
            // 2. ĐỢI ĐỦ HUMAN CLIENT
            // ==========================================

            int expectedHumans =
                Mathf.Max(
                    1,
                    online.PlayerCount
                );

            while (
                NetworkManager.Singleton
                    .ConnectedClientsIds.Count <
                expectedHumans
                &&
                Time.realtimeSinceStartup < deadline)
            {
                int connected =
                    NetworkManager.Singleton
                        .ConnectedClientsIds.Count;

                SetStatus(
                    $"CONNECTING {connected}/{expectedHumans}"
                );

                yield return null;
            }


            int finalConnected =
                NetworkManager.Singleton
                    .ConnectedClientsIds.Count;


            Debug.Log(
                "[PoseKartOnlineLobbyStart] " +
                $"Lobby players = {expectedHumans}, " +
                $"NGO connected = {finalConnected}"
            );


            if (finalConnected < expectedHumans)
            {
                FailStart(
                    $"CONNECTION TIMEOUT " +
                    $"{finalConnected}/{expectedHumans}"
                );

                yield break;
            }


            // ==========================================
            // 3. LOAD RACE SCENE
            // ==========================================

            SetStatus(
                "LOADING RACE..."
            );


            if (NetworkManager.Singleton.SceneManager == null)
            {
                FailStart(
                    "NETWORK SCENE MANAGER NOT AVAILABLE"
                );

                yield break;
            }


            SceneEventProgressStatus result =
                NetworkManager.Singleton
                    .SceneManager
                    .LoadScene(
                        raceSceneName,
                        LoadSceneMode.Single
                    );


            if (result !=
                SceneEventProgressStatus.Started)
            {
                FailStart(
                    $"SCENE LOAD FAILED: {result}"
                );

                yield break;
            }


            Debug.Log(
                $"[PoseKartOnlineLobbyStart] " +
                $"Loading race scene: {raceSceneName}"
            );
        }


        // =========================================================
        // ERROR
        // =========================================================

        private void FailStart(
            string message)
        {
            Debug.LogError(
                $"[PoseKartOnlineLobbyStart] {message}",
                this
            );

            startingRace = false;

            SetStatus(message);

            Refresh();
        }


        private void SetStatus(
            string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log(
                $"[PoseKartOnline] {message}",
                this
            );
        }
    }
}
