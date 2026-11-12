using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledSideViewReturnsToTheExactFirstAnchorAfterCarrierMotion(PathAlgorithm algorithm)
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context, settings: new NavigationMotorSettings2D(Fixed64.Two,
            Fixed64.Zero, (Fixed64)16, Fixed64.Zero));
        var carrier = new NavigationCarrier2D(7, 11, default, Vector2d.Zero);
        host.Sample(new NavigationSupport2D(Ground.Point, Vector2d.Forward, Fixed64.One, carrier));
        host.RefreshSupport();
        host.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        ApplySideView(host, algorithm: algorithm).Should().Be(NavigationGuideStatus.Pending);

        var moved = Vector2d.Right * Fixed64.Quarter;
        host.CarrierPose = new NavigationPose2D(moved, Fixed64.Zero);
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Should().Be(moved);
        host.Accept(moved, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();

        WaitForSideViewConnector(context, host);
        var scheduler = context.Guides.Scheduler;
        host.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);

        context.Simulate();
        host.PrepareFrameMotion();
        var firstTarget = Vector2d.Right * Fixed64.FromFraction(1, 8);
        host.Request.Target.Center.Should().Be(firstTarget);
        host.Accept((moved + firstTarget) * Fixed64.Half, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        scheduler.RetainedValidationInterestCount.Should().Be(1);
        Step(context, host, default);
        host.Pose.Center.Should().Be(Vector2d.Right * Fixed64.FromFraction(1, 16));
        Step(context, host, default);
        host.Pose.Center.Should().Be(Vector2d.Zero);
        scheduler.RetainedValidationInterestCount.Should().Be(0);
        host.HasArrived.Should().BeFalse();

        for (int frame = 0; frame < 8; frame++)
            Step(context, host, default);
        host.Pose.Center.Should().Be(Vector2d.Right);
        host.HasArrived.Should().BeTrue();
        scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(-9, 0)]
    [InlineData(9, 0)]
    [InlineData(1, 1)]
    public void SideViewConnectorRejectsGroundedOriginsOutsideTheFiniteSourceTrack(int x, int height)
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        StartMovedSideViewConnector(context, host, Vector2d.Zero,
            new Vector2d(Fixed64.FromFraction(x, 16), Fixed64.FromFraction(height, 16)));
        var moved = host.Pose.Center;
        Step(context, host, default);
        host.Pose.Center.Should().Be(moved);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidStart);
        host.HasArrived.Should().BeFalse();
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SideViewConnectorReprovesRoundedSlopeMotionAfterSnapshotCapacityReturns()
    {
        using var context = GuidedStepScene(slope: true);
        context.Pathing.ResolveSideViewAnchor(GuidedAddress(0), Profile, SideViewPolicy, SideViewBudget, out var anchor)
            .Should().Be(NavigationGuideStatus.Success);
        var moved = anchor + new Vector2d(Fixed64.Quarter, Fixed64.FromFraction(1, 16));
        using var host = new Host(context, pose: new NavigationPose2D(anchor, Fixed64.Zero),
            settings: new NavigationMotorSettings2D(Fixed64.Two, Fixed64.Zero, (Fixed64)16, Fixed64.Zero));
        StartMovedSideViewConnector(context, host, anchor, moved);
        var leases = new NavigationWorldGraphLease?[context.Settings.MaxConcurrentSnapshotLeases];
        void ExhaustCapacity()
        {
            for (int i = 0; i < leases.Length; i++)
                leases[i] = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
        }
        void ReleaseCapacity()
        {
            foreach (var lease in leases)
                lease?.Dispose();
        }

        try
        {
            ExhaustCapacity();
            Step(context, host, default);
            host.Pose.Center.Should().Be(moved);
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(1);
        }
        finally
        {
            ReleaseCapacity();
        }
        context.Simulate();
        host.PrepareFrameMotion();
        host.Accept(host.Request.Target.Center, Ground);
        host.OnRead = ExhaustCapacity;
        try
        {
            host.CommitFrameMotion();
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            host.OnRead = null;
            ReleaseCapacity();
        }
        context.LateSimulate();
        host.Pose.Center.X.Should().BeLessThan(moved.X);
        Vector2d.OrientationSign(moved, anchor, host.Pose.Center).Should().Be(-1);
        var clipped = host.Pose.Center;
        try
        {
            ExhaustCapacity();
            Step(context, host, default);
            host.Pose.Center.Should().Be(clipped);
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            ReleaseCapacity();
        }
        for (int i = 0; i < 8 && context.Guides.Scheduler.RetainedValidationInterestCount != 0; i++)
            Step(context, host, default);
        host.Pose.Center.Should().Be(anchor);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Success);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Fact]
    public void SideViewConnectorInvalidationCommitsPoseAndReleasesItsValidationTurn()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        StartMovedSideViewConnector(context, host, Vector2d.Zero, Vector2d.Right * Fixed64.FromFraction(1, 8));
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Should().Be(Vector2d.Zero);
        context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
            .Should().BeTrue();
        host.Accept(Vector2d.Zero, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.HasArrived.Should().BeFalse();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SideViewConnectorRejectsAnUnrepresentableDistance()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        context.SetFrameRate(1);
        Fixed64 coordinate = (Fixed64)2_000_000_000;
        var moved = new Vector2d(coordinate, coordinate);
        MoveWhileWaitingForSideViewGuide(context, host, Vector2d.Zero, moved);
        for (int frame = 0; frame < 128 && host.GuidanceStatus == NavigationGuideStatus.Pending; frame++)
            Step(context, host, default);
        host.Pose.Center.Should().Be(moved);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.CostOverflow);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Fact]
    public void SideViewConnectorProofChecksAnchorIdentityAndSharesTheSweepBudget()
    {
        using var context = GuidedStepScene(slope: true);
        var source = GuidedAddress(0);
        var plane = new NavigationPlanarBinding(NavigationMode2D.SideView, 1, Fixed64.One);
        context.Pathing.ResolveSideViewAnchor(source, Profile, SideViewPolicy, SideViewBudget, out var anchor)
            .Should().Be(NavigationGuideStatus.Success);
        var moved = anchor + new Vector2d(Fixed64.Quarter, Fixed64.FromFraction(1, 16));
        var rounded = moved - Vector2d.Right * Fixed64.MinIncrement;
        NavigationWorkBudget Budget(int edges, int intervals) => new(4096, 0, 0, edges, 0, 0, 0, 0, 0, intervals, 0);
        NavigationGuideStatus Prove(NavigationCellAddress address, Vector2d origin, Vector2d endpoint,
            Vector2d? actual, NavigationWorkBudget budget) => context.Pathing.ValidateSideViewControllerConnector(address,
                "side", plane, "side", origin, moved, endpoint, actual, Profile, SideViewPolicy, budget);

        Prove(source, moved, anchor, rounded, Budget(0, 64)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Prove(GuidedAddress(2), moved, anchor, null, SideViewBudget).Should().Be(NavigationGuideStatus.InvalidStart);
        Prove(source, moved, moved, null, SideViewBudget).Should().Be(NavigationGuideStatus.Stale);
        Prove(source, new Vector2d(Fixed64.MaxValue, Fixed64.Zero), anchor, null, SideViewBudget)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        int covered = 0;
        while (covered < 64 && Prove(source, moved, anchor, null, Budget(1, covered)) == NavigationGuideStatus.BudgetExceeded)
            covered++;
        covered.Should().BeInRange(1, 63);
        Prove(source, moved, anchor, null, Budget(1, covered)).Should().Be(NavigationGuideStatus.Success);
        Prove(source, moved, anchor, rounded, Budget(1, covered)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Prove(source, moved, anchor, rounded, Budget(1, covered * 2)).Should().Be(NavigationGuideStatus.Success);

        var leases = new NavigationWorldGraphLease?[context.Settings.MaxConcurrentSnapshotLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++)
                leases[i] = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
            Prove(source, moved, anchor, rounded, SideViewBudget).Should().Be(NavigationGuideStatus.Pending);
        }
        finally
        {
            foreach (var lease in leases)
                lease?.Dispose();
        }
    }

    private static void StartMovedSideViewConnector(TrailblazerWorldContext context, Host host,
        Vector2d anchor, Vector2d moved)
    {
        MoveWhileWaitingForSideViewGuide(context, host, anchor, moved);
        WaitForSideViewConnector(context, host);
    }

    private static void MoveWhileWaitingForSideViewGuide(TrailblazerWorldContext context, Host host,
        Vector2d anchor, Vector2d moved)
    {
        host.Sample(Ground);
        host.RefreshSupport();
        host.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        ApplySideView(host).Should().Be(NavigationGuideStatus.Pending);
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Should().Be(anchor);
        host.Accept(moved, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
    }

    private static void WaitForSideViewConnector(TrailblazerWorldContext context, Host host)
    {
        var moved = host.Pose.Center;
        var scheduler = context.Guides.Scheduler;
        for (int frame = 0; frame < 128 && host.GuidanceStatus == NavigationGuideStatus.Pending
            && scheduler.RetainedValidationInterestCount == 0; frame++)
        {
            Step(context, host, default);
            host.Pose.Center.Should().Be(moved);
        }
        scheduler.RetainedValidationInterestCount.Should().Be(1);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
    }
}
