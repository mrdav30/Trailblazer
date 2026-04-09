using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class FlyLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_AirborneAgent_When_FlightUsesFullGravityCompensation_Then_ShouldHover()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        agent.Motor.Handler.Fly.GravityCompensation = Fixed64.One;

        TrailblazerManager.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        agent.Position.y.Should().Be((Fixed64)10);
        agent.Motor.Handler.Fly.IsFlying.Should().BeTrue();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void Given_AirborneAgent_When_FlightUsesPartialGravityCompensation_Then_ShouldReduceGravityByConfiguredAmount()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        agent.Motor.Handler.Fly.GravityCompensation = Fixed64.Half;

        TrailblazerManager.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.Simulate();

        Vector3d expectedVelocity = new(
            Fixed64.Zero,
            -(agent.Motor.Handler.Move.GravityForce * TrailblazerManager.DeltaTime * Fixed64.Half),
            Fixed64.Zero);

        agent.Motor.Handler.Move.FrameVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AirborneAgent_When_FlyingDownward_Then_ShouldNotEnterFallState()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Down;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        agent.Position.y.Should().BeLessThan((Fixed64)10);
        agent.Motor.Handler.Fly.IsFlying.Should().BeTrue();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void Given_FlyingAgent_When_FlightRequestStops_Then_ShouldTransitionIntoFall()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        agent.Motor.Handler.Fly.GravityCompensation = Fixed64.One;

        TrailblazerManager.Simulate();
        agent.FrameRequest.IsRequestingFlight = true;
        agent.Simulate();

        agent.Motor.Handler.Fly.IsFlying.Should().BeTrue();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();

        TrailblazerManager.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fly.IsFlying.Should().BeFalse();
        agent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void Given_FlyingAgent_When_QueryingHorizontalFlightSpeed_Then_ShouldUseFlightSpeedLimits()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        agent.Motor.Handler.Fly.IsFlying = true;
        agent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)7;

        Fixed64 speed = agent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Fast);

        speed.Should().Be((Fixed64)7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_FlyLocomotion_ShouldPreserveConfigurationAndRuntimeState(bool useMemoryPack)
    {
        var source = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        source.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)3;
        source.Motor.Handler.Fly.MaxAscendSpeed = (Fixed64)2;
        source.Motor.Handler.Fly.MaxDescendSpeed = (Fixed64)1.5f;
        source.Motor.Handler.Fly.MaxFlyAcceleration = (Fixed64)24;
        source.Motor.Handler.Fly.GravityCompensation = (Fixed64)0.75f;
        source.Motor.Handler.Fly.IsFlying = true;

        object payload = SerializeRecord(source.Motor, useMemoryPack);

        var target = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: Vector3d.Zero,
            startingMedium: TraversalMedium.Solid);
        PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.Fly.Should().NotBeNull();
        target.Motor.Handler.Fly.MaxFlySpeed.Should().Be((Fixed64)3);
        target.Motor.Handler.Fly.MaxAscendSpeed.Should().Be((Fixed64)2);
        target.Motor.Handler.Fly.MaxDescendSpeed.Should().Be((Fixed64)1.5f);
        target.Motor.Handler.Fly.MaxFlyAcceleration.Should().Be((Fixed64)24);
        target.Motor.Handler.Fly.GravityCompensation.Should().Be((Fixed64)0.75f);
        target.Motor.Handler.Fly.IsFlying.Should().BeTrue();
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
}
