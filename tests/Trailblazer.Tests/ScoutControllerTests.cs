using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;
using Trailblazer.Controllers.Locomotions;

namespace Trailblazer.UnitTests
{
    public class ScoutControllerTests
    {
        [Fact]
        public void Given_When_ForceIsApplied_Then_VelocityShouldIncrease()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            Vector3d initialPosition = scout.WorldPosition;

            // Act
            scout.ScoutController.Traverse(Vector3d.One, TraversalSpeed.Sprint);
            scout.OnFinalizeTraversal();

            // Assert
            Vector3d newPosition = scout.WorldPosition;
            var expectedVelocity = (newPosition - initialPosition) / TrailblazerManager.DeltaTime;

            scout.ScoutController.CurrentVelocity.Should().NotBe(Vector3d.Zero);
            scout.ScoutController.CurrentVelocity.Should().Be(expectedVelocity);
        }

        [Fact]
        public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act
            scout.ScoutController.Traverse(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.OnFinalizeTraversal();

            // Assert
            scout.ScoutController.CurrentVelocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(
                startPosition: Vector3d.Up,
                startVelocity: Vector3d.Down,
                startingMedium: TraversalMedium.Air);

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            // Assert
            scout.ScoutController.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_FallingScout_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.ScoutController.Traverse(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.OnFinalizeTraversal();

            // Assert
            scout.ScoutController.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();

            int expectedJumpFrame = scout.ScoutController.Locomotions.Jump.FrameStartJump;

            // Attempt to jump again immediately
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.ScoutController.IsInAir.Should().BeTrue();
            scout.ScoutController.Locomotions.Jump.IsCoolingDown.Should().BeTrue();
            scout.ScoutController.Locomotions.Jump.FrameStartJump.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            // Act
            // 1st Frame
            TrailblazerManager.Simulate();
            scout.Simulate();

            Vector3d newPlatformPoint = new Vector3d(2, 0, 0);
            Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
                scout.ScoutController.Locomotions.Platform.ActiveTransform,
                newPlatformPoint
            );

            scout.ScoutController.Locomotions.Platform.ActiveTransform = updatedMatrix;
            scout.SetTraversalState(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundState
                {
                    GroundMatrix = updatedMatrix,
                    HitObject = scout.ScoutController.Locomotions.Platform.ActivePlatform
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.WorldPosition.Should().Be(scout.ScoutController.Locomotions.Platform.ActiveTransform.Translation);
        }

        [Fact]
        public void Given_FallingScout_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            // Act - First Frame (Falling)
            TrailblazerManager.Simulate();
            scout.Simulate();

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
            scout.Simulate();

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
                scout.Simulate();

                // Calculate expected velocity update from gravity impulse
                expectedVelocity.y += -scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime;
            }

            // Assert
            scout.ScoutController.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Walk);
            scout.ScoutController.FinishTraversing();

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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Walk);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Walk);
            scout.OnFinalizeTraversal();

            // Assert
            scout.ScoutController.CurrentVelocity.Should().NotBe(Vector3d.Zero);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Release jump after 2 frames
            for (int i = 0; i < 29; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.ScoutController.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.ScoutController.Locomotions.Jump.IsJumping.Should().BeFalse();
            scout.ScoutController.Locomotions.Jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we only simulate 4
            scout.ScoutController.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            FixedQuaternion initialRotation = scout.ScoutController.Locomotions.Platform.ActiveTransform.Rotation;
            FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

            // Act
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Apply platform rotation
            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.CreateRotation(rotationChange) * scout.ScoutController.Locomotions.Platform.ActiveTransform;

            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.ScoutController.Locomotions.Platform.ActiveTransform.Rotation.Should().Be(scout.VisualRotation);
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);
            if (scout.Events != null)
                scout.Events.CanAffordJump = () => false;

            // Act
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            // Assert
            scout.ScoutController.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

        [Fact]
        public void Given_ScoutOnMovingPlatform_Then_PositionShouldMatchPlatform()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(startPosition: Vector3d.Zero, platformMatrix: platform);

            Vector3d expectedPosition = scout.WorldPosition;

            // Act
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Move platform
            Vector3d movementDelta = new Vector3d(1, 0, 0);
            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
                scout.ScoutController.Locomotions.Platform.ActiveTransform, movementDelta
            );

            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.WorldPosition.Should().Be(expectedPosition + movementDelta);
        }

        [Fact]
        public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritAngularMomentum()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            FixedQuaternion initialRotation = scout.VisualRotation;
            FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x08000000L)); // Small rotation

            // Act
            TrailblazerManager.Simulate();
            scout.Simulate();

            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetRotation(scout.ScoutController.Locomotions.Platform.ActiveTransform, rotationChange);
            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.NormalizeRotationMatrix(scout.ScoutController.Locomotions.Platform.ActiveTransform);

            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.VisualRotation.Should().Be(scout.ScoutController.Locomotions.Platform.ActiveTransform.Rotation);
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - Initial Jump
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            Vector3d previousVelocity = scout.ScoutController.CurrentVelocity;

            // Continue holding jump for 3 frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
                scout.Simulate();
            }

            // Assert
            var expected = previousVelocity.y - (scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime * 3);
            scout.ScoutController.CurrentVelocity.y.Should().BeGreaterThan(expected);
        }

        [Fact]
        public void Given_ScoutOnSlope_When_Simulated_Then_VelocityShouldBeProjectedOntoSlope()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right, Fixed64.FromRaw(0x10000000L)) // Shallow slope
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Walk);
            scout.OnFinalizeTraversal();

            // Assert
            var expected = Vector3d.ProjectOnPlane(Vector3d.Forward, scout.ScoutController.GroundNormal);
            expected.y = Fixed64.Zero; // we wipe out any vertical slope force
            scout.ScoutController.CurrentVelocity.Normal.Should().Be(expected.Normal);
        }

        [Fact]
        public void Given_SmallMovements_When_Simulated_Then_PositionShouldAccumulateCorrectly()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);
            Vector3d initialPosition = scout.WorldPosition;

            // Act - Apply movement over multiple frames
            for (int i = 0; i < 10; i++)
            {
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Walk);
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            // Assert

            // we simulated 9 frames
            // first frame is clamped by max ground acceleration
            // terminal velocity for walking is reached after 1st frame
            var expected = (
                    ((scout.ScoutController.Locomotions.Move.MaxGroundAcceleration * TrailblazerManager.DeltaTime) * Vector3d.Forward)
                    + (Vector3d.Forward * 9)
                ) * TrailblazerManager.DeltaTime;
            scout.WorldPosition.Should().Be(expected);
        }

        [Fact]
        public void Given_ScoutJumpsFromMovingPlatform_When_Simulated_Then_ShouldInheritPlatformVelocity()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(startPosition: Vector3d.Zero, platformMatrix: platform);

            // Act 1 - Set initial state
            TrailblazerManager.Simulate();
            scout.Simulate();

            // Arrange - Move platform
            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.ScoutController.Locomotions.Platform.ActiveTransform, new Vector3d(2, 0, 0));

            // Act 2 - Jump from moving platform
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            // Assert
            scout.ScoutController.CurrentVelocity.Should().NotBe(Vector3d.Zero);
            scout.ScoutController.CurrentVelocity.x.Should().Be(scout.ScoutController.Locomotions.Platform.ActiveVelocity.x);
        }

        [Fact]
        public void Given_SlopeAtThreshold_When_Simulated_Then_ShouldNotSlide()
        {
            // Arrange
            var slopeLimit = Fixed64.FromRaw(0xB2B8C75C); // 2998454108L, converts to ~0.698131999932 radians or ~40 degrees; 
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromAxisAngle(Vector3d.Right, slopeLimit)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            // Act
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Walk);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }

        #region Water Traversal Tests

        [Fact]
        public void Given_ScoutAtNeutralBuoyancy_When_Simulated_Then_ShouldRemainSuspended()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.One; // Neutral buoyancy

            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            // Assert - Position should remain stable within a small range
            scout.WorldPosition.y.Should().BeApproximately(initialY, Fixed64.FromRaw(0x00001000)); // Small tolerance
        }

        [Fact]
        public void Given_ScoutJumpsBeforePlatformMoves_When_Simulated_Then_ShouldNotInheritFutureVelocity()
        {
            // Arrange

            var platform = IScoutTestFactory.CreatePlatform();
            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform);

            // Act 1 - Jump before platform movement
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            // Move platform afterward
            scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.ScoutController.Locomotions.Platform.ActiveTransform, new Vector3d(3, 0, 0));

            // Act 2 - Simulate next frame after platform movement
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Idle, isRequestingJump: true);
            scout.Simulate();

            // Assert
            scout.ScoutController.CurrentVelocity.x.Should().Be(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutEntersWater_When_Simulated_Then_ShouldTransitionToSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First frame, still on ground
            TrailblazerManager.Simulate();
            scout.Simulate();

            // 2nd Frame - Enter Water
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y);

            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.ScoutController.Locomotions.Swim.IsSwimming.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            scout.Simulate();

            // Act - Exit water
            scout.SetTraversalState(TraversalMedium.Ground);

            TrailblazerManager.Simulate();
            scout.Simulate();

            // Assert
            scout.ScoutController.Locomotions.Swim.IsSwimming.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutInWater_When_Simulated_Then_ShouldApplyWaterDrag()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            // Act - Enter Water
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Walk);
            scout.Simulate();

            // Act - Simulate 3 Frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Walk);
                scout.Simulate();
            }

            // Assert
            // calculate what the velocity should without drag
            Fixed3x3 transposedMatrix = scout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, Vector3d.Forward);
            Fixed64 speed = scout.ScoutController.MaxSpeedInDirection(desiredLocalDirection);
            Vector3d expectedVelocity = transposedMatrix * (desiredLocalDirection * speed);

            scout.ScoutController.CurrentVelocity.Magnitude.Should().BeLessThan(expectedVelocity.Magnitude);
        }

        [Fact]
        public void Given_ScoutAtWaterSurface_When_Simulated_Then_ShouldExperienceBuoyancyForces()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            // Act - Simulate entry into water
            TrailblazerManager.Simulate();
            scout.Simulate();

            Fixed64 previousY = scout.WorldPosition.y;

            // Simulate multiple frames of floating
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            var tolerance = Fixed64.FromRaw(0x0800);
            // Assert
            scout.WorldPosition.y.Should().BeApproximately(previousY, tolerance); // Allow some small float oscillation
        }

        [Fact]
        public void Given_ScoutWithPositiveBuoyancy_When_Simulated_Then_ShouldFloatUp()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startPosition: Vector3d.Down);
            scout.SetTraversalState(TraversalMedium.Water, Fixed64.Zero);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.FromRaw(0x180000000L); // ~1.5, meaning scout is more buoyant
                                                                                    
            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            // Assert - Scout should float higher
            scout.WorldPosition.y.Should().BeGreaterThan(initialY);
        }

        [Fact]
        public void Given_ScoutWithNegativeBuoyancy_When_Simulated_Then_ShouldSink()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startPosition: Vector3d.Down);
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.Half; // ~0.5, meaning scout is heavier than water

            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
            }

            // Assert - Scout should sink lower
            scout.WorldPosition.y.Should().BeLessThan(initialY);
        }


        #endregion
    }
}
