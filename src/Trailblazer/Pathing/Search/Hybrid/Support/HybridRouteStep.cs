//=======================================================================
// HybridRouteStep.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System;

namespace Trailblazer.Pathing;

internal sealed class HybridRouteStep
{
    public HybridRouteStepKind Kind { get; private set; }

    public IPathRequest SegmentRequest { get; private set; } = null!;

    public TrailblazerWorldContext Context { get; private set; } = null!;

    public Vector3d WaypointPosition { get; private set; }

    public int AdditionalCost { get; private set; }

    public string[] SegmentChartKeys { get; private set; } = Array.Empty<string>();

    private HybridRouteStep() { }

    public static HybridRouteStep Segment(
        IPathRequest request,
        int additionalCost = 0,
        string[]? chartKeys = null) => new()
        {
            Kind = HybridRouteStepKind.PathSegment,
            SegmentRequest = request,
            Context = request.Context,
            AdditionalCost = additionalCost,
            SegmentChartKeys = chartKeys ?? Array.Empty<string>()
        };

    public static HybridRouteStep Waypoint(
        TrailblazerWorldContext context,
        Vector3d position,
        int additionalCost = 0)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        return new()
        {
            Kind = HybridRouteStepKind.Waypoint,
            Context = context,
            WaypointPosition = position,
            AdditionalCost = additionalCost
        };
    }
}
