//=======================================================================
// NavigationFlowAdmissionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationFlowAdmissionTests
{
    [Fact]
    public void WarmCacheHitAdmissionAndPublication_ShouldAllocateZeroBytes()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-warm-admission");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        NavigationQueryLimits limits = CreateLimits(
            maxBatchItems: 1,
            maxConcurrentQueries: 1,
            maxFlowActivePayloadLeases: 1);
        using var gate = new NavigationFlowAdmissionGate(
            world,
            store,
            limits,
            new NavigationQueryAdmissionCoordinator(1));
        RunSuccessfulQuery(gate, query);
        RunSuccessfulQuery(gate, query);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunSuccessfulQuery(gate, query);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }

    [Fact]
    public void ContextReset_ShouldDrainActiveFlowAdmissionBeforeGraphReset()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationFlowAdmissionGate gate = context.Pathing.NavigationFlowAdmissionGate;
        PathQuery query = new(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.One),
            new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
                maxStepUp: Fixed64.Zero,
                maxDropDown: Fixed64.Zero,
                arrivalRadius: Fixed64.Zero,
                allowedMedia: TraversalMedia.Solid,
                capabilities: TraversalCapability.None),
            new NavigationAreaPolicyKey("flow-reset", 1),
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(
                maxLookupProbes: 8,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 2,
                maxEvaluatedEdges: 4,
                maxConnectionLegs: 0,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));

        gate.Begin(query, out NavigationFlowBatchWork work)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(1);

        context.Reset();

        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        Action readReleased = () => work.GetStatus(0);
        readReleased.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Batch_ShouldPublishInStableOrderAndRejectReleasedAlias()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-publication");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, maxConcurrentLeases: 2);
        NavigationQueryLimits limits = CreateLimits(
            maxBatchItems: 2,
            maxConcurrentQueries: 2,
            maxFlowActivePayloadLeases: 2);
        var coordinator = new NavigationQueryAdmissionCoordinator(2);
        using var gate = new NavigationFlowAdmissionGate(
            world,
            store,
            limits,
            coordinator);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(20, query),
                new PathQueryBatchItem(10, query)
            },
            count: 2);

        gate.Begin(batch, out NavigationFlowBatchWork work)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        DrainAdmission(work);
        DrainSearch(work, inputIndex: 0);
        work.PublishReadyPrefix(maximumCount: 2).Should().Be(0,
            "the later stable ordinal cannot publish ahead of the prefix");
        DrainSearch(work, inputIndex: 1);
        work.PublishReadyPrefix(maximumCount: 2).Should().Be(2);
        work.GetStatus(0).Should().Be(NavigationFlowQueryStatus.Success);
        work.GetStatus(1).Should().Be(NavigationFlowQueryStatus.Success);
        work.TakeResult(0).Dispose();
        work.TakeResult(1).Dispose();

        NavigationFlowBatchWork stale = work;
        work.Dispose();
        gate.Begin(query, out NavigationFlowBatchWork replacement)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        stale.Dispose();
        Action readStale = () => stale.GetStatus(0);
        readStale.Should().Throw<ObjectDisposedException>();
        replacement.AdmittedCount.Should().Be(1,
            "a stale release cannot cancel a replacement generation");
        replacement.Dispose();
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Begin_ShouldRejectZeroAndExactDescriptorOverflowBeforeResources()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-envelope");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        long exactBytes = PathQueryBatchItem.GetLogicalRetainedBytes(query);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var coordinator = new NavigationQueryAdmissionCoordinator(1);
        using var rejectedGate = new NavigationFlowAdmissionGate(
            world,
            store,
            CreateLimits(
                maxBatchItems: 1,
                maxConcurrentQueries: 1,
                maxBatchDescriptorBytes: exactBytes - 1),
            coordinator);

        rejectedGate.Begin(
                new PathQueryBatch(Array.Empty<PathQueryBatchItem>(), 0),
                out NavigationFlowBatchWork empty)
            .Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        empty.Should().Be(default(NavigationFlowBatchWork));
        rejectedGate.Begin(query, out NavigationFlowBatchWork overflow)
            .Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        overflow.Should().Be(default(NavigationFlowBatchWork));
        coordinator.ActiveCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);

        using var acceptedGate = new NavigationFlowAdmissionGate(
            world,
            store,
            CreateLimits(
                maxBatchItems: 1,
                maxConcurrentQueries: 1,
                maxBatchDescriptorBytes: exactBytes),
            coordinator);
        acceptedGate.Begin(query, out NavigationFlowBatchWork accepted)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        accepted.Dispose();
    }

    [Fact]
    public void WorkspaceExhaustion_ShouldRemainAnAdmittedTerminalResult()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-workspace-capacity");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        NavigationQueryLimits limits = CreateLimits(
            maxBatchItems: 1,
            maxConcurrentQueries: 1,
            flowWorkspaceNodeCapacity: 0,
            maxFlowActivePayloadLeases: 1);
        var coordinator = new NavigationQueryAdmissionCoordinator(1);
        using var gate = new NavigationFlowAdmissionGate(
            world,
            store,
            limits,
            coordinator);

        gate.Begin(query, out NavigationFlowBatchWork work)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        DrainAdmission(work);

        work.AdmittedCount.Should().Be(1);
        work.IsReadyToPublish(0).Should().BeTrue();
        work.PublishReadyPrefix(1).Should().Be(1);
        work.GetStatus(0).Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        work.Dispose();
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void SharedCoordinator_ShouldBoundConcreteAStarAndFlowGates()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "mixed-gates");
        PathQuery aStar = fixture.CreateQuery(
            origin,
            destination,
            fixture.DefaultProfile);
        PathQuery flow = ToFlowField(aStar);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, maxConcurrentLeases: 2);
        NavigationQueryLimits limits = CreateLimits(
            maxBatchItems: 1,
            maxConcurrentQueries: 1,
            maxFlowActivePayloadLeases: 1);
        var coordinator = new NavigationQueryAdmissionCoordinator(1);
        using var aStarGate = new NavigationAStarAdmissionGate(
            world,
            store,
            limits,
            coordinator);
        using var flowGate = new NavigationFlowAdmissionGate(
            world,
            store,
            limits,
            coordinator);

        aStarGate.Begin(aStar, out NavigationAStarBatchWork aStarWork)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        flowGate.Begin(flow, out NavigationFlowBatchWork flowWork)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        flowWork.AdmittedCount.Should().Be(0);
        flowWork.GetStatus(0).Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        flowWork.Dispose();
        aStarWork.Dispose();
        coordinator.ActiveCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Batch_ShouldCutStableSuffixAtFirstReservationFailure(bool byteBound)
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-batch");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, maxConcurrentLeases: 3);
        long maximumPayloadBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            nodeCount: 2,
            componentCount: 1,
            pageCount: 1);
        NavigationQueryLimits limits = CreateLimits(
            maxBatchItems: 3,
            maxConcurrentQueries: 3,
            maxFlowActivePayloadLeases: byteBound ? 3 : 1,
            maxFlowSinglePayloadBytes: byteBound ? maximumPayloadBytes : 2_048,
            maxFlowActivePayloadBytes: byteBound ? maximumPayloadBytes : 8_192);
        var coordinator = new NavigationQueryAdmissionCoordinator(3);
        using var gate = new NavigationFlowAdmissionGate(
            world,
            store,
            limits,
            coordinator);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(30, query),
                new PathQueryBatchItem(10, query),
                new PathQueryBatchItem(20, query)
            },
            count: 3);

        gate.Begin(batch, out NavigationFlowBatchWork work)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        Action searchBeforeBarrier = () => work.AdvanceSearch(
            inputIndex: 1,
            lookupStepLimit: 1,
            nodeStepLimit: 1,
            edgeStepLimit: 1,
            connectionStepLimit: 0);
        searchBeforeBarrier.Should().Throw<InvalidOperationException>();
        Action publishBeforeBarrier = () => work.PublishReadyPrefix(1);
        publishBeforeBarrier.Should().Throw<InvalidOperationException>();
        for (int step = 0; step < 64 && !work.IsAdmissionComplete; step++)
            work.AdvanceAdmission(lookupStepLimit: 4, endpointCandidateStepLimit: 2);

        work.IsAdmissionComplete.Should().BeTrue();
        work.AdmittedCount.Should().Be(1);
        work.GetStatus(inputIndex: 1).Should().Be(NavigationFlowQueryStatus.Pending);
        work.GetStatus(inputIndex: 2).Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        work.GetStatus(inputIndex: 0).Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        gate.PayloadCache.ReservedLeaseCount.Should().Be(1);
        store.ActiveLeaseCount.Should().Be(1);
        coordinator.ActiveCount.Should().Be(1);

        work.Dispose();
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void QueryWork_ShouldPublishLeaseWithExactResolvedOrigin()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-query-work");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var workspace = new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 2,
            maxReusableBytes: 4_096,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
        using var work = new NavigationFlowQueryWork(
            world,
            store,
            workspace,
            cache);
        work.Begin(query, store.TryAcquire()!);

        for (int step = 0; step < 32 && !work.IsPrepared; step++)
            work.PrepareSearchOrCheckout(lookupStepLimit: 4, endpointCandidateStepLimit: 2);

        work.IsPrepared.Should().BeTrue();
        cache.ReservedLeaseCount.Should().Be(1);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
        {
            work.AdvanceSearch(
                lookupStepLimit: 8,
                nodeStepLimit: 4,
                edgeStepLimit: 8,
                connectionStepLimit: 0);
        }

        work.Publish().Should().Be(NavigationFlowQueryStatus.Success);
        using NavigationFlowQueryResult result = work.TakeResult();
        result.ResolvedOrigin.Should().Be(
            new NavigationCellAddress(fixture.MapId, origin));
        result.PayloadLease.TryGetPayload(out NavigationFlowFieldPayload payload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        payload.TryGetNode(result.ResolvedOrigin, out _).Should().BeTrue();
        result.Dispose();
        work.Dispose();

        var warmWorkspace = new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2);
        using var warm = new NavigationFlowQueryWork(
            world,
            store,
            warmWorkspace,
            cache);
        warm.Begin(query, store.TryAcquire()!);
        for (int step = 0; step < 32 && !warm.IsPrepared; step++)
            warm.PrepareSearchOrCheckout(lookupStepLimit: 4, endpointCandidateStepLimit: 2);

        warm.IsReadyToPublish.Should().BeTrue();
        cache.ReservedLeaseCount.Should().Be(0,
            "a covered cache checkout precedes worst-case payload reservation");
        cache.ActiveLeaseCount.Should().Be(1);
        warm.Publish().Should().Be(NavigationFlowQueryStatus.Success);
        using NavigationFlowQueryResult warmResult = warm.TakeResult();
        warmResult.ResolvedOrigin.Should().Be(result.ResolvedOrigin);
    }

    [Fact]
    public void SharedEndpointWorkspace_ShouldResolveAFlowQuery()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-admission");
        PathQuery source = fixture.CreateQuery(
            origin,
            destination,
            fixture.DefaultProfile);
        var query = new PathQuery(
            source.Start,
            source.End,
            source.Agent,
            source.AreaPolicy,
            source.Traversal,
            PathAlgorithm.FlowField,
            source.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var workspace = new NavigationFlowFieldWorkspace(
            mapCapacity: 1,
            dependencyPageCapacity: 1,
            dependencyComponentCapacity: 1,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 2,
            rayTraceIntervalCapacity: 2);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store.TryAcquire()!,
            query,
            workspace.EndpointWorkspace,
            PathAlgorithm.FlowField);

        for (int step = 0;
            step < 32 && admission.Status == NavigationQueryAdmissionStatus.Pending;
            step++)
        {
            admission.Advance(lookupStepLimit: 4, endpointCandidateStepLimit: 2);
        }

        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        admission.Result.Start.Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, origin));
        admission.Result.End.Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));
    }

    [Fact]
    public void AggregateCoordinator_ShouldBoundMixedPrefixesAndRejectStaleRelease()
    {
        var coordinator = new NavigationQueryAdmissionCoordinator(3);

        coordinator.TryReservePrefix(
                PathAlgorithm.AStar,
                requestedCount: 2,
                out NavigationQueryCapacityReservation aStar)
            .Should().Be(2);
        coordinator.TryReservePrefix(
                PathAlgorithm.FlowField,
                requestedCount: 2,
                out NavigationQueryCapacityReservation flow)
            .Should().Be(1);
        coordinator.ActiveCount.Should().Be(3);

        NavigationQueryCapacityReservation staleFlow = flow;
        flow = coordinator.Trim(flow, retainedCount: 0);
        coordinator.ActiveCount.Should().Be(2);
        coordinator.Release(flow);
        coordinator.TryReservePrefix(
                PathAlgorithm.FlowField,
                requestedCount: 1,
                out NavigationQueryCapacityReservation replacement)
            .Should().Be(1);
        coordinator.Release(staleFlow);
        coordinator.ActiveCount.Should().Be(3,
            "a stale trimmed reservation cannot release a rebound lane");

        coordinator.Release(aStar);
        coordinator.Release(replacement);
        coordinator.ActiveCount.Should().Be(0);
    }

    private static PathQuery ToFlowField(PathQuery source) => new(
        source.Start,
        source.End,
        source.Agent,
        source.AreaPolicy,
        source.Traversal,
        PathAlgorithm.FlowField,
        source.Budget,
        allowTransitions: false,
        new FlowFieldQueryOptions(Fixed64.Zero));

    private static void DrainAdmission(NavigationFlowBatchWork work)
    {
        for (int step = 0; step < 64 && !work.IsAdmissionComplete; step++)
            work.AdvanceAdmission(lookupStepLimit: 4, endpointCandidateStepLimit: 2);
        work.IsAdmissionComplete.Should().BeTrue();
    }

    private static void DrainSearch(NavigationFlowBatchWork work, int inputIndex)
    {
        for (int step = 0; step < 64 && !work.IsReadyToPublish(inputIndex); step++)
        {
            work.AdvanceSearch(
                inputIndex,
                lookupStepLimit: 8,
                nodeStepLimit: 4,
                edgeStepLimit: 8,
                connectionStepLimit: 0);
        }
        work.IsReadyToPublish(inputIndex).Should().BeTrue();
    }

    private static void RunSuccessfulQuery(
        NavigationFlowAdmissionGate gate,
        PathQuery query)
    {
        if (gate.Begin(query, out NavigationFlowBatchWork work)
            != NavigationFlowQueryStatus.Pending)
        {
            throw new InvalidOperationException("Flow admission failed.");
        }
        for (int step = 0; step < 64 && !work.IsAdmissionComplete; step++)
            work.AdvanceAdmission(lookupStepLimit: 4, endpointCandidateStepLimit: 2);
        for (int step = 0; step < 64 && !work.IsReadyToPublish(0); step++)
        {
            work.AdvanceSearch(
                inputIndex: 0,
                lookupStepLimit: 8,
                nodeStepLimit: 4,
                edgeStepLimit: 8,
                connectionStepLimit: 0);
        }
        if (!work.IsAdmissionComplete
            || work.PublishReadyPrefix(1) != 1
            || work.GetStatus(0) != NavigationFlowQueryStatus.Success)
        {
            work.Dispose();
            throw new InvalidOperationException("Flow query did not complete successfully.");
        }
        work.TakeResult(0).Dispose();
        work.Dispose();
    }

    private static NavigationQueryLimits CreateLimits(
        int maxBatchItems = 4,
        int maxConcurrentQueries = 4,
        long maxBatchDescriptorBytes = 16_384,
        int maxFlowActivePayloadLeases = 4,
        int flowWorkspaceNodeCapacity = 2,
        long maxFlowSinglePayloadBytes = 2_048,
        long maxFlowActivePayloadBytes = 8_192) => new(
            maxBatchItems,
            maxBatchDescriptorBytes,
            maxConcurrentNavigationQueries: maxConcurrentQueries,
            aStarWorkspaceMapCapacity: 1,
            aStarWorkspaceEndpointPageCapacity: 1,
            aStarWorkspaceComponentCapacity: 1,
            aStarWorkspaceNodeCapacity: 2,
            maxAStarCacheEntries: 4,
            maxAStarReusablePayloadBytes: 8_192,
            maxAStarSinglePayloadBytes: 2_048,
            maxAStarActivePayloadBytes: 8_192,
            maxAStarActivePayloadLeases: 4,
            flowWorkspaceMapCapacity: 1,
            flowWorkspaceEndpointPageCapacity: 1,
            flowWorkspaceComponentCapacity: 1,
            flowWorkspaceNodeCapacity,
            rayWorkspaceCoveredAddressCapacity: Math.Max(1, flowWorkspaceNodeCapacity),
            rayWorkspaceTraceIntervalCapacity: Math.Max(1, flowWorkspaceNodeCapacity),
            aStarWorkspaceGuidePointCapacity: 2,
            maxFlowCacheEntries: 4,
            maxFlowReusablePayloadBytes: 8_192,
            maxFlowSinglePayloadBytes,
            maxFlowActivePayloadBytes,
            maxFlowActivePayloadLeases: maxFlowActivePayloadLeases);
}
