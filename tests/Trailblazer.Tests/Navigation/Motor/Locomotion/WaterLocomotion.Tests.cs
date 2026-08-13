using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class WaterLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_ScoutAtNeutralBuoyancy_When_Simulated_Then_ShouldRemainSuspended()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: (Fixed64)99);
        var swim = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water);

        swim.IsEnabled = true;
        swim.BuoyancyFactor = Fixed64.One; // Neutral buoyancy

        // Act - Simulate multiple frames
        Fixed64 initialY = agent.Position.Y;
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        // Assert - Position should remain stable within a small range
        agent.Position.Y.Should().BeApproximately(
            initialY,
            Fixed64.FromRaw(0x00001000)); // Small tolerance
    }

    [Fact]
    public void Given_ScoutEntersWater_When_SwimIsRequested_Then_ShouldTransitionToSwimming()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        // Act - First frame, still on ground
        TestWorld.Context.Simulate();
        agent.Simulate();

        // 2nd Frame - Enter Water
        TestWorld.Context.Simulate();

        agent.FrameCondition.Medium = TraversalMedium.Liquid;
        agent.FrameCondition.SurfaceLevel = agent.Position.Y;
        agent.FrameRequest.IsRequestingSwim = true;

        agent.Simulate();

        // Assert
        TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water).IsSwimming.Should().BeTrue();
    }

    [Fact]
    public void Given_ScoutEntersWater_When_SwimIsNotRequested_Then_ShouldRemainOutOfSwimming()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        TestWorld.Context.Simulate();
        agent.Simulate();

        TestWorld.Context.Simulate();
        agent.FrameCondition.Medium = TraversalMedium.Liquid;
        agent.FrameCondition.SurfaceLevel = agent.Position.Y;
        agent.Simulate();

        TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water).IsSwimming.Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);

        agent.FrameRequest.IsRequestingSwim = true;
        agent.Simulate();

        // Act - Exit water

        TestWorld.Context.Simulate();

        agent.FrameCondition.Medium = TraversalMedium.Solid;

        agent.Simulate();

        // Assert
        TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water).IsSwimming.Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutInWater_When_Simulated_Then_ShouldApplyWaterDrag()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        var move = motor.Handler.Move;
        swim.IsEnabled = true;

        // Act - Enter Water
        TestWorld.Context.Simulate();
        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Slow;
        agent.FrameRequest.IsRequestingSwim = true;
        agent.Simulate();

        // Act - Simulate 3 Frames
        for (int i = 0; i < 3; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.FrameRequest.IsRequestingSwim = true;
            agent.Simulate();
        }

        // Assert
        // calculate what the velocity should without drag
        Fixed3x3 transposedMatrix = agent.Rotation.ToMatrix3x3();
        Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, Vector3d.Forward);
        Fixed64 speed = motor.MaxHoritzontalSpeedInDirection(desiredLocalDirection, TrekRate.Slow);
        Vector3d expectedVelocity = transposedMatrix * (desiredLocalDirection * speed);

        move.FrameVelocity.Magnitude.Should().BeLessThan(expectedVelocity.Magnitude);
    }

    [Fact]
    public void Given_ScoutAtWaterSurface_When_Simulated_Then_ShouldExperienceBuoyancyForces()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);
        TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water).IsEnabled = true;

        // Act - Simulate entry into water
        TestWorld.Context.Simulate();
        agent.Simulate();

        Fixed64 previousY = agent.Position.Y;

        // Simulate multiple frames of floating
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        var tolerance = Fixed64.FromRaw(0x0800);
        // Assert
        agent.Position.Y.Should().BeApproximately(previousY, tolerance); // Allow some small float oscillation
    }

    [Fact]
    public void Given_ScoutWithPositiveBuoyancy_When_Simulated_Then_ShouldFloatUp()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(startPosition: Vector3d.Down * 5);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        var move = motor.Handler.Move;

        swim.IsEnabled = true;
        swim.BuoyancyFactor = Fixed64.FromRaw(0x180000000L); // ~1.5, meaning agent is more buoyant

        // Act - Simulate multiple frames
        Fixed64 initialY = agent.Position.Y;
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
            if (agent.Position.Y == Fixed64.Zero) // we hit the surface
                break;
        }

        // Assert - Scout should float higher
        agent.Position.Y.Should().BeGreaterThan(initialY);
        move.FrameVelocity.Y.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutWithLowBuoyancy_When_Simulated_Then_ShouldSink()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(startPosition: Vector3d.Down);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        var move = motor.Handler.Move;

        swim.IsEnabled = true;
        swim.BuoyancyFactor = Fixed64.Half; // ~0.5, meaning agent is heavier than water

        // Act - Simulate multiple frames
        Fixed64 initialY = agent.Position.Y;
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        // Assert - Scout should sink lower
        agent.Position.Y.Should().BeLessThan(initialY);
        move.FrameVelocity.Y.Should().BeLessThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutResurfacesFromDive_When_BreathWasLow_Then_ShouldRegenerateBreath()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent();
        var swim = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water);
        swim.UnderwaterTimer = (Fixed64)30;

        // Simulate resurfacing
        swim.IsDiving = false;

        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        swim.UnderwaterTimer.Should().BeLessThan((Fixed64)30);
    }

    [Fact]
    public void Given_DrowningDisabled_When_UnderwaterLong_Then_ShouldNotDrown()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent();
        var swim = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water);
        swim.CanDrown = false;
        swim.HoldBreathTime = Fixed64.One;
        swim.UnderwaterTimer = Fixed64.One + (Fixed64)2;

        TestWorld.Context.Simulate();
        agent.Simulate();

        swim.IsDrowning.Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutDiving_When_MovesUp_Then_ShouldSwimUpward()
    {
        var initialPosition = new Vector3d(0, -2, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: initialPosition,
            startingMedium: TraversalMedium.Liquid);

        for (int i = 0; i < 10; i++) // Simulate swimming upwards
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.Direction = Vector3d.Up;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.FrameRequest.IsRequestingSwim = true;
            agent.Simulate();
        }

        agent.Position.Y.Should().BeGreaterThan(initialPosition.Y); // Should rise
    }

    [Fact]
    public void Given_ScoutUnderwater_When_OutOfBreath_Then_ShouldTriggerDrowning()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(0, -5, 0), startingMedium: TraversalMedium.Liquid);
        var swim = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water);

        swim.HoldBreathTime = (Fixed64)3;
        swim.CanDrown = true;

        for (int i = 0; i < 100; i++) // Simulate prolonged underwater time
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        swim.IsDrowning.Should().BeTrue();
    }

    [Fact]
    public void Given_SwimmingScout_When_JumpRequested_Then_ShouldBreachWater()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var move = motor.Handler.Move;
        swim.CanBreachWater = true;

        bool breached = false;
        agent.Motor.Events.OnStartWaterBreach += () => breached = true;

        // Request a jump while swimming

        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingSwim = true;
        agent.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        agent.Simulate();

        jump.IsJumping.Should().BeTrue();
        breached.Should().BeTrue();
        move.FrameVelocity.Y.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_SwimmingScout_When_JumpRequestedButBreachDisabled_Then_ShouldNotJump()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var move = motor.Handler.Move;
        swim.CanBreachWater = false;

        bool breached = false;
        agent.Motor.Events.OnStartWaterBreach += () => breached = true;

        // Request a jump while swimming, but breach is disabled
        agent.FrameRequest.IsRequestingSwim = true;
        agent.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        agent.Simulate();

        jump.IsJumping.Should().BeFalse();
        breached.Should().BeFalse();
        move.FrameVelocity.Y.Should().BeLessThanOrEqualTo(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutBreachesWater_When_ExitsWater_Then_ShouldStopSwimmingAndTriggerStopBreach()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        var motor = TestRequire.NotNull(agent.Motor);
        var swim = TestRequire.NotNull(motor.Handler.Water);
        swim.CanBreachWater = true;

        bool stopBreach = false;
        agent.Motor.Events.OnStopWaterBreach += () => stopBreach = true;

        // Simulate a jump breach
        agent.FrameRequest.IsRequestingSwim = true;
        agent.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        agent.Simulate();

        for (int i = 0; i < 32; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.IsRequestingSwim = true;
            agent.Simulate();
            if (agent.FrameCondition.Medium == TraversalMedium.Liquid)
                break;
        }

        motor.IsInLiquid.Should().BeTrue();
        swim.IsSwimming.Should().BeTrue();
        stopBreach.Should().BeTrue();
    }

    [Fact]
    public void Given_SwimmingScout_When_SwimIsDisabled_Then_ShouldClearTransientState()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);
        var swim = TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Water);
        swim.IsEnabled = true;
        swim.IsSwimming = true;
        swim.IsDiving = true;

        // Disabling swim should clear transient state (exercises the !_isEnabled branch at IsEnabled set)
        swim.IsEnabled = false;

        swim.IsSwimming.Should().BeFalse();
        swim.IsDiving.Should().BeFalse();
    }
}
