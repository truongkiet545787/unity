namespace PoseKart.Race
{
    public enum RaceState
    {
        Waiting,
        Countdown,
        Racing,
        Finished
    }

    public enum CheckpointProgressResult
    {
        Ignored,
        Advanced,
        LapCompleted,
        RaceCompleted
    }
}
