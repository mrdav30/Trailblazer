//=======================================================================
// HybridRoutePlanner.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing;

internal static class HybridRoutePlanner
{
    public static bool TryPlan(HybridPathRequest request, [NotNullWhen(true)] out HybridRoutePlan? plan)
    {
        plan = null;
        if (request == null || !request.HasValidEndpoints)
            return false;

        if (TryPlanDirect(request, out HybridRoutePlan? directPlan))
        {
            plan = directPlan;
            return true;
        }

        HybridRoutePlan? singleTransitionPlan = GetBetterPlan(
            TryPlanSingleTransitionLocally(request),
            TryPlanSingleTransitionGlobal(request));
        if (singleTransitionPlan != null)
        {
            plan = singleTransitionPlan;
            return true;
        }

        HybridRoutePlan? transitionPairPlan = GetBetterPlan(
            TryPlanTransitionPairLocally(request),
            TryPlanGlobalTransitionPairs(request));

        if (transitionPairPlan != null)
        {
            plan = transitionPairPlan;
            return true;
        }

        HybridRoutePlan? climbTransitionChainPlan = GetBetterPlan(
            TryPlanChainedClimbTransitions(request, GetLocalDirectedClimbTransitions(request)),
            TryPlanChainedClimbTransitions(request, TraversalTransitionQuery.GetDirectedTransitions(TraversalTransitionType.Climb)));
        if (climbTransitionChainPlan != null)
        {
            plan = climbTransitionChainPlan;
            return true;
        }

        return false;
    }

    private static bool TryPlanDirect(HybridPathRequest request, [NotNullWhen(true)] out HybridRoutePlan? plan)
    {
        plan = null;

        if (!TryCreateChartStep(
            request.Origin,
            request.TargetPosition,
            request,
            out HybridRouteStep? step,
            out int chartCost))
        {
            return false;
        }

        plan = new HybridRoutePlan(new[] { step! }, Array.Empty<TraversalTransition>(), chartCost);
        return true;
    }

    private static HybridRoutePlan? TryPlanSingleTransitionLocally(HybridPathRequest request)
    {
        if (request.StartNode == null || request.EndNode == null)
            return null;

        HybridRoutePlan? bestPlan = null;
        int startGridIndex = request.StartNode.GridIndex;
        int endGridIndex = request.EndNode.GridIndex;

        if (TryPlanSingleTransition(
            request,
            TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
                startGridIndex,
                TraversalMedium.Solid,
                TraversalMedium.Solid),
            out HybridRoutePlan? startGridPlan))
        {
            bestPlan = GetBetterPlan(bestPlan, startGridPlan);
        }

        if (startGridIndex != endGridIndex
            && TryPlanSingleTransition(
                request,
                TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(
                    endGridIndex,
                    TraversalMedium.Solid,
                    TraversalMedium.Solid),
                out HybridRoutePlan? endGridPlan))
        {
            bestPlan = GetBetterPlan(bestPlan, endGridPlan);
        }

