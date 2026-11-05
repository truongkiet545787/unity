using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// Displays the player's real stored Nitro charge and boost state.
    /// This component never consumes, refills, or simulates Nitro.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PoseKartNitroMeterUI : MonoBehaviour
    {
        private static readonly Color Track = new Color(0.06f, 0.075f, 0.1f, 0.9f);
        private static readonly Color Cyan = new Color(1f, 0.78f, 0.08f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.9f, 0.22f, 1f);
        private static readonly Color Empty = new Color(0.28f, 0.34f, 0.42f, 1f);
        private static readonly Color White = new Color(0.96f, 0.99f, 1f, 1f);

        [Header("Gameplay references")]
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private CarController playerCar;

        [Header("Meter layout")]
        [Tooltip("Top-center HUD position. Negative Y moves the meter down from the top edge.")]
        [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, -42f);
        [SerializeField] private Vector2 size = new Vector2(640f, 56f);
        [SerializeField] private bool showPercentage;

        [Header("Fill animation")]
        [Tooltip("Charge movement per second. Lower values make pickup/drift charge visibly fill the bar.")]
        [SerializeField, Min(0.1f)] private float fillSmoothingSpeed = 4.5f;
        [SerializeField] private Color normalFillColor = Cyan;
        [SerializeField] private Color activeFillColor = Yellow;
        [SerializeField] private Color emptyFillColor = Empty;

        [Header("Nitro feedback")]
        [Tooltip("Shows TAP only while the existing CarController state is inside the Perfect Nitro window.")]
        [SerializeField] private bool showPerfectTapCue = true;
        [Tooltip("Shows SHOCKWAVE only during the existing full-tank double-tap window.")]
        [SerializeField] private bool showShockwaveCue = true;
        [Tooltip("Shows the current Nitro stage as a small label. Double is presented as Explosive.")]
        [SerializeField] private bool showModeLabel = true;
        [SerializeField, Min(8)] private int cueFontSize = 18;
        [SerializeField] private string perfectTapCueText = "TAP";
        [SerializeField] private string shockwaveCueText = "SHOCKWAVE";

        [Header("Runtime debug (Read Only)")]
        [SerializeField, Range(0f, 1f)] private float actualFill;
        [SerializeField, Range(0f, 1f)] private float displayedFill;
        [SerializeField] private float currentNitroAmount;
        [SerializeField] private float maximumNitroAmount;
        [SerializeField] private bool nitroAvailable;
        [SerializeField] private bool nitroActive;
        [SerializeField] private string meterState = "Waiting for player car";

        private RectTransform meterRoot;
        private Image backgroundImage;
        private Image fillTrackImage;
        private Image fillImage;
        private NitroBarGraphic barGraphic;
        private Text labelText;
        private Text percentageText;
        private Text perfectTapCueTextComponent;
        private Text shockwaveCueTextComponent;
        private float nextResolveTime;
        private bool interfaceBuilt;
        private CarController subscribedCar;

        private void Awake()
        {
            if (raceManager == null)
            {
                raceManager = FindFirstObjectByType<RaceManager>();
            }

            BuildInterface();
            ResolvePlayerCar();
        }

        private void OnEnable()
        {
            BuildInterface();
        }

        private void OnDisable()
        {
            UnsubscribeFromCar();
        }

        private void OnDestroy()
        {
            UnsubscribeFromCar();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolvePlayerCar();
            UpdateMeter(Time.unscaledDeltaTime);
        }

        private void ResolvePlayerCar()
        {
            if (PoseKartLocalPlayerResolver.IsLocalHuman(playerCar))
            {
                SubscribeToCar(playerCar);
                return;
            }

            if (Time.unscaledTime < nextResolveTime)
            {
                return;
            }

            nextResolveTime = Time.unscaledTime + 0.5f;
            CarController resolvedCar =
                PoseKartLocalPlayerResolver.FindLocalCar(raceManager);
            if (resolvedCar != playerCar)
            {
                UnsubscribeFromCar();
                playerCar = resolvedCar;

                // A pickup can be confirmed by the Server/owner RPC before
                // this HUD has finished binding after a scene load or player
                // spawn. Start from the car's authoritative value so that
                // the missed event cannot leave the meter visually empty.
                if (playerCar != null &&
                    PoseKartLocalPlayerResolver.IsLocalHuman(playerCar))
                {
                    displayedFill = playerCar.NitroFill01;
                }
            }
            SubscribeToCar(playerCar);
        }

        private void SubscribeToCar(CarController car)
        {
            if (subscribedCar == car)
                return;

            UnsubscribeFromCar();
            if (car == null)
                return;

            subscribedCar = car;
            subscribedCar.NitroChargeChanged += HandleNitroChargeChanged;
        }

        private void UnsubscribeFromCar()
        {
            if (subscribedCar == null)
                return;

            subscribedCar.NitroChargeChanged -= HandleNitroChargeChanged;
            subscribedCar = null;
        }

        private void HandleNitroChargeChanged()
        {
            // The event is raised by the car we actually subscribed to. During
            // scene loading/rebind, playerCar can briefly still point at the
            // previous clone, so reading that field here can make a confirmed
            // pickup look as if the meter did not change. Prefer the event
            // source and keep the local binding in sync before refreshing.
            CarController changedCar = subscribedCar != null ? subscribedCar : playerCar;
            if (!Application.isPlaying || !isActiveAndEnabled || changedCar == null)
                return;

            if (!PoseKartLocalPlayerResolver.IsLocalHuman(changedCar))
                return;

            playerCar = changedCar;

            // Pickup/drift charge is gameplay-authoritative. Skip the visual
            // easing for this upward edge so a confirmed pickup is visible
            // immediately instead of appearing to be ignored by the HUD.
            displayedFill = Mathf.Max(displayedFill, changedCar.NitroFill01);
            if (fillImage != null)
                fillImage.fillAmount = displayedFill;
            if (barGraphic != null)
            {
                barGraphic.TargetCar = changedCar;
                barGraphic.AutoSyncWithCar = false;
                barGraphic.SetNitroMode(
                    changedCar.CurrentNitroMode,
                    displayedFill,
                    changedCar.IsNitroBoosting);
                barGraphic.TrailingFillAmount = changedCar.NitroFill01;
            }
        }

        private void BuildInterface()
        {

            if (interfaceBuilt &&
                meterRoot != null &&
                barGraphic != null &&
                fillImage != null &&
                labelText != null &&
                percentageText != null &&
                perfectTapCueTextComponent != null &&
                shockwaveCueTextComponent != null)
            {
                return;
            }


            // This component is commonly attached directly to the scene's
            // NitroMeterUI object. Reuse that object instead of creating a
            // second NitroMeterUI underneath it after OnEnable/domain reload.
            GameObject rootObject = string.Equals(gameObject.name, "NitroMeterUI",
                System.StringComparison.Ordinal)
                ? gameObject
                : GetOrCreateChild(transform, "NitroMeterUI", out _);
            meterRoot = rootObject.GetComponent<RectTransform>();

            // Older scene versions keep a valid Bar directly under this
            // object. Do not disable it here: some existing scenes use that
            // serialized NitroBarGraphic as their only visible meter. The
            // generated child remains compatible, and visibility is managed
            // by the scene/HUD hierarchy instead of hiding a serialized bar.
            Transform legacyBar = transform.Find("Bar");
            if (legacyBar != null && legacyBar != rootObject.transform)
                legacyBar.gameObject.SetActive(true);

            // Asphalt-style top-center layout. Apply it to the existing scene
            // instance as well as newly created UI so the old bottom-right
            // parchment layout cannot silently win after a domain reload.
            meterRoot.anchorMin = new Vector2(0.5f, 1f);
            meterRoot.anchorMax = new Vector2(0.5f, 1f);
            meterRoot.pivot = new Vector2(0.5f, 1f);
            meterRoot.anchoredPosition = anchoredPosition;
            meterRoot.sizeDelta = size;

            // Keep the dedicated meter above the other RaceHUD children so the
            // top-center bar remains readable over the gameplay HUD.
            meterRoot.SetAsLastSibling();

            backgroundImage = GetOrAddComponent<Image>(rootObject);
            backgroundImage.color = Color.clear;
            backgroundImage.raycastTarget = false;

            GameObject barObject = GetOrCreateChild(rootObject.transform, "Bar", out _);
            barObject.SetActive(true);
            RectTransform barRect = barObject.GetComponent<RectTransform>();
            SetAnchored(barRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Mathf.Max(180f, size.x), Mathf.Max(24f, size.y - 14f)),
                new Vector2(0.5f, 0.5f));
            fillTrackImage = GetOrAddComponent<Image>(barObject);
            fillTrackImage.color = Track;
            fillTrackImage.raycastTarget = false;
            fillTrackImage.enabled = false;

            // NitroBarGraphic must have its own GameObject.
            // Unity UI Image and custom Graphic must not share the same GameObject.
            GameObject graphicObject = GetOrCreateChild(
                barObject.transform,
                "NitroBarGraphic",
                out _
            );

            RectTransform graphicRect = graphicObject.GetComponent<RectTransform>();

            graphicRect.anchorMin = Vector2.zero;
            graphicRect.anchorMax = Vector2.one;
            graphicRect.offsetMin = Vector2.zero;
            graphicRect.offsetMax = Vector2.zero;
            graphicRect.pivot = new Vector2(0.5f, 0.5f);

            barGraphic = GetOrAddComponent<NitroBarGraphic>(graphicObject);

            if (barGraphic == null)
            {
                Debug.LogError(
                    "[PoseKartNitroMeterUI] Failed to create NitroBarGraphic. " +
                    "Check the NitroBarGraphic component and UI hierarchy.",
                    this
                );
                return;
            }

            graphicObject.SetActive(true);
            barGraphic.enabled = true;
            barGraphic.raycastTarget = false;
            barGraphic.SetVisuals(displayedFill, Track, normalFillColor, White);

            GameObject fillObject = GetOrCreateChild(barObject.transform, "Fill", out _);
            fillObject.SetActive(true);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillImage = GetOrAddComponent<Image>(fillObject);
            if (fillImage.sprite != null)
                fillTrackImage.sprite = fillImage.sprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = displayedFill;
            fillImage.color = normalFillColor;
            fillImage.raycastTarget = false;
            fillImage.enabled = false;

            Transform icon = rootObject.transform.Find("Icon");
            if (icon != null)
                icon.gameObject.SetActive(false);

            

            labelText = GetOrCreateText("Label", "NITRO", rootObject.transform, 16, White,
                TextAnchor.MiddleLeft, out bool labelCreated);
            if (labelCreated)
            {
                SetAnchored(labelText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(48f, -21f), new Vector2(-108f, 25f), new Vector2(0f, 0.5f));
            }
            // The Asphalt-style bar communicates charge visually; the old
            // parchment label is intentionally hidden for the 1:1 top HUD.
            labelText.gameObject.SetActive(false);

            perfectTapCueTextComponent = GetOrCreateText(
                "PerfectTapCue",
                perfectTapCueText,
                rootObject.transform,
                cueFontSize,
                new Color(0.35f, 0.95f, 1f, 1f),
                TextAnchor.MiddleCenter,
                out bool perfectCueCreated);
            if (perfectCueCreated)
            {
                SetAnchored(perfectTapCueTextComponent.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -2f), new Vector2(120f, 22f),
                    new Vector2(0.5f, 1f));
            }
            else
            {
                SetAnchored(perfectTapCueTextComponent.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -2f), new Vector2(120f, 22f),
                    new Vector2(0.5f, 1f));
            }
            if (labelText.font != null)
                perfectTapCueTextComponent.font = labelText.font;
            perfectTapCueTextComponent.fontSize = cueFontSize;
            perfectTapCueTextComponent.text = perfectTapCueText;
            perfectTapCueTextComponent.gameObject.SetActive(false);

            shockwaveCueTextComponent = GetOrCreateText(
                "ShockwaveCue",
                shockwaveCueText,
                rootObject.transform,
                cueFontSize,
                new Color(0.95f, 0.55f, 1f, 1f),
                TextAnchor.MiddleCenter,
                out bool shockwaveCueCreated);
            if (shockwaveCueCreated)
            {
                SetAnchored(shockwaveCueTextComponent.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -2f), new Vector2(180f, 22f),
                    new Vector2(0.5f, 1f));
            }
            else
            {
                SetAnchored(shockwaveCueTextComponent.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -2f), new Vector2(180f, 22f),
                    new Vector2(0.5f, 1f));
            }
            if (labelText.font != null)
                shockwaveCueTextComponent.font = labelText.font;
            shockwaveCueTextComponent.fontSize = cueFontSize;
            shockwaveCueTextComponent.text = shockwaveCueText;
            shockwaveCueTextComponent.gameObject.SetActive(false);

            percentageText = GetOrCreateText("Percentage", "000%", rootObject.transform, 15, White,
                TextAnchor.MiddleRight, out bool percentageCreated);
            if (percentageCreated)
            {
                SetAnchored(percentageText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-12f, -21f), new Vector2(76f, 25f), new Vector2(1f, 0.5f));
            }
            percentageText.gameObject.SetActive(showPercentage);

            interfaceBuilt = true;
