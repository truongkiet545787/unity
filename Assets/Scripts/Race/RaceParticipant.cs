using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class RaceParticipant : MonoBehaviour
    {
        [SerializeField] private string racerName;
        [SerializeField] private bool player;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CarController carController;
        [SerializeField] private LapTracker lapTracker;

        [Header("Runtime Debug (Read Only)")]
        [SerializeField] private int racePosition = 1;
        [SerializeField] private bool finished;
        [SerializeField] private int finishOrder;
        [SerializeField] private double finishTime;

        public string RacerName => racerName;
        public bool IsPlayer => player;
        public Rigidbody Body => body;
        public CarController CarController => carController;
        public LapTracker LapTracker => lapTracker;
        public int RacePosition => racePosition;
        public bool Finished => finished;
        public int FinishOrder => finishOrder;
        public double FinishTime => finishTime;
        public bool IsConfigured => body != null && carController != null && lapTracker != null;

        public void Configure(
            string displayName,
            bool isPlayer,
            Rigidbody racerBody,
            CarController controller,
            LapTracker tracker)
        {
            racerName = displayName;
            player = isPlayer;
            body = racerBody;
            carController = controller;
            lapTracker = tracker;
        }

        public void PrepareRace(int totalLaps, int checkpointCount)
        {
            lapTracker.Configure(totalLaps, checkpointCount);
            racePosition = 1;
            finished = false;
            finishOrder = 0;
            finishTime = 0d;
        }

        public void MarkFinished(int order, double time)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            finishOrder = order;
            finishTime = time;
        }

        public void SetRacePosition(int position)
        {
            racePosition = Mathf.Max(1, position);
        }
    }
}
