using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections.Utility;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationGraphCapacityTests
{
    [Fact]
    public void ZeroCapacityIngress_ShouldFailClosedToTheExactConfigurationScope()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 0);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1));
        ingress.HasPendingWork.Should().BeTrue();
        var blocked = new NavigationGridChangeScope[1];

        ingress.DetachInto(
                Span<GridEventInfo>.Empty,
                blocked,
                out int blockedCount,
                out bool blockAll)
            .Should().Be(0);

        blockedCount.Should().Be(1);
        blocked[0].ConfigurationKey.Should().Be(configuration.ToGridKey());
        blockAll.Should().BeFalse();
        ingress.HasPendingWork.Should().BeFalse(
            "detaching the fail-closed scope resets even a zero-slot free list");
        ingress.Dispose();
    }

    [Fact]
    public void BlockedScopeMerge_ShouldDeduplicateBeforePromotingOverflowToAllBlocked()
    {
        GridConfiguration first = CreateConfiguration();
        GridConfiguration second = new(
            new Vector3d(4, 0, 0),
            new Vector3d(6, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var firstScope = new NavigationGridChangeScope(first.ToGridKey(), 1, 1, 1);
        var secondScope = new NavigationGridChangeScope(second.ToGridKey(), 1, 2, 1);
        var merged = new NavigationGridChangeScope[2];

        int mergedCount = NavigationGraphRuntime.MergeBlockedScopes(
            new[] { firstScope },
            blockedScopeCount: 1,
            new[] { firstScope, secondScope },
            deferredScopeCount: 2,
            merged,
            out bool blockAll);

        blockAll.Should().BeFalse();
        mergedCount.Should().Be(2);
        merged[0].Should().Be(firstScope);
        merged[1].Should().Be(secondScope,
            "a duplicate deferred scope must not consume the remaining exact slot");

        mergedCount = NavigationGraphRuntime.MergeBlockedScopes(
            new[] { firstScope },
            blockedScopeCount: 1,
            new[] { secondScope },
            deferredScopeCount: 1,
            new NavigationGridChangeScope[1],
            out blockAll);

        blockAll.Should().BeTrue(
            "an unrepresentable distinct scope must conservatively close the whole graph");
        mergedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true, true, 0, false, 0, true)]
    [InlineData(false, true, 0, false, 0, false)]
    [InlineData(true, false, 0, false, 0, false)]
    [InlineData(true, true, 1, false, 0, false)]
    [InlineData(true, true, 0, true, 0, false)]
    [InlineData(true, true, 0, false, 1, false)]
    public void SafetyGate_ShouldClearOnlyAfterAnExactUnblockedResnapshot(
        bool safetyPending,
        bool resnapshotAll,
        int blockedScopeCount,
        bool deferAll,
        int deferredScopeCount,
        bool expected)
    {
        NavigationGraphRuntime.CanClearSafetyPendingAfterSnapshot(
                safetyPending,
                resnapshotAll,
                blockedScopeCount,
                deferAll,
                deferredScopeCount)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(
        (int)NavigationOperationFrameResult.Deferred,
        1,
        true,
        false,
        true,
        false,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.None)]
    [InlineData(
        (int)NavigationOperationFrameResult.Published,
        0,
        true,
        false,
        true,
        false,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.ReleaseAndBeginRollback)]
    [InlineData(
        (int)NavigationOperationFrameResult.Published,
        0,
        false,
        true,
        true,
        false,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.ReleaseAndBeginRollback)]
    [InlineData(
        (int)NavigationOperationFrameResult.Rejected,
        0,
        false,
        false,
        false,
        false,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.Release)]
    [InlineData(
        (int)NavigationOperationFrameResult.Rejected,
        1,
        true,
        true,
        true,
        false,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.ReleaseAndTryReopen)]
    [InlineData(
        (int)NavigationOperationFrameResult.Rejected,
        0,
        false,
        false,
        true,
        true,
        (int)NavigationGraphRuntime.OperationTerminalDisposition.ReleaseAndBeginRollback)]
    public void TerminalPublication_ShouldClassifyRetainedAndRejectedCleanupDisposition(
        int resultValue,
        int retainedOperationWorkCount,
        bool hasCompositionWork,
        bool materializedCompletesOperation,
        bool hasClosedStructuralScope,
        bool automaticSeamFullRebuildPending,
        int expectedValue)
    {
        NavigationGraphRuntime.ClassifyOperationTerminalDisposition(
                (NavigationOperationFrameResult)resultValue,
                retainedOperationWorkCount,
                hasCompositionWork,
                materializedCompletesOperation,
                hasClosedStructuralScope,
                automaticSeamFullRebuildPending)
            .Should().Be((NavigationGraphRuntime.OperationTerminalDisposition)expectedValue);
    }

    [Theory]
    [InlineData(
        (int)NavigationCandidatePublication.Published,
        (int)NavigationCandidatePublication.Deferred)]
    [InlineData(
        (int)NavigationCandidatePublication.Deferred,
        (int)NavigationCandidatePublication.Deferred)]
    [InlineData(
        (int)NavigationCandidatePublication.PermanentCapacity,
        (int)NavigationCandidatePublication.PermanentCapacity)]
    public void RetainedWorkClosurePublication_ShouldYieldBeforeContinuation(
        int publicationValue,
        int expectedValue)
    {
        NavigationGraphRuntime.ContinueAfterRetainedClosure(
                (NavigationCandidatePublication)publicationValue)
            .Should().Be((NavigationCandidatePublication)expectedValue,
                "a published composition or materialized safety closure consumes the current publication boundary");
    }

    [Theory]
    [InlineData(
        (int)NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Progressed,
        (int)NavigationGraphRuntime.AutomaticSeamAdvanceDisposition.Continue)]
    [InlineData(
        (int)NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Blocked,
        (int)NavigationGraphRuntime.AutomaticSeamAdvanceDisposition.Retain)]
    [InlineData(
        (int)NavigationAutomaticSeamLifecycleWork.AdvanceStatus.RestartRequired,
        (int)NavigationGraphRuntime.AutomaticSeamAdvanceDisposition.Restart)]
    [InlineData(
        (int)NavigationAutomaticSeamLifecycleWork.AdvanceStatus.CapacityExceeded,
        (int)NavigationGraphRuntime.AutomaticSeamAdvanceDisposition.Restart)]
    [InlineData(
        (int)NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Complete,
        (int)NavigationGraphRuntime.AutomaticSeamAdvanceDisposition.Complete)]
    public void AutomaticSeamAdvance_ShouldMapWorkerStatusToLifecycleAction(
        int statusValue,
        int expectedValue)
    {
        NavigationGraphRuntime.ClassifyAutomaticSeamAdvance(
                (NavigationAutomaticSeamLifecycleWork.AdvanceStatus)statusValue)
            .Should().Be((NavigationGraphRuntime.AutomaticSeamAdvanceDisposition)expectedValue);
    }

    [Theory]
    [InlineData(true, false, false, false,
        (int)NavigationGraphRuntime.MaterializedAdvanceDisposition.Restart,
        (int)NavigationCandidatePublication.Deferred)]
    [InlineData(false, false, true, true,
        (int)NavigationGraphRuntime.MaterializedAdvanceDisposition.RejectCapacity,
        (int)NavigationCandidatePublication.PermanentCapacity)]
    [InlineData(false, true, false, true,
        (int)NavigationGraphRuntime.MaterializedAdvanceDisposition.Defer,
        null)]
    [InlineData(false, true, true, false,
        (int)NavigationGraphRuntime.MaterializedAdvanceDisposition.Ready,
        null)]
    [InlineData(false, true, true, true,
        (int)NavigationGraphRuntime.MaterializedAdvanceDisposition.Publish,
        null)]
    public void MaterializedAdvance_ShouldPrioritizeRestartCapacityAndPublication(
        bool requiresRestart,
        bool withinCapacity,
        bool complete,
        bool publish,
        int expectedValue,
        int? expectedAbandonmentPublication)
    {
        NavigationGraphRuntime.ClassifyMaterializedAdvance(
                requiresRestart,
                withinCapacity,
                complete,
                publish,
                out NavigationCandidatePublication? abandonmentPublication)
            .Should().Be((NavigationGraphRuntime.MaterializedAdvanceDisposition)expectedValue);
        abandonmentPublication.Should().Be(
            expectedAbandonmentPublication.HasValue
                ? (NavigationCandidatePublication?)expectedAbandonmentPublication.Value
                : null);
    }

    [Fact]
    public void OwnedClosureBaselineCapacity_ShouldCountOnlyAnUnretainedRoot()
    {
        NavigationWorldGraph emptyOwner = NavigationWorldGraph.Empty;
        NavigationSurfaceComponentKeySet baseline =
            NavigationSurfaceComponentKeySet.Empty.Add(
                new NavigationSurfaceComponentKey(
                    new NavigationCellAddress("map", default),
                    TraversalMedium.Solid));
        NavigationWorldGraph primaryOwner = emptyOwner.WithClosedStructuralComponents(
            baseline,
            closeAllStructuralComponents: false,
            graphVersion: 1);
        NavigationWorldGraph additionalOwner = emptyOwner.WithOwnedStructuralClosure(
            NavigationSurfaceComponentKeySet.Empty,
            baseline,
            closeAllStructuralComponents: false,
            graphVersion: 1);

        AssertAdditionalCapacity(null, emptyOwner, 0, 0);
        AssertAdditionalCapacity(NavigationSurfaceComponentKeySet.Empty, emptyOwner, 0, 0);
        AssertAdditionalCapacity(baseline, primaryOwner, 0, 0);
        AssertAdditionalCapacity(baseline, additionalOwner, 0, 0);
        AssertAdditionalCapacity(
            baseline,
            emptyOwner,
            baseline.RetainedBytes,
            baseline.PersistentPageCount);

        static void AssertAdditionalCapacity(
            NavigationSurfaceComponentKeySet? candidate,
            NavigationWorldGraph owner,
            long expectedBytes,
            int expectedPages)
        {
            NavigationGraphRuntime.GetOwnedClosureBaselineAdditionalCapacity(
                candidate,
                owner,
                out long retainedBytes,
                out int persistentPages);
            retainedBytes.Should().Be(expectedBytes);
            persistentPages.Should().Be(expectedPages);
        }
    }

    [Theory]
    [InlineData(20, 2, 30, 3, 50, 5, true)]
    [InlineData(101, 2, 0, 3, 0, 5, false)]
    [InlineData(20, 2, 81, 3, 0, 5, false)]
    [InlineData(20, 2, 30, 3, 51, 5, false)]
    [InlineData(20, 2, 30, 3, 50, 6, false)]
    public void RetainedWorkCapacity_ShouldEnforceEachAggregateBoundary(
        long rootBytes,
        int rootPages,
        long workBytes,
        int workPages,
        long rebuildBytes,
        int rebuildPages,
        bool expected)
    {
        NavigationGraphRuntime.IsRetainedWorkWithinCapacity(
                rootBytes,
                rootPages,
                workBytes,
                workPages,
                rebuildBytes,
                rebuildPages,
                maximumBytes: 100,
                maximumPages: 10)
            .Should().Be(expected);
    }

    [Fact]
    public void DisposedIngress_ShouldIgnoreCallbacksAndUnpublishedPrefixes()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2);
        GridConfiguration configuration = CreateConfiguration();
        GridEventInfo committed = CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1);
        ingress.Enqueue(committed);

        ingress.Dispose();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 2, obstacleCount: 2));
        ingress.RequeuePrefix(new[] { committed });

        ingress.HasPendingWork.Should().BeFalse();
        var events = new GridEventInfo[2];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(events, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(0);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void DisposedGraphStore_ShouldRejectLeasesAndPublicationIdempotently()
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: 1_000_000,
            maxActiveBytes: 1_000_000,
            maxPersistentPages: 1_000,
            maxConcurrentLeases: 2);
        store.Dispose();
        store.Dispose();

        store.CanPublish.Should().BeFalse(
            "a disposed store must reject publication before a candidate is built");
        store.TryAcquire().Should().BeNull();
        var leases = new NavigationWorldGraphLease?[2];
        store.TryAcquirePrefix(leases).Should().Be(0);
        leases.Should().OnlyContain(lease => lease == null);
        store.TryPublish(new NavigationWorldGraph(
                graphVersion: 1,
                Array.Empty<NavigationMapInstance>()))
            .Should().Be(NavigationCandidatePublication.Deferred);
        store.Current.Should().BeSameAs(NavigationWorldGraph.Empty);
    }

    [Fact]
    public void Ingress_ShouldCoalesceExactFinalStateAndFailClosedOnOverflow()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 2, obstacleCount: 2));

        var coalesced = new GridEventInfo[10];
        var blocked = new NavigationGridChangeScope[2];
        int coalescedCount = ingress.DetachInto(
            coalesced,
            blocked,
            out int blockedCount,
            out bool blockAll);

        coalescedCount.Should().Be(1);
        coalesced[0].ChangeSequence.Should().Be(2);
        coalesced[0].ObstacleCount.Should().Be(2);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();

        ingress.Enqueue(new GridEventInfo(1, 0, 1, configuration, 1, GridEventKind.GridChanged));
        ingress.Enqueue(new GridEventInfo(1, 0, 1, configuration, 2, GridEventKind.GridChanged));
        ingress.Enqueue(new GridEventInfo(1, 0, 1, configuration, 3, GridEventKind.GridChanged));
        ingress.DetachInto(coalesced, blocked, out blockedCount, out blockAll).Should().Be(0);
        blockedCount.Should().Be(1);
        blocked[0].ConfigurationKey.Should().Be(configuration.ToGridKey());
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void Ingress_ShouldMoveInterleavedReplacementToLogicalTail()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 2,
            obstacleCount: 1,
            index: new VoxelIndex(1, 0, 0)));
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 3, obstacleCount: 2));

        var detached = new GridEventInfo[1];
        var blocked = new NavigationGridChangeScope[2];
        ingress.DetachInto(detached, blocked, out int blockedCount, out bool blockAll).Should().Be(1);
        detached[0].ChangeSequence.Should().Be(2);
        blockedCount.Should().Be(1);
        blocked[0].ConfigurationKey.Should().Be(configuration.ToGridKey());
        blockAll.Should().BeFalse();

        ingress.DetachInto(detached, blocked, out blockedCount, out blockAll).Should().Be(1);
        detached[0].ChangeSequence.Should().Be(3);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void Ingress_ShouldMoveAMiddleReplacementToTailWithoutDisturbingItsNeighbors()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 3);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 1,
            obstacleCount: 1,
            index: new VoxelIndex(0, 0, 0)));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 2,
            obstacleCount: 1,
            index: new VoxelIndex(1, 0, 0)));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 3,
            obstacleCount: 1,
            index: new VoxelIndex(2, 0, 0)));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 4,
            obstacleCount: 2,
            index: new VoxelIndex(1, 0, 0)));

        var detached = new GridEventInfo[3];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(detached, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(3);

        detached[0].ChangeSequence.Should().Be(1);
        detached[1].ChangeSequence.Should().Be(3);
        detached[2].ChangeSequence.Should().Be(4);
        detached[2].VoxelIndex.Should().Be(new VoxelIndex(1, 0, 0));
        detached[2].ObstacleCount.Should().Be(2);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void Ingress_ShouldKeepSameAddressEventsFromDistinctGridGenerations()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 4);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 1,
            obstacleCount: 1,
            worldSpawnToken: 1,
            gridIndex: 0,
            gridSpawnToken: 1));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 2,
            obstacleCount: 2,
            worldSpawnToken: 2,
            gridIndex: 0,
            gridSpawnToken: 1));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 3,
            obstacleCount: 3,
            worldSpawnToken: 2,
            gridIndex: 1,
            gridSpawnToken: 1));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 4,
            obstacleCount: 4,
            worldSpawnToken: 2,
            gridIndex: 1,
            gridSpawnToken: 2));

        var detached = new GridEventInfo[4];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(detached, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(4,
                "world, grid slot, and grid generation are all part of coalescing identity");
        detached[0].WorldSpawnToken.Should().Be(1);
        detached[1].WorldSpawnToken.Should().Be(2);
        detached[2].GridIndex.Should().Be(1);
        detached[3].GridSpawnToken.Should().Be(2);
        detached.Should().OnlyContain(info => info.VoxelIndex == default);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void Ingress_ShouldKeepDistinctIdentityFieldsAcrossDeterministicHashCollisions()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(30_000_000, 30_000_000, 30_000_000),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        (GridEventInfo First, GridEventInfo Second)[] collisions =
        {
            (
                CreateVoxelEvent(
                    configuration,
                    sequence: 1,
                    obstacleCount: 1,
                    index: new VoxelIndex(29_138, 125_122, 440_498),
                    gridIndex: 0,
                    worldSpawnToken: 1,
                    gridSpawnToken: 1_715),
                CreateVoxelEvent(
                    configuration,
                    sequence: 2,
                    obstacleCount: 2,
                    index: new VoxelIndex(373_269, 1_602_861, 5_642_949),
                    gridIndex: 0,
                    worldSpawnToken: 1,
                    gridSpawnToken: 21_958)),
            (
                CreateVoxelEvent(
                    configuration,
                    sequence: 3,
                    obstacleCount: 1,
                    index: new VoxelIndex(86_411, 364_429, 988_091),
                    gridIndex: 3_757,
                    worldSpawnToken: 1,
                    gridSpawnToken: 1),
                CreateVoxelEvent(
                    configuration,
                    sequence: 4,
                    obstacleCount: 2,
                    index: new VoxelIndex(231_771, 977_469, 2_650_251),
                    gridIndex: 10_077,
                    worldSpawnToken: 1,
                    gridSpawnToken: 1)),
            (
                CreateVoxelEvent(
                    configuration,
                    sequence: 5,
                    obstacleCount: 1,
                    index: new VoxelIndex(1_665_093, 5_799_117, 15_445_173),
                    gridIndex: 57_417,
                    worldSpawnToken: 57_418,
                    gridSpawnToken: 1),
                CreateVoxelEvent(
                    configuration,
                    sequence: 6,
                    obstacleCount: 2,
                    index: new VoxelIndex(2_111_925, 7_355_325, 19_589_925),
                    gridIndex: 7_290,
                    worldSpawnToken: 72_826,
                    gridSpawnToken: 2))
        };

        foreach ((GridEventInfo first, GridEventInfo second) in collisions)
        {
            GetIngressEventKeyHashCode(first).Should().Be(
                GetIngressEventKeyHashCode(second),
                "the fixture must exercise equality after a real deterministic hash collision");
            var ingress = new NavigationGridChangeIngress(capacity: 2);
            ingress.Enqueue(first);
            ingress.Enqueue(second);
            var detached = new GridEventInfo[2];

            ingress.DetachInto(
                    detached,
                    Span<NavigationGridChangeScope>.Empty,
                    out int blockedCount,
                    out bool blockAll)
                .Should().Be(2,
                    "world, grid-slot, and grid-generation identity remain exact after hash collision");
            detached[0].ChangeSequence.Should().Be(first.ChangeSequence);
            detached[1].ChangeSequence.Should().Be(second.ChangeSequence);
            blockedCount.Should().Be(0);
            blockAll.Should().BeFalse();
        }
    }

    [Fact]
    public void Ingress_ShouldRequeueStructuralCarryoverPrefixAheadOfNewerCommittedEvents()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 8);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(new GridEventInfo(
            1,
            0,
            1,
            configuration,
            1,
            GridEventKind.GridChanged,
            changeStamp: new GridChangeStamp(2, 2)));
        var detached = new GridEventInfo[2];
        var blocked = new NavigationGridChangeScope[2];
        ingress.DetachInto(detached, blocked, out _, out _).Should().Be(2);

        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 3, obstacleCount: 2));
        ingress.Enqueue(new GridEventInfo(
            1,
            0,
            1,
            configuration,
            1,
            GridEventKind.GridRemoved,
            changeStamp: new GridChangeStamp(4, 4)));
        ingress.RequeuePrefix(detached);

        var ordered = new GridEventInfo[8];
        ingress.DetachInto(ordered, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(3);
        ordered[0].ChangeSequence.Should().Be(2,
            "the detached lifecycle prefix must remain ahead of later committed changes");
        ordered[1].ChangeSequence.Should().Be(3);
        ordered[1].ObstacleCount.Should().Be(2,
            "the newer same-address final state must not be overwritten by detached state");
        ordered[2].ChangeSequence.Should().Be(4);
        blockedCount.Should().Be(0);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void IngressRequeue_WhenLaterIngressConsumesCapacity_ShouldFailClosedWithoutPartialPrefix()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2);
        GridConfiguration configuration = CreateConfiguration();
        GridEventInfo[] detached =
        {
            new(
                1,
                0,
                1,
                configuration,
                1,
                GridEventKind.GridRemoved,
                changeStamp: new GridChangeStamp(1, 1)),
            CreateVoxelEvent(
                configuration,
                sequence: 2,
                obstacleCount: 1,
                index: new VoxelIndex(1, 0, 0))
        };
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 3,
            obstacleCount: 1,
            index: new VoxelIndex(2, 0, 0)));

        ingress.RequeuePrefix(detached);

        var events = new GridEventInfo[2];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(
                events,
                blocked,
                out int blockedCount,
                out bool blockAll,
                out bool topologyLifecycleCoverageLost)
            .Should().Be(0,
                "an unpublished prefix must never be partially restored ahead of later changes");
        topologyLifecycleCoverageLost.Should().BeTrue();
        blockAll.Should().BeFalse();
        blockedCount.Should().Be(1);
        blocked[0].ConfigurationKey.Should().Be(configuration.ToGridKey());
    }

    [Fact]
    public void IngressRequeue_WhenDetachedSequenceIsNotStrictlyIncreasing_ShouldFailClosed()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 4);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 3, obstacleCount: 1));
        GridEventInfo[] detached =
        {
            CreateVoxelEvent(
                configuration,
                sequence: 2,
                obstacleCount: 1,
                index: new VoxelIndex(1, 0, 0)),
            new(
                1,
                0,
                1,
                configuration,
                1,
                GridEventKind.GridRemoved,
                changeStamp: new GridChangeStamp(1, 1))
        };

        ingress.RequeuePrefix(detached);

        var events = new GridEventInfo[4];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(
                events,
                blocked,
                out int blockedCount,
                out bool blockAll,
                out bool topologyLifecycleCoverageLost)
            .Should().Be(0,
                "committed sequence order cannot be repaired by silently sorting a stale prefix");
        topologyLifecycleCoverageLost.Should().BeTrue();
        blockAll.Should().BeFalse();
        blockedCount.Should().Be(1);
    }

    [Fact]
    public void Ingress_ShouldFailClosedOnlyOverflowedConfigurationScopes()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2, maximumScopes: 2);
        GridConfiguration first = CreateConfiguration();
        GridConfiguration second = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        ingress.Enqueue(CreateVoxelEvent(first, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(CreateVoxelEvent(second, sequence: 2, obstacleCount: 1, gridIndex: 1));
        ingress.Enqueue(CreateVoxelEvent(first, sequence: 3, obstacleCount: 2, index: new VoxelIndex(1, 0, 0)));

        var detached = new GridEventInfo[2];
        var blocked = new NavigationGridChangeScope[2];
        ingress.DetachInto(detached, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(0);

        blockAll.Should().BeFalse();
        blockedCount.Should().Be(2);
        blocked.Should().Contain(scope => scope.ConfigurationKey == first.ToGridKey());
        blocked.Should().Contain(scope => scope.ConfigurationKey == second.ToGridKey());
    }

    [Fact]
    public void IngressOverflow_ShouldReportLostTopologyLifecycleCoverage()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 1);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(new GridEventInfo(
            1,
            0,
            1,
            configuration,
            1,
            GridEventKind.GridRemoved));
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 2, obstacleCount: 1));

        var detached = new GridEventInfo[1];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(
                detached,
                blocked,
                out _,
                out _,
                out bool topologyLifecycleCoverageLost)
            .Should().Be(0);

        topologyLifecycleCoverageLost.Should().BeTrue(
            "overflow discarded an exact grid-generation lifecycle event");
    }

    [Fact]
    public void IngressOverflow_ShouldContinueTrackingLaterTopologyLifecycleEvents()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 1);
        GridConfiguration configuration = CreateConfiguration();
        ingress.Enqueue(CreateVoxelEvent(configuration, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(CreateVoxelEvent(
            configuration,
            sequence: 2,
            obstacleCount: 1,
            index: new VoxelIndex(1, 0, 0)));
        ingress.Enqueue(new GridEventInfo(
            1,
            0,
            1,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.GridRemoved,
            changeStamp: new GridChangeStamp(3, 3)));

        var detached = new GridEventInfo[1];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(
                detached,
                blocked,
                out int blockedCount,
                out bool blockAll,
                out bool topologyLifecycleCoverageLost)
            .Should().Be(0);

        topologyLifecycleCoverageLost.Should().BeTrue(
            "topology loss that arrives after overflow is still part of the fail-closed scope");
        blockedCount.Should().Be(1);
        blockAll.Should().BeFalse();
    }

    [Fact]
    public void IngressScopeSaturation_ShouldBlockAllWhileAnyUntrackedScopeRemains()
    {
        var ingress = new NavigationGridChangeIngress(capacity: 2, maximumScopes: 1);
        GridConfiguration first = CreateConfiguration();
        var second = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        ingress.Enqueue(CreateVoxelEvent(first, sequence: 1, obstacleCount: 1));
        ingress.Enqueue(CreateVoxelEvent(
            second,
            sequence: 2,
            obstacleCount: 1,
            gridIndex: 1));

        var detached = new GridEventInfo[1];
        var blocked = new NavigationGridChangeScope[1];
        ingress.DetachInto(detached, blocked, out int blockedCount, out bool blockAll)
            .Should().Be(1);

        detached[0].ChangeSequence.Should().Be(1);
        blockedCount.Should().Be(0);
        blockAll.Should().BeTrue(
            "once distinct scope capacity is exhausted, the remaining committed work is global");
    }

    [Fact]
    public void Ingress_ShouldRetainConfiguredCapacityWithoutCallbackAllocationOrResize()
    {
        const int capacity = 32;
        long byteCeiling = NavigationGridChangeIngress.GetRetainedBytes(capacity);
        int resolvedCapacity = NavigationGridChangeIngress.GetMaximumCapacity(
            capacity,
            byteCeiling);
        var ingress = new NavigationGridChangeIngress(resolvedCapacity);
        GridConfiguration configuration = CreateConfiguration();
        var events = new GridEventInfo[capacity];
        for (int i = 0; i < events.Length; i++)
        {
            events[i] = CreateVoxelEvent(
                configuration,
                sequence: (ulong)i + 1,
                obstacleCount: 1,
                index: new VoxelIndex(i, 0, 0));
        }

        ingress.GetRetainedBytes().Should().BeLessThanOrEqualTo(byteCeiling);
        ingress.IndexCapacity.Should().BeGreaterThan(capacity);
        int indexCapacity = ingress.IndexCapacity;

        for (int i = 0; i < events.Length; i++)
            ingress.Enqueue(events[i]);
        ingress.Reset();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < events.Length; i++)
            ingress.Enqueue(events[i]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        ingress.IndexCapacity.Should().Be(indexCapacity);
        NavigationGridChangeIngress.GetMaximumCapacity(capacity, byteCeiling - 1)
            .Should().BeLessThan(capacity);
    }

    [Fact]
    public void WarmIdleMaintenanceAndSnapshotLeaseReuse_ShouldAllocateZero()
    {
        TrailblazerWorldContextSettings settings = CreateSettings();
        using var world = new GridWorld();
        using var runtime = new NavigationGraphRuntime(world, settings);
        runtime.Maintain(1);
        runtime.TryAcquire()!.Dispose();
        for (int frame = 2; frame < 34; frame++)
        {
            runtime.Maintain(frame);
            runtime.TryAcquire()!.Dispose();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allCheckedOut = true;
        for (int frame = 34; frame < 134; frame++)
        {
            runtime.Maintain(frame);
            using NavigationWorldGraphLease? graphLease = runtime.TryAcquire();
            allCheckedOut &= graphLease != null;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allCheckedOut.Should().BeTrue();
        allocated.Should().Be(0);
    }

    [Fact]
    public void SnapshotPressure_ShouldStopNewLeasesWhileSafetyUpdatesContinue()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxRetiredSnapshots: 1,
            maxRetiredSnapshotBytes: 1_000_000);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        context.Pathing.Admit(new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1)).Should().BeTrue();
        context.Simulate();

        NavigationWorldGraphLease first = context.Pathing.TryAcquireNavigationGraph()!;
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        var firstObstacle = context.World.AllocateObstacleToken();
        grid.TryAddObstacle(voxel!, firstObstacle).Should().BeTrue();
        context.Simulate();
        context.Pathing.TryAcquireNavigationGraph().Should().BeNull();

        var secondObstacle = context.World.AllocateObstacleToken();
        grid.TryAddObstacle(voxel!, secondObstacle).Should().BeTrue();
        long pressuredVersion = first.Graph.GraphVersion;
        context.Simulate();
        context.Pathing.TryGetNavigationGraphCellState("map", default, out NavigationGraphCellState pressured)
            .Should().BeTrue();
        pressured.ObstacleCount.Should().Be(2);

        first.Dispose();
        context.Simulate();
        context.Pathing.TryGetNavigationGraphCellState("map", default, out NavigationGraphCellState caughtUp)
            .Should().BeTrue();
        caughtUp.ObstacleCount.Should().Be(2);
        using NavigationWorldGraphLease available = context.Pathing.TryAcquireNavigationGraph()!;
        available.Should().NotBeNull();
        available.Graph.GraphVersion.Should().BeGreaterThan(pressuredVersion);
    }

    [Fact]
    public void WriterBlockingLease_ShouldKeepAdmissionClosedUntilExactPhysicalResnapshotPublishes()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 0);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .AddCell(default, cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(install).Should().BeTrue();
        for (int frame = 0;
             frame < 64 && install.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            context.Simulate();
        }
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        using (NavigationWorldGraphLease pressure = context.Pathing.TryAcquireNavigationGraph()!)
        {
            VoxelGrid grid = context.World.ActiveGrids[gridIndex];
            grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

            context.Simulate();

            store.IsSafetyPending.Should().BeTrue(
                "a leased writer-blocking root must retain the exact resnapshot obligation");
            context.Pathing.TryAcquireNavigationGraph().Should().BeNull(
                "no reader may observe the pre-resnapshot physical generation");
        }

        for (int frame = 0; frame < 64 && store.IsSafetyPending; frame++)
            context.Simulate();

        store.IsSafetyPending.Should().BeFalse(
            "the exact unblocked resnapshot is the only publication that reopens admission");
        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                default,
                out NavigationGraphCellState refreshed)
            .Should().BeTrue();
        refreshed.ObstacleCount.Should().Be(1);
        using NavigationWorldGraphLease reopened = context.Pathing.TryAcquireNavigationGraph()!;
        reopened.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriterBlockingLease_ShouldRetainTopologyRemovalUntilFullRebuildPublishes(
        bool overflowTopologyEvent)
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxIngressEntries: overflowTopologyEvent
                ? 1
                : TrailblazerWorldContextSettings.Default.MaxIngressEntries,
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 0);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .AddCell(default, new NavigationCell(
                        TraversalMedia.Solid,
                        TraversalCapability.None,
                        default,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        Fixed64.One))
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(install).Should().BeTrue();
        SimulateUntil(context, () => install.Receipt.Status != NavigationOperationStatus.Pending);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        using (NavigationWorldGraphLease pressure = context.Pathing.TryAcquireNavigationGraph()!)
        {
            if (overflowTopologyEvent)
            {
                VoxelGrid grid = context.World.ActiveGrids[gridIndex];
                grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
                grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
            }
            context.World.TryRemoveGrid(gridIndex).Should().BeTrue();

            context.Simulate();

            store.IsSafetyPending.Should().BeTrue(
                "a topology lifecycle event drained under writer pressure must stay fail-closed");
            context.Pathing.TryAcquireNavigationGraph().Should().BeNull();
        }

        SimulateUntil(
            context,
            () => !store.IsSafetyPending && !store.Current.HasClosedStructuralScope);

        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                default,
                out NavigationGraphCellState removed)
            .Should().BeTrue();
        removed.IsMaterialized.Should().BeFalse(
            "the retained topology removal must drive an exact full rebuild after pressure clears");
        using NavigationWorldGraphLease reopened = context.Pathing.TryAcquireNavigationGraph()!;
        reopened.Graph.HasClosedStructuralScope.Should().BeFalse();
    }

    [Fact]
    public void DeferredReset_ShouldCloseSnapshotAdmissionUntilEmptyRootPublishes()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxRetiredSnapshots: 1,
            maxRetiredSnapshotBytes: 1_000_000);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One))
            .Build();
        context.Pathing.Admit(new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1)).Should().BeTrue();
        context.Simulate();
        NavigationWorldGraphLease retiredLease = context.Pathing.TryAcquireNavigationGraph()!;
        context.Pathing.Admit(new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    new[] { NavigationCellOverlayOperation.Suppress(default) })
            })),
            2,
            context.FrameCount + 1)).Should().BeTrue();
        context.Simulate();
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        NavigationWorldGraph current = store.Current;
        current.Checkout();
        try
        {
            context.Pathing.Reset();

            store.IsSafetyPending.Should().BeTrue();
            context.Pathing.TryAcquireNavigationGraph().Should().BeNull();
            context.Pathing.TryGetNavigationGraphCellState("map", default, out _)
                .Should().BeTrue("the old root remains physically retained but is not query-admissible");

            context.Simulate();

            store.IsSafetyPending.Should().BeTrue(
                "a reset must remain fail-closed while the empty root cannot publish");
            context.Pathing.TryAcquireNavigationGraph().Should().BeNull();
            context.Pathing.TryGetNavigationGraphCellState("map", default, out _)
                .Should().BeTrue("blocked reset maintenance cannot discard the retained root");
        }
        finally
        {
            current.Return();
            retiredLease.Dispose();
        }

        context.Simulate();
        store.IsSafetyPending.Should().BeFalse();
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
        using NavigationWorldGraphLease afterReset = context.Pathing.TryAcquireNavigationGraph()!;
        afterReset.Should().NotBeNull();
    }

    [Fact]
    public void ActiveSnapshotByteCeiling_ShouldIncludePreparedBakeMemory()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            maxActiveSnapshotBytes: TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridConfiguration configuration = CreateConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);

        context.Pathing.Admit(operation).Should().BeTrue();
        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
    }

    [Fact]
    public void DefaultCapacityEnvelope_ShouldAdmitConfiguredGlobalOverlayMaxima()
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        long maximumObservedActiveSnapshotBytes =
            context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes;
        int maximumObservedPersistentGraphPages =
            context.Pathing.GetNavigationGraphDiagnostics().PersistentGraphPageCount;
        long sequence = 0;
        for (int mapIndex = 0; mapIndex < 4; mapIndex++)
        {
            var configuration = new GridConfiguration(
                new Vector3d(mapIndex * 10_000, 0, 0),
                new Vector3d((mapIndex * 10_000) + 4_096, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            string mapId = $"max-{mapIndex}";
            NavigationMap map = new NavigationMapBuilder(mapId, binding)
                .AddCell(default, new NavigationCell(
                    TraversalMedia.Solid,
                    TraversalCapability.None,
                    default,
                    Fixed64.Zero,
                    Fixed64.Zero,
                    Fixed64.One))
                .Build();
            var install = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, 1),
                OverlayReplacementPolicy.Clear,
                ++sequence,
                context.FrameCount + 1);
            context.Pathing.Admit(install).Should().BeTrue();
            for (int frame = 0; frame < 64 && install.Receipt.Status == NavigationOperationStatus.Pending; frame++)
            {
                context.Simulate();
                maximumObservedActiveSnapshotBytes = Math.Max(
                    maximumObservedActiveSnapshotBytes,
                    context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes);
                maximumObservedPersistentGraphPages = Math.Max(
                    maximumObservedPersistentGraphPages,
                    context.Pathing.GetNavigationGraphDiagnostics().PersistentGraphPageCount);
            }
            install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

            var cells = new NavigationCellOverlayOperation[settings.OperationLimits.MaxOverlayCellsPerMap];
            var connections = new NavigationConnectionOverlayOperation[
                settings.OperationLimits.MaxOverlayConnectionsPerMap];
            var transitions = new TraversalTransitionOverlayOperation[
                settings.OperationLimits.MaxOverlayTransitionsPerMap];
            NavigationCell cell = new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One);
            for (int i = 0; i < cells.Length; i++)
                cells[i] = NavigationCellOverlayOperation.Set(new VoxelIndex(i + 1, 0, 0), cell);
            for (int i = 0; i < connections.Length; i++)
            {
                VoxelIndex sourceIndex = new(i + 1, 0, 0);
                VoxelIndex destinationIndex = new(i + 2, 0, 0);
                binding.TryGetCellPrism(sourceIndex, out GridCellPrism sourcePrism).Should().BeTrue();
                binding.TryGetCellPrism(destinationIndex, out GridCellPrism destinationPrism).Should().BeTrue();
                Vector3d entryAnchor = new(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z);
                Vector3d exitAnchor = new(destinationPrism.Center.X, destinationPrism.VerticalMin, destinationPrism.Center.Z);
                connections[i] = NavigationConnectionOverlayOperation.Upsert(
                    new NavigationConnection(
                        $"connection-{i:D4}",
                        sourceIndex,
                        new NavigationCellAddress(mapId, destinationIndex),
                        entryAnchor,
                        exitAnchor,
                        Fixed64.Zero,
                        Fixed64.Half));
                transitions[i] = TraversalTransitionOverlayOperation.Upsert(
                    new TraversalTransitionDefinition(
                        $"transition-{i:D4}",
                        TraversalTransitionType.Climb,
                        sourceIndex,
                        TraversalMedium.Solid,
                        new NavigationCellAddress(mapId, destinationIndex),
                        TraversalMedium.Solid,
                        TraversalCapability.Climb));
            }
            NavigationMapOverlayDelta[] deltas =
            {
                new(mapId, cells),
                new(mapId, connections: connections),
                new(mapId, transitions: transitions)
            };
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                var operation = new NavigationOverlayCommitOperation(
                    new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                        new[] { deltas[delta] })),
                    ++sequence,
                    context.FrameCount + 1);
                context.Pathing.Admit(operation).Should().BeTrue();
                for (int frame = 0;
                    frame < 128 && operation.Receipt.Status == NavigationOperationStatus.Pending;
                    frame++)
                {
                    context.Simulate();
                    maximumObservedActiveSnapshotBytes = Math.Max(
                        maximumObservedActiveSnapshotBytes,
                        context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes);
                    maximumObservedPersistentGraphPages = Math.Max(
                        maximumObservedPersistentGraphPages,
                        context.Pathing.GetNavigationGraphDiagnostics().PersistentGraphPageCount);
                }
                operation.Receipt.Status.Should().Be(
                    NavigationOperationStatus.Applied,
                    "map {0} delta {1} should be legal; rejection was {2}; active={3}, pages={4}, peak={5}",
                    mapIndex,
                    delta,
                    operation.Receipt.Rejection,
                    context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes,
                    context.Pathing.GetNavigationGraphDiagnostics().PersistentGraphPageCount,
                    maximumObservedActiveSnapshotBytes);
            }
        }

        NavigationGraphDiagnosticsSnapshot diagnostics = context.Pathing.GetNavigationGraphDiagnostics();
        maximumObservedActiveSnapshotBytes.Should().Be(50_855_688,
            "the active envelope includes the published root plus retained operation, "
            + "composition, the exact unpublished materialized candidate, and affected-component "
            + "work at the largest overlay boundary "
            + "with one retained 104-byte portal certificate, immutable transition pages, "
            + "and no duplicate waypoint sequence per explicit corridor leg; exact batch bounds "
            + "also omit the former three 64-byte phantom-map tree nodes");
        maximumObservedPersistentGraphPages.Should().Be(527_612,
            "the conservative page envelope counts shared persistent ownership at every "
            + "unpublished work boundary, including exact materialized candidate ownership "
            + "and immutable transition-page roots, without the former phantom changed-map, "
            + "whole-map, and transition-refresh source nodes beyond the batch count");
        diagnostics.ActiveSnapshotBytes.Should().BeLessThanOrEqualTo(settings.MaxActiveSnapshotBytes);
        diagnostics.PersistentGraphPageCount.Should().BeLessThanOrEqualTo(settings.MaxPersistentGraphPages);
        diagnostics.ActiveSnapshotBytes.Should().Be(17_996_832,
            "endpoint incidence adds a 32-byte index field block, a 288-byte outer root, "
            + "262,528 bytes for four 1,025-address inner maps, and 885,600 bytes for "
            + "4,100 one-page owner rows; the automatic seam index adds its 224-byte "
            + "empty immutable root; four published map instances each retain one new "
            + "8-byte grid last-change sequence (32 bytes total); explicit edges retain one "
            + "compiled 104-byte portal-certificate sequence per adjacent leg without retaining "
            + "a second waypoint sequence; the exact surface-component "
            + "membership, record, and member-sequence ownership remains after deleting the "
            + "2,080-byte duplicate composition carrier, with 112 bytes for the medium-partition "
            + "index header and shared empty non-Solid roots; immutable transition pages "
            + "retain the source-full and destination-reference records");
        diagnostics.PersistentGraphPageCount.Should().Be(148_365,
            "the endpoint index adds one root page, 4 outer/4,100 inner nodes, and "
            + "12,300 fixed-row pages for the 4,100 distinct endpoint addresses; the "
            + "empty automatic seam index owns four roots; exact surface components add "
            + "exact component ownership plus two shared empty non-Solid partition roots, and "
            + "explicit records replace the former waypoint page "
            + "with one portal-certificate page per adjacent leg without the deleted 30-page "
            + "legacy carrier; transition pages add exact outer, map, page, and record-array "
            + "ownership");
        for (int mapIndex = 0; mapIndex < 4; mapIndex++)
        {
            context.Pathing.TryGetNavigationGraphCellState(
                $"max-{mapIndex}",
                new VoxelIndex(settings.OperationLimits.MaxOverlayCellsPerMap, 0, 0),
                out NavigationGraphCellState state).Should().BeTrue();
            state.HasCell.Should().BeTrue();
        }
    }

    [Fact]
    public void ActiveSnapshotByteCeiling_ShouldIncludeRetainedOverlayPayloads()
    {
        GridConfiguration configuration = CreateConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, 1);
        NavigationConnection smallConnection = new(
            "connection",
            default,
            new NavigationCellAddress("map", default),
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMapOverlayState smallOverlay = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "map",
                connections: new[] { NavigationConnectionOverlayOperation.Upsert(smallConnection) }),
            1);
        var witnesses = new NavigationCellAddress[256];
        for (int i = 0; i < witnesses.Length; i++)
            witnesses[i] = new NavigationCellAddress($"witness-{i:D3}", new VoxelIndex(i, 0, 0));
        NavigationConnection largeConnection = new(
            "connection",
            default,
            new NavigationCellAddress("destination-with-a-much-longer-map-id", default),
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            witnesses);
        NavigationMapOverlayState largeOverlay = smallOverlay.Apply(
            new NavigationMapOverlayDelta(
                "map",
                connections: new[] { NavigationConnectionOverlayOperation.Upsert(largeConnection) }),
            2);

        NavigationMapInstance smallInstance = NavigationMapInstanceTestFactory.ComposeDetached(
            new NavigationOperationCandidate.MapState(
                map,
                1,
                prepared.RetainedBytes,
                smallOverlay,
                0),
            previous: null,
            instanceVersion: 1);
        NavigationMapInstance largeInstance = NavigationMapInstanceTestFactory.ComposeDetached(
            new NavigationOperationCandidate.MapState(
                map,
                1,
                prepared.RetainedBytes,
                largeOverlay,
                0),
            smallInstance,
            instanceVersion: 2);
        var smallGraph = new NavigationWorldGraph(1, new[] { smallInstance });
        var largeGraph = new NavigationWorldGraph(2, new[] { largeInstance });
        largeGraph.RetainedBytes.Should().BeGreaterThan(smallGraph.RetainedBytes);
        long byteCeiling = smallGraph.RetainedBytes
            + ((largeGraph.RetainedBytes - smallGraph.RetainedBytes) / 2);
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 3,
            maxRetiredSnapshots: 2,
            maxRetiredBytes: 1_000_000,
            maxActiveBytes: byteCeiling,
            maxPersistentPages: 1_000,
            maxConcurrentLeases: 1);

        store.TryPublish(smallGraph).Should().Be(NavigationCandidatePublication.Published);
        store.TryPublish(largeGraph).Should().Be(NavigationCandidatePublication.PermanentCapacity);
    }

    [Fact]
    public void SemanticComposeWork_ShouldNotRecountPriorPhysicalRootAndPayload()
    {
        GridConfiguration configuration = CreateConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, 1);
        var sourceState = new NavigationOperationCandidate.MapState(
            map,
            1,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            0);
        NavigationMapInstance dormant = NavigationMapInstanceTestFactory.ComposeDetached(
            sourceState,
            previous: null,
            instanceVersion: 1);
        var physicalPage = new NavigationPhysicalPage(0, 2);
        physicalPage.IsPresent[0] = true;
        physicalPage.ObstacleCounts[0] = 1;
        PersistentIntMap<NavigationPhysicalPage> physicalPages =
            PersistentIntMap<NavigationPhysicalPage>.Empty.Set(0, physicalPage);
        NavigationMapInstance materialized = dormant.Materialize(
            new NavigationGridBaselineCapture(
                addressCount: 1,
                physicalPages,
                capturedChangeSequence: 1,
                worldSpawnToken: 1,
                gridIndex: 0,
                gridSpawnToken: 1,
                gridLastChangeSequence: 1,
                configurationKey: map.GridBinding.Key),
            instanceVersion: 2);
        NavigationCell changedCell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        var delta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(default, changedCell) });
        NavigationMapOverlayState overlay = NavigationMapOverlayState.Empty.Apply(delta, 1);
        var nextState = new NavigationOperationCandidate.MapState(
            map,
            1,
            prepared.RetainedBytes,
            overlay,
            0);
        var dormantWork = new NavigationMapInstance.ComposeWork(nextState, dormant, delta, 3);
        var materializedWork = new NavigationMapInstance.ComposeWork(nextState, materialized, delta, 3);

        materializedWork.RetainedBytes.Should().Be(dormantWork.RetainedBytes);
        materializedWork.PersistentPageCount.Should().Be(dormantWork.PersistentPageCount);

        var meter = new MaintenanceWorkMeter(
            new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1));
        materializedWork.Advance(meter).Should().BeTrue();
        materializedWork.Result.TryGetPhysicalState(
            0,
            out bool isPresent,
            out byte obstacleCount).Should().BeTrue();
        isPresent.Should().BeTrue();
        obstacleCount.Should().Be(1);
    }

    [Fact]
    public void MaintenanceCarryover_ShouldFailClosedUntilExactBaselineCatchup()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: defaults.MaintenanceBudget.MaxComponentNodes,
            maxSeamCandidateProbes: defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            maxExplicitEdges: defaults.MaintenanceBudget.MaxExplicitEdges,
            maxDependencyEntries: defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maintenanceBudget: budget));
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .AddCell(new VoxelIndex(1, 0, 0), cell)
            .Build();
        GridConfiguration unrelatedConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(unrelatedConfiguration, out _).Should().BeTrue();
        unrelatedConfiguration.TryNormalize(out NormalizedGridConfiguration unrelatedBinding)
            .Should().BeTrue();
        NavigationMap unrelated = new NavigationMapBuilder("unrelated", unrelatedBinding)
            .AddCell(default, cell)
            .Build();
        var firstInstall = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        var unrelatedInstall = new NavigationMapCommitOperation(
            new PreparedNavigationMap(unrelated, 1),
            OverlayReplacementPolicy.Clear,
            2,
            1);
        context.Pathing.Admit(firstInstall).Should().BeTrue();
        context.Pathing.Admit(unrelatedInstall).Should().BeTrue();
        SimulateUntil(
            context,
            () => firstInstall.Receipt.Status != NavigationOperationStatus.Pending
                && unrelatedInstall.Receipt.Status != NavigationOperationStatus.Pending
                && IsMaterialized(context, "map")
                && IsMaterialized(context, "unrelated"));
        firstInstall.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        unrelatedInstall.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryGetNavigationGraphCellState(
                "unrelated",
                default,
                out NavigationGraphCellState initiallyOpen)
            .Should().BeTrue();
        initiallyOpen.IsMaterialized.Should().BeTrue();
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? first).Should().BeTrue();
        grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? second).Should().BeTrue();
        grid.TryAddObstacle(first!, context.World.AllocateObstacleToken()).Should().BeTrue();
        grid.TryAddObstacle(second!, context.World.AllocateObstacleToken()).Should().BeTrue();

        context.Simulate();

        context.Pathing.TryGetNavigationGraphCellState("map", default, out NavigationGraphCellState closed)
            .Should().BeTrue();
        closed.IsMaterialized.Should().BeFalse();
        context.Pathing.TryGetNavigationGraphCellState(
                "unrelated",
                default,
                out NavigationGraphCellState unrelatedOpen)
            .Should().BeTrue();
        unrelatedOpen.IsMaterialized.Should().BeTrue();

        context.Simulate();

        context.Pathing.TryGetNavigationGraphCellState("map", default, out NavigationGraphCellState caughtUp)
            .Should().BeTrue();
        caughtUp.IsMaterialized.Should().BeTrue();
        caughtUp.ObstacleCount.Should().Be(1);
        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                new VoxelIndex(1, 0, 0),
                out NavigationGraphCellState secondCaughtUp)
            .Should().BeTrue();
        secondCaughtUp.ObstacleCount.Should().Be(1);
    }

    [Fact]
    public void BroadOverflow_ShouldReopenAgainstFinalReplacementGeneration()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maxIngressEntries: 2));
        GridConfiguration configuration = CreateConfiguration();
        context.World.TryAddGrid(configuration, out ushort firstGridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(install).Should().BeTrue();
        SimulateUntil(
            context,
            () => install.Receipt.Status != NavigationOperationStatus.Pending
                && IsMaterialized(context, "map"));
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        long firstGeneration = GetCellState(context, "map").GridSpawnToken;

        context.World.TryRemoveGrid(firstGridIndex).Should().BeTrue();
        context.World.Reset();
        context.World.TryAddGrid(configuration, out ushort replacementGridIndex).Should().BeTrue();
        replacementGridIndex.Should().Be(firstGridIndex);

        context.Simulate();
        GetCellState(context, "map").IsMaterialized.Should().BeFalse();

        SimulateUntil(context, () => IsMaterialized(context, "map"));
        NavigationGraphCellState replacement = GetCellState(context, "map");
        replacement.IsMaterialized.Should().BeTrue();
        replacement.GridSpawnToken.Should().NotBe(firstGeneration);
    }

    [Fact]
    public void BaselineAddressBudget_ShouldCarryOverScopesWithoutReopeningEarly()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            maxConsumedEnvelopes: defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            maxBaselineAddresses: 3,
            maxOverlaySlots: defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: defaults.MaintenanceBudget.MaxComponentNodes,
            maxSeamCandidateProbes: defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            maxExplicitEdges: defaults.MaintenanceBudget.MaxExplicitEdges,
            maxDependencyEntries: defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
            maintenanceBudget: budget,
            maxIngressEntries: 1,
            operationLimits: CreateOperationLimits(
                maxOverlayCellsPerMap: 1,
                maxOverlayCells: 1,
                maxPreparedMapBytes: 3_000_000),
            maxDynamicCellSlotsPerMap: 1,
            maxDynamicCellSlots: 1));
        GridConfiguration firstConfiguration = CreateConfiguration();
        GridConfiguration secondConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(firstConfiguration, out ushort firstGridIndex).Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, out ushort secondGridIndex).Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding).Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var firstInstall = new NavigationMapCommitOperation(
            new PreparedNavigationMap(new NavigationMapBuilder("first", firstBinding)
                .AddCell(default, cell)
                .AddCell(new VoxelIndex(1, 0, 0), cell)
                .Build(), 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        var secondInstall = new NavigationMapCommitOperation(
            new PreparedNavigationMap(new NavigationMapBuilder("second", secondBinding)
                .AddCell(default, cell)
                .AddCell(new VoxelIndex(1, 0, 0), cell)
                .Build(), 1),
            OverlayReplacementPolicy.Clear,
            2,
            1);
        context.Pathing.Admit(firstInstall).Should().BeTrue();
        context.Pathing.Admit(secondInstall).Should().BeTrue();
        SimulateUntil(
            context,
            () => firstInstall.Receipt.Status != NavigationOperationStatus.Pending
                && secondInstall.Receipt.Status != NavigationOperationStatus.Pending
                && IsMaterialized(context, "first")
                && IsMaterialized(context, "second"));
        firstInstall.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        secondInstall.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        VoxelGrid firstGrid = context.World.ActiveGrids[firstGridIndex];
        VoxelGrid secondGrid = context.World.ActiveGrids[secondGridIndex];
        firstGrid.TryGetVoxel(default(VoxelIndex), out Voxel? firstVoxel).Should().BeTrue();
        secondGrid.TryGetVoxel(default(VoxelIndex), out Voxel? secondVoxel).Should().BeTrue();
        firstGrid.TryAddObstacle(firstVoxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        secondGrid.TryAddObstacle(secondVoxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

        context.Simulate();
        GetCellState(context, "first").IsMaterialized.Should().BeFalse();
        GetCellState(context, "second").IsMaterialized.Should().BeFalse();

        context.Simulate();
        GetCellState(context, "first").IsMaterialized.Should().BeTrue();
        GetCellState(context, "second").IsMaterialized.Should().BeFalse();

        context.Simulate();
        GetCellState(context, "second").IsMaterialized.Should().BeTrue();
    }

    [Fact]
    public void OversizedSingleMapBaseline_ShouldChunkAndRestartAfterInterleavedMutation()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            maxBaselineAddresses: 2,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maintenanceBudget: budget,
                operationLimits: CreateOperationLimits(
                    maxOverlayCellsPerMap: 1,
                    maxOverlayCells: 1,
                    maxPreparedMapBytes: 2_000_000),
                maxDynamicCellSlotsPerMap: 1,
                maxDynamicCellSlots: 1));
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 5; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(operation).Should().BeTrue();

        context.Simulate();
        GetCellState(context, "map").IsMaterialized.Should().BeFalse();
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? first).Should().BeTrue();
        grid.TryAddObstacle(first!, context.World.AllocateObstacleToken()).Should().BeTrue();

        context.Simulate();
        GetCellState(context, "map").IsMaterialized.Should().BeFalse(
            "the old first chunk must be discarded after the captured change sequence advances");
        context.Simulate();
        GetCellState(context, "map").IsMaterialized.Should().BeFalse();
        context.Simulate();
        GetCellState(context, "map").IsMaterialized.Should().BeFalse();
        context.Simulate();

        NavigationGraphCellState reopened = GetCellState(context, "map");
        reopened.IsMaterialized.Should().BeTrue();
        reopened.ObstacleCount.Should().Be(1);
        context.Pathing.RetainedBaselineCaptureCount.Should().Be(0);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void OversizedSingleMapBaseline_ShouldConvergeDuringContinuousUnrelatedGridChurn()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            maxBaselineAddresses: 2,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maintenanceBudget: budget,
                operationLimits: CreateOperationLimits(
                    maxOverlayCellsPerMap: 1,
                    maxOverlayCells: 1,
                    maxPreparedMapBytes: 2_000_000),
                maxDynamicCellSlotsPerMap: 1,
                maxDynamicCellSlots: 1));
        GridConfiguration mappedConfiguration = new(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        GridConfiguration unrelatedConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(mappedConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(unrelatedConfiguration, out ushort unrelatedGridIndex).Should().BeTrue();
        mappedConfiguration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 5; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
        context.Pathing.Admit(new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            1,
            1)).Should().BeTrue();
        VoxelGrid unrelatedGrid = context.World.ActiveGrids[unrelatedGridIndex];
        unrelatedGrid.TryGetVoxel(default(VoxelIndex), out Voxel? unrelatedVoxel).Should().BeTrue();

        for (int frame = 0; frame < 3; frame++)
        {
            context.Simulate();
            if (frame < 2)
            {
                unrelatedGrid.TryAddObstacle(
                    unrelatedVoxel!,
                    context.World.AllocateObstacleToken()).Should().BeTrue();
            }
        }

        GetCellState(context, "map").IsMaterialized.Should().BeTrue(
            "only target-grid mutations may restart a chunked baseline");
        context.Pathing.RetainedBaselineCaptureCount.Should().Be(0);
    }

    [Fact]
    public void ChunkedBaseline_ShouldRespectExactPrefixPageBoundaryAndComplete()
    {
        const int exactPageCeiling = 458;
        const int oneBelowPageCeiling = 457;

        using (TrailblazerWorldContext insufficient = CreateChunkedBaselineContext(
            oneBelowPageCeiling,
            includeExplicitConnection: true))
        {
            insufficient.Simulate();
            NavigationGraphDiagnosticsSnapshot belowMinimum =
                insufficient.Pathing.GetNavigationGraphDiagnostics();
            belowMinimum.BaselineRebuildCount.Should().Be(1,
                "the source root, retained operation state, baseline prefix, and exact-component "
                + "preparation own 458 conservative pages after deleting the 16-page legacy "
                + "composition carrier and adding exact ownership for the unpublished "
                + "materialized component candidate; 457 retains the first baseline prefix "
                + "but cannot retain its later exact combined peak");
            belowMinimum.PersistentGraphPageCount.Should().BeLessThan(exactPageCeiling);
            for (int frame = 0; frame < 32; frame++)
                insufficient.Simulate();
            insufficient.Pathing.RetainedOperationWorkCount.Should().Be(0);
            insufficient.Pathing.RetainedCompositionWorkCount.Should().Be(0);
            NavigationGraphDiagnosticsSnapshot retained =
                insufficient.Pathing.GetNavigationGraphDiagnostics();
            retained.BaselineRebuildCount.Should().Be(1);
            retained.PersistentGraphPageCount.Should().BeLessThanOrEqualTo(oneBelowPageCeiling);
            IsMaterialized(insufficient, "map").Should().BeFalse();
        }

        using TrailblazerWorldContext complete = CreateChunkedBaselineContext(
            exactPageCeiling,
            includeExplicitConnection: true);
        complete.Simulate();
        NavigationGraphDiagnosticsSnapshot prefix = complete.Pathing.GetNavigationGraphDiagnostics();
        prefix.BaselineRebuildCount.Should().Be(1);
        prefix.PersistentGraphPageCount.Should().BeLessThanOrEqualTo(exactPageCeiling);
        prefix.ActiveSnapshotBytes.Should().BeLessThanOrEqualTo(4_000_000);
        GetCellState(complete, "map").IsMaterialized.Should().BeFalse();
        SimulateUntil(complete, () => IsMaterialized(complete, "map"));
        NavigationGraphDiagnosticsSnapshot completed = complete.Pathing.GetNavigationGraphDiagnostics();
        completed.BaselineCapacityBlockedCount.Should().Be(0);
        completed.PersistentGraphPageCount.Should().BeLessThanOrEqualTo(exactPageCeiling);
    }

    [Fact]
    public void ExactGridEvent_ShouldCollectOnlyOneOfManyDisconnectedMaps()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maxActiveSnapshotBytes: 16_000_000,
                operationLimits: CreateOperationLimits(maxMaps: 129),
                maxPersistentGraphPages: 16_000));
        VoxelGrid? targetGrid = null;
        long sequence = 0;
        for (int i = 0; i < 129; i++)
        {
            GridConfiguration configuration = new(
                new Vector3d(i * 2, 0, 0),
                new Vector3d(i * 2, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            var map = new NavigationMapBuilder($"map-{i:D3}", binding)
                .AddCell(default, new NavigationCell(
                    TraversalMedia.Solid,
                    TraversalCapability.None,
                    default,
                    Fixed64.Zero,
                    Fixed64.Zero,
                    Fixed64.One))
                .Build();
            var commit = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, 1),
                OverlayReplacementPolicy.Clear,
                ++sequence,
                context.FrameCount + 1);
            context.Pathing.Admit(commit).Should().BeTrue();
            while (commit.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            commit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            if (i == 128)
                targetGrid = context.World.ActiveGrids[gridIndex];
        }
        targetGrid!.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        targetGrid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

        context.Simulate();

        context.Pathing.LastAffectedMapCollectionCount.Should().Be(1,
            "exact configuration indexing must append the touched ordinal without scanning all maps");
        context.Pathing.NavigationMaintenanceMeter.BaselineAddresses.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void RemovingLastChunkedMap_ShouldReleaseRebuildAndCapturedPages()
    {
        using TrailblazerWorldContext context = CreateChunkedBaselineContext(1_000);
        context.Simulate();
        context.Pathing.GetNavigationGraphDiagnostics().BaselineRebuildCount.Should().Be(1);
        var remove = new NavigationMapRemoveOperation(
            "map",
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();

        context.Simulate();

        NavigationGraphDiagnosticsSnapshot diagnostics =
            context.Pathing.GetNavigationGraphDiagnostics();
        diagnostics.Maps.Should().BeEmpty();
        diagnostics.BaselineRebuildCount.Should().Be(0);
        diagnostics.BaselineRebuildBytes.Should().Be(0);
        diagnostics.BaselineRebuildPageCount.Should().Be(0);
        context.Pathing.RetainedBaselineCaptureCount.Should().Be(0);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void NonReusedDynamicSlots_ShouldRespectLifetimeCapacity()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings());
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(40, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        context.Pathing.Admit(new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1)).Should().BeTrue();
        context.Simulate();
        long sequence = 1;
        for (int i = 1; i <= 32; i++)
        {
            VoxelIndex index = new(i, 0, 0);
            context.Pathing.Admit(CreateCellOperation(
                NavigationCellOverlayOperation.Set(index, cell),
                ++sequence,
                context.FrameCount + 1)).Should().BeTrue();
            context.Simulate();
            context.Pathing.Admit(CreateCellOperation(
                NavigationCellOverlayOperation.RevertToBake(index),
                ++sequence,
                context.FrameCount + 1)).Should().BeTrue();
            context.Simulate();
        }
        var overCapacity = CreateCellOperation(
            NavigationCellOverlayOperation.Set(new VoxelIndex(33, 0, 0), cell),
            ++sequence,
            context.FrameCount + 1);

        context.Pathing.Admit(overCapacity).Should().BeTrue();
        context.Simulate();

        overCapacity.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        overCapacity.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                new VoxelIndex(33, 0, 0),
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void CompositionWorkBudget_ShouldCarryOverWhenTotalGraphExceedsPerFrameBudget()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maintenanceBudget: budget));
        GridConfiguration firstConfiguration = CreateConfiguration();
        var secondConfiguration = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var thirdConfiguration = new GridConfiguration(
            new Vector3d(20, 0, 0),
            new Vector3d(22, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding).Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding).Should().BeTrue();
        thirdConfiguration.TryNormalize(out NormalizedGridConfiguration thirdBinding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap first = new NavigationMapBuilder("first", firstBinding)
            .AddCell(default, cell)
            .Build();
        NavigationMap second = new NavigationMapBuilder("second", secondBinding)
            .AddCell(default, cell)
            .Build();
        NavigationMap third = new NavigationMapBuilder("third", thirdBinding)
            .AddCell(default, cell)
            .Build();
        var firstOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(first, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(firstOperation).Should().BeTrue();
        for (int i = 0; i < 256 && firstOperation.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        firstOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var overBudget = new NavigationMapCommitOperation(
            new PreparedNavigationMap(second, 1),
            OverlayReplacementPolicy.Clear,
            2,
            context.FrameCount + 1);
        var sameBatch = new NavigationMapCommitOperation(
            new PreparedNavigationMap(third, 1),
            OverlayReplacementPolicy.Clear,
            3,
            context.FrameCount + 1);

        context.Pathing.Admit(overBudget).Should().BeTrue();
        context.Pathing.Admit(sameBatch).Should().BeTrue();
        context.Simulate();

        overBudget.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.TryGetNavigationGraphCellState("first", default, out _).Should().BeTrue();
        context.Pathing.TryGetNavigationGraphCellState("second", default, out _).Should().BeFalse();

        for (int i = 0; i < 64 && sameBatch.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();

        overBudget.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        sameBatch.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryGetNavigationGraphCellState("second", default, out _).Should().BeTrue();
        context.Pathing.TryGetNavigationGraphCellState("third", default, out _).Should().BeTrue();
    }

    [Fact]
    public void StructuralClosurePublication_ShouldRequeueAfterWriterLeasePressureClears()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maxRetiredSnapshots: 0,
                maxRetiredSnapshotBytes: 0,
                maintenanceBudget: budget));
        GridConfiguration firstConfiguration = CreateConfiguration();
        var secondConfiguration = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap Map(string id, NormalizedGridConfiguration binding) =>
            new NavigationMapBuilder(id, binding)
                .AddCell(default, cell)
                .Build();
        var first = new NavigationMapCommitOperation(
            new PreparedNavigationMap(Map("first", firstBinding), 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(first).Should().BeTrue();
        SimulateUntil(context, () => first.Receipt.Status != NavigationOperationStatus.Pending);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        using (NavigationWorldGraphLease pressure = context.Pathing.TryAcquireNavigationGraph()!)
        {
            var second = new NavigationMapCommitOperation(
                new PreparedNavigationMap(Map("second", secondBinding), 1),
                OverlayReplacementPolicy.Clear,
                2,
                context.FrameCount + 1);
            context.Pathing.Admit(second).Should().BeTrue();
            for (int frame = 0; frame < 8; frame++)
                context.Simulate();

            second.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
            context.Pathing.RetainedOperationWorkCount.Should().Be(0,
                "a failed initial closure publication requeues the operation before retaining composition work");
            context.Pathing.RetainedCompositionWorkCount.Should().Be(0,
                "a structural cursor cannot transfer ownership until its closure publishes");
            pressure.Graph.HasClosedStructuralScope.Should().BeFalse(
                "the writer-blocking generation remains the unchanged pre-operation root");
            store.Current.Should().BeSameAs(pressure.Graph);

            pressure.Dispose();
            SimulateUntil(
                context,
                () => second.Receipt.Status != NavigationOperationStatus.Pending);

            second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }

        store.IsSafetyPending.Should().BeFalse();
        store.Current.HasClosedStructuralScope.Should().BeFalse();
        context.Pathing.TryGetNavigationGraphCellState("first", default, out _)
            .Should().BeTrue();
        context.Pathing.TryGetNavigationGraphCellState("second", default, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void StructuralWorkCapacity_ShouldRejectAggregateRootCandidateAndScratchAndCleanup()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(15, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 16; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
        var prepared = new PreparedNavigationMap(builder.Build(), 1);
        NavigationOperationLimits limits = CreateOperationLimits(maxPreparedMapBytes: 3_000_000);
        var fold = new NavigationMapFoldWork(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            prepared,
            OverlayReplacementPolicy.Clear,
            limits,
            new GridCellPrism[limits.MaxCorridorCells],
            new Vector3d[(limits.MaxCorridorCells * 2) - 2],
            new NavigationCellAddress[limits.MaxCorridorCells],
            new NavigationAddressStampSet(limits.MaxCorridorCells));
        var foldMeter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        NavigationOperationRejection foldRejection = NavigationOperationRejection.None;
        bool foldComplete = false;
        for (int i = 0; i < 64 && !foldComplete; i++)
        {
            foldComplete = fold.Advance(foldMeter, out foldRejection);
            foldMeter.Reset();
        }
        foldComplete.Should().BeTrue();
        foldRejection.Should().Be(NavigationOperationRejection.None);
        NavigationOperationCandidate candidate = fold.Candidate;
        NavigationWorldGraph empty = NavigationWorldGraph.CreateEmpty(0);
        long cap = System.Math.Max(candidate.RetainedBytes, empty.RetainedBytes) + 64;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maxActiveSnapshotBytes: cap));
        var operation = new NavigationMapCommitOperation(
            prepared,
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(operation).Should().BeTrue();
        empty.RetainedBytes.Should().BeLessThan(cap);
        candidate.RetainedBytes.Should().BeLessThan(cap);
        checked(empty.RetainedBytes + candidate.RetainedBytes).Should().BeGreaterThan(cap);

        for (int i = 0; i < 16 && operation.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
    }

    [Fact]
    public void StructuralPublicationScratchBoundary_ShouldRejectBeforeRetainingAPartialComposition()
    {
        var limits = new NavigationOperationLimits(
            maxPendingOperations: 1,
            maxPendingDescriptorBytes: 1_000_000,
            maxPreparedMapBytes: 1_000_000,
            maxBatchItems: 1,
            maxBatchDescriptorBytes: 1_000_000,
            maxBatchSortScratchBytes: 4_096,
            maxCorridorCells: 2,
            maxMaps: 1,
            maxRetainedMapIdentities: 1,
            maxOverlayCellsPerMap: 0,
            maxOverlayConnectionsPerMap: 0,
            maxOverlayTransitionsPerMap: 0,
            maxOverlayCells: 0,
            maxOverlayConnections: 0,
            maxOverlayTransitions: 0,
            maxTransitionRulesPerMap: 1,
            maxTransitionRules: 1);
        GridConfiguration configuration = CreateConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var prepared = new PreparedNavigationMap(
            new NavigationMapBuilder("map", binding)
                .AddCell(default, new NavigationCell(
                    TraversalMedia.Solid,
                    TraversalCapability.None,
                    default,
                    Fixed64.Zero,
                    Fixed64.Zero,
                    Fixed64.One))
                .Build(),
            1);
        var probeProcessor = new NavigationOperationProcessor(
            limits,
            maxBakedCellsPerMap: 1,
            navigationAreaCount: 1);
        long emptyOperationBytes = probeProcessor.Candidate.RetainedBytes;
        var probeOperation = new NavigationMapCommitOperation(
            prepared,
            OverlayReplacementPolicy.Clear,
            1,
            1);
        probeProcessor.Admit(probeOperation).Should().BeTrue();
        probeProcessor.ProcessFrame(
                1,
                static (_, _, _, _) => NavigationCandidatePublication.Published)
            .Should().Be(NavigationOperationFrameResult.Published);
        NavigationWorldGraph empty = NavigationWorldGraph.CreateEmpty(0);
        NavigationOperationCandidate candidate = probeProcessor.Candidate;
        long candidateGrowth = Math.Max(0L, candidate.RetainedBytes - empty.RetainedBytes);
        long operationGrowth = Math.Max(0L, candidate.RetainedBytes - emptyOperationBytes);
        long minimumScratch = NavigationStructuralCompositionWork.GetMinimumScratchBytes(
            sourceMapCount: 0,
            candidateMapCount: 1,
            changedMapCount: 1,
            overlayCellCount: 0);
        long exactPublicationBoundary = checked(
            empty.RetainedBytes + candidateGrowth + minimumScratch);
        long operationFoldBoundary = checked(
            empty.RetainedBytes + operationGrowth + probeProcessor.CoverageScratchBytes);
        exactPublicationBoundary.Should().BeGreaterThan(operationFoldBoundary,
            "this fixture isolates structural publication scratch after operation folding fits");
        exactPublicationBoundary.Should().BeGreaterThan(
            TrailblazerWorldContextSettings.MinimumActiveSnapshotBytes);

        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maxActiveSnapshotBytes: exactPublicationBoundary - 1,
                operationLimits: limits,
                maxDynamicCellSlotsPerMap: 1,
                maxDynamicCellSlots: 1));
        var operation = new NavigationMapCommitOperation(
            prepared,
            OverlayReplacementPolicy.Clear,
            1,
            1);

        context.Pathing.Admit(operation).Should().BeTrue();
        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0,
            "a preflight rejection cannot leave a closure or scratch owner behind");
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
    }

    [Fact]
    public void OverlayWorkBudget_ShouldFoldPersistentSlotsAcrossFramesAtomically()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maintenanceBudget: budget));
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(commit).Should().BeTrue();
        for (int i = 0; i < 64 && commit.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            "map",
                            new[]
                            {
                                NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), cell),
                                NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), cell),
                                NavigationCellOverlayOperation.Set(new VoxelIndex(3, 0, 0), cell)
                            })
                    })),
            2,
            context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();

        context.Simulate();

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.NavigationMaintenanceMeter.OverlaySlots.Should().Be(1);
        int consumedOverlaySlots = context.Pathing.NavigationMaintenanceMeter.OverlaySlots;
        for (int x = 1; x <= 3; x++)
            context.Pathing.TryGetNavigationGraphCellState("map", new VoxelIndex(x, 0, 0), out _)
                .Should().BeFalse();

        for (int i = 0; i < 16 && overlay.Receipt.Status == NavigationOperationStatus.Pending; i++)
        {
            context.Simulate();
            consumedOverlaySlots += context.Pathing.NavigationMaintenanceMeter.OverlaySlots;
        }

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        consumedOverlaySlots.Should().Be(12,
            "folding, supersedence, and instance-page preparation must each advance under the shared meter");
        for (int x = 1; x <= 3; x++)
            context.Pathing.TryGetNavigationGraphCellState("map", new VoxelIndex(x, 0, 0), out _)
                .Should().BeTrue();
    }

    [Fact]
    public void PreserveReplacement_ShouldMeterSemanticPagePreparationWithoutEarlyPublication()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maintenanceBudget: budget));
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(3, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap original = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(original, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(install).Should().BeTrue();
        while (install.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), cell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), cell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(3, 0, 0), cell)
                })
            })),
            2,
            context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        while (overlay.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var preserve = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            3,
            context.FrameCount + 1);
        context.Pathing.Admit(preserve).Should().BeTrue();

        int consumedOverlaySlots = 0;
        for (int i = 0; i < 32 && preserve.Receipt.Status == NavigationOperationStatus.Pending; i++)
        {
            context.Simulate();
            consumedOverlaySlots += context.Pathing.NavigationMaintenanceMeter.OverlaySlots;
            context.Pathing.NavigationMaintenanceMeter.OverlaySlots.Should().BeLessThanOrEqualTo(1);
            if (preserve.Receipt.Status == NavigationOperationStatus.Pending)
            {
                using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
                lease.Graph.TryGetMap("map", out NavigationMapInstance? published).Should().BeTrue();
                published!.BakeVersion.Should().Be(1);
            }
        }

        preserve.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        consumedOverlaySlots.Should().Be(10,
            "preserved cell validation and semantic-page rebuilding must both be metered");
        using NavigationWorldGraphLease finalLease = context.Pathing.TryAcquireNavigationGraph()!;
        finalLease.Graph.TryGetMap("map", out NavigationMapInstance? final).Should().BeTrue();
        final!.BakeVersion.Should().Be(2);
        final.LastCopiedSemanticPages.Should().BePositive();
    }

    [Fact]
    public void OverlayWorkBudget_ShouldResumeSecondOperationWithoutReapplyingFirst()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(maintenanceBudget: budget));
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var map = new NavigationMapBuilder("map", binding).AddCell(default, cell).Build();
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(commit).Should().BeTrue();
        while (commit.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();

        int frame = context.FrameCount + 1;
        NavigationOverlayCommitOperation first = CreateCellOperation(
            NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), cell),
            2,
            frame);
        NavigationOverlayCommitOperation second = CreateCellOperation(
            NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), cell),
            3,
            frame);
        context.Pathing.Admit(first).Should().BeTrue();
        context.Pathing.Admit(second).Should().BeTrue();

        context.Simulate();

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.TryGetNavigationGraphCellState(
            "map",
            new VoxelIndex(1, 0, 0),
            out _).Should().BeFalse();

        for (int i = 0; i < 16 && second.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        first.Receipt.PublishedFrame.Should().Be(second.Receipt.PublishedFrame);
        context.Pathing.TryGetNavigationGraphCellState(
            "map",
            new VoxelIndex(1, 0, 0),
            out _).Should().BeTrue();
        context.Pathing.TryGetNavigationGraphCellState(
            "map",
            new VoxelIndex(2, 0, 0),
            out _).Should().BeTrue();
    }

    private static GridEventInfo CreateVoxelEvent(
        GridConfiguration configuration,
        ulong sequence,
        byte obstacleCount,
        VoxelIndex index = default,
        ushort gridIndex = 0,
        long worldSpawnToken = 1,
        long gridSpawnToken = 1) => new(
        worldSpawnToken,
        gridIndex,
        gridSpawnToken,
        configuration,
        1,
        GridEventKind.ObstacleAdded,
        index,
        default,
        default,
        new GridChangeStamp(sequence, sequence),
        true,
        true,
        obstacleCount);

    private static int GetIngressEventKeyHashCode(in GridEventInfo eventInfo)
    {
        int hash = SwiftHashTools.CombineHashCodes(
            eventInfo.WorldSpawnToken.GetHashCode(),
            eventInfo.GridSpawnToken.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, eventInfo.GridIndex.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, eventInfo.VoxelIndex.GetHashCode());
    }

    private static NavigationOverlayCommitOperation CreateCellOperation(
        NavigationCellOverlayOperation cell,
        long sequence,
        int frame) => new(
        new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta("map", new[] { cell }) })),
        sequence,
        frame);

    private static void SimulateUntil(
        TrailblazerWorldContext context,
        System.Func<bool> condition,
        int maximumFrames = 512)
    {
        for (int frame = 0; frame < maximumFrames && !condition(); frame++)
            context.Simulate();
        condition().Should().BeTrue("the bounded maintenance pipeline must converge");
    }

    private static NavigationGraphCellState GetCellState(
        TrailblazerWorldContext context,
        string mapId)
    {
        context.Pathing.TryGetNavigationGraphCellState(mapId, default, out NavigationGraphCellState state)
            .Should().BeTrue();
        return state;
    }

    private static bool IsMaterialized(
        TrailblazerWorldContext context,
        string mapId) => context.Pathing.TryGetNavigationGraphCellState(
            mapId,
            default,
            out NavigationGraphCellState state)
        && state.IsMaterialized;

    private static TrailblazerWorldContextSettings CreateSettings(
        int maxConcurrentSnapshotLeases = 2,
        int maxRetiredSnapshots = 2,
        long maxRetiredSnapshotBytes = 2_000_000,
        long maxActiveSnapshotBytes = 1_000_000,
        MaintenanceWorkBudget? maintenanceBudget = null,
        int maxIngressEntries = 32,
        NavigationOperationLimits? operationLimits = null,
        int maxDynamicCellSlotsPerMap = 32,
        int maxDynamicCellSlots = 64,
        int maxPersistentGraphPages = 1_000)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            operationLimits ?? CreateOperationLimits(),
            maintenanceBudget ?? defaults.MaintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            maxIngressEntries,
            maxIngressBytes: Math.Max(32, operationLimits?.MaxMaps ?? 16) * 256L,
            maxActiveSnapshots: 3,
            maxActiveSnapshotBytes,
            maxRetiredSnapshots,
            maxRetiredSnapshotBytes,
            maxPersistentGraphPages,
            maxDynamicCellSlotsPerMap,
            maxDynamicCellSlots,
            navigationAreaCount: 1,
            maxAreaPolicies: 8,
            maxAreaRulesPerPolicy: 32,
            maxAreaRules: 64,
            maxConcurrentSnapshotLeases,
            CreateQueryLimits(defaults.QueryLimits, maxConcurrentSnapshotLeases));
    }

    private static NavigationQueryLimits CreateQueryLimits(
        NavigationQueryLimits source,
        int maxConcurrentQueries) => new(
            source.MaxBatchItems,
            source.MaxBatchDescriptorBytes,
            maxConcurrentQueries,
            source.AStarWorkspaceMapCapacity,
            source.AStarWorkspaceEndpointPageCapacity,
            source.AStarWorkspaceComponentCapacity,
            source.AStarWorkspaceNodeCapacity,
            source.MaxAStarCacheEntries,
            source.MaxAStarReusablePayloadBytes,
            source.MaxAStarSinglePayloadBytes,
            source.MaxAStarActivePayloadBytes,
            source.MaxAStarActivePayloadLeases,
            source.FlowWorkspaceMapCapacity,
            source.FlowWorkspaceEndpointPageCapacity,
            source.FlowWorkspaceComponentCapacity,
            source.FlowWorkspaceNodeCapacity,
            source.RayWorkspaceCoveredAddressCapacity,
            source.RayWorkspaceTraceIntervalCapacity,
            source.AStarWorkspaceGuidePointCapacity,
            source.MaxFlowCacheEntries,
            source.MaxFlowReusablePayloadBytes,
            source.MaxFlowSinglePayloadBytes,
            source.MaxFlowActivePayloadBytes,
            source.MaxFlowActivePayloadLeases);

    private static TrailblazerWorldContext CreateChunkedBaselineContext(
        int maxPersistentGraphPages,
        bool includeExplicitConnection = false)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = new(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            maxBaselineAddresses: 64,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                maxActiveSnapshotBytes: 4_000_000,
                maintenanceBudget: budget,
                operationLimits: CreateOperationLimits(
                    maxOverlayCellsPerMap: 1,
                    maxOverlayCells: 1,
                    maxPreparedMapBytes: 3_000_000),
                maxDynamicCellSlotsPerMap: 1,
                maxDynamicCellSlots: 1,
                maxPersistentGraphPages: maxPersistentGraphPages));
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(128, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            context.World.TryAddGrid(configuration, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            NavigationCell cell = new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One);
            var builder = new NavigationMapBuilder("map", binding);
            for (int x = 0; x < 129; x++)
                builder.AddCell(new VoxelIndex(x, 0, 0), cell);
            if (includeExplicitConnection)
            {
                binding.TryGetCellPrism(default, out GridCellPrism source).Should().BeTrue();
                binding.TryGetCellPrism(new VoxelIndex(1, 0, 0), out GridCellPrism destination)
                    .Should().BeTrue();
                builder.AddConnection(new NavigationConnection(
                    "page-ceiling",
                    default,
                    new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                    new Vector3d(source.Center.X, source.VerticalMin, source.Center.Z),
                    new Vector3d(destination.Center.X, destination.VerticalMin, destination.Center.Z),
                    Fixed64.Zero,
                    Fixed64.One));
            }
            context.Pathing.Admit(new NavigationMapCommitOperation(
                new PreparedNavigationMap(builder.Build(), 1),
                OverlayReplacementPolicy.Clear,
                1,
                1)).Should().BeTrue();
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationOperationLimits CreateOperationLimits(
        int maxOverlayCellsPerMap = 32,
        int maxOverlayCells = 64,
        long maxPreparedMapBytes = 1_000_000,
        int maxMaps = 16) => new(
        maxPendingOperations: 32,
        maxPendingDescriptorBytes: 1_000_000,
        maxPreparedMapBytes,
        maxBatchItems: 32,
        maxBatchDescriptorBytes: 1_000_000,
        maxBatchSortScratchBytes: 1_000_000,
        maxCorridorCells: 64,
        maxMaps,
        maxRetainedMapIdentities: Math.Max(32, maxMaps),
        maxOverlayCellsPerMap,
        maxOverlayConnectionsPerMap: 32,
        maxOverlayTransitionsPerMap: 32,
        maxOverlayCells,
        maxOverlayConnections: 64,
        maxOverlayTransitions: 64,
        maxTransitionRulesPerMap: 32,
        maxTransitionRules: 64);

    private static GridConfiguration CreateConfiguration() => new(
        Vector3d.Zero,
        new Vector3d(2, 1, 1),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: GridStorageKind.Dense);
}
