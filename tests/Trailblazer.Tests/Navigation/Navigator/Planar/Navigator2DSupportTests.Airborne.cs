using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    private static NavigationMotorSettings2D ExtraJumpSettings => new(Settings.MoveSpeed,
        Settings.TurnRateRadians, Settings.Gravity, Settings.JumpSpeed, maxJumpCount: 2);

    [Fact]
    public void ExtraJumpConsumesItsAllowanceDuringAscentAndLandingReplenishesIt()
    {
        using var context = Scene();
        using var host = new Host(context, settings: ExtraJumpSettings);
        host.Sample(Ground);
        host.RefreshSupport();

        var jump = new NavigationInput2D(null, true);
        Step(context, host, jump);
        host.Velocity.Y.Should().Be((Fixed64)3);
        host.IsGrounded.Should().BeFalse();
        Step(context, host, new NavigationInput2D(null, true, blockJump: true));
        host.Velocity.Y.Should().Be(Fixed64.Two);

        context.Simulate();
        host.PrepareFrameMotion(jump);
        Vector2d launch = host.Request.Target.Center - host.Pose.Center;
        launch.Y.Should().Be(Fixed64.FromFraction(3, 16));
        host.Accept(host.Pose.Center + launch * Fixed64.Half, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Velocity.Y.Should().Be(Fixed64.FromFraction(3, 2));
        Step(context, host, jump);
        host.Velocity.Y.Should().Be(Fixed64.Half);

        host.Sample(null);
        host.RefreshSupport();
        for (int frame = 0; frame < 12 && !host.IsGrounded; frame++)
        {
            context.Simulate();
            host.PrepareFrameMotion(jump);
            var target = host.Request.Target.Center;
            if (target.Y <= Fixed64.Zero)
                host.Accept(Vector2d.Zero, Ground);
            else
                host.Accept(target, null);
            host.CommitFrameMotion();
            context.LateSimulate();
        }

        host.IsGrounded.Should().BeTrue();
        Step(context, host, jump);
        Step(context, host, jump);
        host.Pose.Center.Y.Should().Be(Fixed64.FromFraction(3, 8));
        host.Velocity.Y.Should().Be((Fixed64)3);
    }

    [Fact]
    public void UnspentExtraJumpCannotRestartAscentAtTheApexOrWhileFalling()
    {
        using var context = Scene();
        using var host = new Host(context, settings: ExtraJumpSettings);
        host.Sample(Ground);
        host.RefreshSupport();
        var jump = new NavigationInput2D(null, true);
        Step(context, host, jump);
        host.Sample(null);
        host.RefreshSupport();
        for (int i = 0; i < 3; i++)
            Step(context, host, default);
        host.Velocity.Y.Should().Be(Fixed64.Zero);

        Step(context, host, jump);
        host.Velocity.Y.Should().Be(-Fixed64.One);
        Step(context, host, jump);
        host.Velocity.Y.Should().Be(-Fixed64.Two);
        host.IsGrounded.Should().BeFalse();
    }

    [Fact]
    public void ExtraJumpLimitMustBePositiveAndIsSpecificToSideView()
    {
        Action create = () => new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero, Fixed64.One,
            Fixed64.One, maxJumpCount: 0);
        create.Should().Throw<ArgumentException>();

        using var context = Scene(NavigationMode2D.TopDown);
        var profile = new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Quarter, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        create = () => new Host(context, profile, new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, maxJumpCount: 2));
        create.Should().Throw<ArgumentException>();
    }
}
