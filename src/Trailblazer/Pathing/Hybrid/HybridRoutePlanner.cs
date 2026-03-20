using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

internal static class HybridRoutePlanner
{
    public static bool TryPlan(HybridPathRequest request, out HybridRoutePlan plan)
    {
        plan = null;
        if (request == null || !request.HasValidEndpoints)
            return false;

        if (TryPlanDirect(request, out HybridRoutePlan directPlan))
        {
            plan = directPlan;
            return true;
        }

        if (TryPlanSingleTransition(request, out HybridRoutePlan singleTransitionPlan))
        {
            plan = singleTransitionPlan;
            return true;
        }

        if (TryPlanTransitionPair(request, out HybridRoutePlan transitionPairPlan))
        {
            plan = transitionPairPlan;
            return true;
        }

        return false;
    }

    private static bool TryPlanDirect(HybridPathRequest request, out HybridRoutePlan plan)
    {
        plan = null;

        if (!TryCreateChartStep(
            request.Origin,
            request.TargetPosition,
            request,
            out HybridRouteStep step,
            out int chartCost))
        {
            return false;
        }

        plan = new HybridRoutePlan(new[] { step }, Array.Empty<TraversalTransition>(), chartCost);
        return true;
    }

    private static bool TryPlanSingleTransition(HybridPathRequest request, out HybridRoutePlan plan)
    {
        plan = null;
        HybridRoutePlan bestPlan = null;

        foreach (TraversalTransition transition in GetDirectedTransitions())
        {
            if (transition.Source.Kind != TraversalTransitionAnchorKind.Chart
                || transition.Destination.Kind != TraversalTransitionAnchorKind.Chart)
            {
                continue;
            }

            if (!TryCreateChartStep(
                request.Origin,
                transition.Source.Position,
                request,
                out HybridRouteStep toTransitionStep,
                out int toTransitionCost))
            {
                continue;
            }

            if (!TryCreateChartStep(
                transition.Destination.Position,
                request.TargetPosition,
                request,
                out HybridRouteStep toTargetStep,
                out int toTargetCost))
            {
                continue;
            }

            var candidate = new HybridRoutePlan(
                new[]
                {
                    toTransitionStep,
                    HybridRouteStep.Waypoint(transition.Destination.Position, transition.PathCostModifier),
                    toTargetStep
                },
                new[] { transition },
                toTransitionCost + transition.PathCostModifier + toTargetCost);

            bestPlan = GetBetterPlan(bestPlan, candidate);
        }

        plan = bestPlan;
        return plan != null;
    }

    private static bool TryPlanTransitionPair(HybridPathRequest request, out HybridRoutePlan plan)
    {
        plan = null;
        HybridRoutePlan bestPlan = null;
        TraversalTransition[] transitions = GetDirectedTransitions();

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition entry = transitions[i];
            if (entry.Source.Kind != TraversalTransitionAnchorKind.Chart
                || entry.Destination.Kind != TraversalTransitionAnchorKind.Volume)
            {
                continue;
            }

            if (!TryCreateChartStep(
                request.Origin,
                entry.Source.Position,
                request,
                out HybridRouteStep toEntryStep,
                out int toEntryCost))
            {
                continue;
            }

            for (int j = 0; j < transitions.Length; j++)
            {
                TraversalTransition exit = transitions[j];
                if (exit.Source.Kind != TraversalTransitionAnchorKind.Volume
                    || exit.Destination.Kind != TraversalTransitionAnchorKind.Chart
                    || entry.Destination.VolumeMode != exit.Source.VolumeMode)
                {
                    continue;
                }

                if (!TryCreateVolumeStep(
                    entry.Destination.Position,
                    exit.Source.Position,
                    request,
                    entry.Destination.VolumeMode,
                    out HybridRouteStep volumeStep,
                    out int volumeCost))
                {
                    continue;
                }

                if (!TryCreateChartStep(
                    exit.Destination.Position,
                    request.TargetPosition,
                    request,
                    out HybridRouteStep toTargetStep,
                    out int toTargetCost))
                {
                    continue;
                }

                var candidate = new HybridRoutePlan(
                    new[]
                    {
                        toEntryStep,
                        HybridRouteStep.Waypoint(entry.Destination.Position, entry.PathCostModifier),
                        volumeStep,
                        HybridRouteStep.Waypoint(exit.Destination.Position, exit.PathCostModifier),
                        toTargetStep
                    },
                    new[] { entry, exit },
                    toEntryCost + entry.PathCostModifier + volumeCost + exit.PathCostModifier + toTargetCost);

                bestPlan = GetBetterPlan(bestPlan, candidate);
            }
        }

        plan = bestPlan;
        return plan != null;
    }

    private static bool TryCreateChartStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        out HybridRouteStep step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        AStarPathRequest chartRequest = AStarPathRequest.Create(
            origin,
            destination,
            request.UnitSize,
            request.Heuristic,
            request.AllowUnwalkable);
        if (chartRequest == null)
            return false;

        chartRequest.MaxClimbHeight = request.MaxClimbHeight;

        if (chartRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(destination);
            return true;
        }

        AStarSurveyResult surveyResult = AStarSurveyor.Shared.FindPath(chartRequest);
        if (!surveyResult.HasPath)
            return false;

        pathCost = surveyResult.Waypoints[^1].PathCost;
        step = HybridRouteStep.Segment(chartRequest);
        return true;
    }

    private static bool TryCreateVolumeStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        VolumeTraversalMode traversalMode,
        out HybridRouteStep step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        VolumePathRequest volumeRequest = VolumePathRequest.Create(
            origin,
            destination,
            request.UnitSize,
            request.Heuristic,
            request.AllowUnwalkable,
            traversalMode);
        if (volumeRequest == null)
            return false;

        if (volumeRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(destination);
            return true;
        }

        VolumeSurveyResult surveyResult = VolumeSurveyor.Shared.FindPath(volumeRequest);
        if (!surveyResult.HasPath)
            return false;

        pathCost = surveyResult.Waypoints[^1].PathCost;
        step = HybridRouteStep.Segment(volumeRequest);
        return true;
    }

    private static HybridRoutePlan GetBetterPlan(HybridRoutePlan current, HybridRoutePlan candidate)
    {
        if (candidate == null)
            return current;

        if (current == null || candidate.TotalPathCost < current.TotalPathCost)
            return candidate;

        return current;
    }

    private static TraversalTransition[] GetDirectedTransitions()
    {
        TraversalTransition[] transitions = TraversalTransitionRegistry.AllTransitions;
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);

        for (int i = 0; i < transitions.Length; i++)
        {
            directed.Add(transitions[i]);

            if (transitions[i].IsBidirectional)
            {
                directed.Add(new TraversalTransition(
                    transitions[i].Id,
                    transitions[i].Type,
                    transitions[i].Destination,
                    transitions[i].Source,
                    transitions[i].PathCostModifier,
                    transitions[i].IsBidirectional));
            }
        }

        return directed.ToArray();
    }
}
