using System.Collections;
using PoseKart.Vehicle;
using PoseKart.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class RaceParticipant : MonoBehaviour
    {
        [SerializeField] private string racerName;
        [SerializeField] private bool player;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CarController carController;
        [SerializeField] private LapTracker lapTracker;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private int racePosition = 1;
        [SerializeField] private bool finished;
        [SerializeField] private int finishOrder;
        [SerializeField] private double finishTime;
        [SerializeField, Min(0f)] private float finishedPresenceDelay = 1.25f;

        private Coroutine finishedPresenceRoutine;
        private Collider[] cachedColliders;
        private bool[] cachedColliderStates;
        private Renderer[] cachedRenderers;
        private bool[] cachedRendererStates;
        private bool finishedPresenceApplied;

        public string RacerName => racerName;
        public bool IsPlayer => player;
        public Rigidbody Body => body;
        public CarController CarController => carController;
        public LapTracker LapTracker => lapTracker;
        public int RacePosition => racePosition;
        public bool Finished => finished;
        public int FinishOrder => finishOrder;
        public double FinishTime => finishTime;
        public bool IsConfigured => body != null && carController != null && lapTracker != null;

        public void Configure(
            string displayName,
            bool isPlayer,
            Rigidbody racerBody,
            CarController controller,
            LapTracker tracker)
        {
            racerName = displayName;
            player = isPlayer;
            body = racerBody;
            carController = controller;
            lapTracker = tracker;
        }

        public void PrepareRace(int totalLaps, int checkpointCount)
        {
            RestoreFinishedPresence();
            lapTracker.Configure(totalLaps, checkpointCount);
            racePosition = 1;
            finished = false;
            finishOrder = 0;
            finishTime = 0d;
        }

        public void MarkFinished(int order, double time)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            finishOrder = order;
            finishTime = time;

            if (IsOnlineNetworkRuntime())
                ScheduleFinishedPresence();
        }

        public void SetRacePosition(int position)
        {
            racePosition = Mathf.Max(1, position);
        }

        public void ApplyNetworkProgress(PoseKartRaceProgressState state)
        {
            if (lapTracker != null)
                lapTracker.ApplyNetworkProgress(state);

            racePosition = Mathf.Max(1, state.racePosition);

            if (state.finished && !finished)
            {
                finished = true;
                finishOrder = Mathf.Max(1, state.finishOrder);
                finishTime = System.Math.Max(0d, state.finishTime);
            }

            if (state.finished)
            {
                if (IsOnlineNetworkRuntime())
                    ScheduleFinishedPresence();
            }
            else
            {
                RestoreFinishedPresence();
            }
        }

        private bool IsOnlineNetworkRuntime()
        {
            NetworkManager network = NetworkManager.Singleton;
            return network != null && network.IsListening;
        }

        private void ScheduleFinishedPresence()
        {
            if (finishedPresenceApplied ||
                finishedPresenceRoutine != null)
            {
                return;
            }

            finishedPresenceRoutine = StartCoroutine(
                ApplyFinishedPresenceAfterDelay()
            );
        }

        private IEnumerator ApplyFinishedPresenceAfterDelay()
        {
            if (finishedPresenceDelay > 0f)
            {
                yield return new WaitForSeconds(
                    finishedPresenceDelay
                );
            }

            CachePresenceComponents();

            for (int i = 0; i < cachedColliders.Length; i++)
            {
                Collider collider = cachedColliders[i];
                if (collider != null)
                    collider.enabled = false;
            }

            if (!IsLocalNetworkOwner())
            {
                for (int i = 0; i < cachedRenderers.Length; i++)
                {
                    Renderer renderer = cachedRenderers[i];
                    if (renderer != null)
                        renderer.enabled = false;
                }
            }

            finishedPresenceApplied = true;
            finishedPresenceRoutine = null;
        }

        private void CachePresenceComponents()
        {
            if (cachedColliders == null)
            {
                cachedColliders =
                    GetComponentsInChildren<Collider>(true);
                cachedColliderStates =
                    new bool[cachedColliders.Length];

                for (int i = 0; i < cachedColliders.Length; i++)
                {
                    cachedColliderStates[i] =
                        cachedColliders[i] != null &&
                        cachedColliders[i].enabled;
                }
            }

            if (cachedRenderers == null)
            {
                cachedRenderers =
                    GetComponentsInChildren<Renderer>(true);
                cachedRendererStates =
                    new bool[cachedRenderers.Length];

                for (int i = 0; i < cachedRenderers.Length; i++)
                {
                    cachedRendererStates[i] =
                        cachedRenderers[i] != null &&
                        cachedRenderers[i].enabled;
                }
            }
        }

        private bool IsLocalNetworkOwner()
        {
            NetworkObject networkObject =
                GetComponentInParent<NetworkObject>();

            return networkObject != null &&
                   networkObject.IsPlayerObject &&
                   networkObject.IsOwner;
        }

        private void RestoreFinishedPresence()
        {
            if (finishedPresenceRoutine != null)
            {
                StopCoroutine(finishedPresenceRoutine);
                finishedPresenceRoutine = null;
            }

            if (!finishedPresenceApplied)
                return;

            CachePresenceComponents();

            for (int i = 0; i < cachedColliders.Length; i++)
            {
                Collider collider = cachedColliders[i];
                if (collider != null)
                    collider.enabled = cachedColliderStates[i];
            }

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer renderer = cachedRenderers[i];
                if (renderer != null)
                    renderer.enabled = cachedRendererStates[i];
            }

            finishedPresenceApplied = false;
        }

        private void OnDestroy()
        {
            if (finishedPresenceRoutine != null)
                StopCoroutine(finishedPresenceRoutine);
        }
    }
}
