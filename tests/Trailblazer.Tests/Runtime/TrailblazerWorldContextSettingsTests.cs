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
        settings.MaintenanceBudget.MaxSeamCandidateProbes.Should().Be(65_536);
        settings.MaintenanceBudget.MaxExplicitEdges.Should().Be(65_536);
        settings.MaintenanceBudget.MaxDependencyEntries.Should().Be(65_536);
        settings.MaintenanceBudget.MaxSurfaceComponentEdges.Should().Be(65_536);

        settings.GuideSampleBudget.Should().Be(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 8,
            maxPortalChecks: 32,
            maxPrismChecks: 32,
            maxTraceIntervals: 32,
            maxLocalRecoveryAttempts: 1));

        settings.MaxIngressEntries.Should().Be(16_384);
        settings.MaxIngressBytes.Should().Be(4_194_304);
        settings.MaxActiveSnapshots.Should().Be(3);
        settings.MaxActiveSnapshotBytes.Should().Be(66_379_544);
        settings.MaxRetiredSnapshots.Should().Be(8);
        settings.MaxRetiredSnapshotBytes.Should().Be(67_108_864);
        settings.MaxPersistentGraphPages.Should().Be(660_550);
        settings.MaxDynamicCellSlotsPerMap.Should().Be(4_096);
        settings.MaxDynamicCellSlots.Should().Be(16_384);
        settings.MaxAreaPolicies.Should().Be(64);
        settings.MaxAreaRulesPerPolicy.Should().Be(4_096);
        settings.MaxAreaRules.Should().Be(65_536);
        settings.MaxConcurrentSnapshotLeases.Should().Be(8);
        settings.QueryLimits.MaxBatchItems.Should().Be(8);
        settings.QueryLimits.MaxBatchDescriptorBytes.Should().Be(65_536);
        settings.QueryLimits.MaxConcurrentNavigationQueries.Should().Be(8);
        settings.QueryLimits.AStarWorkspaceMapCapacity.Should().Be(16);
        settings.QueryLimits.AStarWorkspaceEndpointPageCapacity.Should().Be(512);
        settings.QueryLimits.AStarWorkspaceComponentCapacity.Should().Be(512);
        settings.QueryLimits.AStarWorkspaceNodeCapacity.Should().Be(4_096);
        settings.QueryLimits.MaxAStarCacheEntries.Should().Be(128);
        settings.QueryLimits.MaxAStarReusablePayloadBytes.Should().Be(16_777_216);
        settings.QueryLimits.MaxAStarSinglePayloadBytes.Should().Be(1_126_720);
        settings.QueryLimits.MaxAStarActivePayloadBytes.Should().Be(4_194_304);
        settings.QueryLimits.MaxAStarActivePayloadLeases.Should().Be(8);
        settings.QueryLimits.FlowWorkspaceMapCapacity.Should().Be(16);
        settings.QueryLimits.FlowWorkspaceEndpointPageCapacity.Should().Be(512);
        settings.QueryLimits.FlowWorkspaceComponentCapacity.Should().Be(512);
        settings.QueryLimits.FlowWorkspaceNodeCapacity.Should().Be(4_096);
        settings.QueryLimits.RayWorkspaceCoveredAddressCapacity.Should().Be(4_096);
        settings.QueryLimits.RayWorkspaceTraceIntervalCapacity.Should().Be(4_096);
        settings.QueryLimits.AStarWorkspaceGuidePointCapacity.Should().Be(8_191);
        settings.QueryLimits.AStarWorkspaceGuidePointCapacity.Should().Be(
            (settings.QueryLimits.AStarWorkspaceNodeCapacity * 2) - 1);
        NavigationAStarPayload.GetMaximumRetainedBytes(
                settings.QueryLimits.AStarWorkspaceGuidePointCapacity,
                settings.QueryLimits.AStarWorkspaceNodeCapacity - 1,
                settings.QueryLimits.AStarWorkspaceComponentCapacity,
                settings.QueryLimits.AStarWorkspaceEndpointPageCapacity)
            .Should().BeLessThanOrEqualTo(
                settings.QueryLimits.MaxAStarSinglePayloadBytes);
        settings.QueryLimits.MaxFlowCacheEntries.Should().Be(128);
        settings.QueryLimits.MaxFlowReusablePayloadBytes.Should().Be(33_554_432);
        settings.QueryLimits.MaxFlowSinglePayloadBytes.Should().Be(524_288);
        settings.QueryLimits.MaxFlowActivePayloadBytes.Should().Be(4_194_304);
        settings.QueryLimits.MaxFlowActivePayloadLeases.Should().Be(8);
    }

    [Fact]
    public void Constructor_ShouldRetainExplicitLimits()
    {
        NavigationOperationLimits operationLimits = CreateOperationLimits();
        var maintenanceBudget = new MaintenanceWorkBudget(1, 32, 3, 4, 5, 7, 21, 23);
        var guideSampleBudget = new GuideSampleWorkBudget(2, 3, 5, 7, 11, 13, 17);
        NavigationQueryLimits queryLimits = CreateQueryLimits(maxConcurrentQueries: 2);

        var settings = new TrailblazerWorldContextSettings(
            operationLimits,
            maintenanceBudget,
            guideSampleBudget,
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
            maxConcurrentSnapshotLeases: 23,
            queryLimits);

        settings.OperationLimits.Should().Be(operationLimits);
        settings.MaintenanceBudget.Should().Be(maintenanceBudget);
        settings.GuideSampleBudget.Should().Be(guideSampleBudget);
        settings.MaxIngressEntries.Should().Be(10);
        settings.MaxIngressBytes.Should().Be(256);
        settings.MaxActiveSnapshots.Should().Be(12);
        settings.MaxActiveSnapshotBytes.Should().Be(
            TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes);
        settings.MaxRetiredSnapshots.Should().Be(14);
        settings.MaxRetiredSnapshotBytes.Should().Be(15);
        settings.MaxPersistentGraphPages.Should().Be(
            TrailblazerWorldContextSettings.MinimumPersistentGraphPages);
        settings.MaxDynamicCellSlotsPerMap.Should().Be(17);
        settings.MaxDynamicCellSlots.Should().Be(18);
        settings.NavigationAreaCount.Should().Be(19);
        settings.MaxAreaPolicies.Should().Be(20);
        settings.MaxAreaRulesPerPolicy.Should().Be(21);
        settings.MaxAreaRules.Should().Be(22);
        settings.MaxConcurrentSnapshotLeases.Should().Be(23);
        settings.QueryLimits.Should().Be(queryLimits);
    }

    [Fact]
    public void MaintenanceBudget_ShouldRejectNonPositiveCounters()
    {
        for (int invalidIndex = 0; invalidIndex < 8; invalidIndex++)
        {
            int[] values = { 1, 1, 1, 1, 1, 1, 1, 1 };
            values[invalidIndex] = 0;

            Action create = () => _ = new MaintenanceWorkBudget(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                values[7]);

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
        Action defaultQueries = () => _ = CreateSettings(
            queryLimits: new NavigationQueryLimits());
        Action queryLeaseMismatch = () => _ = CreateSettings(
            maxConcurrentSnapshotLeases: 1,
            queryLimits: CreateQueryLimits(maxConcurrentQueries: 2));
        Action areaPolicyWork = () => _ = CreateSettings(
            maintenanceBudget: new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1));
        Action catalogCopyWork = () => _ = CreateSettings(
            maintenanceBudget: new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 4),
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
        defaultQueries.Should().Throw<ArgumentException>();
        queryLeaseMismatch.Should().Throw<ArgumentException>();
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

    [Fact]
    public void QueryLimits_ShouldRejectNegativeComponentCapacity()
    {
        Action aStar = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            aStarComponentCapacity: -1);
        Action flow = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            flowComponentCapacity: -1);

        aStar.Should().Throw<ArgumentOutOfRangeException>();
        flow.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void QueryLimits_ShouldRejectInvalidRayAndGuidePointCapacities()
    {
        Action zeroCoveredAddresses = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            rayCoveredAddressCapacity: 0);
        Action negativeTraceIntervals = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            rayTraceIntervalCapacity: -1);
        Action traceExceedsCovered = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 2);
        Action guidePointsBelowNodes = () => _ = CreateQueryLimits(
            maxConcurrentQueries: 1,
            aStarGuidePointCapacity: 0);

        zeroCoveredAddresses.Should().Throw<ArgumentOutOfRangeException>();
        negativeTraceIntervals.Should().Throw<ArgumentOutOfRangeException>();
        traceExceedsCovered.Should().Throw<ArgumentException>();
        guidePointsBelowNodes.Should().Throw<ArgumentException>();
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        NavigationOperationLimits? operationLimits = null,
        MaintenanceWorkBudget? maintenanceBudget = null,
        GuideSampleWorkBudget? guideSampleBudget = null,
        int maxIngressEntries = 1,
        long maxIngressBytes = 256,
        int maxActiveSnapshots = 3,
        long? maxActiveSnapshotBytes = null,
        int maxRetiredSnapshots = 1,
        long maxRetiredSnapshotBytes = 1,
        int? maxPersistentGraphPages = null,
        int maxDynamicCellSlotsPerMap = 1,
        int maxDynamicCellSlots = 1,
        int navigationAreaCount = 1,
        int maxAreaPolicies = 1,
        int maxAreaRulesPerPolicy = 1,
        int maxAreaRules = 1,
        int maxConcurrentSnapshotLeases = 1,
        NavigationQueryLimits? queryLimits = null) => new(
            operationLimits ?? CreateOperationLimits(),
            maintenanceBudget ?? new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 3),
            guideSampleBudget ?? new GuideSampleWorkBudget(1, 1, 1, 1, 1, 1, 1),
            maxIngressEntries,
            maxIngressBytes,
            maxActiveSnapshots,
            maxActiveSnapshotBytes ?? TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes,
            maxRetiredSnapshots,
            maxRetiredSnapshotBytes,
            maxPersistentGraphPages ?? TrailblazerWorldContextSettings.MinimumPersistentGraphPages,
            maxDynamicCellSlotsPerMap,
            maxDynamicCellSlots,
            navigationAreaCount,
            maxAreaPolicies,
            maxAreaRulesPerPolicy,
            maxAreaRules,
            maxConcurrentSnapshotLeases,
            queryLimits ?? CreateQueryLimits(maxConcurrentSnapshotLeases));

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

    private static NavigationQueryLimits CreateQueryLimits(
        int maxConcurrentQueries,
        int aStarComponentCapacity = 1,
        int flowComponentCapacity = 1,
        int rayCoveredAddressCapacity = 1,
        int rayTraceIntervalCapacity = 1,
        int aStarGuidePointCapacity = 1) => new(
            maxBatchItems: 1,
            maxBatchDescriptorBytes: 264,
            maxConcurrentNavigationQueries: maxConcurrentQueries,
            aStarWorkspaceMapCapacity: 1,
            aStarWorkspaceEndpointPageCapacity: 1,
            aStarWorkspaceComponentCapacity: aStarComponentCapacity,
            aStarWorkspaceNodeCapacity: 1,
            maxAStarCacheEntries: 1,
            maxAStarReusablePayloadBytes: 1,
            maxAStarSinglePayloadBytes: 1,
            maxAStarActivePayloadBytes: 1,
            maxAStarActivePayloadLeases: 1,
            flowWorkspaceMapCapacity: 1,
            flowWorkspaceEndpointPageCapacity: 1,
            flowWorkspaceComponentCapacity: flowComponentCapacity,
            flowWorkspaceNodeCapacity: 1,
            rayWorkspaceCoveredAddressCapacity: rayCoveredAddressCapacity,
            rayWorkspaceTraceIntervalCapacity: rayTraceIntervalCapacity,
            aStarWorkspaceGuidePointCapacity: aStarGuidePointCapacity,
            maxFlowCacheEntries: 1,
            maxFlowReusablePayloadBytes: 1,
            maxFlowSinglePayloadBytes: 1,
            maxFlowActivePayloadBytes: 1,
            maxFlowActivePayloadLeases: 1);
}
