using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class LocomotionCompositionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_MinimalProfile_When_MotorIsCreated_Then_OnlyCoreLocomotionsAreInstalled()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateCoreOnly());

        agent.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        Assert.NotNull(agent.Motor.Handler.Move);
        Assert.NotNull(agent.Motor.Handler.Fall);
        Assert.NotNull(agent.Motor.Handler.Platform);
        agent.Motor.Handler.Jump.Should().BeNull();
        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Water.Should().BeNull();
        agent.Motor.Handler.Fly.Should().BeNull();
        agent.Motor.Handler.Climb.Should().BeNull();
    }

    [Fact]
    public void Given_NavigatorOverride_When_Initialized_Then_CustomProfileIsUsed()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var navigator = new MinimalProfileNavigator(context);
        navigator.Setup(Vector3d.Zero);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        var motor = TestRequire.NotNull(navigator.Motor);

        motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        motor.Handler.Jump.Should().BeNull();
        motor.Handler.Water.Should().BeNull();
        motor.Handler.Fly.Should().BeNull();
        motor.Handler.Climb.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_When_JumpRequested_Then_JumpIsIgnored()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent(
            profile: LocomotionProfile.CreateCoreOnly());

        agent.FrameRequest.IsRequestingJump = true;
        agent.Simulate();

        agent.Position.y.Should().Be(Fixed64.Zero);
        agent.Motor.IsOnSolid.Should().BeTrue();
        agent.Motor.Handler.Jump.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_When_SwimInputRequested_Then_InputDoesNotMoveAgent()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(
            profile: LocomotionProfile.CreateCoreOnly());

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.Simulate();

        agent.Position.x.Should().Be(Fixed64.Zero);
        agent.Position.z.Should().Be(Fixed64.Zero);
        agent.Motor.Handler.Water.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_OnSteepSlope_When_Simulated_Then_FallStartsWithoutSlideModule()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

        var profile = LocomotionProfile.CreateCoreOnly();
        profile.Move.SlopeLimit = (Fixed64)45;

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: platform,
            profile: profile);
        var motor = TestRequire.NotNull(agent.Motor);

        TrailblazerManager.Simulate();
        agent.Simulate();

        motor.Handler.Slide.Should().BeNull();
        TestRequire.NotNull(motor.Handler.Fall).IsFalling.Should().BeTrue();
    }

    [Fact]
    public void Given_DefaultMotor_When_ReconfiguredToMinimalProfile_Then_OptionalLocomotionsAreRemoved()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var motor = TestRequire.NotNull(agent.Motor);
        TestRequire.NotNull(motor.Handler.Jump).RegisterJump();

        motor.SetLocomotionProfile(LocomotionProfile.CreateCoreOnly());

        motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        motor.Handler.Jump.Should().BeNull();
        Assert.NotNull(motor.Handler.Platform);
        motor.Handler.Slide.Should().BeNull();
        motor.Handler.Water.Should().BeNull();
        motor.Handler.Fly.Should().BeNull();
        agent.Motor.Handler.Climb.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_MinimalProfile_ShouldPreserveInstalledKinds(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateCoreOnly());
        source.Motor.Handler.Move.MaxFastSpeed = (Fixed64)2;
        source.Motor.Handler.Fall.IsFalling = true;
        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        SerializationUtility.PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        target.Motor.Handler.Move.MaxFastSpeed.Should().Be((Fixed64)2);
        target.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        target.Motor.Handler.Jump.Should().BeNull();
        Assert.NotNull(target.Motor.Handler.Platform);
        target.Motor.Handler.Slide.Should().BeNull();
        target.Motor.Handler.Water.Should().BeNull();
        target.Motor.Handler.Fly.Should().BeNull();
        target.Motor.Handler.Climb.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_DefaultProfile_ShouldLoadAllModules_WhenInstalledKindsUsesDeclaredDefault(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateJumpReadyAgent(
            profile: LocomotionProfile.CreateCoreOnly());
        SerializationUtility.PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.All);
        Assert.NotNull(target.Motor.Handler.Jump);
        Assert.NotNull(target.Motor.Handler.Platform);
        Assert.NotNull(target.Motor.Handler.Slide);
        Assert.NotNull(target.Motor.Handler.Water);
        Assert.NotNull(target.Motor.Handler.Fly);
        Assert.NotNull(target.Motor.Handler.Climb);
    }

    private sealed class MinimalProfileNavigator : Navigator
    {
        public MinimalProfileNavigator(TrailblazerWorldContext context)
            : base(context)
        {
        }

        public override void CheckTrekCondition()
        {
        }

        protected override LocomotionProfile CreateLocomotionProfile()
        {
            return LocomotionProfile.CreateCoreOnly();
        }
    }
}
