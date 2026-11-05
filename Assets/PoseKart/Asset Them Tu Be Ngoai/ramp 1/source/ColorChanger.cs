using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Continuous rainbow colour animation for boost arrows and other visual markers.
/// Supports 3D renderers, SpriteRenderers, UI Images and legacy UI Text.
/// </summary>
public sealed class ColorChanger : MonoBehaviour
{
    [Header("Cài đặt tốc độ")]
    [Tooltip("Tốc độ chạy qua dải màu cầu vồng.")]
    [Min(0f)] public float speed = 1.0f;

    [SerializeField, Range(0f, 1f)] private float saturation = 1f;
    [SerializeField, Range(0f, 1f)] private float brightness = 1f;
    [SerializeField] private bool useUnscaledTime;

    private Renderer[] meshRenderers;
    private SpriteRenderer[] spriteRenderers;
    private Image uiImage;
    private Text uiText;
    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        // Get children too, so the script works when placed on a BoostPad parent.
        meshRenderers = GetComponentsInChildren<Renderer>(true);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        uiImage = GetComponentInChildren<Image>(true);
        uiText = GetComponentInChildren<Text>(true);
        propertyBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        float currentTime = useUnscaledTime ? Time.unscaledTime : Time.time;
        float hue = Mathf.Repeat(currentTime * speed, 1f);
        Color nextColor = Color.HSVToRGB(hue, saturation, brightness);

        ApplyToMeshes(nextColor);

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    spriteRenderers[i].color = nextColor;
            }
        }

        if (uiImage != null)
            uiImage.color = nextColor;

        if (uiText != null)
            uiText.color = nextColor;
    }

    private void ApplyToMeshes(Color color)
    {
        if (meshRenderers == null)
            return;

        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);

        for (int i = 0; i < meshRenderers.Length; i++)
        {
            if (meshRenderers[i] != null)
                meshRenderers[i].SetPropertyBlock(propertyBlock);
        }
    }
}
