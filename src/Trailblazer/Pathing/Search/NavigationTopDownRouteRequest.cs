//=======================================================================
// NavigationTopDownRouteRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

internal readonly struct NavigationTopDownRouteRequest
{
    internal NavigationTopDownRouteRequest(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities,
        NavigationWorkBudget budget)
    {
        Start = start;
        End = end;
        Radius = radius;
        AreaPolicy = areaPolicy;
        Medium = medium;
        Capabilities = capabilities;
        Budget = budget;
    }

    internal NavigationCellAddress Start { get; }
    internal NavigationCellAddress End { get; }
    internal Fixed64 Radius { get; }
    internal NavigationAreaPolicyKey AreaPolicy { get; }
    internal TraversalMedium Medium { get; }
    internal TraversalCapability Capabilities { get; }
    internal NavigationWorkBudget Budget { get; }
}
