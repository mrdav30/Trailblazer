//=======================================================================
// NavigationFlowAdmissionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationFlowAdmissionTests
{
    [Fact]
    public void DefaultLimits_ShouldCoverTheExactTransitionEnabledFlowEnvelope()
    {
        NavigationQueryLimits limits = NavigationQueryLimits.Default;
        long exactBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            limits.FlowWorkspaceNodeCapacity,
            limits.FlowWorkspaceNodeCapacity - 1,
            limits.FlowWorkspaceComponentCapacity,
            limits.FlowWorkspaceEndpointPageCapacity);

        exactBytes.Should().Be(1_012_024L);
        limits.MaxFlowSinglePayloadBytes.Should().Be(exactBytes);

        using var world = new GridWorld();
        using var oneByteShort = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: exactBytes,
            maxSinglePayloadBytes: exactBytes - 1,
            maxActivePayloadBytes: exactBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        oneByteShort.TryReservePayload(exactBytes, out _).Should().BeFalse();
    }

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
            new NavigationQueryAdmissionCoordinator(1),
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
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
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

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
            transitionInstructionCount: query.AllowTransitions ? 1 : 0,
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
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
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
            world,
            maxEntries: 2,
            maxReusableBytes: 4_096,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        using var work = new NavigationFlowQueryWork(
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
        payload.TryGetNode(
                result.ResolvedOrigin,
                TraversalMedium.Solid,
                out _)
            .Should().BeTrue();
        result.Dispose();
        work.Dispose();

        var warmWorkspace = new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2);
        using var warm = new NavigationFlowQueryWork(
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
    public void CachedSuccess_ShouldRejectGraphMutationBeforePublication()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease seeded)
            .Should().Be(NavigationFlowFieldStatus.Success);
        seeded.Dispose();
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 1, 1, 8, 8, 8),
            cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();

        NavigationWorldGraph changed = fixture.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(fixture.Graph.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);

        work.Publish().Should().Be(NavigationFlowQueryStatus.Stale);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void CachedNoPath_ShouldRejectRawWorldMutationBeforePublication()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var proof = new NavigationFlowFieldPayload(
            fixture.Complete.Key,
            new[] { fixture.Complete.Nodes[0] },
            new[] { 0 },
            Array.Empty<NavigationTransitionInstruction>(),
            fixture.Complete.Dependencies,
            isComplete: true,
            worldChangeSequence: fixture.World.ChangeSequence);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: proof.RetainedBytes,
            maxSinglePayloadBytes: proof.RetainedBytes,
            maxActivePayloadBytes: proof.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                proof.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                proof,
                fixture.FarOrigin,
                ref reservation,
                out _)
            .Should().Be(NavigationFlowFieldStatus.NoPath);
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 1, 1, 8, 8, 8),
            cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();

        VoxelGrid grid = fixture.World.ActiveGrids[0];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, fixture.World.AllocateObstacleToken())
            .Should().BeTrue();

        work.Publish().Should().Be(NavigationFlowQueryStatus.Stale);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void QueryWork_ShouldReserveTheExactNoTransitionEnvelope()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-transition-envelope");
        PathQuery baseline = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile));
        var query = new PathQuery(
            baseline.Start,
            baseline.End,
            baseline.Agent,
            baseline.AreaPolicy,
            baseline.Traversal,
            baseline.Algorithm,
            baseline.Budget,
            allowTransitions: false,
            baseline.FlowField);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        long exactBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            nodeCount: 2,
            transitionInstructionCount: 0,
            componentCount: 1,
            pageCount: 1);

        using (var exactCache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: exactBytes,
            maxSinglePayloadBytes: exactBytes,
            maxActivePayloadBytes: exactBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace()))
        using (var exact = new NavigationFlowQueryWork(
            store,
            new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2),
            exactCache))
        {
            exact.Begin(query, store.TryAcquire()!);
            for (int step = 0; step < 32 && !exact.IsPrepared; step++)
            {
                exact.PrepareSearchOrCheckout(
                    lookupStepLimit: 4,
                    endpointCandidateStepLimit: 2);
            }

            exact.IsPrepared.Should().BeTrue();
            exact.Status.Should().Be(NavigationFlowQueryStatus.Pending);
            exactCache.ReservedPayloadBytes.Should().Be(exactBytes);
        }

        using var shortCache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: exactBytes,
            maxSinglePayloadBytes: exactBytes - 1,
            maxActivePayloadBytes: exactBytes - 1,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        using var oneByteShort = new NavigationFlowQueryWork(
            store,
            new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2),
            shortCache);
        oneByteShort.Begin(query, store.TryAcquire()!);
        for (int step = 0; step < 32 && !oneByteShort.IsPrepared; step++)
        {
            oneByteShort.PrepareSearchOrCheckout(
                lookupStepLimit: 4,
                endpointCandidateStepLimit: 2);
        }

        oneByteShort.IsPrepared.Should().BeTrue();
        oneByteShort.Publish().Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        shortCache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void QueryWork_UnsupportedResult_ShouldRejectSecondBeginBeforePublish()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "flow-unsupported-reentry");
        PathQuery query = fixture.CreateQuery(
            origin,
            destination,
            fixture.DefaultProfile);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(
                fixture.Graph,
                maxConcurrentLeases: 2);
        var workspace = new NavigationFlowFieldWorkspace(1, 1, 1, 2, 2, 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: 4_096,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 4_096,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        using var work = new NavigationFlowQueryWork(store, workspace, cache);
        work.Begin(query, store.TryAcquire()!);
        work.IsReadyToPublish.Should().BeTrue();

        using NavigationWorldGraphLease secondLease = store.TryAcquire()!;
        Action secondBegin = () => work.Begin(query, secondLease);

        secondBegin.Should().Throw<InvalidOperationException>();
        work.Publish().Should().Be(NavigationFlowQueryStatus.Unsupported);
        store.ActiveLeaseCount.Should().Be(1);
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
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.FlowField);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);

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
