using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing
{
    public class PathPartitionHeapTests
    {
        [Fact]
        public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
        {
            var heap = new PathHeap();

            var voxel = new PathPartition
            {
                PathCost = 1
            };

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

            var a = new PathPartition { PathCost = 30 };
            var b = new PathPartition { PathCost = 20 };
            var c = new PathPartition { PathCost = 10 };

            heap.Add(a);
            heap.Add(b);
            heap.Add(c);

            // Update 'a' to have lower cost than 'c'
            a.PathCost = 5;
            heap.SortUp(a);

            heap.RemoveFirst(out var first);
            Assert.Equal(a, first);
        }
    }
}
