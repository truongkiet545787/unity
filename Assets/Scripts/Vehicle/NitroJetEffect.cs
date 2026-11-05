using UnityEngine;
using System.Collections.Generic;

namespace PoseKart.Vehicle
{
    public sealed class NitroJetEffect : MonoBehaviour
    {
        [Header("Custom Nitro VFX")]
        [Tooltip("Optional Nitro VFX prefab. If assigned, it replaces the procedural line-renderer flames.")]
        [SerializeField] private GameObject customNitroPrefab;
        [Tooltip("Optional exhaust anchors. Assign one anchor per prefab instance (usually left and right exhaust). If empty, one instance is placed under the vehicle visual pivot.")]
        [SerializeField] private Transform[] customNitroSpawnPoints;
        [Tooltip("Optional glow/light prefab spawned at the same exhaust anchors. Leave empty if the Nitro prefab already contains its own glow.")]
        [SerializeField] private GameObject customNitroGlowPrefab;
        [Tooltip("Optional glow anchors. If empty, Custom Nitro Spawn Points are reused.")]
        [SerializeField] private Transform[] customNitroGlowSpawnPoints;
        [Tooltip("Starts/stops ParticleSystems inside the custom prefab when Nitro changes state.")]
        [SerializeField] private bool controlCustomParticleSystems = true;
        [Tooltip("Changes the custom prefab's ParticleSystem/Renderer tint when Nitro mode changes.")]
        [SerializeField] private bool tintCustomNitroVfx = true;
        [SerializeField] private Color customSingleColor = new Color(1f, 0.78f, 0.1f, 1f);
        [SerializeField] private Color customDoubleColor = new Color(1f, 0.35f, 0.05f, 1f);
        [SerializeField] private Color customPerfectColor = new Color(0.1f, 0.88f, 1f, 1f);
        [SerializeField] private Color customShockwaveColor = new Color(0.85f, 0.15f, 1f, 1f);

        private CarController car;
        private LineRenderer[] jets;
        private Material material;
        private Material coreMaterial;
        private Light glow;
        private readonly List<GameObject> customNitroInstances = new List<GameObject>();
        private readonly List<GameObject> customNitroGlowInstances = new List<GameObject>();
        // MaterialPropertyBlock wraps a native Unity object and must be created
        // from a Unity lifecycle method, never from a MonoBehaviour field initializer.
        private MaterialPropertyBlock customColorBlock;
        private bool customNitroIsActive;
        private CarController.NitroMode lastMode = (CarController.NitroMode)(-1);
        private float currentLengthMultiplier = 1f;
        private float currentGlowIntensity = 4f;
        private Vector3 previousVehiclePosition;
        private bool hasPreviousVehiclePosition;
        private bool resetVfxSuppressed;
        private CarController.NitroMode resetSuppressionMode;

        public bool HasCustomNitroVfx => customNitroPrefab != null || customNitroGlowPrefab != null;

        private void Start()
        {
            customColorBlock = new MaterialPropertyBlock();
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

            if (HasCustomNitroVfx)
            {
                CreateCustomNitroInstances(parentPivot);
                ApplyCustomModeVisuals(CarController.NitroMode.Single);
                SetCustomNitroActive(false);
                return;
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
            if (car == null)
                car = GetComponent<CarController>();

            if (car != null)
            {
                if (hasPreviousVehiclePosition &&
                    (transform.position - previousVehiclePosition).sqrMagnitude > 16f)
                {
                    resetVfxSuppressed = true;
                    resetSuppressionMode = car.VisualNitroMode;
                }
                previousVehiclePosition = transform.position;
                hasPreviousVehiclePosition = true;

                if (resetVfxSuppressed &&
                    (!car.IsVisualNitroBoosting ||
                     car.VisualNitroMode != resetSuppressionMode))
                {
                    resetVfxSuppressed = false;
                }
            }

            // Boost pad chỉ tăng tốc chứ không kích hoạt nitrojet
            bool active = car != null &&
                car.IsVisualNitroBoosting &&
                !resetVfxSuppressed;

            if (HasCustomNitroVfx)
            {
                if (active && car != null && car.VisualNitroMode != lastMode)
                    ApplyCustomModeVisuals(car.VisualNitroMode);
                if (active != customNitroIsActive)
                    SetCustomNitroActive(active);
                return;
            }

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

        private void CreateCustomNitroInstances(Transform parentPivot)
        {
            CreateCustomVfxInstances(
                customNitroPrefab,
                customNitroSpawnPoints,
                parentPivot,
                "Nitro VFX",
                customNitroInstances);

            Transform[] glowPoints = customNitroGlowSpawnPoints != null &&
                customNitroGlowSpawnPoints.Length > 0
                ? customNitroGlowSpawnPoints
                : customNitroSpawnPoints;
            CreateCustomVfxInstances(
                customNitroGlowPrefab,
                glowPoints,
                parentPivot,
                "Nitro Glow",
                customNitroGlowInstances);
        }

        private void CreateCustomVfxInstances(
            GameObject prefab,
            Transform[] spawnPoints,
            Transform parentPivot,
            string suffix,
            List<GameObject> destination)
        {
            destination.Clear();
            if (prefab == null)
                return;

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    Transform spawnPoint = spawnPoints[i];
                    if (spawnPoint == null)
                        continue;

                    GameObject instance = Instantiate(prefab, spawnPoint, false);
                    instance.name = prefab.name + " (" + suffix + ")";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one;
                    instance.SetActive(false);
                    destination.Add(instance);
                }
            }
            else if (parentPivot != null)
            {
                GameObject instance = Instantiate(prefab, parentPivot, false);
                instance.name = prefab.name + " (" + suffix + ")";
                instance.SetActive(false);
                destination.Add(instance);
            }
        }

