using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationFlowFieldGuideTests
{
    [Theory]
    [InlineData(false, 7L)]
    [InlineData(true, 8L)]
    public void SampleOrdinalAdvance_ShouldChangeOnlyWithTheSourceIdentity(
        bool advance,
        long expected)
    {
        NavigationFlowFieldGuideLease.AdvanceSampleOrdinal(7L, advance)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true, NavigationGuideStatus.Success, NavigationGuideStatus.Success, true)]
    [InlineData(false, NavigationGuideStatus.Success, NavigationGuideStatus.Stale, false)]
    [InlineData(true, NavigationGuideStatus.BudgetExceeded,
        NavigationGuideStatus.BudgetExceeded, true)]
    [InlineData(true, NavigationGuideStatus.Stale, NavigationGuideStatus.Stale, true)]
    public void TransitionSampleFinalization_ShouldPreserveFailureOrRejectAStaleSuccess(
        bool epochCurrent,
        NavigationGuideStatus status,
        NavigationGuideStatus expected,
        bool retainSample)
    {
        var sample = new NavigationFlowSample(
            Vector3d.Forward,
            Vector3d.One,
            TraversalMedium.Solid,
            default,
            hasTransition: false);

        NavigationFlowFieldGuideLease.ResolveTransitionSampleStatus(
                epochCurrent,
                status,
                ref sample)
            .Should().Be(expected);

        sample.Should().Be(retainSample
            ? new NavigationFlowSample(
                Vector3d.Forward,
                Vector3d.One,
                TraversalMedium.Solid,
                default,
                hasTransition: false)
            : default);
    }

    [Theory]
    [InlineData(false, 7UL, 7UL, false)]
    [InlineData(true, 8UL, 7UL, false)]
    [InlineData(true, 7UL, 7UL, true)]
    public void SampleEpochCurrentness_ShouldRequirePayloadAndWorldSequence(
        bool payloadCurrent,
        ulong currentWorldSequence,
        ulong expectedWorldSequence,
        bool expected)
    {
        NavigationFlowFieldGuideLease.IsSampleEpochCurrent(
                payloadCurrent,
                currentWorldSequence,
                expectedWorldSequence)
            .Should().Be(expected);
    }

    [Fact]
    public void TransitionCompletion_ShouldAtomicallyAdvanceCursorState()
    {
        var source = new NavigationCellAddress("source", default);
        var destination = new NavigationCellAddress("destination", default);
        TraversalMedium medium = TraversalMedium.Solid;
        bool pending = true;
        long ordinal = 7;

        NavigationFlowFieldGuideLease.ResolveTransitionCompletion(
                destination,
                TraversalMedium.Gas,
                nextSampleOrdinal: 8,
                ref source,
                ref medium,
                ref pending,
                ref ordinal)
            .Should().Be(NavigationGuideStatus.Success);

        source.Should().Be(destination);
        medium.Should().Be(TraversalMedium.Gas);
        pending.Should().BeFalse();
        ordinal.Should().Be(8);
    }

    [Theory]
    [InlineData((int)GridCoveredAddressCursorStatus.Stale, true, true,
        (int)NavigationGuideStatus.Stale, false)]
    [InlineData((int)GridCoveredAddressCursorStatus.More, true, false,
        (int)NavigationGuideStatus.Success, false)]
    [InlineData((int)GridCoveredAddressCursorStatus.Complete, false, true,
        (int)NavigationGuideStatus.LocalRecoveryRequired, true)]
    [InlineData((int)GridCoveredAddressCursorStatus.Complete, true, true,
        (int)NavigationGuideStatus.Success, true)]
    public void RebaseCursorStatus_ShouldResolveOnlyTerminalCursorStates(
        int cursorStatusValue,
        bool hasCandidate,
        bool expectedHandled,
        int expectedStatusValue,
        bool expectedBest)
    {
        var best = new NavigationCellAddress("best", default);

        NavigationSelectedEdgeProgressWork.TryResolveRebaseCursorStatus(
                (GridCoveredAddressCursorStatus)cursorStatusValue,
                hasCandidate,
                best,
                out NavigationGuideStatus status,
                out NavigationCellAddress rebased)
            .Should().Be(expectedHandled);

        status.Should().Be((NavigationGuideStatus)expectedStatusValue);
        rebased.Should().Be(expectedBest ? best : default);
    }

    [Theory]
    [InlineData(false, 1, "current", 2, "candidate", true)]
    [InlineData(true, 2, "current", 1, "candidate", true)]
    [InlineData(true, 1, "current", 2, "candidate", false)]
    [InlineData(true, 1, "b", 1, "a", true)]
    [InlineData(true, 1, "b", 1, "b", false)]
    [InlineData(true, 1, "b", 1, "c", false)]
    public void RebaseCandidateSelection_ShouldChooseNearestThenCanonicalAddress(
        bool initiallyHasCandidate,
        int initialDistance,
        string initialMapId,
        int candidateDistance,
        string candidateMapId,
        bool expectedCandidate)
    {
        bool hasCandidate = initiallyHasCandidate;
        Fixed64 bestDistance = (Fixed64)initialDistance;
        var best = new NavigationCellAddress(initialMapId, default);
        var candidate = new NavigationCellAddress(candidateMapId, default);

        NavigationSelectedEdgeProgressWork.SelectNearestCandidate(
            (Fixed64)candidateDistance,
            candidate,
            ref hasCandidate,
            ref bestDistance,
            ref best);

        hasCandidate.Should().BeTrue();
        bestDistance.Should().Be((Fixed64)(expectedCandidate
            ? candidateDistance
            : initialDistance));
        best.Should().Be(expectedCandidate
            ? candidate
            : new NavigationCellAddress(initialMapId, default));
    }

    [Fact]
    public void RebaseCandidateAdmission_ShouldIgnoreARealNodeInAClosedStructuralComponent()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        fixture.Graph.TryGetNodeRef(
                fixture.NearOrigin,
                out NavigationNodeRef node)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(node, out NavigationNodeState state)
            .Should().BeTrue();
        fixture.Graph.TryGetSurfaceComponent(
                fixture.NearOrigin,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey component,
                out _)
            .Should().BeTrue();
        NavigationWorldGraph closed = fixture.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(component),
            closeAllStructuralComponents: false,
            fixture.Graph.GraphVersion + 1);
        closed.TryGetNodeState(node, out _).Should().BeFalse(
            "the retained node reference belongs to a temporarily closed component");
        var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 8,
            maxCursorLegScans: 0,
            maxCursorRebases: 0,
            maxPortalChecks: 0,
            maxPrismChecks: 1,
            maxTraceIntervals: 0,
            maxLocalRecoveryAttempts: 0));
        bool hasCandidate = false;
        Fixed64 bestDistance = Fixed64.Zero;
        NavigationCellAddress best = default;

        NavigationSelectedEdgeProgressWork.ConsiderCandidateAddress(
                closed,
                fixture.Far,
                TraversalMedium.Solid,
                state.FootAnchor,
                fixture.NearOrigin,
                ref meter,
                ref hasCandidate,
                ref bestDistance,
                ref best)
            .Should().Be(NavigationGuideStatus.Success);

        hasCandidate.Should().BeFalse();
        bestDistance.Should().Be(Fixed64.Zero);
        best.Should().Be(default(NavigationCellAddress));
        meter.GetPrismCheckAllowance().Should().Be(0);
        meter.GetCurrentNodeLookupAllowance().Should().Be(8,
            "closed nodes are rejected before the payload lookup budget is touched");
    }

    [Theory]
    [InlineData(0, (int)NavigationGuideStatus.Success, true)]
    [InlineData(1, (int)NavigationGuideStatus.CostOverflow, false)]
    [InlineData(2, (int)NavigationGuideStatus.CostOverflow, false)]
    [InlineData(3, (int)NavigationGuideStatus.CostOverflow, false)]
    [InlineData(4, (int)NavigationGuideStatus.Success, false)]
    [InlineData(5, (int)NavigationGuideStatus.Success, true)]
    public void SegmentProgress_ShouldHandleDegenerateBeforeReachedAndCheckedOverflowCases(
        int stage,
        int expectedStatusValue,
        bool expectedPassed)
    {
        Vector3d start = Vector3d.Zero;
        Vector3d end = Vector3d.Right;
        Vector3d actual = Vector3d.Zero;
        switch (stage)
        {
            case 0:
                end = Vector3d.Zero;
                break;
            case 1:
                start = new Vector3d(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero);
                end = new Vector3d(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero);
                break;
            case 2:
                actual = new Vector3d(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero);
                break;
            case 3:
                Fixed64 half = Fixed64.FromRaw((long.MaxValue / 2L) + 1L);
                end = new Vector3d(half, Fixed64.Zero, Fixed64.Zero);
                actual = new Vector3d(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero);
                break;
            case 5:
                actual = Vector3d.Right;
                break;
        }

        NavigationSelectedEdgeProgressWork.HasReachedOrPassed(
                start,
                end,
                actual,
                out bool passed)
            .Should().Be((NavigationGuideStatus)expectedStatusValue);

        passed.Should().Be(expectedPassed);
    }

    [Fact]
    public void DefaultLease_ShouldFailClosedWithoutManufacturingAHeading()
    {
        NavigationFlowFieldLease lease = default;
        var meter = new GuideSampleWorkMeter(
            new GuideSampleWorkBudget(1, 1, 1, 1, 1, 1, 1));

        lease.Status.Should().Be(NavigationGuideStatus.Stale);
        lease.OriginIntegrationCost.Should().Be(Fixed64.Zero);
        lease.TrySampleHeading(
                Vector3d.Zero,
                new GuideSampleWorkBudget(1, 1, 1, 1, 1, 1, 1),
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale);
        heading.Should().Be(Vector3d.Zero);
        lease.TrySample(Vector3d.Zero, ref meter, out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Stale);
        sample.Should().Be(default(NavigationFlowSample));
        lease.CompletePendingTransition(default)
            .Should().Be(NavigationGuideStatus.Stale);
        lease.Dispose();
    }

    [Theory]
    [InlineData(0, 1, (int)NavigationGuideStatus.BudgetExceeded)]
    [InlineData(1, 0, (int)NavigationGuideStatus.BudgetExceeded)]
    [InlineData(1, 1, (int)NavigationGuideStatus.Success)]
    public void HeadingSample_ShouldRequireBothLegAndTraceWork(
        int cursorLegScans,
        int traceIntervals,
        int expectedStatus)
    {
        var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 0,
            maxCursorLegScans: cursorLegScans,
            maxCursorRebases: 0,
            maxPortalChecks: 0,
            maxPrismChecks: 0,
            maxTraceIntervals: traceIntervals,
            maxLocalRecoveryAttempts: 0));

        NavigationSelectedEdgeProgressWork.TrySetHeading(
                Vector3d.Zero,
                Vector3d.Right,
                ref meter,
                out Vector3d heading)
            .Should().Be((NavigationGuideStatus)expectedStatus);
        heading.Should().Be(expectedStatus == (int)NavigationGuideStatus.Success
            ? Vector3d.Right
            : Vector3d.Zero);
    }

    [Fact]
    public void HeadingSample_WhenRepresentableEndpointsHaveUnrepresentableDelta_ShouldOverflow()
    {
        var meter = new GuideSampleWorkMeter(
            new GuideSampleWorkBudget(0, 1, 0, 0, 0, 1, 0));
        var start = new Vector3d(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero);
        var target = new Vector3d(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero);

        NavigationSelectedEdgeProgressWork.TrySetHeading(
                start,
                target,
                ref meter,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.CostOverflow);
        heading.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void NativeSelectedEdge_ExtremeLegalPositions_ShouldFailClosedForProgressAndRecovery()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        Fixed64 cellWidth = (Fixed64)200_000;
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(cellWidth * (Fixed64)2, (Fixed64)2, Fixed64.One),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                cellWidth,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { source, destination },
                "sample-progress-overflow");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(source, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, source);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(origin, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(target, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(target, out GridCellPrism targetPrism)
            .Should().BeTrue();
        Vector3d sourceActual = sourceState.FootAnchor
            + Vector3d.Right * (cellWidth / (Fixed64)4);
        Vector3d targetActual = targetState.FootAnchor
            + Vector3d.Right * (cellWidth / (Fixed64)4);
        sourcePrism.Contains(sourceActual).Should().BeTrue();
        targetPrism.Contains(targetActual).Should().BeTrue();

        guide.TrySampleHeading(sourceActual, GenerousSampleBudget, out Vector3d sourceHeading)
            .Should().Be(NavigationGuideStatus.CostOverflow);
        guide.TrySampleHeading(targetActual, GenerousSampleBudget, out Vector3d targetHeading)
            .Should().Be(NavigationGuideStatus.CostOverflow);

        sourceHeading.Should().Be(Vector3d.Zero);
        targetHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void LocalRecovery_MaximumRectangularPrismCorner_ShouldOverflowWithoutMovingTheGuide()
    {
        using var world = new GridWorld();
        Fixed64 maximumEvenMetric = Fixed64.FromRaw(long.MaxValue - 1L);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                maximumEvenMetric,
                maximumEvenMetric,
                maximumEvenMetric),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex cell = default;
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { cell },
                "sample-recovery-distance-overflow");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(cell, cell, fixture.DefaultProfile),
            Fixed64.Zero);
        var address = new NavigationCellAddress(fixture.MapId, cell);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            address,
            address,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            address);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(address, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(address, out NavigationNodeRef node)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(node, out NavigationNodeState state)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(address, out GridCellPrism prism)
            .Should().BeTrue();
        Vector2d oppositePlanarCorner = prism.GetFootprintVertex(2);
        var actualFoot = new Vector3d(
            oppositePlanarCorner.X,
            prism.VerticalMax,
            oppositePlanarCorner.Y);
        prism.Contains(actualFoot).Should().BeTrue();
        Vector3d.TryGetDistance(actualFoot, state.FootAnchor, out Fixed64 distance)
            .Should().BeFalse();
        distance.Should().Be(Fixed64.MaxValue);

        guide.TrySample(
                actualFoot,
                GenerousSampleBudget,
                out NavigationFlowSample overflowSample)
            .Should().Be(NavigationGuideStatus.CostOverflow);

        overflowSample.Heading.Should().Be(Vector3d.Zero);
        overflowSample.Target.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Success,
            "a recoverable cost overflow must not invalidate or advance the lease");
        guide.TrySample(
                state.FootAnchor,
                GenerousSampleBudget,
                out NavigationFlowSample retrySample)
            .Should().Be(NavigationGuideStatus.Success);
        retrySample.Target.Should().Be(state.FootAnchor);
        retrySample.Heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitSelectedEdge_ExtremeLegalPositions_ShouldReportEachProgressOverflow(
        int progressLeg)
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        Fixed64 cellWidth = (Fixed64)200_000;
        Fixed64 halfLeg = cellWidth / (Fixed64)2;
        Vector3d entryOffset = progressLeg == 0
            ? Vector3d.Right * halfLeg
            : Vector3d.Zero;
        Vector3d exitOffset = progressLeg == 2
            ? Vector3d.Left * halfLeg
            : Vector3d.Zero;
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(cellWidth * (Fixed64)2, (Fixed64)2, Fixed64.One),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                cellWidth,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                configuration,
                new[] { source, destination },
                $"sample-explicit-progress-{progressLeg}",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "extreme-progress",
                        source,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.One,
                        entryOffset,
                        exitOffset)
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(source, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, source);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        payload.TryGetNode(
                origin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode originNode)
            .Should().BeTrue();
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator outgoing =
            fixture.Graph.EnumerateStructuralSurfaceEdges(sourceRef);
        NavigationGraphEdge selectedEdge = default;
        while (outgoing.MoveNext())
        {
            if (outgoing.CurrentOrdinal == originNode.SelectedEdge.CanonicalOutgoingOrdinal)
                selectedEdge = outgoing.Current;
        }
        selectedEdge.Kind.Should().Be(NavigationGraphEdgeKind.Explicit,
            "the explicit corridor is cheaper than the giant native crossing");
        fixture.Graph.TryGetNodeRef(target, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        Vector3d actual = progressLeg == 0
            ? sourceState.FootAnchor + Vector3d.Right * (halfLeg / (Fixed64)2)
            : targetState.FootAnchor - Vector3d.Right * (halfLeg / (Fixed64)2);

        guide.TrySampleHeading(actual, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.CostOverflow);
        heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void LocalRecoveryStatus_ShouldAppendWithoutRenumberingExistingValues()
    {
        ((byte)NavigationGuideStatus.Success).Should().Be(0);
        ((byte)NavigationGuideStatus.Stale).Should().Be(10);
        ((byte)NavigationGuideStatus.LocalRecoveryRequired).Should().Be(11);
    }

    [Fact]
    public void CacheTransfer_ShouldSampleNativeSelectedEdgeFromActualFoot()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        NavigationFlowFieldPayloadLease staleAlias = payloadLease;
        fixture.Far.TryGetNode(
                fixture.FarOrigin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode originNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                originNode.SelectedEdge.Target,
                out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState target)
            .Should().BeTrue();

        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);

        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.OriginIntegrationCost.Should().Be(originNode.IntegrationCost);
        staleAlias.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        var noNodeLookupBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 0,
            maxCursorLegScans: 128,
            maxCursorRebases: 128,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 128);
        guide.TrySampleHeading(
                source.FootAnchor,
                noNodeLookupBudget,
                out Vector3d lookupBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        lookupBlockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().Be((target.FootAnchor - source.FootAnchor).Normalized);
        fixture.Graph.TryGetSeamPrism(
                fixture.FarOrigin,
                out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                originNode.SelectedEdge.Target,
                out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                fixture.Far.Key.Agent.Shape.Radius,
                fixture.Far.Key.Agent.Shape.Height,
                out Vector3d sourcePortal,
                out Vector3d targetPortal)
            .Should().BeTrue();
        sourcePortal.Should().Be(targetPortal);

        guide.TrySampleHeading(sourcePortal, GenerousSampleBudget, out heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().Be((target.FootAnchor - sourcePortal).Normalized,
            "crossing the selected portal must advance the directed leg into its target cell");

        NavigationFlowFieldLease copied = guide;
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        copied.Status.Should().Be(NavigationGuideStatus.Stale);
        copied.Dispose();
        staleAlias.Dispose();
    }

    [Fact]
    public void VolumeSelectedEdge_ShouldGuideCenteredAnchorsAndDebitTheTargetRebase()
    {
        using var world = new GridWorld();
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(1, 0, 0);
        NavigationCell gas = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { sourceIndex, targetIndex },
                "volume-guide",
                new[] { gas, gas });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 4);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        PathQuery query = new(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, sourceIndex),
                fixture.MapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, targetIndex),
                fixture.MapId),
            profile,
            NavigationAStarExitTestHarness.Policy.Key,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(
                8_192, 32, 128, 1_024, 1_024, 0, 0, 0, 0, 1_024, 0),
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));
        var sourceAddress = new NavigationCellAddress(fixture.MapId, sourceIndex);
        var targetAddress = new NavigationCellAddress(fixture.MapId, targetIndex);
        NavigationFlowFieldPayload payload = RunVolumeFlow(
            world,
            store,
            fixture.Graph,
            query,
            sourceAddress,
            targetAddress);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef targetNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                targetNode,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceAnchor)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d targetAnchor)
            .Should().BeTrue();
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            sourceAddress);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(sourceAddress, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);

        var noNodeLookupBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 0,
            maxCursorLegScans: 128,
            maxCursorRebases: 8,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0);
        guide.TrySampleHeading(
                sourceAnchor,
                noNodeLookupBudget,
                out Vector3d lookupBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        lookupBlockedHeading.Should().Be(Vector3d.Zero);
        var noRayTraceBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 8,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 0,
            maxLocalRecoveryAttempts: 0);
        guide.TrySampleHeading(
                sourceAnchor,
                noRayTraceBudget,
                out Vector3d traceBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        traceBlockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                sourceAnchor,
                GenerousSampleBudget,
                out Vector3d sourceHeading)
            .Should().Be(NavigationGuideStatus.Success);
        sourceHeading.Should().Be((targetAnchor - sourceAnchor).Normalized);
        var noCursorRebaseBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0);
        guide.TrySampleHeading(
                targetAnchor,
                noRayTraceBudget,
                out Vector3d arrivedTraceBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        arrivedTraceBlockedHeading.Should().Be(Vector3d.Zero,
            "certifying an exact selected-edge target still consumes ray work before rebasing");
        guide.TrySampleHeading(
                targetAnchor,
                noCursorRebaseBudget,
                out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                targetAnchor,
                GenerousSampleBudget,
                out Vector3d arrivedHeading)
            .Should().Be(NavigationGuideStatus.Success);
        arrivedHeading.Should().Be(Vector3d.Zero);
        Vector3d destinationOffset = targetAnchor
            + Vector3d.Right * (Fixed64.One / (Fixed64)8);
        guide.TrySampleHeading(
                destinationOffset,
                noRayTraceBudget,
                out Vector3d destinationTraceBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        destinationTraceBlockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                destinationOffset,
                GenerousSampleBudget,
                out Vector3d destinationHeading)
            .Should().Be(NavigationGuideStatus.Success);
        destinationHeading.Should().Be((targetAnchor - destinationOffset).Normalized);
        guide.Dispose();
    }

    [Fact]
    public void Sample_ExhaustedGraphLeaseCapacity_ShouldRetryWithoutCursorMutation()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        var graphLeases = new NavigationWorldGraphLease?[8];
        try
        {
            for (int i = 0; i < graphLeases.Length; i++)
                graphLeases[i] = TestRequire.NotNull(fixture.Store.TryAcquire());

            guide.TrySampleHeading(
                    source.FootAnchor,
                    GenerousSampleBudget,
                    out Vector3d blockedHeading)
                .Should().Be(NavigationGuideStatus.CapacityExceeded);
            blockedHeading.Should().Be(Vector3d.Zero);
        }
        finally
        {
            for (int i = 0; i < graphLeases.Length; i++)
                graphLeases[i]?.Dispose();
        }

        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().NotBe(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void Sample_DependencyChangedAfterGuideCreation_ShouldMarkGuideStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        NavigationWorldGraph changed = fixture.Graph.WithAreaCatalog(
            NavigationAreaCatalog.Empty,
            fixture.Graph.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);

        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale);

        heading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SelectedEdgeSample_WithStaleDependency_ShouldStopBeforeGeometryWork()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        var meter = new GuideSampleWorkMeter(GenerousSampleBudget);

        NavigationSelectedEdgeProgressWork.TrySample(
                dependencyCurrent: false,
                fixture.World,
                fixture.Store,
                fixture.Graph,
                fixture.Far,
                fixture.FarOrigin,
                TraversalMedium.Solid,
                source.FootAnchor,
                ref meter,
                new GridCoveredAddressCursor(1),
                new GridCoveredAddressGeneration[1],
                new GridCoveredAddress[8],
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace(),
                out NavigationFlowFieldNode currentNode,
                out NavigationCellAddress nextSource,
                out Vector3d target,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale);

        currentNode.Should().Be(default(NavigationFlowFieldNode));
        nextSource.Should().Be(fixture.FarOrigin);
        target.Should().Be(Vector3d.Zero);
        heading.Should().Be(Vector3d.Zero);
        meter.GetCurrentNodeLookupAllowance().Should().Be(
            GenerousSampleBudget.MaxCurrentNodeLookupProbes,
            "dependency rejection must precede node and geometry work");
    }

    [Fact]
    public void ExactNodeRebase_ToDestination_ShouldSucceedWithZeroHeading()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();

        guide.TrySampleHeading(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
    }

    [Fact]
    public void DestinationArrivalRadius_ShouldBeInclusiveForSurfaceSampling()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        NavigationAgentProfile baseline = fixture.FarQuery.Agent;
        var profile = new NavigationAgentProfile(
            baseline.Shape,
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            Fixed64.Quarter,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery source = fixture.FarQuery;
        var query = new PathQuery(
            source.Start,
            source.End,
            profile,
            source.AreaPolicy,
            source.Traversal,
            source.Algorithm,
            source.Budget,
            source.AllowTransitions,
            source.FlowField);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.Clone(
            fixture.Far,
            new NavigationFlowFieldPayloadKey(
                query,
                fixture.Far.Key.DestinationAddress,
                fixture.Far.Key.StartMedium,
                fixture.Far.Key.TargetMedia));
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture.Store,
            payload,
            fixture.FarOrigin);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                payload.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        Vector3d outside = destination.FootAnchor
            + Vector3d.Right * (profile.ArrivalRadius + Fixed64.FromRaw(1));
        Vector3d boundary = destination.FootAnchor
            + Vector3d.Right * profile.ArrivalRadius;

        guide.TrySampleHeading(outside, GenerousSampleBudget, out Vector3d outsideHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(boundary, GenerousSampleBudget, out Vector3d boundaryHeading)
            .Should().Be(NavigationGuideStatus.Success);

        outsideHeading.Should().Be((destination.FootAnchor - outside).Normalized);
        boundaryHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void DestinationRecovery_DisplacedOffMap_ShouldRayRejoinTheSameLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                fixture.Far.Key.DestinationAddress,
                out GridCellPrism destinationPrism)
            .Should().BeTrue();
        Vector3d actualFoot = destination.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        destinationPrism.Contains(actualFoot).Should().BeFalse();

        guide.TrySampleHeading(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d arrivedHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(
                actualFoot,
                GenerousSampleBudget,
                out Vector3d recoveryHeading)
            .Should().Be(NavigationGuideStatus.Success);

        arrivedHeading.Should().Be(Vector3d.Zero);
        recoveryHeading.Should().Be(
            (destination.FootAnchor - actualFoot).Normalized);
        Vector3d oppositeSide = destination.FootAnchor + Vector3d.Right * (Fixed64)5;
        guide.TrySampleHeading(
                oppositeSide,
                GenerousSampleBudget,
                out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired,
                "destination recovery cannot rejoin through mapped cells outside its source-only chain");
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
    }

    [Fact]
    public void DestinationRecovery_WhenHostReturnsToMappedSource_ShouldRebaseAndResume()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Far.TryGetNode(
                fixture.FarOrigin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode sourceFlow)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                sourceFlow.SelectedEdge.Target,
                out NavigationNodeRef selectedTargetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                selectedTargetRef,
                out NavigationNodeState selectedTarget)
            .Should().BeTrue();

        guide.TrySampleHeading(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d arrivedHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d resumedHeading)
            .Should().Be(NavigationGuideStatus.Success);

        arrivedHeading.Should().Be(Vector3d.Zero);
        resumedHeading.Should().Be(
            (selectedTarget.FootAnchor - source.FootAnchor).Normalized,
            "a host correction onto an earlier mapped payload node must rewind and resume the same guide");
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(2, 0)]
    [InlineData(4, 1)]
    [InlineData(6, 0)]
    public void DestinationRecovery_MappedRebaseShouldDebitExactWorkBeforeCursorMutation(
        int category,
        int allowance)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        guide.TrySampleHeading(
                destination.FootAnchor,
                GenerousSampleBudget,
                out _)
            .Should().Be(NavigationGuideStatus.Success);
        var limits = new[] { 128, 128, 8, 32, 32, 32, 1 };
        limits[category] = allowance;
        var budget = new GuideSampleWorkBudget(
            limits[0],
            limits[1],
            limits[2],
            limits[3],
            limits[4],
            limits[5],
            limits[6]);

        guide.TrySampleHeading(source.FootAnchor, budget, out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);

        blockedHeading.Should().Be(Vector3d.Zero);
        retryHeading.Should().NotBe(Vector3d.Zero,
            "a failed mapped rebase must leave the destination cursor retryable");
        guide.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetOnlyFoot_ShouldFinishCurrentSelectedEdgeBeforeAdvanceOrArrival(
        bool targetIsDestination)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayload payload = targetIsDestination
            ? fixture.Near
            : fixture.Far;
        NavigationCellAddress origin = targetIsDestination
            ? fixture.NearOrigin
            : fixture.FarOrigin;
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture.Store,
            payload,
            origin);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        payload.TryGetNode(
                origin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                sourceNode.SelectedEdge.Target,
                out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(origin, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                sourceNode.SelectedEdge.Target,
                out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                payload.Key.Agent.Shape.Radius,
                payload.Key.Agent.Shape.Height,
                out _,
                out Vector3d targetPortal)
            .Should().BeTrue();
        Vector3d actualFoot = targetPortal
            + portal.SourceToTarget.Normalized * (Fixed64.One / (Fixed64)8)
            + new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)8);
        sourcePrism.Contains(actualFoot).Should().BeFalse();
        targetPrism.Contains(actualFoot).Should().BeTrue();
        var noRecoveryBudget = new GuideSampleWorkBudget(
            128,
            128,
            8,
            32,
            32,
            32,
            0);

        guide.TrySampleHeading(actualFoot, noRecoveryBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((targetState.FootAnchor - actualFoot).Normalized,
            "target-only containment remains part of the current selected edge until its anchor is reached");
        guide.TrySampleHeading(
                sourceState.FootAnchor,
                noRecoveryBudget,
                out Vector3d reentryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        reentryHeading.Should().NotBe(Vector3d.Zero,
            "retreat through the selected portal must rewind without local recovery");
        Vector3d overshotTarget = targetState.FootAnchor
            + portal.SourceToTarget.Normalized * (Fixed64.One / (Fixed64)8);
        var noCursorRebaseBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 32,
            maxPrismChecks: 32,
            maxTraceIntervals: 32,
            maxLocalRecoveryAttempts: 0);
        guide.TrySampleHeading(
                overshotTarget,
                noCursorRebaseBudget,
                out Vector3d rebaseBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        rebaseBlockedHeading.Should().Be(Vector3d.Zero,
            "passing a selected native edge must debit cursor-rebase work before advancing");
        guide.Dispose();
    }

    [Fact]
    public void BudgetFailure_ShouldNotMutateCursorAndRetryShouldMatchFreshGuide()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = new(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 8,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease firstPayload = Publish(cache, fixture);
        NavigationFlowFieldPayloadLease secondPayload;
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out secondPayload,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, firstPayload),
                out NavigationFlowFieldLease retryGuide)
            .Should().Be(NavigationGuideStatus.Success);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, secondPayload),
                out NavigationFlowFieldLease freshGuide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();

        retryGuide.TrySampleHeading(
                source.FootAnchor,
                default,
                out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        retryGuide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        freshGuide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d freshHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().Be(freshHeading);

        retryGuide.Dispose();
        freshGuide.Dispose();
    }

    [Fact]
    public void ZeroWitnessExplicit_AtOffAxisEntry_ShouldReplayPortalBeforeExit()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                new[] { start, destination },
                "sample-explicit",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "bridge",
                        start,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.Zero,
                        entryOffset: new Vector3d(
                            Fixed64.Zero,
                            Fixed64.Zero,
                            -Fixed64.One / (Fixed64)4),
                        exitOffset: new Vector3d(
                            Fixed64.Zero,
                            Fixed64.Zero,
                            -Fixed64.One / (Fixed64)4))
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            baseline.Shape,
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            Fixed64.Quarter,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(start, destination, profile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(target, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        fixture.Graph.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey(fixture.MapId, "bridge"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        GridNavigationPortal portal = record.NavigationPortals[0];
        portal.TryResolveProfile(
                query.Agent.Shape.Radius,
                query.Agent.Shape.Height,
                out Vector3d sourcePortal,
                out Vector3d targetPortal)
            .Should().BeTrue();
        payload.TryGetNode(
                origin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode flowNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges =
            fixture.Graph.EnumerateStructuralSurfaceEdges(sourceRef);
        NavigationGraphEdge selectedEdge = default;
        while (edges.MoveNext())
        {
            if (edges.CurrentOrdinal == flowNode.SelectedEdge.CanonicalOutgoingOrdinal)
                selectedEdge = edges.Current;
        }
        selectedEdge.Kind.Should().Be(NavigationGraphEdgeKind.Explicit);
        Vector3d selectedExitTarget = record.Definition.ExitAnchor;
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                origin,
                source,
                targetState,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 1,
                out NavigationFlowRejoinTarget exitTarget)
            .Should().BeTrue();
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                origin,
                source,
                targetState,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 2,
                out NavigationFlowRejoinTarget destinationTarget)
            .Should().BeTrue();
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                origin,
                source,
                targetState,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 3,
                out _)
            .Should().BeFalse();
        exitTarget.Position.Should().Be(record.Definition.ExitAnchor);
        destinationTarget.Position.Should().Be(targetState.FootAnchor);
        Vector3d entry = source.FootAnchor + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            -Fixed64.One / (Fixed64)4);

        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d entryApproachHeading)
            .Should().Be(NavigationGuideStatus.Success);
        entryApproachHeading.Should().Be((entry - source.FootAnchor).Normalized);

        guide.TrySampleHeading(
                entry,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((sourcePortal - entry).Normalized);
        targetPortal.Should().Be(sourcePortal,
            "the contact portal resolves to one shared crossing anchor");

        guide.TrySampleHeading(
                sourcePortal,
                GenerousSampleBudget,
                out heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().Be(
            (record.Definition.ExitAnchor - sourcePortal).Normalized,
            "a zero-width portal is already crossed at its resolved anchor");

        Vector3d withinArrival = targetState.FootAnchor
            - Vector3d.Right * profile.ArrivalRadius;
        Vector3d outsideArrival = targetState.FootAnchor
            - Vector3d.Right * (profile.ArrivalRadius + Fixed64.FromRaw(1));
        guide.TrySampleHeading(outsideArrival, GenerousSampleBudget, out heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().NotBe(Vector3d.Zero);
        guide.TrySampleHeading(withinArrival, GenerousSampleBudget, out heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void ZeroWitnessHorizontalExplicit_ShouldResolvePortalForSmallerQueryBody()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var destination = new VoxelIndex(0, 1, 0);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(Fixed64.One, (Fixed64)4, Fixed64.One),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.One,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                configuration,
                new[] { start, destination },
                "sample-explicit-horizontal",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "lift",
                        start,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.Zero,
                        entryOffset: new Vector3d(
                            -Fixed64.One / (Fixed64)4,
                            Fixed64.Zero,
                            Fixed64.Zero),
                        exitOffset: new Vector3d(
                            -Fixed64.One / (Fixed64)4,
                            Fixed64.Zero,
                            Fixed64.Zero))
                });
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Zero,
                Fixed64.One / (Fixed64)2,
                Fixed64.Zero),
            (Fixed64)2,
            (Fixed64)2,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(start, destination, profile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey(fixture.MapId, "lift"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        GridNavigationPortal portal = record.NavigationPortals[0];
        portal.FaceKind.Should().Be(VoxelContactFaceKind.Horizontal);
        portal.TryResolveProfile(
                query.Agent.Shape.Radius,
                query.Agent.Shape.Height,
                out Vector3d sourcePortal,
                out _)
            .Should().BeTrue();
        Vector3d entry = source.FootAnchor + new Vector3d(
            -Fixed64.One / (Fixed64)4,
            Fixed64.Zero,
            Fixed64.Zero);

        guide.TrySampleHeading(entry, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((sourcePortal - entry).Normalized);
        guide.Dispose();
    }

    [Fact]
    public void ExplicitSelectedEdge_AfterDirectedExitCrossing_ShouldContinueOnTargetSelectedEdge()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var explicitTarget = new VoxelIndex(1, 0, 0);
        var destination = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                new[] { start, explicitTarget, destination },
                "sample-explicit-progress",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "bridge",
                        start,
                        explicitTarget,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.Zero)
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(start, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var explicitTargetAddress = new NavigationCellAddress(
            fixture.MapId,
            explicitTarget);
        var destinationAddress = new NavigationCellAddress(
            fixture.MapId,
            destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            destinationAddress,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, start);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                explicitTargetAddress,
                out NavigationNodeRef explicitTargetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                explicitTargetRef,
                out NavigationNodeState explicitTargetState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                destinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destinationState)
            .Should().BeTrue();
        Vector3d displacedFoot = sourceState.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        sourcePrism.Contains(displacedFoot).Should().BeFalse();

        guide.TrySampleHeading(
                displacedFoot,
                GenerousSampleBudget,
                out Vector3d rejoinHeading)
            .Should().Be(NavigationGuideStatus.Success);

        guide.TrySampleHeading(
                explicitTargetState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        rejoinHeading.Should().Be(Vector3d.Backward);
        heading.Should().Be(
            (destinationState.FootAnchor - explicitTargetState.FootAnchor).Normalized);
        guide.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AutomaticSeam_ShouldSampleProfileResolvedDirectedPortal(
        bool stacked,
        bool reverse)
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        NavigationAgentProfile profile = stacked
            ? new NavigationAgentProfile(
                fixture.DefaultProfile.Shape,
                maxStepUp: (Fixed64)2,
                maxDropDown: (Fixed64)2,
                fixture.DefaultProfile.ArrivalRadius,
                fixture.DefaultProfile.AllowedMedia,
                fixture.DefaultProfile.Capabilities)
            : fixture.DefaultProfile;
        PathQuery surfaceQuery = fixture.CreateQuery(profile);
        if (reverse)
        {
            surfaceQuery = new PathQuery(
                surfaceQuery.End,
                surfaceQuery.Start,
                surfaceQuery.Agent,
                surfaceQuery.AreaPolicy,
                surfaceQuery.Traversal,
                surfaceQuery.Algorithm,
                surfaceQuery.Budget,
                surfaceQuery.AllowTransitions,
                surfaceQuery.FlowField);
        }
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            surfaceQuery,
            Fixed64.Zero);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        NavigationCellAddress origin = reverse ? targetAddress : sourceAddress;
        NavigationCellAddress destination = reverse ? sourceAddress : targetAddress;
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            fixture.Context.World,
            store,
            fixture.Graph,
            query,
            origin,
            destination,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.Context.World,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        payload.TryGetNode(
                origin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode flowNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(destination, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges =
            fixture.Graph.EnumerateStructuralSurfaceEdges(sourceRef);
        NavigationGraphEdge selectedEdge = default;
        while (edges.MoveNext())
        {
            if (edges.CurrentOrdinal == flowNode.SelectedEdge.CanonicalOutgoingOrdinal)
                selectedEdge = edges.Current;
        }
        selectedEdge.Kind.Should().Be(NavigationGraphEdgeKind.Seam);
        selectedEdge.AutomaticSeam.Portal.TryResolveProfile(
                query.Agent.Shape.Radius,
                query.Agent.Shape.Height,
                out Vector3d firstPortal,
                out Vector3d secondPortal)
            .Should().BeTrue();
        Vector3d sourcePortal = selectedEdge.AutomaticSeam.IsReverse
            ? secondPortal
            : firstPortal;
        Vector3d expectedPortal = selectedEdge.AutomaticSeam.IsReverse
            ? firstPortal
            : secondPortal;
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                origin,
                sourceState,
                targetState,
                flowNode.SelectedEdge,
                expectedPortal,
                targetOrdinal: 1,
                out NavigationFlowRejoinTarget rejoinTarget)
            .Should().BeTrue();
        rejoinTarget.Position.Should().Be(expectedPortal);
        Vector3d actualStart = reverse ? fixture.End : fixture.Start;
        Vector3d actualEnd = reverse ? fixture.Start : fixture.End;

        guide.TrySampleHeading(
                actualStart,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((actualEnd - actualStart).Normalized);
        if (stacked)
        {
            sourcePortal.Should().NotBe(expectedPortal,
                "the stacked seam exposes a real directed portal leg");
            guide.TrySampleHeading(
                    sourcePortal,
                    GenerousSampleBudget,
                    out Vector3d portalLegHeading)
                .Should().Be(NavigationGuideStatus.Success);
            portalLegHeading.Should().Be((expectedPortal - sourcePortal).Normalized,
                "a stacked seam must target its directed exit portal before the destination foot");
        }
        guide.Dispose();
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexNativeSelectedEdge_ShouldSampleCertifiedPortalFromOffCenterFoot(
        HexOrientation orientation)
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            new Vector3d(-8, 0, -8),
            new Vector3d(8, 2, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex source = FindHexCellWithNeighbor(binding, HexDirection.QPositive);
        VoxelIndex offset = HexDirectionUtility.GetOffset(HexDirection.QPositive);
        var target = new VoxelIndex(
            source.x + offset.x,
            source.y + offset.y,
            source.z + offset.z);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { source, target },
                $"sample-hex-{orientation}");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(source, target, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, source);
        var destination = new NavigationCellAddress(fixture.MapId, target);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            destination,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(destination, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        binding.TryGetCellPrism(source, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(target, out GridCellPrism targetPrism)
            .Should().BeTrue();
        Vector3d actualFoot = sourceState.FootAnchor + new Vector3d(
            Fixed64.One / (Fixed64)8,
            Fixed64.Zero,
            Fixed64.One / (Fixed64)8);
        sourcePrism.Contains(actualFoot).Should().BeTrue();
        Vector3d expected = SampleAdjacentCorridor(
            sourcePrism,
            targetPrism,
            actualFoot,
            targetState.FootAnchor,
            query.Agent.Shape);

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(expected);
        guide.Dispose();
    }

    [Fact]
    public void PositiveRadiusNativeCrossing_ShouldTreatSelectedPortalAsOpen()
    {
        using var world = new GridWorld();
        VoxelIndex destination = default;
        var originIndex = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { destination, originIndex },
                "sample-radius");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var shape = new KinematicBodyShape(
            Fixed64.One / (Fixed64)4,
            baseline.Shape.Height,
            baseline.Shape.RootToFootOffsetY);
        var profile = new NavigationAgentProfile(
            shape,
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(originIndex, destination, profile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, originIndex);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetSeamPrism(origin, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(target, out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d sourcePortal,
                out _)
            .Should().BeTrue();
        Vector3d actualFoot = sourcePortal
            - portal.SourceToTarget.Normalized * (Fixed64.One / (Fixed64)8);
        sourcePrism.Contains(actualFoot).Should().BeTrue();
        targetPrism.Contains(actualFoot).Should().BeFalse();

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((sourcePortal - actualFoot).Normalized);
        guide.Dispose();
    }

    [Fact]
    public void WeightedNativeRejoin_ShouldAcceptTheAlreadySelectedEdgeAfterSourceRayIsBlocked()
    {
        using var world = new GridWorld();
        VoxelIndex destination = default;
        var originIndex = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { destination, originIndex },
                "sample-weighted-rejoin",
                new[]
                {
                    NavigationAStarExitTestHarness.ExpensiveCell,
                    NavigationAStarExitTestHarness.Cell
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var shape = new KinematicBodyShape(
            Fixed64.One / (Fixed64)4,
            baseline.Shape.Height,
            baseline.Shape.RootToFootOffsetY);
        var profile = new NavigationAgentProfile(
            shape,
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(originIndex, destination, profile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, originIndex);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(origin, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(target, out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out _,
                out Vector3d targetPortal)
            .Should().BeTrue();
        Vector3d actualFoot = source.FootAnchor + Vector3d.Right + Vector3d.Backward;
        sourcePrism.Contains(actualFoot).Should().BeFalse();
        targetPrism.Contains(actualFoot).Should().BeFalse();

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((targetPortal - actualFoot).Normalized);
        guide.Dispose();
    }

    [Fact]
    public void PositiveRadiusNativeSample_ShouldRejectOverlapWithNonSelectedWall()
    {
        using var world = new GridWorld();
        VoxelIndex destination = default;
        var originIndex = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { destination, originIndex },
                "sample-radius-wall");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var shape = new KinematicBodyShape(
            Fixed64.One / (Fixed64)4,
            baseline.Shape.Height,
            baseline.Shape.RootToFootOffsetY);
        var profile = new NavigationAgentProfile(
            shape,
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(originIndex, destination, profile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, originIndex);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(origin, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(target, out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState targetState)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(target, out GridCellPrism targetPrism)
            .Should().BeTrue();
        Vector3d actualFoot = sourceState.FootAnchor + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)3 / (Fixed64)8);
        sourcePrism.Contains(actualFoot).Should().BeTrue();

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);

        heading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                targetState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d arrivedHeading)
            .Should().Be(NavigationGuideStatus.Success);
        arrivedHeading.Should().Be(Vector3d.Zero);
        Vector3d outsideCorner = targetState.FootAnchor + new Vector3d(
            -((Fixed64)3 / (Fixed64)4),
            Fixed64.Zero,
            (Fixed64)3 / (Fixed64)4);
        targetPrism.Contains(outsideCorner).Should().BeFalse();

        guide.TrySampleHeading(
                outsideCorner,
                GenerousSampleBudget,
                out Vector3d cornerHeading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired,
                "a positive-radius body cannot rejoin a destination through its outside corner");
        cornerHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexRebase_ShouldRejectBroadPhaseCandidateOutsideExactPrism(
        HexOrientation orientation)
    {
        using var world = new GridWorld();
        Fixed64 radius = (Fixed64)2;
        GridConfiguration configuration = new(
            new Vector3d(-8, 0, -8),
            new Vector3d(8, 2, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(radius, (Fixed64)2, orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex destination = FindHexCellWithNeighbor(
            binding,
            HexDirection.QPositive);
        VoxelIndex offset = HexDirectionUtility.GetOffset(HexDirection.QPositive);
        var originIndex = new VoxelIndex(
            destination.x + offset.x,
            destination.y + offset.y,
            destination.z + offset.z);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { destination, originIndex },
                $"sample-rebase-{orientation}");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(originIndex, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, originIndex);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            1,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        binding.TryGetCellPrism(destination, out GridCellPrism destinationPrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(originIndex, out GridCellPrism originPrism)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef originRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(originRef, out NavigationNodeState originState)
            .Should().BeTrue();
        Vector3d[] corners =
        {
            new(destinationPrism.Center.X + radius, destinationPrism.VerticalMin,
                destinationPrism.Center.Z + radius),
            new(destinationPrism.Center.X + radius, destinationPrism.VerticalMin,
                destinationPrism.Center.Z - radius),
            new(destinationPrism.Center.X - radius, destinationPrism.VerticalMin,
                destinationPrism.Center.Z + radius),
            new(destinationPrism.Center.X - radius, destinationPrism.VerticalMin,
                destinationPrism.Center.Z - radius)
        };
        Vector3d actualFoot = default;
        Fixed64 bestOriginDistance = Fixed64.Zero;
        bool found = false;
        for (int i = 0; i < corners.Length; i++)
        {
            if (destinationPrism.Contains(corners[i])
                || originPrism.Contains(corners[i])
                || !Vector3d.TryGetDistance(
                    corners[i],
                    originState.FootAnchor,
                    out Fixed64 distance))
            {
                continue;
            }
            if (!found || distance > bestOriginDistance)
            {
                found = true;
                bestOriginDistance = distance;
                actualFoot = corners[i];
            }
        }
        found.Should().BeTrue();

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);

        heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void ExplicitMultiWitnessSelectedEdge_ShouldFollowActualLegAndRewindOnRetreat()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 1, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex source = default;
        var firstWitness = new VoxelIndex(1, 0, 0);
        var secondWitness = new VoxelIndex(2, 0, 0);
        var destination = new VoxelIndex(3, 0, 0);
        var mappedRebase = new VoxelIndex(0, 0, 1);
        var excludedPrefix = new VoxelIndex(1, 0, 1);
        Vector3d sourceFoot = NavigationAStarExitTestHarness.GetFoot(binding, source);
        Vector3d destinationFoot = NavigationAStarExitTestHarness.GetFoot(
            binding,
            destination);
        var connection = new NavigationConnection(
            "sample-corridor",
            source,
            new NavigationCellAddress("sample-multi", destination),
            sourceFoot + new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)4),
            destinationFoot - new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)4),
            Fixed64.One / (Fixed64)4,
            Fixed64.One,
            new[]
            {
                new NavigationCellAddress("sample-multi", firstWitness),
                new NavigationCellAddress("sample-multi", secondWitness)
            });
        NavigationCell ordinary = NavigationAStarExitTestHarness.Cell;
        var expensiveWitness = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            (Fixed64)100,
            (Fixed64)4,
            (Fixed64)4);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        SimulateUntilTerminal(context, policyOperation.Receipt);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("sample-multi", binding)
                    .AddCell(source, ordinary)
                    .AddCell(firstWitness, expensiveWitness)
                    .AddCell(secondWitness, expensiveWitness)
                    .AddCell(destination, ordinary)
                    .AddCell(mappedRebase, ordinary)
                    .AddCell(excludedPrefix, ordinary)
                    .AddConnection(connection)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        SimulateUntilTerminal(context, mapOperation.Receipt);
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        NavigationWorldGraph graph = store.Current;
        NavigationAgentProfile baseline = NavigationAStarExitTestHarness.Profile();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            NavigationAStarExitTestHarness.Query(
                sourceFoot,
                "sample-multi",
                destinationFoot,
                "sample-multi",
                profile),
            Fixed64.One);
        var origin = new NavigationCellAddress("sample-multi", source);
        var destinationAddress = new NavigationCellAddress("sample-multi", destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            context.World,
            store,
            graph,
            query,
            origin,
            destinationAddress,
            NavigationFlowFieldStatus.Success);
        payload.TryGetNode(
                origin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode originNode)
            .Should().BeTrue();
        var mappedRebaseAddress = new NavigationCellAddress(
            "sample-multi",
            mappedRebase);
        payload.TryGetNode(
                mappedRebaseAddress,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode mappedRebaseNode)
            .Should().BeTrue();
        mappedRebaseNode.SelectedEdge.Target.Should().Be(origin,
            "the added mapped cell has one legal native continuation into the explicit source");
        var excludedPrefixAddress = new NavigationCellAddress(
            "sample-multi",
            excludedPrefix);
        payload.TryGetNode(
                excludedPrefixAddress,
                TraversalMedium.Solid,
                out _)
            .Should().BeFalse(
                "the field closes exactly one integration-cost unit beyond its settled origin");
        originNode.SelectedEdge.Target.Should().Be(destinationAddress,
            "the explicit connection must beat the expensive native witness route");
        graph.TryGetNodeRef(origin, out NavigationNodeRef originRef)
            .Should().BeTrue();
        graph.TryGetNodeState(originRef, out NavigationNodeState originState)
            .Should().BeTrue();
        graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        graph.TryGetNodeState(destinationRef, out NavigationNodeState destinationState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator outgoing = graph.EnumerateStructuralSurfaceEdges(
            originRef);
        NavigationGraphEdge selectedEdge = default;
        while (outgoing.MoveNext())
        {
            if (outgoing.CurrentOrdinal == originNode.SelectedEdge.CanonicalOutgoingOrdinal)
                selectedEdge = outgoing.Current;
        }
        selectedEdge.Kind.Should().Be(NavigationGraphEdgeKind.Explicit);
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                origin,
                originState,
                destinationState,
                originNode.SelectedEdge,
                selectedEdge.ExplicitConnection.Definition.ExitAnchor,
                targetOrdinal: 1,
                out NavigationFlowRejoinTarget exitTarget)
            .Should().BeTrue();
        exitTarget.Position.Should().Be(
            selectedEdge.ExplicitConnection.Definition.ExitAnchor);
        using var cache = new NavigationFlowFieldPayloadCache(
            context.World,
            1,
            payload.RetainedBytes,
            payload.RetainedBytes,
            payload.RetainedBytes,
            2,
            8,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            store,
            payload,
            origin);
        cache.TryCheckout(
                store,
                store.Current,
                payload.Key,
                origin,
                out NavigationFlowFieldPayloadLease rebasePayloadLease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(origin, rebasePayloadLease),
                out NavigationFlowFieldLease rebaseGuide)
            .Should().Be(NavigationGuideStatus.Success);
        graph.TryGetNodeRef(
                new NavigationCellAddress("sample-multi", firstWitness),
                out NavigationNodeRef firstRef)
            .Should().BeTrue();
        graph.TryGetNodeState(firstRef, out NavigationNodeState firstState)
            .Should().BeTrue();
        graph.TryGetNodeRef(
                new NavigationCellAddress("sample-multi", secondWitness),
                out NavigationNodeRef secondRef)
            .Should().BeTrue();
        graph.TryGetNodeState(secondRef, out NavigationNodeState secondState)
            .Should().BeTrue();
        graph.TryGetNodeRef(mappedRebaseAddress, out NavigationNodeRef mappedRebaseRef)
            .Should().BeTrue();
        graph.TryGetNodeState(mappedRebaseRef, out NavigationNodeState mappedRebaseState)
            .Should().BeTrue();
        graph.TryGetNodeRef(excludedPrefixAddress, out NavigationNodeRef excludedPrefixRef)
            .Should().BeTrue();
        graph.TryGetNodeState(excludedPrefixRef, out NavigationNodeState excludedPrefixState)
            .Should().BeTrue();
        binding.TryGetCellPrism(firstWitness, out GridCellPrism firstPrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(secondWitness, out GridCellPrism secondPrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(source, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        Vector3d expectedForward = SampleAdjacentCorridor(
            firstPrism,
            secondPrism,
            firstState.FootAnchor,
            secondState.FootAnchor,
            query.Agent.Shape);
        GridCellGeometry.TryCreateNavigationPortal(
                firstPrism,
                secondPrism,
                out GridNavigationPortal selectedPortal)
            .Should().BeTrue();
        selectedPortal.TryResolveProfile(
                query.Agent.Shape.Radius,
                query.Agent.Shape.Height,
                out Vector3d sourcePortal,
                out _)
            .Should().BeTrue();
        Vector3d portalApproach = sourcePortal
            - selectedPortal.SourceToTarget.Normalized * (Fixed64.One / (Fixed64)8);
        firstPrism.Contains(portalApproach).Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                firstPrism,
                out GridNavigationPortal incomingPortal)
            .Should().BeTrue();
        incomingPortal.TryResolveProfile(
                query.Agent.Shape.Radius,
                query.Agent.Shape.Height,
                out _,
                out Vector3d incomingTargetPortal)
            .Should().BeTrue();
        Vector3d incomingApproach = incomingTargetPortal
            + incomingPortal.SourceToTarget.Normalized * (Fixed64.One / (Fixed64)8);
        firstPrism.Contains(incomingApproach).Should().BeTrue();
        Vector3d otherWallOverlap = firstState.FootAnchor + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)3 / (Fixed64)8);
        firstPrism.Contains(otherWallOverlap).Should().BeTrue();
        Vector3d displacedBeforeSource = originState.FootAnchor
            - Vector3d.Right * ((Fixed64)3 / (Fixed64)4);
        sourcePrism.Contains(displacedBeforeSource).Should().BeFalse();

        var noLocalRecoveryBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 128,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0);
        rebaseGuide.TrySampleHeading(
                mappedRebaseState.FootAnchor,
                noLocalRecoveryBudget,
                out Vector3d recoveryBudgetHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        recoveryBudgetHeading.Should().Be(Vector3d.Zero);
        rebaseGuide.TrySampleHeading(
                excludedPrefixState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d excludedPrefixHeading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);
        excludedPrefixHeading.Should().Be(Vector3d.Zero,
            "a mapped cell beyond the immutable field prefix cannot become a cursor source");
        rebaseGuide.TrySampleHeading(
                mappedRebaseState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d mappedRebaseHeading)
            .Should().Be(NavigationGuideStatus.Success);
        mappedRebaseHeading.Should().Be(Vector3d.Backward,
            "a legal mapped host position must rebase the cursor before following that cell's native continuation");
        rebaseGuide.Dispose();

        guide.TrySampleHeading(
                displacedBeforeSource,
                GenerousSampleBudget,
                out Vector3d recoveryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        recoveryHeading.Should().Be(
            (originState.FootAnchor - displacedBeforeSource).Normalized,
            "an explicit selected edge must preserve its cursor when a legal prefix ray rejoins its source");

        var noExplicitPortalBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 128,
            maxPortalChecks: 0,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 128);
        guide.TrySampleHeading(
                originState.FootAnchor,
                noExplicitPortalBudget,
                out Vector3d portalBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        portalBlockedHeading.Should().Be(Vector3d.Zero);

        guide.TrySampleHeading(
                originState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d entryApproachHeading)
            .Should().Be(NavigationGuideStatus.Success);
        entryApproachHeading.Should().Be(
            (connection.EntryAnchor - originState.FootAnchor).Normalized);
        var noWitnessPortalBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 128,
            maxPortalChecks: 1,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 128);
        guide.TrySampleHeading(
                firstState.FootAnchor,
                noWitnessPortalBudget,
                out Vector3d witnessPortalBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        witnessPortalBlockedHeading.Should().Be(Vector3d.Zero);
        var noWitnessPrismBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 128,
            maxPortalChecks: 128,
            maxPrismChecks: 2,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 128);
        guide.TrySampleHeading(
                firstState.FootAnchor,
                noWitnessPrismBudget,
                out Vector3d prismBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        prismBlockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                connection.EntryAnchor,
                GenerousSampleBudget,
                out Vector3d firstWitnessApproachHeading)
            .Should().Be(NavigationGuideStatus.Success);
        firstWitnessApproachHeading.Should().NotBe(Vector3d.Zero,
            "passing the action entry still requires replaying the first witnessed leg");

        guide.TrySampleHeading(
                destinationState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d destinationAnchorHeading)
            .Should().Be(NavigationGuideStatus.Success);
        destinationAnchorHeading.Should().Be(Vector3d.Zero,
            "the explicit destination is terminal even before the lease cursor advances through its witnessed corridor");

        guide.TrySampleHeading(
                firstState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d firstHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(secondState.FootAnchor, GenerousSampleBudget, out _)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(
                firstState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retreatHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(portalApproach, GenerousSampleBudget, out Vector3d portalHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(incomingApproach, GenerousSampleBudget, out Vector3d incomingHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySampleHeading(otherWallOverlap, GenerousSampleBudget, out Vector3d invalidHeading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);

        firstHeading.Should().Be(expectedForward);
        retreatHeading.Should().Be(expectedForward,
            "directed progress must rewind to the actual witness after retreat");
        portalHeading.Should().NotBe(Vector3d.Zero);
        incomingHeading.Should().Be(expectedForward);
        invalidHeading.Should().Be(Vector3d.Zero,
            "an earlier mapped witness outside the required chain must not be skipped");
        guide.TrySampleHeading(
                connection.ExitAnchor,
                GenerousSampleBudget,
                out Vector3d exitHeading)
            .Should().Be(NavigationGuideStatus.Success);
        exitHeading.Should().Be(
            (destinationState.FootAnchor - connection.ExitAnchor).Normalized);
        Vector3d overshotDestination =
            destinationState.FootAnchor + Vector3d.Right * (Fixed64.One / (Fixed64)8);
        var noCursorRebaseBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 128);
        guide.TrySampleHeading(
                overshotDestination,
                noCursorRebaseBudget,
                out Vector3d rebaseBlockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        rebaseBlockedHeading.Should().Be(Vector3d.Zero,
            "crossing the explicit exit cannot advance the lease without cursor-rebase work");
        guide.TrySampleHeading(
                overshotDestination,
                GenerousSampleBudget,
                out Vector3d overshootHeading)
            .Should().Be(NavigationGuideStatus.Success);
        overshootHeading.Should().Be(
            (destinationState.FootAnchor - overshotDestination).Normalized);
        guide.TrySampleHeading(
                destinationState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d arrivedHeading)
            .Should().Be(NavigationGuideStatus.Success);
        arrivedHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    public void NativeSample_BelowExactCategoryAllowance_ShouldFailWithoutCursorMutation(
        int category,
        int allowance)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        var limits = new[] { 3, 2, 0, 1, 2, 2, 0 };
        limits[category] = allowance;
        var budget = new GuideSampleWorkBudget(
            limits[0],
            limits[1],
            limits[2],
            limits[3],
            limits[4],
            limits[5],
            limits[6]);

        guide.TrySampleHeading(source.FootAnchor, budget, out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().NotBe(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(0, 5)]
    [InlineData(0, 6)]
    [InlineData(0, 7)]
    [InlineData(0, 8)]
    [InlineData(0, 9)]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(6, 0)]
    public void ExactNodeRebase_BelowExactCategoryAllowance_ShouldFailWithoutCursorMutation(
        int category,
        int allowance)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        var limits = new[] { 11, 1, 1, 0, 2, 0, 1 };
        limits[category] = allowance;
        var budget = new GuideSampleWorkBudget(
            limits[0],
            limits[1],
            limits[2],
            limits[3],
            limits[4],
            limits[5],
            limits[6]);

        guide.TrySampleHeading(destination.FootAnchor, budget, out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySampleHeading(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalRecovery_DisplacedBeyondCurrentSource_ShouldRayRejoinTheSameLease(
        bool addUnmappedGrid)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                fixture.FarOrigin,
                out GridCellPrism sourcePrism)
            .Should().BeTrue();
        Vector3d actualFoot = source.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        sourcePrism.Contains(actualFoot).Should().BeFalse();
        if (addUnmappedGrid)
        {
            GridConfiguration unrelated = new(
                actualFoot,
                actualFoot,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            fixture.World.TryAddGrid(unrelated, out _).Should().BeTrue();
        }
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldLease sameLease = guide;

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(Vector3d.Backward);
        sameLease.Status.Should().Be(NavigationGuideStatus.Success);
        cache.ActiveLeaseCount.Should().Be(1);
        guide.Dispose();
    }

    [Fact]
    public void LocalRecovery_WhenMappedGridMutatesAfterGuideCreation_ShouldReturnStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                fixture.FarOrigin,
                out GridCellPrism sourcePrism)
            .Should().BeTrue();
        Vector3d actualFoot =
            source.FootAnchor + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        sourcePrism.Contains(actualFoot).Should().BeFalse();
        fixture.Graph.TryGetMap(
                fixture.FarOrigin.MapId,
                out NavigationMapInstance? instance)
            .Should().BeTrue();
        instance.Should().NotBeNull();
        fixture.World.ActiveGrids[instance!.GridIdentity.GridIndex]
            .TryRemoveVoxel(new VoxelIndex(4, 0, 0))
            .Should().BeTrue();

        guide.TrySampleHeading(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale,
                "local recovery cannot rebase through a mapped grid generation newer than its graph");

        heading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void LocalRecoveryRejoin_ShouldConsumeExactlyOneAttempt()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        Vector3d actualFoot = source.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        var meter = new GuideSampleWorkMeter(GenerousSampleBudget);

        guide.TrySample(actualFoot, ref meter, out NavigationFlowSample firstSample)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(actualFoot, ref meter, out NavigationFlowSample exhaustedSample)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);

        firstSample.Heading.Should().Be(Vector3d.Backward);
        exhaustedSample.Heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(2, NavigationGuideStatus.CapacityExceeded)]
    public void LocalRecoveryRejoin_ShouldPropagateTerminalWorkStatus(
        int scenario,
        NavigationGuideStatus expected)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        NavigationImmediateRayWorkspace workspace = scenario == 2
            ? new NavigationImmediateRayWorkspace(8, 64, 64, 128, 0)
            : NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace();
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            1,
            fixture.Far.RetainedBytes,
            fixture.Far.RetainedBytes,
            fixture.Far.RetainedBytes,
            1,
            8,
            workspace);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        Vector3d actualFoot =
            source.FootAnchor + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        GuideSampleWorkBudget budget = scenario == 0
            ? new GuideSampleWorkBudget(128, 128, 8, 32, 32, 32, 0)
            : GenerousSampleBudget;

        guide.TrySampleHeading(actualFoot, budget, out Vector3d heading)
            .Should().Be(expected);

        heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void RejoinTargets_NativeSelectedEdge_ShouldExposeStableSourcePortalAndTargetOrdinals()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        fixture.Far.TryGetNode(
                fixture.FarOrigin,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode flowNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                flowNode.SelectedEdge.Target,
                out NavigationNodeRef targetRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetRef, out NavigationNodeState target)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges =
            fixture.Graph.EnumerateStructuralSurfaceEdges(sourceRef);
        NavigationGraphEdge selectedEdge = default;
        while (edges.MoveNext())
        {
            if (edges.CurrentOrdinal == flowNode.SelectedEdge.CanonicalOutgoingOrdinal)
                selectedEdge = edges.Current;
        }
        selectedEdge.Kind.Should().Be(NavigationGraphEdgeKind.Native);
        selectedEdge.NativePortal.TryTranslate(
                source.Center,
                out GridNavigationPortal translatedPortal)
            .Should().BeTrue();
        translatedPortal.TryResolveProfile(
                fixture.Far.Key.Agent.Shape.Radius,
                fixture.Far.Key.Agent.Shape.Height,
                out _,
                out Vector3d selectedExitTarget)
            .Should().BeTrue();

        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                fixture.FarOrigin,
                source,
                target,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 0,
                out NavigationFlowRejoinTarget sourceTarget)
            .Should().BeTrue();
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                fixture.FarOrigin,
                source,
                target,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 1,
                out NavigationFlowRejoinTarget portalTarget)
            .Should().BeTrue();
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                fixture.FarOrigin,
                source,
                target,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 2,
                out NavigationFlowRejoinTarget nodeTarget)
            .Should().BeTrue();
        NavigationSelectedEdgeProgressWork.TryGetRejoinTarget(
                fixture.FarOrigin,
                source,
                target,
                flowNode.SelectedEdge,
                selectedExitTarget,
                targetOrdinal: 3,
                out _)
            .Should().BeFalse();

        sourceTarget.Position.Should().Be(source.FootAnchor);
        sourceTarget.Constraint.Kind.Should().Be(
            NavigationRayChainConstraintKind.SourceAddress);
        portalTarget.Constraint.Kind.Should().Be(
            NavigationRayChainConstraintKind.SelectedEdge);
        nodeTarget.Position.Should().Be(target.FootAnchor);
    }

    private static GuideSampleWorkBudget GenerousSampleBudget => new(
        128,
        128,
        8,
        32,
        32,
        32,
        1);

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture) => new(
        fixture.World,
        maxEntries: 1,
        maxReusableBytes: fixture.Far.RetainedBytes,
        maxSinglePayloadBytes: fixture.Far.RetainedBytes,
        maxActivePayloadBytes: fixture.Far.RetainedBytes,
        maxActiveLeases: 1,
        guideMapCapacity: 8,
        immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        return lease;
    }

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationWorldGraphStore store,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress origin)
    {
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                store,
                payload,
                origin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        return lease;
    }

    private static NavigationFlowFieldPayload RunVolumeFlow(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress source,
        NavigationCellAddress target)
    {
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        graph.TryGetNodeRef(source, out NavigationNodeRef sourceNode).Should().BeTrue();
        graph.TryGetNodeRef(target, out NavigationNodeRef targetNode).Should().BeTrue();
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            query,
            new NavigationResolvedEndpoint(
                sourceNode,
                source,
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                Vector3d.Zero,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                targetNode,
                target,
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                Vector3d.Zero,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Gas,
            TraversalMedia.Gas,
            new NavigationWorkMeter(query.Budget),
            world.ChangeSequence,
            requiresWorldStamp: false);
        using var work = new NavigationFlowFieldWork(
            world,
            resolved,
            new NavigationFlowFieldWorkspace(1, 8, 8, 8, 16, 8));
        for (int step = 0;
            step < 1_024 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(64, 64, 64, 64);
        }
        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        return work.Result!;
    }

    private static VoxelIndex FindHexCellWithNeighbor(
        NormalizedGridConfiguration binding,
        HexDirection direction)
    {
        VoxelIndex offset = HexDirectionUtility.GetOffset(direction);
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int r = 1; r < binding.Length - 1; r++)
            {
                var source = new VoxelIndex(q, 0, r);
                var target = new VoxelIndex(
                    source.x + offset.x,
                    source.y + offset.y,
                    source.z + offset.z);
                if (binding.IsValidIndex(source) && binding.IsValidIndex(target))
                    return source;
            }
        }
        throw new InvalidOperationException("The test configuration has no hex pair.");
    }

    private static Vector3d SampleAdjacentCorridor(
        GridCellPrism source,
        GridCellPrism target,
        Vector3d actualFoot,
        Vector3d targetFoot,
        KinematicBodyShape shape)
    {
        GridCellPrism[] cells = { source, target };
        var waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            2,
            actualFoot,
            targetFoot,
            shape.Radius,
            shape.Height);
        cursor.Advance(cells, waypoints, maxWork: 5)
            .Should().Be(GridNavigationCorridorValidationStatus.Complete);
        Vector3d next = targetFoot;
        for (int i = 0; i < cursor.PortalWaypointCount; i++)
        {
            if (waypoints[i] == actualFoot)
                continue;
            next = waypoints[i];
            break;
        }
        return (next - actualFoot).Normalized;
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int i = 0;
             i < 4_096 && receipt.Status == NavigationOperationStatus.Pending;
             i++)
        {
            context.Simulate();
        }
    }
}
