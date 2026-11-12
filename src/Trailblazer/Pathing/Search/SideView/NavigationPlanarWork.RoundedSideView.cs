//=======================================================================
// NavigationPlanarWork.RoundedSideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationPlanarWork
{
    internal NavigationGuideStatus ValidateRoundedSideViewMotion(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, VoxelIndex sourceIndex,
        NavigationCellAddress destination, Vector2d start, Vector2d end, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy policy, NavigationWorkMeter meter)
    {
        if (!TryGetMap(graph, destination.MapId, out var target, out var targetBinding, NavigationMode2D.SideView)
            || targetBinding.EmbeddingY != binding.EmbeddingY)
            return NavigationGuideStatus.Stale;
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationGuideStatus.BudgetExceeded;

        var from = (Map: source, Index: sourceIndex);
        var to = (Map: target, Index: destination.Index);
        var status = ResolveRoundedPin(from, to, start, profile, meter, out var startPin);
        if (status != NavigationGuideStatus.Success)
            return status;
        status = ResolveRoundedPin(to, from, end, profile, meter, out var endPin);
        if (status != NavigationGuideStatus.Success)
            return status;
        return TraceBoundMotion(world, graph, source, binding, startPin, endPin, start, end, profile.Radius,
            profile.AxisLength, TraversalMedia.Solid, profile.Capabilities, policy, meter);
    }

    private static NavigationGuideStatus ResolveRoundedPin(
        (NavigationMapInstance Map, VoxelIndex Index) first, (NavigationMapInstance Map, VoxelIndex Index) second,
        Vector2d center, in NavigationAgentProfile2D profile, NavigationWorkMeter meter, out WorldVoxelIndex pin)
    {
        pin = default;
        // Preserve the route lattice. A free center lookup could seed the union from an overlapping grid.
        for (int i = 0; i < 2; i++)
        {
            var owner = i == 0 ? first : second;
            if (!meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            var map = owner.Map.Map;
            bool hasPrism = map.GridBinding.TryGetCellPrism(owner.Index, out var prism);
            if (!(hasPrism & map.ContainsGridIndex(owner.Index)))
                return NavigationGuideStatus.Stale;
            if (CapsuleTouchesFootprint(prism, center, profile))
            {
                pin = Pin(owner.Map, owner.Index);
                return NavigationGuideStatus.Success;
            }
        }

        // shortcut: require contact with a retained owner until pin selection can preserve the lattice beyond them.
        return NavigationGuideStatus.NoPath;
    }

    private static bool CapsuleTouchesFootprint(in GridCellPrism prism,
        Vector2d center, in NavigationAgentProfile2D profile)
    {
        Span<Vector2d> footprint = stackalloc Vector2d[6];
        Vector2d origin = prism.Center.ToVector2d();
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            footprint[i] = prism.GetFootprintVertex(i) - origin;
        return FixedConvex2dRelations.IntersectsUprightCapsule(center, profile.AxisLength, profile.Radius,
            origin, footprint[..prism.FootprintVertexCount]);
    }
}