        private void SetCustomNitroActive(bool active)
        {
            customNitroIsActive = active;

            SetInstancesActive(customNitroInstances, active);
            SetInstancesActive(customNitroGlowInstances, active);
        }

        private void SetInstancesActive(List<GameObject> instances, bool active)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                GameObject instance = instances[i];
                if (instance == null)
                    continue;

                if (!active)
                {
                    if (controlCustomParticleSystems)
                    {
                        ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                        for (int j = 0; j < systems.Length; j++)
                            systems[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }

                    instance.SetActive(false);
                    continue;
                }

                instance.SetActive(true);
                if (controlCustomParticleSystems)
                {
                    ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                    for (int j = 0; j < systems.Length; j++)
                        systems[j].Play(true);
                }
            }
        }

        private void ApplyCustomModeVisuals(CarController.NitroMode mode)
        {
            lastMode = mode;
            if (!tintCustomNitroVfx)
                return;

            Color tint = GetCustomModeColor(mode);
            ApplyCustomModeVisualsToInstances(customNitroInstances, tint);
            ApplyCustomModeVisualsToInstances(customNitroGlowInstances, tint);
        }

        private void ApplyCustomModeVisualsToInstances(List<GameObject> instances, Color tint)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                GameObject instance = instances[i];
                if (instance == null)
                    continue;

                ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < systems.Length; j++)
                {
                    ParticleSystem.MainModule main = systems[j].main;
                    main.startColor = tint;
                }

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer renderer = renderers[j];
                    if (renderer == null)
                        continue;

                    renderer.GetPropertyBlock(customColorBlock);
                    Material sharedMaterial = renderer.sharedMaterial;
                    if (sharedMaterial != null)
                    {
                        if (sharedMaterial.HasProperty("_BaseColor"))
                            customColorBlock.SetColor("_BaseColor", tint);
                        if (sharedMaterial.HasProperty("_Color"))
                            customColorBlock.SetColor("_Color", tint);
                        if (sharedMaterial.HasProperty("_TintColor"))
                            customColorBlock.SetColor("_TintColor", tint);
                    }
                    renderer.SetPropertyBlock(customColorBlock);
                }

                Light[] lights = instance.GetComponentsInChildren<Light>(true);
                for (int j = 0; j < lights.Length; j++)
                {
                    if (lights[j] != null)
                        lights[j].color = tint;
                }
            }
        }

        private Color GetCustomModeColor(CarController.NitroMode mode)
        {
            switch (mode)
            {
                case CarController.NitroMode.Double:
                    return customDoubleColor;
                case CarController.NitroMode.Perfect:
                    return customPerfectColor;
                case CarController.NitroMode.Shockwave:
                    return customShockwaveColor;
                case CarController.NitroMode.Single:
                default:
                    return customSingleColor;
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
            DestroyInstances(customNitroInstances);
            DestroyInstances(customNitroGlowInstances);

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

        private void DestroyInstances(List<GameObject> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                    Destroy(instances[i]);
            }
        }
    }
}
