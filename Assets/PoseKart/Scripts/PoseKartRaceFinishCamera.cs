using System;
using System.Collections;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Automatic player-only finish cinematic on the EXISTING Main Camera.
/// Subscribes to RaceManager.PlayerFinishedEvent; no RaceManager or RaceHUD edits needed.
/// Keeps the race result/time recorded by RaceManager, temporarily takes control from
/// PoseKartCinematicCamera, then shows a simple standalone result overlay.
/// </summary>
[RequireComponent(typeof(UnityEngine.Camera))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(10001)] // Apply our pose after the existing kart camera shake (10000).
[AddComponentMenu("PoseKart/Race Finish Camera")]
public sealed class PoseKartRaceFinishCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private Rigidbody playerCar;
    [SerializeField] private PoseKartCinematicCamera chaseCamera;
    [Tooltip("Optional: the finish-line Transform. If None, the player's crossing point is used.")]
    [SerializeField] private Transform finishLine;
    [Tooltip("Optional: the existing RaceHUD Canvas GameObject. Auto-detected if None.")]
    [SerializeField] private GameObject raceHudRoot;
    [SerializeField] private bool hideRaceHudDuringFinish = false;
    [Tooltip("Optional extra HUD Canvas roots to hide (e.g. a separate arcade speedometer Canvas). Do not assign RaceManager or Main Camera.")]
    [SerializeField] private GameObject[] additionalHudRootsToHide;

    [Header("Cinematic Duration (seconds)")]
    [SerializeField, Min(0.05f)] private float entryBlendDuration = 0.4f;
    [SerializeField, Min(0.1f)] private float finishLineShotDuration = 1.1f;
    [SerializeField, Min(0.1f)] private float sideTrackingDuration = 1.35f;
    [SerializeField, Min(0.1f)] private float heroShotDuration = 1.65f;
    [SerializeField, Min(0.1f)] private float resultsFadeDuration = 0.5f;

    [Header("Camera Framing")]
    [SerializeField, Range(0.2f, 3f)] private float carLookHeight = 0.95f;
    [SerializeField, Range(35f, 85f)] private float finishShotFov = 56f;
    [SerializeField, Range(35f, 85f)] private float sideShotFov = 58f;
    [SerializeField, Range(35f, 85f)] private float heroShotFov = 49f;

    [Header("Runtime Debug (Read Only)")]
    [SerializeField] private bool cinematicPlaying;
    [SerializeField] private bool resultsVisible;

    private UnityEngine.Camera sceneCamera;
    private RaceManager subscribedManager;
    private Coroutine bindRetryCoroutine;
    private bool chaseWasEnabled;
    private bool chaseWasTakenOver;
    private bool hudWasActive;
    private bool hudWasHidden;
    private bool[] additionalHudWasActive;
    private bool completed;
    private Vector3 crossingPosition;
    private Vector3 crossingForward = Vector3.forward;
    private ShotPose lastPose;
    private bool hasPose;

    private GameObject overlayRoot;
    private CanvasGroup finishGroup;
    private CanvasGroup resultsGroup;
    private Text resultsPositionText;
    private Text resultsTimeText;

    public bool IsPlaying => cinematicPlaying;
    public bool ResultsVisible => resultsVisible;
    public void SetPlayerCar(Rigidbody newPlayer)
    {
        playerCar = newPlayer;
    }
    private void Awake()
    {
        sceneCamera = GetComponent<UnityEngine.Camera>();
        ResolveReferences();
    }

    private void OnEnable()
    {
        BindRaceManager();
        // If RaceManager is created later, retry at most once per second,
        // never an expensive scene-wide search every frame.
        if (subscribedManager == null && bindRetryCoroutine == null)
            bindRetryCoroutine = StartCoroutine(RetryBindRaceManager());
    }

    private void Start()
    {
        BindRaceManager();
    }

    private IEnumerator RetryBindRaceManager()
    {
        WaitForSecondsRealtime delay = new WaitForSecondsRealtime(1f);
        while (isActiveAndEnabled && subscribedManager == null)
        {
            yield return delay;
            if (!isActiveAndEnabled)
                break;
            BindRaceManager();
        }
        bindRetryCoroutine = null;
    }

    private void LateUpdate()
    {
        // The player's KartCollisionCameraShake executes at order 10000. Reapply the
        // cinematic pose after it so that an old shake cannot fight the finish shot.
        if ((cinematicPlaying || resultsVisible) && hasPose && sceneCamera != null)
            Apply(lastPose);
    }

    private void ResolveReferences()
    {
        if (sceneCamera == null)
            sceneCamera = GetComponent<UnityEngine.Camera>();
        if (chaseCamera == null)
            chaseCamera = GetComponent<PoseKartCinematicCamera>();
        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();
        if (playerCar == null && raceManager != null && raceManager.PlayerController != null)
        {
            CarController controller = raceManager.PlayerController;
            playerCar = controller.TargetBody != null
                ? controller.TargetBody
                : controller.GetComponent<Rigidbody>();
        }
        if (raceHudRoot == null && hideRaceHudDuringFinish)
        {
            PoseKart.UI.RaceHUD hud = FindFirstObjectByType<PoseKart.UI.RaceHUD>();
            if (hud != null)
                raceHudRoot = hud.gameObject;
        }
    }

    private void BindRaceManager()
    {
        ResolveReferences();
        if (raceManager == null || subscribedManager == raceManager)
            return;

        UnbindRaceManager();
        subscribedManager = raceManager;
        subscribedManager.PlayerFinishedEvent += OnPlayerFinished;

        // Also works if the component was enabled just after the finish event fired.
        if (subscribedManager.PlayerFinished && !cinematicPlaying && !completed)
            OnPlayerFinished(subscribedManager);
    }

    private void UnbindRaceManager()
    {
        if (subscribedManager == null)
            return;
        subscribedManager.PlayerFinishedEvent -= OnPlayerFinished;
        subscribedManager = null;
    }

    private void OnPlayerFinished(RaceManager manager)
    {
        if (!isActiveAndEnabled || cinematicPlaying || completed || manager == null || !manager.PlayerFinished)
            return;
        raceManager = manager;
        StartCoroutine(PlayFinish());
    }

    /// <summary>
    /// Normally started automatically by RaceManager.PlayerFinishedEvent.
    /// Can also be started with StartCoroutine(finishCamera.PlayFinish()).
    /// </summary>
    public IEnumerator PlayFinish()
    {
        if (cinematicPlaying || completed)
            yield break;

        ResolveReferences();

        cinematicPlaying = true;
        completed = true;
        resultsVisible = false;

        // KHÔNG tạo PoseKart Finish Overlay nữa.
        // UI kết quả do PoseKartRaceFinishUI của mình quản lý.

        if (sceneCamera == null ||
            playerCar == null ||
            chaseCamera == null)
        {
            Debug.LogWarning(
                "[PoseKartRaceFinishCamera] Missing Camera, Player Car, or Chase Camera.",
                this
            );

            cinematicPlaying = false;
            resultsVisible = true;
            yield break;
        }

        // Lưu camera hiện tại trước khi takeover.
        ShotPose startingPose = new ShotPose(
            transform.position,
            transform.rotation,
            sceneCamera.fieldOfView
        );

        chaseWasEnabled = chaseCamera.enabled;
        chaseWasTakenOver = true;

        chaseCamera.enabled = false;

        sceneCamera.fieldOfView = startingPose.fov;

        // Vị trí xe cán finish.
        crossingPosition = finishLine != null
            ? finishLine.position
            : playerCar.position;

        crossingForward = Vector3.ProjectOnPlane(
            playerCar.transform.forward,
            Vector3.up
        );

        if (crossingForward.sqrMagnitude < 0.0001f)
            crossingForward = Vector3.forward;

        crossingForward.Normalize();


        // Shot 1
        // false = KHÔNG hiện cái FINISH! tự gen cũ.
        yield return BlendFrom(
            startingPose,
            0,
            entryBlendDuration,
            false
        );

        yield return AnimateShot(
            0,
            finishLineShotDuration
        );


        // Shot 2
        yield return BlendFrom(
            lastPose,
            1,
            0.25f,
            false
        );

        yield return AnimateShot(
            1,
            Mathf.Max(
                0.1f,
                sideTrackingDuration - 0.25f
            )
        );


        // Shot 3 - Hero shot
        yield return BlendFrom(
            lastPose,
            2,
            0.35f,
            false
        );

        yield return AnimateShot(
            2,
            Mathf.Max(
                0.1f,
                heroShotDuration - 0.35f
            )
        );


        // Giữ camera ở hero pose.
        resultsVisible = true;
        cinematicPlaying = false;

        // PoseKartRaceFinishUI sẽ tự hiển thị ResultsPanel custom.
    }

    private IEnumerator BlendFrom(ShotPose start, int shot, float duration, bool revealFinish)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.05f, duration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Smooth01(Mathf.Clamp01(elapsed / duration));
            ShotPose target = EvaluateShot(shot, 0f);
            SetPose(new ShotPose(
                Vector3.Lerp(start.position, target.position, t),
                Quaternion.Slerp(start.rotation, target.rotation, t),
                Mathf.Lerp(start.fov, target.fov, t)));
            if (revealFinish)
                finishGroup.alpha = Mathf.Clamp01(elapsed / 0.22f);
            yield return null;
        }
        SetPose(EvaluateShot(shot, 0f));
        if (revealFinish)
            finishGroup.alpha = 1f;
    }

    private IEnumerator AnimateShot(int shot, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.1f, duration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetPose(EvaluateShot(shot, Smooth01(Mathf.Clamp01(elapsed / duration))));
            yield return null;
        }
        SetPose(EvaluateShot(shot, 1f));
    }

    private ShotPose EvaluateShot(int shot, float t)
    {
        Vector3 carPosition = playerCar != null ? playerCar.position : crossingPosition;
        Vector3 forward = crossingForward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 position;
        Vector3 lookAt = carPosition + Vector3.up * carLookHeight;
        float fov;

        switch (shot)
        {
            case 0: // Finish-line low three-quarter shot; subtly follows the crossing car.
                Vector3 finishCameraPosition = crossingPosition - forward * 1.4f
                    + right * 3.8f + Vector3.up * 1.25f;
                Vector3 followPosition = carPosition - forward * 1.3f
                    + right * 3.8f + Vector3.up * 1.3f;
                position = Vector3.Lerp(finishCameraPosition, followPosition, 0.2f + t * 0.35f);
                fov = finishShotFov;
                break;
            case 1: // Side tracking shot.
                position = carPosition + right * Mathf.Lerp(4.2f, 4.6f, t)
                    - forward * Mathf.Lerp(0.4f, 0.8f, t)
                    + Vector3.up * Mathf.Lerp(1.25f, 1.55f, t);
                fov = sideShotFov;
                break;
            default: // Low front-left hero shot.
                position = carPosition + forward * Mathf.Lerp(3.6f, 3.0f, t)
                    - right * Mathf.Lerp(2.8f, 1.9f, t)
                    + Vector3.up * Mathf.Lerp(1.45f, 1.2f, t);
                fov = heroShotFov;
                break;
        }
        Vector3 direction = lookAt - position;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : transform.rotation;
        return new ShotPose(position, rotation, fov);
    }

    private void SetPose(ShotPose pose)
    {
        lastPose = pose;
        hasPose = true;
        Apply(pose);
    }

    private void Apply(ShotPose pose)
    {
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        sceneCamera.fieldOfView = pose.fov;
    }

    private void PopulateResults()
    {
        if (resultsPositionText == null || resultsTimeText == null)
            return;
        if (raceManager == null)
        {
            resultsPositionText.text = "RACE COMPLETE";
            resultsTimeText.text = string.Empty;
            return;
        }
        resultsPositionText.text = "POSITION  " + raceManager.PlayerFinishPosition + " / " + raceManager.RacerCount;
        resultsTimeText.text = "TIME  " + FormatTime(raceManager.PlayerFinishTimeSeconds);
    }

    private void BuildOverlayIfNeeded()
    {
        if (overlayRoot != null)
            return;

        overlayRoot = new GameObject("PoseKart Finish Overlay",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = overlayRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Arial", 32);

        Text finish = CreateText(overlayRoot.transform, "FINISH!", font, 112,
            new Color(1f, 0.83f, 0.19f, 1f), new Vector2(0f, 310f), new Vector2(1100f, 170f));
        finish.fontStyle = FontStyle.Bold;
        Outline finishOutline = finish.gameObject.AddComponent<Outline>();
        finishOutline.effectColor = new Color(0.02f, 0.04f, 0.08f, 1f);
        finishOutline.effectDistance = new Vector2(4f, -4f);
        finishGroup = finish.gameObject.AddComponent<CanvasGroup>();
        finishGroup.alpha = 0f;

        GameObject resultBackground = new GameObject("Results Background",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        resultBackground.transform.SetParent(overlayRoot.transform, false);
        RectTransform backgroundRect = (RectTransform)resultBackground.transform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        resultBackground.GetComponent<Image>().color = new Color(0.01f, 0.025f, 0.06f, 0.70f);
        resultsGroup = resultBackground.GetComponent<CanvasGroup>();
        resultsGroup.alpha = 0f;
        resultsGroup.interactable = false;
        resultsGroup.blocksRaycasts = false;

        GameObject card = new GameObject("Result Card", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(resultBackground.transform, false);
        RectTransform cardRect = (RectTransform)card.transform;
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(760f, 450f);
        cardRect.anchoredPosition = Vector2.zero;
        card.GetComponent<Image>().color = new Color(0.025f, 0.065f, 0.12f, 0.96f);

        Text title = CreateText(card.transform, "RACE COMPLETE", font, 64,
            new Color(1f, 0.83f, 0.19f, 1f), new Vector2(0f, 128f), new Vector2(710f, 100f));
        title.fontStyle = FontStyle.Bold;
        resultsPositionText = CreateText(card.transform, "POSITION", font, 52,
            Color.white, new Vector2(0f, 20f), new Vector2(700f, 90f));
        resultsPositionText.fontStyle = FontStyle.Bold;
        resultsTimeText = CreateText(card.transform, "TIME", font, 43,
            new Color(0.25f, 0.92f, 0.96f, 1f), new Vector2(0f, -76f), new Vector2(700f, 80f));
        CreateText(card.transform, "POSEKART", font, 26,
            new Color(0.65f, 0.75f, 0.82f, 1f), new Vector2(0f, -170f), new Vector2(700f, 60f));

        overlayRoot.SetActive(false);
    }

    private static Text CreateText(Transform parent, string value, Font font, int fontSize,
        Color color, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject gameObject = new GameObject(value, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Text));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        Text text = gameObject.GetComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static string FormatTime(double seconds)
    {
        seconds = Math.Max(0d, seconds);
        int minutes = (int)(seconds / 60d);
        int wholeSeconds = (int)(seconds % 60d);
        int milliseconds = (int)((seconds - Math.Floor(seconds)) * 1000d);
        return $"{minutes:00}:{wholeSeconds:00}.{milliseconds:000}";
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void OnDisable()
    {
        if (bindRetryCoroutine != null)
        {
            StopCoroutine(bindRetryCoroutine);
            bindRetryCoroutine = null;
        }
        UnbindRaceManager();
        if (cinematicPlaying)
            StopAllCoroutines();
        cinematicPlaying = false;
        resultsVisible = false;
        hasPose = false;
        if (chaseCamera != null && chaseWasTakenOver)
            chaseCamera.enabled = chaseWasEnabled;
        chaseWasTakenOver = false;
        if (hudWasHidden && raceHudRoot != null)
            raceHudRoot.SetActive(hudWasActive);
        hudWasHidden = false;
        if (additionalHudRootsToHide != null && additionalHudWasActive != null)
        {
            for (int i = 0; i < additionalHudRootsToHide.Length && i < additionalHudWasActive.Length; i++)
            {
                if (additionalHudWasActive[i] && additionalHudRootsToHide[i] != null)
                    additionalHudRootsToHide[i].SetActive(true);
            }
        }
        additionalHudWasActive = null;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (overlayRoot != null)
            Destroy(overlayRoot);
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
