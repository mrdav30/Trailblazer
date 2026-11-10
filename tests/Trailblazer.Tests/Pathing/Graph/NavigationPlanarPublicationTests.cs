using System;
using System.Linq;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationPlanarPublicationTests
{
    private static readonly NavigationCell Open = new(
        TraversalMedia.Solid, TraversalCapability.None, default,
        Fixed64.Zero, Fixed64.One, Fixed64.Two);

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("planar-isolation", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void Publish_DefaultAndOverlayAreBoundToTheSelectedLayer()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        NavigationMap map = Planar(grid).SetDefaultCell(Open).Build();
        Commit(context, map, 1).Should().Be(NavigationOperationStatus.Applied);

        SimulateUntil(context, () => Diagnostics(context).IsMaterialized);
        Diagnostics(context).Cells.Should().HaveCount(6)
            .And.OnlyContain(cell => cell.Index.y == 1 && cell.HasCell && cell.IsPresent);

        var valid = new VoxelIndex(0, 1, 0);
        var invalid = new VoxelIndex(1, 0, 0);
        Overlay(context, 2, NavigationCellOverlayOperation.Suppress(valid),
            NavigationCellOverlayOperation.Set(invalid, Open))
            .Should().Be(NavigationOperationStatus.Rejected);
        Diagnostics(context).Cells.Single(cell => cell.Index.Equals(valid)).HasCell.Should().BeTrue();

        Overlay(context, 3, NavigationCellOverlayOperation.Suppress(valid))
            .Should().Be(NavigationOperationStatus.Applied);
        Commit(context, map, 4).Should().Be(NavigationOperationStatus.Applied);
        Diagnostics(context).Cells.Single(cell => cell.Index.Equals(valid)).HasCell.Should().BeFalse();

        using var lease = context.Pathing.TryAcquireNavigationGraph();
        var visible = new NavigationCellAddress("planar", new VoxelIndex(2, 1, 0));
        TestRequire.NotNull(lease).Graph.TryGetStructuralMediumStateRef(
            visible, TraversalMedium.Solid, out _).Should().BeFalse();
    }

    [Fact]
    public void Candidate_SpatialSemanticLookupExcludesAnAuthoredPlanarCell()
    {
        var processor = new NavigationOperationProcessor(TrailblazerWorldContextSettings.Default.OperationLimits);
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        var index = new VoxelIndex(0, 1, 0);
        NavigationMap map = Planar(grid).AddCell(index, Open).Build();
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear, 1, 0);
        processor.Admit(operation).Should().BeTrue();
        processor.ProcessFrame(0);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        processor.Candidate.TryGetSemanticState(new NavigationCellAddress(map.MapId, index),
            out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_SparsePhysicalChangesKeepTheLayerAndRefreshTheGeneration()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1),
            storageKind: GridStorageKind.Sparse);
        var first = new VoxelIndex(0, 1, 0);
        var later = new VoxelIndex(1, 1, 0);
        var outside = new VoxelIndex(1, 0, 0);
        context.World.TryAddGrid(grid, new[] { first }, out ushort slot).Should().BeTrue();
        Commit(context, Planar(grid).SetDefaultCell(Open).Build(), 1)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => Diagnostics(context).IsMaterialized);
        Diagnostics(context).Cells.Select(cell => cell.Index).Should().Equal(first);

        var live = TestRequire.Grid(context, slot);
        live.TryAddVoxel(outside, out _).Should().BeTrue();
        SimulateUntil(context, () => PublishedSequence(context) == context.World.ChangeSequence);
        Diagnostics(context).Cells.Select(cell => cell.Index).Should().Equal(first);
        live.TryAddVoxel(later, out _).Should().BeTrue();
        SimulateUntil(context, () => Diagnostics(context).Cells.Count == 2);
        Diagnostics(context).Cells.Select(cell => cell.Index).Should().Equal(first, later);
        PublishedSequence(context).Should().Be(context.World.ChangeSequence);
    }

    private static ulong PublishedSequence(TrailblazerWorldContext context)
    {
        using var lease = context.Pathing.TryAcquireNavigationGraph();
        return TestRequire.NotNull(lease).Graph.TryGetCoveredAddressGeneration("planar", out var generation)
            ? generation.GridLastChangeSequence : 0;
    }

    [Fact]
    public void Replace_RejectsChangingTheDomainOfALiveMap()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Commit(context, Planar(grid).AddCell(new VoxelIndex(1, 1, 0), Open).Build(), 1)
            .Should().Be(NavigationOperationStatus.Applied);

        NavigationMap spatial = new NavigationMapBuilder("planar", grid)
            .AddCell(new VoxelIndex(1, 1, 0), Open)
            .Build();
        Commit(context, spatial, 2).Should().Be(NavigationOperationStatus.Rejected);
        Diagnostics(context).BakeVersion.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("planar")]
    public void Publish_SpatialGuidesCannotUseAPlanarBake(string? filter)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        var first = new VoxelIndex(0, 1, 0);
        context.World.TryAddGrid(grid, out ushort slot).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        Commit(context, Planar(grid).SetDefaultCell(Open).Build(), 2)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => Diagnostics(context).IsMaterialized);

        var start = new NavigationEndpoint(new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero), filter);
        var end = new NavigationEndpoint(new Vector3d(Fixed64.One, Fixed64.Half, Fixed64.Zero), filter);
        var budget = new NavigationWorkBudget(256, 32, 32, 64, 64, 16, 16, 16, 64, 64, 0);
        var intent = new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid);
        var query = new PathQuery(start, end, PathTestFactory.DefaultNavigationProfile,
            Policy.Key, intent, PathAlgorithm.AStar, budget, allowTransitions: false);
        context.Guides.RequestGuide(query, out var guide).Should().Be(NavigationGuideStatus.NoMap);
        using var ownedGuide = guide;
        query = new PathQuery(start, end, query.Agent,
            Policy.Key, intent, PathAlgorithm.FlowField, budget, allowTransitions: false);
        context.Guides.RequestFlowField(query, out var flow).Should().Be(NavigationGuideStatus.NoMap);
        using var ownedFlow = flow;

        TestRequire.Grid(context, slot).TryGetVoxel(first, out var voxel).Should().BeTrue();
        context.Pathing.TryResolveCommittedCell(grid.ToGridKey(), TestRequire.NotNull(voxel).WorldIndex,
            out _, out _, out _).Should().Be(NavigationCommittedCellResolveStatus.NoCell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_OverlappingSpatialCoverageCannotSatisfyAPlanarMapFilter(bool tightCapacity)
    {
        using var context = tightCapacity
            ? CreateContextWithOneSpatialSlot() : TrailblazerWorldContext.CreateOwned();
        var planar = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        var spatial = new GridConfiguration(Vector3d.Up, new Vector3d(2, 1, 0));
        context.World.TryAddGrid(planar, out _).Should().BeTrue();
        context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        Commit(context, Planar(planar).SetDefaultCell(Open).Build(), 2)
            .Should().Be(NavigationOperationStatus.Applied);
        Commit(context, new NavigationMapBuilder("spatial", spatial).SetDefaultCell(Open).Build(), 3)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps
            .All(map => map.IsMaterialized));

        Vector3d from = new(Fixed64.Zero, Fixed64.Half, Fixed64.Zero);
        Vector3d to = new(Fixed64.One, Fixed64.Half, Fixed64.Zero);
        PathQuery query = Query(from, to);
        if (!tightCapacity)
            context.Guides.TryGetDirectHeading(query, from, out _).Should().Be(NavigationRayStatus.Success);
        context.Guides.RequestGuide(query, out var guide).Should().Be(NavigationGuideStatus.Success);
        using var ownedGuide = guide;
        context.Guides.TryGetDirectHeading(Query(from, to, startMap: "planar"), from, out _)
            .Should().Be(NavigationRayStatus.Blocked);
        context.Guides.TryGetDirectHeading(Query(from, to, endMap: "planar"), from, out _)
            .Should().Be(NavigationRayStatus.Blocked);

        query = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy,
            query.Traversal, PathAlgorithm.FlowField, query.Budget, allowTransitions: false);
        context.Guides.RequestFlowField(query, out var flow).Should().Be(NavigationGuideStatus.Success);
        using var ownedFlow = flow.GetValueOrDefault();
        if (!tightCapacity)
        {
            Vector3d planarOnly = new(Fixed64.Zero, Fixed64.Half, Fixed64.One);
            ownedFlow.TrySample(planarOnly, new GuideSampleWorkBudget(64, 32, 1, 16, 16, 32, 1), out var sample)
                .Should().Be(NavigationGuideStatus.Success);
            sample.Target.Z.Should().Be(Fixed64.Zero, "a rejoin can return to the spatial corridor");
            sample.Heading.Z.Should().BeLessThan(Fixed64.Zero);
        }
    }

    [Fact]
    public void SpatialFlowRebase_RejectsGraphGrowthBeyondItsGenerationStorage()
    {
        using var context = CreateContextWithOneSpatialSlot();
        var spatial = new GridConfiguration(Vector3d.Up, new Vector3d(2, 1, 0));
        var remote = new GridConfiguration(new Vector3d(8, 1, 0), new Vector3d(10, 1, 0));
        context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        context.World.TryAddGrid(remote, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        Commit(context, new NavigationMapBuilder("spatial", spatial).SetDefaultCell(Open).Build(), 2)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => Diagnostics(context).IsMaterialized);
        var query = Query(new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Half, Fixed64.Zero));
        var flowQuery = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy,
            query.Traversal, PathAlgorithm.FlowField, query.Budget, allowTransitions: false);
        context.Guides.RequestFlowField(flowQuery, out var flow).Should().Be(NavigationGuideStatus.Success);
        using var ownedFlow = flow.GetValueOrDefault();

        Commit(context, new NavigationMapBuilder("remote", remote).SetDefaultCell(Open).Build(), 3)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps
            .All(map => map.IsMaterialized));
        Vector3d offCorridor = new(Fixed64.Zero, Fixed64.Half, Fixed64.One);
        ownedFlow.TrySample(offCorridor, new GuideSampleWorkBudget(64, 32, 1, 16, 16, 32, 1), out _)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_SpatialVolumeDistinguishesRequiredAndAlternativePlanarCoverage(bool required)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration planar;
        GridConfiguration spatial;
        if (required)
        {
            planar = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(2, 1, 1));
            spatial = new GridConfiguration(new Vector3d(-3, 1, -1), new Vector3d(-1, 1, 1));
            context.World.TryAddGrid(planar, out _).Should().BeTrue();
            context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        }
        else
        {
            // Both grids are missing the same non-endpoint prism at world (1, 1, 0).
            planar = new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 1, 0),
                storageKind: GridStorageKind.Sparse);
            spatial = new GridConfiguration(new Vector3d(-1, 1, -1), new Vector3d(1, 1, 1),
                storageKind: GridStorageKind.Sparse);
            VoxelIndex[] present =
            {
                new(0, 0, 0), new(0, 0, 1), new(0, 0, 2),
                new(1, 0, 0), new(1, 0, 1), new(1, 0, 2), new(2, 0, 0), new(2, 0, 2)
            };
            context.World.TryAddGrid(planar, Array.Empty<VoxelIndex>(), out _).Should().BeTrue();
            context.World.TryAddGrid(spatial, present, out _).Should().BeTrue();
        }
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.Fly, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        Commit(context, Planar(planar).SetDefaultCell(air).Build(), 2)
            .Should().Be(NavigationOperationStatus.Applied);
        var spatialBuilder = new NavigationMapBuilder("spatial", spatial).SetDefaultCell(air);
        if (!required)
            spatialBuilder.AddCell(new VoxelIndex(2, 0, 1), air);
        Commit(context, spatialBuilder.Build(), 3)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps
            .All(map => map.IsMaterialized));

        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.FromFraction(3, 4), Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Gas, TraversalCapability.Fly);
        var endpoint = new NavigationEndpoint(
            new Vector3d(required ? -Fixed64.One : Fixed64.Zero, Fixed64.Half, Fixed64.Zero), "spatial");
        var query = new PathQuery(endpoint, endpoint, profile, Policy.Key,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas), PathAlgorithm.AStar,
            new NavigationWorkBudget(256, 32, 32, 64, 64, 16, 16, 16, 64, 64, 0), allowTransitions: false);
        context.Guides.RequestGuide(query, out var guide).Should().Be(NavigationGuideStatus.InvalidStart);
        using var ownedGuide = guide;
    }

    [Theory]
    [InlineData(false, false, NavigationGuideStatus.Success)]
    [InlineData(false, true, NavigationGuideStatus.Success)]
    [InlineData(true, true, NavigationGuideStatus.Success)]
    [InlineData(true, false, NavigationGuideStatus.Stale)]
    public void Publish_CoincidentPlanarPrismCannotShadowSpatialVolume(
        bool addCoincidentGrid, bool publishPlanarMap, NavigationGuideStatus expected)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var spatial = new GridConfiguration(new Vector3d(-1, 1, -1), new Vector3d(1, 1, 1));
        context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.Fly, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var planar = new GridConfiguration(new Vector3d(-2, 0, -1), new Vector3d(1, 1, 1));
        ushort coincidentIndex = default;
        if (addCoincidentGrid)
            context.World.TryAddGrid(planar, out coincidentIndex).Should().BeTrue();
        if (publishPlanarMap)
            Commit(context, Planar(planar).SetDefaultCell(air).Build(), 2).Should().Be(NavigationOperationStatus.Applied);
        if (publishPlanarMap && addCoincidentGrid)
        {
            var distant = new GridConfiguration(new Vector3d(32, 0, -1), new Vector3d(34, 1, 1));
            context.World.TryAddGrid(distant, out _).Should().BeTrue();
            Commit(context, new NavigationMapBuilder("distant", distant)
                .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.Half).SetDefaultCell(air).Build(), 3)
                .Should().Be(NavigationOperationStatus.Applied);
        }
        Commit(context, new NavigationMapBuilder("spatial", spatial).SetDefaultCell(air).Build(), 4)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps
            .All(map => map.IsMaterialized || (!addCoincidentGrid && map.MapId == "planar")));

        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.FromFraction(3, 4), Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Gas, TraversalCapability.Fly);
        var endpoint = new NavigationEndpoint(new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero), "spatial");
        var query = new PathQuery(endpoint, endpoint, profile, Policy.Key,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas), PathAlgorithm.AStar,
            new NavigationWorkBudget(256, 32, 32, 64, 64, 16, 16, 16, 64, 64, 0), allowTransitions: false);
        var status = context.Guides.RequestGuide(query, out var retained);
        using var owned = retained;
        status.Should().Be(expected);
        if (!publishPlanarMap || !addCoincidentGrid)
            return;

        ulong rawSequence = context.World.ChangeSequence;
        RemoveMap("distant", 5);
        context.World.ChangeSequence.Should().Be(rawSequence);
        retained!.Value.Status.Should().Be(NavigationGuideStatus.Success);
        RemoveMap("planar", 6);
        context.World.ChangeSequence.Should().Be(rawSequence);
        retained.Value.Status.Should().Be(NavigationGuideStatus.Stale);
        QueryVolume().Should().Be(NavigationGuideStatus.Stale, "the graph no longer excludes the live raw grid");

        Commit(context, Planar(planar).SetDefaultCell(air).Build(), 7).Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps.All(map => map.IsMaterialized));
        QueryVolume().Should().Be(NavigationGuideStatus.Success);
        CheckExclusionProofLimits();
        context.World.TryRemoveGrid(coincidentIndex).Should().BeTrue();
        context.World.TryAddGrid(planar, out _).Should().BeTrue();
        QueryVolume().Should().Be(NavigationGuideStatus.Stale, "a new raw grid has not inherited the old binding");

        void RemoveMap(string mapId, long sequence)
        {
            var remove = new NavigationMapRemoveOperation(mapId, sequence, context.FrameCount + 1);
            context.Pathing.Admit(remove).Should().BeTrue();
            SimulateUntil(context, () => remove.Receipt.Status != NavigationOperationStatus.Pending);
            remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }

        NavigationGuideStatus QueryVolume()
        {
            var status = context.Guides.RequestGuide(query, out var guide);
            using var ownedGuide = guide;
            return status;
        }

        void CheckExclusionProofLimits()
        {
            using var graphLease = context.Pathing.TryAcquireNavigationGraph();
            var graph = TestRequire.NotNull(graphLease).Graph;
            graph.TryGetNodeRef(new NavigationCellAddress("spatial", new VoxelIndex(1, 0, 1)), out var node)
                .Should().BeTrue();
            var workspace = new NavigationRayWorkspace(2, 2, 0, 64, 0);
            var evaluator = new NavigationVolumeAnchorEvaluator(context.World, graph, profile, Policy, workspace);
            Evaluate(2, 2).Should().Be(NavigationVolumeAnchorStatus.BudgetExceeded);
            Evaluate(3, 0).Should().Be(NavigationVolumeAnchorStatus.CapacityExceeded);
            Evaluate(3, 2).Should().Be(NavigationVolumeAnchorStatus.Success);

            NavigationVolumeAnchorStatus Evaluate(int probes, int pages) => evaluator.Evaluate(
                node, TraversalMedia.Gas,
                new NavigationWorkMeter(new NavigationWorkBudget(probes, 0, 0, 0, 0, 0, 0, 0, 0, 64, 0)),
                new NavigationDependencyWorkspace(pages, 0), out _, out _);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_RejectsSpatialLinksToAPlanarTarget(bool targetArrivesLast)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var planar = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        var spatial = new GridConfiguration(new Vector3d(-2, 1, 0), new Vector3d(-1, 1, 0));
        context.World.TryAddGrid(planar, out _).Should().BeTrue();
        context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        var destination = new NavigationCellAddress("planar", new VoxelIndex(0, 1, 0));
        var transition = new TraversalTransitionDefinition("cross", TraversalTransitionType.Jump,
            new VoxelIndex(1, 0, 0), TraversalMedium.Solid, destination, TraversalMedium.Solid,
            TraversalCapability.Jump);
        NavigationMap spatialMap = new NavigationMapBuilder("spatial", spatial)
            .SetDefaultCell(Open).AddTransition(transition).Build();
        NavigationMap planarMap = Planar(planar).SetDefaultCell(Open).Build();

        Commit(context, targetArrivesLast ? spatialMap : planarMap, 1)
            .Should().Be(NavigationOperationStatus.Applied);
        Commit(context, targetArrivesLast ? planarMap : spatialMap, 2)
            .Should().Be(NavigationOperationStatus.Rejected);
        context.Pathing.GetNavigationGraphDiagnostics().Maps.Should().ContainSingle();
    }

    [Fact]
    public void Publish_AdjacentDomainsHaveNoSpatialSeamOrExplicitCorridor()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var planar = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 1));
        var spatial = new GridConfiguration(new Vector3d(-2, 1, 0), new Vector3d(-1, 1, 0));
        context.World.TryAddGrid(planar, out _).Should().BeTrue();
        context.World.TryAddGrid(spatial, out _).Should().BeTrue();
        Commit(context, Planar(planar).SetDefaultCell(Open).Build(), 1)
            .Should().Be(NavigationOperationStatus.Applied);
        Commit(context, new NavigationMapBuilder("spatial", spatial).SetDefaultCell(Open).Build(), 2)
            .Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(context, () => context.Pathing.GetNavigationGraphDiagnostics().Maps
            .All(map => map.IsMaterialized));

        using var lease = context.Pathing.TryAcquireNavigationGraph();
        TestRequire.NotNull(lease).Graph.AutomaticSeams.PairCount.Should().Be(0);
        var source = new VoxelIndex(1, 0, 0);
        var destination = new NavigationCellAddress("planar", new VoxelIndex(0, 1, 0));
        Vector3d sourceFoot = new(-Fixed64.One, Fixed64.Half, Fixed64.Zero);
        Vector3d destinationFoot = new(Fixed64.Zero, Fixed64.Half, Fixed64.Zero);
        var connection = new NavigationConnection("cross", source, destination,
            sourceFoot, destinationFoot, Fixed64.Zero, Fixed64.One);
        var spatialDelta = new NavigationMapOverlayDelta("spatial",
            connections: new[] { NavigationConnectionOverlayOperation.Upsert(connection) });
        ApplyOverlay(context, 3, spatialDelta).Should().Be(NavigationOperationStatus.Rejected);

        var planarDelta = new NavigationMapOverlayDelta("planar",
            connections: new[] { NavigationConnectionOverlayOperation.Suppress("cross") });
        ApplyOverlay(context, 4, planarDelta).Should().Be(NavigationOperationStatus.Rejected);
        var planarAction = new NavigationMapOverlayDelta("planar", transitions: new[]
        {
            TraversalTransitionOverlayOperation.Upsert(new TraversalTransitionDefinition("cross",
                TraversalTransitionType.Jump, new VoxelIndex(0, 1, 0), TraversalMedium.Solid,
                new NavigationCellAddress("spatial", source), TraversalMedium.Solid))
        });
        ApplyOverlay(context, 5, planarAction).Should().Be(NavigationOperationStatus.Rejected);
        var invalidAnchor = new NavigationMapOverlayDelta("planar", transitions: new[]
        {
            TraversalTransitionOverlayOperation.Upsert(new TraversalTransitionDefinition("override",
                TraversalTransitionType.Jump, destination.Index, TraversalMedium.Solid,
                new NavigationCellAddress("planar", new VoxelIndex(1, 1, 0)), TraversalMedium.Solid,
                sourcePointOverride: new Vector3d(0, 1, 0), hasSourcePointOverride: true))
        });
        ApplyOverlay(context, 6, invalidAnchor).Should().Be(NavigationOperationStatus.Rejected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_RejectsPlanarActionsAcrossDifferentEmbeddings(bool targetArrivesLast)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var source = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 0));
        var target = new GridConfiguration(new Vector3d(4, 0, 0), new Vector3d(5, 1, 0));
        context.World.TryAddGrid(source, out _).Should().BeTrue();
        context.World.TryAddGrid(target, out _).Should().BeTrue();
        NavigationMap owner = Planar(source).SetDefaultCell(Open)
            .AddTransition(new TraversalTransitionDefinition("wrong-plane", TraversalTransitionType.Jump,
                new VoxelIndex(0, 1, 0), TraversalMedium.Solid,
                new NavigationCellAddress("target", new VoxelIndex(0, 0, 0)), TraversalMedium.Solid)).Build();
        NavigationMap destination = new NavigationMapBuilder("target", target).SetDefaultCell(Open)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).Build();
        Commit(context, targetArrivesLast ? owner : destination, 1).Should().Be(NavigationOperationStatus.Applied);
        Commit(context, targetArrivesLast ? destination : owner, 2).Should().Be(NavigationOperationStatus.Rejected);
    }

    private static PathQuery Query(Vector3d start, Vector3d end, string? startMap = null, string? endMap = null) =>
        new(new NavigationEndpoint(start, startMap), new NavigationEndpoint(end, endMap),
            PathTestFactory.DefaultNavigationProfile, Policy.Key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar, new NavigationWorkBudget(256, 32, 32, 64, 64, 16, 16, 16, 64, 64, 0),
            allowTransitions: false);

    private static TrailblazerWorldContext CreateContextWithOneSpatialSlot()
    {
        NavigationQueryLimits q = NavigationQueryLimits.Default;
        var limits = new NavigationQueryLimits(q.MaxBatchItems, q.MaxBatchDescriptorBytes,
            q.MaxConcurrentNavigationQueries, 1, q.AStarWorkspaceEndpointPageCapacity,
            q.AStarWorkspaceComponentCapacity, q.AStarWorkspaceNodeCapacity, q.MaxAStarCacheEntries,
            q.MaxAStarReusablePayloadBytes, q.MaxAStarSinglePayloadBytes, q.MaxAStarActivePayloadBytes,
            q.MaxAStarActivePayloadLeases, 1, q.FlowWorkspaceEndpointPageCapacity,
            q.FlowWorkspaceComponentCapacity, q.FlowWorkspaceNodeCapacity, q.RayWorkspaceCoveredAddressCapacity,
            q.RayWorkspaceTraceIntervalCapacity, q.AStarWorkspaceGuidePointCapacity, q.MaxFlowCacheEntries,
            q.MaxFlowReusablePayloadBytes, q.MaxFlowSinglePayloadBytes, q.MaxFlowActivePayloadBytes,
            q.MaxFlowActivePayloadLeases);
        TrailblazerWorldContextSettings d = TrailblazerWorldContextSettings.Default;
        return TrailblazerWorldContext.CreateOwned(settings: new TrailblazerWorldContextSettings(
            d.OperationLimits, d.MaintenanceBudget, d.GuideSampleBudget, d.MovementGroupPadding,
            d.MaxIngressEntries, d.MaxIngressBytes, d.MaxActiveSnapshots, d.MaxActiveSnapshotBytes,
            d.MaxRetiredSnapshots, d.MaxRetiredSnapshotBytes, d.MaxPersistentGraphPages,
            d.MaxDynamicCellSlotsPerMap, d.MaxDynamicCellSlots, d.NavigationAreaCount, d.MaxAreaPolicies,
            d.MaxAreaRulesPerPolicy, d.MaxAreaRules, d.MaxConcurrentSnapshotLeases, limits));
    }

    private static NavigationMapBuilder Planar(GridConfiguration grid) =>
        new NavigationMapBuilder("planar", grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.Half);

    private static NavigationGraphMapDiagnostic Diagnostics(TrailblazerWorldContext context) =>
        context.Pathing.GetNavigationGraphDiagnostics().Maps.Single();

    private static NavigationOperationStatus Commit(TrailblazerWorldContext context, NavigationMap map, long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, sequence), OverlayReplacementPolicy.PreserveAndRevalidate,
            sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        SimulateUntil(context, () => operation.Receipt.Status != NavigationOperationStatus.Pending);
        return operation.Receipt.Status;
    }

    private static NavigationOperationStatus Overlay(TrailblazerWorldContext context, long sequence,
        params NavigationCellOverlayOperation[] cells) =>
        ApplyOverlay(context, sequence, new NavigationMapOverlayDelta("planar", cells));

    private static NavigationOperationStatus ApplyOverlay(TrailblazerWorldContext context, long sequence,
        NavigationMapOverlayDelta delta)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { delta })),
            sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        SimulateUntil(context, () => operation.Receipt.Status != NavigationOperationStatus.Pending);
        return operation.Receipt.Status;
    }

    private static void SimulateUntil(TrailblazerWorldContext context, Func<bool> condition)
    {
        for (int i = 0; i < 32 && !condition(); i++)
            context.Simulate();
        condition().Should().BeTrue();
    }
}
