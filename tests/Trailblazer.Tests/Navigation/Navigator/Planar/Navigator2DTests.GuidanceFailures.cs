using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedScheduledTransferPreservesItsResultAndReleasesTheLease(bool resetPathing)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.FlowField, Budget)
            .Should().Be(NavigationGuideStatus.Pending);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        for (int i = 0; i < 32 && cache.ActiveLeaseCount == 0; i++)
            context.Simulate();
        cache.ActiveLeaseCount.Should().Be(1);
        var store = context.Pathing.NavigationGraphStore;
        if (resetPathing)
            context.Pathing.Reset();
        else
            store.MarkSafetyPending();
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(Vector2d.Zero);
        navigator.GuidanceStatus.Should().Be(resetPathing ? NavigationGuideStatus.Stale : NavigationGuideStatus.CapacityExceeded);
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        cache.ActiveLeaseCount.Should().Be(0);
        store.ClearSafetyPending();
        if (!resetPathing)
        {
            for (int i = 0; i < 32 && !navigator.HasArrived; i++)
                Step(context, navigator);
            navigator.HasArrived.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovenScheduledOriginCannotStartMoving(bool exhausted)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        var budget = exhausted ? new NavigationWorkBudget(512, 16, 16, 0, 0, 0, 0, 0, 64, 64, 0) : Budget;
        navigator.ApplyGuidedTrekRequest(Cell(0, 0), Policy.Key, PathAlgorithm.AStar, budget)
            .Should().Be(NavigationGuideStatus.Pending);
        var moved = exhausted ? new Vector2d(Fixed64.Quarter, Fixed64.Zero) : new Vector2d(6, 0);
        navigator.PrepareFrameMotion();
        navigator.Accept(moved);
        navigator.CommitFrameMotion();
        var expected = exhausted ? NavigationGuideStatus.BudgetExceeded : NavigationGuideStatus.Stale;
        for (int i = 0; i < 32 && navigator.GuidanceStatus != expected; i++)
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            navigator.Request.Target.Center.Should().Be(moved);
            navigator.Accept(moved);
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }
        navigator.GuidanceStatus.Should().Be(expected);
        navigator.HasArrived.Should().BeFalse();
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        if (exhausted)
        {
            Step(context, navigator);
            navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.BudgetExceeded);
            context.Simulate();
            navigator.PrepareFrameMotion(Vector2d.Zero);
            navigator.Accept(Vector2d.Zero);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            context.Simulate();
            navigator.ApplyGuidedTrekRequest(Cell(0, 0), Policy.Key, PathAlgorithm.AStar, Budget)
                .Should().Be(NavigationGuideStatus.Pending);
            navigator.PrepareFrameMotion();
            navigator.Accept(moved);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            for (int i = 0; i < 16 && !navigator.HasArrived; i++)
                Step(context, navigator);
            navigator.HasArrived.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    public void VisibilityAdvancesOnlyForAcceptedLookahead(int speed, bool block)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter, speed: (Fixed64)speed);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => navigator.ConfigurePathRechecks(0, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => navigator.ConfigurePathRechecks(2, 2));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => navigator.ConfigurePathRechecks(2, -1));
        navigator.ConfigurePathRechecks(0);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        navigator.PrepareFrameMotion();
        var target = new Vector2d(Fixed64.Zero, (Fixed64)speed / (Fixed64)16);
        navigator.Request.Target.Center.Should().Be(target);
        navigator.Accept(block ? Vector2d.Zero : target);
        navigator.CommitFrameMotion();
        navigator.HasArrived.Should().Be(speed == 16);
        context.LateSimulate();
        if (block)
        {
            navigator.ConfigurePathRechecks(0);
            Step(context, navigator);
            navigator.Pose.Center.Should().Be(Vector2d.Zero);
        }
        if (navigator.HasArrived)
            Step(context, navigator);
    }

    [Fact]
    public void UnreachableFormationCellFallsBackToTheCommonGoal()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        foreach (var (x, z) in new[] { (0, 0), (0, 1), (1, 1), (2, 1), (2, 2), (3, 1), (4, 2) })
            builder.AddCell(Cell(x, z).Index, Open);
        PublishLayer(context, builder.Build(), 2);
        using var left = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        using var right = new Host(context, new Vector2d(2, 2), Fixed64.Quarter, alternateId: true);
        left.MovementGroupId.Should().Be(-1);
        left.ApplyGuidedTrekRequest(default, Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.InvalidEnd);
        left.MovementGroupId.Should().Be(-1);
        left.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        left.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        right.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Pending);
        Step(context, left);
        for (int i = 0; i < 20 && !right.HasArrived; i++)
            Step(context, right);
        right.LastCommittedCell!.Value.Address.Should().Be(Cell(3, 1));
        context.Reset();
        left.MovementGroupId.Should().Be(-1);
    }

    [Fact]
    public void ClosingValidationOwnerRejectsThePlanarConnectorAndReturnsItsLease()
    {
        using var context = CreateScene(portal: false);
        context.Guides.RequestTopDownGuide(Cell(0, 0), Cell(0, 1), Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, PathAlgorithm.AStar, Budget, out var guide)
            .Should().Be(NavigationGuideStatus.Success);
        using var lease = guide;
        var scheduler = context.Guides.Scheduler;
        scheduler.Dispose();
        var status = NavigationGuideStatus.Success;
        scheduler.RegisterPlanarConnectorValidation(new NavigationValidationInterest(), guide.RouteLease, ref status)
            .Should().BeNull();
        status.Should().Be(NavigationGuideStatus.CapacityExceeded);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void CancelledQueueEntryRetainsItsOutcomeUntilTheNextAcquisitionAttempt()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Pending);
        context.Pathing.Reset();
        navigator.PrepareFrameMotion();
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Cancelled);
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        Step(context, navigator);
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.NoMap);
    }

    [Fact]
    public void RetiringAnOldGroupCannotRemoveTheReplacementGroup()
    {
        using var context = CreateScene(portal: false);
        var navigation = context.Navigation;
        var first = navigation.GetPlanarMovementGroups(context, "plane");
        var session = new MovementGroupSession { GroupId = 1 };
        first.UpdateTarget(session, Vector3d.Forward, Vector3d.Zero, Fixed64.Quarter);
        navigation.Reset();
        var replacement = navigation.GetPlanarMovementGroups(context, "plane");
        navigation.ReleasePlanarMovementGroup("plane", first, session);
        navigation.GetPlanarMovementGroups(context, "plane").Should().BeSameAs(replacement);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ControllerStartRequiresLookupAllowanceBeforeAdmission(int lookups)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var budget = new NavigationWorkBudget(lookups, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        navigator.ApplyGuidedTrekRequest(Cell(1, 0), Policy.Key, PathAlgorithm.AStar, budget)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
    }

    [Fact]
    public void ControllerStartReusesPlaneBindingWhenDistantMapsMiss()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        for (int i = 0; i < 8; i++)
        {
            var grid = new GridConfiguration(new Vector3d(i * 10, 0, 0), new Vector3d(i * 10, 1, 0));
            context.World.TryAddGrid(grid, out _).Should().BeTrue();
            PublishLayer(context, new NavigationMapBuilder(i == 0 ? "plane" : $"remote{i}", grid)
                .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One).SetDefaultCell(Open).Build(), i + 2);
        }

        var goal = new NavigationCellAddress("remote7", new VoxelIndex(0, 1, 0));
        var budget = new NavigationWorkBudget(50, 32, 32, 64, 64, 16, 16, 16, 64, 64, 0);
        var status = context.Guides.RequestTopDownGuide(goal, goal, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, PathAlgorithm.AStar, budget, out var guide);
        using (guide)
            status.Should().Be(NavigationGuideStatus.Success);

        using var navigator = new Host(context, new Vector2d(70, 0), Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, PathAlgorithm.AStar, budget)
            .Should().Be(NavigationGuideStatus.Success);
        Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
    }

    [Fact]
    public void NearbyMembersCanShareTheSameCertifiedFormationCell()
    {
        using var context = CreateScene(portal: false);
        using var left = new Host(context, new Vector2d(0, 1), Fixed64.Quarter);
        using var right = new Host(context, new Vector2d(Fixed64.Quarter, Fixed64.One), Fixed64.Quarter, alternateId: true);
        left.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        right.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 20 && !right.HasArrived; i++)
            Step(context, right);
        right.LastCommittedCell!.Value.Address.Should().Be(Cell(3, 1));
    }

    [Fact]
    public void MissedVisibilityPhaseDoesNotMoveTheRecheckToAnotherFrame()
    {
        using var context = CreateScene(portal: false);
        var initial = new Vector2d(Fixed64.Quarter, Fixed64.Zero);
        using var navigator = new Host(context, initial, Fixed64.Quarter);
        int phase = (int)((context.FrameCount + 1) % 3);
        navigator.ConfigurePathRechecks(3, phase);
        navigator.ApplyGuidedTrekRequest(Cell(1, 0), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        Step(context, navigator);
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(Vector2d.Right);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        navigator.HasArrived.Should().BeFalse();
        Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
    }

    [Fact]
    public void VisibilityArmedThisFrameWaitsForItsNextPhase()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigurePathRechecks(1, 0);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(Vector2d.Zero);
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        Step(context, navigator);
        navigator.Pose.Center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.Quarter));
    }

    [Fact]
    public void VisibilityPeekRejectsDefaultDisposedTerminalAndStaleGuides()
    {
        using var context = CreateScene(portal: false);
        default(NavigationTopDownGuideLease).TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();
        Request(Cell(0, 0), out var terminal);
        using var terminalLease = terminal;
        terminal.TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();
        Request(Cell(1, 0), out var disposed);
        disposed.Dispose();
        disposed.TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();
        Request(Cell(1, 0), out var stale);
        using var staleLease = stale;
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 1, 11)), out _)
            .Should().BeTrue();
        stale.TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();

        void Request(NavigationCellAddress end, out NavigationTopDownGuideLease guide) =>
            context.Guides.RequestTopDownGuide(Cell(0, 0), end, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
                TraversalCapability.None, PathAlgorithm.AStar, Budget, out guide).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void FormationCenterLookupRejectsMissingOrIncompatiblePublishedCells()
    {
        using var context = CreateScene(portal: false);
        var pathing = context.Pathing;
        pathing.TryGetPlanarBinding("plane", out var binding).Should().BeTrue();
        pathing.TryGetTopDownCenter(default, binding, out _).Should().BeFalse();
        pathing.TryGetTopDownCenter(new NavigationCellAddress("missing", default), binding, out _).Should().BeFalse();
        pathing.TryGetTopDownCenter(new NavigationCellAddress("plane", new VoxelIndex(1, 0, 1)), binding, out _).Should().BeFalse();
        pathing.TryGetTopDownCenter(Cell(100, 0), binding, out _).Should().BeFalse();
        var differentMode = new NavigationPlanarBinding(NavigationMode2D.SideView, 1, Fixed64.One);
        pathing.TryGetTopDownCenter(Cell(0, 0), differentMode, out _).Should().BeFalse();
        var differentEmbedding = new NavigationPlanarBinding(NavigationMode2D.TopDown, 1, Fixed64.Zero);
        pathing.TryGetTopDownCenter(Cell(0, 0), differentEmbedding, out _).Should().BeFalse();
        pathing.NavigationGraphStore.MarkSafetyPending();
        pathing.TryGetTopDownCenter(Cell(0, 0), binding, out _).Should().BeFalse();
        pathing.NavigationGraphStore.ClearSafetyPending();
    }
}
