using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class SolidChartPartitionHeapTests : IDisposable
{
    public SolidChartPartitionHeapTests()
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

        SolidChartPartition voxel = CreateAttachedPartition(Vector3d.Zero, pathCost: 1);

        heap.Add(voxel);
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
        var heap = new PathHeap();

        SolidChartPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0), pathCost: 30);
        SolidChartPartition b = CreateAttachedPartition(new Vector3d(1, 0, 0), pathCost: 20);
        SolidChartPartition c = CreateAttachedPartition(new Vector3d(2, 0, 0), pathCost: 10);

        heap.Add(a);
        heap.Add(b);
        heap.Add(c);

        // Update 'a' to have lower cost than 'c'
        a.PathCost = 5;
        heap.SortUp(a);

        heap.RemoveFirst(out var first);
        Assert.Equal(a, first);
    }

    [Fact]
    public void PathCost_ShouldExpireWhenSurveyVersionAdvances()
    {
        SolidChartPartition partition = CreateAttachedPartition(Vector3d.Zero, pathCost: 7);
        partition.PathCostModifier = 3;

        Assert.Equal(7, partition.PathCost);
        Assert.Equal(10, partition.PathCostTotal);

        SolidChartPartition.AdvancePathCostVersion();

        Assert.Equal(int.MaxValue, partition.PathCost);
        Assert.Equal(int.MaxValue, partition.PathCostTotal);
    }

    private static SolidChartPartition CreateAttachedPartition(Vector3d position, int pathCost)
    {
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel));

        var partition = new SolidChartPartition
        {
        };

        partition.OnAddToVoxel(voxel);
        partition.PathCost = pathCost;
        return partition;
    }
}
