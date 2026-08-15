//=======================================================================
// GuidedVolumeExitPlanner.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using FixedMathSharp;
using GridForge.Grids;
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
    /// <param name="allowUnwalkableEndpoints"></param>
    /// <param name="allowTraversalTransitions"></param>
    /// <param name="maxClimbHeight"></param>
    /// <param name="volumeHeuristic"></param>
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
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod volumeHeuristic,
        int flowFieldExtraFloodRange,
        [NotNullWhen(true)] out VolumePathRequest? request,
        out GuidedVolumeExitHandoff? handoff,
        out int totalPathCost)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        request = null;
        handoff = null;
        totalPathCost = 0;

        TraversalTransition bestTransition = default;
        VolumePathRequest? bestRequest = null;
        int bestTotalCost = int.MaxValue;

        if (!TryPlanWithTransitions(
                GetLocalDirectedTransitions(context, targetPosition, medium),
                context,
                origin,
                targetPosition,
                unitSize,
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                volumeHeuristic,
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
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
                volumeHeuristic,
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
            targetPosition,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            maxClimbHeight,
            flowFieldExtraFloodRange);
        return true;
    }


    private static bool TryPlanWithTransitions(
        TraversalTransition[] transitions,
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        HeuristicMethod volumeHeuristic,
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
                volumeHeuristic,
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
                allowUnwalkableEndpoints,
                allowTraversalTransitions,
                maxClimbHeight,
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
        Vector3d targetPosition,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        int flowFieldExtraFloodRange)
    {
        return new GuidedVolumeExitHandoff
        {
            TransitionId = transition.Id,
            ChartOriginPosition = transition.Destination.Position,
            TargetPosition = targetPosition,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = maxClimbHeight,
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange,
            IsRequestingClimb = transition.PreserveClimbIntentOnFollowup
        };
    }

    private static bool TryGetChartLegCost(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        int flowFieldExtraFloodRange,
        out int chartCost)
    {
        chartCost = 0;

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

        HybridRoutePlan? routePlan = HybridPathRequest.CreateFromFlowField(flowFieldRequest)?.RoutePlan;
        return routePlan != null
            && routePlan.DirectedTransitions.Length > 0
            && TryAssignChartCost(routePlan.TotalPathCost, out chartCost);
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

    private static bool TryAssignChartCost(int cost, out int chartCost)
    {
        chartCost = cost;
        return true;
    }
}
