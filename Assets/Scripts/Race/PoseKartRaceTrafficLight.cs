using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Presentation-only traffic light for the race start. It follows the
    /// existing RaceManager countdown event, so Online Host and Clients use
    /// the same shared countdown instead of starting another local timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartRaceTrafficLight : MonoBehaviour
    {
        [Header("Race Countdown")]
        [SerializeField] private RaceManager raceManager;

        [Header("Light Groups")]
        [Tooltip("Point/spot lights assigned to the red lamp(s).")]
        [SerializeField] private Light[] redLights;
        [Tooltip("Point/spot lights assigned to the amber/yellow lamp(s).")]
        [SerializeField] private Light[] amberLights;
        [Tooltip("Point/spot lights assigned to the green lamp(s).")]
        [SerializeField] private Light[] greenLights;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float redIntensity = 3f;
        [SerializeField, Min(0f)] private float amberIntensity = 3f;
        [SerializeField, Min(0f)] private float greenIntensity = 4f;
        [SerializeField] private Color redColor = new Color(1f, 0.04f, 0.01f);
        [SerializeField] private Color amberColor = new Color(1f, 0.45f, 0.02f);
        [SerializeField] private Color greenColor = new Color(0.05f, 1f, 0.15f);

        private string lastCountdownDisplay;
        private RaceManager subscribedRaceManager;

        private void Awake()
        {
            ResolveRaceManager();
            SetAllLights(false);
        }

        private void OnEnable()
        {
            ResolveRaceManager();
            SubscribeRaceManager();
        }

        private void OnDisable()
        {
            if (subscribedRaceManager != null)
            {
                subscribedRaceManager.CountdownTickEvent -= HandleCountdownTick;
                subscribedRaceManager.RaceStartedEvent -= HandleRaceStarted;
                subscribedRaceManager = null;
            }
        }

        private void Update()
        {
            // Supports scene objects that become active after RaceManager and
            // also catches a current countdown state after a scene transition.
            if (raceManager == null)
                ResolveRaceManager();

            SubscribeRaceManager();

            if (raceManager == null || raceManager.State == RaceState.Waiting)
                return;

            string display = raceManager.CountdownDisplay;
            if (!string.IsNullOrEmpty(display) && display != lastCountdownDisplay)
                HandleCountdownTick(display == "GO!" ? 0 : ParseCountdown(display), display);
        }

        private void ResolveRaceManager()
        {
            if (raceManager == null)
                raceManager = FindFirstObjectByType<RaceManager>();
        }

        private void SubscribeRaceManager()
        {
            if (subscribedRaceManager == raceManager)
                return;

            if (subscribedRaceManager != null)
            {
                subscribedRaceManager.CountdownTickEvent -= HandleCountdownTick;
                subscribedRaceManager.RaceStartedEvent -= HandleRaceStarted;
            }

            subscribedRaceManager = raceManager;
            if (subscribedRaceManager != null)
            {
                subscribedRaceManager.CountdownTickEvent += HandleCountdownTick;
                subscribedRaceManager.RaceStartedEvent += HandleRaceStarted;
            }
        }

        private static int ParseCountdown(string display)
        {
            return int.TryParse(display, out int value) ? value : -1;
        }

        private void HandleCountdownTick(int count, string display)
        {
            lastCountdownDisplay = display;

            if (display == "GO!" || count <= 0)
            {
                SetGroup(redLights, false, redColor, redIntensity);
                SetGroup(amberLights, false, amberColor, amberIntensity);
                SetGroup(greenLights, true, greenColor, greenIntensity);
                return;
            }

            if (count >= 3)
            {
                SetGroup(redLights, true, redColor, redIntensity);
                SetGroup(amberLights, false, amberColor, amberIntensity);
                SetGroup(greenLights, false, greenColor, greenIntensity);
            }
            else if (count == 2)
            {
                // Red + amber gives a clear launch warning.
                SetGroup(redLights, true, redColor, redIntensity);
                SetGroup(amberLights, true, amberColor, amberIntensity);
                SetGroup(greenLights, false, greenColor, greenIntensity);
            }
            else
            {
                SetGroup(redLights, false, redColor, redIntensity);
                SetGroup(amberLights, true, amberColor, amberIntensity);
                SetGroup(greenLights, false, greenColor, greenIntensity);
            }
        }

        private void HandleRaceStarted()
        {
            HandleCountdownTick(0, "GO!");
        }

        private void SetAllLights(bool enabled)
        {
            SetGroup(redLights, enabled, redColor, redIntensity);
            SetGroup(amberLights, enabled, amberColor, amberIntensity);
            SetGroup(greenLights, enabled, greenColor, greenIntensity);
        }

        private static void SetGroup(
            Light[] lights,
            bool enabled,
            Color color,
            float intensity)
        {
            if (lights == null)
                return;

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                    continue;

                light.color = color;
                light.intensity = intensity;
                light.enabled = enabled;
            }
        }
    }
}
