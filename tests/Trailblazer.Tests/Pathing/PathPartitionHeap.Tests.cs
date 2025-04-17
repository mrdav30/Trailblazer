using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing
{
    public class PathPartitionHeapTests
    {
        [Fact]
        public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
        {
            PathPartitionHeap.FastClear();
            var node = new PathPartition();
            node.HeapCost = 1;

            PathPartitionHeap.Add(node);
            Assert.Equal(1u, PathPartitionHeap.Count);

            PathPartitionHeap.RemoveFirst(out PathPartition removed);
            Assert.Equal(node, removed);
            Assert.Equal(0u, PathPartitionHeap.Count);

            // Should not leave stale data
            Assert.Null(PathPartitionHeap.PeekAt(0));
        }

        [Fact]
        public void AStarHeap_ShouldSortAfterCostUpdate()
        {
            PathPartitionHeap.FastClear();

            var a = new PathPartition { HeapCost = 30 };
            var b = new PathPartition { HeapCost = 20 };
            var c = new PathPartition { HeapCost = 10 };

            PathPartitionHeap.Add(a);
            PathPartitionHeap.Add(b);
            PathPartitionHeap.Add(c);

            // Update 'a' to have lower cost than 'c'
            a.HeapCost = 5;
            PathPartitionHeap.SortUp(a);

            PathPartitionHeap.RemoveFirst(out var first);
            Assert.Equal(a, first);
        }

    }
}
