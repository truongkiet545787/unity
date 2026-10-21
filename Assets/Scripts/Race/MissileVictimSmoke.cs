
using System.Collections;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class MissileVictimSmoke : MonoBehaviour
    {
        [Header("Smoke Settings")]
        [SerializeField, Min(0.1f)]
        private float smokeDuration = 1.8f;

        private ParticleSystem[] particles;
        private Coroutine activeRoutine;

        private void Awake()
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
        }

        public void AttachToVictim(
            Transform victimTransform,
            float duration = -1f)
        {
            if (victimTransform == null)
                return;

            transform.SetParent(victimTransform, false);

            if (particles == null || particles.Length == 0)
                particles = GetComponentsInChildren<ParticleSystem>(true);

            if (particles.Length == 0)
            {
                Debug.LogWarning(
                    "[PoseKart] Smoke prefab has no Particle System.",
                    this
                );

                Destroy(gameObject);
                return;
            }

            if (activeRoutine != null)
                StopCoroutine(activeRoutine);

            foreach (ParticleSystem ps in particles)
            {
                if (ps == null) continue;

                var emission = ps.emission;
                emission.enabled = true;

                ps.Clear(true);
                ps.Play(true);
            }

            float activeDuration =
                duration > 0f ? duration : smokeDuration;

            activeRoutine = StartCoroutine(
                StopAndDestroyRoutine(activeDuration)
            );
        }

        private IEnumerator StopAndDestroyRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            // Stop emitting, but allow existing particles to fade naturally.
            foreach (ParticleSystem ps in particles)
            {
                if (ps == null) continue;

                ps.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmitting
                );
            }

            // Wait for all particles, including child systems, to disappear.
            float timeout = Time.time + 15f;

            while (Time.time < timeout)
            {
                bool anyAlive = false;

                foreach (ParticleSystem ps in particles)
                {
                    if (ps != null && ps.IsAlive(true))
                    {
                        anyAlive = true;
                        break;
                    }
                }

                if (!anyAlive)
                    break;

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
