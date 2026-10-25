using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Input
{
    [DisallowMultipleComponent]
    public sealed class GamepadInputProvider : MonoBehaviour, ICarInputProvider
    {
        [Header("References")]
        [SerializeField] private CarController carController;
        [SerializeField] private KartItemInventory itemInventory;

        [Header("Steering")]
        [SerializeField, Range(0f, 0.5f)] private float steeringDeadzone = 0.12f;
        [SerializeField, Range(0.25f, 2f)] private float steeringSensitivity = 1f;
        [SerializeField, Range(0.5f, 3f)] private float steeringCurve = 1.15f;
        [SerializeField] private bool invertSteering;

        [Header("Triggers")]
        [SerializeField, Range(0f, 0.5f)] private float triggerDeadzone = 0.05f;

        [Header("Pause")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private bool managePauseLocally = true;

        [Header("Camera Cycle")]
        [SerializeField] private UnityEngine.Camera[] gameplayCameras;
        [SerializeField] private int startingCameraIndex;

        [Header("Runtime")]
        [SerializeField] private float steering;
        [SerializeField] private float throttle;
        [SerializeField] private float brake;
        [SerializeField] private bool drift;
        [SerializeField] private bool paused;
        [SerializeField] private int activeCameraIndex;

        public float Steering => steering;
        public float Throttle => throttle;
        public float Brake => brake;
        public bool Boost => false;
        public bool IsInputValid => isActiveAndEnabled && Gamepad.current != null;
        public bool Drift => drift;

        private void Awake()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (itemInventory == null) itemInventory = GetComponent<KartItemInventory>();

            LoadSettings();

            int count = gameplayCameras != null ? gameplayCameras.Length : 0;
            activeCameraIndex = Mathf.Clamp(startingCameraIndex, 0, Mathf.Max(0, count - 1));
            if (count > 0) ApplyCameraIndex(activeCameraIndex);
        }

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                ClearDrivingInput();
                return;
            }

            steering = ProcessSteering(pad.leftStick.x.ReadValue());
            throttle = ProcessTrigger(pad.rightTrigger.ReadValue());
            brake = ProcessTrigger(pad.leftTrigger.ReadValue());
            drift = pad.buttonSouth.isPressed;

            // B / Circle = Nitro
            if (pad.buttonEast.wasPressedThisFrame && carController != null)
                carController.UseStoredNitro();

            // X / Square = Item
            if (pad.buttonWest.wasPressedThisFrame &&
                itemInventory != null &&
                itemInventory.HasItem)
                itemInventory.UseItem();

            // Y / Triangle = Camera
            if (pad.buttonNorth.wasPressedThisFrame)
                CycleCamera();

            // Start / Options = Pause
            if (pad.startButton.wasPressedThisFrame)
                TogglePause();
        }

        private float ProcessSteering(float value)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= steeringDeadzone) return 0f;

            magnitude = Mathf.InverseLerp(steeringDeadzone, 1f, magnitude);
            magnitude = Mathf.Pow(magnitude, steeringCurve) * steeringSensitivity;

            float result = Mathf.Clamp01(magnitude) * Mathf.Sign(value);
            if (invertSteering) result = -result;

            return Mathf.Clamp(result, -1f, 1f);
        }

        private float ProcessTrigger(float value)
        {
            if (value <= triggerDeadzone) return 0f;
            return Mathf.InverseLerp(triggerDeadzone, 1f, value);
        }

        private void CycleCamera()
        {
            if (gameplayCameras == null || gameplayCameras.Length <= 1) return;
            activeCameraIndex = (activeCameraIndex + 1) % gameplayCameras.Length;
            ApplyCameraIndex(activeCameraIndex);
        }

        private void ApplyCameraIndex(int index)
        {
            if (gameplayCameras == null || gameplayCameras.Length == 0) return;

            index = Mathf.Clamp(index, 0, gameplayCameras.Length - 1);

            for (int i = 0; i < gameplayCameras.Length; i++)
            {
                if (gameplayCameras[i] != null)
                    gameplayCameras[i].gameObject.SetActive(i == index);
            }

            activeCameraIndex = index;
        }

        public void TogglePause()
        {
            if (!managePauseLocally) return;

            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;

            if (pausePanel != null)
                pausePanel.SetActive(paused);

            if (paused)
                ClearDrivingInput();
        }

        public void ForceUnpause()
        {
            paused = false;
            Time.timeScale = 1f;
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        public void ClearDrivingInput()
        {
            steering = 0f;
            throttle = 0f;
            brake = 0f;
            drift = false;
        }

        private void OnDisable()
        {
            ClearDrivingInput();
        }

        public void SetSteeringSensitivity(float value)
        {
            steeringSensitivity = Mathf.Clamp(value, 0.25f, 2f);
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Sensitivity", steeringSensitivity);
            PlayerPrefs.Save();
        }

        public void SetSteeringDeadzone(float value)
        {
            steeringDeadzone = Mathf.Clamp(value, 0f, 0.5f);
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Deadzone", steeringDeadzone);
            PlayerPrefs.Save();
        }

        public void SetSteeringCurve(float value)
        {
            steeringCurve = Mathf.Clamp(value, 0.5f, 3f);
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Curve", steeringCurve);
            PlayerPrefs.Save();
        }

        public void SetInvertSteering(bool value)
        {
            invertSteering = value;
            PlayerPrefs.SetInt("PoseKart_Gamepad_InvertSteering", value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ResetControllerSettings()
        {
            steeringDeadzone = 0.12f;
            steeringSensitivity = 1f;
            steeringCurve = 1.15f;
            invertSteering = false;
            SaveSettings();
        }

        private void LoadSettings()
        {
            steeringSensitivity = PlayerPrefs.GetFloat("PoseKart_Gamepad_Sensitivity", steeringSensitivity);
            steeringDeadzone = PlayerPrefs.GetFloat("PoseKart_Gamepad_Deadzone", steeringDeadzone);
            steeringCurve = PlayerPrefs.GetFloat("PoseKart_Gamepad_Curve", steeringCurve);
            invertSteering = PlayerPrefs.GetInt("PoseKart_Gamepad_InvertSteering", invertSteering ? 1 : 0) == 1;
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Sensitivity", steeringSensitivity);
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Deadzone", steeringDeadzone);
            PlayerPrefs.SetFloat("PoseKart_Gamepad_Curve", steeringCurve);
            PlayerPrefs.SetInt("PoseKart_Gamepad_InvertSteering", invertSteering ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
