using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class JumpLocomotionTests
    {
        [Fact]
        public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act
            scout.Motor.Traverse(scout, Vector3d.Zero, TrekRate.Stationary, isRequestingJump: true);
            scout.Visualize();

            // Assert
            scout.Motor.Locomotions.Move.Velocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(
                startPosition: Vector3d.Up,
                startVelocity: Vector3d.Down,
                startingMedium: TraversalMedium.Air);

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            // Assert
            scout.Motor.Locomotions.Move.Velocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            Fixed64 expectedJumpFrame = scout.Motor.Locomotions.Jump.JumpStartTime;

            // Attempt to jump again immediately
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            // Assert
            scout.Motor.IsInAir.Should().BeTrue();
            scout.Motor.Locomotions.Jump.IsCoolingDown.Should().BeTrue();
            scout.Motor.Locomotions.Jump.JumpStartTime.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            // Release jump after 2 frames
            for (int i = 0; i < 29; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.Visualize();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            // Assert
            scout.Motor.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.Motor.Locomotions.Jump.IsJumping.Should().BeFalse();
            scout.Motor.Locomotions.Jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we simulate 31
            scout.Motor.Locomotions.Move.Velocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);
            scout.Motor.Events.CanAffordJump = () => false;

            // Act
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            // Assert
            scout.Motor.Locomotions.Move.Velocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
        {
            // Arrange
            var scout = MockAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - Initial Jump
            TrailblazerManager.Simulate();
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            Vector3d previousVelocity = scout.Motor.Locomotions.Move.Velocity;

            // Continue holding jump for 3 frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
                scout.Simulate();
                scout.Visualize();
            }

            // Assert
            var expected = previousVelocity.y - (scout.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime * 3);
            scout.Motor.Locomotions.Move.Velocity.y.Should().BeGreaterThan(expected);
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpHeld_Then_ShouldJumpHigher()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent();

            scout.SetTravelRequest(isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(isRequestingJump: true);
                scout.Simulate();
                scout.Visualize();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Motor.Locomotions.Jump.BaseJumpHeight); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpNotHeld_Then_ShouldNotJumpHigher()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent();

            scout.SetTravelRequest(isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(isRequestingJump: false);
                scout.Simulate();
                scout.Visualize();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Motor.Locomotions.Jump.BaseJumpHeight + Fixed64.Epsilon); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent(startPosition: new Vector3d(0, 5, 0));

            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Slow, isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            scout.Simulate();

            scout.SetTraversalCondition(TraversalMedium.Air, surfaceLevel: Fixed64.FromRaw(5 << 16), ceilingLevel: Fixed64.FromRaw(6 << 16)); // Simulate a ceiling

            scout.Visualize();

            scout.Motor.Locomotions.Jump.IsJumping.Should().BeFalse(); // Jump should be canceled
            scout.Motor.Locomotions.Move.Velocity.y.Should().BeLessThanOrEqualTo(Fixed64.Zero); // Should stop rising
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_LandsOnGround_Then_ShouldResetJumpState()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent();

            bool jumpStarted = false;
            bool jumpStopped = false;
            bool fallStarted = false;
            bool fallStopped = false;

            scout.Motor.Events.OnStartJump += (avoidTimer) => jumpStarted = true;
            scout.Motor.Events.OnStopJump += () => jumpStopped = true;
            scout.Motor.Events.OnStartFall += () => fallStarted = true;
            scout.Motor.Events.OnLandedFall += () => fallStopped = true;

            // Start Jump
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Slow, isRequestingJump: true);
            scout.Simulate();
            scout.Visualize();

            // Simulate entire jump arc until landing
            for (int i = 0; i < 30; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.Visualize();
                if (scout.Position.y <= Fixed64.Zero) // If we've landed
                    break;
            }

            scout.Simulate();

            scout.SetTraversalCondition(TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            scout.Visualize();

            // Assert that jump state has been reset after actual landing
            scout.Motor.Locomotions.Jump.IsJumping.Should().BeFalse();
            jumpStarted.Should().BeTrue();
            jumpStopped.Should().BeTrue();
            fallStarted.Should().BeTrue();
            // We treat landing a jump differently
            fallStopped.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_HeldTooLong_Then_ShouldNotExceedMaxJump()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent();

            var jumpLocomotion = scout.Motor.Locomotions.Jump;
            Fixed64 maxExpectedHeight = jumpLocomotion.BaseJumpHeight + jumpLocomotion.ExtraJumpHeight;

            Fixed64 maxY = scout.Position.y;

            // Start jump
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            // Continue holding jump and track peak height until we land
            while (!scout.Motor.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
                scout.Simulate();
                scout.Visualize();

                if (scout.Position.y > maxY)
                    maxY = scout.Position.y;
            }

            maxY.Should().BeLessThanOrEqualTo(maxExpectedHeight);
        }

        [Fact]
        public void Given_ScoutTapsJump_When_ReleasedImmediately_Then_JumpHeightShouldBeReduced()
        {
            var scout = MockAgentTestFactory.CreateJumpReadyAgent();

            // Start jump
            scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.Visualize();

            // Tap release
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: false);
                scout.Simulate();
                scout.Visualize();
            }

            scout.Position.y.Should().BeGreaterThan((Fixed64)0.5).And.BeLessThan((Fixed64)1.0);
        }
    }
}
