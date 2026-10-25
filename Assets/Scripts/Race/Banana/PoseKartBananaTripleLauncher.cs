using System.Collections;
using PoseKart.Audio;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public enum BananaLaunchDirection
    {
        Forward,
        Rearward
    }

    public enum BananaLauncherState
    {
        Hidden,
        Deploying,
        Equipped,
        Firing,
        Retracting
    }

    /// <summary>
    /// Banana Triple Launcher weapon system for PoseKart.
    /// Deploys a rocket launcher on the vehicle when the Banana Trap item is equipped,
    /// fires 3 banana projectiles in a ballistic triangle formation upon activation,
    /// and retracts smoothly back into the vehicle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartBananaTripleLauncher : MonoBehaviour
    {
        [Header("Weapon Mount & Model")]
        [Tooltip("Prefab của tháp phóng chuối (Launcher Prefab). Kéo thả prefab tùy ý vào đây.")]
        [SerializeField] private GameObject launcherPrefab;
        [Tooltip("Transform điểm gắn vũ khí trên xe (BananaWeaponMount). Tự động tạo nếu để trống.")]
        [SerializeField] private Transform weaponMount;
        [Tooltip("Điểm đầu nòng phóng chuối (MuzzlePoint). Tự động tìm child trong launcher nếu để trống.")]
        [SerializeField] private Transform muzzlePoint;
        [Tooltip("GameObject hiển thị của launcher (nếu đã đặt sẵn trong hierarchy thay vì instantiate từ prefab).")]
        [SerializeField] private GameObject launcherInstance;

        [Header("Projectile & Ground Trap Prefabs")]
        [Tooltip("Prefab của viên đạn chuối đang bay (PoseKartBananaProjectile). Nếu để trống sẽ tự tạo đạn mang visual chuối.")]
        [SerializeField] private GameObject bananaProjectilePrefab;
        [Tooltip("Prefab bẫy chuối trên mặt đường sau khi tiếp đất (KartBananaTrap).")]
        [SerializeField] private GameObject bananaTrapPrefab;
        [Tooltip("Prefab hiệu ứng nổ nhỏ / bụi đất khi chuối tiếp đất.")]
        [SerializeField] private GameObject landingVfxPrefab;

        [Header("VFX & Audio Feedback")]
        [Tooltip("Hệ thống hạt khói/lửa lóe sáng ở đầu nòng khi bắn mỗi quả.")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [Tooltip("AudioSource chuyên dụng phát tiếng bắn trên bệ phóng (tùy chọn).")]
        [SerializeField] private AudioSource launchAudioSource;
        [Tooltip("Âm thanh khi triển khai mọc tháp phóng chuối.")]
        [SerializeField] private AudioClip deployClip;
        [Tooltip("Âm thanh khi khai hỏa từng quả chuối.")]
        [SerializeField] private AudioClip fireClip;

        [Header("Launch Ballistics & Formation")]
        [Tooltip("Hướng bắn chuối: Forward (bắn về phía trước) hoặc Rearward (bắn lùi về phía sau xe).")]
        [SerializeField] private BananaLaunchDirection launchDirection = BananaLaunchDirection.Forward;
        [Tooltip("Tốc độ phóng cơ bản của quả chuối (m/s).")]
        [SerializeField, Min(5f)] private float launchBaseSpeed = 16f;
        [Tooltip("Góc ngẩng nòng phóng hướng lên trên (độ) để tạo đường bay cong parabolic.")]
        [SerializeField, Range(5f, 50f)] private float launchPitchAngle = 22f;
        [Tooltip("Góc tản trái/phải cho 3 quả (độ). Tạo đội hình tam giác.")]
        [SerializeField, Range(2f, 35f)] private float spreadAngle = 12f;
        [Tooltip("Khoảng cách thời gian giữa 3 lần phóng (giây).")]
        [SerializeField, Range(0.04f, 0.4f)] private float fireInterval = 0.12f;
        [Tooltip("Tỉ lệ vận tốc của xe truyền vào đạn chuối đang bay.")]
        [SerializeField, Range(0f, 1f)] private float vehicleVelocityInheritance = 0.85f;
        [Tooltip("Độ nảy giật lùi (recoil) của thân súng khi bắn mỗi quả chuối (mét).")]
        [SerializeField, Range(0f, 0.4f)] private float recoilKickDistance = 0.14f;

        [Header("Animation Timings")]
        [Tooltip("Thời gian mọc tháp súng khi nhặt được item chuối (giây).")]
        [SerializeField, Range(0.1f, 1f)] private float deployDuration = 0.32f;
        [Tooltip("Thời gian thu tháp súng sau khi bắn xong (giây).")]
        [SerializeField, Range(0.1f, 1f)] private float retractDuration = 0.28f;
        [Tooltip("Thời gian chờ sau phát bắn cuối trước khi bắt đầu thu tháp súng (giây).")]
        [SerializeField, Range(0f, 0.8f)] private float postFireRetractDelay = 0.18f;

        [Header("Runtime State")]
        [SerializeField] private BananaLauncherState currentState = BananaLauncherState.Hidden;

        private CarController car;
        private KartItemInventory inventory;
        private Vector3 initialLocalScale = Vector3.one;
        private Vector3 originalLocalPos = Vector3.zero;
        private Coroutine activeAnimCoroutine;

        public BananaLauncherState CurrentState => currentState;
        public bool IsFiring => currentState == BananaLauncherState.Firing;
        public bool IsEquipped => currentState == BananaLauncherState.Equipped || currentState == BananaLauncherState.Deploying;
        public BananaLaunchDirection Direction
        {
            get => launchDirection;
            set => launchDirection = value;
        }

        public GameObject LauncherPrefab
        {
            get => launcherPrefab;
            set => launcherPrefab = value;
        }

        public Transform WeaponMount
        {
            get => weaponMount;
            set => weaponMount = value;
        }

        public Transform MuzzlePoint
        {
            get => muzzlePoint;
            set => muzzlePoint = value;
        }

        public GameObject BananaTrapPrefab
        {
            get => bananaTrapPrefab;
            set => bananaTrapPrefab = value;
        }

        private void Awake()
        {
            car = GetComponent<CarController>();
            inventory = GetComponent<KartItemInventory>();

            SetupHierarchy();

            if (inventory != null)
            {
                inventory.ItemCollected += OnItemCollected;
                inventory.ItemUsed += OnItemUsed;
                inventory.ItemExpired += OnItemExpired;
            }
        }

        private void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.ItemCollected -= OnItemCollected;
                inventory.ItemUsed -= OnItemUsed;
                inventory.ItemExpired -= OnItemExpired;
            }
        }

        private void Start()
        {
            // Initial hide state
            if (launcherInstance != null)
            {
                launcherInstance.transform.localScale = Vector3.zero;
                launcherInstance.SetActive(false);
            }
            currentState = BananaLauncherState.Hidden;
        }

        public void SetupHierarchy()
        {
            // 1. Ensure Weapon Mount
            if (weaponMount == null)
            {
                Transform existingMount = transform.Find("BananaWeaponMount");
                if (existingMount != null)
                {
                    weaponMount = existingMount;
                }
                else
                {
                    GameObject mountObj = new GameObject("BananaWeaponMount");
                    mountObj.transform.SetParent(transform, false);
                    mountObj.transform.localPosition = new Vector3(0f, 0.95f, -0.45f);
                    mountObj.transform.localRotation = Quaternion.identity;
                    weaponMount = mountObj.transform;
                }
            }

            // 2. Ensure Launcher Instance
            if (launcherInstance == null)
            {
                // Check if already a child under mount
                if (weaponMount.childCount > 0)
                {
                    Transform firstChild = weaponMount.GetChild(0);
                    launcherInstance = firstChild.gameObject;
                }
                else if (launcherPrefab != null)
                {
                    launcherInstance = Instantiate(launcherPrefab, weaponMount);
                    launcherInstance.name = "BananaTripleLauncher";
                    launcherInstance.transform.localPosition = Vector3.zero;
                    launcherInstance.transform.localRotation = Quaternion.identity;
                }
            }

            // 3. Cache base transform values
            if (launcherInstance != null)
            {
                if (launcherInstance.transform.localScale.sqrMagnitude > 0.001f)
                    initialLocalScale = launcherInstance.transform.localScale;
                else
                    initialLocalScale = Vector3.one;

                originalLocalPos = launcherInstance.transform.localPosition;

                // 4. Resolve MuzzlePoint
                if (muzzlePoint == null)
                {
                    Transform foundMuzzle = FindMuzzleRecursive(launcherInstance.transform);
                    if (foundMuzzle != null)
                    {
                        muzzlePoint = foundMuzzle;
                    }
                    else
                    {
                        GameObject muzzleObj = new GameObject("MuzzlePoint");
                        muzzleObj.transform.SetParent(launcherInstance.transform, false);
                        muzzleObj.transform.localPosition = new Vector3(0f, 0.3f, 1.2f);
                        muzzlePoint = muzzleObj.transform;
                    }
                }

                // 5. Resolve MuzzleFlash particle system
                if (muzzleFlash == null && muzzlePoint != null)
                {
                    muzzleFlash = muzzlePoint.GetComponentInChildren<ParticleSystem>();
                }

                // 6. Resolve AudioSource
                if (launchAudioSource == null)
                {
                    launchAudioSource = launcherInstance.GetComponent<AudioSource>();
                }
            }

            // Fallback for bananaTrapPrefab if not set
            if (bananaTrapPrefab == null)
            {
                if (inventory != null && inventory.BananaPrefab != null)
                    bananaTrapPrefab = inventory.BananaPrefab;
                else
                    bananaTrapPrefab = KartItemInventory.SharedBananaPrefab;
            }
        }

        private Transform FindMuzzleRecursive(Transform root)
        {
            if (root == null) return null;
            string n = root.name.ToLowerInvariant();
            if (n.Contains("muzzle") || n.Contains("mozzle") || n.Contains("firepoint"))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform res = FindMuzzleRecursive(root.GetChild(i));
                if (res != null) return res;
            }
            return null;
        }

        private void OnItemCollected(KartItemPickup.ItemType type)
        {
            if (type == KartItemPickup.ItemType.BananaTrap)
            {
                DeployLauncher();
            }
            else
            {
                if (currentState != BananaLauncherState.Hidden && currentState != BananaLauncherState.Firing)
                {
                    RetractLauncher();
                }
            }
        }

        private void OnItemUsed(KartItemPickup.ItemType type)
        {
            if (type != KartItemPickup.ItemType.BananaTrap && currentState != BananaLauncherState.Hidden && currentState != BananaLauncherState.Firing)
            {
                RetractLauncher();
            }
        }

        private void OnItemExpired(KartItemPickup.ItemType type)
        {
            if (currentState != BananaLauncherState.Hidden && currentState != BananaLauncherState.Firing)
            {
                RetractLauncher();
            }
        }

        /// <summary>
        /// Deploy the launcher with smooth scale-up animation.
        /// </summary>
        public void DeployLauncher()
        {
            if (launcherInstance == null)
                SetupHierarchy();

            if (launcherInstance == null) return;

            if (activeAnimCoroutine != null)
                StopCoroutine(activeAnimCoroutine);

            activeAnimCoroutine = StartCoroutine(DeployCoroutine());
        }

        private IEnumerator DeployCoroutine()
        {
            currentState = BananaLauncherState.Deploying;
            launcherInstance.SetActive(true);
            launcherInstance.transform.localPosition = originalLocalPos;

            // SFX
            PlayDeploySound();

            float elapsed = 0f;
            while (elapsed < deployDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / deployDuration);
                // Elastic bounce ease-out
                float bounce = 1f + Mathf.Sin(t * Mathf.PI) * 0.18f;
                launcherInstance.transform.localScale = Vector3.LerpUnclamped(Vector3.zero, initialLocalScale * bounce, t);
                yield return null;
            }

            launcherInstance.transform.localScale = initialLocalScale;
            currentState = BananaLauncherState.Equipped;
            activeAnimCoroutine = null;
        }

        /// <summary>
        /// Retract the launcher with smooth scale-down animation.
        /// </summary>
        public void RetractLauncher()
        {
            if (launcherInstance == null || !launcherInstance.activeSelf)
            {
                currentState = BananaLauncherState.Hidden;
                return;
            }

            if (activeAnimCoroutine != null)
                StopCoroutine(activeAnimCoroutine);

            activeAnimCoroutine = StartCoroutine(RetractCoroutine());
        }

        private IEnumerator RetractCoroutine()
        {
            currentState = BananaLauncherState.Retracting;

            float elapsed = 0f;
            Vector3 startScale = launcherInstance.transform.localScale;
            while (elapsed < retractDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / retractDuration);
                float easeIn = t * t;
                launcherInstance.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, easeIn);
                yield return null;
            }

            launcherInstance.transform.localScale = Vector3.zero;
            launcherInstance.SetActive(false);
            currentState = BananaLauncherState.Hidden;
            activeAnimCoroutine = null;
        }

        /// <summary>
        /// Invoked when the player or AI racer activates the Banana Trap item.
        /// Initiates the sequential 3-banana ballistic launch.
        /// Returns true to immediately consume the item from inventory.
        /// </summary>
        public bool TryFire(KartItemInventory settings)
        {
            if (currentState == BananaLauncherState.Firing)
                return false;

            if (launcherInstance == null)
                SetupHierarchy();

            if (activeAnimCoroutine != null)
                StopCoroutine(activeAnimCoroutine);

            StartCoroutine(FireTripleSequence(settings));
            return true;
        }

        private IEnumerator FireTripleSequence(KartItemInventory settings)
        {
            currentState = BananaLauncherState.Firing;

            if (launcherInstance != null && !launcherInstance.activeSelf)
            {
                launcherInstance.SetActive(true);
                launcherInstance.transform.localScale = initialLocalScale;
            }

            // Setup shot parameters for Triangle Spread formation:
            // Shot 0: Left (-spreadAngle), moderate speed
            // Shot 1: Center (0°), higher speed (travels farther ahead/back)
            // Shot 2: Right (+spreadAngle), moderate speed
            float[] angleOffsets = new float[] { -spreadAngle, 0f, spreadAngle };
            float[] speedMultipliers = new float[] { 0.94f, 1.16f, 0.94f };

            // Determine base fire direction
            Vector3 baseDir;
            if (launchDirection == BananaLaunchDirection.Forward)
                baseDir = car != null ? car.transform.forward : transform.forward;
            else
                baseDir = car != null ? -car.transform.forward : -transform.forward;

            Vector3 upAxis = car != null ? car.transform.up : Vector3.up;

            for (int i = 0; i < 3; i++)
            {
                // Calculate shot trajectory with spread and pitch
                Quaternion spreadRot = Quaternion.AngleAxis(angleOffsets[i], upAxis);
                Vector3 horizontalDir = spreadRot * baseDir;

                Vector3 pitchAxis = Vector3.Cross(upAxis, horizontalDir).normalized;
                Quaternion pitchRot = Quaternion.AngleAxis(launchPitchAngle, pitchAxis);
                Vector3 launchDirVector = pitchRot * horizontalDir;

                float speed = launchBaseSpeed * speedMultipliers[i];
                Vector3 launchVel = launchDirVector * speed;

                if (car != null && car.TargetBody != null)
                {
                    Vector3 carVel = car.TargetBody.linearVelocity;
                    launchVel += carVel * vehicleVelocityInheritance;
                }

                Vector3 spawnPos =
    muzzlePoint != null
        ? muzzlePoint.position
        : (weaponMount != null
            ? weaponMount.position
            : transform.position);

                // =====================================================
                // GAMEPLAY PROJECTILE - SERVER
                // =====================================================
                SpawnBananaProjectile(
                    spawnPos,
                    launchVel,
                    settings
                );

                // =====================================================
                // ONLINE VISUAL SYNC
                // Cho Client thấy viên chuối bay khỏi nòng.
                // Gameplay vẫn chỉ chạy trên Server.
                // =====================================================
                if (PoseKartNetworkItemBridge.IsOnlineRace &&
                    Unity.Netcode.NetworkManager.Singleton != null &&
                    Unity.Netcode.NetworkManager.Singleton.IsServer)
                {
                    PoseKartNetworkItemBridge bridge =
                        GetComponent<PoseKartNetworkItemBridge>();

                    if (bridge != null)
                    {
                        bridge.ServerSyncBananaShotVisual(
                            spawnPos,
                            launchVel
                        );
                    }
                }

                // Host/offline local visual
                if (muzzleFlash != null)
                {
                    muzzleFlash.Play();
                }

                PlayFireSound(spawnPos);

                // Visual Recoil
                StartCoroutine(ApplyRecoilKick());

                if (i < 2)
                {
                    yield return new WaitForSeconds(fireInterval);
                }
            }

            // Wait before retracting
            // Wait before retracting
            yield return new WaitForSeconds(postFireRetractDelay);

            // Online:
            // báo cho các Client thu turret cosmetic của racer này.
            if (PoseKartNetworkItemBridge.IsOnlineRace &&
                Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                PoseKartNetworkItemBridge bridge =
                    GetComponent<PoseKartNetworkItemBridge>();

                if (bridge != null)
                {
                    bridge.ServerSyncBananaLauncherRetract();
                }
            }

            // Server / Offline tự thu turret như cũ.
            yield return RetractCoroutine();
        }
        public void PlayNetworkShotVisual(
    Vector3 serverSpawnPosition,
    Vector3 velocity)
        {
            Vector3 visualSpawnPosition =
                muzzlePoint != null
                    ? muzzlePoint.position
                    : serverSpawnPosition;

            // Muzzle flash
            if (muzzleFlash != null)
            {
                muzzleFlash.Play();
            }

            // Sound
            PlayFireSound(visualSpawnPosition);

            // =====================================================
            // Chọn prefab visual.
            // Projectile prefab có thì dùng.
            // Không có thì dùng Banana Trap prefab làm hình quả chuối bay.
            // =====================================================
            GameObject visualPrefab = bananaProjectilePrefab;

            if (visualPrefab == null)
            {
                visualPrefab = bananaTrapPrefab;
            }

            if (visualPrefab == null &&
                inventory != null &&
                inventory.BananaPrefab != null)
            {
                visualPrefab = inventory.BananaPrefab;
            }

            if (visualPrefab == null)
            {
                visualPrefab = KartItemInventory.SharedBananaPrefab;
            }

            if (visualPrefab == null)
            {
                Debug.LogWarning(
                    "[BananaLauncher] No visual prefab available for network shot.",
                    this
                );

                return;
            }

            Quaternion rotation =
                velocity.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(velocity.normalized)
                    : transform.rotation;

            GameObject visual =
                Instantiate(
                    visualPrefab,
                    visualSpawnPosition,
                    rotation
                );

            visual.name = "BananaTriple_VisualOnly";

            // =====================================================
            // TẮT TOÀN BỘ GAMEPLAY.
            // Đây chỉ là hình ảnh local.
            // =====================================================

            PoseKartBananaProjectile projectile =
                visual.GetComponent<PoseKartBananaProjectile>();

            if (projectile != null)
            {
                projectile.enabled = false;
            }

            KartBananaTrap trap =
                visual.GetComponent<KartBananaTrap>();

            if (trap != null)
            {
                trap.enabled = false;
            }

            Collider[] colliders =
                visual.GetComponentsInChildren<Collider>(true);

            foreach (Collider col in colliders)
            {
                if (col != null)
                    col.enabled = false;
            }

            Unity.Netcode.NetworkBehaviour[] netBehaviours =
                visual.GetComponentsInChildren<
                    Unity.Netcode.NetworkBehaviour
                >(true);

            foreach (Unity.Netcode.NetworkBehaviour behaviour
                     in netBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = false;
            }

            // Rigidbody cosmetic
            Rigidbody body =
                visual.GetComponent<Rigidbody>();

            if (body == null)
            {
                body = visual.AddComponent<Rigidbody>();
            }

            body.isKinematic = false;
            body.useGravity = true;

            body.linearVelocity = velocity;

            // Xoay quả chuối khi bay cho dễ nhìn
            body.angularVelocity =
                new Vector3(5f, 8f, 4f);

            // Chỉ là projectile visual.
            // Trap thật sẽ được Server spawn sau khi gameplay projectile đáp đất.
            Destroy(visual, 2.5f);
        }
        private void SpawnBananaProjectile(Vector3 spawnPosition, Vector3 velocity, KartItemInventory settings)
        {
            GameObject projObj;
            if (bananaProjectilePrefab != null)
            {
                projObj = Instantiate(bananaProjectilePrefab, spawnPosition, Quaternion.LookRotation(velocity.normalized));
            }
            else
            {
                projObj = new GameObject("BananaTriple_Projectile");
                projObj.transform.position = spawnPosition;
                projObj.transform.rotation = Quaternion.LookRotation(velocity.normalized);
            }

            PoseKartBananaProjectile projectile = projObj.GetComponent<PoseKartBananaProjectile>();
            if (projectile == null)
                projectile = projObj.AddComponent<PoseKartBananaProjectile>();

            float lifetime = settings != null ? settings.BananaLifetime : 12f;
            float slipDuration = settings != null ? settings.BananaSlipDuration : 1f;
            float immunity = settings != null ? settings.BananaOwnerImmunity : 0.65f;
            LayerMask groundLayers = settings != null ? settings.BananaGroundLayers : (LayerMask)(~0);

            GameObject trapPrefabToUse = bananaTrapPrefab != null
                ? bananaTrapPrefab
                : (settings != null && settings.BananaPrefab != null ? settings.BananaPrefab : KartItemInventory.SharedBananaPrefab);
            // Banana projectile phải là Rigidbody động để linearVelocity hoạt động.
            // Nếu prefab đang để Kinematic thì Unity sẽ không cho nó bay.
            Rigidbody projectileBody = projObj.GetComponent<Rigidbody>();

            if (projectileBody == null)
            {
                projectileBody = projObj.AddComponent<Rigidbody>();
            }

            projectileBody.isKinematic = false;
            projectileBody.useGravity = true;
            projectileBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;

            // Đảm bảo không mang velocity cũ từ prefab/pooling.
            projectileBody.linearVelocity = Vector3.zero;
            projectileBody.angularVelocity = Vector3.zero;
            projectile.Initialize(
                car,
                velocity,
                trapPrefabToUse,
                lifetime,
                slipDuration,
                immunity,
                groundLayers,
                landingVfxPrefab);
        }

        private IEnumerator ApplyRecoilKick()
        {
            if (launcherInstance == null) yield break;

            Vector3 kickDir = launchDirection == BananaLaunchDirection.Forward ? -Vector3.forward : Vector3.forward;
            Vector3 targetKickPos = originalLocalPos + kickDir * recoilKickDistance;

            float kickTime = 0.05f;
            float elapsed = 0f;
            while (elapsed < kickTime)
            {
                elapsed += Time.deltaTime;
                launcherInstance.transform.localPosition = Vector3.Lerp(originalLocalPos, targetKickPos, elapsed / kickTime);
                yield return null;
            }

            float returnTime = 0.09f;
            elapsed = 0f;
            while (elapsed < returnTime)
            {
                elapsed += Time.deltaTime;
                launcherInstance.transform.localPosition = Vector3.Lerp(targetKickPos, originalLocalPos, elapsed / returnTime);
                yield return null;
            }

            launcherInstance.transform.localPosition = originalLocalPos;
        }

        private void PlayDeploySound()
        {
            if (launchAudioSource != null && deployClip != null)
            {
                launchAudioSource.PlayOneShot(deployClip);
            }
            else
            {
                AudioManager.PlayLauncherDeploy(transform.position);
            }
        }

        private void PlayFireSound(Vector3 position)
        {
            if (launchAudioSource != null && fireClip != null)
            {
                launchAudioSource.PlayOneShot(fireClip);
            }
            else
            {
                AudioManager.PlayLauncherFire(position);
            }
        }
    }
}
