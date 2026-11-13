using System.Runtime.CompilerServices;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrivalRegionUsesTheAuthoredSupportEvenWhenTheDestinationIsBlocked(bool representableMidpoint)
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        var goal = Cell(2);
        var left = new Vector2d(Fixed64.One + Fixed64.Half, -Fixed64.Half);
        var right = representableMidpoint
            ? new Vector2d(Fixed64.Two, -Fixed64.Half)
            : left + new Vector2d(Fixed64.MinIncrement, Fixed64.FromRaw(2));
        builder.AddPlanarSupport(goal.Index, left, right);
        Publish(context, builder.Build());

        // The requested cell center is one unit from x=1. Its authored anchor is only 0.75 units away.
        var status = context.Guides.RequestSideViewGuide(Cell(0), goal, Profile(Fixed64.Half), Policy.Key,
            PathAlgorithm.AStar, GuideBudget(candidates: 32), out var guide,
            arrivalRadius: Fixed64.Half + Fixed64.Quarter);
        using (guide)
        {
            status.Should().Be(representableMidpoint ? NavigationGuideStatus.Success : NavigationGuideStatus.InvalidEnd);
            if (!representableMidpoint)
                return;
            guide.StepCount.Should().Be(2);
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            guide.TryGetCurrentStep(out var terminal, out _).Should().Be(NavigationGuideStatus.Success);
            terminal.Should().Be(Cell(1));
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void ArrivalRegionSkipsTheDisconnectedApproachToABlockedCenter(PathAlgorithm algorithm, bool scheduled)
    {
        using var context = Scene(out _, settings: PlanarSchedulingTestSettings.Create());
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        builder.AddCell(Cell(3).Index, Solid).AddPlanarSupport(Cell(3).Index,
            new Vector2d(Fixed64.Two + Fixed64.Half, -Fixed64.Half), new Vector2d(Fixed64.Three, -Fixed64.Half));
        // The x=2 center is blocked. Its closer x=2.75 approach is isolated from x=0.
        Publish(context, builder.Build());
        var profile = Profile(Fixed64.Half);
        var start = Cell(0);
        var destination = Cell(2);
        var radius = Fixed64.One;
        var budget = GuideBudget(candidates: 32);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => context.Guides.RequestSideViewGuide(start, destination,
            profile, Policy.Key, algorithm, budget, out _, arrivalRadius: -Fixed64.MinIncrement));
        context.Guides.RequestSideViewGuide(start, destination, profile, Policy.Key, algorithm, budget, out _)
            .Should().Be(NavigationGuideStatus.InvalidEnd);

        NavigationSideViewGuideLease guide;
        if (scheduled)
        {
            context.Guides.ScheduleSideViewGuide(start, destination, profile, Policy.Key, algorithm, budget,
                out var pending, arrivalRadius: radius).Should().Be(NavigationGuideStatus.Pending);
            using var request = TestRequire.NotNull(pending);
            RunScheduled(context, request).Should().Be(NavigationGuideStatus.Success);
            request.TryTakeSideViewGuide(out guide).Should().BeTrue();
        }
        else
        {
            context.Guides.RequestSideViewGuide(start, destination, profile, Policy.Key, algorithm, budget,
                out guide, arrivalRadius: radius).Should().Be(NavigationGuideStatus.Success);
        }
        using (guide)
        {
            guide.StepCount.Should().Be(2);
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(Cell(1));
            center.Should().Be(Vector2d.Right);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledGuidePublishesExactStepAtTheImmediateLookupBoundary(PathAlgorithm algorithm)
    {
        using var context = ScheduledStepScene();
        var profile = Profile(Fixed64.Half, up: Fixed64.Quarter);
        int lookups = 1;
        for (; lookups < 128; lookups++)
        {
            var status = RequestSideView(context, Cell(0), Cell(1), profile, algorithm,
                out var acquired, GuideBudget(lookups: lookups));
            using var immediate = acquired;
            if (status == NavigationGuideStatus.Success)
                break;
            status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        }
        lookups.Should().BeLessThan(128);
        using (var shortRequest = ScheduleStep(context, algorithm, profile, GuideBudget(lookups: lookups - 1)))
        {
            RunScheduled(context, shortRequest).Should().Be(NavigationGuideStatus.BudgetExceeded);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ReservedPayloadBytes.Should().Be(0);
        }
        using var request = ScheduleStep(context, algorithm, profile, GuideBudget(lookups: lookups));
        int publicationFrames = 0;
        for (int frame = 0; frame < 128 && request.Status == NavigationGuideStatus.Pending; frame++)
        {
            if (request.Phase == NavigationGuideRequestPhase.Publication)
                publicationFrames++;
            TickScheduledSideView(context);
            var work = context.Guides.Scheduler.LastFrameCountedWork;
            work.LookupRemaining.Should().BeInRange(0, 1);
            work.NodeRemaining.Should().BeInRange(0, 1);
        }
        request.Status.Should().Be(NavigationGuideStatus.Success);
        publicationFrames.Should().BeGreaterThanOrEqualTo(3);
        request.TryTakeGuide(out _).Should().BeFalse();
        request.TryTakeFlowField(out _).Should().BeFalse();
        request.TryTakeSideViewGuide(out var result).Should().BeTrue();
        using var guide = result;
        request.TryTakeSideViewGuide(out _).Should().BeFalse();
        guide.TotalCost.Should().Be(Fixed64.One + Fixed64.Quarter);
        guide.StepCount.Should().Be(3);
        for (int step = 0; step < 3; step++)
        {
            guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(Cell(step == 2 ? 1 : 0));
            center.Should().Be(new Vector2d(step == 2 ? Fixed64.One : Fixed64.Zero,
                step == 0 ? Fixed64.Zero : Fixed64.Quarter));
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledCohortSharesExactProfilesAndRejectsAnIncompatibleStepLimit(PathAlgorithm algorithm)
    {
        using var context = ScheduledStepScene();
        var climber = Profile(Fixed64.Half, up: Fixed64.Quarter);
        using var leader = ScheduleStep(context, algorithm, climber);
        using var follower = ScheduleStep(context, algorithm, climber);
        using var restricted = ScheduleStep(context, algorithm, Profile(Fixed64.Half));
        RunScheduled(context, follower).Should().Be(NavigationGuideStatus.Success);
        leader.Status.Should().Be(NavigationGuideStatus.Success);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(2);
        cache.LeasedBytes.Should().Be(NavigationPlanarGuidePayload.MeasureBytes(3));
        RunScheduled(context, restricted).Should().Be(NavigationGuideStatus.NoPath);
        leader.TryTakeSideViewGuide(out var first).Should().BeTrue();
        using var firstGuide = first;
        follower.TryTakeSideViewGuide(out var second).Should().BeTrue();
        using var secondGuide = second;
        firstGuide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        secondGuide.CurrentStepIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScheduledPartialStepReleasesItsReservationOnCancellationOrStaleness(bool stale)
    {
        using var context = ScheduledStepScene();
        using var request = ScheduleStep(context, PathAlgorithm.FlowField, Profile(Fixed64.Half, up: Fixed64.Quarter));
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        for (int frame = 0; frame < 128 && cache.ReservedPayloadBytes == 0; frame++)
            TickScheduledSideView(context);
        cache.ReservedPayloadBytes.Should().Be(NavigationPlanarGuidePayload.MeasureBytes(3));
        request.Phase.Should().Be(NavigationGuideRequestPhase.Publication);
        if (stale)
        {
            var remove = new NavigationMapRemoveOperation("side", 3, context.FrameCount + 1);
            context.Pathing.Admit(remove).Should().BeTrue();
            RunScheduled(context, request).Should().Be(NavigationGuideStatus.Stale);
            remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        else
        {
            request.Dispose();
            request.Status.Should().Be(NavigationGuideStatus.Cancelled);
        }
        request.TryTakeSideViewGuide(out _).Should().BeFalse();
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledFlowServesACoveredFollowerBeforePromotingAnotherOrigin()
    {
        using var context = ScheduledStepScene();
        var profile = Profile(Fixed64.Half, up: Fixed64.Quarter);
        using var leader = ScheduleStep(context, PathAlgorithm.FlowField, profile);
        context.Guides.ScheduleSideViewGuide(Cell(1), Cell(1), profile, Policy.Key, PathAlgorithm.FlowField,
            GuideBudget(), out var queued).Should().Be(NavigationGuideStatus.Pending);
        using var uncovered = TestRequire.NotNull(queued);
        using var covered = ScheduleStep(context, PathAlgorithm.FlowField, profile);
        RunScheduled(context, covered).Should().Be(NavigationGuideStatus.Success);
        uncovered.Status.Should().Be(NavigationGuideStatus.Pending);
        RunScheduled(context, uncovered).Should().Be(NavigationGuideStatus.Success);
        uncovered.TryTakeSideViewGuide(out var acquired).Should().BeTrue();
        using var guide = acquired;
        guide.StepCount.Should().Be(1);
        guide.TotalCost.Should().Be(Fixed64.Zero);
        guide.TryGetCurrentStep(out var owner, out _).Should().Be(NavigationGuideStatus.Success);
        owner.Should().Be(Cell(1));
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledSideViewChargesTheProfileToBothDescriptorLimits(PathAlgorithm algorithm)
    {
        var profile = Profile(Fixed64.Half, up: Fixed64.Quarter);
        var normalized = new NavigationPlanarRouteRequest(Cell(0), Cell(1), profile.Radius, Policy.Key,
            TraversalMedium.Solid, profile.Capabilities, GuideBudget());
        long bytes = normalized.GetLogicalRetainedBytes() + Unsafe.SizeOf<NavigationAgentProfile2D>();
        using (var queue = ScheduledStepScene(new NavigationSchedulingSettings(maxRetainedDescriptorBytes: bytes - 1)))
        {
            queue.Guides.ScheduleSideViewGuide(Cell(0), Cell(1), profile, Policy.Key, algorithm, GuideBudget(),
                out var rejected).Should().Be(NavigationGuideStatus.CapacityExceeded);
            rejected.Should().BeNull();
            queue.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
        }
        using var gate = ScheduledStepScene(new NavigationSchedulingSettings(maxRetainedDescriptorBytes: bytes),
            maxBatchDescriptorBytes: bytes - 1);
        using var request = ScheduleStep(gate, algorithm, profile);
        gate.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(bytes);
        RunScheduled(gate, request).Should().Be(NavigationGuideStatus.CapacityExceeded);
        request.Dispose();
        gate.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
    }

    [Fact]
    public void ScheduledSideViewRejectsInvalidIntentAndRevalidatesAnUnclaimedResult()
    {
        using var context = ScheduledStepScene();
        context.Guides.ScheduleSideViewGuide(Cell(0), Cell(1), default, Policy.Key, PathAlgorithm.AStar,
            GuideBudget(), out var invalid).Should().Be(NavigationGuideStatus.InvalidProfile);
        invalid.Should().BeNull();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        using var request = ScheduleStep(context, PathAlgorithm.AStar, Profile(Fixed64.Half, up: Fixed64.Quarter));
        RunScheduled(context, request).Should().Be(NavigationGuideStatus.Success);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
            .Should().BeTrue();
        request.TryTakeSideViewGuide(out _).Should().BeFalse();
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    private static TrailblazerWorldContext ScheduledStepScene(NavigationSchedulingSettings? scheduling = null,
        long? maxBatchDescriptorBytes = null)
    {
        var context = Scene(out _, settings: PlanarSchedulingTestSettings.Create(scheduling, maxBatchDescriptorBytes));
        Publish(context, BuildGuideStep(Fixed64.Zero));
        return context;
    }

    private static NavigationSideViewGuideRequest ScheduleStep(TrailblazerWorldContext context, PathAlgorithm algorithm,
        in NavigationAgentProfile2D profile, NavigationWorkBudget? budget = null)
    {
        context.Guides.ScheduleSideViewGuide(Cell(0), Cell(1), profile, Policy.Key, algorithm, budget ?? GuideBudget(),
            out var request).Should().Be(NavigationGuideStatus.Pending);
        return TestRequire.NotNull(request);
    }

    private static NavigationGuideStatus RunScheduled(TrailblazerWorldContext context, NavigationSideViewGuideRequest request)
    {
        for (int frame = 0; frame < 128 && request.Status == NavigationGuideStatus.Pending; frame++)
            TickScheduledSideView(context);
        return request.Status;
    }

    private static void TickScheduledSideView(TrailblazerWorldContext context)
    {
        context.Simulate();
        context.LateSimulate();
    }
}
