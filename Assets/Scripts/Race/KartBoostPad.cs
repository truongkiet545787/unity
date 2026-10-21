using System.Collections.Generic;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class KartBoostPad : MonoBehaviour
    {
        [Header("Boost Settings")]
        [SerializeField] private bool jump;
        [SerializeField] private Vector3 landingPoint;

        [Header("Boost Tuning")]
        [SerializeField, Min(0f)] private float normalBoost = 1.5f;
        [SerializeField, Min(0f)] private float jumpBoost = 1.2f;
        [Tooltip("Instant velocity change in m/s, not the top-speed bonus.")]
        [SerializeField, Min(0f)] private float boostSecondParameter = 3f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 3f;

        private readonly Dictionary<CarController, float> cooldown =
            new Dictionary<CarController, float>();

        public void Configure(bool launch, Vector3 destination)
        {
            jump = launch;
            landingPoint = destination;
        }

        private void OnTriggerEnter(Collider other)
        {
            CarController car = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetComponentInParent<CarController>()
                : other.GetComponentInParent<CarController>();

            if (car == null || !car.ControlEnabled || !car.IsGrounded || car.TargetBody == null)
                return;

            if (Vector3.Dot(car.TargetBody.linearVelocity, transform.forward) < 2f)
                return;

            if (cooldown.TryGetValue(car, out float ready) && Time.time < ready)
                return;

            cooldown[car] = Time.time + cooldownSeconds;
            float duration = jump ? jumpBoost : normalBoost;

            // A pad is independent from the fuel-based Nitro mode.
            car.ApplyPadBoost(duration, boostSecondParameter);

            if (jump)
                car.LaunchStunt(landingPoint);

            KartParticleEffect.Burst(
                car.transform.position,
                new Color(0.1f, 0.9f, 1f, 1f),
                24);
        }
    }
}
