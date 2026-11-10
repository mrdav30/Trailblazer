//=======================================================================
// TrailblazerGuideService.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

public sealed partial class TrailblazerGuideService
{
    /// <summary>Acquires a retained route between canonical cell centers on one published top-down map.</summary>
    /// <remarks>
    /// A* and Flow use swept-circle legs and retain the selected center sequence in the shared A* route-guide
    /// lease and byte limits. Dispose each successful lease. A graph publication or raw world change invalidates it.
    /// </remarks>
    public NavigationGuideStatus RequestTopDownGuide(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities, PathAlgorithm algorithm,
        NavigationWorkBudget budget, out NavigationTopDownGuideLease guide)
    {
        TrailblazerPathingService pathing = _context.Pathing;
        return pathing.RunTopDownRoute(start, end, radius, areaPolicy, medium, capabilities, algorithm, budget,
            default, out _, out _, pathing.NavigationAStarAdmissionGate.PayloadCache, out guide);
    }
}
