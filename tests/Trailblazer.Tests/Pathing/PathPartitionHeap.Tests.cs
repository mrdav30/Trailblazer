using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using System.Linq;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class PathHeapTests : IDisposable
{
    public PathHeapTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();

        GlobalGridManager.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition voxel = CreateAttachedPartition(Vector3d.Zero);

        heap.Add(voxel, pathCost: 1);
        Assert.Equal(1u, heap.HeapCount);

        heap.RemoveFirst(out SolidChartPartition removed);
        Assert.Equal(voxel, removed);
        Assert.Equal(0u, heap.HeapCount);

        // Should not leave stale data
        Assert.Null(heap.PeekAt(0));
    }

    [Fact]
    public void AStarHeap_ShouldSortAfterCostUpdate()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0));
        SolidChartPartition b = CreateAttachedPartition(new Vector3d(1, 0, 0));
        SolidChartPartition c = CreateAttachedPartition(new Vector3d(2, 0, 0));

        heap.Add(a, pathCost: 30);
        heap.Add(b, pathCost: 20);
        heap.Add(c, pathCost: 10);

        heap.UpdatePathCost(a, pathCost: 5);
        heap.SortUp(a);

        heap.RemoveFirst(out var first);
        Assert.Equal(a, first);
    }

    [Fact]
    public void Heap_ShouldTrackVoxelPathCostAndClosedState()
    {
        var heap = new PathHeap<Voxel>();

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel a));
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(1, 0, 0), out _, out Voxel b));
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(2, 0, 0), out _, out Voxel c));

        heap.Add(a, pathCost: 30);
        heap.Add(b, pathCost: 20);
        heap.Add(c, pathCost: 10);

        heap.UpdatePathCost(a, pathCost: 5);
        heap.SortUp(a);

        heap.RemoveFirst(out Voxel first);
        heap.SetClosed(first);

        Assert.Equal(a, first);
        Assert.True(heap.IsClosed(a));
        Assert.True(heap.TryGetPathCost(a, out int pathCost));
        Assert.Equal(5, pathCost);
        Assert.Single(heap.EnumerateClosed().ToArray());
    }

    private static SolidChartPartition CreateAttachedPartition(Vector3d position)
    {
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel));

        var partition = new SolidChartPartition();

        partition.OnAddToVoxel(voxel);
        return partition;
    }
}
