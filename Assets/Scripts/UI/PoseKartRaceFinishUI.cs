using System;
using System.Text;
using PoseKart.Race;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PoseKart.Multiplayer;
namespace PoseKart.UI
{
    public enum FinishEditorPreviewMode
    {
        ResultsPanel,
        FinishBanner,
        Both,
        Hidden
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PoseKartRaceFinishUI : MonoBehaviour
    {
        // =========================================================
        // COLORS
        // =========================================================
        private Transform darkOverlay;
        private static readonly Color Yellow =
            new Color(1f, 0.82f, 0.22f, 1f);

        private static readonly Color White =
            new Color(0.96f, 0.99f, 1f, 1f);

        private static readonly Color Silver =
            new Color(0.82f, 0.88f, 0.94f, 1f);

        private static readonly Color Bronze =
            new Color(0.84f, 0.52f, 0.28f, 1f);


        // =========================================================
        // INSPECTOR
        // =========================================================

        [Header("Editor Preview")]
        [SerializeField]
        private bool showInEditMode = true;

        [SerializeField]
        private FinishEditorPreviewMode editorPreviewMode =
            FinishEditorPreviewMode.ResultsPanel;


        [Header("Race")]
        [SerializeField]
        private RaceManager raceManager;

        [SerializeField]
        private string vehicleSelectScene =
            "PoseKart_VehicleSelect";


        [Header("Presentation")]
        [SerializeField, Min(0.5f)]
        private float finishDuration = 1.15f;

        [SerializeField, Min(0.1f)]
        private float resultsRevealDuration = 0.5f;

        [SerializeField, Min(0f)]
        private float buttonUnlockDelay = 0.25f;


        // =========================================================
        // AUTO-WIRED UI
        // Không SerializeField -> không hiện dài ngoằng Inspector
        // =========================================================

        private Transform finishAnimation;
        private CanvasGroup finishGroup;
        private RectTransform finishRect;

        private Transform resultsPanel;
        private CanvasGroup resultsGroup;
        private RectTransform resultsRect;

        private Button playAgainButton;
        private Button selectVehicleButton;

        private EventSystem eventSystem;


        // =========================================================
        // TEXT REFERENCES
        // Tự hỗ trợ cả TMP và Legacy Text
        // =========================================================

        private readonly TextBinding resultHeading =
            new TextBinding();

        private readonly TextBinding positionValue =
            new TextBinding();

        private readonly TextBinding totalTime =
            new TextBinding();

        private readonly TextBinding laps =
            new TextBinding();

        private readonly TextBinding resultHint =
            new TextBinding();

        private readonly TextBinding leaderboard =
            new TextBinding();


        // =========================================================
        // RUNTIME
        // =========================================================

        private float presentationTime;
        private bool presentationStarted;
        private bool resultsShown;

        private bool defaultButtonSelected;

        private float leaderboardRefreshTimer;

        private const float LeaderboardRefreshRate = 0.10f;


        // =========================================================
        // TEXT ADAPTER
        // =========================================================

        private sealed class TextBinding
        {
            private TMP_Text tmp;
            private Text legacy;

            public bool IsBound =>
                tmp != null || legacy != null;

            public void Bind(Transform target)
            {
                tmp = null;
                legacy = null;

                if (target == null)
                    return;

                tmp = target.GetComponent<TMP_Text>();
                legacy = target.GetComponent<Text>();
            }

            public void SetText(string value)
            {
                if (tmp != null)
                    tmp.text = value;

                if (legacy != null)
                    legacy.text = value;
            }

            public void SetColor(Color color)
            {
                if (tmp != null)
                    tmp.color = color;

                if (legacy != null)
                    legacy.color = color;
            }
        }


        // =========================================================
        // AUTO WIRE
        // =========================================================

        [ContextMenu("Auto-Wire UI Elements")]
        public void AutoWireHierarchy()
        {
            /*
             * Script này phải nằm trên:
             *
             * RaceFinishUI
             * ├── FinishAnimation
             * └── ResultsPanel
             */

            Transform root = transform;

            finishAnimation =
                FindDeepChild(root, "FinishAnimation");

            resultsPanel =
                FindDeepChild(root, "ResultsPanel");


            // FINISH ANIMATION
            if (finishAnimation != null)
            {
                finishGroup =
                    finishAnimation.GetComponent<CanvasGroup>();

                finishRect =
                    finishAnimation.GetComponent<RectTransform>();
            }
            else
            {
                finishGroup = null;
                finishRect = null;
            }

            if (darkOverlay != null)
            {
                darkOverlay.gameObject.SetActive(false);
            }
            // RESULTS PANEL
            if (resultsPanel != null)
            {
                resultsGroup =
                    resultsPanel.GetComponent<CanvasGroup>();

                resultsRect =
                    resultsPanel.GetComponent<RectTransform>();


                resultHeading.Bind(
                    FindDeepChild(resultsPanel, "Heading")
                );

                positionValue.Bind(
                    FindDeepChild(resultsPanel, "PositionValue")
                );

                totalTime.Bind(
                    FindDeepChild(resultsPanel, "TotalTime")
                );

                laps.Bind(
                    FindDeepChild(resultsPanel, "Laps")
                );

                resultHint.Bind(
                    FindDeepChild(resultsPanel, "ResultHint")
                );

                leaderboard.Bind(
                    FindDeepChild(resultsPanel, "Leaderboard")
                );


                Transform playAgain =
                    FindDeepChild(resultsPanel, "PlayAgain");

                if (playAgain == null)
                {
                    playAgain =
                        FindDeepChild(
                            resultsPanel,
                            "PlayAgain_Button"
                        );
                }

                playAgainButton =
                    playAgain != null
                        ? playAgain.GetComponent<Button>()
                        : null;


                Transform selectVehicle =
                    FindDeepChild(
                        resultsPanel,
                        "SelectVehicle"
                    );

                if (selectVehicle == null)
                {
                    selectVehicle =
                        FindDeepChild(
                            resultsPanel,
                            "SelectVehicle_Button"
                        );
                }

                selectVehicleButton =
                    selectVehicle != null
                        ? selectVehicle.GetComponent<Button>()
                        : null;
            }
            else
            {
                resultsGroup = null;
                resultsRect = null;

                playAgainButton = null;
                selectVehicleButton = null;
            }
        }


        // =========================================================
        // UNITY LIFECYCLE
        // =========================================================

        private void Awake()
        {
            AutoWireHierarchy();

            if (!Application.isPlaying)
            {
                ApplyEditorPreview();
                return;
            }

            ResolveRaceManager();
            InitializeRuntimeUI();
        }


        private void OnEnable()
        {
            darkOverlay =
    FindDeepChild(transform, "DarkOverlay");
            AutoWireHierarchy();

            if (!Application.isPlaying)
            {
                ApplyEditorPreview();
                return;
            }

            ResolveRaceManager();
            RegisterButtons();

            if (raceManager != null)
            {
                // Tránh subscribe trùng
                raceManager.PlayerFinishedEvent -=
                    HandlePlayerFinished;

                raceManager.PlayerFinishedEvent +=
                    HandlePlayerFinished;
            }
        }


        private void Start()
        {
            if (!Application.isPlaying)
                return;

            if (raceManager != null &&
                raceManager.PlayerFinished)
            {
                HandlePlayerFinished(raceManager);
            }
        }


        private void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            if (raceManager != null)
            {
                raceManager.PlayerFinishedEvent -=
                    HandlePlayerFinished;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            // FAIL-SAFE:
            // Nếu vì execution order mà miss PlayerFinishedEvent,
            // tự kiểm tra trạng thái RaceManager mỗi frame.
            if (!presentationStarted)
            {
                if (raceManager == null)
                    ResolveRaceManager();

                if (raceManager != null && raceManager.PlayerFinished)
                {
                    Debug.Log(
                        "[RaceFinishUI] PlayerFinished detected -> showing Finish UI.",
                        this
                    );

                    HandlePlayerFinished(raceManager);
                }

                return;
            }

            float dt = Time.unscaledDeltaTime;
            presentationTime += dt;

            // FINISH banner
            if (!resultsShown)
            {
                AnimateFinish();

                if (presentationTime >= finishDuration)
                {
                    RevealResults();
                }

                return;
            }

            // Results
            AnimateResults();

            leaderboardRefreshTimer -= dt;

            if (leaderboardRefreshTimer <= 0f)
            {
                leaderboardRefreshTimer = LeaderboardRefreshRate;
                RefreshLeaderboard();
            }
        }


        private void OnValidate()
        {
            finishDuration =
                Mathf.Max(0.5f, finishDuration);

            resultsRevealDuration =
                Mathf.Max(0.1f, resultsRevealDuration);

            buttonUnlockDelay =
                Mathf.Max(0f, buttonUnlockDelay);
        }


        // =========================================================
        // INITIALIZATION
        // =========================================================

        private void ResolveRaceManager()
        {
            if (raceManager == null)
            {
                raceManager =
                    FindFirstObjectByType<RaceManager>();
            }
        }


        private void InitializeRuntimeUI()
        {
            /*
             * QUAN TRỌNG:
             *
             * KHÔNG SetActive(false) RaceFinishUI.
             * Nếu tắt root thì script cũng bị tắt và không nhận
             * PlayerFinishedEvent.
             */

            if (finishAnimation != null)
            {
                finishAnimation.gameObject.SetActive(true);
            }

            if (resultsPanel != null)
            {
                resultsPanel.gameObject.SetActive(true);
            }


            SetCanvasGroup(
                finishGroup,
                0f,
                false
            );

            SetCanvasGroup(
                resultsGroup,
                0f,
                false
            );


            if (finishRect != null)
            {
                finishRect.localScale =
                    Vector3.one;
            }

            if (resultsRect != null)
            {
                resultsRect.localScale =
                    Vector3.one;
            }


            presentationStarted = false;
            resultsShown = false;
            defaultButtonSelected = false;

            RegisterButtons();
            SetButtonsInteractable(false);
        }


        private void RegisterButtons()
        {
            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(
                    PlayAgain
                );

                playAgainButton.onClick.AddListener(
                    PlayAgain
                );
            }


            if (selectVehicleButton != null)
            {
                selectVehicleButton.onClick.RemoveListener(
                    SelectVehicle
                );

                selectVehicleButton.onClick.AddListener(
                    SelectVehicle
                );
            }
        }


