using System;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Plans a bounded volume-first handoff into chart-backed traversal for navigators.
/// </summary>
internal static class GuidedVolumeExitPlanner
{
    public static bool TryPlan(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        VolumeTraversalMode traversalMode,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
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

        TraversalTransition[] localTransitions = GetLocalDirectedTransitions(targetPosition);
        if (TryPlanWithTransitions(
            localTransitions,
            origin,
            targetPosition,
            unitSize,
            traversalMode,
            chartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange,
            ref bestTransition,
            ref bestRequest,
            ref bestTotalCost))
        {
            request = bestRequest;
            totalPathCost = bestTotalCost;
            handoff = CreateHandoff(
                bestTransition,
                targetPosition,
                chartPathMode,
                allowUnwalkableEndNode,
                allowTraversalTransitions,
                aStarHeuristic,
                aStarMaxClimbHeight,
                flowFieldExtraFloodRange);
            return true;
        }

        if (!TryPlanWithTransitions(
            TraversalTransitionQuery.GetDirectedTransitions(),
            origin,
            targetPosition,
            unitSize,
            traversalMode,
            chartPathMode,
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
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
            allowUnwalkableEndNode,
            allowTraversalTransitions,
            aStarHeuristic,
            aStarMaxClimbHeight,
            flowFieldExtraFloodRange);
        return true;
    }

    private static bool TryPlanWithTransitions(
        TraversalTransition[] transitions,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        VolumeTraversalMode traversalMode,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
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
            if (transition.Source.Kind != TraversalTransitionAnchorKind.Volume
                || transition.Destination.Kind != TraversalTransitionAnchorKind.Chart
                || transition.Source.VolumeMode != traversalMode)
            {
                continue;
            }

            VolumePathRequest volumeRequest = VolumePathRequest.Create(
                origin,
                transition.Source.Position,
                unitSize,
                aStarHeuristic,
                allowUnwalkableEndNode,
                traversalMode);
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
                allowUnwalkableEndNode,
                allowTraversalTransitions,
                aStarHeuristic,
                aStarMaxClimbHeight,
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

    private static TraversalTransition[] GetLocalDirectedTransitions(Vector3d targetPosition)
    {
        if (!GlobalGridManager.TryGetVoxel(targetPosition, out Voxel targetVoxel))
            return Array.Empty<TraversalTransition>();

        return TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(targetVoxel.GridIndex);
    }

    private static GuidedVolumeExitHandoff CreateHandoff(
        TraversalTransition transition,
        Vector3d targetPosition,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange)
    {
        return new GuidedVolumeExitHandoff
        {
            TransitionId = transition.Id,
            ChartOriginPosition = transition.Destination.Position,
            TargetPosition = targetPosition,
            ChartPathMode = chartPathMode,
            AllowUnwalkableEndNode = allowUnwalkableEndNode,
            AllowTraversalTransitions = allowTraversalTransitions,
            AStarHeuristic = aStarHeuristic,
            AStarMaxClimbHeight = aStarMaxClimbHeight,
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange
        };
    }

    private static bool TryGetChartLegCost(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode chartPathMode,
        bool allowUnwalkableEndNode,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
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
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (flowFieldRequest == null)
                    return false;

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
                    allowUnwalkableEndNode,
                    allowTraversalTransitions);
                if (aStarRequest == null)
                    return false;

                aStarRequest.MaxClimbHeight = aStarMaxClimbHeight;
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
        chartCost = 0;

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromAStar(request);
        return hybridRequest?.RoutePlan != null
            && hybridRequest.RoutePlan.DirectedTransitions.Length > 0
            && TryAssignChartCost(hybridRequest.RoutePlan.TotalPathCost, out chartCost);
    }

    private static bool TryGetTransitionAwareChartCost(
        FlowFieldPathRequest request,
        out int chartCost)
    {
        chartCost = 0;

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromFlowField(request);
        return hybridRequest?.RoutePlan != null
            && hybridRequest.RoutePlan.DirectedTransitions.Length > 0
            && TryAssignChartCost(hybridRequest.RoutePlan.TotalPathCost, out chartCost);
    }

    private static bool TryAssignChartCost(int cost, out int chartCost)
    {
        chartCost = cost;
        return true;
    }
}
