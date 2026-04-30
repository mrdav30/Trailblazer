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
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetVoxel_ShouldRemainOnTheQueryLayer()
    {
        Vector3d query = new(2, 1, 0);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 1);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.y.Should().Be(query.y);
    }

    [Fact]
    public void GetVoxel_ShouldAdvanceToTheNextRing_WhenTheFirstRingIsBlocked()
    {
        Vector3d query = Vector3d.Zero;
        BlockFirstRing(query);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 2);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.Should().Be(new Vector3d(-2, 0, 0));
    }

    [Fact]
    public void GetVoxel_ShouldBiasSearchFromTheAnchorVoxelLocalOffset()
    {
        Fixed64 quarter = TrailblazerWorldManager.VoxelSize / 4;
        Vector3d query = new(quarter * 3, Fixed64.Zero, quarter);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 1);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void GetVoxel_ShouldAdvancePositiveXLayer_WhenTheFirstRingIsBlocked()
    {
        Fixed64 quarter = TrailblazerWorldManager.VoxelSize / 4;
        Vector3d query = new(quarter * 3, Fixed64.Zero, quarter);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);
        BlockFirstRing(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 2);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.Should().Be(new Vector3d(2, 0, 0));
    }

    [Fact]
    public void GetVoxel_ShouldAdvancePositiveZLayer_WhenTheFirstRingIsBlocked()
    {
        Fixed64 halfVoxel = TrailblazerWorldManager.VoxelSize / 2;
        Fixed64 quarter = TrailblazerWorldManager.VoxelSize / 4;
        Fixed64 eighth = TrailblazerWorldManager.VoxelSize / 8;
        Vector3d query = new(halfVoxel - eighth, Fixed64.Zero, halfVoxel + quarter);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);
        BlockFirstRing(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 2);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.Should().Be(new Vector3d(0, 0, 2));
    }

    [Fact]
    public void GetVoxel_ShouldReturnFalse_WhenSearchRadiusIsExhausted()
    {
        Vector3d query = Vector3d.Zero;
        Voxel anchorVoxel = TestRequire.VoxelAt(query);
        BlockFirstRing(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 1);

        AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? voxel).Should().BeFalse();
        voxel.Should().BeNull();
    }

    [Fact]
    public void GetVoxel_ShouldBiasInNegativeZDirection_AndAdvanceLayer_WhenFirstRingIsBlocked()
    {
        // Placing the query at (halfVoxel, 0, 0) makes zOffsetFromCenter dominate (xOffset = 0)
        // and be negative, so InitializeDirection chooses direction (0, -1).
        // Blocking the first ring then forces a layer advance where _direction.z < 0,
        // exercising the negative-Z branches in both InitializeDirection and the ring-advance logic.
        Fixed64 halfVoxel = TrailblazerWorldManager.VoxelSize / 2;
        Vector3d query = new(halfVoxel, Fixed64.Zero, Fixed64.Zero);
        Voxel anchorVoxel = TestRequire.VoxelAt(query);
        BlockFirstRing(query);

        AlternativeVoxelFinder.Shared.SetQuery(query, anchorVoxel, maxTestDistance: 2);

        Voxel voxel = TestRequire.Created(AlternativeVoxelFinder.Shared.GetVoxel(out Voxel? createdVoxel), createdVoxel);
        voxel.WorldPosition.z.Should().BeLessThan(Fixed64.Zero);
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
        var (grid, voxel) = TestRequire.GridAndVoxelAt(position);
        grid.TryAddObstacle(
            voxel,
            new BoundsKey(position, position)).Should().BeTrue();
    }
}
