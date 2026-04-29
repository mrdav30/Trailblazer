using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the reusable waypoint trail generated for a raw volume request.
/// </summary>
public sealed class VolumeSurveyResult : SurveyResult
{
    public AStarWaypoint[]? Waypoints { get; private set; }

    public override bool HasPath => IsValid && Waypoints != null && Waypoints.Length > 0;

    public static readonly VolumeSurveyResult Empty = new();

    public static VolumeSurveyResult Create(
        AStarWaypoint[] waypoints,
        string[] chartsUtilized,
        int key)
    {
        return new VolumeSurveyResult()
        {
            IsValid = true,
            IsInUse = false,
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
            Waypoints = waypoints,
            LastUsedFrame = -1,
            RequestHashKey = key
        };
    }

    public override void Reset()
    {
        base.Reset();
        Waypoints = null;
    }
}
