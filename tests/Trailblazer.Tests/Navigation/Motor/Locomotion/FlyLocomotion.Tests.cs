using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class FlyLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_AirborneAgent_When_FlightUsesFullGravityCompensation_Then_ShouldHover()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var fly = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fly);
        var fall = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fall);

        fly.GravityCompensation = Fixed64.One;

        TestWorld.Context.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        agent.Position.Y.Should().Be((Fixed64)10);
        fly.IsFlying.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void Given_AirborneAgent_When_FlightUsesPartialGravityCompensation_Then_ShouldReduceGravityByConfiguredAmount()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var fly = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fly);

        fly.GravityCompensation = Fixed64.Half;

        TestWorld.Context.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.Simulate();

        Vector3d expectedVelocity = new(
            Fixed64.Zero,
            -(agent.Motor.Handler.Forces.GravityForce * TestWorld.Context.DeltaTime * Fixed64.Half),
            Fixed64.Zero);

        agent.Motor.Handler.Move.FrameVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AirborneAgent_When_FlyingDownward_Then_ShouldNotEnterFallState()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var fly = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fly);
        var fall = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fall);

        TestWorld.Context.Simulate();
        agent.FrameRequest.Direction = Vector3d.Down;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        agent.Position.Y.Should().BeLessThan((Fixed64)10);
        fly.IsFlying.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void Given_FlyingAgent_When_FlightRequestStops_Then_ShouldTransitionIntoFall()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var fly = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fly);
        var fall = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fall);

        fly.GravityCompensation = Fixed64.One;

        TestWorld.Context.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        fly.IsFlying.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();

        TestWorld.Context.Simulate();
        agent.Simulate();

        fly.IsFlying.Should().BeFalse();
        fall.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void FlyLocomotion_WhenDisabled_ShouldClearTransientState()
    {
        var fly = new FlyLocomotion
        {
            IsFlying = true
        };
        fly.IsEnabled.Should().BeTrue();

        fly.IsEnabled = false;

        fly.IsEnabled.Should().BeFalse();
        fly.IsFlying.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_FlyLocomotion_WhenDisabledOnLoad_ShouldClearIsFlying(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var sourceFly = TestRequire.NotNull(TestRequire.NotNull(source.Motor).Handler.Fly);

        // Mark as flying and then disable before serializing.
        sourceFly.IsFlying = true;
        sourceFly.IsEnabled = false;

        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: Vector3d.Zero,
            startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        TestRequire.NotNull(targetMotor.Handler.Fly).IsEnabled = true;
        SerializationUtility.PopulateRecord(targetMotor, payload, useMemoryPack);
        var targetFly = TestRequire.NotNull(targetMotor.Handler.Fly);

        targetFly.IsEnabled.Should().BeFalse();
        targetFly.IsFlying.Should().BeFalse();
    }

    [Fact]
    public void Given_FlyingAgent_When_QueryingHorizontalFlightSpeed_Then_ShouldUseFlightSpeedLimits()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var fly = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Fly);

        fly.IsFlying = true;
        fly.MaxFlySpeed = (Fixed64)7;

        Fixed64 speed = agent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Fast);

        speed.Should().Be((Fixed64)7);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_FlyLocomotion_ShouldPreserveConfigurationAndRuntimeState(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        var sourceFly = TestRequire.NotNull(TestRequire.NotNull(source.Motor).Handler.Fly);
        sourceFly.MaxFlySpeed = (Fixed64)3;
        sourceFly.MaxAscendSpeed = (Fixed64)2;
        sourceFly.MaxDescendSpeed = (Fixed64)1.5f;
        sourceFly.MaxFlyAcceleration = (Fixed64)24;
        sourceFly.GravityCompensation = (Fixed64)0.75f;
        sourceFly.IsFlying = true;

        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: Vector3d.Zero,
            startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        SerializationUtility.PopulateRecord(targetMotor, payload, useMemoryPack);

        var targetFly = TestRequire.NotNull(targetMotor.Handler.Fly);
        targetFly.MaxFlySpeed.Should().Be((Fixed64)3);
        targetFly.MaxAscendSpeed.Should().Be((Fixed64)2);
        targetFly.MaxDescendSpeed.Should().Be((Fixed64)1.5f);
        targetFly.MaxFlyAcceleration.Should().Be((Fixed64)24);
        targetFly.GravityCompensation.Should().Be((Fixed64)0.75f);
        targetFly.IsFlying.Should().BeTrue();
    }
}
