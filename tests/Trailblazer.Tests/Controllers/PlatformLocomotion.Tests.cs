using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

namespace Trailblazer.Tests.Controllers
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
				scout.ScoutController.Locomotions.Platform.ActiveTransform,
				newPlatformPoint
			);

			scout.ScoutController.Locomotions.Platform.ActiveTransform = updatedMatrix;

			// 2nd Frame
			TrailblazerManager.Simulate();
			scout.StartTraversal();

			scout.SetTraversalCondition(
				TraversalMedium.Ground,
				Fixed64.Zero,
				new SurfaceCondition
				{
					SurfaceMatrix = updatedMatrix,
					SurfaceObject = scout.ScoutController.Locomotions.Platform.ActivePlatform
				}
			);

			scout.FinalizeTraversal();

			// Assert
			scout.WorldPosition.Should().Be(scout.ScoutController.Locomotions.Platform.ActiveTransform.Translation);
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
			scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.CreateRotation(rotationChange) * scout.ScoutController.Locomotions.Platform.ActiveTransform;

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.ScoutController.Locomotions.Platform.ActiveTransform.Rotation.Should().Be(scout.VisualRotation);
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
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Move platform
			Vector3d movementDelta = new(1, 0, 0);
			scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(
				scout.ScoutController.Locomotions.Platform.ActiveTransform, movementDelta
			);

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

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
			scout.StartTraversal();
			scout.FinalizeTraversal();

			scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetRotation(scout.ScoutController.Locomotions.Platform.ActiveTransform, rotationChange);
			scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.NormalizeRotationMatrix(scout.ScoutController.Locomotions.Platform.ActiveTransform);

			TrailblazerManager.Simulate();
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.VisualRotation.Should().Be(scout.ScoutController.Locomotions.Platform.ActiveTransform.Rotation);
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
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Move platform afterward
			scout.ScoutController.Locomotions.Platform.ActiveTransform = Fixed4x4.SetTranslation(scout.ScoutController.Locomotions.Platform.ActiveTransform, new Vector3d(3, 0, 0));

			// Act 2 - Simulate next frame after platform movement
			TrailblazerManager.Simulate();
			scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary, isRequestingJump: true);
			scout.StartTraversal();
			scout.FinalizeTraversal();

			// Assert
			scout.ScoutController.Locomotions.Move.CurrentVelocity.x.Should().Be(Fixed64.Zero);
		}
	}
}
