using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

namespace Trailblazer.Tests.Controllers
{
    [Collection("TrailblazerCollection")]
    public class FallLocomotionTests
    {
        [Fact]
        public void Given_FallingScout_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.ScoutController.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.ScoutController.Traverse(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_FallingScout_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            // Act - First Frame (Falling)
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.IsInAir.Should().BeTrue();

            // Simulate hitting the ground before the next frame
            scout.SetTraversalCondition(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new SurfaceCondition
                {
                    SurfaceObject = null,
                    SurfaceMatrix = Fixed4x4.Identity,
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();

            // Act - Second Frame (After Ground Contact)
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.IsGrounded.Should().BeTrue();
        }

        [Fact]
        public void Given_AirborneScout_When_SimulatedOverMultipleFrames_Then_VelocityShouldMatchGravity()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(
                startPosition: new Vector3d(0, 100, 0),
                startingMedium: TraversalMedium.Air
            );
            Vector3d expectedVelocity = Vector3d.Zero;  // store impulse-based velocity change per frame

            // Act - Simulate falling for 5 frames
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();

                // Calculate expected velocity update from gravity impulse
                expectedVelocity.y += -scout.ScoutController.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;
            }

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutInAir_When_NoMovement_Then_ShouldFallNaturally()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var scout = IScoutTestFactory.CreateMockScout(startPosition: initialPosition, startingMedium: TraversalMedium.Air);

            for (int i = 0; i < 20; i++) // Simulate multiple frames
            {
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.WorldPosition.y.Should().BeLessThan(initialPosition.y); // Should be falling
        }

        [Fact]
        public void Given_ScoutInAir_When_MovesForward_Then_ShouldStillBeAffectedByGravity()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var scout = IScoutTestFactory.CreateFallingScout(startPosition: initialPosition);

            for (int i = 0; i < 20; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(new Vector3d(1, 0, 0), TraversalSpeed.Moderate);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.WorldPosition.y.Should().BeLessThan(initialPosition.y); // Gravity should still apply
            scout.WorldPosition.x.Should().BeGreaterThan(Fixed64.Zero); // Should also move forward
        }
    }
}
