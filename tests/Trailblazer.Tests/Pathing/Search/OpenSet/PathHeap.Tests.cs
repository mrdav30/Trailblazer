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
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition voxel = CreateAttachedPartition(Vector3d.Zero);

        heap.Add(voxel, pathCost: 1);
        Assert.Equal(1u, heap.HeapCount);

        heap.RemoveFirst(out SolidChartPartition? removed);
        Assert.Equal(voxel, TestRequire.NotNull(removed));
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
    public void AStarHeap_ShouldPreferLowerTieBreakCost_WhenPathCostsMatch()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition farther = CreateAttachedPartition(new Vector3d(0, 0, 0));
        SolidChartPartition closer = CreateAttachedPartition(new Vector3d(1, 0, 0));

        heap.Add(farther, pathCost: 10, tieBreakCost: 20);
        heap.Add(closer, pathCost: 10, tieBreakCost: 5);

        heap.RemoveFirst(out SolidChartPartition? first);

        Assert.Equal(closer, TestRequire.NotNull(first));
    }

    [Fact]
    public void Heap_ShouldTrackVoxelPathCostAndClosedState()
    {
        var heap = new PathHeap<Voxel>();

        Voxel a = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel b = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        Voxel c = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(2, 0, 0));

        heap.Add(a, pathCost: 30);
        heap.Add(b, pathCost: 20);
        heap.Add(c, pathCost: 10);

        heap.UpdatePathCost(a, pathCost: 5);
        heap.SortUp(a);

        heap.RemoveFirst(out Voxel? first);
        Voxel removedVoxel = TestRequire.NotNull(first);
        heap.SetClosed(removedVoxel);

        Assert.Equal(a, removedVoxel);
        Assert.True(heap.IsClosed(a));
        Assert.True(heap.TryGetPathCost(a, out int pathCost));
        Assert.Equal(5, pathCost);
        Assert.Single(heap.EnumerateClosed().ToArray());
    }

    [Fact]
    public void Heap_ShouldPreferRightChild_DuringSortDown_WhenRightIsCheapest()
    {
        // Build a heap where after removing root, the right child is cheaper than left,
        // forcing SortDown to take the right-child branch.
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition root = CreateAttachedPartition(new Vector3d(0, 0, 0));
        SolidChartPartition left = CreateAttachedPartition(new Vector3d(1, 0, 0));
        SolidChartPartition right = CreateAttachedPartition(new Vector3d(2, 0, 0));

        // Add in order: root cheapest, then the two children with right cheaper than left.
        heap.Add(root, pathCost: 1);
        heap.Add(left, pathCost: 30);
        heap.Add(right, pathCost: 20);

        heap.RemoveFirst(out SolidChartPartition? first);
        Assert.Equal(root, TestRequire.NotNull(first));

        // After SortDown the right child (cost 20) should bubble up as the new minimum.
        heap.RemoveFirst(out SolidChartPartition? second);
        Assert.Equal(right, TestRequire.NotNull(second));
    }

    [Fact]
    public void Heap_FastClear_ShouldAllowReuse()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0));
        heap.Add(a, pathCost: 5);
        heap.SetClosed(a);
        Assert.True(heap.IsClosed(a));

        heap.FastClear();

        Assert.Equal(0u, heap.HeapCount);
        // After FastClear the old item should no longer be tracked as open or closed.
        Assert.False(heap.Contains(a));
        Assert.False(heap.IsClosed(a));
    }

    [Fact]
    public void Heap_Reset_ShouldFullyReinitializeState()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0));
        heap.Add(a, pathCost: 10);
        heap.SetClosed(a);

        heap.Reset();

        Assert.Equal(0u, heap.HeapCount);
        Assert.False(heap.Contains(a));
        Assert.False(heap.IsClosed(a));
        Assert.Equal(0, heap.TrackedCount);
    }

    [Fact]
    public void Heap_TryGetPathCost_ShouldReturnMaxInt_ForUnknownItem()
    {
        var heap = new PathHeap<SolidChartPartition>();
        SolidChartPartition unknown = CreateAttachedPartition(new Vector3d(0, 0, 0));

        bool found = heap.TryGetPathCost(unknown, out int cost);
        Assert.False(found);
        Assert.Equal(int.MaxValue, cost);
    }

    [Fact]
    public void Heap_TwoItemRemoval_ShouldNotTriggerSortDown()
    {
        var heap = new PathHeap<SolidChartPartition>();

        SolidChartPartition a = CreateAttachedPartition(new Vector3d(0, 0, 0));
        SolidChartPartition b = CreateAttachedPartition(new Vector3d(1, 0, 0));

        heap.Add(a, pathCost: 5);
        heap.Add(b, pathCost: 10);

        heap.RemoveFirst(out SolidChartPartition? first);
        Assert.Equal(a, TestRequire.NotNull(first));
        Assert.Equal(1u, heap.HeapCount);

        heap.RemoveFirst(out SolidChartPartition? second);
        Assert.Equal(b, TestRequire.NotNull(second));
        Assert.Equal(0u, heap.HeapCount);
    }

    [Fact]
    public void Heap_Add_ShouldIgnoreDuplicates_AndResizePastDefaultCapacity()
    {
        var heap = new PathHeap<HeapNode>();
        int originalCapacity = heap.Capacity;

        HeapNode duplicate = new("duplicate");
        heap.Add(duplicate, pathCost: 1);
        heap.Add(duplicate, pathCost: 0);

        Assert.Equal(1u, heap.HeapCount);

        for (int i = 0; i < PathHeap<HeapNode>.DefaultCapacity; i++)
            heap.Add(new HeapNode($"node-{i}"), pathCost: i + 2);

        Assert.Equal((uint)PathHeap<HeapNode>.DefaultCapacity + 1u, heap.HeapCount);
        Assert.True(heap.Capacity > originalCapacity);
    }

    [Fact]
    public void Heap_ReusedCapacity_ShouldNotAllocatePerNodeMetadata()
    {
        const int nodeCount = 4096;
        var heap = new PathHeap<HeapNode>();
        HeapNode[] nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new HeapNode($"node-{i}"))
            .ToArray();

        for (int i = 0; i < nodes.Length; i++)
            heap.Add(nodes[i], pathCost: i);

        heap.FastClear();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < nodes.Length; i++)
            heap.Add(nodes[i], pathCost: i);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4096, $"Expected reused heap metadata to avoid per-node allocation, but allocated {allocated} bytes.");
    }

    private static SolidChartPartition CreateAttachedPartition(Vector3d position)
    {
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, position);

        var partition = new SolidChartPartition();

        partition.OnAddToVoxel(voxel);
        return partition;
    }

    private sealed class HeapNode
    {
        public HeapNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
