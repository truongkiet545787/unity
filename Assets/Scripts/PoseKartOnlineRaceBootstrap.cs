using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PoseKartOnlineRaceBootstrap : MonoBehaviour
    {
        [Header("Network Player")]
        [SerializeField]
        private GameObject networkPlayerPrefab;
        [Header("Network AI")]
        [SerializeField] private GameObject networkAIPrefab;

        [SerializeField, Min(1)]
        private int totalRacers = 8;

        [Header("Starting Grid")]
        [SerializeField]
        private Transform[] spawnPoints;


        [Header("Legacy Offline Racers")]
        [Tooltip("Player cũ đang đặt sẵn trong scene race.")]
        [SerializeField]
        private GameObject legacyPlayer;

        [Tooltip("7 AI cũ đang đặt sẵn trong scene race.")]
        [SerializeField]
        private GameObject[] legacyAI;


        [Header("Runtime Debug")]
        [SerializeField]
        private bool onlineRace;

        [SerializeField]
        private int connectedHumans;

        [SerializeField]
        private int spawnedHumans;

        private NetworkSceneManager subscribedSceneManager;
        private bool networkSceneLoadCompleted;
        private bool networkSceneLoadTimedOut;

        public bool NetworkSceneLoadCompleted =>
            networkSceneLoadCompleted;

        public bool NetworkSceneLoadTimedOut =>
            networkSceneLoadTimedOut;

        private void Awake()
        {
            SubscribeToNetworkSceneEvents();
        }

        private void SubscribeToNetworkSceneEvents()
        {
            NetworkManager network = NetworkManager.Singleton;

            if (network == null ||
                network.SceneManager == null ||
                subscribedSceneManager != null)
            {
                return;
            }

            subscribedSceneManager = network.SceneManager;
            subscribedSceneManager.OnLoadEventCompleted +=
                HandleNetworkLoadEventCompleted;
        }

        private void HandleNetworkLoadEventCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            NetworkManager network = NetworkManager.Singleton;

            if (network == null ||
                !network.IsServer ||
                sceneName != SceneManager.GetActiveScene().name)
            {
                return;
            }

            networkSceneLoadTimedOut =
                clientsTimedOut != null &&
                clientsTimedOut.Count > 0;

            networkSceneLoadCompleted =
                !networkSceneLoadTimedOut;

            Debug.Log(
                $"[OnlineRaceBootstrap] NGO scene load completed | " +
                $"Scene={sceneName} | Mode={loadSceneMode} | " +
                $"Completed={clientsCompleted?.Count ?? 0} | " +
                $"TimedOut={clientsTimedOut?.Count ?? 0}",
                this
            );
        }

        private void OnDestroy()
        {
            if (subscribedSceneManager != null)
            {
                subscribedSceneManager.OnLoadEventCompleted -=
                    HandleNetworkLoadEventCompleted;
                subscribedSceneManager = null;
            }
        }


        private IEnumerator Start()
        {
            // Đợi scene + NetworkManager ổn định.
            yield return null;

            // Awake normally subscribes before NGO reports completion. Retry
            // once in case the NetworkManager became available one frame later.
            SubscribeToNetworkSceneEvents();

            PoseKartOnlineSession online =
                PoseKartOnlineSession.Instance;

            NetworkManager network =
                NetworkManager.Singleton;


            // =====================================================
            // OFFLINE
            // =====================================================

            if (online == null ||
                !online.InSession ||
                network == null ||
                !network.IsListening)
            {
                onlineRace = false;

                Debug.Log(
                    "[OnlineRaceBootstrap] OFFLINE mode -> " +
                    "giữ Player + AI cũ.",
                    this
                );

                yield break;
            }


            // =====================================================
            // ONLINE
            // =====================================================

            onlineRace = true;

            Debug.Log(
                "[OnlineRaceBootstrap] ONLINE race detected.",
                this
            );


            // Online thì không dùng Player + AI cũ đặt sẵn.
            DisableLegacyRacers();


            // Client tuyệt đối không spawn xe.
            // Chỉ Host/Server spawn NetworkObjects.
            if (!network.IsServer)
            {
                Debug.Log(
                    "[OnlineRaceBootstrap] Client -> " +
                    "waiting for Host to spawn players.",
                    this
                );

                yield break;
            }


            // Đợi ConnectedClients list ổn định thêm chút.
            float deadline =
                Time.realtimeSinceStartup + 5f;


            while (network.ConnectedClientsIds.Count == 0 &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }


            connectedHumans =
                network.ConnectedClientsIds.Count;


            Debug.Log(
                $"[OnlineRaceBootstrap] Connected humans = " +
                $"{connectedHumans}",
                this
            );


            SpawnHumanPlayers();

            SpawnNetworkAI();
        }
        private void SpawnNetworkAI()
        {
            NetworkManager network =
                NetworkManager.Singleton;

            if (network == null ||
                !network.IsServer)
            {
                return;
            }


            if (networkAIPrefab == null)
            {
                Debug.LogError(
                    "[OnlineRaceBootstrap] Network AI Prefab chưa được gán!",
                    this
                );

                return;
            }


            int humanCount =
                network.ConnectedClientsIds.Count;

            int aiCount =
                Mathf.Max(
                    0,
                    totalRacers - humanCount
                );


            Debug.Log(
                $"[OnlineRaceBootstrap] " +
                $"Humans={humanCount} | " +
                $"AI={aiCount} | " +
                $"Total={totalRacers}",
                this
            );


            for (int i = 0; i < aiCount; i++)
            {
                int spawnIndex =
                    humanCount + i;


                if (spawnPoints == null ||
                    spawnIndex >= spawnPoints.Length)
                {
                    Debug.LogError(
                        $"[OnlineRaceBootstrap] " +
                        $"Không có Spawn Point cho AI index {i}.",
                        this
                    );

                    break;
                }


                Transform spawn =
                    spawnPoints[spawnIndex];

                if (spawn == null)
                {
                    Debug.LogError(
                        $"[OnlineRaceBootstrap] " +
                        $"Spawn_{spawnIndex + 1} đang NULL.",
                        this
                    );

                    continue;
                }


                GameObject aiObject =
                    Instantiate(
                        networkAIPrefab,
                        spawn.position,
                        spawn.rotation
                    );


                aiObject.name =
                    $"Network_AI_{i + 1}";


                NetworkObject netObject =
                    aiObject.GetComponent<NetworkObject>();


                if (netObject == null)
                {
                    Debug.LogError(
                        "[OnlineRaceBootstrap] " +
                        "Network AI thiếu NetworkObject!",
                        aiObject
                    );

                    Destroy(aiObject);

                    continue;
                }


                // AI không phải PlayerObject.
                // Server/Host sở hữu AI.
                netObject.Spawn(true);


                Debug.Log(
                    $"[OnlineRaceBootstrap] " +
                    $"Spawned AI {i + 1} " +
                    $"at Spawn_{spawnIndex + 1}",
                    aiObject
                );
            }
        }

        // =========================================================
        // DISABLE OLD LOCAL RACERS
        // =========================================================

        private void DisableLegacyRacers()
        {
            if (legacyPlayer != null)
            {
                legacyPlayer.SetActive(false);
            }


            if (legacyAI == null)
                return;


            for (int i = 0; i < legacyAI.Length; i++)
            {
                if (legacyAI[i] != null)
                {
                    legacyAI[i].SetActive(false);
                }
            }
        }


        // =========================================================
        // SPAWN HUMAN NETWORK PLAYERS
        // =========================================================

        private void SpawnHumanPlayers()
        {
            NetworkManager network =
                NetworkManager.Singleton;


            if (network == null ||
                !network.IsServer)
            {
                return;
            }


            if (networkPlayerPrefab == null)
            {
                Debug.LogError(
                    "[OnlineRaceBootstrap] " +
                    "Network Player Prefab chưa được gán!",
                    this
                );

                return;
            }


            if (spawnPoints == null ||
                spawnPoints.Length == 0)
            {
                Debug.LogError(
                    "[OnlineRaceBootstrap] " +
                    "Chưa có Spawn Points!",
                    this
                );

                return;
            }


            List<ulong> clientIds =
                new List<ulong>(
                    network.ConnectedClientsIds
                );


            // Cho grid ổn định:
            // Host thường clientId 0, rồi client 1, 2...
            clientIds.Sort();


            spawnedHumans = 0;


            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId =
                    clientIds[i];


                // Nếu NGO đã auto spawn PlayerObject thì không spawn lần 2.
                if (network.ConnectedClients.TryGetValue(
                        clientId,
                        out NetworkClient client)
                    &&
                    client.PlayerObject != null)
                {
                    Debug.LogWarning(
                        $"[OnlineRaceBootstrap] Client {clientId} " +
                        $"đã có PlayerObject -> skip.",
                        this
                    );

                    continue;
                }


                int spawnIndex =
                    Mathf.Min(
                        i,
                        spawnPoints.Length - 1
                    );


                Transform spawn =
                    spawnPoints[spawnIndex];


                if (spawn == null)
                {
                    Debug.LogError(
                        $"[OnlineRaceBootstrap] " +
                        $"Spawn_{spawnIndex + 1} đang NULL.",
                        this
                    );

                    continue;
                }


                GameObject playerObject =
                    Instantiate(
                        networkPlayerPrefab,
                        spawn.position,
                        spawn.rotation
                    );


                NetworkObject netObject =
                    playerObject.GetComponent<NetworkObject>();


                if (netObject == null)
                {
                    Debug.LogError(
                        "[OnlineRaceBootstrap] " +
                        "PoseKart_NetworkPlayer thiếu NetworkObject!",
                        playerObject
                    );

                    Destroy(playerObject);

                    continue;
                }


                // Gắn chiếc xe này thành PlayerObject
                // của đúng client.
                netObject.SpawnAsPlayerObject(
                    clientId,
                    true
                );


                spawnedHumans++;


                Debug.Log(
                    $"[OnlineRaceBootstrap] " +
                    $"Spawned Client {clientId} " +
                    $"at Spawn_{spawnIndex + 1}",
                    playerObject
                );
            }


            Debug.Log(
                $"[OnlineRaceBootstrap] " +
                $"Spawn completed: {spawnedHumans} human racers.",
                this
            );
        }
    }
}
