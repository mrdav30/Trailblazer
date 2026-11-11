using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void GuideRetainsTheCertifiedStepBendAndChargesForwardEntry(PathAlgorithm algorithm, bool descending)
    {
        var area = new NavigationAreaPolicy(Policy.Key, new[]
        {
            new NavigationAreaRule(true, Fixed64.Quarter)
        });
        using var context = Scene(out _, area);
        Publish(context, BuildGuideStep(Fixed64.Half));

        var low = Cell(0);
        var high = Cell(1);
        var start = descending ? high : low;
        var end = descending ? low : high;
        var profile = Profile(Fixed64.Half, up: descending ? Fixed64.Zero : Fixed64.Quarter,
            down: descending ? Fixed64.Quarter : Fixed64.Zero);
        var pathing = context.Pathing;
        var request = new NavigationPlanarRouteRequest(start, end, profile.Radius, Policy.Key, TraversalMedium.Solid,
            profile.Capabilities, GuideBudget());
        long exactBytes = NavigationPlanarGuidePayload.MeasureBytes(3);
        var small = new NavigationAStarPayloadCache(context.World, 1, exactBytes, exactBytes - 1, exactBytes, 2);
        pathing.FindPlanarRoute(request, algorithm, default, out _, out _, small, out var unavailable, profile)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);
        unavailable.Should().BeNull();
        small.ActiveLeaseCount.Should().Be(0);
        small.ReservedPayloadBytes.Should().Be(0);

        RequestSideView(context, end, start, profile, algorithm, out var rejected)
            .Should().Be(NavigationGuideStatus.NoPath);
        rejected.StepCount.Should().Be(0);

        RequestSideView(context, start, end, profile, algorithm, out var result).Should().Be(NavigationGuideStatus.Success);
        using var guide = result;
        guide.StepCount.Should().Be(3);
        guide.TotalCost.Should().Be(Fixed64.One + Fixed64.Half + (descending ? Fixed64.Zero : Fixed64.Half));
        var lowCenter = Vector2d.Zero;
        var highCenter = new Vector2d(Fixed64.One, Fixed64.Quarter);
        VerifyStep(0, start, descending ? highCenter : lowCenter);
        VerifyStep(1, low, new Vector2d(Fixed64.Zero, Fixed64.Quarter));
        VerifyStep(2, end, descending ? lowCenter : highCenter);
        guide.CurrentStepIndex.Should().Be(2);
        pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(exactBytes);

        void VerifyStep(int step, NavigationCellAddress owner, Vector2d center)
        {
            guide.CurrentStepIndex.Should().Be(step);
            guide.TryGetCurrentStep(out var actualOwner, out var actualCenter).Should().Be(NavigationGuideStatus.Success);
            actualOwner.Should().Be(owner);
            actualCenter.Should().Be(center);
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void GuideRejectsStepCostOverflowWithoutReturningALease(PathAlgorithm algorithm)
    {
        using var context = Scene(out _);
        Publish(context, BuildGuideStep(Fixed64.MaxValue));
        var profile = Profile(Fixed64.Half, up: Fixed64.Quarter);
        RequestSideView(context, Cell(0), Cell(1), profile, algorithm, out var guide)
            .Should().Be(NavigationGuideStatus.CostOverflow);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void GuideTraversesCompatibleMapsAndInvalidatesOnBakeOrWorldChange(PathAlgorithm algorithm)
    {
        using var context = Scene(out _);
        var source = SideView("side", Grid).AddCell(Cell(4, 0).Index, Solid);
        AddFloor(source, 3, -Fixed64.Half);
        AddFloor(source, 4, -Fixed64.Half);
        Publish(context, source.Build());
        var adjacent = new GridConfiguration(new Vector3d(5, 0, 0), new Vector3d(5, 1, 0));
        context.World.TryAddGrid(adjacent, out _).Should().BeTrue();
        var index = new VoxelIndex(0, 1, 0);
        var target = SideView("neighbor", adjacent).AddCell(index, Solid)
            .AddPlanarSupport(index, new((Fixed64)4 + Fixed64.Half, -Fixed64.Half),
                new((Fixed64)5 + Fixed64.Half, -Fixed64.Half));
        Publish(context, target.Build(), 3);
        var end = new NavigationCellAddress("neighbor", index);
        var profile = Profile();

        RequestSideView(context, Cell(3), end, profile, algorithm, out var result).Should().Be(NavigationGuideStatus.Success);
        using var guide = result;
        RequestSideView(context, Cell(3), end, profile, algorithm, out var second).Should().Be(NavigationGuideStatus.Success);
        using var independent = second;
        guide.StepCount.Should().Be(3);
        guide.TotalCost.Should().Be(Fixed64.Two);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out var owner, out var center).Should().Be(NavigationGuideStatus.Success);
        owner.Should().Be(end);
        center.Should().Be(new Vector2d((Fixed64)5, -Fixed64.Quarter));
        independent.CurrentStepIndex.Should().Be(0);

        ulong rawSequence = context.World.ChangeSequence;
        Publish(context, SideView("neighbor", adjacent).AddCell(index, Solid).Build(), 4, 2);
        context.World.ChangeSequence.Should().Be(rawSequence);
        guide.TryGetCurrentStep(out owner, out center).Should().Be(NavigationGuideStatus.Stale);
        owner.Should().Be(default(NavigationCellAddress));
        center.Should().Be(Vector2d.Zero);
        independent.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
        RequestSideView(context, Cell(3), end, profile, algorithm, out _).Should().Be(NavigationGuideStatus.InvalidEnd);

        RequestSideView(context, Cell(3), Cell(3), profile, algorithm, out var stationary)
            .Should().Be(NavigationGuideStatus.Success);
        using var one = stationary;
        one.StepCount.Should().Be(1);
        one.TotalCost.Should().Be(Fixed64.Zero);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
            .Should().BeTrue();
        one.Status.Should().Be(NavigationGuideStatus.Stale);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void GuideRejectsInvalidAdmissionAndBudgetBeforeRetainingPayload(PathAlgorithm algorithm)
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        Publish(context, builder.Build());
        var profile = Profile();
        RequestSideView(context, default, Cell(1), profile, algorithm, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        RequestSideView(context, Cell(0), default, profile, algorithm, out _).Should().Be(NavigationGuideStatus.InvalidEnd);
        RequestSideView(context, Cell(0), Cell(1), default, algorithm, out _).Should().Be(NavigationGuideStatus.InvalidProfile);
        RequestSideView(context, Cell(0), Cell(1), profile, (PathAlgorithm)byte.MaxValue, out _)
            .Should().Be(NavigationGuideStatus.Unsupported);
        RequestSideView(context, Cell(2), Cell(1), profile, algorithm, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        RequestSideView(context, Cell(0), Cell(1), profile, algorithm, out _, GuideBudget(candidates: 0))
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        RequestSideView(context, Cell(0), Cell(1), profile, algorithm, out _, GuideBudget(candidates: 1))
            .Should().Be(NavigationGuideStatus.BudgetExceeded);

        // Each of the two anchors must be counted and then materialized inside the same lookup budget.
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        for (int lookups = 1; lookups < 128; lookups++)
        {
            var status = RequestSideView(context, Cell(0), Cell(1), profile, algorithm, out var result,
                GuideBudget(lookups: lookups));
            using var guide = result;
            if (status == NavigationGuideStatus.Success)
            {
                guide.StepCount.Should().Be(2);
                return;
            }
            status.Should().Be(NavigationGuideStatus.BudgetExceeded);
            guide.StepCount.Should().Be(0);
            cache.ActiveLeaseCount.Should().Be(0);
            cache.ReservedPayloadBytes.Should().Be(0);
        }
        Assert.Fail("The finite route did not fit the lookup budget.");
    }

    private static NavigationGuideStatus RequestSideView(TrailblazerWorldContext context, NavigationCellAddress start,
        NavigationCellAddress end, in NavigationAgentProfile2D profile, PathAlgorithm algorithm,
        out NavigationSideViewGuideLease guide, NavigationWorkBudget? budget = null) =>
        context.Guides.RequestSideViewGuide(start, end, profile, Policy.Key, algorithm,
            budget ?? GuideBudget(), out guide);

    private static NavigationWorkBudget GuideBudget(int lookups = 4096, int candidates = 2, int nodes = 64,
        int edges = 128, int covered = 4096) => new(lookups, candidates, nodes, edges, 0, 0, 0, 0, 0, covered, 0);

    private static NavigationMap BuildGuideStep(Fixed64 enterCost)
    {
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        return builder.AddCell(Cell(1).Index, new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
                default, enterCost, Fixed64.One, Fixed64.Two))
            .AddPlanarSupport(Cell(1).Index, new(Fixed64.Half, -Fixed64.Quarter),
                new(Fixed64.One + Fixed64.Half, -Fixed64.Quarter))
            .AddCell(Cell(0, 2).Index, Air).AddCell(Cell(1, 2).Index, Air).Build();
    }
}
