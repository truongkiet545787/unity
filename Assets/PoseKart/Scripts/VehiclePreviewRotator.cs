using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Drag-to-spin showroom preview: reversed horizontal drag, smooth response,
/// and inertial rotation after releasing the mouse. No Collider required.
/// </summary>
public class VehiclePreviewRotator : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private bool rotate = true;

    [Header("Gamepad Rotation")]
    [SerializeField] private bool gamepadRotate = true;

    [Tooltip("Tốc độ xoay xe bằng Right Stick.")]
    [SerializeField, Range(30f, 360f)]
    private float gamepadRotationSpeed = 120f;

    [Tooltip("Deadzone của Right Stick.")]
    [SerializeField, Range(0f, 0.5f)]
    private float gamepadDeadzone = 0.15f;

    [SerializeField] private bool invertGamepad = false;
    [Tooltip("Drag sensitivity. 25 means roughly 0.5 degrees per mouse pixel.")]
    [SerializeField, Range(1f, 100f)]
    private float rotationSpeed = 25f;

    [Tooltip("Camera that renders the preview vehicle. Usually Main Camera.")]
    [SerializeField] private Camera previewCamera;

    [Tooltip("Extra clickable space around the on-screen vehicle.")]
    [SerializeField, Min(0f)] private float clickPadding = 20f;

    [Tooltip("ON reverses the rotation direction of the previous script.")]
    [SerializeField] private bool invertDrag = true;

    [Header("Smooth Showroom Feel")]
    [Tooltip("How long the spin takes to catch up with your hand. Higher = more delay.")]
    [SerializeField, Range(0.01f, 0.35f)]
    private float dragSmoothTime = 0.10f;

    [Tooltip("How quickly the vehicle slows after mouse release. Lower = longer inertia.")]
    [SerializeField, Range(0.5f, 15f)]
    private float releaseFriction = 4.5f;

    [Tooltip("Safety cap for the spin velocity (degrees per second).")]
    [SerializeField, Range(60f, 1080f)]
    private float maxSpinSpeed = 540f;

    [Header("Optional Hover Effect")]
    [SerializeField] private bool enableFloating = false;
    [SerializeField] private float floatHeight = 0.08f;
    [SerializeField] private float floatSpeed = 1.5f;

    private Vector3 startLocalPosition;
    private Vector2 lastMousePosition;
    private float currentSpinSpeed;
    private float spinSmoothVelocity;
    private bool isDragging;

    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    private void OnEnable()
    {
        startLocalPosition = transform.localPosition;
        isDragging = false;
        currentSpinSpeed = 0f;
        spinSmoothVelocity = 0f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
            return;

        HandleMouseDrag(dt);
        HandleGamepadRotation(dt);
        HandleFloating();
    }

    private void HandleMouseDrag(float dt)
    {
        Mouse mouse = Mouse.current;
        if (!rotate || mouse == null)
        {
            isDragging = false;
            currentSpinSpeed = 0f;
            spinSmoothVelocity = 0f;
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            isDragging = IsMouseOverVehicle(mousePosition)
                && !IsOverInteractiveUI(mousePosition);

            lastMousePosition = mousePosition;
            spinSmoothVelocity = 0f;
        }

        if (isDragging && mouse.leftButton.isPressed)
        {
            float deltaX = mousePosition.x - lastMousePosition.x;
            float direction = invertDrag ? -1f : 1f;
            float degreesPerPixel = rotationSpeed * 0.02f;

            float desiredSpinSpeed = Mathf.Clamp(
                (deltaX * degreesPerPixel * direction) / dt,
                -maxSpinSpeed,
                maxSpinSpeed);

            // The model eases toward the hand speed, instead of snapping to it.
            currentSpinSpeed = Mathf.SmoothDamp(
                currentSpinSpeed,
                desiredSpinSpeed,
                ref spinSmoothVelocity,
                dragSmoothTime,
                Mathf.Infinity,
                dt);
        }
        else
        {
            // Carry the remaining rotation into a smooth inertial coast.
            isDragging = false;
            spinSmoothVelocity = 0f;
            currentSpinSpeed *= Mathf.Exp(-releaseFriction * dt);

            if (Mathf.Abs(currentSpinSpeed) < 0.4f)
                currentSpinSpeed = 0f;
        }

        lastMousePosition = mousePosition;

        if (Mathf.Abs(currentSpinSpeed) > 0f)
        {
            transform.Rotate(
                Vector3.up,
                currentSpinSpeed * dt,
                Space.World);
        }
    }
    private void HandleGamepadRotation(float dt)
    {
        if (!rotate || !gamepadRotate)
            return;

        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        float stickX = pad.rightStick.x.ReadValue();

        // Deadzone để tránh stick bị drift nhẹ
        if (Mathf.Abs(stickX) < gamepadDeadzone)
            stickX = 0f;

        if (Mathf.Abs(stickX) <= 0f)
            return;

        float direction = invertGamepad ? -1f : 1f;

        transform.Rotate(
            Vector3.up,
            stickX * gamepadRotationSpeed * direction * dt,
            Space.World
        );
    }
    private bool IsMouseOverVehicle(Vector2 mousePosition)
    {
        Camera cam = previewCamera != null ? previewCamera : Camera.main;
        if (cam == null)
            return false;

        // Check only on mouse-down; not every frame.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
        Vector2 screenMin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 screenMax = new Vector2(float.MinValue, float.MinValue);
        bool found = false;

        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled)
                continue;

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);

                        Vector3 screen = cam.WorldToScreenPoint(corner);
                        if (screen.z <= 0f)
                            continue;

                        Vector2 projected = new Vector2(screen.x, screen.y);
                        screenMin = Vector2.Min(screenMin, projected);
                        screenMax = Vector2.Max(screenMax, projected);
                        found = true;
                    }
        }

        if (!found)
            return false;

        Rect clickArea = Rect.MinMaxRect(
            screenMin.x - clickPadding,
            screenMin.y - clickPadding,
            screenMax.x + clickPadding,
            screenMax.y + clickPadding);

        return clickArea.Contains(mousePosition);
    }

    private bool IsOverInteractiveUI(Vector2 mousePosition)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        uiHits.Clear();
        EventSystem.current.RaycastAll(eventData, uiHits);

        foreach (RaycastResult hit in uiHits)
        {
            if (hit.gameObject == null)
                continue;

            Selectable selectable = hit.gameObject.GetComponentInParent<Selectable>();
            if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
                return true;
        }

        return false;
    }

    private void HandleFloating()
    {
        if (!enableFloating)
            return;

        Vector3 pos = startLocalPosition;
        pos.y += Mathf.Sin(Time.unscaledTime * floatSpeed) * floatHeight;
        transform.localPosition = pos;
    }

    // Keep the public methods used by any existing vehicle-select UI scripts.
    public void SetRotation(bool enabled)
    {
        rotate = enabled;
        if (!enabled)
        {
            isDragging = false;
            currentSpinSpeed = 0f;
            spinSmoothVelocity = 0f;
        }
    }

    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = Mathf.Max(0f, speed);
    }

    private void OnDisable()
    {
        isDragging = false;
        currentSpinSpeed = 0f;
        spinSmoothVelocity = 0f;
    }
}
