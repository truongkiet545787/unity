
using UnityEngine;
using TMPro;
using PoseKart.UI;
using PoseKart.Vehicle;

public class PoseKartSpeedHUD : MonoBehaviour
{
    public Rigidbody playerRigidbody;
    public TMP_Text speedText;

    private float displayedSpeed;
    private CarController playerCar;
    private float nextResolveTime;

    private void OnEnable()
    {
        playerCar = null;
        displayedSpeed = 0f;
    }

    void LateUpdate()
    {
        if (speedText == null)
            return;

        if (playerCar == null || !PoseKartLocalPlayerResolver.IsLocalHuman(playerCar) ||
            Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.25f;
            playerCar = PoseKartLocalPlayerResolver.FindLocalCar();
            playerRigidbody = playerCar != null ? playerCar.TargetBody : null;
        }

        if (playerRigidbody == null)
            return;

        Vector3 velocity = playerRigidbody.linearVelocity;

        // Chỉ tính tốc độ di chuyển trên mặt phẳng
        float speedKmh = new Vector2(
            velocity.x,
            velocity.z
        ).magnitude * 3.6f;

        // Làm mượt số hiển thị
        displayedSpeed = Mathf.Lerp(
            displayedSpeed,
            speedKmh,
            1f - Mathf.Exp(-10f * Time.deltaTime)
        );

        int speed = Mathf.RoundToInt(displayedSpeed);

        speedText.text =
            $"{speed:000} <size=45%>km/h</size>";
    }
}
