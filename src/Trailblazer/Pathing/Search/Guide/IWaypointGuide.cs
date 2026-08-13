//=======================================================================
// IWaypointGuide.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines an interface for guiding an agent along a sequence of waypoints, providing methods to query and advance
/// waypoint progression and to determine movement direction.
/// </summary>
/// <remarks>
/// Implementations of this interface are responsible for managing waypoint navigation logic, including
/// tracking the agent's current position within the waypoint sequence and determining the appropriate direction for
/// movement. This interface is typically used in pathfinding or navigation systems where agents must follow a
/// predefined route.
/// </remarks>
public interface IWaypointGuide : IGuide
{
    /// <summary>
    /// Returns the index of the current waypoint being pursued.
    /// </summary>
    int CurrentWaypointIndex { get; }

    /// <summary>
    /// Returns the index used to track guide progression based on the given position.
    /// </summary>
    /// <param name="from">The agent’s current position.</param>
    /// <returns>The progression index corresponding to the given position.</returns>
    int GetIndex(Vector3d from);

    /// <summary>
    /// Advances the current waypoint index (called when agent has reached the current target).
    /// </summary>
    void AdvanceWaypoint();

    /// <summary>
    /// Returns the direction the agent should move in, based on the current path and position.
    /// </summary>
    /// <param name="from">The current position of the agent.</param>
    /// <returns>A normalized direction vector toward the next target.</returns>
    Vector3d GetCurrentWaypointDirection(Vector3d from);
}
