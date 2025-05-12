using Xunit;
using GridForge.Grids;
using GridForge.Configuration;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Tests.Pathing
{
    [Collection("TraversableNavMapCollection")]
    public class PathingNavigationMapTests
    {
        [Fact]
        public void Register_AddsMapToManager()
        {
            var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
            GlobalGridManager.TryAddGrid(config, out _);

            var map = BuildSinglePointMap("TestMap", new Vector3d(0, 0, 0));
            PathingManager.Register(map);

            Assert.True(PathingManager.TryGetNavigationMap("TestMap", out var retrieved));
            Assert.Equal(map, retrieved);
        }

        [Fact]
        public void InitializeMap_AddsPartitionToExpectedNode()
        {
            var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
            GlobalGridManager.TryAddGrid(config, out _);

            var map = BuildSinglePointMap("InitMap", new Vector3d(0, 0, 0));
            PathingManager.Register(map);
            PathingManager.InitializeMap("InitMap");

            Assert.True(GlobalGridManager.TryGetGridAndNode(new Vector3d(0, 0, 0), out _, out Node node));
            Assert.True(node.TryGetPartition<PathPartition>(out _));
        }

        [Fact]
        public void UnloadMap_RemovesOnlyItsPartition()
        {
            var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
            GlobalGridManager.TryAddGrid(config, out _);

            var pos = new Vector3d(0, 0, 0);
            var mapA = BuildSinglePointMap("MapA", pos);
            var mapB = BuildSinglePointMap("MapB", pos);

            PathingManager.Register(mapA);
            PathingManager.Register(mapB);

            PathingManager.InitializeMap("MapA");
            PathingManager.InitializeMap("MapB");

            // Validate partition exists and belongs to both
            GlobalGridManager.TryGetGridAndNode(pos, out _, out Node node);
            node.TryGetPartition(out PathPartition partition);
            Assert.True(partition.BelongsTo("MapA"));
            Assert.True(partition.BelongsTo("MapB"));

            PathingManager.Unload("MapA");

            // Should still be there because MapB owns it
            Assert.True(node.TryGetPartition<PathPartition>(out var afterUnload));
            Assert.True(afterUnload.BelongsTo("MapB"));
        }

        [Fact]
        public void IsWalkable_ShouldReturnFalseForOutOfBounds()
        {
            var map = BuildSinglePointMap("BoundsTest", new Vector3d(0, 0, 0));
            Assert.False(map.IsWalkable(new Vector3d(10, 0, 10))); // Way outside
        }

        [Fact]
        public void InitializeMap_ShouldNotDuplicatePartition()
        {
            var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
            GlobalGridManager.TryAddGrid(config, out _);

            var map = BuildSinglePointMap("DuplicateInit", new Vector3d(0, 0, 0));
            PathingManager.Register(map);
            PathingManager.InitializeMap("DuplicateInit");
            PathingManager.InitializeMap("DuplicateInit"); // idempotent

            GlobalGridManager.TryGetGridAndNode(new Vector3d(0, 0, 0), out _, out Node node);
            var count = node.TryGetPartition<PathPartition>(out var partition) ? 1 : 0;

            Assert.True(count == 1);
            Assert.True(partition.BelongsTo("DuplicateInit"));
        }

        private static NavigationChart BuildSinglePointMap(string name, Vector3d worldPos)
        {
            // Convert a single world point into an aligned map
            Vector3d origin = worldPos - new Vector3d(1, 1, 1);
            bool[,,] data = new bool[3, 3, 3];
            data[1, 1, 1] = true;

            return NavigationChart.From3D(name, data, origin, Fixed64.One);
        }
    }
}
