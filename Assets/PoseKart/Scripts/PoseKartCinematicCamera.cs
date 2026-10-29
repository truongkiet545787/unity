using UnityEngine;
using System.Collections; // Đã thêm thư viện này cho IEnumerator
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
[AddComponentMenu("PoseKart/Cinematic Camera")]
public sealed class PoseKartCinematicCamera : MonoBehaviour
{
    [SerializeField] private Volume raceVolume;
    [SerializeField, Range(0f, 0.3f)] private float nitroMotionBlur = 0.14f;
    [SerializeField, Range(0.1f, 20f)] private float motionBlurResponse = 8f;

    private MotionBlur motionBlur;
    [Header("Asphalt Intro - Flash Transitions")]
    [SerializeField] private CanvasGroup flashGroup;

    [SerializeField, Range(0f, 1f)]
    private float flashIntensity = 0.85f;

    [SerializeField, Min(0.01f)]
    private float flashInDuration = 0.065f;

    [SerializeField, Min(0.01f)]
    private float flashOutDuration = 0.20f;

    [Header("Shot Camera Punch")]
    [SerializeField, Range(0f, 8f)]
    private float shotFovPunch = 3.5f;

    [Header("Transition Audio (Optional)")]
    [SerializeField] private AudioSource transitionAudio;
    [SerializeField] private AudioClip whooshClip;

    // ĐÃ XÓA KHỐI KHAI BÁO BỊ TRÙNG LẶP Ở ĐÂY

    [Header("Target")]
    public Rigidbody targetRigidbody;

    [Header("First Person View")]
    [Tooltip("Optional fallback anchor for offline use. In multiplayer the local owner supplies the vehicle anchor at runtime.")]
    [SerializeField] private Transform firstPersonAnchor;
    [SerializeField] private KeyCode viewToggleKey = KeyCode.C;
    [SerializeField, Min(0.1f)] private float viewTransitionSpeed = 12f;
    [SerializeField, Range(35f, 105f)] private float firstPersonFov = 72f;
    [SerializeField, Range(0f, 12f)] private float firstPersonNitroFovPunch = 3f;

    [Header("Follow")]
    public float distance = 4.2f;
    public float height = 2.25f;
    public float lookHeight = 0.95f;
    public float lookAhead = 2.6f;
    public float smoothTime = 0.12f;
    public float rotationSpeed = 10f;

    [Header("Asphalt Chase Feel")]
    public float speedPullback = 0.45f;
    public float speedLift = 0.12f;
    public float speedLookAhead = 1.4f;
    public float accelerationLookAhead = 0.35f;
    public float steeringOffset = 0.18f;
    public float steeringOffsetResponse = 8f;
    public float steeringTilt = 1.5f;
    public float driftTilt = 2.2f;

    [Header("Dynamic FOV")]
    public float normalFOV = 62f;
    public float highSpeedFOV = 73f;
    public float nitroFOV = 81f;
    public float maxSpeedKmh = 140f;
    public float fovResponse = 6f;
    public float nitroPullback = 0.35f;
    public float nitroTransitionSpeed = 5f;

    [Header("Cinematic Effects")]
    public float maxTilt = 2f;
    public float fullTiltYawRate = 100f;
    public float tiltResponse = 5f;
    public float landingShake = 0.1f;
    public float boostShake = 0.025f;
    public float shakeFrequency = 27f;

    [Header("NEW - Nitro Camera Punch")]
    [Tooltip("Extra FOV immediately after Nitro ignition.")]
    [Range(0f, 12f)]
    public float nitroFovPunch = 5f;

    [Tooltip("Temporary backward camera kick when Nitro starts.")]
    [Range(0f, 1.5f)]
    public float nitroKickPullback = 0.55f;

    [Range(0.1f, 12f)]
    public float nitroKickRecovery = 3.5f;

    [Header("NEW - Acceleration / Braking")]
    [Range(0f, 0.8f)]
    public float accelerationPullback = 0.22f;

