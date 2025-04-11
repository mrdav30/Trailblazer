using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;

namespace Trailblazer.Tests.Controllers
{
    [Collection("TrailblazerCollection")]
    public class SlideLocomotionTests
    {
        [Fact]
        public void Given_ScoutOnSteepSlope_When_Moving_Then_ShouldSlideDown()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right * Fixed64.Half, (Fixed64)0.85)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.ScoutController.Traverse(Vector3d.Forward, MovementSpeed.Slow);
            scout.ScoutController.FinishFrameTraversal(scout.TraversalCondition);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();
        }

        [Fact]
        public void Given_ShallowSlope_When_ScoutMovesOntoIt_Then_ShouldNotSlide()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Forward, FixedMath.Atan(Fixed64.FromRaw(0x08000000L)))
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.ScoutController.Traverse(Vector3d.Forward, MovementSpeed.Slow);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }


        [Fact]
        public void Given_ScoutOnSteepSlope_When_NoInput_Then_ShouldStillSlide()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform);

            // No movement input
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutSliding_When_FrictionIsHigh_Then_ShouldReduceSpeed()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, surfaceFriction: Fixed64.One); // max friction

            // Simulate several frames to allow friction to take effect
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.ScoutController.Traverse(Vector3d.Zero, MovementSpeed.Stationary);
                scout.ScoutController.FinishFrameTraversal(scout.TraversalCondition);
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().BeLessThan((Fixed64)1);
        }

        [Fact]
        public void Given_ScoutOnHighFrictionDownSlope_When_Sliding_Then_ShouldSlideSlowerThanLowFriction()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)50);
            var platform = IScoutTestFactory.CreatePlatform(platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var lowFrictionScout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, surfaceFriction: Fixed64.Zero);
            var highFrictionScout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, surfaceFriction: Fixed64.One);

            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();

                lowFrictionScout.StartTraversal();
                highFrictionScout.StartTraversal();

                lowFrictionScout.FinalizeTraversal();
                highFrictionScout.FinalizeTraversal();
            }

            var low = lowFrictionScout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude;
            var high = highFrictionScout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude;

            lowFrictionScout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();
            highFrictionScout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();

            high.Should().BeLessThan(low);
        }

        [Fact]
        public void Given_ScoutStartsSliding_When_SlopeBecomesShallow_Then_ShouldStopSliding()
        {
            var steepSlope = FixedMath.DegToRad((Fixed64)60);
            var shallowSlope = FixedMath.DegToRad((Fixed64)10);

            // Start on steep slope
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(steepSlope, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform);
            scout.ScoutController.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            // Simulate sliding for a few frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();

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
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutSliding_When_SidewaysInput_Then_ShouldInfluenceDirection()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform);

            scout.ScoutController.Locomotions.Slide.SlopeLimit = (Fixed64)45;
            scout.ScoutController.Locomotions.Slide.SidewaysControl = (Fixed64)1;

            // Provide sideways input (e.g. strafe right)
            Vector3d sidewaysInput = Vector3d.Right;

            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(sidewaysInput, MovementSpeed.Slow);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();

            var velocity = scout.ScoutController.Locomotions.Move.CurrentVelocity;
            velocity.x.Should().NotBe(Fixed64.Zero, "Sideways input should influence sliding direction");
        }

        [Fact]
        public void Given_ScoutFallsOntoSteepSlope_When_Lands_Then_ShouldStartSliding()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreateFallingScout(startPosition: new Vector3d(0, 2, 0), surfaceLevel: Fixed64.Zero, platformMatrix: platform);

            for (int i = 0; i < 32; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
                if (scout.ScoutController.IsGrounded)
                    break;
            }

            // simulate 1 more frame to capture grounded slope
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.ScoutController.IsGrounded.Should().BeTrue();
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeTrue();
        }

        [Fact]
        public void Given_SlideLocomotionDisabled_When_OnSteepSlope_Then_ShouldNotSlide()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform);
            scout.ScoutController.Locomotions.Slide.IsEnabled = false;

            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }
    }
}
