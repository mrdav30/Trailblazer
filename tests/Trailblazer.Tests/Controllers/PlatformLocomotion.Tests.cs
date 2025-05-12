using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigator.Motor;

namespace Trailblazer.Tests.Navigator.Motor
{
    [Collection("TrailblazerCollection")]
    public class PlatformLocomotionTests
	{
		[Fact]
		public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
		{
			// Arrange
			var scout = IScoutTestFactory.CreatePlatformScout();

			// Act
			// 1st Frame
			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			Vector3d newPlatformPoint = new(2, 0, 0);
			Fixed4x4 updatedMatrix = Fixed4x4.SetTranslation(
				scout.Controller.Locomotions.Platform.ActiveTransform,
				newPlatformPoint
			);

			scout.Controller.Locomotions.Platform.ActiveTransform = updatedMatrix;

			// 2nd Frame
			TrailblazerManager.Simulate();
			scout.StartTraversal();

			scout.SetTraversalCondition(
				TraversalMedium.Ground,
				Fixed64.Zero,
				new GroundCondition
				{
					GroundMatrix = updatedMatrix,
					BaseObject = scout.Controller.Locomotions.Platform.ActivePlatform
				}
			);

			scout.FinalizeTraversal();

			// Assert
			scout.Position.Should().Be(scout.Controller.Locomotions.Platform.ActiveTransform.Translation);
		}

		[Fact]
		public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
		{
			// Arrange
			var scout = IScoutTestFactory.CreatePlatformScout();

			FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

			// Act
			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Apply platform rotation
			scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.CreateRotation(rotationChange) * scout.Controller.Locomotions.Platform.ActiveTransform;

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.Controller.Locomotions.Platform.ActiveTransform.Rotation.Should().Be(scout.Rotation);
		}

		[Fact]
		public void Given_ScoutOnMovingPlatform_Then_PositionShouldMatchPlatform()
		{
			// Arrange
			var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
			var scout = IScoutTestFactory.CreatePlatformScout(startPosition: Vector3d.Zero, platformMatrix: platform);

			Vector3d expectedPosition = scout.Position;

			// Act
			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Move platform
			Vector3d movementDelta = new(1, 0, 0);
			scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
				scout.Controller.Locomotions.Platform.ActiveTransform, movementDelta
			);

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.Position.Should().Be(expectedPosition + movementDelta);
		}

		[Fact]
		public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritRotation()
		{
			// Arrange
			var scout = IScoutTestFactory.CreatePlatformScout();

			FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x08000000L)); // Small rotation

			// Act
			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.SetRotation(scout.Controller.Locomotions.Platform.ActiveTransform, rotationChange);
			scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.NormalizeRotationMatrix(scout.Controller.Locomotions.Platform.ActiveTransform);

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.Rotation.Should().Be(scout.Controller.Locomotions.Platform.ActiveTransform.Rotation);
		}

		[Fact]
		public void Given_ScoutJumpsBeforePlatformMoves_When_Simulated_Then_ShouldNotInheritFutureVelocity()
		{
			// Arrange

			var platform = IScoutTestFactory.CreatePlatform();
			var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			// Act 1 - Jump before platform movement
			TrailblazerManager.Simulate();
			scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Move platform afterward
			scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.Controller.Locomotions.Platform.ActiveTransform, new Vector3d(3, 0, 0));

			// Act 2 - Simulate next frame after platform movement
			TrailblazerManager.Simulate();
			scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert - we didn't pick up any horizontal velocity from the platform
			scout.Controller.Locomotions.Move.CurrentVelocity.x.Should().Be(Fixed64.Zero);
		}

        [Fact]
        public void Given_ScoutWithInitTransfer_When_Jumping_Then_ShouldInheritPlatformVelocity()
        {
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			scout.Controller.Locomotions.Platform.PlatformVelocity = new Vector3d(1, 0, 0);

            TrailblazerManager.Simulate();
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.Controller.Locomotions.Move.CurrentVelocity.x.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutWithPermaLocked_When_PlatformMoves_Then_ScoutFollowsPlatform()
        {
            var scout = IScoutTestFactory.CreatePlatformScout();
            scout.Controller.Locomotions.Platform.MovementTransfer = MotionTransfer.PermaLocked;

            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            var moveDelta = new Vector3d(2, 0, 0);
            scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
                scout.Controller.Locomotions.Platform.ActiveTransform, moveDelta
            );

            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.Position.Should().Be(moveDelta);
        }

        [Fact]
        public void Given_ScoutHoldingOldPlatform_When_HoldTimesOut_Then_ShouldDetach()
        {
            var scout = IScoutTestFactory.CreatePlatformScout();
            var platform = scout.Controller.Locomotions.Platform.ActivePlatform;

            scout.Controller.Locomotions.Platform.SetHoldPlatform(platform);

			var newPlatform = IScoutTestFactory.CreatePlatform();

			scout.Controller.Locomotions.Platform.ActivePlatform = newPlatform;

			bool release = false;
            // Simulate exceeding the max hold frame count
            for (int i = 0; i < PlatformLocomotion.MaxHoldPlatformFrames + 1; i++)
            {
                release = scout.Controller.Locomotions.Platform.CanReleaseHoldOnPlatform();
				if (release)
					break;
            }

            release.Should().BeTrue();
        }

        [Fact]
        public void Given_PlatformLocomotionDisabled_When_Cleared_Then_ShouldResetState()
        {
            var scout = IScoutTestFactory.CreatePlatformScout();

            scout.Controller.Locomotions.Platform.IsEnabled = false;

            scout.Controller.Locomotions.Platform.ActivePlatform.Should().BeNull();
            scout.Controller.Locomotions.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
        }

        [Fact]
        public void Given_ScoutOnPlatform_When_JumpsWithInitTransfer_Then_InertiaShouldNotDoubleApply()
        {
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

			var initialPlatformVelocity = new Vector3d(1, 0, 0);
            scout.Controller.Locomotions.Platform.PlatformVelocity = initialPlatformVelocity;

            TrailblazerManager.Simulate();
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            Vector3d velocityAfterJump = scout.Controller.Locomotions.Move.CurrentVelocity;

            velocityAfterJump.x.Should().Be(initialPlatformVelocity.x);
        }

        [Fact]
        public void Given_JumpingScout_When_PlatformIsMoving_Then_ShouldInheritVelocity()
        {
            // Arrange
            var platform = IScoutTestFactory.CreatePlatform(startPosition: Vector3d.Zero);
            var scout = IScoutTestFactory.CreatePlatformScout(startPosition: Vector3d.Zero, platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

            // Act 1 - Set initial state
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Arrange - Move platform
            scout.Controller.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.Controller.Locomotions.Platform.ActiveTransform, new Vector3d(2, 0, 0));

            // Act 2 - Jump from moving platform
            TrailblazerManager.Simulate();
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();



            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.Should().NotBe(Vector3d.Zero);
            scout.Controller.Locomotions.Move.CurrentVelocity.x.Should().Be(scout.Controller.Locomotions.Platform.PlatformVelocity.x);
        }

        [Fact]
        public void Given_ScoutUsesPlatformInertia_When_Lands_Then_FrameVelocityShouldBeCleared()
        {
			var platform = IScoutTestFactory.CreatePlatform();
            var scout = IScoutTestFactory.CreateFallingScout(platformMatrix: platform);
            scout.Controller.Locomotions.Platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

            scout.StartTraversal();

			scout.SetTraversalCondition(medium: TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            scout.FinalizeTraversal(); // Would trigger inertia clearing

            scout.Controller.Locomotions.Platform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
        }

    }
}
