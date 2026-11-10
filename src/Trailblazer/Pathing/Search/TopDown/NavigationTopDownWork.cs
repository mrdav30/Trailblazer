//=======================================================================
// NavigationTopDownWork.cs
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

/// <summary>Proves stationary and swept circles against one published top-down domain.</summary>
internal sealed partial class NavigationTopDownWork
{
    private readonly GridCoveredAddressCursor _cursor;
    private readonly GridPlanarLayer[] _layers;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationTopDownWork(NavigationRayWorkspace workspace)
    {
        _workspace = workspace;
        _cursor = new GridCoveredAddressCursor(workspace.MapCapacity);
        _layers = new GridPlanarLayer[workspace.MapCapacity];
    }

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
            status = TraceCircle(world, graph, layerCount, binding.EmbeddingY, pinned, pinned, center, center,
                radius, medium, capabilities, policy, meter, NavigationGuideStatus.InvalidStart);
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
        return TraceCircle(world, graph, layerCount, binding.EmbeddingY, Pin(source, sourceIndex),
            Pin(target, destination.Index), start, end, radius, medium, capabilities, policy, meter,
            NavigationGuideStatus.NoPath);
    }

    internal static bool TryGetMap(NavigationWorldGraph graph, string mapId, out NavigationMapInstance instance,
        out NavigationPlanarBinding binding)
    {
        if (graph.TryGetMap(mapId, out instance) && instance.IsMaterialized
            && instance.Map.PlanarBinding is NavigationPlanarBinding planar)
        {
            binding = planar;
            return true;
        }
        binding = default;
        return false;
    }

    private NavigationGuideStatus FindContainingCell(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance map, NavigationPlanarBinding binding, Vector2d center, NavigationWorkMeter meter,
        out VoxelGrid? grid, out Voxel? voxel)
    {
        grid = null;
        voxel = null;
        var point = new Vector3d(center.X, binding.EmbeddingY, center.Y);
        NavigationGuideStatus status = BeginContainingCells(world, graph, map, point, meter, out var cursorStatus);
        if (status != NavigationGuideStatus.Success)
            return status;
        while (cursorStatus != GridCoveredAddressCursorStatus.Complete)
        {
            status = ReadContainingCell(world, map, binding, point, meter, ref cursorStatus, out var candidate);
            if (status != NavigationGuideStatus.Success)
                return status;
            if (!meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            if (world.TryGetGridAndVoxel(Pin(map, candidate), out grid, out voxel))
                return NavigationGuideStatus.Success;
        }
        grid = null;
        return NavigationGuideStatus.NoPath;
    }

    private NavigationGuideStatus BeginContainingCells(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance map, Vector3d point, NavigationWorkMeter meter, out GridCoveredAddressCursorStatus cursorStatus)
    {
        graph.TryGetCoveredAddressGeneration(map.MapId, out var generation);
        world.TryBeginCoveredAddresses(_cursor, point, point, 1, map.Map.GridBinding.Key);
        Span<GridCoveredAddressGeneration> input = stackalloc GridCoveredAddressGeneration[1] { generation };
        return AdvanceCoverage(world, input, Span<GridCoveredAddress>.Empty, meter, out cursorStatus, out _);
    }

    private NavigationGuideStatus ReadContainingCell(GridWorld world, NavigationMapInstance map,
        NavigationPlanarBinding binding, Vector3d point, NavigationWorkMeter meter,
        ref GridCoveredAddressCursorStatus cursorStatus, out VoxelIndex index)
    {
        index = default;
        Span<GridCoveredAddress> output = stackalloc GridCoveredAddress[1];
        do
        {
            NavigationGuideStatus status = AdvanceCoverage(world, ReadOnlySpan<GridCoveredAddressGeneration>.Empty, output,
                meter, out cursorStatus, out int count);
            if (status != NavigationGuideStatus.Success)
                return status;
            if (count == 0 || output[0].VoxelIndex.y != binding.LayerIndex)
                continue;
            if (!meter.TryConsumeEndpointCandidates(1))
                return NavigationGuideStatus.BudgetExceeded;
            VoxelIndex candidate = output[0].VoxelIndex;
            map.Map.GridBinding.TryGetCellPrism(candidate, out var prism);
            if (prism.Contains(point))
            {
                index = candidate;
                return NavigationGuideStatus.Success;
            }
        }
        while (cursorStatus != GridCoveredAddressCursorStatus.Complete);
        return NavigationGuideStatus.NoPath;
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
        return TraceCircle(world, graph, layerCount, binding.EmbeddingY, source!.WorldIndex, target!.WorldIndex,
            start, end, radius, medium, capabilities, policy, meter, NavigationGuideStatus.NoPath);
    }

    internal NavigationGuideStatus BindLayers(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, Vector3d point, NavigationWorkMeter meter,
        out int layerCount)
    {
        NavigationGuideStatus status = CollectLayers(graph, binding, meter, out layerCount);
        if (status != NavigationGuideStatus.Success)
            return status;
        world.TryBeginCoveredAddresses(_cursor, point, point, layerCount, source.Map.GridBinding.Key);
        Span<GridCoveredAddressGeneration> input = stackalloc GridCoveredAddressGeneration[1];
        for (int i = 0; i < layerCount; i++)
        {
            input[0] = _layers[i].Generation;
            // Bind all compatible generations to distinguish stale data from unsupported geometry.
            status = AdvanceCoverage(world, input, Span<GridCoveredAddress>.Empty, meter, out _, out _);
            if (status != NavigationGuideStatus.Success)
                return status;
        }
        return NavigationGuideStatus.Success;
    }

    private NavigationGuideStatus AdvanceCoverage(GridWorld world,
        ReadOnlySpan<GridCoveredAddressGeneration> input, Span<GridCoveredAddress> output,
        NavigationWorkMeter meter, out GridCoveredAddressCursorStatus status, out int outputCount)
    {
        status = world.AdvanceCoveredAddresses(_cursor, input, output,
            input.IsEmpty ? 0 : meter.RemainingLookupProbes, input.IsEmpty ? meter.RemainingLookupProbes : 0,
            output.Length, out int lookups, out int addresses, out _, out outputCount);
        int probes = lookups + addresses;
        meter.TryConsumeLookupProbes(probes);
        if (status == GridCoveredAddressCursorStatus.Stale)
            return NavigationGuideStatus.Stale;
        // Binding does not probe addresses and each enumeration call has room for one output.
        return status == GridCoveredAddressCursorStatus.More && probes == 0
            ? NavigationGuideStatus.BudgetExceeded
            : NavigationGuideStatus.Success;
    }

    private NavigationGuideStatus CollectLayers(NavigationWorldGraph graph, NavigationPlanarBinding binding,
        NavigationWorkMeter meter, out int count)
    {
        count = 0;
        for (int i = 0; i < graph.MapCount; i++)
        {
            if (!meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            if (!graph.TryGetCoveredAddressGeneration(i, out string mapId, out var generation))
                continue;
            graph.TryGetMap(mapId, out NavigationMapInstance instance);
            if (instance.Map.PlanarBinding is not NavigationPlanarBinding candidate
                || candidate.EmbeddingY != binding.EmbeddingY)
            {
                continue;
            }
            if (count == _layers.Length)
                return NavigationGuideStatus.CapacityExceeded;
            _layers[count++] = new GridPlanarLayer(generation, candidate.LayerIndex);
        }
        return NavigationGuideStatus.Success;
    }

    internal static WorldVoxelIndex Pin(NavigationMapInstance instance, VoxelIndex index) =>
        new(instance.GridIdentity.WorldSpawnToken, instance.GridIdentity.GridIndex, instance.GridIdentity.GridSpawnToken, index);

    internal NavigationGuideStatus TraceCircle(GridWorld world, NavigationWorldGraph graph,
        int layerCount, Fixed64 embeddingY, WorldVoxelIndex source, WorldVoxelIndex target, Vector2d start, Vector2d end,
        Fixed64 radius, TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy,
        NavigationWorkMeter meter, NavigationGuideStatus blocked)
    {
        int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
        int addressLimit = Math.Min(_workspace.CoveredAddressCapacity, meter.RemainingCoveredVoxelIntervals);
        var cells = _workspace.BodyTraceCells;
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(world,
            _layers.AsSpan(0, layerCount), embeddingY, source, target, start, end, Fixed64.Zero, radius,
            cells, _workspace.BodyTraceScratch,
            gridLimit, addressLimit, _workspace.CoveredAddressCapacity, meter.RemainingGridCandidateWork);
        meter.TryConsumeLookupProbes(report.GridCandidateCount);
        meter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount);
        NavigationVolumeAnchorStatus status = NavigationVolumeAnchorEvaluator.ClassifyTraceStatus(report.Status,
            gridLimit, _workspace.MapCapacity, addressLimit, _workspace.CoveredAddressCapacity);

        if (status == NavigationVolumeAnchorStatus.Success)
        {
            // Complete traces contain only required cells. The cursor validated their generations before tracing.
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                graph.TryGetMapId(cell.ConfigurationKey, out string mapId);
                graph.TryGetMap(mapId, out NavigationMapInstance instance);
                if (instance.TryGetSlot(cell.Cell.VoxelIndex, out int slot))
                {
                    // Absent semantics yield a default cell with no admissible medium.
                    instance.TryGetEffectiveCell(slot, out NavigationCell semantics);
                    instance.TryGetPhysicalState(slot, out bool present, out byte obstacleCount);
                    if ((present & (obstacleCount == 0))
                        && TraversalEvaluator.IsCellPassable(semantics, medium, capabilities, radius, policy, out _))
                        continue;
                }
                status = NavigationVolumeAnchorStatus.Unavailable;
                break;
            }
        }
        cells.Clear();
        return status switch
        {
            NavigationVolumeAnchorStatus.Success => NavigationGuideStatus.Success,
            NavigationVolumeAnchorStatus.Unavailable => blocked,
            NavigationVolumeAnchorStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
            NavigationVolumeAnchorStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
            _ => NavigationGuideStatus.BudgetExceeded
        };
    }
}
