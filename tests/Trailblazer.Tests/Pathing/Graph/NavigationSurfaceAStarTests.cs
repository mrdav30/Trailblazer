//=======================================================================
// NavigationSurfaceAStarTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FluentAssertions;
using System;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationSurfaceAStarTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("astar", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void RetainedBytes_ShouldMatchMaximumForEmptyAndPopulatedLogicalLayouts()
    {
        var emptyDependencies = new GraphDependencyStamp(
            Policy.Key,
            new GraphComponentDependency[0],
            new GraphPageDependency[0]);
        var components = new[]
        {
            new GraphComponentDependency(
                new NavigationSurfaceComponentKey(
                    new NavigationCellAddress("map-a", default)),
                1),
            new GraphComponentDependency(
                new NavigationSurfaceComponentKey(
                    new NavigationCellAddress("map-b", default)),
                2)
        };
        var pages = new[]
        {
            new GraphPageDependency("map-a", 1, 0, 0, 1, 1),
            new GraphPageDependency("map-a", 1, 0, 1, 2, 2),
            new GraphPageDependency("map-b", 2, 0, 0, 3, 3),
            new GraphPageDependency("map-b", 2, 0, 1, 4, 4)
        };
        var populatedDependencies = new GraphDependencyStamp(
            Policy.Key,
            components,
            pages);

        emptyDependencies.RetainedBytes.Should().Be(48L);
        GraphDependencyStamp.GetRetainedBytes(componentCount: 0, pageCount: 0)
            .Should().Be(emptyDependencies.RetainedBytes);
        populatedDependencies.RetainedBytes.Should().Be(352L);
        GraphDependencyStamp.GetRetainedBytes(components.Length, pages.Length)
            .Should().Be(populatedDependencies.RetainedBytes);

        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.One),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var start = new NavigationCellAddress("map-a", default);
        var end = new NavigationCellAddress("map-b", new VoxelIndex(2, 0, 0));
        var key = new NavigationAStarPayloadKey(query, start, end);
        var emptyPayload = new NavigationAStarPayload(
            key,
            new NavigationCellAddress[0],
            Fixed64.Zero,
            emptyDependencies,
            NavigationSurfaceAStarStatus.NoPath);
        var populatedPayload = new NavigationAStarPayload(
            key,
            new[]
            {
                start,
                new NavigationCellAddress("map-a", new VoxelIndex(1, 0, 0)),
                end
            },
            Fixed64.One,
            populatedDependencies,
            NavigationSurfaceAStarStatus.Success);

        emptyPayload.RetainedBytes.Should().Be(384L);
        NavigationAStarPayload.GetMaximumRetainedBytes(0, 0, 0)
            .Should().Be(emptyPayload.RetainedBytes);
        populatedPayload.RetainedBytes.Should().Be(784L);
        NavigationAStarPayload.GetMaximumRetainedBytes(
                populatedPayload.Nodes.Length,
                components.Length,
                pages.Length)
            .Should().Be(populatedPayload.RetainedBytes);
    }

    [Fact]
    public void Advance_ShouldFindCanonicalFixedPointNativePathUnderUnitChunks()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)6, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        var addresses = new[]
        {
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        world.TryAddGrid(configuration, addresses, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(addresses[0], Cell)
            .AddCell(addresses[1], Cell)
            .AddCell(addresses[2], Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("map", addresses[0]),
                out NavigationSurfaceComponent component)
            .Should().BeTrue();
        component.AllSurfaceEdgesEuclideanCertified.Should().BeTrue();
        using NavigationWorldGraphStore store = CreateStore(
            graph,
            maxConcurrentLeases: 2);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        binding.TryGetCellPrism(addresses[0], out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(addresses[2], out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 64,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 3,
                maxEvaluatedEdges: 3,
                maxConnectionLegs: 0,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 4,
            componentCapacity: 6,
            nodeCapacity: 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            lease!,
            query,
            workspace.EndpointWorkspace,
            PathAlgorithm.AStar);
        for (int step = 0;
             step < 64 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(admission.Result, workspace);

        for (int step = 0;
             step < 64 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(
                lookupStepLimit: 1,
                nodeStepLimit: 1,
                edgeStepLimit: 1,
                connectionStepLimit: 1);
        }

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be((Fixed64)4);
        search.Result.Nodes.Should().Equal(
            new NavigationCellAddress("map", addresses[0]),
            new NavigationCellAddress("map", addresses[1]),
            new NavigationCellAddress("map", addresses[2]));
        workspace.NodeTable.TryGetSlot(admission.Result.Start.Node, out int startSlot)
            .Should().BeTrue();
        NavigationAStarNodeRecord startRecord = workspace.NodeTable.GetRecord(startSlot);
        startRecord.Heuristic.Should().Be((Fixed64)4);
        startRecord.EstimatedTotalCost.Should().Be((Fixed64)4);
        admission.Meter.ExpandedNodes.Should().Be(3);
        admission.Meter.EvaluatedEdges.Should().Be(3);
        search.Result.RetainedBytes.Should().BeGreaterThan(0);
        var resultBoundedCache = new NavigationAStarPayloadCache(
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: search.Result.RetainedBytes - 1,
            maxActivePayloadBytes: search.Result.RetainedBytes - 1);
        resultBoundedCache.TryReservePayload(
                search.Result.RetainedBytes,
                out NavigationAStarPayloadReservation rejectedReservation)
            .Should().BeFalse("one payload larger than the concrete cache ceiling is rejected");
        rejectedReservation.Should().Be(default(NavigationAStarPayloadReservation));

        long maximumPayloadBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            Math.Min(workspace.PathNodes.Length, query.Budget.MaxExpandedNodes),
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        var cache = new NavigationAStarPayloadCache(
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes,
            maxSinglePayloadBytes: maximumPayloadBytes);
        NavigationAStarPayloadLease canonicalLease = PublishPayload(cache, store, search.Result);
        canonicalLease.Payload.Should().BeSameAs(search.Result);
        cache.CachedBytes.Should().Be(search.Result.RetainedBytes);
        cache.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease checkoutLease)
            .Should().BeTrue();
        checkoutLease.Payload.Should().BeSameAs(search.Result);
        var duplicate = new NavigationAStarPayload(
            search.Result.Key,
            (NavigationCellAddress[])search.Result.Nodes.Clone(),
            search.Result.Cost,
            search.Result.Dependencies,
            search.Result.Status);
        FluentActions.Invoking(() => new NavigationAStarPayload(
                search.Result.Key,
                (NavigationCellAddress[])search.Result.Nodes.Clone(),
                search.Result.Cost,
                search.Result.Dependencies,
                NavigationSurfaceAStarStatus.BudgetExceeded))
            .Should().Throw<ArgumentException>(
                "terminal failures must never become reusable payloads");
        NavigationAStarPayloadLease racedLease = PublishPayload(cache, store, duplicate);
        racedLease.Payload.Should().BeSameAs(search.Result,
            "same-key publications converge on one immutable payload");
        NavigationAStarGuideLease? guide = null;
        NavigationGuideLease publicGuide = default;
        using (NavigationAStarQueryWork cachedQuery = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(cachedQuery, 64);
            cachedQuery.Status.Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease queryLease = cachedQuery.TakeResult();
            queryLease.Payload.Should().BeSameAs(search.Result);
            cache.TryCreateGuide(store, queryLease, out guide)
                .Should().Be(NavigationAStarQueryStatus.Success);
            guide.Should().NotBeNull();
            publicGuide = new NavigationGuideLease(guide!);
            publicGuide.TryGetCurrentWaypoint(
                    out NavigationCellAddress waypoint,
                    out Vector3d waypointPosition)
                .Should().Be(NavigationGuideStatus.Success);
            waypoint.Should().Be(new NavigationCellAddress("map", addresses[0]));
            waypointPosition.Should().Be(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z));
            store.ActiveLeaseCount.Should().Be(0,
                "a guide does not retain its short graph lease between calls");
            publicGuide.TryAdvanceWaypoint().Should().Be(NavigationGuideStatus.Success);
            publicGuide.CurrentWaypointIndex.Should().Be(1);
        }
        store.ActiveLeaseCount.Should().Be(0,
            "cached guide acquisition must not retain the graph snapshot lease");
        for (int i = 0; i < 8; i++)
        {
            cache.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmPayloadLease)
                .Should().BeTrue();
            cache.TryCreateGuide(store, warmPayloadLease, out NavigationAStarGuideLease? warmGuide)
                .Should().Be(NavigationAStarQueryStatus.Success);
            new NavigationGuideLease(warmGuide!).Dispose();
        }
        long beforeGuideCheckout = System.GC.GetAllocatedBytesForCurrentThread();
        bool guideCheckoutSucceeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (!cache.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmPayloadLease)
                || cache.TryCreateGuide(store, warmPayloadLease, out NavigationAStarGuideLease? warmGuide)
                    != NavigationAStarQueryStatus.Success)
            {
                guideCheckoutSucceeded = false;
                break;
            }
            new NavigationGuideLease(warmGuide!).Dispose();
        }
        long guideCheckoutAllocations =
            System.GC.GetAllocatedBytesForCurrentThread() - beforeGuideCheckout;
        guideCheckoutSucceeded.Should().BeTrue();
        guideCheckoutAllocations.Should().Be(0,
            "warmed guide checkout and return reuse cache-owned lease shells");
        NavigationWorldGraph topologyChanged = graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(graph.GraphVersion + 1);
        store.TryPublish(topologyChanged).Should().Be(NavigationCandidatePublication.Published);
        publicGuide.TryGetCurrentWaypoint(out _, out _)
            .Should().Be(NavigationGuideStatus.Stale);
        publicGuide.Status.Should().Be(NavigationGuideStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(4,
            "a stale guide remains bounded by the active lease ceiling until disposal");
        publicGuide.Dispose();
        cache.ActiveLeaseCount.Should().Be(3);
        cache.TryCheckout(search.Result.Key, topologyChanged, out _).Should().BeFalse();
        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(search.Result.RetainedBytes,
            "invalidating a checked-out payload must detach rather than invalidate its leases");
        canonicalLease.Payload.Should().BeSameAs(search.Result);
        checkoutLease.Payload.Should().BeSameAs(search.Result);
        racedLease.Payload.Should().BeSameAs(search.Result);
        racedLease.Dispose();
        checkoutLease.Dispose();
        canonicalLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        using NavigationWorldGraphStore capacityStore = CreateStore(graph);
        var undersized = new NavigationAStarPayloadCache(
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes - 1,
            maxSinglePayloadBytes: search.Result.RetainedBytes - 1);
        undersized.TryReservePayload(search.Result.RetainedBytes, out _).Should().BeFalse();
        undersized.Count.Should().Be(0);

        var detachedOnly = new NavigationAStarPayloadCache(
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: search.Result.RetainedBytes,
            maxActivePayloadBytes: search.Result.RetainedBytes);
        NavigationAStarPayloadLease detachedLease = PublishPayload(
            detachedOnly,
            capacityStore,
            search.Result);
        detachedOnly.Count.Should().Be(0);
        detachedOnly.CachedBytes.Should().Be(0);
        detachedOnly.DetachedBytes.Should().Be(search.Result.RetainedBytes);
        detachedOnly.TryReservePayload(duplicate.RetainedBytes, out _).Should().BeFalse(
            "the exact active-payload ceiling cannot retain a second detached result");
        detachedLease.Dispose();
        detachedOnly.DetachedBytes.Should().Be(0);
        NavigationAStarPayloadLease recoveredDetachedLease = PublishPayload(
            detachedOnly,
            capacityStore,
            duplicate);
        recoveredDetachedLease.Dispose();

        var leaseCapped = new NavigationAStarPayloadCache(
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes,
            maxSinglePayloadBytes: search.Result.RetainedBytes,
            maxActivePayloadBytes: search.Result.RetainedBytes,
            maxActiveLeases: 1);
        NavigationAStarPayloadLease soleLease = PublishPayload(
            leaseCapped,
            capacityStore,
            search.Result);
        leaseCapped.TryCheckout(search.Result.Key, graph, out _).Should().BeFalse(
            "same-payload checkout count is independently bounded from retained bytes");
        soleLease.Dispose();
        leaseCapped.TryCheckout(
                search.Result.Key,
                graph,
                out NavigationAStarPayloadLease recoveredLease)
            .Should().BeTrue();
        recoveredLease.Dispose();

        NavigationAStarPayload second = ClonePayload(
            search.Result,
            new NavigationCellAddress("map", new VoxelIndex(3, 0, 0)));
        NavigationAStarPayload third = ClonePayload(
            search.Result,
            new NavigationCellAddress("map", new VoxelIndex(4, 0, 0)));
        long twoPayloadBytes = checked(search.Result.RetainedBytes + second.RetainedBytes);
        var lru = new NavigationAStarPayloadCache(
            maxEntries: 2,
            maxReusableBytes: twoPayloadBytes,
            maxSinglePayloadBytes: search.Result.RetainedBytes);
        NavigationAStarPayloadLease firstLease = PublishPayload(lru, capacityStore, search.Result);
        NavigationAStarPayloadLease secondLease = PublishPayload(lru, capacityStore, second);
        firstLease.Dispose();
        lru.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease recentLease)
            .Should().BeTrue();
        recentLease.Dispose();
        NavigationAStarPayloadLease thirdLease = PublishPayload(lru, capacityStore, third);

        lru.TryCheckout(second.Key, graph, out _).Should().BeFalse(
            "the least-recently-used entry is evicted deterministically");
        lru.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease retainedLease)
            .Should().BeTrue();
        retainedLease.Dispose();
        secondLease.Payload.Should().BeSameAs(second,
            "an evicted active payload remains valid until its final lease returns");
        lru.DetachedBytes.Should().Be(second.RetainedBytes);
        secondLease.Dispose();
        lru.DetachedBytes.Should().Be(0);
        thirdLease.Dispose();
        lru.ActiveLeaseCount.Should().Be(0);
        for (int i = 0; i < 8; i++)
        {
            lru.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmLease)
                .Should().BeTrue();
            warmLease.Dispose();
        }
        long beforeCheckout = System.GC.GetAllocatedBytesForCurrentThread();
        bool checkoutSucceeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (!lru.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease hotLease))
            {
                checkoutSucceeded = false;
                break;
            }
            hotLease.Dispose();
        }
        long checkoutAllocations =
            System.GC.GetAllocatedBytesForCurrentThread() - beforeCheckout;
        checkoutSucceeded.Should().BeTrue();
        checkoutAllocations.Should().Be(0,
            "warmed cache-hit checkout and return use a cache-owned lease shell");
    }

    [Fact]
    public void Advance_ShouldTraverseAutomaticSeamAndCaptureBothMapPages()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        var targetCenter = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = new(
            targetCenter,
            targetCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation sourceOperation = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("source", sourceBinding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        NavigationMapCommitOperation targetOperation = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("target", targetBinding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(sourceOperation).Should().BeTrue();
        context.Pathing.Admit(targetOperation).Should().BeTrue();
        for (int frame = 0;
            frame < 512 && targetOperation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
        sourceOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph;
        using (NavigationWorldGraphLease published =
            context.Pathing.TryAcquireNavigationGraph()!)
        {
            published.Graph.AutomaticSeams.PairCount.Should().Be(1);
            graph = published.Graph.WithAreaCatalog(
                catalog,
                published.Graph.GraphVersion);
        }
        using NavigationWorldGraphStore store = CreateStore(graph);
        GridCellPrism sourcePrism = GetPrism(sourceBinding, default);
        GridCellPrism targetPrism = GetPrism(targetBinding, default);
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                sourcePrism.Center.X,
                sourcePrism.VerticalMin,
                sourcePrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                targetPrism.Center.X,
                targetPrism.VerticalMin,
                targetPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 2, 2, 1, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(2, 4, 6, 4);
        var cache = new NavigationAStarPayloadCache(1);
        using NavigationAStarQueryWork queryWork = BeginReservedQuery(
            context.World,
            store,
            query,
            workspace,
            cache);
        DrainQuery(queryWork, 64);
        queryWork.Status.Should().Be(NavigationAStarQueryStatus.Success);
        NavigationAStarPayloadLease payloadLease = queryWork.TakeResult();
        NavigationAStarPayload payload = payloadLease.Payload;
        payload.Nodes.Should().Equal(
            new NavigationCellAddress("source", default),
            new NavigationCellAddress("target", default));
        payload.Dependencies.Pages.Should().Contain(
            dependency => dependency.MapId == "source");
        payload.Dependencies.Pages.Should().Contain(
            dependency => dependency.MapId == "target");
        payloadLease.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Advance_ShouldCapturePageReadFromImpassableAlternative()
    {
        const int LastAddress = 64;
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)132, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        var addresses = new VoxelIndex[LastAddress + 1];
        for (int i = 0; i < addresses.Length; i++)
            addresses[i] = new VoxelIndex(i, 0, 0);
        world.TryAddGrid(configuration, addresses, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i < addresses.Length; i++)
        {
            NavigationCell cell = i == LastAddress
                ? new NavigationCell(
                    TraversalMedia.Gas,
                    TraversalCapability.None,
                    default,
                    Fixed64.Zero,
                    (Fixed64)4,
                    (Fixed64)4)
                : Cell;
            builder.AddCell(addresses[i], cell);
        }
        NavigationMap map = builder.Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        binding.TryGetCellPrism(addresses[63], out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(addresses[0], out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 256,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 64,
                maxEvaluatedEdges: 126,
                maxConnectionLegs: 0,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 4,
            componentCapacity: 6,
            nodeCapacity: 65);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            lease!,
            query,
            workspace.EndpointWorkspace,
            PathAlgorithm.AStar);
        for (int step = 0;
             step < 256 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(admission.Result, workspace);

        for (int step = 0;
             step < 2_048 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(1, 1, 1, 1);
        }

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Dependencies.Pages.Should().HaveCount(2);
        search.Result.Dependencies.Pages[0].PageIndex.Should().Be(0);
        search.Result.Dependencies.Pages[1].PageIndex.Should().Be(1,
            "the denied neighbor's semantic/physical page was evaluated");
    }

    [Fact]
    public void Advance_ShouldStampDisconnectedExplicitWitnessComponentAndPage()
    {
        using var world = new GridWorld();
        var sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var destinationCenter = new Vector3d(10, 0, 0);
        var destinationConfiguration = new GridConfiguration(
            destinationCenter,
            destinationCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var witnessCenter = new Vector3d(20, 0, 0);
        var witnessMaximum = new Vector3d(24, 0, 0);
        var witnessConfiguration = new GridConfiguration(
            witnessCenter,
            witnessMaximum,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(destinationConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(witnessConfiguration, out ushort witnessGridIndex)
            .Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        destinationConfiguration.TryNormalize(
                out NormalizedGridConfiguration destinationBinding)
            .Should().BeTrue();
        witnessConfiguration.TryNormalize(out NormalizedGridConfiguration witnessBinding)
            .Should().BeTrue();
        GridCellPrism sourcePrism = GetPrism(sourceBinding, default);
        GridCellPrism destinationPrism = GetPrism(destinationBinding, default);
        var sourceFoot = new Vector3d(
            sourcePrism.Center.X,
            sourcePrism.VerticalMin,
            sourcePrism.Center.Z);
        var destinationFoot = new Vector3d(
            destinationPrism.Center.X,
            destinationPrism.VerticalMin,
            destinationPrism.Center.Z);
        NavigationMap sourceMap = new NavigationMapBuilder("A", sourceBinding)
            .AddCell(default, Cell)
            .Build();
        NavigationMap destinationMap = new NavigationMapBuilder("B", destinationBinding)
            .AddCell(default, Cell)
            .Build();
        NavigationMap witnessMap = new NavigationMapBuilder("C", witnessBinding)
            .AddCell(default, Cell)
            .AddCell(new VoxelIndex(2, 0, 0), Cell)
            .AddCell(new VoxelIndex(4, 0, 0), Cell)
            .Build();
        NavigationOperationCandidate.MapState CreateState(NavigationMap map)
        {
            var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
            return new NavigationOperationCandidate.MapState(
                prepared.Map,
                prepared.BakeVersion,
                prepared.RetainedBytes,
                NavigationMapOverlayState.Empty,
                dynamicSlotGeneration: 0,
                bakedCellLookup: prepared.BakedCellLookup);
        }
        NavigationOperationCandidate.MapState sourceState = CreateState(sourceMap);
        NavigationOperationCandidate.MapState destinationState = CreateState(destinationMap);
        NavigationOperationCandidate.MapState witnessState = CreateState(witnessMap);
        NavigationMapInstance sourceInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState,
            previous: null,
            instanceVersion: 1);
        NavigationMapInstance destinationInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            destinationState,
            previous: null,
            instanceVersion: 1);
        NavigationMapInstance witnessInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            witnessState,
            previous: null,
            instanceVersion: 1);
        var connection = new NavigationConnection(
            "a-to-b",
            default,
            new NavigationCellAddress("B", default),
            sourceFoot,
            destinationFoot,
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[]
            {
                new NavigationCellAddress("C", default),
                new NavigationCellAddress("C", new VoxelIndex(2, 0, 0)),
                new NavigationCellAddress("C", new VoxelIndex(4, 0, 0))
            });
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("A", connection.Id),
            connection,
            isActive: true,
            corridorCost: (Fixed64)20,
            NavigationPagedSequence<Vector3d>.Empty);
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        var endpointRowBuilder =
            new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        endpointRowBuilder.Append(record.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpointRow =
            endpointRowBuilder.Seal();
        connections = connections.SetEndpointRow(
            record.Source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            endpointRow,
            out _);
        connections = connections.SetEndpointRow(
            record.Destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            endpointRow,
            out _);
        NavigationWorldGraph graph = CreateGraph(
            new[] { sourceInstance, destinationInstance, witnessInstance },
            connections);
        var witnessComponents = new NavigationSurfaceComponentKey[3];
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", default),
                out witnessComponents[0],
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", new VoxelIndex(2, 0, 0)),
                out witnessComponents[1],
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", new VoxelIndex(4, 0, 0)),
                out witnessComponents[2],
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("A", default),
                out NavigationSurfaceComponentKey sourceComponent,
                out _)
            .Should().BeTrue();
        witnessComponents.Should().OnlyHaveUniqueItems();
        witnessComponents.Should().NotContain(sourceComponent,
            "same-page witnesses remain structurally disconnected from the explicit endpoints");
        using NavigationWorldGraphStore store = CreateStore(graph);
        var query = new PathQuery(
            new NavigationEndpoint(sourceFoot, mapId: "A"),
            new NavigationEndpoint(destinationFoot, mapId: "B"),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(128, 2, 2, 1, 4, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var insufficientWorkspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 3,
            componentCapacity: 3,
            nodeCapacity: 4);
        var insufficientCache = new NavigationAStarPayloadCache(1);
        using (NavigationAStarQueryWork insufficient = BeginReservedQuery(
            world,
            store,
            query,
            insufficientWorkspace,
            insufficientCache))
        {
            DrainQuery(insufficient, 256);
            insufficient.Status.Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
        }
        var workspace = new NavigationAStarWorkspace(3, 3, 4, 4);
        var cache = new NavigationAStarPayloadCache(1);
        GraphDependencyStamp dependencies;
        using (NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(work, 256);
            work.Status.Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease payloadLease = work.TakeResult();
            payloadLease.Payload.Nodes.Should().Equal(
                new NavigationCellAddress("A", default),
                new NavigationCellAddress("B", default));
            foreach (NavigationSurfaceComponentKey witnessComponent in witnessComponents)
            {
                payloadLease.Payload.Dependencies.Components.Should().ContainSingle(
                    dependency => dependency.Key == witnessComponent);
            }
            payloadLease.Payload.Dependencies.Pages.Should().ContainSingle(
                dependency => dependency.MapId == "C" && dependency.PageIndex == 0);
            dependencies = payloadLease.Payload.Dependencies;
            graph.IsDependencyCurrent(dependencies).Should().BeTrue();
            payloadLease.Dispose();
        }

        VoxelGrid witnessGrid = world.ActiveGrids[witnessGridIndex];
        witnessGrid.TryGetVoxel(default(VoxelIndex), out Voxel? witnessVoxel)
            .Should().BeTrue();
        witnessGrid.TryAddObstacle(
                witnessVoxel!,
                world.AllocateObstacleToken())
            .Should().BeTrue();
        NavigationMapInstance changedWitness = NavigationMapInstanceTestFactory.Compose(
            world,
            witnessState,
            witnessInstance,
            instanceVersion: 2);
        NavigationWorldGraph changedGraph = CreateGraph(
            new[] { sourceInstance, destinationInstance, changedWitness },
            connections);
        changedGraph.IsDependencyCurrent(dependencies).Should().BeFalse(
            "a physical mutation of the consumed witness page invalidates the result");
    }

    [Theory]
    [InlineData(4, (int)NavigationSurfaceAStarStatus.Success)]
    [InlineData(1, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    public void Advance_ShouldMeterEveryExplicitConnectionLeg(
        int maximumConnectionLegs,
        int expectedStatusValue)
    {
        NavigationSurfaceAStarStatus expectedStatus =
            (NavigationSurfaceAStarStatus)expectedStatusValue;
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)130, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex sourceIndex = default;
        VoxelIndex witnessIndex = new(65, 0, 0);
        VoxelIndex destinationIndex = new(2, 0, 0);
        world.TryAddGrid(
                configuration,
                new[] { sourceIndex, witnessIndex, destinationIndex },
                out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var mapBuilder = new NavigationMapBuilder("map", binding)
            .AddCell(sourceIndex, Cell)
            .AddCell(destinationIndex, Cell);
        for (int i = 3; i <= witnessIndex.x; i++)
            mapBuilder.AddCell(new VoxelIndex(i, 0, 0), Cell);
        NavigationMap map = mapBuilder.Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        binding.TryGetCellPrism(sourceIndex, out GridCellPrism sourcePrism).Should().BeTrue();
        binding.TryGetCellPrism(destinationIndex, out GridCellPrism destinationPrism)
            .Should().BeTrue();
        var definition = new NavigationConnection(
            "shortcut",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            new Vector3d(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z),
            new Vector3d(
                destinationPrism.Center.X,
                destinationPrism.VerticalMin,
                destinationPrism.Center.Z),
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[] { new NavigationCellAddress("map", witnessIndex) });
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("map", definition.Id),
            definition,
            isActive: true,
            corridorCost: (Fixed64)8,
            NavigationPagedSequence<Vector3d>.Empty);
        var alternateDefinition = new NavigationConnection(
            "z-shortcut",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            definition.EntryAnchor,
            definition.ExitAnchor,
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[] { new NavigationCellAddress("map", witnessIndex) });
        var alternateRecord = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("map", alternateDefinition.Id),
            alternateDefinition,
            isActive: true,
            corridorCost: (Fixed64)8,
            NavigationPagedSequence<Vector3d>.Empty);
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        connections = connections.SetOwner(alternateRecord, out _);
        var ownerRowBuilder =
            new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        ownerRowBuilder.Append(new NavigationConnectionOwnerKey("map", "a-missing"));
        ownerRowBuilder.Append(record.Owner);
        ownerRowBuilder.Append(alternateRecord.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> ownerRow =
            ownerRowBuilder.Seal();
        connections = connections.SetEndpointRow(
            record.Source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            ownerRow,
            out _);
        connections = connections.SetEndpointRow(
            record.Destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            ownerRow,
            out _);
        NavigationWorldGraph graph = CreateGraph(instance, connections);
        graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("map", default),
                out NavigationSurfaceComponent component)
            .Should().BeTrue();
        component.AllSurfaceEdgesEuclideanCertified.Should().BeFalse(
                "an active uncertified self-edge disables the component heuristic even when the route does not use it");
        using NavigationWorldGraphStore store = CreateStore(graph);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        var query = new PathQuery(
            new NavigationEndpoint(definition.EntryAnchor),
            new NavigationEndpoint(definition.ExitAnchor),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 64,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 2,
                maxEvaluatedEdges: 3,
                maxConnectionLegs: maximumConnectionLegs,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 4, 6, 4);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            lease!,
            query,
            workspace.EndpointWorkspace,
            PathAlgorithm.AStar);
        for (int step = 0;
             step < 64 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(1, 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(admission.Result, workspace);

        for (int step = 0;
             step < 64 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(1, 1, 1, 1);
        }

        search.Status.Should().Be(expectedStatus);
        admission.Meter.EvaluatedEdges.Should().Be(
            expectedStatus == NavigationSurfaceAStarStatus.Success ? 3 : 2,
            "each raw endpoint-row owner is bounded even when its record is missing");
        admission.Meter.ConnectionLegs.Should().Be(maximumConnectionLegs);
        if (expectedStatus == NavigationSurfaceAStarStatus.Success)
        {
            search.Result.Cost.Should().Be((Fixed64)8);
            workspace.NodeTable.TryGetSlot(admission.Result.Start.Node, out int startSlot)
                .Should().BeTrue();
            workspace.NodeTable.GetRecord(startSlot).Heuristic.Should().Be(Fixed64.Zero);
            search.Result.Dependencies.Pages.Should().ContainSingle(
                page => page.PageIndex == 1,
                "the explicit witness page was read during corridor evaluation");
        }
        else
        {
            search.Result.Should().BeNull(
                "an exhausted query has no reusable route or complete dependency payload");
        }
    }

    [Fact]
    public void Advance_ShouldProduceDependencyStampedNoPathPayload()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)6, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex startIndex = default;
        VoxelIndex endIndex = new(2, 0, 0);
        world.TryAddGrid(configuration, new[] { startIndex, endIndex }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(startIndex, Cell)
            .AddCell(endIndex, Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        binding.TryGetCellPrism(startIndex, out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(endIndex, out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(32, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 2, 4, 2);
        var cache = new NavigationAStarPayloadCache(1);
        using NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        DrainQuery(work, 64);

        work.Status.Should().Be(NavigationAStarQueryStatus.NoPath);
        store.ActiveLeaseCount.Should().Be(0,
            "negative query completion must release its graph snapshot lease");
        var key = new NavigationAStarPayloadKey(
            query,
            new NavigationCellAddress("map", startIndex),
            new NavigationCellAddress("map", endIndex));
        cache.TryCheckout(key, graph, out NavigationAStarPayloadLease payloadLease)
            .Should().BeTrue("dependency-stamped negative results remain reusable");
        payloadLease.Payload.Status.Should().Be(NavigationSurfaceAStarStatus.NoPath);
        payloadLease.Payload.HasPath.Should().BeFalse();
        payloadLease.Payload.Dependencies.Pages.Should().NotBeEmpty();
        payloadLease.Dispose();
    }

    private static NavigationWorldGraph CreateGraph(
        NavigationMapInstance instance,
        NavigationExplicitConnectionIndex? explicitConnections = null) =>
        CreateGraph(new[] { instance }, explicitConnections);

    private static NavigationWorldGraph CreateGraph(
        NavigationMapInstance[] instances,
        NavigationExplicitConnectionIndex? explicitConnections = null)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        var graph = new NavigationWorldGraph(
            1,
            instances,
            areaCatalog: catalog,
            explicitConnections: explicitConnections);
        return graph.WithSurfaceComponents(NavigationSurfaceComponentTestFactory.Build(graph));
    }

    private static NavigationWorldGraphStore CreateStore(
        NavigationWorldGraph graph,
        int maxConcurrentLeases = 1)
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        return store;
    }

    private static NavigationAStarPayload ClonePayload(
        NavigationAStarPayload source,
        NavigationCellAddress end) => new(
        new NavigationAStarPayloadKey(source.Key.Query, source.Key.Start, end),
        (NavigationCellAddress[])source.Nodes.Clone(),
        source.Cost,
        source.Dependencies,
        source.Status);

    private static NavigationAStarPayloadLease PublishPayload(
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
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return lease;
    }

    private static NavigationAStarQueryWork BeginReservedQuery(
        GridWorld world,
        NavigationWorldGraphStore store,
        PathQuery query,
        NavigationAStarWorkspace workspace,
        NavigationAStarPayloadCache cache)
    {
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        graphLease.Should().NotBeNull();
        long maximumBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            Math.Min(workspace.PathNodes.Length, query.Budget.MaxExpandedNodes),
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        var work = new NavigationAStarQueryWork(world, store, workspace, cache);
        work.BeginReserved(query, graphLease!, ref reservation);
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return work;
    }

    private static void DrainQuery(NavigationAStarQueryWork work, int stepLimit)
    {
        for (int step = 0; step < stepLimit && !work.IsPrepared; step++)
            work.PrepareSearchOrCheckout(1, 1);
        for (int step = 0; step < stepLimit && !work.IsReadyToPublish; step++)
            work.AdvanceSearch(1, 1, 1, 1);
        if (work.IsReadyToPublish)
            work.Publish();
    }

    private static GridCellPrism GetPrism(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return prism;
    }

    private static NavigationAgentProfile Profile() => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);
}
