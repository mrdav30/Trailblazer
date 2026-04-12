using FixedMathSharp;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Plans a bounded volume-first handoff into chart-backed traversal for navigators.
/// </summary>
internal static class GuidedVolumeExitPlanner
{
    /// <summary>
    /// Attempts to plan a volume exit path from the origin to a target position, followed by a chart-backed leg to the final destination.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="targetPosition"></param>
    /// <param name="unitSize"></param>
    /// <param name="medium"></param>
    /// <param name="chartPathMode"></param>
    /// <param name="allowUnwalkableEndpoints"></param>
    /// <param name="allowTraversalTransitions"></param>
    /// <param name="maxClimbHeight"></param>
    /// <param name="aStarHeuristic"></param>
    /// <param name="flowFieldExtraFloodRange"></param>
    /// <param name="request"></param>
    /// <param name="handoff"></param>
    /// <param name="totalPathCost"></param>
    /// <returns>True if a valid path was found; otherwise, false.</returns>
    public static bool TryPlan(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        TraversalMedium medium,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        out VolumePathRequest request,
        out GuidedVolumeExitHandoff handoff,
        out int totalPathCost)
    {
        request = null;
        handoff = null;
        totalPathCost = 0;

        if (chartPathMode != GuidedPathMode.AStar
            && chartPathMode != GuidedPathMode.FlowField)
        {
            return false;
        }

        TraversalTransition bestTransition = default;
        VolumePathRequest bestRequest = null;
        int bestTotalCost = int.MaxValue;

        if (!TryPlanWithTransitions(
                GetLocalDirectedTransitions(targetPosition, medium),
                origin,
                targetPosition,
                unitSize,
                chartPathMode,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                aStarHeuristic,
                flowFieldExtraFloodRange,
                ref bestTransition,
                ref bestRequest,
                ref bestTotalCost)
            && !TryPlanWithTransitions(
                TraversalTransitionQuery.GetDirectedTransitions(medium, TraversalMedium.Solid),
                origin,
                targetPosition,
                unitSize,
                chartPathMode,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                aStarHeuristic,
                flowFieldExtraFloodRange,
                ref bestTransition,
                ref bestRequest,
                ref bestTotalCost))
        {
            return false;
        }

        request = bestRequest;
        totalPathCost = bestTotalCost;
        handoff = CreateHandoff(
            bestTransition,
            targetPosition,
            chartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
            flowFieldExtraFloodRange);
        return true;
    }

    private static bool TryPlanWithTransitions(
        TraversalTransition[] transitions,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        ref TraversalTransition bestTransition,
        ref VolumePathRequest bestRequest,
        ref int bestTotalCost)
    {
        if (transitions == null || transitions.Length == 0)
            return false;

        bool foundPlan = false;

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            VolumePathRequest volumeRequest = VolumePathRequest.Create(
                origin,
                transition.Source.Position,
                unitSize,
                aStarHeuristic,
                allowUnwalkableEndpoints,
                transition.Source.Medium);
            if (volumeRequest == null)
                continue;

            int volumeCost = 0;
            if (!volumeRequest.HasZeroDisplacement)
            {
                VolumeSurveyResult volumeResult = VolumeSurveyor.Shared.FindPath(volumeRequest);
                if (!volumeResult.HasPath)
                    continue;

                volumeCost = volumeResult.Waypoints[^1].PathCost;
            }

            if (!TryGetChartLegCost(
                transition.Destination.Position,
                targetPosition,
                unitSize,
                chartPathMode,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                aStarHeuristic,
                flowFieldExtraFloodRange,
                out int chartCost))
            {
                continue;
            }

            int totalCost = volumeCost + transition.PathCostModifier + chartCost;
            if (totalCost >= bestTotalCost)
                continue;

            bestTotalCost = totalCost;
            bestTransition = transition;
            bestRequest = volumeRequest;
            foundPlan = true;
        }

