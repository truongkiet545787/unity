
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class PoseKartCinematicCamera : MonoBehaviour
{
    [Header("Target")]
    public Rigidbody targetRigidbody;

    [Header("Follow")]
    public float distance = 5.8f;
    public float height = 2.5f;
    public float lookHeight = 1.1f;
    public float lookAhead = 2f;
    public float smoothTime = 0.13f;
    public float rotationSpeed = 8f;

    [Header("Dynamic FOV")]
    public float normalFOV = 65f;
    public float highSpeedFOV = 82f;
    public float nitroFOV = 90f;
    public float maxSpeedKmh = 140f;
    public float fovResponse = 5f;

    [Header("Cinematic Effects")]
    public float maxTilt = 4f;
    public float fullTiltYawRate = 100f;
    public float tiltResponse = 5f;
    public float nitroPullback = 0.7f;
    public float nitroTransitionSpeed = 4f;

    private Camera cam;
    private Vector3 smoothPosition;
    private Vector3 followVelocity;

    private float previousYaw;
    private float currentTilt;
    private float nitroBlend;

    private bool nitroActive;
    private bool initialized;

    private float shakeRemaining;
    private float shakeDuration;
    private float shakeStrength;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    public void SetTarget(Rigidbody rb)
    {
        targetRigidbody = rb;
        initialized = false;
    }

    void LateUpdate()
    {
        if (targetRigidbody == null)
            return;

        Transform car = targetRigidbody.transform;
        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        Vector3 velocity = targetRigidbody.linearVelocity;
        velocity.y = 0f;

        float speed = velocity.magnitude * 3.6f;

        if (!initialized)
        {
            previousYaw = car.eulerAngles.y;
            smoothPosition = transform.position;
            initialized = true;
        }

        // Nitro transition
        nitroBlend = Mathf.MoveTowards(
            nitroBlend,
            nitroActive ? 1f : 0f,
            nitroTransitionSpeed * dt
        );

        // Camera follows behind the vehicle
        float currentDistance =
            distance + nitroPullback * nitroBlend;

        Vector3 desiredPosition =
            car.position
            - car.forward * currentDistance
            + Vector3.up * height;

        smoothPosition = Vector3.SmoothDamp(
            smoothPosition,
            desiredPosition,
            ref followVelocity,
            Mathf.Max(0.01f, smoothTime),
            Mathf.Infinity,
            dt
        );

        // Cinematic tilt during turns
        float yawDelta = Mathf.DeltaAngle(
            previousYaw,
            car.eulerAngles.y
        );

        float yawRate = yawDelta / dt;
        previousYaw = car.eulerAngles.y;

        float targetTilt = -Mathf.Clamp(
            yawRate / Mathf.Max(1f, fullTiltYawRate),
            -1f,
            1f
        ) * maxTilt;

        currentTilt = Mathf.Lerp(
            currentTilt,
            targetTilt,
            1f - Mathf.Exp(-tiltResponse * dt)
        );

        // Impact shake
        Vector3 shakeOffset = Vector3.zero;

        if (shakeRemaining > 0f)
        {
            float fade = shakeRemaining /
                Mathf.Max(0.01f, shakeDuration);

            float t = Time.time * 27f;

            shakeOffset = new Vector3(
                Mathf.PerlinNoise(t, 0f) * 2f - 1f,
                Mathf.PerlinNoise(0f, t) * 2f - 1f,
                0f
            ) * shakeStrength * fade;

            shakeRemaining -= dt;
        }

        transform.position = smoothPosition + shakeOffset;

        Vector3 lookPoint =
            car.position
            + Vector3.up * lookHeight
            + car.forward * lookAhead;

        Vector3 lookDirection =
            lookPoint - transform.position;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up
                ) * Quaternion.Euler(0f, 0f, currentTilt);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-rotationSpeed * dt)
            );
        }

        // Speed-based FOV
        float speedFactor = Mathf.Clamp01(
            speed / Mathf.Max(1f, maxSpeedKmh)
        );

        float speedFOV = Mathf.Lerp(
            normalFOV,
            highSpeedFOV,
            speedFactor
        );

        float desiredFOV = Mathf.Lerp(
            speedFOV,
            nitroFOV,
            nitroBlend
        );

        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView,
            desiredFOV,
            1f - Mathf.Exp(-fovResponse * dt)
        );
    }

    public void SetNitro(bool active)
    {
        nitroActive = active;
    }

    public void PlayImpact(float intensity)
    {
        shakeDuration = 0.2f;
        shakeRemaining = shakeDuration;

        shakeStrength = Mathf.Clamp01(intensity) * 0.14f;
    }
}
