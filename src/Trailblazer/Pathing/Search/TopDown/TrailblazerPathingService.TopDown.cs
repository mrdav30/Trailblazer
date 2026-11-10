//=======================================================================
// TrailblazerPathingService.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;

namespace Trailblazer.Pathing;

/// <content>Proves top-down placement and ordinary legs against the current publication.</content>
public sealed partial class TrailblazerPathingService
{
    /// <summary>Checks whether a top-down circle fits at the exact center and resolves its cell in the named map.</summary>
    /// <remarks>
    /// Coordinates are world X/Z. The map fixes the selected layer and world Y embedding.
    /// Compatible published layers at that embedding can supply neighboring footprint coverage.
    /// Tangency is allowed. Required cells must be present, open for the requested medium and capabilities,
    /// allowed by the area policy, and have sufficient radius clearance. The result is a snapshot address.
    /// </remarks>
    /// <returns>
    /// Success returns the canonical address. InvalidStart means the center or footprint cannot be admitted.
    /// NoMap means the named map is absent, dormant, or spatial. An unavailable policy or changed evidence returns Stale.
    /// </returns>
    /// <exception cref="System.ArgumentException">The map ID or area policy key is invalid.</exception>
    public NavigationGuideStatus ResolveTopDownEndpoint(string mapId, Vector2d center, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities,
        NavigationWorkBudget budget, out NavigationCellAddress address)
    {
        EnsureUsable();
        ChronicleGuard.ThrowIfArgument(string.IsNullOrWhiteSpace(mapId), nameof(mapId));
        return RunTopDownQuery(mapId, null, center, default, radius, areaPolicy, medium, capabilities, budget, out address);
    }

    /// <summary>Validates the swept top-down circle between the centers of two ordinary neighboring cells.</summary>
    /// <remarks>
    /// The addresses must be on materialized top-down layers with the same embedding. Rectangular legs use
    /// cardinal neighbors. Compatible footprints can meet across map boundaries. Exact tangency is allowed.
    /// Every required cell must be physically present and pass the current obstacle, medium, capability,
    /// area, and radius checks. An evaluated leg consumes one edge allowance and bounded lookup and coverage work.
    /// </remarks>
    /// <returns>
    /// Success means the complete leg is clear. NoPath means the leg is unsupported or blocked. InvalidStart and
    /// InvalidEnd identify invalid addresses. NoMap means a named top-down map is unavailable. Stale means the
    /// policy or publication evidence is unavailable or changed.
    /// </returns>
    /// <exception cref="System.ArgumentException">The area policy key is invalid.</exception>
    public NavigationGuideStatus ValidateTopDownLeg(NavigationCellAddress source, NavigationCellAddress destination,
        Fixed64 radius, NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities,
        NavigationWorkBudget budget)
    {
        EnsureUsable();
        if (source.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        if (destination.MapId == null)
            return NavigationGuideStatus.InvalidEnd;
        return RunTopDownQuery(source.MapId, source.Index, default, destination, radius, areaPolicy, medium,
            capabilities, budget, out _);
    }

    /// <summary>Finds an immediate route between cell centers on compatible published top-down maps.</summary>
    /// <remarks>
    /// The caller supplies a buffer for the route, including both endpoints. Success writes the ordered addresses and
    /// exact fixed-point cost. Other outcomes leave the buffer unchanged with zero count and cost. The result describes
    /// one graph snapshot. Both algorithms use ordinary swept-circle legs and charge forward destination entry costs.
    /// </remarks>
    public NavigationGuideStatus FindTopDownRoute(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities, PathAlgorithm algorithm,
        NavigationWorkBudget budget, Span<NavigationCellAddress> route, out int count, out Fixed64 cost)
        => RunTopDownRoute(start, end, radius, areaPolicy, medium, capabilities, algorithm, budget, route,
            out count, out cost, null, out _);

    internal NavigationGuideStatus RunTopDownRoute(NavigationCellAddress start, NavigationCellAddress end, Fixed64 radius,
        NavigationAreaPolicyKey areaPolicy, TraversalMedium medium, TraversalCapability capabilities, PathAlgorithm algorithm,
        NavigationWorkBudget budget, Span<NavigationCellAddress> route, out int count, out Fixed64 cost,
        NavigationAStarPayloadCache? retention, out NavigationTopDownGuideLease guide, bool allowTransitions = false)
    {
        EnsureUsable();
        count = 0;
        cost = default;
        guide = default;
        var status = NavigationTopDownRouteRequest.TryCreate(start, end, radius, areaPolicy, medium, capabilities, algorithm,
            budget, allowTransitions, out var request);
        if (status != NavigationGuideStatus.Success)
            return status;
        return algorithm == PathAlgorithm.AStar
            ? _navigationAStarAdmissionGate.FindTopDown(_context.World, request, route, out count, out cost, retention, out guide)
            : _navigationFlowAdmissionGate.FindTopDown(_context.World, request, route, out count, out cost, retention, out guide);
    }

    private NavigationGuideStatus RunTopDownQuery(string mapId, VoxelIndex? sourceIndex, Vector2d center,
        NavigationCellAddress destination, Fixed64 radius, NavigationAreaPolicyKey areaPolicy, TraversalMedium medium,
        TraversalCapability capabilities, NavigationWorkBudget budget, out NavigationCellAddress address,
        Vector2d? motionTarget = null, NavigationPlanarBinding? controllerBinding = null,
        (string? Start, string? End) preferredMaps = default)
    {
        areaPolicy.Validate(nameof(areaPolicy));
        address = default;
        TraversalMedia media = NavigationCell.ToMedia(medium);
        if (!IsTopDownProfileValid(radius, media, capabilities))
            return NavigationGuideStatus.InvalidProfile;

        NavigationImmediateRayWorkspace immediate = _immediateRayWorkspace;
        lock (immediate.SyncRoot)
        {
            var world = _context.World;
            var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
            using NavigationWorldGraphLease? lease = TryAcquireNavigationGraph();
            if (lease == null)
                return NavigationGuideStatus.CapacityExceeded;
            NavigationWorldGraph graph = lease.Graph;
            NavigationMapInstance source;
            NavigationPlanarBinding binding;
            if (controllerBinding is NavigationPlanarBinding plane)
            {
                var reference = ResolveControllerReferenceMap(graph, mapId, plane, preferredMaps.Start, out source, out binding);
                if (reference != NavigationGuideStatus.Success)
                    return reference;
            }
            else if (!NavigationTopDownWork.TryGetMap(graph, mapId, out source, out binding))
                return NavigationGuideStatus.NoMap;
            if (graph.AreAllStructuralComponentsClosed
                || !graph.AreaCatalog.TryGet(areaPolicy, out NavigationAreaPolicy policy))
                return NavigationGuideStatus.Stale;

            NavigationTopDownWork topDown = immediate.RayWork.Workspace.TopDown;
            immediate.WorkMeter.Reset(budget);
            NavigationGuideStatus status = motionTarget is Vector2d target
                ? topDown.ValidateMotion(world, graph, source, binding, center, target, radius, media,
                    capabilities, policy, immediate.WorkMeter, preferredMaps)
                : sourceIndex is VoxelIndex index
                ? topDown.ValidateLeg(world, graph, source, binding, index, destination, radius, media,
                    capabilities, policy, immediate.WorkMeter)
                : controllerBinding.HasValue
                ? topDown.ResolveControllerStart(world, graph, binding, center, radius, media,
                    capabilities, policy, immediate.WorkMeter, out address, preferredMaps.Start!)
                : topDown.Resolve(world, graph, source, binding, center, radius, media, capabilities, policy,
                    immediate.WorkMeter, out address);
            return FinishTopDownQuery(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence), ref address);
        }
    }

