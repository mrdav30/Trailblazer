using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class PathingNavigationMapTests
{
    [Fact]
    public void Register_AddsMapToManager()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("TestMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);

        Assert.True(PathManager.TryGetNavigationChart("TestMap", out var retrieved));
        Assert.Equal(map, retrieved);

        PathManager.UnloadChart("TestMap");
    }

    [Fact]
    public void InitializeMap_AddsPartitionToExpectedVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("InitMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("InitMap");

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition<PathPartition>(out _));

        PathManager.UnloadChart("InitMap");
    }

    [Fact]
    public void UnloadMap_RemovesOnlyItsPartition()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var pos = new Vector3d(0, 0, 0);
        var mapA = PathTestFactory.BuildSinglePointMap("MapA", pos);
        var mapB = PathTestFactory.BuildSinglePointMap("MapB", pos);

        PathManager.Register(mapA);
        PathManager.Register(mapB);

        PathManager.InitializeChart("MapA");
        PathManager.InitializeChart("MapB");

        // Validate partition exists and belongs to both
        GlobalGridManager.TryGetGridAndVoxel(pos, out _, out Voxel voxel);
        voxel.TryGetPartition(out PathPartition partition);
        Assert.True(partition.BelongsTo("MapA"));
        Assert.True(partition.BelongsTo("MapB"));

        PathManager.UnloadChart("MapA");

        // Should still be there because MapB owns it
        Assert.True(voxel.TryGetPartition<PathPartition>(out var afterUnload));
        Assert.True(afterUnload.BelongsTo("MapB"));

        PathManager.UnloadChart("MapB");
    }

    [Fact]
    public void IsWalkable_ShouldReturnFalseForOutOfBounds()
    {
        var map = PathTestFactory.BuildSinglePointMap("BoundsTest", new Vector3d(0, 0, 0));
        Assert.False(map.IsWalkable(new Vector3d(10, 0, 10))); // Way outside

        PathManager.UnloadChart("BoundsTest");
    }

    [Fact]
    public void InitializeMap_ShouldNotDuplicatePartition()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("DuplicateInit", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("DuplicateInit");
        PathManager.InitializeChart("DuplicateInit"); // idempotent

        GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel voxel);
        var count = voxel.TryGetPartition<PathPartition>(out var partition) ? 1 : 0;

        Assert.True(count == 1);
        Assert.True(partition.BelongsTo("DuplicateInit"));

        PathManager.UnloadChart("DuplicateInit");
    }
}
