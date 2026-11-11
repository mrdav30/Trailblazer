//=======================================================================
// NavigationPlanarWork.cs
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

/// <summary>Owns bounded native planar coverage against one published domain.</summary>
internal sealed partial class NavigationPlanarWork
{
    private readonly GridCoveredAddressCursor _cursor;
    private readonly GridPlanarLayer[] _layers;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationPlanarWork(NavigationRayWorkspace workspace)
    {
        _workspace = workspace;
        _cursor = new GridCoveredAddressCursor(workspace.MapCapacity);
        _layers = new GridPlanarLayer[workspace.MapCapacity];
    }

    internal static bool TryGetMap(NavigationWorldGraph graph, string mapId, out NavigationMapInstance instance,
        out NavigationPlanarBinding binding, NavigationMode2D mode = NavigationMode2D.TopDown)
    {
        if (graph.TryGetMap(mapId, out instance) && instance.IsMaterialized
            && instance.Map.PlanarBinding is NavigationPlanarBinding planar && planar.Mode == mode)
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
                || candidate.Mode != binding.Mode || candidate.EmbeddingY != binding.EmbeddingY)
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

    internal NavigationGuideStatus TraceBody(GridWorld world, NavigationWorldGraph graph,
        int layerCount, NavigationPlanarBinding binding, WorldVoxelIndex source, WorldVoxelIndex target, Vector2d start, Vector2d end,
        Fixed64 radius, Fixed64 axisLength, TraversalMedia medium, TraversalCapability capabilities, NavigationAreaPolicy policy,
        NavigationWorkMeter meter, NavigationGuideStatus blocked)
    {
        int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
        int addressLimit = Math.Min(_workspace.CoveredAddressCapacity, meter.RemainingCoveredVoxelIntervals);
        var cells = _workspace.BodyTraceCells;
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(world,
            _layers.AsSpan(0, layerCount), binding.EmbeddingY, source, target, start, end, axisLength, radius,
            cells, _workspace.BodyTraceScratch,
            gridLimit, addressLimit, _workspace.CoveredAddressCapacity, meter.RemainingGridCandidateWork);
        meter.TryConsumeLookupProbes(report.GridCandidateCount);
        meter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount);
        NavigationVolumeAnchorStatus status = NavigationVolumeAnchorEvaluator.ClassifyTraceStatus(report.Status,
            gridLimit, _workspace.MapCapacity, addressLimit, _workspace.CoveredAddressCapacity);

        if (status == NavigationVolumeAnchorStatus.Success)
        {
            bool sideView = binding.Mode == NavigationMode2D.SideView;
            // Support constraints belong to anchors. The rest of the body needs open space.
            TraversalMedia coverageMedium = sideView ? TraversalMedia.None : medium;
            Fixed64 coverageRadius = sideView ? Fixed64.Zero : radius;
            // Complete traces contain only required cells. The cursor validated their generations before tracing.
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                graph.TryGetMapId(cell.ConfigurationKey, out string mapId);
                graph.TryGetMap(mapId, out NavigationMapInstance instance);
                VoxelIndex index = cell.Cell.VoxelIndex;
                if (instance.TryGetSlot(index, out int slot)
                    && instance.TryGetEffectiveCell(slot, out NavigationCell semantics))
                {
                    instance.TryGetPhysicalState(slot, out bool present, out byte obstacleCount);
                    if ((present & (obstacleCount == 0))
                        && TraversalEvaluator.IsCellPassable(semantics, coverageMedium, capabilities, coverageRadius, policy, out _))
                    {
                        if (sideView && instance.Map.TryGetPlanarSupport(index, out var support))
                        {
                            status = support.CheckClearance(start, end, axisLength, radius);
                            if (status != NavigationVolumeAnchorStatus.Success)
                                break;
                        }
                        continue;
                    }
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
