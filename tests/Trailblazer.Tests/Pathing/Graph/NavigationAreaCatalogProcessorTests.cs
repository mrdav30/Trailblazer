//=======================================================================
// NavigationAreaCatalogProcessorTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationAreaCatalogProcessorTests
{
    [Fact]
    public void Prepare_ShouldMeterOneDeterministicEligiblePrefix()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 2,
            maxAreaPolicies: 3,
            maxAreaRules: 6,
            maxDependencyEntries: 7);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var first = new NavigationAreaPolicyCommitOperation(CreatePolicy("a", 1, 2), 1, 1);
        var second = new NavigationAreaPolicyCommitOperation(CreatePolicy("b", 1, 2), 2, 1);
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);

        processor.Admit(first).Should().BeTrue();
        processor.Admit(second).Should().BeTrue();

        NavigationAreaCatalogProcessor.PreparedFrame firstFrame = processor.Prepare(
            1,
            NavigationAreaCatalog.Empty,
            meter,
            long.MaxValue,
            int.MaxValue);

        firstFrame.Count.Should().Be(1);
        meter.DependencyEntries.Should().Be(3);
        firstFrame.Complete(1);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        meter.Reset();
        NavigationAreaCatalogProcessor.PreparedFrame secondFrame = processor.Prepare(
            2,
            firstFrame.Candidate,
            meter,
            long.MaxValue,
            int.MaxValue);

        secondFrame.Count.Should().Be(1);
        meter.DependencyEntries.Should().Be(5);
        secondFrame.Complete(2);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.PendingCount.Should().Be(0);
        processor.PendingRuleCount.Should().Be(0);
        processor.PendingRetainedBytes.Should().Be(0);
    }

    [Fact]
    public void Prepare_ShouldReserveExactComparisonWorkBeforeSameRevisionScan()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 4,
            maxAreaPolicies: 1,
            maxAreaRules: 4,
            maxDependencyEntries: 5);
        NavigationAreaPolicy currentPolicy = CreatePolicy("ground", 1, 4);
        NavigationAreaCatalog.Empty.TryPublish(
            currentPolicy,
            1,
            4,
            4,
            4,
            out NavigationAreaCatalog current).Should().Be(NavigationOperationRejection.None);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var operation = new NavigationAreaPolicyCommitOperation(CreatePolicy("ground", 1, 4), 1, 1);
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        meter.TryConsumeDependencyEntries(1).Should().BeTrue();
        processor.Admit(operation).Should().BeTrue();

        NavigationAreaCatalogProcessor.PreparedFrame deferred = processor.Prepare(
            1,
            current,
            meter,
            long.MaxValue,
            int.MaxValue);

        deferred.Count.Should().Be(0);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        meter.Reset();
        NavigationAreaCatalogProcessor.PreparedFrame applied = processor.Prepare(
            2,
            current,
            meter,
            long.MaxValue,
            int.MaxValue);

        applied.Count.Should().Be(1);
        applied.Candidate.Should().BeSameAs(current);
        meter.DependencyEntries.Should().Be(5);
        applied.Complete(2);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void Admission_ShouldBoundPendingRulesAndRetainedBytes()
    {
        TrailblazerWorldContextSettings ruleSettings = CreateSettings(
            navigationAreaCount: 2,
            maxAreaPolicies: 2,
            maxAreaRules: 2,
            maxDependencyEntries: 4);
        var ruleProcessor = new NavigationAreaCatalogProcessor(ruleSettings);
        var first = new NavigationAreaPolicyCommitOperation(CreatePolicy("a", 1, 2), 1, 1);
        var overRuleLimit = new NavigationAreaPolicyCommitOperation(CreatePolicy("b", 1, 2), 2, 1);

        ruleProcessor.Admit(first).Should().BeTrue();
        ruleProcessor.Admit(overRuleLimit).Should().BeFalse();
        overRuleLimit.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        ruleProcessor.PendingCount.Should().Be(1);
        ruleProcessor.PendingRuleCount.Should().Be(2);
        ruleProcessor.PendingRetainedBytes.Should().Be(first.Policy.RetainedBytes);

        ruleProcessor.Reset();
        ruleProcessor.PendingCount.Should().Be(0);
        ruleProcessor.PendingRuleCount.Should().Be(0);
        ruleProcessor.PendingRetainedBytes.Should().Be(0);

        TrailblazerWorldContextSettings byteSettings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3,
            maxPendingDescriptorBytes: 64);
        var byteProcessor = new NavigationAreaCatalogProcessor(byteSettings);
        var oversizedId = new string('x', 128);
        var overByteLimit = new NavigationAreaPolicyCommitOperation(
            CreatePolicy(oversizedId, 1, 1),
            1,
            1);

        byteProcessor.Admit(overByteLimit).Should().BeFalse();
        overByteLimit.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        byteProcessor.PendingRetainedBytes.Should().Be(0);
    }

    [Fact]
    public void SameRevision_ShouldUseExactContentAndRemainCatalogNoOp()
    {
        NavigationAreaPolicy first = CreatePolicy("ground", 1, 2);
        NavigationAreaCatalog.Empty.TryPublish(
            first,
            2,
            2,
            2,
            4,
            out NavigationAreaCatalog current).Should().Be(NavigationOperationRejection.None);
        long retainedBytes = current.RetainedBytes;

        current.TryPublish(first, 2, 2, 2, 4, out NavigationAreaCatalog sameReference)
            .Should().Be(NavigationOperationRejection.None);
        current.TryPublish(CreatePolicy("ground", 1, 2), 2, 2, 2, 4, out NavigationAreaCatalog sameContent)
            .Should().Be(NavigationOperationRejection.None);
        var conflict = new NavigationAreaPolicy(
            first.Key,
            new[]
            {
                new NavigationAreaRule(false, Fixed64.Zero),
                new NavigationAreaRule(true, Fixed64.Zero)
            });
        current.TryPublish(conflict, 2, 2, 2, 4, out NavigationAreaCatalog rejected)
            .Should().Be(NavigationOperationRejection.ValidationFailed);

        sameReference.Should().BeSameAs(current);
        sameContent.Should().BeSameAs(current);
        rejected.Should().BeSameAs(current);
        current.Version.Should().Be(1);
        current.RetainedBytes.Should().Be(retainedBytes);
    }

    [Fact]
    public void Prepare_ShouldDeterministicallyKeepFirstSameRevisionWinner()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 2,
            maxAreaRules: 2,
            maxDependencyEntries: 5);
        var processor = new NavigationAreaCatalogProcessor(settings);
        NavigationAreaPolicy firstPolicy = CreatePolicy("ground", 1, 1);
        var conflictPolicy = new NavigationAreaPolicy(
            firstPolicy.Key,
            new[] { new NavigationAreaRule(false, Fixed64.Zero) });
        var first = new NavigationAreaPolicyCommitOperation(firstPolicy, 1, 1);
        var conflict = new NavigationAreaPolicyCommitOperation(conflictPolicy, 2, 1);
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(conflict).Should().BeTrue();

        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            NavigationAreaCatalog.Empty,
            meter,
            long.MaxValue,
            int.MaxValue);

        frame.Count.Should().Be(1);
        frame.Complete(1);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        conflict.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        meter.Reset();
        frame = processor.Prepare(2, frame.Candidate, meter, long.MaxValue, int.MaxValue);
        frame.Complete(2);
        conflict.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        frame.Candidate.TryGet(firstPolicy.Key, out NavigationAreaPolicy? winner).Should().BeTrue();
        winner.Should().BeSameAs(firstPolicy);
    }

    [Fact]
    public void Prepare_ShouldTerminallyRejectNewPolicyAtExactFullCatalogBudget()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3);
        NavigationAreaPolicy existing = CreatePolicy("existing", 1, 1);
        NavigationAreaCatalog.Empty.TryPublish(
            existing,
            1,
            1,
            1,
            1,
            out NavigationAreaCatalog full).Should().Be(NavigationOperationRejection.None);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("additional", 1, 1),
            1,
            1);
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        processor.Admit(operation).Should().BeTrue();

        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            full,
            meter,
            long.MaxValue,
            int.MaxValue);

        frame.Count.Should().Be(1);
        meter.DependencyEntries.Should().Be(3);
        frame.Complete(1);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.PendingCount.Should().Be(0);
    }

    [Fact]
    public void DependencyStamp_ShouldTrackOnlyExactPolicyIdentity()
    {
        NavigationAreaPolicy firstPolicy = CreatePolicy("first", 1, 1);
        NavigationAreaPolicy secondPolicy = CreatePolicy("second", 1, 1);
        NavigationAreaCatalog.Empty.TryPublish(firstPolicy, 2, 1, 1, 2, out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        catalog.TryPublish(secondPolicy, 2, 1, 1, 2, out catalog)
            .Should().Be(NavigationOperationRejection.None);
        var graph = new NavigationWorldGraph(1, Array.Empty<NavigationMapInstance>(), catalog);
        graph.TryGetDependencyStamp(
            firstPolicy.Key,
            ReadOnlySpan<NavigationSurfaceComponentKey>.Empty,
            ReadOnlySpan<GraphPageDependencyAddress>.Empty,
            out GraphDependencyStamp stamp).Should().BeTrue();

        catalog.TryPublish(CreatePolicy("second", 2, 1), 2, 1, 1, 2, out NavigationAreaCatalog unrelated)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph unrelatedRevision = graph.WithAreaCatalog(unrelated, 2);
        unrelatedRevision.IsDependencyCurrent(stamp).Should().BeTrue();

        unrelated.TryPublish(CreatePolicy("first", 2, 1), 2, 1, 1, 2, out NavigationAreaCatalog depended)
            .Should().Be(NavigationOperationRejection.None);
        unrelatedRevision.WithAreaCatalog(depended, 3).IsDependencyCurrent(stamp).Should().BeFalse();
    }

    [Fact]
    public void Runtime_ShouldRejectAreaPolicyWhenCatalogCannotFitSnapshotCapacity()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 512,
            maxAreaPolicies: 1,
            maxAreaRules: 512,
            maxDependencyEntries: 513,
            maxActiveSnapshotBytes: TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("ground", 1, 512),
            1,
            context.FrameCount + 1);

        context.Pathing.Admit(operation).Should().BeTrue();
        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.TryResolveNavigationAreaPolicy(operation.Policy.Key, out _).Should().BeFalse();
    }

    [Fact]
    public void PermanentCapacity_ShouldTerminallyRejectPreparedPrefixAndReleaseAccounting()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var operation = new NavigationAreaPolicyCommitOperation(CreatePolicy("ground", 1, 1), 1, 1);
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        processor.Admit(operation).Should().BeTrue();
        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            NavigationAreaCatalog.Empty,
            meter,
            long.MaxValue,
            int.MaxValue);

        frame.CompleteCapacityRejected();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.PendingCount.Should().Be(0);
        processor.PendingRuleCount.Should().Be(0);
        processor.PendingRetainedBytes.Should().Be(0);
    }

    [Fact]
    public void Diagnostics_ShouldIncludePendingAreaPolicyRetention()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("future", 1, context.Settings.NavigationAreaCount),
            1,
            context.FrameCount + 2);

        context.Pathing.Admit(operation).Should().BeTrue();
        NavigationGraphDiagnosticsSnapshot diagnostics = context.Pathing.GetNavigationGraphDiagnostics();

        diagnostics.PendingAreaPolicyCount.Should().Be(1);
        diagnostics.PendingAreaRuleCount.Should().Be(context.Settings.NavigationAreaCount);
        diagnostics.PendingAreaPolicyBytes.Should().Be(operation.Policy.RetainedBytes);
        diagnostics.ActiveSnapshotBytes.Should().BeGreaterThanOrEqualTo(
            NavigationWorldGraph.Empty.RetainedBytes + operation.Policy.RetainedBytes);
    }

    private static NavigationAreaPolicy CreatePolicy(string id, long revision, int ruleCount)
    {
        var rules = new NavigationAreaRule[ruleCount];
        for (int i = 0; i < rules.Length; i++)
            rules[i] = new NavigationAreaRule(true, (Fixed64)i);
        return new NavigationAreaPolicy(new NavigationAreaPolicyKey(id, revision), rules);
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        int navigationAreaCount,
        int maxAreaPolicies,
        int maxAreaRules,
        int maxDependencyEntries,
        long maxPendingDescriptorBytes = 1_048_576,
        long? maxActiveSnapshotBytes = null)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        NavigationOperationLimits limits = defaults.OperationLimits;
        var operationLimits = new NavigationOperationLimits(
            limits.MaxPendingOperations,
            maxPendingDescriptorBytes,
            limits.MaxPreparedMapBytes,
            limits.MaxBatchItems,
            limits.MaxBatchDescriptorBytes,
            limits.MaxBatchSortScratchBytes,
            limits.MaxCorridorCells,
            limits.MaxMaps,
            limits.MaxRetainedMapIdentities,
            limits.MaxOverlayCellsPerMap,
            limits.MaxOverlayConnectionsPerMap,
            limits.MaxOverlayTransitionsPerMap,
            limits.MaxOverlayCells,
            limits.MaxOverlayConnections,
            limits.MaxOverlayTransitions,
            limits.MaxTransitionRulesPerMap,
            limits.MaxTransitionRules);
        MaintenanceWorkBudget budget = defaults.MaintenanceBudget;
        var maintenanceBudget = new MaintenanceWorkBudget(
            budget.MaxConsumedEnvelopes,
            budget.MaxBaselineAddresses,
            budget.MaxOverlaySlots,
            budget.MaxComponentNodes,
            budget.MaxSeamCandidateProbes,
            budget.MaxExplicitEdges,
            maxDependencyEntries);
        return new TrailblazerWorldContextSettings(
            operationLimits,
            maintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            maxActiveSnapshotBytes ?? defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            navigationAreaCount,
            maxAreaPolicies,
            navigationAreaCount,
            maxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }
}
