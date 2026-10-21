using System;
using PoseKart.Race;
using PoseKart.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// The single editable Canvas HUD for the PoseKart race. The hierarchy is
    /// created once in Edit Mode and gameplay data comes from existing systems.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PoseKartArcadeHUD : MonoBehaviour
    {
        private static readonly Color Navy = new Color(0.012f, 0.035f, 0.075f, 0.92f);
        private static readonly Color NavySoft = new Color(0.012f, 0.035f, 0.075f, 0.78f);
        private static readonly Color White = new Color(0.96f, 0.98f, 1f, 1f);
        private static readonly Color Cyan = new Color(0.04f, 0.86f, 0.93f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.78f, 0.08f, 1f);
        private static readonly Color Red = new Color(0.96f, 0.025f, 0.16f, 0.94f);
        private static readonly Color Muted = new Color(0.72f, 0.78f, 0.84f, 1f);
        private static readonly Color Banana = new Color(1f, 0.75f, 0.12f, 1f);
        private static readonly Color Shield = new Color(0.18f, 0.9f, 1f, 1f);
        private static readonly KartItemPickup.ItemType[] RouletteItems =
{
    KartItemPickup.ItemType.InkShot,
    KartItemPickup.ItemType.HomingMissile,
    KartItemPickup.ItemType.BananaTrap,
    KartItemPickup.ItemType.Shield
};

        [Header("Gameplay references")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private CarController playerCar;

        [Header("Layout")]
        [Tooltip("Only create the initial HUD if ArcadeHUD_Root does not exist. Never overwrite an existing Edit Mode design automatically.")]
        [SerializeField] private bool buildLayoutInEditMode = true;
        [SerializeField] private bool hideLegacySpeedometer = true;
        [Tooltip("Use the original parchment-style NitroMeterUI when it exists instead of creating a second Nitro bar.")]
        [SerializeField] private bool useDedicatedNitroMeter = true;
        [SerializeField] private bool showProgress = false;
        [SerializeField, Min(0.01f)] private float valueSmoothing = 12f;

        [Header("Item roulette")]
        [SerializeField] private Sprite mysteryItemIcon;
        [Tooltip("Optional icons. Leave empty to use the built-in readable labels.")]
        [SerializeField] private Sprite inkIcon;
        [SerializeField] private Sprite missileIcon;
        [SerializeField] private Sprite bananaIcon;
        [SerializeField] private Sprite shieldIcon;
        [SerializeField, Range(0.35f, 2f)] private float rouletteDuration = 0.82f;

        [Header("Hand-control panel")]
        [SerializeField] private bool showHandDetails = true;

        [Header("Pause Control")]
        [Tooltip("Button ESC trên HUD dùng để tạm dừng game.")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private bool isPaused;

        [Header("Runtime debug")]
        [SerializeField] private float actualSpeedKmh;
        [SerializeField] private float displayedNitro;
        [SerializeField] private string hudState = "Edit Mode Layout Ready";
        [SerializeField] private string itemHudState = "EMPTY";
        [SerializeField] private string handConnectionState = "HAND: DISCONNECTED";

        private TMP_Text positionLabel;
        private TMP_Text positionValue;
        private TMP_Text lapLabel;
        private TMP_Text lapValue;
        private TMP_Text progressValue;
        private TMP_Text speedValue;
        private TMP_Text timerValue;
        private TMP_Text driftCallout;
        private TMP_Text airCallout;
        private TMP_Text nitroCallout;
        private Image nitroFill;
        private GameObject progressRow;
        private GameObject driftCalloutRoot;
        private GameObject airCalloutRoot;
        private GameObject nitroCalloutRoot;

        private GameObject itemHudRoot;
        private GameObject rouletteRoot;
        private Image selectionFrame;
        private TMP_Text selectionPointer;
        private Image heldItemIcon;
        private TMP_Text heldItemName;
        private TMP_Text heldItemState;

        private GameObject eventBannerRoot;
        private CanvasGroup eventBannerGroup;
        private TMP_Text eventBannerTitle;
        private TMP_Text eventBannerSubtitle;
        private TMP_Text handStatus;
        private TMP_Text handDetails;
        private PoseKartMinimap minimap;
        private KartItemInventory playerInventory;
        private PoseKart.Input.LocalPoseTracker poseTracker;
        private bool inventorySubscribed;
        private bool layoutReady;
        private bool building;
        private bool forceLayoutRebuild;
        private bool runtimeBindAttempted;
        private float displayedSpeed;
        private Color normalNitroFillColor = Color.yellow;
        private float rouletteFrameOffsetX;
        private float roulettePointerOffsetX;
        private float eventBannerUntil;
        private float rouletteStartedAt;
        private float rouletteEndsAt;
        private float rouletteHideAt;
        private int rouletteTargetIndex;
        private int rouletteLastIndex = -1;
        private bool roulettePlaying;
        private KartItemPickup.ItemType lastHeldItem;
        private bool lastHasItem;
        private bool lastNoTarget;
        private string lastHandText;
        private int lastObservedLap = 1;
        private float savedTimeScale = 1f;
        private bool pauseButtonBound;
#if UNITY_EDITOR
        private bool editorBuildQueued;
#endif

        private void OnEnable()
        {
            layoutReady = false;
            runtimeBindAttempted = false;
#if UNITY_EDITOR
            // Auto-create only when there is no saved HUD. Do not reset edited UI.
            QueueEditorLayout();
#endif
        }

        private void OnValidate()
        {
            if (building || Application.isPlaying) return;
#if UNITY_EDITOR
            // Editing component settings must never rebuild the existing layout.
            QueueEditorLayout();
#endif
        }

        private void OnDisable()
        {
            UnsubscribeInventory();
            UnsubscribeRaceManager();
            UnbindPauseButton();
            if (isPaused)
            {
                SetPaused(false);
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= BuildEditorLayoutDelayed;
            editorBuildQueued = false;
#endif
        }

        private void OnDestroy()
        {
            if (isPaused)
            {
                Time.timeScale = savedTimeScale > 0.001f ? savedTimeScale : 1f;
                AudioListener.pause = false;
                isPaused = false;
            }
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                EnsureReferences();
                EnsureLayout();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                if (buildLayoutInEditMode && transform.Find("ArcadeHUD_Root") == null)
                    QueueEditorLayout();
#endif
                return;
            }

            EnsureReferences();
            if (!layoutReady) EnsureLayout();
            CheckPauseInput();
            RefreshLiveValues();
            UpdateRoulette();
            RefreshItemHud();
            RefreshHandPanel();
        }

#if UNITY_EDITOR
        private void QueueEditorLayout()
        {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                !buildLayoutInEditMode || !isActiveAndEnabled || editorBuildQueued ||
                transform.Find("ArcadeHUD_Root") != null) return;
            editorBuildQueued = true;
            UnityEditor.EditorApplication.delayCall += BuildEditorLayoutDelayed;
        }

        private void BuildEditorLayoutDelayed()
        {
            editorBuildQueued = false;
            if (this == null || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                !buildLayoutInEditMode || !isActiveAndEnabled || building || !gameObject.scene.IsValid() ||
                transform.Find("ArcadeHUD_Root") != null) return;
            EnsureReferences();
            EnsureLayout();
        }
#endif

        // WARNING: This manual command deliberately reapplies default UI sizes and positions.
        // It is never called automatically when a saved HUD already exists.
        [ContextMenu("FORCE Rebuild Arcade HUD (Overwrites UI Design)")]
        private void RebuildLayout()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[PoseKart HUD] Stop Play Mode before forcing a HUD rebuild.", this);
                return;
            }
            layoutReady = false;
            forceLayoutRebuild = true;
            try
            {
                EnsureReferences();
                EnsureLayout();
            }
            finally
            {
                forceLayoutRebuild = false;
            }
        }

        private void EnsureReferences()
        {
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
            if (playerCar == null && raceManager != null) playerCar = raceManager.PlayerController;
            if (playerInventory == null && playerCar != null)
                playerInventory = playerCar.GetComponent<KartItemInventory>();
            // Re-subscribe after OnDisable/OnEnable, even if the inventory reference survived.
            if (playerInventory != null && !inventorySubscribed)
                SubscribeInventory();
            if (raceManager != null && !raceManagerSubscribed)
                SubscribeRaceManager();
            if (poseTracker == null && playerCar != null)
                poseTracker = playerCar.GetComponent<PoseKart.Input.LocalPoseTracker>();
            EnsurePauseButton();
        }

        private bool raceManagerSubscribed;

        private void SubscribeRaceManager()
        {
            if (raceManagerSubscribed || raceManager == null) return;
            raceManager.CountdownTickEvent += HandleCountdownTick;
            raceManagerSubscribed = true;
        }

        private void UnsubscribeRaceManager()
        {
            if (!raceManagerSubscribed || raceManager == null) return;
            raceManager.CountdownTickEvent -= HandleCountdownTick;
            raceManagerSubscribed = false;
        }

        private void HandleCountdownTick(int count, string display)
        {
            if (count > 0)
            {
                ShowEventBanner(display, "GET READY!", 0.95f);
            }
            else
            {
                ShowEventBanner("GO!", "RACE START!", 0.85f);
            }
        }

        private void SubscribeInventory()
        {
            if (inventorySubscribed || playerInventory == null) return;
            playerInventory.ItemCollected += HandleItemCollected;
            playerInventory.ItemUsed += HandleItemUsed;
            playerInventory.ItemActivationFailed += HandleItemActivationFailed;
            playerInventory.ItemExpired += HandleItemExpired;
            inventorySubscribed = true;
        }

        private void UnsubscribeInventory()
        {
            if (!inventorySubscribed || playerInventory == null) return;
            playerInventory.ItemCollected -= HandleItemCollected;
            playerInventory.ItemUsed -= HandleItemUsed;
            playerInventory.ItemActivationFailed -= HandleItemActivationFailed;
            playerInventory.ItemExpired -= HandleItemExpired;
            inventorySubscribed = false;
        }

        private void EnsureLayout()
        {
            if (building || layoutReady) return;

            // The saved RaceHUD in older scene revisions can retain a zero
            // Canvas scale. That also hides the dedicated top-center Nitro
            // meter when we bind an already-built ArcadeHUD_Root in Play Mode.
            if (transform.localScale.sqrMagnitude < 0.0001f)
                transform.localScale = Vector3.one;

            // NEVER build or reset an editable HUD in Play Mode. The saved scene
            // is the source of truth for RectTransforms, fonts, colors and sizes.
            if (Application.isPlaying)
            {
                if (runtimeBindAttempted) return;
                runtimeBindAttempted = true;
                if (TryBindExistingLayout())
                {
                    PrepareRuntimeVisualState();
                    layoutReady = true;
                    hudState = "Playing: using saved HUD layout";
                }
                else
                {
                    hudState = "Missing ArcadeHUD_Root: build HUD in Edit Mode";
                    Debug.LogError("[PoseKart HUD] ArcadeHUD_Root is missing. Stop Play Mode, select RaceHUD and use the force rebuild context menu only if you need a new layout. Play Mode will NOT overwrite your design.", this);
                }
                return;
            }

            // OnValidate and editor reloads must never regenerate a layout the
            // user already designed. Only an explicit force rebuild may do so.
            if (!forceLayoutRebuild && transform.Find("ArcadeHUD_Root") != null)
            {
                TryBindExistingLayout();
                layoutReady = true;
                return;
            }

            building = true;
            try
            {
                // RaceHUD was serialized with a zero Canvas scale. Restore it so
                // the editable HUD and the legacy countdown can both render.
                transform.localScale = Vector3.one;
                SetActiveIfFound("Position", false);
                SetActiveIfFound("Lap", false);
                SetActiveIfFound("Time", false);
                SetActiveIfFound("RaceStatus", false);
                SetActiveIfFound("NitroMeterUI", false);
                if (hideLegacySpeedometer) SetActiveIfFound("SpeedometerUI", false);

                RectTransform root = GetOrCreateArcadeRoot();
                MoveExistingToRoot(root, "Arcade Race Info", "Arcade Nitro Bar", "Arcade Speed Panel",
                    "Arcade Timer Ribbon", "Arcade Event Callouts");
                // These are serialized remnants of the previous layout. Keep
                // them in the scene for migration safety, but never render both
                // generations of the HUD at the same time.
                SetActiveChild(root, "Arcade Race Info", false);
                SetActiveChild(root, "Arcade Nitro Bar", false);
                SetActiveChild(root, "Arcade Speed Panel", false);
                SetActiveChild(root, "Arcade Timer Ribbon", false);
                SetActiveChild(root, "Arcade Event Callouts", false);

                RectTransform infoPanel = CreatePanel("RaceInfo_TopLeft", root, new Vector2(56f, -40f),
                    new Vector2(390f, 190f), Navy, 16f, 16f, 14f, 0f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f));
                positionLabel = CreateText("POS.", "Position Label", infoPanel, new Vector2(112f, -20f),
                    new Vector2(75f, 28f), 20f, Muted, TextAlignmentOptions.Left);
                positionValue = CreateText("1 / 4", "Position Value", infoPanel, new Vector2(190f, -14f),
                    new Vector2(174f, 42f), 35f, White, TextAlignmentOptions.Right);
                lapLabel = CreateText("LAP", "Lap Label", infoPanel, new Vector2(112f, -78f),
                    new Vector2(75f, 28f), 20f, Muted, TextAlignmentOptions.Left);
                lapValue = CreateText("1 / 3", "Lap Value", infoPanel, new Vector2(190f, -72f),
                    new Vector2(174f, 40f), 32f, White, TextAlignmentOptions.Right);
                progressRow = CreateText("", "Progress Value", infoPanel, new Vector2(112f, -134f),
                    new Vector2(252f, 25f), 17f, Cyan, TextAlignmentOptions.Right).gameObject;
                progressValue = progressRow.GetComponent<TMP_Text>();
                timerValue = CreateText("00:00.000", "Race Timer", infoPanel, new Vector2(108f, -160f),
                    new Vector2(268f, 24f), 18f, White, TextAlignmentOptions.Right);
                CreateText("||", "Pause", infoPanel, new Vector2(16f, -18f),
                    new Vector2(70f, 115f), 58f, White, TextAlignmentOptions.Center);

                RectTransform speedPanel = CreatePanel("SpeedPanel_BottomRight", root, new Vector2(-54f, 156f),
                    new Vector2(370f, 112f), NavySoft, 18f, 0f, 18f, 0f,
                    new Vector2(1f, 0f), new Vector2(1f, 0f));
                CreateText("KM/H", "Speed Unit", speedPanel, new Vector2(28f, 20f),
                    new Vector2(90f, 28f), 22f, White, TextAlignmentOptions.Left);
                speedValue = CreateText("000", "Digital Speed", speedPanel, new Vector2(100f, 12f),
                    new Vector2(242f, 74f), 62f, White, TextAlignmentOptions.Right);

                if (UseDedicatedNitroMeter())
                {
                    // PoseKartNitroMeterUI owns the original parchment-style
                    // bar, icon, label and percentage. Do not create a second
                    // simplified NitroBar on top of it.
                    DisableLegacyNitroBar(root);
                    nitroFill = null;
                }
                else
                {
                    RectTransform nitroPanel = CreatePanel("NitroBar", root, new Vector2(-54f, 72f),
                        new Vector2(370f, 52f), NavySoft, 8f, 8f, 8f, 8f,
                        new Vector2(1f, 0f), new Vector2(1f, 0f));
                    Image nitroTrack = CreateImage("Nitro Track", nitroPanel, Vector2.zero, Vector2.zero,
                        new Color(0.01f, 0.02f, 0.04f, 0.95f));
                    nitroTrack.rectTransform.anchorMin = Vector2.zero;
                    nitroTrack.rectTransform.anchorMax = Vector2.one;
                    nitroTrack.rectTransform.offsetMin = new Vector2(12f, 10f);
                    nitroTrack.rectTransform.offsetMax = new Vector2(-12f, -10f);
                    nitroFill = CreateImage("Nitro Fill", nitroTrack.rectTransform, Vector2.zero, Vector2.zero, Yellow);
                    nitroFill.rectTransform.anchorMin = Vector2.zero;
                    nitroFill.rectTransform.anchorMax = Vector2.one;
                    nitroFill.rectTransform.pivot = new Vector2(0f, 0.5f);
                    nitroFill.rectTransform.offsetMin = Vector2.zero;
                    nitroFill.rectTransform.offsetMax = Vector2.zero;
                    nitroFill.type = Image.Type.Filled;
                    nitroFill.fillMethod = Image.FillMethod.Horizontal;
                    nitroFill.fillOrigin = 0;
                    normalNitroFillColor = nitroFill.color;
                    CreateText("NITRO", "Nitro Label", nitroPanel, new Vector2(18f, -7f),
                        new Vector2(72f, 25f), 15f, White, TextAlignmentOptions.Left);
                }

                RectTransform eventPanel = CreatePanel("EventBanner_TopCenter", root, Vector2.zero,
                    new Vector2(350f, 66f), NavySoft, 10f, 10f, 10f, 10f,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                eventPanel.anchoredPosition = new Vector2(0f, -32f);
                eventBannerRoot = eventPanel.gameObject;
                // UnityEngine.Object uses a custom null check, so avoid ?? for Components.
                eventBannerGroup = eventBannerRoot.GetComponent<CanvasGroup>();
                if (eventBannerGroup == null)
                    eventBannerGroup = eventBannerRoot.AddComponent<CanvasGroup>();
                eventBannerTitle = CreateText("ITEM BOX", "Event Title", eventPanel, new Vector2(22f, -8f),
                    new Vector2(306f, 26f), 20f, Yellow, TextAlignmentOptions.Center);
                eventBannerSubtitle = CreateText("RANDOM ITEM", "Event Subtitle", eventPanel, new Vector2(22f, -35f),
                    new Vector2(306f, 19f), 12f, White, TextAlignmentOptions.Center);
                if (eventBannerGroup != null)
                {
                    eventBannerGroup.alpha = Application.isPlaying ? 0f : 1f;
                    eventBannerGroup.interactable = false;
                    eventBannerGroup.blocksRaycasts = false;
                }

                CreateMinimapFrame(root);
                BuildItemHud(root);
                BuildHandPanel(root);
                if (Application.isPlaying) SetActive(rouletteRoot, false);

                RectTransform calloutPanel = CreatePanel("Race Callouts", root, new Vector2(-55f, 270f),
                    new Vector2(320f, 194f), Color.clear, 0f, 0f, 0f, 0f,
                    new Vector2(1f, 0f), new Vector2(1f, 0f));
                driftCalloutRoot = CreateCallout(calloutPanel, "Drift Callout", "DRIFT", 0f, out driftCallout);
                airCalloutRoot = CreateCallout(calloutPanel, "Air Callout", "AIR TIME", -62f, out airCallout);
                nitroCalloutRoot = CreateCallout(calloutPanel, "Nitro Callout", "NITRO", -124f, out nitroCallout);
                SetActive(driftCalloutRoot, false);
                SetActive(airCalloutRoot, false);
                SetActive(nitroCalloutRoot, false);

                progressRow.SetActive(showProgress);
                layoutReady = true;
                hudState = "Editable arcade HUD ready";
                MarkEditorDirty();
            }
            finally { building = false; }
        }

        private bool TryBindExistingLayout()
        {
            Transform root = transform.Find("ArcadeHUD_Root");
            if (root == null) return false;

            positionLabel = FindComponent<TMP_Text>(root, "RaceInfo_TopLeft/Position Label");
            positionValue = FindComponent<TMP_Text>(root, "RaceInfo_TopLeft/Position Value");
            lapLabel = FindComponent<TMP_Text>(root, "RaceInfo_TopLeft/Lap Label");
            lapValue = FindComponent<TMP_Text>(root, "RaceInfo_TopLeft/Lap Value");
            progressRow = FindObject(root, "RaceInfo_TopLeft/Progress Value");
            progressValue = progressRow == null ? null : progressRow.GetComponent<TMP_Text>();
            timerValue = FindComponent<TMP_Text>(root, "RaceInfo_TopLeft/Race Timer");
            speedValue = FindComponent<TMP_Text>(root, "SpeedPanel_BottomRight/Digital Speed");
            nitroFill = FindComponent<Image>(root, "NitroBar/Nitro Track/Nitro Fill");
            if (UseDedicatedNitroMeter())
            {
                DisableLegacyNitroBar(root);
                nitroFill = null;
            }

            eventBannerRoot = FindObject(root, "EventBanner_TopCenter");
            eventBannerGroup = eventBannerRoot == null ? null : eventBannerRoot.GetComponent<CanvasGroup>();
            eventBannerTitle = FindComponent<TMP_Text>(root, "EventBanner_TopCenter/Event Title");
            eventBannerSubtitle = FindComponent<TMP_Text>(root, "EventBanner_TopCenter/Event Subtitle");

            itemHudRoot = FindObject(root, "ItemHUD_Right");
            rouletteRoot = FindObject(root, "ItemHUD_Right/RoulettePanel");
            selectionFrame = FindComponent<Image>(root, "ItemHUD_Right/RoulettePanel/Selection Frame");
            selectionPointer = FindComponent<TMP_Text>(root, "ItemHUD_Right/RoulettePanel/Selection Pointer");
            heldItemIcon = FindComponent<Image>(root, "ItemHUD_Right/HeldItemPanel/Held Item Icon");
            heldItemName = FindComponent<TMP_Text>(root, "ItemHUD_Right/HeldItemPanel/Held Item Name");
            heldItemState = FindComponent<TMP_Text>(root, "ItemHUD_Right/HeldItemPanel/Held Item State");

            handStatus = FindComponent<TMP_Text>(root, "HandControl_BottomLeft/Hand Status");
            handDetails = FindComponent<TMP_Text>(root, "HandControl_BottomLeft/Hand Details");
            driftCalloutRoot = FindObject(root, "Race Callouts/Drift Callout");
            airCalloutRoot = FindObject(root, "Race Callouts/Air Callout");
            nitroCalloutRoot = FindObject(root, "Race Callouts/Nitro Callout");
            driftCallout = FindComponent<TMP_Text>(root, "Race Callouts/Drift Callout/Drift Callout Text");
            airCallout = FindComponent<TMP_Text>(root, "Race Callouts/Air Callout/Air Callout Text");
            nitroCallout = FindComponent<TMP_Text>(root, "Race Callouts/Nitro Callout/Nitro Callout Text");

            minimap = root.GetComponentInChildren<PoseKartMinimap>(true);

            // Preserve whichever graphics exist instead of rebuilding the entire
            // interface just because the user renamed or removed one child.
            if (nitroFill != null)
                normalNitroFillColor = nitroFill.color;

            RectTransform firstCard = rouletteRoot == null ? null :
                rouletteRoot.transform.Find("Item Card 0") as RectTransform;
            if (firstCard != null)
            {
                if (selectionFrame != null)
                    rouletteFrameOffsetX = selectionFrame.rectTransform.anchoredPosition.x - firstCard.anchoredPosition.x;
                if (selectionPointer != null)
                    roulettePointerOffsetX = selectionPointer.rectTransform.anchoredPosition.x - firstCard.anchoredPosition.x;
            }

            if (Application.isPlaying && (positionValue == null || lapValue == null ||
                speedValue == null || timerValue == null ||
                (!UseDedicatedNitroMeter() && nitroFill == null)))
            {
                Debug.LogWarning("[PoseKart HUD] Some HUD text/image references are missing or were renamed. Existing layout is preserved; affected values will not update until the hierarchy is corrected.", this);
            }
            EnsurePauseButton();
            return true;
        }

        private bool UseDedicatedNitroMeter()
        {
            return useDedicatedNitroMeter &&
                FindFirstObjectByType<PoseKartNitroMeterUI>(FindObjectsInactive.Include) != null;
        }

        private static void DisableLegacyNitroBar(Transform root)
        {
            GameObject legacy = FindObject(root, "NitroBar");
            if (legacy != null)
                legacy.SetActive(false);
        }

        private void PrepareRuntimeVisualState()
        {
            // Missing CanvasGroup is fixed without touching the panel's layout.
            if (eventBannerRoot != null && eventBannerGroup == null)
                eventBannerGroup = eventBannerRoot.AddComponent<CanvasGroup>();
            if (eventBannerGroup != null)
            {
                eventBannerGroup.alpha = 0f;
                eventBannerGroup.interactable = false;
                eventBannerGroup.blocksRaycasts = false;
            }

            // Runtime-only visibility changes are intentional; saved transforms
            // and designer visuals are left untouched.
            SetActive(rouletteRoot, false);
        }

        private static GameObject FindObject(Transform root, string path)
        {
            Transform found = root == null ? null : root.Find(path);
            return found == null ? null : found.gameObject;
        }

        private static T FindComponent<T>(Transform root, string path) where T : Component
        {
            Transform found = root == null ? null : root.Find(path);
            return found == null ? null : found.GetComponent<T>();
        }

        private RectTransform GetOrCreateArcadeRoot()
        {
            GameObject rootObject = GetOrCreate("ArcadeHUD_Root", transform);
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.localScale = Vector3.one;
            return root;
        }

        private void MoveExistingToRoot(RectTransform root, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Transform existing = transform.Find(names[i]);
                if (existing != null && existing != root.transform) existing.SetParent(root, false);
            }
        }

        private void CreateMinimapFrame(RectTransform root)
        {
            RectTransform frame = CreatePanel("Minimap_Frame", root, new Vector2(-52f, -44f),
                new Vector2(300f, 300f), NavySoft, 14f, 14f, 14f, 14f,
                new Vector2(1f, 1f), new Vector2(1f, 1f));
            minimap = FindFirstObjectByType<PoseKartMinimap>();
            if (minimap == null || minimap.transform == frame.transform) return;
            minimap.transform.SetParent(frame, false);
            RectTransform mapRect = minimap.rectTransform;
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = new Vector2(10f, 10f);
            mapRect.offsetMax = new Vector2(-10f, -10f);
            mapRect.localScale = Vector3.one;
            minimap.transform.SetAsLastSibling();
        }

        private void BuildItemHud(RectTransform root)
        {
            RectTransform itemRootRect = CreatePanel("ItemHUD_Right", root, new Vector2(-48f, -350f),
                new Vector2(360f, 230f), Color.clear, 0f, 0f, 0f, 0f,
                new Vector2(1f, 1f), new Vector2(1f, 1f));
            itemHudRoot = itemRootRect.gameObject;
            RectTransform roulettePanel = CreatePanel("RoulettePanel", itemRootRect, Vector2.zero,
                new Vector2(360f, 112f), NavySoft, 10f, 0f, 10f, 0f,
                new Vector2(0f, 1f), new Vector2(0f, 1f));
            rouletteRoot = roulettePanel.gameObject;
            for (int i = 0; i < RouletteItems.Length; i++)
            {
                float x = 7f + i * 87f;
                Image card = CreateImage("Item Card " + i, roulettePanel, new Vector2(x, -8f),
                    new Vector2(80f, 96f), ItemColor(RouletteItems[i], 0.34f));
                Sprite icon = ItemIcon(RouletteItems[i]);
                Image iconImage = CreateImage("Icon", card.rectTransform, new Vector2(40f, -12f),
                    new Vector2(50f, 50f), icon == null ? ItemColor(RouletteItems[i], 0.95f) : White);
                iconImage.preserveAspect = true;
                iconImage.sprite = icon;
                TMP_Text fallback = CreateText(ItemShortName(RouletteItems[i]), "Fallback Label", card.rectTransform,
                    new Vector2(4f, -65f), new Vector2(72f, 23f), 13f, White, TextAlignmentOptions.Center);
                fallback.gameObject.SetActive(icon == null);
            }
            selectionFrame = CreateImage("Selection Frame", roulettePanel, new Vector2(7f, -8f),
                new Vector2(80f, 96f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            selectionFrame.transform.SetAsLastSibling();
            selectionPointer = CreateText("^", "Selection Pointer", roulettePanel, new Vector2(34f, -104f),
                new Vector2(25f, 20f), 22f, Cyan, TextAlignmentOptions.Center);

            RectTransform heldPanel = CreatePanel("HeldItemPanel", itemRootRect, new Vector2(0f, -124f),
                new Vector2(360f, 92f), Navy, 0f, 10f, 0f, 10f,
                new Vector2(0f, 1f), new Vector2(0f, 1f));
            heldItemIcon = CreateImage("Held Item Icon", heldPanel, new Vector2(18f, -13f),
                new Vector2(66f, 66f), new Color(1f, 1f, 1f, 0.12f));
            heldItemIcon.preserveAspect = true;
            heldItemName = CreateText("EMPTY", "Held Item Name", heldPanel, new Vector2(98f, -20f),
                new Vector2(230f, 30f), 21f, White, TextAlignmentOptions.Left);
            heldItemState = CreateText("COLLECT A GIFT BOX", "Held Item State", heldPanel, new Vector2(98f, -52f),
                new Vector2(230f, 23f), 13f, Muted, TextAlignmentOptions.Left);
        }

        private void BuildHandPanel(RectTransform root)
        {
            RectTransform panel = CreatePanel("HandControl_BottomLeft", root, new Vector2(56f, 44f),
                new Vector2(330f, 76f), NavySoft, 12f, 0f, 0f, 12f,
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            CreateText("HAND CONTROL", "Hand Title", panel, new Vector2(16f, 17f),
                new Vector2(145f, 23f), 15f, Cyan, TextAlignmentOptions.Left);
            handStatus = CreateText("HAND: DISCONNECTED", "Hand Status", panel, new Vector2(16f, -9f),
                new Vector2(294f, 24f), 16f, White, TextAlignmentOptions.Left);
            handDetails = CreateText("STEER --   DRIFT --   SKILL --", "Hand Details", panel, new Vector2(16f, -39f),
                new Vector2(294f, 20f), 12f, Muted, TextAlignmentOptions.Left);
        }

        private void RefreshLiveValues()
        {
            if (!layoutReady || raceManager == null || playerCar == null)
            {
                hudState = "Waiting for race references";
                return;
            }
            // Only replace live race values. Labels, fonts and positions are user-editable.
            if (positionValue != null)
                positionValue.SetText("<color=#FFD20A>{0}</color> / {1}", raceManager.PlayerPosition, raceManager.RacerCount);

            int currentLap = raceManager.CurrentLap;
            if (lapValue != null)
                lapValue.SetText("{0} / {1}", currentLap, raceManager.TotalLaps);

            if (Application.isPlaying && currentLap > lastObservedLap)
            {
                lastObservedLap = currentLap;
                if (currentLap == raceManager.TotalLaps)
                {
                    ShowEventBanner("FINAL LAP!", $"{currentLap} / {raceManager.TotalLaps}", 2.2f);
                }
                else
                {
                    ShowEventBanner($"LAP {currentLap} / {raceManager.TotalLaps}", "KEEP GOING!", 2.0f);
                }
                if (playerCar != null)
                {
                    PoseKart.Audio.AudioManager.PlayPickup(playerCar, playerCar.transform.position);
                }
            }
            else if (Application.isPlaying && currentLap < lastObservedLap)
            {
                lastObservedLap = currentLap;
            }

            if (progressValue != null) progressValue.text = showProgress ? "PROGRESS        --" : string.Empty;

            Rigidbody body = playerCar.TargetBody;
            Vector3 velocity = body == null ? Vector3.zero : body.linearVelocity;
            velocity.y = 0f;
            actualSpeedKmh = velocity.magnitude * 3.6f;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, valueSmoothing) * Time.unscaledDeltaTime);
            displayedSpeed = Mathf.Lerp(displayedSpeed, actualSpeedKmh, blend);
            if (speedValue != null)
                speedValue.SetText("{0:000}", Mathf.RoundToInt(displayedSpeed));
            if (timerValue != null)
                timerValue.text = FormatTime(raceManager.RaceTimeSeconds);

            float maximumNitro = Mathf.Max(0.01f, playerCar.MaxNitroChargeAmount);
            float nitro = Mathf.Clamp01(playerCar.StoredNitro / maximumNitro);
            displayedNitro = Mathf.Lerp(displayedNitro, nitro, blend);
            if (nitroFill != null)
            {
                nitroFill.fillAmount = displayedNitro;
                // Preserve the Nitro fill color chosen in Edit Mode.
                nitroFill.color = playerCar.IsBoosting
                    ? Color.Lerp(normalNitroFillColor, White, 0.35f)
                    : normalNitroFillColor;
            }
            SetActive(driftCalloutRoot, playerCar.IsDrifting);
            SetActive(airCalloutRoot, playerCar.IsAirAction && !playerCar.IsGrounded);
            SetActive(nitroCalloutRoot, playerCar.IsBoosting);
            hudState = raceManager.State.ToString();
        }


        private void RefreshItemHud()
        {
            if (playerInventory == null ||
                heldItemName == null ||
                heldItemIcon == null ||
                heldItemState == null)
                return;

            bool hasItem = playerInventory.HasItem;
            KartItemPickup.ItemType item = playerInventory.CurrentItem;
            bool noTarget = playerInventory.NoTargetFeedbackActive;

            // No item is currently held.
            if (!hasItem)
            {
                heldItemName.text = "EMPTY";
                heldItemState.text = "COLLECT A GIFT BOX";

                heldItemIcon.sprite = null;
                heldItemIcon.color =
                    new Color(1f, 1f, 1f, 0.12f);

                itemHudState = "EMPTY";
                return;
            }

            // Hide the actual reward during roulette.
            if (roulettePlaying)
            {
                heldItemName.text = "???";
                heldItemState.text = "ROLLING...";

                heldItemIcon.sprite = mysteryItemIcon;
                heldItemIcon.color = mysteryItemIcon != null
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.12f);

                itemHudState = "ROLLING";
                return;
            }

            // Reveal the actual reward only when roulette ends.
            heldItemName.text = item == KartItemPickup.ItemType.HomingMissile
                ? $"{ItemDisplayName(item)} x{playerInventory.ItemCharges}"
                : ItemDisplayName(item);
            heldItemIcon.sprite = ItemIcon(item);

            heldItemIcon.color = heldItemIcon.sprite != null
                ? Color.white
                : ItemColor(item, 0.9f);

            heldItemState.text = noTarget
                ? "NO TARGET"
                : "READY - SPACE / SKILL";

            itemHudState = noTarget ? "NO TARGET" : "READY";
        }


        private void RefreshHandPanel()
        {
            if (handStatus == null) return;
            if (poseTracker == null && playerCar != null)
                poseTracker = playerCar.GetComponent<PoseKart.Input.LocalPoseTracker>();
            string status;
            if (poseTracker == null) status = "HAND: DISCONNECTED";
            else if (poseTracker.TrackingValid && poseTracker.Fresh) status = "HAND: CONNECTED";
            else if (poseTracker.Mode == "Connecting" || (poseTracker.Status != null && poseTracker.Status.Contains("Connecting"))) status = "HAND: CONNECTING";
            else status = "HAND: WAITING";
            if (status != lastHandText)
            {
                lastHandText = status;
                handConnectionState = status;
                handStatus.text = status;
                handStatus.color = status == "HAND: CONNECTED" ? Cyan : White;
            }
            if (!showHandDetails || poseTracker == null)
            {
                handDetails.text = "STEER --   DRIFT --   SKILL --";
                return;
            }
            handDetails.SetText("STEER {0:+0.00;-0.00;0.00}   DRIFT {1:0}   SKILL {2:0}",
                poseTracker.Steering, poseTracker.Drift ? 1f : 0f, poseTracker.SkillSequence > 0 ? 1f : 0f);
        }

        private void HandleItemCollected(KartItemPickup.ItemType item)
        {
            if (Application.isPlaying) BeginRoulette(item);
        }

        private void HandleItemUsed(KartItemPickup.ItemType item)
        {
            roulettePlaying = false;
            rouletteHideAt = Time.unscaledTime + 0.15f;
            ShowEventBanner("ITEM USED", ItemDisplayName(item), 0.35f);
        }

        private void HandleItemActivationFailed(KartItemPickup.ItemType item)
        {
            if (item == KartItemPickup.ItemType.HomingMissile ||
                item == KartItemPickup.ItemType.InkShot)
            {
                ShowEventBanner("NO TARGET", "GET CLOSER TO A RIVAL", 1.1f);
            }
        }

        private void HandleItemExpired(KartItemPickup.ItemType item)
        {
            ShowEventBanner("ITEM EXPIRED", ItemDisplayName(item), 0.8f);
        }

        private void BeginRoulette(KartItemPickup.ItemType item)
        {
            rouletteTargetIndex = ItemIndex(item);
            rouletteStartedAt = Time.unscaledTime;
            rouletteEndsAt = rouletteStartedAt + Mathf.Max(0.35f, rouletteDuration);
            rouletteHideAt = 0f;
            roulettePlaying = true;
            rouletteLastIndex = -1;
            SetActive(itemHudRoot, true);
            SetActive(rouletteRoot, true);
            ShowEventBanner("ITEM BOX", "RANDOM ITEM", 1.1f);
            UpdateRouletteVisual(0);
        }

        private void UpdateRoulette()
        {
            if (!Application.isPlaying) return;
            if (roulettePlaying)
            {
                float duration = Mathf.Max(0.01f, rouletteEndsAt - rouletteStartedAt);
                float progress = Mathf.Clamp01((Time.unscaledTime - rouletteStartedAt) / duration);
                int totalSteps = 12 + rouletteTargetIndex;
                int step = Mathf.Min(totalSteps, Mathf.FloorToInt(progress * totalSteps));
                int index = step >= totalSteps ? rouletteTargetIndex : step % RouletteItems.Length;
                if (index != rouletteLastIndex)
                {
                    rouletteLastIndex = index;
                    UpdateRouletteVisual(index);
                    PoseKart.Audio.AudioManager.PlayRouletteTick();
                }
                if (progress >= 1f)
                {
                    roulettePlaying = false;
                    UpdateRouletteVisual(rouletteTargetIndex);
                    rouletteHideAt = Time.unscaledTime + 0.15f;
                }
            }
            if (rouletteHideAt > 0f &&
    Time.unscaledTime >= rouletteHideAt &&
    !roulettePlaying)
            {
                rouletteHideAt = 0f;
                SetActive(rouletteRoot, false);
            }
            if (eventBannerGroup != null)
            {
                if (eventBannerUntil > 0f && Time.unscaledTime >= eventBannerUntil) eventBannerGroup.alpha = 0f;
                else if (eventBannerUntil > 0f) eventBannerGroup.alpha = 1f;
            }
        }

        private void UpdateRouletteVisual(int index)
        {
            // Change only horizontal position; do NOT reset a designer's
            // RectTransform anchors, pivots, size or vertical placement.
            if (rouletteRoot == null) return;
            index = Mathf.Clamp(index, 0, RouletteItems.Length - 1);
            RectTransform card = rouletteRoot.transform.Find("Item Card " + index) as RectTransform;
            if (card == null) return;
            if (selectionFrame != null)
            {
                RectTransform frame = selectionFrame.rectTransform;
                Vector2 position = frame.anchoredPosition;
                position.x = card.anchoredPosition.x + rouletteFrameOffsetX;
                frame.anchoredPosition = position;
            }
            if (selectionPointer != null)
            {
                RectTransform pointer = selectionPointer.rectTransform;
                Vector2 position = pointer.anchoredPosition;
                position.x = card.anchoredPosition.x + roulettePointerOffsetX;
                pointer.anchoredPosition = position;
            }
        }

        private void ShowEventBanner(string title, string subtitle, float seconds)
        {
            if (eventBannerGroup == null) return;
            if (eventBannerTitle != null) eventBannerTitle.text = title;
            if (eventBannerSubtitle != null) eventBannerSubtitle.text = subtitle;
            eventBannerUntil = Time.unscaledTime + seconds;
            eventBannerGroup.alpha = 1f;
        }

        public void HideEventBanner()
        {
            eventBannerUntil = 0f;
            if (eventBannerGroup != null)
            {
                eventBannerGroup.alpha = 0f;
            }
        }

        private void EnsurePauseButton()
        {
            if (pauseButton == null)
            {
                Transform root = transform.Find("ArcadeHUD_Root");
                if (root != null)
                {
                    pauseButton = FindComponent<Button>(root, "RaceInfo_TopLeft/ESC");
                    if (pauseButton == null)
                    {
                        Button[] buttons = root.GetComponentsInChildren<Button>(true);
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            if (buttons[i] != null && buttons[i].name.Equals("ESC", StringComparison.OrdinalIgnoreCase))
                            {
                                pauseButton = buttons[i];
                                break;
                            }
                        }
                    }
                }
            }

            if (pauseButton != null && !pauseButtonBound)
            {
                pauseButton.onClick.RemoveListener(TogglePause);
                pauseButton.onClick.AddListener(TogglePause);
                pauseButtonBound = true;
            }
        }

        private void UnbindPauseButton()
        {
            if (pauseButton != null)
            {
                pauseButton.onClick.RemoveListener(TogglePause);
            }
            pauseButtonBound = false;
        }

        private void CheckPauseInput()
        {
            if (raceManager != null && raceManager.PlayerFinished)
            {
                if (isPaused) SetPaused(false);
                return;
            }

            bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                escPressed = true;
            }
