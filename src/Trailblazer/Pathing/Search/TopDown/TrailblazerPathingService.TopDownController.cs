//=======================================================================
// TrailblazerPathingService.TopDownController.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

public sealed partial class TrailblazerPathingService
{
    private static NavigationGuideStatus ResolveControllerReferenceMap(NavigationWorldGraph graph, string boundMapId,
        NavigationPlanarBinding plane, string? preferredMapId, out NavigationMapInstance map, out NavigationPlanarBinding layer)
    {
        layer = plane;
        // An accepted seam handoff can outlive the original map's publication.
        if (preferredMapId != null && !string.Equals(preferredMapId, boundMapId, System.StringComparison.Ordinal)
            && graph.TryGetMap(preferredMapId, out map)
            && NavigationTopDownWork.TryGetControllerLayer(map, plane, out layer))
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

    internal bool TryGetTopDownCenter(NavigationCellAddress address, NavigationPlanarBinding plane, out Vector2d center)
    {
        center = default;
        using var lease = TryAcquireNavigationGraph();
        if (lease == null || address.MapId == null || !lease.Graph.TryGetMap(address.MapId, out var map)
            || !NavigationTopDownWork.TryGetControllerLayer(map, plane, out var layer) || address.Index.y != layer.LayerIndex
            || !map.Map.GridBinding.TryGetCellPrism(address.Index, out var prism))
            return false;
        center = prism.Center.ToVector2d();
        return true;
    }
}
