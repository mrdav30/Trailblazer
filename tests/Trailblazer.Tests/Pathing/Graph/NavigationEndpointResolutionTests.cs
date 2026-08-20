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
using System.Reflection;
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
            componentCapacity: 3,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);

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
        NavigationWorldGraph graph = CreateAdmissionGraph(selected, closer);
        using NavigationWorldGraphStore store = CreateStore(graph);
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
            componentCapacity: 6,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: 32,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));

        var unfiltered = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            unfiltered,
            graph,
            new NavigationEndpoint(
                requested,
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.One),
            NavigationEndpointRole.Start,
            PointProfile());
        Drain(unfiltered);

        unfiltered.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        unfiltered.Result.Address.MapId.Should().Be("closer");
        graph.TryGetNodeState(unfiltered.Result.Node, out NavigationNodeState unfilteredState)
            .Should().BeTrue();
        unfilteredState.FootAnchor.X.Should().Be(closerPrism.Center.X);

        workspace.Reset();
        meter.Reset(CreateRayBudget(64));
        var filtered = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            filtered,
            graph,
            new NavigationEndpoint(
                requested,
                "selected",
                EndpointResolutionPolicy.NearestNavigable,
                Fixed64.One),
            NavigationEndpointRole.Start,
            PointProfile());
        Drain(filtered);

        filtered.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        filtered.Result.Address.MapId.Should().Be("selected");
        graph.TryGetNodeState(filtered.Result.Node, out NavigationNodeState filteredState)
            .Should().BeTrue();
        filteredState.FootAnchor.X.Should().Be(selectedPrism.Center.X);
    }

    [Theory]
    [InlineData((int)NavigationEndpointRole.Start)]
    [InlineData((int)NavigationEndpointRole.Destination)]
    public void NearestNavigable_ShouldProveTheExactOverlappingCandidate(int roleValue)
    {
        var role = (NavigationEndpointRole)roleValue;
        using var world = new GridWorld();
        NavigationMapInstance overlapping = CreateInstance(
            world,
            "a-overlap",
            CreateAlternateOverlapConfiguration(),
            physicallyPresent: true);
        GridConfiguration candidateConfiguration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance candidate = CreateInstance(
            world,
            "z-candidate",
            candidateConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(overlapping, candidate);
        using NavigationWorldGraphStore store = CreateStore(graph);
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d requested = role == NavigationEndpointRole.Start
            ? new Vector3d(
                prism.Center.X - (Fixed64)2,
                prism.VerticalMin,
                prism.Center.Z)
            : new Vector3d(
                prism.Center.X + (Fixed64)2,
                prism.VerticalMin,
                prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 8,
            componentCapacity: 8,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: 32,
            guidePointCapacity: 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        work.Begin(
            graph,
            new NavigationEndpoint(
                requested,
                "z-candidate",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            role,
            Profile(),
            Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface));

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        work.Result.Address.Should().Be(new NavigationCellAddress("z-candidate", default));
    }

    [Theory]
    [InlineData(32, 1, (int)NavigationEndpointResolutionStatus.BudgetExceeded)]
    [InlineData(1, 64, (int)NavigationEndpointResolutionStatus.CapacityExceeded)]
    public void NearestNavigable_ShouldPropagateCandidateRayLimits(
        int traceCapacity,
        int traceBudget,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        NavigationMapInstance overlapping = CreateInstance(
            world,
            "a-overlap",
            CreateAlternateOverlapConfiguration(),
            physicallyPresent: true);
        GridConfiguration candidateConfiguration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance candidate = CreateInstance(
            world,
            "z-candidate",
            candidateConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(overlapping, candidate);
        using NavigationWorldGraphStore store = CreateStore(graph);
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 8,
            componentCapacity: 8,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: traceCapacity,
            guidePointCapacity: 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(traceBudget));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        work.Begin(
            graph,
            new NavigationEndpoint(
                new Vector3d(
                    prism.Center.X - (Fixed64)2,
                    prism.VerticalMin,
                    prism.Center.Z),
                "z-candidate",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            NavigationEndpointRole.Start,
            Profile(),
            Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface));

        Drain(work);

        work.Status.Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData((int)NavigationEndpointRole.Start, false)]
    [InlineData((int)NavigationEndpointRole.Start, true)]
    [InlineData((int)NavigationEndpointRole.Destination, false)]
    [InlineData((int)NavigationEndpointRole.Destination, true)]
    public void NearestNavigable_ShouldRespectDirectedSeamsForBothEndpointRoles(
        int roleValue,
        bool reverse)
    {
        var role = (NavigationEndpointRole)roleValue;
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        bool candidateIsSource = role == NavigationEndpointRole.Destination ^ reverse;
        string candidateMapId = candidateIsSource ? "source" : "target";
        Vector3d requested = role == NavigationEndpointRole.Start
            ? reverse ? fixture.End : fixture.Start
            : reverse ? fixture.Start : fixture.End;
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 8,
            componentCapacity: 8,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 16,
            rayTraceIntervalCapacity: 8,
            guidePointCapacity: 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            fixture.Context.World,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        work.Begin(
            fixture.Graph,
            new NavigationEndpoint(
                requested,
                candidateMapId,
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            role,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface));

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        work.Result.Address.MapId.Should().Be(candidateMapId);
    }

    [Theory]
    [InlineData(1, (int)NavigationEndpointResolutionStatus.CapacityExceeded)]
    [InlineData(2, (int)NavigationEndpointResolutionStatus.Success)]
    public void NearestNavigable_ShouldMergeBlockedProofBeforeRankingFartherCandidate(
        int componentCapacity,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        GridConfiguration closerConfiguration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance closer = CreateInstance(
            world,
            "a-closer",
            closerConfiguration,
            physicallyPresent: true);
        GridConfiguration fartherConfiguration = CreateAlternateOverlapConfiguration();
        NavigationMapInstance farther = CreateInstance(
            world,
            "z-farther",
            fartherConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(closer, farther);
        using NavigationWorldGraphStore store = CreateStore(graph);
        closerConfiguration.TryNormalize(out NormalizedGridConfiguration closerBinding)
            .Should().BeTrue();
        closerBinding.TryGetCellPrism(default, out GridCellPrism closerPrism)
            .Should().BeTrue();
        Vector3d requested = new(
            closerPrism.Center.X + (Fixed64)0.75,
            closerPrism.VerticalMin,
            closerPrism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 4,
            componentCapacity: componentCapacity,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: 32,
            guidePointCapacity: 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                requested,
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: (Fixed64)2),
            NavigationEndpointRole.Start,
            Profile());

        Drain(work);

        work.Status.Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue);
        if (work.Status == NavigationEndpointResolutionStatus.Success)
            work.Result.Address.MapId.Should().Be("z-farther");
    }

    [Fact]
    public void NearestNavigable_ShouldRevalidateTheAccumulatedNegativeProof()
    {
        using var world = new GridWorld();
        GridConfiguration closerConfiguration = CreateDenseCellConfiguration(
            new Vector3d(Fixed64.Zero, Fixed64.Zero, (Fixed64)2));
        NavigationMapInstance closer = CreateInstance(
            world,
            "a-closer",
            closerConfiguration,
            physicallyPresent: true);
        GridConfiguration fartherConfiguration = CreateDenseCellConfiguration(
            new Vector3d((Fixed64)4.5, Fixed64.Zero, Fixed64.Zero));
        NavigationMapInstance farther = CreateInstance(
            world,
            "z-farther",
            fartherConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(closer, farther);
        using NavigationWorldGraphStore store = CreateStore(graph);
        closerConfiguration.TryNormalize(out NormalizedGridConfiguration closerBinding)
            .Should().BeTrue();
        closerBinding.TryGetCellPrism(default, out GridCellPrism closerPrism)
            .Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(2, 4, 4, 2, 64, 32, 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                new Vector3d(
                    closerPrism.Center.X + (Fixed64)1.5,
                    closerPrism.VerticalMin,
                    closerPrism.Center.Z - (Fixed64)2),
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: (Fixed64)4),
            NavigationEndpointRole.Start,
            Profile());
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("a-closer", default),
                out NavigationSurfaceComponentKey closerComponent,
                out _)
            .Should().BeTrue();

        for (int step = 0;
             step < 64 && workspace.EndpointWorkspace.ComponentCount == 0;
             step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        workspace.EndpointWorkspace.Components[0].Should().Be(closerComponent);
        work.Status.Should().Be(NavigationEndpointResolutionStatus.Pending);

        NavigationGridGenerationIdentity identity = closer.GridIdentity;
        ulong nextHighWater = closer.GridHighWaterSequence + 1;
        var removed = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            closerConfiguration,
            gridVersion: 2,
            changeKind: GridEventKind.SparseVoxelRemoved,
            voxelIndex: default,
            changeStamp: new GridChangeStamp(nextHighWater, nextHighWater),
            hasVoxelState: true,
            isVoxelPresent: false);
        NavigationMapInstance revisedCloser = closer.Apply(
            identity.WorldSpawnToken,
            removed,
            instanceVersion: 2);
        NavigationWorldGraph revised = new(
            graph.GraphVersion + 1,
            new[] { revisedCloser, farther },
            areaCatalog: graph.AreaCatalog,
            surfaceComponents: graph.SurfaceComponents);
        store.TryPublish(revised).Should().Be(NavigationCandidatePublication.Published);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Stale);
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
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z),
                resolution: EndpointResolutionPolicy.Strict),
            NavigationEndpointRole.Start,
            Profile());

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.InvalidEndpoint);
        meter.EndpointCandidates.Should().Be(1,
            "the topology address is charged before graph-authored physical filtering");
    }

    [Fact]
    public void NearestNavigable_ShouldRejectPassableNodeWithoutSurfaceComponentAsStale()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph complete = CreateAdmissionGraph(instance);
        NavigationWorldGraph graph = new(
            complete.GraphVersion,
            new[] { instance },
            areaCatalog: complete.AreaCatalog);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(1, 4, 4, 1, 8, 4, 1);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z),
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.One),
            NavigationEndpointRole.Start,
            PointProfile());

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Stale);
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
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)),
            NavigationEndpointRole.Start,
            Profile());

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
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 2,
            componentCapacity: 4,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateBudget(16, 2));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)),
            NavigationEndpointRole.Start,
            Profile());

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
            componentCapacity: 4,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        work.Begin(lease!, query);

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

    [Theory]
    [InlineData((int)PathAlgorithm.AStar)]
    [InlineData((int)PathAlgorithm.FlowField)]
    public void QueryAdmission_ShouldSelectCanonicalOverlappingEndpoint(int algorithmValue)
    {
        var algorithm = (PathAlgorithm)algorithmValue;
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
            PointProfile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            algorithm,
            CreateRayBudget(64),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 4,
            componentCapacity: 6,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: 32,
            guidePointCapacity: 1);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            algorithm);
        work.Begin(lease!, query);

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

    [Theory]
    [InlineData((int)PathAlgorithm.AStar)]
    [InlineData((int)PathAlgorithm.FlowField)]
    public void QueryAdmission_UnitChunks_ShouldYieldAfterAtomicCandidateRay(
        int algorithmValue)
    {
        var algorithm = (PathAlgorithm)algorithmValue;
        using var world = new GridWorld();
        GridConfiguration configuration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance instance = CreateInstance(
            world,
            "candidate",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d start = new(
            prism.Center.X - (Fixed64)2,
            prism.VerticalMin,
            prism.Center.Z);
        Vector3d end = new(
            prism.Center.X + (Fixed64)2,
            prism.VerticalMin,
            prism.Center.Z);
        var query = new PathQuery(
            new NavigationEndpoint(
                start,
                "candidate",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            new NavigationEndpoint(
                end,
                "candidate",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            algorithm,
            CreateRayBudget(64),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 8, 8, 2, 16, 8, 2);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            algorithm);
        work.Begin(store.TryAcquire()!, query);

        FieldInfo endpointWorkField = typeof(NavigationQueryAdmissionWork).GetField(
            "_endpointWork",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var endpointWork = (NavigationEndpointResolutionWork)endpointWorkField.GetValue(work)!;
        FieldInfo pendingCandidateField = typeof(NavigationEndpointResolutionWork).GetField(
            "_pendingCandidate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int step = 0; step < 64; step++)
        {
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
            var pending = (NavigationResolvedEndpoint)pendingCandidateField.GetValue(
                endpointWork)!;
            if (pending.Node.IsValid)
                break;
        }
        ((NavigationResolvedEndpoint)pendingCandidateField.GetValue(endpointWork)!).Node.IsValid
            .Should().BeTrue();
        typeof(NavigationEndpointResolutionWork).GetField(
                "_cursorComplete",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(endpointWork, true);

        int lookupBefore = work.Meter.LookupProbes;
        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        (work.Meter.LookupProbes - lookupBefore).Should().BeGreaterThan(1);
        work.Status.Should().Be(NavigationQueryAdmissionStatus.Pending);
        work.Result.Start.Should().Be(default(NavigationResolvedEndpoint));

        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        ((NavigationResolvedEndpoint)typeof(NavigationQueryAdmissionWork).GetField(
                "_start",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(work)!).Node.IsValid.Should().BeTrue();
        Drain(work);
        work.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
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
            componentCapacity: 3,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);

        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        work.Begin(lease!, default);

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
            var workspace = new NavigationAStarWorkspace(
                mapCapacity: 1,
                endpointPageCapacity: 2,
                componentCapacity: 4,
                nodeCapacity: 1,
                rayCoveredAddressCapacity: 1,
                rayTraceIntervalCapacity: 1,
                guidePointCapacity: 1);
            using var exact = new NavigationQueryAdmissionWork(
                world,
                exactStore,
                workspace.EndpointWorkspace,
                workspace.RayWorkspace,
                PathAlgorithm.AStar);
            exact.Begin(lease!, query);

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
            var workspace = new NavigationAStarWorkspace(
                mapCapacity: 1,
                endpointPageCapacity: 2,
                componentCapacity: 4,
                nodeCapacity: 1,
                rayCoveredAddressCapacity: 1,
                rayTraceIntervalCapacity: 1,
                guidePointCapacity: 1);
            using var below = new NavigationQueryAdmissionWork(
                world,
                belowStore,
                workspace.EndpointWorkspace,
                workspace.RayWorkspace,
                PathAlgorithm.AStar);
            below.Begin(lease!, query);

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

    private static void BeginEndpoint(
        NavigationEndpointResolutionWork work,
        NavigationWorldGraph graph,
        NavigationEndpoint endpoint,
        NavigationEndpointRole role,
        NavigationAgentProfile profile)
    {
        work.Begin(
            graph,
            endpoint,
            role,
            profile,
            Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface));
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

    private static GridConfiguration CreateAlternateOverlapConfiguration() => new(
        new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero),
        new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)4),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(
            (Fixed64)4,
            (Fixed64)4,
            (Fixed64)4),
        storageKind: GridStorageKind.Sparse);

    private static GridConfiguration CreateCandidateOverlapConfiguration() => new(
        new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero),
        new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)4),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)4),
        storageKind: GridStorageKind.Sparse);

    private static GridConfiguration CreateDenseCellConfiguration(Vector3d center) => new(
        center,
        center,
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2),
        storageKind: GridStorageKind.Dense);

    private static NavigationAgentProfile Profile() => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    private static NavigationAgentProfile PointProfile() => new(
        new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
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

    private static NavigationWorkBudget CreateRayBudget(int traceIntervals) => new(
        maxLookupProbes: 256,
        maxEndpointCandidates: 64,
        maxExpandedNodes: 8,
        maxEvaluatedEdges: 32,
        maxConnectionLegs: 16,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: traceIntervals,
        maxCoveredVoxelIntervals: 128,
        maxSimplificationRays: 0);
}
