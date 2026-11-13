using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("complete")]
    [InlineData("replace")]
    public void StaleHeldActionIsRetiredBeforeTheNextOrdinaryFrame(string operation)
    {
        var floor = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var source = GuidedAddress(0);
        var destination = GuidedAddress(1);
        var builder = new NavigationMapBuilder("side", GuidedGrid)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .AddCell(source.Index, floor).AddCell(destination.Index, floor)
            .AddPlanarSupport(source.Index, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, -Fixed64.Half))
            .AddPlanarSupport(destination.Index, new(Fixed64.Half, -Fixed64.Half), new(Fixed64.One + Fixed64.Half, -Fixed64.Half))
            .AddTransition(new TraversalTransitionDefinition("jump", TraversalTransitionType.Jump, source.Index,
                TraversalMedium.Solid, destination, TraversalMedium.Solid, TraversalCapability.Jump, Fixed64.Zero));
        using var context = GuidedScene(builder.Build());
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        var budget = new NavigationWorkBudget(4096, 32, 64, 128, 0, 16, 0, 0, 0, 4096, 0);
        host.ApplySideViewGuidedTrekRequest(source, destination, SideViewPolicy, PathAlgorithm.AStar, budget,
            allowTransitions: true).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        host.PendingTransition.HasValue.Should().BeTrue();
        var action = host.PendingTransition!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(1);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
            .Should().BeTrue();

        host.PendingTransition.Should().BeNull();
        host.PrepareActionFrameMotion(default, host.Pose).Should().Be(NavigationGuideStatus.Stale);
        host.CompletePendingTransition(default).Should().Be(NavigationGuideStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(1, "a mismatched token cannot retire the held action");

        if (operation == "prepare")
            host.PrepareActionFrameMotion(action, host.Pose).Should().Be(NavigationGuideStatus.Stale);
        else if (operation == "complete")
            host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
        else
            host.TryReplaceNavigationProfile(host.NavigationProfile).Should().Be(NavigationProfileReplacementStatus.Unchanged);

        cache.ActiveLeaseCount.Should().Be(0);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        host.TryReplaceNavigationProfile(host.NavigationProfile).Should().Be(NavigationProfileReplacementStatus.Unchanged);
    }

    [Theory]
    [InlineData(TraversalTransitionType.Jump, PathAlgorithm.AStar, false, false)]
    [InlineData(TraversalTransitionType.Jump, PathAlgorithm.FlowField, true, false)]
    [InlineData(TraversalTransitionType.Climb, PathAlgorithm.AStar, true, false)]
    [InlineData(TraversalTransitionType.Custom, PathAlgorithm.FlowField, false, false)]
    [InlineData(TraversalTransitionType.Climb, PathAlgorithm.FlowField, true, true)]
    public void AuthoredSideViewActionHoldsUntilHostAcceptsSupportedLanding(TraversalTransitionType type,
        PathAlgorithm algorithm, bool scheduled, bool invalidate)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var grid = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(4, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var policy = new NavigationAreaPolicy(SideViewPolicy, new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy, 1, 1)).Should().BeTrue();
        bool jump = type == TraversalTransitionType.Jump;
        bool climb = type == TraversalTransitionType.Climb;
        bool drop = type == TraversalTransitionType.Custom;
        string id = drop ? "drop" : climb ? "ladder" : "jump";
        var capability = drop ? TraversalCapability.None : climb ? TraversalCapability.Climb : TraversalCapability.Jump;
        var fromHeight = drop ? Fixed64.Half + Fixed64.Quarter : -Fixed64.Half;
        var toHeight = climb ? Fixed64.Half + Fixed64.Quarter : -Fixed64.Half;
        int landingX = jump ? 3 : 2;
        int fromRow = drop ? 2 : 1;
        int toRow = climb ? 2 : 1;
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var toll = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Half, Fixed64.One, Fixed64.Two);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var builder = new NavigationMapBuilder("side", grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(air);
        var start = AddFloor(0, fromRow, fromHeight, solid);
        var departure = AddFloor(1, fromRow, fromHeight, solid);
        var landing = AddFloor(landingX, toRow, toHeight, toll);
        var goal = AddFloor(landingX + 1, toRow, toHeight, solid);
        builder.AddTransition(new TraversalTransitionDefinition(id, type, departure.Index, TraversalMedium.Solid, landing,
            TraversalMedium.Solid, capability, (Fixed64)3));
        PublishGuidedMap(context, builder.Build(), 2);

        var profile = new NavigationAgentProfile2D(NavigationMode2D.SideView, Fixed64.Quarter, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Half, TraversalMedia.Solid, capability);
        var budget = new NavigationWorkBudget(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0);
        using var host = new Host(context, profile,
            pose: new NavigationPose2D(new(Fixed64.Zero, fromHeight + profile.Radius), Fixed64.Zero));
        host.Sample(FloorSupport(host.Pose.Center, fromHeight));
        host.RefreshSupport();
        if (scheduled)
            host.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        host.ApplySideViewGuidedTrekRequest(start, goal, SideViewPolicy, algorithm, budget, allowTransitions: true)
            .Should().Be(scheduled ? NavigationGuideStatus.Pending : NavigationGuideStatus.Success);
        for (int i = 0; i < 128 && !host.PendingTransition.HasValue; i++)
            AdvanceOnFloor(fromHeight);
        host.PendingTransition.HasValue.Should().BeTrue();
        var action = host.PendingTransition!.Value;
        action.Id.Should().Be(id);
        action.Type.Should().Be(type);
        action.SourceAddress.Should().Be(departure);
        action.DestinationAddress.Should().Be(landing);
        host.GuideCost.Should().Be(Fixed64.FromFraction(11, 2));
        host.Pose.Center.Should().Be(action.SourcePosition);
        host.TryReplaceNavigationProfile(host.NavigationProfile).Should().Be(NavigationProfileReplacementStatus.ActionInProgress);
        host.PendingTransition!.Value.Matches(action).Should().BeTrue();
        host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
        AdvanceOnFloor(fromHeight);
        host.Pose.Center.Should().Be(action.SourcePosition);

        bool checkLandingSupport = jump && !scheduled;
        if (checkLandingSupport)
        {
            var priorAction = action;
            var payload = SerializationUtility.SerializeRecord(host, false);
            SerializationUtility.PopulateRecord(host, payload, false);
            host.PendingTransition.Should().BeNull();
            host.PrepareActionFrameMotion(priorAction, new NavigationPose2D(priorAction.SourcePosition, Fixed64.Zero))
                .Should().Be(NavigationGuideStatus.Stale);
            host.CompletePendingTransition(priorAction).Should().Be(NavigationGuideStatus.Stale);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
            host.Sample(FloorSupport(host.Pose.Center, fromHeight));
            host.RefreshSupport();
            for (int i = 0; i < 8 && !host.PendingTransition.HasValue; i++)
                AdvanceOnFloor(fromHeight);
            host.PendingTransition.HasValue.Should().BeTrue();
            action = host.PendingTransition!.Value;
            action.Matches(priorAction).Should().BeFalse();

            host.Sample(null);
            host.RefreshSupport();
            host.PrepareActionFrameMotion(action, new NavigationPose2D(action.SourcePosition, Fixed64.Zero))
                .Should().Be(NavigationGuideStatus.InvalidStart);
            host.Sample(FloorSupport(host.Pose.Center, fromHeight));
            host.RefreshSupport();
            Action rotate = () => host.PrepareActionFrameMotion(action,
                new NavigationPose2D(action.SourcePosition, Fixed64.Quarter));
            rotate.Should().Throw<ArgumentException>();
        }

        if (jump)
        {
            AcceptAction(new Vector2d(Fixed64.One, Fixed64.Zero));
            for (int i = 1; i <= 5; i++)
                AcceptAction(new Vector2d(Fixed64.One + i * Fixed64.Quarter, Fixed64.Zero));
            for (int i = 0; i <= 3; i++)
                AcceptAction(new Vector2d(Fixed64.Two + (i + 1) * Fixed64.Quarter, -Fixed64.Quarter));
        }
        else if (climb)
        {
            for (int i = 1; i <= 5; i++)
                AcceptAction(action.SourcePosition + Vector2d.Forward * (i * Fixed64.Quarter));
            for (int i = 1; i <= 4; i++)
                AcceptAction(new Vector2d(Fixed64.One + i * Fixed64.Quarter, action.DestinationPosition.Y));
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                AcceptAction(action.SourcePosition + Vector2d.Right * (i * Fixed64.Quarter));
            for (int i = 1; i <= 5; i++)
                AcceptAction(new Vector2d(action.DestinationPosition.X, action.SourcePosition.Y - i * Fixed64.Quarter));
        }
        host.Pose.Center.Should().Be(action.DestinationPosition);
        if (checkLandingSupport)
        {
            host.IsGrounded.Should().BeFalse();
            host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
            checkLandingSupport = false;
            AcceptAction(action.DestinationPosition);
            host.Sample(null);
            host.RefreshSupport();
            host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
            host.Sample(FloorSupport(host.Pose.Center, toHeight));
            host.RefreshSupport();
            AcceptAction(action.DestinationPosition + Vector2d.Right * Fixed64.Quarter, supported: true);
            AcceptAction(action.DestinationPosition);
        }
        if (invalidate)
        {
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
            host.PrepareActionFrameMotion(action, new NavigationPose2D(action.DestinationPosition, Fixed64.Zero))
                .Should().Be(NavigationGuideStatus.Stale);
        }
        else
            host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
        host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
        host.PendingTransition.Should().BeNull();
        for (int i = 0; i < (invalidate ? 128 : 8) && !host.HasArrived; i++)
            AdvanceOnFloor(toHeight);
        host.HasArrived.Should().BeTrue();
        if (invalidate)
            host.GuideCost.Should().Be(Fixed64.One);
        host.LastCommittedCell!.Value.Address.Should().Be(goal);
        host.LastCommittedCell.Value.Medium.Should().Be(TraversalMedium.Solid);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        NavigationCellAddress AddFloor(int x, int row, Fixed64 height, NavigationCell cell)
        {
            var index = new VoxelIndex(x, 1, row);
            builder.AddCell(index, cell).AddPlanarSupport(index, new((Fixed64)x - Fixed64.Half, height),
                new((Fixed64)x + Fixed64.Half, height));
            return new NavigationCellAddress("side", index);
        }

        void AdvanceOnFloor(Fixed64 height)
        {
            context.Simulate();
            host.PrepareFrameMotion();
            var target = host.Request.Target.Center;
            host.Accept(target, FloorSupport(target, height));
            host.CommitFrameMotion();
            context.LateSimulate();
        }

        void AcceptAction(Vector2d target, bool supported = false)
        {
            context.Simulate();
            host.PrepareActionFrameMotion(action, new NavigationPose2D(target, Fixed64.Zero)).Should().Be(NavigationGuideStatus.Success);
            host.Request.Target.Center.Should().Be(target);
            bool landingAccepted = target == action.DestinationPosition && !checkLandingSupport;
            if (invalidate && landingAccepted)
                context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
                    .Should().BeTrue();
            host.Accept(target, landingAccepted || supported ? FloorSupport(target, toHeight) : null);
            host.CommitFrameMotion();
            context.LateSimulate();
            host.Pose.Center.Should().Be(target);
            if (!landingAccepted)
                host.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
        }
    }

    private static NavigationSupport2D FloorSupport(Vector2d center, Fixed64 height) =>
        new(new Vector2d(center.X, height), Vector2d.Forward, Fixed64.One);
}
