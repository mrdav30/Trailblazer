using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AlternativeVoxelFinderTests : IDisposable
{
    public AlternativeVoxelFinderTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
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
    public void GetVoxel_ShouldRemainOnTheQueryLayer()
    {
        Vector3d query = new(2, 1, 0);
        GlobalGridManager.TryGetVoxel(query, out Voxel anchorVoxel).Should().BeTrue();

        AlternativeVoxelFinder.Instance.SetQuery(query, anchorVoxel, maxTestDistance: 1);

        AlternativeVoxelFinder.Instance.GetVoxel(out Voxel voxel).Should().BeTrue();
        voxel.WorldPosition.y.Should().Be(query.y);
    }

    [Fact]
    public void GetVoxel_ShouldAdvanceToTheNextRing_WhenTheFirstRingIsBlocked()
    {
        Vector3d query = Vector3d.Zero;
        BlockFirstRing(query);
        GlobalGridManager.TryGetVoxel(query, out Voxel anchorVoxel).Should().BeTrue();

        AlternativeVoxelFinder.Instance.SetQuery(query, anchorVoxel, maxTestDistance: 2);

        AlternativeVoxelFinder.Instance.GetVoxel(out Voxel voxel).Should().BeTrue();
        voxel.WorldPosition.Should().Be(new Vector3d(-2, 0, 0));
    }

    [Fact]
    public void GetVoxel_ShouldBiasSearchFromTheAnchorVoxelLocalOffset()
    {
        Fixed64 quarter = GlobalGridManager.VoxelSize / 4;
        Vector3d query = new(quarter * 3, Fixed64.Zero, quarter);
        GlobalGridManager.TryGetVoxel(query, out Voxel anchorVoxel).Should().BeTrue();

        AlternativeVoxelFinder.Instance.SetQuery(query, anchorVoxel, maxTestDistance: 1);

        AlternativeVoxelFinder.Instance.GetVoxel(out Voxel voxel).Should().BeTrue();
        voxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    private static void BlockFirstRing(Vector3d center)
    {
        AddObstacle(center + new Vector3d(-1, 0, -1));
        AddObstacle(center + new Vector3d(-1, 0, 0));
        AddObstacle(center + new Vector3d(-1, 0, 1));
        AddObstacle(center + new Vector3d(0, 0, -1));
        AddObstacle(center + new Vector3d(0, 0, 1));
        AddObstacle(center + new Vector3d(1, 0, -1));
        AddObstacle(center + new Vector3d(1, 0, 0));
        AddObstacle(center + new Vector3d(1, 0, 1));
    }

    private static void AddObstacle(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        GridObstacleManager.TryAddObstacle(
            voxel.GlobalIndex,
            new BoundsKey(position, position)).Should().BeTrue();
    }
}
