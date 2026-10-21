using PoseKart.AI;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>One reusable ink debuff per kart. Shield is checked before activation.</summary>
    [DisallowMultipleComponent]
    public sealed class KartInkStatus : MonoBehaviour
    {
        private CarController car;
        private RaceParticipant participant;
        private RaceManager raceManager;
        private bool isPlayer;
        private float inkUntil;
        private float steeringWobble = 0.28f;
        private float throttleMultiplier = 0.86f;
        private KartInkSteeringProxy proxy;
        private MonoBehaviour originalProvider;

        public bool IsInkActive => isActiveAndEnabled && Time.time < inkUntil;
        public float AISteeringWobble => steeringWobble;
        public float AIThrottleMultiplier => throttleMultiplier;

        /// <returns>True when ink lands; false when blocked or racer is invalid.</returns>
        public static bool TryApply(CarController victim, float duration,
            float aiSteeringWobble, float aiThrottleMultiplier, Vector3 impactPosition)
        {
            if (victim == null || !victim.isActiveAndEnabled || !victim.ControlEnabled)
                return false;

            RaceParticipant racer = victim.GetComponent<RaceParticipant>();
            if (racer != null && racer.Finished)
                return false;

            KartItemDefense shield = victim.GetComponent<KartItemDefense>();
            if (shield != null && shield.TryBlockIncomingItem(impactPosition))
                return false;

            KartInkStatus status = victim.GetComponent<KartInkStatus>();
            if (status == null)
                status = victim.gameObject.AddComponent<KartInkStatus>();
            status.Begin(victim, racer, duration, aiSteeringWobble, aiThrottleMultiplier);
            return true;
        }

        private void Begin(CarController victim, RaceParticipant racer, float duration,
            float aiSteeringWobble, float aiThrottleMultiplier)
        {
            car = victim;
            participant = racer;
            isPlayer = racer != null && racer.IsPlayer;
            // Resolve once on hit; never search the scene in Update.
            if (isPlayer || racer == null)
                raceManager = FindFirstObjectByType<RaceManager>();
            if (racer == null)
                isPlayer = raceManager != null && raceManager.PlayerController == car;

            inkUntil = Mathf.Max(inkUntil, Time.time + Mathf.Max(0.25f, duration));
            steeringWobble = Mathf.Clamp(aiSteeringWobble, 0f, 0.6f);
            throttleMultiplier = Mathf.Clamp(aiThrottleMultiplier, 0.25f, 1f);
            enabled = true;

            if (isPlayer)
                KartInkScreenOverlay.ShowUntil(inkUntil);
            else
                TryAttachAIProxy();
        }

        private void TryAttachAIProxy()
        {
            if (car == null || proxy != null)
                return;

            // Never override keyboard, MediaPipe or another temporary provider.
            AIInputProvider ai = car.InputProviderSource as AIInputProvider;
            if (ai == null)
                return;

            originalProvider = ai;
            proxy = GetComponent<KartInkSteeringProxy>();
            if (proxy == null)
                proxy = gameObject.AddComponent<KartInkSteeringProxy>();
            proxy.Configure(originalProvider, this);
            proxy.enabled = true;
            car.SetInputProvider(proxy);
        }

        private void Update()
        {
            if (car == null || !car.isActiveAndEnabled || !car.ControlEnabled ||
                (participant != null && participant.Finished) ||
                (isPlayer && raceManager != null && raceManager.PlayerFinished))
            {
                if (isPlayer)
                    KartInkScreenOverlay.Hide();
                Finish();
                return;
            }

            if (Time.time >= inkUntil)
            {
                Finish();
                return;
            }

            // AI may acquire its provider after the ink effect was applied.
            if (!isPlayer && proxy == null)
                TryAttachAIProxy();
        }

        public void ClearForRace()
        {
            if (isPlayer)
                KartInkScreenOverlay.Hide();
            Finish();
        }

        private void Finish()
        {
            if (car != null && proxy != null && car.InputProviderSource == proxy && originalProvider != null)
                car.SetInputProvider(originalProvider);

            if (proxy != null)
            {
                proxy.enabled = false;
                Destroy(proxy);
            }
            proxy = null;
            originalProvider = null;
            inkUntil = 0f;
            enabled = false;
        }

        private void OnDisable()
        {
            // Also restore the original input when the kart is disabled mid-effect.
            if (car != null && proxy != null && car.InputProviderSource == proxy && originalProvider != null)
                car.SetInputProvider(originalProvider);
            if (proxy != null)
            {
                proxy.enabled = false;
                Destroy(proxy);
            }
            proxy = null;
            originalProvider = null;
        }
    }
}
