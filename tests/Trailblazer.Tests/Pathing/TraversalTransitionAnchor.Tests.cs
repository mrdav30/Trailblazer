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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TraversalTransitionAnchor_GasAndLiquid_ShouldCreateFromVoxelIndex()
    {
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel voxel).Should().BeTrue();

        var gas = TraversalTransitionAnchor.Gas(voxel.GlobalIndex);
        gas.Medium.Should().Be(TraversalMedium.Gas);
        gas.VoxelIndex.Should().Be(voxel.GlobalIndex);
        gas.HasPointOverride.Should().BeFalse();
        gas.IsVolumeMedium.Should().BeTrue();

        var liquid = TraversalTransitionAnchor.Liquid(voxel.GlobalIndex);
        liquid.Medium.Should().Be(TraversalMedium.Liquid);
        liquid.VoxelIndex.Should().Be(voxel.GlobalIndex);
        liquid.HasPointOverride.Should().BeFalse();
        liquid.IsVolumeMedium.Should().BeTrue();
    }

    [Fact]
    public void TraversalTransitionAnchor_ShouldCreateFromVoxelIndexWithPointOverride()
    {
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel voxel).Should().BeTrue();
        var pointOverride = new Vector3d(1.1, 0, 0);

        var gasWithOverride = TraversalTransitionAnchor.Gas(voxel.GlobalIndex, pointOverride);
        gasWithOverride.HasPointOverride.Should().BeTrue();
        gasWithOverride.PointOverride.Should().Be(pointOverride);
        gasWithOverride.Position.Should().Be(pointOverride);

        var liquidWithOverride = TraversalTransitionAnchor.Liquid(voxel.GlobalIndex, pointOverride);
        liquidWithOverride.HasPointOverride.Should().BeTrue();
        liquidWithOverride.PointOverride.Should().Be(pointOverride);

        var solidWithOverride = TraversalTransitionAnchor.Solid(voxel.GlobalIndex, pointOverride);
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
        GlobalGridManager.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxelA).Should().BeTrue();

        // A point override that resolves to a completely different voxel.
        Action action = () => TraversalTransitionAnchor.Solid(voxelA.GlobalIndex, new Vector3d(2, 0, 0));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TraversalTransitionAnchor_Position_ShouldUseVoxelWorldPosition_WhenNoOverride()
    {
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel voxel).Should().BeTrue();
        var anchor = TraversalTransitionAnchor.Solid(voxel.GlobalIndex);
        anchor.Position.Should().Be(voxel.WorldPosition);
    }
}
