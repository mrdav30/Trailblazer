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
        string currentMapId, string preferredMapId, in NavigationAgentProfile2D? replacement = null,
        NavigationWorkBudget replacementBudget = default, Vector2d? replacementWitness = null,
        Vector2d? detachedReplacementOrigin = null)
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
                return replacement.HasValue ? NavigationCommittedCellResolveStatus.Unavailable : NavigationCommittedCellResolveStatus.NoCell;
            GuideSampleWorkBudget sample = _context.Settings.GuideSampleBudget;
            var prismBudget = GetPlanarPrismLookupBudget(sample);
            immediate.WorkMeter.Reset(prismBudget);
            var work = immediate.RayWork.Workspace.Planar;
            var status = work.FindControllerCell(world, graph, binding, center,
                immediate.WorkMeter, out var map, out grid, out voxel, preferredMapId, replacement.HasValue);
            if (status == NavigationGuideStatus.NoPath && detachedReplacementOrigin is Vector2d origin && origin != center)
            {
                // Both centers must be detached in the same publication before the host-only fallback.
                immediate.WorkMeter.Reset(prismBudget);
                if (work.FindControllerCell(world, graph, binding, origin, immediate.WorkMeter,
                    out _, out _, out _, preferredMapId, requireAuthoredCell: true) != NavigationGuideStatus.NoPath)
                    return NavigationCommittedCellResolveStatus.Unavailable;
            }
            status = ResolvePlanarSnapshotStatus(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence));
            if (status == NavigationGuideStatus.NoPath)
                return NavigationCommittedCellResolveStatus.NoCell;
            if (status != NavigationGuideStatus.Success)
                return NavigationCommittedCellResolveStatus.Unavailable;
            acceptedMapId = map.MapId;
            VoxelIndex index = voxel!.Index;
            if (!map.TryGetSlot(index, out int slot) || !map.TryGetEffectiveCell(slot, out NavigationCell cell))
                return replacement.HasValue ? NavigationCommittedCellResolveStatus.Unavailable : NavigationCommittedCellResolveStatus.NoCell;
            if (replacement is NavigationAgentProfile2D profile)
            {
                NavigationAreaPolicy? rules = null;
                if (policy is NavigationAreaPolicyKey key && !graph.AreaCatalog.TryGet(key, out rules))
                    return NavigationCommittedCellResolveStatus.Unavailable;
                immediate.WorkMeter.Reset(policy.HasValue ? replacementBudget : new NavigationWorkBudget(
                    sample.MaxCurrentNodeLookupProbes, 0, 0, 0, 0, 0, 0, 0, 0,
                    _context.Settings.QueryLimits.RayWorkspaceCoveredAddressCapacity, 0));
                var pin = voxel.WorldIndex;
                status = work.TraceBoundMotion(world, graph, map, map.Map.PlanarBinding!.Value,
                    pin, pin, center, center, profile.Radius, profile.AxisLength, NavigationCell.ToMedia(medium),
                    profile.Capabilities, rules, immediate.WorkMeter, (map, profile, replacementWitness, prismBudget));
                if (ResolvePlanarSnapshotStatus(status, graph, _navigationGraph.Current, epoch,
                    (world.SpawnToken, world.Version, world.ChangeSequence)) != NavigationGuideStatus.Success)
                    return NavigationCommittedCellResolveStatus.Unavailable;
            }
            state = new NavigationCommittedCellState(new NavigationCellAddress(map.MapId, index),
                cell.Area, medium, graph.GraphVersion, policy, frame);
            return NavigationCommittedCellResolveStatus.Resolved;
        }
    }

    private static NavigationWorkBudget GetPlanarPrismLookupBudget(GuideSampleWorkBudget sample) =>
        new(sample.MaxCurrentNodeLookupProbes, sample.MaxPrismChecks, 0, 0, 0, 0, 0, 0, 0, 0, 0);

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
