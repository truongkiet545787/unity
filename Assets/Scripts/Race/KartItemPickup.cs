using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class KartItemPickup : MonoBehaviour
    {
        // Unity serializes enum values as integers. NEVER renumber the legacy slots.
        // Boost = 0 is reserved for old scene/prefab/HUD compatibility; it is disabled.
        public enum ItemType
        {
            Boost = 0, // LEGACY / DISABLED. Not obtainable or usable.
            HomingMissile = 1,
            BananaTrap = 2,
            Shield = 3,
            InkShot = 4
        }

        [SerializeField] private ItemType itemType;
        [SerializeField] private float respawnSeconds = 6f;

        private static readonly System.Collections.Generic.List<KartItemPickup> allPickups = new System.Collections.Generic.List<KartItemPickup>();
        public static System.Collections.Generic.IReadOnlyList<KartItemPickup> AllPickups => allPickups;

        private int pickupIndex = -1;
        public int PickupIndex => pickupIndex;
        public float RespawnSeconds => respawnSeconds;
        private bool isAvailable = true;
        public bool IsAvailable => isAvailable && (trigger == null || trigger.enabled);

        public static void InitializeAllPickups()
        {
            if (allPickups.Count > 0) return;
            allPickups.AddRange(FindObjectsByType<KartItemPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            allPickups.Sort((a, b) =>
            {
                Vector3 pa = a.transform.position;
                Vector3 pb = b.transform.position;
                int cx = pa.x.CompareTo(pb.x);
                if (cx != 0) return cx;
                int cy = pa.y.CompareTo(pb.y);
                if (cy != 0) return cy;
                return pa.z.CompareTo(pb.z);
            });
            for (int i = 0; i < allPickups.Count; i++)
            {
                if (allPickups[i] != null)
                    allPickups[i].pickupIndex = i;
            }
        }

        [Header("Ink Shot - Item Pool Injection")]
        [Tooltip("Chance to replace the existing item-pool roll with Ink Shot. Set on item boxes; 0 disables Ink.")]
        [SerializeField, Range(0f, 1f)] private float inkShotChance = 0.12f;

        private Collider trigger;
        private Renderer[] visuals;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            visuals = GetComponentsInChildren<Renderer>(true);
            InitializeAllPickups();
        }

        public void Configure(ItemType type)
        {
            itemType = type;
        }

        public ItemType RollAuthoritativeItem()
        {
            return inkShotChance > 0f && Random.value < inkShotChance
                ? ItemType.InkShot
                : RollNonBoostItem();
        }

        public void ServerSetCollected()
        {
            isAvailable = false;
            CancelInvoke(nameof(Respawn));
            Invoke(nameof(Respawn), respawnSeconds);
        }

        public void ClientSyncCollect(float duration)
        {
            isAvailable = false;
            if (trigger != null)
                trigger.enabled = false;

            foreach (Renderer visual in visuals)
            {
                if (visual != null)
                    visual.enabled = false;
            }

            PoseKart.Audio.AudioManager.PlayPickup(null, transform.position);
            KartParticleEffect.Burst(transform.position, new Color(1f, .78f, .16f, 1f), 48);

            CancelInvoke(nameof(Respawn));
            Invoke(nameof(Respawn), duration);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isAvailable || trigger == null || !trigger.enabled)
                return;

            Rigidbody otherBody = other.attachedRigidbody;
            CarController car = otherBody != null ? otherBody.GetComponentInParent<CarController>() : null;
            if (car == null)
                car = other.GetComponentInParent<CarController>();
            if (car == null)
                return;

            if (PoseKartNetworkItemBridge.IsOnlineRace)
            {
                var bridge = car.GetComponent<PoseKartNetworkItemBridge>();
                if (bridge != null)
                {
                    bridge.HandlePickupTrigger(pickupIndex);
                    return;
                }
            }

            // OFFLINE MODE (original logic)
            KartItemInventory inventory = GetOrAddComponent<KartItemInventory>(car.gameObject);
            if (inventory == null)
                return;

            ItemType rolledItem = RollAuthoritativeItem();
            if (!inventory.TryCollect(rolledItem))
                return;

            PoseKart.Audio.AudioManager.PlayPickup(car, transform.position);
            KartParticleEffect.Burst(transform.position, new Color(1f, .78f, .16f, 1f), 48);

            isAvailable = false;
            trigger.enabled = false;
            foreach (Renderer visual in visuals)
            {
                if (visual != null)
                    visual.enabled = false;
            }

            CancelInvoke(nameof(Respawn));
            Invoke(nameof(Respawn), respawnSeconds);
        }

        private void Respawn()
        {
            isAvailable = true;
            if (trigger != null)
                trigger.enabled = true;

            foreach (Renderer visual in visuals)
            {
                if (visual != null)
                    visual.enabled = true;
            }
        }

        public static bool Activate(CarController car, ItemType type)
        {
            return Activate(car, type, null);
        }

        public static bool Activate(CarController car, ItemType type, KartItemInventory settings)
        {
            if (car == null)
                return false;

            // The old Boost enum value is intentionally retained for serialized data,
            // but cannot be activated. Nitro remains CarController's speed mechanic.
            if (type == ItemType.Boost)
                return false;

            if (type == ItemType.HomingMissile)
                return LaunchMissile(car, settings);

            if (type == ItemType.BananaTrap)
            {
                PoseKartBananaTripleLauncher tripleLauncher = car.GetComponent<PoseKartBananaTripleLauncher>() ?? car.GetComponentInChildren<PoseKartBananaTripleLauncher>();
                if (tripleLauncher != null)
                {
                    return tripleLauncher.TryFire(settings);
                }
                return DropBanana(car, settings);
            }

            if (type == ItemType.InkShot)
                return LaunchInk(car, settings);

            if (type == ItemType.Shield)
            {
                KartItemDefense defense = GetOrAddComponent<KartItemDefense>(car.gameObject);
                if (defense == null)
                    return false;

                if (settings != null)
                {
                    defense.ConfigureShield(
                        settings.ShieldVisualPrefab,
                        settings.ShieldParticlePrefab,
                        settings.ShieldDuration,
                        settings.MaximumShieldBlockedHits,
                        settings.ShieldColor);
                }

                defense.ActivateShield();
                return true;
            }

            return false;
        }

        private static bool warnedLegacyBoostPool;

        private static ItemType RollNonBoostItem()
        {
            // Rejection sampling preserves the relative Missile/Banana/Shield weights
            // configured in the existing KartItemPoolConfig without requiring its source.
            // Bounded attempts protect against a misconfigured Boost-only pool.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                ItemType result = KartItemPoolConfig.RollItem();
                if (result == ItemType.HomingMissile ||
                    result == ItemType.BananaTrap ||
                    result == ItemType.Shield ||
                    result == ItemType.InkShot)
                    return result;
            }

            if (!warnedLegacyBoostPool)
            {
                warnedLegacyBoostPool = true;
                Debug.LogWarning(
                    "[PoseKart Items] Item pool repeatedly returned legacy Boost. " +
                    "Set KartItemPoolConfig > Boost Weight = 0. " +
                    "Using a non-Boost fallback meanwhile.");
            }

            // Safe fallback even when the pool has only Boost or zero valid weights.
            switch (Random.Range(0, 3))
            {
                case 0: return ItemType.HomingMissile;
                case 1: return ItemType.BananaTrap;
                default: return ItemType.Shield;
            }
        }

        private static bool LaunchMissile(CarController owner, KartItemInventory settings)
        {
            if (owner == null || owner.TargetBody == null)
                return false;

            float range = settings != null ? settings.MissileTargetRange : 130f;
            float angle = settings != null ? settings.MissileTargetAngle : 110f;
            CarController target = FindTarget(owner, range, angle);
            if (target == null)
                return false;

            Vector3 spawnPosition = owner.transform.position + owner.transform.forward * 2.5f + Vector3.up * .8f;
            Vector3 targetDirection = target.transform.position - spawnPosition;
            Quaternion spawnRotation = targetDirection.sqrMagnitude > .001f
                ? Quaternion.LookRotation(targetDirection)
                : owner.transform.rotation;

            bool usingPrefab = settings != null && settings.MissilePrefab != null;
            GameObject projectile = usingPrefab
                ? Instantiate(settings.MissilePrefab, spawnPosition, spawnRotation)
                : new GameObject("AutoAim_Missile");

            if (projectile == null)
                return false;

            projectile.name = "AutoAim_Missile";
            if (!projectile.activeSelf)
                projectile.SetActive(true);
            if (!usingPrefab)
                projectile.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            // IMPORTANT: UnityEngine.Object uses overloaded == null semantics.
            // Do not use ?? to retrieve/add Unity components: a missing component
            // can otherwise leave a non-usable Unity reference behind.
            // Create the Rigidbody BEFORE adding the fallback missile visual.
            Rigidbody body = GetOrAddComponent<Rigidbody>(projectile);
            if (body == null)
            {
                Debug.LogError("[KartItemPickup] Could not create a Rigidbody for AutoAim_Missile.", projectile);
                Destroy(projectile);
                return false;
            }

            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (!usingPrefab)
                GetOrAddComponent<KartMissileModel>(projectile);

            Collider[] projectileColliders = projectile.GetComponentsInChildren<Collider>(true);
            if (projectileColliders.Length == 0)
            {
                SphereCollider sphere = projectile.AddComponent<SphereCollider>();
                if (sphere == null)
                {
                    Debug.LogError("[KartItemPickup] Could not create a collider for AutoAim_Missile.", projectile);
                    Destroy(projectile);
                    return false;
                }

                sphere.radius = .35f;
                sphere.isTrigger = true;
            }
            else
            {
                foreach (Collider projectileCollider in projectileColliders)
                {
                    if (projectileCollider != null)
                        projectileCollider.isTrigger = true;
                }
            }

            HomingKartMissile homing = GetOrAddComponent<HomingKartMissile>(projectile);
            if (homing == null)
            {
                Debug.LogError("[KartItemPickup] Could not create HomingKartMissile on AutoAim_Missile.", projectile);
                Destroy(projectile);
                return false;
            }

            // BẮT BUỘC missile phải chạy FixedUpdate trên Server.
            homing.enabled = true;

            homing.Configure(
    owner,
    target,
    settings != null ? settings.MissileSpeed : 62f,
    settings != null ? settings.MissileTrackingStrength : 8f,
    settings != null ? settings.MissileLifetime : 7f,
    settings != null ? settings.MissileImpactExplosionPrefab : null,
    settings != null ? settings.MissileImpactExplosionLifetime : 4f,
    settings != null ? settings.MissileExplosionRadius : 6f,
    settings != null ? settings.MissileMaximumHorizontalKnockback : 5f,
    settings != null ? settings.MissileMaximumUpwardKnockback : 2.5f,

    // Hai thằng này lúc trước bị đảo.
    settings != null ? settings.MissileImpactTorque : 12f,
    settings != null ? settings.MissileMinimumEffectiveDistance : .5f,

    settings != null ? settings.MissileExplosionFalloff : null,
    settings != null ? settings.MissileKnockbackRecoveryDuration : .7f,
    settings == null || settings.MissileExplosionAffectsNearbyRacers,
    settings == null || settings.MissileExplosionLineOfSightCheck,
    settings != null ? settings.MissileTargetPrediction : .75f,
    settings != null ? settings.MissileMaximumPredictionTime : 1.25f,
    settings != null ? settings.MissileGroundFirePrefab : null,
    settings != null ? settings.MissileGroundFireDuration : 5f,
    settings != null ? settings.MissileGroundFireRadius : 3f,
    settings == null || settings.MissileGroundFireAffectsRacers
);
            if (PoseKartNetworkItemBridge.IsOnlineRace)
            {
                var netObj = projectile.GetComponent<Unity.Netcode.NetworkObject>();

                if (netObj == null)
                {
                    Debug.LogError(
                        "[KartItemPickup] Online missile prefab is missing NetworkObject.",
                        projectile
                    );

                    Destroy(projectile);
                    return false;
                }

                if (!netObj.IsSpawned)
                    netObj.Spawn(true);
            }

            PoseKart.Audio.AudioManager.PlayMissileLaunch(projectile.transform.position);
            return true;
        }

        private static bool LaunchInk(CarController owner, KartItemInventory settings)
        {
            if (owner == null || owner.TargetBody == null)
                return false;

            float range = settings != null ? settings.InkTargetRange : 95f;
            float angle = settings != null ? settings.InkTargetAngle : 85f;
            CarController target = FindTarget(owner, range, angle);
            if (target == null)
                return false; // Inventory retains the item when no target is available.

            Transform muzzle = null;
            KartLauncherSkill launcher = owner.GetComponent<KartLauncherSkill>() ?? owner.GetComponentInChildren<KartLauncherSkill>();
            if (launcher != null)
            {
                muzzle = launcher.CurrentMuzzlePoint;
            }

            return KartInkProjectile.Launch(
                owner,
                target,
                settings != null ? settings.InkProjectileSpeed : 68f,
                settings != null ? settings.InkProjectileLifetime : 3.5f,
                settings != null ? settings.InkBlindDuration : 3f,
                settings != null ? settings.InkAISteeringWobble : 0.28f,
                settings != null ? settings.InkAIThrottleMultiplier : 0.86f,
                settings != null ? settings.InkProjectilePrefab : null,
                muzzle);
        }

        private static CarController FindTarget(CarController owner, float range, float angle)
        {
            if (owner == null || owner.TargetBody == null)
                return null;

            CarController best = null;
            float bestScore = float.PositiveInfinity;
            float rangeSqr = Mathf.Max(1f, range * range);

            foreach (CarController candidate in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (candidate == null || candidate == owner || candidate.TargetBody == null)
                    continue;

                if (PoseKartNetworkItemBridge.IsOnlineRace)
                {
                    if (!candidate.gameObject.activeInHierarchy)
                        continue;
                }
                else
                {
                    if (!candidate.isActiveAndEnabled)
                        continue;
                }

                RaceParticipant participant = candidate.GetComponent<RaceParticipant>();
                if (participant != null && participant.Finished)
                    continue;

                Vector3 delta = candidate.TargetBody.worldCenterOfMass - owner.TargetBody.worldCenterOfMass;
                delta.y = 0f;
                float distanceSqr = delta.sqrMagnitude;
                if (distanceSqr < .01f || distanceSqr > rangeSqr)
                    continue;

                float candidateAngle = Vector3.Angle(owner.transform.forward, delta);
                if (candidateAngle > angle)
                    continue;

                float score = distanceSqr * (1f + candidateAngle / Mathf.Max(1f, angle));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool DropBanana(CarController owner, KartItemInventory settings)
        {
            float dropDistance = settings != null ? settings.BananaDropDistance : 2.2f;
            float lifetime = settings != null ? settings.BananaLifetime : 12f;
            float slipDuration = settings != null ? settings.BananaSlipDuration : 1f;
            float immunity = settings != null ? settings.BananaOwnerImmunity : .65f;

            int groundLayers = settings != null ? settings.BananaGroundLayers.value : Physics.DefaultRaycastLayers;
            float probeHeight = settings != null ? settings.BananaGroundProbeHeight : 1.5f;
            float probeDistance = settings != null ? settings.BananaGroundProbeDistance : 6f;
            float maximumSlope = settings != null ? settings.BananaMaximumGroundSlope : 55f;

            GameObject bananaPrefab = settings != null && settings.BananaPrefab != null
                ? settings.BananaPrefab
                : KartItemInventory.SharedBananaPrefab;

            Vector3 rearCenter = owner.transform.position - owner.transform.forward * dropDistance;
            Vector3 right = owner.transform.right;
            Vector3 forward = owner.transform.forward;

            // 3 bananas spread: Center, Left (-1.35m), Right (+1.35m) with slight stagger
            Vector3[] offsets = new Vector3[]
            {
                Vector3.zero,
                -right * 1.35f - forward * 0.35f,
                right * 1.35f - forward * 0.35f
            };

            int spawnedCount = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 desired = rearCenter + offsets[i];
                if (!TryFindBananaGround(owner, desired, probeHeight, probeDistance, maximumSlope,
                    groundLayers, out RaycastHit ground))
                {
                    // Fallback to center ground if side edge failed
                    if (!TryFindBananaGround(owner, rearCenter, probeHeight, probeDistance, maximumSlope,
                        groundLayers, out ground))
                    {
                        continue;
                    }
                }

                Vector3 forwardOnGround = Vector3.ProjectOnPlane(forward, ground.normal);
                if (forwardOnGround.sqrMagnitude < .001f)
                    forwardOnGround = Vector3.ProjectOnPlane(Vector3.forward, ground.normal);
                Quaternion surfaceRotation = Quaternion.LookRotation(forwardOnGround.normalized, ground.normal);
                Quaternion prefabRotation = bananaPrefab != null ? bananaPrefab.transform.rotation : Quaternion.identity;
                Quaternion placementRotation = surfaceRotation * prefabRotation;
                Vector3 position = ground.point + ground.normal * .035f;

                GameObject banana = bananaPrefab != null
                    ? Instantiate(bananaPrefab, position, placementRotation)
                    : KartBananaTrap.CreateFallbackVisual();

                if (banana == null)
                    continue;

                banana.transform.SetPositionAndRotation(position, placementRotation);
                if (!banana.activeSelf)
                    banana.SetActive(true);

                KartBananaTrap trap = GetOrAddComponent<KartBananaTrap>(banana);
                if (trap != null)
                {
                    trap.enabled = true;
                    trap.Configure(owner, lifetime, slipDuration, immunity, ground.point, ground.normal);
                    spawnedCount++;

                    if (PoseKartNetworkItemBridge.IsOnlineRace && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
                    {
                        var netObj = banana.GetComponent<Unity.Netcode.NetworkObject>();
                        if (netObj != null && !netObj.IsSpawned)
                        {
                            netObj.Spawn(true);
                        }
                    }
                }
            }

            if (spawnedCount > 0)
            {
                PoseKart.Audio.AudioManager.PlayBananaDrop(rearCenter);
                return true;
            }

            Debug.LogWarning("[Banana] DROP FAILED: no valid ground below the rear of " +
                owner.name + ". Check Banana Ground Layers / Probe Distance.", owner);
            return false;
        }

        private static bool TryFindBananaGround(
            CarController owner,
            Vector3 desired,
            float probeHeight,
            float probeDistance,
            float maximumSlope,
            int groundLayers,
            out RaycastHit bestHit)
        {
            bestHit = default;
            if (owner == null || groundLayers == 0)
                return false;

            Vector3 origin = desired + Vector3.up * Mathf.Max(0.5f, probeHeight);
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                Mathf.Max(1f, probeDistance),
                groundLayers,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            float bestDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || collider.transform.IsChildOf(owner.transform))
                    continue;

                CarController hitCar = collider.GetComponentInParent<CarController>();
                if (hitCar != null)
                    continue;

                float slope = Vector3.Angle(hit.normal, Vector3.up);
                if (slope > maximumSlope || hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }

            return found;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
                return null;

            T component = target.GetComponent<T>();
            if (component == null)
                component = target.AddComponent<T>();

            return component;
        }
    }
}
