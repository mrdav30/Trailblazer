using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    public void ScheduledGuideRejoinsMovedOriginThroughTheFairTurnEvenAfterModeChange(PathAlgorithm algorithm,
        bool movedOntoAnchor)
    {
        using var context = CreateScene(portal: false);
        var offset = new Vector2d(Fixed64.Zero, Fixed64.Quarter);
        Vector2d initial = movedOntoAnchor ? offset : Vector2d.Zero;
        using var navigator = new Host(context, initial, Fixed64.Quarter);
        if (movedOntoAnchor)
            navigator.ConfigurePathRechecks(1, 0);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ApplyGuidedTrekRequest(Cell(4, 0), Policy.Key, algorithm, Budget).Should().Be(NavigationGuideStatus.Pending);
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(initial);
        Vector2d moved = movedOntoAnchor ? Vector2d.Zero : offset;
        navigator.Accept(moved);
        navigator.CommitFrameMotion();
        navigator.Velocity.Should().Be((moved - initial) * (Fixed64)16);
        var scheduler = context.Guides.Scheduler;
        bool switched = false;
        bool rejoined = false;
        for (int i = 0; i < 128 && !navigator.HasArrived; i++)
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            if (!switched && scheduler.RetainedValidationInterestCount > 0)
                navigator.Request.Target.Center.Should().Be(moved);
            navigator.Accept(navigator.Request.Target.Center);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            if (!switched && scheduler.RetainedValidationInterestCount > 0)
            {
                navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
                scheduler.RetainedValidationInterestCount.Should().Be(1);
                switched = true;
            }
            if (switched && navigator.Pose.Center == Vector2d.Zero)
                rejoined = true;
        }
        switched.Should().BeTrue();
        rejoined.Should().BeTrue();
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(Cell(4, 0));
        scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Fact]
    public void ChangingAcquisitionModeCancelsQueuedInterestAndPreservesIntent()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Pending);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        for (int i = 0; i < 10 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(Cell(0, 1));
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void GuideCommitsOccupancyAcrossAMapSeam(PathAlgorithm algorithm, bool removeOriginMap)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var west = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0));
        var east = new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(3, 1, 0));
        context.World.TryAddGrid(west, out _).Should().BeTrue();
        context.World.TryAddGrid(east, out var easternIndex).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        PublishLayer(context, new NavigationMapBuilder("plane", west).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .SetDefaultCell(Open).Build(), 2);
        PublishLayer(context, new NavigationMapBuilder("east", east).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .SetDefaultCell(Open).Build(), 3);
        var destination = new NavigationCellAddress("east", new VoxelIndex(1, 1, 0));
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var entries = new List<string>();
        navigator.CommittedCellChanged += state => { if (state.HasValue) entries.Add(state.Value.Address.MapId); };
        navigator.ApplyGuidedTrekRequest(destination, Policy.Key, algorithm, Budget).Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 16 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        navigator.GuideCost.Should().Be((Fixed64)3);
        navigator.LastCommittedCell!.Value.Address.Should().Be(destination);
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).GridIndex.Should().Be(easternIndex);
        entries.Should().Equal("plane", "plane", "east", "east");

        if (removeOriginMap)
        {
            var removal = new NavigationMapRemoveOperation("plane", 4, context.FrameCount + 1);
            context.Pathing.Admit(removal).Should().BeTrue();
            for (int i = 0; i < 8; i++)
                Step(context, navigator);
            removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            navigator.LastCommittedCell!.Value.Address.Should().Be(destination);
            PublishLayer(context, new NavigationMapBuilder("plane", west)
                .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 5, bakeVersion: 2);
            Step(context, navigator);
            navigator.LastCommittedCell!.Value.Address.Should().Be(destination);
            var eastOrigin = new NavigationCellAddress("east", new VoxelIndex(0, 1, 0));
            navigator.ApplyGuidedTrekRequest(eastOrigin, Policy.Key, algorithm, Budget).Should().Be(NavigationGuideStatus.Success);
            for (int i = 0; i < 8 && !navigator.HasArrived; i++)
                Step(context, navigator);
            navigator.HasArrived.Should().BeTrue();
            navigator.LastCommittedCell!.Value.Address.Should().Be(eastOrigin);
            return;
        }

        if (algorithm == PathAlgorithm.FlowField)
        {
            var removal = new NavigationMapRemoveOperation("east", 4, context.FrameCount + 1);
            context.Pathing.Admit(removal).Should().BeTrue();
            for (int i = 0; i < 8; i++)
                context.Simulate();
            removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            navigator.ApplyGuidedTrekRequest(new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0)), Policy.Key,
                algorithm, Budget).Should().Be(NavigationGuideStatus.InvalidStart);
            PublishLayer(context, new NavigationMapBuilder("east", east)
                .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One).SetDefaultCell(Open).Build(), 5, bakeVersion: 2);
        }

        navigator.ApplyGuidedTrekRequest(new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0)), Policy.Key,
            algorithm, Budget).Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 16 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void MovementGroupUsesDistinctCertifiedFormationCellsAndReleasesMembership()
    {
        using var context = CreateScene(portal: false);
        using var left = new Host(context, new Vector2d(0, 1), Fixed64.Quarter);
        using var right = new Host(context, new Vector2d(2, 1), Fixed64.Quarter, alternateId: true);
        left.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        right.ApplyGuidedTrekRequest(Cell(3, 1), Policy.Key, PathAlgorithm.FlowField, Budget, groupId: 9)
            .Should().Be(NavigationGuideStatus.Success);
        left.MovementGroupId.Should().Be(9);
        right.MovementGroupId.Should().Be(9);
        for (int i = 0; i < 20 && (!left.HasArrived || !right.HasArrived); i++)
        {
            context.Simulate();
            left.PrepareFrameMotion();
            right.PrepareFrameMotion();
            left.Accept(left.Request.Target.Center);
            right.Accept(right.Request.Target.Center);
            left.CommitFrameMotion();
            right.CommitFrameMotion();
            context.LateSimulate();
        }
        left.LastCommittedCell!.Value.Address.Should().Be(Cell(2, 1));
        right.LastCommittedCell!.Value.Address.Should().Be(Cell(4, 1));
        left.MovementGroupId.Should().Be(-1);
        right.MovementGroupId.Should().Be(-1);
    }

    [Fact]
    public void GuidedOccupancyKeepsItsMapAtCoincidentLayerFaces()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var below = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0));
        var above = new GridConfiguration(new Vector3d(0, 1, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(below, out var lowerIndex).Should().BeTrue();
        context.World.TryAddGrid(above, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        PublishLayer(context, new NavigationMapBuilder("above", above)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half).SetDefaultCell(Open).Build(), 2);
        PublishLayer(context, new NavigationMapBuilder("plane", below)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half).SetDefaultCell(Open).Build(), 3);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var goal = new NavigationCellAddress("plane", new VoxelIndex(1, 0, 0));
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, PathAlgorithm.FlowField, Budget).Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 8 && !navigator.HasArrived; i++)
        {
            Step(context, navigator);
            Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator)).GridIndex.Should().Be(lowerIndex);
        }
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
    }

    [Fact]
    public void VisibilityPhaseStartsTheNextOrdinaryLegWithoutBypassingAStaleGuide()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        int phase = (int)((context.FrameCount + 1) % 2);
        navigator.ConfigurePathRechecks(2, phase);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.Quarter));
        navigator.Accept(navigator.Request.Target.Center);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 1, 11)), out _)
            .Should().BeTrue();
        navigator.CommitFrameMotion();
        navigator.Pose.Center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.Quarter));
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        navigator.HasArrived.Should().BeFalse();
        context.LateSimulate();
        for (int i = 0; i < 12 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false, false, false)]
    [InlineData(PathAlgorithm.FlowField, false, false, false)]
    [InlineData(PathAlgorithm.AStar, true, false, false)]
    [InlineData(PathAlgorithm.FlowField, true, false, false)]
    [InlineData(PathAlgorithm.AStar, false, true, false)]
    [InlineData(PathAlgorithm.AStar, false, false, true)]
    public void HeldActionCompletesOnceAfterTheHostAcceptsItsExactLanding(PathAlgorithm algorithm, bool scheduled,
        bool terminalLanding, bool cancel)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var toll = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Half, Fixed64.One, Fixed64.Two);
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .AddCell(Cell(0, 0).Index, Open).AddCell(Cell(1, 0).Index, Open)
            .AddCell(Cell(3, 0).Index, toll).AddCell(Cell(4, 0).Index, Open)
            .AddTransition(new TraversalTransitionDefinition("gap", TraversalTransitionType.Jump,
                Cell(1, 0).Index, TraversalMedium.Solid, Cell(3, 0), TraversalMedium.Solid,
                TraversalCapability.Jump, (Fixed64)3));
        PublishLayer(context, builder.Build(), 2);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter, capabilities: TraversalCapability.Jump);
        if (scheduled)
            navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        var goal = Cell(terminalLanding ? 3 : 4, 0);
        if (!scheduled && !terminalLanding && !cancel)
        {
            context.Guides.RequestTopDownGuide(Cell(0, 0), goal, Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
                TraversalCapability.Jump, algorithm, Budget, out var preview, allowTransitions: true)
                .Should().Be(NavigationGuideStatus.Success);
            using var previewLease = preview;
            preview.TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();
            preview.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            preview.TryGetNextOrdinaryStep(out _, out _).Should().BeFalse();
        }
        navigator.ApplyGuidedTrekRequest(goal, Policy.Key, algorithm, Budget, allowTransitions: true)
            .Should().Be(scheduled ? NavigationGuideStatus.Pending : NavigationGuideStatus.Success);
        for (int i = 0; i < 128 && !navigator.PendingTransition.HasValue; i++)
            Step(context, navigator);
        var action = navigator.PendingTransition!.Value;
        navigator.Pose.Center.Should().Be(new Vector2d(1, 0));
        navigator.GuideCost.Should().Be((Fixed64)(terminalLanding ? 4 : 5) + Fixed64.Half);
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
        navigator.CompletePendingTransition(default).Should().Be(NavigationGuideStatus.Stale);
        Step(context, navigator);
        navigator.Pose.Center.Should().Be(action.SourcePosition);
        if (cancel)
        {
            navigator.CancelGuidance();
            navigator.PrepareActionFrameMotion(action, default).Should().Be(NavigationGuideStatus.Stale);
            navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
            return;
        }

        if (!scheduled && !terminalLanding)
        {
            var store = context.Pathing.NavigationGraphStore;
            store.MarkSafetyPending();
            navigator.PendingTransition.Should().BeNull();
            navigator.PrepareActionFrameMotion(action, default).Should().Be(NavigationGuideStatus.CapacityExceeded);
            navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.CapacityExceeded);
            store.ClearSafetyPending();

            context.Simulate();
            navigator.PrepareFrameMotion();
            navigator.Accept(action.DestinationPosition);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
            context.Simulate();
            navigator.PrepareActionFrameMotion(action, default).Should().Be(NavigationGuideStatus.InvalidStart);
            navigator.PrepareFrameMotion();
            navigator.Accept(action.SourcePosition);
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }

        for (int i = 1; i <= 8; i++)
        {
            context.Simulate();
            var target = new NavigationPose2D(new Vector2d(Fixed64.One + i * Fixed64.Quarter, Fixed64.Zero), Fixed64.Zero);
            navigator.PrepareActionFrameMotion(action, target).Should().Be(NavigationGuideStatus.Success);
            navigator.Accept(target.Center);
            navigator.CommitFrameMotion();
            context.LateSimulate();
            if (i < 8)
                navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
        }
        var landingStore = context.Pathing.NavigationGraphStore;
        landingStore.MarkSafetyPending();
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.CapacityExceeded);
        landingStore.ClearSafetyPending();
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
        navigator.HasArrived.Should().Be(terminalLanding);
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
        navigator.PendingTransition.Should().BeNull();
        for (int i = 0; i < 8 && !navigator.HasArrived; i++)
            Step(context, navigator);
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
    }

    private static void PublishLayer(TrailblazerWorldContext context, NavigationMap map, long sequence, long bakeVersion = 1)
    {
        var publication = new NavigationMapCommitOperation(new PreparedNavigationMap(map, bakeVersion), OverlayReplacementPolicy.Clear,
            sequence, context.FrameCount + 1);
        context.Pathing.Admit(publication).Should().BeTrue();
        for (int i = 0; i < 8; i++)
        {
            context.Simulate();
            context.LateSimulate();
        }
        publication.Receipt.Status.Should().Be(NavigationOperationStatus.Applied, "publication rejection was {0}", publication.Receipt.Rejection);
    }

    private static void Step(TrailblazerWorldContext context, Host navigator)
    {
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        context.LateSimulate();
    }
}
