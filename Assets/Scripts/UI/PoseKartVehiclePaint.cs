using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

namespace PoseKart.UI
{
    public sealed class PoseKartVehiclePaint : MonoBehaviour
    {
        [System.Serializable]
        public class VehicleColorOption
        {
            public string displayName;
            public Color color = Color.white;
            public VehicleModelMaterialOption[] modelMaterials;
            // Compatibility fallback for an older palette that had one
            // material per colour instead of one set per vehicle.
            public Material material;
        }

        [System.Serializable]
        public class VehicleModelMaterialOption
        {
            public string modelName;
            public Material bodyMaterial;
            public Material wheelMaterial;
        }
        public const string VehiclePreferenceKey = "PoseKart.SelectedVehicle";
        public const string ColorPreferenceKey = "PoseKart.SelectedColor";
        public static readonly string[] VehicleNames = { "Rally", "Sport", "Buggy" };
        public static readonly string[] ColorNames =
        {
            "Racing Red",
            "Jet Black",
            "Pearl White",
            "Electric Blue",
            "Neon Green",
            "Solar Yellow",
            "Royal Purple",
            "Sunset Orange"
        };
        private static readonly Color[] DefaultColors =
        {
            new Color(.90f,.03f,.02f),
            new Color(.01f,.01f,.015f),
            new Color(.95f,.95f,1f),
            new Color(.02f,.35f,1f),
            new Color(.10f,1f,.15f),
            new Color(1f,.75f,.02f),
            new Color(.35f,.05f,.65f),
            new Color(1f,.28f,.03f)
        };
        [SerializeField] private VehicleColorOption[] colors;
        [SerializeField] private GameObject[] vehicleModels;
        [SerializeField] private bool useSavedSelection = true;
        private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        private int vehicleIndex, colorIndex;
        public int VehicleCount => vehicleModels == null ? 0 : vehicleModels.Length;
        public int ColorCount
        {
            get
            {
                EnsurePalette();
                return colors.Length;
            }
        }
        public void Configure(GameObject[] models, bool saved) { vehicleModels = models; useSavedSelection = saved; }

        public string GetColorName(int index)
        {
            EnsurePalette();
            if (colors != null && index >= 0 && index < colors.Length &&
                !string.IsNullOrWhiteSpace(colors[index].displayName))
                return colors[index].displayName;

            return ColorNames[Mathf.Clamp(index, 0, ColorNames.Length - 1)];
        }

        private Color GetColor(int index)
        {
            EnsurePalette();
            if (colors != null && index >= 0 && index < colors.Length)
                return colors[index].color;

            return DefaultColors[Mathf.Clamp(index, 0, DefaultColors.Length - 1)];
        }

        private Material GetMaterial(int index)
        {
            EnsurePalette();
            if (colors != null && index >= 0 && index < colors.Length)
                return colors[index].material;

            return null;
        }

        private VehicleModelMaterialOption GetModelMaterials(int color, int model)
        {
            EnsurePalette();
            if (color < 0 || color >= colors.Length)
                return null;

            VehicleModelMaterialOption[] modelMaterials = colors[color].modelMaterials;
            if (modelMaterials == null || model < 0 || model >= modelMaterials.Length)
                return null;

            return modelMaterials[model];
        }

        private void OnValidate()
        {
            EnsurePalette();
            for (int i = 0; i < colors.Length; i++)
                colors[i].displayName = ColorNames[i];
        }

        private void Awake()
        {
            EnsurePalette();
        }

        private void EnsurePalette()
        {
            if (colors != null && colors.Length == DefaultColors.Length)
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    if (colors[i] == null)
                    {
                        colors[i] = new VehicleColorOption
                        {
                            displayName = ColorNames[i],
                            color = DefaultColors[i]
                        };
                    }
                    colors[i].displayName = ColorNames[i];
                    EnsureModelMaterials(colors[i]);
                }
                return;
            }