        // =========================================================
        // PLAYER FINISHED
        // =========================================================

        private void HandlePlayerFinished(RaceManager manager)
        {
            if (presentationStarted ||
                manager == null ||
                !manager.PlayerFinished)
            {
                return;
            }

            Debug.Log(
                "[RaceFinishUI] HandlePlayerFinished CALLED",
                this
            );

            presentationStarted = true;
            resultsShown = false;
            presentationTime = 0f;
            if (darkOverlay != null)
            {
                darkOverlay.gameObject.SetActive(true);
            }
            leaderboardRefreshTimer = 0f;
            defaultButtonSelected = false;

            gameObject.SetActive(true);

            if (finishAnimation != null)
                finishAnimation.gameObject.SetActive(true);

            if (resultsPanel != null)
                resultsPanel.gameObject.SetActive(true);

            // FINISH hiện trước
            SetCanvasGroup(
                finishGroup,
                1f,
                false
            );

            if (finishRect != null)
                finishRect.localScale = Vector3.one * 0.78f;

            // Result ẩn trong lúc FINISH animation
            SetCanvasGroup(
                resultsGroup,
                0f,
                false
            );

            if (resultsRect != null)
                resultsRect.localScale = Vector3.one * 0.94f;

            BuildResultValues(manager);

            SetButtonsInteractable(false);

            PoseKart.Audio.AudioManager.PlayRaceFinish();
        }


