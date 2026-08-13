//=======================================================================
// VolumeGuide.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Waypoint guide used for raw voxel volume detours.
/// </summary>
public sealed class VolumeGuide : IWaypointGuide
{
    /// <summary>
    /// Gets the trail map result for the volume survey, if available.
    /// </summary>
    public VolumeSurveyResult? TrailMap { get; private set; }

    /// <inheritdoc/>
    public int CurrentWaypointIndex { get; private set; }

    private int _lastTriedIndex;

    /// <summary>
    /// Gets the collection of currently active waypoints used for pathfinding.
    /// </summary>
    /// <remarks>Returns an empty array if no trail map is available or if there are no active waypoints.</remarks>
    public AStarWaypoint[] ActiveWaypoints => TrailMap?.Waypoints ?? Array.Empty<AStarWaypoint>();

    /// <summary>
    /// Initializes the navigation state using the specified survey result.
    /// </summary>
    /// <param name="surveyResult">The survey result containing the path and waypoints to initialize navigation. Must have a valid path.</param>
    /// <returns>true if initialization succeeds and a valid path is present; otherwise, false.</returns>
    public bool Initialize(VolumeSurveyResult surveyResult)
    {
        if (!surveyResult.HasPath)
            return false;

        AStarWaypoint[] waypoints = surveyResult.Waypoints!;
        TrailMap = surveyResult;
        CurrentWaypointIndex = waypoints.Length > 1 ? 1 : 0;
        _lastTriedIndex = CurrentWaypointIndex;
        return true;
    }

    /// <inheritdoc/>
    public int GetIndex(Vector3d from)
    {
        Fixed64 minDistSq = Fixed64.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < ActiveWaypoints.Length; i++)
        {
            Fixed64 distSq = (from - ActiveWaypoints[i].Position).MagnitudeSquared;
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

        if (TrailMap == null || !TrailMap.HasPath)
            return false;

        int closestIndex = GetIndex(origin);
        if (closestIndex == -1)
            return false;

        direction = (ActiveWaypoints[closestIndex].Position - origin).Normalized;
        return true;
    }

    /// <inheritdoc/>
    public Vector3d GetCurrentWaypointDirection(Vector3d origin)
    {
        if (TrailMap == null
            || !TrailMap.HasPath
            || CurrentWaypointIndex < 0
            || CurrentWaypointIndex >= ActiveWaypoints.Length)
        {
            return Vector3d.Zero;
        }

        Vector3d waypoint = ActiveWaypoints[CurrentWaypointIndex].Position;
        if (waypoint == Vector3d.Zero)
            return Vector3d.Zero;

        return (waypoint - origin).Normalized;
    }

    /// <inheritdoc/>
    public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;

        if (TrailMap == null || ActiveWaypoints.Length == 0)
            return false;

        int searchStart = FixedMath.Clamp(_lastTriedIndex, 0, ActiveWaypoints.Length - 1);
        Fixed64 minDistSq = Fixed64.MaxValue;
        int bestIndex = -1;

        for (int i = searchStart; i < ActiveWaypoints.Length; i++)
        {
            Fixed64 distSq = (from - ActiveWaypoints[i].Position).MagnitudeSquared;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return false;

        fallbackDirection = (ActiveWaypoints[bestIndex].Position - from).Normalized;
        _lastTriedIndex = bestIndex;
        return true;
    }

    /// <summary>
    /// Attempts to retrieve the waypoint at the specified index in the active waypoints collection.
    /// </summary>
    /// <remarks>Returns false if the trail map is null, does not have a path, or if the index is out of range.</remarks>
    /// <param name="index">The zero-based index of the waypoint to retrieve. Must be within the bounds of the active waypoints collection.</param>
    /// <param name="waypoint">
    /// When this method returns, contains the waypoint at the specified index if the operation succeeds; otherwise,
    /// the default value for <see cref="AStarWaypoint"/>.
    /// </param>
    /// <returns>true if the waypoint at the specified index was successfully retrieved; otherwise, false.</returns>
    public bool TryGetWaypointAt(int index, out AStarWaypoint waypoint)
    {
        if (TrailMap == null || !TrailMap.HasPath || index < 0 || index >= ActiveWaypoints.Length)
        {
            waypoint = default;
            return false;
        }

        waypoint = ActiveWaypoints[index];
        return true;
    }

    internal void ResetForReuse()
    {
        TrailMap = null;
        CurrentWaypointIndex = 0;
        _lastTriedIndex = 0;
    }
}
