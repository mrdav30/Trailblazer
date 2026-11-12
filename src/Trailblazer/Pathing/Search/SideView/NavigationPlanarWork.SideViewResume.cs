//=======================================================================
// NavigationPlanarWork.SideViewResume.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;

namespace Trailblazer.Pathing;

/// <content>Checks closed witness ownership for controller recovery and profile placement.</content>
internal sealed partial class NavigationPlanarWork
{
    internal NavigationGuideStatus ResolveSideViewResumeSource(GridWorld world, NavigationWorldGraph graph,
        NavigationPlanarBinding plane, Vector2d witness, Vector2d center, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy policy, NavigationWorkMeter meter, string preferredMapId,
        out NavigationCellAddress address, out Vector2d anchor)
    {
        var status = FindSideViewWitness(world, graph, plane, witness, center, profile, policy, meter,
            preferredMapId, null, out address, out anchor);
        return status == NavigationGuideStatus.NoPath ? NavigationGuideStatus.InvalidStart : status;
    }

    private NavigationGuideStatus ValidateReplacementWitness(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance centerMap, NavigationPlanarBinding plane, Vector2d witness, Vector2d center,
        in NavigationAgentProfile2D profile, NavigationAreaPolicy? policy, NavigationWorkMeter meter)
    {
        var status = FindSideViewWitness(world, graph, plane, witness, center, profile, policy, meter,
            centerMap.MapId, centerMap, out _, out _);
        return status == NavigationGuideStatus.NoPath ? NavigationGuideStatus.Success : status;
    }

    private NavigationGuideStatus FindSideViewWitness(GridWorld world, NavigationWorldGraph graph,
        NavigationPlanarBinding plane, Vector2d witness, Vector2d center, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy? policy, NavigationWorkMeter meter, string preferredMapId,
        NavigationMapInstance? replacementMap, out NavigationCellAddress address, out Vector2d anchor)
    {
        address = default;
        anchor = default;
        var point = new Vector3d(witness.X, plane.EmbeddingY, witness.Y);
        int preferred = graph.FindMapOrdinal(preferredMapId);
        int next = preferred < 0 ? 0 : -1;
        NavigationGuideStatus status;
        while ((status = AdvanceControllerLayer(graph, plane, meter, preferred, ref next, out var map, out var layer))
            == NavigationGuideStatus.Success)
        {
            if (replacementMap != null && !AreHorizontalLatticesCompatible(
                replacementMap.Map.GridBinding.Configuration, map.Map.GridBinding.Configuration))
                continue;
            status = BeginContainingCells(world, graph, map, point, meter, out var cursorStatus);
            if (status != NavigationGuideStatus.Success)
                return status;
            while (cursorStatus != GridCoveredAddressCursorStatus.Complete)
            {
                status = ReadContainingCell(world, map, layer, point, meter, ref cursorStatus, out var candidate);
                if (status == NavigationGuideStatus.NoPath)
                    break;
                if (status != NavigationGuideStatus.Success)
                    return status;

                if (replacementMap != null)
                {
                    // A tangent support can be outside the strict coverage traced from the center cell.
                    if (!map.Map.TryGetPlanarSupport(candidate, out var contact) || !IsReplacementContact(contact, center, profile))
                        continue;
                    if (!ReferenceEquals(map, replacementMap))
                    {
                        status = FindReplacementCoverageNeighbor(graph, map, candidate, meter);
                        if (status == NavigationGuideStatus.NoPath)
                            continue;
                        if (status != NavigationGuideStatus.Success)
                            return status;
                    }
                    if (!map.TryGetSlot(candidate, out int slot) || !map.TryGetEffectiveCell(slot, out var cell))
                        return NavigationGuideStatus.InvalidStart;
                    map.TryGetPhysicalState(slot, out bool present, out byte obstacles);
                    if (!(present & (obstacles == 0) & FitsSideViewSupport(contact, cell, profile)
                        & IsPlanarCoveragePassable(cell, TraversalMedia.None, profile.Capabilities, Fixed64.Zero, policy)))
                        return NavigationGuideStatus.InvalidStart;
                    continue;
                }

                // A witness on a footprint edge can belong to several closed cells. Only an
                // authored support whose finite center track accepts the restored pose can seed a route.
                status = ReadSupportAnchor(map, candidate, profile, policy!, meter, NavigationGuideStatus.InvalidStart,
                    out var support, out var candidateAnchor);
                if (status == NavigationGuideStatus.BudgetExceeded)
                    return status;
                if (status != NavigationGuideStatus.Success
                    || !IsSideViewConnectorOrigin(support, candidateAnchor, center, allowRoundedOrigin: true))
                    continue;
                address = new NavigationCellAddress(map.MapId, candidate);
                anchor = candidateAnchor;
                return NavigationGuideStatus.Success;
            }
        }

        return status;
    }

    private NavigationGuideStatus FindReplacementCoverageNeighbor(NavigationWorldGraph graph, NavigationMapInstance map,
        VoxelIndex candidate, NavigationWorkMeter meter)
    {
        // A closed tangent footprint can join the pinned union only through an exact coverage neighbor.
        var target = map.Map.GridBinding;
        var cells = _workspace.BodyTraceCells;
        for (int i = 0; i < cells.Count; i++)
        {
            if (!meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            var covered = cells[i];
            graph.TryGetMapId(covered.ConfigurationKey, out string mapId);
            graph.TryGetMap(mapId, out var source);
            if (GridTracer.ArePlanarCoverageNeighborsOrCoincident(source.Map.GridBinding, covered.Cell.VoxelIndex,
                target, candidate))
                return NavigationGuideStatus.Success;
        }
        return NavigationGuideStatus.NoPath;
    }
}
