
using UnityEngine;

public class VehiclePreviewRotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 25f;
    [SerializeField] private bool rotate = true;

    [Header("Optional Hover Effect")]
    [SerializeField] private bool enableFloating = false;
    [SerializeField] private float floatHeight = 0.08f;
    [SerializeField] private float floatSpeed = 1.5f;

    private Vector3 startLocalPosition;

    private void OnEnable()
    {
        startLocalPosition = transform.localPosition;
    }

    private void Update()
    {
        if (rotate)
        {
            transform.Rotate(
                Vector3.up,
                rotationSpeed * Time.deltaTime,
                Space.Self
            );
        }

        if (enableFloating)
        {
            Vector3 pos = startLocalPosition;
            pos.y += Mathf.Sin(
                Time.time * floatSpeed
            ) * floatHeight;

            transform.localPosition = pos;
        }
    }

    public void SetRotation(bool enabled)
    {
        rotate = enabled;
    }

    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = speed;
    }
}
