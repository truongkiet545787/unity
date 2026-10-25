using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Input
{
    [DisallowMultipleComponent]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        public enum ActiveInput { Keyboard, Gamepad }

        [SerializeField] private CarController carController;
        [SerializeField] private KeyboardInputProvider keyboardInput;
        [SerializeField] private GamepadInputProvider gamepadInput;

        [Header("Auto Switch")]
        [SerializeField] private bool autoSwitch = true;
        [SerializeField, Range(0.01f, 0.5f)] private float stickThreshold = 0.18f;
        [SerializeField, Range(0.01f, 0.5f)] private float triggerThreshold = 0.10f;

        [Header("Runtime")]
        [SerializeField] private ActiveInput activeInput = ActiveInput.Keyboard;

        public ActiveInput CurrentInput => activeInput;
        public bool UsingGamepad => activeInput == ActiveInput.Gamepad;

        private void Awake()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (keyboardInput == null) keyboardInput = GetComponent<KeyboardInputProvider>();
            if (gamepadInput == null) gamepadInput = GetComponent<GamepadInputProvider>();
        }

        private void Start()
        {
            SwitchToKeyboard();
        }

        private void Update()
        {
            if (!autoSwitch || carController == null) return;

            if (WasGamepadUsed())
            {
                if (activeInput != ActiveInput.Gamepad) SwitchToGamepad();
                return;
            }

            if (WasKeyboardUsed() && activeInput != ActiveInput.Keyboard)
                SwitchToKeyboard();
        }

        private bool WasGamepadUsed()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null) return false;

            float sqr = stickThreshold * stickThreshold;

            return pad.leftStick.ReadValue().sqrMagnitude > sqr ||
                   pad.rightStick.ReadValue().sqrMagnitude > sqr ||
                   pad.leftTrigger.ReadValue() > triggerThreshold ||
                   pad.rightTrigger.ReadValue() > triggerThreshold ||
                   pad.buttonSouth.wasPressedThisFrame ||
                   pad.buttonEast.wasPressedThisFrame ||
                   pad.buttonWest.wasPressedThisFrame ||
                   pad.buttonNorth.wasPressedThisFrame ||
                   pad.leftShoulder.wasPressedThisFrame ||
                   pad.rightShoulder.wasPressedThisFrame ||
                   pad.startButton.wasPressedThisFrame ||
                   pad.selectButton.wasPressedThisFrame ||
                   pad.dpad.ReadValue().sqrMagnitude > 0.1f;
        }

        private bool WasKeyboardUsed()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;

            return kb.wKey.isPressed ||
                   kb.aKey.isPressed ||
                   kb.sKey.isPressed ||
                   kb.dKey.isPressed ||
                   kb.upArrowKey.isPressed ||
                   kb.downArrowKey.isPressed ||
                   kb.leftArrowKey.isPressed ||
                   kb.rightArrowKey.isPressed ||
                   kb.spaceKey.isPressed ||
                   kb.eKey.wasPressedThisFrame ||
                   kb.escapeKey.wasPressedThisFrame;
        }

        public void SwitchToGamepad()
        {
            if (gamepadInput == null || Gamepad.current == null || carController == null) return;

            if (keyboardInput != null) keyboardInput.enabled = false;

            gamepadInput.enabled = true;
            gamepadInput.ClearDrivingInput();
            carController.SetInputProvider(gamepadInput);
            activeInput = ActiveInput.Gamepad;
        }

        public void SwitchToKeyboard()
        {
            if (keyboardInput == null || carController == null) return;

            if (gamepadInput != null)
            {
                gamepadInput.ClearDrivingInput();
                gamepadInput.enabled = false;
            }

            keyboardInput.enabled = true;
            carController.SetInputProvider(keyboardInput);
            activeInput = ActiveInput.Keyboard;
        }
    }
}
