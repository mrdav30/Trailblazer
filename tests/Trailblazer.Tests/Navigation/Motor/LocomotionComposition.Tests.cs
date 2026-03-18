using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Serialization;
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
            startingMedium: TraversalMedium.Ground,
            profile: LocomotionProfile.CreateMoveAndFallOnly());

        agent.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        agent.Motor.Handler.Move.Should().NotBeNull();
        agent.Motor.Handler.Fall.Should().NotBeNull();
        agent.Motor.Handler.Platform.Should().BeNull();
        agent.Motor.Handler.Jump.Should().BeNull();
        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Swim.Should().BeNull();
    }

    [Fact]
    public void Given_NavigatorOverride_When_Initialized_Then_CustomProfileIsUsed()
    {
        var navigator = new MinimalProfileNavigator();
        navigator.Setup(Vector3d.Zero);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Ground,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });

        navigator.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        navigator.Motor.Handler.Jump.Should().BeNull();
        navigator.Motor.Handler.Swim.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_When_JumpRequested_Then_JumpIsIgnored()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent(
            profile: LocomotionProfile.CreateMoveAndFallOnly());

        agent.FrameRequest.IsRequestingJump = true;
        agent.Simulate();

        agent.Position.y.Should().Be(Fixed64.Zero);
        agent.Motor.IsGrounded.Should().BeTrue();
        agent.Motor.Handler.Jump.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_When_SwimInputRequested_Then_InputDoesNotMoveAgent()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(
            profile: LocomotionProfile.CreateMoveAndFallOnly());

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.Simulate();

        agent.Position.x.Should().Be(Fixed64.Zero);
        agent.Position.z.Should().Be(Fixed64.Zero);
        agent.Motor.Handler.Swim.Should().BeNull();
    }

    [Fact]
    public void Given_MinimalProfile_OnSteepSlope_When_Simulated_Then_FallStartsWithoutSlideModule()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

        var profile = LocomotionProfile.CreateMoveAndFallOnly();
        profile.Move.SlopeLimit = (Fixed64)45;

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: platform,
            profile: profile);

        TrailblazerManager.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void Given_DefaultMotor_When_ReconfiguredToMinimalProfile_Then_OptionalLocomotionsAreRemoved()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        agent.Motor.Handler.Jump.RegisterJump();

        agent.Motor.SetLocomotionProfile(LocomotionProfile.CreateMoveAndFallOnly());

        agent.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        agent.Motor.Handler.Jump.Should().BeNull();
        agent.Motor.Handler.Platform.Should().BeNull();
        agent.Motor.Handler.Slide.Should().BeNull();
        agent.Motor.Handler.Swim.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_MinimalProfile_ShouldPreserveInstalledKinds(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Ground,
            profile: LocomotionProfile.CreateMoveAndFallOnly());
        source.Motor.Handler.Move.MaxFastSpeed = (Fixed64)2;
        source.Motor.Handler.Fall.IsFalling = true;
        object payload = SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        target.Motor.Handler.Move.MaxFastSpeed.Should().Be((Fixed64)2);
        target.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        target.Motor.Handler.Jump.Should().BeNull();
        target.Motor.Handler.Platform.Should().BeNull();
        target.Motor.Handler.Slide.Should().BeNull();
        target.Motor.Handler.Swim.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_DefaultProfile_ShouldLoadAllModules_WhenInstalledKindsUsesDeclaredDefault(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        object payload = SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateJumpReadyAgent(
            profile: LocomotionProfile.CreateMoveAndFallOnly());
        PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.All);
        target.Motor.Handler.Jump.Should().NotBeNull();
        target.Motor.Handler.Platform.Should().NotBeNull();
        target.Motor.Handler.Slide.Should().NotBeNull();
        target.Motor.Handler.Swim.Should().NotBeNull();
    }

    private static object SerializeRecord(IRecordable record, bool useMemoryPack)
    {
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(record)
            : JsonRecordSerializer.Serialize(record, writeIndented: true);
    }

    private static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
        if (useMemoryPack)
        {
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
            return;
        }

        JsonRecordSerializer.Populate(target, (string)payload);
    }

    private sealed class MinimalProfileNavigator : Navigator
    {
        public override void CheckTrekCondition()
        {
        }

        protected override LocomotionProfile CreateLocomotionProfile()
        {
            return LocomotionProfile.CreateMoveAndFallOnly();
        }
    }
}
