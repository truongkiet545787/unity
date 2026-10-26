using System.Collections.Generic;
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
        private bool useRequestPending;
        private float useRequestPendingSince;
        private uint nextUseRequestId;
        private uint pendingUseRequestId;
        private bool pickupRequestPending;
        private int pickupRequestPendingIndex = -1;
        private float pickupRequestPendingSince;

        private const float RequestRetryDelay = 1.5f;

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

        // ============================================================
        // UNITY / NGO LIFECYCLE
        // ============================================================

        private void Awake()
        {
            car = GetComponent<CarController>();
            inventory = GetComponent<KartItemInventory>();
            defense = GetComponent<KartItemDefense>();
            participant = GetComponent<RaceParticipant>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkShieldActive.OnValueChanged +=
                OnShieldActiveStateChanged;

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

        public override void OnNetworkDespawn()
        {
            networkShieldActive.OnValueChanged -=
                OnShieldActiveStateChanged;

            base.OnNetworkDespawn();
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
                car = GetComponent<CarController>();

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
            KartItemDefense.PlayDeflectPingSoundAt(
                impactPoint
            );

            PoseKart.Audio.AudioManager.PlayShieldBlock(
                impactPoint
            );

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
            float torqueStrength)
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
            if (ServerTryBlockItem(point))
                return;

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
                torqueStrength * 1.35f
            );

            Rigidbody body = car.TargetBody;

            if (body != null && !body.isKinematic)
            {
                body.AddForce(
                    Vector3.up * 3.5f,
                    ForceMode.VelocityChange
                );

                Vector3 direction =
                    deltaVelocity.sqrMagnitude > 0.001f
                        ? deltaVelocity.normalized
                        : car.transform.forward;

                Vector3 tumbleAxis =
                    (
                        Vector3.Cross(
                            direction,
                            Vector3.up
                        ) +
                        Random.insideUnitSphere * 0.4f
                    ).normalized;

                body.AddTorque(
                    tumbleAxis *
                    (torqueStrength * 1.5f),
                    ForceMode.Impulse
                );
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
            float stunDuration)
        {
            if (!IsServer)
                return;

            if (ServerTryBlockItem(groundPoint))
            {
                SyncBananaSlipVfxRpc(
                    groundPoint,
                    groundNormal,
                    false
                );

                return;
            }

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
            float aiThrottleMultiplier)
        {
            if (!IsServer)
                return;

            if (ServerTryBlockItem(impactPoint))
            {
                SyncInkImpactVfxRpc(
                    impactPoint,
                    false
                );

                return;
            }

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
