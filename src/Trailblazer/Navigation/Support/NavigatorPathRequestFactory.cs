using FixedMathSharp;
using GridForge.Grids;
using System.Diagnostics.CodeAnalysis;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Creates built-in path requests for navigators from host-facing guided travel commands.
/// </summary>
public static class NavigatorPathRequestFactory
{
    /// <summary>
    /// Attempts to create a pathfinding request using the specified parameters and pathfinding mode.
    /// </summary>
    /// <remarks>
    /// The parameters aStarHeuristic, flowFieldExtraFloodRange, and traversalMedium are only used for specific path modes. 
    /// The method returns false if the combination of parameters does not support the requested pathfinding mode.
    /// </remarks>
    /// <param name="origin">The starting position for the pathfinding request.</param>
    /// <param name="targetPosition">The target position the path should reach.</param>
    /// <param name="unitSize">The size of the unit for which the path is being calculated. Must be a positive value.</param>
    /// <param name="pathMode">The pathfinding algorithm or mode to use when creating the request.</param>
    /// <param name="allowUnwalkableEndpoints">true to allow the origin or target position to be unwalkable; otherwise, false.</param>
    /// <param name="allowTraversalTransitions">true to allow traversal transitions (such as moving between different terrain types); otherwise, false.</param>
    /// <param name="maxClimbHeight">The maximum height the unit can climb during pathfinding. Must be non-negative.</param>
    /// <param name="aStarHeuristic">The heuristic method to use for A* pathfinding. Only relevant when pathMode is AStar or Volume.</param>
    /// <param name="flowFieldExtraFloodRange">The additional range, in units, to flood when using flow field pathfinding. Only relevant when pathMode is
    /// FlowField.</param>
    /// <param name="traversalMedium">The traversal medium to use for pathfinding. Only relevant when pathMode is Volume.</param>
    /// <param name="request">When this method returns, contains the created pathfinding request if successful; otherwise, null.</param>
    /// <returns>true if the pathfinding request was successfully created; otherwise, false.</returns>
    public static bool TryCreate(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode pathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        TraversalMedium traversalMedium,
        [NotNullWhen(true)] out IPathRequest? request)
    {
        switch (pathMode)
        {
            case GuidedPathMode.AStar:
                return TryCreateAStarRequest(
                    origin, targetPosition, unitSize,
                    aStarHeuristic, allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, out request);

            case GuidedPathMode.FlowField:
                return TryCreateFlowFieldRequest(
                    origin, targetPosition, unitSize,
                    allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, flowFieldExtraFloodRange, out request);

            case GuidedPathMode.Aerial:
                request = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndpoints,
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
                    allowUnwalkableEndpoints,
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
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        TraversalMedium traversalMedium,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        handoff = null;

        switch (pathMode)
        {
            case GuidedPathMode.AStar:
                return TryCreateAStarRequest(
                    origin, targetPosition, unitSize,
                    aStarHeuristic, allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, out request);

            case GuidedPathMode.FlowField:
                return TryCreateFlowFieldRequest(
                    origin, targetPosition, unitSize,
                    allowUnwalkableEndpoints, allowTraversalTransitions,
                    maxClimbHeight, flowFieldExtraFloodRange, out request);

            case GuidedPathMode.Aerial:
                VolumePathRequest? volume = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndpoints,
                    TraversalMedium.Gas);
                if (volume == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        TraversalMedium.Gas,
                        fallbackChartPathMode,
                        allowUnwalkableEndpoints,
                        allowTraversalTransitions,
                        maxClimbHeight,
                        aStarHeuristic,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    TraversalMedium.Gas,
                    volume,
                    fallbackChartPathMode,
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

            case GuidedPathMode.Swim:
                if (traversalMedium != TraversalMedium.Liquid)
                {
                    request = null;
                    return false;
                }

                VolumePathRequest? swim = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndpoints,
                    TraversalMedium.Liquid);
                if (swim == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        TraversalMedium.Liquid,
                        fallbackChartPathMode,
                        allowUnwalkableEndpoints,
                        allowTraversalTransitions,
                        maxClimbHeight,
                        aStarHeuristic,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    TraversalMedium.Liquid,
                    swim,
                    fallbackChartPathMode,
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
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out IPathRequest? request,
        out GuidedVolumeExitHandoff? handoff)
    {
        return TryCreateVolumeExitHandoff(
            origin,
            targetPosition,
            unitSize,
            medium,
            fallbackChartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
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

        GuidedPathMode chartPathMode = fallbackChartPathMode == GuidedPathMode.FlowField
            ? GuidedPathMode.FlowField
            : GuidedPathMode.AStar;

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
        GuidedPathMode fallbackChartPathMode,
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
                fallbackChartPathMode,
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
            fallbackChartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
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
        GuidedPathMode fallbackChartPathMode,
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
            fallbackChartPathMode,
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
