using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class VolumeMediumRulesTests : IDisposable
{
    public VolumeMediumRulesTests()
    {
        TrailblazerWorldManager.Setup();
        TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        VolumeMediumRules.ClearGasVoxelRule();
        VolumeMediumRules.ClearLiquidVoxelRule();
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumeMediumRules_ShouldStartWithNoRules()
    {
        VolumeMediumRules.HasGasVoxelRule.Should().BeFalse();
        VolumeMediumRules.HasLiquidVoxelRule.Should().BeFalse();
    }

    [Fact]
    public void VolumeMediumRules_SetGasVoxelRule_ShouldRegisterAndClear()
    {
        int versionBefore = VolumeMediumRules.RegistryVersion;

        VolumeMediumRules.SetGasVoxelRule(static v => false);
        VolumeMediumRules.HasGasVoxelRule.Should().BeTrue();
        VolumeMediumRules.RegistryVersion.Should().BeGreaterThan(versionBefore);

        VolumeMediumRules.ClearGasVoxelRule();
        VolumeMediumRules.HasGasVoxelRule.Should().BeFalse();
    }

    [Fact]
    public void VolumeMediumRules_SetLiquidVoxelRule_ShouldRegisterAndClear()
    {
        int versionBefore = VolumeMediumRules.RegistryVersion;

        VolumeMediumRules.SetLiquidVoxelRule(static v => false);
        VolumeMediumRules.HasLiquidVoxelRule.Should().BeTrue();
        VolumeMediumRules.RegistryVersion.Should().BeGreaterThan(versionBefore);

        VolumeMediumRules.ClearLiquidVoxelRule();
        VolumeMediumRules.HasLiquidVoxelRule.Should().BeFalse();
    }

    [Fact]
    public void VolumeMediumRules_SetGasVoxelPartition_ShouldRegisterRuleForPartitionType()
    {
        VolumeMediumRules.SetGasVoxelPartition<VolumeChartPartition>();
        VolumeMediumRules.HasGasVoxelRule.Should().BeTrue();
        VolumeMediumRules.ClearGasVoxelRule();
    }

    [Fact]
    public void VolumeMediumRules_SetLiquidVoxelPartition_ShouldRegisterRuleForPartitionType()
    {
        VolumeMediumRules.SetLiquidVoxelPartition<VolumeChartPartition>();
        VolumeMediumRules.HasLiquidVoxelRule.Should().BeTrue();
        VolumeMediumRules.ClearLiquidVoxelRule();
    }

    [Fact]
    public void VolumeMediumRules_Matches_ShouldReturnFalse_ForVoxelWithNoPartition()
    {
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        VolumeMediumRules.SetGasVoxelRule(static v => true);

        // The voxel has no SolidChartPartition or VolumeChartPartition, so Matches returns false.
        VolumeMediumRules.Matches(voxel, TraversalMedium.Gas).Should().BeFalse();

        VolumeMediumRules.ClearGasVoxelRule();
    }

    [Fact]
    public void VolumeMediumRules_Matches_ShouldReturnFalse_ForNullVoxel()
    {
        VolumeMediumRules.SetGasVoxelRule(static v => true);
        VolumeMediumRules.Matches(null!, TraversalMedium.Gas).Should().BeFalse();
        VolumeMediumRules.ClearGasVoxelRule();
    }

    [Fact]
    public void VolumeMediumRules_Matches_ShouldReturnFalse_ForUnknownMedium()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(1, 0, 0), TraversalMedium.Gas, "VMRGasTest");
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        VolumeMediumRules.Matches(voxel, TraversalMedium.Unknown).Should().BeFalse();
    }

    [Fact]
    public void VolumeMediumRules_IsConfigured_ShouldReflectPresenceOfRulesOrAuthoredCharts()
    {
        VolumeMediumRules.IsConfigured(TraversalMedium.Gas).Should().BeFalse();
        VolumeMediumRules.IsConfigured(TraversalMedium.Liquid).Should().BeFalse();
        VolumeMediumRules.IsConfigured(TraversalMedium.Unknown).Should().BeFalse();

        VolumeMediumRules.SetGasVoxelRule(static v => false);
        VolumeMediumRules.IsConfigured(TraversalMedium.Gas).Should().BeTrue();
        VolumeMediumRules.ClearGasVoxelRule();

        VolumeMediumRules.SetLiquidVoxelRule(static v => false);
        VolumeMediumRules.IsConfigured(TraversalMedium.Liquid).Should().BeTrue();
        VolumeMediumRules.ClearLiquidVoxelRule();
    }
}

public sealed class ManagedChartTransitionStateTests
{
    [Fact]
    public void ManagedChartTransitionState_Constructor_ShouldThrow_ForNullOrWhitespacePrefix()
    {
        Action nullPrefix = () => _ = new ManagedChartTransitionState(null!, priority: 0);
        nullPrefix.Should().Throw<ArgumentException>();

        Action emptyPrefix = () => _ = new ManagedChartTransitionState(string.Empty, priority: 0);
        emptyPrefix.Should().Throw<ArgumentException>();

        Action whitespace = () => _ = new ManagedChartTransitionState("   ", priority: 0);
        whitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManagedChartTransitionState_ShouldStoreProperties()
    {
        var state = new ManagedChartTransitionState("prefix", priority: 3);
        state.TransitionIdPrefix.Should().Be("prefix");
        state.Priority.Should().Be(3);
        Assert.NotNull(state.TransitionIds);
        state.TransitionIds.Count.Should().Be(0);
    }
}

public sealed class ChartOwnerUtilityTests
{
    [Fact]
    public void ChartOwnerUtility_AddOwners_ShouldCopyItemsFromSource()
    {
        var source = new SwiftHashSet<string>();
        source.Add("chart-a");
        source.Add("chart-b");

        var destination = new SwiftHashSet<string>();
        ChartOwnerUtility.AddOwners(destination, source);

        destination.Contains("chart-a").Should().BeTrue();
        destination.Contains("chart-b").Should().BeTrue();
    }

    [Fact]
    public void ChartOwnerUtility_AddOwners_ShouldNotThrow_WhenDestinationIsNull()
    {
        var source = new SwiftHashSet<string>();
        source.Add("chart-a");

        Action action = () => ChartOwnerUtility.AddOwners(null!, source);
        action.Should().NotThrow();
    }

    [Fact]
    public void ChartOwnerUtility_AddOwners_ShouldNotThrow_WhenSourceIsNull()
    {
        var destination = new SwiftHashSet<string>();

        Action action = () => ChartOwnerUtility.AddOwners(destination, null);
        action.Should().NotThrow();
        destination.Count.Should().Be(0);
    }
}
