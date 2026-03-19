using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the reusable waypoint trail generated for an aerial request.
/// </summary>
public sealed class AerialSurveyResult : SurveyResult
{
    public AStarWaypoint[] Waypoints { get; private set; }

    public override bool HasPath => IsValid && Waypoints != null && Waypoints.Length > 0;

    public static readonly AerialSurveyResult Empty = new();

    public static AerialSurveyResult Create(AStarWaypoint[] waypoints, int key)
    {
        return new AerialSurveyResult()
        {
            IsValid = true,
            IsInUse = false,
            ChartsUtilized = Array.Empty<string>(),
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