    [Range(0f, 0.8f)]
    public float brakingPushIn = 0.18f;

    [Range(0f, 1f)]
    public float accelerationPitch = 0.16f;

    [Range(0.1f, 15f)]
    public float accelerationResponse = 4f;

    [Header("NEW - Drift Cinematography")]
    [Tooltip("Additional sideways framing during a drift.")]
    [Range(0f, 1.5f)]
    public float driftCameraSlide = 0.55f;

    [Tooltip("Look slightly toward the outside of the drift.")]
    [Range(0f, 2f)]
    public float driftLookOffset = 0.65f;

    [Tooltip("Blend a small amount of movement direction into camera heading.")]
    [Range(0f, 0.4f)]
    public float driftVelocityInfluence = 0.16f;

    [Range(0.1f, 15f)]
    public float driftResponse = 4f;

    [Header("NEW - Jump / Landing")]
    [Range(0f, 2f)]
    public float airborneExtraHeight = 0.5f;

    [Range(0f, 2f)]
    public float airborneExtraDistance = 0.45f;

    [Range(0f, 1f)]
    public float airborneLookDown = 0.22f;

    [Range(0f, 1f)]
    public float landingCameraDip = 0.18f;

    [Range(0.1f, 15f)]
    public float landingRecovery = 5f;

    [Header("NEW - Speed Vibration")]
    [Tooltip("Very subtle camera vibration at high speed.")]
    [Range(0f, 0.025f)]
    public float highSpeedVibration = 0.006f;

    [Range(0f, 0.04f)]
    public float nitroVibration = 0.012f;

    [Range(0f, 0.04f)]
    public float driftVibration = 0.006f;

    [Header("NEW - Camera Limits")]
    [Range(0f, 12f)]
    public float maximumRoll = 5.5f;

    [Range(0.01f, 1f)]
    public float minimumFollowSmoothTime = 0.045f;

    [Header("Airborne Stunt Cinematography")]
    [Tooltip("Extra FOV boost when performing stunts (Flat Spin, Barrel Roll, Flip).")]
    public float stuntFovBoost = 9.0f;

    [Tooltip("Extra camera distance pullback during stunts for wider perspective.")]
    public float stuntPullback = 0.85f;

    [Tooltip("Camera height lift during stunts.")]
    public float stuntHeightLift = 0.40f;

    private Camera cam;
    private PoseKart.Vehicle.CarController targetCar;
    private PoseKart.Stunts.PoseKartStuntController targetStuntController;

    private Vector3 followVelocity;
    private Vector3 stableForward = Vector3.forward;
    private Vector3 previousVelocity;

    private float previousYaw;
    private float currentTilt;
    private float currentLateralOffset;
    private float currentDriftSlide;
    private float currentDriftLook;
    private float currentAcceleration;

    private float nitroBlend;
    private float nitroKick;
    private float landingDip;
    private float baseFov;

    private bool nitroActive;
    private bool nitroFallbackState;
    private bool initialized;
    private bool previousAirborne;
    private bool previousNitro;

    private bool firstPersonView;
    private bool localOwnerInputEnabled = true;
    private bool introHandedOff = true;
    private float firstPersonBlend = 1f;