        return foundPlan;
    }

    private static TraversalTransition[] GetLocalDirectedTransitions(
        Vector3d targetPosition,
        TraversalMedium medium)
    {
        if (!GlobalGridManager.TryGetVoxel(targetPosition, out Voxel targetVoxel))
            return Array.Empty<TraversalTransition>();

        return TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(
            targetVoxel.GridIndex,
            medium,
            TraversalMedium.Solid);
    }

    private static GuidedVolumeExitHandoff CreateHandoff(
        TraversalTransition transition,
        Vector3d targetPosition,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange)
    {
        return new GuidedVolumeExitHandoff
        {
            TransitionId = transition.Id,
            ChartOriginPosition = transition.Destination.Position,
            TargetPosition = targetPosition,
            ChartPathMode = chartPathMode,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = maxClimbHeight,
            AStarHeuristic = aStarHeuristic,
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange
        };
    }

    private static bool TryGetChartLegCost(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        out int chartCost)
    {
        chartCost = 0;

        switch (chartPathMode)
        {
            case GuidedPathMode.FlowField:
                FlowFieldPathRequest flowFieldRequest = FlowFieldPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    allowUnwalkableEndpoints,
                    allowTraversalTransitions);
                if (flowFieldRequest == null)
                    return false;

                flowFieldRequest.MaxClimbHeight = maxClimbHeight;
                flowFieldRequest.ExtraFloodRange = flowFieldExtraFloodRange;
                if (flowFieldRequest.HasZeroDisplacement)
                    return true;

                if (TryGetDirectFlowFieldCost(flowFieldRequest, out chartCost))
                    return true;

                if (!allowTraversalTransitions)
                    return false;

                return TryGetTransitionAwareChartCost(flowFieldRequest, out chartCost);

            case GuidedPathMode.AStar:
            default:
                AStarPathRequest aStarRequest = AStarPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkableEndpoints,
                    allowTraversalTransitions);
                if (aStarRequest == null)
                    return false;

                aStarRequest.MaxClimbHeight = maxClimbHeight;
                if (aStarRequest.HasZeroDisplacement)
                    return true;

                if (TryGetDirectAStarCost(aStarRequest, out chartCost))
                    return true;

                if (!allowTraversalTransitions)
                    return false;

                return TryGetTransitionAwareChartCost(aStarRequest, out chartCost);
        }
    }

    private static bool TryGetDirectAStarCost(
        AStarPathRequest request,
        out int chartCost)
    {
        chartCost = 0;

        AStarSurveyResult aStarResult = AStarSurveyor.Shared.FindPath(request);
        return aStarResult.HasPath
            && TryAssignChartCost(aStarResult.Waypoints[^1].PathCost, out chartCost);
    }

    private static bool TryGetDirectFlowFieldCost(
        FlowFieldPathRequest request,
        out int chartCost)
    {
        chartCost = 0;

        FlowFieldSurveyResult flowFieldResult = FlowFieldSurveyor.Shared.FindPath(request);
        return flowFieldResult.HasPath
            && flowFieldResult.Fields.TryGetValue(request.StartNode.GlobalIndex, out FlowField startField)
            && TryAssignChartCost(startField.PathCost, out chartCost);
    }

    private static bool TryGetTransitionAwareChartCost(
        AStarPathRequest request,
        out int chartCost)
    {
        return TryGetTransitionAwareChartCost(HybridPathRequest.CreateFromAStar(request), out chartCost);
    }

    private static bool TryGetTransitionAwareChartCost(
        FlowFieldPathRequest request,
        out int chartCost)
    {
        return TryGetTransitionAwareChartCost(HybridPathRequest.CreateFromFlowField(request), out chartCost);
    }

    internal static bool TryGetTransitionAwareChartCost(
        HybridPathRequest hybridRequest,
        out int chartCost)
    {
        chartCost = 0;

        HybridRoutePlan routePlan = hybridRequest?.RoutePlan;
        return routePlan != null
            && routePlan.DirectedTransitions.Length > 0
            && TryAssignChartCost(routePlan.TotalPathCost, out chartCost);
    }

    private static bool TryAssignChartCost(int cost, out int chartCost)
    {
        chartCost = cost;
        return true;
    }
}
