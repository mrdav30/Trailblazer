using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

namespace Trailblazer.Tests
{
    public class ScoutControllerTests
    {
        #region Movement

        [Fact]
        public void Given_When_ForceIsApplied_Then_VelocityShouldIncrease()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            Vector3d initialPosition = scout.WorldPosition;

            // Act
            scout.ScoutController.Traverse(Vector3d.One, TraversalSpeed.Fast);
            scout.FinalizeTraversal();

            // Assert
            Vector3d newPosition = scout.WorldPosition;
            var expectedVelocity = (newPosition - initialPosition) / TrailblazerManager.DeltaTime;

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().NotBe(Vector3d.Zero);
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().Be(expectedVelocity);
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Slow);
            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().NotBe(Vector3d.Zero);
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Slow);
            scout.FinalizeTraversal();

            // Assert
            var expected = Vector3d.ProjectOnPlane(Vector3d.Forward, scout.ScoutController.GroundNormal);
            expected.y = Fixed64.Zero; // we wipe out any vertical slope force
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Normal.Should().Be(expected.Normal);
        }

        [Fact]
        public void Given_SmallMovements_When_Simulated_Then_PositionShouldAccumulateCorrectly()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - Apply movement over multiple frames
            for (int i = 0; i < 10; i++)
            {
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
                TrailblazerManager.Simulate();
                scout.OnSimulate();
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
        public void Given_ScoutOnMaxWalkableSlope_When_Moving_Then_ShouldStayGrounded()
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
            scout.ScoutController.Traverse(Vector3d.Forward, TraversalSpeed.Slow);

            // Assert
            scout.ScoutController.Locomotions.Slide.IsSliding.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutWhenNoInput_Then_VelocityShouldDecayToZero()
        {
            var scout = IScoutTestFactory.CreateMockScout(startVelocity: new Vector3d(5, 0, 0));

            for (int i = 0; i < 100; i++) // Simulate multiple frames to test deceleration
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
            }

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().BeApproximately(Vector3d.Zero, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutMovesForward_When_ReversedInput_Then_ShouldDecelerate()
        {
            Vector3d iniitialVelocity = new Vector3d(3, 0, 0);
            var scout = IScoutTestFactory.CreateMockScout(startVelocity: iniitialVelocity, startingMedium: TraversalMedium.Ground);
            scout.SetTraversalRequest(new Vector3d(-1, 0, 0), TraversalSpeed.Moderate);

            for (int i = 0; i < 20; i++) // Apply opposing force over time
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
            }

            scout.ScoutController.Locomotions.Move.CurrentVelocity.x.Should().BeLessThan(iniitialVelocity.x); // Should be slowing down
        }

        [Fact]
        public void Given_ScoutOnSlope_When_MovingSideways_Then_VelocityShouldAdjustToSlope()
        {
            var scout = IScoutTestFactory.CreateMockScout(startPosition: new Vector3d(0, 0, 0));
            var slope = FixedMath.DegToRad((Fixed64)30);
            scout.SetTraversalState(
                TraversalMedium.Ground, 
                Fixed64.Zero, 
                new GroundState { 
                        GroundMatrix = Fixed4x4.CreateRotation(FixedQuaternion.FromEulerAngles(slope, Fixed64.Zero, Fixed64.Zero)) 
                    }
                );

            scout.SetTraversalRequest(new Vector3d(1, 0, 0), TraversalSpeed.Slow);
            scout.OnSimulate();

            Vector3d projectedMovement = Vector3d.ProjectOnPlane(scout.ScoutController.Locomotions.Move.CurrentVelocity, scout.ScoutController.GroundNormal);

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().Be(projectedMovement); // Moving sideways should project velocity down slope
        }

        #endregion

        #region Jumping

        [Fact]
        public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act
            scout.ScoutController.Traverse(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.y.Should().BeGreaterThan(Fixed64.Zero);
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
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            int expectedJumpFrame = scout.ScoutController.Locomotions.Jump.FrameStartJump;

            // Attempt to jump again immediately
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.ScoutController.IsInAir.Should().BeTrue();
            scout.ScoutController.Locomotions.Jump.IsCoolingDown.Should().BeTrue();
            scout.ScoutController.Locomotions.Jump.FrameStartJump.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Release jump after 2 frames
            for (int i = 0; i < 29; i++)
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.ScoutController.Locomotions.Jump.IsJumping.Should().BeFalse();
            scout.ScoutController.Locomotions.Jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we simulate 31
            scout.ScoutController.Locomotions.Move.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);
            if (scout.Events != null)
                scout.Events.CanAffordJump = () => false;

            // Act
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - Initial Jump
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            Vector3d previousVelocity = scout.ScoutController.Locomotions.Move.CurrentVelocity;

            // Continue holding jump for 3 frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
                scout.OnSimulate();
            }

            // Assert
            var expected = previousVelocity.y - (scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime * 3);
            scout.ScoutController.Locomotions.Move.CurrentVelocity.y.Should().BeGreaterThan(expected);
        }

        [Fact]
        public void Given_JumpingScout_When_PlatformIsMoving_Then_ShouldInheritVelocity()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(startPosition: Vector3d.Zero, platformMatrix: platform);

            // Act 1 - Set initial state
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Arrange - Move platform
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.SetTranslation(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform, new Vector3d(2, 0, 0));

            // Act 2 - Jump from moving platform
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().NotBe(Vector3d.Zero);
            scout.ScoutController.Locomotions.Move.CurrentVelocity.x.Should().Be(scout.ScoutController.Locomotions.MovingFloor.PlatformVelocity.x);
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpHeld_Then_ShouldJumpHigher()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            for (int i = 0; i < 10; i++) // Simulate holding jump button
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Slow, isRequestingJump: true);
                scout.OnSimulate();
            }

            scout.WorldPosition.y.Should().BeGreaterThan(Fixed64.One); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout(startPosition: new Vector3d(0, 5, 0));

            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Slow, isRequestingJump: true);
            scout.OnSimulate();

            scout.SetTraversalState(TraversalMedium.Air, surfaceLevel: Fixed64.FromRaw(5 << 16), ceilingLevel: Fixed64.FromRaw(6 << 16)); // Simulate a ceiling
            scout.OnSimulate();

            scout.ScoutController.Locomotions.Jump.IsJumping.Should().BeFalse(); // Jump should be canceled
            scout.ScoutController.Locomotions.Move.CurrentVelocity.y.Should().BeLessThanOrEqualTo(Fixed64.Zero); // Should stop rising
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_LandsOnGround_Then_ShouldResetJumpState()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            bool jumpStarted = false;
            bool jumpStopped = false;
            bool fallStarted = false;
            bool fallStopped = false;
               
            scout.Events.OnStartJump += (avoidTimer) => jumpStarted = true;
            scout.Events.OnStopJump += () => jumpStopped = true;
            scout.Events.OnStartFall += () => fallStarted = true;
            scout.Events.OnLandedFall += () => fallStopped = true;

            // Start Jump
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Slow, isRequestingJump: true);
            scout.OnSimulate();

            // Simulate entire jump arc until landing
            for (int i = 0; i < 30; i++)
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
                if (scout.WorldPosition.y <= Fixed64.Zero) // If we've landed
                    break;
            }

            scout.SetTraversalState(TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);
            scout.OnSimulate();

            // Assert that jump state has been reset after actual landing
            scout.ScoutController.Locomotions.Jump.IsJumping.Should().BeFalse();
            jumpStarted.Should().BeTrue();
            jumpStopped.Should().BeTrue();
            fallStarted.Should().BeTrue();
            // We treat landing a jump differently
            fallStopped.Should().BeFalse();
        }

        #endregion

        #region Gravity & Falling

        [Fact]
        public void Given_FallingScout_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingScout();

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime;

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
            scout.OnSimulate();

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
            scout.OnSimulate();

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
                scout.OnSimulate();

                // Calculate expected velocity update from gravity impulse
                expectedVelocity.y += -scout.ScoutController.GravityForce * TrailblazerManager.DeltaTime;
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
                scout.OnSimulate();
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
                scout.OnSimulate();
            }

            scout.WorldPosition.y.Should().BeLessThan(initialPosition.y); // Gravity should still apply
            scout.WorldPosition.x.Should().BeGreaterThan(Fixed64.Zero); // Should also move forward
        }

        #endregion

        #region Water

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
                scout.OnSimulate();
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
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            // Move platform afterward
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.SetTranslation(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform, new Vector3d(3, 0, 0));

            // Act 2 - Simulate next frame after platform movement
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Move.CurrentVelocity.x.Should().Be(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutEntersWater_When_Simulated_Then_ShouldTransitionToSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First frame, still on ground
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // 2nd Frame - Enter Water
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y);

            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.Swim.IsSwimming.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalState(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);

            scout.OnSimulate();

            // Act - Exit water
            scout.SetTraversalState(TraversalMedium.Ground);

            TrailblazerManager.Simulate();
            scout.OnSimulate();

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
            scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
            scout.OnSimulate();

            // Act - Simulate 3 Frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
                scout.OnSimulate();
            }

            // Assert
            // calculate what the velocity should without drag
            Fixed3x3 transposedMatrix = scout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, Vector3d.Forward);
            Fixed64 speed = scout.ScoutController.MaxHoritzontalSpeedInDirection(desiredLocalDirection);
            Vector3d expectedVelocity = transposedMatrix * (desiredLocalDirection * speed);

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().BeLessThan(expectedVelocity.Magnitude);
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
            scout.OnSimulate();

            Fixed64 previousY = scout.WorldPosition.y;

            // Simulate multiple frames of floating
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
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
                scout.OnSimulate();
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
                scout.OnSimulate();
            }

            // Assert - Scout should sink lower
            scout.WorldPosition.y.Should().BeLessThan(initialY);
        }

        [Fact]
        public void Given_ScoutDiving_When_MovesUp_Then_ShouldSwimUpward()
        {
            var initialPosition = new Vector3d(0, -2, 0);
            var scout = IScoutTestFactory.CreateMockScout(startPosition: initialPosition, startingMedium: TraversalMedium.Water);
            scout.ScoutController.Locomotions.Swim.IsSwimming = true;

            for (int i = 0; i < 10; i++) // Simulate swimming upwards
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Up, TraversalSpeed.Slow);
                scout.OnSimulate();
            }

            scout.WorldPosition.y.Should().BeGreaterThan(initialPosition.y); // Should rise
        }

        [Fact]
        public void Given_ScoutUnderwater_When_OutOfBreath_Then_ShouldTriggerDrowning()
        {
            var scout = IScoutTestFactory.CreateMockScout(startPosition: new Vector3d(0, -5, 0), startingMedium: TraversalMedium.Water);

            scout.ScoutController.Locomotions.Swim.HoldBreathTime = (Fixed64)3;
            scout.ScoutController.Locomotions.Swim.CanDrown = true;

            for (int i = 0; i < 100; i++) // Simulate prolonged underwater time
            {
                TrailblazerManager.Simulate();
                scout.OnSimulate();
            }

            scout.ScoutController.Locomotions.Swim.IsDrowning.Should().BeTrue();
        }

        #endregion

        #region Platforming

        [Fact]
        public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            // Act
            // 1st Frame
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            Vector3d newPlatformPoint = new(2, 0, 0);
            Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
                scout.ScoutController.Locomotions.MovingFloor.ActiveTransform,
                newPlatformPoint
            );

            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = updatedMatrix;
            scout.SetTraversalState(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundState
                {
                    GroundMatrix = updatedMatrix,
                    HitObject = scout.ScoutController.Locomotions.MovingFloor.ActivePlatform
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.WorldPosition.Should().Be(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform.Translation);
        }

        [Fact]
        public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

            // Act
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Apply platform rotation
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.CreateRotation(rotationChange) * scout.ScoutController.Locomotions.MovingFloor.ActiveTransform;

            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform.Rotation.Should().Be(scout.VisualRotation);
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
            scout.OnSimulate();

            // Move platform
            Vector3d movementDelta = new(1, 0, 0);
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.SetTranslation(
                scout.ScoutController.Locomotions.MovingFloor.ActiveTransform, movementDelta
            );

            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.WorldPosition.Should().Be(expectedPosition + movementDelta);
        }

        [Fact]
        public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritAngularMomentum()
        {
            // Arrange
            var scout = IScoutTestFactory.CreatePlatformScout();

            FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x08000000L)); // Small rotation

            // Act
            TrailblazerManager.Simulate();
            scout.OnSimulate();

            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.SetRotation(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform, rotationChange);
            scout.ScoutController.Locomotions.MovingFloor.ActiveTransform = Fixed4x4.NormalizeRotationMatrix(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform);

            TrailblazerManager.Simulate();
            scout.OnSimulate();

            // Assert
            scout.VisualRotation.Should().Be(scout.ScoutController.Locomotions.MovingFloor.ActiveTransform.Rotation);
        }

        #endregion

        #region Sliding

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
            scout.ScoutController.FinishFrameTraversal();

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

        #endregion
    }
}
