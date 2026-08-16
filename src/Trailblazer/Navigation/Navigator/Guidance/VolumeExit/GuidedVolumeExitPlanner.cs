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

/// <summary>Plans a bounded volume-first handoff into graph-backed Flow traversal.</summary>
internal static class GuidedVolumeExitPlanner
{
    public static bool TryPlan(
        TrailblazerWorldContext context,
        PathQuery surfaceIntent,
        TraversalMedium medium,
        HeuristicMethod volumeHeuristic,
        [NotNullWhen(true)] out VolumePathRequest? request,
        out GuidedVolumeExitHandoff? handoff,
        out Fixed64 totalPathCost)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        request = null;
        handoff = null;
        totalPathCost = Fixed64.Zero;
        if (surfaceIntent.Algorithm != PathAlgorithm.FlowField
            || !surfaceIntent.AllowTransitions
            || surfaceIntent.Traversal.StartDomain != TraversalDomain.Surface
            || surfaceIntent.Traversal.TargetDomain != TraversalDomain.Surface
            || surfaceIntent.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return false;
        }

        TraversalTransition bestTransition = default;
        VolumePathRequest? bestRequest = null;
        Fixed64 bestTotalCost = Fixed64.MaxValue;
        if (!TryPlanWithTransitions(
                GetLocalDirectedTransitions(context, surfaceIntent.End.Position, medium),
                context,
                surfaceIntent,
                medium,
                volumeHeuristic,
                ref bestTransition,
                ref bestRequest,
                ref bestTotalCost)
            && !TryPlanWithTransitions(
                context.Transitions.GetDirectedTransitions(medium, TraversalMedium.Solid),
                context,
                surfaceIntent,
                medium,
                volumeHeuristic,
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
        handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = bestTransition.Id,
            FollowupQuery = surfaceIntent,
            IsRequestingClimb = bestTransition.PreserveClimbIntentOnFollowup
        };
        return true;
    }

    private static bool TryPlanWithTransitions(
        TraversalTransition[] transitions,
        TrailblazerWorldContext context,
        PathQuery surfaceIntent,
        TraversalMedium medium,
        HeuristicMethod volumeHeuristic,
        ref TraversalTransition bestTransition,
        ref VolumePathRequest? bestRequest,
        ref Fixed64 bestTotalCost)
    {
        if (transitions == null || transitions.Length == 0)
            return false;

        bool foundPlan = false;
        Fixed64 unitSize = surfaceIntent.Agent.Shape.Radius + surfaceIntent.Agent.Shape.Radius;
        bool allowUnwalkableEndpoints =
            surfaceIntent.Start.Resolution != EndpointResolutionPolicy.Strict
            || surfaceIntent.End.Resolution != EndpointResolutionPolicy.Strict;
        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            if (transition.Source.Medium != medium)
                continue;

            VolumePathRequest? volumeRequest = VolumePathRequest.Create(
                context,
                surfaceIntent.Start.Position,
                transition.Source.Position,
                unitSize,
                volumeHeuristic,
                allowUnwalkableEndpoints,
                medium);
            if (volumeRequest == null)
                continue;

            Fixed64 volumeCost = Fixed64.Zero;
            if (!volumeRequest.HasZeroDisplacement)
            {
                VolumeSurveyResult volumeResult = context.Pathing.State.GuideState.VolumeSurveyor.FindPath(volumeRequest);
                if (!volumeResult.HasPath)
                    continue;

                volumeCost = (Fixed64)volumeResult.Waypoints![^1].PathCost;
            }

            if (!TryGetChartLegCost(
                context,
                surfaceIntent,
                transition.Destination.Position,
                out Fixed64 chartCost))
            {
                continue;
            }

            Fixed64 totalCost = volumeCost + (Fixed64)transition.PathCostModifier + chartCost;
            if (totalCost >= bestTotalCost)
                continue;

            bestTransition = transition;
            bestRequest = volumeRequest;
            bestTotalCost = totalCost;
            foundPlan = true;
        }

        return foundPlan;
    }

    private static bool TryGetChartLegCost(
        TrailblazerWorldContext context,
        PathQuery surfaceIntent,
        Vector3d origin,
        out Fixed64 chartCost)
    {
        chartCost = Fixed64.Zero;
        if (origin == surfaceIntent.End.Position)
            return true;

        PathQuery rebased = surfaceIntent.WithStartPosition(origin);
        HybridRoutePlan? plan = HybridPathRequest.Create(context, rebased)?.RoutePlan;
        if (plan == null)
            return false;

        chartCost = plan.TotalPathCost;
        return true;
    }

    private static TraversalTransition[] GetLocalDirectedTransitions(
        TrailblazerWorldContext context,
        Vector3d targetPosition,
        TraversalMedium medium)
    {
        if (!context.World.TryGetVoxel(targetPosition, out Voxel? targetVoxel)
            || targetVoxel == null)
        {
            return Array.Empty<TraversalTransition>();
        }

        return context.Transitions.GetDirectedTransitionsToDestinationGrid(
            targetVoxel.GridIndex,
            medium,
            TraversalMedium.Solid);
    }
}
