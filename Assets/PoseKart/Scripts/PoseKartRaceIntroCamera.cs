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

    [Header("Scene Shot Cameras (optional)")]
    [Tooltip("Camera mẫu cho shot Hero. Main Camera sẽ copy vị trí, rotation và FOV từ camera này.")]
    [SerializeField] private UnityEngine.Camera heroShotCamera;
    [Tooltip("Camera mẫu cho shot Grid. Main Camera sẽ copy vị trí, rotation và FOV từ camera này.")]
    [SerializeField] private UnityEngine.Camera gridShotCamera;
    [Tooltip("Camera mẫu cho shot Rear. Main Camera sẽ copy vị trí, rotation và FOV từ camera này.")]
    [SerializeField] private UnityEngine.Camera rearShotCamera;

    [Header("Manual Shot Overrides")]
    [Tooltip("Fallback khi camera mẫu tương ứng chưa được gán. Bật để tự chỉnh vị trí/góc quay intro trong Inspector.")]
    [SerializeField] private bool useManualShotOverrides = false;
    [Tooltip("Offset local theo hướng xe: X = phải, Y = cao, Z = phía trước.")]
    [SerializeField] private Vector3 heroShotPositionOffset = new Vector3(-2.6f, 1.1f, 3.6f);
    [SerializeField] private Vector3 heroShotRotationEuler = new Vector3(15f, 180f, 0f);
    [SerializeField, Min(1f)] private float heroShotFov = 50f;
    [Tooltip("Offset local theo hướng xe, lấy tâm grid làm gốc.")]
    [SerializeField] private Vector3 gridShotPositionOffset = new Vector3(4.8f, 5.2f, -7.8f);
    [SerializeField] private Vector3 gridShotRotationEuler = new Vector3(25f, 145f, 0f);
    [SerializeField, Min(1f)] private float gridShotFov = 59f;
    [Tooltip("Offset local theo hướng xe: dùng offset âm Z để đứng phía sau xe.")]
    [SerializeField] private Vector3 rearShotPositionOffset = new Vector3(1.6f, 1.45f, -3.1f);
    [SerializeField] private Vector3 rearShotRotationEuler = new Vector3(12f, 0f, 0f);
    [SerializeField, Min(1f)] private float rearShotFov = 58f;

    private UnityEngine.Camera sceneCamera;
    private bool playing;

    public bool IsPlaying => playing;

    private void Awake()
    {
        sceneCamera = GetComponent<UnityEngine.Camera>();
        if (chaseCamera == null)
            chaseCamera = GetComponent<PoseKartCinematicCamera>();

        if (chaseCamera != null)
            chaseCamera.SetIntroHandedOff(false);
    }

    /// <summary>
    /// Call from RaceManager's race-start coroutine BEFORE its existing countdown.
    /// The player/AI must already be positioned and their controls locked.
    /// </summary>
    public IEnumerator PlayIntro()
    {
        if (playing)
            yield break;

        if (chaseCamera != null)
            chaseCamera.SetIntroHandedOff(false);

        if (sceneCamera == null)
            sceneCamera = GetComponent<UnityEngine.Camera>();
        if (chaseCamera == null)
            chaseCamera = GetComponent<PoseKartCinematicCamera>();

        if (playerCar == null || chaseCamera == null || sceneCamera == null)
        {
            if (chaseCamera != null)
                chaseCamera.SetIntroHandedOff(true);

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
        chaseCamera.SetIntroHandedOff(true);
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

        UnityEngine.Camera sceneShotCamera = GetSceneShotCamera(index);
        if (sceneShotCamera != null)
        {
            Transform shotTransform = sceneShotCamera.transform;
            return new ShotPose(
                shotTransform.position,
                shotTransform.rotation,
                sceneShotCamera.fieldOfView);
        }

        if (useManualShotOverrides)
        {
            Vector3 origin = index == 1 ? GridCenter() : player;
            Vector3 positionOffset;
            Vector3 rotationEuler;
            float manualFov;

            switch (index)
            {
                case 0:
                    positionOffset = heroShotPositionOffset;
                    rotationEuler = heroShotRotationEuler;
                    manualFov = heroShotFov;
                    break;
                case 1:
                    positionOffset = gridShotPositionOffset;
                    rotationEuler = gridShotRotationEuler;
                    manualFov = gridShotFov;
                    break;
                default:
                    positionOffset = rearShotPositionOffset;
                    rotationEuler = rearShotRotationEuler;
                    manualFov = rearShotFov;
                    break;
            }

            Vector3 manualPosition = origin
                + right * positionOffset.x
                + Vector3.up * positionOffset.y
                + forward * positionOffset.z;

            Quaternion vehicleBasis =
                Quaternion.LookRotation(forward, Vector3.up);

            return new ShotPose(
                manualPosition,
                vehicleBasis * Quaternion.Euler(rotationEuler),
                manualFov);
        }

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

    private UnityEngine.Camera GetSceneShotCamera(int index)
    {
        switch (index)
        {
            case 0:
                return heroShotCamera;
            case 1:
                return gridShotCamera;
            default:
                return rearShotCamera;
        }
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
    public void SetPlayerCar(Rigidbody newPlayer)
    {
        playerCar = newPlayer;
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
