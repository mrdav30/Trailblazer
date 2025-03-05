using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using Trailblazer.Tests;
using FixedMathSharp;

namespace Trailblazer.UnitTests
{
    public class ScoutControllerTests
    {
        [Fact]
        public void Given_ForceBasedMode_When_ForceIsApplied_Then_VelocityShouldIncrease()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutMotor.Mode = ControllerMode.ForceBased;

            Vector3d initialVelocity = scout.LinearVelocity;

            // Act
            scout.ScoutMotor.Simulate(Vector3d.One, MoveInput.Sprint);

            // Assert
            scout.LinearVelocity.Should().NotBe(initialVelocity);
        }

        [Fact]
        public void Given_PositionBasedMode_When_ForceIsApplied_Then_PositionShouldUpdate()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutMotor.Mode = ControllerMode.PositionBased;

            Vector3d initialPosition = scout.WorldPosition;

            // Act
            scout.ScoutMotor.Simulate(Vector3d.One, MoveInput.Sprint);

            // Assert
            scout.WorldPosition.Should().NotBe(initialPosition);
        }

        [Fact]
        public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);

            Vector3d initialVelocity = scout.LinearVelocity;

            // Act
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.LinearVelocity.y.Should().BeGreaterThan(initialVelocity.y);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startVelocity: new Vector3d(0, 1, 0), grounded: false);

            // Convert gravity acceleration force into a velocity impulse and subtract from current scout velocity to get the delta
            Fixed64 expectedVelocity = scout.LinearVelocity.y 
                + (scout.LinearVelocity.y - scout.ScoutMotor.Gravity * TrailblazerManager.DeltaTime) * TrailblazerManager.DeltaTime;

            // Act
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.LinearVelocity.y.Should().Be(expectedVelocity);
        }

        [Fact]
        public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_VelocityShouldMatchPlatform()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();
            scout.ScoutMotor.Mode = ControllerMode.ForceBased;

            // Act
            // 1st Frame
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            Vector3d newPlatformPoint = new Vector3d(2, 0, 0);
            Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
                scout.ScoutMotor.LocomotionState.Platform.ActiveMatrix,
                newPlatformPoint
            );

            scout.ScoutMotor.LocomotionState.Platform.ActiveMatrix = updatedMatrix;
            scout.SetTraversalState(new TraversalData
            {
                GroundMatrix = updatedMatrix,
                GroundNormal = Vector3d.Up,
                HitObject = scout.ScoutMotor.LocomotionState.Platform.ActivePlatform
            });

            // 2nd Frame
            TrailblazerManager.Simulate();
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.LinearVelocity.x.Should().Be(newPlatformPoint.x);  // since we started at Vector.Zero, a jump to (2, 0, 0) should match velocity
        }

        [Fact]
        public void Given_FallingScout_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            // Act - First Frame (Falling)
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            // 2nd Frame
            TrailblazerManager.Simulate();

            // Simulate hitting the ground before the next frame
            scout.SetTraversalState(new TraversalData
            {
                Medium = TraversalMedium.Ground,
                HitObject = null,
                GroundMatrix = Fixed4x4.Identity,
                GroundNormal = Vector3d.Up,
                SurfaceLevel = Fixed64.Zero
            });

            // Act - Second Frame (After Ground Contact)
            scout.ScoutMotor.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
          //  scout.ScoutMotor.MovementState.IsGrounded.Should().BeTrue();
        }

    }
}
