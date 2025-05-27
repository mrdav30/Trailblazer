using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class SlideLocomotionTests
    {
        [Fact]
        public void Given_ScoutOnSteepSlope_When_Moving_Then_ShouldSlideDown()
        {
            // Arrange
            var platform = MockAgentTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right * Fixed64.Half, (Fixed64)0.85)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.Motor.Traverse(scout, Vector3d.Forward, TrekRate.Slow);
            scout.Motor.FinalizeTraversal(scout, scout.SurfaceState);

            // Assert
            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
        }

        [Fact]
        public void Given_ShallowSlope_When_ScoutMovesOntoIt_Then_ShouldNotSlide()
        {
            // Arrange
            var platform = MockAgentTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Forward, FixedMath.Atan(Fixed64.FromRaw(0x08000000L)))
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.Motor.Traverse(scout,Vector3d.Forward, TrekRate.Slow);

            // Assert
            scout.Motor.Locomotions.Slide.IsSliding.Should().BeFalse();
        }


        [Fact]
        public void Given_ScoutOnSteepSlope_When_NoInput_Then_ShouldStillSlide()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);

            // No movement input
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
            scout.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutSliding_When_FrictionIsHigh_Then_ShouldReduceSpeed()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.One); // max friction

            // Simulate several frames to allow friction to take effect
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.Motor.Traverse(scout, Vector3d.Zero, TrekRate.Stationary);
                scout.Motor.FinalizeTraversal(scout, scout.SurfaceState);
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
            scout.Motor.Locomotions.Move.FrameVelocity.Magnitude.Should().BeLessThan((Fixed64)1);
        }

        [Fact]
        public void Given_ScoutOnHighFrictionDownSlope_When_Sliding_Then_ShouldSlideSlowerThanLowFriction()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)50);
            var platform = MockAgentTestFactory.CreatePlatform(platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var lowFrictionScout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.Zero);
            var highFrictionScout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, surfaceFriction: Fixed64.One);

            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();

                lowFrictionScout.Simulate();
                highFrictionScout.Simulate();

                lowFrictionScout.CommitFrameMotion();
                highFrictionScout.CommitFrameMotion();
            }

            var low = lowFrictionScout.Motor.Locomotions.Move.FrameVelocity.Magnitude;
            var high = highFrictionScout.Motor.Locomotions.Move.FrameVelocity.Magnitude;

            lowFrictionScout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
            highFrictionScout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();

            high.Should().BeLessThan(low);
        }

        [Fact]
        public void Given_ScoutStartsSliding_When_SlopeBecomesShallow_Then_ShouldStopSliding()
        {
            var steepSlope = FixedMath.DegToRad((Fixed64)60);
            var shallowSlope = FixedMath.DegToRad((Fixed64)10);

            // Start on steep slope
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(steepSlope, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);
            scout.Motor.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            // Simulate sliding for a few frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();

            GroundCondition shallowSlopeSurface = new()
            {
                BaseObject = new object(), // Separate from platform
                GroundMatrix = Fixed4x4.CreateRotation(FixedQuaternion.FromEulerAngles(shallowSlope, Fixed64.Zero, Fixed64.Zero))
            };

            // Flatten slope
            scout.SetTraversalCondition(TraversalMedium.Ground, surfaceCondition: shallowSlopeSurface);

            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutSliding_When_SidewaysInput_Then_ShouldInfluenceDirection()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);

            scout.Motor.Locomotions.Slide.SlopeLimit = (Fixed64)45;
            scout.Motor.Locomotions.Slide.SidewaysControl = (Fixed64)1;

            // Provide sideways input (e.g. strafe right)
            Vector3d sidewaysInput = Vector3d.Right;

            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(direction: sidewaysInput, rate: TrekRate.Slow);
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();

            var velocity = scout.Motor.Locomotions.Move.FrameVelocity;
            velocity.x.Should().NotBe(Fixed64.Zero, "Sideways input should influence sliding direction");
        }

        [Fact]
        public void Given_ScoutFallsOntoSteepSlope_When_Lands_Then_ShouldStartSliding()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreateFallingAgent(startPosition: new Vector3d(0, 2, 0), surfaceLevel: Fixed64.Zero, platformMatrix: platform);

            for (int i = 0; i < 32; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
                if (scout.Motor.IsGrounded)
                    break;
            }

            // simulate 1 more frame to capture grounded slope
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            scout.Motor.IsGrounded.Should().BeTrue();
            scout.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
        }

        [Fact]
        public void Given_SlideLocomotionDisabled_When_OnSteepSlope_Then_ShouldNotSlide()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = MockAgentTestFactory.CreatePlatformAgent(platformMatrix: platform);
            scout.Motor.Locomotions.Slide.IsEnabled = false;

            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Motor.Locomotions.Slide.IsSliding.Should().BeFalse();
        }
    }
}
