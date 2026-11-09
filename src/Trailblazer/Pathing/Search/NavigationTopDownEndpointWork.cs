//=======================================================================
// NavigationTopDownEndpointWork.cs
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

/// <summary>Resolves a center in durable address order and proves its stationary circle against one graph.</summary>
internal sealed class NavigationTopDownEndpointWork
{
    private readonly GridCoveredAddressCursor _cursor;
    private readonly GridPlanarLayer[] _layers;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationTopDownEndpointWork(NavigationRayWorkspace workspace)
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
        address = default;
        NavigationGuideStatus status = CollectLayers(graph, binding, meter, out int layerCount);
        if (status != NavigationGuideStatus.Success)
            return status;

        var point = new Vector3d(center.X, binding.EmbeddingY, center.Y);
        world.TryBeginCoveredAddresses(_cursor, point, point, layerCount, source.Map.GridBinding.Key);
        Span<GridCoveredAddressGeneration> input = stackalloc GridCoveredAddressGeneration[1];
        Span<GridCoveredAddress> output = stackalloc GridCoveredAddress[1];
        int bound = 0;
        GridCoveredAddressCursorStatus cursorStatus;
        do
        {
            bool bindingGeneration = bound < layerCount;
            if (bindingGeneration)
                input[0] = _layers[bound].Generation;
            // Validate every eligible generation while yielding addresses only from the named map.
            cursorStatus = world.AdvanceCoveredAddresses(_cursor,
                bindingGeneration ? input : ReadOnlySpan<GridCoveredAddressGeneration>.Empty, output,
                bindingGeneration ? meter.RemainingLookupProbes : 0,
                bindingGeneration ? 0 : meter.RemainingLookupProbes, 1,
                out int lookups, out int addresses, out int inputsConsumed, out int outputCount);
            meter.TryConsumeLookupProbes(lookups + addresses);
            bound += inputsConsumed;
            if (cursorStatus == GridCoveredAddressCursorStatus.Stale)
                return NavigationGuideStatus.Stale;
            if (cursorStatus == GridCoveredAddressCursorStatus.More && meter.RemainingLookupProbes == 0
                && outputCount == 0)
            {
                return NavigationGuideStatus.BudgetExceeded;
            }

            if (outputCount != 0 && output[0].VoxelIndex.y == binding.LayerIndex)
            {
                if (!meter.TryConsumeEndpointCandidates(1))
                    return NavigationGuideStatus.BudgetExceeded;
                GridCoveredAddress candidate = output[0];
                source.Map.GridBinding.TryGetCellPrism(candidate.VoxelIndex, out var prism);
                if (prism.Contains(point))
                {
                    status = ProveCircle(world, graph, _layers.AsSpan(0, layerCount), binding.EmbeddingY,
                        candidate, center, radius, medium, capabilities, policy, meter);
                    if (status == NavigationGuideStatus.Success)
                        address = new NavigationCellAddress(source.MapId, candidate.VoxelIndex);
                    if (status != NavigationGuideStatus.InvalidStart)
                        return status;
                }
            }
        }
        while (cursorStatus != GridCoveredAddressCursorStatus.Complete);
        return NavigationGuideStatus.InvalidStart;
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

    private NavigationGuideStatus ProveCircle(GridWorld world, NavigationWorldGraph graph,
        ReadOnlySpan<GridPlanarLayer> layers, Fixed64 embeddingY, GridCoveredAddress candidate, Vector2d center,
        Fixed64 radius, TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy,
        NavigationWorkMeter meter)
    {
        var pinned = new WorldVoxelIndex(world.SpawnToken, candidate.GridIndex, candidate.GridSpawnToken,
            candidate.VoxelIndex);
        int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
        int addressLimit = Math.Min(_workspace.CoveredAddressCapacity, meter.RemainingCoveredVoxelIntervals);
        var cells = _workspace.BodyTraceCells;
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(world, layers, embeddingY,
            pinned, pinned, center, center, Fixed64.Zero, radius, cells, _workspace.BodyTraceScratch,
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
            NavigationVolumeAnchorStatus.Unavailable => NavigationGuideStatus.InvalidStart,
            NavigationVolumeAnchorStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
            NavigationVolumeAnchorStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
            _ => NavigationGuideStatus.BudgetExceeded
        };
    }
}