#endif
            if (!escPressed)
            {
                try
                {
                    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                        escPressed = true;
                }
                catch { }
            }

            if (escPressed)
            {
                TogglePause();
            }
        }

        public void TogglePause()
        {
            SetPaused(!isPaused);
        }

        public void SetPaused(bool pause)
        {
            if (isPaused == pause) return;

            isPaused = pause;

            if (isPaused)
            {
                savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
                PoseKart.Audio.AudioManager.PlayButtonClick();
                Time.timeScale = 0f;
                AudioListener.pause = true;
                ShowEventBanner("PAUSED", "CLICK ESC OR PRESS ESC TO RESUME", float.MaxValue);
            }
            else
            {
                AudioListener.pause = false;
                PoseKart.Audio.AudioManager.PlayButtonClick();
                Time.timeScale = savedTimeScale;
                HideEventBanner();
            }
        }

        private RectTransform CreatePanel(string objectName, Transform parent, Vector2 position, Vector2 size,
            Color color, float topLeftCut, float topRightCut, float bottomRightCut, float bottomLeftCut,
            Vector2 anchor, Vector2 pivot)
        {
            GameObject panelObject = GetOrCreate(objectName, parent);
            PoseKartHUDPanelGraphic panel = panelObject.GetComponent<PoseKartHUDPanelGraphic>();
            if (panel == null) panel = panelObject.AddComponent<PoseKartHUDPanelGraphic>();
            RectTransform rect = panel.rectTransform;
            SetRect(rect, anchor, pivot, position, size);
            panel.color = color;
            panel.ConfigureCuts(topLeftCut, topRightCut, bottomRightCut, bottomLeftCut);
            return rect;
        }

        private TMP_Text CreateText(string value, string objectName, Transform parent, Vector2 position,
            Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject textObject = GetOrCreate(objectName, parent);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (text == null) text = textObject.AddComponent<TextMeshProUGUI>();
            SetRect(text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color(0f, 0f, 0f, 0.75f);
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        private Image CreateImage(string objectName, RectTransform parent, Vector2 position, Vector2 size, Color color)
        {
            GameObject imageObject = GetOrCreate(objectName, parent);
            Image image = imageObject.GetComponent<Image>();
            if (image == null) image = imageObject.AddComponent<Image>();
            SetRect(image.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private GameObject CreateCallout(RectTransform parent, string objectName, string value, float y, out TMP_Text text)
        {
            RectTransform panel = CreatePanel(objectName, parent, new Vector2(0f, y), new Vector2(320f, 48f),
                NavySoft, 8f, 0f, 8f, 0f, new Vector2(0f, 1f), new Vector2(0f, 1f));
            CreateImage("Accent", panel, new Vector2(302f, 0f), new Vector2(7f, 48f), Cyan);
            text = CreateText(value, objectName + " Text", panel, new Vector2(18f, -7f),
                new Vector2(270f, 34f), 21f, White, TextAlignmentOptions.Right);
            return panel.gameObject;
        }

        private GameObject GetOrCreate(string objectName, Transform parent)
        {
            Transform existing = parent.Find(objectName);
            if (existing != null) return existing.gameObject;
            GameObject result = new GameObject(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private void SetActiveIfFound(string objectName, bool active)
        {
            Transform child = transform.Find(objectName);
            if (child != null) child.gameObject.SetActive(active);
        }

        private static void SetActiveChild(Transform parent, string objectName, bool active)
        {
            Transform child = parent.Find(objectName);
            if (child != null) child.gameObject.SetActive(active);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private void MarkEditorDirty()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif
        }

        private Sprite ItemIcon(KartItemPickup.ItemType item)
        {
            switch (item)
            {
                case KartItemPickup.ItemType.InkShot: return inkIcon;
                case KartItemPickup.ItemType.HomingMissile: return missileIcon;
                case KartItemPickup.ItemType.BananaTrap: return bananaIcon;
                case KartItemPickup.ItemType.Shield: return shieldIcon;
                default: return null;
            }
        }

        private string ItemDisplayName(KartItemPickup.ItemType item)
        {
            switch (item)
            {
                case KartItemPickup.ItemType.InkShot: return "INK SHOT";
                case KartItemPickup.ItemType.HomingMissile: return "HOMING MISSILE";
                case KartItemPickup.ItemType.BananaTrap: return "BANANA TRAP";
                case KartItemPickup.ItemType.Shield: return "SHIELD";
                default: return item.ToString();
            }
        }

        private static string ItemShortName(KartItemPickup.ItemType item)
        {
            switch (item)
            {
                case KartItemPickup.ItemType.InkShot: return "INK";
                case KartItemPickup.ItemType.HomingMissile: return "MISSILE";
                case KartItemPickup.ItemType.BananaTrap: return "BANANA";
                case KartItemPickup.ItemType.Shield: return "SHIELD";
                default: return "ITEM";
            }
        }

        private static int ItemIndex(KartItemPickup.ItemType item)
        {
            for (int i = 0; i < RouletteItems.Length; i++) if (RouletteItems[i] == item) return i;
            return 0;
        }

        private static Color ItemColor(KartItemPickup.ItemType item, float alpha)
        {
            Color color;

            switch (item)
            {
                case KartItemPickup.ItemType.InkShot:
                    color = new Color(0.65f, 0.35f, 0.95f, 1f);
                    break;

                case KartItemPickup.ItemType.HomingMissile:
                    color = Red;
                    break;

                case KartItemPickup.ItemType.BananaTrap:
                    color = Banana;
                    break;

                case KartItemPickup.ItemType.Shield:
                    color = Shield;
                    break;

                default:
                    color = White;
                    break;
            }

            color.a = alpha;
            return color;
        }

        private static string FormatTime(double seconds)
        {
            seconds = Math.Max(0d, seconds);
            int minutes = (int)(seconds / 60d);
            int wholeSeconds = (int)(seconds % 60d);
            int milliseconds = (int)((seconds - Math.Floor(seconds)) * 1000d);
            return $"{minutes:00}:{wholeSeconds:00}.{milliseconds:000}";
        }
    }
}
