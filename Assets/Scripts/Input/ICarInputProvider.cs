namespace PoseKart.Input
{
    /// <summary>
    /// Normalized driving input consumed by a vehicle controller.
    /// Keyboard, pose, and AI providers can share this contract.
    /// </summary>
    public interface ICarInputProvider
    {
        float Steering { get; }
        float Throttle { get; }
        float Brake { get; }
        bool Boost { get; }
        bool IsInputValid { get; }
    }
}
