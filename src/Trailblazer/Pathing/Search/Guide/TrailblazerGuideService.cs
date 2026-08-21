//=======================================================================
// TrailblazerGuideService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned API for guide request, return, cache invalidation, and cache diagnostics.
/// </summary>
public sealed class TrailblazerGuideService
{
    private readonly TrailblazerWorldContext _context;
    private readonly PathingWorldState _state;

    internal TrailblazerGuideService(TrailblazerWorldContext context, PathingWorldState state)
    {
        _context = context;
        _state = state;
    }

    /// <summary>Proves one cost-neutral graph-direct surface heading.</summary>
    internal NavigationRayStatus TryGetDirectHeading(
        PathQuery query,
        Vector3d actualFoot,
        out Vector3d heading)
    {
        EnsureUsable();
        heading = Vector3d.Zero;
        if (query.Traversal.StartDomain != TraversalDomain.Surface
            || query.Traversal.TargetDomain != TraversalDomain.Surface
            || query.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return NavigationRayStatus.Blocked;
        }

        NavigationImmediateRayWorkspace immediate = _context.Pathing.ImmediateRayWorkspace;
        lock (immediate.SyncRoot)
        {
            NavigationWorldGraphStore store = _context.Pathing.NavigationGraphStore;
            using NavigationWorldGraphLease? lease = store.TryAcquire();
            if (lease == null)
                return NavigationRayStatus.CapacityExceeded;
            NavigationWorldGraph graph = lease.Graph;
            if (!graph.AreaCatalog.TryGet(
                    query.AreaPolicy,
                    out NavigationAreaPolicy? areaPolicy)
                || areaPolicy == null)
            {
                return NavigationRayStatus.Stale;
            }

            NavigationRayWork ray = immediate.RayWork;
            ray.Begin(new NavigationRayRequest(
                _context.World,
                store,
                graph,
                query.Agent,
                areaPolicy,
                TraversalMedium.Solid,
                actualFoot,
                query.End.Position,
                NavigationRayEndpointAllowance.None));
            NavigationWorkMeter meter = immediate.WorkMeter;
            meter.Reset(query.Budget);
            NavigationRayStatus status;
            NavigationRayResult result;
            try
            {
                do
                {
                    status = ray.Advance(meter);
                }
                while (status == NavigationRayStatus.Pending);
                result = ray.Result;
            }
            finally
            {
                ray.Reset();
            }
            if (status != NavigationRayStatus.Success)
                return status;
            if (!result.IsSemanticCostNeutral)
                return NavigationRayStatus.Blocked;
            if (!Vector3d.TrySubtract(query.End.Position, actualFoot, out Vector3d delta))
                return NavigationRayStatus.CostOverflow;
            if (delta != Vector3d.Zero)
                heading = delta.Normalized;
            return NavigationRayStatus.Success;
        }
    }

    /// <summary>
    /// Requests one graph-backed surface A* guide for immutable query intent.
    /// </summary>
    public NavigationGuideStatus RequestGuide(
        PathQuery query,
        out NavigationGuideLease? result)
    {
        EnsureUsable();
        result = null;

        if (query.Algorithm != PathAlgorithm.AStar
            || query.AllowTransitions
            || query.Traversal.StartDomain == TraversalDomain.Volume
            || query.Traversal.TargetDomain == TraversalDomain.Volume
            || query.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return NavigationGuideStatus.Unsupported;
        }

        NavigationAStarAdmissionGate gate = _context.Pathing.NavigationAStarAdmissionGate;
        NavigationAStarQueryStatus beginStatus = gate.Begin(query, out NavigationAStarBatchWork work);
        if (beginStatus != NavigationAStarQueryStatus.Pending)
            return NavigationGuideStatusMapper.ToPublic(beginStatus);

        using (work)
        {
            work.AdvanceAdmission(
                query.Budget.MaxLookupProbes,
                query.Budget.MaxEndpointCandidates);
            if (!work.IsAdmissionComplete)
                return NavigationGuideStatus.BudgetExceeded;

            if (!work.IsReadyToPublish(inputIndex: 0))
            {
                work.AdvanceSearch(
                    inputIndex: 0,
                    query.Budget.MaxLookupProbes,
                    nodeStepLimit: int.MaxValue,
                    query.Budget.MaxEvaluatedEdges,
                    query.Budget.MaxConnectionLegs);
                if (!work.IsReadyToPublish(inputIndex: 0))
                    return NavigationGuideStatus.BudgetExceeded;
            }

            work.PublishReadyPrefix(maximumCount: 1);
            NavigationAStarQueryStatus status = work.GetStatus(inputIndex: 0);
            if (status != NavigationAStarQueryStatus.Success)
                return NavigationGuideStatusMapper.ToPublic(status);

            NavigationAStarPayloadLease payloadLease = work.TakeResult(inputIndex: 0);
            NavigationAStarQueryStatus guideStatus = gate.PayloadCache.TryCreateGuide(
                _context.Pathing.NavigationGraphStore,
                payloadLease,
                out NavigationAStarGuideLease? guide);
            if (guideStatus != NavigationAStarQueryStatus.Success || guide == null)
                return NavigationGuideStatusMapper.ToPublic(guideStatus);

            result = new NavigationGuideLease(guide);
            return NavigationGuideStatus.Success;
        }
    }

