using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class SwimLocomotionTests
    {

        [Fact]
        public void Given_ScoutAtNeutralBuoyancy_When_Simulated_Then_ShouldRemainSuspended()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: (Fixed64)99);

            agent.Motor.Locomotions.Swim.IsEnabled = true;
            agent.Motor.Locomotions.Swim.BuoyancyFactor = Fixed64.One; // Neutral buoyancy

            // Act - Simulate multiple frames
            Fixed64 initialY = agent.Position.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
            }

            // Assert - Position should remain stable within a small range
            agent.Position.y.Should().BeApproximately(initialY, Fixed64.FromRaw(0x00001000)); // Small tolerance
        }

        [Fact]
        public void Given_ScoutEntersWater_When_Simulated_Then_ShouldTransitionToSwimming()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - First frame, still on ground
            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            // 2nd Frame - Enter Water
            TrailblazerManager.Simulate();
            agent.Simulate();

            agent.SetTraversalCondition(TraversalMedium.Water, agent.Position.y);

            agent.Visualize();

            // Assert
            agent.Motor.Locomotions.Swim.IsSwimming.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutExitsWater_When_Simulated_Then_ShouldTransitionOutOfSwimming()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);

            agent.Simulate();
            agent.Visualize();

            // Act - Exit water

            TrailblazerManager.Simulate();
            agent.Simulate();

            agent.SetTraversalCondition(TraversalMedium.Ground);

            agent.Visualize();

            // Assert
            agent.Motor.Locomotions.Swim.IsSwimming.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutInWater_When_Simulated_Then_ShouldApplyWaterDrag()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);
            agent.Motor.Locomotions.Swim.IsEnabled = true;

            // Act - Enter Water
            TrailblazerManager.Simulate();
            agent.SetTravelRequest(direction: Vector3d.Forward, rate: TrekRate.Slow);
            agent.Simulate();
            agent.Visualize();

            // Act - Simulate 3 Frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                agent.SetTravelRequest(direction: Vector3d.Forward, rate: TrekRate.Slow);
                agent.Simulate();
                agent.Visualize();
            }

            // Assert
            // calculate what the velocity should without drag
            Fixed3x3 transposedMatrix = agent.Rotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, Vector3d.Forward);
            Fixed64 speed = agent.Motor.MaxHoritzontalSpeedInDirection(desiredLocalDirection, TrekRate.Slow);
            Vector3d expectedVelocity = transposedMatrix * (desiredLocalDirection * speed);

            agent.Motor.Locomotions.Move.Velocity.Magnitude.Should().BeLessThan(expectedVelocity.Magnitude);
        }

        [Fact]
        public void Given_ScoutAtWaterSurface_When_Simulated_Then_ShouldExperienceBuoyancyForces()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.One);
            agent.Motor.Locomotions.Swim.IsEnabled = true;

            // Act - Simulate entry into water
            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            Fixed64 previousY = agent.Position.y;

            // Simulate multiple frames of floating
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
            }

            var tolerance = Fixed64.FromRaw(0x0800);
            // Assert
            agent.Position.y.Should().BeApproximately(previousY, tolerance); // Allow some small float oscillation
        }

        [Fact]
        public void Given_ScoutWithPositiveBuoyancy_When_Simulated_Then_ShouldFloatUp()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(startPosition: Vector3d.Down * 5);

            agent.Motor.Locomotions.Swim.IsEnabled = true;
            agent.Motor.Locomotions.Swim.BuoyancyFactor = Fixed64.FromRaw(0x180000000L); // ~1.5, meaning agent is more buoyant

            // Act - Simulate multiple frames
            Fixed64 initialY = agent.Position.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
                if (agent.Position.y == Fixed64.Zero) // we hit the surface
                    break;
            }

            // Assert - Scout should float higher
            agent.Position.y.Should().BeGreaterThan(initialY);
            agent.Motor.Locomotions.Move.Velocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutWithLowBuoyancy_When_Simulated_Then_ShouldSink()
        {
            // Arrange
            var agent = MockAgentTestFactory.CreateWaterAgent(startPosition: Vector3d.Down);

            agent.Motor.Locomotions.Swim.IsEnabled = true;
            agent.Motor.Locomotions.Swim.BuoyancyFactor = Fixed64.Half; // ~0.5, meaning agent is heavier than water

            // Act - Simulate multiple frames
            Fixed64 initialY = agent.Position.y;
            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
            }

            // Assert - Scout should sink lower
            agent.Position.y.Should().BeLessThan(initialY);
            agent.Motor.Locomotions.Move.Velocity.y.Should().BeLessThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutResurfacesFromDive_When_BreathWasLow_Then_ShouldRegenerateBreath()
        {
            var agent = MockAgentTestFactory.CreateWaterAgent();
            var swim = agent.Motor.Locomotions.Swim;
            swim.UnderwaterTimer = (Fixed64)30;

            // Simulate resurfacing
            swim.IsDiving = false;

            for (int i = 0; i < 10; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
            }

            swim.UnderwaterTimer.Should().BeLessThan((Fixed64)30);
        }

        [Fact]
        public void Given_DrowningDisabled_When_UnderwaterLong_Then_ShouldNotDrown()
        {
            var agent = MockAgentTestFactory.CreateWaterAgent();
            var swim = agent.Motor.Locomotions.Swim;
            swim.CanDrown = false;
            swim.HoldBreathTime = Fixed64.One;
            swim.UnderwaterTimer = Fixed64.One + (Fixed64)2;

            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            swim.IsDrowning.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutDiving_When_MovesUp_Then_ShouldSwimUpward()
        {
            var initialPosition = new Vector3d(0, -2, 0);
            var agent = MockAgentTestFactory.CreateMockAgent(startPosition: initialPosition, startingMedium: TraversalMedium.Water);
            agent.Motor.Locomotions.Swim.IsSwimming = true;

            for (int i = 0; i < 10; i++) // Simulate swimming upwards
            {
                TrailblazerManager.Simulate();
                agent.SetTravelRequest(direction: Vector3d.Up, rate: TrekRate.Slow);
                agent.Simulate();
                agent.Visualize();
            }

            agent.Position.y.Should().BeGreaterThan(initialPosition.y); // Should rise
        }

        [Fact]
        public void Given_ScoutUnderwater_When_OutOfBreath_Then_ShouldTriggerDrowning()
        {
            var agent = MockAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(0, -5, 0), startingMedium: TraversalMedium.Water);

            agent.Motor.Locomotions.Swim.HoldBreathTime = (Fixed64)3;
            agent.Motor.Locomotions.Swim.CanDrown = true;

            for (int i = 0; i < 100; i++) // Simulate prolonged underwater time
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
            }

            agent.Motor.Locomotions.Swim.IsDrowning.Should().BeTrue();
        }

        [Fact]
        public void Given_SwimmingScout_When_JumpRequested_Then_ShouldBreachWater()
        {
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
            agent.Motor.Locomotions.Swim.CanBreachWater = true;

            bool breached = false;
            agent.Motor.Events.OnStartWaterBreach += () => breached = true;

            // Request a jump while swimming
            agent.SetTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            agent.Motor.Locomotions.Jump.IsJumping.Should().BeTrue();
            breached.Should().BeTrue();
            agent.Motor.Locomotions.Move.Velocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_SwimmingScout_When_JumpRequestedButBreachDisabled_Then_ShouldNotJump()
        {
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
            agent.Motor.Locomotions.Swim.CanBreachWater = false;

            bool breached = false;
            agent.Motor.Events.OnStartWaterBreach += () => breached = true;

            // Request a jump while swimming, but breach is disabled
            agent.SetTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            agent.Motor.Locomotions.Jump.IsJumping.Should().BeFalse();
            breached.Should().BeFalse();
            agent.Motor.Locomotions.Move.Velocity.y.Should().BeLessThanOrEqualTo(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutBreachesWater_When_ExitsWater_Then_ShouldStopSwimmingAndTriggerStopBreach()
        {
            var agent = MockAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
            agent.Motor.Locomotions.Swim.CanBreachWater = true;

            bool stopBreach = false;
            agent.Motor.Events.OnStopWaterBreach += () => stopBreach = true;

            // Simulate a jump breach
            agent.SetTravelRequest(direction: Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            agent.Simulate();
            agent.Visualize();

            for (int i = 0; i < 32; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
                agent.Visualize();
                if (agent.SurfaceState.Medium == TraversalMedium.Water)
                    break;
            }

            agent.Motor.IsInWater.Should().BeTrue();
            agent.Motor.Locomotions.Swim.IsSwimming.Should().BeTrue();
            stopBreach.Should().BeTrue();
        }
    }
}
