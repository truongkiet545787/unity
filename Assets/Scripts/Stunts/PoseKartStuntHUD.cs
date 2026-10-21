using System.Collections;
using TMPro;
using UnityEngine;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Asphalt 8-style HUD presentation for Airborne Stunts.
    /// Displays in-air dynamic stunt revolution counters (FLAT SPIN x2!, BARREL ROLL x3!),
    /// combo banners, clean/crash landing announcements, and accumulated Nitro rewards with scale punch bounce.
    /// All UI references are editable in the Unity Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartStuntHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PoseKartStuntController playerStuntController;
        [SerializeField] private TMP_Text stuntNameText;
        [SerializeField] private TMP_Text revolutionCountText;
        [SerializeField] private TMP_Text nitroRewardText;
        [SerializeField] private TMP_Text comboText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform punchTransform;

        [Header("Animation Tuning")]
        [SerializeField, Min(0.1f)] private float inAirVisibleDuration = 1.0f;
        [SerializeField, Min(0.1f)] private float landingVisibleDuration = 1.4f;
        [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.25f;
        [SerializeField, Range(1.1f, 1.8f)] private float punchScale = 1.35f;
        [SerializeField, Min(0.05f)] private float punchDuration = 0.22f;

        [Header("Category Colors")]
        [SerializeField] private Color flatSpinColor = new Color(0f, 0.95f, 1f, 1f);     // Cyan
        [SerializeField] private Color barrelRollColor = new Color(1f, 0.85f, 0.1f, 1f); // Gold
        [SerializeField] private Color flipColor = new Color(1f, 0.3f, 0.75f, 1f);       // Magenta
        [SerializeField] private Color cleanLandingColor = new Color(0.1f, 1f, 0.5f, 1f); // Green
        [SerializeField] private Color crashLandingColor = new Color(1f, 0.25f, 0.25f, 1f); // Red

        private Coroutine displayRoutine;
        private Coroutine punchRoutine;
        private Vector3 initialPunchScale = Vector3.one;

        private void Awake()
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (punchTransform == null && stuntNameText != null)
            {
                punchTransform = stuntNameText.rectTransform;
            }
            if (punchTransform != null)
            {
                initialPunchScale = punchTransform.localScale;
            }
        }

        private void Start()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (playerStuntController == null)
            {
                // Tìm PoseKartStuntController trên xe người chơi
                var allControllers = FindObjectsByType<PoseKartStuntController>(FindObjectsSortMode.None);
                for (int i = 0; i < allControllers.Length; i++)
                {
                    if (allControllers[i].CompareTag("Player") ||
                        allControllers[i].gameObject.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        playerStuntController = allControllers[i];
                        break;
                    }
                }

                if (playerStuntController == null && allControllers.Length > 0)
                {
                    playerStuntController = allControllers[0];
                }
            }

            SubscribeEvents();
        }

        public void ConfigureUI(
            TMP_Text stuntText,
            TMP_Text revText,
            TMP_Text rewardText,
            TMP_Text combo,
            CanvasGroup group,
            RectTransform punch)
        {
            stuntNameText = stuntText;
            revolutionCountText = revText;
            nitroRewardText = rewardText;
            comboText = combo;
            canvasGroup = group;
            punchTransform = punch;
            if (punchTransform != null)
            {
                initialPunchScale = punchTransform.localScale;
            }
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            if (displayRoutine != null) StopCoroutine(displayRoutine);
            if (punchRoutine != null) StopCoroutine(punchRoutine);
            displayRoutine = null;
            punchRoutine = null;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (punchTransform != null) punchTransform.localScale = initialPunchScale;
        }

        private void SubscribeEvents()
        {
            if (playerStuntController == null) return;
            playerStuntController.RevolutionCompleted -= OnRevolutionCompleted;
            playerStuntController.RevolutionCompleted += OnRevolutionCompleted;
            playerStuntController.StuntLanded -= OnStuntLanded;
            playerStuntController.StuntLanded += OnStuntLanded;
        }

        private void UnsubscribeEvents()
        {
            if (playerStuntController == null) return;
            playerStuntController.RevolutionCompleted -= OnRevolutionCompleted;
            playerStuntController.StuntLanded -= OnStuntLanded;
        }

        private void OnRevolutionCompleted(StuntType type, int revCount)
        {
            if (canvasGroup == null) return;

            string stuntTitle;
            Color titleColor;

            switch (type)
            {
                case StuntType.FlatSpin:
                    stuntTitle = "FLAT SPIN";
                    titleColor = flatSpinColor;
                    break;
                case StuntType.BarrelRoll:
                    stuntTitle = "BARREL ROLL";
                    titleColor = barrelRollColor;
                    break;
                case StuntType.FrontFlip:
                    stuntTitle = "FRONT FLIP";
                    titleColor = flipColor;
                    break;
                case StuntType.BackFlip:
                    stuntTitle = "BACK FLIP";
                    titleColor = flipColor;
                    break;
                default:
                    stuntTitle = "STUNT";
                    titleColor = Color.white;
                    break;
            }

            if (stuntNameText != null)
            {
                stuntNameText.text = stuntTitle;
                stuntNameText.color = titleColor;
            }

            if (revolutionCountText != null)
            {
                revolutionCountText.text = $"x{revCount}!";
                revolutionCountText.color = Color.white;
            }

            if (comboText != null)
            {
                comboText.text = revCount > 1 ? "<color=#FFCC00>STUNT COMBO!</color>" : "";
            }

            if (nitroRewardText != null)
            {
                nitroRewardText.text = "";
            }

            ShowBanner(inAirVisibleDuration);
            TriggerPunch();
        }

        private void OnStuntLanded(StuntType type, LandingQuality quality, float rewardFraction, int revCount)
        {
            if (canvasGroup == null) return;

            int rewardPercent = Mathf.RoundToInt(rewardFraction * 100f);

            if (quality == LandingQuality.CleanLanding)
            {
                if (stuntNameText != null)
                {
                    stuntNameText.text = revCount > 1 ? "STUNT COMBO!" : "CLEAN LANDING!";
                    stuntNameText.color = cleanLandingColor;
                }

                if (revolutionCountText != null)
                {
                    revolutionCountText.text = revCount > 0 ? $"x{revCount}" : "";
                    revolutionCountText.color = Color.white;
                }

                if (comboText != null)
                {
                    comboText.text = "PERFECT LANDING!";
                    comboText.color = cleanLandingColor;
                }

                if (nitroRewardText != null)
                {
                    nitroRewardText.text = rewardPercent > 0 ? $"+{rewardPercent}% NITRO" : "";
                    nitroRewardText.color = Color.yellow;
                }
            }
            else if (quality == LandingQuality.CrashLanding)
            {
                if (stuntNameText != null)
                {
                    stuntNameText.text = "CRASH LANDING!";
                    stuntNameText.color = crashLandingColor;
                }

                if (revolutionCountText != null)
                {
                    revolutionCountText.text = "";
                }

                if (comboText != null)
                {
                    comboText.text = "INCOMPLETE ROTATION";
                    comboText.color = Color.gray;
                }

                if (nitroRewardText != null)
                {
                    nitroRewardText.text = "";
                }
            }

            ShowBanner(landingVisibleDuration);
            TriggerPunch();
        }

        private void ShowBanner(float visibleSeconds)
        {
            if (displayRoutine != null) StopCoroutine(displayRoutine);
            displayRoutine = StartCoroutine(AnimateBanner(visibleSeconds));
        }

        private IEnumerator AnimateBanner(float visibleSeconds)
        {
            if (canvasGroup == null) yield break;
            canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(visibleSeconds);

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                canvasGroup.alpha = 1f - t;
                yield return null;
            }

            canvasGroup.alpha = 0f;
            displayRoutine = null;
        }

        private void TriggerPunch()
        {
            if (punchTransform == null) return;
            if (punchRoutine != null) StopCoroutine(punchRoutine);
            punchRoutine = StartCoroutine(AnimatePunch());
        }

        private IEnumerator AnimatePunch()
        {
            if (punchTransform == null) yield break;

            punchTransform.localScale = initialPunchScale * punchScale;
            float halfDuration = punchDuration * 0.5f;

            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                punchTransform.localScale = Vector3.Lerp(initialPunchScale * punchScale, initialPunchScale, t);
                yield return null;
            }

            punchTransform.localScale = initialPunchScale;
            punchRoutine = null;
        }
    }
}
