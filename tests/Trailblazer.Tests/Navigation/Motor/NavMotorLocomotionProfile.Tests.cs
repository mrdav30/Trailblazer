using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class NavMotorLocomotionProfileTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SetLocomotionProfile_ShouldThrowWhileTraversalIsInProgress()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        agent.FrameRequest.Origin = agent.Position;
        agent.FrameRequest.FootPosition = agent.GetFootPosition();
        agent.FrameRequest.Rotation = agent.Rotation;

        agent.Motor.TryTraversal(agent.FrameRequest, out _, out _, out _).Should().BeTrue();
        Action act = () => agent.Motor.SetLocomotionProfile(LocomotionProfile.CreateMoveAndFallOnly());

        act.Should().Throw<InvalidOperationException>();
        agent.Motor.AbortTraversalFrame();
    }

    [Fact]
    public void SetLocomotionProfile_ShouldRefreshActivePlatformWhenGrounded()
    {
        var platformMatrix = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: new Vector3d(3, 0, 2));
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platformMatrix);
        var profile = new LocomotionProfileBuilder(includeOptionalLocomotions: false)
            .WithPlatform()
            .Build();

        agent.Motor.SetLocomotionProfile(profile);

        Assert.NotNull(agent.Motor.Handler.Platform);
        Assert.NotNull(agent.Motor.Handler.Platform!.ActivePlatform);
        agent.Motor.Handler.Platform.ActivePlatform!.Value.Transform.Should().Be(platformMatrix);
    }

    [Fact]
    public void ConfigureLocomotions_ShouldSeedBuilderFromCurrentHandlerComposition()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateMoveAndFallOnly());

        agent.Motor.ConfigureLocomotions(builder => builder
            .WithJump()
            .WithFly());

        agent.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Jump | LocomotionKind.Fly);
        Assert.NotNull(agent.Motor.Handler.Jump);
        Assert.NotNull(agent.Motor.Handler.Fly);
        agent.Motor.Handler.Platform.Should().BeNull();
        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Swim.Should().BeNull();
    }

    [Fact]
    public void StateChanged_ShouldOnlyReportKnownMediumTransitions()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        agent.Motor.StateChanged.Should().BeFalse();

        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        agent.Motor.StateChanged.Should().BeTrue();

        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        agent.Motor.StateChanged.Should().BeFalse();

        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Unknown });
        agent.Motor.StateChanged.Should().BeFalse();
    }

    [Fact]
    public void GetMaxAcceleration_ShouldSelectTraversalSpecificAcceleration_AndRejectUnknownState()
    {
        var groundedAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        groundedAgent.Motor.GetMaxAcceleration().Should().Be(groundedAgent.Motor.Handler.Move.MaxGroundAcceleration);

        var swimmingAgent = MockMotorAgentTestFactory.CreateWaterAgent();
        swimmingAgent.Motor.Handler.Swim!.IsSwimming = true;
        swimmingAgent.Motor.GetMaxAcceleration().Should().Be(swimmingAgent.Motor.Handler.Swim!.MaxSwimAcceleration);
        swimmingAgent.Motor.Handler.Swim.CanSwim = false;
        swimmingAgent.Motor.GetMaxAcceleration().Should().Be(swimmingAgent.Motor.Handler.Move.MaxAirAcceleration);

        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.GetMaxAcceleration().Should().Be(flyingAgent.Motor.Handler.Fly.MaxFlyAcceleration);
        flyingAgent.Motor.Handler.Fly.CanFly = false;
        flyingAgent.Motor.GetMaxAcceleration().Should().Be(flyingAgent.Motor.Handler.Move.MaxAirAcceleration);

        var jumpingAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        jumpingAgent.Motor.Handler.Jump!.IsJumping = true;
        jumpingAgent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        jumpingAgent.Motor.GetMaxAcceleration().Should().Be(jumpingAgent.Motor.Handler.Move.MaxAirAcceleration);

        var unknownAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);
        unknownAgent.Invoking(agent => agent.Motor.GetMaxAcceleration())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown*");
    }

    [Fact]
    public void FlightSpeedScalingAndJumpSpeed_ShouldRespectInstalledLocomotionState()
    {
        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)10;
        flyingAgent.Motor.Handler.Move.MaxSlowSpeed = (Fixed64)2;
        flyingAgent.Motor.Handler.Move.MaxModerateSpeed = (Fixed64)5;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)10;

        flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Stationary).Should().Be(Fixed64.Zero);
        (flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Slow) - (Fixed64)2).Abs().Should().BeLessThan((Fixed64)0.0001f);
        (flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate) - (Fixed64)5).Abs().Should().BeLessThan((Fixed64)0.0001f);
        (flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Fast) - (Fixed64)10).Abs().Should().BeLessThan((Fixed64)0.0001f);

        flyingAgent.Motor.Handler.Move.MaxFastSpeed = Fixed64.Zero;
        (flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate) - (Fixed64)10).Abs().Should().BeLessThan((Fixed64)0.0001f);

        var jumpLessAgent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateMoveAndFallOnly());
        jumpLessAgent.Motor.GetVerticalJumpSpeed().Should().Be(Fixed64.Zero);

        var jumpAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        jumpAgent.Motor.GetVerticalJumpSpeed().Should().BeGreaterThan(Fixed64.Zero);
    }
}
