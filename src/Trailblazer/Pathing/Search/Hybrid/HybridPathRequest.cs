//=======================================================================
// HybridPathRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Immutable internal carrier for graph Flow intent and its staged transition route plan.
/// </summary>
internal sealed class HybridPathRequest
{
    internal PathQuery? SurfaceIntent { get; }

    internal TrailblazerWorldContext Context { get; }

    internal Vector3d Origin { get; }

    internal Voxel? StartNode { get; }

    internal Vector3d TargetPosition { get; }

    internal Voxel? EndNode { get; }

    internal Fixed64 UnitSize { get; }

    internal bool AllowUnwalkableEndpoints { get; }

    internal bool HasValidEndpoints => StartNode != null && EndNode != null;

    internal HybridRoutePlan? RoutePlan { get; }

    private HybridPathRequest(TrailblazerWorldContext context, PathQuery query)
    {
        Context = context;
        SurfaceIntent = query;
        Origin = query.Start.Position;
        TargetPosition = query.End.Position;
        UnitSize = query.Agent.Shape.Radius + query.Agent.Shape.Radius;
        AllowUnwalkableEndpoints = query.Start.Resolution != EndpointResolutionPolicy.Strict
            || query.End.Resolution != EndpointResolutionPolicy.Strict;
        context.World.TryGetVoxel(Origin, out Voxel? startNode);
        context.World.TryGetVoxel(TargetPosition, out Voxel? endNode);
        StartNode = startNode;
        EndNode = endNode;

        HybridRoutePlan? routePlan;
        using (PathManager.EnterState(Context.Pathing.State))
            HybridRoutePlanner.TryPlan(this, out routePlan);

        RoutePlan = routePlan;
    }

    internal static HybridPathRequest? Create(
        TrailblazerWorldContext context,
        PathQuery query)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (query.Algorithm != PathAlgorithm.FlowField
            || !query.AllowTransitions
            || query.Traversal.StartDomain != TraversalDomain.Surface
            || query.Traversal.TargetDomain != TraversalDomain.Surface
            || query.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return null;
        }

        var request = new HybridPathRequest(context, query);
        return request.RoutePlan != null ? request : null;
    }
}
