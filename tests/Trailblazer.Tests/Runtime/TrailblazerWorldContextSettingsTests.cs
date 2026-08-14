using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Runtime;

public sealed class TrailblazerWorldContextSettingsTests
{
    [Fact]
    public void Default_ShouldExposeFiniteRuntimeCeilings()
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;

        settings.OperationLimits.MaxPendingOperations.Should().Be(32);
        settings.OperationLimits.MaxPendingDescriptorBytes.Should().Be(1_048_576);
        settings.OperationLimits.MaxPreparedMapBytes.Should().Be(16_777_216);
        settings.OperationLimits.MaxBatchItems.Should().Be(32);
        settings.OperationLimits.MaxBatchDescriptorBytes.Should().Be(1_048_576);
        settings.OperationLimits.MaxBatchSortScratchBytes.Should().Be(1_048_576);
        settings.OperationLimits.MaxCorridorCells.Should().Be(64);
        settings.OperationLimits.MaxMaps.Should().Be(16);
        settings.OperationLimits.MaxRetainedMapIdentities.Should().Be(32);
        settings.OperationLimits.MaxOverlayCellsPerMap.Should().Be(4_096);
        settings.OperationLimits.MaxOverlayConnectionsPerMap.Should().Be(1_024);
        settings.OperationLimits.MaxOverlayTransitionsPerMap.Should().Be(1_024);
        settings.OperationLimits.MaxOverlayCells.Should().Be(16_384);
        settings.OperationLimits.MaxOverlayConnections.Should().Be(4_096);
        settings.OperationLimits.MaxOverlayTransitions.Should().Be(4_096);

        settings.MaintenanceBudget.MaxConsumedEnvelopes.Should().Be(4_096);
        settings.MaintenanceBudget.MaxBaselineAddresses.Should().Be(65_536);
        settings.MaintenanceBudget.MaxOverlaySlots.Should().Be(16_384);
        settings.MaintenanceBudget.MaxComponentNodes.Should().Be(65_536);
        settings.MaintenanceBudget.MaxExplicitEdges.Should().Be(65_536);
        settings.MaintenanceBudget.MaxDependencyEntries.Should().Be(65_536);

