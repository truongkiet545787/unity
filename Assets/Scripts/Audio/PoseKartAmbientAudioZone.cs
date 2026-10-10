using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Audio
{
    /// <summary>Reusable trigger-based ambience for coast, forest or village areas.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PoseKartAmbientAudioZone : MonoBehaviour
    {
        [SerializeField] private AudioClip ambienceClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.2f;
        [SerializeField, Min(0.05f)] private float fadeDuration = 1.2f;
        [SerializeField] private bool loop = true;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.35f;
        [SerializeField, Min(0.1f)] private float minDistance = 8f;
        [SerializeField, Min(1f)] private float maxDistance = 60f;
        private AudioSource source;
        private int listenersInside;
        private float targetVolume;

        private void Awake()
        {
            Collider zone = GetComponent<Collider>();
            zone.isTrigger = true;
            if (ambienceClip == null) return;
            source = gameObject.AddComponent<AudioSource>();
            source.name = "Ambient Zone Audio";
            source.clip = ambienceClip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        private void Update()
        {
            if (source == null) return;
            targetVolume = listenersInside > 0 ? volume : 0f;
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, Time.deltaTime / fadeDuration);
            if (listenersInside > 0 && !source.isPlaying) source.Play();
            if (listenersInside == 0 && source.volume <= 0.001f) source.Stop();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayerVehicle(other)) listenersInside++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayerVehicle(other)) listenersInside = Mathf.Max(0, listenersInside - 1);
        }

        private static bool IsPlayerVehicle(Collider other)
        {
            CarController car = other == null ? null : other.GetComponentInParent<CarController>();
            return car != null && (car.name.Equals("Buggy_Player", System.StringComparison.OrdinalIgnoreCase)
                || car.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