    private float shakeRemaining;
    private float shakeDuration;
    private float shakeStrength;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (cam != null)
            baseFov = cam.fieldOfView;
        if (raceVolume != null && raceVolume.profile != null &&
    raceVolume.profile.TryGet(out motionBlur))
        {
            motionBlur.active = true;
            motionBlur.intensity.overrideState = true;
            motionBlur.intensity.value = 0f;
        }
    }

    private void Update()
    {
        if (!localOwnerInputEnabled || !introHandedOff || firstPersonAnchor == null)
            return;

        if (Keyboard.current != null &&
    Keyboard.current.cKey.wasPressedThisFrame)
        {
            firstPersonView = !firstPersonView;

            if (firstPersonView)
                firstPersonBlend = 0f;
        }
    }

    public void SetLocalOwner(bool isLocalOwner)
    {
        localOwnerInputEnabled = isLocalOwner;

        if (!isLocalOwner)
        {
            firstPersonView = false;
            firstPersonBlend = 1f;
        }
    }

    public void SetFirstPersonAnchor(Transform anchor)
    {
        firstPersonAnchor = anchor;

        if (firstPersonAnchor == null)
        {
            firstPersonView = false;
            firstPersonBlend = 1f;
        }
    }

    public void SetIntroHandedOff(bool handedOff)
    {
        introHandedOff = handedOff;

        if (!handedOff)
        {
            firstPersonView = false;
            firstPersonBlend = 1f;
        }
    }

    public void SetTarget(Rigidbody rb)
    {
        targetRigidbody = rb;

        targetCar = rb != null
            ? rb.GetComponent<PoseKart.Vehicle.CarController>()
            : null;

        targetStuntController = rb != null
            ? rb.GetComponent<PoseKart.Stunts.PoseKartStuntController>()
            : null;

        initialized = false;
        followVelocity = Vector3.zero;
        currentTilt = 0f;
        currentLateralOffset = 0f;
        currentDriftSlide = 0f;
        currentDriftLook = 0f;
        currentAcceleration = 0f;
        nitroBlend = 0f;
        nitroKick = 0f;
        landingDip = 0f;

        shakeRemaining = 0f;
        shakeDuration = 0f;
        shakeStrength = 0f;
    }

    private void LateUpdate()
    {
        if (targetRigidbody == null)
            return;

        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        if (cam == null)
            cam = GetComponent<Camera>();

        if (targetCar == null)
        {
            targetCar =
                targetRigidbody.GetComponent<PoseKart.Vehicle.CarController>();
        }

        if (targetStuntController == null)
        {
            targetStuntController =
                targetRigidbody.GetComponent<PoseKart.Stunts.PoseKartStuntController>();
        }

        bool isStuntActive = targetStuntController != null && targetStuntController.IsStuntActive;

        Transform car = targetRigidbody.transform;

        Vector3 velocity = targetRigidbody.linearVelocity;
        Vector3 planarVelocity =
            Vector3.ProjectOnPlane(velocity, Vector3.up);

        float speed = planarVelocity.magnitude * 3.6f;

        float speedFactor = Mathf.Clamp01(
            speed / Mathf.Max(1f, maxSpeedKmh)
        );

        float steerInput = targetCar != null
            ? Mathf.Clamp(targetCar.SteeringInput, -1f, 1f)
            : 0f;

        bool drifting = targetCar != null && targetCar.IsDrifting;
        bool airborne = targetCar != null && targetCar.IsAirAction;

        nitroActive = targetCar != null
    ? targetCar.IsBoosting
    : nitroFallbackState;

        UpdateNitroMotionBlur(dt);

        // The chase camera must not overwrite the first-person anchor pose.
        // Camera shake remains available through KartCollisionCameraShake and
        // the local cinematic shake is applied by UpdateFirstPersonCamera().
        if (firstPersonView && firstPersonAnchor != null)
        {
            UpdateFirstPersonCamera(dt);
            return;
        }

        // --------------------------------------------------
        // 1. INITIALIZATION
        // --------------------------------------------------

        if (!initialized)
        {
            Vector3 initialHeading =
                Vector3.ProjectOnPlane(car.forward, Vector3.up);

            if (initialHeading.sqrMagnitude < 0.001f)
                initialHeading = Vector3.forward;

            stableForward = initialHeading.normalized;

            previousYaw = Mathf.Atan2(
                stableForward.x,
                stableForward.z
            ) * Mathf.Rad2Deg;

            previousVelocity = velocity;

            previousAirborne = airborne;
            previousNitro = nitroActive;

            nitroBlend = nitroActive ? 1f : 0f;

            initialized = true;
        }

        // --------------------------------------------------
        // 2. ACCELERATION RESPONSE
        // --------------------------------------------------

        Vector3 acceleration = (velocity - previousVelocity) / dt;

        float verticalImpactSpeed =
            Mathf.Abs(velocity.y - previousVelocity.y);

        previousVelocity = velocity;

        float rawAcceleration =
            Vector3.Dot(acceleration, stableForward);

        float normalizedAcceleration = Mathf.Clamp(
            rawAcceleration / 12f,
            -1f,
            1f
        );

        currentAcceleration = Mathf.Lerp(
            currentAcceleration,
            normalizedAcceleration,
            ExpFactor(accelerationResponse, dt)
        );

        // --------------------------------------------------
        // 3. STABLE HEADING
        // --------------------------------------------------

        Vector3 carHeading =
            Vector3.ProjectOnPlane(car.forward, Vector3.up);

        if (carHeading.sqrMagnitude < 0.001f)
            carHeading = stableForward;

        carHeading.Normalize();

        Vector3 desiredHeading = carHeading;

        if (isStuntActive && planarVelocity.sqrMagnitude > 1f)
        {
            // Decouple camera heading during aerial stunt; follow flight path with stable horizon
            desiredHeading = planarVelocity.normalized;
        }
        else if (drifting &&
            planarVelocity.sqrMagnitude > 4f &&
            Vector3.Dot(carHeading, planarVelocity.normalized) > 0.25f)
        {
            desiredHeading = Vector3.Slerp(
                carHeading,
                planarVelocity.normalized,
                driftVelocityInfluence
            ).normalized;
        }

        float headingResponse = airborne
            ? rotationSpeed * 0.55f
            : rotationSpeed;

        stableForward = Vector3.Slerp(
            stableForward,
            desiredHeading,
            ExpFactor(headingResponse, dt)
        ).normalized;

        Vector3 flatForward = stableForward;

        Vector3 flatRight = new Vector3(
            flatForward.z,
            0f,
            -flatForward.x
        );

        // ĐÃ SỬA: ĐƯA HÀM FlashCut() BỊ KẸT Ở ĐÂY RA NGOÀI LATEUPDATE

        // --------------------------------------------------
        // 4. NITRO IGNITION PUNCH
        // --------------------------------------------------

        if (!previousNitro && nitroActive)
        {
            nitroKick = 1f;

            TriggerShake(
                boostShake,
                0.14f
            );
        }

        previousNitro = nitroActive;

        nitroBlend = Mathf.MoveTowards(
            nitroBlend,
            nitroActive ? 1f : 0f,
            Mathf.Max(0.01f, nitroTransitionSpeed) * dt
        );

        nitroKick = Mathf.MoveTowards(
            nitroKick,
            0f,
            Mathf.Max(0.01f, nitroKickRecovery) * dt
        );

        // --------------------------------------------------
        // 5. DRIFT FRAMING
        // --------------------------------------------------

        float targetSteeringOffset =
            -steerInput *
            steeringOffset *
            (0.45f + speedFactor * 0.55f);

        currentLateralOffset = Mathf.Lerp(
            currentLateralOffset,
            targetSteeringOffset,
            ExpFactor(steeringOffsetResponse, dt)
        );

        float targetDriftSlide = drifting
            ? -steerInput * driftCameraSlide
            : 0f;

        float targetDriftLook = drifting
            ? -steerInput * driftLookOffset
            : 0f;

        currentDriftSlide = Mathf.Lerp(
            currentDriftSlide,
            targetDriftSlide,
            ExpFactor(driftResponse, dt)
        );

        currentDriftLook = Mathf.Lerp(
            currentDriftLook,
            targetDriftLook,
            ExpFactor(driftResponse, dt)
        );

        // --------------------------------------------------
        // 6. CINEMATIC DISTANCE / HEIGHT
        // --------------------------------------------------

        float accelerationKick =
            Mathf.Max(0f, currentAcceleration);

        float brakingKick =
            Mathf.Max(0f, -currentAcceleration);

        float currentDistance =
            distance +
            speedPullback * speedFactor +
            nitroPullback * nitroBlend +
            nitroKickPullback * nitroKick +
            accelerationPullback * accelerationKick -
            brakingPushIn * brakingKick;

        if (airborne)
            currentDistance += airborneExtraDistance;

        if (isStuntActive)
            currentDistance += stuntPullback;

        currentDistance = Mathf.Max(1f, currentDistance);

        float currentHeight =
            height +
            speedLift * speedFactor +
            accelerationPitch * accelerationKick -
            landingDip;

        if (airborne)
            currentHeight += airborneExtraHeight;

        if (isStuntActive)
            currentHeight += stuntHeightLift;

        float dynamicLookAhead =
            lookAhead +
            speedLookAhead * speedFactor +
            accelerationLookAhead * accelerationKick +
            nitroBlend * speedLookAhead * 0.35f;

        // --------------------------------------------------
        // 7. LANDING IMPACT
        // --------------------------------------------------

        if (previousAirborne && !airborne)
        {
            landingDip = landingCameraDip;

            float landingIntensity = Mathf.Clamp01(
                verticalImpactSpeed / 8f
            );

            float extraShake = (targetStuntController != null &&
                targetStuntController.LastLandingQuality == PoseKart.Stunts.LandingQuality.CrashLanding)
                ? 0.15f
                : 0f;

            TriggerShake(
                (landingShake + extraShake) * Mathf.Lerp(
                    0.7f,
                    1.3f,
                    landingIntensity
                ),
                0.2f
            );
        }

        previousAirborne = airborne;

        landingDip = Mathf.MoveTowards(
            landingDip,
            0f,
            Mathf.Max(0.01f, landingRecovery) * dt
        );

        // --------------------------------------------------
        // 8. CAMERA FOLLOW POSITION
        // --------------------------------------------------

        Vector3 desiredPosition =
            car.position -
            flatForward * currentDistance +
            flatRight * (
                currentLateralOffset +
                currentDriftSlide
            ) +
            Vector3.up * currentHeight;

        float actualSmoothTime = Mathf.Max(
            minimumFollowSmoothTime,
            smoothTime
        );

        Vector3 cameraPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            actualSmoothTime,
            Mathf.Infinity,
            dt
        );

        // --------------------------------------------------
        // 9. DYNAMIC CAMERA ROLL
        // --------------------------------------------------

        float stableYaw = Mathf.Atan2(
            flatForward.x,
            flatForward.z
        ) * Mathf.Rad2Deg;

        float yawDelta = Mathf.DeltaAngle(
            previousYaw,
            stableYaw
        );

        float yawRate = yawDelta / dt;

        previousYaw = stableYaw;

        float yawTilt =
            -Mathf.Clamp(
                yawRate / Mathf.Max(1f, fullTiltYawRate),
                -1f,
                1f
            ) * maxTilt;

        float steeringRoll =
            -steerInput * steeringTilt;

        float driftRoll = drifting
            ? -steerInput * driftTilt
            : 0f;

        float targetRoll = Mathf.Clamp(
            yawTilt + steeringRoll + driftRoll,
            -maximumRoll,
            maximumRoll
        );

        currentTilt = Mathf.Lerp(
            currentTilt,
            targetRoll,
            ExpFactor(tiltResponse, dt)
        );

        // --------------------------------------------------
        // 10. SHAKE + SPEED VIBRATION
        // --------------------------------------------------

        Vector3 shakeOffset = Vector3.zero;

        if (shakeRemaining > 0f)
        {
            float fade = Mathf.Clamp01(
                shakeRemaining /
                Mathf.Max(0.01f, shakeDuration)
            );

            float t = Time.time *
                Mathf.Max(1f, shakeFrequency);

            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(t, 0.17f) * 2f - 1f,
                Mathf.PerlinNoise(0.43f, t) * 2f - 1f,
                0f
            );

            shakeOffset += noise * shakeStrength * fade;

            shakeRemaining -= dt;

            if (shakeRemaining <= 0f)
            {
                shakeRemaining = 0f;
                shakeDuration = 0f;
                shakeStrength = 0f;
            }
        }

        float vibrationStrength =
            highSpeedVibration * speedFactor * speedFactor +
            nitroVibration * nitroBlend +
            (drifting ? driftVibration : 0f);

        if (vibrationStrength > 0f)
        {
            float t = Time.time * shakeFrequency * 1.7f;

            shakeOffset += new Vector3(
                Mathf.PerlinNoise(t, 12.3f) * 2f - 1f,
                Mathf.PerlinNoise(18.7f, t) * 2f - 1f,
                0f
            ) * vibrationStrength;
        }

        transform.position =
            cameraPosition +
            transform.right * shakeOffset.x +
            transform.up * shakeOffset.y;

        // --------------------------------------------------
        // 11. LOOK TARGET
        // --------------------------------------------------

        Vector3 lookPoint =
            car.position +
            Vector3.up * lookHeight +
            flatForward * dynamicLookAhead +
            flatRight * currentDriftLook;

        if (airborne)
            lookPoint += Vector3.down * airborneLookDown;

        Vector3 lookDirection =
            lookPoint - transform.position;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up
                ) *
                Quaternion.Euler(0f, 0f, currentTilt);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                ExpFactor(rotationSpeed, dt)
            );
        }

        // --------------------------------------------------
        // 12. FOV + NITRO FLASH PUNCH
        // --------------------------------------------------

        float speedFov = Mathf.Lerp(
            normalFOV,
            highSpeedFOV,
            speedFactor
        );

        float desiredFov = Mathf.Lerp(
            speedFov,
            nitroFOV,
            nitroBlend
        );

        desiredFov += nitroFovPunch * nitroKick;

        if (isStuntActive)
            desiredFov += stuntFovBoost;

        desiredFov = Mathf.Clamp(
            desiredFov,
            35f,
            105f
        );

        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView,
            desiredFov,
            ExpFactor(fovResponse, dt)
        );
    }

    private void UpdateFirstPersonCamera(float dt)
    {
        if (firstPersonBlend < 1f)
        {
            firstPersonBlend = Mathf.MoveTowards(
                firstPersonBlend,
                1f,
                Mathf.Max(0.1f, viewTransitionSpeed) * dt
            );

            transform.position = Vector3.Lerp(
                transform.position,
                firstPersonAnchor.position,
                firstPersonBlend
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                firstPersonAnchor.rotation,
                firstPersonBlend
            );
        }
        else
        {
            // Once the transition is complete, follow the anchor exactly.
            // Continuous Lerp here would make the camera lag behind at speed.
            transform.SetPositionAndRotation(
                firstPersonAnchor.position,
                firstPersonAnchor.rotation
            );
        }

        Vector3 shakeOffset = Vector3.zero;
        if (shakeRemaining > 0f)
        {
            float fade = Mathf.Clamp01(
                shakeRemaining / Mathf.Max(0.01f, shakeDuration)
            );
            float t = Time.time * Mathf.Max(1f, shakeFrequency);
            shakeOffset = new Vector3(
                Mathf.PerlinNoise(t, 0.17f) * 2f - 1f,
                Mathf.PerlinNoise(0.43f, t) * 2f - 1f,
                0f
            ) * shakeStrength * fade;
            shakeRemaining -= dt;

            if (shakeRemaining <= 0f)
            {
                shakeRemaining = 0f;
                shakeDuration = 0f;
                shakeStrength = 0f;
            }
        }

        transform.position += transform.right * shakeOffset.x
            + transform.up * shakeOffset.y;

        float desiredFov = firstPersonFov
            + (nitroActive ? firstPersonNitroFovPunch : 0f);
        desiredFov = Mathf.Clamp(desiredFov, 35f, 105f);

        if (cam != null)
        {
            cam.fieldOfView = Mathf.Lerp(
                cam.fieldOfView,
                desiredFov,
                ExpFactor(fovResponse, dt)
            );
        }
    }

    private static float ExpFactor(float response, float dt)
    {
        return 1f - Mathf.Exp(
            -Mathf.Max(0.01f, response) * dt
        );
    }
    private void UpdateNitroMotionBlur(float dt)
    {
        if (motionBlur == null)
            return;

        float targetIntensity = nitroActive ? nitroMotionBlur : 0f;

        motionBlur.intensity.value = Mathf.MoveTowards(
            motionBlur.intensity.value,
            targetIntensity,
            motionBlurResponse * dt);
    }
    private void TriggerShake(float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f)
            return;

        shakeDuration = duration;
        shakeRemaining = duration;

        shakeStrength = Mathf.Max(
            shakeStrength,
            strength
        );
    }

    public void SetNitro(bool active)
    {
        nitroFallbackState = active;

        if (targetCar == null)
            nitroActive = active;
    }

    public void PlayImpact(float intensity)
    {
        TriggerShake(
            Mathf.Clamp01(intensity) * 0.14f,
            0.2f
        );
    }

    private void OnDisable()
    {
        if (cam != null && baseFov > 0f)
            cam.fieldOfView = baseFov;

        initialized = false;
        firstPersonView = false;
        firstPersonBlend = 1f;
    }

    // ====================================================================
    // LƯU Ý: Các hàm dưới đây có vẻ thuộc về một Script quản lý Intro Camera khác.
    // Tôi đã chuyển chúng ra ngoài LateUpdate() và comment lại các biến bị thiếu 
    // để code trên biên dịch được. Bạn nên chuyển chúng về đúng Script của nó.
    // ====================================================================

    private IEnumerator FlashCut(int nextShot)
    {
        if (flashGroup == null)
        {
            // Apply(EvaluateShot(nextShot, 0f)); // LỖI: Apply() và EvaluateShot() không tồn tại ở đây
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < flashInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / flashInDuration);
            flashGroup.alpha = Mathf.Lerp(0f, flashIntensity, t);
            yield return null;
        }

        flashGroup.alpha = flashIntensity;

        // Apply(EvaluateShot(nextShot, 0f)); // LỖI

        if (transitionAudio != null && whooshClip != null)
            transitionAudio.PlayOneShot(whooshClip);

        elapsed = 0f;

        while (elapsed < flashOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / flashOutDuration);
            flashGroup.alpha = Mathf.Lerp(flashIntensity, 0f, t * t * (3f - 2f * t));
            yield return null;
        }

        flashGroup.alpha = 0f;
    }

    /* ĐÃ COMMENT LẠI VÌ THIẾU QUÁ NHIỀU BIẾN CỦA HỆ THỐNG INTRO (chaseCamera, playing, sceneCamera...)
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
            Debug.LogWarning("[PoseKartRaceIntroCamera] Missing references. Skipping intro.", this);
            yield break;
        }

        playing = true;
        chaseCamera.enabled = false;

        if (flashGroup != null)
            flashGroup.alpha = 0f;

        Apply(EvaluateShot(0, 0f));
        yield return AnimateShot(0, heroShotDuration);

        yield return FlashCut(1);
        yield return AnimateShot(1, gridShotDuration);

        yield return FlashCut(2);
        yield return AnimateShot(2, rearShotDuration);

        yield return BlendToChase(chaseBlendDuration);

        chaseCamera.SetTarget(playerCar);
        chaseCamera.enabled = true;

        if (flashGroup != null)
            flashGroup.alpha = 0f;

        playing = false;
    }
    */
}
