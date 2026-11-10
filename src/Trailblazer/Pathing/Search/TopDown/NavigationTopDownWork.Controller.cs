//=======================================================================
// NavigationTopDownWork.Controller.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <content>Resolves accepted controller centers within the bound plane.</content>
internal sealed partial class NavigationTopDownWork
{
    internal NavigationGuideStatus FindControllerCell(GridWorld world, NavigationWorldGraph graph,
        NavigationPlanarBinding plane, Vector2d center, NavigationWorkMeter meter,
        out NavigationMapInstance map, out VoxelGrid? grid, out Voxel? voxel, string? preferredMapId = null)
    {
        grid = null;
        voxel = null;
        int preferred = preferredMapId == null ? -1 : graph.FindMapOrdinal(preferredMapId);
        int next = preferred < 0 ? 0 : -1;
        NavigationGuideStatus status;
        while ((status = AdvanceControllerLayer(graph, plane, meter, preferred, ref next, out map, out var layer))
            == NavigationGuideStatus.Success)
        {
            status = FindContainingCell(world, graph, map, layer, center, meter, out grid, out voxel);
            if (status != NavigationGuideStatus.NoPath)
                return status;
        }
        return status;
    }

    internal NavigationGuideStatus ResolveControllerStart(GridWorld world, NavigationWorldGraph graph,
        NavigationPlanarBinding plane, Vector2d center, Fixed64 radius, TraversalMedia medium,
        TraversalCapability capabilities, NavigationAreaPolicy policy, NavigationWorkMeter meter,
        out NavigationCellAddress address, string preferredMapId)
    {
        address = default;
        int preferred = graph.FindMapOrdinal(preferredMapId);
        int next = preferred < 0 ? 0 : -1;
        int layerCount = 0;
        var point = new Vector3d(center.X, plane.EmbeddingY, center.Y);
        NavigationGuideStatus status;
        while ((status = AdvanceControllerLayer(graph, plane, meter, preferred, ref next, out var map, out var layer))
            == NavigationGuideStatus.Success)
        {
            var cursorStatus = GridCoveredAddressCursorStatus.More;
            // Only the selected map's local address cursor changes between candidates.
            status = layerCount == 0
                ? BindLayers(world, graph, map, layer, point, meter, out layerCount)
                : BeginContainingCells(world, graph, map, point, meter, out cursorStatus);
            if (status != NavigationGuideStatus.Success)
                return status;
            status = ResolveContainingCells(world, graph, map, layer, point, center, radius, medium, capabilities,
                policy, meter, layerCount, cursorStatus, out address);
            if (status != NavigationGuideStatus.InvalidStart)
                return status;
        }
        return status == NavigationGuideStatus.NoPath ? NavigationGuideStatus.InvalidStart : status;
    }

    private static NavigationGuideStatus AdvanceControllerLayer(NavigationWorldGraph graph, NavigationPlanarBinding plane,
        NavigationWorkMeter meter, int preferred, ref int next, out NavigationMapInstance map, out NavigationPlanarBinding layer)
    {
        layer = default;
        while (next < graph.MapCount)
        {
            int ordinal = next++;
            if (ordinal == preferred)
                continue;
            if (!meter.TryConsumeLookupProbes(1))
            {
                map = null!;
                return NavigationGuideStatus.BudgetExceeded;
            }
            map = graph.GetInstance(ordinal < 0 ? preferred : ordinal);
            if (TryGetControllerLayer(map, plane, out layer))
                return NavigationGuideStatus.Success;
        }
        map = null!;
        return NavigationGuideStatus.NoPath;
    }

    internal static bool TryGetControllerLayer(NavigationMapInstance map, NavigationPlanarBinding plane,
        out NavigationPlanarBinding layer)
    {
        if (map.IsMaterialized && map.Map.PlanarBinding is NavigationPlanarBinding binding
            && binding.Mode == plane.Mode && binding.EmbeddingY == plane.EmbeddingY)
        {
            layer = binding;
            return true;
        }
        layer = default;
        return false;
    }
}
