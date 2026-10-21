
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PoseKart.UI
{
    public sealed class PoseKartVehicleSelect : MonoBehaviour
    {
        private const string RaceScene = "PoseKart_ReferenceIsland";

        [Header("Preview")]
        [SerializeField] private PoseKartVehiclePaint preview;

        [Header("Labels")]
        [SerializeField] private Text vehicleName;
        [SerializeField] private Text colorName;

        [Header("Buttons")]
        [SerializeField] private Button previousVehicle;
        [SerializeField] private Button nextVehicle;
        [SerializeField] private Button previousColor;
        [SerializeField] private Button nextColor;
        [SerializeField] private Button startRace;

        private int selectedVehicle;
        private int selectedColor;

        private bool started;
        private bool listenersBound;
        private bool raceLoading;

        public void Configure(
            PoseKartVehiclePaint paint,
            Text vehicleLabel,
            Text colorLabel,
            Button vehicleLeft,
            Button vehicleRight,
            Button colorLeft,
            Button colorRight,
            Button start)
        {
            UnbindButtons();

            preview = paint;
            vehicleName = vehicleLabel;
            colorName = colorLabel;

            previousVehicle = vehicleLeft;
            nextVehicle = vehicleRight;
            previousColor = colorLeft;
            nextColor = colorRight;
            startRace = start;

            // Also works if Configure is called after Start.
            if (started)
                TryInitialize();
        }

        private void Start()
        {
            started = true;

            selectedVehicle = PlayerPrefs.GetInt(
                PoseKartVehiclePaint.VehiclePreferenceKey, 0);

            selectedColor = PlayerPrefs.GetInt(
                PoseKartVehiclePaint.ColorPreferenceKey, 0);

            TryInitialize();
        }

        private void TryInitialize()
        {
            if (preview == null ||
                vehicleName == null ||
                colorName == null ||
                previousVehicle == null ||
                nextVehicle == null ||
                previousColor == null ||
                nextColor == null ||
                startRace == null)
            {
                Debug.LogError(
                    "[PoseKartVehicleSelect] Missing Inspector references. " +
                    "Check Preview, both Text labels and all five Buttons. " +
                    "If another script calls Configure(), check the objects passed into it.",
                    this);

                return;
            }

            if (preview.VehicleCount <= 0 || preview.ColorCount <= 0)
            {
                Debug.LogError(
                    "[PoseKartVehicleSelect] Preview has no available vehicles or colors.",
                    preview);

                return;
            }

            selectedVehicle = Mathf.Clamp(
                selectedVehicle, 0, preview.VehicleCount - 1);

            selectedColor = Mathf.Clamp(
                selectedColor, 0, preview.ColorCount - 1);

            if (!listenersBound)
            {
                previousVehicle.onClick.AddListener(PreviousVehicle);
                nextVehicle.onClick.AddListener(NextVehicle);

                previousColor.onClick.AddListener(PreviousColor);
                nextColor.onClick.AddListener(NextColor);

                startRace.onClick.AddListener(StartRace);

                listenersBound = true;
            }

            Refresh();
        }

        private void PreviousVehicle()
        {
            if (preview == null || preview.VehicleCount <= 0)
                return;

            selectedVehicle =
                (selectedVehicle + preview.VehicleCount - 1)
                % preview.VehicleCount;

            Refresh();
        }

        private void NextVehicle()
        {
            if (preview == null || preview.VehicleCount <= 0)
                return;

            selectedVehicle =
                (selectedVehicle + 1) % preview.VehicleCount;

            Refresh();
        }

        private void PreviousColor()
        {
            if (preview == null || preview.ColorCount <= 0)
                return;

            selectedColor =
                (selectedColor + preview.ColorCount - 1)
                % preview.ColorCount;

            Refresh();
        }

        private void NextColor()
        {
            if (preview == null || preview.ColorCount <= 0)
                return;

            selectedColor =
                (selectedColor + 1) % preview.ColorCount;

            Refresh();
        }

        private void Refresh()
        {
            if (preview == null || vehicleName == null || colorName == null)
                return;

            preview.Apply(selectedVehicle, selectedColor);

            vehicleName.text =
                "TYPE  /  " +
                PoseKartVehiclePaint.VehicleNames[selectedVehicle];

            colorName.text =
                "COLOR  /  " +
                PoseKartVehiclePaint.ColorNames[selectedColor];
        }

        private void StartRace()
        {
            if (raceLoading)
                return;

            if (!Application.CanStreamedLevelBeLoaded(RaceScene))
            {
                if (vehicleName != null)
                    vehicleName.text =
                        "Reference Island is not in Build Settings";

                return;
            }

            raceLoading = true;

            PlayerPrefs.SetInt(
                PoseKartVehiclePaint.VehiclePreferenceKey,
                selectedVehicle);

            PlayerPrefs.SetInt(
                PoseKartVehiclePaint.ColorPreferenceKey,
                selectedColor);

            PlayerPrefs.Save();

            SceneManager.LoadScene(RaceScene);
        }

        private void UnbindButtons()
        {
            if (!listenersBound)
                return;

            if (previousVehicle != null)
                previousVehicle.onClick.RemoveListener(PreviousVehicle);

            if (nextVehicle != null)
                nextVehicle.onClick.RemoveListener(NextVehicle);

            if (previousColor != null)
                previousColor.onClick.RemoveListener(PreviousColor);

            if (nextColor != null)
                nextColor.onClick.RemoveListener(NextColor);

            if (startRace != null)
                startRace.onClick.RemoveListener(StartRace);

            listenersBound = false;
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }
    }
}
