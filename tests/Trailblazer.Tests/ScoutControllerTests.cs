using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
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
            scout.ScoutController.Mode = ControllerMode.Force;

            Vector3d initialVelocity = scout.LinearVelocity;

            // Act
            scout.ScoutController.Simulate(Vector3d.One, MoveInput.Sprint);

            // Assert
            scout.LinearVelocity.Should().NotBe(initialVelocity);
        }

        [Fact]
        public void Given_PositionBasedMode_When_ForceIsApplied_Then_PositionShouldUpdate()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutController.Mode = ControllerMode.PositionDelta;

            Vector3d initialPosition = scout.WorldPosition;

            // Act
            scout.ScoutController.Simulate(Vector3d.One, MoveInput.Sprint);

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
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.LinearVelocity.y.Should().BeGreaterThan(initialVelocity.y);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(
                startPosition: Vector3d.Up,
                startVelocity: Vector3d.Up,
                grounded: false);

            Vector3d expectedVelocity = scout.LinearVelocity;
            expectedVelocity.y -= scout.ScoutController.Gravity * TrailblazerManager.DeltaTime;

            // Act
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.LinearVelocity.Should().Be(expectedVelocity);
        }

        [Fact]
        public void Given_FallingScout_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            Vector3d expectedVelocity = scout.LinearVelocity;
            expectedVelocity.y -= scout.ScoutController.Gravity * TrailblazerManager.DeltaTime;

            // Act
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.LinearVelocity.Should().Be(expectedVelocity);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);

            // Act - First Jump
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);
            scout.FinalizeMovement();

            int expectedJumpFrame = scout.ScoutController.Locomotion.Jump.FrameStartJump;

            // Simulate leaving the ground before the next frame
            scout.SetTraversalState(
                TraversalMedium.Air,
                Fixed64.Zero,
                new GroundState
                {
                    HitObject = null,
                    GroundMatrix = Fixed4x4.Identity
                }
            );

            // Attempt to jump again immediately
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);

            // Assert
            scout.ScoutController.IsInAir.Should().BeTrue();
            scout.ScoutController.Locomotion.Jump.IsCoolingDown.Should().BeTrue();
            scout.ScoutController.Locomotion.Jump.FrameStartJump.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_VelocityShouldMatchPlatform()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();
            scout.ScoutController.Mode = ControllerMode.Force;

            // Act
            // 1st Frame
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            Vector3d newPlatformPoint = new Vector3d(2, 0, 0);
            Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
                scout.ScoutController.Locomotion.Platform.ActiveMatrix,
                newPlatformPoint
            );

            scout.ScoutController.Locomotion.Platform.ActiveMatrix = updatedMatrix;
            scout.SetTraversalState(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundState
                {
                    GroundMatrix = updatedMatrix,
                    HitObject = scout.ScoutController.Locomotion.Platform.ActivePlatform
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();

            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            var expectedVelocity = (newPlatformPoint - Vector3d.Zero) / TrailblazerManager.DeltaTime;
            scout.LinearVelocity.Should().Be(expectedVelocity);
        }

        [Fact]
        public void Given_FallingScout_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            // Act - First Frame (Falling)
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            // Assert
            scout.ScoutController.IsInAir.Should().BeTrue();

            // Simulate hitting the ground before the next frame
            scout.SetTraversalState(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundState
                {
                    HitObject = null,
                    GroundMatrix = Fixed4x4.Identity,
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();

            // Act - Second Frame (After Ground Contact)
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.ScoutController.IsGrounded.Should().BeTrue();
        }

        [Fact]
        public void Given_AirborneScout_When_SimulatedOverMultipleFrames_Then_VelocityShouldMatchGravity()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(
                startPosition: new Vector3d(0, 100, 0),
                grounded: false
            );
            Vector3d deltaAcceleration = Vector3d.Zero;  // store impulse-based velocity change per frame

            // Act - Simulate falling for 5 frames
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

                // Calculate expected velocity update from gravity impulse
                deltaAcceleration.y += -scout.ScoutController.Gravity * TrailblazerManager.DeltaTime;

                scout.FinalizeMovement();
            }

            // Assert
            scout.LinearVelocity.Should().Be(deltaAcceleration);
        }

        [Fact]
        public void Given_ScoutEntersWater_When_Simulated_Then_ShouldTransitionToSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);

            // Act - First frame, still on ground
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            // 2nd Frame - Enter Water
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.ScoutController.Locomotion.Swim.IsSwimming.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            // Act - Exit water
            scout.SetTraversalState(TraversalMedium.Ground);

            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.ScoutController.Locomotion.Swim.IsSwimming.Should().BeFalse();
        }

        [Fact]
        public void Given_SteepSlope_When_ScoutMovesOntoIt_Then_ShouldStartSliding()
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
            scout.ScoutController.Simulate(Vector3d.Forward, MoveInput.Walk);

            // Assert
            scout.ScoutController.Locomotion.Slide.IsSliding.Should().BeTrue();
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
            scout.ScoutController.Simulate(Vector3d.Forward, MoveInput.Walk);

            // Assert
            scout.ScoutController.Locomotion.Slide.IsSliding.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutOnSlope_When_Simulated_Then_VelocityShouldAlignWithSlope()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right, Fixed64.FromRaw(0x10000000L))
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.ScoutController.Simulate(Vector3d.Forward, MoveInput.Walk);

            // Assert
            scout.LinearVelocity.Should().NotBe(Vector3d.Zero);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);

            // Act - First Jump
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);
            scout.FinalizeMovement();

            // Release jump after 2 frames
            for (int i = 0; i < 2; i++)
            {
                TrailblazerManager.Simulate();
                scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
                scout.FinalizeMovement();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.ScoutController.Locomotion.Fall.IsFalling.Should().BeFalse();
            scout.ScoutController.Locomotion.Jump.IsJumping.Should().BeFalse();
            scout.ScoutController.Locomotion.Jump.IsCoolingDown.Should().BeFalse();
            scout.LinearVelocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();
            scout.ScoutController.Mode = ControllerMode.PositionDelta;

            FixedQuaternion initialRotation = scout.ScoutController.Locomotion.Platform.ActiveMatrix.Rotation;
            FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

            // Act
            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);
            scout.FinalizeMovement();

            // Apply platform rotation
            scout.ScoutController.Locomotion.Platform.ActiveMatrix = Fixed4x4.CreateRotation(rotationChange) * scout.ScoutController.Locomotion.Platform.ActiveMatrix;

            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle);

            // Assert
            scout.ScoutController.Locomotion.Platform.ActiveMatrix.Rotation.Should().Be(scout.VisualRotation);
        }

        [Fact]
        public void Given_ScoutInWater_When_Simulated_Then_ShouldApplyWaterDrag()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);
            scout.ScoutController.Locomotion.Swim.IsEnabled = true;

            // Act - Enter Water
            scout.SetTraversalState(TraversalMedium.Water,scout.WorldPosition.y + Fixed64.One);

            TrailblazerManager.Simulate();
            scout.ScoutController.Simulate(Vector3d.Forward, MoveInput.Walk);
            scout.FinalizeMovement();

            // Act - Simulate 3 Frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.ScoutController.Simulate(Vector3d.Forward, MoveInput.Walk);
                scout.FinalizeMovement();
            }

            // Assert
            // calculate what the velocity should without drag
            Fixed3x3 transposedMatrix = scout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, Vector3d.Forward);
            Fixed64 speed = scout.ScoutController.MaxSpeedInDirection(desiredLocalDirection);
            Vector3d expectedVelocity = transposedMatrix * (desiredLocalDirection * speed);

            scout.LinearVelocity.Magnitude.Should().BeLessThan(expectedVelocity.Magnitude);
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(grounded: true);
            scout.Events.CanAffordJump = () => false;

            // Act
            scout.ScoutController.Simulate(Vector3d.Zero, MoveInput.Idle, hasJumpRequest: true);
            scout.FinalizeMovement();

            // Assert
            scout.LinearVelocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

    }
}
