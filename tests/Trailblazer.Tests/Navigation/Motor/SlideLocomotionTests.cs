using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class SlideLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_ScoutOnSteepSlope_When_Moving_Then_ShouldSlideDown()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right * Fixed64.Half, (Fixed64)0.85)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        TrekRequest frameRequest = new()
        {
            Origin = scout.Position,
            Rotation = scout.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        // Act
        scout.Motor.TryTraversal(frameRequest, out _, out _, out _);
        scout.Motor.FinalizeTraversal(scout.Position, scout.LastPosition, scout.Rotation, scout.FrameCondition, scout.GetFootPosition());

        // Assert
        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();
    }

    [Fact]
    public void Given_ShallowSlope_When_ScoutMovesOntoIt_Then_ShouldNotSlide()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            startPosition: Vector3d.Zero,
            platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Forward, FixedMath.Atan(Fixed64.FromRaw(0x08000000L)))
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform
        );

        TrekRequest frameRequest = new()
        {
            Origin = scout.Position,
            Rotation = scout.Rotation,
            Direction = Vector3d.Forward,
            Rate = TrekRate.Slow
        };

        // Act
        scout.Motor.TryTraversal(frameRequest, out _, out _, out _);

        // Assert
        scout.Motor.Handler.Slide.IsSliding.Should().BeFalse();
    }


    [Fact]
    public void Given_ScoutOnSteepSlope_When_NoInput_Then_ShouldStillSlide()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);

        // No movement input
        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.Simulate();
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutSliding_When_FrictionIsHigh_Then_ShouldReduceSpeed()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.One); // max friction

        var request = new TrekRequest
        {
            Origin = scout.Position,
            Rotation = scout.Rotation,
            Direction = Vector3d.Zero,
            Rate = TrekRate.Stationary
        };

        // Simulate several frames to allow friction to take effect
        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.Motor.TryTraversal(request, out _, out _, out _);
            scout.Motor.FinalizeTraversal(scout.Position, scout.LastPosition, scout.Rotation, scout.FrameCondition, Vector3d.Zero);

            request.Origin = scout.Position;
            request.Rotation = scout.Rotation;
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Magnitude.Should().BeLessThan((Fixed64)1);
    }

    [Fact]
    public void Given_ScoutOnHighFrictionDownSlope_When_Sliding_Then_ShouldSlideSlowerThanLowFriction()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)50);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

        var lowFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.Zero);
        var highFrictionScout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.One);

        for (int i = 0; i < 5; i++)
        {
            TrailblazerManager.Simulate();

            lowFrictionScout.Simulate();
            highFrictionScout.Simulate();
        }

        var low = lowFrictionScout.Motor.Handler.Move.FrameVelocity.Magnitude;
        var high = highFrictionScout.Motor.Handler.Move.FrameVelocity.Magnitude;

        lowFrictionScout.Motor.Handler.Slide.IsSliding.Should().BeTrue();
        highFrictionScout.Motor.Handler.Slide.IsSliding.Should().BeTrue();

        high.Should().BeLessThan(low);
    }

    [Fact]
    public void Given_ScoutStartsSliding_When_SlopeBecomesShallow_Then_ShouldStopSliding()
    {
        var steepSlope = FixedMath.DegToRad((Fixed64)60);
        var shallowSlope = FixedMath.DegToRad((Fixed64)10);

        // Start on steep slope
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(steepSlope, Fixed64.Zero, Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);
        scout.Motor.Handler.Move.SlopeLimit = (Fixed64)45;

        // Simulate sliding for a few frames
        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.Simulate();
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();

        GroundCondition shallowSlopeSurface = new()
        {
            Platform = new PlatformSnapshot(1, Fixed4x4.CreateRotation(FixedQuaternion.FromEulerAngles(shallowSlope, Fixed64.Zero, Fixed64.Zero)))
        };

        // Flatten slope
        scout.FrameCondition.Medium = TraversalMedium.Ground;
        scout.FrameCondition.GroundState = shallowSlopeSurface;

        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.Simulate();
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutSliding_When_SidewaysInput_Then_ShouldInfluenceDirection()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);

        scout.Motor.Handler.Move.SlopeLimit = (Fixed64)45;
        scout.Motor.Handler.Slide.SidewaysControl = (Fixed64)1;

        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.FrameRequest.Direction = Vector3d.Right;
            scout.FrameRequest.Rate = TrekRate.Slow;
            scout.Simulate();
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();

        var velocity = scout.Motor.Handler.Move.FrameVelocity;
        velocity.x.Should().NotBe(Fixed64.Zero, "Sideways input should influence sliding direction");
    }

    [Fact]
    public void Given_ScoutFallsOntoSteepSlope_When_Lands_Then_ShouldStartSliding()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(
                slopeAngle,
                Fixed64.Zero,
                Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startPosition: new Vector3d(0, 2, 0),
            surfaceLevel: Fixed64.Zero,
            platformMatrix: platform);

        for (int i = 0; i < 32; i++)
        {
            TrailblazerManager.Simulate();
            scout.Simulate();
            if (scout.Motor.IsGrounded)
                break;
        }

        // simulate 1 more frame to capture grounded slope
        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Motor.IsGrounded.Should().BeTrue();
        scout.Motor.Handler.Slide.IsSliding.Should().BeTrue();
    }

    [Fact]
    public void Given_SlideLocomotionDisabled_When_OnSteepSlope_Then_ShouldNotSlide()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
        );

        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);
        scout.Motor.Handler.Slide.IsEnabled = false;

        for (int i = 0; i < 3; i++)
        {
            TrailblazerManager.Simulate();
            scout.Simulate();
        }

        scout.Motor.Handler.Slide.IsSliding.Should().BeFalse();
    }
}
