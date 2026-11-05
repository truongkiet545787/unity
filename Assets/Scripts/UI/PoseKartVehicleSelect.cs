
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

namespace PoseKart.UI
{
    public sealed class PoseKartVehicleSelect : MonoBehaviour
    {

        private const string DefaultTargetScene = "PoseKart_OnlineLobby";

        [Header("Scene Transition")]
        [SerializeField] private string targetScene = DefaultTargetScene;

        [Header("Preview")]
        [SerializeField] private PoseKartVehiclePaint preview;
        [Header("Default Panels")]
        [SerializeField] private GameObject controlGuidePanel;
        [Header("Labels")]
        [SerializeField] private Text vehicleName;
        [SerializeField] private Text colorName;

        [Header("Buttons")]
        [SerializeField] private Button previousVehicle;
        [SerializeField] private Button nextVehicle;
        [SerializeField] private Button previousColor;
        [SerializeField] private Button nextColor;
        [SerializeField] private Button startRace;

        [Header("Vehicle Change Animation")]
        [Tooltip("How far the preview platform and vehicle lower before swapping models.")]
        [SerializeField, Min(0f)] private float vehicleChangeDropDistance = 3.4f;
        [Tooltip("Total time for one vehicle to leave and the next vehicle to arrive.")]
        [SerializeField, Min(0.1f)] private float vehicleChangeDuration = 0.8f;
        [SerializeField, Min(0f)] private float vehicleChangeHoldDuration = 0.05f;
        [Tooltip("Small vertical launch above the platform before the new vehicle drops onto it.")]
        [SerializeField, Min(0f)] private float vehicleArrivalHopHeight = 0.65f;
        [SerializeField, Min(0.05f)] private float vehicleArrivalFallDuration = 0.35f;

        private int selectedVehicle;
        private int selectedColor;

        private bool started;
        private bool listenersBound;
        private bool raceLoading;
        private Coroutine vehicleChangeRoutine;
        private Vector3 previewBaseLocalPosition;
        private bool previewBasePositionCached;
        private Transform replacableSpinner;
        private Vector3 spinnerBaseWorldPosition;
        private bool spinnerBasePositionCached;

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

            // The current game instance owns its own selection. PlayerPrefs is
            // used only if the player has not chosen a vehicle this run.
            PoseKartLocalVehicleSelection.Get(
                out selectedVehicle, out selectedColor);

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

            CachePreviewPosition();

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

            // Build the graphics settings entry on this scene's existing Canvas.
            // The helper is idempotent, so re-initializing Vehicle Select cannot
            // create duplicate panels or buttons.
            PoseKartGraphicsSettingsUI.AttachToCanvas(GetComponent<Canvas>());

            // Khi vừa vào scene, hiển thị preview xe và ẩn bảng hướng dẫn.
            if (preview != null)
                preview.gameObject.SetActive(true);

