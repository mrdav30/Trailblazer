using System;
using System.Reflection;
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
    [Fact]
    public void PublicLease_ShouldBeReadonlyGenerationValidatedValueSurface()
    {
        Type type = typeof(NavigationFlowFieldLease);

        type.IsValueType.Should().BeTrue();
        type.IsDefined(
                typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute),
                inherit: false)
            .Should().BeTrue();
        type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().HaveCount(2);
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Should().HaveCount(2);
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Should().HaveCount(4);
    }

    [Fact]
    public void DefaultLease_ShouldFailClosedWithoutManufacturingAHeading()
    {
        NavigationFlowFieldLease lease = default;

        lease.Status.Should().Be(NavigationGuideStatus.Stale);
        lease.OriginIntegrationCost.Should().Be(Fixed64.Zero);
        lease.TrySample(
                Vector3d.Zero,
                new GuideSampleWorkBudget(1, 1, 1, 1, 1, 1, 1),
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale);
        heading.Should().Be(Vector3d.Zero);
        lease.Dispose();
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
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);

        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.OriginIntegrationCost.Should().Be(originNode.IntegrationCost);
        staleAlias.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        guide.TrySample(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);
        heading.Should().Be((target.FootAnchor - source.FootAnchor).Normalized);

        NavigationFlowFieldLease copied = guide;
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        copied.Status.Should().Be(NavigationGuideStatus.Stale);
        copied.Dispose();
        staleAlias.Dispose();
    }

    [Fact]
    public void ExactNodeRebase_ToDestination_ShouldSucceedWithZeroHeading()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
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

        guide.TrySample(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
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
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        payload.TryGetNode(origin, out NavigationFlowFieldNode sourceNode)
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

        guide.TrySample(actualFoot, noRecoveryBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((targetState.FootAnchor - actualFoot).Normalized,
            "target-only containment remains part of the current selected edge until its anchor is reached");
        guide.TrySample(
                sourceState.FootAnchor,
                noRecoveryBudget,
                out Vector3d reentryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        reentryHeading.Should().NotBe(Vector3d.Zero,
            "retreat through the selected portal must rewind without local recovery");
        guide.Dispose();
    }

    [Fact]
    public void BudgetFailure_ShouldNotMutateCursorAndRetryShouldMatchFreshGuide()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = new(
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
                out secondPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, firstPayload),
                out NavigationFlowFieldLease retryGuide)
            .Should().Be(NavigationGuideStatus.Success);
        cache.TryCreateGuide(
                fixture.World,
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

        retryGuide.TrySample(
                source.FootAnchor,
                default,
                out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        retryGuide.TrySample(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        freshGuide.TrySample(
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
        PathQuery query = NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(start, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var target = new NavigationCellAddress(fixture.MapId, destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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
                out _)
            .Should().BeTrue();
        payload.TryGetNode(origin, out NavigationFlowFieldNode flowNode)
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

        guide.TrySample(
                entry,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((sourcePortal - entry).Normalized);
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
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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

        guide.TrySample(entry, GenerousSampleBudget, out Vector3d heading)
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
            store,
            fixture.Graph,
            query,
            origin,
            destinationAddress,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
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

        guide.TrySample(
                explicitTargetState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(
            (destinationState.FootAnchor - explicitTargetState.FootAnchor).Normalized);
        guide.Dispose();
    }

    [Fact]
    public void InternalBatch_ShouldRequireCanonicalOrderAndUseOneSharedMeter()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 8,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease firstPayload = Publish(cache, fixture);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease secondPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, firstPayload),
                out NavigationFlowFieldLease first)
            .Should().Be(NavigationGuideStatus.Success);
        cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, secondPayload),
                out NavigationFlowFieldLease second)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        GuideSampleBatchItem[] items =
        {
            new(stableOrdinal: 1, first, source.FootAnchor),
            new(stableOrdinal: 2, second, source.FootAnchor)
        };
        var results = new GuideSampleBatchResult[2];
        var oneSampleBudget = new GuideSampleWorkBudget(
            128,
            2,
            0,
            1,
            2,
            2,
            0);

        GuideSampleBatch.Sample(items, results, oneSampleBudget);

        results[0].Status.Should().Be(NavigationGuideStatus.Success);
        results[1].Status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        results[1].Heading.Should().Be(Vector3d.Zero);
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void InternalBatch_ShouldRejectNonCanonicalOrderBeforeSampling()
    {
        GuideSampleBatchItem[] items =
        {
            new(stableOrdinal: 2, default, Vector3d.Zero),
            new(stableOrdinal: 1, default, Vector3d.Zero)
        };
        var results = new GuideSampleBatchResult[2];

        Action sample = () => GuideSampleBatch.Sample(
            items,
            results,
            GenerousSampleBudget);

        sample.Should().Throw<ArgumentException>();
        results.Should().OnlyContain(result => result.Status == default);
    }

    [Fact]
    public void GuideSampleWorkMeter_ShouldExposeNoInspectionSurface()
    {
        typeof(GuideSampleWorkMeter)
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeEmpty(
                "budget boundaries are pinned behaviorally instead of through test-only hot-path accessors");
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
            store,
            fixture.Graph,
            query,
            origin,
            destination,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                fixture.Context.World,
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        payload.TryGetNode(origin, out NavigationFlowFieldNode flowNode)
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

        guide.TrySample(
                actualStart,
                GenerousSampleBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be((actualEnd - actualStart).Normalized);
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
            store,
            fixture.Graph,
            query,
            origin,
            destination,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
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
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
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
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
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
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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
        Vector3d actualFoot = sourceState.FootAnchor + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)3 / (Fixed64)8);
        sourcePrism.Contains(actualFoot).Should().BeTrue();

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);

        heading.Should().Be(Vector3d.Zero);
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
            store,
            fixture.Graph,
            query,
            origin,
            target,
            NavigationFlowFieldStatus.Success);
        using var cache = new NavigationFlowFieldPayloadCache(
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
                world,
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

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
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
            Fixed64.Zero);
        var origin = new NavigationCellAddress("sample-multi", source);
        var destinationAddress = new NavigationCellAddress("sample-multi", destination);
        NavigationFlowFieldPayload payload = NavigationFlowFieldCacheTestHarness.RunFlow(
            store,
            graph,
            query,
            origin,
            destinationAddress,
            NavigationFlowFieldStatus.Success);
        payload.TryGetNode(origin, out NavigationFlowFieldNode originNode)
            .Should().BeTrue();
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
                context.World,
                store,
                new NavigationFlowQueryResult(origin, payloadLease),
                out NavigationFlowFieldLease guide)
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

        guide.TrySample(
                firstState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d firstHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(secondState.FootAnchor, GenerousSampleBudget, out _)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(
                firstState.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retreatHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(portalApproach, GenerousSampleBudget, out Vector3d portalHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(incomingApproach, GenerousSampleBudget, out Vector3d incomingHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(otherWallOverlap, GenerousSampleBudget, out Vector3d invalidHeading)
            .Should().Be(NavigationGuideStatus.LocalRecoveryRequired);

        firstHeading.Should().Be(expectedForward);
        retreatHeading.Should().Be(expectedForward,
            "directed progress must rewind to the actual witness after retreat");
        portalHeading.Should().NotBe(Vector3d.Zero);
        incomingHeading.Should().Be(expectedForward);
        invalidHeading.Should().Be(Vector3d.Zero,
            "an earlier mapped witness outside the required chain must not be skipped");
        guide.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeSample_OneBelowConsumedCategory_ShouldFailWithoutCursorMutation(
        int category)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        var limits = new[] { 3, 2, 0, 1, 2, 2, 0 };
        limits[category]--;
        var budget = new GuideSampleWorkBudget(
            limits[0],
            limits[1],
            limits[2],
            limits[3],
            limits[4],
            limits[5],
            limits[6]);

        guide.TrySample(source.FootAnchor, budget, out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySample(
                source.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().NotBe(Vector3d.Zero);
        guide.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void ExactNodeRebase_OneBelowConsumedCategory_ShouldFailWithoutCursorMutation(
        int category)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
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
        limits[category]--;
        var budget = new GuideSampleWorkBudget(
            limits[0],
            limits[1],
            limits[2],
            limits[3],
            limits[4],
            limits[5],
            limits[6]);

        guide.TrySample(destination.FootAnchor, budget, out Vector3d blockedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blockedHeading.Should().Be(Vector3d.Zero);
        guide.TrySample(
                destination.FootAnchor,
                GenerousSampleBudget,
                out Vector3d retryHeading)
            .Should().Be(NavigationGuideStatus.Success);
        retryHeading.Should().Be(Vector3d.Zero);
        guide.Dispose();
    }

    [Fact]
    public void LocalRecovery_DisplacedBeyondCurrentSource_ShouldRayRejoinTheSameLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
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
        Vector3d actualFoot = source.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        sourcePrism.Contains(actualFoot).Should().BeFalse();
        NavigationFlowFieldLease sameLease = guide;

        guide.TrySample(actualFoot, GenerousSampleBudget, out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Success);

        heading.Should().Be(Vector3d.Backward);
        sameLease.Status.Should().Be(NavigationGuideStatus.Success);
        cache.ActiveLeaseCount.Should().Be(1);
        guide.Dispose();
    }

    [Fact]
    public void LocalRecoveryRejoin_ShouldConsumeExactlyOneAttempt()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
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

        guide.TrySample(actualFoot, ref meter, out Vector3d firstHeading)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TrySample(actualFoot, ref meter, out Vector3d exhaustedHeading)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);

        firstHeading.Should().Be(Vector3d.Backward);
        exhaustedHeading.Should().Be(Vector3d.Zero);
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
            1,
            fixture.Far.RetainedBytes,
            fixture.Far.RetainedBytes,
            fixture.Far.RetainedBytes,
            1,
            8,
            workspace);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);
        cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        Vector3d actualFoot =
            source.FootAnchor + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        GuideSampleWorkBudget budget = scenario == 0
            ? new GuideSampleWorkBudget(128, 128, 8, 32, 32, 32, 0)
            : GenerousSampleBudget;

        guide.TrySample(actualFoot, budget, out Vector3d heading)
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
