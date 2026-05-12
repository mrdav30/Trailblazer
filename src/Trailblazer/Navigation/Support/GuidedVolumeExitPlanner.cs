using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Diagnostics.CodeAnalysis;
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
    /// <param name="context">The world context that owns the request and transition registry.</param>
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
        TrailblazerWorldContext context,
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
        [NotNullWhen(true)] out VolumePathRequest? request,
        out GuidedVolumeExitHandoff? handoff,
        out int totalPathCost)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        request = null;
        handoff = null;
        totalPathCost = 0;

        if (chartPathMode != SolidPathAlgorithm.AStar
            && chartPathMode != SolidPathAlgorithm.FlowField)
        {
            return false;
        }

        TraversalTransition bestTransition = default;
        VolumePathRequest? bestRequest = null;
        int bestTotalCost = int.MaxValue;

        if (!TryPlanWithTransitions(
                GetLocalDirectedTransitions(context, targetPosition, medium),
                context,
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
                context.Transitions.GetDirectedTransitions(medium, TraversalMedium.Solid),
                context,
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

        if (bestRequest == null)
            return false;

        request = bestRequest;
        totalPathCost = bestTotalCost;
        handoff = CreateHandoff(
            bestTransition,
            context,
            targetPosition,
            chartPathMode,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            aStarHeuristic,
            flowFieldExtraFloodRange);
        return true;
    }

    public static bool TryPlan(
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
        [NotNullWhen(true)] out VolumePathRequest? request,
        out GuidedVolumeExitHandoff? handoff,
        out int totalPathCost)
    {
        return TryPlan(
            PathRequestContextResolver.DefaultContext,
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
            out request,
            out handoff,
            out totalPathCost);
    }

    private static bool TryPlanWithTransitions(
        TraversalTransition[] transitions,
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        SolidPathAlgorithm chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange,
        ref TraversalTransition bestTransition,
        ref VolumePathRequest? bestRequest,
        ref int bestTotalCost)
    {
        if (transitions == null || transitions.Length == 0)
            return false;

        bool foundPlan = false;

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            VolumePathRequest? volumeRequest = VolumePathRequest.Create(
                context,
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
                VolumeSurveyResult volumeResult = context.Pathing.State.GuideState.VolumeSurveyor.FindPath(volumeRequest);
                if (!volumeResult.HasPath)
                    continue;

                volumeCost = volumeResult.Waypoints![^1].PathCost;
            }

            if (!TryGetChartLegCost(
                context,
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
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        TraversalMedium medium)
    {
        if (!context.World.TryGetVoxel(targetPosition, out Voxel? targetVoxel)
            || targetVoxel == null)
            return Array.Empty<TraversalTransition>();

        return context.Transitions.GetDirectedTransitionsToDestinationGrid(
            targetVoxel.GridIndex,
            medium,
            TraversalMedium.Solid);
    }

    private static GuidedVolumeExitHandoff CreateHandoff(
        TraversalTransition transition,
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        SolidPathAlgorithm chartPathMode,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod aStarHeuristic,
        int flowFieldExtraFloodRange)
    {
        return new GuidedVolumeExitHandoff
        {
            TransitionId = transition.Id,
            Context = context,
            ChartOriginPosition = transition.Destination.Position,
            TargetPosition = targetPosition,
            ChartPathMode = chartPathMode,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = maxClimbHeight,
            AStarHeuristic = aStarHeuristic,
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange,
            IsRequestingClimb = transition.PreserveClimbIntentOnFollowup
        };
    }

    private static bool TryGetChartLegCost(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        SolidPathAlgorithm chartPathMode,
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
            case SolidPathAlgorithm.FlowField:
                FlowFieldPathRequest? flowFieldRequest = FlowFieldPathRequest.Create(
                    context,
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

            case SolidPathAlgorithm.AStar:
            default:
                AStarPathRequest? aStarRequest = AStarPathRequest.Create(
                    context,
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

        AStarSurveyResult aStarResult = request.Context.Pathing.State.GuideState.AStarSurveyor.FindPath(request);
        return aStarResult.HasPath
            && TryAssignChartCost(aStarResult.Waypoints[^1].PathCost, out chartCost);
    }

    private static bool TryGetDirectFlowFieldCost(
        FlowFieldPathRequest request,
        out int chartCost)
    {
        chartCost = 0;

        FlowFieldSurveyResult flowFieldResult = request.Context.Pathing.State.GuideState.FlowFieldSurveyor.FindPath(request);
        return flowFieldResult.HasPath
            && flowFieldResult.Fields != null
            && request.StartNode != null
            && flowFieldResult.Fields.TryGetValue(request.StartNode.WorldIndex, out FlowField startField)
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
        HybridPathRequest? hybridRequest,
        out int chartCost)
    {
        chartCost = 0;

        HybridRoutePlan? routePlan = hybridRequest?.RoutePlan;
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
