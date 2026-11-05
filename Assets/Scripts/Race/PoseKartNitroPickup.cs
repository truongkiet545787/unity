using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// A reusable track Nitro collectible. It only adds stored Nitro; it never
    /// starts the boost itself, so the existing keyboard/MediaPipe/AI Nitro
    /// controls remain the single source of truth for using the charge.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartNitroPickup : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<PoseKartNitroPickup> allPickups =
            new System.Collections.Generic.List<PoseKartNitroPickup>();
        private static int initializedSceneHandle = -1;

        [Header("Nitro Pickup")]
        [Tooltip("Nitro charge units added to the car. The current race cars use 2.5 as a full bar.")]
        [SerializeField, Min(0.01f)] private float nitroAmount = 0.75f;
        [SerializeField, Min(0f)] private float respawnSeconds = 6f;
        [SerializeField] private bool allowAIVehicles;
        [SerializeField] private bool playPickupAudio = true;
        [Tooltip("Uses the orange Nitro pickup sound instead of the blue pickup sound.")]
        [SerializeField] private bool orangePickupSound;

        [Header("Trigger")]
        [SerializeField] private Vector3 triggerSize = new Vector3(1.4f, 1.8f, 1.4f);
        [SerializeField] private Vector3 triggerCenter = new Vector3(0f, 0.9f, 0f);
        [SerializeField, Min(0.05f)] private float serverProximityScanInterval = 0.1f;
        [SerializeField, Min(0.25f)] private float serverPlayerScanRadius = 3f;

        [Header("Runtime Debug")]
        [SerializeField] private bool available = true;
        [SerializeField] private string lastCollector = "None";

        private Collider trigger;
        private Renderer[] visuals;
        private ParticleSystem[] particles;
        private int pickupIndex = -1;
        private CarController lastTriggerCar;
        private float nextTriggerAttemptTime;
        private float nextServerProximityScanTime;
        private readonly Collider[] serverOverlapResults = new Collider[24];

        public float NitroAmount => nitroAmount;
        public bool IsAvailable => available;
        public int PickupIndex => pickupIndex;
        public float RespawnSeconds => respawnSeconds;
        public static System.Collections.Generic.IReadOnlyList<PoseKartNitroPickup> AllPickups => allPickups;

        public static void RefreshAllPickups()
        {
            initializedSceneHandle = -1;
            InitializeAllPickups();
        }

        public static void InitializeAllPickups()
        {
            int activeSceneHandle =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

            if (initializedSceneHandle == activeSceneHandle && allPickups.Count > 0)
                return;

            allPickups.Clear();
            allPickups.AddRange(FindObjectsByType<PoseKartNitroPickup>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
            allPickups.Sort((a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;
                return string.CompareOrdinal(
                    GetHierarchyPath(a.transform),
                    GetHierarchyPath(b.transform));
            });

            for (int i = 0; i < allPickups.Count; i++)
            {
                if (allPickups[i] != null)
                    allPickups[i].pickupIndex = i;
            }

            initializedSceneHandle = activeSceneHandle;
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (trigger == null)
                trigger = gameObject.AddComponent<BoxCollider>();

            trigger.isTrigger = true;
            ApplyTriggerShape();
            visuals = GetComponentsInChildren<Renderer>(true);
            particles = GetComponentsInChildren<ParticleSystem>(true);
            InitializeAllPickups();
            SetAvailable(true);
        }

        /// <summary>
        /// Used by the reference-scene editor setup when it places the
        /// imported nitro model along the racing line.
        /// </summary>
        public void Configure(float amount, float respawn, bool orange = false)
        {
            nitroAmount = Mathf.Max(0.01f, amount);
            respawnSeconds = Mathf.Max(0f, respawn);
            orangePickupSound = orange;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryCollect(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // OnTriggerStay makes a high-speed pass reliable when the pickup
            // and vehicle use small colliders. available prevents duplicates.
            TryCollect(other);
        }

        private void FixedUpdate()
        {
            // Trigger callbacks are normally enough, but an owner-authoritative
            // Rigidbody can move through a scene pickup without producing a
            // reliable Server-side callback. This is only a Server fallback;
            // it calls the same authoritative request path and never adds
            // Nitro directly.
            if (!available || !PoseKartNetworkItemBridge.IsOnlineRace)
                return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer)
                return;

            if (Time.unscaledTime < nextServerProximityScanTime)
                return;

            nextServerProximityScanTime =
                Time.unscaledTime + Mathf.Max(0.05f, serverProximityScanInterval);

            Vector3 scale = transform.lossyScale;
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(scale.x) * Mathf.Max(0.1f, triggerSize.x) * 0.5f,
                Mathf.Abs(scale.y) * Mathf.Max(0.1f, triggerSize.y) * 0.5f,
                Mathf.Abs(scale.z) * Mathf.Max(0.1f, triggerSize.z) * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(triggerCenter),
                halfExtents,
                serverOverlapResults,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider candidate = serverOverlapResults[i];
                if (candidate == null)
                    continue;

                TryCollect(candidate);
                if (!available)
                    break;
            }

            // Owner-authoritative player physics can leave the Host's local
            // Rigidbody callback out of the Server physics step. Resolve the
            // NGO PlayerObjects directly as a server-side fallback, then feed
            // the same TryCollect path used by trigger callbacks.
            if (available && manager.ConnectedClientsList != null)
            {
                float scanRadius = Mathf.Max(0.25f, serverPlayerScanRadius);
                for (int i = 0; i < manager.ConnectedClientsList.Count; i++)
                {
                    NetworkClient client = manager.ConnectedClientsList[i];
                    NetworkObject playerObject = client != null
                        ? client.PlayerObject
                        : null;
                    if (playerObject == null || !playerObject.IsSpawned)
                        continue;

                    CarController playerCar =
                        playerObject.GetComponentInChildren<CarController>(true);
                    if (playerCar == null ||
                        Vector3.Distance(playerCar.transform.position, transform.position) > scanRadius)
                    {
                        continue;
                    }

                    TryCollect(playerCar);
                    if (!available)
                        break;
                }
            }
        }

        private void TryCollect(Collider other)
        {
            if (!available || trigger == null || !trigger.enabled || other == null)
                return;

            Rigidbody body = other.attachedRigidbody;
            CarController car = body != null
                ? body.GetComponentInParent<CarController>()
                : other.GetComponentInParent<CarController>();
            TryCollect(car);
        }

        private void TryCollect(CarController car)
        {
            // ControlEnabled is a driving permission, not a pickup-validity
            // flag. The Host/server proxy can be temporarily locked during
            // countdown, respawn or presentation while it is still a valid
            // racer. Finished/inactive racers are filtered by the bridge.
            if (car == null || !car.isActiveAndEnabled ||
                !car.gameObject.activeInHierarchy)
                return;

            if (!allowAIVehicles)
            {
                if (PoseKartNetworkItemBridge.IsOnlineRace)
                {
                    // RaceParticipant.IsPlayer only means "local player on
                    // this machine". On the Server that would reject a
                    // remote human Client as if it were AI. NGO's
                    // IsPlayerObject is the authoritative human/AI split.
                    NetworkObject networkObject =
                        car.GetComponent<NetworkObject>() ??
                        car.GetComponentInParent<NetworkObject>();

                    if (networkObject == null || !networkObject.IsPlayerObject)
                        return;
                }
                else
                {
                    RaceParticipant participant =
                        car.GetComponent<RaceParticipant>() ??
                        car.GetComponentInParent<RaceParticipant>();
                    if (participant != null && !participant.IsPlayer)
                        return;
                }
            }

            if (PoseKartNetworkItemBridge.IsOnlineRace)
            {
                NetworkManager manager = NetworkManager.Singleton;
                NetworkObject networkObject =
                    car.GetComponent<NetworkObject>() ??
                    car.GetComponentInParent<NetworkObject>();

                // Only the local human owner or the Server may request a
                // pickup. Remote clones must never consume it locally.
                if (manager == null || !manager.IsListening ||
                    (!manager.IsServer &&
                     (networkObject == null ||
                      !networkObject.IsPlayerObject ||
                      !networkObject.IsOwner)))
                {
                    return;
                }

                PoseKartNetworkItemBridge bridge =
                    car.GetComponent<PoseKartNetworkItemBridge>() ??
                    car.GetComponentInParent<PoseKartNetworkItemBridge>();
                if (bridge == null)
                    return;

                if (pickupIndex < 0)
                {
                    InitializeAllPickups();
                    if (pickupIndex < 0)
                        return;
                }

                if (lastTriggerCar == car &&
                    Time.unscaledTime < nextTriggerAttemptTime)
                {
                    return;
                }

                lastTriggerCar = car;
                nextTriggerAttemptTime = Time.unscaledTime + 0.2f;
                bridge.HandleNitroPickupTrigger(pickupIndex);
                return;
            }

            if (!car.AddNitroPickup(nitroAmount))
                return; // Normal meter is full; leave pickup available.

            lastCollector = car.name;
            if (playPickupAudio)
                PoseKart.Audio.AudioManager.PlayNitroPickup(car, transform.position, orangePickupSound);
            KartParticleEffect.Burst(transform.position, new Color(0.1f, 0.9f, 1f, 1f), 32);

            SetAvailable(false);
            if (respawnSeconds > 0f)
                Invoke(nameof(Respawn), respawnSeconds);
            else
                enabled = false;
        }

        public void ServerSetCollected()
        {
            SetAvailable(false);
            CancelInvoke(nameof(Respawn));
            if (respawnSeconds > 0f)
                Invoke(nameof(Respawn), respawnSeconds);
        }

        public void PlayCollectFeedback()
        {
            if (playPickupAudio)
            {
                PoseKart.Audio.AudioManager.PlayNitroPickup(
                    null,
                    transform.position,
                    orangePickupSound);
            }

            KartParticleEffect.Burst(
                transform.position,
                new Color(0.1f, 0.9f, 1f, 1f),
                32);
        }

        public void ClientSyncCollect(float duration)
        {
            SetAvailable(false);
            PlayCollectFeedback();
            CancelInvoke(nameof(Respawn));
            if (duration > 0f)
                Invoke(nameof(Respawn), duration);
        }

        private void Respawn()
        {
            if (this == null)
                return;

            SetAvailable(true);
        }

        private void SetAvailable(bool value)
        {
            available = value;
            if (trigger != null)
                trigger.enabled = value;

            if (visuals != null)
            {
                for (int i = 0; i < visuals.Length; i++)
                {
                    if (visuals[i] != null)
                        visuals[i].enabled = value;
                }
            }

            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] == null)
                        continue;

                    if (value)
                        particles[i].Play(true);
                    else
                        particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void OnDestroy()
        {
            allPickups.Remove(this);
            if (allPickups.Count == 0)
                initializedSceneHandle = -1;
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            string path = target.name;
            Transform parent = target.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private void ApplyTriggerShape()
        {
            BoxCollider box = trigger as BoxCollider;
            if (box == null)
                return;

            box.size = new Vector3(
                Mathf.Max(0.1f, triggerSize.x),
                Mathf.Max(0.1f, triggerSize.y),
                Mathf.Max(0.1f, triggerSize.z));
            box.center = triggerCenter;
        }

        private void OnValidate()
        {
            nitroAmount = Mathf.Max(0.01f, nitroAmount);
            respawnSeconds = Mathf.Max(0f, respawnSeconds);
            triggerSize.x = Mathf.Max(0.1f, triggerSize.x);
            triggerSize.y = Mathf.Max(0.1f, triggerSize.y);
            triggerSize.z = Mathf.Max(0.1f, triggerSize.z);
            serverProximityScanInterval = Mathf.Max(0.05f, serverProximityScanInterval);
            ApplyTriggerShape();
        }
    }
}
