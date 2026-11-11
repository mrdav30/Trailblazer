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
    private static readonly NavigationAreaPolicyKey SideViewPolicy = new("route", 1);
    private static readonly NavigationWorkBudget SideViewBudget = new(4096, 2, 64, 128, 0, 0, 0, 0, 0, 4096, 0);
    private static readonly Vector2d HighAnchor = new(Fixed64.One, Fixed64.Quarter);
    private static readonly Vector2d Bend = new(Fixed64.Zero, Fixed64.Quarter);

    [Theory]
    [InlineData(PathAlgorithm.AStar, NavigationAcquisitionMode.Immediate, false)]
    [InlineData(PathAlgorithm.FlowField, NavigationAcquisitionMode.Immediate, true)]
    [InlineData(PathAlgorithm.AStar, NavigationAcquisitionMode.Scheduled, true)]
    [InlineData(PathAlgorithm.FlowField, NavigationAcquisitionMode.Scheduled, false)]
    public void GuidedStepHonorsItsStagesAndClippedAcceptedMotion(PathAlgorithm algorithm,
        NavigationAcquisitionMode mode, bool descending)
    {
        using var context = GuidedStepScene();
        var start = descending ? HighAnchor : Vector2d.Zero;
        var end = descending ? Vector2d.Zero : HighAnchor;
        var settings = new NavigationMotorSettings2D(Fixed64.Two, Fixed64.Zero, (Fixed64)16, (Fixed64)4);
        using var host = new Host(context, settings: settings, pose: new NavigationPose2D(start, Fixed64.Zero));
        host.Sample(SupportAt(start));
        host.RefreshSupport();
        host.ConfigureAcquisitionMode(mode);
        host.ConfigurePathRechecks(1, 0);
        ApplySideView(host, descending, algorithm).Should().Be(mode == NavigationAcquisitionMode.Scheduled
            ? NavigationGuideStatus.Pending : NavigationGuideStatus.Success);
        bool clipped = false;
        bool acceptedBend = false;
        for (int frame = 0; frame < 128 && !host.HasArrived; frame++)
        {
            context.Simulate();
            Vector2d before = host.Pose.Center;
            host.PrepareFrameMotion();
            var target = host.Request.Target.Center;
            host.Pose.Center.Should().Be(before);
            if (!acceptedBend)
            {
                if (descending)
                    target.Y.Should().Be(Fixed64.Quarter);
                else
                    target.X.Should().Be(Fixed64.Zero);
            }
            else if (descending)
                target.X.Should().Be(Fixed64.Zero);
            else
                target.Y.Should().Be(Fixed64.Quarter);
            (target - before).Magnitude.Should().BeLessThanOrEqualTo(Fixed64.FromFraction(1, 8));
            if (!clipped && target != before)
            {
                target = (before + target) * Fixed64.Half;
                clipped = true;
            }
            host.Accept(target, SupportAt(target));
            host.CommitFrameMotion();
            context.LateSimulate();
            acceptedBend |= target == Bend;
        }
        clipped.Should().BeTrue();
        acceptedBend.Should().BeTrue();
        host.Pose.Center.Should().Be(end);
        host.IsGrounded.Should().BeTrue();
        host.HasArrived.Should().BeTrue();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Success);
        host.GuideCost.Should().Be(Fixed64.One + Fixed64.Quarter);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SideViewCommitRequiresLiveLandingAndRejectsOffStageAcceptedMotion(bool deflect)
    {
        using var context = GuidedStepScene();
        using var host = new Host(context);
        host.Sample(SupportAt(Vector2d.Zero));
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Vector2d accepted = default;
        for (int i = 0; i < 16; i++)
        {
            context.Simulate();
            host.PrepareFrameMotion();
            Vector2d target = host.Request.Target.Center;
            bool fail = deflect ? target == Bend : target == HighAnchor;
            accepted = fail && deflect ? new Vector2d(Fixed64.MinIncrement, target.Y) : target;
            host.Accept(accepted, fail ? null : SupportAt(accepted));
            host.CommitFrameMotion();
            context.LateSimulate();
            if (fail)
                break;
        }
        host.Pose.Center.Should().Be(accepted);
        host.HasArrived.Should().BeFalse();
        host.GuidanceStatus.Should().Be(deflect ? NavigationGuideStatus.InvalidStart : NavigationGuideStatus.InvalidEnd);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Y.Should().Be(accepted.Y - Fixed64.FromFraction(1, 16));
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    public void SideViewGuidanceLetsCarrierMoveAndRejectsTheOldOrigin(NavigationAcquisitionMode mode)
    {
        using var context = GuidedStepScene();
        using var host = new Host(context);
        var carrier = new NavigationCarrier2D(7, 11, new NavigationPose2D(Vector2d.Zero, Fixed64.Pi / 3),
            new Vector2d(3, 2));
        host.Sample(new NavigationSupport2D(SupportAt(Vector2d.Zero)!.Value.Point, Vector2d.Forward, Fixed64.One, carrier));
        host.RefreshSupport();
        host.ConfigureAcquisitionMode(mode);
        ApplySideView(host).Should().Be(mode == NavigationAcquisitionMode.Immediate
            ? NavigationGuideStatus.Success : NavigationGuideStatus.Pending);
        var moved = new Vector2d(Fixed64.FromFraction(1, 8), Fixed64.Zero);
        host.CarrierPose = new NavigationPose2D(moved, carrier.Pose.RotationRadians);
        int reads = 0;
        host.OnCarrier = () => reads++;
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Should().Be(moved);
        host.Accept(moved, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        reads.Should().Be(1);
        for (int frame = 0; frame < 128 && host.GuidanceStatus == NavigationGuideStatus.Pending; frame++)
            Step(context, host, default);
        host.Pose.Center.Should().Be(moved);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidStart);
        host.HasArrived.Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("partial")]
    [InlineData("exact")]
    [InlineData("landing")]
    public void RawChangeInvalidatesSideViewProgressWithoutRollingBackAcceptedPose(string timing)
    {
        using var context = GuidedStepScene();
        var speed = timing == "partial" ? Fixed64.Two : (Fixed64)4;
        using var host = new Host(context, settings: new NavigationMotorSettings2D(speed, Fixed64.Zero, (Fixed64)16, (Fixed64)4));
        host.Sample(SupportAt(Vector2d.Zero));
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        if (timing == "landing")
        {
            // Reach the bend and the first three crossing substeps. The last request is the high anchor.
            for (int i = 0; i < 4; i++)
            {
                context.Simulate();
                host.PrepareFrameMotion();
                host.Accept(host.Request.Target.Center, SupportAt(host.Request.Target.Center));
                host.CommitFrameMotion();
                context.LateSimulate();
            }
        }
        if (timing == "before")
            ChangeWorld();
        context.Simulate();
        host.PrepareFrameMotion();
        if (timing != "before")
            host.OnRead = ChangeWorld;
        var target = host.Request.Target.Center;
        host.Accept(target, SupportAt(target));
        host.CommitFrameMotion();
        context.LateSimulate();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        host.Pose.Center.Should().Be(target);
        host.HasArrived.Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        if (timing == "landing")
        {
            host.Pose.Center.Should().Be(HighAnchor);
            host.OnRead = null;
            Step(context, host, default);
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.Success);
            host.HasArrived.Should().BeTrue();
        }

        void ChangeWorld() => context.World.TryAddGrid(
            new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidedSlopeUsesTheExactSweepOrRejectsAnOffSegmentRawStep(bool tiny)
    {
        using var context = GuidedStepScene(slope: true);
        context.Pathing.ResolveSideViewAnchor(GuidedAddress(0), Profile, SideViewPolicy, SideViewBudget, out var start)
            .Should().Be(NavigationGuideStatus.Success);
        context.Pathing.ResolveSideViewAnchor(GuidedAddress(1), Profile, SideViewPolicy, SideViewBudget, out var end)
            .Should().Be(NavigationGuideStatus.Success);
        var speed = tiny ? Fixed64.FromRaw(16) : (Fixed64)32;
        using var host = new Host(context, pose: new NavigationPose2D(start, Fixed64.Zero),
            settings: new NavigationMotorSettings2D(speed, Fixed64.Zero, (Fixed64)16, (Fixed64)4));
        host.Sample(Ground);
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        Step(context, host, default);
        host.Pose.Center.Should().Be(tiny ? start : end);
        host.HasArrived.Should().Be(!tiny);
        host.GuidanceStatus.Should().Be(tiny ? NavigationGuideStatus.Unsupported : NavigationGuideStatus.Success);
    }

    [Fact]
    public void ZeroSpeedHoldsAnAnchorAndDisplacementCannotRestartAnUnprovedStage()
    {
        using var context = GuidedStepScene();
        using var host = new Host(context,
            settings: new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero, (Fixed64)16, Fixed64.Zero));
        host.Sample(Ground);
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        context.Simulate();
        host.PrepareFrameMotion();
        host.Request.Target.Center.Should().Be(Vector2d.Zero);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
        host.Accept(Vector2d.Right, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        Step(context, host, default);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidStart);
        host.Pose.Center.Should().Be(Vector2d.Right);
    }

    [Fact]
    public void AcceptedManualJumpReleasesTheSideViewGuideBeforeFreeMotion()
    {
        using var context = GuidedStepScene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(null, true));
        host.Request.Target.Center.Y.Should().Be(Fixed64.FromFraction(3, 16));
        host.Accept(host.Request.Target.Center, null);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.GuidanceStatus.Should().BeNull();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SideViewStartRequiresTheExactSupportedAnchorInTheBoundPlane()
    {
        using var context = GuidedStepScene();
        using var host = new Host(context);
        ApplySideView(host).Should().Be(NavigationGuideStatus.InvalidStart);
        host.Sample(Ground);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(default, GuidedAddress(1), SideViewPolicy, PathAlgorithm.AStar, SideViewBudget)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        host.ApplySideViewGuidedTrekRequest(new NavigationCellAddress("missing", default), GuidedAddress(1),
            SideViewPolicy, PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.NoMap);
        var other = new GridConfiguration(new Vector3d(10, 0, -1), new Vector3d(11, 1, 1));
        context.World.TryAddGrid(other, out _).Should().BeTrue();
        PublishGuidedMap(context, new NavigationMapBuilder("other", other).SetPlanarLayer(NavigationMode2D.SideView,
            0, Fixed64.Zero).Build(), 3);
        host.ApplySideViewGuidedTrekRequest(new NavigationCellAddress("other", default), GuidedAddress(1),
            SideViewPolicy, PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.InvalidStart);
        Step(context, host, new NavigationInput2D(Vector2d.Right, false));
        ApplySideView(host).Should().Be(NavigationGuideStatus.InvalidStart);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SideViewStartRejectsAReplacementOfTheBoundSelectedLayer()
    {
        using var context = GuidedStepScene(embedding: Fixed64.Half);
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        var removal = new NavigationMapRemoveOperation("side", 3, context.FrameCount + 1);
        context.Pathing.Admit(removal).Should().BeTrue();
        for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        PublishGuidedMap(context, GuidedMap(GuidedGrid, 0, Fixed64.Half), 4, 2);
        var source = new NavigationCellAddress("side", new VoxelIndex(0, 0, 1));
        var target = new NavigationCellAddress("side", new VoxelIndex(1, 0, 1));
        context.Pathing.ResolveSideViewAnchor(source, Profile, SideViewPolicy, SideViewBudget, out var center)
            .Should().Be(NavigationGuideStatus.Success);
        center.Should().Be(host.Pose.Center);
        host.ApplySideViewGuidedTrekRequest(source, target, SideViewPolicy, PathAlgorithm.AStar, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Stale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SideViewTrekRequiresSolidSideViewProfile(bool sideView)
    {
        var mode = sideView ? NavigationMode2D.SideView : NavigationMode2D.TopDown;
        using var context = Scene(mode);
        var profile = new NavigationAgentProfile2D(mode, Fixed64.Quarter, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, sideView ? Fixed64.Half : Fixed64.Zero, Fixed64.Zero, TraversalMedia.Gas,
            TraversalCapability.None);
        using var host = new Host(context, profile, sideView ? Settings : default(NavigationMotorSettings2D),
            medium: TraversalMedium.Gas);
        ApplySideView(host).Should().Be(NavigationGuideStatus.InvalidProfile);
        host.GuidanceStatus.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScheduledSideViewRequiresTheSameGroundedOriginOnTransfer(bool stale)
    {
        using var context = GuidedStepScene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        host.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        ApplySideView(host).Should().Be(NavigationGuideStatus.Pending);
        context.Simulate();
        host.PrepareFrameMotion();
        host.Accept(Vector2d.Zero, stale ? Ground : null);
        host.CommitFrameMotion();
        context.LateSimulate();
        if (stale)
            context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21)), out _)
                .Should().BeTrue();
        for (int frame = 0; frame < 128 && host.GuidanceStatus == NavigationGuideStatus.Pending; frame++)
        {
            context.Simulate();
            host.PrepareFrameMotion();
            host.Accept(Vector2d.Zero, stale ? Ground : null);
            host.CommitFrameMotion();
            context.LateSimulate();
        }
        host.GuidanceStatus.Should().Be(stale ? NavigationGuideStatus.Stale : NavigationGuideStatus.InvalidStart);
        host.Pose.Center.Should().Be(Vector2d.Zero);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void SideViewStopsFollowingAContinuousLegThatLosesSupport()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        context.Simulate();
        host.PrepareFrameMotion();
        var accepted = host.Request.Target.Center;
        accepted.Should().Be(new Vector2d(Fixed64.Quarter, Fixed64.Zero));
        host.Accept(accepted, null);
        host.CommitFrameMotion();
        context.LateSimulate();
        context.Simulate();
        host.PrepareFrameMotion();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidStart);
        host.Request.Target.Center.Should().Be(new Vector2d(accepted.X, -Fixed64.FromFraction(1, 16)));
    }

    [Fact]
    public void SideViewStageReadClearsOutputAfterLeaseRelease()
    {
        using var context = GuidedStepScene();
        context.Guides.RequestSideViewGuide(GuidedAddress(0), GuidedAddress(1), Profile, SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired;
        var route = guide.RouteLease;
        route.GetSideViewStage(out _, out _).Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
        route.GetSideViewStage(out var source, out var target).Should().Be(NavigationGuideStatus.Stale);
        source.Address.Should().Be(default(NavigationCellAddress));
        target.Address.Should().Be(default(NavigationCellAddress));
        default(NavigationGuideLease).GetSideViewStage(out _, out _).Should().Be(NavigationGuideStatus.Stale);
    }

    private static NavigationGuideStatus ApplySideView(Host host, bool descending = false,
        PathAlgorithm algorithm = PathAlgorithm.AStar) =>
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(descending ? 1 : 0), GuidedAddress(descending ? 0 : 1),
            SideViewPolicy, algorithm, SideViewBudget);

    private static NavigationCellAddress GuidedAddress(int x) => new("side", new VoxelIndex(x, 1, 1));

    private static NavigationSupport2D? SupportAt(Vector2d center) =>
        center == Vector2d.Zero || center == HighAnchor
            ? new NavigationSupport2D(new Vector2d(center.X, center.Y - Fixed64.Half), Vector2d.Forward, Fixed64.One)
            : null;

    private static readonly GridConfiguration GuidedGrid = new(new Vector3d(0, 0, -1), new Vector3d(1, 1, 1));

    private static TrailblazerWorldContext GuidedStepScene(bool slope = false, bool flat = false, Fixed64? embedding = null)
    {
        var context = TrailblazerWorldContext.CreateOwned(settings: PlanarSchedulingTestSettings.Create());
        context.SetFrameRate(16);
        context.World.TryAddGrid(GuidedGrid, out _).Should().BeTrue();
        var policy = new NavigationAreaPolicy(SideViewPolicy, new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy, 1, 1)).Should().BeTrue();
        PublishGuidedMap(context, GuidedMap(GuidedGrid, 1, embedding ?? Fixed64.One, slope, flat), 2);
        return context;
    }

    private static NavigationMap GuidedMap(GridConfiguration grid, int layer, Fixed64 embedding,
        bool slope = false, bool flat = false)
    {
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var low = new VoxelIndex(0, layer, 1);
        var high = new VoxelIndex(1, layer, 1);
        Fixed64 highStart = flat ? -Fixed64.Half : -Fixed64.Quarter;
        return new NavigationMapBuilder("side", grid).SetPlanarLayer(NavigationMode2D.SideView, layer, embedding)
            .AddCell(low, solid).AddCell(high, solid)
            .AddCell(new VoxelIndex(0, layer, 2), air).AddCell(new VoxelIndex(1, layer, 2), air)
            .AddPlanarSupport(low, new(-Fixed64.Half, -Fixed64.Half),
                new(Fixed64.Half, slope ? -Fixed64.Quarter : -Fixed64.Half))
            .AddPlanarSupport(high, new(Fixed64.Half, highStart),
                new(Fixed64.One + Fixed64.Half, slope ? Fixed64.Zero : highStart)).Build();
    }

    private static void PublishGuidedMap(TrailblazerWorldContext context, NavigationMap map, long sequence, long version = 1)
    {
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, version),
            OverlayReplacementPolicy.Clear, sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }
}
