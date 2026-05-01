using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Diagnostics.CodeAnalysis;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Creates built-in path requests for navigators from host-facing guided travel commands.
/// </summary>
public static class NavigatorPathRequestFactory
{
    internal static bool TryCreate(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        SolidPathAlgorithm pathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        TraversalMedium traversalMedium,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
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
                origin,
                targetPosition,
                unitSize,
                aStarHeuristic,
                allowUnwalkableEndpoints,
                traversalMedium);
            if (volume == null)
                return TryCreateVolumeExitHandoff(
                    origin,
                    targetPosition,
                    unitSize,
                    traversalMedium,
                    pathMode,
                    allowUnwalkableEndpoints,
                    allowTraversalTransitions,
                    maxClimbHeight,
                    aStarHeuristic,
                    flowFieldExtraFloodRange,
                    out request,
                    out handoff,
                    out _);

            if (TryCreateVolumeExitHandoffIfNeeded(
                targetPosition,
                traversalMedium,
                volume,
                pathMode,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                aStarHeuristic,
                flowFieldExtraFloodRange,
                out request,
                out handoff))
            {
                return true;
            }

            request = volume;
            return true;
        }

        switch (pathMode)
        {
            case SolidPathAlgorithm.AStar:
                return TryCreateAStarRequest(
                    origin, targetPosition, unitSize,
                    aStarHeuristic, allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, out request);

            case SolidPathAlgorithm.FlowField:
                return TryCreateFlowFieldRequest(
                    origin, targetPosition, unitSize,
                    allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, flowFieldExtraFloodRange, out request);

            default:
                request = null;
                return false;
        }
    }

    private static bool TryCreateVolumeExitHandoff(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        TraversalMedium medium,
        SolidPathAlgorithm chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
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
            origin,
            targetPosition,
            unitSize,
            medium,
            chartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
            flowFieldExtraFloodRange,
            out VolumePathRequest? volumeRequest,
            out handoff,
            out totalPathCost)
            && volumeRequest != null
            && (request = volumeRequest) != null;
    }

    private static bool TryCreateVolumeExitHandoffIfNeeded(
        Vector3d targetPosition,
        TraversalMedium medium,
        VolumePathRequest directRequest,
        SolidPathAlgorithm chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        request = null;
        handoff = null;

        if (directRequest == null
            || !allowTraversalTransitions
            || !TryGetChartBackedTargetState(
                targetPosition,
                medium,
                out bool targetRequiresConstrainedExitHandoff))
        {
            return false;
        }

        if (!targetRequiresConstrainedExitHandoff
            && !TryCreateGasLandingHandoff(
                directRequest,
                targetPosition,
                medium,
                chartPathMode,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                aStarHeuristic,
                flowFieldExtraFloodRange,
                out request,
                out handoff))
        {
            return false;
        }

        if (request != null)
            return true;

        return TryCreateVolumeExitHandoff(
            directRequest.Origin,
            targetPosition,
            directRequest.UnitSize,
            medium,
            chartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
            flowFieldExtraFloodRange,
            out request,
            out handoff,
            out _);
    }

    private static bool TryGetChartBackedTargetState(
        Vector3d targetPosition,
        TraversalMedium medium,
        out bool targetRequiresConstrainedExitHandoff)
    {
        targetRequiresConstrainedExitHandoff = false;

        if (!TrailblazerWorldManager.TryGetVoxel(targetPosition, out Voxel? targetVoxel)
            || targetVoxel == null)
            return false;

        if (targetVoxel.TryGetPartition(out SolidChartPartition? _) != true)
            return false;

        targetRequiresConstrainedExitHandoff = !VolumeMediumRules.Matches(targetVoxel, medium);
        return true;
    }

    private static bool TryCreateGasLandingHandoff(
        VolumePathRequest directRequest,
        Vector3d targetPosition,
        TraversalMedium medium,
        SolidPathAlgorithm chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        request = null;
        handoff = null;

        if (medium != TraversalMedium.Gas)
            return false;

        if (!TryCreateVolumeExitHandoff(
            directRequest.Origin,
            targetPosition,
            directRequest.UnitSize,
            medium,
            chartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
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

    private static bool TryCreateAStarRequest(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        HeuristicMethod heuristic,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        [NotNullWhen(true)] out IPathRequest? request)
    {
        AStarPathRequest? aStar = AStarPathRequest.Create(
            origin,
            targetPosition,
            unitSize,
            heuristic,
            allowUnwalkableEndpoints,
            allowTraversalTransitions);
        if (aStar == null)
        {
            request = null;
            return false;
        }

        aStar.MaxClimbHeight = maxClimbHeight;
        request = aStar;
        return true;
    }

    private static bool TryCreateFlowFieldRequest(
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

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        return result.HasPath && result.Waypoints != null && result.Waypoints.Length > 0
            ? result.Waypoints[^1].PathCost
            : int.MaxValue;
    }
}