        // =========================================================
        // RESULT VALUES
        // =========================================================

        private void BuildResultValues(
            RaceManager manager)
        {
            if (manager == null)
                return;


            int position =
                manager.PlayerFinishPosition;


            // POSITION
            positionValue.SetText(
                PositionLabel(position)
            );

            positionValue.SetColor(
                PositionColor(position)
            );


            // HEADING
            resultHeading.SetText(
                position == 1
                    ? "VICTORY!"
                    : "RACE COMPLETE"
            );


            // TIME
            totalTime.SetText(
                $"TOTAL TIME\n" +
                $"{FormatTime(manager.PlayerFinishTimeSeconds)}"
            );


            // LAPS
            laps.SetText(
                $"LAPS\n" +
                $"{manager.CurrentLap} / {manager.TotalLaps}"
            );


            // RESULT
            resultHint.SetText(
                $"OFFICIAL RESULT  •  " +
                $"{position} / {manager.RacerCount}"
            );


            RefreshLeaderboard();
        }


        // =========================================================
        // LEADERBOARD
        // =========================================================

        private void RefreshLeaderboard()
        {
            if (raceManager == null)
                return;

            leaderboard.SetText(
                BuildLeaderboardText(raceManager)
            );
        }


        public string BuildLeaderboardText(
            RaceManager manager)
        {
            if (manager == null ||
                manager.Racers == null ||
                manager.Racers.Length == 0)
            {
                return "";
            }


            RaceParticipant[] ordered =
                (RaceParticipant[])
                manager.Racers.Clone();


            Array.Sort(
                ordered,
                CompareRacers
            );


            StringBuilder builder =
                new StringBuilder();


            int written = 0;

            for (int i = 0;
                 i < ordered.Length;
                 i++)
            {
                RaceParticipant racer =
                    ordered[i];

                if (racer == null)
                    continue;


                if (written > 0)
                {
                    builder.AppendLine();
                }


                int position =
                    racer.Finished &&
                    racer.FinishOrder > 0
                        ? racer.FinishOrder
                        : racer.RacePosition;


                string racerName =
                    string.IsNullOrWhiteSpace(
                        racer.RacerName
                    )
                        ? racer.name
                        : racer.RacerName;


                if (racer.IsPlayer)
                {
                    racerName += "  (YOU)";
                }


                string raceResult =
                    racer.Finished
                        ? FormatTime(
                            racer.FinishTime
                        )
                        : "RACING";


                builder
                    .Append(position)
                    .Append(".  ")
                    .Append(racerName)
                    .Append("    ")
                    .Append(raceResult);


                written++;
            }


            return builder.ToString();
        }


