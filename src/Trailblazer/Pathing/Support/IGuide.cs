using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the contract for a navigation guide responsible for computing and providing movement directions
/// to reach a specified destination using a particular pathfinding strategy (e.g., A* or flow field).
/// </summary>
public interface IGuide
{
    /// <summary>
    /// Returns the direction the agent should move in, based on the current path and position.
    /// </summary>
    /// <param name="origin">The current position of the agent.</param>
    /// <param name="direction">A normalized direction vector toward the next target.</param>
    /// <returns>True if a direction can be derrived from the provided origin.</returns>
    bool TryGetMovementDirection(Vector3d origin, out Vector3d direction);

    /// <summary>
    /// Attempts to provide a fallback movement direction when agent is stuck.
    /// Returns false if no fallback is available.
    /// </summary>
    bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection);
}
