using System;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Race
{
    public sealed class KartItemInventory : MonoBehaviour
    {
        [Header("Item Inventory")]
        [Tooltip("Seconds the held item remains available after pickup. The countdown pauses before GO and while the game is paused.")]
        [SerializeField, Range(5f, 10f)] private float itemLifetimeSeconds = 8f;
        // Legacy serialized icon: hidden, kept to avoid breaking old prefab data.
        [SerializeField, HideInInspector] private Texture2D boostIcon;
        [InspectorName("Missile Icon")]
        [SerializeField] private Texture2D missileIcon;
        [InspectorName("Banana Icon")]
        [SerializeField] private Texture2D bananaIcon;
        [InspectorName("Shield Icon")]
        [SerializeField] private Texture2D shieldIcon;

        [Header("Homing Missile Visual")]
        [Tooltip("Optional missile prefab. Its local +Z axis should point toward the nose. If empty, the built-in procedural missile is used.")]
        [SerializeField] private GameObject missilePrefab;

        // Legacy fields/getters retained for external script compatibility only.
        // The Boost item cannot be collected or activated.
        [SerializeField, HideInInspector, Min(0.1f)] private float boostDuration = 1.2f;
        [SerializeField, HideInInspector, Min(0f)] private float boostImpulse = 8f;

        [Header("Homing Missile")]
        [SerializeField, Min(1f)] private float missileTargetRange = 130f;
        [SerializeField, Range(1f, 180f)] private float missileTargetAngle = 110f;
        [SerializeField, Min(1f)] private float missileSpeed = 62f;
        [SerializeField, Min(0.1f)] private float missileTrackingStrength = 8f;
        [Tooltip("How strongly the missile leads the target's current velocity. 0 aims at the current position; 1 uses the full calculated lead.")]
        [SerializeField, Range(0f, 1f)] private float missileTargetPrediction = 0.75f;
        [Tooltip("Maximum seconds to aim ahead of a moving target. Keeps prediction stable during sharp turns.")]
        [SerializeField, Range(0f, 3f)] private float missileMaximumPredictionTime = 1.25f;
        [Tooltip("Number of missiles granted by one Homing Missile pickup. Each Space press fires one missile.")]
        [SerializeField, Min(1)] private int missileShotsPerPickup = 3;
        [SerializeField, Min(0.5f)] private float missileLifetime = 7f;
        [Tooltip("Optional world-space VFX spawned once when the missile detonates. Gameplay still works when empty.")]
        [SerializeField] private GameObject missileImpactExplosionPrefab;
        [SerializeField, Min(0.25f)] private float missileImpactExplosionLifetime = 4f;
        [Tooltip("Gameplay radius of the missile shockwave. This is independent from the VFX prefab size.")]
        [SerializeField, Min(0.5f)] private float missileExplosionRadius = 6f;
        [SerializeField, Min(0f)] private float missileMaximumHorizontalKnockback = 5f;
        [SerializeField, Min(0f)] private float missileMaximumUpwardKnockback = 2.5f;
        [Tooltip("Angular impulse applied by a missile hit. Higher values make the kart flip more aggressively.")]
        [SerializeField, Min(0f)] private float missileImpactTorque = 12f;
        [SerializeField, Min(0f)] private float missileMinimumEffectiveDistance = 0.5f;
        [Tooltip("Falloff input is 0 at the edge of the radius and 1 at the explosion center.")]
        [SerializeField] private AnimationCurve missileExplosionFalloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Min(0.05f)] private float missileKnockbackRecoveryDuration = 0.7f;
        [SerializeField] private bool missileExplosionAffectsNearbyRacers = true;
        [SerializeField] private bool missileExplosionLineOfSightCheck = true;

        [Header("Missile Ground Fire")]
        [Tooltip("Prefab ngọn lửa bùng cháy trên mặt đất khi tên lửa chạm đất (có thể gán prefab particle lửa sau).")]
        [SerializeField] private GameObject missileGroundFirePrefab;
        [SerializeField, Min(1f)] private float missileGroundFireDuration = 5f;
        [SerializeField, Min(0.5f)] private float missileGroundFireRadius = 3f;
        [SerializeField] private bool missileGroundFireAffectsRacers = true;

        [Header("Ink Shot")]
        [Tooltip("Dedicated prefab for ink cannon ball projectile. If unassigned, loads from Resources/CannonBall_Ink or falls back to procedural.")]
        [SerializeField] private GameObject inkProjectilePrefab;
        [Tooltip("Forward target acquisition; the shot is retained when no racer is in the cone.")]
        [SerializeField, Min(1f)] private float inkTargetRange = 95f;
        [SerializeField, Range(1f, 180f)] private float inkTargetAngle = 85f;
        [SerializeField, Min(1f)] private float inkProjectileSpeed = 68f;
        [SerializeField, Min(0.5f)] private float inkProjectileLifetime = 3.5f;
        [SerializeField, Min(0.25f)] private float inkBlindDuration = 3f;
        [SerializeField, Range(0f, 0.6f)] private float inkAISteeringWobble = 0.28f;
        [SerializeField, Range(0.25f, 1f)] private float inkAIThrottleMultiplier = 0.86f;

        [Header("Banana Trap")]
        [SerializeField] private GameObject bananaPrefab;
        [SerializeField, Min(0.1f)] private float bananaDropDistance = 2.2f;
        [SerializeField, Min(0.5f)] private float bananaLifetime = 12f;
        [SerializeField, Min(0.1f)] private float bananaSlipDuration = 1f;
        [SerializeField, Min(0f)] private float bananaOwnerImmunity = 0.65f;
        [Tooltip("Layers accepted as a banana placement surface. Leave at Everything unless your project uses a dedicated track layer.")]
        [SerializeField] private LayerMask bananaGroundLayers = ~0;
        [SerializeField, Min(1f)] private float bananaGroundProbeHeight = 1.5f;
        [SerializeField, Min(1f)] private float bananaGroundProbeDistance = 6f;
        [SerializeField, Range(5f, 89f)] private float bananaMaximumGroundSlope = 55f;

        [Header("Shield")]
        [SerializeField] private GameObject shieldVisualPrefab;
        [Tooltip("Prefab Particle hiệu ứng hào quang/vòng bảo vệ quanh xe khi bật khiên (tùy chọn).")]
        [SerializeField] private GameObject shieldParticlePrefab;
        [SerializeField, Min(0.1f)] private float shieldDuration = 5f;
        [SerializeField, Min(1)] private int maximumShieldBlockedHits = 1;
        [SerializeField] private Color shieldColor = new Color(0.2f, 0.9f, 1f, 0.2f);

        [Header("Runtime Item Debug")]
        [InspectorName("Current Item")]
        [SerializeField] private string currentItemLabel = "Empty";
        [InspectorName("Item Use Input")]
        [SerializeField] private string itemUseInput = "SPACE / MediaPipe SKILL";
        public string ItemUseInput => itemUseInput;
        [InspectorName("Item Time Remaining")]
        [SerializeField] private float itemTimeRemaining;

        private CarController car;
        private RaceParticipant racer;
        private KartItemPickup.ItemType held = KartItemPickup.ItemType.HomingMissile;
        private bool hasItem;
        private int itemCharges;
        private float aiUseTime;
        private float noTargetFeedbackUntil;
        private float nextNoTargetLog;
        private static GameObject sharedBananaPrefab;

        /// <summary>Raised after this inventory successfully receives an item.</summary>
        public event Action<KartItemPickup.ItemType> ItemCollected;

        /// <summary>Raised after an item has been activated and consumed.</summary>
        public event Action<KartItemPickup.ItemType> ItemUsed;

        /// <summary>Raised when an activation was rejected, for example a missile with no target.</summary>
        public event Action<KartItemPickup.ItemType> ItemActivationFailed;

        /// <summary>Raised when a held item expires without being used.</summary>
        public event Action<KartItemPickup.ItemType> ItemExpired;

        private void Awake()
        {
            car = GetComponent<CarController>();
            racer = GetComponent<RaceParticipant>();
            if (bananaPrefab != null)
                sharedBananaPrefab = bananaPrefab;
        }

        public static GameObject SharedBananaPrefab
        {
            get
            {
                if (sharedBananaPrefab != null)
                    return sharedBananaPrefab;

                // AI inventories are created by CarController at runtime. Find the
                // serialized player inventory once when those inventories need a
                // shared banana visual, rather than requiring duplicate references
                // on every AI vehicle.
                foreach (KartItemInventory inventory in
                         FindObjectsByType<KartItemInventory>(FindObjectsSortMode.None))
                {
                    if (inventory != null && inventory.bananaPrefab != null)
                    {
                        sharedBananaPrefab = inventory.bananaPrefab;
                        break;
                    }
                }

                return sharedBananaPrefab;
            }
        }
        public KartItemPickup.ItemType CurrentItem => held;
        public bool HasItem => hasItem;
        public int ItemCharges => hasItem ? Mathf.Max(1, itemCharges) : 0;
        public float ItemTimeRemaining => hasItem ? Mathf.Max(0f, itemTimeRemaining) : 0f;
        public float ItemLifetimeSeconds => itemLifetimeSeconds;
        public int MissileShotsPerPickup => missileShotsPerPickup;
        public bool NoTargetFeedbackActive => Time.unscaledTime < noTargetFeedbackUntil;
        public float BoostDuration => boostDuration;
        public float BoostImpulse => boostImpulse;
        public float MissileTargetRange => missileTargetRange;
        public float MissileTargetAngle => missileTargetAngle;
        public float MissileSpeed => missileSpeed;
        public float MissileTrackingStrength => missileTrackingStrength;
        public float MissileTargetPrediction => missileTargetPrediction;
        public float MissileMaximumPredictionTime => missileMaximumPredictionTime;
        public float MissileLifetime => missileLifetime;
        public GameObject MissileImpactExplosionPrefab => missileImpactExplosionPrefab;
        public float MissileImpactExplosionLifetime => missileImpactExplosionLifetime;
        public float MissileExplosionRadius => missileExplosionRadius;
        public float MissileMaximumHorizontalKnockback => missileMaximumHorizontalKnockback;
        public float MissileMaximumUpwardKnockback => missileMaximumUpwardKnockback;
        public float MissileImpactTorque => missileImpactTorque;
        public float MissileMinimumEffectiveDistance => missileMinimumEffectiveDistance;
        public AnimationCurve MissileExplosionFalloff => missileExplosionFalloff;
        public float MissileKnockbackRecoveryDuration => missileKnockbackRecoveryDuration;
        public bool MissileExplosionAffectsNearbyRacers => missileExplosionAffectsNearbyRacers;
        public bool MissileExplosionLineOfSightCheck => missileExplosionLineOfSightCheck;
        public GameObject MissileGroundFirePrefab => missileGroundFirePrefab;
        public float MissileGroundFireDuration => missileGroundFireDuration;
        public float MissileGroundFireRadius => missileGroundFireRadius;
        public bool MissileGroundFireAffectsRacers => missileGroundFireAffectsRacers;
        public GameObject BananaPrefab => bananaPrefab;
        public float BananaDropDistance => bananaDropDistance;
        public float BananaLifetime => bananaLifetime;
        public float BananaSlipDuration => bananaSlipDuration;
        public float BananaOwnerImmunity => bananaOwnerImmunity;
        public LayerMask BananaGroundLayers => bananaGroundLayers;
        public float BananaGroundProbeHeight => bananaGroundProbeHeight;
        public float BananaGroundProbeDistance => bananaGroundProbeDistance;
        public float BananaMaximumGroundSlope => bananaMaximumGroundSlope;
        public GameObject ShieldVisualPrefab => shieldVisualPrefab;
        public GameObject ShieldParticlePrefab => shieldParticlePrefab;
        public float ShieldDuration => shieldDuration;
        public int MaximumShieldBlockedHits => maximumShieldBlockedHits;
        public Color ShieldColor => shieldColor;
        public GameObject MissilePrefab => missilePrefab;
        private static GameObject sharedInkPrefab;
        public GameObject InkProjectilePrefab
        {
            get
            {
                if (inkProjectilePrefab != null)
                    return inkProjectilePrefab;

                if (sharedInkPrefab != null)
                    return sharedInkPrefab;

                foreach (KartItemInventory inventory in
                         FindObjectsByType<KartItemInventory>(FindObjectsSortMode.None))
                {
                    if (inventory != null && inventory.inkProjectilePrefab != null)
                    {
                        sharedInkPrefab = inventory.inkProjectilePrefab;
                        return sharedInkPrefab;
                    }
                }

                sharedInkPrefab = Resources.Load<GameObject>("CannonBall_Ink");
                return sharedInkPrefab;
            }
        }
        public float InkTargetRange => inkTargetRange;
        public float InkTargetAngle => inkTargetAngle;
        public float InkProjectileSpeed => inkProjectileSpeed;
        public float InkProjectileLifetime => inkProjectileLifetime;
        public float InkBlindDuration => inkBlindDuration;
        public float InkAISteeringWobble => inkAISteeringWobble;
        public float InkAIThrottleMultiplier => inkAIThrottleMultiplier;
        public bool TryCollect(KartItemPickup.ItemType item)
        {
            // Last line of defense against legacy Boost grants from any caller.
            if (hasItem || item == KartItemPickup.ItemType.Boost)
                return false;
            if (item != KartItemPickup.ItemType.HomingMissile &&
                item != KartItemPickup.ItemType.BananaTrap &&
                item != KartItemPickup.ItemType.Shield &&
                item != KartItemPickup.ItemType.InkShot)
                return false;
            held = item;
            itemCharges = item == KartItemPickup.ItemType.HomingMissile
                ? Mathf.Max(1, missileShotsPerPickup)
                : 1;
            hasItem = true;
            itemTimeRemaining = Mathf.Max(5f, itemLifetimeSeconds);
            currentItemLabel = held.ToString();
            aiUseTime = Time.time + UnityEngine.Random.Range(1.5f, 4f);
            noTargetFeedbackUntil = 0f;
            if (racer != null && racer.IsPlayer)
            {
                PoseKart.Audio.AudioManager.PlayItemReceived(car);
            }
            ItemCollected?.Invoke(item);
            return true;
        }
        private void Update()
        {
            if (!hasItem) return;

            // Do not burn an item during the pre-race countdown. Using a
            // countdown value instead of Time.time also naturally pauses while
            // the game is paused.
            if (car == null || !car.ControlEnabled)
                return;

            itemTimeRemaining = Mathf.Max(0f, itemTimeRemaining - Time.deltaTime);
            if (itemTimeRemaining <= 0f)
            {
                ExpireItem();
                return;
            }
            

            bool player = racer != null && racer.IsPlayer;
            if (player)
            {
                // Nếu xe đang kích hoạt tháp súng / mortar launcher, để KartLauncherSkill xử lý phím Space và khai hỏa
                KartLauncherSkill launcher = GetComponent<KartLauncherSkill>() ?? GetComponentInChildren<KartLauncherSkill>();
                if (launcher != null && launcher.IsActive)
                    return;

                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    if (PoseKartNetworkItemBridge.IsOnlineRace)
                    {
                        var bridge = GetComponent<PoseKartNetworkItemBridge>();
                        if (bridge != null)
                        {
                            bridge.RequestUseItem();
                            return;
                        }
                    }
                    UseItem();
                }
            }
            else
            {
                var brain = GetComponent<PoseKart.AI.AIRacerBrain>();

                if (PoseKartNetworkItemBridge.IsOnlineRace)
                {
                    var networkManager = Unity.Netcode.NetworkManager.Singleton;
                    var networkObject = GetComponent<Unity.Netcode.NetworkObject>();

                    // Remote human player:
                    // racer.IsPlayer == false trên máy không sở hữu nó,
                    // nhưng tuyệt đối không được coi nó là AI để auto-use item.
                    if (networkObject != null && networkObject.IsPlayerObject)
                        return;

                    // AI online chỉ Server/Host được quyền quyết định dùng item.
                    if (networkManager != null &&
                        networkManager.IsListening &&
                        !networkManager.IsServer)
                    {
                        return;
                    }
                }

                if (brain == null || !brain.enabled)
                {
                    if (Time.time >= aiUseTime)
                    {
                        UseItem();
                    }
                }
                else if (itemTimeRemaining <= 0.8f)
                {
                    // Fallback: don't let item expire if brain held it
                    UseItem();
                }
            }
        }
        public void UseItem()
        {
            if (!hasItem || car == null || !car.ControlEnabled)
                return;

            // =========================================================
            // ONLINE
            // Human player luôn phải đi qua NetworkItemBridge.
            // AI chỉ được phép Activate trực tiếp trên Server.
            // =========================================================
            if (PoseKartNetworkItemBridge.IsOnlineRace)
            {
                var networkObject = GetComponent<Unity.Netcode.NetworkObject>();
                var networkManager = Unity.Netcode.NetworkManager.Singleton;
                var bridge = GetComponent<PoseKartNetworkItemBridge>();

                // Human network player:
                if (networkObject != null && networkObject.IsPlayerObject)
                {
                    // Chỉ local human owner mới gửi request qua ServerRpc
                    if (networkObject.IsOwner && bridge != null)
                    {
                        bridge.RequestUseItem();
                    }
                    // Remote human player tuyệt đối không chạy Activate local
                    return;
                }

                // Bất kỳ non-server peer nào trong Online Race tuyệt đối không được chạy Activate local
                if (networkManager != null &&
                    networkManager.IsListening &&
                    !networkManager.IsServer)
                {
                    return;
                }
            }

            // =========================================================
            // OFFLINE hoặc AI trên SERVER
            // =========================================================
            if (!KartItemPickup.Activate(car, held, this))
            {
                if ((held == KartItemPickup.ItemType.HomingMissile ||
                     held == KartItemPickup.ItemType.InkShot) &&
                    racer != null && racer.IsPlayer)
                {
                    noTargetFeedbackUntil = Time.unscaledTime + 1.5f;

                    if (Time.unscaledTime >= nextNoTargetLog)
                    {
                        bool ink = held == KartItemPickup.ItemType.InkShot;

                        Debug.Log(
                            $"[PoseKart {(ink ? "Ink" : "Missile")}] No target in range " +
                            $"{(ink ? inkTargetRange : missileTargetRange):0}m / " +
                            $"angle {(ink ? inkTargetAngle : missileTargetAngle):0} degrees. Item NOT consumed.",
                            this
                        );

                        nextNoTargetLog = Time.unscaledTime + 2f;
                    }
                }

                aiUseTime = Time.time + 1f;
                ItemActivationFailed?.Invoke(held);
                return;
            }

            KartItemPickup.ItemType usedItem = held;

            if (usedItem == KartItemPickup.ItemType.HomingMissile &&
                itemCharges > 1)
            {
                itemCharges--;
                currentItemLabel = $"{usedItem} ({itemCharges})";

                aiUseTime =
                    Time.time + UnityEngine.Random.Range(0.35f, 0.8f);
            }
            else
            {
                hasItem = false;
                itemCharges = 0;
                itemTimeRemaining = 0f;
                currentItemLabel = "Empty";
            }

            noTargetFeedbackUntil = 0f;
            ItemUsed?.Invoke(usedItem);
        }

        public void SyncFromNetworkUse(int remainingCharges, bool stillHasItem, KartItemPickup.ItemType usedItem)
        {
            held = usedItem;
            itemCharges = remainingCharges;
            hasItem = stillHasItem;
            if (!hasItem)
            {
                itemTimeRemaining = 0f;
                currentItemLabel = "Empty";
            }
            else
            {
                currentItemLabel = $"{held} ({itemCharges})";
            }
            noTargetFeedbackUntil = 0f;
            ItemUsed?.Invoke(usedItem);
        }

        public void NotifyActivationFailedDirect(KartItemPickup.ItemType failedItem)
        {
            noTargetFeedbackUntil = Time.unscaledTime + 1.5f;
            ItemActivationFailed?.Invoke(failedItem);
        }

        public void ConsumeHeldCharge()
        {
            if (!hasItem) return;
            KartItemPickup.ItemType usedItem = held;
            if (usedItem == KartItemPickup.ItemType.HomingMissile && itemCharges > 1)
            {
                itemCharges--;
                currentItemLabel = $"{usedItem} ({itemCharges})";
                aiUseTime = Time.time + UnityEngine.Random.Range(0.35f, 0.8f);
            }
            else
            {
                hasItem = false;
                itemCharges = 0;
                itemTimeRemaining = 0f;
                currentItemLabel = "Empty";
            }
            noTargetFeedbackUntil = 0f;
            ItemUsed?.Invoke(usedItem);
        }

        public void ClearHeldItem()
        {
            if (!hasItem) return;
            KartItemPickup.ItemType usedItem = held;
            hasItem = false;
            itemCharges = 0;
            itemTimeRemaining = 0f;
            currentItemLabel = "Empty";
            noTargetFeedbackUntil = 0f;
            ItemUsed?.Invoke(usedItem);
        }

        public void ResetForRace()
        {
            hasItem = false;
            itemCharges = 0;
            itemTimeRemaining = 0f;
            // HasItem=false means this placeholder cannot be used or shown as an item.
            held = KartItemPickup.ItemType.HomingMissile;
            currentItemLabel = "Empty";
            aiUseTime = 0f;
            noTargetFeedbackUntil = 0f;
            GetComponent<KartItemDefense>()?.ResetForRace();
            GetComponent<KartInkStatus>()?.ClearForRace();
        }

        private void ExpireItem()
        {
            if (!hasItem)
                return;

            KartItemPickup.ItemType expiredItem = held;
            hasItem = false;
            itemCharges = 0;
            itemTimeRemaining = 0f;
            currentItemLabel = "Empty";
            noTargetFeedbackUntil = 0f;
            ItemExpired?.Invoke(expiredItem);
        }
        // Item and Nitro presentation is owned by PoseKartArcadeHUD. Keeping this
        // method empty preserves compatibility with older prefabs without drawing
        // a second IMGUI overlay on top of the Canvas HUD.
        private void OnGUI() { }
    }
}
