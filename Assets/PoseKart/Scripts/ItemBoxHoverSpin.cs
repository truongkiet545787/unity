using UnityEngine;

public class ItemBoxHoverSpin : MonoBehaviour
{
    [Header("Hover")]
    [SerializeField] private bool enableHover = true;
    [SerializeField] private float hoverHeight = 0.25f;
    [SerializeField] private float hoverSpeed = 2f;

    [Header("Spin")]
    [SerializeField] private bool enableSpin = true;
    [SerializeField] private float spinSpeed = 90f;

    [Header("Random Offset")]
    [SerializeField] private bool randomizePhase = true;
    [SerializeField] private bool randomizeStartRotation = true;

    private Vector3 startLocalPosition;
    private float phaseOffset;

    private void Awake()
    {
        startLocalPosition = transform.localPosition;

        if (randomizePhase)
        {
            phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        }

        if (randomizeStartRotation)
        {
            Vector3 euler = transform.localEulerAngles;
            euler.y = Random.Range(0f, 360f);
            transform.localEulerAngles = euler;
        }
    }

    private void OnEnable()
    {
        // Lưu lại vị trí gốc mỗi lần object được bật lại
        startLocalPosition = transform.localPosition;
    }

    private void Update()
    {
        if (enableHover)
        {
            float yOffset = Mathf.Sin(Time.time * hoverSpeed + phaseOffset) * hoverHeight;
            Vector3 pos = startLocalPosition;
            pos.y += yOffset;
            transform.localPosition = pos;
        }

        if (enableSpin)
        {
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }
}