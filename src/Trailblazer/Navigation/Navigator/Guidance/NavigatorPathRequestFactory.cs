//=======================================================================
// NavigatorPathRequestFactory.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics.CodeAnalysis;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Creates built-in path requests for navigators from host-facing guided travel commands.
/// </summary>
public static class NavigatorPathRequestFactory
{
    internal static bool TryCreate(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        TraversalMedium traversalMedium,
        HeuristicMethod volumeHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        handoff = null;

        // For gas and liquid traversal, we only support volume path requests,
        // so we bypass the path mode switch and go straight to trying to create a volume request.
        // If that fails and traversal transitions are allowed,
        // we attempt to create a guided volume exit handoff request which will plan a
        // volume path to an exit if needed before transitioning to a chart-based path for
        // the remainder of the journey.
        if (traversalMedium == TraversalMedium.Gas || traversalMedium == TraversalMedium.Liquid)
        {
            VolumePathRequest? volume = VolumePathRequest.Create(
                context,
                origin,
                targetPosition,
                unitSize,
                volumeHeuristic,
                allowUnwalkableEndpoints,
                traversalMedium);
            if (volume == null)
                return TryCreateVolumeExitHandoff(
                    context,
                    origin,
                    targetPosition,
                    unitSize,
                    traversalMedium,
                    allowUnwalkableEndpoints,
                    allowTraversalTransitions,
                    maxClimbHeight,
                    volumeHeuristic,
                    flowFieldExtraFloodRange,
                    out request,
                    out handoff,
                    out _);

            if (TryCreateVolumeExitHandoffIfNeeded(
                context,
                targetPosition,
                traversalMedium,
                volume,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                volumeHeuristic,
                flowFieldExtraFloodRange,
                out request,
                out handoff))
            {
                return true;
            }

            request = volume;
            return true;
        }

        return TryCreateFlowFieldRequest(
            context,
            origin, targetPosition, unitSize,
            allowUnwalkableEndpoints, allowTraversalTransitions,
            maxClimbHeight, flowFieldExtraFloodRange, out request);
    }

    private static bool TryCreateVolumeExitHandoff(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        TraversalMedium medium,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod volumeHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff,
        out int totalPathCost)
    {
        request = null;
        handoff = null;
        totalPathCost = 0;

        if (!allowTraversalTransitions)
            return false;

        return GuidedVolumeExitPlanner.TryPlan(
            context,
            origin,
            targetPosition,
            unitSize,
            medium,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            volumeHeuristic,
            flowFieldExtraFloodRange,
            out VolumePathRequest? volumeRequest,
            out handoff,
            out totalPathCost)
            && volumeRequest != null
            && (request = volumeRequest) != null;
    }

    private static bool TryCreateVolumeExitHandoffIfNeeded(
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        TraversalMedium medium,
        VolumePathRequest directRequest,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod volumeHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        request = null;
        handoff = null;

        if (directRequest == null
            || !allowTraversalTransitions
            || !TryGetChartBackedTargetState(
                context,
                targetPosition,
                medium,
                out bool targetRequiresConstrainedExitHandoff))
        {
            return false;
        }

        if (!targetRequiresConstrainedExitHandoff
            && !TryCreateGasLandingHandoff(
                directRequest,
                context,
                targetPosition,
                medium,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                volumeHeuristic,
                flowFieldExtraFloodRange,
                out request,
                out handoff))
        {
            return false;
        }

        if (request != null)
            return true;

        return TryCreateVolumeExitHandoff(
            context,
            directRequest.Origin,
            targetPosition,
            directRequest.UnitSize,
            medium,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            volumeHeuristic,
            flowFieldExtraFloodRange,
            out request,
            out handoff,
            out _);
    }

    private static bool TryGetChartBackedTargetState(
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        TraversalMedium medium,
        out bool targetRequiresConstrainedExitHandoff)
    {
        targetRequiresConstrainedExitHandoff = false;

        if (!context.World.TryGetVoxel(targetPosition, out Voxel? targetVoxel)
            || targetVoxel == null)
            return false;

        if (targetVoxel.TryGetPartition(out SolidChartPartition? _) != true)
            return false;

        targetRequiresConstrainedExitHandoff = !VolumeMediumRules.Matches(context.Pathing.State, targetVoxel, medium);
        return true;
    }

    private static bool TryCreateGasLandingHandoff(
        VolumePathRequest directRequest,
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        TraversalMedium medium,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod volumeHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        request = null;
        handoff = null;

        if (medium != TraversalMedium.Gas)
            return false;

        if (!TryCreateVolumeExitHandoff(
            context,
            directRequest.Origin,
            targetPosition,
            directRequest.UnitSize,
            medium,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            volumeHeuristic,
            flowFieldExtraFloodRange,
            out IPathRequest? plannedRequest,
            out GuidedVolumeExitHandoff? plannedHandoff,
            out int handoffPathCost))
        {
            return false;
        }

        if (plannedRequest == null || plannedHandoff == null || directRequest.EndNode == null)
            return false;

        if (directRequest.EndNode.WorldPosition != targetPosition)
        {
            request = plannedRequest;
            handoff = plannedHandoff;
            return true;
        }

        if (handoffPathCost < GetDirectVolumePathCost(directRequest))
        {
            request = plannedRequest;
            handoff = plannedHandoff;
            return true;
        }

        return false;
    }

    private static bool TryCreateFlowFieldRequest(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request)
    {
        FlowFieldPathRequest? flowField = FlowFieldPathRequest.Create(
            context,
            origin,
            targetPosition,
            unitSize,
            allowUnwalkableEndpoints,
            allowTraversalTransitions);
        if (flowField == null)
        {
            request = null;
            return false;
        }

        flowField.MaxClimbHeight = maxClimbHeight;
        flowField.ExtraFloodRange = flowFieldExtraFloodRange;
        request = flowField;
        return true;
    }

    private static int GetDirectVolumePathCost(VolumePathRequest request)
    {
        if (request.HasZeroDisplacement)
            return 0;

        VolumeSurveyResult result = request.Context.Pathing.State.GuideState.VolumeSurveyor.FindPath(request);
        return result.HasPath && result.Waypoints != null && result.Waypoints.Length > 0
            ? result.Waypoints[^1].PathCost
            : int.MaxValue;
    }
}
