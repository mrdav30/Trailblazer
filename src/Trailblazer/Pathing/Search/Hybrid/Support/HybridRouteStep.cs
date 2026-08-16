//=======================================================================
// HybridRouteStep.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal sealed class HybridRouteStep
{
    public PathQuery? SurfaceQuery { get; private set; }

    public VolumePathRequest? VolumeRequest { get; private set; }

    public TrailblazerWorldContext Context { get; private set; } = null!;

    public Vector3d WaypointPosition { get; private set; }

    private HybridRouteStep() { }

    public static HybridRouteStep Surface(
        TrailblazerWorldContext context,
        PathQuery query)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (query.Algorithm != PathAlgorithm.FlowField
            || query.AllowTransitions
            || query.Traversal.StartDomain != TraversalDomain.Surface
            || query.Traversal.TargetDomain != TraversalDomain.Surface
            || query.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            throw new ArgumentException(
                "Hybrid surface stages require a transition-disabled graph Flow query.",
                nameof(query));
        }

        return new HybridRouteStep
        {
            Context = context,
            SurfaceQuery = query
        };
    }

    public static HybridRouteStep Volume(VolumePathRequest request) => new()
        {
            Context = request.Context,
            VolumeRequest = request
        };

    public static HybridRouteStep Waypoint(
        TrailblazerWorldContext context,
        Vector3d position)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        return new()
        {
            Context = context,
            WaypointPosition = position
        };
    }
}
