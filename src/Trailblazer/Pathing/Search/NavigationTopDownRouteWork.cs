//=======================================================================
// NavigationTopDownRouteWork.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Runs an immediate planar query in a borrowed search workspace and copies its route.</summary>
internal static class NavigationTopDownRouteWork
{
    internal static NavigationGuideStatus Find(GridWorld world, NavigationWorldGraphStore store,
        in NavigationTopDownRouteRequest request, NavigationAStarWorkspace? aStar, NavigationRayWork? ray,
        NavigationFlowFieldWorkspace? flow, Span<NavigationCellAddress> route, out int count, out Fixed64 cost)
    {
        count = 0;
        cost = default;
        var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
        using NavigationWorldGraphLease? lease = store.TryAcquire();
        if (lease == null)
            return NavigationGuideStatus.CapacityExceeded;
        NavigationWorldGraph graph = lease.Graph;
        if (!NavigationTopDownWork.TryGetMap(graph, request.Start.MapId, out var map, out var binding))
            return NavigationGuideStatus.NoMap;
        if (graph.AreAllStructuralComponentsClosed || !graph.AreaCatalog.TryGet(request.AreaPolicy, out var policy))
            return NavigationGuideStatus.Stale;

        NavigationRayWorkspace rayWorkspace = aStar?.RayWorkspace ?? flow!.RayWorkspace;
        var meter = new NavigationWorkMeter(request.Budget);
        var planar = new NavigationTopDownSearch(world, graph, rayWorkspace.TopDown, map, binding, request, policy, meter);
        using var query = new NavigationResolvedPathQuery();
        NavigationGuideStatus status = planar.Admit(epoch.ChangeSequence, query);
        bool flowSearch = status == NavigationGuideStatus.Success && flow != null;
        try
        {
            int length = 0;
            Fixed64 total = default;
            if (status == NavigationGuideStatus.Success)
            {
                status = aStar != null
                    ? RunAStar(world, store, query, aStar, ray!, out length, out total)
                    : RunFlow(world, query, flow!, out length, out total);
            }

            // Missing footprint cells can lack a semantic page. Bind the whole snapshot for this detached route.
            status = TrailblazerPathingService.ResolveTopDownSnapshotStatus(status, graph, store.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence));
            if (status != NavigationGuideStatus.Success)
                return status;
            if (route.Length < length)
                return NavigationGuideStatus.CapacityExceeded;

            if (aStar != null)
            {
                for (int i = 0; i < length; i++)
                    graph.TryGetNodeAddress(aStar.PathNodes[i].Node, out route[i]);
            }
            else
            {
                for (int i = 0; i < length; i++)
                    route[i] = flow!.GetRecord(flow.SettledSlots[i]).Address;
            }
            count = length;
            cost = total;
            return NavigationGuideStatus.Success;
        }
        finally
        {
            // Do not pass planar cleanup work to the next scheduled spatial search.
            if (flowSearch)
                flow!.ResetSearch();
        }
    }

    private static NavigationGuideStatus RunAStar(GridWorld world, NavigationWorldGraphStore store,
        NavigationResolvedPathQuery query, NavigationAStarWorkspace workspace, NavigationRayWork ray,
        out int count, out Fixed64 cost)
    {
        count = 0;
        cost = default;
        using var work = new NavigationSurfaceAStarWork(world, store, query, workspace, ray, 0);
        while (work.Status == NavigationSurfaceAStarStatus.Pending)
            work.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        if (work.Status != NavigationSurfaceAStarStatus.Success)
            return NavigationGuideStatusMapper.ToPublic(NavigationAStarQueryWork.MapSearchStatus(work.Status));

        count = workspace.PathNodeCount;
        workspace.NodeTable.TryGetSlot(workspace.PathNodes[count - 1], out int slot);
        cost = workspace.NodeTable.GetRecord(slot).Cost;
        return NavigationGuideStatus.Success;
    }

    private static NavigationGuideStatus RunFlow(GridWorld world, NavigationResolvedPathQuery query,
        NavigationFlowFieldWorkspace workspace, out int count, out Fixed64 cost)
    {
        count = 0;
        cost = default;
        NavigationTopDownSearch planar = query.TopDown!;
        NavigationMediumStateRef origin = new(query.Start.Node, query.StartMedium);
        NavigationCellAddress destination = query.End.Address;
        using var work = new NavigationFlowFieldWork(world, query, workspace);
        while (work.Status == NavigationFlowFieldStatus.Pending)
            work.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        if (work.Status != NavigationFlowFieldStatus.Success)
            return NavigationGuideStatusMapper.ToPublic(work.Status);

        workspace.TryGetSlot(origin, out int slot);
        cost = workspace.GetRecord(slot).IntegrationCost;
        // The result is detached, so reuse the settled-slot buffer to stage its selected successor chain.
        for (int i = 0; ; i++)
        {
            System.Diagnostics.Debug.Assert(i < workspace.SettledCount,
                "Positive leg costs bound the successor chain by the number of settled states.");
            workspace.SettledSlots[i] = slot;
            ref NavigationFlowFieldSearchNode current = ref workspace.GetRecord(slot);
            if (current.Address.Equals(destination))
            {
                count = i + 1;
                return NavigationGuideStatus.Success;
            }
            if (!query.Meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool foundNode = planar.TryGetNode(current.SelectedEdge.Target.Index, out NavigationNodeRef next);
            bool foundSlot = workspace.TryGetSlot(new NavigationMediumStateRef(next, origin.Medium), out slot);
            System.Diagnostics.Debug.Assert(foundNode && foundSlot && workspace.GetRecord(slot).Closed
                && workspace.GetRecord(slot).IntegrationCost < current.IntegrationCost);
        }
    }
}
