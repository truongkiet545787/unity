using PoseKart.UI;
using TMPro;
using UnityEngine;

namespace PoseKart.Multiplayer
{
    /// <summary>
    /// Hiển thị lựa chọn của PHIÊN CHƠI HIỆN TẠI ở Lobby.
    /// Không gửi RPC và không xác nhận Server đã nhận lựa chọn.
    /// Gắn lên GameObject có TextMeshProUGUI trong Online Lobby.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartLobbyVehicleDebug : MonoBehaviour
    {
        [SerializeField] private TMP_Text targetText;
        [SerializeField, Min(0.1f)] private float refreshSeconds = 0.25f;

        private float nextRefreshTime;
        private string lastDisplay;

        private void Awake()
        {
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Refresh();
            nextRefreshTime = Time.unscaledTime + refreshSeconds;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshSeconds;
            Refresh();
        }

        [ContextMenu("Refresh Local Vehicle Label")]
        public void Refresh()
        {
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();

            if (targetText == null)
                return;

            bool hasVehicle = PlayerPrefs.HasKey(PoseKartVehiclePaint.VehiclePreferenceKey);
            bool hasColor = PlayerPrefs.HasKey(PoseKartVehiclePaint.ColorPreferenceKey);
            PoseKartLocalVehicleSelection.Get(out int vehicle, out int color);
            bool liveChoice = PoseKartLocalVehicleSelection.HasCurrentChoice;

            string vehicleName = NameAt(PoseKartVehiclePaint.VehicleNames, vehicle);
            string colorName = NameAt(PoseKartVehiclePaint.ColorNames, color);

            string display =
                $"LOCAL MODEL: {vehicleName} [{vehicle}]" + (liveChoice || hasVehicle ? "" : " (NOT SAVED)") +
                $"\nLOCAL COLOR: {colorName} [{color}]" + (liveChoice || hasColor ? "" : " (NOT SAVED)") +
                $"\nSOURCE: {(liveChoice ? "SESSION" : "PREFS FALLBACK")}";

            if (display == lastDisplay)
                return;

            targetText.text = display;
            lastDisplay = display;
        }

        private static string NameAt(string[] names, int index)
        {
            return names != null && index >= 0 && index < names.Length
                ? names[index]
                : "INVALID INDEX";
        }
    }
}
