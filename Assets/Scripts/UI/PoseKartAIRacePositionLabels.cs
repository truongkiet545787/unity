using PoseKart.Race;
using PoseKart.Multiplayer;
using Unity.Netcode;
using TMPro;
using UnityEngine;

namespace PoseKart.UI
{
    /// <summary>
    /// Creates lightweight world-space TextMeshPro nameplates for all racers.
    /// RacePosition is read from RaceParticipant; this component never ranks cars itself.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PoseKartAIRacePositionLabels : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private UnityEngine.Camera targetCamera;
        [SerializeField, Min(0f)] private float labelHeight = 2.4f;
        [SerializeField, Min(0.001f)] private float labelScale = 0.02f;
        [SerializeField, Range(0.2f, 1f)] private float labelScaleMultiplier = 0.55f;
        [SerializeField, Min(0.5f)] private float labelFontSize = 2.5f;
        [SerializeField, Min(1f)] private float visibilityDistance = 80f;
        [SerializeField, Min(0f)] private float fadeDistance = 55f;
        [SerializeField] private Color firstPlaceColor = new Color(1f, 0.78f, 0.12f, 1f);
        [SerializeField] private Color secondPlaceColor = Color.white;
        [SerializeField] private Color thirdPlaceColor = new Color(0.84f, 0.52f, 0.28f, 1f);
        [SerializeField] private Color fourthPlaceColor = Color.white;

        private LabelBinding[] bindings;

        private sealed class LabelBinding
        {
            public RaceParticipant racer;
            public TextMeshPro text;
            public int displayedPosition;
            public string displayedName;
        }

        private void Awake()
        {
            if (raceManager == null)
            {
                raceManager = FindFirstObjectByType<RaceManager>();
            }

            BuildBindings();
        }

        private void LateUpdate()
        {
            if (bindings == null || HasRosterChanged())
            {
                BuildBindings();
            }

            UnityEngine.Camera cameraToFace = ResolveCamera();

            if (cameraToFace == null || bindings == null)
            {
                return;
            }

            float maxDistance = Mathf.Max(1f, visibilityDistance);
            float fadeStart = Mathf.Clamp(fadeDistance, 0f, maxDistance);
            float maxDistanceSqr = maxDistance * maxDistance;

            for (int index = 0; index < bindings.Length; index++)
            {
                LabelBinding binding = bindings[index];

                if (binding == null || binding.racer == null || binding.text == null)
                {
                    continue;
                }

                // In multiplayer, do not draw the local human's own nameplate.
                // Remote human players and AI remain visible to this camera.
                if (!ShouldDisplayLabel(binding.racer))
                {
                    if (binding.text.gameObject.activeSelf)
                    {
                        binding.text.gameObject.SetActive(false);
                    }

                    continue;
                }

                Transform labelTransform = binding.text.transform;

                Vector3 toCamera =
                    cameraToFace.transform.position - labelTransform.position;

                float distanceSqr = toCamera.sqrMagnitude;

                // Quá xa thì ẩn
                if (distanceSqr >= maxDistanceSqr)
                {
                    if (binding.text.gameObject.activeSelf)
                    {
                        binding.text.gameObject.SetActive(false);
                    }

                    continue;
                }

                // Trong phạm vi thì hiện
                if (!binding.text.gameObject.activeSelf)
                {
                    binding.text.gameObject.SetActive(true);
                }

                // Cập nhật thứ hạng
                int position = Mathf.Clamp(binding.racer.RacePosition, 1, 8);

                string displayName = ResolveDisplayName(binding.racer);
                if (binding.displayedPosition != position || binding.displayedName != displayName)
                {
                    binding.displayedPosition = position;
                    binding.text.color = GetPlaceColor(position);
                    binding.displayedName = displayName;
                    binding.text.text = $"{displayName}  #{position}";
                }

                // Luôn cho mặt chữ cùng hướng với camera
                // => số 1,2,3,4 không bị ngược
                labelTransform.rotation = cameraToFace.transform.rotation;

                // Fade khi ở xa
                float distance = Mathf.Sqrt(distanceSqr);

                float visibility = fadeStart >= maxDistance
                    ? 1f
                    : Mathf.Clamp01(
                        Mathf.InverseLerp(maxDistance, fadeStart, distance)
                    );

                binding.text.alpha = visibility;

                float scaleFactor = Mathf.Lerp(0.68f, 1f, visibility);

                labelTransform.localScale =
                    Vector3.one * (GetEffectiveLabelScale() * scaleFactor);
            }
        }

