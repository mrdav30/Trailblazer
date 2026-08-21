//=======================================================================
// NavigationSurfaceAStarEquivalenceTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
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
public sealed class NavigationSurfaceAStarEquivalenceTests
{
    [Fact]
    public void AStar_ShouldMatchDijkstraAcrossFixedSeedTopologyPermutations()
    {
        int[] seeds = { 0x10203, 0x40506, 0x70809 };
        for (int i = 0; i < seeds.Length; i++)
        {
            AssertGeneratedPermutation(
                seeds[i],
                GridTopologyKind.RectangularPrism,
                HexOrientation.PointyTop);
            AssertGeneratedPermutation(
                seeds[i],
                GridTopologyKind.HexPrism,
                HexOrientation.FlatTop);
            AssertGeneratedPermutation(
                seeds[i],
                GridTopologyKind.HexPrism,
                HexOrientation.PointyTop);
        }
    }

    [Fact]
    public void AStar_ShouldMatchIndependentDijkstraAcrossTopologyAndEdgeMatrix()
    {
        AssertNativeMatrix(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)4),
            default,
            new VoxelIndex(2, 0, 1));
        AssertHexMatrix(HexOrientation.FlatTop);
        AssertHexMatrix(HexOrientation.PointyTop);
        AssertCertifiedExplicitMatrix();
        AssertAutomaticSeamMatrix(stacked: false);
        AssertAutomaticSeamMatrix(stacked: true);
        AssertClearanceMatrix();
    }

    private static void AssertNativeMatrix(
        GridTopologyKind topology,
        GridTopologyMetrics metrics,
        VoxelIndex start,
        VoxelIndex end)
    {
        using var world = new GridWorld();
        var cells = new[]
        {
            start,
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(1, 0, 1),
            end
        };
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)8, (Fixed64)2, (Fixed64)8),
            topologyKind: topology,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                cells,
                "rect");
        PathQuery query = fixture.CreateQuery(start, end, fixture.DefaultProfile);

        AssertEquivalent(world, fixture.Graph, query, expectedHeuristic: true);
    }

    private static void AssertHexMatrix(HexOrientation orientation)
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)12, (Fixed64)2, (Fixed64)12),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex start = FindHexCenter(binding);
        VoxelIndex firstOffset = HexDirectionUtility.GetOffset(HexDirection.QPositive);
        VoxelIndex secondOffset = HexDirectionUtility.GetOffset(HexDirection.RPositive);
        VoxelIndex middle = new(
            start.x + firstOffset.x,
            start.y + firstOffset.y,
            start.z + firstOffset.z);
        VoxelIndex end = new(
            middle.x + secondOffset.x,
            middle.y + secondOffset.y,
            middle.z + secondOffset.z);
        var cells = new[] { start, middle, end };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                cells,
                orientation == HexOrientation.FlatTop ? "flat" : "pointy");
        PathQuery query = fixture.CreateQuery(start, end, fixture.DefaultProfile);

        AssertEquivalent(world, fixture.Graph, query, expectedHeuristic: true);
    }

    private static void AssertCertifiedExplicitMatrix()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(1, 0, 0);
        GridConfiguration configuration = NavigationAStarExitTestHarness.RectangularLine(8);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                configuration,
                new[] { start, end },
                "explicit",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "bridge",
                        start,
                        end,
                        corridorCost: Fixed64.One,
                        radiusClearance: (Fixed64)2)
                });
        PathQuery query = fixture.CreateQuery(start, end, fixture.DefaultProfile);

        AssertEquivalent(world, fixture.Graph, query, expectedHeuristic: true);
    }

    private static void AssertAutomaticSeamMatrix(bool stacked)
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked);
        PathQuery query = fixture.CreateQuery(
            stacked
                ? new NavigationAgentProfile(
                    new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                    maxStepUp: (Fixed64)2,
                    maxDropDown: (Fixed64)2,
                    arrivalRadius: Fixed64.Zero,
                    allowedMedia: TraversalMedia.Solid,
                    capabilities: TraversalCapability.None)
                : fixture.DefaultProfile);

        AssertEquivalent(
            fixture.Context.World,
            fixture.Graph,
            query,
            expectedHeuristic: !stacked);
    }

    private static void AssertClearanceMatrix()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                new[] { start, end },
                "clearance",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "narrow",
                        start,
                        end,
                        corridorCost: Fixed64.One / (Fixed64)4,
                        radiusClearance: Fixed64.Zero,
                        lowerBoundCertified: false),
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "wide",
                        start,
                        end,
                        corridorCost: Fixed64.One / (Fixed64)2,
                        radiusClearance: Fixed64.One / (Fixed64)4,
                        lowerBoundCertified: false)
                });
        var pointProfile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var wideProfile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);

        NavigationAStarExitTestHarness.SearchResult point = AssertEquivalent(
            world,
            fixture.Graph,
            fixture.CreateQuery(start, end, pointProfile),
            expectedHeuristic: false);
        NavigationAStarExitTestHarness.SearchResult wide = AssertEquivalent(
            world,
            fixture.Graph,
            fixture.CreateQuery(start, end, wideProfile),
            expectedHeuristic: false);

        point.Cost.Should().Be(Fixed64.One / (Fixed64)4);
        wide.Cost.Should().Be(Fixed64.One / (Fixed64)2);
    }

    private static NavigationAStarExitTestHarness.SearchResult AssertEquivalent(
        GridWorld world,
        NavigationWorldGraph graph,
        PathQuery query,
        bool expectedHeuristic)
    {
        NavigationAStarExitTestHarness.SearchResult oracle =
            NavigationAStarExitTestHarness.RunDijkstra(world, graph, query);
        NavigationAStarExitTestHarness.SearchResult actual =
            NavigationAStarExitTestHarness.RunAStar(world, graph, query);

        actual.Status.Should().Be(oracle.Status);
        actual.Cost.Should().Be(oracle.Cost);
        actual.StartHeuristic.Should().Be(
            expectedHeuristic ? oracle.DirectFloorDistance : Fixed64.Zero);
        oracle.CertifiedEdgesConsistent.Should().BeTrue();
        return actual;
    }

    private static void AssertGeneratedPermutation(
        int seed,
        GridTopologyKind topology,
        HexOrientation orientation)
    {
        VoxelIndex[] canonical = GenerateConnectedCells(seed, topology, orientation);
        VoxelIndex[] permuted = Permute(canonical, seed ^ 0x5A5A5A5A);
        NavigationAStarExitTestHarness.SearchResult first;
        NavigationAStarExitTestHarness.SearchResult second;
        using (var firstWorld = new GridWorld())
        {
            NavigationAStarExitTestHarness.GraphFixture fixture =
                NavigationAStarExitTestHarness.CreateSingleMap(
                    firstWorld,
                    GeneratedConfiguration(topology, orientation),
                    canonical,
                    "generated");
            first = AssertEquivalent(
                firstWorld,
                fixture.Graph,
                fixture.CreateQuery(
                    canonical[0],
                    canonical[canonical.Length - 1],
                    fixture.DefaultProfile),
                expectedHeuristic: true);
        }
        using (var secondWorld = new GridWorld())
        {
            NavigationAStarExitTestHarness.GraphFixture fixture =
                NavigationAStarExitTestHarness.CreateSingleMap(
                    secondWorld,
                    GeneratedConfiguration(topology, orientation),
                    permuted,
                    "generated");
            second = AssertEquivalent(
                secondWorld,
                fixture.Graph,
                fixture.CreateQuery(
                    canonical[0],
                    canonical[canonical.Length - 1],
                    fixture.DefaultProfile),
                expectedHeuristic: true);
        }

        first.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        second.Status.Should().Be(first.Status);
        second.Cost.Should().Be(first.Cost);
        second.Nodes.Should().Equal(first.Nodes);
    }

    private static GridConfiguration GeneratedConfiguration(
        GridTopologyKind topology,
        HexOrientation orientation) => new(
        Vector3d.Zero,
        new Vector3d((Fixed64)12, (Fixed64)2, (Fixed64)12),
        topologyKind: topology,
        topologyMetrics: topology == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex(Fixed64.One, (Fixed64)2, orientation)
            : GridTopologyMetrics.Rectangular(Fixed64.One, (Fixed64)2, (Fixed64)2),
        storageKind: GridStorageKind.Sparse);

    private static VoxelIndex[] GenerateConnectedCells(
        int seed,
        GridTopologyKind topology,
        HexOrientation orientation)
    {
        GridConfiguration configuration = GeneratedConfiguration(topology, orientation);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var random = new DeterministicRandom((uint)seed);
        VoxelIndex start = topology == GridTopologyKind.HexPrism
            ? FindHexCenter(binding)
            : new VoxelIndex(2, 0, 2);
        var cells = new List<VoxelIndex> { start };
        VoxelIndex cursor = cells[0];
        for (int step = 0; step < 12; step++)
        {
            VoxelIndex offset;
            if (topology == GridTopologyKind.HexPrism)
            {
                HexDirection direction = (HexDirection)random.Next(6);
                offset = HexDirectionUtility.GetOffset(direction);
            }
            else
            {
                offset = random.Next(4) switch
                {
                    0 => new VoxelIndex(-1, 0, 0),
                    1 => new VoxelIndex(1, 0, 0),
                    2 => new VoxelIndex(0, 0, -1),
                    _ => new VoxelIndex(0, 0, 1)
                };
            }
            var next = new VoxelIndex(
                cursor.x + offset.x,
                cursor.y + offset.y,
                cursor.z + offset.z);
            if (!binding.IsValidIndex(next))
                continue;
            cursor = next;
            AddUnique(cells, cursor);
        }
        if (cells.Count == 1)
        {
            VoxelIndex offset = topology == GridTopologyKind.HexPrism
                ? HexDirectionUtility.GetOffset(HexDirection.QPositive)
                : new VoxelIndex(1, 0, 0);
            AddUnique(cells, new VoxelIndex(
                cursor.x + offset.x,
                cursor.y + offset.y,
                cursor.z + offset.z));
        }
        return cells.ToArray();
    }

    private static VoxelIndex[] Permute(VoxelIndex[] source, int seed)
    {
        var result = (VoxelIndex[])source.Clone();
        var random = new DeterministicRandom((uint)seed);
        for (int i = result.Length - 1; i > 0; i--)
        {
            int replacement = random.Next(i + 1);
            (result[i], result[replacement]) = (result[replacement], result[i]);
        }
        return result;
    }

    private static void AddUnique(List<VoxelIndex> cells, VoxelIndex candidate)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Equals(candidate))
                return;
        }
        cells.Add(candidate);
    }

    private struct DeterministicRandom
    {
        private uint _state;

        internal DeterministicRandom(uint seed) => _state = seed == 0 ? 1u : seed;

        internal int Next(int exclusiveMaximum)
        {
            _state = unchecked((_state * 1_664_525u) + 1_013_904_223u);
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }

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
}

