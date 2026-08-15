//=======================================================================
// NavigationEndpointResolutionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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

[Collection("PathingCollection")]
public sealed class NavigationEndpointResolutionTests
{
    [Fact]
    public void ComponentDependencyCapacity_ShouldBeIndependentFromEndpointPages()
    {
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 1,
            nodeCapacity: 1,
            componentCapacity: 3);

        workspace.TryRecordEndpointComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", new VoxelIndex(0, 0, 0))))
            .Should().BeTrue();
        workspace.TryRecordEndpointComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", new VoxelIndex(1, 0, 0))))
            .Should().BeTrue();
        workspace.TryRecordEndpointComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", new VoxelIndex(2, 0, 0))))
            .Should().BeTrue();
        workspace.EndpointComponentCount.Should().Be(3,
            "same-page explicit witnesses may belong to independent weak components");
    }

    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("endpoint", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void NearestNavigable_ShouldApplyMapFilterBeforeDistanceRanking()
    {
        using var world = new GridWorld();
        GridConfiguration selectedConfiguration = CreateConfiguration(Fixed64.Zero);
        GridConfiguration closerConfiguration = CreateConfiguration(Fixed64.One);
        NavigationMapInstance selected = CreateInstance(
            world,
            "selected",
            selectedConfiguration,
            physicallyPresent: true);
        NavigationMapInstance closer = CreateInstance(
            world,
            "closer",
            closerConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = WithSurfaceComponents(
            new NavigationWorldGraph(1, new[] { selected, closer }));
        selectedConfiguration.TryNormalize(out NormalizedGridConfiguration selectedBinding)
            .Should().BeTrue();
        closerConfiguration.TryNormalize(out NormalizedGridConfiguration closerBinding)
            .Should().BeTrue();
        selectedBinding.TryGetCellPrism(default, out GridCellPrism selectedPrism)
            .Should().BeTrue();
        closerBinding.TryGetCellPrism(default, out GridCellPrism closerPrism)
            .Should().BeTrue();
        Vector3d requested = new(
            selectedPrism.Center.X + (Fixed64)0.75,
            selectedPrism.VerticalMin,
            selectedPrism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 4,
            componentCapacity: 6);
        var meter = new NavigationWorkMeter(CreateBudget(64, 8));
        var evaluator = new TraversalEvaluator(graph, Profile(), Policy, TraversalMedium.Solid);

        var unfiltered = new NavigationEndpointResolutionWork(
            world,
            graph,
            new NavigationEndpoint(
                requested,
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.One),
            evaluator,
            meter,
            workspace);
        Drain(unfiltered);

        unfiltered.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        unfiltered.Result.Address.MapId.Should().Be("closer");
        graph.TryGetNodeState(unfiltered.Result.Node, out NavigationNodeState unfilteredState)
            .Should().BeTrue();
        unfilteredState.FootAnchor.X.Should().Be(closerPrism.Center.X);

        workspace.Reset();
        meter.Reset(CreateBudget(64, 8));
        var filtered = new NavigationEndpointResolutionWork(
            world,
            graph,
            new NavigationEndpoint(
                requested,
                "selected",
                EndpointResolutionPolicy.NearestNavigable,
                Fixed64.One),
            evaluator,
            meter,
            workspace);
        Drain(filtered);

        filtered.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        filtered.Result.Address.MapId.Should().Be("selected");
        graph.TryGetNodeState(filtered.Result.Node, out NavigationNodeState filteredState)
            .Should().BeTrue();
        filteredState.FootAnchor.X.Should().Be(selectedPrism.Center.X);
    }

    [Fact]
    public void Strict_ShouldRejectAuthoredCellWhenPhysicalVoxelIsAbsent()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: false);
        NavigationWorldGraph graph = WithSurfaceComponents(
            new NavigationWorldGraph(1, new[] { instance }));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z),
                resolution: EndpointResolutionPolicy.Strict),
            new TraversalEvaluator(graph, Profile(), Policy, TraversalMedium.Solid),
            meter,
            workspace);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.InvalidEndpoint);
        meter.EndpointCandidates.Should().Be(1,
            "the topology address is charged before graph-authored physical filtering");
    }

    [Fact]
    public void Advance_ShouldReturnStaleWhenGridChangesBetweenBoundedChunks()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = WithSurfaceComponents(
            new NavigationWorldGraph(1, new[] { instance }));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)),
            new TraversalEvaluator(graph, Profile(), Policy, TraversalMedium.Solid),
            meter,
            workspace);

        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationEndpointResolutionStatus.Pending);
        world.TryGetGrid(0, out VoxelGrid? grid).Should().BeTrue();
        grid.Should().NotBeNull();
        grid!.TryAddVoxel(new VoxelIndex(1, 0, 0), out _).Should().BeTrue();

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Stale);
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
    }

    [Fact]
    public void Advance_ZeroChunkLimits_ShouldYieldWithoutSpendingQueryBudget()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = WithSurfaceComponents(
            new NavigationWorldGraph(1, new[] { instance }));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)),
            new TraversalEvaluator(graph, Profile(), Policy, TraversalMedium.Solid),
            meter,
            workspace);

        work.Advance(lookupStepLimit: 0, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationEndpointResolutionStatus.Pending);
        meter.LookupProbes.Should().Be(0);
        meter.EndpointCandidates.Should().Be(0);

        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationEndpointResolutionStatus.Pending);
        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationEndpointResolutionStatus.Pending);
        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationEndpointResolutionStatus.Pending);
        meter.EndpointCandidates.Should().Be(0,
            "a local output pause must retain rather than consume the pending address");

        Drain(work);
        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
    }

    [Fact]
    public void PhysicalEnvelopeOutsideAuthoredCells_ShouldAdvanceExactGridHighWaterOnly()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationGridGenerationIdentity identity = instance.GridIdentity;
        ulong nextHighWater = instance.GridHighWaterSequence + 1;
        var envelope = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.SparseVoxelAdded,
            voxelIndex: new VoxelIndex(1, 0, 0),
            changeStamp: new GridChangeStamp(nextHighWater, nextHighWater),
            hasVoxelState: true,
            isVoxelPresent: true);

        NavigationMapInstance next = instance.Apply(
            identity.WorldSpawnToken,
            envelope,
            instanceVersion: 2);

        next.Should().NotBeSameAs(instance);
        next.GridHighWaterSequence.Should().Be(nextHighWater);
        next.PhysicalVersion.Should().Be(instance.PhysicalVersion,
            "an unauthored physical address does not change an effective page");
    }

    [Fact]
    public void QueryAdmission_ShouldResolveBothEndpoints()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph = WithSurfaceComponents(new NavigationWorldGraph(
            1,
            new[] { instance },
            areaCatalog: catalog));
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d point = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var query = new PathQuery(
            new NavigationEndpoint(point),
            new NavigationEndpoint(point),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            CreateBudget(32, 4),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4);
        using var work = new NavigationQueryAdmissionWork(world, lease!, query, workspace);

        for (int step = 0;
             step < 64 && work.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }

        work.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using NavigationResolvedPathQuery result = work.Result;
        result.Graph.Should().BeSameAs(graph);
        result.Start.Address.Should().Be(new NavigationCellAddress("map", default));
        result.End.Address.Should().Be(result.Start.Address);
        result.AreaPolicy.Should().BeSameAs(Policy);
    }

    [Fact]
    public void QueryAdmission_ShouldSelectCanonicalOverlappingEndpoint()
    {
        using var world = new GridWorld();
        GridConfiguration laterMapIdConfiguration = CreateConfiguration(Fixed64.Zero);
        GridConfiguration earlierMapIdConfiguration = CreateConfiguration(Fixed64.One);
        GridConfiguration noCandidateConfiguration = CreateConfiguration((Fixed64)100);
        NavigationMapInstance laterMapId = CreateInstance(
            world,
            "z-map",
            laterMapIdConfiguration,
            physicallyPresent: true);
        NavigationMapInstance earlierMapId = CreateInstance(
            world,
            "a-map",
            earlierMapIdConfiguration,
            physicallyPresent: true);
        NavigationMapInstance noCandidate = CreateInstance(
            world,
            "m-map",
            noCandidateConfiguration,
            physicallyPresent: true);
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph = WithSurfaceComponents(new NavigationWorldGraph(
            1,
            new[] { laterMapId, earlierMapId, noCandidate },
            areaCatalog: catalog));
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        laterMapIdConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d point = new(
            prism.Center.X + (Fixed64)0.75,
            prism.VerticalMin,
            prism.Center.Z);
        var query = new PathQuery(
            new NavigationEndpoint(
                point,
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.One),
            new NavigationEndpoint(
                point,
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.One),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            CreateBudget(256, 32),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 4,
            componentCapacity: 6);
        using var work = new NavigationQueryAdmissionWork(world, lease!, query, workspace);

        for (int step = 0;
             step < 128 && work.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }

        work.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using NavigationResolvedPathQuery result = work.Result;
        result.Start.Address.MapId.Should().Be("a-map");
    }

    [Fact]
    public void QueryAdmission_DefaultProfile_ShouldRejectAndReleaseSnapshotLease()
    {
        using var world = new GridWorld();
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 1,
            componentCapacity: 3);

        using var work = new NavigationQueryAdmissionWork(
            world,
            lease!,
            default,
            workspace);

        work.Status.Should().Be(NavigationQueryAdmissionStatus.InvalidProfile);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void DependencyStampWork_ShouldDebitEachCapturedComponentAndPage()
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateConfiguration(Fixed64.Zero),
            physicallyPresent: true);
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
            new[] { instance },
            areaCatalog: catalog);
        NavigationSurfaceComponentIndex componentIndex =
            NavigationSurfaceComponentTestFactory.Build(graph);
        graph = new NavigationWorldGraph(
            1,
            new[] { instance },
            areaCatalog: catalog,
            surfaceComponents: componentIndex);
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("map", default),
                out NavigationSurfaceComponentKey component,
                out _)
            .Should().BeTrue();
        var components = new[] { component };
        var pages = new[] { new GraphPageDependencyAddress("map", 0) };
        var meter = new NavigationWorkMeter(CreateBudget(2, 0));
        var work = new NavigationDependencyStampWork(
            graph,
            Policy,
            components,
            components.Length,
            pages,
            pages.Length);

        work.Advance(meter, lookupStepLimit: 1).Should().BeFalse();
        meter.LookupProbes.Should().Be(1);
        work.Advance(meter, lookupStepLimit: 1).Should().BeTrue();

        meter.LookupProbes.Should().Be(2);
        work.IsValid.Should().BeTrue();
        work.Result.Components.Should().ContainSingle();
        work.Result.Pages.Should().ContainSingle();
    }

    [Fact]
    public void QueryAdmission_ShouldAcceptExactSharedLookupBudgetAndRejectOneBelow()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d point = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);

        using (NavigationWorldGraphStore exactStore = CreateStore(graph))
        {
            NavigationWorldGraphLease? lease = exactStore.TryAcquire();
            lease.Should().NotBeNull();
            var query = CreateSurfaceQuery(point, CreateBudget(7, 2));
            using var exact = new NavigationQueryAdmissionWork(
                world,
                lease!,
                query,
                new NavigationAStarWorkspace(
                    mapCapacity: 1,
                    endpointPageCapacity: 2,
                    componentCapacity: 4));

            Drain(exact);

            exact.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
            exact.Meter.LookupProbes.Should().Be(7);
            exact.Meter.EndpointCandidates.Should().Be(2);
            exact.Result.Dispose();
        }

        using (NavigationWorldGraphStore belowStore = CreateStore(graph))
        {
            NavigationWorldGraphLease? lease = belowStore.TryAcquire();
            lease.Should().NotBeNull();
            var query = CreateSurfaceQuery(point, CreateBudget(6, 2));
            using var below = new NavigationQueryAdmissionWork(
                world,
                lease!,
                query,
                new NavigationAStarWorkspace(
                    mapCapacity: 1,
                    endpointPageCapacity: 2,
                    componentCapacity: 4));

            Drain(below);

            below.Status.Should().Be(NavigationQueryAdmissionStatus.BudgetExceeded);
            below.Meter.LookupProbes.Should().Be(6);
            belowStore.ActiveLeaseCount.Should().Be(0);
        }
    }

    private static void Drain(NavigationEndpointResolutionWork work)
    {
        for (int step = 0;
             step < 256 && work.Status == NavigationEndpointResolutionStatus.Pending;
             step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        work.Status.Should().NotBe(NavigationEndpointResolutionStatus.Pending);
    }

    private static void Drain(NavigationQueryAdmissionWork work)
    {
        for (int step = 0;
             step < 256 && work.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        work.Status.Should().NotBe(NavigationQueryAdmissionStatus.Pending);
    }

    private static NavigationMapInstance CreateInstance(
        GridWorld world,
        string mapId,
        GridConfiguration configuration,
        bool physicallyPresent)
    {
        VoxelIndex[] physical = physicallyPresent
            ? new[] { default(VoxelIndex) }
            : System.Array.Empty<VoxelIndex>();
        world.TryAddGrid(configuration, physical, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder(mapId, binding)
            .AddCell(default, Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        return NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
    }

    private static NavigationWorldGraph CreateAdmissionGraph(
        params NavigationMapInstance[] instances)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        return WithSurfaceComponents(new NavigationWorldGraph(
            1,
            instances,
            areaCatalog: catalog));
    }

    private static NavigationWorldGraph WithSurfaceComponents(NavigationWorldGraph graph) =>
        graph.WithSurfaceComponents(NavigationSurfaceComponentTestFactory.Build(graph));

    private static NavigationWorldGraphStore CreateStore(NavigationWorldGraph graph)
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        return store;
    }

    private static PathQuery CreateSurfaceQuery(
        Vector3d point,
        NavigationWorkBudget budget) => new(
        new NavigationEndpoint(point),
        new NavigationEndpoint(point),
        Profile(),
        Policy.Key,
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.AStar,
        budget,
        allowTransitions: false);

    private static GridConfiguration CreateConfiguration(Fixed64 minimumX) => new(
        new Vector3d(minimumX, Fixed64.Zero, Fixed64.Zero),
        new Vector3d(minimumX + (Fixed64)4, (Fixed64)2, (Fixed64)4),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)4),
        storageKind: GridStorageKind.Sparse);

    private static NavigationAgentProfile Profile() => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    private static NavigationWorkBudget CreateBudget(int lookupProbes, int endpointCandidates) => new(
        lookupProbes,
        endpointCandidates,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 0,
        maxConnectionLegs: 0,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 0,
        maxSimplificationRays: 0);
}
