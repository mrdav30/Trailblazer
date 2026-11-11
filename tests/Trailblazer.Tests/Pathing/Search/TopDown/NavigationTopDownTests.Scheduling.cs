using System;
using System.Reflection;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Fact]
    public void ScheduledRoute_ExhaustedSequenceDoesNotExposeAnUnlinkedTicket()
    {
        using var context = CreateScheduledPlanarContext();
        using var retained = SchedulePlanar(context, PathAlgorithm.AStar);
        typeof(NavigationGuideScheduler).GetField("_submissionSequence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context.Guides.Scheduler, long.MaxValue);

        NavigationTopDownGuideRequest? planar = retained;
        NavigationGuideRequest? spatial = retained;
        Assert.Throws<InvalidOperationException>(() => RequestPlanar(context, PathAlgorithm.AStar, out planar));
        Assert.Throws<InvalidOperationException>(() => context.Guides.Schedule(SpatialRequest(PathAlgorithm.AStar), out spatial));
        planar.Should().BeNull();
        spatial.Should().BeNull();

        CompleteScheduled(context, retained);
        retained.TryTakeTopDownGuide(out var guide).Should().BeTrue();
        guide.Dispose();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRoute_ReplaysExactCostAndRespectsSharedFrameSlices(PathAlgorithm algorithm)
    {
        var first = ReplayScheduledRoute(algorithm);
        ReplayScheduledRoute(algorithm).Should().Be(first);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRoute_CancelledLeaderPreservesBuildAndSharesOnePayload(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext();
        using var leader = SchedulePlanar(context, algorithm);
        using var first = SchedulePlanar(context, algorithm);
        using var second = SchedulePlanar(context, algorithm);
        for (int frame = 0; frame < 64 && leader.Phase != NavigationGuideRequestPhase.Search; frame++)
            SimulateScheduled(context);
        leader.Phase.Should().Be(NavigationGuideRequestPhase.Search);
        var store = context.Pathing.NavigationGraphStore;
        store.ActiveLeaseCount.Should().Be(1);
        leader.Dispose();
        leader.Status.Should().Be(NavigationGuideStatus.Cancelled);
        store.ActiveLeaseCount.Should().Be(1);
        CompleteScheduled(context, first);
        CompleteScheduled(context, second);

        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(2);
        cache.LeasedBytes.Should().Be(NavigationPlanarGuidePayload.MeasureBytes(5));
        first.TryTakeTopDownGuide(out var firstGuide).Should().BeTrue();
        using (firstGuide)
        {
            second.TryTakeTopDownGuide(out var secondGuide).Should().BeTrue();
            using (secondGuide)
            {
                firstGuide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
                firstGuide.CurrentStepIndex.Should().Be(1);
                secondGuide.CurrentStepIndex.Should().Be(0);
                firstGuide.TotalCost.Should().Be((Fixed64)4);
                secondGuide.TotalCost.Should().Be(firstGuide.TotalCost);
            }
        }
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledRoute_RetainsUnclaimedCapacityAndRejectsStaleTransfer()
    {
        using var context = CreateScheduledPlanarContext(new NavigationSchedulingSettings(maxRetainedRequests: 1));
        using var request = SchedulePlanar(context, PathAlgorithm.FlowField);
        CompleteScheduled(context, request);
        RequestPlanar(context, PathAlgorithm.AStar, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(12, 1, 12)), out _)
            .Should().BeTrue();
        request.TryTakeTopDownGuide(out var stale).Should().BeFalse();
        stale.Status.Should().Be(NavigationGuideStatus.Stale);
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Guides.Scheduler.RetainedDescriptorBytes.Should().Be(0);
        using var retry = SchedulePlanar(context, PathAlgorithm.AStar);
        CompleteScheduled(context, retry);
    }

    [Fact]
    public void ScheduledFlowRoute_PromotesUncoveredOriginAfterServingCoveredFollower()
    {
        using var context = CreateScheduledPlanarContext();
        using var leader = SchedulePlanar(context, PathAlgorithm.FlowField);
        context.Guides.ScheduleTopDownGuide(ScheduledCell(1), ScheduledCell(4), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.FlowField, RouteBudget(), out var other)
            .Should().Be(NavigationGuideStatus.Pending);
        using var uncovered = TestRequire.NotNull(other);
        using var covered = SchedulePlanar(context, PathAlgorithm.FlowField);
        CompleteScheduled(context, leader);
        CompleteScheduled(context, covered);
        uncovered.Status.Should().Be(NavigationGuideStatus.Pending);
        CompleteScheduled(context, uncovered);
        uncovered.TryTakeTopDownGuide(out var guide).Should().BeTrue();
        using (guide)
        {
            guide.TotalCost.Should().Be((Fixed64)3);
            guide.TryGetCurrentStep(out var origin, out _).Should().Be(NavigationGuideStatus.Success);
            origin.Should().Be(ScheduledCell(1));
        }
    }

    [Fact]
    public void ScheduledRoute_SharesSpatialFifoAndCancelsAcrossReset()
    {
        using var context = CreateScheduledPlanarContext();
        using var planar = SchedulePlanar(context, PathAlgorithm.FlowField);
        PathQuery spatial = SpatialRequest(PathAlgorithm.AStar);
        context.Guides.Schedule(spatial, out NavigationGuideRequest? ticket).Should().Be(NavigationGuideStatus.Pending);
        using var later = ticket!;
        SimulateScheduled(context);
        planar.Status.Should().Be(NavigationGuideStatus.Pending);
        later.Phase.Should().Be(NavigationGuideRequestPhase.Queued);
        context.Reset();
        planar.Status.Should().Be(NavigationGuideStatus.Cancelled);
        later.Status.Should().Be(NavigationGuideStatus.Cancelled);
        planar.TryTakeTopDownGuide(out _).Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRoute_RetainsAlgorithmLaneUntilCancelled(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext();
        using var request = SchedulePlanar(context, algorithm);
        SimulateScheduled(context);
        context.Guides.RequestTopDownGuide(ScheduledCell(0), ScheduledCell(4), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, algorithm, RouteBudget(), out _)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
        request.Dispose();
        context.Guides.RequestTopDownGuide(ScheduledCell(0), ScheduledCell(4), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, algorithm, RouteBudget(), out var guide)
            .Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
    }

    [Fact]
    public void ScheduledRoute_DoubleReleaseCannotReturnAReusedSnapshot()
    {
        using var context = CreateScheduledPlanarContext();
        var gate = context.Pathing.NavigationAStarAdmissionGate;
        var query = new NavigationPlanarRouteRequest(ScheduledCell(0), ScheduledCell(4), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, RouteBudget());
        var first = TestRequire.NotNull(gate.BeginScheduledPlanar(context.World, query));
        first.Dispose();
        using var second = TestRequire.NotNull(gate.BeginScheduledPlanar(context.World, query));
        first.Dispose();
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(1);
    }

    [Fact]
    public void ScheduledRoute_StalePublicationDropsItsPartialPayloadReservation()
    {
        using var context = CreateScheduledPlanarContext();
        using var request = SchedulePlanar(context, PathAlgorithm.FlowField);
        for (int frame = 0; frame < 128 && request.Phase != NavigationGuideRequestPhase.Publication; frame++)
            SimulateScheduled(context);
        request.Phase.Should().Be(NavigationGuideRequestPhase.Publication);
        SimulateScheduled(context);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ReservedPayloadBytes.Should().Be(NavigationPlanarGuidePayload.MeasureBytes(5));
        var remove = new NavigationMapRemoveOperation("plane", 3, context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        for (int frame = 0; frame < 64 && request.Status == NavigationGuideStatus.Pending; frame++)
            SimulateScheduled(context);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("algorithm", false)]
    [InlineData("start", false)]
    [InlineData("end", false)]
    [InlineData("radius", false)]
    [InlineData("policy", false)]
    [InlineData("medium", false)]
    [InlineData("capabilities", false)]
    [InlineData("budget", false)]
    [InlineData("actions", false)]
    public void ScheduledAStarRoute_FreezesOnlyExactCompatiblePlanarIntent(string difference, bool compatible)
    {
        using var context = CreateScheduledPlanarContext(new NavigationSchedulingSettings(operationStepLimit: 1));
        using var first = SchedulePlanar(context, PathAlgorithm.AStar);
        var result = context.Guides.ScheduleTopDownGuide(
            difference == "start" ? ScheduledCell(1) : ScheduledCell(0),
            difference == "end" ? ScheduledCell(3) : ScheduledCell(4),
            difference == "radius" ? Fixed64.Half : Fixed64.Quarter,
            difference == "policy" ? new NavigationAreaPolicyKey("other", 1) : Policy.Key,
            difference == "medium" ? TraversalMedium.Gas : TraversalMedium.Solid,
            difference == "capabilities" ? TraversalCapability.Jump : TraversalCapability.None,
            difference == "algorithm" ? PathAlgorithm.FlowField : PathAlgorithm.AStar,
            difference == "budget" ? RouteBudget(lookups: 8191) : RouteBudget(),
            out var scheduled, allowTransitions: difference == "actions");
        result.Should().Be(NavigationGuideStatus.Pending);
        using var candidate = TestRequire.NotNull(scheduled);
        SimulateScheduled(context);
        SimulateScheduled(context);
        first.InCohort.Should().BeTrue();
        candidate.InCohort.Should().Be(compatible);
    }

    [Fact]
    public void ScheduledRoute_RejectsInvalidIntentsBeforeRetainingARequest()
    {
        using var context = CreateScheduledPlanarContext();
        context.Guides.ScheduleTopDownGuide(ScheduledCell(0), ScheduledCell(4), Fixed64.Zero, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.AStar, RouteBudget(), out var request)
            .Should().Be(NavigationGuideStatus.InvalidProfile);
        request.Should().BeNull();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    public void ScheduledRoute_ReportsExistingGateOwnerWithoutReleasingIt(PathAlgorithm algorithm, bool spatial)
    {
        using var context = CreateScheduledPlanarContext();
        using var request = SchedulePlanar(context, algorithm);
        var pathing = context.Pathing;
        var query = new NavigationPlanarRouteRequest(ScheduledCell(0), ScheduledCell(4), Fixed64.Quarter,
            Policy.Key, TraversalMedium.Solid, TraversalCapability.None, RouteBudget());
        IDisposable owner;
        if (!spatial)
        {
            owner = TestRequire.NotNull(algorithm == PathAlgorithm.AStar
                ? pathing.NavigationAStarAdmissionGate.BeginScheduledPlanar(context.World, query)
                : pathing.NavigationFlowAdmissionGate.BeginScheduledPlanar(context.World, query));
        }
        else if (algorithm == PathAlgorithm.AStar)
        {
            pathing.NavigationAStarAdmissionGate.Begin(SpatialRequest(algorithm), out var work)
                .Should().Be(NavigationAStarQueryStatus.Pending);
            owner = work;
        }
        else
        {
            pathing.NavigationFlowAdmissionGate.Begin(SpatialRequest(algorithm), out var work)
                .Should().Be(NavigationFlowQueryStatus.Pending);
            owner = work;
        }
        using (owner)
        {
            SimulateScheduled(context);
            request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
            pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRoute_UsesTheAlgorithmGateDescriptorLimit(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext(maxBatchDescriptorBytes: 1);
        using var request = SchedulePlanar(context, algorithm);
        SimulateScheduled(context);
        request.Status.Should().Be(NavigationGuideStatus.CapacityExceeded);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledRoute_FailsAtActivationWithoutAMaterializedStartMap()
    {
        using var context = CreateScheduledPlanarContext();
        context.Guides.ScheduleTopDownGuide(new NavigationCellAddress("missing", ScheduledCell(0).Index),
            ScheduledCell(4), Fixed64.Quarter, Policy.Key, TraversalMedium.Solid, TraversalCapability.None,
            PathAlgorithm.AStar, RouteBudget(), out var scheduled).Should().Be(NavigationGuideStatus.Pending);
        using var request = TestRequire.NotNull(scheduled);
        SimulateScheduled(context);
        request.Status.Should().Be(NavigationGuideStatus.NoMap);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledRoute_SearchStopsOnSemanticPublication()
    {
        using var context = CreateScheduledPlanarContext();
        using var request = SchedulePlanar(context, PathAlgorithm.AStar);
        for (int frame = 0; frame < 64 && request.Phase != NavigationGuideRequestPhase.Search; frame++)
            SimulateScheduled(context);
        request.Phase.Should().Be(NavigationGuideRequestPhase.Search);
        var remove = new NavigationMapRemoveOperation("plane", 3, context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        for (int frame = 0; frame < 64 && request.Status == NavigationGuideStatus.Pending; frame++)
            SimulateScheduled(context);
        request.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRoute_SlicesActionMaterializationAndLookupLimitedFlowExtraction(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext(
            new NavigationSchedulingSettings(lookupStepLimit: 1, nodeStepLimit: 1, edgeStepLimit: 1),
            jumpScene: true);
        context.Guides.ScheduleTopDownGuide(RouteCell(0, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.Jump, algorithm, RouteBudget(), out var scheduled,
            allowTransitions: true).Should().Be(NavigationGuideStatus.Pending);
        using var request = TestRequire.NotNull(scheduled);
        CompleteScheduled(context, request);
        request.TryTakeTopDownGuide(out var guide).Should().BeTrue();
        using (guide)
        {
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            guide.TryGetPendingTransition(out var action).Should().Be(NavigationGuideStatus.Success);
            action.Id.Should().Be("gap");
            guide.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
            guide.TotalCost.Should().Be((Fixed64)5 + Fixed64.Half);
        }

        if (algorithm == PathAlgorithm.FlowField)
        {
            using var lookupLimited = CreateScheduledPlanarContext(
                new NavigationSchedulingSettings(lookupStepLimit: 1, nodeStepLimit: 8));
            using var extracted = SchedulePlanar(lookupLimited, PathAlgorithm.FlowField);
            CompleteScheduled(lookupLimited, extracted);
        }
    }

    [Fact]
    public void ScheduledRoute_CohortPinRejectsMissingRecycledAndStaleSources()
    {
        using var context = CreateScheduledPlanarContext();
        var world = context.World;
        var store = context.Pathing.NavigationGraphStore;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var origin = ScheduledCell(0);
        using var source = new NavigationCohortSource();
        cache.TryCheckoutPlanarGuide(store, source, origin, out _).Should().Be(NavigationGuideStatus.Stale);
        var payload = new NavigationPlanarGuidePayload(
            new[] { new NavigationAStarGuidePoint(origin, Vector3d.Zero, TraversalMedium.Solid) },
            Fixed64.Zero, Policy.Key, store.Current.GraphVersion, (world.SpawnToken, world.Version, world.ChangeSequence));
        var first = PublishPin();
        source.AStar = first;
        first.Dispose();
        using var reused = PublishPin();
        reused.Should().BeSameAs(source.AStar);
        cache.TryCheckoutPlanarGuide(store, source, origin, out _).Should().Be(NavigationGuideStatus.Stale);
        source.AStar = reused;
        store.MarkSafetyPending();
        cache.TryCheckoutPlanarGuide(store, source, origin, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        store.ClearSafetyPending();
        context.Pathing.Reset();
        cache.TryCheckoutPlanarGuide(store, source, origin, out _).Should().Be(NavigationGuideStatus.Stale);
        source.AStar = null;

        NavigationAStarPayloadLease PublishPin()
        {
            cache.TryReservePayload(payload.RetainedBytes, out var reservation).Should().BeTrue();
            cache.PublishPlanarPayload(store, payload, ref reservation, out var lease)
                .Should().Be(NavigationGuideStatus.Success);
            return TestRequire.NotNull(lease);
        }
    }

    private static (int Frames, int Endpoints, int Nodes, int Edges) ReplayScheduledRoute(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext();
        using var request = SchedulePlanar(context, algorithm);
        request.TryTakeTopDownGuide(out _).Should().BeFalse();
        int frames = 0;
        int endpoints = 0;
        int nodes = 0;
        int edges = 0;
        int publicationFrames = 0;
        while (request.Status == NavigationGuideStatus.Pending && frames++ < 1024)
        {
            bool publishing = request.Phase == NavigationGuideRequestPhase.Publication;
            SimulateScheduled(context);
            if (publishing)
                publicationFrames++;
            var work = context.Guides.Scheduler.LastFrameCountedWork;
            work.LookupRemaining.Should().BeInRange(0, 1);
            work.EndpointCandidateRemaining.Should().BeInRange(0, 1);
            work.NodeRemaining.Should().BeInRange(0, 1);
            work.EdgeRemaining.Should().BeInRange(0, 1);
            endpoints += work.EndpointCandidateRemaining;
            nodes += work.NodeRemaining;
            edges += work.EdgeRemaining;
        }
        request.Status.Should().Be(NavigationGuideStatus.Success);
        endpoints.Should().Be(2);
        publicationFrames.Should().BeGreaterThanOrEqualTo(4);
        request.TryTakeTopDownGuide(out var guide).Should().BeTrue();
        using (guide)
        {
            guide.StepCount.Should().Be(5);
            guide.TotalCost.Should().Be((Fixed64)4);
            for (int x = 0; x < 5; x++)
            {
                guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
                address.Should().Be(ScheduledCell(x));
                center.Should().Be(new Vector2d(x, 0));
                if (x != 4)
                    guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            }
        }
        request.TryTakeTopDownGuide(out _).Should().BeFalse();
        return (frames, endpoints, nodes, edges);
    }

    private static NavigationGuideStatus RequestPlanar(TrailblazerWorldContext context, PathAlgorithm algorithm,
        out NavigationTopDownGuideRequest? request) =>
        context.Guides.ScheduleTopDownGuide(ScheduledCell(0), ScheduledCell(4), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, algorithm, RouteBudget(), out request);

    private static NavigationTopDownGuideRequest SchedulePlanar(TrailblazerWorldContext context, PathAlgorithm algorithm)
    {
        RequestPlanar(context, algorithm, out var request).Should().Be(NavigationGuideStatus.Pending);
        return TestRequire.NotNull(request);
    }

    private static NavigationCellAddress ScheduledCell(int x) => new("plane", new VoxelIndex(x, 1, 0));

    private static void CompleteScheduled(TrailblazerWorldContext context, NavigationTopDownGuideRequest request)
    {
        for (int frame = 0; frame < 1024 && request.Status == NavigationGuideStatus.Pending; frame++)
            SimulateScheduled(context);
        request.Status.Should().Be(NavigationGuideStatus.Success);
    }

    private static void SimulateScheduled(TrailblazerWorldContext context)
    {
        context.Simulate();
        context.LateSimulate();
    }

    private static PathQuery SpatialRequest(PathAlgorithm algorithm)
    {
        var profile = new NavigationAgentProfile(new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        return new PathQuery(
            new NavigationEndpoint(Vector3d.Zero, "spatial"), new NavigationEndpoint(Vector3d.Zero, "spatial"),
            profile, Policy.Key, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            algorithm, RouteBudget(), false);
    }

    private static TrailblazerWorldContext CreateScheduledPlanarContext(NavigationSchedulingSettings? scheduling = null,
        long? maxBatchDescriptorBytes = null, bool jumpScene = false)
    {
        var settings = PlanarSchedulingTestSettings.Create(scheduling, maxBatchDescriptorBytes);
        var context = TrailblazerWorldContext.CreateOwned(settings: settings);
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        if (jumpScene)
            BuildPlanarJump(context);
        else
        {
            var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(4, 1, 0));
            context.World.TryAddGrid(grid, out _).Should().BeTrue();
            Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        }
        return context;
    }
}
