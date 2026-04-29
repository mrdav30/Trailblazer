using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides steering direction based on a sequence of waypoints generated from an A* pathfinding survey.
/// Suitable for direct point-to-point navigation along a computed path.
/// </summary>
public class AStarGuide : IWaypointGuide
{
    /// <summary>
    /// The result of the A* survey, containing the waypoints and path information needed to guide an agent along the path.
    /// </summary>
    public AStarSurveyResult TrailMap { get; private set; } = AStarSurveyResult.Empty;

    /// <summary>
    /// Cached smoothed waypoints generated from the original TrailMap waypoints. 
    /// This allows for optional smoothing (e.g. Catmull-Rom interpolation) without modifying the original path data.
    /// </summary>
    private AStarWaypoint[]? _smoothedWaypoints;

    /// <inheritdoc/>
    public int CurrentWaypointIndex { get; private set; }

    /// <summary>
    /// Indicates whether a smoothing algorithm like spline interpolation should be applied to the final path.
    /// </summary>
    public bool UseSplineSmoothing { get; set; }

    /// <summary>
    /// Tracks the last waypoint index that was used to provide a fallback direction. 
    /// This helps ensure that fallback directions are provided in a forward progression along the path, 
    /// rather than repeatedly returning the same fallback when the agent is stuck.
    /// </summary>
    private int _lastTriedIndex;

    /// <summary>
    /// Initializes the guide with the given A* survey result.
    /// </summary>
    /// <param name="surveyResult">The result of the A* survey containing the waypoints and path information.</param>
    /// <returns>True if the guide is successfully initialized with a valid path; otherwise, false.</returns>
    public bool Initialize(AStarSurveyResult surveyResult)
    {
        if (!surveyResult.HasPath)
            return false;

        TrailMap = surveyResult;
        CurrentWaypointIndex = 0;

        return true;
    }

    /// <summary>
    /// Gets the active waypoints for this guide, applying optional smoothing if enabled.
    /// </summary>
    public AStarWaypoint[] ActiveWaypoints
    {
        get
        {
            if (UseSplineSmoothing)
            {
                if (_smoothedWaypoints == null && TrailMap.Waypoints.Length >= 4)
                    _smoothedWaypoints = AStarSurveyor.CatmullSmooth(TrailMap.Waypoints);
                return _smoothedWaypoints ?? TrailMap.Waypoints;
            }

            return TrailMap.Waypoints;
        }
    }

    /// <summary>
    /// Determines whether the guide has reached the final waypoint.
    /// </summary>
    /// <returns>True if the guide has arrived at the final waypoint; otherwise, false.</returns>
    public bool HasArrived()
    {
        return TrailMap.HasPath && CurrentWaypointIndex == ActiveWaypoints.Length - 1;
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

        if (!TrailMap.HasPath)
            return false;

        int closestIndex = GetIndex(origin);
        direction = (ActiveWaypoints[closestIndex].Position - origin).Normalize();
        return true;
    }

    /// <inheritdoc/>
    public Vector3d GetCurrentWaypointDirection(Vector3d origin)
    {
        if (!TrailMap.HasPath || CurrentWaypointIndex < 0 || CurrentWaypointIndex >= ActiveWaypoints.Length)
            return Vector3d.Zero;

        Vector3d movementDirection = ActiveWaypoints[CurrentWaypointIndex].Position;
        if (movementDirection == Vector3d.Zero)
            return Vector3d.Zero;

        return (movementDirection - origin).Normal;
    }

    /// <inheritdoc/>
    public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;

        if (ActiveWaypoints.Length == 0)
            return false;

        // Start from CurrentIndex + 1 and search forward
        int searchStart = FixedMath.Clamp(_lastTriedIndex, 0, ActiveWaypoints.Length - 1);

        Fixed64 minDistSq = Fixed64.MAX_VALUE;
        int bestIndex = searchStart;

        for (int i = searchStart; i < ActiveWaypoints.Length; i++)
        {
            Fixed64 distSq = (from - ActiveWaypoints[i].Position).SqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                bestIndex = i;
            }
        }

        fallbackDirection = (ActiveWaypoints[bestIndex].Position - from).Normal;
        _lastTriedIndex = bestIndex;
        return true;
    }

    /// <summary>
    /// Attempts to get the waypoint at the specified index. 
    /// </summary>
    /// <param name="index">The index of the waypoint to retrieve.</param>
    /// <param name="waypoint">The waypoint at the specified index, if found.</param>
    /// <returns>True if the waypoint was successfully retrieved; otherwise, false.</returns>
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
