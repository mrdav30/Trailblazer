using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing
{
    public class PathPartitionHeapTests
    {
        [Fact]
        public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
        {
            PathHeap.FastClear();
            var voxel = new PathPartition();
            voxel.HeapCost = 1;

            PathHeap.Add(voxel);
            Assert.Equal(1u, PathHeap.Count);

            PathHeap.RemoveFirst(out PathPartition removed);
            Assert.Equal(voxel, removed);
            Assert.Equal(0u, PathHeap.Count);

            // Should not leave stale data
            Assert.Null(PathHeap.PeekAt(0));
        }

        [Fact]
        public void AStarHeap_ShouldSortAfterCostUpdate()
        {
            PathHeap.FastClear();

            var a = new PathPartition { HeapCost = 30 };
            var b = new PathPartition { HeapCost = 20 };
            var c = new PathPartition { HeapCost = 10 };

            PathHeap.Add(a);
            PathHeap.Add(b);
            PathHeap.Add(c);

            // Update 'a' to have lower cost than 'c'
            a.HeapCost = 5;
            PathHeap.SortUp(a);

            PathHeap.RemoveFirst(out var first);
            Assert.Equal(a, first);
        }

    }
}
