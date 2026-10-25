using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using System.Threading.Tasks;

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

        public event Action SessionChanged;
        public event Action<string> StatusChanged;

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
            Debug.LogWarning(
                "[PoseKartOnline] Removed from session."
            );

            ResetSessionState();

            StatusChanged?.Invoke(
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


        // =====================================================
        // LEAVE
        // =====================================================

        public async Task LeaveRoomAsync()
        {
            if (currentSession == null)
            {
                ResetSessionState();
                return;
            }

            ISession leavingSession =
                currentSession;

            UnbindSessionEvents();

            currentSession = null;

            try
            {
                await leavingSession.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            ResetSessionState();

            StatusChanged?.Invoke(
                "LEFT ROOM"
            );
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
            if (Instance == this)
                Instance = null;

            if (currentSession != null)
                await LeaveRoomAsync();
        }
    }
}
