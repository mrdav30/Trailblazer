//=======================================================================
// NavigationSearchContractTests.cs
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
using Trailblazer.Tests.Pathing.Graph;
using Xunit;

namespace Trailblazer.Tests.Pathing.Search;

public sealed class NavigationSearchContractTests
{
    [Theory]
    [InlineData((int)NavigationSurfaceAStarStatus.Success, 1, true)]
    [InlineData((int)NavigationSurfaceAStarStatus.Success, 0, false)]
    [InlineData((int)NavigationSurfaceAStarStatus.NoPath, 0, true)]
    [InlineData((int)NavigationSurfaceAStarStatus.NoPath, 1, false)]
    [InlineData((int)NavigationSurfaceAStarStatus.BudgetExceeded, 0, false)]
    [InlineData((int)NavigationSurfaceAStarStatus.BudgetExceeded, 1, false)]
    public void AStarPayloadReuse_ShouldRequireAnExactTerminalShape(
        int statusValue,
        int guidePointCount,
        bool expected)
    {
        NavigationAStarPayload.IsReusableResult(
                (NavigationSurfaceAStarStatus)statusValue,
                guidePointCount)
            .Should().Be(expected);
    }

    [Fact]
    public void FlowPayload_ShouldRejectNonEmptyMisalignedArrays()
    {
        var dependencies = new GraphDependencyStamp(
            default,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());

        Action construct = () => _ = new NavigationFlowFieldPayload(
            default,
            new NavigationFlowFieldNode[1],
            Array.Empty<int>(),
            Array.Empty<NavigationTransitionInstruction>(),
            dependencies,
            isComplete: false,
            worldChangeSequence: null);

        construct.Should().Throw<ArgumentException>()
            .WithMessage("Flow payload arrays must be non-empty and aligned.*");
    }

    [Fact]
    public void FlowPayload_ShouldRejectEmptyAlignedArrays()
    {
        var dependencies = new GraphDependencyStamp(
            default,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());

        Action construct = () => _ = new NavigationFlowFieldPayload(
            default,
            Array.Empty<NavigationFlowFieldNode>(),
            Array.Empty<int>(),
            Array.Empty<NavigationTransitionInstruction>(),
            dependencies,
            isComplete: false,
            worldChangeSequence: null);

        construct.Should().Throw<ArgumentException>()
            .WithMessage("Flow payload arrays must be non-empty and aligned.*");
    }

    [Fact]
    public void ReturnedAStarPayloadLease_ShouldRejectPayloadAccess()
    {
        using var world = new GridWorld();
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        NavigationWorldGraph graph = NavigationAStarExitTestHarness.WithPolicy(
            NavigationWorldGraph.Empty);
        graph = graph.WithGraphVersion(graphVersion: 1);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        var dependencies = new GraphDependencyStamp(
            NavigationAStarExitTestHarness.Policy.Key,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());
        var payload = new NavigationAStarPayload(
            default,
            Array.Empty<NavigationAStarGuidePoint>(),
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.Zero,
            dependencies,
            worldChangeSequence: null,
            NavigationSurfaceAStarStatus.NoPath);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: payload.RetainedBytes,
            maxSinglePayloadBytes: payload.RetainedBytes,
            maxActivePayloadBytes: payload.RetainedBytes,
            maxActiveLeases: 1);
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        cache.TryPublish(payload, store, ref reservation, out NavigationAStarPayloadLease lease)
            .Should().BeTrue();
        lease.Payload.Should().BeSameAs(payload);

        lease.Dispose();

        Action access = () => _ = lease.Payload;

        access.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticSeamReference_ShouldExposeTheSelectedDirectedEndpoints(bool reverse)
    {
        var first = new NavigationCellAddress("first", default);
        var second = new NavigationCellAddress("second", default);
        var pair = new NavigationAutomaticSeamPair(first, second, default);
        var seam = new NavigationAutomaticSeamRef(pair, reverse);

        seam.Source.Should().Be(reverse ? second : first);
        seam.Destination.Should().Be(reverse ? first : second);
        seam.Pair.Should().BeSameAs(pair);
    }

    [Fact]
    public void SearchTables_ShouldForgetPriorMembershipAfterReset()
    {
        var state = new NavigationMediumStateRef(default, TraversalMedium.Solid);
        var nodes = new NavigationAStarNodeTable(capacity: 1);
        var pages = new NavigationPageStampSet(capacity: 1);
        var flow = new NavigationFlowFieldWorkspace(
            mapCapacity: 0,
            dependencyPageCapacity: 0,
            dependencyComponentCapacity: 0,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 0,
            rayTraceIntervalCapacity: 0);

        nodes.TryGetOrAdd(state, out _, out _).Should().BeTrue();
        pages.Add("map", pageIndex: 2).Should().BeTrue();
        flow.TryGetOrAdd(state, out _, out _).Should().BeTrue();

        nodes.Reset();
        pages.Reset();
        flow.ResetSearch();

        nodes.TryGetSlot(state, out _).Should().BeFalse();
        pages.Contains("map", pageIndex: 2).Should().BeFalse();
        flow.TryGetSlot(state, out _).Should().BeFalse();
    }

    [Fact]
    public void DefaultBatchHandles_ShouldFailClosedWithoutAnOwner()
    {
        Action inspectAStar = () => default(NavigationAStarBatchWork).GetStatus(0);
        Action inspectFlow = () => default(NavigationFlowBatchWork).GetStatus(0);

        inspectAStar.Should().Throw<ObjectDisposedException>();
        inspectFlow.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ResolvedQuery_ShouldRejectGraphAccessAfterItsSnapshotIsReleased()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationWorldGraphLease lease = fixture.Store.TryAcquire()!;
        fixture.Graph.AreaCatalog.TryGet(
                fixture.FarQuery.AreaPolicy,
                out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            fixture.FarQuery,
            default,
            default,
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Solid,
            new NavigationWorkMeter(fixture.FarQuery.Budget),
            fixture.World.ChangeSequence,
            requiresWorldStamp: false);
        resolved.Graph.Should().BeSameAs(lease.Graph);

        resolved.ReleaseLease();

        FluentActions.Invoking(() => _ = resolved.Graph)
            .Should().Throw<ObjectDisposedException>();
        resolved.Dispose();
    }

