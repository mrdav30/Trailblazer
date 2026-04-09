using FixedMathSharp;
using FluentAssertions;
using System;
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

        agent.Motor.Handler.Platform.Should().NotBeNull();
        agent.Motor.Handler.Platform!.ActivePlatform.Should().NotBeNull();
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
        agent.Motor.Handler.Jump.Should().NotBeNull();
        agent.Motor.Handler.Fly.Should().NotBeNull();
        agent.Motor.Handler.Platform.Should().BeNull();
        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Swim.Should().BeNull();
    }
}
