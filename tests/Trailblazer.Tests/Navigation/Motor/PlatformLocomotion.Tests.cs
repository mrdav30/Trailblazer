using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class PlatformLocomotionTests
	{
		[Fact]
		public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
		{
			// Arrange
			var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

			// Act
			// 1st Frame
			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			Vector3d newPlatformPoint = new(2, 0, 0);
			Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
				scout.Motor.Locomotions.Platform.ActiveTransform,
				newPlatformPoint
			);

			scout.Motor.Locomotions.Platform.ActiveTransform = updatedMatrix;

			// 2nd Frame
			TrailblazerManager.Simulate();
			scout.Simulate();

			scout.SetTraversalCondition(
				TraversalMedium.Ground,
				Fixed64.Zero,
				new GroundCondition
				{
					GroundMatrix = updatedMatrix,
					BaseObject = scout.Motor.Locomotions.Platform.ActivePlatform
				}
			);

			scout.CommitFrameMotion();

			// Assert
			scout.Position.Should().Be(scout.Motor.Locomotions.Platform.ActiveTransform.Translation);
		}

		[Fact]
		public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
		{
			// Arrange
			var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

			FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

			// Act
			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			// Apply platform rotation
			scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.CreateRotation(rotationChange) * scout.Motor.Locomotions.Platform.ActiveTransform;

			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			// Assert
			scout.Motor.Locomotions.Platform.ActiveTransform.Rotation.Should().Be(scout.Rotation);
		}

		[Fact]
		public void Given_ScoutOnMovingPlatform_Then_PositionShouldMatchPlatform()
		{
			// Arrange
			var platform = MockMotorAgentTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
			var scout = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: platform);

			Vector3d expectedPosition = scout.Position;

			// Act
			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			// Move platform
			Vector3d movementDelta = new(1, 0, 0);
			scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
				scout.Motor.Locomotions.Platform.ActiveTransform, movementDelta
			);

			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			// Assert
			scout.Position.Should().Be(expectedPosition + movementDelta);
		}

		[Fact]
		public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritRotation()
		{
			// Arrange
			var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

			FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x08000000L)); // Small rotation

			// Act
			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.SetRotation(scout.Motor.Locomotions.Platform.ActiveTransform, rotationChange);
			scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.NormalizeRotationMatrix(scout.Motor.Locomotions.Platform.ActiveTransform);

			TrailblazerManager.Simulate();
			scout.Simulate();
			scout.CommitFrameMotion();

			// Assert
			scout.Rotation.Should().Be(scout.Motor.Locomotions.Platform.ActiveTransform.Rotation);
		}

		[Fact]
		public void Given_ScoutJumpsBeforePlatformMoves_When_Simulated_Then_ShouldNotInheritFutureVelocity()
		{
			// Arrange

			var platform = MockMotorAgentTestFactory.CreatePlatform();
			var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			// Act 1 - Jump before platform movement
			TrailblazerManager.Simulate();
			scout.ApplyInputTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
			scout.Simulate();
			scout.CommitFrameMotion();

			// Move platform afterward
			scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.Motor.Locomotions.Platform.ActiveTransform, new Vector3d(3, 0, 0));

			// Act 2 - Simulate next frame after platform movement
			TrailblazerManager.Simulate();
			scout.ApplyInputTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
			scout.Simulate();
			scout.CommitFrameMotion();

			// Assert - we didn't pick up any horizontal velocity from the platform
			scout.Motor.Locomotions.Move.FrameVelocity.x.Should().Be(Fixed64.Zero);
		}

        [Fact]
        public void Given_ScoutWithInitTransfer_When_Jumping_Then_ShouldInheritPlatformVelocity()
        {
            var platform = MockMotorAgentTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			scout.Motor.Locomotions.Platform.PlatformVelocity = new Vector3d(1, 0, 0);

            TrailblazerManager.Simulate();
            scout.ApplyInputTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            scout.Motor.Locomotions.Move.FrameVelocity.x.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutWithPermaLocked_When_PlatformMoves_Then_ScoutFollowsPlatform()
        {
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
            scout.Motor.Locomotions.Platform.MovementTransfer = MotionTransfer.PermaLocked;

            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            var moveDelta = new Vector3d(2, 0, 0);
            scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
                scout.Motor.Locomotions.Platform.ActiveTransform, moveDelta
            );

            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            scout.Position.Should().Be(moveDelta);
        }

        [Fact]
        public void Given_ScoutHoldingOldPlatform_When_HoldTimesOut_Then_ShouldDetach()
        {
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
            var platform = scout.Motor.Locomotions.Platform.ActivePlatform;

            scout.Motor.Locomotions.Platform.SetHoldPlatform(platform);

			var newPlatform = MockMotorAgentTestFactory.CreatePlatform();

			scout.Motor.Locomotions.Platform.ActivePlatform = newPlatform;

			bool release = false;
            // Simulate exceeding the max hold frame count
            for (int i = 0; i < PlatformLocomotion.MaxHoldPlatformFrames + 1; i++)
            {
                release = scout.Motor.Locomotions.Platform.CanReleaseHoldOnPlatform();
				if (release)
					break;
            }

            release.Should().BeTrue();
        }

        [Fact]
        public void Given_PlatformLocomotionDisabled_When_Cleared_Then_ShouldResetState()
        {
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

            scout.Motor.Locomotions.Platform.IsEnabled = false;

            scout.Motor.Locomotions.Platform.ActivePlatform.Should().BeNull();
            scout.Motor.Locomotions.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
        }

        [Fact]
        public void Given_ScoutOnPlatform_When_JumpsWithInitTransfer_Then_InertiaShouldNotDoubleApply()
        {
            var platform = MockMotorAgentTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			var initialPlatformVelocity = new Vector3d(1, 0, 0);
            scout.Motor.Locomotions.Platform.PlatformVelocity = initialPlatformVelocity;

            TrailblazerManager.Simulate();
            scout.ApplyInputTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            Vector3d velocityAfterJump = scout.Motor.Locomotions.Move.FrameVelocity;

            velocityAfterJump.x.Should().Be(initialPlatformVelocity.x);
        }

        [Fact]
        public void Given_JumpingScout_When_PlatformIsMoving_Then_ShouldInheritVelocity()
        {
            // Arrange
            var platform = MockMotorAgentTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

            // Act 1 - Set initial state
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Arrange - Move platform
            scout.Motor.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.Motor.Locomotions.Platform.ActiveTransform, new Vector3d(2, 0, 0));

            // Act 2 - Jump from moving platform
            TrailblazerManager.Simulate();
            scout.ApplyInputTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.Locomotions.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
            scout.Motor.Locomotions.Move.FrameVelocity.x.Should().Be(scout.Motor.Locomotions.Platform.PlatformVelocity.x);
        }

        [Fact]
        public void Given_ScoutUsesPlatformInertia_When_Lands_Then_FrameVelocityShouldBeCleared()
        {
			var platform = MockMotorAgentTestFactory.CreatePlatform();
            var scout = MockMotorAgentTestFactory.CreateFallingAgent(platformMatrix: platform);
            scout.Motor.Locomotions.Platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

            scout.Simulate();

			scout.SetTraversalCondition(medium: TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            scout.CommitFrameMotion(); // Would trigger inertia clearing

            scout.Motor.Locomotions.Platform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
        }

    }
}
