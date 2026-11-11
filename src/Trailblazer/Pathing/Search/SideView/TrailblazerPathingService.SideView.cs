//=======================================================================
// TrailblazerPathingService.SideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <content>Proves canonical side-view support anchors and ordinary legs in the current publication.</content>
public sealed partial class TrailblazerPathingService
{
    /// <summary>Resolves the capsule center at an authored Solid support midpoint and proves stationary clearance.</summary>
    /// <remarks>The address selects the support owner, which may differ from the cell containing the center.</remarks>
    public NavigationGuideStatus ResolveSideViewAnchor(NavigationCellAddress address, in NavigationAgentProfile2D profile,
        NavigationAreaPolicyKey areaPolicy, NavigationWorkBudget budget, out Vector2d center) =>
        RunSideViewProof(address, null, profile, areaPolicy, budget, out center);

    /// <summary>Proves an ordinary side-view Solid leg between two authored support anchors.</summary>
    /// <remarks>
    /// Continuous legs require collinear supports with a shared endpoint and equal materialized normals.
    /// Horizontal steps in a rectangular row use the profile's directed step and drop limits.
    /// A step up lifts before crossing. A drop crosses at the source height before descending to support.
    /// </remarks>
    public NavigationGuideStatus ValidateSideViewLeg(NavigationCellAddress source, NavigationCellAddress destination,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, NavigationWorkBudget budget) =>
        RunSideViewProof(source, destination, profile, areaPolicy, budget, out _);

    internal NavigationGuideStatus ResolveSideViewControllerAnchor(NavigationCellAddress address,
        string boundMapId, NavigationPlanarBinding plane, string currentMapId, in NavigationAgentProfile2D profile,
        NavigationAreaPolicyKey areaPolicy, NavigationWorkBudget budget, out Vector2d center) =>
        RunSideViewProof(address, null, profile, areaPolicy, budget, out center, (boundMapId, plane, currentMapId));

    private NavigationGuideStatus RunSideViewProof(NavigationCellAddress address, NavigationCellAddress? destination,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, NavigationWorkBudget budget,
        out Vector2d center, (string MapId, NavigationPlanarBinding Plane, string CurrentMapId)? controller = null)
    {
        EnsureUsable();
        center = default;
        if (address.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        if (destination.HasValue && destination.Value.MapId == null)
            return NavigationGuideStatus.InvalidEnd;
        areaPolicy.Validate(nameof(areaPolicy));
        if (!IsSideViewSolidProfile(profile))
            return NavigationGuideStatus.InvalidProfile;

        var immediate = _immediateRayWorkspace;
        lock (immediate.SyncRoot)
        {
            var world = _context.World;
            var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
            using var lease = TryAcquireNavigationGraph();
            if (lease == null)
                return NavigationGuideStatus.CapacityExceeded;
            var graph = lease.Graph;
            if (!NavigationPlanarWork.TryGetMap(graph, address.MapId, out var map, out var binding, NavigationMode2D.SideView))
                return NavigationGuideStatus.NoMap;
            if (controller is { } shell)
            {
                var reference = ResolveControllerReferenceMap(graph, shell.MapId, shell.Plane, shell.CurrentMapId,
                    out _, out _);
                if (reference != NavigationGuideStatus.Success)
                    return reference;
                if (shell.Plane.EmbeddingY != binding.EmbeddingY)
                    return NavigationGuideStatus.InvalidStart;
            }
            if (graph.AreAllStructuralComponentsClosed || !graph.AreaCatalog.TryGet(areaPolicy, out var policy))
                return NavigationGuideStatus.Stale;

            var work = immediate.RayWork.Workspace.Planar;
            var meter = immediate.WorkMeter;
            meter.Reset(budget);
            var minimum = map.Map.GridBinding.Configuration.BoundsMin;
            var point = new Vector3d(minimum.X, binding.EmbeddingY, minimum.Z);
            var status = work.BindLayers(world, graph, map, binding, point, meter, out int layerCount);
            if (status == NavigationGuideStatus.Success)
                status = destination is NavigationCellAddress target
                    ? work.ValidateSideViewLeg(world, graph, map, binding, address.Index, target, layerCount, profile, policy, meter)
                    : work.ResolveSideViewAnchor(world, graph, map, binding, address.Index, layerCount, profile, policy, meter, out center);
            status = ResolvePlanarSnapshotStatus(status, graph, _navigationGraph.Current, epoch,
                (world.SpawnToken, world.Version, world.ChangeSequence));
            if (status != NavigationGuideStatus.Success)
                center = default;
            return status;
        }
    }

    internal static bool IsSideViewSolidProfile(in NavigationAgentProfile2D profile) =>
        profile.Mode == NavigationMode2D.SideView && (profile.AllowedMedia & TraversalMedia.Solid) != 0;
}
