//=======================================================================
// TrailblazerPathingService.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <content>Proves top-down placement and ordinary legs against the current publication.</content>
public sealed partial class TrailblazerPathingService
{
    private NavigationTopDownWork? _topDown;

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

    private NavigationGuideStatus RunTopDownQuery(string mapId, VoxelIndex? sourceIndex, Vector2d center,
        NavigationCellAddress destination, Fixed64 radius, NavigationAreaPolicyKey areaPolicy, TraversalMedium medium,
        TraversalCapability capabilities, NavigationWorkBudget budget, out NavigationCellAddress address)
    {
        areaPolicy.Validate(nameof(areaPolicy));
        address = default;
        TraversalMedia media = NavigationCell.ToMedia(medium);
        if (radius <= Fixed64.Zero || media == TraversalMedia.None
            || (capabilities & ~NavigationCell.KnownCapabilities) != 0)
        {
            return NavigationGuideStatus.InvalidProfile;
        }

        NavigationImmediateRayWorkspace immediate = _immediateRayWorkspace;
        lock (immediate.SyncRoot)
        {
            var world = _context.World;
            var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
            using NavigationWorldGraphLease? lease = TryAcquireNavigationGraph();
            if (lease == null)
                return NavigationGuideStatus.CapacityExceeded;
            NavigationWorldGraph graph = lease.Graph;
            if (!NavigationTopDownWork.TryGetMap(graph, mapId, out var source, out var binding))
                return NavigationGuideStatus.NoMap;
            if (graph.AreAllStructuralComponentsClosed
                || !graph.AreaCatalog.TryGet(areaPolicy, out NavigationAreaPolicy policy))
                return NavigationGuideStatus.Stale;

            _topDown ??= new NavigationTopDownWork(immediate.RayWork.Workspace);
            immediate.WorkMeter.Reset(budget);
            NavigationGuideStatus status = sourceIndex is VoxelIndex index
                ? _topDown.ValidateLeg(world, graph, source, binding, index, destination, radius, media,
                    capabilities, policy, immediate.WorkMeter)
                : _topDown.Resolve(world, graph, source, binding, center, radius, media, capabilities, policy,
                    immediate.WorkMeter, out address);
            return FinishTopDownQuery(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence), ref address);
        }
    }

    internal static NavigationGuideStatus FinishTopDownQuery(NavigationGuideStatus status,
        NavigationWorldGraph captured, NavigationWorldGraph current,
        (long Spawn, uint Version, ulong Sequence) before, (long Spawn, uint Version, ulong Sequence) after,
        ref NavigationCellAddress address)
    {
        if (ReferenceEquals(captured, current) && before.Equals(after))
            return status;
        address = default;
        return NavigationGuideStatus.Stale;
    }
}
