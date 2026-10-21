using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a short, three-shot race intro on the existing Main Camera, then
/// hands camera control back to PoseKartCinematicCamera before the countdown.
/// The race start/countdown must be coordinated by RaceManager.
/// </summary>
[RequireComponent(typeof(UnityEngine.Camera))]
[DisallowMultipleComponent]
[AddComponentMenu("PoseKart/Race Intro Camera")]
public sealed class PoseKartRaceIntroCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody playerCar;
    [SerializeField] private Rigidbody[] gridCars = new Rigidbody[4];
    [SerializeField] private PoseKartCinematicCamera chaseCamera;

    [Header("Shot Durations (seconds)")]
    [SerializeField, Min(0.1f)] private float heroShotDuration = 1.15f;
    [SerializeField, Min(0.1f)] private float gridShotDuration = 1.35f;
    [SerializeField, Min(0.1f)] private float rearShotDuration = 1.0f;
    [SerializeField, Min(0.1f)] private float chaseBlendDuration = 1.1f;

    private UnityEngine.Camera sceneCamera;
    private bool playing;

    public bool IsPlaying => playing;

    private void Awake()
    {
        sceneCamera = GetComponent<UnityEngine.Camera>();
        if (chaseCamera == null)
            chaseCamera = GetComponent<PoseKartCinematicCamera>();
    }

    /// <summary>
    /// Call from RaceManager's race-start coroutine BEFORE its existing countdown.
    /// The player/AI must already be positioned and their controls locked.
    /// </summary>
    public IEnumerator PlayIntro()
    {
        if (playing)
            yield break;

        if (sceneCamera == null)
            sceneCamera = GetComponent<UnityEngine.Camera>();
        if (chaseCamera == null)
            chaseCamera = GetComponent<PoseKartCinematicCamera>();

        if (playerCar == null || chaseCamera == null || sceneCamera == null)
        {
            Debug.LogWarning(
                "[PoseKartRaceIntroCamera] Missing Player Car, Chase Camera, or Camera. Skipping intro.", this);
            yield break;
        }

        playing = true;
        chaseCamera.enabled = false;

        // Cinematic cut: front-left hero shot, then a gentle camera move.
        Apply(EvaluateShot(0, 0f));
        yield return AnimateShot(0, heroShotDuration);

        // Cinematic cut: elevated view of the complete starting grid.
        Apply(EvaluateShot(1, 0f));
        yield return AnimateShot(1, gridShotDuration);

        // Cinematic cut: low rear three-quarter view of the player's car.
        Apply(EvaluateShot(2, 0f));
        yield return AnimateShot(2, rearShotDuration);

        // No cut here: blend smoothly into the existing chase camera pose.
        yield return BlendToChase(chaseBlendDuration);

        chaseCamera.SetTarget(playerCar); // Resets its follow initialization.
        chaseCamera.enabled = true;
        playing = false;
    }

    private IEnumerator AnimateShot(int shotIndex, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.1f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Apply(EvaluateShot(shotIndex, Smooth01(t)));
            yield return null;
        }

        Apply(EvaluateShot(shotIndex, 1f));
    }

    private IEnumerator BlendToChase(float duration)
    {
        duration = Mathf.Max(0.1f, duration);
        ShotPose start = new ShotPose(transform.position, transform.rotation, sceneCamera.fieldOfView);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Smooth01(Mathf.Clamp01(elapsed / duration));
            ShotPose end = EvaluateChasePose();
            Apply(new ShotPose(
                Vector3.Lerp(start.position, end.position, t),
                Quaternion.Slerp(start.rotation, end.rotation, t),
                Mathf.Lerp(start.fov, end.fov, t)));
            yield return null;
        }

        Apply(EvaluateChasePose());
    }

    private ShotPose EvaluateShot(int index, float t)
    {
        Vector3 player = playerCar.transform.position;
        Vector3 forward = FlatForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 position;
        Vector3 lookAt;
        float fov;

        switch (index)
        {
            case 0: // Front-left hero shot of the player's car.
                position = player + forward * Mathf.Lerp(3.6f, 2.7f, t)
                    - right * Mathf.Lerp(2.6f, 1.8f, t)
                    + Vector3.up * Mathf.Lerp(1.1f, 1.25f, t);
                lookAt = player + Vector3.up * 0.85f;
                fov = 50f;
                break;

            case 1: // Elevated rear-right view of all four grid positions.
                Vector3 center = GridCenter();
                position = center - forward * Mathf.Lerp(7.8f, 6.8f, t)
                    + right * Mathf.Lerp(4.8f, 4.1f, t)
                    + Vector3.up * Mathf.Lerp(5.2f, 5.7f, t);
                lookAt = center + forward * 1f + Vector3.up * 0.65f;
                fov = 59f;
                break;

            default: // Low rear three-quarter shot before returning to chase.
                position = player - forward * Mathf.Lerp(3.1f, 3.7f, t)
                    + right * Mathf.Lerp(1.6f, 0f, t)
                    + Vector3.up * Mathf.Lerp(1.45f, 1.85f, t);
                lookAt = player + forward * Mathf.Lerp(1.1f, 1.6f, t)
                    + Vector3.up * 0.9f;
                fov = Mathf.Lerp(58f, 62f, t);
                break;
        }

        return LookPose(position, lookAt, fov);
    }

    private ShotPose EvaluateChasePose()
    {
        Vector3 forward = FlatForward();
        Vector3 player = playerCar.transform.position;
        Vector3 position = player - forward * chaseCamera.distance
            + Vector3.up * chaseCamera.height;
        Vector3 lookAt = player + Vector3.up * chaseCamera.lookHeight
            + forward * chaseCamera.lookAhead;
        return LookPose(position, lookAt, chaseCamera.normalFOV);
    }

    private Vector3 FlatForward()
    {
        Vector3 forward = Vector3.ProjectOnPlane(playerCar.transform.forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private Vector3 GridCenter()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        if (gridCars != null)
        {
            foreach (Rigidbody car in gridCars)
            {
                if (car == null) continue;
                sum += car.transform.position;
                count++;
            }
        }
        return count > 0 ? sum / count : playerCar.transform.position;
    }

    private static ShotPose LookPose(Vector3 position, Vector3 lookAt, float fov)
    {
        Vector3 direction = lookAt - position;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : Quaternion.identity;
        return new ShotPose(position, rotation, fov);
    }

    private void Apply(ShotPose pose)
    {
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        sceneCamera.fieldOfView = pose.fov;
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private struct ShotPose
    {
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly float fov;

        public ShotPose(Vector3 position, Quaternion rotation, float fov)
        {
            this.position = position;
            this.rotation = rotation;
            this.fov = fov;
        }
    }
}
