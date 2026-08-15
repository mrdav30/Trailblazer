using System;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Tests.Pathing.Graph;

internal static class NavigationSurfaceComponentTestFactory
{
    internal static NavigationSurfaceComponentIndex Build(NavigationWorldGraph graph)
    {
        NavigationSurfaceComponentBuildWork work = CreateBuildWork(graph);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue));
        while (!work.Advance(meter))
            meter.Reset();
        return work.Result;
    }

    internal static NavigationSurfaceComponentBuildWork CreateBuildWork(
        NavigationWorldGraph graph)
    {
        NavigationCellAddressSet seeds = NavigationCellAddressSet.Empty;
        var scratch = new VoxelIndex[1];
        for (int mapOrdinal = 0; mapOrdinal < graph.MapCount; mapOrdinal++)
        {
            NavigationMapInstance instance = graph.GetInstance(mapOrdinal);
            int bakedCursor = 0;
            int dynamicCursor = 0;
            for (int addressOrdinal = 0;
                 addressOrdinal < instance.AddressCount;
                 addressOrdinal++)
            {
                instance.CopyCanonicalAddressChunk(
                    ref bakedCursor,
                    ref dynamicCursor,
                    scratch);
                var address = new NavigationCellAddress(instance.MapId, scratch[0]);
                if (graph.HasEffectiveCell(address))
                    seeds = seeds.Add(address);
            }
        }
        return new NavigationSurfaceComponentBuildWork(
            graph,
            NavigationWorldGraph.Empty,
            NavigationSurfaceComponentKeySet.Empty,
            seeds,
            graph.TotalAddressCount);
    }
}
