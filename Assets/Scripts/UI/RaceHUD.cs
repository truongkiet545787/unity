using PoseKart.Race;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    [DisallowMultipleComponent]
    public sealed class RaceHUD : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private Text positionText;
        [SerializeField] private Text lapText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text countdownText;
        [SerializeField] private Text raceStatusText;

        public bool IsConfigured =>
            raceManager != null
            && positionText != null
            && lapText != null
            && timeText != null
            && countdownText != null
            && raceStatusText != null;

        public void Configure(
            RaceManager manager,
            Text position,
            Text lap,
            Text time,
            Text countdown,
            Text raceStatus)
        {
            raceManager = manager;
            positionText = position;
            lapText = lap;
            timeText = time;
            countdownText = countdown;
            raceStatusText = raceStatus;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (!IsConfigured)
            {
                return;
            }

            positionText.text = $"POSITION\n{raceManager.PlayerPosition}/{raceManager.RacerCount}";
            lapText.text = $"LAP\n{raceManager.CurrentLap}/{raceManager.TotalLaps}";
            timeText.text = $"TIME\n{FormatTime(raceManager.RaceTimeSeconds)}";
            countdownText.text = raceManager.CountdownDisplay;
            raceStatusText.text = $"RACE STATUS\n{raceManager.State.ToString().ToUpperInvariant()}";
        }

        private static string FormatTime(double totalSeconds)
        {
            totalSeconds = System.Math.Max(0d, totalSeconds);
            int minutes = (int)(totalSeconds / 60d);
            int seconds = (int)(totalSeconds % 60d);
            int milliseconds = (int)((totalSeconds - System.Math.Floor(totalSeconds)) * 1000d);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }
    }
}
