using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Fact]
    public void ArrivalRegionRequiresAnAddressOnACompatibleDestinationLayer()
    {
        using var context = CreateScheduledPlanarContext();
        var other = new GridConfiguration(new Vector3d(5, 0, 0), new Vector3d(6, 1, 0));
        context.World.TryAddGrid(other, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder("other", other)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One).SetDefaultCell(Open).Build(), 3);

        Request(new NavigationCellAddress("missing", new VoxelIndex(0, 1, 0))).Should().Be(NavigationGuideStatus.NoMap);
        Request(new NavigationCellAddress("other", new VoxelIndex(0, 1, 0))).Should().Be(NavigationGuideStatus.NoPath);
        Request(new NavigationCellAddress("plane", new VoxelIndex(1, 0, 0))).Should().Be(NavigationGuideStatus.InvalidEnd);
        Request(ScheduledCell(5)).Should().Be(NavigationGuideStatus.InvalidEnd);

        NavigationGuideStatus Request(NavigationCellAddress end) => context.Guides.RequestTopDownGuide(
            ScheduledCell(0), end, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid, TraversalCapability.None,
            PathAlgorithm.AStar, RouteBudget(), out _, arrivalRadius: Fixed64.Two);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ArrivalRegionChargesCandidateWorkAndRespectsTheCandidateCapacity(PathAlgorithm algorithm)
    {
        using var context = CreateScheduledPlanarContext(goalNodeCapacity: 2);
        Request(Fixed64.One, RouteBudget(candidates: 1), out _).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Request(Fixed64.Two, RouteBudget(), out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        Request(Fixed64.MinIncrement, RouteBudget(), out var guide).Should().Be(NavigationGuideStatus.Success);
        using (guide)
        {
            guide.StepCount.Should().Be(1);
            guide.TryGetCurrentStep(out var address, out _).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(ScheduledCell(0));
        }

        NavigationGuideStatus Request(Fixed64 arrivalRadius, NavigationWorkBudget budget,
            out NavigationTopDownGuideLease result) => context.Guides.RequestTopDownGuide(
            ScheduledCell(0), ScheduledCell(0), Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, algorithm, budget, out result, arrivalRadius: arrivalRadius);
    }

    [Fact]
    public void ArrivalRegionRequiresLookupWorkToBindAndEnumerateItsDestinationGrid()
    {
        using var context = CreateScheduledPlanarContext();
        int exactLookups = FindMinimum(Fixed64.Zero);
        int regionalLookups = FindMinimum(Fixed64.MinIncrement);
        regionalLookups.Should().BeGreaterThanOrEqualTo(exactLookups + 2);

        int FindMinimum(Fixed64 radius)
        {
            for (int lookups = 1; lookups < 32; lookups++)
            {
                var status = Request(lookups, radius, out var guide);
                using (guide)
                {
                    if (status == NavigationGuideStatus.Success)
                    {
                        guide.StepCount.Should().Be(1);
                        return lookups;
                    }
                    status.Should().Be(NavigationGuideStatus.BudgetExceeded);
                }
            }
            throw new InvalidOperationException("The same-cell guide exceeded the test lookup ceiling.");
        }

        NavigationGuideStatus Request(int lookupBudget, Fixed64 radius, out NavigationTopDownGuideLease result) =>
            context.Guides.RequestTopDownGuide(ScheduledCell(0), ScheduledCell(0), Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.AStar,
                RouteBudget(lookups: lookupBudget), out result, arrivalRadius: radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrivalRegionDiscardsTheDestinationCursorAfterItsGridChanges(bool afterBinding)
    {
        using var context = CreateScheduledPlanarContext();
        using var graphLease = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
        var graph = graphLease.Graph;
        graph.TryGetMap("plane", out var map).Should().BeTrue();
        var budget = RouteBudget();
        var request = new NavigationPlanarRouteRequest(ScheduledCell(0), ScheduledCell(1), Fixed64.Quarter,
            Policy.Key, TraversalMedium.Solid, TraversalCapability.None, budget, arrivalRadius: Fixed64.One);
        var work = new NavigationRayWorkspace(1, 1, 1, 128, 128).Planar;
        var search = new NavigationPlanarSearch(context.World, graph, work, map, map.Map.PlanarBinding!.Value,
            request, Policy, new NavigationWorkMeter(budget));
        search.BindSource().Should().Be(NavigationGuideStatus.Success);
        search.ResolveStart(out _).Should().Be(NavigationGuideStatus.Success);
        var goals = new NavigationResolvedEndpoint[4];
        int count = 0;
        if (afterBinding)
            search.AdvanceGoalCandidates(goals, ref count).Should().Be(NavigationGuideStatus.Pending);

        context.World.TryRemoveGrid(map.GridIdentity.GridIndex).Should().BeTrue();
        search.AdvanceGoalCandidates(goals, ref count).Should().Be(NavigationGuideStatus.Stale);
        count.Should().Be(afterBinding ? 1 : 0);
    }

    [Fact]
    public void ArrivalRegionLeaseRequiresItsActiveRegionalTerminalAndMap()
    {
        using var context = CreateScheduledPlanarContext();
        var terminal = Vector2d.Right;
        default(NavigationGuideLease).IsPlanarArrivalInRange(terminal, "plane", Fixed64.One).Should().BeFalse();
        Request(null, out var exact).Should().Be(NavigationGuideStatus.Success);
        using (exact)
            exact.RouteLease.IsPlanarArrivalInRange(terminal, "plane", Fixed64.One).Should().BeFalse();

        Request(Fixed64.One, out var regional).Should().Be(NavigationGuideStatus.Success);
        using (regional)
        {
            regional.RouteLease.IsPlanarArrivalInRange(terminal, "plane", Fixed64.One).Should().BeFalse();
            regional.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            regional.RouteLease.IsPlanarArrivalInRange(terminal, "elsewhere", Fixed64.One).Should().BeFalse();
            regional.RouteLease.IsPlanarArrivalInRange(terminal, "plane", Fixed64.One).Should().BeTrue();
        }
        regional.RouteLease.IsPlanarArrivalInRange(terminal, "plane", Fixed64.One).Should().BeFalse();

        NavigationGuideStatus Request(Fixed64? radius, out NavigationTopDownGuideLease guide) =>
            context.Guides.RequestTopDownGuide(ScheduledCell(0), ScheduledCell(2), Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.AStar, RouteBudget(), out guide,
                arrivalRadius: radius);
    }
}
