using System.Collections;
using PoseKart.AI;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;

/// <summary>
/// After RaceManager records the PLAYER finish, transfers Buggy_Player from its
/// human input provider to the existing AIRacerBrain + AIInputProvider.
/// No new Rigidbody, no teleport, no race-result changes, and no per-frame searches.
/// Put this component on Buggy_Player, NOT on Main Camera.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CarController))]
[AddComponentMenu("PoseKart/Finish AI Takeover")]
public sealed class PoseKartFinishAITakeover : MonoBehaviour
{
    [Header("References - assign in Inspector")]
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private CarController playerController;
    [Tooltip("The original human input, normally KeyboardInputProvider on Buggy_Player.")]
    [SerializeField] private MonoBehaviour playerInputProvider;
    [Tooltip("AIInputProvider added to Buggy_Player; keep it disabled before Play.")]
    [SerializeField] private AIInputProvider finishAIInput;
    [Tooltip("A COPY of AI_Coral's AIRacerBrain, attached to Buggy_Player; keep it disabled before Play. Reassign all its references to Buggy_Player.")]
    [SerializeField] private AIRacerBrain finishAIBrain;

    [Header("Runtime Debug (Read Only)")]
    [SerializeField] private bool aiHasControl;

    private RaceManager subscribedManager;
    private Coroutine takeoverCoroutine;

    public bool AIHasControl => aiHasControl;

    private void Awake()
    {
        ResolveReferences();

        // These components must never drive the player during countdown/racing.
        if (finishAIBrain != null)
            finishAIBrain.enabled = false;
        if (finishAIInput != null)
            finishAIInput.enabled = false;
    }

    private void OnEnable()
    {
        BindRaceManager();
    }

    private void Start()
    {
        ResolveReferences();

        // If the copied AI brain configured the CarController in its own Awake,
        // explicitly restore the human provider before the race begins.
        if (!aiHasControl && playerController != null && playerInputProvider != null &&
            playerController.InputProviderSource != playerInputProvider)
        {
            playerController.SetInputProvider(playerInputProvider);
        }

        BindRaceManager();

        if (raceManager == null || playerController == null ||
            finishAIInput == null || finishAIBrain == null || playerInputProvider == null)
        {
            Debug.LogWarning(
                "[PoseKartFinishAITakeover] Assign RaceManager, Player Controller, Player Input, " +
                "Finish AI Input and Finish AI Brain on Buggy_Player. AI takeover will not run until configured.", this);
        }
    }

    private void ResolveReferences()
    {
        if (playerController == null)
            playerController = GetComponent<CarController>();
        if (finishAIInput == null)
            finishAIInput = GetComponent<AIInputProvider>();
        if (finishAIBrain == null)
            finishAIBrain = GetComponent<AIRacerBrain>();
        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>(); // One-time, never Update().
        if (playerInputProvider == null && playerController != null)
        {
            MonoBehaviour current = playerController.InputProviderSource;
            if (current != null && current != finishAIInput)
                playerInputProvider = current;
        }
    }

    private void BindRaceManager()
    {
        if (raceManager == null)
            return;
        if (subscribedManager != raceManager)
        {
            UnbindRaceManager();
            subscribedManager = raceManager;
            subscribedManager.PlayerFinishedEvent += OnPlayerFinished;
        }

        // Handles a component enabled just after the finish event.
        if (subscribedManager.PlayerFinished)
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
        if (!isActiveAndEnabled || manager == null || manager != raceManager ||
            !manager.PlayerFinished || aiHasControl || takeoverCoroutine != null)
            return;

        takeoverCoroutine = StartCoroutine(TakeOverAfterFinish());
    }

    private IEnumerator TakeOverAfterFinish()
    {
        // Let RaceManager finish its own event handler, result capture and control lock.
        yield return new WaitForEndOfFrame();

        if (!isActiveAndEnabled)
        {
            takeoverCoroutine = null;
            yield break;
        }

        ResolveReferences();
        if (playerController == null || finishAIInput == null || finishAIBrain == null)
        {
            Debug.LogError("[PoseKartFinishAITakeover] Missing player controller / AI input / AI brain.", this);
            takeoverCoroutine = null;
            yield break;
        }

        // Keep the same physical car, speed and position. Switch ONLY its input source.
        finishAIInput.enabled = true;
        playerController.StopNitro();
        playerController.SetInputProvider(finishAIInput);
        playerController.SetControlPermission(true);
        finishAIBrain.enabled = true;
        aiHasControl = true;

        // One extra check in case a race-end coroutine applied its lock next frame.
        yield return null;
        if (aiHasControl && playerController != null && !playerController.ControlEnabled)
            playerController.SetControlPermission(true);

        if (Mathf.Approximately(Time.timeScale, 0f))
            Debug.LogWarning("[PoseKartFinishAITakeover] Time.timeScale is zero: physics cannot move the car until RaceManager unpauses.", this);

        takeoverCoroutine = null;
    }

    private void OnDisable()
    {
        UnbindRaceManager();
        if (takeoverCoroutine != null)
        {
            StopCoroutine(takeoverCoroutine);
            takeoverCoroutine = null;
        }
        // Do not restore keyboard control after a recorded finish.
    }
}