        private static int CompareRacers(
            RaceParticipant a,
            RaceParticipant b)
        {
            if (a == null && b == null)
                return 0;

            if (a == null)
                return 1;

            if (b == null)
                return -1;


            // Xe đã finish luôn đứng trên xe chưa finish.
            if (a.Finished && !b.Finished)
                return -1;

            if (!a.Finished && b.Finished)
                return 1;


            // Cả hai đã finish -> thứ tự chính thức.
            if (a.Finished && b.Finished)
            {
                return a.FinishOrder.CompareTo(
                    b.FinishOrder
                );
            }


            // Cả hai vẫn đang chạy.
            return a.RacePosition.CompareTo(
                b.RacePosition
            );
        }


        // =========================================================
        // FINISH ANIMATION
        // =========================================================

        private void AnimateFinish()
        {
            float duration =
                Mathf.Max(
                    0.01f,
                    finishDuration
                );


            float t =
                Mathf.Clamp01(
                    presentationTime /
                    duration
                );


            float punch =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(
                        presentationTime /
                        0.30f
                    )
                );


            float scale =
                Mathf.Lerp(
                    0.78f,
                    1f,
                    punch
                );


            // Cuối animation co nhẹ lại.
            if (t > 0.72f)
            {
                scale =
                    Mathf.Lerp(
                        1f,
                        0.94f,
                        (t - 0.72f) /
                        0.28f
                    );
            }


            if (finishRect != null)
            {
                finishRect.localScale =
                    Vector3.one * scale;
            }


            if (finishGroup != null)
            {
                finishGroup.alpha =
                    t > 0.78f
                        ? Mathf.Lerp(
                            1f,
                            0f,
                            (t - 0.78f) /
                            0.22f
                        )
                        : 1f;
            }
        }


        // =========================================================
        // REVEAL RESULTS
        // =========================================================

        private void RevealResults()
        {
            resultsShown = true;
            presentationTime = 0f;


            SetCanvasGroup(
                finishGroup,
                0f,
                false
            );


            SetCanvasGroup(
                resultsGroup,
                0f,
                true
            );


            if (resultsRect != null)
            {
                resultsRect.localScale =
                    Vector3.one * 0.94f;
            }


            RefreshLeaderboard();


            PoseKart.Audio.AudioManager
                .PlayResultsReveal();


            if (raceManager != null &&
                raceManager.PlayerFinishPosition == 1)
            {
                PoseKart.Audio.AudioManager
                    .PlayVictory();
            }
        }


        // =========================================================
        // RESULTS ANIMATION
        // =========================================================

        private void AnimateResults()
        {
            float reveal =
                Mathf.Clamp01(
                    presentationTime /
                    Mathf.Max(
                        0.01f,
                        resultsRevealDuration
                    )
                );


            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    reveal
                );


            if (resultsGroup != null)
            {
                resultsGroup.alpha = eased;
            }


            if (resultsRect != null)
            {
                resultsRect.localScale =
                    Vector3.one *
                    Mathf.Lerp(
                        0.94f,
                        1f,
                        eased
                    );
            }


            bool unlocked =
                presentationTime >=
                resultsRevealDuration +
                buttonUnlockDelay;


            SetButtonsInteractable(
                unlocked
            );