    [Theory]
    [InlineData("address")]
    [InlineData("selected-edge")]
    public void FlowCache_ShouldRejectEveryConflictingCanonicalNodeIdentity(string field)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldNode[] nodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        NavigationFlowFieldNode first = nodes[0];
        NavigationCellAddress differentAddress = new(
            first.Address.MapId + "-different",
            first.Address.Index);
        NavigationSelectedEdgeRef differentEdge = first.SelectedEdge.IsValid
            ? default
            : new NavigationSelectedEdgeRef(
                first.Address,
                TraversalMedium.Solid,
                canonicalOutgoingOrdinal: 0);
        nodes[0] = new NavigationFlowFieldNode(
            field == "address" ? differentAddress : first.Address,
            first.Medium,
            first.IntegrationCost,
            field == "selected-edge" ? differentEdge : first.SelectedEdge,
            first.TransitionInstructionOrdinal);
        NavigationFlowFieldPayload conflicting = Copy(fixture.Far, nodes);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, conflicting);
        Publish(cache, fixture, fixture.Far).Dispose();
        cache.TryReservePayload(
                conflicting.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                conflicting,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Same-key flow payloads do not share one canonical node prefix.");

        cache.Count.Should().Be(1);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void FlowCache_ShouldRejectConflictingCanonicalWorldStamp()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload stamped = Copy(
            fixture.Far,
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone(),
            fixture.World.ChangeSequence);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, stamped);
        Publish(cache, fixture, fixture.Far).Dispose();
        cache.TryReservePayload(
                stamped.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                stamped,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Equal flow prefixes do not share exact completion dependencies.");

        cache.Count.Should().Be(1);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Theory]
    [InlineData("component-count")]
    [InlineData("page-count")]
    [InlineData("transition-rule-presence")]
    public void FlowCache_EqualCanonicalPrefixShouldRequireExactDependencyShape(string field)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        GraphDependencyStamp baseline = fixture.Far.Dependencies;
        baseline.Components.Should().NotBeEmpty();
        baseline.Pages.Should().NotBeEmpty();
        var changedDependencies = new GraphDependencyStamp(
            baseline.AreaPolicy,
            field == "component-count"
                ? Array.Empty<GraphComponentDependency>()
                : baseline.Components,
            field == "page-count"
                ? Array.Empty<GraphPageDependency>()
                : baseline.Pages,
            field == "transition-rule-presence"
                ? !baseline.HasTransitionRuleDependency
                : baseline.HasTransitionRuleDependency,
            fixture.Graph.TransitionRules.Version);
        NavigationFlowFieldPayload changed = WithDependencies(
            fixture.Far,
            changedDependencies);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, fixture.Far);
        Publish(cache, fixture, fixture.Far).Dispose();
        cache.TryReservePayload(
                changed.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                changed,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Equal flow prefixes do not share exact completion dependencies.");

        cache.Count.Should().Be(1);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("owner")]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("source-address")]
    [InlineData("destination-address")]
    [InlineData("source-medium")]
    [InlineData("destination-medium")]
    [InlineData("source-position")]
    [InlineData("destination-position")]
    [InlineData("locomotion-hints")]
    public void FlowCache_ShouldRejectEveryConflictingTransitionInstructionField(
        string field)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldNode[] nodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        int selectedOrdinal = Array.FindIndex(nodes, node => node.SelectedEdge.IsValid);
        selectedOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationFlowFieldNode selected = nodes[selectedOrdinal];
        nodes[selectedOrdinal] = new NavigationFlowFieldNode(
            selected.Address,
            selected.Medium,
            selected.IntegrationCost,
            selected.SelectedEdge,
            transitionInstructionOrdinal: 0);
        NavigationTransitionInstruction baseline = Instruction(selected);
        NavigationTransitionInstruction conflicting = DifferentInstruction(
            baseline,
            field);
        NavigationFlowFieldPayload first = Copy(fixture.Far, nodes, baseline);
        NavigationFlowFieldPayload second = Copy(fixture.Far, nodes, conflicting);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, first);
        Publish(cache, fixture, first).Dispose();
        cache.TryReservePayload(
                second.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                second,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Same-key flow payloads do not share one canonical node prefix.");

        cache.Count.Should().Be(1);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void ReturnedFlowGuideAlias_ShouldExposeOnlyFailClosedState()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(
            fixture,
            fixture.Far,
            guideMapCapacity: fixture.Graph.MapCount);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture, fixture.Far);
        var result = new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease);
        cache.TryCreateGuide(fixture.Store, result, out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldLease staleAlias = guide;

        guide.Dispose();

        staleAlias.Status.Should().Be(NavigationGuideStatus.Stale);
        staleAlias.OriginIntegrationCost.Should().Be(Fixed64.Zero);
        staleAlias.TrySample(
                Vector3d.Zero,
                default,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Stale);
        sample.Should().Be(default(NavigationFlowSample));
        staleAlias.CompletePendingTransition(default)
            .Should().Be(NavigationGuideStatus.Stale);
        staleAlias.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void DisposingFlowCache_ShouldInvalidateAnActiveGuideAndReleaseItCleanly()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var cache = CreateCache(
            fixture,
            fixture.Far,
            guideMapCapacity: fixture.Graph.MapCount);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture, fixture.Far);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);

        cache.Dispose();

        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        guide.TrySample(
                Vector3d.Zero,
                default,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Stale);
        sample.Should().Be(default(NavigationFlowSample));
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void FlowGuideCreation_ShouldReleaseItsPayloadWhenMapCapacityIsExceeded()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(
            fixture,
            fixture.Far,
            guideMapCapacity: 0);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture, fixture.Far);
        var result = new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease);

        cache.TryCreateGuide(fixture.Store, result, out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);

        guide.Should().Be(default(NavigationFlowFieldLease));
        payloadLease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void AStarGuideAdvance_ShouldRetryWithoutMovingItsCursorAfterGraphLeasePressure()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(
                fixture.Graph,
                maxConcurrentLeases: 1);
        NavigationAStarPayload payload = AStarPayload(fixture);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: payload.RetainedBytes,
            maxSinglePayloadBytes: payload.RetainedBytes,
            maxActivePayloadBytes: payload.RetainedBytes,
            maxActiveLeases: 1);
        NavigationAStarPayloadLease payloadLease = Publish(cache, store, payload);
        cache.TryCreateGuide(store, payloadLease, out NavigationAStarGuideLease? inner)
            .Should().Be(NavigationAStarQueryStatus.Success);
        var guide = new NavigationGuideLease(inner!);
        using NavigationWorldGraphLease pressure = store.TryAcquire()!;
        pressure.Should().NotBeNull();

        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.CapacityExceeded);
        guide.CurrentStepIndex.Should().Be(0);

        pressure.Dispose();
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.CurrentStepIndex.Should().Be(0,
            "a single-step guide remains at its terminal waypoint");
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void AStarGuideCreation_ShouldReleaseItsPayloadUnderGraphLeasePressure()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(
                fixture.Graph,
                maxConcurrentLeases: 1);
        NavigationAStarPayload payload = AStarPayload(fixture);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: payload.RetainedBytes,
            maxSinglePayloadBytes: payload.RetainedBytes,
            maxActivePayloadBytes: payload.RetainedBytes,
            maxActiveLeases: 1);
        NavigationAStarPayloadLease payloadLease = Publish(cache, store, payload);
        using NavigationWorldGraphLease pressure = store.TryAcquire()!;
        pressure.Should().NotBeNull();

        cache.TryCreatePublicGuide(store, payloadLease, out NavigationGuideLease? guide)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);

        guide.Should().BeNull();
        Action accessReturnedLease = () => _ = payloadLease.Payload;
        accessReturnedLease.Should().Throw<ObjectDisposedException>();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void AStarBatch_ShouldBreakEqualStableOrdinalsByInputOrderAndValidateIndices()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        var coordinator = new NavigationQueryAdmissionCoordinator(maximumCount: 1);
        using var gate = new NavigationAStarAdmissionGate(
            fixture.World,
            fixture.Store,
            BatchLimits(),
            coordinator);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(stableOrdinal: 7, query),
                new PathQueryBatchItem(stableOrdinal: 7, query)
            },
            count: 2);
        gate.Begin(batch, out NavigationAStarBatchWork work)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        using (work)
        {
            work.AdmittedCount.Should().Be(1);
            work.GetStatus(0).Should().Be(NavigationAStarQueryStatus.Pending);
            work.GetStatus(1).Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
            FluentActions.Invoking(() => work.GetStatus(-1))
                .Should().Throw<ArgumentOutOfRangeException>();
            FluentActions.Invoking(() => work.GetStatus(2))
                .Should().Throw<ArgumentOutOfRangeException>();
        }
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void FlowBatch_ShouldBreakEqualStableOrdinalsByInputOrderAndValidateIndices()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = fixture.FarQuery;
        var coordinator = new NavigationQueryAdmissionCoordinator(maximumCount: 1);
        using var gate = new NavigationFlowAdmissionGate(
            fixture.World,
            fixture.Store,
            BatchLimits(),
            coordinator,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(stableOrdinal: 7, query),
                new PathQueryBatchItem(stableOrdinal: 7, query)
            },
            count: 2);
        gate.Begin(batch, out NavigationFlowBatchWork work)
            .Should().Be(NavigationFlowQueryStatus.Pending);
        using (work)
        {
            work.AdmittedCount.Should().Be(1);
            work.GetStatus(0).Should().Be(NavigationFlowQueryStatus.Pending);
            work.GetStatus(1).Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
            FluentActions.Invoking(() => work.GetStatus(-1))
                .Should().Throw<ArgumentOutOfRangeException>();
            FluentActions.Invoking(() => work.GetStatus(2))
                .Should().Throw<ArgumentOutOfRangeException>();
        }
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void AStarQueryWork_ShouldPublishUnsupportedAndReleaseReservedResources()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 2,
            nodeCapacity: 4,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 4);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: 4_096,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1);
        using var work = new NavigationAStarQueryWork(
            fixture.World,
            fixture.Store,
            workspace,
            cache);
        NavigationWorldGraphLease lease = fixture.Store.TryAcquire()!;
        cache.TryReservePayload(
                maximumBytes: 2_048,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();

        work.BeginReserved(fixture.FarQuery, lease, ref reservation);

        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        work.IsReadyToPublish.Should().BeTrue();
        work.Publish().Should().Be(NavigationAStarQueryStatus.Unsupported);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void AStarQueryWork_PrepareBeforeBegin_ShouldRemainDormant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: 2_048,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1);
        using var work = new NavigationAStarQueryWork(
            fixture.World,
            fixture.Store,
            new NavigationAStarWorkspace(1, 2, 2, 4, 4, 4, 4),
            cache);

        work.PrepareSearchOrCheckout(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationAStarQueryStatus.Pending);

        work.IsPrepared.Should().BeFalse();
        fixture.Store.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStarQueryWork_DisposeShouldReleaseCachedLeaseBeforeOrAfterPublication(
        bool publish)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        NavigationAStarPayload payload = AStarPayload(fixture, query);
        var cache = CreateAStarCache(fixture, payload, maxActiveLeases: 2);
        Publish(cache, fixture.Store, payload).Dispose();
        using var work = new NavigationAStarQueryWork(
            fixture.World,
            fixture.Store,
            new NavigationAStarWorkspace(1, 2, 2, 4, 4, 4, 4),
            cache);
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        work.BeginReserved(query, fixture.Store.TryAcquire()!, ref reservation);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);
        if (publish)
        {
            work.Publish().Should().Be(NavigationAStarQueryStatus.Success);
            cache.ActiveLeaseCount.Should().Be(1,
                "the untaken successful result remains owned by query work");
        }

        work.Dispose();

        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void AStarQueryWork_CachedNoPathShouldDisposeItsLeaseWithoutPublishingAResult()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        NavigationAStarPayload success = AStarPayload(fixture, query);
        var noPath = new NavigationAStarPayload(
            success.Key,
            Array.Empty<NavigationAStarGuidePoint>(),
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.Zero,
            success.Dependencies,
            success.WorldChangeSequence,
            NavigationSurfaceAStarStatus.NoPath);
        var cache = CreateAStarCache(fixture, noPath, maxActiveLeases: 2);
        Publish(cache, fixture.Store, noPath).Dispose();
        using var work = new NavigationAStarQueryWork(
            fixture.World,
            fixture.Store,
            new NavigationAStarWorkspace(1, 2, 2, 4, 4, 4, 4),
            cache);
        cache.TryReservePayload(
                noPath.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        work.BeginReserved(query, fixture.Store.TryAcquire()!, ref reservation);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);

        work.Publish().Should().Be(NavigationAStarQueryStatus.NoPath);

        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        FluentActions.Invoking(() => work.TakeResult())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AStarQueryWork_GeneratedPayloadShouldRejectDependencyRevisionBeforePublish()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        var workspace = new NavigationAStarWorkspace(1, 4, 4, 8, 16, 16, 16);
        long maximumBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: maximumBytes,
            maxSinglePayloadBytes: maximumBytes,
            maxActivePayloadBytes: maximumBytes,
            maxActiveLeases: 1);
        using var work = new NavigationAStarQueryWork(
            fixture.World,
            fixture.Store,
            workspace,
            cache);
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        work.BeginReserved(query, fixture.Store.TryAcquire()!, ref reservation);
        for (int step = 0; step < 256 && !work.IsPrepared; step++)
            work.PrepareSearchOrCheckout(64, 16);
        for (int step = 0; step < 256 && !work.IsReadyToPublish; step++)
            work.AdvanceSearch(64, 64, 64, 64);
        work.IsReadyToPublish.Should().BeTrue();
        work.Status.Should().Be(NavigationAStarQueryStatus.Pending);
        cache.Count.Should().Be(0,
            "the completed search must still own an unpublished payload");

        NavigationWorldGraph changed = fixture.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(fixture.Graph.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);

        work.Publish().Should().Be(NavigationAStarQueryStatus.Stale);
        cache.Count.Should().Be(0,
            "a generated payload whose exact dependencies changed must not enter the cache");
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        FluentActions.Invoking(() => work.TakeResult())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AStarCache_ShouldKeepAnInactiveLargerCanonicalPayloadBehindOtherReservations()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        NavigationAStarPayload canonical = AStarPayload(fixture, query);
        var shorter = new NavigationAStarPayload(
            canonical.Key,
            Array.Empty<NavigationAStarGuidePoint>(),
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.Zero,
            canonical.Dependencies,
            canonical.WorldChangeSequence,
            NavigationSurfaceAStarStatus.NoPath);
        canonical.RetainedBytes.Should().BeGreaterThan(shorter.RetainedBytes);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: canonical.RetainedBytes,
            maxSinglePayloadBytes: canonical.RetainedBytes,
            maxActivePayloadBytes: checked(
                canonical.RetainedBytes + shorter.RetainedBytes),
            maxActiveLeases: 2);
        Publish(cache, fixture.Store, canonical).Dispose();
        cache.TryReservePayload(
                canonical.RetainedBytes,
                out NavigationAStarPayloadReservation blocker)
            .Should().BeTrue();
        cache.TryReservePayload(
                shorter.RetainedBytes,
                out NavigationAStarPayloadReservation candidate)
            .Should().BeTrue();

        cache.TryPublish(shorter, fixture.Store, ref candidate, out _)
            .Should().BeFalse(
                "the unrelated reservation leaves room for the shorter candidate but not the larger canonical payload");

        candidate.HasLeaseSlot.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReleasePayloadReservation(ref candidate);
        cache.ReleasePayloadReservation(ref blocker);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FlowQueryWork_ShouldPublishUnsupportedWithoutReservingPayloadCapacity()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var workspace = new NavigationFlowFieldWorkspace(
            mapCapacity: 1,
            dependencyPageCapacity: 2,
            dependencyComponentCapacity: 2,
            nodeCapacity: 4,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: 4_096,
            maxSinglePayloadBytes: 2_048,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1,
            guideMapCapacity: 1,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        using var work = new NavigationFlowQueryWork(fixture.Store, workspace, cache);
        NavigationWorldGraphLease lease = fixture.Store.TryAcquire()!;
        PathQuery aStarQuery = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);

        work.Begin(aStarQuery, lease);

        work.IsReadyToPublish.Should().BeTrue();
        work.Publish().Should().Be(NavigationFlowQueryStatus.Unsupported);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FlowQueryWork_PrepareBeforeBegin_ShouldRemainDormant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, fixture.Far);
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 2, 2, 4, 4, 4),
            cache);

        work.PrepareSearchOrCheckout(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationFlowQueryStatus.Pending);

        work.IsPrepared.Should().BeFalse();
        fixture.Store.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FlowQueryWork_DisposeShouldReleaseACachedLeaseBeforePublication()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, fixture.Far);
        Publish(cache, fixture, fixture.Far).Dispose();
        var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 2, 2, 8, 8, 8),
            cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        work.Dispose();

        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FlowCache_ShouldReuseATombstoneAfterEveryTableSlotWasVisited()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload first = fixture.Far;
        NavigationFlowFieldPayload? second = null;
        NavigationFlowFieldPayload? probe = null;
        int firstBucket = first.Key.GetHashCode() & 1;
        for (int expansion = 1; expansion < 64; expansion++)
        {
            NavigationFlowFieldPayload candidate =
                NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                    fixture.Far,
                    fixture.FarQuery,
                    expansion);
            if ((candidate.Key.GetHashCode() & 1) != firstBucket && second == null)
                second = candidate;
            else if (!candidate.Key.Equals(first.Key)
                && (second == null || !candidate.Key.Equals(second.Key)))
                probe = candidate;
            if (second != null && probe != null)
                break;
        }
        second.Should().NotBeNull("a one-bit table bucket must have an opposite-key hash");
        probe.Should().NotBeNull("a third exact key is needed to prove tombstone reuse");
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, first);
        Publish(cache, fixture, first).Dispose();
        Publish(cache, fixture, second!).Dispose();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                probe!.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        Publish(cache, fixture, probe).Dispose();

        cache.Count.Should().Be(1);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                probe.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease lease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        lease.Dispose();
    }

    [Fact]
    public void FlowCache_CheckoutShouldRespectActiveByteCapacityAndRecoverAfterRelease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload first = fixture.Far;
        NavigationFlowFieldPayload second =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                first,
                fixture.FarQuery,
                expansion: 1);
        long retainedBytes = first.RetainedBytes;
        second.RetainedBytes.Should().Be(retainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 2,
            maxReusableBytes: checked(retainedBytes * 2),
            maxSinglePayloadBytes: retainedBytes,
            maxActivePayloadBytes: retainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: fixture.Graph.MapCount,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, second).Dispose();
        NavigationFlowFieldPayloadLease blocker = Publish(cache, fixture, first);

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                second.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease rejected,
                out NavigationFlowFieldPayload? rejectedProof)
            .Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
        rejected.Should().Be(default(NavigationFlowFieldPayloadLease));
        rejectedProof.Should().BeNull();
        cache.ActiveLeaseCount.Should().Be(1);
        cache.LeasedBytes.Should().Be(retainedBytes);

        blocker.Dispose();
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                second.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease recovered,
                out NavigationFlowFieldPayload? recoveredProof)
            .Should().Be(NavigationFlowFieldStatus.Success);
        recoveredProof.Should().BeSameAs(second);
        cache.ActiveLeaseCount.Should().Be(1);
        cache.LeasedBytes.Should().Be(retainedBytes);
        recovered.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void FlowQueryWork_CachedCapacityRejectionShouldStopTheAdmissionPrefix()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload first = fixture.Far;
        NavigationFlowFieldPayload second =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                first,
                fixture.FarQuery,
                expansion: 1);
        NavigationFlowFieldPayloadKey key = second.Key;
        var query = new PathQuery(
            fixture.FarQuery.Start,
            key.Destination,
            key.Agent,
            key.AreaPolicy,
            key.Traversal,
            PathAlgorithm.FlowField,
            key.Budget,
            key.AllowTransitions,
            key.FlowField);
        long retainedBytes = first.RetainedBytes;
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 2,
            maxReusableBytes: checked(retainedBytes * 2),
            maxSinglePayloadBytes: retainedBytes,
            maxActivePayloadBytes: retainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: fixture.Graph.MapCount,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, second).Dispose();
        NavigationFlowFieldPayloadLease blocker = Publish(cache, fixture, first);
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 2, 2, 8, 8, 8),
            cache);
        work.Begin(query, fixture.Store.TryAcquire()!);

        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);

        work.IsReadyToPublish.Should().BeTrue();
        work.ReservationRejected.Should().BeTrue(
            "the admission gate must stop at the first cache-capacity rejection");
        work.Publish().Should().Be(NavigationFlowQueryStatus.CapacityExceeded);
        cache.ActiveLeaseCount.Should().Be(1);
        cache.LeasedBytes.Should().Be(retainedBytes);
        blocker.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(0, (int)NavigationFlowQueryStatus.CostOverflow)]
    [InlineData(1, (int)NavigationFlowQueryStatus.Stale)]
    [InlineData(2, (int)NavigationFlowQueryStatus.Stale)]
    public void FlowQueryWork_CachedCostOverflowProofShouldRequireCurrentLeaseAndDependencies(
        int invalidationKind,
        int expectedStatusValue)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldNode[] nodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        int originOrdinal = Array.FindIndex(
            nodes,
            node => node.Address == fixture.FarOrigin
                && node.Medium == fixture.Far.Key.StartMedium);
        originOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationFlowFieldNode origin = nodes[originOrdinal];
        nodes[originOrdinal] = new NavigationFlowFieldNode(
            origin.Address,
            origin.Medium,
            Fixed64.MaxValue,
            origin.SelectedEdge,
            origin.TransitionInstructionOrdinal);
        NavigationFlowFieldPayload overflow = Copy(fixture.Far, nodes);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, overflow);
        cache.TryReservePayload(
                overflow.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                overflow,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease published)
            .Should().Be(NavigationFlowFieldStatus.CostOverflow);
        published.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(1);
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 2, 2, 8, 8, 8),
            cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();
        if (invalidationKind == 1)
        {
            NavigationWorldGraph changed = fixture.Graph
                .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
                .WithGraphVersion(fixture.Graph.GraphVersion + 1);
            fixture.Store.TryPublish(changed)
                .Should().Be(NavigationCandidatePublication.Published);
        }
        else if (invalidationKind == 2)
        {
            cache.Reset();
        }

        work.Publish().Should().Be((NavigationFlowQueryStatus)expectedStatusValue);

        cache.Count.Should().Be(invalidationKind == 0 ? 1 : 0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FlowQueryWork_ResetCachedSuccessBeforePublicationShouldFailClosedWithoutDoubleRemoval()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, fixture.Far);
        Publish(cache, fixture, fixture.Far).Dispose();
        using var work = new NavigationFlowQueryWork(
            fixture.Store,
            new NavigationFlowFieldWorkspace(1, 2, 2, 8, 8, 8),
            cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.PrepareSearchOrCheckout(8, 4);
        work.IsReadyToPublish.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        cache.Reset();

        work.Publish().Should().Be(NavigationFlowQueryStatus.Stale);
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlowCache_ZeroEntryPublicationShouldDetachOnlySuccessfulPayloads(
        bool successful)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload payload = successful ? fixture.Far : fixture.Complete;
        NavigationCellAddress requiredOrigin = successful
            ? fixture.FarOrigin
            : new NavigationCellAddress(
                fixture.FarOrigin.MapId,
                new VoxelIndex(99, 0, 0));
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: payload.RetainedBytes,
            maxActivePayloadBytes: payload.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: fixture.Graph.MapCount,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        NavigationFlowFieldStatus status = cache.TryPublishOrPromote(
            fixture.Store,
            payload,
            requiredOrigin,
            ref reservation,
            out NavigationFlowFieldPayloadLease lease);

        status.Should().Be(successful
            ? NavigationFlowFieldStatus.Success
            : NavigationFlowFieldStatus.NoPath);
        reservation.Should().Be(default(NavigationFlowFieldReservation));
        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(successful ? 1 : 0);
        cache.DetachedBytes.Should().Be(successful ? payload.RetainedBytes : 0);
        if (successful)
        {
            lease.TryGetPayload(out NavigationFlowFieldPayload detached)
                .Should().Be(NavigationFlowFieldStatus.Success);
            detached.Should().BeSameAs(payload);
        }
        else
        {
            lease.Should().Be(default(NavigationFlowFieldPayloadLease));
        }
        lease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false, (int)NavigationFlowFieldStatus.Pending)]
    [InlineData(true, (int)NavigationFlowFieldStatus.NoPath)]
    public void FlowCache_MissingOriginShouldDistinguishIncompleteFromCompleteCoverage(
        bool complete,
        int expectedStatusValue)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload payload = complete ? fixture.Complete : fixture.Near;
        payload.IsComplete.Should().Be(complete);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, payload);
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        var missing = new NavigationCellAddress(
            payload.Key.DestinationAddress.MapId,
            new GridForge.Spatial.VoxelIndex(100, 0, 0));

        cache.TryPublishOrPromote(
                fixture.Store,
                payload,
                missing,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be((NavigationFlowFieldStatus)expectedStatusValue);

        lease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.Count.Should().Be(1,
            "a canonical incomplete or negative proof remains reusable for covered origins");
    }

    [Theory]
    [InlineData((int)NavigationAStarQueryStatus.Pending, NavigationGuideStatus.Stale)]
    [InlineData((int)NavigationAStarQueryStatus.Success, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationAStarQueryStatus.Unsupported, NavigationGuideStatus.Unsupported)]
    [InlineData((int)NavigationAStarQueryStatus.NoMap, NavigationGuideStatus.NoMap)]
    [InlineData((int)NavigationAStarQueryStatus.InvalidProfile, NavigationGuideStatus.InvalidProfile)]
    [InlineData((int)NavigationAStarQueryStatus.InvalidStart, NavigationGuideStatus.InvalidStart)]
    [InlineData((int)NavigationAStarQueryStatus.InvalidEnd, NavigationGuideStatus.InvalidEnd)]
    [InlineData((int)NavigationAStarQueryStatus.NoPath, NavigationGuideStatus.NoPath)]
    [InlineData((int)NavigationAStarQueryStatus.BudgetExceeded, NavigationGuideStatus.BudgetExceeded)]
    [InlineData((int)NavigationAStarQueryStatus.CostOverflow, NavigationGuideStatus.CostOverflow)]
    [InlineData((int)NavigationAStarQueryStatus.CapacityExceeded, NavigationGuideStatus.CapacityExceeded)]
    [InlineData((int)NavigationAStarQueryStatus.Stale, NavigationGuideStatus.Stale)]
    public void AStarGuideStatusMapper_ShouldNeverExposeAnUndefinedPublicStatus(
        int statusValue,
        NavigationGuideStatus expected)
    {
        NavigationGuideStatusMapper.ToPublic((NavigationAStarQueryStatus)statusValue)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationFlowQueryStatus.Pending, NavigationGuideStatus.Stale)]
    [InlineData((int)NavigationFlowQueryStatus.Success, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationFlowQueryStatus.Unsupported, NavigationGuideStatus.Unsupported)]
    [InlineData((int)NavigationFlowQueryStatus.NoMap, NavigationGuideStatus.NoMap)]
    [InlineData((int)NavigationFlowQueryStatus.InvalidProfile, NavigationGuideStatus.InvalidProfile)]
    [InlineData((int)NavigationFlowQueryStatus.InvalidStart, NavigationGuideStatus.InvalidStart)]
    [InlineData((int)NavigationFlowQueryStatus.InvalidEnd, NavigationGuideStatus.InvalidEnd)]
    [InlineData((int)NavigationFlowQueryStatus.NoPath, NavigationGuideStatus.NoPath)]
    [InlineData((int)NavigationFlowQueryStatus.BudgetExceeded, NavigationGuideStatus.BudgetExceeded)]
    [InlineData((int)NavigationFlowQueryStatus.CostOverflow, NavigationGuideStatus.CostOverflow)]
    [InlineData((int)NavigationFlowQueryStatus.CapacityExceeded, NavigationGuideStatus.CapacityExceeded)]
    [InlineData((int)NavigationFlowQueryStatus.Stale, NavigationGuideStatus.Stale)]
    public void FlowGuideStatusMapper_ShouldNeverExposeAnUndefinedPublicStatus(
        int statusValue,
        NavigationGuideStatus expected)
    {
        NavigationGuideStatusMapper.ToPublic((NavigationFlowQueryStatus)statusValue)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationRayStatus.Blocked, NavigationGuideStatus.LocalRecoveryRequired)]
    [InlineData((int)NavigationRayStatus.BudgetExceeded, NavigationGuideStatus.BudgetExceeded)]
    [InlineData((int)NavigationRayStatus.CostOverflow, NavigationGuideStatus.CostOverflow)]
    [InlineData((int)NavigationRayStatus.CapacityExceeded, NavigationGuideStatus.CapacityExceeded)]
    [InlineData((int)NavigationRayStatus.Stale, NavigationGuideStatus.Stale)]
    public void FlowRecoveryRayStatus_ShouldPreserveTerminalCauseAndFailClosed(
        int statusValue,
        NavigationGuideStatus expected)
    {
        NavigationGuideStatusMapper.ToPublic((NavigationRayStatus)statusValue)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Complete, (int)NavigationFlowFieldStatus.Stale)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded, (int)NavigationFlowFieldStatus.BudgetExceeded)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.CostOverflow, (int)NavigationFlowFieldStatus.CostOverflow)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.CapacityExceeded, (int)NavigationFlowFieldStatus.CapacityExceeded)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Stale, (int)NavigationFlowFieldStatus.Stale)]
    public void FlowTraversalStatus_ShouldPreserveTerminalCauseAndFailClosed(
        int statusValue,
        int expectedStatusValue)
    {
        NavigationGuideStatusMapper.ToFlowField(
                (NavigationTraversalEdgeAdvanceStatus)statusValue)
            .Should().Be((NavigationFlowFieldStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData((int)NavigationFlowNodeLookupStatus.Success, false, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationFlowNodeLookupStatus.Success, true, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationFlowNodeLookupStatus.NotFound, false, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationFlowNodeLookupStatus.NotFound, true, NavigationGuideStatus.Stale)]
    [InlineData((int)NavigationFlowNodeLookupStatus.BudgetExceeded, false, NavigationGuideStatus.BudgetExceeded)]
    [InlineData((int)NavigationFlowNodeLookupStatus.BudgetExceeded, true, NavigationGuideStatus.BudgetExceeded)]
    public void FlowNodeLookupStatus_ShouldDistinguishRequiredNodesFromRecoveryCandidates(
        int statusValue,
        bool required,
        NavigationGuideStatus expected)
    {
        NavigationSelectedEdgeProgressWork.MapNodeLookupStatus(
                (NavigationFlowNodeLookupStatus)statusValue,
                required)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationSurfaceEdgeAdvanceStatus.Pending, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationSurfaceEdgeAdvanceStatus.Edge, NavigationGuideStatus.Success)]
    [InlineData((int)NavigationSurfaceEdgeAdvanceStatus.Complete, NavigationGuideStatus.Stale)]
    [InlineData((int)NavigationSurfaceEdgeAdvanceStatus.Blocked, NavigationGuideStatus.BudgetExceeded)]
    public void FlowSelectedEdgeLookupStatus_ShouldPreserveBudgetAndFailClosedOtherwise(
        int statusValue,
        NavigationGuideStatus expected)
    {
        NavigationSelectedEdgeProgressWork.MapStructuralEdgeStatus(
                (NavigationSurfaceEdgeAdvanceStatus)statusValue)
            .Should().Be(expected);
    }

    [Fact]
    public void AStarGuidePool_ShouldRetireOnlyAnExhaustedGeneration()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationAStarPayload payload = AStarPayload(fixture);
        var cache = new NavigationAStarPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: payload.RetainedBytes,
            maxSinglePayloadBytes: payload.RetainedBytes,
            maxActivePayloadBytes: payload.RetainedBytes,
            maxActiveLeases: 1);
        var guide = new NavigationAStarGuideLease(cache);
        NavigationAStarGuideLease? freeGuides = null;

        NavigationAStarPayloadCache.RetainGuideIfReusable(
            long.MaxValue,
            guide,
            ref freeGuides);
        freeGuides.Should().BeNull();

        NavigationAStarPayloadCache.RetainGuideIfReusable(1, guide, ref freeGuides);
        freeGuides.Should().BeSameAs(guide);
    }

    [Fact]
    public void FlowGuidePool_ShouldRespectDisposalCapacityAndGenerationRetirement()
    {
        var guide = new NavigationFlowFieldGuideLease(0);
        var freeGuides = new NavigationFlowFieldGuideLease?[1];

        NavigationFlowFieldPayloadCache.RetainGuideIfReusable(
                ulong.MaxValue,
                disposed: false,
                guide,
                freeGuides,
                freeGuideCount: 0)
            .Should().Be(0);
        NavigationFlowFieldPayloadCache.RetainGuideIfReusable(
                1,
                disposed: true,
                guide,
                freeGuides,
                freeGuideCount: 0)
            .Should().Be(0);
        NavigationFlowFieldPayloadCache.RetainGuideIfReusable(
                1,
                disposed: false,
                guide,
                freeGuides,
                freeGuideCount: 1)
            .Should().Be(1);
        NavigationFlowFieldPayloadCache.RetainGuideIfReusable(
                1,
                disposed: false,
                guide,
                freeGuides,
                freeGuideCount: 0)
            .Should().Be(1);
        freeGuides[0].Should().BeSameAs(guide);

        int freeGuideCount = 1;
        NavigationFlowFieldPayloadCache.RentGuide(
                freeGuides,
                ref freeGuideCount,
                coveredAddressGenerationCapacity: 0)
            .Should().BeSameAs(guide);
        freeGuideCount.Should().Be(0);
        freeGuides[0].Should().BeNull();

        NavigationFlowFieldPayloadCache.RentGuide(
                freeGuides,
                ref freeGuideCount,
                coveredAddressGenerationCapacity: 0)
            .Should().NotBeSameAs(guide);
        freeGuideCount.Should().Be(0);

        Action advance = () => NavigationGenerationCounter.Advance(
            ulong.MaxValue,
            "The flow guide generation is exhausted.");
        advance.Should().Throw<InvalidOperationException>()
            .WithMessage("The flow guide generation is exhausted.");
    }

    [Fact]
    public void FlowPayloadLeaseSlot_ShouldRetireOnlyAnExhaustedGeneration()
    {
        var freeLeaseSlots = new int[1];
        int freeLeaseCount = 0;

        NavigationFlowFieldPayloadCache.RecycleLeaseSlotIdentity(
                ulong.MaxValue,
                slot: 7,
                freeLeaseSlots,
                ref freeLeaseCount)
            .Should().Be(NavigationFlowFieldPayloadCache.LeaseSlotState.Retired);
        freeLeaseCount.Should().Be(0);

        NavigationFlowFieldPayloadCache.RecycleLeaseSlotIdentity(
                1,
                slot: 7,
                freeLeaseSlots,
                ref freeLeaseCount)
            .Should().Be(NavigationFlowFieldPayloadCache.LeaseSlotState.Free);
        freeLeaseCount.Should().Be(1);
        freeLeaseSlots[0].Should().Be(7);
    }

    [Fact]
    public void QueryAdmission_ShouldRejectASecondBeginWithoutReplacingItsGraphLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, maxConcurrentLeases: 2);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 2,
            nodeCapacity: 4,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 4);
        using var admission = new NavigationQueryAdmissionWork(
            fixture.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        NavigationWorldGraphLease first = store.TryAcquire()!;
        NavigationWorldGraphLease second = store.TryAcquire()!;
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        admission.Begin(first, query, query.Traversal.StartMedium, query.Traversal.TargetMedia);

        FluentActions.Invoking(() => admission.Begin(
                second,
                query,
                query.Traversal.StartMedium,
                query.Traversal.TargetMedia))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The query admission work is already active.");

        second.Dispose();
        admission.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void QueryAdmission_ShouldFailStaleWhenItsExactAreaPolicyIsMissing()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 2,
            nodeCapacity: 4,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 4);
        using var admission = new NavigationQueryAdmissionWork(
            fixture.World,
            fixture.Store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        NavigationWorldGraphLease lease = fixture.Store.TryAcquire()!;
        PathQuery source = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        var missingPolicy = new NavigationAreaPolicyKey("missing-policy", revision: 1);
        var query = new PathQuery(
            source.Start,
            source.End,
            source.Agent,
            missingPolicy,
            source.Traversal,
            source.Algorithm,
            source.Budget,
            source.AllowTransitions,
            source.FlowField);
        admission.Begin(lease, query, query.Traversal.StartMedium, query.Traversal.TargetMedia);

        admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationQueryAdmissionStatus.Stale);

        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void QueryAdmission_ShouldYieldWithoutSpendingWhenItsChunkLimitsAreZero()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 2,
            nodeCapacity: 4,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 4);
        using var admission = new NavigationQueryAdmissionWork(
            fixture.World,
            fixture.Store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        NavigationWorldGraphLease lease = fixture.Store.TryAcquire()!;
        PathQuery query = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        admission.Begin(lease, query, query.Traversal.StartMedium, query.Traversal.TargetMedia);

        admission.Advance(lookupStepLimit: 0, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationQueryAdmissionStatus.Pending);

        admission.Meter.LookupProbes.Should().Be(0);
        admission.Meter.EndpointCandidates.Should().Be(0);
        admission.Dispose();
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void QueryAdmission_ShouldFailAtPolicyLookupWhenTheSharedBudgetIsAlreadyEmpty()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        PathQuery source = WithAlgorithm(fixture.FarQuery, PathAlgorithm.AStar);
        var query = new PathQuery(
            source.Start,
            source.End,
            source.Agent,
            source.AreaPolicy,
            source.Traversal,
            source.Algorithm,
            default,
            source.AllowTransitions,
            source.FlowField);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 4, 4, 4, 4);
        using var admission = new NavigationQueryAdmissionWork(
            fixture.World,
            fixture.Store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            fixture.Store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);

        admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationQueryAdmissionStatus.BudgetExceeded);

        admission.Meter.LookupProbes.Should().Be(0);
        admission.Meter.EndpointCandidates.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void DisposedFlowCache_ShouldRejectGuideCreationAndInvalidateItsPayloadLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var cache = CreateCache(fixture, fixture.Far, guideMapCapacity: 1);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture, fixture.Far);
        var result = new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease);

        cache.Dispose();

        cache.TryCreateGuide(fixture.Store, result, out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Stale);
        guide.Should().Be(default(NavigationFlowFieldLease));
        payloadLease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        NavigationFlowFieldPayload payload,
        int guideMapCapacity = 0) => new(
        fixture.World,
        maxEntries: 1,
        maxReusableBytes: payload.RetainedBytes,
        maxSinglePayloadBytes: payload.RetainedBytes,
        maxActivePayloadBytes: payload.RetainedBytes,
        maxActiveLeases: 1,
        guideMapCapacity,
        immediateRayWorkspace:
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        NavigationFlowFieldPayload payload)
    {
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                payload,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        return lease;
    }

    private static NavigationAStarPayloadLease Publish(
        NavigationAStarPayloadCache cache,
        NavigationWorldGraphStore store,
        NavigationAStarPayload payload)
    {
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        cache.TryPublish(payload, store, ref reservation, out NavigationAStarPayloadLease lease)
            .Should().BeTrue();
        return lease;
    }

    private static NavigationAStarPayloadCache CreateAStarCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        NavigationAStarPayload payload,
        int maxActiveLeases) => new(
        fixture.World,
        maxEntries: 1,
        maxReusableBytes: payload.RetainedBytes,
        maxSinglePayloadBytes: payload.RetainedBytes,
        maxActivePayloadBytes: payload.RetainedBytes,
        maxActiveLeases);

    private static NavigationAStarPayload AStarPayload(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture) => new(
        new NavigationAStarPayloadKey(
            fixture.FarQuery,
            fixture.FarOrigin,
            fixture.FarOrigin,
            TraversalMedium.Solid,
            TraversalMedia.Solid),
        new[]
        {
            new NavigationAStarGuidePoint(
                fixture.FarOrigin,
                Vector3d.Zero,
                TraversalMedium.Solid)
        },
        Array.Empty<NavigationTransitionInstruction>(),
        Fixed64.Zero,
        fixture.Far.Dependencies,
        worldChangeSequence: null,
        NavigationSurfaceAStarStatus.Success);

    private static NavigationAStarPayload AStarPayload(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        PathQuery query) => new(
        new NavigationAStarPayloadKey(
            query,
            fixture.FarOrigin,
            fixture.Far.Key.DestinationAddress,
            TraversalMedium.Solid,
            TraversalMedia.Solid),
        new[]
        {
            new NavigationAStarGuidePoint(
                fixture.FarOrigin,
                Vector3d.Zero,
                TraversalMedium.Solid)
        },
        Array.Empty<NavigationTransitionInstruction>(),
        Fixed64.Zero,
        fixture.Far.Dependencies,
        worldChangeSequence: null,
        NavigationSurfaceAStarStatus.Success);

    private static PathQuery WithAlgorithm(PathQuery query, PathAlgorithm algorithm) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        algorithm,
        query.Budget,
        query.AllowTransitions,
        algorithm == PathAlgorithm.FlowField ? query.FlowField : default);

    private static NavigationQueryLimits BatchLimits() => new(
        maxBatchItems: 2,
        maxBatchDescriptorBytes: 2_048,
        maxConcurrentNavigationQueries: 1,
        aStarWorkspaceMapCapacity: 1,
        aStarWorkspaceEndpointPageCapacity: 2,
        aStarWorkspaceComponentCapacity: 2,
        aStarWorkspaceNodeCapacity: 4,
        maxAStarCacheEntries: 1,
        maxAStarReusablePayloadBytes: 4_096,
        maxAStarSinglePayloadBytes: 2_048,
        maxAStarActivePayloadBytes: 2_048,
        maxAStarActivePayloadLeases: 1,
        flowWorkspaceMapCapacity: 1,
        flowWorkspaceEndpointPageCapacity: 2,
        flowWorkspaceComponentCapacity: 2,
        flowWorkspaceNodeCapacity: 4,
        rayWorkspaceCoveredAddressCapacity: 4,
        rayWorkspaceTraceIntervalCapacity: 4,
        aStarWorkspaceGuidePointCapacity: 4,
        maxFlowCacheEntries: 1,
        maxFlowReusablePayloadBytes: 4_096,
        maxFlowSinglePayloadBytes: 2_048,
        maxFlowActivePayloadBytes: 2_048,
        maxFlowActivePayloadLeases: 1);

    private static NavigationFlowFieldPayload Copy(
        NavigationFlowFieldPayload payload,
        NavigationFlowFieldNode[] nodes,
        ulong? worldChangeSequence = null) => new(
        payload.Key,
        nodes,
        (int[])payload.AddressLookupOrdinals.Clone(),
        (NavigationTransitionInstruction[])payload.TransitionInstructions.Clone(),
        payload.Dependencies,
        payload.IsComplete,
        worldChangeSequence);

    private static NavigationFlowFieldPayload Copy(
        NavigationFlowFieldPayload payload,
        NavigationFlowFieldNode[] nodes,
        NavigationTransitionInstruction instruction) => new(
        payload.Key,
        nodes,
        (int[])payload.AddressLookupOrdinals.Clone(),
        new[] { instruction },
        payload.Dependencies,
        payload.IsComplete,
        payload.WorldChangeSequence);

    private static NavigationFlowFieldPayload WithDependencies(
        NavigationFlowFieldPayload payload,
        GraphDependencyStamp dependencies) => new(
        payload.Key,
        (NavigationFlowFieldNode[])payload.Nodes.Clone(),
        (int[])payload.AddressLookupOrdinals.Clone(),
        (NavigationTransitionInstruction[])payload.TransitionInstructions.Clone(),
        dependencies,
        payload.IsComplete,
        payload.WorldChangeSequence);

    private static NavigationTransitionInstruction Instruction(
        NavigationFlowFieldNode node) => new(
        NavigationTransitionIdentityKind.Definition,
        node.Address.MapId,
        "transition",
        TraversalTransitionType.Jump,
        node.Address,
        node.SelectedEdge.Target,
        node.Medium,
        node.SelectedEdge.TargetMedium,
        Vector3d.Zero,
        Vector3d.One,
        TraversalTransitionLocomotionHints.None);

    private static NavigationTransitionInstruction DifferentInstruction(
        NavigationTransitionInstruction instruction,
        string field) => new(
        field == "identity"
            ? NavigationTransitionIdentityKind.Rule
            : instruction.IdentityKind,
        field == "owner" ? "different-owner" : instruction.OwnerMapId,
        field == "id" ? "different-id" : instruction.Id,
        field == "type" ? TraversalTransitionType.Climb : instruction.Type,
        field == "source-address"
            ? instruction.DestinationAddress
            : instruction.SourceAddress,
        field == "destination-address"
            ? instruction.SourceAddress
            : instruction.DestinationAddress,
        field == "source-medium" ? TraversalMedium.Gas : instruction.SourceMedium,
        field == "destination-medium"
            ? TraversalMedium.Liquid
            : instruction.DestinationMedium,
        field == "source-position" ? Vector3d.One : instruction.SourcePosition,
        field == "destination-position" ? Vector3d.Zero : instruction.DestinationPosition,
        field == "locomotion-hints"
            ? TraversalTransitionLocomotionHints.RequestClimb
            : instruction.LocomotionHints);

}
