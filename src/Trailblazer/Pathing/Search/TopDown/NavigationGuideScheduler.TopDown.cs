//=======================================================================
// NavigationGuideScheduler.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Uses the shared queue and cohort source pin for top-down routes.</content>
internal sealed partial class NavigationGuideScheduler
{
    private NavigationTopDownRouteRequest _cohortTopDownQuery;
    private NavigationTopDownRouteWork? _topDownWork;

    internal NavigationGuideStatus ScheduleTopDown(in NavigationTopDownRouteRequest query, PathAlgorithm algorithm,
        out NavigationTopDownGuideRequest? request)
    {
        request = null;
        long bytes = query.GetLogicalRetainedBytes();
        lock (_sync)
        {
            ChronicleGuard.ThrowIfDisposed(_closed, nameof(TrailblazerWorldContext));
            if (!HasRequestCapacity(bytes))
                return NavigationGuideStatus.CapacityExceeded;
            var candidate = new NavigationTopDownGuideRequest(this, algorithm, query, bytes)
            { Flavor = NavigationGuideRequestFlavor.TopDown };
            Enqueue(candidate);
            request = candidate;
            return NavigationGuideStatus.Pending;
        }
    }

    private void BeginTopDown(NavigationTopDownGuideRequest request)
    {
        var pathing = _context.Pathing;
        var world = _context.World;
        _topDownWork = request.Algorithm == PathAlgorithm.FlowField
            ? pathing.NavigationFlowAdmissionGate.BeginScheduledTopDown(world, request.TopDownQuery)
            : pathing.NavigationAStarAdmissionGate.BeginScheduledTopDown(world, request.TopDownQuery);
        if (_topDownWork == null)
            Complete(request, NavigationGuideStatus.CapacityExceeded);
        else
            SetPhase(NavigationGuideRequestPhase.Admission);
    }

    // True means the continuation is still active and may need to yield on a drained slice.
    private bool AdvanceTopDown(NavigationTopDownGuideRequest request)
    {
        var work = _topDownWork!;
        if (_phase == NavigationGuideRequestPhase.Publication)
            return PublishTopDown(request, work);
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

    private bool PublishTopDown(NavigationTopDownGuideRequest request, NavigationTopDownRouteWork work)
    {
        if (_sharedAdmission)
        {
            Complete(request, CheckoutTopDown(request));
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

    private NavigationGuideStatus CheckoutTopDown(NavigationTopDownGuideRequest request)
    {
        _source.Transfer = _cohortPending == 1;
        var pathing = _context.Pathing;
        var status = pathing.NavigationAStarAdmissionGate.PayloadCache.TryCheckoutTopDownGuide(
            pathing.NavigationGraphStore, _source, request.TopDownQuery.Start, out var guide);
        if (status == NavigationGuideStatus.Success)
            request.TopDownGuide = guide;
        return status;
    }

    internal bool TryTakeTopDownGuide(NavigationTopDownGuideRequest request, out NavigationTopDownGuideLease guide)
    {
        lock (_sync)
        {
            guide = default;
            if (request.Owner != this || request.TopDownGuide is not NavigationTopDownGuideLease candidate)
                return false;
            request.Status = IsWorldCurrent(request) ? candidate.Status : NavigationGuideStatus.Stale;
            bool transferred = request.Status == NavigationGuideStatus.Success;
            if (transferred)
            {
                guide = candidate;
                request.TopDownGuide = null;
            }
            Release(request);
            return transferred;
        }
    }
}
