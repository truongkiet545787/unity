using UnityEngine;

#if DOTWEEN_ENABLED
using DG.Tweening;
#endif

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Visual-only squash and stretch for a car. Assign a mesh/visual child to
    /// carMesh; never assign the Rigidbody root or a physics collider object.
    ///
    /// DOTween is used when the DOTWEEN_ENABLED scripting symbol is present.
    /// The small built-in fallback keeps the project compiling and preserves
    /// the effect when DOTween has not been imported yet.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PoseKart/Cartoon Car Animation")]
    public sealed class CartoonCarAnimation : MonoBehaviour
    {
        [Header("Visual reference")]
        [Tooltip("Visual child only. Do not assign the Rigidbody or a collider root.")]
        public Transform carMesh;

        [Header("Stretch on accelerate")]
        [SerializeField, Min(0f)] private float stretchDuration = 0.3f;
        [SerializeField, Min(0f)] private float stretchRecoveryDuration = 0.2f;
        [SerializeField, Min(0f)] private float stretchZ = 0.08f;
        [SerializeField, Range(0f, 0.5f)] private float stretchXY = 0.04f;

        [Header("Squash on impact")]
        [SerializeField, Min(0f)] private float squashDuration = 0.15f;
        [SerializeField, Range(0f, 0.5f)] private float squashY = 0.18f;
        [SerializeField, Range(0f, 0.5f)] private float squashX = 0.1f;

        private Vector3 baseScale;
        private bool initialized;

#if DOTWEEN_ENABLED
        private Tween activeTween;
#endif

        private enum FallbackMode
        {
            None,
            Stretch,
            SquashYoyo
        }

        private FallbackMode fallbackMode;
        private Vector3 fallbackPeak;
        private float fallbackElapsed;
        private bool fallbackReturning;

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (!initialized || fallbackMode == FallbackMode.None || carMesh == null)
                return;

            fallbackElapsed += Time.deltaTime;
            float duration = fallbackMode == FallbackMode.Stretch && fallbackReturning
                ? stretchRecoveryDuration
                : fallbackMode == FallbackMode.Stretch
                    ? stretchDuration
                    : squashDuration;

            float t = duration > 0f ? Mathf.Clamp01(fallbackElapsed / duration) : 1f;
            t = EaseOut(t);

            if (!fallbackReturning)
                carMesh.localScale = Vector3.Lerp(baseScale, fallbackPeak, t);
            else
                carMesh.localScale = Vector3.Lerp(fallbackPeak, baseScale, t);

            if (fallbackElapsed < duration)
                return;

            if (!fallbackReturning)
            {
                fallbackReturning = true;
                fallbackElapsed = 0f;
                return;
            }

            fallbackMode = FallbackMode.None;
            fallbackReturning = false;
            carMesh.localScale = baseScale;
        }

        /// <summary>
        /// Call when the car receives a meaningful acceleration impulse.
        /// Repeated calls safely replace the previous visual animation.
        /// </summary>
        public void StretchOnAccelerate()
        {
            if (!Initialize())
                return;

            Vector3 target = new Vector3(
                baseScale.x * (1f - stretchXY),
                baseScale.y * (1f - stretchXY),
                baseScale.z * (1f + stretchZ));

#if DOTWEEN_ENABLED
            StopTween();
            activeTween = DOTween.Sequence()
                .Append(carMesh.DOScale(target, stretchDuration).SetEase(Ease.OutQuad))
                .Append(carMesh.DOScale(baseScale, stretchRecoveryDuration).SetEase(Ease.OutQuad))
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
#else
            StartFallback(target, FallbackMode.Stretch);
#endif
        }

        /// <summary>
        /// Call for a hard brake, landing, collision or other impact.
        /// The Yoyo loop returns the visual to its original scale automatically.
        /// </summary>
        public void SquashOnImpact()
        {
            if (!Initialize())
                return;

            Vector3 target = new Vector3(
                baseScale.x * (1f + squashX),
                baseScale.y * (1f - squashY),
                baseScale.z);

#if DOTWEEN_ENABLED
            StopTween();
            activeTween = carMesh.DOScale(target, squashDuration)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
#else
            StartFallback(target, FallbackMode.SquashYoyo);
#endif
        }

        /// <summary>
        /// Restores the visual mesh without touching the Rigidbody or colliders.
        /// </summary>
        public void ResetVisualScale()
        {
            if (!Initialize())
                return;

#if DOTWEEN_ENABLED
            StopTween();
#endif
            fallbackMode = FallbackMode.None;
            fallbackReturning = false;
            carMesh.localScale = baseScale;
        }

        private bool Initialize()
        {
            if (initialized)
                return carMesh != null;
            if (carMesh == null)
                carMesh = transform.childCount > 0 ? transform.GetChild(0) : null;
            if (carMesh == null)
                return false;

            baseScale = carMesh.localScale;
            initialized = true;
            return true;
        }

        private void StartFallback(Vector3 target, FallbackMode mode)
        {
            fallbackMode = mode;
            fallbackPeak = target;
            fallbackElapsed = 0f;
            fallbackReturning = false;
            carMesh.localScale = baseScale;
        }

#if DOTWEEN_ENABLED
        private void StopTween()
        {
            if (activeTween == null)
                return;
            activeTween.Kill();
            activeTween = null;
        }
#endif

        private void OnDisable()
        {
#if DOTWEEN_ENABLED
            StopTween();
#endif
            fallbackMode = FallbackMode.None;
            fallbackReturning = false;
            if (initialized && carMesh != null)
                carMesh.localScale = baseScale;
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }
    }
}
