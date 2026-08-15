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
/// Immutable internal carrier for a FlowField request and its staged transition route plan.
/// </summary>
internal sealed class HybridPathRequest
{
    internal TrailblazerWorldContext Context { get; }

    internal Vector3d Origin { get; }

    internal Voxel? StartNode { get; }

    internal Vector3d TargetPosition { get; }

    internal Voxel? EndNode { get; }

    internal Fixed64 UnitSize { get; }

    internal bool AllowUnwalkableEndpoints { get; }

    internal Fixed64 MaxClimbHeight { get; }

    internal int ExtraFloodRange { get; }

    internal bool HasValidEndpoints => StartNode != null && EndNode != null;

    internal HybridRoutePlan? RoutePlan { get; }

    private HybridPathRequest(FlowFieldPathRequest request)
    {
        Context = request.Context;
        Origin = request.Origin;
        StartNode = request.StartNode;
        TargetPosition = request.TargetPosition;
        EndNode = request.EndNode;
        UnitSize = request.UnitSize;
        AllowUnwalkableEndpoints = request.AllowUnwalkableEndpoints;
        MaxClimbHeight = request.MaxClimbHeight;
        ExtraFloodRange = request.ExtraFloodRange;

        HybridRoutePlan? routePlan;
        using (PathManager.EnterState(Context.Pathing.State))
            HybridRoutePlanner.TryPlan(this, out routePlan);

        RoutePlan = routePlan;
    }

    internal static HybridPathRequest? CreateFromFlowField(FlowFieldPathRequest request)
    {
        if (request?.HasValidEndpoints != true)
            return null;

        var hybridRequest = new HybridPathRequest(request);
        return hybridRequest.RoutePlan != null ? hybridRequest : null;
    }
}