internal static class NavigationAStarExitTestHarness
{
    internal static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    internal static readonly NavigationCell ExpensiveCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        (Fixed64)100,
        (Fixed64)4,
        (Fixed64)4);

    internal static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("phase-34e-exit", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    internal readonly struct ExplicitEdgeSpec
    {
        internal ExplicitEdgeSpec(
            string id,
            VoxelIndex source,
            VoxelIndex destination,
            Fixed64 corridorCost,
            Fixed64 radiusClearance,
            Vector3d entryOffset = default,
            Vector3d exitOffset = default,
            bool lowerBoundCertified = true,
            VoxelIndex[]? witnesses = null,
            bool omitPortalCertificates = false,
            Vector3d portalTranslation = default)
        {
            Id = id;
            Source = source;
            Destination = destination;
            CorridorCost = corridorCost;
            RadiusClearance = radiusClearance;
            EntryOffset = entryOffset;
            ExitOffset = exitOffset;
            LowerBoundCertified = lowerBoundCertified;
            Witnesses = witnesses ?? Array.Empty<VoxelIndex>();
            OmitPortalCertificates = omitPortalCertificates;
            PortalTranslation = portalTranslation;
        }

        internal string Id { get; }
        internal VoxelIndex Source { get; }
        internal VoxelIndex Destination { get; }
        internal Fixed64 CorridorCost { get; }
        internal Fixed64 RadiusClearance { get; }
        internal Vector3d EntryOffset { get; }
        internal Vector3d ExitOffset { get; }
        internal bool LowerBoundCertified { get; }
        internal VoxelIndex[] Witnesses { get; }
        internal bool OmitPortalCertificates { get; }
        internal Vector3d PortalTranslation { get; }
    }

    internal readonly struct GraphFixture
    {
        internal GraphFixture(
            NavigationWorldGraph graph,
            NormalizedGridConfiguration binding,
            string mapId)
        {
            Graph = graph;
            Binding = binding;
            MapId = mapId;
        }

        internal NavigationWorldGraph Graph { get; }
        internal NormalizedGridConfiguration Binding { get; }
        internal string MapId { get; }
        internal NavigationAgentProfile DefaultProfile => Profile();

        internal PathQuery CreateQuery(
            VoxelIndex start,
            VoxelIndex end,
            NavigationAgentProfile profile) => Query(
            GetFoot(Binding, start),
            MapId,
            GetFoot(Binding, end),
            MapId,
            profile);
    }

    internal sealed class SeamFixture : IDisposable
    {
        internal SeamFixture(
            TrailblazerWorldContext context,
            NavigationWorldGraph graph,
            Vector3d start,
            Vector3d end)
        {
            Context = context;
            Graph = graph;
            Start = start;
            End = end;
        }

        internal TrailblazerWorldContext Context { get; }
        internal NavigationWorldGraph Graph { get; }
        internal Vector3d Start { get; }
        internal Vector3d End { get; }
        internal NavigationAgentProfile DefaultProfile => Profile();

        internal PathQuery CreateQuery(NavigationAgentProfile profile) =>
            Query(Start, "source", End, "target", profile);

        public void Dispose() => Context.Dispose();
    }

    internal readonly struct SearchResult
    {
        internal SearchResult(
            NavigationSurfaceAStarStatus status,
            Fixed64 cost,
            NavigationCellAddress[] nodes,
            Fixed64 startHeuristic,
            Fixed64 directFloorDistance,
            bool certifiedEdgesConsistent,
            NavigationAStarPayload? payload = null)
        {
            Status = status;
            Cost = cost;
            Nodes = nodes;
            StartHeuristic = startHeuristic;
            DirectFloorDistance = directFloorDistance;
            CertifiedEdgesConsistent = certifiedEdgesConsistent;
            Payload = payload;
        }

        internal NavigationSurfaceAStarStatus Status { get; }
        internal Fixed64 Cost { get; }
        internal NavigationCellAddress[] Nodes { get; }
        internal Fixed64 StartHeuristic { get; }
        internal Fixed64 DirectFloorDistance { get; }
        internal bool CertifiedEdgesConsistent { get; }
        internal NavigationAStarPayload? Payload { get; }
    }

    internal static GridConfiguration RectangularLine(int width) => new(
        Vector3d.Zero,
        new Vector3d((Fixed64)width, (Fixed64)2, (Fixed64)2),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)1, (Fixed64)2, (Fixed64)1),
        storageKind: GridStorageKind.Sparse);

    internal static GraphFixture CreateSingleMap(
        GridWorld world,
        GridConfiguration configuration,
        VoxelIndex[] cells,
        string mapId,
        NavigationCell[]? navigationCells = null)
    {
        world.TryAddGrid(configuration, cells, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < cells.Length; i++)
            builder.AddCell(cells[i], navigationCells?[i] ?? Cell);
        NavigationMapInstance instance = Compose(world, builder.Build());
        return new GraphFixture(CreateGraph(new[] { instance }), binding, mapId);
    }

    internal static GraphFixture CreateExplicitMap(
        GridWorld world,
        GridConfiguration configuration,
        VoxelIndex[] cells,
        string mapId,
        ExplicitEdgeSpec[] edges,
        NavigationCell? cell = null)
    {
        world.TryAddGrid(configuration, cells, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < cells.Length; i++)
            builder.AddCell(cells[i], cell ?? Cell);
        NavigationMapInstance instance = Compose(world, builder.Build());
        NavigationExplicitConnectionIndex index = BuildExplicitIndex(
            binding,
            mapId,
            edges);
        return new GraphFixture(CreateGraph(new[] { instance }, index), binding, mapId);
    }

    internal static SeamFixture CreateAutomaticSeam(bool stacked)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        Vector3d targetCenter = stacked
            ? new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero)
            : new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
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
        NavigationMapCommitOperation source = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("source", sourceBinding)
                    .AddCell(default, Cell)
                    .Build(),
                1),
            OverlayReplacementPolicy.Clear,
            1,
            context.FrameCount + 1);
        NavigationMapCommitOperation target = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("target", targetBinding)
                    .AddCell(default, Cell)
                    .Build(),
                1),
            OverlayReplacementPolicy.Clear,
            2,
            context.FrameCount + 1);
        context.Pathing.Admit(source).Should().BeTrue();
        context.Pathing.Admit(target).Should().BeTrue();
        for (int frame = 0;
            frame < 1_024 && target.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
        source.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        target.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationWorldGraph graph;
        using (NavigationWorldGraphLease lease =
            context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.AutomaticSeams.PairCount.Should().Be(1);
            graph = WithPolicy(lease.Graph);
        }
        return new SeamFixture(
            context,
            graph,
            GetFoot(sourceBinding, default),
            GetFoot(targetBinding, default));
    }

    internal static SearchResult RunAStar(
        GridWorld world,
        NavigationWorldGraph graph,
        PathQuery query)
    {
        using NavigationWorldGraphStore store = CreateStore(graph, 2);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        var workspace = new NavigationAStarWorkspace(
            Math.Max(1, graph.MapCount),
            endpointPageCapacity: 128,
            componentCapacity: 130,
            nodeCapacity: 128,
            rayCoveredAddressCapacity: 128,
            rayTraceIntervalCapacity: 128,
            guidePointCapacity: 128);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        for (int step = 0;
            step < 1_024 && admission.Status == NavigationQueryAdmissionStatus.Pending;
            step++)
        {
            admission.Advance(64, 16);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        NavigationNodeRef startNode = admission.Result.Start.Node;
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);
        for (int step = 0;
            step < 4_096 && search.Status == NavigationSurfaceAStarStatus.Pending;
            step++)
        {
            search.Advance(64, 64, 64, 64);
        }
        search.Status.Should().NotBe(NavigationSurfaceAStarStatus.Pending);
        workspace.NodeTable.TryGetSlot(
                new NavigationMediumStateRef(startNode, TraversalMedium.Solid),
                out int startSlot)
            .Should().BeTrue();
        Fixed64 heuristic = workspace.NodeTable.GetRecord(startSlot).Heuristic;
        NavigationDistanceMath.TryFloor(
                query.Start.Position,
                query.End.Position,
                out Fixed64 directFloor)
            .Should().BeTrue();
        if (search.Status is not NavigationSurfaceAStarStatus.Success
            and not NavigationSurfaceAStarStatus.NoPath)
        {
            return new SearchResult(
                search.Status,
                Fixed64.Zero,
                Array.Empty<NavigationCellAddress>(),
                heuristic,
                directFloor,
                certifiedEdgesConsistent: true);
        }
        NavigationAStarPayload payload = search.Result;
        var pathAddresses = new NavigationCellAddress[workspace.PathNodeCount];
        for (int i = 0; i < pathAddresses.Length; i++)
        {
            graph.TryGetNodeAddress(workspace.PathNodes[i].Node, out pathAddresses[i])
                .Should().BeTrue();
        }
        return new SearchResult(
            payload.Status,
            payload.Cost,
            pathAddresses,
            heuristic,
            directFloor,
            certifiedEdgesConsistent: true,
            payload);
    }

    internal static SearchResult RunDijkstra(
        GridWorld world,
        NavigationWorldGraph graph,
        PathQuery query)
    {
        using NavigationWorldGraphStore store = CreateStore(graph, 2);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        var workspace = new NavigationAStarWorkspace(
            Math.Max(1, graph.MapCount),
            endpointPageCapacity: 128,
            componentCapacity: 130,
            nodeCapacity: 128,
            rayCoveredAddressCapacity: 128,
            rayTraceIntervalCapacity: 128,
            guidePointCapacity: 128);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        for (int step = 0;
            step < 1_024 && admission.Status == NavigationQueryAdmissionStatus.Pending;
            step++)
        {
            admission.Advance(64, 16);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        NavigationResolvedPathQuery resolved = admission.Result;
        try
        {
            var evaluator = new TraversalEvaluator(
                resolved.Graph,
                resolved.Query.Agent,
                resolved.AreaPolicy,
                resolved.StartMedium);
            const int Capacity = 128;
            var nodes = new NavigationNodeRef[Capacity];
            var distances = new Fixed64[Capacity];
            var parents = new int[Capacity];
            var closed = new bool[Capacity];
            Array.Fill(parents, -1);
            nodes[0] = resolved.Start.Node;
            distances[0] = Fixed64.Zero;
            int count = 1;
            int endSlot = -1;
            bool consistent = true;
            NavigationNodeState startState = default;
            NavigationNodeState endState = default;
            resolved.Graph.TryGetNodeState(resolved.Start.Node, out startState).Should().BeTrue();
            resolved.Graph.TryGetNodeState(resolved.End.Node, out endState).Should().BeTrue();

            while (true)
            {
                int current = SelectMinimum(resolved.Graph, nodes, distances, closed, count);
                if (current < 0)
                    break;
                closed[current] = true;
                if (nodes[current] == resolved.End.Node)
                {
                    endSlot = current;
                    break;
                }
                NavigationSurfaceEdgeEnumerator edges =
                    resolved.Graph.EnumerateSurfaceEdges(nodes[current]);
                while (edges.MoveNext())
                {
                    NavigationGraphEdge edge = edges.Current;
                    TraversalEvaluationStatus evaluation = evaluator.EvaluateEdge(
                        nodes[current],
                        edge,
                        out TraversalEdgeEvidence evidence);
                    if (evaluation == TraversalEvaluationStatus.CostOverflow)
                    {
                        return new SearchResult(
                            NavigationSurfaceAStarStatus.CostOverflow,
                            Fixed64.Zero,
                            Array.Empty<NavigationCellAddress>(),
                            Fixed64.Zero,
                            Fixed64.Zero,
                            false);
                    }
                    if (evaluation != TraversalEvaluationStatus.Passable)
                        continue;
                    consistent &= IsCertifiedEdgeConsistent(
                        resolved.Graph,
                        nodes[current],
                        edge.Target,
                        endState.FootAnchor,
                        evidence.Cost);
                    int target = FindNode(nodes, count, edge.Target);
                    if (target < 0)
                    {
                        target = count++;
                        count.Should().BeLessThanOrEqualTo(Capacity);
                        nodes[target] = edge.Target;
                        distances[target] = Fixed64.MaxValue;
                    }
                    Fixed64.TryAdd(
                            distances[current],
                            evidence.Cost,
                            out Fixed64 candidate)
                        .Should().BeTrue();
                    if (closed[target] || candidate >= distances[target])
                        continue;
                    distances[target] = candidate;
                    parents[target] = current;
                }
            }

            NavigationDistanceMath.TryFloor(
                    startState.FootAnchor,
                    endState.FootAnchor,
                    out Fixed64 directFloor)
                .Should().BeTrue();
            if (endSlot < 0)
            {
                return new SearchResult(
                    NavigationSurfaceAStarStatus.NoPath,
                    Fixed64.Zero,
                    Array.Empty<NavigationCellAddress>(),
                    Fixed64.Zero,
                    directFloor,
                    consistent);
            }
            var reversed = new NavigationCellAddress[count];
            int pathCount = 0;
            for (int cursor = endSlot; cursor >= 0; cursor = parents[cursor])
            {
                resolved.Graph.TryGetNodeAddress(nodes[cursor], out reversed[pathCount++])
                    .Should().BeTrue();
            }
            var path = new NavigationCellAddress[pathCount];
            for (int i = 0; i < pathCount; i++)
                path[i] = reversed[pathCount - i - 1];
            return new SearchResult(
                NavigationSurfaceAStarStatus.Success,
                distances[endSlot],
                path,
                Fixed64.Zero,
                directFloor,
                consistent);
        }
        finally
        {
            resolved.Dispose();
        }
    }

    internal static NavigationWorldGraphStore CreateStore(
        NavigationWorldGraph graph,
        int maxConcurrentLeases = 4)
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

    internal static PathQuery Query(
        Vector3d start,
        string startMap,
        Vector3d end,
        string endMap,
        NavigationAgentProfile profile) => new(
        new NavigationEndpoint(start, startMap),
        new NavigationEndpoint(end, endMap),
        profile,
        Policy.Key,
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(
            maxLookupProbes: 8_192,
            maxEndpointCandidates: 32,
            maxExpandedNodes: 128,
            maxEvaluatedEdges: 1_024,
            maxConnectionLegs: 1_024,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: 0,
            maxSimplificationRays: 0),
        allowTransitions: false);

    internal static NavigationAgentProfile Profile() => new(
        new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    internal static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    internal static NavigationWorldGraph WithPolicy(NavigationWorldGraph graph)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        return graph.WithAreaCatalog(catalog, graph.GraphVersion);
    }

    private static NavigationMapInstance Compose(GridWorld world, NavigationMap map)
    {
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        return NavigationMapInstanceTestFactory.Compose(world, state, null, 1);
    }

    private static NavigationWorldGraph CreateGraph(
        NavigationMapInstance[] instances,
        NavigationExplicitConnectionIndex? explicitConnections = null)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                1,
                1,
                1,
                1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        var graph = new NavigationWorldGraph(
            1,
            instances,
            areaCatalog: catalog,
            explicitConnections: explicitConnections);
        return graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
    }

    private static NavigationExplicitConnectionIndex BuildExplicitIndex(
        NormalizedGridConfiguration binding,
        string mapId,
        ExplicitEdgeSpec[] specs)
    {
        var records = new NavigationExplicitConnectionRecord[specs.Length];
        NavigationExplicitConnectionIndex index = NavigationExplicitConnectionIndex.Empty;
        for (int i = 0; i < specs.Length; i++)
        {
            ExplicitEdgeSpec spec = specs[i];
            var witnessAddresses = new NavigationCellAddress[spec.Witnesses.Length];
            for (int witness = 0; witness < witnessAddresses.Length; witness++)
            {
                witnessAddresses[witness] = new NavigationCellAddress(
                    mapId,
                    spec.Witnesses[witness]);
            }
            var definition = new NavigationConnection(
                spec.Id,
                spec.Source,
                new NavigationCellAddress(mapId, spec.Destination),
                GetFoot(binding, spec.Source) + spec.EntryOffset,
                GetFoot(binding, spec.Destination) + spec.ExitOffset,
                spec.RadiusClearance,
                portalHeightClearance: (Fixed64)4,
                witnesses: witnessAddresses);
            var portalBuilder = new NavigationPagedSequence<GridNavigationPortal>.Builder(
                GridNavigationPortal.SizeInBytes);
            VoxelIndex previous = spec.Source;
            bool portalsValid = true;
            for (int portalOrdinal = 0;
                portalOrdinal <= spec.Witnesses.Length;
                portalOrdinal++)
            {
                VoxelIndex next = portalOrdinal < spec.Witnesses.Length
                    ? spec.Witnesses[portalOrdinal]
                    : spec.Destination;
                if (!binding.TryGetCellPrism(previous, out GridCellPrism sourcePrism)
                    || !binding.TryGetCellPrism(next, out GridCellPrism destinationPrism)
                    || !GridCellGeometry.TryCreateNavigationPortal(
                        sourcePrism,
                        destinationPrism,
                        out GridNavigationPortal portal))
                {
                    portalsValid = false;
                    break;
                }
                if (spec.PortalTranslation != Vector3d.Zero
                    && !portal.TryTranslate(
                        spec.PortalTranslation,
                        out portal))
                {
                    portalsValid = false;
                    break;
                }
                portalBuilder.Append(portal);
                previous = next;
            }
            NavigationPagedSequence<GridNavigationPortal> portals = portalsValid
                && !spec.OmitPortalCertificates
                ? portalBuilder.Seal()
                : NavigationPagedSequence<GridNavigationPortal>.Empty;
            records[i] = new NavigationExplicitConnectionRecord(
                new NavigationConnectionOwnerKey(mapId, spec.Id),
                definition,
                isActive: true,
                spec.CorridorCost,
                portals,
                isLowerBoundCertified: spec.LowerBoundCertified);
            index = index.SetOwner(records[i], out _);
        }
        var rows = new SortedDictionary<NavigationCellAddress, List<NavigationConnectionOwnerKey>>();
        for (int i = 0; i < records.Length; i++)
        {
            AddOwner(rows, records[i].Source, records[i].Owner);
            AddOwner(rows, records[i].Destination, records[i].Owner);
        }
        foreach (KeyValuePair<NavigationCellAddress, List<NavigationConnectionOwnerKey>> pair in rows)
        {
            pair.Value.Sort();
            var builder = new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
            for (int i = 0; i < pair.Value.Count; i++)
                builder.Append(pair.Value[i]);
            index = index.SetEndpointRow(
                pair.Key,
                NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
                builder.Seal(),
                out _);
        }
        return index;
    }

    private static void AddOwner(
        SortedDictionary<NavigationCellAddress, List<NavigationConnectionOwnerKey>> rows,
        NavigationCellAddress address,
        NavigationConnectionOwnerKey owner)
    {
        if (!rows.TryGetValue(address, out List<NavigationConnectionOwnerKey>? owners))
        {
            owners = new List<NavigationConnectionOwnerKey>();
            rows.Add(address, owners);
        }
        owners.Add(owner);
    }

    private static int SelectMinimum(
        NavigationWorldGraph graph,
        NavigationNodeRef[] nodes,
        Fixed64[] distances,
        bool[] closed,
        int count)
    {
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (closed[i] || distances[i] == Fixed64.MaxValue)
                continue;
            if (best < 0 || distances[i] < distances[best])
            {
                best = i;
                continue;
            }
            if (distances[i] != distances[best])
                continue;
            graph.TryGetNodeAddress(nodes[i], out NavigationCellAddress current);
            graph.TryGetNodeAddress(nodes[best], out NavigationCellAddress prior);
            if (current.CompareTo(prior) < 0)
                best = i;
        }
        return best;
    }

    private static int FindNode(
        NavigationNodeRef[] nodes,
        int count,
        NavigationNodeRef target)
    {
        for (int i = 0; i < count; i++)
        {
            if (nodes[i] == target)
                return i;
        }
        return -1;
    }

    private static bool IsCertifiedEdgeConsistent(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        NavigationNodeRef target,
        Vector3d goal,
        Fixed64 edgeCost)
    {
        graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress);
        if (!graph.SurfaceComponents.TryGet(
                sourceAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponent component)
            || !component.AllSurfaceEdgesEuclideanCertified)
        {
            return true;
        }
        if (!graph.TryGetNodeState(source, out NavigationNodeState sourceState)
            || !graph.TryGetNodeState(target, out NavigationNodeState targetState)
            || !NavigationDistanceMath.TryFloor(
                sourceState.FootAnchor,
                goal,
                out Fixed64 sourceHeuristic)
            || !NavigationDistanceMath.TryFloor(
                targetState.FootAnchor,
                goal,
                out Fixed64 targetHeuristic)
            || !Fixed64.TryAdd(edgeCost, targetHeuristic, out Fixed64 upperBound))
        {
            return false;
        }
        return sourceHeuristic <= upperBound;
    }
}
