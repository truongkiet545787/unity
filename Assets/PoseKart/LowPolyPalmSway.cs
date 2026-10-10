using UnityEngine;

public class LowPolyPalmSway : MonoBehaviour
{
    [Header("Sway Settings")]
    [SerializeField] private float swayAngle = 3f;
    [SerializeField] private float swaySpeed = 1.2f;
    [SerializeField] private float secondaryAngle = 1.2f;

    [Header("Randomization")]
    [SerializeField] private bool randomizePhase = true;
    [SerializeField] private bool randomizeSpeed = true;
    [SerializeField] private Vector2 speedRange = new Vector2(0.9f, 1.4f);

    private Quaternion startRotation;
    private float phase;
    private float runtimeSpeed;

    private void Awake()
    {
        startRotation = transform.localRotation;
        phase = randomizePhase ? Random.Range(0f, 100f) : 0f;
        runtimeSpeed = randomizeSpeed ? Random.Range(speedRange.x, speedRange.y) : swaySpeed;
    }

    private void Update()
    {
        float t = Time.time * runtimeSpeed + phase;

        float swayZ = Mathf.Sin(t) * swayAngle;
        float swayX = Mathf.Sin(t * 0.73f) * secondaryAngle;

        transform.localRotation =
            startRotation *
            Quaternion.Euler(swayX, 0f, swayZ);
    }
}