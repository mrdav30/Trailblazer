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
public sealed class VolumeChartPartitionTests : IDisposable
{
    private readonly BoundsKey _obstacleKey = new(Vector3d.Zero, Vector3d.Zero);

    public VolumeChartPartitionTests()
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
    public void HasAnyOwners_ShouldBeFalse_WhenPartitionIsUninitialized()
    {
        var partition = new VolumeChartPartition();
        partition.HasAnyOwners.Should().BeFalse();
    }

    [Fact]
    public void SupportsMedium_ShouldReturnFalse_ForSolidMedium()
    {
        var partition = new VolumeChartPartition();
        // TraversalMedium.Solid is not a volume type; exercises the default switch arm.
        partition.SupportsMedium(TraversalMedium.Solid).Should().BeFalse();
    }

    [Fact]
    public void BelongsTo_ShouldReturnFalse_WhenPartitionHasNoOwners()
    {
        var partition = new VolumeChartPartition();
        partition.BelongsTo("any-chart").Should().BeFalse();
    }

    [Fact]
    public void HandleChange_ShouldSetIsWalkableFalse_WhenEventIndexIsDefault()
    {
        var partition = new VolumeChartPartition();
        // default ObstacleEventInfo has VoxelIndex == default, making the condition false.
        partition.HandleChange(default);
        partition.IsWalkable.Should().BeFalse();
    }

    [Fact]
    public void HandleChange_ShouldTrackObstacleEvents_WhenAttachedToVoxel()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(Vector3d.Zero, TraversalMedium.Liquid, "VolumePartitionObstacle");
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel voxel).Should().BeTrue();
        voxel.TryGetPartition(out VolumeChartPartition partition).Should().BeTrue();
        partition.IsWalkable.Should().BeTrue();

        GridObstacleManager.TryAddObstacle(voxel.GlobalIndex, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeFalse();

        GridObstacleManager.TryRemoveObstacle(voxel.GlobalIndex, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeTrue();
    }

    [Fact]
    public void ApplyAuthoredState_ShouldPopulateOwnersAndVolumeKinds()
    {
        var partition = new VolumeChartPartition();
        var state = new ResolvedChartVoxelState();
        state.AddOwner("LiquidChart", new NavigationChartCell(TraversalMedia.Liquid), priority: 0, registrationOrder: 1);

        partition.ApplyAuthoredState(
            state,
            effectiveChartOwner: "LiquidChart",
            effectiveCell: new NavigationChartCell(TraversalMedia.Liquid));

        partition.HasAnyOwners.Should().BeTrue();
        partition.BelongsTo("LiquidChart").Should().BeTrue();
        partition.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
        partition.SupportsMedium(TraversalMedium.Solid).Should().BeFalse();
        partition.EffectiveChartOwner.Should().Be("LiquidChart");

        // Null state clears the owner list without throwing (exercises the null-conditional path).
        partition.ApplyAuthoredState(
            null,
            effectiveChartOwner: null,
            effectiveCell: NavigationChartCell.Empty);

        partition.HasAnyOwners.Should().BeFalse();
        partition.EffectiveChartOwner.Should().BeNull();
    }
}
