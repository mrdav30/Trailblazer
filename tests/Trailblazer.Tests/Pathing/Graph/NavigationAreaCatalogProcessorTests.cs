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
    public void PolicyPublicationLimits_ShouldRejectInvalidCountsWithoutChangingCatalog()
    {
        NavigationAreaPolicy twoRules = CreatePolicy("ground", 1, 2);

        NavigationAreaCatalog.Empty.TryPublish(
                twoRules,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 2,
                maxRules: 2,
                out NavigationAreaCatalog invalidShape)
            .Should().Be(NavigationOperationRejection.ValidationFailed);
        invalidShape.Should().BeSameAs(NavigationAreaCatalog.Empty);

        NavigationAreaCatalog.Empty.TryPublish(
                twoRules,
                maxPolicies: 1,
                requiredRuleCount: 2,
                maxRulesPerPolicy: 1,
                maxRules: 2,
                out NavigationAreaCatalog oversizedPolicy)
            .Should().Be(NavigationOperationRejection.CapacityExceeded);
        oversizedPolicy.Should().BeSameAs(NavigationAreaCatalog.Empty);

        NavigationAreaPolicy oneRule = CreatePolicy("ground", 1, 1);
        NavigationAreaCatalog.Empty.TryPublish(
                oneRule,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 2,
                maxRules: 1,
                out NavigationAreaCatalog current)
            .Should().Be(NavigationOperationRejection.None);
        NavigationAreaPolicy replacement = CreatePolicy("ground", 2, 2);

        current.TryPublish(
                replacement,
                maxPolicies: 1,
                requiredRuleCount: 2,
                maxRulesPerPolicy: 2,
                maxRules: 1,
                out NavigationAreaCatalog overTotal)
            .Should().Be(NavigationOperationRejection.CapacityExceeded);

        overTotal.Should().BeSameAs(current);
        current.Version.Should().Be(1);
        current.TotalRuleCount.Should().Be(1);
        current.TryGet(oneRule.Key, out NavigationAreaPolicy? retained).Should().BeTrue();
        retained.Should().BeSameAs(oneRule);
    }

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
    public void Prepare_ShouldPublishAtMostTheConfiguredBatchPrefix()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 2,
            maxAreaRules: 2,
            maxDependencyEntries: 8,
            maxBatchItems: 1);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var first = new NavigationAreaPolicyCommitOperation(CreatePolicy("a", 1, 1), 1, 1);
        var second = new NavigationAreaPolicyCommitOperation(CreatePolicy("b", 1, 1), 2, 1);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(second).Should().BeTrue();

        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            NavigationAreaCatalog.Empty,
            new MaintenanceWorkMeter(settings.MaintenanceBudget),
            long.MaxValue,
            int.MaxValue);

        frame.Count.Should().Be(1,
            "the configured publication prefix is an exact deterministic frame bound");
        frame.Candidate.TryGet(first.Policy.Key, out _).Should().BeTrue();
        frame.Candidate.TryGet(second.Policy.Key, out _).Should().BeFalse();
        frame.Complete(1);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
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
    public void AdmissionDescriptorCeiling_ShouldSaturateWithoutArithmeticOverflow()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3,
            maxPendingDescriptorBytes: long.MaxValue);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("ground", 1, 1),
            1,
            1);

        processor.Admit(operation).Should().BeTrue(
            "adding the maximum rule allowance to an unbounded descriptor ceiling must saturate");
        processor.PendingRetainedBytes.Should().Be(operation.Policy.RetainedBytes);
    }

    [Fact]
    public void Prepare_ShouldRejectAPolicyThatFitsBytesButExceedsThePageCeiling()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("ground", 1, 1),
            1,
            1);
        processor.Admit(operation).Should().BeTrue();
        NavigationAreaCatalog current = NavigationAreaCatalog.Empty;

        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            current,
            new MaintenanceWorkMeter(settings.MaintenanceBudget),
            maxCatalogBytes: long.MaxValue,
            maxCatalogPages: current.PersistentPageCount);

        frame.Count.Should().Be(1);
        frame.Candidate.Should().BeSameAs(current,
            "an over-page candidate must not escape the prepared publication prefix");
        frame.Complete(1);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.PendingCount.Should().Be(0);
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
    public void Runtime_ShouldRetainAreaPolicyUntilLeasePressureClears()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3,
            maxRetiredSnapshots: 0);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        using NavigationWorldGraphLease pressure =
            context.Pathing.TryAcquireNavigationGraph()!;
        var operation = new NavigationAreaPolicyCommitOperation(
            CreatePolicy("leased", 1, 1),
            publicationSequence: 1,
            effectiveFrame: context.FrameCount + 1);

        context.Pathing.Admit(operation).Should().BeTrue();
        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending,
            "transient snapshot pressure must not terminally reject prepared policy work");
        context.Pathing.TryResolveNavigationAreaPolicy(operation.Policy.Key, out _)
            .Should().BeFalse();
        context.Pathing.GetNavigationGraphDiagnostics().PendingAreaPolicyCount.Should().Be(1);

        pressure.Dispose();
        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryResolveNavigationAreaPolicy(
                operation.Policy.Key,
                out NavigationAreaPolicy? published)
            .Should().BeTrue();
        published.Should().BeSameAs(operation.Policy);
        context.Pathing.GetNavigationGraphDiagnostics().PendingAreaPolicyCount.Should().Be(0);
    }

    [Theory]
    [InlineData(
        (int)NavigationCandidatePublication.Published,
        true,
        (int)NavigationOperationStatus.Applied,
        (int)NavigationOperationRejection.None,
        0)]
    [InlineData(
        (int)NavigationCandidatePublication.PermanentCapacity,
        true,
        (int)NavigationOperationStatus.Rejected,
        (int)NavigationOperationRejection.CapacityExceeded,
        0)]
    [InlineData(
        (int)NavigationCandidatePublication.Deferred,
        true,
        (int)NavigationOperationStatus.Pending,
        (int)NavigationOperationRejection.None,
        1)]
    [InlineData(
        (int)NavigationCandidatePublication.Published,
        false,
        (int)NavigationOperationStatus.Pending,
        (int)NavigationOperationRejection.None,
        1)]
    public void RuntimePolicyFrameCompletion_ShouldFollowPublicationDisposition(
        int publicationValue,
        bool policyPrepared,
        int expectedStatusValue,
        int expectedRejectionValue,
        int expectedPendingCount)
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

        NavigationGraphRuntime.CompletePolicyFrame(
            frame,
            1,
            policyPrepared,
            (NavigationCandidatePublication)publicationValue);

        operation.Receipt.Status.Should().Be((NavigationOperationStatus)expectedStatusValue);
        operation.Receipt.Rejection.Should().Be(
            (NavigationOperationRejection)expectedRejectionValue);
        processor.PendingCount.Should().Be(expectedPendingCount);
        if (expectedPendingCount == 0)
        {
            processor.PendingRuleCount.Should().Be(0);
            processor.PendingRetainedBytes.Should().Be(0);
        }
    }

    [Fact]
    public void PermanentCapacity_ShouldPreserveAnEarlierSemanticRejection()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRules: 1,
            maxDependencyEntries: 3);
        NavigationAreaPolicy existing = CreatePolicy("ground", 1, 1);
        NavigationAreaCatalog.Empty.TryPublish(
                existing,
                1,
                1,
                1,
                1,
                out NavigationAreaCatalog current)
            .Should().Be(NavigationOperationRejection.None);
        var conflictingRules = new[] { new NavigationAreaRule(false, Fixed64.One) };
        var conflict = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(existing.Key, conflictingRules),
            1,
            1);
        var processor = new NavigationAreaCatalogProcessor(settings);
        processor.Admit(conflict).Should().BeTrue();
        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            1,
            current,
            new MaintenanceWorkMeter(settings.MaintenanceBudget),
            long.MaxValue,
            int.MaxValue);

        frame.Count.Should().Be(1);
        frame.Candidate.Should().BeSameAs(current);
        frame.CompleteCapacityRejected();

        conflict.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        conflict.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed,
            "a later runtime capacity decision must not erase the prepared semantic failure");
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

    [Fact]
    public void AdmissionStateMachine_ShouldRejectEachInvalidOrderingWithoutRetainingIt()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            navigationAreaCount: 1,
            maxAreaPolicies: 2,
            maxAreaRules: 2,
            maxDependencyEntries: 5,
            maxPendingOperations: 1);
        var processor = new NavigationAreaCatalogProcessor(settings);
        var accepted = new NavigationAreaPolicyCommitOperation(CreatePolicy("accepted", 1, 1), 10, 5);
        processor.Admit(accepted).Should().BeTrue();

        processor.Admit(accepted).Should().BeFalse(
            "an already-claimed receipt cannot be admitted twice");

        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("duplicate", 1, 1), 10, 5),
            NavigationOperationRejection.DuplicateSequence);
        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("regressing", 1, 1), 9, 5),
            NavigationOperationRejection.RegressingSequence);
        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("old-frame", 1, 1), 11, 4),
            NavigationOperationRejection.RegressingEffectiveFrame);

        NavigationAreaCatalogProcessor.PreparedFrame frame = processor.Prepare(
            5,
            NavigationAreaCatalog.Empty,
            new MaintenanceWorkMeter(settings.MaintenanceBudget),
            long.MaxValue,
            int.MaxValue);
        frame.Complete(5);
        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("late", 1, 1), 12, 5),
            NavigationOperationRejection.LateEffectiveFrame);
        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("wrong-shape", 1, 2), 13, 6),
            NavigationOperationRejection.ValidationFailed);

        var retained = new NavigationAreaPolicyCommitOperation(CreatePolicy("retained", 1, 1), 14, 7);
        processor.Admit(retained).Should().BeTrue();
        AssertRejected(
            processor,
            new NavigationAreaPolicyCommitOperation(CreatePolicy("queue-full", 1, 1), 15, 7),
            NavigationOperationRejection.CapacityExceeded);

        processor.PendingCount.Should().Be(1);
        processor.PendingRuleCount.Should().Be(1);
        processor.PendingRetainedBytes.Should().Be(retained.Policy.RetainedBytes);
    }

    private static void AssertRejected(
        NavigationAreaCatalogProcessor processor,
        NavigationAreaPolicyCommitOperation operation,
        NavigationOperationRejection expected)
    {
        processor.Admit(operation).Should().BeFalse();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(expected);
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
        long? maxActiveSnapshotBytes = null,
        int? maxRetiredSnapshots = null,
        int? maxPendingOperations = null,
        int? maxBatchItems = null)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        NavigationOperationLimits limits = defaults.OperationLimits;
        var operationLimits = new NavigationOperationLimits(
            maxPendingOperations ?? limits.MaxPendingOperations,
            maxPendingDescriptorBytes,
            limits.MaxPreparedMapBytes,
            maxBatchItems ?? limits.MaxBatchItems,
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
            maxRetiredSnapshots ?? defaults.MaxRetiredSnapshots,
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
