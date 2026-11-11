using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Fact]
    public void SideViewOccupancyCannotAdoptACoincidentTopDownPublication()
    {
        using var context = Scene();
        var grid = new GridConfiguration(new Vector3d(-5, 0, -5), new Vector3d(5, 1, 5));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var map = new NavigationMapBuilder("top", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .SetDefaultCell(new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
                Fixed64.Zero, Fixed64.One, Fixed64.Zero)).Build();
        var publish = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1), OverlayReplacementPolicy.Clear,
            2, context.FrameCount + 1);
        context.Pathing.Admit(publish).Should().BeTrue();
        for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.LastCommittedCell!.Value.Address.MapId.Should().Be("side");
        var remove = new NavigationMapRemoveOperation("side", 3, context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.LastCommittedCell.Should().BeNull();
    }

    [Fact]
    public void RotatingCarrierTransformsTheSameAnchorAndFailedLookupRebindsFromFreshSupport()
    {
        using var context = Scene();
        using var host = new Host(context);
        var anchor = new Vector2d(2, 1);
        var before = new NavigationPose2D(new Vector2d(3, 4), Fixed64.Zero);
        var after = new NavigationPose2D(new Vector2d(4, 6), Fixed64.Pi / 2);
        host.Sample(Carried(before));
        host.RefreshSupport();
        host.CarrierPose = after;
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        // The anchor moves from (5,5) to (3,8), while the passenger stays upright.
        host.Request.Target.Center.Should().Be(new Vector2d(-2, 3));
        host.Request.Target.RotationRadians.Should().Be(Fixed64.Zero);
        host.Accept(host.Request.Target.Center, Carried(after));
        host.CommitFrameMotion();
        context.LateSimulate();
        after = new NavigationPose2D(after.Center, Fixed64.Zero);
        host.CarrierPose = after;
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Request.Target.Center.Should().Be(new Vector2d(1, 2));
        host.Accept(host.Request.Target.Center, Carried(after));
        host.CommitFrameMotion();
        context.LateSimulate();
        host.CarrierPose = null;
        var rebound = new NavigationPose2D(new Vector2d(9, 6), after.RotationRadians);
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Request.Target.Center.Should().Be(host.Pose.Center);
        host.Accept(host.Request.Target.Center, Carried(rebound));
        host.CommitFrameMotion();
        context.LateSimulate();
        host.CarrierPose = rebound;
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(new Vector2d(1, 2));

        NavigationSupport2D Carried(NavigationPose2D pose) => new(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, pose, anchor));
    }

    [Fact]
    public void ClippedUpwardCarryCannotTurnIntoJumpSpeedOnTheNextFrame()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, default, Vector2d.Zero)));
        host.RefreshSupport();
        host.CarrierPose = new NavigationPose2D(Vector2d.Forward, Fixed64.Zero);
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true));
        host.Request.Target.Center.Y.Should().Be(Fixed64.One + Fixed64.FromFraction(3, 16));
        host.Accept(Vector2d.Zero, null);
        host.CommitFrameMotion();
        context.LateSimulate();
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Request.Target.Center.Y.Should().Be(-Fixed64.FromFraction(1, 16));
    }

    [Fact]
    public void OverflowingAcceptedVelocityPreservesPreviouslyCommittedState()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        var cell = host.LastCommittedCell;
        cell.Should().NotBeNull();
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Accept(new Vector2d(Fixed64.MaxValue, Fixed64.Zero), null);
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<OverflowException>();
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.Velocity.Should().Be(Vector2d.Zero);
        host.LastCommittedCell.Should().Be(cell);
        host.Support!.Value.Point.Should().Be(Ground.Point);
        host.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public void SideViewHostStepsReplayExactlyAndRemainAllocationFreeWhenWarm()
    {
        List<(Vector2d Center, Vector2d Velocity, Vector2d Facing, bool Grounded)> Run()
        {
            using var context = Scene();
            using var host = new Host(context);
            host.Sample(Ground);
            host.RefreshSupport();
            var result = new List<(Vector2d, Vector2d, Vector2d, bool)>();
            for (int i = 0; i < 32; i++)
            {
                context.Simulate();
                host.PrepareFrameMotion(new NavigationInput2D(i < 16 ? Vector2d.Right : -Vector2d.Right, i == 2));
                host.Accept(host.Request.Target.Center, i < 2 || i == 20 ? Ground : null);
                host.CommitFrameMotion();
                context.LateSimulate();
                if (i == 2)
                    host.Velocity.Y.Should().Be(Fixed64.FromFraction(3, 1));
                result.Add((host.Pose.Center, host.Velocity, host.Facing, host.IsGrounded));
            }
            return result;
        }
        Run().Should().Equal(Run());

        using var context = Scene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        var input = new NavigationInput2D(Vector2d.Zero, false);
        for (int i = 0; i < 128; i++) Step(context, host, input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) Step(context, host, input);
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtremeCarrierMotionRejectsBeforeAcceptedStateChanges(bool subtractionOverflow)
    {
        using var context = Scene();
        using var host = new Host(context);
        Fixed64 beforeY = subtractionOverflow ? Fixed64.MinValue : Fixed64.Zero;
        host.Sample(new NavigationSupport2D(default, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, new NavigationPose2D(new Vector2d(Fixed64.Zero, beforeY), Fixed64.Zero), default)));
        host.RefreshSupport();
        host.CarrierPose = new NavigationPose2D(new Vector2d(Fixed64.Zero, Fixed64.MaxValue), Fixed64.Zero);
        context.Simulate();
        if (subtractionOverflow)
            host.Invoking(h => h.PrepareFrameMotion(Vector2d.Zero)).Should().Throw<OverflowException>();
        else
        {
            host.PrepareFrameMotion(Vector2d.Zero);
            host.Accept(-Vector2d.Forward, null);
            host.Invoking(h => h.CommitFrameMotion()).Should().Throw<OverflowException>();
        }
        host.Pose.Center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void CarryFreeAcceptedMotorSpeedMustAlsoBeRepresentable()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(new NavigationSupport2D(default, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, default, default)));
        host.RefreshSupport();
        host.CarrierPose = new NavigationPose2D(new Vector2d(Fixed64.Zero, Fixed64.MinValue / 2), Fixed64.Zero);
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Zero, true));
        host.Accept(Vector2d.Zero, null);
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<OverflowException>();
        host.Pose.Center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void GravityAndTargetOverflowCannotSubmitASaturatedPose()
    {
        using var context = Scene();
        using var host = new Host(context, settings: new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero,
            Fixed64.MaxValue, Fixed64.Zero));
        for (int i = 0; i < 16; i++)
        {
            context.Simulate();
            host.PrepareFrameMotion(Vector2d.Zero);
            host.Accept(Vector2d.Zero, null);
            host.CommitFrameMotion();
            context.LateSimulate();
        }
        context.Simulate();
        host.Invoking(h => h.PrepareFrameMotion(Vector2d.Zero)).Should().Throw<OverflowException>();
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.Dispose();
        using var below = new Host(context, pose: new NavigationPose2D(new Vector2d(Fixed64.Zero, Fixed64.MinValue), Fixed64.Zero));
        below.Invoking(h => h.PrepareFrameMotion(Vector2d.Zero)).Should().Throw<OverflowException>();
    }

    [Fact]
    public void DefaultCarrierLookupSuppressesCarryWithoutLosingFreshSupport()
    {
        using var context = Scene();
        using var host = new Host(context) { UseBaseCarrierLookup = true };
        host.Sample(new NavigationSupport2D(default, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, default, default)));
        host.RefreshSupport();
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.IsGrounded.Should().BeTrue();
    }
}