    /// <summary>
    /// Requests one graph-backed destination-centric flow field for immutable query intent.
    /// </summary>
    public NavigationGuideStatus RequestFlowField(
        PathQuery query,
        out NavigationFlowFieldLease? result)
    {
        EnsureUsable();
        result = null;

        if (query.Algorithm != PathAlgorithm.FlowField
            || query.AllowTransitions
            || query.Traversal.StartDomain != TraversalDomain.Surface
            || query.Traversal.TargetDomain != TraversalDomain.Surface
            || query.Traversal.CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return NavigationGuideStatus.Unsupported;
        }

        NavigationFlowAdmissionGate gate = _context.Pathing.NavigationFlowAdmissionGate;
        NavigationFlowQueryStatus beginStatus = gate.Begin(query, out NavigationFlowBatchWork work);
        if (beginStatus != NavigationFlowQueryStatus.Pending)
            return ToPublic(beginStatus);

        using (work)
        {
            work.AdvanceAdmission(
                query.Budget.MaxLookupProbes,
                query.Budget.MaxEndpointCandidates);
            if (!work.IsAdmissionComplete)
                return NavigationGuideStatus.BudgetExceeded;

            if (!work.IsReadyToPublish(inputIndex: 0))
            {
                work.AdvanceSearch(
                    inputIndex: 0,
                    query.Budget.MaxLookupProbes,
                    nodeStepLimit: int.MaxValue,
                    query.Budget.MaxEvaluatedEdges,
                    query.Budget.MaxConnectionLegs);
                if (!work.IsReadyToPublish(inputIndex: 0))
                    return NavigationGuideStatus.BudgetExceeded;
            }

            work.PublishReadyPrefix(maximumCount: 1);
            NavigationFlowQueryStatus status = work.GetStatus(inputIndex: 0);
            if (status != NavigationFlowQueryStatus.Success)
                return ToPublic(status);

            using NavigationFlowQueryResult flowResult = work.TakeResult(inputIndex: 0);
            NavigationGuideStatus guideStatus = gate.PayloadCache.TryCreateGuide(
                _context.Pathing.NavigationGraphStore,
                flowResult,
                out NavigationFlowFieldLease guide);
            if (guideStatus != NavigationGuideStatus.Success)
            {
                guide.Dispose();
                return guideStatus;
            }

            result = guide;
            return NavigationGuideStatus.Success;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalVolumeGuideCount"/>
    public int TotalVolumeGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.TotalVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseVolumeGuideCount"/>
    public int InUseVolumeGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.InUseVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.IsPooling"/>
    public bool IsPooling
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.IsPooling;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.AnyInUse"/>
    public bool AnyInUse
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.AnyInUse;
        }
    }

    /// <summary>
    /// Requests a typed guide for the supplied validated path request.
    /// </summary>
    public bool RequestGuide<T>(IPathRequest request, [NotNullWhen(true)] out T? result)
        where T : class, IGuide
    {
        if (!IsRequestOwnedByThisContext(request))
        {
            result = null;
            return false;
        }

        using (EnterUsableState())
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.RequestGuide(IPathRequest,out IGuide?)"/>
    public bool RequestGuide(IPathRequest request, [NotNullWhen(true)] out IGuide? result)
    {
        if (!IsRequestOwnedByThisContext(request))
        {
            result = null;
            return false;
        }

        using (EnterUsableState())
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.ReturnGuide(IGuide?,bool)"/>
    public void ReturnGuide(IGuide? guide, bool dispose = false)
    {
        if (guide == null)
            return;

        using (EnterUsableState())
            PathGuideFactory.ReturnGuide(guide, dispose);
    }

    /// <inheritdoc cref="PathGuideFactory.InvalidateCacheFor(string)"/>
    public void InvalidateCacheFor(string chartKey)
    {
        using (EnterUsableState())
            PathGuideFactory.InvalidateCacheFor(chartKey);
    }

    /// <inheritdoc cref="PathGuideFactory.FlushCache(bool)"/>
    public void FlushCache(bool force = false)
    {
        using (EnterUsableState())
            PathGuideFactory.FlushCache(force);
    }

    internal void CullExpiredGuides(int currentFrame)
    {
        using (EnterUsableState())
            PathGuideFactory.CullExpiredGuides(currentFrame);
    }

    internal void InvalidateVolumeCache()
    {
        using (EnterUsableState())
            PathGuideFactory.InvalidateVolumeCache();
    }

    private IDisposable EnterUsableState()
    {
        EnsureUsable();
        return PathManager.EnterState(_state);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerGuideService is bound to an inactive GridWorld.");
    }

    private bool IsRequestOwnedByThisContext(IPathRequest request) =>
        request != null && ReferenceEquals(request.Context, _context);

    private static NavigationGuideStatus ToPublic(NavigationFlowQueryStatus status) => status switch
    {
        NavigationFlowQueryStatus.Success => NavigationGuideStatus.Success,
        NavigationFlowQueryStatus.Unsupported => NavigationGuideStatus.Unsupported,
        NavigationFlowQueryStatus.NoMap => NavigationGuideStatus.NoMap,
        NavigationFlowQueryStatus.InvalidProfile => NavigationGuideStatus.InvalidProfile,
        NavigationFlowQueryStatus.InvalidStart => NavigationGuideStatus.InvalidStart,
        NavigationFlowQueryStatus.InvalidEnd => NavigationGuideStatus.InvalidEnd,
        NavigationFlowQueryStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationFlowQueryStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationFlowQueryStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationFlowQueryStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        NavigationFlowQueryStatus.Stale => NavigationGuideStatus.Stale,
        _ => NavigationGuideStatus.Stale
    };
}
