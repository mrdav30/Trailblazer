//=======================================================================
// NavigationPlanarWork.TopDown.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;

namespace Trailblazer.Pathing;

/// <content>Proves stationary and swept circles in a top-down domain.</content>
internal sealed partial class NavigationPlanarWork
{
    internal NavigationGuideStatus Resolve(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, Vector2d center, Fixed64 radius,
        TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy,
        NavigationWorkMeter meter, out NavigationCellAddress address)
    {
        var point = new Vector3d(center.X, binding.EmbeddingY, center.Y);
        NavigationGuideStatus status = BindLayers(world, graph, source, binding, point, meter, out int layerCount);
        if (status != NavigationGuideStatus.Success)
        {
            address = default;
            return status;
        }
        return ResolveContainingCells(world, graph, source, binding, point, center, radius, medium, capabilities,
            policy, meter, layerCount, GridCoveredAddressCursorStatus.More, out address);
    }

    private NavigationGuideStatus ResolveContainingCells(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, Vector3d point, Vector2d center, Fixed64 radius,
        TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy, NavigationWorkMeter meter,
        int layerCount, GridCoveredAddressCursorStatus cursorStatus, out NavigationCellAddress address)
    {
        address = default;
        while (cursorStatus != GridCoveredAddressCursorStatus.Complete)
        {
            NavigationGuideStatus status = ReadContainingCell(world, source, binding, point, meter, ref cursorStatus, out var candidate);
            if (status == NavigationGuideStatus.NoPath)
                return NavigationGuideStatus.InvalidStart;
            if (status != NavigationGuideStatus.Success)
                return status;
            WorldVoxelIndex pinned = Pin(source, candidate);
            status = TraceBody(world, graph, layerCount, binding, pinned, pinned, center, center,
                radius, Fixed64.Zero, medium, capabilities, policy, meter, NavigationGuideStatus.InvalidStart);
            if (status == NavigationGuideStatus.Success)
                address = new NavigationCellAddress(source.MapId, candidate);
            if (status != NavigationGuideStatus.InvalidStart)
                return status;
        }
        return NavigationGuideStatus.InvalidStart;
    }

    internal NavigationGuideStatus ValidateLeg(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, VoxelIndex sourceIndex,
        NavigationCellAddress destination, Fixed64 radius, TraversalMedia medium, TraversalCapability capabilities,
        NavigationAreaPolicy policy, NavigationWorkMeter meter)
    {
        if (!TryGetMap(graph, destination.MapId, out var target, out var targetBinding))
            return NavigationGuideStatus.NoMap;
        if (binding.EmbeddingY != targetBinding.EmbeddingY)
            return NavigationGuideStatus.NoPath;
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationGuideStatus.BudgetExceeded;
        if (sourceIndex.y != binding.LayerIndex
            || !source.Map.GridBinding.TryGetCellPrism(sourceIndex, out GridCellPrism from))
            return NavigationGuideStatus.InvalidStart;
        if (destination.Index.y != targetBinding.LayerIndex
            || !target.Map.GridBinding.TryGetCellPrism(destination.Index, out GridCellPrism to))
            return NavigationGuideStatus.InvalidEnd;

        Vector2d start = from.Center.ToVector2d();
        Vector2d end = to.Center.ToVector2d();
        if (!IsOrdinaryNeighbor(from.TopologyKind, start, end))
            return NavigationGuideStatus.NoPath;

        var point = new Vector3d(start.X, binding.EmbeddingY, start.Y);
        NavigationGuideStatus status = BindLayers(world, graph, source, binding, point, meter, out int layerCount);
        if (status != NavigationGuideStatus.Success)
            return status;
        return TraceBody(world, graph, layerCount, binding, Pin(source, sourceIndex),
            Pin(target, destination.Index), start, end, radius, Fixed64.Zero, medium, capabilities, policy, meter,
            NavigationGuideStatus.NoPath);
    }

    internal NavigationGuideStatus ValidateMotion(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance map, NavigationPlanarBinding binding, Vector2d start, Vector2d end, Fixed64 radius,
        TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy, NavigationWorkMeter meter,
        (string? Start, string? End) preferredMaps)
    {
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationGuideStatus.BudgetExceeded;
        NavigationGuideStatus status = FindControllerCell(world, graph, binding, start, meter, out _, out _, out var source,
            preferredMaps.Start);
        if (status != NavigationGuideStatus.Success)
            return status;
        status = FindControllerCell(world, graph, binding, end, meter, out _, out _, out var target, preferredMaps.End);
        if (status != NavigationGuideStatus.Success)
            return status;
        var point = new Vector3d(start.X, binding.EmbeddingY, start.Y);
        status = BindLayers(world, graph, map, binding, point, meter, out int layerCount);
        if (status != NavigationGuideStatus.Success)
            return status;
        return TraceBody(world, graph, layerCount, binding, source!.WorldIndex, target!.WorldIndex,
            start, end, radius, Fixed64.Zero, medium, capabilities, policy, meter, NavigationGuideStatus.NoPath);
    }

}
