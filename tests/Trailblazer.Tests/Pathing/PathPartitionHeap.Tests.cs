using FixedMathSharp;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class PathPartitionHeapTests : IDisposable
{
    public PathPartitionHeapTests()
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
        var heap = new PathHeap();

        PathPartition voxel = CreateAttachedPartition(Vector3d.Zero, pathCost: 1);

        heap.Add(voxel);
        Assert.Equal(1u, heap.HeapCount);

        heap.RemoveFirst(out PathPartition removed);
        Assert.Equal(voxel, removed);
        Assert.Equal(0u, heap.HeapCount);

        // Should not leave stale data
        Assert.Null(heap.PeekAt(0));
    }

    [Fact]
    public void AStarHeap_ShouldSortAfterCostUpdate()
    {
        var heap = new PathHeap();

        PathPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0), pathCost: 30);
        PathPartition b = CreateAttachedPartition(new Vector3d(1, 0, 0), pathCost: 20);
        PathPartition c = CreateAttachedPartition(new Vector3d(2, 0, 0), pathCost: 10);

        heap.Add(a);
        heap.Add(b);
        heap.Add(c);

        // Update 'a' to have lower cost than 'c'
        a.PathCost = 5;
        heap.SortUp(a);

        heap.RemoveFirst(out var first);
        Assert.Equal(a, first);
    }

    private static PathPartition CreateAttachedPartition(Vector3d position, int pathCost)
    {
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel));

        var partition = new PathPartition
        {
            PathCost = pathCost
        };

        partition.OnAddToVoxel(voxel);
        return partition;
    }
}
