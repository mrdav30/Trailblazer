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
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
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
    public void NonPositiveSettings_ShouldRejectUnboundedOrNonProgressingWork(int argument)
    {
        Action action = () => new NavigationSchedulingSettings(
            maxRetainedRequests: argument == 0 ? 0 : 1,
            maxRetainedDescriptorBytes: argument == 1 ? 0 : 1,
            lookupStepLimit: argument == 2 ? 0 : 1,
            endpointCandidateStepLimit: argument == 3 ? 0 : 1,
            nodeStepLimit: argument == 4 ? 0 : 1,
            edgeStepLimit: argument == 5 ? 0 : 1,
            connectionStepLimit: argument == 6 ? 0 : 1);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Schedule_ShouldSpreadWorkAndTransferExactSuccessfulLeaseOnce(PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query);
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

    private static TrailblazerWorldContext CreateContext(PathAlgorithm algorithm, out PathQuery query,
        NavigationSchedulingSettings? scheduling = null)
    {
        var configuration = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 1, 1));
        var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var settings = new TrailblazerWorldContextSettings(defaults.OperationLimits, defaults.MaintenanceBudget,
            defaults.GuideSampleBudget, defaults.MovementGroupPadding, defaults.MaxIngressEntries,
            defaults.MaxIngressBytes, defaults.MaxActiveSnapshots, defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots, defaults.MaxRetiredSnapshotBytes, defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap, defaults.MaxDynamicCellSlots, defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies, defaults.MaxAreaRulesPerPolicy, defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases, defaults.QueryLimits, scheduling);
        var context = TrailblazerWorldContext.Attach(world, takeOwnership: true, settings: settings);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("scheduled", binding);
        for (int i = 0; i < 3; i++)
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
            new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, new VoxelIndex(2, 0, 0)), "scheduled"),
            profile, key, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0), false);
        return context;
    }
}
