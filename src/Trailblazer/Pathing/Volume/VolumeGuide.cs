using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Waypoint guide used for raw voxel volume detours.
/// </summary>
public sealed class VolumeGuide : IWaypointGuide
{
    public VolumeSurveyResult TrailMap { get; private set; }

    public int CurrentWaypointIndex { get; private set; }

    private int _lastTriedIndex;

    public AStarWaypoint[] ActiveWaypoints => TrailMap.Waypoints;

    public bool Initialize(VolumeSurveyResult surveyResult)
    {
        if (!surveyResult.HasPath)
            return false;

        TrailMap = surveyResult;
        CurrentWaypointIndex = surveyResult.Waypoints.Length > 1 ? 1 : 0;
        _lastTriedIndex = CurrentWaypointIndex;
        return true;
    }

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

    public void AdvanceWaypoint() => CurrentWaypointIndex++;

    public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;

        if (!TrailMap.HasPath)
            return false;

        int closestIndex = GetIndex(origin);
        if (closestIndex == -1)
            return false;

        direction = (ActiveWaypoints[closestIndex].Position - origin).Normalize();
        return true;
    }

    public Vector3d GetMovementDirection(Vector3d origin)
    {
        if (!TrailMap.HasPath
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

    public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;

        if (ActiveWaypoints.Length == 0)
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

    public bool TryGetWaypointAt(int index, out AStarWaypoint waypoint)
    {
        if (!TrailMap.HasPath || index < 0 || index >= ActiveWaypoints.Length)
        {
            waypoint = default;
            return false;
        }

        waypoint = ActiveWaypoints[index];
        return true;
    }
}
