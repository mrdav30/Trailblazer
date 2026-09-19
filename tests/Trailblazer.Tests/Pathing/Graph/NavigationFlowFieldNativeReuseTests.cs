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

public sealed class NavigationFlowFieldNativeReuseTests
{
    [Theory]
    [InlineData(-1, 0)]
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
    public void WarmNativeSample_ShouldPreserveExactCategoryBoundariesAndRetry(
        int limitedCategory,
        int allowance)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = CreateCache(fixture.World, fixture.Far);
        using var warm = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        using var fresh = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        Vector3d foot = GetFoot(fixture.Graph, fixture.FarOrigin);
        Warm(warm, foot);
        var limits = new[] { 3, 2, 0, 1, 2, 2, 0 };
        if (limitedCategory >= 0)
            limits[limitedCategory] = allowance;
        var budget = new GuideSampleWorkBudget(
            limits[0], limits[1], limits[2], limits[3], limits[4], limits[5], limits[6]);

        AssertSameSample(
            warm, fresh, foot, budget,
            limitedCategory < 0 ? NavigationGuideStatus.Success : NavigationGuideStatus.BudgetExceeded);

        NavigationFlowSample retry = AssertSameSample(
            warm, fresh, foot, GenerousBudget, NavigationGuideStatus.Success);
        retry.Heading.Should().Be(Vector3d.Left);
        retry.Target.Should().Be(GetFoot(fixture.Graph, fixture.MarginOrigin));
        retry.Medium.Should().Be(TraversalMedium.Solid);
        retry.HasTransition.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void WarmNativeSample_WithMixedCandidates_ShouldPreserveMergePrefetchDebit(
        int legAllowance)
    {
        using var world = new GridWorld();
        var start = new VoxelIndex(1, 0, 0);
        var fixture = CreateMixedNativeFixture(world);
        using var store = NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var payload = BuildFlow(world, store, fixture, start, default);
        using var cache = CreateCache(world, payload);
        using var warm = CreateGuide(cache, store, payload, origin);
        using var fresh = CreateGuide(cache, store, payload, origin);
        Vector3d foot = GetFoot(fixture.Graph, origin);
        Warm(warm, foot);
        payload.TryGetNode(origin, TraversalMedium.Solid, out NavigationFlowFieldNode node)
            .Should().BeTrue();
        node.SelectedEdge.CanonicalOutgoingOrdinal.Should().Be(0);
        fixture.Graph.TryGetNodeRef(origin, out NavigationNodeRef source).Should().BeTrue();
        var edges = fixture.Graph.EnumerateStructuralSurfaceEdges(source);
        var structuralMeter = new GuideSampleWorkMeter(GenerousBudget);
        int remainingSteps = int.MaxValue;
        NavigationSurfaceEdgeAdvanceStatus edgeStatus;
        do
        {
            edgeStatus = edges.AdvanceOne(ref structuralMeter, ref remainingSteps);
        }
        while (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Pending);
        edgeStatus.Should().Be(NavigationSurfaceEdgeAdvanceStatus.Edge);
        edges.Current.Kind.Should().Be(NavigationGraphEdgeKind.Native);
        structuralMeter.GetCursorLegScanAllowance().Should().Be(
            GenerousBudget.MaxCursorLegScans - 3,
            "ordinal zero prefetches a native edge, rejects an incoming owner, and reads an outgoing owner");
        var budget = new GuideSampleWorkBudget(128, legAllowance, 8, 32, 32, 32, 1);

        NavigationFlowSample sample = AssertSameSample(
            warm, fresh, foot, budget,
            legAllowance < 4 ? NavigationGuideStatus.BudgetExceeded : NavigationGuideStatus.Success);

        if (legAllowance >= 4)
            sample.Heading.Should().Be(Vector3d.Left);
        AssertSameSample(warm, fresh, foot, GenerousBudget, NavigationGuideStatus.Success)
            .Heading.Should().Be(Vector3d.Left);
    }

