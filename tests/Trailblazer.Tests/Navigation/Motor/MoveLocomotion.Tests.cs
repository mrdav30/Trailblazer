using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class MoveLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_When_ForceIsApplied_Then_VelocityShouldIncrease()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

        Vector3d initialPosition = agent.Position;
        agent.FrameRequest = new()
        {
            Origin = agent.Position,
            Rotation = agent.Rotation,
            Direction = Vector3d.One,
            Rate = TrekRate.Fast
        };

        // Act
        agent.Simulate();

        // Assert
        Vector3d newPosition = agent.Position;
        var expectedVelocity = (newPosition - initialPosition) * TrailblazerManager.InvDeltaTime;

        agent.Motor.Locomotions.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
        agent.Motor.Locomotions.Move.FrameVelocity.Should().Be(expectedVelocity);
    }

    [Fact]
    public void Given_SmallMovements_When_Simulated_Then_PositionShouldAccumulateCorrectly()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

        // Act - Apply movement over multiple frames
        for (int i = 0; i < 10; i++)
        {
            TrailblazerManager.Simulate();
            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.Simulate();
        }

        // Assert
        var speed = agent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Forward, TrekRate.Slow);
        var expected = ((Vector3d.Forward * speed) * 10) * TrailblazerManager.DeltaTime;

        agent.Position.Should().Be(expected);
    }

    [Fact]
    public void Given_AgentOnMaxWalkableSlope_When_Moving_Then_ShouldStayGrounded()
    {
        // Arrange
        var slopeLimit = Fixed64.FromRaw(0xB2B8C75C); // 2998454108L, converts to ~0.698131999932 radians or ~40 degrees; 
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right, slopeLimit)
        );

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        TrekRequest frameRequest = new TrekRequest
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        // Act
        agent.Motor.Traverse(frameRequest);

        // Assert
        agent.Motor.Locomotions.Slide.IsSliding.Should().BeFalse();
    }

    [Fact]
    public void Given_AgentWhenNoInput_Then_VelocityShouldDecayToZero()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: new Vector3d(5, 0, 0), startingMedium: TraversalMedium.Ground);

        for (int i = 0; i < 100; i++) // Simulate multiple frames to test deceleration
        {
            TrailblazerManager.Simulate();
            agent.Simulate();
            if (agent.Velocity == Vector3d.Zero)
                break;
        }

        agent.Motor.Locomotions.Move.FrameVelocity.Should().BeApproximately(Vector3d.Zero, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AgentMovesForward_When_ReversedInput_Then_ShouldDecelerate()
    {
        Vector3d iniitialVelocity = new(3, 0, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startVelocity: iniitialVelocity,
            startingMedium: TraversalMedium.Ground);

        agent.FrameRequest.Direction = new(-1, 0, 0);
        agent.FrameRequest.Rate = TrekRate.Moderate;

        for (int i = 0; i < 20; i++) // Apply opposing force over time
        {
            TrailblazerManager.Simulate();
            agent.Simulate();
        }

        // Should be slowing down
        agent.Motor.Locomotions.Move.FrameVelocity.x.Should().BeLessThan(iniitialVelocity.x);
    }

    [Fact]
    public void Given_AgentOnSlope_When_MovingSideways_Then_VelocityShouldAdjustToSlope()
    {
        var slope = FixedMath.DegToRad((Fixed64)30);
        var ground = Fixed4x4.CreateRotation(FixedQuaternion.FromEulerAngles(slope, Fixed64.Zero, Fixed64.Zero));
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: ground);

        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Slow;

        agent.Simulate();

        // calculate speed without slopespeed modifier
        var speed = agent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Slow);
        var projectedVelocity = ((speed * Vector3d.Right) * TrailblazerManager.DeltaTime)
            * TrailblazerManager.InvDeltaTime;

        agent.Motor.Locomotions.Move.FrameVelocity.x.Should().BeLessThan(projectedVelocity.x); // Moving sideways should project velocity down slope
    }


    [Fact]
    public void Given_AgentOnSlope_When_Simulated_Then_VelocityShouldAlignWithSlope()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromAxisAngle(
                Vector3d.Right,
                Fixed64.FromRaw(0x10000000L))
        );

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        agent.FrameRequest = new()
        {
            Origin = agent.Position,
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        // Act
        agent.Simulate();

        // Assert
        agent.Motor.Locomotions.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
    }

    [Fact]
    public void Given_AgentOnSlope_When_Simulated_Then_VelocityShouldBeProjectedOntoSlope()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right, Fixed64.FromRaw(0x10000000L)) // Shallow slope
        );

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Slow;

        // Act
        agent.Simulate();

        // Assert - Projected vector must lie in the tangent plane of the slope
        var velocity = agent.Motor.Locomotions.Move.FrameVelocity;
        var slopeNormal = agent.Motor.CurrentState.SurfaceNormal;
        var expected = Vector3d.ProjectOnPlane(Vector3d.Forward, slopeNormal);

        // Ensure downward movement on downhill slopes & upward movement on uphill slopes
        if (Fixed64.Sign(expected.y) != Fixed64.Sign(agent.Motor.FrameSlopeAngle))
            expected.y *= -1;

        velocity.Normal.Should().BeApproximately(expected.Normal, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AgentOnDownhillSlope_When_MovingDownhill_Then_ShouldAccelerate()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)30);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        for (int i = 0; i < 10; i++)
        {
            TrailblazerManager.Simulate();

            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.Simulate();
        }

        agent.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(agent.Motor.Locomotions.Move.MaxSlowSpeed);
    }

    [Fact]
    public void Given_AgentOnUphillSlope_When_MovingUphill_Then_ShouldDecelerate()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)30);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromEulerAngles(-slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Moderate;

        agent.Simulate();

        agent.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeLessThan(Fixed64.One);
    }

    [Fact]
    public void Given_AgentOnFlatSurface_When_MovingForward_Then_ShouldMaintainSpeed()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Moderate;
        agent.Simulate();

        // Simulate multiple frames
        for (int i = 0; i < 10; i++)
        {
            TrailblazerManager.Simulate();
            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Moderate;
            agent.Simulate();
        }

        agent.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().Be(agent.Motor.Locomotions.Move.MaxModerateSpeed);
    }

    [Fact]
    public void Given_AgentMoving_When_StopRequested_Then_ShouldStopImmediately()
    {
        var initialVelocity = new Vector3d(5, 0, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: initialVelocity, startingMedium: TraversalMedium.Ground);

        agent.Simulate();

        Assert.True(agent.Velocity <= initialVelocity && agent.Velocity >= Vector3d.Zero);
    }

    [Fact]
    public void Given_AgentWalkingOnHighFrictionGround_When_Moving_Then_ShouldMoveSlower()
    {
        var lowFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: Fixed64.Zero);
        var highFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: Fixed64.One);

        // Simulate walking forward for both
        for (int i = 0; i < 5; i++)
        {
            TrailblazerManager.Simulate();

            lowFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            lowFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            lowFrictionScout.Simulate();

            highFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            highFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            highFrictionScout.Simulate();
        }

        var low = lowFrictionScout.Motor.Locomotions.Move.FrameVelocity.Magnitude;
        var high = highFrictionScout.Motor.Locomotions.Move.FrameVelocity.Magnitude;

        high.Should().BeLessThan(low);
    }

    [Fact]
    public void Given_AgentOnLowFrictionGround_When_StopsMoving_Then_ShouldSlideSlightly()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            surfaceFriction: Fixed64.Fraction(1, 100)); // Very low friction

        // Apply forward movement
        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Fast;

        agent.Simulate();

        var initialVelocity = agent.Motor.Locomotions.Move.FrameVelocity;

        // Stop input
        TrailblazerManager.Simulate();
        agent.Simulate();

        agent.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(Fixed64.Zero);
        agent.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeLessThan(initialVelocity.Magnitude);
    }

    [Fact]
    public void Given_ScoutGrounded_When_Moving_Then_ControlMultiplierShouldNotAffectSpeed()
    {
        var scout = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Ground);
        scout.Motor.Locomotions.Move.MaxSidewaysSpeed = (Fixed64)5;

        var speed = scout.Motor.MaxHoritzontalSpeedInDirection(
            Vector3d.Right,
            TrekRate.Moderate);

        speed.Should().BeApproximately((Fixed64)5, Fixed64.Epsilon);
    }
}
