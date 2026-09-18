using System;
using System.Reflection;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationGuideSchedulingTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_CancelledBuildingLeaderThenRawMutationMustNotReviveAnyMember(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out var query,
            new NavigationSchedulingSettings(operationStepLimit: 1, nodeStepLimit: 1));
        using var leader = Schedule(context, query);
        using var follower = Schedule(context, query);
        for (int frame = 0; frame < 128 && leader.Phase != NavigationGuideRequestPhase.Search; frame++) context.Simulate();
        leader.Phase.Should().Be(NavigationGuideRequestPhase.Search);
        leader.Dispose();
        var grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out var voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        for (int frame = 0; frame < 128 && follower.Status == NavigationGuideStatus.Pending; frame++) context.Simulate();
        leader.Status.Should().Be(NavigationGuideStatus.Cancelled);
        follower.Status.Should().Be(NavigationGuideStatus.Stale);
        follower.Dispose();
        AssertDrained(context);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_CancelBeforeBuild_ShouldReleaseAdmissionAndPromoteFollower(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        for (int i = 0; i < 128 && leader.Phase != NavigationGuideRequestPhase.Admission; i++) context.Simulate();
        leader.Phase.Should().Be(NavigationGuideRequestPhase.Admission);
        Diagnostics(context, algorithm).BuildStarts.Should().Be(0);
        leader.Dispose();
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        Complete(context, follower);
        Diagnostics(context, algorithm).BuildStarts.Should().Be(1);
        Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_IncompatibleInterleaving_ShouldNotAttachAcrossTheBoundary(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1, endpointCandidateStepLimit: 64));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest unrelated = Schedule(context,
            WithAlgorithm(query, algorithm == PathAlgorithm.AStar ? PathAlgorithm.FlowField : PathAlgorithm.AStar));
        using NavigationGuideRequest laterMatch = Schedule(context, query);
        Complete(context, first);
        unrelated.Status.Should().Be(NavigationGuideStatus.Pending);
        laterMatch.Status.Should().Be(NavigationGuideStatus.Pending);
        Complete(context, unrelated);
        laterMatch.Status.Should().Be(NavigationGuideStatus.Pending);
        Complete(context, laterMatch);
        Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(0);
        Diagnostics(context, algorithm).CacheCheckoutHits.Should().Be(1);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void FrozenCohort_CancellationAfterPublication_ShouldRetainPinOnlyForLiveFollowers(PathAlgorithm algorithm, bool cancelAll)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        Complete(context, leader);
        follower.Status.Should().Be(NavigationGuideStatus.Pending);
        leader.Dispose();
        if (cancelAll)
            follower.Dispose();
        else
        {
            Complete(context, follower);
            Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(1);
            follower.Dispose();
        }
        AssertDrained(context);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void FrozenCohort_CancelFollowersDuringBuild_ShouldNotLeakOrRestartLeader(PathAlgorithm algorithm, bool cancelAll)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(nodeStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        for (int i = 0; i < 128 && leader.Phase != NavigationGuideRequestPhase.Search; i++) context.Simulate();
        leader.Phase.Should().Be(NavigationGuideRequestPhase.Search);
        follower.Dispose();
        if (!cancelAll)
            Complete(context, leader);
        leader.Dispose();
        Diagnostics(context, algorithm).BuildStarts.Should().Be(1);
        AssertDrained(context);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_ResetDuringFollowerAdmission_ShouldDrainAllOwnership(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(lookupStepLimit: 1, operationStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        Complete(context, leader);
        for (int i = 0; i < 256 && follower.Phase != NavigationGuideRequestPhase.Admission; i++) context.Simulate();
        follower.Phase.Should().Be(NavigationGuideRequestPhase.Admission);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(1);
        context.Pathing.Reset();
        follower.Status.Should().Be(NavigationGuideStatus.Cancelled);
        AssertDrained(context);
    }

    [Fact]
    public void FrozenCohort_InvalidLeaderOrigin_ShouldNotFailValidFollower()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        using NavigationGuideRequest leader = Schedule(context,
            query.WithStartState(new Vector3d(-100, 0, 0), TraversalMedium.Solid));
        using NavigationGuideRequest follower = Schedule(context, query);
        Complete(context, follower);
        leader.Status.Should().Be(NavigationGuideStatus.InvalidStart);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(1);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_RawMutationDuringFormation_ShouldFenceMembersAddedLater(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        context.Simulate();
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        for (int i = 0; i < 64 && follower.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        leader.Status.Should().Be(NavigationGuideStatus.Stale);
        follower.Status.Should().Be(NavigationGuideStatus.Stale);
        Diagnostics(context, algorithm).BuildStarts.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_CancelledLeaderAtCapacityOne_ShouldTransferToOldestRemaining(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(nodeStepLimit: 1), limits: CohortLimits(1, reusable: false));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest oldest = Schedule(context, query);
        using NavigationGuideRequest last = Schedule(context, query);
        for (int i = 0; i < 128 && leader.Phase != NavigationGuideRequestPhase.Search; i++) context.Simulate();
        leader.Dispose();
        Complete(context, oldest);
        for (int i = 0; i < 128 && last.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        last.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
        Diagnostics(context, algorithm).BuildStarts.Should().Be(1);
        Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(1);
        oldest.Dispose();
        last.Dispose();
        AssertDrained(context);
    }

    private static void AssertDrained(TrailblazerWorldContext context)
    {
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        NavigationAStarPayloadCache a = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationFlowFieldPayloadCache f = context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        a.ActiveLeaseCount.Should().Be(0);
        f.ActiveLeaseCount.Should().Be(0);
        a.ReservedLeaseCount.Should().Be(0);
        f.ReservedLeaseCount.Should().Be(0);
        a.ReservedPayloadBytes.Should().Be(0);
        f.ReservedPayloadBytes.Should().Be(0);
        a.LeasedBytes.Should().Be(0);
        f.LeasedBytes.Should().Be(0);
        a.DetachedBytes.Should().Be(0);
        f.DetachedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 8)]
    [InlineData(PathAlgorithm.FlowField, 8)]
    [InlineData(PathAlgorithm.AStar, 1)]
    [InlineData(PathAlgorithm.FlowField, 1)]
    public void FrozenCohort_NonReusablePayload_ShouldShareOrTransferOldestAtCapacityOne(PathAlgorithm algorithm, int capacity)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16), limits: CohortLimits(capacity, reusable: false));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, query);
        Complete(context, first);
        if (capacity == 1)
        {
            for (int i = 0; i < 64 && second.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
            second.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
            Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(0);
        }
        else
        {
            Complete(context, second);
            Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(1);
            if (algorithm == PathAlgorithm.AStar)
            {
                first.TryTakeGuide(out NavigationGuideLease? left).Should().BeTrue();
                second.TryTakeGuide(out NavigationGuideLease? right).Should().BeTrue();
                using NavigationGuideLease a = left!.Value;
                using NavigationGuideLease b = right!.Value;
                a.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
                b.CurrentStepIndex.Should().Be(0);
                a.Dispose();
                b.Status.Should().Be(NavigationGuideStatus.Success);
            }
        }
        Diagnostics(context, algorithm).BuildStarts.Should().Be(1);
        first.Dispose();
        second.Dispose();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.DetachedBytes.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void FrozenCohort_PresentButInsufficientExtraCost_ShouldFinishCoveredFollowerBeforePromotion()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery baseQuery,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 64, nodeStepLimit: 1), cells: 10);
        PathQuery QueryAt(int start) => new PathQuery(baseQuery.Start, baseQuery.End, baseQuery.Agent,
            baseQuery.AreaPolicy, baseQuery.Traversal, baseQuery.Algorithm, baseQuery.Budget, false,
            new FlowFieldQueryOptions((Fixed64)2)).WithStartState(
                baseQuery.Start.Position + new Vector3d(start, 0, 0), TraversalMedium.Solid);
        using NavigationGuideRequest leader = Schedule(context, QueryAt(5));
        using NavigationGuideRequest uncovered = Schedule(context, QueryAt(3));
        using NavigationGuideRequest covered = Schedule(context, QueryAt(7));
        using NavigationGuideRequest unrelated = Schedule(context, WithAlgorithm(baseQuery, PathAlgorithm.AStar));
        Complete(context, covered);
        uncovered.Status.Should().Be(NavigationGuideStatus.Pending);
        unrelated.Status.Should().Be(NavigationGuideStatus.Pending);
        Diagnostics(context, PathAlgorithm.FlowField).SharedCoverageMisses.Should().Be(1);
        Diagnostics(context, PathAlgorithm.FlowField).SharedFollowerCheckouts.Should().Be(1);
        Complete(context, uncovered);
        unrelated.Status.Should().Be(NavigationGuideStatus.Pending);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(2);
        Diagnostics(context, PathAlgorithm.FlowField).Promotions.Should().Be(1);
        Complete(context, unrelated);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_MultiFrameFormation_ShouldExcludeArrivalsAfterItsFiniteHorizon(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 64, operationStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest frozen = Schedule(context, query);
        context.Simulate();
        using NavigationGuideRequest late = Schedule(context, query);
        Complete(context, frozen);
        Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(1);
        late.Status.Should().Be(NavigationGuideStatus.Pending);
        Complete(context, late);
        Diagnostics(context, algorithm).SharedFollowerCheckouts.Should().Be(1);
        Diagnostics(context, algorithm).CacheCheckoutHits.Should().Be(1);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FrozenCohort_CancelledBuildingLeader_ShouldPreserveOneBuildForFollower(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(nodeStepLimit: 1));
        using NavigationGuideRequest leader = Schedule(context, query);
        using NavigationGuideRequest follower = Schedule(context, query);
        for (int frame = 0; frame < 128 && leader.Phase != NavigationGuideRequestPhase.Search; frame++)
            context.Simulate();
        leader.Phase.Should().Be(NavigationGuideRequestPhase.Search);
        leader.Dispose();
        Complete(context, follower);
        Diagnostics(context, algorithm).BuildStarts.Should().Be(1,
            "the frozen follower still owns interest in the original query extent");
        leader.Status.Should().Be(NavigationGuideStatus.Cancelled);
    }

    [Fact]
    public void ScheduledRebake_ShouldBuildFarOriginOnceAndReuseForNearOrigin()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? oldResult)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationFlowFieldLease old = oldResult!.Value;
        Rebake(context);
        using NavigationGuideRequest far = Schedule(context, query);
        using NavigationGuideRequest near = Schedule(context,
            query.WithStartState(query.Start.Position + new Vector3d(1, 0, 0), TraversalMedium.Solid));
        Complete(context, far);
        Complete(context, near);
        old.Status.Should().Be(NavigationGuideStatus.Stale);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(2);
        Diagnostics(context, PathAlgorithm.FlowField).Promotions.Should().Be(0);
        far.TryTakeFlowField(out NavigationFlowFieldLease? farResult).Should().BeTrue();
        near.TryTakeFlowField(out NavigationFlowFieldLease? nearResult).Should().BeTrue();
        using NavigationFlowFieldLease farLease = farResult!.Value;
        using NavigationFlowFieldLease nearLease = nearResult!.Value;
        farLease.OriginIntegrationCost.Should().Be((Fixed64)2);
        nearLease.OriginIntegrationCost.Should().Be(Fixed64.One);
    }

    [Fact]
    public void ImmediateRebake_ShouldRetainItsStaleIncumbentOutcome()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        Rebake(context);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Stale);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(1);
    }

    [Fact]
    public void ScheduledRebake_DisconnectedGraph_ShouldReturnFreshNoPath()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        Rebake(context, disconnected: true);
        using NavigationGuideRequest request = Schedule(context, query);
        for (int i = 0; i < 256 && request.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.NoPath);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(2);
    }

    [Fact]
    public void ScheduledRebake_OneBelowFarNodeBudget_ShouldNotRefillTheQuery()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var farQuery = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal,
            query.Algorithm, new NavigationWorkBudget(4096, 4096, 2, 4096, 4096, 0, 0, 0, 0, 0, 0), false);
        RequestSynchronously(context, farQuery.WithStartState(query.Start.Position + new Vector3d(1, 0, 0),
            TraversalMedium.Solid)).Should().Be(NavigationGuideStatus.Success);
        Rebake(context);
        using NavigationGuideRequest request = Schedule(context, farQuery);
        for (int i = 0; i < 256 && request.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(2);
        Diagnostics(context, PathAlgorithm.FlowField).CompletedQueries.Should().Be(2);
    }

    [Fact]
    public void ScheduledRebake_ActiveCapacityOne_ShouldKeepOldLeaseStaleAndRejectNewReservation()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            limits: CohortLimits(1));
        context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? old).Should().Be(NavigationGuideStatus.Success);
        using NavigationFlowFieldLease oldLease = old!.Value;
        Rebake(context);
        using NavigationGuideRequest request = Schedule(context, query);
        for (int i = 0; i < 256 && request.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
        oldLease.Status.Should().Be(NavigationGuideStatus.Stale);
        Diagnostics(context, PathAlgorithm.FlowField).BuildStarts.Should().Be(1);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.DetachedBytes.Should().BeGreaterThan(0);
        oldLease.Dispose();
        request.Dispose();
        AssertDrained(context);
    }

    private static NavigationAcquisitionDiagnostics Diagnostics(TrailblazerWorldContext context, PathAlgorithm algorithm) =>
        algorithm == PathAlgorithm.AStar
            ? context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics
            : context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics;

    private static void Rebake(TrailblazerWorldContext context, bool disconnected = false)
    {
        var configuration = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 1, 1));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("scheduled", binding);
        for (int i = 0; i < 3; i++)
            if (!disconnected || i != 1)
                builder.AddCell(new VoxelIndex(i, 0, 0), GuidedPathTestScene.Cell(TraversalMedia.Solid));
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 2),
            OverlayReplacementPolicy.Clear, 3, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    [Fact]
    public void OperationCeiling_ShouldPermitProgressPastRetainedResultsAndCancelledScanEntries()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16, operationStepLimit: 1));
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        var retained = new NavigationGuideRequest[6];
        try
        {
            for (int i = 0; i < retained.Length; i++)
            {
                retained[i] = Schedule(context, query);
                Complete(context, retained[i]);
            }
            using NavigationGuideRequest cancelled = Schedule(context, query);
            using NavigationGuideRequest last = Schedule(context, query);
            cancelled.Dispose();
            for (int frame = 0; frame < 12 && last.Status == NavigationGuideStatus.Pending; frame++)
            {
                context.Simulate();
                context.Guides.Scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
            }
            last.Status.Should().Be(NavigationGuideStatus.Success);
            foreach (NavigationGuideRequest request in retained)
                request.Status.Should().Be(NavigationGuideStatus.Success);
        }
        finally
        {
            foreach (NavigationGuideRequest? request in retained)
                request?.Dispose();
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void NodeSlice_ShouldCountPostprocessingAndNeverRefillWithinAFrame(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16, nodeStepLimit: 1));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, WithAlgorithm(query,
            algorithm == PathAlgorithm.AStar ? PathAlgorithm.FlowField : PathAlgorithm.AStar));
        int totalCountedNodes = 0;
        for (int frame = 0; frame < 128 && second.Status == NavigationGuideStatus.Pending; frame++)
        {
            context.Simulate();
            NavigationAcquisitionSlice consumed = context.Guides.Scheduler.LastFrameCountedWork;
            consumed.NodeRemaining.Should().BeInRange(0, 1);
            totalCountedNodes += consumed.NodeRemaining;
            context.Guides.Scheduler.LastFrameOperationCount.Should().BeLessThan(64,
                "an exhausted slice must yield on no progress instead of burning the operation ceiling");
        }
        first.Status.Should().Be(NavigationGuideStatus.Success);
        second.Status.Should().Be(NavigationGuideStatus.Success);
        totalCountedNodes.Should().BeGreaterThan(6, "both three-node searches also reconstruct or clear/replay/copy payloads");
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void MixedColdRequests_OneBelowAggregateNodeAllowance_ShouldRetainTheFinalPayloadCopy(PathAlgorithm firstAlgorithm)
    {
        // Three cells: A* spends 3 expansions + 3 reconstruction + 1 reverse + 1 initial
        // guide point + 6 replay points (including repeated source feet) + 5 payload points = 19. Flow spends 3 each
        // for expansion, transition count, replay scan and payload copy = 12.
        using TrailblazerWorldContext context = CreateContext(firstAlgorithm, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16, nodeStepLimit: 30));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, WithAlgorithm(query,
            firstAlgorithm == PathAlgorithm.AStar ? PathAlgorithm.FlowField : PathAlgorithm.AStar));

        context.Simulate();

        first.Status.Should().Be(NavigationGuideStatus.Success);
        second.Status.Should().Be(NavigationGuideStatus.Pending);
        context.Guides.Scheduler.LastFrameCountedWork.NodeRemaining.Should().Be(30);
        context.Guides.Scheduler.LastFrameRemaining.NodeRemaining.Should().Be(0);
        context.Simulate();
        second.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.Scheduler.LastFrameCountedWork.NodeRemaining.Should().Be(1);
    }

    [Fact]
    public void ReusedFlowWorkspace_OneBelowClearSearchAndCopyAllowance_ShouldYieldWithoutRefilling()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16, nodeStepLimit: 14));
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        var profile = new NavigationAgentProfile(query.Agent.Shape, query.Agent.MaxStepUp,
            query.Agent.MaxDropDown, Fixed64.Quarter, query.Agent.AllowedMedia, query.Agent.Capabilities);
        var freshKey = new PathQuery(query.Start, query.End, profile, query.AreaPolicy, query.Traversal,
            query.Algorithm, query.Budget, false);
        using NavigationGuideRequest request = Schedule(context, freshKey);

        context.Simulate();

        // Three retained records to clear, then 12 search/count/replay/copy node steps.
        request.Status.Should().Be(NavigationGuideStatus.Pending);
        context.Guides.Scheduler.LastFrameCountedWork.NodeRemaining.Should().Be(14);
        context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.Scheduler.LastFrameCountedWork.NodeRemaining.Should().Be(1);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(2);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField, PathAlgorithm.FlowField)]
    [InlineData(PathAlgorithm.AStar, PathAlgorithm.FlowField)]
    public void WarmRequests_WithSharedAllowance_ShouldCompleteTogetherInOneFrame(
        PathAlgorithm firstAlgorithm, PathAlgorithm secondAlgorithm)
    {
        using TrailblazerWorldContext context = CreateContext(firstAlgorithm, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 16));
        PathQuery secondQuery = WithAlgorithm(query, secondAlgorithm);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        RequestSynchronously(context, secondQuery).Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, secondQuery);

        context.Simulate();

        first.Status.Should().Be(NavigationGuideStatus.Success);
        second.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(2);
    }

    [Fact]
    public void MixedWarmRequests_OneBelowAggregateEndpointAllowance_ShouldYieldSecondRequest()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(endpointCandidateStepLimit: 3));
        PathQuery flow = WithAlgorithm(query, PathAlgorithm.FlowField);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        RequestSynchronously(context, flow).Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, flow);

        context.Simulate();

        first.Status.Should().Be(NavigationGuideStatus.Success);
        second.Status.Should().Be(NavigationGuideStatus.Pending);
        context.Simulate();
        second.Status.Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 2, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(PathAlgorithm.AStar, 3, NavigationGuideStatus.Success)]
    [InlineData(PathAlgorithm.FlowField, 2, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(PathAlgorithm.FlowField, 3, NavigationGuideStatus.Success)]
    public void SharedFrameSlices_ShouldPreserveExactTotalExpandedNodeBudget(
        PathAlgorithm algorithm, int maximumNodes, NavigationGuideStatus expected)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(nodeStepLimit: 1));
        query = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal,
            algorithm, new NavigationWorkBudget(4096, 4096, maximumNodes, 4096, 4096, 0, 0, 0, 0, 0, 0), false);
        using NavigationGuideRequest request = Schedule(context, query);

        for (int frame = 0; frame < 128 && request.Status == NavigationGuideStatus.Pending; frame++)
            context.Simulate();

        request.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void BatchAdmission_ShouldNotRefillCandidatesForTheNextMember(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        var batch = new PathQueryBatch(new[]
        {
            new PathQueryBatchItem(1, query),
            new PathQueryBatchItem(2, query)
        }, 2);

        if (algorithm == PathAlgorithm.AStar)
        {
            context.Pathing.NavigationAStarAdmissionGate.Begin(batch, out NavigationAStarBatchWork work)
                .Should().Be(NavigationAStarQueryStatus.Pending);
            using (work)
            {
                work.AdvanceAdmission(8192, 3);
                work.IsAdmissionComplete.Should().BeFalse("two endpoints per member need four shared candidate credits");
                work.AdvanceAdmission(8192, 1);
                work.IsAdmissionComplete.Should().BeTrue();
            }
        }
        else
        {
            context.Pathing.NavigationFlowAdmissionGate.Begin(batch, out NavigationFlowBatchWork work)
                .Should().Be(NavigationFlowQueryStatus.Pending);
            using (work)
            {
                work.AdvanceAdmission(8192, 3);
                work.IsAdmissionComplete.Should().BeFalse("two endpoints per member need four shared candidate credits");
                work.AdvanceAdmission(8192, 1);
                work.IsAdmissionComplete.Should().BeTrue();
            }
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void AcquisitionDiagnostics_CancelledSearch_ShouldRetainWorkExactlyOnce(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(nodeStepLimit: 1));
        NavigationAcquisitionDiagnostics counters = algorithm == PathAlgorithm.AStar
            ? context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics
            : context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics;
        using NavigationGuideRequest request = Schedule(context, query);
        for (int frame = 0; frame < 128 && counters.BuildStarts == 0; frame++)
            context.Simulate();
        counters.BuildStarts.Should().Be(1);
        request.Status.Should().Be(NavigationGuideStatus.Pending);
        counters.CompletedQueries.Should().Be(0);
        request.Dispose();
        request.Dispose();
        request.Status.Should().Be(NavigationGuideStatus.Cancelled);
        counters.CompletedQueries.Should().Be(1, "cancelled admitted work is accounted only once");
        counters.EndpointCandidates.Should().BeGreaterThan(0);
        counters.CacheCheckoutHits.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 0)]
    [InlineData(PathAlgorithm.AStar, 1)]
    [InlineData(PathAlgorithm.AStar, 2)]
    [InlineData(PathAlgorithm.FlowField, 0)]
    [InlineData(PathAlgorithm.FlowField, 1)]
    [InlineData(PathAlgorithm.FlowField, 2)]
    public void Dispose_QueuedActiveOrCompleted_ShouldReleaseCapacityWithoutAffectingReplacement(
        PathAlgorithm algorithm, int phase)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(maxRetainedRequests: 1));
        NavigationGuideRequest request = Schedule(context, query);
        if (phase == 1)
            context.Simulate();
        else if (phase == 2)
            Complete(context, request);
        context.Guides.Schedule(query, out NavigationGuideRequest? rejected)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
        rejected.Should().BeNull();
        request.Dispose();
        request.Status.Should().Be(phase == 2 ? NavigationGuideStatus.Success : NavigationGuideStatus.Cancelled);
        using NavigationGuideRequest replacement = Schedule(context, query);
        request.Dispose();
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
        Complete(context, replacement);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(algorithm == PathAlgorithm.AStar ? 1 : 0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(algorithm == PathAlgorithm.FlowField ? 1 : 0);
        replacement.Dispose();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Fact]
    public void CompletedUnclaimedRequest_ShouldNotBlockLaterMixedFifoWork()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, WithAlgorithm(query, PathAlgorithm.FlowField));
        using NavigationGuideRequest third = Schedule(context, query);
        for (int frame = 0; frame < 4096 && first.Status == NavigationGuideStatus.Pending; frame++)
        {
            context.Simulate();
            second.Status.Should().Be(NavigationGuideStatus.Pending);
            third.Status.Should().Be(NavigationGuideStatus.Pending);
        }
        first.Status.Should().Be(NavigationGuideStatus.Success);
        Complete(context, second);
        third.Status.Should().Be(NavigationGuideStatus.Pending);
        Complete(context, third);
        first.TryTakeGuide(out NavigationGuideLease? lease).Should().BeTrue();
        lease!.Value.Dispose();
    }

    [Fact]
    public void DescriptorCeiling_ShouldCountCompletedRequestsAndRecoverExactBytes()
    {
        // Descriptor = 248 fixed bytes + three nine-character identifiers (54 bytes).
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(maxRetainedDescriptorBytes: 604));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, query);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(2);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(604);
        Complete(context, first);
        context.Guides.Schedule(query, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        first.TryTakeGuide(out NavigationGuideLease? lease).Should().BeTrue();
        lease!.Value.Dispose();
        using NavigationGuideRequest third = Schedule(context, query);
        second.Dispose();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(302);
        Complete(context, third);
    }

    [Fact]
    public void DescriptorOneBelow_ShouldRejectWithoutReservingRequestCapacity()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(maxRetainedDescriptorBytes: 301));
        context.Guides.Schedule(query, out NavigationGuideRequest? rejected)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
        rejected.Should().BeNull();
        var shorter = new PathQuery(new NavigationEndpoint(query.Start.Position),
            new NavigationEndpoint(query.End.Position), query.Agent, query.AreaPolicy, query.Traversal,
            query.Algorithm, query.Budget, query.AllowTransitions);
        using NavigationGuideRequest accepted = Schedule(context, shorter);
        Complete(context, accepted);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ActiveRequest_ShouldHoldItsSynchronousGateUntilDisposed(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest request = Schedule(context, query);
        context.Simulate();
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.CapacityExceeded);
        request.Dispose();
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ExistingGateOwner_ShouldFailScheduledActivationWithoutCancellingThatOwner(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest request = Schedule(context, query);
        if (algorithm == PathAlgorithm.AStar)
        {
            context.Pathing.NavigationAStarAdmissionGate.Begin(query, out NavigationAStarBatchWork work)
                .Should().Be(NavigationAStarQueryStatus.Pending);
            using (work)
            {
                context.Simulate();
                request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
                work.AdmittedCount.Should().Be(1);
            }
        }
        else
        {
            context.Pathing.NavigationFlowAdmissionGate.Begin(query, out NavigationFlowBatchWork work)
                .Should().Be(NavigationFlowQueryStatus.Pending);
            using (work)
            {
                context.Simulate();
                request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
                work.AdmittedCount.Should().Be(1);
            }
        }
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void TakingCompletedResult_AfterPublishedOrRawWorldChange_ShouldFailStaleAndReleaseLease(
        PathAlgorithm algorithm, bool publish)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest request = Schedule(context, query);
        Complete(context, request);
        if (publish)
        {
            var remove = new NavigationMapRemoveOperation("scheduled", 3, context.FrameCount + 1);
            context.Pathing.Admit(remove).Should().BeTrue();
            GuidedPathTestScene.AdvanceUntilApplied(context, remove.Receipt);
        }
        else
        {
            VoxelGrid grid = context.World.ActiveGrids[0];
            grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        }
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void PublicationWhileActive_ShouldFailStaleInsteadOfRestarting(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest request = Schedule(context, query);
        context.Simulate();
        var remove = new NavigationMapRemoveOperation("scheduled", 3, context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        for (int frame = 0; frame < 4096 && request.Status == NavigationGuideStatus.Pending; frame++)
            context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrDispose_ShouldReleaseCompletedActiveAndQueuedOwnership(bool dispose)
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        using NavigationGuideRequest completed = Schedule(context, query);
        Complete(context, completed);
        using NavigationGuideRequest active = Schedule(context, WithAlgorithm(query, PathAlgorithm.FlowField));
        using NavigationGuideRequest queued = Schedule(context, query);
        context.Simulate();
        if (dispose)
            context.Dispose();
        else
            context.Reset();
        completed.Status.Should().Be(NavigationGuideStatus.Success);
        active.Status.Should().Be(NavigationGuideStatus.Cancelled);
        queued.Status.Should().Be(NavigationGuideStatus.Cancelled);
        completed.TryTakeGuide(out _).Should().BeFalse();
        active.TryTakeFlowField(out _).Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void WorkSubmittedByHostHook_ShouldNotActivateUntilTheFollowingFrame()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        NavigationGuideRequest? request = null;
        using IDisposable hook = context.RegisterOnSimulate("scheduler-test", 0, () =>
        {
            if (request == null)
                request = Schedule(context, query);
        });
        context.Simulate();
        using NavigationGuideRequest accepted = TestRequire.NotNull(request);
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.CapacityExceeded);
        Complete(context, accepted);
    }

    [Fact]
    public void TinySlices_ShouldCompleteAtIdenticalFramesAndExactCostsOnReplay()
    {
        (int firstCompletion, int secondCompletion) = ReplayTinySlices();
        (int replayFirstCompletion, int replaySecondCompletion) = ReplayTinySlices();
        replayFirstCompletion.Should().Be(firstCompletion);
        replaySecondCompletion.Should().Be(secondCompletion);
        secondCompletion.Should().BeGreaterThan(firstCompletion);
        secondCompletion.Should().BeGreaterThan(10);
    }

    private static (int FirstCompletion, int SecondCompletion) ReplayTinySlices()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(lookupStepLimit: 1, endpointCandidateStepLimit: 1,
                nodeStepLimit: 1, edgeStepLimit: 1, connectionStepLimit: 1));
        using NavigationGuideRequest first = Schedule(context, query);
        using NavigationGuideRequest second = Schedule(context, WithAlgorithm(query, PathAlgorithm.FlowField));
        Complete(context, first);
        int firstCompletion = context.FrameCount;
        Complete(context, second);
        int secondCompletion = context.FrameCount;
        first.TryTakeGuide(out NavigationGuideLease? guide).Should().BeTrue();
        second.TryTakeFlowField(out NavigationFlowFieldLease? flow).Should().BeTrue();
        using NavigationGuideLease guideLease = guide!.Value;
        using NavigationFlowFieldLease flowLease = flow!.Value;
        guideLease.TotalCost.Should().Be((Fixed64)2);
        flowLease.OriginIntegrationCost.Should().Be((Fixed64)2);
        return (firstCompletion, secondCompletion);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void TotalQueryBudget_ShouldStillFailAcrossPositiveFrameSlices(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        query = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal,
            algorithm, default, false);
        using NavigationGuideRequest request = Schedule(context, query);
        for (int frame = 0; frame < 32 && request.Status == NavigationGuideStatus.Pending; frame++)
            context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidQueryAndDisposedService_ShouldRejectWithoutCreatingTickets()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        context.Guides.Schedule(default, out NavigationGuideRequest? rejected)
            .Should().Be(NavigationGuideStatus.InvalidProfile);
        rejected.Should().BeNull();
        context.Dispose();
        Action action = () => context.Guides.Schedule(query, out _);
        action.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void DirectPathingReset_ShouldCancelScheduledWorkBeforeInvalidatingItsGate(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest active = Schedule(context, query);
        using NavigationGuideRequest queued = Schedule(context, query);
        context.Simulate();
        context.Pathing.Reset();
        active.Status.Should().Be(NavigationGuideStatus.Cancelled);
        queued.Status.Should().Be(NavigationGuideStatus.Cancelled);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
        context.Simulate();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void RawWorldChange_BeforePublication_ShouldFailWithoutCreatingAPublicLease(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        using NavigationGuideRequest request = Schedule(context, query);
        for (int frame = 0; frame < 4096 && request.Phase != NavigationGuideRequestPhase.Publication; frame++)
            context.Simulate();
        request.Phase.Should().Be(NavigationGuideRequestPhase.Publication);
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void PublicLeaseCapacityExhaustion_ShouldFailAtCompletionAndReleaseGate(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        var guides = new NavigationGuideLease?[context.Settings.QueryLimits.MaxAStarActivePayloadLeases];
        var flows = new NavigationFlowFieldLease?[context.Settings.QueryLimits.MaxFlowActivePayloadLeases];
        try
        {
            if (algorithm == PathAlgorithm.AStar)
            {
                for (int i = 0; i < guides.Length; i++)
                    context.Guides.RequestGuide(query, out guides[i]).Should().Be(NavigationGuideStatus.Success);
            }
            else
            {
                for (int i = 0; i < flows.Length; i++)
                    context.Guides.RequestFlowField(query, out flows[i]).Should().Be(NavigationGuideStatus.Success);
            }
            using NavigationGuideRequest request = Schedule(context, query);
            for (int frame = 0; frame < 4096 && request.Status == NavigationGuideStatus.Pending; frame++)
                context.Simulate();
            request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            foreach (NavigationGuideLease? guide in guides)
                guide?.Dispose();
            foreach (NavigationFlowFieldLease? flow in flows)
                flow?.Dispose();
        }
        RequestSynchronously(context, query).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void FlowLeader_PublicationInvalidatingItsPinBeforePublicGuideMustFailStaleAndDrain()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out var query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        var gate = context.Pathing.NavigationFlowAdmissionGate;
        gate.Begin(query, out var seed).Should().Be(NavigationFlowQueryStatus.Pending);
        for (int i = 0; i < 4096 && !seed.IsAdmissionComplete; i++) seed.AdvanceAdmission(4096, 4096);
        for (int i = 0; i < 4096 && !seed.IsReadyToPublish(0); i++) seed.AdvanceSearch(0, 4096, 4096, 4096, 4096);
        seed.PublishReadyPrefix(1);
        seed.GetStatus(0).Should().Be(NavigationFlowQueryStatus.Success);
        var retained = seed.TakeResult(0).PayloadLease;
        seed.Dispose();
        retained.TryGetPayload(out var payload).Should().Be(NavigationFlowFieldStatus.Success);
        using var request = Schedule(context, query);
        for (int i = 0; i < 4096 && request.Phase != NavigationGuideRequestPhase.Publication; i++) context.Simulate();
        request.Phase.Should().Be(NavigationGuideRequestPhase.Publication);
        object coordinator = typeof(NavigationFlowAdmissionGate).GetField("_coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(gate)!;
        object sync = coordinator.GetType().GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;
        using var started = new ManualResetEventSlim();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            started.Set();
            try { context.Simulate(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        var cache = gate.PayloadCache;
        var store = context.Pathing.NavigationGraphStore;
        try
        {
            lock (sync)
            {
                worker.Start();
                started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).Should().BeTrue();
                SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, 10_000)
                    .Should().BeTrue("the only conflicting monitor is coordinator.Release after the source-pin transfer");
                var graph = store.Current;
                store.TryPublish(graph.WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
                    .WithGraphVersion(graph.GraphVersion + 1)).Should().Be(NavigationCandidatePublication.Published);
                cache.CompletePostLockValidation(store, payload, NavigationFlowFieldStatus.Success, ref retained)
                    .Should().Be(NavigationFlowFieldStatus.Stale);
            }
            worker.Join(10_000).Should().BeTrue();
            failure.Should().BeNull();
            request.Status.Should().Be(NavigationGuideStatus.Stale);
            request.TryTakeFlowField(out _).Should().BeFalse();
        }
        finally
        {
            worker.Join(10_000).Should().BeTrue();
            retained.Dispose();
        }
        request.Dispose();
        for (int i = 0; i < 16; i++) context.Simulate();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Fact]
    public void ScheduleRacingContextDisposal_ShouldRejectWithoutRetainingAnOrphanedTicket()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        NavigationGuideScheduler scheduler = context.Guides.Scheduler;
        object sync = typeof(NavigationGuideScheduler).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scheduler)!;
        using var started = new ManualResetEventSlim();
        Exception? failure = null;
        NavigationGuideRequest? request = null;
        var thread = new Thread(() =>
        {
            started.Set();
            try { context.Guides.Schedule(query, out request); }
            catch (Exception error) { failure = error; }
        });
        lock (sync)
        {
            thread.Start();
            started.Wait(TestContext.Current.CancellationToken);
            SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 10_000)
                .Should().BeTrue("the request must pass service admission and wait on the scheduler lock");
            context.Dispose();
        }
        thread.Join(10_000).Should().BeTrue();
        failure.Should().BeOfType<ObjectDisposedException>();
        request.Should().BeNull();
        scheduler.RetainedRequestCount.Should().Be(0);
        scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Fact]
    public void ConcurrentRepeatedDisposal_ShouldNotReleaseTheReplacementRequest()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            new NavigationSchedulingSettings(maxRetainedRequests: 1));
        NavigationGuideScheduler scheduler = context.Guides.Scheduler;
        object sync = typeof(NavigationGuideScheduler).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scheduler)!;
        using NavigationGuideRequest original = Schedule(context, query);
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            started.Set();
            original.Dispose();
        });
        NavigationGuideRequest replacement;
        lock (sync)
        {
            thread.Start();
            started.Wait(TestContext.Current.CancellationToken);
            SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 10_000)
                .Should().BeTrue();
            original.Dispose();
            replacement = Schedule(context, query);
        }
        thread.Join(10_000).Should().BeTrue();
        using (replacement)
        {
            original.Status.Should().Be(NavigationGuideStatus.Cancelled);
            scheduler.RetainedRequestCount.Should().Be(1);
            Complete(context, replacement);
        }
        scheduler.RetainedRequestCount.Should().Be(0);
        scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void InactiveHostWorld_ShouldInvalidateCompletedTransferWithoutNeedingAnotherFrame(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
        using NavigationGuideRequest request = Schedule(context, query);
        Complete(context, request);
        context.World.Dispose();
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Fact]
    public void InactiveHostWorld_ShouldCancelScheduledOwnershipBeforeRejectingSimulation()
    {
        using TrailblazerWorldContext context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        using NavigationGuideRequest active = Schedule(context, query);
        using NavigationGuideRequest queued = Schedule(context, query);
        context.Simulate();
        context.World.Dispose();
        Action simulate = context.Simulate;
        InvalidOperationException error = simulate.Should().Throw<InvalidOperationException>().Which;
        active.Status.Should().Be(NavigationGuideStatus.Cancelled, error.ToString());
        queued.Status.Should().Be(NavigationGuideStatus.Cancelled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NonPositiveSettings_ShouldRejectUnboundedOrNonProgressingWork(int argument)
    {
        Action action = () => new NavigationSchedulingSettings(
            maxRetainedRequests: argument == 0 ? 0 : 1,
            maxRetainedDescriptorBytes: argument == 1 ? 0 : 1,
            lookupStepLimit: argument == 2 ? 0 : 1,
            endpointCandidateStepLimit: argument == 3 ? 0 : 1,
            nodeStepLimit: argument == 4 ? 0 : 1,
            edgeStepLimit: argument == 5 ? 0 : 1,
            connectionStepLimit: argument == 6 ? 0 : 1,
            operationStepLimit: argument == 7 ? 0 : 1);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Schedule_ShouldSpreadWorkAndTransferExactSuccessfulLeaseOnce(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query,
            new NavigationSchedulingSettings(operationStepLimit: 1));
        context.Guides.Schedule(query, out NavigationGuideRequest? accepted)
            .Should().Be(NavigationGuideStatus.Pending);
        using NavigationGuideRequest request = TestRequire.NotNull(accepted);
        request.Algorithm.Should().Be(algorithm);
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
        context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.Pending);
        Complete(context, request).Should().BeGreaterThan(1);
        if (algorithm == PathAlgorithm.AStar)
        {
            request.TryTakeFlowField(out _).Should().BeFalse();
            request.TryTakeGuide(out NavigationGuideLease? result).Should().BeTrue();
            using NavigationGuideLease lease = result!.Value;
            lease.Status.Should().Be(NavigationGuideStatus.Success);
            lease.TotalCost.Should().Be((Fixed64)2);
            lease.StepCount.Should().BeGreaterThan(1);
            request.TryTakeGuide(out _).Should().BeFalse();
        }
        else
        {
            request.TryTakeGuide(out _).Should().BeFalse();
            request.TryTakeFlowField(out NavigationFlowFieldLease? result).Should().BeTrue();
            using NavigationFlowFieldLease lease = result!.Value;
            lease.Status.Should().Be(NavigationGuideStatus.Success);
            lease.OriginIntegrationCost.Should().Be((Fixed64)2);
            request.TryTakeFlowField(out _).Should().BeFalse();
        }
        request.Status.Should().Be(NavigationGuideStatus.Success);
    }

    private static int Complete(TrailblazerWorldContext context, NavigationGuideRequest request)
    {
        int frames = 0;
        for (; frames < 4096 && request.Status == NavigationGuideStatus.Pending; frames++)
            context.Simulate();
        request.Status.Should().Be(NavigationGuideStatus.Success);
        return frames;
    }

    private static NavigationGuideRequest Schedule(TrailblazerWorldContext context, PathQuery query)
    {
        context.Guides.Schedule(query, out NavigationGuideRequest? request).Should().Be(NavigationGuideStatus.Pending);
        return TestRequire.NotNull(request);
    }

    private static PathQuery WithAlgorithm(PathQuery query, PathAlgorithm algorithm) => new(
        query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal, algorithm, query.Budget, query.AllowTransitions);

    private static NavigationGuideStatus RequestSynchronously(TrailblazerWorldContext context, PathQuery query)
    {
        if (query.Algorithm == PathAlgorithm.AStar)
        {
            NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);
            lease?.Dispose();
            return status;
        }
        else
        {
            NavigationGuideStatus status = context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? lease);
            lease?.Dispose();
            return status;
        }
    }

    private static NavigationQueryLimits CohortLimits(int leases, bool reusable = true)
    {
        NavigationQueryLimits d = NavigationQueryLimits.Default;
        return new NavigationQueryLimits(d.MaxBatchItems, d.MaxBatchDescriptorBytes, d.MaxConcurrentNavigationQueries,
            d.AStarWorkspaceMapCapacity, d.AStarWorkspaceEndpointPageCapacity, d.AStarWorkspaceComponentCapacity,
            d.AStarWorkspaceNodeCapacity, reusable ? d.MaxAStarCacheEntries : 0, d.MaxAStarReusablePayloadBytes,
            d.MaxAStarSinglePayloadBytes, d.MaxAStarActivePayloadBytes, leases, d.FlowWorkspaceMapCapacity,
            d.FlowWorkspaceEndpointPageCapacity, d.FlowWorkspaceComponentCapacity, d.FlowWorkspaceNodeCapacity,
            d.RayWorkspaceCoveredAddressCapacity, d.RayWorkspaceTraceIntervalCapacity, d.AStarWorkspaceGuidePointCapacity,
            reusable ? d.MaxFlowCacheEntries : 0, d.MaxFlowReusablePayloadBytes, d.MaxFlowSinglePayloadBytes,
            d.MaxFlowActivePayloadBytes, leases);
    }

    private static TrailblazerWorldContext CreateContext(PathAlgorithm algorithm, out PathQuery query,
        NavigationSchedulingSettings? scheduling = null, int cells = 3, NavigationQueryLimits? limits = null)
    {
        var configuration = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(cells, 1, 1));
        var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var settings = new TrailblazerWorldContextSettings(defaults.OperationLimits, defaults.MaintenanceBudget,
            defaults.GuideSampleBudget, defaults.MovementGroupPadding, defaults.MaxIngressEntries,
            defaults.MaxIngressBytes, defaults.MaxActiveSnapshots, defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots, defaults.MaxRetiredSnapshotBytes, defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap, defaults.MaxDynamicCellSlots, defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies, defaults.MaxAreaRulesPerPolicy, defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases, limits ?? defaults.QueryLimits, scheduling);
        var context = TrailblazerWorldContext.Attach(world, takeOwnership: true, settings: settings);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("scheduled", binding);
        for (int i = 0; i < cells; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), GuidedPathTestScene.Cell(TraversalMedia.Solid));
        var key = new NavigationAreaPolicyKey("scheduled", 1);
        GuidedPathTestScene.PublishMapAndPolicy(context, builder.Build(), 1,
            OverlayReplacementPolicy.Clear, 1,
            new NavigationAreaPolicy(key, new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        query = new PathQuery(
            new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, new VoxelIndex(0, 0, 0)), "scheduled"),
            new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, new VoxelIndex(cells - 1, 0, 0)), "scheduled"),
            profile, key, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0), false);
        return context;
    }
}
