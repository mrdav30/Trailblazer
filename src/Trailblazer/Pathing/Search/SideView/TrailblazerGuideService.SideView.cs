//=======================================================================
// TrailblazerGuideService.SideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Acquires guides over authored side-view support.</content>
public sealed partial class TrailblazerGuideService
{
    /// <summary>Acquires an ordinary Solid side-view guide between two authored support anchors.</summary>
    /// <remarks>
    /// A* and Flow prove directed swept-capsule legs. Steps retain their lift or descent waypoint.
    /// The guide uses the shared A* payload lease and byte limits. A graph publication or raw world
    /// change invalidates it. Dispose each successful lease.
    /// </remarks>
    public NavigationGuideStatus RequestSideViewGuide(NavigationCellAddress start, NavigationCellAddress end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, PathAlgorithm algorithm,
        NavigationWorkBudget budget, out NavigationSideViewGuideLease guide)
    {
        EnsureUsable();
        guide = default;
        areaPolicy.Validate(nameof(areaPolicy));
        if (start.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        if (end.MapId == null)
            return NavigationGuideStatus.InvalidEnd;
        if (!TrailblazerPathingService.IsSideViewSolidProfile(profile))
            return NavigationGuideStatus.InvalidProfile;
        if (algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField)
            return NavigationGuideStatus.Unsupported;

        var request = new NavigationPlanarRouteRequest(start, end, profile.Radius, areaPolicy,
            TraversalMedium.Solid, profile.Capabilities, budget);
        var pathing = _context.Pathing;
        var status = pathing.FindPlanarRoute(request, algorithm, default, out _, out _,
            pathing.NavigationAStarAdmissionGate.PayloadCache, out var acquired, profile);
        if (acquired != null)
            guide = new NavigationSideViewGuideLease(acquired);
        return status;
    }
}
