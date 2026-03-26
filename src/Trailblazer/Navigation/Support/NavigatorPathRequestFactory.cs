using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

// TODO: clean this up, remove code duplication

/// <summary>
/// Creates built-in path requests for navigators from host-facing guided travel commands.
/// </summary>
public static class NavigatorPathRequestFactory
{
    public static bool TryCreate(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode pathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        TraversalMedium traversalMedium,
        out IPathRequest request)
    {
        switch (pathMode)
        {
            case GuidedPathMode.AStar:
                var aStar = AStarPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (aStar == null)
                {
                    request = null;
                    return false;
                }

                aStar.MaxClimbHeight = aStarMaxClimbHeight;
                request = aStar;
                return true;

            case GuidedPathMode.FlowField:
                var flowField = FlowFieldPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (flowField == null)
                {
                    request = null;
                    return false;
                }

                flowField.ExtraFloodRange = flowFieldExtraFloodRange;
                request = flowField;
                return true;

            case GuidedPathMode.Aerial:
                request = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    TraversalMedium.Gas);
                return request != null;

            case GuidedPathMode.Swim:
                if (traversalMedium != TraversalMedium.Liquid)
                {
                    request = null;
                    return false;
                }

                request = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    TraversalMedium.Liquid);
                return request != null;

            default:
                request = null;
                return false;
        }
    }

    internal static bool TryCreate(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode pathMode,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        TraversalMedium traversalMedium,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff)
    {
        handoff = null;

        switch (pathMode)
        {
            case GuidedPathMode.AStar:
                var aStar = AStarPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (aStar == null)
                {
                    request = null;
                    return false;
                }

                aStar.MaxClimbHeight = aStarMaxClimbHeight;
                request = aStar;
                return true;

            case GuidedPathMode.FlowField:
                var flowField = FlowFieldPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (flowField == null)
                {
                    request = null;
                    return false;
                }

                flowField.ExtraFloodRange = flowFieldExtraFloodRange;
                request = flowField;
                return true;

            case GuidedPathMode.Aerial:
                var volume = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    TraversalMedium.Gas);
                if (volume == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        TraversalMedium.Gas,
                        fallbackChartPathMode,
                        allowUnwalkableEndNode,
                        allowTraversalTransitions,
                        aStarHeuristic,
                        aStarMaxClimbHeight,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    TraversalMedium.Gas,
                    volume,
                    fallbackChartPathMode,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions,
                    aStarHeuristic,
                    aStarMaxClimbHeight,
                    flowFieldExtraFloodRange,
                    out request,
                    out handoff))
                {
                    return true;
                }

                request = volume;
                return true;

            case GuidedPathMode.Swim:
                if (traversalMedium != TraversalMedium.Liquid)
                {
                    request = null;
                    return false;
                }

                var swim = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndNode,
                    TraversalMedium.Liquid);
                if (swim == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        TraversalMedium.Liquid,
                        fallbackChartPathMode,
                        allowUnwalkableEndNode,
                        allowTraversalTransitions,
                        aStarHeuristic,
                        aStarMaxClimbHeight,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    TraversalMedium.Liquid,
                    swim,
                    fallbackChartPathMode,
                    allowUnwalkableEndNode,
                    allowTraversalTransitions,
                    aStarHeuristic,
                    aStarMaxClimbHeight,
                    flowFieldExtraFloodRange,
                    out request,
                    out handoff))
                {
                    return true;
                }

                request = swim;
                return true;

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
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff)
    {
        return TryCreateVolumeExitHandoff(
            origin,
            targetPosition,
            unitSize,
            medium,
            fallbackChartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out request,
            out handoff,
            out _);
    }

    private static bool TryCreateVolumeExitHandoff(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        TraversalMedium medium,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff,
        out int totalPathCost)
    {
        request = null;
        handoff = null;
        totalPathCost = 0;

        if (!allowTraversalTransitions)
            return false;

        GuidedPathMode chartPathMode = fallbackChartPathMode == GuidedPathMode.FlowField
            ? GuidedPathMode.FlowField
            : GuidedPathMode.AStar;

        return GuidedVolumeExitPlanner.TryPlan(
            origin,
            targetPosition,
            unitSize,
            medium,
            chartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out VolumePathRequest volumeRequest,
            out handoff,
            out totalPathCost)
            && TryAssignPlannedRequest(volumeRequest, out request);
    }

    private static bool TryCreateVolumeExitHandoffIfNeeded(
        Vector3d targetPosition,
        TraversalMedium medium,
        VolumePathRequest directRequest,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff)
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
                fallbackChartPathMode,
                allowUnwalkableEndNode,
                allowTraversalTransitions,
                aStarHeuristic,
                aStarMaxClimbHeight,
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
            fallbackChartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out request,
            out handoff);
    }

    private static bool TryGetChartBackedTargetState(
        Vector3d targetPosition,
        TraversalMedium medium,
        out bool targetRequiresConstrainedExitHandoff)
    {
        targetRequiresConstrainedExitHandoff = false;

        if (!GlobalGridManager.TryGetVoxel(targetPosition, out Voxel targetVoxel))
            return false;

        if (!targetVoxel.TryGetPartition(out SolidChartPartition _))
            return false;

        targetRequiresConstrainedExitHandoff = !VolumeMediumRules.Matches(targetVoxel, medium);
        return true;
    }

    private static bool TryCreateGasLandingHandoff(
        VolumePathRequest directRequest,
        Vector3d targetPosition,
        TraversalMedium medium,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff)
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
            fallbackChartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out IPathRequest plannedRequest,
            out GuidedVolumeExitHandoff plannedHandoff,
            out int handoffPathCost))
        {
            return false;
        }

        if (directRequest.EndNode == null
            || directRequest.EndNode.WorldPosition != targetPosition)
        {
            request = plannedRequest;
            handoff = plannedHandoff;
            return true;
        }

        if (!TryGetDirectVolumePathCost(directRequest, out int directPathCost)
            || handoffPathCost < directPathCost)
        {
            request = plannedRequest;
            handoff = plannedHandoff;
            return true;
        }

        return false;
    }

    private static bool TryGetDirectVolumePathCost(
        VolumePathRequest request,
        out int pathCost)
    {
        pathCost = 0;

        if (request == null)
            return false;

        if (request.HasZeroDisplacement)
            return true;

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        return result.HasPath
            && result.Waypoints.Length > 0
            && TryAssignPathCost(result.Waypoints[^1].PathCost, out pathCost);
    }

    private static bool TryAssignPathCost(int cost, out int pathCost)
    {
        pathCost = cost;
        return true;
    }

    private static bool TryAssignPlannedRequest(
        VolumePathRequest volumeRequest,
        out IPathRequest request)
    {
        request = volumeRequest;
        return request != null;
    }
}
