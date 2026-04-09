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
public sealed class SolidChartPartitionTests : IDisposable
{
    private readonly BoundsKey _obstacleKey = new(Vector3d.Zero, Vector3d.Zero);

    public SolidChartPartitionTests()
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
    public void VoxelGetter_ShouldThrow_WhenPartitionIsDetached()
    {
        var partition = new SolidChartPartition();

        Action act = () => _ = partition.Voxel;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyAuthoredStateAndReset_ShouldTrackAndClearOwners()
    {
        var partition = new SolidChartPartition();
        var state = new ResolvedChartVoxelState();
        state.AddOwner("OwnedChart", NavigationChartCell.Solid, priority: 0, registrationOrder: 1);

        partition.ApplyAuthoredState(
            state,
            effectiveChartOwner: "OwnedChart",
            effectiveCell: new NavigationChartCell(
                TraversalMedia.Solid,
                pathCostModifier: 7,
                flags: NavigationChartCellFlags.TransitionSourceHint));

        partition.HasAnyOwners.Should().BeTrue();
        partition.BelongsTo("OwnedChart").Should().BeTrue();
        partition.EffectiveChartOwner.Should().Be("OwnedChart");
        partition.PathCostModifier.Should().Be(7);
        partition.ChartFlags.Should().Be(NavigationChartCellFlags.TransitionSourceHint);

        partition.Reset();

        partition.HasAnyOwners.Should().BeFalse();
        partition.BelongsTo("OwnedChart").Should().BeFalse();
        partition.EffectiveChartOwner.Should().BeNull();
        partition.PathCostModifier.Should().Be(0);
        partition.ChartFlags.Should().Be(NavigationChartCellFlags.None);
    }

    [Fact]
    public void HandleChange_ShouldTrackObstacleAddAndRemoval()
    {
        PathManager.Register(PathTestFactory.BuildSinglePointMap("SolidPartitionObstacle", Vector3d.Zero));
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel voxel).Should().BeTrue();
        voxel.TryGetPartition(out SolidChartPartition partition).Should().BeTrue();
        partition.IsWalkable.Should().BeTrue();
        partition.HasAnyOwners.Should().BeTrue();

        GridObstacleManager.TryAddObstacle(voxel.GlobalIndex, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeFalse();

        GridObstacleManager.TryRemoveObstacle(voxel.GlobalIndex, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeTrue();

        partition.HandleChange(default);
        partition.IsWalkable.Should().BeFalse();
    }
}
