//=======================================================================
// HybridRoutePlan.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal sealed class HybridRoutePlan
{
    public HybridRoutePlan(
        HybridRouteStep[] steps,
        TraversalTransition[] directedTransitions,
        Fixed64 totalPathCost)
    {
        Steps = steps ?? Array.Empty<HybridRouteStep>();
        DirectedTransitions = directedTransitions ?? Array.Empty<TraversalTransition>();
        TotalPathCost = totalPathCost;
    }

    public HybridRouteStep[] Steps { get; }

    public TraversalTransition[] DirectedTransitions { get; }

    public Fixed64 TotalPathCost { get; }
}
