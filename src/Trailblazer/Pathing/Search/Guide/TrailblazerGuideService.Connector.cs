//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

public sealed partial class TrailblazerGuideService
{
    // The caller must own a scheduler validation turn. This bounded ray/copy is atomic.
    internal NavigationGuideStatus TryProveAStarConnector(PathQuery query, Vector3d actualFoot,
        NavigationGuideLease guide)
    {
        EnsureUsable();
        NavigationGuideStatus begin = guide.BeginConnector(out NavigationGuideStep anchor);
        if (begin != NavigationGuideStatus.Success)
            return begin;
        if (anchor.Medium != query.Traversal.StartMedium)
            return NavigationGuideStatus.Unsupported;
        NavigationImmediateRayWorkspace immediate = _context.Pathing.ImmediateRayWorkspace;
        NavigationAStarPayloadCache cache = _context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        lock (immediate.SyncRoot)
        {
            NavigationWorldGraphStore store = _context.Pathing.NavigationGraphStore;
            using NavigationWorldGraphLease? snapshot = store.TryAcquire();
            if (snapshot == null)
                return NavigationGuideStatus.CapacityExceeded;
            NavigationWorldGraph graph = snapshot.Graph;
            if (!graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy policy))
                return NavigationGuideStatus.Stale;
            NavigationRayWork ray = immediate.RayWork;
            NavigationWorkMeter meter = immediate.WorkMeter;
            meter.Reset(query.Budget);
            long reserved = 0;
            try
            {
                ray.Begin(new NavigationRayRequest(_context.World, store, graph, query.Agent, policy,
                    anchor.Medium, actualFoot, anchor.Position, NavigationRayEndpointAllowance.None,
                    NavigationRayChainConstraint.FinishAt(anchor.Address)));
                NavigationRayStatus status;
                do { status = ray.Advance(meter); } while (status == NavigationRayStatus.Pending);
                if (status != NavigationRayStatus.Success)
                    return NavigationGuideStatusMapper.ToPublic(status);
                // FinishAt and successful exclusive ray ownership establish the exact endpoint.
                ray.TryGetSuccessfulDependencies(out NavigationDependencyWorkspace dependencies, out ulong epoch);
                var sort = new NavigationDependencySortWork(dependencies.Components, dependencies.ComponentCount,
                    dependencies.Pages, dependencies.PageCount);
                if (!sort.Advance(meter, meter.RemainingLookupProbes))
                    return NavigationGuideStatus.BudgetExceeded;
                long bytes = NavigationAStarConnectorProof.MetadataBytes
                    + GraphDependencyStamp.GetRetainedBytes(dependencies.ComponentCount, dependencies.PageCount)
                    + policy.RetainedBytes;
                if (!cache.TryReserveEndpointProof(bytes))
                    return NavigationGuideStatus.CapacityExceeded;
                reserved = bytes;
                var capture = new NavigationDependencyStampWork(graph, policy, dependencies.Components,
                    dependencies.ComponentCount, dependencies.Pages, dependencies.PageCount);
                if (!capture.Advance(meter, meter.RemainingLookupProbes))
                    return NavigationGuideStatus.BudgetExceeded;
                System.Diagnostics.Debug.Assert(capture.IsValid,
                    "Successful exclusive ray dependencies are unique, sorted and present in their retained graph.");
                var proof = new NavigationAStarConnectorProof(capture.Result, policy, anchor, actualFoot, epoch);
                return CompleteAStarConnectorProof(guide, proof, ref reserved);
            }
            finally
            {
                cache.ReleaseEndpointProof(ref reserved);
                ray.Reset();
            }
        }
    }

    internal NavigationGuideStatus CompleteAStarConnectorProof(NavigationGuideLease guide,
        NavigationAStarConnectorProof proof, ref long reservation)
    {
        // Preserve stale-proof precedence before installation's separately bounded route validation.
        if (!proof.IsCurrent(_context.World, _context.Pathing.NavigationGraphStore))
            return NavigationGuideStatus.Stale;
        return guide.InstallConnector(proof, ref reservation);
    }
}
