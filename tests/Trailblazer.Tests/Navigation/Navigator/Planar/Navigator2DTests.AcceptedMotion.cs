using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ArrivalRegionSelectsTheReachableBoundaryBesideABlockedCenter(PathAlgorithm algorithm)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var goal = Cell(2, 0);
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget, arrivalRadius: -Fixed64.MinIncrement));
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget)
            .Should().Be(NavigationGuideStatus.InvalidEnd);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget, arrivalRadius: Fixed64.Zero)
            .Should().Be(NavigationGuideStatus.InvalidEnd);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget,
            arrivalRadius: Fixed64.One - Fixed64.MinIncrement).Should().Be(NavigationGuideStatus.InvalidEnd);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget, arrivalRadius: Fixed64.One)
            .Should().Be(NavigationGuideStatus.Success);
        var approach = Vector2d.Right;
        bool clipped = false;
        for (int frame = 0; frame < 24 && !navigator.HasArrived; frame++)
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            Vector2d target = navigator.Request.Target.Center;
            bool outside = !clipped && target == approach;
            navigator.Accept(outside ? approach - Vector2d.Right * Fixed64.MinIncrement : target);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            if (outside)
            {
                navigator.HasArrived.Should().BeFalse();
                clipped = true;
            }
        }
        clipped.Should().BeTrue();
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(approach);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void ArrivalRegionCanFinishOnTheProvedFinalLegBeforeItsCanonicalCenter(PathAlgorithm algorithm,
        bool lookAhead)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        if (lookAhead)
            navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(2, 0), Policy.Key, algorithm, Budget,
            arrivalRadius: Fixed64.One + Fixed64.Half).Should().Be(NavigationGuideStatus.Success);
        var approach = Vector2d.Right * Fixed64.Half;
        bool clipped = false;
        for (int frame = 0; frame < 24 && !navigator.HasArrived; frame++)
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            Vector2d target = navigator.Request.Target.Center;
            bool justOutside = !clipped && target == approach;
            navigator.Accept(justOutside ? approach - Vector2d.Right * Fixed64.MinIncrement : target);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            if (justOutside)
            {
                navigator.HasArrived.Should().BeFalse();
                clipped = true;
            }
        }
        clipped.Should().BeTrue();
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.X.Should().BeGreaterThanOrEqualTo(approach.X).And.BeLessThan(Fixed64.One);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrivalRegionLookaheadAdvancesOnlyForAnAcceptedClipOnTheSegment(bool onSegment)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(2, 0), Policy.Key, PathAlgorithm.AStar, Budget,
            arrivalRadius: Fixed64.One + Fixed64.Half).Should().Be(NavigationGuideStatus.Success);

        context.Simulate();
        navigator.PrepareFrameMotion();
        var target = navigator.Request.Target.Center;
        target.X.Should().BeGreaterThan(Fixed64.Zero);
        var accepted = new Vector2d(target.X * Fixed64.Half, onSegment ? Fixed64.Zero : Fixed64.MinIncrement);
        navigator.Accept(accepted);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        navigator.Pose.Center.Should().Be(accepted);
        navigator.HasArrived.Should().BeFalse();

        context.Simulate();
        navigator.PrepareFrameMotion();
        if (onSegment)
            navigator.Request.Target.Center.X.Should().BeGreaterThan(accepted.X);
        else
            navigator.Request.Target.Center.X.Should().BeLessThan(accepted.X);
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        context.LateSimulate();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ArrivalRegionRejectsAClipWhenAcceptedMotionInvalidatesTheGuide(PathAlgorithm algorithm)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(Cell(2, 0), Policy.Key, algorithm, Budget,
            arrivalRadius: Fixed64.One + Fixed64.Half).Should().Be(NavigationGuideStatus.Success);
        for (int frame = 0; frame < 24; frame++)
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            Vector2d target = navigator.Request.Target.Center;
            bool wouldArrive = target == Vector2d.Right * Fixed64.Half;
            navigator.Accept(target);
            if (wouldArrive)
            {
                navigator.OnRead = () => context.World.TryAddGrid(
                    new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 1, 11)), out _).Should().BeTrue();
            }
            navigator.CommitFrameMotion();
            context.LateSimulate();
            if (wouldArrive)
            {
                navigator.Pose.Center.Should().Be(target);
                navigator.HasArrived.Should().BeFalse();
                navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
                return;
            }
        }
        Assert.Fail("The route did not prepare a target inside the goal region.");
    }

    [Fact]
    public void ArrivalRegionLookaheadKeepsIntermediateLegsBeforeTheFinalApproach()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget,
            arrivalRadius: Fixed64.One).Should().Be(NavigationGuideStatus.Success);

        Step(context, navigator);
        navigator.HasArrived.Should().BeFalse();
        for (int frame = 0; frame < 24 && !navigator.HasArrived; frame++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        Vector2d goal = new(Fixed64.Three, Fixed64.One);
        Vector2d.CompareDistanceSquared(navigator.Pose.Center, goal, Vector2d.Zero, Vector2d.Right)
            .Should().BeLessThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(8, 8, 7, 8)]
    [InlineData(8, 9, 8, 8)]
    public void GuidedStepMakesProgressAtOneRawUnit(int startX, int startZ, int nextX, int nextZ)
    {
        using var context = CreateScene(portal: false);
        var offset = new Vector2d(Fixed64.FromRaw(startX), Fixed64.FromRaw(startZ));
        using var navigator = new Host(context, offset, Fixed64.Quarter, speed: (Fixed64)16 * Fixed64.MinIncrement);
        navigator.ApplyGuidedTrekRequest(Cell(0, 0), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);

        Step(context, navigator);
        navigator.Pose.Center.Should().Be(new Vector2d(Fixed64.FromRaw(nextX), Fixed64.FromRaw(nextZ)));
        for (int i = 1; i < startX + startZ && navigator.Pose.Center != Vector2d.Zero; i++)
        {
            navigator.HasArrived.Should().BeFalse();
            Step(context, navigator);
        }
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void VisibilityKeepsPartialAcceptedProgress(PathAlgorithm algorithm, bool reapplyPhase)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, algorithm, Budget)
            .Should().Be(NavigationGuideStatus.Success);

        for (int frame = 0; frame < 8 && !navigator.HasArrived; frame++)
        {
            context.Simulate();
            if (reapplyPhase)
                navigator.ConfigurePathRechecks(1, 0);
            navigator.PrepareFrameMotion();
            var remaining = navigator.Request.Target.Center.Y - navigator.Pose.Center.Y;
            remaining.Should().BeGreaterThan(Fixed64.Zero);
            if (frame == 0)
                remaining.Should().Be(Fixed64.Quarter);
            var clipped = FixedMath.Min(remaining, Fixed64.Eighth);
            navigator.Accept(navigator.Pose.Center + new Vector2d(Fixed64.Zero, clipped));
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(new Vector2d(0, 1));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(6, 3)]
    public void VisibilityKeepsSourceWaypointWhenAcceptedMotionLeavesTheProvedSegment(int xEighths, int zEighths)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);

        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.Quarter));
        var deflected = new Vector2d(xEighths * Fixed64.Eighth, zEighths * Fixed64.Eighth);
        navigator.Accept(deflected);
        navigator.CommitFrameMotion();
        context.LateSimulate();

        Step(context, navigator);
        Vector2d.CompareDistanceSquared(navigator.Pose.Center, Vector2d.Zero, deflected, Vector2d.Zero)
            .Should().BeNegative();
        navigator.HasArrived.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionLandingCommitsItsMapOnCoincidentLayerFaces(bool clipTarget)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var below = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0));
        var above = new GridConfiguration(new Vector3d(0, 1, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(below, out _).Should().BeTrue();
        context.World.TryAddGrid(above, out var upperIndex).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var goal = new NavigationCellAddress("above", new VoxelIndex(1, 0, 0));
        PublishLayer(context, new NavigationMapBuilder("above", above)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half).SetDefaultCell(Open).Build(), 2);
        PublishLayer(context, new NavigationMapBuilder("plane", below)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half).SetDefaultCell(Open)
            .AddTransition(new TraversalTransitionDefinition("cross", TraversalTransitionType.Jump,
                default, TraversalMedium.Solid, goal, TraversalMedium.Solid,
                TraversalCapability.Jump, Fixed64.Zero)).Build(), 3);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter, capabilities: TraversalCapability.Jump);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, PathAlgorithm.AStar, Budget, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.Success);
        Step(context, navigator);
        var action = navigator.PendingTransition!.Value;

        context.Simulate();
        var requested = action.DestinationPosition + (clipTarget ? Vector2d.Right * Fixed64.Quarter : Vector2d.Zero);
        navigator.PrepareActionFrameMotion(action, new NavigationPose2D(requested, Fixed64.Zero))
            .Should().Be(NavigationGuideStatus.Success);
        navigator.Accept(action.DestinationPosition);
        navigator.CommitFrameMotion();
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
        context.LateSimulate();
        Step(context, navigator);
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).GridIndex.Should().Be(upperIndex);
    }

    [Fact]
    public void UnauthoredCellKeepsAcceptedPhysicalMapAfterSeamHandoff()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var west = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        var east = new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(4, 1, 0));
        context.World.TryAddGrid(west, out _).Should().BeTrue();
        context.World.TryAddGrid(east, out var easternIndex).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        PublishLayer(context, new NavigationMapBuilder("plane", west)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One).SetDefaultCell(Open).Build(), 2);
        var goal = new NavigationCellAddress("east", new VoxelIndex(1, 1, 0));
        PublishLayer(context, new NavigationMapBuilder("east", east)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .AddCell(new VoxelIndex(0, 1, 0), Open).AddCell(goal.Index, Open).Build(), 3);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, PathAlgorithm.AStar, Budget).Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 16 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        var removal = new NavigationMapRemoveOperation("plane", 4, context.FrameCount + 1);
        context.Pathing.Admit(removal).Should().BeTrue();
        for (int i = 0; i < 8; i++)
            Step(context, navigator);
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);

        context.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
        navigator.Accept(new Vector2d(4, 0));
        navigator.CommitFrameMotion();
        context.LateSimulate();
        navigator.LastCommittedCell.Should().BeNull();
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).GridIndex.Should().Be(easternIndex);
        Step(context, navigator);
        navigator.LastCommittedCell.Should().BeNull();
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).GridIndex.Should().Be(easternIndex);
    }

    [Fact]
    public void AcceptedHandoffAdoptsAndReleasesAnExistingOccupancyTicket()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        Step(context, navigator);
        var initial = Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator));
        context.World.TryGetGridAndVoxel(initial, out var grid, out _).Should().BeTrue();
        grid!.TryGetVoxel(Cell(1, 0).Index, out var dest).Should().BeTrue();
        grid.TryAddVoxelOccupant(dest!, navigator).Should().BeTrue();

        context.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
        navigator.Accept(new Vector2d(1, 0));
        navigator.CommitFrameMotion();
        context.LateSimulate();
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).Should().Be(dest!.WorldIndex);
        navigator.Dispose();
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
    }
}
