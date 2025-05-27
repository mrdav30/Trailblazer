using FixedMathSharp;

/// <summary>
/// Defines the contract for a navigation guide responsible for computing and providing movement directions
/// to reach a specified destination using a particular pathfinding strategy (e.g., A* or flow field).
/// </summary>
public interface IGuide
{
    /// <summary>
    /// Indicates whether a valid path has been computed.
    /// </summary>
    public bool HasPath { get; }

    /// <summary>
    /// Indicates whether the path contains additional waypoints to follow.
    /// </summary>
    public bool HasWaypoints { get; }

    /// <summary>
    /// Indicates whether the agent has arrived at the destination.
    /// </summary>
    public bool HasArrived { get; }

    /// <summary>
    /// Called once when the guide is initialized or recycled for reuse.
    /// </summary>
    void OnSetup();

    /// <summary>
    /// Requests a movement path from a start to a destination position, given a unit size.
    /// </summary>
    /// <param name="from">The starting world position.</param>
    /// <param name="destination">The target world position.</param>
    /// <param name="size">The unit size used to compute valid paths.</param>
    void RequestMovementPath(Vector3d from, Vector3d destination, Fixed64 size);

    /// <summary>
    /// Returns the direction the agent should move in, based on the current path and position.
    /// </summary>
    /// <param name="from">The current position of the agent.</param>
    /// <returns>A normalized direction vector toward the next target.</returns>
    Vector3d GetMovementDirection(Vector3d from);

    /// <summary>
    /// Advances the internal path index to the next waypoint.
    /// </summary>
    void MoveToNextWaypoint();

    /// <summary>
    /// Attempts to get the next waypoint in the path, if available.
    /// </summary>
    /// <param name="waypoint">The output waypoint position.</param>
    /// <returns>True if a waypoint is available; otherwise, false.</returns>
    bool TryGetNextWaypoint(out Vector3d waypoint);

    /// <summary>
    /// Resets the guide to a clean state for reuse or reinitialization.
    /// </summary>
    void Reset();
}
