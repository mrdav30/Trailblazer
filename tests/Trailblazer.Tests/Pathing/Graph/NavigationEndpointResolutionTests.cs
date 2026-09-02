//=======================================================================
// NavigationEndpointResolutionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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

[Collection("PathingCollection")]
public sealed class NavigationEndpointResolutionTests
{
    [Theory]
    [InlineData(false, 1, "current", 2, "candidate", true)]
    [InlineData(true, 2, "current", 1, "candidate", true)]
    [InlineData(true, 1, "current", 2, "candidate", false)]
    [InlineData(true, 1, "b", 1, "a", true)]
    [InlineData(true, 1, "b", 1, "b", false)]
    [InlineData(true, 1, "b", 1, "c", false)]
    public void CandidateSelection_ShouldPreferNearestThenCanonicalAddress(
        bool hasCurrent,
        int currentDistance,
        string currentMapId,
        int candidateDistance,
        string candidateMapId,
        bool expected)
    {
        var current = new NavigationResolvedEndpoint(
            default,
            new NavigationCellAddress(currentMapId, default),
            TraversalMedia.Solid,
            TraversalMedium.Solid,
            Vector3d.Left,
            (Fixed64)currentDistance);
        var candidate = new NavigationResolvedEndpoint(
            default,
            new NavigationCellAddress(candidateMapId, default),
            TraversalMedia.Gas,
            TraversalMedium.Gas,
            Vector3d.Right,
            (Fixed64)candidateDistance);

        NavigationEndpointResolutionWork.SelectPreferredCandidate(
                hasCurrent,
                current,
                candidate,
                out NavigationResolvedEndpoint preferred)
            .Should().Be(expected);

        preferred.Address.Should().Be(expected ? candidate.Address : current.Address);
        preferred.ResolutionDistance.Should().Be(expected
            ? candidate.ResolutionDistance
            : current.ResolutionDistance);
        preferred.Media.Should().Be(expected ? TraversalMedia.Gas : TraversalMedia.Solid);
        preferred.ResolutionMedium.Should().Be(expected
            ? TraversalMedium.Gas
            : TraversalMedium.Solid);
        preferred.FootAnchor.Should().Be(expected ? Vector3d.Right : Vector3d.Left);
    }

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
                new NavigationCellAddress("map", new VoxelIndex(0, 0, 0)),
                TraversalMedium.Solid))
            .Should().BeTrue();
        workspace.TryRecordEndpointComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                TraversalMedium.Solid))
            .Should().BeTrue();
        workspace.TryRecordEndpointComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", new VoxelIndex(2, 0, 0)),
                TraversalMedium.Solid))
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
        int terminalLookupProbes = meter.LookupProbes;
        int terminalCandidates = meter.EndpointCandidates;
        filtered.Advance(lookupStepLimit: 64, endpointCandidateStepLimit: 64)
            .Should().Be(NavigationEndpointResolutionStatus.Success);
        meter.LookupProbes.Should().Be(terminalLookupProbes);
        meter.EndpointCandidates.Should().Be(terminalCandidates,
            "a terminal endpoint resolution must not consume deterministic work twice");

        workspace.Reset();
        meter.Reset(CreateRayBudget(64));
        var outsideAnchorLimit = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        BeginEndpoint(
            outsideAnchorLimit,
            graph,
            new NavigationEndpoint(
                requested,
                "selected",
                EndpointResolutionPolicy.NearestNavigable,
                Fixed64.Half),
            NavigationEndpointRole.Start,
            PointProfile());
        Drain(outsideAnchorLimit);

        outsideAnchorLimit.Status.Should().Be(
            NavigationEndpointResolutionStatus.InvalidEndpoint,
            "prism overlap cannot admit a foot anchor beyond the exact resolution radius");
        meter.EndpointCandidates.Should().Be(4,
            "the half-unit bounds deterministically scan four covered voxels before rejecting the sole mapped anchor");
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
            TraversalMedia.Solid);

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
            TraversalMedia.Solid);

        Drain(work);

        work.Status.Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData(1, 4, (int)NavigationEndpointResolutionStatus.CapacityExceeded, 1)]
    [InlineData(2, 4, (int)NavigationEndpointResolutionStatus.Success, 12)]
    [InlineData(4, 1, (int)NavigationEndpointResolutionStatus.CapacityExceeded, 1)]
    [InlineData(4, 2, (int)NavigationEndpointResolutionStatus.Success, 12)]
    public void NearestNavigable_ShouldRejectCandidateWhenItsRayProofDependencyDoesNotFit(
        int pageCapacity,
        int componentCapacity,
        int expectedStatusValue,
        int expectedCandidateCount)
    {
        using var world = new GridWorld();
        NavigationMapInstance overlap = CreateInstance(
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
        NavigationWorldGraph graph = CreateAdmissionGraph(overlap, candidate);
        using NavigationWorldGraphStore store = CreateStore(graph);
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d requested = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: pageCapacity,
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
                "z-candidate",
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: (Fixed64)4),
            NavigationEndpointRole.Start,
            PointProfile());

        Drain(work);

        work.Status.Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue,
            "each independently captured ray dependency must fit before the blocked proof is accepted");
        if (work.Status == NavigationEndpointResolutionStatus.Success)
        {
            work.Result.Address.Should().Be(
                new NavigationCellAddress("z-candidate", default));
        }
        else
        {
            work.Result.Should().Be(default(NavigationResolvedEndpoint));
        }
        meter.EndpointCandidates.Should().Be(expectedCandidateCount,
            "capacity failure terminates on the first proof, while success completes the deterministic nearest scan");
    }

    [Fact]
    public void NearestNavigable_ShouldRejectAFormerlyOverlappingRayPageAfterCandidateAcceptance()
    {
        using var world = new GridWorld();
        NavigationMapInstance overlap = CreateInstance(
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
        NavigationWorldGraph graph = CreateAdmissionGraph(overlap, candidate);
        using NavigationWorldGraphStore store = CreateStore(graph);
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d requested = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 2,
            endpointPageCapacity: 4,
            componentCapacity: 4,
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
                "z-candidate",
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: (Fixed64)4),
            NavigationEndpointRole.Start,
            PointProfile());

        for (int step = 0;
             step < 64 && work.Result.Address.MapId != "z-candidate";
             step++)
        {
            work.Advance(lookupStepLimit: 64, endpointCandidateStepLimit: 1)
                .Should().Be(NavigationEndpointResolutionStatus.Pending);
        }

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Pending,
            "candidate-ray acceptance yields before endpoint finalization");
        work.Result.Address.Should().Be(new NavigationCellAddress("z-candidate", default));
        workspace.EndpointWorkspace.Dependencies.Pages.Should().Contain(
            page => page.MapId == "a-overlap");
        var candidateAddress = new NavigationCellAddress("z-candidate", default);
        var overlapAddress = new NavigationCellAddress("a-overlap", default);
        graph.TryGetSurfaceComponent(
                candidateAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey candidateComponent,
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                overlapAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey overlapComponent,
                out _)
            .Should().BeTrue();
        graph.TryGetComponentDependency(
                candidateComponent,
                out GraphComponentDependency priorCandidateComponent)
            .Should().BeTrue();
        graph.TryGetComponentDependency(
                overlapComponent,
                out GraphComponentDependency priorOverlapComponent)
            .Should().BeTrue();
        var revised = new NavigationWorldGraph(
            graph.GraphVersion + 1,
            new[] { candidate },
            areaCatalog: graph.AreaCatalog,
            surfaceComponents: graph.SurfaceComponents);
        revised.TryGetComponentDependency(
                candidateComponent,
                out GraphComponentDependency currentCandidateComponent)
            .Should().BeTrue();
        currentCandidateComponent.Should().Be(priorCandidateComponent);
        revised.TryGetComponentDependency(
                overlapComponent,
                out GraphComponentDependency currentOverlapComponent)
            .Should().BeTrue();
        currentOverlapComponent.Should().Be(priorOverlapComponent,
            "this snapshot changes only the overlap page dependency under validation");
        revised.TryGetPageDependency(
                new GraphPageDependencyAddress("a-overlap", pageIndex: 0),
                out _)
            .Should().BeFalse();
        store.TryPublish(revised).Should().Be(NavigationCandidatePublication.Published);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Stale);
        work.Result.Should().Be(default(NavigationResolvedEndpoint),
            "a stale page proof must not expose the provisionally accepted candidate");
    }

    [Fact]
    public void SecondEndpoint_ShouldRejectRayPageUnionOneOverRetainedProofCapacity()
    {
        using var world = new GridWorld();
        GridConfiguration firstConfiguration = CreateDenseCellConfiguration(
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero));
        NavigationMapInstance first = CreateInstance(
            world,
            "a-first",
            firstConfiguration,
            physicallyPresent: true);
        NavigationMapInstance overlap = CreateInstance(
            world,
            "m-overlap",
            CreateAlternateOverlapConfiguration(),
            physicallyPresent: true);
        GridConfiguration candidateConfiguration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance candidate = CreateInstance(
            world,
            "z-candidate",
            candidateConfiguration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(first, overlap, candidate);
        using NavigationWorldGraphStore store = CreateStore(graph);
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        firstBinding.TryGetCellPrism(default, out GridCellPrism firstPrism)
            .Should().BeTrue();
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration candidateBinding)
            .Should().BeTrue();
        candidateBinding.TryGetCellPrism(default, out GridCellPrism candidatePrism)
            .Should().BeTrue();
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 2,
            componentCapacity: 4,
            nodeCapacity: 3,
            rayCoveredAddressCapacity: 64,
            rayTraceIntervalCapacity: 32,
            guidePointCapacity: 3);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        Vector3d firstFoot = new(
            firstPrism.Center.X,
            firstPrism.VerticalMin,
            firstPrism.Center.Z);
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(firstFoot, "a-first"),
            NavigationEndpointRole.Start,
            PointProfile());
        Drain(work);
        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        workspace.EndpointWorkspace.PageCount.Should().Be(1);

        Vector3d requested = new(
            candidatePrism.Center.X,
            candidatePrism.VerticalMin,
            candidatePrism.Center.Z);
        BeginEndpoint(
            work,
            graph,
            new NavigationEndpoint(
                requested,
                "z-candidate",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)4),
            NavigationEndpointRole.Destination,
            PointProfile());
        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded,
            "the retained first-endpoint page and candidate page leave no slot for the independently valid overlap-ray proof");
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
        workspace.EndpointWorkspace.PageCount.Should().Be(2,
            "the rejected ray-page union must not partially mutate the retained endpoint proof");
    }

    [Fact]
    public void NearestNavigable_ShouldRejectNearerCandidateWhenItsComponentProofDoesNotFit()
    {
        using var world = new GridWorld();
        GridConfiguration configuration =
            NavigationAStarExitTestHarness.RectangularLine(3);
        VoxelIndex[] candidates = { default, new VoxelIndex(2, 0, 0) };
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            candidates,
            candidates,
            Cell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(new VoxelIndex(1, 0, 0), out GridCellPrism emptyPrism)
            .Should().BeTrue();
        Vector3d requested = new(
            emptyPrism.Center.X + (Fixed64.One / (Fixed64)4),
            emptyPrism.VerticalMin,
            emptyPrism.Center.Z);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 1,
            componentCapacity: 1,
            nodeCapacity: 2,
            rayCoveredAddressCapacity: 8,
            rayTraceIntervalCapacity: 8,
            guidePointCapacity: 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(16));
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
                "map",
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)2),
            NavigationEndpointRole.Start,
            PointProfile());

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded,
            "the nearer cell's independently valid component proof cannot replace an earlier proof atomically when the union is over capacity");
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
        meter.EndpointCandidates.Should().BeGreaterThan(1,
            "the resolver must reject only after considering multiple independently valid candidates");
        workspace.EndpointWorkspace.ComponentCount.Should().Be(1,
            "the rejected component must not partially mutate the accumulated endpoint proof");
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
            TraversalMedia.Solid);

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

    [Theory]
    [InlineData(0, (int)TraversalMedium.Gas)]
    [InlineData(1, (int)TraversalMedium.Gas)]
    [InlineData(2, (int)TraversalMedium.Gas)]
    [InlineData(0, (int)TraversalMedium.Liquid)]
    [InlineData(0, (int)TraversalMedium.Unknown)]
    public void NearestNavigable_WhenFinalSolidProofIsBlocked_ShouldUseVolumeFallbackOrReject(
        int requestedHeight,
        int volumeMediumValue)
    {
        var volumeMedium = (TraversalMedium)volumeMediumValue;
        bool hasVolumeFallback = volumeMedium != TraversalMedium.Unknown;
        TraversalMedia volumeMedia = hasVolumeFallback
            ? NavigationCell.ToMedia(volumeMedium)
            : TraversalMedia.None;
        TraversalMedia requestedMedia = TraversalMedia.Solid | volumeMedia;
        using var world = new GridWorld();
        NavigationCell mixedCell = new(
            requestedMedia,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        GridConfiguration candidateConfiguration = CreateCandidateOverlapConfiguration();
        NavigationMapInstance candidate = CreateInstance(
            world,
            "a-candidate",
            candidateConfiguration,
            physicallyPresent: true,
            mixedCell);
        NavigationMapInstance blocker = CreateInstance(
            world,
            "z-blocker",
            CreateAlternateOverlapConfiguration(),
            physicallyPresent: true,
            mixedCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(candidate, blocker);
        using NavigationWorldGraphStore store = CreateStore(graph);
        candidateConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            requestedMedia,
            TraversalCapability.None);
        var workspace = new NavigationAStarWorkspace(2, 4, 4, 2, 64, 32, 2);
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
                new Vector3d(
                    prism.Center.X + (Fixed64)0.75,
                    requestedHeight switch
                    {
                        0 => prism.VerticalMin,
                        1 => prism.VerticalMin + (Fixed64)0.25,
                        _ => prism.VerticalMin + Fixed64.Half
                    },
                    prism.Center.Z),
                "a-candidate",
                EndpointResolutionPolicy.NearestNavigable,
                Fixed64.One),
            NavigationEndpointRole.Start,
            profile,
            Policy,
            requestedMedia);

        Drain(work);

        work.Status.Should().Be(hasVolumeFallback
            ? NavigationEndpointResolutionStatus.Success
            : NavigationEndpointResolutionStatus.InvalidEndpoint);
        if (hasVolumeFallback)
        {
            work.Result.Address.Should().Be(new NavigationCellAddress("a-candidate", default));
            work.Result.Media.Should().Be(volumeMedia);
            work.Result.ResolutionMedium.Should().Be(volumeMedium);
        }
        else
        {
            work.Result.Should().Be(default(NavigationResolvedEndpoint));
        }
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
                TraversalMedium.Solid,
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
        ulong nextGridLastChangeSequence = closer.GridLastChangeSequence + 1;
        var removed = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            closerConfiguration,
            gridVersion: 2,
            changeKind: GridEventKind.SparseVoxelRemoved,
            voxelIndex: default,
            changeStamp: new GridChangeStamp(
                nextGridLastChangeSequence,
                nextGridLastChangeSequence),
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
    public void Strict_OneBelowEndpointPageCapacity_ShouldFailBeforeAcceptingTheCell()
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
            endpointPageCapacity: 0,
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

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded);
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
        meter.EndpointCandidates.Should().Be(1);
        workspace.EndpointWorkspace.PageCount.Should().Be(0,
            "the rejected page dependency must not partially mutate the proof");
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
    public void VolumeEndpoint_ShouldKeepWorldMutationStaleAfterAtomicBodyTrace()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationAStarWorkspace(1, 2, 4, 1, 64, 0, 1);
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
                new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)),
            NavigationEndpointRole.Start,
            profile,
            Policy,
            TraversalMedia.Gas);

        work.Advance(64, 1).Should().Be(NavigationEndpointResolutionStatus.Pending);
        workspace.RayWorkspace.BodyTraceCells.Count.Should().BeGreaterThan(0);
        world.TryGetGrid(0, out VoxelGrid? grid).Should().BeTrue();
        grid!.TryAddVoxel(new VoxelIndex(1, 0, 0), out _).Should().BeTrue();

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Stale);
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
    }

    [Fact]
    public void EndpointResolution_ShouldKeepOriginalWorldStaleAheadOfBudgetFailure()
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 1, 1, 0, 1);
        var meter = new NavigationWorkMeter(CreateBudget(0, 1));
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
            new NavigationEndpoint(Vector3d.Zero, "map"),
            NavigationEndpointRole.Start,
            Profile());
        world.TryAddGrid(
                CreateDenseCellConfiguration(new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero)),
                System.Array.Empty<VoxelIndex>(),
                out _)
            .Should().BeTrue();

        work.Advance(1, 1).Should().Be(NavigationEndpointResolutionStatus.Stale);
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
    public void PhysicalEnvelopeOutsideAuthoredCells_ShouldAdvanceExactGridLastChangeSequenceOnly()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationGridGenerationIdentity identity = instance.GridIdentity;
        ulong nextGridLastChangeSequence = instance.GridLastChangeSequence + 1;
        var envelope = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.SparseVoxelAdded,
            voxelIndex: new VoxelIndex(1, 0, 0),
            changeStamp: new GridChangeStamp(
                nextGridLastChangeSequence,
                nextGridLastChangeSequence),
            hasVoxelState: true,
            isVoxelPresent: true);

        NavigationMapInstance next = instance.Apply(
            identity.WorldSpawnToken,
            envelope,
            instanceVersion: 2);

        next.Should().NotBeSameAs(instance);
        next.GridLastChangeSequence.Should().Be(nextGridLastChangeSequence);
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
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
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
        work.Begin(lease!, query, TraversalMedium.Solid, TraversalMedia.Solid);

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
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
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
        work.Begin(lease!, query, TraversalMedium.Solid, TraversalMedia.Solid);

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
    [InlineData(false)]
    [InlineData(true)]
    public void NearestVolumeTie_ShouldSelectCanonicalAddressIndependentOfGridOrder(
        bool canonicalOnLeft)
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMapInstance left = CreateInstance(
            world,
            canonicalOnLeft ? "a-map" : "z-map",
            CreateDenseCellConfiguration(
                new Vector3d((Fixed64)(-2), Fixed64.Zero, Fixed64.Zero)),
            physicallyPresent: true,
            gasCell);
        NavigationMapInstance right = CreateInstance(
            world,
            canonicalOnLeft ? "z-map" : "a-map",
            CreateDenseCellConfiguration(
                new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero)),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(left, right);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationAStarWorkspace(2, 2, 4, 2, 8, 0, 2);
        var meter = new NavigationWorkMeter(CreateRayBudget(16));
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
                new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: (Fixed64)3),
            NavigationEndpointRole.Start,
            profile,
            Policy,
            TraversalMedia.Gas);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        work.Result.Address.MapId.Should().Be("a-map");
        work.Result.ResolutionDistance.Should().Be((Fixed64)2);
        work.Result.ResolutionMedium.Should().Be(TraversalMedium.Gas);
        meter.EndpointCandidates.Should().Be(2);
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
        work.Begin(
            lease!,
            default,
            TraversalMedium.Solid,
            TraversalMedia.Solid);

        work.Status.Should().Be(NavigationQueryAdmissionStatus.InvalidProfile);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(
        (int)TraversalMedium.Unknown,
        (int)TraversalMedia.Solid,
        (int)(TraversalMedia.Solid | TraversalMedia.Gas),
        (int)NavigationQueryAdmissionStatus.InvalidStart)]
    [InlineData(
        (int)TraversalMedium.Solid,
        (int)TraversalMedia.None,
        (int)(TraversalMedia.Solid | TraversalMedia.Gas),
        (int)NavigationQueryAdmissionStatus.InvalidEnd)]
    [InlineData(
        (int)TraversalMedium.Solid,
        8,
        (int)(TraversalMedia.Solid | TraversalMedia.Gas),
        (int)NavigationQueryAdmissionStatus.InvalidEnd)]
    [InlineData(
        (int)TraversalMedium.Solid,
        (int)TraversalMedia.Gas,
        (int)TraversalMedia.Solid,
        (int)NavigationQueryAdmissionStatus.InvalidProfile)]
    [InlineData(
        (int)TraversalMedium.Solid,
        (int)TraversalMedia.Gas,
        (int)TraversalMedia.Gas,
        (int)NavigationQueryAdmissionStatus.InvalidProfile)]
    [InlineData(
        (int)TraversalMedium.Solid,
        (int)TraversalMedia.Gas,
        (int)(TraversalMedia.Solid | TraversalMedia.Gas),
        (int)NavigationQueryAdmissionStatus.NoPath)]
    public void ExactMediumAdmission_ShouldRejectMalformedOrUnreachableIntent(
        int startMediumValue,
        int targetMediaValue,
        int allowedMediaValue,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        var workspace = new NavigationAStarWorkspace(1, 1, 1, 1, 1, 1, 1);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        PathQuery query = CreateSurfaceQuery(Vector3d.Zero, CreateBudget(1, 1));
        TraversalIntent publicIntent = allowedMediaValue == (int)TraversalMedia.Gas
            ? new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas)
            : query.Traversal;
        query = new PathQuery(
            query.Start,
            query.End,
            new NavigationAgentProfile(
                query.Agent.Shape,
                query.Agent.MaxStepUp,
                query.Agent.MaxDropDown,
                query.Agent.ArrivalRadius,
                (TraversalMedia)allowedMediaValue,
                query.Agent.Capabilities),
            query.AreaPolicy,
            publicIntent,
            query.Algorithm,
            query.Budget,
            query.AllowTransitions);

        work.Begin(
            store.TryAcquire()!,
            query,
            (TraversalMedium)startMediumValue,
            (TraversalMedia)targetMediaValue);

        work.Status.Should().Be((NavigationQueryAdmissionStatus)expectedStatusValue);
        store.ActiveLeaseCount.Should().Be(0);
        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be((NavigationQueryAdmissionStatus)expectedStatusValue);
        work.Meter.LookupProbes.Should().Be(0);
        work.Meter.EndpointCandidates.Should().Be(0);
    }

    [Fact]
    public void ExactMediumAdmission_ShouldRetainEveryQualifyingMediumAtOneVolumeAddress()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationCell volumeCell = new(
            TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true,
            volumeCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.None);
        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.Zero),
            profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            CreateRayBudget(4),
            allowTransitions: true);
        var workspace = new NavigationAStarWorkspace(1, 2, 4, 1, 64, 64, 1);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);

        work.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Gas,
            TraversalMedia.Gas | TraversalMedia.Liquid);
        Drain(work);

        work.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using NavigationResolvedPathQuery result = work.Result;
        result.StartMedium.Should().Be(TraversalMedium.Gas);
        result.TargetMedia.Should().Be(TraversalMedia.Gas | TraversalMedia.Liquid);
        result.Start.Media.Should().Be(TraversalMedia.Gas);
        result.End.Media.Should().Be(TraversalMedia.Gas | TraversalMedia.Liquid);
        result.Start.FootAnchor.Should().Be(new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero));
        result.End.FootAnchor.Should().Be(result.Start.FootAnchor);
    }

    [Fact]
    public void ExactMediumAdmission_ShouldRejectWorldMutationSinceQueryBegin()
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero), "map"),
            new NavigationEndpoint(new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero), "map"),
            profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            CreateRayBudget(8),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 1, 8, 0, 1);
        using var work = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        work.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Gas,
            TraversalMedia.Gas);
        world.TryAddGrid(
                CreateDenseCellConfiguration(new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero)),
                System.Array.Empty<VoxelIndex>(),
                out _)
            .Should().BeTrue();

        Drain(work);

        work.Status.Should().Be(NavigationQueryAdmissionStatus.Stale);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData((int)TraversalMedium.Gas, (int)EndpointResolutionPolicy.Strict)]
    [InlineData((int)TraversalMedium.Gas, (int)EndpointResolutionPolicy.NearestNavigable)]
    [InlineData((int)TraversalMedium.Liquid, (int)EndpointResolutionPolicy.Strict)]
    [InlineData((int)TraversalMedium.Liquid, (int)EndpointResolutionPolicy.NearestNavigable)]
    public void VolumeEndpoint_ShouldResolveStrictAndNearestZeroLengthPlacement(
        int mediumValue,
        int resolutionValue)
    {
        TraversalMedium medium = (TraversalMedium)mediumValue;
        TraversalMedia media = NavigationCell.ToMedia(medium);
        using var world = new GridWorld();
        NavigationCell volumeCell = new(
            media,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true,
            volumeCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            media,
            TraversalCapability.None);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 1, 1, 0, 1);
        var meter = new NavigationWorkMeter(
            new NavigationWorkBudget(16, 2, 0, 0, 0, 0, 0, 0, 0, 2, 0));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        Vector3d footAnchor = new(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero);
        work.Begin(
            graph,
            new NavigationEndpoint(
                footAnchor,
                "map",
                (EndpointResolutionPolicy)resolutionValue,
                Fixed64.Zero),
            NavigationEndpointRole.Start,
            profile,
            Policy,
            media);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Success);
        work.Result.Media.Should().Be(media);
        work.Result.ResolutionMedium.Should().Be(medium);
        work.Result.FootAnchor.Should().Be(footAnchor);
        work.Result.ResolutionDistance.Should().Be(Fixed64.Zero);
    }

    [Theory]
    [InlineData(1, 1, 1, 1, (int)NavigationVolumeAnchorStatus.Success)]
    [InlineData(0, 1, 1, 1, (int)NavigationVolumeAnchorStatus.BudgetExceeded)]
    [InlineData(1, 1, 0, 1, (int)NavigationVolumeAnchorStatus.BudgetExceeded)]
    [InlineData(1, 0, 1, 1, (int)NavigationVolumeAnchorStatus.CapacityExceeded)]
    [InlineData(1, 1, 1, 0, (int)NavigationVolumeAnchorStatus.CapacityExceeded)]
    public void VolumeAnchor_ShouldSeparateExactGridAndAddressBudgetFromCapacity(
        int lookupBudget,
        int mapCapacity,
        int coveredBudget,
        int coveredCapacity,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(
            mapCapacity,
            pageCapacity: 2,
            componentCapacity: 2,
            coveredAddressCapacity: coveredCapacity,
            traceIntervalCapacity: 0);
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            lookupBudget,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: coveredBudget,
            maxSimplificationRays: 0));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out _);

        status.Should().Be((NavigationVolumeAnchorStatus)expectedStatusValue);
        meter.VolumeUnionChecks.Should().Be(1,
            "the one canonical GridForge body trace is the union-check authority");
        meter.LookupProbes.Should().BeLessThanOrEqualTo(lookupBudget);
        meter.CoveredVoxelIntervals.Should().BeLessThanOrEqualTo(coveredBudget);
    }

    [Fact]
    public void VolumeAnchor_WarmedEvaluationAndReset_ShouldAllocateZeroBytes()
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var budget = new NavigationWorkBudget(1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0);
        var workspace = new NavigationRayWorkspace(1, 2, 2, 1, 0);
        var meter = new NavigationWorkMeter(budget);
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);
        evaluator.Evaluate(
                node,
                TraversalMedia.Gas,
                meter,
                workspace.Dependencies,
                out _,
                out _)
            .Should().Be(NavigationVolumeAnchorStatus.Success);
        workspace.BodyTraceCells.Count.Should().Be(1);
        workspace.Reset();
        workspace.BodyTraceCells.Count.Should().Be(0);
        meter.Reset(budget);
        evaluator.Evaluate(
                node,
                TraversalMedia.Gas,
                meter,
                workspace.Dependencies,
                out _,
                out _)
            .Should().Be(NavigationVolumeAnchorStatus.Success);

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            workspace.Reset();
            meter.Reset(budget);
            _ = evaluator.Evaluate(
                node,
                TraversalMedia.Gas,
                meter,
                workspace.Dependencies,
                out _,
                out _);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void VolumeAnchor_ShouldRejectAnyRequiredCellSemanticFailure(int failureKind)
    {
        using var world = new GridWorld();
        TraversalMedia cellMedia = failureKind == 0
            ? TraversalMedia.Liquid
            : TraversalMedia.Gas;
        TraversalCapability requiredCapability = failureKind == 1
            ? TraversalCapability.Swim
            : TraversalCapability.None;
        Fixed64 radiusClearance = failureKind == 3
            ? Fixed64.Zero
            : Fixed64.One;
        Fixed64 heightClearance = failureKind == 4
            ? Fixed64.Half
            : Fixed64.One;
        NavigationCell cell = new(
            cellMedia,
            requiredCapability,
            default,
            Fixed64.Zero,
            radiusClearance,
            heightClearance);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(Vector3d.Zero),
            physicallyPresent: true,
            cell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                failureKind == 3 ? Fixed64.Half : Fixed64.Zero,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        NavigationAreaPolicy policy = failureKind == 2
            ? new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("deny-volume", 1),
                new[] { new NavigationAreaRule(false, Fixed64.Zero) })
            : Policy;
        var workspace = new NavigationRayWorkspace(1, 2, 2, 1, 0);
        var meter = new NavigationWorkMeter(
            new NavigationWorkBudget(1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out TraversalMedia media);

        status.Should().Be(NavigationVolumeAnchorStatus.Unavailable);
        media.Should().Be(TraversalMedia.None);
        workspace.Dependencies.PageCount.Should().Be(1);
    }

    [Fact]
    public void VolumeAnchor_ShouldMapArithmeticOverflowToCostOverflow()
    {
        using var world = new GridWorld();
        Fixed64 radius = Fixed64.MaxValue;
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.One);
        VoxelIndex[] cells =
        {
            default,
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            NavigationAStarExitTestHarness.RectangularLine(cells.Length),
            cells,
            cells,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", cells[1]),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(radius, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(1, 2, 2, 64, 0);
        var meter = new NavigationWorkMeter(
            new NavigationWorkBudget(1, 0, 0, 0, 0, 0, 0, 0, 0, 64, 0));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out _);

        status.Should().Be(NavigationVolumeAnchorStatus.CostOverflow);
    }

    [Fact]
    public void VolumeAnchor_ShouldMapCenteredAnchorOverflowToCostOverflow()
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateDenseCellConfiguration(
                new Vector3d(
                    Fixed64.Zero,
                    Fixed64.MinValue + (Fixed64)2,
                    Fixed64.Zero)),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(1, 2, 2, 1, 0);
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            new NavigationWorkMeter(CreateRayBudget(1)),
            workspace.Dependencies,
            out _,
            out _);

        status.Should().Be(NavigationVolumeAnchorStatus.CostOverflow);
    }

    [Fact]
    public void VolumeAnchor_ShouldRequireEveryCoveredCellInOneGrid()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            default,
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)3, (Fixed64)2, (Fixed64)4),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.One,
                        (Fixed64)2,
                        (Fixed64)4),
                    storageKind: GridStorageKind.Sparse),
                cells,
                "wide-gas",
                new[] { gasCell, gasCell, gasCell });
        fixture.Graph.TryGetNodeRef(
                new NavigationCellAddress(fixture.MapId, cells[1]),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.One, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(1, 4, 4, 64, 0);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            fixture.Graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out TraversalMedia media);

        status.Should().Be(NavigationVolumeAnchorStatus.Success);
        media.Should().Be(TraversalMedia.Gas);
        workspace.BodyTraceCells.Should().Contain(
            cell => cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage
                && cell.Cell.VoxelIndex == cells[0]);
        workspace.BodyTraceCells.Should().Contain(
            cell => cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage
                && cell.Cell.VoxelIndex == cells[2]);
    }

    [Fact]
    public void VolumeAnchor_ShouldRecordRequiredCoverageAcrossAdjacentGrids()
    {
        using var world = new GridWorld();
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMapInstance source = CreateInstance(
            world,
            "source",
            CreateWideDenseCellConfiguration(Fixed64.Zero),
            physicallyPresent: true,
            gasCell);
        NavigationMapInstance left = CreateInstance(
            world,
            "left",
            CreateWideDenseCellConfiguration((Fixed64)(-2)),
            physicallyPresent: true,
            gasCell);
        NavigationMapInstance right = CreateInstance(
            world,
            "right",
            CreateWideDenseCellConfiguration((Fixed64)2),
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(source, left, right);
        graph.TryGetNodeRef(
                new NavigationCellAddress("source", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape((Fixed64)1.5, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(3, 6, 6, 64, 0);
        var meter = new NavigationWorkMeter(CreateRayBudget(64));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out TraversalMedia media);

        status.Should().Be(NavigationVolumeAnchorStatus.Success);
        media.Should().Be(TraversalMedia.Gas);
        workspace.Dependencies.PageCount.Should().Be(3);
        workspace.BodyTraceCells.Should().Contain(
            cell => cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage
                && cell.ConfigurationKey.Equals(left.Map.GridBinding.Key));
        workspace.BodyTraceCells.Should().Contain(
            cell => cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage
                && cell.ConfigurationKey.Equals(right.Map.GridBinding.Key));
    }

    [Theory]
    [InlineData(true, (int)NavigationVolumeAnchorStatus.Unavailable)]
    [InlineData(false, (int)NavigationVolumeAnchorStatus.Stale)]
    public void VolumeAnchor_ShouldRecordMappedAlternativePagesBeforeSkippingSemantics(
        bool authorAlternative,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        GridConfiguration routeConfiguration = new(
            new Vector3d(-Fixed64.One, Fixed64.Zero, -Fixed64.One),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex[] routeCells =
        {
            new(0, 0, 0), new(0, 0, 1), new(0, 0, 2),
            new(1, 0, 0), new(1, 0, 1), new(1, 0, 2),
            new(2, 0, 0), new(2, 0, 1), new(2, 0, 2)
        };
        VoxelIndex[] routePhysical =
        {
            routeCells[0], routeCells[1], routeCells[2],
            routeCells[3], routeCells[4],
            routeCells[6], routeCells[8]
        };
        NavigationCell gasCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMapInstance route = CreateInstance(
            world,
            "route",
            routeConfiguration,
            routePhysical,
            routeCells,
            gasCell);
        NavigationMapInstance alternative = CreateInstance(
            world,
            "alternative",
            new GridConfiguration(
                new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
                new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
                storageKind: GridStorageKind.Sparse),
            System.Array.Empty<VoxelIndex>(),
            authorAlternative
                ? new[] { default(VoxelIndex) }
                : System.Array.Empty<VoxelIndex>(),
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(route, alternative);
        graph.TryGetNodeRef(
                new NavigationCellAddress("route", new VoxelIndex(1, 0, 1)),
                out NavigationNodeRef node)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape((Fixed64)0.75, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(2, 4, 4, 16, 0);
        var meter = new NavigationWorkMeter(CreateRayBudget(16));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            workspace);

        NavigationVolumeAnchorStatus status = evaluator.Evaluate(
            node,
            TraversalMedia.Gas,
            meter,
            workspace.Dependencies,
            out _,
            out _);

        status.Should().Be((NavigationVolumeAnchorStatus)expectedStatusValue);
        workspace.BodyTraceCells.Should().ContainSingle(
            cell => cell.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency);
        if (authorAlternative)
            workspace.Dependencies.PageCount.Should().Be(2);
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
                TraversalMedium.Solid,
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DependencyStampWork_ShouldRejectDuplicateOrUnknownCanonicalAddresses(
        bool usePages,
        bool useUnknownAddress)
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateConfiguration(Fixed64.Zero),
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("map", default),
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey component,
                out _)
            .Should().BeTrue();
        NavigationSurfaceComponentKey[] components = usePages
            ? Array.Empty<NavigationSurfaceComponentKey>()
            : useUnknownAddress
                ? new[]
                {
                    new NavigationSurfaceComponentKey(
                        new NavigationCellAddress("unknown", default),
                        TraversalMedium.Solid)
                }
                : new[] { component, component };
        GraphPageDependencyAddress[] pages = !usePages
            ? Array.Empty<GraphPageDependencyAddress>()
            : useUnknownAddress
                ? new[] { new GraphPageDependencyAddress("unknown", 0) }
                : new[]
                {
                    new GraphPageDependencyAddress("map", 0),
                    new GraphPageDependencyAddress("map", 0)
                };
        var meter = new NavigationWorkMeter(CreateBudget(4, 0));
        var work = new NavigationDependencyStampWork(
            graph,
            Policy,
            components,
            components.Length,
            pages,
            pages.Length);

        work.Advance(meter, lookupStepLimit: 4).Should().BeTrue();

        work.IsComplete.Should().BeTrue();
        work.IsValid.Should().BeFalse();
        work.Result.Should().BeNull();
    }

    [Fact]
    public void NearestNavigable_ShouldReportOverflowBeforeEnumeratingCandidates()
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateConfiguration(Fixed64.Zero),
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 1,
            componentCapacity: 1,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateRayBudget(1));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        var endpoint = new NavigationEndpoint(
            new Vector3d(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero),
            resolution: EndpointResolutionPolicy.NearestNavigable,
            maxResolutionDistance: Fixed64.One);

        BeginEndpoint(work, graph, endpoint, NavigationEndpointRole.Start, PointProfile());
        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CostOverflow);
        meter.EndpointCandidates.Should().Be(0);
    }

    [Fact]
    public void StrictEndpoint_MaximumRectangularPrismCorner_ShouldReportCandidateDistanceOverflow()
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
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism)
            .Should().BeTrue();
        Vector2d oppositePlanarCorner = prism.GetFootprintVertex(2);
        Fixed64 interiorOffset = Fixed64.FromRaw(1);
        var endpointPosition = new Vector3d(
            oppositePlanarCorner.X > prism.Center.X
                ? oppositePlanarCorner.X - interiorOffset
                : oppositePlanarCorner.X + interiorOffset,
            prism.VerticalMax - interiorOffset,
            oppositePlanarCorner.Y > prism.Center.Z
                ? oppositePlanarCorner.Y - interiorOffset
                : oppositePlanarCorner.Y + interiorOffset);
        Vector3d footAnchor = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        prism.Contains(endpointPosition).Should().BeTrue();
        Vector3d.TryGetDistance(endpointPosition, footAnchor, out Fixed64 distance)
            .Should().BeFalse();
        distance.Should().Be(Fixed64.MaxValue);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 1,
            componentCapacity: 1,
            nodeCapacity: 1,
            rayCoveredAddressCapacity: 1,
            rayTraceIntervalCapacity: 1,
            guidePointCapacity: 1);
        var meter = new NavigationWorkMeter(CreateRayBudget(16));
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
            new NavigationEndpoint(endpointPosition, "map"),
            NavigationEndpointRole.Start,
            PointProfile());
        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CostOverflow);
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
        meter.EndpointCandidates.Should().Be(1,
            "the overflow is discovered while qualifying the exact mapped candidate");
        workspace.EndpointWorkspace.PageCount.Should().Be(1);
    }

    [Fact]
    public void NearestVolumeEndpoint_DistantDiagonalCandidate_ShouldReportDistanceOverflowAfterAnchorCertification()
    {
        using var world = new GridWorld();
        Fixed64 diagonal = Fixed64.FromRaw((long.MaxValue / 4L) * 3L);
        GridConfiguration configuration = CreateDenseCellConfiguration(
            new Vector3d(diagonal, diagonal, diagonal));
        var gasCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true,
            gasCell);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef node)
            .Should().BeTrue();
        graph.TryGetNodeState(node, out NavigationNodeState state)
            .Should().BeTrue();
        state.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d volumeFoot)
            .Should().BeTrue();
        Vector3d.TryGetDistance(Vector3d.Zero, volumeFoot, out Fixed64 distance)
            .Should().BeFalse();
        distance.Should().Be(Fixed64.MaxValue);
        var certificationWorkspace = new NavigationRayWorkspace(1, 4, 4, 64, 0);
        var certificationMeter = new NavigationWorkMeter(CreateRayBudget(64));
        var evaluator = new NavigationVolumeAnchorEvaluator(
            world,
            graph,
            profile,
            Policy,
            certificationWorkspace);
        evaluator.Evaluate(
                node,
                TraversalMedia.Gas,
                certificationMeter,
                certificationWorkspace.Dependencies,
                out Vector3d certifiedFoot,
                out TraversalMedia certifiedMedia)
            .Should().Be(NavigationVolumeAnchorStatus.Success);
        certifiedFoot.Should().Be(volumeFoot);
        certifiedMedia.Should().Be(TraversalMedia.Gas);
        var workspace = new NavigationAStarWorkspace(1, 4, 4, 1, 64, 0, 1);
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
                Vector3d.Zero,
                mapId: "map",
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.MaxValue),
            NavigationEndpointRole.Start,
            profile,
            Policy,
            TraversalMedia.Gas);

        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CostOverflow);
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
        meter.EndpointCandidates.Should().Be(1);
        meter.VolumeUnionChecks.Should().Be(1,
            "the centered volume placement succeeds before its diagonal resolution distance overflows");
        workspace.EndpointWorkspace.PageCount.Should().Be(1);
    }

    [Theory]
    [InlineData(7, (int)NavigationQueryAdmissionStatus.Success, 7, 2, 1)]
    [InlineData(6, (int)NavigationQueryAdmissionStatus.BudgetExceeded, 6, 1, 0)]
    public void QueryAdmission_ShouldEnforceExactSharedLookupBudget(
        int lookupBudget,
        int expectedStatusValue,
        int expectedLookupProbes,
        int expectedEndpointCandidates,
        int expectedActiveLeases)
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
        using NavigationWorldGraphStore store = CreateStore(graph);
        var query = CreateSurfaceQuery(point, CreateBudget(lookupBudget, 2));
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
        work.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);

        work.Advance(lookupStepLimit: 0, endpointCandidateStepLimit: 0)
            .Should().Be(NavigationQueryAdmissionStatus.Pending);
        work.Meter.LookupProbes.Should().Be(0,
            "a zero scheduler chunk must yield without spending the query budget");
        work.Meter.EndpointCandidates.Should().Be(0);

        Drain(work);

        work.Status.Should().Be((NavigationQueryAdmissionStatus)expectedStatusValue);
        work.Meter.LookupProbes.Should().Be(expectedLookupProbes);
        work.Meter.EndpointCandidates.Should().Be(expectedEndpointCandidates);
        store.ActiveLeaseCount.Should().Be(expectedActiveLeases);
        if (work.Status == NavigationQueryAdmissionStatus.Success)
            work.Result.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndpointResolution_ZeroMapWorkspaceShouldRejectFilteredAndUnfilteredGraphs(
        bool filtered)
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateConfiguration(Fixed64.Zero),
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var workspace = new NavigationAStarWorkspace(0, 1, 1, 1, 1, 1, 1);
        var meter = new NavigationWorkMeter(CreateBudget(4, 1));
        var work = new NavigationEndpointResolutionWork(
            world,
            store,
            meter,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            new NavigationRayWork(workspace.RayWorkspace));
        var endpoint = new NavigationEndpoint(
            Vector3d.Zero,
            mapId: filtered ? "map" : null);

        BeginEndpoint(work, graph, endpoint, NavigationEndpointRole.Start, Profile());

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded);
        work.Advance(4, 1).Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded);
        meter.LookupProbes.Should().Be(0);
        meter.EndpointCandidates.Should().Be(0);
    }

    [Fact]
    public void EndpointResolution_MissingMapFilterShouldFinishAsNoMapWithoutScanningWorld()
    {
        using var world = new GridWorld();
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            CreateConfiguration(Fixed64.Zero),
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        var workspace = new NavigationAStarWorkspace(1, 1, 1, 1, 1, 1, 1);
        var meter = new NavigationWorkMeter(CreateBudget(4, 1));
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
            new NavigationEndpoint(Vector3d.Zero, mapId: "missing"),
            NavigationEndpointRole.Start,
            Profile());
        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.NoMap);
        meter.EndpointCandidates.Should().Be(0);
    }

    [Fact]
    public void EndpointResolution_ShouldRejectAResolvedSolidWhenComponentCapacityIsZero()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d foot = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(1, 1, 0, 1, 1, 1, 1);
        var meter = new NavigationWorkMeter(CreateBudget(8, 1));
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
            new NavigationEndpoint(foot),
            NavigationEndpointRole.Start,
            Profile());
        Drain(work);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.CapacityExceeded);
        workspace.EndpointPageCount.Should().Be(1,
            "the candidate page fits before exact component stamping exhausts capacity");
        workspace.EndpointComponentCount.Should().Be(0);
    }

    [Fact]
    public void NearestNavigableSingleCandidate_ShouldPublishAcceptedRayResultOnNextAdvance()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d foot = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 1, 64, 32, 1);
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
                foot,
                mapId: "map",
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.Zero),
            NavigationEndpointRole.Start,
            Profile());

        for (int step = 0; step < 64 && !work.Result.Node.IsValid; step++)
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);

        work.Status.Should().Be(NavigationEndpointResolutionStatus.Pending,
            "the completed candidate ray yields before the cursor publishes its final status");
        work.Result.Address.Should().Be(new NavigationCellAddress("map", default));
        int candidates = meter.EndpointCandidates;

        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationEndpointResolutionStatus.Success);

        work.Result.Address.Should().Be(new NavigationCellAddress("map", default));
        meter.EndpointCandidates.Should().Be(candidates,
            "cursor completion must not rediscover the accepted candidate");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearestNavigableSingleCandidate_ShouldRejectAcceptedRayAfterDependencyRevision(
        bool revisePolicy)
    {
        using var world = new GridWorld();
        GridConfiguration configuration = CreateConfiguration(Fixed64.Zero);
        NavigationMapInstance instance = CreateInstance(
            world,
            "map",
            configuration,
            physicallyPresent: true);
        NavigationWorldGraph graph = CreateAdmissionGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism).Should().BeTrue();
        Vector3d foot = new(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        var workspace = new NavigationAStarWorkspace(1, 2, 2, 1, 64, 32, 1);
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
                foot,
                mapId: "map",
                resolution: EndpointResolutionPolicy.NearestNavigable,
                maxResolutionDistance: Fixed64.Zero),
            NavigationEndpointRole.Start,
            Profile());

        for (int step = 0; step < 64 && !work.Result.Node.IsValid; step++)
            work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        work.Status.Should().Be(NavigationEndpointResolutionStatus.Pending);
        work.Result.Address.Should().Be(new NavigationCellAddress("map", default));

        NavigationWorldGraph revised;
        if (revisePolicy)
        {
            var replacement = new NavigationAreaPolicy(
                new NavigationAreaPolicyKey(Policy.Key.PolicyId, Policy.Key.Revision + 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) });
            NavigationAreaCatalog.Empty.TryPublish(
                    replacement,
                    maxPolicies: 1,
                    requiredRuleCount: 1,
                    maxRulesPerPolicy: 1,
                    maxRules: 1,
                    out NavigationAreaCatalog revisedCatalog)
                .Should().Be(NavigationOperationRejection.None);
            revised = graph.WithAreaCatalog(
                revisedCatalog,
                graph.GraphVersion + 1);
        }
        else
        {
            NavigationGridGenerationIdentity identity = instance.GridIdentity;
            ulong nextGridLastChangeSequence = instance.GridLastChangeSequence + 1;
            var removed = new GridEventInfo(
                identity.WorldSpawnToken,
                identity.GridIndex,
                identity.GridSpawnToken,
                configuration,
                gridVersion: 2,
                changeKind: GridEventKind.SparseVoxelRemoved,
                voxelIndex: default,
                changeStamp: new GridChangeStamp(
                    nextGridLastChangeSequence,
                    nextGridLastChangeSequence),
                hasVoxelState: true,
                isVoxelPresent: false);
            NavigationMapInstance revisedInstance = instance.Apply(
                identity.WorldSpawnToken,
                removed,
                instanceVersion: 2);
            revised = new NavigationWorldGraph(
                graph.GraphVersion + 1,
                new[] { revisedInstance },
                areaCatalog: graph.AreaCatalog,
                surfaceComponents: graph.SurfaceComponents);
        }
        store.TryPublish(revised).Should().Be(NavigationCandidatePublication.Published);

        work.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1)
            .Should().Be(NavigationEndpointResolutionStatus.Stale,
                "an accepted ray must be revalidated before its final endpoint is published");
        work.Result.Should().Be(default(NavigationResolvedEndpoint));
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
            TraversalMedia.Solid);
    }

    private static NavigationMapInstance CreateInstance(
        GridWorld world,
        string mapId,
        GridConfiguration configuration,
        bool physicallyPresent,
        NavigationCell? cell = null)
    {
        VoxelIndex[] physical = physicallyPresent
            ? new[] { default(VoxelIndex) }
            : System.Array.Empty<VoxelIndex>();
        return CreateInstance(
            world,
            mapId,
            configuration,
            physical,
            new[] { default(VoxelIndex) },
            cell ?? Cell);
    }

    private static NavigationMapInstance CreateInstance(
        GridWorld world,
        string mapId,
        GridConfiguration configuration,
        VoxelIndex[] physical,
        VoxelIndex[] authored,
        NavigationCell cell)
    {
        world.TryAddGrid(configuration, physical, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < authored.Length; i++)
            builder.AddCell(authored[i], cell);
        NavigationMap map = builder.Build();
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
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
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

    private static GridConfiguration CreateWideDenseCellConfiguration(Fixed64 centerX) => new(
        new Vector3d(centerX, Fixed64.Zero, Fixed64.Zero),
        new Vector3d(centerX, Fixed64.Zero, Fixed64.Zero),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)6),
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
