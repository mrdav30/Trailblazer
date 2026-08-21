//=======================================================================
// TrailblazerGuideService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned API for actionable A* and flow-field guide acquisition.
/// </summary>
public sealed class TrailblazerGuideService
{
    private readonly TrailblazerWorldContext _context;
    internal TrailblazerGuideService(TrailblazerWorldContext context)
    {
        _context = context;
    }

    /// <summary>Proves one cost-neutral graph-direct surface heading.</summary>
    internal NavigationRayStatus TryGetDirectHeading(
        PathQuery query,
        Vector3d actualFoot,
        out Vector3d heading)
    {
        EnsureUsable();
        heading = Vector3d.Zero;
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
                query.Traversal.StartMedium,
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
    /// Requests one graph-backed A* guide for immutable query intent.
    /// </summary>
    public NavigationGuideStatus RequestGuide(
        PathQuery query,
        out NavigationGuideLease? result)
    {
        EnsureUsable();
        result = null;

        if (query.Algorithm != PathAlgorithm.AStar)
        {
            return NavigationGuideStatus.Unsupported;
        }

        NavigationAStarAdmissionGate gate = _context.Pathing.NavigationAStarAdmissionGate;
        NavigationAStarQueryStatus beginStatus = gate.Begin(query, out NavigationAStarBatchWork work);
        if (beginStatus != NavigationAStarQueryStatus.Pending)
            return NavigationGuideStatusMapper.ToPublic(beginStatus);

        using (work)
        {
            while (!work.IsAdmissionComplete)
            {
                work.AdvanceAdmission(
                    query.Budget.MaxLookupProbes,
                    query.Budget.MaxEndpointCandidates);
            }

            while (!work.IsReadyToPublish(inputIndex: 0))
            {
                work.AdvanceSearch(
                    inputIndex: 0,
                    query.Budget.MaxLookupProbes,
                    nodeStepLimit: int.MaxValue,
                    query.Budget.MaxEvaluatedEdges,
                    query.Budget.MaxConnectionLegs);
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

        if (query.Algorithm != PathAlgorithm.FlowField)
        {
            return NavigationGuideStatus.Unsupported;
        }

        NavigationFlowAdmissionGate gate = _context.Pathing.NavigationFlowAdmissionGate;
        NavigationFlowQueryStatus beginStatus = gate.Begin(query, out NavigationFlowBatchWork work);
        if (beginStatus != NavigationFlowQueryStatus.Pending)
            return ToPublic(beginStatus);

        using (work)
        {
            while (!work.IsAdmissionComplete)
            {
                work.AdvanceAdmission(
                    query.Budget.MaxLookupProbes,
                    query.Budget.MaxEndpointCandidates);
            }

            while (!work.IsReadyToPublish(inputIndex: 0))
            {
                work.AdvanceSearch(
                    inputIndex: 0,
                    query.Budget.MaxLookupProbes,
                    nodeStepLimit: int.MaxValue,
                    query.Budget.MaxEvaluatedEdges,
                    query.Budget.MaxConnectionLegs);
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

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerGuideService is bound to an inactive GridWorld.");
    }

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
