namespace Trailblazer.Navigation;

/// <summary>
/// Identifies the built-in path request strategy used when a object builds guided requests from a target position.
/// </summary>
public enum SolidPathAlgorithm
{
    /// <summary>
    /// Provides functionality for performing A* pathfinding operations.
    /// </summary>
    AStar,
    /// <summary>
    /// Provides functionality for performing flow field pathfinding operations.
    /// </summary>
    FlowField
}
