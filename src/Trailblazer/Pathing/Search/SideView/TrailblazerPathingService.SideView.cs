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
    /// Adjoining supports use a straight leg when collinear with matching normals. A valley can use
    /// two supported sweeps through an exact intersection of the finite capsule-center tracks.
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

    internal NavigationGuideStatus ValidateSideViewControllerMotion(NavigationCellAddress source,
        NavigationCellAddress destination, string boundMapId, NavigationPlanarBinding plane, string currentMapId,
        Vector2d start, Vector2d end, in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy,
        NavigationWorkBudget budget) => RunSideViewProof(source, destination, profile, areaPolicy, budget, out _,
            (boundMapId, plane, currentMapId), (start, end));

    internal NavigationGuideStatus ValidateSideViewControllerConnector(NavigationCellAddress source,
        string boundMapId, NavigationPlanarBinding plane, string currentMapId, Vector2d origin, Vector2d current,
        Vector2d anchor, Vector2d? roundedTarget, in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy,
        NavigationWorkBudget budget) => RunSideViewProof(source, null, profile, areaPolicy, budget, out _,
            (boundMapId, plane, currentMapId), connector: (origin, current, anchor, roundedTarget));

    private NavigationGuideStatus RunSideViewProof(NavigationCellAddress address, NavigationCellAddress? destination,
        in NavigationAgentProfile2D profile, NavigationAreaPolicyKey areaPolicy, NavigationWorkBudget budget,
        out Vector2d center, (string MapId, NavigationPlanarBinding Plane, string CurrentMapId)? controller = null,
        (Vector2d Start, Vector2d End)? motion = null,
        (Vector2d Origin, Vector2d Current, Vector2d Anchor, Vector2d? RoundedTarget)? connector = null)
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
                return connector.HasValue ? NavigationGuideStatus.Pending : NavigationGuideStatus.CapacityExceeded;
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
            NavigationGuideStatus status;
            if (motion is { } sweep)
            {
                status = work.ValidateRoundedSideViewMotion(world, graph, map, binding, address.Index,
                    destination!.Value, sweep.Start, sweep.End, profile, policy, meter);
            }
            else
            {
                var minimum = map.Map.GridBinding.Configuration.BoundsMin;
                var point = new Vector3d(minimum.X, binding.EmbeddingY, minimum.Z);
                status = work.BindLayers(world, graph, map, binding, point, meter, out int layerCount);
                if (status == NavigationGuideStatus.Success)
                    status = connector is { } join
                        ? work.TraceSideViewConnector(world, graph, map, binding, address.Index, layerCount,
                            join, profile, policy, meter)
                        : destination is NavigationCellAddress target
                        ? work.ValidateSideViewLeg(world, graph, map, binding, address.Index, target, layerCount, profile, policy, meter)
                        : work.ResolveSideViewAnchor(world, graph, map, binding, address.Index, layerCount, profile, policy, meter, out center);
            }
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
