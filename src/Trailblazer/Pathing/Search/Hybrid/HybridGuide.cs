using FixedMathSharp;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Internal waypoint guide produced from a staged transition-aware route.
/// </summary>
internal sealed class HybridGuide : IWaypointGuide
{
    /// <summary>
    /// The active waypoints for this guide, which may be generated from either A* or flow field segments depending on the current stage of the plan. 
    /// This allows the guide to provide consistent waypoint-based navigation regardless of the underlying pathfinding strategy used for each segment of the route.
    /// </summary>
    public AStarWaypoint[] ActiveWaypoints { get; private set; } = Array.Empty<AStarWaypoint>();

    /// <summary>
    /// Returns the index of the current waypoint being pursued. 
    /// This is used to track progression through the waypoints of the current stage in the plan, and it allows the guide to determine which waypoint to target for movement directions. 
    /// The index is updated as the agent reaches each waypoint, and it helps ensure that the guide provides directions toward the correct target as the agent moves along the route.
    /// </summary>
    public int CurrentWaypointIndex { get; private set; }

    /// <summary>
    /// Tracks the last waypoint index that was used to provide a fallback direction. 
    /// This helps ensure that fallback directions are provided in a forward progression along the path, rather than repeatedly returning the same fallback when the agent is stuck. 
    /// By updating this index each time a fallback direction is provided, the guide can offer more dynamic and contextually relevant fallback directions as the agent navigates through the waypoints of the current stage in the plan.
    /// </summary>
    private int _lastTriedIndex;

    /// <summary>
    /// Initializes the guide with the given waypoints for the current stage of the plan.
    /// </summary>
    /// <param name="waypoints">The waypoints to initialize the guide with.</param>
    /// <returns>True if the guide was successfully initialized; otherwise, false.</returns>
    public bool Initialize(AStarWaypoint[] waypoints)
    {
        if (waypoints == null || waypoints.Length == 0)
            return false;

        ActiveWaypoints = waypoints;
        CurrentWaypointIndex = waypoints.Length > 1 ? 1 : 0;
        _lastTriedIndex = CurrentWaypointIndex;
        return true;
    }

    /// <inheritdoc/>
    public int GetIndex(Vector3d from)
    {
        Fixed64 minDistSq = Fixed64.MAX_VALUE;
        int bestIndex = -1;
        for (int i = 0; i < ActiveWaypoints.Length; i++)
        {
            Fixed64 distSq = (from - ActiveWaypoints[i].Position).SqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                bestIndex = i;
            }

            if (minDistSq <= Fixed64.Epsilon)
                break;
        }

        return bestIndex;
    }

    /// <inheritdoc/>
    public void AdvanceWaypoint() => CurrentWaypointIndex++;

    /// <inheritdoc/>
    public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;

        if (ActiveWaypoints == null || ActiveWaypoints.Length == 0)
            return false;

        int closestIndex = GetIndex(origin);
        if (closestIndex == -1)
            return false;

        direction = (ActiveWaypoints[closestIndex].Position - origin).Normalize();
        return true;
    }

    /// <inheritdoc/>
    public Vector3d GetCurrentWaypointDirection(Vector3d origin)
    {
        if (ActiveWaypoints == null
            || CurrentWaypointIndex < 0
            || CurrentWaypointIndex >= ActiveWaypoints.Length)
        {
            return Vector3d.Zero;
        }

        Vector3d waypoint = ActiveWaypoints[CurrentWaypointIndex].Position;
        if (waypoint == Vector3d.Zero)
            return Vector3d.Zero;

        return (waypoint - origin).Normal;
    }

    /// <inheritdoc/>
    public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;

        if (ActiveWaypoints == null || ActiveWaypoints.Length == 0)
            return false;

        int searchStart = FixedMath.Clamp(_lastTriedIndex, 0, ActiveWaypoints.Length - 1);
        Fixed64 minDistSq = Fixed64.MAX_VALUE;
        int bestIndex = -1;

        for (int i = searchStart; i < ActiveWaypoints.Length; i++)
        {
            Fixed64 distSq = (from - ActiveWaypoints[i].Position).SqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return false;

        fallbackDirection = (ActiveWaypoints[bestIndex].Position - from).Normal;
        _lastTriedIndex = bestIndex;
        return true;
    }

    /// <summary>
    /// Attempts to get the waypoint at the specified index. 
    /// This can be used to retrieve specific waypoints for debugging, visualization, or advanced navigation logic that may require direct access to the waypoints of the current stage in the plan. 
    /// By providing a method to access waypoints by index, the guide allows for greater flexibility and control over how the waypoints are utilized within the broader pathing system.
    /// </summary>
    /// <param name="index">The index of the waypoint to retrieve.</param>
    /// <param name="waypoint">When this method returns, contains the waypoint at the specified index, if the index is valid; otherwise, the default value for the type.</param>
    /// <returns>True if the waypoint was successfully retrieved; otherwise, false.</returns>
    public bool TryGetWaypointAt(int index, out AStarWaypoint waypoint)
    {
        if (ActiveWaypoints == null || index < 0 || index >= ActiveWaypoints.Length)
        {
            waypoint = default;
            return false;
        }

        waypoint = ActiveWaypoints[index];
        return true;
    }
}
