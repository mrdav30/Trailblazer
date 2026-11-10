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
    public void Guide_ExplicitPlanarJumpIsDirectedAndHoldsAnExactLeaseToken(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        BuildPlanarJump(context);
        NavigationCellAddress start = RouteCell(0, 0);
        NavigationCellAddress finish = RouteCell(4, 0);

        Request(start, finish, TraversalCapability.Jump, allowTransitions: false, out _)
            .Should().Be(NavigationGuideStatus.NoPath);
        Request(start, finish, TraversalCapability.None, allowTransitions: true, out _)
            .Should().Be(NavigationGuideStatus.NoPath);
        Request(finish, start, TraversalCapability.Jump, allowTransitions: true, out _)
            .Should().Be(NavigationGuideStatus.NoPath);
        Request(start, finish, TraversalCapability.Jump, allowTransitions: true, out var guide)
            .Should().Be(NavigationGuideStatus.Success);
        using var lease = guide;
        Request(start, finish, TraversalCapability.Jump, allowTransitions: true, out var independent)
            .Should().Be(NavigationGuideStatus.Success);
        using var otherLease = independent;
        guide.TotalCost.Should().Be((Fixed64)5 + Fixed64.Half);
        guide.StepCount.Should().Be(4);
        guide.TryGetPendingTransition(out _).Should().Be(NavigationGuideStatus.Pending);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        independent.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetPendingTransition(out NavigationTransitionInstruction2D jump).Should().Be(NavigationGuideStatus.Success);
        jump.Id.Should().Be("gap");
        jump.OwnerMapId.Should().Be("plane");
        jump.Type.Should().Be(TraversalTransitionType.Jump);
        jump.SourceAddress.Should().Be(RouteCell(1, 0));
        jump.DestinationAddress.Should().Be(RouteCell(3, 0));
        jump.SourcePosition.Should().Be(new Vector2d(1, 0));
        jump.DestinationPosition.Should().Be(new Vector2d(3, 0));
        jump.SourceMedium.Should().Be(TraversalMedium.Solid);
        jump.DestinationMedium.Should().Be(TraversalMedium.Solid);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Pending);
        independent.CompletePendingTransition(jump).Should().Be(NavigationGuideStatus.Stale);
        guide.CompletePendingTransition(jump).Should().Be(NavigationGuideStatus.Success);
        guide.CompletePendingTransition(jump).Should().Be(NavigationGuideStatus.Stale);
        guide.TryGetCurrentStep(out var address, out _).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(RouteCell(3, 0));

        NavigationGuideStatus Request(NavigationCellAddress from, NavigationCellAddress to,
            TraversalCapability capabilities, bool allowTransitions, out NavigationTopDownGuideLease lease) =>
            context.Guides.RequestTopDownGuide(from, to, Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, capabilities, algorithm, RouteBudget(), out lease, allowTransitions);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_TransitionSuppressionStalesAHeldPlanarJump(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        BuildPlanarJump(context);
        Request(out var guide).Should().Be(NavigationGuideStatus.Success);
        using var lease = guide;
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetPendingTransition(out var jump).Should().Be(NavigationGuideStatus.Success);
        Apply(TraversalTransitionOverlayOperation.Suppress("gap"), 3);
        guide.TryGetPendingTransition(out _).Should().Be(NavigationGuideStatus.Stale);
        guide.CompletePendingTransition(jump).Should().Be(NavigationGuideStatus.Stale);
        Request(out _).Should().Be(NavigationGuideStatus.NoPath);
        Apply(TraversalTransitionOverlayOperation.RevertToBake("gap"), 4);
        Request(out var restored).Should().Be(NavigationGuideStatus.Success);
        using var restoredLease = restored;
        restored.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        restored.CompletePendingTransition(jump).Should().Be(NavigationGuideStatus.Stale);

        NavigationGuideStatus Request(out NavigationTopDownGuideLease result) =>
            context.Guides.RequestTopDownGuide(RouteCell(0, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, TraversalCapability.Jump, algorithm, RouteBudget(), out result, allowTransitions: true);

        void Apply(TraversalTransitionOverlayOperation change, long sequence)
        {
            var overlay = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta("plane", transitions: new[] { change }) })),
                sequence, context.FrameCount + 1);
            context.Pathing.Admit(overlay).Should().BeTrue();
            for (int i = 0; i < 16 && overlay.Receipt.Status == NavigationOperationStatus.Pending; i++)
                context.Simulate();
            overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_ChargesPlanarTransitionCandidateBudget(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        BuildPlanarJump(context);
        var budget = new NavigationWorkBudget(8192, 128, 128, 512, 512, 0, 128, 128, 1024, 4096, 0);
        context.Guides.RequestTopDownGuide(RouteCell(0, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.Jump, algorithm, budget, out var lease, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        lease.Status.Should().Be(NavigationGuideStatus.Stale);
        lease.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
        default(NavigationTransitionInstruction2D).Matches(default).Should().BeFalse();

        int edgeLimit = algorithm == PathAlgorithm.AStar ? 1 : 2;
        var limitedEdges = new NavigationWorkBudget(8192, 128, 128, edgeLimit, 512, 128, 128, 128, 1024, 4096, 0);
        context.Guides.RequestTopDownGuide(RouteCell(1, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.Jump, algorithm, limitedEdges, out _, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
    }

    [Fact]
    public void Guide_FlowChargesLookupWorkWhileResolvingTheForwardActionOrdinal()
    {
        using var context = CreateContext();
        BuildPlanarJump(context);
        for (int lookups = 0; lookups < 512; lookups++)
        {
            var result = context.Guides.RequestTopDownGuide(RouteCell(1, 0), RouteCell(4, 0), Fixed64.Quarter,
                Policy.Key, TraversalMedium.Solid, TraversalCapability.Jump, PathAlgorithm.FlowField,
                RouteBudget(lookups: lookups), out var guide, allowTransitions: true);
            if (result == NavigationGuideStatus.Success)
            {
                using var lease = guide;
                lease.TryGetPendingTransition(out var action).Should().Be(NavigationGuideStatus.Success);
                action.Id.Should().Be("gap");
                return;
            }
            result.Should().Be(NavigationGuideStatus.BudgetExceeded);
        }
        Assert.Fail("The bounded lookup sweep never admitted the directed action.");
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 3)]
    [InlineData(PathAlgorithm.FlowField, 1)]
    public void Guide_RejectsAnActionWhenEitherAnchorLosesClearance(PathAlgorithm algorithm, int suppressedX)
    {
        using var context = CreateContext();
        BuildPlanarJump(context);
        RouteOverlay(context, NavigationCellOverlayOperation.Suppress(RouteCell(suppressedX, 0).Index), 3);
        context.Guides.RequestTopDownGuide(RouteCell(0, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.Jump, algorithm, RouteBudget(), out _, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void Guide_RejectsAnUnrepresentablePlanarActionCost()
    {
        using var context = CreateContext();
        BuildPlanarJump(context, Fixed64.MaxValue);
        context.Guides.RequestTopDownGuide(RouteCell(0, 0), RouteCell(4, 0), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.Jump, PathAlgorithm.AStar, RouteBudget(), out _,
            allowTransitions: true).Should().Be(NavigationGuideStatus.CostOverflow);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_RetainsAZeroCostActionFromALaterSemanticPage(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        int sourceX = NavigationSemanticPage.SlotCount + 1;
        NavigationCellAddress source = RouteCell(sourceX, 0);
        NavigationCellAddress destination = RouteCell(sourceX + 2, 0);
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(sourceX + 3, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= sourceX; x++)
            builder.AddCell(RouteCell(x, 0).Index, Open);
        builder.AddCell(destination.Index, Open);
        builder.AddTransition(new TraversalTransitionDefinition("zero-cost-gap", TraversalTransitionType.Custom,
            source.Index, TraversalMedium.Solid, destination, TraversalMedium.Solid));
        builder.AddTransition(new TraversalTransitionDefinition("zz-parallel-gap", TraversalTransitionType.Custom,
            source.Index, TraversalMedium.Solid, destination, TraversalMedium.Solid));
        builder.AddTransition(new TraversalTransitionDefinition("earlier-source", TraversalTransitionType.Custom,
            RouteCell(sourceX - 1, 0).Index, TraversalMedium.Solid, destination, TraversalMedium.Solid,
            actionCost: (Fixed64)5));
        Publish(context, builder.Build(), 2);

        context.Guides.RequestTopDownGuide(source, destination, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, algorithm, RouteBudget(), out var result, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.Success);
        using var lease = result;
        lease.TotalCost.Should().Be(Fixed64.Zero);
        lease.TryGetPendingTransition(out var action).Should().Be(NavigationGuideStatus.Success);
        action.Id.Should().Be("zero-cost-gap");
        action.SourceAddress.Should().Be(source);
        action.DestinationAddress.Should().Be(destination);
        lease.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_AuthoredActionCanLandOnACompatibleMapPublishedLater(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var left = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        var right = new GridConfiguration(new Vector3d(4, 0, 0), new Vector3d(5, 1, 0));
        context.World.TryAddGrid(left, out _).Should().BeTrue();
        context.World.TryAddGrid(right, out _).Should().BeTrue();
        var source = new NavigationCellAddress("left", new VoxelIndex(1, 1, 0));
        var destination = new NavigationCellAddress("right", new VoxelIndex(0, 1, 0));
        Publish(context, new NavigationMapBuilder("left", left).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .SetDefaultCell(Open).AddTransition(new TraversalTransitionDefinition("cross-map", TraversalTransitionType.Jump,
                source.Index, TraversalMedium.Solid, destination, TraversalMedium.Solid,
                TraversalCapability.Jump, Fixed64.Two)).Build(), 2);
        Publish(context, new NavigationMapBuilder("right", right).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .SetDefaultCell(Open).Build(), 3);

        context.Guides.RequestTopDownGuide(source, destination, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.Jump, algorithm, RouteBudget(), out var result, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.Success);
        using var lease = result;
        lease.TotalCost.Should().Be(Fixed64.Two);
        lease.TryGetPendingTransition(out var action).Should().Be(NavigationGuideStatus.Success);
        action.OwnerMapId.Should().Be("left");
        action.DestinationAddress.Should().Be(destination);
        action.DestinationPosition.Should().Be(new Vector2d(4, 0));
    }

    private static void BuildPlanarJump(TrailblazerWorldContext context, Fixed64? actionCost = null)
    {
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var both = new NavigationCell(TraversalMedia.Solid | TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var charged = new NavigationCell(TraversalMedia.Solid | TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Half, Fixed64.One, Fixed64.Two);
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .AddCell(RouteCell(0, 0).Index, Open).AddCell(RouteCell(1, 0).Index, both)
            .AddCell(RouteCell(3, 0).Index, charged).AddCell(RouteCell(4, 0).Index, Open)
            .AddTransition(new TraversalTransitionDefinition("gap", TraversalTransitionType.Jump,
                RouteCell(1, 0).Index, TraversalMedium.Solid, RouteCell(3, 0), TraversalMedium.Solid,
                TraversalCapability.Jump, actionCost ?? (Fixed64)3))
            .AddTransition(new TraversalTransitionDefinition("gas-gap", TraversalTransitionType.Custom,
                RouteCell(1, 0).Index, TraversalMedium.Gas, RouteCell(3, 0), TraversalMedium.Gas))
            .AddTransition(new TraversalTransitionDefinition("dormant", TraversalTransitionType.Jump,
                RouteCell(1, 0).Index, TraversalMedium.Solid,
                new NavigationCellAddress("unpublished", RouteCell(3, 0).Index), TraversalMedium.Solid));
        Publish(context, builder.Build(), 2);
    }
}
