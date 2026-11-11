//=======================================================================
// NavigationGuideScheduler.Planar.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Uses the shared queue and cohort source pin for planar routes.</content>
internal sealed partial class NavigationGuideScheduler
{
    private NavigationPlanarRouteRequest _cohortPlanarQuery;
    private NavigationAgentProfile2D _cohortSideViewProfile;
    private NavigationPlanarRouteWork? _planarWork;

    internal NavigationGuideStatus SchedulePlanar(in NavigationPlanarRouteRequest query, PathAlgorithm algorithm,
        out NavigationGuideRequest? request, in NavigationAgentProfile2D sideViewProfile = default)
    {
        request = null;
        long bytes = query.GetLogicalRetainedBytes(sideViewProfile);
        lock (_sync)
        {
            ChronicleGuard.ThrowIfDisposed(_closed, nameof(TrailblazerWorldContext));
            if (!HasRequestCapacity(bytes))
                return NavigationGuideStatus.CapacityExceeded;
            NavigationGuideRequest candidate = sideViewProfile.Mode == NavigationMode2D.SideView
                ? new NavigationSideViewGuideRequest(this, algorithm, query, sideViewProfile, bytes)
                : new NavigationTopDownGuideRequest(this, algorithm, query, bytes);
            Enqueue(candidate);
            request = candidate;
            return NavigationGuideStatus.Pending;
        }
    }

    private void BeginPlanar(NavigationGuideRequest request, INavigationPlanarGuideRequest planar)
    {
        var pathing = _context.Pathing;
        var world = _context.World;
        _planarWork = request.Algorithm == PathAlgorithm.FlowField
            ? pathing.NavigationFlowAdmissionGate.BeginScheduledPlanar(world, planar.PlanarQuery, planar.SideViewProfile)
            : pathing.NavigationAStarAdmissionGate.BeginScheduledPlanar(world, planar.PlanarQuery, planar.SideViewProfile);
        if (_planarWork == null)
            Complete(request, NavigationGuideStatus.CapacityExceeded);
        else
            SetPhase(NavigationGuideRequestPhase.Admission);
    }

    // True means the continuation is still active and may need to yield on a drained slice.
    private bool AdvancePlanar(NavigationGuideRequest request, INavigationPlanarGuideRequest planar)
    {
        var work = _planarWork!;
        if (_phase == NavigationGuideRequestPhase.Publication)
            return PublishPlanar(request, planar, work);
        work.Advance(ref _frameRemaining, endpointOnly: _sharedAdmission);
        if (work.Status == NavigationGuideStatus.Success)
            SetPhase(NavigationGuideRequestPhase.Publication);
        else if (work.Status != NavigationGuideStatus.Pending)
        {
            Complete(request, work.Status);
            return false;
        }
        else if (work.AdmissionComplete)
            SetPhase(NavigationGuideRequestPhase.Search);
        return true;
    }

    private bool PublishPlanar(NavigationGuideRequest request, INavigationPlanarGuideRequest planar,
        NavigationPlanarRouteWork work)
    {
        if (_sharedAdmission)
        {
            Complete(request, CheckoutPlanar(planar));
            return false;
        }
        var cache = _context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var status = work.PublishPin(cache, ref _frameRemaining, out var pin);
        if (status == NavigationGuideStatus.Pending)
            return true;
        if (status != NavigationGuideStatus.Success)
        {
            Complete(request, status);
            return false;
        }
        _source.AStar = pin;
        PublishLeader(request);
        return false;
    }

    private NavigationGuideStatus CheckoutPlanar(INavigationPlanarGuideRequest request)
    {
        _source.Transfer = _cohortPending == 1;
        var pathing = _context.Pathing;
        var status = pathing.NavigationAStarAdmissionGate.PayloadCache.TryCheckoutPlanarGuide(
            pathing.NavigationGraphStore, _source, request.PlanarQuery.Start, out var guide);
        if (status == NavigationGuideStatus.Success)
            request.PlanarGuide = guide;
        return status;
    }

    internal bool TryTakePlanarGuide<T>(T request, out NavigationGuideLease guide)
        where T : NavigationGuideRequest, INavigationPlanarGuideRequest
    {
        lock (_sync)
        {
            guide = default;
            if (request.Owner != this || request.PlanarGuide is not NavigationGuideLease candidate)
                return false;
            request.Status = IsWorldCurrent(request) ? candidate.Status : NavigationGuideStatus.Stale;
            bool transferred = request.Status == NavigationGuideStatus.Success;
            if (transferred)
            {
                guide = candidate;
                request.PlanarGuide = null;
            }
            Release(request);
            return transferred;
        }
    }
}
