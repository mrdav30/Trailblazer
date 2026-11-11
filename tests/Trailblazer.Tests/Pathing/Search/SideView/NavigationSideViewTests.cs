using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    private static readonly NavigationAreaPolicy Policy = new(new NavigationAreaPolicyKey("support", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });
    private static readonly NavigationCell Solid = new(TraversalMedia.Solid, TraversalCapability.None, default,
        Fixed64.Zero, Fixed64.One, Fixed64.Two);
    private static readonly NavigationCell Air = new(TraversalMedia.Gas, TraversalCapability.None, default,
        Fixed64.One, Fixed64.Zero, Fixed64.Zero);
    private static readonly GridConfiguration Grid = new(new Vector3d(0, 0, -1), new Vector3d(4, 1, 1));

    [Fact]
    public void CapsuleFitsOpenHeadroomButRejectsPenetrationAndAnUnsupportedGap()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        foreach (int x in new[] { 0, 1, 3, 4 }) AddFloor(builder, x, -Fixed64.Half);
        builder.AddCell(Cell(2).Index, Air).AddCell(Cell(0, 2).Index, Air).AddCell(Cell(1, 2).Index, Air);
        Publish(context, builder.Build());

        var standing = Profile(Fixed64.Half + Fixed64.Quarter);
        Anchor(context, Cell(0), standing, out Vector2d open).Should().Be(NavigationGuideStatus.Success);
        open.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.FromFraction(1, 8)));
        Leg(context, Cell(0), Cell(1), standing).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(3), standing, out var blocked).Should().Be(NavigationGuideStatus.InvalidStart);
        blocked.Should().Be(Vector2d.Zero);
        Leg(context, Cell(3), Cell(4), standing).Should().Be(NavigationGuideStatus.NoPath);

        foreach (Fixed64 axis in new[] { Fixed64.Zero, Fixed64.Half - Fixed64.MinIncrement, Fixed64.Half })
        {
            Anchor(context, Cell(3), Profile(axis), out _).Should().Be(NavigationGuideStatus.Success);
            Leg(context, Cell(3), Cell(4), Profile(axis)).Should().Be(NavigationGuideStatus.Success);
        }
        Leg(context, Cell(3), Cell(4), Profile(Fixed64.Half + Fixed64.MinIncrement)).Should().Be(NavigationGuideStatus.NoPath);
        Anchor(context, Cell(2), Profile(), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Leg(context, Cell(2), Cell(3), Profile()).Should().Be(NavigationGuideStatus.InvalidStart);
        Leg(context, Cell(1), Cell(3), Profile()).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StepProvesTheLiftAndUsesDirectedHeightLimits(bool ceiling, bool lowerOnRight)
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        int low = lowerOnRight ? 1 : 0;
        int high = 1 - low;
        AddFloor(builder, low, -Fixed64.Half);
        AddFloor(builder, high, -Fixed64.Quarter);
        builder.AddCell(Cell(high, 2).Index, Air);
        if (!ceiling) builder.AddCell(Cell(low, 2).Index, Air);
        Publish(context, builder.Build());

        var profile = Profile(Fixed64.Half, up: Fixed64.Quarter, down: Fixed64.Quarter);
        Anchor(context, Cell(0), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(1), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, Cell(low), Cell(high), profile).Should().Be(ceiling ? NavigationGuideStatus.NoPath : NavigationGuideStatus.Success);
        Leg(context, Cell(high), Cell(low), profile).Should().Be(ceiling ? NavigationGuideStatus.NoPath : NavigationGuideStatus.Success);
        Leg(context, Cell(low), Cell(high), Profile(Fixed64.Half, up: Fixed64.Quarter - Fixed64.MinIncrement))
            .Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, Cell(high), Cell(low), Profile(Fixed64.Half, down: Fixed64.Quarter - Fixed64.MinIncrement))
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void CollinearSlopeUsesItsNormalOffsetAndRejectsAChangedJoin()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid).AddCell(Cell(0).Index, Solid).AddCell(Cell(1).Index, Solid)
            .AddPlanarSupport(Cell(0).Index, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, -Fixed64.Quarter))
            .AddPlanarSupport(Cell(1).Index, new(Fixed64.Half, -Fixed64.Quarter), new(Fixed64.One + Fixed64.Half, Fixed64.Zero));
        Publish(context, builder.Build());
        Anchor(context, Cell(0), Profile(), out var center).Should().Be(NavigationGuideStatus.Success);
        center.X.Should().BeLessThan(Fixed64.Zero);
        center.Y.Should().BeGreaterThan(-Fixed64.FromFraction(3, 8));
        Leg(context, Cell(0), Cell(1), Profile()).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(0), Profile(normal: Fixed64.One), out _).Should().Be(NavigationGuideStatus.InvalidStart);

        builder = SideView("side", Grid).AddCell(Cell(0).Index, Solid).AddCell(Cell(1).Index, Solid)
            .AddPlanarSupport(Cell(0).Index, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, -Fixed64.Quarter))
            .AddPlanarSupport(Cell(1).Index, new(Fixed64.Half, -Fixed64.Quarter), new(Fixed64.One + Fixed64.Half, Fixed64.Quarter));
        Publish(context, builder.Build(), 3, 2);
        Leg(context, Cell(0), Cell(1), Profile()).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void LegChargesBudgetsAndHasNoWarmAllocation()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        Publish(context, builder.Build());
        var profile = Profile();
        Leg(context, Cell(0), Cell(1), profile, Budget(edges: 0)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Leg(context, Cell(0), Cell(1), profile, Budget(candidates: 0)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Leg(context, Cell(0), Cell(1), profile, Budget(candidates: 1)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Leg(context, Cell(0), Cell(1), profile, Budget(lookups: 2)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Leg(context, Cell(0), Cell(1), profile, Budget(covered: 0)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        for (int i = 0; i < 128; i++) Leg(context, Cell(0), Cell(1), profile);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allProved = true;
        for (int i = 0; i < 128; i++) allProved &= Leg(context, Cell(0), Cell(1), profile) == NavigationGuideStatus.Success;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allProved.Should().BeTrue();
        allocated.Should().Be(0);
    }

    [Fact]
    public void RoundedAnchorMustClearItsOwnSlope()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid).AddCell(Cell(0).Index, Solid)
            .AddPlanarSupport(Cell(0).Index, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, Fixed64.Half));
        Publish(context, builder.Build());
        // The rounded offset (-1, +1) raw is only sqrt(2) raw units from z=x.
        Anchor(context, Cell(0), Profile(radius: Fixed64.FromRaw(2)), out var center)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void AnchorCannotPenetrateAnAdjoiningAuthoredSurface()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid).AddCell(Cell(0).Index, Solid)
            .AddPlanarSupport(Cell(0).Index, new(Fixed64.FromFraction(3, 8), -Fixed64.Half),
                new(Fixed64.Half, -Fixed64.Half));
        AddFloor(builder, 1, -Fixed64.Quarter);
        Publish(context, builder.Build());
        Anchor(context, Cell(0), Profile(), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, Cell(0), Profile(radius: Fixed64.FromFraction(1, 16)), out _)
            .Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void SweepChecksAuthoredHeadroomSurfacesBetweenClearEndpoints()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        Fixed64 height = Fixed64.FromFraction(5, 8);
        builder.AddCell(Cell(0, 2).Index, Air).AddCell(Cell(1, 2).Index, Air)
            .AddPlanarSupport(Cell(0, 2).Index, new(Fixed64.FromFraction(7, 16), height), new(Fixed64.Half, height));
        Publish(context, builder.Build());
        var tall = Profile(Fixed64.Half + Fixed64.Quarter);
        Anchor(context, Cell(0), tall, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(1), tall, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, Cell(0), Cell(1), tall).Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, Cell(1), Cell(0), tall).Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, Cell(0), Cell(1), Profile()).Should().Be(NavigationGuideStatus.Success);
    }

    private static TrailblazerWorldContext Scene(out ushort slot, NavigationAreaPolicy? policy = null)
    {
        var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        context.World.TryAddGrid(Grid, out slot).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy ?? Policy, 1, 1)).Should().BeTrue();
        return context;
    }

    private static NavigationMapBuilder SideView(string mapId, GridConfiguration grid) =>
        new NavigationMapBuilder(mapId, grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One);

    private static void Publish(TrailblazerWorldContext context, NavigationMap map, long sequence = 2, long version = 1)
    {
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, version), OverlayReplacementPolicy.Clear,
            sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        Advance(context);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    private static void AddFloor(NavigationMapBuilder builder, int x, Fixed64 height) =>
        builder.AddCell(Cell(x).Index, Solid).AddPlanarSupport(Cell(x).Index,
            new Vector2d((Fixed64)x - Fixed64.Half, height), new Vector2d((Fixed64)x + Fixed64.Half, height));

    private static NavigationCellAddress Cell(int x, int z = 1) => new("side", new VoxelIndex(x, 1, z));

    private static NavigationAgentProfile2D Profile(Fixed64 axis = default, Fixed64 up = default,
        Fixed64 down = default, Fixed64? normal = null, Fixed64? radius = null,
        TraversalCapability capabilities = TraversalCapability.None) => new(NavigationMode2D.SideView,
            radius ?? Fixed64.Quarter, axis, up, down, normal ?? Fixed64.Half, Fixed64.Zero,
            TraversalMedia.Solid, capabilities);

    private static NavigationWorkBudget Budget(int lookups = 256, int candidates = 2, int edges = 1, int covered = 256) =>
        new(lookups, candidates, 0, edges, 0, 0, 0, 0, 0, covered, 0);

    private static NavigationGuideStatus Anchor(TrailblazerWorldContext context, NavigationCellAddress address,
        in NavigationAgentProfile2D profile, out Vector2d center) =>
        context.Pathing.ResolveSideViewAnchor(address, profile, Policy.Key, Budget(), out center);

    private static NavigationGuideStatus Leg(TrailblazerWorldContext context, NavigationCellAddress from,
        NavigationCellAddress to, in NavigationAgentProfile2D profile, NavigationWorkBudget? budget = null) =>
        context.Pathing.ValidateSideViewLeg(from, to, profile, Policy.Key, budget ?? Budget());
}
