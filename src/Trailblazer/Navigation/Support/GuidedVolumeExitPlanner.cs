using FixedMathSharp;
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
        bool allowUnwalkable,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        out VolumePathRequest request,
        out GuidedVolumeExitHandoff handoff)
    {
        request = null;
        handoff = null;

        if (chartPathMode != GuidedPathMode.AStar
            && chartPathMode != GuidedPathMode.FlowField)
        {
            return false;
        }

        TraversalTransition bestTransition = default;
        VolumePathRequest bestRequest = null;
        int bestTotalCost = int.MaxValue;

        TraversalTransition[] transitions = TraversalTransitionQuery.GetDirectedTransitions();
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
                allowUnwalkable,
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
                allowUnwalkable,
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
        }

        if (bestRequest == null)
            return false;

        request = bestRequest;
        handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = bestTransition.Id,
            ChartOriginPosition = bestTransition.Destination.Position,
            TargetPosition = targetPosition,
            ChartPathMode = chartPathMode,
            AllowUnwalkable = allowUnwalkable,
            AllowTraversalTransitions = allowTraversalTransitions,
            AStarHeuristic = aStarHeuristic,
            AStarMaxClimbHeight = aStarMaxClimbHeight,
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange
        };
        return true;
    }

    private static bool TryGetChartLegCost(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode chartPathMode,
        bool allowUnwalkable,
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
                    allowUnwalkable,
                    allowTraversalTransitions);
                if (flowFieldRequest == null)
                    return false;

                flowFieldRequest.ExtraFloodRange = flowFieldExtraFloodRange;
                if (flowFieldRequest.HasZeroDisplacement)
                    return true;

                FlowFieldSurveyResult flowFieldResult = FlowFieldSurveyor.Shared.FindPath(flowFieldRequest);
                return flowFieldResult.HasPath
                    && flowFieldResult.Fields.TryGetValue(flowFieldRequest.StartNode.GlobalIndex, out FlowField startField)
                    && TryAssignChartCost(startField.PathCost, out chartCost);

            case GuidedPathMode.AStar:
            default:
                AStarPathRequest aStarRequest = AStarPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    allowTraversalTransitions);
                if (aStarRequest == null)
                    return false;

                aStarRequest.MaxClimbHeight = aStarMaxClimbHeight;
                if (aStarRequest.HasZeroDisplacement)
                    return true;

                AStarSurveyResult aStarResult = AStarSurveyor.Shared.FindPath(aStarRequest);
                return aStarResult.HasPath
                    && TryAssignChartCost(aStarResult.Waypoints[^1].PathCost, out chartCost);
        }
    }

    private static bool TryAssignChartCost(int cost, out int chartCost)
    {
        chartCost = cost;
        return true;
    }
}
