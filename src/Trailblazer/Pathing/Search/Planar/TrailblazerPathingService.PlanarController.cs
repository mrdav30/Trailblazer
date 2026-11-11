//=======================================================================
// TrailblazerPathingService.PlanarController.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;

namespace Trailblazer.Pathing;

/// <content>Resolves accepted controller centers and occupancy in either native planar mode.</content>
public sealed partial class TrailblazerPathingService
{
    internal bool TryGetPlanarBinding(string mapId, out NavigationPlanarBinding binding)
    {
        using var lease = TryAcquireNavigationGraph();
        binding = default;
        if (lease != null && lease.Graph.TryGetMap(mapId, out var map) && map.IsMaterialized
            && map.Map.PlanarBinding is NavigationPlanarBinding planar)
        {
            binding = planar;
            return true;
        }
        return false;
    }

    internal NavigationCommittedCellResolveStatus ResolvePlanarCommittedCell(string mapId, NavigationPlanarBinding binding,
        Vector2d center, TraversalMedium medium, NavigationAreaPolicyKey? policy, long frame,
        out VoxelGrid? grid, out Voxel? voxel, out NavigationCommittedCellState? state, out string? acceptedMapId,
        string currentMapId, string preferredMapId)
    {
        grid = null;
        voxel = null;
        state = null;
        acceptedMapId = null;
        var immediate = _immediateRayWorkspace;
        lock (immediate.SyncRoot)
        {
            var world = _context.World;
            var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
            using var lease = TryAcquireNavigationGraph();
            if (lease == null)
                return NavigationCommittedCellResolveStatus.Unavailable;
            var graph = lease.Graph;
            if (ResolveControllerReferenceMap(graph, mapId, binding, currentMapId, out _, out _)
                != NavigationGuideStatus.Success)
                return NavigationCommittedCellResolveStatus.NoCell;
            GuideSampleWorkBudget sample = _context.Settings.GuideSampleBudget;
            immediate.WorkMeter.Reset(new NavigationWorkBudget(sample.MaxCurrentNodeLookupProbes, sample.MaxPrismChecks,
                0, 0, 0, 0, 0, 0, 0, 0, 0));
            var status = immediate.RayWork.Workspace.Planar.FindControllerCell(world, graph, binding, center,
                immediate.WorkMeter, out var map, out grid, out voxel, preferredMapId);
            status = ResolvePlanarSnapshotStatus(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence));
            if (status == NavigationGuideStatus.NoPath)
                return NavigationCommittedCellResolveStatus.NoCell;
            if (status != NavigationGuideStatus.Success)
                return NavigationCommittedCellResolveStatus.Unavailable;
            acceptedMapId = map.MapId;
            VoxelIndex index = voxel!.Index;
            if (!map.TryGetSlot(index, out int slot) || !map.TryGetEffectiveCell(slot, out NavigationCell cell))
                return NavigationCommittedCellResolveStatus.NoCell;
            state = new NavigationCommittedCellState(new NavigationCellAddress(map.MapId, index),
                cell.Area, medium, graph.GraphVersion, policy, frame);
            return NavigationCommittedCellResolveStatus.Resolved;
        }
    }

    private static NavigationGuideStatus ResolveControllerReferenceMap(NavigationWorldGraph graph, string boundMapId,
        NavigationPlanarBinding plane, string? preferredMapId, out NavigationMapInstance map, out NavigationPlanarBinding layer)
    {
        layer = plane;
        // An accepted seam handoff can outlive the original map's publication.
        if (preferredMapId != null && !string.Equals(preferredMapId, boundMapId, System.StringComparison.Ordinal)
            && graph.TryGetMap(preferredMapId, out map)
            && NavigationPlanarWork.TryGetControllerLayer(map, plane, out layer))
            return NavigationGuideStatus.Success;
        if (graph.TryGetMap(boundMapId, out map))
        {
            if (map.Map.PlanarBinding != plane)
                return NavigationGuideStatus.Stale;
            if (map.IsMaterialized)
            {
                layer = plane;
                return NavigationGuideStatus.Success;
            }
        }
        return NavigationGuideStatus.NoMap;
    }

}
