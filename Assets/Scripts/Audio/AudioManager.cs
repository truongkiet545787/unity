using System.Collections;
using System.Collections.Generic;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Audio
{
    /// <summary>
    /// HỆ THỐNG QUẢN LÝ ÂM THANH DUY NHẤT VÀ TẬP TRUNG (CENTRALIZED AUDIO MANAGER)
    /// Hợp nhất tất cả hệ thống âm thanh trong game thành một script duy nhất:
    /// - 2D UI / Global SFX & 3D Positional SFX (Object Pooling)
    /// - Âm thanh Môi trường đảo (Sóng biển, gió nhiệt đới, lá cọ, chim ngẫu nhiên)
    /// - Âm thanh Xe & Tiếp đất (Còi xe Honk, tiếp đất nhẹ/vừa/mạnh)
    /// - Âm thanh Vũ khí & Kỹ năng (Tháp súng Launcher deploy/fire)
    /// - Âm thanh Vật phẩm & Hiệu ứng đua (Hộp quà, Nitro, Tên lửa, Chuối, Khiên, Boost)
    /// - Âm thanh HUD & Giao diện (Đếm ngược Countdown 3-2-1-GO, Roulette tick, Kết thúc cuộc đua Finish/Victory/Click)
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour
    {
        private static AudioManager _instance;

        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<AudioManager>();
                    if (_instance == null)
                    {
                        GameObject managerObject = new GameObject("[AudioManager]");
                        _instance = managerObject.AddComponent<AudioManager>();
                    }
                }
                return _instance;
            }
        }

        #region Serialized Fields - Environment Ambience (Môi trường)

        [Header("Global Island Ambience")]
        [SerializeField] private AudioClip oceanWavesLoop;
        [SerializeField] private AudioClip tropicalWindLoop;
        [SerializeField] private AudioClip palmLeavesLoop;
        [SerializeField, Range(0f, 1f)] private float oceanVolume = 0.22f;
        [SerializeField, Range(0f, 1f)] private float windVolume = 0.16f;
        [SerializeField, Range(0f, 1f)] private float palmVolume = 0.1f;
        [SerializeField, Min(0.1f)] private float oceanMinDistance = 25f;
        [SerializeField, Min(1f)] private float oceanMaxDistance = 220f;
        [SerializeField] private Transform oceanAnchor;
        [SerializeField] private Transform forestAnchor;

        [Header("Occasional Tropical Birds")]
        [SerializeField] private AudioClip[] birdClips;
        [SerializeField, Min(1f)] private float minimumBirdDelay = 12f;
        [SerializeField, Min(1f)] private float maximumBirdDelay = 28f;
        [SerializeField, Range(0f, 1f)] private float birdVolume = 0.16f;
        [SerializeField, Min(1f)] private float birdMinDistance = 8f;
        [SerializeField, Min(1f)] private float birdMaxDistance = 55f;
        [SerializeField] private Transform birdAnchor;

        #endregion

        #region Serialized Fields - Vehicle Audio (Còi & Tiếp đất)

        [Header("Vehicle Horn Audio")]
        [SerializeField] private AudioClip hornClip;
        [SerializeField, Range(0f, 1f)] private float defaultHornVolume = 0.7f;

        [Header("Vehicle Landing Audio")]
        [SerializeField] private AudioClip lightTireClip;
        [SerializeField] private AudioClip mediumLandingClip;
        [SerializeField] private AudioClip heavyLandingClip;
        [SerializeField] private AudioClip suspensionClunkClip;
        [SerializeField, Range(0f, 1f)] private float defaultLandingVolume = 0.85f;

        #endregion

        #region Serialized Fields - Weapons & Skills (Tháp súng)

        [Header("Weapon & Skill Audio")]
        [SerializeField] private AudioClip launcherDeployClip;
        [SerializeField] private AudioClip launcherFireClip;
        [SerializeField, Range(0f, 1f)] private float weaponVolume = 0.85f;

        #endregion

        #region Serialized Fields - Race Items & Pickups (Vật phẩm)

        [Header("Item Pickup Audio")]
        [SerializeField] private AudioClip pickupClip;
        [SerializeField] private AudioClip itemReceivedClip;
        [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float receivedVolume = 0.85f;

        [Header("Nitro Pickup Audio")]
        [SerializeField] private AudioClip blueNitroPickupClip;
        [SerializeField] private AudioClip orangeNitroPickupClip;
        [SerializeField, Range(0f, 1f)] private float blueNitroPickupVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float orangeNitroPickupVolume = 0.9f;

        [Header("Item Combat & Effect Audio")]
        [SerializeField] private AudioClip missileLaunchClip;
        [SerializeField] private AudioClip missileImpactClip;
        [SerializeField] private AudioClip bananaDropClip;
        [SerializeField] private AudioClip bananaSlipClip;
        [SerializeField] private AudioClip shieldActivateClip;
        [SerializeField] private AudioClip shieldBlockClip;
        [SerializeField] private AudioClip shieldExpireClip;
        [SerializeField, Range(0f, 1f)] private float effectVolume = 0.75f;

        #endregion

        #region Serialized Fields - UI & Race HUD (Giao diện)

        [Header("Countdown Audio")]
        [SerializeField] private AudioClip countdownBeepClip;
        [SerializeField] private AudioClip countdownGoClip;
        [SerializeField, Range(0f, 1f)] private float countdownVolume = 0.85f;

        [Header("Roulette Audio")]
        [SerializeField] private AudioClip rouletteTickClip;
        [SerializeField, Range(0f, 1f)] private float rouletteTickVolume = 0.7f;

        [Header("Race Finish Audio")]
        [SerializeField] private AudioClip finishSoundClip;
        [SerializeField] private AudioClip victorySoundClip;
        [SerializeField] private AudioClip resultsRevealClip;
        [SerializeField] private AudioClip buttonClickClip;
        [SerializeField, Range(0f, 1f)] private float finishAudioVolume = 0.9f;

        #endregion

        #region Audio Pool Settings & State

        [Header("Audio Pool Settings")]
        [SerializeField] private int initialPoolSize = 16;
        [SerializeField] private int maxPoolSize = 40;

        private AudioSource sfx2DSource;
        private readonly Queue<AudioSource> pooled3DSources = new Queue<AudioSource>();
        private readonly List<AudioSource> activeLoops = new List<AudioSource>();
        private Transform poolContainer;
        private float nextBirdTime;

        #endregion

        #region Lifecycle & Initialization

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureForLoadedScene()
        {
            if (FindFirstObjectByType<AudioManager>() != null) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            GameObject root = new GameObject("[AudioManager]");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<AudioManager>();
        }

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize2DSource();
            InitializePool();
            EnsureSingleAudioListener();
            StartEnvironmentAmbience();
            ScheduleNextBird();
        }

        protected virtual void Start()
        {
            EnsureSingleAudioListener();
        }

        /// <summary>
        /// Đảm bảo trong Scene chỉ có duy nhất 1 AudioListener hoạt động (ưu tiên MainCamera).
        /// Tránh cảnh báo Unity spam "There are 2 audio listeners in the scene."
        /// </summary>
        public void EnsureSingleAudioListener()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (listeners == null || listeners.Length <= 1) return;

            AudioListener primary = null;
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].CompareTag("MainCamera"))
                {
                    primary = listeners[i];
                    break;
                }
            }

            if (primary == null)
            {
                primary = listeners[0];
            }

            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i] != primary)
                {
                    listeners[i].enabled = false;
                }
            }
        }

        private void Initialize2DSource()
        {
            if (sfx2DSource == null)
            {
                sfx2DSource = gameObject.AddComponent<AudioSource>();
                sfx2DSource.playOnAwake = false;
                sfx2DSource.spatialBlend = 0f;
                sfx2DSource.loop = false;
                sfx2DSource.ignoreListenerPause = true;
            }
        }

        private void InitializePool()
        {
            if (poolContainer == null)
            {
                GameObject container = new GameObject("3D_SFX_Pool");
                container.transform.SetParent(transform);
                poolContainer = container.transform;
            }

            for (int i = 0; i < initialPoolSize; i++)
            {
                CreateNewPooledSource();
            }
        }

        private AudioSource CreateNewPooledSource()
        {
            GameObject sourceObj = new GameObject("Pooled_AudioSource");
            sourceObj.transform.SetParent(poolContainer);
            AudioSource source = sourceObj.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            sourceObj.SetActive(false);
            pooled3DSources.Enqueue(source);
            return source;
        }

        private AudioSource GetPooledSource()
        {
            while (pooled3DSources.Count > 0)
            {
                AudioSource candidate = pooled3DSources.Dequeue();
                if (candidate != null)
                {
                    candidate.gameObject.SetActive(true);
                    return candidate;
                }
            }
            return CreateNewPooledSource();
        }

        private void ReturnSourceToPool(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;
            source.transform.SetParent(poolContainer);
            source.gameObject.SetActive(false);

            if (pooled3DSources.Count < maxPoolSize)
            {
                pooled3DSources.Enqueue(source);
            }
            else
            {
                Destroy(source.gameObject);
            }
        }

        #endregion

        #region Environment Ambience & Birds

        private void StartEnvironmentAmbience()
        {
            if (oceanWavesLoop != null)
                PlayLoop(oceanWavesLoop, oceanVolume, oceanAnchor, oceanMinDistance, oceanMaxDistance);

            if (tropicalWindLoop != null)
                PlayLoop(tropicalWindLoop, windVolume, null, 30f, 180f);

            if (palmLeavesLoop != null)
                PlayLoop(palmLeavesLoop, palmVolume, forestAnchor, 15f, 90f);
        }

        protected virtual void Update()
        {
            UpdateTropicalBirds();
        }

        private void UpdateTropicalBirds()
        {
            if (birdClips == null || birdClips.Length == 0) return;
            if (Time.time < nextBirdTime) return;

            AudioClip clip = birdClips[Random.Range(0, birdClips.Length)];
            if (clip != null)
            {
                Vector3 position = birdAnchor != null ? birdAnchor.position : transform.position;
                float volume = birdVolume * Random.Range(0.75f, 1f);
                float pitch = Random.Range(0.92f, 1.08f);
                PlaySFXAtPosition(clip, position, volume, pitch, birdMinDistance, birdMaxDistance);
            }

            ScheduleNextBird();
        }

        private void ScheduleNextBird()
        {
            nextBirdTime = Time.time + Random.Range(minimumBirdDelay, Mathf.Max(minimumBirdDelay, maximumBirdDelay));
        }

        #endregion

        #region Core SFX & Pooling API (2D & 3D)

        public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            if (sfx2DSource == null) Initialize2DSource();

            sfx2DSource.pitch = pitch;
            sfx2DSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            PlaySFXAtPosition(clip, position, volume, pitch, 1f, 500f);
        }

        public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volume, float pitch, float minDistance, float maxDistance)
        {
            if (clip == null) return;

            AudioSource source = GetPooledSource();
            if (source == null)
            {
                AudioSource.PlayClipAtPoint(clip, position, volume);
                return;
            }

            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = pitch;
            source.minDistance = Mathf.Max(0.1f, minDistance);
            source.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            source.spatialBlend = 1f;
            source.loop = false;
            source.Play();

            StartCoroutine(ReleaseSourceWhenFinished(source, clip.length, pitch));
        }

        private IEnumerator ReleaseSourceWhenFinished(AudioSource source, float clipLength, float pitch)
        {
            float safeDuration = Mathf.Abs(pitch) > 0.01f ? (clipLength / Mathf.Abs(pitch)) : clipLength;
            yield return new WaitForSeconds(safeDuration + 0.05f);
            ReturnSourceToPool(source);
        }

        public AudioSource PlayLoop(AudioClip clip, float volume = 1f, Transform anchor = null, float minDistance = 10f, float maxDistance = 100f)
        {
            if (clip == null) return null;

            GameObject host = new GameObject($"Loop_{clip.name}");
            if (anchor != null)
            {
                host.transform.SetParent(anchor);
                host.transform.localPosition = Vector3.zero;
            }
            else
            {
                host.transform.SetParent(transform);
            }

            AudioSource source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = Mathf.Clamp01(volume);
            source.spatialBlend = anchor == null ? 0.15f : 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            source.Play();

            activeLoops.Add(source);
            return source;
        }

        public void StopLoop(AudioClip clip)
        {
            if (clip == null) return;

            for (int i = activeLoops.Count - 1; i >= 0; i--)
            {
                AudioSource s = activeLoops[i];
                if (s != null && s.clip == clip)
                {
                    s.Stop();
                    Destroy(s.gameObject);
                    activeLoops.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Static API - Vehicle (Còi & Tiếp đất)

        public static void PlayHorn(Vector3 position, float volume = -1f)
        {
            AudioManager mgr = Instance;
            if (mgr == null || mgr.hornClip == null) return;
            float vol = volume >= 0f ? volume : mgr.defaultHornVolume;
            mgr.PlaySFXAtPosition(mgr.hornClip, position, vol, 1f, 3f, 40f);
        }

        public static void PlayLanding(Vector3 position, float impactSpeed, float severity)
        {
            AudioManager mgr = Instance;
            if (mgr == null) return;

            AudioClip clip = impactSpeed >= 7.2f
                ? mgr.heavyLandingClip
                : impactSpeed >= 4.0f
                    ? mgr.mediumLandingClip
                    : mgr.lightTireClip;

            if (clip != null)
            {
                float pitch = Random.Range(0.96f, 1.04f);
                float vol = mgr.defaultLandingVolume * Mathf.Lerp(0.45f, 1f, severity);
                mgr.PlaySFXAtPosition(clip, position, vol, pitch, 2f, 32f);
            }

            if (mgr.suspensionClunkClip != null && impactSpeed >= 4.0f)
            {
                mgr.PlaySFXAtPosition(mgr.suspensionClunkClip, position, mgr.defaultLandingVolume * 0.35f, 1f, 2f, 32f);
            }
        }

        #endregion

        #region Static API - Weapons & Skills (Tháp súng Launcher)

        public static void PlayLauncherDeploy(Vector3 position)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.launcherDeployClip != null)
                mgr.PlaySFXAtPosition(mgr.launcherDeployClip, position, mgr.weaponVolume, 1f, 2f, 35f);
        }

        public static void PlayLauncherFire(Vector3 position)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.launcherFireClip != null)
                mgr.PlaySFXAtPosition(mgr.launcherFireClip, position, mgr.weaponVolume, 1f, 3f, 50f);
        }

        #endregion

        #region Static API - Items & Race Pickups

        public static void PlayPickup(CarController car, Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr == null || mgr.pickupClip == null || car == null) return;

            if (IsPlayerCar(car))
                mgr.PlaySFX(mgr.pickupClip, mgr.pickupVolume);
            else
                mgr.PlaySFXAtPosition(mgr.pickupClip, worldPosition, mgr.pickupVolume);
        }

        public static void PlayNitroPickup(CarController car, Vector3 worldPosition, bool orange)
        {
            AudioManager mgr = Instance;
            if (mgr == null || car == null) return;

            AudioClip clip = orange ? mgr.orangeNitroPickupClip : mgr.blueNitroPickupClip;
            float volume = orange ? mgr.orangeNitroPickupVolume : mgr.blueNitroPickupVolume;

            if (clip == null)
            {
                clip = mgr.pickupClip;
                volume = mgr.pickupVolume;
            }
            if (clip == null) return;

            if (IsPlayerCar(car))
                mgr.PlaySFX(clip, volume);
            else
                mgr.PlaySFXAtPosition(clip, worldPosition, volume);
        }

        public static void PlayItemReceived(CarController car)
        {
            AudioManager mgr = Instance;
            if (mgr == null || mgr.itemReceivedClip == null || car == null || !IsPlayerCar(car)) return;
            mgr.PlaySFX(mgr.itemReceivedClip, mgr.receivedVolume);
        }

        public static void PlayRoulette()
        {
            PlayRouletteTick();
        }

        public static void PlayMissileLaunch(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.missileLaunchClip != null)
                mgr.PlaySFXAtPosition(mgr.missileLaunchClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayMissileImpact(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.missileImpactClip != null)
                mgr.PlaySFXAtPosition(mgr.missileImpactClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayBananaDrop(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.bananaDropClip != null)
                mgr.PlaySFXAtPosition(mgr.bananaDropClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayBananaSlip(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.bananaSlipClip != null)
                mgr.PlaySFXAtPosition(mgr.bananaSlipClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayShieldActivate(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.shieldActivateClip != null)
                mgr.PlaySFXAtPosition(mgr.shieldActivateClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayShieldBlock(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.shieldBlockClip != null)
                mgr.PlaySFXAtPosition(mgr.shieldBlockClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayShieldExpire(Vector3 worldPosition)
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.shieldExpireClip != null)
                mgr.PlaySFXAtPosition(mgr.shieldExpireClip, worldPosition, mgr.effectVolume);
        }

        public static void PlayBoostActivate(Vector3 worldPosition)
        {
            // Boost audio is handled directly by vehicle audio (PoseKartVehicleAudio)
        }

        private static bool IsPlayerCar(CarController car)
        {
            if (car == null) return false;
            var participant = car.GetComponent<PoseKart.Race.RaceParticipant>();
            return participant != null
                ? participant.IsPlayer
                : car.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Static API - UI, Countdown & Finish

        public static void PlayCountdown(string display)
        {
            AudioManager mgr = Instance;
            if (mgr == null || string.IsNullOrEmpty(display)) return;

            AudioClip clip = display == "GO!" ? mgr.countdownGoClip : mgr.countdownBeepClip;
            if (clip != null)
            {
                mgr.PlaySFX(clip, mgr.countdownVolume);
            }
        }

        public static void PlayRouletteTick()
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.rouletteTickClip != null)
            {
                mgr.PlaySFX(mgr.rouletteTickClip, mgr.rouletteTickVolume);
            }
        }

        public static void PlayRaceFinish()
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.finishSoundClip != null)
                mgr.PlaySFX(mgr.finishSoundClip, mgr.finishAudioVolume);
        }

        public static void PlayVictory()
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.victorySoundClip != null)
                mgr.PlaySFX(mgr.victorySoundClip, mgr.finishAudioVolume);
        }

        public static void PlayResultsReveal()
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.resultsRevealClip != null)
                mgr.PlaySFX(mgr.resultsRevealClip, mgr.finishAudioVolume);
        }

        public static void PlayButtonClick()
        {
            AudioManager mgr = Instance;
            if (mgr != null && mgr.buttonClickClip != null)
                mgr.PlaySFX(mgr.buttonClickClip, mgr.finishAudioVolume);
        }

        #endregion

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