    [Theory]
    [InlineData(2, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(3, NavigationGuideStatus.Success)]
    [InlineData(8, NavigationGuideStatus.Success)]
    public void Resolver_AfterPublicationWithNewIncidentCandidates_ShouldUseNewStructuralDebit(
        int legAllowance,
        NavigationGuideStatus expectedStatus)
    {
        using var world = new GridWorld();
        var fixture = CreateMixedNativeFixture(world);
        // Both snapshots retain the exact same cell slots and native portal. Only
        // the later snapshot adds incident owners that structural merging must debit.
        var original = new NavigationWorldGraph(
            fixture.Graph.GraphVersion,
            new[] { fixture.Graph.GetInstance(0) },
            fixture.Graph.AreaCatalog,
            surfaceComponents: fixture.Graph.SurfaceComponents);
        using var store = NavigationAStarExitTestHarness.CreateStore(original, 8);
        var origin = new NavigationCellAddress(fixture.MapId, new VoxelIndex(1, 0, 0));
        original.TryGetNodeRef(origin, out NavigationNodeRef source).Should().BeTrue();
        var resolver = new NavigationSelectedEdgeResolver();
        var warmMeter = new GuideSampleWorkMeter(GenerousBudget);
        resolver.TryResolve(original, source, 0, ref warmMeter, out _)
            .Should().Be(NavigationGuideStatus.Success);
        warmMeter.GetCursorLegScanAllowance().Should().Be(GenerousBudget.MaxCursorLegScans - 1);
        store.TryPublish(fixture.Graph.WithGraphVersion(original.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);
        NavigationWorldGraph current = store.Current;
        current.TryGetNodeRef(origin, out NavigationNodeRef currentSource).Should().BeTrue();
        currentSource.Should().Be(source);
        var budget = new GuideSampleWorkBudget(7, legAllowance, 5, 4, 3, 2, 1);
        var meter = new GuideSampleWorkMeter(budget);
        var enumeratedMeter = new GuideSampleWorkMeter(budget);

        resolver.TryResolve(current, source, 0, ref meter, out NavigationGraphEdge edge)
            .Should().Be(expectedStatus);
        EnumerateSelectedEdge(current, source, 0, ref enumeratedMeter, out NavigationGraphEdge enumerated)
            .Should().Be(expectedStatus);

        meter.Should().Be(enumeratedMeter);
        var exactMeter = new GuideSampleWorkMeter(budget);
        exactMeter.TryConsumeCursorLegScans(Math.Min(legAllowance, 3)).Should().BeTrue();
        meter.Should().Be(exactMeter, "publication adds two charged owners without changing source or ordinal zero");
        edge.Should().Be(enumerated);
        if (expectedStatus == NavigationGuideStatus.Success)
        {
            edge.Kind.Should().Be(NavigationGraphEdgeKind.Native);
            current.TryGetNodeAddress(edge.Target, out NavigationCellAddress target).Should().BeTrue();
            target.Should().Be(new NavigationCellAddress(fixture.MapId, default));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Resolver_ChangedSelectedOrdinal_ShouldReturnTheOtherEdgeAndExactWork(int ordinal)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        fixture.Graph.TryGetNodeRef(fixture.MarginOrigin, out NavigationNodeRef source).Should().BeTrue();
        var resolver = new NavigationSelectedEdgeResolver();
        var warmMeter = new GuideSampleWorkMeter(GenerousBudget);
        resolver.TryResolve(fixture.Graph, source, 1 - ordinal, ref warmMeter, out NavigationGraphEdge previous)
            .Should().Be(NavigationGuideStatus.Success);
        var budget = new GuideSampleWorkBudget(7, 5, 4, 3, 2, 1, 0);
        var meter = new GuideSampleWorkMeter(budget);
        var enumeratedMeter = new GuideSampleWorkMeter(budget);

        resolver.TryResolve(fixture.Graph, source, ordinal, ref meter, out NavigationGraphEdge edge)
            .Should().Be(NavigationGuideStatus.Success);
        EnumerateSelectedEdge(fixture.Graph, source, ordinal, ref enumeratedMeter, out NavigationGraphEdge enumerated)
            .Should().Be(NavigationGuideStatus.Success);

        edge.Should().Be(enumerated);
        edge.Target.Should().NotBe(previous.Target);
        edge.Kind.Should().Be(NavigationGraphEdgeKind.Native);
        fixture.Graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress target).Should().BeTrue();
        target.Should().Be(ordinal == 0 ? fixture.NearOrigin : fixture.FarOrigin);
        edge.NativePortal.SourceToTarget.Should().Be(ordinal == 0 ? Vector3d.Left : Vector3d.Right);
        meter.Should().Be(enumeratedMeter);
        var exactMeter = new GuideSampleWorkMeter(budget);
        exactMeter.TryConsumeCursorLegScans(ordinal + 1).Should().BeTrue();
        meter.Should().Be(exactMeter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WarmNativeSample_ShouldReevaluateCurrentFootAndDirectedProgress(int position)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = CreateCache(fixture.World, fixture.Far);
        using var warm = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        using var fresh = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        Vector3d source = GetFoot(fixture.Graph, fixture.FarOrigin);
        Vector3d target = GetFoot(fixture.Graph, fixture.MarginOrigin);
        Warm(warm, source);
        Vector3d actual = position switch
        {
            0 => source + Vector3d.Forward * Fixed64.Quarter,
            1 => source + Vector3d.Left * Fixed64.Half,
            2 => target + Vector3d.Right * Fixed64.Quarter,
            _ => target
        };

        NavigationFlowSample sample = AssertSameSample(
            warm, fresh, actual, GenerousBudget, NavigationGuideStatus.Success);

        sample.Heading.Should().NotBe(Vector3d.Zero);
        if (position == 0)
            sample.Heading.Should().NotBe(Vector3d.Left, "off-center feet must approach the portal anew");
        else
            sample.Heading.Should().Be(Vector3d.Left);
        if (position == 3)
            sample.Target.Should().Be(GetFoot(fixture.Graph, fixture.NearOrigin));
        else
            sample.Target.Should().Be(target);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void WarmSparseHexSample_ShouldPreserveOffCenterGuidanceAndWork(HexOrientation orientation)
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            new Vector3d(-8, 0, -8), new Vector3d(8, 2, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex start = FindHexPair(binding, out VoxelIndex end);
        var fixture = NavigationAStarExitTestHarness.CreateSingleMap(
            world, configuration, new[] { start, end }, "native-hex");
        using var store = NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 8);
        var origin = new NavigationCellAddress(fixture.MapId, start);
        var destination = new NavigationCellAddress(fixture.MapId, end);
        var payload = BuildFlow(world, store, fixture, start, end);
        using var cache = CreateCache(world, payload);
        using var warm = CreateGuide(cache, store, payload, origin);
        using var fresh = CreateGuide(cache, store, payload, origin);
        Vector3d foot = GetFoot(fixture.Graph, origin);
        Warm(warm, foot);
        Vector3d actual = foot + new Vector3d(
            Fixed64.One / (Fixed64)8, Fixed64.Zero, Fixed64.One / (Fixed64)8);

        NavigationFlowSample sample = AssertSameSample(
            warm, fresh, actual, GenerousBudget, NavigationGuideStatus.Success);

        sample.Heading.Should().NotBe(Vector3d.Zero);
        sample.Target.Should().Be(GetFoot(fixture.Graph, destination));
        sample.Medium.Should().Be(TraversalMedium.Solid);
        sample.HasTransition.Should().BeFalse();
    }

    [Fact]
    public void WarmSample_AfterCompatiblePublication_ShouldPreserveGuidanceAndMeter()
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = CreateCache(fixture.World, fixture.Far);
        using var warm = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        Vector3d foot = GetFoot(fixture.Graph, fixture.FarOrigin);
        Warm(warm, foot);
        fixture.Store.TryPublish(fixture.Graph.WithGraphVersion(fixture.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);
        using var fresh = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);

        AssertSameSample(warm, fresh, foot, GenerousBudget, NavigationGuideStatus.Success)
            .Heading.Should().Be(Vector3d.Left);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
        fixture.Store.RetiredGenerationCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(1, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(2, NavigationGuideStatus.Success)]
    public void BoundNearGuides_AfterSameKeyPromotion_ShouldKeepOriginalNodeLookupDebit(
        int nodeAllowance,
        NavigationGuideStatus expectedStatus)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World, 1, fixture.Far.RetainedBytes, fixture.Far.RetainedBytes,
            checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes),
            3, fixture.Graph.MapCount,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        using var warm = CreateGuide(cache, fixture.Store, fixture.Near, fixture.NearOrigin);
        using var fresh = CreateGuide(cache, fixture.Store, fixture.Near, fixture.NearOrigin);
        Vector3d foot = GetFoot(fixture.Graph, fixture.NearOrigin);
        Warm(warm, foot);
        fixture.Near.Key.Should().Be(fixture.Far.Key);
        fixture.Near.Nodes.Length.Should().BeLessThan(fixture.Far.Nodes.Length);
        cache.TryReservePayload(fixture.Far.RetainedBytes, out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store, fixture.Far, fixture.FarOrigin, ref reservation, out var promoted)
            .Should().Be(NavigationFlowFieldStatus.Success);
        using var ownedPromotion = promoted;
        promoted.TryGetPayload(out NavigationFlowFieldPayload published).Should().Be(NavigationFlowFieldStatus.Success);
        published.Should().BeSameAs(fixture.Far);
        cache.AcquisitionDiagnostics.Promotions.Should().Be(1);
        cache.DetachedBytes.Should().Be(fixture.Near.RetainedBytes);
        var budget = new GuideSampleWorkBudget(nodeAllowance, 128, 8, 32, 32, 32, 1);
        var warmMeter = new GuideSampleWorkMeter(budget);
        var freshMeter = new GuideSampleWorkMeter(budget);

        warm.TrySample(foot, ref warmMeter, out NavigationFlowSample warmSample).Should().Be(expectedStatus);
        fresh.TrySample(foot, ref freshMeter, out NavigationFlowSample freshSample).Should().Be(expectedStatus);

        warmSample.Should().Be(freshSample);
        warmMeter.Should().Be(freshMeter);
        warmMeter.GetCurrentNodeLookupAllowance().Should().Be(Math.Max(0, nodeAllowance - 2),
            "both old bindings still search the two-node Near payload, not the promoted four-node payload");
        if (expectedStatus != NavigationGuideStatus.Success)
            warmSample.Should().Be(default(NavigationFlowSample));
        NavigationFlowSample retry = AssertSameSample(
            warm, fresh, foot, new GuideSampleWorkBudget(2, 128, 8, 32, 32, 32, 1),
            NavigationGuideStatus.Success);
        retry.Heading.Should().Be(Vector3d.Left);
        retry.Target.Should().Be(GetFoot(fixture.Graph, fixture.Near.Key.DestinationAddress));
    }

    [Theory]
    [InlineData(1, NavigationGuideStatus.BudgetExceeded)]
    [InlineData(2, NavigationGuideStatus.Success)]
    public void WarmRebasedNode_ShouldReplayOnlyItsOwnLookupDebit(
        int nodeAllowance,
        NavigationGuideStatus expectedStatus)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using var cache = CreateCache(fixture.World, fixture.Far);
        using var warm = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        Warm(warm, GetFoot(fixture.Graph, fixture.FarOrigin));
        Vector3d foot = GetFoot(fixture.Graph, fixture.MarginOrigin);
        var rebaseMeter = new GuideSampleWorkMeter(GenerousBudget);
        warm.TrySample(foot, ref rebaseMeter, out NavigationFlowSample advanced)
            .Should().Be(NavigationGuideStatus.Success);
        advanced.Target.Should().Be(GetFoot(fixture.Graph, fixture.NearOrigin));
        rebaseMeter.GetCurrentNodeLookupAllowance().Should().Be(
            GenerousBudget.MaxCurrentNodeLookupProbes - 5,
            "the advancing call searches the old source in three probes and its successor in two");
        using var fresh = CreateGuide(cache, fixture.Store, fixture.Far, fixture.MarginOrigin);
        var budget = new GuideSampleWorkBudget(nodeAllowance, 128, 8, 32, 32, 32, 1);

        AssertSameSample(warm, fresh, foot, budget, expectedStatus);

        NavigationFlowSample retry = AssertSameSample(
            warm, fresh, foot, new GuideSampleWorkBudget(2, 128, 8, 32, 32, 32, 1),
            NavigationGuideStatus.Success);
        retry.Heading.Should().Be(Vector3d.Left);
        retry.Target.Should().Be(GetFoot(fixture.Graph, fixture.NearOrigin));
    }

