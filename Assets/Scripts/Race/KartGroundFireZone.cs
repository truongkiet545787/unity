using System.Collections.Generic;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Fiery ground patch spawned when a missile strikes the terrain.
    /// Burns for a set duration, displays ground flames, and penalizes racers driving through it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartGroundFireZone : MonoBehaviour
    {
        [Header("Ground Fire Settings")]
        [SerializeField] private float duration = 5f;
        [SerializeField] private float radius = 3f;
        [SerializeField] private bool affectsRacers = true;
        [SerializeField, Range(0.2f, 1f)] private float burnSpeedMultiplier = 0.65f;
        [SerializeField] private float burnTorque = 6f;

        private CarController owner;
        private float spawnTime;
        private SphereCollider triggerCollider;
        private ParticleSystem proceduralFlames;
        private ParticleSystem proceduralSmoke;
        private readonly HashSet<CarController> burningCars = new HashSet<CarController>();

        public void Initialize(CarController creator, float fireDuration, float fireRadius, bool damageRacers, GameObject customPrefab)
        {
            owner = creator;
            duration = Mathf.Max(1f, fireDuration);
            radius = Mathf.Max(0.5f, fireRadius);
            affectsRacers = damageRacers;
            spawnTime = Time.time;

            triggerCollider = GetComponent<SphereCollider>();
            if (triggerCollider == null)
                triggerCollider = gameObject.AddComponent<SphereCollider>();

            triggerCollider.isTrigger = true;
            triggerCollider.radius = radius;

            if (customPrefab != null)
            {
                GameObject customInstance = Instantiate(customPrefab, transform.position, transform.rotation, transform);
                customInstance.name = "Custom_Fire_Visual";
            }
            else
            {
                CreateProceduralFireVisual();
            }

            // Audio sizzle
            PoseKart.Audio.AudioManager.PlayBananaSlip(transform.position);

            Destroy(gameObject, duration + 0.5f);
        }

        private void CreateProceduralFireVisual()
        {
            // Procedural flame particles directly on the ground
            proceduralFlames = KartParticleEffect.Create(
                "Ground Flame Patch",
                transform,
                new Color(1f, 0.42f, 0.08f, 0.9f),
                0.65f,
                0.55f);

            if (proceduralFlames != null)
            {
                var main = proceduralFlames.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.85f, 0.15f, 1f),
                    new Color(1f, 0.28f, 0.02f, 0.85f));

                var shape = proceduralFlames.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius * 0.85f;

                var emission = proceduralFlames.emission;
                emission.enabled = true;
                emission.rateOverTime = 38f;

                proceduralFlames.Play();
            }

            // Dark smoke rising from the burning ground
            proceduralSmoke = KartParticleEffect.Create(
                "Ground Fire Smoke",
                transform,
                new Color(0.12f, 0.12f, 0.14f, 0.6f),
                1.1f,
                0.7f);

            if (proceduralSmoke != null)
            {
                var main = proceduralSmoke.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);

                var shape = proceduralSmoke.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius * 0.7f;

                var emission = proceduralSmoke.emission;
                emission.enabled = true;
                emission.rateOverTime = 16f;

                proceduralSmoke.Play();
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!affectsRacers || other == null) return;

            CarController car = other.GetComponentInParent<CarController>();
            if (car == null || !car.isActiveAndEnabled || car.TargetBody == null) return;

            // Apply burn stumble & deceleration
            if (!burningCars.Contains(car))
            {
                burningCars.Add(car);
                ApplyFireBurn(car);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            CarController car = other.GetComponentInParent<CarController>();
            if (car != null)
            {
                burningCars.Remove(car);
            }
        }

        private void ApplyFireBurn(CarController car)
        {
            if (car == null || car.TargetBody == null) return;

            // Check if car has an active shield
            KartItemDefense defense = car.GetComponent<KartItemDefense>();
            if (defense != null && defense.IsShieldActive)
            {
                defense.TryBlockMajorCollision(transform.position, false, 8f);
                return;
            }

            // Stumble the car with a fiery burn torque and speed loss
            Vector3 slipTorque = Vector3.up * (Random.value > 0.5f ? burnTorque : -burnTorque);
            car.TargetBody.AddTorque(slipTorque, ForceMode.Impulse);

            // Speed loss
            Vector3 vel = car.TargetBody.linearVelocity;
            car.TargetBody.linearVelocity = vel * burnSpeedMultiplier;

            // Visual spark burst & sound
            KartParticleEffect.Burst(car.transform.position + Vector3.up * 0.4f, new Color(1f, 0.4f, 0.05f, 1f), 18);
            PoseKart.Audio.AudioManager.PlayBananaSlip(car.transform.position);
        }

        private void Update()
        {
            float elapsed = Time.time - spawnTime;
            if (elapsed >= duration - 0.75f)
            {
                // Soft fade out particle emissions
                if (proceduralFlames != null)
                {
                    var em = proceduralFlames.emission;
                    em.rateOverTime = Mathf.Lerp(em.rateOverTime.constant, 0f, Time.deltaTime * 3f);
                }
                if (proceduralSmoke != null)
                {
                    var em = proceduralSmoke.emission;
                    em.rateOverTime = Mathf.Lerp(em.rateOverTime.constant, 0f, Time.deltaTime * 3f);
                }
            }
        }
    }
}
