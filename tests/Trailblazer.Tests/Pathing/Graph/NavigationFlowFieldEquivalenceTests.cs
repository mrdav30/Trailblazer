//=======================================================================
// NavigationFlowFieldEquivalenceTests.cs
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
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationFlowFieldEquivalenceTests
{
    [Fact]
    public void FlowField_ShouldMatchIndependentDijkstraAcrossRectangularStorageAndSparseHoles()
    {
        AssertRectangularMatrix(GridStorageKind.Dense);
        AssertRectangularMatrix(GridStorageKind.Sparse);
    }

    [Fact]
    public void FlowField_ShouldMatchIndependentDijkstraAcrossFlatAndPointyHexes()
    {
        AssertHexMatrix(HexOrientation.FlatTop);
        AssertHexMatrix(HexOrientation.PointyTop);
    }

    [Fact]
    public void FlowField_ShouldMatchAStarAcrossExplicitClearanceAndAutomaticSeams()
    {
        using (var world = new GridWorld())
        {
            VoxelIndex start = default;
            var destination = new VoxelIndex(4, 0, 0);
            NavigationAStarExitTestHarness.GraphFixture fixture =
                NavigationAStarExitTestHarness.CreateExplicitMap(
                    world,
                    NavigationAStarExitTestHarness.RectangularLine(8),
                    LineCells(4),
                    "explicit-equivalence",
                    new[]
                    {
                        new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                            "narrow",
                            start,
                            destination,
                            corridorCost: (Fixed64)4,
                            radiusClearance: Fixed64.Zero,
                            witnesses: Between(0, 4)),
                        new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                            "wide",
                            start,
                            destination,
                            corridorCost: (Fixed64)6,
                            radiusClearance: (Fixed64)2,
                            witnesses: Between(0, 4))
                    },
                    NavigationAStarExitTestHarness.ExpensiveCell);
            var point = new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None);
            var wide = new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None);

            AssertEquivalent(
                world,
                fixture.Graph,
                fixture.CreateQuery(start, destination, point),
                new NavigationCellAddress(fixture.MapId, start),
                new NavigationCellAddress(fixture.MapId, destination));
            AssertEquivalent(
                world,
                fixture.Graph,
                fixture.CreateQuery(start, destination, wide),
                new NavigationCellAddress(fixture.MapId, start),
                new NavigationCellAddress(fixture.MapId, destination));
        }

        using (NavigationAStarExitTestHarness.SeamFixture seam =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false))
        {
            AssertEquivalent(
                seam.Context.World,
                seam.Graph,
                seam.CreateQuery(seam.DefaultProfile),
                new NavigationCellAddress("source", default),
                new NavigationCellAddress("target", default));
        }
    }

    [Fact]
    public void FlowField_ShouldApplyNodeAndAreaWeightsExactlyOnce()
    {
        using var world = new GridWorld();
        WeightedFixture fixture = CreateWeightedFixture(world, reverseInsertion: false);
        VoxelIndex[] cells = fixture.Cells;
        PathQuery query = fixture.CreateQuery(cells[0], cells[cells.Length - 1]);
        NavigationCellAddress origin = new(fixture.MapId, cells[0]);
        NavigationCellAddress destination = new(
            fixture.MapId,
            cells[cells.Length - 1]);

        NavigationAStarExitTestHarness.SearchResult oracle =
            NavigationAStarExitTestHarness.RunDijkstra(world, fixture.Graph, query);
        NavigationFlowFieldPayload payload = RunFlow(
            fixture.Graph,
            ToFlowField(query),
            origin,
            destination);

        oracle.Cost.Should().Be((Fixed64)18);
        payload.TryGetNode(origin, out NavigationFlowFieldNode start).Should().BeTrue();
        start.IntegrationCost.Should().Be((Fixed64)18);
        payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, cells[1]),
                out NavigationFlowFieldNode first)
            .Should().BeTrue();
        first.IntegrationCost.Should().Be((Fixed64)10);
        payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, cells[2]),
                out NavigationFlowFieldNode second)
            .Should().BeTrue();
        second.IntegrationCost.Should().Be((Fixed64)9);
    }

    [Fact]
    public void FlowField_ShouldIgnoreCellInsertionOrder()
    {
        NavigationFlowFieldPayload first;
        NavigationFlowFieldPayload second;
        using (var firstWorld = new GridWorld())
        {
            WeightedFixture fixture = CreateWeightedFixture(
                firstWorld,
                reverseInsertion: false);
            PathQuery query = fixture.CreateQuery(
                fixture.Cells[0],
                fixture.Cells[fixture.Cells.Length - 1]);
            first = RunFlow(
                fixture.Graph,
                ToFlowField(query),
                new NavigationCellAddress(fixture.MapId, fixture.Cells[0]),
                new NavigationCellAddress(
                    fixture.MapId,
                    fixture.Cells[fixture.Cells.Length - 1]));
        }
        using (var secondWorld = new GridWorld())
        {
            WeightedFixture fixture = CreateWeightedFixture(
                secondWorld,
                reverseInsertion: true);
            PathQuery query = fixture.CreateQuery(
                fixture.Cells[0],
                fixture.Cells[fixture.Cells.Length - 1]);
            second = RunFlow(
                fixture.Graph,
                ToFlowField(query),
                new NavigationCellAddress(fixture.MapId, fixture.Cells[0]),
                new NavigationCellAddress(
                    fixture.MapId,
                    fixture.Cells[fixture.Cells.Length - 1]));
        }

        second.Nodes.Should().Equal(first.Nodes);
        second.AddressLookupOrdinals.Should().Equal(first.AddressLookupOrdinals);
    }

    [Fact]
    public void FlowField_ShouldMatchAStarAcrossRectangularHexRectangularSeams()
    {
        using CrossTopologySeamFixture fixture = CreateCrossTopologySeamFixture();

        AssertEquivalent(
            fixture.Context.World,
            fixture.Graph,
            fixture.Query,
            fixture.Origin,
            fixture.Destination);
    }

    [Fact]
    public void PayloadDependencies_ShouldRejectARepublishedComponentRevision()
    {
        NavigationFlowFieldPayload payload;
        using (var sourceWorld = new GridWorld())
        {
            WeightedFixture source = CreateWeightedFixture(
                sourceWorld,
                reverseInsertion: false,
                graphVersion: 1);
            PathQuery query = source.CreateQuery(
                source.Cells[0],
                source.Cells[source.Cells.Length - 1]);
            payload = RunFlow(
                source.Graph,
                ToFlowField(query),
                new NavigationCellAddress(source.MapId, source.Cells[0]),
                new NavigationCellAddress(
                    source.MapId,
                    source.Cells[source.Cells.Length - 1]));

            source.Graph.IsDependencyCurrent(payload.Dependencies).Should().BeTrue();
        }

        using var replacementWorld = new GridWorld();
        WeightedFixture replacement = CreateWeightedFixture(
            replacementWorld,
            reverseInsertion: false,
            graphVersion: 2);

        replacement.Graph.IsDependencyCurrent(payload.Dependencies).Should().BeFalse();
    }

    [Fact]
    public void CompleteNoPathDependencies_ShouldStaleWhenCrossPageBridgeReappears()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridWorld world = context.World;
        const int CellCount = 66;
        const string MapId = "physical-bridge";
        var authored = new VoxelIndex[CellCount];
        var initiallyPresent = new VoxelIndex[CellCount - 1];
        for (int i = 0; i < authored.Length; i++)
        {
            authored[i] = new VoxelIndex(i, 0, 0);
            if (i < 64)
                initiallyPresent[i] = authored[i];
            else if (i > 64)
                initiallyPresent[i - 1] = authored[i];
        }
        GridConfiguration configuration =
            NavigationAStarExitTestHarness.RectangularLine(CellCount);
        world.TryAddGrid(configuration, initiallyPresent, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var builder = new NavigationMapBuilder(MapId, binding);
        for (int i = 0; i < authored.Length; i++)
            builder.AddCell(authored[i], NavigationAStarExitTestHarness.Cell);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            publicationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        for (int frame = 0;
            frame < 2_048
                && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                    || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
            frame++)
        {
            context.Simulate();
        }
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationWorldGraph absentGraph;
        using (NavigationWorldGraphLease lease =
            context.Pathing.TryAcquireNavigationGraph()!)
        {
            absentGraph = lease.Graph;
        }
        var fixture = new NavigationAStarExitTestHarness.GraphFixture(
            absentGraph,
            binding,
            MapId);
        VoxelIndex destinationIndex = authored[63];
        VoxelIndex bridgeIndex = authored[64];
        VoxelIndex originIndex = authored[65];
        PathQuery query = fixture.CreateQuery(
            originIndex,
            destinationIndex,
            fixture.DefaultProfile);
        NavigationCellAddress origin = new(MapId, originIndex);
        NavigationCellAddress destination = new(MapId, destinationIndex);

        NavigationFlowFieldPayload noPath = RunFlow(
            absentGraph,
            ToFlowField(query),
            origin,
            destination,
            NavigationFlowFieldStatus.NoPath);
        noPath.IsComplete.Should().BeTrue();
        noPath.Dependencies.Pages.Should().HaveCount(2);
        noPath.Dependencies.Pages[0].PageIndex.Should().Be(0);
        noPath.Dependencies.Pages[1].PageIndex.Should().Be(1);
        absentGraph.IsDependencyCurrent(noPath.Dependencies).Should().BeTrue();

        VoxelGrid grid = world.ActiveGrids[0];
        grid.TryAddVoxel(bridgeIndex, out _).Should().BeTrue();
        NavigationWorldGraph? presentGraph = null;
        for (int frame = 0; frame < 256 && presentGraph == null; frame++)
        {
            context.Simulate();
            using NavigationWorldGraphLease lease =
                context.Pathing.TryAcquireNavigationGraph()!;
            if (lease.Graph.TryGetNodeRef(
                    new NavigationCellAddress(MapId, bridgeIndex),
                    out NavigationNodeRef bridgeNode)
                && lease.Graph.TryGetNodeState(
                    bridgeNode,
                    out NavigationNodeState bridgeState)
                && bridgeState.IsPresent)
            {
                presentGraph = lease.Graph;
            }
        }
        presentGraph.Should().NotBeNull();

        absentGraph.TryGetPageDependency(
                new GraphPageDependencyAddress(MapId, 0),
                out GraphPageDependency absentPage)
            .Should().BeTrue();
        presentGraph!.TryGetPageDependency(
                new GraphPageDependencyAddress(MapId, 0),
                out GraphPageDependency presentPage)
            .Should().BeTrue();
        presentPage.Should().Be(absentPage,
            "the physical reappearance occurred only on the next graph page");
        presentGraph.IsDependencyCurrent(noPath.Dependencies).Should().BeFalse(
            "the structurally adjacent missing predecessor page can restore reachability");
        NavigationFlowFieldPayload reachable = RunFlow(
            presentGraph,
            ToFlowField(query),
            origin,
            destination);
        reachable.TryGetNode(origin, out NavigationFlowFieldNode restored)
            .Should().BeTrue();
        restored.IntegrationCost.Should().Be((Fixed64)2);
    }

    [Fact]
    public void EqualAndLowerReverseCandidates_ShouldUseCanonicalEdgeAndDecreaseKey()
    {
        AssertExplicitDiamond(reverseInsertion: false, originToEarlierCost: (Fixed64)8);
        AssertExplicitDiamond(reverseInsertion: true, originToEarlierCost: (Fixed64)8);
        AssertExplicitDiamond(reverseInsertion: false, originToEarlierCost: (Fixed64)12);
    }

    private static void AssertRectangularMatrix(GridStorageKind storage)
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: storage);
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(1, 0, 1),
            new(2, 0, 1)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                cells,
                storage == GridStorageKind.Dense ? "dense-rect" : "sparse-rect");
        PathQuery query = fixture.CreateQuery(
            cells[0],
            cells[cells.Length - 1],
            fixture.DefaultProfile);

        AssertEquivalent(
            world,
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[cells.Length - 1]));
    }

    private static void AssertExplicitDiamond(
        bool reverseInsertion,
        Fixed64 originToEarlierCost)
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var later = new VoxelIndex(4, 0, 0);
        var earlier = new VoxelIndex(8, 0, 0);
        var destination = new VoxelIndex(12, 0, 0);
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges =
        {
            new("a-origin-later", origin, later, (Fixed64)4, (Fixed64)2,
                witnesses: Between(0, 4)),
            new("later-terminal", later, destination, (Fixed64)8, (Fixed64)2,
                witnesses: Between(4, 12)),
            new("z-origin-earlier", origin, earlier, originToEarlierCost, (Fixed64)2,
                witnesses: Between(0, 8)),
            new("earlier-terminal", earlier, destination, (Fixed64)4, (Fixed64)2,
                witnesses: Between(8, 12))
        };
        if (reverseInsertion)
            Array.Reverse(edges);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(16),
                LineCells(12),
                reverseInsertion ? "diamond-reverse" : "diamond-forward",
                edges,
                NavigationAStarExitTestHarness.ExpensiveCell);
        PathQuery query = fixture.CreateQuery(
            origin,
            destination,
            fixture.DefaultProfile);
        NavigationAStarExitTestHarness.SearchResult oracle =
            NavigationAStarExitTestHarness.RunDijkstra(world, fixture.Graph, query);
        NavigationAStarExitTestHarness.SearchResult aStar =
            NavigationAStarExitTestHarness.RunAStar(world, fixture.Graph, query);
        var originAddress = new NavigationCellAddress(fixture.MapId, origin);
        var destinationAddress = new NavigationCellAddress(
            fixture.MapId,
            destination);
        NavigationFlowFieldPayload payload = RunFlow(
            fixture.Graph,
            ToFlowField(query),
            originAddress,
            destinationAddress);

        oracle.Cost.Should().Be((Fixed64)212, "the independent diamond oracle");
        aStar.Cost.Should().Be(oracle.Cost, "A* must match the independent oracle");
        payload.TryGetNode(
                originAddress,
                out NavigationFlowFieldNode flowOrigin)
            .Should().BeTrue();
        payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, later),
                out NavigationFlowFieldNode flowLater)
            .Should().BeTrue();
        payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, earlier),
                out NavigationFlowFieldNode flowEarlier)
            .Should().BeTrue();
        flowLater.IntegrationCost.Should().Be((Fixed64)108);
        flowEarlier.IntegrationCost.Should().Be((Fixed64)104);
        flowOrigin.IntegrationCost.Should().Be(
            oracle.Cost,
            "the immutable flow payload must copy the decreased origin cost");
        flowOrigin.SelectedEdge.Target.Should().Be(
            new NavigationCellAddress(fixture.MapId, later));
        flowOrigin.SelectedEdge.CanonicalOutgoingOrdinal.Should().Be(1);
    }

    private static VoxelIndex[] LineCells(int lastX)
    {
        var result = new VoxelIndex[lastX + 1];
        for (int x = 0; x <= lastX; x++)
            result[x] = new VoxelIndex(x, 0, 0);
        return result;
    }

    private static VoxelIndex[] Between(int sourceX, int destinationX)
    {
        var result = new VoxelIndex[destinationX - sourceX - 1];
        for (int ordinal = 0; ordinal < result.Length; ordinal++)
            result[ordinal] = new VoxelIndex(sourceX + ordinal + 1, 0, 0);
        return result;
    }

    private static void AssertHexMatrix(HexOrientation orientation)
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)12, (Fixed64)2, (Fixed64)12),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(
                (Fixed64)2,
                (Fixed64)2,
                orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex start = FindHexCenter(binding);
        VoxelIndex firstOffset = HexDirectionUtility.GetOffset(HexDirection.QPositive);
        VoxelIndex secondOffset = HexDirectionUtility.GetOffset(HexDirection.RPositive);
        var middle = new VoxelIndex(
            start.x + firstOffset.x,
            start.y + firstOffset.y,
            start.z + firstOffset.z);
        var destination = new VoxelIndex(
            middle.x + secondOffset.x,
            middle.y + secondOffset.y,
            middle.z + secondOffset.z);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { start, middle, destination },
                orientation == HexOrientation.FlatTop ? "flat-flow" : "pointy-flow");
        PathQuery query = fixture.CreateQuery(
            start,
            destination,
            fixture.DefaultProfile);

        AssertEquivalent(
            world,
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, start),
            new NavigationCellAddress(fixture.MapId, destination));
    }

    private static void AssertEquivalent(
        GridWorld world,
        NavigationWorldGraph graph,
        PathQuery aStarQuery,
        NavigationCellAddress origin,
        NavigationCellAddress destination)
    {
        NavigationAStarExitTestHarness.SearchResult oracle =
            NavigationAStarExitTestHarness.RunDijkstra(world, graph, aStarQuery);
        NavigationAStarExitTestHarness.SearchResult aStar =
            NavigationAStarExitTestHarness.RunAStar(world, graph, aStarQuery);
        PathQuery flowQuery = ToFlowField(aStarQuery);
        NavigationFlowFieldPayload payload = RunFlow(
            graph,
            flowQuery,
            origin,
            destination);

        oracle.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        aStar.Status.Should().Be(oracle.Status);
        aStar.Cost.Should().Be(oracle.Cost);
        payload.TryGetNode(origin, out NavigationFlowFieldNode flowOrigin)
            .Should().BeTrue();
        flowOrigin.IntegrationCost.Should().Be(oracle.Cost);
    }

    private static NavigationFlowFieldPayload RunFlow(
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress origin,
        NavigationCellAddress destination,
        NavigationFlowFieldStatus expectedStatus = NavigationFlowFieldStatus.Success)
    {
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        graph.TryGetNodeRef(origin, out NavigationNodeRef originNode).Should().BeTrue();
        graph.TryGetNodeRef(destination, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            query,
            new NavigationResolvedEndpoint(originNode, origin, Fixed64.Zero),
            new NavigationResolvedEndpoint(destinationNode, destination, Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            new NavigationWorkMeter(query.Budget));
        var workspace = new NavigationFlowFieldWorkspace(0, 512, 512, 512, 512, 512);
        using var work = new NavigationFlowFieldWork(resolved, workspace);
        for (int step = 0;
            step < 16_384 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(512, 512, 512, 512);
        }
        work.Status.Should().Be(expectedStatus);
        return work.Result!;
    }

    private static PathQuery ToFlowField(PathQuery query) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        PathAlgorithm.FlowField,
        query.Budget,
        query.AllowTransitions,
        default);

    private static VoxelIndex FindHexCenter(NormalizedGridConfiguration binding)
    {
        HexDirection[] directions =
        {
            HexDirection.QNegative,
            HexDirection.QNegativeRPositive,
            HexDirection.RNegative,
            HexDirection.RPositive,
            HexDirection.QPositiveRNegative,
            HexDirection.QPositive
        };
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int r = 1; r < binding.Length - 1; r++)
            {
                var candidate = new VoxelIndex(q, 0, r);
                bool valid = true;
                for (int i = 0; i < directions.Length; i++)
                {
                    VoxelIndex offset = HexDirectionUtility.GetOffset(directions[i]);
                    valid &= binding.IsValidIndex(new VoxelIndex(
                        candidate.x + offset.x,
                        candidate.y + offset.y,
                        candidate.z + offset.z));
                }
                if (valid)
                    return candidate;
            }
        }
        throw new InvalidOperationException("The test configuration has no interior hex cell.");
    }

    private static WeightedFixture CreateWeightedFixture(
        GridWorld world,
        bool reverseInsertion,
        long graphVersion = 1)
    {
        const string MapId = "weighted";
        GridConfiguration configuration = NavigationAStarExitTestHarness.RectangularLine(4);
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0)
        };
        world.TryAddGrid(configuration, cells, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var defaultCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            new NavigationAreaId(0),
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var weightedCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            new NavigationAreaId(1),
            (Fixed64)2,
            (Fixed64)4,
            (Fixed64)4);
        var destinationCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            new NavigationAreaId(1),
            (Fixed64)3,
            (Fixed64)4,
            (Fixed64)4);
        NavigationCell[] authored =
        {
            defaultCell,
            weightedCell,
            defaultCell,
            destinationCell
        };
        var builder = new NavigationMapBuilder(MapId, binding);
        for (int ordinal = 0; ordinal < cells.Length; ordinal++)
        {
            int index = reverseInsertion ? cells.Length - ordinal - 1 : ordinal;
            builder.AddCell(cells[index], authored[index]);
        }
        var prepared = new PreparedNavigationMap(builder.Build(), bakeVersion: 1);
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
            null,
            graphVersion);
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("weighted-flow", 1),
            new[]
            {
                new NavigationAreaRule(true, Fixed64.Zero),
                new NavigationAreaRule(true, (Fixed64)5)
            });
        NavigationAreaCatalog.Empty.TryPublish(
                policy,
                maxPolicies: 1,
                requiredRuleCount: 2,
                maxRulesPerPolicy: 2,
                maxRules: 2,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        var graph = new NavigationWorldGraph(
            graphVersion,
            new[] { instance },
            areaCatalog: catalog);
        graph = graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
        return new WeightedFixture(graph, binding, MapId, cells, policy.Key);
    }

    private static CrossTopologySeamFixture CreateCrossTopologySeamFixture()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics rectangular = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridTopologyMetrics hex = GridTopologyMetrics.Hex(
            (Fixed64)2,
            (Fixed64)2,
            HexOrientation.FlatTop);
        GridConfiguration lowerConfiguration = CreateSingleCellConfiguration(
            Vector3d.Zero,
            GridTopologyKind.RectangularPrism,
            rectangular);
        GridConfiguration middleConfiguration = CreateSingleCellConfiguration(
            new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero),
            GridTopologyKind.HexPrism,
            hex);
        GridConfiguration upperConfiguration = CreateSingleCellConfiguration(
            new Vector3d(Fixed64.Zero, (Fixed64)4, Fixed64.Zero),
            GridTopologyKind.RectangularPrism,
            rectangular);
        context.World.TryAddGrid(lowerConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(middleConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(upperConfiguration, out _).Should().BeTrue();
        lowerConfiguration.TryNormalize(out NormalizedGridConfiguration lower)
            .Should().BeTrue();
        middleConfiguration.TryNormalize(out NormalizedGridConfiguration middle)
            .Should().BeTrue();
        upperConfiguration.TryNormalize(out NormalizedGridConfiguration upper)
            .Should().BeTrue();

        AdmitSingleCellMap(context, "lower", lower, 1);
        AdmitSingleCellMap(context, "middle", middle, 2);
        NavigationMapCommitOperation last = AdmitSingleCellMap(
            context,
            "upper",
            upper,
            3);
        for (int frame = 0;
            frame < 2_048 && last.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationWorldGraph graph;
        using (NavigationWorldGraphLease lease =
            context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.AutomaticSeams.PairCount.Should().Be(2);
            graph = NavigationAStarExitTestHarness.WithPolicy(lease.Graph);
        }

        var origin = new NavigationCellAddress("lower", default);
        var destination = new NavigationCellAddress("upper", default);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            maxStepUp: (Fixed64)2,
            maxDropDown: (Fixed64)2,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None);
        PathQuery query = NavigationAStarExitTestHarness.Query(
            NavigationAStarExitTestHarness.GetFoot(lower, default),
            origin.MapId,
            NavigationAStarExitTestHarness.GetFoot(upper, default),
            destination.MapId,
            profile);
        return new CrossTopologySeamFixture(
            context,
            graph,
            query,
            origin,
            destination);
    }

    private static GridConfiguration CreateSingleCellConfiguration(
        Vector3d center,
        GridTopologyKind kind,
        GridTopologyMetrics metrics) => new(
        center,
        center,
        topologyKind: kind,
        topologyMetrics: metrics,
        storageKind: GridStorageKind.Dense);

    private static NavigationMapCommitOperation AdmitSingleCellMap(
        TrailblazerWorldContext context,
        string mapId,
        NormalizedGridConfiguration binding,
        long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder(mapId, binding)
                    .AddCell(default, NavigationAStarExitTestHarness.Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private sealed class CrossTopologySeamFixture : IDisposable
    {
        internal CrossTopologySeamFixture(
            TrailblazerWorldContext context,
            NavigationWorldGraph graph,
            PathQuery query,
            NavigationCellAddress origin,
            NavigationCellAddress destination)
        {
            Context = context;
            Graph = graph;
            Query = query;
            Origin = origin;
            Destination = destination;
        }

        internal TrailblazerWorldContext Context { get; }
        internal NavigationWorldGraph Graph { get; }
        internal PathQuery Query { get; }
        internal NavigationCellAddress Origin { get; }
        internal NavigationCellAddress Destination { get; }

        public void Dispose() => Context.Dispose();
    }

    private readonly struct WeightedFixture
    {
        internal WeightedFixture(
            NavigationWorldGraph graph,
            NormalizedGridConfiguration binding,
            string mapId,
            VoxelIndex[] cells,
            NavigationAreaPolicyKey policy)
        {
            Graph = graph;
            Binding = binding;
            MapId = mapId;
            Cells = cells;
            Policy = policy;
        }

        internal NavigationWorldGraph Graph { get; }
        internal NormalizedGridConfiguration Binding { get; }
        internal string MapId { get; }
        internal VoxelIndex[] Cells { get; }
        internal NavigationAreaPolicyKey Policy { get; }

        internal PathQuery CreateQuery(VoxelIndex start, VoxelIndex end) => new(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(Binding, start),
                MapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(Binding, end),
                MapId),
            NavigationAStarExitTestHarness.Profile(),
            Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                8_192,
                32,
                128,
                1_024,
                1_024,
                0,
                0,
                0,
                0,
                0,
                0),
            allowTransitions: false);
    }
}