#if UNITY_EDITOR
            if (!Application.isPlaying && gameObject.scene.IsValid())
            {
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif
        }

        private void UpdateMeter(float deltaTime)
        {

            if (meterRoot == null ||
                fillImage == null ||
                barGraphic == null ||
                labelText == null ||
                percentageText == null)
            {
                // Reconnect UI references if a child was changed or removed.
                interfaceBuilt = false;
                BuildInterface();
            }

            // Avoid NullReferenceException if UI references are still missing.
            if (meterRoot == null ||
                fillImage == null ||
                barGraphic == null ||
                labelText == null ||
                percentageText == null)
            {
                return;
            }

            // Keep the value read by the polling path identical to the object
            // that raised NitroChargeChanged. This matters when a network
            // player is rebound during spawn/scene transitions.
            if (subscribedCar != null)
                playerCar = subscribedCar;

            bool hasCar = playerCar != null && playerCar.isActiveAndEnabled;
            if (!hasCar)
            {
                meterState = "Waiting for player car";
                actualFill = 0f;
                currentNitroAmount = 0f;
                maximumNitroAmount = 0f;
                nitroAvailable = false;
                nitroActive = false;
                UpdateModeLabel(CarController.NitroMode.None, false, White);
                UpdateFeedbackCues(null);
                displayedFill = Mathf.MoveTowards(displayedFill, 0f, fillSmoothingSpeed * deltaTime);
                // Keep the empty track visible while the race manager is still
                // resolving the spawned player car. Hiding the whole root made
                // the Nitro HUD appear to be missing during scene startup.
                meterRoot.gameObject.SetActive(true);
                fillImage.fillAmount = displayedFill;
                fillImage.color = emptyFillColor;
                barGraphic.TargetCar = null;
                barGraphic.AutoSyncWithCar = false;
                barGraphic.SetVisuals(displayedFill, Track, Color.clear, Color.clear);
                return;
            }

            meterRoot.gameObject.SetActive(true);
            currentNitroAmount = playerCar.StoredNitro;
            maximumNitroAmount = Mathf.Max(0.01f, playerCar.MaxNitroChargeAmount);
            actualFill = Mathf.Clamp01(currentNitroAmount / maximumNitroAmount);
            nitroAvailable = playerCar.NitroReady;
            nitroActive = playerCar.IsNitroBoosting;
            // Spending nitro follows real fuel instantaneously for frame-perfect timing
            if (nitroActive || actualFill < displayedFill)
                displayedFill = actualFill;
            else
                displayedFill = Mathf.MoveTowards(displayedFill, actualFill, fillSmoothingSpeed * deltaTime);
            fillImage.fillAmount = displayedFill;
            // This component owns the local-player binding and the smoothed
            // display value. Do not let NitroBarGraphic resolve an arbitrary
            // remote/AI car and overwrite it in its own Update().
            barGraphic.TargetCar = playerCar;
            barGraphic.AutoSyncWithCar = false;

            bool full = actualFill >= 0.999f;
            Color fillColor;
            Color highlightColor;
            if (nitroActive)
            {
                switch (playerCar.CurrentNitroMode)
                {
                    case CarController.NitroMode.Single:
                        meterState = "SINGLE";
                        fillColor = new Color(1f, 0.85f, 0.15f, 1f); // Warm Yellow
                        highlightColor = new Color(1f, 0.98f, 0.7f, 0.85f);
                        break;
                    case CarController.NitroMode.Double:
                        meterState = "DOUBLE";
                        fillColor = new Color(1f, 0.45f, 0.05f, 1f); // Fiery Orange
                        highlightColor = new Color(1f, 0.8f, 0.4f, 0.85f);
                        break;
                    case CarController.NitroMode.Perfect:
                        meterState = "PERFECT";
                        fillColor = new Color(0.12f, 0.88f, 1f, 1f); // Electric Cyan / Blue (Xanh)
                        highlightColor = new Color(0.7f, 0.98f, 1f, 0.9f);
                        break;
                    case CarController.NitroMode.Shockwave:
                        meterState = "SHOCKWAVE";
                        fillColor = new Color(0.82f, 0.22f, 1f, 1f); // Shockwave Purple (Tím)
                        highlightColor = new Color(0.95f, 0.65f, 1f, 0.95f);
                        break;
                    default:
                        meterState = "ACTIVE";
                        fillColor = activeFillColor;
                        highlightColor = new Color(1f, 0.98f, 0.72f, 0.72f);
                        break;
                }
                labelText.color = fillColor;
                if (labelText.gameObject.activeSelf)
                    labelText.text = meterState;
            }
            else if (actualFill <= 0.001f)
            {
                meterState = "EMPTY";
                fillColor = Color.clear;
                highlightColor = Color.clear;
                labelText.color = new Color(White.r, White.g, White.b, 0.56f);
            }
            else
            {
                meterState = full ? "FULL" : "AVAILABLE";
                fillColor = normalFillColor;
                highlightColor = new Color(1f, 0.98f, 0.72f, 0.52f);
                labelText.color = full ? White : Cyan;
            }

            // The gameplay value remains authoritative; only earned charge is
            // eased visually. NitroBarGraphic synchronizes the gradient and glow.
            barGraphic.SetNitroMode(playerCar.CurrentNitroMode, displayedFill, nitroActive);
            barGraphic.TrailingFillAmount = actualFill;
            fillImage.color = fillColor;

            UpdateModeLabel(playerCar.CurrentNitroMode, nitroActive, fillColor);
            UpdateFeedbackCues(playerCar);

            percentageText.gameObject.SetActive(showPercentage);
            if (showPercentage)
            {
                string percentage = $"{Mathf.RoundToInt(displayedFill * 100f):000}%";
                percentageText.text = percentage;
                percentageText.color = nitroActive ? fillColor : White;
            }
        }

        private void UpdateModeLabel(CarController.NitroMode mode, bool active, Color color)
        {
            if (labelText == null)
                return;

            labelText.gameObject.SetActive(showModeLabel && active);
            if (!showModeLabel || !active)
                return;

            labelText.text = mode switch
            {
                CarController.NitroMode.Single => "SINGLE",
                CarController.NitroMode.Double => "EXPLOSIVE",
                CarController.NitroMode.Perfect => "PERFECT",
                CarController.NitroMode.Shockwave => "SHOCKWAVE",
                _ => "NITRO"
            };
            labelText.color = color;
        }

        private void UpdateFeedbackCues(CarController car)
        {
            if (perfectTapCueTextComponent == null || shockwaveCueTextComponent == null)
                return;

            bool showShockwave = showShockwaveCue && car != null &&
                car.IsShockwaveTimingNow;
            bool showPerfectTap = showPerfectTapCue && car != null &&
                !showShockwave && car.IsPerfectTimingNow;

            perfectTapCueTextComponent.gameObject.SetActive(showPerfectTap);
            shockwaveCueTextComponent.gameObject.SetActive(showShockwave);

            if (showPerfectTap)
            {
                float pulse = 0.65f + 0.35f *
                    (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f));
                perfectTapCueTextComponent.color = new Color(0.35f, 0.95f, 1f, pulse);
            }

            if (showShockwave)
            {
                float pulse = 0.7f + 0.3f *
                    (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 16f));
                shockwaveCueTextComponent.color = new Color(0.95f, 0.55f, 1f, pulse);
            }
        }

        private static GameObject GetOrCreateChild(Transform parent, string objectName, out bool created)
        {
            Transform existing = parent != null ? parent.Find(objectName) : null;
            if (existing != null)
            {
                created = false;
                return existing.gameObject;
            }

            GameObject result = new GameObject(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            created = true;
            return result;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target != null ? target.GetComponent<T>() : null;
            return component != null ? component : target != null ? target.AddComponent<T>() : null;
        }

        private static Text GetOrCreateText(string objectName, string value, Transform parent,
            int fontSize, Color color, TextAnchor alignment, out bool created)
        {
            GameObject textObject = GetOrCreateChild(parent, objectName, out created);
            Text text = GetOrAddComponent<Text>(textObject);
            if (created)
            {
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = fontSize;
                text.fontStyle = FontStyle.Bold;
                text.alignment = alignment;
                text.color = color;
                text.text = value;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return text;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private void OnValidate()
        {
            size.x = Mathf.Max(180f, size.x);
            size.y = Mathf.Max(48f, size.y);
            fillSmoothingSpeed = Mathf.Max(0.1f, fillSmoothingSpeed);
            cueFontSize = Mathf.Max(8, cueFontSize);
        }
    }
}
