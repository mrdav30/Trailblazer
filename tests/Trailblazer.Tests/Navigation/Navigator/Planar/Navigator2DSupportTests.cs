using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    private static readonly NavigationAgentProfile2D Profile = new(NavigationMode2D.SideView, Fixed64.Quarter,
        Fixed64.Half, Fixed64.Quarter, Fixed64.Half, Fixed64.Half, Fixed64.Zero,
        TraversalMedia.Solid, TraversalCapability.Jump);
    private static readonly NavigationMotorSettings2D Settings = new((Fixed64)4, Fixed64.Zero, (Fixed64)16, (Fixed64)4);
    private static readonly NavigationSupport2D Ground = new(Vector2d.Zero, Vector2d.Forward, Fixed64.One);

    [Fact]
    public void StandingAndHorizontalMotionPublishOnlyAcceptedSupportAndFacing()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.SupportStatus.Should().Be(NavigationSupportStatus2D.Unavailable);
        host.ReadCount.Should().Be(0);
        host.Sample(Ground);
        host.RefreshSupport();
        host.ReadCount.Should().Be(1);
        host.LastWasRefresh.Should().BeTrue();
        host.IsGrounded.Should().BeTrue();
        host.LastCommittedCell.Should().BeNull();

        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(-Vector2d.Right, false));
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.Facing.Should().Be(Vector2d.Right);
        host.Request.Target.Center.Should().Be(new Vector2d(-Fixed64.Quarter, Fixed64.Zero));
        host.Accept(new Vector2d(-Fixed64.FromFraction(1, 8), Fixed64.Zero), Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Facing.Should().Be(-Vector2d.Right);
        host.Velocity.Should().Be(new Vector2d(-Fixed64.Two, Fixed64.Zero));
        host.IsGrounded.Should().BeTrue();
        host.LastCommittedCell!.Value.Medium.Should().Be(TraversalMedium.Solid);
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Velocity.Should().Be(Vector2d.Zero);
        host.Facing.Should().Be(-Vector2d.Right);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void JumpRequiresSupportCapabilityAndHostPermission(bool blocked, bool noCapability, bool admitted)
    {
        using var context = Scene();
        var profile = new NavigationAgentProfile2D(NavigationMode2D.SideView, Fixed64.Quarter, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Half, Fixed64.Zero, TraversalMedia.Solid,
            noCapability ? TraversalCapability.None : TraversalCapability.Jump);
        using var host = new Host(context, profile);
        host.Sample(Ground);
        host.RefreshSupport();
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true, blockJump: blocked));
        host.Request.Target.Center.Y.Should().Be(admitted ? Fixed64.FromFraction(3, 16) : Fixed64.Zero);
        host.Accept(host.Request.Target.Center, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.IsGrounded.Should().Be(!admitted);
        if (admitted)
        {
            // A witness still visible under an ascending capsule cannot admit another jump.
            Step(context, host, new NavigationInput2D(Vector2d.Zero, true));
            host.Velocity.Y.Should().Be(Fixed64.Two);
        }
    }

    [Fact]
    public void CeilingClippingStopsAscentAndFreshLandingRestoresJump()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true));
        host.Accept(Vector2d.Zero, null);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.IsGrounded.Should().BeFalse();
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, false));
        host.Request.Target.Center.Y.Should().Be(-Fixed64.FromFraction(1, 16));
        host.Accept(Vector2d.Zero, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.IsGrounded.Should().BeTrue();
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true));
        host.Request.Target.Center.Y.Should().Be(Fixed64.FromFraction(3, 16));
    }

    [Theory]
    [InlineData(NavigationSupportStatus2D.NoSupport)]
    [InlineData(NavigationSupportStatus2D.Unavailable)]
    public void LosingSupportFallsAndNeverReusesThePreviousSample(NavigationSupportStatus2D status)
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Right);
        host.Result = new NavigationMotionResult2D(host.Request.Target, status);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Support.Should().BeNull();
        host.IsGrounded.Should().BeFalse();
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true));
        host.Request.Target.Center.Y.Should().Be(-Fixed64.FromFraction(1, 16));
    }

    [Fact]
    public void WalkableSlopeUsesTangentSpeedAndSteepWitnessDoesNotGround()
    {
        using var context = Scene();
        using var host = new Host(context);
        var slope = new NavigationSupport2D(Vector2d.Zero,
            new Vector2d(-Fixed64.FromFraction(3, 5), Fixed64.FromFraction(4, 5)), Fixed64.One);
        host.Sample(slope);
        host.RefreshSupport();
        host.IsGrounded.Should().BeTrue();
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Right);
        host.Request.Target.Center.Should().Be(new Vector2d(Fixed64.FromFraction(1, 5), Fixed64.FromFraction(3, 20)));
        host.Accept(host.Request.Target.Center, slope);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.IsGrounded.Should().BeTrue();
        host.Sample(new NavigationSupport2D(host.Pose.Center, Vector2d.Right, Fixed64.One));
        host.RefreshSupport();
        host.IsGrounded.Should().BeFalse();
    }

    [Fact]
    public void CarrierDeltaIsAppliedOnceAndStationaryRotatedCarrierIsExactlyStill()
    {
        using var context = Scene();
        using var host = new Host(context);
        var carrier = new NavigationCarrier2D(7, 11, new NavigationPose2D(Vector2d.Zero, Fixed64.Pi / 3),
            new Vector2d(3, 2));
        host.CarrierPose = carrier.Pose;
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One, carrier));
        host.RefreshSupport();
        for (int i = 0; i < 8; i++)
        {
            Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
            host.Pose.Center.Should().Be(Vector2d.Zero);
            host.Velocity.Should().Be(Vector2d.Zero);
        }
        host.CarrierPose = new NavigationPose2D(Vector2d.Right, carrier.Pose.RotationRadians);
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Request.Target.Center.Should().Be(Vector2d.Right);
        host.Accept(host.Request.Target.Center, new NavigationSupport2D(Vector2d.Right, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, host.CarrierPose.Value, carrier.LocalAnchor)));
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Velocity.Should().Be(new Vector2d(16, 0));
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(Vector2d.Right);
        host.Velocity.Should().Be(Vector2d.Zero);
        host.CarrierPose = null;
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(Vector2d.Right);
    }

    [Fact]
    public void InvalidAcceptedResultAndRefreshCannotMutatePoseOrOccupancy()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Result = new NavigationMotionResult2D(new NavigationPose2D(Vector2d.Right, Fixed64.Zero), NavigationSupportStatus2D.NoSupport);
        host.Invoking(h => h.RefreshSupport()).Should().Throw<ArgumentException>();
        host.Pose.Center.Should().Be(Vector2d.Zero);
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Result = default;
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<ArgumentException>();
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.LastCommittedCell.Should().BeNull();
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SideViewInputsAndAcceptedRotationMustRemainUpright()
    {
        using var context = Scene();
        using var host = new Host(context);
        context.Simulate();
        host.Invoking(h => h.PrepareFrameMotion(Vector2d.Forward)).Should().Throw<ArgumentException>();
        host.Invoking(h => h.PrepareFrameMotion(new NavigationInput2D(null, false, Vector2d.Forward)))
            .Should().Throw<ArgumentException>();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Result = new NavigationMotionResult2D(new NavigationPose2D(Vector2d.Zero, Fixed64.One), NavigationSupportStatus2D.NoSupport);
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<ArgumentException>();
        host.Pose.RotationRadians.Should().Be(Fixed64.Zero);
    }

    private static void Step(TrailblazerWorldContext context, Host host, NavigationInput2D input)
    {
        context.Simulate();
        host.PrepareFrameMotion(input);
        host.Accept(host.Request.Target.Center, host.Support);
        host.CommitFrameMotion();
        context.LateSimulate();
    }

    private static TrailblazerWorldContext Scene(NavigationMode2D mode = NavigationMode2D.SideView)
    {
        var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        var grid = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 1, 4));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("side", grid).SetPlanarLayer(mode, 1, Fixed64.One);
        builder.AddCell(new VoxelIndex(4, 1, 4), new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, Fixed64.One, Fixed64.Two));
        context.Pathing.Admit(new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear, 1, 1)).Should().BeTrue();
        for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
        return context;
    }

    private sealed class Host : Navigator2D
    {
        internal Host(TrailblazerWorldContext context, NavigationAgentProfile2D? profile = null,
            NavigationMotorSettings2D? settings = null, NavigationPose2D? pose = null,
            TraversalMedium medium = TraversalMedium.Solid)
            : base(context, "side", Guid.Parse("bb9033a6-52a5-4cc2-91e7-ddabbb8e1363"),
                pose ?? new NavigationPose2D(Vector2d.Zero, Fixed64.Zero), profile ?? Profile, settings ?? Settings, medium) { }
        internal NavigationMotionRequest2D Request { get; private set; }
        internal NavigationMotionResult2D Result { get; set; }
        internal NavigationPose2D? CarrierPose { get; set; }
        internal int ReadCount { get; private set; }
        internal bool LastWasRefresh { get; private set; }
        internal Action? OnRead { get; set; }
        internal Action? OnCarrier { get; set; }
        internal bool UseBaseCarrierLookup { get; set; }
        internal void Sample(NavigationSupport2D? support) => Accept(Pose.Center, support);
        internal void Accept(Vector2d center, NavigationSupport2D? support) => Result = new NavigationMotionResult2D(
            new NavigationPose2D(center, Fixed64.Zero), support.HasValue ? NavigationSupportStatus2D.Found : NavigationSupportStatus2D.NoSupport, support);
        protected override void SubmitMotionTarget(in NavigationMotionRequest2D request) => Request = request;
        protected override NavigationMotionResult2D ReadAcceptedMotion(NavigationMotionRequest2D? request)
        {
            ReadCount++;
            LastWasRefresh = !request.HasValue;
            OnRead?.Invoke();
            return Result;
        }
        protected override bool TryReadCarrierPose(int stableId, long lifetime, out NavigationPose2D pose)
        {
            if (UseBaseCarrierLookup)
                return base.TryReadCarrierPose(stableId, lifetime, out pose);
            stableId.Should().Be(7);
            lifetime.Should().Be(11);
            OnCarrier?.Invoke();
            pose = CarrierPose.GetValueOrDefault();
            return CarrierPose.HasValue;
        }
    }
}
