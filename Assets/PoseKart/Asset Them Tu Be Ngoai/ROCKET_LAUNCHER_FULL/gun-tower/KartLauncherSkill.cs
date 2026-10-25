using System.Collections;
using PoseKart.Audio;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart
{
    /// <summary>
    /// Tháp súng / Mortar Launcher gắn trên xe:
    /// 1. Tự động mọc lên khi nhặt Homing Missile hoặc Ink Trap / Ink Shot.
    /// 2. Hỗ trợ bệ phóng riêng biệt cho Ink Trap (inkLauncherPrefab / inkLauncherModel) với nòng MozzlePoint riêng.
    /// 3. Tự động xoay nòng bám đuổi xe đối thủ phía trước.
    /// 4. Bấm SPACE để khai hỏa tên lửa hoặc đạn mực ra khỏi đúng nòng MozzlePoint.
    /// 5. Thu tháp súng vào trong nóc xe mượt mà, dọn slot item và kết thúc skill.
    /// </summary>
    [DisallowMultipleComponent]
    public class KartLauncherSkill : MonoBehaviour
    {
        public enum SkillState { Ready, Deploying, LockingOn, WaitingForFire, Retracting }

        [Header("Missile Launcher (Homing Missile)")]
        [Tooltip("Prefab/GameObject tháp súng phóng tên lửa (tower) đặt trên xe.")]
        [SerializeField] private GameObject launcherModel;
        [Tooltip("Điểm đầu nòng phóng tên lửa (Muzzle_Point).")]
        [SerializeField] private Transform muzzlePoint;
        [Tooltip("Prefab tên lửa (OFFICAL_ROCKET hoặc lấy từ KartItemInventory).")]
        [SerializeField] private GameObject missilePrefab;
        [Tooltip("Tỉ lệ kích thước khi bung lên của Missile Launcher (mặc định 0.7, 0.7, 0.7).")]
        [SerializeField] private Vector3 targetScale = new Vector3(0.7f, 0.7f, 0.7f);

        [Header("Ink Trap / Ink Shot Launcher (Riêng biệt)")]
        [Tooltip("Prefab bệ phóng mực / ink trap launcher riêng biệt (kéo prefab vào đây nếu muốn tự sinh lúc chạy).")]
        [SerializeField] private GameObject inkLauncherPrefab;
        [Tooltip("GameObject bệ phóng Ink Trap đặt sẵn trên xe (nếu có).")]
        [SerializeField] private GameObject inkLauncherModel;
        [Tooltip("Điểm đầu nòng MozzlePoint / MuzzlePoint của bệ phóng mực. Tự động tìm child tên 'MozzlePoint' / 'Muzzle_Point' nếu để trống.")]
        [SerializeField] private Transform inkMuzzlePoint;
        [Tooltip("Prefab đạn mực / cannon ball đen (hoặc lấy từ KartItemInventory / Resources).")]
        [SerializeField] private GameObject inkPrefab;
        [Tooltip("Tỉ lệ kích thước khi bung lên của Ink Launcher (mặc định 1.6, 1.6, 1.6 tương ứng với model Zenithe).")]
        [SerializeField] private Vector3 inkTargetScale = new Vector3(1.6f, 1.6f, 1.6f);

        [Header("Settings")]
        [SerializeField] private float deployDuration = 0.35f;   // Thời gian mọc/thu launcher (giây)
        [SerializeField] private float autoFireTimeout = 3.5f;   // Tự bắn nếu không bấm Space sau 3.5s

        [Header("Debug / Testing")]
        [Tooltip("Phím F đã bị tắt vĩnh viễn theo yêu cầu.")]
#pragma warning disable 0414
        [SerializeField] private bool enableDebugTestKey = false;
        [SerializeField] private KeyCode testKey = KeyCode.None;
#pragma warning restore 0414

        [Header("State (Read Only)")]
        [SerializeField] private SkillState currentState = SkillState.Ready;

        private CarController car;
        private Rigidbody rb;
        private KartItemInventory inventory;
        private CarController currentTargetCar;
        private KartItemPickup.ItemType activeItemType = KartItemPickup.ItemType.HomingMissile;
        private Coroutine skillCoroutine;

        // Lưu góc quay ban đầu của Missile & Ink Launcher
        private Quaternion initialLocalRotation = Quaternion.identity;
        private Quaternion inkInitialLocalRotation = Quaternion.identity;

        public bool IsActive => currentState != SkillState.Ready;
        public GameObject LauncherModel => launcherModel;
        public GameObject InkLauncherModel => inkLauncherModel;
        public GameObject InkLauncherPrefab => inkLauncherPrefab;
        public Transform MuzzlePoint => muzzlePoint;
        public Transform InkMuzzlePoint => inkMuzzlePoint;
        public Transform CurrentMuzzlePoint => GetActiveMuzzlePoint();
        public Vector3 InkTargetScale { get => inkTargetScale; set => inkTargetScale = value; }
        public Vector3 TargetScale { get => targetScale; set => targetScale = value; }

        private void OnValidate()
        {
            if (inkLauncherModel != null && inkLauncherModel.transform.localScale.sqrMagnitude > 0.001f)
            {
                inkTargetScale = inkLauncherModel.transform.localScale;
            }
            if (launcherModel != null && launcherModel.transform.localScale.sqrMagnitude > 0.001f)
            {
                targetScale = launcherModel.transform.localScale;
            }
        }

        private void Awake()
        {
            ResolveReferences();

            // Nếu script này gắn trên child (ví dụ LauncherPivot) mà xe đã có KartLauncherSkill,
            // chuyển các tham chiếu sang cho xe rồi tự dọn dẹp để tránh chạy trùng 2 script.
            if (car != null && car.gameObject != this.gameObject)
            {
                KartLauncherSkill parentSkill = car.GetComponent<KartLauncherSkill>();
                if (parentSkill != null)
                {
                    if (parentSkill.launcherModel == null || !parentSkill.launcherModel.scene.IsValid())
                    {
                        parentSkill.launcherModel = this.launcherModel;
                        parentSkill.muzzlePoint = this.muzzlePoint;
                        if (parentSkill.missilePrefab == null)
                            parentSkill.missilePrefab = this.missilePrefab;
                    }
                    if (parentSkill.inkLauncherModel == null || !parentSkill.inkLauncherModel.scene.IsValid())
                    {
                        parentSkill.inkLauncherModel = this.inkLauncherModel;
                        parentSkill.inkLauncherPrefab = this.inkLauncherPrefab;
                        parentSkill.inkMuzzlePoint = this.inkMuzzlePoint;
                        if (parentSkill.inkPrefab == null)
                            parentSkill.inkPrefab = this.inkPrefab;
                    }
                    Destroy(this);
                    return;
                }
            }

            // Ghi nhận Scale & Rotation của Missile Launcher
            if (launcherModel != null)
            {
                if (launcherModel.transform.localScale.sqrMagnitude > 0.001f)
                {
                    targetScale = launcherModel.transform.localScale;
                }
                initialLocalRotation = launcherModel.transform.localRotation;

                launcherModel.transform.localScale = Vector3.zero;
                launcherModel.SetActive(false);
            }

            // Ghi nhận Scale & Rotation của Ink Launcher
            if (inkLauncherModel != null)
            {
                if (inkLauncherModel.transform.localScale.sqrMagnitude > 0.001f)
                {
                    inkTargetScale = inkLauncherModel.transform.localScale;
                }
                else if (inkTargetScale.sqrMagnitude <= 0.001f)
                {
                    inkTargetScale = new Vector3(1.6f, 1.6f, 1.6f);
                }
                inkInitialLocalRotation = inkLauncherModel.transform.localRotation;

                inkLauncherModel.transform.localScale = Vector3.zero;
                inkLauncherModel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeInventoryEvents();
        }

        private void OnDisable()
        {
            UnsubscribeInventoryEvents();
            ResetSkillState();
        }

        public void ResolveReferences()
        {
            if (car == null)
                car = GetComponent<CarController>() ?? GetComponentInParent<CarController>() ?? GetComponentInChildren<CarController>();
            if (rb == null)
                rb = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>();
            if (inventory == null)
                inventory = GetComponent<KartItemInventory>() ?? GetComponentInParent<KartItemInventory>() ?? GetComponentInChildren<KartItemInventory>();

            // 1. Tự động định vị tháp súng tên lửa (tower) nếu chưa gán hoặc bị mất liên kết
            if (launcherModel == null || !launcherModel.scene.IsValid())
            {
                Transform found = transform.Find("tower") ?? transform.Find("Tower");
                if (found == null)
                    found = transform.Find("LauncherPivot/tower") ?? transform.Find("LauncherPivot/Tower");
                if (found == null && car != null)
                    found = car.transform.Find("LauncherPivot/tower") ?? car.transform.Find("LauncherPivot/Tower") ?? car.transform.Find("tower");

                if (found != null)
                {
                    launcherModel = found.gameObject;
                }
            }

            // 2. Tự động định vị đầu nòng Muzzle_Point cho tên lửa
            if (launcherModel != null)
            {
                if (muzzlePoint == null)
                {
                    muzzlePoint = launcherModel.transform.Find("Muzzle_Point")
                               ?? launcherModel.transform.Find("MuzzlePoint")
                               ?? launcherModel.transform.Find("muzzle");
                }

                if (launcherModel.transform.localScale.sqrMagnitude > 0.001f)
                {
                    targetScale = launcherModel.transform.localScale;
                }
                initialLocalRotation = launcherModel.transform.localRotation;
            }

            // 3. Tự động định vị bệ phóng Ink Launcher riêng nếu chưa gán
            if (inkLauncherModel == null || !inkLauncherModel.scene.IsValid())
            {
                Transform foundInk = transform.Find("InkLauncher") ?? transform.Find("ink_launcher") ??
                                     transform.Find("InkTrapLauncher") ?? transform.Find("ink_trap") ??
                                     transform.Find("InkTrap") ?? transform.Find("Ink_Trap") ??
                                     transform.Find("LauncherPivot/InkLauncher") ?? transform.Find("LauncherPivot/ink_launcher") ??
                                     transform.Find("LauncherPivot/InkTrapLauncher");
                if (foundInk == null && car != null)
                {
                    foundInk = car.transform.Find("InkLauncher") ?? car.transform.Find("InkTrapLauncher") ??
                               car.transform.Find("LauncherPivot/InkLauncher") ?? car.transform.Find("LauncherPivot/InkTrapLauncher");
                }
                if (foundInk != null)
                {
                    inkLauncherModel = foundInk.gameObject;
                }
            }

            // 4. Nếu có Prefab Ink Launcher mà chưa sinh Instance thì tự động sinh
            if (inkLauncherModel == null && inkLauncherPrefab != null)
            {
                Transform pivot = transform.Find("LauncherPivot") ?? (car != null ? car.transform.Find("LauncherPivot") : null) ?? transform;
                GameObject instantiated = Instantiate(inkLauncherPrefab, pivot);
                instantiated.name = inkLauncherPrefab.name;
                instantiated.transform.localPosition = Vector3.zero;
                instantiated.transform.localRotation = Quaternion.identity;
                inkLauncherModel = instantiated;
            }

            // 5. Tự động định vị nòng MozzlePoint / MuzzlePoint cho Ink Launcher
            if (inkLauncherModel != null)
            {
                if (inkMuzzlePoint == null)
                {
                    inkMuzzlePoint = FindMozzlePoint(inkLauncherModel.transform);
                }

                if (inkLauncherModel.transform.localScale.sqrMagnitude > 0.001f)
                {
                    inkTargetScale = inkLauncherModel.transform.localScale;
                }
                else if (inkTargetScale.sqrMagnitude <= 0.001f)
                {
                    inkTargetScale = new Vector3(1.6f, 1.6f, 1.6f);
                }
                inkInitialLocalRotation = inkLauncherModel.transform.localRotation;
            }
            else if (inkMuzzlePoint == null)
            {
                // Thử tìm MozzlePoint trên xe trực tiếp nếu user gắn sẵn trên thân xe
                inkMuzzlePoint = FindMozzlePoint(transform);
                if (inkMuzzlePoint == null && car != null)
                {
                    inkMuzzlePoint = FindMozzlePoint(car.transform);
                }
            }

            // 6. Prefabs fallback
            if (missilePrefab == null && inventory != null)
            {
                missilePrefab = inventory.MissilePrefab;
            }
            if (inkPrefab == null && inventory != null)
            {
                inkPrefab = inventory.InkProjectilePrefab;
            }
        }

        public static Transform FindMozzlePoint(Transform parent)
        {
            if (parent == null) return null;

            string[] names = new string[]
            {
                "MozzlePoint", "Mozzle_Point", "mozzlepoint", "mozzle_point", "mozzle", "Mozzle",
                "Muzzle_Point", "MuzzlePoint", "muzzlepoint", "muzzle_point", "muzzle", "Muzzle",
                "FirePoint", "Fire_Point", "ShootPoint", "Shoot_Point", "Nozzle", "nozzle"
            };

            foreach (string n in names)
            {
                Transform found = FindDeepChild(parent, n);
                if (found != null) return found;
            }

            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                string lower = child.name.ToLowerInvariant();
                if (lower.Contains("mozzle") || lower.Contains("muzzle") || lower.Contains("shootpoint") || lower.Contains("firepoint"))
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            Transform result = parent.Find(name);
            if (result != null) return result;
            for (int i = 0; i < parent.childCount; i++)
            {
                result = FindDeepChild(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        public GameObject GetActiveLauncherModel()
        {
            if (activeItemType == KartItemPickup.ItemType.InkShot && inkLauncherModel != null)
                return inkLauncherModel;
            return launcherModel;
        }

        public Transform GetActiveMuzzlePoint()
        {
            if (activeItemType == KartItemPickup.ItemType.InkShot)
            {
                if (inkMuzzlePoint != null) return inkMuzzlePoint;
                if (inkLauncherModel != null)
                {
                    inkMuzzlePoint = FindMozzlePoint(inkLauncherModel.transform);
                    if (inkMuzzlePoint != null) return inkMuzzlePoint;
                }
            }
            return muzzlePoint;
        }

        public Vector3 GetActiveTargetScale()
        {
            if (activeItemType == KartItemPickup.ItemType.InkShot && inkLauncherModel != null)
            {
                if (inkTargetScale.sqrMagnitude > 0.001f)
                    return inkTargetScale;
                return new Vector3(1.6f, 1.6f, 1.6f);
            }
            if (targetScale.sqrMagnitude > 0.001f)
                return targetScale;
            return new Vector3(0.7f, 0.7f, 0.7f);
        }

        public Quaternion GetActiveInitialRotation()
        {
            if (activeItemType == KartItemPickup.ItemType.InkShot && inkLauncherModel != null)
                return inkInitialLocalRotation;
            return initialLocalRotation;
        }

        public void SetInkLauncher(GameObject model, Transform muzzle = null)
        {
            inkLauncherModel = model;
            if (muzzle != null)
                inkMuzzlePoint = muzzle;
            else if (model != null)
                inkMuzzlePoint = FindMozzlePoint(model.transform);

            if (inkLauncherModel != null)
            {
                if (inkLauncherModel.transform.localScale.sqrMagnitude > 0.001f)
                    inkTargetScale = inkLauncherModel.transform.localScale;
                inkInitialLocalRotation = inkLauncherModel.transform.localRotation;
                inkLauncherModel.transform.localScale = Vector3.zero;
                inkLauncherModel.SetActive(false);
            }
        }

        public void SetInkLauncherPrefab(GameObject prefab)
        {
            inkLauncherPrefab = prefab;
            ResolveReferences();
        }

        private void SubscribeInventoryEvents()
        {
            if (inventory != null)
            {
                inventory.ItemCollected -= HandleItemCollected;
                inventory.ItemCollected += HandleItemCollected;
            }
        }

        private void UnsubscribeInventoryEvents()
        {
            if (inventory != null)
            {
                inventory.ItemCollected -= HandleItemCollected;
            }
        }

        private void ResetSkillState()
        {
            if (skillCoroutine != null)
            {
                StopCoroutine(skillCoroutine);
                skillCoroutine = null;
            }

            currentState = SkillState.Ready;
            currentTargetCar = null;

            if (launcherModel != null)
            {
                launcherModel.transform.localRotation = initialLocalRotation;
                launcherModel.transform.localScale = targetScale;
                launcherModel.SetActive(false);
            }

            if (inkLauncherModel != null)
            {
                inkLauncherModel.transform.localRotation = inkInitialLocalRotation;
                inkLauncherModel.transform.localScale = inkTargetScale;
                inkLauncherModel.SetActive(false);
            }
        }

        private void HandleItemCollected(KartItemPickup.ItemType item)
        {
            if (item == KartItemPickup.ItemType.HomingMissile || item == KartItemPickup.ItemType.InkShot)
            {
                activeItemType = item;
                ResolveReferences(); // Đảm bảo tự động tìm thấy Ink Launcher hoặc MozzlePoint nếu vừa được thêm vào
                Debug.Log($"[KartLauncherSkill] Đã nhặt {item}! Bệ phóng {(item == KartItemPickup.ItemType.InkShot ? "Ink Trap" : "Missile")} chuẩn bị vươn lên...");
                ActivateSkill();
            }
        }

        public void ActivateSkill(CarController target = null)
        {
            if (currentState != SkillState.Ready) return;

            currentTargetCar = target;
            skillCoroutine = StartCoroutine(SkillSequenceRoutine());
        }

        private IEnumerator SkillSequenceRoutine()
        {
            GameObject activeModel = GetActiveLauncherModel();
            Vector3 activeTargetScale = GetActiveTargetScale();
            Quaternion activeInitialRot = GetActiveInitialRotation();

            // ----------------------------------------------------
            // 1. MỌC LAUNCHER TỪ THÂN XE (Phóng to mượt mà từ 0 -> targetScale)
            // ----------------------------------------------------
            currentState = SkillState.Deploying;
            PoseKart.Audio.AudioManager.PlayLauncherDeploy(transform.position);

            if (activeModel != null)
            {
                activeModel.transform.localRotation = activeInitialRot;
                activeModel.transform.localScale = Vector3.zero;
                activeModel.SetActive(true);

                float elapsed = 0f;
                while (elapsed < deployDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, elapsed / deployDuration);
                    activeModel.transform.localScale = Vector3.Lerp(Vector3.zero, activeTargetScale, t);
                    yield return null;
                }
                activeModel.transform.localScale = activeTargetScale;
            }

            // ----------------------------------------------------
            // 2. QUÉT TÌM MỤC TIÊU PHÍA TRƯỚC
            // ----------------------------------------------------
            currentState = SkillState.LockingOn;
            if (currentTargetCar == null)
            {
                currentTargetCar = FindClosestOpponentAhead();
            }

            // ----------------------------------------------------
            // 3. CHỜ BẤM PHÍM SPACE ĐỂ BẮN (XOAY NÒNG BÁM MỤC TIÊU TỰ ĐỘNG)
            // ----------------------------------------------------
            currentState = SkillState.WaitingForFire;
            float waitTimer = 0f;
            bool fired = false;

            Transform vehicleTransform = car != null ? car.transform : transform;
            bool onlineRace = PoseKartNetworkItemBridge.IsOnlineRace;

            PoseKartNetworkItemBridge networkBridge = null;
            Unity.Netcode.NetworkObject networkObject = null;
            bool isLocalHumanOwner = false;

            if (onlineRace && car != null)
            {
                networkBridge = car.GetComponent<PoseKartNetworkItemBridge>();
                networkObject = car.GetComponent<Unity.Netcode.NetworkObject>();

                isLocalHumanOwner =
                    networkBridge != null &&
                    networkObject != null &&
                    networkObject.IsPlayerObject &&
                    networkObject.IsOwner;
            }
            while (!fired && waitTimer < autoFireTimeout)
            {
                waitTimer += Time.deltaTime;

                // Nếu target bị mất/destroy giữa chừng thì tìm target mới
                if (currentTargetCar == null || !currentTargetCar.gameObject.activeInHierarchy)
                {
                    currentTargetCar = FindClosestOpponentAhead();
                }

                // Xoay tháp súng theo hướng xe địch
                if (activeModel != null)
                {
                    if (currentTargetCar != null)
                    {
                        Vector3 toTarget = currentTargetCar.transform.position - activeModel.transform.position;
                        Vector3 flatDir = Vector3.ProjectOnPlane(toTarget, vehicleTransform.up);
                        if (flatDir.sqrMagnitude > 0.01f)
                        {
                            Transform activeMuzzle = GetActiveMuzzlePoint();

                            Vector3 barrelDir;

                            if (activeMuzzle != null)
                            {
                                // Hướng thực tế từ tâm turret tới đầu nòng.
                                barrelDir =
                                    activeMuzzle.position -
                                    activeModel.transform.position;

                                barrelDir =
                                    Vector3.ProjectOnPlane(
                                        barrelDir,
                                        vehicleTransform.up
                                    );
                            }
                            else
                            {
                                barrelDir = activeModel.transform.forward;
                            }

                            if (barrelDir.sqrMagnitude > 0.001f)
                            {
                                Quaternion correction =
                                    Quaternion.FromToRotation(
                                        barrelDir.normalized,
                                        flatDir.normalized
                                    );

                                Quaternion targetWorldRot =
                                    correction *
                                    activeModel.transform.rotation;

                                activeModel.transform.rotation =
                                    Quaternion.Slerp(
                                        activeModel.transform.rotation,
                                        targetWorldRot,
                                        Time.deltaTime * 12f
                                    );
                            }
                        }
                    }
                    else
                    {
                        // Khi chưa có mục tiêu, nòng súng quay đều hướng về phía trước xe
                        Quaternion defaultWorldRot = vehicleTransform.rotation * activeInitialRot;
                        activeModel.transform.rotation = Quaternion.Slerp(
                            activeModel.transform.rotation,
                            defaultWorldRot,
                            Time.deltaTime * 8f
                        );
                    }
                }

                bool spacePressed = false;

                // Offline: giữ nguyên input cũ.
                // Online: chỉ local HUMAN owner mới được đọc Space.
                // Remote player và AI tuyệt đối không phản ứng với bàn phím máy này.
                if (!onlineRace || isLocalHumanOwner)
                {
                    if (Keyboard.current != null &&
                        Keyboard.current.spaceKey.wasPressedThisFrame)
                    {
                        spacePressed = true;
                    }

#if ENABLE_LEGACY_INPUT_MANAGER
                    if (!spacePressed && Input.GetKeyDown(KeyCode.Space))
                        spacePressed = true;
#endif

                    if (!spacePressed && UnityEngine.InputSystem.Gamepad.current != null &&
                        UnityEngine.InputSystem.Gamepad.current.buttonWest.wasPressedThisFrame)
                    {
                        spacePressed = true;
                    }
                }
                if (spacePressed)
                {
                    fired = true;
                    break;
                }

                yield return null;
            }

            // ----------------------------------------------------
            // 4. BẮN TÊN LỬA / INK
            // ----------------------------------------------------
            if (onlineRace)
            {
                // Online:
                // launcher chỉ là animation/visual.
                // Local human owner gửi request, Server mới spawn projectile thật.
                if (isLocalHumanOwner && networkBridge != null)
                {
                    networkBridge.RequestUseItem();
                    PoseKart.Audio.AudioManager.PlayLauncherFire(transform.position);
                }

                // TUYỆT ĐỐI KHÔNG:
                // FireProjectile();
                // inventory.ClearHeldItem();
            }
            else
            {
                FireProjectile();
                PoseKart.Audio.AudioManager.PlayLauncherFire(transform.position);

                if (inventory != null)
                {
                    inventory.ClearHeldItem();
                }
            }

            yield return new WaitForSeconds(0.15f);

            // ----------------------------------------------------
            // 5. THU LAUNCHER VÀO THÂN XE (Thu nhỏ mượt mà về 0)
            // ----------------------------------------------------
            currentState = SkillState.Retracting;
            if (activeModel != null)
            {
                float retractElapsed = 0f;
                Quaternion startLocalRot = activeModel.transform.localRotation;
                Vector3 startScale = activeModel.transform.localScale;

                while (retractElapsed < deployDuration)
                {
                    retractElapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, retractElapsed / deployDuration);

                    activeModel.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                    activeModel.transform.localRotation = Quaternion.Slerp(startLocalRot, activeInitialRot, t);
                    yield return null;
                }

                activeModel.transform.localRotation = activeInitialRot;
                activeModel.transform.localScale = activeTargetScale;
                activeModel.SetActive(false);
            }

            currentTargetCar = null;
            currentState = SkillState.Ready;
            skillCoroutine = null;
        }

        private void FireProjectile()
        {
            if (PoseKartNetworkItemBridge.IsOnlineRace)
                return;

            // Nếu là Ink Shot thì phóng mực / cannon ball từ nòng MozzlePoint
            if (activeItemType == KartItemPickup.ItemType.InkShot)
            {
                CarController inkTarget = currentTargetCar != null ? currentTargetCar : FindClosestOpponentAhead();
                if (inkTarget != null)
                {
                    GameObject inkPrefabToUse = inkPrefab != null
                        ? inkPrefab
                        : (inventory != null ? inventory.InkProjectilePrefab : null);

                    Transform spawnMuzzle = GetActiveMuzzlePoint();

                    KartInkProjectile.Launch(car, inkTarget, 68f, 3.5f, 3f, 0.28f, 0.86f, inkPrefabToUse, spawnMuzzle);
                }
                return;
            }

            // Bắn Homing Missile
            GameObject prefab = missilePrefab != null
                ? missilePrefab
                : (inventory != null ? inventory.MissilePrefab : null);

            Transform missileMuzzle = muzzlePoint != null ? muzzlePoint : GetActiveMuzzlePoint();

            Vector3 spawnPos = missileMuzzle != null
                ? missileMuzzle.position
                : (launcherModel != null ? launcherModel.transform.position + launcherModel.transform.forward * 1.2f : transform.position + transform.forward * 2f + Vector3.up);

            Vector3 targetDir = currentTargetCar != null
                ? (currentTargetCar.transform.position - spawnPos).normalized
                : (missileMuzzle != null ? missileMuzzle.forward : (launcherModel != null ? launcherModel.transform.forward : transform.forward));

            Quaternion spawnRot = Quaternion.LookRotation(targetDir);

            GameObject projectile = prefab != null
                ? Instantiate(prefab, spawnPos, spawnRot)
                : new GameObject("AutoAim_Missile");

            if (prefab == null)
            {
                KartMissileModel.Attach(projectile);
            }
            if (projectile == null) return;

            Rigidbody pBody = projectile.GetComponent<Rigidbody>();
            if (pBody == null) pBody = projectile.AddComponent<Rigidbody>();
            pBody.useGravity = false;
            pBody.isKinematic = true;
            pBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Collider[] projectileColliders = projectile.GetComponentsInChildren<Collider>(true);
            if (projectileColliders.Length == 0)
            {
                SphereCollider sphere = projectile.AddComponent<SphereCollider>();
                sphere.radius = 0.35f;
                sphere.isTrigger = true;
            }
            else
            {
                foreach (Collider col in projectileColliders)
                {
                    if (col != null) col.isTrigger = true;
                }
            }

            HomingKartMissile homing = projectile.GetComponent<HomingKartMissile>();
            if (homing == null) homing = projectile.AddComponent<HomingKartMissile>();

            homing.Configure(
                car,
                currentTargetCar,
                inventory != null ? inventory.MissileSpeed : 65f,
                inventory != null ? inventory.MissileTrackingStrength : 8.5f,
                inventory != null ? inventory.MissileLifetime : 6f,
                inventory != null ? inventory.MissileImpactExplosionPrefab : null,
                inventory != null ? inventory.MissileImpactExplosionLifetime : 4f,
                inventory != null ? inventory.MissileExplosionRadius : 6f,
                inventory != null ? inventory.MissileMaximumHorizontalKnockback : 5f,
                inventory != null ? inventory.MissileMaximumUpwardKnockback : 2.5f,
                inventory != null ? inventory.MissileImpactTorque : 12f,
                inventory != null ? inventory.MissileMinimumEffectiveDistance : 0.5f,
                inventory != null ? inventory.MissileExplosionFalloff : null,
                inventory != null ? inventory.MissileKnockbackRecoveryDuration : 0.7f,
                inventory == null || inventory.MissileExplosionAffectsNearbyRacers,
                inventory == null || inventory.MissileExplosionLineOfSightCheck,
                inventory != null ? inventory.MissileTargetPrediction : 0.75f,
                inventory != null ? inventory.MissileMaximumPredictionTime : 1.25f
            );
            if (PoseKartNetworkItemBridge.IsOnlineRace)
            {
                Debug.LogWarning(
                    "[KartLauncherSkill] Blocked legacy local projectile spawn during Online Race.",
                    this
                );
                return;
            }
                AudioManager.PlayMissileLaunch(spawnPos);
        }

        private CarController FindClosestOpponentAhead()
        {
            CarController[] allCars =
                FindObjectsByType<CarController>(
                    FindObjectsSortMode.None
                );

            CarController bestTarget = null;
            float bestScore = float.PositiveInfinity;

            if (car == null)
                return null;

            bool onlineRace =
                PoseKartNetworkItemBridge.IsOnlineRace;

            float range =
                activeItemType == KartItemPickup.ItemType.InkShot
                    ? (inventory != null ? inventory.InkTargetRange : 95f)
                    : (inventory != null ? inventory.MissileTargetRange : 130f);

            float angle =
                activeItemType == KartItemPickup.ItemType.InkShot
                    ? (inventory != null ? inventory.InkTargetAngle : 85f)
                    : (inventory != null ? inventory.MissileTargetAngle : 110f);

            float rangeSqr = range * range;

            for (int i = 0; i < allCars.Length; i++)
            {
                CarController candidate = allCars[i];

                if (candidate == null ||
                    candidate == car ||
                    candidate.TargetBody == null)
                {
                    continue;
                }

                // Online remote replicas có thể disable CarController,
                // nhưng GameObject vẫn là target hợp lệ.
                if (onlineRace)
                {
                    if (!candidate.gameObject.activeInHierarchy)
                        continue;
                }
                else
                {
                    if (!candidate.isActiveAndEnabled)
                        continue;
                }

                RaceParticipant participant =
                    candidate.GetComponent<RaceParticipant>();

                if (participant != null && participant.Finished)
                    continue;

                Vector3 delta =
                    candidate.TargetBody.worldCenterOfMass -
                    car.TargetBody.worldCenterOfMass;

                delta.y = 0f;

                float distanceSqr = delta.sqrMagnitude;

                if (distanceSqr < 0.01f ||
                    distanceSqr > rangeSqr)
                {
                    continue;
                }

                float candidateAngle =
                    Vector3.Angle(
                        car.transform.forward,
                        delta
                    );

                if (candidateAngle > angle)
                    continue;

                float score =
                    distanceSqr *
                    (1f + candidateAngle / Mathf.Max(1f, angle));

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }

            // QUAN TRỌNG:
            // Không còn fallback chọn xe gần nhất phía sau.
            return bestTarget;
        }
    }
}