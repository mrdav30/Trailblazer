//=======================================================================
// TrailblazerPathingService.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <content>Resolves strict top-down placement against the current publication.</content>
public sealed partial class TrailblazerPathingService
{
    private NavigationTopDownEndpointWork? _topDownEndpoint;

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
            if (!graph.TryGetMap(mapId, out NavigationMapInstance source)
                || !source.IsMaterialized
                || source.Map.PlanarBinding is not NavigationPlanarBinding binding)
            {
                return NavigationGuideStatus.NoMap;
            }
            if (graph.AreAllStructuralComponentsClosed
                || !graph.AreaCatalog.TryGet(areaPolicy, out NavigationAreaPolicy policy))
                return NavigationGuideStatus.Stale;

            _topDownEndpoint ??= new NavigationTopDownEndpointWork(immediate.RayWork.Workspace);
            immediate.WorkMeter.Reset(budget);
            NavigationGuideStatus status = _topDownEndpoint.Resolve(world, graph, source, binding, center,
                radius, media, capabilities, policy, immediate.WorkMeter, out address);
            return FinishTopDownEndpoint(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence), ref address);
        }
    }

    internal static NavigationGuideStatus FinishTopDownEndpoint(NavigationGuideStatus status,
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
