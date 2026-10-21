using System.Collections;
using TMPro;
using UnityEngine;

namespace PoseKart.Stunts
{
    /// <summary>
    /// Asphalt 8-style HUD banner for stunts.
    /// Features in-air dynamic roll counter popups (x1, x2, x3...) with scale punch bounce,
    /// followed by the final Nitro reward announcement upon landing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartBarrelRollHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PoseKartBarrelRollController playerRollController;
        [SerializeField] private TMP_Text message;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform punchTransform;

        [Header("Animation")]
        [UnityEngine.Serialization.FormerlySerializedAs("visibleSeconds")]
        [SerializeField, Min(0.1f)] private float inAirVisibleSeconds = 1.0f;
        [SerializeField, Min(0.1f)] private float landingVisibleSeconds = 1.4f;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.25f;
        [SerializeField, Range(1.1f, 1.8f)] private float punchScale = 1.35f;
        [SerializeField, Min(0.05f)] private float punchDuration = 0.22f;

        private Coroutine displayRoutine;
        private Coroutine punchRoutine;
        private Vector3 initialPunchScale = Vector3.one;

        private void Awake()
        {
            if (group != null) group.alpha = 0f;
            if (punchTransform == null && message != null)
            {
                punchTransform = message.rectTransform;
            }
            if (punchTransform != null)
            {
                initialPunchScale = punchTransform.localScale;
            }
        }

        private void Start()
        {
            if (playerRollController == null)
            {
                playerRollController = FindFirstObjectByType<PoseKartBarrelRollController>();
                if (playerRollController != null)
                {
                    SubscribeEvents();
                }
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
            if (group != null) group.alpha = 0f;
            if (punchTransform != null) punchTransform.localScale = initialPunchScale;
        }

        private void SubscribeEvents()
        {
            if (playerRollController == null) return;
            playerRollController.RollStepCompleted -= OnRollStepCompleted;
            playerRollController.RollStepCompleted += OnRollStepCompleted;
            playerRollController.BarrelRollLanded -= OnBarrelRollLanded;
            playerRollController.BarrelRollLanded += OnBarrelRollLanded;
        }

        private void UnsubscribeEvents()
        {
            if (playerRollController == null) return;
            playerRollController.RollStepCompleted -= OnRollStepCompleted;
            playerRollController.BarrelRollLanded -= OnBarrelRollLanded;
        }

        private void OnRollStepCompleted(int completedRollCount)
        {
            if (message == null || group == null) return;

            string rollText = completedRollCount > 1
                ? $"<color=#FFD700>BARREL ROLL</color> <color=#00E5FF>x{completedRollCount}!</color>"
                : "<color=#FFD700>BARREL ROLL!</color>";

            ShowBanner(rollText, inAirVisibleSeconds);
            TriggerPunch();
        }

        private void OnBarrelRollLanded(PoseKartBarrelRollController controller)
        {
            if (message == null || group == null) return;

            int rolls = controller.CompletedRolls;
            int nitroPercent = Mathf.RoundToInt(controller.LastRewardFraction * 100f);

            string rollPrefix = rolls > 1 ? $"BARREL ROLL x{rolls}!" : "BARREL ROLL!";
            string bannerText = $"<color=#FFD700>{rollPrefix}</color>  <color=#00FF88>+{nitroPercent}% NITRO</color>";

            ShowBanner(bannerText, landingVisibleSeconds);
            TriggerPunch();
        }

        private void ShowBanner(string text, float holdDuration)
        {
            message.text = text;
            if (displayRoutine != null) StopCoroutine(displayRoutine);
            displayRoutine = StartCoroutine(DisplayRoutine(holdDuration));
        }

        private void TriggerPunch()
        {
            if (punchTransform == null) return;
            if (punchRoutine != null) StopCoroutine(punchRoutine);
            punchRoutine = StartCoroutine(PunchRoutine());
        }

        private IEnumerator PunchRoutine()
        {
            float elapsed = 0f;
            Vector3 startScale = initialPunchScale * punchScale;

            while (elapsed < punchDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / punchDuration);
                // Ease out back / elastic bounce feel
                float smoothT = 1f - Mathf.Pow(1f - t, 3f);
                punchTransform.localScale = Vector3.LerpUnclamped(startScale, initialPunchScale, smoothT);
                yield return null;
            }

            punchTransform.localScale = initialPunchScale;
            punchRoutine = null;
        }

        private IEnumerator DisplayRoutine(float holdDuration)
        {
            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(holdDuration);

            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / fadeSeconds);
                yield return null;
            }

            group.alpha = 0f;
            displayRoutine = null;
        }
    }
}
