using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpeedometerNeedleUI : MonoBehaviour
{
    [Header("References")]
    public Rigidbody targetRigidbody;
    public RectTransform needleTransform;
    public TMP_Text speedText;

    [Header("Speed Settings")]
    public float maxSpeedKmh = 180f;

    [Tooltip("Angle at 0 km/h")]
    public float minNeedleAngle = 140f;

    [Tooltip("Angle at max speed")]
    public float maxNeedleAngle = -40f;

    [Header("Smoothing")]
    public float needleSmoothSpeed = 8f;
    public float speedSmoothSpeed = 8f;

    private float displayedSpeed;
    private float displayedAngle;

    void Start()
    {
        if (targetRigidbody == null)
        {
            Debug.LogWarning("SpeedometerNeedleUI: targetRigidbody is not assigned.");
        }

        displayedAngle = minNeedleAngle;
        ApplyNeedleRotation(displayedAngle);
    }

    void Update()
    {
        if (targetRigidbody == null || needleTransform == null)
            return;

        Vector3 velocity = targetRigidbody.linearVelocity;
        velocity.y = 0f;

        float currentSpeedKmh = velocity.magnitude * 3.6f;
        currentSpeedKmh = Mathf.Clamp(currentSpeedKmh, 0f, maxSpeedKmh);

        displayedSpeed = Mathf.Lerp(displayedSpeed, currentSpeedKmh, Time.deltaTime * speedSmoothSpeed);

        float normalized = displayedSpeed / maxSpeedKmh;
        float targetAngle = Mathf.Lerp(minNeedleAngle, maxNeedleAngle, normalized);

        displayedAngle = Mathf.LerpAngle(displayedAngle, targetAngle, Time.deltaTime * needleSmoothSpeed);
        ApplyNeedleRotation(displayedAngle);

        if (speedText != null)
        {
            speedText.text = $"{Mathf.RoundToInt(displayedSpeed):000} km/h";
        }
    }

    private void ApplyNeedleRotation(float angle)
    {
        needleTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}