using PoseKart.Vehicle;
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
        [Header("Nitro Pickup")]
        [Tooltip("Nitro charge units added to the car. The current race cars use 2.5 as a full bar.")]
        [SerializeField, Min(0.01f)] private float nitroAmount = 0.75f;
        [SerializeField, Min(0f)] private float respawnSeconds = 6f;
        [SerializeField] private bool allowAIVehicles = true;
        [SerializeField] private bool playPickupAudio = true;
        [Tooltip("Uses the orange Nitro pickup sound instead of the blue pickup sound.")]
        [SerializeField] private bool orangePickupSound;

        [Header("Trigger")]
        [SerializeField] private Vector3 triggerSize = new Vector3(1.4f, 1.8f, 1.4f);
        [SerializeField] private Vector3 triggerCenter = new Vector3(0f, 0.9f, 0f);

        [Header("Runtime Debug")]
        [SerializeField] private bool available = true;
        [SerializeField] private string lastCollector = "None";

        private Collider trigger;
        private Renderer[] visuals;
        private ParticleSystem[] particles;

        public float NitroAmount => nitroAmount;
        public bool IsAvailable => available;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (trigger == null)
                trigger = gameObject.AddComponent<BoxCollider>();

            trigger.isTrigger = true;
            ApplyTriggerShape();
            visuals = GetComponentsInChildren<Renderer>(true);
            particles = GetComponentsInChildren<ParticleSystem>(true);
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

        private void TryCollect(Collider other)
        {
            if (!available || trigger == null || !trigger.enabled || other == null)
                return;

            Rigidbody body = other.attachedRigidbody;
            CarController car = body != null
                ? body.GetComponentInParent<CarController>()
                : other.GetComponentInParent<CarController>();
            if (car == null || !car.ControlEnabled)
                return;

            if (!allowAIVehicles)
            {
                RaceParticipant participant = car.GetComponent<RaceParticipant>();
                if (participant != null && !participant.IsPlayer)
                    return;
            }

            float before = car.StoredNitro;
            car.AddNitro(nitroAmount);
            if (car.StoredNitro <= before + 0.0001f)
                return; // Full bars do not consume a pickup.

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
            ApplyTriggerShape();
        }
    }
}
