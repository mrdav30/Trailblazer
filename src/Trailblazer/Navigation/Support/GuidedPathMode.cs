namespace Trailblazer.Navigation;

/// <summary>
/// Identifies the built-in path request strategy used when a navigator builds guided requests from a target position.
/// </summary>
public enum GuidedPathMode
{
    AStar,
    FlowField,
    Aerial
}