        return bestPlan;
    }

    private static HybridRoutePlan? TryPlanSingleTransitionGlobal(HybridPathRequest request)
    {
        return TryPlanSingleTransition(
            request,
            TraversalTransitionQuery.GetDirectedTransitions(
                TraversalMedium.Solid,
                TraversalMedium.Solid),
            out HybridRoutePlan? plan)
            ? plan
            : null;
    }

    private static bool TryPlanSingleTransition(
        HybridPathRequest request,
        TraversalTransition[] transitions,
        [NotNullWhen(true)] out HybridRoutePlan? plan)
    {
        plan = null;
        HybridRoutePlan? bestPlan = null;
        if (transitions == null || transitions.Length == 0)
            return false;

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            if (!TryCreateChartStep(
                request.Origin,
                transition.Source.Position,
                request,
                out HybridRouteStep? toTransitionStep,
                out int toTransitionCost))
            {
                continue;
            }

            if (!TryCreateChartStep(
                transition.Destination.Position,
                request.TargetPosition,
                request,
                out HybridRouteStep? toTargetStep,
                out int toTargetCost))
            {
                continue;
            }

            var candidate = new HybridRoutePlan(
                new[]
                {
                    toTransitionStep!,
                    HybridRouteStep.Waypoint(request.Context, transition.Destination.Position, transition.PathCostModifier),
                    toTargetStep!
                },
                new[] { transition },
                toTransitionCost + transition.PathCostModifier + toTargetCost);

            bestPlan = GetBetterPlan(bestPlan, candidate);
        }

        plan = bestPlan;
        return plan != null;
    }

    private static HybridRoutePlan? TryPlanTransitionPairLocally(HybridPathRequest request)
    {
        if (request.StartNode == null || request.EndNode == null)
            return null;

        HybridRoutePlan? bestPlan = null;

        HybridRoutePlan? gasPlan = TryPlanTransitionPairForMedium(
            request,
            TraversalMedium.Gas,
            TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
                request.StartNode.GridIndex,
                TraversalMedium.Solid,
                TraversalMedium.Gas),
            TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(
                request.EndNode.GridIndex,
                TraversalMedium.Gas,
                TraversalMedium.Solid));
        bestPlan = GetBetterPlan(bestPlan, gasPlan);

        HybridRoutePlan? liquidPlan = TryPlanTransitionPairForMedium(
            request,
            TraversalMedium.Liquid,
            TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
                request.StartNode.GridIndex,
                TraversalMedium.Solid,
                TraversalMedium.Liquid),
            TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(
                request.EndNode.GridIndex,
                TraversalMedium.Liquid,
                TraversalMedium.Solid));
        bestPlan = GetBetterPlan(bestPlan, liquidPlan);

        return bestPlan;
    }

    private static HybridRoutePlan? TryPlanGlobalTransitionPairs(HybridPathRequest request)
    {
        HybridRoutePlan? bestPlan = null;

        bestPlan = GetBetterPlan(
            bestPlan,
            TryPlanTransitionPairForMedium(
                request,
                TraversalMedium.Gas,
                TraversalTransitionQuery.GetDirectedTransitions(
                    TraversalMedium.Solid,
                    TraversalMedium.Gas),
                TraversalTransitionQuery.GetDirectedTransitions(
                    TraversalMedium.Gas,
                    TraversalMedium.Solid)));

        bestPlan = GetBetterPlan(
            bestPlan,
            TryPlanTransitionPairForMedium(
                request,
                TraversalMedium.Liquid,
                TraversalTransitionQuery.GetDirectedTransitions(
                    TraversalMedium.Solid,
                    TraversalMedium.Liquid),
                TraversalTransitionQuery.GetDirectedTransitions(
                    TraversalMedium.Liquid,
                    TraversalMedium.Solid)));

        return bestPlan;
    }

    private static HybridRoutePlan? TryPlanTransitionPairForMedium(
        HybridPathRequest request,
        TraversalMedium volumeMedium,
        TraversalTransition[] entries,
        TraversalTransition[] exits)
    {
        return TryPlanTransitionPair(request, volumeMedium, entries, exits, out HybridRoutePlan? plan)
            ? plan
            : null;
    }

    private static bool TryPlanTransitionPair(
        HybridPathRequest request,
        TraversalMedium volumeMedium,
        TraversalTransition[] entries,
        TraversalTransition[] exits,
        [NotNullWhen(true)] out HybridRoutePlan? plan)
    {
        plan = null;
        HybridRoutePlan? bestPlan = null;
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
            if (!TryCreateChartStep(
                request.Origin,
                entry.Source.Position,
                request,
                out HybridRouteStep? toEntryStep,
                out int toEntryCost))
            {
                continue;
            }

            for (int j = 0; j < exits.Length; j++)
            {
                TraversalTransition exit = exits[j];
                if (!TryCreateVolumeStep(
                    entry.Destination.Position,
                    exit.Source.Position,
                    request,
                    volumeMedium,
                    out HybridRouteStep? volumeStep,
                    out int volumeCost))
                {
                    continue;
                }

                if (!TryCreateChartStep(
                    exit.Destination.Position,
                    request.TargetPosition,
                    request,
                    out HybridRouteStep? toTargetStep,
                    out int toTargetCost))
                {
                    continue;
                }

                var candidate = new HybridRoutePlan(
                    new[]
                    {
                        toEntryStep!,
                        HybridRouteStep.Waypoint(request.Context, entry.Destination.Position, entry.PathCostModifier),
                        volumeStep!,
                        HybridRouteStep.Waypoint(request.Context, exit.Destination.Position, exit.PathCostModifier),
                        toTargetStep!
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
        [NotNullWhen(true)] out HybridRouteStep? step,
        out int pathCost)
    {
        if (request.ChartRequestKind == HybridChartRequestKind.FlowField)
        {
            return TryCreateFlowFieldStep(
                origin,
                destination,
                request,
                out step,
                out pathCost);
        }

        return TryCreateAStarStep(
            origin,
            destination,
            request,
            out step,
            out pathCost);
    }

    private static bool TryCreateAStarStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        [NotNullWhen(true)] out HybridRouteStep? step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        AStarPathRequest? chartRequest = AStarPathRequest.Create(
            request.Context,
            origin,
            destination,
            request.UnitSize,
            request.Heuristic,
            request.AllowUnwalkableEndpoints);
        if (chartRequest == null)
            return false;

        chartRequest.MaxClimbHeight = request.MaxClimbHeight;

        if (chartRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(request.Context, destination);
            return true;
        }

        AStarSurveyResult surveyResult = request.Context.Pathing.State.GuideState.AStarSurveyor.FindPath(chartRequest);
        if (!surveyResult.HasPath)
            return false;

        pathCost = surveyResult.Waypoints[^1].PathCost;
        step = HybridRouteStep.Segment(chartRequest, chartKeys: surveyResult.ChartsUtilized);
        return true;
    }

    private static bool TryCreateFlowFieldStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        [NotNullWhen(true)] out HybridRouteStep? step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        FlowFieldPathRequest? chartRequest = FlowFieldPathRequest.Create(
            request.Context,
            origin,
            destination,
            request.UnitSize,
            request.AllowUnwalkableEndpoints);
        if (chartRequest == null)
            return false;

        chartRequest.MaxClimbHeight = request.MaxClimbHeight;
        chartRequest.ExtraFloodRange = request.ExtraFloodRange;

        if (chartRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(request.Context, destination);
            return true;
        }

        FlowFieldSurveyResult surveyResult = request.Context.Pathing.State.GuideState.FlowFieldSurveyor.FindPath(chartRequest);
        if (!surveyResult.HasPath
            || surveyResult.Fields == null
            || chartRequest.StartNode == null
            || !surveyResult.Fields.TryGetValue(chartRequest.StartNode.WorldIndex, out FlowField startField))
        {
            return false;
        }

        pathCost = startField.PathCost;
        step = HybridRouteStep.Segment(chartRequest, chartKeys: surveyResult.ChartsUtilized);
        return true;
    }

    private static bool TryCreateVolumeStep(
        Vector3d origin,
        Vector3d destination,
        HybridPathRequest request,
        TraversalMedium medium,
        [NotNullWhen(true)] out HybridRouteStep? step,
        out int pathCost)
    {
        step = null;
        pathCost = 0;

        VolumePathRequest? volumeRequest = VolumePathRequest.Create(
            request.Context,
            origin,
            destination,
            request.UnitSize,
            request.Heuristic,
            request.AllowUnwalkableEndpoints,
            medium);
        if (volumeRequest == null)
            return false;

        if (volumeRequest.HasZeroDisplacement)
        {
            step = HybridRouteStep.Waypoint(request.Context, destination);
            return true;
        }

        VolumeSurveyResult surveyResult = request.Context.Pathing.State.GuideState.VolumeSurveyor.FindPath(volumeRequest);
        if (!surveyResult.HasPath)
            return false;

        pathCost = surveyResult.Waypoints![^1].PathCost;
        step = HybridRouteStep.Segment(volumeRequest, chartKeys: surveyResult.ChartsUtilized);
        return true;
    }

    private static HybridRoutePlan? GetBetterPlan(HybridRoutePlan? current, HybridRoutePlan? candidate)
    {
        if (candidate == null)
            return current;

        if (current == null || candidate.TotalPathCost < current.TotalPathCost)
            return candidate;

        return current;
    }

    private static HybridRoutePlan? TryPlanChainedClimbTransitions(
        HybridPathRequest request,
        TraversalTransition[] transitions)
    {
        if (transitions == null || transitions.Length == 0)
            return null;

        int[] bestCosts = new int[transitions.Length];
        int[] previousIndices = new int[transitions.Length];
        HybridRouteStep?[] entrySteps = new HybridRouteStep?[transitions.Length];
        HybridRouteStep?[] bridgeSteps = new HybridRouteStep?[transitions.Length];
        bool[] visited = new bool[transitions.Length];

        for (int i = 0; i < transitions.Length; i++)
        {
            bestCosts[i] = int.MaxValue;
            previousIndices[i] = -1;

            if (!TryCreateChartStep(
                request.Origin,
                transitions[i].Source.Position,
                request,
                out HybridRouteStep? entryStep,
                out int entryCost))
            {
                continue;
            }

            entrySteps[i] = entryStep;
            bestCosts[i] = entryCost + transitions[i].PathCostModifier;
        }

        int bestEndIndex = -1;
        HybridRouteStep? bestExitStep = null;
        int bestTotalCost = int.MaxValue;

        while (true)
        {
            int currentIndex = GetCheapestUnvisitedTransition(bestCosts, visited);
            if (currentIndex < 0)
                break;

            visited[currentIndex] = true;
            TraversalTransition currentTransition = transitions[currentIndex];
            int currentCost = bestCosts[currentIndex];

            if (TryCreateChartStep(
                currentTransition.Destination.Position,
                request.TargetPosition,
                request,
                out HybridRouteStep? exitStep,
                out int exitCost))
            {
                int totalCost = currentCost + exitCost;
                if (totalCost < bestTotalCost)
                {
                    bestTotalCost = totalCost;
                    bestEndIndex = currentIndex;
                    bestExitStep = exitStep;
                }
            }

            for (int nextIndex = 0; nextIndex < transitions.Length; nextIndex++)
            {
                if (visited[nextIndex])
                    continue;

                HybridRouteStep? bridgeStep = null;
                int bridgeCost = 0;
                if (transitions[nextIndex].Source.Position != currentTransition.Destination.Position
                    && !TryCreateChartStep(
                        currentTransition.Destination.Position,
                        transitions[nextIndex].Source.Position,
                        request,
                        out bridgeStep,
                        out bridgeCost))
                {
                    continue;
                }

                int candidateCost = currentCost + bridgeCost + transitions[nextIndex].PathCostModifier;
                if (candidateCost >= bestCosts[nextIndex])
                    continue;

                bestCosts[nextIndex] = candidateCost;
                previousIndices[nextIndex] = currentIndex;
                bridgeSteps[nextIndex] = bridgeStep;
            }
        }

        if (bestEndIndex < 0 || bestExitStep == null)
            return null;

        return BuildChainedClimbPlan(
            request.Context,
            transitions,
            previousIndices,
            entrySteps,
            bridgeSteps,
            bestEndIndex,
            bestExitStep,
            bestTotalCost);
    }

    private static TraversalTransition[] GetLocalDirectedClimbTransitions(HybridPathRequest request)
    {
        if (request?.StartNode == null || request.EndNode == null)
            return Array.Empty<TraversalTransition>();

        int startGridIndex = request.StartNode.GridIndex;
        int endGridIndex = request.EndNode.GridIndex;
        SwiftList<TraversalTransition> climbTransitions = new();
        SwiftHashSet<string> seenTransitionIds = new();

        AddTransitions(
            climbTransitions,
            seenTransitionIds,
            TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
                startGridIndex,
                TraversalTransitionType.Climb));

        TraversalTransition[] destinationTransitions = FilterTransitionsByType(
            TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(endGridIndex),
            TraversalTransitionType.Climb);
        AddTransitions(climbTransitions, seenTransitionIds, destinationTransitions);

        if (startGridIndex != endGridIndex)
        {
            AddTransitions(
                climbTransitions,
                seenTransitionIds,
                TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
                    endGridIndex,
                    TraversalTransitionType.Climb));

            TraversalTransition[] originDestinationTransitions = FilterTransitionsByType(
                TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(startGridIndex),
                TraversalTransitionType.Climb);
            AddTransitions(climbTransitions, seenTransitionIds, originDestinationTransitions);
        }

        return climbTransitions.Count == 0
            ? Array.Empty<TraversalTransition>()
            : climbTransitions.ToArray();
    }

    private static TraversalTransition[] FilterTransitionsByType(
        TraversalTransition[] transitions,
        TraversalTransitionType type)
    {
        if (transitions == null || transitions.Length == 0)
            return Array.Empty<TraversalTransition>();

        SwiftList<TraversalTransition> climbTransitions = new();
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i].Type == type)
                climbTransitions.Add(transitions[i]);
        }

        return climbTransitions.Count == 0
            ? Array.Empty<TraversalTransition>()
            : climbTransitions.ToArray();
    }

    private static void AddTransitions(
        SwiftList<TraversalTransition> destination,
        SwiftHashSet<string> seenTransitionIds,
        TraversalTransition[] source)
    {
        if (source == null || source.Length == 0)
            return;

        for (int i = 0; i < source.Length; i++)
        {
            TraversalTransition transition = source[i];
            if (seenTransitionIds.Add(transition.Id))
                destination.Add(transition);
        }
    }

    private static int GetCheapestUnvisitedTransition(int[] bestCosts, bool[] visited)
    {
        int bestIndex = -1;
        int bestCost = int.MaxValue;
        for (int i = 0; i < bestCosts.Length; i++)
        {
            if (visited[i] || bestCosts[i] >= bestCost)
                continue;

            bestCost = bestCosts[i];
            bestIndex = i;
        }

        return bestIndex;
    }

    private static HybridRoutePlan BuildChainedClimbPlan(
        TrailblazerWorldContext context,
        TraversalTransition[] transitions,
        int[] previousIndices,
        HybridRouteStep?[] entrySteps,
        HybridRouteStep?[] bridgeSteps,
        int endIndex,
        HybridRouteStep exitStep,
        int totalCost)
    {
        SwiftList<int> reversedTransitionIndices = new();
        for (int index = endIndex; index >= 0; index = previousIndices[index])
            reversedTransitionIndices.Add(index);

        int transitionCount = reversedTransitionIndices.Count;
        var orderedTransitions = new TraversalTransition[transitionCount];
        var steps = new SwiftList<HybridRouteStep>();

        int startTransitionIndex = reversedTransitionIndices[transitionCount - 1];
        steps.Add(entrySteps[startTransitionIndex]!);

        int orderedIndex = 0;
        for (int i = transitionCount - 1; i >= 0; i--)
        {
            int transitionIndex = reversedTransitionIndices[i];
            TraversalTransition transition = transitions[transitionIndex];
            orderedTransitions[orderedIndex] = transition;

            if (orderedIndex > 0 && bridgeSteps[transitionIndex] != null)
                steps.Add(bridgeSteps[transitionIndex]!);

            steps.Add(HybridRouteStep.Waypoint(
                context,
                transition.Destination.Position,
                transition.PathCostModifier));
            orderedIndex++;
        }

        steps.Add(exitStep);
        return new HybridRoutePlan(steps.ToArray(), orderedTransitions, totalCost);
    }

}