            VehicleColorOption[] previous = colors;
            colors = new VehicleColorOption[DefaultColors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                if (previous != null && i < previous.Length && previous[i] != null)
                {
                    colors[i] = previous[i];
                    colors[i].displayName = ColorNames[i];
                    EnsureModelMaterials(colors[i]);
                    continue;
                }

                colors[i] = new VehicleColorOption
                {
                    displayName = ColorNames[i],
                    color = DefaultColors[i],
                    modelMaterials = CreateModelMaterials(),
                    material = null
                };
            }
        }

        private static VehicleModelMaterialOption[] CreateModelMaterials()
        {
            VehicleModelMaterialOption[] result = new VehicleModelMaterialOption[VehicleNames.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = new VehicleModelMaterialOption { modelName = VehicleNames[i] };
            return result;
        }

        private static void EnsureModelMaterials(VehicleColorOption option)
        {
            if (option == null)
                return;

            VehicleModelMaterialOption[] previous = option.modelMaterials;
            if (previous != null && previous.Length == VehicleNames.Length)
            {
                for (int i = 0; i < previous.Length; i++)
                {
                    if (previous[i] == null)
                        previous[i] = new VehicleModelMaterialOption();
                    previous[i].modelName = VehicleNames[i];
                }
                return;
            }

            option.modelMaterials = new VehicleModelMaterialOption[VehicleNames.Length];
            for (int i = 0; i < option.modelMaterials.Length; i++)
            {
                option.modelMaterials[i] = new VehicleModelMaterialOption
                {
                    modelName = VehicleNames[i]
                };
            }

            // Preserve a material assigned through the old single-material
            // palette as the body material of every model.
            if (option.material != null)
            {
                for (int i = 0; i < option.modelMaterials.Length; i++)
                    option.modelMaterials[i].bodyMaterial = option.material;
            }
        }
        private void Start()
        {
            if (!useSavedSelection)
                return;

            // Khi chơi Online, mỗi NetworkPlayer (kể cả remote clone) phải
            // nhận model/màu qua NetworkVariable của owner. Không lấy
            // PlayerPrefs của máy đang render để vẽ TẤT CẢ xe.
            NetworkObject networkObject = GetComponentInParent<NetworkObject>();
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkObject != null &&
                networkManager != null &&
                networkManager.IsListening)
                return;

            // Vehicle Select / Offline giữ nguyên hành vi cũ.
            Apply(
                PlayerPrefs.GetInt(VehiclePreferenceKey, 0),
                PlayerPrefs.GetInt(ColorPreferenceKey, 0)
            );
        }
        public void Apply(int selectedVehicle, int selectedColor)
        {
            if (vehicleModels == null || vehicleModels.Length == 0) return;
            vehicleIndex = Mathf.Clamp(selectedVehicle, 0, vehicleModels.Length - 1); colorIndex = Mathf.Clamp(selectedColor, 0, ColorCount - 1);
            for (int i = 0; i < vehicleModels.Length; i++) if (vehicleModels[i] != null) vehicleModels[i].SetActive(i == vehicleIndex);
            GameObject active = vehicleModels[vehicleIndex]; if (active == null) return;

            VehicleModelMaterialOption selectedModelMaterials = GetModelMaterials(colorIndex, vehicleIndex);
            if (selectedModelMaterials != null &&
                (selectedModelMaterials.bodyMaterial != null || selectedModelMaterials.wheelMaterial != null))
            {
                ApplyModelMaterialVariant(active, selectedModelMaterials);
                return;
            }

            Material selectedMaterial = GetMaterial(colorIndex);
            if (selectedMaterial != null)
            {
                ApplyMaterialVariant(active, selectedMaterial);
                return;
            }

            RestoreOriginalMaterials(active);
            var properties = new MaterialPropertyBlock();
            Color selectedColorValue = GetColor(colorIndex);
            foreach (Renderer renderer in active.GetComponentsInChildren<Renderer>(true).Where(IsPaintableBody))
            { renderer.GetPropertyBlock(properties); properties.SetColor("_BaseColor", selectedColorValue); properties.SetColor("_Color", selectedColorValue); renderer.SetPropertyBlock(properties); }
        }

        private void ApplyMaterialVariant(GameObject active, Material selectedMaterial)
        {
            RestoreOriginalMaterials(active);
            foreach (Renderer renderer in active.GetComponentsInChildren<Renderer>(true).Where(IsPaintableBody))
            {
                if (!originalMaterials.TryGetValue(renderer, out Material[] sourceMaterials))
                {
                    sourceMaterials = renderer.sharedMaterials;
                    originalMaterials[renderer] = sourceMaterials;
                }

                Material[] materials = (Material[])sourceMaterials.Clone();
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = selectedMaterial;

                renderer.sharedMaterials = materials;
                renderer.SetPropertyBlock(null);
            }
        }

        private void ApplyModelMaterialVariant(GameObject active, VehicleModelMaterialOption selectedMaterials)
        {
            RestoreOriginalMaterials(active);
            foreach (Renderer renderer in active.GetComponentsInChildren<Renderer>(true).Where(IsPaintableRenderer))
            {
                if (!originalMaterials.TryGetValue(renderer, out Material[] sourceMaterials))
                {
                    sourceMaterials = renderer.sharedMaterials;
                    originalMaterials[renderer] = sourceMaterials;
                }

                Material selectedMaterial = IsWheelRenderer(renderer)
                    ? selectedMaterials.wheelMaterial
                    : selectedMaterials.bodyMaterial;

                if (selectedMaterial == null)
                    continue;

                Material[] materials = (Material[])sourceMaterials.Clone();
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = selectedMaterial;

                renderer.sharedMaterials = materials;
                renderer.SetPropertyBlock(null);
            }
        }

        private void RestoreOriginalMaterials(GameObject active)
        {
            foreach (Renderer renderer in active.GetComponentsInChildren<Renderer>(true).Where(IsPaintableRenderer))
            {
                if (originalMaterials.TryGetValue(renderer, out Material[] sourceMaterials))
                    renderer.sharedMaterials = (Material[])sourceMaterials.Clone();
            }
        }

        private static bool IsPaintableRenderer(Renderer renderer)
        { return IsPaintableBody(renderer) || IsWheelRenderer(renderer); }

        private static bool IsPaintableBody(Renderer renderer)
        { string value = renderer.name.ToLowerInvariant(); return value == "body" || value.Contains("body") || value.Contains("roadster"); }

        private static bool IsWheelRenderer(Renderer renderer)
        {
            string value = renderer.name.ToLowerInvariant();
            return value.Contains("wheel") || value.Contains("tire") || value.Contains("tyre");
        }
    }
}
