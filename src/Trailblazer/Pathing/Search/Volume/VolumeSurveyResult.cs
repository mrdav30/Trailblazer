using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the reusable waypoint trail generated for a raw volume request.
/// </summary>
public sealed class VolumeSurveyResult : SurveyResult
{
    /// <summary>
    /// Gets the sequence of waypoints calculated by the A* pathfinding algorithm.
    /// </summary>
    public AStarWaypoint[]? Waypoints { get; private set; }

    /// <inheritdoc/>
    public override bool HasPath => IsValid && Waypoints != null && Waypoints.Length > 0;

    /// <summary>
    /// Represents an empty result for a volume survey operation.
    /// </summary>
    /// <remarks>Use this field to represent a default or uninitialized state when no survey data is available.</remarks>
    public static readonly VolumeSurveyResult Empty = new();

    private VolumeSurveyResult() { }

    /// <summary>
    /// Creates a new instance of the VolumeSurveyResult class using the specified waypoints, charts, and key.
    /// </summary>
    /// <param name="waypoints">An array of AStarWaypoint objects representing the waypoints to include in the survey result. Can be null.</param>
    /// <param name="chartsUtilized">An array of chart names that were utilized in the survey. If null, an empty array is used.</param>
    /// <param name="key">A key used to identify or hash the request associated with this survey result.</param>
    /// <returns>A new VolumeSurveyResult instance initialized with the provided waypoints, charts, and key.</returns>
    public static VolumeSurveyResult Create(
        AStarWaypoint[] waypoints,
        string[] chartsUtilized,
        int key) =>
        Create(PathManager.TryGetActiveState(out PathingWorldState? state) ? state!.Context : null, waypoints, chartsUtilized, key);

    internal static VolumeSurveyResult Create(
        TrailblazerWorldContext? context,
        AStarWaypoint[] waypoints,
        string[] chartsUtilized,
        int key)
    {
        return new VolumeSurveyResult()
        {
            IsValid = true,
            IsInUse = false,
            Context = context,
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
            Waypoints = waypoints,
            LastUsedFrame = -1,
            RequestHashKey = key
        };
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        Waypoints = null;
    }
}
