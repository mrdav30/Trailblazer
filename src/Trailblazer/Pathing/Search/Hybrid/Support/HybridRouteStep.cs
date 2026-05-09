using FixedMathSharp;
using System;

namespace Trailblazer.Pathing;

internal sealed class HybridRouteStep
{
    public HybridRouteStepKind Kind { get; private set; }

    public IPathRequest SegmentRequest { get; private set; } = null!;

    public Vector3d WaypointPosition { get; private set; }

    public int AdditionalCost { get; private set; }

    public string[] SegmentChartKeys { get; private set; } = Array.Empty<string>();

    public static HybridRouteStep Segment(
        IPathRequest request,
        int additionalCost = 0,
        string[]? chartKeys = null) => new()
    {
        Kind = HybridRouteStepKind.PathSegment,
        SegmentRequest = request,
        AdditionalCost = additionalCost,
        SegmentChartKeys = chartKeys ?? Array.Empty<string>()
    };

    public static HybridRouteStep Waypoint(Vector3d position, int additionalCost = 0) => new()
    {
        Kind = HybridRouteStepKind.Waypoint,
        WaypointPosition = position,
        AdditionalCost = additionalCost
    };
}
