using System;
using System.Linq;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Route_DetoursAtExactCostAndRequeriesAfterSemanticPublication(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
        {
            if (x != 2)
                builder.AddCell(RouteCell(x, 0).Index, Open);
            builder.AddCell(RouteCell(x, 1).Index, Open);
        }
        Publish(context, builder.Build(), 2);

        var output = new NavigationCellAddress[16];
        NavigationCellAddress start = RouteCell(0, 0);
        NavigationCellAddress end = RouteCell(4, 0);
        FindRoute(context, start, end, Fixed64.Quarter, algorithm, output, out int count, out Fixed64 cost)
            .Should().Be(NavigationGuideStatus.Success);
        count.Should().Be(7);
        cost.Should().Be((Fixed64)6);
        output[0].Should().Be(start);
        output[count - 1].Should().Be(end);
        var first = output.Take(count).ToArray();
        first.Should().Contain(RouteCell(2, 1)).And.NotContain(RouteCell(2, 0));
        for (int i = 1; i < count; i++)
            ValidateLeg(context, output[i - 1], output[i], Fixed64.Quarter).Should().Be(NavigationGuideStatus.Success);
        FindRoute(context, start, end, Fixed64.Quarter, algorithm, output, out count, out cost)
            .Should().Be(NavigationGuideStatus.Success);
        output.Take(count).Should().Equal(first);

        ulong rawSequence = context.World.ChangeSequence;
        RouteOverlay(context, NavigationCellOverlayOperation.Set(RouteCell(2, 0).Index, Open), 3);
        context.World.ChangeSequence.Should().Be(rawSequence);
        FindRoute(context, start, end, Fixed64.Quarter, algorithm, output, out count, out cost)
            .Should().Be(NavigationGuideStatus.Success);
        cost.Should().Be((Fixed64)4);
        output.Take(count).Should().Equal(Enumerable.Range(0, 5).Select(x => RouteCell(x, 0)));
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Route_PortalUsesFullCircleAndChargesOnlyForwardEntry(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var toll = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Quarter, Fixed64.One, Fixed64.Two);
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
            builder.AddCell(RouteCell(x, 1).Index, x == 4 ? toll : Open);
        Publish(context, builder.Build(), 2);
        var output = new NavigationCellAddress[8];
        var start = RouteCell(0, 1);
        var end = RouteCell(4, 1);
        FindRoute(context, start, end, Fixed64.Half, algorithm, output, out int count, out Fixed64 cost)
            .Should().Be(NavigationGuideStatus.Success);
        output.Take(count).Should().Equal(Enumerable.Range(0, 5).Select(x => RouteCell(x, 1)));
        cost.Should().Be((Fixed64)4 + Fixed64.Quarter);
        FindRoute(context, end, start, Fixed64.Half, algorithm, output, out count, out cost)
            .Should().Be(NavigationGuideStatus.Success);
        cost.Should().Be((Fixed64)4);
        FindRoute(context, start, end, Fixed64.Half + Fixed64.MinIncrement, algorithm, output, out count, out cost)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        count.Should().Be(0);
        cost.Should().Be(Fixed64.Zero);
        output[0].Should().Be(end);

        FindRoute(context, start, end, Fixed64.Half, algorithm, output.AsSpan(0, 4), out count, out cost)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
        output[0].Should().Be(end);
        RouteOverlay(context, NavigationCellOverlayOperation.Suppress(RouteCell(2, 1).Index), 3);
        FindRoute(context, start, end, Fixed64.Half, algorithm, output, out count, out cost)
            .Should().Be(NavigationGuideStatus.NoPath);
        output[0].Should().Be(end);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Route_UsesSharedBudgetsAndLeavesTheCallerBufferUnchangedOnFailure(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        var start = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var end = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 0));
        var output = new[] { end, start };

        Check(RouteBudget(candidates: 0), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(candidates: 1), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(lookups: 0), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(lookups: 4), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(edges: 0), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(covered: 2), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(nodes: 1), NavigationGuideStatus.BudgetExceeded);
        if (algorithm == PathAlgorithm.FlowField)
            Check(RouteBudget(lookups: 9), NavigationGuideStatus.BudgetExceeded);
        Check(RouteBudget(edges: 1), NavigationGuideStatus.Success);
        output.Should().Equal(start, end);

        FindRoute(context, start, start, Fixed64.Quarter, algorithm, output, out int count, out Fixed64 cost)
            .Should().Be(NavigationGuideStatus.Success);
        count.Should().Be(1);
        cost.Should().Be(Fixed64.Zero);

        void Check(NavigationWorkBudget budget, NavigationGuideStatus expected)
        {
            FindRoute(context, start, end, Fixed64.Quarter, algorithm, output, out int length, out Fixed64 total, budget)
                .Should().Be(expected);
            if (expected == NavigationGuideStatus.Success)
                return;
            length.Should().Be(0);
            total.Should().Be(Fixed64.Zero);
            output.Should().Equal(end, start);
        }
    }

    [Fact]
    public void Route_RejectsInvalidDomainProfileAndEndpointInputs()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var start = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var end = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 0));
        var offLayer = new NavigationCellAddress("plane", default);
        var other = new NavigationCellAddress("other", end.Index);
        var output = new NavigationCellAddress[2];

        FindRoute(context, start, end, Fixed64.Quarter, PathAlgorithm.AStar, output, out _, out _)
            .Should().Be(NavigationGuideStatus.NoMap);
        Publish(context, TopDown("plane", grid).AddCell(start.Index, Open).Build(), 2);
        Check(default, start, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidStart);
        Check(start, default, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidEnd);
        Check(start, other, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.Unsupported);
        Check(start, start, Fixed64.Zero, PathAlgorithm.AStar, NavigationGuideStatus.InvalidProfile);
        Check(start, start, Fixed64.Quarter, (PathAlgorithm)255, NavigationGuideStatus.Unsupported);
        Check(offLayer, start, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidStart);
        Check(start, offLayer, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidEnd);
        Check(end, start, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidStart);
        Check(start, end, Fixed64.Quarter, PathAlgorithm.AStar, NavigationGuideStatus.InvalidEnd);
        context.Pathing.FindTopDownRoute(start, start, Fixed64.Quarter, new NavigationAreaPolicyKey("missing", 1),
            TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.AStar, RouteBudget(), output, out _, out _)
            .Should().Be(NavigationGuideStatus.Stale);

        void Check(NavigationCellAddress from, NavigationCellAddress to, Fixed64 radius, PathAlgorithm algorithm,
            NavigationGuideStatus expected) =>
            FindRoute(context, from, to, radius, algorithm, output, out _, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Route_ReportsSweepCapacityAndCheckedCostOverflow(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var start = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var end = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 0));
        var toll = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.MaxValue, Fixed64.One, Fixed64.Two);
        Publish(context, TopDown("plane", grid).AddCell(start.Index, Open).AddCell(end.Index, toll).Build(), 2);
        var output = new NavigationCellAddress[2];
        FindRoute(context, start, end, Fixed64.Quarter, algorithm, output, out _, out _)
            .Should().Be(NavigationGuideStatus.CostOverflow);

        var request = new NavigationTopDownRouteRequest(start, end, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, RouteBudget());
        var aStar = algorithm == PathAlgorithm.AStar ? new NavigationAStarWorkspace(1, 1, 1, 2, 1, 0, 2) : null;
        var flow = algorithm == PathAlgorithm.FlowField ? new NavigationFlowFieldWorkspace(1, 1, 1, 2, 1, 0) : null;
        NavigationTopDownRouteWork.Find(context.World, context.Pathing.NavigationGraphStore, request,
            aStar, aStar == null ? null : new NavigationRayWork(aStar.RayWorkspace), flow, output, out _, out _, null, out _)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
    }

    [Fact]
    public void Route_ReportsSweepBoundsOverflowWhileExpandingAnIntermediateNeighbor()
    {
        using var context = CreateContext();
        Fixed64 maxCenter = Fixed64.MaxValue - (Fixed64.One - Fixed64.MinIncrement);
        var grid = new GridConfiguration(new Vector3d(maxCenter - (Fixed64)6, Fixed64.Zero, (Fixed64)(-2)),
            new Vector3d(maxCenter - Fixed64.One, Fixed64.One, Fixed64.Two));
        var wide = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0),
            topologyMetrics: GridForge.Grids.Topology.GridTopologyMetrics.Rectangular((Fixed64)4, Fixed64.One, Fixed64.One));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        context.World.TryAddGrid(wide, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        Publish(context, TopDown("wide", wide).SetDefaultCell(Open).Build(), 3);
        var start = new NavigationCellAddress("plane", new VoxelIndex(4, 1, 2));
        var end = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 2));
        var output = new NavigationCellAddress[8];
        FindRoute(context, start, end, Fixed64.Quarter, PathAlgorithm.AStar, output, out _, out _)
            .Should().Be(NavigationGuideStatus.CostOverflow);
    }

    [Fact]
    public void Route_SharesLaneAndGraphLeaseCapacity()
    {
        using var context = CreateContext();
        var address = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var output = new NavigationCellAddress[1];
        var request = new NavigationTopDownRouteRequest(address, address, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, RouteBudget());
        var coordinator = new NavigationQueryAdmissionCoordinator(1);
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        using var aStar = new NavigationAStarAdmissionGate(context.World, store, context.Settings.QueryLimits, coordinator);
        using var flow = new NavigationFlowAdmissionGate(context.World, store, context.Settings.QueryLimits, coordinator,
            context.Pathing.ImmediateRayWorkspace);
        coordinator.TryReservePrefix(PathAlgorithm.FlowField, 1, out var reservation).Should().Be(1);
        aStar.FindTopDown(context.World, request, output, out _, out _, null, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        coordinator.Release(reservation);
        coordinator.TryReservePrefix(PathAlgorithm.AStar, 1, out reservation).Should().Be(1);
        flow.FindTopDown(context.World, request, output, out _, out _, null, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        coordinator.Release(reservation);

        aStar.Dispose();
        flow.Dispose();
        aStar.FindTopDown(context.World, request, output, out _, out _, null, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        flow.FindTopDown(context.World, request, output, out _, out _, null, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);

        var leases = new NavigationWorldGraphLease?[context.Settings.MaxConcurrentSnapshotLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++)
                leases[i] = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
            FindRoute(context, address, address, Fixed64.Quarter, PathAlgorithm.AStar, output, out _, out _)
                .Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            foreach (NavigationWorldGraphLease? lease in leases)
                lease?.Dispose();
        }
    }

    private static NavigationCellAddress RouteCell(int x, int z) => new("plane", new VoxelIndex(x + 1, 1, z + 1));

    private static NavigationGuideStatus FindRoute(TrailblazerWorldContext context, NavigationCellAddress start,
        NavigationCellAddress end, Fixed64 radius, PathAlgorithm algorithm, Span<NavigationCellAddress> output,
        out int count, out Fixed64 cost, NavigationWorkBudget? budget = null) =>
        context.Pathing.FindTopDownRoute(start, end, radius, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, algorithm, budget ?? RouteBudget(), output, out count, out cost);

    private static NavigationWorkBudget RouteBudget(int lookups = 8192, int candidates = 128, int nodes = 128,
        int edges = 512, int covered = 4096) => new(lookups, candidates, nodes, edges, 512, 128, 128, 128, 1024, covered, 0);

    private static void RouteOverlay(TrailblazerWorldContext context, NavigationCellOverlayOperation cell, long sequence)
    {
        var overlay = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
            new[] { new NavigationMapOverlayDelta("plane", new[] { cell }) })), sequence, context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        for (int i = 0; i < 16 && overlay.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }
}
