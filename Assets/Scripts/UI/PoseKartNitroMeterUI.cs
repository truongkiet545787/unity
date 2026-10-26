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
        private float nextResolveTime;
        private bool interfaceBuilt;

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
                return;
            }

            if (Time.unscaledTime < nextResolveTime)
            {
                return;
            }

            nextResolveTime = Time.unscaledTime + 0.5f;
            playerCar = PoseKartLocalPlayerResolver.FindLocalCar(raceManager);
        }

        private void BuildInterface()
        {

            if (interfaceBuilt &&
                meterRoot != null &&
                barGraphic != null &&
                fillImage != null &&
                labelText != null &&
                percentageText != null)
            {
                return;
            }


            GameObject rootObject = GetOrCreateChild(transform, "NitroMeterUI", out _);
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

            barGraphic.raycastTarget = false;
            barGraphic.SetVisuals(displayedFill, Track, normalFillColor, White);

            GameObject fillObject = GetOrCreateChild(barObject.transform, "Fill", out _);
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

            bool hasCar = playerCar != null && playerCar.isActiveAndEnabled;
            if (!hasCar)
            {
                meterState = "Waiting for player car";
                actualFill = 0f;
                currentNitroAmount = 0f;
                maximumNitroAmount = 0f;
                nitroAvailable = false;
                nitroActive = false;
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
            fillImage.color = fillColor;

            percentageText.gameObject.SetActive(showPercentage);
            if (showPercentage)
            {
                percentageText.text = $"{Mathf.RoundToInt(actualFill * 100f):000}%";
                percentageText.color = nitroActive ? fillColor : White;
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
        }
    }
}
