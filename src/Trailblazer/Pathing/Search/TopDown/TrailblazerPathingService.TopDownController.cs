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
