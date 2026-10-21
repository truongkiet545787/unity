using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Runtime-only visual attached to a racer after a successful banana hit.
    /// Uses the existing prefab (including its ParticleSpin script), follows the
    /// kart, and stops as soon as CarController's real banana stun ends.
    /// Does not modify kart physics, input, camera or stun timing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartBananaDizzyVFX : MonoBehaviour
    {
        private const float MaxParticleTailSeconds = 2f;

        private CarController target;
        private GameObject effectInstance;
        private GameObject prefabSource;
        private ParticleSystem[] particles;
        private bool emitting;
        private float tailDeadline;

        /// <summary>Called only after TryApplyBananaStun returns true.</summary>
        public void Play(CarController car, GameObject prefab, Vector3 localOffset, float scaleMultiplier)
        {
            if (car == null || prefab == null)
                return;

            target = car;

            // Reuse an already-running effect if another banana extends the stun.
            // A different assigned prefab replaces the previous effect cleanly.
            if (effectInstance == null || prefabSource != prefab)
            {
                ClearInstance();
                prefabSource = prefab;

                // Optional direct child of each kart for fine placement.
                // Without it, use the supplied offset from the kart root.
                Transform anchor = car.transform.Find("DizzyAnchor");
                Transform parent = anchor != null ? anchor : car.transform;

                effectInstance = Instantiate(prefab, parent, false);
                effectInstance.name = "Banana_Dizzy_VFX";
                effectInstance.transform.localPosition = anchor != null ? Vector3.zero : localOffset;
                effectInstance.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scaleMultiplier);
                if (!effectInstance.activeSelf)
                    effectInstance.SetActive(true);

                // VFX must never become another physical obstacle or pickup.
                Collider[] colliders = effectInstance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null) colliders[i].enabled = false;

                particles = effectInstance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem particle = particles[i];
                    if (particle == null) continue;
                    var main = particle.main;
                    // Dizzy stars should stay over the kart, not leave a world-space trail.
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.stopAction = ParticleSystemStopAction.None;
                }
            }

            // The prefab's own ParticleSpin.Update keeps working untouched.
            // Restart emission if the effect was in its fade-out tail.
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem particle = particles[i];
                    if (particle != null && particle.gameObject.activeInHierarchy)
                        particle.Play(false);
                }
            }

            emitting = true;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (effectInstance == null)
            {
                particles = null;
                emitting = false;
                enabled = false;
                return;
            }

            // Trust the actual stun state: race reset, disabling control, or a
            // second banana extending stun all produce the correct VFX lifetime.
            if (emitting && (target == null || !target.isActiveAndEnabled || !target.IsBananaStunned))
            {
                emitting = false;
                tailDeadline = Time.time + MaxParticleTailSeconds;
                if (particles != null)
                {
                    for (int i = 0; i < particles.Length; i++)
                    {
                        if (particles[i] != null)
                            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }

            if (emitting)
                return;

            bool anyAlive = false;
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null && particles[i].IsAlive(false))
                    {
                        anyAlive = true;
                        break;
                    }
                }
            }

            if (!anyAlive || Time.time >= tailDeadline)
            {
                ClearInstance();
                enabled = false;
            }
        }

        private void ClearInstance()
        {
            if (effectInstance != null)
                Destroy(effectInstance);
            effectInstance = null;
            prefabSource = null;
            particles = null;
            emitting = false;
        }

        private void OnDisable()
        {
            // If the kart is disabled/destroyed mid-race, don't leave VFX behind.
            ClearInstance();
        }
    }
}