    internal NavigationGuideStatus ValidateTopDownMotion(string mapId, NavigationPlanarBinding binding, Vector2d start,
        Vector2d end, Fixed64 radius, NavigationAreaPolicyKey policy, TraversalMedium medium,
        TraversalCapability capabilities, NavigationWorkBudget budget, (string? Start, string? End) preferredMaps = default) =>
        RunTopDownQuery(mapId, null, start, default, radius, policy, medium, capabilities, budget, out _, end, binding,
            preferredMaps);

    internal NavigationGuideStatus ResolveTopDownControllerStart(string mapId, NavigationPlanarBinding binding,
        Vector2d center, Fixed64 radius, NavigationAreaPolicyKey policy, TraversalMedium medium,
        TraversalCapability capabilities, NavigationWorkBudget budget, out NavigationCellAddress address,
        string preferredMapId) =>
        RunTopDownQuery(mapId, null, center, default, radius, policy, medium, capabilities, budget, out address,
            controllerBinding: binding, preferredMaps: (preferredMapId, null));

    internal bool TryGetTopDownBinding(string mapId, out NavigationPlanarBinding binding)
    {
        using var lease = TryAcquireNavigationGraph();
        binding = default;
        return lease != null && NavigationTopDownWork.TryGetMap(lease.Graph, mapId, out _, out binding);
    }

    internal NavigationCommittedCellResolveStatus ResolveTopDownCommittedCell(string mapId, NavigationPlanarBinding binding,
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
            var status = immediate.RayWork.Workspace.TopDown.FindControllerCell(world, graph, binding, center,
                immediate.WorkMeter, out var map, out grid, out voxel, preferredMapId);
            status = ResolveTopDownSnapshotStatus(status, graph, _navigationGraph.Current, epoch,
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

    internal static bool IsTopDownProfileValid(Fixed64 radius, TraversalMedia media, TraversalCapability capabilities) =>
        radius > Fixed64.Zero && media != TraversalMedia.None && (capabilities & ~NavigationCell.KnownCapabilities) == 0;

    internal static NavigationGuideStatus FinishTopDownQuery(NavigationGuideStatus status,
        NavigationWorldGraph captured, NavigationWorldGraph current,
        (long Spawn, uint Version, ulong Sequence) before, (long Spawn, uint Version, ulong Sequence) after,
        ref NavigationCellAddress address)
    {
        status = ResolveTopDownSnapshotStatus(status, captured, current, before, after);
        if (status == NavigationGuideStatus.Stale)
            address = default;
        return status;
    }

    internal static NavigationGuideStatus ResolveTopDownSnapshotStatus(NavigationGuideStatus status,
        NavigationWorldGraph captured, NavigationWorldGraph current,
        (long Spawn, uint Version, ulong Sequence) before, (long Spawn, uint Version, ulong Sequence) after) =>
        ReferenceEquals(captured, current) && before.Equals(after) ? status : NavigationGuideStatus.Stale;
}
