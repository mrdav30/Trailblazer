using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class VolumeMediumRulesTests : IDisposable
{
    public VolumeMediumRulesTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        VolumeMediumRules.ClearGasVoxelRule();
        VolumeMediumRules.ClearLiquidVoxelRule();
        PathManager.Reset();
        TestWorld.Reset();
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
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

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
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "VMRGasTest");
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

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

public sealed class NavigationChartRegistrationValueTests
{
    [Fact]
    public void NavigationChartRegistration_Constructor_ShouldThrow_ForNullOrWhitespacePrefix()
    {
        NavigationChart chart = CreateChart(priority: 0);

        Action nullPrefix = () => _ = new NavigationChartRegistration(chart, registrationOrder: 1, null!);
        nullPrefix.Should().Throw<ArgumentException>();

        Action emptyPrefix = () => _ = new NavigationChartRegistration(chart, registrationOrder: 1, string.Empty);
        emptyPrefix.Should().Throw<ArgumentException>();

        Action whitespace = () => _ = new NavigationChartRegistration(chart, registrationOrder: 1, "   ");
        whitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NavigationChartRegistration_ShouldStoreProperties()
    {
        NavigationChart chart = CreateChart(priority: 3);
        var state = new NavigationChartRegistration(chart, registrationOrder: 7, "prefix");
        state.TransitionIdPrefix.Should().Be("prefix");
        state.Priority.Should().Be(3);
        state.RegistrationOrder.Should().Be(7);
        state.Chart.Should().BeSameAs(chart);
        Assert.NotNull(state.TransitionIds);
        state.TransitionIds.Count.Should().Be(0);
    }

    private static NavigationChart CreateChart(int priority)
    {
        bool[,,] data =
        {
            {
                { true }
            }
        };

        return NavigationChart.From3D("RegistrationValueChart", data, Vector3d.Zero, Fixed64.One, priority: priority);
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
