//=======================================================================
// NavigationPlanarRouteRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

internal readonly struct NavigationPlanarRouteRequest
{
    internal NavigationPlanarRouteRequest(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities,
        NavigationWorkBudget budget, bool allowTransitions = false)
    {
        Start = start;
        End = end;
        Radius = radius;
        AreaPolicy = areaPolicy;
        Medium = medium;
        Capabilities = capabilities;
        Budget = budget;
        AllowTransitions = allowTransitions;
    }

    internal NavigationCellAddress Start { get; }
    internal NavigationCellAddress End { get; }
    internal Fixed64 Radius { get; }
    internal NavigationAreaPolicyKey AreaPolicy { get; }
    internal TraversalMedium Medium { get; }
    internal TraversalCapability Capabilities { get; }
    internal NavigationWorkBudget Budget { get; }
    internal bool AllowTransitions { get; }

    internal static NavigationGuideStatus TryCreate(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities, PathAlgorithm algorithm,
        NavigationWorkBudget budget, bool allowTransitions, out NavigationPlanarRouteRequest request)
    {
        areaPolicy.Validate(nameof(areaPolicy));
        request = default;
        if (start.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        if (end.MapId == null)
            return NavigationGuideStatus.InvalidEnd;
        if (!TrailblazerPathingService.IsTopDownProfileValid(radius, NavigationCell.ToMedia(medium), capabilities))
            return NavigationGuideStatus.InvalidProfile;
        if (algorithm != PathAlgorithm.AStar && algorithm != PathAlgorithm.FlowField)
            return NavigationGuideStatus.Unsupported;
        request = new NavigationPlanarRouteRequest(start, end, radius, areaPolicy, medium, capabilities, budget,
            allowTransitions);
        return NavigationGuideStatus.Success;
    }

    internal bool Matches(in NavigationPlanarRouteRequest candidate, bool requireStart) =>
        (!requireStart || Start == candidate.Start) && End == candidate.End && Radius == candidate.Radius
        && AreaPolicy == candidate.AreaPolicy && Medium == candidate.Medium && Capabilities == candidate.Capabilities
        && Budget == candidate.Budget && AllowTransitions == candidate.AllowTransitions;

    internal long GetLogicalRetainedBytes() => PathQueryBatchItem.LogicalRetainedBytes
        + Unsafe.SizeOf<NavigationPlanarRouteRequest>() + Unsafe.SizeOf<NavigationTopDownGuideLease?>()
        + ((long)Start.MapId.Length + End.MapId.Length + AreaPolicy.PolicyId.Length) * sizeof(char);
}