    [Fact]
    public void PooledGuide_ReboundToSameVersionAndNodeIdentity_ShouldUseNewGridGeometry()
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var configuration = new GridConfiguration(
            new Vector3d(64, 0, 0), new Vector3d(74, 2, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)2),
            storageKind: GridStorageKind.Sparse);
        var start = new VoxelIndex(3, 0, 0);
        var other = NavigationAStarExitTestHarness.CreateSingleMap(
            fixture.World, configuration,
            new[] { default(VoxelIndex), new VoxelIndex(1, 0, 0), new VoxelIndex(2, 0, 0), start },
            fixture.FarOrigin.MapId);
        using var otherStore = NavigationAStarExitTestHarness.CreateStore(other.Graph, 8);
        var otherPayload = BuildFlow(fixture.World, otherStore, other, start, default);
        other.Graph.GraphVersion.Should().Be(fixture.Graph.GraphVersion);
        using var cache = CreateCache(fixture.World, fixture.Far);
        using var first = CreateGuide(cache, fixture.Store, fixture.Far, fixture.FarOrigin);
        Warm(first, GetFoot(fixture.Graph, fixture.FarOrigin));
        first.Dispose();
        using var rebound = CreateGuide(cache, otherStore, otherPayload, fixture.FarOrigin);
        using var fresh = CreateGuide(cache, otherStore, otherPayload, fixture.FarOrigin);
        first.Status.Should().Be(NavigationGuideStatus.Stale);
        first.Dispose();
        cache.ActiveLeaseCount.Should().Be(2);
        Vector3d actual = GetFoot(other.Graph, fixture.FarOrigin) + Vector3d.Forward * Fixed64.Quarter;

        NavigationFlowSample sample = AssertSameSample(
            rebound, fresh, actual, GenerousBudget, NavigationGuideStatus.Success);

        sample.Heading.Should().NotBe(Vector3d.Left);
        sample.Target.Should().Be(GetFoot(other.Graph, fixture.MarginOrigin));
    }

    private static GuideSampleWorkBudget GenerousBudget => new(128, 128, 8, 32, 32, 32, 1);

    private static NavigationAStarExitTestHarness.GraphFixture CreateMixedNativeFixture(GridWorld world)
    {
        var start = new VoxelIndex(1, 0, 0);
        var other = new VoxelIndex(2, 0, 0);
        return NavigationAStarExitTestHarness.CreateExplicitMap(
            world,
            NavigationAStarExitTestHarness.RectangularLine(3),
            new[] { default(VoxelIndex), start, other },
            "native-merge",
            new[]
            {
                new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                    "a-incoming", other, start, (Fixed64)10, Fixed64.One),
                new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                    "b-outgoing", start, other, (Fixed64)10, Fixed64.One)
            });
    }

    private static NavigationGuideStatus EnumerateSelectedEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        int ordinal,
        ref GuideSampleWorkMeter meter,
        out NavigationGraphEdge edge)
    {
        var edges = graph.EnumerateStructuralSurfaceEdges(source);
        int remainingSteps = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus status = edges.AdvanceOne(ref meter, ref remainingSteps);
            if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (status != NavigationSurfaceEdgeAdvanceStatus.Edge)
            {
                edge = default;
                return status == NavigationSurfaceEdgeAdvanceStatus.Blocked
                    ? NavigationGuideStatus.BudgetExceeded
                    : NavigationGuideStatus.Stale;
            }
            if (edges.CurrentOrdinal == ordinal)
            {
                edge = edges.Current;
                return NavigationGuideStatus.Success;
            }
        }
    }

    private static NavigationFlowFieldPayloadCache CreateCache(
        GridWorld world,
        NavigationFlowFieldPayload payload) => new(
        world, 1, payload.RetainedBytes, payload.RetainedBytes, payload.RetainedBytes,
        2, 8, NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

    private static NavigationFlowFieldLease CreateGuide(
        NavigationFlowFieldPayloadCache cache,
        NavigationWorldGraphStore store,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress origin)
    {
        if (cache.TryCheckout(store, store.Current, payload.Key, origin, out var payloadLease, out _)
            != NavigationFlowFieldStatus.Success)
        {
            cache.TryReservePayload(payload.RetainedBytes, out NavigationFlowFieldReservation reservation)
                .Should().BeTrue();
            cache.TryPublishOrPromote(store, payload, origin, ref reservation, out payloadLease)
                .Should().Be(NavigationFlowFieldStatus.Success);
        }
        cache.TryCreateGuide(store, new NavigationFlowQueryResult(origin, payloadLease), out var guide)
            .Should().Be(NavigationGuideStatus.Success);
        return guide;
    }

    private static NavigationFlowFieldPayload BuildFlow(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationAStarExitTestHarness.GraphFixture fixture,
        VoxelIndex start,
        VoxelIndex end) => NavigationFlowFieldCacheTestHarness.RunFlow(
        world, store, fixture.Graph,
        NavigationFlowFieldCacheTestHarness.ToFlowField(
            fixture.CreateQuery(start, end, fixture.DefaultProfile), Fixed64.Zero),
        new NavigationCellAddress(fixture.MapId, start),
        new NavigationCellAddress(fixture.MapId, end),
        NavigationFlowFieldStatus.Success);

    private static Vector3d GetFoot(NavigationWorldGraph graph, NavigationCellAddress address)
    {
        graph.TryGetNodeRef(address, out NavigationNodeRef node).Should().BeTrue();
        graph.TryGetNodeState(node, out NavigationNodeState state).Should().BeTrue();
        return state.FootAnchor;
    }

    private static void Warm(NavigationFlowFieldLease guide, Vector3d foot)
    {
        guide.TrySample(foot, GenerousBudget, out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Success);
        sample.Heading.Should().NotBe(Vector3d.Zero);
    }

    private static NavigationFlowSample AssertSameSample(
        NavigationFlowFieldLease warm,
        NavigationFlowFieldLease fresh,
        Vector3d foot,
        GuideSampleWorkBudget budget,
        NavigationGuideStatus expectedStatus)
    {
        var warmMeter = new GuideSampleWorkMeter(budget);
        var freshMeter = new GuideSampleWorkMeter(budget);
        warm.TrySample(foot, ref warmMeter, out NavigationFlowSample warmSample).Should().Be(expectedStatus);
        fresh.TrySample(foot, ref freshMeter, out NavigationFlowSample freshSample).Should().Be(expectedStatus);
        warmSample.Should().Be(freshSample);
        warmMeter.Should().Be(freshMeter, "repeated sampling must preserve every exact work category");
        if (expectedStatus != NavigationGuideStatus.Success)
            warmSample.Should().Be(default(NavigationFlowSample));
        return warmSample;
    }

    private static VoxelIndex FindHexPair(NormalizedGridConfiguration binding, out VoxelIndex target)
    {
        VoxelIndex offset = HexDirectionUtility.GetOffset(HexDirection.QPositive);
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int r = 1; r < binding.Length - 1; r++)
            {
                var source = new VoxelIndex(q, 0, r);
                target = new VoxelIndex(source.x + offset.x, source.y + offset.y, source.z + offset.z);
                if (binding.IsValidIndex(source) && binding.IsValidIndex(target))
                    return source;
            }
        }
        throw new InvalidOperationException("The test configuration has no adjacent hex cells.");
    }
}
