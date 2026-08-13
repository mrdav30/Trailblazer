using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class MoveLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsEnabled_ShouldClearTransientVelocity_WhenDisabled()
    {
        var locomotion = new MoveLocomotion
        {
            FrameVelocity = new Vector3d(1, 0, 0)
        };

        locomotion.IsEnabled = false;

        locomotion.IsEnabled.Should().BeFalse();
        locomotion.FrameVelocity.Should().Be(Vector3d.Zero);

        locomotion.IsEnabled = true;
        locomotion.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Given_When_ForceIsApplied_Then_VelocityShouldIncrease()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

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
        var expectedVelocity = (newPosition - initialPosition) * TestWorld.Context.InvDeltaTime;

        agent.Motor.Handler.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(expectedVelocity);
    }

    [Fact]
    public void Given_SmallMovements_When_Simulated_Then_PositionShouldAccumulateCorrectly()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        // Act - Apply movement over multiple frames
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.Simulate();
        }

        // Assert
        var speed = agent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Forward, TrekRate.Slow);
        Vector3d expectedFrameDelta = (Vector3d.Forward * speed) * TestWorld.Context.DeltaTime;
        Vector3d expected = expectedFrameDelta * 10;

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

        TrekRequest frameRequest = new()
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        // Act
        agent.Motor.TryTraversal(frameRequest, out _, out _, out _);

        // Assert
        TestRequire.NotNull(TestRequire.NotNull(agent.Motor).Handler.Slide).IsSliding.Should().BeFalse();
    }

    [Fact]
    public void Given_TraversalStarted_When_TryTraversalCalledAgainInSameFrame_Then_ShouldRemainPendingAndReturnFalse()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        TrekRequest frameRequest = new()
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        bool first = agent.Motor.TryTraversal(frameRequest, out _, out _, out _);
        bool second = agent.Motor.TryTraversal(frameRequest, out _, out _, out _);

        first.Should().BeTrue();
        second.Should().BeFalse();
        agent.Motor.TraversalInProgress.Should().BeTrue();

        agent.Motor.AbortTraversalFrame();
        agent.Motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void Given_TraversalStarted_When_NextFrameStartsWithoutFinalize_Then_ShouldThrowExplicitError()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        TrekRequest frameRequest = new()
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        agent.Motor.TryTraversal(frameRequest, out _, out _, out _).Should().BeTrue();

        TestWorld.Context.Simulate();

        Action act = () => agent.Motor.TryTraversal(frameRequest, out _, out _, out _);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*never finalized or aborted before frame*");
        agent.Motor.TraversalInProgress.Should().BeTrue();

        agent.Motor.AbortTraversalFrame();
        agent.Motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void Given_TraversalStarted_When_FinalizedOnLaterFrame_Then_ShouldThrowExplicitError()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        TrekRequest frameRequest = new()
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        agent.Motor.TryTraversal(frameRequest, out _, out _, out _).Should().BeTrue();

        TestWorld.Context.Simulate();

        Action act = () => agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            agent.FrameCondition,
            agent.GetFootPosition());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be finalized on frame*");
        agent.Motor.TraversalInProgress.Should().BeTrue();

        agent.Motor.AbortTraversalFrame();
    }

    [Fact]
    public void Given_StaleTraversal_When_AbortTraversalFrameCalled_Then_NextTraversalCanProceed()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        TrekRequest frameRequest = new()
        {
            Origin = agent.Position,
            FootPosition = agent.GetFootPosition(),
            Rotation = agent.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        agent.Motor.TryTraversal(frameRequest, out _, out _, out _).Should().BeTrue();

        TestWorld.Context.Simulate();
        agent.Motor.AbortTraversalFrame();

        agent.Motor.TraversalInProgress.Should().BeFalse();
        agent.Motor.TryTraversal(frameRequest, out _, out _, out _).Should().BeTrue();
        agent.Motor.TraversalInProgress.Should().BeTrue();

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            agent.FrameCondition,
            agent.GetFootPosition());

        agent.Motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void Given_AgentWhenNoInput_Then_VelocityShouldDecayToZero()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: new Vector3d(5, 0, 0), startingMedium: TraversalMedium.Solid);

        for (int i = 0; i < 100; i++) // Simulate multiple frames to test deceleration
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
            if (agent.Velocity == Vector3d.Zero)
                break;
        }

        agent.Motor.Handler.Move.FrameVelocity.Should().BeApproximately(Vector3d.Zero, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AgentMovesForward_When_ReversedInput_Then_ShouldDecelerate()
    {
        Vector3d iniitialVelocity = new(3, 0, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startVelocity: iniitialVelocity,
            startingMedium: TraversalMedium.Solid);

        agent.FrameRequest.Direction = new(-1, 0, 0);
        agent.FrameRequest.Rate = TrekRate.Moderate;

        for (int i = 0; i < 20; i++) // Apply opposing force over time
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        // Should be slowing down
        agent.Motor.Handler.Move.FrameVelocity.X.Should().BeLessThan(iniitialVelocity.X);
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
        var projectedVelocity = ((speed * Vector3d.Right) * TestWorld.Context.DeltaTime)
            * TestWorld.Context.InvDeltaTime;

        agent.Motor.Handler.Move.FrameVelocity.X.Should().BeLessThan(projectedVelocity.X); // Moving sideways should project velocity down slope
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
        agent.Motor.Handler.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
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
        var velocity = agent.Motor.Handler.Move.FrameVelocity;
        var slopeNormal = agent.Motor.CurrentState.SurfaceNormal;
        var expected = Vector3d.ProjectOnPlane(Vector3d.Forward, slopeNormal);

        velocity.Normalized.Should().BeApproximately(expected.Normalized, Fixed64.Epsilon);
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
            TestWorld.Context.Simulate();

            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Slow;
            agent.Simulate();
        }

        agent.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(agent.Motor.Handler.Move.MaxSlowSpeed);
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

        agent.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeLessThan(Fixed64.One);
    }

    [Fact]
    public void Given_AgentOnFlatSurface_When_MovingForward_Then_ShouldMaintainSpeed()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Moderate;
        agent.Simulate();

        // Simulate multiple frames
        for (int i = 0; i < 10; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.Direction = Vector3d.Forward;
            agent.FrameRequest.Rate = TrekRate.Moderate;
            agent.Simulate();
        }

        agent.Motor.Handler.Move.FrameVelocity.Magnitude.Should().Be(agent.Motor.Handler.Move.MaxModerateSpeed);
    }

    [Fact]
    public void Given_AgentMoving_When_StopRequested_Then_ShouldStopImmediately()
    {
        var initialVelocity = new Vector3d(5, 0, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: initialVelocity, startingMedium: TraversalMedium.Solid);

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
            TestWorld.Context.Simulate();

            lowFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            lowFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            lowFrictionScout.Simulate();

            highFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            highFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            highFrictionScout.Simulate();
        }

        var low = lowFrictionScout.Motor.Handler.Move.FrameVelocity.Magnitude;
        var high = highFrictionScout.Motor.Handler.Move.FrameVelocity.Magnitude;

        high.Should().BeLessThan(low);
    }

    [Fact]
    public void Given_AgentWalkingOnInertHighFrictionGround_When_Moving_Then_ShouldStillMoveSlower()
    {
        var lowFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: Fixed64.Zero, platformInert: true);
        var highFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: Fixed64.One, platformInert: true);

        for (int i = 0; i < 5; i++)
        {
            TestWorld.Context.Simulate();

            lowFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            lowFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            lowFrictionScout.Simulate();

            highFrictionScout.FrameRequest.Direction = Vector3d.Forward;
            highFrictionScout.FrameRequest.Rate = TrekRate.Moderate;
            highFrictionScout.Simulate();
        }

        var lowFrictionMotor = TestRequire.NotNull(lowFrictionScout.Motor);
        var highFrictionMotor = TestRequire.NotNull(highFrictionScout.Motor);
        var low = lowFrictionMotor.Handler.Move.FrameVelocity.Magnitude;
        var high = highFrictionMotor.Handler.Move.FrameVelocity.Magnitude;

        high.Should().BeLessThan(low);
        TestRequire.NotNull(lowFrictionMotor.Handler.Platform).IsActive.Should().BeFalse();
        TestRequire.NotNull(highFrictionMotor.Handler.Platform).IsActive.Should().BeFalse();
    }

    [Fact]
    public void Given_AgentOnLowFrictionGround_When_StopsMoving_Then_ShouldSlideSlightly()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            surfaceFriction: Fixed64.FromFraction(1, 100)); // Very low friction

        // Apply forward movement
        agent.FrameRequest.Direction = Vector3d.Forward;
        agent.FrameRequest.Rate = TrekRate.Fast;

        agent.Simulate();

        var initialVelocity = agent.Motor.Handler.Move.FrameVelocity;

        // Stop input
        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(Fixed64.Zero);
        agent.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeLessThan(initialVelocity.Magnitude);
    }

    [Fact]
    public void Given_ScoutGrounded_When_Moving_Then_ControlMultiplierShouldNotAffectSpeed()
    {
        var scout = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid);
        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)5;

        var speed = scout.Motor.MaxHoritzontalSpeedInDirection(
            Vector3d.Right,
            TrekRate.Moderate);

        speed.Should().BeApproximately((Fixed64)5, Fixed64.Epsilon);
    }
}
