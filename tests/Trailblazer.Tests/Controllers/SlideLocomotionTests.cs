using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Slow);
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Slow);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }
    }
}
