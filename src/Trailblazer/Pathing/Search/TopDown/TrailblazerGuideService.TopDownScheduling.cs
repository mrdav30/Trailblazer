//=======================================================================
// TrailblazerGuideService.TopDownScheduling.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <content>Queues a planar route in the shared acquisition scheduler.</content>
public sealed partial class TrailblazerGuideService
{
    /// <summary>Schedules a retained top-down route between canonical cell centers.</summary>
    /// <remarks>
    /// Pending returns an owned request. The shared FIFO admits both endpoints and advances search within
    /// context frame allowances. Contiguous compatible requests share a build with independently admitted endpoints
    /// and lease cursors. Flow cohorts may share destination intent across origins and promote uncovered origins in
    /// queue order. Dispose the request or take its result once.
    /// </remarks>
    public NavigationGuideStatus ScheduleTopDownGuide(NavigationCellAddress start, NavigationCellAddress end,
        Fixed64 radius, NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities,
        PathAlgorithm algorithm, NavigationWorkBudget budget, out NavigationTopDownGuideRequest? request,
        bool allowTransitions = false)
    {
        EnsureUsable();
        request = null;
        var status = NavigationPlanarRouteRequest.TryCreate(start, end, radius, areaPolicy, medium, capabilities, algorithm,
            budget, allowTransitions, out var query);
        return status == NavigationGuideStatus.Success
            ? _scheduler.ScheduleTopDown(query, algorithm, out request) : status;
    }
}
