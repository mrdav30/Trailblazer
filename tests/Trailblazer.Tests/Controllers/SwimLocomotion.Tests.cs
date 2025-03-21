using Xunit;
using FluentAssertions;
using Trailblazer.Controllers;
using FixedMathSharp;
using Trailblazer.Tests.Assertions;

namespace Trailblazer.Tests.Controllers
{
    [Collection("TrailblazerCollection")]
    public class SwimLocomotionTests
    {

        [Fact]
        public void Given_ScoutAtNeutralBuoyancy_When_Simulated_Then_ShouldRemainSuspended()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.One; // Neutral buoyancy

            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            // Assert - Position should remain stable within a small range
            scout.WorldPosition.y.Should().BeApproximately(initialY, Fixed64.FromRaw(0x00001000)); // Small tolerance
        }

        [Fact]
        public void Given_ScoutEntersWater_When_Simulated_Then_ShouldTransitionToSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First frame, still on ground
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // 2nd Frame - Enter Water
            TrailblazerManager.Simulate();
            scout.StartTraversal();

            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y);

            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.Locomotions.Swim.IsSwimming.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);
            scout.ScoutController.UpdateTraversal(scout.TraversalCondition);

            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Act - Exit water

            TrailblazerManager.Simulate();
            scout.StartTraversal();

            scout.SetTraversalCondition(TraversalMedium.Ground);

            scout.FinalizeTraversal();

            // Assert
            scout.ScoutController.Locomotions.Swim.IsSwimming.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutInWater_When_Simulated_Then_ShouldApplyWaterDrag()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout();
            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);
            scout.ScoutController.UpdateTraversal(scout.TraversalCondition);

            // Act - Enter Water
            TrailblazerManager.Simulate();
            scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Act - Simulate 3 Frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTraversalRequest(Vector3d.Forward, TraversalSpeed.Slow);
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y + Fixed64.One);
            scout.ScoutController.UpdateTraversal(scout.TraversalCondition);

            // Act - Simulate entry into water
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            Fixed64 previousY = scout.WorldPosition.y;

            // Simulate multiple frames of floating
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
            scout.SetTraversalCondition(TraversalMedium.Water, Fixed64.Zero);
            scout.ScoutController.UpdateTraversal(scout.TraversalCondition);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.FromRaw(0x180000000L); // ~1.5, meaning scout is more buoyant

            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            // Assert - Scout should float higher
            scout.WorldPosition.y.Should().BeGreaterThan(initialY);
        }

        [Fact]
        public void Given_ScoutWithNegativeBuoyancy_When_Simulated_Then_ShouldSink()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startPosition: Vector3d.Down);
            scout.SetTraversalCondition(TraversalMedium.Water, scout.WorldPosition.y);
            scout.ScoutController.UpdateTraversal(scout.TraversalCondition);

            scout.ScoutController.Locomotions.Swim.IsEnabled = true;
            scout.ScoutController.Locomotions.Swim.BuoyancyFactor = Fixed64.Half; // ~0.5, meaning scout is heavier than water

            // Act - Simulate multiple frames
            Fixed64 initialY = scout.WorldPosition.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
                scout.StartTraversal();
                scout.FinalizeTraversal();
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
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.ScoutController.Locomotions.Swim.IsDrowning.Should().BeTrue();
        }
    }
}
