using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

namespace Trailblazer.Tests.Controllers
{
    [Collection("TrailblazerCollection")]
    public class MoveLocomotionTests
    {
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
        public void Given_SmallMovements_When_Simulated_Then_PositionShouldAccumulateCorrectly()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - Apply movement over multiple frames
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Move.CurrentVelocity.x.Should().BeLessThan(iniitialVelocity.x); // Should be slowing down
        }

        [Fact]
        public void Given_ScoutOnSlope_When_MovingSideways_Then_VelocityShouldAdjustToSlope()
        {
            var scout = IScoutTestFactory.CreateMockScout(startPosition: new Vector3d(0, 0, 0));
            var slope = FixedMath.DegToRad((Fixed64)30);

            scout.SetTraversalCondition(
                medium: TraversalMedium.Ground,
                surfaceCondition: new SurfaceCondition
                {
                    SurfaceMatrix = Fixed4x4.CreateRotation(FixedQuaternion.FromEulerAngles(slope, Fixed64.Zero, Fixed64.Zero))
                }
                );
            scout.SetTraversalRequest(new Vector3d(1, 0, 0), TraversalSpeed.Slow);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            Vector3d projectedMovement = Vector3d.ProjectOnPlane(scout.ScoutController.Locomotions.Move.CurrentVelocity, scout.ScoutController.CurrentState.SurfaceNormal);

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().Be(projectedMovement); // Moving sideways should project velocity down slope
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

            // Assert - Projected vector must lie in the tangent plane of the slope
            var velocity = scout.ScoutController.Locomotions.Move.CurrentVelocity;
            var slopeNormal = scout.ScoutController.CurrentState.SurfaceNormal;
            var expected = Vector3d.ProjectOnPlane(Vector3d.Forward, slopeNormal);

            // Ensure downward movement on downhill slopes & upward movement on uphill slopes
            if (scout.ScoutController.CurrentState.SlopeAngle != Fixed64.Zero 
                && Fixed64.Sign(expected.y) != Fixed64.Sign(scout.ScoutController.CurrentState.SlopeAngle))
            {
                expected.y *= -1;
            }

            velocity.Normal.Should().BeApproximately(expected.Normal, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutOnDownhillSlope_When_MovingDownhill_Then_ShouldAccelerate()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)30);
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            for(int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().BeGreaterThan(Fixed64.One);
        }

        [Fact]
        public void Given_ScoutOnUphillSlope_When_MovingUphill_Then_ShouldDecelerate()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)30);
            var platform = IScoutTestFactory.CreatePlatform(
                startPosition: Vector3d.Zero,
                platformRotation: FixedQuaternion.FromEulerAngles(-slopeAngle, Fixed64.Zero, Fixed64.Zero)
            );

            var scout = IScoutTestFactory.CreatePlatformScout(
                startPosition: Vector3d.Zero,
                platformMatrix: platform
            );

            scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Moderate);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().BeLessThan(Fixed64.One);
        }

        [Fact]
        public void Given_ScoutOnFlatSurface_When_MovingForward_Then_ShouldMaintainSpeed()
        {
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Moderate);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Simulate multiple frames
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Moderate);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Magnitude.Should().Be((Fixed64)2);
        }

        [Fact]
        public void Given_ScoutMoving_When_StopRequested_Then_ShouldStopImmediately()
        {
            var scout = IScoutTestFactory.CreateMockScout(startVelocity: new Vector3d(5, 0, 0));

            scout.SetTraversalRequest(Vector3d.Zero, TraversalSpeed.Stationary);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.ScoutController.Locomotions.Move.CurrentVelocity.Should().Be(Vector3d.Zero);
        }
    }
}