        settings.MaxIngressEntries.Should().Be(16_384);
        settings.MaxIngressBytes.Should().Be(4_194_304);
        settings.MaxActiveSnapshots.Should().Be(3);
        settings.MaxActiveSnapshotBytes.Should().Be(33_554_432);
        settings.MaxRetiredSnapshots.Should().Be(8);
        settings.MaxRetiredSnapshotBytes.Should().Be(67_108_864);
        settings.MaxPersistentGraphPages.Should().Be(262_144);
        settings.MaxDynamicCellSlotsPerMap.Should().Be(4_096);
        settings.MaxDynamicCellSlots.Should().Be(16_384);
        settings.MaxAreaPolicies.Should().Be(64);
        settings.MaxAreaRulesPerPolicy.Should().Be(4_096);
        settings.MaxAreaRules.Should().Be(65_536);
        settings.MaxConcurrentSnapshotLeases.Should().Be(8);
    }

    [Fact]
    public void Constructor_ShouldRetainExplicitLimits()
    {
        NavigationOperationLimits operationLimits = CreateOperationLimits();
        var maintenanceBudget = new MaintenanceWorkBudget(1, 32, 3, 4, 7, 21);

        var settings = new TrailblazerWorldContextSettings(
            operationLimits,
            maintenanceBudget,
            maxIngressEntries: 10,
            maxIngressBytes: 256,
            maxActiveSnapshots: 12,
            maxActiveSnapshotBytes: TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes,
            maxRetiredSnapshots: 14,
            maxRetiredSnapshotBytes: 15,
            maxPersistentGraphPages: TrailblazerWorldContextSettings.MinimumPersistentGraphPages,
            maxDynamicCellSlotsPerMap: 17,
            maxDynamicCellSlots: 18,
            navigationAreaCount: 19,
            maxAreaPolicies: 20,
            maxAreaRulesPerPolicy: 21,
            maxAreaRules: 22,
            maxConcurrentSnapshotLeases: 23);

        settings.OperationLimits.Should().Be(operationLimits);
        settings.MaintenanceBudget.Should().Be(maintenanceBudget);
        settings.MaxIngressEntries.Should().Be(10);
        settings.MaxIngressBytes.Should().Be(256);
        settings.MaxActiveSnapshots.Should().Be(12);
        settings.MaxActiveSnapshotBytes.Should().Be(2_088);
        settings.MaxRetiredSnapshots.Should().Be(14);
        settings.MaxRetiredSnapshotBytes.Should().Be(15);
        settings.MaxPersistentGraphPages.Should().Be(30);
        settings.MaxDynamicCellSlotsPerMap.Should().Be(17);
        settings.MaxDynamicCellSlots.Should().Be(18);
        settings.NavigationAreaCount.Should().Be(19);
        settings.MaxAreaPolicies.Should().Be(20);
        settings.MaxAreaRulesPerPolicy.Should().Be(21);
        settings.MaxAreaRules.Should().Be(22);
        settings.MaxConcurrentSnapshotLeases.Should().Be(23);
    }

    [Fact]
    public void MaintenanceBudget_ShouldRejectNonPositiveCounters()
    {
        for (int invalidIndex = 0; invalidIndex < 6; invalidIndex++)
        {
            int[] values = { 1, 1, 1, 1, 1, 1 };
            values[invalidIndex] = 0;

            Action create = () => _ = new MaintenanceWorkBudget(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5]);

            create.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Constructor_ShouldRejectInvalidNestedAndPositiveLimits()
    {
        Action defaultOperations = () => _ = CreateSettings(operationLimits: new NavigationOperationLimits());
        Action defaultMaintenance = () => _ = CreateSettings(maintenanceBudget: new MaintenanceWorkBudget());
        Action ingressEntries = () => _ = CreateSettings(maxIngressEntries: 0);
        Action ingressBytes = () => _ = CreateSettings(maxIngressBytes: 0);
        Action undersizedIngressBytes = () => _ = CreateSettings(
            maxIngressBytes: TrailblazerWorldContextSettings.MinimumIngressBytes - 1);
        Action activeSnapshots = () => _ = CreateSettings(maxActiveSnapshots: 2);
        Action activeSnapshotBytes = () => _ = CreateSettings(maxActiveSnapshotBytes: 0);
        Action undersizedActiveSnapshotBytes = () => _ = CreateSettings(
            maxActiveSnapshotBytes: TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes - 1);
        Action persistentPages = () => _ = CreateSettings(maxPersistentGraphPages: 0);
        Action undersizedPersistentPages = () => _ = CreateSettings(
            maxPersistentGraphPages: TrailblazerWorldContextSettings.MinimumPersistentGraphPages - 1);
        Action areaCount = () => _ = CreateSettings(navigationAreaCount: 0);
        Action areaPolicies = () => _ = CreateSettings(maxAreaPolicies: 0);
        Action areaRulesPerPolicy = () => _ = CreateSettings(maxAreaRulesPerPolicy: 0);
        Action areaRules = () => _ = CreateSettings(maxAreaRules: 0);
        Action concurrentLeases = () => _ = CreateSettings(maxConcurrentSnapshotLeases: 0);
        Action areaPolicyWork = () => _ = CreateSettings(
            maintenanceBudget: new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1));
        Action catalogCopyWork = () => _ = CreateSettings(
            maintenanceBudget: new MaintenanceWorkBudget(1, 1, 1, 1, 1, 4),
            navigationAreaCount: 1,
            maxAreaPolicies: 2,
            maxAreaRulesPerPolicy: 1,
            maxAreaRules: 2);

        defaultOperations.Should().Throw<ArgumentException>();
        defaultMaintenance.Should().Throw<ArgumentException>();
        ingressEntries.Should().Throw<ArgumentOutOfRangeException>();
        ingressBytes.Should().Throw<ArgumentOutOfRangeException>();
        undersizedIngressBytes.Should().Throw<ArgumentOutOfRangeException>();
        activeSnapshots.Should().Throw<ArgumentOutOfRangeException>();
        activeSnapshotBytes.Should().Throw<ArgumentOutOfRangeException>();
        undersizedActiveSnapshotBytes.Should().Throw<ArgumentOutOfRangeException>();
        persistentPages.Should().Throw<ArgumentOutOfRangeException>();
        undersizedPersistentPages.Should().Throw<ArgumentOutOfRangeException>();
        areaCount.Should().Throw<ArgumentOutOfRangeException>();
        areaPolicies.Should().Throw<ArgumentOutOfRangeException>();
        areaRulesPerPolicy.Should().Throw<ArgumentOutOfRangeException>();
        areaRules.Should().Throw<ArgumentOutOfRangeException>();
        concurrentLeases.Should().Throw<ArgumentOutOfRangeException>();
        areaPolicyWork.Should().Throw<ArgumentException>();
        catalogCopyWork.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldAllowDisabledRetentionAndDynamicSlots()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 0,
            maxDynamicCellSlotsPerMap: 0,
            maxDynamicCellSlots: 0);

        settings.MaxRetiredSnapshots.Should().Be(0);
        settings.MaxRetiredSnapshotBytes.Should().Be(0);
        settings.MaxDynamicCellSlotsPerMap.Should().Be(0);
        settings.MaxDynamicCellSlots.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeOrInconsistentAggregateLimits()
    {
        Action retiredSnapshots = () => _ = CreateSettings(maxRetiredSnapshots: -1);
        Action retiredSnapshotBytes = () => _ = CreateSettings(maxRetiredSnapshotBytes: -1);
        Action dynamicPerMap = () => _ = CreateSettings(maxDynamicCellSlotsPerMap: -1);
        Action dynamicTotal = () => _ = CreateSettings(maxDynamicCellSlots: -1);
        Action dynamicAggregate = () => _ = CreateSettings(
            maxDynamicCellSlotsPerMap: 2,
            maxDynamicCellSlots: 1);
        Action areaAggregate = () => _ = CreateSettings(
            maxAreaRulesPerPolicy: 2,
            maxAreaRules: 1);

        retiredSnapshots.Should().Throw<ArgumentOutOfRangeException>();
        retiredSnapshotBytes.Should().Throw<ArgumentOutOfRangeException>();
        dynamicPerMap.Should().Throw<ArgumentOutOfRangeException>();
        dynamicTotal.Should().Throw<ArgumentOutOfRangeException>();
        dynamicAggregate.Should().Throw<ArgumentException>();
        areaAggregate.Should().Throw<ArgumentException>();
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        NavigationOperationLimits? operationLimits = null,
        MaintenanceWorkBudget? maintenanceBudget = null,
        int maxIngressEntries = 1,
        long maxIngressBytes = 256,
        int maxActiveSnapshots = 3,
        long maxActiveSnapshotBytes = 2_088,
        int maxRetiredSnapshots = 1,
        long maxRetiredSnapshotBytes = 1,
        int maxPersistentGraphPages = 30,
        int maxDynamicCellSlotsPerMap = 1,
        int maxDynamicCellSlots = 1,
        int navigationAreaCount = 1,
        int maxAreaPolicies = 1,
        int maxAreaRulesPerPolicy = 1,
        int maxAreaRules = 1,
        int maxConcurrentSnapshotLeases = 1) => new(
            operationLimits ?? CreateOperationLimits(),
            maintenanceBudget ?? new MaintenanceWorkBudget(1, 1, 1, 1, 1, 3),
            maxIngressEntries,
            maxIngressBytes,
            maxActiveSnapshots,
            maxActiveSnapshotBytes,
            maxRetiredSnapshots,
            maxRetiredSnapshotBytes,
            maxPersistentGraphPages,
            maxDynamicCellSlotsPerMap,
            maxDynamicCellSlots,
            navigationAreaCount,
            maxAreaPolicies,
            maxAreaRulesPerPolicy,
            maxAreaRules,
            maxConcurrentSnapshotLeases);

    private static NavigationOperationLimits CreateOperationLimits() => new(
        maxPendingOperations: 1,
        maxPendingDescriptorBytes: 1,
        maxPreparedMapBytes: 1,
        maxBatchItems: 1,
        maxBatchDescriptorBytes: 1,
        maxBatchSortScratchBytes: 4_000,
        maxCorridorCells: 2,
        maxMaps: 1,
        maxRetainedMapIdentities: 1,
        maxOverlayCellsPerMap: 0,
        maxOverlayConnectionsPerMap: 0,
        maxOverlayTransitionsPerMap: 0,
        maxOverlayCells: 0,
        maxOverlayConnections: 0,
        maxOverlayTransitions: 0);
}
