
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

public class PoseKartHorn : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CarController car;

    [Header("Horn Settings")]
    [SerializeField] private bool isPlayer = true;
    [SerializeField, Range(0f, 1f)]
    private float hornVolume = 0.7f;
    [SerializeField] private float hornCooldown = 0.6f;

    [Header("AI Horn When Overtaking")]
    [SerializeField] private Transform playerTarget;
    [SerializeField, Range(0f, 1f)]
    private float aiHornChance = 0.35f;
    [SerializeField] private float aiDetectionDistance = 8f;
    [SerializeField] private float aiLateralDistance = 4f;
    [SerializeField] private float aiMinimumSpeedKmh = 20f;
    [SerializeField] private float aiCooldownMin = 15f;
    [SerializeField] private float aiCooldownMax = 20f;

    private float nextHornTime;
    private float nextAiHornTime;
    private bool aiWasBehindPlayer;

    private void Awake()
    {
        if (car == null)
            car = GetComponent<CarController>();
    }

    private void Update()
    {
        if (car == null || !car.ControlEnabled)
            return;

        if (isPlayer)
        {
            if (Keyboard.current != null &&
                Keyboard.current.hKey.wasPressedThisFrame)
            {
                Honk();
            }

            return;
        }

        CheckAiOvertake();
    }

    public void Honk()
    {
        if (car == null || !car.ControlEnabled)
            return;

        if (Time.time < nextHornTime)
            return;

        PoseKart.Audio.AudioManager.PlayHorn(transform.position, hornVolume);
        nextHornTime = Time.time + hornCooldown;
    }

    private void CheckAiOvertake()
    {
        if (playerTarget == null ||
            playerTarget == transform ||
            car.TargetBody == null)
            return;

        float speedKmh =
            car.TargetBody.linearVelocity.magnitude * 3.6f;

        if (speedKmh < aiMinimumSpeedKmh ||
            !car.IsGrounded)
        {
            aiWasBehindPlayer = false;
            return;
        }

        Vector3 delta =
            transform.position - playerTarget.position;

        delta.y = 0f;

        if (delta.magnitude > aiDetectionDistance)
        {
            aiWasBehindPlayer = false;
            return;
        }

        Vector3 playerForward =
            Vector3.ProjectOnPlane(
                playerTarget.forward, Vector3.up);

        Vector3 aiForward =
            Vector3.ProjectOnPlane(
                transform.forward, Vector3.up);

        if (playerForward.sqrMagnitude < 0.01f ||
            aiForward.sqrMagnitude < 0.01f)
            return;

        playerForward.Normalize();
        aiForward.Normalize();

        // Only detect vehicles travelling in
        // approximately the same direction.
        if (Vector3.Dot(playerForward, aiForward) < 0.6f)
        {
            aiWasBehindPlayer = false;
            return;
        }

        float forwardDistance =
            Vector3.Dot(delta, playerForward);

        float lateralDistance =
            Vector3.ProjectOnPlane(
                delta, playerForward).magnitude;

        if (lateralDistance > aiLateralDistance)
        {
            aiWasBehindPlayer = false;
            return;
        }

        // AI approaches from behind.
        if (forwardDistance < -0.5f)
        {
            aiWasBehindPlayer = true;
            return;
        }

        // AI has crossed in front of Player.
        if (aiWasBehindPlayer &&
            forwardDistance > 0.5f)
        {
            aiWasBehindPlayer = false;

            if (Time.time < nextAiHornTime)
                return;

            float minCooldown =
                Mathf.Min(aiCooldownMin, aiCooldownMax);

            float maxCooldown =
                Mathf.Max(aiCooldownMin, aiCooldownMax);

            nextAiHornTime = Time.time +
                Random.Range(minCooldown, maxCooldown);

            if (Random.value < aiHornChance)
            {
                Honk();
            }
        }
    }
}
