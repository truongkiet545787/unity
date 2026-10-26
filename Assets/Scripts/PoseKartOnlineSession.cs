using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PoseKartOnlineSession : MonoBehaviour
    {
        private string localPlayerName = "Player";

        public string LocalPlayerName =>
            localPlayerName;
        public static PoseKartOnlineSession Instance { get; private set; }

        private const string PlayerNameKey = "name";
        private const string ReadyKey = "ready";

        [Header("Session")]
        [SerializeField, Min(2)]
        private int maxPlayers = 8;

        [SerializeField]
        private bool privateSession = true;

        [Header("Runtime Debug")]
        [SerializeField]
        private bool initialized;

        [SerializeField]
        private bool inSession;

        [SerializeField]
        private bool isHost;

        [SerializeField]
        private bool localReady;

        [SerializeField]
        private string joinCode;

        [SerializeField]
        private string sessionId;

        [SerializeField]
        private int currentPlayerCount;

        private ISession currentSession;

        private bool localLeaveInProgress;
        private bool localCloseInProgress;
        private bool remoteCloseHandled;
        private bool networkDisconnectSubscribed;

        public event Action SessionChanged;
        public event Action<string> StatusChanged;
        public event Action SessionClosed;

        public bool Initialized => initialized;
        public bool InSession => inSession;
        public bool IsHost => isHost;
        public bool LocalReady => localReady;

        public string JoinCode => joinCode;
        public ISession CurrentSession => currentSession;

        public int PlayerCount =>
            currentSession != null
                ? currentSession.PlayerCount
                : 0;

        public int MaxPlayers =>
            currentSession != null
                ? currentSession.MaxPlayers
                : maxPlayers;

        public int AICount =>
            Mathf.Max(0, 8 - PlayerCount);

        public int ReadyPlayerCount
        {
            get
            {
                if (currentSession == null)
                    return 0;

                int count = 0;

                for (int i = 0;
                     i < currentSession.Players.Count;
                     i++)
                {
                    var player =
                        currentSession.Players[i];

                    if (IsPlayerReady(player))
                        count++;
                }

                return count;
            }
        }

        public bool AllPlayersReady =>
            currentSession != null &&
            PlayerCount > 0 &&
            ReadyPlayerCount == PlayerCount;


        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            DontDestroyOnLoad(gameObject);
            SceneManager.activeSceneChanged += HandleReadyResetSceneChange;
        }
        private async void HandleReadyResetSceneChange(
    Scene previousScene,
    Scene nextScene)
        {
            // Chỉ reset khi từ Race quay lại Lobby.
            if (previousScene.name != "PoseKart_ReferenceIsland" ||
                nextScene.name != "PoseKart_OnlineLobby")
            {
                return;
            }

            // Chỉ Session singleton thật được xử lý.
            if (Instance != this ||
                !inSession ||
                currentSession == null)
            {
                return;
            }

            Debug.Log(
                "[PoseKartOnline] Returning to Lobby -> Reset local Ready"
            );

            // Mỗi Host/Client tự reset Ready của chính mình.
            await SetReadyAsync(false);

            Debug.Log(
                "[PoseKartOnline] Local Ready reset completed."
            );
        }

        private async void Start()
        {
            await InitializeAsync();
        }


        public async Task<bool> InitializeAsync()
        {
            if (initialized)
                return true;

            try
            {
                StatusChanged?.Invoke(
                    "Connecting to Unity Services..."
                );

                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService
                        .Instance
                        .SignInAnonymouslyAsync();
                }

                initialized = true;

                StatusChanged?.Invoke(
                    "ONLINE READY"
                );

                Debug.Log(
                    "[PoseKartOnline] Signed in: " +
                    AuthenticationService.Instance.PlayerId
                );

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                StatusChanged?.Invoke(
                    "ONLINE INITIALIZATION FAILED"
                );

                return false;
            }
        }

        public async Task<bool> StartGameplayNetworkAsync()
        {
            if (currentSession == null)
            {
                StatusChanged?.Invoke(
                    "NO ACTIVE SESSION"
                );

                return false;
            }


            if (!currentSession.IsHost)
            {
                StatusChanged?.Invoke(
                    "ONLY HOST CAN START"
                );

                return false;
            }


            if (!AllPlayersReady)
            {
                StatusChanged?.Invoke(
                    "NOT ALL PLAYERS READY"
                );

                return false;
            }


            return await StartRelayNetworkAsync();
        }

        private async Task<bool> StartRelayNetworkAsync()
        {
            if (currentSession == null || !currentSession.IsHost)
                return false;

            NetworkManager networkManager =
                Unity.Netcode.NetworkManager.Singleton;

            if (networkManager == null)
            {
                StatusChanged?.Invoke(
                    "NETWORK MANAGER MISSING"
                );

                return false;
            }

            if (networkManager.IsListening)
            {
                StatusChanged?.Invoke(
                    "RELAY ALREADY STARTED"
                );

                return true;
            }

            try
            {
                StatusChanged?.Invoke(
                    "STARTING RELAY..."
                );


                RelayNetworkOptions relayOptions =
    new RelayNetworkOptions(
        RelayProtocol.Default,
        null,
        true
    );

                await currentSession
                    .AsHost()
                    .Network
                    .StartRelayNetworkAsync(relayOptions);


                StatusChanged?.Invoke(
                    "RELAY STARTED"
                );


                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);

                StatusChanged?.Invoke(
                    "RELAY START FAILED"
                );

                return false;
            }
        }
        // =====================================================
        // CREATE ROOM
        // =====================================================

        public async Task<bool> CreateRoomAsync(string playerName)
        {
            if (!await InitializeAsync())
                return false;

            try
            {
                await LeaveRoomAsync();

                StatusChanged?.Invoke("CREATING ROOM...");

                RelayNetworkOptions relayOptions =
                    new RelayNetworkOptions(
                        RelayProtocol.Default,
                        null,
                        true
                    );

                SessionOptions options =
                    new SessionOptions
                    {
                        MaxPlayers = maxPlayers,
                        IsPrivate = privateSession
                    }
                    .WithRelayNetwork(relayOptions);

                currentSession =
                    await MultiplayerService.Instance
                        .CreateSessionAsync(options);

                if (currentSession == null)
                {
                    StatusChanged?.Invoke(
                        "CREATE ROOM FAILED"
                    );

                    return false;
                }

                inSession = true;
                isHost = currentSession.IsHost;

                joinCode = currentSession.Code;
                sessionId = currentSession.Id;

                BindSessionEvents();

                localPlayerName =
    string.IsNullOrWhiteSpace(playerName)
        ? "Player"
        : playerName.Trim();

                await SetInitialPlayerDataAsync(
                    localPlayerName
                );

                if (isHost && !await StartRelayNetworkAsync())
                {
                    await LeaveRoomAsync();
                    return false;
                }

                RefreshRuntimeState();

                StatusChanged?.Invoke(
                    $"ROOM CREATED: {joinCode}"
                );

                bool ngoListening =
    Unity.Netcode.NetworkManager.Singleton != null &&
    Unity.Netcode.NetworkManager.Singleton.IsListening;

                Debug.Log(
                    $"[PoseKartOnline] ROOM CREATED\n" +
                    $"Session: {sessionId}\n" +
                    $"Code: {joinCode}\n" +
                    $"Host: {isHost}\n" +
                    $"Players: {PlayerCount}/{MaxPlayers}\n" +
                    $"NGO Listening: {ngoListening}"
                );

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                StatusChanged?.Invoke(
                    "CREATE ROOM FAILED"
                );

                return false;
            }
        }


        // =====================================================
        // JOIN ROOM
        // =====================================================

        public async Task<bool> JoinRoomAsync(
    string code,
    string playerName)
        {
            if (!await InitializeAsync())
                return false;

            if (string.IsNullOrWhiteSpace(code))
            {
                StatusChanged?.Invoke(
                    "ENTER JOIN CODE"
                );

                return false;
            }

            code = code.Trim().ToUpperInvariant();

            try
            {
                // Nếu đang ở session cũ thì rời trước.
                if (currentSession != null)
                {
                    await LeaveRoomAsync();
                }

                StatusChanged?.Invoke(
                    $"JOINING {code}..."
                );

                Debug.Log(
                    $"[PoseKartOnline] Trying to join room: {code}"
                );


                // =====================================================
                // 1. JOIN SESSION
                // =====================================================
                NetworkManager nm =
    NetworkManager.Singleton;

                if (nm == null)
                {
                    Debug.LogError(
                        "[PoseKartOnline] NetworkManager.Singleton is NULL"
                    );

                    StatusChanged?.Invoke(
                        "NETWORK MANAGER MISSING"
                    );

                    return false;
                }

                Debug.Log(
                    $"[PoseKartOnline] BEFORE JOIN | " +
                    $"IsListening={nm.IsListening} | " +
                    $"IsClient={nm.IsClient} | " +
                    $"IsServer={nm.IsServer} | " +
                    $"Shutdown={nm.ShutdownInProgress}"
                );

                // Nếu NetworkManager còn trạng thái từ lần test trước,
                // shutdown sạch trước khi để MPS tự StartClient.
                if (nm.IsListening)
                {
                    Debug.LogWarning(
                        "[PoseKartOnline] NetworkManager still running. Shutting down..."
                    );

                    nm.Shutdown();
                }

                while (nm.ShutdownInProgress ||
                       nm.IsListening)
                {
                    await Task.Yield();
                }

                Debug.Log(
                    "[PoseKartOnline] NetworkManager clean -> joining session."
                );
                currentSession =
                    await MultiplayerService.Instance
                        .JoinSessionByCodeAsync(code);


                if (currentSession == null)
                {
                    Debug.LogError(
                        "[PoseKartOnline] Join returned NULL session."
                    );

                    StatusChanged?.Invoke(
                        "JOIN FAILED"
                    );

                    return false;
                }


                // =====================================================
                // 2. SESSION JOIN THÀNH CÔNG
                // =====================================================

                inSession = true;
                isHost = currentSession.IsHost;

                joinCode = currentSession.Code;
                sessionId = currentSession.Id;


                BindSessionEvents();


                Debug.Log(
                    $"[PoseKartOnline] SESSION JOIN SUCCESS\n" +
                    $"Session: {sessionId}\n" +
                    $"Code: {joinCode}\n" +
                    $"Host: {isHost}\n" +
                    $"Players: {currentSession.PlayerCount}/" +
                    $"{currentSession.MaxPlayers}\n" +
                    $"Network State: {currentSession.Network.State}"
                );


                // =====================================================
                // 3. PLAYER DATA
                // =====================================================
                //
                // QUAN TRỌNG:
                // Nếu set name / ready lỗi thì KHÔNG được coi
                // cả Join Session là thất bại.
                // =====================================================

                try
                {
                    localPlayerName =
    string.IsNullOrWhiteSpace(playerName)
        ? "Player"
        : playerName.Trim();

                    await SetInitialPlayerDataAsync(
                        localPlayerName
                    );
                }
                catch (Exception playerDataException)
                {
                    Debug.LogWarning(
                        "[PoseKartOnline] Joined room successfully, " +
                        "but player data setup failed:\n" +
                        playerDataException
                    );
                }


                // =====================================================
                // 4. REFRESH LOCAL STATE
                // =====================================================

                RefreshRuntimeState();


                StatusChanged?.Invoke(
                    "JOINED ROOM"
                );


                Debug.Log(
                    $"[PoseKartOnline] JOIN COMPLETE\n" +
                    $"Players: {PlayerCount}/{MaxPlayers}\n" +
                    $"Network State: {currentSession.Network.State}"
                );


                return true;
            }
            catch (SessionException sessionException)
            {
                Debug.LogError(
                    $"[PoseKartOnline] JOIN SESSION FAILED\n" +
                    $"Code: {code}\n" +
                    $"Error: {sessionException.Error}\n" +
                    $"{sessionException}"
                );


                ResetSessionState();


                StatusChanged?.Invoke(
                    $"JOIN FAILED: {sessionException.Error}"
                );


                return false;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[PoseKartOnline] JOIN FAILED\n" +
                    $"Code: {code}\n" +
                    $"{exception}"
                );


                ResetSessionState();


                StatusChanged?.Invoke(
                    "JOIN FAILED"
                );


                return false;
            }
        }


        // =====================================================
        // PLAYER DATA
        // =====================================================

        private async Task SetInitialPlayerDataAsync(
            string playerName)
        {
            if (currentSession == null ||
                currentSession.CurrentPlayer == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName =
                    isHost
                        ? "Host"
                        : "Player";
            }

            playerName = playerName.Trim();

            var properties =
                new Dictionary<string, PlayerProperty>
                {
                    {
                        PlayerNameKey,
                        new PlayerProperty(
                            visibility:
                                VisibilityPropertyOptions.Member,
                            value:
                                playerName
                        )
                    },

                    {
                        ReadyKey,
                        new PlayerProperty(
                            visibility:
                                VisibilityPropertyOptions.Member,
                            value:
                                "0"
                        )
                    }
                };


            currentSession.CurrentPlayer
                .SetProperties(properties);

            await currentSession
                .SaveCurrentPlayerDataAsync();

            localReady = false;

            SessionChanged?.Invoke();
        }


        public async Task SetReadyAsync(
            bool ready)
        {
            if (currentSession == null ||
                currentSession.CurrentPlayer == null)
            {
                return;
            }

            try
            {
                currentSession
                    .CurrentPlayer
                    .SetProperty(
                        ReadyKey,
                        new PlayerProperty(
                            visibility:
                                VisibilityPropertyOptions.Member,
                            value:
                                ready ? "1" : "0"
                        )
                    );


                await currentSession
                    .SaveCurrentPlayerDataAsync();


                localReady = ready;

                StatusChanged?.Invoke(
                    ready
                        ? "READY"
                        : "NOT READY"
                );

                RefreshRuntimeState();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                StatusChanged?.Invoke(
                    "READY UPDATE FAILED"
                );
            }
        }


        public async Task ToggleReadyAsync()
        {
            await SetReadyAsync(
                !localReady
            );
        }


        // =====================================================
        // PLAYER LIST
        // =====================================================

        public string BuildPlayerListText()
        {
            if (currentSession == null)
                return "NO SESSION";

            StringBuilder sb =
                new StringBuilder();


            for (int i = 0;
                 i < currentSession.Players.Count;
                 i++)
            {
                var player =
                    currentSession.Players[i];


                string playerName =
                    GetPlayerProperty(
                        player,
                        PlayerNameKey
                    );

                if (string.IsNullOrWhiteSpace(
                    playerName))
                {
                    playerName =
                        $"Player {i + 1}";
                }


                bool ready =
                    IsPlayerReady(player);


                bool host =
                    player.Id ==
                    currentSession.Host;


                sb.Append(i + 1)
                  .Append(". ")
                  .Append(playerName);


                if (host)
                    sb.Append("  [HOST]");


                sb.Append(
                    ready
                        ? "  READY"
                        : "  ..."
                );


                if (i <
                    currentSession.Players.Count - 1)
                {
                    sb.AppendLine();
                }
            }


            return sb.ToString();
        }


        private static string GetPlayerProperty(
            IReadOnlyPlayer player,
            string key)
        {
            if (player == null ||
                player.Properties == null)
            {
                return null;
            }


            if (player.Properties.TryGetValue(
                key,
                out PlayerProperty property))
            {
                return property.Value;
            }


            return null;
        }


        private static bool IsPlayerReady(
            IReadOnlyPlayer player)
        {
            return GetPlayerProperty(
                player,
                ReadyKey
            ) == "1";
        }


        // =====================================================
        // SESSION EVENTS
        // =====================================================

        private void BindSessionEvents()
        {
            if (currentSession == null)
                return;

            remoteCloseHandled = false;
            BindNetworkDisconnectCallback();


            currentSession.PlayerJoined -=
                HandlePlayerJoined;

            currentSession.PlayerJoined +=
                HandlePlayerJoined;


            currentSession.PlayerHasLeft -=
                HandlePlayerLeft;

            currentSession.PlayerHasLeft +=
                HandlePlayerLeft;


            currentSession.PlayerPropertiesChanged -=
                HandlePlayerPropertiesChanged;

            currentSession.PlayerPropertiesChanged +=
                HandlePlayerPropertiesChanged;


            currentSession.Changed -=
                HandleSessionChanged;

            currentSession.Changed +=
                HandleSessionChanged;


            currentSession.SessionHostChanged -=
                HandleHostChanged;

            currentSession.SessionHostChanged +=
                HandleHostChanged;


            currentSession.RemovedFromSession -=
                HandleRemovedFromSession;

            currentSession.RemovedFromSession +=
                HandleRemovedFromSession;

            currentSession.Deleted -=
                HandleSessionDeleted;

            currentSession.Deleted +=
                HandleSessionDeleted;
        }


        private void UnbindSessionEvents()
        {
            if (currentSession == null)
                return;

            currentSession.PlayerJoined -=
                HandlePlayerJoined;

            currentSession.PlayerHasLeft -=
                HandlePlayerLeft;

            currentSession.PlayerPropertiesChanged -=
                HandlePlayerPropertiesChanged;

            currentSession.Changed -=
                HandleSessionChanged;

            currentSession.SessionHostChanged -=
                HandleHostChanged;

            currentSession.RemovedFromSession -=
                HandleRemovedFromSession;

            currentSession.Deleted -=
                HandleSessionDeleted;
        }

        private void BindNetworkDisconnectCallback()
        {
            NetworkManager network =
                NetworkManager.Singleton;

            if (network == null ||
                networkDisconnectSubscribed)
            {
                return;
            }

            network.OnClientDisconnectCallback -=
                HandleNetworkClientDisconnect;

            network.OnClientDisconnectCallback +=
                HandleNetworkClientDisconnect;

            networkDisconnectSubscribed = true;
        }

        private void UnbindNetworkDisconnectCallback()
        {
            if (!networkDisconnectSubscribed)
                return;

            NetworkManager network =
                NetworkManager.Singleton;

            if (network != null)
            {
                network.OnClientDisconnectCallback -=
                    HandleNetworkClientDisconnect;
            }

            networkDisconnectSubscribed = false;
        }

        private void HandleSessionDeleted()
        {
            if (localLeaveInProgress ||
                localCloseInProgress ||
                remoteCloseHandled)
            {
                return;
            }

            remoteCloseHandled = true;
            _ = CleanupRemoteSessionClosureAsync(
                "HOST CLOSED ROOM"
            );
        }

        private void HandleNetworkClientDisconnect(
            ulong clientId)
        {
            NetworkManager network =
                NetworkManager.Singleton;

            if (network == null ||
                network.IsServer ||
                clientId != network.LocalClientId ||
                currentSession == null ||
                localLeaveInProgress ||
                localCloseInProgress ||
                remoteCloseHandled)
            {
                return;
            }

            remoteCloseHandled = true;
            _ = CleanupRemoteSessionClosureAsync(
                "NETWORK DISCONNECTED"
            );
        }

        private async Task CleanupRemoteSessionClosureAsync(
            string reason)
        {
            if (currentSession == null)
                return;

            UnbindSessionEvents();
            currentSession = null;

            await ShutdownNetworkManagerAsync();

            UnbindNetworkDisconnectCallback();
            ResetSessionState();

            StatusChanged?.Invoke(reason);
            SessionClosed?.Invoke();
        }


        private void HandlePlayerJoined(
            string playerId)
        {
            Debug.Log(
                $"[PoseKartOnline] Joined: {playerId}"
            );

            RefreshRuntimeState();
        }


        private void HandlePlayerLeft(
            string playerId)
        {
            Debug.Log(
                $"[PoseKartOnline] Left: {playerId}"
            );

            RefreshRuntimeState();
        }


        private void HandlePlayerPropertiesChanged()
        {
            RefreshRuntimeState();
        }


        private void HandleSessionChanged()
        {
            RefreshRuntimeState();
        }


        private void HandleHostChanged(
            string playerId)
        {
            isHost =
                currentSession != null &&
                currentSession.IsHost;

            StatusChanged?.Invoke(
                isHost
                    ? "YOU ARE NOW HOST"
                    : "HOST CHANGED"
            );

            RefreshRuntimeState();
        }


        private void HandleRemovedFromSession()
        {
            if (localLeaveInProgress ||
                localCloseInProgress ||
                remoteCloseHandled)
            {
                return;
            }

            remoteCloseHandled = true;
            _ = CleanupRemoteSessionClosureAsync(
                "REMOVED FROM ROOM"
            );
        }


        private void RefreshRuntimeState()
        {
            if (currentSession == null)
                return;

            isHost =
                currentSession.IsHost;

            currentPlayerCount =
                currentSession.PlayerCount;

            localReady =
                IsPlayerReady(
                    currentSession.CurrentPlayer
                );

            SessionChanged?.Invoke();
        }

        public void RefreshAfterSceneLoad()
        {
            RefreshRuntimeState();
        }


        // =====================================================
        // LEAVE
        // =====================================================

        public async Task<bool> LeaveRoomAsync()
        {
            localLeaveInProgress = true;

            if (currentSession == null)
            {
                bool networkStopped =
                    await ShutdownClientNetworkAsync();

                UnbindNetworkDisconnectCallback();
                ResetSessionState();
                localLeaveInProgress = false;
                return networkStopped;
            }

            ISession leavingSession =
                currentSession;

            UnbindSessionEvents();

            currentSession = null;

            bool sessionLeft = true;

            try
            {
                await leavingSession.LeaveAsync();
            }
            catch (Exception exception)
            {
                sessionLeft = false;
                Debug.LogException(exception);
            }

            bool networkStoppedAfterLeave =
                await ShutdownClientNetworkAsync();

            UnbindNetworkDisconnectCallback();
            ResetSessionState();

            StatusChanged?.Invoke(
                "LEFT ROOM"
            );

            localLeaveInProgress = false;
            return sessionLeft && networkStoppedAfterLeave;
        }

        public async Task<bool> CloseRoomAsync()
        {
            if (localCloseInProgress)
                return false;

            localCloseInProgress = true;

            if (currentSession == null)
            {
                bool networkStopped =
                    await ShutdownNetworkManagerAsync();

                UnbindNetworkDisconnectCallback();
                ResetSessionState();
                localCloseInProgress = false;
                return networkStopped;
            }

            if (!currentSession.IsHost)
            {
                localCloseInProgress = false;
                return false;
            }

            ISession closingSession =
                currentSession;

            UnbindSessionEvents();
            currentSession = null;

            bool sessionDeleted = true;

            try
            {
                await closingSession
                    .AsHost()
                    .DeleteAsync();
            }
            catch (Exception exception)
            {
                sessionDeleted = false;
                Debug.LogException(exception);
            }

            bool networkStoppedAfterDelete =
                await ShutdownNetworkManagerAsync();

            UnbindNetworkDisconnectCallback();
            ResetSessionState();

            StatusChanged?.Invoke(
                sessionDeleted
                    ? "ROOM CLOSED"
                    : "ROOM CLOSE FAILED"
            );

            SessionClosed?.Invoke();
            localCloseInProgress = false;

            return sessionDeleted &&
                   networkStoppedAfterDelete;
        }

        private async Task<bool> ShutdownNetworkManagerAsync()
        {
            NetworkManager network =
                NetworkManager.Singleton;

            if (network == null)
            {
                return true;
            }

            if (network.IsListening ||
                network.ShutdownInProgress)
            {
                network.Shutdown();
            }

            float deadline =
                Time.realtimeSinceStartup + 5f;

            while ((network.IsListening ||
                    network.ShutdownInProgress) &&
                   Time.realtimeSinceStartup < deadline)
            {
                await Task.Yield();
            }

            bool stopped =
                !network.IsListening &&
                !network.ShutdownInProgress;

            if (!stopped)
            {
                Debug.LogError(
                    "[PoseKartOnline] NGO shutdown timed out.",
                    this
                );
            }

            return stopped;
        }

        private async Task<bool> ShutdownClientNetworkAsync()
        {
            NetworkManager network =
                NetworkManager.Singleton;

            if (network == null ||
                !network.IsClient ||
                network.IsServer)
            {
                return true;
            }

            return await ShutdownNetworkManagerAsync();
        }


        private void ResetSessionState()
        {
            currentSession = null;

            inSession = false;
            isHost = false;
            localReady = false;

            joinCode = "";
            sessionId = "";

            currentPlayerCount = 0;

            SessionChanged?.Invoke();
        }


        private async void OnDestroy()
        {
            SceneManager.activeSceneChanged -= HandleReadyResetSceneChange;
            UnbindNetworkDisconnectCallback();

            if (Instance != this)
                return;

            Instance = null;

            if (currentSession != null)
                await LeaveRoomAsync();
        }
    }
}
