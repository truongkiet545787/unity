using System.Collections.Generic;
using PoseKart.Multiplayer;
using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Multiplayer authority bridge cho hệ thống Item của PoseKart.
    ///
    /// ONLINE:
    /// - Server authoritative pickup.
    /// - Human owner gửi request dùng item lên Server.
    /// - Server spawn / xử lý Missile, Banana, Ink, Shield.
    /// - Physics hit của remote human được gửi về đúng Owner Client.
    ///
    /// OFFLINE:
    /// - Component này passive, logic offline cũ vẫn chạy bình thường.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PoseKartNetworkItemBridge : NetworkBehaviour
    {
        private CarController car;
        private KartItemInventory inventory;
        private KartItemDefense defense;
        private RaceParticipant participant;
        private PoseKartRocketStart rocketStart;
        private PoseKartWreckController wreck;
        private PoseKartShockwaveVfx shockwaveVfx;
        private bool useRequestPending;
        private float useRequestPendingSince;
        private uint nextUseRequestId;
        private uint pendingUseRequestId;
        private bool pickupRequestPending;
        private int pickupRequestPendingIndex = -1;
        private float pickupRequestPendingSince;
        private bool nitroPickupRequestPending;
        private int nitroPickupRequestPendingIndex = -1;
        private float nitroPickupRequestPendingSince;
        private float nextShockwavePresentationTime = float.NegativeInfinity;
        private ulong lastShockwavePresentationVictim = ulong.MaxValue;
        private static readonly Dictionary<ulong, float> vehicleCollisionCooldowns =
            new Dictionary<ulong, float>();
        private static ulong nextRaceInteractionEventId;

        private const float RequestRetryDelay = 1.5f;
        private const float ShockwavePresentationContactDistance = 8f;
        private const float VehicleCollisionPresentationDistance = 10f;

        public bool UseRequestPending =>
            useRequestPending &&
            Time.unscaledTime - useRequestPendingSince < RequestRetryDelay;

        // ============================================================
        // SHIELD NETWORK STATE
        // ============================================================

        private readonly NetworkVariable<bool> networkShieldActive =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public bool IsNetworkShieldActive => networkShieldActive.Value;

        // Unlike CarController.CurrentNitroMode on a remote clone, this state
        // is written by the owning human or the Server-owned AI and is safe for
        // authoritative Shockwave immunity checks.
        private readonly NetworkVariable<byte> networkNitroMode =
            new NetworkVariable<byte>(
                (byte)CarController.NitroMode.None,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public bool IsAuthoritativeShockwaveActive =>
            IsOnlineRace && IsSpawned
                ? networkNitroMode.Value == (byte)CarController.NitroMode.Shockwave
                : car != null && car.IsShockwaveActive;

        // ============================================================
        // ONLINE STATE
        // ============================================================

        public static bool IsOnlineRace
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;

                if (nm != null && nm.IsListening)
                    return true;

                PoseKart.Multiplayer.PoseKartOnlineSession session =
                    PoseKart.Multiplayer.PoseKartOnlineSession.Instance;

                return session != null && session.InSession;
            }
        }

        /// <summary>
        /// QUAN TRỌNG:
        ///
        /// RaceParticipant.IsPlayer chỉ cho biết đây có phải local player
        /// trên máy hiện tại hay không.
        ///
        /// NetworkObject.IsPlayerObject mới cho biết object này thực sự là
        /// một HUMAN network player.
        ///
        /// Ví dụ:
        /// - Player 2 nhìn từ Host:
        ///   participant.IsPlayer == false
        ///   NetworkObject.IsPlayerObject == true
        ///
        /// Không được dùng participant.IsPlayer để phân biệt Human / AI online.
        /// </summary>
        private bool IsHumanNetworkPlayer()
        {
            return NetworkObject != null &&
                   NetworkObject.IsPlayerObject;
        }

        private bool IsRemoteHumanPlayer()
        {
            return IsHumanNetworkPlayer() && !IsOwner;
        }

        private static string ResolveRacerName(CarController racer)
        {
            if (racer == null)
                return "Unknown";

            RaceParticipant racerParticipant =
                racer.GetComponent<RaceParticipant>() ??
                racer.GetComponentInParent<RaceParticipant>();

            if (racerParticipant != null &&
                !string.IsNullOrWhiteSpace(racerParticipant.RacerName))
            {
                return racerParticipant.RacerName.Trim();
            }

            NetworkObject racerObject =
                racer.GetComponentInParent<NetworkObject>();
            return racerObject != null && racerObject.IsPlayerObject
                ? "Player"
                : "AI";
        }

        private void ServerBroadcastRaceInteraction(
            PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind kind,
            CarController attacker,
            CarController victim)
        {
            if (!IsServer || !IsSpawned)
                return;

            ulong eventId = ++nextRaceInteractionEventId;
            if (eventId == 0)
                eventId = ++nextRaceInteractionEventId;

            SyncRaceInteractionRpc(
                eventId,
                (int)kind,
                ResolveRacerName(attacker),
                ResolveRacerName(victim));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncRaceInteractionRpc(
            ulong eventId,
            int kind,
            string source,
            string target)
        {
            PoseKart.UI.PoseKartRaceInteractionFeed.ReceiveServerEvent(
                eventId,
                kind,
                source,
                target);
        }

        // ============================================================
        // UNITY / NGO LIFECYCLE
        // ============================================================

        private void Awake()
        {
            ResolveGameplayReferences();
        }

        private void ResolveGameplayReferences()
        {
            car = GetComponent<CarController>() ??
                GetComponentInChildren<CarController>(true) ??
                GetComponentInParent<CarController>();
            inventory = GetComponent<KartItemInventory>();
            defense = GetComponent<KartItemDefense>();
            participant = GetComponent<RaceParticipant>();
            rocketStart = GetComponent<PoseKartRocketStart>();
            wreck = GetComponent<PoseKartWreckController>();
            if (wreck == null)
                wreck = gameObject.AddComponent<PoseKartWreckController>();

            shockwaveVfx = GetComponent<PoseKartShockwaveVfx>();
            if (shockwaveVfx == null)
                shockwaveVfx = gameObject.AddComponent<PoseKartShockwaveVfx>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // The network player can finish wiring its visual/vehicle child
            // after this bridge's Awake. Resolve again before owner RPCs can
            // deliver a confirmed Nitro pickup.
            ResolveGameplayReferences();

            networkShieldActive.OnValueChanged +=
                OnShieldActiveStateChanged;
            networkNitroMode.OnValueChanged +=
                OnNetworkNitroModeChanged;
            if (car != null)
            {
                car.NitroModeChanged += OnCarNitroModeChanged;
                if (IsServer)
                    networkNitroMode.Value = (byte)car.CurrentNitroMode;
            }
            ApplyNetworkNitroPresentation(networkNitroMode.Value);

            // Mọi peer phải có cùng deterministic pickup index.
            KartItemPickup.InitializeAllPickups();

            // Client join/spawn sau vẫn phải thấy shield đúng trạng thái.
            if (!IsServer &&
                networkShieldActive.Value &&
                defense != null)
            {
                defense.ActivateShieldVisualOnly();
            }
        }

        // ============================================================
        // ROCKET START AUTHORITY
        // ============================================================

        /// <summary>
        /// Called once by the local human or the Server-owned AI when it
        /// begins holding acceleration during the shared countdown. No tier,
        /// timing or boost values are accepted from the client.
        /// </summary>
        public void RequestRocketStartInput()
        {
            if (rocketStart == null)
                rocketStart = GetComponent<PoseKartRocketStart>();

            if (rocketStart == null || !IsSpawned)
                return;

            if (IsServer)
            {
                AcceptRocketStartInputOnServer();
            }
            else if (IsOwner && IsHumanNetworkPlayer())
            {
                RequestRocketStartInputServerRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestRocketStartInputServerRpc(
            RpcParams rpcParams = default)
        {
            if (!IsServer ||
                !IsSpawned ||
                !IsHumanNetworkPlayer() ||
                rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (rocketStart == null)
                rocketStart = GetComponent<PoseKartRocketStart>();

            AcceptRocketStartInputOnServer();
        }

        private void AcceptRocketStartInputOnServer()
        {
            if (!IsServer || rocketStart == null)
                return;

            if (!rocketStart.RegisterAuthoritativeInput(GetServerClockTime()))
                return;

            rocketStart.SetPreparingVisual(true);
            SyncRocketStartPreparationClientRpc(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncRocketStartPreparationClientRpc(bool active)
        {
            if (rocketStart == null)
                rocketStart = GetComponent<PoseKartRocketStart>();

            rocketStart?.SetPreparingVisual(active);
        }

        public void ServerPublishRocketStartResult(
            RocketStartQuality quality,
            float impulse,
            float duration)
        {
            if (!IsServer || !IsSpawned)
                return;

            SyncRocketStartResultClientRpc(
                (int)quality,
                Mathf.Max(0f, impulse),
                Mathf.Max(0f, duration));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncRocketStartResultClientRpc(
            int qualityValue,
            float impulse,
            float duration)
        {
            if (rocketStart == null)
                rocketStart = GetComponent<PoseKartRocketStart>();

            if (rocketStart == null)
                return;

            RocketStartQuality quality = (RocketStartQuality)Mathf.Clamp(
                qualityValue,
                (int)RocketStartQuality.None,
                (int)RocketStartQuality.NoBoost);

            // Server applies gameplay for AI and the Host owner. A remote
            // human's actual Rigidbody boost is applied only on that owner's
            // client. Other peers receive presentation only.
            bool applyGameplay = IsServer
                ? (!IsHumanNetworkPlayer() || IsOwner)
                : (IsHumanNetworkPlayer() && IsOwner);

            rocketStart.ApplyReplicatedResult(
                quality,
                impulse,
                duration,
                applyGameplay);
        }

        private double GetServerClockTime()
        {
            if (NetworkManager != null && NetworkManager.IsListening)
                return NetworkManager.ServerTime.Time;

            return Time.timeAsDouble;
        }

        public override void OnNetworkDespawn()
        {
            networkShieldActive.OnValueChanged -=
                OnShieldActiveStateChanged;
            networkNitroMode.OnValueChanged -=
                OnNetworkNitroModeChanged;
            if (car != null)
            {
                car.NitroModeChanged -= OnCarNitroModeChanged;
                car.ClearRemoteNitroPresentation();
            }

            base.OnNetworkDespawn();
        }

        private void OnCarNitroModeChanged(CarController.NitroMode mode)
        {
            if (!IsOnlineRace || !IsSpawned)
                return;

            byte modeValue = (byte)mode;
            if (IsServer)
            {
                networkNitroMode.Value = modeValue;
            }
            else if (IsOwner && IsHumanNetworkPlayer())
            {
                ReportNitroModeServerRpc(modeValue);
            }
        }

        private void OnNetworkNitroModeChanged(
            byte previousMode,
            byte newMode)
        {
            ApplyNetworkNitroPresentation(newMode);
        }

        private void ApplyNetworkNitroPresentation(byte modeValue)
        {
            if (car == null)
                car = GetComponent<CarController>();
            if (car == null)
                return;

            bool localGameplayAuthority = IsHumanNetworkPlayer()
                ? IsOwner
                : IsServer;
            if (localGameplayAuthority)
            {
                car.ClearRemoteNitroPresentation();
                return;
            }

            CarController.NitroMode mode = (CarController.NitroMode)Mathf.Clamp(
                modeValue,
                (byte)CarController.NitroMode.None,
                (byte)CarController.NitroMode.Perfect);
            car.ApplyRemoteNitroPresentation(
                mode == CarController.NitroMode.None ? 0f : 1f,
                mode);
        }

        [Rpc(SendTo.Server)]
        private void ReportNitroModeServerRpc(
            byte modeValue,
            RpcParams rpcParams = default)
        {
            if (!IsServer ||
                !IsSpawned ||
                !IsHumanNetworkPlayer() ||
                rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            modeValue = (byte)Mathf.Clamp(
                modeValue,
                (byte)CarController.NitroMode.None,
                (byte)CarController.NitroMode.Perfect);
            networkNitroMode.Value = modeValue;
        }

        private void OnShieldActiveStateChanged(
            bool previousValue,
            bool newValue)
        {
            if (defense == null)
                return;

            // Server điều khiển gameplay shield trực tiếp.
            // Client chỉ sync visual.
            if (!IsServer)
            {
                if (newValue)
                {
                    defense.ActivateShieldVisualOnly();
                }
                else
                {
                    defense.DeactivateShieldVisualOnly();
                }
            }
        }

        // ============================================================
        // PICKUP
        // ============================================================

        /// <summary>
        /// Gọi khi kart chạm Item Box.
        ///
        /// Human:
        /// Local owner gửi request.
        ///
        /// AI:
        /// Server xử lý trực tiếp.
        /// </summary>
        public void HandlePickupTrigger(int pickupIndex)
        {
            if (!IsOnlineRace)
                return;

            if (participant != null && participant.Finished)
                return;

            // Remote clone không được tự request pickup.
            if (!IsOwner && !IsServer)
                return;

            if (IsServer)
            {
                ServerProcessPickup(pickupIndex);
            }
            else
            {
                if (pickupRequestPending &&
                    pickupRequestPendingIndex == pickupIndex &&
                    Time.unscaledTime - pickupRequestPendingSince < RequestRetryDelay)
                {
                    return;
                }

                pickupRequestPending = true;
                pickupRequestPendingIndex = pickupIndex;
                pickupRequestPendingSince = Time.unscaledTime;
                RequestPickupServerRpc(pickupIndex);
            }
        }

        public void HandleNitroPickupTrigger(int pickupIndex)
        {
            if (!IsOnlineRace ||
                (participant != null && participant.Finished) ||
                (!IsOwner && !IsServer))
            {
                return;
            }

            if (IsServer)
            {
                ServerProcessNitroPickup(pickupIndex);
                return;
            }

            if (nitroPickupRequestPending &&
                nitroPickupRequestPendingIndex == pickupIndex &&
                Time.unscaledTime - nitroPickupRequestPendingSince < RequestRetryDelay)
            {
                return;
            }

            nitroPickupRequestPending = true;
            nitroPickupRequestPendingIndex = pickupIndex;
            nitroPickupRequestPendingSince = Time.unscaledTime;
            RequestNitroPickupServerRpc(pickupIndex);
        }

        [Rpc(SendTo.Server)]
        private void RequestPickupServerRpc(
            int pickupIndex,
            RpcParams rpcParams = default)
        {
            // Chỉ owner thật của NetworkObject mới có quyền request.
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[ItemBridge] Reject pickup RPC: " +
                    $"sender={rpcParams.Receive.SenderClientId}, " +
                    $"owner={OwnerClientId}",
                    this
                );

                return;
            }

            ServerProcessPickup(pickupIndex);
        }

        [Rpc(SendTo.Server)]
        private void RequestNitroPickupServerRpc(
            int pickupIndex,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[ItemBridge] Reject Nitro pickup RPC: " +
                    $"sender={rpcParams.Receive.SenderClientId}, " +
                    $"owner={OwnerClientId}",
                    this);
                return;
            }

            ServerProcessNitroPickup(pickupIndex);
        }
        private FlashImage localHitFlash;

        private void PlayLocalHitFlash(
            Color color,
            float duration,
            float maxAlpha)
        {
            // Không flash cho AI hoặc remote clone
            if (!IsHumanNetworkPlayer() || !IsOwner)
                return;

            if (localHitFlash == null)
            {
                localHitFlash =
                    FindFirstObjectByType<FlashImage>(
                        FindObjectsInactive.Include);
            }

            if (localHitFlash == null)
                return;

            localHitFlash.Flash(
                duration,
                0f,
                maxAlpha,
                color);
        }
        private void ServerProcessPickup(int pickupIndex)
        {
            if (!IsServer)
                return;

            IReadOnlyList<KartItemPickup> pickups =
                KartItemPickup.AllPickups;

            if (pickupIndex < 0 ||
                pickupIndex >= pickups.Count)
            {
                return;
            }

            KartItemPickup pickup = pickups[pickupIndex];

            if (pickup == null || !pickup.IsAvailable)
                return;

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (participant == null)
                participant = GetComponent<RaceParticipant>();

            if (participant != null && participant.Finished)
                return;

            // Không nhặt item mới khi đang giữ item.
            if (inventory != null && inventory.HasItem)
                return;

            // Anti-invalid request.
            float distance =
                Vector3.Distance(
                    transform.position,
                    pickup.transform.position
                );

            if (distance > 25f)
            {
                Debug.LogWarning(
                    $"[ItemBridge] Reject pickup {pickupIndex}: " +
                    $"distance={distance:0.0}m",
                    this
                );

                return;
            }

            // Server roll item duy nhất.
            KartItemPickup.ItemType rolledItem =
                pickup.RollAuthoritativeItem();

            if (inventory == null ||
                !inventory.TryCollect(rolledItem))
            {
                return;
            }

            float respawnTime = pickup.RespawnSeconds;

            // Chỉ consume gift sau khi inventory đã nhận item thành công.
            pickup.ServerSetCollected();

            // Remote HUMAN Client cần nhận inventory/HUD.
            // AI không cần ClientRpc này.
            if (IsRemoteHumanPlayer())
            {
                AwardItemClientRpc((int)rolledItem);
            }

            // Đồng bộ Item Box cho toàn bộ peer.
            SyncPickupCollectedRpc(
                pickupIndex,
                respawnTime
            );
        }

        private void ServerProcessNitroPickup(int pickupIndex)
        {
            if (!IsServer)
                return;

            if (car == null)
                ResolveGameplayReferences();

            PoseKartNitroPickup.RefreshAllPickups();
            IReadOnlyList<PoseKartNitroPickup> pickups =
                PoseKartNitroPickup.AllPickups;

            if (pickupIndex < 0 || pickupIndex >= pickups.Count)
                return;

            PoseKartNitroPickup pickup = pickups[pickupIndex];
            if (pickup == null || !pickup.IsAvailable || car == null)
                return;

            if (participant == null)
                participant = GetComponent<RaceParticipant>();
            if (participant != null && participant.Finished)
                return;

            float distance = Vector3.Distance(
                transform.position,
                pickup.transform.position);
            if (distance > 25f)
                return;

            float amount = pickup.NitroAmount;
            bool isRemoteHumanOwner = IsHumanNetworkPlayer() &&
                NetworkManager.Singleton != null &&
                OwnerClientId != NetworkManager.ServerClientId;
            PoseKartNetworkPlayerOwner ownerSync =
                GetComponent<PoseKartNetworkPlayerOwner>() ??
                GetComponentInParent<PoseKartNetworkPlayerOwner>();
            bool hasCapacity = isRemoteHumanOwner
                ? ownerSync != null &&
                  ownerSync.ServerCanAcceptNitroPickup(amount)
                : car.CanAcceptNitroPickup(amount);
            if (!hasCapacity)
                return;

            // Remote human Nitro is still simulated by its owner. The Server
            // confirms the pickup once, then tells that owner to add the charge.
            if (isRemoteHumanOwner)
            {
                AwardNitroPickupClientRpc(amount);
            }
            else if (!car.AddNitroPickup(amount))
            {
                return;
            }

            Debug.Log(
                $"[NITRO PICKUP] SERVER ACCEPT car={car.name} " +
                $"index={pickupIndex} amount={amount:0.00} " +
                $"normal={car.StoredNitro:0.00}",
                this);

            // The authoritative side owns the pickup consumption. The owner
            // client only receives the confirmed charge for remote humans.
            float respawnTime = pickup.RespawnSeconds;
            pickup.ServerSetCollected();
            // The Host is also the Server, so do its local presentation here.
            // Remote Clients receive the same presentation through the RPC.
            pickup.PlayCollectFeedback();
            SyncNitroPickupCollectedRpc(pickupIndex, respawnTime);
        }

        [Rpc(SendTo.Owner)]
        private void AwardItemClientRpc(int itemTypeInt)
        {
            // Host đã update inventory trực tiếp trên Server.
            if (IsServer)
                return;

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (inventory != null)
            {
                inventory.TryCollect(
                    (KartItemPickup.ItemType)itemTypeInt
                );
            }
        }

        [Rpc(SendTo.Owner)]
        private void AwardNitroPickupClientRpc(float amount)
        {
            if (IsServer)
                return;

            if (car == null)
                ResolveGameplayReferences();

            if (car == null)
            {
                Debug.LogWarning(
                    $"[NITRO PICKUP] OWNER REJECT car reference missing " +
                    $"amount={amount:0.00}",
                    this);
                return;
            }

            bool applied = car.AddNitroPickup(amount);
            Debug.Log(
                $"[NITRO PICKUP] OWNER APPLY car={car.name} " +
                $"amount={amount:0.00} applied={applied} " +
                $"normal={car.StoredNitro:0.00}",
                this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncPickupCollectedRpc(
            int pickupIndex,
            float respawnDuration)
        {
            pickupRequestPending = false;
            pickupRequestPendingIndex = -1;

            IReadOnlyList<KartItemPickup> pickups =
                KartItemPickup.AllPickups;

            if (pickupIndex < 0 ||
                pickupIndex >= pickups.Count)
            {
                return;
            }

            KartItemPickup pickup = pickups[pickupIndex];

            if (pickup != null)
            {
                pickup.ClientSyncCollect(respawnDuration);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncNitroPickupCollectedRpc(
            int pickupIndex,
            float respawnDuration)
        {
            // ServerSetCollected() already hides the pickup and plays the
            // Host feedback. Do not play it twice on a Host.
            if (IsServer)
                return;

            nitroPickupRequestPending = false;
            nitroPickupRequestPendingIndex = -1;

            PoseKartNitroPickup.RefreshAllPickups();
            IReadOnlyList<PoseKartNitroPickup> pickups =
                PoseKartNitroPickup.AllPickups;
            if (pickupIndex < 0 || pickupIndex >= pickups.Count)
                return;

            PoseKartNitroPickup pickup = pickups[pickupIndex];
            if (pickup != null)
                pickup.ClientSyncCollect(respawnDuration);
        }

        // ============================================================
        // ITEM USE
        // ============================================================

        /// <summary>
        /// Human owner gọi hàm này khi dùng item online.
        ///
        /// Host:
        /// trực tiếp ServerExecuteUseItem().
        ///
        /// Client:
        /// gửi RPC lên Host.
        /// </summary>
        public bool RequestUseItem()
        {
            if (!IsOnlineRace)
                return false;

            // Chỉ owner của human player được request.
            if (!IsOwner)
                return false;

            if (participant == null)
                participant = GetComponent<RaceParticipant>();

            if (participant != null && participant.Finished)
                return false;

            if (car == null)
            {
                car = GetComponent<CarController>() ??
                    GetComponentInChildren<CarController>(true) ??
                    GetComponentInParent<CarController>();
            }

            // Quyền lái được giữ ở owner. Kiểm tra control trên chính owner
            // trước khi gửi RPC để không cho dùng item trước GO / sau Finish.
            if (car == null || !car.ControlEnabled)
                return false;

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (inventory == null || !inventory.HasItem)
                return false;

            if (useRequestPending &&
                Time.unscaledTime - useRequestPendingSince < RequestRetryDelay)
            {
                return false;
            }

            useRequestPending = true;
            useRequestPendingSince = Time.unscaledTime;
            pendingUseRequestId = ++nextUseRequestId;
            if (pendingUseRequestId == 0)
                pendingUseRequestId = ++nextUseRequestId;

            PoseKart.KartLauncherSkill launcher =
                GetComponent<PoseKart.KartLauncherSkill>() ??
                GetComponentInChildren<PoseKart.KartLauncherSkill>(true);
            Vector3 itemSpawnPosition = default;
            Quaternion itemSpawnRotation = Quaternion.identity;
            bool hasItemSpawnPose = launcher != null &&
                launcher.TryGetActiveNetworkShotPose(
                    out itemSpawnPosition,
                    out itemSpawnRotation);

            if (IsServer)
            {
                ServerExecuteUseItem(
                    requestId: pendingUseRequestId
                );
            }
            else
            {
                RequestUseItemServerRpc(
                    itemSpawnPosition,
                    itemSpawnRotation,
                    hasItemSpawnPose,
                    pendingUseRequestId);
            }

            return true;
        }

        [Rpc(SendTo.Server)]
        private void RequestUseItemServerRpc(
            Vector3 itemSpawnPosition,
            Quaternion itemSpawnRotation,
            bool hasItemSpawnPose,
            uint requestId,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[ItemBridge] Reject use RPC: " +
                    $"sender={rpcParams.Receive.SenderClientId}, " +
                    $"owner={OwnerClientId}",
                    this
                );

                return;
            }

            ServerExecuteUseItem(
                itemSpawnPosition,
                itemSpawnRotation,
                hasItemSpawnPose,
                requestId);
        }

        private void SendItemActivationFailed(
            KartItemPickup.ItemType item,
            uint requestId)
        {
            if (IsRemoteHumanPlayer())
            {
                NotifyItemActivationFailedClientRpc(
                    (int)item,
                    requestId
                );
            }
            else if (IsOwner && inventory != null)
            {
                inventory.NotifyActivationFailedDirect(item);
                useRequestPending = false;
                pendingUseRequestId = 0;
            }
        }

        /// <summary>
        /// SERVER là nơi duy nhất Activate item của human online.
        /// </summary>
        private void ServerExecuteUseItem(
            Vector3 itemSpawnPosition = default,
            Quaternion itemSpawnRotation = default,
            bool hasItemSpawnPose = false,
            uint requestId = 0)
        {
            if (!IsServer)
                return;

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (car == null)
                car = GetComponent<CarController>();

            if (participant == null)
                participant = GetComponent<RaceParticipant>();

            // Remote human dùng owner-authoritative control/physics, vì vậy
            // CarController trên Server proxy luôn có thể bị ControlEnabled=false.
            // Sender của RPC đã được xác thực bằng OwnerClientId ở trên.
            bool remoteHumanServerProxy =
                IsRemoteHumanPlayer();

            if (inventory == null ||
                !inventory.HasItem ||
                car == null ||
                (participant != null && participant.Finished) ||
                (!car.ControlEnabled && !remoteHumanServerProxy))
            {
                if (inventory != null && inventory.HasItem)
                {
                    SendItemActivationFailed(
                        inventory.CurrentItem,
                        requestId
                    );
                }

                return;
            }

            KartItemPickup.ItemType heldItem =
                inventory.CurrentItem;

            bool activated =
                KartItemPickup.Activate(
                    car,
                    heldItem,
                    inventory,
                    itemSpawnPosition,
                    itemSpawnRotation,
                    hasItemSpawnPose
                );
            if (heldItem == KartItemPickup.ItemType.HomingMissile ||
    heldItem == KartItemPickup.ItemType.InkShot)
            {
                Debug.LogWarning(
                    $"[ITEM FIRE TRACE] " +
                    $"kart={name} | " +
                    $"item={heldItem} | " +
                    $"activated={activated} | " +
                    $"server={IsServer} | " +
                    $"time={Time.time:F2}",
                    this
                );
            }

            // Missile / Ink có thể fail nếu Server không tìm thấy target.
            if (!activated)
            {
                SendItemActivationFailed(
                    heldItem,
                    requestId
                );

                return;
            }

            // Missile/Ink gameplay đã được Server tạo ở trên. Chỉ đồng bộ
            // muzzle VFX cosmetic một lần sau activation thành công.
            if (heldItem == KartItemPickup.ItemType.HomingMissile ||
                heldItem == KartItemPickup.ItemType.InkShot)
            {
                ServerSyncLauncherMuzzleVfx(
                    heldItem,
                    itemSpawnPosition,
                    itemSpawnRotation,
                    hasItemSpawnPose);
            }

            // Server inventory là authoritative.
            inventory.ConsumeHeldCharge();

            if (IsOwner)
                useRequestPending = false;

            // Đồng bộ HUD/inventory về remote owner.
            if (IsRemoteHumanPlayer())
            {
                ConfirmUseItemClientRpc(
                    inventory.ItemCharges,
                    inventory.HasItem,
                    (int)heldItem,
                    requestId
                );
            }
        }

        private void ServerSyncLauncherMuzzleVfx(
            KartItemPickup.ItemType itemType,
            Vector3 requestedPosition,
            Quaternion requestedRotation,
            bool hasRequestedPose)
        {
            if (!IsServer)
                return;

            Vector3 position = car != null
                ? car.transform.position + car.transform.forward * 1.6f + Vector3.up * 1.2f
                : transform.position;
            Quaternion rotation = car != null
                ? car.transform.rotation
                : transform.rotation;

            // Đây chính là pose mà owner đã gửi và LaunchMissile/LaunchInk
            // đã chấp nhận, nên Client thấy muzzle cùng vị trí projectile.
            if (hasRequestedPose &&
                IsValidMuzzlePose(requestedPosition, requestedRotation))
            {
                position = requestedPosition;
                rotation = requestedRotation;
            }
            else
            {
                PoseKart.KartLauncherSkill launcher =
                    GetComponent<PoseKart.KartLauncherSkill>() ??
                    GetComponentInChildren<PoseKart.KartLauncherSkill>(true);

                if (launcher != null &&
                    launcher.TryGetActiveNetworkShotPose(
                        out Vector3 localPosition,
                        out Quaternion localRotation))
                {
                    position = localPosition;
                    rotation = localRotation;
                }
            }

            SyncLauncherMuzzleVfxRpc(
                (int)itemType,
                position,
                rotation);
        }

        private static bool IsValidMuzzlePose(
            Vector3 position,
            Quaternion rotation)
        {
            return !float.IsNaN(position.x) &&
                   !float.IsNaN(position.y) &&
                   !float.IsNaN(position.z) &&
                   !float.IsInfinity(position.x) &&
                   !float.IsInfinity(position.y) &&
                   !float.IsInfinity(position.z) &&
                   !float.IsNaN(rotation.x) &&
                   !float.IsNaN(rotation.y) &&
                   !float.IsNaN(rotation.z) &&
                   !float.IsNaN(rotation.w) &&
                   !float.IsInfinity(rotation.x) &&
                   !float.IsInfinity(rotation.y) &&
                   !float.IsInfinity(rotation.z) &&
                   !float.IsInfinity(rotation.w);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncLauncherMuzzleVfxRpc(
            int itemTypeInt,
            Vector3 position,
            Quaternion rotation)
        {
            if (itemTypeInt != (int)KartItemPickup.ItemType.HomingMissile &&
                itemTypeInt != (int)KartItemPickup.ItemType.InkShot)
            {
                return;
            }

            PoseKart.KartLauncherSkill launcher =
                GetComponent<PoseKart.KartLauncherSkill>() ??
                GetComponentInChildren<PoseKart.KartLauncherSkill>(true);

            if (launcher == null)
                return;

            launcher.PlayNetworkMuzzleVfx(
                (KartItemPickup.ItemType)itemTypeInt,
                position,
                rotation);
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmUseItemClientRpc(
            int remainingCharges,
            bool stillHasItem,
            int itemTypeInt,
            uint requestId)
        {
            if (IsServer)
                return;

            if (!useRequestPending ||
                pendingUseRequestId != requestId)
            {
                return;
            }

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (inventory != null)
            {
                inventory.SyncFromNetworkUse(
                    remainingCharges,
                    stillHasItem,
                    (KartItemPickup.ItemType)itemTypeInt
                );
            }

            useRequestPending = false;
            pendingUseRequestId = 0;
        }

        [Rpc(SendTo.Owner)]
        private void NotifyItemActivationFailedClientRpc(
            int itemTypeInt,
            uint requestId)
        {
            if (IsServer)
                return;

            if (!useRequestPending ||
                pendingUseRequestId != requestId)
            {
                return;
            }

            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            if (inventory != null)
            {
                inventory.NotifyActivationFailedDirect(
                    (KartItemPickup.ItemType)itemTypeInt
                );
            }

            useRequestPending = false;
            pendingUseRequestId = 0;
        }

        // ============================================================
        // SHIELD
        // ============================================================

        public void ServerSetShieldActive(bool active)
        {
            if (!IsServer)
                return;

            networkShieldActive.Value = active;
        }

        public void ServerBroadcastShieldExpiredSfx(Vector3 worldPosition)
        {
            if (!IsServer || !IsSpawned)
                return;

            SyncShieldExpiredSfxRpc(worldPosition);
        }

        public void ServerBroadcastShieldContactSfx(Vector3 worldPosition)
        {
            if (!IsServer || !IsSpawned)
                return;

            SyncShieldContactSfxRpc(worldPosition);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncShieldContactSfxRpc(Vector3 worldPosition)
        {
            // Physical Shield collisions are detected by the server-only
            // force field, so remote listeners need an explicit one-shot.
            if (!IsServer)
            {
                PoseKart.Audio.AudioManager.PlayShieldBlock(
                    worldPosition
                );
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncShieldExpiredSfxRpc(Vector3 worldPosition)
        {
            // The server already played the authoritative local sound.
            if (!IsServer)
            {
                PoseKart.Audio.AudioManager.PlayShieldExpire(
                    worldPosition
                );
            }
        }

        /// <summary>
        /// Server authoritative shield block.
        /// </summary>
        public bool ServerTryBlockItem(Vector3 impactPoint)
        {
            if (!IsServer)
                return false;

            if (!networkShieldActive.Value)
                return false;

            if (defense == null)
                defense = GetComponent<KartItemDefense>();

            if (defense == null)
                return false;

            bool blocked =
                defense.TryBlockIncomingItem(impactPoint);

            if (!blocked)
                return false;

            if (defense.BlockedHitsRemaining <= 0 ||
                !defense.IsShieldActive)
            {
                networkShieldActive.Value = false;
            }

            SyncShieldBlockedRpc(impactPoint);

            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncShieldBlockedRpc(
            Vector3 impactPoint)
        {
            // TryBlockIncomingItem already played both sounds on the server.
            // Replicate them only to clients so a Host does not hear doubles.
            if (!IsServer)
            {
                // The Server already received KartItemDefense.OnShieldBlocked.
                // Clients need the same point-accurate visual callback because
                // their visual-only shield never consumes the gameplay hit.
                defense?.PlayShieldImpactVisual(impactPoint);
                GetComponent<PoseKartShieldGeneratorVisual>()?
                    .HandleShieldBlocked(impactPoint);

                KartItemDefense.PlayDeflectPingSoundAt(
                    impactPoint
                );

                PoseKart.Audio.AudioManager.PlayShieldBlock(
                    impactPoint
                );
            }

            Color energyCyan =
                new Color(
                    0.18f,
                    0.92f,
                    1f,
                    1f
                );

            KartParticleEffect.Burst(
                impactPoint,
                energyCyan,
                28
            );

            KartParticleEffect.Burst(
                impactPoint,
                Color.white,
                14
            );

            // BLOCKED UI chỉ trên local HUMAN owner.
            if (IsOwner && IsHumanNetworkPlayer())
            {
                PoseKartShieldBlockedUI.ShowBlocked(
                    "BLOCKED!"
                );
            }
        }

        // ============================================================
        // VEHICLE COLLISION PRESENTATION
        // ============================================================

        /// <summary>
        /// Routes one meaningful vehicle-to-vehicle impact to the Server.
        /// This is presentation-only; CarController keeps the physical
        /// collision response on the existing Rigidbody authority.
        /// </summary>
        public void NotifyVehicleCollision(
            CarController victim,
            Vector3 impactPoint,
            float normalImpactSpeed,
            float intensity)
        {
            if (victim == null || car == null)
                return;

            NetworkObject victimObject =
                victim.GetComponentInParent<NetworkObject>();

            if (!IsOnlineRace)
            {
                PlayVehicleCollisionPresentation(
                    GetComponentInParent<NetworkObject>(),
                    victimObject,
                    impactPoint,
                    normalImpactSpeed,
                    intensity);
                return;
            }

            if (NetworkObject == null ||
                !IsSpawned ||
                victimObject == null ||
                !victimObject.IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ServerProcessVehicleCollision(
                    victimObject,
                    impactPoint,
                    normalImpactSpeed,
                    intensity);
            }
            else if (IsOwner && IsHumanNetworkPlayer())
            {
                RequestVehicleCollisionServerRpc(
                    new NetworkObjectReference(victimObject),
                    impactPoint,
                    normalImpactSpeed,
                    intensity);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestVehicleCollisionServerRpc(
            NetworkObjectReference victimReference,
            Vector3 impactPoint,
            float normalImpactSpeed,
            float intensity,
            RpcParams rpcParams = default)
        {
            if (!IsServer ||
                !IsSpawned ||
                !IsHumanNetworkPlayer() ||
                rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!victimReference.TryGet(out NetworkObject victimObject))
                return;

            ServerProcessVehicleCollision(
                victimObject,
                impactPoint,
                normalImpactSpeed,
                intensity);
        }

        private void ServerProcessVehicleCollision(
            NetworkObject victimObject,
            Vector3 impactPoint,
            float normalImpactSpeed,
            float intensity)
        {
            if (!IsServer ||
                !IsSpawned ||
                victimObject == null ||
                !victimObject.IsSpawned ||
                victimObject == NetworkObject ||
                !IsFinite(normalImpactSpeed) ||
                car == null ||
                normalImpactSpeed < car.CollisionPresentationMinimumSpeed)
            {
                return;
            }

            CarController victim =
                victimObject.GetComponent<CarController>() ??
                victimObject.GetComponentInChildren<CarController>();
            if (victim == null || car == null)
                return;

            Vector3 attackerPosition = car.transform.position;
            Vector3 victimPosition = victim.transform.position;
            if ((victimPosition - attackerPosition).sqrMagnitude >
                VehicleCollisionPresentationDistance *
                VehicleCollisionPresentationDistance)
            {
                return;
            }

            if (!IsFinite(impactPoint) ||
                (impactPoint - attackerPosition).sqrMagnitude > 64f)
            {
                impactPoint = Vector3.Lerp(
                    attackerPosition,
                    victimPosition,
                    0.5f);
            }

            ulong firstId = NetworkObjectId < victimObject.NetworkObjectId
                ? NetworkObjectId
                : victimObject.NetworkObjectId;
            ulong secondId = NetworkObjectId > victimObject.NetworkObjectId
                ? NetworkObjectId
                : victimObject.NetworkObjectId;
            ulong pairKey = (firstId * 1099511628211UL) ^ secondId;
            float now = Time.time;
            if (vehicleCollisionCooldowns.TryGetValue(pairKey, out float nextAllowed) &&
                now < nextAllowed)
            {
                return;
            }

            vehicleCollisionCooldowns[pairKey] = now +
                Mathf.Max(0.05f, car.CollisionPresentationCooldown);
            SyncVehicleCollisionImpactRpc(
                new NetworkObjectReference(NetworkObject),
                new NetworkObjectReference(victimObject),
                impactPoint,
                normalImpactSpeed,
                Mathf.Clamp01(intensity));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncVehicleCollisionImpactRpc(
            NetworkObjectReference attackerReference,
            NetworkObjectReference victimReference,
            Vector3 impactPoint,
            float normalImpactSpeed,
            float intensity)
        {
            if (!attackerReference.TryGet(out NetworkObject attackerObject) ||
                !victimReference.TryGet(out NetworkObject victimObject))
            {
                return;
            }

            KartParticleEffect.Burst(
                impactPoint,
                new Color(1f, 0.55f, 0.16f, 1f),
                Mathf.RoundToInt(Mathf.Lerp(12f, 28f, Mathf.Clamp01(intensity))));

            bool localAttacker =
                attackerObject.IsPlayerObject && attackerObject.IsOwner;
            bool localVictim =
                victimObject.IsPlayerObject && victimObject.IsOwner;
            if (!localAttacker && !localVictim)
                return;

            CarController localCar = localVictim
                ? victimObject.GetComponent<CarController>() ??
                  victimObject.GetComponentInChildren<CarController>()
                : attackerObject.GetComponent<CarController>() ??
                  attackerObject.GetComponentInChildren<CarController>();
            localCar?.GetComponent<PoseKartVehicleAudio>()?
                .PlayNetworkCollisionImpact(normalImpactSpeed);

            PoseKartShockwaveImpactPresentation.PlayLocal(
                Mathf.Lerp(0.15f, 0.65f, Mathf.Clamp01(intensity)),
                impactPoint);
        }

        private static void PlayVehicleCollisionPresentation(
            NetworkObject attackerObject,
            NetworkObject victimObject,
            Vector3 impactPoint,
            float normalImpactSpeed,
            float intensity)
        {
            KartParticleEffect.Burst(
                impactPoint,
                new Color(1f, 0.55f, 0.16f, 1f),
                Mathf.RoundToInt(Mathf.Lerp(12f, 28f, Mathf.Clamp01(intensity))));

            if (victimObject != null && victimObject.IsPlayerObject && victimObject.IsOwner)
            {
                victimObject.GetComponent<PoseKartVehicleAudio>()?
                    .PlayNetworkCollisionImpact(normalImpactSpeed);
            }
            else if (attackerObject != null &&
                     attackerObject.IsPlayerObject && attackerObject.IsOwner)
            {
                attackerObject.GetComponent<PoseKartVehicleAudio>()?
                    .PlayNetworkCollisionImpact(normalImpactSpeed);
            }

            PoseKartShockwaveImpactPresentation.PlayLocal(
                Mathf.Lerp(0.15f, 0.65f, Mathf.Clamp01(intensity)),
                impactPoint);
        }

        // ============================================================
        // SHOCKWAVE IMPACT PRESENTATION
        // ============================================================

        /// <summary>
        /// Called once by the existing Shockwave collision path. Online, the
        /// Server validates the hit, consumes defense and routes the single
        /// gameplay impulse to the victim authority before broadcasting
        /// cosmetic presentation. Offline, this method performs the same
        /// validation locally.
        /// </summary>
        public void NotifyShockwaveContact(
            CarController victim,
            Vector3 impactPoint,
            float relativeSpeed)
        {
            if (victim == null || car == null ||
                (!car.IsShockwaveActive && !IsAuthoritativeShockwaveActive))
                return;

            NetworkObject victimObject =
                victim.GetComponentInParent<NetworkObject>();

            if (!IsOnlineRace)
            {
                if (victim.IsShockwaveActive)
                    return;

                PoseKartWreckController offlineVictimWreck =
                    victim.GetComponent<PoseKartWreckController>() ??
                    victim.GetComponentInChildren<PoseKartWreckController>();
                if (offlineVictimWreck != null &&
                    offlineVictimWreck.IsGameplayProtected)
                {
                    return;
                }

                KartItemDefense offlineDefense =
                    victim.GetComponent<KartItemDefense>();
                if (offlineDefense != null &&
                    offlineDefense.TryBlockMissileExplosion(impactPoint))
                {
                    return;
                }

                Vector3 offlineDelta = BuildShockwaveDelta(
                    victim,
                    impactPoint,
                    relativeSpeed);
                if (offlineDelta.sqrMagnitude < 0.0001f ||
                    victim.TargetBody == null ||
                    victim.TargetBody.isKinematic)
                {
                    return;
                }

                victim.ApplyMissileExplosionImpact(
                    offlineDelta,
                    0.6f,
                    impactPoint,
                    car.ShockwaveTorque);

                // Offline has no network event to replicate, but should still
                // receive the same local presentation.
                PoseKartWreckController offlineWreck =
                    victim.GetComponent<PoseKartWreckController>() ??
                    victim.GetComponentInChildren<PoseKartWreckController>();
                offlineWreck?.TriggerShockwaveDebrisVisual(
                    impactPoint,
                    offlineWreck.ShockwaveDebrisLifetime);
                PoseKartShockwaveImpactPresentation.PlayLocal(0.55f, impactPoint);
                return;
            }

            // An online kart that is already despawned must not create a late
            // local effect or attempt to send an RPC.
            if (NetworkObject == null ||
                !IsSpawned ||
                victimObject == null ||
                !victimObject.IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ServerProcessShockwaveContact(
                    victimObject,
                    impactPoint,
                    relativeSpeed);
            }
            else if (IsOwner && IsHumanNetworkPlayer())
            {
                RequestShockwaveContactServerRpc(
                    new NetworkObjectReference(victimObject),
                    impactPoint,
                    relativeSpeed);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestShockwaveContactServerRpc(
            NetworkObjectReference victimReference,
            Vector3 impactPoint,
            float relativeSpeed,
            RpcParams rpcParams = default)
        {
            if (!IsServer ||
                !IsSpawned ||
                rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!victimReference.TryGet(out NetworkObject victimObject))
                return;

            ServerProcessShockwaveContact(
                victimObject,
                impactPoint,
                relativeSpeed);
        }

        private void ServerProcessShockwaveContact(
            NetworkObject victimObject,
            Vector3 impactPoint,
            float relativeSpeed)
        {
            if (!IsServer ||
                !IsSpawned ||
                victimObject == null ||
                !victimObject.IsSpawned)
            {
                return;
            }

            if (car == null)
                car = GetComponent<CarController>();

            CarController victim =
                victimObject.GetComponent<CarController>() ??
                victimObject.GetComponentInChildren<CarController>();

            if (car == null || victim == null || victim == car)
                return;

            PoseKartNetworkItemBridge victimBridge =
                victimObject.GetComponent<PoseKartNetworkItemBridge>();
            if (!IsAuthoritativeShockwaveActive ||
                (victimBridge != null
                    ? victimBridge.IsAuthoritativeShockwaveActive
                    : victim.IsShockwaveActive))
                return;

            float minimumSpeed = car.ShockwaveMinimumImpactSpeed;
            if (!IsFinite(relativeSpeed) || relativeSpeed < minimumSpeed)
                return;

            Vector3 attackerPosition = car.transform.position;
            Vector3 victimPosition = victim.transform.position;
            if ((victimPosition - attackerPosition).sqrMagnitude >
                ShockwavePresentationContactDistance *
                ShockwavePresentationContactDistance)
            {
                return;
            }

            // Shield consumption is authoritative here, immediately before
            // the gameplay impulse, so a blocked contact cannot be presented
            // as a successful Shockwave hit.
            if (victimBridge != null &&
                victimBridge.ServerTryBlockItem(impactPoint))
                return;

            if (victim.TargetBody == null || victim.TargetBody.isKinematic)
                return;

            PoseKartWreckController victimWreck =
                victim.GetComponent<PoseKartWreckController>() ??
                victim.GetComponentInChildren<PoseKartWreckController>();
            if (victimWreck != null && victimWreck.IsGameplayProtected)
                return;

            ulong victimId = victimObject.NetworkObjectId;
            float now = Time.time;
            if (victimId == lastShockwavePresentationVictim &&
                now < nextShockwavePresentationTime)
            {
                return;
            }

            lastShockwavePresentationVictim = victimId;
            nextShockwavePresentationTime =
                now + Mathf.Max(0.18f, car.ShockwaveHitCooldown);

            if (!IsFinite(impactPoint) ||
                (impactPoint - attackerPosition).sqrMagnitude > 64f)
            {
                impactPoint = Vector3.Lerp(
                    attackerPosition,
                    victimPosition,
                    0.5f);
            }

            ServerBroadcastRaceInteraction(
                PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.ShockwaveHit,
                car,
                victim);

            Vector3 deltaVelocity = BuildShockwaveDelta(
                victim,
                impactPoint,
                relativeSpeed);
            if (deltaVelocity.sqrMagnitude < 0.0001f)
                return;

            // Remote human Rigidbody physics belongs to its owner. AI and the
            // Host player are simulated by this Server copy.
            if (victimObject.IsPlayerObject && !victimObject.IsOwner)
            {
                if (victimBridge == null)
                    return;

                victimBridge.ApplyShockwaveImpactOwnerRpc(
                    deltaVelocity,
                    0.6f,
                    impactPoint,
                    car.ShockwaveTorque);
            }
            else
            {
                victim.ApplyMissileExplosionImpact(
                    deltaVelocity,
                    0.6f,
                    impactPoint,
                    car.ShockwaveTorque);
            }

            float intensity = Mathf.Lerp(
                0.75f,
                1f,
                Mathf.InverseLerp(
                    minimumSpeed,
                    Mathf.Max(minimumSpeed + 0.1f, minimumSpeed * 2.5f),
                    relativeSpeed));

            SyncShockwaveImpactRpc(
                new NetworkObjectReference(NetworkObject),
                new NetworkObjectReference(victimObject),
                impactPoint,
                intensity,
                relativeSpeed);
        }

        private Vector3 BuildShockwaveDelta(
            CarController victim,
            Vector3 impactPoint,
            float relativeSpeed)
        {
            if (victim == null || car == null)
                return Vector3.zero;

            Vector3 source = victim.TargetBody != null
                ? victim.TargetBody.worldCenterOfMass
                : victim.transform.position;
            Vector3 away = Vector3.ProjectOnPlane(
                source - impactPoint,
                Vector3.up);
            if (away.sqrMagnitude < 0.0001f)
            {
                away = Vector3.ProjectOnPlane(
                    victim.transform.position - car.transform.position,
                    Vector3.up);
            }
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.ProjectOnPlane(-car.transform.forward, Vector3.up);
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.right;
            away.Normalize();

            float strength = Mathf.InverseLerp(
                car.ShockwaveMinimumImpactSpeed,
                Mathf.Max(
                    car.ShockwaveMinimumImpactSpeed + 0.1f,
                    car.ShockwaveMinimumImpactSpeed * 2.5f),
                Mathf.Max(0f, relativeSpeed));
            strength = Mathf.Lerp(0.55f, 1f, strength);
            return away * car.ShockwavePush * strength +
                Vector3.up * car.ShockwaveLift * strength;
        }

        [Rpc(SendTo.Owner)]
        private void ApplyShockwaveImpactOwnerRpc(
            Vector3 deltaVelocity,
            float recoveryDuration,
            Vector3 impactPoint,
            float torqueStrength)
        {
            if (car == null)
                car = GetComponent<CarController>();

            if (car == null ||
                car.TargetBody == null ||
                car.TargetBody.isKinematic ||
                IsAuthoritativeShockwaveActive)
            {
                return;
            }

            car.ApplyMissileExplosionImpact(
                deltaVelocity,
                recoveryDuration,
                impactPoint,
                torqueStrength);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncShockwaveImpactRpc(
            NetworkObjectReference attackerReference,
            NetworkObjectReference victimReference,
            Vector3 impactPoint,
            float intensity,
            float relativeSpeed)
        {
            if (!attackerReference.TryGet(out NetworkObject attackerObject) ||
                !victimReference.TryGet(out NetworkObject victimObject))
            {
                return;
            }

            float localRoleMultiplier = 0.18f;
            bool localPlayerInvolved = false;
            if (victimObject.IsPlayerObject && victimObject.IsOwner)
            {
                localRoleMultiplier = 1f;
                localPlayerInvolved = true;
            }
            else if (attackerObject.IsPlayerObject && attackerObject.IsOwner)
            {
                localRoleMultiplier = 0.55f;
                localPlayerInvolved = true;
            }

            PoseKartWreckController victimWreck =
                victimObject.GetComponent<PoseKartWreckController>() ??
                victimObject.GetComponentInChildren<PoseKartWreckController>();
            victimWreck?.TriggerShockwaveDebrisVisual(
                impactPoint,
                victimWreck.ShockwaveDebrisLifetime);

            PoseKartShockwaveVfx victimShockwaveVfx =
                victimObject.GetComponent<PoseKartShockwaveVfx>() ??
                victimObject.GetComponentInChildren<PoseKartShockwaveVfx>();
            victimShockwaveVfx?.PlayImpact(
                impactPoint,
                intensity);

            if (localPlayerInvolved)
            {
                CarController localCar = victimObject.IsPlayerObject && victimObject.IsOwner
                    ? victimObject.GetComponent<CarController>() ??
                      victimObject.GetComponentInChildren<CarController>()
                    : attackerObject.GetComponent<CarController>() ??
                      attackerObject.GetComponentInChildren<CarController>();
                localCar?.GetComponent<PoseKartVehicleAudio>()?
                    .PlayNetworkCollisionImpact(relativeSpeed);

                PoseKartShockwaveImpactPresentation.PlayLocal(
                    Mathf.Clamp01(intensity) * localRoleMultiplier,
                    impactPoint);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        // ============================================================
        // MISSILE HIT
        // ============================================================

        /// <summary>
        /// Sends exactly one presentation event for one authoritative Missile
        /// explosion. This is intentionally owned by a persistent kart bridge,
        /// not by the projectile that is about to be despawned.
        /// </summary>
        public void ServerBroadcastMissileExplosionVfx(
            Vector3 point,
            float lifetime)
        {
            if (!IsServer || !IsSpawned)
                return;

            SyncMissileExplosionVfxRpc(
                point,
                Mathf.Max(0.25f, lifetime)
            );
        }

        /// <summary>
        /// Server gọi khi missile trúng racer.
        ///
        /// Remote Human:
        /// gửi physics impact về Owner Client.
        ///
        /// Host Player / AI:
        /// xử lý trực tiếp trên Server.
        /// </summary>
        public void ServerApplyMissileHit(
            Vector3 point,
            Vector3 deltaVelocity,
            float torqueStrength,
            CarController attacker = null)
        {
            if (!IsServer)
                return;
            if (car == null)
                car = GetComponent<CarController>();

            Rigidbody hitBody = car != null ? car.TargetBody : null;

            Debug.LogWarning(
                $"[MISSILE HIT CHECK] " +
                $"victim={name} | " +
                $"isOwner={IsOwner} | " +
                $"isHuman={NetworkObject.IsPlayerObject} | " +
                $"shield={IsNetworkShieldActive} | " +
                $"hasBody={hitBody != null} | " +
                $"kinematic={(hitBody != null && hitBody.isKinematic)} | " +
                $"shockwave={(car != null && car.IsShockwaveActive)}",
                this
            );
            if (wreck != null && wreck.IsGameplayProtected)
                return;

            if (ServerTryBlockItem(point))
            {
                ServerBroadcastRaceInteraction(
                    PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.MissileBlocked,
                    attacker,
                    car);
                return;
            }

            ServerBroadcastRaceInteraction(
                PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.MissileHit,
                attacker,
                car);

            // Missile death consumes the currently held item on the
            // authoritative inventory. The owner RPC clears its local HUD copy.
            inventory?.ClearHeldItem();

            if (wreck != null)
            {
                float wreckDelay = wreck.RespawnDelay;
                SyncMissileWreckVisualClientRpc(point, wreckDelay);

                if (IsRemoteHumanPlayer())
                {
                    ApplyMissileWreckPhysicsOwnerRpc(point, deltaVelocity, torqueStrength, wreckDelay);
                }
                else
                {
                    wreck.ExecuteOwnerWreckPhysics(point, deltaVelocity, torqueStrength, wreckDelay);
                }
                return;
            }

            // The Server decides whether the hit was valid. The smoke itself is
            // cosmetic, so every peer spawns it on its local victim clone.
            SpawnMissileVictimSmokeClientRpc(1.8f);

            if (IsRemoteHumanPlayer())
            {
                ApplyMissileHitClientRpc(
                    point,
                    deltaVelocity,
                    torqueStrength
                );
            }
            else
            {
                // Host human hoặc Server-owned AI.
                ExecuteLocalMissileImpact(
                    point,
                    deltaVelocity,
                    torqueStrength
                );
            }

        }


        [Rpc(SendTo.ClientsAndHost)]
        private void SyncMissileWreckVisualClientRpc(Vector3 point, float delay)
        {
            if (wreck != null)
            {
                wreck.ExecuteWreckVisualAndDebris(point, delay);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ApplyMissileWreckPhysicsOwnerRpc(
            Vector3 point,
            Vector3 deltaVelocity,
            float torqueStrength,
            float delay)
        {
            if (wreck != null)
            {
                wreck.ExecuteOwnerWreckPhysics(point, deltaVelocity, torqueStrength, delay);
            }
        }

        [Rpc(SendTo.Server)]
        public void OwnerReportWreckRestoredServerRpc()
        {
            if (!IsServer)
                return;

            ServerBroadcastWreckRestored();
        }

        public void ServerBroadcastWreckRestored()
        {
            if (!IsServer)
                return;

            SyncMissileWreckRestoredClientRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncMissileWreckRestoredClientRpc()
        {
            if (wreck != null)
            {
                wreck.RestoreIntactVisualOnly();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpawnMissileVictimSmokeClientRpc(float lifetime)
        {
            MissileVictimSmokeSpawner.SpawnForVictim(
                transform,
                lifetime
            );
        }

        private void ExecuteLocalMissileImpact(
            Vector3 point,
            Vector3 deltaVelocity,
            float torqueStrength)
        {
            if (car == null)
                car = GetComponent<CarController>();

            if (car == null)
                return;

            car.ApplyMissileExplosionImpact(
                deltaVelocity,
                0.7f,
                point,
                torqueStrength
            );
            if (car.TargetBody != null && !car.IsShockwaveActive)
            {
                PlayLocalHitFlash(
                    new Color(1f, 0.08f, 0.02f, 1f),
                    0.16f,
                    0.28f);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ApplyMissileHitClientRpc(
            Vector3 point,
            Vector3 deltaVelocity,
            float torqueStrength)
        {
            ExecuteLocalMissileImpact(
                point,
                deltaVelocity,
                torqueStrength
            );
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncMissileExplosionVfxRpc(
            Vector3 point,
            float lifetime)
        {
            if (inventory == null)
                inventory = GetComponent<KartItemInventory>();

            GameObject explosionPrefab =
                inventory != null
                    ? inventory.MissileImpactExplosionPrefab
                    : null;

            if (explosionPrefab != null)
            {
                GameObject effect =
                    Instantiate(
                        explosionPrefab,
                        point,
                        Quaternion.identity
                    );

                if (effect != null)
                {
                    if (!effect.activeSelf)
                        effect.SetActive(true);

                    foreach (Renderer renderer in
                             effect.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer != null)
                            renderer.enabled = true;
                    }

                    ParticleSystem[] systems =
                        effect.GetComponentsInChildren<ParticleSystem>(true);

                    float cleanup = Mathf.Max(
                        0.25f,
                        lifetime
                    );

                    foreach (ParticleSystem ps in systems)
                    {
                        if (ps == null)
                            continue;

                        if (!ps.gameObject.activeSelf)
                            ps.gameObject.SetActive(true);

                        ps.Play(true);

                        ParticleSystem.MainModule main = ps.main;

                        if (!main.loop)
                        {
                            cleanup = Mathf.Max(
                                cleanup,
                                main.duration +
                                main.startLifetime.constantMax +
                                0.25f
                            );
                        }
                    }

                    Destroy(
                        effect,
                        Mathf.Max(0.25f, cleanup)
                    );
                }
            }
            else
            {
                // Fallback
                KartParticleEffect.Burst(
                    point,
                    new Color(
                        1f,
                        0.35f,
                        0.05f,
                        1f
                    ),
                    52
                );
            }

            // HomingKartMissile already plays the authoritative Host sound.
            // Remote peers play their local copy here, avoiding a Host double
            // playback while keeping the event audible on every Client.
            if (!IsServer)
            {
                PoseKart.Audio.AudioManager.PlayMissileImpact(
                    point
                );
            }

            if (IsOwner && IsHumanNetworkPlayer())
            {
                var impulse =
                    FindFirstObjectByType<
                        Cinemachine.CinemachineImpulseSource
                    >();

                if (impulse != null)
                    impulse.GenerateImpulse(2f);
            }
        }
        // ============================================================
        // BANANA SHOT VISUAL SYNC
        // ============================================================

        /// <summary>
        /// Server gọi mỗi khi Banana Triple Launcher bắn 1 viên.
        /// Gameplay projectile vẫn chỉ tồn tại trên Server.
        /// RPC này chỉ tạo projectile VISUAL + muzzle flash + sound trên Client.
        /// </summary>
        public void ServerSyncBananaShotVisual(
            Vector3 spawnPosition,
            Vector3 velocity)
        {
            if (!IsServer)
                return;

            SyncBananaShotVisualRpc(
                spawnPosition,
                velocity
            );
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncBananaShotVisualRpc(
            Vector3 spawnPosition,
            Vector3 velocity)
        {
            // Host đã tự thấy projectile gameplay + sound + muzzle flash.
            // Không phát thêm lần nữa trên Host.
            if (IsServer)
                return;

            PoseKartBananaTripleLauncher launcher =
                GetComponent<PoseKartBananaTripleLauncher>() ??
                GetComponentInChildren<PoseKartBananaTripleLauncher>();

            if (launcher == null)
                return;

            launcher.PlayNetworkShotVisual(
                spawnPosition,
                velocity
            );
        }
        public void ServerSyncBananaLauncherRetract()
        {
            if (!IsServer)
                return;

            SyncBananaLauncherRetractRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncBananaLauncherRetractRpc()
        {
            // Server tự retract bằng FireTripleSequence().
            if (IsServer)
                return;

            PoseKartBananaTripleLauncher launcher =
                GetComponent<PoseKartBananaTripleLauncher>() ??
                GetComponentInChildren<PoseKartBananaTripleLauncher>();

            if (launcher != null)
            {
                launcher.RetractLauncher();
            }
        }
        // ============================================================
        // BANANA HIT
        // ============================================================

        /// <summary>
        /// Server gọi khi banana phát hiện racer đi qua.
        /// </summary>
        public void ServerApplyBananaHit(
            Vector3 groundPoint,
            Vector3 groundNormal,
            float stunDuration,
            CarController attacker = null)
        {
            if (!IsServer)
                return;

            if (wreck != null && wreck.IsGameplayProtected)
            {
                SyncBananaSlipVfxRpc(groundPoint, groundNormal, false);
                return;
            }

            if (ServerTryBlockItem(groundPoint))
            {
                ServerBroadcastRaceInteraction(
                    PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.BananaBlocked,
                    attacker,
                    car);
                SyncBananaSlipVfxRpc(
                    groundPoint,
                    groundNormal,
                    false
                );

                return;
            }

            ServerBroadcastRaceInteraction(
                PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.BananaHit,
                attacker,
                car);

            // FIX QUAN TRỌNG:
            // Không dùng participant.IsPlayer ở đây.
            //
            // Player 2 trên Host có participant.IsPlayer == false,
            // nhưng NetworkObject.IsPlayerObject == true.
            if (IsRemoteHumanPlayer())
            {
                ApplyBananaHitClientRpc(
                    groundPoint,
                    stunDuration
                );
            }
            else
            {
                // Host local player hoặc AI.
                ExecuteLocalBananaSlip(
                    stunDuration
                );
            }

            SyncBananaSlipVfxRpc(
                groundPoint,
                groundNormal,
                true
            );
        }

        private void ExecuteLocalBananaSlip(
            float stunDuration)
        {
            if (car == null)
                car = GetComponent<CarController>();

            if (car == null)
                return;

            bool stunned =
                car.TryApplyBananaStun(
                    stunDuration
                );

            if (!stunned)
                return;
            PlayLocalHitFlash(
    new Color(1f, 0.75f, 0.05f, 1f),
    0.14f,
    0.22f);
            KartBananaTrap.PlayDizzyEffect(car);

            Rigidbody body = car.TargetBody;

            if (body == null || body.isKinematic)
                return;

            Vector3 velocity = body.linearVelocity;

            body.linearVelocity =
                new Vector3(
                    velocity.x * 0.68f,
                    velocity.y,
                    velocity.z * 0.68f
                );

            body.AddTorque(
                car.transform.up *
                (
                    Random.value < 0.5f
                        ? 12.5f
                        : -12.5f
                ),
                ForceMode.Impulse
            );
        }

        [Rpc(SendTo.Owner)]
        private void ApplyBananaHitClientRpc(
            Vector3 groundPoint,
            float stunDuration)
        {
            ExecuteLocalBananaSlip(stunDuration);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncBananaSlipVfxRpc(
            Vector3 groundPoint,
            Vector3 groundNormal,
            bool stunned)
        {
            PoseKart.Audio.AudioManager.PlayBananaSlip(
                groundPoint
            );

            KartParticleEffect.Burst(
                groundPoint +
                groundNormal * 0.25f,
                stunned
                    ? new Color(
                        1f,
                        0.82f,
                        0.08f,
                        1f
                    )
                    : new Color(
                        0.25f,
                        0.9f,
                        1f,
                        1f
                    ),
                stunned ? 34 : 20
            );

            if (stunned && car != null)
            {
                KartBananaTrap.PlayDizzyEffect(car);
            }
        }

        // ============================================================
        // INK HIT
        // ============================================================

        /// <summary>
        /// Server gọi khi Ink projectile trúng racer.
        ///
        /// Human:
        /// overlay chỉ xuất hiện trên màn hình victim owner.
        ///
        /// AI:
        /// Server áp AI debuff.
        /// </summary>
        public void ServerApplyInkHit(
            Vector3 impactPoint,
            float blindDuration,
            float aiSteeringWobble,
            float aiThrottleMultiplier,
            CarController attacker = null)
        {
            if (!IsServer)
                return;

            if (wreck != null && wreck.IsGameplayProtected)
            {
                SyncInkImpactVfxRpc(impactPoint, false);
                return;
            }

            if (ServerTryBlockItem(impactPoint))
            {
                ServerBroadcastRaceInteraction(
                    PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.InkBlocked,
                    attacker,
                    car);
                SyncInkImpactVfxRpc(
                    impactPoint,
                    false
                );

                return;
            }

            ServerBroadcastRaceInteraction(
                PoseKart.UI.PoseKartRaceInteractionFeed.InteractionKind.InkHit,
                attacker,
                car);

            // FIX:
            // Human/AI phải phân biệt bằng NetworkObject,
            // không phải RaceParticipant.IsPlayer.
            bool isHuman =
                IsHumanNetworkPlayer();

            if (isHuman)
            {
                if (IsRemoteHumanPlayer())
                {
                    // Player 2, Player 3...
                    ApplyInkScreenOverlayClientRpc(
                        blindDuration
                    );
                }
                else if (IsOwner)
                {
                    // Host local human.
                    KartInkScreenOverlay.ShowUntil(
                        Time.time + blindDuration
                    );
                    PlayLocalHitFlash(
    new Color(0.04f, 0.08f, 0.2f, 1f),
    0.20f,
    0.18f);
                }
            }
            else
            {
                // AI debuff authoritative trên Server.
                if (car == null)
                    car = GetComponent<CarController>();

                if (car != null)
                {
                    KartInkStatus.TryApply(
                        car,
                        blindDuration,
                        aiSteeringWobble,
                        aiThrottleMultiplier,
                        impactPoint
                    );
                }
            }

            SyncInkImpactVfxRpc(
                impactPoint,
                true
            );
        }

        [Rpc(SendTo.Owner)]
        private void ApplyInkScreenOverlayClientRpc(
            float blindDuration)
        {
            KartInkScreenOverlay.ShowUntil(

                Time.time + blindDuration
            );
            PlayLocalHitFlash(
    new Color(0.04f, 0.08f, 0.2f, 1f),
    0.20f,
    0.18f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncInkImpactVfxRpc(
            Vector3 impactPoint,
            bool applied)
        {
            PoseKart.Audio.AudioManager.PlayMissileImpact(
                impactPoint
            );

            KartParticleEffect.Burst(
                impactPoint,
                applied
                    ? new Color(
                        0.05f,
                        0.06f,
                        0.1f,
                        1f
                    )
                    : new Color(
                        0.22f,
                        0.86f,
                        1f,
                        1f
                    ),
                applied ? 32 : 18
            );
        }
    }
}
