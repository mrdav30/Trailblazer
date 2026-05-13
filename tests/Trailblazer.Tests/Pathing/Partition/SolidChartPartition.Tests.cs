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
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
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
    public void GetHashCode_ShouldUseStoredWorldIndex_WithoutResolvingVoxel()
    {
        var partition = new SolidChartPartition();

        Action act = () => _ = partition.GetHashCode();

        act.Should().NotThrow("pathing heaps hash partitions frequently and must not perform live voxel lookups");
        int first = partition.GetHashCode();
        int second = partition.GetHashCode();
        second.Should().Be(first);
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
        var (grid, voxel) = TestRequire.GridAndVoxelAt(Vector3d.Zero);
        voxel!.TryGetPartition(out SolidChartPartition? partition).Should().BeTrue();
        partition!.IsWalkable.Should().BeTrue();
        partition.HasAnyOwners.Should().BeTrue();

        grid!.TryAddObstacle(voxel, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeFalse();

        grid!.TryRemoveObstacle(voxel, _obstacleKey).Should().BeTrue();
        partition.IsWalkable.Should().BeTrue();

        partition.HandleChange(default);
        partition.IsWalkable.Should().BeFalse();
    }

    [Fact]
    public void HandleChange_ShouldInvalidateOwningContext_WhenObstacleEventFiresWithoutActivePathingState()
    {
        TestWorld.Reset();

        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out ushort gridIndex).Should().BeTrue();
        context.World.TryGetGrid(gridIndex, out VoxelGrid? grid).Should().BeTrue();
        context.Pathing.Register(PathTestFactory.BuildSinglePointMap("OwnerScopedObstacle", Vector3d.Zero))
            .Should()
            .BeTrue();
        context.World.TryGetVoxel(Vector3d.Zero, out Voxel? voxel).Should().BeTrue();
        SolidPartitionReachability.SolidPartitionReachabilityStats before =
            context.Guides.CaptureReachabilityStats();

        Action act = () => grid!.TryAddObstacle(voxel!, _obstacleKey).Should().BeTrue();

        act.Should().NotThrow();
        context.Guides.CaptureReachabilityStats().Version.Should().BeGreaterThan(before.Version);
    }

    /// <summary>
    /// Covers the <c>ChartOwners?.Clear()</c> null-conditional branch in <c>Reset</c>.
    /// A freshly constructed <c>SolidChartPartition</c> never has <c>ApplyAuthoredState</c>
    /// called, so <c>ChartOwners</c> is null and the <c>?.Clear()</c> must silently no-op.
    /// </summary>
    [Fact]
    public void Reset_ShouldBeNoOp_WhenChartOwnersHasNotBeenInitialized()
    {
        var partition = new SolidChartPartition();

        // Reset on a partition that has never had ApplyAuthoredState called.
        Action act = () => partition.Reset();
        act.Should().NotThrow("null ChartOwners must be handled safely by the null-conditional");

        partition.HasAnyOwners.Should().BeFalse();
        partition.EffectiveChartOwner.Should().BeNull();
    }

    [Fact]
    public void IsImpassable_ShouldReturnFalse_WhenUnitSizeIsZeroOrNegative()
    {
        PathManager.Register(PathTestFactory.BuildSinglePointMap("IsImpassableZeroSize", Vector3d.Zero));
        PathManager.InitializeChart("IsImpassableZeroSize");

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        voxel!.TryGetPartition(out SolidChartPartition? partition).Should().BeTrue();

        // unitSize <= 0 should return false without performing any clearance check.
        partition!.IsImpassable(Fixed64.Zero).Should().BeFalse();
        partition.IsImpassable(Fixed64.Zero - Fixed64.One).Should().BeFalse();
    }

    [Fact]
    public void IsImpassable_ShouldSkipClearance_WhenUnitFitsWithinSingleVoxel()
    {
        PathManager.Register(PathTestFactory.BuildSinglePointMap("IsImpassableSingleVoxel", Vector3d.Zero));

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);

        typeof(SolidChartPartition)
            .GetProperty(nameof(SolidChartPartition.Neighbors))!
            .SetValue(partition, null);

        partition.IsImpassable(Fixed64.One).Should().BeFalse();

        Action oversizedCheck = () => partition.IsImpassable(Fixed64.Two);
        oversizedCheck.Should().Throw<InvalidOperationException>(
            "units larger than one voxel still require clearance data");
    }

    [Fact]
    public void GetNeighborClearance_ShouldReturnZero_WhenVoxelHasObstacle()
    {
        PathManager.Register(PathTestFactory.BuildSinglePointMap("ClearanceObstacle", Vector3d.Zero));
        PathManager.InitializeChart("ClearanceObstacle");

        var (grid, voxel) = TestRequire.GridAndVoxelAt(Vector3d.Zero);
        voxel!.TryGetPartition(out SolidChartPartition? partition).Should().BeTrue();

        // Before obstacle: clearance should be at least 1.
        partition!.GetNeighborClearance().Should().BeGreaterThan(0);
        // Add obstacle → IsWalkable = false.
        grid!.TryAddObstacle(voxel, _obstacleKey).Should().BeTrue();

        // CheckClearance: TryGetClearanceOrigin returns false because !IsWalkable.
        // origin.IsBlocked == true → clearance is set to 0.
        partition.GetNeighborClearance().Should().Be(0);

        // Cleanup.
        grid!.TryRemoveObstacle(voxel, _obstacleKey).Should().BeTrue();
    }

    [Fact]
    public void HasAnyOwners_AndBelongsTo_ShouldReturnFalse_WhenChartOwnersIsNull()
    {
        // Exercises the ChartOwners == null branch in HasAnyOwners and BelongsTo.
        // A freshly created partition has never been through ApplyAuthoredState,
        // so its ChartOwners field is null.
        var partition = new SolidChartPartition();

        partition.HasAnyOwners.Should().BeFalse("ChartOwners is null on a fresh uninitialized partition");
        partition.BelongsTo("AnyChart").Should().BeFalse("ChartOwners is null so Contains returns false");
    }

    [Fact]
    public void GetNeighborClearance_ShouldReturnCachedValue_OnSecondCall()
    {
        // Exercises the _isClearanceValid == true early-return branch in CheckClearance.
        PathManager.Register(PathTestFactory.BuildSinglePointMap("ClearanceCached", Vector3d.Zero));
        PathManager.InitializeChart("ClearanceCached");

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);

        byte first = partition.GetNeighborClearance();  // computes and marks valid
        byte second = partition.GetNeighborClearance(); // uses cached value

        second.Should().Be(first, "second call returns the cached clearance without recomputing");

        PathManager.UnloadChart("ClearanceCached");
    }

    [Fact]
    public void GetNeighborClearance_ShouldReturnDefaultMax_WhenIsWalkableForcedFalseOnUnblockedVoxel()
    {
        // Exercises the ternary false-branch in CheckClearance:
        // origin?.IsBlocked == true ? 0 : DefaultDegreeCap
        // HandleChange(default) sets IsWalkable = false (VoxelIndex == default)
        // but the underlying voxel is not actually blocked, so DefaultDegreeCap is returned.
        PathManager.Register(PathTestFactory.BuildSinglePointMap("ClearanceDefault", Vector3d.Zero));
        PathManager.InitializeChart("ClearanceDefault");

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);

        // Force IsWalkable = false without blocking the voxel.
        partition.HandleChange(default);
        partition.IsWalkable.Should().BeFalse();

        // Voxel is not blocked → origin.IsBlocked == false → DefaultDegreeCap is returned.
        byte clearance = partition.GetNeighborClearance();
        clearance.Should().BeGreaterThan(0, "unblocked voxel falls through to DefaultDegreeCap path");

        PathManager.UnloadChart("ClearanceDefault");
    }
}
