using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Audio
{
    /// <summary>
    /// One lightweight ambience manager per loaded scene. It creates sources only when
    /// clips are assigned, so empty projects do not accumulate silent AudioSources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartEnvironmentAudioManager : MonoBehaviour
    {
        [Header("Global island ambience")]
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

        [Header("Occasional tropical birds")]
        [SerializeField] private AudioClip[] birdClips;
        [SerializeField, Min(1f)] private float minimumBirdDelay = 12f;
        [SerializeField, Min(1f)] private float maximumBirdDelay = 28f;
        [SerializeField, Range(0f, 1f)] private float birdVolume = 0.16f;
        [SerializeField, Min(1f)] private float birdMinDistance = 8f;
        [SerializeField, Min(1f)] private float birdMaxDistance = 55f;
        [SerializeField] private Transform birdAnchor;

        private AudioSource oceanSource;
        private AudioSource windSource;
        private AudioSource palmSource;
        private AudioSource birdSource;
        private float nextBirdTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureForLoadedScene()
        {
            if (FindFirstObjectByType<PoseKartEnvironmentAudioManager>() != null) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            GameObject root = new GameObject("PoseKart Environment Audio");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<PoseKartEnvironmentAudioManager>();
        }

        private void Awake()
        {
            oceanSource = CreateLoop("Ocean Waves", oceanWavesLoop, oceanVolume, oceanAnchor, oceanMinDistance, oceanMaxDistance);
            windSource = CreateLoop("Tropical Wind", tropicalWindLoop, windVolume, null, 30f, 180f);
            palmSource = CreateLoop("Palm Leaves", palmLeavesLoop, palmVolume, forestAnchor, 15f, 90f);
            if (birdClips != null && birdClips.Length > 0)
                birdSource = CreateSource("Tropical Birds", birdAnchor, 1f, birdMinDistance, birdMaxDistance, false);
            ScheduleNextBird();
        }

        private void Update()
        {
            if (birdSource == null || birdClips == null || birdClips.Length == 0) return;
            if (Time.time < nextBirdTime || birdSource.isPlaying) return;
            AudioClip clip = birdClips[Random.Range(0, birdClips.Length)];
            if (clip != null)
            {
                birdSource.pitch = Random.Range(0.92f, 1.08f);
                birdSource.PlayOneShot(clip, birdVolume * Random.Range(0.75f, 1f));
            }
            ScheduleNextBird();
        }

        private void ScheduleNextBird()
        {
            nextBirdTime = Time.time + Random.Range(minimumBirdDelay, Mathf.Max(minimumBirdDelay, maximumBirdDelay));
        }

        private AudioSource CreateLoop(string sourceName, AudioClip clip, float volume, Transform anchor, float minDistance, float maxDistance)
        {
            if (clip == null) return null;
            AudioSource source = CreateSource(sourceName, anchor, anchor == null ? 0.15f : 1f, minDistance, maxDistance, true);
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.Play();
            return source;
        }

        private AudioSource CreateSource(string sourceName, Transform anchor, float spatialBlend, float minDistance, float maxDistance, bool loop)
        {
            GameObject host = anchor == null ? gameObject : anchor.gameObject;
            AudioSource source = host.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.spatialize = spatialBlend > 0.8f;
            return source;
        }
    }
}