            if (controlGuidePanel != null)
                controlGuidePanel.SetActive(false);
            // Khi vào màn chọn xe bằng tay cầm,
            // tự focus vào nút đổi xe bên phải.
            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == null)
            {
                EventSystem.current.SetSelectedGameObject(
                    nextVehicle.gameObject
                );
            }
        }

        private void PreviousVehicle()
        {
            if (preview == null || preview.VehicleCount <= 0)
                return;

            if (vehicleChangeRoutine != null)
                return;

            int nextVehicle =
                (selectedVehicle + preview.VehicleCount - 1)
                % preview.VehicleCount;

            ChangeVehicle(nextVehicle);
        }

        private void NextVehicle()
        {
            if (preview == null || preview.VehicleCount <= 0)
                return;

            if (vehicleChangeRoutine != null)
                return;

            int nextVehicle =
                (selectedVehicle + 1) % preview.VehicleCount;

            ChangeVehicle(nextVehicle);
        }

        private void ChangeVehicle(int nextVehicle)
        {
            if (nextVehicle == selectedVehicle)
                return;

            selectedVehicle = Mathf.Clamp(nextVehicle, 0, preview.VehicleCount - 1);
            RefreshLabels();
            vehicleChangeRoutine = StartCoroutine(AnimateVehicleChange());
        }

        private void PreviousColor()
        {
            if (preview == null || preview.ColorCount <= 0)
                return;

            if (vehicleChangeRoutine != null)
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

            if (vehicleChangeRoutine != null)
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

            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (preview == null || vehicleName == null || colorName == null)
                return;

            vehicleName.text =
                "TYPE  /  " +
                PoseKartVehiclePaint.VehicleNames[selectedVehicle];

            colorName.text =
                "COLOR  /  " +
                preview.GetColorName(selectedColor);
        }

        private void CachePreviewPosition()
        {
            if (preview == null || previewBasePositionCached)
            {
                ResolveSpinner();
                return;
            }

            previewBaseLocalPosition = preview.transform.localPosition;
            previewBasePositionCached = true;
            ResolveSpinner();
        }

        private void ResolveSpinner()
        {
            if (replacableSpinner == null)
            {
                GameObject spinnerObject = GameObject.Find("ReplacableSpinner");
                if (spinnerObject != null)
                    replacableSpinner = spinnerObject.transform;
            }

            if (replacableSpinner != null && !spinnerBasePositionCached)
            {
                spinnerBaseWorldPosition = replacableSpinner.position;
                spinnerBasePositionCached = true;
            }
        }

        private IEnumerator AnimateVehicleChange()
        {
            CachePreviewPosition();

            VehiclePreviewRotator rotator =
                preview != null ? preview.GetComponent<VehiclePreviewRotator>() : null;
            rotator?.SetRotation(false);

            float halfDuration = Mathf.Max(0.05f, vehicleChangeDuration * 0.5f);
            Vector3 loweredPosition = previewBaseLocalPosition +
                Vector3.down * vehicleChangeDropDistance;

            yield return MovePreviewAndSpinner(
                previewBaseLocalPosition,
                loweredPosition,
                0f,
                vehicleChangeDropDistance,
                halfDuration,
                false);

            // Swap while the platform is below the camera's main view.
            preview.Apply(selectedVehicle, selectedColor);

            if (vehicleChangeHoldDuration > 0f)
                yield return new WaitForSecondsRealtime(vehicleChangeHoldDuration);

            Vector3 launchPosition = previewBaseLocalPosition +
                Vector3.up * vehicleArrivalHopHeight;

            // The platform rises with the new vehicle, but the vehicle continues
            // slightly above it to create a small arcade-style launch.
            yield return MovePreviewAndSpinner(
                loweredPosition,
                launchPosition,
                vehicleChangeDropDistance,
                0f,
                halfDuration,
                false);

            // Let the new vehicle fall onto the platform instead of snapping.
            yield return MovePreviewAndSpinner(
                launchPosition,
                previewBaseLocalPosition,
                0f,
                0f,
                vehicleArrivalFallDuration,
                true);

            rotator?.SetRotation(true);
            vehicleChangeRoutine = null;
        }

        private IEnumerator MovePreviewAndSpinner(
            Vector3 from,
            Vector3 to,
            float fromSpinnerDrop,
            float toSpinnerDrop,
            float duration,
            bool accelerateDown)
        {
            if (preview == null)
                yield break;

            Vector3 spinnerFrom = spinnerBaseWorldPosition +
                Vector3.down * fromSpinnerDrop;
            Vector3 spinnerTo = spinnerBaseWorldPosition +
                Vector3.down * toSpinnerDrop;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = accelerateDown
                    ? normalized * normalized
                    : normalized * normalized * (3f - 2f * normalized);
                preview.transform.localPosition = Vector3.LerpUnclamped(from, to, eased);

                if (replacableSpinner != null && spinnerBasePositionCached)
                {
                    replacableSpinner.position = Vector3.LerpUnclamped(
                        spinnerFrom, spinnerTo, eased);
                }

                yield return null;
            }

            preview.transform.localPosition = to;

            if (replacableSpinner != null && spinnerBasePositionCached)
                replacableSpinner.position = spinnerTo;
        }

        private void StartRace()
        {
            if (raceLoading)
                return;

            string sceneToLoad = string.IsNullOrEmpty(targetScene) ? DefaultTargetScene : targetScene;

            if (!Application.CanStreamedLevelBeLoaded(sceneToLoad))
            {
                if (vehicleName != null)
                    vehicleName.text =
                        sceneToLoad + " is not in Build Settings";

                return;
            }

            raceLoading = true;

            // Commit BEFORE scene load. A shared PlayerPrefs store in multiple
            // editor players must not overwrite this running player's choice.
            PoseKartLocalVehicleSelection.Choose(selectedVehicle, selectedColor);
            Debug.Log($"[VehicleSelect] LOCAL CHOICE committed: vehicle={selectedVehicle}, color={selectedColor}", this);

            // Remember for next game launch (not the live online sync source).
            PlayerPrefs.SetInt(
                PoseKartVehiclePaint.VehiclePreferenceKey,
                selectedVehicle);

            PlayerPrefs.SetInt(
                PoseKartVehiclePaint.ColorPreferenceKey,
                selectedColor);

            PlayerPrefs.Save();

            if (Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsListening &&
                Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(
                    sceneToLoad,
                    LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(sceneToLoad);
            }
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
            if (vehicleChangeRoutine != null)
                StopCoroutine(vehicleChangeRoutine);

            UnbindButtons();
        }
    }
}