            if (unlocked &&
                !defaultButtonSelected)
            {
                defaultButtonSelected = true;

                eventSystem =
                    EventSystem.current;


                if (eventSystem != null &&
                    playAgainButton != null)
                {
                    eventSystem
                        .SetSelectedGameObject(
                            playAgainButton.gameObject
                        );
                }
            }
        }


        // =========================================================
        // BUTTONS
        // =========================================================

        public void PlayAgain()
        {
            PoseKart.Audio.AudioManager
                .PlayButtonClick();

            Time.timeScale = 1f;


            PoseKartOnlineRaceRestart onlineRestart =
                FindFirstObjectByType<
                    PoseKartOnlineRaceRestart>();


            if (onlineRestart != null)
            {
                onlineRestart.RequestPlayAgain();

                return;
            }


            // Offline fallback.
            SceneManager.LoadScene(
                SceneManager
                    .GetActiveScene()
                    .buildIndex
            );
        }


        public void SelectVehicle()
        {
            PoseKart.Audio.AudioManager
                .PlayButtonClick();


            if (!Application
                .CanStreamedLevelBeLoaded(
                    vehicleSelectScene
                ))
            {
                resultHint.SetText(
                    $"SCENE NOT IN BUILD SETTINGS: " +
                    $"{vehicleSelectScene}"
                );

                return;
            }


            Time.timeScale = 1f;

            SceneManager.LoadScene(
                vehicleSelectScene
            );
        }


        private void SetButtonsInteractable(
            bool interactable)
        {
            if (playAgainButton != null)
            {
                playAgainButton.interactable =
                    interactable;
            }


            if (selectVehicleButton != null)
            {
                selectVehicleButton.interactable =
                    interactable;
            }


            if (resultsGroup != null)
            {
                resultsGroup.blocksRaycasts =
                    interactable;

                resultsGroup.interactable =
                    interactable;
            }
        }


        // =========================================================
        // EDITOR PREVIEW
        // =========================================================

        public void ApplyEditorPreview()
        {
            if (Application.isPlaying)
                return;


            bool showFinish =
                showInEditMode &&
                (
                    editorPreviewMode ==
                    FinishEditorPreviewMode
                        .FinishBanner
                    ||
                    editorPreviewMode ==
                    FinishEditorPreviewMode
                        .Both
                );


            bool showResults =
                showInEditMode &&
                (
                    editorPreviewMode ==
                    FinishEditorPreviewMode
                        .ResultsPanel
                    ||
                    editorPreviewMode ==
                    FinishEditorPreviewMode
                        .Both
                );


            if (editorPreviewMode ==
                FinishEditorPreviewMode.Hidden)
            {
                showFinish = false;
                showResults = false;
            }


            SetCanvasGroup(
                finishGroup,
                showFinish ? 1f : 0f,
                false
            );


            SetCanvasGroup(
                resultsGroup,
                showResults ? 1f : 0f,
                false
            );


            if (finishRect != null)
            {
                finishRect.localScale =
                    Vector3.one;
            }


            if (resultsRect != null)
            {
                resultsRect.localScale =
                    Vector3.one;
            }
        }


        // =========================================================
        // HELPERS
        // =========================================================

        private static void SetCanvasGroup(
            CanvasGroup group,
            float alpha,
            bool visible)
        {
            if (group == null)
                return;

            group.alpha = alpha;

            group.interactable =
                visible;

            group.blocksRaycasts =
                visible;
        }


        private static string PositionLabel(
            int position)
        {
            switch (position)
            {
                case 1:
                    return "1ST";

                case 2:
                    return "2ND";

                case 3:
                    return "3RD";

                default:
                    return $"{position}TH";
            }
        }


        private static Color PositionColor(
            int position)
        {
            switch (position)
            {
                case 1:
                    return Yellow;

                case 2:
                    return Silver;

                case 3:
                    return Bronze;

                default:
                    return White;
            }
        }


        private static string FormatTime(
            double totalSeconds)
        {
            if (totalSeconds <= 0d)
            {
                return "00:00.000";
            }


            TimeSpan span =
                TimeSpan.FromSeconds(
                    totalSeconds
                );


            return
                $"{(int)span.TotalMinutes:00}:" +
                $"{span.Seconds:00}." +
                $"{span.Milliseconds:000}";
        }


        private static Transform FindDeepChild(
            Transform parent,
            string objectName)
        {
            if (parent == null ||
                string.IsNullOrEmpty(objectName))
            {
                return null;
            }


            for (int i = 0;
                 i < parent.childCount;
                 i++)
            {
                Transform child =
                    parent.GetChild(i);


                if (child.name == objectName)
                {
                    return child;
                }


                Transform nested =
                    FindDeepChild(
                        child,
                        objectName
                    );


                if (nested != null)
                {
                    return nested;
                }
            }


            return null;
        }
    }
}