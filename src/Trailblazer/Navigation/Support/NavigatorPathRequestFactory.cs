using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

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
        bool allowUnwalkable,
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
                    allowUnwalkable,
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
                    allowUnwalkable,
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
                    allowUnwalkable,
                    VolumeTraversalMode.Open);
                return request != null;

            case GuidedPathMode.Swim:
                if (traversalMedium != TraversalMedium.Water)
                {
                    request = null;
                    return false;
                }

                request = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    VolumeTraversalMode.Water);
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
        bool allowUnwalkable,
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
                    allowUnwalkable,
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
                    allowUnwalkable,
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
                    allowUnwalkable,
                    VolumeTraversalMode.Open);
                if (volume == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        VolumeTraversalMode.Open,
                        fallbackChartPathMode,
                        allowUnwalkable,
                        allowTraversalTransitions,
                        aStarHeuristic,
                        aStarMaxClimbHeight,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    VolumeTraversalMode.Open,
                    volume,
                    fallbackChartPathMode,
                    allowUnwalkable,
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
                if (traversalMedium != TraversalMedium.Water)
                {
                    request = null;
                    return false;
                }

                var swim = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    VolumeTraversalMode.Water);
                if (swim == null)
                    return TryCreateVolumeExitHandoff(
                        origin,
                        targetPosition,
                        unitSize,
                        VolumeTraversalMode.Water,
                        fallbackChartPathMode,
                        allowUnwalkable,
                        allowTraversalTransitions,
                        aStarHeuristic,
                        aStarMaxClimbHeight,
                        flowFieldExtraFloodRange,
                        out request,
                        out handoff);

                if (TryCreateVolumeExitHandoffIfNeeded(
                    targetPosition,
                    VolumeTraversalMode.Water,
                    swim,
                    fallbackChartPathMode,
                    allowUnwalkable,
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
        VolumeTraversalMode traversalMode,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkable,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out IPathRequest request,
        out GuidedVolumeExitHandoff handoff)
    {
        request = null;
        handoff = null;

        if (!allowTraversalTransitions)
            return false;

        GuidedPathMode chartPathMode = fallbackChartPathMode == GuidedPathMode.FlowField
            ? GuidedPathMode.FlowField
            : GuidedPathMode.AStar;

        return GuidedVolumeExitPlanner.TryPlan(
            origin,
            targetPosition,
            unitSize,
            traversalMode,
            chartPathMode,
            allowUnwalkable,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out VolumePathRequest volumeRequest,
            out handoff)
            && TryAssignPlannedRequest(volumeRequest, out request);
    }

    private static bool TryCreateVolumeExitHandoffIfNeeded(
        Vector3d targetPosition,
        VolumeTraversalMode traversalMode,
        VolumePathRequest directRequest,
        GuidedPathMode fallbackChartPathMode,
        bool allowUnwalkable,
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
            || !ShouldAttemptVolumeExitHandoff(targetPosition, traversalMode))
        {
            return false;
        }

        if (directRequest.EndNode != null
            && directRequest.EndNode.WorldPosition == targetPosition)
        {
            return false;
        }

        return TryCreateVolumeExitHandoff(
            directRequest.Origin,
            targetPosition,
            directRequest.UnitSize,
            traversalMode,
            fallbackChartPathMode,
            allowUnwalkable,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            out request,
            out handoff);
    }

    private static bool ShouldAttemptVolumeExitHandoff(
        Vector3d targetPosition,
        VolumeTraversalMode traversalMode)
    {
        if (!GlobalGridManager.TryGetVoxel(targetPosition, out Voxel targetVoxel))
            return false;

        if (!targetVoxel.TryGetPartition(out PathPartition _))
            return false;

        return !VolumeTraversalRules.Matches(targetVoxel, traversalMode);
    }

    private static bool TryAssignPlannedRequest(
        VolumePathRequest volumeRequest,
        out IPathRequest request)
    {
        request = volumeRequest;
        return request != null;
    }
}
