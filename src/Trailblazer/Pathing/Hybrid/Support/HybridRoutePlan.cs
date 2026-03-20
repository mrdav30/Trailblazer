using System;

namespace Trailblazer.Pathing;

internal sealed class HybridRoutePlan
{
    public HybridRoutePlan(
        HybridRouteStep[] steps,
        TraversalTransition[] directedTransitions,
        int totalPathCost)
    {
        Steps = steps ?? Array.Empty<HybridRouteStep>();
        DirectedTransitions = directedTransitions ?? Array.Empty<TraversalTransition>();
        TotalPathCost = totalPathCost;
    }

    public HybridRouteStep[] Steps { get; }

    public TraversalTransition[] DirectedTransitions { get; }

    public int TotalPathCost { get; }
}
