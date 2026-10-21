
using UnityEngine;

public class CustomCursor : MonoBehaviour
{
    [Header("Cursor")]
    [SerializeField] private Texture2D cursorTexture;

    [Tooltip("Point inside the image used for clicking. (0,0) is top-left.")]
    [SerializeField] private Vector2 hotspot = Vector2.zero;

    private void OnEnable()
    {
        ApplyCursor();
    }

    private void ApplyCursor()
    {
        if (cursorTexture == null)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Cursor.SetCursor(
            cursorTexture,
            hotspot,
            CursorMode.Auto
        );
    }

    private void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
