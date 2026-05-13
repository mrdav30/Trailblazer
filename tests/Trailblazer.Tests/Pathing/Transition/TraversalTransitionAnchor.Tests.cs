using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class TraversalTransitionAnchorTests : IDisposable
{
    public TraversalTransitionAnchorTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TraversalTransitionAnchor_GasAndLiquid_ShouldCreateFromVoxelIndex()
    {
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        var gas = TraversalTransitionAnchor.Gas(voxel.WorldIndex);
        gas.Medium.Should().Be(TraversalMedium.Gas);
        gas.VoxelIndex.Should().Be(voxel.WorldIndex);
        gas.HasPointOverride.Should().BeFalse();
        gas.IsVolumeMedium.Should().BeTrue();

        var liquid = TraversalTransitionAnchor.Liquid(voxel.WorldIndex);
        liquid.Medium.Should().Be(TraversalMedium.Liquid);
        liquid.VoxelIndex.Should().Be(voxel.WorldIndex);
        liquid.HasPointOverride.Should().BeFalse();
        liquid.IsVolumeMedium.Should().BeTrue();
    }

    [Fact]
    public void TraversalTransitionAnchor_ShouldCreateFromVoxelIndexWithPointOverride()
    {
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        var pointOverride = new Vector3d(1.1, 0, 0);

        var gasWithOverride = TraversalTransitionAnchor.Gas(voxel.WorldIndex, pointOverride);
        gasWithOverride.HasPointOverride.Should().BeTrue();
        gasWithOverride.PointOverride.Should().Be(pointOverride);
        gasWithOverride.Position.Should().Be(pointOverride);

        var liquidWithOverride = TraversalTransitionAnchor.Liquid(voxel.WorldIndex, pointOverride);
        liquidWithOverride.HasPointOverride.Should().BeTrue();
        liquidWithOverride.PointOverride.Should().Be(pointOverride);

        var solidWithOverride = TraversalTransitionAnchor.Solid(voxel.WorldIndex, pointOverride);
        solidWithOverride.HasPointOverride.Should().BeTrue();
        solidWithOverride.PointOverride.Should().Be(pointOverride);
    }

    [Fact]
    public void TraversalTransitionAnchor_SolidTryGetVolumeMedium_ShouldReturnFalse()
    {
        var solid = TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0));
        solid.IsVolumeMedium.Should().BeFalse();
        solid.TryGetVolumeMedium(out TraversalMedium medium).Should().BeFalse();
        medium.Should().Be(TraversalMedium.Unknown);
    }

    [Fact]
    public void TraversalTransitionAnchor_GasTryGetVolumeMedium_ShouldReturnGas()
    {
        var gas = TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0));
        gas.TryGetVolumeMedium(out TraversalMedium medium).Should().BeTrue();
        medium.Should().Be(TraversalMedium.Gas);
    }

    [Fact]
    public void TraversalTransitionAnchor_LiquidTryGetVolumeMedium_ShouldReturnLiquid()
    {
        var liquid = TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0));
        liquid.TryGetVolumeMedium(out TraversalMedium medium).Should().BeTrue();
        medium.Should().Be(TraversalMedium.Liquid);
    }

    [Fact]
    public void TraversalTransitionAnchor_CreateFromPosition_ShouldThrow_WhenPositionIsOutsideGrid()
    {
        Action action = () => TraversalTransitionAnchor.Solid(new Vector3d(100, 0, 0));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TraversalTransitionAnchor_Create_ShouldThrow_WhenPointOverrideIsInDifferentVoxel()
    {
        Voxel voxelA = TestRequire.VoxelAt(new Vector3d(0, 0, 0));

        // A point override that resolves to a completely different voxel.
        Action action = () => TraversalTransitionAnchor.Solid(voxelA.WorldIndex, new Vector3d(2, 0, 0));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TraversalTransitionAnchor_Position_ShouldUseVoxelWorldPosition_WhenNoOverride()
    {
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        var anchor = TraversalTransitionAnchor.Solid(voxel.WorldIndex);
        anchor.Position.Should().Be(voxel.WorldPosition);
    }
}
