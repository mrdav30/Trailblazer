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

        HybridRoutePlan singleTransitionPlan = TryPlanSingleTransitionLocally(request);
        if (singleTransitionPlan == null
            && TryPlanSingleTransition(request, TraversalTransitionQuery.GetDirectedTransitions(), out HybridRoutePlan globalSingleTransitionPlan))
        {
            singleTransitionPlan = globalSingleTransitionPlan;
        }

        if (singleTransitionPlan != null)
        {
            plan = singleTransitionPlan;
            return true;
        }

        HybridRoutePlan transitionPairPlan = TryPlanTransitionPairLocally(request);
        if (transitionPairPlan == null)
        {
            TraversalTransition[] allTransitions = TraversalTransitionQuery.GetDirectedTransitions();
            if (TryPlanTransitionPair(
                request,
                allTransitions,
                allTransitions,
                out HybridRoutePlan globalTransitionPairPlan))
            {
                transitionPairPlan = globalTransitionPairPlan;
            }
        }

        if (transitionPairPlan != null)
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

    private static HybridRoutePlan TryPlanSingleTransitionLocally(HybridPathRequest request)
    {
        HybridRoutePlan bestPlan = null;
        int startGridIndex = request.StartNode.GridIndex;
        int endGridIndex = request.EndNode.GridIndex;

        if (TryPlanSingleTransition(
            request,
            TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(startGridIndex),
            out HybridRoutePlan startGridPlan))
        {
            bestPlan = GetBetterPlan(bestPlan, startGridPlan);
        }

        if (startGridIndex != endGridIndex
            && TryPlanSingleTransition(
                request,
                TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(endGridIndex),
                out HybridRoutePlan endGridPlan))
        {
            bestPlan = GetBetterPlan(bestPlan, endGridPlan);
        }

        return bestPlan;
    }

    private static bool TryPlanSingleTransition(
        HybridPathRequest request,
        TraversalTransition[] transitions,
        out HybridRoutePlan plan)
    {
        plan = null;
        HybridRoutePlan bestPlan = null;
        if (transitions == null || transitions.Length == 0)
            return false;

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            if (transition.Source.Space != TraversalTransitionAnchorSpace.Chart
                || transition.Destination.Space != TraversalTransitionAnchorSpace.Chart)
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

    private static HybridRoutePlan TryPlanTransitionPairLocally(HybridPathRequest request)
    {
        TraversalTransition[] localEntries = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(request.StartNode.GridIndex);
        TraversalTransition[] localExits = TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(request.EndNode.GridIndex);

        return TryPlanTransitionPair(request, localEntries, localExits, out HybridRoutePlan plan)
            ? plan
            : null;
    }

    private static bool TryPlanTransitionPair(
        HybridPathRequest request,
        TraversalTransition[] entries,
        TraversalTransition[] exits,
        out HybridRoutePlan plan)
    {
        plan = null;
        HybridRoutePlan bestPlan = null;
        if (entries == null
            || exits == null
            || entries.Length == 0
            || exits.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            TraversalTransition entry = entries[i];
            if (entry.Source.Space != TraversalTransitionAnchorSpace.Chart
                || !entry.Destination.TryGetVolumeTraversalMode(out VolumeTraversalMode entryTraversalMode))
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

            for (int j = 0; j < exits.Length; j++)
            {
                TraversalTransition exit = exits[j];
                if (!exit.Source.TryGetVolumeTraversalMode(out VolumeTraversalMode exitTraversalMode)
                    || exit.Destination.Space != TraversalTransitionAnchorSpace.Chart
                    || entryTraversalMode != exitTraversalMode)
                {
                    continue;
                }

                if (!TryCreateVolumeStep(
                    entry.Destination.Position,
                    exit.Source.Position,
                    request,
                    entryTraversalMode,
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

        return request.ChartRequestKind switch
        {
            HybridChartRequestKind.FlowField => TryCreateFlowFieldStep(
                origin,
                destination,
                request,
                out step,
                out pathCost),
            _ => TryCreateAStarStep(
                origin,
                destination,
                request,
                out step,
                out pathCost),
        };
    }

    private static bool TryCreateAStarStep(
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
            request.AllowUnwalkableEndNode);
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

    private static bool TryCreateFlowFieldStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        out HybridRouteStep step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        FlowFieldPathRequest chartRequest = FlowFieldPathRequest.Create(
            origin,
            destination,
            request.UnitSize,
            request.AllowUnwalkableEndNode);
        if (chartRequest == null)
            return false;

        chartRequest.ExtraFloodRange = request.ExtraFloodRange;

        if (chartRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(destination);
            return true;
        }

        FlowFieldSurveyResult surveyResult = FlowFieldSurveyor.Shared.FindPath(chartRequest);
        if (!surveyResult.HasPath
            || !surveyResult.Fields.TryGetValue(chartRequest.StartNode.GlobalIndex, out FlowField startField))
        {
            return false;
        }

        pathCost = startField.PathCost;
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
            request.AllowUnwalkableEndNode,
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

}
