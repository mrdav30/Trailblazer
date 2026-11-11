using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void CrossMapRoute_UsesExactCentersAndForwardEnterCost(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var westGrid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        var eastGrid = new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(3, 0, 0));
        context.World.TryAddGrid(westGrid, out _).Should().BeTrue();
        context.World.TryAddGrid(eastGrid, out _).Should().BeTrue();
        var westStart = new NavigationCellAddress("west", new VoxelIndex(0, 1, 0));
        var westSeam = new NavigationCellAddress("west", new VoxelIndex(1, 1, 0));
        var eastSeam = new NavigationCellAddress("east", default);
        var eastEnd = new NavigationCellAddress("east", new VoxelIndex(1, 0, 0));
        var toll = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Half, Fixed64.One, Fixed64.Two);
        Publish(context, TopDown("west", westGrid).AddCell(westStart.Index, Open)
            .AddCell(westSeam.Index, Open).Build(), 2);
        Publish(context, new NavigationMapBuilder("east", eastGrid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half)
            .AddCell(eastSeam.Index, toll).AddCell(eastEnd.Index, Open).Build(), 3);

        var route = new NavigationCellAddress[4];
        FindRoute(context, westStart, eastEnd, Fixed64.Half, algorithm, route, out int count, out var cost)
            .Should().Be(NavigationGuideStatus.Success);
        count.Should().Be(4);
        cost.Should().Be((Fixed64)3 + Fixed64.Half);
        route.Should().Equal(westStart, westSeam, eastSeam, eastEnd);

        FindRoute(context, eastEnd, westSeam, Fixed64.Half, algorithm, route, out count, out cost)
            .Should().Be(NavigationGuideStatus.Success);
        count.Should().Be(3);
        cost.Should().Be(Fixed64.Two + Fixed64.Half);
        route[..count].Should().Equal(eastEnd, eastSeam, westSeam);

        FindRoute(context, eastSeam, westSeam, Fixed64.Half, algorithm, route, out count, out cost)
            .Should().Be(NavigationGuideStatus.Success);
        count.Should().Be(2);
        cost.Should().Be(Fixed64.One);
        route[..count].Should().Equal(eastSeam, westSeam);

        RequestTopDownGuide(context, westStart, eastEnd, algorithm, out var result)
            .Should().Be(NavigationGuideStatus.Success);
        using var guide = result;
        guide.TotalCost.Should().Be((Fixed64)3 + Fixed64.Half);
        guide.StepCount.Should().Be(4);
        var expected = new[] { westStart, westSeam, eastSeam, eastEnd };
        for (int i = 0; i < expected.Length; i++)
        {
            guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(expected[i]);
            center.Should().Be(new Vector2d(i, 0));
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, HexOrientation.PointyTop)]
    [InlineData(PathAlgorithm.AStar, HexOrientation.FlatTop)]
    [InlineData(PathAlgorithm.FlowField, HexOrientation.PointyTop)]
    [InlineData(PathAlgorithm.FlowField, HexOrientation.FlatTop)]
    public void CrossMapRoute_DiscoversHexNeighborsAtExactAuthoredCenters(
        PathAlgorithm algorithm, HexOrientation orientation)
    {
        using var context = CreateContext();
        var metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        var reference = new GridConfiguration(Vector3d.Zero, new Vector3d(8, 0, 8),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        reference.TryNormalize(out var referenceBinding).Should().BeTrue();
        referenceBinding.TryGetCellPrism(new VoxelIndex(1, 0, 0), out var neighbor).Should().BeTrue();
        var first = new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        var second = new GridConfiguration(neighbor.Center, neighbor.Center,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder("first", first)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 2);
        Publish(context, new NavigationMapBuilder("second", second)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 3);
        var start = new NavigationCellAddress("first", default);
        var end = new NavigationCellAddress("second", default);
        var route = new NavigationCellAddress[2];
        Check(start, end);
        Check(end, start);

        void Check(NavigationCellAddress from, NavigationCellAddress to)
        {
            ValidateLeg(context, from, to, Fixed64.Quarter).Should().Be(NavigationGuideStatus.Success);
            FindRoute(context, from, to, Fixed64.Quarter, algorithm, route, out int count, out var cost)
                .Should().Be(NavigationGuideStatus.Success);
            count.Should().Be(2);
            route.Should().Equal(from, to);
            var offset = neighbor.Center.ToVector2d();
            offset.TryGetMagnitudeCeiling(out var expected).Should().BeTrue();
            cost.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("radius")]
    [InlineData("orientation")]
    [InlineData("sparse")]
    public void CrossMapRoute_RejectsNonNativeOrUnauthoredHexCandidates(string kind)
    {
        using var context = CreateContext();
        var metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        var otherMetrics = kind switch
        {
            "radius" => GridTopologyMetrics.Hex(Fixed64.Two, Fixed64.One, HexOrientation.PointyTop),
            "orientation" => GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.FlatTop),
            _ => metrics
        };
        var first = new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        var reference = new GridConfiguration(Vector3d.Zero, new Vector3d(8, 0, 8),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        reference.TryNormalize(out var binding).Should().BeTrue();
        binding.TryGetCellPrism(new VoxelIndex(1, 0, 0), out var neighbor).Should().BeTrue();
        var lastCenter = kind == "sparse" ? neighbor.Center + new Vector3d(8, 0, 8) : neighbor.Center;
        var second = new GridConfiguration(neighbor.Center, lastCenter,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: otherMetrics);
        var start = new NavigationCellAddress("first", default);
        var end = new NavigationCellAddress("second", kind == "sparse" ? new VoxelIndex(2, 0, 2) : default);
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder(start.MapId, first)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).AddCell(start.Index, Open).Build(), 2);
        Publish(context, new NavigationMapBuilder(end.MapId, second)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).AddCell(end.Index, Open).Build(), 3);

        var output = new[] { end, start };
        FindRoute(context, start, end, Fixed64.Quarter, PathAlgorithm.AStar, output, out int count, out _)
            .Should().Be(NavigationGuideStatus.NoPath);
        count.Should().Be(0);
        output.Should().Equal(end, start);
    }

    [Theory]
    [InlineData("coincident", NavigationGuideStatus.NoPath)]
    [InlineData("diagonal", NavigationGuideStatus.NoPath)]
    [InlineData("different width", NavigationGuideStatus.NoPath)]
    [InlineData("different length", NavigationGuideStatus.NoPath)]
    [InlineData("mixed topology", NavigationGuideStatus.NoPath)]
    [InlineData("separated", NavigationGuideStatus.NoPath)]
    [InlineData("north gap", NavigationGuideStatus.NoPath)]
    [InlineData("different embedding", NavigationGuideStatus.NoPath)]
    [InlineData("closed", NavigationGuideStatus.Stale)]
    [InlineData("spatial", NavigationGuideStatus.NoMap)]
    [InlineData("dormant", NavigationGuideStatus.NoMap)]
    public void CrossMapRoute_RejectsIncompatibleDomainsAndNonNeighbors(string kind, NavigationGuideStatus expected)
    {
        using var context = CreateContext();
        var first = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        int x = kind is "coincident" or "north gap" or "different width"
            ? 0 : kind is "mixed topology" or "separated" ? 2 : 1;
        int z = kind is "diagonal" or "different width" ? 1 : kind == "north gap" ? 2 : 0;
        var metrics = kind switch
        {
            "different width" => GridTopologyMetrics.Rectangular(Fixed64.Two, Fixed64.One, Fixed64.One),
            "different length" => GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.Two),
            "mixed topology" => GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One),
            _ => default
        };
        var second = new GridConfiguration(new Vector3d(x, 0, z), new Vector3d(x, 0, z), topologyMetrics: metrics,
            topologyKind: kind == "mixed topology" ? GridTopologyKind.HexPrism : GridTopologyKind.RectangularPrism);
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        if (kind != "dormant")
            context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, TopDown("plane", first).SetDefaultCell(Open).Build(), 2);
        var neighbor = new NavigationMapBuilder("neighbor", second).SetDefaultCell(Open);
        if (kind != "spatial")
            neighbor.SetPlanarLayer(NavigationMode2D.TopDown, 0,
                kind == "different embedding" ? Fixed64.Zero : Fixed64.Half);
        Publish(context, neighbor.Build(), 3, materialize: kind != "dormant");
        if (kind == "closed")
        {
            var store = context.Pathing.NavigationGraphStore;
            var current = store.Current;
            store.TryPublish(current.WithClosedStructuralComponents(NavigationSurfaceComponentKeySet.Empty,
                closeAllStructuralComponents: true, current.GraphVersion + 1)).Should().Be(NavigationCandidatePublication.Published);
        }

        var start = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var end = new NavigationCellAddress("neighbor", default);
        var route = new NavigationCellAddress[2];
        Check(start, end);
        Check(end, start);

        void Check(NavigationCellAddress from, NavigationCellAddress to)
        {
            FindRoute(context, from, to, Fixed64.Quarter, PathAlgorithm.AStar, route, out int count, out _)
                .Should().Be(expected);
            count.Should().Be(0);
        }
    }

    [Fact]
    public void CrossMapDiscovery_ChargesLookupsAndDiscardsChangedWorldEvidence()
    {
        using var context = CreateContext();
        var west = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        var east = new GridConfiguration(Vector3d.Right, Vector3d.Right);
        context.World.TryAddGrid(west, out ushort westSlot).Should().BeTrue();
        context.World.TryAddGrid(east, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder("east", east)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 2);
        var graph = context.Pathing.NavigationGraphStore.Current;
        graph.TryGetCoveredAddressGeneration("east", out var generation).Should().BeTrue();
        var layer = new GridPlanarLayer(generation, 0);
        NavigationPlanarWork discovery = context.Pathing.ImmediateRayWorkspace.RayWork.Workspace.Planar;

        Begin();
        var zero = new NavigationWorkMeter(RouteBudget(lookups: 0));
        discovery.BindNeighborCandidateLayer(context.World, layer, zero)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);

        Begin();
        var one = new NavigationWorkMeter(RouteBudget(lookups: 1));
        discovery.BindNeighborCandidateLayer(context.World, layer, one)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Pending);
        discovery.ReadNeighborCandidate(context.World, one, out _, out _)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        context.World.IncrementGridVersion(westSlot, significant: true);
        discovery.ReadNeighborCandidate(context.World, one, out _, out _)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Stale);

        Begin();
        context.World.IncrementGridVersion(westSlot, significant: true);
        discovery.BindNeighborCandidateLayer(context.World, layer, one)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Stale);

        void Begin() => discovery.BeginNeighborCandidates(context.World, layer, west, east, Vector3d.Zero, Fixed64.Zero)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossMapExpansion_CannotContinueWithoutLookupOrCurrentCoverage(bool changeWorld)
    {
        using var context = CreateContext();
        var west = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        var east = new GridConfiguration(Vector3d.Right, Vector3d.Right);
        context.World.TryAddGrid(west, out ushort westSlot).Should().BeTrue();
        context.World.TryAddGrid(east, out _).Should().BeTrue();
        var start = new NavigationCellAddress("west", default);
        var end = new NavigationCellAddress("east", default);
        Publish(context, new NavigationMapBuilder(start.MapId, west)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 2);
        Publish(context, new NavigationMapBuilder(end.MapId, east)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 3);
        var graph = context.Pathing.NavigationGraphStore.Current;
        NavigationPlanarWork.TryGetMap(graph, start.MapId, out var map, out var binding).Should().BeTrue();
        var budget = RouteBudget();
        var meter = new NavigationWorkMeter(budget);
        var request = new NavigationPlanarRouteRequest(start, end, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, budget);
        var search = new NavigationPlanarSearch(context.World, graph,
            context.Pathing.ImmediateRayWorkspace.RayWork.Workspace.Planar, map, binding, request, Policy, meter);
        search.BindSource().Should().Be(NavigationGuideStatus.Success);
        search.ResolveStart(out var origin).Should().Be(NavigationGuideStatus.Success);
        search.BeginEdges(new NavigationMediumStateRef(origin.Node, TraversalMedium.Solid), incoming: false);

        var noEdges = new NavigationAcquisitionSlice(1);
        var noLookups = new NavigationAcquisitionSlice(0, edgeStepLimit: 1);
        int lookupsBefore = meter.LookupProbes;
        search.AdvanceEdge(ref noEdges, out _, out _, out _, out _).Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked);
        search.AdvanceEdge(ref noLookups, out _, out _, out _, out _).Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked);
        meter.LookupProbes.Should().Be(lookupsBefore);

        int nativeDirections = NavigationMap.GetNativeSurfaceDirectionCount(GridTopologyKind.RectangularPrism);
        for (int i = 0; i < nativeDirections; i++)
            Advance().Should().Be(NavigationTraversalEdgeAdvanceStatus.Pending);
        if (changeWorld)
        {
            // Skip the source generation, open the target cursor, then bind its generation.
            for (int i = 0; i < 3; i++)
                Advance().Should().Be(NavigationTraversalEdgeAdvanceStatus.Pending);
            context.World.IncrementGridVersion(westSlot, significant: true);
            Advance().Should().Be(NavigationTraversalEdgeAdvanceStatus.Stale);
        }
        else
        {
            meter.TryConsumeLookupProbes(meter.RemainingLookupProbes).Should().BeTrue();
            Advance().Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        }

        NavigationTraversalEdgeAdvanceStatus Advance()
        {
            var remaining = new NavigationAcquisitionSlice(int.MaxValue, edgeStepLimit: int.MaxValue);
            return search.AdvanceEdge(ref remaining, out _, out _, out _, out _);
        }
    }
}
