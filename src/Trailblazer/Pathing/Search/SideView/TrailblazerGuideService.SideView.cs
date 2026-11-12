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
        NavigationWorkBudget budget, out NavigationSideViewGuideLease guide) =>
        RequestSideViewGuide(start, end, profile, areaPolicy, algorithm, budget, out guide, allowTransitions: false);

    /// <summary>Acquires a Solid side-view guide with optional authored actions between supported anchors.</summary>
    /// <remarks>The host owns the action trajectory and completes the exact held token after accepting the landing.</remarks>
    public NavigationGuideStatus RequestSideViewGuide(NavigationCellAddress start, NavigationCellAddress end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, PathAlgorithm algorithm,
        NavigationWorkBudget budget, out NavigationSideViewGuideLease guide, bool allowTransitions)
    {
        EnsureUsable();
        guide = default;
        var status = TryCreateSideViewRequest(start, end, profile, areaPolicy, algorithm, budget, allowTransitions, out var request);
        if (status != NavigationGuideStatus.Success)
            return status;
        var pathing = _context.Pathing;
        status = pathing.FindPlanarRoute(request, algorithm, default, out _, out _,
            pathing.NavigationAStarAdmissionGate.PayloadCache, out var acquired, profile);
        if (acquired != null)
            guide = new NavigationSideViewGuideLease(acquired);
        return status;
    }

    /// <summary>Schedules an ordinary Solid side-view guide between two authored support anchors.</summary>
    /// <remarks>
    /// Pending returns an owned request. The shared FIFO advances admission, search and waypoint publication
    /// within context frame allowances. Dispose the request or take its result once.
    /// </remarks>
    public NavigationGuideStatus ScheduleSideViewGuide(NavigationCellAddress start, NavigationCellAddress end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, PathAlgorithm algorithm,
        NavigationWorkBudget budget, out NavigationSideViewGuideRequest? request) =>
        ScheduleSideViewGuide(start, end, profile, areaPolicy, algorithm, budget, out request, allowTransitions: false);

    /// <summary>Schedules a Solid side-view guide with optional authored actions between supported anchors.</summary>
    public NavigationGuideStatus ScheduleSideViewGuide(NavigationCellAddress start, NavigationCellAddress end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, PathAlgorithm algorithm,
        NavigationWorkBudget budget, out NavigationSideViewGuideRequest? request, bool allowTransitions)
    {
        EnsureUsable();
        request = null;
        var status = TryCreateSideViewRequest(start, end, profile, areaPolicy, algorithm, budget, allowTransitions, out var query);
        if (status != NavigationGuideStatus.Success)
            return status;
        status = _scheduler.SchedulePlanar(query, algorithm, out var queued, profile);
        request = (NavigationSideViewGuideRequest?)queued;
        return status;
    }

    private static NavigationGuideStatus TryCreateSideViewRequest(NavigationCellAddress start, NavigationCellAddress end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, PathAlgorithm algorithm,
        NavigationWorkBudget budget, bool allowTransitions, out NavigationPlanarRouteRequest request)
    {
        request = default;
        areaPolicy.Validate(nameof(areaPolicy));
        if (start.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        if (end.MapId == null)
            return NavigationGuideStatus.InvalidEnd;
        if (!TrailblazerPathingService.IsSideViewSolidProfile(profile))
            return NavigationGuideStatus.InvalidProfile;
        if (algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField)
            return NavigationGuideStatus.Unsupported;

        request = new NavigationPlanarRouteRequest(start, end, profile.Radius, areaPolicy,
            TraversalMedium.Solid, profile.Capabilities, budget, allowTransitions);
        return NavigationGuideStatus.Success;
    }
}
