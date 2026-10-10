using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class WaterSurfaceMotion : MonoBehaviour
{
    [Header("Texture Scroll")]
    [SerializeField] private Vector2 baseMapScroll = new Vector2(0.015f, 0.008f);
    [SerializeField] private Vector2 normalMapScroll = new Vector2(-0.02f, 0.01f);

    [Header("Optional Color Pulse")]
    [SerializeField] private bool pulseColor = true;
    [SerializeField] private Color baseColorA = new Color(0.16f, 0.72f, 0.78f, 1f);
    [SerializeField] private Color baseColorB = new Color(0.12f, 0.64f, 0.72f, 1f);
    [SerializeField] private float colorPulseSpeed = 0.35f;

    private Renderer rend;
    private Material runtimeMat;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        runtimeMat = rend.material; // tạo instance riêng
    }

    private void Update()
    {
        float t = Time.time;

        if (runtimeMat.HasProperty(BaseMapId))
        {
            runtimeMat.SetTextureOffset("_BaseMap", baseMapScroll * t);
        }

        if (runtimeMat.HasProperty(BumpMapId))
        {
            runtimeMat.SetTextureOffset("_BumpMap", normalMapScroll * t);
        }

        if (pulseColor && runtimeMat.HasProperty(BaseColorId))
        {
            float lerp = 0.5f + 0.5f * Mathf.Sin(t * colorPulseSpeed);
            runtimeMat.SetColor("_BaseColor", Color.Lerp(baseColorA, baseColorB, lerp));
        }
    }
}