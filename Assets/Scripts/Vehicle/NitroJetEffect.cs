using UnityEngine;

namespace PoseKart.Vehicle
{
    public sealed class NitroJetEffect : MonoBehaviour
    {
        private CarController car;
        private LineRenderer[] jets;
        private Material material;
        private Material coreMaterial;
        private Light glow;
        private CarController.NitroMode lastMode = (CarController.NitroMode)(-1);
        private float currentLengthMultiplier = 1f;
        private float currentGlowIntensity = 4f;

        private void Start()
        {
            car = GetComponent<CarController>();

            Transform parentPivot = transform.Find("StuntPivot");
            if (parentPivot == null)
            {
                var stunt = GetComponent<PoseKart.Stunts.PoseKartStuntController>();
                if (stunt != null && stunt.VisualPivot != null)
                {
                    parentPivot = stunt.VisualPivot;
                }
                else
                {
                    parentPivot = transform.Find("Visual") ?? transform;
                }
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            material = new Material(shader);
            coreMaterial = new Material(material);

            jets = new LineRenderer[4];
            for (int i = 0; i < jets.Length; i++)
            {
                GameObject go = new GameObject(i % 2 == 0 ? "Nitro plume" : "Nitro core");
                go.transform.SetParent(parentPivot, false);
                var line = go.AddComponent<LineRenderer>();
                jets[i] = line;
                line.sharedMaterial = i % 2 == 0 ? material : coreMaterial;
                line.useWorldSpace = false;
                line.positionCount = 4;
                line.numCapVertices = 4;
                line.widthCurve = new AnimationCurve(
                    new Keyframe(0f, 0.45f),
                    new Keyframe(0.25f, 0.65f),
                    new Keyframe(1f, 0f)
                );
                line.widthMultiplier = i % 2 == 0 ? 0.65f : 0.24f;
                line.enabled = false;
            }

            GameObject lamp = new GameObject("Nitro glow");
            lamp.transform.SetParent(parentPivot, false);
            lamp.transform.localPosition = new Vector3(0f, 0.5f, -1.5f);
            glow = lamp.AddComponent<Light>();
            glow.range = 5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;

            ApplyModeVisuals(CarController.NitroMode.Single, true);
        }

        private void LateUpdate()
        {
            // Boost pad chỉ tăng tốc chứ không kích hoạt nitrojet
            bool active = car != null && car.IsVisualNitroBoosting;

            if (car != null && active)
            {
                CarController.NitroMode mode = car.VisualNitroMode;
                if (mode != lastMode)
                {
                    ApplyModeVisuals(mode, false);
                }
            }

            if (glow != null)
            {
                glow.intensity = active ? currentGlowIntensity : 0f;
            }

            for (int i = 0; i < jets.Length; i++)
            {
                LineRenderer line = jets[i];
                if (line == null) continue;

                line.enabled = active;
                if (!active) continue;

                float baseLength = (i % 2 == 0 ? 3.8f : 2.2f) * currentLengthMultiplier;
                float length = baseLength * (1f + 0.12f * Mathf.Sin(Time.time * 43f + i));
                float x = i < 2 ? -0.42f : 0.42f;

                line.SetPosition(0, new Vector3(x, 0.45f, -1.1f));
                line.SetPosition(1, new Vector3(x, 0.45f, -1.5f));
                line.SetPosition(2, new Vector3(x, 0.47f, -1.1f - length * 0.6f));
                line.SetPosition(3, new Vector3(x, 0.45f, -1.1f - length));
            }
        }

        private void ApplyModeVisuals(CarController.NitroMode mode, bool force)
        {
            if (mode == lastMode && !force)
                return;

            lastMode = mode;

            Color plumeColor;
            Color coreColor;
            Color tailColor;
            Color glowColor;

            switch (mode)
            {
                case CarController.NitroMode.Single:
                    // Warm Golden Yellow
                    plumeColor = new Color(1f, 0.78f, 0.1f, 1f);
                    coreColor = new Color(1f, 0.96f, 0.65f, 1f);
                    tailColor = new Color(0.95f, 0.4f, 0.05f, 1f);
                    glowColor = new Color(1f, 0.8f, 0.15f, 1f);
                    currentLengthMultiplier = 1f;
                    currentGlowIntensity = 4f;
                    break;

                case CarController.NitroMode.Double:
                    // Fiery Red-Orange
                    plumeColor = new Color(1f, 0.35f, 0.05f, 1f);
                    coreColor = new Color(1f, 0.75f, 0.35f, 1f);
                    tailColor = new Color(0.9f, 0.1f, 0.02f, 1f);
                    glowColor = new Color(1f, 0.42f, 0.08f, 1f);
                    currentLengthMultiplier = 1.25f;
                    currentGlowIntensity = 5f;
                    break;

                case CarController.NitroMode.Perfect:
                    // Electric Cyan / Blue (Màu xanh Asphalt)
                    plumeColor = new Color(0.1f, 0.88f, 1f, 1f);
                    coreColor = new Color(0.85f, 0.98f, 1f, 1f);
                    tailColor = new Color(0.05f, 0.3f, 0.95f, 1f);
                    glowColor = new Color(0.12f, 0.9f, 1f, 1f);
                    currentLengthMultiplier = 1.35f;
                    currentGlowIntensity = 5.5f;
                    break;

                case CarController.NitroMode.Shockwave:
                    // Neon Shockwave Purple / Magenta (Màu tím Asphalt)
                    plumeColor = new Color(0.85f, 0.15f, 1f, 1f);
                    coreColor = new Color(0.95f, 0.7f, 1f, 1f);
                    tailColor = new Color(0.45f, 0.02f, 0.85f, 1f);
                    glowColor = new Color(0.88f, 0.2f, 1f, 1f);
                    currentLengthMultiplier = 1.65f;
                    currentGlowIntensity = 7f;
                    break;

                default:
                    // Item or Pad boost fallback (Cyan/Yellow)
                    plumeColor = new Color(0.15f, 0.9f, 1f, 1f);
                    coreColor = new Color(0.9f, 0.98f, 1f, 1f);
                    tailColor = new Color(0.08f, 0.2f, 1f, 1f);
                    glowColor = Color.cyan;
                    currentLengthMultiplier = 1.1f;
                    currentGlowIntensity = 4f;
                    break;
            }

            if (material != null)
            {
                material.SetColor("_BaseColor", plumeColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", plumeColor);
            }

            if (coreMaterial != null)
            {
                coreMaterial.SetColor("_BaseColor", coreColor);
                if (coreMaterial.HasProperty("_Color")) coreMaterial.SetColor("_Color", coreColor);
            }

            if (glow != null)
            {
                glow.color = glowColor;
            }

            if (jets != null)
            {
                for (int i = 0; i < jets.Length; i++)
                {
                    if (jets[i] != null)
                    {
                        jets[i].startColor = i % 2 == 0 ? plumeColor : coreColor;
                        jets[i].endColor = tailColor;
                        jets[i].widthMultiplier = (i % 2 == 0 ? 0.65f : 0.24f) *
                            (mode == CarController.NitroMode.Shockwave ? 1.3f : 1f);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (jets != null)
            {
                for (int i = 0; i < jets.Length; i++)
                {
                    if (jets[i] != null) Destroy(jets[i].gameObject);
                }
            }
            if (glow != null) Destroy(glow.gameObject);
            if (material != null) Destroy(material);
            if (coreMaterial != null) Destroy(coreMaterial);
        }
    }
}