        private void BuildBindings()
        {
            RaceParticipant[] racers = raceManager == null ? null : raceManager.Racers;
            if (racers == null || racers.Length == 0)
            {
                DestroyBindings();
                return;
            }

            DestroyBindings();
            LabelBinding[] newBindings = new LabelBinding[racers.Length];
            int bindingIndex = 0;
            for (int index = 0; index < racers.Length; index++)
            {
                RaceParticipant racer = racers[index];
                if (racer == null)
                {
                    continue;
                }

                TextMeshPro label = CreateLabel(racer);
                newBindings[bindingIndex++] = new LabelBinding
                {
                    racer = racer,
                    text = label,
                    displayedPosition = 0,
                    displayedName = string.Empty
                };
            }

            if (bindingIndex != newBindings.Length)
                System.Array.Resize(ref newBindings, bindingIndex);
            bindings = newBindings;
        }

        private bool HasRosterChanged()
        {
            RaceParticipant[] racers = raceManager == null ? null : raceManager.Racers;
            if (racers == null || bindings == null)
                return true;

            int validCount = 0;
            for (int i = 0; i < racers.Length; i++)
            {
                if (racers[i] != null)
                    validCount++;
            }
            if (validCount != bindings.Length)
                return true;

            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i] == null || bindings[i].racer == null)
                    return true;

                bool found = false;
                for (int racerIndex = 0; racerIndex < racers.Length; racerIndex++)
                {
                    if (racers[racerIndex] == bindings[i].racer)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return true;
            }
            return false;
        }

        private TextMeshPro CreateLabel(RaceParticipant racer)
        {
            GameObject labelObject = new GameObject("Race Nameplate");
            labelObject.transform.SetParent(racer.transform, false);
            labelObject.transform.localPosition = Vector3.up * labelHeight;
            labelObject.transform.localScale = Vector3.one * GetEffectiveLabelScale();

            TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = labelFontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.outlineWidth = 0.18f;
            label.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            if (labelFont != null)
            {
                label.font = labelFont;
            }
            else if (TMP_Settings.defaultFontAsset != null)
            {
                label.font = TMP_Settings.defaultFontAsset;
            }

            label.text = "";
            label.color = secondPlaceColor;
            return label;
        }

        private float GetEffectiveLabelScale()
        {
            // Older scenes contain a serialized value of 3 for this field.
            // Cap that legacy value so the new nameplate cannot cover the screen.
            return Mathf.Min(labelScale, 1f) * Mathf.Clamp(labelScaleMultiplier, 0.2f, 1f);
        }

        private static string ResolveDisplayName(RaceParticipant racer)
        {
            if (racer == null)
                return "";

            PoseKartNetworkPlayerOwner networkPlayer =
                racer.GetComponent<PoseKartNetworkPlayerOwner>();
            if (networkPlayer != null && !string.IsNullOrWhiteSpace(networkPlayer.DisplayName))
                return networkPlayer.DisplayName;

            if (!string.IsNullOrWhiteSpace(racer.RacerName))
                return racer.RacerName;

            return racer.IsPlayer ? "Player" : "AI";
        }

        private static bool ShouldDisplayLabel(RaceParticipant racer)
        {
            if (racer == null)
                return false;

            NetworkObject networkObject =
                racer.GetComponentInParent<NetworkObject>();

            if (networkObject != null && networkObject.IsPlayerObject)
            {
                return !networkObject.IsOwner;
            }

            // Offline racers and AI keep the existing label behavior.
            return true;
        }

        private UnityEngine.Camera ResolveCamera()
        {
            if (targetCamera != null && targetCamera.isActiveAndEnabled)
            {
                return targetCamera;
            }

            targetCamera = UnityEngine.Camera.main;

            if (targetCamera == null)
            {
                targetCamera = FindFirstObjectByType<UnityEngine.Camera>();
            }

            return targetCamera;
        }

        private Color GetPlaceColor(int position)
        {
            switch (position)
            {
                case 1: return firstPlaceColor;
                case 2: return secondPlaceColor;
                case 3: return thirdPlaceColor;
                default: return fourthPlaceColor;
            }
        }

        private void OnDestroy()
        {
            DestroyBindings();
        }

        private void OnDisable()
        {
            DestroyBindings();
        }

        private void DestroyBindings()
        {
            if (bindings == null) return;

            for (int index = 0; index < bindings.Length; index++)
            {
                if (bindings[index] != null && bindings[index].text != null)
                    Destroy(bindings[index].text.gameObject);
            }

            bindings = null;
        }
    }
}
